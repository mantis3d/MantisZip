using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 内容行（Content）左右对齐回归（2026-11-08 用户真机报障）：
/// 缩略图右侧的「名称 + 元数据」块比左侧缩略图**低一块**。
///
/// 根因：左侧缩略图 <c>VerticalAlignment="Top"</c>，右侧信息 StackPanel 是
/// <c>VerticalAlignment="Center"</c>——行高由缩略图决定（128px 级），信息块较短时
/// 居中会让它的顶边下沉约 (缩略图高度 - 信息块高度)/2。
/// 用例：真实 view 的 ItemTemplate + 手工 ContentPresenter 真实布局，断言两者顶边一致。
/// </summary>
public class CardRowContentAlignmentTests
{
    [AvaloniaFact]
    public void ContentRow_InfoBlock_StartsAtThumbnailTop()
    {
        var (host, presenter) = HostContentRow();

        var rowGrid = presenter.GetVisualDescendants()
            .OfType<Grid>()
            .First(g => g.Children.Count == 2
                        && g.Children[0].GetType() == typeof(Panel)
                        && g.Children[1] is StackPanel);

        var thumbnail = Assert.IsType<ContentControl>(
            rowGrid.Children[0].GetVisualDescendants().OfType<ContentControl>().First());
        var info = Assert.IsType<StackPanel>(rowGrid.Children[1]);

        var thumbnailTop = thumbnail.TranslatePoint(default, host);
        var infoTop = info.TranslatePoint(default, host);

        Assert.NotNull(thumbnailTop);
        Assert.NotNull(infoTop);
        Assert.True(
            Math.Abs(thumbnailTop!.Value.Y - infoTop!.Value.Y) <= 1.0,
            $"右侧信息块与缩略图顶边错位：缩略图 Top={thumbnailTop.Value.Y:0.##}，信息块 Top={infoTop.Value.Y:0.##}"
            + $"\n诊断：rowGrid.Bounds={rowGrid.Bounds} thumbnail.Bounds={thumbnail.Bounds} info.Bounds={info.Bounds}"
            + $"\nchildren={string.Join(", ", rowGrid.Children.Select(c => $"{c.GetType().Name}@({c.Bounds.X},{c.Bounds.Y}) {c.Bounds.Width}x{c.Bounds.Height}"))}");

        host.Close();
    }

    /// <summary>真实 Content 行 ItemTemplate + 手工 ContentPresenter（模拟列表项容器）＋真实布局。</summary>
    private static (Window Host, ContentPresenter Presenter) HostContentRow()
    {
        var win = new MainWindow();
        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;
        vm.LayoutMode = FileListLayoutMode.Content;

        var view = Assert.IsType<ArchiveContentView>(
            win.FindControl<ContentControl>("ArchiveContentHost")!.Content);
        var sv = Assert.IsAssignableFrom<ScrollViewer>(view.FindControl<ScrollViewer>("ContentLayoutScrollViewer"));
        var items = Assert.IsType<ItemsControl>(sv!.Content);
        var template = Assert.IsAssignableFrom<IDataTemplate>(items.ItemTemplate);

        var item = new ArchiveItemModel { Name = "a.txt", FullPath = "a.txt", Size = 1024 };

        const double hostWidth = 600;
        var host = new Window { Width = hostWidth, Height = 400 };
        foreach (var (key, value) in view.Resources)
        {
            host.Resources[key] = value;
        }

        // App 运行时注入的布局资源（测试 App 不是真正的 App 实例，需手工补齐）
        host.Resources["ThumbnailCardSize"] = 128.0;
        host.Resources["SpacingXxsThk"] = new Thickness(4);
        host.Resources["SpacingXsThk"] = new Thickness(4);
        host.Resources["SpacingSmThk"] = new Thickness(8);
        host.Resources["SpacingSm"] = 8.0;
        host.Resources["ControlHeightSm"] = 22.0;
        host.Resources["ControlHeightXxl"] = 128.0;

        var presenter = new ContentPresenter
        {
            Content = item,
            ContentTemplate = template,
            // 真实列表里行高由内容决定（StackPanel 垂直排布）；不设置会让 presenter 撑满宿主高度，
            // 把「居中偏移」放大成整行高度的一半，失真
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };
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
}
