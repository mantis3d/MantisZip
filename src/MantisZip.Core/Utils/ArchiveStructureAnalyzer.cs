using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 分析压缩包结构，判断是否具有单一根目录（用于"智能解压到此处"功能）。
/// </summary>
public static class ArchiveStructureAnalyzer
{
    /// <summary>
    /// 判断压缩包中的条目是否全部位于同一根目录下。
    /// </summary>
    /// <param name="items">压缩包条目列表。</param>
    /// <returns>
    /// <c>true</c> 如果所有文件共享一个根目录（或压缩包为空/仅有目录）；
    /// <c>false</c> 如果存在根目录下的文件或存在多个不同根目录。
    /// </returns>
    public static bool HasSingleRootDirectory(IReadOnlyList<ArchiveItem> items)
    {
        // 空压缩包 → true
        if (items == null || items.Count == 0)
        {
            CoreLog.Info($"ArchiveStructureAnalyzer: empty archive -> singleRoot=true");
            return true;
        }

        string? singleRoot = null;
        bool hasFileEntries = false;

        foreach (var item in items)
        {
            // 跳过目录条目，仅使用文件分析结构
            if (item.IsDirectory)
                continue;

            hasFileEntries = true;

            // 处理 FullPath，统一为正斜杠
            var fullPath = ArchivePath.Normalize(item.FullPath);
            var firstSlash = fullPath.IndexOf('/');
            var root = firstSlash >= 0 ? fullPath[..firstSlash] : string.Empty;

            // 文件在压缩包根目录 → 分散结构，非单一根目录
            if (root.Length == 0)
            {
                CoreLog.Info($"ArchiveStructureAnalyzer: file at root '{fullPath}' -> singleRoot=false (dispersed)");
                return false;
            }

            if (singleRoot == null)
            {
                singleRoot = root;
            }
            else if (!string.Equals(singleRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                CoreLog.Info($"ArchiveStructureAnalyzer: multiple roots '{singleRoot}' vs '{root}' -> singleRoot=false");
                return false;
            }
        }

        // 只有目录条目（无文件）→ 视为单一根目录
        if (!hasFileEntries)
        {
            CoreLog.Info($"ArchiveStructureAnalyzer: no file entries -> singleRoot=true");
            return true;
        }

        var result = singleRoot != null;
        CoreLog.Info($"ArchiveStructureAnalyzer: root='{singleRoot}', totalItems={items.Count} -> singleRoot={result}");
        return result;
    }

    /// <summary>
    /// 判断压缩包里的条目是否**真的能剥掉一层**：存在可命名的唯一根。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="HasSingleRootDirectory"/> 的差别只在两个边界：空包、以及「只有目录条目」
    /// 的包 —— 那两种情况下 <see cref="HasSingleRootDirectory"/> 回答 <see langword="true"/>
    /// （结构上确实没有散落文件），但 <see cref="GetSingleRootDirectory"/> 取不到名字，
    /// 剥根无从执行。UI 用它决定「智能去根」档位要不要出现，否则会出现「窗里能选、
    /// 跑起来什么都不做」的死选项；执行侧也用它，避免「剥了但没有名字」的半生效状态。
    /// <para>
    /// 实现**故意直接问 <see cref="GetSingleRootDirectory"/>**：剥根的前提是拿到那一段名字，
    /// 「能不能剥」和「剥哪一段」本来就是同一个判定的两种说法，分两处写必然漂移。
    /// </para>
    /// </remarks>
    public static bool HasStrippableRoot(IReadOnlyList<ArchiveItem>? items)
        => items is { Count: > 0 } && GetSingleRootDirectory(items) != null;

    /// <summary>
    /// 取压缩包的唯一根目录名（如 <c>7zip/</c> 里的 <c>7zip</c>）。
    /// </summary>
    /// <param name="items">压缩包条目列表。</param>
    /// <returns>
    /// 所有文件条目共享的首段目录名；散装（根目录下有文件）或多根时返回 <see langword="null"/>。
    /// 空包 / 只有目录条目时同样返回 <see langword="null"/>（没有可剥的根）。
    /// </returns>
    /// <remarks>
    /// 与 <see cref="HasSingleRootDirectory"/> 的区别：那个回答「能不能剥」，这个回答「剥哪一段」。
    /// 剥根需要具体的段名，光有布尔值没法执行 —— 两者共用同一套判定，避免两处各判一次而漂移。
    /// </remarks>
    public static string? GetSingleRootDirectory(IReadOnlyList<ArchiveItem> items)
    {
        if (items == null || items.Count == 0)
            return null;

        string? singleRoot = null;
        bool hasFileEntries = false;

        foreach (var item in items)
        {
            if (item.IsDirectory)
                continue;

            hasFileEntries = true;

            var fullPath = ArchivePath.Normalize(item.FullPath);
            var firstSlash = fullPath.IndexOf('/');
            var root = firstSlash >= 0 ? fullPath[..firstSlash] : string.Empty;

            if (root.Length == 0)
                return null;

            if (singleRoot == null)
            {
                singleRoot = root;
            }
            else if (!string.Equals(singleRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return hasFileEntries ? singleRoot : null;
    }
}
