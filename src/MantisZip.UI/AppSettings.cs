using System.IO;
using System.Text.Json;

namespace MantisZip.UI;

/// <summary>
/// 应用设置（存储在 %LOCALAPPDATA%\MantisZip\settings.json）
/// </summary>
public class AppSettings
{
    // ===== 压缩 =====
    public string DefaultFormat { get; set; } = "zip";     // zip / 7z / tar.gz
    public int DefaultLevel { get; set; } = 5;
    public bool CloseAfterCompress { get; set; } = true;
    public bool KeepOriginalExtension { get; set; } = false;  // 保留源文件扩展名（abc.max → abc.max.zip）

    // ===== 解压 =====
    public string ExtractDestination { get; set; } = "ask"; // same-dir / desktop / last / ask
    public string FileConflictAction { get; set; } = "ask"; // overwrite / rename / skip / ask
    public bool OpenFolderAfterExtract { get; set; } = false;

    // ===== 上下文菜单 =====
    public bool EnableCompressMenu { get; set; } = true;
    public bool EnableExtractMenu { get; set; } = true;
    public bool EnableOpenMenu { get; set; } = true;     // 用 MantisZip 打开
    public bool EnableQuickCompress { get; set; } = true;
    public bool EnableCascadingMenu { get; set; } = false;  // 层叠子菜单
    public bool ShowMenuIcons { get; set; } = true;

    // ===== 预览 =====
    public bool EnableImagePreview { get; set; } = true;
    public bool EnableTextPreview { get; set; } = true;
    public long MaxTextPreviewBytes { get; set; } = 5 * 1024 * 1024;

    // ===== 高级 =====
    public string SevenZipPath { get; set; } = @"C:\Program Files\7-Zip\7z.exe";

    // ===== 持久化 =====
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    private static AppSettings? _instance;
    public static AppSettings Instance => _instance ??= Load();

    public void Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { /* 忽略保存失败 */ }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch { /* 忽略加载失败，使用默认值 */ }
        return new AppSettings();
    }
}
