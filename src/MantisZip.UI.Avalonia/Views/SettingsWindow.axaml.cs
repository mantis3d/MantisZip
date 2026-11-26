using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Controls;
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

        // 并行解压帮助弹窗：VM 发起请求 → code-behind 弹窗（规则：对话框经回调委托与 View 解耦）
        if (DataContext is SettingsWindowViewModel vm)
        {
            vm.ShowParallelHelpRequested += () =>
            {
                var dlg = new HelpDialog(LocalizationManager.T("ParallelHelp_Title"), new MarkdownHelpContent(LocalizationManager.T("ParallelHelp_Markdown")))
                {
                    Width = 520,
                    Height = 460
                };
                dlg.ShowDialog(this);
            };
        }

        // 恢复上次关闭时保存的窗口尺寸（2026-10-30；主窗口走 WindowStateManager，这里只记尺寸）
        var geometry = AppSettings.Load();
        WindowPlacement.Apply(this, geometry.SettingsWindowWidth, geometry.SettingsWindowHeight);
        Closed += OnClosed;
    }

    /// <summary>
    /// 三行文件名模板的预设选择器回调（控件只发事件，写设置归 VM）。
    /// 行身份靠控件 <c>Tag</c>（Dialog/Quick/Separate），这样三个 handler 只需写一份。
    /// </summary>
    private void OnPresetSelected(object? sender, MantisZip.Core.Utils.FileNameTemplatePreset preset)
    {
        if (sender is not TemplatePresetPicker { Tag: string tag }) return;
        if (DataContext is not SettingsWindowViewModel vm) return;
        if (!TryParseRow(tag, out var row)) return;

        vm.ApplyFileNameTemplatePreset(row, preset);
    }

    /// <summary>「存为预设」图标按钮：用该行预设名框里的名字 + 该行模板写回设置（同名覆盖）。</summary>
    private void OnPresetSaveRequested(object? sender, string name)
    {
        if (sender is not TemplatePresetPicker { Tag: string tag }) return;
        if (DataContext is not SettingsWindowViewModel vm) return;
        if (!TryParseRow(tag, out var row)) return;

        vm.SaveFileNameTemplatePreset(row);
    }

    /// <summary>「删除」图标按钮：删掉该行预设名框里那条预设。</summary>
    private void OnPresetDeleteRequested(object? sender, string name)
    {
        if (sender is not TemplatePresetPicker { Tag: string tag }) return;
        if (DataContext is not SettingsWindowViewModel vm) return;
        if (!TryParseRow(tag, out var row)) return;

        vm.DeleteFileNameTemplatePreset(row);
    }

    /// <summary>预设消息气泡 1.6s 自动收起后回调：清空该行消息，让下次同样的消息还能重新弹出。</summary>
    private void OnPresetMessageDismissed(object? sender, EventArgs e)
    {
        if (sender is not TemplatePresetPicker { Tag: string tag }) return;
        if (DataContext is not SettingsWindowViewModel vm) return;
        if (!TryParseRow(tag, out var row)) return;

        vm.DismissFileNameTemplatePresetMessage(row);
    }

    /// <summary>把控件 <c>Tag</c> 翻译成行枚举；Tag 是 XAML 里手写的字面量，拼错时静默无动作。</summary>
    private static bool TryParseRow(string tag, out FileNameTemplateRow row)
        => Enum.TryParse(tag, ignoreCase: true, out row);

    /// <summary>关闭时写回窗口尺寸（非法值回落 XAML 默认 900×640）。</summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        var settings = AppSettings.Load();
        settings.SettingsWindowWidth = WindowPlacement.Capture(Width, 900);
        settings.SettingsWindowHeight = WindowPlacement.Capture(Height, 640);
        settings.Save();
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
        // 缩略图卡片外观（边框 / 垫底色 / 尺寸）：注入资源并通知主窗口按新尺寸重取缩略图
        App.RefreshThumbnailStyle();

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
        var dialog = new HelpDialog(
            LocalizationManager.T("AdaptiveHelp_Title"),
            new MarkdownHelpContent(LocalizationManager.T("AdaptiveHelp_Markdown")));
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
