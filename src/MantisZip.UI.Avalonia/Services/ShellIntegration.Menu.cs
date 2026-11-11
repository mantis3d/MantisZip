using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
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
            using var comKey = RegistryHost.Open(
                $@"Software\Classes\CLSID\{ComClsid}");
            if (comKey != null) return true;

            // 回退：检查静态注册表
            using var key = RegistryHost.Open(
                $@"Software\Classes\*\shell\{CascadeRoot}");
            if (key != null) return true;
            using var key2 = RegistryHost.Open(
                $@"Software\Classes\*\shell\{CompressVerb}\command");
            if (key2 != null) return true;
            using var key3 = RegistryHost.Open(
                $@"Software\Classes\*\shell\{ExtractSmartVerb}\command");
            if (key3 != null) return true;
            using var key4 = RegistryHost.Open(
                $@"Software\Classes\*\shell\{CompressSeparateVerb}\command");
            return key4 != null;
        }
    }

    /// <summary>
    /// 安装 Shell 右键菜单。
    /// 默认走静态级联方案（ExtendedSubCommandsKey，不依赖 Explorer 加载 .NET COM 组件）；
    /// 仅当用户显式打开 <see cref="AppSettings.EnableDynamicMenu"/>（实验）时才注册 COM 动态菜单。
    /// </summary>
    public static void Install()
    {
        App.DebugLog("ShellIntegration.Install: starting");
        RunExclusive("Install", () =>
        {
            var s = AppSettings.Load();
            var exePath = GetExePath();

            // 先清理旧注册，避免残留。
            // 注意：这里**不能**调 Uninstall()——它会重启 Explorer（issue #80）。
            CleanupRegistrations();

            if (s.EnableDynamicMenu)
            {
                if (!InstallCom())
                {
                    // COM 注册失败（DLL 缺失 / 位宽不符 / runtimeconfig 不匹配）→ 直接回退静态级联，
                    // 状态写 fallback（设置页会显示「动态菜单加载失败，已回退到静态菜单」）。
                    App.DebugLog("ShellIntegration.Install: COM not available, falling back to cascade");
                    SetDynamicMenuStatus(DynamicMenuStatus_Fallback);
                    InstallCascade(s, exePath);
                }
                else
                {
                    // COM 注册成功 → 进入「试用期」：级联兜底**同时**在位，
                    // 等 COM handler 自己上报心跳（真的画出菜单了）才由 CheckComStatus 转 active 并撤掉级联。
                    // 试用期内 COM 与级联会同时出现一次，这是有意的——宁可重一次，也不让用户两头空（issue #80）。
                    App.DebugLog("ShellIntegration.Install: COM registered, status=pending (cascade stays as safety net until heartbeat proves COM works)");
                    SetDynamicMenuStatus(DynamicMenuStatus_Pending);
                    SetRegistryValue(ContextMenuRegPath, ComProbeStartUtcValue,
                        DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    InstallCascade(s, exePath);
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
            NotifyAssocChanged();

            // ── Install summary ──
            var dynStatus = GetDynamicMenuStatus();
            App.DebugLog($"ShellIntegration.Install: done — dynStatus={dynStatus}, installed={IsInstalled}, exePath={exePath}");
        });
    }

    /// <summary>
    /// 卸载所有 MantisZip 右键菜单注册，并重启 Explorer 释放 comhost.dll 句柄。
    /// ⚠ 只给 <c>--uninstall-shell</c>（Inno Setup 的 <c>[UninstallRun]</c>）用：
    /// 卸载程序需要删除安装目录里的文件，Explorer 持有句柄会导致删除失败。
    /// 设置页的「安装 / 应用」必须走 <see cref="CleanupRegistrations"/>，不能重启资源管理器。
    /// </summary>
    public static void Uninstall()
    {
        App.DebugLog("ShellIntegration.Uninstall: starting");
        RunExclusive("Uninstall", () =>
        {
            CleanupRegistrations();
            RestartExplorerForUnload();
            NotifyAssocChanged();
            App.DebugLog("ShellIntegration.Uninstall: done");
        });
    }

    /// <summary>
    /// 纯注册表清理：删掉级联 + COM + 状态键，**不重启 Explorer、不发 Shell 通知**。
    /// <see cref="Install"/> 与 <see cref="Uninstall"/> 共用。
    /// </summary>
    private static void CleanupRegistrations()
    {
        UninstallStaticMenus();
        UninstallCom();
        DeleteRegistryKey(ContextMenuRegPath);
    }

    /// <summary>
    /// 从设置页卸载右键菜单：只清注册表并通知 Shell，**不重启资源管理器**。
    /// （重启只属于安装程序卸载流程，见 <see cref="Uninstall"/>。）
    /// </summary>
    public static void RemoveMenus()
    {
        App.DebugLog("ShellIntegration.RemoveMenus: starting");
        RunExclusive("RemoveMenus", () =>
        {
            CleanupRegistrations();
            NotifyAssocChanged();
            App.DebugLog("ShellIntegration.RemoveMenus: done");
        });
    }

    /// <summary>
    /// 用命名互斥把注册表写操作串行化。
    /// 右键菜单会在多选时一次拉起多个 MantisZip 进程，它们同时跑 <see cref="CheckComStatus"/>，
    /// 没有互斥时会出现「A 装级联、B 删级联」的交叉覆盖（issue #80 日志里 5 实例并发 1184ms）。
    /// 异常**向上抛**（失败必须让用户看见，见规则 20）——两个调用方都已有 try/catch。
    /// </summary>
    private static void RunExclusive(string operation, Action action, int timeoutMs = 15000)
    {
        try
        {
            using var mutex = new Mutex(false, ShellRegMutexName);
            bool acquired;
            try { acquired = mutex.WaitOne(timeoutMs); }
            catch (AbandonedMutexException) { acquired = true; } // 上一持有者崩溃 → 视为拿到

            if (!acquired)
            {
                App.DebugLog($"ShellIntegration.{operation}: mutex timeout after {timeoutMs}ms, skipped");
                return;
            }

            try { action(); }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            App.DebugLog($"ShellIntegration.{operation}: failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 重启 Explorer 进程以卸载已加载的 COM DLL（comhost.dll）。
    /// 在静默卸载（Inno Setup [UninstallRun]）场景下，Explorer 可能仍持有
    /// comhost.dll 的文件句柄，导致卸载程序无法删除安装目录下的文件。
    /// </summary>
    private static void RestartExplorerForUnload()
    {
        try
        {
            var explorerProcesses = Process.GetProcessesByName("explorer");
            if (explorerProcesses.Length == 0)
            {
                App.DebugLog("RestartExplorer: no Explorer process found, skipping");
                return;
            }

            App.DebugLog($"RestartExplorer: terminating {explorerProcesses.Length} Explorer process(es)");

            foreach (var proc in explorerProcesses)
            {
                try { proc.Kill(); }
                catch { /* process may have already exited */ }
            }

            // Wait for processes to fully exit (DLL handles released)
            foreach (var proc in explorerProcesses)
            {
                try { proc.WaitForExit(3000); }
                catch { /* already exited or inaccessible */ }
            }

            // Brief delay for OS to release file handles
            Thread.Sleep(500);

            // Restart Explorer (Windows auto-recreates the desktop shell)
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            App.DebugLog("RestartExplorer: Explorer restarted successfully");
        }
        catch (Exception ex)
        {
            App.DebugLog($"RestartExplorer: failed ({ex.Message}), proceeding anyway");
        }
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
                    using var key = RegistryHost.Open(
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
            using var key = RegistryHost.Create(ContextMenuRegPath);
            key?.SetValue("DynamicMenuStatus", status, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            App.DebugLog($"SetDynamicMenuStatus: failed to write status '{status}': {ex.Message}");
        }
    }

    /// <summary>
    /// 应用启动时（含每次右键拉起的新进程）校正动态菜单状态。
    ///
    /// 判据只有一条：<b>handler 自己上报的心跳</b>（<see cref="LastRenderUtcValue"/>）。
    /// 不再遍历 Explorer 模块列表 —— <c>comhost.dll</c> 是 .NET 的 native shim，Explorer 先
    /// <c>LoadLibrary</c> 再问 <c>DllGetClassObject</c>，**激活失败时 DLL 仍留在模块列表里**，
    /// 所以「模块存在」不能当「COM 能用」的判据（issue #80 根因 1）。
    ///
    /// 状态迁移：
    /// <list type="bullet">
    /// <item><c>pending</c> + 心跳新鲜 → <c>active</c>（撤掉级联，只剩 COM）</item>
    /// <item><c>pending</c> + 试用期已过 → <c>fallback</c>（永久回退级联）</item>
    /// <item><c>pending</c> + 其余 → 保持 <c>pending</c>，只补级联（用户始终有可用菜单）</item>
    /// <item><c>active</c> + 心跳过期/从未 → <c>fallback</c> + 装回级联（issue #80 根因 2：原先 <c>active</c> 后永久早退）</item>
    /// <item><c>fallback</c> → 确保级联在位</item>
    /// <item><c>disabled</c> / <c>not_installed</c> → 不动</item>
    /// </list>
    /// </summary>
    public static void CheckComStatus()
    {
        var status = GetDynamicMenuStatus();
        if (status != DynamicMenuStatus_Pending
            && status != DynamicMenuStatus_Active
            && status != DynamicMenuStatus_Fallback)
            return;

        RunExclusive("CheckComStatus", () =>
        {
            var health = ReadHeartbeatHealth();
            switch (DecideComAction(status, health, IsProbeExpired()))
            {
                case ComStatusAction.PromoteToActive:
                    App.DebugLog($"CheckComStatus: heartbeat={health}, promoting pending → active, removing cascade");
                    SetDynamicMenuStatus(DynamicMenuStatus_Active);
                    UninstallStaticMenus();
                    NotifyAssocChanged();
                    break;

                case ComStatusAction.DemoteToFallback:
                    App.DebugLog($"CheckComStatus: status={status}, heartbeat={health} → fallback, restoring cascade");
                    SetDynamicMenuStatus(DynamicMenuStatus_Fallback);
                    InstallCascade(AppSettings.Load(), GetExePath());
                    NotifyAssocChanged();
                    break;

                case ComStatusAction.EnsureCascade:
                    // 只在级联缺失时才写（幂等）：稳态下每次右键启动不再重写 ~60 个注册表键
                    if (!IsCascadeInstalled())
                    {
                        App.DebugLog($"CheckComStatus: status={status}, heartbeat={health}, cascade missing → installing fallback menu");
                        InstallCascade(AppSettings.Load(), GetExePath());
                        NotifyAssocChanged();
                    }
                    break;

                default:
                    App.DebugLog($"CheckComStatus: status={status}, heartbeat={health}, nothing to do");
                    break;
            }
        });
    }

    /// <summary>心跳健康度（<see cref="ReadHeartbeatHealth"/> 的返回值）。</summary>
    internal enum ComHeartbeatHealth
    {
        /// <summary>≤ <see cref="FreshHeartbeatWindow"/> 且 PID 指向当前存活的 Explorer —— COM 刚刚确实画出了菜单。</summary>
        Fresh,
        /// <summary>≤ <see cref="RecentHeartbeatWindow"/> —— 曾经能用，暂不降级。</summary>
        Recent,
        /// <summary>早于 <see cref="RecentHeartbeatWindow"/> —— 可能已失效。</summary>
        Stale,
        /// <summary>从未写过 —— 升级前的老安装，或 COM 一次都没成功。</summary>
        Never,
    }

    /// <summary>状态机的下一步动作。</summary>
    internal enum ComStatusAction { None, PromoteToActive, DemoteToFallback, EnsureCascade }

    /// <summary>「心跳新鲜」窗口：够覆盖「右键 → 点击菜单项 → 新进程启动」这一串，又不至于把隔夜的心跳当刚发生。</summary>
    internal static readonly TimeSpan FreshHeartbeatWindow = TimeSpan.FromMinutes(30);

    /// <summary>「心跳仍可信」窗口：这段时间内画出来过就暂不降级（避免长期不右键的用户被误判）。</summary>
    internal static readonly TimeSpan RecentHeartbeatWindow = TimeSpan.FromDays(7);

    /// <summary>试用期：<c>pending</c> 起 2 天内一次心跳都没收到 → 认定 COM 不可用，永久回退级联。</summary>
    internal static readonly TimeSpan ComProbeWindow = TimeSpan.FromDays(2);

    /// <summary>
    /// 纯决策（不碰注册表，可单测）：当前状态 + 心跳健康度 + 试用期是否过期 → 下一步动作。
    /// </summary>
    internal static ComStatusAction DecideComAction(string status, ComHeartbeatHealth health, bool probeExpired)
        => status switch
        {
            DynamicMenuStatus_Pending when health == ComHeartbeatHealth.Fresh => ComStatusAction.PromoteToActive,
            DynamicMenuStatus_Pending when probeExpired => ComStatusAction.DemoteToFallback,
            DynamicMenuStatus_Pending => ComStatusAction.EnsureCascade,
            DynamicMenuStatus_Active when health is ComHeartbeatHealth.Stale or ComHeartbeatHealth.Never
                => ComStatusAction.DemoteToFallback,
            DynamicMenuStatus_Active => ComStatusAction.None,
            DynamicMenuStatus_Fallback => ComStatusAction.EnsureCascade,
            _ => ComStatusAction.None,
        };

    /// <summary>读取 handler 上报的心跳并折算成健康度。任何异常都按 <see cref="ComHeartbeatHealth.Never"/> 处理（宁可回退级联）。</summary>
    internal static ComHeartbeatHealth ReadHeartbeatHealth()
    {
        try
        {
            using var key = RegistryHost.Open(ContextMenuRegPath);
            if (key?.GetValue(LastRenderUtcValue) is not string raw
                || !DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var renderedUtc))
                return ComHeartbeatHealth.Never;

            var age = DateTime.UtcNow - renderedUtc.ToUniversalTime();
            if (age < TimeSpan.Zero) age = TimeSpan.Zero; // 时钟回拨 → 当作刚刚发生

            int pid = key.GetValue(LastRenderPidValue) is int p ? p : 0;
            if (age <= FreshHeartbeatWindow && pid > 0 && IsProcessAlive(pid, "explorer"))
                return ComHeartbeatHealth.Fresh;

            return age <= RecentHeartbeatWindow ? ComHeartbeatHealth.Recent : ComHeartbeatHealth.Stale;
        }
        catch (Exception ex)
        {
            App.DebugLog($"ReadHeartbeatHealth: failed: {ex.Message}");
            return ComHeartbeatHealth.Never;
        }
    }

    /// <summary>试用期是否已过。老安装没有 <see cref="ComProbeStartUtcValue"/> → 视为已过期（宁可回退级联）。</summary>
    private static bool IsProbeExpired()
    {
        try
        {
            using var key = RegistryHost.Open(ContextMenuRegPath);
            if (key?.GetValue(ComProbeStartUtcValue) is not string raw
                || !DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startUtc))
                return true;

            return DateTime.UtcNow - startUtc.ToUniversalTime() > ComProbeWindow;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>级联入口与子命令根是否都在位（两处缺一即视为未安装）。</summary>
    private static bool IsCascadeInstalled()
    {
        try
        {
            using var entry = RegistryHost.Open($@"Software\Classes\*\shell\{CascadeRoot}");
            if (entry == null) return false;
            using var sub = RegistryHost.Open($@"Software\Classes\{CascadeRoot}\File\shell");
            return sub != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>指定 PID 是否仍是同名进程（用于确认心跳来自当前这一代 Explorer）。</summary>
    private static bool IsProcessAlive(int pid, string expectedName)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited
                && string.Equals(proc.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

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
