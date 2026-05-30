using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MantisZip", "debug.log");
    // StartupLog 已合并到 LogFile（同一个文件）
    private static string StartupLog => LogFile;
    private static Mutex? _instanceMutex;

    #region 全局初始化

    /// <summary>
    /// 全局初始化：所有入口（主窗口、--compress、--extract 等）都先经过这里。
    /// 用于设置编码、全局静态变量等。
    /// </summary>
    private static void InitializeApp()
    {
        // 注册编码提供程序，支持 GBK 等非系统预装编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // 不再全局设置 ZipStrings.CodePage。
        // ZipEngine 会按压缩包检测 UTF-8 标记，自动选择合适的编码。

        // 从用户设置加载 7z.dll 路径，覆盖 SevenZipEngine 的默认值
        try
        {
            var s = AppSettings.Instance;
            if (!string.IsNullOrEmpty(s.SevenZipPath))
            {
                SevenZipEngine.SevenZipDllPath = s.SevenZipPath;
                LogDebug("InitializeApp: SevenZipDllPath set to {0}", s.SevenZipPath);
            }
        }
        catch (Exception initEx)
        {
            TraceLog("InitializeApp: failed to load AppSettings: {0}", initEx.Message);
        }

        // 注册 7z.dll 解析回调 — 默认位置找不到时弹出对话框让用户手动指定
        SevenZipEngine.SevenZipDllResolveCallback = () =>
        {
            var disp = Current?.Dispatcher;
            if (disp == null || disp.CheckAccess())
                return ShowSevenZipDllDialog();
            return disp.Invoke(ShowSevenZipDllDialog);
        };
    }

    /// <summary>
    /// 弹出文件选择对话框让用户手动选择 7z.dll 路径。
    /// 选择后保存到 AppSettings，下次启动自动使用。
    /// </summary>
    private static string? ShowSevenZipDllDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "未找到 7z.dll - 请选择 7z.dll 文件",
            Filter = "7z.dll|7z.dll|动态链接库 (*.dll)|*.dll|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            // 默认指向应用目录下的 x64/x86 子目录
            InitialDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.Is64BitProcess ? "x64" : "x86"),
        };

        if (dialog.ShowDialog() == true)
        {
            var path = dialog.FileName;
            try
            {
                var settings = AppSettings.Instance;
                settings.SevenZipPath = path;
                settings.Save();
                LogDebug("ShowSevenZipDllDialog: saved 7z.dll path to settings: {0}", path);
            }
            catch (Exception ex)
            {
                TraceLog("ShowSevenZipDllDialog: failed to save settings: {0}", ex.Message);
            }
            return path;
        }

        return null;
    }

    #endregion

    #region OnStartup

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局初始化（所有入口最先运行）
        InitializeApp();

        // 初始化语言管理器（需在 InitializeApp 之后，因为初始化加载 AppSettings）
        LanguageManager.Instance.Initialize();

        // 应用已保存的主题设置
        ApplyTheme(AppSettings.Instance.Theme);

        // 初始化 CoreLog 日志脱敏委托。此后所有 CoreLog 写入自动脱敏。
        CoreLog.RedactOverride = msg =>
            LogRedactor.RedactPaths(msg, LogRedactor.ParseMode(AppSettings.Instance.LogPrivacyMode));

        // CoreLog 的 DEBUG 日志受“启用调试日志”设置控制
        CoreLog.ShouldWriteOverride = () => AppSettings.Instance.EnableDebugLogging;

        // ===== 统一日志 =====
        // 所有日志（启动日志、调试日志、CoreLog 追踪日志）都写入同一个文件：
        // %LOCALAPPDATA%\MantisZip\debug.log
        // 该文件持久化保留，不被自动删除。
        LogStartup($"START BaseDir={AppDomain.CurrentDomain.BaseDirectory} Args=[{string.Join(" ", e.Args)}]");

        LogDebug("LogDebug: debug.log will be appended");

        LogStartup($"启动参数: {string.Join(" ", e.Args)}");
        TraceLog("OnStartup: after args log");

        try
        {
            if (e.Args.Length > 0)
            {
                switch (e.Args[0])
                {
                    case "--install-shell":
                        ShellIntegration.Install();
                        AppMessageBox.Show(L.T(L.App_ShellInstalled),
                            L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--uninstall-shell":
                        ShellIntegration.Uninstall();
                        AppMessageBox.Show(L.T(L.App_ShellUninstalled), L.T(L.App_MantisZipTitle),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--install-assoc":
                        ShellIntegration.InstallAssociations();
                        AppMessageBox.Show(L.T(L.App_AssocInstalled),
                            L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--uninstall-assoc":
                        ShellIntegration.UninstallAssociations();
                        AppMessageBox.Show(L.T(L.Settings_Assoc_UninstalledMsg), L.T(L.App_MantisZipTitle),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--compress":
                        HandleCompress(e.Args.Skip(1).ToArray());
                        return; // HandleCompress 内部会 Shutdown 或继续运行

                    case "--extract":
                        HandleExtract(e.Args.Skip(1).Where(a => !string.IsNullOrEmpty(a)).ToArray());
                        return;

                    case "--extract-here":
                        HandleExtractHere(e.Args.Skip(1).Where(a => !string.IsNullOrEmpty(a)).ToArray());
                        return;

                    case "--extract-to-name":
                        HandleExtractToNamed(e.Args.Skip(1).Where(a => !string.IsNullOrEmpty(a)).ToArray());
                        return;

                    case "--extract-smart":
                        HandleExtractSmart(e.Args.Skip(1).Where(a => !string.IsNullOrEmpty(a)).ToArray());
                        return;

                    case "--compress-quick":
                        HandleCompressQuick(e.Args.Skip(1).ToArray());
                        return;

                    case "--compress-separate":
                        HandleCompressSeparate(e.Args.Skip(1).ToArray());
                        return;

                    case "--compress-combined":
                        HandleCompressCombined(e.Args.Skip(1).ToArray());
                        return;

                    case "--test":
                        LogStartup("--test 模式：启动测试成功");
                        AppMessageBox.Show(
                            L.TF(L.App_StartupSuccess, AppDomain.CurrentDomain.BaseDirectory, StartupLog),
                            L.T(L.App_StartupTestTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--open":
                        HandleOpen(e.Args.Length > 1 ? e.Args[1] : null);
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            Log("OnStartup 异常: {0}\n{1}", ex.Message, ex.StackTrace ?? "");
            AppMessageBox.Show(L.TF(L.App_StartupFailed, ex.Message), L.T(L.App_StartupErrorTitle),
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // 正常启动：手动创建主窗口（已移除 StartupUri）
        TraceLog("OnStartup: creating MainWindow");
        var mainWin = new MainWindow();
        TraceLog("OnStartup: MainWindow created, showing");
        mainWin.Show();
        TraceLog("OnStartup: MainWindow shown");
        base.OnStartup(e);
        Log("程序启动");
        TraceLog("OnStartup: startup complete");
    }

    #endregion

    #region 选项创建

    /// <summary>
    /// 创建解压选项，包含冲突处理设置和 Ask 弹窗回调。
    /// 回调会在后台线程调用，使用 Dispatcher 调度到 UI 线程显示对话框。
    /// </summary>
    internal static ArchiveOptions CreateExtractOptions()
    {
        bool applyToAll = false;
        FileConflictAction? chosenAction = null;

        return new ArchiveOptions
        {
            ConflictAction = GetConflictActionFromSettings(),
            ConflictResolver = info =>
            {
                // 已勾选"应用到全部" → 直接返回记忆的选择
                if (applyToAll && chosenAction.HasValue)
                    return chosenAction.Value;

                // 调度到 UI 线程显示模态对话框
                var dispatcher = Current?.Dispatcher;
                if (dispatcher == null) return FileConflictAction.Overwrite;

                var result = dispatcher.Invoke(() =>
                {
                    var dialog = new ConflictDialog(info);
                    dialog.ShowDialog();
                    info.CustomName = dialog.CustomName;
                    return (Action: dialog.ResultAction, All: dialog.ApplyToAll);
                });

                if (result.All)
                {
                    applyToAll = true;
                    chosenAction = result.Action;
                }

                return result.Action;
            }
        };
    }

    /// <summary>
    /// 创建压缩选项，包含文件读取错误的处理回调。
    /// </summary>
    internal static ArchiveOptions CreateCompressOptions()
    {
        bool applyToAll = false;
        FileErrorAction? chosenAction = null;

        return new ArchiveOptions
        {
            PreserveDirectoryRoot = AppSettings.Instance.PreserveDirectoryRoot,
            ErrorResolver = info =>
            {
                if (applyToAll && chosenAction.HasValue)
                    return chosenAction.Value;

                var dispatcher = Current?.Dispatcher;
                if (dispatcher == null) return FileErrorAction.Abort;

                var result = dispatcher.Invoke(() =>
                {
                    var dialog = new ErrorDialog(info);
                    dialog.ShowDialog();
                    return (Action: dialog.ResultAction, All: dialog.ApplyToAll);
                });

                if (result.All)
                {
                    applyToAll = true;
                    chosenAction = result.Action;
                }

                return result.Action;
            }
        };
    }

    /// <summary>
    /// 自动生成唯一的文件名：重复时加 (1),(2)... 后缀。
    /// 正确处理 .tar.gz 等双扩展名。
    /// </summary>
    internal static string GetUniquePath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        string name, ext;

        // 特别处理 .tar.gz 双扩展名
        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            // "C:\path\archive.tar.gz" → name="archive", ext=".tar.gz"
            var withoutExt = path[..^7]; // 去掉 .tar.gz
            name = Path.GetFileName(withoutExt);
            ext = ".tar.gz";
        }
        else
        {
            name = Path.GetFileNameWithoutExtension(path);
            ext = Path.GetExtension(path);
        }

        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
            {
                LogDebug("GetUniquePath: {0} → {1}", path, candidate);
                return candidate;
            }
        }
        LogDebug("GetUniquePath: {0} → {0} (all names taken, fallback)", path);
        return path; // 999 个名字全被占用了，直接覆盖原文件
    }

    internal static FileConflictAction GetConflictActionFromSettings()
    {
        return AppSettings.Instance.FileConflictAction switch
        {
            "overwrite" => FileConflictAction.Overwrite,
            "rename" => FileConflictAction.Rename,
            "skip" => FileConflictAction.Skip,
            "ask" => FileConflictAction.Ask,
            "overwrite-if-older" => FileConflictAction.OverwriteIfOlder,
            "overwrite-if-smaller" => FileConflictAction.OverwriteIfSmaller,
            _ => FileConflictAction.Overwrite
        };
    }

    #endregion

    #region 解压目标 / 工具方法

    /// <summary>
    /// 根据设置或用户选择返回解压目标目录。返回 null 表示用户取消。
    /// </summary>
    internal static string? ResolveExtractDestinationStatic(string archivePath, AppSettings settings)
    {
        var destSetting = settings.ExtractDestination;

        if (destSetting == "same-dir")
        {
            var dir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        else if (destSetting == "desktop")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        // "ask" 或未知值 → 弹出选择对话框
        var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "选择解压目录"
        };
        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void OpenInExplorerStatic(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start("explorer.exe", path);
        }
        catch (Exception explorerEx) { TraceLog("OpenInExplorerStatic: failed for '{0}': {1}", path, explorerEx.Message); }
    }

    #endregion

    #region OnExit

    protected override void OnExit(ExitEventArgs e)
    {
        // 清理预览临时文件
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle));
            if (Directory.Exists(tempDir))
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        Directory.Delete(tempDir, recursive: true);
                        Log(L.T(L.App_CleanedPreviewTemp));
                        break;
                    }
                    catch (Exception) when (i < 4)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        Thread.Sleep(200);
                    }
                }
            }
        }
        catch (Exception exitEx) { LogDebug("OnExit: temp cleanup failed: {0}", exitEx.Message); }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    #endregion

    #region 主题

    /// <summary>
    /// 应用亮色/暗色主题。替换 Application 级别的主题 ResourceDictionary，
    /// 并直接设置 SystemColors 覆盖（WPF XAML 中的 x:Static 键有时与控件模板查找不匹配）。
    /// </summary>
    public static void ApplyTheme(string themeName)
    {
        var uri = new Uri($"Themes/{themeName}.xaml", UriKind.Relative);
        var newDict = new ResourceDictionary { Source = uri };

        var existing = Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString?.StartsWith("Themes/") == true);

        if (existing != null)
            Current.Resources.MergedDictionaries.Remove(existing);

        Current.Resources.MergedDictionaries.Add(newDict);

        // SystemColors 覆盖（用 C# 保证 key 对象精准匹配）
        bool isDark = themeName == "Dark";
        var res = Current.Resources;
        if (isDark)
        {
            res[SystemColors.ControlBrushKey]              = new SolidColorBrush(Gray(0x2D));
            res[SystemColors.ControlTextBrushKey]          = new SolidColorBrush(Gray(0xD4));
            res[SystemColors.WindowBrushKey]               = new SolidColorBrush(Gray(0x1E));
            res[SystemColors.WindowTextBrushKey]           = new SolidColorBrush(Gray(0xD4));
            res[SystemColors.MenuBrushKey]                 = new SolidColorBrush(Gray(0x2D));
            res[SystemColors.MenuTextBrushKey]             = new SolidColorBrush(Gray(0xD4));
            res[SystemColors.MenuBarBrushKey]              = new SolidColorBrush(Gray(0x2D));
            res[SystemColors.HighlightBrushKey]            = new SolidColorBrush(Rgb(0x4F, 0xC3, 0xF7));
            res[SystemColors.HighlightTextBrushKey]        = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.InactiveSelectionHighlightBrushKey]  = new SolidColorBrush(Rgb(0x26, 0x4F, 0x78));
            res[SystemColors.InactiveSelectionHighlightTextBrushKey] = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.GrayTextBrushKey]             = new SolidColorBrush(Gray(0x6E));
            res[SystemColors.ActiveBorderBrushKey]         = new SolidColorBrush(Gray(0x3E));
            res[SystemColors.ControlLightBrushKey]         = new SolidColorBrush(Gray(0x3E));
            res[SystemColors.ControlLightLightBrushKey]    = new SolidColorBrush(Gray(0x4A));
            res[SystemColors.ControlDarkBrushKey]          = new SolidColorBrush(Gray(0x25));
            res[SystemColors.ControlDarkDarkBrushKey]      = new SolidColorBrush(Gray(0x1A));
            res[SystemColors.AppWorkspaceBrushKey]         = new SolidColorBrush(Gray(0x1E));
            res[SystemColors.InfoBrushKey]                 = new SolidColorBrush(Gray(0x3E));
            res[SystemColors.InfoTextBrushKey]             = new SolidColorBrush(Gray(0xD4));
            res[SystemColors.ScrollBarBrushKey]            = new SolidColorBrush(Gray(0x2D));
            // ColorKey 变体（有的控件模板用 Color 而非 Brush）
            res[SystemColors.WindowColorKey]               = Rgb(0x1E, 0x1E, 0x1E);
            res[SystemColors.ControlColorKey]              = Gray(0x2D);
            res[SystemColors.ControlTextColorKey]          = Gray(0xD4);
            res[SystemColors.WindowTextColorKey]           = Gray(0xD4);
            res[SystemColors.GrayTextColorKey]             = Gray(0x6E);
            res[SystemColors.MenuColorKey]                 = Gray(0x2D);
            res[SystemColors.MenuTextColorKey]             = Gray(0xD4);
            res[SystemColors.MenuBarColorKey]              = Gray(0x2D);
            res[SystemColors.HighlightColorKey]            = Rgb(0x4F, 0xC3, 0xF7);
            res[SystemColors.HighlightTextColorKey]        = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.ActiveBorderColorKey]         = Gray(0x3E);
            res[SystemColors.ControlLightColorKey]         = Gray(0x3E);
            res[SystemColors.ControlLightLightColorKey]    = Gray(0x4A);
            res[SystemColors.ControlDarkColorKey]          = Gray(0x25);
            res[SystemColors.ControlDarkDarkColorKey]      = Gray(0x1A);
            res[SystemColors.AppWorkspaceColorKey]         = Gray(0x1E);
            res[SystemColors.InfoColorKey]                 = Gray(0x3E);
            res[SystemColors.InfoTextColorKey]             = Gray(0xD4);
            res[SystemColors.ScrollBarColorKey]            = Gray(0x2D);
        }
        else
        {
            res[SystemColors.ControlBrushKey]              = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.ControlTextBrushKey]          = new SolidColorBrush(Gray(0x33));
            res[SystemColors.WindowBrushKey]               = new SolidColorBrush(Rgb(0xF5, 0xF5, 0xF5));
            res[SystemColors.WindowTextBrushKey]           = new SolidColorBrush(Gray(0x33));
            res[SystemColors.MenuBrushKey]                 = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.MenuTextBrushKey]             = new SolidColorBrush(Gray(0x33));
            res[SystemColors.MenuBarBrushKey]              = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.HighlightBrushKey]            = new SolidColorBrush(Rgb(0x21, 0x96, 0xF3));
            res[SystemColors.HighlightTextBrushKey]        = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.InactiveSelectionHighlightBrushKey]  = new SolidColorBrush(Rgb(0xCC, 0xE0, 0xF0));
            res[SystemColors.InactiveSelectionHighlightTextBrushKey] = new SolidColorBrush(Rgb(0x00, 0x00, 0x00));
            res[SystemColors.GrayTextBrushKey]             = new SolidColorBrush(Gray(0x88));
            res[SystemColors.ActiveBorderBrushKey]         = new SolidColorBrush(Rgb(0xCC, 0xCC, 0xCC));
            res[SystemColors.ControlLightBrushKey]         = new SolidColorBrush(Rgb(0xE0, 0xE0, 0xE0));
            res[SystemColors.ControlLightLightBrushKey]    = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.ControlDarkBrushKey]          = new SolidColorBrush(Rgb(0xC0, 0xC0, 0xC0));
            res[SystemColors.ControlDarkDarkBrushKey]      = new SolidColorBrush(Gray(0x80));
            res[SystemColors.AppWorkspaceBrushKey]         = new SolidColorBrush(Rgb(0xF5, 0xF5, 0xF5));
            res[SystemColors.InfoBrushKey]                 = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.InfoTextBrushKey]             = new SolidColorBrush(Gray(0x33));
            res[SystemColors.ScrollBarBrushKey]            = new SolidColorBrush(Rgb(0xF0, 0xF0, 0xF0));
            // ColorKey 变体（有的控件模板用 Color 而非 Brush）
            res[SystemColors.WindowColorKey]               = Rgb(0xF5, 0xF5, 0xF5);
            res[SystemColors.ControlColorKey]              = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.ControlTextColorKey]          = Gray(0x33);
            res[SystemColors.WindowTextColorKey]           = Gray(0x33);
            res[SystemColors.GrayTextColorKey]             = Gray(0x88);
            res[SystemColors.MenuColorKey]                 = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.MenuTextColorKey]             = Gray(0x33);
            res[SystemColors.MenuBarColorKey]              = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.HighlightColorKey]            = Rgb(0x21, 0x96, 0xF3);
            res[SystemColors.HighlightTextColorKey]        = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.ActiveBorderColorKey]         = Rgb(0xCC, 0xCC, 0xCC);
            res[SystemColors.ControlLightColorKey]         = Rgb(0xE0, 0xE0, 0xE0);
            res[SystemColors.ControlLightLightColorKey]    = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.ControlDarkColorKey]          = Rgb(0xC0, 0xC0, 0xC0);
            res[SystemColors.ControlDarkDarkColorKey]      = Gray(0x80);
            res[SystemColors.AppWorkspaceColorKey]         = Rgb(0xF5, 0xF5, 0xF5);
            res[SystemColors.InfoColorKey]                 = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.InfoTextColorKey]             = Gray(0x33);
            res[SystemColors.ScrollBarColorKey]            = Rgb(0xF0, 0xF0, 0xF0);
        }
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color Gray(byte g) => Color.FromRgb(g, g, g);

    /// <summary>
    /// 在指定元素上应用彩色 Emoji 渲染模式（Grayscale vs ClearType）。
    /// 根据 AppSettings.UseColorEmoji 切换。
    /// </summary>
    public static void ApplyTextRenderingMode(DependencyObject element)
    {
        if (element == null) return;
        TextOptions.SetTextRenderingMode(element,
            AppSettings.Instance.UseColorEmoji
                ? TextRenderingMode.Grayscale
                : TextRenderingMode.ClearType);
    }

    #endregion

    #region 嵌套类型

    /// <summary>
    /// 简单输入框对话框，用于 --compress-combined 无公共父目录时让用户输入压缩包名称。
    /// </summary>
    internal class ArchiveNameDialog : Window
    {
        public string? ArchiveName { get; private set; }
        private readonly TextBox _textBox;

        public ArchiveNameDialog(string defaultName)
        {
            Title = L.T(L.App_CompressCombinedPromptTitle);
            Width = 400; Height = 160;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;

            var stack = new StackPanel { Margin = new Thickness(12) };

            var label = new TextBlock
            {
                Text = L.T(L.App_CompressCombinedPromptLabel),
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(label);

            _textBox = new TextBox
            {
                Text = defaultName,
                Margin = new Thickness(0, 0, 0, 12)
            };
            _textBox.SelectAll();
            _textBox.Focus();
            stack.Children.Add(_textBox);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okBtn = new Button
            {
                Content = L.T(L.MsgBox_Ok),
                Width = 80, Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okBtn.Click += (_, _) =>
            {
                ArchiveName = _textBox.Text.Trim();
                if (!string.IsNullOrEmpty(ArchiveName))
                    DialogResult = true;
            };
            btnPanel.Children.Add(okBtn);

            var cancelBtn = new Button
            {
                Content = L.T(L.MsgBox_Cancel),
                Width = 80, Height = 28,
                IsCancel = true
            };
            btnPanel.Children.Add(cancelBtn);

            stack.Children.Add(btnPanel);
            Content = stack;
        }
    }

    #endregion
}
