using Avalonia.Controls;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 一次执行收尾后宿主的退出策略。
///
/// 为什么放在上下文里而不是让执行器去问「是不是 CLI」：CLI 的弹窗流程
/// （<c>--compress</c> / <c>--extract</c>）必须用 <c>ShutdownMode.OnExplicitShutdown</c> 加显式
/// <c>Shutdown()</c> 才能退出，主窗口流程则关窗即结束；这是「宿主」的差异，执行器只需要照着做，
/// 不应该认识宿主身份。把退出时机表达成一个可执行的事实，执行器里就不会出现
/// <c>if (isCli) Shutdown();</c> 这种分支。
/// </summary>
public enum ExitPolicy
{
    /// <summary>不退出进程（桌面主窗口流程）：本次运行结束即回到正常界面，应用继续运行。</summary>
    None,

    /// <summary>
    /// 本次运行收尾后请求应用退出（CLI 弹窗流程）。
    ///
    /// 为什么叫 AfterRun 而不是 OnSuccess：成功、取消、失败三条路径 CLI 都必须退出，
    /// 否则隐藏了主窗口的进程会挂在后台不退。
    /// </summary>
    ShutdownAfterRun,
}

/// <summary>
/// 一次压缩执行（含测试流程）的宿主上下文：只装「因宿主不同而不同」的运行时依赖。
///
/// 为什么这样设计：压缩执行的入口有三处——主窗口、CLI 弹窗批处理、主窗口的测试流程——
/// 它们的差异原本靠 <c>MainWindowViewModel</c> 上的公开可变通道
/// （<c>PendingCompressFlow</c> / <c>PendingCompletedCallback</c> / <c>PendingExtractPostActions</c> …）
/// 传进 <c>RunWithProgress</c>：谁先写谁生效，忘复位就串到下一次运行，测试流程还得「借用」
/// 压缩流程的字段。收成一个不可变上下文后，宿主把自己的差异一次讲清（属主窗口、用哪个
/// presenter、结束后是否退出），执行器只读上下文，不再回读 VM 的内部状态。
///
/// 三条边界，改这个类型前先读：
/// ① 不许出现「是不是 CLI」这类宿主身份布尔——宿主差异必须表达成可执行的事实
///   （<c>ExitPolicy</c> / <see cref="DisplayPaths"/> / <see cref="Owner"/>），
///   而不是让执行器分支到不同的代码路径；
/// ② 不许装业务数据（输出路径、密码、冲突策略……）——那些属于设置载荷，随
///   <see cref="Confirmed{T}"/> 一起走到执行器；
/// ③ 不许装可变的回传通道（让执行器回调去写 VM 的 Action）——结果一律走
///   <see cref="CompressOutcome"/>。
///
/// 流程标志（<see cref="IsCompressFlow"/> / <see cref="IsTestFlow"/>）不进 presenter 契约：
/// 它们由会话层在构造 <c>ProgressRunRequest.ConfigureWindow</c> 时映射到
/// <c>ProgressWindow</c>，这样 presenter 契约不会随窗口属性增长。
/// </summary>
/// <param name="Owner">对话框与进度窗口的属主窗口。为什么必须给：模态弹窗没有属主会掉到主窗口后面；CLI 无属主时显式传 <see langword="null"/>，让「这次运行有没有宿主窗口」在调用点可见。</param>
/// <param name="Presenter">本次运行的进度呈现器（主窗口与 CLI 各自实现，见 <see cref="IProgressPresenter"/>）。</param>
/// <param name="ExitPolicy">收尾后的宿主退出策略；刻意不给默认值——「跑完要不要退出进程」必须由宿主在调用点明确声明，不能靠忘记传参时的默认行为蒙对。</param>
/// <param name="DisplayPaths">批处理进度窗口的初始行（一个元素一行）；<see langword="null"/> 或空表示不进入批处理模式。</param>
/// <param name="IsCompressFlow">本次是否按压缩流程呈现（压缩为 <see langword="true"/>；测试流程为 <see langword="false"/>，进度窗口据此显示/隐藏压缩通道说明行）。</param>
/// <param name="IsTestFlow">本次是否为测试流程（自检窗口据此自动钉住 📌，不参与自动关闭倒计时）。</param>
public sealed record CompressRunContext(
    Window? Owner,
    IProgressPresenter Presenter,
    ExitPolicy ExitPolicy,
    IReadOnlyList<string>? DisplayPaths = null,
    bool IsCompressFlow = false,
    bool IsTestFlow = false);
