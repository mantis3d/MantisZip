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
            StatusMessage = $"不支持的文件格式: {Path.GetExtension(path)}";
            return;
        }

        IsLoading = true;
        StatusMessage = "正在加载压缩包...";
        ClearArchiveInternal();

        try
        {
            var result = await _archiveService.LoadArchiveAsync(path);

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
                StatusMessage = $"已加载 {result.Entries.Count} 个条目";
                Title = $"MantisZip - {Path.GetFileName(path)}";
            }
            else if (result.IsPasswordRequired)
            {
                StatusMessage = "此压缩包已加密，请输入密码（Phase 0 暂不支持密码）";
            }
            else if (result.IsCancelled)
            {
                StatusMessage = "已取消";
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "无法打开压缩包";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ShowPreviewAsync(ArchiveItemModel entry)
    {
        try
        {
            var ext = Path.GetExtension(entry.Name);
            var previewType = PreviewService.ClassifyPreview(ext);

            if (previewType == PreviewType.Unsupported)
            {
                Preview.ShowUnsupported();
                StatusMessage = $"暂不支持预览 {ext} 文件";
                return;
            }

            if (CurrentArchivePath == null) return;

            StatusMessage = "正在提取文件...";

            var tempFile = await PreviewService.ExtractToTempAsync(
                CurrentArchivePath, entry, _currentFormat);

            if (tempFile == null)
            {
                Preview.ShowUnsupported("提取文件失败");
                return;
            }

            switch (previewType)
            {
                case PreviewType.Text:
                    Preview.ShowText(tempFile);
                    StatusMessage = $"文本预览: {entry.DisplayName}";
                    break;
                case PreviewType.Csv:
                    Preview.ShowCsv(tempFile);
                    StatusMessage = $"CSV 预览: {entry.DisplayName}";
                    break;
                case PreviewType.Pe:
                    Preview.ShowPe(tempFile);
                    StatusMessage = $"PE 元数据: {entry.DisplayName}";
                    break;
                case PreviewType.Image:
                    Preview.ShowImage(tempFile);
                    StatusMessage = $"图片预览: {entry.DisplayName}";
                    break;
                case PreviewType.Gif:
                    Preview.ShowGif(tempFile);
                    StatusMessage = $"GIF 预览: {entry.DisplayName}";
                    break;
                case PreviewType.Svg:
                    Preview.ShowSvg(tempFile);
                    StatusMessage = $"SVG 预览: {entry.DisplayName}";
                    break;
                case PreviewType.Font:
                    Preview.ShowFont(tempFile);
                    StatusMessage = $"字体预览: {entry.DisplayName}";
                    break;
                case PreviewType.Audio:
                    Preview.ShowAudio(tempFile);
                    StatusMessage = $"音频信息: {entry.DisplayName}";
                    break;
                case PreviewType.Sqlite:
                    Preview.ShowSqlitePreview(tempFile);
                    StatusMessage = $"SQLite 数据库: {entry.DisplayName}";
                    break;
                case PreviewType.Iso:
                    Preview.ShowIso(tempFile);
                    StatusMessage = $"ISO 镜像: {entry.DisplayName}";
                    break;
                case PreviewType.Torrent:
                    Preview.ShowTorrent(tempFile);
                    StatusMessage = $"种子信息: {entry.DisplayName}";
                    break;
                case PreviewType.Office:
                    Preview.ShowOffice(tempFile);
                    StatusMessage = $"Office 文档: {entry.DisplayName}";
                    break;
                case PreviewType.Video:
                    Preview.ShowVideo(tempFile);
                    StatusMessage = $"视频信息: {entry.DisplayName}";
                    break;
                case PreviewType.Html:
                case PreviewType.Markdown:
                    Preview.ShowUnsupported($"暂不支持预览 {(previewType == PreviewType.Html ? "HTML" : "Markdown")} 文件");
                    StatusMessage = $"暂不支持预览 {ext} 文件";
                    break;
            }
        }
        catch (Exception ex)
        {
            Preview.ShowUnsupported($"预览失败: {ex.Message}");
            StatusMessage = $"预览失败: {ex.Message}";
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
