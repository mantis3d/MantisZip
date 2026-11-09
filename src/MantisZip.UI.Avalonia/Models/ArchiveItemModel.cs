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

    /// <summary>条目是否加密（决定预览/双击时是否提示需要密码）。</summary>
    [ObservableProperty]
    private bool _isEncrypted;

    /// <summary>
    /// 当前压缩格式是否能提供逐项压缩后大小。
    /// false（如 7z/RAR/.tgz/.gz）时，文件与目录的压缩后大小列均显示空。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressedSizeDisplay))]
    private bool _compressedSizeAvailable = true;

    [ObservableProperty]
    private Bitmap? _iconSource;

    /// <summary>
    /// 卡片布局兜底用的**大尺寸**图标（Windows 上取自 Shell 图像列表的 256px 档）。
    /// null = 拿不到大图标（非 Windows / 该类型无大图），卡片退回 <see cref="IconSource"/> 原尺寸显示。
    /// </summary>
    [ObservableProperty]
    private Bitmap? _largeIconSource;

    /// <summary>
    /// 图像类内容卡的缩略图（Tile / Content 布局）。null = 无缩略图（卡片回退到扩展名图标）。
    /// </summary>
    [ObservableProperty]
    private Bitmap? _thumbnailSource;

    /// <summary>
    /// 文本类内容卡的摘录（Tile / Content 布局）。null = 无可展示文本（卡片回退到扩展名图标）。
    /// </summary>
    [ObservableProperty]
    private string? _textSnippet;


    /// <summary>
    /// 卡片布局（Tile / Content）里的选中视觉状态；详情布局用 DataGrid 自带的行选中，不读此属性。
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 主窗口列表**复选框列**的勾选状态（v0.5.6 `file-list-checkbox-selection`）。
    /// 仅详情布局的复选框列绑定该属性；预览树复用本模型但不绑它。
    /// 勾选是 <c>MainWindowViewModel.SelectedEntries</c> 的另一个入口，不构成第二份事实源。
    /// </summary>
    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private double _sizeRatio;

    [ObservableProperty]
    private double _compressedSizeRatio;

    [ObservableProperty]
    private double _dateRatio;

    [ObservableProperty]
    private double _ratioBarValue;

    /// <summary>
    /// 该条目的**数据条**是否启用（用户界面称「数据条 / Data Bars」；2026-10-22 由「进度条」改名）。
    /// </summary>
    [ObservableProperty]
    private bool _progressBarEnabled = true;

    [ObservableProperty]
    private bool _useDirProgressColor;

    // Brush key properties for progress bar color switching
    // 注（2026-10-22）：当前视图**未使用**这些键 —— 目录/文件换色改由
    // ArchiveContentView 里「双 Rectangle 叠加 + IsVisible="{Binding UseDirProgressColor}"」
    // 实现（保留 DynamicResource 随主题联动）。此处仅记录各列对应的资源键，
    // 供将来若改用「key → brush」方案时参考；勿误以为改这里就能换色。
    public string SizeBarBrushKey => UseDirProgressColor ? "ProgressBarSizeDirBrush" : "ProgressBarSizeBrush";
    public string CompressedSizeBarBrushKey => UseDirProgressColor ? "ProgressBarCompressedSizeDirBrush" : "ProgressBarCompressedSizeBrush";
    public string RatioBarBrushKey => "ProgressBarRatioBrush";
    public string DateBarBrushKey => UseDirProgressColor ? "ProgressBarDateDirBrush" : "ProgressBarDateBrush";

    public bool HasIcon => IconSource != null;

    /// <summary>
    /// 内容卡是否有内容可画（缩略图或文本摘录任一非空）。
    /// 派生属性通知集中在这两个源属性的 partial 方法里（AGENTS.md「派生属性通知」），
    /// 不要改成逐字段 <c>[NotifyPropertyChangedFor]</c>。
    /// </summary>
    public bool HasThumbnailContent => ThumbnailSource != null || !string.IsNullOrEmpty(TextSnippet);

    partial void OnThumbnailSourceChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasThumbnailContent));
        OnPropertyChanged(nameof(ShowLargeIcon));
        OnPropertyChanged(nameof(ShowSmallIcon));
    }

    partial void OnTextSnippetChanged(string? value)
    {
        OnPropertyChanged(nameof(HasThumbnailContent));
        OnPropertyChanged(nameof(ShowLargeIcon));
        OnPropertyChanged(nameof(ShowSmallIcon));
    }


    /// <summary>卡片是否走「大图标」兜底：无缩略图内容且拿到了大尺寸图标。</summary>
    public bool ShowLargeIcon => !HasThumbnailContent && LargeIconSource != null;

    /// <summary>卡片是否走「小图标原尺寸」兜底：无缩略图内容且没有大图标可用（非 Windows 常见）。</summary>
    public bool ShowSmallIcon => !HasThumbnailContent && LargeIconSource == null;

    partial void OnLargeIconSourceChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(ShowLargeIcon));
        OnPropertyChanged(nameof(ShowSmallIcon));
    }


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
    /// 短日期显示（yyyy-MM-dd）：Tile 迷你行 / 假列头日期单元宽度（104~168px）放不下 19 字符时间戳。
    /// 依赖 <see cref="LastModifiedDisplay"/> 固定 "yyyy-MM-dd" 前缀——若改该格式（如变成 dd/MM/yyyy），
    /// 切出来的就不是纯日期了；格式变更时必须同步改这里的切片逻辑。
    /// </summary>
    public string LastModifiedShortDisplay => LastModifiedDisplay.Length >= 10
        ? LastModifiedDisplay[..10]
        : LastModifiedDisplay;

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
            IsDirectory = item.IsDirectory,
            IsEncrypted = item.IsEncrypted
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
            IsEncrypted = IsEncrypted
        };
    }

    private static string FormatSize(long bytes) => FormatUtil.FormatSize(bytes);
}
