using SharpCompress.Archives;

namespace MantisZip.Core.Utils;

/// <summary>
/// 压缩包路径一站式处理工具。
/// 统一处理 \ → / 转换、尾部斜杠清理、以及基于归一化路径的条目查找。
/// </summary>
public static class ArchivePath
{
    /// <summary>
    /// 将压缩包路径中的反斜杠统一替换为正斜杠。
    /// null 安全：输入 null 返回空字符串。
    /// </summary>
    public static string Normalize(string? path)
        => (path ?? "").Replace('\\', '/');

    /// <summary>
    /// 去除路径尾部的目录分隔符（保留根路径如 "C:\" 的原状）。
    /// </summary>
    public static string TrimEndSeparator(string path)
    {
        while (path.Length > 3 && (path[^1] == '\\' || path[^1] == '/'))
            path = path[..^1];
        return path;
    }

    /// <summary>
    /// 获取路径中的文件名部分（自动处理尾部斜杠）。
    /// </summary>
    public static string GetFileName(string path)
        => Path.GetFileName(TrimEndSeparator(path));

    /// <summary>
    /// 获取路径中的父目录部分（自动处理尾部斜杠）。
    /// </summary>
    public static string GetDirectoryName(string path)
        => Path.GetDirectoryName(TrimEndSeparator(path)) ?? ".";

    /// <summary>
    /// 获取路径中的文件名（不含扩展名，自动处理尾部斜杠）。
    /// 已知双扩展名（.tar.gz）优先匹配，与 <see cref="ArchiveEngine.GetFormatByExtension"/> 保持一致。
    /// </summary>
    public static string GetFileNameWithoutExtension(string path)
    {
        var trimmed = TrimEndSeparator(path);
        if (trimmed.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(trimmed[..^7]);
        return Path.GetFileNameWithoutExtension(trimmed);
    }

    /// <summary>
    /// 在压缩包条目集合中查找指定名称的条目（路径分隔符不敏感）。
    /// </summary>
    public static IArchiveEntry? FindEntry(this IEnumerable<IArchiveEntry> entries, string entryName)
        => entries.FirstOrDefault(e => Normalize(e.Key) == entryName);
}
