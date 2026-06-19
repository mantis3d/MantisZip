using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _isOwnDrag;
    private string? _dragDropTempDir;
    private PointerPressedEventArgs? _dragStartEvent;
    private Point _dragStartPoint;

    public MainWindow()
    {
        InitializeComponent();

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
            return result ? dialog.Password : null;
        };
        DataContext = vm;

        // ── Phase 3: Wire up ViewModel dialog callbacks ──

        vm.ShowExtractSettingsDialog = async (evm) =>
        {
            var dialog = new ExtractSettingsWindow(evm.ArchivePaths);
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
                cvm.DefaultFormat = dialog.ViewModel.DefaultFormat;
                cvm.CompressionLevel = dialog.ViewModel.CompressionLevel;
                cvm.OutputPath = dialog.ViewModel.OutputPath;
                cvm.Password = dialog.ViewModel.Password;
                cvm.Encrypt = dialog.ViewModel.Encrypt;
                cvm.Comment = dialog.ViewModel.Comment;
                cvm.CommentDistribution = dialog.ViewModel.CommentDistribution;
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

        vm.GetOpenFilePaths = async () =>
        {
            var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select files to add",
                AllowMultiple = true
            });
            return result.Count > 0 ? result.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList() : null;
        };

        // Setup drag-drop from file list
        var fileGrid = this.FindControl<DataGrid>("FileListGrid");
        if (fileGrid != null)
        {
            fileGrid.PointerPressed += (s, e) =>
            {
                _dragStartPoint = e.GetPosition(fileGrid);
                _dragStartEvent = e;
            };

            fileGrid.PointerMoved += async (s, e) =>
            {
                if (_dragStartEvent == null) return;

                var pos = e.GetPosition(fileGrid);
                var delta = pos - _dragStartPoint;
                if (Math.Abs(delta.X) < 10 && Math.Abs(delta.Y) < 10)
                    return;

                var triggerEvent = _dragStartEvent;
                _dragStartEvent = null; // Prevent re-entry

                var vm2 = DataContext as MainWindowViewModel;
                if (vm2?.SelectedEntry == null) return;
                var archivePath = vm2.CurrentArchivePath;
                if (string.IsNullOrEmpty(archivePath)) return;

                var entry = vm2.SelectedEntry;
                var format = ArchiveFormatHelper.GetFormat(archivePath);

                // Create temp directory for extracted file
                _dragDropTempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "DragDrop", Guid.NewGuid().ToString());
                Directory.CreateDirectory(_dragDropTempDir);

                try
                {
                    vm2.StatusMessage = "正在提取文件...";

                    var targetPath = Path.Combine(_dragDropTempDir, entry.Name);
                    var dir = Path.GetDirectoryName(targetPath);
                    if (dir != null && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    await ArchiveEntryExtractor.ExtractEntryAsync(
                        archivePath,
                        entry.FullPath,
                        targetPath,
                        format);

                    // Create data transfer with the extracted file
                    var storageFile = await StorageProvider.TryGetFileFromPathAsync(new Uri(targetPath));
                    if (storageFile != null)
                    {
                        var dataTransfer = new DataTransfer();
                        dataTransfer.Add(DataTransferItem.CreateFile(storageFile));

                        _isOwnDrag = true;
                        vm2.StatusMessage = "正在拖拽 — 放到目标位置以复制文件";

                        await DragDrop.DoDragDropAsync(triggerEvent, dataTransfer, DragDropEffects.Copy);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Drag-drop failed: {ex.Message}");
                }
                finally
                {
                    _isOwnDrag = false;
                    CleanupDragDropTemp();
                    if (DataContext is MainWindowViewModel vm3)
                        vm3.StatusMessage = "";
                }
            };
        }

        // Prevent reacting to our own drag-drop
        this.AddHandler(DragDrop.DropEvent, (s, e) =>
        {
            if (_isOwnDrag)
                e.Handled = true;
        });
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

    private void CleanupDragDropTemp()
    {
        if (_dragDropTempDir != null && Directory.Exists(_dragDropTempDir))
        {
            try
            {
                Directory.Delete(_dragDropTempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
            _dragDropTempDir = null;
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
}
