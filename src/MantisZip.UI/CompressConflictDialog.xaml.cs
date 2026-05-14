using System.IO;
using System.Windows;

namespace MantisZip.UI;

public enum CompressConflictAction
{
    Overwrite,
    Add,
    Rename,
    Cancel
}

public partial class CompressConflictDialog : Window
{
    public CompressConflictAction ResultAction { get; private set; } = CompressConflictAction.Cancel;

    /// <param name="canAdd">是否支持"添加到压缩包"（Tar 不支持）</param>
    public CompressConflictDialog(string filePath, bool canAdd)
    {
        InitializeComponent();
        FileNameText.Text = $"“{Path.GetFileName(filePath)}”";
        if (!canAdd)
        {
            AddBtn.IsEnabled = false;
            AddBtn.ToolTip = "此格式不支持添加文件";
        }
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = CompressConflictAction.Overwrite;
        DialogResult = true;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = CompressConflictAction.Add;
        DialogResult = true;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = CompressConflictAction.Rename;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
