using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using System.Globalization;

namespace MantisZip.UI.Avalonia.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = new SettingsWindowViewModel();

        // 手动 light dismiss：Popup 遮罩会拦截外部点击导致按钮收不到 Click，
        // 改为监听窗口任意 PointerPressed——先关闭快捷浮层，按钮 Click 再打开
        AddHandler(InputElement.PointerPressedEvent,
            (_, _) => CustomPathQuickPopup.IsOpen = false,
            RoutingStrategies.Tunnel);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsWindowViewModel vm)
            vm.SaveCommand.Execute(null);

        // 刷新全局字体、紧凑度、调试日志开关等需要立即生效的设置
        App.RefreshAppFontFamily();
        App.RefreshCompactness();
        App.RefreshDebugLogSettings();

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
        // Kept for future use
    }

    // ── 自定义路径 QuickPath ────────────────────────────────────────────────

    private void CustomPathQuickButton_Click(object? sender, RoutedEventArgs e)
    {
        // 打开 Popup 前刷新数据源（收藏/历史可能已变化）
        CustomPathQuickControl.RefreshSources();
        CustomPathQuickPopup.IsOpen = true;
    }

    /// <summary>QuickPathControl 选中路径 → 写入自定义路径并关闭浮层。</summary>
    private void CustomPathQuickControl_PathSelected(object? sender, string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (DataContext is SettingsWindowViewModel vm)
            vm.CustomPath = path;
        CustomPathQuickPopup.IsOpen = false;
    }
}

public class PositionDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string pos)
            return LocalizationManager.T($"Metadata_Position{pos}");
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value;
}
