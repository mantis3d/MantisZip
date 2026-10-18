// ============================================================================
// AppShutdown 漏斗行为测试
//
// 目的：锁定 Services/AppShutdown.cs 的两条核心语义——
//   ① 主循环启动前（OnFrameworkInitializationCompleted 未返回）调用 Request：
//      绝不同步退出，而是 Post 到队列，等 dispatcher 泵消息时才执行
//      （同步 Shutdown 会拆掉 Dispatcher → 随后 MainLoop→PushFrame 抛未处理异常）；
//   ② 主循环启动后调用 Request：与直接 Shutdown 等价（立即执行，零语义变更）。
//
// 为什么不用真实/伪造的 desktop lifetime：Avalonia 12 的 IApplicationLifetime 在源码中被显式标注为
// 「-not- implementable by user code」，测试无法实现该接口；真实 lifetime 又需要完整 AppBuilder/平台后端，
// 在测试进程内构造等于重蹈「启动整条主循环」的覆辙。漏斗只依赖退出委托，故测试直接传记录用 lambda。
// ============================================================================
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// AppShutdown.Request 的延后/直通语义（共 3 个用例）。
/// 静态状态位在每个用例前后复位（实现 IDisposable，xUnit 每个用例新建实例）。
/// </summary>
public class AppShutdownTests : IDisposable
{
    /// <summary>记录退出调用次数与退出码的探针。</summary>
    private sealed class ShutdownProbe
    {
        public int CallCount { get; private set; }

        public int LastExitCode { get; private set; } = int.MinValue;

        /// <summary>可直接作为 AppShutdown.Request 的退出委托。</summary>
        public void Shutdown(int exitCode)
        {
            CallCount++;
            LastExitCode = exitCode;
        }
    }

    public AppShutdownTests() => AppShutdown.ResetForTest();

    public void Dispose() => AppShutdown.ResetForTest();

    /// <summary>
    /// 测试 1：主循环启动前调用 → 立即不退出（<b>这一步正是崩溃的根因</b>），
    /// dispatcher 泵一轮消息后恰好执行一次退出，退出码 0。
    ///
    /// 锁定的回归：有人把「主循环前」的分支改回同步退出（或删掉 Post），
    /// 断言 1（立即 0 次）会最先失败。
    /// </summary>
    [AvaloniaFact]
    public void Request_BeforeMainLoop_DefersUntilDispatcherPumps()
    {
        var probe = new ShutdownProbe();

        AppShutdown.Request(probe.Shutdown);

        Assert.Equal(0, probe.CallCount); // 主循环未启动：绝不能同步退出

        Dispatcher.UIThread.RunJobs(); // 模拟主循环首轮泵消息

        Assert.Equal(1, probe.CallCount);
        Assert.Equal(0, probe.LastExitCode);
    }

    /// <summary>
    /// 测试 2：主循环启动后（MarkFrameworkInitCompleted 已调用）调用 → 立即退出，
    /// 不需要泵消息，与修复前的直接调用完全等价。
    ///
    /// 锁定的回归：有人把漏斗改成「一律 Post」，导致主循环内退出被推迟一轮
    /// （虽多数场景无害，但会改变既有退出时序，属未评估的行为变更）。
    /// </summary>
    [AvaloniaFact]
    public void Request_AfterMainLoop_ShutsDownImmediately()
    {
        AppShutdown.MarkFrameworkInitCompleted();
        var probe = new ShutdownProbe();

        AppShutdown.Request(probe.Shutdown);

        Assert.Equal(1, probe.CallCount);
        Assert.Equal(0, probe.LastExitCode);
    }

    /// <summary>
    /// 测试 3：退出码透传到退出委托（两条分支都要透传）。
    /// </summary>
    [AvaloniaFact]
    public void Request_ForwardsExitCode()
    {
        // 主循环前分支
        var beforeLoop = new ShutdownProbe();
        AppShutdown.Request(beforeLoop.Shutdown, 3);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, beforeLoop.CallCount);
        Assert.Equal(3, beforeLoop.LastExitCode);

        // 主循环后分支
        AppShutdown.MarkFrameworkInitCompleted();
        var afterLoop = new ShutdownProbe();
        AppShutdown.Request(afterLoop.Shutdown, 7);
        Assert.Equal(1, afterLoop.CallCount);
        Assert.Equal(7, afterLoop.LastExitCode);
    }
}
