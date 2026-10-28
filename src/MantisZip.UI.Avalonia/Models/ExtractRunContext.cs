using Avalonia.Controls;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 一次解压执行的宿主上下文：只装「因宿主不同而不同」的运行时依赖。
///
/// 为什么不用 <see cref="CompressRunContext"/> 加开关复用：解压上下文里合法地没有
/// 「是否压缩流程」「是否测试流程」这两件事，共用一个大上下文会让类型上天天出现
/// 「这个字段解压时该传什么」的疑问，也给了把压缩语义再塞回解压路径的后门；
/// 两个独立类型让「解压没有压缩流程开关」成为编译期事实，而不是注释里的约定。
///
/// 与 <see cref="CompressRunContext"/> 相同，三条边界不变：
/// ① 不装宿主身份布尔（CLI / 主窗口的差异落在 <c>ExitPolicy</c>、<see cref="DisplayPaths"/>、
///   <see cref="Owner"/> 这些可执行事实上）；
/// ② 不装业务数据（目标目录、冲突策略、密码、过滤条件……属于设置载荷，随
///   <see cref="Confirmed{T}"/> 交给执行器）；
/// ③ 不装可变回传通道——结果一律走 <see cref="ExtractOutcome"/>。
/// </summary>
/// <param name="Owner">对话框与进度窗口的属主窗口；CLI 无属主时显式传 <see langword="null"/>。</param>
/// <param name="Presenter">本次运行的进度呈现器（主窗口与 CLI 各自实现，见 <see cref="IProgressPresenter"/>）。</param>
/// <param name="ExitPolicy">收尾后的宿主退出策略；刻意不给默认值，强制宿主在调用点声明。</param>
/// <param name="DisplayPaths">批处理进度窗口的初始行（多包解压时一个压缩包一行）；<see langword="null"/> 或空表示不进入批处理模式。</param>
public sealed record ExtractRunContext(
    Window? Owner,
    IProgressPresenter Presenter,
    ExitPolicy ExitPolicy,
    IReadOnlyList<string>? DisplayPaths = null);
