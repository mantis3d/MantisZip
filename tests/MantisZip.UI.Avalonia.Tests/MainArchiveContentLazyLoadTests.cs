using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩包内容区延迟加载回归测试（2026-10-21 启动优化，阶段 2）。
///
/// 内容区（目录树 / 文件列表 DataGrid / 地址栏 / 预览面板宿主 / 拖拽覆层）共 ~153 个元素，
/// 压缩包未打开时不可见 → 改为首次加载压缩包时才实例化
/// （<c>MainWindow.EnsureArchiveContent()</c>）。本测试锁定「默认不创建、加载后创建且只创建一次」。
/// </summary>
public class MainArchiveContentLazyLoadTests
{
    [AvaloniaFact]
    public void ArchiveContent_IsNotCreated_Until_ArchiveLoaded()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);
        Assert.Null(host!.Content);

        var vm = (MainWindowViewModel)win.DataContext!;
        Assert.False(vm.IsArchiveLoaded);

        // 加载压缩包 → 内容区才被创建
        vm.IsArchiveLoaded = true;

        Assert.IsType<ArchiveContentView>(host.Content);
        win.Close();
    }

    [AvaloniaFact]
    public void ArchiveContent_IsCreated_OnlyOnce()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;
        var first = host!.Content;

        vm.IsArchiveLoaded = false;
        vm.IsArchiveLoaded = true;

        Assert.Same(first, host.Content);
        win.Close();
    }

    [AvaloniaFact]
    public void ArchiveContentView_Contains_FileListGrid_And_PreviewHost()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;

        var view = Assert.IsType<ArchiveContentView>(host!.Content);
        // 内容区关键控件由视图公开（MainWindow 通过这些属性做布局数学 / 列状态 / 拖拽覆层）
        Assert.NotNull(view.FileListGridControl);
        Assert.NotNull(view.PreviewPanelHostControl);
        Assert.NotNull(view.FolderTreeBorderControl);
        Assert.NotNull(view.DragAddOverlayControl);
        win.Close();
    }
}
