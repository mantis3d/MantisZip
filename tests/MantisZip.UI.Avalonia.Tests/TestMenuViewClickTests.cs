using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using MantisZip.UI.Avalonia.Controls.Menus;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 回归守卫：「测试」菜单的诊断窗口必须能取到 owner（否则 28 个窗口全部静默打不开）。
///
/// 背景：<see cref="TestMenuView"/> 的 <c>CreateMenuItem()</c> 用 <c>Content = null</c>
/// 把根 <c>MenuItem</c> 从视图上摘下，视图自身从不进入可视树。2026-10-22 启动优化阶段 3
/// （86e5139a）把点击处理从 <c>MainWindow</c> 迁进该视图后，owner 用
/// <c>TopLevel.GetTopLevel(this)</c> 取值恒为 <c>null</c>，配合
/// <c>if (window != null &amp;&amp; owner != null)</c> 就变成「点了毫无反应」。
///
/// 本用例复刻真实结构（MenuItem 挂在窗口里的 Menu 上、视图已分离）直接断言
/// <see cref="TestMenuView.ResolveOwner"/> 的解析结果：旧写法（只看 <c>this</c>）必红。
/// 比驱动真实点击稳定 —— 无头环境里 <c>ShowDialog</c> 不登记 <c>OwnedWindows</c>，
/// 无法从窗口集合观察。
/// </summary>
public class TestMenuViewClickTests
{
    [AvaloniaFact]
    public void ResolveOwner_FindsWindowThroughMenuItem_WhenViewIsDetached()
    {
        var app = Application.Current!;
        var theme = new FluentTheme();
        app.Styles.Add(theme);

        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = new Menu()
        };

        try
        {
            var view = new TestMenuView();
            var rootMenuItem = view.CreateMenuItem();     // ← 这一步把内容从视图上摘下
            ((Menu)window.Content).Items.Add(rootMenuItem);

            window.Show();
            PumpLayout(window);

            var about = rootMenuItem.Items
                .OfType<MenuItem>()
                .First(item => item.Tag as string == "AboutWindow");

            // 未展开子菜单时子项还没进可视树（真实点击时它挂在 PopupRoot 里，也不是 Window），
            // 视图本身更是从未进树 —— 只靠 TopLevel 查询会全部落空，这正是旧写法的失效点。
            Assert.Null(TopLevel.GetTopLevel(about));
            Assert.Null(TopLevel.GetTopLevel(view));

            // 修复后：沿逻辑父级回溯（子项 → 根菜单项 → 主菜单 → 窗口）拿得到承载窗口
            Assert.Same(window, TestMenuView.ResolveOwner(about, view));

            // 真机点击时子菜单是**展开的**（子项被容器化、可能落在弹出层）：解析结果必须同样正确，
            // 不能因为「展开后才进树」就换一套逻辑。
            rootMenuItem.IsSubMenuOpen = true;
            PumpLayout(window);

            Assert.NotNull(TopLevel.GetTopLevel(about));
            Assert.Same(window, TestMenuView.ResolveOwner(about, view));
        }
        finally
        {
            window.Close();
            app.Styles.Remove(theme);
        }
    }

    [AvaloniaFact]
    public void ResolveOwner_ReturnsNull_WhenMenuIsNotHostedYet()
    {
        // 还没挂进任何窗口：必须返回 null，让点击处理器降级成 Show() 而不是静默丢弃。
        var view = new TestMenuView();
        var rootMenuItem = view.CreateMenuItem();
        var about = rootMenuItem.Items
            .OfType<MenuItem>()
            .First(item => item.Tag as string == "AboutWindow");

        Assert.Null(TestMenuView.ResolveOwner(about, view));
    }

    /// <summary>
    /// 无头环境 Show() 不会自动跑布局，必须手动推进（同 IconTestWindowMatrixTests 的做法）。
    /// 命名空间用短名：本文件在 <c>MantisZip.UI.Avalonia.Tests</c> 下，写 <c>Avalonia.Size</c>
    /// 会被解析成 <c>MantisZip.UI.Avalonia.Size</c>。
    /// </summary>
    private static void PumpLayout(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.Measure(new Size(400, 300));
            window.Arrange(new Rect(0, 0, 400, 300));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
