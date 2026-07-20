using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

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
        // ── Apply system theme ──
        ApplySystemTheme();
        if (PlatformSettings is IPlatformSettings ps)
        {
            ps.ColorValuesChanged += (_, _) =>
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplySystemTheme());
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

    private void ApplySystemTheme()
    {
        if (PlatformSettings is not IPlatformSettings ps) return;
        try
        {
            var isDark = ps.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
            DebugLog($"[Theme] ApplySystemTheme dark={isDark}");

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
            DebugLog($"[Theme] ApplySystemTheme ERROR: {ex.Message}");
        }
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

    internal static void DebugLog(string msg)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MantisZip", "debug.log");
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    // ════════════════════════════════════════════════════════════════
    //  Extract helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 带提权支持的异步解压。先检查目标目录可写性，权限不足时弹出提权对话框。
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

            await engine.ExtractAsync(archivePath, targetDir);
            Console.WriteLine($"Extracted: {archivePath} -> {targetDir}");
            return false;
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

            await engine.ExtractAsync(archivePath, targetDir);
            Console.WriteLine($"Extracted: {archivePath} -> {targetDir}");
            return false;
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

    // ════════════════════════════════════════════════════════════════
    //  Shell commands (--install-shell, --uninstall-shell, etc.)
    // ════════════════════════════════════════════════════════════════

    private static void HandleShellCommand(string command, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.Error.WriteLine("Shell integration is only supported on Windows.");
            desktop.Shutdown();
            return;
        }

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
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLog($"HandleShellCommand: {command} failed: {ex.Message}");
            Console.Error.WriteLine($"Failed to execute {command}: {ex.Message}");
        }

        desktop.Shutdown();
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
                        finally { desktop.Shutdown(); }
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
                finally { desktop.Shutdown(); }
            });
        }
    }

    private static async Task ShowCompressDialogAndRun(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var dlg = new CompressSettingsWindow(paths);

        // Intercept CloseAction: on "Compress", run the compression
        dlg.ViewModel.CloseAction = async (result) =>
        {
            dlg.Close();
            if (result)
            {
                var vm = dlg.ViewModel;
                var request = new CompressRequest
                {
                    SourcePaths = paths.ToList(),
                    Mode = CompressOutputMode.Manual,
                    Format = vm.DefaultFormat,
                    CompressionLevel = vm.CompressionLevel,
                    Password = vm.Password,
                    Encrypt = vm.Encrypt,
                    Comment = vm.Comment,
                    CommentDistribution = vm.CommentDistribution,
                    OutputPath = vm.OutputPath,
                    PreserveDirectoryRoot = true,
                };

                await CompressWithProgress(request, "压缩", desktop);
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

        _ = CompressWithProgress(request, "快速压缩", desktop);
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
                StartPipeServer(allPaths, cts.Token, CompressSeparatePipeName, pipeReady);
                pipeReady.Wait(3000);

                _ = Task.Delay(800).ContinueWith(_ =>
                {
                    cts.Cancel();
                    mutex.Dispose();
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try { await RunCompressSeparate(allPaths, desktop); }
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

    private static async Task RunCompressSeparate(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
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

        await CompressWithProgress(request, "批量压缩", desktop);
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

        await CompressWithProgress(request, "合并压缩", desktop);
    }

    // ════════════════════════════════════════════════════════════════
    //  Shared: compress with ProgressWindow
    // ════════════════════════════════════════════════════════════════

    private static async Task CompressWithProgress(CompressRequest request, string title, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var progressWindow = new ProgressWindow(title);
        progressWindow.InitCancellation();
        progressWindow.Show();
        desktop.MainWindow = progressWindow;

        var doneEvent = new ManualResetEventSlim(false);
        Exception? captureException = null;

        _ = Task.Run(async () =>
        {
            try
            {
                var rawProgress = ProgressViewModel.CreateBackgroundProgress(
                    progressWindow, p => progressWindow.SetProgress(p));

                var avCompress = new AvaloniaCompressService();
                var result = await avCompress.CompressAsync(request, rawProgress, progressWindow.CancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    progressWindow.SetStatus(result.Failed > 0 ? "失败" : "完成");
                    progressWindow.SetProgress(new ArchiveProgress { PercentComplete = 100 });
                });

                // Small delay so user can see completion
                await Task.Delay(1500);
            }
            catch (OperationCanceledException) { }
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

        progressWindow.Close();

        if (captureException != null)
            Console.Error.WriteLine($"Compression error: {captureException.Message}");

        desktop.Shutdown();
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
