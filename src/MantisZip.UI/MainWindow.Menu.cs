using System.IO;
using System.Linq;
using System.Windows;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Microsoft.Win32;

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
            Filter = "压缩文件|*.zip;*.7z;*.rar;*.tar;*.tar.gz;*.gz;*.iso|所有文件|*.*",
            Title = "打开压缩包"
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
            Filter = "所有文件|*.*", Title = "选择要压缩的文件", Multiselect = true
        };
        if (ofd.ShowDialog() == true)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "ZIP 压缩文件|*.zip", Title = "保存为", FileName = "archive.zip"
            };
            if (sfd.ShowDialog() == true)
                await CompressAsync(ofd.FileNames, sfd.FileName);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentArchivePath))
            await LoadArchiveAsync(_currentArchivePath);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show($"MantisZip - 全功能解压缩软件\n\n版本: {AppConstants.Version}\n基于 .NET 9 + WPF\n\n支持格式: ZIP, 7z, TAR, GZ, RAR (只读)\n\n7-Zip 组件遵循 GNU LGPL 许可证\nhttps://www.7-zip.org",
            "关于 MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
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
                SetStatus("密码已匹配");
                if (dialog.RememberPassword)
                {
                    var patterns = dialog.Patterns.Count > 0
                        ? dialog.Patterns
                        : new List<string> { Path.GetFileName(_currentArchivePath) };
                    PasswordManager.Instance.AddPassword(userPwd, dialog.Description ?? "", patterns);
                }
            }
            else
                MessageBox.Show("密码错误", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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
        try { Clipboard.SetText(text); SetStatus($"已复制 {items.Count} 个文件名"); }
        catch { SetStatus("复制失败"); }
    }

    private void FileListCtx_CopyPath(object sender, RoutedEventArgs e)
    {
        var items = GetRightClickSelection();
        if (items.Count == 0) return;
        var text = string.Join(Environment.NewLine, items.Select(i => i.FullPath));
        try { Clipboard.SetText(text); SetStatus($"已复制 {items.Count} 个路径"); }
        catch { SetStatus("复制失败"); }
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

        if (filesToExtract.Count == 0) { SetStatus("没有可提取的文件"); return; }

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
                pw.SetProgress((double)i / filesToExtract.Count * 100, $"正在提取: {item.Name}");

                var outputPath = Path.Combine(dest, item.FullPath);
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                await ArchiveEntryExtractor.ExtractEntryAsync(
                    _currentArchivePath!, item.FullPath, outputPath, _currentFormat, _currentPassword, pw.CancellationToken);
            }

            pw.SetComplete("提取完成");
            if (AppSettings.Instance.OpenFolderAfterExtract) OpenInExplorer(dest);
            await Task.Delay(800);
            pw.Close();
            SetStatus("提取完成");
        }
        catch (OperationCanceledException) { pw.Close(); SetStatus("已取消"); }
        catch (Exception ex) { pw.Close(); MessageBox.Show($"提取失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); SetStatus("提取失败"); }
    }
}
