using System.IO;
using Microsoft.Win32;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

internal static partial class ShellIntegration
{
    #region 文件关联

    /// <summary>检查文件关联是否已安装。</summary>
    public static bool AreAssociationsInstalled => ArchiveExtensions.Any(AreAssociationsInstalledForExtension);

    /// <summary>检查指定的扩展名是否已安装文件关联。</summary>
    public static bool AreAssociationsInstalledForExtension(string ext)
    {
        var progId = GetProgId(ext);
        using var key = RegistryHost.Open($@"Software\Classes\{ext}\OpenWithProgids");
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
            using var key = RegistryHost.Open($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice");
            var progId = key?.GetValue("Progid") as string;
            if (!string.IsNullOrEmpty(progId) && progId.IndexOf("MantisZip", StringComparison.OrdinalIgnoreCase) >= 0)
                return AssocStatus.IsDefault;
        }
        catch (Exception ex) { App.DebugLog($"ShellIntegration.GetAssociationStatus: failed: {ex.Message}"); }

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
        using var key = RegistryHost.Create(extKey);
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
        App.DebugLog("ShellIntegration.EnsureApplicationsRegistered: starting");
        var exePath = GetExePath();

        var appKey = $@"Software\Classes\Applications\{Path.GetFileName(exePath)}";
        SetRegistryValue(appKey, "FriendlyAppName", LocalizationManager.T("App_MantisZipTitle"));

        // 防御性删除旧的 shell\open\command（v0.7.x 之前的版本注册过此键），
        // 避免软件安装时 Shell 关联刷新导致 .exe 的打开操作被错误地路由到 Applications
        // 入口来执行。OpenWithProgids + 各格式独立 ProgId 已能覆盖所有正常打开场景，
        // 不再需要此命令入口。
        DeleteRegistryKey($@"{appKey}\shell\open\command");

        // 注意：仅注册 SupportedTypes，不注册 shell\open\command（已在上方删除）。
        // SupportedTypes 用于"打开方式"对话框的候选列表，不含实际执行命令。
        foreach (var ext in ArchiveExtensions)
        {
            if (ext == ".tar.gz") continue;
            SetRegistryValue($@"{appKey}\SupportedTypes", ext, "");
        }

        App.DebugLog("ShellIntegration.EnsureApplicationsRegistered: done");
    }

    /// <summary>注册一个扩展名的独立 ProgId（含图标和打开命令）。</summary>
    private static void EnsureProgIdRegistered(string ext)
    {
        var progId = GetProgId(ext);
        var exePath = GetExePath();
        var progIdKey = $@"Software\Classes\{progId}";

        bool isNew;
        using (var check = RegistryHost.Open(progIdKey))
        {
            isNew = check == null;
        }

        if (isNew)
        {
            // 友好名称与动词标签仅首次创建时写入，避免覆盖用户的自定义设置
            var displayName = LocalizationManager.T("Shell_ProgIdDesc") + " — " + ext.TrimStart('.');
            SetRegistryValue(progIdKey, null, displayName);
            SetRegistryValue($@"{progIdKey}\shell\open", null, LocalizationManager.T("Shell_OpenVerb"));
        }

        // 打开命令与图标每次安装都刷新（自愈）：
        // 1) 旧版本在格式图标文件缺失时写入的是 exe 兜底图标，补齐文件后重装关联即可更新；
        // 2) 便携版换目录后 command 指向的旧路径失效，重新安装关联即可修复。
        SetRegistryValue($@"{progIdKey}\shell\open\command", null, $@"""{exePath}"" --open-dispatch ""%1""");

        var iconPath = GetIconPath(ext);
        if (iconPath != null)
            SetRegistryValue($@"{progIdKey}\DefaultIcon", null, iconPath);
        else
            SetRegistryValue($@"{progIdKey}\DefaultIcon", null, $@"""{exePath},0""");

        // 写完关联键后必须通知 Shell，否则图标 / "打开方式" 列表沿用旧缓存
        NotifyAssocChanged();
    }

    /// <summary>
    /// 清理旧的 MantisZip.Archive 单 ProgId（v0.5 以前版本迁移用），
    /// 并清理各格式 OpenWithProgids 下遗留的该旧 ProgId 值。
    /// </summary>
    public static void CleanupOldProgId()
    {
        DeleteRegistryKey($@"Software\Classes\{OldProgId}");

        // 旧版单 ProgId 时代在 OpenWithProgids 里留下的值也要清（只删键不删值会永久残留，
        // 且该值指向已删除的 ProgId，导致"打开方式"里出现一个打不开的条目）。
        foreach (var ext in ArchiveExtensions)
        {
            try
            {
                using var key = RegistryHost.Open($@"Software\Classes\{ext}\OpenWithProgids", writable: true);
                key?.DeleteValue(OldProgId, throwOnMissingValue: false);
            }
            catch (Exception ex) { App.DebugLog($"ShellIntegration.CleanupOldProgId: failed to clear {ext} OpenWithProgids: {ex.Message}"); }
        }

        NotifyAssocChanged();
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
        App.DebugLog("ShellIntegration.InstallAssociations: starting");

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

        App.DebugLog("ShellIntegration.InstallAssociations: done");
    }

    /// <summary>删除单个扩展名的文件关联（OpenWithProgids + DefaultIcon + 独立 ProgId）。</summary>
    public static void UninstallAssociationForExtension(string ext)
    {
        var progId = GetProgId(ext);

        // 删除 OpenWithProgids 条目
        try
        {
            using var key = RegistryHost.Open($@"Software\Classes\{ext}\OpenWithProgids", writable: true);
            key?.DeleteValue(progId, throwOnMissingValue: false);
        }
        catch (Exception ex) { App.DebugLog($"ShellIntegration.UninstallAssociationForExtension: failed to delete OpenWithProgids: {ex.Message}"); }

        // 删除 DefaultIcon（仅当指向我们的图标路径时）
        try
        {
            var iconPath = GetIconPath(ext);
            var exePath = GetExePath();
            using var iconKey = RegistryHost.Open($@"Software\Classes\{ext}\DefaultIcon", writable: true);
            if (iconKey?.GetValue(null) is string currentIcon && !string.IsNullOrWhiteSpace(currentIcon))
            {
                // 判据放宽（命中任一即清）：
                //   ① 与安装时写入的现值相等；
                //   ② 与快照（"exe",0）相等；
                //   ③ 指向的文件不存在（安装目录变更、或测试输出目录被 dotnet clean 清掉）；
                //   ④ 值路径含 \Resources\Icons\ **且** basename ∈ {zip,sevenz,rar,tar,targz,gz,iso}.ico ——
                //      加 basename 白名单是为**避免误删第三方图标**；该收窄不影响命中现场残留
                //      （残留值为 …\…\Resources\Icons\zip.ico，basename 为 zip.ico ∈ 白名单）。
                bool matchesCurrent  = iconPath != null && currentIcon.Equals(iconPath, StringComparison.OrdinalIgnoreCase);
                bool matchesSnapshot = exePath != null && currentIcon.Equals($@"""{exePath}"",0", StringComparison.OrdinalIgnoreCase);
                bool pointsToMissing = !File.Exists(TrimIconIndex(currentIcon));
                bool isOurIconDir    = currentIcon.IndexOf(@"\Resources\Icons\", StringComparison.OrdinalIgnoreCase) >= 0
                    && new[] { "zip.ico", "sevenz.ico", "rar.ico", "tar.ico", "targz.ico", "gz.ico", "iso.ico" }
                           .Contains(Path.GetFileName(TrimIconIndex(currentIcon)), StringComparer.OrdinalIgnoreCase);

                if (matchesCurrent || matchesSnapshot || pointsToMissing || isOurIconDir)
                    iconKey.DeleteValue(null!);
            }
        }
        catch (Exception ex) { App.DebugLog($"ShellIntegration.UninstallAssociationForExtension: failed to delete DefaultIcon: {ex.Message}"); }

        // 删除独立 ProgId 键（仅当该 ProgId 不被其他扩展引用时）
        DeleteRegistryKey($@"Software\Classes\{progId}");

        // 清理"悬空默认"：仅当 UserChoice 指向我们的 ProgId 时删除该键。
        // UserChoice 的 SetValue 权限被系统 DENY，但删除整键是允许的——
        // 删掉后 Windows 干净回退，而不是保留一个指向已删 ProgId 的幽灵默认。
        try
        {
            var ucPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice";
            string? curProgId;
            using (var uc = RegistryHost.Open(ucPath))
                curProgId = uc?.GetValue("Progid") as string;

            if (!string.IsNullOrEmpty(curProgId) &&
                curProgId.StartsWith("MantisZip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    RegistryHost.Delete(ucPath);
                }
                catch (Exception ex)
                {
                    App.DebugLog($"ShellIntegration.UninstallAssociationForExtension: failed to clear UserChoice: {ex.Message}");
                    // best-effort：不清理 UserChoice 不应中断卸载流程
                }
            }
        }
        catch (Exception ex) { App.DebugLog($"ShellIntegration.UninstallAssociationForExtension: failed to clear UserChoice: {ex.Message}"); }

        // 卸载后同样要通知 Shell，否则"打开方式"里仍挂着已删除的条目
        NotifyAssocChanged();
    }

    /// <summary>
    /// 卸载所有文件关联：删除所有独立 ProgId 和扩展名的 OpenWithProgids 条目。
    /// </summary>
    public static void UninstallAssociations()
    {
        App.DebugLog("ShellIntegration.UninstallAssociations: starting");

        // 删除每个内置扩展名的 OpenWithProgids + DefaultIcon + 独立 ProgId
        foreach (var ext in ArchiveExtensions)
        {
            UninstallAssociationForExtension(ext);
        }

        // 也清理自定义扩展名（残留）
        if (RegistryHost.Open($@"Software\Classes\{CustomProgId}") != null)
        {
            DeleteRegistryKey($@"Software\Classes\{CustomProgId}");
        }

        // 清理 Applications 条目（可选，遗留清理）
        var exePath = GetExePath();
        if (exePath != null)
        {
            DeleteRegistryKey($@"Software\Classes\Applications\{Path.GetFileName(exePath)}");
        }

        // 自定义 ProgId / Applications 清理发生在最后一次逐扩展通知之后，收口点统一补一次通知
        NotifyAssocChanged();

        App.DebugLog("ShellIntegration.UninstallAssociations: done");
    }

    /// <summary>
    /// 启动自愈：对每个内置扩展名，**仅当同时满足以下三条（AND）**时，才重建缺失的 per-format ProgId：
    ///   1) `UserChoice.Progid` 以 `MantisZip` 开头（注册表事实：用户曾把我们设为默认）；
    ///   2) `Software\Classes\{ProgId}` 键缺失（确实损坏）；
    ///   3) 该格式未被用户在设置里显式禁用（`IsAssocEnabledInSettings` 为 true）。
    /// 注意：**不是 OR**——第 1 条是必需的注册表事实，第 3 条只是"用户未显式禁用"的闸门，
    /// 单独满足"设置里启用"而无注册表痕迹时**不得**自愈（否则会凭空给从未关联过的格式写注册表）。
    /// 本三条 AND 即判据的权威口径，与代码内注释一致。
    ///
    /// 只重建 ProgId + OpenWithProgids + DefaultIcon，**从不写 UserChoice**（哈希保护），
    /// 因此绝不会覆盖用户在 Windows 里对其他程序的选择——只把自己丢失的注册补回来。
    /// 若 UserChoice 仍指向我们，重建 ProgId 后双击默认即自动恢复。
    /// </summary>
    public static void SelfHealAssociations(AppSettings s)
    {
        foreach (var ext in ArchiveExtensions)
        {
            try
            {
                var progId = GetProgId(ext);
                bool userChoseUs;
                using (var uc = RegistryHost.Open($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice"))
                    userChoseUs = (uc?.GetValue("Progid") as string)
                        ?.StartsWith("MantisZip", StringComparison.OrdinalIgnoreCase) == true;

                // 触发判据（三条 AND）：
                //   1) UserChoice.Progid 以 MantisZip 开头（注册表痕迹）
                //   2) Software\Classes\{ProgId} 键缺失（确实损坏）
                //   3) 该格式未被用户在设置里显式禁用
                if (!userChoseUs) continue;                        // 无我们的痕迹 → 不自愈
                if (!IsAssocEnabledInSettings(s, ext)) continue;   // 显式禁用 → 不自愈

                bool progIdMissing;
                using (var k = RegistryHost.Open($@"Software\Classes\{progId}"))
                    progIdMissing = k == null;

                if (progIdMissing)
                {
                    App.DebugLog($"ShellIntegration.SelfHealAssociations: rebuilding missing ProgId for {ext} (userChoseUs={userChoseUs})");
                    InstallAssociationForExtension(ext);
                    // 自愈成功后注册应用
                    EnsureApplicationsRegistered();
                    // 重建后通知 Shell，双击默认才会立即用回 MantisZip（否则仍读旧缓存）
                    NotifyAssocChanged();
                }
                // 额外加固：ProgId 键存在但 shell\open\command 指向失效路径时也可考虑重建（本计划先标记，后续如需再细化）
            }
            catch (Exception ex) { App.DebugLog($"ShellIntegration.SelfHealAssociations: failed for {ext}: {ex.Message}"); }
        }
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
            using var key = RegistryHost.Open(userChoice);
            var progId = key?.GetValue("Progid") as string;

            if (string.IsNullOrEmpty(progId))
                return LocalizationManager.T("Settings_Assoc_CurrentHandler_None");

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
                // HKCR 只读查询，不在 HKCU 写入收口范围，沙箱不适用
                using var progIdKey = Registry.ClassesRoot.OpenSubKey(progId);
                var displayName = progIdKey?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(displayName))
                    return CleanAppName(displayName);
            }
            catch (Exception ex) { App.DebugLog($"ShellIntegration.GetCurrentHandler: failed to read ProgId friendly name: {ex.Message}"); }

            // 去除扩展名后缀："Bandizip.zip" → "Bandizip"
            var knownExts = new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".iso" };
            foreach (var knownExt in knownExts)
            {
                if (progId.EndsWith(knownExt, StringComparison.OrdinalIgnoreCase))
                    return progId.Substring(0, progId.Length - knownExt.Length);
            }

            return progId;
        }
        catch (Exception ex)
        {
            App.DebugLog($"ShellIntegration.GetCurrentHandler: failed to read handler: {ex.Message}");
            return LocalizationManager.T("Settings_Assoc_CurrentHandler_None");
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

    /// <summary>从图标注册表值中提取文件路径：去掉引号与 ",0" / ",1" 索引后缀。</summary>
    private static string TrimIconIndex(string rawValue)
    {
        var value = rawValue.Trim();
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            if (end > 1) return value.Substring(1, end - 1);
        }
        var comma = value.LastIndexOf(',');
        return comma > 0 ? value.Substring(0, comma) : value;
    }

    #endregion
}

public enum AssocStatus
{
    NotAssociated,
    NotDefault,
    IsDefault
}
