using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 权限不足提示对话框 — 仅 OK 按钮，无提权选项。
/// 显示无法写入的目录列表和手动开启提权的提示。
/// </summary>
public partial class ElevationInfoDialog : Window
{
    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T("ElevationInfoDialog_Title");
    public string MessageText => LocalizationManager.T("ElevationInfoDialog_Message");
    public string HintText => LocalizationManager.T("ElevationInfoDialog_Hint");
    public string OkText => LocalizationManager.T("ElevationInfoDialog_Ok");

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="ElevationInfoDialog(IReadOnlyList{string})" />。
    /// </summary>
    public ElevationInfoDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ElevationInfoDialog(IReadOnlyList<string> unwritableDirs) : this()
    {
        DirectoryList.ItemsSource = unwritableDirs;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
