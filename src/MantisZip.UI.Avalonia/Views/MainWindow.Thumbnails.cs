using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Views;

/// <summary>
/// 文件列表「缩略图布局」（平铺 Tile / 内容 Content）的代码后置接线。
///
/// 背景：详情模式仍是 DataGrid（自带选择/键盘/拖拽），平铺与内容模式是 ItemsRepeater——
/// **没有**内建选择层，因此这里做三件事：
/// ① 把按下/键盘/右键解释成「选择 + 动作」，选择算法本身在 <see cref="ThumbnailSelectionController"/>（纯逻辑、可单测）；
/// ② 双击/键盘走与 DataGrid **同一份**实现（<see cref="HandleItemDoubleTapAsync"/> / <see cref="HandleFileListKeyDown"/>）；
/// ③ 拖出（拖拽解压）走与 DataGrid **同一份**实现（<see cref="StartFileDragAsync"/>），
///    避免「两套拖拽、只有一套有覆层/光标/落点解压」的重复实现。
///
/// 事件接线说明：内容区（含两个 ScrollViewer）是延迟创建的 <see cref="Controls.ArchiveContentView"/>，
/// 里面的 XAML 事件属性无法解析到本类的方法，所以这里在内容区创建后按 x:Name 程序化订阅（幂等）；
/// 同时按约定名字保留处理器方法，XAML 侧若直接挂属性接线也能解析。
/// 两条路径同时存在时不会重复执行：按下用 <c>_thumbnailLastProcessedPress</c> 判重，
/// 双击/键盘用 <see cref="RoutedEventArgs.Handled"/> 短路。
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Shift 范围选择的锚点（上一次单击的条目）。
    /// 由 <see cref="ThumbnailSelectionController"/> 返回、这里保存：Shift 连点时锚点不动，范围才能连续扩张。
    /// </summary>
    private ArchiveItemModel? _thumbnailSelectionAnchor;

    /// <summary>
    /// 复选框模式的 Shift 连勾锚点（与选区锚点分开存：勾选和选区是两套状态，
    /// 勾选模式下选区只是高亮，混用会让「先 Shift 选区再 Shift 连勾」跳错起点）。
    /// </summary>
    private ArchiveItemModel? _thumbnailCheckAnchor;

    /// <summary>平铺/内容容器的事件是否已订阅（内容区延迟创建，订阅动作必须幂等）。</summary>
    private bool _thumbnailLayoutEventsHooked;

    /// <summary>
    /// 正在把缩略图选择写回 VM 的窗口期。
    /// 隐藏的 DataGrid 仍双向绑定 <c>SelectedEntry</c>：写主选中项会让它触发 SelectionChanged 并回写「单选」，
    /// 覆盖掉刚写入的多选集合 —— 期间由 <c>FileListGrid_SelectionChanged</c> 跳过回写。
    /// </summary>
    private bool _isApplyingThumbnailSelection;

    /// <summary>最近一次已处理的按下事件（Tunnel 订阅 + XAML 属性接线可能同时存在，靠引用相等判重）。</summary>
    private PointerPressedEventArgs? _thumbnailLastProcessedPress;

    // ───────────────────────────────────────────────────────────
    // 事件接线
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// 订阅平铺/内容两个容器上的事件（由 <c>EnsureArchiveContent()</c> 在内容区创建后调用一次）。
    /// </summary>
    private void SetupThumbnailLayoutEvents()
    {
        if (_thumbnailLayoutEventsHooked) return;
        _thumbnailLayoutEventsHooked = true;

        HookThumbnailLayoutContainer("TileLayoutScrollViewer");
        HookThumbnailLayoutContainer("ContentLayoutScrollViewer");
    }

    /// <summary>按 x:Name 订阅单个缩略图容器；元素不存在时只记日志（详情模式不受影响）。</summary>
    private void HookThumbnailLayoutContainer(string name)
    {
        var viewer = FindThumbnailLayoutViewer(name);
        if (viewer == null)
        {
            App.DebugLog($"[MainWindow] thumbnail layout container '{name}' not found — thumbnail events not wired");
            return;
        }

        // 按下/移动/松开走 Tunnel：容器内的项模板可能先处理冒泡事件，
        // Tunnel 才能保证「先拿到按下」再让内层做它自己的事（与 DataGrid 的拖拽起点策略一致）。
        viewer.AddHandler(InputElement.PointerPressedEvent, ThumbnailLayout_PointerPressed, RoutingStrategies.Tunnel);
        viewer.AddHandler(InputElement.PointerMovedEvent, ThumbnailLayout_PointerMoved, RoutingStrategies.Tunnel);
        viewer.AddHandler(InputElement.PointerReleasedEvent, ThumbnailLayout_PointerReleased, RoutingStrategies.Tunnel);

        // ScrollViewer 默认不可聚焦：不置 true 则 KeyDown 永远收不到（Enter/Back/Delete 全部失效）。
        // 焦点本身在按下时移到容器（见 ThumbnailLayout_PointerPressed），键盘操作才有落点。
        viewer.Focusable = true;

        viewer.KeyDown += ThumbnailLayout_KeyDown;
        viewer.ContextRequested += ThumbnailLayout_ContextRequested;

        // 双击兜底：项模板根的 DoubleTapped 若已接线会先置 Handled，
        // 容器级订阅（默认不收已处理事件）因此不会重复执行。
        viewer.DoubleTapped += ThumbnailItem_DoubleTapped;
    }

    /// <summary>
    /// 定位缩略图容器：内容区延迟创建，元素可能在本窗口、也可能在内容区里，两处都查。
    /// </summary>
    private ScrollViewer? FindThumbnailLayoutViewer(string name)
        => this.FindControl<ScrollViewer>(name) ?? _archiveContent?.FindControl<ScrollViewer>(name);

    // ───────────────────────────────────────────────────────────
    // 选择层
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// 平铺/内容布局的指针按下（Tunnel 阶段）。
    ///
    /// 左键：命中条目 → 交给 <see cref="ThumbnailSelectionController"/> 解释 Ctrl/Shift → 写回 VM 与逐项 IsSelected，
    /// 并记录拖拽起点（真正的拖拽在 <see cref="ThumbnailLayout_PointerMoved"/> 判定）。
    /// 右键：只把命中条目纳入选区（让右键菜单作用于它），不参与拖拽；明显的多选右键保持不动。
    /// 空白/滚动条：不改变选区，仅丢弃上一次的拖拽起点。
    /// </summary>
    private void ThumbnailLayout_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not Control container) return;

        // 同一次按下可能被两条接线各调一次（程序化订阅 + XAML 属性）——按事件实例判重
        if (ReferenceEquals(_thumbnailLastProcessedPress, e)) return;
        _thumbnailLastProcessedPress = e;

        var point = e.GetCurrentPoint(container);
        var pressedItem = HitTestArchiveItem(e.Source);

        // 键盘操作的落点：把焦点移到容器，Enter/Back/Delete 才会派发到 ThumbnailLayout_KeyDown
        container.Focus();

        // 复选框模式：**整张卡片就是勾选热区**。勾选逻辑全在这里（卡片里的 CheckBox 只负责显示），
        // 否则「按下卡片」与「按下复选框」会各切换一次、互相抵消。
        if (vm.IsCheckboxMode)
        {
            _dragStartEvent = null;
            _dragPreservedSelection = null;

            if (!point.Properties.IsLeftButtonPressed)
            {
                // 右键：命中项未勾选时先勾上，让右键菜单（删除 / 解压）作用于它
                if (pressedItem != null && !pressedItem.IsChecked)
                    ApplyCheckPointer(pressedItem, shift: false, ctrl: false, vm);
                return;
            }

            if (pressedItem == null) return;

            ApplyCheckPointer(
                pressedItem,
                e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                e.KeyModifiers.HasFlag(KeyModifiers.Control),
                vm);
            e.Handled = true;   // 阻止卡片内的 CheckBox 再切换一次
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            // 右键：命中项不在选区时单选它（已在选区内则保持多选，右键菜单作用于整批）
            if (pressedItem != null && !vm.SelectedEntries.Contains(pressedItem))
                ApplyThumbnailSelection(pressedItem, ctrl: false, shift: false, vm);
            return;
        }

        if (pressedItem == null)
        {
            _dragStartEvent = null;
            _dragPreservedSelection = null;
            return;
        }

        // 「按下项是否已在选区内」必须在改写选区**之前**取：
        // 选择控制器把「无修饰键按下」一律收缩为单选，若不留快照，多选状态下拖任一已选条目
        // 只会拖出 1 个（与 DataGrid 侧 Tunnel 阶段保存快照的做法一致，也符合资源管理器语义）。
        var keepMultiSelection = vm.SelectedEntries.Count > 1 && vm.SelectedEntries.Contains(pressedItem);

        ApplyThumbnailSelection(
            pressedItem,
            ctrl: e.KeyModifiers.HasFlag(KeyModifiers.Control),
            shift: e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            vm);

        // 拖拽起点（与 DataGrid 的 Tunnel PointerPressed 同一语义：起点 + 按下事件 + 多选快照）
        _dragStartPoint = point.Position;
        _dragStartEvent = e;
        _dragPreservedSelection = keepMultiSelection
            ? vm.SelectedEntries.Select(m => m.ToCoreItem()).ToList()
            : null;
    }

    /// <summary>
    /// 把一次按下解释成新选区，并写回「VM 三件套 + 逐项 IsSelected」。
    /// 这是缩略图选择的**唯一写入口**（左键 / 右键 / 拖拽前都走这里），保证同步逻辑只有一份。
    /// </summary>
    private void ApplyThumbnailSelection(ArchiveItemModel? pressedItem, bool ctrl, bool shift, MainWindowViewModel vm)
    {
        var result = ThumbnailSelectionController.HandlePointerPressed(
            pressedItem,
            ctrl,
            shift,
            vm.CurrentEntries.ToList(),
            _thumbnailSelectionAnchor,
            vm.SelectedEntries);

        _thumbnailSelectionAnchor = result.Anchor;

        _isApplyingThumbnailSelection = true;
        try
        {
            // 先写主选中项（预览/状态栏/菜单命令都读它），再整体覆盖多选集合
            vm.SelectedEntry = result.Primary;

            vm.SelectedEntries.Clear();
            vm.SelectedEntries.AddRange(result.Selection);

            // 逐项同步必须遍历**全部已加载条目**而不是只改变化的：
            // 被移出选区的项若不重置，切回该目录时还会残留高亮。
            var selected = new HashSet<ArchiveItemModel>(result.Selection);
            foreach (var item in vm.CurrentEntries)
                item.IsSelected = selected.Contains(item);
        }
        finally
        {
            _isApplyingThumbnailSelection = false;
        }
    }

    /// <summary>
    /// 把一次「勾选点击」（复选框模式）解释成勾选变更并写回模型。
    /// <para>
    /// 三条路径（2026-11-03 起选区与勾选解耦）：
    /// ① Shift = 从锚点到该卡**连勾**（只加不减），锚点不变；
    /// ② Ctrl = 切换该卡勾选，并把它并进选区（用 Ctrl 累积一组，随后点其中任意一张即可整组切换）；
    /// ③ 无修饰键 = 若当前选区有 2 项以上且包含该卡，**整组一起切换**成与它相反的状态；否则只切这一张。
    /// </para>
    /// 勾选集合由 VM 维护（<c>OnEntryPropertyChanged</c> → <c>CheckedEntries</c>），这里负责选区的写法与卡片高亮。
    /// </summary>
    private void ApplyCheckPointer(ArchiveItemModel? pressedItem, bool shift, bool ctrl, MainWindowViewModel vm)
    {
        if (shift)
        {
            var ranged = ThumbnailSelectionController.HandleCheckPointerPressed(
                pressedItem,
                shift: true,
                vm.CurrentEntries.ToList(),
                _thumbnailCheckAnchor);

            _thumbnailCheckAnchor = ranged.Anchor;
            foreach (var item in ranged.ToCheck)
                item.IsChecked = true;

            if (ranged.Primary != null)
                vm.SelectedEntry = ranged.Primary;

            return;
        }

        if (pressedItem == null) return;

        if (ctrl)
        {
            // Ctrl：切换该卡勾选，同时把它并进 / 移出选区（用于累积一组）
            var nowChecked = !pressedItem.IsChecked;
            vm.SetChecked(new[] { pressedItem }, nowChecked);

            var selection = new List<ArchiveItemModel>(vm.SelectedEntries);
            if (nowChecked)
            {
                if (!selection.Contains(pressedItem))
                    selection.Add(pressedItem);
            }
            else
            {
                selection.Remove(pressedItem);
            }

            ApplyCardSelection(vm, selection, pressedItem);
            return;
        }

        // 无修饰键：成组切换优先（选区里的行一起变）
        if (vm.ToggleCheckedForGroup(pressedItem, vm.SelectedEntries))
        {
            vm.SelectedEntry = pressedItem;
            return;
        }

        vm.SetChecked(new[] { pressedItem }, !pressedItem.IsChecked);
        ApplyCardSelection(vm, new[] { pressedItem }, pressedItem);
    }

    /// <summary>写回卡片选区（<c>SelectedEntries</c> + 逐项 <c>IsSelected</c>）与主选中项。</summary>
    private static void ApplyCardSelection(
        MainWindowViewModel vm,
        IReadOnlyList<ArchiveItemModel> selection,
        ArchiveItemModel? primary)
    {
        vm.SelectedEntries.Clear();
        vm.SelectedEntries.AddRange(selection);

        var selected = new HashSet<ArchiveItemModel>(selection);
        foreach (var item in vm.CurrentEntries)
            item.IsSelected = selected.Contains(item);

        vm.SelectedEntry = primary ?? (selection.Count > 0 ? selection[0] : null);
    }

    /// <summary>
    /// 命中测试：自命中元素沿视觉树上溯，返回第一个 DataContext 为 <see cref="ArchiveItemModel"/> 的控件。
    /// 返回 null 表示按在空白、列间空隙或滚动条上（这些位置不应改变选区）。
    /// </summary>
    private static ArchiveItemModel? HitTestArchiveItem(object? source)
    {
        for (var v = source as Visual; v != null; v = v.GetVisualParent())
        {
            if (v is Control { DataContext: ArchiveItemModel model })
                return model;
        }
        return null;
    }

    // ───────────────────────────────────────────────────────────
    // 拖出（拖拽解压）
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// 平铺/内容布局的指针移动：跨过 4px 阈值且设置允许时启动拖出，实现与 DataGrid 共用。
    /// </summary>
    private async void ThumbnailLayout_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartEvent is not { } triggerEvent) return;
        if (sender is not Visual root) return;

        if (!TryBeginFileDrag(e.GetPosition(root) - _dragStartPoint)) return;

        // 先取后清：拖拽期间指针事件仍会派发，清空按下态可防止重入
        _dragStartEvent = null;
        var preservedSelection = _dragPreservedSelection;
        _dragPreservedSelection = null;

        if (DataContext is not MainWindowViewModel vm) return;

        // 多选时拖整个选区；单选时拖选中项（按下时已把命中项写进 SelectedEntries）
        var items = vm.SelectedEntries.Count > 0
            ? vm.SelectedEntries.ToList()
            : vm.SelectedEntry is { } single ? new List<ArchiveItemModel> { single } : new List<ArchiveItemModel>();
        if (items.Count == 0) return;

        await StartFileDragAsync(triggerEvent, items, preservedSelection);
    }

    /// <summary>松开即丢弃拖拽起点，避免「单击选中」被后续小幅移动误判成拖拽（与 DataGrid 一致）。</summary>
    private void ThumbnailLayout_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStartEvent = null;
        _dragPreservedSelection = null;
    }

    /// <summary>
    /// 拖拽启动的唯一判据（详情 DataGrid 与平铺/内容容器共用）：
    /// 4px 阈值 + <c>EnableDragExtract</c> 设置开关。
    /// </summary>
    /// <param name="delta">自按下点起的位移。</param>
    private bool TryBeginFileDrag(Point delta)
    {
        // 阈值：镜像 WPF SystemParameters.MinimumHorizontalDragDistance (~4px)，
        // 避免 32px 造成的"拖起来黏手"感
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
            return false;

        if (AppSettings.Load().EnableDragExtract)
            return true;

        // 设置开关关闭：与 DataGrid 原实现一致，丢弃按下态（本次按下不再重试拖拽）
        App.DebugLog("[MainWindow] EnableDragExtract off — drag skipped");
        _dragStartEvent = null;
        _dragPreservedSelection = null;
        return false;
    }

    /// <summary>
    /// 启动文件「拖出」（拖拽解压）的完整流程：覆层窗口 + 状态光标 + 自实现 OLE 拖拽 + 落点后解压。
    /// 详情 DataGrid 与平铺/内容容器都调用这里 —— 覆层/光标/落点逻辑因此只有一份实现。
    /// </summary>
    /// <param name="pressEvent">触发拖拽的按下事件（自实现 OLE 拖拽需要它释放指针捕获）。</param>
    /// <param name="items">命中/选中的条目（模型层），用于在无快照时生成拖拽载荷。</param>
    /// <param name="preservedSelection">
    /// 按下瞬间保留的多选快照（Core 条目）。DataGrid 在 Tunnel 阶段保存它（早于行选中变化）；
    /// 平铺/内容模式按下即已同步选区，传 null 表示直接按 <paramref name="items"/> 拖。
    /// </param>
    private async Task StartFileDragAsync(
        PointerPressedEventArgs pressEvent,
        IReadOnlyList<ArchiveItemModel> items,
        IReadOnlyList<ArchiveItem>? preservedSelection)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var archivePath = vm.CurrentArchivePath;
        if (string.IsNullOrEmpty(archivePath)) return;

        // 拖拽载荷：优先用按下瞬间的快照，否则用传入条目（两者都是 Core 条目，后续解压只认 Core）
        var selectedItems = preservedSelection is { Count: > 0 }
            ? preservedSelection
            : items.Select(m => m.ToCoreItem()).ToList();
        if (selectedItems.Count == 0) return;

        var allItems = vm.GetAllRawItems();
        var password = vm.GetSessionPassword(archivePath);

        // 目录展开成其下的文件（扁平列表）
        var expandedItems = DragDropItemExpander.ExpandItems(selectedItems, allItems);
        if (expandedItems.Count == 0)
            return;

        _isOwnDrag = true;
        vm.StatusMessage = LocalizationManager.T("Status_DragHint");

        // ── 创建 Avalonia 覆层窗口（UI 线程创建，背景线程经 Win32 控制其显隐/置顶） ──
        var overlayWin = new Window
        {
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            Width = 1,
            Height = 1,
            Topmost = true,
            ShowActivated = false,
        };
        overlayWin.Show();

        var overlayHwnd = overlayWin.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        App.DebugLog($"[MainWindow] Overlay HWND=0x{overlayHwnd:X}");
        if (overlayHwnd != nint.Zero)
        {
            var exStyle = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                        | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

            // 去掉标题栏（走 Win32，规避 Avalonia 12 枚举兼容问题）
            var overlayStyle = NativeMethods.GetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(overlayHwnd, NativeMethods.GWL_STYLE,
                overlayStyle & ~0x00C00000u); // clear WS_CAPTION (title bar)

        }

        var mainHwnd = this.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        App.DebugLog($"[MainWindow] Main HWND=0x{mainHwnd:X}");
        using var controller = new OverlayController(overlayHwnd, mainHwnd);
        controller.Start();

        // 拖拽光标：按 overlay 状态加载不同 .cur，源文件位于项目 Resources\Cursors\，
        // 构建时复制到输出目录的 Resources\Cursors\ 下（与 MenuIcons 同模式）。
        // 状态→文件约定：
        //   None/默认        → DragCursor.cur     （金色）
        //   Success(可放置)  → DragCursorOk.cur   （绿色）
        //   Warning(警告)    → DragCursorWarn.cur （红色）
        //   自家窗口         → DragCursorSelf.cur （灰色）
        // 缺失文件回退基础 DragCursor.cur，再回退系统标准箭头（共享句柄，不可销毁）。
        // 仅从文件加载的自定义句柄在拖拽结束后销毁。
        var cursorHandles = new HashSet<nint>();
        var cursorsDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Cursors");

        nint LoadStateCursor(string fileName, nint fallback)
        {
            var path = Path.Combine(cursorsDir, fileName);
            if (File.Exists(path))
            {
                var h = NativeMethods.LoadCursorFromFile(path);
                if (h != nint.Zero)
                {
                    cursorHandles.Add(h);
                    return h;
                }
            }
            return fallback;
        }

        var baseCursor = LoadStateCursor("DragCursor.cur", nint.Zero);
        if (baseCursor == nint.Zero)
            baseCursor = NativeMethods.LoadCursor(nint.Zero, new nint(NativeMethods.OCR_NORMAL));
        var okCursor = LoadStateCursor("DragCursorOk.cur", baseCursor);
        var warnCursor = LoadStateCursor("DragCursorWarn.cur", baseCursor);
        var selfCursor = LoadStateCursor("DragCursorSelf.cur", baseCursor);

        // 每次 GiveFeedback 按 overlay 当前状态动态选光标（与覆层颜色同一状态源）
        Func<nint> cursorProvider = () =>
        {
            if (controller.IsOverOwnWindow)
                return selfCursor;
            return controller.CurrentStatus switch
            {
                DropTargetDetector.DropTargetStatus.Success => okCursor,
                DropTargetDetector.DropTargetStatus.Warning => warnCursor,
                _ => baseCursor
            };
        };

        try
        {
            // 自实现 OLE 拖拽：GiveFeedback 返回 S_OK 并直接 SetCursor 自定义光标。
            // Avalonia 的 DoDragDropAsync 内部固定返回 USEDEFAULTCURSORS，会让系统用
            // LoadCursor(OCR_NO) 显示禁止光标，而替换 OCR_NO 资源表在本机无效（已实证），
            // 因此绕开它自行控制光标。Esc 由 QueryContinueDrag 的 fEscapePressed 处理。
            App.DebugLog("[MainWindow] Custom OLE DoDragDrop START");
            _dragCancelled = false;
            var result = CustomOleDragDrop.PerformDragDrop(
                pressEvent, MantisZipDragFormatName, archivePath,
                cursorProvider, DragDropEffects.Copy,
                () => _dragCancelled = true);
            App.DebugLog($"[MainWindow] Custom OLE DoDragDrop DONE: result={result}");

            // 拖拽结束立刻关闭覆层，先于任何对话框处理
            controller.Stop();
            overlayWin.Close();
            App.DebugLog("[MainWindow] Overlay closed");

            if (_dragCancelled)
            {
                // 用户按 Esc 或右键取消拖拽 → 不执行解压
                App.DebugLog("[MainWindow] Drag cancelled during drag — extraction skipped");
                vm.StatusMessage = LocalizationManager.T("Status_DragDragCancelled");
            }
            else
            {
                NativeMethods.GetCursorPos(out var dropPt);
                App.DebugLog($"[MainWindow] Drop point captured: ({dropPt.X}, {dropPt.Y})");

                vm.StatusMessage = LocalizationManager.T("Status_DragDetectingTarget");
                var dragService = new DragDropService(
                    archivePath, password, this, vm.CurrentFolder ?? "");
                await dragService.ExecuteAfterDropAsync(selectedItems, allItems, vm);
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"[MainWindow] DragDrop EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // 释放自定义光标句柄（仅从文件加载的；共享标准箭头不销毁）
            foreach (var h in cursorHandles)
                NativeMethods.DestroyIcon(h);
            // Safety net: ensure overlay is closed even if early close was skipped
            try { overlayWin.Close(); } catch { }
            try { controller.Stop(); } catch { }
            _isOwnDrag = false;
            App.DebugLog("[MainWindow] DragDrop cleanup done");
        }
    }

    // ───────────────────────────────────────────────────────────
    // 双击 / 键盘（与详情 DataGrid 共用）
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// 双击/Enter 的统一行为：目录 → 进入该目录；文件 → 提取到临时目录并用系统默认程序打开
    /// （提取失败/需密码等提示由 <c>OpenEntryWithDefaultAppAsync</c> 自己处理）。
    /// </summary>
    private async Task HandleItemDoubleTapAsync(ArchiveItemModel? item)
    {
        if (item == null) return;
        if (DataContext is not MainWindowViewModel vm) return;

        if (item.IsDirectory)
        {
            vm.NavigateToFolderPath(item.FullPath);
            return;
        }

        // 文件双击：提取到临时目录并用系统默认方式打开
        if (!string.IsNullOrEmpty(vm.CurrentArchivePath))
            await vm.OpenEntryWithDefaultAppAsync(item);
    }

    /// <summary>
    /// 项双击：与详情 DataGrid 的双击是同一入口（<see cref="HandleItemDoubleTapAsync"/>）。
    /// 先置 <c>Handled</c> 再执行，容器级兜底订阅因此不会把同一次双击处理两遍。
    /// </summary>
    private async void ThumbnailItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;

        var item = HitTestArchiveItem(e.Source);
        if (item == null) return;
        await HandleItemDoubleTapAsync(item);
    }

    /// <summary>
    /// 项单击：选中已在 Tunnel 的 <see cref="ThumbnailLayout_PointerPressed"/> 里完成，这里有意留空。
    /// 保留空方法体是为了让 XAML 侧的 <c>Tapped</c> 属性接线能被解析（两处各写一份状态会互相覆盖：
    /// Tapped 与 PointerPressed 的触发顺序/命中元素并不一致）。
    /// </summary>
    private void ThumbnailItem_Tapped(object? sender, TappedEventArgs e)
    {
    }

    /// <summary>
    /// 平铺/内容布局的键盘操作：与详情 DataGrid 完全同一实现
    /// （Enter = 双击行为、Back = 上溯一级、Delete = 删除选中项）。
    /// </summary>
    private void ThumbnailLayout_KeyDown(object? sender, KeyEventArgs e)
    {
        // 防双触发：XAML 若同时挂了同名属性，先执行的那次已把事件标记为 Handled
        if (e.Handled) return;
        HandleFileListKeyDown(e);
    }

    /// <summary>
    /// 文件列表键盘操作（详情 DataGrid 与平铺/内容容器共用）：
    /// Enter = 双击行为、Back = 上溯一级、Delete = 删除选中项。
    /// 选中项来源：详情模式取 DataGrid 的 SelectedItem，平铺/内容模式退回 VM 的 SelectedEntry。
    /// </summary>
    private void HandleFileListKeyDown(KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // 内容区未创建时不主动触发创建（键盘事件只可能在内容区可见时到达）
        var item = _archiveContent?.FileListGridControl.SelectedItem as ArchiveItemModel ?? vm.SelectedEntry;

        switch (e.Key)
        {
            case Key.Enter:
                if (item == null) return;
                e.Handled = true;
                // 与双击同一入口：目录进目录、文件走「提取到临时目录并打开」。
                // 异步部分不阻塞按键处理；异常已由 OpenEntryWithDefaultAppAsync 内部兜住。
                _ = HandleItemDoubleTapAsync(item);
                break;

            case Key.Back:
                vm.GoUpCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete:
                vm.DeleteFilesCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ───────────────────────────────────────────────────────────
    // 右键
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// 平铺/内容布局的右键：先把命中条目纳入选区（已在选区内则保持多选），
    /// 随后**不拦截**事件，由 XAML 挂在 ScrollViewer 上的 ContextMenu 按默认行为打开。
    ///
    /// 顺序是语义要求而非实现细节：菜单命令读的是 VM 的选中项，未选中的条目上右键
    /// 若不先选中，菜单就会作用在上一个选中条目上。
    /// </summary>
    private void ThumbnailLayout_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var hit = HitTestArchiveItem(e.Source);
        if (hit != null && !vm.SelectedEntries.Contains(hit))
            ApplyThumbnailSelection(hit, ctrl: false, shift: false, vm);

        // Avalonia 12：ContextRequested 未置 Handled 时，挂在控件上的 ContextMenu 会自行打开，
        // 因此这里显式保持 false（鼠标右键的选中已在上面的 PointerPressed 里做过，
        // 这条路径同时兜住键盘菜单键 / 触控长按）。
        e.Handled = false;
    }
}
