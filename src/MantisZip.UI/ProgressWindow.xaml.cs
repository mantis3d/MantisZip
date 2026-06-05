using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// 进度窗口 - 解压/压缩共用
/// 显示两个进度条：当前文件进度（上）+ 总体进度（下）
/// </summary>
public partial class ProgressWindow : Window
{
    /// <summary>
    /// Creates an <see cref="IProgress{T}"/> that dispatches callbacks to the UI thread
    /// at <see cref="DispatcherPriority.Background"/> priority.
    /// This bypasses <see cref="System.Progress{T}"/> which always uses Normal priority (above Render),
    /// allowing the progress bar to be visually updated between progress reports.
    /// </summary>
    public static IProgress<ArchiveProgress> CreateBackgroundProgress(ProgressWindow pw)
    {
        return new BackgroundDispatcherProgress(pw.Dispatcher, pw.SetProgress);
    }

    /// <summary>
    /// Creates a general-purpose <see cref="IProgress{T}"/> that invokes <paramref name="callback"/>
    /// on the given <paramref name="dispatcher"/> at Background priority.
    /// Safe to call from any thread.
    /// </summary>
    public static IProgress<ArchiveProgress> CreateBackgroundProgress(Dispatcher dispatcher, Action<ArchiveProgress> callback)
    {
        return new BackgroundDispatcherProgress(dispatcher, callback);
    }

    /// <summary>
    /// Custom IProgress that dispatches directly to the UI thread at Background priority.
    /// Unlike Progress&lt;T&gt; which uses SynchronizationContext.Post (Normal priority, 8 > Render 6),
    /// this uses Background (3 < Render 6) so the dispatcher can process Render items
    /// between progress updates, enabling the progress bar to actually paint intermediate values.
    /// </summary>
    private sealed class BackgroundDispatcherProgress : IProgress<ArchiveProgress>
    {
        private readonly Dispatcher _dispatcher;
        private readonly Action<ArchiveProgress> _callback;

        public BackgroundDispatcherProgress(Dispatcher dispatcher, Action<ArchiveProgress> callback)
        {
            _dispatcher = dispatcher;
            _callback = callback;
        }

        public void Report(ArchiveProgress value)
        {
            // From any thread: queue the update at Background priority.
            // Background (3) < Render (6), so the dispatcher will process this,
            // then process any pending Render (paint) items, then the next Background item.
            _dispatcher.BeginInvoke((Action)(() => _callback(value)), DispatcherPriority.Background);
        }
    }

    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _pauseEvent = new(initialState: true);
    private ObservableCollection<BatchItem>? _batchItems;
    private bool _isBatchMode;
    private int _currentBatchIndex = -1;
    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(100);

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    /// <summary>暂停事件。Set = 运行中，Reset = 已暂停。提取循环通过此事件阻塞。</summary>
    public ManualResetEventSlim PauseEvent => _pauseEvent;

    /// <summary>当前是否暂停</summary>
    public bool IsPaused => !_pauseEvent.IsSet;

    /// <summary>当前是否为批处理模式</summary>
    public bool IsBatchMode => _isBatchMode;

    /// <summary>批处理中是否有失败项</summary>
    public bool HasFailures => _batchItems?.Any(i => i.Status == BatchItemStatus.Failed) ?? false;

    public ProgressWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 从 ArchiveProgress 更新两个进度条。
    /// 此方法由 BackgroundDispatcherProgress 在 Background 优先级下调用，
    /// 或直接从 UI 线程调用。无论哪种情况，我们都在 UI 线程上，直接更新控件。
    /// </summary>
    public void SetProgress(ArchiveProgress p)
    {
        App.LogDebug("[TRACE] ProgressWindow.SetProgress called: PercentComplete={0}, FilePercentComplete={1}, CurrentFile='{2}'",
            p.PercentComplete, (object?)p.FilePercentComplete ?? "null", p.CurrentFile ?? "");

        TotalProgressBar.Value = p.PercentComplete;
        PercentText.Text = $"{p.PercentComplete:F1}%";
        FileNameText.Text = p.CurrentFile;

        if (p.FilePercentComplete.HasValue)
        {
            FileProgressBar.Value = p.FilePercentComplete.Value;
            FilePercentText.Text = $"{p.FilePercentComplete.Value:F0}%";
        }

        // 文件计数：TotalFiles > 0 时才显示，否则保持空白
        if (p.TotalFiles > 0)
        {
            FileCountText.Text = L.TF(L.Progress_FileCount, p.ProcessedFiles, p.TotalFiles);
            FileCountText.Visibility = Visibility.Visible;
        }

        // 批处理模式：更新当前项的进度百分比（100ms 节流）
        if (_isBatchMode && _currentBatchIndex >= 0 && _batchItems != null &&
            _currentBatchIndex < _batchItems.Count)
        {
            var now = DateTime.UtcNow;
            // 0% 和 100% 强制刷新，中间值每 100ms 刷新一次
            if (p.PercentComplete >= 100 || p.PercentComplete <= 0 ||
                (now - _lastProgressUpdate) >= ProgressThrottle)
            {
                _batchItems[_currentBatchIndex].Progress = p.PercentComplete;
                _lastProgressUpdate = now;
            }
        }
    }

    /// <summary>
    /// 兼容旧调用：只设总体进度。
    /// </summary>
    public void SetProgress(double percent, string currentFile)
    {
        SetProgress(new ArchiveProgress
        {
            PercentComplete = percent,
            CurrentFile = currentFile
        });
    }

    /// <summary>
    /// 设置完成状态。
    /// </summary>
    public void SetComplete(string message)
    {
        App.LogDebug("[TRACE] ProgressWindow.SetComplete called: message='{0}'", message);
        TotalProgressBar.Value = 100;
        PercentText.Text = "100%";
        FileProgressBar.Value = 100;
        FilePercentText.Text = "100%";
        FileNameText.Text = L.T(L.Progress_Done);
        CancelButton.Content = L.T(L.Progress_Button_Close);
    }

    /// <summary>
    /// L.T(L.Settings_Title)L.T(L.App_ErrorTitle)状态。
    /// </summary>
    public void SetError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            CancelButton.Content = L.T(L.Progress_Button_Close);
        });
    }

    #region 批处理模式

    /// <summary>
    /// 初始化批处理模式。设置文件列表、标题和窗口大小。
    /// 必须在 UI 线程调用。
    /// </summary>
    public void InitBatchMode(IReadOnlyList<string> paths)
    {
        App.LogDebug("[BATCH] InitBatchMode: entering, paths.Count={0}", paths.Count);
        _isBatchMode = true;
        _currentBatchIndex = -1;
        _lastProgressUpdate = DateTime.MinValue;
        _batchItems = new ObservableCollection<BatchItem>(
            paths.Select(p => new BatchItem
            {
                Name = Path.GetFileName(p),
                FullPath = p,
                Status = BatchItemStatus.Pending
            }));

        BatchFileList.ItemsSource = _batchItems;
        BatchFileList.Visibility = Visibility.Visible;
        Title = L.T(L.Progress_Batch_Title);
        ResizeMode = ResizeMode.CanResizeWithGrip;
        App.LogDebug("[BATCH] InitBatchMode: items=[{0}]", string.Join(", ", _batchItems.Select(b => $"\"{b.Name}\"")));
    }

    /// <summary>
    /// 将指定索引的批处理项标记为"进行中"，并更新文件名显示。
    /// 可从后台线程安全调用。
    /// </summary>
    public void SetCurrentBatchItem(int index)
    {
        void Update()
        {
            if (_batchItems == null || index < 0 || index >= _batchItems.Count)
            {
                App.LogDebug("[BATCH] SetCurrentBatchItem: index={0} out of range (count={1}), skipping", index, _batchItems?.Count ?? -1);
                return;
            }

            // 将前一项标记为完成（如果还是 InProgress）
            if (index > 0 && _batchItems[index - 1].Status == BatchItemStatus.InProgress)
            {
                App.LogDebug("[BATCH] SetCurrentBatchItem: completing previous item[{0}]=\"{1}\"", index - 1, _batchItems[index - 1].Name);
                _batchItems[index - 1].Status = BatchItemStatus.Completed;
                _batchItems[index - 1].Progress = 100;
            }

            // 只覆盖 Pending 状态的项 — 不覆盖已被 onItemStatus 设为 Skipped/Completed/Failed 的项。
            // 由于 Dispatcher 队列顺序不确定（日志中 UpdateBatchItemStatus 在 SetCurrentBatchItem 之前执行），
            // 不能依赖后台线程的调用顺序。
            if (_batchItems[index].Status != BatchItemStatus.Pending)
            {
                App.LogDebug("[BATCH] SetCurrentBatchItem: item[{0}]=\"{1}\" already has status {2}, not overwriting", index, _batchItems[index].Name, _batchItems[index].Status);
                return;
            }

            App.LogDebug("[BATCH] SetCurrentBatchItem: marking item[{0}]=\"{1}\" as InProgress", index, _batchItems[index].Name);
            _currentBatchIndex = index;
            _batchItems[index].Status = BatchItemStatus.InProgress;
            _batchItems[index].Progress = 0;
            FileNameText.Text = _batchItems[index].Name;
        }
        DispatchIfNeeded(Update, DispatcherPriority.Background);
    }

    /// <summary>
    /// 更新指定批处理项的状态。
    /// 可从后台线程安全调用。
    /// </summary>
    public void UpdateBatchItemStatus(int index, BatchItemStatus status, string? errorMessage = null)
    {
        void Update()
        {
            if (_batchItems == null || index < 0 || index >= _batchItems.Count)
            {
                App.LogDebug("[BATCH] UpdateBatchItemStatus: index={0} out of range (count={1}), status={2}, skipping", index, _batchItems?.Count ?? -1, status);
                return;
            }

            App.LogDebug("[BATCH] UpdateBatchItemStatus: item[{0}]=\"{1}\" status: {2} -> {3}{4}",
                index, _batchItems[index].Name, _batchItems[index].Status, status,
                status == BatchItemStatus.Failed && errorMessage != null ? $", error=\"{errorMessage}\"" : "");

            _batchItems[index].Status = status;
            if (status == BatchItemStatus.Failed)
                _batchItems[index].ErrorMessage = errorMessage;
            if (status == BatchItemStatus.Skipped)
                _batchItems[index].Progress = 100;
        }
        DispatchIfNeeded(Update, DispatcherPriority.Background);
    }

    /// <summary>
    /// 批处理完成但有失败项时调用。
    /// 显示成功/失败汇总，窗口保持打开供用户查看。
    /// 可从后台线程安全调用。
    /// </summary>
    public void CompleteWithErrors()
    {
        void Update()
        {
            if (_batchItems == null) return;

            int succeeded = _batchItems.Count(i => i.Status == BatchItemStatus.Completed);
            int failed = _batchItems.Count(i => i.Status == BatchItemStatus.Failed);
            App.LogDebug("[BATCH] CompleteWithErrors: succeeded={0}, failed={1}, total={2}", succeeded, failed, _batchItems.Count);

            SetComplete(L.TF(L.Progress_Batch_CompleteWithErrors, succeeded, failed));
        }
        DispatchIfNeeded(Update, DispatcherPriority.Background);
    }

    /// <summary>
    /// 批处理完成后调用，将所有还处于 InProgress 的项设为 Completed。
    /// 解决最后一项从未收到完成状态的问题。
    /// </summary>
    public void FinalizeBatch()
    {
        void Update()
        {
            if (_batchItems == null) return;
            for (int i = 0; i < _batchItems.Count; i++)
            {
                if (_batchItems[i].Status == BatchItemStatus.InProgress)
                {
                    _batchItems[i].Status = BatchItemStatus.Completed;
                    _batchItems[i].Progress = 100;
                }
            }
        }
        DispatchIfNeeded(Update, DispatcherPriority.Background);
    }

    #endregion

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("[BATCH] CancelButton_Click: _isBatchMode={0}, _cts?.IsCancellationRequested={1}", _isBatchMode, _cts?.IsCancellationRequested ?? false);
        if (_isBatchMode)
        {
            // 批处理模式下，Cancel 按钮变为"关闭"；直接关闭窗口不 Cancel
            App.LogDebug("[BATCH] CancelButton_Click: batch mode, closing window");
            Close();
            return;
        }
        App.LogDebug("[BATCH] CancelButton_Click: cancelling operation");
        _cts?.Cancel();
        Close();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pauseEvent.IsSet)
        {
            // 正在运行 → 暂停
            App.LogDebug("[BATCH] PauseButton_Click: pausing (pauseEvent was Set)");
            _pauseEvent.Reset();
            PauseButton.Content = L.T(L.Progress_Button_Resume);
            FileNameText.Text = L.T(L.Progress_Paused);
        }
        else
        {
            // 已暂停 → 恢复
            App.LogDebug("[BATCH] PauseButton_Click: resuming (pauseEvent was Reset)");
            _pauseEvent.Set();
            PauseButton.Content = L.T(L.Progress_Button_Pause);
            FileNameText.Text = L.T(L.Progress_Resuming);
        }
    }

    #region 密码匹配区

    private string? _password;
    private bool _isRevealed;

    /// <summary>
    /// 显示密码区，设置为"正在尝试"L.T(L.Settings_Menu_StatusGroup)（L.T(L.PwdMgr_Col_Password)隐藏、复制禁用）。
    /// 从后台线程调用安全。
    /// </summary>
    public void ShowPasswordAttempt(string description)
    {
        void Update()
        {
            PasswordSection.Visibility = Visibility.Visible;
            _password = null;
            _isRevealed = false;
            PwdMatchText.Text = L.T(L.Progress_MatchingPassword);
            PwdRuleText.Text = L.TF(L.Progress_PwdRule, description);
            PwdStatusText.Text = "";
            PwdRevealBtn.IsEnabled = false;
            PwdCopyBtn.IsEnabled = false;
            // 重置按钮样式（使用主题颜色）
            PasswordSection.Background = (SolidColorBrush)Application.Current.Resources["Theme_StatusWarning"];
            PasswordSection.BorderBrush = (SolidColorBrush)Application.Current.Resources["Theme_StatusWarning"];
        }
        DispatchIfNeeded(Update);
    }

    /// <summary>
    /// 密码验证通过，显示匹配到的密码，启用复制按钮。
    /// 根据 AppSettings.PasswordRevealByDefault 决定初始为明文或掩码。
    /// </summary>
    public void ShowPasswordMatched(string password, string description)
    {
        void Update()
        {
            PasswordSection.Visibility = Visibility.Visible;
            _password = password;

            // 根据设置决定默认是否显示明文
            bool revealByDefault = AppSettings.Instance.PasswordRevealByDefault;
            _isRevealed = revealByDefault;
            PwdMatchText.Text = revealByDefault
                ? L.TF(L.Progress_PwdMatched, password)
                : L.T(L.Progress_PwdMatchedHidden);
            PwdRevealBtn.Content = revealByDefault ? "🙈" : "👁";

            PwdRuleText.Text = L.TF(L.Progress_PwdRule, description);
            PwdStatusText.Text = L.T(L.Progress_PwdVerifying);
            PwdRevealBtn.IsEnabled = true;
            PwdCopyBtn.IsEnabled = true;
            PasswordSection.Background = (SolidColorBrush)Application.Current.Resources["Theme_StatusSuccess"];
            PasswordSection.BorderBrush = (SolidColorBrush)Application.Current.Resources["Theme_StatusSuccess"];
        }
        DispatchIfNeeded(Update);
    }

    /// <summary>
    /// 禁用取消按钮。在进入不可中断的关键操作前调用，防止用户误取消。
    /// </summary>
    public void DisableCancel()
    {
        DispatchIfNeeded(() => CancelButton.IsEnabled = false);
    }

    /// <summary>
    /// 隐藏密码区（用于无加密文件）。
    /// </summary>
    public void HidePasswordSection()
    {
        DispatchIfNeeded(() => PasswordSection.Visibility = Visibility.Collapsed);
    }

    private void PwdRevealBtn_Click(object sender, RoutedEventArgs e)
    {
        _isRevealed = !_isRevealed;
        if (_isRevealed && _password != null)
            PwdMatchText.Text = L.TF(L.Progress_PwdMatched, _password);
        else if (_password != null)
            PwdMatchText.Text = L.T(L.Progress_PwdMatchedHidden);
        PwdRevealBtn.Content = _isRevealed ? "🙈" : "👁";
    }

    private void PwdCopyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_password)) return;
        try
        {
            Clipboard.SetText(_password);
            PwdStatusText.Text = L.T(L.Progress_PwdToClipboard);
        }
        catch
        {
            PwdStatusText.Text = L.T(L.Progress_PwdCopyFailed);
        }
    }

    #endregion

    /// <summary>
    /// 调度到 UI 线程执行。默认 Normal 优先级（兼容现有调用方）。
    /// 批处理方法请使用 <see cref="DispatcherPriority.Background"/> 以便与进度报告交错执行。
    /// </summary>
    private void DispatchIfNeeded(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        Dispatcher.BeginInvoke(action, priority);
    }

    /// <summary>
    /// 初始化取消令牌。
    /// </summary>
    public void InitCancellation()
    {
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// 将 IProgress 包装为暂停感知版本。提取循环中的 Report 调用会在暂停时阻塞，
    /// 直到恢复或取消。
    /// </summary>
    public IProgress<ArchiveProgress> CreatePauseAwareProgress(IProgress<ArchiveProgress> inner)
    {
        return new PauseAwareProgress(inner, _pauseEvent, _cts?.Token ?? CancellationToken.None);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Dispose();
        _pauseEvent.Set(); // 确保不会阻塞后台线程
        _pauseEvent.Dispose();
        base.OnClosed(e);
    }
}

/// <summary>
/// 暂停感知的 IProgress 包装器。Report 时会先等待 PauseEvent（若已重置则阻塞直到恢复或取消）。
/// </summary>
internal class PauseAwareProgress : IProgress<ArchiveProgress>
{
    private readonly IProgress<ArchiveProgress> _inner;
    private readonly ManualResetEventSlim _pauseEvent;
    private readonly CancellationToken _cancellationToken;

    public PauseAwareProgress(IProgress<ArchiveProgress> inner, ManualResetEventSlim pauseEvent, CancellationToken cancellationToken)
    {
        _inner = inner;
        _pauseEvent = pauseEvent;
        _cancellationToken = cancellationToken;
    }

    public void Report(ArchiveProgress value)
    {
        try
        {
            // 暂停时无限期阻塞，直到用户点击恢复（_pauseEvent 被 Set）或取消。
            // Wait(CancellationToken) 在令牌被取消时抛出 OperationCanceledException。
            _pauseEvent.Wait(_cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 取消时不再上报进度
            return;
        }
        _inner.Report(value);
    }
}
