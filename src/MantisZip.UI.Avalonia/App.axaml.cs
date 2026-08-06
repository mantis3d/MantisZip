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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // When precompiled XAML is available, AvaloniaXamlLoader uses it.
        // If not available (e.g. after clean build), InitializeComponent() from
        // the source generator handles runtime loading.
        // InitializeComponent();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // ── Initialize OLE for drag-drop (required on Windows for DragDrop.DoDragDropAsync) ──
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            NativeMethods.OleInitialize(nint.Zero);

        // ── Apply theme (System/Light/Dark) ──
        ApplyTheme();
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

        // ── Apply compactness mode ──
        var appSettings = AppSettings.Load();
        var compactMode = appSettings.CompactnessMode switch
        {
            "Compact" => CompactnessMode.Compact,
            "Loose" => CompactnessMode.Loose,
            _ => CompactnessMode.Normal,
        };
        ApplyCompactness(compactMode);

        // ── Initialize magic detection settings ──
        PreviewService.EnableFormatDetection = appSettings.EnableFormatDetection;
        PreviewService.PreviewHeadSize = appSettings.PreviewHeadSize;

        // ── Restore saved language (AppSettings uses "zh"/"en", LocalizationManager uses zh-CN/en) ──
        if (appSettings.Language == "en")
            LocalizationManager.CurrentLanguage = AppLanguage.English;

        // ── 首次运行：Shell 集成安装（延迟到用户进程，非提权）──
        // 安装程序会写入 FirstRunShell=1 / FirstRunAssoc=1 到注册表，首次启动时处理
        var isPortable = File.Exists(Path.Combine(AppContext.BaseDirectory, "Portable.txt"));
        if (!isPortable)
        {
            try
            {
                using var firstRunKey = Registry.CurrentUser.OpenSubKey(
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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();

            if (args.Length == 0)
            {
                // No args: normal UI startup
                desktop.MainWindow = new MainWindow();
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
                        desktop.MainWindow = new MainWindow();
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
                                    _ = RunExtractCliAsync(path, Directory.GetCurrentDirectory(), args, desktop);
                                    break;
                                case "smart-extract":
                                    _ = RunExtractSmartCliAsync(path, args, desktop);
                                    break;
                                case "extract-dialog":
                                    _ = RunExtractCliAsync(path, Path.GetDirectoryName(path) ?? ".", args, desktop);
                                    break;
                                default: // "open"
                                    desktop.MainWindow = new MainWindow();
                                    var mainWindow = (MainWindow)desktop.MainWindow;
                                    mainWindow.LoadArchiveOnStartup(path);
                                    break;
                            }
                        }
                        else
                        {
                            desktop.MainWindow = new MainWindow();
                        }
                        break;

                    case "--extract":
                        // Extract to same directory as archive
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            _ = RunExtractCliAsync(path, Path.GetDirectoryName(path) ?? ".", args, desktop);
                        else
                            desktop.Shutdown();
                        break;

                    case "--extract-here":
                        // Extract to current directory
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            _ = RunExtractCliAsync(path, Directory.GetCurrentDirectory(), args, desktop);
                        else
                            desktop.Shutdown();
                        break;

                    case "--extract-to-name":
                        // Extract to subfolder named after archive (no extension)
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            var dirName = Path.GetFileNameWithoutExtension(path);
                            // Handle .tar.gz double extension
                            if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                                dirName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
                            var targetDir = Path.Combine(Path.GetDirectoryName(path) ?? ".", dirName);
                            Directory.CreateDirectory(targetDir);
                            _ = RunExtractCliAsync(path, targetDir, args, desktop);
                        }
                        else
                            desktop.Shutdown();
                        break;

                    case "--extract-smart":
                        // Smart extract: analyze archive structure and choose extraction mode
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            _ = RunExtractSmartCliAsync(path, args, desktop);
                        else
                            desktop.Shutdown();
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

                    default:
                        // Unknown args: just show UI
                        desktop.MainWindow = new MainWindow();
                        break;
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ════════════════════════════════════════════════════════════════
    //  Theme
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 应用主题（三态）：System=跟随系统，Light=亮色，Dark=暗色。
    /// 替换主题 ResourceDictionary 并设置 RequestedThemeVariant 供 FluentTheme 使用。
    /// </summary>
    private void ApplyTheme()
    {
        if (PlatformSettings is not IPlatformSettings ps) return;
        try
        {
            var settings = AppSettings.Load();
            bool isDark = settings.Theme switch
            {
                "Light" => false,
                "Dark" => true,
                _ => ps.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark,
            };
            DebugLog($"[Theme] ApplyTheme theme={settings.Theme} dark={isDark}");

            // ── Swap resource dictionary ──
            var uri = new Uri(isDark ? DarkThemeUri : LightThemeUri);
            Resources.MergedDictionaries.Clear();
            if (AvaloniaXamlLoader.Load(uri) is IResourceProvider themeProvider)
                Resources.MergedDictionaries.Add(themeProvider);

            // ── Re-add icon resources (cleared above) ──
            var iconUri = new Uri(IconResourcesUri);
            if (AvaloniaXamlLoader.Load(iconUri) is IResourceProvider iconProvider)
                Resources.MergedDictionaries.Add(iconProvider);

            // ── Set theme variant for FluentTheme ──
            RequestedThemeVariant = isDark
                ? global::Avalonia.Styling.ThemeVariant.Dark
                : global::Avalonia.Styling.ThemeVariant.Light;
        }
        catch (Exception ex)
        {
            DebugLog($"[Theme] ApplyTheme ERROR: {ex.Message}");
        }
    }

    /// <summary>
    /// 供 SettingsWindow 保存设置后立即刷新主题（无需重启）。
    /// </summary>
    internal static void RefreshTheme()
    {
        if (Current is App app)
            app.ApplyTheme();
    }

    /// <summary>
    /// 从 AppSettings 读取全局界面字体设置，更新 AppGlobalFont 资源及所有已打开窗口。
    /// </summary>
    private void ApplyAppFontFamily()
    {
        try
        {
            var settings = AppSettings.Load();
            var fontName = settings.AppFontFamily;

            if (string.IsNullOrEmpty(fontName))
            {
                // 默认字体：重建资源确保 DynamicResource 重求值，清除窗口本地值让 Style 接管
                Resources.Remove("AppGlobalFont");
                Resources.Add("AppGlobalFont", FontFamily.Default);
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var w in desktop.Windows)
                        w.ClearValue(TextBlock.FontFamilyProperty);
                }
            }
            else
            {
                // 指定字体：更新资源 + 设置窗口本地值，确保立即可见
                var font = new FontFamily(fontName);
                Resources["AppGlobalFont"] = font;
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var w in desktop.Windows)
                        w.FontFamily = font;
                }
            }

            DebugLog($"[Font] Applied global font: {(string.IsNullOrEmpty(fontName) ? "default" : fontName)}");
        }
        catch (Exception ex)
        {
            DebugLog($"[Font] Failed to apply global font: {ex.Message}");
        }
    }

    /// <summary>
    /// 供 SettingsWindow 保存设置后立即刷新全局字体（无需重启）。
    /// </summary>
    internal static void RefreshAppFontFamily()
    {
        if (Current is App app)
            app.ApplyAppFontFamily();
    }

    /// <summary>
    /// 应用紧凑度模式，覆盖 12 个间距/控件高度资源的运行时值。
    /// 使用 DynamicResource 的自动更新机制即时生效（无需重启）。
    /// </summary>
    private void ApplyCompactness(CompactnessMode mode)
    {
        DebugLog($"[Compactness] Applying mode: {mode}");

        // 三档间距数值
        static double GetSpacing(string key, CompactnessMode m) => (key, m) switch
        {
            ("SpacingXxs", CompactnessMode.Compact) => 2,
            ("SpacingXxs", CompactnessMode.Normal) => 4,
            ("SpacingXxs", CompactnessMode.Loose) => 6,

            ("SpacingXs", CompactnessMode.Compact) => 4,
            ("SpacingXs", CompactnessMode.Normal) => 8,
            ("SpacingXs", CompactnessMode.Loose) => 12,

            ("SpacingSm", CompactnessMode.Compact) => 8,
            ("SpacingSm", CompactnessMode.Normal) => 12,
            ("SpacingSm", CompactnessMode.Loose) => 16,

            ("SpacingMd", CompactnessMode.Compact) => 12,
            ("SpacingMd", CompactnessMode.Normal) => 16,
            ("SpacingMd", CompactnessMode.Loose) => 24,

            ("SpacingLg", CompactnessMode.Compact) => 16,
            ("SpacingLg", CompactnessMode.Normal) => 24,
            ("SpacingLg", CompactnessMode.Loose) => 32,

            ("SpacingXl", CompactnessMode.Compact) => 24,
            ("SpacingXl", CompactnessMode.Normal) => 32,
            ("SpacingXl", CompactnessMode.Loose) => 48,

            ("ControlHeightSm", CompactnessMode.Compact) => 22,
            ("ControlHeightSm", CompactnessMode.Normal) => 26,
            ("ControlHeightSm", CompactnessMode.Loose) => 30,

            ("ControlHeightMd", CompactnessMode.Compact) => 28,
            ("ControlHeightMd", CompactnessMode.Normal) => 32,
            ("ControlHeightMd", CompactnessMode.Loose) => 38,

            ("ControlHeightLg", CompactnessMode.Compact) => 42,
            ("ControlHeightLg", CompactnessMode.Normal) => 48,
            ("ControlHeightLg", CompactnessMode.Loose) => 54,

            ("ControlMinHeight", CompactnessMode.Compact) => 32,
            ("ControlMinHeight", CompactnessMode.Normal) => 40,
            ("ControlMinHeight", CompactnessMode.Loose) => 48,

            ("BorderRadius", CompactnessMode.Compact) => 4,
            ("BorderRadius", CompactnessMode.Normal) => 6,
            ("BorderRadius", CompactnessMode.Loose) => 8,

            ("DialogPadding", CompactnessMode.Compact) => 12,
            ("DialogPadding", CompactnessMode.Normal) => 16,
            ("DialogPadding", CompactnessMode.Loose) => 24,

            _ => 0,
        };

        var keys = new[]
        {
            "SpacingXxs", "SpacingXs", "SpacingSm", "SpacingMd", "SpacingLg", "SpacingXl",
            "ControlHeightSm", "ControlHeightMd", "ControlHeightLg", "ControlMinHeight",
        };

        foreach (var key in keys)
            Resources[key] = GetSpacing(key, mode);

        // Typed resources — double primitive cannot be used directly on
        // CornerRadius / Thickness properties via DynamicResource.
        Resources["BorderRadius"] = new CornerRadius(GetSpacing("BorderRadius", mode));
        Resources["DialogPadding"] = new Thickness(GetSpacing("DialogPadding", mode));
        // Thickness variants of spacing keys (for Margin / Padding)
        foreach (var k in new[] { "SpacingXxs", "SpacingXs", "SpacingSm", "SpacingMd", "SpacingLg", "SpacingXl" })
            Resources[k + "Thk"] = new Thickness(GetSpacing(k, mode));

        // TextControlPadding: use smaller horizontal padding (SpacingXxs) and
        // larger vertical padding (SpacingXs) so switching to Loose mode increases
        // vertical breathing room without shrinking the editable width too much.
        // Reduce TextControlPadding to half to keep TextBox padding visually aligned
        // with other controls while preserving increased vertical breathing room.
        Resources["TextControlPadding"] = new Thickness(
            GetSpacing("SpacingXxs", mode) * 0.5, // left/right (half)
            GetSpacing("SpacingXs", mode) * 0.5   // top/bottom (half)
        );
    }

    /// <summary>
    /// 供 SettingsWindow 保存设置后立即刷新紧凑度（无需重启）。
    /// </summary>
    internal static void RefreshCompactness()
    {
        if (Current is App app)
        {
            var settings = AppSettings.Load();
            var mode = settings.CompactnessMode switch
            {
                "Compact" => CompactnessMode.Compact,
                "Loose" => CompactnessMode.Loose,
                _ => CompactnessMode.Normal,
            };
            app.ApplyCompactness(mode);
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
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MantisZip", "debug.log");
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            RotateDebugLogIfNeeded(logPath);

            // 与 App.Log 保持一致：写入前做路径脱敏
            var redacted = LogRedactor.RedactPaths(msg, LogRedactor.ParseMode(_debugLogPrivacyMode));
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}\n");
        }
        catch { }
    }

    /// <summary>
    /// 刷新调试日志开关与脱敏模式缓存。SettingsWindow 保存设置后调用。
    /// </summary>
    internal static void RefreshDebugLogSettings()
    {
        var settings = AppSettings.Load();
        _debugLogEnabled = settings.EnableDebugLogging;
        _debugLogPrivacyMode = settings.LogPrivacyMode;
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

    // ════════════════════════════════════════════════════════════════
    //  Extract helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 为 CLI 解压解析密码：从 PasswordManager 查找已保存密码并快速验证。
    /// </summary>
    private static string? ResolveCliPassword(string archivePath, IArchiveEngine engine)
    {
        try
        {
            var allMatches = PasswordManager.Instance.FindMatchingPasswords(archivePath);
            foreach (var entry in allMatches)
            {
                var pwd = entry.Password;
                if (string.IsNullOrEmpty(pwd)) continue;

                var service = new PasswordService();
                if (service.QuickVerifyPassword(archivePath, pwd, engine))
                {
                    DebugLog($"CLI extract: saved password matched for '{archivePath}'");
                    return pwd;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"CLI extract: password lookup failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 带提权支持的异步解压。先检查目标目录可写性，权限不足时弹出提权对话框。
    /// 自动尝试已保存密码。
    /// </summary>
    private static async Task<bool> TryExtractArchiveAsync(
        string archivePath,
        string targetDir,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            // Pre-check target directory writability
            var unwritable = new List<string>();
            if (!IsDirectoryWritable(targetDir))
                unwritable.Add(targetDir);

            if (unwritable.Count > 0)
            {
                var restarted = await HandleElevationAsync(unwritable, originalArgs, desktop);
                // If user chose to restart as admin, we return without extracting.
                // If user declined or already elevated, we still return (can't proceed).
                return restarted;
            }

            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
            {
                Console.Error.WriteLine($"Unsupported archive format: {archivePath}");
                return false;
            }

            var password = ResolveCliPassword(archivePath, engine);
            return await RunCliExtractWithProgressAsync(
                archivePath, targetDir, password, engine, originalArgs, desktop);
        }
        catch (UnauthorizedAccessException)
        {
            return await HandleElevationAsync(new[] { targetDir }, originalArgs, desktop);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Extraction failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 带提权支持的异步智能解压。先分析目标目录可写性，权限不足时弹出提权对话框。
    /// 解压成功后根据 DeleteArchiveAfterExtract 设置尝试删除原包。
    /// 自动尝试已保存密码。
    /// </summary>
    private static async Task<bool> TryExtractSmartAsync(
        string archivePath,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        string targetDir = string.Empty;
        try
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
            {
                Console.Error.WriteLine($"Unsupported archive format: {archivePath}");
                return false;
            }

            // List entries to analyze structure
            var items = await engine.ListEntriesAsync(archivePath);
            var hasSingleRoot = ArchiveStructureAnalyzer.HasSingleRootDirectory(items);

            if (hasSingleRoot)
            {
                // Single root folder: extract to current directory
                targetDir = Directory.GetCurrentDirectory();
                Console.WriteLine("SmartExtract: single root detected, extracting to current directory");
            }
            else
            {
                // Dispersed files: extract to named subfolder
                var dirName = Path.GetFileNameWithoutExtension(archivePath);
                if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                    dirName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(archivePath));
                targetDir = Path.Combine(Path.GetDirectoryName(archivePath) ?? ".", dirName);
                Directory.CreateDirectory(targetDir);
                Console.WriteLine($"SmartExtract: dispersed structure, extracting to {targetDir}");
            }

            // Check writability before extraction
            var unwritable = new List<string>();
            if (!IsDirectoryWritable(targetDir))
                unwritable.Add(targetDir);

            if (unwritable.Count > 0)
                return await HandleElevationAsync(unwritable, originalArgs, desktop);

            var password = ResolveCliPassword(archivePath, engine);
            return await RunCliExtractWithProgressAsync(
                archivePath, targetDir, password, engine, originalArgs, desktop);
        }
        catch (UnauthorizedAccessException)
        {
            return !string.IsNullOrEmpty(targetDir)
                ? await HandleElevationAsync(new[] { targetDir }, originalArgs, desktop)
                : false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Smart extraction failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 尝试在解压后删除原始压缩包（移动到回收站）。
    /// 基于 DoubleClickOpenThreshold 的文件大小检查由 UI 侧的点击事件完成，
    /// CLI 模式不做额外大小检查。
    /// 重试 3 次（200ms 间隔），给 7z.dll 等外部组件释放文件句柄的时间。
    /// </summary>
    private static void TryDeleteArchiveAfterExtract(string archivePath)
    {
        var settings = AppSettings.Load();
        if (!settings.DeleteArchiveAfterExtract) return;
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return;

        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                FileSystem.DeleteFile(
                    archivePath,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin);
                DebugLog($"TryDeleteArchiveAfterExtract: moved '{archivePath}' to recycle bin");
                return;
            }
            catch (Exception ex) when (retry < 2)
            {
                DebugLog($"TryDeleteArchiveAfterExtract: attempt {retry + 1} failed for '{archivePath}': {ex.Message}");
                Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                DebugLog($"TryDeleteArchiveAfterExtract: failed for '{archivePath}' after 3 attempts: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// CLI 解压入口包装：调用 TryExtractArchiveAsync，完成后 shutdown。
    /// </summary>
    private static async Task RunExtractCliAsync(
        string archivePath,
        string targetDir,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var restarted = await TryExtractArchiveAsync(archivePath, targetDir, originalArgs, desktop);
        if (!restarted)
            desktop.Shutdown();
    }

    /// <summary>
    /// CLI 智能解压入口包装：调用 TryExtractSmartAsync，完成后 shutdown。
    /// </summary>
    private static async Task RunExtractSmartCliAsync(
        string archivePath,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var restarted = await TryExtractSmartAsync(archivePath, originalArgs, desktop);
        if (!restarted)
            desktop.Shutdown();
    }

    /// <summary>
    /// 在进度窗口中执行 CLI 解压（对齐 WPF HandleExtractBatchCore）。
    /// 显示批处理文件列表（单文件也显示）、逐项状态、可暂停/取消；
    /// 成功时自动关闭（2.5s，尊重 📌 KeepOpenOnComplete），失败时等待用户手动关闭。
    /// 权限不足时关闭窗口后走提权流程。
    /// </summary>
    private static async Task<bool> RunCliExtractWithProgressAsync(
        string archivePath,
        string targetDir,
        string? password,
        IArchiveEngine engine,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var progressWindow = new ProgressWindow(LocalizationManager.T("Progress_Title_Extract"));
        progressWindow.InitCancellation();
        progressWindow.Show();
        desktop.MainWindow = progressWindow;

        // 始终显示文件列表（单文件也显示）：批处理列表项 = 压缩包路径
        progressWindow.InitBatchMode(new[] { archivePath });
        progressWindow.SetCurrentBatchItem(0);

        var ct = progressWindow.CancellationToken;
        var doneEvent = new ManualResetEventSlim(false);
        Exception? captureException = null;
        bool cancelled = false;
        bool elevationRequested = false;

        _ = Task.Run(async () =>
        {
            try
            {
                var rawProgress = progressWindow.CreatePauseAwareProgress(
                    ProgressWindow.CreateBackgroundProgress(progressWindow));

                var extractResult = await engine.ExtractAsync(archivePath, targetDir, password, rawProgress, ct);

                progressWindow.FinalizeBatch();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (extractResult.HasFailures)
                    {
                        // 部分条目失败（如权限不足）：显示错误汇总（对齐 WPF ExtractResult.HasFailures 路径）
                        progressWindow.SetErrorSummary(
                            $"{extractResult.FailedEntries} 个文件未能写入到 {targetDir}（可能权限不足）");
                    }
                    progressWindow.SetComplete(LocalizationManager.T("Cli_StatusDone"));
                });
                Console.WriteLine($"Extracted: {archivePath} -> {targetDir}");
                TryDeleteArchiveAfterExtract(archivePath);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (UnauthorizedAccessException)
            {
                // 目录创建级权限不足：关闭窗口后由外层走提权流程
                elevationRequested = true;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    progressWindow.Close();
                    desktop.MainWindow = null;
                });
            }
            catch (Exception ex)
            {
                captureException = ex;
                Console.Error.WriteLine($"Extraction failed: {ex.Message}");
            }
            finally
            {
                doneEvent.Set();
            }
        });

        await Task.Run(() => doneEvent.Wait());
        progressWindow.FinalizeBatch();

        if (elevationRequested)
        {
            return await HandleElevationAsync(new[] { targetDir }, originalArgs, desktop);
        }

        if (cancelled)
        {
            await Dispatcher.UIThread.InvokeAsync(() => progressWindow.Close());
            return false;
        }

        if (captureException != null)
        {
            // 失败：标记批次项为 Failed（FinalizeBatch 已把 InProgress 置为 Completed，这里覆盖为失败）
            progressWindow.UpdateBatchItemStatus(0, BatchItemStatus.Failed, captureException.Message);
            // 显示错误汇总并等待用户手动关闭窗口
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                progressWindow.SetErrorSummary(captureException!.Message);
                progressWindow.CompleteWithErrors();
            });
            await WaitForWindowCloseAsync(progressWindow);
            return false;
        }

        // 成功：自动关闭或等待（KeepOpenOnComplete 生效）
        await progressWindow.AutoCloseOrWaitAsync(2500, () => progressWindow.Close());
        return false;
    }

    /// <summary>
    /// 等待用户手动关闭进度窗口（等待 Closed 事件）。
    /// 若窗口已不可见（如取消流程已关闭），立即返回。
    /// </summary>
    private static async Task WaitForWindowCloseAsync(ProgressWindow window)
    {
        if (!window.IsVisible)
            return;

        var closed = new ManualResetEventSlim(false);
        EventHandler handler = null!;
        handler = (_, _) => { closed.Set(); window.Closed -= handler; };
        window.Closed += handler;
        try
        {
            await Task.Run(() => closed.Wait());
        }
        finally
        {
            window.Closed -= handler;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Shell commands (--install-shell, --uninstall-shell, etc.)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handle headless shell commands (--install-shell, --uninstall-shell, etc.).
    /// Uses Environment.Exit(0) instead of desktop.Shutdown() because these commands
    /// run during OnFrameworkInitializationCompleted, before the Dispatcher main loop
    /// starts — calling Shutdown() there causes InvalidOperationException.
    /// </summary>
    private static void HandleShellCommand(string command, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.Error.WriteLine("Shell integration is only supported on Windows.");
            Environment.Exit(1);
            return;
        }

        int exitCode = 0;

        try
        {
            switch (command)
            {
                case "--install-shell":
                    DebugLog("Shell command: --install-shell (native)");
                    ShellIntegration.Install();
                    Console.WriteLine("Shell extension installed successfully.");
                    break;

                case "--uninstall-shell":
                    DebugLog("Shell command: --uninstall-shell (native)");
                    ShellIntegration.Uninstall();
                    Console.WriteLine("Shell extension uninstalled successfully.");
                    break;

                case "--install-assoc":
                    DebugLog("Shell command: --install-assoc (native)");
                    ShellIntegration.InstallAssociations();
                    Console.WriteLine("File associations installed successfully.");
                    break;

                case "--uninstall-assoc":
                    DebugLog("Shell command: --uninstall-assoc (native)");
                    ShellIntegration.UninstallAssociations();
                    Console.WriteLine("File associations uninstalled successfully.");
                    break;

                default:
                    Console.Error.WriteLine($"Unknown shell command: {command}");
                    exitCode = 1;
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLog($"HandleShellCommand: {command} failed: {ex.Message}");
            Console.Error.WriteLine($"Failed to execute {command}: {ex.Message}");
            exitCode = 1;
        }

        Environment.Exit(exitCode);
    }

    // ════════════════════════════════════════════════════════════════
    //  Elevation helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 检测指定目录是否可写入。尝试创建测试文件并用 DeleteOnClose 自动清理。
    /// </summary>
    private static bool IsDirectoryWritable(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            var testFile = Path.Combine(dirPath, Path.GetRandomFileName());
            using (var fs = File.Create(testFile, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    /// <summary>
    /// 检测当前进程是否以管理员权限运行。
    /// </summary>
    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// 以管理员权限重新启动当前进程，传递原始 CLI 参数。
    /// </summary>
    private static void RestartAsAdmin(string[] originalArgs)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        var args = string.Join(" ", originalArgs.Select(a => $"\"{a}\""));
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            Verb = "runas",
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// 弹出权限不足对话框处理流程：
    ///   - 已提权 → 显示 ElevationFailedDialog
    ///   - 允许提权 → 显示 ElevationDialog → 用户选择提权则 RestartAsAdmin 后 shutdown
    ///   - 不允许/取消 → 显示 ElevationInfoDialog
    /// 返回 true 表示进程已重启（调用方无需再 shutdown），false 表示用户取消或已失败。
    /// </summary>
    private static async Task<bool> HandleElevationAsync(
        IReadOnlyList<string> unwritable,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var owner = desktop.MainWindow;

        if (IsElevated())
        {
            // Already elevated but still failing — show failed dialog
            var dlg = new Dialogs.ElevationFailedDialog(unwritable);
            if (owner != null)
                await dlg.ShowDialog<bool?>(owner);
            else
                dlg.Show();
            return false;
        }

        // Show elevation confirm dialog
        var elevationDlg = new Dialogs.ElevationDialog(unwritable);
        bool? result;
        if (owner != null)
        {
            result = await elevationDlg.ShowDialog<bool?>(owner);
        }
        else
        {
            // No window available yet (CLI mode), use a temp owner
            var tempOwner = new Window { IsVisible = false };
            desktop.MainWindow = tempOwner;
            result = await elevationDlg.ShowDialog<bool?>(tempOwner);
        }

        if (result == true)
        {
            // User chose to elevate — restart as admin and shutdown current process
            RestartAsAdmin(originalArgs);
            desktop.Shutdown();
            return true;
        }
        else
        {
            // User declined — show info dialog
            var infoDlg = new Dialogs.ElevationInfoDialog(unwritable);
            if (owner != null)
                await infoDlg.ShowDialog<bool?>(owner);
            else
                infoDlg.Show();
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  IPC helpers (NamedPipe multi-instance)
    // ════════════════════════════════════════════════════════════════

    private static void StartPipeServer(List<string> allPaths, CancellationToken ct, string pipeName, ManualResetEventSlim readyEvent)
    {
        Task.Run(async () =>
        {
            try
            {
                readyEvent.Set();
                while (!ct.IsCancellationRequested)
                {
                    using var pipe = new NamedPipeServerStream(
                        pipeName, PipeDirection.In, -1,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.WaitForConnectionAsync(ct);
                        var receivedCount = 0;
                        using var reader = new StreamReader(pipe);
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            lock (allPaths)
                            {
                                if (!allPaths.Contains(line) && (File.Exists(line) || Directory.Exists(line)))
                                {
                                    allPaths.Add(line);
                                    receivedCount++;
                                }
                            }
                        }
                        Debug.WriteLine($"PipeServer ({pipeName}): received {receivedCount} new paths from client");
                    }
                    catch (OperationCanceledException) { throw; }
                    finally { pipe.Dispose(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception pipeEx) { Debug.WriteLine($"PipeServer ({pipeName}): error: {pipeEx.Message}"); }
        });
    }

    private static void SendPathsThroughPipe(List<string> paths, string pipeName)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe);
            foreach (var p in paths)
                writer.WriteLine(p);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SendPathsThroughPipe ({pipeName}) failed: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  --compress : IPC + CompressSettingsWindow
    // ════════════════════════════════════════════════════════════════

    private static void HandleCompress(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0) { desktop.Shutdown(); return; }

        // 显式管理退出：对话框关闭时默认 ShutdownMode(OnLastWindowClose) 会立即退出进程，
        // 但压缩流程需要在对话框关闭后继续显示 ProgressWindow（对齐 WPF HandleCompress 的
        // app.ShutdownMode = OnExplicitShutdown 用法）
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (OperatingSystem.IsWindows())
        {
            bool firstInstance;
            var mutex = new Mutex(true, CompressMutexName, out firstInstance);

            if (firstInstance)
            {
                var allPaths = new List<string>(myPaths);
                var cts = new CancellationTokenSource();
                var pipeReady = new ManualResetEventSlim(false);
                StartPipeServer(allPaths, cts.Token, CompressPipeName, pipeReady);
                pipeReady.Wait(3000);

                _ = Task.Delay(800).ContinueWith(_ =>
                {
                    cts.Cancel();
                    mutex.Dispose();
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try { await ShowCompressDialogAndRun(allPaths, desktop); }
                        catch (Exception ex) { App.DebugLog($"HandleCompress: ShowCompressDialogAndRun 异常: {ex.Message}"); desktop.Shutdown(); }
                    });
                });
            }
            else
            {
                SendPathsThroughPipe(myPaths, CompressPipeName);
                desktop.Shutdown();
            }
        }
        else
        {
            // Non-Windows: show dialog directly (no IPC)
            Dispatcher.UIThread.Post(async () =>
            {
                try { await ShowCompressDialogAndRun(myPaths, desktop); }
                catch (Exception ex) { App.DebugLog($"HandleCompress: ShowCompressDialogAndRun 异常: {ex.Message}"); desktop.Shutdown(); }
            });
        }
    }

    private static async Task ShowCompressDialogAndRun(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var dlg = new CompressSettingsWindow(paths);

        // 压缩流程是否已接管（接管后由 CompressWithProgress 负责退出进程，这里不再退出）
        bool compressStarted = false;

        // 对话框关闭（用户点 X 或取消）→ 退出进程（对齐 WPF ShowCompressWindow 的 win.Closed += Shutdown）
        dlg.Closed += (_, _) =>
        {
            if (!compressStarted)
                desktop.Shutdown();
        };

        // Intercept CloseAction: on "Compress", run the compression
        dlg.ViewModel.CloseAction = async (result) =>
        {
            if (result)
            {
                compressStarted = true;
                // 此入口覆盖了窗口内部 CloseAction，需显式快照高级选项（仅本次压缩生效）
                dlg.SnapshotFormatOptionsToViewModel();
                dlg.Close();
                var vm = dlg.ViewModel;
                var request = new CompressRequest
                {
                    SourcePaths = paths.ToList(),
                    Mode = CompressOutputMode.Manual,
                    Format = vm.DefaultFormat,
                    CompressionLevel = vm.CompressionLevel,
                    Password = vm.GetActivePassword(),
                    Encrypt = vm.Encrypt,
                    Comment = vm.Comment,
                    CommentDistribution = vm.CommentDistribution,
                    OutputPath = vm.OutputPath,
                    PreserveDirectoryRoot = true,
                    SplitSize = vm.SplitSize,
                    FileNameEncoding = vm.FileNameEncoding,
                    ZipCompressionMethod = vm.ZipCompressionMethod,
                    ZipEncryptionMethod = vm.ZipEncryptionMethod,
                    SevenZipCompressionMethod = vm.SevenZipCompressionMethod,
                    SevenZipSolid = vm.SevenZipSolid,
                    SevenZipSolidBlockSize = vm.SevenZipSolidBlockSize,
                    SevenZipDictionarySize = vm.SevenZipDictionarySize,
                    SevenZipNumFastBytes = vm.SevenZipNumFastBytes,
                    SevenZipMatchFinder = vm.SevenZipMatchFinder,
                    SevenZipEncryptHeaders = vm.SevenZipEncryptHeaders,
                };

                await CompressWithProgress(request, LocalizationManager.T("Cli_Compress"), desktop);
            }
            else
            {
                // 取消 → 关闭对话框，Closed 事件触发退出
                dlg.Close();
            }
            await Task.CompletedTask;
        };

        dlg.Show();
        desktop.MainWindow = dlg;
    }

    // ════════════════════════════════════════════════════════════════
    //  --compress-quick : defaults + ProgressWindow, then exit
    // ════════════════════════════════════════════════════════════════

    private static void HandleCompressQuick(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0) { desktop.Shutdown(); return; }

        var settings = AppSettings.Load();

        // Auto-determine output path from first source
        var first = myPaths[0];
        var dir = File.Exists(first)
            ? Path.GetDirectoryName(first)
            : Path.GetDirectoryName(first.TrimEnd('\\', '/'));
        dir ??= Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var baseName = Path.GetFileNameWithoutExtension(first.TrimEnd('\\', '/'));
        var ext = settings.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + settings.DefaultFormat;
        var outputPath = Path.Combine(dir, baseName + ext);

        var request = new CompressRequest
        {
            SourcePaths = myPaths,
            Mode = CompressOutputMode.Manual,
            Format = settings.DefaultFormat,
            CompressionLevel = settings.DefaultLevel,
            OutputPath = outputPath,
            PreserveDirectoryRoot = true,
        };

        _ = CompressWithProgress(request, LocalizationManager.T("Cli_QuickCompress"), desktop);
    }

    // ════════════════════════════════════════════════════════════════
    //  --compress-separate : IPC + per-item batch compress
    // ════════════════════════════════════════════════════════════════

    private static void HandleCompressSeparate(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0) { desktop.Shutdown(); return; }

        if (OperatingSystem.IsWindows())
        {
            bool firstInstance;
            var mutex = new Mutex(true, CompressSeparateMutexName, out firstInstance);

            if (firstInstance)
            {
                var allPaths = new List<string>(myPaths);
                var cts = new CancellationTokenSource();
                var pipeReady = new ManualResetEventSlim(false);

        // 立即显示进度窗口，避免 IPC 收集期间用户无反馈（对齐 WPF HandleCompressSeparate）
        var progressWindow = new ProgressWindow(LocalizationManager.T("Progress_Batch_Title"));
        progressWindow.InitCancellation();
        progressWindow.Show();
        desktop.MainWindow = progressWindow;
        // 收集期预填充首个实例的路径，列表始终可见
        progressWindow.InitBatchMode(myPaths);
        progressWindow.SetProgress(0, LocalizationManager.T("App_CompressCollecting"));
                // IPC 收集期间点取消 → 同步终止管道收集
                progressWindow.CancellationToken.Register(() => cts.Cancel());

                StartPipeServer(allPaths, cts.Token, CompressSeparatePipeName, pipeReady);
                pipeReady.Wait(3000);

                _ = Task.Delay(800).ContinueWith(_ =>
                {
                    cts.Cancel();
                    mutex.Dispose();
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try { await RunCompressSeparate(allPaths, desktop, progressWindow); }
                        finally { desktop.Shutdown(); }
                    });
                });
            }
            else
            {
                SendPathsThroughPipe(myPaths, CompressSeparatePipeName);
                desktop.Shutdown();
            }
        }
        else
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try { await RunCompressSeparate(myPaths, desktop); }
                finally { desktop.Shutdown(); }
            });
        }
    }

    private static async Task RunCompressSeparate(
        List<string> paths,
        IClassicDesktopStyleApplicationLifetime desktop,
        ProgressWindow? existingWindow = null)
    {
        var settings = AppSettings.Load();

        var request = new CompressRequest
        {
            SourcePaths = paths,
            Mode = CompressOutputMode.Separate,
            Format = settings.DefaultFormat,
            CompressionLevel = settings.DefaultLevel,
            KeepOriginalExtension = false,
            PreserveDirectoryRoot = true,
        };

        // 标题用批处理专用标题（InitBatchMode 不再覆盖标题，由调用方传入）
        await CompressWithProgress(
            request,
            LocalizationManager.T("Progress_Batch_Title"),
            desktop,
            existingWindow);
    }

    // ════════════════════════════════════════════════════════════════
    //  --compress-combined : IPC + single combined archive
    // ════════════════════════════════════════════════════════════════

    private static void HandleCompressCombined(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0) { desktop.Shutdown(); return; }

        if (OperatingSystem.IsWindows())
        {
            bool firstInstance;
            var mutex = new Mutex(true, CompressCombinedMutexName, out firstInstance);

            if (firstInstance)
            {
                var allPaths = new List<string>(myPaths);
                var cts = new CancellationTokenSource();
                var pipeReady = new ManualResetEventSlim(false);
                StartPipeServer(allPaths, cts.Token, CompressCombinedPipeName, pipeReady);
                pipeReady.Wait(3000);

                _ = Task.Delay(800).ContinueWith(_ =>
                {
                    cts.Cancel();
                    mutex.Dispose();
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try { await RunCompressCombined(allPaths, desktop); }
                        finally { desktop.Shutdown(); }
                    });
                });
            }
            else
            {
                SendPathsThroughPipe(myPaths, CompressCombinedPipeName);
                desktop.Shutdown();
            }
        }
        else
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try { await RunCompressCombined(myPaths, desktop); }
                finally { desktop.Shutdown(); }
            });
        }
    }

    private static async Task RunCompressCombined(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var settings = AppSettings.Load();

        // Determine common parent for archive name
        var commonParent = FindCommonParent(paths);
        string parentDir;
        string archiveName;

        if (commonParent != null && !IsDriveRoot(commonParent))
        {
            parentDir = commonParent;
            archiveName = Path.GetFileName(commonParent.TrimEnd('\\', '/'));
        }
        else
        {
            // No common parent: use first file's directory
            var first = paths[0];
            parentDir = Path.GetDirectoryName(first.TrimEnd('\\', '/'))
                ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            archiveName = Path.GetFileNameWithoutExtension(first.TrimEnd('\\', '/'));
        }

        var ext = settings.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + settings.DefaultFormat;
        var outputPath = Path.Combine(parentDir, archiveName + ext);

        var request = new CompressRequest
        {
            SourcePaths = paths,
            Mode = CompressOutputMode.Combined,
            Format = settings.DefaultFormat,
            CompressionLevel = settings.DefaultLevel,
            OutputPath = outputPath,
            PreserveDirectoryRoot = true,
        };

        await CompressWithProgress(request, LocalizationManager.T("Cli_CombinedCompress"), desktop);
    }

    // ════════════════════════════════════════════════════════════════
    //  Shared: compress with ProgressWindow
    // ════════════════════════════════════════════════════════════════

    private static async Task CompressWithProgress(
        CompressRequest request,
        string title,
        IClassicDesktopStyleApplicationLifetime desktop,
        ProgressWindow? existingWindow = null)
    {
        var progressWindow = existingWindow ?? new ProgressWindow(title);
        if (existingWindow == null)
        {
            progressWindow.InitCancellation();
            progressWindow.Show();
        }
        desktop.MainWindow = progressWindow;

        // 始终显示文件列表（单文件也显示）：批处理列表项 = 输出路径
        var outputPaths = AvaloniaCompressService.GetOutputPaths(request);
        if (outputPaths is { Count: > 0 })
            progressWindow.InitBatchMode(outputPaths);

        var doneEvent = new ManualResetEventSlim(false);
        Exception? captureException = null;
        bool cancelled = false;
        bool partialFailure = false;

        _ = Task.Run(async () =>
        {
            try
            {
                var rawProgress = progressWindow.CreatePauseAwareProgress(
                    ProgressViewModel.CreateBackgroundProgress(
                        progressWindow, p => progressWindow.SetProgress(p)));

                var avCompress = new AvaloniaCompressService();
                var result = await avCompress.CompressAsync(
                    request,
                    rawProgress,
                    progressWindow.CancellationToken,
                    onItemStatus: (index, status) =>
                    {
                        // 逐项状态更新驱动批处理文件列表（对齐 WPF CompressAsync onItemStatus 接线）
                        progressWindow.SetCurrentBatchItem(index);
                        progressWindow.UpdateBatchItemStatus(index, status);
                    });
                progressWindow.FinalizeBatch();

                partialFailure = result.Failed > 0;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (partialFailure)
                    {
                        // 部分条目失败（如权限不足）：显示错误汇总（对齐 WPF result.Failed > 0 → CompleteWithErrors 分支）
                        progressWindow.SetErrorSummary(
                            $"{result.Failed} 个文件未能写入到 {request.OutputPath}（可能权限不足）");
                        progressWindow.CompleteWithErrors();
                    }
                    else
                    {
                        progressWindow.SetComplete(LocalizationManager.T("Cli_StatusDone"));
                    }
                });
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                captureException = ex;
                Console.Error.WriteLine($"Compression failed: {ex.Message}");
            }
            finally
            {
                doneEvent.Set();
            }
        });

        // Wait for completion (non-blocking via the ongoing main loop)
        await Task.Run(() => doneEvent.Wait());

        if (cancelled)
        {
            await Dispatcher.UIThread.InvokeAsync(() => progressWindow.Close());
            desktop.Shutdown();
            return;
        }

        if (captureException != null)
        {
            // 失败：标记批次项为 Failed（FinalizeBatch 已把 InProgress 置为 Completed，这里覆盖为失败）
            progressWindow.UpdateBatchItemStatus(0, BatchItemStatus.Failed, captureException.Message);
            // 显示错误汇总并等待用户手动关闭窗口（对齐 WPF 失败分支 CompleteWithErrors + wait Closed）
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                progressWindow.SetErrorSummary(captureException!.Message);
                progressWindow.CompleteWithErrors();
            });
            await WaitForWindowCloseAsync(progressWindow);
            desktop.Shutdown();
            return;
        }

        if (partialFailure)
        {
            // 部分失败：等待用户手动关闭窗口后退出
            await WaitForWindowCloseAsync(progressWindow);
            desktop.Shutdown();
            return;
        }

        // 成功：自动关闭（2.5s）或等待（📌 KeepOpenOnComplete 生效），窗口关闭后退出进程
        await progressWindow.AutoCloseOrWaitAsync(2500, () => desktop.Shutdown());
    }

    // ════════════════════════════════════════════════════════════════
    //  Path utilities
    // ════════════════════════════════════════════════════════════════

    internal static string? FindCommonParent(List<string> paths)
    {
        if (paths.Count == 0) return null;
        var parents = paths.Select(p =>
        {
            var trimmed = p.TrimEnd('\\', '/');
            return File.Exists(trimmed)
                ? Path.GetDirectoryName(trimmed) ?? ""
                : Path.GetDirectoryName(trimmed) ?? "";
        }).ToList();

        if (parents.Any(string.IsNullOrEmpty)) return null;

        var common = parents[0];
        for (int i = 1; i < parents.Count; i++)
        {
            while (!parents[i].StartsWith(common, StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(common);
                if (parent == null) return null;
                common = parent;
            }
        }
        return common;
    }

    internal static bool IsDriveRoot(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':';
    }
}
