using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 构建结果预览树的静态服务。从 ArchiveItem 列表或源路径列表
/// 生成 PreviewTreeNode 树，用于 ExtractSettingsWindow 和 CompressSettingsWindow 的右侧预览面板。
/// </summary>
public static class ResultPreviewService
{
    /// <summary>
    /// 构建解压预览树。
    /// </summary>
    /// <param name="entries">归档条目列表（来自压缩包）。</param>
    /// <param name="destDir">目标解压目录。</param>
    /// <param name="rootName">根节点显示名称（默认为 "解压预览"）。</param>
    /// <param name="checkExists">是否逐文件检查目标位置是否存在（默认 false 仅快速模式）。</param>
    /// <returns>根节点，包含完整的树结构。</returns>
    public static PreviewTreeNode BuildExtractPreview(
        IEnumerable<ArchiveItem> entries,
        string destDir,
        string? rootName = null,
        bool checkExists = false)
    {
        var root = new PreviewTreeNode
        {
            Name = rootName ?? "解压预览",
            FullPath = "",
            DisplayLabel = rootName ?? destDir
        };

        var itemsList = entries.ToList();
        var dirsAdded = new HashSet<string>();

        // Phase 1: Build tree structure from archive entries (using ArchiveTreeBuilder logic adapted for PreviewTreeNode)
        foreach (var item in itemsList.Where(i => i.IsDirectory))
        {
            var path = item.FullPath.TrimEnd('/');
            if (dirsAdded.Add(path))
                AddFolderNode(root, path, item);
        }

        foreach (var item in itemsList.Where(i => !i.IsDirectory))
        {
            var fullPath = item.FullPath;
            var lastSlash = fullPath.LastIndexOf('/');
            while (lastSlash >= 0)
            {
                var dirPath = fullPath[..lastSlash];
                if (dirsAdded.Add(dirPath))
                    AddFolderNode(root, dirPath, null);
                lastSlash = dirPath.LastIndexOf('/');
            }

            // Add file node
            var parentPath = "";
            var fileName = fullPath;
            var slashIdx = fullPath.LastIndexOf('/');
            if (slashIdx >= 0)
            {
                parentPath = fullPath[..slashIdx];
                fileName = fullPath[(slashIdx + 1)..];
            }

            var parent = FindOrCreateParent(root, parentPath);
            var fileNode = new PreviewTreeNode
            {
                Name = fileName,
                FullPath = fullPath,
                Size = item.Size,
                SizeDisplay = FormatUtil.FormatSize(item.Size),
                IsExpanded = false
            };

            // Check if file exists at destination
            if (checkExists)
            {
                var realPath = Path.Combine(destDir, fullPath.Replace('/', Path.DirectorySeparatorChar));
                fileNode.ExistsAtDestination = File.Exists(realPath);
            }

            parent.Children.Add(fileNode);
        }

        // Phase 2: Calculate descendant counts
        CalculateDescendantStats(root);

        // Expand root by default
        root.IsExpanded = true;

        return root;
    }

    /// <summary>
    /// 构建压缩预览树。
    /// </summary>
    /// <param name="sourcePaths">用户选择的源路径列表（文件或目录）。</param>
    /// <param name="rootName">根节点显示名称。</param>
    /// <returns>根节点，包含完整的树结构。</returns>
    public static PreviewTreeNode BuildCompressPreview(
        IReadOnlyList<string> sourcePaths,
        string? rootName = null)
    {
        var root = new PreviewTreeNode
        {
            Name = rootName ?? "📦",
            FullPath = "",
            DisplayLabel = rootName ?? "压缩内容"
        };

        foreach (var path in sourcePaths)
        {
            if (Directory.Exists(path))
            {
                // Add directory and its contents
                var dirNode = BuildDirectoryNode(path, path);
                root.Children.Add(dirNode);
            }
            else if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                var fileNode = new PreviewTreeNode
                {
                    Name = fi.Name,
                    FullPath = fi.Name,
                    Size = fi.Length,
                    SizeDisplay = FormatUtil.FormatSize(fi.Length),
                    IsExpanded = false
                };
                root.Children.Add(fileNode);
            }
        }

        CalculateDescendantStats(root);
        root.IsExpanded = true;

        return root;
    }

    /// <summary>
    /// 递归统计每个节点的 TotalDescendantCount 和 MaxChildDepth。
    /// </summary>
    private static int CalculateDescendantStats(PreviewTreeNode node, int depth = 0)
    {
        int count = 0;
        int maxChildDepth = depth;

        foreach (var child in node.Children)
        {
            if (child is PreviewTreeNode previewChild)
            {
                count++;
                var childDescendantCount = CalculateDescendantStats(previewChild, depth + 1);
                count += childDescendantCount;
                maxChildDepth = Math.Max(maxChildDepth, previewChild.MaxChildDepth);
            }
        }

        node.TotalDescendantCount = count;
        node.MaxChildDepth = maxChildDepth;
        return count;
    }

    /// <summary>
    /// 根据路径构建一个目录节点及其内容的预览树。
    /// </summary>
    private static PreviewTreeNode BuildDirectoryNode(string rootPath, string currentPath)
    {
        var dirInfo = new DirectoryInfo(currentPath);
        var relativePath = currentPath.Length >= rootPath.Length
            ? currentPath[rootPath.Length..].TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/')
            : dirInfo.Name;

        var node = new PreviewTreeNode
        {
            Name = dirInfo.Name,
            FullPath = relativePath,
            IsExpanded = false
        };

        try
        {
            // Add subdirectories
            foreach (var subDir in dirInfo.GetDirectories())
            {
                var childNode = BuildDirectoryNode(rootPath, subDir.FullName);
                node.Children.Add(childNode);
            }

            // Add files
            foreach (var file in dirInfo.GetFiles())
            {
                var fileRelPath = string.IsNullOrEmpty(relativePath)
                    ? file.Name
                    : $"{relativePath}/{file.Name}";

                var fileNode = new PreviewTreeNode
                {
                    Name = file.Name,
                    FullPath = fileRelPath,
                    Size = file.Length,
                    SizeDisplay = FormatUtil.FormatSize(file.Length),
                    IsExpanded = false
                };
                node.Children.Add(fileNode);
            }
        }
        catch
        {
            // Skip inaccessible directories
        }

        return node;
    }

    /// <summary>
    /// 在预览树中添加一个目录节点。
    /// </summary>
    private static void AddFolderNode(PreviewTreeNode root, string dirPath, ArchiveItem? item)
    {
        var parts = dirPath.Split('/');
        var current = root;

        for (int i = 0; i < parts.Length; i++)
        {
            var partName = parts[i];
            var currentPath = string.Join("/", parts.Take(i + 1));
            var existing = current.Children
                .OfType<PreviewTreeNode>()
                .FirstOrDefault(n => n.FullPath == currentPath);

            if (existing == null)
            {
                existing = new PreviewTreeNode
                {
                    Name = partName,
                    FullPath = currentPath,
                    IsExpanded = false
                };
                current.Children.Add(existing);
            }

            current = existing;
        }
    }

    /// <summary>
    /// 在树中查找或创建指定路径的父目录节点。
    /// </summary>
    private static PreviewTreeNode FindOrCreateParent(PreviewTreeNode root, string path)
    {
        if (string.IsNullOrEmpty(path))
            return root;

        var parts = path.Split('/');
        var current = root;

        for (int i = 0; i < parts.Length; i++)
        {
            var partName = parts[i];
            var currentPath = string.Join("/", parts.Take(i + 1));
            var existing = current.Children
                .OfType<PreviewTreeNode>()
                .FirstOrDefault(n => n.FullPath == currentPath);

            if (existing == null)
            {
                existing = new PreviewTreeNode
                {
                    Name = partName,
                    FullPath = currentPath,
                    IsExpanded = false
                };
                current.Children.Add(existing);
            }

            current = existing;
        }

        return current;
    }
}
