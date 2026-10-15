using Avalonia.Controls;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// Markdown 驱动的帮助内容控件（配合 <see cref="HelpDialog"/> 外壳使用）。
/// <para>
/// 把本地化 Markdown 文本交给 <see cref="MarkdownPreviewBuilder.Build"/> 渲染成原生 Avalonia 控件树
/// （标题/段落/列表/表格/代码块/引用块；段落为 <c>SelectableTextBlock</c>，文本可选中复制）。
/// 相比手写卡片式 <c>UserControl</c>，写帮助文档只需维护三语 Markdown 文案，无需改 XAML。
/// </para>
/// </summary>
public partial class MarkdownHelpContent : UserControl
{
    /// <summary>
    /// XAML 编译器要求：带 <c>x:Class</c> 的类型必须具备公共无参构造（否则报 AVLN3000）。
    /// 运行时请使用带 markdown 的构造；本构造仅满足编译期/设计器需要。
    /// </summary>
    public MarkdownHelpContent() : this(string.Empty) { }

    public MarkdownHelpContent(string markdown)
    {
        InitializeComponent();
        MarkdownHost.Content = MarkdownPreviewBuilder.Build(markdown ?? string.Empty);
    }
}
