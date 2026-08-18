using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

public class PreviewViewModelTests
{
    private const string MinimalSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\">" +
        "<rect width=\"100\" height=\"50\" fill=\"red\"/></svg>";

    /// <summary>
    /// 回归测试：ShowSvg 成功后必须设置 PreviewType = Svg。
    /// 加载遮罩（IsLoadingPreview）只在 PreviewType 变为非 None 时关闭；
    /// 缺失该赋值会导致 SVG 预览永远停留在加载状态（曾为真实 bug）。
    /// </summary>
    [AvaloniaFact]
    public void ShowSvg_AfterShowLoading_SetsPreviewTypeAndDismissesLoading()
    {
        var vm = new PreviewViewModel();
        var svgPath = Path.Combine(Path.GetTempPath(), $"mantiszip_svg_test_{Guid.NewGuid():N}.svg");
        File.WriteAllText(svgPath, MinimalSvg);
        try
        {
            vm.ShowLoading("test.svg");
            Assert.Equal(PreviewType.None, vm.PreviewType);
            Assert.True(vm.IsLoadingPreview);

            vm.ShowSvg(svgPath);

            Assert.Equal(PreviewType.Svg, vm.PreviewType);
            Assert.False(vm.IsLoadingPreview);
            Assert.True(vm.IsSvgVisible);
            Assert.NotNull(vm.PreviewImage);
        }
        finally
        {
            File.Delete(svgPath);
        }
    }

    /// <summary>
    /// 回归测试：小图（宽度 ≤ 1920）预览必须保持原生分辨率，禁止被放大。
    /// DecodeToWidth 会无条件把位图缩放到目标宽度，曾导致 200×150 的小图被放大成 1920×1440。
    /// </summary>
    [AvaloniaFact]
    public void ShowImage_SmallImage_KeepsNativeResolution()
    {
        var pngPath = CreateTestPng(200, 150);
        try
        {
            var vm = new PreviewViewModel();
            vm.ShowImage(pngPath);

            Assert.Equal(PreviewType.Image, vm.PreviewType);
            Assert.NotNull(vm.PreviewImage);
            Assert.Equal(200, vm.PreviewImage.PixelSize.Width);
            Assert.Equal(150, vm.PreviewImage.PixelSize.Height);
            Assert.Equal(200, vm.ImageWidth);
            Assert.Equal(150, vm.ImageHeight);
        }
        finally
        {
            File.Delete(pngPath);
        }
    }

    /// <summary>
    /// 回归测试：大图（宽度 > 1920）预览时降采样到 1920 宽，
    /// 避免解码超大位图（如 30000×20000）的巨额内存开销。
    /// </summary>
    [AvaloniaFact]
    public void ShowImage_LargeImage_DownscalesTo1920()
    {
        var pngPath = CreateTestPng(3000, 2000);
        try
        {
            var vm = new PreviewViewModel();
            vm.ShowImage(pngPath);

            Assert.Equal(PreviewType.Image, vm.PreviewType);
            Assert.NotNull(vm.PreviewImage);
            Assert.Equal(1920, vm.PreviewImage.PixelSize.Width);
            Assert.Equal(1280, vm.PreviewImage.PixelSize.Height);
        }
        finally
        {
            File.Delete(pngPath);
        }
    }

    /// <summary>用 SkiaSharp 生成纯色 PNG 测试图。</summary>
    private static string CreateTestPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(Path.GetTempPath(), $"mantiszip_img_test_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    /// <summary>
    /// 能力注册表：Image=缩放+透明+压平；AnimatedImage=缩放+透明+动画控制（无压平，方案 A 决策）；
    /// Svg=透明+压平；IcoGallery=仅透明；未注册类型=None。
    /// </summary>
    [Fact]
    public void PreviewCapabilities_Registry_DeclaresExpectedFlags()
    {
        Assert.True(PreviewCapabilities.For(PreviewType.Image).HasFlag(PreviewCapability.Zoom));
        Assert.True(PreviewCapabilities.For(PreviewType.Image).HasFlag(PreviewCapability.Transparency));
        Assert.True(PreviewCapabilities.For(PreviewType.Image).HasFlag(PreviewCapability.FlattenAlpha));
        Assert.False(PreviewCapabilities.For(PreviewType.Image).HasFlag(PreviewCapability.AnimationControls));

        Assert.True(PreviewCapabilities.For(PreviewType.AnimatedImage).HasFlag(PreviewCapability.Zoom));
        Assert.True(PreviewCapabilities.For(PreviewType.AnimatedImage).HasFlag(PreviewCapability.Transparency));
        Assert.True(PreviewCapabilities.For(PreviewType.AnimatedImage).HasFlag(PreviewCapability.AnimationControls));
        Assert.False(PreviewCapabilities.For(PreviewType.AnimatedImage).HasFlag(PreviewCapability.FlattenAlpha));

        Assert.True(PreviewCapabilities.For(PreviewType.Svg).HasFlag(PreviewCapability.Transparency));
        Assert.True(PreviewCapabilities.For(PreviewType.Svg).HasFlag(PreviewCapability.FlattenAlpha));

        Assert.True(PreviewCapabilities.For(PreviewType.IcoGallery).HasFlag(PreviewCapability.Transparency));
        Assert.False(PreviewCapabilities.For(PreviewType.IcoGallery).HasFlag(PreviewCapability.FlattenAlpha));

        Assert.Equal(PreviewCapability.None, PreviewCapabilities.For(PreviewType.Text));
    }

    /// <summary>
    /// GIF 预览必须暴露透明控制（🏁 棋盘格）且不暴露压平（🎨 为静态图专用）。
    /// 用 1×1 透明 GIF 样本（base64 内嵌，SKCodec 可解 1 帧）。
    /// </summary>
    [AvaloniaFact]
    public void ShowGif_ExposesTransparencyControls()
    {
        var gifPath = CreateTestGif();
        try
        {
            var vm = new PreviewViewModel();
            vm.ShowGif(gifPath);

            Assert.Equal(PreviewType.AnimatedImage, vm.PreviewType);
            Assert.True(vm.HasTransparencyControls);
            Assert.False(vm.HasFlattenAlphaControls);
            Assert.True(vm.HasAnimationControls);
        }
        finally
        {
            File.Delete(gifPath);
        }
    }

    /// <summary>写一个 1×1 透明 GIF 到临时目录（经典 43 字节样本，base64）。</summary>
    private static string CreateTestGif()
    {
        var bytes = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        var path = Path.Combine(Path.GetTempPath(), $"mantiszip_gif_test_{Guid.NewGuid():N}.gif");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
