using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Converters;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Layout;
using Avalonia.Media;
using MantisZip.UI.Avalonia.Services;
using System.ComponentModel;

namespace MantisZip.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _isOwnDrag;
    private PointerPressedEventArgs? _dragStartEvent;
    private Point _dragStartPoint;
    private List<ArchiveItem>? _dragPreservedSelection;
    private string? _lastSortMemberPath;
    private bool _lastSortDescending;

    /// <summary>拖拽时写入数据对象的自定义格式名（使 IDataObject 非空，避免 Explorer 显示禁止光标）</summary>
    private const string MantisZipDragFormatName = "MantisZipDragFormat";

    /// <summary>拖拽期间是否被取消（Esc 或右键取消手势，由自实现 IDropSource.QueryContinueDrag 同步置位）</summary>
    private bool _dragCancelled;

    public MainWindow()
    {
        InitializeComponent();

        var columnStates = WindowStateManager.Load(this);
        ApplyColumnStates(columnStates);

        var vm = new MainWindowViewModel();
        vm.GetOpenFilePath = OpenFileDialogAsync;
        vm.ShowSettingsWindow = async () =>
        {
            var dialog = new SettingsWindow();
            await dialog.ShowDialog(this);
            // 设置窗口可能改动了主题，刷新主窗口菜单里「切换颜色模式」的当前主题文案
            vm.RefreshLocalizedStrings();
        };
        vm.ShowPasswordDialog = async (archivePath) =>
        {
            var dialog = new PasswordDialog(Path.GetFileName(archivePath));
            var result = await dialog.ShowDialog<bool>(this);
            if (!result) return null;
            return new PasswordDialogResponse
            {
                Password = dialog.Password,
                RememberInSession = dialog.RememberInSession,
                SavePermanently = dialog.SavePermanently,
                Description = dialog.Description,
                Patterns = dialog.Patterns
            };
        };
        DataContext = vm;

        // ── Phase 3: Wire up ViewModel dialog callbacks ──

        vm.ShowExtractSettingsDialog = async (evm) =>
        {
            var dialog = new ExtractSettingsWindow(evm.ArchivePaths);

            // Pass archive entries for preview tree
            var allItems = vm.GetAllRawItems();
            if (allItems.Count > 0)
                dialog.SetEntries(allItems);

            var result = await dialog.ShowDialog<bool>(this);
            if (result)
            {
                evm.DestinationPath = dialog.ViewModel.DestinationPath;
                evm.ConflictAction = dialog.ViewModel.ConflictAction;
                evm.OpenFolderAfterExtract = dialog.ViewModel.OpenFolderAfterExtract;
                // 文件过滤：仅当启用过滤且有条目时，将匹配条目 key 回传，供实际解压只解压匹配项
                evm.FilteredEntryKeys = dialog.GetFilteredEntryKeys();
            }
            return result;
        };

        vm.ShowExtractFolderPicker = (entries, initialPath, currentFolder, preserveFullPath) =>
            CustomFilePickerDialog.ShowExtractFolderAsync(this, entries, initialPath, currentFolder, preserveFullPath);

        vm.ShowCompressSettingsDialog = async (cvm) =>
        {
            var dialog = new CompressSettingsWindow(cvm.SelectedPaths);
            var result = await dialog.ShowDialog<bool>(this);
            if (result)
            {
                // 复制 SelectedPaths：对话框有自己的 ViewModel，用户添加的文件仅存在 dialog.ViewModel 中
                cvm.SelectedPaths.Clear();
                foreach (var p in dialog.ViewModel.SelectedPaths)
                    cvm.SelectedPaths.Add(p);

                cvm.DefaultFormat = dialog.ViewModel.DefaultFormat;
                cvm.CompressionLevel = dialog.ViewModel.CompressionLevel;
                cvm.OutputMode = dialog.ViewModel.OutputMode;
                cvm.OutputPath = dialog.ViewModel.OutputPath;
                cvm.Password = dialog.ViewModel.Password;
                cvm.Encrypt = dialog.ViewModel.Encrypt;
                cvm.IsPasswordLibraryMode = dialog.ViewModel.IsPasswordLibraryMode;
                cvm.SelectedPasswordEntry = dialog.ViewModel.SelectedPasswordEntry;
                cvm.Comment = dialog.ViewModel.Comment;
                cvm.CommentDistribution = dialog.ViewModel.CommentDistribution;
                cvm.FileFilter = dialog.GetFilter();

                // 高级格式选项（仅本次压缩生效，来源为对话框面板快照，不再经 AppSettings 中转）
                cvm.FileNameEncoding = dialog.ViewModel.FileNameEncoding;
                cvm.ZipCompressionMethod = dialog.ViewModel.ZipCompressionMethod;
                cvm.ZipEncryptionMethod = dialog.ViewModel.ZipEncryptionMethod;
                cvm.SevenZipCompressionMethod = dialog.ViewModel.SevenZipCompressionMethod;
                cvm.SevenZipSolid = dialog.ViewModel.SevenZipSolid;
                cvm.SevenZipSolidBlockSize = dialog.ViewModel.SevenZipSolidBlockSize;
                cvm.SevenZipDictionarySize = dialog.ViewModel.SevenZipDictionarySize;
                cvm.SevenZipNumFastBytes = dialog.ViewModel.SevenZipNumFastBytes;
                cvm.SevenZipMatchFinder = dialog.ViewModel.SevenZipMatchFinder;
                cvm.SevenZipEncryptHeaders = dialog.ViewModel.SevenZipEncryptHeaders;
                // 分卷设置（同样仅本次生效；此前未复制导致 cvm.SplitSize 恒为 0，对话框分卷选择丢失）
                cvm.SelectedSplitSizeOption = dialog.ViewModel.SelectedSplitSizeOption;
                cvm.CustomSplitSizeText = dialog.ViewModel.CustomSplitSizeText;
            }
            return result;
        };

        vm.ShowPasswordManager = async () =>
        {
            var dialog = new PasswordManagerWindow();
            await dialog.ShowDialog(this);
        };

        vm.ShowAboutDialog = async () =>
        {
            var dialog = new AboutWindow();
            await dialog.ShowDialog(this);
        };

        vm.ShowFavoritesDialog = async () =>
        {
            var dialog = new FavoriteManagerWindow();
            await dialog.ShowDialog(this);
        };

        vm.RunWithProgress = async (title, filePaths, operation) =>
        {
            var pw = new ProgressWindow(title);
            pw.InitCancellation();
            var hasFileList = filePaths is { Count: > 0 };

            // 批处理状态上报：操作闭包内经 BatchStatusReporter 传给引擎 onItemStatus
            vm.BatchStatusReporter = (index, status) =>
            {
                pw.SetCurrentBatchItem(index);
                pw.UpdateBatchItemStatus(index, status);
            };

            try
            {
                pw.Show();
                if (hasFileList)
                {
                    pw.InitBatchMode(filePaths!);
                    pw.SetCurrentBatchItem(0);
                }

                var progress = pw.CreatePauseAwareProgress(
                    ProgressViewModel.CreateBackgroundProgress(pw, p => pw.SetProgress(p)));
                await operation(progress, pw.CancellationToken);

                if (hasFileList)
                    pw.UpdateBatchItemStatus(0, BatchItemStatus.Completed);

                // 成功：标记完成态 + 尊重 📌 KeepOpenOnComplete（对齐 WPF MainWindow.Menu.cs AutoCloseOrWaitAsync(0, ...)）
                pw.SetComplete(LocalizationManager.T("Cli_StatusDone"));
                await pw.AutoCloseOrWaitAsync(0, () => pw.Close());
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception)
            {
                if (hasFileList)
                    pw.UpdateBatchItemStatus(0, BatchItemStatus.Failed);
                return false;
            }
            finally
            {
                pw.Close();
                vm.BatchStatusReporter = null;
            }
        };

        vm.CopyToClipboard = async (text) =>
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    var transfer = new global::Avalonia.Input.DataTransfer();
                    var item = new global::Avalonia.Input.DataTransferItem();
                    item.SetText(text);
                    transfer.Add(item);
                    await topLevel.Clipboard.SetDataAsync(transfer);
                }
            }
            catch
            {
                // Clipboard not available in this environment
            }
        };

        vm.ShowCommentDialog = async (existingComment) =>
        {
            var dialog = new CommentDialog(existingComment);
            var result = await dialog.ShowDialog<bool>(this);
            return result ? dialog.Comment : null;
        };

        // ════════════════════════════════════════════════
        //  压缩/解压冲突对话框回调（从后台线程调用）
        // ════════════════════════════════════════════════

        // 弹窗逻辑统一走 CompressFlow.ShowConflictDialogAsync（与 CLI 右键菜单共用），
        // 本回调仅把 owner 窗口（MainWindow）与 VM 委托接线
        vm.ShowCompressConflictDialog = info => CompressFlow.ShowConflictDialogAsync(this, info);

        vm.ShowExtractFileConflictDialogAsync = async info =>
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new ConflictDialog(info);
                await dlg.ShowDialog(this);

                // 用户选择"取消整个操作" → 抛异常终止解压（与拖拽原有语义一致）
                if (dlg.CancelOperation)
                    throw new OperationCanceledException("用户取消整个解压操作");

                if (dlg.ResultAction == FileConflictAction.Rename && !string.IsNullOrEmpty(dlg.CustomName))
                {
                    info.CustomName = dlg.CustomName;
                }

                return (dlg.ResultAction, dlg.ApplyToAll);
            });
        };

        // ── Wire up select-all / invert-selection callbacks ──
        vm.SelectAllEntriesAction = () =>
        {
            FileListGrid.SelectedItems.Clear();
            if (FileListGrid.ItemsSource is System.Collections.IList source)
            {
                foreach (var item in source)
                {
                    if (item is ArchiveItemModel)
                        FileListGrid.SelectedItems.Add(item);
                }
            }
        };
        vm.InvertSelectionAction = () =>
        {
            var selected = new HashSet<object>();
            foreach (var item in FileListGrid.SelectedItems)
                selected.Add(item);
            var allItems = new List<object>();
            if (FileListGrid.ItemsSource is System.Collections.IList source)
            {
                foreach (var item in source)
                    allItems.Add(item);
            }
            FileListGrid.SelectedItems.Clear();
            foreach (var item in allItems)
            {
                if (!selected.Contains(item))
                    FileListGrid.SelectedItems.Add(item);
            }
        };

        vm.GetOpenFilePaths = async () =>
        {
            var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationManager.T("Main_SelectFilesTitle"),
                AllowMultiple = true
            });
            return result.Count > 0 ? result.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList() : null;
        };

        // ── Wire up metadata panel settings → open Settings window ──
        vm.Preview.OpenSettingsToMetadataTab = async () =>
        {
            if (vm.ShowSettingsWindow != null)
                await vm.ShowSettingsWindow();
        };

        // Setup drag-drop from file list
        var fileGrid = this.FindControl<DataGrid>("FileListGrid");
        if (fileGrid != null)
        {
            fileGrid.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
            {
                _dragStartPoint = e.GetPosition(fileGrid);
                _dragStartEvent = e;

                // 命中测试：按下行是否已属于当前选区（镜像 WPF InputHitTest 语义）。
                // Tunnel 阶段先于 DataGrid 行自身的选中处理，此时 SelectedItems 仍是旧选区；
                // 若按下的是未选中行，则不保留旧多选区 —— 拖拽只拖新按下的行。
                var pressedItem = HitTestPressedRowItem(fileGrid, _dragStartPoint);
                var pressedInSelection = pressedItem != null
                    && fileGrid.SelectedItems.Contains(pressedItem);

                // Save multi-selection state at press time (before drag starts)
                if (fileGrid.SelectedItems.Count > 1 && pressedInSelection)
                {
                    _dragPreservedSelection = fileGrid.SelectedItems
                        .OfType<ArchiveItemModel>()
                        .Select(m => m.ToCoreItem())
                        .ToList();
                }
                else
                {
                    _dragPreservedSelection = null;
                }
            }, RoutingStrategies.Tunnel);

            // Clear drag state on release to prevent click-selection from triggering drag
            fileGrid.AddHandler(InputElement.PointerReleasedEvent, (s, e) =>
            {
                _dragStartEvent = null;
                _dragPreservedSelection = null;
            }, RoutingStrategies.Tunnel);

            fileGrid.PointerMoved += async (s, e) =>
            {
                if (_dragStartEvent == null) return;

                var pos = e.GetPosition(fileGrid);
                var delta = pos - _dragStartPoint;
                // 拖拽启动阈值：镜像 WPF SystemParameters.MinimumHorizontalDragDistance (~4px)，
                // 避免 32px 造成的"拖起来黏手"感
                if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
                    return;

                // 设置开关：EnableDragExtract = false 时不启动拖拽（与 WPF MainWindow.DragDrop.cs 行为一致）
                if (!AppSettings.Load().EnableDragExtract)
                {
                    App.DebugLog("[MainWindow] EnableDragExtract off — drag skipped");
                    _dragStartEvent = null;
                    _dragPreservedSelection = null;
                    return;
                }

                // Save trigger event before nulling (Avalonia DragDrop.DoDragDropAsync needs it)
                var triggerEvent = _dragStartEvent;
                _dragStartEvent = null; // Prevent re-entry

                var vm2 = DataContext as MainWindowViewModel;
                if (vm2?.SelectedEntry == null) return;
                var archivePath = vm2.CurrentArchivePath;
                if (string.IsNullOrEmpty(archivePath)) return;

                // Get selected items (support multi-select)
                var selectedItems = _dragPreservedSelection
                    ?? new List<ArchiveItem> { vm2.SelectedEntry.ToCoreItem() };
                var allItems = vm2.GetAllRawItems();

                var password = vm2.GetSessionPassword(archivePath);

                // Expand items: directories become their contained files (flat list)
                var expandedItems = DragDropItemExpander.ExpandItems(selectedItems, allItems);
                if (expandedItems.Count == 0)
                    return;

                _isOwnDrag = true;
                vm2.StatusMessage = LocalizationManager.T("Status_DragHint");

                // ── Create Avalonia overlay window (works on UI thread, controlled via Win32 from background) ──
                var overlayWin = new Window
                {
                    ShowInTaskbar = false,
                    Background = Brushes.Transparent,
                    Width = 1,
                    Height = 1,
                    Topmost = true,
                    ShowActivated = false,
                };
                overlayWin.Show();

                var overlayHwnd = overlayWin.TryGetPlatformHandle()?.Handle ?? nint.Zero;
                App.DebugLog($"[MainWindow] Overlay HWND=0x{overlayHwnd:X}");
                if (overlayHwnd != nint.Zero)
                {
                    var exStyle = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE);
                    NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE,
                        exStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                                | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

                    // Remove title bar via Win32 (avoids Avalonia 12 enum compatibility issues)
                    var overlayStyle = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE);
                    NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE,
                        overlayStyle & ~0x00C00000u); // clear WS_CAPTION (title bar)

                }

                var mainHwnd = this.TryGetPlatformHandle()?.Handle ?? nint.Zero;
                App.DebugLog($"[MainWindow] Main HWND=0x{mainHwnd:X}");
                using var controller = new OverlayController(overlayHwnd, mainHwnd);
                controller.Start();

                // 拖拽光标：按 overlay 状态加载不同 .cur，源文件位于项目 Resources\Cursors\，
                // 构建时复制到输出目录的 Resources\Cursors\ 下（与 MenuIcons 同模式）。
                // 状态→文件约定：
                //   None/默认        → DragCursor.cur     （金色）
                //   Success(可放置)  → DragCursorOk.cur   （绿色）
                //   Warning(警告)    → DragCursorWarn.cur （红色）
                //   自家窗口         → DragCursorSelf.cur （灰色）
                // 缺失文件回退基础 DragCursor.cur，再回退系统标准箭头（共享句柄，不可销毁）。
                // 仅从文件加载的自定义句柄在拖拽结束后销毁。
                var cursorHandles = new HashSet<nint>();
                var cursorsDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Cursors");

                nint LoadStateCursor(string fileName, nint fallback)
                {
                    var path = Path.Combine(cursorsDir, fileName);
                    if (File.Exists(path))
                    {
                        var h = NativeMethods.LoadCursorFromFile(path);
                        if (h != nint.Zero)
                        {
                            cursorHandles.Add(h);
                            return h;
                        }
                    }
                    return fallback;
                }

                var baseCursor = LoadStateCursor("DragCursor.cur", nint.Zero);
                if (baseCursor == nint.Zero)
                    baseCursor = NativeMethods.LoadCursor(nint.Zero, new nint(NativeMethods.OCR_NORMAL));
                var okCursor = LoadStateCursor("DragCursorOk.cur", baseCursor);
                var warnCursor = LoadStateCursor("DragCursorWarn.cur", baseCursor);
                var selfCursor = LoadStateCursor("DragCursorSelf.cur", baseCursor);

                // 每次 GiveFeedback 按 overlay 当前状态动态选光标（与覆层颜色同一状态源）
                Func<nint> cursorProvider = () =>
                {
                    if (controller.IsOverOwnWindow)
                        return selfCursor;
                    return controller.CurrentStatus switch
                    {
                        DropTargetDetector.DropTargetStatus.Success => okCursor,
                        DropTargetDetector.DropTargetStatus.Warning => warnCursor,
                        _ => baseCursor
                    };
                };

                try
                {
                    // 自实现 OLE 拖拽：GiveFeedback 返回 S_OK 并直接 SetCursor 自定义光标。
                    // Avalonia 的 DoDragDropAsync 内部固定返回 USEDEFAULTCURSORS，会让系统用
                    // LoadCursor(OCR_NO) 显示禁止光标，而替换 OCR_NO 资源表在本机无效（已实证），
                    // 因此绕开它自行控制光标。Esc 由 QueryContinueDrag 的 fEscapePressed 处理。
                    App.DebugLog("[MainWindow] Custom OLE DoDragDrop START");
                    _dragCancelled = false;
                    var result = CustomOleDragDrop.PerformDragDrop(
                        triggerEvent, MantisZipDragFormatName, archivePath,
                        cursorProvider, DragDropEffects.Copy,
                        () => _dragCancelled = true);
                    App.DebugLog($"[MainWindow] Custom OLE DoDragDrop DONE: result={result}");

                    // Close overlay IMMEDIATELY after drag completes, before dialog processing
                    controller.Stop();
                    overlayWin.Close();
                    App.DebugLog("[MainWindow] Overlay closed");

                    if (_dragCancelled)
                    {
                        // 用户按 Esc 或右键取消拖拽 → 不执行解压
                        App.DebugLog("[MainWindow] Drag cancelled during drag — extraction skipped");
                        if (vm2 != null)
                            vm2.StatusMessage = LocalizationManager.T("Status_DragDragCancelled");
                    }
                    else
                    {
                        NativeMethods.GetCursorPos(out var dropPt);
                        App.DebugLog($"[MainWindow] Drop point captured: ({dropPt.X}, {dropPt.Y})");

                        if (vm2 != null && !string.IsNullOrEmpty(archivePath))
                        {
                            vm2.StatusMessage = LocalizationManager.T("Status_DragDetectingTarget");
                            var dragService = new DragDropService(
                                archivePath, password, this, vm2.CurrentFolder ?? "");
                            await dragService.ExecuteAfterDropAsync(
                                selectedItems, allItems, vm2);
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.DebugLog($"[MainWindow] DragDrop EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    // 释放自定义光标句柄（仅从文件加载的；共享标准箭头不销毁）
                    foreach (var h in cursorHandles)
                        NativeMethods.DestroyIcon(h);
                    // Safety net: ensure overlay is closed even if early close was skipped
                    try { overlayWin.Close(); } catch { }
                    try { controller.Stop(); } catch { }
                    _isOwnDrag = false;
                    App.DebugLog("[MainWindow] DragDrop cleanup done");
                }
            };
        }

        // Prevent reacting to our own drag-drop
        this.AddHandler(DragDrop.DropEvent, (s, e) =>
        {
            if (_isOwnDrag)
                e.Handled = true;
        });

        // Persist window position/size/state + column widths on close
        Closing += (_, _) => WindowStateManager.Save(this, CaptureColumnStates());
    }

    /// <summary>
    /// 将 window.json 中保存的列状态应用到 FileListGrid（按 ColumnId=SortMemberPath 匹配）。
    /// 名称列不允许隐藏；无 SortMemberPath 的列（图标列）不参与。
    /// </summary>
    private void ApplyColumnStates(List<WindowStateManager.ColumnStateDto>? states)
    {
        if (states == null || states.Count == 0)
            return;

        try
        {
            var columnDict = new Dictionary<string, DataGridColumn>();
            foreach (var col in FileListGrid.Columns)
            {
                if (!string.IsNullOrEmpty(col.SortMemberPath) && !columnDict.ContainsKey(col.SortMemberPath))
                    columnDict[col.SortMemberPath] = col;
            }

            // 按保存的 DisplayIndex 升序应用，避免设置顺序冲突
            foreach (var state in states.Where(s => !string.IsNullOrEmpty(s.ColumnId))
                                        .OrderBy(s => s.DisplayIndex))
            {
                if (state.ColumnId == null || !columnDict.TryGetValue(state.ColumnId, out var col))
                    continue;

                if (state.Width > 0)
                    col.Width = new DataGridLength(state.Width);

                // 名称列不可隐藏
                if (state.ColumnId != "Name")
                    col.IsVisible = state.Visible;

                col.DisplayIndex = state.DisplayIndex;
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"ApplyColumnStates: failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 捕获 FileListGrid 各列的宽度/可见性/顺序快照（仅限有 SortMemberPath 的列），
    /// 供 WindowStateManager.Save 持久化到 window.json。
    /// </summary>
    private List<WindowStateManager.ColumnStateDto>? CaptureColumnStates()
    {
        try
        {
            var states = new List<WindowStateManager.ColumnStateDto>();
            foreach (var col in FileListGrid.Columns)
            {
                if (string.IsNullOrEmpty(col.SortMemberPath))
                    continue;

                states.Add(new WindowStateManager.ColumnStateDto
                {
                    ColumnId = col.SortMemberPath,
                    Width = col.Width.Value,
                    Visible = col.IsVisible,
                    DisplayIndex = col.DisplayIndex
                });
            }
            return states;
        }
        catch (Exception ex)
        {
            App.DebugLog($"CaptureColumnStates: failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 命中测试：返回按下位置所在行的数据项（ArchiveItemModel）；未命中任何行时返回 null。
    /// 镜像 WPF MainWindow.DragDrop.cs 的 InputHitTest 语义：判断按下的行是否已在当前选区中。
    /// </summary>
    private static ArchiveItemModel? HitTestPressedRowItem(DataGrid grid, Point position)
    {
        var hit = grid.InputHitTest(position);
        for (var v = hit as Visual; v != null; v = v.GetVisualParent())
        {
            if (v is DataGridRow row && row.DataContext is ArchiveItemModel model)
                return model;
        }
        return null;
    }

    private async void FileListGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not ArchiveItemModel item) return;
        if (DataContext is not MainWindowViewModel vm) return;

        if (item.IsDirectory)
        {
            vm.NavigateToFolderPath(item.FullPath);
            return;
        }

        // 文件双击：提取到临时目录并用系统默认方式打开
        if (!string.IsNullOrEmpty(vm.CurrentArchivePath))
            await vm.OpenEntryWithDefaultAppAsync(item);
    }

    private void FileListGrid_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not ArchiveItemModel item) return;
        if (DataContext is not MainWindowViewModel vm) return;

        switch (e.Key)
        {
            case global::Avalonia.Input.Key.Enter:
                if (item.IsDirectory)
                {
                    vm.NavigateToFolderPath(item.FullPath);
                    e.Handled = true;
                }
                break;

            case global::Avalonia.Input.Key.Back:
                vm.GoUpCommand.Execute(null);
                e.Handled = true;
                break;

            case global::Avalonia.Input.Key.Delete:
                vm.DeleteFilesCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void FileListGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        if (sender is not DataGrid grid) return;

        // 清除所有列标题上的排序标记，恢复原始文字
        foreach (var column in grid.Columns)
        {
            if (column.Header is string headerText)
                column.Header = headerText.TrimEnd('▲', '▼', ' ').TrimEnd();
        }

        var col = e.Column;
        var sortMemberPath = col.SortMemberPath;

        // 推算新方向：切换同一列时翻转，新列默认为升序
        if (_lastSortMemberPath != sortMemberPath)
        {
            _lastSortDescending = false;
        }
        else
        {
            _lastSortDescending = !_lastSortDescending;
        }
        _lastSortMemberPath = sortMemberPath;

        // 阻止默认排序（改用自定义手动排序）
        e.Handled = true;

        // 更新列头箭头
        if (col.Header is string header)
        {
            var clean = header.TrimEnd('▲', '▼', ' ').TrimEnd();
            col.Header = _lastSortDescending ? clean + " ▼" : clean + " ▲";
        }

        // 手动排序：.. 导航行 → 目录 → 文件，组内排序
        if (DataContext is MainWindowViewModel vm)
        {
            var entries = vm.CurrentEntries.ToList();

            List<ArchiveItemModel> sorted;
            if (_lastSortDescending)
            {
                sorted = entries
                    .OrderBy(e => e.Name == ".." ? 0 : e.IsDirectory ? 1 : 2)
                    .ThenByDescending(e => GetSortValue(e, sortMemberPath))
                    .ToList();
            }
            else
            {
                sorted = entries
                    .OrderBy(e => e.Name == ".." ? 0 : e.IsDirectory ? 1 : 2)
                    .ThenBy(e => GetSortValue(e, sortMemberPath))
                    .ToList();
            }

            vm.CurrentEntries.Clear();
            foreach (var item in sorted)
                vm.CurrentEntries.Add(item);
        }
    }

    private static IComparable GetSortValue(ArchiveItemModel item, string memberPath)
    {
        return memberPath switch
        {
            "Name" or "NameDisplay" => item.NameDisplay,
            "Size" => item.Size,
            "CompressedSize" => item.CompressedSize,
            "LastModified" => item.LastModified,
            "CompressionRatio" => item.CompressionRatio,
            _ => item.NameDisplay
        };
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        // Accept drop if files are being dragged (check for File format)
        if (e.DataTransfer != null && e.DataTransfer.Formats.Contains(DataFormat.File))
            e.DragEffects = DragDropEffects.Copy;
        else
            e.DragEffects = DragDropEffects.None;
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer == null) return;

        foreach (var item in e.DataTransfer.Items)
        {
            var raw = item.TryGetRaw(DataFormat.File);
            if (raw is IStorageFile storageFile)
            {
                var path = storageFile.TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) continue;

                if (ArchiveFormatHelper.IsArchiveFile(path))
                {
                    var vm = DataContext as MainWindowViewModel;
                    if (vm != null)
                        await vm.LoadArchiveAsync(path);
                    return; // Only open the first matching archive
                }
            }
        }
    }

    private void FileListGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.SelectedEntries.Clear();
        foreach (var item in FileListGrid.SelectedItems)
        {
            if (item is ArchiveItemModel model)
                vm.SelectedEntries.Add(model);
        }
    }

    public async void LoadArchiveOnStartup(string path)
    {
        // Wait for window to be ready
        await Task.Delay(100); // Small delay to let the window initialize
        var vm = DataContext as MainWindowViewModel;
        if (vm != null)
        {
            await vm.LoadArchiveAsync(path);
        }
    }

    private async Task<string?> OpenFileDialogAsync()
    {
        // 以当前已打开压缩包的所在目录作为「场景相关路径」初值（无则 null → 走优先级链其它来源）
        var contextPath = (DataContext as MainWindowViewModel)?.CurrentArchivePath is { } c
            ? Path.GetDirectoryName(c)
            : null;
        return await CustomFilePickerDialog.ShowOpenFileAsync(
            this,
            initialPath: contextPath,
            fileExtensions:
            [
                "*.zip", "*.7z", "*.rar", "*.tar", "*.tgz", "*.tar.gz", "*.gz", "*.iso"
            ]);
    }

    // ── Filter picker buttons (🧪 pick from selected items) ──

    private void PickDateFrom_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;
        try
        {
            if (vm.SelectedEntries.Count == 0)
            {
                WritePickerTrace("PickDateFrom: SelectedEntries is empty");
                return;
            }
            var dates = vm.SelectedEntries
                .Where(i => !i.IsDirectory && i.LastModified > DateTime.MinValue)
                .Select(i => i.LastModified)
                .ToList();
            if (dates.Count == 0)
            {
                WritePickerTrace("PickDateFrom: no valid dates, entries=" + vm.SelectedEntries.Count);
                return;
            }
            var minDate = dates.Min();
            WritePickerTrace($"PickDateFrom: minDate={minDate:O}, picker.IsNull={FilterDateFromPicker == null}, vm.IsNull={vm == null}");
            vm.FilterDateFrom = minDate;
            if (FilterDateFromPicker != null)
                FilterDateFromPicker.SelectedDate = minDate;
        }
        catch (Exception ex)
        {
            WritePickerTrace($"PickDateFrom ERROR: {ex}");
        }
    }

    private void PickDateTo_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;
        try
        {
            if (vm.SelectedEntries.Count == 0)
            {
                WritePickerTrace("PickDateTo: SelectedEntries is empty");
                return;
            }
            var dates = vm.SelectedEntries
                .Where(i => !i.IsDirectory && i.LastModified > DateTime.MinValue)
                .Select(i => i.LastModified)
                .ToList();
            if (dates.Count == 0)
            {
                WritePickerTrace("PickDateTo: no valid dates");
                return;
            }
            var maxDate = dates.Max();
            vm.FilterDateTo = maxDate;
            if (FilterDateToPicker != null)
                FilterDateToPicker.SelectedDate = maxDate;
        }
        catch (Exception ex)
        {
            WritePickerTrace($"PickDateTo ERROR: {ex}");
        }
    }

    private static void WritePickerTrace(string msg)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MantisZip", "debug.log");
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [PN] {msg}\n");
        }
        catch { }
    }

    private void PickSizeMin_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.SelectedEntries.Count == 0) return;
        var sizes = vm.SelectedEntries
            .Where(i => !i.IsDirectory)
            .Select(i => i.Size)
            .ToList();
        if (sizes.Count == 0) return;
        vm.FilterSizeMin = sizes.Min();
        vm.FilterSizeUnit = "B";
    }

    private void PickSizeMax_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.SelectedEntries.Count == 0) return;
        var sizes = vm.SelectedEntries
            .Where(i => !i.IsDirectory)
            .Select(i => i.Size)
            .ToList();
        if (sizes.Count == 0) return;
        vm.FilterSizeMax = sizes.Max();
        vm.FilterSizeUnit = "B";
    }

    private void AddressBar_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == global::Avalonia.Input.Key.Enter)
        {
            if (sender is AutoCompleteBox box)
            {
                var vm = DataContext as MainWindowViewModel;
                vm?.NavigateToFolderPath(box.Text ?? "");
            }
            e.Handled = true;
        }
    }

    private void ColumnHeaderContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Items.Clear();

        foreach (var column in FileListGrid.Columns)
        {
            var header = GetColumnHeaderText(column);
            // 名称列不允许隐藏（图标列无表头文字，同样跳过）
            if (header == LocalizationManager.T("DataGrid_Name") || string.IsNullOrEmpty(header))
                continue;

            // 与主菜单切换图标同构：ToggleIconBox（可见=强调色底，隐藏=透明空心）
            var iconBox = new Border
            {
                Classes = { "ToggleIconBox" },
                Background = ToggleIconBackground(column.IsVisible),
                Child = GetColumnHeaderIcon(column) is { } iconData
                    ? new PathIcon
                    {
                        Data = iconData,
                        Width = 12,
                        Height = 12,
                        Foreground = GetThemeBrush("ThemeTextPrimaryBrush")
                    }
                    : null
            };

            // 与主菜单切换项同构：Header 内 StackPanel（ToggleIconBox + 文字）
            var menuItem = new MenuItem
            {
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = GetSpacingXxs(),
                    Children =
                    {
                        iconBox,
                        new TextBlock { Text = header }
                    }
                },
                Tag = column
            };
            menuItem.Click += (s, args) =>
            {
                column.IsVisible = !column.IsVisible;
                iconBox.Background = ToggleIconBackground(column.IsVisible);
            };
            menu.Items.Add(menuItem);
        }
    }

    /// <summary>可见 → ThemeToggleBrush（强调色底），隐藏 → Transparent（空心），与主菜单切换图标一致。</summary>
    private static IBrush ToggleIconBackground(bool isVisible)
    {
        return new BoolToToggleBgBrushConverter().Convert(isVisible, typeof(IBrush), null, CultureInfo.InvariantCulture) as IBrush
            ?? Brushes.Transparent;
    }

    /// <summary>从列标题的 StackPanel 中提取 PathIcon 的几何，与列头图标同款，保证视觉一致。</summary>
    private static Geometry? GetColumnHeaderIcon(DataGridColumn column)
    {
        if (column.Header is StackPanel panel)
        {
            var icon = panel.Children.OfType<PathIcon>().FirstOrDefault();
            if (icon != null)
                return icon.Data;
        }
        return null;
    }

    /// <summary>从当前应用资源中取主题画刷（主题切换后动态解析）。</summary>
    private static IBrush GetThemeBrush(string key)
    {
        if (Application.Current?.TryFindResource(key, out var brush) == true && brush is IBrush b)
            return b;
        return Brushes.Gray;
    }

    /// <summary>解析紧凑度间距资源（与主菜单 Header StackPanel 的 SpacingXxs 一致）。</summary>
    private static double GetSpacingXxs()
    {
        if (Application.Current?.TryFindResource("SpacingXxs", out var spacing) == true && spacing is double d)
            return d;
        return 4;
    }

    private static string GetColumnHeaderText(DataGridColumn column)
    {
        if (column.Header is string s)
            return s.TrimEnd('▲', '▼', ' ').TrimEnd();
        if (column.Header is StackPanel panel)
        {
            var tb = panel.Children.OfType<TextBlock>().LastOrDefault();
            if (tb != null)
                return tb.Text ?? "";
        }
        return "";
    }

    private async void RecentFileMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is string filePath)
        {
            if (DataContext is MainWindowViewModel vm && vm.OpenRecentFileCommand.CanExecute(filePath))
            {
                vm.OpenRecentFileCommand.Execute(filePath);
            }
        }
    }

    private async void TestWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string tag)
            return;

        // AppMessageBox uses static Show() — handle separately
        if (tag == "AppMessageBox")
        {
            await AppMessageBox.Show("这是一个测试消息框\n可用于测试消息弹窗的显示效果。",
                "测试", MessageBoxButton.OKCancel, MessageBoxImage.Information, this);
            return;
        }

        Window? window = tag switch
        {
            "IconTestWindow" => new Dialogs.IconTestWindow(),
            "AboutWindow" => new AboutWindow(),
            "SettingsWindow" => new Views.SettingsWindow(),
            "PasswordManagerWindow" => new PasswordManagerWindow(),
            "DonationDialog" => new DonationDialog(),
            "LogPrivacyHelpDialog" => new LogPrivacyHelpDialog(),
            "PasswordHelpDialog" => new PasswordHelpDialog(),
            "CommentDialog" => new CommentDialog(),
            "PasswordEditDialog" => new PasswordEditDialog(),
            "PasswordDialog" => new PasswordDialog("test.7z"),
            "ProgressWindow" => new ProgressWindow("测试进度窗口"),
            "ErrorDialog" => new ErrorDialog(new FileErrorInfo { FilePath = @"C:\test\test.zip", ErrorMessage = "这是一个测试错误信息\n可用于测试错误对话框的显示效果。" }),
            "CompressSettingsWindow" => new CompressSettingsWindow(),
            "ExtractSettingsWindow" => new ExtractSettingsWindow(),
            "CompressConflictDialog" => new CompressConflictDialog(@"C:\test\file.txt", "file.txt"),
            "ConflictDialog" => new ConflictDialog(new FileConflictInfo { FilePath = @"C:\existing\file.txt" }),
            "MatchedPasswordDialog" => new MatchedPasswordDialog(new PasswordEntry { Description = "测试密码", Patterns = { "*.zip" }, Password = "test123" }, "test.zip"),
            "ElevationDialog" => new ElevationDialog(new[] { @"C:\Protected\Dir" }),
            "ElevationFailedDialog" => new ElevationFailedDialog(new[] { @"C:\Protected\Dir" }),
            "ElevationInfoDialog" => new ElevationInfoDialog(new[] { @"C:\Protected\Dir" }),
            "AddFavoriteDialog" => new AddFavoriteDialog(),
            "FavoriteManagerWindow" => new FavoriteManagerWindow(),
            "ArchiveCommentDialog" => new ArchiveCommentDialog(@"C:\test.zip", ArchiveFormat.Zip, "测试注释"),
            _ => null
        };

        if (window != null)
        {
            await window.ShowDialog(this);
        }
    }
}
