using System;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Views;

public partial class PreviewPanel : UserControl
{
    private PreviewViewModel? _vm;
    private CancellationTokenSource? _resizeDebounceCts;

    public PreviewPanel()
    {
        InitializeComponent();

        this.DataContextChanged += OnDataContextChanged;
        // FontPreviewScrollViewer 在 InitializeComponent 后可用，只订阅一次
        if (FontPreviewScrollViewer != null)
            FontPreviewScrollViewer.SizeChanged += OnFontPreviewScrollerSizeChanged;

        // 订阅内容区域外层 ScrollViewer 的 SizeChanged，用于 ZoomFit 自适应视口
        if (PreviewContentScroller != null)
            PreviewContentScroller.SizeChanged += OnContentScrollerSizeChanged;

        // 订阅 contentTop 横条的 SizeChanged：横条高度变化（字段增删/换行）不会触发
        // 外层 ScrollViewer 的 SizeChanged，但会改变图像的可用视口高度，必须单独重算
        if (ContentTopBorder != null)
            ContentTopBorder.SizeChanged += OnContentTopSizeChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // 清理旧 VM 的订阅，防止重复订阅和内存泄漏
        if (_vm != null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        if (this.DataContext is PreviewViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
            ApplyInfoPanelOrientation(vm.InfoPanelOrientation);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        var vm = _vm;
        if (vm == null) return;
        if (args.PropertyName == nameof(PreviewViewModel.InfoPanelOrientation))
        {
            ApplyInfoPanelOrientation(vm.InfoPanelOrientation);
        }
        // CSV/SQLite: DataView 在 Avalonia DataGrid 中无法自动生成正确列，
        // 数据源变化或切换可见时都需要手动设置列。
        bool csvDataChanged = args.PropertyName == nameof(PreviewViewModel.IsCsvVisible)
                           || args.PropertyName == nameof(PreviewViewModel.CsvData);
        if (csvDataChanged && vm.IsCsvVisible)
        {
            SetupDataGridColumns(CsvDataGrid, vm.CsvDataTable);
        }
        bool sqliteDataChanged = args.PropertyName == nameof(PreviewViewModel.IsSqliteVisible)
                              || args.PropertyName == nameof(PreviewViewModel.SqliteTableData);
        if (sqliteDataChanged && vm.IsSqliteVisible)
        {
            SetupDataGridColumns(SqliteDataGrid, vm.CurrentSqliteTable);
        }
        bool xlsxDataChanged = args.PropertyName == nameof(PreviewViewModel.IsXlsxVisible)
                            || args.PropertyName == nameof(PreviewViewModel.XlsxData);
        if (xlsxDataChanged && vm.IsXlsxVisible)
        {
            SetupDataGridColumns(XlsxDataGrid, vm.XlsxDataTable);
        }
        bool pptxChanged = args.PropertyName == nameof(PreviewViewModel.IsPptxVisible)
                        || args.PropertyName == nameof(PreviewViewModel.CurrentSlideItems);
        if (pptxChanged && vm.IsPptxVisible)
        {
            BuildPptxSlide(vm);
        }
    }

    /// <summary>
    /// 为 DataGrid 手动创建列，绑定到 DataRowView.Row.ItemArray[index]。
    /// 绕过 Avalonia DataGrid 无法从 DataView 正确自动生成列的问题。
    /// 参见 https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid/issues/27
    /// </summary>
    private static void SetupDataGridColumns(DataGrid grid, DataTable? table)
    {
        if (table == null) return;
        grid.Columns.Clear();
        for (int i = 0; i < table.Columns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = table.Columns[i].ColumnName,
                Binding = new Binding($"Row.ItemArray[{i}]"),
                IsReadOnly = true,
            });
        }
    }

    /// <summary>
    /// 根据当前幻灯片的文本项构建 Canvas 子控件（按坐标绝对定位）。
    /// 白底画布使用深色文字；无文本项时显示占位提示。
    /// </summary>
    private void BuildPptxSlide(PreviewViewModel vm)
    {
        if (PptxSlideCanvas == null) return;
        PptxSlideCanvas.Children.Clear();

        var items = vm.CurrentSlideItems;
        if (items == null || items.Count == 0)
        {
            // 空演示文稿（无幻灯片）显示 "此演示文稿为空"；
            // 有幻灯片但当前张无文字则显示 "（此幻灯片无文字）"
            var msg = vm.PptxTotalSlides == 0
                ? MantisZip.UI.Avalonia.Services.LocalizationManager.T("Preview_PptxEmpty")
                : MantisZip.UI.Avalonia.Services.LocalizationManager.T("Preview_PptxSlideEmpty");
            var placeholder = new TextBlock
            {
                Text = msg,
                Foreground = new SolidColorBrush(Colors.Gray),
                TextWrapping = TextWrapping.Wrap,
            };
            // Canvas 子元素不响应对齐，用绝对定位居中占位
            Canvas.SetLeft(placeholder, 20);
            Canvas.SetTop(placeholder, 20);
            PptxSlideCanvas.Children.Add(placeholder);
            return;
        }

        foreach (var item in items)
        {
            var tb = new TextBlock
            {
                Text = item.Text,
                FontSize = item.FontSize,
                FontWeight = item.IsBold ? FontWeight.Bold : FontWeight.Normal,
                Foreground = new SolidColorBrush(Colors.Black),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400,
            };
            Canvas.SetLeft(tb, item.X);
            Canvas.SetTop(tb, item.Y);
            PptxSlideCanvas.Children.Add(tb);
        }
    }

    /// <summary>
    /// 内容区域外层 ScrollViewer 尺寸变化时更新 ViewModel 的视口大小，
    /// 供 ZoomFit 和初始缩放计算使用（替代硬编码 600×500）。
    /// 可用高度 = 外层 ScrollViewer 高度 - contentTop 横条高度，
    /// 否则图像按完整视口缩放会超出可用区域产生滚动条。
    /// </summary>
    private void OnContentScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateViewportSize();
    }

    /// <summary>
    /// contentTop 横条高度变化（字段增删/内容换行）时更新视口高度。
    /// 横条位于外层 ScrollViewer 内部，其尺寸变化不会触发外层 SizeChanged，
    /// 但会直接改变图像可用高度，必须单独处理。
    /// </summary>
    private void OnContentTopSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateViewportSize();
    }

    /// <summary>
    /// 统一计算可用视口尺寸：外层 ScrollViewer 完整尺寸减去 contentTop 横条占用高度。
    /// 防御：横条未布局时 Bounds.Height 为 NaN/0，一律按 0 处理。
    /// </summary>
    private void UpdateViewportSize()
    {
        if (_vm == null || PreviewContentScroller == null) return;
        var w = PreviewContentScroller.Bounds.Width;
        var h = PreviewContentScroller.Bounds.Height;

        // contentTop 横条占用顶部高度，从可用视口高度中扣除
        if (ContentTopBorder != null)
        {
            var topHeight = ContentTopBorder.Bounds.Height;
            if (double.IsFinite(topHeight) && topHeight > 0)
                h -= topHeight;
        }

        if (w <= 0 || h <= 0) return;
        _vm.ViewportWidth = w;
        _vm.ViewportHeight = h;
        _vm.ReFitIfNeeded();
    }

    private void OnFontPreviewScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_vm == null || FontPreviewScrollViewer == null) return;
        var w = FontPreviewScrollViewer.Bounds.Width;
        if (w <= 0) return;
        // 防抖：用户连续拖拽时不重复触发 SkiaSharp 重新渲染，
        // 松开鼠标后 200ms 才更新宽度并重新渲染字体预览
        _resizeDebounceCts?.Cancel();
        _resizeDebounceCts = new CancellationTokenSource();
        var ct = _resizeDebounceCts.Token;
        var uiCtx = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            await Task.Delay(200, ct);
            if (!ct.IsCancellationRequested)
            {
                uiCtx?.Post(_ =>
                {
                    var vm = _vm;
                    if (vm == null) return;
                    vm.FontPreviewWrapWidth = w;
                    vm.ReRenderFontPreview();
                }, null);
            }
        }, ct);
    }

    private void OnOutlineItemClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock tb && tb.DataContext is DocxOutlineItem item && _vm != null)
        {
            var totalLen = _vm.DocxFullText.Length;
            if (totalLen == 0) return;
            var ratio = (double)item.CharOffset / totalLen;
            var maxY = DocxFullTextScroller.ScrollBarMaximum.Y;
            var offsetY = ratio * maxY;
            DocxFullTextScroller.Offset = new Vector(DocxFullTextScroller.Offset.X, offsetY);
        }
    }

    public void ApplyInfoPanelOrientation(string orientation)
    {
        if (PreviewInfoBorder == null) return;
        var isVertical = orientation == "Vertical";

        if (isVertical)
        {
            // Info panel below content
            Grid.SetRow(PreviewInfoBorder, 2);
            Grid.SetColumn(PreviewInfoBorder, 0);
            Grid.SetRowSpan(PreviewInfoBorder, 1);
            PreviewRootGrid.RowDefinitions[2].Height = GridLength.Auto;
            PreviewRootGrid.ColumnDefinitions[1].Width = new GridLength(0);
            PreviewInfoBorder.Width = double.NaN;
            PreviewInfoBorder.MaxWidth = double.PositiveInfinity;
            PreviewInfoBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            PreviewInfoBorder.Margin = new Thickness(0, 8, 0, 0);
            PreviewInfoBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            // Info panel to the right of content (default)
            Grid.SetRow(PreviewInfoBorder, 0);
            Grid.SetColumn(PreviewInfoBorder, 1);
            Grid.SetRowSpan(PreviewInfoBorder, 1);
            PreviewRootGrid.RowDefinitions[2].Height = new GridLength(0);
            PreviewRootGrid.ColumnDefinitions[1].Width = new GridLength(220, GridUnitType.Pixel);
            PreviewInfoBorder.Width = 220;
            PreviewInfoBorder.MaxWidth = 220;
            PreviewInfoBorder.BorderThickness = new Thickness(1, 0, 0, 0);
            PreviewInfoBorder.Margin = new Thickness(0);
            PreviewInfoBorder.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }
}

public class OrientationToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Orientation orientation && parameter is string mode)
            return mode == "vertical"
                ? orientation == Orientation.Vertical
                : orientation == Orientation.Horizontal;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

    public class InvertBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

/// <summary>
/// 两端对齐的 WrapPanel。同一行内的子元素均匀分布，间距自动分配。
/// </summary>
public class JustifyWrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        if (double.IsInfinity(width)) width = 10000;

        double totalHeight = 0;
        double rowWidth = 0;
        double rowHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var childWidth = child.DesiredSize.Width;
            var childHeight = child.DesiredSize.Height;

            if (rowWidth + childWidth > width && rowWidth > 0)
            {
                totalHeight += rowHeight;
                rowWidth = childWidth;
                rowHeight = childHeight;
            }
            else
            {
                rowWidth += childWidth;
                rowHeight = Math.Max(rowHeight, childHeight);
            }
        }
        totalHeight += rowHeight;
        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = finalSize.Width;
        if (width <= 0) return finalSize;

        double y = 0;
        var row = new List<Control>();
        double rowWidth = 0;
        double rowHeight = 0;

        void ArrangeRow()
        {
            if (row.Count == 0) return;
            double spacing = row.Count > 1
                ? (width - rowWidth) / (row.Count - 1)
                : 0;
            double x = 0;
            foreach (var child in row)
            {
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
                x += child.DesiredSize.Width + spacing;
            }
            y += rowHeight;
        }

        foreach (Control child in Children)
        {
            var childWidth = child.DesiredSize.Width;
            var childHeight = child.DesiredSize.Height;

            if (rowWidth + childWidth > width && row.Count > 0)
            {
                ArrangeRow();
                row.Clear();
                rowWidth = 0;
                rowHeight = 0;
            }

            row.Add(child);
            rowWidth += childWidth;
            rowHeight = Math.Max(rowHeight, childHeight);
        }
        ArrangeRow();

        return finalSize;
    }
}
