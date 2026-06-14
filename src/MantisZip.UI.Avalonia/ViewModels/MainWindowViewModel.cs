using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using System.Collections.ObjectModel;

namespace MantisZip.UI.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ArchiveService _archiveService = new();
    private ArchiveFormat _currentFormat;
    private IReadOnlyList<ArchiveItem>? _allRawItems;

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
    /// 会话密码缓存：压缩包路径 → 密码（仅内存，不持久化）。
    /// </summary>
    private readonly Dictionary<string, string> _sessionPasswords = new(StringComparer.OrdinalIgnoreCase);

    // ── i18n ──

    [ObservableProperty]
    private string _currentLanguage = LocalizationManager.CurrentLanguageCode;

    [ObservableProperty]
    private Dictionary<string, string> _localizedStrings = new();

    [RelayCommand]
    private void SwitchLanguage()
    {
        LocalizationManager.CurrentLanguage = LocalizationManager.CurrentLanguage == AppLanguage.Chinese
            ? AppLanguage.English
            : AppLanguage.Chinese;
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
            "Menu_File", "Menu_OpenArchive", "Menu_Settings", "Menu_Exit",
            "Menu_View", "Menu_ToggleTheme", "Menu_Language", "Menu_LangChinese", "Menu_LangEnglish",
            "Tree_Browse",
            "DataGrid_Name", "DataGrid_Size", "DataGrid_Compressed", "DataGrid_Modified",
            "App_Title"
        };
        foreach (var key in keys)
        {
            newDict[key] = LocalizationManager.T(key);
        }
        LocalizedStrings = newDict;
        OnPropertyChanged(nameof(LocalizedStrings));
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
                StatusMessage = LocalizationManager.T("Status_Loaded", result.Entries.Count);
                Title = $"{LocalizationManager.T("App_Title")} - {Path.GetFileName(path)}";
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
        // 切换文件前停止上一个 GIF 动画
        Preview.StopGifTimer();

        try
        {
            var ext = Path.GetExtension(entry.Name);
            var previewType = PreviewService.ClassifyPreview(ext);

            if (previewType == PreviewType.Unsupported)
            {
                Preview.ShowUnsupported();
                StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                return;
            }

            if (CurrentArchivePath == null) return;

            StatusMessage = LocalizationManager.T("Status_Extracting");

            var tempFile = await PreviewService.ExtractToTempAsync(
                CurrentArchivePath, entry, _currentFormat);

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
                    Preview.ShowImage(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Image", entry.DisplayName);
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
            Preview.ShowUnsupported(LocalizationManager.T("Status_PreviewFailed", ex.Message));
            StatusMessage = LocalizationManager.T("Status_PreviewFailed", ex.Message);
        }
    }

    private void NavigateToFolder(FolderNode node)
    {
        if (_allRawItems == null) return;

        CurrentFolder = node.FullPath;
        var filtered = ArchiveEntryLister.GetEntriesInFolder(_allRawItems, node.FullPath, showSubfolders: false);

        CurrentEntries.Clear();
        foreach (var item in filtered)
        {
            var model = ArchiveItemModel.FromCore(item);
            var ext = Path.GetExtension(model.Name);
            model.IconSource = IconService.GetFileIcon(ext);
            CurrentEntries.Add(model);
        }
    }

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
        Title = "MantisZip";
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
}
