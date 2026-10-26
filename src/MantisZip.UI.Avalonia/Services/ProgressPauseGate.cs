using System.Threading.Tasks;
using MantisZip.UI.Avalonia.Dialogs;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 进度窗口「暂停/继续」的异步等待闸门。
///
/// 背景：<see cref="ProgressWindow.PauseEvent"/> 是 <see cref="System.Threading.ManualResetEventSlim"/>，
/// 全仓没有 Task 化替代（ProgressViewModel 只暴露该事件）。冲突弹窗的暂停分支原先直接
/// <c>PauseEvent.Wait(ct)</c>，而该分支的续体回到 UI 线程 —— 暂停后 UI 线程被 hold 住，
/// 进度窗口的「继续」按钮再也点不动（真死锁，不是原注释说的「后台线程等待」）。
///
/// 这里把**阻塞等待明确挪到线程池**：只在暂停期间占用一个线程，恢复或关窗
/// （ProgressWindow.OnClosed 会 Set 事件）即释放；暂停语义与令牌语义与原来完全一致，
/// UI 线程不再被阻塞（不是用 Task.Run 把 UI 线程阻塞藏起来，而是把不可 await 的事件等待移出 UI 线程）。
/// 更彻底的方案是给 ProgressViewModel 增加 awaitable 暂停信号（TaskCompletionSource + Set/Reset 配对），
/// 但那要改 ProgressWindow/ProgressViewModel（本任务范围外），故先按此实现。
/// </summary>
internal static class ProgressPauseGate
{
    /// <summary>异步等待进度窗口离开暂停态；令牌取消时抛出 <see cref="System.OperationCanceledException"/>（与原语义一致）。</summary>
    internal static Task WaitForResumeAsync(ProgressWindow window)
        => Task.Run(() => window.PauseEvent.Wait(window.CancellationToken), window.CancellationToken);
}
