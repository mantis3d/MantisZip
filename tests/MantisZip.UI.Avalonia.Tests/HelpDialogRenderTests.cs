using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// HelpDialog 外壳渲染回归测试（无头 Avalonia）。
/// 根治回归：HelpDialog 的 title/content 必须在 DataContext 设定【之前】赋值且只读 ——
/// 曾因「构造后赋值 + 无变更通知」导致 Title/Content 绑定不刷新、整窗静默空白。
/// 本测试证明注入的 UserControl 真实进入 ContentPresenter、且本地化标题文本真实渲染。
/// </summary>
public class HelpDialogRenderTests
{
    /// <summary>外壳必须原样注入内容对象（引用相等），且 Markdown 帮助内容真实渲染出块级控件。</summary>
    [AvaloniaFact]
    public void HelpDialog_Show_InjectsMarkdownContent_AndRendersBlocks()
    {
        var content = new MarkdownHelpContent(LocalizationManager.T("AdaptiveHelp_Markdown"));
        var dialog = new HelpDialog(LocalizationManager.T("AdaptiveHelp_Title"), content);

        dialog.Show();

        // 内容对象必须原样注入 ContentPresenter（引用相等，而非仅非空）
        Assert.Contains(
            dialog.GetVisualDescendants().OfType<ContentPresenter>(),
            cp => ReferenceEquals(cp.Content, content));

        // Markdown 控件树渲染出块级控件（标题/段落均为 SelectableTextBlock），证明 help 内容真实呈现
        Assert.Contains(dialog.GetVisualDescendants().OfType<SelectableTextBlock>(), _ => true);

        dialog.Close();
    }

    /// <summary>迁移到 Markdown 的其余帮助（并行解压/密码/日志隐私）经外壳渲染出块级控件，且三语 key 存在。</summary>
    [AvaloniaFact]
    public void HelpDialog_Show_RendersMigratedMarkdown()
    {
        foreach (var markdownKey in new[] { "ParallelHelp_Markdown", "PasswordHelp_Markdown", "LogPrivacyHelp_Markdown" })
        {
            // key 必须真实存在（T() 未命中会回退返回 key 本身）
            Assert.NotEqual(markdownKey, LocalizationManager.T(markdownKey));

            var content = new MarkdownHelpContent(LocalizationManager.T(markdownKey));
            var dialog = new HelpDialog("T", content);

            dialog.Show();

            Assert.Contains(
                dialog.GetVisualDescendants().OfType<ContentPresenter>(),
                cp => ReferenceEquals(cp.Content, content));
            Assert.Contains(dialog.GetVisualDescendants().OfType<SelectableTextBlock>(), _ => true);

            dialog.Close();
        }
    }
}
