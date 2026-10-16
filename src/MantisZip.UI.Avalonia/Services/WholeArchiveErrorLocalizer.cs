using SharpCompress.Common;
using SharpSevenZip.Exceptions;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 整包级失败的错误原文 → 本地化文案映射，消除「本地化外壳 + 英文内核」的中英混杂。
/// <para>
/// 逐条目失败走 <see cref="ViewModels.ProgressViewModel"/> 的 4 类原因通道（红/黄摘要），
/// 不在此处处理；本类只负责**整包异常**（打开/压缩/解压整体失败）。
/// </para>
/// </summary>
public static class WholeArchiveErrorLocalizer
{
    /// <summary>
    /// 把整包异常映射为本地化文案 + 原文详情。
    /// </summary>
    /// <returns>
    /// <c>Message</c>：本地化后的用户可见文案；<c>Detail</c>：异常原文（供 tooltip 呈现，可能为 null）。
    /// </returns>
    public static (string Message, string? Detail) ToUserText(Exception ex)
    {
        var (message, detail) = Map(ex);

        // IO 类错误附加可操作提示（磁盘满 / 文件被占用 / 路径过长）
        var hint = IoErrorHintResolver.TryGetHint(ex);
        if (hint != null)
            detail = string.IsNullOrEmpty(detail) ? hint : detail + "\n" + hint;

        return (message, detail);
    }

    private static (string Message, string? Detail) Map(Exception ex)
    {
        switch (ex)
        {
            // FileNotFoundException.FileName 缺失时（少见）落到 default 兜底
            case FileNotFoundException fnf when !string.IsNullOrEmpty(fnf.FileName):
                return (LocalizationManager.T("Error_Whole_FileNotFound", fnf.FileName), fnf.Message);

            case DirectoryNotFoundException dnf:
                return (LocalizationManager.T("Error_Whole_DirectoryNotFound", dnf.Message), dnf.Message);

            case UnauthorizedAccessException uae:
                return (LocalizationManager.T("Error_Whole_UnauthorizedAccess", uae.Message), uae.Message);

            // thrower 侧已本地化（如 Status_ArchiveCorrupted），透传原文避免双重本地化
            case InvalidDataException:
                return (ex.Message, null);

            // SharpCompress 损坏/格式异常（如 "Failed to locate the Zip Header"、
            // "Unexpected EOF reading tar file"、"Not a tar file."）→ 本地化「文件损坏」。
            // 派生类型置于基类 ArchiveException 之前（避免 switch 不可达）。
            case InvalidFormatException:
            case IncompleteArchiveException:
            case ArchiveException:
                return (LocalizationManager.T("Error_Whole_Corrupted"), ex.Message);

            // SharpSevenZip 解压/校验失败（"File is corrupted. Data error has occured."）→ 明确数据损坏
            case ExtractionFailedException:
                return (LocalizationManager.T("Error_Whole_Corrupted"), ex.Message);

            // SharpSevenZipArchiveException：加密头无密码 与 头部损坏 抛同一异常，措辞保留歧义
            case SharpSevenZipArchiveException:
                return (LocalizationManager.T("Error_Whole_CorruptedOrPassword"), ex.Message);

            default:
                return (LocalizationManager.T("Error_Whole_FallbackFmt", ex.Message), ex.Message);
        }
    }
}
