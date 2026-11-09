using System.ComponentModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 假列头右键菜单 Tile/Content 判别回归（用户 2026-11-07 真机反馈：Tile 列头开关行显隐无效、
/// 反而改变了 Content 模式的行）。
///
/// 现象对应实现：菜单 Opening 里用 <c>menu.PlacementTarget</c> 判断宿主是 Tile 还是 Content 列头——
/// 这是 WPF 语义（Control.ContextMenu 打开时自动回填 PlacementTarget）；Avalonia 的该属性
/// 不自动回填 ⇒ 恒 null ⇒ isTile 恒 false ⇒ **Tile 菜单也在写 Content 的字段**。
///
/// 调用方式：headless 里 ContextMenu.Open 不触发 XAML 注册的 Opening（实测），且生产里
/// PlacementTarget 同样是 null——因此用反射以「真实菜单对象 + Undo 的状态」直接调用 handler，
/// 与生产输入一致。用例：Tile 菜单点第一项（大小），Tile 的字段必须翻转、Content 零变化。
/// </summary>
public class CardRowHeaderMenuTests
{
    private static void RaiseOpening(ArchiveContentView view, ContextMenu menu)
    {
        var handler = Assert.IsAssignableFrom<MethodInfo>(typeof(ArchiveContentView).GetMethod(
            "CardRowHeaderContextMenu_Opening", BindingFlags.NonPublic | BindingFlags.Instance));
        handler.Invoke(view, new object?[] { menu, new CancelEventArgs() });
    }

    private static void ClickFirstItem(ContextMenu menu)
    {
        // CardRowCatalog.Kinds[0] = Size，菜单首项即「大小」行开关
        var first = Assert.IsType<MenuItem>(menu.Items!.Cast<object>().First());
        Assert.Equal(CardRowKind.Size, first.Tag);
        first.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }
    [AvaloniaFact]
    public void TileRowHeaderMenu_Click_TogglesTileField_NotContentField()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;
        var view = Assert.IsType<ArchiveContentView>(host!.Content);
        var border = Assert.IsType<Border>(view.FindControl<Border>("TileRowHeaderBorder"));
        var menu = Assert.IsType<ContextMenu>(border!.ContextMenu);

        AppSettings.ResetCacheForTests();
        var snap = new
        {
            AppSettings.Load().TileRowShowSize,
            AppSettings.Load().TileRowShowCompressedSize,
            AppSettings.Load().TileRowShowRatio,
            AppSettings.Load().TileRowShowDate,
            AppSettings.Load().ContentRowShowSize,
            AppSettings.Load().ContentRowShowCompressedSize,
            AppSettings.Load().ContentRowShowRatio,
            AppSettings.Load().ContentRowShowDate,
        };
        try
        {
            RaiseOpening(view, menu);
            ClickFirstItem(menu);
            // Tile 菜单必须翻 Tile 的字段（Content 零变化）
            Assert.NotEqual(snap.TileRowShowSize, AppSettings.Load().TileRowShowSize);
            Assert.Equal(snap.ContentRowShowSize, AppSettings.Load().ContentRowShowSize);
            Assert.Equal(snap.ContentRowShowCompressedSize, AppSettings.Load().ContentRowShowCompressedSize);
        }
        finally
        {
            // 落盘净零：8 字段全部按快照还原（惯例同 CardRowSettingsTests）
            var disk = AppSettings.Load();
            MainWindowViewModel.ApplyCardRow(disk, true, CardRowKind.Size, snap.TileRowShowSize);
            MainWindowViewModel.ApplyCardRow(disk, true, CardRowKind.CompressedSize, snap.TileRowShowCompressedSize);
            MainWindowViewModel.ApplyCardRow(disk, true, CardRowKind.Ratio, snap.TileRowShowRatio);
            MainWindowViewModel.ApplyCardRow(disk, true, CardRowKind.Date, snap.TileRowShowDate);
            MainWindowViewModel.ApplyCardRow(disk, false, CardRowKind.Size, snap.ContentRowShowSize);
            MainWindowViewModel.ApplyCardRow(disk, false, CardRowKind.CompressedSize, snap.ContentRowShowCompressedSize);
            MainWindowViewModel.ApplyCardRow(disk, false, CardRowKind.Ratio, snap.ContentRowShowRatio);
            MainWindowViewModel.ApplyCardRow(disk, false, CardRowKind.Date, snap.ContentRowShowDate);
            disk.Save();
        }

        win.Close();
    }

    [AvaloniaFact]
    public void ContentRowHeaderMenu_Click_TogglesContentField_NotTileField()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;
        var view = Assert.IsType<ArchiveContentView>(host!.Content);
        var border = Assert.IsType<Border>(view.FindControl<Border>("ContentRowHeaderBorder"));
        var menu = Assert.IsType<ContextMenu>(border!.ContextMenu);

        AppSettings.ResetCacheForTests();
        var snapSize = AppSettings.Load().ContentRowShowSize;
        var snapTile = AppSettings.Load().TileRowShowSize;
        try
        {
            RaiseOpening(view, menu);
            ClickFirstItem(menu);
            // Content 菜单必须翻 Content 的字段（Tile 零变化）
            Assert.NotEqual(snapSize, AppSettings.Load().ContentRowShowSize);
            Assert.Equal(snapTile, AppSettings.Load().TileRowShowSize);
        }
        finally
        {
            var disk = AppSettings.Load();
            MainWindowViewModel.ApplyCardRow(disk, false, CardRowKind.Size, snapSize);
            disk.Save();
        }

        win.Close();
    }
}
