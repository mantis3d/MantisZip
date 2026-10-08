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
using Avalonia.Threading;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Models;

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

        // WebView 相关初始化（NavigationCompleted / NavigationStarted 订阅）已移到
        // EnsureWebViewForHtml —— NativeWebView 改为惰性创建，不再在 XAML 中声明，
        // 详见 PreviewPanel.axaml 中 WebViewHost 的注释。
        InstallWebViewGuard();
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

        // HTML WebView 预览宿主：仅在真正需要 WebView 渲染 HTML 时才惰性创建 NativeWebView，
        // 避免「首次点击任意条目」就初始化 WebView2（详见 PreviewPanel.axaml 的 WebViewHost 注释）。
        //
        // IsWebViewHtmlVisible 是由 PreviewType / IsWebViewVisible / IsHtmlSourceMode 三个属性
        // 合成的派生属性，而 ShowHtmlPreview 内部的赋值顺序是
        // IsWebViewVisible=true（此时 PreviewType 还是 None）→ PreviewType=Html，
        // 因此不能只监听 IsWebViewHtmlVisible 一个名字：这里监听全部相关来源，
        // 由 EnsureWebViewForHtml 统一按合成条件复核，避免依赖某一属性的赋值时序。
        if (args.PropertyName is nameof(PreviewViewModel.IsWebViewHtmlVisible)
                             or nameof(PreviewViewModel.IsWebViewVisible)
                             or nameof(PreviewViewModel.PreviewType)
                             or nameof(PreviewViewModel.HtmlWebViewUri))
        {
            if (vm.IsWebViewHtmlVisible)
            {
                EnsureWebViewForHtml();
            }
            else
            {
                // 离开 HTML WebView 预览：关闭创建窗口期，避免后续无关的 UI 线程异常被误判为 WebView 失败
                CloseWebViewCreationWindow();
            }
        }
    }

    // ─────────────────────────── WebView 惰性创建与失败降级 ───────────────────────────

    /// <summary>WebView 创建窗口期标记：非 0 表示「正在创建 WebView」，此时拦截 WebView2 相关的 UI 线程未处理异常。</summary>
    private static int _webViewCreationWindow;

    /// <summary>WebView 创建窗口期内的失败回调（由静态守卫在 UI 线程上调用）。</summary>
    private static Action<Exception>? _webViewFailed;

    /// <summary>
    /// 当前挂载在 <see cref="WebViewHost"/> 上的 NativeWebView（仅 HTML 预览期间非空）。
    /// 用于在创建窗口期之外仍然判定「异常是否属于本控件的 WebView2」，消除时序竞态：
    /// NavigationCompleted 可能先于异步的 WebView2 异常到达并把窗口期清零。
    /// </summary>
    private static NativeWebView? _liveWebView;

    /// <summary>
    /// 已安装守卫的 UI 线程 Dispatcher。
    ///
    /// 必须按 Dispatcher 实例记录而不是用一个静态 bool：`Dispatcher.UIThread` 会随
    /// Avalonia headless 测试的每个用例重建，若只用一个「已安装」标记，守卫只会订阅到
    /// 第一个 Dispatcher，后续用例新建的 Dispatcher 上根本没有守卫，WebView2 异常会直接
    /// 逃逸并终止进程 —— 表现为该用例「单跑通过、全量跑偶发失败」。
    /// </summary>
    private static Dispatcher? _guardedDispatcher;

    /// <summary>已确认本机 WebView2 不可用（Runtime 缺失 / 创建被拒 / COM 冲突），后续不再重试，直接走降级。</summary>
    private bool _webViewUnavailable;

    /// <summary>
    /// 安装 UI 线程未处理异常守卫，拦截 WebView2 相关的异常并标记 Handled。
    ///
    /// NativeWebView attach 时初始化 WebView2 失败，异常由 Avalonia 内部 Task 重新抛到 Dispatcher
    /// （NativeWebView.OnAttached → Task.ThrowAsync → SendOrPostCallbackDispatcherOperation.InvokeCore），
    /// 位于 ShowPreviewAsync 的 try/catch 之外，会一路传到 AppDomain.UnhandledException 并终止进程。
    ///
    /// 命中条件为「处于创建窗口期 **或** 仍有 NativeWebView 挂载」且异常栈确实来自 WebView2，
    /// 因此不会掩盖其它真实的 UI 线程异常 —— 不满足条件时一律保持系统默认的「记录 + 终止」行为。
    /// </summary>
    private static void InstallWebViewGuard()
    {
        // 按 Dispatcher 实例去重：同一个 Dispatcher 只订阅一次，换了实例则重新订阅。
        var dispatcher = Dispatcher.UIThread;
        if (ReferenceEquals(_guardedDispatcher, dispatcher)) return;
        _guardedDispatcher = dispatcher;

        dispatcher.UnhandledException += OnWebViewGuardUnhandledException;
    }

    /// <summary>
    /// UI 线程未处理异常守卫的处理逻辑（静态，供所有 Dispatcher 实例复用）。
    /// </summary>
    private static void OnWebViewGuardUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 与本控件的 WebView 无关：保持原有行为（只记录，不改 Handled）。
        // 注意不能只看创建窗口期：WebView2 的异步失败可能在 NavigationCompleted 之后
        // 才抛到 Dispatcher，那时窗口期已被清零 —— 因此「仍有 NativeWebView 挂载」也算命中，
        // 以消除该时序竞态（否则守卫会漏掉异常并终止进程）。
        if (Volatile.Read(ref _webViewCreationWindow) == 0 && _liveWebView == null) return;

        // 异常栈不来自 WebView2：同样保持原有行为，避免误吞真实 bug
        if (!LooksLikeWebViewFailure(e.Exception)) return;

        Volatile.Write(ref _webViewCreationWindow, 0);

        var handler = _webViewFailed;
        _webViewFailed = null;

        App.DebugLog($"WebView2 初始化失败，已拦截（降级到 ReverseMarkdown）: " +
                     $"{e.Exception.GetType().Name}: {e.Exception.Message}");

        // 标记为已处理，阻止异常继续传播到 AppDomain 造成进程终止
        e.Handled = true;

        try
        {
            handler?.Invoke(e.Exception);
        }
        catch (Exception fallbackEx)
        {
            App.DebugLog($"WebView 降级处理失败: {fallbackEx.GetType().Name}: {fallbackEx.Message}");
        }
    }

    /// <summary>
    /// 判断异常是否源自 WebView2 / NativeWebView（检查整条异常链的调用栈）。
    /// </summary>
    private static bool LooksLikeWebViewFailure(Exception? ex)
    {
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            var stack = cur.StackTrace;
            if (stack == null) continue;
            if (stack.Contains("WebView2", StringComparison.Ordinal) ||
                stack.Contains("NativeWebView", StringComparison.Ordinal) ||
                stack.Contains("WebViewAdapter", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>关闭创建窗口期并释放回调引用（离开 HTML WebView 预览或创建结束时调用）。</summary>
    private void CloseWebViewCreationWindow()
    {
        Interlocked.Exchange(ref _webViewCreationWindow, 0);
        _webViewFailed = null;
    }

    /// <summary>
    /// 惰性创建 NativeWebView 并放入 <see cref="WebViewHost"/>。
    /// 仅当预览类型确为 HTML 且走 WebView 渲染路径（<see cref="PreviewViewModel.IsWebViewHtmlVisible"/>）时调用。
    /// </summary>
    private void EnsureWebViewForHtml()
    {
        var vm = _vm;
        if (vm == null || _webViewUnavailable) return;

        // 已创建：只需确保 Source 绑定生效（绑定为一次性设置，随 VM 更新自动生效）
        if (WebViewHost.Content is NativeWebView) return;

        InstallWebViewGuard();

        NativeWebView webView;
        try
        {
            webView = new NativeWebView
            {
                // 沿用宿主的主题背景，保持与原 XAML 声明一致的观感
                Background = WebViewHost.Background,
            };
            webView.Bind(NativeWebView.SourceProperty, new Binding(nameof(PreviewViewModel.HtmlWebViewUri)));

            // WebView 初始化安全检测：Runtime 缺失 / 导航失败时 NavigationCompleted 会触发且 IsSuccess=false
            webView.NavigationCompleted += OnWebViewNavigationCompleted;
            // 导航拦截：根据设置阻止外部链接跳转
            webView.NavigationStarted += OnWebViewNavigationStarted;
        }
        catch (Exception ex)
        {
            App.DebugLog($"NativeWebView 实例化失败，降级到 ReverseMarkdown: {ex.GetType().Name}: {ex.Message}");
            HandleWebViewUnavailable(ex.GetType().Name);
            return;
        }

        // 打开创建窗口期：attach 引发的 WebView2 异步失败会被守卫拦截
        Interlocked.Exchange(ref _webViewCreationWindow, 1);
        _webViewFailed = ex => HandleWebViewUnavailable(ex.GetType().Name);

        // 在挂载**之前**登记，使守卫从 attach 的第一刻起就能识别该 WebView 的异常，
        // 不会因窗口期被 NavigationCompleted 提前清零而漏判。
        _liveWebView = webView;

        try
        {
            // attach → NativeWebView.OnAttached() → 初始化 WebView2（可能同步抛出）
            WebViewHost.Content = webView;
        }
        catch (Exception ex)
        {
            App.DebugLog($"WebView 挂载失败，降级到 ReverseMarkdown: {ex.GetType().Name}: {ex.Message}");
            HandleWebViewUnavailable(ex.GetType().Name);
        }
    }

    /// <summary>
    /// WebView2 不可用时的降级处理：拆除损坏的 WebView，标记本机不可用，并切到 ReverseMarkdown 控件树预览。
    /// </summary>
    private void HandleWebViewUnavailable(string reason)
    {
        _webViewUnavailable = true;
        CloseWebViewCreationWindow();

        try
        {
            if (WebViewHost.Content is NativeWebView broken)
            {
                broken.NavigationCompleted -= OnWebViewNavigationCompleted;
                broken.NavigationStarted -= OnWebViewNavigationStarted;
                broken.ClearValue(NativeWebView.SourceProperty);
                WebViewHost.Content = null;
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"WebView 清理失败: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // 必须在拆除**之后**才清除登记：detach 本身也可能抛出 WebView2 异常，
            // 那时仍需守卫兜住，先清零反而会漏掉并终止进程。
            _liveWebView = null;
        }

        var vm = _vm;
        if (vm == null) return;

        // 直接用内存中的 HTML 源码降级：ShowHtmlFallback(filePath) 需要真实文件路径，
        // 而此处能拿到的 CurrentPreviewFilePath 是压缩包内部条目路径，会导致降级静默失败。
        App.DebugLog($"HTML 预览降级到 ReverseMarkdown（原因: {reason}）");
        vm.ShowHtmlFallbackFromSource(vm.HtmlSourceContent);
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
    /// WebView NavigationCompleted 事件：WebView2 Runtime 缺失或导航失败时触发，
    /// IsSuccess=false 说明 WebView 无法渲染，降级到 ReverseMarkdown 控件树预览。
    /// </summary>
    private void OnWebViewNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            // 导航成功 → WebView2 环境创建已成功结束，关闭创建窗口期
            CloseWebViewCreationWindow();
            return;
        }

        // 导航失败（Runtime 缺失 / 创建被拒）：**不**关闭创建窗口期，
        // 紧随其后的异步 WebView2 异常仍需要守卫兜住，否则会终止进程。
        // 单次导航失败不代表 WebView2 永久不可用，故只降级本次预览，不标记 _webViewUnavailable。
        var vm = _vm;
        if (vm == null || !vm.IsWebViewVisible || vm.IsFallbackActive) return;

        App.DebugLog($"WebView navigation failed (IsSuccess=false), falling back to ReverseMarkdown");
        vm.ShowHtmlFallbackFromSource(vm.HtmlSourceContent);
    }

    /// <summary>
    /// WebView NavigationStarted 事件：根据安全设置拦截非本地导航。
    /// AllowNavigation=false 时阻止跳转到外部 URL。
    /// </summary>
    private void OnWebViewNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        // 如果用户允许导航，放行
        if (AppSettings.Load().AllowNavigation) return;

        // 否则阻止非文件导航（本地 HTML 文件间的跳转放行）
        if (e.Request != null && !e.Request.IsFile)
        {
            e.Cancel = true;
            App.DebugLog($"WebView navigation blocked: {e.Request}");
        }
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
            // 通过 BlockIndex 在全文控件树中找到目标块，TranslatePoint 精确滚动到该位置。
            // 旧实现按字符比例近似滚动，块高度不均匀时误差明显。
            if (_vm.DocxContentPanel is Panel panel
                && item.BlockIndex >= 0 && item.BlockIndex < panel.Children.Count
                && panel.Children[item.BlockIndex] is Control target
                && DocxFullTextScroller != null)
            {
                var pos = target.TranslatePoint(new Point(0, 0), DocxFullTextScroller);
                if (pos.HasValue)
                    DocxFullTextScroller.Offset = new Vector(0, Math.Max(0, pos.Value.Y - 8));
            }
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
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Orientation orientation && parameter is string mode)
            return mode == "vertical"
                ? orientation == Orientation.Vertical
                : orientation == Orientation.Horizontal;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

    public class InvertBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
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
