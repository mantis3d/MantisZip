using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 桌面（主窗口）环境的 <see cref="IProgressPresenter"/> 实现：把「建窗 → 配置 → 显示 →
/// 逐条目行播种 → 跑操作 → 完成态 → 自动关闭 → 取消/异常收尾」这段窗口生命周期从
/// <c>MainWindow.RunWithProgress</c> 闭包里搬到唯一实现处。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么搬出来：</b>这段流程原先两次手抄（主窗口闭包 + CLI 的 <c>CompressWithProgress</c>），
/// 两边的收尾细节已经开始漂移（一边吞异常一边抛、一边等关闭一边直接退）。收成一个类之后，
/// 「窗口怎么呈现」只有一处真相，压缩/解压/测试只决定「跑什么、完成后做什么」。
/// </para>
/// <para>
/// <b>为什么不认识业务：</b>本类只认 <see cref="ProgressRunRequest"/> 的通用字段
/// （标题、行、说明、配置钩子、完成回调）。压缩还是解压、GUI 还是 CLI，都不该在这里出现分支，
/// 否则每加一个流程就要改一次 presenter。
/// </para>
/// </remarks>
public sealed class MainWindowProgressPresenter : IProgressPresenter
{
    /// <summary>
    /// 状态栏回写回调。
    /// 密码重试被用户取消时，旧实现会把「已取消 - 需要密码」写进主窗口状态栏；
    /// 这条信息不影响 presenter 的返回值语义，因此做成显式端口而不是让 presenter 去摸 ViewModel。
    /// </summary>
    private readonly Action<string>? _statusMessageSink;

    /// <summary>
    /// 创建桌面进度呈现器。
    /// </summary>
    /// <param name="statusMessageSink">可选的状态栏回写端口（参数为已本地化文案）。</param>
    public MainWindowProgressPresenter(Action<string>? statusMessageSink = null)
    {
        _statusMessageSink = statusMessageSink;
    }

    /// <inheritdoc />
    public ProgressWindow? ActiveWindow { get; private set; }

    /// <inheritdoc />
    public async Task<bool> RunAsync(ProgressRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 先建窗、先配置、再 Show：配置钩子里可能设置批处理列表/说明行/流程标记，
        // 若在 Show 之后做，窗口首帧会是「未配置」形态并闪烁一次。
        var window = new ProgressWindow(request.Title);
        window.InitCancellation();
        request.ConfigureWindow?.Invoke(window);
        if (request.OperationNote is not null)
            window.SetOperationNote(request.OperationNote);

        ActiveWindow = window;
        var hasFileList = request.DisplayPaths is { Count: > 0 };
        try
        {
            window.Show();
            if (hasFileList)
            {
                window.InitBatchMode(request.DisplayPaths!);
                window.SetCurrentBatchItem(0);
            }

            var progress = window.CreatePauseAwareProgress(
                ProgressViewModel.CreateBackgroundProgress(window, p => window.SetProgress(p)));
            await request.Operation(progress, window.CancellationToken);

            // 成功：整批逐行收尾（只给第 0 行收尾会让多包批处理里其余行停在最后一次上报的
            // 百分比并残留包名/底纹，看起来像「批次完成没有更新界面」），再交给调用方的完成回调
            // （测试流结果通道 / 删除后刷新）；没有回调时写默认完成文案；随后统一尊重 📌 的自动关闭倒计时。
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
            // 取消不是错误：立即关闭，不写错误汇总、不压钉。
            window.Close();
            return false;
        }
        catch (PasswordRetryCancelledException)
        {
            // 用户在密码弹窗点取消：行标记「已取消 - 需要密码」并回写状态栏，
            // 与旧 RunWithProgress 的呈现逐字对齐（行 Failed 会自动压钉等手动关闭）。
            var message = LocalizationManager.T("Status_PasswordCancelled");
            window.SetStatusMessage(message);
            // 当前批次行（不是硬编码第 0 行）置 Failed：失败行自动压钉等手动关闭
            window.FailCurrentBatchItem(message);
            _statusMessageSink?.Invoke(message);
            await window.AutoCloseOrWaitAsync(() => window.Close());
            return false;
        }
        catch (Exception ex)
        {
            // 失败：文案先落到窗口（用户看得到），然后原样抛出把结论交给调用方，
            // 由调用方决定是 Failed 还是放弃——presenter 不吞异常、也不替调用方弹框。
            var (wholeErrorText, wholeErrorDetail) = WholeArchiveErrorLocalizer.ToUserText(ex);
            window.SetErrorSummary(wholeErrorText, wholeErrorDetail);
            App.DebugLog($"[ProgressPresenter] operation failed: {ex}");
            // 当前批次行置 Failed（自动压钉）：整包失败时用户能看出是哪一行没跑完
            window.FailCurrentBatchItem();
            await window.AutoCloseOrWaitAsync(() => window.Close());
            throw;
        }
        finally
        {
            // 收尾后不再暴露窗口：调用方/会话拿到 ActiveWindow 就必须是「正在显示的那一个」。
            ActiveWindow = null;
        }
    }
}
