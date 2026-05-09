using System.Windows;
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
