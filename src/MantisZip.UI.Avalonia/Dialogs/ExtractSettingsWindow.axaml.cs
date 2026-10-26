using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
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
/// </summary>
public partial class ExtractSettingsWindow : Window
{
    private bool _loaded;

    /// <summary>后台逐包校验的取消源（窗口关闭时取消，避免无谓 IO）。</summary>
    private CancellationTokenSource? _validationCts;

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
        InitializeComponent();
        ViewModel = new ExtractSettingsViewModel(Array.Empty<string>());
        DataContext = ViewModel;
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
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        _session = session;
        _context = context;

        ViewModel = Initialize(archivePaths);
        SubscribeViewModel();
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
    private void OnClosed(object? sender, EventArgs e)
    {
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
    /// 生产构造的界面装配：建 ViewModel、接好浏览/解锁/校验等宿主回调。
    /// 返回装配好的 ViewModel，让构造函数以赋值表达式拿到确定非空的属性——避免构造函数里
    /// 「调用方法后才赋值」造成的可空流分析告警。
    /// </summary>
    private ExtractSettingsViewModel Initialize(IReadOnlyList<string> archivePaths)
    {
        InitializeComponent();

        ViewModel = new ExtractSettingsViewModel(archivePaths);

        // 设置文件夹浏览回调（解压模式：弹窗内建 ResultTreeView 实时冲突检测）。
        // 冲突预览语义绑定首包条目（与提取一致），无首包条目时无法预览。
        ViewModel.BrowseFolder = async () =>
        {
            var first = ViewModel.FirstArchiveEntries;
            if (first == null)
                return null;
            return (await CustomFilePickerDialog.ShowExtractFolderAsync(this, first, ViewModel.DestinationPath))?.DestPath;
        };

        DataContext = ViewModel;

        // 窗口关闭时取消后台逐包校验 + 冲突扫描
        Closed += (_, _) =>
        {
            _validationCts?.Cancel();
            ViewModel.CancelConflictScan();
        };

        // 浏览回调：解压模式文件夹对话框（内建 ResultTreeView 实时冲突检测）。
        // QuickPathPicker 只收目录，此处返回目录即可。
        DestinationPicker.BrowseAction = async (owner, current) =>
            ViewModel.FirstArchiveEntries == null
                ? null
                : (await CustomFilePickerDialog.ShowExtractFolderAsync(
                    owner ?? this, ViewModel.FirstArchiveEntries, ViewModel.DestinationPath))?.DestPath;

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
