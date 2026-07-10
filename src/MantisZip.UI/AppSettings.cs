using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using MantisZip.Core;
using MantisZip.Core.Utils;

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
    public bool PreserveDirectoryRoot { get; set; } = true;    // 压缩文件夹时保留外层目录
    public string ZipEncoding { get; set; } = "utf-8";         // ZIP 文件名编码：utf-8 / gbk / default
    public string SevenZipCompressionMethod { get; set; } = "LZMA2"; // 7z 压缩方法：LZMA / LZMA2 / PPMd / BZip2 / Deflate
    public bool SevenZipSolid { get; set; } = true;            // 7z 固实压缩
    public string SevenZipSolidBlockSize { get; set; } = "";   // 7z 固实块大小：""=默认 / "64m" / "256m" / "512m" / "1g"
    public int SevenZipDictionarySize { get; set; } = 0;       // 7z 字典大小（字节）：0=默认 / 2^24 / 2^25 / 2^27 / 2^28
    public int SevenZipNumFastBytes { get; set; } = 0;         // 7z Word Size：0=默认 / 32 / 64 / 128 / 255
    public string SevenZipMatchFinder { get; set; } = "";      // 7z 匹配器：""=默认 / "bt2" / "bt3" / "bt4"
    public string ZipCompressionMethod { get; set; } = "deflate"; // ZIP 压缩方法：deflate / deflate64 / bzip2 / lzma / ppmd / store
    public string ZipEncryptionMethod { get; set; } = "aes256";   // ZIP 加密方式：aes256 / aes192 / aes128 / zipcrypto
    public bool SevenZipEncryptHeaders { get; set; } = true;      // 7z 加密文件名

    // ===== 解压 =====
    public string ExtractDestination { get; set; } = "ask"; // same-dir / desktop / last / ask
    public string FileConflictAction { get; set; } = "ask"; // overwrite / rename / skip / ask
    public bool OpenFolderAfterExtract { get; set; } = false;
    /// <summary>双击文件打开阈值（字节），超过此大小弹出确认框。0 = 禁用双击打开。</summary>
    public long DoubleClickOpenThreshold { get; set; } = 10 * 1024 * 1024; // 默认 10 MB

    // ===== 上下文菜单 / 文件关联 =====
    public bool EnableCompressMenu { get; set; } = true;
    public bool EnableExtractMenu { get; set; } = true;
    public bool EnableOpenMenu { get; set; } = true;     // 用 MantisZip 打开
    public bool EnableQuickCompress { get; set; } = true;
    public bool EnableCompressSeparate { get; set; } = true;
    public bool EnableCompressCombined { get; set; } = true;
    public bool EnableDynamicMenu { get; set; } = true;     // 动态 COM 菜单（默认启用）
    public bool ShowMenuIcons { get; set; } = true;
    public bool EnableSmartExtractMenu { get; set; } = true;   // 智能解压到此处
    public bool EnableExtractHereMenu { get; set; } = true;     // 解压到此处
    public bool EnableExtractToNamedMenu { get; set; } = true;  // 解压到（压缩包名）
    public bool EnableExtractToMenu { get; set; } = true;       // 解压到……

    // ===== 文件关联 — 逐扩展名设置 =====
    public bool AssocZip { get; set; } = true;
    public bool Assoc7z { get; set; } = true;
    public bool AssocRar { get; set; } = true;
    public bool AssocTar { get; set; } = true;
    public bool AssocTarGz { get; set; } = true;   // controls .tar.gz
    public bool AssocGz { get; set; } = true;
    public bool AssocIso { get; set; } = false;     // ISO defaults to unchecked
    public List<string> CustomAssocExtensions { get; set; } = new();

    // ===== 交互 =====
    public bool EnableDragExtract { get; set; } = true;

    // ===== 解压 ====
    /// <summary>解压条目时保留压缩包内的完整路径（默认关闭 = 相对当前浏览目录）</summary>
    public bool ExtractPreserveFullPath { get; set; } = false;

    // ===== 预览 =====
    public bool UseColorEmoji { get; set; } = true;
    public bool EnableImagePreview { get; set; } = true;
    public bool EnableTextPreview { get; set; } = true;
    public int MaxTablePreviewRows { get; set; } = 100;
    public int MaxTablePreviewCols { get; set; } = 100;
    public long MaxTextPreviewBytes { get; set; } = 5 * 1024 * 1024;
    public long MaxPreviewFileSize { get; set; } = 15 * 1024 * 1024; // 默认 15 MB
    public int TextPreviewFontSize { get; set; } = 12;
    public string TextPreviewFontFamily { get; set; } = "";           // 空=系统默认
    public string TextEncodingPreference { get; set; } = "auto";      // auto / utf-8 / gbk
    public int FontPreviewFontSize { get; set; } = 12;
    public string FontPreviewSampleText { get; set; } =
        "The quick brown fox jumps over the lazy dog.\n0123456789\n天地玄黄 宇宙洪荒 日月盈昃 辰宿列张";
    public int PreviewPosition { get; set; } = 4; // 1=Bottom, 2=Below tree, 3=Below file list, 4=Right
    public string InfoPanelOrientation { get; set; } = "Vertical"; // Horizontal / Vertical
    public bool ShowPreviewPanel { get; set; } = true;
    /// <summary>格式检测头部字节数（默认 4KB），用于魔数检测读取的文件头部大小</summary>
    public int PreviewHeadSize { get; set; } = 4096;
    /// <summary>启用魔数检测文件真实格式（默认开启）。当扩展名缺失或错误时，通过文件头 magic byte 识别真实格式。关闭时回退到纯扩展名判断。</summary>
    public bool EnableFormatDetection { get; set; } = true;

    // ===== 密码管理 =====
    public bool ShowPasswordMatchNotification { get; set; } = true;
    public bool PasswordRevealByDefault { get; set; } = false;

    // ===== 外观 =====
    public string Theme { get; set; } = "Light";    // "Light" | "Dark"
    public int MaxRecentFiles { get; set; } = 10;
    public string Language { get; set; } = "zh";
    public bool ShowProgressBars { get; set; } = true;
    public bool SeparateDirBaseline { get; set; } = false;

    // ===== 调试 =====
    public bool EnableDebugLogging { get; set; } = false;
    public string LogPrivacyMode { get; set; } = "extension"; // "off" | "filename" | "extension" | "full"

    // ===== 高级 =====
    /// <summary>7z.dll 路径（SharpSevenZip 使用，空字符串 = 自动探测，优先自带版本）</summary>
    public string SevenZipPath { get; set; } = "";
    /// <summary>启动时自动清理 %TEMP%\MantisZip\ 临时文件</summary>
    public bool CleanTempOnStartup { get; set; } = true;
    /// <summary>CLI 模式下遇到权限不足时，是否弹提权窗口（默认 false = 仅提示不可写目录）</summary>
    public bool AllowElevation { get; set; } = false;

    // ===== 默认路径优先级 =====
    /// <summary>
    /// QuickPathPreDialog 默认路径优先级策略。
    /// "context"   = 场景相关 > 资源管理器 > 最近使用 > 桌面
    /// "explorer"  = 资源管理器 > 场景相关 > 最近使用 > 桌面
    /// "recent"    = 最近使用 > 场景相关 > 资源管理器 > 桌面
    /// "desktop"   = 直接桌面
    /// </summary>
    public string DefaultPathPriority { get; set; } = "context";

    // ===== 持久化 =====
    /// <summary>便携模式：exe 同级存在 Portable.txt 时启用，路径重定向到 Data/ 目录。</summary>
    public static bool IsPortableMode { get; private set; }
    private static string SettingsDir { get; set; } = "";
    private static string SettingsFile { get; set; } = "";

    static AppSettings()
    {
        IsPortableMode = File.Exists(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Portable.txt"));

        if (IsPortableMode)
        {
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            Directory.CreateDirectory(dataDir);
            SettingsDir = dataDir;
            SettingsFile = Path.Combine(dataDir, "settings.json");
            PasswordManager.CustomDataDir = dataDir;
        }
        else
        {
            SettingsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
            SettingsFile = Path.Combine(SettingsDir, "settings.json");
        }
    }

    private static readonly Lazy<AppSettings> _instance = new(() => Load(), LazyThreadSafetyMode.ExecutionAndPublication);
    public static AppSettings Instance => _instance.Value;

    /// <summary>
    /// 保存设置到 settings.json。返回 true 表示成功，false 表示失败。
    /// 调用方可根据返回值决定是否提示用户。
    /// </summary>
    public bool Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            // 便携版不写注册表
            if (!IsPortableMode)
            {
                // 同步上下文菜单设置到注册表（供 COM 组件读取）
                SyncContextMenuToRegistry();
            }
            return true;
        }
        catch (Exception ex)
        {
            App.LogDebug("AppSettings.Save: failed: {0}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 将上下文菜单相关的设置同步到 HKCU\Software\MantisZip\ContextMenu。
    /// COM 组件（MantisZip.ShellExt）在 Explorer 进程内从注册表读取这些设置。
    /// </summary>
    private void SyncContextMenuToRegistry()
    {
        try
        {
            const string keyPath = @"Software\MantisZip\ContextMenu";
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (key == null) return;

            key.SetValue("EnableDynamicMenu", EnableDynamicMenu ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("ShowMenuIcons", ShowMenuIcons ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("EnableOpenMenu", EnableOpenMenu ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("EnableExtractHereMenu", EnableExtractHereMenu ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("EnableSmartExtractMenu", EnableSmartExtractMenu ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("EnableExtractToNamedMenu", EnableExtractToNamedMenu ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("EnableExtractToMenu", EnableExtractToMenu ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("EnableCompressSeparate", EnableCompressSeparate ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("EnableCompressCombined", EnableCompressCombined ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("EnableCompressMenu", EnableCompressMenu ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("EnableQuickCompress", EnableQuickCompress ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            App.LogDebug("AppSettings.SyncContextMenuToRegistry: failed: {0}", ex.Message);
        }
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
        catch (Exception ex) { CoreLog.Trace("AppSettings.Load: failed: {0}", ex.Message); }
        return new AppSettings();
    }
}
