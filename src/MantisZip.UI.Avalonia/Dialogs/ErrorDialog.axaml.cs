using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 文件读取错误对话框：重试 / 跳过 / 中止，支持"应用到全部"。
/// </summary>
public partial class ErrorDialog : Window
{
    /// <summary>用户选择的处理方式</summary>
    public FileErrorAction ResultAction { get; private set; } = FileErrorAction.Retry;
    /// <summary>用户是否勾选了"应用到全部"</summary>
    public bool ApplyToAll => ApplyAllCheck.IsChecked == true;

    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T("Error_Title");
    public string RetryText => LocalizationManager.T("Error_Retry");
    public string SkipText => LocalizationManager.T("Error_Skip");
    public string AbortText => LocalizationManager.T("Error_Abort");
    public string ApplyAllText => LocalizationManager.T("Error_ApplyToAll");

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="ErrorDialog(FileErrorInfo)"/>。
    /// </summary>
    public ErrorDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ErrorDialog(FileErrorInfo info)
    {
        InitializeComponent();
        DataContext = this;

        HeaderText.Text = string.Format(LocalizationManager.T("Error_Header"), $"\"{Path.GetFileName(info.FilePath)}\"");
        ErrorMsgText.Text = info.ErrorMessage;
    }

    private void Retry_Click(object? sender, RoutedEventArgs e)
    {
        ResultAction = FileErrorAction.Retry;
        Close(true);
    }

    private void Skip_Click(object? sender, RoutedEventArgs e)
    {
        ResultAction = FileErrorAction.Skip;
        Close(true);
    }

    private void Abort_Click(object? sender, RoutedEventArgs e)
    {
        ResultAction = FileErrorAction.Abort;
        Close(true);
    }
}
