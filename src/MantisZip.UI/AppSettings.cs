using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
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

    // ===== 解压 =====
    public string ExtractDestination { get; set; } = "ask"; // same-dir / desktop / last / ask
    public string FileConflictAction { get; set; } = "ask"; // overwrite / rename / skip / ask
    public bool OpenFolderAfterExtract { get; set; } = false;

    // ===== 上下文菜单 / 文件关联 =====
    public bool EnableCompressMenu { get; set; } = true;
    public bool EnableExtractMenu { get; set; } = true;
    public bool EnableOpenMenu { get; set; } = true;     // 用 MantisZip 打开
    public bool EnableQuickCompress { get; set; } = true;
    public bool EnableCompressSeparate { get; set; } = true;
    public bool EnableCompressCombined { get; set; } = true;
    public bool EnableCascadingMenu { get; set; } = true;   // 层叠子菜单（默认启用，避免多文件右键动词上限问题）
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

    // ===== 持久化 =====
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

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
            // 同步上下文菜单设置到注册表（供 COM 组件读取）
            SyncContextMenuToRegistry();
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

            key.SetValue("EnableCascadingMenu", EnableCascadingMenu ? 1 : 0, RegistryValueKind.DWord);
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
