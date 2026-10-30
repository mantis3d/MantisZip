using Avalonia.Threading;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 命令行（无主窗口）环境的 <see cref="IProgressPresenter"/> 实现：把 CLI 手抄的
/// 「进度窗 → 跑操作 → 收尾 → 自动关闭/等待」搬进唯一实现处。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单独一份而不是复用桌面实现：</b>两者共享流程骨架，但 CLI 有两个不可杂交的差异：
/// ① 进度窗可能是**已显示**的窗口（<c>--compress-separate</c> 的 IPC 收集窗），不能再 Show 一次；
/// ② 压缩引擎调用必须离开 UI 线程（CLI 没有主窗口可泵消息，重活留在 UI 线程会让进度窗假死）。
/// 用构造参数表达这两点，比在实现里判断「是不是 CLI」干净。
/// </para>
/// <para>
/// <b>为什么取消/异常的处理与桌面一致：</b>两条入口的收尾语义本来就该一致（取消静默、
/// 异常先写摘要再抛出），CLI 侧历史差异只在「退出进程由谁负责」——那属于宿主的退出策略，
/// 不属于呈现器。
/// </para>
/// </remarks>
public sealed class CliProgressPresenter : IProgressPresenter
{
    /// <summary>已有的进度窗口（已 Show）；为 null 时由本类创建并显示。</summary>
    private readonly ProgressWindow? _existingWindow;

    /// <summary>窗口就绪后的宿主接线（CLI 用来把窗口设为 <c>desktop.MainWindow</c>）。</summary>
    private readonly Action<ProgressWindow>? _onWindowReady;

    /// <summary>是否把操作放到线程池执行（压缩引擎的重活不能占 UI 线程）。</summary>
    private readonly bool _runOperationOffUiThread;

    /// <summary>
    /// 创建 CLI 进度呈现器。
    /// </summary>
    /// <param name="existingWindow">
    /// 已显示的进度窗口（如 <c>--compress-separate</c> 的收集窗）。传 null 时本类自行建窗并显示。
    /// </param>
    /// <param name="onWindowReady">窗口就绪后的宿主接线，便于宿主设置主窗口/退出策略。</param>
    /// <param name="runOperationOffUiThread">是否把操作放到线程池（默认 true）。</param>
    public CliProgressPresenter(
        ProgressWindow? existingWindow = null,
        Action<ProgressWindow>? onWindowReady = null,
        bool runOperationOffUiThread = true)
    {
        _existingWindow = existingWindow;
        _onWindowReady = onWindowReady;
        _runOperationOffUiThread = runOperationOffUiThread;
    }

    /// <inheritdoc />
    public ProgressWindow? ActiveWindow { get; private set; }

    /// <inheritdoc />
    public async Task<bool> RunAsync(ProgressRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var window = _existingWindow ?? new ProgressWindow(request.Title);
        // startup-native-splash：进度窗即 CLI 的终端窗口，出现即关 splash（与旧实现一致）
        NativeSplash.Hook(window);
        ActiveWindow = window;

        request.ConfigureWindow?.Invoke(window);
        if (request.OperationNote is not null)
            window.SetOperationNote(request.OperationNote);

        if (_existingWindow is null)
        {
            window.InitCancellation();
            window.Show();
        }
        _onWindowReady?.Invoke(window);

        var hasFileList = request.DisplayPaths is { Count: > 0 };
        if (hasFileList)
        {
            window.InitBatchMode(request.DisplayPaths!);
            window.SetCurrentBatchItem(0);
        }

        try
        {
            var progress = window.CreatePauseAwareProgress(
                ProgressViewModel.CreateBackgroundProgress(window, p => window.SetProgress(p)));

            if (_runOperationOffUiThread)
                await Task.Run(() => request.Operation(progress, window.CancellationToken));
            else
                await request.Operation(progress, window.CancellationToken);

            // 批处理收尾：把仍停在「进行中」的行统一置终态（旧 CLI 的 FinalizeBatch 语义）。
            if (hasFileList)
                window.FinalizeBatch();
            if (request.OnCompleted is not null)
                request.OnCompleted(window);
            else
                window.SetComplete(LocalizationManager.T("Cli_StatusDone"));
            await window.AutoCloseOrWaitAsync(() => window.Close());
            return true;
        }
        catch (OperationCanceledException)
        {
            window.Close();
            return false;
        }
        catch (Exception ex)
        {
            // 失败：错误摘要 + 计数落窗口（压钉等手动关闭），日志走 stderr（CLI 的传统通道），
            // 然后原样抛出，由调用方决定退出码/退出时机。
            var (wholeErrorText, wholeErrorDetail) = WholeArchiveErrorLocalizer.ToUserText(ex);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                window.SetErrorSummary(wholeErrorText, wholeErrorDetail);
                window.CompleteWithErrors();
            });
            Console.Error.WriteLine($"Progress operation failed: {ex.Message}");
            if (hasFileList)
                window.UpdateBatchItemStatus(0, BatchItemStatus.Failed, ex.Message);
            await window.AutoCloseOrWaitAsync(() => window.Close());
            throw;
        }
        finally
        {
            ActiveWindow = null;
        }
    }
}
