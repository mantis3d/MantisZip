// 注意：本文件的命名空间是 MantisZip.UI.Avalonia.Tests，`Avalonia.` 前缀会先命中
// MantisZip.UI.Avalonia，因此新 using 必须显式写 global:: 才能指到真正的 Avalonia 命名空间。
using global::Avalonia.Themes.Fluent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「关于 → 更新」页签的按钮可读性回归。
/// 背景：与 <see cref="UpdateAvailableDialogTests"/> 同一个缺陷的第二个宿主——2026-10-28 用户真机
/// 反馈「关于窗口里的检测更新按钮也有文字问题」，根因同样是按钮本地写了
/// <c>Padding="{DynamicResource SpacingXsThk}"</c>（8,8,8,8）而高度只有 <c>ControlHeight</c>=28，
/// 只剩 12px 给 14px 字号的文字。
/// 这里把「按钮高度减去上下内边距必须留够文字空间」的规则套到关于窗口上，避免再漏第三处。
/// </summary>
public class AboutWindowUpdateTabTests
{
    [AvaloniaFact]
    public void UpdateTab_Buttons_HaveVisibleText_WithEnoughVerticalRoom()
    {
        // 裸测试宿主不自带 ThemeLight.axaml（ControlHeight / SpacingXsThk 都在里面），手工并入
        var theme = new ResourceInclude(new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"))
        {
            Source = new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"),
        };
        var app = Application.Current!;
        app.Resources.MergedDictionaries.Add(theme);
        // TabControl 的 ControlTheme 来自 FluentTheme 样式（不是资源字典），
        // 裸测试宿主没有它 ⇒ 页签容器根本不会生成、按钮集合为空。必须在应用级样式里补上。
        var fluent = new FluentTheme();
        app.Styles.Add(fluent);
        try
        {
            var window = new AboutWindow();
            window.Show();
            PumpLayout(window);

            // 「更新」是第 5 个页签：TabControl 只实例化选中的页签内容，必须显式切过去
            var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            Assert.Equal(5, tabs.ItemCount);
            tabs.SelectedIndex = 4;
            // 切换页签后 TabControl 的 content presenter 是下一轮布局/调度才生成并排布的，
            // 必须把「布局泵」跑几轮，否则拿到的是 Bounds=0 的新控件（本测试第一版就栽在这）。
            PumpLayout(window);

            // 只检查「真正可见」的按钮：最新版本详情块在没检查出结果时是隐藏的，
            // 它里面的「打开发布页」按钮 Bounds 恒为 0，拿它做几何断言没有意义。
            var buttons = tabs.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible)
                .ToList();
            Assert.NotEmpty(buttons);
            Assert.Contains(buttons, b => (b.Content as string)?.Contains("检查") == true);

            foreach (var button in buttons)
            {
                var content = button.Content as string;
                Assert.False(string.IsNullOrWhiteSpace(content), "关于窗口按钮文字为空");

                var verticalRoom = button.Bounds.Height - button.Padding.Top - button.Padding.Bottom;
                Assert.True(
                    verticalRoom >= 8,
                    $"按钮「{content}」可用高度只有 {verticalRoom:F1}px（Bounds={button.Bounds.Height:F1}，Padding 上下={button.Padding.Top:F1}+{button.Padding.Bottom:F1}），文字会被裁掉");
            }

            window.Close();
        }
        finally
        {
            app.Resources.MergedDictionaries.Remove(theme);
            app.Styles.Remove(fluent);
        }
    }

    /// <summary>
    /// KI-008 的回归点：检查**失败**时必须显示「检查更新失败」，不能显示「已是最新版本」。
    /// <para>
    /// 改造前 <see cref="UpdateService.CheckForUpdateAsync"/> 把「无更新 / 网络失败 / 超时」全塌缩成
    /// <c>null</c>，所以断网时「关于 → 更新」给出的答案是「已是最新」——在最该报警的时候给了
    /// 最误导的答案。三态改造后这里锁住 UI 侧的分支。
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task UpdateTab_WhenCheckFails_ShowsFailureInsteadOfUpToDate()
    {
        var theme = new ResourceInclude(new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"))
        {
            Source = new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"),
        };
        var app = Application.Current!;
        app.Resources.MergedDictionaries.Add(theme);
        var fluent = new FluentTheme();
        app.Styles.Add(fluent);
        try
        {
            // 让网络层必定失败（Core 的 internal 测试钩子，见 MantisZip.Core.csproj 的 InternalsVisibleTo）
            UpdateService.SetTestHandler(new FailingHandler());
            try
            {
                var window = new AboutWindow();
                window.Show();
                PumpLayout(window);

                var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
                tabs.SelectedIndex = 4;
                PumpLayout(window);

                var checkButton = tabs.GetVisualDescendants().OfType<Button>()
                    .First(b => (b.Content as string)?.Contains("检查") == true);
                checkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                // 处理器是 async void：轮询等它把状态写出来（无头环境没有真实网络，几次调度即完成）
                for (var i = 0; i < 100 && window.UpdateStatusText == LocalizationManager.T("Update_Checking"); i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10);
                }
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(LocalizationManager.T("Update_CheckFailed"), window.UpdateStatusText);
                Assert.NotEqual(LocalizationManager.T("Update_UpToDate"), window.UpdateStatusText);
                window.Close();
            }
            finally
            {
                UpdateService.SetTestHandler(null);
            }
        }
        finally
        {
            app.Resources.MergedDictionaries.Remove(theme);
            app.Styles.Remove(fluent);
        }
    }

    /// <summary>必定抛异常的 HTTP 桩：模拟断网 / 超时。</summary>
    private sealed class FailingHandler : DelegatingHandler
    {
        public FailingHandler()
        {
            // 不挂 InnerHandler 时默认实现会抛 InvalidOperationException；本桩完全重写 SendAsync、永不真发包
            InnerHandler = new HttpClientHandler();
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("模拟断网");
    }

    /// <summary>
    /// 无头环境不会自动跑布局循环：手工 Measure/Arrange + 刷新布局 + 清调度队列，重复几轮
    /// 让「切换页签 → 生成内容 → 排布」这条链走完。
    /// </summary>
    private static void PumpLayout(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.Measure(new Size(900, 700));
            window.Arrange(new Rect(0, 0, 900, 700));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
