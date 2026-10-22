using Avalonia.Controls;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// 压缩包内容区（目录树 / 文件列表 / 预览面板 / 拖拽添加覆层）。
///
/// 2026-10-21 启动优化：自 <c>MainWindow.axaml</c> 抽出为独立 UserControl，**按需创建**——
/// 未加载压缩包时内容区不可见，启动路径无需实例化这 ~153 个元素
/// （由 <c>MainWindow.EnsureArchiveContent()</c> 在 <c>IsArchiveLoaded</c> 首次为 true 时创建）。
///
/// 根 Grid 即拆分前的 <c>ArchiveContentGrid</c>（列/行定义与子元素坐标原样保留）→ 布局零变化。
/// 本类只负责**公开具名控件**（供 MainWindow 的布局数学 / 列状态 / 拖拽覆层逻辑访问），
/// 业务逻辑仍在 MainWindow 内（XAML 事件处理器在 MainWindow 侧订阅，见 EnsureArchiveContent）。
///
/// 注：属性名统一加 <c>Control</c> 后缀，避免与 XAML 编译器为 <c>x:Name</c> 生成的字段重名。
/// </summary>
public partial class ArchiveContentView : UserControl
{
    public ArchiveContentView()
    {
        InitializeComponent();
    }

    // ── 具名控件（x:Name 生成字段为 private，这里公开只读入口）──

    /// <summary>内容区根 Grid（列/行定义用于预览面板布局数学）。</summary>
    public Grid ContentGrid => ArchiveContentGrid;

    /// <summary>文件列表 DataGrid。</summary>
    public DataGrid FileListGridControl => FileListGrid;

    /// <summary>目录树面板。</summary>
    public Border FolderTreeBorderControl => FolderTreeBorder;

    /// <summary>目录树 / 文件列表之间的竖向分隔条。</summary>
    public GridSplitter TreeFileSplitterControl => TreeFileSplitter;

    /// <summary>文件列表面板。</summary>
    public Grid FileListPanelControl => FileListPanel;

    /// <summary>目录树搜索框。</summary>
    public TextBox TreeSearchBoxControl => TreeSearchBox;

    /// <summary>预览面板宿主（延迟创建预览面板用）。</summary>
    public ContentControl PreviewPanelHostControl => PreviewPanelHost;

    /// <summary>预览面板横向分隔条。</summary>
    public GridSplitter PreviewRowSplitterControl => PreviewRowSplitter;

    /// <summary>预览面板纵向分隔条。</summary>
    public GridSplitter PreviewColSplitterControl => PreviewColSplitter;

    /// <summary>拖拽添加覆层（绿色可添加 / 红色不支持）。</summary>
    public Border DragAddOverlayControl => DragAddOverlay;

    /// <summary>拖拽添加覆层背景。</summary>
    public Border DragAddOverlayBgControl => DragAddOverlayBg;

    /// <summary>拖拽添加覆层图标（✓ / ⚠）。</summary>
    public TextBlock DragAddOverlayIconControl => DragAddOverlayIcon;

    /// <summary>拖拽添加覆层文字。</summary>
    public TextBlock DragAddOverlayTextControl => DragAddOverlayText;

    // ── 列头排序箭头（MainWindow 的 UpdateSortArrows 用）──

    /// <summary>名称列排序箭头。</summary>
    public TextBlock NameHeaderArrowControl => NameHeaderArrow;

    /// <summary>大小列排序箭头。</summary>
    public TextBlock SizeHeaderArrowControl => SizeHeaderArrow;

    /// <summary>压缩后大小列排序箭头。</summary>
    public TextBlock CompressedSizeHeaderArrowControl => CompressedSizeHeaderArrow;

    /// <summary>压缩率列排序箭头。</summary>
    public TextBlock RatioHeaderArrowControl => RatioHeaderArrow;

    /// <summary>修改时间列排序箭头。</summary>
    public TextBlock LastModifiedArrowControl => LastModifiedHeaderArrow;
}
