using System.Windows;
using System.Collections.Generic;
using MantisZip.Core;
using System.Linq;

namespace MantisZip.UI;

/// <summary>
/// 密码编辑对话框
/// </summary>
public partial class PasswordEditDialog : Window
{
    public string ResultPassword { get; private set; } = string.Empty;
    public string ResultDescription { get; private set; } = string.Empty;
    public List<string> ResultPatterns { get; private set; } = new();

    public PasswordEditDialog()
    {
        InitializeComponent();
    }

    public PasswordEditDialog(string id, string password, string description, string patterns)
    {
        InitializeComponent();
        PasswordBox.Text = password;
        DescriptionBox.Text = description;
        PatternsBox.Text = patterns;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Text;
        if (string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show("请输入密码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            PasswordBox.Focus();
            return;
        }

        ResultPassword = password;
        ResultDescription = DescriptionBox.Text?.Trim() ?? string.Empty;

        // 解析匹配规则
        ResultPatterns = PatternsBox.Text
            ?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList() ?? new List<string>();

        if (ResultPatterns.Count == 0)
        {
            // 默认使用密码作为匹配规则
            ResultPatterns.Add(password);
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}