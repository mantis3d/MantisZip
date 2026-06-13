using System.IO;
using Microsoft.Win32;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

internal static partial class ShellIntegration
{
    #region 文件关联

    /// <summary>检查文件关联是否已安装。</summary>
    public static bool AreAssociationsInstalled => ArchiveExtensions.Any(AreAssociationsInstalledForExtension);

    /// <summary>检查指定的扩展名是否已安装文件关联。</summary>
    public static bool AreAssociationsInstalledForExtension(string ext)
    {
        var progId = GetProgId(ext);
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids");
        if (key == null) return false;
        return key.GetValue(progId) != null;
    }

    /// <summary>检查指定扩展名的关联状态（无关联/关联未默认/关联且默认）。</summary>
    public static AssocStatus GetAssociationStatus(string ext)
    {
        if (!AreAssociationsInstalledForExtension(ext))
            return AssocStatus.NotAssociated;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice");
            var progId = key?.GetValue("Progid") as string;
            if (!string.IsNullOrEmpty(progId) && progId.IndexOf("MantisZip", StringComparison.OrdinalIgnoreCase) >= 0)
                return AssocStatus.IsDefault;
        }
        catch { }

        return AssocStatus.NotDefault;
    }

    /// <summary>为单个扩展名安装文件关联（OpenWithProgids + DefaultIcon + 独立 ProgId）。</summary>
    public static void InstallAssociationForExtension(string ext)
    {
        var progId = GetProgId(ext);

        // 1. 注册独立 ProgId（含具体图标和打开命令）
        EnsureProgIdRegistered(ext);

        // 2. 写 OpenWithProgids
        var extKey = $@"Software\Classes\{ext}\OpenWithProgids";
        using var key = Registry.CurrentUser.CreateSubKey(extKey);
        key?.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);

        // 3. 写 DefaultIcon
        var iconPath = GetIconPath(ext);
        if (iconPath != null)
        {
            SetRegistryValue($@"Software\Classes\{ext}\DefaultIcon", null, iconPath);
        }
        else
        {
            // 自定义扩展名 — 使用软件自身图标
            var exePath = GetExePath();
            if (exePath != null)
                SetRegistryValue($@"Software\Classes\{ext}\DefaultIcon", null, $@"""{exePath}"",0");
        }
    }

    /// <summary>注册 Applications 条目（全局，仅一次）。</summary>
    private static void EnsureApplicationsRegistered()
    {
        App.LogDebug("ShellIntegration.EnsureApplicationsRegistered: starting");
        var exePath = GetExePath();

        var appKey = $@"Software\Classes\Applications\{Path.GetFileName(exePath)}";
        SetRegistryValue(appKey, "FriendlyAppName", L.T(L.App_MantisZipTitle));
        SetRegistryValue($@"{appKey}\shell\open\command", null, $@"""{exePath}"" --open ""%1""");
        foreach (var ext in ArchiveExtensions)
        {
            if (ext == ".tar.gz") continue;
            SetRegistryValue($@"{appKey}\SupportedTypes", ext, "");
        }

        App.LogDebug("ShellIntegration.EnsureApplicationsRegistered: done");
    }

    /// <summary>注册一个扩展名的独立 ProgId（含图标和打开命令）。</summary>
    private static void EnsureProgIdRegistered(string ext)
    {
        var progId = GetProgId(ext);
        var exePath = GetExePath();
        var progIdKey = $@"Software\Classes\{progId}";

        // 只有需要创建时才写入，避免覆盖用户的自定义设置
        using var check = Registry.CurrentUser.OpenSubKey(progIdKey);
        if (check != null) return;

        // 友好名称
        var displayName = L.T(L.Shell_ProgIdDesc) + " — " + ext.TrimStart('.');
        SetRegistryValue(progIdKey, null, displayName);
        SetRegistryValue($@"{progIdKey}\shell\open", null, L.T(L.Shell_OpenVerb));
        SetRegistryValue($@"{progIdKey}\shell\open\command", null, $@"""{exePath}"" --open ""%1""");

        // 具体格式图标
        var iconPath = GetIconPath(ext);
        if (iconPath != null)
            SetRegistryValue($@"{progIdKey}\DefaultIcon", null, iconPath);
        else
            SetRegistryValue($@"{progIdKey}\DefaultIcon", null, $@"""{exePath},0""");
    }

    /// <summary>清理旧的 MantisZip.Archive 单 ProgId（v0.5 以前版本迁移用）。</summary>
    public static void CleanupOldProgId()
    {
        DeleteRegistryKey($@"Software\Classes\{OldProgId}");
    }

    /// <summary>安装文件关联前的预备工作：迁移旧版 + 注册 Applications 条目。</summary>
    public static void PrepareAssocRegistration()
    {
        CleanupOldProgId();
        EnsureApplicationsRegistered();
    }

    /// <summary>
    /// 各格式扩展名逐个关联独立 ProgId。
    /// 写入 HKCU\Software\Classes，无需管理员权限。
    /// </summary>
    /// <param name="extensions">要安装的扩展名列表。null 表示安装所有内置扩展名。</param>
    public static void InstallAssociations(IEnumerable<string>? extensions = null)
    {
        App.LogDebug("ShellIntegration.InstallAssociations: starting");

        // 清理旧的单 ProgId（v0.5 以前版本迁移）
        CleanupOldProgId();

        // 注册 Applications 条目
        EnsureApplicationsRegistered();

        // 逐个安装独立 ProgId + 文件关联
        var targets = extensions ?? (IEnumerable<string>)ArchiveExtensions;
        foreach (var ext in targets)
        {
            InstallAssociationForExtension(ext);
        }

        App.LogDebug("ShellIntegration.InstallAssociations: done");
    }

    /// <summary>删除单个扩展名的文件关联（OpenWithProgids + DefaultIcon + 独立 ProgId）。</summary>
    public static void UninstallAssociationForExtension(string ext)
    {
        var progId = GetProgId(ext);

        // 删除 OpenWithProgids 条目
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", writable: true);
            key?.DeleteValue(progId, throwOnMissingValue: false);
        }
        catch { /* 该扩展可能没有 OpenWithProgids 键 */ }

        // 删除 DefaultIcon（仅当指向我们的图标路径时）
        try
        {
            var iconPath = GetIconPath(ext);
            var exePath = GetExePath();
            using var extKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}", writable: true);
            if (extKey?.GetValue(null) is string currentIcon)
            {
                if (iconPath != null && currentIcon.Equals(iconPath, StringComparison.OrdinalIgnoreCase))
                {
                    extKey.DeleteValue(null!);
                }
                else if (exePath != null && currentIcon.Equals($@"""{exePath}"",0", StringComparison.OrdinalIgnoreCase))
                {
                    extKey.DeleteValue(null!);
                }
            }
        }
        catch { }

        // 删除独立 ProgId 键（仅当该 ProgId 不被其他扩展引用时）
        DeleteRegistryKey($@"Software\Classes\{progId}");
    }

    /// <summary>
    /// 卸载所有文件关联：删除所有独立 ProgId 和扩展名的 OpenWithProgids 条目。
    /// </summary>
    public static void UninstallAssociations()
    {
        App.LogDebug("ShellIntegration.UninstallAssociations: starting");

        // 删除每个内置扩展名的 OpenWithProgids + DefaultIcon + 独立 ProgId
        foreach (var ext in ArchiveExtensions)
        {
            UninstallAssociationForExtension(ext);
        }

        // 也清理自定义扩展名（残留）
        if (Registry.CurrentUser.OpenSubKey($@"Software\Classes\{CustomProgId}") != null)
        {
            DeleteRegistryKey($@"Software\Classes\{CustomProgId}");
        }

        // 清理 Applications 条目（可选，遗留清理）
        var exePath = GetExePath();
        if (exePath != null)
        {
            DeleteRegistryKey($@"Software\Classes\Applications\{Path.GetFileName(exePath)}");
        }

        App.LogDebug("ShellIntegration.UninstallAssociations: done");
    }

    /// <summary>返回已安装文件关联的扩展名数量（用于 UI 状态显示）。</summary>
    public static int GetInstalledExtensionCount()
    {
        // .tgz 不在 BuiltinAssocFormats 中，不计入 UI 状态总数
        return ArchiveExtensions
            .Where(e => e != ".tgz")
            .Count(AreAssociationsInstalledForExtension);
    }

    /// <summary>读取指定扩展名的当前默认打开程序，返回友好显示名称。</summary>
    public static string GetCurrentHandler(string ext)
    {
        try
        {
            var userChoice = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice";
            using var key = Registry.CurrentUser.OpenSubKey(userChoice);
            var progId = key?.GetValue("Progid") as string;

            if (string.IsNullOrEmpty(progId))
                return "未设置";

            // 检查是否是我们
            if (progId.Contains("MantisZip", StringComparison.OrdinalIgnoreCase))
                return "MantisZip";

            // Applications\xxx.exe 格式
            if (progId.StartsWith("Applications\\", StringComparison.OrdinalIgnoreCase))
            {
                var exeName = progId.Substring("Applications\\".Length);
                return Path.GetFileNameWithoutExtension(exeName);
            }

            // 尝试从 ProgId 获取友好名称
            try
            {
                using var progIdKey = Registry.ClassesRoot.OpenSubKey(progId);
                var displayName = progIdKey?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(displayName))
                    return CleanAppName(displayName);
            }
            catch { }

            // 去除扩展名后缀："Bandizip.zip" → "Bandizip"
            var knownExts = new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".iso" };
            foreach (var knownExt in knownExts)
            {
                if (progId.EndsWith(knownExt, StringComparison.OrdinalIgnoreCase))
                    return progId.Substring(0, progId.Length - knownExt.Length);
            }

            return progId;
        }
        catch
        {
            return "未设置";
        }
    }

    /// <summary>清理 ProgId 友好名称中常见的格式描述后缀。</summary>
    private static string CleanAppName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        // Strip common suffixes that make the result look like a format description
        string[] suffixes = { " Archive", " Compressed", " File", " Document", " Image" };
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - suffix.Length);
        }

        return name;
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
            ".tgz" or ".tar.gz" => "targz.ico",
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

public enum AssocStatus
{
    NotAssociated,
    NotDefault,
    IsDefault
}
