using System.IO;
using System.Windows;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// 文件读取错误对话框：重试 / 跳过 / 中止，支持"L.T(L.Settings_Menu_Btn_Apply)到全部"。
/// Topmost 以确保不被其他窗口挡住。
/// </summary>
public partial class ErrorDialog : Window
{
    public FileErrorAction ResultAction { get; private set; }
    public bool ApplyToAll => ApplyAllCheck.IsChecked == true;

    public ErrorDialog(FileErrorInfo info)
    {
        InitializeComponent();
        HeaderText.Text = string.Format(L.T(L.Error_Header), $"“{Path.GetFileName(info.FilePath)}”");
        ErrorMsgText.Text = info.ErrorMessage;
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileErrorAction.Retry;
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileErrorAction.Skip;
        DialogResult = true;
    }

    private void Abort_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileErrorAction.Abort;
        DialogResult = true;
    }
}
