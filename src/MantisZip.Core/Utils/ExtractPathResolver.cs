using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 解压路径统一计算模块（Single Source of Truth）。
/// 预览树与实际解压共用本模块计算「压缩包条目 → 目标相对路径」，
/// 从结构上杜绝预览效果与实际解压不一致。
///
/// 语义（与 SelectedItemsExtractService 历史逻辑逐字一致）：
/// - preserveFullPath=false 且 currentFolder 非空时，裁剪当前浏览文件夹前缀
///   （条目不在当前文件夹下时保持原路径）
/// - 裁剪后经 <see cref="FileConflictHelper.SanitizeEntryPath"/> 净化（防 Zip Slip）
/// </summary>
public static class ExtractPathResolver
{
    /// <summary>
    /// 裁剪当前浏览文件夹前缀。preserveFullPath=true 或 currentFolder 为空时不裁剪。
    /// </summary>
    /// <param name="entryPath">压缩包内条目路径（如 "docs/a/b.txt"）。</param>
    /// <param name="currentFolder">当前浏览的压缩包内路径（如 "docs"），空 = 根目录不裁剪。</param>
    /// <param name="preserveFullPath">是否保留完整路径（AppSettings.ExtractPreserveFullPath）。</param>
    public static string TrimCurrentFolderPrefix(string entryPath, string currentFolder, bool preserveFullPath)
    {
        if (preserveFullPath || string.IsNullOrEmpty(currentFolder))
            return entryPath;

        var cf = currentFolder.TrimEnd('/') + "/";
        return entryPath.StartsWith(cf, StringComparison.OrdinalIgnoreCase)
            ? entryPath.Substring(cf.Length)
            : entryPath;
    }

    /// <summary>
    /// 计算单条目的目标相对路径（剥根 / 加根 → 裁剪 → 净化）。
    /// </summary>
    /// <param name="entryKey">条目 key（ArchiveItem.FullPath ?? Name）。</param>
    /// <param name="currentFolder">当前浏览的压缩包内路径。</param>
    /// <param name="preserveFullPath">是否保留完整路径。</param>
    /// <param name="stripRoot">要剥掉的唯一根目录段（空 = 不剥）；只剥一层。</param>
    /// <param name="addRoot">要加的一层根目录段（空 = 不加）。</param>
    /// <returns>净化后的相对路径（'/' 分隔）；该条目被剥空时返回 null（调用方应跳过它）。</returns>
    /// <exception cref="InvalidOperationException">条目路径净化后为空（恶意路径）。</exception>
    /// <remarks>
    /// <b>叠序不可换</b>：必须先剥/加根再裁浏览前缀。反过来的话，剥根本该把
    /// <c>7zip/</c> 整段去掉，但浏览前缀已按 <c>7zip</c> 裁过一遍，剥根就找不到目标了。
    /// </remarks>
    public static string? ResolveRelativePath(
        string entryKey,
        string currentFolder,
        bool preserveFullPath,
        string? stripRoot = null,
        string? addRoot = null)
    {
        var adjusted = ArchiveRootPolicy.Apply(entryKey, stripRoot, addRoot);
        if (adjusted == null)
            return null;

        var outputEntryPath = TrimCurrentFolderPrefix(adjusted, currentFolder, preserveFullPath);
        return FileConflictHelper.SanitizeEntryPath(outputEntryPath);
    }

    /// <summary>
    /// 批量计算所有条目的目标相对路径。
    /// </summary>
    /// <returns>entryKey → 净化后的相对路径（OrdinalIgnoreCase 字典）。</returns>
    /// <exception cref="InvalidOperationException">任一条目路径净化失败（解压侧使用：整个操作失败，不产生半套错误文件）。</exception>
    public static IReadOnlyDictionary<string, string> ResolveAll(
        IEnumerable<ArchiveItem> entries,
        string currentFolder,
        bool preserveFullPath,
        string? stripRoot = null,
        string? addRoot = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in entries)
        {
            var key = item.FullPath ?? item.Name;
            var resolved = ResolveRelativePath(key, currentFolder, preserveFullPath, stripRoot, addRoot);
            if (resolved != null)
                result[key] = resolved;
            // resolved == null：剥根后条目本身消失（如根目录条目），不进预览树
        }
        return result;
    }
}
