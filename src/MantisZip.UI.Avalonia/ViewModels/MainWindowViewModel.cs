using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using System.Collections.ObjectModel;

namespace MantisZip.UI.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ArchiveService _archiveService = new();
    private ArchiveFormat _currentFormat;

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
            }
        }
        catch (Exception ex)
        {
            Preview.ShowUnsupported($"预览失败: {ex.Message}");
            StatusMessage = $"预览失败: {ex.Message}";
        }
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
        CurrentArchivePath = null;
        IsArchiveLoaded = false;
        SelectedEntry = null;
        Preview.Clear();
    }
}
