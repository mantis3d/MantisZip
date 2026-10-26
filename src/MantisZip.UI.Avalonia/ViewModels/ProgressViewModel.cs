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
/// 进度窗口状态类别：驱动状态行左侧「状态图标」——图标始终存在且始终对应当前状态。
/// </summary>
public enum ProgressStatusKind
{
    /// <summary>准备中（枚举/分组/启动各组的静默期）。</summary>
    Preparing,
    /// <summary>进行中（引擎已开始上报）。</summary>
    Running,
    /// <summary>已暂停。</summary>
    Paused,
    /// <summary>已完成（成功）。</summary>
    Completed,
    /// <summary>失败（整包异常或逐条目失败）。</summary>
    Failed,
    /// <summary>已取消（用户主动取消，非错误）。</summary>
    Cancelled,
}

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

    /// <summary>当前冻结段的起点（null = 未冻结）。暂停/模态弹窗期间不计入已用时间。</summary>
    private DateTime? _clockFrozenSinceUtc;

    /// <summary>已结算的历史冻结总时长。</summary>
    private TimeSpan _clockFrozenTotal;

    /// <summary>模态弹窗冻结计数（冲突/密码弹窗，防嵌套）。</summary>
    private int _modalFreezeCount;

    /// <summary>已处理文件数（引擎上报；兼容重载 TotalFiles=0 时不清零）。</summary>
    private long _statsProcessed;

    /// <summary>本次操作总文件数（引擎上报；0 = 未上报，统计卡计数回退纯计数）。</summary>
    private long _statsTotalFiles;

    private long _statsSkipped;
    private long _statsFailed;
    private long _statsOverwritten;

    /// <summary>是否出现过冲突统计（压缩路径恒不上报 → 统计项按 Rule 6 隐藏）。</summary>
    private bool _hasConflictStats;

    // ── 语言切换重渲缓存（OnCultureChanged 用最近一次值重新格式化动态文案）──
    /// <summary>最近一次有效速度采样（B/s；0 = 尚无）。</summary>
    private double _lastSpeedBytesPerSec;
    /// <summary>最近一次 ETA（秒；null = 未知）。</summary>
    private double? _lastEtaSeconds;
    /// <summary>最近一次文件计数文案的分子/分母与批次标志。</summary>
    private int _lastFileCountCurrent;
    private int _lastFileCountTotal;
    private bool _lastFileCountIsBatch;

    /// <summary>当前文件所在目录（SplitFilePath 分离结果，路径行绑定）。</summary>
    private string _dirName = string.Empty;

    /// <summary>ZIP 并行解压批次行（BatchIndex 驱动 upsert；非并行恒为空 → 容器隐藏）。</summary>
    private readonly ObservableCollection<ParallelBatchProgressItem> _parallelBatchItems = new();

    /// <summary>顶部内容模式（默认 Detailed；详见 <see cref="ContentMode"/>。窗口构造时按 AppSettings 持久化值覆盖）。</summary>
    private ProgressContentMode _contentMode = ProgressContentMode.Detailed;

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
        LocalizedStrings = new Dictionary<string, string>();
        FillLocalizedStrings();

        // 语言即时刷新：进行中切换语言时重灌窗口文案；窗口关闭必须 DetachLocalization 退订（防静态事件泄漏）
        LocalizationManager.CultureChanged += OnCultureChanged;

        // T6: 操作计时基线 + 当前文件标签初值 + 集合变更通知（并行批次行 / 条目行）
        RestartClock();
        CurrentFileLabel = LocalizationManager.T("Progress_CurrentFileLabel");
        StoreSectionHeaderText = LocalizationManager.T("Progress_Section_StoreFiles");
        UpdateChannelHeaderText();
        _parallelBatchItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasParallelBatches));
            NotifyChannelProperties();
        };
        _entryItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEntryItems));
        RefreshTimeDisplay();
    }

    /// <summary>
    /// 把 ProgressWindow 绑定的静态文案灌入 <see cref="LocalizedStrings"/>（ctor 与 OnCultureChanged 共用）。
    /// 该字典由本方法显式逐键填充，漏登记 = XAML 绑定空白且构建不报错。
    /// </summary>
    private void FillLocalizedStrings()
    {
        var s = LocalizedStrings;
        s["Progress_Cancel"] = LocalizationManager.T("Progress_Cancel");
        s["Progress_Complete"] = LocalizationManager.T("Progress_Complete");
        s["Progress_Cancelling"] = LocalizationManager.T("Progress_Cancelling");
        s["Progress_Button_Pause"] = LocalizationManager.T("Progress_Button_Pause");
        s["Progress_Button_Resume"] = LocalizationManager.T("Progress_Button_Resume");
        s["Progress_Button_Close"] = LocalizationManager.T("Progress_Button_Close");
        s["Progress_KeepOpen"] = LocalizationManager.T("Progress_KeepOpen");
        s["Progress_Paused"] = LocalizationManager.T("Progress_Paused");
        s["Progress_Resuming"] = LocalizationManager.T("Progress_Resuming");
        s["MsgBox_Cancel"] = LocalizationManager.T("MsgBox_Cancel");
        // T6 新增 key（统计/时间/并行批次/密码徽标——此字典由显式数组构建，漏登记 = XAML 绑定空白）
        s["Progress_Stats_Processed"] = LocalizationManager.T("Progress_Stats_Processed");
        s["Progress_Stats_Skipped"] = LocalizationManager.T("Progress_Stats_Skipped");
        s["Progress_Stats_Failed"] = LocalizationManager.T("Progress_Stats_Failed");
        s["Progress_Stats_Overwritten"] = LocalizationManager.T("Progress_Stats_Overwritten");
        s["Progress_Stats_Speed"] = LocalizationManager.T("Progress_Stats_Speed");
        s["Progress_Time_Elapsed"] = LocalizationManager.T("Progress_Time_Elapsed");
        s["Progress_Time_Remaining"] = LocalizationManager.T("Progress_Time_Remaining");
        s["Progress_CurrentFileLabel"] = LocalizationManager.T("Progress_CurrentFileLabel");
        s["Progress_Batch_ArchiveOf"] = LocalizationManager.T("Progress_Batch_ArchiveOf");
        s["Progress_Batch_FilesProgress"] = LocalizationManager.T("Progress_Batch_FilesProgress");
        s["Progress_Batch_Pwd_Matching"] = LocalizationManager.T("Progress_Batch_Pwd_Matching");
        s["Progress_Batch_Pwd_MatchedTip"] = LocalizationManager.T("Progress_Batch_Pwd_MatchedTip");
        s["Progress_Batch_Pwd_Rule"] = LocalizationManager.T("Progress_Batch_Pwd_Rule");
        s["Progress_Batch_Pwd_Desc"] = LocalizationManager.T("Progress_Batch_Pwd_Desc");
        // T7 新增 key（顶部内容模式/信息量分级 + 并行统计/批次/条目/Toast——
        // 此字典由显式数组构建，漏登记 = XAML 绑定空白且构建不报错；
        // 文案值由后续任务落 JSON，此处先登记 T() 占位调用）
        s["Progress_Mode_Label"] = LocalizationManager.T("Progress_Mode_Label");
        s["Progress_Mode_Simple"] = LocalizationManager.T("Progress_Mode_Simple");
        s["Progress_Mode_Detailed"] = LocalizationManager.T("Progress_Mode_Detailed");
        s["Progress_Mode_List"] = LocalizationManager.T("Progress_Mode_List");
        s["Progress_Density_Minimal"] = LocalizationManager.T("Progress_Density_Minimal");
        s["Progress_Density_Medium"] = LocalizationManager.T("Progress_Density_Medium");
        s["Progress_Density_Full"] = LocalizationManager.T("Progress_Density_Full");
        s["Progress_Stats_Parallel"] = LocalizationManager.T("Progress_Stats_Parallel");
        s["Progress_Stats_Compressed"] = LocalizationManager.T("Progress_Stats_Compressed");
        s["Progress_Batch_SectionTitle"] = LocalizationManager.T("Progress_Batch_SectionTitle");
        s["Progress_Batch_Count"] = LocalizationManager.T("Progress_Batch_Count");
        s["Progress_Entry_Pending"] = LocalizationManager.T("Progress_Entry_Pending");
        s["Progress_Entry_Active"] = LocalizationManager.T("Progress_Entry_Active");
        // T9 新增 key（条目行终态文案）——Progress_Stats_* 是带 {0} 计数的格式串，
        // 行内状态无计数可代，故独立成键；文案值由后续任务落 JSON，此处先登记 T() 占位调用
        s["Progress_Entry_Completed"] = LocalizationManager.T("Progress_Entry_Completed");
        s["Progress_Entry_Skipped"] = LocalizationManager.T("Progress_Entry_Skipped");
        s["Progress_Entry_Failed"] = LocalizationManager.T("Progress_Entry_Failed");
        s["Progress_Entry_Overwritten"] = LocalizationManager.T("Progress_Entry_Overwritten");
        s["Progress_Toast_Copied"] = LocalizationManager.T("Progress_Toast_Copied");
        s["Progress_Batch_Label"] = LocalizationManager.T("Progress_Batch_Label");
        s["Progress_CompressChannelHint"] = LocalizationManager.T("Progress_CompressChannelHint");
        s["Progress_Tooltip_Progress"] = LocalizationManager.T("Progress_Tooltip_Progress");
        s["Progress_Tooltip_Bytes"] = LocalizationManager.T("Progress_Tooltip_Bytes");
        s["Progress_Tooltip_Ratio"] = LocalizationManager.T("Progress_Tooltip_Ratio");
        // Task 10：自动关闭倒计时后缀（数字由可编辑 TextBox 承载，此 key 刻意不含 {0}）
        s["Progress_AutoCloseSuffix"] = LocalizationManager.T("Progress_AutoCloseSuffix");
        // Task 11：解压后置开关的 tooltip（复用设置页既有文案 key）
        s["Settings_Extract_OpenFolderAfter"] = LocalizationManager.T("Settings_Extract_OpenFolderAfter");
        s["Settings_Extract_DeleteArchiveAfterExtract"] = LocalizationManager.T("Settings_Extract_DeleteArchiveAfterExtract");
    }

    /// <summary>
    /// 语言切换处理器：重灌静态文案并重生依赖本地化格式串的动态文案
    /// （速度 / 剩余时间 / 文件计数 / 统计栏 / 统计卡短标签）。
    /// 不 dispatch（<see cref="LocalizationManager.CultureChanged"/> 由 UI 线程 setter 触发，
    /// 见 PreviewViewModel.OnCultureChanged 先例）。StatusMessage/准备态文案不在刷新范围（spec 范围外）。
    /// </summary>
    private void OnCultureChanged(object? sender, EventArgs e)
    {
        FillLocalizedStrings();
        OnPropertyChanged(nameof(LocalizedStrings));

        // 「当前文件」标签 + 已用时间
        CurrentFileLabel = LocalizationManager.T("Progress_CurrentFileLabel");
        StoreSectionHeaderText = LocalizationManager.T("Progress_Section_StoreFiles");
        UpdateChannelHeaderText();
        RefreshTimeDisplay();

        // 速度（按上次采样值重格式化）
        if (_lastSpeedBytesPerSec > 0)
            SpeedText = LocalizationManager.T("Progress_Stats_Speed", FormatUtil.FormatSize((long)_lastSpeedBytesPerSec));

        // 剩余时间（ETA）
        RemainingText = _lastEtaSeconds is { } eta && eta > 0
            ? LocalizationManager.T("Progress_Time_Remaining",
                ProgressDisplayCalculator.FormatDuration(TimeSpan.FromSeconds(eta)))
            : string.Empty;

        // 文件计数 + 批次档案序号
        if (_lastFileCountTotal > 0)
        {
            FileCountText = LocalizationManager.T("Progress_FileCount", _lastFileCountCurrent, _lastFileCountTotal);
            BatchArchiveIndexText = _lastFileCountIsBatch
                ? LocalizationManager.T("Progress_Batch_ArchiveOf", _lastFileCountCurrent, _lastFileCountTotal)
                : string.Empty;
        }

        // 统计栏（含计数的格式串）——按缓存计数重渲；空值不重渲（避免把未上报的项填成 0）
        if (_statsTotalFiles > 0)
            StatsProcessedText = LocalizationManager.T("Progress_Stats_Processed", $"{_statsProcessed}/{_statsTotalFiles}");
        if (!string.IsNullOrEmpty(StatsSkippedText))
            StatsSkippedText = LocalizationManager.T("Progress_Stats_Skipped", _statsSkipped);
        if (!string.IsNullOrEmpty(StatsFailedText))
            StatsFailedText = LocalizationManager.T("Progress_Stats_Failed", _statsFailed);
        if (!string.IsNullOrEmpty(StatsOverwrittenText))
            StatsOverwrittenText = LocalizationManager.T("Progress_Stats_Overwritten", _statsOverwritten);
        if (!string.IsNullOrEmpty(StatsTotalSizeValue))
            StatsTotalSizeText = LocalizationManager.T("Progress_Stats_TotalSize", StatsTotalSizeValue);

        // 统计卡短标签（get-only 派生属性，读取 T()）
        OnPropertyChanged(nameof(StatsProcessedLabel));
        OnPropertyChanged(nameof(StatsSkippedLabel));
        OnPropertyChanged(nameof(StatsFailedLabel));
        OnPropertyChanged(nameof(StatsOverwrittenLabel));
        OnPropertyChanged(nameof(StatsTotalSizeLabel));

        // 批处理计数文案（BatchCountText 等）
        NotifyBatchProperties();
    }

    /// <summary>窗口关闭时调用，退订静态 CultureChanged，防止 VM 被静态事件根引用泄漏。</summary>
    public void DetachLocalization()
        => LocalizationManager.CultureChanged -= OnCultureChanged;

    // ════════════════════════════════════════════
    //  Observable Properties
    // ════════════════════════════════════════════

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private int _percentComplete;

    [ObservableProperty]
    private int _filePercentComplete;

    /// <summary>当前文件进度比例（0–1），供详细面板「当前文件」行的文件底纹使用。</summary>
    public double CurrentFileRatio => Math.Clamp(FilePercentComplete, 0, 100) / 100.0;

    /// <summary>
    /// 详细面板「当前文件」行右侧信息：合并/写入阶段显示「已写 / 总量」，其余阶段显示当前文件大小。
    /// 该行独立于并行通道行、始终存在，解决「所有通道完成后详细面板不再变化」的问题。
    /// </summary>
    [ObservableProperty]
    private string _detailFileInfoText = string.Empty;

    /// <summary>详细面板「压缩通道」小标题（含「N 组并行」，组数来自引擎 BatchCount）。</summary>
    [ObservableProperty]
    private string _compressChannelsHeaderText = string.Empty;

    /// <summary>详细面板「存储文件」小标题文案（仅「写出仅存储文件」阶段可见）。</summary>
    [ObservableProperty]
    private string _storeSectionHeaderText = string.Empty;

    /// <summary>
    /// 是否处于「写出仅存储文件」阶段（Finalizing 且已收到合并字节）：
    /// 决定「存储文件」小标题是否出现，以及该行左侧「当前文件」标签是否让位给这个小标题。
    /// </summary>
    [ObservableProperty]
    private bool _showStoreSectionHeader;

    /// <summary>引擎上报的并行组数（BatchCount），用于「压缩通道 · N 组并行」标题。</summary>
    private int _channelGroupCount;

    [ObservableProperty]
    private string? _fileName;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private bool _isPaused;

    // 暂停/继续直接改 IsPaused（TogglePause 与 PauseFromConflict 均如此）→ 冻结/解冻自动跟随
    partial void OnIsPausedChanged(bool value)
    {
        // 暂停：保存当前状态文本；恢复：还原（取代旧的永久"正在恢复"文案）
        _statusBeforePause = value ? StatusMessage : _statusBeforePause;
        if (!value && _statusBeforePause is not null)
            StatusMessage = _statusBeforePause;
        UpdateClockFreezeState();
    }

    [ObservableProperty]
    private bool _hasErrors;

    [ObservableProperty]
    private string? _errorSummaryText;

    /// <summary>黄色摘要段：压缩端带原因跳过的逐条目汇总（≤20 行，溢出加 …另 N 项）。</summary>
    [ObservableProperty]
    private string? _skippedSummaryText;

    /// <summary>是否存在带原因的跳过条目。独立于 <see cref="HasErrors"/>——黄色段绝不影响红段标志（D4）。</summary>
    [ObservableProperty]
    private bool _hasSkippedEntries;

    /// <summary>红+黄合并摘要（红段 + 单个 \n + 黄段，空段省略无空行；供复制按钮使用）。</summary>
    [ObservableProperty]
    private string? _combinedSummaryText;

    /// <summary>红段或黄段任一非空（错误摘要复制按钮显隐；集中通知模式，避免逐字段标注）。</summary>
    [ObservableProperty]
    private bool _hasAnySummary;

    /// <summary>红段 tooltip：逐条目原因 + 异常原文详情（摘要框 hover 查看；无详情时为 null）。</summary>
    [ObservableProperty]
    private string? _errorSummaryTooltip;

    /// <summary>黄段 tooltip：逐条目原因 + 异常原文详情（无详情时为 null）。</summary>
    [ObservableProperty]
    private string? _skippedSummaryTooltip;

    // ════════════════════════════════════════════
    //  解压后置动作开关（spec §11；实例级，仅本次生效，不回写设置，D17）
    // ════════════════════════════════════════════

    /// <summary>显示「解压后打开目录」开关（能力位；压缩/测试/拖拽场景 false，D18）。</summary>
    [ObservableProperty]
    private bool _showOpenFolderToggle;

    /// <summary>显示「解压后移入回收站」开关（能力位；压缩/测试/CLI 拖拽场景 false，D18）。</summary>
    [ObservableProperty]
    private bool _showDeleteArchiveToggle;

    /// <summary>本次解压完成后打开目标目录（初值镜像设置或对话框选择；不回写设置）。</summary>
    [ObservableProperty]
    private bool _openFolderAfterExtract;

    /// <summary>本次解压完成后把原压缩包移入回收站（同上）。</summary>
    [ObservableProperty]
    private bool _deleteArchiveAfterExtract;

    /// <summary>测试流程标记（自检窗口）；置 true 自动钉住窗口（📌）。</summary>
    [ObservableProperty]
    private bool _isTestFlow;

    [ObservableProperty]
    private string? _fileCountText;

    [ObservableProperty]
    private bool _isCancelEnabled = true;

    /// <summary>是否显示暂停按钮（完成/失败终态隐藏——操作已结束，暂停无意义）。</summary>
    [ObservableProperty]
    private bool _isPauseButtonVisible = true;

    /// <summary>Is the keep-open toggle currently checked (📌 pinned open).</summary>
    [ObservableProperty]
    private bool _keepOpenOnComplete;

    /// <summary>自动关闭倒计时可见（仅延时等待期间 true；📌/失败/测试流为手动等待不显示）。</summary>
    [ObservableProperty]
    private bool _autoCloseCountdownVisible;

    partial void OnKeepOpenOnCompleteChanged(bool value)
    {
        // 📌 钉住 → 隐藏倒计时组（手动等待无倒计时）；不自动恢复（切手动等待是单向的）
        if (value) AutoCloseCountdownVisible = false;
    }

    /// <summary>当前状态类别：驱动状态行左侧状态图标（图标始终存在且始终对应当前状态）。</summary>
    [ObservableProperty]
    private ProgressStatusKind _statusKind = ProgressStatusKind.Preparing;

    /// <summary>状态图标资源键（GeometryResourceConverter → PathIcon.Data）。</summary>
    public string StatusIconKey => StatusKind switch
    {
        ProgressStatusKind.Preparing => "IconArchiveClock",
        ProgressStatusKind.Running => "IconLightning",
        ProgressStatusKind.Paused => "IconPause",
        ProgressStatusKind.Completed => "IconCheckmark",
        ProgressStatusKind.Failed => "IconWarning",
        ProgressStatusKind.Cancelled => "IconProhibited",
        _ => "IconInfo",
    };

    /// <summary>状态图标前景画刷资源键（BrushResourceConverter → PathIcon.Foreground）。</summary>
    public string StatusBrushKey => StatusKind switch
    {
        ProgressStatusKind.Paused => "ThemeStatusWarningBrush",
        ProgressStatusKind.Completed => "ThemeStatusSuccessBrush",
        ProgressStatusKind.Failed => "ThemeStatusErrorBrush",
        _ => "ThemeTextSecondaryBrush",
    };

    partial void OnStatusKindChanged(ProgressStatusKind value)
    {
        OnPropertyChanged(nameof(StatusIconKey));
        OnPropertyChanged(nameof(StatusBrushKey));
    }

    /// <summary>当前为压缩流程（vs 解压）：控制通道区说明文字的可见性。解压侧恒为 false。</summary>
    [ObservableProperty]
    private bool _isCompressFlow;

    // 切压缩/解压流程时刷新通道派生属性（ShowCompressChannelHint 依赖本字段）
    partial void OnIsCompressFlowChanged(bool value) => NotifyChannelProperties();

    // T5: 红段驱动 HasErrors；false→true 时自动钉住 📌（绝不反向解除，D10）
    partial void OnHasErrorsChanged(bool value)
    {
        if (value)
        {
            KeepOpenOnComplete = true;
            StatusKind = ProgressStatusKind.Failed;   // 状态图标转「失败」
        }
    }

    // T5: 测试流程标记置 true 时自动钉住 📌
    partial void OnIsTestFlowChanged(bool value)
    {
        if (value) KeepOpenOnComplete = true;
    }

    /// <summary>
    /// 操作说明行文案（如「正在删除 3 个文件（共 1.2 MB）」）。
    /// 用于「整包重写、无逐条目回调」的操作（删除）：N 行文件列表只能是假进度，故用一行真实影响面代替。
    /// 空值 → 整行隐藏（规则 6），压缩/解压流程不设置本属性，行为不变。
    /// </summary>
    [ObservableProperty]
    private string? _operationNoteText;

    /// <summary>说明行是否可见（有文案才显示）。</summary>
    public bool ShowOperationNote => !string.IsNullOrEmpty(OperationNoteText);

    partial void OnOperationNoteTextChanged(string? value)
        => OnPropertyChanged(nameof(ShowOperationNote));

    /// <summary>设置操作说明行文案（null/空 = 隐藏）。须在窗口显示前或 UI 线程调用。</summary>
    public void SetOperationNote(string? text) => OperationNoteText = text;

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

    /// <summary>存在真实并行批次行（区别于 HasParallelBatches「集合非空」——非并行单行也会使后者为 true）。</summary>
    public bool HasParallelChannel => _parallelBatchItems.Any(x => x.IsParallel);

    /// <summary>压缩通道说明行可见性：压缩流程且有并行通道（非并行单行时不显示，D8）。</summary>
    public bool ShowCompressChannelHint => IsCompressFlow && HasParallelChannel;

    // ── 压缩率计划：整体压缩汇总（后置回填，仅 ZIP 压缩完成）──
    private long _overallCompressedBytes;
    private long _overallTotalBytes;

    // ── 终态摘要（完成/失败/取消后冻结的统计）──
    /// <summary>终态冻结的最终耗时（null = 进行中/未开始）。冻结后 <see cref="RefreshTimeDisplay"/> 恒显示该值，时间不再增长。</summary>
    private TimeSpan? _finalElapsed;

    /// <summary>已结束压缩包上报的总字节累计（批量模式在切换档案时结算，见 <see cref="SetCurrentBatchItem"/>）。</summary>
    private long _finishedArchiveBytes;

    /// <summary>当前压缩包上报的总字节（解压=解压后大小，压缩=原始大小），切换档案时并入 <see cref="_finishedArchiveBytes"/>。</summary>
    private long _currentArchiveBytes;

    /// <summary>整体压缩汇总可用（后置回填 TotalCompressedBytes > 0；仅 ZIP 压缩完成）。</summary>
    public bool HasOverallCompression => _overallCompressedBytes > 0;

    /// <summary>整体压缩汇总文案（格式 A："300 MB (60%)"＝输出大小 (率%)，D4/D6）。</summary>
    public string OverallCompressionText => _overallCompressedBytes > 0
        ? $"{FormatUtil.FormatSize(_overallCompressedBytes)} ({_overallCompressedBytes * 100.0 / Math.Max(1, _overallTotalBytes):0}%)"
        : string.Empty;

    /// <summary>实时速度可用（EMA 平滑后有值；无字节上报的引擎为空）。完整档据此在段 2 显示速度。</summary>
    public bool HasLiveSpeed => !string.IsNullOrEmpty(SpeedText);

    /// <summary>段 2 第 1 个分隔符（并行度 · 速度 / 压缩汇总）。</summary>
    public bool ShowChannelMetaSeparator => HasParallelDegree && (HasLiveSpeed || HasOverallCompression);

    /// <summary>段 2 第 2 个分隔符（速度 · 压缩汇总）：两者都在时才出现。</summary>
    public bool ShowSpeedMetaSeparator => HasLiveSpeed && HasOverallCompression;

    /// <summary>段 2 整行可见性：并行度 / 实时速度 / 整体压缩汇总任一存在即显示（都无则整段隐藏，D5）。</summary>
    public bool ShowChannelMetaRow => HasParallelDegree || HasLiveSpeed || HasOverallCompression;

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

    /// <summary>统计栏/统计卡：总大小完整文案（如 "总大小 1.00 MB"；引擎未上报 TotalBytes 时为空 → Rule 6 隐藏）。</summary>
    [ObservableProperty]
    private string _statsTotalSizeText = string.Empty;

    /// <summary>统计卡数值：总大小数值（如 "1.00 MB"；引擎未上报 TotalBytes 时为空 → Rule 6 隐藏）。</summary>
    [ObservableProperty]
    private string _statsTotalSizeValue = string.Empty;

    /// <summary>统计卡行 3（已处理）：文件大小（引擎上报 TotalBytes 后随进度更新；默认 "—" 行级占位，D2/D4）。</summary>
    [ObservableProperty]
    private string _statsProcessedSize = "—";

    /// <summary>统计卡行 2（总大小卡）：文件总数（引擎上报 TotalFiles 后填充；默认 "—" 行级占位，D2/D4）。</summary>
    [ObservableProperty]
    private string _statsTotalCount = "—";

    /// <summary>统计卡行 3（跳过）：终态字节累加值（FileTotalBytes 事件优先 ↘ 播种行 Size 回退；从未累加到字节保持 "—"，D2 注 2/D4）。</summary>
    [ObservableProperty]
    private string _statsSkippedSize = "—";

    /// <summary>统计卡行 3（出错）：终态字节累加值（同 StatsSkippedSize 规则）。</summary>
    [ObservableProperty]
    private string _statsFailedSize = "—";

    /// <summary>统计卡行 3（已覆盖）：终态字节累加值（同 StatsSkippedSize 规则）。</summary>
    [ObservableProperty]
    private string _statsOverwrittenSize = "—";

    // 终态字节累加器（D4）：驱动 StatsSkippedSize/StatsFailedSize/StatsOverwrittenSize；
    // 仅 UI 线程访问（SetProgress/UpdateEntryStatus 均经 dispatch），无需同步
    private long _statsSkippedBytes;
    private long _statsFailedBytes;
    private long _statsOverwrittenBytes;

    /// <summary>速度文案（如 "12.3 MB/s"）。</summary>
    [ObservableProperty]
    private string _speedText = string.Empty;

    /// <summary>速度每次变化都要重算段 2 的三处派生可见性（每秒最多几次，开销可忽略）。</summary>
    partial void OnSpeedTextChanged(string value) => NotifyChannelMetaVisibility();

    /// <summary>段 2 派生可见性集中通知（并行度 / 速度 / 压缩汇总三者的行与分隔符）。</summary>
    private void NotifyChannelMetaVisibility()
    {
        OnPropertyChanged(nameof(HasLiveSpeed));
        OnPropertyChanged(nameof(ShowChannelMetaSeparator));
        OnPropertyChanged(nameof(ShowSpeedMetaSeparator));
        OnPropertyChanged(nameof(ShowChannelMetaRow));
    }

    /// <summary>已用时间文案。</summary>
    [ObservableProperty]
    private string _elapsedText = string.Empty;

    /// <summary>剩余时间文案（ETA 无效时为空）。</summary>
    [ObservableProperty]
    private string _remainingText = string.Empty;

    /// <summary>
    /// 终态汇总①：压缩包数量文案（生成/解压 N 个；批量有失败时显示 ok/total）。
    /// 刻意只放统计卡 / 时间行 / 段 2 都拿不到的维度 —— 文件数、用时、体积、压缩率已由它们承担，
    /// 重复一遍会让终态多出一整块冗余信息。空串 = 未进入终态 → 按规则 6 整块隐藏。
    /// </summary>
    [ObservableProperty]
    private string _completionArchivesText = string.Empty;

    /// <summary>终态汇总②：平均速度文案（终态总字节 / 实际耗时）。空串 = 不显示（耗时过短或字节未知）。</summary>
    [ObservableProperty]
    private string _completionSpeedText = string.Empty;

    /// <summary>终态汇总③：压缩后大小文案（仅 ZIP 压缩可用——只有它上报 TotalCompressedBytes）。</summary>
    [ObservableProperty]
    private string _completionCompressedText = string.Empty;

    /// <summary>终态汇总行是否可见（任一项有内容）。完整档在段 3、中档在中档单行末尾各显示一次。</summary>
    public bool HasCompletionStats =>
        !string.IsNullOrEmpty(CompletionArchivesText)
        || !string.IsNullOrEmpty(CompletionSpeedText)
        || !string.IsNullOrEmpty(CompletionCompressedText);

    partial void OnCompletionArchivesTextChanged(string value) => OnPropertyChanged(nameof(HasCompletionStats));

    partial void OnCompletionSpeedTextChanged(string value) => OnPropertyChanged(nameof(HasCompletionStats));

    partial void OnCompletionCompressedTextChanged(string value) => OnPropertyChanged(nameof(HasCompletionStats));

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
        OnPropertyChanged(nameof(HasEntryItems));
        OnPropertyChanged(nameof(HasParallelDegree));
        OnPropertyChanged(nameof(IsMinimalDensity));
        OnPropertyChanged(nameof(IsMediumDensity));
        OnPropertyChanged(nameof(IsFullDensity));
        NotifyChannelMetaVisibility();
        NotifyChannelProperties();
    }

    /// <summary>通道派生属性集中通知（P1）：CollectionChanged 与 NotifyDisplayProperties 均调用本方法，
    /// 保证「首个并行行到达」与「用户切换模式/并行度」两种路径都刷新 HasParallelChannel/ShowCompressChannelHint。</summary>
    private void NotifyChannelProperties()
    {
        OnPropertyChanged(nameof(HasParallelChannel));
        OnPropertyChanged(nameof(ShowCompressChannelHint));
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

    /// <summary>统计卡短标签（总大小，从格式化文案剥离占位符得到；与其余短标签一样不随属性变更通知）。</summary>
    public string StatsTotalSizeLabel => LocalizationManager.T("Progress_Stats_TotalSize", string.Empty).Trim();

    /// <summary>统计卡行 2（已处理）：已处理文件数纯计数（方案 A 去分母——分母与总大小卡行 2 StatsTotalCount 同源重复，比例由总进度条承担；中等档 StatsProcessedText 保留 N/M 分数）。</summary>
    public string StatsProcessedCount => _statsProcessed.ToString();

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
        // 进行中/暂停/准备态主动取消 → 图标转「已取消」；已完成/失败后点击「关闭」不改状态
        if (StatusKind is ProgressStatusKind.Preparing or ProgressStatusKind.Running or ProgressStatusKind.Paused)
        {
            StatusKind = ProgressStatusKind.Cancelled;
            // 取消同样是终态：冻结已用时间（窗口随即关闭，但失败/保留窗口场景仍有意义）
            FreezeCompletion();
        }
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
            StatusKind = ProgressStatusKind.Paused;
        }
        else
        {
            // Paused → resume
            _pauseEvent.Set();
            IsPaused = false;
            StatusKind = ProgressStatusKind.Running;
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

    // ════════════════════════════════════════════
    //  准备态（引擎首次上报之前）
    // ════════════════════════════════════════════

    private bool _isPreparing;
    private string? _preparingText;
    /// <summary>暂停前的状态文本（恢复时还原，取代永久停留的"正在恢复"）。</summary>
    private string? _statusBeforePause;

    /// <summary>
    /// 进入准备态：总进度条以不定进度滚动 + 状态行提示「正在准备…」。
    /// 大目录在引擎首次上报前有「枚举 + 分组 + 启动各组」的静默期，此态避免进度条空白被误认为出错。
    /// 操作开始（构造 / 切换批次档案）时调用；首个 SetProgress 上报即结束。
    /// </summary>
    /// <remarks>
    /// **不重设计时基线**：调用方除了「构造新窗口」，还有「批处理切换档案」——后者必须与整批共用
    /// 同一个计时基线。更要紧的是这条路径会在终态之后被调用：批处理回调经 Dispatcher 以
    /// <c>DispatcherPriority.Background</c> 投递，而终态 <c>SetComplete</c> 由操作闭包在 UI 线程
    /// 直接执行（Normal 优先级）⇒ 队尾那条迟到的 <c>SetCurrentBatchItem</c> 会排在冻结**之后**才跑。
    /// 若在这里重置，就会把已冻结的最终耗时打回 0 并清空终态统计（用户实测「压缩完成后时间又被重置」）。
    /// 新操作重置只走 <c>ctor</c> / <see cref="InitBatchMode"/>。
    /// </remarks>
    public void BeginPreparing()
    {
        // 终态之后迟到的调用（批处理队尾回调）不得把窗口从「已完成」拉回「正在准备…」
        if (_finalElapsed != null)
            return;

        _isPreparing = true;
        // 新一轮操作：阶段与扫描计数归零（上一档案的「正在收尾…」「已发现 N 个」不得残留）
        _activePhase = null;
        ScanCountText = null;
        IsIndeterminate = true;
        _preparingText = LocalizationManager.T("Progress_Preparing");
        StatusMessage = _preparingText;
        StatusKind = ProgressStatusKind.Preparing;
    }

    /// <summary>结束准备态：不定进度条恢复确定态，准备文案让位给真实状态（若已被其它状态覆盖则不动）。</summary>
    private void EndPreparing()
    {
        if (!_isPreparing) return;
        _isPreparing = false;
        IsIndeterminate = false;
        if (StatusMessage == _preparingText)
            StatusMessage = string.IsNullOrEmpty(WindowTitle) ? null : WindowTitle;
        if (StatusKind == ProgressStatusKind.Preparing)
            StatusKind = ProgressStatusKind.Running;
    }

    /// <summary>引擎最近一次上报是否携带「当前文件百分比」。false = 未知（如 7z mt=on 路径
    /// 只报文件级事件、FilePercentComplete 恒为 null），此时简约面板的「当前文件」进度条
    /// 回退到总进度 PercentComplete，避免长时间僵死在 0%。</summary>
    private bool _hasFilePercent;

    // ════════════════════════════════════════════
    //  阶段态（引擎上报 ArchivePhase）
    // ════════════════════════════════════════════

    /// <summary>最近一次引擎上报的阶段（null = 本次操作尚未上报阶段）。</summary>
    private ArchivePhase? _activePhase;

    /// <summary>扫描阶段「已发现 N 个文件」文案；非扫描阶段为 null（文件计数行回到 N/M）。</summary>
    [ObservableProperty]
    private string? _scanCountText;

    /// <summary>扫描计数可见性：与 <see cref="FileCountText"/> 在信息区文件计数行互斥显隐。</summary>
    public bool HasScanCount => !string.IsNullOrEmpty(ScanCountText);

    partial void OnScanCountTextChanged(string? value) => OnPropertyChanged(nameof(HasScanCount));

    /// <summary>阶段 → 本地化文案 key。</summary>
    private static string PhaseKey(ArchivePhase phase) => phase switch
    {
        ArchivePhase.Scanning => "Progress_Phase_Scanning",
        ArchivePhase.ReadingArchive => "Progress_Phase_ReadingArchive",
        ArchivePhase.Compressing => "Progress_Phase_Compressing",
        ArchivePhase.Extracting => "Progress_Phase_Extracting",
        ArchivePhase.Finalizing => "Progress_Phase_Finalizing",
        _ => "Progress_Phase_Testing",
    };

    /// <summary>
    /// 应用引擎阶段上报：状态行改为阶段文案（只显示阶段名，计数器另在文件计数行）；
    /// 扫描阶段额外切不定进度条 + 显示「已发现 N 个文件」。
    /// 暂停/失败/取消已是更具体的人为状态，阶段上报不得覆盖它们的文案。
    /// </summary>
    private void ApplyPhase(ArchivePhase phase)
    {
        _activePhase = phase;

        var runningLike = StatusKind is ProgressStatusKind.Preparing or ProgressStatusKind.Running;
        if (runningLike)
            StatusMessage = LocalizationManager.T(PhaseKey(phase));

        if (phase == ArchivePhase.Scanning)
        {
            if (runningLike) StatusKind = ProgressStatusKind.Running;
            IsIndeterminate = true;   // 总数未知：进度条滚动而非僵在 0%
            return;
        }

        IsIndeterminate = false;
        ScanCountText = null;
    }

    /// <summary>
    /// 是否为「纯阶段上报」：只带阶段标签、**不含任何进度信息**（引擎
    /// <c>ArchiveProgressPhaseExtensions.ReportPhase</c> 的形态）。
    /// 这类上报只应刷新状态行阶段文案，不得参与主进度 / 速度 / ETA / 字节等计算。
    /// </summary>
    private static bool IsPhaseOnlyReport(ArchiveProgress p) =>
        p.PercentComplete == 0
        && p.ProcessedFiles == 0
        && p.ProcessedBytes == 0
        && !p.EntryStatus.HasValue
        && p.BatchIndex == null
        && string.IsNullOrEmpty(p.EntryKey)
        // 带当前文件名 / 文件大小 / 文件进度的上报不算「纯阶段上报」：
        // 引擎首个 0% 但已能显示文件名的上报必须继续走常规处理，否则文件名被丢弃
        //（详细面板「当前文件」行与简约面板都依赖它）。
        && string.IsNullOrEmpty(p.CurrentFile)
        && p.FileTotalBytes == 0
        && p.FilePercentComplete == null;

    /// <summary>
    /// 简约面板「当前文件」进度条取值：有逐文件百分比时用它，否则回退到总进度。
    /// </summary>
    public int SimpleFileBarValue => _hasFilePercent ? FilePercentComplete : PercentComplete;

    partial void OnPercentCompleteChanged(int value) => OnPropertyChanged(nameof(SimpleFileBarValue));
    partial void OnFilePercentCompleteChanged(int value)
    {
        OnPropertyChanged(nameof(SimpleFileBarValue));
        OnPropertyChanged(nameof(CurrentFileRatio));
    }

    /// <summary>
    /// Update all progress-bound properties from an <see cref="ArchiveProgress"/> report.
    /// Safe to call from any thread; dispatches internally.
    /// </summary>
    public void SetProgress(ArchiveProgress p)
    {
        // 终态之后到达的上报一律丢弃（终态是黏滞状态）。SetProgress 内部要切回 UI 线程，
        // worker 线程的最后几帧进度会排在 SetComplete / SetErrorSummary / Cancel 之后才执行；
        // 若照常处理就会落进下方的「计时基线兜底」RestartClock()，把已冻结的最终耗时
        // 重新从 0 计时、并清空终态统计摘要（用户实测「完成后重置计时、看不到摘要」即此）。
        // 唯一例外：ZIP 的「后置回填」压缩后字节也在末尾几帧里上报——只吸收这一项累加量，
        // 否则「压缩后大小」会因为迟到而被丢掉。
        if (_finalElapsed != null)
        {
            if (p.TotalCompressedBytes > 0)
            {
                _overallCompressedBytes = p.TotalCompressedBytes;
                if (p.TotalBytes > 0)
                    _overallTotalBytes = p.TotalBytes;
                OnPropertyChanged(nameof(HasOverallCompression));
                OnPropertyChanged(nameof(OverallCompressionText));
                NotifyChannelMetaVisibility();
                RefreshCompletionStats();
            }
            return;
        }

        // 首个上报到达 → 结束准备态（不定进度条恢复确定态）
        EndPreparing();

        // 阶段上报（引擎只在阶段边界带 Phase）：先刷状态行文案，再走常规进度处理
        if (p.Phase.HasValue)
        {
            ApplyPhase(p.Phase.Value);
            if (p.Phase.Value == ArchivePhase.Scanning)
                ScanCountText = LocalizationManager.T("Progress_FileCount_Scanning", p.TotalFiles);

            // 纯阶段上报只携带阶段标签（不携带任何进度信息）→ **不得回写主进度**。
            // 否则解压末尾的「正在收尾…」会把已到 100% 的主进度打回 0%，
            // 用户看到进度条倒退（与 2026-10-13「主进度单调不减」的修复意图冲突）。
            if (IsPhaseOnlyReport(p))
                return;
        }

        // Task 6: 批次 upsert 必须在早返回【之前】——EntryStatus 上报同样携带批次身份
        // （ZipEngine.cs:3208-3220 的逐条目终态事件带 BatchIndex/BatchCount），
        // 若被下方早返回拦下，通道行的 Percent/FileRatio 会冻结在中途值。
        if (p.BatchIndex.HasValue)
            UpsertParallelBatch(p);

        // T6: 逐条目终态事件 = 独立通道，置于一切计数/节流逻辑之前，
        // 不参与百分比/速度/ETA/字节/批次计算（早返回，绝不落入下方分支）
        if (p.EntryStatus.HasValue && !string.IsNullOrEmpty(p.EntryKey))
        {
            // fileSize 末参：channel-info 的 FileTotalBytes（逐条目终态字节，供统计卡跳过/出错/已覆盖累加器）
            // compressedBytes 末参：压缩率计划的后置回填逐条目压缩后字节（仅 ZIP 压缩）
            UpdateEntryStatus(p.EntryKey,
                EntryProgressItem.MapEntryStatus(p.EntryStatus.Value), null, p.FileTotalBytes, p.EntryCompressedBytes);
            // T5: 确定性条目结果聚合（红/黄摘要段）——必须在早返回前收集，不落入速度采样路径
            CollectEntryOutcome(p.EntryKey, p.EntryStatus.Value, p.EntryFailureReason, p.EntryErrorDetail, p.EntryErrorHResult);
            return;
        }

        // 非并行报告（无 BatchIndex 且非逐条目终态）：合成/更新单条通道行（IsParallel=false），复用批次行样式。
        // 防御条件：已有并行行时不落单行（并行/非并行报告在切换瞬间可能交错）。
        if (p.BatchIndex == null && p.EntryKey == null
            && !_parallelBatchItems.Any(x => x.IsParallel))
        {
            UpsertSingleChannelRow(p);
        }

        // 操作计时基线兜底（ctor/InitBatchMode 已重置，此处防漏）。
        // 注意：绝不能带 `_finalElapsed != null` 条件 —— 终态后仍会有迟到上报（见方法开头注释），
        // 那样会把冻结的最终耗时重置为 0 并清空摘要。新操作重置走 BeginPreparing / InitBatchMode。
        if (_opStartUtc == default)
            RestartClock();

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
        {
            FilePercentComplete = (int)Math.Clamp(p.FilePercentComplete.Value, 0, 100);
            _hasFilePercent = true;
        }
        else
        {
            // 未携带逐文件百分比（如 7z mt=on 路径）→ 标记未知，简约面板条回退到总进度
            _hasFilePercent = false;
        }
        OnPropertyChanged(nameof(SimpleFileBarValue));

        // 详细面板「当前文件」行右侧信息：
        // 合并/写入阶段（Finalizing）显示合并字节的「已写 / 总量」，其余阶段显示当前文件大小。
        if (_activePhase == ArchivePhase.Finalizing && p.BatchTotalBytes > 0)
        {
            DetailFileInfoText = $"{FormatUtil.FormatSize(p.BatchProcessedBytes)} / {FormatUtil.FormatSize(p.BatchTotalBytes)}";
            // 该阶段即「写出仅存储文件」：顶部通道行不再变化，下方换成「存储文件」小标题 + 该行
            ShowStoreSectionHeader = true;
        }
        else if (p.FileTotalBytes > 0)
        {
            DetailFileInfoText = FormatUtil.FormatSize(p.FileTotalBytes);
        }

        // File count + 批次档案序号（"2 / 5"）
        if (_isBatchMode && _batchItems != null && _batchItems.Count > 0)
        {
            int current = _currentBatchIndex >= 0
                ? Math.Min(_currentBatchIndex + 1, _batchItems.Count)
                : Math.Min((int)p.PercentComplete / 100 * _batchItems.Count, _batchItems.Count);
            if (current < 1) current = 1;
            FileCountText = LocalizationManager.T("Progress_FileCount", current, _batchItems.Count);
            BatchArchiveIndexText = LocalizationManager.T("Progress_Batch_ArchiveOf", current, _batchItems.Count);
            _lastFileCountCurrent = current;
            _lastFileCountTotal = _batchItems.Count;
            _lastFileCountIsBatch = true;
        }
        else
        {
            FileCountText = LocalizationManager.T("Progress_FileCount", 1, 1);
            BatchArchiveIndexText = string.Empty;
            _lastFileCountCurrent = 1;
            _lastFileCountTotal = 1;
            _lastFileCountIsBatch = false;
        }

        // 速度采样（EMA 平滑；tracker 内置 100ms 节流）→ 速度文案
        double speed = _speedTracker.RecordSample(p.ProcessedBytes, DateTime.UtcNow);
        if (speed > 0)
        {
            _lastSpeedBytesPerSec = speed;
            SpeedText = LocalizationManager.T("Progress_Stats_Speed", FormatUtil.FormatSize((long)speed));
        }

        // ETA → 剩余时间（速度无效/总字节未知时清空）
        double? eta = _speedTracker.ComputeEtaSeconds(
            _speedTracker.ProcessedInArchive(p.ProcessedBytes), p.TotalBytes);
        _lastEtaSeconds = eta;
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
        // 已处理：仅引擎上报 TotalFiles 时更新（兼容重载 TotalFiles=0 不得清零）；
        // 卡值/格式化参数用 N/M 分子分母（如 "60/100"）
        if (p.TotalFiles > 0)
        {
            _statsProcessed = p.ProcessedFiles;
            _statsTotalFiles = p.TotalFiles;
            StatsProcessedText = LocalizationManager.T("Progress_Stats_Processed", $"{_statsProcessed}/{_statsTotalFiles}");
            StatsTotalCount = _statsTotalFiles.ToString();
        }

        // 总大小：仅引擎上报 TotalBytes 时更新（只写不清——未上报的引擎保持空串，Rule 6 隐藏）
        if (p.TotalBytes > 0)
        {
            var sizeText = FormatUtil.FormatSize(p.TotalBytes);
            StatsTotalSizeValue = sizeText;
            StatsTotalSizeText = LocalizationManager.T("Progress_Stats_TotalSize", sizeText);
            StatsProcessedSize = FormatUtil.FormatSize(p.ProcessedBytes);
            // 终态摘要用：当前压缩包总字节（批量切换时结算，见 SetCurrentBatchItem）
            _currentArchiveBytes = p.TotalBytes;
        }

        // 整体压缩汇总（压缩率计划）：后置回填，仅 ZIP 压缩完成时上报
        if (p.TotalCompressedBytes > 0)
        {
            _overallCompressedBytes = p.TotalCompressedBytes;
            _overallTotalBytes = p.TotalBytes;
            OnPropertyChanged(nameof(HasOverallCompression));
            OnPropertyChanged(nameof(OverallCompressionText));
            NotifyChannelMetaVisibility();
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
        // 记录并行组数：用于详细面板「压缩通道 · N 组并行」小标题（引擎只给 BatchIndex，不给总数）
        if (p.BatchIndex!.Value + 1 > _channelGroupCount)
        {
            _channelGroupCount = p.BatchIndex.Value + 1;
            UpdateChannelHeaderText();
        }

        // 首个并行批次上报到达前，非批次上报（解压大包预测试状态 / 压缩收集阶段）已合成一条
        // IsParallel=false 单行并占据集合索引 0。IsParallel 是 init 只读，若不清掉，batch 0 会写进
        // 这条非并行行 → XAML 里批次底纹（进度条）、序号、N% 均绑 IsVisible=IsParallel → 第一行
        // 永久无进度条（看似恒 0），且补洞创建使其余行序号从 2 起。故首个并行上报先清空残留单行。
        RemoveSingleChannelRows();

        int idx = p.BatchIndex!.Value;
        while (_parallelBatchItems.Count <= idx)
            _parallelBatchItems.Add(new ParallelBatchProgressItem { Index = _parallelBatchItems.Count + 1, IsParallel = true });

        var row = _parallelBatchItems[idx];
        row.Percent = Math.Clamp(p.BatchPercentComplete ?? p.PercentComplete, 0, 100);
        row.BatchRatio = Math.Clamp(row.Percent / 100.0, 0.0, 1.0);   // 右区批次底纹
        // Task 6: 当前文件字节进度 0..1 → 驱动文件名格底纹宽度（MultiBinding）
        row.FileRatio = Math.Clamp((p.FilePercentComplete ?? 0) / 100.0, 0.0, 1.0);
        // T6: 详细模式行显示批次当前文件名（未上报时保持上次值）
        row.CurrentFile = p.CurrentFile ?? row.CurrentFile;
        // 左区：目录（中间省略）+ 文件名（恒完整）+ 文件大小
        var (dir, name) = ProgressDisplayCalculator.SplitFilePath(row.CurrentFile);
        row.DirectoryText = ProgressDisplayCalculator.MiddleEllipsis(dir);
        row.FileNameText = name;
        row.FileSizeText = p.FileTotalBytes > 0 ? FormatUtil.FormatSize(p.FileTotalBytes) : string.Empty;
        // 右区：合并百分比（进度优先，文件个数括注；任一 null 退化仅百分比，P5）
        row.PctDetailText = p.BatchProcessedFiles.HasValue && p.BatchTotalFiles.HasValue
            ? $"{Math.Round(row.Percent)}% ({p.BatchProcessedFiles.Value}/{p.BatchTotalFiles.Value})"
            : (row.Percent > 0 ? $"{Math.Round(row.Percent)}%" : string.Empty);
        // 右区：信息列（字节进度 · 压缩率）+ ToolTip
        row.InfoText = BuildInfoText(p);
        row.TooltipText = BuildTooltipText(p);
        // T7: 状态色随完成度切换（BrushResourceConverter 消费 StatusBrushName → 批次进度条前景）
        row.StatusBrushName = row.Percent >= 100 ? "ThemeStatusSuccessBrush" : "ThemeProgressFillBrush";
        if (p.BatchProcessedFiles.HasValue && p.BatchTotalFiles.HasValue)
            row.DetailText = LocalizationManager.T("Progress_Batch_FilesProgress",
                p.BatchProcessedFiles.Value, p.BatchTotalFiles.Value);
    }

    /// <summary>
    /// 复位详细面板的分区状态：并行组数归零、「存储文件」小标题隐藏
    /// （新批次 / 新档案不得继承上一轮的通道数与写出阶段标记）。
    /// </summary>
    private void ResetChannelSectionState()
    {
        _channelGroupCount = 0;
        ShowStoreSectionHeader = false;
        UpdateChannelHeaderText();
    }

    /// <summary>
    /// 刷新详细面板「压缩通道」小标题：组数已知（≥2）时附「N 组并行」，否则只显示标题。
    /// </summary>
    private void UpdateChannelHeaderText()
    {
        var title = LocalizationManager.T("Progress_Section_CompressChannels");
        CompressChannelsHeaderText = _channelGroupCount > 1
            ? $"{title} · {LocalizationManager.T("Progress_Section_CompressChannelsGroups", _channelGroupCount)}"
            : title;
    }

    /// <summary>移除残留的非并行单行（并行批次接管详细面板时调用，保证 batch 索引与行索引对齐）。
    /// UpsertSingleChannelRow 仅在「集合内没有任何并行行」时创建单行，故此处移除不会打乱已建批次行。</summary>
    private void RemoveSingleChannelRows()
    {
        for (int i = _parallelBatchItems.Count - 1; i >= 0; i--)
        {
            if (!_parallelBatchItems[i].IsParallel)
                _parallelBatchItems.RemoveAt(i);
        }
    }

    /// <summary>非并行单行 upsert：集合恒保持至多 1 条 IsParallel=false 的行（复用批次行样式，D2/D8）。</summary>
    private void UpsertSingleChannelRow(ArchiveProgress p)
    {
        var existing = _parallelBatchItems.FirstOrDefault(x => !x.IsParallel);
        if (existing == null)
        {
            existing = new ParallelBatchProgressItem { IsParallel = false };
            _parallelBatchItems.Add(existing);
        }
        existing.Percent = Math.Clamp(p.PercentComplete, 0, 100);
        // 单行不填充 BatchRatio/PctDetailText：批次底纹与合并百分比与底部总进度重复（2026-10-10 修订，D2/D8/D10），
        // 右区仅信息列（压缩率全窗口唯一出处）；信息列为空时 ShowRightRegion=false 整右区隐藏。
        existing.FileRatio = Math.Clamp((p.FilePercentComplete ?? 0) / 100.0, 0.0, 1.0);  // 左区文件底纹
        existing.CurrentFile = string.IsNullOrEmpty(p.CurrentFile) ? existing.CurrentFile : p.CurrentFile;
        var (dir, name) = ProgressDisplayCalculator.SplitFilePath(existing.CurrentFile);
        existing.DirectoryText = ProgressDisplayCalculator.MiddleEllipsis(dir);
        existing.FileNameText = name;
        existing.FileSizeText = p.FileTotalBytes > 0 ? FormatUtil.FormatSize(p.FileTotalBytes) : string.Empty;
        existing.InfoText = BuildInfoText(p);   // 右区：字节进度 + 压缩率
        existing.TooltipText = BuildTooltipText(p); // 右区 ToolTip
    }

    /// <summary>右区信息列：字节进度 · 压缩率（缺段省略，规则 6）。</summary>
    private static string BuildInfoText(ArchiveProgress p)
    {
        var parts = new List<string>();
        if (p.BatchTotalBytes > 0)
            parts.Add($"{FormatUtil.FormatSize(p.BatchProcessedBytes)}/{FormatUtil.FormatSize(p.BatchTotalBytes)}");
        if (p.CompressionRatio is { } r) parts.Add($"{r:0.#}%");
        return string.Join(" · ", parts);
    }

    /// <summary>右区 ToolTip：进度 / 字节 / 压缩率（缺段省略行，规则 6）。</summary>
    private static string BuildTooltipText(ArchiveProgress p)
    {
        var lines = new List<string>
        {
            $"{LocalizationManager.T("Progress_Tooltip_Progress")}: {Math.Round(p.PercentComplete)}%",
        };
        if (p.BatchTotalBytes > 0)
            lines.Add($"{LocalizationManager.T("Progress_Tooltip_Bytes")}: {FormatUtil.FormatSize(p.BatchProcessedBytes)}/{FormatUtil.FormatSize(p.BatchTotalBytes)}");
        if (p.CompressionRatio is { } r)
            lines.Add($"{LocalizationManager.T("Progress_Tooltip_Ratio")}: {r:0.#}%");
        return string.Join("\n", lines);
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
            // 分隔符归一化（\→/）：压缩侧 FileScanner 产出的键含反斜杠，而引擎 ZIP 条目键为正斜杠，
            // 归一化后两种来源命中同一行（防后置回填/终态事件新建重复行）。
            _entryIndex[ArchivePath.Normalize(key)] = row;
        }
    }

    /// <summary>清空条目行、key 索引与终态字节累加器（新批次/新操作前调用，三者必须同步清）。</summary>
    public void ClearEntryItems()
    {
        _entryItems.Clear();
        _entryIndex.Clear();
        _statsSkippedBytes = 0;
        _statsFailedBytes = 0;
        _statsOverwrittenBytes = 0;
        StatsSkippedSize = "—";
        StatsFailedSize = "—";
        StatsOverwrittenSize = "—";
        // 压缩率计划：整体压缩汇总随条目行一起清空
        _overallCompressedBytes = 0;
        _overallTotalBytes = 0;
        OnPropertyChanged(nameof(HasOverallCompression));
        OnPropertyChanged(nameof(OverallCompressionText));
        NotifyChannelMetaVisibility();
        // T5: 新批次/新操作不得继承上一操作的失败/跳过聚合（红黄摘要段、整包错误一并清空）
        ClearEntryOutcomes();
    }

    /// <summary>
    /// 按 EntryKey upsert 条目行：命中则更新状态（及可选进度），未命中则新建行
    /// （未播种路径的兜底，兼容 TAR/GZ 渐进模式 D7）。
    /// 查找走 <c>_entryIndex</c> 字典 O(1)——100k 条目线性扫描为 O(n²)，不可接受。
    /// </summary>
    public void UpdateEntryStatus(string entryKey, EntryRowState state, double? percent, long? fileSize = null, long? compressedBytes = null)
    {
        var lookupKey = ArchivePath.Normalize(entryKey);   // 分隔符归一化，与 SeedEntryItems 一致
        if (!_entryIndex.TryGetValue(lookupKey, out var row))
        {
            row = new EntryProgressItem
            {
                EntryKey = entryKey,
                Name = System.IO.Path.GetFileName(entryKey),
                State = EntryRowState.Pending,
            };
            _entryItems.Add(row);
            _entryIndex[lookupKey] = row;
        }

        // 终态字节累加（D4）：同终态重复上报只计一次（检查旧状态）；
        // 字节来源 = fileSize 事件值（前向兼容 FileTotalBytes，非空且 >0）↘ 播种行 Size 回退
        if (state is EntryRowState.Skipped or EntryRowState.Failed or EntryRowState.Overwritten
            && row.State != state)
        {
            var size = fileSize is > 0 ? fileSize.Value : row.Size;
            if (size > 0)
                AccumulateTerminalBytes(state, size);
        }

        // 压缩率计划：后置回填的逐条目压缩后字节（仅 ZIP 压缩完成；0/空不覆盖既有值）
        if (compressedBytes is > 0)
            row.CompressedSize = compressedBytes.Value;

        row.State = state;
        // T9: 行内状态文案（本地化）；Active 返回 null 不赋值——视图层改显 PercentText
        string? statusText = ResolveEntryStatusText(state);
        if (statusText != null)
            row.StatusText = statusText;
        if (percent.HasValue)
            row.Percent = percent.Value;
    }

    /// <summary>按终态类别累加字节并刷新对应统计卡行 3（0 字节保持 "—"：未知不装真，D2 注 2）。</summary>
    private void AccumulateTerminalBytes(EntryRowState state, long size)
    {
        switch (state)
        {
            case EntryRowState.Skipped:
                _statsSkippedBytes += size;
                StatsSkippedSize = FormatUtil.FormatSize(_statsSkippedBytes);
                break;
            case EntryRowState.Failed:
                _statsFailedBytes += size;
                StatsFailedSize = FormatUtil.FormatSize(_statsFailedBytes);
                break;
            case EntryRowState.Overwritten:
                _statsOverwrittenBytes += size;
                StatsOverwrittenSize = FormatUtil.FormatSize(_statsOverwrittenBytes);
                break;
        }
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

    /// <summary>模态弹窗（冲突/密码）弹出：已用时间开始冻结。均在 UI 线程调用，无需加锁。</summary>
    public void BeginModalFreeze()
    {
        _modalFreezeCount++;
        UpdateClockFreezeState();
    }

    /// <summary>模态弹窗关闭：解除冻结（若此时仍处于暂停态，冻结由 IsPaused 延续）。</summary>
    public void EndModalFreeze()
    {
        if (_modalFreezeCount > 0) _modalFreezeCount--;
        UpdateClockFreezeState();
    }

    /// <summary>
    /// 冻结状态机：冻结条件 = 暂停 或 有未结算的模态弹窗。
    /// 进入冻结记起点，退出冻结把本段时长累加进 <see cref="_clockFrozenTotal"/>。
    /// </summary>
    private void UpdateClockFreezeState()
    {
        bool shouldFreeze = IsPaused || _modalFreezeCount > 0;
        if (shouldFreeze && _clockFrozenSinceUtc == null)
        {
            _clockFrozenSinceUtc = DateTime.UtcNow;
        }
        else if (!shouldFreeze && _clockFrozenSinceUtc is { } frozenSince)
        {
            _clockFrozenTotal += DateTime.UtcNow - frozenSince;
            _clockFrozenSinceUtc = null;
        }
    }

    /// <summary>
    /// 新操作起点：重设计时基线，并清空终态冻结值与统计摘要
    /// （同一 VM 被下一次操重复用时必须调用，否则会把上一次的终态摘要带过去）。
    /// </summary>
    private void RestartClock()
    {
        _opStartUtc = DateTime.UtcNow;
        _finalElapsed = null;
        _finishedArchiveBytes = 0;
        _currentArchiveBytes = 0;
        CompletionArchivesText = string.Empty;
        CompletionSpeedText = string.Empty;
        CompletionCompressedText = string.Empty;
    }

    /// <summary>刷新"已用时"文本（code-behind DispatcherTimer 每 1s 调用；未开始时跳过）。
    /// 冻结期间（暂停/模态弹窗）不计入已用时间；**终态后恒显示冻结的最终耗时**（计时器继续 tick 也不会增长）。</summary>
    public void RefreshTimeDisplay()
    {
        // 终态：显示冻结值并直接返回（幂等，多次 tick 结果一致）
        if (_finalElapsed is { } finalElapsed)
        {
            ElapsedText = LocalizationManager.T("Progress_Time_Elapsed",
                ProgressDisplayCalculator.FormatDuration(finalElapsed));
            return;
        }
        if (_opStartUtc == default) return;
        var now = DateTime.UtcNow;
        var elapsed = now - _opStartUtc - _clockFrozenTotal;
        if (_clockFrozenSinceUtc is { } frozenSince)
            elapsed -= now - frozenSince;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        ElapsedText = LocalizationManager.T("Progress_Time_Elapsed",
            ProgressDisplayCalculator.FormatDuration(elapsed));
    }

    /// <summary>
    /// 进入终态：冻结最终耗时并重建终态汇总（完成/失败/取消三条路径共用，幂等）。
    /// 冻结后 <see cref="RefreshTimeDisplay"/> 不再按现实时间重算，解决「完成后已用时间仍在走」。
    /// </summary>
    private void FreezeCompletion()
    {
        if (_finalElapsed != null)
            return; // 幂等：SetComplete → SetErrorSummary 等多次调用只认首次

        if (_opStartUtc != default)
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _opStartUtc - _clockFrozenTotal;
            if (_clockFrozenSinceUtc is { } frozenSince)
                elapsed -= now - frozenSince;
            _finalElapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }

        // 瞬时速度只在运行期有意义：终态由「平均速度」接管，通道行（段 2）的 ⚡ 与中档单行的速度随之隐藏
        SpeedText = string.Empty;

        RefreshTimeDisplay();
        RefreshCompletionStats();
    }

    /// <summary>重算终态汇总（仅在已冻结时生效；批次项状态后续变化时调用可保持计数准确）。</summary>
    private void RefreshCompletionStats()
    {
        if (_finalElapsed == null)
            return;

        CompletionArchivesText = BuildCompletionArchivesText();

        // 压缩后大小：只有 ZIP 压缩会在收尾时后置回填 TotalCompressedBytes，其余引擎整项隐藏
        CompletionCompressedText = IsCompressFlow && _overallCompressedBytes > 0
            ? LocalizationManager.T("Progress_Summary_CompressedSize", FormatUtil.FormatSize(_overallCompressedBytes))
            : string.Empty;

        // 平均速度 = 终态总字节 / 实际耗时（<0.5s 视为过短，不显示虚高值）
        var totalBytes = _finishedArchiveBytes + _currentArchiveBytes;
        CompletionSpeedText = totalBytes > 0 && _finalElapsed.Value.TotalSeconds > 0.5
            ? LocalizationManager.T("Progress_Summary_AvgSpeed",
                FormatUtil.FormatSize((long)(totalBytes / _finalElapsed.Value.TotalSeconds)))
            : string.Empty;
    }

    /// <summary>
    /// 压缩包数量文案：非批次一律 1 个；批次 = 档案总数，其中失败项计入失败数（此时显示 ok/total）。
    /// 只产出统计卡 / 时间行 / 段 2 都拿不到的维度 —— 文件数、用时、体积、压缩率已由它们承担，
    /// 终态再写一遍就是重复信息（用户实测反馈：摘要与统计卡内容重叠）。
    /// </summary>
    private string BuildCompletionArchivesText()
    {
        var isBatch = _isBatchMode && _batchItems is { Count: > 0 };
        int archivesTotal = isBatch ? _batchItems!.Count : 1;
        int archivesFailed = isBatch ? _batchItems!.Count(i => i.Status == BatchItemStatus.Failed) : 0;
        int archivesOk = Math.Max(0, archivesTotal - archivesFailed);

        if (IsCompressFlow)
            return archivesFailed > 0
                ? LocalizationManager.T("Progress_Summary_ArchivesCreatedPartial", archivesOk, archivesTotal)
                : LocalizationManager.T("Progress_Summary_ArchivesCreated", archivesTotal);

        return archivesFailed > 0
            ? LocalizationManager.T("Progress_Summary_ArchivesExtractedPartial", archivesOk, archivesTotal)
            : LocalizationManager.T("Progress_Summary_ArchivesExtracted", archivesTotal);
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
        // 终态：先冻结最终耗时 + 生成统计摘要（再改文案，保证摘要反映最终计数）
        FreezeCompletion();
        PercentComplete = 100;
        FilePercentComplete = 100;
        FileName = LocalizationManager.T("Progress_Done");
        // 完成后清空目录行，防止上一个档案的目录残留显示
        DirName = string.Empty;
        StatusMessage = message;
        IsIndeterminate = false;
        // 终态：扫描计数让位给最终文件计数（终态文案里已有统计摘要）
        ScanCountText = null;
        // 已完成（成功）；失败/取消态不被覆盖（失败测试流会 SetErrorSummary→SetComplete 两步走）
        if (StatusKind is not (ProgressStatusKind.Failed or ProgressStatusKind.Cancelled))
            StatusKind = ProgressStatusKind.Completed;
        // 终态：操作已结束，暂停无意义 → 隐藏暂停按钮
        IsPauseButtonVisible = false;
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
        RestartClock();
        _parallelBatchItems.Clear();
        ResetChannelSectionState();
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

        // 终态之后迟到的批量回调（Dispatcher 背景优先级，可能排在 SetComplete 之后）：
        // 直接丢弃——否则 BeginPreparing 会把窗口从「已完成」拉回「正在准备…」。
        if (_finalElapsed != null)
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
        // 终态摘要：上一个压缩包到此结束，把它上报的总字节结算进累计（新包重新累计；
        // 放在 Pending 守卫之后，避免同一档案被重复调用时重复计入）
        _finishedArchiveBytes += _currentArchiveBytes;
        _currentArchiveBytes = 0;
        _batchItems[index].Status = BatchItemStatus.InProgress;
        _batchItems[index].Progress = 0;
        FileName = _batchItems[index].Name;
        // 切换压缩包时重置文件进度条，避免残留上一个包未置满的脏值
        FilePercentComplete = 0;
        _hasFilePercent = false; // 新档案的文件百分比未知，交由首次上报决定（决定回退还是逐文件）
        // ETA 守卫：档案切换字节基线归零重起 EMA（上一档案累计字节对新档案无意义）
        _speedTracker.OnArchiveSwitch(0, DateTime.UtcNow);
        // 并行批次行随档案切换清空（新档案重新上报 BatchIndex）
        _parallelBatchItems.Clear();
        ResetChannelSectionState();
        // T6: 归档切换后条目行必须为空（新档案的逐条目事件/播种重建行，绝不继承上一档案）
        ClearEntryItems();
        // 目录行残留清理（新档案的 DirName 由下次 SetProgress 填充）
        DirName = string.Empty;
        // 新档案进入准备态（重新显示「正在准备…」+ 不定进度条，直到该档案首次上报）
        BeginPreparing();
    }

    /// <summary>
    /// 冲突解决后改写批处理行的名称（自动重命名 / 用户在冲突对话框里改了包名）。
    /// 只换显示名与完整路径，不动状态、进度与统计。
    /// Safe to call from any thread.
    /// </summary>
    /// <param name="index">批处理项索引</param>
    /// <param name="outputPath">实际写出的压缩包绝对路径</param>
    public void UpdateBatchItemName(int index, string outputPath)
    {
        if (_batchItems == null || index < 0 || index >= _batchItems.Count)
            return;
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        var item = _batchItems[index];
        item.Name = System.IO.Path.GetFileName(outputPath);
        item.FullPath = outputPath;
        // 正在压缩的就是本行时，同步信息区显示的文件名（否则停留在重命名前的旧名）
        if (index == _currentBatchIndex)
            FileName = item.Name;
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
        {
            // T5: 批次行置 Failed 时自动钉住 📌（失败窗口保持打开，D10）
            KeepOpenOnComplete = true;
            _batchItems[index].ErrorMessage = errorMessage;
            StatusKind = ProgressStatusKind.Failed;   // 状态图标转「失败」
        }
        if (status is BatchItemStatus.Skipped or BatchItemStatus.Completed)
        {
            _batchItems[index].Progress = 100;
            // 当前项完成/跳过时，文件进度条同步置满。
            // 引擎不一定发出最终 FilePercentComplete=100（加密 ZIP 的 s7zAccumPct
            // 累积到不了 100、7z 最终报告缺失等），UI 层在此兜底保证显示正确。
            if (index == _currentBatchIndex)
                FilePercentComplete = 100;
        }
        // 终态摘要：若已冻结（批已结束/失败），批次行状态变化要同步刷新「成功 n / 共 m」
        RefreshCompletionStats();
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
        StatusKind = ProgressStatusKind.Failed;   // 完成但含错误 → 状态图标转「失败」
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
        // 终态摘要：批收尾后压缩包成功/总数定稿
        RefreshCompletionStats();
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
    /// Set whole-archive error summary (red segment header, selectable, shown between progress bars and buttons).
    /// Header 恒为红段首行，逐条目红行列在其后。
    /// </summary>
    public void SetErrorSummary(string message, string? detail = null)
    {
        if (string.IsNullOrEmpty(message)) return;
        _wholeArchiveError = message;
        _wholeArchiveErrorDetail = detail;
        RebuildSummaryTexts();
        // 状态行切到失败文案：否则引擎在「准备静默期」（首次进度上报前）抛错时，状态行会
        // 永久停在 BeginPreparing 写入的「正在准备…」（EndPreparing 只在首个 SetProgress 时调用）。
        // 后续若再走 SetComplete/CompleteWithErrors 会覆盖本文案（如「部分失败但整体完成」）。
        _isPreparing = false;
        IsIndeterminate = false;
        StatusMessage = LocalizationManager.T("Status_OperationFailed");
        // 失败终态：操作已结束，暂停无意义 → 隐藏暂停按钮
        // （批中单项失败走 SetBatchItemStatus/OnHasErrorsChanged，不在这里隐藏——批操作仍在继续）
        IsPauseButtonVisible = false;
        // 失败也是终态：冻结耗时并给出统计摘要（取消由 Cancel 命令单独冻结）
        FreezeCompletion();
    }

    // ════════════════════════════════════════════
    //  T5: 确定性条目结果聚合（红/黄摘要段）
    // ════════════════════════════════════════════

    /// <summary>逐条目失败/跳过结果（分类原因 + 可操作提示 + 异常原文，后两者仅供 tooltip）。</summary>
    private readonly record struct EntryOutcome(EntryFailureReason Reason, string? Hint, string? Detail);

    /// <summary>红段：逐条目 Failed（EntryKey 去重，first-wins）。</summary>
    private readonly Dictionary<string, EntryOutcome> _failedEntries = new(StringComparer.Ordinal);

    /// <summary>黄段：压缩端带原因 Skipped（EntryKey 去重，first-wins）。冲突跳过无原因不收集。</summary>
    private readonly Dictionary<string, EntryOutcome> _skippedEntries = new(StringComparer.Ordinal);

    /// <summary>整包级错误（SetErrorSummary 传入，红段首行）。</summary>
    private string? _wholeArchiveError;

    /// <summary>整包级错误的异常原文详情（tooltip）。</summary>
    private string? _wholeArchiveErrorDetail;

    /// <summary>每个摘要段的最大行数（含溢出行；溢出计数 = 总数 - 已展示）。</summary>
    private const int SummaryMaxLines = 20;

    /// <summary>
    /// 收集单条目终态结果：Failed → 红段；Skipped 且带原因 → 黄段。
    /// EntryKey 去重（first-wins），新条目入表才重建摘要文本；同时把原因/详情写入列表行（D8 tooltip）。
    /// </summary>
    private void CollectEntryOutcome(string key, ArchiveEntryStatus status, EntryFailureReason? reason, string? detail, int? hresult)
    {
        EntryFailureReason effectiveReason;
        if (status == ArchiveEntryStatus.Failed)
        {
            effectiveReason = reason ?? EntryFailureReason.Other;
            if (_failedEntries.TryAdd(key, new EntryOutcome(effectiveReason, HintFor(effectiveReason, hresult), detail)))
                RebuildSummaryTexts();
        }
        else if (status == ArchiveEntryStatus.Skipped && reason.HasValue)
        {
            effectiveReason = reason.Value;
            if (_skippedEntries.TryAdd(key, new EntryOutcome(effectiveReason, HintFor(effectiveReason, hresult), detail)))
                RebuildSummaryTexts();
        }
        else
        {
            return; // 非失败/非带原因跳过：不写行 tooltip
        }

        // D8：列表行 tooltip 数据（Failed / 带原因 Skipped）
        if (_entryIndex.TryGetValue(key, out var row))
        {
            row.FailureReasonText = ReasonText(effectiveReason);
            row.FailureHint = HintFor(effectiveReason, hresult);
            row.FailureDetail = detail;
        }
    }

    /// <summary>IoFailed 且有 HResult 时解析 Win32 可操作提示（磁盘满/占用/路径过长），否则 null。</summary>
    private static string? HintFor(EntryFailureReason reason, int? hresult) =>
        reason == EntryFailureReason.IoFailed && hresult.HasValue
            ? IoErrorHintResolver.FromHResult(hresult.Value)
            : null;

    /// <summary>
    /// 重建红/黄摘要文本与派生标志（HasErrors 仅由红段文本驱动，D4；黄段驱动 HasSkippedEntries）。
    /// 合并摘要 = 红段 + 单个 \n + 黄段，空段省略无空行。
    /// </summary>
    private void RebuildSummaryTexts()
    {
        var failedFormat = new Func<string, EntryOutcome, string>((key, o) =>
            LocalizationManager.T("Progress_Failed_EntryFmt", key, ReasonText(o.Reason)));
        var skippedFormat = new Func<string, EntryOutcome, string>((key, o) =>
            LocalizationManager.T("Progress_Skipped_EntryFmt", key, ReasonText(o.Reason)));

        ErrorSummaryText = BuildSummaryText(_wholeArchiveError,
            BuildBreakdownLine(_failedEntries, "Progress_FailedBreakdown_Fmt"), _failedEntries, failedFormat);
        SkippedSummaryText = BuildSummaryText(null,
            BuildBreakdownLine(_skippedEntries, "Progress_SkippedBreakdown_Fmt"), _skippedEntries, skippedFormat);
        ErrorSummaryTooltip = BuildSummaryTooltip(_wholeArchiveError, _wholeArchiveErrorDetail,
            BuildBreakdownLine(_failedEntries, "Progress_FailedBreakdown_Fmt"), _failedEntries, failedFormat);
        SkippedSummaryTooltip = BuildSummaryTooltip(null, null,
            BuildBreakdownLine(_skippedEntries, "Progress_SkippedBreakdown_Fmt"), _skippedEntries, skippedFormat);

        HasErrors = !string.IsNullOrEmpty(ErrorSummaryText);
        HasSkippedEntries = !string.IsNullOrEmpty(SkippedSummaryText);
        HasAnySummary = HasErrors || HasSkippedEntries;
        CombinedSummaryText = (ErrorSummaryText, SkippedSummaryText) switch
        {
            ({ } r, { } s) => r + "\n" + s,
            ({ } r, null) => r,
            (null, { } s) => s,
            _ => null,
        };
    }

    /// <summary>
    /// 构建单段摘要：header（可空）→ 失败原因分布行（可空）→ 条目行；每段封顶
    /// <see cref="SummaryMaxLines"/> 行，溢出时追加本地化「…另 N 项」行并截断。
    /// </summary>
    private static string? BuildSummaryText(string? header, string? breakdown,
        Dictionary<string, EntryOutcome> entries, Func<string, EntryOutcome, string> format)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(header)) lines.Add(header!);
        if (!string.IsNullOrEmpty(breakdown)) lines.Add(breakdown!);
        int shown = 0;
        foreach (var (key, o) in entries)
        {
            if (lines.Count >= SummaryMaxLines - 1 && shown + 1 < entries.Count)
            {
                lines.Add(LocalizationManager.T("Progress_ErrorSummary_More", entries.Count - shown));
                return string.Join("\n", lines);
            }
            lines.Add(format(key, o));
            shown++;
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>
    /// 构建单段摘要 tooltip：与 <see cref="BuildSummaryText"/> 同结构（同 20 行封顶），
    /// 但每行尾附异常原文详情（" —— " 分隔）。无任何详情时返回 null（避免与正文重复）。
    /// </summary>
    private static string? BuildSummaryTooltip(string? header, string? headerDetail, string? breakdown,
        Dictionary<string, EntryOutcome> entries, Func<string, EntryOutcome, string> format)
    {
        var hasDetail = !string.IsNullOrEmpty(headerDetail)
            || entries.Values.Any(o => !string.IsNullOrEmpty(o.Detail) || !string.IsNullOrEmpty(o.Hint));
        if (!hasDetail) return null;

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(header))
            lines.Add(string.IsNullOrEmpty(headerDetail) ? header! : header + "\n" + headerDetail);
        if (!string.IsNullOrEmpty(breakdown)) lines.Add(breakdown!);
        int shown = 0;
        foreach (var (key, o) in entries)
        {
            if (lines.Count >= SummaryMaxLines - 1 && shown + 1 < entries.Count)
            {
                lines.Add(LocalizationManager.T("Progress_ErrorSummary_More", entries.Count - shown));
                break;
            }
            var line = format(key, o);
            if (!string.IsNullOrEmpty(o.Hint)) line += " —— " + o.Hint;
            if (!string.IsNullOrEmpty(o.Detail)) line += " —— " + o.Detail;
            lines.Add(line);
            shown++;
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>
    /// 失败原因分布行：如 `共 3 个失败：权限不足 2 · 数据损坏 1`（按数量降序，同数量按枚举序）。
    /// 无条目时返回 null（纯整包错误不显示分布行）。占 <see cref="SummaryMaxLines"/> 一行配额。
    /// </summary>
    private static string? BuildBreakdownLine(Dictionary<string, EntryOutcome> entries, string formatKey)
    {
        if (entries.Count == 0) return null;
        var parts = entries.Values
            .GroupBy(o => o.Reason)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => (int)g.Key)
            .Select(g => $"{ReasonText(g.Key)} {g.Count()}");
        return LocalizationManager.T(formatKey, entries.Count, string.Join(" · ", parts));
    }

    /// <summary>失败根因 → 本地化文案（Rule 13：禁止硬编码用户可见字符串）。</summary>
    private static string ReasonText(EntryFailureReason reason) => reason switch
    {
        EntryFailureReason.PermissionDenied => LocalizationManager.T("Error_Reason_PermissionDenied"),
        EntryFailureReason.IoFailed => LocalizationManager.T("Error_Reason_IoFailed"),
        EntryFailureReason.CrcMismatch => LocalizationManager.T("Error_Reason_CrcMismatch"),
        _ => LocalizationManager.T("Error_Reason_Other"),
    };

    /// <summary>公开重置：清空红/黄聚合、整包错误与三个摘要文本及其派生标志（新操作前调用）。</summary>
    public void ClearEntryOutcomes()
    {
        _failedEntries.Clear();
        _skippedEntries.Clear();
        _wholeArchiveError = null;
        _wholeArchiveErrorDetail = null;
        ErrorSummaryText = null;
        SkippedSummaryText = null;
        CombinedSummaryText = null;
        ErrorSummaryTooltip = null;
        SkippedSummaryTooltip = null;
        HasErrors = false;
        HasSkippedEntries = false;
        HasAnySummary = false;
    }

    /// <summary>
    /// 声明解压后置开关能力位并镜像初值（spec §11.3）：
    /// openFolderSeed 非 null → 对话框流程，用对话框选择作初值；否则镜像 AppSettings。
    /// 全部为实例级，不回写设置（D17）。
    /// </summary>
    public void SetExtractPostActions(bool openFolder, bool deleteArchive, bool? openFolderSeed, bool? deleteArchiveSeed = null)
    {
        ShowOpenFolderToggle = openFolder;
        ShowDeleteArchiveToggle = deleteArchive;
        if (!openFolder && !deleteArchive) return;   // 能力位关 → 不镜像（保持默认 false）

        var settings = AppSettings.Load();
        OpenFolderAfterExtract = openFolderSeed ?? settings.OpenFolderAfterExtract;
        DeleteArchiveAfterExtract = deleteArchiveSeed ?? settings.DeleteArchiveAfterExtract;
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
