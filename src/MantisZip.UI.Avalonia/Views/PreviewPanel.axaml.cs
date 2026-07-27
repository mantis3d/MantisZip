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
    /// 内容区域外层 ScrollViewer 尺寸变化时更新 ViewModel 的视口大小，
    /// 供 ZoomFit 和初始缩放计算使用（替代硬编码 600×500）。
    /// </summary>
    private void OnContentScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_vm == null || PreviewContentScroller == null) return;
        var w = PreviewContentScroller.Bounds.Width;
        var h = PreviewContentScroller.Bounds.Height;
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
