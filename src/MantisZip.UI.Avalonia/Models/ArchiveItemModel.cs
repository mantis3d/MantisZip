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
    [NotifyPropertyChangedFor(nameof(SizeDisplay))]
    [NotifyPropertyChangedFor(nameof(CompressionRatio))]
    private long _size;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressedSizeDisplay))]
    [NotifyPropertyChangedFor(nameof(CompressionRatio))]
    private long _compressedSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastModifiedDisplay))]
    private DateTime _lastModified;

    [ObservableProperty]
    private bool _isDirectory;

    /// <summary>
    /// 当前压缩格式是否能提供逐项压缩后大小。
    /// false（如 7z/RAR/.tgz/.gz）时，文件与目录的压缩后大小列均显示空。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressedSizeDisplay))]
    private bool _compressedSizeAvailable = true;

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

    /// <summary>大小显示：始终格式化，0 显示 "0 B"（文件/目录一致）。</summary>
    public string SizeDisplay => FormatSize(Size);

    /// <summary>
    /// 压缩后大小显示：格式无法提供逐项压缩后大小时（如 7z/RAR/.tgz/.gz）显示空，否则格式化。
    /// </summary>
    public string CompressedSizeDisplay => CompressedSizeAvailable ? FormatSize(CompressedSize) : "";

    /// <summary>日期显示：MinValue 显示空，否则格式化为 "yyyy-MM-dd HH:mm:ss"。</summary>
    public string LastModifiedDisplay =>
        LastModified > DateTime.MinValue ? LastModified.ToString("yyyy-MM-dd HH:mm:ss") : "";

    /// <summary>
    /// 压缩率（0–100 的百分比数值）。Size<=0 时返回 0。
    /// 派生自 <see cref="Size"/> / <see cref="CompressedSize"/>，聚合后自动重算。
    /// </summary>
    public double CompressionRatio => Size > 0
        ? Math.Round((double)CompressedSize / Size * 100, 1)
        : 0;

    /// <summary>
    /// 压缩率显示文本（如 "75.0%"）。
    /// Size=0 或格式无法提供逐项压缩后大小时（<see cref="CompressedSizeAvailable"/> 为 false）返回空；
    /// 目录与文件一视同仁（目录显示聚合压缩率）。
    /// </summary>
    public string RatioDisplay
    {
        get
        {
            if (Size == 0 || !CompressedSizeAvailable) return "";
            if (CompressedSize == 0) return "0.0%";
            if (CompressedSize >= Size) return "100.0%";
            return $"{CompressionRatio:F1}%";
        }
    }

    /// <summary>
    /// 压缩率排序值（0–1）。目录返回 -1 以便排在最后。
    /// </summary>
    public double RatioSort
    {
        get
        {
            if (IsDirectory || Size <= 0) return -1;
            if (CompressedSize <= 0) return 0;
            return Math.Min((double)CompressedSize / Size, 1.0);
        }
    }

    public static ArchiveItemModel FromCore(ArchiveItem item)
    {
        return new ArchiveItemModel
        {
            Name = item.Name,
            DisplayName = item.DisplayName,
            FullPath = item.FullPath,
            Size = item.Size,
            CompressedSize = item.CompressedSize,
            LastModified = item.LastModified,
            IsDirectory = item.IsDirectory
        };
    }

    /// <summary>
    /// Convert to Core ArchiveItem for drag-drop and service operations.
    /// </summary>
    public ArchiveItem ToCoreItem()
    {
        return new ArchiveItem
        {
            FullPath = FullPath,
            Name = Name,
            Size = Size,
            CompressedSize = CompressedSize,
            LastModified = LastModified,
            IsDirectory = IsDirectory,
            IsEncrypted = IsDirectory ? false : false
        };
    }

    private static string FormatSize(long bytes) => FormatUtil.FormatSize(bytes);
}
