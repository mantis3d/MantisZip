using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Dialogs;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 图标测试窗「三档尺寸 × 双主题矩阵」页渲染回归测试（无头 Avalonia）。
/// 根治回归：矩阵页把 ThemeLight / ThemeDark 两份字典通过 `ResourceInclude` 合并进
/// **每个色块的 StackPanel.Resources** —— 这种控件级嵌套 include 在本仓库没有先例。
/// 一旦 Avalonia 在该作用域静默忽略它，浅色块与深色块就会渲染成同一个背景色
/// （都跟随当前应用主题），而构建期完全不报错。本测试直接断言两种亮度的色块同时存在，
/// 并断言三档尺寸（16 / 22 / 28）各渲染一次。
///
/// 注：测试宿主只配置了裸 `Application`（见 TestAppBuilder），没有 FluentTheme，
/// 因此 TabControl 取不到控件模板、项容器根本不会被创建。这里给窗口单独挂一份
/// FluentTheme，既让模板生效，又不会污染其它测试用例。
/// </summary>
public class IconTestWindowMatrixTests
{
    /// <summary>矩阵页必须同时渲染出「浅色底块」与「深色底块」，且三档尺寸各出现一次。</summary>
    [AvaloniaFact]
    public void MatrixPage_RendersLightAndDarkBlocks_WithThreeSizeSteps()
    {
        // 宿主只配了裸 Application，没有控件主题 → 先把 FluentTheme 挂到应用级
        // （控件级 Styles 加不进去：TabControl 的模板来自主题字典，只在应用级生效），
        // 用例结束移除，避免影响其它用例。
        var app = Application.Current!;
        var theme = new FluentTheme();
        app.Styles.Add(theme);
        var window = new IconTestWindow { Width = 1800, Height = 1200 };
        window.Show();
        PumpLayout(window);

        var tabs = window.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
        Assert.NotNull(tabs);
        Assert.True(tabs!.ItemCount >= 2, "图标测试窗应有两个页签（图标列表 / 尺寸矩阵）");

        // 切到矩阵页并重新走一遍布局，让虚拟化 ListBox 创建项容器
        tabs.SelectedIndex = 1;
        PumpLayout(window);

        var backgrounds = window.GetVisualDescendants().OfType<Border>()
            .Select(b => b.Background)
            .OfType<ISolidColorBrush>()
            .Select(b => b.Color)
            .Where(c => c.A > 200)
            .Select(c => (c.R + c.G + c.B) / 3.0)
            .ToList();

        // 浅色块（白底深色图标）与深色块（深底浅色图标）必须同时存在；
        // 若嵌套 ResourceInclude 失效，两者都会跟随应用主题而只剩一种亮度。
        Assert.Contains(backgrounds, l => l > 200);
        Assert.Contains(backgrounds, l => l < 80);

        var iconSizes = window.GetVisualDescendants().OfType<PathIcon>()
            .Select(pi => pi.Width)
            .Where(w => w is 16 or 22 or 28)
            .Distinct()
            .OrderBy(w => w)
            .ToList();
        Assert.Equal(new double[] { 16, 22, 28 }, iconSizes);

        window.Close();
        app.Styles.Remove(theme);
    }

    /// <summary>无头环境里 Show() 不会自动完成布局，必须显式 Measure/Arrange 并抽干调度队列。</summary>
    private static void PumpLayout(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.Measure(new Size(1800, 1200));
            window.Arrange(new Rect(0, 0, 1800, 1200));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
