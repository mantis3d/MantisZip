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

            default:
                return (LocalizationManager.T("Error_Whole_FallbackFmt", ex.Message), ex.Message);
        }
    }
}
