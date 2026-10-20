using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// MarkdownPreviewBuilder 章节分隔回归：非首个块的标题应有顶部留白，
/// 首块（文档最上方）不留白。也用于帮助弹窗的 Markdown 正文排版。
/// </summary>
public class MarkdownPreviewBuilderTests
{
    [AvaloniaFact]
    public void Build_NonFirstHeading_HasTopMargin_FirstBlockNone()
    {
        // 块序列：段落(intro) → H2 → 段落 → H2 → 段落
        var panel = MarkdownPreviewBuilder.Build("intro\n\n## A\ntext A\n\n## B\ntext B");

        Assert.True(panel.Children.Count >= 5);

        // 首个块（intro 段落）不得有顶部留白
        Assert.Equal(0, panel.Children[0].Margin.Top);

        // 至少一个标题块带顶部留白（两个 H2 都应带）
        var headingsWithGap = panel.Children.Count(c => c.Margin.Top > 0);
        Assert.Equal(2, headingsWithGap);
    }

    [AvaloniaFact]
    public void Build_HeadingAsFirstBlock_HasNoTopMargin()
    {
        // 密码帮助的 Markdown 以 `## 标题` 开头 → 首块标题不留白（避免顶部多余空隙）
        var panel = MarkdownPreviewBuilder.Build("## Title\n\nbody");

        Assert.True(panel.Children.Count >= 2);
        Assert.Equal(0, panel.Children[0].Margin.Top);
    }
}
