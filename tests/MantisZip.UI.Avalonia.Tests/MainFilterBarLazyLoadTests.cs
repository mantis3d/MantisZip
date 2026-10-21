using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 过滤栏延迟加载回归测试（2026-10-21 启动优化）。
///
/// 过滤栏默认隐藏，其 ~50 个元素的 XAML 膨胀在启动路径上无意义 → 改为首次显示时才实例化
/// （<c>MainWindow.EnsureFilterBar()</c>）。本测试锁定「默认不创建、显示时创建且只创建一次」。
/// </summary>
public class MainFilterBarLazyLoadTests
{
    [AvaloniaFact]
    public void FilterBar_IsNotCreated_Until_FilterBarVisible()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("FilterBarHost");
        Assert.NotNull(host);
        Assert.Null(host!.Content);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsFilterBarVisible = true;

        Assert.IsType<MainFilterBar>(host.Content);
        win.Close();
    }

    [AvaloniaFact]
    public void FilterBar_IsCreated_OnlyOnce()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("FilterBarHost");
        Assert.NotNull(host);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsFilterBarVisible = true;
        var first = host!.Content;
        vm.IsFilterBarVisible = false;
        vm.IsFilterBarVisible = true;

        Assert.Same(first, host.Content);
        win.Close();
    }
}
