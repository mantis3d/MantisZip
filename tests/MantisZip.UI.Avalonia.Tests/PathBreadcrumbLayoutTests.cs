using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using MantisZip.UI.Avalonia.Controls;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 面包屑的高度稳定性回归：浏览态（分段 / 空路径占位）与编辑态必须等高。
///
/// 背景（2026-10-29 真机反馈）：外壳高度原本由内容决定 —— 段按钮 20px、空路径占位文案
/// 17px + 上下各 8px Margin = 33px、编辑框 20px，三种内容高度不同，于是「点地址栏进编辑态」
/// 时整行会跳 13px，下方内容整体位移。修法是把内层 Grid 钉在 <c>MinHeight=20</c>，
/// 并去掉占位文案的上下 Margin。本用例就是防止它再退化。
/// </summary>
public class PathBreadcrumbLayoutTests
{
    [AvaloniaFact]
    public void BrowseAndEditMode_HaveSameHeight_WhenPathHasSegments()
    {
        var (window, crumb) = Show(@"C:\Users\Admin");

        var browse = crumb.Bounds.Height;
        // 高度由控件自己决定（ControlHeightMd = 32），宿主不写 Height/MinHeight；
        // 宿主若写了 Height 就是覆盖，等于把「一处改」又散回各处。
        Assert.Equal(32, browse, 1);

        crumb.EnterEditMode();
        Pump(window);
        var editing = crumb.Bounds.Height;

        Assert.Equal(browse, editing, 1);
    }

    [AvaloniaFact]
    public void BrowseAndEditMode_HaveSameHeight_WhenPathIsEmpty()
    {
        // 空路径走「占位文案」分支，这是历史上高度差 13px 的那条路径（设置页「路径」面板）
        var (window, crumb) = Show(string.Empty);

        var browse = crumb.Bounds.Height;
        crumb.EnterEditMode();
        Pump(window);
        var editing = crumb.Bounds.Height;

        Assert.Equal(browse, editing, 1);
    }

    [AvaloniaFact]
    public void BrowseAndEditMode_HaveSameHeight_ForVirtualPath()
    {
        var (window, crumb) = Show("docs/images", BreadcrumbPathKind.Virtual);

        var browse = crumb.Bounds.Height;
        crumb.EnterEditMode();
        Pump(window);
        var editing = crumb.Bounds.Height;

        Assert.Equal(browse, editing, 1);
    }

    private static (Window Window, PathBreadcrumb Crumb) Show(string path, BreadcrumbPathKind kind = BreadcrumbPathKind.Disk)
    {
        EnsureThemeLoaded();

        var crumb = new PathBreadcrumb
        {
            PathKind = kind,
            Path = path,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };
        var window = new Window
        {
            Width = 480,
            Height = 120,
            Content = new StackPanel { Children = { crumb } },
        };
        window.Show();
        Pump(window);
        return (window, crumb);
    }

    /// <summary>无头环境里样式/布局都要手动泵几轮才会落到 Bounds 上。</summary>
    private static void Pump(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(480, 120));
            window.Arrange(new Rect(0, 0, 480, 120));
            window.UpdateLayout();
        }
    }

    /// <summary>并入浅色主题：<c>ControlHeightSm</c> / <c>SpacingXxsThk</c> 等资源在主题字典里。</summary>
    private static void EnsureThemeLoaded()
    {
        var app = Application.Current!;
        var uri = new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml");
        if (app.Resources.MergedDictionaries.OfType<ResourceInclude>().Any(r => r.Source == uri)) return;
        app.Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });
    }
}
