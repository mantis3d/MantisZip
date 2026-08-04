using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
    ExtractFolder,
    /// <summary>多选模式：文件+目录混合选择，勾选累积，跨目录保留。</summary>
    PickItems
}

/// <summary>文件浏览列表项。</summary>
public class FileBrowserItem : ObservableObject
{
    public string FullPath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public Bitmap? Icon { get; set; }
    public string SizeText { get; set; } = string.Empty;
    public string ModifiedText { get; set; } = string.Empty;
    public string SubText { get; set; } = string.Empty;
    public bool ShowSubText { get; set; }

    /// <summary>是否可勾选（PickItems 模式文件+目录可勾选；其他模式 false 隐藏勾选框）。</summary>
    public bool CanCheck { get; set; }

    private bool _isSelected;

    /// <summary>勾选状态（PickItems 模式累积）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
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

    /// <summary>PickItems 模式累积选中的路径列表（按路径排序，FullPath 去重）。</summary>
    public IReadOnlyList<string> SelectedPaths { get; private set; } = Array.Empty<string>();

    /// <summary>PickItems 模式累积中的路径（内部可变集合）。</summary>
    private readonly List<string> _accumulatedPaths = new();

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
    public string ExtractPreviewTitle => LocalizationManager.T("Picker_ExtractPreviewTitle");

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

    /// <summary>打开文件/文件夹（多选，PickItems 模式）。返回选中路径列表，取消返回 null。</summary>
    public static async Task<IReadOnlyList<string>?> ShowOpenItemsAsync(Window owner, string? initialPath = null)
    {
        var dialog = new CustomFilePickerDialog(PickerMode.PickItems, null, null, initialPath, null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        // Close(true) 由 Ok 分支设置，Close(false) 由取消设置 → 结果即是否确认
        var confirmed = await dialog.ShowDialog<bool>(owner);
        return confirmed ? dialog.SelectedPaths : null;
    }

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
            PickerMode.PickItems => LocalizationManager.T("Picker_PickItemsTitle"),
            _ => LocalizationManager.T("Picker_PickFolderTitle")
        };

        // 右侧面板切换：PickItems 显示累积面板，ExtractFolder 显示解压预览面板
        PickItemsPanel.IsVisible = mode == PickerMode.PickItems;
        ExtractFolderPanel.IsVisible = mode == PickerMode.ExtractFolder;
        // 右栏分隔条：仅当右栏面板可见时显示（否则拖动无意义）
        RightSplitter.IsVisible = mode is PickerMode.PickItems or PickerMode.ExtractFolder;
        AccumulatedItemsControl.ItemsSource = _accumulatedItems;
        ClearAccumulatedButtonText.Text = LocalizationManager.T("Picker_ClearSelection");

        // PickItems 模式：批量按钮文案固定（计数显示在下方 PickActionCountText，避免按钮宽度跳变）
        if (mode == PickerMode.PickItems)
        {
            AddSelectedButtonText.Text = LocalizationManager.T("Picker_AddSelected");
            RemoveSelectedButtonText.Text = LocalizationManager.T("Picker_RemoveSelected");
            // 初始计数文本：未选中任何项时的灰字占位
            PickActionCountText.Text = LocalizationManager.T("Picker_AddRemoveEmpty");
            PickActionCountText.Foreground = GetThemeBrush("ThemeTextSecondaryBrush");
        }

        // 系统浏览按钮：PickItems / ExtractFolder 模式隐藏
        if (mode is PickerMode.PickItems or PickerMode.ExtractFolder)
        {
            SystemBrowseButton.IsVisible = false;
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

        // 窗口高度：解压/多选模式更高
        Height = mode switch
        {
            PickerMode.ExtractFolder => 620,
            PickerMode.PickItems => 500,
            _ => 420
        };

        // 文件列表是否显示文件（PickFolder/ExtractFolder 只显示目录）
        // 通过过滤实现：见 LoadDirectory

        // 初始目录
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

    private bool _isSyncingFileName;

    private void FileNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        // 程序内同步文件名框（选中文件）时不触发清除选中
        if (_isSyncingFileName) return;

        // 用户在文件名框输入时清除选中（避免选中文件覆盖输入）
        if (FileList.SelectedItem is FileBrowserItem { IsDirectory: false })
        {
            FileList.SelectedItem = null;
        }
    }

    /// <summary>同步文件名框内容（不触发清除选中逻辑）。</summary>
    private void SyncFileNameBox(string fileName)
    {
        _isSyncingFileName = true;
        try
        {
            FileNameBox.Text = fileName;
        }
        finally
        {
            _isSyncingFileName = false;
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

    // （盘符下拉已移除——目录树 Tab（QuickPathControl）取代盘符选择入口）

    // ── Path resolution ────────────────────────────────────────────────────

    /// <summary>
    /// 按用户设置的优先级链解析初始路径。
    /// 链顺序来自 <see cref="AppSettings.DefaultPathOrder"/>（值域 context/explorer/recent/custom），
    /// 依次探测第一个存在的路径；桌面始终作为最终兜底。
    /// </summary>
    private static string ResolveInitialPath(string? initialPath)
    {
        var settings = AppSettings.Load();
        var order = settings.DefaultPathOrder ?? new List<string>();

        // 依次尝试链上的每一项；未知值跳过
        foreach (var kind in order)
        {
            var candidate = kind switch
            {
                "context" => ResolveContextPath(initialPath),
                "explorer" => ExplorerWindowTracker.GetActiveExplorerPath(),
                "recent" => PathHistoryManager.GetRecent(1).FirstOrDefault()?.Path,
                "custom" => ResolveCustomPath(settings.CustomDefaultPath),
                _ => null
            };
            if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        // 兜底：桌面
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    /// <summary>场景路径：调用方传入的上下文路径（存在则用，否则 null 跳过）。</summary>
    private static string? ResolveContextPath(string? initialPath)
    {
        if (string.IsNullOrWhiteSpace(initialPath)) return null;
        var p = Environment.ExpandEnvironmentVariables(initialPath.Trim());
        if (Directory.Exists(p)) return p;
        var dir = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
        if (File.Exists(p)) return Path.GetDirectoryName(p);
        return null;
    }

    /// <summary>手动路径：用户填写的固定路径（留空或不存在则 null 跳过）。</summary>
    private static string? ResolveCustomPath(string? customPath)
    {
        if (string.IsNullOrWhiteSpace(customPath)) return null;
        var p = Environment.ExpandEnvironmentVariables(customPath.Trim());
        return Directory.Exists(p) ? p : null;
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
        UpdateFavoriteButtonState();
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
        UpdateFavoriteButtonState();
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
        UpdateFavoriteButtonState();
    }

    private void Up_Click(object? sender, RoutedEventArgs e)
    {
        var parent = Path.GetDirectoryName(_currentDir.TrimEnd('\\', '/'));
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            NavigateTo(parent);
    }

    // ── Directory listing ──────────────────────────────────────────────────

    private readonly ObservableCollection<FileBrowserItem> _fileItems = new();

    /// <summary>右侧累积面板显示项（PickItems 模式）。</summary>
    private sealed record AccumulatedPathItem(string Path);

    private readonly ObservableCollection<AccumulatedPathItem> _accumulatedItems = new();

    private void LoadDirectory(string dir)
    {
        _fileItems.Clear();
        FileList.ItemsSource = _fileItems;
        FileList.SelectedItem = null;

        try
        {
            // 目录列表
            var showFiles = _mode is PickerMode.SaveFile or PickerMode.OpenFile or PickerMode.PickItems;
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
        var item = new FileBrowserItem
        {
            FullPath = path,
            DisplayName = info.Name,
            IsDirectory = true,
            Icon = IconService.GetFolderIcon(),
            SizeText = string.Empty,
            ModifiedText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            ShowSubText = false,
            CanCheck = _mode == PickerMode.PickItems
        };
        SubscribeIsSelected(item);
        item.IsSelected = _accumulatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
        return item;
    }

    private FileBrowserItem CreateFileItem(string path)
    {
        var info = new FileInfo(path);
        var ext = info.Extension;
        var item = new FileBrowserItem
        {
            FullPath = path,
            DisplayName = info.Name,
            IsDirectory = false,
            Icon = IconService.GetFileIcon(string.IsNullOrEmpty(ext) ? ".file" : ext),
            SizeText = FormatUtil.FormatSize(info.Length),
            ModifiedText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            ShowSubText = false,
            CanCheck = _mode == PickerMode.PickItems
        };
        SubscribeIsSelected(item);
        item.IsSelected = _accumulatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
        return item;
    }

    /// <summary>
    /// 订阅勾选状态变更：勾选框点击等通过 TwoWay 绑定修改 IsSelected 时，
    /// 统一走 <see cref="ToggleAccumulated"/> 累积/移除。
    /// </summary>
    private void SubscribeIsSelected(FileBrowserItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileBrowserItem.IsSelected))
            {
                ToggleAccumulated(item.FullPath, item.IsSelected);
            }
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
        // PickItems：仅刷新批量按钮计数（Extended 多选）
        if (_mode == PickerMode.PickItems)
        {
            UpdatePickPanel();
            return;
        }

        if (FileList.SelectedItem is not FileBrowserItem item) return;
        if (item.IsDirectory)
        {
            // 选择目录：预览/高亮，但不自动进入（等待双击或 Enter）
            QuickPath.SetCurrentPath(item.FullPath);
        }
        else if (_mode is PickerMode.SaveFile or PickerMode.OpenFile)
        {
            // 选中文件 → 文件名框自动同步
            SyncFileNameBox(item.DisplayName);
        }
    }

    private void FileList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileList.SelectedItem is not FileBrowserItem item) return;
        if (item.IsDirectory)
        {
            NavigateTo(item.FullPath);
        }
        else if (_mode == PickerMode.PickItems)
        {
            // 双击文件：切换勾选累积（IsSelected setter 经订阅累积），不关闭对话框
            item.IsSelected = !item.IsSelected;
        }
        else if (_mode == PickerMode.SaveFile)
        {
            // 保存模式：双击文件 → 填入文件名框（可再改格式）
            SyncFileNameBox(item.DisplayName);
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
                else if (_mode == PickerMode.PickItems)
                {
                    // PickItems：Enter 切换勾选（同双击），不关闭
                    item.IsSelected = !item.IsSelected;
                }
                else if (_mode == PickerMode.SaveFile)
                    SyncFileNameBox(item.DisplayName); // 保存模式 Enter 文件 → 填入文件名
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

    // ── PickItems accumulation ─────────────────────────────────────────────

    /// <summary>统一累积入口：勾选框/双击/批量/清空/回填全部经过此方法。</summary>
    private void ToggleAccumulated(string path, bool isChecked)
    {
        if (isChecked)
        {
            if (!_accumulatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                _accumulatedPaths.Add(path);
        }
        else
        {
            _accumulatedPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        }
        UpdatePickPanel();
    }

    /// <summary>批量添加当前高亮项（勾选 + 累积）。IsSelected setter 经订阅自动累积。</summary>
    private void AddSelected_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in (FileList.SelectedItems ?? Array.Empty<object>()).OfType<FileBrowserItem>().Where(i => i.CanCheck))
        {
            item.IsSelected = true;
        }
    }

    /// <summary>批量移除当前高亮项（取消勾选 + 取消累积）。IsSelected setter 经订阅自动移除。</summary>
    private void RemoveSelected_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in (FileList.SelectedItems ?? Array.Empty<object>()).OfType<FileBrowserItem>())
        {
            item.IsSelected = false;
        }
    }

    /// <summary>累积面板逐项移除（× 按钮）。同步回当前可见列表中对应项。</summary>
    private void RemoveItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            ToggleAccumulated(path, false);
            foreach (var item in _fileItems.Where(i =>
                         string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                item.IsSelected = false;
            }
        }
    }

    /// <summary>清空全部累积。</summary>
    private void ClearAccumulated_Click(object? sender, RoutedEventArgs e)
    {
        _accumulatedPaths.Clear();
        foreach (var item in _fileItems)
        {
            item.IsSelected = false;
        }
        UpdatePickPanel();
    }

    /// <summary>刷新右侧累积面板：标题计数、累积列表、批量按钮计数、空占位。</summary>
    private void UpdatePickPanel()
    {
        var count = _accumulatedPaths.Count;

        // 标题计数
        PickTitleText.Text = LocalizationManager.T("Picker_SelectedCount", count);

        // 累积列表（按路径排序）
        _accumulatedItems.Clear();
        foreach (var p in _accumulatedPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            _accumulatedItems.Add(new AccumulatedPathItem(p));
        }

        // 高亮项 → 添加/移除按钮可用性 + 计数文本
        var highlighted = (FileList.SelectedItems ?? Array.Empty<object>())
            .OfType<FileBrowserItem>()
            .Where(i => i.CanCheck)
            .ToList();
        var addable = highlighted.Count(i => !_accumulatedPaths.Contains(i.FullPath, StringComparer.OrdinalIgnoreCase));
        var removable = highlighted.Count(i => _accumulatedPaths.Contains(i.FullPath, StringComparer.OrdinalIgnoreCase));
        AddSelectedButton.IsEnabled = addable > 0;
        RemoveSelectedButton.IsEnabled = removable > 0;
        if (addable > 0 || removable > 0)
        {
            PickActionCountText.Text = LocalizationManager.T("Picker_AddRemoveCount", addable, removable);
            PickActionCountText.Foreground = GetThemeBrush("ThemeTextPrimaryBrush");
        }
        else
        {
            PickActionCountText.Text = LocalizationManager.T("Picker_AddRemoveEmpty");
            PickActionCountText.Foreground = GetThemeBrush("ThemeTextSecondaryBrush");
        }

        // 空占位
        PickEmptyText.Text = LocalizationManager.T("Picker_AccumulatedEmpty");
        PickEmptyText.IsVisible = count == 0;
    }

    /// <summary>从当前应用资源中取主题画刷（主题切换后动态解析）。</summary>
    private static IBrush GetThemeBrush(string key)
    {
        if (Application.Current?.TryFindResource(key, out var brush) == true && brush is IBrush b)
        {
            return b;
        }
        return Brushes.Gray;
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

    // ── Add to favorites ────────────────────────────────────────────────────

    /// <summary>
    /// 收藏按钮点击：弹出 <see cref="AddFavoriteDialog"/>（预填当前目录名+路径），
    /// 确认后加入收藏并刷新速选面板。
    /// </summary>
    private async void AddFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentDir) || FavoritePathManager.Exists(_currentDir)) return;

        var dirName = Path.GetFileName(_currentDir.TrimEnd('\\', '/'));
        var dialog = new AddFavoriteDialog(dirName, _currentDir);
        if (await dialog.ShowDialog<bool>(this))
        {
            FavoritePathManager.Add(dialog.FavoriteName, dialog.FavoritePath);
            QuickPath.RefreshSources();
            QuickPath.SetCurrentPath(_currentDir);
            UpdateFavoriteButtonState();
        }
    }

    /// <summary>
    /// 同步收藏按钮状态：当前目录已在收藏（含系统路径）时置灰并提示，否则可点击。
    /// 导航路径变化时调用。
    /// </summary>
    private void UpdateFavoriteButtonState()
    {
        var isFavorite = !string.IsNullOrEmpty(_currentDir) && FavoritePathManager.Exists(_currentDir);
        AddFavoriteButton.IsEnabled = !isFavorite;
        ToolTip.SetTip(AddFavoriteButton,
            isFavorite
                ? LocalizationManager.T("Picker_AlreadyFavorite")
                : LocalizationManager.T("Picker_AddFavorite"));
    }

    // ── Confirm / Cancel ───────────────────────────────────────────────────

    private void TryConfirmFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        SelectedPath = filePath;
        SelectedFileName = Path.GetFileName(filePath);
        Close(true);
    }

    /// <summary>
    /// 解析文件名框输入的文件路径。
    /// 支持绝对路径或相对当前目录的文件名；文件不存在时返回 null。
    /// </summary>
    private string? ResolveTypedFilePath(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();

        // 绝对路径
        if (Path.IsPathRooted(text))
        {
            return File.Exists(text) ? text : null;
        }

        // 相对当前目录
        var combined = Path.Combine(_currentDir, text);
        return File.Exists(combined) ? combined : null;
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

            case PickerMode.PickItems:
                SelectedPaths = _accumulatedPaths
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                Close(true);
                break;

            case PickerMode.OpenFile:
                // 优先文件名框输入：完整路径或相对当前目录的文件名
                var typedPath = ResolveTypedFilePath(FileNameBox.Text);
                if (typedPath != null)
                {
                    SelectedPath = typedPath;
                    SelectedFileName = Path.GetFileName(typedPath);
                    Close(true);
                    break;
                }

                // 回退：浏览区选中文件
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
                case PickerMode.PickItems:
                    // PickItems 模式系统浏览按钮已隐藏；此处仅防御
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomFilePickerDialog] SystemBrowse failed: {ex.Message}");
        }
    }
}
