using System;
using System.Diagnostics;
using System.Windows;

namespace MantisZip.UI;

/// <summary>
/// 捐赠支持窗口，展示打赏二维码和第三方平台链接。
/// </summary>
public partial class DonationDialog : Window
{
    public DonationDialog()
    {
        InitializeComponent();
    }

    private void OpenAfdian_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://afdian.com/a/mantiszip");
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/sponsors/mantiszip");
    }

    private void OpenBuyMeACoffee_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://buymeacoffee.com/mantiszip");
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.LogDebug("DonationDialog.OpenUrl: failed to open {0}: {1}", url, ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
