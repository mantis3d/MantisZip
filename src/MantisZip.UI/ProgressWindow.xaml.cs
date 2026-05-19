using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MantisZip.Core.Abstractions;
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

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    /// <summary>暂停事件。Set = 运行中，Reset = 已暂停。提取循环通过此事件阻塞。</summary>
    public ManualResetEventSlim PauseEvent => _pauseEvent;

    /// <summary>当前是否暂停</summary>
    public bool IsPaused => !_pauseEvent.IsSet;

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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pauseEvent.IsSet)
        {
            // 正在运行 → 暂停
            _pauseEvent.Reset();
            PauseButton.Content = L.T(L.Progress_Button_Resume);
            FileNameText.Text = L.T(L.Progress_Paused);
        }
        else
        {
            // 已暂停 → 恢复
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
            // 重置按钮样式
            PasswordSection.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1)); // #FFF8E1
            PasswordSection.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82)); // #FFE082
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
            PasswordSection.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
            PasswordSection.BorderBrush = new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7));
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
    /// 若在后台线程则调度到 UI 线程执行。
    /// </summary>
    private void DispatchIfNeeded(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.BeginInvoke(action);
    }

    /// <summary>
    /// 初始化取消令牌。
    /// </summary>
    public void InitCancellation()
    {
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// 将 IProgress 包装为暂停感知版本。提取循环中的 Report 调用会在暂停时阻塞。
    /// </summary>
    public IProgress<ArchiveProgress> CreatePauseAwareProgress(IProgress<ArchiveProgress> inner)
    {
        return new PauseAwareProgress(inner, _pauseEvent);
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
/// 暂停感知的 IProgress 包装器。Report 时会先等待 PauseEvent（若已重置则阻塞）。
/// </summary>
internal class PauseAwareProgress : IProgress<ArchiveProgress>
{
    private readonly IProgress<ArchiveProgress> _inner;
    private readonly ManualResetEventSlim _pauseEvent;

    public PauseAwareProgress(IProgress<ArchiveProgress> inner, ManualResetEventSlim pauseEvent)
    {
        _inner = inner;
        _pauseEvent = pauseEvent;
    }

    public void Report(ArchiveProgress value)
    {
        // 暂停时短暂等待（100ms 轮询），避免无限期阻塞线程池线程导致饥饿/死锁。
        // 暂停后用户可取消（Cancel）来停止操作。
        _pauseEvent.Wait(100);
        _inner.Report(value);
    }
}
