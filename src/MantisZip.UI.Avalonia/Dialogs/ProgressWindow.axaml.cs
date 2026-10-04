using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// Progress window shared between compress and extract operations.
/// Shows current file name, per-file progress bar, overall progress bar,
/// batch file list, password matching section, and action buttons.
/// Must be shown non-modal (use .Show()).
/// </summary>
public partial class ProgressWindow : Window
{
    private readonly ProgressViewModel _vm;

    /// <summary>最近显示的进度窗口（供冲突对话框"暂停"时定位，对齐 WPF Current.Windows.OfType&lt;ProgressWindow&gt;）。</summary>
    private static ProgressWindow? _currentVisible;

    /// <summary>
    /// 「已用时间」刷新计时器（1s）：进度由引擎经 IProgress 推送，
    /// 但两个档案之间可能长时间无报告，故时间行需独立定时刷新。
    /// OnOpened 启动 / OnClosed 停止，避免窗口关闭后继续计时（防泄漏）。
    /// </summary>
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// 当前可见的进度窗口；无则为 null。
    /// 冲突对话框点击"暂停"时由 ExtractFlow/CompressFlow 通过此属性找到进度窗口，
    /// 调用 <see cref="PauseFromConflict"/> 进入暂停态（对齐 WPF App.xaml.cs ConflictResolver）。
    /// </summary>
    public static ProgressWindow? CurrentVisible => _currentVisible;

    public ProgressWindow()
    {
        InitializeComponent();
        _vm = new ProgressViewModel();
        DataContext = _vm;

        // Wire up close request from ViewModel
        _vm.RequestClose += () =>
        {
            Dispatcher.UIThread.Post(() => Close());
        };

        // 已用时间行每秒刷新（引擎无报告期间也走字）
        _elapsedTimer.Tick += (_, _) => _vm.RefreshTimeDisplay();

        // 按初始信息量分级（Medium）落地行距与批处理列表高度
        ApplyInfoDensity();
    }

    /// <summary>
    /// Creates a progress window with a custom title.
    /// </summary>
    /// <param name="title">Window title (e.g. "正在解压..." / "正在压缩...").</param>
    public ProgressWindow(string title) : this()
    {
        _vm.WindowTitle = title;
    }

    /// <summary>
    /// Creates an <see cref="IProgress{T}"/> that dispatches callbacks to the UI thread
    /// at <see cref="DispatcherPriority.Background"/> priority.
    /// </summary>
    public static IProgress<ArchiveProgress> CreateBackgroundProgress(ProgressWindow pw)
    {
        return ProgressViewModel.CreateBackgroundProgress(pw, pw.SetProgress);
    }

    /// <summary>
    /// Creates a general-purpose <see cref="IProgress{T}"/> at Background priority.
    /// </summary>
    public static IProgress<ArchiveProgress> CreateBackgroundProgress(
        Dispatcher dispatcher, Action<ArchiveProgress> callback)
    {
        return ProgressViewModel.CreateBackgroundProgress(null!, callback);
    }

    // ════════════════════════════════════════════
    //  Properties
    // ════════════════════════════════════════════

    /// <summary>Cancellation token for the current operation.</summary>
    public CancellationToken CancellationToken => _vm.CancellationToken;

    /// <summary>Batch file items (null when not in batch mode).</summary>
    public ObservableCollection<BatchItem>? BatchItems => _vm.BatchItems;

    /// <summary>Whether batch mode is active.</summary>
    public bool IsBatchMode => _vm.IsBatchMode;

    /// <summary>Pause event. Set = running, Reset = paused.</summary>
    public ManualResetEventSlim PauseEvent => _vm.PauseEvent;

    /// <summary>Whether the operation is currently paused.</summary>
    public bool IsPaused => _vm.IsPaused;

    /// <summary>Whether batch has at least one failed item.</summary>
    public bool HasFailures => _vm.HasFailures;

    /// <summary>Whether to keep the window open on complete.</summary>
    public bool KeepOpenOnComplete => _vm.KeepOpenOnComplete;

    // ════════════════════════════════════════════
    //  Initialization & Cancellation
    // ════════════════════════════════════════════

    /// <summary>
    /// Initialize the cancellation token source.
    /// Must be called before starting the cancellable operation.
    /// </summary>
    public void InitCancellation()
    {
        _vm.InitCancellation();
    }

    // ════════════════════════════════════════════
    //  Progress Updates
    // ════════════════════════════════════════════

    /// <summary>
    /// Update all progress displays from an <see cref="ArchiveProgress"/> report.
    /// Safe to call from any thread (dispatches to UI thread internally).
    /// </summary>
    public void SetProgress(ArchiveProgress p)
    {
        _vm.SetProgress(p);
    }

    /// <summary>
    /// Compatibility overload: set overall progress and current file name only.
    /// </summary>
    public void SetProgress(double percent, string currentFile)
    {
        _vm.SetProgress(percent, currentFile);
    }

    /// <summary>
    /// Set the status message (e.g. "正在解压..." / "正在压缩..." / "完成").
    /// </summary>
    public void SetStatus(string message)
    {
        _vm.StatusMessage = message;
    }

    /// <summary>
    /// Mark the operation as complete.
    /// Sets all progress bars to 100% and changes cancel button to "Close".
    /// </summary>
    public void SetComplete(string message)
    {
        _vm.SetComplete(message);
        CancelButtonIcon.Data = (Geometry?)this.FindResource("IconCheckmark");
        CancelButtonText.Text = _vm.LocalizedStrings.TryGetValue("Progress_Button_Close", out var closeText)
            ? closeText
            : "Close";
    }

    /// <summary>
    /// Set error summary text (selectable, shown between progress bars and buttons).
    /// </summary>
    public void SetErrorSummary(string message)
    {
        _vm.SetErrorSummary(message);
    }

    // ════════════════════════════════════════════
    //  Auto-close / Manual Close
    // ════════════════════════════════════════════

    /// <summary>
    /// After completion, wait for auto-close or manual close.
    /// If KeepOpenOnComplete is set during countdown, switches to manual wait.
    /// </summary>
    public async Task AutoCloseOrWaitAsync(int delayMs, Action closeAction)
    {
        if (_vm.KeepOpenOnComplete)
        {
            await WaitForManualCloseAsync();
        }
        else
        {
            int step = 100;
            int elapsed = 0;
            while (elapsed < delayMs)
            {
                await Task.Delay(step);
                elapsed += step;
                if (_vm.KeepOpenOnComplete)
                {
                    await WaitForManualCloseAsync();
                    break;
                }
            }
        }
        closeAction();
    }

    /// <summary>
    /// Wait for the user to manually close the window.
    /// </summary>
    private async Task WaitForManualCloseAsync()
    {
        if (!IsVisible)
            return;

        var closed = new ManualResetEventSlim(false);
        EventHandler handler = null!;
        handler = (_, _) => { closed.Set(); Closed -= handler; };
        Closed += handler;
        try
        {
            await Task.Run(() => closed.Wait());
        }
        finally
        {
            Closed -= handler;
        }
    }

    // ════════════════════════════════════════════
    //  Batch Mode
    // ════════════════════════════════════════════

    /// <summary>
    /// Initialize batch mode with the given file paths.
    /// Shows the batch file list and sets the title.
    /// Must be called on the UI thread.
    /// </summary>
    public void InitBatchMode(System.Collections.Generic.IReadOnlyList<string> paths)
    {
        _vm.InitBatchMode(paths);
        BatchFileList.IsVisible = true;
        PauseButtonIcon.Data = (Geometry?)this.FindResource("IconPause");
        PauseButtonText.Text = _vm.LocalizedStrings.TryGetValue("Progress_Button_Pause", out var pauseText)
            ? pauseText
            : "Pause";
        Width = 560;
    }

    /// <summary>
    /// Mark the specified batch item as "in progress".
    /// Safe to call from any thread.
    /// </summary>
    public void SetCurrentBatchItem(int index)
    {
        DispatchIfNeeded(() => _vm.SetCurrentBatchItem(index), DispatcherPriority.Background);
    }

    /// <summary>
    /// Update the status of a specific batch item.
    /// Safe to call from any thread.
    /// </summary>
    public void UpdateBatchItemStatus(int index, BatchItemStatus status, string? errorMessage = null)
    {
        DispatchIfNeeded(() => _vm.UpdateBatchItemStatus(index, status, errorMessage), DispatcherPriority.Background);
    }

    /// <summary>
    /// Called when batch completes with errors. Shows success/failure summary.
    /// Safe to call from any thread.
    /// </summary>
    public void CompleteWithErrors()
    {
        DispatchIfNeeded(() => _vm.CompleteWithErrors(), DispatcherPriority.Background);
    }

    /// <summary>
    /// Finalize batch: marks all still-InProgress items as Completed.
    /// Safe to call from any thread.
    /// </summary>
    public void FinalizeBatch()
    {
        DispatchIfNeeded(() => _vm.FinalizeBatch(), DispatcherPriority.Background);
    }

    // ════════════════════════════════════════════
    //  Batch Password Badge（行内密码徽标）
    // ════════════════════════════════════════════

    /// <summary>
    /// 点亮/熄灭指定批处理行的密码徽标（🔄 匹配中 / 🔑●●●● 已匹配）。
    /// 路径 A（预匹配密码整批点亮）与路径 B（批循环内逐包点亮）共用此入口。
    /// Safe to call from any thread.
    /// </summary>
    public void SetBatchPasswordState(int index, BatchPasswordState state,
        string? password, string? rule, string? description)
    {
        DispatchIfNeeded(() =>
        {
            _vm.SetBatchPasswordState(index, state, password, rule, description);
            // 徽标入场缩放动画：仅 Matched 点亮瞬间一次性触发（淡入由 Opacity 绑定 + Transitions 声明式完成）
            if (state == BatchPasswordState.Matched)
                PlayBadgeEnterScale(index);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 密码徽标 0.6→1.0 缩放入场（原型 .badge-enter :172-176，0.25s；与 Opacity 淡入同步）。
    /// 一次性触发，不引入 VM 状态：先摘掉 ScaleTransform 的 Transitions 静默置 0.6（避免反向动画），
    /// 再挂回过渡并 Post 到下一帧抬到 1.0，由 0.25s DoubleTransition 完成缩放。
    /// 行容器未实现（虚拟化未命中）或找不到徽标时静默跳过——纯视觉增强，不影响功能。
    /// </summary>
    private void PlayBadgeEnterScale(int index)
    {
        if (index < 0 || _vm.BatchItems is not { } batchItems || index >= batchItems.Count)
            return;
        if (BatchFileList.ContainerFromIndex(index) is not { } container)
            return; // 行容器未创建/被虚拟化裁剪 → 跳过动画
        var badge = container.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Name == "PwdBadgeBtn");
        if (badge?.RenderTransform is not ScaleTransform scale)
            return;

        var transitions = scale.Transitions;
        scale.Transitions = null;          // 摘过渡：0.6 起始值瞬时生效
        scale.ScaleX = 0.6;
        scale.ScaleY = 0.6;
        scale.Transitions = transitions;   // 挂回：随后的 1.0 走 0.25s 过渡
        Dispatcher.UIThread.Post(() =>
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        });
    }

    // ════════════════════════════════════════════
    //  T6: 并行度 / 条目行（EntryItems）包装
    // ════════════════════════════════════════════

    /// <summary>
    /// 设置并行解压线程度（进度窗口并行统计显示用；串行路径传 1 → HasParallelDegree=false 自动隐藏）。
    /// Safe to call from any thread.
    /// </summary>
    public void SetParallelDegree(int degree)
    {
        DispatchIfNeeded(() => _vm.ParallelDegree = degree, DispatcherPriority.Background);
    }

    /// <summary>
    /// 播种条目行（列表模式初始 Pending 行；未播种路径由引擎终态事件 upsert 兜底）。
    /// 列目录在后台进行期间解压可能已先行上报若干条目——此时播种会 <c>ClearEntryItems</c>
    /// 清掉这些已上报的终态行（每条目只上报一次 → 永久停留在 Pending），故已有任何条目行时
    /// 跳过播种，整体转渐进模式（确定性优于「有时清、有时不清」）。需强制重播先调 <see cref="ClearEntries"/>。
    /// Safe to call from any thread.
    /// </summary>
    public void SeedEntries(System.Collections.Generic.IReadOnlyList<(string Key, string Name, long Size)> items)
    {
        DispatchIfNeeded(() =>
        {
            if (_vm.HasEntryItems) return;
            _vm.SeedEntryItems(items);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 清空条目行（新批次/新操作开始前调用）。
    /// Safe to call from any thread.
    /// </summary>
    public void ClearEntries()
    {
        DispatchIfNeeded(() => _vm.ClearEntryItems(), DispatcherPriority.Background);
    }

    /// <summary>
    /// Disable the cancel button. Used before entering non-interruptible operations.
    /// </summary>
    public void DisableCancel()
    {
        DispatchIfNeeded(() => _vm.DisableCancel());
    }

    // ════════════════════════════════════════════
    //  Pause / Resume
    // ════════════════════════════════════════════

    /// <summary>
    /// Called by conflict dialogs to enter paused state without toggling the button text loop.
    /// </summary>
    public void PauseFromConflict()
    {
        _vm.PauseEvent.Reset();
        _vm.IsPaused = true;
        PauseButtonIcon.Data = (Geometry?)this.FindResource("IconPlay");
        if (_vm.LocalizedStrings.TryGetValue("Progress_Button_Resume", out var resumeText))
            PauseButtonText.Text = resumeText;
        _vm.StatusMessage = LocalizationManager.T("Progress_Paused");
    }

    /// <summary>
    /// Create a pause-aware progress wrapper.
    /// </summary>
    public IProgress<ArchiveProgress> CreatePauseAwareProgress(IProgress<ArchiveProgress> inner)
    {
        return _vm.CreatePauseAwareProgress(inner);
    }

    // ════════════════════════════════════════════
    //  Content Mode / Info Density（顶部切换区）
    // ════════════════════════════════════════════

    // 内容模式三选：仅切换 VM 枚举属性，派生可见性（IsSimpleMode/IsDetailedMode/IsListMode）由 VM 集中通知。
    // 处理器内二次校验 IsChecked——RadioButton 取消勾选时也会触发 Click，直接赋值会误重置模式。

    private void ModeFullPathRadio_Click(object? sender, RoutedEventArgs e)
    {
        if (ModeFullPathRadio.IsChecked == true)
            _vm.ContentMode = Models.ProgressContentMode.Simple;
    }

    private void ModeDirOnlyRadio_Click(object? sender, RoutedEventArgs e)
    {
        if (ModeDirOnlyRadio.IsChecked == true)
            _vm.ContentMode = Models.ProgressContentMode.Detailed;
    }

    private void ModeNameOnlyRadio_Click(object? sender, RoutedEventArgs e)
    {
        if (ModeNameOnlyRadio.IsChecked == true)
            _vm.ContentMode = Models.ProgressContentMode.List;
    }

    // 信息量分级三选：赋值 VM 枚举后立即重排（行距 + 批处理列表高度）。
    private void DensityCompactRadio_Click(object? sender, RoutedEventArgs e)
    {
        if (DensityCompactRadio.IsChecked != true) return;
        _vm.InfoDensity = Models.ProgressInfoDensity.Minimal;
        ApplyInfoDensity();
    }

    private void DensityNormalRadio_Click(object? sender, RoutedEventArgs e)
    {
        if (DensityNormalRadio.IsChecked != true) return;
        _vm.InfoDensity = Models.ProgressInfoDensity.Medium;
        ApplyInfoDensity();
    }

    private void DensityLooseRadio_Click(object? sender, RoutedEventArgs e)
    {
        if (DensityLooseRadio.IsChecked != true) return;
        _vm.InfoDensity = Models.ProgressInfoDensity.Full;
        ApplyInfoDensity();
    }

    /// <summary>
    /// 按当前信息量分级重排批处理列表可视高度（信息量分级控制「显示多少信息」，非间距）。
    /// 数值全部从紧凑度资源键推导（Rule 5：不硬编码间距/高度），跟随全局紧凑度设置联动。
    /// 行距不再由此处动态调整——<c>RootGrid.RowSpacing</c> 固定绑定 {DynamicResource SpacingXs}，
    /// 间距唯一来源是全局紧凑度（CompactnessMode）。
    /// </summary>
    private void ApplyInfoDensity()
    {
        // 批处理列表可视行数：Minimal 5 行 / Medium 8 行 / Full 11 行（行高基准 = ControlHeightMd）
        double rowHeight = GetResourceDouble("ControlHeightMd", fallback: 32);
        double rows = _vm.InfoDensity == Models.ProgressInfoDensity.Minimal ? 5
            : (_vm.InfoDensity == Models.ProgressInfoDensity.Full ? 11 : 8);
        BatchFileList.MaxHeight = rowHeight * rows;
    }

    /// <summary>从窗口/应用资源中读取 double 类型的紧凑度资源键（如 SpacingXs / ControlHeightMd）。</summary>
    private double GetResourceDouble(string key, double fallback)
    {
        if (this.TryFindResource(key, ThemeVariant.Default, out var res) && res is double d)
            return d;
        if (Application.Current?.Resources.TryGetResource(key, ThemeVariant.Default, out var appRes) == true
            && appRes is double ad)
            return ad;
        return fallback;
    }

    // ════════════════════════════════════════════
    //  批处理行密码徽标 Flyout（掩码/明文 + 复制明文 + 规则/描述）
    // ════════════════════════════════════════════

    /// <summary>Flyout 当前显示的密码与显示态（Flyout 关闭即失效，由 Opening 重置）。</summary>
    private string? _flyoutPassword;

    /// <summary>Flyout 是否处于明文显示态（初始值取 PasswordRevealByDefault 设置）。</summary>
    private bool _flyoutRevealed;

    /// <summary>
    /// 当前打开的密码 Flyout 内容根面板（同一时刻仅一个 Flyout 打开，故单字段即可）。
    /// 由 <see cref="PwdBadgeBtn_Click"/> 填充，👁/复制两个处理器据此定位命名控件。
    /// </summary>
    private StackPanel? _pwdFlyoutPanel;

    /// <summary>复制 ✓ 图标复原计时器（1.2s 一次性，对齐原型 :1011；OnClosed 停止防泄漏）。</summary>
    private DispatcherTimer? _copyIconTimer;

    /// <summary>复制成功 Toast 隐藏计时器（1.6s 一次性，对齐原型 :1072；OnClosed 停止防泄漏）。</summary>
    private DispatcherTimer? _copyToastTimer;

    /// <summary>
    /// 点击行内 🔑●●●● 徽标：填充 Flyout 内容（按 x:Name，避免继承触发元素 DataContext 导致串行数据）。
    /// </summary>
    private void PwdBadgeBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not BatchItem item)
            return;

        _flyoutPassword = item.MatchedPassword;
        _flyoutRevealed = Models.AppSettings.Load().PasswordRevealByDefault;

        var flyout = button.Flyout;
        if (flyout is not Flyout { Content: StackPanel panel })
            return;
        _pwdFlyoutPanel = panel;

        // 掩码/明文文本（初始态尊重 PasswordRevealByDefault）
        panel.FindControl<TextBlock>("PwdFlyoutMaskText")!.Text =
            _flyoutRevealed ? _flyoutPassword ?? string.Empty : MaskPassword(_flyoutPassword);
        // 👁 图标同步（IconEye / IconEyeOff 均为既有图标资源）
        panel.FindControl<PathIcon>("PwdFlyoutEyeIcon")!.Data =
            (Geometry?)this.FindResource(_flyoutRevealed ? "IconEye" : "IconEyeOff");

        // 规则行（为空则隐藏该行）
        var ruleText = panel.FindControl<TextBlock>("PwdFlyoutRuleText")!;
        ruleText.Text = string.IsNullOrEmpty(item.PasswordRule)
            ? string.Empty
            : LocalizationManager.T("Progress_Batch_Pwd_Rule", item.PasswordRule);
        ruleText.IsVisible = !string.IsNullOrEmpty(item.PasswordRule);

        // 描述行（为空则隐藏该行）
        var descText = panel.FindControl<TextBlock>("PwdFlyoutDescText")!;
        descText.Text = string.IsNullOrEmpty(item.PasswordDescription)
            ? string.Empty
            : LocalizationManager.T("Progress_Batch_Pwd_Desc", item.PasswordDescription);
        descText.IsVisible = !string.IsNullOrEmpty(item.PasswordDescription);
    }

    /// <summary>👁 切换：仅切显示态（不影响复制——复制恒为明文）。</summary>
    private void PwdFlyoutRevealBtn_Click(object? sender, RoutedEventArgs e)
    {
        var panel = _pwdFlyoutPanel;
        var maskText = panel?.FindControl<TextBlock>("PwdFlyoutMaskText");
        var eyeIcon = panel?.FindControl<PathIcon>("PwdFlyoutEyeIcon");
        if (maskText == null || eyeIcon == null)
            return;

        _flyoutRevealed = !_flyoutRevealed;
        maskText.Text = _flyoutRevealed ? _flyoutPassword ?? string.Empty : MaskPassword(_flyoutPassword);
        eyeIcon.Data = (Geometry?)this.FindResource(_flyoutRevealed ? "IconEye" : "IconEyeOff");
    }

    /// <summary>复制按钮：恒复制明文密码（不复制掩码）。</summary>
    private async void PwdFlyoutCopyBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_flyoutPassword)) return;

        var statusTarget = _pwdFlyoutPanel?.FindControl<TextBlock>("PwdFlyoutMaskText");

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            var transfer = new global::Avalonia.Input.DataTransfer();
            var item = new global::Avalonia.Input.DataTransferItem();
            item.SetText(_flyoutPassword);
            transfer.Add(item);
            await clipboard.SetDataAsync(transfer);
            if (statusTarget != null)
                statusTarget.Text = LocalizationManager.T("Progress_PwdToClipboard");

            // ✓ 复制反馈：图标翻转 1.2s 后复原 + 底部 Toast 1.6s 后隐藏（原型 :1007-1013）
            ShowCopyFeedback(sender);
        }
        catch
        {
            // 剪贴板为尽力而为操作，失败仅提示不抛出
            if (statusTarget != null)
                statusTarget.Text = LocalizationManager.T("Progress_PwdCopyFailed");
        }
    }

    /// <summary>
    /// 复制成功反馈（原型 .copied + showToast）：
    /// ① 按钮 Content 图标短暂翻转为 ✓，1.2s 后复原为复制图标；
    /// ② 窗口底部 CopyToast 淡入显示，1.6s 后隐藏。
    /// 两个一次性计时器重复触发前先停旧的（防叠加），OnClosed 统一清理。
    /// </summary>
    private void ShowCopyFeedback(object? sender)
    {
        // ① 图标翻转：sender 即复制按钮，其 Content 为未命名 PathIcon（无需 x:Name）
        if (sender is Button { Content: PathIcon copyIcon })
        {
            copyIcon.Data = (Geometry?)this.FindResource("IconCheckmark");

            _copyIconTimer?.Stop();
            var iconTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            iconTimer.Tick += (_, _) =>
            {
                iconTimer.Stop();
                if (ReferenceEquals(_copyIconTimer, iconTimer)) _copyIconTimer = null;
                copyIcon.Data = (Geometry?)this.FindResource("IconCopy");
            };
            _copyIconTimer = iconTimer;
            iconTimer.Start();
        }

        // ② Toast 淡入：先置可见（Opacity 保持 0），下一拍抬到 1 触发 DoubleTransition 0.2s 淡入
        if (this.FindControl<Border>("CopyToast") is { } toast)
        {
            toast.Opacity = 0;
            toast.IsVisible = true;
            Dispatcher.UIThread.Post(() => toast.Opacity = 1);

            _copyToastTimer?.Stop();
            var toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
            toastTimer.Tick += (_, _) =>
            {
                toastTimer.Stop();
                if (ReferenceEquals(_copyToastTimer, toastTimer)) _copyToastTimer = null;
                toast.IsVisible = false;
                toast.Opacity = 0;
            };
            _copyToastTimer = toastTimer;
            toastTimer.Start();
        }
    }

    /// <summary>密码掩码（固定 6 位 ●，不泄露密码长度；空密码返回空串）。</summary>
    private static string MaskPassword(string? password) =>
        string.IsNullOrEmpty(password) ? string.Empty : "●●●●●●";

    private void PauseButton_Click(object? sender, RoutedEventArgs e)
    {
        _vm.TogglePauseCommand.Execute(null);
if (_vm.IsPaused)
        {
            PauseButtonIcon.Data = (Geometry?)this.FindResource("IconPlay");
            if (_vm.LocalizedStrings.TryGetValue("Progress_Button_Resume", out var resumeText))
                PauseButtonText.Text = resumeText;
        }
        else
        {
            PauseButtonIcon.Data = (Geometry?)this.FindResource("IconPause");
            if (_vm.LocalizedStrings.TryGetValue("Progress_Button_Pause", out var pauseText))
                PauseButtonText.Text = pauseText;
        }
    }

    // ════════════════════════════════════════════
    //  Window Lifecycle
    // ════════════════════════════════════════════

    /// <summary>
    /// 用户点击窗口右上角 X（CloseReason = WindowClosing）时触发取消，
    /// 与取消按钮行为一致，避免压缩/解压被强行终止留下损坏的压缩包。
    /// 程序化关闭（Close()/ApplicationShutdown）不触发取消。
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.WindowClosing && _vm.IsCancelEnabled)
        {
            _vm.CancelOperation();
        }
        base.OnClosing(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _currentVisible = this;
        // 启动「已用时间」刷新（窗口关闭时停止，防计时器泄漏）
        _elapsedTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ReferenceEquals(_currentVisible, this))
            _currentVisible = null;
        _elapsedTimer.Stop();
        // 复制反馈一次性计时器清理（关闭后不再触发图标复原/Toast 隐藏，防残留）
        _copyIconTimer?.Stop();
        _copyIconTimer = null;
        _copyToastTimer?.Stop();
        _copyToastTimer = null;
        _vm.PauseEvent.Set();
        base.OnClosed(e);
    }

    /// <summary>
    /// Dispatch an action to the UI thread.
    /// </summary>
    private void DispatchIfNeeded(Action action, DispatcherPriority? priority = null)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action, priority ?? DispatcherPriority.Normal);
        }
    }
}
