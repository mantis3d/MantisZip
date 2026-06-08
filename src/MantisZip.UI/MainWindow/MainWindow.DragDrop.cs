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
        if (_isOwnDrag)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
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

        // 检测点按的条目是否已在选区内——如果是，保存当前所有选中条目；
        // 否则清空保存，让 PreviewMouseMove 使用 DataGrid 处理后的单选集。
        var hitTest = FileListGrid.InputHitTest(_dragStartPoint) as DependencyObject;
        var row = FindVisualParent<DataGridRow>(hitTest);
        if (row?.Item is ArchiveItem rowItem &&
            FileListGrid.SelectedItems.Cast<ArchiveItem>().Contains(rowItem))
        {
            _dragPreservedSelection = FileListGrid.SelectedItems.Cast<ArchiveItem>().ToList();

            // 多选状态 + 点按已选中条目 → 阻止 DataGrid 内部处理此点击
            // （否则 DataGrid 在 Extended 模式下会清空多选，只保留点击的这一项）
            if (_dragPreservedSelection.Count > 1)
                e.Handled = true;
        }
        else
        {
            _dragPreservedSelection = null;
        }
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

            // 防止 async void 重入：提取阶段不处理新的 PreviewMouseMove
            if (_isDragExtracting) return;

            // 优先使用 PreviewMouseLeftButtonDown 时保存的多选集（不受 DataGrid 清空影响）
            var selectedItems = _dragPreservedSelection
                ?? FileListGrid.SelectedItems.Cast<ArchiveItem>().ToList();
            _dragPreservedSelection = null; // 一次性消费
            if (selectedItems.Count == 0) return;

            var hitTest = FileListGrid.InputHitTest(_dragStartPoint) as DependencyObject;
            var row = FindVisualParent<DataGridRow>(hitTest);
            if (row?.Item is ArchiveItem rowItem && !selectedItems.Contains(rowItem))
            {
                FileListGrid.SelectedItem = rowItem;
                selectedItems = new List<ArchiveItem> { rowItem };
            }

            // 保存选中目录供路径裁剪用
            var selectedDirs = selectedItems.Where(s => s.IsDirectory).ToList();

            // 展开目录为其包含的全部文件，非目录项直接保留
            var filesToDrag = ExpandDragItems(selectedItems);
            if (filesToDrag.Count == 0) return;

            // 至此确认需要提取，置重入锁
            _isDragExtracting = true;

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
                    var outputPath = GetDragExtractPath(item, selectedDirs, _dragTempDir);
                    pw.SetProgress((double)i / filesToDrag.Count * 100, L.TF(L.Main_Status_Extracting, item.NameDisplay ?? item.Name));
                    await ExtractEntryForDragAsync(item, outputPath);
                    extractedPaths.Add(outputPath);
                }

                if (ct.IsCancellationRequested) { SetStatus(L.T(L.Main_Status_AddCancel)); return; }
                if (extractedPaths.Count == 0) { SetStatus(L.T(L.Main_Status_NoDragFiles)); return; }

                // 枚举 temp 目录下的一级条目（文件和目录），传给 CF_HDROP。
                // Explorer 收到目录句柄后会递归复制整个目录树，从而保留子目录结构；
                // 而传扁平文件列表会导致所有文件平铺到目标目录（结构丢失）。
                var topLevelItems = Directory.EnumerateFileSystemEntries(_dragTempDir).ToList();
                if (topLevelItems.Count == 0) { SetStatus(L.T(L.Main_Status_NoDragFiles)); return; }

                pw.SetProgress(100, L.T(L.Main_Status_DragWaiting));
                _isOwnDrag = true;
                try { DragDrop.DoDragDrop(FileListGrid, new DataObject(DataFormats.FileDrop, topLevelItems.ToArray()), DragDropEffects.Copy); }
                finally { _isOwnDrag = false; }
                App.LogDebug("DragDrop: completed, topLevelItems={0}, paths=[{1}]", topLevelItems.Count, string.Join("; ", topLevelItems));
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
                _isDragExtracting = false;
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
            _isDragExtracting = false;
        }
    }

    /// <summary>
    /// 展开选集：目录项展开为其内部所有文件；非目录项直接保留。去重。
    /// 所有路径比较前均归一化分隔符，以兼容 ZIP 中可能使用的 \ 路径。
    /// </summary>
    private List<ArchiveItem> ExpandDragItems(IReadOnlyList<ArchiveItem> items)
    {
        var result = new List<ArchiveItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!item.IsDirectory)
            {
                if (seen.Add(item.FullPath.Replace('\\', '/')))
                    result.Add(item);
            }
            else
            {
                var dirPath = item.FullPath.Replace('\\', '/');
                var dirPrefix = dirPath.EndsWith("/") ? dirPath : dirPath + "/";
                foreach (var child in _allItems)
                {
                    if (child.IsDirectory) continue;
                    var childPath = child.FullPath.Replace('\\', '/');
                    if (childPath.StartsWith(dirPrefix, StringComparison.Ordinal) &&
                        seen.Add(childPath))
                    {
                        result.Add(child);
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 计算拖拽提取的目标路径：对选中目录展开的文件，去掉选中目录的父路径前缀，
    /// 保留选中目录名作为输出根容器。非目录展开的文件保持完整归档路径。
    /// 示例：选中 "Total Commander 10/PLUGINS/wbx" 时，
    ///   wbx/ButtonBarChanger/file.wbx → _dragTempDir\wbx\ButtonBarChanger\file.wbx ✅
    ///   而非 _dragTempDir\Total Commander 10\PLUGINS\wbx\ButtonBarChanger\file.wbx ❌
    /// </summary>
    private string GetDragExtractPath(ArchiveItem item, IReadOnlyList<ArchiveItem> selectedDirs, string tempDir)
    {
        var normalizedPath = item.FullPath.Replace('\\', '/');
        var relativePath = normalizedPath;

        foreach (var dir in selectedDirs)
        {
            var dirPath = dir.FullPath.Replace('\\', '/').TrimEnd('/');
            var dirPrefix = dirPath + "/";
            if (normalizedPath.StartsWith(dirPrefix, StringComparison.Ordinal))
            {
                // 此文件从该目录展开：去掉选中目录的父路径，保留目录名及以下子结构
                var idx = dirPath.LastIndexOf('/');
                if (idx >= 0)
                    relativePath = normalizedPath.Substring(idx + 1);
                // idx < 0 表示根级目录，无需裁剪，保留完整路径
                break;
            }
        }

        var safeEntryPath = FileConflictHelper.SanitizeEntryPath(relativePath);
        // 使用 GetFullPath 归一化分隔符（\ vs /），避免 CF_HDROP 中的 mixed separators 问题
        return Path.GetFullPath(Path.Combine(tempDir, safeEntryPath));
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

        await ArchiveEntryExtractor.ExtractEntryAsync(
            _currentArchivePath!, item.FullPath, outputPath, _currentFormat, _currentPassword);
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
