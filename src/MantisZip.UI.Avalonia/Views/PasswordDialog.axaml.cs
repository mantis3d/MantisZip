using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Views;

public partial class PasswordDialog : Window
{
    public string? Password { get; private set; }
    public bool RememberInSession { get; private set; } = true;
    public string FileName { get; set; } = "";

    public string DialogTitle => LocalizationManager.T("Password_Title");
    public string PromptText => string.IsNullOrEmpty(FileName)
        ? LocalizationManager.T("Password_Prompt")
        : $"{LocalizationManager.T("Password_Prompt")}\n\n{FileName}";
    public string PasswordPlaceholder => LocalizationManager.T("Password_Placeholder");
    public string RememberText => LocalizationManager.T("Password_Remember");
    public string OkText => LocalizationManager.T("Password_Ok");
    public string CancelText => LocalizationManager.T("Password_Cancel");

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
