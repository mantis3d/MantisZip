using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Dialogs;
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
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsWindowViewModel vm)
            vm.SaveCommand.Execute(null);

        // 刷新全局字体、紧凑度、主题、调试日志开关等需要立即生效的设置
        App.RefreshAppFontFamily();
        App.RefreshCompactness();
        App.RefreshTheme();
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

    // 自定义格式：编辑
    private void EditCustomFormat_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is FormatDefinitionViewModel fmt)
        {
            var vm = DataContext as SettingsWindowViewModel;
            vm?.EditCustomFormat(fmt, this);
        }
    }

    // 自定义格式：删除
    private void DeleteCustomFormat_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is FormatDefinitionViewModel fmt)
        {
            var vm = DataContext as SettingsWindowViewModel;
            vm?.DeleteCustomFormat(fmt);
        }
    }

    // 自适应规则：编辑
    private void EditRule_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AdaptiveOverrideRuleViewModel rule)
        {
            var vm = DataContext as SettingsWindowViewModel;
            vm?.EditRule(rule, this);
        }
    }

    // 自适应规则：删除
    private void DeleteRule_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AdaptiveOverrideRuleViewModel rule)
        {
            var vm = DataContext as SettingsWindowViewModel;
            vm?.DeleteRule(rule);
        }
    }

    private void OnAdaptiveHelpClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new HelpDialog
        {
            HelpTitle = LocalizationManager.T("Adaptive_Help_Title"),
            HelpContent = new AdaptiveHelpContent(),
        };
        dialog.ShowDialog(this);
    }
}

public class PositionDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string pos)
            return FieldEditItem.GetPositionDisplayName(pos);
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value;
}
