// 注意：本文件的命名空间是 MantisZip.UI.Avalonia.Tests，`Avalonia.` 前缀会先命中
// MantisZip.UI.Avalonia，因此新 using 必须显式写 global:: 才能指到真正的 Avalonia 命名空间。
using global::Avalonia.Controls.Documents;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「发现新版本」提示对话框的几何与契约回归。
/// 背景：2026-10-28 首次真机验证时三个按钮**只剩色块、没有文字**——按钮本地写了
/// <c>Padding="{DynamicResource SpacingSmThk}"</c>（12,12,12,12），而按钮高度只有
/// <c>ControlHeight</c>=28，算下来只剩 4px 文字区，视觉上等同于「无文字」。
/// 这里用几何断言（按钮高度减去上下内边距必须留出至少 8px 给文字）把这类
/// 「内边距吃掉高度」的写法钉死，不依赖具体主题数值。
/// </summary>
public class UpdateAvailableDialogTests
{
    private static UpdateInfo NewInfo(string releaseNotes = "") =>
        new("0.5.3", "https://example.invalid/release", "https://example.invalid/release", null, releaseNotes);

    /// <summary>
    /// 取出 TextBlock 的完整文本：MarkdownPreviewBuilder 往 <c>Inlines</c> 里塞 <c>Run</c>，
    /// 此时 <c>Text</c> 属性保持 null，只看 <c>Text</c> 会把渲染成功的正文误判为空。
    /// </summary>
    private static string CollectText(TextBlock tb)
    {
        if (!string.IsNullOrEmpty(tb.Text))
            return tb.Text;

        if (tb.Inlines is null)
            return string.Empty;

        return string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text));
    }

    [AvaloniaFact]
    public void VersionCompare_UsesSingleLineWithCurrentAndLatest()
    {
        var dialog = new UpdateAvailableDialog(NewInfo());

        // 旧版把「发现新版本 v0.5.3」和「最新版本 0.5.3」分成两行（同一句话说两遍）；
        // 现在合并成「当前版本 X → 最新版本 0.5.3」一行，必须同时带出当前版本
        Assert.Contains("0.5.3", dialog.VersionCompareText);
        Assert.Contains(DebugUpdateVersion.Effective, dialog.VersionCompareText);
    }

    [AvaloniaFact]
    public void ReleaseNotes_AreRenderedAsMarkdown_WhenBodyPresent()
    {
        var theme = new ResourceInclude(new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"))
        {
            Source = new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"),
        };
        var app = Application.Current!;
        app.Resources.MergedDictionaries.Add(theme);
        try
        {
            var dialog = new UpdateAvailableDialog(NewInfo("## 修复内容\n\n- 删除目录现在真的会删掉"));
            dialog.Show();
            dialog.Measure(new Size(560, 700));
            dialog.Arrange(new Rect(0, 0, 560, 700));
            dialog.UpdateLayout();

            // 标题与列表项都被 MarkdownPreviewBuilder 渲染成了独立 TextBlock；
            // 注意：builder 是往 Inlines 里塞 Run（SelectableTextBlock.Text 保持 null），
            // 所以不能只看 Text 属性，要连 Inlines 一起收。
            var texts = dialog.GetVisualDescendants().OfType<TextBlock>()
                .Select(CollectText)
                .ToList();
            Assert.Contains("修复内容", texts);
            Assert.Contains(texts, t => t.Contains("删除目录现在真的会删掉"));

            // 有正文时不该再显示发布页地址回退行
            Assert.True(dialog.HasReleaseNotes);
            Assert.False(dialog.HasNoReleaseNotes);

            dialog.Close();
        }
        finally
        {
            app.Resources.MergedDictionaries.Remove(theme);
        }
    }

    [AvaloniaFact]
    public void ReleaseNotes_FallBackToReleaseUrl_WhenBodyMissing()
    {
        var dialog = new UpdateAvailableDialog(NewInfo());

        // 接口没给 body：不给空白区，退回发布页地址让用户自己去看全文
        Assert.False(dialog.HasReleaseNotes);
        Assert.True(dialog.HasNoReleaseNotes);
        Assert.Equal("https://example.invalid/release", dialog.ReleaseNotesText);
    }

    [AvaloniaFact]
    public void Buttons_HaveVisibleText_WithEnoughVerticalRoom()
    {
        // 主题资源键（ControlHeight=28、SpacingSmThk=12,12,12,12）来自 ThemeLight.axaml，
        // 而测试宿主是裸 Application、不带这份字典 ⇒ 手工并入。否则 Height/Padding 都退回
        // auto，按钮会被撑到够高，几何断言永远绿（本测试第一版就踩过这个坑）。
        var theme = new ResourceInclude(new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"))
        {
            Source = new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"),
        };
        var app = Application.Current!;
        app.Resources.MergedDictionaries.Add(theme);
        try
        {
            var dialog = new UpdateAvailableDialog(NewInfo());
            dialog.Show();
            dialog.Measure(new Size(560, 600));
            dialog.Arrange(new Rect(0, 0, 560, 600));
            dialog.UpdateLayout();

            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(3, buttons.Count);

            foreach (var button in buttons)
            {
                // 1) 文字内容真的存在（绑定没断）
                var content = Assert.IsType<string>(button.Content);
                Assert.False(string.IsNullOrWhiteSpace(content), "按钮文字为空");

                // 2) 文字真的渲染出来，且没有被内边距挤成一条缝
                var textBlocks = button.GetVisualDescendants().OfType<TextBlock>().ToList();
                Assert.NotEmpty(textBlocks);
                Assert.Contains(textBlocks, tb => !string.IsNullOrWhiteSpace(tb.Text));

                var verticalRoom = button.Bounds.Height - button.Padding.Top - button.Padding.Bottom;
                Assert.True(
                    verticalRoom >= 8,
                    $"按钮「{content}」可用高度只有 {verticalRoom:F1}px（Bounds={button.Bounds.Height:F1}，Padding 上下={button.Padding.Top:F1}+{button.Padding.Bottom:F1}），文字会被裁掉");
            }

            dialog.Close();
        }
        finally
        {
            app.Resources.MergedDictionaries.Remove(theme);
        }
    }

    [AvaloniaFact]
    public void Completion_ReturnsRemindLater_WhenClosedWithoutClickingButtons()
    {
        var dialog = new UpdateAvailableDialog(NewInfo());
        dialog.Show();
        dialog.Close();

        // 关窗兜底：调用方 await Completion 不能永久挂起
        Assert.True(dialog.Completion.IsCompleted);
        Assert.Equal(UpdatePromptChoice.RemindLater, dialog.Completion.Result);
    }
}
