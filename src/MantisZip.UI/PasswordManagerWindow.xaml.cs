using System.Windows;
using System.Collections.ObjectModel;
using MantisZip.Core;

namespace MantisZip.UI;

/// <summary>
/// 密码管理器窗口。密码默认以掩码显示，可切换明文。
/// </summary>
public partial class PasswordManagerWindow : Window
{
    private bool _showPasswords;

    public PasswordManagerWindow()
    {
        InitializeComponent();
        LoadPasswords(showPasswords: false);
    }

    private void LoadPasswords(bool showPasswords)
    {
        _showPasswords = showPasswords;
        var passwords = new ObservableCollection<PasswordEntryView>();

        foreach (var entry in PasswordManager.Instance.GetAllPasswords())
        {
            passwords.Add(new PasswordEntryView
            {
                Id = entry.Id,
                Password = showPasswords ? entry.Password : "••••••••",
                Description = entry.Description,
                PatternDisplay = string.Join(", ", entry.Patterns),
                CreatedAt = entry.CreatedAt,
                LastUsed = entry.LastUsed
            });
        }

        PasswordGrid.ItemsSource = passwords;
    }

    /// <summary>
    /// 切换密码明文/掩码显示。
    /// </summary>
    private void ToggleShowPasswords_Click(object sender, RoutedEventArgs e)
    {
        var show = !_showPasswords;
        LoadPasswords(show);
        ShowPwdBtn.Content = show ? "👁 隐藏密码" : "👁 显示密码";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordEditDialog();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            PasswordManager.Instance.AddPassword(dialog.ResultPassword, dialog.ResultDescription, dialog.ResultPatterns);
            LoadPasswords(_showPasswords);
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordGrid.SelectedItem is not PasswordEntryView entry) return;

        var dialog = new PasswordEditDialog(entry.Id, entry.Password, entry.Description, entry.PatternDisplay);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            PasswordManager.Instance.UpdatePassword(entry.Id, dialog.ResultPassword, dialog.ResultDescription, dialog.ResultPatterns);
            LoadPasswords(_showPasswords);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordGrid.SelectedItem is not PasswordEntryView entry) return;

        var result = MessageBox.Show(
            $"确定要删除密码 \"{entry.Password}\" 吗？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            PasswordManager.Instance.DeletePassword(entry.Id);
            LoadPasswords(_showPasswords);
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordHelpDialog();
        dialog.Owner = this;
        dialog.ShowDialog();
    }
}

public class PasswordEntryView
{
    public string Id { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PatternDisplay { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsed { get; set; }
}