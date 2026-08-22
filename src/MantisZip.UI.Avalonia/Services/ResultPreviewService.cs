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
    /// <param name="progress">进度回调（0–100），在后台线程调用，需自行节流/封送到 UI 线程。</param>
    /// <param name="preserveFullPath">是否保留完整路径（AppSettings.ExtractPreserveFullPath）。</param>
    /// <param name="currentFolder">当前浏览的压缩包内路径（路径裁剪锚点，与解压侧 ExtractPathResolver 同语义）。</param>
    /// <returns>根节点（destDir 自身），包含完整的树结构。</returns>
    public static PreviewTreeNode BuildExtractPreview(
        IEnumerable<ArchiveItem> entries,
        string destDir,
        string? rootName = null,
        bool checkExists = false,
        FileFilterCriteria? filter = null,
        IProgress<double>? progress = null,
        bool preserveFullPath = true,
        string currentFolder = "")
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
        var fileItems = itemsList.Where(i => !i.IsDirectory).ToList();
        var dirsAdded = new HashSet<string>();

        // Phase 1: Build tree structure from archive entries
        // 统一路径计算（与解压侧 ExtractPathResolver 同语义）；恶意路径条目逐条跳过，不影响整树
        foreach (var item in itemsList.Where(i => i.IsDirectory))
        {
            string path;
            try
            {
                path = ExtractPathResolver.ResolveRelativePath(item.FullPath ?? item.Name, currentFolder, preserveFullPath);
            }
            catch
            {
                continue;
            }
            path = path.TrimEnd('/');
            if (path.Length > 0 && dirsAdded.Add(path))
                AddFolderNode(destNode, path, item);
        }

        // 进度节流：每 1% 上报一次，避免大量条目时向 UI 线程投递过多回调
        int totalFiles = fileItems.Count;
        int processedFiles = 0;
        double lastReportedPct = -1;

        void ReportProgress()
        {
            if (progress == null || totalFiles == 0) return;
            var pct = processedFiles * 100.0 / totalFiles;
            if (pct - lastReportedPct >= 1.0 || processedFiles == totalFiles)
            {
                lastReportedPct = pct;
                progress.Report(pct);
            }
        }

        foreach (var item in fileItems)
        {
            string fullPath;
            try
            {
                fullPath = ExtractPathResolver.ResolveRelativePath(item.FullPath ?? item.Name, currentFolder, preserveFullPath);
            }
            catch
            {
                // 恶意路径条目：跳过，不进入预览树
                continue;
            }
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

            processedFiles++;
            ReportProgress();
        }

        // Phase 2: Check directory existence at destination
        if (checkExists)
        {
            MarkDirectoryConflicts(destNode, destDir);
        }

        // Phase 3: Calculate descendant counts (destNode is now the root)
        CalculateDescendantStats(destNode);

        // Expand by default
        destNode.IsExpanded = true;

        progress?.Report(100);

        return destNode;
    }

    /// <summary>
    /// 构建压缩预览树（双产物）：同时产出预览树与过滤后的压缩计划（B 数据集）。
    /// 树 = A 数据（完整结构 + IsFilteredOut 标记）；Plan = B 数据（每源输出包路径 + 匹配文件清单）。
    /// 执行侧（CompressService）消费 Plan，保证预览 = 实际。
    /// </summary>
    /// <param name="sourcePaths">用户选择的源路径列表（文件或目录）。</param>
    /// <param name="rootName">根节点显示名称。</param>
    /// <param name="filter">文件过滤条件，不为空且 IsActive 时对文件节点标记 IsFilteredOut。</param>
    /// <param name="outputMode">输出模式（Manual / Separate / Combined）。</param>
    /// <param name="outputPath">Manual/Combined 模式下的输出路径。</param>
    /// <param name="format">压缩格式（"zip" / "7z" / "tar.gz"）。</param>
    /// <param name="keepOriginalExtension">Separate 模式下是否保留源文件扩展名。</param>
    /// <returns>根节点与压缩计划（B 数据集）。</returns>
    public static (PreviewTreeNode Root, CompressPlan Plan) BuildCompressPreview(
        IReadOnlyList<string> sourcePaths,
        string? rootName = null,
        FileFilterCriteria? filter = null,
        CompressOutputMode outputMode = CompressOutputMode.Manual,
        string? outputPath = null,
        string format = "zip",
        bool keepOriginalExtension = false)
    {
        PreviewTreeNode root;
        IReadOnlyList<CompressPlanItem> planItems;
        string? planOutputPath = null;

        // 过滤激活时：为每个源准备匹配文件收集器（树构建时填入，构建后回填 B）
        bool filterActive = filter != null && filter.IsActive;
        var collectors = new Dictionary<string, List<string>>();
        if (filterActive)
        {
            foreach (var p in sourcePaths)
                collectors[p] = new List<string>();
        }

        switch (outputMode)
        {
            case CompressOutputMode.Manual:
            case CompressOutputMode.Combined:
            {
                // 确定有效的输出路径
                string? effectiveOutputPath = outputPath;
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

                // B 数据集：全部源共享同一输出包
                planItems = CompressPathPlanner.PlanSingle(sourcePaths, effectiveOutputPath, format);
                planOutputPath = effectiveOutputPath;

                BuildSingleArchivePreview(root, sourcePaths, effectiveOutputPath, format, filter, planItems, collectors);
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

                // B 数据集：每源一个输出包（Bug 2 语义：目录源用完整目录名）
                planItems = CompressPathPlanner.PlanSeparate(sourcePaths, format, keepOriginalExtension);

                BuildSeparateArchivesPreview(root, sourcePaths, format, keepOriginalExtension, filter, planItems, collectors);
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
                planItems = Array.Empty<CompressPlanItem>();
                break;
        }

        CalculateDescendantStats(root);
        root.IsExpanded = true;

        // 回填 B：过滤激活时用收集器重建 IncludedFiles（目录无匹配 → 空清单，非 null）
        if (filterActive)
        {
            planItems = planItems
                .Select(item => collectors.TryGetValue(item.SourcePath, out var list)
                    ? item with { IncludedFiles = list }
                    : item)
                .ToList();
        }

        return (root, new CompressPlan(outputMode, planOutputPath, planItems));
    }

    /// <summary>
    /// 计算压缩包名称（唯一实现收敛到 Core 层 <see cref="CompressPathPlanner"/>）。
    /// </summary>
    private static string ComputeArchiveName(string sourcePath, string format, bool keepOriginalExt)
        => CompressPathPlanner.ComputeArchiveName(sourcePath, format, keepOriginalExt);

    /// <summary>
    /// 构建 Manual/Combined 模式的单压缩包预览。
    /// </summary>
    private static void BuildSingleArchivePreview(
        PreviewTreeNode root,
        IReadOnlyList<string> sourcePaths,
        string? outputPath,
        string format,
        FileFilterCriteria? filter,
        IReadOnlyList<CompressPlanItem> planItems,
        Dictionary<string, List<string>> collectors)
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

        // 添加源文件/目录作为子节点，同时收集匹配文件绝对路径（填入 B）
        foreach (var path in sourcePaths)
        {
            // 该源对应的匹配文件收集器（仅过滤激活时收集）
            collectors.TryGetValue(path, out var included);
            if (Directory.Exists(path))
            {
                var dirNode = BuildDirectoryNode(path, path, filter, included);
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
                if (!isFiltered && included != null)
                    included.Add(path);
                archiveNode.Children.Add(fileNode);
            }
        }

        // 检查压缩包内所有文件是否均被过滤（空存档标记）
        archiveNode.IsArchiveEmpty = !NodeHasVisibleContent(archiveNode);

        root.Children.Add(archiveNode);
    }

    /// <summary>
    /// 递归检查节点及其所有子节点中是否有未被过滤的可见节点。
    /// </summary>
    private static bool NodeHasVisibleContent(PreviewTreeNode node)
    {
        if (node.Children.Count == 0)
            return !node.IsFilteredOut; // 叶子节点：本身未被过滤即可见
        return node.Children.OfType<PreviewTreeNode>().Any(NodeHasVisibleContent);
    }

    /// <summary>
    /// 构建 Separate 模式的多压缩包预览。
    /// 输出路径取自已规划的 B（planItems，每源一个 OutputArchivePath），预览与实际压缩同源。
    /// </summary>
    private static void BuildSeparateArchivesPreview(
        PreviewTreeNode root,
        IReadOnlyList<string> sourcePaths,
        string format,
        bool keepOriginalExtension,
        FileFilterCriteria? filter,
        IReadOnlyList<CompressPlanItem> planItems,
        Dictionary<string, List<string>> collectors)
    {
        // 源 → 计划项查找表（B 提供输出包路径，不再本地重算）
        var itemBySource = new Dictionary<string, CompressPlanItem>();
        foreach (var item in planItems)
            itemBySource[item.SourcePath] = item;

        // 按输出父目录分组
        var groups = new Dictionary<string, List<(string sourcePath, string archivePath)>>();

        foreach (var path in sourcePaths)
        {
            var archivePath = itemBySource.TryGetValue(path, out var item)
                ? item.OutputArchivePath
                : CompressPathPlanner.ComputeOutputPath(path, format, keepOriginalExtension);
            var parentDir = Path.GetDirectoryName(archivePath) ?? "";

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

                // 该源对应的匹配文件收集器（仅过滤激活时收集）
                collectors.TryGetValue(sourcePath, out var included);

                // 源文件作为子节点
                if (Directory.Exists(sourcePath))
                {
                    var dirNode = BuildDirectoryNode(sourcePath, sourcePath, filter, included);
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
                    if (!isFiltered && included != null)
                        included.Add(sourcePath);
                    archiveNode.Children.Add(fileNode);
                }

                // 检查压缩包内所有文件是否均被过滤
                archiveNode.IsArchiveEmpty = !NodeHasVisibleContent(archiveNode);
                groupNode.Children.Add(archiveNode);
            }

            root.Children.Add(groupNode);
        }
    }

    /// <summary>
    /// 对已组装完成的树根重算子孙统计（合并多压缩包预览在 VM 侧组装子树后调用，
    /// 供摘要栏显示全树文件数/总大小）。
    /// </summary>
    public static void RecalculateDescendantStats(PreviewTreeNode root) => CalculateDescendantStats(root);

    /// <summary>
    /// 递归统计每个节点的 TotalDescendantCount、TotalDescendantSize 和 MaxChildDepth。
    /// TotalDescendantCount 只统计文件节点（不含目录条目），TotalDescendantSize 为子树内文件大小之和。
    /// 被过滤（IsFilteredOut）的节点整棵跳过：过滤掉的文件不会实际解压/压缩，不计入目录统计（与摘要栏语义一致）。
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
                // 跳过被过滤的节点（其子树亦不会实际处理）
                if (previewChild.IsFilteredOut)
                    continue;

                // 只统计文件节点，目录条目自身不计入数量
                if (!previewChild.IsDirectory)
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
    /// <param name="rootPath">源根目录绝对路径。</param>
    /// <param name="currentPath">当前目录绝对路径。</param>
    /// <param name="filter">文件过滤条件，不为空且 IsActive 时对文件节点标记 IsFilteredOut。</param>
    /// <param name="includedFiles">匹配过滤条件的文件绝对路径收集器（过滤激活时传入，未匹配则跳过）；null 时不收集。</param>
    private static PreviewTreeNode BuildDirectoryNode(string rootPath, string currentPath, FileFilterCriteria? filter = null, List<string>? includedFiles = null)
    {
        var dirInfo = new DirectoryInfo(currentPath);
        var relativePath = currentPath.Length >= rootPath.Length
            ? currentPath[rootPath.Length..].TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/')
            : dirInfo.Name;

        var node = new PreviewTreeNode
        {
            Name = dirInfo.Name,
            FullPath = string.IsNullOrEmpty(relativePath) ? dirInfo.Name : relativePath,
            IsExpanded = false,
            IsDirectory = true
        };

        try
        {
            foreach (var subDir in dirInfo.GetDirectories())
            {
                var childNode = BuildDirectoryNode(rootPath, subDir.FullName, filter, includedFiles);
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
                if (!isFiltered && includedFiles != null)
                    includedFiles.Add(file.FullName);
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
                    IsExpanded = false,
                    IsDirectory = true
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
                    IsExpanded = false,
                    IsDirectory = true
                };
                current.Children.Add(existing);
            }

            current = existing;
        }

        return current;
    }

    /// <summary>
    /// 递归标记目录节点在目标路径是否已存在。
    /// </summary>
    private static void MarkDirectoryConflicts(PreviewTreeNode node, string destDir)
    {
        foreach (var child in node.Children.OfType<PreviewTreeNode>())
        {
            if (child.IsDirectory && !child.IsTruncated)
            {
                var realPath = Path.Combine(destDir, child.FullPath.Replace('/', Path.DirectorySeparatorChar));
                child.ExistsAtDestination = Directory.Exists(realPath);
                MarkDirectoryConflicts(child, destDir);
            }
        }
    }
}
