using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// Shell 右键菜单集成。
/// 写入 HKCU\Software\Classes，无需管理员权限。
/// 始终使用层叠子菜单模式（动词模式已在 v0.4.0 移除）。
/// 菜单顺序：
///   1. 用MantisZip打开
///   2. 用MantisZip解压到此处
///   3. 用MantisZip解压到（压缩包名）
///   4. 用MantisZip解压到……
///   5. 压缩到独立的（文件名）
///   6. 压缩到（父目录名）
///   7. 用MantisZip压缩
/// </summary>
internal static class ShellIntegration
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
    private static readonly string[] ArchiveExtensions =
        ArchiveEngineFactory.SupportedExtensions;

    // Registry key identifiers — must stay as fixed English strings (language-independent)
    private const string CascadeRoot = "MantisZip";
    private const string ProgId = "MantisZip.Archive";

    // Verb names (仅用于 Uninstall 清理旧注册，动词模式已在 v0.4.0 移除)
    private const string OpenVerb = "01_MantisZipOpen";
    private const string ExtractHereVerb = "02_MantisZipExtractHere";
    private const string ExtractSmartVerb = "02_MantisZipSmartExtract";
    private const string ExtractToNamedVerb = "03_MantisZipExtractToNamed";
    private const string ExtractVerb = "04_MantisZipExtract";
    private const string CompressSeparateVerb = "05_MantisZipCompressSeparate";
    private const string CompressCombinedVerb = "06_MantisZipCompressCombined";
    private const string CompressVerb = "07_MantisZipCompress";

    // Display names (shown in context menu — localized)
    private static readonly string OpenDisplay = L.T(L.Shell_Open);
    private static readonly string ExtractHereDisplay = L.T(L.Shell_ExtractHere);
    private static readonly string ExtractSmartDisplay = L.T(L.Shell_SmartExtract);
    private static readonly string ExtractToNamedDisplay = L.T(L.Shell_ExtractToNamed);
    private static readonly string ExtractDisplay = L.T(L.Shell_ExtractTo);
    private static readonly string CompressSeparateDisplay = L.T(L.Shell_CompressSeparate);
    private static readonly string CompressCombinedDisplay = L.T(L.Shell_CompressCombined);
    private static readonly string CompressDisplay = L.T(L.Shell_Compress);

    /// <summary>检查是否已L.T(L.Settings_Menu_Btn_Install) Shell 扩展（COM 或静态注册表）。</summary>
    public static bool IsInstalled
    {
        get
        {
            // 检查 COM 注册（优先）
            using var comKey = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\CLSID\{ComClsid}");
            if (comKey != null) return true;

            // 回退：检查静态注册表
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\*\shell\{CascadeRoot}");
            if (key != null) return true;
            using var key2 = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\*\shell\{CompressVerb}\command");
            if (key2 != null) return true;
            using var key3 = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\*\shell\{ExtractSmartVerb}\command");
            if (key3 != null) return true;
            using var key4 = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\*\shell\{CompressSeparateVerb}\command");
            return key4 != null;
        }
    }

    /// <summary>
    /// L.T(L.Settings_Menu_Btn_Install) Shell 右键菜单。
    /// 使用 COM IContextMenu 实现（MantisZip.ShellExt.comhost.dll），
    /// 回退到静态注册表方案（旧版兼容）。
    /// </summary>
    public static void Install()
    {
        App.LogDebug("ShellIntegration.Install: starting");
        var s = AppSettings.Instance;
        var exePath = GetExePath();

        // 先清理旧注册，避免残留
        Uninstall();

        // 尝试安装 COM 组件（MantisZip.ShellExt.comhost.dll）
        if (InstallCom())
        {
            App.LogDebug("ShellIntegration.Install: COM context menu installed");
        }
        else
        {
            // 回退到静态注册表方案（仅级联模式，动词模式已在 v0.4.0 移除）
            App.LogDebug("ShellIntegration.Install: COM not available, falling back to static cascade");
            InstallCascade(s, exePath);
        }

        // 通知 Windows Shell 刷新上下文菜单缓存
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        App.LogDebug("ShellIntegration.Install: done, exePath={0}", exePath);
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
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractSmartVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractToNamedVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressSeparateVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressCombinedVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressVerb}");

            // 菜单分隔线（独立动词模式）
            DeleteRegistryKey($@"Software\Classes\{target}\shell\00_MantisZipSepTop");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\02a_MantisZipSep");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\04a_MantisZipSep");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\99_MantisZipSepBottom");

            // 旧版动词L.T(L.Main_Col_Name)（v0.1.3 及以前）
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipOpen");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipExtract");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipQuick");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipCompress");
        }

        // 清理 COM 组件注册
        UninstallCom();

        // 旧版 per-extension 注册（v0.1.3 早期版本遗留）
        foreach (var ext in ArchiveExtensions)
        {
            DeleteRegistryKey($@"Software\Classes\{ext}\shell\MantisZipExtract");
        }
        // 通知 Windows Shell 刷新上下文菜单缓存
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

        App.LogDebug("ShellIntegration.Uninstall: done");
    }

    #region COM 组件注册/反注册

    // MantisZip.ShellExt 组件 CLSID（必须与 ContextMenuHandler.cs 的 [Guid] 一致）
    private const string ComClsid = "{C90B2A1E-5E4F-4A7A-9B0F-8C1D3E5F7A9B}";
    private const string ComProgId = "MantisZip.ContextMenu";
    private const string ComHandlerKey = "MantisZip";

    /// <summary>
    /// 安装 COM 右键菜单（MantisZip.ShellExt.comhost.dll）。
    /// 写入 HKCU\Software\Classes，无需管理员权限。
    /// 返回 true 表示安装成功，false 表示 COM host DLL 不存在（触发回退）。
    /// </summary>
    private static bool InstallCom()
    {
        try
        {
            var baseDir = Path.GetDirectoryName(GetExePath());
            if (baseDir == null) return false;

            var comhostPath = Path.Combine(baseDir, "MantisZip.ShellExt.comhost.dll");
            if (!File.Exists(comhostPath))
            {
                // Also check subdirectory (x64/x86 for architecture-specific builds)
                comhostPath = Path.Combine(baseDir,
                    Environment.Is64BitProcess ? "x64" : "x86",
                    "MantisZip.ShellExt.comhost.dll");
                if (!File.Exists(comhostPath))
                {
                    App.LogDebug("InstallCom: comhost.dll not found at {0}", comhostPath);
                    return false;
                }
            }

            App.LogDebug("InstallCom: registering from {0}", comhostPath);

            // 1. CLSID 注册（HKCU — per-user）
            //    .NET 6+ comhost.dll 通过 runtimeconfig.json 定位运行时和托管程序集，
            //    不需要 InprocServer32 下额外的 Assembly/Class/CodeBase 值
            var clsidKey = $@"Software\Classes\CLSID\{ComClsid}";
            SetRegistryValue(clsidKey, null, "MantisZip Context Menu Handler");
            SetRegistryValue($@"{clsidKey}\InprocServer32", null, comhostPath);
            SetRegistryValue($@"{clsidKey}\InprocServer32", "ThreadingModel", "Apartment");
            SetRegistryValue($@"{clsidKey}\ProgId", null, ComProgId);
            // ProgId 反向查找（由 COM CreateObject/ProgId 需要）
            SetRegistryValue($@"Software\Classes\{ComProgId}", null, "MantisZip Context Menu Handler");
            SetRegistryValue($@"Software\Classes\{ComProgId}\CLSID", null, ComClsid);

            // 2. 上下文菜单处理程序注册
            foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
            {
                var handlerKey = $@"Software\Classes\{target}\shellex\ContextMenuHandlers\{ComHandlerKey}";
                SetRegistryValue(handlerKey, null, ComClsid);
            }

            App.LogDebug("InstallCom: COM registration successful");
            WriteMenuTextToRegistry();
            return true;
        }
        catch (Exception ex)
        {
            App.LogDebug("InstallCom: failed: {0}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 卸载 COM 右键菜单，并清理旧的静态注册表条目。
    /// </summary>
    private static void UninstallCom()
    {
        try
        {
            // 1. 删除上下文菜单处理程序注册
            foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
            {
                DeleteRegistryKey($@"Software\Classes\{target}\shellex\ContextMenuHandlers\{ComHandlerKey}");
            }

            // 2. 删除 CLSID 注册及 ProgId 反向查找
            DeleteRegistryKey($@"Software\Classes\CLSID\{ComClsid}");
            DeleteRegistryKey($@"Software\Classes\{ComProgId}");

            App.LogDebug("UninstallCom: COM registration cleaned up");
        }
        catch (Exception ex)
        {
            App.LogDebug("UninstallCom: failed: {0}", ex.Message);
        }
    }

    #endregion

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
        // 注：分隔线使用独立的 separator verb（CommandFlags=8），
        // 不直接设置在真实动词上，避免部分 Windows 版本上
        // ECF_SEPARATORBEFORE 导致动词本身不显示。
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

        // 解压相关动词（仅压缩包；每个独立控制）
        if (includeExtract)
        {
            bool hasExtract = s.EnableExtractHereMenu || s.EnableSmartExtractMenu || s.EnableExtractToNamedMenu || s.EnableExtractToMenu;
            if (hasExtract)
            {
                order++;
                InstallSeparator(shellPath, order);
            }

            // 2. 用MantisZip解压到此处
            if (s.EnableExtractHereMenu)
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

            // 2.5. 智能解压到此处 (Smart Extract)
            if (s.EnableSmartExtractMenu)
            {
                order++;
                var verb = $"{order:D2}_smartextract";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractSmartDisplay);
                SetRegistryValue(verbPath, "AppliesTo", BuildAppliesToFilter());
                if (s.ShowMenuIcons)
                    SetRegistryValue(verbPath, "Icon", $"""{exePath},0""");
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract-smart ""{argVar}""");
            }

            // 3. 用MantisZip解压到（压缩包名）
            if (s.EnableExtractToNamedMenu)
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

            // 4. 用MantisZip解压到……
            if (s.EnableExtractToMenu)
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

        // 压缩
        {
            bool hasCompress = s.EnableCompressSeparate || s.EnableCompressCombined || s.EnableCompressMenu;
            if (hasCompress)
            {
                order++;
                InstallSeparator(shellPath, order);
            }

            // 5. 压缩到独立的（文件名）
            if (s.EnableCompressSeparate)
            {
                order++;
                var verb = $"{order:D2}_compressseparate";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, CompressSeparateDisplay);
                if (s.ShowMenuIcons)
                    SetRegistryValue(verbPath, "Icon", $"""{exePath},0""");
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --compress-separate ""{argVar}""");
            }

            // 6. 压缩到（父目录名）
            if (s.EnableCompressCombined)
            {
                order++;
                var verb = $"{order:D2}_compresscombined";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, CompressCombinedDisplay);
                if (s.ShowMenuIcons)
                    SetRegistryValue(verbPath, "Icon", $"""{exePath},0""");
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --compress-combined ""{argVar}""");
            }

            // 7. 用MantisZip压缩
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
    }

    #endregion

    #region L.T(L.Settings_Tab_FileAssoc)

    /// <summary>检查L.T(L.Settings_Tab_FileAssoc)L.T(L.MsgBox_Yes)L.T(L.MsgBox_No)已L.T(L.Settings_Menu_Btn_Install)。</summary>
    public static bool AreAssociationsInstalled => ArchiveExtensions.Any(AreAssociationsInstalledForExtension);

    /// <summary>检查指定的扩展名是否已安装文件关联。</summary>
    public static bool AreAssociationsInstalledForExtension(string ext)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids");
        if (key == null) return false;
        return key.GetValue(ProgId) != null;
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

    /// <summary>为单个扩展名安装文件关联（OpenWithProgids + DefaultIcon）。</summary>
    public static void InstallAssociationForExtension(string ext)
    {
        if (ext == ".tar.gz") return; // .tar.gz 文件的扩展名是 .gz，由 .gz 注册覆盖

        // 写 OpenWithProgids
        var extKey = $@"Software\Classes\{ext}\OpenWithProgids";
        using var key = Registry.CurrentUser.CreateSubKey(extKey);
        key?.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

        // 写 DefaultIcon
        var iconPath = GetIconPath(ext);
        if (iconPath != null)
            SetRegistryValue($@"Software\Classes\{ext}\DefaultIcon", null, iconPath);
    }

    /// <summary>
    /// 仅注册 ProgId 和 Applications 条目（不安装任何扩展名关联）。
    /// 在 InstallAssoc_Click 中先调用此方法确保基础注册存在，
    /// 然后只安装用户勾选的扩展名。
    /// </summary>
    public static void EnsureProgIdRegistered()
    {
        App.LogDebug("ShellIntegration.EnsureProgIdRegistered: starting");
        var exePath = GetExePath();

        // 1. 注册 ProgId
        var progIdKey = $@"Software\Classes\{ProgId}";
        SetRegistryValue(progIdKey, null, L.T(L.Shell_ProgIdDesc));
        SetRegistryValue($@"{progIdKey}\shell\open", null, L.T(L.Shell_OpenVerb));
        SetRegistryValue($@"{progIdKey}\shell\open\command", null, $@"""{exePath}"" --open ""%1""");
        SetRegistryValue($@"{progIdKey}\DefaultIcon", null, $@"""{exePath},0""");

        // 2. 注册 Applications 条目
        var appKey = $@"Software\Classes\Applications\{Path.GetFileName(exePath)}";
        SetRegistryValue(appKey, "FriendlyAppName", L.T(L.App_MantisZipTitle));
        SetRegistryValue($@"{appKey}\shell\open\command", null, $@"""{exePath}"" --open ""%1""");
        foreach (var ext in ArchiveExtensions)
        {
            if (ext == ".tar.gz") continue;
            SetRegistryValue($@"{appKey}\SupportedTypes", ext, "");
        }

        App.LogDebug("ShellIntegration.EnsureProgIdRegistered: done");
    }

    /// <summary>
    /// 注册 ProgId 并将L.T(L.Shell_Compress)格式扩展名与之关联。
    /// 写入 HKCU\Software\Classes，无需管理员权限。
    /// </summary>
    public static void InstallAssociations()
    {
        App.LogDebug("ShellIntegration.InstallAssociations: starting");
        EnsureProgIdRegistered();

        foreach (var ext in ArchiveExtensions)
        {
            InstallAssociationForExtension(ext);
        }

        App.LogDebug("ShellIntegration.InstallAssociations: done");
    }

    /// <summary>删除单个扩展名的文件关联（OpenWithProgids + DefaultIcon）。</summary>
    public static void UninstallAssociationForExtension(string ext)
    {
        if (ext == ".tar.gz") return;

        // 删除 OpenWithProgids 条目
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", writable: true);
            key?.DeleteValue(ProgId, throwOnMissingValue: false);
        }
        catch { /* 该扩展可能没有 OpenWithProgids 键 */ }

        // 删除 DefaultIcon（仅当指向我们的图标路径时）
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

    /// <summary>
    /// L.T(L.Settings_Assoc_Uninstall)：删除 ProgId 和扩展名的 OpenWithProgids 条目。
    /// </summary>
    public static void UninstallAssociations()
    {
        App.LogDebug("ShellIntegration.UninstallAssociations: starting");

        // 删除每个扩展名的 ProgId 条目 和 DefaultIcon
        foreach (var ext in ArchiveExtensions)
        {
            UninstallAssociationForExtension(ext);
        }

        // 删除 Applications 条目
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "");
        if (!string.IsNullOrEmpty(exeName))
            DeleteRegistryKey($@"Software\Classes\Applications\{exeName}");

        // 删除 ProgId
        DeleteRegistryKey($@"Software\Classes\{ProgId}");

        App.LogDebug("ShellIntegration.UninstallAssociations: done");
    }

    /// <summary>返回已安装文件关联的扩展名数量。</summary>
    public static int GetInstalledExtensionCount()
    {
        return ArchiveExtensions.Count(AreAssociationsInstalledForExtension);
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

    private static void SetRegistryDword(string subKey, string valueName, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey);
        key?.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    /// <summary>在指定位置添加一条菜单分隔线。</summary>
    private static void InstallSeparator(string shellPath, int order)
    {
        var verb = $"{order:D2}_sep";
        SetRegistryDword($@"{shellPath}\{verb}", "CommandFlags", 8);
    }

    private static void DeleteRegistryKey(string subKey)
    {
        Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Write localized ShellExt menu text to registry under HKCU\Software\MantisZip\ContextMenu.
    /// Called during COM install so ShellExt reads the current language's strings.
    /// </summary>
    private static void WriteMenuTextToRegistry()
    {
        var regPath = @"Software\MantisZip\ContextMenu";
        SetRegistryValue(regPath, "TextOpen", L.T(L.ShellExt_Open));
        SetRegistryValue(regPath, "TextExtractHereSingle", L.T(L.ShellExt_ExtractHereSingle));
        SetRegistryValue(regPath, "TextExtractHereMulti", L.T(L.ShellExt_ExtractHereMulti));
        SetRegistryValue(regPath, "TextSmartExtractSingle", L.T(L.ShellExt_SmartExtractSingle));
        SetRegistryValue(regPath, "TextSmartExtractMulti", L.T(L.ShellExt_SmartExtractMulti));
        SetRegistryValue(regPath, "TextExtractToNamed", L.T(L.ShellExt_ExtractToNamed));
        SetRegistryValue(regPath, "TextExtractTo", L.T(L.ShellExt_ExtractTo));
        SetRegistryValue(regPath, "TextCompress", L.T(L.ShellExt_Compress));
        App.LogDebug("WriteMenuTextToRegistry: wrote ShellExt menu text to registry");
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

public enum AssocStatus
{
    NotAssociated,
    NotDefault,
    IsDefault
}
