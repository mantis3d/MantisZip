using Avalonia;
using MantisZip.UI.Avalonia.Services;

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
        // 文案 key 按 AppSettings 语言 + 启动方式选择（SplashTextSpec 纯函数分发，格式化在此处统一 T()）；
        // 主题色据 Theme（System 视为深色）。
        if (ShouldShowSplash(args))
        {
            var splashSettings = Models.AppSettings.Load(); // 独立于 Avalonia 的纯 .NET 单例，安全使用
            Services.LocalizationManager.CurrentLanguage =
                Services.LocalizationManager.FromSettingsCode(splashSettings.Language);
            var spec = SplashTextSpec(args);
            Services.NativeSplash.Show(Services.LocalizationManager.T(spec.Key, spec.Args),
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
    /// Splash 文案 spec 按启动方式选择（计划文档「splash 文案选择」；除新增 Fmt key 外全部为现有 key）：
    /// 单文件场景带文件名、多文件场景带参数——全部为显示期一次性静态字符串，无中途更新需求。
    /// 纯函数返回 (key, 插值参数)，格式化由调用处 LocalizationManager.T 统一完成（便于测试与语言解耦）。
    /// </summary>
    internal static (string Key, object?[] Args) SplashTextSpec(string[] args)
    {
        if (args.Length == 0) return ("Status_Starting", []);
        var cmd = args[0].ToLowerInvariant();
        var paths = args.Skip(1).Where(p => !string.IsNullOrEmpty(p) && File.Exists(p)).ToArray();

        switch (cmd)
        {
            case "--open":
            case "--open-dispatch":
                // 单文件带文件名；文件无效回退静态
                if (paths.Length == 1)
                    return ("Status_OpeningArchiveFmt", [Path.GetFileName(paths[0])]);
                return ("Status_OpeningArchive", []);

            case "--extract":
            case "--extract-here":
            case "--extract-to-name":
            case "--extract-smart":
                if (paths.Length == 1)
                    return ("Status_ExtractingFmt", [Path.GetFileName(paths[0])]);
                return ("Status_ExtractingBatchFmt", [paths.Length]);

            case "--compress-quick":
                if (paths.Length > 1)
                    return ("Status_CompressingBatchFmt", [paths.Length]);
                return ("Progress_Title_Compress", []); // 单文件不带文件名（用户拍板忽略）

            case "--compress":
            case "--compress-separate":
            case "--compress-combined":
                return ("App_CompressCollecting", []); // IPC 收集期总数未知，静态最诚实

            default:
                return ("Status_Starting", []);
        }
    }
}
