using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Services;

/// <summary>
/// 目录统计信息。
/// </summary>
public readonly record struct DirStats(int Count, long Size, long CompressedSize);

/// <summary>
/// 按文件夹路径筛选压缩包条目，处理扁平/默认两种浏览模式。
/// </summary>
public static class ArchiveEntryLister
{
    /// <summary>
    /// 获取指定文件夹下的条目列表。
    /// </summary>
    /// <param name="allItems">全部条目（引擎返回的原始列表）</param>
    /// <param name="folderPath">目标文件夹路径，空字符串表示根目录</param>
    /// <param name="showSubfolders">true=扁平模式（显示所有递归文件），false=默认模式（仅直接条目+隐式目录）</param>
    /// <returns>筛选后的条目列表（已设置 DisplayName）</returns>
    public static List<ArchiveItem> GetEntriesInFolder(
        IReadOnlyList<ArchiveItem> allItems,
        string folderPath,
        bool showSubfolders)
    {
        string prefix = string.IsNullOrEmpty(folderPath) ? "" : folderPath + "/";

        if (showSubfolders)
            return GetFlattenedEntries(allItems, folderPath, prefix);
        else
            return GetDirectEntries(allItems, folderPath, prefix);
    }

    /// <summary>
    /// 扁平模式：收集指定目录下所有递归子目录的文件（不含目录自身），
    /// DisplayName 设为相对于当前目录的路径。
    /// </summary>
    private static List<ArchiveItem> GetFlattenedEntries(
        IReadOnlyList<ArchiveItem> allItems, string folderPath, string prefix)
    {
        var directItems = new List<ArchiveItem>();

        foreach (var item in allItems)
        {
            if (item.IsDirectory) continue;
            if (!item.FullPath.StartsWith(prefix)) continue;

            if (string.IsNullOrEmpty(folderPath) || item.Name.StartsWith(prefix))
            {
                directItems.Add(item);
            }
        }

        // 设置 DisplayName 为相对路径
        foreach (var item in directItems)
        {
            item.DisplayName = string.IsNullOrEmpty(folderPath)
                ? item.Name
                : item.Name.StartsWith(prefix)
                    ? item.Name[prefix.Length..]
                    : item.Name;
        }

        return directItems;
    }

    /// <summary>
    /// 默认模式：直接条目（直接位于指定目录下的文件和目录）+ 隐式合成目录。
    /// </summary>
    private static List<ArchiveItem> GetDirectEntries(
        IReadOnlyList<ArchiveItem> allItems, string folderPath, string prefix)
    {
        var directItems = new List<ArchiveItem>();
        var implicitDirs = new HashSet<string>();

        foreach (var item in allItems)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                // 根目录：直接显示顶级条目
                if (!item.Name.Contains("/"))
                {
                    directItems.Add(item);
                    continue;
                }

                var firstSlash = item.Name.IndexOf('/');
                var dirName = item.Name[..firstSlash];

                if (!implicitDirs.Add(dirName)) continue;

                if (item.IsDirectory && item.FullPath == dirName)
                {
                    directItems.Add(item);
                    continue;
                }

                // 合成隐式目录
                directItems.Add(new ArchiveItem
                {
                    Name = dirName + "/",
                    FullPath = dirName,
                    Size = 0,
                    IsDirectory = true,
                });
            }
            else
            {
                // 子目录：只显示该目录下的第一级条目
                if (!item.Name.StartsWith(prefix)) continue;
                if (item.FullPath == folderPath) continue;

                var rest = item.Name[prefix.Length..].TrimEnd('/');
                var restParts = rest.Split('/');

                if (restParts.Length == 1)
                {
                    directItems.Add(item);
                    if (item.IsDirectory) implicitDirs.Add(restParts[0]);
                }
                else
                {
                    var subDir = restParts[0];
                    if (implicitDirs.Add(subDir))
                    {
                        var subDirFullPath = folderPath + "/" + subDir;
                        directItems.Add(new ArchiveItem
                        {
                            Name = subDirFullPath + "/",
                            FullPath = subDirFullPath,
                            Size = 0,
                            IsDirectory = true,
                        });
                    }
                }
            }
        }

        // 通用去重
        var seen = new HashSet<string>();
        var deduped = new List<ArchiveItem>();
        foreach (var item in directItems)
        {
            if (seen.Add(item.FullPath)) deduped.Add(item);
        }

        // 设置 DisplayName
        SetDisplayName(deduped, folderPath, prefix);

        return deduped;
    }

    /// <summary>
    /// 设置条目的 DisplayName（相对于当前目录的显示名称）。
    /// </summary>
    public static void SetDisplayName(List<ArchiveItem> items, string? folderPath = null, string? prefix = null)
    {
        prefix ??= string.IsNullOrEmpty(folderPath) ? "" : folderPath + "/";

        foreach (var item in items)
        {
            item.DisplayName = string.IsNullOrEmpty(folderPath)
                ? item.Name.TrimEnd('/')
                : item.Name.StartsWith(prefix)
                    ? item.Name[prefix.Length..].TrimEnd('/')
                    : item.Name;
        }
    }

    /// <summary>
    /// 预计算目录统计信息：每个目录包含的文件数、总大小、压缩后大小。
    /// </summary>
    public static Dictionary<string, DirStats> ComputeDirectoryStats(IReadOnlyList<ArchiveItem> allItems)
    {
        var stats = new Dictionary<string, DirStats>();

        foreach (var item in allItems)
        {
            if (item.IsDirectory) continue;

            var name = item.Name;
            var lastSlash = name.LastIndexOf('/');
            if (lastSlash < 0) continue; // 根目录文件

            var parts = name[..lastSlash].Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                var dirPath = string.Join("/", parts, 0, i + 1);
                var stat = stats.GetValueOrDefault(dirPath);
                stats[dirPath] = new DirStats(stat.Count + 1, stat.Size + item.Size, stat.CompressedSize + item.CompressedSize);
            }
        }

        return stats;
    }

}
