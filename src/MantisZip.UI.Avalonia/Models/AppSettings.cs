using System.Text.Json;
using System.Text.Json.Serialization;
using MantisZip.Core;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 应用设置（存储于 %LOCALAPPDATA%\MantisZip\settings.json，便携模式为 exe 旁 Data/settings.json）
/// JSON 格式与 WPF 版本兼容，但仅保留 Avalonia 版本使用的字段。
/// </summary>
public class AppSettings
{
    // ===== 压缩 =====
    public string DefaultFormat { get; set; } = "zip";
    public int DefaultLevel { get; set; } = 5;
    public bool CloseAfterCompress { get; set; } = true;
    public bool KeepOriginalExtension { get; set; } = false;
    public string ZipEncoding { get; set; } = "utf-8";
    public string SevenZipCompressionMethod { get; set; } = "LZMA2";
    public bool SevenZipSolid { get; set; } = true;
    public string SevenZipSolidBlockSize { get; set; } = "";
    /// <summary>
    /// 7z 字典大小：""=默认，或 "64m" / "256m" / "1g" / "4g"（CustomParameters["d"] 直传 7z.dll）。
    /// 旧版为 int 字节值，经 <see cref="LegacyIntToSizeStringConverter"/> 兼容读取。
    /// </summary>
    [JsonConverter(typeof(LegacyIntToSizeStringConverter))]
    public string SevenZipDictionarySize { get; set; } = "";
    public int SevenZipNumFastBytes { get; set; } = 0;
    public string SevenZipMatchFinder { get; set; } = "";
    /// <summary>
    /// 7z 多线程压缩（mt=on）。默认 true，利用多核 CPU 并行压缩。
    /// 仅对 7z 格式有效。
    /// </summary>
    public bool SevenZipMultithreaded { get; set; } = true;

    // ===== 自适应压缩（三个独立开关） =====
    /// <summary>自适应压缩：已压缩文件自动 Store。仅对 ZIP + Deflate/Deflate64 有效。</summary>
    public bool AdaptiveCompression { get; set; }

    /// <summary>魔数检测：自适应开启时，对大文件做魔数增强识别。自适应关闭时无效。</summary>
    public bool AdaptiveSmartDetect { get; set; }

    /// <summary>多线程压缩：所有可压缩文件走 SharpSevenZip mt=on。可与自适应同时开启。</summary>
    public bool MultiThreadedCompression { get; set; }

    /// <summary>
    /// 旧版自适应压缩模式（向后兼容反序列化）。
    /// 已废弃：新代码应使用 <see cref="AdaptiveCompression"/> + <see cref="AdaptiveSmartDetect"/> + <see cref="MultiThreadedCompression"/>。
    /// </summary>
    // CS0618 抑制：刻意保留旧枚举属性以读取旧 settings.json，Load() 中迁移为三个新开关
#pragma warning disable CS0618
    public AdaptiveCompressionMode AdaptiveCompressionMode { get; set; } = AdaptiveCompressionMode.Disabled;
#pragma warning restore CS0618

    /// <summary>【已废弃 · Phase 2 合并】多线程模式「仅存储格式」列表 —— 功能已并入「自适应规则」（Store 类规则）。仅保留用于旧配置迁移读取，迁移后清空。</summary>
    public List<string> MultiThreadedStoreFormatIds { get; set; } = new();

    /// <summary>用户自定义格式列表。</summary>
    public List<FormatDefinition> CustomFormats { get; set; } = new();

    /// <summary>用户自定义压缩级别覆盖规则。</summary>
    public List<AdaptiveOverrideRule> AdaptiveOverrides { get; set; } = new();

    /// <summary>
    /// 默认自适应规则集版本（2026-10-20 Phase 2 起）：0 = 旧版默认规则集（含 svg/tar/wav 等未压缩格式），
    /// 1 = 与 SSOT 对齐后的默认规则集。加载时按版本迁移旧持久化数据。
    /// </summary>
    public int AdaptiveOverridesDefaultsVersion { get; set; } = 0;

    public string ZipCompressionMethod { get; set; } = "deflate";
    public string ZipEncryptionMethod { get; set; } = "aes256";
    public bool SevenZipEncryptHeaders { get; set; } = true;
    /// <summary>压缩时输出文件已存在（文件冲突）的默认处理：ask / overwrite / add / rename / skip</summary>
    public string CompressFileConflictAction { get; set; } = "ask";

    // ===== 分卷 =====
    public string SplitSizeTag { get; set; } = "0";
    public string CustomSplitSizeMB { get; set; } = "";

    // ===== 解压 =====
    public string ExtractDestination { get; set; } = "ask"; // same-dir / desktop / last / ask
    public string FileConflictAction { get; set; } = "ask"; // overwrite / rename / skip / ask
    public bool OpenFolderAfterExtract { get; set; } = false;
    public bool ExtractPreserveFullPath { get; set; } = false;
    /// <summary>
    /// 并行解压线程数（1 = 串行，>1 = 并行线程数）。
    /// 默认值 = Environment.ProcessorCount。
    /// </summary>
    public int ParallelExtractDegree { get; set; } = Environment.ProcessorCount;

    /// <summary>并行解压全局开关：false = 始终串行（线程数=1）。</summary>
    public bool EnableParallelExtract { get; set; } = true;

    /// <summary>智能并行度：true = 按磁盘类型/预测试自动选择线程数。</summary>
    public bool SmartParallelDegree { get; set; } = true;

    /// <summary>智能模式下 SSD 的线程数（1-16）。</summary>
    public int SmartSsdDegree { get; set; } = 12;

    /// <summary>智能模式下 HDD 的线程数（1-16）。</summary>
    public int SmartHddDegree { get; set; } = 8;

    /// <summary>大包预测试：≥1GB 压缩包先测最优线程数（仅智能模式生效）。</summary>
    public bool LargeArchivePreTest { get; set; } = true;

    // ===== 进度窗口 =====
    /// <summary>完成后自动关闭延时（秒，0–60；0 = 立即关闭）。📌/失败/测试流不受此影响（始终留窗）。</summary>
    public int AutoCloseDelaySeconds { get; set; } = 3;

    // ===== 交互 =====
    public bool EnableDragExtract { get; set; } = true;
    /// <summary>双击压缩包时的行为：open / extract-here / smart-extract / extract-dialog</summary>
    public string DoubleClickAction { get; set; } = "open";
    /// <summary>双击打开阈值（字节），超过此大小弹出确认框。0 = 禁用双击打开。</summary>
    public long DoubleClickOpenThreshold { get; set; } = 10 * 1024 * 1024; // 默认 10 MB
    /// <summary>解压完成后将原压缩包移到回收站</summary>
    public bool DeleteArchiveAfterExtract { get; set; } = false;

    // ===== 上下文菜单 =====
    public bool EnableOpenMenu { get; set; } = true;
    public bool EnableCompressMenu { get; set; } = true;
    public bool EnableExtractMenu { get; set; } = true;
    public bool EnableQuickCompress { get; set; } = true;
    public bool EnableExtractHereMenu { get; set; } = true;
    public bool EnableExtractToNamedMenu { get; set; } = true;
    public bool EnableExtractToMenu { get; set; } = true;
    public bool EnableSmartExtractMenu { get; set; } = true;
    public bool EnableCompressSeparate { get; set; } = true;
    public bool EnableCompressCombined { get; set; } = true;
    public bool ShowMenuIcons { get; set; } = true;
    public bool EnableDynamicMenu { get; set; } = true;

    // ===== 高级 =====
    public string SevenZipPath { get; set; } = "";
    public bool PreserveDirectoryRoot { get; set; } = true;
    public bool CleanTempOnStartup { get; set; } = true;
    /// <summary>写入目标目录无权限时是否弹窗提权。false = 仅提示，不弹提权框。</summary>
    public bool AllowElevation { get; set; } = false;

    // ===== 预览 =====
    public bool EnableImagePreview { get; set; } = true;
    public bool EnableTextPreview { get; set; } = true;
    public long MaxTextPreviewBytes { get; set; } = 1 * 1024 * 1024; // 1 MB
    public int TextPreviewFontSize { get; set; } = 13;
    public int MaxTablePreviewRows { get; set; } = 100;
    public int MaxTablePreviewCols { get; set; } = 100;
    public long MaxPreviewFileSize { get; set; } = 15 * 1024 * 1024;
    public string TextPreviewFontFamily { get; set; } = "";
    public string TextEncodingPreference { get; set; } = "auto";
    public int FontPreviewFontSize { get; set; } = 12;
    public string FontPreviewSampleText { get; set; } = "The quick brown fox jumps over the lazy dog.\n0123456789\n天地玄黄 宇宙洪荒 日月盈昃 辰宿列张";
    public bool FontPreviewEnableLigature { get; set; } = true;
    public int PreviewPosition { get; set; } = 4;
    public string InfoPanelOrientation { get; set; } = "Vertical";
    public bool ShowPreviewInfoPanel { get; set; } = true;
    public bool ShowPreviewPanel { get; set; } = true;
    public bool EnableFormatDetection { get; set; } = true;
    public int PreviewHeadSize { get; set; } = 4096;

    // ===== HTML 预览安全 =====
    /// <summary>允许 HTML 预览中执行 JavaScript（默认禁止）。</summary>
    public bool AllowJavaScript { get; set; }
    /// <summary>允许 HTML 预览中加载外部资源（图片/CSS/字体等，默认禁止）。</summary>
    public bool AllowExternalResources { get; set; }
    /// <summary>允许 HTML 预览中点击链接导航到其他页面（默认禁止）。</summary>
    public bool AllowNavigation { get; set; }

    // ===== 密码管理 =====
    public bool ShowPasswordMatchNotification { get; set; } = true;
    public bool PasswordRevealByDefault { get; set; } = false;

    // ===== 外观 =====
    /// <summary>主题：System=跟随系统 / Light=亮色 / Dark=暗色（三态）</summary>
    public string Theme { get; set; } = "System";
    public int MaxRecentFiles { get; set; } = 10;
    public string AppFontFamily { get; set; } = "";
    public string CompactnessMode { get; set; } = "Normal";
    public string Language { get; set; } = "zh";
    /// <summary>
    /// 是否显示文件列表里的**数据条**（每行文字后那条表示相对占比的彩色横条）。
    /// 2026-10-22：用户界面文案已从「进度条」改名为「数据条 / Data Bars」（该横条表达的是
    /// 数据相对占比，不是任务进度）；**属性名保持不变** —— 它是 settings.json 的持久化键，
    /// 改名会导致老用户的设置读不到。
    /// </summary>
    public bool ShowProgressBars { get; set; } = true;
    /// <summary>进度窗口内容模式持久化（"simple"/"detailed"/"list"；默认 "detailed"）。</summary>
    public string ProgressContentMode { get; set; } = "detailed";
    /// <summary>进度窗口信息量分级持久化（"minimal"/"medium"/"full"；默认 "medium"）。</summary>
    public string ProgressInfoDensity { get; set; } = "medium";
    /// <summary>进度窗口宽度持久化（像素；默认 800，窗口 MinWidth=500 兜底）。</summary>
    public double ProgressWindowWidth { get; set; } = 800;
    public bool SeparateDirBaseline { get; set; } = false;
    /// <summary>目录树：切换目录时自动展开到当前目录并收起其他分支。</summary>
    public bool AutoExpandTreeToCurrent { get; set; } = false;

    // ===== 文件关联 =====
    public bool AssocZip { get; set; } = true;
    public bool Assoc7z { get; set; } = true;
    public bool AssocRar { get; set; } = true;
    public bool AssocTar { get; set; } = true;
    public bool AssocTarGz { get; set; } = true;
    public bool AssocGz { get; set; } = true;
    public bool AssocIso { get; set; } = false;
    public List<string> CustomAssocExtensions { get; set; } = new();

    // ===== 收藏夹 =====
    public List<string> FavoritePaths { get; set; } = new();

    // ===== 文件过滤 =====
    /// <summary>用户保存的过滤预设列表（上限 20 个）。内置预设由 <see cref="FileFilterPreset.GetBuiltInPresets"/> 提供。</summary>
    public List<FileFilterPreset> FilterPresets { get; set; } = new();

    /// <summary>添加用户预设（上限 20 个）。超过上限时静默忽略。</summary>
    public void AddPreset(FileFilterPreset preset)
    {
        if (FilterPresets.Count >= 20) return;
        FilterPresets.Add(preset);
    }

    // ===== 默认路径优先级 =====
    /// <summary>文件选择器初始路径的优先级顺序（不含桌面，桌面始终兜底）。值域: context / explorer / recent / custom。</summary>
    public List<string> DefaultPathOrder { get; set; } = new() { "context", "explorer", "recent", "custom" };
    /// <summary>手动路径值（对应 <see cref="DefaultPathOrder"/> 中的 "custom" 项）。留空 = 跳过该项。</summary>
    public string CustomDefaultPath { get; set; } = "";

    // ===== 文件选择器 =====
    /// <summary>PickItems 模式右侧累积面板宽度（像素）。0 = 未设置，使用默认宽度 260。</summary>
    public double PickItemsPanelWidth { get; set; }
    /// <summary>ExtractFolder 模式右侧解压预览面板宽度（像素）。0 = 未设置，使用默认宽度 260。</summary>
    public double ExtractFolderPanelWidth { get; set; }
    /// <summary>
    /// 左侧「路径切换面板」的标签名（<c>Controls.PathTab</c> 成员名，如 <c>Tree</c>）。
    /// 语义随 <see cref="PickerRememberTab"/> 变化：记住模式 = 上次关闭选择器时的标签（选择器负责写回）；
    /// 固定模式 = 设置里选定的固定标签（对话框内手动切换不会被记住）。
    /// 空值/未知值一律落回「收藏」标签，保证首次使用与旧配置文件行为一致。
    /// 注意：只影响面板标签，与 <see cref="DefaultPathOrder"/>（初始目录落点）无关。
    /// </summary>
    public string PickerSourceTab { get; set; } = "";
    /// <summary>
    /// 是否让文件选择器左侧「路径切换面板」记住上次使用的标签。
    /// true（默认）= 每次打开沿用上次关闭时的标签；false = 固定用 <see cref="PickerSourceTab"/> 指定的标签。
    /// </summary>
    public bool PickerRememberTab { get; set; } = true;

    // ===== 调试 =====
    public bool EnableDebugLogging { get; set; } = false;
    public string LogPrivacyMode { get; set; } = "extension";

    // ===== 持久化 =====
    /// <summary>便携模式：exe 同级存在 Portable.txt 时启用，设置路径重定向到 exe 旁 Data/ 目录。</summary>
    public static bool IsPortableMode { get; private set; }

    /// <summary>应用数据目录：便携模式 = exe 旁 Data/，否则 = %LOCALAPPDATA%\MantisZip。</summary>
    public static string DataDir { get; private set; } = "";

    /// <summary>
    /// 临时文件根目录：便携模式 = exe 旁 Data/Temp/，普通模式 = %TEMP%/MantisZip/。
    /// 仅返回路径，不创建目录（调用点各自负责 <see cref="Directory.CreateDirectory"/>）。
    /// </summary>
    public static string GetTempDir() =>
        IsPortableMode
            ? Path.Combine(DataDir, "Temp")
            : Path.Combine(Path.GetTempPath(), "MantisZip");

    private static readonly string SettingsDir;
    private static readonly string SettingsFile;

    static AppSettings()
    {
        IsPortableMode = File.Exists(Path.Combine(AppContext.BaseDirectory, "Portable.txt"));

        if (IsPortableMode)
        {
            var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dataDir);
            DataDir = dataDir;
            SettingsDir = dataDir;
            SettingsFile = Path.Combine(dataDir, "settings.json");
            PasswordManager.CustomDataDir = dataDir;
        }
        else
        {
            DataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
            SettingsDir = DataDir;
            SettingsFile = Path.Combine(SettingsDir, "settings.json");
        }
    }

    // ── 进程级缓存（startup-appsettings-cache：启动路径 72 处 Load 全量读盘+STJ 反序列化，冷 60-77ms/暖 2-16ms 每次重复支付）──
    private static AppSettings? _cached;
    private static readonly object SyncRoot = new();

    public static AppSettings Load()
    {
        lock (SyncRoot)
        {
            // 缓存命中（startup-appsettings-cache：每进程只读盘一次）
            if (_cached != null) return _cached;
            try
            {
                if (!File.Exists(SettingsFile))
                {
                    var defaults = CreateWithDefaults();
                    _cached = defaults; // 不存在时缓存默认值，避免每次重新生成默认规则表
                    return defaults;
                }
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings) ?? CreateWithDefaults();
                // 向后兼容：从旧 AdaptiveCompressionMode 枚举迁移为三个独立开关
                if (settings.AdaptiveCompressionMode != AdaptiveCompressionMode.Disabled)
                {
                    settings.AdaptiveCompression = true;
                    settings.AdaptiveSmartDetect = settings.AdaptiveCompressionMode == AdaptiveCompressionMode.SmartDetect;
                    settings.MultiThreadedCompression = settings.AdaptiveCompressionMode == AdaptiveCompressionMode.MultiThreaded;
                    settings.AdaptiveCompressionMode = AdaptiveCompressionMode.Disabled;
                }
                // 首次安装时填充默认规则
                if (settings.AdaptiveOverrides.Count == 0)
                {
                    settings.AdaptiveOverrides = GetDefaultAdaptiveOverrides();
                }
                // 并行解压智能线程数越界 clamp 到 [1,16]（边界情况 #8）
                settings.SmartSsdDegree = Math.Clamp(settings.SmartSsdDegree, 1, 16);
                settings.SmartHddDegree = Math.Clamp(settings.SmartHddDegree, 1, 16);
                // Phase 2 / Task 10：旧版默认规则集迁移到与 SSOT 对齐的版本
                MigrateAdaptiveOverridesDefaults(settings);
                _cached = settings;
                return settings;
            }
            catch
            {
                // 异常态不缓存：下次 Load 重试读盘，避免把损坏文件态固化
                return new AppSettings();
            }
        }
    }

    public bool Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(SettingsFile, json);
            // 落盘成功后缓存同步为被保存实例（外部可见值与内存一致）
            lock (SyncRoot)
            {
                _cached = this;
            }
            return true;
        }
        catch
        {
            // 失败不动缓存：缓存仍代表最后一次成功落盘值
            return false;
        }
    }

    /// <summary>测试钩子：清空进程级缓存（internal，经 InternalsVisibleTo；沿 StartupTimer.ResetForTest 惯例）。</summary>
    internal static void ResetCacheForTests()
    {
        lock (SyncRoot)
        {
            _cached = null;
        }
    }

    /// <summary>创建带默认自适应规则的新实例。</summary>
    private static AppSettings CreateWithDefaults()
    {
        var settings = new AppSettings();
        settings.AdaptiveOverrides = GetDefaultAdaptiveOverrides();
        return settings;
    }

    /// <summary>
    /// 返回默认自适应压缩覆盖规则（**与 SSOT「已压缩」集合对齐** —— Phase 2 / Task 10 + 合并）。
    /// 六类：图片 / 视频 / 音频 / 压缩包 / 字体 / 文档（字体与文档来自旧「仅存储格式」默认列表，合并后并入）。
    /// 不含未压缩格式：svg（XML 文本）/ tar（未压缩归档）/ wav（PCM）/ tga/hdr/exr（未压缩位图）。
    /// </summary>
    internal static List<AdaptiveOverrideRule> GetDefaultAdaptiveOverrides() => new()
    {
        new() { Name = "图片类", FormatIds = new() { "Jpeg", "Png", "WebP", "Bmp", "Gif", "Ico" }, Level = AdaptiveLevel.Store, Enabled = true },
        new() { Name = "视频类", FormatIds = new() { "Mp4", "Mkv", "WebM", "Wmv", "Mov", "Avi", "Flv" }, Level = AdaptiveLevel.Store, Enabled = true },
        new() { Name = "音频类", FormatIds = new() { "Mp3", "Flac", "Ogg" }, Level = AdaptiveLevel.Store, Enabled = true },
        new() { Name = "压缩包类", FormatIds = new() { "Zip", "SevenZip", "Rar", "Gz", "Bz2", "Xz", "Zstd", "Iso" }, Level = AdaptiveLevel.Store, Enabled = true },
        new() { Name = "字体类", FormatIds = new() { "Ttf", "Otf", "Woff", "Woff2" }, Level = AdaptiveLevel.Store, Enabled = true },
        new() { Name = "文档类", FormatIds = new() { "Pdf", "Docx", "Xlsx", "Pptx", "Epub" }, Level = AdaptiveLevel.Store, Enabled = true },
    };

    /// <summary>默认规则集版本（与 SSOT 对齐 + 并入旧「仅存储格式」后的版本号）。</summary>
    private const int CurrentAdaptiveOverridesDefaultsVersion = 1;

    /// <summary>
    /// 迁移旧持久化数据（Phase 2 / Task 10 + 「仅存储格式」合并）：
    /// ① 旧版默认规则集（4 条，名称与格式 ID 完全一致）→ 整体替换为新的 6 类默认规则集；
    ///    用户自行增删改过的规则集原样保留（只推进版本号）。
    /// ② 旧「仅存储格式」列表中**用户额外添加**的格式 ID（不在旧默认列表内）→ 迁移为一条
    ///    「仅存储格式（迁移）」规则（Store 级别）；旧默认列表本身已被 SSOT 覆盖，不再重复迁移。
    /// ③ 迁移后清空旧列表（该功能已并入自适应规则）。
    /// </summary>
    internal static void MigrateAdaptiveOverridesDefaults(AppSettings settings)
    {
        if (settings.AdaptiveOverridesDefaultsVersion >= CurrentAdaptiveOverridesDefaultsVersion)
            return;

        if (IsLegacyDefaultOverrides(settings.AdaptiveOverrides))
            settings.AdaptiveOverrides = GetDefaultAdaptiveOverrides();

        // 旧「仅存储格式」列表 → 规则（仅迁移用户额外添加的 ID）
        var legacyDefaults = GetDefaultMultiThreadedStoreFormatIds();
        var customStoreIds = (settings.MultiThreadedStoreFormatIds ?? new List<string>())
            .Where(id => !legacyDefaults.Contains(id))
            .ToList();
        if (customStoreIds.Count > 0)
        {
            settings.AdaptiveOverrides.Add(new AdaptiveOverrideRule
            {
                Name = "仅存储格式（迁移）",
                FormatIds = customStoreIds,
                Level = AdaptiveLevel.Store,
                Enabled = true,
            });
        }
        settings.MultiThreadedStoreFormatIds = new List<string>();

        settings.AdaptiveOverridesDefaultsVersion = CurrentAdaptiveOverridesDefaultsVersion;
    }

    /// <summary>判断当前规则集是否仍为旧版默认规则集（名称 + 格式 ID 集合逐一比对）。</summary>
    private static bool IsLegacyDefaultOverrides(List<AdaptiveOverrideRule>? rules)
    {
        if (rules == null || rules.Count != 4) return false;

        // 旧版默认：图片类/视频类/音频类/压缩包类
        var expected = new (string Name, string[] Formats)[]
        {
            ("图片类", new[] { "Jpeg", "Png", "WebP", "Bmp", "Gif", "Ico", "Tga", "Hdr", "Exr", "Svg" }),
            ("视频类", new[] { "Mp4", "Mkv", "WebM", "Wmv", "Mov", "Avi", "Flv" }),
            ("音频类", new[] { "Mp3", "Flac", "Wav", "Ogg" }),
            ("压缩包类", new[] { "Zip", "SevenZip", "Rar", "Tar", "Gz", "Bz2", "Xz", "Zstd", "Iso" }),
        };

        foreach (var (name, formats) in expected)
        {
            var rule = rules.FirstOrDefault(r => r.Name == name);
            if (rule == null) return false;
            if (rule.FormatIds.Count != formats.Length) return false;
            if (!formats.All(f => rule.FormatIds.Contains(f))) return false;
            if (rule.Level != AdaptiveLevel.Store || !rule.Enabled) return false;
        }
        return true;
    }

    /// <summary>旧「仅存储格式」默认 ID 列表（内置已压缩格式）—— 仅用于迁移时区分「用户自定义项」。</summary>
    private static List<string> GetDefaultMultiThreadedStoreFormatIds() => new()
    {
        // 图片
        "Jpeg", "Png", "WebP", "Bmp", "Gif", "Ico", "Tga", "Hdr", "Exr", "Svg",
        // 视频
        "Mp4", "Mkv", "WebM", "Wmv", "Mov", "Avi", "Flv",
        // 音频
        "Mp3", "Flac", "Wav", "Ogg",
        // 压缩包
        "Zip", "SevenZip", "Rar", "Tar", "Gz", "Bz2", "Xz", "Zstd", "Iso",
        // 字体
        "Ttf", "Otf", "Woff", "Woff2",
        // 文档
        "Pdf", "Docx", "Xlsx", "Pptx", "Epub",
    };
}

/// <summary>
/// 兼容旧版 settings.json：SevenZipDictionarySize 原为 int 字节值（如 268435456 = 256MB），
/// 新版改为 string（"256m" / "1g"）。读取时数字 → 转换为带单位字符串；写入时一律写 string。
/// </summary>
internal sealed class LegacyIntToSizeStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? "",
            JsonTokenType.Number when reader.TryGetInt64(out var bytes) => BytesToSizeString(bytes),
            _ => "",
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);

    /// <summary>字节数 → 7z 大小字符串（取整到最大单位，如 268435456 → "256m"）。</summary>
    internal static string BytesToSizeString(long bytes)
    {
        if (bytes <= 0) return "";
        if (bytes % (1L << 30) == 0) return $"{bytes >> 30}g";
        if (bytes % (1L << 20) == 0) return $"{bytes >> 20}m";
        if (bytes % (1L << 10) == 0) return $"{bytes >> 10}k";
        return $"{bytes}b";
    }
}
