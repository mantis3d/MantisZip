using Microsoft.Win32;

namespace MantisZip.UI;

/// <summary>
/// Shell 右键菜单集成。
/// 写入 HKCU\Software\Classes，无需管理员权限。
/// 支持层叠子菜单 (cascade) 和独立动词两种模式，由 AppSettings.EnableCascadingMenu 控制。
/// </summary>
internal static class ShellIntegration
{
    private static readonly string[] ArchiveExtensions =
        [".zip", ".7z", ".rar", ".tar", ".tgz", ".tar.gz", ".gz", ".bz2", ".cab", ".iso"];

    private const string CascadeRoot = "MantisZip";

    // Non-cascade verb names
    private const string CompressVerb = "MantisZipCompress";
    private const string QuickVerb = "MantisZipQuick";
    private const string OpenVerb = "MantisZipOpen";
    private const string ExtractVerb = "MantisZipExtract";

    /// <summary>检查是否已安装 Shell 扩展。</summary>
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
    /// 安装 Shell 右键菜单。根据 AppSettings 中的开关决定层叠/独立模式及各动词启用状态。
    /// </summary>
    public static void Install()
    {
        var s = AppSettings.Instance;
        var exePath = GetExePath();

        // 先卸载旧注册，避免残留
        Uninstall();

        if (s.EnableCascadingMenu)
            InstallCascade(s, exePath);
        else
            InstallVerbs(s, exePath);
    }

    /// <summary>
    /// 卸载所有 MantisZip 右键菜单注册。
    /// </summary>
    public static void Uninstall()
    {
        // 层叠入口
        foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
        {
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CascadeRoot}");
        }

        // 层叠子命令定义
        DeleteRegistryKey($@"Software\Classes\{CascadeRoot}");

        // 独立动词
        foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
        {
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{QuickVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{OpenVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractVerb}");
        }

        // 旧版 per-extension 注册（v0.1.3 早期版本遗留）
        foreach (var ext in ArchiveExtensions)
        {
            DeleteRegistryKey($@"Software\Classes\{ext}\shell\{ExtractVerb}");
        }
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
        SetRegistryValue(entryPath, "MUIVerb", "MantisZip");

        var subCommandsPath = $@"{CascadeRoot}\{subKeySuffix}";
        SetRegistryValue(entryPath, "ExtendedSubCommandsKey", subCommandsPath);

        if (s.ShowMenuIcons)
            SetRegistryValue(entryPath, "Icon", @"%SystemRoot%\system32\shell32.dll,3");

        // 子命令：{CascadeRoot}\{suffix}\shell\{order_name}\command
        var shellPath = $@"Software\Classes\{subCommandsPath}\shell";
        int order = 0;

        if (s.EnableCompressMenu)
        {
            order++;
            var verb = $"{order:D2}_compress";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, "用 MantisZip 压缩");
            if (s.ShowMenuIcons)
                SetRegistryValue(verbPath, "Icon", @"%SystemRoot%\system32\shell32.dll,3");
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --compress ""{argVar}""");
        }

        if (s.EnableQuickCompress)
        {
            order++;
            var verb = $"{order:D2}_quick";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, "压缩为 .zip...");
            if (s.ShowMenuIcons)
                SetRegistryValue(verbPath, "Icon", @"%SystemRoot%\system32\shell32.dll,3");
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --compress-quick ""{argVar}""");
        }

        // "打开" 仅对压缩包文件显示（AppliesTo）
        if (s.EnableOpenMenu)
        {
            order++;
            var verb = $"{order:D2}_open";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, "用 MantisZip 打开");
            SetRegistryValue(verbPath, "AppliesTo", BuildAppliesToFilter());
            if (s.ShowMenuIcons)
                SetRegistryValue(verbPath, "Icon", @"%SystemRoot%\system32\shell32.dll,3");
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --open ""{argVar}""");
        }

        if (includeExtract && s.EnableExtractMenu)
        {
            order++;
            var verb = $"{order:D2}_extract";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, "用 MantisZip 解压");
            SetRegistryValue(verbPath, "AppliesTo", BuildAppliesToFilter());
            if (s.ShowMenuIcons)
                SetRegistryValue(verbPath, "Icon", @"%SystemRoot%\system32\shell32.dll,3");
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract ""{argVar}""");
        }
    }

    #endregion

    #region 独立动词模式

    private static void InstallVerbs(AppSettings s, string exePath)
    {
        // ——— *（所有文件）———
        if (s.EnableCompressMenu)
            InstallVerb("*", CompressVerb, "用 MantisZip 压缩", $@"""{exePath}"" --compress ""%1""", s.ShowMenuIcons);
        if (s.EnableQuickCompress)
            InstallVerb("*", QuickVerb, "压缩为 .zip...", $@"""{exePath}"" --compress-quick ""%1""", s.ShowMenuIcons);
        if (s.EnableOpenMenu)
            InstallVerb("*", OpenVerb, "用 MantisZip 打开", $@"""{exePath}"" --open ""%1""", s.ShowMenuIcons, BuildAppliesToFilter());
        if (s.EnableExtractMenu)
            InstallVerb("*", ExtractVerb, "用 MantisZip 解压", $@"""{exePath}"" --extract ""%1""", s.ShowMenuIcons, BuildAppliesToFilter());

        // ——— Directory ———
        if (s.EnableCompressMenu)
            InstallVerb("Directory", CompressVerb, "用 MantisZip 压缩", $@"""{exePath}"" --compress ""%1""", s.ShowMenuIcons);
        if (s.EnableQuickCompress)
            InstallVerb("Directory", QuickVerb, "压缩为 .zip...", $@"""{exePath}"" --compress-quick ""%1""", s.ShowMenuIcons);

        // ——— Directory\Background ———
        if (s.EnableCompressMenu)
            InstallVerb(@"Directory\Background", CompressVerb, "用 MantisZip 压缩", $@"""{exePath}"" --compress ""%V""", s.ShowMenuIcons);
        if (s.EnableQuickCompress)
            InstallVerb(@"Directory\Background", QuickVerb, "压缩为 .zip...", $@"""{exePath}"" --compress-quick ""%V""", s.ShowMenuIcons);
    }

    private static void InstallVerb(string target, string verbName, string displayName, string command, bool showIcon, string? appliesTo = null)
    {
        var key = $@"Software\Classes\{target}\shell\{verbName}";
        SetRegistryValue(key, null, displayName);
        if (showIcon)
            SetRegistryValue(key, "Icon", @"%SystemRoot%\system32\shell32.dll,3");
        if (appliesTo != null)
            SetRegistryValue(key, "AppliesTo", appliesTo);
        SetRegistryValue($@"{key}\command", null, command);
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
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            path = path.Replace(".dll", ".exe", StringComparison.OrdinalIgnoreCase);
        return path ?? throw new InvalidOperationException("无法确定可执行文件路径");
    }

    #endregion
}
