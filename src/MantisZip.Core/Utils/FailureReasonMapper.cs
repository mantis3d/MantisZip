using System.IO;
using MantisZip.Core.Abstractions;
using SharpCompress.Common;

namespace MantisZip.Core.Utils;

/// <summary>
/// 异常 → <see cref="EntryFailureReason"/> 分类器。
/// 三引擎失败/跳过埋点与 ReadErrorHandler 预检共用，保证同一异常类型映射一致；
/// 分类只出 4 类（不透传 ex.Message 到摘要正文，spec D2）；异常原文另经
/// <see cref="Abstractions.ArchiveProgress.EntryErrorDetail"/> 透传，仅供 UI tooltip
/// （error-info-enhancement 计划修订 D2）。
/// </summary>
public static class FailureReasonMapper
{
    /// <summary>按异常运行时类型分类；不在已知类别内一律归 Other。</summary>
    public static EntryFailureReason FromException(Exception ex) => ex switch
    {
        UnauthorizedAccessException => EntryFailureReason.PermissionDenied,
        IOException => EntryFailureReason.IoFailed,
        // 官方 CheckCrc 路径在条目 CRC 不匹配时抛 InvalidFormatException（消息含 "CRC mismatch"）。
        // 仅 CRC 类归 CrcMismatch，其它格式异常仍归 Other。
        InvalidFormatException ifx when ifx.Message.Contains("CRC", StringComparison.OrdinalIgnoreCase)
            => EntryFailureReason.CrcMismatch,
        _ => EntryFailureReason.Other,
    };
}
