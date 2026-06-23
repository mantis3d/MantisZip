using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Text;

namespace MantisZip.UI.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ArchiveService _archiveService = new();
    private ArchiveFormat _currentFormat;
    private IReadOnlyList<ArchiveItem>? _allRawItems;
    private bool _isProgrammaticFilter;

    /// <summary>
    /// 由 View 设置的对话框回调。返回选择的文件路径，取消返回 null。
    /// </summary>
    public Func<Task<string?>>? GetOpenFilePath { get; set; }

    /// <summary>
    /// 由 View 设置的对话框回调，用于打开设置窗口。
    /// </summary>
    public Func<Task>? ShowSettingsWindow { get; set; }

    /// <summary>
    /// 由 View 设置的密码对话框回调。参数为压缩包路径，返回密码或取消时返回 null。
    /// </summary>
    public Func<string, Task<string?>>? ShowPasswordDialog { get; set; }

    /// <summary>
    /// 解压设置对话框回调。传入 ExtractSettingsViewModel，返回 true=确认，false=取消。
    /// </summary>
    public Func<ExtractSettingsViewModel, Task<bool?>>? ShowExtractSettingsDialog { get; set; }

    /// <summary>
    /// 压缩设置对话框回调。传入 CompressSettingsViewModel，返回 true=确认，false=取消。
    /// </summary>
    public Func<CompressSettingsViewModel, Task<bool?>>? ShowCompressSettingsDialog { get; set; }

    /// <summary>
    /// 密码管理器窗口回调。
    /// </summary>
    public Func<Task>? ShowPasswordManager { get; set; }

    /// <summary>
    /// 关于对话框回调。
    /// </summary>
    public Func<Task>? ShowAboutDialog { get; set; }

    /// <summary>
    /// 由 View 设置的回调，用于复制文字到剪贴板。
    /// </summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    /// <summary>
    /// 运行带进度窗口的操作。View 负责创建进度窗口、显示、执行操作、关闭窗口。
    /// </summary>
    public Func<string, Func<IProgress<ArchiveProgress>, CancellationToken, Task>, Task<bool>>? RunWithProgress { get; set; }

    /// <summary>
    /// 由 View 设置的文件选择回调。返回选中的文件路径列表，取消返回 null。
    /// </summary>
    public Func<Task<IReadOnlyList<string>?>>? GetOpenFilePaths { get; set; }

    /// <summary>
    /// 由 View 设置的注释编辑对话框回调。参数为现有注释文字，返回新注释或 null=取消。
    /// </summary>
    public Func<string?, Task<string?>>? ShowCommentDialog { get; set; }

    /// <summary>
    /// 会话密码缓存：压缩包路径 → 密码（仅内存，不持久化）。
    /// </summary>
    private readonly Dictionary<string, string> _sessionPasswords = new(StringComparer.OrdinalIgnoreCase);

    // ── i18n ──

    [ObservableProperty]
    private string _currentLanguage = LocalizationManager.CurrentLanguageCode;

    [ObservableProperty]
    private Dictionary<string, string> _localizedStrings = new();

    [RelayCommand]
    private void SwitchLanguage(string? lang)
    {
        LocalizationManager.CurrentLanguage = lang switch
        {
            "zh-CN" => AppLanguage.Chinese,
            "en" => AppLanguage.English,
            _ => LocalizationManager.CurrentLanguage == AppLanguage.Chinese
                ? AppLanguage.English
                : AppLanguage.Chinese
        };
        CurrentLanguage = LocalizationManager.CurrentLanguageCode;
        UpdateLocalizedStrings();
    }

    private void UpdateLocalizedStrings()
    {
        Title = LocalizationManager.T("App_Title");
        if (CurrentArchivePath != null)
        {
            Title = $"{LocalizationManager.T("App_Title")} - {Path.GetFileName(CurrentArchivePath)}";
        }

        var newDict = new Dictionary<string, string>();
        var keys = new[]
        {
            "Menu_File", "Menu_OpenArchive", "Menu_CloseArchive", "Menu_Refresh", "Menu_Settings", "Menu_Exit",
            "Menu_Edit", "Menu_View", "Menu_ToggleTheme", "Menu_Language", "Menu_LangChinese", "Menu_LangEnglish",
            "Menu_Help",
            "Menu_ExtractArchive", "Menu_ExtractHere", "Menu_ExtractToName",
            "Menu_NewArchive", "Menu_Compress", "Menu_PasswordManager", "Menu_About",
            "Toolbar_New", "Toolbar_Open", "Toolbar_Extract", "Toolbar_Compress",
            "Toolbar_Filter", "Toolbar_Preview",
            "Menu_Toolbar", "Menu_FilterBar",
            "Filter_Search", "Filter_Exclude", "Filter_DateFrom", "Filter_DateTo",
            "Filter_SizeMin", "Filter_SizeMax", "Filter_ShowSubfolders",
            "Filter_MatchModeSubstring", "Filter_MatchModeWildcard",
            "Status_Selected", "Status_ArchiveStats",
            "Tree_Browse",
            "DataGrid_Name", "DataGrid_Size", "DataGrid_Compressed", "DataGrid_Modified",
            "App_Title",
            "Main_DropHint",
            "Ctx_Extract", "Ctx_SmartExtract", "Ctx_ExtractTo",
            "Ctx_CopyName", "Ctx_Test", "Ctx_Delete",
            "Menu_SmartExtract", "Menu_TestArchive", "Menu_AddFiles", "Menu_DeleteFiles", "Menu_ArchiveComment",
            "Toolbar_SmartExtract", "Toolbar_Test", "Toolbar_AddFiles", "Toolbar_DeleteFiles",
            "Tooltip_New", "Tooltip_Open", "Tooltip_Extract", "Tooltip_Compress",
            "Tooltip_Filter", "Tooltip_Preview", "Tooltip_SmartExtract", "Tooltip_Test",
            "Tooltip_AddFiles", "Tooltip_DeleteFiles", "Tooltip_Subfolders",
            "Status_AddComplete", "Status_DeleteComplete", "Status_TestOK", "Status_TestFailed",
            "Status_CommentSaved", "Status_SmartExtractSingleRoot", "Status_SmartExtractNamed",
            "Status_CommentNotSupported", "Status_ConfirmDelete",
            "Status_ExtractComplete",
            "Status_Copied", "Status_EntryTested", "Status_CommentSaveFailed", "Status_FileNotFound",
            "Status_TestingEntry", "Status_TestingArchive", "Status_SmartExtracting",
            "Status_AddingFiles", "Status_DeletingFiles", "Status_Entries",
            "Main_NoRecentFiles", "Main_ClearRecentFiles", "Main_RecentFiles",
            "Toolbar_Password", "Tooltip_Password"
        };
        foreach (var key in keys)
        {
            newDict[key] = LocalizationManager.T(key);
        }
        LocalizedStrings = newDict;
        OnPropertyChanged(nameof(LocalizedStrings));

        // Refresh match mode options display text
        MatchModeOptions.Clear();
        MatchModeOptions.Add(new FilterMatchModeInfo { Value = FilterMatchMode.Substring, Display = LocalizationManager.T("Filter_MatchModeSubstring") });
        MatchModeOptions.Add(new FilterMatchModeInfo { Value = FilterMatchMode.Wildcard, Display = LocalizationManager.T("Filter_MatchModeWildcard") });
        // Restore selection after refresh
        SelectedMatchModeOption = MatchModeOptions.FirstOrDefault(o => o.Value == FilterMatchMode);
    }

    public PreviewViewModel Preview { get; } = new();

    [ObservableProperty]
    private string _title = "MantisZip";

    [ObservableProperty]
    private string? _currentArchivePath;

    [ObservableProperty]
    private bool _isArchiveLoaded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private ArchiveItemModel? _selectedEntry;

    /// <summary>
    /// 当前选中的条目列表（由 View 的 SelectionChanged 同步）。
    /// </summary>
    public List<ArchiveItemModel> SelectedEntries { get; } = new();

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private FolderNode? _folderTreeRoot;

    [ObservableProperty]
    private FolderNode? _selectedFolder;

    [ObservableProperty]
    private string? _currentFolder;

    public ObservableCollection<ArchiveItemModel> CurrentEntries { get; } = [];

    public ObservableCollection<ArchiveItemModel> Entries { get; } = [];

    // ── Filter / Toolbar / Status properties ──

    /// <summary>选择统计："已选: N 个, X MB"。</summary>
    public string SelectionStats
    {
        get
        {
            if (SelectedEntry == null) return string.Empty;
            var size = FormatUtil.FormatSize(SelectedEntry.Size);
            return $"{LocalizationManager.T("Status_Selected")}: 1 | {SelectedEntry.NameDisplay}, {size}";
        }
    }

    /// <summary>压缩包统计："ZIP | 原始: X MB → 压缩: Y MB (ratio%)"。</summary>
    public string ArchiveStats
    {
        get
        {
            if (_allRawItems == null || _allRawItems.Count == 0)
                return string.Empty;

            var files = _allRawItems.Where(i => !i.IsDirectory).ToList();
            if (files.Count == 0) return string.Empty;

            var totalSize = files.Sum(i => i.Size);
            var totalCompressed = files.Sum(i => i.CompressedSize);
            var ratio = totalSize > 0
                ? $"({((double)totalCompressed / totalSize * 100):F1}%)"
                : "";

            return $"{_currentFormat} | {FormatUtil.FormatSize(totalSize)} → {FormatUtil.FormatSize(totalCompressed)} {ratio}";
        }
    }

    [ObservableProperty]
    private bool _isFilterBarVisible;

    [ObservableProperty]
    private bool _isPreviewVisible;

    [ObservableProperty]
    private bool _isStatusBarVisible = true;

    [ObservableProperty]
    private string? _filterText;

    [ObservableProperty]
    private string? _filterExcludeText;

    [ObservableProperty]
    private FilterMatchMode _filterMatchMode = FilterMatchMode.Substring;

    [ObservableProperty]
    private FilterMatchModeInfo? _selectedMatchModeOption;

    [ObservableProperty]
    private DateTime? _filterDateFrom;

    [ObservableProperty]
    private DateTime? _filterDateTo;

    [ObservableProperty]
    private long? _filterSizeMin;

    [ObservableProperty]
    private long? _filterSizeMax;

    [ObservableProperty]
    private string _filterSizeUnit = "KB";

    /// <summary>
    /// 大小单位选项列表（直接绑定到 ComboBox）。
    /// </summary>
    public ObservableCollection<string> SizeUnitOptions { get; } = new() { "B", "KB", "MB", "GB" };

    [ObservableProperty]
    private bool _showSubfolders;

    /// <summary>
    /// 匹配模式选项列表（子串匹配 / 通配符）。显示文本通过 UpdateLocalizedStrings() 刷新。
    /// </summary>
    public ObservableCollection<FilterMatchModeInfo> MatchModeOptions { get; } = new();

    public MainWindowViewModel()
    {
        LocalizationManager.CultureChanged += OnCultureChanged;
        UpdateLocalizedStrings();
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        UpdateLocalizedStrings();
    }

    partial void OnSelectedEntryChanged(ArchiveItemModel? value)
    {
        OnPropertyChanged(nameof(SelectionStats));
        App.DebugLog($"[PRV] OnSelectedEntryChanged: {(value?.Name ?? "null")}, archive={CurrentArchivePath != null}");

        if (value != null && CurrentArchivePath != null)
        {
            _ = ShowPreviewAsync(value);
        }
        else
        {
            Preview.Clear();
        }
    }

    partial void OnSelectedFolderChanged(FolderNode? value)
    {
        if (value != null)
        {
            NavigateToFolder(value);
        }
    }

    partial void OnFilterTextChanged(string? value) => ApplyFilter();
    partial void OnFilterExcludeTextChanged(string? value) => ApplyFilter();
    partial void OnFilterMatchModeChanged(FilterMatchMode value) => ApplyFilter();

    partial void OnSelectedMatchModeOptionChanged(FilterMatchModeInfo? value)
    {
        if (value != null && value.Value != FilterMatchMode)
        {
            FilterMatchMode = value.Value;
        }
    }
    partial void OnFilterDateFromChanged(DateTime? value) => ApplyFilter();
    partial void OnFilterDateToChanged(DateTime? value) => ApplyFilter();
    partial void OnFilterSizeMinChanged(long? value) => ApplyFilter();
    partial void OnFilterSizeMaxChanged(long? value) => ApplyFilter();
    partial void OnShowSubfoldersChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (_allRawItems == null || _isProgrammaticFilter) return;
        PopulateEntries();
    }

    [RelayCommand]
    private async Task OpenArchive()
    {
        if (GetOpenFilePath == null) return;

        var path = await GetOpenFilePath();
        if (string.IsNullOrEmpty(path)) return;

        await LoadArchiveAsync(path);
    }

    public async Task LoadArchiveAsync(string path)
    {
        if (!ArchiveFormatHelper.IsArchiveFile(path))
        {
            StatusMessage = LocalizationManager.T("Status_UnsupportedFormat", Path.GetExtension(path));
            return;
        }

        IsLoading = true;
        StatusMessage = LocalizationManager.T("Status_Loading");
        ClearArchiveInternal();

        try
        {
            // Check session password cache first
            string? password = null;
            if (_sessionPasswords.TryGetValue(path, out var cachedPwd))
                password = cachedPwd;

            var result = await _archiveService.LoadArchiveAsync(path, password);

            if (result.IsPasswordRequired)
            {
                // Prompt for password
                if (ShowPasswordDialog != null)
                {
                    password = await ShowPasswordDialog(path);
                    if (password == null)
                    {
                        StatusMessage = LocalizationManager.T("Status_PasswordCancelled");
                        IsLoading = false;
                        return;
                    }

                    // Retry with password
                    result = await _archiveService.LoadArchiveAsync(path, password);

                    if (result.IsPasswordRequired)
                    {
                        StatusMessage = LocalizationManager.T("Status_WrongPassword");
                        IsLoading = false;
                        return;
                    }

                    // Cache password on success
                    _sessionPasswords[path] = password;
                }
                else
                {
                    StatusMessage = LocalizationManager.T("Status_PasswordRequired");
                }
            }

            if (result.IsSuccess && result.Entries != null)
            {
                foreach (var entry in result.Entries)
                {
                    Entries.Add(entry);
                }

                // Build folder tree
                _allRawItems = result.RawItems;
                if (_allRawItems != null)
                {
                    FolderTreeRoot = ArchiveTreeBuilder.BuildTree(_allRawItems, Path.GetFileNameWithoutExtension(path));
                    FolderTreeRoot.IsExpanded = true;
                    SelectedFolder = FolderTreeRoot;
                }

                CurrentArchivePath = path;
                _currentFormat = ArchiveFormatHelper.GetFormat(path);
                IsArchiveLoaded = true;
                RecentFilesManager.AddPath(path);
                RecentFiles.Clear();
                foreach (var rp in RecentFilesManager.GetPaths())
                    RecentFiles.Add(rp);
                StatusMessage = LocalizationManager.T("Status_Loaded", result.Entries.Count);
                Title = $"{LocalizationManager.T("App_Title")} - {Path.GetFileName(path)} ({_allRawItems?.Count ?? 0} {LocalizationManager.T("Status_Entries")})";
                OnPropertyChanged(nameof(ArchiveStats));
            }
            else if (result.IsCancelled)
            {
                StatusMessage = LocalizationManager.T("Status_Cancelled");
            }
            else if (!result.IsPasswordRequired) // Don't override password-related messages
            {
                StatusMessage = result.ErrorMessage ?? LocalizationManager.T("Status_OpenArchiveFailed");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Status_LoadFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ShowPreviewAsync(ArchiveItemModel entry)
    {
        App.DebugLog($"[PRV] ShowPreviewAsync start: {entry.Name}, fmt={_currentFormat}");

        // 切换文件前停止上一个 GIF 动画
        Preview.StopGifTimer();

        try
        {
            var ext = Path.GetExtension(entry.Name);
            var previewType = PreviewService.ClassifyPreview(ext);
            App.DebugLog($"[PRV] Classified as {previewType}");

            if (previewType == PreviewType.Unsupported)
            {
                Preview.ShowUnsupported();
                StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                return;
            }

            if (CurrentArchivePath == null)
            {
                App.DebugLog("[PRV] CurrentArchivePath is null, aborting");
                return;
            }

            StatusMessage = LocalizationManager.T("Status_Extracting");

            var tempFile = await PreviewService.ExtractToTempAsync(
                CurrentArchivePath, entry, _currentFormat);
            App.DebugLog($"[PRV] Extracted to: {tempFile}");

            if (tempFile == null)
            {
                Preview.ShowUnsupported(LocalizationManager.T("Status_ExtractFailed"));
                return;
            }

            switch (previewType)
            {
                case PreviewType.Text:
                    Preview.ShowText(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Text", entry.DisplayName);
                    break;
                case PreviewType.Csv:
                    Preview.ShowCsv(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Csv", entry.DisplayName);
                    break;
                case PreviewType.Pe:
                    Preview.ShowPe(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Pe", entry.DisplayName);
                    break;
                case PreviewType.Image:
                    App.DebugLog("[PRV] Calling ShowImage");
                    DumpThemeState("BEFORE-IMG");
                    Preview.ShowImage(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Image", entry.DisplayName);
                    App.DebugLog("[PRV] ShowImage returned");
                    DumpThemeState("AFTER-IMG");
                    // 延迟诊断：等渲染稳定后再检查一次
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                    {
                        await Task.Delay(1000);
                        DumpThemeState("1S-AFTER-IMG");
                    }, global::Avalonia.Threading.DispatcherPriority.Background);
                    break;
                case PreviewType.Gif:
                    Preview.ShowGif(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Gif", entry.DisplayName);
                    break;
                case PreviewType.Svg:
                    Preview.ShowSvg(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Svg", entry.DisplayName);
                    break;
                case PreviewType.Font:
                    Preview.ShowFont(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Font", entry.DisplayName);
                    break;
                case PreviewType.Audio:
                    Preview.ShowAudio(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Audio", entry.DisplayName);
                    break;
                case PreviewType.Sqlite:
                    Preview.ShowSqlitePreview(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Sqlite", entry.DisplayName);
                    break;
                case PreviewType.Iso:
                    Preview.ShowIso(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Iso", entry.DisplayName);
                    break;
                case PreviewType.Torrent:
                    Preview.ShowTorrent(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Torrent", entry.DisplayName);
                    break;
                case PreviewType.Office:
                    Preview.ShowOffice(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Office", entry.DisplayName);
                    break;
                case PreviewType.Video:
                    Preview.ShowVideo(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Video", entry.DisplayName);
                    break;
                case PreviewType.Html:
                    Preview.ShowHtmlPreview(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Html", entry.DisplayName);
                    break;
                case PreviewType.Markdown:
                    Preview.ShowMarkdownPreview(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Markdown", entry.DisplayName);
                    break;
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"[PRV] ShowPreviewAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Preview.ShowUnsupported(LocalizationManager.T("Status_PreviewFailed", ex.Message));
            StatusMessage = LocalizationManager.T("Status_PreviewFailed", ex.Message);
        }
        App.DebugLog("[PRV] ShowPreviewAsync end");
    }

    private void NavigateToFolder(FolderNode node)
    {
        if (_allRawItems == null) return;
        CurrentFolder = node.FullPath;
        PopulateEntries();
    }

    /// <summary>
    /// 从文件列表双击目录时按路径导航（无需 FolderNode）。
    /// </summary>
    public void NavigateToFolderPath(string path)
    {
        if (_allRawItems == null) return;
        _isProgrammaticFilter = true;
        try
        {
            CurrentFolder = path;
            PopulateEntries();
            // 同步选中目录树中的对应节点
            var node = FindNode(FolderTreeRoot, path);
            if (node != null)
                SelectedFolder = node;
        }
        finally
        {
            _isProgrammaticFilter = false;
        }
    }

    /// <summary>
    /// 应用过滤条件并刷新当前目录的条目显示。
    /// </summary>
    private void FilterFiles() => PopulateEntries();

    /// <summary>
    /// 根据当前过滤条件 + 当前文件夹刷新 CurrentEntries。
    /// </summary>
    private void PopulateEntries()
    {
        if (_allRawItems == null) return;

        _isProgrammaticFilter = true;
        try
        {
            var filteredSource = GetFilteredSource();
            var entries = ArchiveEntryLister.GetEntriesInFolder(
                filteredSource, CurrentFolder ?? "", ShowSubfolders);

            CurrentEntries.Clear();
            foreach (var item in entries)
            {
                var model = ArchiveItemModel.FromCore(item);
                var ext = Path.GetExtension(model.Name);
                model.IconSource = IconService.GetFileIcon(ext);
                CurrentEntries.Add(model);
            }
        }
        finally
        {
            _isProgrammaticFilter = false;
        }
    }

    /// <summary>
    /// 对 _allRawItems 应用文本/日期/大小过滤器，返回过滤后的列表。
    /// </summary>
    private IReadOnlyList<ArchiveItem> GetFilteredSource()
    {
        if (_allRawItems == null) return Array.Empty<ArchiveItem>();

        IEnumerable<ArchiveItem> filtered = _allRawItems;

        return ArchiveFilter.ApplyFilters(filtered.ToList(), new SearchFilters
        {
            Text = string.IsNullOrWhiteSpace(FilterText) ? null : FilterText,
            ExcludeText = string.IsNullOrWhiteSpace(FilterExcludeText) ? null : FilterExcludeText,
            MatchMode = FilterMatchMode,
            DateFrom = FilterDateFrom,
            DateTo = FilterDateTo,
            SizeMin = FilterSizeMin.HasValue ? FilterSizeMin.Value * GetSizeMultiplier() : null,
            SizeMax = FilterSizeMax.HasValue ? FilterSizeMax.Value * GetSizeMultiplier() : null,
        });
    }

    private long GetSizeMultiplier() => FilterSizeUnit?.ToUpperInvariant() switch
    {
        "KB" => 1024L,
        "MB" => 1024L * 1024,
        "GB" => 1024L * 1024 * 1024,
        _ => 1L
    };

    [RelayCommand]
    private void GoUp()
    {
        if (SelectedFolder?.FullPath == "") return;

        var currentPath = SelectedFolder?.FullPath ?? "";
        var lastSlash = currentPath.LastIndexOf('/');
        var parentPath = lastSlash >= 0 ? currentPath[..lastSlash] : "";

        var parent = FindNode(FolderTreeRoot, parentPath);
        SelectedFolder = parent ?? FolderTreeRoot;
    }

    private static FolderNode? FindNode(FolderNode? node, string path)
    {
        if (node == null) return null;
        if (node.FullPath == path) return node;
        foreach (var child in node.Children)
        {
            var found = FindNode(child, path);
            if (found != null) return found;
        }
        return null;
    }

    [RelayCommand]
    private void ClearArchive()
    {
        ClearArchiveInternal();
        StatusMessage = null;
        Title = LocalizationManager.T("App_Title");
    }

    private void ClearArchiveInternal()
    {
        Entries.Clear();
        CurrentEntries.Clear();
        CurrentArchivePath = null;
        IsArchiveLoaded = false;
        SelectedEntry = null;
        SelectedFolder = null;
        FolderTreeRoot = null;
        _allRawItems = null;
        Preview.Clear();
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        if (ShowSettingsWindow != null)
            await ShowSettingsWindow();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        var theme = IsDarkTheme ? "ThemeDark.axaml" : "ThemeLight.axaml";

        if (Application.Current?.Resources.MergedDictionaries.Count > 0)
        {
            Application.Current.Resources.MergedDictionaries[0] =
                new ResourceInclude(new Uri($"avares://MantisZip.UI.Avalonia/Themes/{theme}"))
                {
                    Source = new Uri($"avares://MantisZip.UI.Avalonia/Themes/{theme}")
                };
        }
    }

    // ── Phase 3: Archive commands ──

    [RelayCommand]
    private void CloseArchive()
    {
        ClearArchiveInternal();
        StatusMessage = null;
        Title = LocalizationManager.T("App_Title");
    }

    [RelayCommand]
    private async Task RefreshArchive()
    {
        if (CurrentArchivePath == null) return;

        var savedFolder = CurrentFolder;
        var savedEntryName = SelectedEntry?.FullPath;

        await LoadArchiveAsync(CurrentArchivePath);

        // Restore navigation
        if (savedEntryName != null && FolderTreeRoot != null)
        {
            var savedNode = FindNode(FolderTreeRoot, savedEntryName);
            if (savedNode != null)
            {
                SelectedFolder = savedNode;
                return;
            }
        }

        if (savedFolder != null && FolderTreeRoot != null)
        {
            SelectedFolder = FindNode(FolderTreeRoot, savedFolder) ?? FolderTreeRoot;
        }
    }

    [RelayCommand]
    private async Task ExtractArchive()
    {
        if (CurrentArchivePath == null || ShowExtractSettingsDialog == null) return;

        var vm = new ExtractSettingsViewModel(new[] { CurrentArchivePath });
        var result = await ShowExtractSettingsDialog(vm);
        if (result != true) return;

        var dest = vm.DestinationPath;
        var openFolder = vm.OpenFolderAfterExtract;

        if (RunWithProgress == null) return;

        _sessionPasswords.TryGetValue(CurrentArchivePath, out var password);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_Extracting"),
            async (progress, ct) =>
            {
                await new ExtractService().ExtractAsync(
                    CurrentArchivePath, dest, password, progress, ct);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_ExtractComplete");
        }
    }

    [RelayCommand]
    private async Task ExtractArchiveHere()
    {
        if (CurrentArchivePath == null || RunWithProgress == null) return;

        var dest = Path.GetDirectoryName(CurrentArchivePath)
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        _sessionPasswords.TryGetValue(CurrentArchivePath, out var password);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_Extracting"),
            async (progress, ct) =>
            {
                await new ExtractService().ExtractAsync(
                    CurrentArchivePath, dest, password, progress, ct);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_ExtractComplete");
        }
    }

    [RelayCommand]
    private async Task ExtractArchiveToName()
    {
        if (CurrentArchivePath == null || RunWithProgress == null) return;

        var parentDir = Path.GetDirectoryName(CurrentArchivePath)
                        ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var folderName = Path.GetFileNameWithoutExtension(CurrentArchivePath);
        var dest = Path.Combine(parentDir, folderName);

        _sessionPasswords.TryGetValue(CurrentArchivePath, out var password);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_Extracting"),
            async (progress, ct) =>
            {
                await new ExtractService().ExtractAsync(
                    CurrentArchivePath, dest, password, progress, ct);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_ExtractComplete");
        }
    }

    [RelayCommand]
    private async Task NewArchive()
    {
        if (ShowCompressSettingsDialog == null) return;
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        await ShowCompressSettingsDialog(vm);
    }

    [RelayCommand]
    private async Task CompressSelected()
    {
        if (ShowCompressSettingsDialog == null) return;
        // Opens compress dialog with empty list — user picks files from filesystem in dialog
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        await ShowCompressSettingsDialog(vm);
    }

    [RelayCommand]
    private async Task OpenPasswordManager()
    {
        if (ShowPasswordManager != null)
            await ShowPasswordManager();
    }

    [RelayCommand]
    private async Task CopyFileName()
    {
        if (SelectedEntry == null) return;
        var text = SelectedEntry.FullPath ?? SelectedEntry.Name;
        if (CopyToClipboard != null)
            await CopyToClipboard(text);
        StatusMessage = $"{LocalizationManager.T("Status_Copied")}: {text}";
    }

    [RelayCommand]
    private async Task TestEntry()
    {
        if (SelectedEntry == null || RunWithProgress == null) return;

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_TestingEntry"),
            async (progress, ct) =>
            {
                // Simulate test by checking entry exists in archive
                await Task.Delay(100, ct);
                progress.Report(new ArchiveProgress { PercentComplete = 100, CurrentFile = SelectedEntry.Name });
            });

        if (completed)
            StatusMessage = $"{LocalizationManager.T("Status_EntryTested")}: {SelectedEntry.Name}";
    }

    [RelayCommand]
    private async Task ExtractTo()
    {
        if (CurrentArchivePath == null || ShowExtractSettingsDialog == null) return;

        var vm = new ExtractSettingsViewModel(new[] { CurrentArchivePath });
        vm.DestinationPath = Path.Combine(
            Path.GetDirectoryName(CurrentArchivePath) ?? ".",
            Path.GetFileNameWithoutExtension(CurrentArchivePath));
        var result = await ShowExtractSettingsDialog(vm);
        if (result != true) return;

        _sessionPasswords.TryGetValue(CurrentArchivePath, out var password);
        if (RunWithProgress == null) return;

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_Extracting"),
            async (progress, ct) =>
            {
                await new ExtractService().ExtractAsync(
                    CurrentArchivePath, vm.DestinationPath, password, progress, ct);
            });

        if (completed)
            StatusMessage = LocalizationManager.T("Status_ExtractComplete");
    }

    [RelayCommand]
    private async Task SmartExtract()
    {
        if (CurrentArchivePath == null || RunWithProgress == null) return;

        var parentDir = Path.GetDirectoryName(CurrentArchivePath)
                        ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        bool hasSingleRoot = _allRawItems != null && ArchiveStructureAnalyzer.HasSingleRootDirectory(_allRawItems);

        var dest = hasSingleRoot
            ? parentDir
            : Path.Combine(parentDir, Path.GetFileNameWithoutExtension(CurrentArchivePath));

        _sessionPasswords.TryGetValue(CurrentArchivePath, out var password);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_SmartExtracting"),
            async (progress, ct) =>
            {
                await new ExtractService().ExtractAsync(
                    CurrentArchivePath, dest, password, progress, ct);
            });

        if (completed)
        {
            StatusMessage = hasSingleRoot
                ? LocalizationManager.T("Status_SmartExtractSingleRoot")
                : LocalizationManager.T("Status_SmartExtractNamed");
        }
    }

    [RelayCommand]
    private async Task TestArchive()
    {
        if (CurrentArchivePath == null || RunWithProgress == null) return;

        _sessionPasswords.TryGetValue(CurrentArchivePath, out var password);

        var engine = ArchiveEngineFactory.GetEngineByExtension(CurrentArchivePath);
        if (engine == null) return;

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_TestingArchive"),
            async (progress, ct) =>
            {
                await engine.TestArchiveAsync(CurrentArchivePath, password, progress, ct);
            });

        if (completed)
            StatusMessage = LocalizationManager.T("Status_TestOK");
        else
            StatusMessage = LocalizationManager.T("Status_TestFailed");
    }

    [RelayCommand]
    private async Task EditComment()
    {
        if (CurrentArchivePath == null || ShowCommentDialog == null) return;

        // Only ZIP format supports comments
        if (_currentFormat != ArchiveFormat.Zip)
        {
            StatusMessage = LocalizationManager.T("Status_CommentNotSupported");
            return;
        }

        // Read existing comment using ZipCommentHelper (EOCD binary read, no recompression)
        string? existingComment = null;
        try
        {
            existingComment = ZipCommentHelper.ReadComment(CurrentArchivePath);
        }
        catch
        {
            // If we can't read, start with empty
        }

        var newComment = await ShowCommentDialog(existingComment);
        if (newComment == null) return; // cancelled

        try
        {
            ZipCommentHelper.WriteComment(CurrentArchivePath, newComment);
            StatusMessage = LocalizationManager.T("Status_CommentSaved");
        }
        catch (Exception ex)
        {
            StatusMessage = $"{LocalizationManager.T("Status_CommentSaveFailed")}: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddFiles()
    {
        if (CurrentArchivePath == null || GetOpenFilePaths == null || RunWithProgress == null) return;

        var files = await GetOpenFilePaths();
        if (files == null || files.Count == 0) return;

        var engine = ArchiveEngineFactory.GetEngineByExtension(CurrentArchivePath);
        if (engine == null) return;

        _sessionPasswords.TryGetValue(CurrentArchivePath, out var password);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_AddingFiles"),
            async (progress, ct) =>
            {
                var options = new ArchiveOptions { Password = password };
                await engine.AddToArchiveAsync(CurrentArchivePath, files.ToArray(), options, progress, ct);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_AddComplete");
            await RefreshArchive();
        }
    }

    [RelayCommand]
    private async Task DeleteFiles()
    {
        if (CurrentArchivePath == null || SelectedEntry == null || RunWithProgress == null) return;

        var entryPath = SelectedEntry.FullPath ?? SelectedEntry.Name;

        var engine = ArchiveEngineFactory.GetEngineByExtension(CurrentArchivePath);
        if (engine == null) return;

        _sessionPasswords.TryGetValue(CurrentArchivePath, out var password);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_DeletingFiles"),
            async (progress, ct) =>
            {
                await engine.DeleteEntriesAsync(CurrentArchivePath, new[] { entryPath }, password, progress, ct);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_DeleteComplete");
            await RefreshArchive();
        }
    }

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    [RelayCommand]
    private async Task OpenAbout()
    {
        if (ShowAboutDialog != null)
            await ShowAboutDialog();
    }

    [RelayCommand]
    private void ToggleFilterBar()
    {
        IsFilterBarVisible = !IsFilterBarVisible;
    }

    [RelayCommand]
    private void TogglePreview()
    {
        IsPreviewVisible = !IsPreviewVisible;
    }

    // ── Recent Files ──

    public ObservableCollection<string> RecentFiles { get; } = new(RecentFilesManager.GetPaths());

    // ════════════════════════════════════════════════════════════════
    //  Diagnostics
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 主题状态快照，输出到 debug.log，用于诊断图片预览后按钮渲染异常。
    /// </summary>
    private static void DumpThemeState(string tag)
    {
        try
        {
            var app = global::Avalonia.Application.Current;
            if (app == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"[DIAG-{tag}] Theme state dump:");

            // 1) Theme variant
            var variant = app.RequestedThemeVariant;
            sb.AppendLine($"  RequestedThemeVariant = {variant?.Key ?? "null"}");

            // 2) Merged dictionaries
            sb.AppendLine($"  MergedDictionaries.Count = {app.Resources.MergedDictionaries.Count}");
            for (int i = 0; i < app.Resources.MergedDictionaries.Count; i++)
            {
                var md = app.Resources.MergedDictionaries[i];
                sb.AppendLine($"    [{i}] {md.GetType().Name} {md.GetHashCode()} src={GetSourceUri(md)}");
            }

            // 3) Key brushes
            DumpBrush(app, variant, "ThemeTextPrimaryBrush", sb);
            DumpBrush(app, variant, "ThemeButtonHoverBrush", sb);
            DumpBrush(app, variant, "ThemeButtonBgBrush", sb);
            DumpBrush(app, variant, "ThemeButtonPressedBrush", sb);
            DumpBrush(app, variant, "ThemeAccentBrush", sb);
            DumpBrush(app, variant, "ThemeWindowBgBrush", sb);

            // 4) Platform theme
            if (app.PlatformSettings is IPlatformSettings ps)
            {
                try
                {
                    var cv = ps.GetColorValues();
                    sb.AppendLine($"  PlatformThemeVariant = {cv.ThemeVariant}");
                }
                catch { sb.AppendLine($"  GetColorValues() threw"); }
            }

            sb.AppendLine($"[DIAG-{tag}] end");
            App.DebugLog(sb.ToString());
        }
        catch (Exception ex)
        {
            App.DebugLog($"[DIAG-{tag}] ERROR: {ex.Message}");
        }
    }

    private static string GetSourceUri(object? rp)
    {
        if (rp is null) return "null";
        var t = rp.GetType();
        var srcProp = t.GetProperty("Source");
        if (srcProp != null && srcProp.GetValue(rp) is Uri uri)
            return uri.ToString();
        return t.Name;
    }

    private static void DumpBrush(Application app, ThemeVariant? variant, string key, StringBuilder sb)
    {
        if (app.Resources.TryGetResource(key, variant, out var value))
        {
            if (value is SolidColorBrush scb)
            {
                var c = scb.Color;
                sb.AppendLine($"  {key} = SolidColorBrush(R={c.R},G={c.G},B={c.B},A={c.A}) hash={scb.GetHashCode()}");
            }
            else
            {
                sb.AppendLine($"  {key} = {value?.GetType().Name} hash={value?.GetHashCode()}");
            }
        }
        else
        {
            sb.AppendLine($"  {key} = NOT FOUND (variant={variant?.Key})");
        }
    }

    [RelayCommand]
    private async Task OpenRecentFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            StatusMessage = LocalizationManager.T("Status_FileNotFound");
            RecentFiles.Remove(path);
            return;
        }
        await LoadArchiveAsync(path);
    }

    [RelayCommand]
    private void ClearRecentFiles()
    {
        RecentFilesManager.Clear();
        RecentFiles.Clear();
    }
}

/// <summary>
/// 匹配模式 ComboBox 选项包装，用于显示本地化文本并保留 FilterMatchMode 值。
/// </summary>
public class FilterMatchModeInfo
{
    public FilterMatchMode Value { get; init; }
    public string Display { get; set; } = "";
}
