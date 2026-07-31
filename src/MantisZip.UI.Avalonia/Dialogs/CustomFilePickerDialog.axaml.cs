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
    private readonly string[]? _fileExtensions;

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
    public string BackText => LocalizationManager.T("Picker_Back");
    public string ForwardText => LocalizationManager.T("Picker_Forward");
    public string UpText => LocalizationManager.T("Picker_Up");
    public string FileNameLabel => LocalizationManager.T("Picker_FileName");
    public string FileTypeLabel => LocalizationManager.T("Picker_FileType");

    // ── Static entry points ────────────────────────────────────────────────

    /// <summary>选择文件夹。返回所选目录路径，取消返回 null。</summary>
    public static Task<string?> ShowFolderAsync(Window owner, string? initialPath = null)
        => ShowInternal(owner, PickerMode.PickFolder, null, null, initialPath, null);

    /// <summary>保存文件。返回完整保存路径，取消返回 null。</summary>
    public static Task<string?> ShowSaveFileAsync(Window owner, string? initialPath = null, string? defaultExtension = null)
        => ShowInternal(owner, PickerMode.SaveFile, null, defaultExtension, initialPath, null);

    /// <summary>打开文件（单文件）。返回文件路径，取消返回 null。</summary>
    /// <param name="fileExtensions">文件筛选器（扩展名列表，如 "*.zip" / ".zip" 或 "zip"）。null 或空 = 显示所有文件。</param>
    public static Task<string?> ShowOpenFileAsync(Window owner, string? initialPath = null, string[]? fileExtensions = null)
        => ShowInternal(owner, PickerMode.OpenFile, null, null, initialPath, fileExtensions);

    /// <summary>解压模式：选择目标目录，底部实时显示解压冲突预览。返回目录路径，取消返回 null。</summary>
    public static Task<string?> ShowExtractFolderAsync(Window owner, IReadOnlyList<ArchiveItem> entries, string? initialPath = null)
        => ShowInternal(owner, PickerMode.ExtractFolder, entries, null, initialPath, null);

    private static async Task<string?> ShowInternal(
        Window owner, PickerMode mode, IReadOnlyList<ArchiveItem>? entries, string? defaultExtension, string? initialPath, string[]? fileExtensions)
    {
        var dialog = new CustomFilePickerDialog(mode, entries, defaultExtension, initialPath, fileExtensions)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        await dialog.ShowDialog(owner);
        return dialog.SelectedPath;
    }

    // ── Constructors ───────────────────────────────────────────────────────

    /// <summary>设计时无参构造函数。</summary>
    public CustomFilePickerDialog()
        : this(PickerMode.PickFolder, null, null, null, null)
    {
    }

    public CustomFilePickerDialog(PickerMode mode, IReadOnlyList<ArchiveItem>? entries = null, string? defaultExtension = null, string? initialPath = null, string[]? fileExtensions = null)
    {
        InitializeComponent();
        _mode = mode;
        _entries = entries;
        _defaultExtension = defaultExtension;
        _fileExtensions = fileExtensions;

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

        // 文件名/文件类型行：仅 SaveFile / OpenFile 模式显示
        if (mode is PickerMode.SaveFile or PickerMode.OpenFile)
        {
            InitFileNameArea(mode);
        }
        else
        {
            FileNameArea.IsVisible = false;
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
        InitDriveSelector();
        var startDir = ResolveInitialPath(initialPath);
        NavigateTo(startDir);

        // 地址栏补全/历史
        InitPathAutoComplete();
    }

    // ── File name + File type row ──────────────────────────────────────────

    private bool _isSyncingFileType;

    /// <summary>
    /// 初始化底部文件名 + 文件类型行。
    /// - SaveFile：文件类型 = 压缩格式（zip/7z/tar.gz），切换时更新文件名扩展名（格式联动）
    /// - OpenFile：文件类型 = 筛选器（传入扩展名组 + 所有文件），切换时重新过滤列表
    /// </summary>
    private void InitFileNameArea(PickerMode mode)
    {
        if (mode == PickerMode.SaveFile)
        {
            FileTypeSelector.ItemsSource = new List<string>
            {
                "*.zip",
                "*.7z",
                "*.tar.gz"
            };
            FileTypeSelector.SelectedIndex = _defaultExtension switch
            {
                ".7z" => 1,
                ".tar.gz" => 2,
                _ => 0
            };

            // 预填文件名（来自 initialPath 的末尾，或默认扩展名）
            FileNameBox.Text = _defaultExtension == null
                ? string.Empty
                : "untitled" + _defaultExtension;
        }
        else // OpenFile
        {
            FileTypeSelector.ItemsSource = new List<string>
            {
                LocalizationManager.T("Picker_FileTypeArchive"),
                LocalizationManager.T("Picker_FileTypeAll")
            };
            FileTypeSelector.SelectedIndex = 0;
        }
    }

    private void FileNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        // 用户在文件名框输入时清除选中（避免选中文件覆盖输入）
        if (FileList.SelectedItem is FileBrowserItem { IsDirectory: false })
        {
            FileList.SelectedItem = null;
        }
    }

    private void FileTypeSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingFileType) return;
        if (FileTypeSelector.SelectedIndex < 0) return;

        if (_mode == PickerMode.OpenFile)
        {
            // 重新加载当前目录应用新筛选
            LoadDirectory(_currentDir);
        }
        else if (_mode == PickerMode.SaveFile)
        {
            UpdateSaveFileExtension();
        }
    }

    /// <summary>SaveFile 模式：文件类型切换时更新文件名扩展名（格式联动）。</summary>
    private void UpdateSaveFileExtension()
    {
        var extension = GetSelectedSaveExtension();
        if (string.IsNullOrEmpty(extension)) return;

        var name = FileNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) return;

        // 去掉旧扩展名，换上当前类型扩展名
        var baseName = Path.GetFileNameWithoutExtension(name);
        FileNameBox.Text = baseName + extension;
    }

    private string GetSelectedSaveExtension()
    {
        var ext = FileTypeSelector.SelectedItem as string;
        return ext switch
        {
            "*.zip" => ".zip",
            "*.7z" => ".7z",
            "*.tar.gz" => ".tar.gz",
            _ => _defaultExtension ?? ".zip"
        };
    }

    // ── Drive selector ─────────────────────────────────────────────────────

    private bool _isSyncingDrive;

    /// <summary>
    /// 填充盘符下拉列表（C:\ / D:\ 等），并选中当前目录所在盘。
    /// </summary>
    private void InitDriveSelector()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => d.Name.TrimEnd('\\', '/') + "\\") // "C:\"
                .ToList();

            DriveSelector.ItemsSource = drives;
            if (drives.Count > 0)
                DriveSelector.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomFilePickerDialog] InitDriveSelector failed: {ex.Message}");
        }
    }

    private void DriveSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingDrive) return;
        if (DriveSelector.SelectedItem is not string drive) return;
        if (string.IsNullOrEmpty(drive)) return;

        // 确保盘符带根路径分隔符
        var root = drive.EndsWith('\\') ? drive : drive + "\\";
        if (Directory.Exists(root))
        {
            NavigateTo(root);
        }
    }

    /// <summary>同步盘符下拉选中到当前目录所在盘（NavigateTo 内部调用）。</summary>
    private void SyncDriveSelector(string dir)
    {
        try
        {
            var root = Path.GetPathRoot(dir);
            if (string.IsNullOrEmpty(root)) return;

            _isSyncingDrive = true;
            try
            {
                foreach (var item in DriveSelector.ItemsSource ?? Array.Empty<string>())
                {
                    if (item is string drive &&
                        string.Equals(drive.TrimEnd('\\', '/') + "\\", root, StringComparison.OrdinalIgnoreCase))
                    {
                        DriveSelector.SelectedItem = drive;
                        break;
                    }
                }
            }
            finally
            {
                _isSyncingDrive = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomFilePickerDialog] SyncDriveSelector failed: {ex.Message}");
        }
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

        SyncDriveSelector(dir);
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
        SyncDriveSelector(dir);
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
        SyncDriveSelector(dir);
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
                    .Where(MatchesFileFilter)
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

    /// <summary>
    /// 文件筛选器匹配（OpenFile 模式）。支持 "*.zip" / ".zip" / "zip" / "*.*" 格式。
    /// 无筛选器（null/空）或含 "*.*" 时显示所有文件。
    /// 文件类型下拉选中「所有文件」（index 1）时同样不过滤。
    /// </summary>
    private bool MatchesFileFilter(string filePath)
    {
        if (_mode != PickerMode.OpenFile || _fileExtensions == null || _fileExtensions.Length == 0)
            return true;
        if (_fileExtensions.Any(e => e == "*.*" || e == "*"))
            return true;
        // 文件类型下拉选中「所有文件」→ 不过滤
        if (FileTypeSelector.SelectedIndex == 1)
            return true;

        var ext = Path.GetExtension(filePath);
        foreach (var pattern in _fileExtensions)
        {
            var p = pattern.Trim().ToLowerInvariant();
            if (p.StartsWith("*.")) p = p[1..]; // "*.zip" → ".zip"
            if (p.StartsWith(".") || p.Length <= 1)
            {
                if (string.Equals(ext, p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else
            {
                if (string.Equals(ext, "." + p, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
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
        else if (_mode == PickerMode.SaveFile)
        {
            // 保存模式：双击文件 → 填入文件名框（可再改格式）
            FileNameBox.Text = item.DisplayName;
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
                else if (_mode == PickerMode.SaveFile)
                    FileNameBox.Text = item.DisplayName; // 保存模式 Enter 文件 → 填入文件名
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
                // 文件名来自底部文件名文本框
                var name = FileNameBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    _ = AppMessageBox.Show(
                        LocalizationManager.T("Picker_WarningEnterName"),
                        LocalizationManager.T("Picker_SaveFileTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning, this);
                    return;
                }
                // 格式联动：应用当前文件类型扩展名
                if (!Path.HasExtension(name))
                {
                    name = name + GetSelectedSaveExtension();
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
