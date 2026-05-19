using System.IO;
using System.Linq;
using System.Windows;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Microsoft.Win32;
using MantisZip.UI.Localization;
using System.Windows.Input;

namespace MantisZip.UI;

public partial class MainWindow
{
    private void NewArchive_Click(object sender, RoutedEventArgs e)
    {
        var window = new CompressSettingsWindow();
        window.Owner = this;
        window.Show();
    }

    private async void OpenArchive_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = L.T(L.Main_OpenFileFilter),
            Title = L.T(L.Shell_Open)
        };
        if (dialog.ShowDialog() == true)
            await LoadArchiveAsync(dialog.FileName);
    }

    private async void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentArchivePath)) return;
        var dest = ResolveExtractDestination(_currentArchivePath);
        if (dest != null)
            await ExtractAsync(_currentArchivePath, dest);
    }

    private async void Compress_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = L.T(L.Compress_FileFilter), Title = L.T(L.Main_SelectFilesTitle), Multiselect = true
        };
        if (ofd.ShowDialog() == true)
        {
            var sfd = new SaveFileDialog
            {
                Filter = L.T(L.Main_SaveZipFilter), Title = L.T(L.Main_SaveZipTitle), FileName = "archive.zip"
            };
            if (sfd.ShowDialog() == true)
                await CompressAsync(ofd.FileNames, sfd.FileName);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentArchivePath))
        {
            var prevFolder = _currentFolder;
            await LoadArchiveAsync(_currentArchivePath);
            if (!string.IsNullOrEmpty(prevFolder))
            {
                FilterFiles(prevFolder);
                SelectFolderInTree(prevFolder);
            }
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        AppMessageBox.Show(L.TF(L.Main_About_Text, AppConstants.Version),
            L.T(L.Settings_Advanced_AboutHeader), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow();
        window.OnTextFontSizeChanged = size =>
        {
            if (PreviewTextBox.Visibility == Visibility.Visible)
                PreviewTextBox.FontSize = size;
        };
        window.ShowDialog();
        if (PreviewTextBox.Visibility == Visibility.Visible)
            PreviewTextBox.FontSize = AppSettings.Instance.TextPreviewFontSize;
    }

    private void EnterPassword_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentArchivePath)) return;
        var dialog = new PasswordDialog(Path.GetFileName(_currentArchivePath));
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            var userPwd = dialog.ResultPassword;
            if (string.IsNullOrEmpty(userPwd)) return;
            var engine = ArchiveEngineFactory.GetEngineByExtension(_currentArchivePath);
            if (engine == null) return;
            if (App.QuickVerifyPassword(_currentArchivePath, userPwd, engine))
            {
                _currentPassword = userPwd;
                UpdatePasswordStatus();
                UpdateEnterPasswordBtnState();
                SetStatus(L.T(L.Main_Status_PwdMatched));
                if (dialog.RememberPassword)
                {
                    var patterns = dialog.Patterns.Count > 0
                        ? dialog.Patterns
                        : new List<string> { Path.GetFileName(_currentArchivePath) };
                    try { PasswordManager.Instance.AddPassword(userPwd, dialog.Description ?? "", patterns); }
                    catch (Exception pwdEx) { App.LogDebug("PasswordDialog: failed to save password: {0}", pwdEx.Message); }
                }
            }
            else
                AppMessageBox.Show(L.T(L.Main_Status_WrongPwd), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region 添加/删除操作

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentArchivePath)) return;
        var engine = ArchiveEngineFactory.GetEngine(_currentFormat);
        if (engine == null || !engine.CanAdd(_currentFormat)) return;

        var filter = L.T(L.Compress_FileFilter);
        var title = L.T(L.Main_SelectFilesTitle);
        var ofd = new OpenFileDialog
        {
            Filter = filter,
            Title = title,
            Multiselect = true
        };
        if (ofd.ShowDialog() == true)
            await AddFilesToCurrentArchiveAsync(ofd.FileNames);
    }

    private void FileListCtx_AddFiles(object sender, RoutedEventArgs e)
    {
        AddFiles_Click(sender, e);
    }

    private async void DeleteFiles_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedEntriesAsync();
    }

    private async void FileListCtx_DeleteFiles(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedEntriesAsync();
    }

    /// <summary>
    /// 键盘 Delete 键处理 — 删除选中文件
    /// </summary>
    private void FileListGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && !string.IsNullOrEmpty(_currentArchivePath) && FileListGrid.IsReadOnly)
        {
            e.Handled = true;
            _ = DeleteSelectedEntriesAsync();
        }
    }

    /// <summary>
    /// 从当前压缩包中删除选中的条目（目录自动展开为内部文件）。
    /// </summary>
    private async Task DeleteSelectedEntriesAsync()
    {
        if (string.IsNullOrEmpty(_currentArchivePath)) return;

        var selectedItems = FileListGrid.SelectedItems.Cast<ArchiveItem>().ToList();
        if (selectedItems.Count == 0) return;

        // 目录展开为内部所有文件
        var selectedDirs = selectedItems.Where(i => i.IsDirectory).Select(d => d.FullPath.TrimEnd('/') + "/").ToHashSet();
        var filesToDelete = selectedItems
            .Where(i => !i.IsDirectory)
            .Concat(_allItems.Where(i => !i.IsDirectory && selectedDirs.Any(d => i.FullPath.StartsWith(d))))
            .DistinctBy(i => i.FullPath)
            .Select(i => i.FullPath)
            .ToList();

        if (filesToDelete.Count == 0) { SetStatus(L.T(L.Main_Status_NoFilesToExtract)); return; }

        // 确认对话框
        var confirm = AppMessageBox.Show(this,
            L.TF(L.Main_DeleteConfirm, filesToDelete.Count),
            L.T(L.Main_DeleteConfirmTitle),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var engine = ArchiveEngineFactory.GetEngineByExtension(_currentArchivePath);
        if (engine == null) return;

        var pw = new ProgressWindow();
        pw.InitCancellation();
        pw.Show();
        pw.SetProgress(0, L.T(L.Main_Status_Deleting));

        try
        {
            await engine.DeleteEntriesAsync(_currentArchivePath!, filesToDelete.ToArray(), _currentPassword,
                ProgressWindow.CreateBackgroundProgress(pw),
                pw.CancellationToken);

            pw.SetComplete(L.T(L.Main_Status_DeleteDone));
            await Task.Delay(800);
            pw.Close();

            var prevFolder = _currentFolder;
            await LoadArchiveAsync(_currentArchivePath!);
            if (!string.IsNullOrEmpty(prevFolder))
            {
                FilterFiles(prevFolder);
                SelectFolderInTree(prevFolder);
            }
            SetStatus(L.T(L.Main_Status_DeleteDone));
        }
        catch (OperationCanceledException)
        {
            pw.Close();
            SetStatus(L.T(L.Main_Status_AddCancel));
        }
        catch (NotSupportedException ex)
        {
            pw.Close();
            AppMessageBox.Show(ex.Message, L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus(L.T(L.Main_Status_Ready));
        }
        catch (Exception ex)
        {
            pw.Close();
            AppMessageBox.Show(L.TF(L.Main_Status_DeleteFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(L.T(L.Main_Status_DeleteFailed));
        }
    }

    #endregion

    #region 文件列表右键菜单

    private List<ArchiveItem> GetRightClickSelection()
    {
        return FileListGrid.SelectedItems.Cast<ArchiveItem>().ToList();
    }

    private async void FileListCtx_ExtractTo(object sender, RoutedEventArgs e)
    {
        var items = GetRightClickSelection();
        if (items.Count == 0 || string.IsNullOrEmpty(_currentArchivePath)) return;
        var dest = ResolveExtractDestination(_currentArchivePath);
        if (dest == null) return;
        await ExtractSelectedAsync(items, dest);
    }

    private async void FileListCtx_ExtractHere(object sender, RoutedEventArgs e)
    {
        var items = GetRightClickSelection();
        if (items.Count == 0 || string.IsNullOrEmpty(_currentArchivePath)) return;
        var dest = Path.GetDirectoryName(_currentArchivePath);
        if (string.IsNullOrEmpty(dest)) dest = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        await ExtractSelectedAsync(items, dest);
    }

    private void FileListCtx_CopyName(object sender, RoutedEventArgs e)
    {
        var items = GetRightClickSelection();
        if (items.Count == 0) return;
        var text = string.Join(Environment.NewLine, items.Select(i => i.Name.TrimEnd('/')));
        try { Clipboard.SetText(text); SetStatus(L.TF(L.Main_Status_CopiedNames, items.Count)); }
        catch { SetStatus(L.T(L.Main_Status_CopyFailed)); }
    }

    private void FileListCtx_CopyPath(object sender, RoutedEventArgs e)
    {
        var items = GetRightClickSelection();
        if (items.Count == 0) return;
        var text = string.Join(Environment.NewLine, items.Select(i => i.FullPath));
        try { Clipboard.SetText(text); SetStatus(L.TF(L.Main_Status_CopiedPaths, items.Count)); }
        catch { SetStatus(L.T(L.Main_Status_CopyFailed)); }
    }

    #endregion

    private void PasswordManager_Click(object sender, RoutedEventArgs e)
    {
        new PasswordManagerWindow { Owner = this }.ShowDialog();
    }

    private void TestArchive_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentArchivePath))
            _ = TestArchiveAsync(_currentArchivePath);
    }

    private void PreviewToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _previewPanelEnabled = PreviewToggleBtn.IsChecked == true;
        AppSettings.Instance.ShowPreviewPanel = _previewPanelEnabled;
        AppSettings.Instance.Save();

        if (_previewPanelEnabled)
        {
            ShowPreviewPanel();
            if (FileListGrid.SelectedItem is ArchiveItem selected && !string.IsNullOrEmpty(_currentArchivePath))
            {
                if (selected.IsDirectory) ShowDirectoryPreview(selected);
                else _ = ShowPreviewAsync(selected);
            }
        }
        else
            HidePreview();
    }

    private void OpenHint_Click(object sender, RoutedEventArgs e)
    {
        OpenArchive_Click(sender, e);
    }

    /// <summary>
    /// 解压选中的条目到指定目录。目录自动展开为内部所有文件。
    /// </summary>
    private async Task ExtractSelectedAsync(List<ArchiveItem> items, string dest)
    {
        var selectedDirs = items.Where(i => i.IsDirectory).Select(d => d.FullPath.TrimEnd('/') + "/").ToHashSet();
        var filesToExtract = items
            .Where(i => !i.IsDirectory)
            .Concat(_allItems.Where(i => !i.IsDirectory && selectedDirs.Any(d => i.FullPath.StartsWith(d))))
            .DistinctBy(i => i.FullPath)
            .ToList();

        if (filesToExtract.Count == 0) { SetStatus(L.T(L.Main_Status_NoFilesToExtract)); return; }

        var pw = new ProgressWindow();
        pw.InitCancellation();
        pw.Show();
        pw.SetProgress(0, "正在提取...");

        try
        {
            for (int i = 0; i < filesToExtract.Count; i++)
            {
                var item = filesToExtract[i];
                pw.CancellationToken.ThrowIfCancellationRequested();
                pw.SetProgress((double)i / filesToExtract.Count * 100, L.TF(L.Main_Status_Extracting, item.Name));

                var safeEntryPath = FileConflictHelper.SanitizeEntryPath(item.FullPath);
                var outputPath = FileConflictHelper.GetSafePath(dest, safeEntryPath);
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                await ArchiveEntryExtractor.ExtractEntryAsync(
                    _currentArchivePath!, item.FullPath, outputPath, _currentFormat, _currentPassword, pw.CancellationToken);
            }

            pw.SetComplete(L.T(L.Main_Status_ExtractItemsDone));
            if (AppSettings.Instance.OpenFolderAfterExtract) OpenInExplorer(dest);
            await Task.Delay(800);
            pw.Close();
            SetStatus(L.T(L.Main_Status_ExtractItemsDone));
        }
        catch (OperationCanceledException) { pw.Close(); SetStatus(L.T(L.Main_Status_AddCancel)); }
        catch (Exception ex) { pw.Close(); AppMessageBox.Show(L.TF(L.Main_Status_ExtractFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); SetStatus(L.T(L.Main_Status_ExtractFailed)); }
    }
}
