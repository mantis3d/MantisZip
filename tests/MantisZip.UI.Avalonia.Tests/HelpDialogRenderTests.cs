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
    /// <summary>外壳必须原样注入内容对象（引用相等），并渲染内容控件的本地化标题。</summary>
    [AvaloniaFact]
    public void HelpDialog_Show_InjectsContentPresenter_AndRendersContentTitle()
    {
        var content = new AdaptiveHelpContent();
        var dialog = new HelpDialog("T", content);

        dialog.Show();

        // 内容对象必须原样注入 ContentPresenter（引用相等，而非仅非空）
        Assert.Contains(
            dialog.GetVisualDescendants().OfType<ContentPresenter>(),
            cp => ReferenceEquals(cp.Content, content));

        // 渲染文本必须包含内容控件的本地化标题（证明绑定在 attach 后求值成功）
        var texts = dialog.GetVisualDescendants().OfType<TextBlock>()
            .Select(tb => tb.Text ?? string.Empty)
            .ToList();
        Assert.Contains(LocalizationManager.T("Adaptive_Help_Title"), texts);

        dialog.Close();
    }

    /// <summary>已迁移的并行解压帮助内容必须经外壳渲染出其章节标题（与窗口标题同一本地化 key）。</summary>
    [AvaloniaFact]
    public void HelpDialog_Show_RendersMigratedContentTitle()
    {
        var content = new ParallelExtractHelpContent();
        var dialog = new HelpDialog(LocalizationManager.T("ParallelHelp_Title"), content);

        dialog.Show();

        Assert.Contains(
            dialog.GetVisualDescendants().OfType<ContentPresenter>(),
            cp => ReferenceEquals(cp.Content, content));

        var texts = dialog.GetVisualDescendants().OfType<TextBlock>()
            .Select(tb => tb.Text ?? string.Empty)
            .ToList();
        Assert.Contains(LocalizationManager.T("ParallelHelp_Title"), texts);

        dialog.Close();
    }
}
