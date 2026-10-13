using System.IO;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 异常 → <see cref="EntryFailureReason"/> 分类器。
/// 三引擎失败/跳过埋点与 ReadErrorHandler 预检共用，保证同一异常类型映射一致；
/// 不透传 ex.Message（spec D2）。
/// </summary>
public static class FailureReasonMapper
{
    /// <summary>按异常运行时类型分类；不在已知两类内一律归 Other。</summary>
    public static EntryFailureReason FromException(Exception ex) => ex switch
    {
        UnauthorizedAccessException => EntryFailureReason.PermissionDenied,
        IOException => EntryFailureReason.IoFailed,
        _ => EntryFailureReason.Other,
    };
}
