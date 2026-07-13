using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 统一解压路径选择与选项对话框 — 包含 QuickPathControl + 解压选项。
/// </summary>
public partial class UnifiedExtractDialog : Window
{
    // ── Result properties ───────────────────────────────────────────────────

    public string SelectedPath => PathControl.PathText;
    public Core.Abstractions.FileConflictAction ConflictAction { get; private set; } = Core.Abstractions.FileConflictAction.Overwrite;
    public bool PreserveDirectoryRoot => PreserveRootCheck.IsChecked == true;

    /// <summary>Pre-set the target path (e.g. extract to archive name folder).</summary>
    public string PresetPath
    {
        get => PathControl.PathText;
        set => PathControl.PathText = value;
    }

    // ── Localized string properties ─────────────────────────────────────────

    public string WinTitle => LocalizationManager.T("Extract_Title");
    public string HeaderText => LocalizationManager.T("Extract_Header");
    public string OptionsTitle => LocalizationManager.T("Extract_Options");
    public string ConflictLabel => LocalizationManager.T("Extract_ConflictLabel");
    public string PreserveRootLabel => LocalizationManager.T("Extract_PreserveRoot");
    public string OkText => LocalizationManager.T("MsgBox_Ok");
    public string CancelText => LocalizationManager.T("MsgBox_Cancel");

    // ── Constructors ────────────────────────────────────────────────────────

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用。
    /// </summary>
    [Obsolete("Design-time only")]
    public UnifiedExtractDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>
    /// 创建统一解压对话框，指定拥有者窗口。
    /// </summary>
    public UnifiedExtractDialog(Window owner) : this()
    {
    }

    // ── Event handlers ──────────────────────────────────────────────────────

    private async void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathControl.PathText))
        {
            await AppMessageBox.Show("请选择解压目标路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        switch (ConflictCombo.SelectedIndex)
        {
            case 0: ConflictAction = Core.Abstractions.FileConflictAction.Overwrite; break;
            case 1: ConflictAction = Core.Abstractions.FileConflictAction.Skip; break;
            case 2: ConflictAction = Core.Abstractions.FileConflictAction.Rename; break;
            case 3: ConflictAction = Core.Abstractions.FileConflictAction.OverwriteIfOlder; break;
            case 4: ConflictAction = Core.Abstractions.FileConflictAction.OverwriteIfSmaller; break;
        }

        PathControl.AddToHistory(PathControl.PathText);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
