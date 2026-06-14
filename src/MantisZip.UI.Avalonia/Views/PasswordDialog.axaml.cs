using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MantisZip.UI.Avalonia.Views;

public partial class PasswordDialog : Window
{
    public string? Password { get; private set; }
    public bool RememberInSession { get; private set; } = true;
    public string FileName { get; set; } = "";

    public PasswordDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public PasswordDialog(string fileName) : this()
    {
        FileName = fileName;
    }

    private void OnRevealToggle(object? sender, RoutedEventArgs e)
    {
        // Toggle between masked and plain text
        if (PasswordBox.PasswordChar == '\0')
            PasswordBox.PasswordChar = '●';
        else
            PasswordBox.PasswordChar = '\0';
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Password = PasswordBox.Text;
        RememberInSession = RememberCheck.IsChecked == true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Password = null;
        Close(false);
    }
}
