using Avalonia.Threading;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 应用退出统一漏斗（全仓唯一的退出调用点）。
///
/// 背景（已取证，2026-10-18）：Avalonia 的 <c>OnFrameworkInitializationCompleted</c> 在主循环启动
/// <b>之前</b>执行，而 CLI 分发正是在该回调内同步跑完的。此时同步调用 <c>desktop.Shutdown()</c>
/// 会关掉 UI Dispatcher，随后 <c>StartCore → Dispatcher.MainLoop → Dispatcher.PushFrame</c> 抛
/// <c>InvalidOperationException: Cannot perform requested operation because the Dispatcher shut down</c>，
/// 主线程未处理异常 → 进程以 <c>0xE0434352</c> 终止、堆栈打到 stderr
/// （实测 12 条 CLI 提前退出路径中招：<c>--help</c>、<c>--extract-*</c> 路径失效、
/// <c>--compress-*</c> 路径失效与多选压缩的非首实例分支）。
///
/// 因此退出只能走这里：
/// <list type="bullet">
/// <item><b>主循环尚未启动</b>（<see cref="MarkFrameworkInitCompleted"/> 之前）→ 改为
/// <c>Dispatcher.UIThread.Post</c>，把退出排到队列，等主循环首轮泵消息时执行（Dispatcher 已就绪）；</item>
/// <item><b>主循环已启动</b> → 与直接调用 <c>Shutdown()</c> <b>逐字相同</b>（零语义变更）。</item>
/// </list>
///
/// 判据说明：Avalonia 未公开「主循环是否已启动」的查询 API（<c>Dispatcher</c> 只有
/// <c>ShutdownStarted</c>/<c>ShutdownFinished</c> 事件），故用「<c>OnFrameworkInitializationCompleted</c>
/// 是否已返回」作为等价判据 —— 该回调内同步执行的一切（含 CLI 各入口的同步段）都落在崩溃窗口内，
/// 之后的异步延续（dispatcher 队列 / 对话框回调）都落在窗口外。若将来把
/// <see cref="MarkFrameworkInitCompleted"/> 提前到 <c>base.OnFrameworkInitializationCompleted()</c> 之前，
/// 整个窗口会退回崩溃行为（已由 AppShutdownWiringTests 的文本顺序断言锁定）。
///
/// 形参为什么是 <see cref="Action{T}"/> 而不是 <c>IControlledApplicationLifetime</c>：
/// Avalonia 12 的 <c>IApplicationLifetime</c> 在源码中被显式标注为
/// 「This interface or abstract class is -not- implementable by user code」，测试无法伪造 lifetime
/// 实例 → 漏斗的两条分支将完全没有单测。改为接收退出委托后，调用点写
/// <c>AppShutdown.Request(desktop.Shutdown)</c>（方法组），测试传一个记录用的 lambda 即可精确分离
/// 「延后 / 直通」两条分支（见 AppShutdownTests）。
/// </summary>
internal static class AppShutdown
{
    /// <summary><c>OnFrameworkInitializationCompleted</c> 是否已返回（= 主循环是否已启动的等价判据）。</summary>
    private static bool _frameworkInitCompleted;

    /// <summary>
    /// 由 <c>App.OnFrameworkInitializationCompleted</c> 在 <c>base.</c> 调用<b>之后</b>立即调用一次。
    /// </summary>
    public static void MarkFrameworkInitCompleted() => _frameworkInitCompleted = true;

    /// <summary>
    /// 请求退出应用。<paramref name="shutdown"/> 传 <c>desktop.Shutdown</c>（方法组）即可。
    /// </summary>
    public static void Request(Action<int> shutdown, int exitCode = 0)
    {
        if (!_frameworkInitCompleted)
        {
            // 主循环未启动：直接 Shutdown 会拆掉 Dispatcher，随后 MainLoop→PushFrame 抛未处理异常。
            Dispatcher.UIThread.Post(() => shutdown(exitCode));
            return;
        }

        shutdown(exitCode);
    }

    /// <summary>重置状态位（测试隔离用）。</summary>
    internal static void ResetForTest() => _frameworkInitCompleted = false;
}
