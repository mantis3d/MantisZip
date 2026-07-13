using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 快速路径选择对话框 — 内嵌 QuickPathControl 控件，支持文件夹/文件打开/保存模式。
/// </summary>
public partial class QuickPathDialog : Window
{
    // ── Localized string properties ─────────────────────────────────────────

    public string WinTitle => LocalizationManager.T("QuickPath_Title");
    public string PromptText => LocalizationManager.T("QuickPath_Prompt");
    public string OkText => LocalizationManager.T("MsgBox_Ok");
    public string CancelText => LocalizationManager.T("MsgBox_Cancel");

    // ── Result properties ───────────────────────────────────────────────────

    /// <summary>Selected path, or null if cancelled.</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>Selected filename in save mode.</summary>
    public string? SelectedFileName => PathControl.FileName;

    // ── Property proxies (delegated to QuickPathControl) ────────────────────

    public bool IsFolderMode
    {
        get => PathControl.IsFolderMode;
        set => PathControl.IsFolderMode = value;
    }

    public bool IsFileOpenMode
    {
        get => PathControl.IsFileOpenMode;
        set => PathControl.IsFileOpenMode = value;
    }

    public string FileTypeFilter
    {
        get => PathControl.FileTypeFilter;
        set => PathControl.FileTypeFilter = value;
    }

    /// <summary>
    /// Proxied to <see cref="FileTypeFilter"/> — Avalonia QuickPathControl
    /// does not have a separate FileOpenFilter property.
    /// </summary>
    public string FileOpenFilter
    {
        get => PathControl.FileTypeFilter;
        set => PathControl.FileTypeFilter = value;
    }

    public string DefaultFileName
    {
        get => PathControl.DefaultFileName;
        set => PathControl.DefaultFileName = value;
    }

    public string InitialPath
    {
        get => PathControl.PathText;
        set => PathControl.PathText = value;
    }

    // ── Constructors ────────────────────────────────────────────────────────

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="QuickPathDialog(bool)"/>。
    /// </summary>
    [Obsolete("Design-time only")]
    public QuickPathDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public QuickPathDialog(bool isFolderMode) : this()
    {
        IsFolderMode = isFolderMode;
    }

    // ── Event handlers ──────────────────────────────────────────────────────

    private async void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathControl.PathText))
        {
            await AppMessageBox.Show(
                LocalizationManager.T("QuickPath_SelectPathWarning"),
                LocalizationManager.T("QuickPath_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SelectedPath = PathControl.PathText;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
