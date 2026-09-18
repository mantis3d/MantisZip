using System.Text.Json;
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
    public int SevenZipDictionarySize { get; set; } = 0;
    public int SevenZipNumFastBytes { get; set; } = 0;
    public string SevenZipMatchFinder { get; set; } = "";
    /// <summary>
    /// 7z 多线程压缩（mt=on）。默认 true，利用多核 CPU 并行压缩。
    /// 仅对 7z 格式有效。
    /// </summary>
    public bool SevenZipMultithreaded { get; set; } = true;

    /// <summary>
    /// 自适应压缩。启用时对已压缩文件（图片/音视频/字体/归档等）自动 Store，其余保持用户选定级别。
    /// 仅对 ZIP + Deflate/Deflate64 有效。
    /// </summary>
    public bool AdaptiveCompression { get; set; }

    // ===== 自适应压缩级别 =====
    /// <summary>自适应压缩模式（Disabled / StoreForCompressed / SmartDetect / MultiThreaded）。</summary>
    public AdaptiveCompressionMode AdaptiveCompressionMode { get; set; } = AdaptiveCompressionMode.StoreForCompressed;

    /// <summary>多线程模式下用户自定义仅存储格式 ID 列表（不可删除的唯一规则）。</summary>
    public List<string> MultiThreadedStoreFormatIds { get; set; } = new();

    /// <summary>用户自定义格式列表。</summary>
    public List<FormatDefinition> CustomFormats { get; set; } = new();

    /// <summary>用户自定义压缩级别覆盖规则。</summary>
    public List<AdaptiveOverrideRule> AdaptiveOverrides { get; set; } = new();

    public string ZipCompressionMethod { get; set; } = "deflate";
    public string ZipEncryptionMethod { get; set; } = "aes256";
    public bool SevenZipEncryptHeaders { get; set; } = true;

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
    public bool ShowProgressBars { get; set; } = true;
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

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return CreateWithDefaults();
            var json = File.ReadAllText(SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? CreateWithDefaults();
            // 首次安装时填充默认规则
            if (settings.AdaptiveOverrides.Count == 0)
            {
                settings.AdaptiveOverrides = GetDefaultAdaptiveOverrides();
            }
            // 首次安装时填充默认仅存储格式（多线程模式用）
            if (settings.MultiThreadedStoreFormatIds.Count == 0)
            {
                settings.MultiThreadedStoreFormatIds = GetDefaultMultiThreadedStoreFormatIds();
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public bool Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>创建带默认自适应规则的新实例。</summary>
    private static AppSettings CreateWithDefaults()
    {
        var settings = new AppSettings();
        settings.AdaptiveOverrides = GetDefaultAdaptiveOverrides();
        settings.MultiThreadedStoreFormatIds = GetDefaultMultiThreadedStoreFormatIds();
        return settings;
    }

    /// <summary>返回默认自适应压缩覆盖规则。</summary>
    private static List<AdaptiveOverrideRule> GetDefaultAdaptiveOverrides() => new()
    {
        new() { Name = "图片类", FormatIds = new() { "Jpeg", "Png", "WebP", "Bmp", "Gif", "Ico", "Tga", "Hdr", "Exr", "Svg" }, Level = AdaptiveLevel.Store, Enabled = true },
        new() { Name = "视频类", FormatIds = new() { "Mp4", "Mkv", "WebM", "Wmv", "Mov", "Avi", "Flv" }, Level = AdaptiveLevel.Store, Enabled = true },
        new() { Name = "音频类", FormatIds = new() { "Mp3", "Flac", "Wav", "Ogg" }, Level = AdaptiveLevel.Store, Enabled = true },
        new() { Name = "压缩包类", FormatIds = new() { "Zip", "SevenZip", "Rar", "Tar", "Gz", "Bz2", "Xz", "Zstd", "Iso" }, Level = AdaptiveLevel.Store, Enabled = true },
    };

    /// <summary>返回多线程模式默认仅存储格式 ID（内置已压缩格式）。</summary>
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
