namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 一次执行的终态分类。压缩与解压共用这一套分类，避免两边各自长出「成功 / 失败 / 已取消」的措辞。
///
/// 为什么用枚举而不是四个派生类型：调用方需要的是带业务名字的
/// <see cref="CompressOutcome"/> / <see cref="ExtractOutcome"/>（API 边界靠类型名表达），
/// 状态只是它的一个字段。若再把状态拆成子类型，switch 里就得写
/// <c>case CompressFailedOutcome</c> 之类，类型名喧宾夺主，而且压缩/解压要各写四个子类型。
/// </summary>
public enum RunOutcomeState
{
    /// <summary>
    /// 正常跑完。业务层面的「部分条目失败」由进度窗口的错误汇总承载，不改变「跑完了」这一事实——
    /// 否则调用方会在「已跑完」与「失败」之间重复弹窗。
    /// </summary>
    Completed,

    /// <summary>用户主动取消（进度窗口取消按钮 / 密码重试弹窗取消）。不是错误，不弹失败提示。</summary>
    Cancelled,

    /// <summary>执行中途抛异常（错误摘要已由 presenter 写进进度窗口）。原因见 <see cref="RunOutcome.Reason"/>。</summary>
    Failed,

    /// <summary>前置条件不满足，根本没有开始（路径不可写、设置不完整等）。原因见 <see cref="RunOutcome.Reason"/>。</summary>
    NotStarted,
}

/// <summary>
/// 一次压缩 / 解压执行的统一结果骨架。
///
/// 为什么需要基类：会话层（压缩会话、解压会话）共享的只是
/// 「Completed / Cancelled / Failed / NotStarted」这套语义与「失败必带原因」这个不变量；
/// 具体类型名交给派生类型，调用方不必面对 <c>object</c> 或一个别扭的泛型参数。
/// 之所以只用一层继承而不是把四态拆成子类型：状态是结果的一个字段，不是结果的种类。
/// </summary>
public abstract record RunOutcome
{
    /// <summary>派生类型只能由本程序集构造，保证下面几个不变量不被外部绕过。</summary>
    /// <param name="state">终态分类。</param>
    /// <param name="reason">面向用户的原因；正常终态用空串表示「无原因」。</param>
    private protected RunOutcome(RunOutcomeState state, string reason)
    {
        State = state;
        Reason = reason;
    }

    /// <summary>终态分类。调用方据此决定显示什么、要不要刷新列表。</summary>
    public RunOutcomeState State { get; }

    /// <summary>
    /// 面向用户的失败 / 未开始原因（已本地化）。Completed 与 Cancelled 时为空串。
    ///
    /// 为什么是「空串 = 无原因」而不是可空引用：调用方最常见的写法是
    /// <c>vm.StatusMessage = outcome.Reason;</c>；可空会把「失败原因已校验非空」这一保证
    /// 在类型系统里丢掉一半，逼每个调用点写 <c>?? string.Empty</c> 或 <c>!</c>。
    /// </summary>
    public string Reason { get; }

    /// <summary>是否正常跑完。</summary>
    public bool IsCompleted => State == RunOutcomeState.Completed;

    /// <summary>是否用户取消。</summary>
    public bool IsCancelled => State == RunOutcomeState.Cancelled;

    /// <summary>是否执行失败（错误详情已在进度窗口上展示过）。</summary>
    public bool IsFailed => State == RunOutcomeState.Failed;

    /// <summary>是否因前置条件不足而未开始。</summary>
    public bool IsNotStarted => State == RunOutcomeState.NotStarted;
}

/// <summary>
/// 一次压缩执行的终态结果。调用方只依据它决定「显示什么、是否刷新归档列表」，
/// 不再回读会话内部字段或进度窗口的残留状态。
///
/// 为什么四个状态做成静态入口、构造函数藏起来：不变量（失败必带非空原因、成功不带原因）
/// 只在 <see cref="Failed"/> / <see cref="NotStarted"/> 两处保证一次，外部也造不出
/// 「失败但没有原因」的实例。无载荷的 <see cref="Completed"/> / <see cref="Cancelled"/>
/// 用单例：record 是值语义，重复 new 没有意义。
/// </summary>
public sealed record CompressOutcome : RunOutcome
{
    /// <summary>仅供静态入口使用；原因不变量见 <see cref="Failed"/> / <see cref="NotStarted"/>。</summary>
    /// <param name="state">终态分类。</param>
    /// <param name="reason">面向用户的原因。</param>
    private CompressOutcome(RunOutcomeState state, string reason) : base(state, reason) { }

    /// <summary>压缩正常跑完。</summary>
    public static CompressOutcome Completed { get; } = new(RunOutcomeState.Completed, string.Empty);

    /// <summary>用户取消了压缩。</summary>
    public static CompressOutcome Cancelled { get; } = new(RunOutcomeState.Cancelled, string.Empty);

    /// <summary>压缩中途失败。</summary>
    /// <param name="reason">面向用户的失败原因（通常是 WholeArchiveErrorLocalizer 产出的文案）。</param>
    /// <returns>Failed 状态的结果。</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="reason"/> 为 <see langword="null"/> 或空白时抛出；Debug 构建下另外触发
    /// <see cref="System.Diagnostics.Debug.Fail(string)"/>。
    /// </exception>
    public static CompressOutcome Failed(string reason)
        => new(RunOutcomeState.Failed, OutcomeReasonGuard.Require(reason, "CompressOutcome.Failed"));

    /// <summary>压缩未开始（前置条件不满足，例如输出路径不可写）。</summary>
    /// <param name="reason">面向用户的原因，必须非空。</param>
    /// <returns>NotStarted 状态的结果。</returns>
    /// <exception cref="ArgumentException">原因被省略或只有空白字符。</exception>
    public static CompressOutcome NotStarted(string reason)
        => new(RunOutcomeState.NotStarted, OutcomeReasonGuard.Require(reason, "CompressOutcome.NotStarted"));
}

/// <summary>
/// 一次解压执行的终态结果。与 <see cref="CompressOutcome"/> 结构相同但有独立类型名：
/// 调用方签名里写的是「解压结果」，误传压缩结果应当在编译期就被挡住，
/// 而不是靠一个共用枚举在运行期靠约定区分。
/// </summary>
public sealed record ExtractOutcome : RunOutcome
{
    /// <summary>仅供静态入口使用；原因不变量见 <see cref="Failed"/> / <see cref="NotStarted"/>。</summary>
    /// <param name="state">终态分类。</param>
    /// <param name="reason">面向用户的原因。</param>
    private ExtractOutcome(RunOutcomeState state, string reason) : base(state, reason) { }

    /// <summary>解压正常跑完。</summary>
    public static ExtractOutcome Completed { get; } = new(RunOutcomeState.Completed, string.Empty);

    /// <summary>用户取消了解压。</summary>
    public static ExtractOutcome Cancelled { get; } = new(RunOutcomeState.Cancelled, string.Empty);

    /// <summary>解压中途失败。</summary>
    /// <param name="reason">面向用户的失败原因，必须非空。</param>
    /// <returns>Failed 状态的结果。</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="reason"/> 为 <see langword="null"/> 或空白时抛出；Debug 构建下另外触发
    /// <see cref="System.Diagnostics.Debug.Fail(string)"/>。
    /// </exception>
    public static ExtractOutcome Failed(string reason)
        => new(RunOutcomeState.Failed, OutcomeReasonGuard.Require(reason, "ExtractOutcome.Failed"));

    /// <summary>解压未开始（例如目标目录不可写、压缩包已不存在）。</summary>
    /// <param name="reason">面向用户的原因，必须非空。</param>
    /// <returns>NotStarted 状态的结果。</returns>
    /// <exception cref="ArgumentException">原因被省略或只有空白字符。</exception>
    public static ExtractOutcome NotStarted(string reason)
        => new(RunOutcomeState.NotStarted, OutcomeReasonGuard.Require(reason, "ExtractOutcome.NotStarted"));
}
