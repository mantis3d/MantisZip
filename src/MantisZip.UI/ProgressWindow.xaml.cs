using System.Windows;

namespace MantisZip.UI;

/// <summary>
/// 进度窗口 - 解压/压缩共用
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
    /// 设置进度
    /// </summary>
    public void SetProgress(double percent, string currentFile)
    {
        App.Log("【ProgressWindow】SetProgress: {0}% - {1}", percent, currentFile);
        
        // 尝试 BeginInvoke 代替 Invoke
        if (Dispatcher.CheckAccess())
        {
            App.Log("【ProgressWindow】同线程，直接执行");
            ProgressBar.Value = percent;
            PercentText.Text = $"{percent:F1}%";
            FileNameText.Text = currentFile;
        }
        else
        {
            App.Log("【ProgressWindow】BeginInvoke...");
            Dispatcher.BeginInvoke(() =>
            {
                App.Log("【ProgressWindow】BeginInvoke 执行");
                ProgressBar.Value = percent;
                PercentText.Text = $"{percent:F1}%";
                FileNameText.Text = currentFile;
            });
        }
    }

    /// <summary>
    /// 设置完成状态
    /// </summary>
    public void SetComplete(string message)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.Value = 100;
            PercentText.Text = "100%";
            StatusText.Text = message;
            FileNameText.Text = "完成";
            CancelButton.Content = "关闭";
        });
    }

    /// <summary>
    /// 设置错误状态
    /// </summary>
    public void SetError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            CancelButton.Content = "关闭";
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // 取消操作
        _cts?.Cancel();
        Close();
    }

    /// <summary>
    /// 初始化取消令牌
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