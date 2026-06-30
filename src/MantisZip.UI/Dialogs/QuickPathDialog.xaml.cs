using System.Windows;
using MantisZip.UI.Controls;

namespace MantisZip.UI;

public partial class QuickPathDialog : Window
{
    /// <summary>Selected path (directory for save mode, full path for folder/file-open mode), or null if cancelled.</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>Selected filename in save mode (null for folder/file-open modes).</summary>
    public string? SelectedFileName
    {
        get => PathControl.FileName;
    }

    /// <summary>Whether to use folder mode (default true).</summary>
    public bool IsFolderMode
    {
        get => PathControl.IsFolderMode;
        set => PathControl.IsFolderMode = value;
    }

    /// <summary>Whether to use file-open mode.</summary>
    public bool IsFileOpenMode
    {
        get => PathControl.IsFileOpenMode;
        set => PathControl.IsFileOpenMode = value;
    }

    /// <summary>File filter for save mode.</summary>
    public string FileTypeFilter
    {
        get => PathControl.FileTypeFilter;
        set => PathControl.FileTypeFilter = value;
    }

    /// <summary>File filter for file-open mode.</summary>
    public string FileOpenFilter
    {
        get => PathControl.FileOpenFilter;
        set => PathControl.FileOpenFilter = value;
    }

    /// <summary>Default filename for save mode.</summary>
    public string DefaultFileName
    {
        get => PathControl.DefaultFileName;
        set => PathControl.DefaultFileName = value;
    }

    /// <summary>Initial path to show.</summary>
    public string InitialPath
    {
        get => PathControl.PathText;
        set => PathControl.PathText = value;
    }

    public QuickPathDialog()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathControl.PathText))
        {
            AppMessageBox.Show("请选择一个路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SelectedPath = PathControl.PathText;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}