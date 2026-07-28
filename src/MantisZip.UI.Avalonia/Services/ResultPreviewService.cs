using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
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
    /// <param name="rootName">根节点显示名称（未使用，保留参数兼容）。</param>
    /// <param name="checkExists">是否逐文件检查目标位置是否存在（默认 false 仅快速模式）。</param>
    /// <param name="filter">文件过滤条件，不为空且 IsActive 时对文件节点标记 IsFilteredOut。</param>
    /// <returns>根节点（destDir 自身），包含完整的树结构。</returns>
    public static PreviewTreeNode BuildExtractPreview(
        IEnumerable<ArchiveItem> entries,
        string destDir,
        string? rootName = null,
        bool checkExists = false,
        FileFilterCriteria? filter = null)
    {
        // 根节点即解压目标目录本身（不再有概念容器层）
        var destNode = new PreviewTreeNode
        {
            Name = Path.GetFileName(destDir.TrimEnd(Path.DirectorySeparatorChar)),
            FullPath = destDir,
            DisplayLabel = destDir,
            IsExpanded = true
        };

        var itemsList = entries.ToList();
        var dirsAdded = new HashSet<string>();

        // Phase 1: Build tree structure from archive entries
        foreach (var item in itemsList.Where(i => i.IsDirectory))
        {
            var path = item.FullPath.TrimEnd('/');
            if (dirsAdded.Add(path))
                AddFolderNode(destNode, path, item);
        }

        foreach (var item in itemsList.Where(i => !i.IsDirectory))
        {
            var fullPath = item.FullPath;
            var lastSlash = fullPath.LastIndexOf('/');
            while (lastSlash >= 0)
            {
                var dirPath = fullPath[..lastSlash];
                if (dirsAdded.Add(dirPath))
                    AddFolderNode(destNode, dirPath, null);
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

            var parent = FindOrCreateParent(destNode, parentPath);
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

            // Apply file filter: mark non-matching files as filtered out
            if (filter != null && filter.IsActive && !FileFilterMatcher.IsMatch(filter, item))
                fileNode.IsFilteredOut = true;

            parent.Children.Add(fileNode);
        }

        // Phase 2: Calculate descendant counts (destNode is now the root)
        CalculateDescendantStats(destNode);

        // Expand by default
        destNode.IsExpanded = true;

        return destNode;
    }

    /// <summary>
    /// 构建压缩预览树。
    /// </summary>
    /// <param name="sourcePaths">用户选择的源路径列表（文件或目录）。</param>
    /// <param name="rootName">根节点显示名称。</param>
    /// <param name="filter">文件过滤条件，不为空且 IsActive 时对文件节点标记 IsFilteredOut。</param>
    /// <param name="outputMode">输出模式（Manual / Separate / Combined）。</param>
    /// <param name="outputPath">Manual/Combined 模式下的输出路径。</param>
    /// <param name="format">压缩格式（"zip" / "7z" / "tar.gz"）。</param>
    /// <param name="keepOriginalExtension">Separate 模式下是否保留源文件扩展名。</param>
    /// <returns>根节点，包含完整的树结构。</returns>
    public static PreviewTreeNode BuildCompressPreview(
        IReadOnlyList<string> sourcePaths,
        string? rootName = null,
        FileFilterCriteria? filter = null,
        CompressOutputMode outputMode = CompressOutputMode.Manual,
        string? outputPath = null,
        string format = "zip",
        bool keepOriginalExtension = false)
    {
        PreviewTreeNode root;

        switch (outputMode)
        {
            case CompressOutputMode.Manual:
            case CompressOutputMode.Combined:
            {
                // 确定有效的输出路径
                string effectiveOutputPath = outputPath;
                if (string.IsNullOrEmpty(effectiveOutputPath))
                {
                    var first = sourcePaths[0];
                    var dir = Directory.Exists(first)
                        ? first.TrimEnd(Path.DirectorySeparatorChar)
                        : Path.GetDirectoryName(first) ?? "";
                    effectiveOutputPath = Path.Combine(dir, ComputeArchiveName(first, format, keepOriginalExt: false));
                }

                // 根节点为压缩包所在的父目录
                var parentDir = Path.GetDirectoryName(effectiveOutputPath) ?? "";
                root = new PreviewTreeNode
                {
                    Name = Path.GetFileName(parentDir.TrimEnd(Path.DirectorySeparatorChar)),
                    FullPath = parentDir,
                    DisplayLabel = parentDir,
                    IsExpanded = true
                };

                BuildSingleArchivePreview(root, sourcePaths, effectiveOutputPath, format, filter);
                break;
            }

            case CompressOutputMode.Separate:
            {
                // 虚拟根节点（无显示标签），其子节点作为顶级项直接显示
                root = new PreviewTreeNode
                {
                    Name = "",
                    FullPath = "",
                    DisplayLabel = "",
                    IsExpanded = true
                };

                BuildSeparateArchivesPreview(root, sourcePaths, format, keepOriginalExtension, filter);
                break;
            }

            default:
                root = new PreviewTreeNode
                {
                    Name = "",
                    FullPath = "",
                    DisplayLabel = "",
                    IsExpanded = true
                };
                break;
        }

        CalculateDescendantStats(root);
        root.IsExpanded = true;

        return root;
    }

    /// <summary>
    /// 计算压缩包名称，与 Core 层 ComputeSeparateOutputPath 保持一致。
    /// </summary>
    private static string ComputeArchiveName(string sourcePath, string format, bool keepOriginalExt)
    {
        string baseName;
        if (Directory.Exists(sourcePath))
            baseName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
        else
            baseName = keepOriginalExt
                ? Path.GetFileName(sourcePath)
                : Path.GetFileNameWithoutExtension(sourcePath);
        string ext = format == "tar.gz" ? ".tar.gz" : "." + format;
        return baseName + ext;
    }

    /// <summary>
    /// 构建 Manual/Combined 模式的单压缩包预览。
    /// </summary>
    private static void BuildSingleArchivePreview(
        PreviewTreeNode root,
        IReadOnlyList<string> sourcePaths,
        string? outputPath,
        string format,
        FileFilterCriteria? filter)
    {
        // 确定输出路径：未指定时用第一个源文件所在目录
        if (string.IsNullOrEmpty(outputPath))
        {
            var first = sourcePaths[0];
            var dir = Directory.Exists(first)
                ? first.TrimEnd(Path.DirectorySeparatorChar)
                : Path.GetDirectoryName(first) ?? "";
            outputPath = Path.Combine(dir, ComputeArchiveName(first, format, keepOriginalExt: false));
        }

        // 输出压缩包节点
        var archiveNode = new PreviewTreeNode
        {
            Name = Path.GetFileName(outputPath),
            FullPath = outputPath,
            DisplayLabel = Path.GetFileName(outputPath),
            IsArchiveNode = true,
            IsExpanded = true,
            ExistsAtDestination = File.Exists(outputPath)
        };

        // 添加源文件/目录作为子节点
        foreach (var path in sourcePaths)
        {
            if (Directory.Exists(path))
            {
                var dirNode = BuildDirectoryNode(path, path, filter);
                archiveNode.Children.Add(dirNode);
            }
            else if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                var isFiltered = filter != null && filter.IsActive && !FileFilterMatcher.IsMatch(filter, path);
                var fileNode = new PreviewTreeNode
                {
                    Name = fi.Name,
                    FullPath = fi.Name,
                    Size = fi.Length,
                    SizeDisplay = FormatUtil.FormatSize(fi.Length),
                    IsExpanded = false,
                    IsFilteredOut = isFiltered
                };
                archiveNode.Children.Add(fileNode);
            }
        }

        root.Children.Add(archiveNode);
    }

    /// <summary>
    /// 构建 Separate 模式的多压缩包预览。
    /// </summary>
    private static void BuildSeparateArchivesPreview(
        PreviewTreeNode root,
        IReadOnlyList<string> sourcePaths,
        string format,
        bool keepOriginalExtension,
        FileFilterCriteria? filter)
    {
        // 按输出父目录分组
        var groups = new Dictionary<string, List<(string sourcePath, string archivePath)>>();

        foreach (var path in sourcePaths)
        {
            string parentDir;
            if (Directory.Exists(path))
                parentDir = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
            else
                parentDir = Path.GetDirectoryName(path) ?? "";

            var archiveName = ComputeArchiveName(path, format, keepOriginalExtension);
            var archivePath = Path.Combine(parentDir, archiveName);

            if (!groups.TryGetValue(parentDir, out var list))
            {
                list = new List<(string, string)>();
                groups[parentDir] = list;
            }
            list.Add((path, archivePath));
        }

        foreach (var kvp in groups)
        {
            var groupDir = kvp.Key;
            // 分组目录节点（显示完整路径）
            var groupNode = new PreviewTreeNode
            {
                Name = Path.GetFileName(groupDir),
                FullPath = groupDir,
                DisplayLabel = groupDir,
                IsExpanded = true
            };

            foreach (var (sourcePath, archivePath) in kvp.Value)
            {
                var archiveName = Path.GetFileName(archivePath);

                // 压缩包节点
                var archiveNode = new PreviewTreeNode
                {
                    Name = archiveName,
                    FullPath = archivePath,
                    DisplayLabel = archiveName,
                    IsArchiveNode = true,
                    IsExpanded = true,
                    ExistsAtDestination = File.Exists(archivePath)
                };

                // 源文件作为子节点
                if (Directory.Exists(sourcePath))
                {
                    var dirNode = BuildDirectoryNode(sourcePath, sourcePath, filter);
                    archiveNode.Children.Add(dirNode);
                }
                else if (File.Exists(sourcePath))
                {
                    var fi = new FileInfo(sourcePath);
                    var isFiltered = filter != null && filter.IsActive && !FileFilterMatcher.IsMatch(filter, sourcePath);
                    var fileNode = new PreviewTreeNode
                    {
                        Name = fi.Name,
                        FullPath = fi.Name,
                        Size = fi.Length,
                        SizeDisplay = FormatUtil.FormatSize(fi.Length),
                        IsExpanded = false,
                        IsFilteredOut = isFiltered
                    };
                    archiveNode.Children.Add(fileNode);
                }

                groupNode.Children.Add(archiveNode);
            }

            root.Children.Add(groupNode);
        }
    }

    /// <summary>
    /// 递归统计每个节点的 TotalDescendantCount、TotalDescendantSize 和 MaxChildDepth。
    /// </summary>
    private static int CalculateDescendantStats(PreviewTreeNode node, int depth = 0)
    {
        int count = 0;
        long totalSize = node.Size;
        int maxChildDepth = depth;

        foreach (var child in node.Children)
        {
            if (child is PreviewTreeNode previewChild)
            {
                count++;
                var childDescendantCount = CalculateDescendantStats(previewChild, depth + 1);
                count += childDescendantCount;
                totalSize += previewChild.TotalDescendantSize;
                maxChildDepth = Math.Max(maxChildDepth, previewChild.MaxChildDepth);
            }
        }

        node.TotalDescendantCount = count;
        node.TotalDescendantSize = totalSize;
        node.MaxChildDepth = maxChildDepth;
        return count;
    }

    /// <summary>
    /// 根据路径构建一个目录节点及其内容的预览树。
    /// </summary>
    private static PreviewTreeNode BuildDirectoryNode(string rootPath, string currentPath, FileFilterCriteria? filter = null)
    {
        var dirInfo = new DirectoryInfo(currentPath);
        var relativePath = currentPath.Length >= rootPath.Length
            ? currentPath[rootPath.Length..].TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/')
            : dirInfo.Name;

        var node = new PreviewTreeNode
        {
            Name = dirInfo.Name,
            FullPath = string.IsNullOrEmpty(relativePath) ? dirInfo.Name : relativePath,
            IsExpanded = false
        };

        try
        {
            foreach (var subDir in dirInfo.GetDirectories())
            {
                var childNode = BuildDirectoryNode(rootPath, subDir.FullName, filter);
                node.Children.Add(childNode);
            }

            foreach (var file in dirInfo.GetFiles())
            {
                var fileRelPath = string.IsNullOrEmpty(relativePath)
                    ? file.Name
                    : $"{relativePath}/{file.Name}";

                var isFiltered = filter != null && filter.IsActive && !FileFilterMatcher.IsMatch(filter, file.FullName);
                var fileNode = new PreviewTreeNode
                {
                    Name = file.Name,
                    FullPath = fileRelPath,
                    Size = file.Length,
                    SizeDisplay = FormatUtil.FormatSize(file.Length),
                    IsExpanded = false,
                    IsFilteredOut = isFiltered
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
