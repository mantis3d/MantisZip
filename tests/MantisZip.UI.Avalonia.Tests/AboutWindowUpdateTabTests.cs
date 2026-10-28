// 注意：本文件的命名空间是 MantisZip.UI.Avalonia.Tests，`Avalonia.` 前缀会先命中
// MantisZip.UI.Avalonia，因此新 using 必须显式写 global:: 才能指到真正的 Avalonia 命名空间。
using global::Avalonia.Themes.Fluent;
using Avalonia;
using Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Services;
using System.Text;
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

    /// <summary>
    /// 「关于 → 更新」也要显示更新内容：接口带回 release body 时，详情区里必须出现渲染后的
    /// Markdown 正文（而不是只给一个「打开发布页」链接）。
    /// </summary>
    [AvaloniaFact]
    public async Task UpdateTab_WhenCheckFindsNewVersion_ShowsReleaseNotes()
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
            // 远高于真实版本（0.5.4），保证判定为「有新版本」；body 里放一个独一无二的标记
            UpdateService.SetTestHandler(new FixedReleaseHandler(ReleaseJsonWithMarker));
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

                for (var i = 0; i < 100 && window.UpdateStatusText == LocalizationManager.T("Update_Checking"); i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10);
                }
                Dispatcher.UIThread.RunJobs();
                PumpLayout(window);

                // 版本行/详情区先成立，再验证正文真的进了 NotesHost（不是只有开关为 true）
                Assert.Contains("9.9.9", window.UpdateLatestVersionText);
                Assert.True(window.ShowLatestVersion, "发现新版本时详情区应显示");
                Assert.True(window.ShowReleaseNotes, "接口带回 body 时应显示更新说明区");

                var notesHost = window.FindControl<ContentControl>("NotesHost");
                Assert.NotNull(notesHost);
                Assert.NotNull(notesHost!.Content);

                var rendered = CollectText(notesHost);
                Assert.Contains(ReleaseNotesMarker, rendered);
                // Markdown 的列表项也要真的渲染成控件（不是把原文当一行纯文本塞进去）
                Assert.Contains("第一处修复", rendered);

                // 「下载」按钮必须在更新说明**上方**：更新说明可能很长，操作按钮放底部
                // 就得先滚到底才看得见（2026-10-28 用户反馈的排版问题）。
                var downloadButton = tabs.GetVisualDescendants().OfType<Button>()
                    .First(b => (b.Content as string) == LocalizationManager.T("Update_Download"));
                var versionText = tabs.GetVisualDescendants().OfType<TextBlock>()
                    .First(t => t.Text == window.UpdateLatestVersionText);
                var notesBorder = notesHost.GetVisualAncestors().OfType<Border>().First();

                var downloadTop = downloadButton.TranslatePoint(default, window)!.Value.Y;
                var notesTop = notesBorder.TranslatePoint(default, window)!.Value.Y;
                var versionCenter = versionText.TranslatePoint(default, window)!.Value.Y + (versionText.Bounds.Height / 2);
                var downloadCenter = downloadButton.TranslatePoint(default, window)!.Value.Y + (downloadButton.Bounds.Height / 2);

                Assert.True(
                    downloadTop < notesTop,
                    $"「下载」按钮应在更新说明上方（download.Y={downloadTop:F1}，notes.Y={notesTop:F1}）");
                // 与「最新版本」同一行：两者垂直中心对齐（允许布局取整的 2px 误差）
                Assert.InRange(Math.Abs(downloadCenter - versionCenter), 0, 2);

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

    /// <summary>更新说明里的唯一标记，用来区分「渲染了正文」与「渲染了别的字符串」。</summary>
    private const string ReleaseNotesMarker = "RELEASE-NOTES-MARKER";

    private const string ReleaseJsonWithMarker = """
        {
          "prerelease": false,
          "tag_name": "v9.9.9",
          "published_at": "2026-01-02T03:04:05Z",
          "body": "## 修复内容\n\n- 第一处修复 RELEASE-NOTES-MARKER\n- 第二处修复\n"
        }
        """;

    /// <summary>
    /// 收集一段可视子树里的全部文字：<see cref="TextBlock.Text"/> 与 <c>Inlines</c> 里的 Run
    /// （MarkdownPreviewBuilder 写的是 Run，只看 Text 会误判为空）。
    /// </summary>
    private static string CollectText(Visual root)
    {
        var builder = new StringBuilder();
        foreach (var textBlock in root.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!string.IsNullOrEmpty(textBlock.Text))
            {
                builder.Append(textBlock.Text);
            }

            if (textBlock.Inlines is { } inlines)
            {
                foreach (var run in inlines.OfType<Run>())
                {
                    if (!string.IsNullOrEmpty(run.Text))
                    {
                        builder.Append(run.Text);
                    }
                }
            }
        }

        return builder.ToString();
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

    /// <summary>返回固定 JSON 的 HTTP 桩：模拟「GitHub 上确实有更高版本」。</summary>
    private sealed class FixedReleaseHandler(string json) : DelegatingHandler
    {
        public FixedReleaseHandler()
            : this(string.Empty)
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
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
