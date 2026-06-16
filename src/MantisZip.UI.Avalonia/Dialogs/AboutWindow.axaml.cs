using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MantisZip.UI.Avalonia.Dialogs;

public partial class AboutWindow : Window
{
    public string VersionText => "v0.4.0";

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
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
