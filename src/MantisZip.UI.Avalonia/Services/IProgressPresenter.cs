using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Dialogs;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 「起进度 → 跑 → 返回是否跑完」的统一端口：进度窗口的生命周期只在这里实现一次。
///
/// 为什么需要它：主窗口的 <c>RunWithProgress</c> 与 CLI 的压缩、解压两套进度实现是三份手抄的
/// 同一段流程（创建窗口 → 播种批处理 → 建进度对象 → 跑 → 取消/异常收尾 → 等自动关闭），
/// 每修一次只修到其中一份上，于是「失败后关不关窗」「取消后弹不弹提示」「倒计时出不出来」
/// 这类问题反复在某一侧复发。把流程收进端口后，宿主差异只剩实现类自己知道。
///
/// 本契约不含压缩 / 解压语义：它同样承载解压与测试流程。不要在这里加「是否压缩」「是否 CLI」
/// 「失败怎么显示」这类成员——一旦掺进去，实现类立刻退化成 <c>if (isCli)</c> /
/// <c>if (isExtract)</c> 的老样子，收口就白做了。
///
/// 分工：
/// ① presenter 负责：窗口的创建、配置、展示、批处理行播种、终态可见性（完成 / 取消 / 错误摘要）、
///   等待自动关闭或被手动关闭，以及把「用户取消」与「异常」收成两种可判定的出口；
/// ② 调用方（会话层）负责：逐条目状态、业务结果（完成 / 失败 / 未开始）与关窗之后的动作
///   （删包、写历史、按 <see cref="MantisZip.UI.Avalonia.Models.ExitPolicy"/> 退出）；
/// ③ 运行期要写窗口的逐项状态时，通过 <see cref="ActiveWindow"/> 取窗口句柄。
/// </summary>
public interface IProgressPresenter
{
    /// <summary>
    /// 当前正在呈现的进度窗口；未开始或已收尾后为 <see langword="null"/>。
    ///
    /// 为什么暴露它：批处理的逐行状态、密码徽标等细节由调用方在执行过程中写入
    /// （现状 <c>vm.BatchStatusReporter</c> / <c>vm.LastProgressWindow</c> 就干这个）。
    /// 若把 <see cref="ProgressWindow"/> 的二十余个成员镜像进本接口，接口会立刻随窗口一起漂移，
    /// 变成第二个窗口 API。约定：实现必须在 UI 线程维护该属性；调用方若在后台线程使用窗口，
    /// 需自行经 Dispatcher 调度（与现有 CLI 实现一致）。
    /// </summary>
    ProgressWindow? ActiveWindow { get; }

    /// <summary>起进度、执行 <see cref="ProgressRunRequest.Operation"/>、等窗口收尾。</summary>
    /// <param name="request">本次运行的标题、操作、批处理行、完成回调与一次性窗口配置。</param>
    /// <returns>
    /// <see langword="true"/>：操作正常跑完（业务层面的部分条目失败由窗口的错误汇总体现，仍算跑完）；
    /// <see langword="false"/>：用户取消（含密码重试弹窗取消）或窗口已被宿主关闭。
    /// 拿到 <see langword="false"/> 的调用方必须产出「已取消」结果，且不得再弹失败提示。
    /// </returns>
    /// <remarks>
    /// 操作抛出的非取消异常由实现捕获、把错误摘要写进窗口后**原样抛出**：调用方只需把它转成
    /// 结构化结果（如 <c>MantisZip.UI.Avalonia.Models.CompressOutcome.Failed(reason)</c>），
    /// 不得重复展示同一错误。
    ///
    /// 为什么返回 <see cref="bool"/> 而不是一个 outcome 类型：presenter 只掌握「跑没跑完」这一件事实，
    /// 失败的原因与文案属于调用方业务（要本地化、要决定是否写历史），取消是正常终态——两条通道
    /// 各自只有一种含义。若把失败也压进返回值，presenter 就必须自己发明用户可见文案，
    /// 或者把 Exception 挂到某个属性上让调用方回读，那正是本次收口要消灭的「回读可变状态」。
    /// </remarks>
    Task<bool> RunAsync(ProgressRunRequest request);
}

/// <summary>
/// 一次进度呈现的请求。用 <c>required</c> 表达「标题与操作必须给全」，其余都是可选呈现细节：
/// 以后新增字段不会逼所有调用方改代码。
///
/// 为什么窗口的一次性配置收成 <see cref="ConfigureWindow"/> 而不是给每个窗口属性开一个请求字段：
/// 本请求是压缩/解压无关的通用契约，而「是否压缩流程」「解压完成后打开目录」之类是具体流程
/// 才有的配置；开成字段等于把 <see cref="ProgressWindow"/> 的属性表抄一份进契约，
/// 窗口一加属性契约就得跟着改。
/// </summary>
public sealed record ProgressRunRequest
{
    /// <summary>进度窗口标题（调用方已本地化）。</summary>
    public required string Title { get; init; }

    /// <summary>
    /// 要执行的操作。进度对象与取消令牌由 presenter 提供，操作只管往里报进度；
    /// 签名与现有 <c>Func&lt;IProgress&lt;ArchiveProgress&gt;, CancellationToken, Task&gt;</c> 一致，
    /// 现有操作闭包可原样复用。
    /// </summary>
    public required Func<IProgress<ArchiveProgress>, CancellationToken, Task> Operation { get; init; }

    /// <summary>
    /// 批处理列表的初始行（一个元素一行）；<see langword="null"/> 或空表示不进入批处理模式。
    ///
    /// 为什么在请求里：主窗口与 CLI 都要播种，而播种语义只有「一行一项」这一条；
    /// 逐行的实时状态变化仍由调用方通过 <see cref="IProgressPresenter.ActiveWindow"/> 写入。
    /// </summary>
    public IReadOnlyList<string>? DisplayPaths { get; init; }

    /// <summary>
    /// 操作说明行文案（调用方已本地化；<see langword="null"/> = 不显示该行）。
    /// 用于「整包重写、没有逐条目回调」的操作（如删除），用一行真实影响面代替假进度列表。
    /// </summary>
    public string? OperationNote { get; init; }

    /// <summary>
    /// 窗口创建后、<c>Show()</c> 之前调用的一次性配置钩子：流程标志、手动关闭后的动作
    /// （打开目录 / 删包）、条目预播种等窗口专属配置都放这里。实现必须先调用它再显示窗口，
    /// 否则用户会看到一次「没配置好」的闪烁。
    /// </summary>
    public Action<ProgressWindow>? ConfigureWindow { get; init; }

    /// <summary>
    /// 操作成功后、进入自动关闭倒计时之前的完成回调（如测试流程的成功 / 损坏文案）。
    /// 为 <see langword="null"/> 时由实现写默认完成态。
    ///
    /// 为什么用回调而不是请求字段：完成文案属于调用方业务（含本地化），presenter 不该认识它。
    /// </summary>
    public Action<ProgressWindow>? OnCompleted { get; init; }
}
