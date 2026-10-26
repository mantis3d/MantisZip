using System.Threading;
using System.Threading.Tasks;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 压缩执行会话：接收一份「用户确认过的不可变输入」，把它跑完，并只用一个
/// <see cref="CompressOutcome"/> 回报结局。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要有这一层：</b>现状的压缩执行散在 <c>MainWindowViewModel</c> 的
/// <c>ExecuteCompressFromSettings</c> 与 <c>App.CliCompress</c> 的 CLI 路径里——两处几乎逐行相同，
/// 却各自持有自己的 ViewModel 与服务，任何一次行为修正都必须改两份，且很容易只改到一份
/// （CLI 与 GUI 的实际行为不一致就是这么来的）。会话把「压缩业务执行」这段收敛成唯一实现：
/// 宿主只负责「怎么拿到输入」与「怎么展示结局」，不再复述执行细节。
/// </para>
/// <para>
/// <b>为什么宿主环境走参数而不是字段：</b><see cref="CompressRunContext"/> 携带的是属主窗口、
/// 进度展示器与退出策略这类宿主编排。它们是「谁来跑」的问题，与「压什么」无关；把两者拆开后，
/// 同一份 <see cref="CompressSettings"/> 可以在 GUI（有属主、有进度窗、退出策略为 None）与
/// CLI（可能无属主、运行完要退出）下复用，也让执行器本身不依赖任何具体窗口类型。
/// </para>
/// <para>
/// <b>为什么返回值是一个载荷而不是 bool：</b>四个结局（跑完 / 取消 / 前置不成立 / 失败）
/// 各自对应完全不同的宿主反应——跑完要写历史、取消必须静默、前置不成立要提示原因、失败不能
/// 重复弹窗（异常已由展示器呈现过）。用一个只能表达「成/败」的 bool，调用方就只能猜，
/// 猜错就会出现「用户取消却被弹一个失败框」这类现状缺陷。
/// </para>
/// <para>
/// <b>边界：</b>本接口不暴露任何窗口类型、不接收 ViewModel、也不提供「执行完请写回这里」的回传槽。
/// 执行期需要展示的一切都经 <see cref="CompressRunContext.Presenter"/> 下达，需要收尾的宿主动作
/// 由 <see cref="CompressRunContext.ExitPolicy"/> 声明。
/// </para>
/// </remarks>
public interface ICompressSession
{
    /// <summary>
    /// 按已确认的设置执行一次压缩，并把结局表达成 <see cref="CompressOutcome"/>。
    /// </summary>
    /// <param name="settings">用户在确认那一刻冻结的不可变输入；本方法只读不写。</param>
    /// <param name="context">宿主环境（属主窗口、进度展示器、退出策略）。</param>
    /// <param name="cancellationToken">
    /// 调用方额外的取消信号；与进度窗口自身的取消令牌合并使用（任一触发即取消）。
    /// </param>
    /// <returns>
    /// <see cref="CompressOutcome.Completed"/>（正常跑完）或
    /// <see cref="CompressOutcome.Cancelled"/>（用户取消 / 进度窗口被关）或
    /// <see cref="CompressOutcome.NotStarted(string)"/>（前置不成立，未进入压缩）或
    /// <see cref="CompressOutcome.Failed(string)"/>（执行异常，结论仅供日志/诊断，不再重复展示）。
    /// </returns>
    Task<CompressOutcome> RunAsync(
        CompressSettings settings,
        CompressRunContext context,
        CancellationToken cancellationToken = default);
}
