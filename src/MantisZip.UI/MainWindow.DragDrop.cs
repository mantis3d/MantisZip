using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;

namespace MantisZip.UI;

public partial class MainWindow
{
    #region 窗口拖入事件

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (_isOwnDrag) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length == 0) return;

        if (_currentArchivePath != null && File.Exists(_currentArchivePath))
        {
            if (files.Length == 1 && IsArchiveFile(files[0]))
            {
                _ = LoadArchiveAsync(files[0]);
                return;
            }

            var result = MessageBox.Show(this,
                $"将 {files.Length} 个文件/文件夹添加到「{Path.GetFileName(_currentArchivePath)}」？",
                "添加到压缩包", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                await AddFilesToCurrentArchiveAsync(files);
            return;
        }

        var path = files[0];
        if (IsArchiveFile(path))
        {
            _ = LoadArchiveAsync(path);
        }
        else
        {
            var capturedFiles = files.ToArray();
#pragma warning disable CS4014
            Dispatcher.BeginInvoke(new Action(() =>
#pragma warning restore CS4014
            {
                var window = new CompressSettingsWindow();
                foreach (var f in capturedFiles) window.AddSourcePath(f);
                window.Owner = this;
                window.Show();
                window.Activate();
            }));
        }
    }

    private async Task AddFilesToCurrentArchiveAsync(string[] files)
    {
        var engine = ArchiveEngineFactory.GetEngine(_currentFormat);
        if (engine == null) { SetStatus("不支持的压缩格式"); return; }

        try
        {
            SetStatus("正在添加文件到压缩包...");
            ShowProgress(true);
            var progress = new Progress<ArchiveProgress>(p =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ProgressBar.Value = p.PercentComplete;
                    ProgressText.Text = p.CurrentFile;
                });
            });

            await engine.AddToArchiveAsync(_currentArchivePath!, files,
                new ArchiveOptions { CompressionLevel = AppSettings.Instance.DefaultLevel },
                progress, entryBasePath: string.IsNullOrEmpty(_currentFolder) ? null : _currentFolder);

            await LoadArchiveAsync(_currentArchivePath!);
            SetStatus("文件已添加到压缩包");
        }
        catch (NotSupportedException ex)
        {
            MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("就绪");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"添加文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("添加失败");
        }
        finally { ShowProgress(false); }
    }

    #endregion

    #region 文件列表拖出

    private void FileListGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<ScrollBar>(e.OriginalSource as DependencyObject) != null) return;
        _dragStartPoint = e.GetPosition(FileListGrid);
    }

    private async void FileListGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _currentArchivePath == null) return;
        if (!AppSettings.Instance.EnableDragExtract) return;

        var pos = e.GetPosition(FileListGrid);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumHorizontalDragDistance)
            return;

        var selectedItems = FileListGrid.SelectedItems.Cast<ArchiveItem>().ToList();
        if (selectedItems.Count == 0) return;

        var hitTest = FileListGrid.InputHitTest(_dragStartPoint) as DependencyObject;
        var row = FindVisualParent<DataGridRow>(hitTest);
        if (row?.Item is ArchiveItem rowItem && !selectedItems.Contains(rowItem))
        {
            FileListGrid.SelectedItem = rowItem;
            selectedItems = new List<ArchiveItem> { rowItem };
        }

        var filesToDrag = selectedItems.Where(i => !i.IsDirectory).ToList();
        if (filesToDrag.Count == 0) return;

        _dragTempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "DragDrop", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dragTempDir);

        var pw = new ProgressWindow();
        pw.InitCancellation();
        pw.Owner = this;
        pw.Show();
        var ct = pw.CancellationToken;

        try
        {
            var extractedPaths = new List<string>();
            for (int i = 0; i < filesToDrag.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                var item = filesToDrag[i];
                var outputPath = Path.Combine(_dragTempDir, item.FullPath);
                pw.SetProgress((double)i / filesToDrag.Count * 100, $"正在提取: {item.NameDisplay ?? item.Name}");
                await ExtractEntryForDragAsync(item, outputPath);
                extractedPaths.Add(outputPath);
            }

            if (ct.IsCancellationRequested) { SetStatus("已取消"); return; }
            if (extractedPaths.Count == 0) { SetStatus("没有可拖拽的文件"); return; }

            pw.SetProgress(100, "⏳ 正在拖拽 — 放到目标位置以复制文件");
            _isOwnDrag = true;
            try { DragDrop.DoDragDrop(FileListGrid, new DataObject(DataFormats.FileDrop, extractedPaths.ToArray()), DragDropEffects.Copy); }
            finally { _isOwnDrag = false; }
        }
        catch (NotSupportedException ex)
        {
            MessageBox.Show(this, ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"提取文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { if (pw.IsVisible) pw.Close(); } catch { }
            CleanupDragTempDir();
            SetStatus("就绪");
        }
    }

    private static void ExtractTarGzSingleEntry(string archivePath, string entryName, string outputPath)
    {
        using var inputStream = File.OpenRead(archivePath);
        var isTarGz = archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
                   || archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
        Stream tarStream = inputStream;
        if (isTarGz || Path.GetExtension(archivePath).Equals(".gz", StringComparison.OrdinalIgnoreCase))
            tarStream = new ICSharpCode.SharpZipLib.GZip.GZipInputStream(inputStream);

        using var tarIn = new ICSharpCode.SharpZipLib.Tar.TarInputStream(tarStream, System.Text.Encoding.UTF8);
        ICSharpCode.SharpZipLib.Tar.TarEntry? entry;
        while ((entry = tarIn.GetNextEntry()) != null)
        {
            if (entry.IsDirectory) continue;
            if (entry.Name == entryName)
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                using var outStream = File.Create(outputPath);
                tarIn.CopyEntryContents(outStream);
                return;
            }
        }
        throw new FileNotFoundException($"在压缩包中未找到条目: {entryName}");
    }

    private void CleanupDragTempDir()
    {
        if (_dragTempDir == null) return;
        try { if (Directory.Exists(_dragTempDir)) Directory.Delete(_dragTempDir, recursive: true); }
        catch (Exception dragCleanupEx) { App.LogDebug("CleanupDragTempDir: failed: {0}", dragCleanupEx.Message); }
        _dragTempDir = null;
    }

    private async Task ExtractEntryForDragAsync(ArchiveItem item, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        switch (_currentFormat)
        {
            case ArchiveFormat.Zip:
            case ArchiveFormat.SevenZip:
                await ArchiveEntryExtractor.ExtractEntryAsync(
                    _currentArchivePath!, item.FullPath, outputPath, _currentFormat, _currentPassword);
                break;
            case ArchiveFormat.Tar:
            case ArchiveFormat.GZip:
                ExtractTarGzSingleEntry(_currentArchivePath!, item.FullPath, outputPath);
                break;
            default:
                throw new NotSupportedException($"格式 {_currentFormat} 不支持拖拽提取");
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T t) return t;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    #endregion
}
