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
using MantisZip.UI.Localization;

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
        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (_isOwnDrag) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 0) return;

            if (_currentArchivePath != null && File.Exists(_currentArchivePath))
            {
                if (files.Length == 1 && IsArchiveFile(files[0]))
                {
                    await LoadArchiveAsync(files[0]);
                    return;
                }

                var result = AppMessageBox.Show(this,
                    L.TF(L.Main_DragAddConfirm, files.Length, Path.GetFileName(_currentArchivePath)),
                    L.T(L.CompressConflict_Add), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                    await AddFilesToCurrentArchiveAsync(files);
                return;
            }

            var path = files[0];
            if (IsArchiveFile(path))
            {
                await LoadArchiveAsync(path);
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
        catch (Exception ex)
        {
            App.TraceLog("Window_Drop: unexpected error: {0}", ex.Message);
        }
    }

    private async Task AddFilesToCurrentArchiveAsync(string[] files)
    {
        App.LogDebug("[TRACE] AddFilesToCurrentArchiveAsync: files count={0}, format={1}", files.Length, _currentFormat);
        var engine = ArchiveEngineFactory.GetEngine(_currentFormat);
        if (engine == null) { SetStatus(L.T(L.Main_DragFormatUnsupported)); return; }
        App.LogDebug("[TRACE] AddFilesToCurrentArchiveAsync: engine={0}, CanAdd={1}", engine.GetType().Name, engine.CanAdd(_currentFormat));

        var pw = new ProgressWindow();
        pw.InitCancellation();
        pw.Show();
        pw.SetProgress(0, L.T(L.Main_Status_AddingFiles));

        try
        {
            App.LogDebug("[TRACE] AddFilesToCurrentArchiveAsync: calling engine.AddToArchiveAsync...");
            await engine.AddToArchiveAsync(_currentArchivePath!, files,
                new ArchiveOptions { CompressionLevel = AppSettings.Instance.DefaultLevel },
                ProgressWindow.CreateBackgroundProgress(pw),
                cancellationToken: pw.CancellationToken,
                entryBasePath: string.IsNullOrEmpty(_currentFolder) ? null : _currentFolder);
            App.LogDebug("[TRACE] AddFilesToCurrentArchiveAsync: engine.AddToArchiveAsync returned");

            pw.SetComplete(L.T(L.Main_Status_AddDone));
            App.LogDebug("[TRACE] AddFilesToCurrentArchiveAsync: SetComplete called, waiting for auto-close");
            await pw.AutoCloseOrWaitAsync(800, () => pw.Close());
            App.LogDebug("[TRACE] AddFilesToCurrentArchiveAsync: window closed, reloading archive");

            var prevFolder = _currentFolder;
            await LoadArchiveAsync(_currentArchivePath!);
            if (!string.IsNullOrEmpty(prevFolder))
            {
                FilterFiles(prevFolder);
                SelectFolderInTree(prevFolder);
            }
            SetStatus(L.T(L.Main_Status_AddDone));
        }
        catch (OperationCanceledException)
        {
            App.LogDebug("[TRACE] AddFilesToCurrentArchiveAsync: canceled");
            pw.Close();
            SetStatus(L.T(L.Main_Status_AddCancel));
        }
        catch (NotSupportedException ex)
        {
            App.LogDebug("[TRACE] AddFilesToCurrentArchiveAsync: NotSupportedException: {0}", ex.Message);
            pw.Close();
            AppMessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus(L.T(L.Main_Status_Ready));
        }
        catch (Exception ex)
        {
            App.LogDebug("[TRACE] AddFilesToCurrentArchiveAsync: Exception: {0}", ex.Message);
            pw.Close();
            AppMessageBox.Show(L.TF(L.Main_Status_AddFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(L.T(L.Main_Status_AddFailed));
        }
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
        ProgressWindow? pw = null;
        try
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

            _dragTempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle), "DragDrop", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_dragTempDir);

            pw = new ProgressWindow();
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
                    var safeEntryPath = FileConflictHelper.SanitizeEntryPath(item.FullPath);
                    var outputPath = Path.Combine(_dragTempDir, safeEntryPath);
                    pw.SetProgress((double)i / filesToDrag.Count * 100, L.TF(L.Main_Status_Extracting, item.NameDisplay ?? item.Name));
                    await ExtractEntryForDragAsync(item, outputPath);
                    extractedPaths.Add(outputPath);
                }

                if (ct.IsCancellationRequested) { SetStatus(L.T(L.Main_Status_AddCancel)); return; }
                if (extractedPaths.Count == 0) { SetStatus(L.T(L.Main_Status_NoDragFiles)); return; }

                pw.SetProgress(100, L.T(L.Main_Status_DragWaiting));
                _isOwnDrag = true;
                try { DragDrop.DoDragDrop(FileListGrid, new DataObject(DataFormats.FileDrop, extractedPaths.ToArray()), DragDropEffects.Copy); }
                finally { _isOwnDrag = false; }
            }
            catch (NotSupportedException ex)
            {
                AppMessageBox.Show(this, ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppMessageBox.Show(this, L.TF(L.Main_Status_ExtractFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { if (pw.IsVisible) pw.Close(); } catch { }
                CleanupDragTempDir();
                SetStatus(L.T(L.Main_Status_Ready));
            }
        }
        catch (Exception ex)
        {
            App.TraceLog("FileListGrid_PreviewMouseMove: unexpected error: {0}", ex.Message);
            AppMessageBox.Show(L.TF(L.Main_Status_ExtractFailed, ex.Message),
                L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            try { if (pw?.IsVisible == true) pw.Close(); } catch { }
            CleanupDragTempDir();
            SetStatus(L.T(L.Main_Status_Ready));
        }
    }

    private static void ExtractTarGzSingleEntry(string archivePath, string entryName, string outputPath)
    {
        // 路径安全检查（防御纵深，调用方已净化但仍做最终验证）
        var normalized = Path.GetFullPath(outputPath);
        var segments = normalized.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(s => s == ".."))
            throw new InvalidOperationException($"输出路径包含非法路径穿越: {outputPath}");

        using var inputStream = File.OpenRead(archivePath);
        // 传入原始压缩流，让 TarReader 自动检测 gzip 头
        using var reader = SharpCompress.Readers.Tar.TarReader.OpenReader(inputStream, new SharpCompress.Readers.ReaderOptions { LookForHeader = true });
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;
            if (entry.IsDirectory) continue;
            if (entry.Key == entryName)
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                using var outStream = File.Create(outputPath);
                using var entryStream = reader.OpenEntryStream();
                entryStream.CopyTo(outStream);
                return;
            }
        }
        throw new FileNotFoundException(L.TF(L.Core_Drag_EntryNotFound, entryName));
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
                await Task.Run(() =>
                    ExtractTarGzSingleEntry(_currentArchivePath!, item.FullPath, outputPath));
                break;
            default:
                throw new NotSupportedException(L.TF(L.Core_Drag_FormatUnsupported, _currentFormat));
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
