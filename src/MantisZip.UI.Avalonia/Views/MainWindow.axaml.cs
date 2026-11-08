using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Converters;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Layout;
using Avalonia.Media;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _isOwnDrag;
    private PointerPressedEventArgs? _dragStartEvent;
    private Point _dragStartPoint;
    private List<ArchiveItem>? _dragPreservedSelection;
    private string? _lastSortMemberPath;
    private bool _lastSortDescending;

    /// <summary>拖拽时写入数据对象的自定义格式名（使 IDataObject 非空，避免 Explorer 显示禁止光标）</summary>
    private const string MantisZipDragFormatName = "MantisZipDragFormat";

    /// <summary>文件列表图标列的持久化标识（该列无 SortMemberPath，用 Tag="Icon" 标识以便持久化列位置）</summary>
    private const string IconColumnTag = "Icon";

    /// <summary>拖拽期间是否被取消（Esc 或右键取消手势，由自实现 IDropSource.QueryContinueDrag 同步置位）</summary>
    private bool _dragCancelled;

    /// <summary>预览位置切换时各位置（1=底部, 2=目录树下方, 3=文件列表下方, 4=右侧）的记忆尺寸（含单元类型：Star 保持比例伸缩、Pixel 固定），切换后恢复用</summary>
    private readonly Dictionary<int, GridLength> _previewSizeByPosition = new();

    /// <summary>当前已应用的预览位置（切换前据此保存旧位置尺寸）</summary>
    private int _lastAppliedPreviewPosition = 4;

    /// <summary>预览面板显隐（与 AppSettings.ShowPreviewPanel 同步；false 时压缩占位行列，树/列表撑满）</summary>
    private bool _previewPanelEnabled = true;

    /// <summary>预览面板是否已延迟创建（EnsurePreviewPanel 幂等标记）。</summary>
    private bool _previewPanelCreated;

    /// <summary>拖拽添加覆层呼吸动画计时器（100ms tick，正弦 alpha 40-120，与拖拽解压 OverlayController 参数一致）</summary>
    private readonly DispatcherTimer _dragAddOverlayTimer;

    /// <summary>覆层呼吸动画 tick 计数（显示时从 0 复位）</summary>
    private int _dragAddOverlayTick;

    /// <summary>过滤栏是否已创建（延迟加载：默认隐藏时不在启动路径实例化）</summary>
    private bool _filterBarCreated;

    /// <summary>四个顶级菜单的子项是否已创建（延迟加载：首次交互时创建）</summary>
    private bool _fileMenuItemsCreated;
    private bool _editMenuItemsCreated;
    private bool _viewMenuItemsCreated;
    private bool _toolsMenuItemsCreated;

    /// <summary>压缩包内容区是否已创建（延迟加载：2026-10-21 启动优化）</summary>
    private bool _archiveContentCreated;

    /// <summary>延迟创建的内容区控件（目录树 / 文件列表 / 预览面板 / 拖拽覆层）</summary>
    private Controls.ArchiveContentView? _archiveContent;

    /// <summary>待应用的列状态快照（内容区创建后才应用）</summary>
    private List<WindowStateManager.ColumnStateDto>? _pendingColumnStates;

    public MainWindow()
    {
        Services.StartupTimer.Mark("Win.Ctor.Enter");
        InitializeComponent();
        // 1359 行 XAML 解析 + 子控件构造（含内嵌 PreviewPanel 855 行控件树）
        Services.StartupTimer.Mark("Win.Xaml");

        // 拖拽添加覆层呼吸动画（与拖拽解压覆层一致：约 2s 周期，正弦 alpha 40-120，仅背景层呼吸）
        _dragAddOverlayTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher.UIThread)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _dragAddOverlayTimer.Tick += OnDragAddOverlayTimerTick;

        // 窗口图标：从嵌入资源加载（与 WPF 版 Icon="/Resources/App.ico" 一致）
        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://MantisZip.UI.Avalonia/Resources/App.ico"));
            Icon = new WindowIcon(iconStream);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[MainWindow] Failed to load window icon: {ex.Message}");
        }

        // 读取持久化的列状态/排序/布局快照（window.json）。
        // 2026-10-21 启动优化：压缩包内容区（目录树/文件列表/预览）已改为**延迟创建**，
        // 启动时这些控件尚不存在 → 此处只把快照存起来，等 EnsureArchiveContent() 创建控件后再应用。
        var columnStates = WindowStateManager.Load(
            this, out var savedSortColumnPath, out var savedSortDirection, out var hadWindowSnapshot);
        _pendingColumnStates = columnStates;

        // 恢复持久化的列排序状态（方向编码与 WPF window.json 兼容：0=无, 1=升序, 2=降序）。
        // 出厂默认 = 名称升序（2026-11-08）：仅当**从未有过 window.json**（首次运行）时生效；
        // 已有快照但 SortColumnPath 为空 = 用户点列头第三下主动清除排序，必须保持不排序。
        _lastSortMemberPath = !string.IsNullOrEmpty(savedSortColumnPath)
            ? savedSortColumnPath
            : hadWindowSnapshot ? null : "Name";
        _lastSortDescending = savedSortDirection == 2;
        // WindowStateManager.Load 读盘 + 列状态/排序恢复
        Services.StartupTimer.Mark("Win.State");

        // 布局快照（目录树/文件列表列宽 + 预览各位置记忆尺寸）与预览位置的应用
        // 同样延后到内容区创建后（ApplyPreviewLayout 见 EnsureArchiveContent）
        Services.StartupTimer.Mark("Win.Layout");

        var vm = new MainWindowViewModel();
        // 主 VM 构造（内含 PreviewViewModel 构造）
        Services.StartupTimer.Mark("Win.VM");

        // 桌面进度呈现器：压缩/解压/测试统一经它开窗、收尾（窗口生命周期只写一次）。
        // 状态栏回写端口只服务「密码重试被取消」这类跨层提示，presenter 本身不碰 ViewModel。
        var progressPresenter = new MainWindowProgressPresenter(message => vm.StatusMessage = message);
        vm.GetOpenFilePath = OpenFileDialogAsync;
        vm.SaveLayoutAction = SaveLayout;
        vm.ShowSettingsWindow = async () =>
        {
            var dialog = new SettingsWindow();
            await dialog.ShowDialog(this);
            // 设置窗口可能改动了主题，刷新主窗口菜单里「切换颜色模式」的当前主题文案
            vm.RefreshLocalizedStrings();
            // 预览面板显隐/位置可能在设置窗口中修改，统一重新应用（含菜单勾选状态同步）
            ApplyPreviewLayout();
        };
        vm.ShowPasswordDialog = async (archivePath) =>
        {
            var dialog = new PasswordDialog(Path.GetFileName(archivePath));
            var result = await dialog.ShowDialog<bool>(this);
            if (!result) return null;
            return new PasswordDialogResponse
            {
                Password = dialog.Password,
                RememberInSession = dialog.RememberInSession,
                SavePermanently = dialog.SavePermanently,
                Description = dialog.Description,
                Patterns = dialog.Patterns
            };
        };
        // 工具栏文件名代码页下拉选「其他…」时的代码页输入框
        vm.ShowCodePageInputDialog = () => CodePageInputDialog.ShowAsync(this);
        // 预览面板「输入密码」按钮共享同一密码对话框回调
        vm.Preview.ShowPasswordDialog = vm.ShowPasswordDialog;
        // 密码输入成功后重新触发当前条目的预览
        vm.Preview.PasswordEntered = vm.RePreviewCurrentEntry;
        // 预览面板密码持久化（保存到密码库）
        vm.Preview.PasswordService = vm.PasswordService;
        // 工具栏「密码」按钮（已匹配态）：查看/复制当前压缩包的密码与规则
        vm.ShowMatchedPasswordDialog = async (entry, archiveName) =>
        {
            var dialog = new MatchedPasswordDialog(entry, archiveName);
            await dialog.ShowDialog<bool>(this);
        };
        DataContext = vm;

        // 工具栏代码页菜单先建一份（语言切换/切换编码后由 Click 重新建，见 RebuildNameEncodingMenu）
        RebuildNameEncodingMenu();

        // 菜单子项延迟创建（2026-10-22 启动优化阶段 3）：
        // 文件/编辑/视图/工具四个顶级菜单的子项（共 25 项 + 图标）已抽到
        // Controls/Menus/*MenuItems.axaml → 启动路径不再实例化这批元素。
        // 触发时机：鼠标悬停到该顶级菜单（PointerEnteredItem，提前构建，点击时已就绪）
        // 或键盘展开时（SubmenuOpened 兜底）。
        HookLazyMenu(FileMenuItem, EnsureFileMenuItems);
        HookLazyMenu(EditMenuItem, EnsureEditMenuItems);
        HookLazyMenu(ViewMenuItem, EnsureViewMenuItems);
        HookLazyMenu(ToolsMenuItem, EnsureToolsMenuItems);

        // 测试菜单：Debug 构建显示，Release 自动隐藏。
        // 2026-10-22 启动优化阶段 3：整块菜单（含 28 个子项与图标）已抽到
        // Controls/Menus/TestMenuView.axaml → Release 构建**完全不加载**这批元素
        // （原先即使菜单不可见也会被实例化，白付启动成本）。
        // 用 #if DEBUG 包裹：Release 下连这段代码都不编译（避免 CS0162 不可达告警）。
#if DEBUG
        if (AppConstants.ShowTestMenu)
        {
            var testMenuView = new Controls.Menus.TestMenuView { DataContext = vm };
            MainMenu.Items.Add(testMenuView.CreateMenuItem());
        }
#endif

        // 全局快捷键（唯一事实源）：MenuItem.InputGesture 只负责「在菜单里显示」快捷键文字
        // （Avalonia 官方文档：Setting this property does not cause the input gesture to be handled
        //   by the menu item, it simply displays the gesture text in the menu），
        // 真正生效的按键绑定必须登记在这里；用命令实例直绑（KeyBinding 非 Control，无 DataContext）。
        RegisterKeyBindings(vm);

        // 条目重填后重应用列排序（进入目录/刷新/过滤后保持排序状态）
        vm.EntriesRefreshed += (_, _) =>
        {
            ApplyCurrentSort();
            UpdateSortArrows();
        };

        // 菜单「显示预览面板」切换（IsPreviewVisible 变化）时同步压缩/恢复布局占位
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsPreviewVisible))
            {
                _previewPanelEnabled = vm.IsPreviewVisible;
                ApplyPreviewPosition(_lastAppliedPreviewPosition);
            }
            // 过滤栏延迟创建：首次显示时才实例化（启动优化：默认隐藏时省 ~50 元素 XAML 膨胀）
            else if (e.PropertyName == nameof(MainWindowViewModel.IsFilterBarVisible) && vm.IsFilterBarVisible)
            {
                EnsureFilterBar();
            }
            // 内容区延迟创建：首次加载压缩包时才实例化（启动优化：省 ~153 元素 XAML 膨胀）
            else if (e.PropertyName == nameof(MainWindowViewModel.IsArchiveLoaded) && vm.IsArchiveLoaded)
            {
                EnsureArchiveContent();
            }
        };

        // ── Phase 3: Wire up ViewModel dialog callbacks ──

        vm.ShowExtractSettingsDialog = async (archivePaths) =>
        {
            // 桌面宿主差异：Owner = 主窗口（冲突/错误弹窗的属主）、Presenter = 桌面实现、
            // 不接管进程退出（GUI 里解压结束只关进度窗）。
            var session = new ExtractSession();
            var context = new ExtractRunContext(
                Owner: this,
                Presenter: progressPresenter,
                ExitPolicy: ExitPolicy.None,
                DisplayPaths: archivePaths);

            // 设置窗口注入执行器与宿主差异：窗口自己产出 ConfirmOutcome 载荷，
            // 调用方不再 ShowDialog<bool> 之后回读 ViewModel / FileFilter / 过滤集合。
            var dialog = new ExtractSettingsWindow(session, context, archivePaths);

            // 本会话已缓存的口令随首包回填：确认事务会把它冻结进载荷，
            // 解压会话据此按「只使用提供的密码」策略执行（与原 ExtractFlow 传参路径等价）。
            if (archivePaths.Count > 0)
                dialog.SetKnownPassword(vm.GetSessionPassword(archivePaths[0]));

            // 首包条目已在内存：跳过重复读取，过滤统计与冲突预览立即就绪。
            var allItems = vm.GetAllRawItems();
            if (allItems.Count > 0)
                dialog.SetEntries(allItems);

            var confirmation = await dialog.ShowDialog<ConfirmOutcome<ExtractSettings>>(this);
            if (confirmation is not Confirmed<ExtractSettings> confirmed)
                return;   // 取消 / 点 X：静默收场，不弹任何提示

            var outcome = await dialog.RunConfirmedAsync(confirmed.Value);
            if (outcome.IsCompleted)
            {
                vm.StatusMessage = LocalizationManager.T("Status_ExtractComplete");
                // 勾了「解压完成后删除压缩包」时源包已不在：立刻关包，避免界面停在已删除的包上
                vm.SyncArchiveStateAfterExtract();
            }
            else if (outcome.IsFailed)
            {
                // 错误摘要已由 presenter 写进进度窗（并等用户确认关闭），这里只留结论，不重复弹窗。
                vm.StatusMessage = outcome.Reason;
            }
            else if (outcome.IsNotStarted)
            {
                // NotStarted 没有进度窗可承载原因：必须显式展示，不许静默退出。
                await AppMessageBox.Show(outcome.Reason, LocalizationManager.T("Progress_Title_Extract"),
                    MessageBoxButton.OK, MessageBoxImage.Warning, this);
            }
        };

        // 条目选择模式（v0.5.5 第三步后半）：弹解压设置窗的「选择要解压的文件」页，
        // 窗口自己执行（确认即载荷 + 唯一执行器），返回值只表示「已确认」还是「取消」。
        vm.ShowExtractEntryPicker = async (allEntries, presetEntries, initialPath, currentFolder, password) =>
        {
            var session = new ExtractSession();
            var presenter = new MainWindowProgressPresenter();
            var context = new ExtractRunContext(Owner: this, Presenter: presenter, ExitPolicy.None);
            return await ExtractSettingsWindow.ShowWithPresetSelectionAsync(
                this, session, context,
                new[] { vm.CurrentArchivePath! },
                allEntries,
                presetEntries,
                initialPath ?? string.Empty,
                currentFolder,
                password);
        };

        vm.ShowCompressSettingsDialog = async (sourcePaths) =>
        {
            // 桌面宿主差异：Owner = 主窗口（冲突/错误弹窗的属主）、Presenter = 桌面实现、
            // 不接管进程退出（GUI 里压缩结束只关进度窗）；只有桌面宿主有「打开输出目录」的能力。
            var session = new CompressSession(openOutputFolder: CompressFlow.OpenOutputFolderAsync);
            var context = new CompressRunContext(
                Owner: this,
                Presenter: progressPresenter,
                ExitPolicy: ExitPolicy.None,
                IsCompressFlow: true);

            var dialog = new CompressSettingsWindow(session, context, sourcePaths);
            var confirmation = await dialog.ShowDialog<ConfirmOutcome<CompressSettings>>(this);
            if (confirmation is not Confirmed<CompressSettings> confirmed)
                return;   // 取消 / 点 X：静默收场，不弹任何提示

            var outcome = await dialog.RunConfirmedAsync(confirmed.Value);
            if (outcome.IsCompleted)
            {
                vm.StatusMessage = LocalizationManager.T("Status_Compressed");
            }
            else if (outcome.IsFailed)
            {
                // 错误摘要已由 presenter 写进进度窗（并等用户确认关闭），这里只留结论，不重复弹窗。
                vm.StatusMessage = outcome.Reason;
            }
            else if (outcome.IsNotStarted)
            {
                // NotStarted 没有进度窗可承载原因：必须显式展示，不许静默退出。
                await AppMessageBox.Show(outcome.Reason, LocalizationManager.T("Compress_Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning, this);
            }
        };

        vm.ShowPasswordManager = async () =>
        {
            var dialog = new PasswordManagerWindow();
            await dialog.ShowDialog(this);
        };

        vm.ShowAboutDialog = async () =>
        {
            var dialog = new AboutWindow();
            await dialog.ShowDialog(this);
        };

        vm.ShowDonateDialog = async () =>
        {
            var dialog = new DonationDialog();
            await dialog.ShowDialog(this);
        };

        vm.ShowFavoritesDialog = async () =>
        {
            var dialog = new FavoriteManagerWindow();
            await dialog.ShowDialog(this);
        };

        vm.RunWithProgress = async (title, filePaths, operation) =>
        {
            // 一次性状态仍留在 VM 上（解压/测试/删除/添加四条旧路径尚未接线），在这里读掉后
            // 只以通用呈现字段交给 presenter —— presenter 不再认识 Pending* 侧通道。
            var operationNote = vm.PendingOperationNote;
            vm.PendingOperationNote = null;
            var onCompleted = vm.PendingCompletedCallback;
            vm.PendingCompletedCallback = null;
            var postActions = vm.PendingExtractPostActions;
            vm.PendingExtractPostActions = null;
            var isTestFlow = vm.PendingTestFlow;
            vm.PendingTestFlow = false;

            try
            {
                return await progressPresenter.RunAsync(new ProgressRunRequest
                {
                    Title = title,
                    DisplayPaths = filePaths,
                    OperationNote = operationNote,
                    Operation = operation,
                    OnCompleted = onCompleted,
                    ConfigureWindow = window =>
                    {
                        // Task 6: 测试流标记（自动按下 📌，完成后不自动关闭）
                        window.IsTestFlow = isTestFlow;
                        vm.LastProgressWindow = window;
                        // spec §11.2: 解压后置开关能力位 + 初值（仅解压入口置位 → 压缩/测试流两开关隐藏）
                        if (postActions is { } pa)
                            window.SetExtractPostActions(pa.OpenFolder, pa.DeleteArchive, pa.OpenFolderSeed);
                        // 批处理状态上报：操作闭包内经 BatchStatusReporter 传给引擎 onItemStatus
                        vm.BatchStatusReporter = (index, status) =>
                        {
                            window.SetCurrentBatchItem(index);
                            window.UpdateBatchItemStatus(index, status);
                        };
                        // 批处理输出路径上报：冲突解决后（自动重命名/手输包名）把列表里的旧包名换成实际包名
                        vm.BatchOutputPathReporter = (index, outputPath) => window.UpdateBatchItemName(index, outputPath);
                    },
                });
            }
            catch (Exception ex)
            {
                // 旧契约以 false 表达「未完成」：presenter 已把错误摘要写进窗口并等用户关闭，
                // 这里保留返回值语义，让既有解压/测试/删除调用方继续走「未完成」分支。
                App.DebugLog($"[RunWithProgress] operation failed: {ex}");
                return false;
            }
            finally
            {
                vm.BatchStatusReporter = null;
                vm.BatchOutputPathReporter = null;
            }
        };

        vm.CopyToClipboard = async (text) =>
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    var transfer = new global::Avalonia.Input.DataTransfer();
                    var item = new global::Avalonia.Input.DataTransferItem();
                    item.SetText(text);
                    transfer.Add(item);
                    await topLevel.Clipboard.SetDataAsync(transfer);
                }
            }
            catch
            {
                // Clipboard not available in this environment
            }
        };

        vm.ShowCommentDialog = async (existingComment) =>
        {
            var dialog = new CommentDialog(existingComment);
            var result = await dialog.ShowDialog<bool>(this);
            return result ? dialog.Comment : null;
        };

        // ════════════════════════════════════════════════
        //  压缩/解压冲突对话框回调（从后台线程调用）
        // ════════════════════════════════════════════════

        // 弹窗逻辑统一走 CompressFlow.ShowConflictDialogAsync（与 CLI 右键菜单共用），
        // 本回调仅把 owner 窗口（MainWindow）与 VM 委托接线
        vm.ShowCompressConflictDialog = info => CompressFlow.ShowConflictDialogAsync(this, info);

        // 弹窗逻辑统一走 ExtractFlow.ShowConflictDialogAsync（与 CLI 右键菜单共用），
        // 本回调仅把 owner 窗口（MainWindow）与 VM 委托接线
        vm.ShowExtractFileConflictDialogAsync = info => ExtractFlow.ShowConflictDialogAsync(this, info);

        // 添加文件冲突弹窗：复用解压弹窗循环，仅标题用「添加冲突」
        vm.ShowAddFileConflictDialogAsync = info => ExtractFlow.ShowConflictDialogAsync(this, info, titleKey: "AddConflict_Title");

        // ── Wire up select-all / invert-selection callbacks ──
        vm.SelectAllEntriesAction = () =>
        {
            FileListGrid.SelectedItems.Clear();
            if (FileListGrid.ItemsSource is System.Collections.IList source)
            {
                foreach (var item in source)
                {
                    if (item is ArchiveItemModel)
                        FileListGrid.SelectedItems.Add(item);
                }
            }
        };
        vm.InvertSelectionAction = () =>
        {
            var selected = new HashSet<object>();
            foreach (var item in FileListGrid.SelectedItems)
                selected.Add(item);
            var allItems = new List<object>();
            if (FileListGrid.ItemsSource is System.Collections.IList source)
            {
                foreach (var item in source)
                    allItems.Add(item);
            }
            FileListGrid.SelectedItems.Clear();
            foreach (var item in allItems)
            {
                if (!selected.Contains(item))
                    FileListGrid.SelectedItems.Add(item);
            }
        };

        vm.GetOpenFilePaths = async () =>
        {
            // 以当前已打开压缩包的所在目录作为「场景相关路径」初值（无则 null → 走优先级链其它来源）
            var contextPath = vm.CurrentArchivePath is { } archive
                ? Path.GetDirectoryName(archive)
                : null;
            return await CustomFilePickerDialog.ShowOpenItemsAsync(this, initialPath: contextPath);
        };

        // ── Wire up metadata panel settings → open Settings window ──
        vm.Preview.OpenSettingsToMetadataTab = async () =>
        {
            if (vm.ShowSettingsWindow != null)
                await vm.ShowSettingsWindow();
        };

        // 文件列表拖出（拖拽解压）接线 → 随内容区创建时进行（见 SetupFileListDragDrop）

        // Prevent reacting to our own drag-drop
        this.AddHandler(DragDrop.DropEvent, (s, e) =>
        {
            if (_isOwnDrag)
                e.Handled = true;
        });

        // Persist window position/size/state + column widths on close
        Closing += (_, _) => WindowStateManager.Save(this, CaptureColumnStates(), _lastSortMemberPath,
            _lastSortMemberPath == null ? 0 : (_lastSortDescending ? 2 : 1));

        Services.StartupTimer.Mark("Win.Ctor.Exit");
        // 首帧打点 + flush 启动 trace（一次性）
        Opened += OnStartupOpened;

        // 预览面板延迟创建：首帧后 0.5s 定时预取（把控件树构造挪出启动与打开压缩包关键路径）；
        // 0.5s 内即发生预览请求时由 vm.PreviewPanelNeeded 兜底同步创建。
        Opened += (_, _) => DispatcherTimer.RunOnce(
            () => Dispatcher.UIThread.Post(PrefetchPreviewPanel, DispatcherPriority.Background),
            TimeSpan.FromMilliseconds(500));
        vm.PreviewPanelNeeded += EnsurePreviewPanel;
    }

    /// <summary>
    /// 首帧后 0.5s 预取预览面板（把控件树构造挪出启动与打开压缩包关键路径）。
    ///
    /// 2026-10-21（阶段 2）：预览面板宿主位于**延迟创建的内容区**内 → 未打开压缩包时
    /// 预取会把 ~153 元素的内容区一起拉进启动路径（启动优化倒退）→ 此时跳过预取，
    /// 改由 <see cref="EnsureArchiveContent"/> 在内容区创建后立即创建预览面板。
    /// </summary>
    private void PrefetchPreviewPanel()
    {
        if (_archiveContent == null) return;   // 内容区未创建（未打开压缩包）→ 不预取
        EnsurePreviewPanel();
    }

    /// <summary>
    /// 延迟创建预览面板控件树（启动时 XAML 仅保留占位 host，855 行控件树不再计入启动关键路径）。
    /// 幂等；构造异常时清理半成品 Content 且 flag 不置位，允许兜底路径重试。
    /// </summary>
    private void EnsurePreviewPanel()
    {
        if (_previewPanelCreated) return;
        _previewPanelCreated = true; // 先置位防重入（构造中再触发直接返回）
        try
        {
            if (DataContext is not MainWindowViewModel vm) { _previewPanelCreated = false; return; }
            var panel = new PreviewPanel { DataContext = vm.Preview };
            PreviewPanelHost.Content = panel;
            App.DebugLog("EnsurePreviewPanel: preview panel created (lazy)");
        }
        catch (Exception ex)
        {
            _previewPanelCreated = false;
            PreviewPanelHost.Content = null; // 清理半成品
            App.DebugLog($"EnsurePreviewPanel failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 窗口首次 Opened：打首帧近似点并 flush 启动 trace（一次性）。
    /// 与 splash 计划衔接：splash 也在 Opened 关闭，语义一致。
    /// </summary>
    private void OnStartupOpened(object? sender, EventArgs e)
    {
        Opened -= OnStartupOpened;
        Services.StartupTimer.Mark("Win.Visible");
        // Avalonia 无 WPF ContentRendered：用 Render 优先级回调近似首帧上屏
        Dispatcher.UIThread.Post(() =>
        {
            Services.StartupTimer.Mark("Win.FirstFrame");
            Services.StartupTimer.Flush("first-frame");
        }, DispatcherPriority.Render);
    }

    /// <summary>
    /// 将 window.json 中保存的列状态应用到 FileListGrid（按 ColumnId=SortMemberPath 或 Tag 匹配）。
    /// 名称列不允许隐藏；图标列（Tag="Icon"）参与持久化 —— 用户可拖拽调整其位置。
    /// 旧版 JSON（图标列未参与持久化）时：图标列固定最左，其余列按相对顺序顺延。
    /// </summary>
    private void ApplyColumnStates(List<WindowStateManager.ColumnStateDto>? states)
    {
        if (states == null || states.Count == 0)
            return;

        try
        {
            var columnDict = new Dictionary<string, DataGridColumn>();
            foreach (var col in FileListGrid.Columns)
            {
                // 复选框列不参与列状态持久化：它由 ApplyCheckboxColumnVisibility 独占管理，
                // 一旦被这里的 DisplayIndex/IsVisible 回放碰到就会被顶到末尾并隐藏（实测）。
                if (col.Tag is string cbTag && cbTag == "Checkbox")
                    continue;

                var id = GetColumnId(col);
                if (id != null && !columnDict.ContainsKey(id))
                    columnDict[id] = col;
            }

            // 新格式 JSON（含图标列状态）：按保存的 DisplayIndex 恢复全部列位置
            if (states.Any(s => s.ColumnId == IconColumnTag))
            {
                foreach (var state in states.Where(s => !string.IsNullOrEmpty(s.ColumnId))
                                            .OrderBy(s => s.DisplayIndex))
                {
                    if (state.ColumnId == null || !columnDict.TryGetValue(state.ColumnId, out var col))
                        continue;

                    if (state.Width > 0)
                        col.Width = new DataGridLength(state.Width);

                    // 名称列与图标列不可隐藏
                    if (state.ColumnId != "Name" && state.ColumnId != IconColumnTag)
                        col.IsVisible = state.Visible;

                    col.DisplayIndex = state.DisplayIndex;
                }
            }
            else
            {
                // 旧格式 JSON（图标列此前不参与持久化，可能已被挤到中间）：
                // 图标列强制回最左（DisplayIndex 0），其余列按保存的相对顺序从 1 开始顺延。
                if (columnDict.TryGetValue(IconColumnTag, out var iconCol))
                    iconCol.DisplayIndex = 0;

                int nextIndex = 1;
                foreach (var state in states.Where(s => !string.IsNullOrEmpty(s.ColumnId))
                                            .OrderBy(s => s.DisplayIndex))
                {
                    if (state.ColumnId == null || !columnDict.TryGetValue(state.ColumnId, out var col))
                        continue;

                    if (state.Width > 0)
                        col.Width = new DataGridLength(state.Width);

                    // 名称列不可隐藏
                    if (state.ColumnId != "Name")
                        col.IsVisible = state.Visible;

                    col.DisplayIndex = nextIndex++;
                }
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"ApplyColumnStates: failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 根据 AppSettings.PreviewPosition 重新布局预览面板位置（单 Grid 附加属性切换，不搬移控件）。
    /// 1=底部, 2=目录树下方, 3=文件列表下方, 4=文件列表右侧（默认）。
    /// 每次应用前完整重置所有相关属性再按目标位置设置，避免状态残留。
    /// </summary>
    private void ApplyPreviewPosition(int position)
    {
        if (ArchiveContentGrid == null || PreviewPanelHost == null)
            return;

        // 切换位置前保存旧位置的当前尺寸（Star 保留星号权重维持比例伸缩、Pixel 保持固定，
        // 见 SaveCurrentPreviewSize）。
        // 位置未变化（设置窗口保存/菜单切换触发同位置重应用）时同样先记录当前尺寸：
        // 分隔条拖拽产生的尺寸只存在于 Grid 定义中，若不记录，下面的完整重置
        // 会把它丢弃，回退到默认（3* 星号 / 1* / 200px）或字典里过期的旧值 → 面板缩到最小。
        if (position != _lastAppliedPreviewPosition)
            SaveCurrentPreviewSize(_lastAppliedPreviewPosition);
        else
            SaveCurrentPreviewSize(position);

        // ── 完整重置：清掉上一种布局的所有痕迹 ──
        ArchiveContentGrid.RowDefinitions[1].Height = new GridLength(0);
        ArchiveContentGrid.RowDefinitions[2].Height = new GridLength(0);
        ArchiveContentGrid.ColumnDefinitions[3].Width = new GridLength(0);
        ArchiveContentGrid.ColumnDefinitions[4].Width = new GridLength(0);
        PreviewRowSplitter.IsVisible = false;
        PreviewColSplitter.IsVisible = false;
        Grid.SetRowSpan(FolderTreeBorder, 1);
        Grid.SetRowSpan(TreeFileSplitter, 1);
        Grid.SetRowSpan(FileListPanel, 1);
        Grid.SetRowSpan(PreviewPanelHost, 1);
        Grid.SetColumnSpan(PreviewPanelHost, 1);

        switch (position)
        {
            case 1: // 底部：预览横跨全部 5 列
                ArchiveContentGrid.RowDefinitions[1].Height = new GridLength(4);
                ArchiveContentGrid.RowDefinitions[2].Height = _previewSizeByPosition.TryGetValue(1, out var h1)
                    ? h1
                    : new GridLength(1, GridUnitType.Star);
                PreviewRowSplitter.IsVisible = true;
                Grid.SetColumn(PreviewRowSplitter, 0);
                Grid.SetColumnSpan(PreviewRowSplitter, 5);
                Grid.SetRow(PreviewPanelHost, 2);
                Grid.SetColumn(PreviewPanelHost, 0);
                Grid.SetColumnSpan(PreviewPanelHost, 5);
                break;

            case 2: // 目录树下方：文件列表跨 3 行占满底部
                ArchiveContentGrid.RowDefinitions[1].Height = new GridLength(4);
                ArchiveContentGrid.RowDefinitions[2].Height = _previewSizeByPosition.TryGetValue(2, out var h2)
                    ? h2
                    : new GridLength(200);
                Grid.SetRowSpan(FileListPanel, 3);
                Grid.SetRowSpan(TreeFileSplitter, 3);
                PreviewRowSplitter.IsVisible = true;
                Grid.SetColumn(PreviewRowSplitter, 0);
                Grid.SetColumnSpan(PreviewRowSplitter, 1);
                Grid.SetRow(PreviewPanelHost, 2);
                Grid.SetColumn(PreviewPanelHost, 0);
                Grid.SetColumnSpan(PreviewPanelHost, 1);
                break;

            case 3: // 文件列表下方：目录树跨 3 行占满底部
                ArchiveContentGrid.RowDefinitions[1].Height = new GridLength(4);
                ArchiveContentGrid.RowDefinitions[2].Height = _previewSizeByPosition.TryGetValue(3, out var h3)
                    ? h3
                    : new GridLength(200);
                Grid.SetRowSpan(FolderTreeBorder, 3);
                Grid.SetRowSpan(TreeFileSplitter, 3);
                PreviewRowSplitter.IsVisible = true;
                Grid.SetColumn(PreviewRowSplitter, 2);
                Grid.SetColumnSpan(PreviewRowSplitter, 3);
                Grid.SetRow(PreviewPanelHost, 2);
                Grid.SetColumn(PreviewPanelHost, 2);
                Grid.SetColumnSpan(PreviewPanelHost, 3);
                break;

            default: // 4: 文件列表右侧（默认布局）
                ArchiveContentGrid.ColumnDefinitions[3].Width = new GridLength(5);
                ArchiveContentGrid.ColumnDefinitions[4].Width = _previewSizeByPosition.TryGetValue(4, out var w4)
                    ? w4
                    : new GridLength(3, GridUnitType.Star);
                PreviewColSplitter.IsVisible = true;
                Grid.SetRow(PreviewPanelHost, 0);
                Grid.SetColumn(PreviewPanelHost, 4);
                break;
        }

        // 面板隐藏时：压缩预览占位行列 + 隐藏 splitter + 复位 RowSpan，让树/列表撑满
        if (!_previewPanelEnabled)
        {
            // 隐藏前记录当前尺寸（保留单元类型），保证重新打开时保留用户拖过的宽度/高度
            SaveCurrentPreviewSize(position);
            PreviewRowSplitter.IsVisible = false;
            PreviewColSplitter.IsVisible = false;
            ArchiveContentGrid.RowDefinitions[1].Height = new GridLength(0);
            ArchiveContentGrid.RowDefinitions[2].Height = new GridLength(0);
            ArchiveContentGrid.ColumnDefinitions[3].Width = new GridLength(0);
            ArchiveContentGrid.ColumnDefinitions[4].Width = new GridLength(0);
            Grid.SetRowSpan(FolderTreeBorder, 1);
            Grid.SetRowSpan(TreeFileSplitter, 1);
            Grid.SetRowSpan(FileListPanel, 1);
        }

        _lastAppliedPreviewPosition = position;
    }

    /// <summary>
    /// 从磁盘重读设置并统一应用预览面板的显隐与位置（启动时与设置窗口保存后调用）。
    /// 同时把显隐同步回 MainWindowViewModel.IsPreviewVisible，保证菜单勾选状态一致。
    /// </summary>
    private void ApplyPreviewLayout()
    {
        var settings = AppSettings.Load();
        _previewPanelEnabled = settings.ShowPreviewPanel;
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsPreviewVisible = settings.ShowPreviewPanel;
            // 设置保存后同步信息面板显隐与方向（此前仅启动时初始化一次，设置窗口修改后不生效）
            vm.Preview.ShowInfoPanel = settings.ShowPreviewInfoPanel;
            vm.Preview.InfoPanelOrientation = settings.InfoPanelOrientation;
        }
        ApplyPreviewPosition(settings.PreviewPosition);
    }

    /// <summary>
    /// 保存指定预览位置的当前尺寸到记忆字典（保留 GridLength 单元类型）：
    /// Star 布局（未保存过布局时拖拽分隔条，Avalonia Split 行为保持 Star）记录星号权重，
    /// 恢复后仍随窗口比例伸缩；Pixel 布局（树/文件列宽恢复为像素后拖分隔条，经 Resize 分支
    /// 转成的像素值）恢复后固定。若统一降级成像素值记录，恢复时会把比例伸缩的窗格固定死。
    /// 面板隐藏压缩产生的 0 / Auto 不记录。
    /// </summary>
    private void SaveCurrentPreviewSize(int position)
    {
        if (ArchiveContentGrid == null)
            return;

        if (position == 4)
            RecordPreviewSize(4, ArchiveContentGrid.ColumnDefinitions[4].Width);
        else if (position is 1 or 2 or 3)
            RecordPreviewSize(position, ArchiveContentGrid.RowDefinitions[2].Height);
    }

    /// <summary>记录预览尺寸：仅 Pixel/Star 且值有效时记录；Auto 与 0（面板隐藏压缩态）跳过。</summary>
    private void RecordPreviewSize(int position, GridLength length)
    {
        if (length.GridUnitType is GridUnitType.Pixel or GridUnitType.Star && length.Value > 0)
            _previewSizeByPosition[position] = length;
    }

    /// <summary>
    /// 启动时加载上次手动保存的布局快照（layout.json）：
    /// 回填预览面板各位置记忆尺寸 + 应用目录树/文件列表列宽（像素）。
    /// 必须在 ApplyPreviewLayout() 之前调用，保证 ApplyPreviewPosition 能读到已回填的预览尺寸。
    /// </summary>
    private void ApplySavedLayout()
    {
        var snapshot = LayoutStateManager.Load();
        if (snapshot == null)
            return;

        try
        {
            if (snapshot.PreviewSizeByPosition is { Count: > 0 })
            {
                foreach (var kvp in snapshot.PreviewSizeByPosition)
                {
                    if (kvp.Value.GridUnitType is GridUnitType.Pixel or GridUnitType.Star && kvp.Value.Value > 0)
                        _previewSizeByPosition[kvp.Key] = kvp.Value;
                }
            }

            if (snapshot.TreeColumnWidth is > 0)
                ArchiveContentGrid.ColumnDefinitions[0].Width = new GridLength(snapshot.TreeColumnWidth.Value, GridUnitType.Pixel);
            if (snapshot.FileListColumnWidth is > 0)
                ArchiveContentGrid.ColumnDefinitions[2].Width = new GridLength(snapshot.FileListColumnWidth.Value, GridUnitType.Pixel);
        }
        catch (Exception ex)
        {
            App.DebugLog($"ApplySavedLayout: failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 手动保存内容区布局快照（菜单「保存布局」触发）：
    /// 目录树列宽（col 0）+ 文件列表列宽（col 2）+ 预览面板各位置记忆尺寸。
    /// 树/文件列 Star 布局用 ActualWidth 捕获实际像素值；预览尺寸保留单元类型
    /// （Star 维持比例伸缩、Pixel 维持固定），保证恢复后与保存时一致。
    /// </summary>
    private void SaveLayout()
    {
        try
        {
            // 先记录当前活动预览位置的实时尺寸（分隔条拖拽值只存在于 Grid 定义中）
            SaveCurrentPreviewSize(_lastAppliedPreviewPosition);

            var snapshot = new LayoutStateManager.LayoutSnapshot
            {
                TreeColumnWidth = CaptureColumnActualWidth(ArchiveContentGrid, 0),
                FileListColumnWidth = CaptureColumnActualWidth(ArchiveContentGrid, 2),
                PreviewSizeByPosition = new Dictionary<int, GridLength>(_previewSizeByPosition)
            };

            LayoutStateManager.Save(snapshot);
            if (DataContext is MainWindowViewModel vm)
                vm.StatusMessage = LocalizationManager.T("Status_LayoutSaved");
        }
        catch (Exception ex)
        {
            App.DebugLog($"SaveLayout: failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 捕获指定列的实际像素宽度：Pixel 布局直接用值，Star 布局用 ActualWidth。
    /// 返回 >0 的有效宽度；Grid 未布局（ActualWidth=0）时返回 null。
    /// </summary>
    private static double? CaptureColumnActualWidth(Grid grid, int index)
    {
        var w = grid.ColumnDefinitions[index].Width;
        var pixel = w.GridUnitType == GridUnitType.Pixel ? w.Value : grid.ColumnDefinitions[index].ActualWidth;
        return pixel > 0 ? pixel : null;
    }
    /// <summary>
    /// 获取列的持久化标识：优先 SortMemberPath，其次 Tag（图标列 Tag="Icon"）。
    /// </summary>
    private static string? GetColumnId(DataGridColumn col)
    {
        if (!string.IsNullOrEmpty(col.SortMemberPath))
            return col.SortMemberPath;
        return col.Tag as string;
    }

    /// <summary>
    /// 捕获 FileListGrid 各列的宽度/可见性/顺序快照（标识 = SortMemberPath 或 Tag），
    /// 供 WindowStateManager.Save 持久化到 window.json。图标列（Tag="Icon"）一并保存，
    /// 保证用户拖拽调整后的列位置（含图标列）能被正确恢复。
    /// </summary>
    private List<WindowStateManager.ColumnStateDto>? CaptureColumnStates()
    {
        try
        {
            // 内容区尚未创建（本次未打开过压缩包）→ 列状态未被用户改动，
            // 原样回写启动时读到的快照，避免用空列表覆盖用户已保存的列宽/显隐
            if (_archiveContent == null)
                return _pendingColumnStates;

            var states = new List<WindowStateManager.ColumnStateDto>();
            foreach (var col in FileListGrid.Columns)
            {
                var id = GetColumnId(col);
                if (id == null)
                    continue;

                states.Add(new WindowStateManager.ColumnStateDto
                {
                    ColumnId = id,
                    Width = col.Width.Value,
                    Visible = col.IsVisible,
                    DisplayIndex = col.DisplayIndex
                });
            }
            return states;
        }
        catch (Exception ex)
        {
            App.DebugLog($"CaptureColumnStates: failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 命中测试：返回按下位置所在行的数据项（ArchiveItemModel）；未命中任何行时返回 null。
    /// 镜像 WPF MainWindow.DragDrop.cs 的 InputHitTest 语义：判断按下的行是否已在当前选区中。
    /// </summary>
    private static ArchiveItemModel? HitTestPressedRowItem(DataGrid grid, Point position)
    {
        var hit = grid.InputHitTest(position);
        for (var v = hit as Visual; v != null; v = v.GetVisualParent())
        {
            if (v is DataGridRow row && row.DataContext is ArchiveItemModel model)
                return model;
        }
        return null;
    }

    /// <summary>
    /// 命中测试：判断按下位置是否落在「复选框列」的复选框上。
    /// 只有该列的单元格模板里有 <see cref="CheckBox"/>，所以自命中元素上溯「先遇到 CheckBox 还是
    /// 先遇到 DataGridRow」即可区分列（Avalonia 的 <c>DataGridCell</c> 不公开所属列，不能用列标记判断）。
    /// 命中时返回该行的数据项，供复选框模式的 Shift 连勾使用；命中别的列返回 false。
    /// </summary>
    private static bool TryHitCheckbox(object? source, out ArchiveItemModel? item)
    {
        item = null;
        for (var v = source as Visual; v != null; v = v.GetVisualParent())
        {
            if (v is DataGridRow row)
            {
                // 先碰到行 = 命中的是别的列（名称 / 大小 / 日期……），不抢它的点击
                item = row.DataContext as ArchiveItemModel;
                return false;
            }

            if (v is CheckBox { DataContext: ArchiveItemModel model })
            {
                item = model;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 详情列表双击：行为统一在 <see cref="HandleItemDoubleTapAsync"/>（平铺/内容模式共用同一入口）。
    /// </summary>
    private async void FileListGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not ArchiveItemModel item) return;

        await HandleItemDoubleTapAsync(item);
    }

    /// <summary>
    /// 详情列表键盘：语义统一在 <see cref="HandleFileListKeyDown"/>
    /// （Enter = 双击行为、Back = 上溯、Delete = 删除；平铺/内容模式共用同一实现）。
    /// </summary>
    private void FileListGrid_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (sender is not DataGrid) return;

        HandleFileListKeyDown(e);
    }

    private void FileListGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        if (sender is not DataGrid grid) return;

        var sortMemberPath = e.Column.SortMemberPath;

        // 阻止默认排序（改用自定义手动排序）
        e.Handled = true;

        // 图标列（无 SortMemberPath）不参与排序
        if (string.IsNullOrEmpty(sortMemberPath))
            return;

        // 三态循环：新列 → 升序；同列升序 → 降序；同列降序 → 未排序（恢复原始顺序）
        if (_lastSortMemberPath != sortMemberPath)
        {
            _lastSortMemberPath = sortMemberPath;
            _lastSortDescending = false;
        }
        else if (!_lastSortDescending)
        {
            _lastSortDescending = true;
        }
        else
        {
            _lastSortMemberPath = null;
            _lastSortDescending = false;
        }

        ApplyCurrentSort();
        UpdateSortArrows();
    }

    /// <summary>
    /// 假列头排序（Tile/Content 各 5 个单元挂此入口，经 ArchiveContentView.CardHeaderSortRequested 转发）。
    /// 与 Details 列头点击完全同一三态循环（新列升序 → 同列降序 → 清除排序恢复原序）和同一窗口级
    /// 排序状态 ⇒ 切回 Details 列表所见一致；ApplyCurrentSort 重排 CurrentEntries ⇒ 卡片顺序自动跟随。
    /// </summary>
    private void OnCardHeaderSortTapped(object? sender, string sortPath)
    {
        if (_lastSortMemberPath != sortPath)
        {
            _lastSortMemberPath = sortPath;
            _lastSortDescending = false;
        }
        else if (!_lastSortDescending)
        {
            _lastSortDescending = true;
        }
        else
        {
            _lastSortMemberPath = null;
            _lastSortDescending = false;
        }

        ApplyCurrentSort();
        UpdateSortArrows();
    }

    /// <summary>
    /// 注册全局快捷键（**菜单快捷键的唯一事实源**）。
    ///
    /// 背景：Avalonia 的 <c>MenuItem.InputGesture</c> **只显示**快捷键文字、不处理按键
    /// （官方文档：Setting this property does not cause the input gesture to be handled by the
    /// menu item, it simply displays the gesture text in the menu）。
    /// 因此真正生效的按键必须登记在 <see cref="Avalonia.Input.IInputElement.KeyBindings"/>
    /// （Avalonia 12 的 API 名是 <c>KeyBindings</c>，11 版的 <c>InputBindings</c> 已移除）；
    /// 此处用命令实例直绑（KeyBinding 不是 Control，拿不到 DataContext）。
    ///
    /// 约定：新增菜单快捷键时 ① 在此登记（生效）② 在对应 MenuItem 上写同样的
    /// <c>InputGesture</c>（菜单里显示）③ 在 AGENTS.md「快捷键」小节同步。
    /// </summary>
    private void RegisterKeyBindings(MainWindowViewModel vm)
    {
        void Add(Key key, KeyModifiers modifiers, System.Windows.Input.ICommand command)
        {
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(key, modifiers),
                Command = command,
            });
        }

        // 文件
        Add(Key.O, KeyModifiers.Control, vm.OpenArchiveCommand);
        Add(Key.N, KeyModifiers.Control, vm.NewArchiveCommand);
        Add(Key.W, KeyModifiers.Control, vm.CloseArchiveCommand);
        Add(Key.F5, KeyModifiers.None, vm.RefreshArchiveCommand);
        Add(Key.L, KeyModifiers.Control | KeyModifiers.Shift, vm.OpenArchiveLocationCommand);

        // 编辑
        Add(Key.E, KeyModifiers.Control, vm.ExtractArchiveCommand);
        Add(Key.E, KeyModifiers.Control | KeyModifiers.Shift, vm.ExtractArchiveHereCommand);
        Add(Key.T, KeyModifiers.Control | KeyModifiers.Shift, vm.ExtractArchiveToNameCommand);
        Add(Key.T, KeyModifiers.Control, vm.TestArchiveCommand);
        Add(Key.A, KeyModifiers.Control | KeyModifiers.Shift, vm.AddFilesCommand);

        // 视图
        Add(Key.P, KeyModifiers.Control, vm.TogglePreviewCommand);
        Add(Key.V, KeyModifiers.Control | KeyModifiers.Shift, vm.CycleViewModeCommand);
        Add(Key.I, KeyModifiers.Control | KeyModifiers.Shift, vm.ToggleInfoPanelVisibilityCommand);
        Add(Key.C, KeyModifiers.Control | KeyModifiers.Shift, vm.CopyFilePathCommand);

        // 帮助
        Add(Key.OemComma, KeyModifiers.Control, vm.OpenSettingsCommand);
    }

    /// <summary>
    /// 延迟创建压缩包内容区（启动优化，2026-10-21）：目录树 / 文件列表 DataGrid / 地址栏 / 预览面板 /
    /// 拖拽添加覆层共 ~153 个元素，压缩包未打开时不可见 → 启动路径无需实例化。
    /// 首次加载压缩包（<c>IsArchiveLoaded</c> 变 true）时创建，并补上启动期推迟的
    /// 列状态 / 排序箭头 / 布局快照 / 预览位置恢复。
    /// </summary>
    private void EnsureArchiveContent()
    {
        if (_archiveContentCreated) return;
        _archiveContentCreated = true;

        var view = new Controls.ArchiveContentView { DataContext = DataContext };
        _archiveContent = view;
        ArchiveContentHost.Content = view;

        // 内容区内的 XAML 事件处理器在 MainWindow 侧订阅（处理器实现仍在 MainWindow）
        // 注：列标题右键菜单（ColumnHeaderContextMenu_Opening）随内容区迁入 ArchiveContentView
        view.FileListGridControl.SelectionChanged += FileListGrid_SelectionChanged;
        view.FileListGridControl.DoubleTapped += FileListGrid_DoubleTapped;
        view.FileListGridControl.KeyDown += FileListGrid_KeyDown;
        view.FileListGridControl.Sorting += FileListGrid_Sorting;

        // 假列头排序：Tile/Content 列头单元左键 → 同一窗口级排序状态（三态循环与 Details 一致）
        view.CardHeaderSortRequested += OnCardHeaderSortTapped;

        // 首次创建即对齐假列头箭头（window.json 恢复的排序状态可能非空）
        view.UpdateCardSortArrows(_lastSortMemberPath, _lastSortDescending);

        // 复选框模式：勾选集合变化 → 回写 DataGrid 选区（Ctrl/Shift 点选的反向由 SelectionChanged 处理）
        if (DataContext is MainWindowViewModel checkboxVm)
        {
            // DataGridColumn 不在视觉树上、不继承 DataContext ⇒ 列上的 IsVisible 绑定不生效，
            // 只能在这里按 VM 状态命令式切显隐（首次 + 之后每次切换）。
            checkboxVm.PropertyChanged += OnCheckboxVmPropertyChanged;
        }

        // 文件列表「拖出」（拖拽解压）接线
        SetupFileListDragDrop();

        // 平铺/内容布局（缩略图）容器的事件接线：选择层 / 双击 / 键盘 / 右键 / 拖出
        // （实现在 MainWindow.Thumbnails.cs；容器为 x:Name，随内容区一起创建）
        SetupThumbnailLayoutEvents();

        // 启动期推迟的恢复动作（此时控件已存在）
        ApplyColumnStates(_pendingColumnStates);
        UpdateSortArrows();
        ApplySavedLayout();   // 必须在 ApplyPreviewLayout() 之前（预览尺寸回填）

        // 复选框列必须在列状态回放之后再应用显隐（回放会重排 DisplayIndex，先应用会被顶走）
        ApplyCheckboxColumnVisibility();
        ApplyPreviewLayout();

        // 预览面板此刻创建（内容区已就位）——替代原「首帧后 0.5s 预取」
        // （预取时机已随内容区延迟创建而后移，见 PrefetchPreviewPanel）
        EnsurePreviewPanel();
    }

    // ── 内容区具名控件访问（延迟创建；内容区未创建时按需创建）──

    /// <summary>内容区根 Grid（预览面板布局数学）。</summary>
    private Grid ArchiveContentGrid => EnsureArchiveContentCore().ContentGrid;

    /// <summary>文件列表 DataGrid。</summary>
    private DataGrid FileListGrid => EnsureArchiveContentCore().FileListGridControl;

    /// <summary>目录树面板。</summary>
    private Border FolderTreeBorder => EnsureArchiveContentCore().FolderTreeBorderControl;

    /// <summary>目录树 / 文件列表之间的竖向分隔条。</summary>
    private GridSplitter TreeFileSplitter => EnsureArchiveContentCore().TreeFileSplitterControl;

    /// <summary>文件列表面板。</summary>
    private Grid FileListPanel => EnsureArchiveContentCore().FileListPanelControl;

    /// <summary>预览面板宿主。</summary>
    private ContentControl PreviewPanelHost => EnsureArchiveContentCore().PreviewPanelHostControl;

    /// <summary>预览面板横向分隔条。</summary>
    private GridSplitter PreviewRowSplitter => EnsureArchiveContentCore().PreviewRowSplitterControl;

    /// <summary>预览面板纵向分隔条。</summary>
    private GridSplitter PreviewColSplitter => EnsureArchiveContentCore().PreviewColSplitterControl;

    /// <summary>拖拽添加覆层。</summary>
    private Border DragAddOverlay => EnsureArchiveContentCore().DragAddOverlayControl;

    /// <summary>拖拽添加覆层背景。</summary>
    private Border DragAddOverlayBg => EnsureArchiveContentCore().DragAddOverlayBgControl;

    /// <summary>拖拽添加覆层图标。</summary>
    private TextBlock DragAddOverlayIcon => EnsureArchiveContentCore().DragAddOverlayIconControl;

    /// <summary>拖拽添加覆层文字。</summary>
    private TextBlock DragAddOverlayText => EnsureArchiveContentCore().DragAddOverlayTextControl;

    /// <summary>名称列排序箭头。</summary>
    private TextBlock NameHeaderArrow => EnsureArchiveContentCore().NameHeaderArrowControl;

    /// <summary>大小列排序箭头。</summary>
    private TextBlock SizeHeaderArrow => EnsureArchiveContentCore().SizeHeaderArrowControl;

    /// <summary>压缩后大小列排序箭头。</summary>
    private TextBlock CompressedSizeHeaderArrow => EnsureArchiveContentCore().CompressedSizeHeaderArrowControl;

    /// <summary>压缩率列排序箭头。</summary>
    private TextBlock RatioHeaderArrow => EnsureArchiveContentCore().RatioHeaderArrowControl;

    /// <summary>修改时间列排序箭头。</summary>
    private TextBlock LastModifiedHeaderArrow => EnsureArchiveContentCore().LastModifiedArrowControl;

    /// <summary>
    /// 确保内容区已创建并返回（供上面的具名控件访问器使用）。
    /// 已创建 → 直接返回；未创建 → 创建（并补齐推迟的恢复动作）。
    /// </summary>
    private Controls.ArchiveContentView EnsureArchiveContentCore()
    {
        if (_archiveContent == null)
            EnsureArchiveContent();
        return _archiveContent!;
    }

    /// <summary>
    /// 菜单子项延迟创建（2026-10-22 启动优化阶段 3）。
    ///
    /// 顶级菜单项常驻（用户一眼能看到「文件/编辑/视图/工具」），其**子项**改为首次交互时才创建：
    /// ① <c>PointerEnteredItem</c>：鼠标悬停到该顶级菜单时预构建（点击展开时已就绪，无空白帧）；
    /// ② <c>SubmenuOpened</c>：键盘展开时兜底构建。
    ///
    /// 子项来自独立的 <c>StackPanel</c> 视图（<c>Controls/Menus/*MenuItems.axaml</c>），
    /// 创建后把其 <c>Children</c> 搬进顶级 <see cref="MenuItem"/> 的 <c>Items</c>。
    /// </summary>
    private void HookLazyMenu(MenuItem menuItem, Action ensure)
    {
        if (menuItem == null) return;
        menuItem.PointerEnteredItem += (_, _) => ensure();
        menuItem.SubmenuOpened += (_, _) => ensure();
    }

    /// <summary>把子项视图的子元素搬进目标菜单项（幂等：只搬一次）。</summary>
    private static void AttachMenuItems(MenuItem menuItem, Panel view, object? dataContext)
    {
        foreach (var child in view.Children.ToList())
        {
            view.Children.Remove(child);
            // 显式绑定 DataContext：脱离原父级后继承链会断（菜单项从菜单树继承 VM）
            child.DataContext = dataContext;
            menuItem.Items.Add(child);
        }
    }

    /// <summary>文件菜单子项：首次交互时创建并挂入。</summary>
    internal void EnsureFileMenuItems()
    {
        if (_fileMenuItemsCreated) return;
        _fileMenuItemsCreated = true;
        var view = new Controls.Menus.FileMenuItems { DataContext = DataContext };
        AttachMenuItems(FileMenuItem, view, DataContext);
    }

    /// <summary>编辑菜单子项：首次交互时创建并挂入。</summary>
    internal void EnsureEditMenuItems()
    {
        if (_editMenuItemsCreated) return;
        _editMenuItemsCreated = true;
        var view = new Controls.Menus.EditMenuItems { DataContext = DataContext };
        AttachMenuItems(EditMenuItem, view, DataContext);
    }

    /// <summary>视图菜单子项：首次交互时创建并挂入。</summary>
    internal void EnsureViewMenuItems()
    {
        if (_viewMenuItemsCreated) return;
        _viewMenuItemsCreated = true;
        var view = new Controls.Menus.ViewMenuItems { DataContext = DataContext };
        AttachMenuItems(ViewMenuItem, view, DataContext);
    }

    /// <summary>工具菜单子项：首次交互时创建并挂入。</summary>
    internal void EnsureToolsMenuItems()
    {
        if (_toolsMenuItemsCreated) return;
        _toolsMenuItemsCreated = true;
        var view = new Controls.Menus.ToolsMenuItems { DataContext = DataContext };
        AttachMenuItems(ToolsMenuItem, view, DataContext);
    }

    /// <summary>
    /// 过滤栏延迟创建：过滤栏默认隐藏，其 ~50 个元素的 XAML 膨胀
    /// 在启动路径上毫无意义 → 改为首次显示（<c>IsFilterBarVisible = true</c>）时才实例化
    /// <see cref="Controls.MainFilterBar"/> 并放入宿主 <c>FilterBarHost</c>。
    /// </summary>
    private void EnsureFilterBar()
    {
        if (_filterBarCreated) return;
        _filterBarCreated = true;
        FilterBarHost.Content = new Controls.MainFilterBar { DataContext = DataContext };
    }

    /// <summary>
    /// 按当前排序状态重排 CurrentEntries：
    /// - .. 导航行永远置顶（防御性保留，Avalonia 列表暂无导航行）
    /// - SeparateDirBaseline 开启时目录排在文件前
    /// - 已排序列：按列值排序（组内保持原始顺序）；未排序：保持压缩包原始顺序
    /// </summary>
    private void ApplyCurrentSort()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // 用 VM 缓存的设置（AppSettings.Load() 每次读磁盘，不在此调用）
        var separateDirBaseline = vm.SeparateDirBaseline;
        var entries = vm.CurrentEntries.ToList();
        var path = _lastSortMemberPath;

        IOrderedEnumerable<ArchiveItemModel> sorted = entries
            .OrderBy(e => e.Name == ".." ? 0 : 1)
            .ThenBy(e => separateDirBaseline ? (e.IsDirectory ? 0 : 1) : 0);

        if (!string.IsNullOrEmpty(path))
        {
            sorted = _lastSortDescending
                ? sorted.ThenByDescending(e => GetSortValue(e, path))
                : sorted.ThenBy(e => GetSortValue(e, path));
        }

        vm.CurrentEntries.Clear();
        foreach (var item in sorted)
            vm.CurrentEntries.Add(item);
    }

    /// <summary>
    /// 更新各列头排序箭头：激活列显示 ▲/▼，其余列清空。
    /// </summary>
    private void UpdateSortArrows()
    {
        var path = _lastSortMemberPath;
        var desc = _lastSortDescending;
        SetSortArrow(NameHeaderArrow, path == "Name", desc);
        SetSortArrow(SizeHeaderArrow, path == "Size", desc);
        SetSortArrow(CompressedSizeHeaderArrow, path == "CompressedSize", desc);
        SetSortArrow(RatioHeaderArrow, path == "RatioSort", desc);
        SetSortArrow(LastModifiedHeaderArrow, path == "LastModified", desc);

        // 假列头箭头同步：Tile/Content 各 5 个单元吃同一份排序状态（内容区未创建时跳过）
        _archiveContent?.UpdateCardSortArrows(path, desc);
    }

    private static void SetSortArrow(TextBlock? arrow, bool isActive, bool descending)
    {
        if (arrow == null) return;
        arrow.Text = isActive ? (descending ? "▼" : "▲") : "";
    }

    private static IComparable GetSortValue(ArchiveItemModel item, string memberPath)
    {
        return memberPath switch
        {
            "Name" or "NameDisplay" => item.NameDisplay,
            "Size" => item.Size,
            "CompressedSize" => item.CompressedSize,
            "LastModified" => item.LastModified,
            "RatioSort" or "CompressionRatio" => item.RatioSort,
            _ => item.NameDisplay
        };
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer == null || !e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.None;
            HideDragAddOverlay();
            return;
        }

        var vm = DataContext as MainWindowViewModel;
        bool archiveLoaded = vm?.CurrentArchivePath != null && File.Exists(vm.CurrentArchivePath);

        if (archiveLoaded)
        {
            // 拖入单个压缩包 → 切换打开，不显示添加覆层（与 WPF Window_Drop 行为一致）
            var paths = GetDroppedLocalPaths(e);
            if (paths.Count == 1 && ArchiveFormatHelper.IsArchiveFile(paths[0]))
            {
                e.DragEffects = DragDropEffects.Copy;
                HideDragAddOverlay();
                return;
            }

            var engine = ArchiveEngineFactory.GetEngineByExtension(vm!.CurrentArchivePath!);
            // 分卷集不能追加条目（IsCurrentVolumeSet）：有效格式已解析成 SevenZip，光看 CanAdd 会误判成可写。
            bool canAdd = !vm.IsCurrentVolumeSet
                && engine?.CanAdd(ArchiveFormatHelper.GetFormat(vm.CurrentArchivePath!)) == true;
            if (canAdd)
            {
                e.DragEffects = DragDropEffects.Copy;
                ShowDragAddOverlay(isGreen: true,
                    LocalizationManager.T("DragAdd_OverlayAddTo", BuildTargetDisplay(vm)));
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
                ShowDragAddOverlay(isGreen: false, LocalizationManager.T("DragAdd_OverlayUnsupported"));
            }
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        HideDragAddOverlay();
    }

    private void OnWindowDragLeave(object? sender, DragEventArgs e)
    {
        HideDragAddOverlay();
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        HideDragAddOverlay();
        if (e.DataTransfer == null) return;

        var paths = GetDroppedLocalPaths(e);
        if (paths.Count == 0) return;

        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;

        if (vm.CurrentArchivePath != null && File.Exists(vm.CurrentArchivePath))
        {
            // 分支 1：拖入单个压缩包 → 切换打开
            if (paths.Count == 1 && ArchiveFormatHelper.IsArchiveFile(paths[0]))
            {
                await vm.LoadArchiveAsync(paths[0]);
                return;
            }

            // 分支 2：确认框 → 添加到当前压缩包（复用 WPF 文案与冲突处理）
            var result = await AppMessageBox.Show(
                LocalizationManager.T("Main_DragAddConfirm", paths.Count, Path.GetFileName(vm.CurrentArchivePath)),
                LocalizationManager.T("CompressConflict_Add"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, this);
            if (result == MessageBoxResult.Yes)
                await vm.AddFilesToArchiveAsync(paths);
            return;
        }

        // 未打开压缩包
        if (ArchiveFormatHelper.IsArchiveFile(paths[0]))
        {
            // 分支 3a：拖入压缩包 → 打开
            await vm.LoadArchiveAsync(paths[0]);
        }
        else
        {
            // 分支 3b：拖入非压缩包 → 压缩对话框预填源文件，确认后走统一压缩管道
            // （与「新建压缩包」同一执行链：选项回传 + AdoptPlan + 进度窗口 + 冲突弹窗）
            await vm.DragInCompressAsync(paths);
        }
    }

    /// <summary>收集拖入项的本地路径（文件 + 文件夹均支持，IStorageFolder 继承自 IStorageItem）。</summary>
    private static List<string> GetDroppedLocalPaths(DragEventArgs e)
    {
        var paths = new List<string>();
        foreach (var item in e.DataTransfer.Items)
        {
            var raw = item.TryGetRaw(DataFormat.File);
            if (raw is IStorageItem storageItem)
            {
                var path = storageItem.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
        }
        return paths;
    }

    /// <summary>构建覆层目标显示文案：压缩包名 + 当前浏览目录（如 "backup.zip/文档"）。</summary>
    private static string BuildTargetDisplay(MainWindowViewModel vm)
    {
        var archiveName = Path.GetFileName(vm.CurrentArchivePath) ?? vm.CurrentArchivePath ?? string.Empty;
        return string.IsNullOrEmpty(vm.CurrentFolder)
            ? archiveName
            : $"{archiveName}/{vm.CurrentFolder}";
    }

    /// <summary>覆层呼吸动画：与 OverlayController 相同的正弦公式 alpha 40-120（约 2s 周期），仅背景层 Opacity 呼吸。</summary>
    private void OnDragAddOverlayTimerTick(object? sender, EventArgs e)
    {
        double breath = 80 + 40 * Math.Sin(_dragAddOverlayTick * Math.PI / 10);
        byte alpha = (byte)Math.Clamp(breath, 40, 120);
        DragAddOverlayBg.Opacity = alpha / 255.0;
        _dragAddOverlayTick++;
    }

    /// <summary>显示拖拽添加覆层：绿色=可添加，红色=不可添加。视觉对齐拖拽解压覆层——
    /// 状态色呼吸背景（#6BD46B/#F43643）+ 8px 不透明边框 + 白色粗体文字 + ✓/⚠ 图标（颜色与拖拽解压一致）。</summary>
    private void ShowDragAddOverlay(bool isGreen, string text)
    {
        var color = isGreen ? Color.Parse("#6BD46B") : Color.Parse("#F43643");
        DragAddOverlay.BorderBrush = new SolidColorBrush(color);
        DragAddOverlayBg.Background = new SolidColorBrush(color);
        DragAddOverlayIcon.Text = isGreen ? "\u2713" : "\u26A0";
        DragAddOverlayIcon.Foreground = new SolidColorBrush(
            isGreen ? Color.Parse("#90E060") : Color.Parse("#FFE020"));
        DragAddOverlayText.Text = text;
        DragAddOverlay.IsVisible = true;
        _dragAddOverlayTick = 0;
        _dragAddOverlayTimer.Start();
    }

    private void HideDragAddOverlay()
    {
        DragAddOverlay.IsVisible = false;
        _dragAddOverlayTimer.Stop();
    }

    /// <summary>
    /// 取当前 DataGrid 的行选区（成组勾选用）。复选框可以跨目录累积，但选区只会包含当前目录的行，
    /// 所以这里直接读 <c>SelectedItems</c>，不读 <c>SelectedEntries</c>（后者在切换目录瞬间可能还是旧实例）。
    /// </summary>
    private IReadOnlyList<ArchiveItemModel> SelectedGridItems(MainWindowViewModel vm)
    {
        var grid = FileListGrid;
        if (grid == null || grid.SelectedItems.Count == 0)
            return vm.SelectedEntries;

        var items = new List<ArchiveItemModel>(grid.SelectedItems.Count);
        foreach (var item in grid.SelectedItems)
        {
            if (item is ArchiveItemModel model)
                items.Add(model);
        }

        return items.Count > 0 ? items : vm.SelectedEntries;
    }

    private void FileListGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // 缩略图模式（平铺/内容）正在写选区：隐藏的 DataGrid 仍双向绑定 SelectedEntry，
        // 会以「单选」回写覆盖掉刚写入的多选集合 → 由写入口（ApplyThumbnailSelection）负责同步。
        if (_isApplyingThumbnailSelection) return;

        // 复选框模式下选区与勾选是两套独立状态（2026-11-03 起）：
        // 选区照常跟随 DataGrid，勾选由复选框 / 成组切换维护，互不覆盖。
        vm.SelectedEntries.Clear();
        foreach (var item in FileListGrid.SelectedItems)
        {
            if (item is ArchiveItemModel model)
                vm.SelectedEntries.Add(model);
        }
    }


    private void OnCheckboxVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsCheckboxMode)) return;
        if (DataContext is not MainWindowViewModel vm) return;

        // 切模式时 DataGrid 会因为增删「复选框列」、以及 SelectedItem 绑定重置自己的行选区：
        // 整个过程必须屏蔽 FileListGrid_SelectionChanged 的回写，否则 VM 刚还原好的多选会被压成一条。
        _isApplyingThumbnailSelection = true;
        try
        {
            ApplyCheckboxColumnVisibility();

            // 关闭复选框模式时 VM 已把「当前目录内的勾选」还原成 SelectedEntries，
            // 但表格的行选区由 DataGrid 自己持有，这里必须再回填一次。
            if (!vm.IsCheckboxMode)
                ApplyVmSelectionToGridCore(vm);
        }
        finally
        {
            _isApplyingThumbnailSelection = false;
        }
    }

    /// <summary>
    /// 把 VM 的选区写进 DataGrid 的 <c>SelectedItems</c>（仅当前目录内的行，跨目录项加进去会抛
    /// <c>ArgumentException: The item is not contained in the ItemsSource</c>）。
    /// 调用方须已置 <c>_isApplyingThumbnailSelection</c> 闸门。
    /// </summary>
    private void ApplyVmSelectionToGridCore(MainWindowViewModel vm)
    {
        var grid = FileListGrid;
        if (grid == null) return;

        grid.SelectedItems.Clear();
        foreach (var entry in vm.SelectedEntries)
        {
            if (vm.CurrentEntries.Contains(entry))
                grid.SelectedItems.Add(entry);
        }
    }

    /// <summary>
    /// 按 <see cref="MainWindowViewModel.IsCheckboxMode"/> 切换「复选框列」的显隐。
    /// <para>
    /// 为什么不用 XAML 绑定：<c>DataGridColumn</c> 不是 <c>Control</c>，不在视觉树上、不继承 DataContext，
    /// 列上的 <c>IsVisible="{Binding …}"</c> 永远不会解析（绑定静默失败，表现为列一直在）。
    /// </para>
    /// </summary>
    private void ApplyCheckboxColumnVisibility()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var visible = vm.IsCheckboxMode;
        foreach (var column in FileListGrid.Columns)
        {
            if (column.Tag is string tag && string.Equals(tag, "Checkbox", StringComparison.Ordinal))
            {
                column.IsVisible = visible;
                if (visible) column.DisplayIndex = 0;
                break;
            }
        }
    }



    public async void LoadArchiveOnStartup(string path)
    {
        // Wait for window to be ready
        await Task.Delay(100); // Small delay to let the window initialize
        var vm = DataContext as MainWindowViewModel;
        if (vm != null)
        {
            await vm.LoadArchiveAsync(path);
        }
    }

    private async Task<string?> OpenFileDialogAsync()
    {
        // 以当前已打开压缩包的所在目录作为「场景相关路径」初值（无则 null → 走优先级链其它来源）
        var contextPath = (DataContext as MainWindowViewModel)?.CurrentArchivePath is { } c
            ? Path.GetDirectoryName(c)
            : null;
        return await CustomFilePickerDialog.ShowOpenFileAsync(
            this,
            initialPath: contextPath,
            // 文件类型下拉项从引擎工厂的单一事实源派生：早先这里硬编码 8 项，
            // 新增 .jar/.war/.ear/.apk（issue #54）时很容易漏同步。
            // 分卷集只认首卷、故补 *.001 / *.z01（issue #70③ / #68）；设置 →「文件类型关联」里的自定义扩展名也一并纳入。
            // 第三项「自解压程序 (*.exe)」默认不选中（issue #36）：只有要打开 SFX 时才需要列出 exe。
            fileTypes: [.. ArchiveFormatHelper.BuildOpenDialogFileTypes(AppSettings.Load())]);
    }

    /// <summary>
    /// 文件列表「拖出」（拖拽解压）的事件接线。
    /// 2026-10-21 启动优化：内容区延迟创建 → 拖拽接线随内容区创建时进行（构造期不再查找控件）。
    /// 拖拽主体（覆层 / 状态光标 / OLE 拖拽 / 落点解压）已抽到 <see cref="StartFileDragAsync"/>，
    /// 与平铺/内容模式容器共用同一份实现；这里只保留「按下记起点 + 移动过阈值后启动」。
    /// </summary>
    private void SetupFileListDragDrop()
    {
        var fileGrid = FileListGrid;
        if (fileGrid == null) return;

        fileGrid.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
        {
            _dragStartPoint = e.GetPosition(fileGrid);

            // 复选框模式下的两个拦截（Tunnel 阶段，早于 DataGrid 自己的选区处理）：
            //   ① Shift：DataGrid 的 Shift 只扩「行选区」，勾选不会跟着扩 ⇒ 按区间直接勾选；
            //   ② 无修饰键且当前选区有 2 项以上：把这组一起切换成与被点行相反的状态
            //      （先 Ctrl/Shift 选好几行，再点其中任意一行的复选框）。
            if (DataContext is MainWindowViewModel gridVm
                && gridVm.IsCheckboxMode
                && TryHitCheckbox(e.Source, out var checkRow)
                && checkRow != null)
            {
                var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (shift)
                {
                    ApplyCheckPointer(checkRow, shift: true, ctrl: false, gridVm);
                    e.Handled = true;
                    _dragStartEvent = null;
                    _dragPreservedSelection = null;
                    return;
                }

                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)
                    && gridVm.ToggleCheckedForGroup(checkRow, SelectedGridItems(gridVm)))
                {
                    e.Handled = true;
                    _dragStartEvent = null;
                    _dragPreservedSelection = null;
                    return;
                }
            }

            // 命中测试：仅在按下位置命中数据行时才记录拖拽起点。
            // 列标题（调整列宽/拖拽重排列）、空白区域、滚动条等按下不启动文件拖拽
            // （镜像 WPF FileListGrid_PreviewMouseMove 的 row 空检查语义）。
            var pressedItem = HitTestPressedRowItem(fileGrid, _dragStartPoint);
            if (pressedItem == null)
            {
                _dragStartEvent = null;
                _dragPreservedSelection = null;
                return;
            }
            _dragStartEvent = e;

            // 按下行是否已属于当前选区（镜像 WPF InputHitTest 语义）。
            // Tunnel 阶段先于 DataGrid 行自身的选中处理，此时 SelectedItems 仍是旧选区；
            // 若按下的是未选中行，则不保留旧多选区 —— 拖拽只拖新按下的行。
            var pressedInSelection = fileGrid.SelectedItems.Contains(pressedItem);

            // Save multi-selection state at press time (before drag starts)
            if (fileGrid.SelectedItems.Count > 1 && pressedInSelection)
            {
                _dragPreservedSelection = fileGrid.SelectedItems
                    .OfType<ArchiveItemModel>()
                    .Select(m => m.ToCoreItem())
                    .ToList();
            }
            else
            {
                _dragPreservedSelection = null;
            }
        }, RoutingStrategies.Tunnel);

        // Clear drag state on release to prevent click-selection from triggering drag
        fileGrid.AddHandler(InputElement.PointerReleasedEvent, (s, e) =>
        {
            _dragStartEvent = null;
            _dragPreservedSelection = null;
        }, RoutingStrategies.Tunnel);

        fileGrid.PointerMoved += async (s, e) =>
        {
            // 拖拽起点由上面的 Tunnel PointerPressed 记录
            if (_dragStartEvent is not { } triggerEvent) return;

            // 阈值 + 设置开关：与平铺/内容容器共用同一判据（TryBeginFileDrag）
            if (!TryBeginFileDrag(e.GetPosition(fileGrid) - _dragStartPoint)) return;

            // 先取后清：拖拽期间指针事件仍会派发，清空按下态可防止重入
            _dragStartEvent = null;
            var preservedSelection = _dragPreservedSelection;
            _dragPreservedSelection = null;

            var vm = DataContext as MainWindowViewModel;
            if (vm?.SelectedEntry == null) return;

            // 拖拽条目：多选时用按下瞬间的快照（preservedSelection），单选时用当前选中项
            var items = new List<ArchiveItemModel> { vm.SelectedEntry };
            await StartFileDragAsync(triggerEvent, items, preservedSelection);
        };
    }

    /// <summary>
    /// 工具栏代码页按钮被点击：先重建菜单项，再让 <see cref="Button"/> 的 Flyout 照常弹出。
    /// <para>
    /// 为什么在 Click 里建而不是在 XAML 里静态列：选项文案要随语言变、「其他代码页…」需要
    /// 走命令参数、当前项要打勾 —— 用 code-behind 现建能直接拿 <see cref="MainWindowViewModel"/> 的
    /// 命令与选中项，不必给 NameEncodingOption 塞 ICommand。
    /// </para>
    /// <para>
    /// Avalonia 的 <c>Button.OnClick</c> 先冒泡 Click 事件、再弹出 Flyout，所以这里重建完菜单项
    /// 正好赶上本次弹出；同时 <see cref="MenuFlyout"/> 的 <c>Opened</c> 也会重建一次兜底
    /// （重建是幂等的，不会重复添加）。
    /// </para>
    /// </summary>
    private void OnNameEncodingClick(object? sender, RoutedEventArgs e) => RebuildNameEncodingMenu();

    /// <summary>菜单弹出后按最新状态重建一次（兜底：若 Click 与弹窗顺序在某个平台上反转）。</summary>
    private void OnNameEncodingMenuOpened(object? sender, EventArgs e) => RebuildNameEncodingMenu();

    /// <summary>
    /// 按 VM 的当前选项重建代码页菜单：九个固定项，当前项打勾（<see cref="MenuItemToggleType.Radio"/>），
    /// 命令统一走 <c>ChangeNameEncodingCommand</c>（「其他代码页…」由 VM 内部再弹数字输入框）。
    /// </summary>
    private void RebuildNameEncodingMenu()
    {
        // Flyout 不在可视树里，x:Name 不会生成字段 ⇒ 从按钮上取
        if (NameEncodingButton?.Flyout is not MenuFlyout flyout || DataContext is not MainWindowViewModel vm)
            return;

        var selectedKey = vm.NameEncodingSelectedOption?.Key;
        flyout.Items.Clear();

        foreach (var option in vm.NameEncodingOptions)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = option.Display,
                Command = vm.ChangeNameEncodingCommand,
                CommandParameter = option.Key,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "NameEncoding",
                IsChecked = string.Equals(option.Key, selectedKey, StringComparison.Ordinal),
            });
        }
    }
}
