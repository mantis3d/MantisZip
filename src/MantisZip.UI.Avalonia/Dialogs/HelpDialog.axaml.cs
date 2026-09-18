using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 统一帮助弹窗外壳 — 显示动态内容 + 关闭按钮。
/// 调用方通过 HelpContent 属性注入 UserControl。
/// </summary>
public partial class HelpDialog : Window
{
    public string HelpTitle { get; set; } = "";
    public string CloseText => LocalizationManager.T("MsgBox_Close");
    public object? HelpContent { get; set; }

    public HelpDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
