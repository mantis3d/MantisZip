using System.Threading;
using System.Windows;
using System.Windows.Media;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// 进度窗口 - 解压/压缩共用
/// 显示两个进度条：当前文件进度（上）+ 总体进度（下）
/// </summary>
public partial class ProgressWindow : Window
{
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
    /// </summary>
    public void SetProgress(ArchiveProgress p)
    {
        void UpdateUI()
        {
            TotalProgressBar.Value = p.PercentComplete;
            PercentText.Text = $"{p.PercentComplete:F1}%";
            FileNameText.Text = p.CurrentFile;

            if (p.FilePercentComplete.HasValue)
            {
                FileProgressBar.Value = p.FilePercentComplete.Value;
                FilePercentText.Text = $"{p.FilePercentComplete.Value:F0}%";
            }

            // 文件计数：TotalFiles > 0 时才L.T(L.Pwd_ShowBtn)，L.T(L.MsgBox_No)则保持空白
            if (p.TotalFiles > 0)
            {
                FileCountText.Text = L.TF(L.Progress_FileCount, p.ProcessedFiles, p.TotalFiles);
                FileCountText.Visibility = Visibility.Visible;
            }
        }

        if (Dispatcher.CheckAccess())
            UpdateUI();
        else
            Dispatcher.BeginInvoke((Action)UpdateUI);
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
        Dispatcher.Invoke(() =>
        {
            TotalProgressBar.Value = 100;
            PercentText.Text = "100%";
            FileProgressBar.Value = 100;
            FilePercentText.Text = "100%";
            FileNameText.Text = L.T(L.Progress_Done);
            CancelButton.Content = L.T(L.Progress_Button_Close);
        });
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
        _pauseEvent.Wait(); // 暂停时阻塞，恢复后继续
        _inner.Report(value);
    }
}
