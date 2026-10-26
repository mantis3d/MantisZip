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
        var columnStates = WindowStateManager.Load(this, out var savedSortColumnPath, out var savedSortDirection);
        _pendingColumnStates = columnStates;

        // 恢复持久化的列排序状态（方向编码与 WPF window.json 兼容：0=无, 1=升序, 2=降序）
        _lastSortMemberPath = string.IsNullOrEmpty(savedSortColumnPath) ? null : savedSortColumnPath;
        _lastSortDescending = savedSortDirection == 2;
        // WindowStateManager.Load 读盘 + 列状态/排序恢复
        Services.StartupTimer.Mark("Win.State");

        // 布局快照（目录树/文件列表列宽 + 预览各位置记忆尺寸）与预览位置的应用
        // 同样延后到内容区创建后（ApplyPreviewLayout 见 EnsureArchiveContent）
        Services.StartupTimer.Mark("Win.Layout");

        var vm = new MainWindowViewModel();
        // 主 VM 构造（内含 PreviewViewModel 构造）
        Services.StartupTimer.Mark("Win.VM");
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

        vm.ShowExtractSettingsDialog = async (evm) =>
        {
            var dialog = new ExtractSettingsWindow(evm.ArchivePaths);

            // Pass archive entries for preview tree
            var allItems = vm.GetAllRawItems();
            if (allItems.Count > 0)
                dialog.SetEntries(allItems);

            var result = await dialog.ShowDialog<bool>(this);
            if (result)
            {
                // 执行侧设置整体接管（单一入口，与压缩侧同构）；历史教训见 CopyExecutionSettingsFrom 的 XML 注释
                evm.CopyExecutionSettingsFrom(dialog.ViewModel);

                // 文件过滤：仅当启用过滤且有条目时，将匹配条目 key 回传，供实际解压只解压匹配项
                evm.FilteredEntryKeys = dialog.GetFilteredEntryKeys();
            }
            return result;
        };

        vm.ShowExtractFolderPicker = (entries, initialPath, currentFolder, preserveFullPath) =>
            CustomFilePickerDialog.ShowExtractFolderAsync(this, entries, initialPath, currentFolder, preserveFullPath);

        vm.ShowCompressSettingsDialog = async (cvm) =>
        {
            var dialog = new CompressSettingsWindow(cvm.SelectedPaths);
            var result = await dialog.ShowDialog<bool>(this);
            if (result)
            {
                // 执行侧设置整体接管（单一入口）；历史上逐属性手抄漏过 ConflictAction / SplitSize /
                // MultiThreadedCompression 等，见 CopyExecutionSettingsFrom 的 XML 注释
                cvm.CopyExecutionSettingsFrom(dialog.ViewModel);
                // 过滤条件由 View 从 FileFilterEditor 读出，不在 ViewModel 的可拷贝字段内
                cvm.FileFilter = dialog.GetFilter();

                // 接管 B 数据集：对话框 VM 构建的过滤后压缩计划（含每源输出路径 + 匹配文件白名单）。
                // 必须最后执行 —— 前面 SelectedPaths 拷贝会触发无参重建，AdoptPlan 使在途重建过期，
                // 确保执行侧消费的是对话框内用户所见的一致结果（预览 = 实际）。
                cvm.AdoptPlan(dialog.ViewModel.GetPlanForExecution());
            }
            return result;
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
            var pw = new ProgressWindow(title);
            pw.InitCancellation();
            // Task 6: 压缩流程标记（供压缩通道说明行显示；解压流程保持 false）
            pw.IsCompressFlow = vm.PendingCompressFlow;
            vm.PendingCompressFlow = false;
            // Task 6: 测试流标记（自动按下 📌，完成后不自动关闭）与完成回调/最近窗口回读通道
            pw.IsTestFlow = vm.PendingTestFlow;
            vm.PendingTestFlow = false;
            var onCompleted = vm.PendingCompletedCallback;
            vm.PendingCompletedCallback = null;
            vm.LastProgressWindow = pw;
            // spec §11.2: 解压后置开关能力位 + 初值（仅解压入口置位 → 压缩/测试流两开关隐藏）
            var postActions = vm.PendingExtractPostActions;
            vm.PendingExtractPostActions = null;
            if (postActions is { } pa)
                pw.SetExtractPostActions(pa.OpenFolder, pa.DeleteArchive, pa.OpenFolderSeed);
            // 列表播种：压缩流程枚举全量条目（后台；失败/取消/超阈值放弃，终态 upsert 兜底）
            var seedRequest = vm.PendingCompressRequest;
            vm.PendingCompressRequest = null;
            if (seedRequest is not null)
                CompressFlow.TrySeedEntryItemsInBackground(pw, seedRequest, pw.CancellationToken);
            var hasFileList = filePaths is { Count: > 0 };

            // 批处理状态上报：操作闭包内经 BatchStatusReporter 传给引擎 onItemStatus
            vm.BatchStatusReporter = (index, status) =>
            {
                pw.SetCurrentBatchItem(index);
                pw.UpdateBatchItemStatus(index, status);
            };

            // 批处理输出路径上报：冲突解决后（自动重命名/手输包名）把列表里的旧包名换成实际包名
            vm.BatchOutputPathReporter = (index, outputPath) => pw.UpdateBatchItemName(index, outputPath);

            try
            {
                pw.Show();
                if (hasFileList)
                {
                    pw.InitBatchMode(filePaths!);
                    pw.SetCurrentBatchItem(0);
                }

                var progress = pw.CreatePauseAwareProgress(
                    ProgressViewModel.CreateBackgroundProgress(pw, p => pw.SetProgress(p)));
                await operation(progress, pw.CancellationToken);

                // 成功收尾：整批逐行置完成。此前只硬编码 `UpdateBatchItemStatus(0, Completed)`，
                // 多包批处理里除第一个包以外的行会停在最后一次上报的百分比（98% 之类），
                // 且残留包名/底纹，看起来像「批次完成没有更新界面」。
                if (hasFileList)
                    pw.FinalizeBatch();

                // 成功：完成回调（测试流结果通道）优先，否则默认完成态；
                // 再尊重 📌 KeepOpenOnComplete 统一走 AutoCloseOrWaitAsync（延迟来自设置，Task 6）
                if (onCompleted is not null)
                    onCompleted(pw);
                else
                    pw.SetComplete(LocalizationManager.T("Cli_StatusDone"));
                await pw.AutoCloseOrWaitAsync(() => pw.Close());
                return true;
            }
            catch (OperationCanceledException)
            {
                // D13：取消不是错误 → 立即关闭（不写错误汇总、不压钉）
                pw.Close();
                return false;
            }
            catch (PasswordRetryCancelledException)
            {
                // 用户在密码弹窗点取消：行标记「已取消 - 需要密码」并在状态栏提示。
                // 主窗口全部解压入口（ExtractArchive/Here/ToName/ExtractTo/SmartExtract）均经本闭包，
                // 故在此单点接线，避免逐个方法重复分支。
                var msg = LocalizationManager.T("Status_PasswordCancelled");
                // 同步进度窗口状态行（否则取消发生在首报前时会停在「正在准备…」）
                pw.SetStatusMessage(msg);
                if (hasFileList)
                    pw.FailCurrentBatchItem(msg);   // 行 Failed 自动压钉 → 等手动关闭
                vm.StatusMessage = msg;
                await pw.AutoCloseOrWaitAsync(() => pw.Close());   // 📌 已由行 Failed 压下 → 等手动关闭
                return false;
            }
            catch (Exception ex)
            {
                // 失败：写错误汇总（自动压钉 📌）+ 日志，行标 Failed；关窗决策交给 AutoCloseOrWaitAsync 等待手动关闭
                var (wholeErrorText, wholeErrorDetail) = WholeArchiveErrorLocalizer.ToUserText(ex);
                pw.SetErrorSummary(wholeErrorText, wholeErrorDetail);
                App.DebugLog($"[RunWithProgress] operation failed: {ex}");
                if (hasFileList)
                    pw.FailCurrentBatchItem();
                await pw.AutoCloseOrWaitAsync(() => pw.Close());   // SetErrorSummary 已自动压钉 → 等手动关闭
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

    private async void FileListGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not ArchiveItemModel item) return;
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

    private void FileListGrid_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not ArchiveItemModel item) return;
        if (DataContext is not MainWindowViewModel vm) return;

        switch (e.Key)
        {
            case global::Avalonia.Input.Key.Enter:
                if (item.IsDirectory)
                {
                    vm.NavigateToFolderPath(item.FullPath);
                    e.Handled = true;
                }
                break;

            case global::Avalonia.Input.Key.Back:
                vm.GoUpCommand.Execute(null);
                e.Handled = true;
                break;

            case global::Avalonia.Input.Key.Delete:
                vm.DeleteFilesCommand.Execute(null);
                e.Handled = true;
                break;
        }
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

        // 文件列表「拖出」（拖拽解压）接线
        SetupFileListDragDrop();

        // 启动期推迟的恢复动作（此时控件已存在）
        ApplyColumnStates(_pendingColumnStates);
        UpdateSortArrows();
        ApplySavedLayout();   // 必须在 ApplyPreviewLayout() 之前（预览尺寸回填）
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
            bool canAdd = engine?.CanAdd(ArchiveFormatHelper.GetFormat(vm.CurrentArchivePath!)) == true;
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

    private void FileListGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.SelectedEntries.Clear();
        foreach (var item in FileListGrid.SelectedItems)
        {
            if (item is ArchiveItemModel model)
                vm.SelectedEntries.Add(model);
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
            fileExtensions:
            [
                "*.zip", "*.7z", "*.rar", "*.tar", "*.tgz", "*.tar.gz", "*.gz", "*.iso"
            ]);
    }

    /// <summary>
    /// 文件列表「拖出」（拖拽解压）的事件接线。
    /// 2026-10-21 启动优化：内容区延迟创建 → 拖拽接线随内容区创建时进行（构造期不再查找控件）。
    /// </summary>
    private void SetupFileListDragDrop()
    {
        var fileGrid = FileListGrid;
        if (fileGrid == null) return;

        fileGrid.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
        {
            _dragStartPoint = e.GetPosition(fileGrid);

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
            if (_dragStartEvent == null) return;

            var pos = e.GetPosition(fileGrid);
            var delta = pos - _dragStartPoint;
            // 拖拽启动阈值：镜像 WPF SystemParameters.MinimumHorizontalDragDistance (~4px)，
            // 避免 32px 造成的"拖起来黏手"感
            if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
                return;

            // 设置开关：EnableDragExtract = false 时不启动拖拽（与 WPF MainWindow.DragDrop.cs 行为一致）
            if (!AppSettings.Load().EnableDragExtract)
            {
                App.DebugLog("[MainWindow] EnableDragExtract off — drag skipped");
                _dragStartEvent = null;
                _dragPreservedSelection = null;
                return;
            }

            // Save trigger event before nulling (Avalonia DragDrop.DoDragDropAsync needs it)
            var triggerEvent = _dragStartEvent;
            _dragStartEvent = null; // Prevent re-entry

            var vm2 = DataContext as MainWindowViewModel;
            if (vm2?.SelectedEntry == null) return;
            var archivePath = vm2.CurrentArchivePath;
            if (string.IsNullOrEmpty(archivePath)) return;

            // Get selected items (support multi-select)
            var selectedItems = _dragPreservedSelection
                ?? new List<ArchiveItem> { vm2.SelectedEntry.ToCoreItem() };
            var allItems = vm2.GetAllRawItems();

            var password = vm2.GetSessionPassword(archivePath);

            // Expand items: directories become their contained files (flat list)
            var expandedItems = DragDropItemExpander.ExpandItems(selectedItems, allItems);
            if (expandedItems.Count == 0)
                return;

            _isOwnDrag = true;
            vm2.StatusMessage = LocalizationManager.T("Status_DragHint");

            // ── Create Avalonia overlay window (works on UI thread, controlled via Win32 from background) ──
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

                // Remove title bar via Win32 (avoids Avalonia 12 enum compatibility issues)
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
                    triggerEvent, MantisZipDragFormatName, archivePath,
                    cursorProvider, DragDropEffects.Copy,
                    () => _dragCancelled = true);
                App.DebugLog($"[MainWindow] Custom OLE DoDragDrop DONE: result={result}");

                // Close overlay IMMEDIATELY after drag completes, before dialog processing
                controller.Stop();
                overlayWin.Close();
                App.DebugLog("[MainWindow] Overlay closed");

                if (_dragCancelled)
                {
                    // 用户按 Esc 或右键取消拖拽 → 不执行解压
                    App.DebugLog("[MainWindow] Drag cancelled during drag — extraction skipped");
                    if (vm2 != null)
                        vm2.StatusMessage = LocalizationManager.T("Status_DragDragCancelled");
                }
                else
                {
                    NativeMethods.GetCursorPos(out var dropPt);
                    App.DebugLog($"[MainWindow] Drop point captured: ({dropPt.X}, {dropPt.Y})");

                    if (vm2 != null && !string.IsNullOrEmpty(archivePath))
                    {
                        vm2.StatusMessage = LocalizationManager.T("Status_DragDetectingTarget");
                        var dragService = new DragDropService(
                            archivePath, password, this, vm2.CurrentFolder ?? "");
                        await dragService.ExecuteAfterDropAsync(
                            selectedItems, allItems, vm2);
                    }
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
        };
    }
}
