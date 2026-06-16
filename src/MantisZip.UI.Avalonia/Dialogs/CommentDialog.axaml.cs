using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MantisZip.UI.Avalonia.Dialogs;

public partial class CommentDialog : Window
{
    public string? Comment { get; private set; }

    public CommentDialog(string? existingComment)
    {
        InitializeComponent();
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
