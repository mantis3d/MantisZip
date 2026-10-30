using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 解压设置窗口。点「开始解压」时由 <see cref="ExtractSettingsViewModel.TryConfirmAsync"/> 一次产出
/// <see cref="ConfirmOutcome{T}"/>，窗口只负责「拒绝留窗 / 确认或取消关窗」。
/// <para>
/// 两种用法：主窗口与诊断菜单用 <c>ShowDialog&lt;ConfirmOutcome&lt;ExtractSettings&gt;&gt;</c>（有属主）；
/// CLI 没有主窗口，只能非模态 <see cref="Window.Show"/> 后 await <see cref="Completion"/>。
/// 两者都不回读窗口属性——载荷在确认事务里已冻结。
/// </para>
/// <para>
/// 文件/目录选择按 AGENTS.md 规则 16「设置窗内嵌选择」：窗口内直接内嵌
/// <see cref="CustomFilePickerControl"/>（输出设置 = PickFolder、添加压缩包 = PickItems），
/// 不再为「设置窗里再弹一个选择器」新增 <c>ShowXxxAsync</c> 调用。
/// </para>
/// </summary>
public partial class ExtractSettingsWindow : Window
{
    private bool _loaded;

    /// <summary>后台逐包校验的取消源（窗口关闭时取消，避免无谓 IO）。</summary>
    private CancellationTokenSource? _validationCts;

    /// <summary>两个内嵌选择器完成 <c>Initialize</c> 之前，导航产生的事件不得回写 ViewModel。</summary>
    private bool _pickersReady;

    /// <summary>
    /// 选择器 ↔ 「已选压缩包」列表的同步闸门：挡住「回填选择器 → 选择器 SelectionChanged → 又追加」的回环。
    /// </summary>
    private bool _syncingArchiveSelection;

    /// <summary>注入的执行器；设计时窗口（无参构造）没有它，也就不能真正开始解压。</summary>
    private readonly IExtractSession? _session;

    /// <summary>注入的运行上下文（宿主差异：Owner / Presenter / 退出策略）。</summary>
    private readonly ExtractRunContext? _context;

    /// <summary>非模态 Show（CLI 无属主窗口）时的确认结果通道：窗口关闭即完成。</summary>
    private readonly TaskCompletionSource<ConfirmOutcome<ExtractSettings>> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>本次关窗携带的确认结果；null 表示用户直接按 X 关闭（视为取消）。</summary>
    private ConfirmOutcome<ExtractSettings>? _closedOutcome;

    /// <summary>
    /// ViewModel，公开给设计时/诊断窗口使用；生产调用方不应回读它构造请求——载荷在确认事务里已冻结。
    /// setter 私有：只有构造函数经 <see cref="Initialize"/> 装配一次。
    /// </summary>
    public ExtractSettingsViewModel ViewModel { get; private set; }

    /// <summary>
    /// 非模态 <see cref="Window.Show"/> 时的确认结果。CLI 没有属主窗口，无法用 ShowDialog，
    /// 只能 Show + 等关窗；关窗即完成，用户按 X 视为取消。
    /// </summary>
    public Task<ConfirmOutcome<ExtractSettings>> Completion => _completion.Task;

    /// <summary>
    /// 设计时/诊断菜单用的无参构造：只装配界面，不注入执行器，因此不能执行解压。
    /// Avalonia 的 x:Class 也要求公共无参构造存在。
    /// </summary>
    public ExtractSettingsWindow()
    {
        ViewModel = Initialize(Array.Empty<string>(), hideAddArchivesTab: false);
        SubscribeViewModel();
    }

    /// <summary>
    /// 生产构造：执行器与运行上下文由宿主注入，确认结果走 <see cref="ConfirmOutcome{T}"/>，
    /// 调用方不再回读窗口状态。
    /// 之所以做成必填依赖，是为了让「设置窗口自己就能执行」在类型上成立——
    /// 调用方再也拿不到「ShowDialog&lt;bool&gt; 之后再回读窗口属性」这条后门。
    /// </summary>
    /// <param name="session">解压执行器。</param>
    /// <param name="context">宿主差异（Owner / Presenter / 退出策略）。</param>
    /// <param name="archivePaths">待解压的压缩包路径。</param>
    public ExtractSettingsWindow(
        IExtractSession session,
        ExtractRunContext context,
        IReadOnlyList<string> archivePaths)
        : this(session, context, archivePaths, hideAddArchivesTab: false)
    {
    }

    /// <summary>
    /// 生产构造（带「添加压缩包」页显隐）：拖拽 / 右键「解压到」等入口的压缩包已由场景确定，
    /// 不需要再让用户加包时传 <paramref name="hideAddArchivesTab"/> = <see langword="true"/>。
    /// </summary>
    /// <param name="session">解压执行器。</param>
    /// <param name="context">宿主差异（Owner / Presenter / 退出策略）。</param>
    /// <param name="archivePaths">待解压的压缩包路径。</param>
    /// <param name="hideAddArchivesTab">是否隐藏「添加压缩包」选项卡。</param>
    public ExtractSettingsWindow(
        IExtractSession session,
        ExtractRunContext context,
        IReadOnlyList<string> archivePaths,
        bool hideAddArchivesTab = false,
        bool entrySelectionMode = false,
        string? entryCurrentFolder = null,
        bool entryPreserveFullPath = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        _session = session;
        _context = context;

        ViewModel = Initialize(archivePaths, hideAddArchivesTab, entrySelectionMode, entryCurrentFolder, entryPreserveFullPath);
        SubscribeViewModel();
    }

    /// <summary>
    /// 「选择要解压的文件」入口（v0.5.5 第三步后半）：右键「解压到」与拖拽兜底共用。
    /// 窗口自己就是执行入口（确认即载荷 + 唯一执行器），返回 true 表示已确认并执行（含用户取消 → false）。
    /// </summary>
    public static async Task<bool> ShowEntrySelectionAsync(
        Window owner,
        IExtractSession session,
        ExtractRunContext context,
        IReadOnlyList<string> archivePaths,
        IReadOnlyList<ArchiveItem> entries,
        string initialDestination,
        string? currentFolder,
        bool preserveFullPath,
        string? password)
    {
        var dialog = new ExtractSettingsWindow(
            session, context, archivePaths,
            hideAddArchivesTab: true,
            entrySelectionMode: true,
            entryCurrentFolder: currentFolder,
            entryPreserveFullPath: preserveFullPath);

        if (!string.IsNullOrWhiteSpace(initialDestination))
            dialog.ViewModel.DestinationPath = initialDestination;
        if (!string.IsNullOrEmpty(password))
            dialog.ViewModel.SetKnownPassword(password);
        dialog.SetEntries(entries);

        var outcome = await dialog.ShowDialog<ConfirmOutcome<ExtractSettings>>(owner);
        return outcome is Confirmed<ExtractSettings>;
    }

    /// <summary>
    /// 装配确认事务的结局出口与提示回调：拒绝留窗（原因弹在窗口上），确认/取消关窗。
    /// 设计时窗口也要订阅，否则从诊断菜单打开后点「开始解压」会静默无反应。
    /// </summary>
    private void SubscribeViewModel()
    {
        ViewModel.ConfirmCompleted += OnConfirmCompleted;

        // 消息提示回调（弹窗以本窗口为 owner）：拒绝原因必须让用户看见。
        ViewModel.ShowMessage = (msg, title) =>
            AppMessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning, this);

        Closed += OnClosed;
    }

    /// <summary>
    /// 把确认结局翻译成窗口动作：拒绝留窗、确认/取消关窗；关窗携带的结果供
    /// <see cref="Completion"/> 与 <c>ShowDialog&lt;ConfirmOutcome&lt;…&gt;&gt;</c> 读取。
    /// </summary>
    private void OnConfirmCompleted(ConfirmOutcome<ExtractSettings> outcome)
    {
        switch (outcome)
        {
            case Rejected<ExtractSettings> rejected:
                // 拒绝必须留在窗口并显示原因：静默关窗会让用户以为解压开始了。
                if (ViewModel.ShowMessage != null)
                    _ = ViewModel.ShowMessage(rejected.Reason, LocalizationManager.T("Progress_Title_Extract"));
                return;

            default:
                _closedOutcome = outcome;
                Close(outcome);
                return;
        }
    }

    /// <summary>关窗统一出口：解除订阅并把确认结果（缺省 = 取消）交给等待方。</summary>
    /// <summary>恢复上次关闭时保存的窗口尺寸与左侧设置列宽度（2026-10-30）。</summary>
    private void ApplyPersistedGeometry()
    {
        var settings = AppSettings.Load();
        WindowPlacement.Apply(this, settings.ExtractWindowWidth, settings.ExtractWindowHeight);
        WindowPlacement.ApplyLeftColumn(RootGrid, settings.ExtractWindowLeftColumnWidth, 620);
    }

    /// <summary>关闭时写回窗口尺寸与左侧设置列宽度（非法值回落 XAML 默认 1180×760 / 620）。</summary>
    private void PersistGeometry()
    {
        var settings = AppSettings.Load();
        settings.ExtractWindowWidth = WindowPlacement.Capture(Width, 1180);
        settings.ExtractWindowHeight = WindowPlacement.Capture(Height, 760);
        settings.ExtractWindowLeftColumnWidth = WindowPlacement.CaptureLeftColumn(RootGrid, 620);
        settings.Save();
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        PersistGeometry();
        ViewModel.ConfirmCompleted -= OnConfirmCompleted;
        _completion.TrySetResult(_closedOutcome ?? new Cancelled<ExtractSettings>());
    }

    /// <summary>
    /// 预置「已知密码」（会话内记住的密码）：入口在打开对话框前调用，随确认载荷交给执行器。
    /// </summary>
    /// <param name="password">入口已确认的密码；无则传 <see langword="null"/>。</param>
    public void SetKnownPassword(string? password) => ViewModel.SetKnownPassword(password);

    /// <summary>
    /// 预置本次运行的密码策略：桌面入口只使用已知密码，CLI 入口才回退密码库匹配。
    /// </summary>
    /// <param name="policy">入口选定的取密策略。</param>
    public void SetPasswordPolicy(ExtractPasswordPolicy policy) => ViewModel.SetPasswordPolicy(policy);

    /// <summary>
    /// 用注入的执行器跑一次已确认的解压载荷（宿主只处理 <see cref="ExtractOutcome"/> 四态）。
    /// </summary>
    public Task<ExtractOutcome> RunConfirmedAsync(ExtractSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_session is null || _context is null)
        {
            return Task.FromResult(
                ExtractOutcome.NotStarted(LocalizationManager.T("Settings_Window_NoExecutor")));
        }

        return _session.RunAsync(settings, _context);
    }

    /// <summary>
    /// 构造期的界面装配：建 ViewModel、接好内嵌选择器 / 解锁 / 校验等宿主回调。
    /// 返回装配好的 ViewModel，让构造函数以赋值表达式拿到确定非空的属性——避免构造函数里
    /// 「调用方法后才赋值」造成的可空流分析告警。
    /// </summary>
    /// <param name="archivePaths">待解压的压缩包路径（设计时为空）。</param>
    /// <param name="hideAddArchivesTab">是否隐藏「添加压缩包」选项卡。</param>
    private ExtractSettingsViewModel Initialize(IReadOnlyList<string> archivePaths, bool hideAddArchivesTab, bool entrySelectionMode = false, string? entryCurrentFolder = null, bool entryPreserveFullPath = true)
    {
        InitializeComponent();

        ViewModel = new ExtractSettingsViewModel(archivePaths);

        DataContext = ViewModel;

        // 「添加压缩包」页按场景显隐（拖拽 / 「解压到」等已由入口确定压缩包时隐藏）
        AddArchivesTab.IsVisible = !hideAddArchivesTab;

        // 条目选择模式（右键「解压到」/ 拖拽兜底）：隐藏加包页，改显「选择要解压的文件」页并默认选中它
        EntrySelectionTab.IsVisible = entrySelectionMode;
        if (entrySelectionMode)
        {
            AddArchivesTab.IsVisible = false;
            ViewModel.InitializeEntrySelection(Array.Empty<ArchiveItem>(), entryCurrentFolder, entryPreserveFullPath);
            EntrySelectionTab.IsSelected = true;
        }
        // 恢复上次关闭时保存的窗口尺寸与左侧设置列宽度（2026-10-30）
        ApplyPersistedGeometry();

        // 窗口关闭时取消后台逐包校验 + 冲突扫描，并断开列表回填订阅
        Closed += (_, _) =>
        {
            _validationCts?.Cancel();
            ViewModel.CancelConflictScan();
            ViewModel.SourceItems.CollectionChanged -= OnSourceItemsChanged;
        };

        // ── 内嵌选择器：输出设置（PickFolder）──
        // 切换浏览目录 = 更改解压目标；Initialize 期间的导航（把默认目标解析到最近存在的父目录）不回写，
        // 否则会把默认的「目录\包名」覆盖成包所在目录。
        DestinationPicker.PathChanged += OnDestinationPickerPathChanged;
        DestinationPicker.Initialize(initialPath: ViewModel.DestinationPath);

        // ── 内嵌选择器：添加压缩包（PickItems）──
        // 扩展名清单取 Core 的单一事实源（ArchiveExtensionCatalog），不在这里硬编码。
        ArchivePicker.SelectionChanged += OnArchivePickerSelectionChanged;
        var archiveStartDir = ViewModel.SourceItems.Count > 0
            ? System.IO.Path.GetDirectoryName(ViewModel.SourceItems[0].Path) ?? string.Empty
            : string.Empty;
        ArchivePicker.Initialize(
            initialPath: archiveStartDir,
            fileExtensions: ArchiveExtensionCatalog.EngineExtensions.ToArray());

        // PickItems 控件自带右侧「累积选择」面板；本窗改由下方「已选压缩包」列表承担（计划第③点），
        // 控件本身本轮冻结，故由宿主按控件内部命名折叠。
        CollapsePickerAccumulatedPanel(ArchivePicker);

        // 「高级模式」开关与压缩窗对称（2026-10-30 用户反馈）：关闭 = 单击即追加、切目录清空；
        // 打开 = 跨目录累积（每个条目显示复选框）。默认关闭。
        AdvancedModeCheck.IsChecked = false;
        ArchivePicker.IsSimpleMode = true;

        // PickItems 简单模式进入目录时会清空预选：Initialize 之后再把已选包回填进选择器。
        SyncArchivePickerFromSourceItems();
        ViewModel.SourceItems.CollectionChanged += OnSourceItemsChanged;

        _pickersReady = true;

        // ── 手动解锁（自动匹配失败后的补救）：复用主流程 PasswordDialog ──
        ViewModel.ShowUnlockDialog = async (archivePath) =>
        {
            var pwdDialog = new Views.PasswordDialog(System.IO.Path.GetFileName(archivePath));
            var ok = await pwdDialog.ShowDialog<bool>(this);
            if (!ok) return null;
            return new PasswordDialogResponse
            {
                Password = pwdDialog.Password,
                RememberInSession = pwdDialog.RememberInSession,
                SavePermanently = pwdDialog.SavePermanently,
                Description = pwdDialog.Description,
                Patterns = pwdDialog.Patterns
            };
        };

        // 双击列表中的锁定/加密未匹配行 → 手动输密码
        FileListBox.DoubleTapped += async (_, _) =>
        {
            if (ExtractSettingsViewModel.IsUnlockable(ViewModel.SelectedSourceItem) && ViewModel.SelectedSourceItem != null)
                await ViewModel.UnlockManuallyAsync(ViewModel.SelectedSourceItem);
        };

        // 「已选压缩包」列表：Delete 逐项移除（回填选择器由集合事件统一处理）
        FileListBox.KeyDown += OnFileListBoxKeyDown;

        // 双击树上 🔒 占位节点 → 同上（占位节点 FullPath = 源包路径）
        PreviewTree.NodeDoubleTapped += async (_, node) =>
        {
            var item = ViewModel.SourceItems.FirstOrDefault(i =>
                string.Equals(i.Path, node.FullPath, StringComparison.OrdinalIgnoreCase));
            if (ExtractSettingsViewModel.IsUnlockable(item))
                await ViewModel.UnlockManuallyAsync(item!);
        };

        Loaded += OnLoaded;

        return ViewModel;
    }

    /// <summary>
    /// 输出设置页：浏览目录变化即写回解压目标（选择器只在用户导航后触发，初始化期间的事件被忽略）。
    /// </summary>
    /// <summary>
    /// 「高级模式」开关：关闭 = 简单模式（单击即追加、切目录清空）；打开 = 跨目录累积（带复选框）。
    /// 与压缩窗 <c>CompressSettingsWindow.OnAdvancedModeChanged</c> 同一语义，仅宿主不同。
    /// </summary>
    private void OnAdvancedModeChanged(object? sender, RoutedEventArgs e)
    {
        if (!_pickersReady && ArchivePicker is null) return;
        ArchivePicker.IsSimpleMode = AdvancedModeCheck.IsChecked != true;
    }
    private void OnDestinationPickerPathChanged(object? sender, string path)
    {
        if (!_pickersReady || string.IsNullOrWhiteSpace(path)) return;
        ViewModel.DestinationPath = path;
    }

    /// <summary>
    /// 添加压缩包页：选择器 → ViewModel **只追加**。
    /// <para>
    /// PickItems 简单模式的语义是「当前目录列表选中项 = 结果」：切目录、单击目录行都会把累积集合
    /// 清空或整体替换。若把「不在 <c>SelectedPaths</c> 里」当作删除，一次目录导航就会清空已选列表，
    /// 所以这里只取差集新增；删除只走下方「已选压缩包」列表（行删除按钮 / Delete 键）。
    /// </para>
    /// </summary>
    private void OnArchivePickerSelectionChanged(object? sender, EventArgs e)
    {
        if (!_pickersReady || _syncingArchiveSelection) return;

        var known = ViewModel.SourceItems
            .Select(item => item.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = ArchivePicker.SelectedPaths.Where(path => !known.Contains(path)).ToList();
        if (added.Count == 0) return;

        // 追加会触发 SourceItems.CollectionChanged：置闸门避免它又回填选择器（回环）
        _syncingArchiveSelection = true;
        try
        {
            ViewModel.AddSourceArchives(added);
        }
        finally
        {
            _syncingArchiveSelection = false;
        }

        // 新行状态是 Pending：补跑逐包校验（已校验行不会被重复读取）
        _ = ValidateSourceArchivesAsync();
    }

    /// <summary>
    /// 「已选压缩包」列表增删 → 把行集合回填到选择器选中项（集合事件是唯一回填入口，
    /// 保证删除后选择器不再显示已移除的包；<c>SourceArchiveItem</c> 行对象本身自始至终没被重建）。
    /// </summary>
    private void OnSourceItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_pickersReady || _syncingArchiveSelection) return;
        SyncArchivePickerFromSourceItems();
    }

    /// <summary>把「已选压缩包」的路径集合回填进选择器（闸门内执行，选择器回抛的事件被忽略）。</summary>
    private void SyncArchivePickerFromSourceItems()
    {
        _syncingArchiveSelection = true;
        try
        {
            ArchivePicker.SetSelectedPaths(ViewModel.SourceItems.Select(item => item.Path).ToList());
        }
        finally
        {
            _syncingArchiveSelection = false;
        }
    }

    /// <summary>新增压缩包后补跑逐包校验；窗口已关闭（取消源已取消）时静默返回。</summary>
    private async Task ValidateSourceArchivesAsync()
    {
        try
        {
            await ViewModel.ValidateAllAsync(_validationCts?.Token ?? default);
        }
        catch (OperationCanceledException)
        {
            // 窗口关闭 / 校验被取消：行状态已回退 Pending，无需提示
        }
    }

    /// <summary>「已选压缩包」列表键盘删除（逐项删除，不重建列表）。</summary>
    private void OnFileListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        // 只认 Delete：Backspace 容易误删（列表里按退格通常被理解为「返回」，不是「移除」）
        if (e.Key != Key.Delete) return;
        if (ViewModel.SelectedSourceItem is null) return;

        ViewModel.RemoveSelectedSourceArchiveCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// 折叠 <see cref="CustomFilePickerControl"/> 在 PickItems 模式自带的右侧「累积选择」面板：
    /// 计划第③点要求去掉它、改由下方「已选压缩包」列表承担（否则 620px 左栏被内部右栏再切走 260px，
    /// 文件列表只剩两百来像素，且与下方列表语义重复）。
    /// 控件本轮冻结（只许改本窗口的三个文件），故按控件内部命名折叠；任一元素缺失时静默降级
    /// （面板照旧显示，功能不受影响），不抛异常让整个设置窗口不可用。
    /// </summary>
    private static void CollapsePickerAccumulatedPanel(CustomFilePickerControl picker)
    {
        if (FindPickerChild<Control>(picker, "PickItemsPanel") is { } panel)
            panel.IsVisible = false;

        if (FindPickerChild<Control>(picker, "RightSplitter") is { } splitter)
            splitter.IsVisible = false;

        if (FindPickerChild<Grid>(picker, "BrowserGrid") is not { } browser) return;

        // 右栏列（第 5 列）与分割条列（第 4 列）：Auto + 0 宽，把空间全部还给文件列表
        if (browser.ColumnDefinitions.Count > 4)
        {
            var rightColumn = browser.ColumnDefinitions[4];
            rightColumn.Width = GridLength.Auto;
            rightColumn.MinWidth = 0;
            rightColumn.MaxWidth = double.PositiveInfinity;
        }

        if (browser.ColumnDefinitions.Count > 3)
            browser.ColumnDefinitions[3].Width = new GridLength(0);
    }

    /// <summary>
    /// 按名称取控件内部元素：先走名字域 <see cref="Control.FindControl{T}"/>，
    /// 取不到再遍历逻辑树兜底（宿主折叠控件内部面板用，不依赖其视觉树已生成）。
    /// </summary>
    private static T? FindPickerChild<T>(Control root, string name) where T : Control
        => root.FindControl<T>(name)
           ?? ((ILogical)root).GetLogicalDescendants()
               .OfType<T>()
               .FirstOrDefault(child => child.Name == name);

    /// <summary>
    /// 注入首包（当前打开压缩包）的条目列表——MainWindow 单包路径使用：
    /// 条目已在内存，跳过对首包的重复读取；过滤统计与冲突预览立即就绪。
    /// </summary>
    public void SetEntries(IReadOnlyList<ArchiveItem> entries)
        => ViewModel.SetFirstArchiveEntries(entries);

    /// <summary>
    /// 读取过滤控件当前条件（仅窗口自用）：供过滤统计、预览灰显与确认事务的过滤快照使用。
    /// 仅当启用过滤且 <c>filter.IsActive</c> 时返回非 null；对外不再是公开出口。
    /// </summary>
    private FileFilterCriteria? GetFilter()
    {
        if (FileFilterControl == null) return null;
        if (!FileFilterControl.IsFilterEnabled) return null;
        var filter = FileFilterControl.GetFilter();
        return filter.IsActive ? filter : null;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        InitFileFilter();

        // 注入过滤条件读取器（选中切换重建 / 确认事务的过滤快照共用）
        ViewModel.FilterProvider = () => GetFilter();

        // Subscribe to DestinationPath changes for preview rebuild
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 冲突标记全量完成后刷新视图（冲突计数、高亮等）
        ViewModel.PreviewTreeInvalidated += (_, _) =>
        {
            Dispatcher.UIThread.Post(() => PreviewTree.RefreshDisplay());
        };

        // 首包条目已由外部注入（MainWindow 单包路径）时立即构建冲突预览；
        // 否则 ValidateAllAsync 完成首包校验后自动填充 ⏳→树
        if (ViewModel.FirstArchiveEntries != null && !string.IsNullOrWhiteSpace(ViewModel.DestinationPath))
            ViewModel.RebuildMergedPreview();

        // 逐包后台校验（损坏 / 需密码 → 行内徽标 + 预览占位；窗口关闭时经 CTS 取消）
        _validationCts = new CancellationTokenSource();
        _ = ViewModel.ValidateAllAsync(_validationCts.Token);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExtractSettingsViewModel.DestinationPath))
        {
            // 目标路径变化 → 冲突高亮全量失效，重建所有子树
            ViewModel.InvalidatePreviewCache();
            ViewModel.RebuildMergedPreview();
        }
    }

    /// <summary>初始化文件过滤控件（预设 + 事件）。</summary>
    private void InitFileFilter()
    {
        var settings = AppSettings.Load();
        FileFilterControl.LoadPresets(settings.FilterPresets);

        FileFilterControl.SavePresetRequested += name =>
        {
            var filter = FileFilterControl.GetFilter();
            if (!filter.IsActive) return;
            settings.AddPreset(new FileFilterPreset(name, filter));
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets);
        };

        FileFilterControl.DeletePresetRequested += preset =>
        {
            settings.FilterPresets.Remove(preset);
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets);
        };

        FileFilterControl.RenamePresetRequested += (preset, newName) =>
        {
            var existing = settings.FilterPresets.FirstOrDefault(p => p.Name == newName);
            if (existing != null)
            {
                _ = AppMessageBox.Show(
                    LocalizationManager.T("FileFilter_PresetNameExists"),
                    LocalizationManager.T("FileFilter_RenamePresetTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    this);
                return;
            }
            preset.Name = newName;
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets, newName);
        };

        // 过滤条件变更时更新统计并重建预览树
        UpdateFilterStats();
        FileFilterControl.FilterChanged += OnFileFilterChanged;
    }

    private void UpdateFilterStats()
    {
        var entries = ViewModel.FirstArchiveEntries;
        if (entries == null) return;

        var filter = GetFilter();
        if (filter != null)
        {
            var matched = entries.Count(e => !e.IsDirectory && FileFilterMatcher.IsMatch(filter, e));
            var total = entries.Count(e => !e.IsDirectory);
            FileFilterControl.SetFilterStats(LocalizationManager.T("Extract_FilterStatsFormat", matched, total));
        }
        else
        {
            FileFilterControl.SetFilterStats("");
        }
    }

    private void OnFileFilterChanged()
    {
        UpdateFilterStats();
        // 过滤灰显标记全量失效
        ViewModel.InvalidatePreviewCache();
        ViewModel.RebuildMergedPreview();
    }
}
