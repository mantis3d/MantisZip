using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

public partial class CommentDialog : Window
{
    public string? Comment { get; private set; }

    public string DialogTitle => LocalizationManager.T("Comment_Title");
    public string PlaceholderText => LocalizationManager.T("Comment_Placeholder");
    public string SaveText => LocalizationManager.T("Comment_Save");
    public string CancelText => LocalizationManager.T("Comment_Cancel");

    public CommentDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public CommentDialog(string? existingComment) : this()
    {
        CommentTextBox.Text = existingComment ?? "";
        CommentTextBox.SelectionStart = CommentTextBox.Text?.Length ?? 0;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        Comment = CommentTextBox.Text;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
