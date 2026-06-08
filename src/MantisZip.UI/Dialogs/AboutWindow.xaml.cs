using System;
using System.Diagnostics;
using System.Windows;

namespace MantisZip.UI;

/// <summary>
/// 关于窗口，展示应用信息、作者、依赖库和致谢。
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = "v" + AppConstants.Version;
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.LogDebug("AboutWindow: failed to open {0}: {1}", e.Uri, ex.Message);
        }
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
