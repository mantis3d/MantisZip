using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace MantisZip.UI.Avalonia;

public partial class App : Application
{
    private const string LightThemeUri = "avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml";
    private const string DarkThemeUri = "avares://MantisZip.UI.Avalonia/Themes/ThemeDark.axaml";
    private const string IconResourcesUri = "avares://MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml";

    // ── IPC Mutex/Pipe names ──
    private const string CompressMutexName = "MantisZipCompressMutex";
    private const string CompressPipeName = "MantisZipCompressPipe";
    private const string CompressSeparateMutexName = "MantisZipCompressSeparateMutex";
    private const string CompressSeparatePipeName = "MantisZipCompressSeparatePipe";
    private const string CompressCombinedMutexName = "MantisZipCompressCombinedMutex";
    private const string CompressCombinedPipeName = "MantisZipCompressCombinedPipe";
    // 解压族：多选时 Explorer 可能逐项启动进程（静态层叠兜底菜单只带 %1），
    // 每个动词各自一组互斥量/管道名，互不干扰
    private const string ExtractMutexName = "MantisZipExtractMutex";
    private const string ExtractPipeName = "MantisZipExtractPipe";
    private const string ExtractHereMutexName = "MantisZipExtractHereMutex";
    private const string ExtractHerePipeName = "MantisZipExtractHerePipe";
    private const string ExtractToNameMutexName = "MantisZipExtractToNameMutex";
    private const string ExtractToNamePipeName = "MantisZipExtractToNamePipe";
    private const string ExtractSmartMutexName = "MantisZipExtractSmartMutex";
    private const string ExtractSmartPipeName = "MantisZipExtractSmartPipe";

    // ── 主题字典缓存（startup-theme-dedup-trace：消除启动期 ApplyTheme 双重加载）──

    // 当前已合并的主题字典 URI。初值 = LightThemeUri：App.axaml:10 静态合并的默认主题。
    // ⚠ 契约：若修改 App.axaml MergedDictionaries 的默认主题，必须同步此初值；
    //   若修改其顺序（[主题, 图标]），必须同步 ApplyTheme 内的图标取法（MergedDictionaries[1]）。
    private Uri? _loadedThemeUri = new Uri(LightThemeUri);

    // 图标字典缓存（与主题无关，跨主题切换复用，避免 85KB AppIcons.axaml 反复解析）
    private IResourceProvider? _cachedIconProvider;

    public override void Initialize()
    {
        // 启动打点：App.axaml 加载（主题字典 + 85KB 图标字典 + 全局样式）耗时归因
        Services.StartupTimer.Mark("App.Initialize.Enter");
        AvaloniaXamlLoader.Load(this);
        Services.StartupTimer.Mark("App.Initialize.Exit");
        // When precompiled XAML is available, AvaloniaXamlLoader uses it.
        // If not available (e.g. after clean build), InitializeComponent() from
        // the source generator handles runtime loading.
        // InitializeComponent();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 启动打点：Avalonia 框架初始化完成锚点（含 BuildAvaloniaApp/UsePlatformDetect/App.axaml 资源解析）
        Services.StartupTimer.Mark("Avalonia.InitDone");

        // ── 注册代码页编码提供程序（GBK/GB2312/936 等中文编码）──
        // 必须在所有路径（CLI/正常启动）最早执行，否则 Encoding.GetEncoding(936)
        // 抛 NotSupportedException 导致文本预览退化为 "coding 936 无法预览"。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // ── Initialize OLE for drag-drop (required on Windows for DragDrop.DoDragDropAsync) ──
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            NativeMethods.OleInitialize(nint.Zero);
        Services.StartupTimer.Mark("Init.EncodingOle");

        // ── Apply theme (System/Light/Dark) ──
        ApplyTheme();
        Services.StartupTimer.Mark("Init.Theme");
        if (PlatformSettings is IPlatformSettings ps)
        {
            ps.ColorValuesChanged += (_, _) =>
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // 仅「跟随系统」模式需要响应系统主题变化
                    if (AppSettings.Load().Theme == "System")
                        ApplyTheme();
                });
        }

        // ── Apply global font from settings ──
        ApplyAppFontFamily();
        Services.StartupTimer.Mark("Init.Font");

        // ── Apply compactness mode ──
        var appSettings = AppSettings.Load();
        var compactMode = appSettings.CompactnessMode switch
        {
            "Compact" => CompactnessMode.Compact,
            "Loose" => CompactnessMode.Loose,
            _ => CompactnessMode.Normal,
        };
        ApplyCompactness(compactMode);
        Services.StartupTimer.Mark("Init.Settings");

        // ── CoreLog 初始化：注入诊断开关 + 路径脱敏委托 ──
        // CoreLog.Trace/Info/Error 写入同一 debug.log，但 Core 层无法引用 AppSettings，
        // 必须由 UI 层在启动时注入脱敏逻辑，否则 Core 日志中的路径不会被脱敏。
        CoreLog.Initialize(appSettings.EnableDebugLogging, appSettings.LogPrivacyMode);

        // ── 启动时清理临时目录（对齐 WPF App.xaml.cs:141）—— fire-and-forget，不阻塞 UI ──
        _ = Task.Run(CleanTempOnStartupCore);

        // ── Initialize preview settings (runtime caches; SettingsWindow.Save 同步保持即时生效) ──
        PreviewService.EnableFormatDetection = appSettings.EnableFormatDetection;
        PreviewService.PreviewHeadSize = appSettings.PreviewHeadSize;
        PreviewService.EnableImagePreview = appSettings.EnableImagePreview;
        PreviewService.EnableTextPreview = appSettings.EnableTextPreview;
        PreviewService.MaxPreviewFileSize = appSettings.MaxPreviewFileSize;
        PreviewService.MaxTextPreviewBytes = appSettings.MaxTextPreviewBytes;
        PreviewService.MaxTablePreviewRows = appSettings.MaxTablePreviewRows;
        PreviewService.MaxTablePreviewCols = appSettings.MaxTablePreviewCols;

        // ── Restore saved language (AppSettings uses "zh"/"en"/"zh-TW") ──
        LocalizationManager.CurrentLanguage =
            LocalizationManager.FromSettingsCode(appSettings.Language);
        Services.StartupTimer.Mark("Init.PreviewCfgLocale");

        // ── 7z.dll 路径接线 + 用户解析回调（对齐 WPF InitializeApp，App.xaml.cs:46-68）──
        // 从用户设置加载 7z.dll 路径，覆盖 SevenZipEngine 的默认值
        try
        {
            if (!string.IsNullOrEmpty(appSettings.SevenZipPath))
            {
                SevenZipEngine.SevenZipDllPath = appSettings.SevenZipPath;
                DebugLog($"OnFrameworkInitializationCompleted: SevenZipDllPath set to {appSettings.SevenZipPath}");
            }
        }
        catch (Exception sevenZipInitEx)
        {
            DebugLog($"OnFrameworkInitializationCompleted: failed to apply SevenZipPath setting: {sevenZipInitEx.Message}");
        }

        // 立即把 7z.dll 路径交给 SharpSevenZip：库是进程级单例，一旦被加载就无法再换路径。
        // 必须早于任何 SharpSevenZip 触达点（如右键解压前的密码探测），否则会先按默认探测路径
        // 加载库、后续再设用户配置的路径即抛 SharpSevenZipLibraryException（issue #52）。
        SevenZipEngine.ConfigureLibraryPathEarly();

        // 注册 7z.dll 解析回调 — 默认位置找不到时弹出对话框让用户手动指定
        SevenZipEngine.SevenZipDllResolveCallback = ResolveSevenZipDllViaDialog;
        Services.StartupTimer.Mark("Init.SevenZip");

        // ── 首次运行：Shell 集成安装（延迟到用户进程，非提权）──
        // 安装程序会写入 FirstRunShell=1 / FirstRunAssoc=1 到注册表，首次启动时处理
        var isPortable = File.Exists(Path.Combine(AppContext.BaseDirectory, "Portable.txt"));
        if (!isPortable)
        {
            try
            {
                using var firstRunKey = RegistryHost.Open(
                    @"Software\MantisZip", writable: true);
                if (firstRunKey != null)
                {
                    var firstRunShell = firstRunKey.GetValue("FirstRunShell") as string;
                    if (firstRunShell == "1")
                    {
                        App.DebugLog("OnFrameworkInitializationCompleted: FirstRunShell marker found, installing shell integration...");
                        ShellIntegration.Install();
                        firstRunKey.DeleteValue("FirstRunShell");
                        App.DebugLog("OnFrameworkInitializationCompleted: first-run shell integration installed");
                    }

                    var firstRunAssoc = firstRunKey.GetValue("FirstRunAssoc") as string;
                    if (firstRunAssoc == "1")
                    {
                        App.DebugLog("OnFrameworkInitializationCompleted: FirstRunAssoc marker found, registering file associations...");
                        ShellIntegration.InstallAssociations();
                        firstRunKey.DeleteValue("FirstRunAssoc");
                        App.DebugLog("OnFrameworkInitializationCompleted: first-run file associations registered");
                    }
                }
            }
            catch (Exception firstRunEx)
            {
                App.DebugLog($"OnFrameworkInitializationCompleted: first-run handling failed: {firstRunEx.Message}");
            }

            // ── 文件关联自愈：修复"UserChoice 悬空/ProgId 被误删"的历史损坏 ──
            try
            {
                ShellIntegration.SelfHealAssociations(AppSettings.Load());
            }
            catch (Exception healEx)
            {
                App.DebugLog($"OnFrameworkInitializationCompleted: SelfHealAssociations failed: {healEx.Message}");
            }

            // ── 检查 COM 动态菜单状态（如果 pending，检测 Explorer 是否已加载 comhost.dll）──
            try
            {
                ShellIntegration.CheckComStatus();
            }
            catch (Exception comCheckEx)
            {
                App.DebugLog($"OnFrameworkInitializationCompleted: CheckComStatus failed: {comCheckEx.Message}");
            }
        }
        else
        {
            App.DebugLog("OnFrameworkInitializationCompleted: portable mode detected, skipping shell integration and file association registration");
        }
        Services.StartupTimer.Mark("Init.ShellFirstRun");

        // 启动打点：App 初始化全部完成（CLI 分发之前）
        Services.StartupTimer.Mark("Init.Done");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 进程退出诊断（纯观察）：窗口生命周期 + 僵尸状态 + 异常记录 → %LOCALAPPDATA%\MantisZip\lifecycle.log
            Services.StartupTimer.Mark("Init.LifetimeDiag.Enter");
            Services.LifetimeDiagnostics.Install(desktop);
            Services.StartupTimer.Mark("Init.LifetimeDiag.Exit");

            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();

            if (args.Length == 0)
            {
                // No args: normal UI startup
                Services.StartupTimer.Mark("Init.MainWindow.New");
                desktop.MainWindow = new MainWindow();
                Services.NativeSplash.Hook(desktop.MainWindow); // startup-native-splash：主窗口首帧关 splash
            }
            else
            {
                var command = args[0].ToLowerInvariant();
                var cmdPaths = args.Length > 1 ? args.Skip(1).ToArray() : Array.Empty<string>();
                var path = cmdPaths.Length > 0 ? cmdPaths[0] : null;

                switch (command)
                {
                    case "--open":
                        // Open archive in MainWindow
                        Services.StartupTimer.Mark("Init.MainWindow.New");
                        desktop.MainWindow = new MainWindow();
                        Services.NativeSplash.Hook(desktop.MainWindow); // startup-native-splash：主窗口关 splash（加载遮罩 B 接管后续反馈）
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            var mainWindow = (MainWindow)desktop.MainWindow;
                            mainWindow.LoadArchiveOnStartup(path);
                        }
                        break;

                    case "--open-dispatch":
                        // Dispatch based on DoubleClickAction setting (used by file association / shell verb)
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            var settings = AppSettings.Load();
                            var action = settings.DoubleClickAction ?? "open";
                            switch (action)
                            {
                                case "extract-here":
                                    _ = RunExtractCliAsync(path, Path.GetDirectoryName(path) ?? ".", args, desktop);
                                    break;
                                case "smart-extract":
                                    _ = RunExtractSmartCliAsync(path, args, desktop);
                                    break;
                                case "extract-dialog":
                                    _ = RunExtractCliAsync(path, Path.GetDirectoryName(path) ?? ".", args, desktop);
                                    break;
                                default: // "open"
                                    Services.StartupTimer.Mark("Init.MainWindow.New");
                                    desktop.MainWindow = new MainWindow();
                                    Services.NativeSplash.Hook(desktop.MainWindow); // startup-native-splash：主窗口关 splash
                                    var mainWindow = (MainWindow)desktop.MainWindow;
                                    mainWindow.LoadArchiveOnStartup(path);
                                    break;
                            }
                        }
                        else
                        {
                            Services.StartupTimer.Mark("Init.MainWindow.New");
                            desktop.MainWindow = new MainWindow();
                            Services.NativeSplash.Hook(desktop.MainWindow); // startup-native-splash：主窗口关 splash
                        }
                        break;

                    case "--extract":
                        // 弹出解压设置窗口（对齐 WPF HandleExtractBatch mode=extract）：
                        // 用户选择目标路径 / 冲突策略 / 过滤条件后批处理解压，取消则退出。
                        // 多选时 Explorer 可能逐项启动进程（静态层叠兜底菜单只带 %1），
                        // 先做 IPC 合并，保证只弹一个设置窗。
                        HandleExtract(cmdPaths.ToList(), desktop);
                        break;

                    case "--extract-here":
                        // Extract to each archive's directory (压缩包所在目录), not the process working directory
                        // 多选时同样先做 IPC 合并，再按最终数量走单文件流程或批处理
                        HandleDirectExtract(cmdPaths.ToList(), "here", args, desktop);
                        break;

                    case "--extract-to-name":
                        // Extract to subfolder named after archive (no extension)
                        HandleDirectExtract(cmdPaths.ToList(), "toname", args, desktop);
                        break;

                    case "--extract-smart":
                        // Smart extract: analyze archive structure and choose extraction mode
                        HandleDirectExtract(cmdPaths.ToList(), "smart", args, desktop);
                        break;

                    case "--compress":
                        // IPC multi-instance compress with settings dialog
                        HandleCompress(cmdPaths.ToList(), desktop);
                        break;

                    case "--compress-quick":
                        // Direct compress with AppSettings defaults + ProgressWindow
                        HandleCompressQuick(cmdPaths.ToList(), desktop);
                        break;

                    case "--compress-separate":
                        // IPC multi-instance per-item sequential compress
                        HandleCompressSeparate(cmdPaths.ToList(), desktop);
                        break;

                    case "--compress-combined":
                        // IPC multi-instance single combined archive
                        HandleCompressCombined(cmdPaths.ToList(), desktop);
                        break;

                    case "--install-shell":
                    case "--uninstall-shell":
                    case "--install-assoc":
                    case "--uninstall-assoc":
                        HandleShellCommand(command, desktop);
                        break;

                    case "--help":
                    case "-h":
                        // Show CLI help (console output if launched from terminal, dialog otherwise), then exit
                        _ = ShowCliHelpThenShutdown(desktop);
                        break;

                    case "--test":
                        // Startup self-test: show success dialog, then exit
                        _ = ShowStartupTestThenShutdown(desktop);
                        break;

                    default:
                        // Unknown args: just show UI
                        DebugLog($"警告: 无法识别的命令行参数 '{command}'。使用 --help 查看可用命令。");
                        Services.StartupTimer.Mark("Init.MainWindow.New");
                        desktop.MainWindow = new MainWindow();
                        Services.NativeSplash.Hook(desktop.MainWindow); // startup-native-splash：主窗口关 splash
                        break;
                }
            }
        }

        base.OnFrameworkInitializationCompleted();

        // 主循环启动窗口结束：此后 AppShutdown.Request 与直接 Shutdown 等价（见 Services/AppShutdown.cs）
        AppShutdown.MarkFrameworkInitCompleted();
    }

    // ════════════════════════════════════════════════════════════════
    //  CLI helpers (--help / --test) + 7z.dll resolve callback
    // ════════════════════════════════════════════════════════════════

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    /// <summary>
    /// 显示 CLI 帮助信息后退出。优先输出到父控制台（从 cmd/powershell 调用时），否则弹窗显示。
    /// （移植自 WPF App.ShowHelp，App.xaml.cs:927）
    /// </summary>
    private static async Task ShowCliHelpThenShutdown(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var helpText = LocalizationManager.T("App_CliHelp", AppConstants.Version);

        if (AttachConsole(ATTACH_PARENT_PROCESS))
        {
            Console.WriteLine(helpText);
            FreeConsole();
            AppShutdown.Request(desktop.Shutdown);
            return;
        }

        await AppMessageBox.Show(helpText, LocalizationManager.T("App_CliHelpTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
        AppShutdown.Request(desktop.Shutdown);
    }

    /// <summary>
    /// 显示启动测试成功弹窗后退出（--test 模式，对齐 WPF case "--test"）。
    /// </summary>
    private static async Task ShowStartupTestThenShutdown(IClassicDesktopStyleApplicationLifetime desktop)
    {
        DebugLog("--test 模式：启动测试成功");
        await AppMessageBox.Show(
            LocalizationManager.T("App_StartupTestMessage",
                AppConstants.Version, AppContext.BaseDirectory),
            LocalizationManager.T("App_StartupTestTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
        AppShutdown.Request(desktop.Shutdown);
    }

    /// <summary>
    /// 7z.dll 用户解析回调（SevenZipEngine.SevenZipDllResolveCallback）：
    /// 弹窗说明原因 + 文件选择框让用户手动定位 7z.dll，选择后保存到设置。
    /// 引擎在后台线程锁内同步调用本回调，经 UI 线程 Post + TCS 阻塞等待结果；
    /// 若恰好在 UI 线程触发则放弃弹窗（无法阻塞等待模态框），退回设置值避免死锁。
    /// </summary>
    private static string? ResolveSevenZipDllViaDialog()
    {
        var dispatcher = Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
        {
            DebugLog("ShowSevenZipDllDialog: invoked on UI thread, skipping dialog to avoid deadlock");
            return TryGetConfiguredSevenZipDllPath();
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Post(() => _ = ResolveSevenZipDllViaDialogCore(tcs));
        try
        {
            tcs.Task.Wait();
        }
        catch (AggregateException ex)
        {
            DebugLog($"ShowSevenZipDllDialog: wait failed: {ex.GetBaseException().Message}");
            return null;
        }
        return tcs.Task.Result;
    }

    private static async Task ResolveSevenZipDllViaDialogCore(TaskCompletionSource<string?> tcs)
    {
        try
        {
            // 先说明原因（对齐 WPF QuickPathPreDialog 文件模式的引导语义）
            var confirm = await AppMessageBox.Show(
                LocalizationManager.T("SevenZipDll_MissingMessage"),
                LocalizationManager.T("SevenZipDll_MissingTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK)
            {
                tcs.TrySetResult(null);
                return;
            }

            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner == null)
            {
                DebugLog("ShowSevenZipDllDialog: no main window available, cannot open file picker");
                tcs.TrySetResult(null);
                return;
            }

            var picked = await CustomFilePickerDialog.ShowOpenFileAsync(
                owner,
                initialPath: TryGetConfiguredSevenZipDllPath(),
                fileExtensions: ["*.dll"]);
            if (!string.IsNullOrEmpty(picked))
            {
                SaveSevenZipPath(picked);
                tcs.TrySetResult(picked);
                return;
            }

            tcs.TrySetResult(null);
        }
        catch (Exception ex)
        {
            DebugLog($"ShowSevenZipDllDialog failed: {ex.Message}");
            tcs.TrySetResult(null);
        }
    }

    /// <summary>读取设置中的 7z.dll 路径（仅当文件存在时返回）。</summary>
    private static string? TryGetConfiguredSevenZipDllPath()
    {
        try
        {
            var p = AppSettings.Load().SevenZipPath;
            return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
        }
        catch (Exception ex)
        {
            DebugLog($"TryGetConfiguredSevenZipDllPath failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>保存用户指定的 7z.dll 路径到设置。</summary>
    private static void SaveSevenZipPath(string path)
    {
        try
        {
            var settings = AppSettings.Load();
            settings.SevenZipPath = path;
            if (!settings.Save())
                DebugLog("ShowSevenZipDllDialog: settings.Save() returned false");
            else
                DebugLog($"ShowSevenZipDllDialog: saved 7z.dll path to settings: {path}");
        }
        catch (Exception ex)
        {
            DebugLog($"ShowSevenZipDllDialog: failed to save settings: {ex.Message}");
        }
    }

    /// <summary>调试日志文件大小上限（10 MB），超过时自动轮转（与 WPF 版一致）。</summary>
    private const long MaxDebugLogFileSize = 10L * 1024 * 1024;

    /// <summary>调试日志开关缓存（首次调用时从设置读取；SettingsWindow 保存后调用 <see cref="RefreshDebugLogSettings"/> 刷新）</summary>
    private static bool? _debugLogEnabled;
    private static string _debugLogPrivacyMode = "extension";

    internal static void DebugLog(string msg)
    {
        // 惰性读取设置并缓存（AppSettings.Load() 每次读磁盘，不能每次调用都读）
        if (!_debugLogEnabled.HasValue)
            RefreshDebugLogSettings();
        if (_debugLogEnabled != true)
            return;

        try
        {
            var logPath = Path.Combine(
                AppSettings.DataDir, "debug.log");
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            RotateDebugLogIfNeeded(logPath);

            // 与 App.Log 保持一致：写入前做路径脱敏
            var redacted = LogRedactor.RedactPaths(msg, LogRedactor.ParseMode(_debugLogPrivacyMode));
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}\n");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App.DebugLog write failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 刷新调试日志开关与脱敏模式缓存。SettingsWindow 保存设置后调用。
    /// </summary>
    internal static void RefreshDebugLogSettings()
    {
        var settings = AppSettings.Load();
        _debugLogEnabled = settings.EnableDebugLogging;
        _debugLogPrivacyMode = settings.LogPrivacyMode;
        // 同步刷新 CoreLog 的诊断开关与脱敏委托
        CoreLog.Initialize(settings.EnableDebugLogging, settings.LogPrivacyMode);
    }

    /// <summary>
    /// 检查日志文件大小，超过上限时自动轮转（添加时间戳后缀）。与 WPF 版行为一致。
    /// </summary>
    private static void RotateDebugLogIfNeeded(string logPath)
    {
        try
        {
            var fileInfo = new FileInfo(logPath);
            if (fileInfo.Exists && fileInfo.Length > MaxDebugLogFileSize)
            {
                var backupPath = logPath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                File.Move(logPath, backupPath);
            }
        }
        catch { /* 轮转失败不影响继续写入 */ }
    }

}
