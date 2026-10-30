using System.Text.RegularExpressions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 数字分卷（<c>.001</c> … <c>.999</c>）的命名识别与兄弟卷枚举 —— 分卷相关逻辑的**单一事实源**。
/// </summary>
/// <remarks>
/// <para>
/// 7z / zip / rar 的分卷集只有**第一卷**带完整头部与卷间链接信息，任何解压器都必须从 <c>.001</c> 打开
/// （issue #70①：<c>.zip.001</c> 此前被魔数路由到 SharpCompress，后者不支持多卷）。
/// </para>
/// <para>
/// 同一套命名规则还要供三处使用，故集中在此：
/// ① <see cref="ArchiveHeaderProbe"/> 估算「分卷集有效长度」与跨卷读取头部；
/// ② 解压后「删除源压缩包」要整组送回收站（issue #70②，此前只删第一卷）；
/// ③ 文件选择器的过滤模式要含 <c>*.001</c>（issue #70③）。
/// </para>
/// </remarks>
public static class ArchiveVolumeSet
{
    /// <summary>分卷后缀：三位数字扩展名（<c>archive.7z.001</c>）。</summary>
    private static readonly Regex VolumeSuffixRegex = new(@"\.(\d{3})$", RegexOptions.Compiled);

    /// <summary>
    /// 文件选择器用的分卷过滤模式。分卷只认第一卷，故只列 <c>*.001</c>；中间卷会由
    /// <see cref="FindFirstVolume"/> 回退到第一卷。
    /// </summary>
    public static readonly string[] VolumeFilterPatterns = ["*.001"];

    /// <summary>
    /// 拆分「基名 + 卷号」：<c>D:\a\pack.zip.003</c> → <c>D:\a\pack.zip</c> 与 <c>3</c>。
    /// </summary>
    public static bool TryGetVolumeBase(string path, out string basePath, out int index)
    {
        basePath = path;
        index = 0;
        if (string.IsNullOrEmpty(path))
            return false;

        var match = VolumeSuffixRegex.Match(path);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups[1].Value, out var parsed) || parsed < 1)
            return false;

        basePath = path[..^match.Length];
        index = parsed;
        return true;
    }

    /// <summary>路径是否带分卷后缀（<c>.001</c> … <c>.999</c>）。</summary>
    public static bool HasVolumeSuffix(string path) => TryGetVolumeBase(path, out _, out _);

    /// <summary>
    /// 枚举分卷集的全部卷：从 <c>.001</c> 起按连续编号，遇到缺号即停。
    /// 非分卷命名则返回「文件自身」（存在时），否则空集。
    /// </summary>
    public static IReadOnlyList<string> EnumerateVolumes(string path)
    {
        if (string.IsNullOrEmpty(path))
            return [];

        if (!TryGetVolumeBase(path, out var basePath, out _))
            return File.Exists(path) ? [path] : [];

        var volumes = new List<string>();
        for (var i = 1; i <= 999; i++)
        {
            var part = $"{basePath}.{i:D3}";
            if (!File.Exists(part))
                break;
            volumes.Add(part);
        }
        return volumes;
    }

    /// <summary>
    /// 分卷集的锚点：传入任意一卷都回到 <c>.001</c>；不是分卷命名则原样返回（文件不存在时为 null）。
    /// </summary>
    public static string? FindFirstVolume(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (!TryGetVolumeBase(path, out var basePath, out var index))
            return File.Exists(path) ? path : null;

        if (index == 1)
            return File.Exists(path) ? path : null;

        var first = basePath + ".001";
        return File.Exists(first) ? first : null;
    }
}
