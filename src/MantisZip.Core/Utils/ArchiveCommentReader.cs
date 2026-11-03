using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 压缩包注释读取工具（跨引擎统一入口）。
/// <para>
/// - ZIP：读取 EOCD 注释字段（<see cref="ZipCommentHelper"/>）
/// - RAR（RAR5）：通过 7z.dll 读取归档级注释（kpidComment）
/// - 其他格式：不支持，返回 null
/// </para>
/// <para>
/// 注意：RAR4（旧格式）注释 7z.dll 不解析（CMT 块被跳过），本工具同样返回 null；
/// 7z.dll 对 RAR 只读不写，因此注释仅支持读取，不支持编辑。
/// </para>
/// </summary>
public static class ArchiveCommentReader
{
    /// <summary>
    /// 读取压缩包注释。无注释、格式不支持或读取失败时返回 null。
    /// <paramref name="password"/> 用于加密归档（如加密文件名的 RAR5）的注释读取。
    /// </summary>
    public static string? ReadComment(string archivePath, ArchiveFormat format, string? password = null)
    {
        try
        {
            // 自解压 exe 按扩展名会被判成 Zip，内嵌 RAR 的注释得走 7z.dll 才读得到（issue #36）
            if (SfxProbe.TryGetPayloadFormat(archivePath) is { } sfxFormat)
                format = sfxFormat;

            return format switch
            {
                ArchiveFormat.Zip => ReadZipComment(archivePath),
                ArchiveFormat.Rar => ReadRarComment(archivePath, password),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            CoreLog.Trace("ArchiveCommentReader.ReadComment: failed to read comment for {0}: {1}",
                archivePath, ex.Message);
            return null;
        }
    }

    private static string? ReadZipComment(string archivePath)
    {
        var comment = ZipCommentHelper.ReadComment(archivePath);
        return string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    private static string? ReadRarComment(string archivePath, string? password)
    {
        // 工厂内部会配 7z.dll 路径（issue #52），并对自解压 exe 显式指定内嵌格式（#36）
        using var extractor = SevenZipExtractorFactory.Create(archivePath, password);

        foreach (var prop in extractor.ArchiveProperties)
        {
            if (!string.Equals(prop.Name, "Comment", StringComparison.OrdinalIgnoreCase)) continue;
            if (prop.Value is string comment && !string.IsNullOrWhiteSpace(comment))
                return comment.Trim();
        }

        return null;
    }
}
