using System.Windows;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI;

/// <summary>
/// 文件冲突对话框：覆盖 / 重命名 / 跳过，支持"应用到全部"。
/// Topmost 以确保不被进度窗口挡住。
/// </summary>
public partial class ConflictDialog : Window
{
    public FileConflictAction ResultAction { get; private set; }
    public bool ApplyToAll => ApplyAllCheck.IsChecked == true;

    public ConflictDialog(string fileName)
    {
        InitializeComponent();
        FileNameText.Text = $"“{fileName}”";
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileConflictAction.Overwrite;
        DialogResult = true;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileConflictAction.Rename;
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileConflictAction.Skip;
        DialogResult = true;
    }
}
