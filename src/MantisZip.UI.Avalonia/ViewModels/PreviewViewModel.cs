using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HarfBuzzSharp;
using MantisZip.Core.Utils;
using SkiaSharp;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ClosedXML.Excel;
using System.IO.Compression;
using System.Xml.Linq;
using Markdig;
using ReverseMarkdown;
using Microsoft.Data.Sqlite;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;
using Avalonia.Styling;

namespace MantisZip.UI.Avalonia.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    /// <summary>本地化字符串字典，XAML 通过 {Binding LocalizedStrings[Key]} 访问。</summary>
    public Dictionary<string, string> LocalizedStrings { get; } = new();

    [ObservableProperty]
    private PreviewType _previewType = PreviewType.None;

    [ObservableProperty]
    private string _textContent = string.Empty;

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private bool _isPreviewVisible;

    [ObservableProperty]
    private ObservableCollection<FormatMetadataItem> _formatMetadata = [];

    public PreviewViewModel()
    {
        MetadataSettingsManager.SettingsChanged += OnMetadataSettingsChanged;
        LocalizationManager.CultureChanged += OnCultureChanged;
        UpdateLocalizedStrings();
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        UpdateLocalizedStrings();
    }

    private void UpdateLocalizedStrings()
    {
        LocalizedStrings["Preview_Ligature"] = LocalizationManager.T("Preview_Ligature");
        LocalizedStrings["Preview_FileListLabel"] = LocalizationManager.T("Preview_FileListLabel");
        LocalizedStrings["Preview_OutlineLabel"] = LocalizationManager.T("Preview_OutlineLabel");
        OnPropertyChanged(nameof(LocalizedStrings));
        OnPropertyChanged(nameof(LoadingFileDisplay));
    }

    private void OnMetadataSettingsChanged()
    {
        OnPropertyChanged(nameof(FieldOrientation));
    }

    // ── 元数据系统属性（通用信息 / 格式信息分离） ──

    /// <summary>通用文件信息（Phase 1，格式检测前设置）。</summary>
    [ObservableProperty]
    private ObservableCollection<MetadataSection> _commonSections = [];

    /// <summary>格式特有信息（Phase 2，格式检测后设置）。</summary>
    [ObservableProperty]
    private ObservableCollection<MetadataSection> _formatSections = [];

    /// <summary>格式信息正在提取中（Phase 1 → Phase 2 之间为 true）。</summary>
    [ObservableProperty]
    private bool _isFormatPending;

    partial void OnIsFormatPendingChanged(bool value) => OnPropertyChanged(nameof(IsFormatEmpty));

    /// <summary>是否有格式信息可显示。</summary>
    [ObservableProperty]
    private bool _hasFormatSections;

    partial void OnHasFormatSectionsChanged(bool value) => OnPropertyChanged(nameof(IsFormatEmpty));

    /// <summary>无格式信息且不在加载中（显示空提示）。</summary>
    public bool IsFormatEmpty => !IsFormatPending && !HasFormatSections;

    /// <summary>信息面板字段显示方向，从 MetadataPanelSettings 读取。</summary>
    public global::Avalonia.Layout.Orientation FieldOrientation =>
        MetadataSettingsManager.Load().FieldLayoutMode == "horizontal"
            ? global::Avalonia.Layout.Orientation.Horizontal
            : global::Avalonia.Layout.Orientation.Vertical;

    /// <summary>文件列表顶部的元数据项（来自 common + format）。</summary>
    [ObservableProperty]
    private ObservableCollection<InfoPanelRow> _contentTopItems = [];

    [ObservableProperty]
    private bool _isContentTopVisible;

    /// <summary>Phase 1 保存的通用字段值，供 Phase 2 ShowXxx 合并用。</summary>
    internal Dictionary<string, string?>? CurrentCommonValues { get; set; }

    /// <summary>打开设置窗口到元数据面板标签页，由 View 注入。</summary>
    public Func<Task>? OpenSettingsToMetadataTab { get; set; }

    /// <summary>元数据面板设置按钮的提示文字。</summary>
    public string MetadataSettingsTooltip => LocalizationManager.T("Metadata_Panel_SettingsTooltip");

    [ObservableProperty]
    private string _previewHeaderText = string.Empty;

    // FontFamily 手动实现，不使用 [ObservableProperty]（源生成器对 Avalonia.Media 命名空间有已知问题）
    private global::Avalonia.Media.FontFamily _fontFamily = global::Avalonia.Media.FontFamily.Default;

    public global::Avalonia.Media.FontFamily FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, value);
    }

    /// <summary>
    /// 文本预览的字体，独立于 FontFamily（后者是框架继承属性，会被传播到子控件）。
    /// 只绑到文本预览 TextBox，不影响界面其他部分。
    /// </summary>
    private global::Avalonia.Media.FontFamily _textPreviewFontFamily = global::Avalonia.Media.FontFamily.Default;

    public global::Avalonia.Media.FontFamily TextPreviewFontFamily
    {
        get => _textPreviewFontFamily;
        set => SetProperty(ref _textPreviewFontFamily, value);
    }

    // ── ICO Gallery ──

    /// <summary>ICO 画廊的帧列表。</summary>
    public ObservableCollection<IcoFrame> IcoFrames { get; } = [];

    /// <summary>
    /// Markdown 预览控件树（由 MarkdownPreviewBuilder 生成）。
    /// </summary>
    private Panel? _markdownPreviewPanel;
    public Panel? MarkdownPreviewPanel
    {
        get => _markdownPreviewPanel;
        set => SetProperty(ref _markdownPreviewPanel, value);
    }

    /// <summary>原始帧（保留 Alpha），用于去透明还原。</summary>
    internal List<IcoFrame>? IcoOriginalFrames { get; set; }

    /// <summary>当前是否压平 Alpha（显示 RGB 原始颜色）。</summary>
    [ObservableProperty]
    private bool _icoFlattenAlpha;

    /// <summary>
    /// 字体预览的自动换行宽度。由 PreviewPanel 代码后置根据 ScrollViewer 实际宽度设置。
    /// </summary>
    public double FontPreviewWrapWidth { get; set; } = 700;

    // ── Toolbar ──

    [ObservableProperty]
    private bool _isToolbarVisible;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    /// <summary>
    /// 缩放后的图像逻辑尺寸，用于绑定 Image 的 Width/Height
    /// （替代 ScaleTransform，使 ScrollViewer 正确计算滚动区域）。
    /// </summary>
    public double ScaledWidth => Math.Max(1, ImageWidth * ZoomLevel);
    public double ScaledHeight => Math.Max(1, ImageHeight * ZoomLevel);

    [ObservableProperty]
    private int _fontSize = 13;

    // ── Loading state ──

    [ObservableProperty]
    private bool _isLoadingPreview;

    [ObservableProperty]
    private string _loadingFileName = string.Empty;

    /// <summary>加载中提示文案（本地化，含文件名）。</summary>
    public string LoadingFileDisplay =>
        string.IsNullOrEmpty(LoadingFileName)
            ? string.Empty
            : LocalizationManager.T("Preview_LoadingFile", LoadingFileName);

    partial void OnLoadingFileNameChanged(string value) =>
        OnPropertyChanged(nameof(LoadingFileDisplay));

    public bool HasZoomControls => PreviewType is PreviewType.Image or PreviewType.AnimatedImage;
    public bool HasFontSizeControls => PreviewType == PreviewType.Text;

    // Computed visibility per preview type
    public bool IsTextVisible => PreviewType == PreviewType.Text;
    public bool IsCsvVisible => PreviewType == PreviewType.Csv;
    public bool IsPeVisible => PreviewType == PreviewType.Pe;
    public bool IsUnsupportedVisible => PreviewType == PreviewType.Unsupported || PreviewType == PreviewType.None;

    public bool IsImageVisible => PreviewType == PreviewType.Image;
    public bool IsAnimatedImageVisible => PreviewType == PreviewType.AnimatedImage;
    public bool IsSvgVisible => PreviewType == PreviewType.Svg;
    public bool IsFontVisible => PreviewType == PreviewType.Font;
    public bool IsAudioVisible => PreviewType == PreviewType.Audio;
    public bool IsSqliteVisible => PreviewType == PreviewType.Sqlite;
    public bool IsIsoVisible => PreviewType == PreviewType.Iso;
    public bool IsTorrentVisible => PreviewType == PreviewType.Torrent;
    public bool IsOfficeVisible => PreviewType == PreviewType.Office;
    public bool IsDocxVisible => PreviewType == PreviewType.Docx;
    public bool IsXlsxVisible => PreviewType == PreviewType.Xlsx;
    public bool IsPptxVisible => PreviewType == PreviewType.Pptx;
    public bool IsVideoVisible => PreviewType == PreviewType.Video;
    public bool IsHtmlVisible => PreviewType == PreviewType.Html;
    public bool IsMarkdownVisible => PreviewType == PreviewType.Markdown;
    public bool IsMarkdownOrHtmlVisible => PreviewType is PreviewType.Markdown or PreviewType.Html;
    public bool IsPdfVisible => PreviewType == PreviewType.Pdf;
    public bool HasPdfNavigation => IsPdfVisible && _pdfTotalPages > 1;
    public bool IsIcoGalleryVisible => PreviewType == PreviewType.IcoGallery;
    public bool HasDocxOutline => DocxOutline.Count > 0;

    partial void OnPreviewTypeChanged(PreviewType value)
    {
        // 离开字体预览时取消主题切换订阅
        if (value != PreviewType.Font)
            UnsubscribeThemeChanged();

        OnPropertyChanged(nameof(IsTextVisible));
        OnPropertyChanged(nameof(IsCsvVisible));
        OnPropertyChanged(nameof(IsPeVisible));
        OnPropertyChanged(nameof(IsImageVisible));
        OnPropertyChanged(nameof(IsAnimatedImageVisible));
        OnPropertyChanged(nameof(IsSvgVisible));
        OnPropertyChanged(nameof(IsFontVisible));
        OnPropertyChanged(nameof(IsAudioVisible));
        OnPropertyChanged(nameof(IsSqliteVisible));
        OnPropertyChanged(nameof(IsIsoVisible));
        OnPropertyChanged(nameof(IsTorrentVisible));
        OnPropertyChanged(nameof(IsOfficeVisible));
        OnPropertyChanged(nameof(IsDocxVisible));
        OnPropertyChanged(nameof(IsXlsxVisible));
        OnPropertyChanged(nameof(IsPptxVisible));
        OnPropertyChanged(nameof(HasDocxOutline));
        OnPropertyChanged(nameof(HasPptxNavigation));
        OnPropertyChanged(nameof(PptxPageInfo));
        OnPropertyChanged(nameof(IsVideoVisible));
        OnPropertyChanged(nameof(IsUnsupportedVisible));
        OnPropertyChanged(nameof(HasZoomControls));
        OnPropertyChanged(nameof(HasFontSizeControls));
        OnPropertyChanged(nameof(HasAnimationControls));
        OnPropertyChanged(nameof(HasTransparencyControls));
        OnPropertyChanged(nameof(HasFlattenAlphaControls));
        OnPropertyChanged(nameof(HasLigatureControls));
        OnPropertyChanged(nameof(IsHtmlVisible));
        OnPropertyChanged(nameof(IsMarkdownVisible));
        OnPropertyChanged(nameof(IsMarkdownOrHtmlVisible));
        OnPropertyChanged(nameof(IsPdfVisible));
        OnPropertyChanged(nameof(HasPdfNavigation));
        OnPropertyChanged(nameof(IsIcoGalleryVisible));
        OnPropertyChanged(nameof(IsFontTextFallbackVisible));

        // Auto-dismiss loading overlay when switching to actual preview content.
        // PreviewType.None is set by ShowLoading() — keep the overlay visible.
        // All other PreviewType values represent actual content — hide the overlay.
        if (value != PreviewType.None)
            IsLoadingPreview = false;
    }

    partial void OnZoomLevelChanged(double value)
    {
        OnPropertyChanged(nameof(ScaledWidth));
        OnPropertyChanged(nameof(ScaledHeight));
    }

    partial void OnImageWidthChanged(int value)
    {
        OnPropertyChanged(nameof(ScaledWidth));
    }

    partial void OnImageHeightChanged(int value)
    {
        OnPropertyChanged(nameof(ScaledHeight));
    }

    partial void OnPreviewImageChanged(global::Avalonia.Media.Imaging.Bitmap? value)
    {
        OnPropertyChanged(nameof(IsFontTextFallbackVisible));
    }

    // ── CSV ──

    // DataView 实现了 IEnumerable，可绑定到 ItemsControl
    private DataTable? _csvDataTable;
    /// <summary>供代码后置访问原始 DataTable 以设置 DataGrid 列。</summary>
    public DataTable? CsvDataTable => _csvDataTable;

    [ObservableProperty]
    private System.Data.DataView? _csvData;

    // ── PE ──

    [ObservableProperty]
    private string _peTitle = string.Empty;

    [ObservableProperty]
    private string _peSubtitle = string.Empty;

    public ObservableCollection<PeMetadataItem> PeMetadata { get; } = [];

    // ── Image ──

    [ObservableProperty]
    private global::Avalonia.Media.Imaging.Bitmap? _previewImage;

    [ObservableProperty]
    private int _imageWidth;

    [ObservableProperty]
    private int _imageHeight;

    /// <summary>
    /// 预览视口大小（由 PreviewPanel 代码后置在布局变化时更新）。
    /// 用于 ZoomFit 和初始缩放计算，替代硬编码的 600×500。
    /// </summary>
    internal double ViewportWidth { get; set; } = 800;
    internal double ViewportHeight { get; set; } = 600;

    // ── Torrent ──

    [ObservableProperty]
    private ObservableCollection<TorrentTreeNode> _torrentTreeRoots = [];

    // ── SQLite ──

    [ObservableProperty]
    private System.Data.DataView? _sqliteTableData;

    [ObservableProperty]
    private ObservableCollection<string> _sqliteTableNames = [];

    [ObservableProperty]
    private int _selectedTableIndex;

    private string? _lastPreviewFilePath;
    private System.Data.DataTable? _currentSqliteTable;
    /// <summary>供代码后置访问原始 DataTable 以设置 DataGrid 列。</summary>
    public System.Data.DataTable? CurrentSqliteTable => _currentSqliteTable;

    // ── DOCX ──

    [ObservableProperty]
    private ObservableCollection<DocxOutlineItem> _docxOutline = [];

    /// <summary>
    /// DOCX 全文控件树（段落 TextBlock 与表格 Grid 按文档顺序交替），
    /// 由 ShowDocx 构建，替代原先的纯文本 DocxFullText。
    /// </summary>
    private Panel? _docxContentPanel;
    public Panel? DocxContentPanel
    {
        get => _docxContentPanel;
        set => SetProperty(ref _docxContentPanel, value);
    }

    [ObservableProperty]
    private string _docxNoOutlineText = string.Empty;

    // ── XLSX ──

    private DataTable? _xlsxDataTable;
    public DataTable? XlsxDataTable => _xlsxDataTable;

    [ObservableProperty]
    private System.Data.DataView? _xlsxData;

    // ── PPTX ──

    [ObservableProperty]
    private ObservableCollection<PptxSlideModel> _pptxSlides = [];

    [ObservableProperty]
    private int _pptxCurrentSlide = 1;

    [ObservableProperty]
    private int _pptxTotalSlides;

    [ObservableProperty]
    private double _pptxCanvasWidth = 960;

    [ObservableProperty]
    private double _pptxCanvasHeight = 540;

    /// <summary>当前幻灯片文本项（Canvas 定位用），由 OnPptxCurrentSlideChanged 维护。</summary>
    [ObservableProperty]
    private List<PptxTextItem>? _currentSlideItems;

    public bool HasPptxNavigation => IsPptxVisible && PptxTotalSlides > 1;

    public string PptxPageInfo => PptxTotalSlides > 0 ? $"{PptxCurrentSlide} / {PptxTotalSlides}" : string.Empty;

    partial void OnPptxCurrentSlideChanged(int value)
    {
        var slide = PptxSlides.ElementAtOrDefault(value - 1);
        CurrentSlideItems = slide?.TextItems;
        OnPropertyChanged(nameof(PptxPageInfo));
    }

    partial void OnPptxTotalSlidesChanged(int value)
    {
        OnPropertyChanged(nameof(HasPptxNavigation));
        OnPropertyChanged(nameof(PptxPageInfo));
    }

    [RelayCommand]
    private void PptxPreviousSlide()
    {
        if (PptxCurrentSlide > 1)
            PptxCurrentSlide--;
    }

    [RelayCommand]
    private void PptxNextSlide()
    {
        if (PptxCurrentSlide < PptxTotalSlides)
            PptxCurrentSlide++;
    }

    // ── GIF animation ──

    [ObservableProperty]
    private bool _isPlaying = true;

    [ObservableProperty]
    private int _currentFrame;

    [ObservableProperty]
    private int _totalFrames;

    // ── Preview Info Panel ──

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fileSize = string.Empty;

    [ObservableProperty]
    private string _compressedSize = string.Empty;

    [ObservableProperty]
    private string _compressionRatio = string.Empty;

    [ObservableProperty]
    private string _modifiedDate = string.Empty;

    [ObservableProperty]
    private bool _isInfoPanelVisible;

    /// <summary>用户偏好：是否显示信息面板（持久化，与内容驱动状态独立）。</summary>
    [ObservableProperty]
    private bool _showInfoPanel = true;

    /// <summary>信息面板最终可见性 = 内容驱动状态 &amp;&amp; 用户偏好。</summary>
    public bool IsInfoPanelEffectiveVisible => IsInfoPanelVisible && ShowInfoPanel;

    partial void OnIsInfoPanelVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInfoPanelEffectiveVisible));
    }

    partial void OnShowInfoPanelChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInfoPanelEffectiveVisible));
    }

    [ObservableProperty]
    private string _infoPanelOrientation = "Vertical";

    public bool IsHorizontalInfoPanel => InfoPanelOrientation != "Vertical";
    public bool IsVerticalInfoPanel => InfoPanelOrientation == "Vertical";

    partial void OnInfoPanelOrientationChanged(string value)
    {
        OnPropertyChanged(nameof(IsHorizontalInfoPanel));
        OnPropertyChanged(nameof(IsVerticalInfoPanel));
    }

    public void ToggleInfoPanelOrientation()
    {
        InfoPanelOrientation = InfoPanelOrientation == "Vertical" ? "Horizontal" : "Vertical";
    }

    [RelayCommand]
    private async Task OpenMetadataSettings()
    {
        if (OpenSettingsToMetadataTab != null)
            await OpenSettingsToMetadataTab();
    }

    public void SetFileInfo(string name, string size, string compressed, string ratio, string modified)
    {
        FileName = name;
        FileSize = size;
        CompressedSize = compressed;
        CompressionRatio = ratio;
        ModifiedDate = modified;
        IsInfoPanelVisible = true;
    }

    public bool HasAnimationControls => PreviewType == PreviewType.AnimatedImage;

    // ── Ligature toggle ──

    public bool HasLigatureControls => PreviewType == PreviewType.Font;
    public bool HasTransparencyControls => PreviewType is PreviewType.Image or PreviewType.Svg or PreviewType.IcoGallery;
    public bool HasFlattenAlphaControls => PreviewType is PreviewType.Image or PreviewType.Svg;

    [ObservableProperty]
    private bool _isLigatureEnabled = true;

    [ObservableProperty]
    private bool _isTransparencyBgShown;

    /// <summary>原始预览位图（未压平 Alpha 时的备份）。</summary>
    private Bitmap? _originalPreviewImage;

    /// <summary>图像的 SkiaSharp 缓存副本，用于快速像素级操作（压平 Alpha 等）。</summary>
    private SkiaSharp.SKBitmap? _skOriginalPreview;

    /// <summary>是否已压平 Alpha（不显示透明）。</summary>
    [ObservableProperty]
    private bool _isFlattenAlpha;

    public bool CanLigatureToggle => _fontSupportsLigature;

    private List<GifFrameData>? _gifFrames;
    private int _gifCurrentFrameIndex;
    private DispatcherTimer? _gifTimer;


    // ── Toolbar commands ──

    /// <summary>
    /// 标记当前是否为「适应视口」模式。为 true 时，视口尺寸变化会重新调用 ZoomFit。
    /// </summary>
    private bool _isZoomFitActive;

    [RelayCommand]
    private void ZoomIn()
    {
        _isZoomFitActive = false;
        ZoomLevel = Math.Min(ZoomLevel + 0.25, 5.0);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        _isZoomFitActive = false;
        ZoomLevel = Math.Max(ZoomLevel - 0.25, 0.1);
    }

    [RelayCommand]
    private void ZoomFit()
    {
        _isZoomFitActive = true;
        if (ImageWidth > 0 && ImageHeight > 0 && ViewportWidth > 0 && ViewportHeight > 0)
        {
            var fitX = ViewportWidth / ImageWidth;
            var fitY = ViewportHeight / ImageHeight;
            ZoomLevel = Math.Min(fitX, fitY);
            if (ZoomLevel > 1.0) ZoomLevel = 1.0;
        }
        else
        {
            ZoomLevel = 1.0;
        }
    }

    /// <summary>
    /// 如果在「适应视口」模式（_isZoomFitActive），重新计算缩放比例。
    /// 由 PreviewPanel 代码后置在视口尺寸变化时调用。
    /// </summary>
    public void ReFitIfNeeded()
    {
        if (_isZoomFitActive)
            ZoomFit();
    }

    [RelayCommand]
    private void ToggleFlattenAlpha()
    {
        IsFlattenAlpha = !IsFlattenAlpha;
        if (IsFlattenAlpha)
        {
            if (_skOriginalPreview != null)
                PreviewImage = FlattenAlphaSkia(_skOriginalPreview);
            else if (PreviewImage != null)
                PreviewImage = FlattenAlpha(PreviewImage);
        }
        else
        {
            if (_originalPreviewImage != null)
                PreviewImage = _originalPreviewImage;
        }
    }

    [RelayCommand]
    private void IncreaseFontSize()
    {
        FontSize = Math.Min(FontSize + 2, 48);
        var settings = AppSettings.Load();
        settings.TextPreviewFontSize = FontSize;
        settings.Save();
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        FontSize = Math.Max(FontSize - 2, 8);
        var settings = AppSettings.Load();
        settings.TextPreviewFontSize = FontSize;
        settings.Save();
    }

    // ── GIF controls ──

    [RelayCommand]
    private void PlayPauseGif()
    {
        IsPlaying = !IsPlaying;
        if (IsPlaying)
            StartGifAnimation();
        else
            StopGifTimer();
    }

    [RelayCommand]
    private void PreviousGifFrame()
    {
        if (_gifFrames == null || _gifFrames.Count == 0) return;
        StopGifTimer();
        _gifCurrentFrameIndex = (_gifCurrentFrameIndex - 1 + _gifFrames.Count) % _gifFrames.Count;
        CurrentFrame = _gifCurrentFrameIndex;
        PreviewImage = _gifFrames[_gifCurrentFrameIndex].Bitmap;
    }

    [RelayCommand]
    private void NextGifFrame()
    {
        if (_gifFrames == null || _gifFrames.Count == 0) return;
        StopGifTimer();
        _gifCurrentFrameIndex = (_gifCurrentFrameIndex + 1) % _gifFrames.Count;
        CurrentFrame = _gifCurrentFrameIndex;
        PreviewImage = _gifFrames[_gifCurrentFrameIndex].Bitmap;
    }

    // ── Ligature toggle ──

    [RelayCommand]
    private void ToggleTransparencyBg()
    {
        IsTransparencyBgShown = !IsTransparencyBgShown;
    }

    [RelayCommand]
    private void ToggleLigature()
    {
        IsLigatureEnabled = !IsLigatureEnabled;
        // 持久化到设置
        var settings = AppSettings.Load();
        settings.FontPreviewEnableLigature = IsLigatureEnabled;
        settings.Save();
        // 重新渲染
        ReRenderFontPreview();
    }

    partial void OnCurrentFrameChanged(int value)
    {
        if (_gifFrames == null || value < 0 || value >= _gifFrames.Count) return;
        if (!_isAnimating)
            StopGifTimer();
        _gifCurrentFrameIndex = value;
        PreviewImage = _gifFrames[value].Bitmap;
    }

    /// <summary>
    /// 显示文本预览。
    /// </summary>
    public void ShowText(string filePath)
    {
        var content = TextEncodingDetector.DetectAndReadText(filePath);
        TextContent = content;
        PreviewType = PreviewType.Text;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        // 从设置加载文本预览字号和字体
        var settings = AppSettings.Load();
        FontSize = settings.TextPreviewFontSize;
        var fontFamilyName = settings.TextPreviewFontFamily;
        try
        {
            TextPreviewFontFamily = !string.IsNullOrEmpty(fontFamilyName)
                ? new global::Avalonia.Media.FontFamily(fontFamilyName)
                : global::Avalonia.Media.FontFamily.Default;
        }
        catch
        {
            TextPreviewFontFamily = global::Avalonia.Media.FontFamily.Default;
        }
    }

    /// <summary>
    /// 在预览面板显示压缩包注释（ZIP EOCD 注释 / RAR5 归档注释）。
    /// 注释直接以文本预览方式展示，信息面板显示压缩包名。
    /// </summary>
    /// <param name="comment">注释文本（已去除首尾空白）</param>
    /// <param name="archiveName">压缩包文件名（含扩展名），用于信息面板</param>
    public void ShowArchiveComment(string comment, string archiveName)
    {
        Clear();

        // 与 ShowText 一致：从设置加载文本预览字号和字体
        var settings = AppSettings.Load();
        FontSize = settings.TextPreviewFontSize;
        var fontFamilyName = settings.TextPreviewFontFamily;
        try
        {
            TextPreviewFontFamily = !string.IsNullOrEmpty(fontFamilyName)
                ? new global::Avalonia.Media.FontFamily(fontFamilyName)
                : global::Avalonia.Media.FontFamily.Default;
        }
        catch
        {
            TextPreviewFontFamily = global::Avalonia.Media.FontFamily.Default;
        }

        TextContent = comment;
        PreviewType = PreviewType.Text;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        PreviewHeaderText = LocalizationManager.T("Preview_ArchiveCommentTitle");

        // 信息面板：显示压缩包名（其余通用字段留空）
        UpdateCommonMetadata(archiveName, string.Empty, string.Empty, string.Empty, string.Empty);
    }

    /// <summary>
    /// 显示 CSV 表格预览。
    /// </summary>
    public void ShowCsv(string filePath)
    {
        // 行列上限来自运行时配置（App.axaml.cs 启动时 + 设置保存时同步），与 WPF 版一致
        var maxRows = PreviewService.MaxTablePreviewRows;
        var maxCols = PreviewService.MaxTablePreviewCols;
        var table = new DataTable();
        var lines = File.ReadLines(filePath).Take(maxRows + 1).ToList();

        if (lines.Count > 0)
        {
            var rawHeaders = CsvParser.ParseCsvLine(lines[0]);
            var headers = CsvParser.MakeUniqueColumnNames(rawHeaders.Take(maxCols).ToArray());
            foreach (var h in headers)
                table.Columns.Add(h);

            foreach (var line in lines.Skip(1).Take(maxRows))
            {
                var values = CsvParser.ParseCsvLine(line);
                var row = table.NewRow();
                for (int i = 0; i < Math.Min(values.Length, table.Columns.Count); i++)
                    row[i] = values[i].Trim();
                table.Rows.Add(row);
            }
        }

        _csvDataTable = table;
        CsvData = table.DefaultView;  // DataView 可绑定到 ItemsControl
        PreviewType = PreviewType.Csv;
        IsPreviewVisible = true;
        IsToolbarVisible = false;
    }

    /// <summary>
    /// 显示 PE 元数据预览。
    /// </summary>
    public void ShowPe(string filePath)
    {
        var info = PeParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported(LocalizationManager.T("Preview_PeFailed"));
            return;
        }

        PeTitle = info.ProductName ?? info.AdditionalInfo ?? Path.GetFileName(filePath);
        PeSubtitle = LocalizationManager.T("Preview_Pe_Subtitle",
            info.Architecture ?? LocalizationManager.T("Preview_Pe_Unknown"),
            info.Subsystem ?? LocalizationManager.T("Preview_Pe_Unknown"));
        PeMetadata.Clear();

        AddPeMeta(LocalizationManager.T("Preview_Pe_ProductName"), info.ProductName);
        AddPeMeta(LocalizationManager.T("Preview_Pe_Company"), info.CompanyName);
        AddPeMeta(LocalizationManager.T("Preview_Pe_FileVersion"), info.FileVersion);
        AddPeMeta(LocalizationManager.T("Preview_Pe_ProductVersion"), info.ProductVersion);
        AddPeMeta(LocalizationManager.T("Preview_Pe_Description"), info.AdditionalInfo);

        PreviewType = PreviewType.Pe;
        IsPreviewVisible = true;
        IsToolbarVisible = false;
    }

    // ── Image ──

    /// <summary>
    /// 显示图片预览。
    /// </summary>
    public void ShowImage(string filePath)
    {
        App.DebugLog($"[IMG] ShowImage: {filePath}");
        App.DebugLog($"[IMG] Before: PreviewType={PreviewType}, PreviewImage={(PreviewImage != null ? $"w{ImageWidth}xh{ImageHeight}" : "null")}");

        // 解码尺寸策略：DecodeToWidth 会无条件把位图缩放到目标宽度（小图也会被放大），
        // 因此先经 SKCodec 读头部拿真实宽度，仅当宽 > 1920 时才降采样；小图原生解码保持清晰度，
        // 与 WPF 版 ShowImage 的 DecodePixelWidth 门槛语义一致。
        global::Avalonia.Media.Imaging.Bitmap bitmap;
        using (var fs = File.OpenRead(filePath))
        {
            using var skStream = new SKManagedStream(fs, disposeManagedStream: false);
            using var codec = SKCodec.Create(skStream);
            if (codec == null || codec.Info.Width <= 0)
            {
                // codec 无法解析时退回原生解码路径（解码失败由上层异常处理）
                fs.Position = 0;
                bitmap = new global::Avalonia.Media.Imaging.Bitmap(fs);
            }
            else if (codec.Info.Width > 1920)
            {
                fs.Position = 0;
                bitmap = global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(fs, 1920);
            }
            else
            {
                fs.Position = 0;
                bitmap = new global::Avalonia.Media.Imaging.Bitmap(fs);
            }
        }
        App.DebugLog($"[IMG] Bitmap loaded: {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}, dpi={bitmap.Dpi.X}x{bitmap.Dpi.Y}");

        // 先设置 PreviewType 让 Image 控件进入可见状态，再设置 Source
        PreviewType = PreviewType.Image;
        PreviewImage = bitmap;
        _originalPreviewImage = bitmap;

        // 缓存 SkiaSharp 副本供像素级操作
        _skOriginalPreview?.Dispose();
        _skOriginalPreview = BitmapToSkia(bitmap);

        ImageWidth = bitmap.PixelSize.Width;
        ImageHeight = bitmap.PixelSize.Height;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        PreviewHeaderText = LocalizationManager.T("Preview_Header_Image");
        // 初始缩放：适应视口
        ZoomFit();

        var formatValues = new Dictionary<string, string?>
        {
            [MetadataKeys.Dimensions] = $"{ImageWidth} × {ImageHeight}",
            [MetadataKeys.ImageDpi] = $"{bitmap.Dpi.X:F0} × {bitmap.Dpi.Y:F0}",
        };
        MetadataHelper.RenderFormatToViewModel(this, formatValues, "image");
        App.DebugLog($"[IMG] ShowImage done: PreviewType={PreviewType}, Zoom={ZoomLevel}, IsToolbarVisible={IsToolbarVisible}");
    }

    // ── ICO Gallery ──

    public void ShowIcoGallery(string filePath)
    {
        App.DebugLog($"[ICO] ShowIcoGallery: {filePath}");

        var frames = IcoParser.ExtractFrames(filePath);
        if (frames.Count == 0)
        {
            App.DebugLog("[ICO] No frames extracted, showing unsupported");
            ShowUnsupported("");
            return;
        }

        IcoFrames.Clear();
        IcoOriginalFrames = frames;
        IcoFlattenAlpha = false;
        foreach (var f in frames)
            IcoFrames.Add(f);

        PreviewType = PreviewType.IcoGallery;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        PreviewHeaderText = LocalizationManager.T("Preview_Header_IcoSizes", frames.Count);

        var fi = new FileInfo(filePath);
        var icoFormatValues = new Dictionary<string, string?>
        {
            ["IconCount"] = frames.Count.ToString(),
        };
        MetadataHelper.RenderFormatToViewModel(this, icoFormatValues, "ico");
    }

    [RelayCommand]
    private void ToggleIcoFlattenAlpha()
    {
        IcoFlattenAlpha = !IcoFlattenAlpha;
        var src = IcoOriginalFrames;
        if (src == null) return;

        IcoFrames.Clear();
        foreach (var f in src)
        {
            if (IcoFlattenAlpha)
                IcoFrames.Add(new IcoFrame(FlattenAlpha(f.Bitmap), f.Width, f.Height));
            else
                IcoFrames.Add(f);
        }
    }

    private static Bitmap FlattenAlpha(Bitmap source)
    {
        // Encode source to PNG bytes, decode with SkiaSharp
        byte[] srcBytes;
        using (var ms = new MemoryStream())
        {
            source.Save(ms);
            srcBytes = ms.ToArray();
        }
        using var srcSk = SkiaSharp.SKBitmap.Decode(srcBytes);
        if (srcSk == null) return source;

        // Copy bitmap and set alpha to 255 for all pixels,
        // revealing the original RGB colors beneath transparency.
        using var dstSk = new SkiaSharp.SKBitmap(srcSk.Width, srcSk.Height);
        using (var canvas = new SkiaSharp.SKCanvas(dstSk))
        {
            canvas.DrawBitmap(srcSk, 0, 0);
        }
        for (int y = 0; y < dstSk.Height; y++)
        {
            for (int x = 0; x < dstSk.Width; x++)
            {
                dstSk.SetPixel(x, y, dstSk.GetPixel(x, y).WithAlpha(255));
            }
        }

        using var image = SkiaSharp.SKImage.FromBitmap(dstSk);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var ms2 = new MemoryStream(data.ToArray());
        return new Bitmap(ms2);
    }

    /// <summary>
    /// 将 Avalonia Bitmap 转为 SkiaSharp SKBitmap（一次性 PNG 解码代价，仅在加载时支付）。
    /// </summary>
    private static SkiaSharp.SKBitmap? BitmapToSkia(Bitmap? source)
    {
        if (source == null) return null;
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            source.Save(ms);
            bytes = ms.ToArray();
        }
        return SkiaSharp.SKBitmap.Decode(bytes);
    }

    /// <summary>
    /// 从 SKBitmap 批量操作像素并返回 Avalonia WriteableBitmap（零 PNG 编解码）。
    /// 将所有像素 alpha 设为 255，显示 RGB 原始颜色。
    /// </summary>
    private static Bitmap FlattenAlphaSkia(SkiaSharp.SKBitmap src)
    {
        if (src == null) throw new ArgumentNullException(nameof(src));

        int totalBytes = src.Height * src.RowBytes;
        byte[] pixelData = new byte[totalBytes];
        System.Runtime.InteropServices.Marshal.Copy(src.GetPixels(), pixelData, 0, totalBytes);

        // 步进 4 字节（BGRA8888），第 4 字节是 alpha → 设为 255
        for (int i = 3; i < totalBytes; i += 4)
            pixelData[i] = 255;

        var wb = new WriteableBitmap(
            new global::Avalonia.PixelSize(src.Width, src.Height),
            new global::Avalonia.Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888,
            global::Avalonia.Platform.AlphaFormat.Premul);
        using var locked = wb.Lock();
        System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, locked.Address, totalBytes);
        return wb;
    }

    // ── GIF ──

    /// <summary>
    /// 显示 GIF 预览。
    /// </summary>
    public void ShowGif(string filePath)
    {
        StopGifTimer();
        _gifFrames = null;

        try
        {
            var frames = GifDecoder.DecodeFrames(filePath);
            if (frames == null || frames.Count == 0)
            {
                ShowUnsupported(LocalizationManager.T("Preview_GifDecodeFailed"));
                return;
            }

            TotalFrames = frames.Count;
            IsPlaying = true;
            CurrentFrame = 0;
            _gifCurrentFrameIndex = 0;

            _gifFrames = frames;

            if (frames.Count > 0)
            {
                PreviewImage = frames[0].Bitmap;
                _originalPreviewImage = frames[0].Bitmap;

                // 缓存 SkiaSharp 副本
                _skOriginalPreview?.Dispose();
                _skOriginalPreview = BitmapToSkia(frames[0].Bitmap);

                ImageWidth = frames[0].Bitmap.PixelSize.Width;
                ImageHeight = frames[0].Bitmap.PixelSize.Height;
            }

            // 启动动画
            if (frames.Count > 1)
                StartGifAnimation();

            // 初始缩放：适应视口
            ZoomFit();

            PreviewType = PreviewType.AnimatedImage;
            IsPreviewVisible = true;
            IsToolbarVisible = true;
            PreviewHeaderText = LocalizationManager.T("Preview_Header_Gif");
            var gifFormatValues = new Dictionary<string, string?>
            {
                [MetadataKeys.Dimensions] = $"{ImageWidth} × {ImageHeight}",
                [MetadataKeys.FrameCount] = TotalFrames.ToString(),
            };
            MetadataHelper.RenderFormatToViewModel(this, gifFormatValues, "image");
        }
        catch (Exception ex)
        {
            ShowUnsupported(LocalizationManager.T("Preview_GifLoadFailed", ex.Message));
        }
    }

    private void StartGifAnimation()
    {
        if (_gifFrames == null || _gifFrames.Count <= 1) return;

        StopGifTimer();
        _gifTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher.UIThread);
        _gifTimer.Interval = TimeSpan.FromMilliseconds(_gifFrames[_gifCurrentFrameIndex].DelayMs);
        _gifTimer.Tick += OnGifTimerTick;
        _gifTimer.Start();
    }

    internal void StopGifTimer()
    {
        if (_gifTimer != null)
        {
            _gifTimer.Stop();
            _gifTimer.Tick -= OnGifTimerTick;
            _gifTimer = null;
        }
    }

    private bool _isAnimating;

    private void OnGifTimerTick(object? sender, EventArgs e)
    {
        try
        {
            if (_gifFrames == null || _gifFrames.Count == 0) return;

            _isAnimating = true;
            _gifCurrentFrameIndex = (_gifCurrentFrameIndex + 1) % _gifFrames.Count;
            CurrentFrame = _gifCurrentFrameIndex;
            PreviewImage = _gifFrames[_gifCurrentFrameIndex].Bitmap;

            if (_gifTimer != null && _gifCurrentFrameIndex < _gifFrames.Count)
                _gifTimer.Interval = TimeSpan.FromMilliseconds(_gifFrames[_gifCurrentFrameIndex].DelayMs);
            _isAnimating = false;
        }
        catch
        {
            _isAnimating = false;
        }
    }

    // ── SVG ──

    /// <summary>
    /// 显示 SVG 预览（通过 Bitmap 栅格化渲染）。
    /// </summary>
    public void ShowSvg(string filePath)
    {
        try
        {
            var svg = new Svg.Skia.SKSvg();
            svg.Load(filePath);

            if (svg.Picture == null)
            {
                ShowUnsupported(LocalizationManager.T("Preview_SvgParseFailed"));
                return;
            }

            var rect = svg.Picture.CullRect;
            var svgW = Math.Max(1, (float)rect.Width);
            var svgH = Math.Max(1, (float)rect.Height);

            // 最小预览尺寸 512px（小图标自动放大），最大 2048px 防撑爆
            const float minSize = 512f;
            const float maxSize = 2048f;
            var scale = 1f;
            if (svgW < minSize && svgH < minSize)
                scale = Math.Min(minSize / svgW, minSize / svgH);
            if (svgW * scale > maxSize || svgH * scale > maxSize)
                scale = Math.Min(maxSize / (svgW * scale), maxSize / (svgH * scale)) * scale;

            var w = (int)(svgW * scale);
            var h = (int)(svgH * scale);

            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(w, h));
            var canvas = surface.Canvas;
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            canvas.Scale((float)w / rect.Width, (float)h / rect.Height);
            canvas.DrawPicture(svg.Picture);
            canvas.Flush();

            using var img = surface.Snapshot();
            using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(data.ToArray());
            // 先设置 PreviewType 让 Image 控件进入可见状态并关闭加载遮罩，再设置 Source
            PreviewType = PreviewType.Svg;
            PreviewImage = new global::Avalonia.Media.Imaging.Bitmap(ms);
            _originalPreviewImage = PreviewImage;

            // 缓存 SkiaSharp 副本（从同一 Snapshot 直接转，无需第二次 PNG 编解码）
            _skOriginalPreview?.Dispose();
            _skOriginalPreview = SkiaSharp.SKBitmap.FromImage(img);

            // 缩放基础尺寸 = 栅格化后的位图尺寸（与 ShowImage 对齐，供 ZoomFit/ScaledWidth 使用）
            ImageWidth = w;
            ImageHeight = h;

            IsPreviewVisible = true;
            IsToolbarVisible = true;
            PreviewHeaderText = LocalizationManager.T("Preview_Header_Svg");
            // 初始缩放：适应视口（与 ShowImage 对齐）
            ZoomFit();
        }
        catch (Exception ex)
        {
            ShowUnsupported(LocalizationManager.T("Preview_SvgRenderFailed", ex.Message));
        }
    }

    // ── Font ──

    private string? _fontPreviewFontPath;
    private string _fontPreviewSampleText = string.Empty;
    private byte[]? _fontPreviewCachedData;
    private bool _fontPreviewIsDark;
    private bool _fontSupportsLigature;
    /// <summary>主题切换时自动重渲染字体预览。</summary>
    private bool _themeSubscribed;

    /// <summary>
    /// 显示字体元数据与示例文本。
    /// </summary>
    public void ShowFont(string filePath)
    {
        var info = FontParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported(LocalizationManager.T("Preview_FontParseFailed"));
            return;
        }
        PreviewType = PreviewType.Font;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        PreviewHeaderText = info.FontName ?? LocalizationManager.T("Preview_Header_Font");
        var fontFormatValues = new Dictionary<string, string?>
        {
            [MetadataKeys.FontName] = info.FontName,
            [MetadataKeys.FontStyle] = info.FontStyle,
            [MetadataKeys.GlyphCount] = info.GlyphCount?.ToString(),
        };
        MetadataHelper.RenderFormatToViewModel(this, fontFormatValues, "font");

        var fontFilePath = info.FontDecompressedPath ?? filePath;
        _fontPreviewFontPath = fontFilePath;

        // 读取并缓存字体数据，供 ReRenderFontPreview 复用（避免重复文件 I/O）
        _fontPreviewCachedData = File.ReadAllBytes(fontFilePath);

        // 从设置读取样本文字和字号
        var settings = AppSettings.Load();
        var sampleText = settings.FontPreviewSampleText;
        if (string.IsNullOrEmpty(sampleText))
            sampleText = "The quick brown fox jumps over the lazy dog\n0123456789\nABCDEFGHIJKLMNOPQRSTUVWXYZ\nabcdefghijklmnopqrstuvwxyz\n天地玄黄 宇宙洪荒 日月盈昃 辰宿列张";

        FontSize = settings.FontPreviewFontSize;
        _fontPreviewIsDark = Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
        IsLigatureEnabled = settings.FontPreviewEnableLigature;

        // 检测连字支持
        _fontSupportsLigature = CheckFontSupportsLigature(_fontPreviewCachedData, FontSize);
        OnPropertyChanged(nameof(CanLigatureToggle));

        // Avalonia 12 FontFamily(fileUri#name) 对所有格式的字体都会崩溃（Skia 原生 bug），
        // 统一走 SkiaSharp FromStream + 自动换行位图渲染。
        FontFamily = global::Avalonia.Media.FontFamily.Default;
        RenderFontPreview(_fontPreviewCachedData, sampleText, _fontPreviewIsDark);

        // 订阅主题切换，自动重渲染字体预览
        SubscribeThemeChanged();
    }

    /// <summary>
    /// 重新渲染字体预览（用于窗口缩放后更新折行宽度）。
    /// 使用 ShowFont 时缓存的字体数据和主题色，避免重复文件 I/O。
    /// </summary>
    public void ReRenderFontPreview()
    {
        if (_fontPreviewCachedData == null) return;
        // 每次重新渲染时重新判断暗色模式（主题可能在预览期间已切换）
        _fontPreviewIsDark = Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
        RenderFontPreview(_fontPreviewCachedData, _fontPreviewSampleText, _fontPreviewIsDark);
    }

    private void SubscribeThemeChanged()
    {
        if (_themeSubscribed) return;
        var app = Application.Current;
        if (app != null)
        {
            app.ActualThemeVariantChanged += OnAppThemeChanged;
            _themeSubscribed = true;
        }
    }

    private void UnsubscribeThemeChanged()
    {
        if (!_themeSubscribed) return;
        var app = Application.Current;
        if (app != null)
            app.ActualThemeVariantChanged -= OnAppThemeChanged;
        _themeSubscribed = false;
    }

    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        if (PreviewType != PreviewType.Font) return;
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _fontPreviewIsDark = Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
            RenderFontPreview(_fontPreviewCachedData, _fontPreviewSampleText, _fontPreviewIsDark);
        });
    }

    /// <summary>
    /// 检测字体是否支持连字（liga feature）。
    /// 用 HarfBuzz 对同一文本分别以 liga=1 和 liga=0 做 shaping，
    /// 如果得到的 glyph 序列不同，说明字体实现了 liga 替代规则。
    /// </summary>
    private static bool CheckFontSupportsLigature(byte[] fontData, float fontSize)
    {
        try
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(fontData, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                using var blob = new HarfBuzzSharp.Blob(handle.AddrOfPinnedObject(), fontData.Length, HarfBuzzSharp.MemoryMode.Duplicate);
                using var face = new HarfBuzzSharp.Face(blob, 0);
                using var hbFont = new global::HarfBuzzSharp.Font(face);

                const string testText = "fi ff fl ffi ffl --> != =>";

                // 用全部 4 个影响连字的 feature 来对比：
                //   calt (Contextual Alternates) — Fira Code 等编程字体用
                //   liga (Standard Ligatures)     — fi/fl/ff 等传统合字
                //   dlig (Discretionary Ligatures) — 可选连字
                //   clig (Contextual Ligatures)    — 上下文连字
                var ligFeatures = new[]
                {
                    new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("calt"), 1),
                    new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("liga"), 1),
                    new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("dlig"), 1),
                    new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("clig"), 1),
                };
                var noLigFeatures = new[]
                {
                    new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("calt"), 0),
                    new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("liga"), 0),
                    new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("dlig"), 0),
                    new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("clig"), 0),
                };

                var bufferOn = new HarfBuzzSharp.Buffer();
                bufferOn.AddUtf8(testText);
                bufferOn.GuessSegmentProperties();
                hbFont.Shape(bufferOn, ligFeatures);

                var bufferOff = new HarfBuzzSharp.Buffer();
                bufferOff.AddUtf8(testText);
                bufferOff.GuessSegmentProperties();
                hbFont.Shape(bufferOff, noLigFeatures);

                var onInfos = bufferOn.GlyphInfos;
                var offInfos = bufferOff.GlyphInfos;

                if (onInfos.Length != offInfos.Length)
                    return true;

                for (int i = 0; i < onInfos.Length; i++)
                {
                    if (onInfos[i].Codepoint != offInfos[i].Codepoint)
                        return true;
                }

                return false;
            }
            finally
            {
                handle.Free();
            }
        }
        catch
        {
            return false;
        }
    }

    private void RenderFontPreview(byte[] fontData, string sampleText, bool isDark)
    {
        PreviewImage = null;
        _fontPreviewSampleText = sampleText;
        // 保留原始（未过滤）样本文本，供渲染失败时回退显示，避免显示被过滤后的空文本
        var originalSampleText = sampleText;
        try
        {
            using var memStream = new MemoryStream(fontData);
            using var typeface = SkiaSharp.SKTypeface.FromStream(memStream);
            if (typeface != null)
            {
                // ── 检测字体是否支持 CJK 字符 ──
                var testChars = "中天国汉字";
                var testGlyphs = typeface.GetGlyphs(testChars.AsSpan());
                bool supportsCjk = testGlyphs.Any(g => g != 0);

                if (!supportsCjk)
                {
                    // 不支持 CJK 时按字符移除中文字符，而不是删除整行——
                    // 行首中文标签（如「英文：」「数字：」）不应导致整行内容丢失。
                    // 保留英文/数字/符号/emoji（含 surrogate pair），仅剔除 CJK 区字符。
                    var filtered = new StringBuilder(sampleText.Length);
                    foreach (char c in sampleText)
                    {
                        if (c >= 0x4E00 && c <= 0x9FFF) continue;  // CJK Unified
                        if (c >= 0x3000 && c <= 0x303F) continue;  // CJK Symbols
                        if (c >= 0xFF00 && c <= 0xFFEF) continue;  // Fullwidth / Halfwidth
                        if (c >= 0x2E80 && c <= 0x2EFF) continue;  // CJK Radicals
                        filtered.Append(c);
                    }

                    var filteredText = filtered.ToString().Trim();
                    if (string.IsNullOrEmpty(filteredText))
                    {
                        // 全部字符被过滤（样本文本全为中文）时，回退到默认英文样本文本，
                        // 避免空文本导致后续渲染异常或空白预览。
                        sampleText = "The quick brown fox jumps over the lazy dog\n0123456789\nABCDEFGHIJKLMNOPQRSTUVWXYZ\nabcdefghijklmnopqrstuvwxyz";
                    }
                    else
                    {
                        sampleText = filteredText;
                    }
                }

                var textColor = isDark ? SkiaSharp.SKColors.White : SkiaSharp.SKColors.Black;

                using var font = new SkiaSharp.SKFont(typeface, FontSize);
                using var paint = new SkiaSharp.SKPaint
                {
                    Color = textColor,
                    IsAntialias = true,
                };

                // ── 自动换行渲染（一次性完成折行和测量，避免重复 MeasureText） ──
                float wrapWidth = Math.Max((float)FontPreviewWrapWidth - 40f, 100f);
                float padding = 20f;
                var logicalLines = sampleText.Split('\n');

                // (Text, Width) — 折行时同时缓存每行宽度，消除二次测量
                var wrappedLines = new List<(string Text, float Width)>();
                float totalHeight = 0;
                float maxLineWidth = 0;

                foreach (var logicalLine in logicalLines)
                {
                    var lineWidth = font.MeasureText(logicalLine);
                    if (lineWidth <= wrapWidth)
                    {
                        wrappedLines.Add((logicalLine, lineWidth));
                        if (lineWidth > maxLineWidth) maxLineWidth = lineWidth;
                        totalHeight += font.Spacing;
                    }
                    else
                    {
                        // 在单词边界折行，逐词测量
                        var words = logicalLine.Split(' ');
                        var currentLine = new StringBuilder();
                        float currentWidth = 0;
                        foreach (var word in words)
                        {
                            var wordWidth = font.MeasureText(word);
                            float spaceWidth = currentLine.Length > 0 ? font.MeasureText(" ") : 0;
                            if (currentWidth + spaceWidth + wordWidth > wrapWidth && currentLine.Length > 0)
                            {
                                wrappedLines.Add((currentLine.ToString(), currentWidth));
                                if (currentWidth > maxLineWidth) maxLineWidth = currentWidth;
                                totalHeight += font.Spacing;
                                currentLine = new StringBuilder(word);
                                currentWidth = wordWidth;
                            }
                            else
                            {
                                if (currentLine.Length > 0)
                                {
                                    currentLine.Append(' ');
                                    currentWidth += spaceWidth;
                                }
                                currentLine.Append(word);
                                currentWidth += wordWidth;
                            }
                        }
                        if (currentLine.Length > 0)
                        {
                            wrappedLines.Add((currentLine.ToString(), currentWidth));
                            if (currentWidth > maxLineWidth) maxLineWidth = currentWidth;
                            totalHeight += font.Spacing;
                        }
                    }
                }

                int bmpW = Math.Max((int)Math.Ceiling(Math.Min(maxLineWidth, wrapWidth) + padding * 2), 200);
                int bmpH = Math.Max((int)Math.Ceiling(totalHeight + padding * 2), 50);

                using var bitmap = new SkiaSharp.SKBitmap(bmpW, bmpH, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
                using var canvas = new SkiaSharp.SKCanvas(bitmap);
                canvas.Clear(SkiaSharp.SKColors.Transparent);

                float y = padding + font.Spacing;

                // ── 连字渲染：如果字体支持连字且用户启用了，用 HarfBuzz shaping ──
                bool useLigature = IsLigatureEnabled && _fontSupportsLigature;

                if (useLigature)
                {
                    // 创建 HarfBuzz font — 用 GCHandle pinned 的 font data 构造 Blob
                    var hbHandle = System.Runtime.InteropServices.GCHandle.Alloc(fontData, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    {
                        using var hbBlob = new HarfBuzzSharp.Blob(hbHandle.AddrOfPinnedObject(), fontData.Length, HarfBuzzSharp.MemoryMode.Duplicate);
                        using var hbFace = new HarfBuzzSharp.Face(hbBlob, 0);
                        float upem = hbFace.UnitsPerEm;
                        using var hbFont = new global::HarfBuzzSharp.Font(hbFace);
                        hbFont.SetScale((int)upem, (int)upem);

                        foreach (var (line, _) in wrappedLines)
                        {
                            using var buffer = new HarfBuzzSharp.Buffer();
                            buffer.AddUtf8(line);
                            buffer.GuessSegmentProperties();
                            // 开启全部 4 个连字相关 feature：calt(Fira Code)/liga/dlig/clig
                            var enableLigFeatures = new[]
                            {
                                new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("calt"), 1),
                                new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("liga"), 1),
                                new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("dlig"), 1),
                                new HarfBuzzSharp.Feature(HarfBuzzSharp.Tag.Parse("clig"), 1),
                            };
                            hbFont.Shape(buffer, enableLigFeatures);

                            var infos = buffer.GlyphInfos;
                            var positions = buffer.GlyphPositions;

                            int glyphCount = infos.Length;
                            if (glyphCount == 0)
                            {
                                // 空行或无可 shaping 字形（如纯空格行）：
                                // 跳过绘制（SKTextBlobBuilder.Build() 对 0 glyph 返回 null，
                                // DrawText(null) 会抛 ArgumentNullException），仅推进行距。
                                y += font.Spacing;
                                continue;
                            }

                            var skPositions = new SkiaSharp.SKPoint[glyphCount];
                            float cx = 0;
                            float scale = FontSize / upem;
                            float baseline = y;
                            for (int i = 0; i < glyphCount; i++)
                            {
                                skPositions[i] = new SkiaSharp.SKPoint(
                                    cx + positions[i].XOffset * scale,
                                    baseline + positions[i].YOffset * scale);
                                cx += positions[i].XAdvance * scale;
                            }

                            // 使用 SKTextBlob 绘制 shaped glyphs
                            var builder = new SkiaSharp.SKTextBlobBuilder();
                            var run = builder.AllocatePositionedRun(font, glyphCount);
                            for (int i = 0; i < glyphCount; i++)
                            {
                                run.Glyphs[i] = (ushort)infos[i].Codepoint;
                                run.Positions[i] = skPositions[i];
                            }
                            using var blob = builder.Build();
                            if (blob == null)
                            {
                                // 防御：Build() 意外返回 null 时跳过绘制，避免 DrawText(null) 抛异常
                                y += font.Spacing;
                                continue;
                            }
                            canvas.DrawText(blob, padding, 0, paint);
                            y += font.Spacing;
                        }
                    }
                    finally
                    {
                        hbHandle.Free();
                    }
                }
                else
                {
                    // ── 无连字：走 DrawText ──
                    foreach (var (line, _) in wrappedLines)
                    {
                        canvas.DrawText(line, padding, y, SkiaSharp.SKTextAlign.Left, font, paint);
                        y += font.Spacing;
                    }
                }

                // ── 直接内存拷贝：SKBitmap → WriteableBitmap（跳过 PNG 编解码） ──
                int stride = bmpW * 4; // BGRA8888, 4 bytes/pixel
                int totalBytes = stride * bmpH;
                var pixelData = new byte[totalBytes];
                Marshal.Copy(bitmap.GetPixels(), pixelData, 0, totalBytes);

                var writeableBmp = new global::Avalonia.Media.Imaging.WriteableBitmap(
                    new global::Avalonia.PixelSize(bmpW, bmpH),
                    new global::Avalonia.Vector(96, 96),
                    global::Avalonia.Platform.PixelFormat.Bgra8888,
                    global::Avalonia.Platform.AlphaFormat.Premul);

                using var locked = writeableBmp.Lock();
                Marshal.Copy(pixelData, 0, locked.Address, totalBytes);

                PreviewImage = writeableBmp;
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"[FONT] SkiaSharp render failed: {ex.Message}");
        }

        if (PreviewImage == null)
        {
            // 渲染失败时回退显示原始样本文本（未过滤），避免显示空文本导致空白预览
            TextContent = originalSampleText;
        }
        else
        {
            TextContent = string.Empty;
        }
    }

    /// <summary>
    /// 字体预览的 SkiaSharp 渲染失败时是否显示回退文本。
    /// </summary>
    public bool IsFontTextFallbackVisible => PreviewType == PreviewType.Font && PreviewImage == null;

    // ── Audio ──

    /// <summary>
    /// 显示音频元数据信息。
    /// </summary>
    public void ShowAudio(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        FileFormatInfo? info = ext switch
        {
            ".flac" => FlacParser.Parse(filePath),
            ".wav" => RiffParser.Parse(filePath),
            ".mp3" => Id3v2Parser.Parse(filePath),
            _ => null
        };
        if (info == null)
        {
            ShowUnsupported(LocalizationManager.T("Preview_AudioFailed"));
            return;
        }
        PreviewType = PreviewType.Audio;
        IsPreviewVisible = true;
        IsToolbarVisible = false;
        PreviewHeaderText = LocalizationManager.T("Preview_Header_Audio");
        var audioFormatValues = new Dictionary<string, string?>();
        if (info.Duration.HasValue)
            audioFormatValues[MetadataKeys.Duration] = info.Duration.Value.ToString(@"mm\:ss");
        if (info.SampleRate.HasValue)
            audioFormatValues[MetadataKeys.SampleRate] = $"{info.SampleRate} Hz";
        if (info.Channels.HasValue)
            audioFormatValues[MetadataKeys.Channels] = info.Channels.Value.ToString();
        if (info.Bitrate.HasValue)
            audioFormatValues[MetadataKeys.Bitrate] = $"{info.Bitrate} kbps";
        if (info.BitDepth.HasValue)
            audioFormatValues[MetadataKeys.BitDepth] = $"{info.BitDepth}-bit";
        if (info.Artist != null)
            audioFormatValues[MetadataKeys.Artist] = info.Artist;
        if (info.Album != null)
            audioFormatValues[MetadataKeys.Album] = info.Album;
        MetadataHelper.RenderFormatToViewModel(this, audioFormatValues, "audio");
    }

    // ── SQLite ──

    private void LoadSqliteTable(string filePath, string tableName)
    {
        using var conn = new SqliteConnection($"Data Source={filePath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT 100";
        using var reader = cmd.ExecuteReader();

        var table = new DataTable();
        for (int i = 0; i < reader.FieldCount && i < 100; i++)
            table.Columns.Add(reader.GetName(i));

        while (reader.Read())
        {
            var row = table.NewRow();
            for (int i = 0; i < reader.FieldCount && i < 100; i++)
                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            table.Rows.Add(row);
        }
        _currentSqliteTable = table;
        SqliteTableData = table.DefaultView;
    }

    partial void OnSelectedTableIndexChanged(int value)
    {
        if (value >= 0 && value < SqliteTableNames.Count && !string.IsNullOrEmpty(_lastPreviewFilePath))
        {
            LoadSqliteTable(_lastPreviewFilePath, SqliteTableNames[value]);
        }
    }

    /// <summary>
    /// 显示 SQLite 数据库预览。
    /// </summary>
    public void ShowSqlitePreview(string filePath)
    {
        try
        {
            _lastPreviewFilePath = filePath;

            using var conn = new SqliteConnection($"Data Source={filePath};Pooling=False");
            conn.Open();

            // 获取所有表名
            var tables = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    tables.Add(reader.GetString(0));
            }

            SqliteTableNames = new ObservableCollection<string>(tables);

            // 加载第一个表
            if (tables.Count > 0)
            {
                SelectedTableIndex = 0;
                LoadSqliteTable(filePath, tables[0]);
            }

            PreviewType = PreviewType.Sqlite;
            IsPreviewVisible = true;
            IsToolbarVisible = false;
            PreviewHeaderText = LocalizationManager.T("Preview_Header_Sqlite");
            var sqliteFormatValues = new Dictionary<string, string?>
            {
                [MetadataKeys.TableCount] = tables.Count.ToString(),
            };
            MetadataHelper.RenderFormatToViewModel(this, sqliteFormatValues, "sqlite");
        }
        catch (Exception ex)
        {
            ShowUnsupported(LocalizationManager.T("Preview_SqliteFailed", ex.Message));
        }
    }

    // ── ISO ──

    /// <summary>
    /// 显示光盘镜像元数据。
    /// </summary>
    public void ShowIso(string filePath)
    {
        var info = IsoParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported(LocalizationManager.T("Preview_IsoFailed"));
            return;
        }
        PreviewType = PreviewType.Iso;
        IsPreviewVisible = true;
        IsToolbarVisible = false;
        PreviewHeaderText = LocalizationManager.T("Preview_Header_Iso");
        var isoFormatValues = new Dictionary<string, string?>
        {
            [MetadataKeys.VolumeLabel] = info.VolumeLabel,
            [MetadataKeys.IsoFormat] = info.DisplayName,
        };
        if (info.DiskSize.HasValue)
            isoFormatValues[MetadataKeys.TotalSize] = FormatFileSize(info.DiskSize.Value);
        MetadataHelper.RenderFormatToViewModel(this, isoFormatValues, "iso");
    }

    // ── Torrent ──

    /// <summary>
    /// 显示 BT 种子元数据与文件列表。
    /// </summary>
    public void ShowTorrent(string filePath)
    {
        var info = TorrentParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported(LocalizationManager.T("Preview_TorrentFailed"));
            return;
        }
        PreviewType = PreviewType.Torrent;
        IsPreviewVisible = true;
        IsToolbarVisible = false;
        PreviewHeaderText = info.TorrentFileName ?? LocalizationManager.T("Preview_Header_Torrent");
        var torrentFormatValues = new Dictionary<string, string?>();
        torrentFormatValues[MetadataKeys.TorrentFileName] = info.TorrentFileName;
        if (info.InfoHashV1 != null)
            torrentFormatValues[MetadataKeys.InfoHash] = info.InfoHashV1;
        if (info.FileCount.HasValue)
            torrentFormatValues[MetadataKeys.FileCount] = info.FileCount.Value.ToString();
        if (info.TorrentTotalSize.HasValue)
            torrentFormatValues[MetadataKeys.TotalSize] = FormatFileSize(info.TorrentTotalSize.Value);
        if (info.IsPrivate == true)
            torrentFormatValues[MetadataKeys.IsPrivate] = LocalizationManager.T("MsgBox_Yes");
        if (info.CreatedBy != null)
            torrentFormatValues[MetadataKeys.CreatedBy] = info.CreatedBy;
        if (info.MagnetLink != null)
            torrentFormatValues[MetadataKeys.MagnetLink] = info.MagnetLink;
        if (info.TrackerUrl != null)
            torrentFormatValues[MetadataKeys.TrackerUrl] = info.TrackerUrl;
        if (info.TrackerCount.HasValue && info.TrackerCount.Value > 1)
            torrentFormatValues[MetadataKeys.TrackerCount] = info.TrackerCount.Value.ToString();
        if (info.CreationDate != null)
            torrentFormatValues[MetadataKeys.CreatedDate] = info.CreationDate.Value.ToString("yyyy-MM-dd HH:mm:ss");
        if (!string.IsNullOrEmpty(info.AdditionalInfo))
            torrentFormatValues[MetadataKeys.AdditionalInfo] = info.AdditionalInfo;
        MetadataHelper.RenderFormatToViewModel(this, torrentFormatValues, "torrent");

        // 种子内文件列表（目录树结构）
        TorrentTreeRoots = new ObservableCollection<TorrentTreeNode>(
            info.TorrentFileEntries != null ? BuildTorrentTree(info.TorrentFileEntries) : []);
    }

    /// <summary>
    /// 从扁平的 (路径, 大小) 列表构建目录树。
    /// </summary>
    private static List<TorrentTreeNode> BuildTorrentTree(List<(string Path, long Size)> entries)
    {
        var rootNodes = new List<TorrentTreeNode>();
        var dirLookup = new Dictionary<string, TorrentTreeNode>();

        foreach (var (path, size) in entries)
        {
            var parts = path.Split('/');
            TorrentTreeNode? parent = null;
            string currentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                currentPath = i == 0 ? parts[i] : currentPath + "/" + parts[i];

                if (i == parts.Length - 1)
                {
                    // 叶子文件
                    var fileNode = new TorrentTreeNode
                    {
                        Name = parts[i],
                        IsDirectory = false,
                        Size = size,
                    };
                    if (parent != null)
                        parent.Children.Add(fileNode);
                    else
                        rootNodes.Add(fileNode);
                }
                else
                {
                    // 目录节点
                    if (!dirLookup.TryGetValue(currentPath, out var dirNode))
                    {
                        dirNode = new TorrentTreeNode
                        {
                            Name = parts[i],
                            IsDirectory = true,
                        };
                        dirLookup[currentPath] = dirNode;
                        if (parent != null)
                            parent.Children.Add(dirNode);
                        else
                            rootNodes.Add(dirNode);
                    }
                    parent = dirNode;
                }
            }
        }

        return rootNodes;
    }

    // ── Office ──

    /// <summary>
    /// 显示 Office 文档元数据。
    /// </summary>
    public void ShowOffice(string filePath)
    {
        var info = OfficeParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported(LocalizationManager.T("Preview_OfficeFailed"));
            return;
        }
        PreviewType = PreviewType.Office;
        IsPreviewVisible = true;
        IsToolbarVisible = false;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        PreviewHeaderText = ext switch
        {
            ".docx" => LocalizationManager.T("Preview_OfficeDocx"),
            ".xlsx" => LocalizationManager.T("Preview_OfficeXlsx"),
            ".pptx" => LocalizationManager.T("Preview_OfficePptx"),
            _ => LocalizationManager.T("Preview_OfficeGeneric")
        };
        FormatMetadata.Clear();
    }

    // ── DOCX ──

    /// <summary>
    /// 显示 DOCX 文档大纲 + 全文（左右分栏布局）。
    /// 标题检测采用三种方式（满足任一即视为标题）：
    /// 1. StyleId 以 "Heading" 开头（不区分大小写）
    /// 2. 样式的显示名称（StyleName）包含 "heading" 或 "标题"
    /// 3. ParagraphProperties.OutlineLevel 已设置（比样式更可靠）
    /// </summary>
    public void ShowDocx(string filePath)
    {
        try
        {
            // 大文件保护
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Exists && fileInfo.Length > 50 * 1024 * 1024)
            {
                ShowUnsupported(LocalizationManager.T("Preview_DocxTooLarge"));
                return;
            }

            using var doc = WordprocessingDocument.Open(filePath, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
            {
                ShowUnsupported(LocalizationManager.T("Preview_DocxEmpty"));
                return;
            }

            // 构建标题样式 ID 集合（不区分大小写）：
            // - StyleId 以 "Heading" 开头的样式
            // - 显示名称（StyleName）包含 "heading" 或 "标题" 的样式
            var headingStyleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stylesPart = doc.MainDocumentPart?.StyleDefinitionsPart;
            if (stylesPart?.Styles != null)
            {
                foreach (var style in stylesPart.Styles.Descendants<DocumentFormat.OpenXml.Wordprocessing.Style>())
                {
                    var sid = style.StyleId?.Value;
                    if (sid == null) continue;

                    // 直接匹配 StyleId
                    if (sid.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
                    {
                        headingStyleIds.Add(sid);
                        continue;
                    }

                    // 匹配显示名称
                    var nameVal = style.Descendants<StyleName>()
                        .FirstOrDefault()?.Val?.Value;
                    if (nameVal != null)
                    {
                        var nameLower = nameVal.ToLowerInvariant();
                        if (nameLower.Contains("heading") || nameLower.Contains("标题"))
                            headingStyleIds.Add(sid);
                    }
                }
            }

            var outline = new List<DocxOutlineItem>();
            // 全文控件树：段落与表格按文档顺序交替排列
            var content = new StackPanel { Spacing = 6 };

            // 按文档顺序遍历 body 子元素（段落与表格交替出现，需保持顺序）。
            // 表格渲染为真正的 Grid 网格（带边框），不再用 | 分隔符模拟。
            foreach (var element in body.ChildElements)
            {
                if (element is Paragraph para)
                {
                    AppendDocxParagraph(para, headingStyleIds, outline, content);
                }
                else if (element is Table table)
                {
                    AppendDocxTable(table, content);
                }
            }

            DocxOutline = new ObservableCollection<DocxOutlineItem>(outline);
            DocxContentPanel = content;
            DocxNoOutlineText = outline.Count > 0 ? string.Empty : LocalizationManager.T("Preview_DocxNoOutline");

            if (content.Children.Count == 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = LocalizationManager.T("Preview_DocxEmpty"),
                    Foreground = GetThemeBrush("ThemeTextSecondaryBrush"),
                    TextWrapping = TextWrapping.Wrap,
                });
                DocxNoOutlineText = string.Empty;
            }

            PreviewType = PreviewType.Docx;
            IsPreviewVisible = true;
            IsToolbarVisible = false;
        }
        catch (Exception ex)
        {
            App.DebugLog($"ShowDocx failed: {ex.Message}");
            ShowUnsupported(LocalizationManager.T("Preview_DocxFailed"));
        }
    }

    /// <summary>
    /// 将单个 DOCX 段落追加为全文控件树中的 TextBlock，并检测标题写入大纲。
    /// 标题检测三路：StyleId 集合匹配 / OutlineLevel 显式属性。
    /// </summary>
    private static void AppendDocxParagraph(
        Paragraph para,
        HashSet<string> headingStyleIds,
        List<DocxOutlineItem> outline,
        Panel content)
    {
        var text = string.Concat(para.Descendants<Text>().Select(t => t.Text ?? string.Empty));
        // 记录该段落块在控件树中的索引（大纲跳转用）
        int blockIndex = content.Children.Count;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var paraProps = para.ParagraphProperties;
        var styleId = paraProps?.ParagraphStyleId?.Val?.Value;

        // 标题检测方式 1: StyleId 在 headingStyleIds 集合中
        bool isHeading = styleId != null && headingStyleIds.Contains(styleId);
        int level = 1;
        if (isHeading && styleId != null)
        {
            // 从 styleId 提取尾随数字（"Heading1"→1, "heading2"→2, "标题 1"→1）
            var numStr = new string(styleId.SkipWhile(c => !char.IsDigit(c))
                                           .TakeWhile(char.IsDigit)
                                           .ToArray());
            if (int.TryParse(numStr, out var parsedLevel))
                level = parsedLevel;
        }

        // 标题检测方式 2: OutlineLevel（覆盖样式检测，因为显式设置的大纲级别更可靠）
        // OutlineLevel 在 OpenXml 中：1=Heading1 ... 9=Heading9
        var outlineLevelVal = paraProps?.OutlineLevel?.Val?.Value;
        if (outlineLevelVal.HasValue && outlineLevelVal.Value > 0 && outlineLevelVal.Value <= 9)
        {
            isHeading = true;
            level = outlineLevelVal.Value;
        }

        if (isHeading)
        {
            level = Math.Clamp(level, 1, 6);
            outline.Add(new DocxOutlineItem
            {
                Text = text,
                Level = level,
                BlockIndex = blockIndex
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
        });
    }

    /// <summary>
    /// 将 DOCX 表格渲染为全文控件树中的 Grid 网格（带边框/底色），保留文档顺序。
    /// 列数取最宽行；合并单元格（GridSpan/VerticalMerge）按存在单元格计数，忽略其布局合并。
    /// </summary>
    private static void AppendDocxTable(Table table, Panel content)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0) return;

        // 列数 = 最宽行的单元格数
        int colCount = rows.Max(r => r.Elements<TableCell>().Count());
        if (colCount == 0) return;

        var borderBrush = GetThemeBrush("ThemeBorderBrush");
        var cellBg = GetThemeBrush("ThemeSurfaceBgBrush");

        var grid = new Grid
        {
            Margin = new Thickness(0, 2),
            ClipToBounds = true,
        };

        // 列宽按内容自适应（Auto）：内容短的列收缩、内容长的列自然变宽。
        // 单元格 MaxWidth 限制单列最大宽度，超长内容换行而非无限撑宽表格。
        for (int i = 0; i < colCount; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        int rowIdx = 0;
        foreach (var row in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cells = row.Elements<TableCell>().ToList();

            for (int colIdx = 0; colIdx < colCount; colIdx++)
            {
                string cellText = colIdx < cells.Count
                    ? string.Concat(cells[colIdx].Descendants<Text>().Select(t => t.Text ?? string.Empty)).Trim()
                    : string.Empty;

                var cellBorder = new global::Avalonia.Controls.Border
                {
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    Background = cellBg,
                    Padding = new Thickness(6, 3),
                    Child = new TextBlock
                    {
                        Text = cellText,
                        TextWrapping = TextWrapping.Wrap,
                        // 限制单列最大宽度，配合 Auto 列宽：长内容换行而非无限撑宽表格
                        MaxWidth = 400,
                    },
                };
                // TextBlock 前景色继承自内容面板视觉祖先，无需显式设置
                Grid.SetRow(cellBorder, rowIdx);
                Grid.SetColumn(cellBorder, colIdx);
                grid.Children.Add(cellBorder);
            }
            rowIdx++;
        }

        content.Children.Add(grid);
    }

    // ── XLSX ──

    /// <summary>
    /// 显示 XLSX 工作表预览（ClosedXML → DataGrid）。
    /// </summary>
    public void ShowXlsx(string filePath)
    {
        XLWorkbook? workbook = null;
        try
        {
            workbook = new XLWorkbook(filePath);
            var ws = workbook.Worksheet(1);
            var range = ws.RangeUsed();

            if (range == null)
            {
                TextContent = LocalizationManager.T("Preview_XlsxEmpty");
                PreviewType = PreviewType.Xlsx;
                IsPreviewVisible = true;
                IsToolbarVisible = false;
                return;
            }

            var table = new DataTable();
            var firstRow = range.FirstRow().CellsUsed().Take(100).ToList();

            // Column headers from first row
            for (int i = 0; i < firstRow.Count; i++)
            {
                var colName = firstRow[i].GetFormattedString();
                if (string.IsNullOrWhiteSpace(colName))
                    colName = $"Column{i + 1}";
                // Ensure unique column names
                var uniqueName = colName;
                int suffix = 1;
                while (table.Columns.Contains(uniqueName))
                    uniqueName = $"{colName}_{suffix++}";
                table.Columns.Add(uniqueName);
            }

            if (table.Columns.Count == 0)
            {
                TextContent = LocalizationManager.T("Preview_XlsxEmpty");
                PreviewType = PreviewType.Xlsx;
                IsPreviewVisible = true;
                IsToolbarVisible = false;
                return;
            }

            // Data rows (limit 100)
            int rowCount = 0;
            foreach (var row in range.RowsUsed().Skip(1))
            {
                if (rowCount >= 100) break;
                var dataRow = table.NewRow();
                for (int i = 0; i < table.Columns.Count && i < row.CellsUsed().Count(); i++)
                {
                    dataRow[i] = row.Cell(i + 1).GetFormattedString();
                }
                table.Rows.Add(dataRow);
                rowCount++;
            }

            _xlsxDataTable = table;
            XlsxData = table.DefaultView;

            PreviewType = PreviewType.Xlsx;
            IsPreviewVisible = true;
            IsToolbarVisible = false;
        }
        catch (Exception ex)
        {
            App.DebugLog($"ShowXlsx failed: {ex.Message}");
            if (ex.Message.Contains("password") || ex.Message.Contains("protected"))
                ShowUnsupported(LocalizationManager.T("Preview_XlsxProtected"));
            else
                ShowUnsupported(LocalizationManager.T("Preview_XlsxFailed"));
        }
        finally
        {
            workbook?.Dispose();
        }
    }

    // ── PPTX ──

    /// <summary>
    /// 显示 PPTX 幻灯片文本预览（手动解析 a:t 元素 + Canvas 按形状位置定位）。
    /// 每张幻灯片一个 <see cref="PptxSlideModel"/>，文本项按 a:sp 形状的 xfrm 坐标
    /// 缩放映射到固定宽度 Canvas（比例来自 presentation.xml 的 sldSz，默认 16:9）。
    /// 翻页通过 PptxCurrentSlide / PptxPreviousSlideCommand / PptxNextSlideCommand 控制。
    /// </summary>
    public void ShowPptx(string filePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var slideEntries = archive.Entries
                .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                         && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName)
                .ToList();

            if (slideEntries.Count == 0)
            {
                // 空演示文稿：PptxSlides 为空，Canvas 面板显示 Preview_PptxEmpty 占位
                PptxSlides.Clear();
                PptxTotalSlides = 0;
                PptxCurrentSlide = 1;
                CurrentSlideItems = null;
                PreviewType = PreviewType.Pptx;
                IsPreviewVisible = true;
                IsToolbarVisible = false;
                return;
            }

            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";

            // 读取幻灯片尺寸（EMU）。默认 10" × 7.5" (12192000 × 9144000 EMU)
            long slideWidthEmu = 12192000, slideHeightEmu = 9144000;
            var presEntry = archive.GetEntry("ppt/presentation.xml");
            if (presEntry != null)
            {
                try
                {
                    using var presStream = presEntry.Open();
                    var presDoc = XDocument.Load(presStream);
                    var sldSz = presDoc.Descendants(p + "sldSz").FirstOrDefault();
                    if (sldSz != null)
                    {
                        if (long.TryParse(sldSz.Attribute("cx")?.Value, out var cx) && cx > 0)
                            slideWidthEmu = cx;
                        if (long.TryParse(sldSz.Attribute("cy")?.Value, out var cy) && cy > 0)
                            slideHeightEmu = cy;
                    }
                }
                catch (Exception ex)
                {
                    App.DebugLog($"ShowPptx: failed to read presentation.xml size: {ex.Message}");
                }
            }

            // 缩放比例：固定 Canvas 宽度（默认 960），高度按 slide 比例
            const double canvasWidth = 960;
            double scaleX = canvasWidth / slideWidthEmu;
            double canvasHeight = Math.Round(slideHeightEmu * scaleX);

            var slides = new List<PptxSlideModel>();
            int slideNumber = 0;

            foreach (var entry in slideEntries)
            {
                slideNumber++;
                var model = new PptxSlideModel { SlideNumber = slideNumber };
                try
                {
                    using var stream = entry.Open();
                    var slideDoc = XDocument.Load(stream);

                    // 每个 p:sp 形状：位置 (xfrm/off) + 文本 (a:t)
                    // 注意：形状元素 sp 属于 presentationml 命名空间（p），
                    // 其内部属性 xfrm/p/t/rPr 才属于 drawingml（a）
                    foreach (var shape in slideDoc.Descendants(p + "sp"))
                    {
                        var xfrm = shape.Descendants(a + "xfrm").FirstOrDefault();
                        if (xfrm == null) continue;

                        var off = xfrm.Descendants(a + "off").FirstOrDefault();
                        double x = 0, y = 0;
                        if (off != null)
                        {
                            double.TryParse(off.Attribute("x")?.Value, out x);
                            double.TryParse(off.Attribute("y")?.Value, out y);
                        }

                        // 形状内所有 a:t 文本（保留段落间换行）
                        var text = string.Join("\n",
                            shape.Descendants(a + "p")
                                .Select(pPara => string.Concat(pPara.Descendants(a + "t").Select(t => t.Value)))
                                .Where(v => !string.IsNullOrWhiteSpace(v)));
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        // 字体属性：首个 run 的 rPr（sz 单位 = 1/100 pt，b="1" 加粗）
                        double fontSize = 14;
                        bool isBold = false;
                        var rPr = shape.Descendants(a + "rPr").FirstOrDefault();
                        if (rPr != null)
                        {
                            if (double.TryParse(rPr.Attribute("sz")?.Value, out var sz) && sz > 0)
                                fontSize = sz / 100.0;
                            isBold = rPr.Attribute("b")?.Value == "1";
                        }

                        model.TextItems.Add(new PptxTextItem
                        {
                            Text = text,
                            X = Math.Round(x * scaleX),
                            Y = Math.Round(y * scaleX),
                            FontSize = Math.Clamp(fontSize, 8, 72),
                            IsBold = isBold,
                        });
                    }

                    // 按 y（从上到下）再按 x（从左到右）排序
                    model.TextItems.Sort((i1, i2) =>
                    {
                        int cmp = i1.Y.CompareTo(i2.Y);
                        return cmp != 0 ? cmp : i1.X.CompareTo(i2.X);
                    });
                }
                catch (Exception ex)
                {
                    App.DebugLog($"ShowPptx: failed to parse slide {slideNumber}: {ex.Message}");
                }
                slides.Add(model);
            }

            PptxSlides = new ObservableCollection<PptxSlideModel>(slides);
            PptxTotalSlides = slides.Count;
            PptxCanvasWidth = canvasWidth;
            PptxCanvasHeight = canvasHeight;
            PptxCurrentSlide = 1;
            CurrentSlideItems = slides.FirstOrDefault()?.TextItems;

            PreviewType = PreviewType.Pptx;
            IsPreviewVisible = true;
            IsToolbarVisible = false;
        }
        catch (Exception ex)
        {
            App.DebugLog($"ShowPptx failed: {ex.Message}");
            ShowUnsupported(LocalizationManager.T("Preview_PptxFailed"));
        }
    }

    // ── PDF ──

    private PdfDocument? _pdfDocument;
    private int _pdfTotalPages;
    /// <summary>PDF 页面渲染缩放比例，限制大页面位图尺寸。</summary>
    private float _pdfRenderScale = 1.0f;

    [ObservableProperty]
    private int _pdfCurrentPage = 1;

    [ObservableProperty]
    private string _pdfPageInfo = string.Empty;

    partial void OnPdfCurrentPageChanged(int value)
    {
        if (_pdfDocument != null && value >= 1 && value <= _pdfTotalPages)
            _ = LoadPdfPageAsync(value);
    }

    [RelayCommand]
    private void PdfPreviousPage()
    {
        if (PdfCurrentPage > 1)
            PdfCurrentPage--;
    }

    [RelayCommand]
    private void PdfNextPage()
    {
        if (PdfCurrentPage < _pdfTotalPages)
            PdfCurrentPage++;
    }

    /// <summary>
    /// 显示 PDF 预览：PdfPig 解析元数据 + SkiaSharp 逐页位图渲染。
    /// 渲染在后台线程进行，避免阻塞 UI。
    /// </summary>
    public async Task ShowPdfAsync(string filePath)
    {
        // 先保留加载状态（由调用方 ShowPreviewAsync 的 ShowLoading 设置），
        // 渲染完成后再设置 PreviewType 和 UI 内容，避免空白中间状态。
        var info = PdfParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported(LocalizationManager.T("Preview_PdfParseFailed"));
            return;
        }

        try
        {
            // 后台线程：打开文档 + 渲染第一页
            _pdfDocument?.Dispose();
            _pdfDocument = null;

            (PdfDocument doc, int totalPages, float renderScale) = await Task.Run(() =>
            {
                var d = PdfDocument.Open(filePath, SkiaRenderingParsingOptions.Instance);
                d.AddSkiaPageFactory();

                // 获取第 1 页实际尺寸，计算合适的渲染缩放比例
                float scale = 1.0f;
                try
                {
                    var page = d.GetPage(1);
                    float pw = (float)page.Width;
                    float ph = (float)page.Height;
                    const float maxW = 1920f, maxH = 1080f;
                    if (pw > maxW || ph > maxH)
                        scale = Math.Min(maxW / pw, maxH / ph);
                }
                catch { /* 尺寸获取失败，用默认 1.0 */ }

                return (d, d.NumberOfPages, scale);
            });

            _pdfDocument = doc;
            _pdfTotalPages = totalPages;
            _pdfRenderScale = renderScale;
            PdfPageInfo = $"1 / {_pdfTotalPages}";

            await LoadPdfPageAsync(1);

            // 渲染完成，一次性设置 UI 状态
            PreviewType = PreviewType.Pdf;
            IsPreviewVisible = true;
            IsToolbarVisible = false;
            PreviewHeaderText = $"PDF {info.AdditionalInfo ?? ""}";
            var pdfFormatValues = new Dictionary<string, string?>();
            if (info.Title != null) pdfFormatValues[MetadataKeys.Title] = info.Title;
            if (info.Author != null) pdfFormatValues[MetadataKeys.Author] = info.Author;
            if (info.Subject != null) pdfFormatValues[MetadataKeys.Subject] = info.Subject;
            if (info.PageCount.HasValue) pdfFormatValues[MetadataKeys.PageCount] = info.PageCount.Value.ToString();
            pdfFormatValues[MetadataKeys.Encrypted] = info.IsEncrypted == true ? LocalizationManager.T("MsgBox_Yes") : LocalizationManager.T("MsgBox_No");
            if (info.CreationDate.HasValue) pdfFormatValues[MetadataKeys.CreatedDate] = info.CreationDate.Value.ToString("yyyy-MM-dd HH:mm");
            if (info.ModifiedDate.HasValue) pdfFormatValues[MetadataKeys.DocModifiedDate] = info.ModifiedDate.Value.ToString("yyyy-MM-dd HH:mm");
            MetadataHelper.RenderFormatToViewModel(this, pdfFormatValues, "pdf");
        }
        catch (Exception ex)
        {
            App.DebugLog($"[PDF] Failed to open or render PDF: {ex.Message}");
            ShowUnsupported(LocalizationManager.T("Preview_PdfOpenFailed"));
        }
    }

    private async Task LoadPdfPageAsync(int pageNumber)
    {
        if (_pdfDocument == null) return;

        byte[]? pixelData = null;
        int width = 0, height = 0;

        try
        {
            // 后台线程：渲染 + 像素数据拷贝
            await Task.Run(() =>
            {
                using var bitmap = _pdfDocument.GetPageAsSKBitmap(pageNumber, _pdfRenderScale, SKColors.White);
                if (bitmap == null) return;

                width = bitmap.Width;
                height = bitmap.Height;
                int stride = width * 4;
                int totalBytes = stride * height;
                pixelData = new byte[totalBytes];
                System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), pixelData, 0, totalBytes);
            });

            if (pixelData == null)
            {
                App.DebugLog($"[PDF] GetPageAsSKBitmap returned null for page {pageNumber}");
                return;
            }

            // UI 线程：创建 WriteableBitmap 并设置属性
            var wb = new global::Avalonia.Media.Imaging.WriteableBitmap(
                new global::Avalonia.PixelSize(width, height),
                new global::Avalonia.Vector(96, 96),
                global::Avalonia.Platform.PixelFormat.Bgra8888,
                global::Avalonia.Platform.AlphaFormat.Premul);

            using var locked = wb.Lock();
            System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, locked.Address, pixelData.Length);

            PreviewImage = wb;
            ImageWidth = width;
            ImageHeight = height;
            PdfPageInfo = $"{pageNumber} / {_pdfTotalPages}";
            PdfCurrentPage = pageNumber;
            ZoomFit();
        }
        catch (Exception ex)
        {
            App.DebugLog($"[PDF] LoadPdfPageAsync({pageNumber}) failed: {ex.Message}");
        }
    }

    // ── Video ──

    /// <summary>
    /// 显示视频元数据。
    /// </summary>
    public void ShowVideo(string filePath)
    {
        var info = VideoParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported(LocalizationManager.T("Preview_VideoFailed"));
            return;
        }
        PreviewType = PreviewType.Video;
        IsPreviewVisible = true;
        IsToolbarVisible = false;
        PreviewHeaderText = LocalizationManager.T("Preview_Header_Video");
        var videoFormatValues = new Dictionary<string, string?>();
        if (info.VideoWidth.HasValue && info.VideoHeight.HasValue)
            videoFormatValues[MetadataKeys.Resolution] = $"{info.VideoWidth} × {info.VideoHeight}";
        if (info.Duration.HasValue)
            videoFormatValues[MetadataKeys.Duration] = info.Duration.Value.ToString(@"hh\:mm\:ss");
        if (info.Codec != null)
            videoFormatValues[MetadataKeys.Codec] = info.Codec;
        if (info.Bitrate.HasValue)
            videoFormatValues[MetadataKeys.Bitrate] = $"{info.Bitrate} kbps";
        MetadataHelper.RenderFormatToViewModel(this, videoFormatValues, "video");
    }

    /// <summary>
    /// 格式化文件大小为人类可读字符串。
    /// </summary>
    private static string FormatFileSize(long bytes)
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
        return unitIndex == 0 ? $"{bytes} {units[unitIndex]}" : $"{size:F2} {units[unitIndex]}";
    }

    /// <summary>
    /// 按资源键解析主题画刷（如 "ThemeBorderBrush"）。资源缺失时返回 null，由调用方回退。
    /// 与 MarkdownPreviewBuilder.GetThemeBrush 实现一致。
    /// </summary>
    private static IBrush? GetThemeBrush(string key)
    {
        if (Application.Current is not global::Avalonia.Controls.IResourceHost host)
            return null;
        return host.TryFindResource(key, out var value) ? value as IBrush : null;
    }

    /// <summary>
    /// 显示 HTML 预览（通过 ReverseMarkdown → Markdown → 控件树）。
    /// </summary>
    public void ShowHtmlPreview(string filePath)
    {
        var html = File.ReadAllText(filePath);
        var converter = new Converter();
        var markdown = converter.Convert(html);
        var panel = MarkdownPreviewBuilder.Build(markdown);
        MarkdownPreviewPanel = panel;
        PreviewType = PreviewType.Html;
        IsPreviewVisible = true;
        IsToolbarVisible = false;
    }

    /// <summary>
    /// 显示 Markdown 预览（Markdig AST → Avalonia 控件树）。
    /// 使用 <see cref="MarkdownPreviewBuilder"/> 将 Markdown 转换为纯控件树，
    /// 替代原有的 Markdig → HTML → WebView2 管线。
    /// </summary>
    public void ShowMarkdownPreview(string filePath)
    {
        var markdown = File.ReadAllText(filePath);
        var panel = MarkdownPreviewBuilder.Build(markdown);
        MarkdownPreviewPanel = panel;
        PreviewType = PreviewType.Markdown;
        IsPreviewVisible = true;
        IsToolbarVisible = false;
    }

    /// <summary>
    /// 显示暂不支持预览提示。
    /// </summary>
    public void ShowUnsupported(string? message = null)
    {
        TextContent = message ?? LocalizationManager.T("Preview_Unsupported");
        PreviewType = PreviewType.Unsupported;
        IsPreviewVisible = true;
        IsToolbarVisible = false;
    }

    /// <summary>
    /// Phase 1 填充通用文件信息。只渲染通用 section，不碰格式 section。
    /// 格式 section 留空并设置 IsFormatPending=true，等 Phase 2 的 ShowXxx 填充。
    /// </summary>
    public void UpdateCommonMetadata(
        string fileName,
        string fileSize,
        string? compressedSize,
        string compressionRatio,
        string modifiedDate)
    {
        FileName = fileName;
        FileSize = fileSize;
        CompressedSize = compressedSize ?? string.Empty;
        CompressionRatio = compressionRatio;
        ModifiedDate = modifiedDate;
        IsInfoPanelVisible = true;

        CurrentCommonValues = new Dictionary<string, string?>
        {
            [MetadataKeys.FileName] = fileName,
            [MetadataKeys.FileSize] = fileSize,
            [MetadataKeys.CompressedSize] = compressedSize,
            [MetadataKeys.CompressionRatio] = compressionRatio,
            [MetadataKeys.FileModifiedDate] = modifiedDate,
        };

        MetadataHelper.RenderCommonToViewModel(this, CurrentCommonValues);
    }

    /// <summary>
    /// 将格式字段与 Phase 1 保存的通用字段合并，供渲染使用。
    /// 格式字段优先（可覆盖通用字段的同名键）。
    /// </summary>
    internal Dictionary<string, string?> MergeWithCommon(Dictionary<string, string?> formatValues)
    {
        var merged = new Dictionary<string, string?>();
        if (CurrentCommonValues != null)
        {
            foreach (var kv in CurrentCommonValues)
                merged[kv.Key] = kv.Value;
        }
        foreach (var kv in formatValues)
            merged[kv.Key] = kv.Value;
        return merged;
    }

    public void Clear()
    {
        PreviewType = PreviewType.None;
        TextContent = string.Empty;
        HeaderText = string.Empty;
        PeTitle = string.Empty;
        PeSubtitle = string.Empty;
        PeMetadata.Clear();
        CsvData = null;
        FormatMetadata.Clear();
        CommonSections.Clear();
        FormatSections.Clear();
        IsFormatPending = false;
        HasFormatSections = false;
        ContentTopItems.Clear();
        PreviewHeaderText = string.Empty;
        PreviewImage = null;
        ImageWidth = 0;
        ImageHeight = 0;
        _pdfDocument?.Dispose();
        _pdfDocument = null;
        _pdfTotalPages = 0;
        _pdfRenderScale = 1.0f;
        PdfCurrentPage = 1;
        PdfPageInfo = string.Empty;
        MarkdownPreviewPanel = null;
        DocxOutline.Clear();
        DocxContentPanel = null;
        DocxNoOutlineText = string.Empty;
        XlsxData = null;
        _xlsxDataTable = null;
        PptxSlides.Clear();
        PptxTotalSlides = 0;
        PptxCurrentSlide = 1;
        CurrentSlideItems = null;
        TorrentTreeRoots.Clear();
        SqliteTableData = null;
        SqliteTableNames.Clear();
        SelectedTableIndex = 0;
        _lastPreviewFilePath = null;
        StopGifTimer();
        _gifFrames = null;
        FontFamily = global::Avalonia.Media.FontFamily.Default;
        IsPreviewVisible = false;
        IsToolbarVisible = false;
        ZoomLevel = 1.0;
        FontSize = 13;
        IsTransparencyBgShown = false;
        IsFlattenAlpha = false;
        _originalPreviewImage = null;
        _skOriginalPreview?.Dispose();
        _skOriginalPreview = null;

        // Reset info panel
        FileName = string.Empty;
        FileSize = string.Empty;
        CompressedSize = string.Empty;
        CompressionRatio = string.Empty;
        ModifiedDate = string.Empty;
        IsInfoPanelVisible = false;
        IsLoadingPreview = false;
        LoadingFileName = string.Empty;
    }

    /// <summary>
    /// Switch to loading state: clear old content, show loading indicator with file name.
    /// Phase 1 of two-phase preview — called immediately when user selects a new file.
    /// </summary>
    public void ShowLoading(string? fileName = null)
    {
        // Reuse Clear() to reset all preview state, then override for loading phase.
        // This avoids duplicated reset logic — Clear() and ShowLoading() stay in sync.
        Clear();
        LoadingFileName = fileName ?? string.Empty;
        IsLoadingPreview = true;
        IsPreviewVisible = true;
    }

    private void AddPeMeta(string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            PeMetadata.Add(new PeMetadataItem { Key = key, Value = value });
        }
    }



}

/// <summary>
/// PE 元数据的键值对模型。
/// </summary>
public class PeMetadataItem
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 种子文件树节点，支持展开/折叠。
/// </summary>
public class TorrentTreeNode : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public string SizeDisplay => IsDirectory ? string.Empty : FormatUtil.FormatSize(Size);
    public ObservableCollection<TorrentTreeNode> Children { get; set; } = [];

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// DOCX 文档大纲条目，用于左右分栏预览。
/// BlockIndex 指向全文控件树（DocxContentPanel.Children）中的块索引，用于点击跳转。
/// </summary>
public class DocxOutlineItem
{
    public string Text { get; set; } = string.Empty;
    public int Level { get; set; }
    public int BlockIndex { get; set; }
    public global::Avalonia.Thickness Indent => new global::Avalonia.Thickness((Level - 1) * 20, 2, 0, 2);
}

/// <summary>
/// PPTX 单张幻灯片的文本项集合（Canvas 按位置渲染）。
/// </summary>
public class PptxSlideModel
{
    public int SlideNumber { get; set; }
    public List<PptxTextItem> TextItems { get; set; } = [];
}

/// <summary>
/// PPTX 幻灯片内的单个文本形状（已按 Canvas 缩放比例转换坐标）。
/// </summary>
public class PptxTextItem
{
    public string Text { get; set; } = string.Empty;
    /// <summary>Canvas 左上角 X 坐标（px）。</summary>
    public double X { get; set; }
    /// <summary>Canvas 左上角 Y 坐标（px）。</summary>
    public double Y { get; set; }
    public double FontSize { get; set; } = 14;
    public bool IsBold { get; set; }
}
