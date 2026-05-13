using System.Windows;
using System.Windows.Media;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI;

/// <summary>
/// 进度窗口 - 解压/压缩共用
/// 显示两个进度条：当前文件进度（上）+ 总体进度（下）
/// </summary>
public partial class ProgressWindow : Window
{
    private CancellationTokenSource? _cts;

    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    public ProgressWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 从 ArchiveProgress 更新两个进度条。
    /// </summary>
    public void SetProgress(ArchiveProgress p)
    {
        if (Dispatcher.CheckAccess())
        {
            TotalProgressBar.Value = p.PercentComplete;
            PercentText.Text = $"{p.PercentComplete:F1}%";
            FileNameText.Text = p.CurrentFile;

            if (p.FilePercentComplete.HasValue)
            {
                FileProgressBar.Value = p.FilePercentComplete.Value;
                FilePercentText.Text = $"{p.FilePercentComplete.Value:F0}%";
            }
        }
        else
        {
            Dispatcher.BeginInvoke(() =>
            {
                TotalProgressBar.Value = p.PercentComplete;
                PercentText.Text = $"{p.PercentComplete:F1}%";
                FileNameText.Text = p.CurrentFile;

                if (p.FilePercentComplete.HasValue)
                {
                    FileProgressBar.Value = p.FilePercentComplete.Value;
                    FilePercentText.Text = $"{p.FilePercentComplete.Value:F0}%";
                }
            });
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
        Dispatcher.Invoke(() =>
        {
            TotalProgressBar.Value = 100;
            PercentText.Text = "100%";
            FileProgressBar.Value = 100;
            FilePercentText.Text = "100%";
            FileNameText.Text = "完成";
            CancelButton.Content = "关闭";
        });
    }

    /// <summary>
    /// 设置错误状态。
    /// </summary>
    public void SetError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            CancelButton.Content = "关闭";
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }

    #region 密码匹配区

    private string? _password;
    private bool _isRevealed;

    /// <summary>
    /// 显示密码区，设置为"正在尝试"状态（密码隐藏、复制禁用）。
    /// 从后台线程调用安全。
    /// </summary>
    public void ShowPasswordAttempt(string description)
    {
        void Update()
        {
            PasswordSection.Visibility = Visibility.Visible;
            _password = null;
            _isRevealed = false;
            PwdMatchText.Text = "正在尝试匹配密码规则…";
            PwdRuleText.Text = $"规则：{description}";
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
                ? $"✅ 已匹配密码：{password}"
                : $"✅ 已匹配密码：******";
            PwdRevealBtn.Content = revealByDefault ? "🙈" : "👁";

            PwdRuleText.Text = $"规则：{description}";
            PwdStatusText.Text = "✅ 密码验证通过，正在解压…";
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
            PwdMatchText.Text = $"✅ 已匹配密码：{_password}";
        else if (_password != null)
            PwdMatchText.Text = $"✅ 已匹配密码：******";
        PwdRevealBtn.Content = _isRevealed ? "🙈" : "👁";
    }

    private void PwdCopyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_password)) return;
        try
        {
            Clipboard.SetText(_password);
            PwdStatusText.Text = "✅ 已复制到剪贴板";
        }
        catch
        {
            PwdStatusText.Text = "❌ 复制失败";
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

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
