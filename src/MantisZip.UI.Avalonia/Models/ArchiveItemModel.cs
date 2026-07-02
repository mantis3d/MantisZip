using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Models;

public partial class ArchiveItemModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>显示名称：优先用 DisplayName，回退到 Name。</summary>
    public string NameDisplay => !string.IsNullOrEmpty(DisplayName) ? DisplayName : Name;

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

    [ObservableProperty]
    private Bitmap? _iconSource;

    [ObservableProperty]
    private double _sizeRatio;

    [ObservableProperty]
    private double _compressedSizeRatio;

    [ObservableProperty]
    private double _dateRatio;

    [ObservableProperty]
    private double _ratioBarValue;

    [ObservableProperty]
    private bool _progressBarEnabled = true;

    [ObservableProperty]
    private bool _useDirProgressColor;

    // Brush key properties for progress bar color switching
    public string SizeBarBrushKey => UseDirProgressColor ? "ProgressBarSizeDirBrush" : "ProgressBarSizeBrush";
    public string CompressedSizeBarBrushKey => UseDirProgressColor ? "ProgressBarCompressedSizeDirBrush" : "ProgressBarCompressedSizeBrush";
    public string RatioBarBrushKey => "ProgressBarRatioBrush";
    public string DateBarBrushKey => UseDirProgressColor ? "ProgressBarDateDirBrush" : "ProgressBarDateBrush";

    public bool HasIcon => IconSource != null;

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

    private static string FormatSize(long bytes) => FormatUtil.FormatSize(bytes);
}
