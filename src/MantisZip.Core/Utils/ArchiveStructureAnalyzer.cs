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
            var fullPath = item.FullPath?.Replace('\\', '/') ?? string.Empty;
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
}
