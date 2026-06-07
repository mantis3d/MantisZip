using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// 已匹配密码查看对话框。
/// 显示已匹配的密码、描述和规则，支持显示/隐藏和复制。
/// </summary>
public partial class MatchedPasswordDialog : Window
{
    private readonly string _password;
    private bool _isRevealed;

    public MatchedPasswordDialog(string fileName, string password, string? description, List<string>? patterns)
    {
        InitializeComponent();
        _password = password;
        HeaderText.Text = L.TF(L.PwdMatched_Header, fileName);
        PwdPlainText.Text = password;

        if (patterns != null && patterns.Count > 0)
        {
            RulesSection.Visibility = Visibility.Visible;
            RulesText.Text = L.TF(L.PwdMatched_Rules, string.Join(", ", patterns));
        }

        if (!string.IsNullOrEmpty(description))
        {
            DescriptionSection.Visibility = Visibility.Visible;
            DescriptionText.Text = L.TF(L.PwdMatched_Description, description);
        }
    }

    private void PwdRevealBtn_Click(object sender, RoutedEventArgs e)
    {
        _isRevealed = !_isRevealed;
        PwdMaskedText.Visibility = _isRevealed ? Visibility.Collapsed : Visibility.Visible;
        PwdPlainText.Visibility = _isRevealed ? Visibility.Visible : Visibility.Collapsed;
        PwdRevealBtn.Content = _isRevealed ? "🙈" : "👁";
    }

    private void PwdCopyBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_password);
            var originalText = PwdCopyBtn.Content;
            PwdCopyBtn.Content = "✅";
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            timer.Tick += (_, _) =>
            {
                PwdCopyBtn.Content = "📋";
                timer.Stop();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            App.LogDebug("MatchedPasswordDialog.Copy: {0}", ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
