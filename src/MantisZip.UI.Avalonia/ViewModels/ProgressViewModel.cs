using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the progress window — shared between compress and extract operations.
/// Uses CommunityToolkit.Mvvm for observable properties and relay commands.
/// </summary>
public partial class ProgressViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _pauseEvent = new(initialState: true);
    private ObservableCollection<BatchItem>? _batchItems;
    private bool _isBatchMode;
    private int _currentBatchIndex = -1;
    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(100);

    // ── T6: 显示模式 / 速度 / 时间 / 冲突统计 / 并行批次 ──

    /// <summary>速度/ETA 追踪器（EMA 平滑；档案切换时 OnArchiveSwitch 归零重起）。</summary>
    private readonly ProgressSpeedTracker _speedTracker = new();

    /// <summary>本次操作开始时间（UTC；ctor/InitBatchMode 重置，SetProgress 首报兜底）。</summary>
    private DateTime _opStartUtc;

    /// <summary>已处理文件数（引擎上报；兼容重载 TotalFiles=0 时不清零）。</summary>
    private long _statsProcessed;
    private long _statsSkipped;
    private long _statsFailed;
    private long _statsOverwritten;

    /// <summary>是否出现过冲突统计（压缩路径恒不上报 → 统计项按 Rule 6 隐藏）。</summary>
    private bool _hasConflictStats;

    /// <summary>当前文件所在目录（SplitFilePath 分离结果，路径行绑定）。</summary>
    private string _dirName = string.Empty;

    /// <summary>ZIP 并行解压批次行（BatchIndex 驱动 upsert；非并行恒为空 → 容器隐藏）。</summary>
    private readonly ObservableCollection<ParallelBatchProgressItem> _parallelBatchItems = new();

    /// <summary>顶部内容模式（默认 Simple；详见 <see cref="ContentMode"/>）。</summary>
    private ProgressContentMode _contentMode = ProgressContentMode.Simple;

    /// <summary>信息量分级（默认 Medium；详见 <see cref="InfoDensity"/>）。</summary>
    private ProgressInfoDensity _infoDensity = ProgressInfoDensity.Medium;

    /// <summary>逐条目行集合（列表模式数据源；播种/引擎终态事件驱动）。</summary>
    private readonly ObservableCollection<EntryProgressItem> _entryItems = new();

    /// <summary>
    /// EntryKey → 条目行索引（<see cref="UpdateEntryStatus"/> O(1) upsert）。
    /// 必须与 <see cref="_entryItems"/> 同步维护（Seed/Clear 入口统一维护）。
    /// 100k 条目场景：每条目恰好一条终态事件 → 线性扫描总计 O(n²)≈10^10 次字符串比较，
    /// 字典将单次更新降为 O(1)、总体 O(n)，故选字典而非线性扫描。
    /// </summary>
    private readonly Dictionary<string, EntryProgressItem> _entryIndex = new(StringComparer.Ordinal);

    /// <summary>并行解压线程度（引擎实际生效值；null = 未接线/批次已重置）。</summary>
    private int? _parallelDegree;

    /// <summary>
    /// Localized strings bound by the ProgressWindow UI.
    /// </summary>
    public Dictionary<string, string> LocalizedStrings { get; }

    public ProgressViewModel()
    {
        LocalizedStrings = new Dictionary<string, string>
        {
            ["Progress_Cancel"] = LocalizationManager.T("Progress_Cancel"),
            ["Progress_Complete"] = LocalizationManager.T("Progress_Complete"),
            ["Progress_Cancelling"] = LocalizationManager.T("Progress_Cancelling"),
            ["Progress_Button_Pause"] = LocalizationManager.T("Progress_Button_Pause"),
            ["Progress_Button_Resume"] = LocalizationManager.T("Progress_Button_Resume"),
            ["Progress_Button_Close"] = LocalizationManager.T("Progress_Button_Close"),
            ["Progress_KeepOpen"] = LocalizationManager.T("Progress_KeepOpen"),
            ["Progress_Paused"] = LocalizationManager.T("Progress_Paused"),
            ["Progress_Resuming"] = LocalizationManager.T("Progress_Resuming"),
            ["MsgBox_Cancel"] = LocalizationManager.T("MsgBox_Cancel"),
            // T6 新增 key（统计/时间/并行批次/密码徽标——此字典由显式数组构建，漏登记 = XAML 绑定空白）
            ["Progress_Stats_Processed"] = LocalizationManager.T("Progress_Stats_Processed"),
            ["Progress_Stats_Skipped"] = LocalizationManager.T("Progress_Stats_Skipped"),
            ["Progress_Stats_Failed"] = LocalizationManager.T("Progress_Stats_Failed"),
            ["Progress_Stats_Overwritten"] = LocalizationManager.T("Progress_Stats_Overwritten"),
            ["Progress_Stats_Speed"] = LocalizationManager.T("Progress_Stats_Speed"),
            ["Progress_Time_Elapsed"] = LocalizationManager.T("Progress_Time_Elapsed"),
            ["Progress_Time_Remaining"] = LocalizationManager.T("Progress_Time_Remaining"),
            ["Progress_CurrentFileLabel"] = LocalizationManager.T("Progress_CurrentFileLabel"),
            ["Progress_Batch_ArchiveOf"] = LocalizationManager.T("Progress_Batch_ArchiveOf"),
            ["Progress_Batch_FilesProgress"] = LocalizationManager.T("Progress_Batch_FilesProgress"),
            ["Progress_Batch_Pwd_Matching"] = LocalizationManager.T("Progress_Batch_Pwd_Matching"),
            ["Progress_Batch_Pwd_MatchedTip"] = LocalizationManager.T("Progress_Batch_Pwd_MatchedTip"),
            ["Progress_Batch_Pwd_Rule"] = LocalizationManager.T("Progress_Batch_Pwd_Rule"),
            ["Progress_Batch_Pwd_Desc"] = LocalizationManager.T("Progress_Batch_Pwd_Desc"),
            // T7 新增 key（顶部内容模式/信息量分级 + 并行统计/批次/条目/Toast——
            // 此字典由显式数组构建，漏登记 = XAML 绑定空白且构建不报错；
            // 文案值由后续任务落 JSON，此处先登记 T() 占位调用）
            ["Progress_Mode_Label"] = LocalizationManager.T("Progress_Mode_Label"),
            ["Progress_Mode_Simple"] = LocalizationManager.T("Progress_Mode_Simple"),
            ["Progress_Mode_Detailed"] = LocalizationManager.T("Progress_Mode_Detailed"),
            ["Progress_Mode_List"] = LocalizationManager.T("Progress_Mode_List"),
            ["Progress_Density_Minimal"] = LocalizationManager.T("Progress_Density_Minimal"),
            ["Progress_Density_Medium"] = LocalizationManager.T("Progress_Density_Medium"),
            ["Progress_Density_Full"] = LocalizationManager.T("Progress_Density_Full"),
            ["Progress_Stats_Parallel"] = LocalizationManager.T("Progress_Stats_Parallel"),
            ["Progress_Batch_SectionTitle"] = LocalizationManager.T("Progress_Batch_SectionTitle"),
            ["Progress_Batch_Count"] = LocalizationManager.T("Progress_Batch_Count"),
            ["Progress_Entry_Pending"] = LocalizationManager.T("Progress_Entry_Pending"),
            ["Progress_Entry_Active"] = LocalizationManager.T("Progress_Entry_Active"),
            // T9 新增 key（条目行终态文案）——Progress_Stats_* 是带 {0} 计数的格式串，
            // 行内状态无计数可代，故独立成键；文案值由后续任务落 JSON，此处先登记 T() 占位调用
            ["Progress_Entry_Completed"] = LocalizationManager.T("Progress_Entry_Completed"),
            ["Progress_Entry_Skipped"] = LocalizationManager.T("Progress_Entry_Skipped"),
            ["Progress_Entry_Failed"] = LocalizationManager.T("Progress_Entry_Failed"),
            ["Progress_Entry_Overwritten"] = LocalizationManager.T("Progress_Entry_Overwritten"),
            ["Progress_Toast_Copied"] = LocalizationManager.T("Progress_Toast_Copied"),
            ["Progress_Batch_Label"] = LocalizationManager.T("Progress_Batch_Label"),
        };

        // T6: 操作计时基线 + 当前文件标签初值 + 集合变更通知（并行批次行 / 条目行）
        _opStartUtc = DateTime.UtcNow;
        CurrentFileLabel = LocalizationManager.T("Progress_CurrentFileLabel");
        _parallelBatchItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasParallelBatches));
            OnPropertyChanged(nameof(IsDetailedAvailable));
        };
        _entryItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEntryItems));
        RefreshTimeDisplay();
    }

    // ════════════════════════════════════════════
    //  Observable Properties
    // ════════════════════════════════════════════

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private int _percentComplete;

    [ObservableProperty]
    private int _filePercentComplete;

    [ObservableProperty]
    private string? _fileName;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _hasErrors;

    [ObservableProperty]
    private string? _errorSummaryText;

    [ObservableProperty]
    private string? _fileCountText;

    [ObservableProperty]
    private bool _isCancelEnabled = true;

    /// <summary>Is the keep-open toggle currently checked (📌 pinned open).</summary>
    [ObservableProperty]
    private bool _keepOpenOnComplete;

    // ════════════════════════════════════════════
    //  T6: 显示模式 / 统计 / 时间 / 速度 / 并行批次
    // ════════════════════════════════════════════

    /// <summary>顶部内容模式（v6 原型三模式：Simple/Detailed/List；默认 Simple）。</summary>
    public ProgressContentMode ContentMode
    {
        get => _contentMode;
        set { if (SetProperty(ref _contentMode, value)) NotifyDisplayProperties(); }
    }

    /// <summary>信息量分级（v6 原型三档：Minimal/Medium/Full，控制「显示多少信息」非间距；默认 Medium）。</summary>
    public ProgressInfoDensity InfoDensity
    {
        get => _infoDensity;
        set { if (SetProperty(ref _infoDensity, value)) NotifyDisplayProperties(); }
    }

    /// <summary>简明模式（默认档）。</summary>
    public bool IsSimpleMode => _contentMode == ProgressContentMode.Simple;

    /// <summary>详细模式（显示并行批次详细行）。</summary>
    public bool IsDetailedMode => _contentMode == ProgressContentMode.Detailed;

    /// <summary>列表模式（逐条目行）。</summary>
    public bool IsListMode => _contentMode == ProgressContentMode.List;

    /// <summary>信息量「少」档：仅总进度 + 时间（统计卡/中等单行均隐藏）。</summary>
    public bool IsMinimalDensity => _infoDensity == ProgressInfoDensity.Minimal;

    /// <summary>信息量「中」档：追加中等单行（已处理 + 速度 + 并行）。</summary>
    public bool IsMediumDensity => _infoDensity == ProgressInfoDensity.Medium;

    /// <summary>信息量「完整」档：追加统计卡（与中等单行互斥，对齐原型 JS 语义）。</summary>
    public bool IsFullDensity => _infoDensity == ProgressInfoDensity.Full;

    /// <summary>详细模式可用：并行批次行非空（CollectionChanged 驱动通知；空列表不允许停留 Detailed）。</summary>
    public bool IsDetailedAvailable => _parallelBatchItems.Count > 0;

    /// <summary>当前文件所在目录（路径行绑定）。</summary>
    public string DirName
    {
        get => _dirName;
        set => SetProperty(ref _dirName, value);
    }

    /// <summary>统计栏：是否显示跳过/出错/已覆盖（引擎上报过冲突统计才为 true；压缩恒 false）。</summary>
    public bool HasConflictStats => _hasConflictStats;

    /// <summary>并行批次容器可见性（集合非空时 true；CollectionChanged 驱动通知）。</summary>
    public bool HasParallelBatches => _parallelBatchItems.Count > 0;

    /// <summary>并行批次行集合（ZIP 并行解压时由 BatchIndex 驱动 upsert）。</summary>
    public ObservableCollection<ParallelBatchProgressItem> ParallelBatchItems => _parallelBatchItems;

    /// <summary>逐条目行集合（列表模式数据源；SeedEntryItems 播种 / UpdateEntryStatus upsert）。</summary>
    public ObservableCollection<EntryProgressItem> EntryItems => _entryItems;

    /// <summary>条目行容器可见性（集合非空时 true；CollectionChanged 驱动通知）。</summary>
    public bool HasEntryItems => _entryItems.Count > 0;

    /// <summary>并行解压线程度（ExtractFlow 接线；null = 未接线或新批次已重置）。</summary>
    public int? ParallelDegree
    {
        get => _parallelDegree;
        set { if (SetProperty(ref _parallelDegree, value)) NotifyDisplayProperties(); }
    }

    /// <summary>并行统计显示开关：仅并行度 ≥ 2 时显示（degree=1 串行路径自动隐藏，Rule 6）。</summary>
    public bool HasParallelDegree => _parallelDegree is >= 2;

    /// <summary>统计栏：已处理。</summary>
    [ObservableProperty]
    private string _statsProcessedText = string.Empty;

    /// <summary>统计栏：跳过。</summary>
    [ObservableProperty]
    private string _statsSkippedText = string.Empty;

    /// <summary>统计栏：出错。</summary>
    [ObservableProperty]
    private string _statsFailedText = string.Empty;

    /// <summary>统计栏：已覆盖。</summary>
    [ObservableProperty]
    private string _statsOverwrittenText = string.Empty;

    /// <summary>速度文案（如 "12.3 MB/s"）。</summary>
    [ObservableProperty]
    private string _speedText = string.Empty;

    /// <summary>已用时间文案。</summary>
    [ObservableProperty]
    private string _elapsedText = string.Empty;

    /// <summary>剩余时间文案（ETA 无效时为空）。</summary>
    [ObservableProperty]
    private string _remainingText = string.Empty;

    /// <summary>「当前文件」标签（本地化，ctor 初始化）。</summary>
    [ObservableProperty]
    private string _currentFileLabel = string.Empty;

    /// <summary>批次档案序号（如 "2 / 5"；非批次为空）。</summary>
    [ObservableProperty]
    private string _batchArchiveIndexText = string.Empty;

    /// <summary>派生显示属性集中通知（AGENTS.md 派生属性通知模式，禁逐字段 NotifyPropertyChangedFor）。
    /// 新增派生属性只改这一处。</summary>
    private void NotifyDisplayProperties()
    {
        OnPropertyChanged(nameof(IsSimpleMode));
        OnPropertyChanged(nameof(IsDetailedMode));
        OnPropertyChanged(nameof(IsListMode));
        OnPropertyChanged(nameof(IsDetailedAvailable));
        OnPropertyChanged(nameof(HasEntryItems));
        OnPropertyChanged(nameof(HasParallelDegree));
        OnPropertyChanged(nameof(IsMinimalDensity));
        OnPropertyChanged(nameof(IsMediumDensity));
        OnPropertyChanged(nameof(IsFullDensity));
    }

    // ════════════════════════════════════════════
    //  T7: 统计卡（短标签 + 计数）与批处理列表计数
    // ════════════════════════════════════════════

    /// <summary>统计卡短标签：从既有格式化文案剥离 {0} 占位符（"已处理 {0}" → "已处理"）。
    /// 不新增本地化 key（三语文案由 T11 统一落地），也不随属性变更通知（语言切换刷新为既有已知缺陷）。</summary>
    public string StatsProcessedLabel => LocalizationManager.T("Progress_Stats_Processed", string.Empty).Trim();

    /// <summary>统计卡短标签（跳过）。</summary>
    public string StatsSkippedLabel => LocalizationManager.T("Progress_Stats_Skipped", string.Empty).Trim();

    /// <summary>统计卡短标签（出错）。</summary>
    public string StatsFailedLabel => LocalizationManager.T("Progress_Stats_Failed", string.Empty).Trim();

    /// <summary>统计卡短标签（已覆盖）。</summary>
    public string StatsOverwrittenLabel => LocalizationManager.T("Progress_Stats_Overwritten", string.Empty).Trim();

    /// <summary>统计卡数值（仅计数；完整档卡片的 value 槽，label 槽用上方短标签）。</summary>
    public long StatsProcessedCount => _statsProcessed;

    /// <summary>统计卡数值（跳过）。</summary>
    public long StatsSkippedCount => _statsSkipped;

    /// <summary>统计卡数值（出错）。</summary>
    public long StatsFailedCount => _statsFailed;

    /// <summary>统计卡数值（已覆盖）。</summary>
    public long StatsOverwrittenCount => _statsOverwritten;

    /// <summary>统计卡数值派生属性集中通知（AGENTS.md 派生属性通知模式，禁逐字段 NotifyPropertyChangedFor）。
    /// 引擎统计上报处调用一次即刷新全部 4 张卡的数值。</summary>
    private void NotifyStatsProperties()
    {
        OnPropertyChanged(nameof(StatsProcessedCount));
        OnPropertyChanged(nameof(StatsSkippedCount));
        OnPropertyChanged(nameof(StatsFailedCount));
        OnPropertyChanged(nameof(StatsOverwrittenCount));
    }

    /// <summary>批处理列表是否至少有一项（Row 0 计数文案的显隐守卫；无批次时隐藏计数而非整区，列表区始终显示）。</summary>
    public bool HasBatchItems => _batchItems is { Count: > 0 };

    /// <summary>批处理列表计数文案（如 "3 个文件"；无批次时为空串）。</summary>
    public string BatchCountText => _batchItems is null
        ? string.Empty
        : LocalizationManager.T("Progress_Batch_Count", _batchItems.Count);

    /// <summary>批处理相关派生属性集中通知（AGENTS.md 派生属性通知模式）：新增派生属性只改这一处。</summary>
    private void NotifyBatchProperties()
    {
        OnPropertyChanged(nameof(BatchItems));
        OnPropertyChanged(nameof(IsBatchMode));
        OnPropertyChanged(nameof(HasBatchItems));
        OnPropertyChanged(nameof(BatchCountText));
    }

    // ════════════════════════════════════════════
    //  Batch Mode Properties
    // ════════════════════════════════════════════

    public ObservableCollection<BatchItem>? BatchItems => _batchItems;

    public bool IsBatchMode => _isBatchMode;

    /// <summary>Pause event. Set = running, Reset = paused.</summary>
    public ManualResetEventSlim PauseEvent => _pauseEvent;

    /// <summary>Batch has at least one failed item.</summary>
    public bool HasFailures => _batchItems?.Any(i => i.Status == BatchItemStatus.Failed) ?? false;

    /// <summary>Cancellation token from the internal CTS.</summary>
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    // ════════════════════════════════════════════
    //  Events (for code-behind coordination)
    // ════════════════════════════════════════════

    /// <summary>Raised when the cancel button is clicked and batch mode should close the window.</summary>
    public event Action? RequestClose;

    // ════════════════════════════════════════════
    //  Commands
    // ════════════════════════════════════════════

    /// <summary>
    /// Cancel the current operation. Called by the Cancel button.
    /// Always requests window close (matches WPF CancelButton_Click), regardless of batch mode.
    /// The window close is a no-op when the caller already owns the close (e.g. RunWithProgress's finally).
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        CancelOperation();
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Cancel the underlying operation without requesting a window close.
    /// Used by the window's X button (OnClosing): the window is already closing,
    /// so only the cancellation token needs to be triggered.
    /// </summary>
    public void CancelOperation()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Toggle pause/resume state.
    /// </summary>
    [RelayCommand]
    private void TogglePause()
    {
        if (_pauseEvent.IsSet)
        {
            // Running → pause
            _pauseEvent.Reset();
            IsPaused = true;
            StatusMessage = LocalizationManager.T("Progress_Paused");
        }
        else
        {
            // Paused → resume
            _pauseEvent.Set();
            IsPaused = false;
            StatusMessage = LocalizationManager.T("Progress_Resuming");
        }
    }

    // ════════════════════════════════════════════
    //  Public Methods
    // ════════════════════════════════════════════

    /// <summary>
    /// Initialize the cancellation token source.
    /// Must be called before any cancellable operation starts.
    /// </summary>
    public void InitCancellation()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Update all progress-bound properties from an <see cref="ArchiveProgress"/> report.
    /// Safe to call from any thread; dispatches internally.
    /// </summary>
    public void SetProgress(ArchiveProgress p)
    {
        // T6: 逐条目终态事件 = 独立通道，置于一切计数/节流逻辑之前，
        // 不参与百分比/速度/ETA/字节/批次计算（早返回，绝不落入下方分支）
        if (p.EntryStatus.HasValue && !string.IsNullOrEmpty(p.EntryKey))
        {
            UpdateEntryStatus(p.EntryKey,
                EntryProgressItem.MapEntryStatus(p.EntryStatus.Value), null);
            return;
        }

        // 操作计时基线兜底（ctor/InitBatchMode 已重置，此处防漏）
        if (_opStartUtc == default)
            _opStartUtc = DateTime.UtcNow;

        // 路径/文件名分离（剥引擎前缀 + '/' 拆分 → 目录行/文件名行两行显示）
        if (!string.IsNullOrEmpty(p.CurrentFile))
        {
            var (dir, name) = ProgressDisplayCalculator.SplitFilePath(p.CurrentFile);
            DirName = dir;
            FileName = name;

            // T6: 当前文件 → Active 行推导（列表模式 ⏳n%）：
            // 按 Path.GetFileName 匹配条目行，同一时刻至多一行 Active
            ActivateEntryByName(System.IO.Path.GetFileName(p.CurrentFile), p.FilePercentComplete);
        }

        // 总进度：批次模式用加权公式（与旧实现数学等价），非批次 = 引擎百分比
        if (_isBatchMode && _batchItems != null && _batchItems.Count > 1)
        {
            double overall = ProgressDisplayCalculator.ComputeOverallPercent(
                Math.Max(_currentBatchIndex, 0), p.PercentComplete, _batchItems.Count);
            PercentComplete = (int)Math.Clamp(overall, 0, 100);
        }
        else
        {
            PercentComplete = (int)Math.Clamp(p.PercentComplete, 0, 100);
        }
        if (p.FilePercentComplete.HasValue)
            FilePercentComplete = (int)Math.Clamp(p.FilePercentComplete.Value, 0, 100);

        // File count + 批次档案序号（"2 / 5"）
        if (_isBatchMode && _batchItems != null && _batchItems.Count > 0)
        {
            int current = _currentBatchIndex >= 0
                ? Math.Min(_currentBatchIndex + 1, _batchItems.Count)
                : Math.Min((int)p.PercentComplete / 100 * _batchItems.Count, _batchItems.Count);
            if (current < 1) current = 1;
            FileCountText = LocalizationManager.T("Progress_FileCount", current, _batchItems.Count);
            BatchArchiveIndexText = LocalizationManager.T("Progress_Batch_ArchiveOf", current, _batchItems.Count);
        }
        else
        {
            FileCountText = LocalizationManager.T("Progress_FileCount", 1, 1);
            BatchArchiveIndexText = string.Empty;
        }

        // 速度采样（EMA 平滑；tracker 内置 100ms 节流）→ 速度文案
        double speed = _speedTracker.RecordSample(p.ProcessedBytes, DateTime.UtcNow);
        if (speed > 0)
            SpeedText = LocalizationManager.T("Progress_Stats_Speed", FormatUtil.FormatSize((long)speed));

        // ETA → 剩余时间（速度无效/总字节未知时清空）
        double? eta = _speedTracker.ComputeEtaSeconds(
            _speedTracker.ProcessedInArchive(p.ProcessedBytes), p.TotalBytes);
        RemainingText = eta.HasValue
            ? LocalizationManager.T("Progress_Time_Remaining",
                ProgressDisplayCalculator.FormatDuration(TimeSpan.FromSeconds(eta.Value)))
            : string.Empty;

        // 冲突统计（压缩路径恒不上报 → HasConflictStats 保持 false，统计项按 Rule 6 隐藏）
        if (p.SkippedFiles.HasValue || p.FailedFiles.HasValue || p.OverwrittenFiles.HasValue)
        {
            if (!_hasConflictStats)
            {
                _hasConflictStats = true;
                OnPropertyChanged(nameof(HasConflictStats));
            }
        }
        if (p.SkippedFiles.HasValue)
        {
            _statsSkipped = p.SkippedFiles.Value;
            StatsSkippedText = LocalizationManager.T("Progress_Stats_Skipped", _statsSkipped);
        }
        if (p.FailedFiles.HasValue)
        {
            _statsFailed = p.FailedFiles.Value;
            StatsFailedText = LocalizationManager.T("Progress_Stats_Failed", _statsFailed);
        }
        if (p.OverwrittenFiles.HasValue)
        {
            _statsOverwritten = p.OverwrittenFiles.Value;
            StatsOverwrittenText = LocalizationManager.T("Progress_Stats_Overwritten", _statsOverwritten);
        }
        // 已处理：仅引擎上报 TotalFiles 时更新（兼容重载 TotalFiles=0 不得清零）
        if (p.TotalFiles > 0)
        {
            _statsProcessed = p.ProcessedFiles;
            StatsProcessedText = LocalizationManager.T("Progress_Stats_Processed", _statsProcessed);
        }

        // 统计卡数值集中刷新（T7：4 张卡的计数槽位）
        NotifyStatsProperties();

        // 批次模式：行级实时统计 + 当前行进度（100ms 节流）
        if (_isBatchMode && _currentBatchIndex >= 0 && _batchItems != null &&
            _currentBatchIndex < _batchItems.Count)
        {
            UpdateBatchItemStats(p);

            var now = DateTime.UtcNow;
            if (p.PercentComplete >= 100 || p.PercentComplete <= 0 ||
                (now - _lastProgressUpdate) >= ProgressThrottle)
            {
                _batchItems[_currentBatchIndex].Progress = p.PercentComplete;
                _lastProgressUpdate = now;
            }

            // 终值兜底：末档案不触发 SetCurrentBatchItem 切换，>=100 立即写行统计+摘要
            if (p.PercentComplete >= 100)
            {
                var row = _batchItems[_currentBatchIndex];
                if (row.TotalFiles > 0)
                    row.SummaryText = BuildStatsSummaryText(
                        row.ProcessedFiles, row.SkippedFiles, row.FailedFiles, row.OverwrittenFiles);
            }
        }

        // 并行批次行 upsert（BatchIndex 上报时驱动；未上报不动集合，避免非并行清空闪烁）
        if (p.BatchIndex.HasValue)
            UpsertParallelBatch(p);
    }

    /// <summary>把当前档案的统计写入当前批次行（行级实时统计；SetProgress 批次分支调用）。</summary>
    private void UpdateBatchItemStats(ArchiveProgress p)
    {
        if (_batchItems == null || _currentBatchIndex < 0 || _currentBatchIndex >= _batchItems.Count)
            return;
        var row = _batchItems[_currentBatchIndex];
        if (p.SkippedFiles.HasValue) row.SkippedFiles = p.SkippedFiles.Value;
        if (p.FailedFiles.HasValue) row.FailedFiles = p.FailedFiles.Value;
        if (p.OverwrittenFiles.HasValue) row.OverwrittenFiles = p.OverwrittenFiles.Value;
        if (p.TotalFiles > 0)
        {
            row.TotalFiles = p.TotalFiles;
            row.ProcessedFiles = p.ProcessedFiles;
        }
    }

    /// <summary>并行批次行 upsert（BatchIndex 0-based → Index=BatchIndex+1；补洞创建，Percent/DetailText 消费批级字段）。</summary>
    private void UpsertParallelBatch(ArchiveProgress p)
    {
        int idx = p.BatchIndex!.Value;
        while (_parallelBatchItems.Count <= idx)
            _parallelBatchItems.Add(new ParallelBatchProgressItem { Index = _parallelBatchItems.Count + 1 });

        var row = _parallelBatchItems[idx];
        row.Percent = Math.Clamp(p.BatchPercentComplete ?? p.PercentComplete, 0, 100);
        // T6: 详细模式行显示批次当前文件名（未上报时保持上次值）
        row.CurrentFile = p.CurrentFile ?? row.CurrentFile;
        // T7: 状态色随完成度切换（BrushResourceConverter 消费 StatusBrushName → 批次进度条前景）
        row.StatusBrushName = row.Percent >= 100 ? "ThemeStatusSuccessBrush" : "ThemeProgressFillBrush";
        if (p.BatchProcessedFiles.HasValue && p.BatchTotalFiles.HasValue)
            row.DetailText = LocalizationManager.T("Progress_Batch_FilesProgress",
                p.BatchProcessedFiles.Value, p.BatchTotalFiles.Value);
    }

    // ════════════════════════════════════════════
    //  T6: 逐条目行（EntryItems）播种 / upsert / Active 推导
    // ════════════════════════════════════════════

    /// <summary>
    /// 播种条目行（D7）：清空后按传入顺序建行，全部落 <see cref="EntryRowState.Pending"/>。
    /// 未播种的路径由 <see cref="UpdateEntryStatus"/> 的 upsert 兜底（渐进模式）。
    /// </summary>
    public void SeedEntryItems(IReadOnlyList<(string Key, string Name, long Size)> items)
    {
        ClearEntryItems();
        foreach (var (key, name, size) in items)
        {
            var row = new EntryProgressItem
            {
                EntryKey = key,
                Name = name,
                Size = size,
                State = EntryRowState.Pending,
                // T9: 播种行的等待文案（本地化；Rule 13 禁止硬编码）
                StatusText = LocalizationManager.T("Progress_Entry_Pending"),
            };
            _entryItems.Add(row);
            _entryIndex[key] = row;
        }
    }

    /// <summary>清空条目行与 key 索引（新批次/新操作前调用，两者必须同步清）。</summary>
    public void ClearEntryItems()
    {
        _entryItems.Clear();
        _entryIndex.Clear();
    }

    /// <summary>
    /// 按 EntryKey upsert 条目行：命中则更新状态（及可选进度），未命中则新建行
    /// （未播种路径的兜底，兼容 TAR/GZ 渐进模式 D7）。
    /// 查找走 <c>_entryIndex</c> 字典 O(1)——100k 条目线性扫描为 O(n²)，不可接受。
    /// </summary>
    public void UpdateEntryStatus(string entryKey, EntryRowState state, double? percent)
    {
        if (!_entryIndex.TryGetValue(entryKey, out var row))
        {
            row = new EntryProgressItem
            {
                EntryKey = entryKey,
                Name = System.IO.Path.GetFileName(entryKey),
                State = EntryRowState.Pending,
            };
            _entryItems.Add(row);
            _entryIndex[entryKey] = row;
        }

        row.State = state;
        // T9: 行内状态文案（本地化）；Active 返回 null 不赋值——视图层改显 PercentText
        string? statusText = ResolveEntryStatusText(state);
        if (statusText != null)
            row.StatusText = statusText;
        if (percent.HasValue)
            row.Percent = percent.Value;
    }

    /// <summary>
    /// 行状态 → 已本地化的行内状态文案（Rule 13：禁止硬编码用户可见字符串）。
    /// <see cref="EntryRowState.Active"/> 返回 null：进行中行由视图层显示
    /// <see cref="EntryProgressItem.PercentText"/>，此处保留既有 <see cref="EntryProgressItem.StatusText"/> 不动。
    /// </summary>
    private static string? ResolveEntryStatusText(EntryRowState state) => state switch
    {
        EntryRowState.Pending   => LocalizationManager.T("Progress_Entry_Pending"),
        EntryRowState.Completed => LocalizationManager.T("Progress_Entry_Completed"),
        EntryRowState.Skipped   => LocalizationManager.T("Progress_Entry_Skipped"),
        EntryRowState.Failed    => LocalizationManager.T("Progress_Entry_Failed"),
        EntryRowState.Overwritten => LocalizationManager.T("Progress_Entry_Overwritten"),
        // Active（及未来新增成员）：不覆盖视图层选择的显示
        _ => null,
    };

    /// <summary>
    /// 「当前文件 → Active 行」推导（列表模式 ⏳n%）：按文件名匹配条目行并置 Active、写入当前文件百分比。
    /// 不变式：同一时刻至多一行 Active——原 Active 行若非本次命中，回落为 Completed（终态行不动）。
    /// </summary>
    private void ActivateEntryByName(string fileName, double? percent)
    {
        if (_entryItems.Count == 0 || string.IsNullOrEmpty(fileName))
            return;

        EntryProgressItem? target = null;
        EntryProgressItem? previousActive = null;
        foreach (var row in _entryItems)
        {
            if (target == null && string.Equals(row.Name, fileName, StringComparison.Ordinal))
                target = row;
            if (row.IsActive)
                previousActive = row;
        }

        if (target == null)
            return;
        if (previousActive != null && !ReferenceEquals(previousActive, target))
            previousActive.State = EntryRowState.Completed;
        target.State = EntryRowState.Active;
        if (percent.HasValue)
            target.Percent = percent.Value;
    }

    /// <summary>拼批次行摘要文案（已处理恒显示，跳过/出错/已覆盖 &gt;0 才追加；全走本地化）。</summary>
    private static string BuildStatsSummaryText(long processed, long skipped, long failed, long overwritten)
    {
        var parts = new List<string> { LocalizationManager.T("Progress_Stats_Processed", processed) };
        if (skipped > 0) parts.Add(LocalizationManager.T("Progress_Stats_Skipped", skipped));
        if (failed > 0) parts.Add(LocalizationManager.T("Progress_Stats_Failed", failed));
        if (overwritten > 0) parts.Add(LocalizationManager.T("Progress_Stats_Overwritten", overwritten));
        return string.Join(" ", parts);
    }

    /// <summary>刷新"已用时"文本（code-behind DispatcherTimer 每 1s 调用；未开始时跳过）。</summary>
    public void RefreshTimeDisplay()
    {
        if (_opStartUtc == default) return;
        ElapsedText = LocalizationManager.T("Progress_Time_Elapsed",
            ProgressDisplayCalculator.FormatDuration(DateTime.UtcNow - _opStartUtc));
    }

    /// <summary>
    /// Compatibility overload: set overall progress and current file name only.
    /// </summary>
    public void SetProgress(double percent, string currentFile)
    {
        SetProgress(new ArchiveProgress
        {
            PercentComplete = percent,
            CurrentFile = currentFile
        });
    }

    /// <summary>
    /// Mark the operation as complete.
    /// Sets all progress bars to 100% and changes cancel button to "Close".
    /// </summary>
    public void SetComplete(string message)
    {
        PercentComplete = 100;
        FilePercentComplete = 100;
        FileName = LocalizationManager.T("Progress_Done");
        // 完成后清空目录行，防止上一个档案的目录残留显示
        DirName = string.Empty;
        StatusMessage = message;
        IsIndeterminate = false;
    }

    // ════════════════════════════════════════════
    //  Batch Mode Methods
    // ════════════════════════════════════════════

    /// <summary>
    /// Initialize batch mode with the given file paths.
    /// </summary>
    public void InitBatchMode(System.Collections.Generic.IReadOnlyList<string> paths)
    {
        _isBatchMode = true;
        _currentBatchIndex = -1;
        _lastProgressUpdate = DateTime.MinValue;
        // T6: 重置计时基线与并行批次行（新批次从头计时；上一批次行不得残留）
        _opStartUtc = DateTime.UtcNow;
        _parallelBatchItems.Clear();
        // T6: 新批次不得继承上一批次的条目行与并行度
        ClearEntryItems();
        ParallelDegree = null;
        _batchItems = new ObservableCollection<BatchItem>(
            paths.Select(p => new BatchItem
            {
                Name = System.IO.Path.GetFileName(p),
                FullPath = p,
                Status = BatchItemStatus.Pending
            }));
        // 批次列表派生属性集中通知（BatchItems / IsBatchMode / HasBatchItems / BatchCountText）
        NotifyBatchProperties();
        // 注意：不在此处覆盖 WindowTitle —— 标题由调用方传入（非批处理操作也显示列表，标题不能只属于批处理）
    }
    /// <summary>
    /// Set the current batch item index as "in progress".
    /// Safe to call from any thread.
    /// </summary>
    public void SetCurrentBatchItem(int index)
    {
        if (_batchItems == null || index < 0 || index >= _batchItems.Count)
            return;

        // Complete previous item if still InProgress
        if (index > 0 && _batchItems[index - 1].Status == BatchItemStatus.InProgress)
        {
            var prev = _batchItems[index - 1];
            prev.Status = BatchItemStatus.Completed;
            prev.Progress = 100;
            // 上一档案最终统计写入行摘要（SetProgress 终值兜底已写过则保持，此处防漏）
            if (prev.TotalFiles > 0 && string.IsNullOrEmpty(prev.SummaryText))
                prev.SummaryText = BuildStatsSummaryText(
                    prev.ProcessedFiles, prev.SkippedFiles, prev.FailedFiles, prev.OverwrittenFiles);
        }

        // Only overwrite Pending items (don't overwrite Skipped/Completed/Failed)
        if (_batchItems[index].Status != BatchItemStatus.Pending)
            return;

        _currentBatchIndex = index;
        _batchItems[index].Status = BatchItemStatus.InProgress;
        _batchItems[index].Progress = 0;
        FileName = _batchItems[index].Name;
        // 切换压缩包时重置文件进度条，避免残留上一个包未置满的脏值
        FilePercentComplete = 0;
        // ETA 守卫：档案切换字节基线归零重起 EMA（上一档案累计字节对新档案无意义）
        _speedTracker.OnArchiveSwitch(0, DateTime.UtcNow);
        // 并行批次行随档案切换清空（新档案重新上报 BatchIndex）
        _parallelBatchItems.Clear();
        // T6: 归档切换后条目行必须为空（新档案的逐条目事件/播种重建行，绝不继承上一档案）
        ClearEntryItems();
        // D6 回落：详细模式列表已空时不允许停留（绝不显示空详细列表）
        if (_contentMode == ProgressContentMode.Detailed && _parallelBatchItems.Count == 0)
            ContentMode = ProgressContentMode.Simple;
        // 目录行残留清理（新档案的 DirName 由下次 SetProgress 填充）
        DirName = string.Empty;
    }

    /// <summary>
    /// Update the status of a specific batch item.
    /// Safe to call from any thread.
    /// </summary>
    public void UpdateBatchItemStatus(int index, BatchItemStatus status, string? errorMessage = null)
    {
        if (_batchItems == null || index < 0 || index >= _batchItems.Count)
            return;

        _batchItems[index].Status = status;
        if (status == BatchItemStatus.Failed)
            _batchItems[index].ErrorMessage = errorMessage;
        if (status is BatchItemStatus.Skipped or BatchItemStatus.Completed)
        {
            _batchItems[index].Progress = 100;
            // 当前项完成/跳过时，文件进度条同步置满。
            // 引擎不一定发出最终 FilePercentComplete=100（加密 ZIP 的 s7zAccumPct
            // 累积到不了 100、7z 最终报告缺失等），UI 层在此兜底保证显示正确。
            if (index == _currentBatchIndex)
                FilePercentComplete = 100;
        }
    }

    /// <summary>
    /// Called when batch completes with errors.
    /// Shows success/failure summary.
    /// </summary>
    public void CompleteWithErrors()
    {
        if (_batchItems == null) return;

        int succeeded = _batchItems.Count(i => i.Status == BatchItemStatus.Completed);
        int failed = _batchItems.Count(i => i.Status == BatchItemStatus.Failed);
        SetComplete(LocalizationManager.T("Progress_Batch_CompleteWithErrors", succeeded, failed));
    }

    /// <summary>
    /// Finalize batch: mark all still-InProgress items as Completed.
    /// </summary>
    public void FinalizeBatch()
    {
        if (_batchItems == null) return;
        for (int i = 0; i < _batchItems.Count; i++)
        {
            if (_batchItems[i].Status == BatchItemStatus.InProgress)
            {
                _batchItems[i].Status = BatchItemStatus.Completed;
                _batchItems[i].Progress = 100;
            }
        }
    }

    /// <summary>
    /// 点亮/熄灭指定批处理行的密码徽标（🔄 匹配中 / 🔑●●●● 已匹配 / 熄灭）。
    /// 两条点亮路径共用：路径 A（预匹配密码，解压开始前整批点亮）、路径 B（批循环内轮到该包时逐个点亮）。
    /// 调用方须在 UI 线程执行（ProgressWindow 包装层用 DispatchIfNeeded 兜底）。
    /// </summary>
    public void SetBatchPasswordState(int index, BatchPasswordState state,
        string? password, string? rule, string? description)
    {
        if (_batchItems == null || index < 0 || index >= _batchItems.Count)
            return;
        var row = _batchItems[index];
        row.PasswordState = state;
        // 熄灭时一并清空，避免残留上一个密码的明文/规则
        row.MatchedPassword = state == BatchPasswordState.Matched ? password : null;
        row.PasswordRule = state == BatchPasswordState.Matched ? rule : null;
        row.PasswordDescription = state == BatchPasswordState.Matched ? description : null;
    }

    /// <summary>
    /// Disable the cancel button. Used before entering non-interruptible operations.
    /// </summary>
    public void DisableCancel()
    {
        IsCancelEnabled = false;
    }

    // ════════════════════════════════════════════
    //  Error Summary
    // ════════════════════════════════════════════

    /// <summary>
    /// Set error summary text (selectable, shown between progress bars and buttons).
    /// </summary>
    public void SetErrorSummary(string message)
    {
        ErrorSummaryText = message;
        HasErrors = true;
    }

    // ════════════════════════════════════════════
    //  Progress Helpers
    // ════════════════════════════════════════════

    /// <summary>
    /// Creates an <see cref="IProgress{T}"/> that dispatches callbacks to the UI thread
    /// at <see cref="DispatcherPriority.Background"/> (lowest) priority.
    /// This allows the progress bar to repaint between progress updates.
    /// Safe to call from any thread.
    /// </summary>
    public static IProgress<ArchiveProgress> CreateBackgroundProgress(
        Dialogs.ProgressWindow pw,
        Action<ArchiveProgress> callback)
    {
        return new BackgroundDispatcherProgress(callback);
    }

    /// <summary>
    /// Creates a pause-aware wrapper around the given progress reporter.
    /// Report calls will block when paused, until resumed or cancelled.
    /// </summary>
    public IProgress<ArchiveProgress> CreatePauseAwareProgress(IProgress<ArchiveProgress> inner)
    {
        return new PauseAwareProgress(inner, _pauseEvent, _cts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// Custom IProgress implementation that dispatches to the UI thread at Background priority.
    /// </summary>
    private sealed class BackgroundDispatcherProgress : IProgress<ArchiveProgress>
    {
        private readonly Action<ArchiveProgress> _callback;

        public BackgroundDispatcherProgress(Action<ArchiveProgress> callback)
        {
            _callback = callback;
        }

        public void Report(ArchiveProgress value)
        {
            Dispatcher.UIThread.Post(() => _callback(value), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Pause-aware IProgress wrapper. Report blocks when paused.
    /// </summary>
    private sealed class PauseAwareProgress : IProgress<ArchiveProgress>
    {
        private readonly IProgress<ArchiveProgress> _inner;
        private readonly ManualResetEventSlim _pauseEvent;
        private readonly CancellationToken _cancellationToken;

        public PauseAwareProgress(IProgress<ArchiveProgress> inner, ManualResetEventSlim pauseEvent, CancellationToken cancellationToken)
        {
            _inner = inner;
            _pauseEvent = pauseEvent;
            _cancellationToken = cancellationToken;
        }

        public void Report(ArchiveProgress value)
        {
            try
            {
                _pauseEvent.Wait(_cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            _inner.Report(value);
        }
    }
}
