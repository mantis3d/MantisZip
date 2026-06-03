using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MantisZip.Core;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// 密码输入对话框。
/// 下拉框只显示密码描述，选中后填入 PasswordBox。
/// 支持保存到密码库：描述 + 匹配规则（自动填入文件名）。
/// </summary>
public partial class PasswordDialog : Window
{
    public string FileName { get; set; } = string.Empty;
    public string? ResultPassword { get; private set; }
    public bool RememberPassword { get; private set; }
    public string? Description => DescTextBox.Text;
    public List<string> Patterns { get; private set; } = new();

    public PasswordDialog()
    {
        InitializeComponent();
    }

    public PasswordDialog(string fileName)
    {
        InitializeComponent();
        FileName = fileName;
        FileNameText.Text = fileName;
        // 自动将文件名填入匹配规则第一行
        var nameOnly = Path.GetFileName(fileName);
        PatternsTextBox.Text = nameOnly;
        LoadSavedPasswords(fileName);

        // 根据设置决定默认是否显示明文
        if (AppSettings.Instance.PasswordRevealByDefault)
        {
            _isPasswordRevealed = true;
            PasswordPlainBox.Text = "";
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordPlainBox.Visibility = Visibility.Visible;
            PwdRevealBtn.Content = "🙈";
        }
    }

    private void LoadSavedPasswords(string fileName)
    {
        PasswordSelector.Items.Clear();
        PasswordSelector.Items.Add(new ComboBoxItem { Content = L.T(L.Pwd_NewPasswordOption) });

        var matches = PasswordManager.Instance.FindMatchingPasswords(fileName);
        if (matches.Count > 0)
        {
            foreach (var entry in matches)
            {
                PasswordSelector.Items.Add(new ComboBoxItem
                {
                    Content = $"🔑 {entry.Description}",
                    Tag = entry
                });
            }
            PasswordSelector.Items.Add(new Separator());
        }

        foreach (var entry in PasswordManager.Instance.GetAllPasswords())
        {
            var display = !string.IsNullOrEmpty(entry.Description)
                ? entry.Description
                : L.T(L.Pwd_NoDescription);
            PasswordSelector.Items.Add(new ComboBoxItem
            {
                Content = display,
                Tag = entry
            });
        }

        if (PasswordSelector.Items.Count > 0)
            PasswordSelector.SelectedIndex = 0;
    }

    private void PasswordSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PasswordSelector.SelectedItem is ComboBoxItem item && item.Tag is PasswordEntry entry)
        {
            PasswordBox.Password = entry.Password;
            if (PasswordPlainBox.Visibility == Visibility.Visible)
                PasswordPlainBox.Text = entry.Password;
            // 选中已保存密码时自动勾选并填入描述
            RememberCheckBox.IsChecked = true;
            DescTextBox.Text = entry.Description;
            PatternsTextBox.Text = string.Join("\n", entry.Patterns);
        }
        else
        {
            PasswordBox.Password = "";
            if (PasswordPlainBox.Visibility == Visibility.Visible)
                PasswordPlainBox.Text = "";
        }
    }

    private bool _isPasswordRevealed;

    public bool PasswordRevealed => _isPasswordRevealed;

    private void PwdRevealBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            PasswordPlainBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordPlainBox.Visibility = Visibility.Visible;
            PasswordPlainBox.Focus();
            PasswordPlainBox.Select(PasswordPlainBox.Text.Length, 0);
            PwdRevealBtn.Content = "🙈";
        }
        else
        {
            PasswordBox.Password = PasswordPlainBox.Text;
            PasswordPlainBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordBox.Focus();
            PwdRevealBtn.Content = "👁";
        }
    }

    private void RememberCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (RememberCheckBox.IsChecked == true && string.IsNullOrEmpty(DescTextBox.Text))
        {
            DescTextBox.Text = Path.GetFileNameWithoutExtension(FileName);
        }
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Ok_Click(sender, e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var password = _isPasswordRevealed
            ? PasswordPlainBox.Text
            : PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            AppMessageBox.Show(L.T(L.Pwd_Validation_Required), "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultPassword = password;
        RememberPassword = RememberCheckBox.IsChecked == true;
        Patterns = PatternsTextBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
