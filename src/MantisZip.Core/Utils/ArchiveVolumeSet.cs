using System.Text.RegularExpressions;

namespace MantisZip.Core.Utils;

/// <summary>分卷命名体系：同一份归档被拆成多卷时采用的卷名规则。</summary>
public enum VolumeSetKind
{
    /// <summary>不是分卷命名。</summary>
    None = 0,

    /// <summary>三位数字扩展名：<c>archive.7z.001</c> … <c>.999</c>（7-Zip / 旧式分卷）。</summary>
    Numeric,

    /// <summary>WinRAR 新式：<c>archive.part1.rar</c> … <c>archive.partN.rar</c>（RAR5 默认）。</summary>
    RarPart,

    /// <summary>WinRAR 旧式：首卷 <c>archive.rar</c>，其后 <c>archive.r00</c> … <c>.r99</c>（RAR4 时代）。</summary>
    RarOld,
}

/// <summary>
/// 分卷压缩包的命名识别与兄弟卷枚举 —— 分卷相关逻辑的**单一事实源**。
/// </summary>
/// <remarks>
/// <para>
/// 7z / zip / rar 的分卷集只有**第一卷**带完整头部与卷间链接信息，任何解压器都必须从第一卷打开
/// （issue #70①：<c>.zip.001</c> 此前被魔数路由到 SharpCompress，后者不支持多卷）。
/// </para>
/// <para>
/// 同一套命名规则还要供四处使用，故集中在此：
/// ① <see cref="ArchiveHeaderProbe"/> 估算「分卷集有效长度」与跨卷读取头部；
/// ② 解压后「删除源压缩包」要整组送回收站（issue #70②，此前只删第一卷）；
/// ③ 文件选择器的过滤模式要含 <c>*.001</c>，且中间卷要能认出基名归档（issue #70③）；
/// ④ WinRAR 新式 <c>.partN.rar</c> 命名（issue #69 在 RAR 上的残留：此前完全不认，
///    从 <c>part2</c> 打开既不回溯首卷、删源包也只删一卷）。
/// </para>
/// <para>
/// 识别的三种命名见 <see cref="VolumeSetKind"/>；<see cref="TryGetVolumeBase"/> 统一返回
/// 「归档基名路径」（<c>pack.part2.rar</c> → <c>pack.rar</c>、<c>pack.7z.003</c> → <c>pack.7z</c>），
/// 调用方据此判断归档格式、决定锚点与过滤条件。
/// </para>
/// </remarks>
public static class ArchiveVolumeSet
{
    /// <summary>分卷后缀：三位数字扩展名（<c>archive.7z.001</c>）。</summary>
    private static readonly Regex NumericSuffixRegex = new(@"\.(\d{3})$", RegexOptions.Compiled);

    /// <summary>WinRAR 新式卷名：<c>archive.part12.rar</c>（卷号可能是 1 位或补零的多位）。</summary>
    private static readonly Regex RarPartRegex = new(
        @"^(?<root>.+)\.part(?<num>\d+)\.rar$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>WinRAR 旧式卷名：<c>archive.r00</c> … <c>archive.r99</c>（首卷是 <c>archive.rar</c>）。</summary>
    private static readonly Regex RarOldRegex = new(
        @"^(?<root>.+)\.r(?<num>\d{2})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>卷号上限（三位数字命名最多 999 卷；够任何实际分卷集使用）。</summary>
    private const int MaxVolumeCount = 999;

    /// <summary>
    /// 文件选择器用的分卷过滤模式。分卷只认第一卷，故只列 <c>*.001</c>；中间卷会由
    /// <see cref="FindFirstVolume"/> 回退到第一卷，显示则由 <see cref="TryGetVolumeBase"/> 兜底。
    /// </summary>
    public static readonly string[] VolumeFilterPatterns = ["*.001"];

    /// <summary>路径属于哪种分卷命名（<see cref="VolumeSetKind.None"/> 表示不是分卷）。</summary>
    public static VolumeSetKind GetVolumeSetKind(string path)
    {
        if (string.IsNullOrEmpty(path))
            return VolumeSetKind.None;

        if (NumericSuffixRegex.IsMatch(path))
            return VolumeSetKind.Numeric;

        var name = Path.GetFileName(path);
        if (RarPartRegex.IsMatch(name))
            return VolumeSetKind.RarPart;
        if (RarOldRegex.IsMatch(name))
            return VolumeSetKind.RarOld;

        return VolumeSetKind.None;
    }

    /// <summary>
    /// 拆分「归档基名 + 卷号」：<c>D:\a\pack.7z.003</c> → <c>D:\a\pack.7z</c> 与 <c>3</c>；
    /// <c>D:\a\pack.part2.rar</c> → <c>D:\a\pack.rar</c> 与 <c>2</c>；
    /// <c>D:\a\pack.r00</c> → <c>D:\a\pack.rar</c> 与 <c>2</c>。
    /// </summary>
    public static bool TryGetVolumeBase(string path, out string basePath, out int index)
    {
        basePath = path;
        index = 0;
        if (string.IsNullOrEmpty(path))
            return false;

        var match = NumericSuffixRegex.Match(path);
        if (match.Success)
        {
            if (!int.TryParse(match.Groups[1].Value, out var parsed) || parsed < 1)
                return false;

            basePath = path[..^match.Length];
            index = parsed;
            return true;
        }

        var name = Path.GetFileName(path);
        var dir = Path.GetDirectoryName(path) ?? string.Empty;

        var part = RarPartRegex.Match(name);
        if (part.Success)
        {
            if (!int.TryParse(part.Groups["num"].Value, out var partIndex) || partIndex < 1)
                return false;

            basePath = Path.Combine(dir, part.Groups["root"].Value + ".rar");
            index = partIndex;
            return true;
        }

        var old = RarOldRegex.Match(name);
        if (old.Success)
        {
            if (!int.TryParse(old.Groups["num"].Value, out var oldIndex))
                return false;

            // .r00 = 第 2 卷，.r01 = 第 3 卷（首卷是不带后缀的 archive.rar）
            basePath = Path.Combine(dir, old.Groups["root"].Value + ".rar");
            index = oldIndex + 2;
            return true;
        }

        return false;
    }

    /// <summary>路径是否带分卷后缀（三种命名任一）。</summary>
    public static bool HasVolumeSuffix(string path) => GetVolumeSetKind(path) != VolumeSetKind.None;

    /// <summary>
    /// 枚举分卷集的全部卷（按卷号顺序）：从第 1 卷起按连续编号，遇到缺号即停。
    /// 非分卷命名则返回「文件自身」（存在时），否则空集。
    /// </summary>
    public static IReadOnlyList<string> EnumerateVolumes(string path)
    {
        if (string.IsNullOrEmpty(path))
            return [];

        switch (GetVolumeSetKind(path))
        {
            case VolumeSetKind.Numeric:
                {
                    if (!TryGetVolumeBase(path, out var basePath, out _))
                        return [];
                    return EnumerateNumeric(basePath);
                }

            case VolumeSetKind.RarPart:
                {
                    var name = Path.GetFileName(path);
                    var match = RarPartRegex.Match(name);
                    var dir = Path.GetDirectoryName(path) ?? string.Empty;
                    var root = match.Groups["root"].Value;
                    // 卷号宽度沿用传入卷（part1 → 1 位、part01 → 2 位），避免 "part01"/"part1" 混用
                    var width = match.Groups["num"].Value.Length;
                    return EnumerateRarParts(dir, root, width);
                }

            case VolumeSetKind.RarOld:
                {
                    var name = Path.GetFileName(path);
                    var match = RarOldRegex.Match(name);
                    var dir = Path.GetDirectoryName(path) ?? string.Empty;
                    return EnumerateRarOld(dir, match.Groups["root"].Value);
                }

            default:
                return File.Exists(path) ? [path] : [];
        }
    }

    /// <summary>
    /// 分卷集的锚点：传入任意一卷都回到第一卷；不是分卷命名则原样返回（文件不存在时为 null）。
    /// </summary>
    public static string? FindFirstVolume(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var volumes = EnumerateVolumes(path);
        return volumes.Count > 0 ? volumes[0] : null;
    }

    /// <summary>
    /// 分卷集的文件系统监视过滤模式（任一卷增删都要能被看到）：数字分卷 → <c>pack.7z.*</c>；
    /// WinRAR 新式 → <c>pack.part*.rar</c>；WinRAR 旧式 → <c>pack.*</c>；非分卷 → 文件名本身。
    /// </summary>
    public static string GetVolumeWatchFilter(string path)
    {
        var name = Path.GetFileName(path);
        switch (GetVolumeSetKind(path))
        {
            case VolumeSetKind.Numeric when TryGetVolumeBase(path, out var basePath, out _):
                return Path.GetFileName(basePath) + ".*";

            case VolumeSetKind.RarPart:
                return RarPartRegex.Match(name).Groups["root"].Value + ".part*.rar";

            case VolumeSetKind.RarOld:
                return RarOldRegex.Match(name).Groups["root"].Value + ".*";

            default:
                return name;
        }
    }

    /// <summary>
    /// 分卷集里的任意一卷是否属于给定归档扩展名：<c>TEST.7z.002</c> + <c>.7z</c> → true，
    /// <c>x.part2.rar</c> + <c>.rar</c> → true，<c>x.r00</c> + <c>.rar</c> → true。
    /// 供文件选择器过滤使用（issue #70③：中间卷此前被隐藏，只亮 <c>*.001</c>）。
    /// </summary>
    /// <param name="filePath">待判定的文件路径。</param>
    /// <param name="archiveExtension">归档扩展名（含点，如 <c>.7z</c> / <c>.tar.gz</c>）。</param>
    public static bool MatchesArchiveExtension(string filePath, string archiveExtension)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(archiveExtension) || archiveExtension.Length < 2)
            return false;

        if (!TryGetVolumeBase(filePath, out var basePath, out _))
            return false;

        return Path.GetFileName(basePath).EndsWith(archiveExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> EnumerateNumeric(string basePath)
    {
        var volumes = new List<string>();
        for (var i = 1; i <= MaxVolumeCount; i++)
        {
            var part = $"{basePath}.{i:D3}";
            if (!File.Exists(part))
                break;
            volumes.Add(part);
        }
        return volumes;
    }

    private static IReadOnlyList<string> EnumerateRarParts(string dir, string root, int width)
    {
        var volumes = new List<string>();
        for (var i = 1; i <= MaxVolumeCount; i++)
        {
            var candidate = Path.Combine(dir, $"{root}.part{i.ToString($"D{width}")}.rar");
            if (!File.Exists(candidate))
                break;
            volumes.Add(candidate);
        }
        return volumes;
    }

    private static IReadOnlyList<string> EnumerateRarOld(string dir, string root)
    {
        // 首卷不带数字后缀（archive.rar），其后 .r00 … .r99；首卷缺失则整组不可用
        var first = Path.Combine(dir, root + ".rar");
        if (!File.Exists(first))
            return [];

        var volumes = new List<string> { first };
        for (var i = 0; i <= 99; i++)
        {
            var candidate = Path.Combine(dir, $"{root}.r{i:D2}");
            if (!File.Exists(candidate))
                break;
            volumes.Add(candidate);
        }
        return volumes;
    }
}
