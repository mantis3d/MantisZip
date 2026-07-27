using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MantisZip.Core;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Views;

public partial class PasswordDialog : Window
{
    public string? Password { get; private set; }
    public bool RememberInSession { get; private set; } = true;
    public bool SavePermanently { get; private set; }
    public string? Description { get; private set; }
    public List<string> Patterns { get; private set; } = new();

    public string FileName { get; set; } = "";

    // ── Localized strings ──
    public string DialogTitle => LocalizationManager.T("Password_Title");
    public string PasswordPlaceholder => LocalizationManager.T("Password_Placeholder");
    public string RememberText => LocalizationManager.T("Password_Remember");
    public string SavePermanentlyText => LocalizationManager.T("Password_SavePermanently");
    public string DescriptionLabelText => LocalizationManager.T("Password_DescriptionLabel");
    public string DescriptionWatermark => LocalizationManager.T("Password_DescriptionWatermark");
    public string MatchRulesLabelText => LocalizationManager.T("Password_MatchRulesLabel");
    public string MatchRulesWatermark => LocalizationManager.T("Password_MatchRulesWatermark");
    public string NewPasswordOptionText => LocalizationManager.T("Password_NewPasswordOption");
    public string OkText => LocalizationManager.T("Password_Ok");
    public string CancelText => LocalizationManager.T("Password_Cancel");

    private bool _isUpdatingPasswordSelection;

    public PasswordDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public PasswordDialog(string fileName) : this()
    {
        FileName = fileName;
        FileNameText.Text = fileName;
        LoadSavedPasswords();
    }

    private void LoadSavedPasswords()
    {
        try
        {
            var entries = PasswordManager.Instance.GetAllPasswords();
            foreach (var entry in entries)
            {
                var display = !string.IsNullOrEmpty(entry.Description)
                    ? $"{entry.Description} — {entry.Password}"
                    : entry.Password;
                var item = new ComboBoxItem
                {
                    Content = display,
                    Tag = entry
                };
                PasswordSelector.Items.Add(item);
            }
        }
        catch
        {
            // PasswordManager unavailable — skip saved passwords list
        }
    }

    private void PasswordSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPasswordSelection) return;
        if (PasswordSelector.SelectedItem is ComboBoxItem item && item.Tag is not string tagStr)
        {
            // Saved password selected: fill in the password and mark as "remember permanently"
            if (item.Tag is PasswordEntry entry)
            {
                _isUpdatingPasswordSelection = true;
                PasswordBox.Text = entry.Password;
                PasswordBox.PasswordChar = '●';
                SavePermanentlyCheck.IsChecked = true;
                RememberCheck.IsChecked = true;
                SaveOptionsPanel.IsVisible = true;
                DescTextBox.Text = entry.Description ?? "";
                PatternsTextBox.Text = string.Join(", ", entry.Patterns);
                _isUpdatingPasswordSelection = false;
            }
        }
        else
        {
            // "New password" option selected — clear fields
            _isUpdatingPasswordSelection = true;
            PasswordBox.Text = "";
            PasswordBox.PasswordChar = '●';
            _isUpdatingPasswordSelection = false;
        }
    }

    private void OnRevealToggle(object? sender, RoutedEventArgs e)
    {
        if (PasswordBox.PasswordChar == '\0')
            PasswordBox.PasswordChar = '●';
        else
            PasswordBox.PasswordChar = '\0';
    }

    private void OnRememberChanged(object? sender, RoutedEventArgs e)
    {
        SaveOptionsPanel.IsVisible = RememberCheck.IsChecked == true;
        if (RememberCheck.IsChecked != true)
        {
            SavePermanentlyCheck.IsChecked = false;
        }
    }

    private void PasswordBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnOkClick(sender, e);
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Password = PasswordBox.Text;
        RememberInSession = RememberCheck.IsChecked == true;
        SavePermanently = SavePermanentlyCheck.IsChecked == true && RememberInSession;
        Description = SavePermanently ? DescTextBox.Text?.Trim() : null;
        var patternsText = SavePermanently ? PatternsTextBox.Text?.Trim() : null;
        Patterns = !string.IsNullOrEmpty(patternsText)
            ? patternsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string>();

        if (string.IsNullOrEmpty(Password))
        {
            Password = null;
            Close(false);
            return;
        }

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Password = null;
        Close(false);
    }
}
