using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = new SettingsWindowViewModel();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsWindowViewModel vm)
            vm.SaveCommand.Execute(null);

        // 刷新全局字体、紧凑度等需要立即生效的设置
        App.RefreshAppFontFamily();
        App.RefreshCompactness();

        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private MetadataPanelSettingsViewModel? GetMetadataPanelVM()
    {
        return (DataContext as SettingsWindowViewModel)?.MetadataPanelSettings;
    }

    private void OnPreviewFieldMoveUp(object sender, RoutedEventArgs e)
    {
        App.DebugLog($"[Preview] MoveUp CLICKED sender={sender?.GetType().Name}");
        if (sender is Button btn)
        {
            App.DebugLog($"[Preview] MoveUp DataContext={btn.DataContext?.GetType().Name}, Key={(btn.DataContext is FormatMetadataItem f ? f.Key : "N/A")}");
            if (btn.DataContext is FormatMetadataItem item)
            {
                GetMetadataPanelVM()?.MoveFieldUp(item.Key);
            }
        }
    }

    private void OnPreviewFieldMoveDown(object sender, RoutedEventArgs e)
    {
        App.DebugLog($"[Preview] MoveDown CLICKED");
        if (sender is Button btn && btn.DataContext is FormatMetadataItem item)
        {
            GetMetadataPanelVM()?.MoveFieldDown(item.Key);
        }
    }

    private void OnPreviewFieldTapped(object sender, TappedEventArgs e)
    {
        App.DebugLog($"[Preview] Tapped sender={sender?.GetType().Name}");
        if (sender is Button btn && btn.DataContext is FormatMetadataItem item)
        {
            if (btn.Content?.ToString() == "˄")
                GetMetadataPanelVM()?.MoveFieldUp(item.Key);
            else
                GetMetadataPanelVM()?.MoveFieldDown(item.Key);
        }
    }
}
