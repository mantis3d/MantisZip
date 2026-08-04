using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
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
}
