using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 菜单子项延迟创建回归测试（2026-10-22 启动优化，阶段 3）。
///
/// 文件/编辑/视图/工具四个顶级菜单的子项（共 25 项 + 图标）已抽到
/// <c>Controls/Menus/*MenuItems.axaml</c>，改为**首次交互该菜单时**才创建
/// （悬停预构建 + 键盘展开兜底）→ 启动路径不再实例化这批元素。
/// 本测试锁定「默认不创建、创建后子项就位且只创建一次」。
/// </summary>
public class MainMenuLazyLoadTests
{
    [AvaloniaFact]
    public void MenuItems_AreNotCreated_Until_MenuInteracted()
    {
        var win = new MainWindow();

        // 构造完成时：四个顶级菜单都应为空壳（子项未创建）
        Assert.Empty(ItemsOf(win, "FileMenuItem"));
        Assert.Empty(ItemsOf(win, "EditMenuItem"));
        Assert.Empty(ItemsOf(win, "ViewMenuItem"));
        Assert.Empty(ItemsOf(win, "ToolsMenuItem"));

        // 触发一次懒加载（模拟首次交互）
        win.EnsureFileMenuItems();
        win.EnsureEditMenuItems();
        win.EnsureViewMenuItems();
        win.EnsureToolsMenuItems();

        Assert.NotEmpty(ItemsOf(win, "FileMenuItem"));
        Assert.NotEmpty(ItemsOf(win, "EditMenuItem"));
        Assert.NotEmpty(ItemsOf(win, "ViewMenuItem"));
        Assert.NotEmpty(ItemsOf(win, "ToolsMenuItem"));
        win.Close();
    }

    [AvaloniaFact]
    public void MenuItems_Ensure_IsIdempotent()
    {
        var win = new MainWindow();
        win.EnsureFileMenuItems();
        var first = ItemsOf(win, "FileMenuItem").Count;

        win.EnsureFileMenuItems();
        win.EnsureFileMenuItems();

        Assert.Equal(first, ItemsOf(win, "FileMenuItem").Count);
        win.Close();
    }

    [AvaloniaFact]
    public void MenuItems_HaveDataContext_BoundToViewModel()
    {
        var win = new MainWindow();
        win.EnsureFileMenuItems();

        // 搬运后子项脱离原 StackPanel（继承链断裂）→ 必须显式绑定 VM
        foreach (var item in ItemsOf(win, "FileMenuItem"))
            Assert.Same(win.DataContext, item.DataContext);

        win.Close();
    }

    /// <summary>取某个具名顶级菜单项的子项集合（构造为 0 时返回空列表）。</summary>
    private static System.Collections.Generic.IReadOnlyList<Control> ItemsOf(MainWindow win, string name)
    {
        var menuItem = win.FindControl<MenuItem>(name);
        Assert.NotNull(menuItem);
        return menuItem!.Items.OfType<Control>().ToList();
    }
}
