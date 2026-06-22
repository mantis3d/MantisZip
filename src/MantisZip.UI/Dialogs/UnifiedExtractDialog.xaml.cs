using System.Windows;
using MantisZip.Core.Utils;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI;

public partial class UnifiedExtractDialog : Window
{
    public string SelectedPath => PathControl.PathText;
    public Core.Abstractions.FileConflictAction ConflictAction { get; private set; } = Core.Abstractions.FileConflictAction.Overwrite;
    public bool PreserveDirectoryRoot => PreserveRootCheck.IsChecked == true;

    /// <summary>Pre-set the target path (e.g. extract to archive name folder).</summary>
    public string PresetPath
    {
        get => PathControl.PathText;
        set => PathControl.PathText = value;
    }

    public UnifiedExtractDialog()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathControl.PathText))
        {
            AppMessageBox.Show("请选择解压目标路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        switch (ConflictCombo.SelectedIndex)
        {
            case 0: ConflictAction = Core.Abstractions.FileConflictAction.Overwrite; break;
            case 1: ConflictAction = Core.Abstractions.FileConflictAction.Skip; break;
            case 2: ConflictAction = Core.Abstractions.FileConflictAction.Rename; break;
            case 3: ConflictAction = Core.Abstractions.FileConflictAction.OverwriteIfOlder; break;
            case 4: ConflictAction = Core.Abstractions.FileConflictAction.OverwriteIfSmaller; break;
        }

        PathHistoryManager.Record(PathControl.PathText);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}