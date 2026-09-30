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

    /// <summary>顶部信息显示模式（全路径/仅目录/仅文件名）。</summary>
    private TopDisplayMode _topDisplayMode = TopDisplayMode.FullName;

    /// <summary>信息密度（紧凑/标准/宽松）。</summary>
    private DensityMode _densityMode = DensityMode.Normal;

    /// <summary>已处理文件数（引擎上报；兼容重载 TotalFiles=0 时不清零）。</summary>
    private long _statsProcessed;
    private long _statsSkipped;
    private long _statsFailed;
    private long _statsOverwritten;

    /// <summary>是否出现过冲突统计（压缩路径恒不上报 → 统计项按 Rule 6 隐藏）。</summary>
    private bool _hasConflictStats;

    /// <summary>当前文件所在目录（SplitFilePath 分离结果；变更触发 DirVisible 重算）。</summary>
    private string _dirName = string.Empty;

    /// <summary>ZIP 并行解压批次行（BatchIndex 驱动 upsert；非并行恒为空 → 容器隐藏）。</summary>
    private readonly ObservableCollection<ParallelBatchProgressItem> _parallelBatchItems = new();

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
            ["Progress_RevealTooltip"] = LocalizationManager.T("Progress_RevealTooltip"),
            ["Progress_CopyTooltip"] = LocalizationManager.T("Progress_CopyTooltip"),
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
            // T7 新增 key（顶部显示模式/信息密度单选——此字典由显式数组构建，漏登记 = 单选文字空白）
            ["Progress_Mode_FullPath"] = LocalizationManager.T("Progress_Mode_FullPath"),
            ["Progress_Mode_DirOnly"] = LocalizationManager.T("Progress_Mode_DirOnly"),
            ["Progress_Mode_NameOnly"] = LocalizationManager.T("Progress_Mode_NameOnly"),
            ["Progress_Density_Compact"] = LocalizationManager.T("Progress_Density_Compact"),
            ["Progress_Density_Normal"] = LocalizationManager.T("Progress_Density_Normal"),
            ["Progress_Density_Loose"] = LocalizationManager.T("Progress_Density_Loose"),
        };

        // T6: 操作计时基线 + 当前文件标签初值 + 并行批次集合变更通知
        _opStartUtc = DateTime.UtcNow;
        CurrentFileLabel = LocalizationManager.T("Progress_CurrentFileLabel");
        _parallelBatchItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasParallelBatches));
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

    /// <summary>顶部信息显示模式：全路径（目录+文件名两行）/ 仅目录 / 仅文件名。</summary>
    public TopDisplayMode TopDisplayMode
    {
        get => _topDisplayMode;
        set { if (SetProperty(ref _topDisplayMode, value)) NotifyDisplayProperties(); }
    }

    /// <summary>信息密度：紧凑/标准/宽松（行高与间距由 XAML 按 IsCompactDensity 切换）。</summary>
    public DensityMode DensityMode
    {
        get => _densityMode;
        set { if (SetProperty(ref _densityMode, value)) NotifyDisplayProperties(); }
    }

    /// <summary>目录行可见：非「仅文件名」模式且目录非空。</summary>
    public bool DirVisible => TopDisplayMode != TopDisplayMode.NameOnly && _dirName.Length > 0;

    /// <summary>文件名行可见：非「仅目录」模式。</summary>
    public bool NameVisible => TopDisplayMode != TopDisplayMode.DirOnly;

    /// <summary>是否紧凑密度档。</summary>
    public bool IsCompactDensity => DensityMode == DensityMode.Compact;

    /// <summary>是否宽松密度档。</summary>
    public bool IsLooseDensity => DensityMode == DensityMode.Loose;

    /// <summary>当前文件所在目录（路径行绑定；变更触发 DirVisible 重算）。</summary>
    public string DirName
    {
        get => _dirName;
        set { if (SetProperty(ref _dirName, value)) NotifyDisplayProperties(); }
    }

    /// <summary>统计栏：是否显示跳过/出错/已覆盖（引擎上报过冲突统计才为 true；压缩恒 false）。</summary>
    public bool HasConflictStats => _hasConflictStats;

    /// <summary>并行批次容器可见性（集合非空时 true；CollectionChanged 驱动通知）。</summary>
    public bool HasParallelBatches => _parallelBatchItems.Count > 0;

    /// <summary>并行批次行集合（ZIP 并行解压时由 BatchIndex 驱动 upsert）。</summary>
    public ObservableCollection<ParallelBatchProgressItem> ParallelBatchItems => _parallelBatchItems;

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

    /// <summary>派生显示属性集中通知（AGENTS.md 派生属性通知模式，禁逐字段 NotifyPropertyChangedFor）。</summary>
    private void NotifyDisplayProperties()
    {
        OnPropertyChanged(nameof(DirVisible));
        OnPropertyChanged(nameof(NameVisible));
        OnPropertyChanged(nameof(IsCompactDensity));
        OnPropertyChanged(nameof(IsLooseDensity));
    }

    // ════════════════════════════════════════════
    //  Password Section Properties
    // ════════════════════════════════════════════

    [ObservableProperty]
    private bool _isPasswordSectionVisible;

    [ObservableProperty]
    private string? _passwordMatchText;

    [ObservableProperty]
    private string? _passwordRuleText;

    [ObservableProperty]
    private string? _passwordStatusText;

    [ObservableProperty]
    private bool _isPasswordRevealEnabled;

    [ObservableProperty]
    private bool _isPasswordCopyEnabled;

    [ObservableProperty]
    private bool _isPasswordRevealed;

    private string? _password;

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
        // 操作计时基线兜底（ctor/InitBatchMode 已重置，此处防漏）
        if (_opStartUtc == default)
            _opStartUtc = DateTime.UtcNow;

        // 路径/文件名分离（剥引擎前缀 + '/' 拆分 → 目录行/文件名行两行显示）
        if (!string.IsNullOrEmpty(p.CurrentFile))
        {
            var (dir, name) = ProgressDisplayCalculator.SplitFilePath(p.CurrentFile);
            DirName = dir;
            FileName = name;
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
        // T7: 状态色随完成度切换（BrushResourceConverter 消费 StatusBrushName → 批次进度条前景）
        row.StatusBrushName = row.Percent >= 100 ? "ThemeStatusSuccessBrush" : "ThemeProgressFillBrush";
        if (p.BatchProcessedFiles.HasValue && p.BatchTotalFiles.HasValue)
            row.DetailText = LocalizationManager.T("Progress_Batch_FilesProgress",
                p.BatchProcessedFiles.Value, p.BatchTotalFiles.Value);
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
        _batchItems = new ObservableCollection<BatchItem>(
            paths.Select(p => new BatchItem
            {
                Name = System.IO.Path.GetFileName(p),
                FullPath = p,
                Status = BatchItemStatus.Pending
            }));
        OnPropertyChanged(nameof(BatchItems));
        OnPropertyChanged(nameof(IsBatchMode));
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

    // ════════════════════════════════════════════
    //  Password Section Methods
    // ════════════════════════════════════════════

    /// <summary>
    /// Show the password section with "matching..." status.
    /// </summary>
    public void ShowPasswordAttempt(string description)
    {
        IsPasswordSectionVisible = true;
        _password = null;
        IsPasswordRevealed = false;
        PasswordMatchText = LocalizationManager.T("Progress_MatchingPassword");
        PasswordRuleText = LocalizationManager.T("Progress_PwdRule", description);
        PasswordStatusText = "";
        IsPasswordRevealEnabled = false;
        IsPasswordCopyEnabled = false;
    }

    /// <summary>
    /// Show that password was matched successfully.
    /// </summary>
    public void ShowPasswordMatched(string password, string description)
    {
        IsPasswordSectionVisible = true;
        _password = password;

        // Respect PasswordRevealByDefault setting
        bool revealByDefault = AppSettings.Load().PasswordRevealByDefault;
        IsPasswordRevealed = revealByDefault;
        PasswordMatchText = revealByDefault
            ? LocalizationManager.T("Progress_PwdMatched", password)
            : LocalizationManager.T("Progress_PwdMatchedHidden");

        PasswordRuleText = LocalizationManager.T("Progress_PwdRule", description);
        PasswordStatusText = LocalizationManager.T("Progress_PwdVerifying");
        IsPasswordRevealEnabled = true;
        IsPasswordCopyEnabled = true;
    }

    /// <summary>
    /// Toggle password reveal/hide.
    /// </summary>
    [RelayCommand]
    private void TogglePasswordReveal()
    {
        IsPasswordRevealed = !IsPasswordRevealed;
        if (IsPasswordRevealed && _password != null)
            PasswordMatchText = LocalizationManager.T("Progress_PwdMatched", _password);
        else if (_password != null)
            PasswordMatchText = LocalizationManager.T("Progress_PwdMatchedHidden");
    }

    /// <summary>
    /// Get the current password text (for clipboard copy from code-behind).
    /// </summary>
    public string? GetPassword() => _password;

    /// <summary>
    /// Hide the password section (for non-encrypted files).
    /// </summary>
    public void HidePasswordSection()
    {
        IsPasswordSectionVisible = false;
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