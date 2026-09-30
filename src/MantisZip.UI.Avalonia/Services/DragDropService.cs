using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Orchestrates the post-drop extraction workflow:
/// detect target directory → expand items → 统一走 <see cref="ExtractFlow.RunSelectedItemsExtractionAsync"/>
/// （与右键「解压选中项到」完全同一流程：进度窗口、批处理列表、状态驱动、冲突、取消、失败弹窗）。
/// 拿到目标路径后与右键不再有独立逻辑。
/// </summary>
internal class DragDropService
{
    private readonly string _archivePath;
    private readonly string? _password;
    private readonly Window _ownerWindow;
    private readonly AppSettings _settings;
    private readonly string _currentFolder;

    public DragDropService(string archivePath, string? password, Window ownerWindow, string currentFolder)
    {
        _archivePath = archivePath;
        _password = password;
        _ownerWindow = ownerWindow;
        _settings = AppSettings.Load();
        _currentFolder = currentFolder;
    }

    /// <summary>
    /// Execute the full post-drop workflow:
    /// 1. Detect target directory (fallback to the custom extract-folder picker when detection fails)
    /// 2. Expand selected items to flat file list (BEFORE the picker, so it can show the extract preview)
    /// 3. Show modal ProgressWindow and extract via SelectedItemsExtractService (conflicts/cancellation/errors)
    /// 4. Status message, error dialog, optionally open target folder
    /// </summary>
    public async Task ExecuteAfterDropAsync(
        IReadOnlyList<ArchiveItem> selectedItems,
        IReadOnlyList<ArchiveItem> allItems,
        MainWindowViewModel? vm)
    {
        // 1. Detect target directory
        var (targetDir, status) = DropTargetDetector.DetectTargetDirectory();
        App.DebugLog($"[DragDropService] DetectTargetDirectory: targetDir={targetDir ?? "(null)"}, status={status}");

        // 2. 如果在自己的窗口上松开且未检测到目标目录，直接取消（不弹对话框）
        if (string.IsNullOrEmpty(targetDir) && IsOverOwnWindow())
        {
            App.DebugLog("[DragDropService] Dropped on own window — cancelling");
            if (vm != null)
                vm.StatusMessage = "";
            return;
        }

        // 3. Expand selected items to flat file list
        //    提到选路径之前：ExtractFolder 模式的选择器需要条目才能渲染解压路径/冲突预览。
        //    顺带修掉旧顺序的毛病——无可解压内容时也会先弹一次选择器。
        var itemsToExtract = DragDropItemExpander.ExpandItems(selectedItems, allItems);
        App.DebugLog($"[DragDropService] Expanded: {selectedItems.Count} selected → {itemsToExtract.Count} files to extract");
        if (itemsToExtract.Count == 0)
        {
            App.DebugLog("[DragDropService] No files to extract after expansion");
            return;
        }

        // 4. 检测失败时弹自定义选择器（ExtractFolder 模式：底部实时显示解压路径与冲突预览），
        //    与右键「解压选中项到」完全同一对话框。overlay 已在调用前关闭，弹模态窗安全。
        if (string.IsNullOrEmpty(targetDir))
        {
            var parentDir = Path.GetDirectoryName(_archivePath)
                            ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            // 初始目录 = 压缩包同名文件夹（尚不存在也无妨：选择器会回退到最近的已存在父目录，
            // 真正创建由解压流程负责）
            var initialPath = Path.Combine(parentDir, Path.GetFileNameWithoutExtension(_archivePath));

            App.DebugLog("[DragDropService] DetectTargetDirectory returned null, showing custom extract folder picker...");
            targetDir = await CustomFilePickerDialog.ShowExtractFolderAsync(
                _ownerWindow, itemsToExtract, initialPath,
                _currentFolder, _settings.ExtractPreserveFullPath);
            if (string.IsNullOrEmpty(targetDir))
            {
                App.DebugLog("[DragDropService] User cancelled extract folder picker");
                return;
            }
            App.DebugLog($"[DragDropService] User picked folder: {targetDir}");
        }

        // 5. 统一走 ExtractFlow.RunSelectedItemsExtractionAsync（与右键「解压选中项到」完全同一流程）：
        //    进度窗口 + 压缩包一行批处理列表 + 状态驱动（SetCurrentBatchItem/UpdateBatchItemStatus）+
        //    冲突处理 + 取消 + 失败弹窗。拿到目标路径后此处与右键不再有独立逻辑。
        //    盘根目录（如 C:\）的 GetFileName 为空 → 回退用完整路径
        var folderName = Path.GetFileName(targetDir);
        if (string.IsNullOrEmpty(folderName))
            folderName = targetDir;

        var result = await ExtractFlow.RunSelectedItemsExtractionAsync(
            _archivePath, _password, itemsToExtract, targetDir,
            _currentFolder, _settings.ExtractPreserveFullPath, _settings.FileConflictAction,
            vm?.ShowExtractFileConflictDialogAsync,
            LocalizationManager.T("Status_DragExtractingTo", folderName));

        // 6. Post-extraction: status message, optionally open target folder
        //    失败弹窗已由共享方法统一处理（拖拽与右键一致），此处仅设置状态栏消息
        switch (result.Status)
        {
            case SelectedItemsExtractStatus.Failed:
                App.DebugLog($"[DragDropService] Extraction failed: {result.ErrorMessage}");
                if (vm != null)
                    vm.StatusMessage = LocalizationManager.T("Status_DragFailed", result.ErrorMessage);
                break;

            case SelectedItemsExtractStatus.Success:
                App.DebugLog($"[DragDropService] Extraction complete: {itemsToExtract.Count} files to {targetDir}");
                if (vm != null)
                    vm.StatusMessage = LocalizationManager.T("Status_DragDone", itemsToExtract.Count, itemsToExtract.Count, folderName);

                if (_settings.OpenFolderAfterExtract)
                {
                    try
                    {
                        var engine = ArchiveEngineFactory.GetEngineByExtension(_archivePath);
                        var openPath = engine == null
                            ? targetDir
                            : await SmartOpenPathResolver.ResolveSmartOpenPathAsync(_archivePath, targetDir, engine, _password);
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = openPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DragDropService] Failed to open folder: {ex.Message}");
                    }
                }
                break;

            case SelectedItemsExtractStatus.Cancelled:
                if (vm != null)
                    vm.StatusMessage = LocalizationManager.T("Status_DragCancelled");
                break;
        }
    }

    /// <summary>
    /// Check if the cursor is currently over our own application window.
    /// Uses HWND comparison against the owner window (mirrors OverlayController) —
    /// the class-name heuristic misidentifies other Avalonia apps since all
    /// Avalonia windows share the "Avalonia-" prefix.
    /// </summary>
    private bool IsOverOwnWindow()
    {
        var hWnd = NativeMethods.GetWindowUnderCursor();
        if (hWnd == nint.Zero)
            return false;
        var rootTarget = NativeMethods.GetAncestor(hWnd, 2); // GA_ROOT = 2
        if (rootTarget == nint.Zero)
            rootTarget = hWnd;
        var mainHwnd = _ownerWindow.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        return mainHwnd != nint.Zero && rootTarget == mainHwnd;
    }
}
