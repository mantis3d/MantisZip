using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Expand directory selections into flat file lists.
/// Ported from WPF MainWindow.DragDrop.cs ExpandDragItems.
/// 输出路径计算已统一到 <see cref="SelectedItemsExtractService"/>（右键/拖拽同一语义）。
/// </summary>
internal static class DragDropItemExpander
{
    /// <summary>
    /// Expand selected items: directories become their contained files (recursive).
    /// Files stay as-is. Deduplicates by FullPath.
    /// </summary>
    public static IReadOnlyList<ArchiveItem> ExpandItems(
        IEnumerable<ArchiveItem> selectedItems,
        IReadOnlyList<ArchiveItem> allItems)
    {
        var selectedDirs = selectedItems.Where(i => i.IsDirectory)
            .Select(d => d.FullPath.Replace('\\', '/').TrimEnd('/') + "/")
            .ToList();

        var selectedFiles = selectedItems.Where(i => !i.IsDirectory)
            .Select(f => f.FullPath.Replace('\\', '/'))
            .ToHashSet();

        var result = new List<ArchiveItem>();
        var seen = new HashSet<string>();

        foreach (var item in allItems)
        {
            if (item.IsDirectory)
                continue;

            var normalized = item.FullPath.Replace('\\', '/');

            var inSelectedDir = selectedDirs.Any(d => normalized.StartsWith(d, StringComparison.Ordinal));
            var isSelectedFile = selectedFiles.Contains(normalized);

            if (inSelectedDir || isSelectedFile)
            {
                if (seen.Add(normalized))
                    result.Add(item);
            }
        }

        return result;
    }
}

