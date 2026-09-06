using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// ZIP 压缩包注释编辑对话框。
/// 注意：与 CommentDialog 不同，此对话框直接读写 ZIP 文件 EOCD 注释，
/// 用于在 MainWindow 中编辑已有压缩包的注释。
/// </summary>
public partial class ArchiveCommentDialog : Window
{
    private readonly string _archivePath;
    private readonly ArchiveFormat _format;

    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T("Main_ArchiveComment_Title");
    public string PromptText => LocalizationManager.T("Main_ArchiveComment_Prompt");
    public string ZipOnlyHint => LocalizationManager.T("Main_ArchiveComment_ZipOnly");
    public string SavingStatusText => LocalizationManager.T("Main_ArchiveComment_Saving");
    public string SaveText => LocalizationManager.T("Main_ArchiveComment_Save");
    public string CancelText => LocalizationManager.T("MsgBox_Cancel");

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用带参构造函数。
    /// </summary>
    public ArchiveCommentDialog()
    {
        InitializeComponent();
        DataContext = this;
        _archivePath = string.Empty;
        _format = ArchiveFormat.Zip;
    }

    public ArchiveCommentDialog(string archivePath, ArchiveFormat format, string? currentComment)
    {
        InitializeComponent();
        DataContext = this;

        _archivePath = archivePath;
        _format = format;

        CommentTextBox.Text = currentComment ?? "";
        CommentTextBox.SelectAll();
        CommentTextBox.Focus();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_format != ArchiveFormat.Zip)
        {
            await AppMessageBox.Show(
                LocalizationManager.T("Main_ArchiveComment_NotSupported"),
                LocalizationManager.T("App_ErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                this);
            return;
        }

        var newComment = (CommentTextBox.Text ?? "").Trim();

        // Show saving state
        SaveBtn.IsEnabled = false;
        CancelBtn.IsEnabled = false;
        CommentTextBox.IsEnabled = false;
        ButtonPanel.IsVisible = false;
        SavingStatusBlock.IsVisible = true;

        try
        {
            await Task.Run(() => ZipCommentHelper.WriteComment(_archivePath, newComment));
            Close(true);
        }
        catch (Exception ex)
        {
            await AppMessageBox.Show(
                string.Format(LocalizationManager.T("Main_ArchiveComment_SaveFailed"), ex.Message),
                LocalizationManager.T("App_ErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                this);

            // Restore UI
            SavingStatusBlock.IsVisible = false;
            ButtonPanel.IsVisible = true;
            SaveBtn.IsEnabled = true;
            CancelBtn.IsEnabled = true;
            CommentTextBox.IsEnabled = true;
        }
    }
}
