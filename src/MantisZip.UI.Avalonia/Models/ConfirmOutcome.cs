using System.Diagnostics;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 用户在确认型对话框（压缩设置 / 解压设置 / 删除确认等）里做完决定后的唯一结果形态。
///
/// 为什么这样设计：旧流程用 <c>bool?</c> 返回值表示「确认/取消」，确认后还要回读对话框成员或
/// <c>MainWindowViewModel</c> 上的公开可变通道（<c>Pending*</c> 属性）才拿到设置——调用方必须自己记住
/// 「null 是取消、false 是什么、设置从哪个属性取」，任一分支忘了判空就会带着默认值继续跑。
/// 结果类型化之后三种状态互斥且穷尽：确认即携带不可变快照（<see cref="Confirmed{T}"/>）、
/// 拒绝必须给出原因（<see cref="Rejected{T}"/>）、取消不再与「确认了但没内容」混淆（<see cref="Cancelled{T}"/>）。
/// 调用方可一次写完 <c>switch (r) { case Confirmed&lt;CompressSettings&gt; c: ...; case Rejected&lt;CompressSettings&gt; x: ...; }</c>。
///
/// 基类不带数据，只用于统一方法签名与「先兜住取消」的兜底分支；泛型参数是载荷类型，
/// 因此同一个对话框的实现可以被不同设置类型复用。
/// </summary>
public abstract record ConfirmOutcome<T> { }

/// <summary>
/// 用户确认，且载荷已完整。载荷是确认那一刻的不可变快照，之后对话框销毁、VM 通道复位都不影响它。
///
/// 为什么不在这里校验载荷非空：载荷「必填项是否齐全」由设置类型自身的单一校验谓词保证
/// （计划 §4.1 四法则之四），结果层再校验一次只是把同一语义写出第二份副本，两处迟早不一致。
/// </summary>
/// <typeparam name="T">确认后交给执行器的载荷类型（如 <c>CompressSettings</c> / <c>ExtractSettings</c>）。</typeparam>
/// <param name="Value">确认时生成的载荷快照。</param>
public sealed record Confirmed<T>(T Value) : ConfirmOutcome<T>;

/// <summary>
/// 用户拒绝（校验不通过 / 明确不同意继续），必须携带非空原因。
///
/// 为什么强制原因：拒绝时窗口必须保持打开并把原因显示给用户（计划 §6.3 规则 20 草案），
/// 没有原因的拒绝在界面上等价于「点了没反应」。这里用 <see cref="ArgumentException"/> 加
/// Debug 期 <see cref="Debug.Fail(string)"/> 把这种静默失败变成开发期硬错误，而不是留给用户去猜。
/// </summary>
/// <typeparam name="T">被拒绝的载荷类型（保持与确认结果同签名，便于同一方法签名返回三种状态）。</typeparam>
public sealed record Rejected<T> : ConfirmOutcome<T>
{
    /// <summary>拒绝原因（面向用户的可读文案，调用方直接展示）。构造时保证非空白。</summary>
    /// <remarks>
    /// 用「非空字符串」而不是可空类型：调用方处最常见的写法是 <c>ShowMessage(r.Reason)</c>，
    /// 可空会把这里已经校验过的保证在类型系统里丢掉一半，逼每个调用点写 <c>?? ...</c> 或 <c>!</c>。
    /// </remarks>
    public string Reason { get; }

    /// <summary>创建拒绝结果。</summary>
    /// <param name="reason">面向用户的拒绝原因。</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="reason"/> 为 <see langword="null"/> 或空白时抛出；Debug 构建下另外触发一次
    /// <see cref="Debug.Fail(string)"/>，让「拒绝却不给原因」在开发期立刻暴露。
    /// </exception>
    public Rejected(string reason)
    {
        Reason = OutcomeReasonGuard.Require(reason, "Rejected");
    }
}

/// <summary>
/// 用户主动取消（关窗 / Esc / 「取消」按钮）：不是错误，不需要原因。
/// 为什么单独成一种状态而不是用 <see langword="null"/>：取消是宿主必须处理的正常终态
/// （不弹错误、不写历史、进度窗口直接收尾），而 <see langword="null"/> 在链式调用里极易被
/// <c>??</c>、<c>!.</c> 静默吞掉；独立类型让调用方漏写分支时编译期就有提示。
/// </summary>
/// <typeparam name="T">被取消的载荷类型（与 <see cref="Confirmed{T}"/> 保持一致）。</typeparam>
public sealed record Cancelled<T> : ConfirmOutcome<T>;

/// <summary>
/// 「拒绝 / 失败必须给出原因」的共用守卫。
///
/// 为什么集中在内部类型里：<see cref="Rejected{T}"/>、<c>CompressOutcome.Failed</c>、
/// <c>ExtractOutcome.NotStarted</c> 等都要做同一件事——非空校验加 Debug 期断言；
/// 写三份就迟早有一份被改漏。
/// 为什么抛 <see cref="ArgumentException"/> 而不是 InvalidOperationException：
/// 这是调用方传参错误，应在开发期立刻炸，而不是被 <c>catch (Exception)</c> 当成
/// 「运行失败」展示给用户。
/// </summary>
internal static class OutcomeReasonGuard
{
    /// <summary>校验原因非空并原样返回。</summary>
    /// <param name="reason">调用方给出的面向用户的原因文案。</param>
    /// <param name="member">出错成员名（只用于异常与断言信息，便于定位调用点）。</param>
    /// <returns>校验通过的 <paramref name="reason"/>。</returns>
    /// <exception cref="ArgumentException">原因被省略或只有空白字符。</exception>
    internal static string Require(string reason, string member)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            var message = $"{member} 必须携带非空 reason：没有原因的拒绝/失败在界面上等价于「点了没反应」，调用方也无从向用户交代。";
            Debug.Fail(message);
            throw new ArgumentException(message, nameof(reason));
        }

        return reason;
    }
}
