using System.IO;
using System.Reflection;
using Microsoft.Win32;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// Shell 右键菜单集成。
/// 写入 HKCU\Software\Classes，无需管理员权限。
/// 支持层叠子菜单 (cascade) 和独立动词两种模式，由 AppSettings.EnableCascadingMenu 控制。
/// 菜单顺序（层叠模式下严格遵循）：
///   1. 用MantisZip打开
///   2. 用MantisZip解压到此处
///   3. 用MantisZip解压到（压缩包名）
///   4. 用MantisZip解压到……
///   5. 压缩为（文件名）.zip
///   6. 用MantisZip压缩
/// </summary>
internal static class ShellIntegration
{
    private static readonly string[] ArchiveExtensions =
        [".zip", ".7z", ".rar", ".tar", ".tgz", ".tar.gz", ".gz", ".iso"];

    // Registry key identifiers — must stay as fixed English strings (language-independent)
    private const string CascadeRoot = "MantisZip";
    private const string ProgId = "MantisZip.Archive";

    // Verb names (numbered prefix for verb-mode alphabetical order)
    private const string OpenVerb = "01_MantisZipOpen";
    private const string ExtractHereVerb = "02_MantisZipExtractHere";
    private const string ExtractToNamedVerb = "03_MantisZipExtractToNamed";
    private const string ExtractVerb = "04_MantisZipExtract";
    private const string QuickVerb = "05_MantisZipQuick";
    private const string CompressVerb = "06_MantisZipCompress";

    // Display names (shown in context menu — localized)
    private static readonly string OpenDisplay = L.T(L.Shell_Open);
    private static readonly string ExtractHereDisplay = L.T(L.Shell_ExtractHere);
    private static readonly string ExtractToNamedDisplay = L.T(L.Shell_ExtractToNamed);
    private static readonly string ExtractDisplay = L.T(L.Shell_ExtractTo);
    private static readonly string QuickDisplay = L.T(L.Shell_QuickCompress);
    private static readonly string CompressDisplay = L.T(L.Shell_Compress);

    /// <summary>检查是否已L.T(L.Settings_Menu_Btn_Install) Shell 扩展。</summary>
    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\*\shell\{CascadeRoot}");
            if (key != null) return true;
            using var key2 = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\*\shell\{CompressVerb}\command");
            return key2 != null;
        }
    }

    /// <summary>
    /// L.T(L.Settings_Menu_Btn_Install) Shell 右键菜单。根据 AppSettings 中的开关决定层叠/独立模式及各动词启用L.T(L.Settings_Menu_StatusGroup)。
    /// </summary>
    public static void Install()
    {
        App.LogDebug("ShellIntegration.Install: starting");
        var s = AppSettings.Instance;
        var exePath = GetExePath();

        // 先L.T(L.Settings_Menu_Btn_Uninstall)旧注册，避免残留
        Uninstall();

        if (s.EnableCascadingMenu)
            InstallCascade(s, exePath);
        else
            InstallVerbs(s, exePath);
        App.LogDebug("ShellIntegration.Install: done, cascade={0}, exePath={1}", s.EnableCascadingMenu, exePath);
    }

    /// <summary>
    /// L.T(L.Settings_Menu_Btn_Uninstall)所有 L.T(L.App_MantisZipTitle) 右键菜单注册。
    /// </summary>
    public static void Uninstall()
    {
        App.LogDebug("ShellIntegration.Uninstall: starting");
        // 层叠入口
        foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
        {
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CascadeRoot}");
        }

        // 层叠子命令定义
        DeleteRegistryKey($@"Software\Classes\{CascadeRoot}");

        // 独立动词（含新旧 verb L.T(L.Main_Col_Name)全L.T(L.CompressConflict_Overwrite)，升级时清理旧版注册）
        foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
        {
            // 新名称
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{OpenVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractHereVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractToNamedVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{QuickVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressVerb}");

            // 旧版动词L.T(L.Main_Col_Name)（v0.1.3 及以前）
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipOpen");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipExtract");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipQuick");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipCompress");
        }

        // 旧版 per-extension 注册（v0.1.3 早期版本遗留）
        foreach (var ext in ArchiveExtensions)
        {
            DeleteRegistryKey($@"Software\Classes\{ext}\shell\MantisZipExtract");
        }
        App.LogDebug("ShellIntegration.Uninstall: done");
    }

    #region 层叠子菜单模式 (ExtendedSubCommandsKey)

    private static void InstallCascade(AppSettings s, string exePath)
    {
        // 每个目标类使用独立的子命令 key，因为 %1 / %V 不同
        InstallCascadeFor("*",                  "File",      s, exePath, argVar: "%1", includeExtract: true);
        InstallCascadeFor("Directory",           "Directory", s, exePath, argVar: "%1", includeExtract: false);
        InstallCascadeFor(@"Directory\Background", "Background", s, exePath, argVar: "%V", includeExtract: false);
    }

    private static void InstallCascadeFor(string target, string subKeySuffix, AppSettings s, string exePath, string argVar, bool includeExtract)
    {
        // 顶级层叠入口：MUIVerb + ExtendedSubCommandsKey
        var entryPath = $@"Software\Classes\{target}\shell\{CascadeRoot}";
        SetRegistryValue(entryPath, "MUIVerb", L.T(L.App_MantisZipTitle));

        var subCommandsPath = $@"{CascadeRoot}\{subKeySuffix}";
        SetRegistryValue(entryPath, "ExtendedSubCommandsKey", subCommandsPath);

        if (s.ShowMenuIcons)
            SetRegistryValue(entryPath, "Icon", $"""{exePath},0""");

        // 子命令：{CascadeRoot}\{suffix}\shell\{order_name}\command
        var shellPath = $@"Software\Classes\{subCommandsPath}\shell";
        int order = 0;

        // 1. 用L.T(L.App_MantisZipTitle)L.T(L.Main_Toolbar_Open)（仅L.T(L.Compress_Archive_Group)）
        if (s.EnableOpenMenu)
        {
            order++;
            var verb = $"{order:D2}_open";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, OpenDisplay);
            SetRegistryValue(verbPath, "AppliesTo", BuildAppliesToFilter());
            if (s.ShowMenuIcons)
                SetRegistryValue(verbPath, "Icon", $"""{exePath},0""");
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --open ""{argVar}""");
        }

        // L.T(L.Settings_Tab_Extract)相关动词（仅L.T(L.Compress_Archive_Group)；受 EnableExtractMenu 统一控制）
        if (includeExtract && s.EnableExtractMenu)
        {
            // 2. 用MantisZip解压到此处
            {
                order++;
                var verb = $"{order:D2}_extracthere";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractHereDisplay);
                SetRegistryValue(verbPath, "AppliesTo", BuildAppliesToFilter());
                if (s.ShowMenuIcons)
                    SetRegistryValue(verbPath, "Icon", $"""{exePath},0""");
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract-here ""{argVar}""");
            }

            // 3. 用L.T(L.App_MantisZipTitle)L.T(L.Shell_ExtractToNamed)
            {
                order++;
                var verb = $"{order:D2}_extracttonamed";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractToNamedDisplay);
                SetRegistryValue(verbPath, "AppliesTo", BuildAppliesToFilter());
                if (s.ShowMenuIcons)
                    SetRegistryValue(verbPath, "Icon", $"""{exePath},0""");
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract-to-name ""{argVar}""");
            }

            // 4. 用L.T(L.App_MantisZipTitle)L.T(L.Shell_ExtractTo)
            {
                order++;
                var verb = $"{order:D2}_extract";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractDisplay);
                SetRegistryValue(verbPath, "AppliesTo", BuildAppliesToFilter());
                if (s.ShowMenuIcons)
                    SetRegistryValue(verbPath, "Icon", $"""{exePath},0""");
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract ""{argVar}""");
            }
        }

        // 5. L.T(L.Shell_QuickCompress)
        if (s.EnableQuickCompress)
        {
            order++;
            var verb = $"{order:D2}_quick";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, QuickDisplay);
            if (s.ShowMenuIcons)
                SetRegistryValue(verbPath, "Icon", $"""{exePath},0""");
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --compress-quick ""{argVar}""");
        }

        // 6. 用L.T(L.App_MantisZipTitle)L.T(L.Shell_Compress)
        if (s.EnableCompressMenu)
        {
            order++;
            var verb = $"{order:D2}_compress";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, CompressDisplay);
            if (s.ShowMenuIcons)
                SetRegistryValue(verbPath, "Icon", $"""{exePath},0""");
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --compress ""{argVar}""");
        }
    }

    #endregion

    #region 独立动词模式

    private static void InstallVerbs(AppSettings s, string exePath)
    {
        // 动词按用户要求的顺序注册，使用编号前缀控制排序

        // ——— 1. 用MantisZip打开（仅压缩包）———
        if (s.EnableOpenMenu)
        {
            InstallVerb("*", OpenVerb, OpenDisplay, $@"""{exePath}"" --open ""%1""", s.ShowMenuIcons, exePath, BuildAppliesToFilter());
        }

        // ——— 2-4. L.T(L.Settings_Tab_Extract)动词（仅L.T(L.Compress_Archive_Group)；受 EnableExtractMenu 统一控制）———
        if (s.EnableExtractMenu)
        {
            InstallVerb("*", ExtractHereVerb, ExtractHereDisplay, $@"""{exePath}"" --extract-here ""%1""", s.ShowMenuIcons, exePath, BuildAppliesToFilter());
            InstallVerb("*", ExtractToNamedVerb, ExtractToNamedDisplay, $@"""{exePath}"" --extract-to-name ""%1""", s.ShowMenuIcons, exePath, BuildAppliesToFilter());
            InstallVerb("*", ExtractVerb, ExtractDisplay, $@"""{exePath}"" --extract ""%1""", s.ShowMenuIcons, exePath, BuildAppliesToFilter());
        }

        // ——— 5. L.T(L.Shell_QuickCompress) ———
        if (s.EnableQuickCompress)
        {
            InstallVerb("*", QuickVerb, QuickDisplay, $@"""{exePath}"" --compress-quick ""%1""", s.ShowMenuIcons, exePath);
        }

        // ——— 6. 用L.T(L.App_MantisZipTitle)L.T(L.Shell_Compress) ———
        if (s.EnableCompressMenu)
        {
            InstallVerb("*", CompressVerb, CompressDisplay, $@"""{exePath}"" --compress ""%1""", s.ShowMenuIcons, exePath);
        }

        // ——— Directory ———
        if (s.EnableCompressMenu)
            InstallVerb("Directory", CompressVerb, CompressDisplay, $@"""{exePath}"" --compress ""%1""", s.ShowMenuIcons, exePath);

        // ——— Directory\Background ———
        if (s.EnableCompressMenu)
            InstallVerb(@"Directory\Background", CompressVerb, CompressDisplay, $@"""{exePath}"" --compress ""%V""", s.ShowMenuIcons, exePath);
    }

    private static void InstallVerb(string target, string verbName, string displayName, string command, bool showIcon, string exePath, string? appliesTo = null)
    {
        var key = $@"Software\Classes\{target}\shell\{verbName}";
        SetRegistryValue(key, null, displayName);
        if (showIcon)
            SetRegistryValue(key, "Icon", $"""{exePath},0""");
        if (appliesTo != null)
            SetRegistryValue(key, "AppliesTo", appliesTo);
        SetRegistryValue($@"{key}\command", null, command);
    }

    #endregion

    #region L.T(L.Settings_Tab_FileAssoc)

    /// <summary>检查L.T(L.Settings_Tab_FileAssoc)L.T(L.MsgBox_Yes)L.T(L.MsgBox_No)已L.T(L.Settings_Menu_Btn_Install)。</summary>
    public static bool AreAssociationsInstalled
    {
        get
        {
            // OpenWithProgids 下存的是值（REG_NONE），不是子项
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\.zip\OpenWithProgids");
            if (key == null) return false;
            return key.GetValue(ProgId) != null;
        }
    }

    /// <summary>
    /// 注册 ProgId 并将L.T(L.Shell_Compress)格式扩展名与之关联。
    /// 写入 HKCU\Software\Classes，无需管理员权限。
    /// </summary>
    public static void InstallAssociations()
    {
        App.LogDebug("ShellIntegration.InstallAssociations: starting");
        var exePath = GetExePath();

        // 1. 注册 ProgId
        var progIdKey = $@"Software\Classes\{ProgId}";
        SetRegistryValue(progIdKey, null, L.T(L.Shell_ProgIdDesc));
        SetRegistryValue($@"{progIdKey}\shell\open", null, L.T(L.Shell_OpenVerb));
        SetRegistryValue($@"{progIdKey}\shell\open\command", null, $@"""{exePath}"" --open ""%1""");
        SetRegistryValue($@"{progIdKey}\DefaultIcon", null, $@"""{exePath},0""");

        // 2. 注册 Applications 条目（控制"L.T(L.Main_Toolbar_Open)方式"列表中的L.T(L.Pwd_ShowBtn)L.T(L.Main_Col_Name)）
        var appKey = $@"Software\Classes\Applications\{Path.GetFileName(exePath)}";
        SetRegistryValue(appKey, "FriendlyAppName", L.T(L.App_MantisZipTitle));
        SetRegistryValue($@"{appKey}\shell\open\command", null, $@"""{exePath}"" --open ""%1""");
        foreach (var ext in ArchiveExtensions)
        {
            if (ext == ".tar.gz") continue;
            SetRegistryValue($@"{appKey}\SupportedTypes", ext, "");
        }

        // 3. 每个扩展名写 OpenWithProgids
        foreach (var ext in ArchiveExtensions)
        {
            if (ext == ".tar.gz") continue; // .tar.gz 由 .gz L.T(L.CompressConflict_Overwrite)
            var extKey = $@"Software\Classes\{ext}\OpenWithProgids";
            using var key = Registry.CurrentUser.CreateSubKey(extKey);
            key?.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        // 4. 每个扩展名L.T(L.Settings_Title)独立图标
        foreach (var ext in ArchiveExtensions)
        {
            if (ext == ".tar.gz") continue;
            var iconPath = GetIconPath(ext);
            if (iconPath != null)
                SetRegistryValue($@"Software\Classes\{ext}\DefaultIcon", null, iconPath);
        }

        App.LogDebug("ShellIntegration.InstallAssociations: done");
    }

    /// <summary>
    /// L.T(L.Settings_Assoc_Uninstall)：删除 ProgId 和扩展名的 OpenWithProgids 条目。
    /// </summary>
    public static void UninstallAssociations()
    {
        App.LogDebug("ShellIntegration.UninstallAssociations: starting");

        // 删除每个扩展名的 ProgId 条目 和 DefaultIcon
        foreach (var ext in ArchiveExtensions)
        {
            if (ext == ".tar.gz") continue;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", writable: true);
                key?.DeleteValue(ProgId, throwOnMissingValue: false);
            }
            catch { /* 该扩展可能没有 OpenWithProgids 键 */ }

            // 清理我们设置的 DefaultIcon（仅当指向我们的图标路径时）
            try
            {
                var iconPath = GetIconPath(ext);
                if (iconPath != null)
                {
                    using var extKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}", writable: true);
                    if (extKey?.GetValue(null) is string currentIcon && currentIcon.Equals(iconPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // DeleteValue(null) deletes the (default) registry value
                        // The null! suppresses nullable warning since the API is annotated incorrectly
                        extKey.DeleteValue(null!);
                    }
                }
            }
            catch { }
        }

        // 删除 Applications 条目
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "");
        if (!string.IsNullOrEmpty(exeName))
            DeleteRegistryKey($@"Software\Classes\Applications\{exeName}");

        // 删除 ProgId
        DeleteRegistryKey($@"Software\Classes\{ProgId}");

        App.LogDebug("ShellIntegration.UninstallAssociations: done");
    }

    #endregion

    #region 辅助方法

    private static string BuildAppliesToFilter()
    {
        // .tar.gz 文件的扩展名是 .gz，由 .gz 条目覆盖
        var exts = ArchiveExtensions
            .Where(e => e != ".tar.gz")
            .Select(e => $"System.FileExtension:=\"{e}\"");
        return string.Join(" OR ", exts);
    }

    private static void SetRegistryValue(string subKey, string? valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey);
        key?.SetValue(valueName, value);
    }

    private static void DeleteRegistryKey(string subKey)
    {
        Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }

    private static string GetExePath()
    {
        return Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
    }

    /// <summary>
    /// 返回扩展名对应的图标文件绝对路径。
    /// 图标文件位于输出目录的 Resources\Icons\ 下。
    /// 如果图标文件不存在，返回 null。
    /// </summary>
    private static string? GetIconPath(string extension)
    {
        var iconName = extension.ToLowerInvariant() switch
        {
            ".zip" => "zip.ico",
            ".7z" => "sevenz.ico",
            ".rar" => "rar.ico",
            ".tar" => "tar.ico",
            ".tgz" or ".tar.gz" => "tgz.ico",
            ".gz" => "gz.ico",
            ".iso" => "iso.ico",
            _ => null,
        };
        if (iconName == null) return null;

        var baseDir = Path.GetDirectoryName(GetExePath());
        if (baseDir == null) return null;

        var iconPath = Path.Combine(baseDir, "Resources", "Icons", iconName);
        return File.Exists(iconPath) ? iconPath : null;
    }

    #endregion
}
