using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Skia;

[assembly: AvaloniaTestApplication(typeof(MantisZip.UI.Avalonia.Tests.TestAppBuilder))]

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// Avalonia 无头测试平台引导。
/// UseHeadlessDrawing=false + UseSkia：走真实 Skia 渲染后端而非 Headless stub，
/// 使 Bitmap 解码返回真实像素尺寸（stub 下 LoadBitmap 恒为 1×1、Save 为 no-op），
/// 供图片解码尺寸等需要真实解码行为的测试使用。
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
