using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 添加到压缩包场景的条目名冲突处理（与解压场景共用 <see cref="FileConflictAction"/> 与
/// <see cref="ArchiveOptions.ConflictResolver"/> / <see cref="ArchiveOptions.ConflictResolverAsync"/>）。
///
/// 语义映射（与解压相反，勿直接用 <see cref="FileConflictHelper"/> 的方向比较）：
/// - 解压：Entry = 压缩包条目（新数据），Existing = 磁盘文件（旧数据）；
/// - 添加：Entry = 压缩包已有条目（旧数据），Existing = 磁盘新文件（新数据）。
/// 统一规则：新数据比旧数据更新/更大 → 覆盖（OverwriteIfOlder / OverwriteIfSmaller 比较方向与解压相反）。
/// </summary>
public static class AddConflictHelper
{
    /// <summary>
    /// 异步解析条目名冲突。返回最终条目名；null = 跳过该文件。
    /// </summary>
    /// <param name="entryName">提议的条目名（含 entryBasePath 前缀，"/" 分隔）。</param>
    /// <param name="options">压缩选项；ConflictAction 为 Ask 时优先调用 ConflictResolverAsync。</param>
    /// <param name="entryModified">压缩包内已有同名条目的修改时间；无同名条目时传 null。</param>
    /// <param name="entrySize">压缩包内已有同名条目的大小；无同名条目时传 null。</param>
    /// <param name="newFileModified">磁盘新文件的修改时间。</param>
    /// <param name="newFileSize">磁盘新文件的大小。</param>
    /// <param name="occupiedNames">已占用条目名集合（现有条目 + 本批次已解析条目），OrdinalIgnoreCase；最终名会加入此集合。</param>
    public static async Task<string?> ResolveEntryNameAsync(
        string entryName,
        ArchiveOptions? options,
        DateTime? entryModified,
        long? entrySize,
        DateTime? newFileModified,
        long? newFileSize,
        HashSet<string> occupiedNames)
    {
        // 无冲突：条目名不存在 → 直接添加（即使策略是 Skip 也不跳过，无同名条目可跳）
        if (!occupiedNames.Contains(entryName))
        {
            occupiedNames.Add(entryName);
            return entryName;
        }

        var action = options?.ConflictAction ?? FileConflictAction.Overwrite;

        // Ask → 优先异步回调（UI 对话框场景），其次退回到同步回调
        if (action == FileConflictAction.Ask && options != null)
        {
            if (options.ConflictResolverAsync != null)
            {
                var info = BuildConflictInfo(entryName, entryModified, entrySize, newFileModified, newFileSize, occupiedNames);
                action = await options.ConflictResolverAsync(info);

                if (action == FileConflictAction.Rename && !string.IsNullOrWhiteSpace(info.CustomName))
                {
                    var final = CombineCustomName(entryName, info.CustomName);
                    occupiedNames.Add(final);
                    return final;
                }
            }
            else if (options.ConflictResolver != null)
            {
                return ResolveEntryName(entryName, options, entryModified, entrySize, newFileModified, newFileSize, occupiedNames);
            }
        }

        return ResolveByAction(entryName, action, entryModified, entrySize, newFileModified, newFileSize, occupiedNames);
    }

    /// <summary>同步版，供 <see cref="ArchiveOptions.ConflictResolver"/> 回调路径使用。</summary>
    public static string? ResolveEntryName(
        string entryName,
        ArchiveOptions? options,
        DateTime? entryModified,
        long? entrySize,
        DateTime? newFileModified,
        long? newFileSize,
        HashSet<string> occupiedNames)
    {
        if (!occupiedNames.Contains(entryName))
        {
            occupiedNames.Add(entryName);
            return entryName;
        }

        var action = options?.ConflictAction ?? FileConflictAction.Overwrite;

        if (action == FileConflictAction.Ask && options?.ConflictResolver != null)
        {
            var info = BuildConflictInfo(entryName, entryModified, entrySize, newFileModified, newFileSize, occupiedNames);
            action = options.ConflictResolver(info);

            if (action == FileConflictAction.Rename && !string.IsNullOrWhiteSpace(info.CustomName))
            {
                var final = CombineCustomName(entryName, info.CustomName);
                occupiedNames.Add(final);
                return final;
            }
        }

        return ResolveByAction(entryName, action, entryModified, entrySize, newFileModified, newFileSize, occupiedNames);
    }

    private static string? ResolveByAction(
        string entryName,
        FileConflictAction action,
        DateTime? entryModified,
        long? entrySize,
        DateTime? newFileModified,
        long? newFileSize,
        HashSet<string> occupiedNames)
    {
        var resolved = action switch
        {
            FileConflictAction.Overwrite => entryName,
            FileConflictAction.Skip => null,
            FileConflictAction.Rename => GetUniqueEntryName(entryName, occupiedNames),
            FileConflictAction.OverwriteIfOlder => ShouldOverwriteByTime(entryModified, newFileModified) ? entryName : null,
            FileConflictAction.OverwriteIfSmaller => ShouldOverwriteBySize(entrySize, newFileSize) ? entryName : null,
            _ => entryName
        };
        if (resolved != null)
            occupiedNames.Add(resolved);
        CoreLog.Info($"AddConflictHelper.ResolveByAction: entry='{entryName}', action={action} -> {(resolved ?? "(skip)")}");
        return resolved;
    }

    /// <summary>添加场景：磁盘新文件比压缩包条目更新 → 覆盖（与解压方向相反）。</summary>
    private static bool ShouldOverwriteByTime(DateTime? entryModified, DateTime? newFileModified)
    {
        if (entryModified == null || newFileModified == null)
        {
            CoreLog.Info("AddConflictHelper.OverwriteIfOlder: missing timestamp -> overwrite");
            return true;
        }
        var result = newFileModified.Value > entryModified.Value;
        CoreLog.Info($"AddConflictHelper.OverwriteIfOlder: newFile={newFileModified:yyyy-MM-dd HH:mm:ss}, entry={entryModified:yyyy-MM-dd HH:mm:ss} -> {(result ? "overwrite" : "skip")}");
        return result;
    }

    /// <summary>添加场景：磁盘新文件比压缩包条目大 → 覆盖（与解压方向相反）。</summary>
    private static bool ShouldOverwriteBySize(long? entrySize, long? newFileSize)
    {
        if (entrySize == null || newFileSize == null)
        {
            CoreLog.Info("AddConflictHelper.OverwriteIfSmaller: missing size -> overwrite");
            return true;
        }
        var result = newFileSize.Value > entrySize.Value;
        CoreLog.Info($"AddConflictHelper.OverwriteIfSmaller: newFile={newFileSize}, entry={entrySize} -> {(result ? "overwrite" : "skip")}");
        return result;
    }

    private static FileConflictInfo BuildConflictInfo(
        string entryName,
        DateTime? entryModified,
        long? entrySize,
        DateTime? newFileModified,
        long? newFileSize,
        HashSet<string> occupiedNames)
    {
        var info = new FileConflictInfo
        {
            FilePath = entryName,
            EntrySize = entrySize,
            EntryModified = entryModified,
            ExistingSize = newFileSize,
            ExistingModified = newFileModified,
        };
        // 对话框预填的建议名（仅文件名部分）
        info.SuggestedName = Path.GetFileName(GetUniqueEntryName(entryName, occupiedNames));
        return info;
    }

    /// <summary>
    /// 生成不与其他条目冲突的唯一条目名（file.txt → file (1).txt），正确处理 .tar.gz 双扩展名。
    /// </summary>
    public static string GetUniqueEntryName(string entryName, IReadOnlySet<string> occupiedNames)
    {
        var dir = Path.GetDirectoryName(entryName);
        string bareName, ext;
        if (entryName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            bareName = Path.GetFileName(entryName[..^7]);
            ext = ".tar.gz";
        }
        else
        {
            bareName = Path.GetFileNameWithoutExtension(entryName);
            ext = Path.GetExtension(entryName);
        }

        for (int i = 1; i < 1000; i++)
        {
            var candidateName = $"{bareName} ({i}){ext}";
            var candidate = string.IsNullOrEmpty(dir) ? candidateName : $"{dir.Replace('\\', '/')}/{candidateName}";
            if (!occupiedNames.Contains(candidate))
                return candidate;
        }
        return entryName; // 999 个名字全被占用，直接使用原条目名
    }

    /// <summary>用户自定义名合成最终条目名：保留目录前缀 + 净化文件名（复用 FileConflictHelper.SanitizeFileName）。</summary>
    private static string CombineCustomName(string entryName, string customName)
    {
        var dir = Path.GetDirectoryName(entryName);
        var safeName = FileConflictHelper.SanitizeFileName(customName);
        return string.IsNullOrEmpty(dir) ? safeName : $"{dir.Replace('\\', '/')}/{safeName}";
    }
}