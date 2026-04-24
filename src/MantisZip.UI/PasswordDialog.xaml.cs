using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MantisZip.Core;

namespace MantisZip.UI;

/// <summary>
/// 密码输入对话框
/// </summary>
public partial class PasswordDialog : Window
{
    public string FileName { get; set; } = string.Empty;
    public string? ResultPassword { get; private set; }
    public bool RememberPassword { get; private set; } = true;

    public PasswordDialog()
    {
        InitializeComponent();
    }

    public PasswordDialog(string fileName)
    {
        InitializeComponent();
        FileName = fileName;
        LoadSavedPasswords(fileName);
    }

    private void LoadSavedPasswords(string fileName)
    {
        PasswordCombo.Items.Clear();

        // 加载匹配的密码
        var matches = PasswordManager.Instance.FindMatchingPasswords(fileName);

        if (matches.Count > 0)
        {
            // 添加匹配的密码（高亮显示）
            foreach (var entry in matches)
            {
                var item = new ComboBoxItem
                {
                    Content = $"🔑 {entry.Description}  ({entry.Password})",
                    Tag = entry.Id
                };
                PasswordCombo.Items.Add(item);
            }

            PasswordCombo.Items.Add(new Separator());
        }

        // 添加所有保存的密码
        var all = PasswordManager.Instance.GetAllPasswords();
        foreach (var entry in all)
        {
            var item = new ComboBoxItem
            {
                Content = entry.Description != "" 
                    ? $"{entry.Description}: {entry.Password}" 
                    : entry.Password,
                Tag = entry.Id
            };
            PasswordCombo.Items.Add(item);
        }

        // 输入新密码选项
        PasswordCombo.Items.Add(new ComboBoxItem { Content = "(输入新密码)" });

        // 默认选择第一个
        if (PasswordCombo.Items.Count > 0)
        {
            PasswordCombo.SelectedIndex = 0;
        }
    }

    private void PasswordCombo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_Click(sender, e);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var text = PasswordCombo.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show("请输入密码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultPassword = text;
        RememberPassword = RememberCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}