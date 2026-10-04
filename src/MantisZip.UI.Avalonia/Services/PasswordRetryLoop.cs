using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Views;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 用户在密码弹窗点了取消（区别于引擎密码错误）——调用方据此把当前条目标记为
/// 「已取消 - 需要密码」并继续后续流程（批处理继续下一个包，决策 D7）。
/// </summary>
public sealed class PasswordRetryCancelledException : Exception
{
    /// <summary>被取消的压缩包路径。</summary>
    public string ArchivePath { get; }

    public PasswordRetryCancelledException(string archivePath)
        : base(archivePath)
    {
        ArchivePath = archivePath;
    }
}

/// <summary>密码重试循环的终态。</summary>
public enum PasswordRetryOutcome
{
    /// <summary>解压成功（密码正确或无需密码）。</summary>
    Success,
    /// <summary>用户在密码弹窗点取消。</summary>
    Cancelled,
    /// <summary>压缩包损坏 / 数据错误（继续重弹密码无意义）。</summary>
    CorruptedOrInvalid,
}

/// <summary>
/// 解压密码弹窗兜底：引擎抛出密码类异常时弹 <see cref="PasswordDialog"/>，
/// 输错则提示「密码错误」并重弹，直到正确、取消或判定损坏。
///
/// 语义对齐主窗口浏览的 Phase B 循环（<c>MainWindowViewModel</c> Phase B：QuickVerifyPasswordEx →
/// WrongPassword 提示后 continue 重弹 → CorruptedOrInvalid 直接终止）。
///
/// 线程模型：引擎的密码抛点全部位于并行派发之前（ZIP 顺序/并行入口的 OpenArchive、7z 构造），
/// 且 Parallel.ForEachAsync 会等待 in-flight 批次收尾后才传播异常 —— 因此本方法被调用时
/// 引擎已完全 unwind，不存在「解压进行中弹窗」。
///
/// 层级约束：本类型只在 UI 层的 5 个叶子解压入口使用；Core 与引擎不知道弹窗存在（禁止下沉）。
/// </summary>
public static class PasswordRetryLoop
{
    /// <summary>
    /// 执行一次解压尝试，失败且为密码类错误时进入弹窗重试循环。
    /// </summary>
    /// <param name="archivePath">压缩包路径（弹窗标题用其文件名）。</param>
    /// <param name="initialPassword">首轮尝试使用的密码（null = 无密码）。</param>
    /// <param name="engine">叶子调用点已持有的引擎，用于快速验证新输入的密码。</param>
    /// <param name="owner">弹窗 owner（CLI 流程传进度窗口；主窗口流程可传 null，
    /// 此时退化为 <see cref="ProgressWindow.CurrentVisible"/> 或无 owner 弹窗）。</param>
    /// <param name="setStatus">错密码 / 损坏提示回调（批处理接进度窗口 SetStatus，单文件接状态栏）。</param>
    /// <param name="attempt">以给定密码执行一次解压。</param>
    /// <param name="ct">取消令牌（<see cref="OperationCanceledException"/> 原样上抛，绝不吞）。</param>
    public static async Task<(PasswordRetryOutcome Outcome, string? Password)> RunAsync(
        string archivePath,
        string? initialPassword,
        IArchiveEngine engine,
        Window? owner,
        Action<string> setStatus,
        Func<string?, CancellationToken, Task> attempt,
        CancellationToken ct)
    {
        var password = initialPassword;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await attempt(password, ct);
                return (PasswordRetryOutcome.Success, password);
            }
            catch (OperationCanceledException)
            {
                // 用户取消整个操作：原样上抛，交由既有取消链路处理
                throw;
            }
            catch (Exception ex)
            {
                // 非密码类错误（磁盘满、权限、文件缺失…）不属于本循环职责，原样上抛
                if (!ArchiveService.IsPasswordRelatedError(ex))
                    throw;

                var entered = await ShowPasswordDialogAsync(archivePath, owner);
                if (entered == null)
                    return (PasswordRetryOutcome.Cancelled, null);

                // 快速验证：错密码提示后重弹；损坏直接终止；正确则以新密码重跑 attempt
                var verifyInfo = new PasswordService().QuickVerifyPasswordEx(archivePath, entered, engine);
                if (verifyInfo.Result == PasswordVerificationResult.WrongPassword)
                {
                    setStatus(LocalizationManager.T("Status_WrongPassword"));
                    continue;
                }
                if (verifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
                {
                    setStatus(LocalizationManager.T("Status_ArchiveCorrupted"));
                    return (PasswordRetryOutcome.CorruptedOrInvalid, null);
                }

                password = entered;
            }
        }
    }

    /// <summary>
    /// 在 UI 线程弹出密码对话框；返回 null 表示用户取消。
    /// owner 优先取当前可见的进度窗口（CLI 批处理形态），其次调用方传入的窗口，
    /// 最后当前活动窗口；全部不可用时退化为非模态 Show + 等 Closed。
    /// </summary>
    private static async Task<string?> ShowPasswordDialogAsync(string archivePath, Window? owner)
    {
        var dialogOwner = ResolveDialogOwner(owner);
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new PasswordDialog(Path.GetFileName(archivePath));
            if (dialogOwner != null)
                return await dialog.ShowDialog<bool>(dialogOwner) ? dialog.Password : null;

            // 无可用 owner（无头/纯后台调用）：非模态显示并等关闭，行为等价于模态取消路径
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            await closed.Task;
            return dialog.Password;
        });
    }

    /// <summary>按「进度窗口 → 调用方窗口 → 当前活动窗口」顺序解析弹窗 owner。</summary>
    private static Window? ResolveDialogOwner(Window? owner)
    {
        if (ProgressWindow.CurrentVisible != null)
            return ProgressWindow.CurrentVisible;
        if (owner != null)
            return owner;
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.Windows.FirstOrDefault(w => w.IsVisible && w.IsActive);
    }
}