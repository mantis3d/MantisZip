using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(MantisZip.UI.Avalonia.Tests.TestAppBuilder))]

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// Avalonia 无头测试平台引导。仅初始化基础 Application + Headless 渲染，
/// 供需要 Avalonia 类型（如 Bitmap 解码）的 ViewModel 测试使用。
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
