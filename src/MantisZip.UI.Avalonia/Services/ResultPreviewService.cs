using System.Threading;
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

    /// <summary>单个源（文件或目录）的构建结果。</summary>
    /// <param name="Node">该源的子树节点（浅层或全量）。</param>
    /// <param name="IncludedFiles">该源内经过滤的匹配文件绝对路径清单（供 B 数据集白名单回填）。</param>
    /// <param name="IsFull">是否为全量构建（false = 浅层阶段产物，后续需被全量替换）。</param>
    public sealed record SourceSubtree(PreviewTreeNode Node, List<string> IncludedFiles, bool IsFull);
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
    /// <param name="stripRoot">要剥掉的包内根段（null = 不剥）；必须与执行侧同源，否则预览与实解对不上。</param>
    /// <param name="addRoot">要加的根段（null = 不加）；同上。</param>
    /// <returns>根节点（destDir 自身），包含完整的树结构。</returns>
    public static PreviewTreeNode BuildExtractPreview(
        IEnumerable<ArchiveItem> entries,
        string destDir,
        string? rootName = null,
        bool checkExists = false,
        FileFilterCriteria? filter = null,
        IProgress<double>? progress = null,
        bool preserveFullPath = true,
        string currentFolder = "",
        string? stripRoot = null,
        string? addRoot = null)
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
            string? resolved;
            try
            {
                resolved = ExtractPathResolver.ResolveRelativePath(
                    item.FullPath ?? item.Name, currentFolder, preserveFullPath, stripRoot, addRoot);
            }
            catch
            {
                continue;
            }
            // 剥根会把根目录条目本身剥成空 ⇒ 预览里也不该出现这一层
            if (resolved == null)
                continue;
            var path = resolved.TrimEnd('/');
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
                var relative = ExtractPathResolver.ResolveRelativePath(
                    item.FullPath ?? item.Name, currentFolder, preserveFullPath, stripRoot, addRoot);
                // 剥根后不剩内容（根目录条目本身）⇒ 该文件条目不存在于结果中
                if (relative == null)
                {
                    processedFiles++;
                    ReportProgress();
                    continue;
                }
                fullPath = relative;
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

            // Apply file filter: mark non-matching files as filtered out
            if (filter != null && filter.IsActive && !FileFilterMatcher.IsMatch(filter, item))
                fileNode.IsFilteredOut = true;

            parent.Children.Add(fileNode);

            processedFiles++;
            ReportProgress();
        }

        // Phase 2: Check file/directory existence at destination
        if (checkExists)
        {
            ApplyConflictMarkers(destNode, destDir);
        }

        // Phase 3: Calculate descendant counts (destNode is now the root)
        CalculateDescendantStats(destNode);

        // Expand by default
        destNode.IsExpanded = true;

        progress?.Report(100);

        return destNode;
    }

    /// <summary>
    /// 构建单个源（文件或目录）的子树。不存在路径返回 null。
    /// </summary>
    /// <param name="sourcePath">源文件或目录的绝对路径。</param>
    /// <param name="filter">文件过滤条件。</param>
    /// <param name="maxDepth">最大递归深度（int.MaxValue=全量）。</param>
    /// <param name="maxWidthPerDir">每个目录最多平铺的子项数（int.MaxValue=不限）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="skeletonProgress">骨架进度接收器（每枚举一个文件上报一次累计值）；null 表示不上报。
    /// 回调在构建线程同步调用，实现方不得直接做 UI 操作。</param>
    /// <param name="snapshotSink">骨架期快照接收器（每满 <paramref name="snapshotMinNewFiles"/> 个文件且距上次超过
    /// <paramref name="snapshotInterval"/> 时交付一份「当前已构建部分」的深拷贝，供 UI 渐进长出条目）；
    /// null 表示不做快照。回调同样在构建线程同步调用。</param>
    /// <param name="snapshotInterval">快照最小间隔；null 用默认 <see cref="SnapshotInterval"/>（测试传 <see cref="TimeSpan.Zero"/>）。</param>
    /// <param name="snapshotMinNewFiles">快照最小新增文件数；0 表示用默认 <see cref="SnapshotMinNewFiles"/>（测试传小值）。</param>
    public static SourceSubtree? BuildSourceSubtree(
        string sourcePath,
        FileFilterCriteria? filter = null,
        int maxDepth = int.MaxValue,
        int maxWidthPerDir = int.MaxValue,
        CancellationToken ct = default,
        IProgress<SkeletonProgress>? skeletonProgress = null,
        Action<PreviewTreeNode>? snapshotSink = null,
        TimeSpan? snapshotInterval = null,
        int snapshotMinNewFiles = 0)
    {
        ct.ThrowIfCancellationRequested();
        var included = new List<string>();

        if (Directory.Exists(sourcePath))
        {
            var counter = skeletonProgress == null && snapshotSink == null
                ? null
                : new SkeletonCounter(
                    skeletonProgress,
                    snapshotSink,
                    snapshotInterval ?? SnapshotInterval,
                    snapshotMinNewFiles > 0 ? snapshotMinNewFiles : SnapshotMinNewFiles);
            var node = BuildDirectoryNode(sourcePath, sourcePath, filter, included, maxDepth, maxWidthPerDir, ct, depth: 0,
                skeletonCounter: counter);
            return new SourceSubtree(node, included, IsFull: maxDepth == int.MaxValue && maxWidthPerDir == int.MaxValue);
        }

        if (File.Exists(sourcePath))
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
            // 单文件源只有一个文件：直接上报一次累计值（文件节点不显示目录统计文案，仅为语义完整）
            if (!isFiltered)
            {
                included.Add(sourcePath);
                skeletonProgress?.Report(new SkeletonProgress(1, fi.Length));
            }
            return new SourceSubtree(fileNode, included, IsFull: true);
        }

        return null;
    }

    /// <summary>
    /// 装配压缩预览树：消费已构建的 subtrees（与 sourcePaths 同序，可含 null），
    /// 产出预览根节点与压缩计划。
    /// </summary>
    /// <param name="rootMode">
    /// 「包内根目录」四态（见 <see cref="RootDirectoryMode"/>）；决定预览里要不要省掉源目录那一层、
    /// 要不要在最前面加一层根。必须与执行侧同源 —— 预览显示的样子就是压缩包里的样子。
    /// </param>
    /// <param name="wrapRoot">加根时使用的根名（空 = 用输出包名去扩展名）。</param>
    public static (PreviewTreeNode Root, CompressPlan Plan) AssembleCompressPreview(
        IReadOnlyList<string> sourcePaths,
        IReadOnlyList<SourceSubtree?> subtrees,
        CompressOutputMode outputMode,
        string? outputPath,
        string format,
        bool keepOriginalExtension,
        FileFilterCriteria? filter,
        string? fileNameTemplate = null,
        DateTime? templateTimestamp = null,
        RootDirectoryMode rootMode = RootDirectoryMode.Original,
        string? wrapRoot = null)
    {
        // 「包内根目录」四态：Strip 只在源恰为一个目录时成立；Wrap 在 Separate 下不可用（UI 置灰），
        // 这里再兜一次底（置灰失效时也不会产出「每包各套一层」这种形状）。
        var hasSingleSourceDir = ArchiveRootPolicy.HasSingleSourceDirectory(sourcePaths);
        var rootAdjustment = ArchiveRootPolicy.Resolve(rootMode, hasSingleSourceDir);
        if (outputMode == CompressOutputMode.Separate && rootAdjustment.Wrap)
            rootAdjustment = new RootAdjustment(rootAdjustment.Strip, false);

        // 预览里要落地的两个段：剥 = 源目录名（仅单目录源），加 = 用户给的根名或输出包主名。
        var previewStripRoot = rootAdjustment.Strip && hasSingleSourceDir
            ? ArchiveRootPolicy.GetSourceStripName(sourcePaths[0])
            : null;
        PreviewTreeNode root;
        IReadOnlyList<CompressPlanItem> planItems;
        string? planOutputPath = null;

        // 过滤激活时：为每个源准备匹配文件收集器（树构建时填入，构建后回填 B）
        bool filterActive = filter != null && filter.IsActive;
        var includedBySource = new Dictionary<string, List<string>>();
        if (filterActive)
        {
            for (int i = 0; i < sourcePaths.Count; i++)
            {
                var st = subtrees[i];
                if (st != null)
                    includedBySource[sourcePaths[i]] = st.IncludedFiles;
            }
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
                    effectiveOutputPath = Path.Combine(dir, ComputeArchiveName(first, format, keepOriginalExt: false, fileNameTemplate, templateTimestamp, sourcePaths));
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

                // 输出压缩包节点
                var archiveNode = new PreviewTreeNode
                {
                    Name = Path.GetFileName(effectiveOutputPath),
                    FullPath = effectiveOutputPath,
                    DisplayLabel = Path.GetFileName(effectiveOutputPath),
                    IsArchiveNode = true,
                    IsExpanded = true,
                    ExistsAtDestination = File.Exists(effectiveOutputPath)
                };

                // 挂载各源子树（套根时先包一层「根目录」节点）
                var wrapName = rootAdjustment.Wrap
                    ? ArchiveRootPolicy.GetWrapName(wrapRoot, effectiveOutputPath)
                    : null;
                PreviewTreeNode? wrapNode = null;
                if (wrapName != null)
                {
                    // 根名取用户输入，回落输出包主名；节点只用于展示这一层，不参与计划
                    wrapNode = new PreviewTreeNode
                    {
                        Name = wrapName,
                        FullPath = wrapName,
                        IsDirectory = true,
                        IsExpanded = true
                    };
                    archiveNode.Children.Add(wrapNode);
                }

                var mountTarget = wrapNode?.Children ?? archiveNode.Children;
                for (int i = 0; i < sourcePaths.Count; i++)
                {
                    foreach (var child in ProjectForRootMode(subtrees[i]?.Node, previewStripRoot, sourcePaths.Count == 1))
                        mountTarget.Add(child);
                }

                archiveNode.IsArchiveEmpty = !NodeHasVisibleContent(archiveNode);
                root.Children.Add(archiveNode);
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
                planItems = CompressPathPlanner.PlanSeparate(sourcePaths, format, keepOriginalExtension, fileNameTemplate, templateTimestamp);

                BuildSeparateArchivesPreview(root, sourcePaths, subtrees, planItems, format, keepOriginalExtension, fileNameTemplate, templateTimestamp, previewStripRoot);
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
                .Select(item => includedBySource.TryGetValue(item.SourcePath, out var list)
                    ? item with { IncludedFiles = list }
                    : item)
                .ToList();
        }

        return (root, new CompressPlan(outputMode, planOutputPath, planItems));
    }

    /// <summary>
    /// 构建压缩预览树（双产物）：同时产出预览树与过滤后的压缩计划（B 数据集）。
    /// 薄包装：逐源构建子树 → 装配。
    /// </summary>
    public static (PreviewTreeNode Root, CompressPlan Plan) BuildCompressPreview(
        IReadOnlyList<string> sourcePaths,
        string? rootName = null,
        FileFilterCriteria? filter = null,
        CompressOutputMode outputMode = CompressOutputMode.Manual,
        string? outputPath = null,
        string format = "zip",
        bool keepOriginalExtension = false,
        string? fileNameTemplate = null,
        DateTime? templateTimestamp = null,
        RootDirectoryMode rootMode = RootDirectoryMode.Original,
        string? wrapRoot = null)
    {
        var subtrees = sourcePaths.Select(p => BuildSourceSubtree(p, filter)).ToList();
        return AssembleCompressPreview(sourcePaths, subtrees, outputMode, outputPath, format, keepOriginalExtension, filter, fileNameTemplate, templateTimestamp, rootMode, wrapRoot);
    }

    /// <summary>
    /// 计算压缩包名称（唯一实现收敛到 Core 层 <see cref="CompressPathPlanner"/>）。
    /// </summary>
    private static string ComputeArchiveName(
        string sourcePath, string format, bool keepOriginalExt,
        string? fileNameTemplate, DateTime? templateTimestamp,
        IReadOnlyList<string> sourcePaths)
    {
        // 单包多源（Manual / Combined）无显式输出路径时，模板按「第一个源 + 公共父目录名」渲染。
        var templatedName = CompressPathPlanner.ComputeSingleArchiveName(
            sourcePaths, format, fileNameTemplate, templateTimestamp);
        return templatedName
            ?? CompressPathPlanner.ComputeArchiveName(sourcePath, format, keepOriginalExt);
    }

    /// <summary>
    /// 按「包内根目录」四态把一个源的子树投影成要挂进包里的顶层节点序列。
    /// </summary>
    /// <param name="node">该源构建好的子树根（单目录源 = 目录节点，单文件源 = 文件节点）。</param>
    /// <param name="stripRoot">要剥掉的根段（null = 不剥）。</param>
    /// <param name="singleSource">是否只有一个源（剥根只在单目录源时成立，与执行侧同一判据）。</param>
    /// <returns>
    /// 原样 / 套根 → 该源节点本身；剥根 → 目录节点内部的内容（源目录那一层不再出现），
    /// 与引擎把该目录的子项直接放到包顶层一致。
    /// </returns>
    private static IReadOnlyList<PreviewTreeNode> ProjectForRootMode(
        PreviewTreeNode? node, string? stripRoot, bool singleSource)
    {
        if (node == null)
            return Array.Empty<PreviewTreeNode>();

        if (string.IsNullOrEmpty(stripRoot) || !singleSource || !node.IsDirectory)
            return new[] { node };

        // 名字对不上说明这个源的目录层不是被剥的那一层（防御：别把入口的下层也剥掉）
        if (!string.Equals(node.Name, stripRoot, StringComparison.OrdinalIgnoreCase))
            return new[] { node };

        return node.Children.OfType<PreviewTreeNode>().ToList();
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
    /// 消费已构建的 subtrees（与 sourcePaths 同序，可含 null）。
    /// </summary>
    private static void BuildSeparateArchivesPreview(
        PreviewTreeNode root,
        IReadOnlyList<string> sourcePaths,
        IReadOnlyList<SourceSubtree?> subtrees,
        IReadOnlyList<CompressPlanItem> planItems,
        string format,
        bool keepOriginalExtension,
        string? fileNameTemplate,
        DateTime? templateTimestamp,
        string? previewStripRoot)
    {
        // 源 → 计划项查找表（B 提供输出包路径）
        var itemBySource = new Dictionary<string, CompressPlanItem>();
        foreach (var item in planItems)
            itemBySource[item.SourcePath] = item;

        // 按输出父目录分组
        var groups = new Dictionary<string, List<(int index, string sourcePath, string archivePath)>>();

        for (int i = 0; i < sourcePaths.Count; i++)
        {
            var path = sourcePaths[i];
            var archivePath = itemBySource.TryGetValue(path, out var item)
                ? item.OutputArchivePath
                : CompressPathPlanner.ComputeOutputPath(
                    path, format, keepOriginalExtension, fileNameTemplate, sequence: i + 1, timestamp: templateTimestamp);
            var parentDir = Path.GetDirectoryName(archivePath) ?? "";

            if (!groups.TryGetValue(parentDir, out var list))
            {
                list = new List<(int, string, string)>();
                groups[parentDir] = list;
            }
            list.Add((i, path, archivePath));
        }

        foreach (var kvp in groups)
        {
            var groupDir = kvp.Key;
            var groupNode = new PreviewTreeNode
            {
                Name = Path.GetFileName(groupDir),
                FullPath = groupDir,
                DisplayLabel = groupDir,
                IsExpanded = true
            };

            foreach (var (index, sourcePath, archivePath) in kvp.Value)
            {
                var archiveName = Path.GetFileName(archivePath);
                var archiveNode = new PreviewTreeNode
                {
                    Name = archiveName,
                    FullPath = archivePath,
                    DisplayLabel = archiveName,
                    IsArchiveNode = true,
                    IsExpanded = true,
                    ExistsAtDestination = File.Exists(archivePath)
                };

                // 挂载已构建的子树（替换原有的 BuildDirectoryNode/文件节点）
                // Separate 下 Wrap 恒不生效，只需处理剥根：目录节点被省掉、内容直接进包。
                foreach (var child in ProjectForRootMode(subtrees[index]?.Node, previewStripRoot, sourcePaths.Count == 1))
                    archiveNode.Children.Add(child);

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

    /// <summary>骨架快照的最小间隔：低于这个间隔不重复深拷贝（大树拷贝本身要花时间）。</summary>
    internal static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(800);

    /// <summary>骨架快照的最小新增文件数：太碎的快照只会让预览树不停重建。</summary>
    internal const int SnapshotMinNewFiles = 300;

    /// <summary>
    /// 骨架构建的累计计数器：递归时**按引用**共享同一个实例。
    /// 若按值传初始值，兄弟目录会各自从同一基数重新累加（a 报 1..3、b 又报 1..2），
    /// 实时进度看起来会「往回跳」。仅由构建线程使用，无需加锁。
    /// </summary>
    private sealed class SkeletonCounter
    {
        private readonly IProgress<SkeletonProgress>? _sink;
        private readonly Action<PreviewTreeNode>? _snapshotSink;
        private readonly System.Diagnostics.Stopwatch _sinceSnapshot = System.Diagnostics.Stopwatch.StartNew();
        private readonly TimeSpan _snapshotInterval;
        private readonly int _snapshotMinNewFiles;
        private int _filesSinceSnapshot;
        private PreviewTreeNode? _root;

        /// <summary>已枚举（计入树）的文件总数。</summary>
        public int FileCount;

        /// <summary>已枚举文件的大小之和（字节）。</summary>
        public long TotalSize;

        public SkeletonCounter(
            IProgress<SkeletonProgress>? sink,
            Action<PreviewTreeNode>? snapshotSink,
            TimeSpan snapshotInterval,
            int snapshotMinNewFiles)
        {
            _sink = sink;
            _snapshotSink = snapshotSink;
            _snapshotInterval = snapshotInterval;
            _snapshotMinNewFiles = snapshotMinNewFiles;
        }

        /// <summary>登记正在生长的树根（只在 depth 0 调用一次），快照与进度文案都挂在它上面。</summary>
        public void AttachRoot(PreviewTreeNode root) => _root = root;

        /// <summary>记入一个文件：上报累计值，并在到达阈值时交出一份当前树的深拷贝。</summary>
        public void AddFile(long size)
        {
            FileCount++;
            TotalSize += size;
            _sink?.Report(new SkeletonProgress(FileCount, TotalSize));

            if (_snapshotSink == null || _root == null) return;
            _filesSinceSnapshot++;
            if (_filesSinceSnapshot < _snapshotMinNewFiles || _sinceSnapshot.Elapsed < _snapshotInterval) return;

            _filesSinceSnapshot = 0;
            _sinceSnapshot.Restart();
            // 深拷贝在构建线程完成：此后 UI 线程只读这份快照，不会碰到还在生长的活树。
            _snapshotSink(CloneForSnapshot(_root, new SkeletonProgress(FileCount, TotalSize)));
        }
    }

    /// <summary>
    /// 深拷贝一棵「还在生长」的树，用于骨架期渐进上屏。
    /// 目录节点标 <see cref="PreviewTreeNode.IsSkeletonPartial"/>（让统计行显示「加载中」而不是最终统计），
    /// 只有根节点带上当前累计进度（子目录行没有各自独立的计数）。
    /// </summary>
    internal static PreviewTreeNode CloneForSnapshot(PreviewTreeNode live, SkeletonProgress progress)
        => CloneSubtree(live, progress);

    /// <summary>递归克隆；<paramref name="progress"/> 只在根节点上非空（子目录行没有各自独立的计数）。</summary>
    private static PreviewTreeNode CloneSubtree(PreviewTreeNode live, SkeletonProgress? progress)
    {
        var clone = live.ShallowClone();
        clone.IsSkeletonPartial = live.IsDirectory;
        clone.Origin = live;
        clone.CurrentSkeletonProgress = progress;
        foreach (var child in live.Children.OfType<PreviewTreeNode>())
            clone.Children.Add(CloneSubtree(child, progress: null));
        return clone;
    }

    /// <summary>
    /// 根据路径构建一个目录节点及其内容的预览树。
    /// </summary>
    /// <param name="rootPath">源根目录绝对路径。</param>
    /// <param name="currentPath">当前目录绝对路径。</param>
    /// <param name="filter">文件过滤条件，不为空且 IsActive 时对文件节点标记 IsFilteredOut。</param>
    /// <param name="includedFiles">匹配过滤条件的文件绝对路径收集器（过滤激活时传入，未匹配则跳过）；null 时不收集。</param>
    /// <param name="maxDepth">最大递归深度（int.MaxValue=全量）。</param>
    /// <param name="maxWidthPerDir">每个目录最多平铺的子项数（int.MaxValue=不限）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="depth">当前递归深度（由调用方控制）。</param>
    /// <param name="skeletonCounter">骨架进度累计器（整个源共享同一实例；null 表示不上报）。</param>
    private static PreviewTreeNode BuildDirectoryNode(
        string rootPath, string currentPath, FileFilterCriteria? filter, List<string>? includedFiles,
        int maxDepth = int.MaxValue, int maxWidthPerDir = int.MaxValue, CancellationToken ct = default, int depth = 0,
        SkeletonCounter? skeletonCounter = null)
    {
        ct.ThrowIfCancellationRequested();

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

        // 树根：让计数器知道该拿谁做快照（只在 depth 0 挂一次）
        if (depth == 0) skeletonCounter?.AttachRoot(node);

        try
        {
            var capped = maxWidthPerDir != int.MaxValue;
            int added = 0;

            // 深度边界：不再下钻子目录（挂占位）；但当前层的文件仍然枚举
            bool atBoundary = depth >= maxDepth;

            // 子目录优先（保证目录结构不被海量文件挤占）
            foreach (var subDir in dirInfo.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                if (capped && added >= maxWidthPerDir) { node.Children.Add(CreateLoadingPlaceholder(node.FullPath)); break; }

                if (atBoundary)
                {
                    // 深度边界：非空子目录挂占位，不再递归
                    if (HasAnyEntry(subDir))
                        node.Children.Add(CreateLoadingPlaceholder(node.FullPath));
                }
                else
                {
                    node.Children.Add(BuildDirectoryNode(rootPath, subDir.FullName, filter, includedFiles, maxDepth, maxWidthPerDir, ct, depth + 1, skeletonCounter));
                }
                added++;
            }

            // 文件（在剩余宽度预算内；深度边界时也枚举当前层文件）
            foreach (var file in dirInfo.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                if (capped && added >= maxWidthPerDir) { node.Children.Add(CreateLoadingPlaceholder(node.FullPath)); break; }
                var fileRelPath = string.IsNullOrEmpty(relativePath) ? file.Name : $"{relativePath}/{file.Name}";
                var isFiltered = filter != null && filter.IsActive && !FileFilterMatcher.IsMatch(filter, file.FullName);
                node.Children.Add(new PreviewTreeNode
                {
                    Name = file.Name,
                    FullPath = fileRelPath,
                    Size = file.Length,
                    SizeDisplay = FormatUtil.FormatSize(file.Length),
                    IsExpanded = false,
                    IsFilteredOut = isFiltered
                });
                if (!isFiltered && includedFiles != null) includedFiles.Add(file.FullName);
                // 骨架进度：每处理一个文件上报一次累计值（被过滤的文件不进树，故不计入）
                if (!isFiltered && skeletonCounter != null) skeletonCounter.AddFile(file.Length);
                added++;
            }
        }
        catch (OperationCanceledException)
        {
            throw; // 取消必须向上传播，不能被"跳过不可访问目录"吞掉
        }
        catch
        {
            // Skip inaccessible directories
        }

        return node;
    }

    private static bool HasAnyEntry(DirectoryInfo dir)
    {
        try { return dir.EnumerateFileSystemInfos().Any(); }
        catch { return false; }
    }

    private static PreviewTreeNode CreateLoadingPlaceholder(string parentFullPath) => new()
    {
        Name = string.Empty,
        FullPath = parentFullPath + "/…",
        IsLoadingPlaceholder = true
    };

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
    /// 目标位置冲突检测：设置树中各节点的 <see cref="PreviewTreeNode.ExistsAtDestination"/>。
    /// 优化：① destDir 不存在则整批跳过；② 被过滤项不检查；③ 自顶向下、父目录不存在则子树短路。
    /// </summary>
    /// <param name="root">预览树根节点。</param>
    /// <param name="destDir">目标解压目录。</param>
    /// <param name="maxDepth">相对 root 的最大检查深度。1=仅直接子项；2=直接子项+孙辈；int.MaxValue=全量。</param>
    /// <param name="ct">取消令牌。</param>
    public static void ApplyConflictMarkers(
        PreviewTreeNode root, string destDir, int maxDepth = int.MaxValue, CancellationToken ct = default)
    {
        // ① 目标根不存在 → 所有 ExistsAtDestination 保持默认 false，零 I/O
        if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
            return;

        MarkConflicts(root, destDir, parentExists: true, depth: 0, maxDepth, ct);
    }

    private static void MarkConflicts(
        PreviewTreeNode node, string destDir, bool parentExists, int depth, int maxDepth, CancellationToken ct)
    {
        if (depth >= maxDepth) return;

        foreach (var child in node.Children.OfType<PreviewTreeNode>())
        {
            ct.ThrowIfCancellationRequested();

            // ② 被过滤项不检查（视图会移除、CountConflicts 也跳过）
            if (child.IsFilteredOut)
            {
                child.ExistsAtDestination = false;
                continue;
            }

            var realPath = Path.Combine(destDir, child.FullPath.Replace('/', Path.DirectorySeparatorChar));

            if (child.IsDirectory)
            {
                // ③ 父不存在 → 子树短路
                child.ExistsAtDestination = parentExists && Directory.Exists(realPath);
                MarkConflicts(child, destDir, child.ExistsAtDestination, depth + 1, maxDepth, ct);
            }
            else
            {
                child.ExistsAtDestination = parentExists && File.Exists(realPath);
            }
        }
    }
}
