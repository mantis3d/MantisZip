using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 统一帮助弹窗外壳 —— 标题 + 可滚动/可注入内容 + 关闭按钮。
/// 可复用：调用方经构造函数注入 title/content（closeText 可选）。
/// 关键：属性在 DataContext 设定【之前】赋值、且只读 —— 根治「构造后再赋值 + 无变更通知
/// → 绑定不刷新 → 整窗空白」的时序坑。
/// </summary>
public partial class HelpDialog : Window
{
    public string HelpTitle { get; }
    public object? HelpContent { get; }
    public string CloseText { get; }

    /// <summary>
    /// XAML 编译器要求：带 <c>x:Class</c> 的类型必须具备公共无参构造（否则报 AVLN3000）。
    /// 运行时请使用带参构造注入内容；本构造仅满足编译期/设计器需要。
    /// </summary>
    public HelpDialog() : this(string.Empty, null) { }

    public HelpDialog(string title, object? content, string? closeText = null)
    {
        HelpTitle = title;
        HelpContent = content;
        CloseText = closeText ?? LocalizationManager.T("MsgBox_Close");
        InitializeComponent();
        DataContext = this;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
