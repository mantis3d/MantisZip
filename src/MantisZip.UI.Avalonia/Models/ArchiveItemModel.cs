using CommunityToolkit.Mvvm.ComponentModel;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.Models;

public partial class ArchiveItemModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private long _size;

    [ObservableProperty]
    private string _sizeDisplay = string.Empty;

    [ObservableProperty]
    private long _compressedSize;

    [ObservableProperty]
    private string _compressedSizeDisplay = string.Empty;

    [ObservableProperty]
    private DateTime _lastModified;

    [ObservableProperty]
    private string _lastModifiedDisplay = string.Empty;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private double _compressionRatio;

    /// <summary>
    /// 排序用：目录为 0，文件为 1（实现目录优先排序）。
    /// </summary>
    public int SortOrder => IsDirectory ? 0 : 1;

    public static ArchiveItemModel FromCore(ArchiveItem item)
    {
        return new ArchiveItemModel
        {
            Name = item.Name,
            DisplayName = item.DisplayName,
            FullPath = item.FullPath,
            Size = item.Size,
            SizeDisplay = FormatSize(item.Size),
            CompressedSize = item.CompressedSize,
            CompressedSizeDisplay = FormatSize(item.CompressedSize),
            LastModified = item.LastModified,
            LastModifiedDisplay = item.LastModified.ToString("yyyy-MM-dd HH:mm:ss"),
            IsDirectory = item.IsDirectory,
            CompressionRatio = item.Size > 0
                ? Math.Round((double)item.CompressedSize / item.Size * 100, 1)
                : 0
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;
        var size = (double)bytes;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{size:F2} {units[unitIndex]}";
    }
}
