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
    /// 判断压缩包条目是否命中目标路径：条目本身，或位于该目录之下的任意后代。
    /// <para>
    /// 删除语义单一事实来源：zip 没有真目录，只有条目，所以「删掉一个目录」只能落地成
    /// 「删掉该目录条目本身 + 其下所有条目」。列表里的目录行 FullPath 不带尾斜杠（<c>sub</c>），
    /// 而包内目录条目带尾斜杠（<c>sub/</c>），两者与 <c>sub/a.txt</c> 都必须命中同一目标。
    /// </para>
    /// <para>
    /// 目标去尾斜杠**故意不复用 <see cref="TrimEndSeparator"/>**：后者带 <c>path.Length &gt; 3</c>
    /// 守卫（为保护 <c>C:\</c> 这类根路径），会让 <c>"a/"</c>、<c>"ab/"</c> 原样保留，
    /// 导致短目录名删除失效。此处无条件剥掉尾部所有分隔符。
    /// </para>
    /// </summary>
    /// <param name="entryName">压缩包内条目名（如 <c>sub/</c>、<c>sub/a.txt</c>）</param>
    /// <param name="target">要删除/查找的目标路径（可带或不带尾斜杠）</param>
    /// <returns>命中返回 true；目标为空或只剩分隔符时返回 false（防止误删整包）</returns>
    public static bool MatchesEntryOrDescendant(string entryName, string target)
    {
        var entry = Normalize(entryName);
        var normalizedTarget = Normalize(target);

        while (normalizedTarget.EndsWith('/'))
            normalizedTarget = normalizedTarget[..^1];

        if (normalizedTarget.Length == 0)
            return false;

        if (entry.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            return true;

        // "sub/"（目录条目本身）与 "sub/a.txt"（后代）都满足：目标之后紧跟分隔符
        return entry.Length > normalizedTarget.Length
            && entry[normalizedTarget.Length] == '/'
            && entry.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 在压缩包条目集合中查找指定名称的条目（路径分隔符不敏感）。
    /// </summary>
    public static IArchiveEntry? FindEntry(this IEnumerable<IArchiveEntry> entries, string entryName)
        => entries.FirstOrDefault(e => Normalize(e.Key) == entryName);
}
