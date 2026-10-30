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
    /// 由 View 设置的「查看已匹配密码」对话框回调。参数为密码条目与压缩包文件名（仅展示用），无返回值。
    /// </summary>
    public Func<Core.PasswordEntry, string, Task>? ShowMatchedPasswordDialog { get; set; }

    /// <summary>
    /// 解压设置对话框回调。参数为待解压的压缩包路径；确认后的执行由回调内部完成
    /// （设置窗口产出 <see cref="Models.ConfirmOutcome{T}"/> 载荷 → 注入的解压会话执行 → 结局分支），
    /// 命令本身不再回读对话框状态。
    /// </summary>
    public Func<IReadOnlyList<string>, Task>? ShowExtractSettingsDialog { get; set; }

    /// <summary>
    /// 解压目标文件夹选择回调。传入待解压条目、初始路径、当前浏览目录（压缩包内）与保留完整路径设置，
    /// 返回所选结果（目标目录 <c>DestPath</c> + 对话框内「保留完整路径」勾选值 <c>PreserveFullPath</c>），取消返回 null。
    /// </summary>
    /// <summary>条目选择入口（右键「解压到」/ 拖拽兜底）：返回 true = 已确认并执行，false = 用户取消。</summary>
    public Func<IReadOnlyList<ArchiveItem>, string?, string, bool, string?, Task<bool>>? ShowExtractEntryPicker { get; set; }

    /// <summary>
    /// 压缩设置对话框回调。参数为预填的源路径；确认后的执行由回调内部完成
    /// （设置窗口产出 <see cref="Models.ConfirmOutcome{T}"/> 载荷 → 会话执行 → 结局分支），
    /// 命令本身不再回读对话框状态。
    /// </summary>
    public Func<IReadOnlyList<string>, Task>? ShowCompressSettingsDialog { get; set; }

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
    /// 下一次 <see cref="RunWithProgress"/> 的操作说明行文案（读取后立即复位，null = 不显示）。
    /// 用于「整包重写、无逐条目回调」的操作（如删除）：进度窗显示一行真实影响面，
    /// 而不是播种 N 行永远停在 ⏳ 的假进度。
    /// </summary>
    public string? PendingOperationNote { get; set; }

    /// <summary>测试流标记：传给进度窗口自动按下 📌（D10 触发点 3，完成后不自动关闭）。</summary>
    public bool PendingTestFlow { get; set; }

    /// <summary>完成回调：RunWithProgress 在关窗决策前把最终状态写进进度窗口（spec §9.4 测试结果通道）。</summary>
    public Action<ProgressWindow>? PendingCompletedCallback { get; set; }

    /// <summary>最近一次 RunWithProgress 创建的进度窗口（后置开关消费点回读最终值用，spec §11.3）。</summary>
    public ProgressWindow? LastProgressWindow { get; set; }

    /// <summary>待声明的解压后置开关能力位 + 对话框初值（RunWithProgress 消费，默认 false/null，spec §11.2）。</summary>
    public (bool OpenFolder, bool DeleteArchive, bool? OpenFolderSeed)? PendingExtractPostActions { get; set; }

    /// <summary>
    /// 由 View 设置的批处理状态上报回调（索引 + 状态）。
    /// 操作闭包内可将其作为 onItemStatus 传给引擎，实时更新批处理列表项状态。
    /// </summary>
    public Action<int, BatchItemStatus>? BatchStatusReporter { get; set; }

    /// <summary>
    /// 由 View 设置的批处理输出路径上报回调（索引 + 冲突解决后的最终输出路径）。
    /// 操作闭包内可将其作为 onItemOutputPath 传给引擎：自动重命名/手输包名后，
    /// 进度窗口顶部的「压缩包列表」据此把旧包名改成实际写出的包名。
    /// </summary>
    public Action<int, string>? BatchOutputPathReporter { get; set; }

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
            "zh-TW" => AppLanguage.TraditionalChinese,
            "en" => AppLanguage.English,
            // 未知参数时在三种语言间循环
            _ => LocalizationManager.CurrentLanguage switch
            {
                AppLanguage.Chinese => AppLanguage.English,
                AppLanguage.English => AppLanguage.TraditionalChinese,
                _ => AppLanguage.Chinese,
            },
        };
        CurrentLanguage = LocalizationManager.CurrentLanguageCode;

        // Persist so the selection survives restart (AppSettings uses "zh"/"en"/"zh-TW")
        _appSettings.Language = LocalizationManager.ToSettingsCode(LocalizationManager.CurrentLanguage);
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
        // 工具栏密码按钮 tooltip 跟随语言切换（三态文案）
        UpdateToolbarPasswordState();

        var newDict = new Dictionary<string, string>();
        var keys = new[]
        {
            "Menu_File", "Menu_OpenArchive", "Menu_CloseArchive", "Menu_OpenArchiveLocation", "Menu_Refresh", "Menu_Settings", "Menu_Exit",
            "Menu_Edit", "Menu_View", "Menu_ToggleTheme", "Menu_Language", "Menu_LangChinese", "Menu_LangTraditionalChinese", "Menu_LangEnglish",
            "Menu_Help", "Menu_Tools",
            "Menu_ExtractArchive", "Menu_ExtractHere", "Menu_ExtractToName",
            "Menu_NewArchive", "Menu_PasswordManager", "Menu_About", "Menu_Donate",
            "Toolbar_Open", "Toolbar_Extract", "Toolbar_Compress",
            "Toolbar_Filter", "Toolbar_Preview",
            "Menu_Toolbar",
            "Menu_DataBars", "Menu_SepDirBaseline", "Menu_InfoPanelOrientation", "Menu_ShowInfoPanel",
            "Menu_ShowPreviewPanel", "Menu_SaveLayout",
            "Filter_Search", "Filter_Exclude", "Filter_DateFrom", "Filter_DateTo",
            "Filter_SizeMin", "Filter_SizeMax", "Filter_ShowSubfolders", "Filter_ClearAll",
            "Filter_PickDateFrom", "Filter_PickDateTo", "Filter_PickSizeMin", "Filter_PickSizeMax",
            "Filter_MatchModeSubstring", "Filter_MatchModeWildcard",
            "Status_Selected",
            "Tree_Browse",
            "DataGrid_Name", "DataGrid_Size", "DataGrid_Compressed", "DataGrid_Modified", "DataGrid_Ratio",
            "App_Title",
            "Main_DropHint",
            "Main_ArchiveUnavailable_Reload", "Main_ArchiveUnavailable_Close",
            "Ctx_Extract", "Ctx_ExtractSelectedHere", "Ctx_ExtractSelectedTo", "Ctx_SmartExtract", "Ctx_ExtractTo",
            "Ctx_CopyName", "Ctx_CopyPath", "Ctx_Test", "Ctx_Delete",
            "Menu_SmartExtract", "Menu_TestArchive", "Menu_AddFiles", "Menu_DeleteFiles", "Menu_ArchiveComment",
            "Toolbar_SmartExtract", "Toolbar_Test", "Toolbar_AddFiles", "Toolbar_DeleteFiles","Toolbar_ExtractSelected","Toolbar_ExtractArchive",
            "Tooltip_Open", "Tooltip_Extract", "Tooltip_ExtractSelectedHere", "Tooltip_ExtractSelectedTo", "Tooltip_NewArchive",
            "Tooltip_Filter", "Tooltip_Preview", "Tooltip_SmartExtract", "Tooltip_Test",
            "Tooltip_AddFiles", "Tooltip_DeleteFiles", "Tooltip_Subfolders",
            "Preview_Result_LoadingMore",
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
            "Toolbar_Password",
            "Menu_Test",
            "Tree_ExpandAll", "Tree_CollapseAll", "Tree_ExpandToCurrent", "Tree_AutoExpand", "Tree_Filter",
            "Nav_GoRoot", "Nav_GoBack", "Nav_GoForward", "Nav_GoUp",
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

    /// <summary>预览面板控件树需要就绪（首次预览请求兜底触发；View 侧订阅 EnsurePreviewPanel）。</summary>
    public event Action? PreviewPanelNeeded;

    [ObservableProperty]
    private string _title = "MantisZip";

    [ObservableProperty]
    private string? _currentArchivePath;

    partial void OnCurrentArchivePathChanged(string? value)
    {
        // 同步预览面板当前压缩包路径，供「输入密码」按钮的 CanExecute 判断使用
        Preview.CurrentPreviewFilePath = value;
        // 换包后引擎能力位可能变化（zip ↔ tar），一并刷新删除入口可用性
        OnPropertyChanged(nameof(CanDeleteEntries));
    }

    [ObservableProperty]
    private bool _isArchiveLoaded;

    /// <summary>
    /// 当前压缩包是否支持删除条目（TAR/GZ 等只读格式为 false）。
    /// 三处删除入口（工具栏 / 编辑菜单 / 右键菜单）据此禁用，
    /// 避免用户点了才吃 <c>NotSupportedException</c>。
    /// </summary>
    public bool CanDeleteEntries =>
        IsArchiveLoaded
        && !IsArchiveUnavailable
        && CurrentArchivePath != null
        && (ArchiveEngineFactory.GetEngineByExtension(CurrentArchivePath)?.CanDelete(_currentFormat) ?? false);

    // 能力位随「压缩包加载状态」刷新（格式 `_currentFormat` 在 RefreshArchive 中先于本字段赋值）
    partial void OnIsArchiveLoadedChanged(bool value) => OnPropertyChanged(nameof(CanDeleteEntries));

    // ── 当前压缩包在磁盘上的可用性（打开中被删除 / 被替换） ─────────────────────────────
    //
    // 背景（2026-10-30）：解压时勾选「解压完成后删除压缩包」后，主窗口仍保持「压缩包已打开」，
    // 之后所有读写入口都落到一个已不存在的路径；被别的软件删除、杀毒隔离、网盘同步搬走同理。
    // 由 ArchiveFileGuard 监视磁盘，这里只负责把状态映射成界面（只读横幅 + 入口拦截）。

    /// <summary>当前压缩包的磁盘可用性（见 <see cref="ArchiveFileGuard"/>）。</summary>
    [ObservableProperty]
    private ArchiveAvailability _archiveAvailability = ArchiveAvailability.Available;

    partial void OnArchiveAvailabilityChanged(ArchiveAvailability value) => NotifyArchiveAvailabilityProperties();

    /// <summary>压缩包已不在磁盘上（含被替换）——界面进入只读提示态，所有读写入口预检都会拦下。</summary>
    public bool IsArchiveUnavailable =>
        IsArchiveLoaded && ArchiveAvailability is ArchiveAvailability.Missing or ArchiveAvailability.Replaced;

    /// <summary>是「被替换」而不是「被删除」（决定横幅文案）。</summary>
    public bool IsArchiveReplaced =>
        IsArchiveLoaded && ArchiveAvailability == ArchiveAvailability.Replaced;

    /// <summary>只读横幅标题。</summary>
    public string ArchiveAvailabilityTitleText => LocalizationManager.T(
        IsArchiveReplaced ? "Main_ArchiveReplaced_Title" : "Main_ArchiveMissing_Title");

    /// <summary>只读横幅说明（带当前路径，便于用户确认是哪一个包）。</summary>
    public string ArchiveAvailabilityDetailText => LocalizationManager.T(
        IsArchiveReplaced ? "Main_ArchiveReplaced_Detail" : "Main_ArchiveMissing_Detail",
        CurrentArchivePath ?? string.Empty);

    private void NotifyArchiveAvailabilityProperties()
    {
        OnPropertyChanged(nameof(IsArchiveUnavailable));
        OnPropertyChanged(nameof(IsArchiveReplaced));
        OnPropertyChanged(nameof(ArchiveAvailabilityTitleText));
        OnPropertyChanged(nameof(ArchiveAvailabilityDetailText));
        OnPropertyChanged(nameof(CanDeleteEntries));
    }

    private ArchiveFileGuard? _archiveGuard;

    /// <summary>为刚打开的压缩包建立磁盘监视（会先释放上一份）。</summary>
    private void AttachArchiveGuard(string archivePath)
    {
        DetachArchiveGuard();
        ArchiveAvailability = ArchiveAvailability.Available;

        var guard = ArchiveFileGuard.TryCreate(archivePath);
        if (guard == null)
            return;

        guard.AvailabilityChanged += OnArchiveGuardAvailabilityChanged;
        _archiveGuard = guard;
    }

    private void DetachArchiveGuard()
    {
        if (_archiveGuard == null)
            return;

        _archiveGuard.AvailabilityChanged -= OnArchiveGuardAvailabilityChanged;
        _archiveGuard.Dispose();
        _archiveGuard = null;
    }

    private void OnArchiveGuardAvailabilityChanged(object? sender, ArchiveAvailability availability)
    {
        ArchiveAvailability = availability;

        if (availability == ArchiveAvailability.Missing)
            StatusMessage = LocalizationManager.T("Status_ArchiveMissing");
        else if (availability == ArchiveAvailability.Replaced)
            StatusMessage = LocalizationManager.T("Status_ArchiveReplaced");
    }

    /// <summary>
    /// 入口守卫：确认当前压缩包仍可操作。返回 false 时已经写好状态提示，调用方直接 return。
    /// </summary>
    public bool EnsureArchiveAvailable()
    {
        if (!IsArchiveLoaded || CurrentArchivePath == null)
            return true;

        _archiveGuard?.Check();
        if (ArchiveAvailability is ArchiveAvailability.Missing or ArchiveAvailability.Replaced)
            return false;

        // watcher 可能没建起来（分卷被删 / 目录一开始就不存在），这里再兜一次。
        if (File.Exists(CurrentArchivePath))
            return true;

        ArchiveAvailability = ArchiveAvailability.Missing;
        StatusMessage = LocalizationManager.T("Status_ArchiveMissing");
        return false;
    }

    /// <summary>
    /// 解压收尾后核对源包：本次把源包删掉了就直接关闭压缩包，避免界面继续停留在已不存在的包上。
    /// </summary>
    public void SyncArchiveStateAfterExtract()
    {
        if (!IsArchiveLoaded || CurrentArchivePath == null)
            return;

        if (File.Exists(CurrentArchivePath))
        {
            _archiveGuard?.Check();
            return;
        }

        CloseArchiveAfterRemoval();
    }

    /// <summary>关闭压缩包并提示「已移入回收站」（自己删包这条路径）。</summary>
    private void CloseArchiveAfterRemoval()
    {
        ClearArchiveInternal();
        Title = LocalizationManager.T("App_Title");
        StatusMessage = LocalizationManager.T("Status_ArchiveMovedToRecycleBin");
    }

    /// <summary>只读横幅上的「关闭压缩包」。</summary>
    [RelayCommand]
    private void DismissUnavailableArchive() => CloseArchiveAfterRemoval();

    /// <summary>只读横幅上的「重新打开」：文件回得来（回收站还原 / 网盘同步回来）就重载。</summary>
    [RelayCommand]
    private async Task ReopenUnavailableArchive()
    {
        if (CurrentArchivePath is null)
            return;

        if (!File.Exists(CurrentArchivePath))
        {
            StatusMessage = LocalizationManager.T("Status_ArchiveMissing");
            return;
        }

        await LoadArchiveAsync(CurrentArchivePath);
    }

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
        _sessionPasswords.TryGetValue(GetSessionPasswordKey(archivePath), out var pwd);
        return pwd;
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

    /// <summary>
    /// 是否显示文件列表的**数据条**（每行文字后那条表示相对占比的彩色横条）。
    /// 2026-10-22：用户界面文案已由「进度条」改名为「数据条 / Data Bars」；
    /// 属性名保持不变（对应 settings.json 的持久化键，改名会丢老用户设置）。
    /// </summary>
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

    /// <summary>
    /// 供 PreviewViewModel.PasswordEntered 回调：密码输入成功后重新触发当前条目的预览。
    /// </summary>
    public void RePreviewCurrentEntry()
    {
        if (SelectedEntry != null && CurrentArchivePath != null)
        {
            App.DebugLog("[PRV] RePreviewCurrentEntry: re-triggering preview after password entry");
            _ = ShowPreviewAsync(SelectedEntry);
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
    // 大小单位切换需立即重新过滤（对齐 WPF SizeMinUnit/SizeMaxUnit SelectionChanged → RefreshFilter）
    partial void OnFilterSizeUnitChanged(string value) => ApplyFilter();
    partial void OnShowSubfoldersChanged(bool value) => ApplyFilter();
    // 筛选栏显隐 = 筛选功能总开关：收起时停用筛选（条件保留），重新展开即自动恢复生效
    partial void OnIsFilterBarVisibleChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (_allRawItems == null || _isProgrammaticFilter) return;
        PopulateEntries();
    }

    /// <summary>
    /// 清除全部筛选条件（对齐 WPF ClearFiltersBtn_Click）：搜索词/排除词/日期起止/大小上下限清空，
    /// 单位下拉复位默认 KB、匹配模式复位子串。各属性 OnXxxChanged 自动触发重新过滤，列表恢复全量。
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        FilterText = null;
        FilterExcludeText = null;
        FilterDateFrom = null;
        FilterDateTo = null;
        FilterSizeMin = null;
        FilterSizeMax = null;
        FilterSizeUnit = "KB";
        SelectedMatchModeOption = MatchModeOptions.FirstOrDefault(o => o.Value == FilterMatchMode.Substring);
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
        // 分卷集只认第一卷（issue #70①）：拖入 .002 / .003 时回退到 .001；找不到首卷则按打不开处理。
        path = ArchiveEngineFactory.ResolveVolumeAnchor(path);

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
            var formatForCache = ArchiveFormatHelper.GetFormat(path);
            var cacheKey = GetSessionPasswordKey(path, formatForCache);
            if (_sessionPasswords.TryGetValue(cacheKey, out var cachedPwd))
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
                _sessionPasswords.Remove(cacheKey);
                password = null;
                result = await _archiveService.LoadArchiveAsync(path, null);
            }

            // ── 密码解析流程（对齐 WPF ResolvePasswordAsync + LoadArchiveAsync 组合语义）──
            // 场景 A（unlistable）：条目无法列出（如 EncryptHeaders=true 的 7z，ListEntriesAsync 抛密码异常）
            //   → 必须取得密码才能打开，用户取消则不打开（WPF 同样中止）。
            // 场景 B（hasEncryptedEntries）：条目可列出但含加密条目（ZIP 加密 / RAR / EncryptHeaders=false 的 7z，
            //   文件名可见）→ 先试密码库、无匹配弹对话框；用户取消仍打开仅浏览（未匹配状态），预览/解压按需失败。
            bool unlistable = result.IsPasswordRequired;
            bool hasEncryptedEntries = !unlistable && result.RawItems?.Any(i => i.IsEncrypted) == true;

            if (unlistable || hasEncryptedEntries)
            {
                _hasEncryptedArchive = true;

                if (password != null)
                {
                    // 会话缓存密码已在上方通过 QuickVerify → 直接视为已匹配
                    _sessionPasswords[cacheKey] = password;
                    _currentPassword = password;
                    var cachedEntry = FindSavedPasswordEntry(path, password);
                    _currentPasswordDescription = cachedEntry?.Description;
                    _currentPasswordPatterns = cachedEntry != null ? new List<string>(cachedEntry.Patterns) : null;
                    UpdatePasswordStatus(isMatched: true);
                }
                else
                {
                    // Phase A: 已保存密码静默自动匹配（内部含快速验证）
                    if (engine != null)
                    {
                        var match = _passwordService.TryMatchPasswordEx(path, engine);
                        if (match != null)
                        {
                            var (matchedPwd, matchedDesc, verifyInfo) = match.Value;

                            // 文件损坏：直接报告错误，不再尝试其他密码
                            if (verifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
                            {
                                App.DebugLog($"[PRV] Archive corrupted during auto-match: {verifyInfo.DetailMessage}");
                                StatusMessage = LocalizationManager.T("Status_ArchiveCorrupted");
                                IsLoading = false;
                                return;
                            }

                            password = matchedPwd;
                            if (unlistable)
                                result = await _archiveService.LoadArchiveAsync(path, password);
                            if (!result.IsPasswordRequired)
                            {
                                _sessionPasswords[cacheKey] = password;
                                _currentPassword = password;
                                var savedEntry = FindSavedPasswordEntry(path, password);
                                _currentPasswordDescription = savedEntry?.Description;
                                _currentPasswordPatterns = savedEntry != null ? new List<string>(savedEntry.Patterns) : null;
                                UpdatePasswordStatus(isMatched: true);
                            }
                        }
                    }

                    // Phase B: 密码对话框循环（无已保存匹配时弹出；错密码提示后重试直到正确或取消）
                    while (_currentPassword == null && (!unlistable || result.IsPasswordRequired))
                    {
                        if (engine == null)
                        {
                            StatusMessage = LocalizationManager.T("Status_UnsupportedFormat", path);
                            IsLoading = false;
                            return;
                        }

                        if (ShowPasswordDialog == null)
                        {
                            StatusMessage = LocalizationManager.T("Status_PasswordRequired");
                            IsLoading = false;
                            return;
                        }

                        var dialogResponse = await ShowPasswordDialog(path);
                        if (dialogResponse?.Password == null)
                            break; // 用户取消：不可列出→下方中止不打开；可列出→仅浏览打开

                        password = dialogResponse.Password;

                        // QuickVerify before full retry (fast path)
                        var verifyInfo = _passwordService.QuickVerifyPasswordEx(path, password, engine);
                        if (verifyInfo.Result == PasswordVerificationResult.WrongPassword)
                        {
                            StatusMessage = LocalizationManager.T("Status_WrongPassword");
                            continue;
                        }
                        if (verifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
                        {
                            StatusMessage = LocalizationManager.T("Status_ArchiveCorrupted");
                            IsLoading = false;
                            return;
                        }

                        // 用密码完整加载一次（可列出场景同样重载以统一加载状态；条目内容一致）
                        result = await _archiveService.LoadArchiveAsync(path, password);

                        if (!result.IsPasswordRequired)
                        {
                            // Success
                            _sessionPasswords[cacheKey] = password;
                            _currentPassword = password;
                            // 会话级记录描述/规则，供工具栏「查看已匹配密码」对话框展示
                            _currentPasswordDescription = dialogResponse.Description;
                            _currentPasswordPatterns = dialogResponse.Patterns is { Count: > 0 } ? dialogResponse.Patterns.ToList() : null;

                            if (dialogResponse.SavePermanently)
                            {
                                var saved = _passwordService.TrySavePassword(password, path,
                                    dialogResponse.Patterns, dialogResponse.Description);
                                App.DebugLog($"TrySavePassword (Phase B): savePermanently=true, result={saved}, path={path}");
                            }

                            UpdatePasswordStatus(isMatched: true);
                        }
                        else
                        {
                            StatusMessage = LocalizationManager.T("Status_WrongPassword");
                        }
                    }
                }

                // 收尾：仍未取得密码
                if (_currentPassword == null)
                {
                    if (unlistable)
                    {
                        // 无法列出条目且用户取消 → 不打开
                        StatusMessage = LocalizationManager.T("Status_PasswordCancelled");
                        IsLoading = false;
                        return;
                    }

                    // 可列出 → 仅浏览模式打开（未匹配：底部"已加密" + 工具栏红锁，可后续补输密码）
                    _currentPassword = null;
                    UpdatePasswordStatus(isMatched: false);
                }
            }
            else
            {
                _hasEncryptedArchive = false;
                _currentPassword = null;
                UpdatePasswordStatus(isMatched: false);
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
                // 打开成功后开始监视磁盘：包被外部删除 / 替换时界面要立刻知道（issue：解压删源包后状态残留）
                AttachArchiveGuard(path);
                RecentFilesManager.AddPath(path);
                RecentFiles.Clear();
                foreach (var rp in RecentFilesManager.GetPaths())
                    RecentFiles.Add(rp);
                // CurrentArchivePath 在密码解析流程之后才赋值，此处补一次工具栏密码按钮状态刷新
                UpdateToolbarPasswordState();
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
                // 打开失败（如压缩包损坏）：状态栏 + 模态弹窗双提示（对齐 WPF 行为）
                var errorMessage = result.ErrorMessage ?? LocalizationManager.T("Status_OpenArchiveFailedGeneric");
                StatusMessage = errorMessage;
                await AppMessageBox.Show(
                    errorMessage,
                    LocalizationManager.T("App_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"LoadArchiveAsync: unexpected failure: {ex.Message}");
            var errorMessage = LocalizationManager.T("Status_LoadFailed", ex.Message);
            StatusMessage = errorMessage;
            await AppMessageBox.Show(
                errorMessage,
                LocalizationManager.T("App_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
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
        // 关包即停监视（换包时 LoadArchiveAsync 会重新 Attach），并复位可用性
        DetachArchiveGuard();
        ArchiveAvailability = ArchiveAvailability.Available;
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
        _currentPasswordDescription = null;
        _currentPasswordPatterns = null;
        _hasEncryptedArchive = false;
        PasswordStatusMessage = null;
        PasswordStatusIcon = null;
        UpdateToolbarPasswordState();
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
        if (!EnsureArchiveAvailable()) return;

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
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            AppShutdown.Request(desktop.Shutdown);
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

        // 刷新数据条显示：开启 → 重算比例；关闭 → 比例清零。
        // 数据条 Rectangle 的宽度绑定这些比例（SizeRatio / CompressedSizeRatio /
        // DateRatio / RatioBarValue）→ 归零即隐藏、重算即显示。
        // 等价 WPF 版 ToggleProgressBars_Click 里的 FilterFiles(_currentFolder)
        // （重填条目 → 比例归零/重算），但这里保留当前条目与选中项，不重建集合。
        if (ShowProgressBars)
            ComputeProgressBarRatios();
        else
            ClearProgressBarRatios();

        // 同步每个条目的数据条开关（与 PopulateEntries 中的赋值保持一致）
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
