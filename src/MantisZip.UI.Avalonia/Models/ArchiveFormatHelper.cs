using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 格式检测辅助方法。
/// </summary>
public static class ArchiveFormatHelper
{
    /// <summary>
    /// 根据扩展名判断是否为支持的压缩包格式。
    /// </summary>
    public static bool IsArchiveFile(string path) =>
        ArchiveEngineFactory.GetEngineByExtension(path) != null;

    /// <summary>
    /// 获取压缩包格式。
    /// </summary>
    public static ArchiveFormat GetFormat(string path) =>
        ArchiveEngineFactory.GetFormatByExtension(path);

    /// <summary>
    /// 「打开压缩包」对话框的筛选模式：内置扩展名 + 分卷首卷（<c>*.001</c>）+ 用户在
    /// 设置 →「文件类型关联」里自定义的扩展名。
    /// </summary>
    /// <param name="customExtensions">
    /// 自定义扩展名（来自 <c>AppSettings.CustomAssocExtensions</c>，形如 <c>.xyz</c>）；
    /// 大小写、前导点由本方法归一化，非法项忽略。
    /// </param>
    public static IReadOnlyList<string> BuildOpenDialogPatterns(IEnumerable<string>? customExtensions = null)
    {
        var patterns = new List<string>(ArchiveEngineFactory.SupportedExtensions.Select(ext => "*" + ext));
        patterns.AddRange(ArchiveVolumeSet.VolumeFilterPatterns);

        if (customExtensions is null)
            return patterns;

        foreach (var raw in customExtensions)
        {
            if (NormalizeCustomExtension(raw) is not { } ext)
                continue;

            var pattern = "*" + ext;
            if (!patterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                patterns.Add(pattern);
        }

        return patterns;
    }

    /// <summary>
    /// 自定义扩展名归一化：去空白、转小写、补前导点；空值 / 只有点号 / 含通配符或路径分隔符 → null。
    /// </summary>
    internal static string? NormalizeCustomExtension(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var ext = raw.Trim().ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;

        if (ext.Length <= 1)
            return null;
        if (ext.IndexOfAny(['*', '?', '\\', '/']) >= 0)
            return null;
        // ".." / "..." 这类只剩点号的输入不能变成 "*.." 这种无意义模式。
        if (!ext.Any(char.IsLetterOrDigit))
            return null;

        return ext;
    }
}
