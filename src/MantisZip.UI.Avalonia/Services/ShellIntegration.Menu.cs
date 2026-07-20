using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

internal static partial class ShellIntegration
{
    /// <summary>检查是否已安装 Shell 扩展（COM 或静态注册表）。</summary>
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
    /// 安装 Shell 右键菜单。
    /// 根据 AppSettings.EnableDynamicMenu 决定使用 COM 动态菜单
    /// 还是静态级联方案（ExtendedSubCommandsKey）。
    /// </summary>
    public static void Install()
    {
        App.DebugLog("ShellIntegration.Install: starting");
        var s = AppSettings.Load();
        var exePath = GetExePath();

        // 先清理旧注册，避免残留
        Uninstall();

        if (s.EnableDynamicMenu)
        {
            if (!InstallCom())
            {
                App.DebugLog("ShellIntegration.Install: COM not available, installing cascade only");
                SetDynamicMenuStatus(DynamicMenuStatus_Disabled);
                InstallCascade(s, exePath);
            }
            else
            {
                // 仅注册 COM，不安装级联菜单（避免两个菜单同时出现）。
                // 级联菜单会在 CheckComStatus() 发现 COM 尚未被 Explorer 加载时自动安装。
                App.DebugLog("ShellIntegration.Install: COM registered, status=pending (cascade will be installed if COM not loaded in Explorer)");
                SetDynamicMenuStatus(DynamicMenuStatus_Pending);
            }
        }
        else
        {
            // 安装静态级联方案（可靠兼容所有 Windows 版本）
            App.DebugLog("ShellIntegration.Install: using static cascade registration");
            SetDynamicMenuStatus(DynamicMenuStatus_Disabled);
            InstallCascade(s, exePath);
        }

        // 通知 Windows Shell 刷新上下文菜单缓存
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

        // ── Install summary ──
        var dynStatus = GetDynamicMenuStatus();
        App.DebugLog($"ShellIntegration.Install: done — dynStatus={dynStatus}, installed={IsInstalled}, exePath={exePath}");
    }

    /// <summary>
    /// 卸载所有 MantisZip 右键菜单注册。
    /// </summary>
    public static void Uninstall()
    {
        App.DebugLog("ShellIntegration.Uninstall: starting");
        UninstallStaticMenus();
        UninstallCom();
        DeleteRegistryKey(ContextMenuRegPath);
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        App.DebugLog("ShellIntegration.Uninstall: done");
    }

    /// <summary>
    /// 卸载所有静态菜单注册（层叠入口 + 独立动词），保留 COM 注册。
    /// 当 COM 动态菜单激活时调用，避免两个菜单同时出现。
    /// </summary>
    private static void UninstallStaticMenus()
    {
        // 层叠入口
        foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
        {
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CascadeRoot}");
        }
        // 层叠子命令定义
        DeleteRegistryKey($@"Software\Classes\{CascadeRoot}");

        // 独立动词（含新旧动词名称全量，升级时清理旧版注册）
        foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
        {
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{OpenVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractHereVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractSmartVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractToNamedVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressSeparateVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressCombinedVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\00_MantisZipSepTop");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\02a_MantisZipSep");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\04a_MantisZipSep");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\99_MantisZipSepBottom");
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
    }

    #region COM 组件注册/反注册

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
                    App.DebugLog($"InstallCom: comhost.dll not found at {comhostPath}");
                    return false;
                }
            }

            App.DebugLog($"InstallCom: registering from {comhostPath}");

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

            App.DebugLog("InstallCom: COM registration successful");

            // ── Diagnostics: log all registry keys and verify file paths ──
            App.DebugLog($"InstallCom: DIAG — CLSID=\"{ComClsid}\"");
            App.DebugLog($"InstallCom: DIAG — InprocServer32=\"{comhostPath}\"");
            App.DebugLog($"InstallCom: DIAG — InprocServer32 exists={File.Exists(comhostPath)}");

            // Verify runtimeconfig.json next to comhost.dll (required for .NET COM hosting)
            var comhostDir = Path.GetDirectoryName(comhostPath);
            var runtimeConfig = comhostDir != null
                ? Path.Combine(comhostDir, "MantisZip.ShellExt.runtimeconfig.json")
                : null;
            if (runtimeConfig != null)
                App.DebugLog($"InstallCom: DIAG — runtimeconfig.json exists={File.Exists(runtimeConfig)}");

            // Log all shellex handler keys
            foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
            {
                var handlerKey = $@"Software\Classes\{target}\shellex\ContextMenuHandlers\{ComHandlerKey}";
                string? actualValue = null;
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(
                        $@"Software\Classes\{target}\shellex\ContextMenuHandlers\{ComHandlerKey}");
                    actualValue = key?.GetValue(null) as string;
                }
                catch { }
                App.DebugLog($"InstallCom: DIAG — HKCU\\{handlerKey} = \"{actualValue ?? "(null)"}\"");
            }

            // Log program directory listing for dependency verification
            var baseDirForLog = Path.GetDirectoryName(GetExePath());
            if (baseDirForLog != null)
            {
                try
                {
                    var allDlls = Directory.GetFiles(baseDirForLog, "*.dll");
                    App.DebugLog($"InstallCom: DIAG — App directory has {allDlls.Length} DLL(s)");
                    // Log key DLLs
                    var keyDlls = new[] {
                        "MantisZip.ShellExt.dll", "MantisZip.ShellExt.comhost.dll",
                        "MantisZip.Core.dll", "SharpCompress.dll", "SharpSevenZip.dll"
                    };
                    foreach (var dll in keyDlls)
                    {
                        var dllPath = Path.Combine(baseDirForLog, dll);
                        App.DebugLog($"InstallCom: DIAG — {dll} exists={File.Exists(dllPath)}" +
                            (File.Exists(dllPath) ? $" size={new FileInfo(dllPath).Length}" : ""));
                    }
                    // Check 7z.dll in x64 subdirectory
                    var sevenZ64 = Path.Combine(baseDirForLog, "x64", "7z.dll");
                    var sevenZ86 = Path.Combine(baseDirForLog, "x86", "7z.dll");
                    App.DebugLog($"InstallCom: DIAG — x64\\7z.dll exists={File.Exists(sevenZ64)}");
                    App.DebugLog($"InstallCom: DIAG — x86\\7z.dll exists={File.Exists(sevenZ86)}");
                }
                catch (Exception ex)
                {
                    App.DebugLog($"InstallCom: DIAG — directory listing error: {ex.Message}");
                }
            }

            WriteMenuTextToRegistry();
            return true;
        }
        catch (Exception ex)
        {
            App.DebugLog($"InstallCom: failed: {ex.Message}");
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

            App.DebugLog("UninstallCom: COM registration cleaned up");
        }
        catch (Exception ex)
        {
            App.DebugLog($"UninstallCom: failed: {ex.Message}");
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
        SetRegistryValue(entryPath, "MUIVerb", LocalizationManager.T("App_MantisZipTitle"));

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

        // 1. 用MantisZip打开（所有文件均可，由应用层处理非压缩包）
        if (s.EnableOpenMenu)
        {
            order++;
            var verb = $"{order:D2}_open";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, OpenDisplay);
            if (s.ShowMenuIcons)
            {
                string? icon = GetMenuIconPath(verb);
                if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
            }
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --open ""{argVar}""");
        }

        // 解压相关动词（所有文件均可，跟压缩一样）
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
                if (s.ShowMenuIcons)
                {
                    string? icon = GetMenuIconPath(verb);
                    if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
                }
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract-here ""{argVar}""");
            }

            // 2.5. 智能解压到此处 (Smart Extract)
            if (s.EnableSmartExtractMenu)
            {
                order++;
                var verb = $"{order:D2}_smartextract";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractSmartDisplay);
                if (s.ShowMenuIcons)
                {
                    string? icon = GetMenuIconPath(verb);
                    if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
                }
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract-smart ""{argVar}""");
            }

            // 3. 用MantisZip解压到（压缩包名）
            if (s.EnableExtractToNamedMenu)
            {
                order++;
                var verb = $"{order:D2}_extracttonamed";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractToNamedDisplay);
                if (s.ShowMenuIcons)
                {
                    string? icon = GetMenuIconPath(verb);
                    if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
                }
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract-to-name ""{argVar}""");
            }

            // 4. 用MantisZip解压到……
            if (s.EnableExtractToMenu)
            {
                order++;
                var verb = $"{order:D2}_extract";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractDisplay);
                if (s.ShowMenuIcons)
                {
                    string? icon = GetMenuIconPath(verb);
                    if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
                }
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract ""{argVar}""");
            }

            // 分隔线：解压与压缩之间
            bool hasCompress = s.EnableCompressSeparate || s.EnableCompressCombined || s.EnableCompressMenu;
            if (hasExtract && hasCompress)
            {
                order++;
                InstallSeparator(shellPath, order);
            }
        }

        // 压缩相关动词
        // 5. 压缩到独立的（文件名）
        if (s.EnableCompressSeparate)
        {
            order++;
            var verb = $"{order:D2}_compressseparate";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, CompressSeparateDisplay);
            if (s.ShowMenuIcons)
            {
                string? icon = GetMenuIconPath(verb);
                if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
            }
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
            {
                string? icon = GetMenuIconPath(verb);
                if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
            }
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
            {
                string? icon = GetMenuIconPath(verb);
                if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
            }
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --compress ""{argVar}""");
        }
    }

    #endregion

    /// <summary>在指定位置添加一条菜单分隔线。</summary>
    private static void InstallSeparator(string shellPath, int order)
    {
        var verb = $"{order:D2}_sep";
        SetRegistryDword($@"{shellPath}\{verb}", "CommandFlags", 8);
    }

    /// <summary>
    /// Write localized ShellExt menu text to registry under HKCU\Software\MantisZip\ContextMenu.
    /// Called during COM install so ShellExt reads the current language's strings.
    /// </summary>
    private static void SetDynamicMenuStatus(string status)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ContextMenuRegPath);
            key?.SetValue("DynamicMenuStatus", status, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            App.DebugLog($"SetDynamicMenuStatus: failed to write status '{status}': {ex.Message}");
        }
    }

    /// <summary>
    /// 在应用启动时检查 Explorer 是否已经成功加载了 COM 组件。
    /// 遍历所有 Explorer 进程的模块列表，查找 MantisZip.ShellExt.comhost.dll。
    /// </summary>
    public static void CheckComStatus()
    {
        var status = GetDynamicMenuStatus();
        if (status != DynamicMenuStatus_Pending) return;

        App.DebugLog("CheckComStatus: status=pending, scanning Explorer processes...");
        bool found = false;
        int explorerCount = 0;
        try
        {
            foreach (var proc in Process.GetProcessesByName("explorer"))
            {
                explorerCount++;
                try
                {
                    foreach (ProcessModule module in proc.Modules)
                    {
                        if (module.FileName.IndexOf("MantisZip.ShellExt.comhost.dll",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            App.DebugLog($"CheckComStatus: found comhost.dll loaded in Explorer PID={proc.Id}, promoting to active");
                            found = true;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Explorer 进程可能已退出，或某些模块无法访问
                    App.DebugLog($"CheckComStatus: failed to enumerate modules for PID={proc.Id}: {ex.Message}");
                }
                if (found) break;
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"CheckComStatus: failed to enumerate Explorer processes: {ex.Message}");
        }

        if (found)
        {
            SetDynamicMenuStatus(DynamicMenuStatus_Active);
            App.DebugLog("CheckComStatus: status changed to active, removing static cascade menus");
            UninstallStaticMenus();
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        else
        {
            // COM 尚未被 Explorer 加载 → 安装级联菜单作为兜底，确保右键菜单可用
            App.DebugLog($"CheckComStatus: comhost.dll not found in {explorerCount} Explorer process(es), installing cascade as fallback");
            var s = AppSettings.Load();
            InstallCascade(s, GetExePath());
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        [In] ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        [In] ref Guid riid,
        out IntPtr ppv);

    private static void WriteMenuTextToRegistry()
    {
        var regPath = ContextMenuRegPath;
        SetRegistryValue(regPath, "TextOpen", LocalizationManager.T("ShellExt_Open"));
        SetRegistryValue(regPath, "TextExtractHereSingle", LocalizationManager.T("ShellExt_ExtractHereSingle"));
        SetRegistryValue(regPath, "TextExtractHereMulti", LocalizationManager.T("ShellExt_ExtractHereMulti"));
        SetRegistryValue(regPath, "TextSmartExtractSingle", LocalizationManager.T("ShellExt_SmartExtractSingle"));
        SetRegistryValue(regPath, "TextSmartExtractMulti", LocalizationManager.T("ShellExt_SmartExtractMulti"));
        SetRegistryValue(regPath, "TextExtractToNamed", LocalizationManager.T("ShellExt_ExtractToNamed"));
        SetRegistryValue(regPath, "TextExtractTo", LocalizationManager.T("ShellExt_ExtractTo"));
        SetRegistryValue(regPath, "TextCompress", LocalizationManager.T("ShellExt_Compress"));
        App.DebugLog("WriteMenuTextToRegistry: wrote ShellExt menu text to registry");
    }

    /// <summary>
    /// 返回菜单动词对应的图标文件绝对路径（与 COM 动态菜单一致）。
    /// 动词名形如 "01_open"、"02_extracthere"，自动忽略前缀匹配。
    /// 图标文件位于输出目录的 Resources\MenuIcons\ 下。
    /// 如果图标文件不存在，返回 null。
    /// </summary>
    private static string? GetMenuIconPath(string verb)
    {
        // Strip order prefix ("01_open" → "open")
        int underscoreIdx = verb.IndexOf('_');
        string key = underscoreIdx >= 0 ? verb.Substring(underscoreIdx + 1) : verb;

        var iconName = key.ToLowerInvariant() switch
        {
            "open" => "Open.ico",
            "extracthere" => "ExtractHere.ico",
            "smartextract" => "ExtractSmart.ico",
            "extracttonamed" => "ExtractToNamed.ico",
            "extract" => "ExtractTo.ico",
            "compressseparate" => "CompressSeparate.ico",
            "compresscombined" => "CompressCombined.ico",
            "compress" => "CompressDialog.ico",
            _ => null,
        };
        if (iconName == null) return null;

        var baseDir = Path.GetDirectoryName(GetExePath());
        if (baseDir == null) return null;

        var iconPath = Path.Combine(baseDir, "Resources", "MenuIcons", iconName);
        return File.Exists(iconPath) ? iconPath : null;
    }
}
