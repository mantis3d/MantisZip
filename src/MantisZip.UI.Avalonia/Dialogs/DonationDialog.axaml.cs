using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 捐赠支持窗口，展示打赏二维码和第三方平台链接。
/// </summary>
public partial class DonationDialog : Window
{
    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T("Donate_Title");
    public string HeaderText => LocalizationManager.T("Donate_Header");
    public string QrHintText => LocalizationManager.T("Donate_QrHint");
    public string PlatformsHintText => LocalizationManager.T("Donate_Platforms_Hint");
    public string CloseText => LocalizationManager.T("Donate_Close");
    public string AfdianText => LocalizationManager.T("Donate_Platform_Afdian");
    public string PolarText => LocalizationManager.T("Donate_Platform_Polar");

    public DonationDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void OpenAfdian_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://afdian.com/a/MantisZen");
    }

    private void Polar_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://buy.polar.sh/polar_cl_VaCaW2l2nWkob5CyHe4dOlhL6HrQDK4ueMA9n1JyhNc");
    }

    private static void OpenUrl(string url)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            process.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DonationDialog.OpenUrl: failed to open {url}: {ex.Message}");
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
