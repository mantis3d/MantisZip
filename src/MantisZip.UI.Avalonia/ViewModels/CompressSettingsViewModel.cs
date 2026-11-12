using System.Collections.ObjectModel;
using Avalonia.Controls;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 压缩设置对话框的 ViewModel。
/// 用户选择格式、压缩级别、输出路径、密码和注释；「开始压缩」经
/// <see cref="TryConfirmAsync"/> 一次产出 <see cref="ConfirmOutcome{T}"/>，
/// 结果由窗口订阅 <see cref="ConfirmCompleted"/> 处理（拒绝留窗 / 确认关窗 / 取消关窗）。
/// </summary>
public partial class CompressSettingsViewModel : ObservableObject
{
    /// <summary>源文件/目录路径列表（可修改，显示用）。</summary>
    public ObservableCollection<string> SelectedPaths { get; } = new();

    /// <summary>本地化字符串字典，XAML 通过 {Binding LocalizedStrings[Key]} 访问。</summary>
    public Dictionary<string, string> LocalizedStrings { get; } = new();

    /// <summary>支持的格式选项列表，绑定到格式 ComboBox。来自 <see cref="CompressionOptionData"/> 共享数据源。</summary>
    public List<string> FormatOptions { get; } = new(CompressionOptionData.ArchiveFormatValues);

    /// <summary>源文件摘要文字，用于界面显示。</summary>
    public string SelectedPathsSummary => SelectedPaths.Count > 0
        ? LocalizationManager.T("Compress_NItemsSelected", SelectedPaths.Count)
        : LocalizationManager.T("Compress_NoFilesSelected");

    /// <summary>
    /// 确认事务的结果出口。窗口在构造时订阅，负责把三态翻译成窗口动作
    /// （<see cref="Rejected{T}"/> 留窗显示原因、<see cref="ConfirmOutcome{T}"/> 的确认/取消关窗）。
    /// 之所以是事件而不是回调布尔值：bool 只说明「点了开始」，真正的输入还得回读本 ViewModel；
    /// 事件把载荷一起交出去，调用方就没有回读窗口状态的机会。
    /// </summary>
    public event Action<ConfirmOutcome<CompressSettings>>? ConfirmCompleted;

    /// <summary>
    /// 由 View 注入的「高级格式选项快照」钩子。动态格式面板（<c>FormatOptionsPanel</c>）的值
    /// 只在窗口里可读，而校验与载荷快照都发生在 VM 内，因此必须在确认事务开始前把它抓进来，
    /// 否则确认用的还是构造时的旧值。
    /// </summary>
    public Action? CaptureFormatOptions { get; set; }

    /// <summary>由 View 设置的提示消息回调（弹窗以窗口为 owner）。参数 (消息, 标题)。</summary>
    public Func<string, string, Task>? ShowMessage { get; set; }

    // ── Output mode ──

    /// <summary>Manual 模式下的输出路径缓存，切换模式不丢失。</summary>
    private string? _cachedManualPath;

    [ObservableProperty]
    private CompressOutputMode _outputMode = CompressOutputMode.Manual;

    /// <summary>输出路径在非 Manual 模式下只读。</summary>
    public bool IsOutputPathReadOnly => OutputMode != CompressOutputMode.Manual;

    /// <summary>浏览按钮仅在 Manual 模式下显示。</summary>
    public bool IsBrowseButtonVisible => OutputMode == CompressOutputMode.Manual;

    /// <summary>文件名 / 扩展名区域仅在 Manual/Combined 模式下可见。</summary>
    public bool IsFileNameSectionVisible => OutputMode != CompressOutputMode.Separate;

    /// <summary>内嵌 QuickPathControl 仅在 Manual 模式下可见（Separate/Combined 自动计算路径）。</summary>
    public bool IsQuickPathVisible => OutputMode == CompressOutputMode.Manual;

    /// <summary>
    /// 「输出目录」分组所在网格行的高度：手动模式给 <c>*</c>（内嵌目录选择器吃掉剩余高度，
    /// 窗口拉高时浏览区跟着变高）；分别/合并模式下分组本身隐藏，行高给 0，避免留一块空洞
    /// （2026-10-30 用户反馈「压缩的输出页高度不是弹性的」）。
    /// </summary>
    public GridLength OutputPickerRowHeight =>
        IsQuickPathVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    /// <summary>输出文件扩展名标签（跟随格式）。</summary>
    public string OutputExtensionLabel => GetFormatExtension();

    /// <summary>输出路径标签文字随模式变化。</summary>
    public string OutputPathLabel => OutputMode switch
    {
        CompressOutputMode.Separate => LocalizationManager.T("Compress_OutputMode_Separate"),
        CompressOutputMode.Combined => LocalizationManager.T("Compress_OutputMode_Combined"),
        _ => LocalizationManager.T("Compress_OutputPath"),
    };

    /// <summary>RadioButton 绑定：Manual 模式。</summary>
    public bool IsManualMode
    {
        get => OutputMode == CompressOutputMode.Manual;
        set { if (value) OutputMode = CompressOutputMode.Manual; }
    }

    /// <summary>RadioButton 绑定：Separate 模式。</summary>
    public bool IsSeparateMode
    {
        get => OutputMode == CompressOutputMode.Separate;
        set { if (value) OutputMode = CompressOutputMode.Separate; }
    }

    /// <summary>RadioButton 绑定：Combined 模式。</summary>
    public bool IsCombinedMode
    {
        get => OutputMode == CompressOutputMode.Combined;
        set { if (value) OutputMode = CompressOutputMode.Combined; }
    }

    [ObservableProperty]
    private string _defaultFormat = "zip";

    /// <summary>Separate 模式下是否保留源文件扩展名（如 "file.txt" → "file.txt.zip"）。</summary>
    [ObservableProperty]
    private bool _keepOriginalExtension;

    /// <summary>本次压缩成功后打开输出所在目录并选中产物（初值镜像设置，不回写；进度窗口底部还能临时改）。</summary>
    [ObservableProperty]
    private bool _openFolderAfterCompress;

    /// <summary>当前格式是否为 ZIP。</summary>
    public bool IsZipFormat => DefaultFormat == "zip";

    /// <summary>当前格式是否为 7z。</summary>
    public bool IsSevenZipFormat => DefaultFormat == "7z";

    /// <summary>当前格式是否支持加密（tar.gz 不支持）。</summary>
    public bool IsFormatEncryptionSupported => DefaultFormat != "tar.gz";

    /// <summary>压缩级别下拉列表（来自共享数据源 CompressionOptionData）。</summary>
    public List<CompressionOptionData.ComboOption> CompressionLevelOptions { get; }

    [ObservableProperty]
    private int _compressionLevel = 5;

    [ObservableProperty]
    private CompressionOptionData.ComboOption? _selectedLevelOption;

    [ObservableProperty]
    private string? _outputPath;

    /// <summary>输出路径的目录部分（内嵌 QuickPathControl 选择）。仅 Manual 模式使用。</summary>
    [ObservableProperty]
    private string? _outputDirectory;

    /// <summary>输出文件名（不含扩展名，扩展名由格式决定）。仅 Manual 模式使用。</summary>
    [ObservableProperty]
    private string? _outputFileName;

    /// <summary>防重入标记：OutputPath ↔ (OutputDirectory+OutputFileName) 双向同步。</summary>
    private bool _isSyncingOutputPath;

    // ── 文件名模板 ──
    // 模板启用时命名由模板渲染决定：Manual/Combined 下渲染「第一个源 + 公共父目录名」，
    // Separate 下每个源各渲染一条。空模板 = 完全走旧命名（逐字节不变）。

    /// <summary>
    /// 文件名模板（占位符形式）。空白 = 未启用。启用时「保留源文件扩展名」失效，要保留请写 <c>{ext}</c>。
    /// </summary>
    [ObservableProperty]
    private string? _fileNameTemplate;

    /// <summary>
    /// 模板里 <c>{date}</c>/<c>{datetime}</c> 用的时间戳：对话框一开就定住，
    /// 免得预览在午夜前后一半新日期一半旧日期（执行侧由会话在批开始时另取一次）。
    /// </summary>
    private readonly DateTime _templateTimestamp = DateTime.Now;

    /// <summary>模板语法/结果错误（已本地化）；无错为 <see langword="null"/>。</summary>
    [ObservableProperty]
    private string? _templateError;

    /// <summary>命名预设下拉的数据源（在对话框里增删，立即写回设置）。</summary>
    public ObservableCollection<FileNameTemplatePreset> TemplatePresetOptions { get; } = new();

    /// <summary>选中的命名预设；选中即把模板填进输入框，之后清空选择。</summary>
    [ObservableProperty]
    private FileNameTemplatePreset? _selectedTemplatePreset;

    /// <summary>是否有可用预设（决定预设下拉是否显示）。</summary>
    [ObservableProperty]
    private bool _hasTemplatePresets;

    /// <summary>新预设的名字（来自输入框，用于「存为预设」）。</summary>
    [ObservableProperty]
    private string? _newPresetName;

    /// <summary>
    /// 选中预设 → 把模板填进输入框，并**保留选中态**。
    /// 早先这里填完就置 null（为了能重复选同一项），结果「删除」按钮永远拿不到
    /// <see cref="SelectedTemplatePreset"/>、点了没反应；改成保留选中，用户手动改模板
    /// 改到与预设不同时再由 <see cref="OnFileNameTemplateChanged"/> 清掉选中。
    /// </summary>
    partial void OnSelectedTemplatePresetChanged(FileNameTemplatePreset? value)
    {
        if (value is null) return;
        if (string.Equals(FileNameTemplate, value.Template, StringComparison.Ordinal)) return;
        FileNameTemplate = value.Template;
    }

    /// <summary>把当前模板存成一个命名预设（同名覆盖，上限 20 条），并写回设置。</summary>
    [RelayCommand]
    private void SaveTemplatePreset()
    {
        if (!IsTemplateActive) return;
        var name = string.IsNullOrWhiteSpace(NewPresetName)
            ? (FileNameTemplate ?? string.Empty)
            : NewPresetName!.Trim();

        var settings = AppSettings.Load();
        if (!settings.AddFileNameTemplatePreset(new FileNameTemplatePreset(name, FileNameTemplate!)))
        {
            TemplateError = LocalizationManager.T("Compress_FileNameTemplate_PresetLimit",
                AppSettings.MaxFileNameTemplatePresets);
            return;
        }

        settings.Save();
        ReloadTemplatePresets(settings);
        NewPresetName = null;
    }

    /// <summary>删除选中的命名预设并写回设置。</summary>
    [RelayCommand]
    private void DeleteTemplatePreset()
    {
        if (SelectedTemplatePreset is null) return;
        var settings = AppSettings.Load();
        settings.RemoveFileNameTemplatePreset(SelectedTemplatePreset.Name);
        settings.Save();
        ReloadTemplatePresets(settings);
    }

    /// <summary>把预设列表同步进下拉（保持与 settings.json 一致）。</summary>
    private void ReloadTemplatePresets(AppSettings settings)
    {
        TemplatePresetOptions.Clear();
        foreach (var preset in settings.FileNameTemplatePresets)
            TemplatePresetOptions.Add(preset);
        HasTemplatePresets = TemplatePresetOptions.Count > 0;
    }

    [ObservableProperty]
    private string? _password;

    [ObservableProperty]
    private string? _confirmPassword;

    [ObservableProperty]
    private bool _encrypt;

    [ObservableProperty]
    private string? _comment;

    [ObservableProperty]
    private CommentDistribution _commentDistribution = CommentDistribution.AllSame;

    /// <summary>ZIP 加密方法（共享数据源）。</summary>
    public List<CompressionOptionData.ComboOption> ZipEncryptionMethodOptions { get; }

    [ObservableProperty]
    private string _zipEncryptionMethod = "aes256";

    [ObservableProperty]
    private CompressionOptionData.ComboOption? _selectedZipEncryptionMethodOption;

    [ObservableProperty]
    private bool _sevenZipEncryptHeaders;

    // ── 高级格式选项（仅本次压缩生效）──
    // 由 View 在对话框关闭时从 DynamicFormatOptionsPanel 快照到此处；
    // 构造函数中从 AppSettings 读取当前默认值，保证对话框初始值与设置窗口一致。
    // 不再写回 AppSettings —— 避免压缩设置污染设置窗口的全局默认值。

    /// <summary>ZIP 文件名编码："utf-8" / "gbk" / "default"。</summary>
    public string FileNameEncoding { get; set; } = "utf-8";

    /// <summary>ZIP 压缩方法：""=默认（Deflate），或 "deflate64" / "bzip2" / "lzma" / "ppmd" / "store"。</summary>
    public string ZipCompressionMethod { get; set; } = "deflate";

    /// <summary>7z 压缩方法："LZMA" / "LZMA2" / "PPMd" / "BZip2" / "Deflate"。</summary>
    public string SevenZipCompressionMethod { get; set; } = "LZMA2";

    /// <summary>7z 固实压缩标志。</summary>
    public bool SevenZipSolid { get; set; } = true;

    /// <summary>7z 固实块大小：""=默认，或 "64m" / "256m" / "512m" / "1g"。</summary>
    public string SevenZipSolidBlockSize { get; set; } = "";

    /// <summary>7z 字典大小：""=默认，或 "64m" / "256m" / "1g" / "4g"（CustomParameters["d"] 直传 7z.dll）。</summary>
    public string SevenZipDictionarySize { get; set; } = "";

    /// <summary>7z Word Size（快速字节数），0 表示引擎默认。</summary>
    public int SevenZipNumFastBytes { get; set; }

    /// <summary>7z 匹配器：""=默认，或 "bt2" / "bt3" / "bt4"。</summary>
    public string SevenZipMatchFinder { get; set; } = "";

    /// <summary>7z 多线程压缩（mt=on）。默认 true，利用多核 CPU 并行压缩。</summary>
    public bool SevenZipMultithreaded { get; set; } = true;

    /// <summary>自适应压缩。启用时对已压缩文件自动 Store，其余保持用户选定级别。</summary>
    public bool AdaptiveCompression { get; set; }

    /// <summary>魔数检测。启用时对大文件进行魔数采样检测，更准确识别已压缩格式。</summary>
    public bool AdaptiveSmartDetect { get; set; }

    /// <summary>ZIP 多线程压缩。启用时利用多核 CPU 并行压缩已分组的文件。</summary>
    public bool MultiThreadedCompression { get; set; }

    // ── 分卷 ──

    /// <summary>分卷大小选项（共享数据源）。</summary>
    public List<CompressionOptionData.ComboOption> SplitSizeOptions { get; }

    [ObservableProperty]
    private CompressionOptionData.ComboOption? _selectedSplitSizeOption;

    /// <summary>自定义分卷大小文本（仅自定义模式可用）。</summary>
    [ObservableProperty]
    private string _customSplitSizeText = "";

    /// <summary>是否显示自定义分卷大小输入框。</summary>
    public bool IsCustomSplitSizeVisible => SelectedSplitSizeOption?.Tag == "-1";

    /// <summary>当前分卷大小（字节），0 表示不分卷。</summary>
    public long SplitSize
    {
        get
        {
            if (SelectedSplitSizeOption == null) return 0;
            var tag = SelectedSplitSizeOption.Tag;
            if (tag == "0") return 0;
            if (tag == "-1")
            {
                if (long.TryParse(CustomSplitSizeText, out var mb) && mb > 0)
                    return mb * 1024L * 1024L;
                return 0;
            }
            if (long.TryParse(tag, out var bytes))
                return bytes;
            return 0;
        }
    }

    // ── 文件冲突 ──

    /// <summary>文件冲突默认策略（ask/overwrite/add/rename/skip），仅本次压缩生效。</summary>
    [ObservableProperty]
    private string _conflictAction = "ask";

    /// <summary>冲突策略下拉选项（共享数据源，本地化 Display）。</summary>
    public List<CompressionOptionData.ComboOption> ConflictActionOptions { get; }

    [ObservableProperty]
    private CompressionOptionData.ComboOption? _selectedConflictActionOption;

    // -- Password mode (library vs new password)

    [ObservableProperty]
    private bool _isPasswordLibraryMode = true;

    [ObservableProperty]
    private string _passwordSearchText = "";

    [ObservableProperty]
    private Core.PasswordEntry? _selectedPasswordEntry;

    [ObservableProperty]
    private bool _saveToLibrary = true;

    [ObservableProperty]
    private string _passwordDescription = "";

    [ObservableProperty]
    private bool _autoGenerateRules = true;

    [ObservableProperty]
    private string _rulesText = "";

    /// <summary>Filtered list of password library entries.</summary>
    public ObservableCollection<Core.PasswordEntry> FilteredPasswordEntries { get; } = new();

    /// <summary>Password strength as numeric value 0-4 for visual indicator.</summary>
    public int PasswordStrengthValue
    {
        get
        {
            if (string.IsNullOrEmpty(Password))
                return -1;
            return GetPasswordStrength(Password);
        }
    }

    /// <summary>Visual password strength indicator (●●●●).</summary>
    public string PasswordStrengthIndicator
    {
        get
        {
            if (string.IsNullOrEmpty(Password))
                return "○○○○";
            int strength = GetPasswordStrength(Password);
            return new string('●', Math.Max(1, strength)).PadRight(4, '○');
        }
    }

    [ObservableProperty]
    private string _windowTitle = LocalizationManager.T("Compress_Title");

    // -- Comment radio button backing (sync via partial methods)

    [ObservableProperty]
    private bool _commentAllSame = true;

    [ObservableProperty]
    private bool _commentFirstOnly;

    [ObservableProperty]
    private bool _commentPerLine;

    // ── Preview tree ──

    /// <summary>预览树的根节点。</summary>
    [ObservableProperty]
    private PreviewTreeNode? _previewRoot;

    /// <summary>预览面板是否启用精简模式。</summary>
    [ObservableProperty]
    private bool _previewCompactMode = true;

    /// <summary>是否显示过滤项。</summary>
    [ObservableProperty]
    private bool _showFilteredGhosts;

    /// <summary>预览树是否正在后台构建（构建超过阈值后置 true，驱动加载覆层显示）。</summary>
    [ObservableProperty]
    private bool _isPreviewBuilding;

    /// <summary>
    /// 预览树构建是否进行中（无论快慢，构建开始即置位，驱动"开始压缩"按钮门禁）。
    /// 与 <see cref="IsPreviewBuilding"/>（仅慢构建 ≥250ms 置位，驱动加载覆层）不同：
    /// 快构建也会短暂置位，保证 B 数据集未就绪时用户无法点击压缩（预览 = 实际 的守卫）。
    /// </summary>
    [ObservableProperty]
    private bool _isBuildPending;

    partial void OnIsBuildPendingChanged(bool value) => UpdateCanCompress();

    /// <summary>当前在途预览构建任务（供 StartCompress 等待 B 数据集就绪）。</summary>
    private Task<CompressPlan?>? _pendingBuildTask;

    /// <summary>等待准备期间用户取消的标记（防「已取消仍启动压缩」竞态）。</summary>
    private bool _compressCancelled;

    /// <summary>最近一次预览构建的错误信息（构建失败时记录，供准备失败提示）；null = 无错误。</summary>
    public string? LastBuildError { get; private set; }

    /// <summary>是否正在等待压缩准备（B 数据集构建中）：驱动按钮文案与防重入。</summary>
    [ObservableProperty]
    private bool _isPreparingCompress;

    partial void OnIsPreparingCompressChanged(bool value)
    {
        OnPropertyChanged(nameof(StartCompressText));
        UpdateCanCompress();
    }

    /// <summary>"开始压缩"按钮文案：等待准备时显示"正在准备…"。</summary>
    public string StartCompressText => IsPreparingCompress
        ? LocalizationManager.T("Compress_Preparing")
        : LocalizationManager.T("Compress_Start");

    /// <summary>预览树构建进度（0–100，-1 表示不确定进度/不定进度条）。</summary>
    [ObservableProperty]
    private double _previewBuildProgress = -1;

    /// <summary>预览树构建版本号，用于丢弃过期异步结果。</summary>
    private int _previewBuildVersion;

    private const int ShallowMaxDepth = 1;
    private const int ShallowMaxWidthPerDir = 20;

    /// <summary>按源缓存：源路径 → 最近一次构建结果（浅层或全量）。</summary>
    private readonly Dictionary<string, ResultPreviewService.SourceSubtree> _sourceCache = new();

    /// <summary>缓存对应的过滤条件签名；变化则整表失效。</summary>
    private string _cacheFilterSignature = string.Empty;

    /// <summary>在途构建的取消源（新构建取消旧构建）。</summary>
    private CancellationTokenSource? _previewCts;

    /// <summary>上次渐进重组的时刻（节流用）。</summary>
    private DateTime _lastProgressiveAssemble = DateTime.MinValue;

    /// <summary>骨架进度轮询间隔：用户能感知「数字在动」的最小节奏。</summary>
    private static readonly TimeSpan SkeletonProgressInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>渐进装配的最小间隔：与「源完成时的渐进上屏」共用，避免快照过密导致整树反复重建。</summary>
    private const int ProgressiveAssembleIntervalMs = 250;

    /// <summary>骨架进度轮询定时器。Phase B 期间运行，结束/取消/关窗立即停止并解绑 Tick。</summary>
    private DispatcherTimer? _skeletonProgressTimer;

    /// <summary>驱动当前定时器的构建版本号（0 = 无）。只用于「旧构建收尾不许掐掉新构建轮询」。</summary>
    private int _skeletonProgressOwnerVersion;

    /// <summary>各源最新骨架进度（绝对源路径 → 累计文件数/大小）；构建线程写入、UI 线程每 500ms 读取。</summary>
    private readonly Dictionary<string, SkeletonProgress> _skeletonProgressTrackers = new();

    /// <summary>各源在屏上的浅层子树根节点（装配时直接挂到树上，即屏幕上显示的实例）。
    /// 快照到达后会被换成快照根（它才是当前克隆体的源头）。</summary>
    private readonly Dictionary<string, PreviewTreeNode> _skeletonProgressTargets = new();

    /// <summary>各源最新的「骨架期快照」（后台还在枚举时树的部分拷贝）；装配优先用它，让树渐进长出条目。</summary>
    private readonly Dictionary<string, PreviewTreeNode> _skeletonSnapshots = new();

    /// <summary>保护上面两个字典（构建线程写、UI 线程读）。</summary>
    private readonly object _skeletonProgressLock = new();

    /// <summary>文件过滤条件（由 View 在对话框关闭时从 FileFilterEditor 获取并设置）。</summary>
    public FileFilterCriteria? FileFilter { get; set; }

    /// <summary>
    /// 缓存的上一次构建的压缩计划（B 数据集）。由预览构建派生；执行侧不再「读计划再搬参数」，
    /// 而是在确认事务内由 <c>CaptureSettings()</c> 一次快照进不可变载荷 <see cref="CompressSettings"/>。
    /// </summary>
    public CompressPlan? Plan { get; private set; }

    private static string FilterSignature(FileFilterCriteria? f)
    {
        if (f == null || !f.IsActive) return string.Empty;
        return string.Join("|",
            string.Join(",", f.IncludeExtensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            string.Join(",", f.ExcludeExtensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            f.NamePattern ?? string.Empty,
            f.MinSize?.ToString() ?? string.Empty,
            f.MaxSize?.ToString() ?? string.Empty,
            f.MinDate?.Ticks.ToString() ?? string.Empty,
            f.MaxDate?.Ticks.ToString() ?? string.Empty);
    }

    /// <summary>清空按源缓存（过滤变化 / 接管 Plan 时调用）。</summary>
    public void InvalidateSourceCache()
    {
        _sourceCache.Clear();
        _cacheFilterSignature = string.Empty;
    }

    /// <summary>密码与确认密码是否匹配。</summary>
    public bool PasswordsMatch => Password == ConfirmPassword;

    /// <summary>密码强度描述（None / Weak / Medium / Strong）。</summary>
    public string PasswordStrength
    {
        get
        {
            if (string.IsNullOrEmpty(Password))
                return LocalizationManager.T("Compress_Strength_None");

            if (Password.Length < 4)
                return LocalizationManager.T("Compress_Strength_Weak");

            bool hasUpper = Password.Any(char.IsUpper);
            bool hasLower = Password.Any(char.IsLower);
            bool hasDigit = Password.Any(char.IsDigit);
            bool hasSpecial = Password.Any(c => !char.IsLetterOrDigit(c));

            int types = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);

            if (Password.Length >= 12 && types >= 3)
                return LocalizationManager.T("Compress_Strength_Strong");

            if (Password.Length >= 8 && types >= 2)
                return LocalizationManager.T("Compress_Strength_Medium");

            return LocalizationManager.T("Compress_Strength_Weak");
        }
    }

    public CompressSettingsViewModel(IReadOnlyList<string> sourcePaths)
    {
        foreach (var p in sourcePaths)
            SelectedPaths.Add(p);
        SelectedPaths.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedPathsSummary));
            // 源多/少直接决定「智能去根」可不可用，必须在上面的属性通知之后立刻刷新
            NotifyRootModeAvailability();
            UpdateAutoRules();
            BuildCompressPreview();
            if (OutputMode != CompressOutputMode.Manual)
                RefreshOutputPathState();
            else
                TryAutoFillOutputPath();
            // 源列表变了 {name}/{name:auto} 的取值也会变（1 源 = 源名、≥2 源 = 父目录名），
            // 而 TryAutoFillOutputPath 在路径已存在时会早退 —— 必须在这里无条件重渲染一次。
            if (IsTemplateActive)
            {
                TemplateError = ValidateTemplateNow();
                RefreshTemplatedOutputFileName();
            }
            UpdateCanCompress();
        };

        // Populate localized strings
        // Tab 标题：输出设置 / 添加文件 / 压缩设置（原「常规」+「高级」合并）
        LocalizedStrings["Compress_TabOutput"] = LocalizationManager.T("Compress_TabOutput");
        LocalizedStrings["Compress_TabAddFiles"] = LocalizationManager.T("Compress_TabAddFiles");
        LocalizedStrings["Compress_TabCompress"] = LocalizationManager.T("Compress_TabCompress");
        LocalizedStrings["Picker_CheckboxMode"] = LocalizationManager.T("Picker_CheckboxMode");
        LocalizedStrings["Compress_SelectedSources"] = LocalizationManager.T("Compress_SelectedSources");
        LocalizedStrings["Compress_TabGeneral"] = LocalizationManager.T("Compress_TabGeneral");
        LocalizedStrings["Compress_TabAdvanced"] = LocalizationManager.T("Compress_TabAdvanced");
        LocalizedStrings["Compress_VolumeSize"] = LocalizationManager.T("Compress_VolumeSize");
        LocalizedStrings["Compress_TabPassword"] = LocalizationManager.T("Compress_TabPassword");
        LocalizedStrings["Compress_TabComment"] = LocalizationManager.T("Compress_TabComment");
        LocalizedStrings["Compress_TabFilter"] = LocalizationManager.T("Compress_TabFilter");
        LocalizedStrings["Compress_Format"] = LocalizationManager.T("Compress_Format");
        LocalizedStrings["Compress_Level"] = LocalizationManager.T("Compress_Level");
        LocalizedStrings["Compress_OutputMode"] = LocalizationManager.T("Compress_OutputMode");
        LocalizedStrings["Compress_OutputMode_Manual"] = LocalizationManager.T("Compress_OutputMode_Manual");
        LocalizedStrings["Compress_OutputMode_Separate"] = LocalizationManager.T("Compress_OutputMode_Separate");
        LocalizedStrings["Compress_OutputMode_Combined"] = LocalizationManager.T("Compress_OutputMode_Combined");
        LocalizedStrings["Compress_OutputPath"] = LocalizationManager.T("Compress_OutputPath");
        LocalizedStrings["Compress_OutputFileName"] = LocalizationManager.T("Compress_OutputFileName");
        LocalizedStrings["Compress_OutputPlaceholder"] = LocalizationManager.T("Compress_OutputPlaceholder");
        LocalizedStrings["Compress_SourceFiles"] = LocalizationManager.T("Compress_SourceFiles");
        LocalizedStrings["Compress_Archive_Group"] = LocalizationManager.T("Compress_Archive_Group");
        LocalizedStrings["Compress_AddItems"] = LocalizationManager.T("Compress_AddItems");
        LocalizedStrings["Compress_Remove"] = LocalizationManager.T("Compress_Remove");
        LocalizedStrings["Compress_Browse"] = LocalizationManager.T("Compress_Browse");
        LocalizedStrings["Compress_Password"] = LocalizationManager.T("Compress_Password");
        LocalizedStrings["Compress_PasswordPlaceholder"] = LocalizationManager.T("Compress_PasswordPlaceholder");
        LocalizedStrings["Compress_ConfirmPassword"] = LocalizationManager.T("Compress_ConfirmPassword");
        LocalizedStrings["Compress_ConfirmPlaceholder"] = LocalizationManager.T("Compress_ConfirmPlaceholder");
        LocalizedStrings["Compress_ShowPassword"] = LocalizationManager.T("Compress_ShowPassword");
        LocalizedStrings["Compress_EncryptArchive"] = LocalizationManager.T("Compress_EncryptArchive");
        LocalizedStrings["Compress_EncryptionMethod"] = LocalizationManager.T("Compress_EncryptionMethod");
        LocalizedStrings["Compress_ZipEncryption"] = LocalizationManager.T("Compress_ZipEncryption");
        LocalizedStrings["Compress_EncryptHeaders"] = LocalizationManager.T("Compress_EncryptHeaders");
        LocalizedStrings["Compress_Strength"] = LocalizationManager.T("Compress_Strength");
        LocalizedStrings["Compress_Comment"] = LocalizationManager.T("Compress_Comment");
        LocalizedStrings["Compress_CommentPlaceholder"] = LocalizationManager.T("Compress_CommentPlaceholder");
        LocalizedStrings["Compress_CommentHint"] = LocalizationManager.T("Compress_CommentHint");
        LocalizedStrings["Compress_Distribution"] = LocalizationManager.T("Compress_Distribution");
        LocalizedStrings["Compress_Distribute_AllSame"] = LocalizationManager.T("Compress_Distribute_AllSame");
        LocalizedStrings["Compress_Distribute_FirstOnly"] = LocalizationManager.T("Compress_Distribute_FirstOnly");
        LocalizedStrings["Compress_Distribute_PerLine"] = LocalizationManager.T("Compress_Distribute_PerLine");
        LocalizedStrings["Compress_Pwd_Library"] = LocalizationManager.T("Compress_Pwd_Library");
        LocalizedStrings["Compress_Pwd_NewPassword"] = LocalizationManager.T("Compress_Pwd_NewPassword");
        LocalizedStrings["Compress_Pwd_Search"] = LocalizationManager.T("Compress_Pwd_Search");
        LocalizedStrings["Compress_Pwd_NoEntry"] = LocalizationManager.T("Compress_Pwd_NoEntry");
        LocalizedStrings["Compress_Pwd_Selected"] = LocalizationManager.T("Compress_Pwd_Selected");
        LocalizedStrings["Compress_Pwd_EnterPwd"] = LocalizationManager.T("Compress_Pwd_EnterPwd");
        LocalizedStrings["Compress_Pwd_ConfirmPwd"] = LocalizationManager.T("Compress_Pwd_ConfirmPwd");
        LocalizedStrings["Compress_Pwd_Match"] = LocalizationManager.T("Compress_Pwd_Match");
        LocalizedStrings["Compress_Pwd_NoMatch"] = LocalizationManager.T("Compress_Pwd_NoMatch");
        LocalizedStrings["Compress_Pwd_SaveToLibrary"] = LocalizationManager.T("Compress_Pwd_SaveToLibrary");
        LocalizedStrings["Compress_Pwd_UpdateRules"] = LocalizationManager.T("Compress_Pwd_UpdateRules");
        LocalizedStrings["Compress_Pwd_Description"] = LocalizationManager.T("Compress_Pwd_Description");
        LocalizedStrings["Compress_Pwd_DescWatermark"] = LocalizationManager.T("Compress_Pwd_DescWatermark");
        LocalizedStrings["Compress_Pwd_AutoRules"] = LocalizationManager.T("Compress_Pwd_AutoRules");
        LocalizedStrings["Compress_Pwd_Rules"] = LocalizationManager.T("Compress_Pwd_Rules");
        LocalizedStrings["Compress_Pwd_RulesWatermark"] = LocalizationManager.T("Compress_Pwd_RulesWatermark");
        LocalizedStrings["Compress_Start"] = LocalizationManager.T("Compress_Start");
        LocalizedStrings["Compress_Cancel"] = LocalizationManager.T("Compress_Cancel");
        LocalizedStrings["Compress_WhenFileExists"] = LocalizationManager.T("Compress_WhenFileExists");
        LocalizedStrings["Compress_OpenFolderAfter"] = LocalizationManager.T("Compress_OpenFolderAfter");
        LocalizedStrings["Compress_FileNameTemplate"] = LocalizationManager.T("Compress_FileNameTemplate");
        LocalizedStrings["Compress_FileNameTemplate_HelpTitle"] = LocalizationManager.T("Compress_FileNameTemplate_HelpTitle");
        LocalizedStrings["Compress_FileNameTemplate_Placeholder"] = LocalizationManager.T("Compress_FileNameTemplate_Placeholder");
        LocalizedStrings["Compress_FileNameTemplate_HelpTip"] = LocalizationManager.T("Compress_FileNameTemplate_HelpTip");
        LocalizedStrings["Compress_FileNameTemplate_PresetName"] = LocalizationManager.T("Compress_FileNameTemplate_PresetName");
        LocalizedStrings["Compress_FileNameTemplate_SavePreset"] = LocalizationManager.T("Compress_FileNameTemplate_SavePreset");
        LocalizedStrings["Compress_FileNameTemplate_DeletePreset"] = LocalizationManager.T("Compress_FileNameTemplate_DeletePreset");
        LocalizedStrings["Compress_FileNameSection"] = LocalizationManager.T("Compress_FileNameSection");
        LocalizedStrings["Compress_FileNameTemplate_RowLabel"] = LocalizationManager.T("Compress_FileNameTemplate_RowLabel");
        // 「插入占位符」按钮（规则 13：XAML 里的 LocalizedStrings[...] 必须登记，漏了按钮文字空白）
        LocalizedStrings["Compress_FileNameTemplate_Insert"] = LocalizationManager.T("Compress_FileNameTemplate_Insert");
        LocalizedStrings["Compress_FileNameTemplate_InsertTip"] = LocalizationManager.T("Compress_FileNameTemplate_InsertTip");

        // 包内根目录四态（v0.5.8）。同规则 13：漏登记会让整组单选与说明文字空白。
        LocalizedStrings["Compress_RootMode"] = LocalizationManager.T("Compress_RootMode");
        LocalizedStrings["Compress_RootMode_Original"] = LocalizationManager.T("Compress_RootMode_Original");
        LocalizedStrings["Compress_RootMode_Original_Tip"] = LocalizationManager.T("Compress_RootMode_Original_Tip");
        LocalizedStrings["Compress_RootMode_SmartStrip"] = LocalizationManager.T("Compress_RootMode_SmartStrip");
        LocalizedStrings["Compress_RootMode_SmartStrip_Tip"] = LocalizationManager.T("Compress_RootMode_SmartStrip_Tip");
        LocalizedStrings["Compress_RootMode_SmartWrap"] = LocalizationManager.T("Compress_RootMode_SmartWrap");
        LocalizedStrings["Compress_RootMode_SmartWrap_Tip"] = LocalizationManager.T("Compress_RootMode_SmartWrap_Tip");
        LocalizedStrings["Compress_RootMode_AlwaysWrap"] = LocalizationManager.T("Compress_RootMode_AlwaysWrap");
        LocalizedStrings["Compress_RootMode_AlwaysWrap_Tip"] = LocalizationManager.T("Compress_RootMode_AlwaysWrap_Tip");
        LocalizedStrings["Compress_RootMode_WrapName"] = LocalizationManager.T("Compress_RootMode_WrapName");
        LocalizedStrings["Compress_RootMode_WrapName_Watermark"] = LocalizationManager.T("Compress_RootMode_WrapName_Watermark");
        // 注：下拉裁剪说明行（RootModeHint）走 VM 的 LocalizationManager.T，不经 LocalizedStrings 字典，
        // 因此下面三个 key 不需要登记（规则 13 只要求 XAML 里的 LocalizedStrings[...] 必须登记）。

        // 初始化压缩级别下拉选项（共享数据源，本地化 Display）
        CompressionLevelOptions = CompressionOptionData.LevelOptions
            .Select(o => new CompressionOptionData.ComboOption(o.Tag, LocalizationManager.T("Compress_Level_" + o.Tag switch
            {
                "0" => "Store",
                "3" => "Fast",
                "5" => "Normal",
                "9" => "Max",
                _ => "Normal",
            })))
            .ToList();
        SelectedLevelOption = CompressionLevelOptions.FirstOrDefault(
            o => o.Tag == CompressionLevel.ToString());

        // 初始化 ZIP 加密方式下拉选项（共享数据源）
        ZipEncryptionMethodOptions = CompressionOptionData.ZipEncryptionMethods
            .Select(o => new CompressionOptionData.ComboOption(o.Tag, o.Display))
            .ToList();
        SelectedZipEncryptionMethodOption = ZipEncryptionMethodOptions.FirstOrDefault(
            o => o.Tag == ZipEncryptionMethod);

        // 初始化分卷大小下拉选项
        SplitSizeOptions = CompressionOptionData.SplitSizeOptions
            .Select(o => new CompressionOptionData.ComboOption(
                o.Tag,
                o.Tag switch
                {
                    "0" => LocalizationManager.T("Compress_Volume_None"),
                    "-1" => LocalizationManager.T("Compress_Volume_Custom"),
                    _ => o.Display,
                }))
            .ToList();
        SelectedSplitSizeOption = SplitSizeOptions.FirstOrDefault(o => o.Tag == "0");

        // 初始化文件冲突策略下拉选项（每次询问/覆盖/追加到已有包/自动重命名/跳过）
        ConflictActionOptions = new List<CompressionOptionData.ComboOption>
        {
            new CompressionOptionData.ComboOption("ask", LocalizationManager.T("Compress_Conflict_Ask")),
            new CompressionOptionData.ComboOption("overwrite", LocalizationManager.T("CompressConflict_Overwrite")),
            new CompressionOptionData.ComboOption("add", LocalizationManager.T("CompressConflict_Add")),
            new CompressionOptionData.ComboOption("rename", LocalizationManager.T("CompressConflict_AutoRename")),
            new CompressionOptionData.ComboOption("skip", LocalizationManager.T("CompressConflict_Skip")),
        };
        SelectedConflictActionOption = ConflictActionOptions.FirstOrDefault(o => o.Tag == ConflictAction);

        // Load password library
        LoadPasswordLibrary();

        // 从 AppSettings 加载默认值（对齐 WPF LoadDefaultsFromSettings）
        try
        {
            var settings = AppSettings.Load();
            KeepOriginalExtension = settings.KeepOriginalExtension;
            OpenFolderAfterCompress = settings.OpenFolderAfterCompress;
            FileNameEncoding = settings.ZipEncoding ?? "utf-8";
            ZipCompressionMethod = settings.ZipCompressionMethod ?? "deflate";
            SevenZipCompressionMethod = settings.SevenZipCompressionMethod ?? "LZMA2";
            SevenZipSolid = settings.SevenZipSolid;
            SevenZipSolidBlockSize = settings.SevenZipSolidBlockSize ?? "";
            SevenZipDictionarySize = settings.SevenZipDictionarySize;
            SevenZipNumFastBytes = settings.SevenZipNumFastBytes;
            SevenZipMatchFinder = settings.SevenZipMatchFinder ?? "";
            SevenZipMultithreaded = settings.SevenZipMultithreaded;
            AdaptiveCompression = settings.AdaptiveCompression;
            ZipEncryptionMethod = settings.ZipEncryptionMethod ?? "aes256";
            SevenZipEncryptHeaders = settings.SevenZipEncryptHeaders;
            // 文件冲突默认策略：非法值经 OnConflictActionChanged 同步回退到第一项（每次询问）
            ConflictAction = settings.CompressFileConflictAction;

            // 文件名模板默认值（空白 = 未启用）+ 命名预设
            FileNameTemplate = settings.FileNameTemplateDialog;
            ReloadTemplatePresets(settings);

            // 包内根目录默认值：只在选项域内时采用，越界值回退原样（枚举来自 JSON，可能被手改）
            if (Enum.IsDefined(settings.CompressRootMode))
                RootMode = settings.CompressRootMode;
            WrapRoot = settings.CompressRootName;

            // 下拉项要在选完默认值之后建（控件的 SelectedItem 绑定靠选项集合才能解析出当前项）
            RebuildRootModeOptions();
            // 默认值可能落在当前不可用的档位上（比如设置里选了「智能去根」但这次是多源）⇒ 退回原样
            NotifyRootModeAvailability();
            // 默认格式：仅当在合法选项域内时赋值，否则保持 "zip" 并记录日志
            if (FormatOptions.Contains(settings.DefaultFormat))
            {
                DefaultFormat = settings.DefaultFormat;
            }
            else
            {
                App.DebugLog($"[CompressSettingsViewModel] DefaultFormat '{settings.DefaultFormat}' 不在合法选项域内，回退到 'zip'");
            }

            // 默认压缩级别：仅当 LevelOptions 包含该 Tag 时赋值，否则保持 5 并记录日志
            var validLevelTags = CompressionLevelOptions.Select(o => o.Tag).ToHashSet();
            var levelTag = settings.DefaultLevel.ToString();
            if (validLevelTags.Contains(levelTag))
            {
                CompressionLevel = settings.DefaultLevel;
            }
            else
            {
                App.DebugLog($"[CompressSettingsViewModel] DefaultLevel {settings.DefaultLevel} 不在合法选项域内，回退到 5");
            }
        }
        catch { /* 使用默认值 */ }

        // Auto-generate initial password rules from output mode + source paths
        // Must be called after SelectedPaths is populated (the CollectionChanged
        // handler won't fire for items added before it was attached).
        UpdateAutoRules();

        // 自动填充输出路径：CollectionChanged 不会为构造时已添加的路径触发，
        // 需显式调用（对齐 WPF ShowCompressWindow 自动填充输出路径；CLI --compress 依赖此逻辑）
        TryAutoFillOutputPath();

        // Build initial compress preview from source paths。
        // 必须在 TryAutoFillOutputPath 之后调用（旧顺序：BuildCompressPreview 在 TryAutoFillOutputPath
        // 之前，构造时 OutputPath 为空导致 IsOutputPathValid 早退、Plan=null，且路径填充后不再触发重建，
        // 产生「窗口打开但 B 数据集未就绪、按钮却可点」的竞态 → 点击压缩静默退出）。
        BuildCompressPreview();
    }

    /// <summary>
    /// 构建压缩预览树。由构造函数自动调用，也可在源文件变更或过滤条件变化后重新调用。
    /// 原始树构建在后台线程执行，快速操作时通过版本号丢弃过期结果。
    /// </summary>
    /// <param name="filter">文件过滤条件，不为空且 IsActive 时对文件节点标记 IsFilteredOut；
    /// null 时回退到 <see cref="FileFilter"/>（保持 CollectionChanged 等无参调用不丢失过滤）。</param>
    public void BuildCompressPreview(FileFilterCriteria? filter = null)
    {
        // 保存当前构建任务，供 StartCompress 等待 B 数据集就绪（点击后等待在途构建完成再压缩）
        _pendingBuildTask = BuildCompressPreviewCoreAsync(filter ?? FileFilter);
    }

    private async Task<CompressPlan?> BuildCompressPreviewCoreAsync(FileFilterCriteria? filter)
    {
        var version = ++_previewBuildVersion;
        IsBuildPending = true;
        LastBuildError = null;

        // 取消上一次在途构建（已完成源已在 _sourceCache 中保留）
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        if (SelectedPaths.Count == 0)
        {
            PreviewRoot = null; IsPreviewBuilding = false; IsBuildPending = false; Plan = null;
            return null;
        }

        if (!IsOutputPathValid())
        {
            PreviewRoot = new PreviewTreeNode
            {
                Name = LocalizationManager.T("Compress_OutputPathInvalid"),
                FullPath = "", DisplayLabel = LocalizationManager.T("Compress_OutputPathInvalid"), IsExpanded = true
            };
            IsPreviewBuilding = false; IsBuildPending = false; Plan = null;
            return null;
        }

        // 快照输入
        var paths = SelectedPaths.ToList();
        var outputMode = OutputMode;
        var outputPath = OutputPath;
        var format = DefaultFormat;
        var keepOriginalExtension = KeepOriginalExtension;

        // 过滤签名变化 → 整表失效；源集变化 → 清理已移除源的缓存
        var sig = FilterSignature(filter);
        if (sig != _cacheFilterSignature) InvalidateSourceCache();
        _cacheFilterSignature = sig;
        foreach (var stale in _sourceCache.Keys.Where(k => !paths.Contains(k)).ToList())
            _sourceCache.Remove(stale);

        try
        {
            // ── Phase A：浅层（所有源 maxDepth=1 / width=20；已完成源直接命中缓存）──
            var shallowTask = Task.Run(() =>
            {
                foreach (var p in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_sourceCache.TryGetValue(p, out var cached)) continue; // 已有（浅或全）
                    var st = ResultPreviewService.BuildSourceSubtree(p, filter, ShallowMaxDepth, ShallowMaxWidthPerDir, ct);
                    if (st != null) _sourceCache[p] = st;
                }
            }, ct);

            var delayTask = Task.Delay(250);
            if (await Task.WhenAny(shallowTask, delayTask) == delayTask)
            {
                if (version != _previewBuildVersion) return null;
                PreviewBuildProgress = -1;
                IsPreviewBuilding = true; // 浅层慢时才显示覆层；浅层上屏后立即关闭
            }
            await shallowTask;
            if (version != _previewBuildVersion) return null;

            var assembled = await AssembleAsync(paths, outputMode, outputPath, format, keepOriginalExtension, filter, ct);
            if (version != _previewBuildVersion) return null;
            PreviewRoot = assembled.Root;
            IsPreviewBuilding = false;
            _lastProgressiveAssemble = DateTime.UtcNow;

            // ── Phase B：全量（串行逐源；每源完成即渐进上屏）──
            // 此阶段屏幕上仍是浅层树（目录节点显示「加载中」）：用 500ms 定时器把正在构建的源
            // 已枚举的文件数/大小刷到节点上，文案变成「加载中 1637 个文件，2.45GB」；
            // 同时构建线程每满一批文件交出一份快照，装配后让树「一段段长出来」。
            StartSkeletonProgress(version);

            // 快照回调在构建线程触发（深拷贝已在那里完成），这里只负责切到 UI 线程登记 + 触发装配
            void OnSnapshot(string sourcePath, PreviewTreeNode snapshot)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (version != _previewBuildVersion) return;
                    lock (_skeletonProgressLock)
                    {
                        _skeletonSnapshots[sourcePath] = snapshot;
                        // 后续 500ms 的数字要刷到「当前屏上克隆体的源头」——也就是这份快照
                        _skeletonProgressTargets[sourcePath] = snapshot;
                    }
                    _ = AssembleProgressiveAsync();
                });
            }

            async Task AssembleProgressiveAsync()
            {
                // 与源完成时的渐进装配共用同一节流阈值：快照很密时不每个都重组整棵树
                if ((DateTime.UtcNow - _lastProgressiveAssemble).TotalMilliseconds < ProgressiveAssembleIntervalMs) return;
                try
                {
                    var progressive = await AssembleAsync(paths, outputMode, outputPath, format, keepOriginalExtension, filter, ct);
                    if (version != _previewBuildVersion) return;
                    PreviewRoot = progressive.Root;
                    _lastProgressiveAssemble = DateTime.UtcNow;
                }
                catch (OperationCanceledException)
                {
                    // 被新一轮构建取代：静默
                }
                catch (Exception ex)
                {
                    App.DebugLog($"AssembleProgressive failed: {ex.Message}");
                }
            }

            foreach (var p in paths)
            {
                ct.ThrowIfCancellationRequested();
                if (_sourceCache.TryGetValue(p, out var c) && c.IsFull) continue; // 已全量

                var progressSink = new SkeletonProgressSink(sp => RecordSkeletonProgress(p, sp));
                var snapshotSink = new Action<PreviewTreeNode>(snap => OnSnapshot(p, snap));
                var full = await Task.Run(
                    () => ResultPreviewService.BuildSourceSubtree(p, filter, int.MaxValue, int.MaxValue, ct, progressSink, snapshotSink), ct);
                if (version != _previewBuildVersion) return null;
                if (full != null) _sourceCache[p] = full;
                ForgetSkeletonProgress(p); // 该源已全量，后续由全量子树接管显示
                ForgetSkeletonSnapshot(p);

                // 节流：250ms 内的中间重组跳过（最后一次组装必然执行，不丢源）
                if ((DateTime.UtcNow - _lastProgressiveAssemble).TotalMilliseconds >= 250)
                {
                    var progressive = await AssembleAsync(paths, outputMode, outputPath, format, keepOriginalExtension, filter, ct);
                    if (version != _previewBuildVersion) return null;
                    PreviewRoot = progressive.Root;
                    _lastProgressiveAssemble = DateTime.UtcNow;
                }
            }

            // ── Phase C：最终组装（含完整 Plan）──
            var final = await AssembleAsync(paths, outputMode, outputPath, format, keepOriginalExtension, filter, ct);
            if (version != _previewBuildVersion) return null;
            PreviewRoot = final.Root;
            Plan = final.Plan;
            return final.Plan;
        }
        catch (OperationCanceledException)
        {
            return null; // 被新构建取代：静默退出，不做任何赋值
        }
        catch (Exception ex)
        {
            App.DebugLog($"BuildCompressPreview failed: {ex.Message}");
            LastBuildError = ex.Message;
            Plan = null;
            return null;
        }
        finally
        {
            // 骨架进度定时器只由「当前这一轮构建」负责停：被新构建取代的旧构建走这里时
            // owner 已易主，若照停不误会把新构建的轮询掐掉（新构建的 StartSkeletonProgress 已停过旧表）。
            if (_skeletonProgressOwnerVersion == version) StopSkeletonProgress();

            if (version == _previewBuildVersion)
            {
                IsPreviewBuilding = false;
                IsBuildPending = false;
            }
        }
    }

    /// <summary>后台装配预览树（含 stats）+ Plan，仅把结果赋值切回 UI 线程。</summary>
    private async Task<(PreviewTreeNode Root, CompressPlan Plan)> AssembleAsync(
        List<string> paths, CompressOutputMode outputMode, string? outputPath,
        string format, bool keepOriginalExtension, FileFilterCriteria? filter, CancellationToken ct)
    {
        var subtrees = paths.Select(ResolveSubtreeForDisplay).ToList();

        // 模板必须在装配这一刻取快照：预览树里的包名就是最终文件名，用户改模板后这里
        // 重新装配即可看到新名字（节流由 _previewBuildVersion 处理）。
        var template = IsTemplateActive ? FileNameTemplate : null;
        var timestamp = _templateTimestamp;

        // 包内根目录同样取快照：预览树里的层级必须与执行时一致（预览 = 实际）。
        var rootMode = RootMode;
        var wrapRoot = IsWrapActive ? WrapRoot : null;

        return await Task.Run(() => ResultPreviewService.AssembleCompressPreview(
            paths, subtrees, outputMode, outputPath, format, keepOriginalExtension, filter,
            template, timestamp, rootMode, wrapRoot), ct);
    }

    /// <summary>
    /// 取某个源在装配时使用的子树：已全量的原树 → 骨架期快照（树在长）→ 浅层树。
    /// 快照带空 IncludedFiles（只用于显示统计；B 数据集/Plan 由 Phase C 用全量子树重算）。
    /// </summary>
    private ResultPreviewService.SourceSubtree? ResolveSubtreeForDisplay(string sourcePath)
    {
        if (_sourceCache.TryGetValue(sourcePath, out var cached) && cached.IsFull) return cached;
        lock (_skeletonProgressLock)
        {
            if (_skeletonSnapshots.TryGetValue(sourcePath, out var snapshot))
                return new ResultPreviewService.SourceSubtree(snapshot, new List<string>(), IsFull: false);
        }
        return _sourceCache.TryGetValue(sourcePath, out var shallow) ? shallow : null;
    }

    /// <summary>
    /// 开始骨架进度轮询（Phase B 前调用）：为每个仍处于浅层的源登记屏上节点，并启动 500ms 定时器。
    /// 浅层子树节点在装配时被**直接挂到树上**（<c>archiveNode.Children.Add(st.Node)</c>），
    /// 因此这里登记的实例就是用户正看着的那个节点，改它的进度即可实时刷新统计文案。
    /// </summary>
    /// <param name="version">当前预览构建版本号（用于判断定时器归属）。</param>
    private void StartSkeletonProgress(int version)
    {
        StopSkeletonProgress(); // 任何在跑的旧轮询（含被取代的旧构建）先停
        _skeletonProgressOwnerVersion = version;

        int targetCount;
        lock (_skeletonProgressLock)
        {
            foreach (var (sourcePath, subtree) in _sourceCache)
            {
                // 已全量的源不再显示「加载中」，无需计数
                if (!subtree.IsFull) _skeletonProgressTargets[sourcePath] = subtree.Node;
            }
            targetCount = _skeletonProgressTargets.Count;
        }

        if (targetCount == 0) return; // 无可显示进度的源：不启动空定时器（避免无意义唤醒）

        _skeletonProgressTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher.UIThread)
        {
            Interval = SkeletonProgressInterval
        };
        _skeletonProgressTimer.Tick += OnSkeletonProgressTick;
        _skeletonProgressTimer.Start();
    }

    /// <summary>
    /// 停止骨架进度轮询：停表 + 解绑 Tick + 清空进度表（幂等）。
    /// 调用点三处：Phase B 结束/取消/异常（外层 finally，按版本号只停自己那一轮）、窗口关闭（CompressSettingsWindow.OnClosed）。
    /// </summary>
    public void StopSkeletonProgress()
    {
        if (_skeletonProgressTimer != null)
        {
            // DispatcherTimer 由 Dispatcher 持有强引用：不停表 + 不解绑会把本 VM 一起钉住
            _skeletonProgressTimer.Stop();
            _skeletonProgressTimer.Tick -= OnSkeletonProgressTick;
            _skeletonProgressTimer = null;
        }
        _skeletonProgressOwnerVersion = 0;

        lock (_skeletonProgressLock)
        {
            _skeletonProgressTrackers.Clear();
            _skeletonProgressTargets.Clear();
            _skeletonSnapshots.Clear();
        }
    }

    /// <summary>
    /// 定时器把最新骨架进度刷到原始节点之后触发。屏幕上的树是深拷贝（ResultTreeView.RebuildDisplayTree），
    /// 宿主（压缩设置窗口）订阅这里把增量同步到克隆体，否则数字永远停在克隆那一刻。
    /// </summary>
    public event Action? SkeletonProgressUpdated;

    /// <summary>定时器回调（UI 线程）：把各源最新累计值刷到屏上节点，赋值会触发 DirectoryInfoText 变更通知。</summary>
    private void OnSkeletonProgressTick(object? sender, EventArgs e)
    {
        // 持锁只取快照；赋值放到锁外（PropertyChanged → 绑定求值会同步执行，不能占着锁）
        var snapshot = new List<(PreviewTreeNode Node, SkeletonProgress Progress)>();
        lock (_skeletonProgressLock)
        {
            foreach (var (sourcePath, progress) in _skeletonProgressTrackers)
            {
                if (_skeletonProgressTargets.TryGetValue(sourcePath, out var node))
                    snapshot.Add((node, progress));
            }
        }

        if (snapshot.Count == 0) return;
        foreach (var (node, progress) in snapshot)
            node.CurrentSkeletonProgress = progress;

        // 原始节点已更新，通知宿主把同值同步到显示层克隆体
        SkeletonProgressUpdated?.Invoke();
    }

    /// <summary>记录某源的最新骨架进度（构建线程同步调用：只写字典，不碰 UI）。</summary>
    private void RecordSkeletonProgress(string sourcePath, SkeletonProgress progress)
    {
        lock (_skeletonProgressLock)
            _skeletonProgressTrackers[sourcePath] = progress;
    }

    /// <summary>源全量构建完成后丢弃其进度（屏上随即被全量子树替换）。</summary>
    private void ForgetSkeletonProgress(string sourcePath)
    {
        lock (_skeletonProgressLock)
            _skeletonProgressTrackers.Remove(sourcePath);
    }

    /// <summary>源全量构建完成后丢弃其快照（全量子树已经在缓存里，装配优先取它）。</summary>
    private void ForgetSkeletonSnapshot(string sourcePath)
    {
        lock (_skeletonProgressLock)
            _skeletonSnapshots.Remove(sourcePath);
    }

    /// <summary>
    /// 轻量进度接收器：只把最新累计值写进字典。
    /// **不用** <see cref="Progress{T}"/>——它每次 Report 都会 Post 一个回调到同步上下文（UI 线程），
    /// 几万个文件足以塞满 Dispatcher 队列、反而拖慢界面；本功能只需要 500ms 粒度的「最新值」。
    /// </summary>
    private sealed class SkeletonProgressSink : IProgress<SkeletonProgress>
    {
        private readonly Action<SkeletonProgress> _write;

        public SkeletonProgressSink(Action<SkeletonProgress> write) => _write = write;

        public void Report(SkeletonProgress value) => _write(value);
    }

    partial void OnCompressionLevelChanged(int value)
    {
        // Sync the ComboBox selection when CompressionLevel is set programmatically
        if (CompressionLevelOptions is { } options)
            SelectedLevelOption = options.FirstOrDefault(o => o.Tag == value.ToString());
    }

    partial void OnSelectedLevelOptionChanged(CompressionOptionData.ComboOption? value)
    {
        if (value != null && int.TryParse(value.Tag, out var level))
            CompressionLevel = level;
    }

    partial void OnSelectedZipEncryptionMethodOptionChanged(CompressionOptionData.ComboOption? value)
    {
        if (value != null)
            ZipEncryptionMethod = value.Tag;
    }

    partial void OnZipEncryptionMethodChanged(string value)
    {
        if (ZipEncryptionMethodOptions is { } options)
            SelectedZipEncryptionMethodOption = options.FirstOrDefault(o => o.Tag == value);
    }

    partial void OnConflictActionChanged(string value)
    {
        // 同步下拉选中项；非法值回退到第一项（每次询问）
        if (ConflictActionOptions is { } options)
            SelectedConflictActionOption = options.FirstOrDefault(o => o.Tag == value) ?? options.FirstOrDefault();
    }

    partial void OnSelectedConflictActionOptionChanged(CompressionOptionData.ComboOption? value)
    {
        if (value != null)
            ConflictAction = value.Tag;
    }

    partial void OnConfirmPasswordChanged(string? value)
    {
        OnPropertyChanged(nameof(PasswordsMatch));
    }

    partial void OnOutputModeChanged(CompressOutputMode value)
    {
        // Notify all dependent properties (including RadioButton bindings)
        OnPropertyChanged(nameof(IsOutputPathReadOnly));
        OnPropertyChanged(nameof(IsBrowseButtonVisible));
        OnPropertyChanged(nameof(IsFileNameSectionVisible));
        OnPropertyChanged(nameof(IsFileNameRowVisible));
        OnPropertyChanged(nameof(IsQuickPathVisible));
        OnPropertyChanged(nameof(OutputPickerRowHeight));
        OnPropertyChanged(nameof(OutputPathLabel));
        OnPropertyChanged(nameof(IsManualMode));
        OnPropertyChanged(nameof(IsSeparateMode));
        OnPropertyChanged(nameof(IsCombinedMode));
        OnPropertyChanged(nameof(IsOutputFileNameReadOnly));
        // 分别压缩时每包各自一层，套根无意义 ⇒ 能力位随模式变化
        NotifyRootModeAvailability();
        RefreshOutputPathState();
        TemplateError = ValidateTemplateNow();
        UpdateCanCompress();
        if (AutoGenerateRules)
            RefreshAutoRules();
        // 切换输出方式时刷新预览树（不同模式树结构不同）
        BuildCompressPreview();
    }

    partial void OnOutputPathChanged(string? value)
    {
        StartCompressCommand.NotifyCanExecuteChanged();
        if (AutoGenerateRules)
            RefreshAutoRules();
        BuildCompressPreview();

        // 外部设置完整路径（如自动填充/Combined 合成）时，拆分为目录 + 文件名供 UI 展示
        if (!_isSyncingOutputPath && OutputMode == CompressOutputMode.Manual)
        {
            SplitOutputPath(value);
        }
    }

    /// <summary>QuickPathControl 选择目录 → 合成完整输出路径。</summary>
    partial void OnOutputDirectoryChanged(string? value)
    {
        ComposeManualOutputPath();
    }

    /// <summary>文件名输入 → 合成完整输出路径。</summary>
    partial void OnOutputFileNameChanged(string? value)
    {
        ComposeManualOutputPath();
    }

    /// <summary>格式切换 → 重新合成（扩展名变化）。</summary>
    private void OnOutputFileNameOrFormatChanged()
    {
        if (OutputMode == CompressOutputMode.Manual)
            ComposeManualOutputPath();
    }

    // 四态用下拉而不是竖排单选：四个单选要占四行，而其中两档在多数场景下还用不上。
    // 不可用的档位直接从选项里拿掉（而不是置灰），下方用一行灰字说明为什么少了几项 ——
    // 既不占高度，也不会留下「选了却什么都不发生」的死选项。

    /// <summary>下拉选项：按当前源集合与输出模式裁剪后的四态（顺序固定）。</summary>
    public ObservableCollection<Option> RootModeOptions { get; } = new();

    /// <summary>
    /// 下拉当前选中项。写入按 <see cref="Option.Value"/> 里的枚举名解析 ——
    /// 存名字而不是数值，将来枚举加减值不会让已保存的默认值错位，也不受语言影响。
    /// </summary>
    public Option? SelectedRootModeOption
    {
        get => RootModeOptions.FirstOrDefault(o => o.Value == RootMode.ToString());
        set
        {
            if (value is null)
                return;

            if (Enum.TryParse<RootDirectoryMode>(value.Value, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                RootMode = parsed;
            }
        }
    }

    /// <summary>当前档位的解释文字（ComboBox 的 ToolTip：单选时代每个选项自带一句，改下拉后挂到下拉上）。</summary>
    public string RootModeDescription => LocalizationManager.T($"Compress_RootMode_{RootMode}_Tip");

    /// <summary>
    /// 「为什么下拉里少了几项」——被隐掉的档位在两档都可用时为空串（该行不显示、不占高度）。
    /// </summary>
    public string RootModeHint
    {
        get
        {
            if (CanStripRoot && CanWrapRoot)
                return "";

            return LocalizationManager.T(
                !CanStripRoot && !CanWrapRoot ? "Compress_RootMode_Hint_None"
                : !CanStripRoot ? "Compress_RootMode_Hint_StripUnavailable"
                : "Compress_RootMode_Hint_WrapUnavailable");
        }
    }

    /// <summary>提示行是否可见。</summary>
    public bool IsRootModeHintVisible => !(CanStripRoot && CanWrapRoot);

    /// <summary>根目录模式变化 → 根名框显隐变化、说明文字换一句，预览树结构也变了。</summary>
    partial void OnRootModeChanged(RootDirectoryMode value)
    {
        OnPropertyChanged(nameof(SelectedRootModeOption));
        OnPropertyChanged(nameof(RootModeDescription));
        OnPropertyChanged(nameof(IsWrapRootVisible));
        OnPropertyChanged(nameof(IsWrapActive));
        BuildCompressPreview();
    }

    /// <summary>根名变化 → 预览树里那一层的名字立刻跟着变。</summary>
    partial void OnWrapRootChanged(string? value)
    {
        if (IsWrapActive)
            BuildCompressPreview();
    }

    /// <summary>源集合 / 输出模式变化后重建下拉项（源从 1 个变 2 个，「智能去根」就消失了）。</summary>
    private void NotifyRootModeAvailability()
    {
        OnPropertyChanged(nameof(CanStripRoot));
        OnPropertyChanged(nameof(CanWrapRoot));
        OnPropertyChanged(nameof(RootModeHint));
        OnPropertyChanged(nameof(IsRootModeHintVisible));

        RebuildRootModeOptions();

        // 原来选了某一档、后来条件不再满足 ⇒ 静默退回原样，避免留下一个跑不起来的模式。
        var stripGone = RootMode == RootDirectoryMode.SmartStrip && !CanStripRoot;
        var wrapGone = IsWrapActive && !CanWrapRoot;
        if (stripGone || wrapGone)
        {
            RootMode = RootDirectoryMode.Original;
        }

        OnPropertyChanged(nameof(SelectedRootModeOption));
        OnPropertyChanged(nameof(IsWrapRootVisible));
    }

    /// <summary>按可用性重建下拉项；文案走本地化，值与枚举名一致。</summary>
    private void RebuildRootModeOptions()
    {
        RootModeOptions.Clear();
        RootModeOptions.Add(new Option(
            LocalizationManager.T("Compress_RootMode_Original"), nameof(RootDirectoryMode.Original)));

        if (CanStripRoot)
        {
            RootModeOptions.Add(new Option(
                LocalizationManager.T("Compress_RootMode_SmartStrip"), nameof(RootDirectoryMode.SmartStrip)));
        }

        if (CanWrapRoot)
        {
            RootModeOptions.Add(new Option(
                LocalizationManager.T("Compress_RootMode_SmartWrap"), nameof(RootDirectoryMode.SmartWrap)));
            RootModeOptions.Add(new Option(
                LocalizationManager.T("Compress_RootMode_AlwaysWrap"), nameof(RootDirectoryMode.AlwaysWrap)));
        }
    }

    // ── 文件名模板 ──

    /// <summary>模板是否启用（非空且非空白）。UI 用它切「只读文件名 + 示例行」。</summary>
    public bool IsTemplateActive => !string.IsNullOrWhiteSpace(FileNameTemplate);

    /// <summary>
    /// 输出文件名是否只读。非 Manual 模式本来就只读；模板启用时连 Manual 也由模板渲染，
    /// 手改没有意义（会被下一次刷新覆盖），所以一并置只读。
    /// </summary>
    public bool IsOutputFileNameReadOnly => OutputMode != CompressOutputMode.Manual || IsTemplateActive;

    /// <summary>
    /// 模板错误提示是否可见。只在模板启用且校验出问题时为 true ——
    /// 正常时这一格显示普通文件名框，出错时由红框提示原位替换（同一格、不新增行）。
    /// </summary>
    public bool IsTemplateErrorVisible => IsTemplateActive && TemplateError != null;

    /// <summary>
    /// 「文件名」结果行是否可见。分别压缩时每个源各自成包、这一行没有意义要隐藏；
    /// 但模板出错时必须仍然显示（错误提示就渲染在这一格里），否则用户看不到任何反馈。
    /// </summary>
    public bool IsFileNameRowVisible => IsFileNameSectionVisible || IsTemplateErrorVisible;

    // ── 包内根目录（四态） ──

    /// <summary>
    /// 「包内根目录」四态。默认 <see cref="RootDirectoryMode.Original"/> = 与引入本功能前逐字节一致。
    /// </summary>
    [ObservableProperty]
    private RootDirectoryMode _rootMode = RootDirectoryMode.Original;

    /// <summary>加根时使用的根名；空 = 用压缩包文件名去扩展名。</summary>
    [ObservableProperty]
    private string? _wrapRoot;

    /// <summary>
    /// 「智能去除唯一根」是否可用：只有源恰为一个目录时才有「那个目录本身」可去掉。
    /// 多源/单文件/混合时置灰，避免出现「勾了但什么也没发生」。
    /// </summary>
    public bool CanStripRoot => SelectedPaths.Count == 1 && Directory.Exists(SelectedPaths[0]);

    /// <summary>「智能 / 始终添加唯一根」是否可用：分别压缩每包各一层，套根没有意义。</summary>
    public bool CanWrapRoot => OutputMode != CompressOutputMode.Separate;

    /// <summary>根名输入框是否可见（只在真的会加根时显示，避免多一行无意义控件）。</summary>
    public bool IsWrapRootVisible => CanWrapRoot
        && (RootMode == RootDirectoryMode.SmartWrap || RootMode == RootDirectoryMode.AlwaysWrap);

    /// <summary>当前选择是否会真的加根（供预览与提示用）。</summary>
    public bool IsWrapActive => RootMode is RootDirectoryMode.SmartWrap or RootDirectoryMode.AlwaysWrap;

    /// <summary>模板内容变化 → 重渲染（只改显示与校验，不写回设置）。</summary>
    partial void OnFileNameTemplateChanged(string? value)
    {
        // 手动改过模板且与当前选中的预设不一致 ⇒ 取消选中（避免下拉仍显示旧预设名）
        if (SelectedTemplatePreset is { } preset
            && !string.Equals(preset.Template, value, StringComparison.Ordinal))
        {
            SelectedTemplatePreset = null;
        }

        TemplateError = ValidateTemplateNow();
        RefreshTemplatedOutputFileName();
        OnPropertyChanged(nameof(IsTemplateActive));
        OnPropertyChanged(nameof(IsOutputFileNameReadOnly));
        OnPropertyChanged(nameof(IsTemplateErrorVisible));
        OnPropertyChanged(nameof(IsFileNameRowVisible));
        if (IsTemplateActive)
            BuildCompressPreview();
    }

    partial void OnTemplateErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(IsTemplateErrorVisible));
        OnPropertyChanged(nameof(IsFileNameRowVisible));
    }

    /// <summary>
    /// 立刻校验一次模板（语法 + 结果合法性 + 分别压缩下的重名）。
    /// 返回已本地化原因；合法或未启用时为 <see langword="null"/>。
    /// </summary>
    private string? ValidateTemplateNow()
    {
        if (!IsTemplateActive) return null;

        if (!MantisZip.Core.Utils.FileNameTemplate.TryValidateSyntax(FileNameTemplate, out var syntaxError))
            return DescribeTemplateError(syntaxError);

        var paths = SelectedPaths.ToList();
        if (paths.Count == 0) return null; // 没源时先不报，等用户加源

        if (OutputMode == CompressOutputMode.Separate)
        {
            // 分别压缩：每个源都要有区分度，否则两个源会撞到同一个包名。
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var plan = CompressPathPlanner.PlanSeparate(
                paths, DefaultFormat, KeepOriginalExtension, FileNameTemplate, _templateTimestamp);
            foreach (var item in plan)
            {
                var name = Path.GetFileName(item.OutputArchivePath);
                if (!names.Add(name))
                    return LocalizationManager.T("Compress_FileNameTemplate_Duplicate", name);
            }
        }
        else if (CompressPathPlanner.ComputeSingleArchiveName(
                     paths, DefaultFormat, FileNameTemplate, _templateTimestamp) is null)
        {
            // 单包模式只渲染一次：渲染不出来（非法字符 / 空结果 / 超长）就是结果不合法。
            MantisZip.Core.Utils.FileNameTemplate.TryRender(
                FileNameTemplate, BuildTemplateContext(paths), out _, out var renderError);
            return DescribeTemplateError(renderError);
        }

        return null;
    }

    /// <summary>模板渲染上下文（<c>{name:auto}</c> 在分别压缩下恒等于第一个源，故传 null）。</summary>
    private FileNameTemplateContext BuildTemplateContext(IReadOnlyList<string> paths) => new(
        SourcePath: paths[0],
        Sequence: 1,
        Timestamp: _templateTimestamp,
        AutoName: CompressPathPlanner.ResolveAutoName(paths));

    private static string DescribeTemplateError(FileNameTemplateError? error) => error?.Kind switch
    {
        null => LocalizationManager.T("Compress_FileNameTemplate_InvalidResult"),
        FileNameTemplateErrorKind.UnknownPlaceholder =>
            LocalizationManager.T("Compress_FileNameTemplate_UnknownPlaceholder", error.Token ?? ""),
        FileNameTemplateErrorKind.Unterminated =>
            LocalizationManager.T("Compress_FileNameTemplate_Unterminated"),
        _ => LocalizationManager.T("Compress_FileNameTemplate_InvalidResult"),
    };

    /// <summary>
    /// 模板启用时把渲染结果写进「输出文件名」框（只读展示），并重算 Manual 的完整路径。
    /// 模板为空时不碰用户手填的文件名。
    /// </summary>
    private void RefreshTemplatedOutputFileName()
    {
        if (!IsTemplateActive) return;
        if (OutputMode == CompressOutputMode.Separate) return;

        var rendered = CompressPathPlanner.ComputeSingleArchiveName(
            SelectedPaths.ToList(), DefaultFormat, FileNameTemplate, _templateTimestamp);
        if (rendered is null) return;

        var ext = CompressPathPlanner.GetFormatExtension(DefaultFormat);
        var baseName = rendered.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
            ? rendered[..^ext.Length]
            : rendered;

        _isSyncingOutputPath = true;
        try
        {
            OutputFileName = baseName;
            if (OutputMode == CompressOutputMode.Manual
                && !string.IsNullOrEmpty(OutputDirectory)
                && !string.IsNullOrEmpty(baseName))
            {
                OutputPath = Path.Combine(OutputDirectory, baseName + GetFormatExtension());
            }
        }
        finally
        {
            _isSyncingOutputPath = false;
        }
    }

    /// <summary>打开模板帮助（占位符表 + 示例 + 规则）。</summary>
    [RelayCommand]
    private async Task ShowFileNameTemplateHelpAsync()
    {
        if (ShowHelpDialog is null) return;
        await ShowHelpDialog(
            LocalizationManager.T("Compress_FileNameTemplate_HelpTitle"),
            LocalizationManager.T("Compress_FileNameTemplate_Markdown"));
    }

    /// <summary>由 View 提供的帮助弹窗回调（参数：标题、Markdown 正文）。</summary>
    public Func<string, string, Task>? ShowHelpDialog { get; set; }

    /// <summary>
    /// 将完整输出路径拆分为目录 + 文件名（不含扩展名）。
    /// </summary>
    private void SplitOutputPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return;

        _isSyncingOutputPath = true;
        try
        {
            // 去掉扩展名（含 .tar.gz 双段）
            var dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
            var fileName = Path.GetFileName(fullPath);
            foreach (var ext in new[] { ".tar.gz", ".7z", ".zip" })
            {
                if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    fileName = fileName[..^ext.Length];
                    break;
                }
            }
            if (!string.IsNullOrEmpty(fileName))
                OutputFileName = fileName;
            if (!string.IsNullOrEmpty(dir))
                OutputDirectory = dir;
        }
        finally
        {
            _isSyncingOutputPath = false;
        }
    }

    /// <summary>
    /// Manual 模式：由目录 + 文件名 + 格式扩展名合成完整输出路径。
    /// </summary>
    private void ComposeManualOutputPath()
    {
        if (_isSyncingOutputPath) return;
        if (OutputMode != CompressOutputMode.Manual) return;

        // 模板启用时文件名由模板渲染（RefreshTemplatedOutputFileName 负责），
        // 这里再用用户手填的名字合成会把渲染结果盖掉。
        if (IsTemplateActive) return;

        var dir = OutputDirectory;
        var name = OutputFileName;
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
            return;

        _isSyncingOutputPath = true;
        try
        {
            var ext = GetFormatExtension();
            OutputPath = Path.Combine(dir, name + ext);
        }
        finally
        {
            _isSyncingOutputPath = false;
        }
    }

    partial void OnDefaultFormatChanged(string value)
    {
        if (OutputMode == CompressOutputMode.Combined)
            RefreshCombinedPath();

        // tar.gz 不支持加密，切过去时自动取消加密
        if (value == "tar.gz")
            Encrypt = false;

        // Manual 模式：格式切换 → 重新合成输出路径（扩展名变化）
        if (OutputMode == CompressOutputMode.Manual)
            ComposeManualOutputPath();

        OnPropertyChanged(nameof(IsZipFormat));
        OnPropertyChanged(nameof(IsSevenZipFormat));
        OnPropertyChanged(nameof(OutputExtensionLabel));
        if (AutoGenerateRules)
            RefreshAutoRules();
        OnPropertyChanged(nameof(IsFormatEncryptionSupported));
        // 扩展名是模板示例行的一部分，切换格式要重算示例与校验
        RefreshTemplatedOutputFileName();
        TemplateError = ValidateTemplateNow();
        OnPropertyChanged(nameof(IsTemplateErrorVisible));
        // 切换格式时刷新预览树（压缩包扩展名变化）
        BuildCompressPreview();
    }

    partial void OnIsPasswordLibraryModeChanged(bool value)
    {
        if (!value)
        {
            // Switching to new password mode — clear selected entry
            SelectedPasswordEntry = null;
        }
        else
        {
            // Switching to library mode — refresh list
            ApplyPasswordFilter();
        }
        OnPropertyChanged(nameof(IsPasswordLibraryMode));
        OnPropertyChanged(nameof(SaveCheckLabel));
    }

    /// <summary>
    /// 密码库状态文本：未选中时显示"未选定密码"，选中后显示"已选定: {description}"。
    /// </summary>
    public string PasswordLibraryStatusText =>
        SelectedPasswordEntry != null
            ? string.Format(LocalizationManager.T("Compress_Pwd_Selected"), SelectedPasswordEntry.Description)
            : LocalizationManager.T("Compress_Pwd_NoEntry");

    /// <summary>
    /// 保存复选框标签：密码库模式显示"更新匹配规则"，新密码模式显示"保存到密码库"。
    /// 对标 WPF CompressSettingsWindow.Password.cs UpdatePasswordSourceUI。
    /// </summary>
    public string SaveCheckLabel =>
        IsPasswordLibraryMode
            ? LocalizationManager.T("Compress_Pwd_UpdateRules")
            : LocalizationManager.T("Compress_Pwd_SaveToLibrary");

    partial void OnSelectedPasswordEntryChanged(Core.PasswordEntry? value)
    {
        if (value != null)
        {
            // 库模式下密码来自 SelectedPasswordEntry.Password，不写入 Password 属性
            // （对标 WPF: PasswordBox.Password = "" 且 GetActivePassword 返回 _selectedLibraryEntry?.Password）
            PasswordDescription = value.Description;
            // RulesText 不由条目规则覆盖: WPF 在选中条目时不写 PwdRulesBox.Text，
            // 规则始终来自自动规则（AutoGenerateRules 为 true 时由 RefreshAutoRules 生成）
            // 或用户手动输入。选中条目后若 AutoGenerateRules 为 true 则重新生成。
            if (AutoGenerateRules)
                RefreshAutoRules();
        }
        OnPropertyChanged(nameof(PasswordLibraryStatusText));
    }

    partial void OnPasswordChanged(string? value)
    {
        OnPropertyChanged(nameof(PasswordStrength));
        OnPropertyChanged(nameof(PasswordStrengthValue));
        OnPropertyChanged(nameof(PasswordStrengthIndicator));
        OnPropertyChanged(nameof(PasswordsMatch));

        // 用户手动输入密码时，清除密码库选中并自动切换到新密码模式
        // 对标 WPF OnPasswordContentChanged
        if (!string.IsNullOrEmpty(value) && IsPasswordLibraryMode)
        {
            SelectedPasswordEntry = null;
            IsPasswordLibraryMode = false;
        }
    }

    partial void OnAutoGenerateRulesChanged(bool value)
    {
        if (value)
            RefreshAutoRules();
    }

    partial void OnPasswordSearchTextChanged(string value)
    {
        ApplyPasswordFilter();
    }

    partial void OnCommentAllSameChanged(bool value)
    {
        if (value)
        {
            CommentFirstOnly = false;
            CommentPerLine = false;
            CommentDistribution = CommentDistribution.AllSame;
        }
    }

    partial void OnCommentFirstOnlyChanged(bool value)
    {
        if (value)
        {
            CommentAllSame = false;
            CommentPerLine = false;
            CommentDistribution = CommentDistribution.FirstOnly;
        }
    }

    partial void OnCommentPerLineChanged(bool value)
    {
        if (value)
        {
            CommentAllSame = false;
            CommentFirstOnly = false;
            CommentDistribution = CommentDistribution.PerLine;
        }
    }

    partial void OnSelectedSplitSizeOptionChanged(CompressionOptionData.ComboOption? value)
    {
        OnPropertyChanged(nameof(IsCustomSplitSizeVisible));
        OnPropertyChanged(nameof(SplitSize));
    }

    partial void OnCustomSplitSizeTextChanged(string value)
    {
        OnPropertyChanged(nameof(SplitSize));
    }

    /// <summary>
    /// Load all password entries from PasswordManager into the filtered list.
    /// </summary>
    public void LoadPasswordLibrary()
    {
        FilteredPasswordEntries.Clear();
        var entries = PasswordManager.Instance.GetAllPasswords()
            .OrderByDescending(e => e.LastUsed ?? DateTime.MinValue)
            .ToList();
        foreach (var entry in entries)
        {
            FilteredPasswordEntries.Add(entry);
        }
    }

    /// <summary>
    /// Apply search text filter to the password library.
    /// </summary>
    public void ApplyPasswordFilter()
    {
        var allEntries = PasswordManager.Instance.GetAllPasswords()
            .OrderByDescending(e => e.LastUsed ?? DateTime.MinValue);

        FilteredPasswordEntries.Clear();
        foreach (var entry in allEntries)
        {
            if (string.IsNullOrEmpty(PasswordSearchText) ||
                entry.Description.Contains(PasswordSearchText, StringComparison.OrdinalIgnoreCase) ||
                entry.PatternsDisplay.Contains(PasswordSearchText, StringComparison.OrdinalIgnoreCase))
            {
                FilteredPasswordEntries.Add(entry);
            }
        }
    }

    /// <summary>
    /// Refresh auto-rules from output mode + source file paths.
    /// Matches WPF CompressSettingsWindow.Password.cs RefreshAutoRules logic.
    /// Generates rules that match expected output archive file names (e.g. "document*.zip"),
    /// not source file extensions.
    /// </summary>
    public void RefreshAutoRules()
    {
        if (!AutoGenerateRules) return;

        var ext = DefaultFormat == "tar.gz" ? ".tar.gz" : "." + DefaultFormat;

        switch (OutputMode)
        {
            case CompressOutputMode.Manual:
                if (!string.IsNullOrEmpty(OutputPath))
                {
                    var manualName = Path.GetFileNameWithoutExtension(OutputPath);
                    if (!string.IsNullOrEmpty(manualName))
                        RulesText = $"{manualName}*{ext}";
                }
                break;

            case CompressOutputMode.Separate:
                var rules = new List<string>();
                foreach (var src in SelectedPaths)
                {
                    string baseName;
                    if (File.Exists(src))
                        baseName = Path.GetFileNameWithoutExtension(src);
                    else if (Directory.Exists(src))
                        baseName = ArchivePath.GetFileName(src);
                    else
                        continue;
                    rules.Add($"{baseName}*{ext}");
                }
                RulesText = string.Join("\r\n", rules);
                break;

            case CompressOutputMode.Combined:
                var commonParent = App.FindCommonParent(SelectedPaths.ToList());
                if (commonParent != null && !App.IsDriveRoot(commonParent))
                {
                    var archiveName = ArchivePath.GetFileName(commonParent);
                    RulesText = $"{archiveName}*{ext}";
                }
                else if (AllPathsSameDrive(SelectedPaths))
                {
                    // 同盘符但无公共子目录：压缩包名与盘符相同（如 C.zip）
                    var root = Path.GetPathRoot(SelectedPaths[0]) ?? "C:\\";
                    var driveLetter = root.TrimEnd('\\', '/').TrimEnd(':');
                    RulesText = $"{driveLetter}*{ext}";
                }
                break;
        }
    }

    /// <summary>
    /// Get password strength as numeric value 0-4.
    /// </summary>
    public static int GetPasswordStrength(string? pwd)
    {
        if (string.IsNullOrEmpty(pwd)) return -1;
        int score = 0;
        if (pwd.Length >= 8) score++;
        if (pwd.Any(char.IsUpper) && pwd.Any(char.IsLower)) score++;
        if (pwd.Any(char.IsDigit)) score++;
        if (pwd.Any(c => !char.IsLetterOrDigit(c))) score++;
        return Math.Min(score, 4);
    }

    /// <summary>
    /// 获取当前激活的密码（无论库模式还是新密码模式）。
    /// 库模式且选中条目时返回条目密码，否则返回 Password 属性值。
    /// </summary>
    public string? GetActivePassword()
    {
        if (!Encrypt) return null;
        if (IsPasswordLibraryMode && SelectedPasswordEntry != null)
            return SelectedPasswordEntry.Password;
        return Password;
    }

    [RelayCommand]
    private void TogglePasswordMode()
    {
        IsPasswordLibraryMode = !IsPasswordLibraryMode;
    }

    [RelayCommand]
    private void ClearPasswordSearch()
    {
        PasswordSearchText = "";
    }

    private bool CanExecuteStartCompress()
    {
        // 等待准备期间禁用（防重入：B 就绪前的等待窗口内再点一次）
        if (IsPreparingCompress) return false;
        // 预览树构建期间禁用"开始压缩"：B 数据集未就绪时不允许执行（预览 = 实际的守卫）
        if (IsBuildPending) return false;
        if (SelectedPaths.Count == 0) return false;
        return IsOutputPathValid();
    }

    [RelayCommand]
    private async Task StartCompress()
    {
        // 本方法只做「把确认结果翻译成窗口动作」：校验、等待计划就绪、快照都在
        // TryConfirmAsync 里一次做完，真正的压缩由宿主按载荷执行（设置窗口不再自带执行逻辑）。
        var outcome = await TryConfirmAsync();

        switch (outcome)
        {
            case Rejected<CompressSettings> rejected:
                // 拒绝原因必须让用户看见：现状的「两次密码不一致」分支是静默 return，
                // 用户点了「开始压缩」却什么都没发生，正是本次收口要消灭的静默失败。
                if (ShowMessage != null)
                    await ShowMessage(rejected.Reason, LocalizationManager.T("Compress_Title"));
                return;

            case Cancelled<CompressSettings>:
                // 等待计划就绪期间用户点了「取消」：窗口已由 Cancel 关闭，这里不再重复关窗。
                return;

            default:
                // Confirmed：把载荷交给窗口，由窗口关闭自己并把结果回传给宿主。
                ConfirmCompleted?.Invoke(outcome);
                return;
        }
    }

    /// <summary>
    /// 确认本次压缩设置：校验 → 等待（必要时补建）计划就绪 → 把「用户此刻决定的一切」
    /// 快照成不可变载荷。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么确认要返回载荷而不是 bool：</b>旧流程用 <c>CloseAction(true)</c> 表示「确认」，
    /// 调用方随后还得回读本 ViewModel 的成员才知道要压什么——确认与读值之间隔着一段可变的
    /// 活对象，窗口一关或用户改一笔，执行内容就与确认时不一致。把结果收成
    /// <see cref="Confirmed{T}"/> 之后，「确认即事务」：载荷生成的那一刻，输入就固定了。
    /// </para>
    /// <para>
    /// <b>为什么校验不另写一份：</b>这里与 <c>CanExecuteStartCompress</c> 共用同一套判定条件
    /// （<see cref="ValidateForExecution"/>），避免出现「按钮可点但确认被拒」或反过来的
    /// 双份规则漂移。本次重构刻意<b>不</b>改动任何一条既有判定条件，只改「谁能读到结果」。
    /// </para>
    /// <para>
    /// <b>为什么可能返回取消：</b>等待计划构建期间用户有真实的取消路径（<c>Cancel</c> 命令会
    /// 置 <c>_compressCancelled</c> 并关闭窗口）。这条路径必须表达成
    /// <see cref="Cancelled{T}"/>，而不是「拒绝」（拒绝会把原因显示给用户，取消不该弹任何提示）。
    /// 除此之外没有其他取消来源，所以取消分支只会出现在「已开始等待」之后。
    /// </para>
    /// </remarks>
    /// <returns>
    /// <see cref="Confirmed{T}"/> 并携带压缩载荷；<see cref="Rejected{T}"/> 并携带已本地化的
    /// 用户可见原因；<see cref="Cancelled{T}"/> 表示等待期间用户主动取消。
    /// </returns>
    public async Task<ConfirmOutcome<CompressSettings>> TryConfirmAsync()
    {
        // ⓪ 动态格式面板的值只有窗口读得到：先把它们抓进 VM，后面的校验与快照才与用户所见一致。
        CaptureFormatOptions?.Invoke();

        // ① 单一校验谓词：确认与命令可用性用同一套条件（顺序与原 StartCompress 保持一致：
        //    先引擎能力/密码，再源与输出路径，保证用户先看到最该修的那一条）。
        var rejection = ValidateForExecution();
        if (rejection != null)
            return new Rejected<CompressSettings>(rejection);

        IsPreparingCompress = true;
        try
        {
            // ② 等待 B 数据集就绪（预览=实际的守卫）：点击后如有在途构建则等待其完成；
            //    无在途构建且 Plan 无效则补建一次；就绪后才允许确认。
            var ready = await EnsurePlanReadyAsync();

            // ③ 等待期间用户点了「取消」→ 这次确认作废（窗口已关，不能再启动压缩）。
            if (_compressCancelled)
                return new Cancelled<CompressSettings>();

            // ④ 无法准备（构建失败等）：窗口保持打开并给出原因，用户可调整后重试。
            if (!ready)
                return new Rejected<CompressSettings>(LocalizationManager.T("Compress_PrepareFailed"));

            // ⑤ 快照：此后执行器只读这份载荷，不再回读本 ViewModel。
            return new Confirmed<CompressSettings>(CaptureSettings());
        }
        finally
        {
            IsPreparingCompress = false;
            _compressCancelled = false;
        }
    }

    /// <summary>
    /// 本次确认的「不执行」原因；<see langword="null"/> 表示可以执行。
    /// </summary>
    /// <remarks>
    /// 逐条对应现状已有的拦截点，判定条件一字未改：
    /// ① Deflate64 + 分卷 + 非加密（7z 之外没有可靠的 writer）；
    /// ② 加密但没有可用密码（手动输入与密码库都为空）；
    /// ③ 两次输入的密码不一致；
    /// ④ 没有任何源（现状由 <c>CanExecuteStartCompress</c> 挡住按钮）；
    /// ⑤ 输出路径非法（现状同样只在 CanExecute 里判定）。
    /// 返回的是「给用户看的原因」，因此文案一律走本地化。
    /// </remarks>
    /// <returns>已本地化的拒绝原因；可执行时返回 <see langword="null"/>。</returns>
    private string? ValidateForExecution()
    {
        // ① Deflate64 只能由 7z 写出；分卷 + 非加密会回落到不支持 method 9 的串行 writer。
        if (!Encrypt
            && SplitSize > 0
            && string.Equals(ZipCompressionMethod, "deflate64", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationManager.T("Compress_Deflate64Split_Unsupported");
        }

        // ② 加密压缩必须有密码（手动输入或密码库选中条目）；否则引擎会按「未加密」写出，
        //    与 UI 勾选状态不符（历史缺陷：空密码 + 空确认会静默生成未加密包）。
        if (Encrypt && string.IsNullOrEmpty(GetActivePassword()))
            return LocalizationManager.T("Compress_PasswordRequired");

        // ③ 两次输入的密码必须一致（现状是静默 return，用户看不到任何反馈）。
        if (Encrypt && !PasswordsMatch)
            return LocalizationManager.T("Compress_PasswordMismatch");

        // ④ 没有任何源可压。
        if (SelectedPaths.Count == 0)
            return LocalizationManager.T("Compress_NoSources");

        // ⑤ 输出路径非法（Manual 缺路径 / 目录模式目录不可用）。
        if (!IsOutputPathValid())
            return LocalizationManager.T("Compress_OutputPathInvalid");

        // ⑥ 文件名模板不合法（语法错 / 结果非法 / 分别压缩下重名）。模板为空时整步跳过。
        if (IsTemplateActive)
        {
            var templateRejection = ValidateTemplateNow();
            if (templateRejection != null)
                return templateRejection;
        }

        return null;
    }

    /// <summary>
    /// 把确认那一刻的全部执行输入快照成不可变载荷（<see cref="CompressSettings"/>）。
    /// </summary>
    /// <remarks>
    /// 映射逐字段对齐原 <c>CompressFlow.BuildRequest(CompressSettingsViewModel)</c>：源取计划条目
    /// （目录粒度，过滤语义在 <c>IncludedFiles</c> 白名单里）、密码取
    /// <see cref="GetActivePassword"/>（库模式下来自选中条目）、格式取
    /// <see cref="DefaultFormat"/>。集合一律复制成新列表，避免宿主之后对 VM 集合的改动
    /// 反向影响已确认的载荷。
    /// </remarks>
    /// <returns>只读的压缩载荷。</returns>
    private CompressSettings CaptureSettings()
    {
        // 计划已由 EnsurePlanReadyAsync 保证就绪；这里仍做防御性判空，
        // 万一为空就交出空计划，由执行器按「无可执行条目」处理（不在这里抛异常）。
        var items = Plan?.Items ?? Array.Empty<CompressPlanItem>();

        // 源清单：同一源在 Combined 模式下可能出现多次（不同过滤），按源去重后保留首次出现的目标路径。
        var sources = new List<CompressSourceEntry>(items.Count);
        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!seenSources.Add(item.SourcePath))
                continue;

            sources.Add(new CompressSourceEntry(item.SourcePath, item.OutputArchivePath));
        }

        var planEntries = items
            .Select(item => new CompressPlanEntry(item.SourcePath, item.OutputArchivePath, item.IncludedFiles))
            .ToList();

        // 密码回写意图：把「写哪一条、写什么描述、匹配哪些规则」一起冻结，
        // 避免执行期间用户继续编辑描述/规则导致写库内容与确认时不一致。
        var libraryEntry = IsPasswordLibraryMode ? SelectedPasswordEntry : null;
        var passwordWriteback = new CompressPasswordWriteback(
            SaveToLibrary: SaveToLibrary,
            IsLibraryMode: IsPasswordLibraryMode,
            LibraryEntryId: libraryEntry?.Id,
            LibraryEntryPatterns: libraryEntry != null
                ? new List<string>(libraryEntry.Patterns)
                : Array.Empty<string>(),
            LibraryEntryDescription: libraryEntry?.Description,
            Description: PasswordDescription,
            RulesText: RulesText);

        var engineOptions = new CompressEngineOptions(
            ZipCompressionMethod: ZipCompressionMethod,
            ZipEncryptionMethod: ZipEncryptionMethod,
            SevenZipCompressionMethod: SevenZipCompressionMethod,
            SevenZipSolid: SevenZipSolid,
            SevenZipSolidBlockSize: SevenZipSolidBlockSize,
            SevenZipDictionarySize: SevenZipDictionarySize,
            SevenZipNumFastBytes: SevenZipNumFastBytes,
            SevenZipMatchFinder: SevenZipMatchFinder,
            SevenZipMultithreaded: SevenZipMultithreaded,
            SevenZipEncryptHeaders: SevenZipEncryptHeaders,
            FileNameEncoding: FileNameEncoding,
            KeepOriginalExtension: KeepOriginalExtension,
            AdaptiveCompression: AdaptiveCompression,
            AdaptiveSmartDetect: AdaptiveSmartDetect);

        return new CompressSettings(
            Sources: sources,
            OutputMode: OutputMode,
            Format: DefaultFormat,
            CompressionLevel: CompressionLevel,
            EngineOptions: engineOptions,
            OutputPath: OutputPath,
            Encrypt: Encrypt,
            // 密码取「当前生效的密码」：库模式下是选中条目的密码，否则是手输密码。
            Password: GetActivePassword(),
            PasswordWriteback: passwordWriteback,
            Comment: Comment,
            CommentDistribution: CommentDistribution,
            SplitSizeBytes: SplitSize,
            MultiThreaded: MultiThreadedCompression,
            // 设置对话框目前不暴露并行度，GUI 路径一直是引擎默认值（0 = 由引擎决定）；
            // 保留字段是为了将来把并行度也纳入确认载荷，避免它继续留在全局设置里。
            ParallelDegree: 0,
            ConflictAction: ConflictAction,
            Filter: FileFilter,
            Plan: planEntries,
            OpenFolderAfterCompress: OpenFolderAfterCompress,
            FileNameTemplate: IsTemplateActive ? FileNameTemplate : null,
            RootMode: RootMode,
            // 只有真会加根的两种模式才带上根名，避免「原样模式下留着一个以后会突然生效的名字」。
            WrapRoot: IsWrapActive ? WrapRoot : null);
    }

    /// <summary>
    /// 确保 B 数据集（<see cref="Plan"/>）就绪。等待当前在途构建完成；若 Plan 仍无效
    /// 且输入有效则补建一次（覆盖构造时序残留/构建失败后无在途构建的状态）。
    /// 返回 false 表示无法准备（构建失败或输入无效）。
    /// 注：等待期间用户持续变更输入会持续等待新构建（活锁由用户操作驱动，语义合理），
    /// 取消始终可用（见 <see cref="Cancel"/> 的 _compressCancelled 标记）。
    /// </summary>
    private async Task<bool> EnsurePlanReadyAsync()
    {
        bool attemptedRebuild = false;
        while (true)
        {
            var task = _pendingBuildTask;
            if (task != null)
            {
                try { await task; } catch { /* 构建内部已吞异常，以 Plan 状态为准 */ }
                if (_pendingBuildTask != task) continue; // 等待期间有新构建 → 重等最新
            }

            if (Plan != null) return true;
            if (SelectedPaths.Count == 0 || !IsOutputPathValid()) return false;

            // Plan 仍无效且无在途构建：补建一次（不无限重试，避免构建持续失败时死循环）
            if (attemptedRebuild) return false;
            attemptedRebuild = true;
            BuildCompressPreview();
        }
    }

    [RelayCommand]
    private Task Cancel()
    {
        // 等待 B 数据集准备期间用户取消：标记取消，StartCompress 恢复执行时不再启动压缩
        if (IsPreparingCompress)
            _compressCancelled = true;

        // 取消是静默动作：关窗由窗口订阅方处理，不弹任何提示。
        ConfirmCompleted?.Invoke(new Cancelled<CompressSettings>());
        return Task.CompletedTask;
    }

    // ── Source file management ──

    [RelayCommand]
    private void RemoveSelected(object? selectedItem)
    {
        if (selectedItem is string path)
        {
            SelectedPaths.Remove(path);
        }
    }

    private void UpdateAutoRules()
    {
        // Delegate to RefreshAutoRules for output-mode-based rule generation.
        if (AutoGenerateRules)
            RefreshAutoRules();
    }

    // ── Output mode helpers ──

    /// <summary>
    /// 根据当前模式刷新输出路径的显示内容（路径 + 可编辑状态）。
    /// </summary>
    private void RefreshOutputPathState()
    {
        switch (OutputMode)
        {
            case CompressOutputMode.Manual:
                // 回到 Manual：恢复缓存路径；无缓存则清空（避免残留其他模式的说明文本）
                OutputPath = _cachedManualPath;
                _cachedManualPath = null;
                break;

            case CompressOutputMode.Separate:
                // 首次离开 Manual 时缓存路径；之后不再覆盖（避免转两次模式后丢失原始路径）
                _cachedManualPath ??= OutputPath;
                OutputPath = LocalizationManager.T("Compress_SeparateSummary", SelectedPaths.Count);
                break;

            case CompressOutputMode.Combined:
                // 首次离开 Manual 时缓存路径；之后不再覆盖
                _cachedManualPath ??= OutputPath;
                RefreshCombinedPath();
                break;
        }
    }

    /// <summary>
    /// Manual 模式下输出路径为空时，根据源文件自动生成默认路径。
    /// 行为与 <see cref="RefreshCombinedPath"/> 一致但不弹跨盘符警告（保持空路径让用户手动设置）。
    /// </summary>
    private void TryAutoFillOutputPath()
    {
        if (OutputMode != CompressOutputMode.Manual) return;
        if (!string.IsNullOrEmpty(OutputPath)) return;
        if (SelectedPaths.Count == 0) return;

        var commonParent = App.FindCommonParent(SelectedPaths.ToList());
        if (commonParent != null && !App.IsDriveRoot(commonParent))
        {
            var archiveName = ResolveCombinedArchiveName(commonParent);
            var ext = GetFormatExtension();
            OutputPath = System.IO.Path.Combine(commonParent, archiveName + ext);
        }
        else if (AllPathsSameDrive(SelectedPaths))
        {
            var root = Path.GetPathRoot(SelectedPaths[0]) ?? "C:\\";
            var driveLetter = root.TrimEnd('\\', '/').TrimEnd(':');
            var ext = GetFormatExtension();
            OutputPath = System.IO.Path.Combine(root, driveLetter + ext);
        }
        // 跨盘符：保持空路径，用户手动设置

        // 模板启用时用模板重算文件名（否则自动填充会先把「目录名」写进文件名框）。
        // RefreshTemplatedOutputFileName 只在 OutputDirectory 非空时改 OutputPath，
        // 这里路径已由上面填好，行为是「换掉文件名、保留目录」。
        if (IsTemplateActive) RefreshTemplatedOutputFileName();
    }

    /// <summary>
    /// 合并模式（自动/父目录命名）的压缩包主名：模板启用时按模板渲染（<c>{name}</c> 取首源，
    /// <c>{name:auto}</c> 取公共父目录名），否则沿用公共父目录名。
    /// </summary>
    private string ResolveCombinedArchiveName(string commonParent)
    {
        if (IsTemplateActive)
        {
            var rendered = CompressPathPlanner.ComputeSingleArchiveName(
                SelectedPaths.ToList(), DefaultFormat, FileNameTemplate, _templateTimestamp);
            if (rendered is not null)
            {
                var ext = CompressPathPlanner.GetFormatExtension(DefaultFormat);
                return rendered.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                    ? rendered[..^ext.Length]
                    : rendered;
            }
        }

        return ArchivePath.GetFileName(commonParent);
    }

    /// <summary>
    /// 计算 Combined 模式的输出路径：公共父目录下的合并压缩包。
    /// </summary>
    private void RefreshCombinedPath()
    {
        if (SelectedPaths.Count == 0)
        {
            OutputPath = "";
            return;
        }

        var commonParent = App.FindCommonParent(SelectedPaths.ToList());
        if (commonParent != null && !App.IsDriveRoot(commonParent))
        {
            var archiveName = ResolveCombinedArchiveName(commonParent);
            var ext = GetFormatExtension();
            OutputPath = System.IO.Path.Combine(commonParent, archiveName + ext);
        }
        else if (AllPathsSameDrive(SelectedPaths))
        {
            // 同盘符但无公共子目录 — 输出到盘符根目录，压缩包名与盘符相同（如 C:\C.zip）
            var root = Path.GetPathRoot(SelectedPaths[0]) ?? "C:\\";
            var driveLetter = root.TrimEnd('\\', '/').TrimEnd(':');
            var ext = GetFormatExtension();
            OutputPath = System.IO.Path.Combine(root, driveLetter + ext);
        }
        else
        {
            // 跨驱动器 — 回退到手动模式
            OutputMode = CompressOutputMode.Manual;
            // 通知用户
            _ = AppMessageBox.Show(
                LocalizationManager.T("Compress_CombinedUnavailable"),
                LocalizationManager.T("Compress_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 判断所有源路径是否在同一个盘符下。
    /// </summary>
    private static bool AllPathsSameDrive(IEnumerable<string> paths)
    {
        string? drive = null;
        foreach (var p in paths)
        {
            var d = Path.GetPathRoot(p);
            if (string.IsNullOrEmpty(d)) return false;
            if (drive == null)
                drive = d;
            else if (!string.Equals(drive, d, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return drive != null;
    }

    /// <summary>
    /// 从当前选择的格式推断文件扩展名。
    /// </summary>
    private string GetFormatExtension()
    {
        return DefaultFormat switch
        {
            "tar.gz" => ".tar.gz",
            _ => "." + DefaultFormat,
        };
    }

    /// <summary>
    /// 根据源路径推断默认文件名。
    /// </summary>
    private string GetDefaultFileName()
    {
        if (SelectedPaths.Count == 0) return "archive";
        if (SelectedPaths.Count == 1 && File.Exists(SelectedPaths[0]))
            return Path.GetFileNameWithoutExtension(SelectedPaths[0]);
        if (SelectedPaths.Count == 1 && Directory.Exists(SelectedPaths[0]))
            return ArchivePath.GetFileName(SelectedPaths[0]);
        return $"archive_{DateTime.Now:yyyyMMddHHmmss}";
    }

    /// <summary>
    /// 检查输出路径在当前模式下是否有效。
    /// Manual：路径不能为空且父目录必须存在。
    /// Combined：路径非空即有效（由源文件推导）。
    /// Separate：输出到源文件所在目录，始终有效。
    /// </summary>
    private bool IsOutputPathValid()
    {
        switch (OutputMode)
        {
            case CompressOutputMode.Manual:
                if (string.IsNullOrEmpty(OutputPath))
                    return false;
                var dir = Path.GetDirectoryName(OutputPath);
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir);

            case CompressOutputMode.Combined:
                return !string.IsNullOrEmpty(OutputPath);

            case CompressOutputMode.Separate:
                return true;

            default:
                return true;
        }
    }

    /// <summary>
    /// 更新"开始压缩"按钮的启用状态。由模式切换和源文件变化时调用。
    /// </summary>
    private void UpdateCanCompress()
    {
        // 通知 CloseAction 调用方重新评估按钮状态
        // 实际按钮启用由 Command 的 CanExecute 决定，
        // 这里触发 CanExecuteChanged 刷新
        StartCompressCommand.NotifyCanExecuteChanged();
    }
}
