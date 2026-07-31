using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>文件选择器模式。</summary>
public enum PickerMode
{
    /// <summary>选择文件夹，确定返回目录路径。</summary>
    PickFolder,
    /// <summary>保存文件，支持文件名输入，确定返回完整保存路径。</summary>
    SaveFile,
    /// <summary>打开文件，文件筛选器，确定返回文件路径（单文件）。</summary>
    OpenFile,
    /// <summary>解压模式：PickFolder + 底部解压预览区（ResultTreeView 实时冲突检测）。</summary>
    ExtractFolder
}

/// <summary>文件浏览列表项。</summary>
public class FileBrowserItem
{
    public string FullPath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public Bitmap? Icon { get; set; }
    public string SizeText { get; set; } = string.Empty;
    public string ModifiedText { get; set; } = string.Empty;
    public string SubText { get; set; } = string.Empty;
    public bool ShowSubText { get; set; }
}

/// <summary>
/// 自建文件/目录选择器：地址栏 + QuickPathControl 速选 + 文件浏览 + 解压预览区（仅解压模式）。
/// 静态入口：<see cref="ShowFolderAsync"/> / <see cref="ShowSaveFileAsync"/> /
/// <see cref="ShowOpenFileAsync"/> / <see cref="ShowExtractFolderAsync"/>。
/// 确定返回 <see cref="SelectedPath"/>，取消返回 null。
/// </summary>
public partial class CustomFilePickerDialog : Window
{
    private readonly PickerMode _mode;
    private readonly IReadOnlyList<ArchiveItem>? _entries;
    private readonly string? _defaultExtension;

    /// <summary>确定后返回的路径。</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>保存模式下用户输入的文件名。</summary>
    public string SelectedFileName { get; private set; } = string.Empty;

    // ── Navigation state ──

    private readonly List<string> _backStack = new();
    private readonly List<string> _forwardStack = new();
    private string _currentDir = string.Empty;

    // ── Debounce for extract preview ──

    private CancellationTokenSource? _previewDebounceCts;

    /// <summary>根节点元素（XAML 中通过名称引用窗口自身）。</summary>
    public string OkText => LocalizationManager.T("Common_OK");
    public string CancelText => LocalizationManager.T("Common_Cancel");

    // ── Static entry points ────────────────────────────────────────────────

    /// <summary>选择文件夹。返回所选目录路径，取消返回 null。</summary>
    public static Task<string?> ShowFolderAsync(Window owner, string? initialPath = null)
        => ShowInternal(owner, PickerMode.PickFolder, null, null, initialPath);

    /// <summary>保存文件。返回完整保存路径，取消返回 null。</summary>
    public static Task<string?> ShowSaveFileAsync(Window owner, string? initialPath = null, string? defaultExtension = null)
        => ShowInternal(owner, PickerMode.SaveFile, null, defaultExtension, initialPath);

    /// <summary>打开文件（单文件）。返回文件路径，取消返回 null。</summary>
    public static Task<string?> ShowOpenFileAsync(Window owner, string? initialPath = null)
        => ShowInternal(owner, PickerMode.OpenFile, null, null, initialPath);

    /// <summary>解压模式：选择目标目录，底部实时显示解压冲突预览。返回目录路径，取消返回 null。</summary>
    public static Task<string?> ShowExtractFolderAsync(Window owner, IReadOnlyList<ArchiveItem> entries, string? initialPath = null)
        => ShowInternal(owner, PickerMode.ExtractFolder, entries, null, initialPath);

    private static async Task<string?> ShowInternal(
        Window owner, PickerMode mode, IReadOnlyList<ArchiveItem>? entries, string? defaultExtension, string? initialPath)
    {
        var dialog = new CustomFilePickerDialog(mode, entries, defaultExtension, initialPath)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        await dialog.ShowDialog(owner);
        return dialog.SelectedPath;
    }

    // ── Constructors ───────────────────────────────────────────────────────

    /// <summary>设计时无参构造函数。</summary>
    public CustomFilePickerDialog()
        : this(PickerMode.PickFolder, null, null, null)
    {
    }

    public CustomFilePickerDialog(PickerMode mode, IReadOnlyList<ArchiveItem>? entries = null, string? defaultExtension = null, string? initialPath = null)
    {
        InitializeComponent();
        _mode = mode;
        _entries = entries;
        _defaultExtension = defaultExtension;

        DataContext = this;

        // 标题
        Title = mode switch
        {
            PickerMode.SaveFile => LocalizationManager.T("Picker_SaveFileTitle"),
            PickerMode.OpenFile => LocalizationManager.T("Picker_OpenFileTitle"),
            _ => LocalizationManager.T("Picker_PickFolderTitle")
        };

        // 解压预览区仅解压模式显示
        if (mode != PickerMode.ExtractFolder)
        {
            PreviewArea.IsVisible = false;
        }

        // 窗口高度：解压模式含预览区更高
        if (mode == PickerMode.ExtractFolder)
        {
            Height = 620;
        }
        else
        {
            Height = 420;
        }

        // 文件列表是否显示文件（PickFolder/ExtractFolder 只显示目录）
        // 通过过滤实现：见 LoadDirectory

        // 初始目录
        var startDir = ResolveInitialPath(initialPath);
        NavigateTo(startDir);

        // 地址栏补全/历史
        InitPathAutoComplete();
    }

    // ── Path resolution ────────────────────────────────────────────────────

    private static string ResolveInitialPath(string? initialPath)
    {
        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            var p = Environment.ExpandEnvironmentVariables(initialPath.Trim());
            if (Directory.Exists(p)) return p;
            var dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            if (File.Exists(p)) return Path.GetDirectoryName(p) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    // ── Address bar ────────────────────────────────────────────────────────

    private void InitPathAutoComplete()
    {
        PathAutoComplete.PlaceholderText = LocalizationManager.T("Picker_AddressPlaceholder");

        // 历史建议（来自 PathHistoryManager，Core 持久化）
        PathAutoComplete.ItemsSource = PathHistoryManager.GetRecent(50).Select(h => h.Path).ToList();

        // 输入时实时补全：父目录枚举 + 历史
        PathAutoComplete.TextChanged += (_, _) =>
        {
            var text = PathAutoComplete.Text ?? string.Empty;
            if (text.Length < 2) return;

            var suggestions = new List<string>();
            // 历史中匹配的
            suggestions.AddRange(PathHistoryManager.GetRecent(50)
                .Select(h => h.Path)
                .Where(p => p.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Take(10));
            // 文件系统枚举：父目录下以输入为前缀的目录
            try
            {
                var parent = Path.GetDirectoryName(text.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    var prefix = Path.GetFileName(text.TrimEnd('\\', '/'));
                    suggestions.AddRange(Directory.EnumerateDirectories(parent, (prefix + "*"), SearchOption.TopDirectoryOnly)
                        .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                        .Take(20));
                }
            }
            catch
            {
                // 非法路径等，忽略
            }

            PathAutoComplete.ItemsSource = suggestions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        };
    }

    private void PathAutoComplete_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = PathAutoComplete.Text?.Trim() ?? string.Empty;
            if (Directory.Exists(text))
            {
                NavigateTo(text);
                e.Handled = true;
            }
            else if (File.Exists(text))
            {
                TryConfirmFile(text);
                e.Handled = true;
            }
        }
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    private void NavigateTo(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        if (!string.IsNullOrEmpty(_currentDir) &&
            !string.Equals(_currentDir, dir, StringComparison.OrdinalIgnoreCase))
        {
            _backStack.Add(_currentDir);
            _forwardStack.Clear();
        }
        _currentDir = dir;

        CurrentPathText.Text = dir;
        PathAutoComplete.Text = dir;
        PathHistoryManager.Record(dir);

        LoadDirectory(dir);
        QuickPath.SetCurrentPath(dir);
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (_backStack.Count == 0) return;
        var dir = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);
        if (!string.IsNullOrEmpty(_currentDir))
            _forwardStack.Add(_currentDir);
        _currentDir = dir;
        CurrentPathText.Text = dir;
        PathAutoComplete.Text = dir;
        LoadDirectory(dir);
        QuickPath.SetCurrentPath(dir);
    }

    private void Forward_Click(object? sender, RoutedEventArgs e)
    {
        if (_forwardStack.Count == 0) return;
        var dir = _forwardStack[^1];
        _forwardStack.RemoveAt(_forwardStack.Count - 1);
        if (!string.IsNullOrEmpty(_currentDir))
            _backStack.Add(_currentDir);
        _currentDir = dir;
        CurrentPathText.Text = dir;
        PathAutoComplete.Text = dir;
        LoadDirectory(dir);
        QuickPath.SetCurrentPath(dir);
    }

    private void Up_Click(object? sender, RoutedEventArgs e)
    {
        var parent = Path.GetDirectoryName(_currentDir.TrimEnd('\\', '/'));
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            NavigateTo(parent);
    }

    // ── Directory listing ──────────────────────────────────────────────────

    private readonly ObservableCollection<FileBrowserItem> _fileItems = new();

    private void LoadDirectory(string dir)
    {
        _fileItems.Clear();
        FileList.ItemsSource = _fileItems;
        FileList.SelectedItem = null;

        try
        {
            // 目录列表
            var showFiles = _mode is PickerMode.SaveFile or PickerMode.OpenFile;
            var dirs = Directory.EnumerateDirectories(dir)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var files = showFiles
                ? Directory.EnumerateFiles(dir)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            foreach (var d in dirs)
            {
                _fileItems.Add(CreateDirItem(d));
            }
            foreach (var f in files)
            {
                _fileItems.Add(CreateFileItem(f));
            }

            // 解压模式下路径变化 → 防抖重建预览
            if (_mode == PickerMode.ExtractFolder && _entries != null)
            {
                SchedulePreviewRebuild(dir);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomFilePickerDialog] LoadDirectory failed: {ex.Message}");
        }
    }

    private FileBrowserItem CreateDirItem(string path)
    {
        var info = new DirectoryInfo(path);
        return new FileBrowserItem
        {
            FullPath = path,
            DisplayName = info.Name,
            IsDirectory = true,
            Icon = IconService.GetFolderIcon(),
            SizeText = string.Empty,
            ModifiedText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            ShowSubText = false
        };
    }

    private FileBrowserItem CreateFileItem(string path)
    {
        var info = new FileInfo(path);
        var ext = info.Extension;
        return new FileBrowserItem
        {
            FullPath = path,
            DisplayName = info.Name,
            IsDirectory = false,
            Icon = IconService.GetFileIcon(string.IsNullOrEmpty(ext) ? ".file" : ext),
            SizeText = FormatUtil.FormatSize(info.Length),
            ModifiedText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            ShowSubText = false
        };
    }

    // ── Extract preview (debounced) ────────────────────────────────────────

    private void SchedulePreviewRebuild(string destDir)
    {
        _previewDebounceCts?.Cancel();
        _previewDebounceCts = new CancellationTokenSource();
        var token = _previewDebounceCts.Token;

        // 防抖 ~300ms
        Task.Delay(300, token).ContinueWith(t =>
        {
            if (t.IsCanceled || token.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    var root = ResultPreviewService.BuildExtractPreview(
                        _entries ?? Array.Empty<ArchiveItem>(),
                        destDir,
                        checkExists: true);
                    PreviewTree.Root = root;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CustomFilePickerDialog] Preview rebuild failed: {ex.Message}");
                }
            });
        }, token);
    }

    // ── List interaction ───────────────────────────────────────────────────

    private void FileList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is not FileBrowserItem item) return;
        if (item.IsDirectory)
        {
            // 选择目录：预览/高亮，但不自动进入（等待双击或 Enter）
            QuickPath.SetCurrentPath(item.FullPath);
        }
    }

    private void FileList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileList.SelectedItem is not FileBrowserItem item) return;
        if (item.IsDirectory)
        {
            NavigateTo(item.FullPath);
        }
        else
        {
            TryConfirmFile(item.FullPath);
        }
    }

    private void FileList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (FileList.SelectedItem is FileBrowserItem item)
            {
                if (item.IsDirectory)
                    NavigateTo(item.FullPath);
                else
                    TryConfirmFile(item.FullPath);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Back)
        {
            Up_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            Back_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            Forward_Click(sender, e);
            e.Handled = true;
        }
    }

    private void QuickPath_PathSelected(object? sender, string path)
    {
        if (Directory.Exists(path))
        {
            NavigateTo(path);
        }
        else if (File.Exists(path) && _mode is PickerMode.SaveFile or PickerMode.OpenFile)
        {
            TryConfirmFile(path);
        }
    }

    // ── Confirm / Cancel ───────────────────────────────────────────────────

    private void TryConfirmFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        SelectedPath = filePath;
        SelectedFileName = Path.GetFileName(filePath);
        Close(true);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var selected = FileList.SelectedItem as FileBrowserItem;

        switch (_mode)
        {
            case PickerMode.PickFolder:
            case PickerMode.ExtractFolder:
                // 优先使用浏览区选中项，否则当前目录
                if (selected is { IsDirectory: true })
                {
                    SelectedPath = selected.FullPath;
                }
                else
                {
                    SelectedPath = _currentDir;
                }
                Close(true);
                break;

            case PickerMode.OpenFile:
                if (selected is { IsDirectory: false })
                {
                    SelectedPath = selected.FullPath;
                    SelectedFileName = selected.DisplayName;
                    Close(true);
                }
                else
                {
                    _ = AppMessageBox.Show(
                        LocalizationManager.T("Picker_WarningSelectFile"),
                        LocalizationManager.T("Picker_PickFolderTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning, this);
                }
                break;

            case PickerMode.SaveFile:
                // 文件名输入：地址栏文本或浏览区选中文件
                var name = GetSaveFileName();
                if (string.IsNullOrWhiteSpace(name))
                {
                    _ = AppMessageBox.Show(
                        LocalizationManager.T("Picker_WarningEnterName"),
                        LocalizationManager.T("Picker_SaveFileTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning, this);
                    return;
                }
                // 格式联动：应用默认扩展名
                if (!string.IsNullOrEmpty(_defaultExtension) && !Path.HasExtension(name))
                {
                    name = name + _defaultExtension;
                }
                SelectedPath = Path.Combine(_currentDir, name);
                SelectedFileName = name;
                Close(true);
                break;
        }
    }

    private string GetSaveFileName()
    {
        // 优先浏览区选中的文件，其次地址栏输入的最后一段
        if (FileList.SelectedItem is FileBrowserItem { IsDirectory: false } item)
            return item.DisplayName;
        var text = PathAutoComplete.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var name = Path.GetFileName(text.TrimEnd('\\', '/'));
        // 如果输入的是已有目录路径，则不是文件名
        if (Directory.Exists(text)) return string.Empty;
        // 如果 name 是当前目录名（即输入了目录本身），返回空
        if (string.Equals(name, Path.GetFileName(_currentDir.TrimEnd('\\', '/')), StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return name;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        SelectedPath = null;
        Close(false);
    }

    // ── System dialog fallback ─────────────────────────────────────────────

    private async void SystemBrowse_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storage = StorageProvider;
            switch (_mode)
            {
                case PickerMode.PickFolder:
                case PickerMode.ExtractFolder:
                {
                    var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = LocalizationManager.T("Picker_PickFolderTitle"),
                        AllowMultiple = false,
                        SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(_currentDir)
                    });
                    if (folders.Count >= 1 && folders[0].Path?.LocalPath is { } path)
                    {
                        SelectedPath = path;
                        Close(true);
                    }
                    break;
                }
                case PickerMode.SaveFile:
                {
                    var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = LocalizationManager.T("Picker_SaveFileTitle"),
                        SuggestedFileName = GetSaveFileName(),
                        SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(_currentDir)
                    });
                    if (file?.Path?.LocalPath is { } savePath)
                    {
                        SelectedPath = savePath;
                        SelectedFileName = Path.GetFileName(savePath);
                        Close(true);
                    }
                    break;
                }
                case PickerMode.OpenFile:
                {
                    var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = LocalizationManager.T("Picker_OpenFileTitle"),
                        AllowMultiple = false,
                        SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(_currentDir)
                    });
                    if (files.Count >= 1 && files[0].Path?.LocalPath is { } openPath)
                    {
                        SelectedPath = openPath;
                        SelectedFileName = Path.GetFileName(openPath);
                        Close(true);
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomFilePickerDialog] SystemBrowse failed: {ex.Message}");
        }
    }
}
