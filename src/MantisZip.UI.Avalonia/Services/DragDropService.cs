using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Orchestrates the post-drop extraction workflow:
/// detect target directory, expand items, extract with progress, handle conflicts.
/// </summary>
internal class DragDropService
{
    private readonly string _archivePath;
    private readonly ArchiveFormat _format;
    private readonly string? _password;
    private readonly Window _ownerWindow;
    private readonly AppSettings _settings;

    /// <summary>用户勾选"应用到全部"后记住的冲突处理方式（null = 未决定，继续弹窗）</summary>
    private FileConflictAction? _applyAllAction;

    public DragDropService(string archivePath, ArchiveFormat format, string? password, Window ownerWindow)
    {
        _archivePath = archivePath;
        _format = format;
        _password = password;
        _ownerWindow = ownerWindow;
        _settings = AppSettings.Load();
    }

    /// <summary>
    /// Execute the full post-drop workflow:
    /// 1. Detect target directory (fallback to folder picker)
    /// 2. Expand selected items to flat file list
    /// 3. Show ProgressWindow and extract files with progress
    /// 4. Handle conflicts, cancellation, errors
    /// 5. Optionally open target folder after extraction
    /// </summary>
    public async Task ExecuteAfterDropAsync(
        IReadOnlyList<ArchiveItem> selectedItems,
        IReadOnlyList<ArchiveItem> allItems,
        MainWindowViewModel? vm)
    {
        // 1. Detect target directory
        var (targetDir, status) = DropTargetDetector.DetectTargetDirectory();
        App.DebugLog($"[DragDropService] DetectTargetDirectory: targetDir={targetDir ?? "(null)"}, status={status}");

        // 2. Fallback folder picker if detection failed
        if (string.IsNullOrEmpty(targetDir))
        {
            // 如果在自己的窗口上松开，直接取消（不弹对话框）
            if (IsOverOwnWindow())
            {
                App.DebugLog("[DragDropService] Dropped on own window — cancelling");
                if (vm != null)
                    vm.StatusMessage = "";
                return;
            }

            App.DebugLog("[DragDropService] DetectTargetDirectory returned null, showing folder picker...");
            targetDir = await PickFolderAsync();
            if (targetDir == null)
            {
                App.DebugLog("[DragDropService] User cancelled folder picker");
                return;
            }
            App.DebugLog($"[DragDropService] User picked folder: {targetDir}");
        }


        // 3. Expand selected items to flat file list
        var itemsToExtract = DragDropItemExpander.ExpandItems(selectedItems, allItems);
        App.DebugLog($"[DragDropService] Expanded: {selectedItems.Count} selected → {itemsToExtract.Count} files to extract");
        if (itemsToExtract.Count == 0)
        {
            App.DebugLog("[DragDropService] No files to extract after expansion");
            return;
        }

        // 4. Get conflict action from settings ("ask"/"overwrite"/"rename"/"skip")
        var conflictAction = _settings.FileConflictAction;

        // 5. Get selected directories for path trimming
        var selectedDirs = selectedItems.Where(i => i.IsDirectory).ToList();

        // 6. Show ProgressWindow (non-modal)
        // 盘根目录（如 C:\）的 GetFileName 为空 → 回退用完整路径
        var folderName = Path.GetFileName(targetDir);
        if (string.IsNullOrEmpty(folderName))
            folderName = targetDir;
        var pw = new ProgressWindow(LocalizationManager.T("Status_DragExtractingTo", folderName));
        pw.InitCancellation();
        pw.Show();

        int totalFiles = itemsToExtract.Count;
        int processedFiles = 0;

        try
        {
            // Create progress dispatcher that marshals to UI thread at Background priority
            var progress = ProgressViewModel.CreateBackgroundProgress(pw, p => pw.SetProgress(p));

            // 7. Extract in background thread
            await Task.Run(async () =>
            {
                foreach (var item in itemsToExtract)
                {
                    pw.CancellationToken.ThrowIfCancellationRequested();

                    var outputPath = DragDropItemExpander.GetExtractPath(item, selectedDirs, targetDir);

                    // Create output directory if needed
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                        Directory.CreateDirectory(outputDir);

                    // Handle file conflicts
                    if (File.Exists(outputPath))
                    {
                        switch (conflictAction)
                        {
                            case "skip":
                                Interlocked.Increment(ref processedFiles);
                                continue;
                            case "rename":
                                outputPath = PathHelper.GetUniquePath(outputPath);
                                break;
                            case "overwrite":
                                File.Delete(outputPath);
                                break;
                            case "ask":
                            {
                                // 弹 ConflictDialog，由 Core 的 ResolvePathAsync 处理各策略分支
                                // （Overwrite / Rename / Skip / OverwriteIfOlder / OverwriteIfSmaller / 自定义名）
                                var options = new ArchiveOptions
                                {
                                    ConflictAction = FileConflictAction.Ask,
                                    ConflictResolverAsync = async info =>
                                    {
                                        if (_applyAllAction.HasValue)
                                            return _applyAllAction.Value;

                                        var dlg = await ShowConflictDialogAsync(info);
                                        if (dlg.CancelOperation)
                                            throw new OperationCanceledException("用户取消整个拖拽解压操作");
                                        if (dlg.ApplyToAll)
                                            _applyAllAction = dlg.ResultAction;
                                        info.CustomName = dlg.CustomName;
                                        return dlg.ResultAction;
                                    }
                                };
                                var resolved = await FileConflictHelper.ResolvePathAsync(
                                    outputPath, options, item.LastModified, item.Size);
                                if (resolved == null)
                                {
                                    // Skip / 条件不满足（不覆盖旧文件或小文件）→ 跳过当前文件
                                    Interlocked.Increment(ref processedFiles);
                                    continue;
                                }
                                outputPath = resolved;
                                break;
                            }
                        }
                    }

                    // Extract the entry
                    await ArchiveEntryExtractor.ExtractEntryAsync(
                        _archivePath, item.FullPath, outputPath,
                        _format, _password, pw.CancellationToken);

                    int current = Interlocked.Increment(ref processedFiles);
                    double pct = totalFiles > 0 ? (double)current / totalFiles * 100 : 100;
                    progress.Report(new ArchiveProgress
                    {
                        PercentComplete = pct,
                        CurrentFile = item.Name
                    });
                }
            }, pw.CancellationToken);

            // 8. Post-extraction: status message and optional folder open
            App.DebugLog($"[DragDropService] Extraction complete: {processedFiles}/{totalFiles} files to {targetDir}");
            if (vm != null)
                vm.StatusMessage = LocalizationManager.T("Status_DragDone", processedFiles, totalFiles, folderName);

            if (_settings.OpenFolderAfterExtract)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetDir,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DragDropService] Failed to open folder: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            App.DebugLog("[DragDropService] Extraction cancelled by user");
            // 9. Handle cancellation
            if (vm != null)
                vm.StatusMessage = LocalizationManager.T("Status_DragCancelled");
        }
        catch (Exception ex)
        {
            App.DebugLog($"[DragDropService] Extraction failed: {ex.GetType().Name}: {ex.Message}");
            // 10. Handle errors: status bar message + explicit error dialog
            // （失败必须弹窗提示——拖拽解压无确认环节，用户容易忽略状态栏小字）
            if (vm != null)
                vm.StatusMessage = LocalizationManager.T("Status_DragFailed", ex.Message);
            try
            {
                await AppMessageBox.Show(
                    LocalizationManager.T("Status_DragFailed", ex.Message),
                    LocalizationManager.T("App_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error, _ownerWindow);
            }
            catch (Exception dlgEx)
            {
                App.DebugLog($"[DragDropService] Failed to show error dialog: {dlgEx.Message}");
            }
        }
        finally
        {
            // 11. Always close the progress window
            pw.Close();
        }
    }

    /// <summary>
    /// Show a folder picker dialog as fallback when DropTargetDetector fails.
    /// Uses Avalonia's StorageProvider API.
    /// </summary>
    private async Task<string?> PickFolderAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(_ownerWindow);
            if (topLevel == null)
                return null;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LocalizationManager.T("Status_DragPickFolder"),
                AllowMultiple = false
            });

            return folders.Count >= 1 ? folders[0].Path.LocalPath : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 在 UI 线程弹出冲突对话框，等待用户选择。
    /// 从后台线程调用；通过 Dispatcher.Post 切换到 UI 线程，用 TaskCompletionSource 回传结果。
    /// </summary>
    private Task<ConflictDialog> ShowConflictDialogAsync(FileConflictInfo info)
    {
        var tcs = new TaskCompletionSource<ConflictDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ownerWindow.Dispatcher.Post(async () =>
        {
            try
            {
                var dlg = new ConflictDialog(info);
                await dlg.ShowDialog(_ownerWindow);
                tcs.SetResult(dlg);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// Check if the cursor is currently over our own application window.
    /// Uses HWND comparison against the owner window (mirrors OverlayController) —
    /// the class-name heuristic misidentifies other Avalonia apps since all
    /// Avalonia windows share the "Avalonia-" prefix.
    /// </summary>
    private bool IsOverOwnWindow()
    {
        if (!NativeMethods.GetCursorPos(out var pt))
            return false;
        var hWnd = NativeMethods.WindowFromPoint(pt);
        if (hWnd == nint.Zero)
            return false;
        var rootTarget = NativeMethods.GetAncestor(hWnd, 2); // GA_ROOT = 2
        if (rootTarget == nint.Zero)
            rootTarget = hWnd;
        var mainHwnd = _ownerWindow.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        return mainHwnd != nint.Zero && rootTarget == mainHwnd;
    }
}
