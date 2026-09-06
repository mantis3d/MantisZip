using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 按钮配置
/// </summary>
public enum MessageBoxButton
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel
}

/// <summary>
/// 图标类型
/// </summary>
public enum MessageBoxImage
{
    None,
    Error,
    Warning,
    Question,
    Information
}

/// <summary>
/// 对话框结果
/// </summary>
public enum MessageBoxResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

/// <summary>
/// 统一风格的对话框，替代原生 MessageBox。
/// 使用 <see cref="Window"/> 基类，以模态方式弹出。
/// </summary>
public partial class AppMessageBox : Window
{
    private Action? _action;

    // ── Localized string properties ──
    public string CancelText => LocalizationManager.T("MsgBox_Cancel");
    public string YesText => LocalizationManager.T("MsgBox_Yes");
    public string NoText => LocalizationManager.T("MsgBox_No");
    public string OkText => LocalizationManager.T("MsgBox_Ok");
    public string WinTitle => "MantisZip";

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="Show(string, string, MessageBoxButton, MessageBoxImage)"/>。
    /// </summary>
    public AppMessageBox()
    {
        InitializeComponent();
        DataContext = this;
    }

    private AppMessageBox(string message, string title, MessageBoxButton button, MessageBoxImage icon)
    {
        InitializeComponent();
        DataContext = this;

        Title = title;
        MessageText.Text = message;

        // Set icon emoji
        if (icon != MessageBoxImage.None)
        {
            IconText.Text = icon switch
            {
                MessageBoxImage.Error => "\u274C",
                MessageBoxImage.Warning => "\u26A0\uFE0F",
                MessageBoxImage.Question => "\u2753",
                _ => "\u2139\uFE0F",
            };
            IconText.IsVisible = true;
        }

        // Configure buttons
        switch (button)
        {
            case MessageBoxButton.OK:
                OkBtn.IsVisible = true;
                OkBtn.Focus();
                break;
            case MessageBoxButton.OKCancel:
                OkBtn.IsVisible = true;
                CancelBtn.IsVisible = true;
                CancelBtn.Focus();
                break;
            case MessageBoxButton.YesNo:
                YesBtn.IsVisible = true;
                NoBtn.IsVisible = true;
                NoBtn.Focus();
                break;
            case MessageBoxButton.YesNoCancel:
                YesBtn.IsVisible = true;
                NoBtn.IsVisible = true;
                CancelBtn.IsVisible = true;
                CancelBtn.Focus();
                break;
        }
    }

    private AppMessageBox(string message, string title, MessageBoxButton button, MessageBoxImage icon,
        string? actionButtonText, Action? action)
        : this(message, title, button, icon)
    {
        _action = action;
        if (actionButtonText != null && action != null)
        {
            ActionBtn.Content = actionButtonText;
            ActionBtn.IsVisible = true;
        }
    }

    /// <summary>
    /// 显示消息框，自动查找当前活跃窗口作为所有者。
    /// </summary>
    public static async Task<MessageBoxResult> Show(string message, string title = "",
        MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var owner = GetActiveWindow();
        return await Show(message, title, button, icon, owner);
    }

    /// <summary>
    /// 显示消息框，指定拥有者窗口。
    /// ShowDialog 返回调用 Close(T) 时传入的值。
    /// </summary>
    public static async Task<MessageBoxResult> Show(string message, string title = "",
        MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None,
        Window? owner = null)
    {
        var dialog = new AppMessageBox(message, title, button, icon);
        if (owner != null)
        {
            return await dialog.ShowDialog<MessageBoxResult>(owner);
        }

        var activeWindow = GetActiveWindow();
        if (activeWindow != null)
        {
            return await dialog.ShowDialog<MessageBoxResult>(activeWindow);
        }

        dialog.Show();
        return MessageBoxResult.None;
    }

    /// <summary>
    /// 显示带有操作按钮的消息框（如"打开日志目录"）。
    /// </summary>
    public static async void ShowWithAction(string message, string title,
        string actionButtonText, Action action,
        MessageBoxImage icon = MessageBoxImage.Information)
    {
        var owner = GetActiveWindow();
        var dialog = new AppMessageBox(message, title, MessageBoxButton.OK, icon,
            actionButtonText, action);
        if (owner != null)
        {
            await dialog.ShowDialog<MessageBoxResult>(owner);
            return;
        }

        var activeWindow = GetActiveWindow();
        if (activeWindow != null)
        {
            await dialog.ShowDialog<MessageBoxResult>(activeWindow);
        }
        else
        {
            dialog.Show();
        }
    }

    /// <summary>
    /// 获取当前活跃窗口，用于没有显式 Owner 的对话框自动指定 Owner，
    /// 避免弹窗出现在主窗口后面。
    /// </summary>
    private static Window? GetActiveWindow()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.Windows
                    .OfType<Window>()
                    .FirstOrDefault(w => w.IsActive);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Close 调用带泛型参数的重载，使 ShowDialog&lt;MessageBoxResult&gt; 返回对应值。
    /// </summary>
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.OK);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.Cancel);
    }

    private void OnYesClick(object? sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.Yes);
    }

    private void OnNoClick(object? sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.No);
    }

    private void OnActionBtnClick(object? sender, RoutedEventArgs e)
    {
        _action?.Invoke();
        Close(MessageBoxResult.OK);
    }
}
