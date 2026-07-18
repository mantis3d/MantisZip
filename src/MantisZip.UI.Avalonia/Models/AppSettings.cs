using System.Text.Json;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 应用设置（存储在 %LOCALAPPDATA%\MantisZip\settings.json）
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

    // ===== 上下文菜单 =====
    public bool EnableOpenMenu { get; set; } = true;
    public bool EnableCompressMenu { get; set; } = true;
    public bool EnableExtractHereMenu { get; set; } = true;
    public bool EnableExtractToNamedMenu { get; set; } = true;
    public bool EnableExtractToMenu { get; set; } = true;
    public bool EnableSmartExtractMenu { get; set; } = true;
    public bool EnableCompressSeparate { get; set; } = true;
    public bool EnableCompressCombined { get; set; } = true;
    public bool ShowMenuIcons { get; set; } = true;
    public bool EnableDynamicMenu { get; set; } = true;

    // ===== 解压扩展 =====
    public bool EnableDragExtract { get; set; } = true;
    public bool ExtractPreserveFullPath { get; set; } = false;

    // ===== 高级 =====
    public string SevenZipPath { get; set; } = "";
    public bool PreserveDirectoryRoot { get; set; } = true;
    public bool CleanTempOnStartup { get; set; } = true;

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
    public bool ShowPreviewPanel { get; set; } = true;
    public bool UseColorEmoji { get; set; } = true;
    public bool EnableFormatDetection { get; set; } = true;
    public int PreviewHeadSize { get; set; } = 4096;

    // ===== 密码管理 =====
    public bool ShowPasswordMatchNotification { get; set; } = true;
    public bool PasswordRevealByDefault { get; set; } = false;

    // ===== 外观 =====
    public string Theme { get; set; } = "Light";
    public int MaxRecentFiles { get; set; } = 10;
    public string AppFontFamily { get; set; } = "";
    public string CompactnessMode { get; set; } = "Normal";
    public string Language { get; set; } = "zh";
    public bool ShowProgressBars { get; set; } = true;
    public bool SeparateDirBaseline { get; set; } = false;

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

    // ===== 调试 =====
    public bool EnableDebugLogging { get; set; } = false;
    public string LogPrivacyMode { get; set; } = "extension";

    // ===== 持久化 =====
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new AppSettings();
            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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
}
