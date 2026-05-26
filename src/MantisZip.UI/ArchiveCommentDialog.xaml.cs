using System;
using System.Windows;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_format != ArchiveFormat.Zip)
        {
            AppMessageBox.Show(L.T(L.Main_ArchiveComment_NotSupported),
                L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newComment = CommentTextBox.Text.Trim();

        try
        {
            using var zf = new ZipFile(_archivePath, StringCodec.Default);
            zf.BeginUpdate();
            zf.SetComment(newComment);
            zf.CommitUpdate();

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            App.LogDebug("ArchiveCommentDialog.Save: failed: {0}", ex.Message);
            AppMessageBox.Show(L.TF(L.Main_ArchiveComment_SaveFailed, ex.Message),
                L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
