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
        OpenUrl("https://afdian.com/a/MantisZen");
    }

    private void Polar_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://buy.polar.sh/polar_cl_VaCaW2l2nWkob5CyHe4dOlhL6HrQDK4ueMA9n1JyhNc");
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
