using System.IO;
using System.Windows;
using MantisZip.UI.Localization;

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

    /// <summary>用户输入的自定义文件名（未修改时返回建议名）</summary>
    public string? CustomName => RenameTextBox.Text;

    /// <param name="filePath">目标文件路径</param>
    /// <param name="canAdd">L.T(L.MsgBox_Yes)L.T(L.MsgBox_No)支持L.T(L.CompressConflict_Add)（Tar 不支持）</param>
    /// <param name="suggestedName">L.T(L.CompressConflict_Rename)的建议名（不含路径），用于预填输入框</param>
    public CompressConflictDialog(string filePath, bool canAdd, string? suggestedName = null)
    {
        InitializeComponent();
        HeaderText.Text = string.Format(L.T(L.CompressConflict_Header), $"“{Path.GetFileName(filePath)}”");

        // 预填L.T(L.CompressConflict_Rename)的建议名（由调用方预计算）
        RenameTextBox.Text = suggestedName ?? Path.GetFileName(filePath);

        if (!canAdd)
        {
            AddBtn.IsEnabled = false;
            AddBtn.ToolTip = L.T(L.CompressConflict_Tooltip_NoAdd);
        }
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("CompressConflictDialog: user chose Overwrite for '{0}'", HeaderText.Text);
        ResultAction = CompressConflictAction.Overwrite;
        DialogResult = true;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("CompressConflictDialog: user chose Add for '{0}'", HeaderText.Text);
        ResultAction = CompressConflictAction.Add;
        DialogResult = true;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("CompressConflictDialog: user chose Rename for '{0}', customName='{1}'", HeaderText.Text, RenameTextBox.Text);
        ResultAction = CompressConflictAction.Rename;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("CompressConflictDialog: user cancelled for '{0}'", HeaderText.Text);
        DialogResult = false;
    }
}
