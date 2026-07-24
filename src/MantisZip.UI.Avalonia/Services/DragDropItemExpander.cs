using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Expand directory selections into flat file lists and compute extract target paths.
/// Ported from WPF MainWindow.DragDrop.cs ExpandDragItems + GetDragExtractPath.
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

    /// <summary>
    /// Compute the output path for a file in the target directory.
    /// For items inside selected directories: trim ancestor path to keep relative structure.
    /// </summary>
    public static string GetExtractPath(
        ArchiveItem item,
        IReadOnlyList<ArchiveItem> selectedDirs,
        string targetDirectory)
    {
        var normalized = item.FullPath.Replace('\\', '/');
        var relative = normalized;

        foreach (var dir in selectedDirs)
        {
            var dirPath = dir.FullPath.Replace('\\', '/').TrimEnd('/');
            var prefix = dirPath + "/";
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                var lastSlash = dirPath.LastIndexOf('/');
                relative = lastSlash >= 0
                    ? normalized[(lastSlash + 1)..]
                    : normalized;
                break;
            }
        }

        var safePath = SanitizeRelativePath(relative);
        return Path.GetFullPath(Path.Combine(targetDirectory, safePath));
    }

    private static string SanitizeRelativePath(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "..")
            .ToArray();
        return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
    }
}
