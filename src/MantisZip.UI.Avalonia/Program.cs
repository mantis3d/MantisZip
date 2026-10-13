using Avalonia;

namespace MantisZip.UI.Avalonia;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 启动打点（startup-preview-defer 计划）：必须是 Main 第一行，捕获 pre-Main 基线后的第一个点
        Services.StartupTimer.Begin();
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
}
