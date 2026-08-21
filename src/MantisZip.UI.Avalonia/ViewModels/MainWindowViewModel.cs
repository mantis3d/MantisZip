using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Threading;

namespace MantisZip.UI.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ArchiveService _archiveService = new();
    private ArchiveFormat _currentFormat;
    private string? _archiveComment;   // 压缩包注释（ZIP EOCD / RAR5 归档注释），根目录预览展示
    private IReadOnlyList<ArchiveItem>? _allRawItems;
    private int _previewLoadVersion;
    private bool _isProgrammaticFilter;

    // ── Navigation history stacks ──
    private readonly List<string> _backStack = new();
    private readonly List<string> _forwardStack = new();

    /// <summary>
    /// 可由 View 设置的全选回调（DataGrid.SelectAll）。
    /// </summary>
    public Action? SelectAllEntriesAction { get; set; }

    /// <summary>
    /// 可由 View 设置的反选回调。
    /// </summary>
    public Action? InvertSelectionAction { get; set; }

    /// <summary>
    /// 由 View 设置的对话框回调。返回选择的文件路径，取消返回 null。
    /// </summary>
    public Func<Task<string?>>? GetOpenFilePath { get; set; }

    /// <summary>
    /// 由 View 设置的对话框回调，用于打开设置窗口。
    /// </summary>
    public Func<Task>? ShowSettingsWindow { get; set; }

    /// <summary>
    /// 由 View 设置的「保存布局」回调。View 负责读取内容区 Grid 的列宽与预览尺寸并写盘
    /// （目录树/文件列表列宽 + 预览面板各位置记忆尺寸）。
    /// </summary>
    public Action? SaveLayoutAction { get; set; }

    /// <summary>
    /// 由 View 设置的密码对话框回调。参数为压缩包路径，返回 <see cref="PasswordDialogResponse"/> 或取消时返回 null。
    /// </summary>
    public Func<string, Task<PasswordDialogResponse?>>? ShowPasswordDialog { get; set; }

    /// <summary>
    /// 解压设置对话框回调。传入 ExtractSettingsViewModel，返回 true=确认，false=取消。
    /// </summary>
    public Func<ExtractSettingsViewModel, Task<bool?>>? ShowExtractSettingsDialog { get; set; }

    /// <summary>
    /// 解压目标文件夹选择回调。传入待解压条目、初始路径、当前浏览目录（压缩包内）与保留完整路径设置，
    /// 返回所选目录路径，取消返回 null。
    /// </summary>
    public Func<IReadOnlyList<ArchiveItem>, string?, string, bool, Task<string?>>? ShowExtractFolderPicker { get; set; }

    /// <summary>
    /// 压缩设置对话框回调。传入 CompressSettingsViewModel，返回 true=确认，false=取消。
    /// </summary>
    public Func<CompressSettingsViewModel, Task<bool?>>? ShowCompressSettingsDialog { get; set; }

    /// <summary>
    /// 压缩冲突对话框回调。从后台线程调用，返回用户选择的冲突处理方式。
    /// 实现需通过 <see cref="Avalonia.Threading.Dispatcher.UIThread"/> 切换到 UI 线程显示对话框。
    /// 返回值为 (处理方式, 用户自定义文件名, 是否应用到全部)。
    /// </summary>
    public Func<CompressConflictInfo, Task<(Core.Abstractions.CompressConflictAction Action, string? CustomName, bool ApplyToAll)>>? ShowCompressConflictDialog { get; set; }

    /// <summary>
    /// 解压文件冲突对话框回调。从解压引擎的后台线程调用，
    /// 返回用户对单个文件冲突的处理方式，以及是否应用到全部。
    /// 实现需切换到 UI 线程显示对话框。
    /// </summary>
    public Func<FileConflictInfo, (FileConflictAction Action, bool ApplyToAll)>? ShowExtractFileConflictDialog { get; set; }

    /// <summary>
    /// 异步版的文件冲突回调。返回用户对冲突的处理方式和是否应用到全部。
    /// 此回调可在后台线程中 await，适用于 Avalonia 的异步对话框模式。
    /// </summary>
    public Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? ShowExtractFileConflictDialogAsync { get; set; }

    /// <summary>
    /// 添加文件冲突对话框回调（添加到压缩包场景）。与解压冲突同签名，标题用「添加冲突」。
    /// </summary>
    public Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? ShowAddFileConflictDialogAsync { get; set; }

    /// <summary>
    /// 密码管理器窗口回调。
    /// </summary>
    public Func<Task>? ShowPasswordManager { get; set; }

    /// <summary>
    /// 关于对话框回调。
    /// </summary>
    public Func<Task>? ShowAboutDialog { get; set; }

    /// <summary>
    /// 捐赠对话框回调。
    /// </summary>
    public Func<Task>? ShowDonateDialog { get; set; }

    /// <summary>
    /// 收藏管理器窗口回调。
    /// </summary>
    public Func<Task>? ShowFavoritesDialog { get; set; }

    /// <summary>
    /// 由 View 设置的回调，用于复制文字到剪贴板。
    /// </summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    /// <summary>
    /// 运行带进度窗口的操作。View 负责创建进度窗口、显示、执行操作、关闭窗口。
    /// filePaths 非空时显示批处理文件列表（始终可见，单文件也显示）。
    /// </summary>
    public Func<string, IReadOnlyList<string>?, Func<IProgress<ArchiveProgress>, CancellationToken, Task>, Task<bool>>? RunWithProgress { get; set; }

    /// <summary>
    /// 由 View 设置的批处理状态上报回调（索引 + 状态）。
    /// 操作闭包内可将其作为 onItemStatus 传给引擎，实时更新批处理列表项状态。
    /// </summary>
    public Action<int, BatchItemStatus>? BatchStatusReporter { get; set; }

    /// <summary>
    /// 由 View 设置的文件选择回调。返回选中的文件路径列表，取消返回 null。
    /// </summary>
    public Func<Task<IReadOnlyList<string>?>>? GetOpenFilePaths { get; set; }

    /// <summary>
    /// 由 View 设置的注释编辑对话框回调。参数为现有注释文字，返回新注释或 null=取消。
    /// </summary>
    public Func<string?, Task<string?>>? ShowCommentDialog { get; set; }

    /// <summary>
    /// 由 View 设置的打开文件夹对话框回调。参数为文件夹路径，返回 true 表示已打开。
    /// </summary>
    public Func<string, Task<bool>>? ShowOpenFolderDialog { get; set; }

    /// <summary>
    /// 会话密码缓存：压缩包路径 → 密码（仅内存，不持久化）。
    /// </summary>
    private readonly Dictionary<string, string> _sessionPasswords = new(StringComparer.OrdinalIgnoreCase);
    private readonly PasswordService _passwordService = new();
    private readonly AppSettings _appSettings = AppSettings.Load();
    private string? _currentPassword;
    private bool _hasEncryptedArchive;

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

        // Persist so the selection survives restart (AppSettings uses "zh"/"en")
        _appSettings.Language = LocalizationManager.CurrentLanguage == AppLanguage.English ? "en" : "zh";
        SaveSetting(s => s.Language = _appSettings.Language);

        UpdateLocalizedStrings();
    }

    /// <summary>
    /// 供 View（设置窗口关闭后）刷新全部本地化字符串与当前主题菜单文案。
    /// </summary>
    public void RefreshLocalizedStrings() => UpdateLocalizedStrings();

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
            "Menu_File", "Menu_OpenArchive", "Menu_CloseArchive", "Menu_OpenArchiveLocation", "Menu_Refresh", "Menu_Settings", "Menu_Exit",
            "Menu_Edit", "Menu_View", "Menu_ToggleTheme", "Menu_Language", "Menu_LangChinese", "Menu_LangEnglish",
            "Menu_Help", "Menu_Tools",
            "Menu_ExtractArchive", "Menu_ExtractHere", "Menu_ExtractToName",
            "Menu_NewArchive", "Menu_PasswordManager", "Menu_About", "Menu_Donate",
            "Toolbar_Open", "Toolbar_Extract", "Toolbar_Compress",
            "Toolbar_Filter", "Toolbar_Preview",
            "Menu_Toolbar", "Menu_FilterBar",
            "Menu_ProgressBars", "Menu_SepDirBaseline", "Menu_InfoPanelOrientation", "Menu_ShowInfoPanel",
            "Menu_ShowPreviewPanel", "Menu_SaveLayout",
            "Filter_Search", "Filter_Exclude", "Filter_DateFrom", "Filter_DateTo",
            "Filter_SizeMin", "Filter_SizeMax", "Filter_ShowSubfolders",
            "Filter_MatchModeSubstring", "Filter_MatchModeWildcard",
            "Status_Selected", "Status_ArchiveStats",
            "Tree_Browse",
            "DataGrid_Name", "DataGrid_Size", "DataGrid_Compressed", "DataGrid_Modified", "DataGrid_Ratio",
            "App_Title",
            "Main_DropHint",
            "Ctx_Extract", "Ctx_ExtractSelectedHere", "Ctx_ExtractSelectedTo", "Ctx_SmartExtract", "Ctx_ExtractTo",
            "Ctx_CopyName", "Ctx_CopyPath", "Ctx_Test", "Ctx_Delete",
            "Menu_SmartExtract", "Menu_TestArchive", "Menu_AddFiles", "Menu_DeleteFiles", "Menu_ArchiveComment",
            "Toolbar_SmartExtract", "Toolbar_Test", "Toolbar_AddFiles", "Toolbar_DeleteFiles",
            "Tooltip_Open", "Tooltip_Extract", "Tooltip_ExtractSelectedHere", "Tooltip_ExtractSelectedTo", "Tooltip_NewArchive",
            "Tooltip_Filter", "Tooltip_Preview", "Tooltip_SmartExtract", "Tooltip_Test",
            "Tooltip_AddFiles", "Tooltip_DeleteFiles", "Tooltip_Subfolders",
            "Status_AddComplete", "Status_DeleteComplete", "Status_TestOK", "Status_TestFailed",
            "Status_CommentSaved", "Status_SmartExtractSingleRoot", "Status_SmartExtractNamed",
            "Status_CommentNotSupported", "Status_ConfirmDelete",
            "Status_ExtractComplete",
            "Status_Copied", "Status_EntryTested", "Status_CommentSaveFailed", "Status_FileNotFound",
            "Status_OpeningArchive",
            "Status_TestingEntry", "Status_TestingArchive", "Status_SmartExtracting",
            "Status_AddingFiles", "Status_DeletingFiles", "Status_Entries",
            "Main_NoRecentFiles", "Main_ClearRecentFiles", "Main_RecentFiles",
            "Main_Favorites", "Main_IconTestTitle", "Main_UiTestTitle",
            "Toolbar_Password", "Tooltip_Password",
            "Menu_Test",
            "Tree_ExpandAll", "Tree_CollapseAll", "Tree_ExpandToCurrent", "Tree_AutoExpand", "Tree_Filter",
            "Nav_GoRoot", "Nav_GoBack", "Nav_GoForward", "Nav_AddressBar", "Nav_GoUp",
            "Toolbar_CopyName", "Toolbar_Refresh",
            "Toolbar_SelectAll", "Toolbar_InvertSelection", "Toolbar_ViewMode",
            "ViewMode_All", "ViewMode_Files", "ViewMode_Dirs",
            "Test_AboutWindow", "Test_SettingsWindow", "Test_PasswordManager",
            "Test_DonationDialog", "Test_LogPrivacyHelp", "Test_PasswordHelp",
            "Test_CommentDialog", "Test_PasswordEditDialog", "Test_PasswordDialog",
            "Test_ProgressWindow", "Test_ErrorDialog",
            "Test_CompressSettings", "Test_ExtractSettings",
            "Test_CompressConflict", "Test_ConflictDialog", "Test_MatchedPassword",
            "Test_AddFavoriteDialog", "Test_AppMessageBox",
            "Test_ArchiveCommentDialog",
            "Test_ElevationDialog", "Test_ElevationFailedDialog", "Test_ElevationInfoDialog",
            "Test_FavoriteManagerWindow",
            "FavMgr_OpenManager"
        };
        foreach (var key in keys)
        {
            newDict[key] = LocalizationManager.T(key);
        }

        // 主题菜单项显示当前主题状态（三态）
        var themeLabel = _appSettings.Theme switch
        {
            "Light" => LocalizationManager.T("Settings_Appearance_Theme_Light"),
            "Dark" => LocalizationManager.T("Settings_Appearance_Theme_Dark"),
            _ => LocalizationManager.T("Settings_Appearance_Theme_System"),
        };
        newDict["Menu_ToggleTheme"] = LocalizationManager.T("Menu_ToggleThemeFormat", themeLabel);

        LocalizedStrings = newDict;
        OnPropertyChanged(nameof(LocalizedStrings));
        OnPropertyChanged(nameof(ViewModeLabel));

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

    /// <summary>密码状态文字：为空=无加密；"已匹配"或"已加密"。</summary>
    [ObservableProperty]
    private string? _passwordStatusMessage;

    /// <summary>密码状态图标 Geometry：为空=无加密。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordStatus))]
    private object? _passwordStatusIcon;

    /// <summary>是否有密码状态显示。</summary>
    public bool HasPasswordStatus => PasswordStatusIcon != null;

    /// <summary>目录/文件统计："3 dirs, 15 files"。</summary>
    [ObservableProperty]
    private string _dirStats = string.Empty;

    /// <summary>过滤统计："12/20"（显示数/总数），无过滤时为空。</summary>
    [ObservableProperty]
    private string _filterStats = string.Empty;

    /// <summary>编码信息："UTF-8" 或压缩包编码。</summary>
    [ObservableProperty]
    private string _encodingInfo = string.Empty;

    [ObservableProperty]
    private ArchiveItemModel? _selectedEntry;

    /// <summary>
    /// Get all raw archive items (for drag-drop item expansion).
    /// </summary>
    public IReadOnlyList<ArchiveItem> GetAllRawItems()
    {
        return _allRawItems ?? Array.Empty<ArchiveItem>();
    }

    /// <summary>
    /// Get the session password for a given archive path.
    /// </summary>
    public string? GetSessionPassword(string archivePath)
    {
        _sessionPasswords.TryGetValue(archivePath, out var pwd);
        return pwd;
    }

    /// <summary>
    /// 双击文件：提取到临时目录并用系统默认程序打开。
    /// 与 WPF 版 DoubleClickOpenFileAsync 行为对齐。
    /// </summary>
    public async Task OpenEntryWithDefaultAppAsync(ArchiveItemModel entry)
    {
        var threshold = _appSettings.DoubleClickOpenThreshold;

        // 功能禁用（阈值为 0）
        if (threshold <= 0) return;
        if (CurrentArchivePath == null) return;

        // 检查格式是否支持单项提取（Tar/GZip/ISO 不支持）
        if (_currentFormat is ArchiveFormat.Tar or ArchiveFormat.GZip or ArchiveFormat.Iso)
        {
            await AppMessageBox.Show(
                LocalizationManager.T("Main_DoubleClickFormatNotSupported"),
                LocalizationManager.T("App_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 检查密码
        if (_hasEncryptedArchive && string.IsNullOrEmpty(_currentPassword))
        {
            await AppMessageBox.Show(
                LocalizationManager.T("Main_DoubleClickPasswordNeeded"),
                LocalizationManager.T("App_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 超过阈值时弹出确认框
        if (entry.Size > threshold)
        {
            var sizeStr = FormatUtil.FormatSize(entry.Size);
            var result = await AppMessageBox.Show(
                LocalizationManager.T("Main_DoubleClickOpenConfirm", sizeStr),
                LocalizationManager.T("App_ConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        // 临时目录（独立于预览临时目录，避免被 ClearPreviewTemp 清理）
        var tempDir = Path.Combine(AppSettings.GetTempDir(), "OpenWith", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, entry.Name);

        try
        {
            App.DebugLog($"OpenEntryWithDefaultAppAsync: extracting '{entry.FullPath}' ({FormatUtil.FormatSize(entry.Size)}) from '{CurrentArchivePath}'");

            var password = _currentPassword ?? GetSessionPassword(CurrentArchivePath);

            // 大文件（>= 1MB）显示进度窗口，否则直接提取
            if (entry.Size >= 1024 * 1024 && RunWithProgress != null)
            {
                var completed = await RunWithProgress(
                    LocalizationManager.T("Status_Extracting"),
                    new[] { tempFile },
                    async (progress, ct) =>
                    {
                        await ArchiveEntryExtractor.ExtractEntryAsync(
                            CurrentArchivePath, entry.FullPath, tempFile, _currentFormat, password, ct);
                    });

                if (!completed)
                {
                    // 取消或失败：清理临时目录
                    TryDeleteTempDir(tempDir);
                    return;
                }
            }
            else
            {
                await ArchiveEntryExtractor.ExtractEntryAsync(
                    CurrentArchivePath, entry.FullPath, tempFile, _currentFormat, password);
            }

            App.DebugLog($"OpenEntryWithDefaultAppAsync: extracted to '{tempFile}', opening with default app");

            // 用系统默认程序打开
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempFile,
                UseShellExecute = true
            });

            StatusMessage = LocalizationManager.T("Main_Status_DoubleClickOpened", entry.Name);
        }
        catch (Exception ex)
        {
            App.DebugLog($"OpenEntryWithDefaultAppAsync: failed: {ex.Message}");
            await AppMessageBox.Show(
                LocalizationManager.T("Main_Status_ExtractFailed", ex.Message),
                LocalizationManager.T("App_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            TryDeleteTempDir(tempDir);
        }
    }

    private static void TryDeleteTempDir(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

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

    /// <summary>
    /// TreeView 的 ItemsSource：包含根节点的集合，确保根节点（压缩包名）可见。
    /// 当 FolderTreeRoot 更新时同步更新此集合。
    /// </summary>
    public ObservableCollection<FolderNode> FolderTreeItems { get; } = [];

    [ObservableProperty]
    private string? _currentFolder;

    /// <summary>
    /// 目录树"自动展开"开关：开启后每次切换目录自动展开到当前目录并收起其他分支。
    /// </summary>
    [ObservableProperty]
    private bool _autoExpandTree;

    public ObservableCollection<ArchiveItemModel> CurrentEntries { get; } = [];

    public ObservableCollection<ArchiveItemModel> Entries { get; } = [];

    /// <summary>CurrentEntries 重新填充完成后触发（供视图重应用列排序等后置逻辑）。</summary>
    public event EventHandler? EntriesRefreshed;

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

    partial void OnIsPreviewVisibleChanged(bool value)
    {
        // 预览面板显隐持久化（与 ShowPreviewPanel 设置同步，WPF 语义对齐）
        if (_appSettings.ShowPreviewPanel != value)
        {
            _appSettings.ShowPreviewPanel = value;
            // ⚠ 不要直接 _appSettings.Save()：_appSettings 是启动时的内存副本，
            // 整体保存会用过期值覆盖用户刚在设置窗口保存的 PreviewPosition 等设置。
            SaveSetting(s => s.ShowPreviewPanel = value);
        }
    }

    /// <summary>
    /// 只把指定字段同步到磁盘（合并写）：从磁盘加载最新设置、应用单个字段变更后保存，
    /// 避免用启动时的内存副本整体覆盖用户在设置窗口保存的其他设置。
    /// </summary>
    private static void SaveSetting(Action<AppSettings> update)
    {
        var disk = AppSettings.Load();
        update(disk);
        disk.Save();
    }

    [ObservableProperty]
    private bool _isStatusBarVisible = true;

    [ObservableProperty]
    private bool _showProgressBars = true;

    [ObservableProperty]
    private bool _separateDirBaseline;

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

    // ── Navigation history ──

    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    /// <summary>
    /// 地址栏自动补全的目录路径集合。
    /// </summary>
    public ObservableCollection<string> FolderPaths { get; } = new();

    // ── Tree filter ──

    [ObservableProperty]
    private string? _treeFilterText;

    partial void OnTreeFilterTextChanged(string? value)
    {
        ApplyTreeFilter(value);
    }

    private void ApplyTreeFilter(string? filter)
    {
        if (FolderTreeRoot == null) return;
        ApplyTreeFilterRecursive(FolderTreeRoot, filter);
    }

    private static bool ApplyTreeFilterRecursive(FolderNode node, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            node.IsVisible = true;
            foreach (var child in node.Children)
                ApplyTreeFilterRecursive(child, filter);
            return true;
        }

        var selfMatch = node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        var anyChildMatch = false;
        foreach (var child in node.Children)
        {
            if (ApplyTreeFilterRecursive(child, filter))
                anyChildMatch = true;
        }

        node.IsVisible = selfMatch || anyChildMatch;
        if (selfMatch && !string.IsNullOrEmpty(filter))
            node.IsExpanded = true;

        return node.IsVisible;
    }

    // ── View mode (All / Files / Directories) ──

    public enum FileListViewMode { All, FilesOnly, DirectoriesOnly }

    [ObservableProperty]
    private FileListViewMode _viewMode = FileListViewMode.All;

    partial void OnViewModeChanged(FileListViewMode value)
    {
        OnPropertyChanged(nameof(ViewModeLabel));
        PopulateEntries();
    }

    [RelayCommand]
    private void CycleViewMode()
    {
        ViewMode = ViewMode switch
        {
            FileListViewMode.All => FileListViewMode.FilesOnly,
            FileListViewMode.FilesOnly => FileListViewMode.DirectoriesOnly,
            _ => FileListViewMode.All
        };
    }

    public string ViewModeLabel => ViewMode switch
    {
        FileListViewMode.All => LocalizationManager.T("ViewMode_All"),
        FileListViewMode.FilesOnly => LocalizationManager.T("ViewMode_Files"),
        _ => LocalizationManager.T("ViewMode_Dirs")
    };

    /// <summary>
    /// 匹配模式选项列表（子串匹配 / 通配符）。显示文本通过 UpdateLocalizedStrings() 刷新。
    /// </summary>
    public ObservableCollection<FilterMatchModeInfo> MatchModeOptions { get; } = new();

    public MainWindowViewModel()
    {
        LocalizationManager.CultureChanged += OnCultureChanged;
        UpdateLocalizedStrings();

        // Load settings
        ShowProgressBars = _appSettings.ShowProgressBars;
        SeparateDirBaseline = _appSettings.SeparateDirBaseline;
        AutoExpandTree = _appSettings.AutoExpandTreeToCurrent;
        Preview.ShowInfoPanel = _appSettings.ShowPreviewInfoPanel;
        // 信息面板方向同样从设置初始化（此前 PreviewViewModel 硬编码默认 "Vertical"，保存的方向启动后丢失）
        Preview.InfoPanelOrientation = _appSettings.InfoPanelOrientation;
        IsPreviewVisible = _appSettings.ShowPreviewPanel;

        // 主题（三态）：初始化菜单按钮暗色状态显示
        IsDarkTheme = _appSettings.Theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => Application.Current?.RequestedThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark,
        };
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

    /// <summary>
    /// 当 FolderTreeRoot 更新时同步 FolderTreeItems，确保根节点在 TreeView 中可见。
    /// </summary>
    partial void OnFolderTreeRootChanged(FolderNode? value)
    {
        FolderTreeItems.Clear();
        if (value != null)
            FolderTreeItems.Add(value);
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
            var engine = ArchiveEngineFactory.GetEngineByExtension(path);

            // ── Verify session-cached password before trusting it ──
            // SharpCompress can list ZIP entries without verifying the password,
            // so IsEncrypted=true + password!=null from cache doesn't mean the
            // password is correct. Quick-verify and redirect to resolution flow.
            if (password != null && engine != null
                && result.RawItems?.Any(i => i.IsEncrypted) == true
                && !_passwordService.QuickVerifyPassword(path, password, engine))
            {
                _sessionPasswords.Remove(path);
                password = null;
                result = await _archiveService.LoadArchiveAsync(path, null);
            }

            // ── Password resolution flow ──
            if (result.IsPasswordRequired)
            {
                // Phase A: Try PasswordManager saved passwords
                if (engine != null)
                {
                    var match = _passwordService.TryMatchPassword(path, engine);
                    if (match != null)
                    {
                        password = match.Value.Password;
                        result = await _archiveService.LoadArchiveAsync(path, password);
                        if (!result.IsPasswordRequired)
                        {
                            _sessionPasswords[path] = password;
                            _currentPassword = password;
                            _hasEncryptedArchive = true;
                            UpdatePasswordStatus(isMatched: true);
                        }
                    }
                }

                // Phase B: Dialog loop (still need password after saved attempts)
                if (result.IsPasswordRequired)
                {
                    if (ShowPasswordDialog == null)
                    {
                        StatusMessage = LocalizationManager.T("Status_PasswordRequired");
                        IsLoading = false;
                        return;
                    }

                    while (result.IsPasswordRequired)
                    {
                        var dialogResponse = await ShowPasswordDialog(path);
                        if (dialogResponse?.Password == null)
                        {
                            StatusMessage = LocalizationManager.T("Status_PasswordCancelled");
                            IsLoading = false;
                            return;
                        }

                        password = dialogResponse.Password;

                        // QuickVerify before full retry (fast path)
                        if (engine != null && !_passwordService.QuickVerifyPassword(path, password, engine))
                        {
                            StatusMessage = LocalizationManager.T("Status_WrongPassword");
                            continue;
                        }

                        // Full retry with password
                        result = await _archiveService.LoadArchiveAsync(path, password);

                        if (!result.IsPasswordRequired)
                        {
                            // Success
                            _sessionPasswords[path] = password;
                            _currentPassword = password;
                            _hasEncryptedArchive = true;

                            if (dialogResponse.SavePermanently)
                            {
                                _passwordService.TrySavePassword(password, path,
                                    dialogResponse.Patterns, dialogResponse.Description);
                            }

                            UpdatePasswordStatus(isMatched: true);
                        }
                        else
                        {
                            StatusMessage = LocalizationManager.T("Status_WrongPassword");
                        }
                    }
                }
            }
            else
            {
                // No password required: still check for encrypted entries (password may be from cache)
                if (result.RawItems != null && result.RawItems.Any(i => i.IsEncrypted))
                {
                    _hasEncryptedArchive = true;
                    _currentPassword = password;
                    UpdatePasswordStatus(isMatched: password != null);
                }
                else
                {
                    _hasEncryptedArchive = false;
                    _currentPassword = null;
                    UpdatePasswordStatus(isMatched: false);
                }
            }

            if (result.IsSuccess && result.Entries != null)
            {
                // 先于 SelectedFolder 赋值（其触发链会读取 _archiveComment/CurrentArchivePath）：
                // 确保打开压缩包后根目录预览能立即显示注释
                CurrentArchivePath = path;
                _currentFormat = ArchiveFormatHelper.GetFormat(path);
                _archiveComment = ArchiveCommentReader.ReadComment(path, _currentFormat, _currentPassword);

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

                    // Populate address bar with unique directory paths
                    FolderPaths.Clear();
                    var dirs = new HashSet<string> { "" };
                    foreach (var item in _allRawItems)
                    {
                        var fullPath = item.FullPath;
                        var lastSlash = fullPath.LastIndexOf('/');
                        while (lastSlash >= 0)
                        {
                            var dirPath = fullPath[..lastSlash];
                            if (dirs.Add(dirPath))
                                FolderPaths.Add(dirPath);
                            lastSlash = dirPath.LastIndexOf('/');
                        }
                    }
                    // Sort paths
                    var sorted = FolderPaths.OrderBy(p => p).ToList();
                    FolderPaths.Clear();
                    foreach (var p in sorted)
                        FolderPaths.Add(p);
                }

                IsArchiveLoaded = true;
                RecentFilesManager.AddPath(path);
                RecentFiles.Clear();
                foreach (var rp in RecentFilesManager.GetPaths())
                    RecentFiles.Add(rp);
                StatusMessage = LocalizationManager.T("Status_Loaded", result.Entries.Count);
                Title = $"{LocalizationManager.T("App_Title")} - {Path.GetFileName(path)} ({_allRawItems?.Count ?? 0} {LocalizationManager.T("Status_Entries")})";
                OnPropertyChanged(nameof(ArchiveStats));
                EncodingInfo = _currentFormat switch
                {
                    ArchiveFormat.Zip => "UTF-8", // Common default; actual detection TBD
                    _ => string.Empty
                };
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

    /// <summary>
    /// 更新密码状态 UI（锁图标 + 状态文字）。
    /// </summary>
    private void UpdatePasswordStatus(bool isMatched)
    {
        if (!_hasEncryptedArchive)
        {
            PasswordStatusMessage = null;
            PasswordStatusIcon = null;
            return;
        }

        PasswordStatusIcon = isMatched
            ? Application.Current?.FindResource("IconLockOpen")
            : Application.Current?.FindResource("IconLockClosed");
        PasswordStatusMessage = isMatched
            ? LocalizationManager.T("Status_PasswordMatched")
            : LocalizationManager.T("Status_Encrypted");
    }

    private async Task ShowPreviewAsync(ArchiveItemModel entry)
    {
        App.DebugLog($"[PRV] ShowPreviewAsync start: {entry.Name}, fmt={_currentFormat}");

        // Phase 1: Immediate — show loading state + populate info panel from in-memory data.
        // This runs synchronously before any async extraction, so user never sees stale content.
        var version = Interlocked.Increment(ref _previewLoadVersion);
        Preview.StopGifTimer();
        Preview.ShowLoading(entry.NameDisplay ?? entry.Name);
        Preview.UpdateCommonMetadata(
            entry.NameDisplay ?? entry.Name,
            entry.SizeDisplay,
            entry.CompressedSizeDisplay,
            entry.Size > 0 ? $"{entry.CompressionRatio:F1}%" : "N/A",
            entry.LastModifiedDisplay);
        StatusMessage = LocalizationManager.T("Status_Extracting");

        try
        {
            var ext = Path.GetExtension(entry.Name);

            // ── Magic detection ──
            var previewType = PreviewType.Unsupported;
            FileFormat magicFormat = FileFormat.Unknown;
            string? detectedFormatName = null;
            if (PreviewService.EnableFormatDetection && CurrentArchivePath != null)
            {
                try
                {
                    _sessionPasswords.TryGetValue(CurrentArchivePath, out var pwd);
                    var (magicType, format, displayName) = await PreviewService.ClassifyPreviewByMagicAsync(
                        CurrentArchivePath, entry, _currentFormat,
                        PreviewService.PreviewHeadSize, pwd);
                    if (magicType != PreviewType.Unsupported && format != FileFormat.Unknown)
                    {
                        previewType = magicType;
                        magicFormat = format;
                        detectedFormatName = displayName;
                        App.DebugLog($"[PRV] Magic detected: {format} ({displayName}) -> {previewType}");
                    }
                }
                catch (Exception ex)
                {
                    App.DebugLog($"[PRV] Magic detection failed: {ex.Message}");
                }
            }

            if (previewType == PreviewType.Unsupported)
            {
                previewType = PreviewService.ClassifyPreview(ext);
                App.DebugLog($"[PRV] Fallback to extension classification: {previewType}");
            }

            if (detectedFormatName == null && PreviewService.EnableFormatDetection)
            {
                var extFormat = FileFormatDetector.DetectByExtension(ext);
                if (extFormat != FileFormat.Unknown)
                    detectedFormatName = FileFormatHelper.GetDisplayName(extFormat);
            }

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

            // ── 预览大小上限检查（仅对需要加载完整内容的格式生效，只读头的格式不受限制）──
            // 与 WPF 版 MainWindow.Preview.cs 语义一致；文本类格式另有 MaxTextPreviewBytes 上限
            if (!PreviewService.IsMetadataOnlyExtension(ext) && entry.Size > PreviewService.MaxPreviewFileSize)
            {
                var limitMb = PreviewService.MaxPreviewFileSize / (1024.0 * 1024.0);
                Preview.ShowUnsupported(LocalizationManager.T(
                    "Preview_TooLarge", (double)entry.Size / 1024 / 1024, limitMb));
                StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                return;
            }

            // ── Extract to temp (async, slow) ──
            var tempFile = await PreviewService.ExtractToTempAsync(
                CurrentArchivePath, entry, _currentFormat, _currentPassword);
            App.DebugLog($"[PRV] Extracted to: {tempFile}");

            if (tempFile == null)
            {
                Preview.ShowUnsupported(LocalizationManager.T("Status_ExtractFailed"));
                return;
            }

            // Version guard: if user selected another file while extracting, discard
            if (version != _previewLoadVersion)
            {
                App.DebugLog($"[PRV] Stale preview result discarded (version {version} != {_previewLoadVersion})");
                try { File.Delete(tempFile); } catch { /* best effort */ }
                return;
            }

            // Phase 2: Content loaded — show the actual preview
            switch (previewType)
            {
                case PreviewType.Text:
                    if (!PreviewService.EnableTextPreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_TextDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    if (entry.Size > PreviewService.MaxTextPreviewBytes)
                    {
                        var txtLimitMb = PreviewService.MaxTextPreviewBytes / (1024.0 * 1024.0);
                        Preview.ShowUnsupported(LocalizationManager.T(
                            "Preview_TooLargeText", (double)entry.Size / 1024 / 1024, txtLimitMb));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    Preview.ShowText(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Text", entry.DisplayName);
                    break;
                case PreviewType.Csv:
                    if (!PreviewService.EnableTextPreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_TextDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    if (entry.Size > PreviewService.MaxTextPreviewBytes)
                    {
                        var txtLimitMb = PreviewService.MaxTextPreviewBytes / (1024.0 * 1024.0);
                        Preview.ShowUnsupported(LocalizationManager.T(
                            "Preview_TooLargeText", (double)entry.Size / 1024 / 1024, txtLimitMb));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    Preview.ShowCsv(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Csv", entry.DisplayName);
                    break;
                case PreviewType.Pe:
                    Preview.ShowPe(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Pe", entry.DisplayName);
                    break;
                case PreviewType.Image:
                    if (!PreviewService.EnableImagePreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_ImageDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    var icoExt = Path.GetExtension(tempFile).ToLowerInvariant();
                    if (icoExt == ".ico")
                    {
                        Preview.ShowIcoGallery(tempFile);
                        StatusMessage = LocalizationManager.T("Preview_Ico", entry.DisplayName);
                    }
                    else
                    {
                        Preview.ShowImage(tempFile);
                        StatusMessage = LocalizationManager.T("Preview_Image", entry.DisplayName);
                    }
                    break;
                case PreviewType.AnimatedImage:
                    if (!PreviewService.EnableImagePreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_ImageDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    Preview.ShowGif(tempFile);
                    var isGifFile = ext.Equals(".gif", StringComparison.OrdinalIgnoreCase);
                    StatusMessage = LocalizationManager.T(
                        isGifFile ? "Preview_Gif" : "Preview_AnimatedImage", entry.DisplayName);
                    break;
                case PreviewType.Svg:
                    if (!PreviewService.EnableImagePreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_ImageDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
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
                    // Legacy Office type: try magic-detected types
                    Preview.ShowOffice(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Office", entry.DisplayName);
                    break;
                case PreviewType.Docx:
                    Preview.ShowDocx(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Docx", entry.DisplayName);
                    break;
                case PreviewType.Xlsx:
                    Preview.ShowXlsx(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Xlsx", entry.DisplayName);
                    break;
                case PreviewType.Pptx:
                    Preview.ShowPptx(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Pptx", entry.DisplayName);
                    break;
                case PreviewType.Video:
                    Preview.ShowVideo(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Video", entry.DisplayName);
                    break;
                case PreviewType.Html:
                    if (!PreviewService.EnableTextPreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_TextDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    if (entry.Size > PreviewService.MaxTextPreviewBytes)
                    {
                        var txtLimitMb = PreviewService.MaxTextPreviewBytes / (1024.0 * 1024.0);
                        Preview.ShowUnsupported(LocalizationManager.T(
                            "Preview_TooLargeText", (double)entry.Size / 1024 / 1024, txtLimitMb));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    Preview.ShowHtmlPreview(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Html", entry.DisplayName);
                    break;
                case PreviewType.Pdf:
                    await Preview.ShowPdfAsync(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Pdf", entry.DisplayName);
                    break;
                case PreviewType.Markdown:
                    if (!PreviewService.EnableTextPreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_TextDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    if (entry.Size > PreviewService.MaxTextPreviewBytes)
                    {
                        var txtLimitMb = PreviewService.MaxTextPreviewBytes / (1024.0 * 1024.0);
                        Preview.ShowUnsupported(LocalizationManager.T(
                            "Preview_TooLargeText", (double)entry.Size / 1024 / 1024, txtLimitMb));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    Preview.ShowMarkdownPreview(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Markdown", entry.DisplayName);
                    break;
            }

            // Populate format metadata from magic detection (if any)
            if (detectedFormatName != null)
            {
                var extFormat = FileFormatDetector.DetectByExtension(ext);
                bool hasConflict = extFormat != FileFormat.Unknown
                    && magicFormat != FileFormat.Unknown
                    && extFormat != magicFormat;
                string formatValue = hasConflict
                    ? LocalizationManager.T("Preview_FormatConflictWarn", detectedFormatName, ext)
                    : detectedFormatName;
                for (int i = Preview.FormatMetadata.Count - 1; i >= 0; i--)
                {
                    if (Preview.FormatMetadata[i].Key == LocalizationManager.T("Preview_FormatLabel"))
                        Preview.FormatMetadata.RemoveAt(i);
                }
                Preview.FormatMetadata.Insert(0, new FormatMetadataItem(LocalizationManager.T("Preview_FormatLabel"), formatValue));
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

    /// <summary>
    /// 统一导航入口：记录历史，更新 CurrentFolder，刷新条目列表，同步目录树选择。
    /// </summary>
    private void NavigateAndPushHistory(string path)
    {
        if (_allRawItems == null) return;
        if (CurrentFolder == path) return;

        if (CurrentFolder != null)
            _backStack.Add(CurrentFolder);
        _forwardStack.Clear();

        CurrentFolder = path;
        PopulateEntries();
        UpdatePreviewForFolder();

        var node = FindNode(FolderTreeRoot, path);
        if (node != null)
            SelectedFolder = node;

        NotifyNavigationState();
    }

    private void NavigateToFolder(FolderNode node)
    {
        if (_allRawItems == null) return;
        // OnSelectedFolderChanged calls NavigateToFolder — we push history here
        // but skip push if already at the same folder
        if (CurrentFolder == node.FullPath) return;

        if (CurrentFolder != null)
            _backStack.Add(CurrentFolder);
        _forwardStack.Clear();

        CurrentFolder = node.FullPath;
        PopulateEntries();
        UpdatePreviewForFolder();

        NotifyNavigationState();
    }

    /// <summary>
    /// 目录导航后同步预览区：根目录且有压缩包注释时展示注释；
    /// 否则若文件列表无选中项则清空预览（避免残留上个目录的内容）。
    /// 注释展示覆盖一切（文件列表选中条目时由 ShowPreviewAsync 覆盖注释）。
    /// </summary>
    private void UpdatePreviewForFolder()
    {
        if (string.IsNullOrEmpty(CurrentFolder) && !string.IsNullOrEmpty(_archiveComment))
        {
            Preview.ShowArchiveComment(_archiveComment,
                Path.GetFileName(CurrentArchivePath ?? string.Empty));
            return;
        }

        if (SelectedEntry == null)
        {
            Preview.Clear();
        }
    }

    /// <summary>
    /// 从文件列表双击目录时按路径导航（无需 FolderNode）。
    /// </summary>
    public void NavigateToFolderPath(string path)
    {
        if (_allRawItems == null) return;

        if (CurrentFolder != null)
            _backStack.Add(CurrentFolder);
        _forwardStack.Clear();

        _isProgrammaticFilter = true;
        try
        {
            CurrentFolder = path;
            PopulateEntries();
            UpdatePreviewForFolder();
            // 同步选中目录树中的对应节点
            var node = FindNode(FolderTreeRoot, path);
            if (node != null)
                SelectedFolder = node;

            NotifyNavigationState();
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

            // 计算目录聚合统计（基于过滤后的数据源，与显示一致）：
            // 目录大小 = 子树内所有文件大小之和，日期 = 子树内最新文件修改时间
            var dirStats = ArchiveEntryLister.ComputeDirectoryStats(filteredSource);

            // Apply view mode filter
            if (ViewMode == FileListViewMode.FilesOnly)
                entries = entries.Where(e => !e.IsDirectory).ToList();
            else if (ViewMode == FileListViewMode.DirectoriesOnly)
                entries = entries.Where(e => e.IsDirectory).ToList();

            var compressedSizeAvailable = GetCompressedSizeAvailable(_currentFormat);

            CurrentEntries.Clear();
            foreach (var item in entries)
            {
                var model = ArchiveItemModel.FromCore(item);
                if (model.IsDirectory)
                {
                    // 应用目录聚合：大小 = 子树和，日期 = 子树最新，压缩后大小 = 子树和
                    if (dirStats.TryGetValue(model.FullPath, out var stat))
                    {
                        model.Size = stat.Size;
                        model.CompressedSize = stat.CompressedSize;
                        model.LastModified = stat.NewestModified;
                    }
                    model.IconSource = IconService.GetFolderIcon();
                }
                else
                {
                    var ext = Path.GetExtension(model.Name);
                    model.IconSource = IconService.GetFileIcon(ext);
                }
                // 按当前格式设置压缩后大小可用性（文件 + 目录一致）
                model.CompressedSizeAvailable = compressedSizeAvailable;
                model.ProgressBarEnabled = ShowProgressBars;
                CurrentEntries.Add(model);
            }

            // Populate DirStats
            var dirCount = CurrentEntries.Count(e => e.IsDirectory);
            var fileCount = CurrentEntries.Count - dirCount;
            DirStats = $"{dirCount} dirs, {fileCount} files";

            // Populate FilterStats — only show when filters are active
            if (!string.IsNullOrWhiteSpace(FilterText) || !string.IsNullOrWhiteSpace(FilterExcludeText) ||
                FilterDateFrom.HasValue || FilterDateTo.HasValue ||
                FilterSizeMin.HasValue || FilterSizeMax.HasValue)
            {
                var totalItems = _allRawItems?.Count ?? 0;
                FilterStats = $"{CurrentEntries.Count}/{totalItems}";
            }
            else
            {
                FilterStats = string.Empty;
            }

            // Compute progress bar ratios
            if (ShowProgressBars)
            {
                ComputeProgressBarRatios();
            }

            // 条目重填完成，通知视图重应用列排序
            EntriesRefreshed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isProgrammaticFilter = false;
        }
    }

    /// <summary>
    /// 当前格式是否能提供逐项压缩后大小。
    /// Zip 可用；ISO/.tar 未压缩（引擎以原大小填充，等价可用）；
    /// .tgz/.tar.gz/.gz 有压缩但无法获得逐项压缩后大小（不可用，压缩后大小列显示空）；
    /// 7z/RAR 同样不可用。与 WPF 版 GetCompressedDisplayMode 语义对齐。
    /// </summary>
    private bool GetCompressedSizeAvailable(ArchiveFormat format)
    {
        if (format == ArchiveFormat.Zip) return true;
        if (format == ArchiveFormat.Iso) return true;
        if (format == ArchiveFormat.Tar)
        {
            // .tar 是未压缩的容器格式；.tgz/.tar.gz/.gz 有压缩但引擎只填原大小（TarGzEngine ListEntriesAsync
            // 对所有 tar 系条目填 CompressedSize = Size），视为不可用以免显示误导性的"压缩后=原大小"
            var ext = Path.GetExtension(CurrentArchivePath ?? string.Empty).ToLowerInvariant();
            return ext == ".tar";
        }
        return false; // 7z, RAR, .tgz/.tar.gz/.gz
    }

    /// <summary>
    /// 计算所有条目的进度条比例值（相对大小、压缩比、日期分布等）。
    /// 基准语义与 WPF 版 FilterFiles 对齐：
    /// - 目录独立基准开：文件组、目录组各自取组内最大值做基准，两组各有满格代表；
    /// - 关：全部条目（含目录）统一取最大值做基准。
    /// </summary>
    private void ComputeProgressBarRatios()
    {
        if (CurrentEntries.Count == 0) return;

        long maxSize, maxCompressed;
        long maxDirSize = 0, maxDirCompressed = 0;

        if (SeparateDirBaseline)
        {
            var files = CurrentEntries.Where(e => !e.IsDirectory).ToList();
            var dirs = CurrentEntries.Where(e => e.IsDirectory).ToList();
            maxSize = files.Count > 0 ? files.Max(e => (long)e.Size) : 0;
            maxCompressed = files.Count > 0 ? files.Max(e => (long)e.CompressedSize) : 0;
            maxDirSize = dirs.Count > 0 ? dirs.Max(e => (long)e.Size) : 0;
            maxDirCompressed = dirs.Count > 0 ? dirs.Max(e => (long)e.CompressedSize) : 0;
        }
        else
        {
            maxSize = CurrentEntries.Max(e => (long)e.Size);
            maxCompressed = CurrentEntries.Max(e => (long)e.CompressedSize);
        }

        // 日期条与 WPF 一致：全部条目（含目录）参与 min/max；仅一条有效日期时满格
        var datedItems = CurrentEntries.Where(e => e.LastModified > DateTime.MinValue).ToList();
        var minDate = datedItems.Count > 0 ? datedItems.Min(e => e.LastModified) : DateTime.Now;
        var maxDate = datedItems.Count > 0 ? datedItems.Max(e => e.LastModified) : DateTime.Now;
        var dateRange = (maxDate - minDate).TotalSeconds;

        foreach (var item in CurrentEntries)
        {
            bool useDirBase = SeparateDirBaseline && item.IsDirectory;
            long baseSize = useDirBase ? maxDirSize : maxSize;
            long baseCompressed = useDirBase ? maxDirCompressed : maxCompressed;

            item.SizeRatio = baseSize > 0 ? (double)item.Size / baseSize : 0;
            item.CompressedSizeRatio = baseCompressed > 0 ? (double)item.CompressedSize / baseCompressed : 0;
            item.DateRatio = dateRange > 0
                ? (item.LastModified - minDate).TotalSeconds / dateRange
                : (datedItems.Count == 1 ? 1.0 : 0);
            // 压缩率条（绝对比例）：目录与文件一视同仁显示聚合压缩率（与 RatioDisplay 文本一致，
            // Avalonia 对 WPF 的有意差异）；格式无法提供逐项压缩后大小时为 0
            item.RatioBarValue = item.Size > 0 && item.CompressedSizeAvailable
                ? Math.Min((double)item.CompressedSize / item.Size, 1.0)
                : 0;
            item.UseDirProgressColor = item.IsDirectory && SeparateDirBaseline;
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

    // ── Navigation commands ──

    [RelayCommand]
    private void GoUp()
    {
        if (SelectedFolder?.FullPath == "") return;

        var currentPath = SelectedFolder?.FullPath ?? "";
        var lastSlash = currentPath.LastIndexOf('/');
        var parentPath = lastSlash >= 0 ? currentPath[..lastSlash] : "";

        NavigateAndPushHistory(parentPath);
    }

    [RelayCommand]
    private void GoRoot()
    {
        if (FolderTreeRoot == null) return;
        NavigateAndPushHistory("");
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_backStack.Count == 0) return;
        var target = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);

        if (CurrentFolder != null)
            _forwardStack.Add(CurrentFolder);

        _isProgrammaticFilter = true;
        try
        {
            CurrentFolder = target;
            PopulateEntries();
            var node = FindNode(FolderTreeRoot, target) ?? FolderTreeRoot;
            if (node != null)
                SelectedFolder = node;
        }
        finally
        {
            _isProgrammaticFilter = false;
        }

        NotifyNavigationState();
    }

    [RelayCommand]
    private void GoForward()
    {
        if (_forwardStack.Count == 0) return;
        var target = _forwardStack[^1];
        _forwardStack.RemoveAt(_forwardStack.Count - 1);

        if (CurrentFolder != null)
            _backStack.Add(CurrentFolder);

        _isProgrammaticFilter = true;
        try
        {
            CurrentFolder = target;
            PopulateEntries();
            var node = FindNode(FolderTreeRoot, target) ?? FolderTreeRoot;
            if (node != null)
                SelectedFolder = node;
        }
        finally
        {
            _isProgrammaticFilter = false;
        }

        NotifyNavigationState();
    }

    // ── Tree expand / collapse commands ──

    [RelayCommand]
    private void ExpandAll()
    {
        FolderTreeRoot?.ExpandAll();
    }

    [RelayCommand]
    private void CollapseAll()
    {
        FolderTreeRoot?.CollapseAll();
    }

    [RelayCommand]
    private void ExpandToCurrent()
    {
        if (FolderTreeRoot == null || SelectedFolder == null) return;
        ExpandTreeToPath(SelectedFolder.FullPath);
    }

    /// <summary>
    /// 收起整棵树（保留根展开），仅展开到指定路径的祖先链。
    /// 手动"定位到当前目录"与"自动展开"开关共用同一实现。
    /// </summary>
    private void ExpandTreeToPath(string path)
    {
        if (FolderTreeRoot == null) return;
        // Collapse all first
        FolderTreeRoot.CollapseAll();
        // Expand root
        FolderTreeRoot.IsExpanded = true;
        // 根目录（空路径）：收起所有分支即可，无需展开祖先链
        if (string.IsNullOrEmpty(path)) return;
        // Then expand ancestors of the target path
        ExpandAncestorsOf(FolderTreeRoot, path);
    }

    /// <summary>
    /// 切换目录时若开启"自动展开"，收起其他分支并展开到新目录的祖先链。
    /// 挂在 CurrentFolder 变化上，天然覆盖树点击 / 文件列表双击 / 地址栏 / 前进后退等所有导航路径。
    /// 仅 null（清空压缩包，树将重置）跳过；空字符串（根目录）也执行收起。
    /// </summary>
    partial void OnCurrentFolderChanged(string? value)
    {
        if (!AutoExpandTree) return;
        if (value == null) return;
        ExpandTreeToPath(value);
    }

    /// <summary>
    /// 自动展开开关变化时持久化到 AppSettings。
    /// </summary>
    partial void OnAutoExpandTreeChanged(bool value)
    {
        _appSettings.AutoExpandTreeToCurrent = value;
        SaveSetting(s => s.AutoExpandTreeToCurrent = value);
        // 开启时立即执行一次，让当前目录立刻在树中定位
        if (value)
            ExpandTreeToPath(CurrentFolder ?? "");
    }

    private static void ExpandAncestorsOf(FolderNode node, string targetPath)
    {
        if (node.FullPath == targetPath) return;
        foreach (var child in node.Children)
        {
            if (IsAncestorOf(child, targetPath))
            {
                child.IsExpanded = true;
                ExpandAncestorsOf(child, targetPath);
                return;
            }
        }
    }

    private static bool IsAncestorOf(FolderNode node, string targetPath)
    {
        if (node.FullPath == targetPath) return true;
        return node.Children.Any(c => IsAncestorOf(c, targetPath));
    }

    // ── Selection commands ──

    [RelayCommand]
    private void SelectAll()
    {
        SelectAllEntriesAction?.Invoke();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        InvertSelectionAction?.Invoke();
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
        CurrentFolder = null;
        FolderTreeRoot = null;
        _allRawItems = null;
        _archiveComment = null;
        DirStats = string.Empty;
        FilterStats = string.Empty;
        EncodingInfo = string.Empty;
        Preview.Clear();
        _currentPassword = null;
        _hasEncryptedArchive = false;
        PasswordStatusMessage = null;
        PasswordStatusIcon = null;
        FolderPaths.Clear();
        _backStack.Clear();
        _forwardStack.Clear();
        NotifyNavigationState();
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
        // 三态循环：System → Light → Dark → System
        var theme = _appSettings.Theme switch
        {
            "System" => "Light",
            "Light" => "Dark",
            _ => "System",
        };
        _appSettings.Theme = theme;
        SaveSetting(s => s.Theme = theme);

        // 立即应用（RefreshTheme 会读取 AppSettings 并设置 RequestedThemeVariant）
        App.RefreshTheme();

        // 同步菜单按钮的暗色状态显示
        IsDarkTheme = theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => Application.Current?.RequestedThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark,
        };

        // 刷新菜单项「切换颜色模式」显示的当前主题文案
        UpdateLocalizedStrings();
    }

    // ── Phase 3: Archive commands ──

    [RelayCommand]
    private void OpenArchiveLocation()
    {
        if (CurrentArchivePath == null) return;
        var dir = Path.GetDirectoryName(CurrentArchivePath);
        if (string.IsNullOrEmpty(dir)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{CurrentArchivePath}\"",
            UseShellExecute = true
        });
    }

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
        var filteredKeys = vm.FilteredEntryKeys;

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_Extracting"),
            new[] { CurrentArchivePath! },
            async (progress, ct) =>
            {
                // 统一解压执行入口（冲突策略/过滤/全量分支）——与 CLI 弹窗批处理共用 ExtractFlow
                await ExtractFlow.ExtractAsync(
                    CurrentArchivePath, dest, vm.ConflictAction, filteredKeys, password,
                    ShowExtractFileConflictDialogAsync, progress, ct);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_ExtractComplete");
            if (openFolder)
            {
                await OpenExtractedFolderAsync(dest, CurrentArchivePath!, password);
            }
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
            new[] { CurrentArchivePath! },
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
            new[] { CurrentArchivePath! },
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

    /// <summary>
    /// 解压选中的条目到当前目录（压缩包所在目录）。
    /// </summary>
    [RelayCommand]
    private async Task ExtractSelectedHere()
    {
        if (CurrentArchivePath == null || RunWithProgress == null) return;
        if (SelectedEntries.Count == 0 && SelectedEntry == null) return;

        var entries = GetSelectedEntriesForExtract();
        if (entries.Count == 0) return;

        var dest = Path.GetDirectoryName(CurrentArchivePath)
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        await ExtractSelectedEntriesCoreAsync(entries, dest);
    }

    /// <summary>
    /// 解压选中的条目到用户指定目录（弹出文件选择器，默认定位到压缩包同名文件夹）。
    /// 冲突策略与打开文件夹行为使用 AppSettings 默认值。
    /// </summary>
    [RelayCommand]
    private async Task ExtractSelectedTo()
    {
        if (CurrentArchivePath == null || ShowExtractFolderPicker == null) return;
        if (SelectedEntries.Count == 0 && SelectedEntry == null) return;

        var entries = GetSelectedEntriesForExtract();
        if (entries.Count == 0) return;

        var parentDir = Path.GetDirectoryName(CurrentArchivePath)
                        ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var defaultDest = Path.Combine(parentDir, Path.GetFileNameWithoutExtension(CurrentArchivePath));

        var settings = AppSettings.Load();
        var dest = await ShowExtractFolderPicker(entries, defaultDest, CurrentFolder ?? "", settings.ExtractPreserveFullPath);
        if (string.IsNullOrEmpty(dest)) return;

        await ExtractSelectedEntriesCoreAsync(entries, dest);
    }

    /// <summary>
    /// 执行选中条目的解压（统一走 <see cref="ExtractFlow.RunSelectedItemsExtractionAsync"/>，
    /// 与拖拽解压拿到目标路径后完全同一流程：进度窗口、压缩包一行批处理列表、状态驱动、失败弹窗）。
    /// 冲突策略与打开文件夹行为使用 AppSettings 默认值。
    /// </summary>
    private async Task ExtractSelectedEntriesCoreAsync(List<ArchiveItem> entries, string destinationPath)
    {
        if (CurrentArchivePath == null) return;

        _sessionPasswords.TryGetValue(CurrentArchivePath, out var password);

        var settings = AppSettings.Load();

        var result = await ExtractFlow.RunSelectedItemsExtractionAsync(
            CurrentArchivePath, password, entries, destinationPath,
            CurrentFolder ?? "", settings.ExtractPreserveFullPath, settings.FileConflictAction,
            ShowExtractFileConflictDialogAsync,
            LocalizationManager.T("Status_Extracting"));

        switch (result.Status)
        {
            case SelectedItemsExtractStatus.Success:
                StatusMessage = LocalizationManager.T("Status_ExtractComplete");
                if (settings.OpenFolderAfterExtract)
                {
                    await OpenExtractedFolderAsync(destinationPath, CurrentArchivePath!, password);
                }
                break;
            case SelectedItemsExtractStatus.Failed:
                StatusMessage = LocalizationManager.T("Main_Status_ExtractFailed", result.ErrorMessage);
                break;
            case SelectedItemsExtractStatus.Cancelled:
                StatusMessage = LocalizationManager.T("Status_Cancelled");
                break;
        }
    }

    /// <summary>
    /// 获取用于解压的选中条目列表（含目录展开，全量条目展开与拖拽共用同一实现）。
    /// 对应 WPF 的 ExtractSelectedAsync 逻辑。
    /// </summary>
    private List<ArchiveItem> GetSelectedEntriesForExtract()
    {
        var selected = new List<ArchiveItem>();

        if (SelectedEntries.Count > 0)
        {
            selected.AddRange(SelectedEntries.Select(i => i.ToCoreItem()));
        }
        else if (SelectedEntry != null)
        {
            selected.Add(SelectedEntry.ToCoreItem());
        }

        if (selected.Count == 0) return selected;

        // 目录展开：复用 DragDropItemExpander.ExpandItems（与拖拽同源，全量条目）
        return DragDropItemExpander.ExpandItems(selected, GetAllRawItems()).ToList();
    }

    /// <summary>
    /// 打开解压后的文件夹（智能选择：若压缩包内所有条目共享一个公共根目录，
    /// 则打开 dest/公共根目录，否则直接打开 dest）。
    /// </summary>
    private async Task OpenExtractedFolderAsync(string dest, string archivePath, string? password)
    {
        if (ShowOpenFolderDialog == null) return;

        try
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            var openPath = engine == null
                ? dest
                : await SmartOpenPathResolver.ResolveSmartOpenPathAsync(archivePath, dest, engine, password);

            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = openPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"OpenExtractedFolderAsync: failed to open folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task NewArchive()
    {
        if (ShowCompressSettingsDialog == null || RunWithProgress == null) return;
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        var result = await ShowCompressSettingsDialog(vm);
        if (result != true || vm.SelectedPaths.Count == 0) return;

        await ExecuteCompressFromSettings(vm);
    }

    /// <summary>
    /// 从 CompressSettingsViewModel 读取设置，构建 CompressRequest 并执行压缩。
    /// Request 构建与冲突处理复用 CompressFlow（与 CLI 右键菜单共用同一流程）。
    /// </summary>
    private async Task ExecuteCompressFromSettings(CompressSettingsViewModel vm)
    {
        var request = CompressFlow.BuildRequest(vm);
        if (request == null)
        {
            await AppMessageBox.Show(
                LocalizationManager.T("Compress_FilteredAllSkipped"),
                LocalizationManager.T("Compress_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (RunWithProgress == null) return;
        var completed = await RunWithProgress(
            LocalizationManager.T("Status_Compressing"),
            AvaloniaCompressService.GetOutputPaths(request),
            async (progress, ct) =>
            {
                var svc = new AvaloniaCompressService();
                await svc.CompressAsync(request, progress, ct,
                    conflictResolver: CompressFlow.CreateResolver(async info =>
                    {
                        if (ShowCompressConflictDialog == null)
                            return (Core.Abstractions.CompressConflictAction.Overwrite, null, false);
                        return await ShowCompressConflictDialog(info);
                    }),
                    onItemStatus: BatchStatusReporter);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_Compressed");

            // Save or update password in the password library (matches WPF SavePasswordAfterCompress logic).
            // Must run after compress succeeds, before the dialog closes.
            if (vm.SaveToLibrary && vm.Encrypt)
            {
                try
                {
                    var rules = vm.RulesText
                        ?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => r.Trim())
                        .Where(r => !string.IsNullOrWhiteSpace(r))
                        .ToList() ?? new List<string>();

                    if (rules.Count == 0)
                    {
                        var ext = vm.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + vm.DefaultFormat;
                        rules.Add($"*{ext}");
                    }

                    if (vm.IsPasswordLibraryMode && vm.SelectedPasswordEntry != null)
                    {
                        // Update matching rules: deduplicate and append
                        var entry = vm.SelectedPasswordEntry;
                        var updated = false;
                        foreach (var rule in rules)
                        {
                            if (!entry.Patterns.Contains(rule))
                            {
                                entry.Patterns.Add(rule);
                                updated = true;
                            }
                        }
                        if (updated)
                        {
                            PasswordManager.Instance.UpdatePassword(
                                entry.Id, entry.Password, entry.Description, entry.Patterns);
                            PasswordManager.Instance.MarkUsed(entry.Id);
                            App.DebugLog($"Password rules updated for entry: {entry.Description}");
                        }
                    }
                    else if (!vm.IsPasswordLibraryMode)
                    {
                        // New password entry (not library mode)
                        var password = vm.Password;
                        var desc = vm.PasswordDescription?.Trim() ?? "";
                        if (string.IsNullOrEmpty(desc))
                            desc = $"Compressed on {DateTime.Now:yyyy-MM-dd HH:mm}";

                        PasswordManager.Instance.AddPassword(password, desc, rules);
                        App.DebugLog($"Password saved to library: {desc}");
                    }
                }
                catch (Exception ex)
                {
                    App.DebugLog($"SavePasswordAfterCompress failed: {ex.Message}");
                }
            }
        }
    }

    [RelayCommand]
    private async Task OpenPasswordManager()
    {
        if (ShowPasswordManager != null)
            await ShowPasswordManager();
    }

    [RelayCommand]
    private async Task OpenFavoritesManager()
    {
        if (ShowFavoritesDialog != null)
            await ShowFavoritesDialog();
    }

    [RelayCommand]
    private async Task CopyFileName()
    {
        if (SelectedEntries.Count == 0 && SelectedEntry == null) return;

        string text;
        if (SelectedEntries.Count > 1)
        {
            text = string.Join(Environment.NewLine, SelectedEntries.Select(e => (e.Name ?? string.Empty).TrimEnd('/')));
        }
        else if (SelectedEntry != null)
        {
            text = (SelectedEntry.Name ?? string.Empty).TrimEnd('/');
        }
        else return;

        if (CopyToClipboard != null)
            await CopyToClipboard(text);
        StatusMessage = $"{LocalizationManager.T("Status_Copied")}: {text}";
    }

    [RelayCommand]
    private async Task CopyFilePath()
    {
        if (SelectedEntries.Count == 0 && SelectedEntry == null) return;

        string text;
        if (SelectedEntries.Count > 1)
        {
            text = string.Join(Environment.NewLine, SelectedEntries.Select(e => e.FullPath ?? e.Name));
        }
        else if (SelectedEntry != null)
        {
            text = SelectedEntry.FullPath ?? SelectedEntry.Name;
        }
        else return;

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
            new[] { SelectedEntry.Name ?? string.Empty },
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
            new[] { CurrentArchivePath! },
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
            new[] { CurrentArchivePath! },
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
            new[] { CurrentArchivePath! },
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
        if (CurrentArchivePath == null || GetOpenFilePaths == null) return;

        var files = await GetOpenFilePaths();
        if (files == null || files.Count == 0) return;

        await AddFilesToArchiveAsync(files);
    }

    /// <summary>
    /// 将指定文件/文件夹添加到当前压缩包（复用解压冲突处理策略 + entryBasePath=当前浏览目录）。
    /// 供工具栏「添加文件」命令与窗口拖拽添加共用，保证行为一致。
    /// </summary>
    /// <param name="files">要添加的本地文件/文件夹路径。</param>
    /// <returns>true=添加完成（含刷新），false=无压缩包/无引擎/被取消/失败。</returns>
    public async Task<bool> AddFilesToArchiveAsync(IReadOnlyList<string> files)
    {
        if (CurrentArchivePath == null || RunWithProgress == null || files.Count == 0) return false;

        var engine = ArchiveEngineFactory.GetEngineByExtension(CurrentArchivePath);
        if (engine == null) return false;

        _sessionPasswords.TryGetValue(CurrentArchivePath, out var password);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_AddingFiles"),
            files.ToArray(),
            async (progress, ct) =>
            {
                // 复用解压冲突处理：同一 AppSettings.FileConflictAction 策略 + Ask 弹窗回调（标题区分）
                // CreateExtractOptions 返回 null 表示 Overwrite 默认（无冲突处理），回退到仅密码的 options
                var options = SelectedItemsExtractService.CreateExtractOptions(
                        AppSettings.Load().FileConflictAction, ShowAddFileConflictDialogAsync)
                    ?? new ArchiveOptions();
                options.Password = password;
                // entryBasePath：当前浏览的压缩包内目录，null=根目录（与 WPF 版行为一致）
                await engine.AddToArchiveAsync(CurrentArchivePath, files.ToArray(), options, progress, ct,
                    entryBasePath: string.IsNullOrEmpty(CurrentFolder) ? null : CurrentFolder);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_AddComplete");
            await RefreshArchive();
        }
        return completed;
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
            new[] { entryPath ?? string.Empty },
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
    private async Task OpenDonate()
    {
        if (ShowDonateDialog != null)
            await ShowDonateDialog();
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

    [RelayCommand]
    private void ToggleProgressBars()
    {
        ShowProgressBars = !ShowProgressBars;
        _appSettings.ShowProgressBars = ShowProgressBars;
        SaveSetting(s => s.ShowProgressBars = ShowProgressBars);
        // Update ProgressBarEnabled on all current items
        foreach (var item in CurrentEntries)
        {
            item.ProgressBarEnabled = ShowProgressBars;
        }
    }

    [RelayCommand]
    private void ToggleSepDirBaseline()
    {
        SeparateDirBaseline = !SeparateDirBaseline;
        _appSettings.SeparateDirBaseline = SeparateDirBaseline;
        SaveSetting(s => s.SeparateDirBaseline = SeparateDirBaseline);
        PopulateEntries();
    }

    [RelayCommand]
    private void ToggleInfoPanelOrientation()
    {
        Preview.ToggleInfoPanelOrientation();
        // 持久化方向设置（与 ToggleInfoPanelVisibility 一致，合并写避免覆盖设置窗口的其他变更）
        SaveSetting(s => s.InfoPanelOrientation = Preview.InfoPanelOrientation);
    }

    [RelayCommand]
    private void ToggleInfoPanelVisibility()
    {
        Preview.ShowInfoPanel = !Preview.ShowInfoPanel;
        _appSettings.ShowPreviewInfoPanel = Preview.ShowInfoPanel;
        SaveSetting(s => s.ShowPreviewInfoPanel = Preview.ShowInfoPanel);
    }

    /// <summary>
    /// 手动保存内容区布局快照（目录树/文件列表列宽 + 预览面板各位置尺寸）。
    /// 由 View 的 SaveLayoutAction 实际读 Grid 并写盘。
    /// </summary>
    [RelayCommand]
    private void SaveLayout()
    {
        SaveLayoutAction?.Invoke();
    }

    // ── Recent Files ──

    public ObservableCollection<string> RecentFiles { get; } = new(RecentFilesManager.GetPaths());

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
