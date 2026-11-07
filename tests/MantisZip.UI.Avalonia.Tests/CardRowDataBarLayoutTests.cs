using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// Tile / Content 卡片「数据条」宽度基准回归（2026-11-07 用户真机报障）：
/// 数据条在比例 100% 时**超出卡片边框**。
///
/// 根因：数据条宽度 = 比例 × <c>ContentPresenter.Bounds.Width</c>（列表项容器），
/// 而 Tile 的容器宽 = 卡片 + 16（<c>ThumbnailTileCellWidth</c>），卡片本身只有卡片宽 - 8 内宽；
/// Content 的容器宽 = 整行（含左侧缩略图卡），而条画在右侧元数据栏里。两处都会在 100% 时越界。
///
/// 用例做法：headless 里 ItemsControl 项不物化，故直接取真实 view 的 ItemTemplate，
/// 放进**手工 ContentPresenter**（宽度 = 旧实现会取到的容器宽）并真实布局，检查每根数据条
/// 不超过它所在行 Grid 的宽度。修复前 Tile 例条宽 144 > 行宽 128（红灯）。
/// </summary>
public class CardRowDataBarLayoutTests
{
    [AvaloniaFact]
    public void TileRowDataBar_AtFullRatio_StaysInsideRow()
    {
        // 格子宽 144（卡片 136 + 8 间距），卡片内宽 128
        var (host, presenter) = HostRow(FileListLayoutMode.Tile, "TileLayoutScrollViewer", hostWidth: 144);

        AssertDataBarsFitTheirRow(presenter);

        host.Close();
    }

    [AvaloniaFact]
    public void ContentRowDataBar_AtFullRatio_StaysInsideMetadataPanel()
    {
        // 整行 600：左侧缩略图卡 128 + 边距/内边距，右侧元数据栏 ≈448
        var (host, presenter) = HostRow(FileListLayoutMode.Content, "ContentLayoutScrollViewer", hostWidth: 600);

        AssertDataBarsFitTheirRow(presenter);

        host.Close();
    }

    /// <summary>真实 view 的 ItemTemplate + 手工 ContentPresenter（模拟列表项容器）＋真实布局。</summary>
    private static (Window Host, ContentPresenter Presenter) HostRow(
        FileListLayoutMode mode, string scrollViewerName, double hostWidth)
    {
        var win = new MainWindow();
        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;
        vm.LayoutMode = mode;

        var view = Assert.IsType<ArchiveContentView>(
            win.FindControl<ContentControl>("ArchiveContentHost")!.Content);
        var sv = Assert.IsAssignableFrom<ScrollViewer>(view.FindControl<ScrollViewer>(scrollViewerName));
        var items = Assert.IsType<ItemsControl>(sv!.Content);
        var template = Assert.IsAssignableFrom<IDataTemplate>(items.ItemTemplate);

        // 比例全 1.0 = 100%，修复前就是溢出最明显的状态
        var item = new ArchiveItemModel
        {
            Name = "big.bin",
            FullPath = "big.bin",
            Size = 100,
            SizeRatio = 1.0,
            CompressedSizeRatio = 1.0,
            RatioBarValue = 1.0,
            DateRatio = 1.0,
        };

        var host = new Window { Width = hostWidth, Height = 400 };
        foreach (var (key, value) in view.Resources)
        {
            host.Resources[key] = value;
        }

        // App 运行时注入的资源（测试 App 不是真正的 App 实例，需手工补齐布局相关键）
        host.Resources["ThumbnailTileItemWidth"] = 136.0;
        host.Resources["ThumbnailCardSize"] = 128.0;
        host.Resources["SpacingXxsThk"] = new Thickness(4);
        host.Resources["SpacingXsThk"] = new Thickness(4);
        host.Resources["SpacingSmThk"] = new Thickness(8);
        host.Resources["ControlHeightSm"] = 22.0;

        var presenter = new ContentPresenter { Content = item, ContentTemplate = template };
        host.Content = presenter;
        host.Show();

        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            host.Measure(new Size(hostWidth, 400));
            host.Arrange(new Rect(0, 0, hostWidth, 400));
            host.UpdateLayout();
        }

        win.Close();
        return (host, presenter);
    }

    /// <summary>每根数据条（比例 100% ⇒ 宽度应等于所在行宽）都不得超出其最近祖先 Grid 的宽度。</summary>
    private static void AssertDataBarsFitTheirRow(ContentPresenter presenter)
    {
        var bars = presenter.GetVisualDescendants()
            .OfType<Rectangle>()
            .Where(r => r.Bounds.Width > 0)
            .ToArray();

        Assert.NotEmpty(bars);

        foreach (var bar in bars)
        {
            var row = NearestAncestorGrid(bar);
            Assert.NotNull(row);
            Assert.True(
                bar.Bounds.Width <= row!.Bounds.Width + 0.5,
                $"数据条超出所在行：条宽 {bar.Bounds.Width:0.##} > 行宽 {row.Bounds.Width:0.##}");
        }
    }

    private static Grid? NearestAncestorGrid(Visual visual)
    {
        for (Visual? parent = visual.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (parent is Grid grid)
            {
                return grid;
            }
        }

        return null;
    }
}
