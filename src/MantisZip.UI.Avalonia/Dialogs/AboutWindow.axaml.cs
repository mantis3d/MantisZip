using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MantisZip.UI.Avalonia.Services;
using System.Collections.Generic;

namespace MantisZip.UI.Avalonia.Dialogs;

public partial class AboutWindow : Window
{
    public string VersionText => "v0.4.0";

    public Dictionary<string, string> LocalizedStrings { get; }

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
        LocalizedStrings = new Dictionary<string, string>
        {
            ["About_Title"] = LocalizationManager.T("About_Title"),
            ["About_Technology"] = LocalizationManager.T("About_Technology"),
            ["About_License"] = LocalizationManager.T("About_License"),
            ["About_Close"] = LocalizationManager.T("About_Close"),
        };
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnLicenseClick(object? sender, RoutedEventArgs e)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher != null)
        {
            await launcher.LaunchUriAsync(new Uri("https://github.com/mantis3d/MantisZip"));
        }
    }
}
