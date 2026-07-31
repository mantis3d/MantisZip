using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.Core;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Layout;
using Avalonia.Media;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _isOwnDrag;
    private PointerPressedEventArgs? _dragStartEvent;
    private Point _dragStartPoint;
    private List<ArchiveItem>? _dragPreservedSelection;
    private string? _lastSortMemberPath;
    private bool _lastSortDescending;

    /// <summary>拖拽时写入 DataTransfer 的自定义格式名（使 IDataObject 非空，避免 Explorer 显示禁止光标）</summary>
    private const string MantisZipDragFormatName = "MantisZipDragFormat";

    /// <summary>Esc 检测钩子委托（字段持有防止 GC 回收导致原生回调悬空）</summary>
    private NativeMethods.LowLevelKeyboardProc? _dragEscHookProc;
    /// <summary>拖拽期间是否按下了 Esc</summary>
    private bool _dragEscPressed;

    /// <summary>A 方案：拖拽期间替换 OCR_NO 后保存的原始光标副本（finally 还原用）；nint.Zero 表示未替换</summary>
    private nint _originalNoCursor;

    public MainWindow()
    {
        InitializeComponent();

        WindowStateManager.Load(this);

        var vm = new MainWindowViewModel();
        vm.GetOpenFilePath = OpenFileDialogAsync;
        vm.ShowSettingsWindow = async () =>
        {
            var dialog = new SettingsWindow();
            await dialog.ShowDialog(this);
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
            }
            return result;
        };

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

        vm.ShowQuickPathDialog = async (isFolderMode) =>
        {
            var dialog = new QuickPathDialog(isFolderMode) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var result = await dialog.ShowDialog<bool>(this);
            return result ? dialog.SelectedPath : null;
        };

        vm.ShowArchiveSaveAsDialog = async (archivePath) =>
        {
            var dialog = new ArchiveSaveAsDialog(archivePath) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var result = await dialog.ShowDialog<bool>(this);
            return result ? dialog.SavePath : null;
        };

        vm.ShowQuickPathPreDialog = async (isPickFolderMode, isFileOpenMode) =>
        {
            var dialog = new QuickPathPreDialog(isPickFolderMode, isFileOpenMode) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var result = await dialog.ShowDialog<bool>(this);
            return result ? dialog.SelectedPath : null;
        };

        vm.ShowUnifiedExtractDialog = async (presetPath) =>
        {
            var dialog = new UnifiedExtractDialog(this) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
            return await dialog.ShowDialog<bool>(this) ? dialog.SelectedPath : null;
        };

        vm.RunWithProgress = async (title, operation) =>
        {
            var pw = new ProgressWindow(title);
            pw.InitCancellation();

            try
            {
                pw.Show();
                var progress = ProgressViewModel.CreateBackgroundProgress(pw, p => pw.SetProgress(p));
                await operation(progress, pw.CancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                pw.Close();
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

        vm.ShowCompressConflictDialog = async info =>
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new CompressConflictDialog(
                    info.OutputPath,
                    info.SuggestedName,
                    info.CanAdd);
                await dlg.ShowDialog(this);

                var dlgAction = dlg.ResultAction;
                Core.Abstractions.CompressConflictAction resultAction;
                string? customName = null;

                switch (dlgAction)
                {
                    case Dialogs.CompressConflictAction.Overwrite:
                        resultAction = Core.Abstractions.CompressConflictAction.Overwrite;
                        break;
                    case Dialogs.CompressConflictAction.Add:
                        resultAction = Core.Abstractions.CompressConflictAction.Add;
                        break;
                    case Dialogs.CompressConflictAction.Rename:
                        resultAction = Core.Abstractions.CompressConflictAction.Rename;
                        customName = dlg.CustomName;
                        break;
                    case Dialogs.CompressConflictAction.Skip:
                    case Dialogs.CompressConflictAction.Cancel:
                    default:
                        resultAction = Core.Abstractions.CompressConflictAction.Cancel;
                        break;
                }

                return (resultAction, customName, dlg.ApplyToAll);
            });
        };

        vm.ShowExtractFileConflictDialogAsync = async info =>
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new ConflictDialog(info);
                await dlg.ShowDialog(this);

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
        vm.ShowColumnPickerAction = () => ShowColumnPickerMenu();

        vm.GetOpenFilePaths = async () =>
        {
            var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select files to add",
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

                // Save multi-selection state at press time (before drag starts)
                if (fileGrid.SelectedItems.Count > 1)
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
                if (Math.Abs(delta.X) < 32 && Math.Abs(delta.Y) < 32)
                    return;

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

                var format = ArchiveFormatHelper.GetFormat(archivePath);
                var password = vm2.GetSessionPassword(archivePath);

                // Expand items: directories become their contained files (flat list)
                var expandedItems = DragDropItemExpander.ExpandItems(selectedItems, allItems);
                if (expandedItems.Count == 0)
                    return;

                _isOwnDrag = true;
                vm2.StatusMessage = "拖拽到 Explorer 或桌面以直接解压";

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

                // A 方案：拖拽期间把系统"禁止"光标（OCR_NO）替换为正常箭头/自定义 .cur。
                // OLE 每次鼠标移动都用系统资源设置光标，替换资源表本身即可稳定生效。
                ReplaceSystemNoCursor();

                nint escHook = nint.Zero;
                try
                {
                    var data = new DataTransfer();
                    // 注册自定义平台格式，使 IDataObject 非空（空 DataObject 会让 Explorer 显示禁止光标）
                    data.Add(DataTransferItem.Create(
                        DataFormat.CreateStringPlatformFormat(MantisZipDragFormatName), archivePath));
                    App.DebugLog("[MainWindow] Avalonia DoDragDropAsync START");

                    // 安装低级键盘钩子检测 Esc 取消。OLE 拖拽模态循环会派发钩子消息，
                    // 回调在键盘消息被处理前同步触发，因此 DoDragDropAsync 返回时标志已确定。
                    _dragEscPressed = false;
                    _dragEscHookProc = (nCode, wParam, lParam) =>
                    {
                        if (nCode >= 0 && (int)wParam == NativeMethods.WM_KEYDOWN &&
                            Marshal.ReadInt32(lParam) == NativeMethods.VK_ESCAPE)
                        {
                            _dragEscPressed = true;
                        }
                        return NativeMethods.CallNextHookEx(escHook, nCode, wParam, lParam);
                    };
                    escHook = NativeMethods.SetWindowsHookEx(
                        NativeMethods.WH_KEYBOARD_LL, _dragEscHookProc,
                        NativeMethods.GetModuleHandle(null), 0);
                    if (escHook == nint.Zero)
                        App.DebugLog("[MainWindow] Esc hook install FAILED");

                    var result = await DragDrop.DoDragDropAsync(
                        triggerEvent, data, DragDropEffects.Copy);
                    App.DebugLog($"[MainWindow] Avalonia DoDragDropAsync DONE: result={result}");

                    // Close overlay IMMEDIATELY after drag completes, before dialog processing
                    controller.Stop();
                    overlayWin.Close();
                    App.DebugLog("[MainWindow] Overlay closed");

                    if (_dragEscPressed)
                    {
                        // 用户按 Esc 取消拖拽 → 不执行解压
                        App.DebugLog("[MainWindow] Esc pressed during drag — extraction cancelled");
                        if (vm2 != null)
                            vm2.StatusMessage = "拖拽已取消";
                    }
                    else
                    {
                        NativeMethods.GetCursorPos(out var dropPt);
                        App.DebugLog($"[MainWindow] Drop point captured: ({dropPt.X}, {dropPt.Y})");

                        if (vm2 != null && !string.IsNullOrEmpty(archivePath))
                        {
                            vm2.StatusMessage = "正在检测目标位置...";
                            var dragService = new DragDropService(
                                archivePath, format, password, this);
                            await dragService.ExecuteAfterDropAsync(
                                selectedItems, allItems, vm2);
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.DebugLog($"[MainWindow] DragDropAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    // 还原系统"禁止"光标（与 ReplaceSystemNoCursor 配对，异常路径也执行）
                    RestoreSystemNoCursor();
                    // 卸载键盘钩子（正常/异常路径统一处理）
                    if (escHook != nint.Zero)
                    {
                        NativeMethods.UnhookWindowsHookEx(escHook);
                        _dragEscHookProc = null;
                    }
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

        // Persist window position/size/state on close
        Closing += (_, _) => WindowStateManager.Save(this);
    }

    /// <summary>
    /// A 方案（拖拽光标临时修复）：拖拽期间用正常箭头（或自定义 .cur）替换系统"禁止"光标（OCR_NO）。
    /// 背景：Explorer 不识别我们的自定义格式 → DragEnter 返回 DROPEFFECT_NONE → 显示禁止光标。
    /// OLE 每次鼠标移动都用系统光标资源设置默认光标（Avalonia GiveFeedback 返回 USEDEFAULTCURSORS），
    /// 定时 SetCursor 会被覆盖，唯有替换资源表本身（SetSystemCursor）能稳定生效。
    /// 副作用：拖拽期间全系统"禁止"光标临时变为箭头/自定义图标；进程崩溃时可能残留（需重启还原）。
    /// </summary>
    private void ReplaceSystemNoCursor()
    {
        try
        {
            // 防御：上次异常未还原时先还原
            if (_originalNoCursor != nint.Zero)
                RestoreSystemNoCursor();

            var original = NativeMethods.LoadCursor(nint.Zero, new nint(NativeMethods.OCR_NO));
            if (original == nint.Zero)
                return;
            // CopyIcon 保存副本供还原（LoadCursor 返回的是共享句柄，不能直接持有销毁）
            var saved = NativeMethods.CopyIcon(original);
            if (saved == nint.Zero)
                return;

            // 优先加载自定义 .cur（程序同目录 DragCursor.cur），否则用系统标准箭头兜底
            var custom = nint.Zero;
            var curPath = Path.Combine(AppContext.BaseDirectory, "DragCursor.cur");
            if (File.Exists(curPath))
                custom = NativeMethods.LoadCursorFromFile(curPath);
            if (custom == nint.Zero)
                custom = NativeMethods.LoadCursor(nint.Zero, new nint(NativeMethods.OCR_NORMAL));
            if (custom == nint.Zero)
            {
                NativeMethods.DestroyIcon(saved);
                return;
            }

            if (NativeMethods.SetSystemCursor(custom, NativeMethods.OCR_NO))
            {
                _originalNoCursor = saved;
            }
            else
            {
                // 替换失败：释放副本，保持未替换状态
                NativeMethods.DestroyIcon(saved);
                App.DebugLog("[MainWindow] SetSystemCursor(OCR_NO) FAILED");
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"[MainWindow] ReplaceSystemNoCursor FAILED: {ex.GetType().Name}: {ex.Message}");
            _originalNoCursor = nint.Zero;
        }
    }

    /// <summary>
    /// 还原系统"禁止"光标（与 <see cref="ReplaceSystemNoCursor"/> 配对）。必须在拖拽结束后调用。
    /// </summary>
    private void RestoreSystemNoCursor()
    {
        if (_originalNoCursor == nint.Zero)
            return;
        try
        {
            // 还原后系统持有该句柄，不销毁
            NativeMethods.SetSystemCursor(_originalNoCursor, NativeMethods.OCR_NO);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[MainWindow] RestoreSystemNoCursor FAILED: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _originalNoCursor = nint.Zero;
        }
    }

    private void FileListGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is ArchiveItemModel item && item.IsDirectory)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.NavigateToFolderPath(item.FullPath);
        }
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
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择压缩包",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("压缩包")
                {
                    Patterns = ["*.zip", "*.7z", "*.rar", "*.tar", "*.tgz", "*.gz", "*.tar.gz", "*.iso"]
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = ["*.*"]
                }
            ]
        });

        return result.Count >= 1 ? result[0].Path.LocalPath : null;
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

    private void ShowColumnPickerMenu()
    {
        var menu = new ContextMenu();
        foreach (var column in FileListGrid.Columns)
        {
            var header = GetColumnHeaderText(column);
            // Name column cannot be hidden
            if (header == "Name" || string.IsNullOrEmpty(header))
                continue;

            var menuItem = new MenuItem
            {
                Header = header,
                Icon = new CheckBox
                {
                    IsChecked = column.IsVisible,
                    IsHitTestVisible = false,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Tag = column
            };
            menuItem.Click += (s, args) =>
            {
                column.IsVisible = !column.IsVisible;
                if (menuItem.Icon is CheckBox cb)
                    cb.IsChecked = column.IsVisible;
            };
            menu.Items.Add(menuItem);
        }
        menu.Open(ColumnPickerButton);
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
            "QuickPathDialog" => new QuickPathDialog(true),
            "QuickPathPreDialog" => new QuickPathPreDialog(true, false),
            "ArchiveCommentDialog" => new ArchiveCommentDialog(@"C:\test.zip", ArchiveFormat.Zip, "测试注释"),
            "ArchiveSaveAsDialog" => new ArchiveSaveAsDialog(@"C:\test.zip"),
            "UnifiedExtractDialog" => new UnifiedExtractDialog(this),
            _ => null
        };

        if (window != null)
        {
            await window.ShowDialog(this);
        }
    }
}
