using System.Windows;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class ArchiveCommentDialog : Window
{
    private readonly string _archivePath;
    private readonly ArchiveFormat _format;

    public ArchiveCommentDialog(string archivePath, ArchiveFormat format, string? currentComment)
    {
        InitializeComponent();
        _archivePath = archivePath;
        _format = format;
        CommentTextBox.Text = currentComment ?? "";
        CommentTextBox.SelectAll();
        CommentTextBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_format != ArchiveFormat.Zip)
        {
            AppMessageBox.Show(L.T(L.Main_ArchiveComment_NotSupported),
                L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newComment = CommentTextBox.Text.Trim();

        // 显示保存中状态
        SaveBtn.IsEnabled = false;
        CancelBtn.IsEnabled = false;
        CommentTextBox.IsEnabled = false;
        ButtonPanel.Visibility = Visibility.Collapsed;
        SavingText.Visibility = Visibility.Visible;

        try
        {
            await Task.Run(() => ZipCommentHelper.WriteComment(_archivePath, newComment));

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            App.LogDebug("ArchiveCommentDialog.Save: failed: {0}", ex.Message);
            AppMessageBox.Show(L.TF(L.Main_ArchiveComment_SaveFailed, ex.Message),
                L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);

            // 恢复界面
            SavingText.Visibility = Visibility.Collapsed;
            ButtonPanel.Visibility = Visibility.Visible;
            SaveBtn.IsEnabled = true;
            CancelBtn.IsEnabled = true;
            CommentTextBox.IsEnabled = true;
        }
    }
}
