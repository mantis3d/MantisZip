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
    private string _text = string.Empty;
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

    /// <summary>折叠阈值：段数超过该值时中间段合并为 <c>…</c>。</summary>
    public int MaxSegments { get; set; } = 6;

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
        EditBox.TextChanged += (_, _) =>
        {
            _text = EditBox.Text ?? string.Empty;
            UpdateEditSuggestions();
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
        EditBox.Text = Path ?? string.Empty;
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
        inner.SelectAll();
        return true;
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

        // 窗口级 Ctrl+L：挂在 TopLevel 隧道阶段 → 焦点在列表/树/任意控件时都能直达地址栏，
        // 宿主无需逐个窗口接线（一处接线服务全部宿主）
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnTopLevelKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
            EditBox.ItemsSource = items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
        var segments = BuildSegments(path, PathKind, MaxSegments, ArchiveRootTooltipKey);

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

        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            SegmentsPanel.Children.Add(BuildSegmentView(seg));

            // 末段之后不画分隔符
            if (i < segments.Count - 1)
            {
                SegmentsPanel.Children.Add(CreateSeparator());
            }
        }

        ApplyLocalizedPlaceholder();
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

    private Control BuildSegmentView(Segment seg)
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
            var current = new Button { Content = seg.Label, Classes = { "crumbCurrent" } };
            ToolTip.SetTip(current, seg.Tooltip);
            current.Click += (_, e) =>
            {
                EnterEditMode();
                e.Handled = true;
            };
            return current;
        }

        // 不可点段（UNC 根 / 相对路径首段）：纯文本展示
        if (!seg.IsClickable)
        {
            var label = new TextBlock { Text = seg.Label, Classes = { "crumbStatic" } };
            if (seg.Tooltip.Length > 0) ToolTip.SetTip(label, seg.Tooltip);
            return label;
        }

        var button = new Button { Content = seg.Label, Classes = { "crumb" } };
        ToolTip.SetTip(button, seg.Tooltip);
        button.Click += (_, e) =>
        {
            NavigateRequested?.Invoke(this, seg.Path);
            e.Handled = true;
        };
        return button;
    }

    /// <summary>
    /// 按路径类型切段。<c>Virtual</c>：<c>/</c> 分隔，根段 = 📦；<c>Disk</c>：<c>\</c> 分隔，盘符段可点（上抛 <c>C:\</c>）、UNC 根不可点。
    /// 段数超过 <paramref name="maxSegments"/> 时折叠中间段为单个省略号段。
    /// </summary>
    private static List<Segment> BuildSegments(string path, BreadcrumbPathKind kind, int maxSegments, string? archiveRootTooltipKey)
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

            return MarkCurrent(Fold(list, maxSegments));
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

            return MarkCurrent(Fold(list, maxSegments));
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

        return MarkCurrent(Fold(list, maxSegments));
    }

    /// <summary>段数超过阈值时把中间段折叠为单个省略号段（保留首段 + 末尾若干段）。</summary>
    private static List<Segment> Fold(List<Segment> segments, int maxSegments)
    {
        if (maxSegments < 3) maxSegments = 3;
        if (segments.Count <= maxSegments) return segments;

        var keepTail = maxSegments - 2; // 首段 + … + keepTail 段
        var head = segments[0];
        var hiddenFirst = segments[1];
        var tail = segments.Skip(segments.Count - keepTail).ToList();

        var result = new List<Segment>(maxSegments)
        {
            head,
            new Segment("…", string.Empty, false, false, false, true, hiddenFirst.Tooltip),
        };
        result.AddRange(tail);
        return result;
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
                var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                if (focused is Visual v && (v == this || v == EditBox || EditBox.IsVisualAncestorOf(v)))
                {
                    return;
                }

                CloseEdit();
            },
            global::Avalonia.Threading.DispatcherPriority.Input);
    }
}
