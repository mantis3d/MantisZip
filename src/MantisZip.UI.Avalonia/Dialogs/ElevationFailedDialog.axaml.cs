using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 权限提升失败对话框 — 已以管理员身份运行但仍无法写入时的错误提示。
/// 显示失败目录列表及可能的原因。
/// </summary>
public partial class ElevationFailedDialog : Window
{
    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T("ElevationFailedDialog_Title");
    public string TitleText => LocalizationManager.T("ElevationFailedDialog_Title");
    public string MessageText => LocalizationManager.T("ElevationFailedDialog_Message");
    public string ReasonsText => LocalizationManager.T("ElevationFailedDialog_Reasons");
    public string OkText => LocalizationManager.T("ElevationFailedDialog_Ok");

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="ElevationFailedDialog(IReadOnlyList{string})" />。
    /// </summary>
    public ElevationFailedDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ElevationFailedDialog(IReadOnlyList<string> failedDirectories) : this()
    {
        DirectoryList.ItemsSource = failedDirectories;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
