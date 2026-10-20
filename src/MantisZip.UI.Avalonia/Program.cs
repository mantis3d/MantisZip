using Avalonia;

namespace MantisZip.UI.Avalonia;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 启动打点（startup-preview-defer 计划）：必须是 Main 第一行，捕获 pre-Main 基线后的第一个点
        Services.StartupTimer.Begin();

        // 原生 Win32 启动 Splash（startup-native-splash 计划）：Avalonia 未就绪期间的用户反馈。
        // 非 UI 命令（秒退）不显示；其余（含无参普通启动 + 全部 CLI 慢路径）显示。
        // 文案按 AppSettings 语言 + 启动方式选择；主题色据 Theme（System 视为深色）。
        if (ShouldShowSplash(args))
        {
            var splashSettings = Models.AppSettings.Load(); // 独立于 Avalonia 的纯 .NET 单例，安全使用
            Services.LocalizationManager.CurrentLanguage =
                Services.LocalizationManager.FromSettingsCode(splashSettings.Language);
            Services.NativeSplash.Show(Services.LocalizationManager.T(SplashTextKey(args)),
                splashSettings.Theme != "Light");
        }

        var builder = BuildAvaloniaApp();
        Services.StartupTimer.Mark("AppBuilder.Ready"); // UsePlatformDetect 等平台探测完成
        // StartWithClassicDesktopLifetime 阻塞至退出；后续点位在 App/MainWindow 内打
        builder.StartWithClassicDesktopLifetime(args);
        // CLI 慢路径兜底 flush（主路径已在首帧 flush，此处幂等 no-op）
        Services.StartupTimer.Flush("app-exit");
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>
    /// 非 UI 命令（--install-shell / --uninstall-shell / --install-assoc / --uninstall-assoc / --help / -h / --test
    /// 等秒退命令）不显示 splash——无窗口创建，splash 会悬置无法关闭（计划文档「边界条件」）。
    /// </summary>
    private static bool ShouldShowSplash(string[] args)
    {
        if (args.Length == 0) return true; // 无参普通启动
        return args[0].ToLowerInvariant() switch
        {
            "--install-shell" or "--uninstall-shell" or
            "--install-assoc" or "--uninstall-assoc" or
            "--help" or "-h" or "--test" => false,
            _ => true,
        };
    }

    /// <summary>
    /// Splash 文案 key 按启动方式选择（计划文档「splash 文案选择」；全部为现有 key，
    /// 除无参启动的 Status_Starting 为本期新增三语 key）。
    /// </summary>
    private static string SplashTextKey(string[] args)
    {
        if (args.Length == 0) return "Status_Starting";
        return args[0].ToLowerInvariant() switch
        {
            "--compress" or "--compress-separate" or "--compress-combined" => "App_CompressCollecting",
            "--compress-quick" => "Progress_Title_Compress",
            var c when c.StartsWith("--extract") => "Progress_Title_Extract",
            "--open" or "--open-dispatch" => "Status_OpeningArchive",
            _ => "Status_Starting",
        };
    }
}
