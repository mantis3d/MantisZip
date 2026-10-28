using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 错误对话框的确认载荷：一次关闭即交出完整决策（处理方式 + 是否应用到全部）。
/// 调用方只读 <see cref="ErrorDialog.Completion"/>，不再回读窗口属性——属性补读漏一项就会静默失效。
/// </summary>
public readonly record struct ErrorDialogOutcome(FileErrorAction Action, bool ApplyToAll);

/// <summary>
/// 文件读取错误对话框：重试 / 跳过 / 中止，支持"应用到全部"。
/// </summary>
public partial class ErrorDialog : Window
{
    private readonly TaskCompletionSource<ErrorDialogOutcome> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ErrorDialogOutcome? _outcome;

    /// <summary>
    /// 关窗即完成的确认载荷。模态 <c>ShowDialog</c> 与非模态 <c>Show</c> 两条路径共用；
    /// 未点按钮关闭（X / Alt+F4）时按「重试 + 当前勾选态」收尾（与旧 ResultAction 默认值一致）。
    /// </summary>
    public Task<ErrorDialogOutcome> Completion => _completion.Task;

    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T("Error_Title");
    public string RetryText => LocalizationManager.T("Error_Retry");
    public string SkipText => LocalizationManager.T("Error_Skip");
    public string AbortText => LocalizationManager.T("Error_Abort");
    public string ApplyAllText => LocalizationManager.T("Error_ApplyToAll");

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="ErrorDialog(FileErrorInfo)"/>。
    /// </summary>
    public ErrorDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ErrorDialog(FileErrorInfo info)
    {
        InitializeComponent();
        DataContext = this;

        HeaderText.Text = string.Format(LocalizationManager.T("Error_Header"), $"\"{Path.GetFileName(info.FilePath)}\"");
        ErrorMsgText.Text = info.ErrorMessage;
    }

    protected override void OnClosed(EventArgs e)
    {
        // 兜底：非按钮关闭也给出完整载荷，绝不让调用方读到 null
        _completion.TrySetResult(_outcome ?? new ErrorDialogOutcome(FileErrorAction.Retry, ApplyAllCheck.IsChecked == true));
        base.OnClosed(e);
    }

    /// <summary>一次确认事务内产出快照后再关窗：勾选态与按钮选择同时固化。</summary>
    private void Capture(FileErrorAction action)
    {
        _outcome = new ErrorDialogOutcome(action, ApplyAllCheck.IsChecked == true);
        Close(true);
    }

    private void Retry_Click(object? sender, RoutedEventArgs e) => Capture(FileErrorAction.Retry);

    private void Skip_Click(object? sender, RoutedEventArgs e) => Capture(FileErrorAction.Skip);

    private void Abort_Click(object? sender, RoutedEventArgs e) => Capture(FileErrorAction.Abort);
}
