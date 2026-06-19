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

    // ===== 调试 =====
    public bool EnableDebugLogging { get; set; } = false;

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
