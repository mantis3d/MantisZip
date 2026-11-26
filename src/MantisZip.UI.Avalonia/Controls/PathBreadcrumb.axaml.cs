using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>面包屑路径类型：压缩包内虚拟路径（<c>/</c> 分隔）或磁盘路径（<c>\</c> 分隔）。</summary>
public enum BreadcrumbPathKind
{
    /// <summary>压缩包内虚拟路径：空串 = 根，段路径用 <c>/</c> 拼接。</summary>
    Virtual,

    /// <summary>磁盘路径：段路径用 <c>\</c> 拼接；盘符段可点（上抛 <c>C:\</c>），UNC 根不可点。</summary>
    Disk,
}

/// <summary>
/// 面包屑地址栏控件：浏览态显示可点击的路径分段（祖先段=导航、末段=进编辑态），点末尾空白 / <c>Ctrl+L</c> 也进编辑态。
/// <para>
/// 控件**不导航**：段点击与编辑态 Enter 只上抛 <see cref="NavigateRequested"/>，由宿主决定导航动作，
/// 保持宿主既有导航逻辑（<c>NavigateToFolderPath</c> / <c>NavigateTo</c> / <c>CoerceToDirectory</c>）为单一事实来源。
/// </para>
/// <para>
/// <see cref="Path"/> 为 <see cref="BindingMode.OneWay"/>（宿主→控件），控件从不回写，因此不存在双向绑定环。
/// </para>
/// </summary>
public partial class PathBreadcrumb : UserControl
{
    /// <summary>路径值（宿主→控件单向；跳转只发 <see cref="NavigateRequested"/>）。</summary>
    public static readonly StyledProperty<string> PathProperty =
        AvaloniaProperty.Register<PathBreadcrumb, string>(
            nameof(Path), defaultValue: string.Empty, defaultBindingMode: BindingMode.OneWay);

    /// <summary>路径类型（虚拟 / 磁盘），决定分段与段路径拼接规则。</summary>
    public static readonly StyledProperty<BreadcrumbPathKind> PathKindProperty =
        AvaloniaProperty.Register<PathBreadcrumb, BreadcrumbPathKind>(
            nameof(PathKind), defaultValue: BreadcrumbPathKind.Disk);

    /// <summary>编辑态补全过滤模式（内部固定 <c>MinimumPrefixLength = 0</c>）。</summary>
    public static readonly StyledProperty<AutoCompleteFilterMode> FilterModeProperty =
        AvaloniaProperty.Register<PathBreadcrumb, AutoCompleteFilterMode>(
            nameof(FilterMode), defaultValue: AutoCompleteFilterMode.StartsWith);

    private bool _isEditing;

    /// <summary>候选刷新代次：延后刷新时用它丢弃过期请求。</summary>
    private int _suggestionVersion;
    private string _text = string.Empty;

    /// <summary>
    /// 用户是否刚从下拉候选里挑了一项：挑完 <see cref="AutoCompleteBox"/> 会关下拉，
    /// 关下拉那一刻再提交导航，避免「点了没反应」。
    /// </summary>

    /// <summary>下拉候选列表的点击是否已挂接（模板就绪后才能挂，故用标志防重复挂）。</summary>
    private bool _cultureSubscribed;
    private TopLevel? _topLevel;

    /// <summary>路径值（宿主→控件）。</summary>
    public string Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>路径类型。</summary>
    public BreadcrumbPathKind PathKind
    {
        get => GetValue(PathKindProperty);
        set => SetValue(PathKindProperty, value);
    }

    /// <summary>编辑态补全过滤模式。</summary>
    public AutoCompleteFilterMode FilterMode
    {
        get => GetValue(FilterModeProperty);
        set => SetValue(FilterModeProperty, value);
    }

    /// <summary>
    /// 编辑态补全源：参数 = 编辑框当前文本，返回建议列表。
    /// 为 null 时编辑态不给建议（只做纯文本输入）。
    /// </summary>
    public Func<string?, IEnumerable<string>>? EditCompletionProvider { get; set; }

    /// <summary>段间分隔符的固定占宽（图标 12 + 左右 margin 各 1）。</summary>
    private const double SeparatorWidth = 14;

    /// <summary>折叠时省略号段自身占的宽度（<c>…</c> 小号字 + 左右 margin）。</summary>
    private const double EllipsisWidth = 16;

    /// <summary>虚拟根段（📦）的 ToolTip 本地化 key。</summary>
    public string? ArchiveRootTooltipKey { get; set; }

    /// <summary>编辑态占位文案 / 磁盘空路径占位文案的本地化 key。</summary>
    public string? EditPlaceholderKey { get; set; }

    /// <summary>段点击或编辑态 Enter 提交时上抛目标路径（未归一化，归一化由宿主负责）。</summary>
    public event EventHandler<string>? NavigateRequested;

    /// <summary>预留：分隔符点击请求同级目录枚举（计划「设计决定 3」：第一版不接，仅保留事件位）。</summary>
#pragma warning disable CS0067 // 预留事件：第一版不接入（见计划设计决定 3）
    public event EventHandler<string>? EnumerationRequested;
#pragma warning restore CS0067

    /// <summary>是否处于编辑态。</summary>
    public bool IsEditing => _isEditing;

    /// <summary>
    /// 当前文本（与旧 <c>AutoCompleteBox.Text</c> 语义一致）：浏览态 = <see cref="Path"/>，
    /// 编辑态 = 编辑框文本（提交前也反映用户已输入内容，供宿主的「文件名回落」等逻辑使用）。
    /// </summary>
    public string Text => _isEditing ? (EditBox.Text ?? string.Empty) : _text;

    public PathBreadcrumb()
    {
        InitializeComponent();
        if (Design.IsDesignMode) return;

        EditBox.FilterMode = FilterMode;
        // 关闭「内联补全」（默认开启）：它会在每次按键后把候选剩余部分直接写进 Text，
        // 而本控件每次 TextChanged 都会重建 ItemsSource，两者叠加会把输入串写成 E:E:\AEW\ 这种鬼样子。
        // 地址栏只需要下拉候选，不需要行内补全。
        EditBox.IsTextCompletionEnabled = false;
        // 候选列表是 Popup 里的独立子树：指针事件要挂到它的内容根上，见 AttachDropDownClickHandler
        EditBox.DropDownOpened += (_, _) => AttachDropDownClickHandler();
        EditBox.TextChanged += (_, _) =>
        {
            _text = EditBox.Text ?? string.Empty;
            ScheduleSuggestionUpdate();
        };

        // 控件自身 KeyDown 兜底响应 Ctrl+L（宿主窗口级快捷键另行调用 EnterEditMode）
        AddHandler(KeyDownEvent, OnControlKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// 进入编辑态（点末段 / 点末尾空白 / 宿主窗口级 <c>Ctrl+L</c> 三条入口共用）。
    /// 焦点必须落在 <see cref="AutoCompleteBox"/> 内部的 <c>TextBox</c> 上——
    /// <see cref="AutoCompleteBox"/> 自身不处理文本输入，只 Focus 它会导致「看着能输、打字没反应」。
    /// </summary>
    public void EnterEditMode()
    {
        if (_isEditing) return;
        _isEditing = true;
        PseudoClasses.Set(":editing", true);
        // 编辑态预填「当前路径 + 分隔符」：绝大多数输入是要进子目录，
        // 光标停在末尾，直接敲子目录名即可（要整条替换就 Ctrl+A）。
        EditBox.Text = BuildEditInitialText(Path);
        _text = EditBox.Text ?? string.Empty;
        UpdateEditSuggestions();
        SegmentsPanel.IsVisible = false;
        EmptyHint.IsVisible = false;
        EditBox.IsVisible = true;
        FocusEditTextBox();
    }

    /// <summary>把焦点交给编辑框内部的 TextBox 并全选（直接打字即替换，不用先删）。</summary>
    private void FocusEditTextBox()
    {
        // 首次显示时模板可能尚未实例化：先 ApplyTemplate，再取内部 TextBox
        EditBox.ApplyTemplate();
        if (TryFocusInnerTextBox()) return;

        // 仍取不到（布局尚未发生）→ 下一轮布局后再试一次
        global::Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                if (_isEditing) TryFocusInnerTextBox();
            },
            global::Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private bool TryFocusInnerTextBox()
    {
        var inner = EditBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (inner is null) return false;

        // Enter / Esc 直接挂在内部 TextBox 上：AutoCompleteBox 会自己吞掉 Esc（关闭下拉），
        // 冒泡到 EditBox 的 KeyDown 收不到，必须在内层处理。
        inner.KeyDown -= EditBox_KeyDown;
        inner.KeyDown += EditBox_KeyDown;

        inner.Focus();
        // 光标停在末尾（不是全选）：配合 EnterEditMode 预填的分隔符，直接打字就是进子目录
        inner.CaretIndex = inner.Text?.Length ?? 0;
        return true;
    }

    /// <summary>
    /// 编辑态的初始文本：当前路径 + 末尾分隔符（已有则不重复加）。
    /// 地址栏的实际用法是「从当前目录往子目录钻」，预填分隔符能省掉每次手敲一个反斜杠。
    /// </summary>
    private string BuildEditInitialText(string? path)
    {
        var text = path ?? string.Empty;
        if (text.Length == 0) return text;
        if (text.EndsWith('\\') || text.EndsWith('/')) return text;
        return text + (PathKind == BreadcrumbPathKind.Virtual ? "/" : "\\");
    }

    /// <summary>退出编辑态（不导航）。宿主的 Popup 打开前调用，避免失焦还原与 Popup 焦点竞争。</summary>
    public void CloseEdit()
    {
        if (!_isEditing) return;
        _isEditing = false;
        PseudoClasses.Set(":editing", false);
        EditBox.IsVisible = false;
        SegmentsPanel.IsVisible = true;
        // 文本回到 Path：编辑态未提交的输入不再作为「当前文本」（与旧 AutoCompleteBox 在导航后回填一致）
        _text = Path ?? string.Empty;
        RefreshSegments();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PathProperty)
        {
            var v = change.GetNewValue<string>() ?? string.Empty;
            if (_isEditing) CloseEdit();
            _text = v;
            RefreshSegments();
        }
        else if (change.Property == PathKindProperty)
        {
            RefreshSegments();
        }
        else if (change.Property == FilterModeProperty)
        {
            EditBox.FilterMode = FilterMode;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_cultureSubscribed)
        {
            LocalizationManager.CultureChanged += OnCultureChanged;
            _cultureSubscribed = true;
        }
        RefreshSegments();

        // 窗口缩放 / 侧栏拖动会改变可用宽度：按宽度折叠的结论必须跟着重算，
        // 否则窗口拉窄后会停在旧结论上、被 ClipToBounds 硬切（2026-11-25 前的真实缺陷）。
        // 只在宽度真的变了时才重画 —— SizeChanged 在高度变化时也会触发，那两个方向不需要重排。
        Shell.SizeChanged += OnShellSizeChanged;

        // 窗口级 Ctrl+L：挂在 TopLevel 隧道阶段 → 焦点在列表/树/任意控件时都能直达地址栏，
        // 宿主无需逐个窗口接线（一处接线服务全部宿主）
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnTopLevelKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>外壳宽度变化时重排段（首次布局也走这里：Attached 时 Bounds 还是 0，拿不到可用宽度）。</summary>
    private void OnShellSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 0.5) return;
        RefreshSegments();
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control) && IsEffectivelyVisible)
        {
            EnterEditMode();
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Shell.SizeChanged -= OnShellSizeChanged;
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
            _topLevel = null;
        }
        if (_cultureSubscribed)
        {
            LocalizationManager.CultureChanged -= OnCultureChanged;
            _cultureSubscribed = false;
        }
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        ApplyLocalizedPlaceholder();
        RefreshSegments();
    }

    private void ApplyLocalizedPlaceholder()
    {
        EditBox.PlaceholderText = EditPlaceholderKey is { Length: > 0 } key
            ? LocalizationManager.T(key)
            : string.Empty;
    }

    /// <summary>
    /// 延后一拍再刷新候选。
    /// **不能**在 TextChanged 里同步替换 ItemsSource：AutoCompleteBox 在 TextUpdated 内部会先
    /// PopulateDropDown → CloseDropDown → 把 SelectedItem 写回选择模型；此时我们把 ItemsSource 换掉，
    /// 选择模型里残留的旧索引就会在 GetAt 时越界——真机崩过一次
    /// （System.ArgumentOutOfRangeException at ItemsSourceView`1.GetAt ← SelectedItems.GetEnumerator）。
    /// </summary>
    private void ScheduleSuggestionUpdate()
    {
        var version = ++_suggestionVersion;
        global::Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                if (version != _suggestionVersion || !_isEditing) return;
                UpdateEditSuggestions();
            },
            global::Avalonia.Threading.DispatcherPriority.Background);
    }

    private void UpdateEditSuggestions()
    {
        if (EditCompletionProvider is null)
        {
            EditBox.ItemsSource = null;
            return;
        }

        try
        {
            var items = EditCompletionProvider(EditBox.Text) ?? Enumerable.Empty<string>();
            EditBox.SelectedItem = null;
            var list = items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            EditBox.ItemsSource = list;
            // 候选刷新被延后到 Background，控件自己那次 TextUpdated 早已走完（下拉也随之关掉），
            // 所以这里要显式重新打开下拉，否则「打字有候选」只在第一下有效。
            EditBox.IsDropDownOpen = _isEditing && list.Count > 0 && !string.IsNullOrEmpty(EditBox.Text);
        }
        catch
        {
            // 补全源异常（非法路径等）不应影响输入
            EditBox.ItemsSource = null;
        }
    }

    // ── 分段构建 ────────────────────────────────────────────────────────────

    /// <summary>一个面包屑段。</summary>
    private sealed record Segment(string Label, string Path, bool IsClickable, bool IsCurrent, bool IsVirtualRoot, bool IsEllipsis, string Tooltip);

    private void RefreshSegments()
    {
        SegmentsPanel.Children.Clear();

        var path = Path ?? string.Empty;
        var segments = BuildSegments(path, PathKind, ArchiveRootTooltipKey);

        // 磁盘空路径：显示占位文案，整行可点进编辑态
        var emptyDisk = PathKind == BreadcrumbPathKind.Disk && segments.Count == 0;
        EmptyHint.IsVisible = emptyDisk && !_isEditing;
        if (emptyDisk)
        {
            EmptyHint.Text = EditPlaceholderKey is { Length: > 0 } key
                ? LocalizationManager.T(key)
                : string.Empty;
        }

        SegmentsPanel.IsVisible = !emptyDisk && !_isEditing;

        // 按**实测可用宽度**决定显示几段（旧实现按段数阈值，深而短的路径会被白折）。
        var available = AvailablePanelWidth();
        var visible = available > 0 ? FitToWidth(segments, available) : segments;

        for (var i = 0; i < visible.Count; i++)
        {
            var seg = visible[i];
            // 末段是当前目录：可用宽度连它一段都放不下时，把它自身按字符省略，而不是靠 ClipToBounds 硬切
            var trimTo = seg.IsCurrent ? RemainingWidthFor(visible, i, available) : null;
            SegmentsPanel.Children.Add(BuildSegmentView(seg, trimTo));

            // 末段之后不画分隔符
            if (i < visible.Count - 1)
            {
                SegmentsPanel.Children.Add(CreateSeparator());
            }
        }

        ApplyLocalizedPlaceholder();
    }

    /// <summary>
    /// 面包屑外壳内部可用的横向空间（已扣掉 <see cref="Border"/> 的内边距）。
    /// 尚未完成首次布局时返回 0 —— 调用方据此「先全画」，等 <c>Shell.SizeChanged</c> 再收一遍。
    /// </summary>
    private double AvailablePanelWidth()
    {
        var outer = Shell.Bounds.Width;
        if (outer <= 0) return 0;

        var padding = Shell.Padding;
        var inner = outer - padding.Left - padding.Right;
        return inner > 0 ? inner : 0;
    }

    /// <summary>段间分隔符：优先用真图标（<c>IconChevronRight</c>），资源未加载时退化为 <c>›</c> 字形。</summary>
    private Control CreateSeparator()
    {
        var data = this.TryFindResource("IconChevronRight", out var res) ? res as Geometry : null;
        if (data is null)
        {
            return new TextBlock { Text = "›", Classes = { "crumbSep" } };
        }

        return new PathIcon { Data = data, Classes = { "crumbSep" } };
    }

    // ── 宽度折叠 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 按可用宽度裁出要显示的段：**放得下就全显示**，放不下则保留首段 + <c>…</c> + 末尾能塞下的若干段。
    /// <paramref name="available"/> 为 0（尚未布局）时不裁剪，等 <c>Shell.SizeChanged</c> 再收。
    /// </summary>
    private List<Segment> FitToWidth(List<Segment> segments, double available)
    {
        if (segments.Count <= 1 || available <= 0) return segments;

        var widths = segments.Select(MeasureSegmentWidth).ToList();
        var total = TotalWidth(widths);
        if (total <= available) return segments;

        // 首段必留；末段尽量留（当前目录）。从末尾往前累加，直到装不下。
        var headWidth = widths[0];
        var tail = new List<int>();
        var used = headWidth + JunctionWidth + EllipsisWidth;    // 首段 › …
        for (var i = segments.Count - 1; i >= 1; i--)
        {
            var cost = widths[i] + JunctionWidth;                // … › 该段
            if (used + cost > available && tail.Count > 0) break;
            used += cost;
            tail.Insert(0, i);
        }

        var hidden = segments.Count - 1 - tail.Count;
        if (hidden <= 0) return segments;

        var result = new List<Segment>(tail.Count + 2) { segments[0] };
        result.Add(new Segment(
            "…",
            string.Empty,
            IsClickable: false,
            IsCurrent: false,
            IsVirtualRoot: false,
            IsEllipsis: true,
            Tooltip: LocalizationManager.T("Breadcrumb_HiddenSegments", hidden)));
        foreach (var i in tail) result.Add(segments[i]);
        return result;
    }

    /// <summary>
    /// 两段之间的成本：分隔符本体 + 它前后的 <see cref="StackPanel.Spacing"/> 间隙。
    /// 间隙随紧凑度档位变化（2/4/6），必须按当前值算，否则窄档下会低估、宽档下会高估。
    /// </summary>
    private double JunctionWidth => SeparatorWidth + (SegmentsPanel.Spacing * 2);

    /// <summary>所有段连同段间分隔符的总宽度。</summary>
    private double TotalWidth(IReadOnlyList<double> widths)
    {
        double total = 0;
        for (var i = 0; i < widths.Count; i++)
        {
            total += widths[i];
            if (i < widths.Count - 1) total += JunctionWidth;
        }
        return total;
    }

    /// <summary>
    /// 单段实际占宽：段视图里最宽的那个子元素。
    /// 根段是 <c>StackPanel</c>（图标，固定 14+margin）、省略号是 <c>TextBlock</c>、
    /// 其余是 <c>Button</c> / <c>TextBlock</c>——统一走 <see cref="Layoutable.Measure"/>，与真实渲染同一套文本栈。
    /// <para>
    /// **必须先把视图挂进面板再量**：脱树的 <c>Button</c> 还没应用控件模板，<c>Measure</c> 恒得 0，
    /// 折叠加宽会全部算成「放得下」（2026-11-25 实测踩到：400px 窗口里 4 段 510px 也不折）。
    /// 量完立刻摘掉，不会留在视觉树里，也不会触发可见的重排。
    /// </para>
    /// </summary>
    private double MeasureSegmentWidth(Segment seg)
    {
        var view = BuildSegmentView(seg, trimTo: null);
        SegmentsPanel.Children.Add(view);
        try
        {
            view.Measure(Size.Infinity);
            return view.DesiredSize.Width;
        }
        finally
        {
            SegmentsPanel.Children.Remove(view);
        }
    }

    /// <summary>
    /// 给末段留的剩余宽度：可用宽度减去「它前面所有段 + 分隔符」。
    /// 结果为 null 表示不裁剪（放得下）。
    /// </summary>
    private double? RemainingWidthFor(IReadOnlyList<Segment> visible, int index, double available)
    {
        // 只有末段需要裁剪；其它段要么整段显示、要么已被 FitToWidth 折掉
        if (index != visible.Count - 1) return null;
        if (available <= 0) return null;

        var widths = visible.Select(MeasureSegmentWidth).ToList();
        double used = 0;
        for (var i = 0; i < index; i++)
        {
            used += widths[i] + JunctionWidth;
        }

        var remaining = available - used;
        return remaining >= widths[index] ? null : Math.Max(0, remaining);
    }

    /// <summary>
    /// 构建段视图。<paramref name="trimTo"/> 非 null 时表示该段要按此宽度截断显示（只有末段会用到）——
    /// 用 <see cref="TextTrimming"/> 让文字自己省略，而不是让外层 <c>ClipToBounds</c> 硬切掉半个字。
    /// </summary>
    private Control BuildSegmentView(Segment seg, double? trimTo)
    {
        // 折叠省略号：仅展示 + ToolTip
        if (seg.IsEllipsis)
        {
            var dots = new TextBlock { Text = "…", Classes = { "crumbEllipsis" } };
            ToolTip.SetTip(dots, seg.Tooltip);
            return dots;
        }

        // 虚拟根（📦 图标）
        if (seg.IsVirtualRoot)
        {
            // 资源可能尚未加载（无头测试 / 设计器）：TryFindResource 取不到时留空，不抛异常
            var iconData = this.TryFindResource("IconArchive", out var iconRes) ? iconRes as Geometry : null;
            var iconBrush = this.TryFindResource("ThemeTextPrimaryBrush", out var brushRes) ? brushRes as IBrush : null;
            var icon = new PathIcon
            {
                Data = iconData,
                Width = 14,
                Height = 14,
                Foreground = iconBrush,
            };
            var host = new StackPanel { Orientation = Orientation.Horizontal, Classes = { "crumbRoot" } };
            host.Children.Add(icon);
            if (seg.IsClickable)
            {
                host.Cursor = new Cursor(StandardCursorType.Hand);
                host.PointerPressed += (_, e) =>
                {
                    NavigateRequested?.Invoke(this, seg.Path);
                    e.Handled = true;
                };
            }
            if (seg.Tooltip.Length > 0) ToolTip.SetTip(host, seg.Tooltip);
            return host;
        }

        // 末段（当前目录）：可点击——点它进编辑态，而不是「导航到自己」
        if (seg.IsCurrent)
        {
            var label = new TextBlock
            {
                Text = seg.Label,
                VerticalAlignment = VerticalAlignment.Center,
                // 末段超宽时自己省略（保留开头 + 省略号），完整路径仍在 ToolTip 里
                TextTrimming = trimTo is null ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            };
            if (trimTo is { } w) label.MaxWidth = w;

            var current = new Button { Content = label, Classes = { "crumbCurrent" } };
            ToolTip.SetTip(current, seg.Tooltip);
            current.Click += (_, e) =>
            {
                EnterEditMode();
                e.Handled = true;
            };
            return current;
        }

        // 不可点段（UNC 根 / 相对路径首段）：纯文本展示
        // （TextBlock 本来就不解析助记符，`_` 原样显示）
        if (!seg.IsClickable)
        {
            var label = new TextBlock { Text = seg.Label, Classes = { "crumbStatic" } };
            if (seg.Tooltip.Length > 0) ToolTip.SetTip(label, seg.Tooltip);
            return label;
        }

        // ⚠ 不能用 `Content = seg.Label`（字符串）：Button 的 ContentPresenter 默认
        // RecognizesAccessKey=True，会把第一个 `_` 当成助记符前缀吃掉、把后一个字符变成
        // Alt 快捷键，于是 `my_folder_name` 显示成 `myfolder_name`（2026-11-25 修复）。
        // 塞 TextBlock 就绕过 AccessText 解析，文字原样渲染；顺带与末段分支保持同一形态。
        var segmentLabel = new TextBlock
        {
            Text = seg.Label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Button { Content = segmentLabel, Classes = { "crumb" } };

        ToolTip.SetTip(button, seg.Tooltip);
        button.Click += (_, e) =>
        {
            NavigateRequested?.Invoke(this, seg.Path);
            e.Handled = true;
        };
        return button;
    }

    /// <summary>
    /// 按路径类型切段（**不折叠**，折叠由 <see cref="FitToWidth"/> 按实际宽度决定）。
    /// <c>Virtual</c>：<c>/</c> 分隔，根段 = 📦；<c>Disk</c>：<c>\</c> 分隔，盘符段可点（上抛 <c>C:\</c>）、UNC 根不可点。
    /// </summary>
    private static List<Segment> BuildSegments(string path, BreadcrumbPathKind kind, string? archiveRootTooltipKey)
    {
        var list = new List<Segment>();
        var trimmed = (path ?? string.Empty).Trim();

        if (kind == BreadcrumbPathKind.Virtual)
        {
            var rootTip = archiveRootTooltipKey is { Length: > 0 } k ? LocalizationManager.T(k) : string.Empty;
            var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = string.Empty;
            list.Add(new Segment(string.Empty, string.Empty, true, false, true, false, rootTip));

            foreach (var part in parts)
            {
                current = current.Length == 0 ? part : current + "/" + part;
                list.Add(new Segment(part, current, true, false, false, false, current));
            }

            if (parts.Length == 0) return list;

            return MarkCurrent(list);
        }

        // Disk
        if (trimmed.Length == 0) return list;

        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // UNC：\\server\share 作为整体根，不可点击
            var unc = trimmed.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (unc.Length == 0) return list;

            var rootName = "\\\\" + unc[0];
            var rest = new List<string>();
            if (unc.Length > 1)
            {
                rootName += "\\" + unc[1];
                rest.AddRange(unc.Skip(2));
            }

            list.Add(new Segment(rootName, rootName, false, false, false, false, rootName));
            var current = rootName;
            foreach (var part in rest)
            {
                current += "\\" + part;
                list.Add(new Segment(part, current, true, false, false, false, current));
            }

            return MarkCurrent(list);
        }

        // 盘符根（C:）或相对路径首段
        var raw = trimmed.TrimEnd('\\');
        var pieces = raw.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length == 0) return list;

        var cur2 = pieces[0];
        // 盘符可点：上抛「C:\」而不是「C:」——后者是驱动器相对路径，下游会解析成该盘的当前目录
        var isDrive = cur2.Length == 2 && char.IsLetter(cur2[0]) && cur2[1] == ':';
        var driveTarget = cur2 + "\\";
        list.Add(new Segment(
            cur2,
            isDrive ? driveTarget : cur2,
            isDrive,
            false,
            false,
            false,
            isDrive ? driveTarget : cur2));

        for (var i = 1; i < pieces.Length; i++)
        {
            cur2 += "\\" + pieces[i];
            list.Add(new Segment(pieces[i], cur2, true, false, false, false, cur2));
        }

        return MarkCurrent(list);
    }

    /// <summary>把「末段」标记为当前目录（加粗 + 点击进编辑态，而不是导航到自己）。</summary>
    private static List<Segment> MarkCurrent(List<Segment> segments)
    {
        if (segments.Count == 0) return segments;
        var last = segments[^1];
        segments[^1] = last with { IsCurrent = true, IsClickable = false };
        return segments;
    }

    // ── 事件 ────────────────────────────────────────────────────────────────

    private void OnControlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            EnterEditMode();
            e.Handled = true;
        }
    }

    private void BlankClickTarget_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EnterEditMode();
            e.Handled = true;
        }
    }

    private void EmptyHint_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EnterEditMode();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 给下拉候选列表挂「点击即提交」：候选项在 Popup 里，默认点它只把文本填进去（还要再按一次 Enter），
    /// 而地址栏的手感是「点一下就去那儿」。
    /// 用 PointerPressed 而不是 SelectionChanged：AutoCompleteBox 在打字时会**自动把首个候选设为 SelectedItem**，
    /// 若在 SelectionChanged 里提交，输到一半就会被"自动导航"（实测输入 E: 就跳走）。
    /// </summary>
    private void AttachDropDownClickHandler()
    {
        if (EditBox.GetVisualDescendants().OfType<Popup>().FirstOrDefault()?.Child is not Control popupRoot) return;

        popupRoot.AddHandler(
            PointerPressedEvent,
            OnDropDownItemPointerPressed,
            global::Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    /// <summary>候选列表按下即提交（下拉在按下瞬间就会关闭，所以不能用 Released）。</summary>
    private void OnDropDownItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isEditing) return;

        var item = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (item?.DataContext is not string picked || string.IsNullOrWhiteSpace(picked)) return;

        e.Handled = true;
        CloseEdit();
        NavigateRequested?.Invoke(this, picked.Trim());
    }
    private void EditBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = EditBox.Text?.Trim() ?? string.Empty;
            CloseEdit();
            if (text.Length > 0) NavigateRequested?.Invoke(this, text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseEdit();
            e.Handled = true;
        }
    }

    /// </summary>
    private void EditBox_LostFocus(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 失焦即还原浏览态（不导航）；宿主 Popup 打开前应显式 CloseEdit，避免竞争。
        // 注意：编辑时焦点在 AutoCompleteBox 内部的 TextBox 上，从 EditBox 视角看也是一次「失焦」，
        // 所以延后一拍再判断焦点是否仍在本控件子树内，否则一进编辑态就会自己关掉。
        if (!_isEditing) return;

        global::Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                if (!_isEditing) return;

                // 下拉候选列表在 Popup 里，点它时焦点会短暂离开本控件子树；此时若直接 CloseEdit，
                // EditBox 被隐藏 → 下拉被关掉 → 那一次点击就丢了（「候选列表点了没反应」）。
        
                var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                if (focused is Visual v)
                {
                    if (v == this || v == EditBox || EditBox.IsVisualAncestorOf(v)) return;

                    // 下拉候选在 Popup（独立 PopupRoot）里，点候选时焦点会跑到 PopupRoot 下的 ListBoxItem；
                    // 这同样属于「仍在本控件内」，不能当成失焦退出编辑态，否则下拉会被关掉、那一次点击就丢了。
                    if (global::Avalonia.Controls.TopLevel.GetTopLevel(v) is global::Avalonia.Controls.Primitives.PopupRoot) return;
                }

                CloseEdit();
            },
            global::Avalonia.Threading.DispatcherPriority.Input);
    }
}
