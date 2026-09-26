using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 权限提升确认对话框 — 允许用户选择以管理员身份重新启动。
/// 单个目录显示单行短消息，多个目录显示列表 + 格式化计数消息。
/// </summary>
public partial class ElevationDialog : Window
{
    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T("ElevationDialog_Title");
    public string HintText => LocalizationManager.T("ElevationDialog_Hint");
    public string ElevateText => LocalizationManager.T("ElevationDialog_Elevate");
    public string CancelText => LocalizationManager.T("ElevationDialog_Cancel");

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="ElevationDialog(IReadOnlyList{string})" />。
    /// </summary>
    public ElevationDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ElevationDialog(IReadOnlyList<string> unwritableDirs) : this()
    {
        if (unwritableDirs.Count == 1)
        {
            MessageText.Text = string.Format(
                LocalizationManager.T("ElevationDialog_Message"),
                unwritableDirs[0]);
        }
        else
        {
            MessageText.Text = string.Format(
                LocalizationManager.T("ElevationDialog_MultiMessage"),
                unwritableDirs.Count);
            DirectoryList.ItemsSource = unwritableDirs;
            DirectoryList.IsVisible = true;
        }
    }

    private void Elevate_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
