using MantisZip.Core.Abstractions;
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
            // 删除目标在包内一个都匹配不到。必须排在 FileNotFoundException 基类 case **之前**：
            // 基类分支会把所有「找不到」统一成 Error_Whole_FileNotFound，异常自带的
            // 「压缩包中不存在条目」消息根本到不了用户眼前。
            case ArchiveEntryNotFoundException aen:
                return (LocalizationManager.T("Error_Delete_EntryNotFound", aen.EntryPath), aen.Message);

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

            // SharpSevenZip 库加载失败（找不到 7z.dll，或库已被别的路径加载而无法切换）→ 明确指向
            // 7z.dll，避免把英文 "Can not load 7-zip library or internal COM error!" 直接抛给用户
            case SharpSevenZipLibraryException:
                return (LocalizationManager.T("Error_Whole_SevenZipLibrary"), ex.Message);

            // Core 对「加密包无密码」抛 InvalidOperationException（消息含「加密」/encrypted）：
            // 映射到干净本地化文案。不改 Core 消息本身——其为中英双语且被密码分类启发式
            // （PasswordService.ClassifyGenericException 查 "password"/"encrypted"）依赖。
            // 置于最后：若某引擎异常派生自 InvalidOperationException，其专属分支优先命中。
            case InvalidOperationException ioe when IsEncryptedSignal(ioe.Message):
                return (LocalizationManager.T("Error_ArchiveEncrypted"), ioe.Message);

            // Core 检测到的「已知可读条件」异常（如 Zip Slip「压缩条目包含非法路径」、
            // 「条目路径净化后为空」）：消息本身已是用户可读中文，直接透传，不加「操作失败：」前缀。
            case InvalidOperationException ioe when ContainsCjk(ioe.Message):
                return (ioe.Message, null);

            default:
                return (LocalizationManager.T("Error_Whole_FallbackFmt", ex.Message), ex.Message);
        }
    }

    /// <summary>消息是否表示「加密包需要密码」（Core 双语消息含「加密」或 "encrypted"）。</summary>
    private static bool IsEncryptedSignal(string message) =>
        message.Contains("加密", StringComparison.Ordinal)
        || message.Contains("encrypted", StringComparison.OrdinalIgnoreCase);

    /// <summary>消息是否含 CJK 表意文字（判定 Core 已给出用户可读中文文案，无需再套前缀）。</summary>
    private static bool ContainsCjk(string message)
    {
        foreach (var c in message)
        {
            if (c is >= '\u4e00' and <= '\u9fff') return true;
        }
        return false;
    }
}
