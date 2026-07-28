using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// 可复用的结果预览树控件。
/// 显示压缩/解压后的文件目录树，支持精简/完整模式切换、冲突标记、过滤项灰显。
/// </summary>
public partial class ResultTreeView : UserControl
{
    /// <summary>原始树的根节点（未应用显示规则）。</summary>
    private PreviewTreeNode? _originalRoot;

    // ── StyledProperties ──

    /// <summary>根节点（树的起点，含所有子节点）。</summary>
    public static readonly StyledProperty<PreviewTreeNode?> RootProperty =
        AvaloniaProperty.Register<ResultTreeView, PreviewTreeNode?>(nameof(Root));

    /// <summary>每个目录最多平铺文件数（超出的折叠成 … 还有 N 个）。</summary>
    public static readonly StyledProperty<int> MaxItemsPerDirectoryProperty =
        AvaloniaProperty.Register<ResultTreeView, int>(nameof(MaxItemsPerDirectory), 5);

    /// <summary>最大显示深度（超出的折叠成 … 还有 N 层）。</summary>
    public static readonly StyledProperty<int> MaxDepthProperty =
        AvaloniaProperty.Register<ResultTreeView, int>(nameof(MaxDepth), 5);

    /// <summary>是否启用精简模式。</summary>
    public static readonly StyledProperty<bool> CompactModeProperty =
        AvaloniaProperty.Register<ResultTreeView, bool>(nameof(CompactMode), true);

    /// <summary>是否显示被过滤排除的文件（灰色显示）。</summary>
    public static readonly StyledProperty<bool> ShowFilteredGhostsProperty =
        AvaloniaProperty.Register<ResultTreeView, bool>(nameof(ShowFilteredGhosts), false);

    /// <summary>摘要文本（由控件内部计算）。</summary>
    public static readonly StyledProperty<string> SummaryTextProperty =
        AvaloniaProperty.Register<ResultTreeView, string>(nameof(SummaryText), "");

    /// <summary>是否显示摘要栏。</summary>
    public static readonly StyledProperty<bool> ShowSummaryBarProperty =
        AvaloniaProperty.Register<ResultTreeView, bool>(nameof(ShowSummaryBar), true);

    // ── Observable collection for display tree ──

    /// <summary>显示树节点集合（已应用精简/过滤规则）。</summary>
    public ObservableCollection<PreviewTreeNode> DisplayNodes { get; } = new();

    // ── .NET Properties ──

    public PreviewTreeNode? Root
    {
        get => GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public int MaxItemsPerDirectory
    {
        get => GetValue(MaxItemsPerDirectoryProperty);
        set => SetValue(MaxItemsPerDirectoryProperty, value);
    }

    public int MaxDepth
    {
        get => GetValue(MaxDepthProperty);
        set => SetValue(MaxDepthProperty, value);
    }

    public bool CompactMode
    {
        get => GetValue(CompactModeProperty);
        set => SetValue(CompactModeProperty, value);
    }

    public bool ShowFilteredGhosts
    {
        get => GetValue(ShowFilteredGhostsProperty);
        set => SetValue(ShowFilteredGhostsProperty, value);
    }

    public string SummaryText
    {
        get => GetValue(SummaryTextProperty);
        set => SetValue(SummaryTextProperty, value);
    }

    public bool ShowSummaryBar
    {
        get => GetValue(ShowSummaryBarProperty);
        set => SetValue(ShowSummaryBarProperty, value);
    }

    /// <summary>
    /// 静态构造函数：注册属性变更回调。
    /// </summary>
    static ResultTreeView()
    {
        RootProperty.Changed.AddClassHandler<ResultTreeView>((view, e) =>
            view.OnRootChanged(e.NewValue as PreviewTreeNode));
        CompactModeProperty.Changed.AddClassHandler<ResultTreeView>((view, _) =>
            view.RebuildDisplayTree());
        MaxItemsPerDirectoryProperty.Changed.AddClassHandler<ResultTreeView>((view, _) =>
            view.RebuildDisplayTree());
        MaxDepthProperty.Changed.AddClassHandler<ResultTreeView>((view, _) =>
            view.RebuildDisplayTree());
        ShowFilteredGhostsProperty.Changed.AddClassHandler<ResultTreeView>((view, _) =>
            view.RebuildDisplayTree());
    }

    public ResultTreeView()
    {
        InitializeComponent();
        ToolTip.SetTip(LocateButton, LocalizationManager.T("Preview_Result_Locate"));
        ToolTip.SetTip(FilterToggle, LocalizationManager.T("Preview_Result_ShowFiltered"));
    }

    /// <summary>
    /// 强制刷新显示树（展开状态等非 Root 变更场景）。
    /// </summary>
    public void RefreshDisplay() => RebuildDisplayTree();

    /// <summary>
    /// 当 Root 属性变化时，保存原始树引用并重建显示树。
    /// </summary>
    private void OnRootChanged(PreviewTreeNode? root)
    {
        _originalRoot = root;
        RebuildDisplayTree();
    }

    /// <summary>
    /// 重建显示树，应用精简/过滤规则。
    /// 重建前后自动保存/恢复用户展开状态（从 DisplayNodes 中读取，因为用户操作的是克隆体）。
    /// </summary>
    private void RebuildDisplayTree()
    {
        // 保存当前显示树的展开状态（用户手动展开的节点）
        var expandedPaths = new HashSet<string>();
        foreach (var node in DisplayNodes)
            CollectExpandedPaths(node, expandedPaths);

        DisplayNodes.Clear();

        if (_originalRoot == null)
            return;

        // Deep-clone the original tree and apply display rules
        var displayRoot = DeepCloneNode(_originalRoot);
        ApplyDisplayRules(displayRoot, 0);

        // 恢复展开状态
        RestoreExpandedPaths(displayRoot, expandedPaths);

        // 虚拟根节点（空 DisplayLabel）不显示自身，其子节点直接作为顶级项
        if (string.IsNullOrEmpty(displayRoot.DisplayLabel))
        {
            foreach (var child in displayRoot.Children.OfType<PreviewTreeNode>())
                DisplayNodes.Add(child);
            UpdateSummaryMulti(DisplayNodes);
        }
        else
        {
            DisplayNodes.Add(displayRoot);
            UpdateSummary(displayRoot);
        }

        UpdateConflictCount();
    }

    private static void CollectExpandedPaths(PreviewTreeNode node, HashSet<string> paths)
    {
        if (node.IsExpanded) paths.Add(node.FullPath);
        foreach (var child in node.Children.OfType<PreviewTreeNode>())
            CollectExpandedPaths(child, paths);
    }

    private static void RestoreExpandedPaths(PreviewTreeNode root, HashSet<string> paths)
    {
        if (paths.Contains(root.FullPath))
            root.IsExpanded = true;
        foreach (var child in root.Children.OfType<PreviewTreeNode>())
            RestoreExpandedPaths(child, paths);
    }

    /// <summary>
    /// 深拷贝整个树（递归）。
    /// </summary>
    private static PreviewTreeNode DeepCloneNode(PreviewTreeNode source)
    {
        var clone = source.ShallowClone();
        foreach (var child in source.Children)
        {
            if (child is PreviewTreeNode previewChild)
            {
                clone.Children.Add(DeepCloneNode(previewChild));
            }
        }
        return clone;
    }

    /// <summary>
    /// 递归应用显示规则：先移除过滤项（不占截断计数），再深度/数量截断。
    /// </summary>
    private void ApplyDisplayRules(PreviewTreeNode node, int depth)
    {
        if (!CompactMode)
            return; // Full mode: show everything as-is

        // 0. 先移除过滤项，保证它们不占用截断名额
        if (!ShowFilteredGhosts)
        {
            node.Children = node.Children.Where(c => !(c is PreviewTreeNode pt && pt.IsFilteredOut)).ToList();
        }

        // 1. 深度截断 (CompactMode 且超过 MaxDepth)
        if (depth >= MaxDepth && node.Children.Count > 0)
        {
            var totalDeep = CountDeepDescendants(node);
            var totalFiles = CountTotalFiles(node);

            node.Children.Clear();
            var depthLabel = LocalizationManager.T("Preview_Result_TruncatedDepth", totalDeep);
            node.Children.Add(new PreviewTreeNode
            {
                Name = depthLabel,
                DisplayLabel = depthLabel,
                IsTruncated = true,
                TruncatedDepth = totalDeep,
                FullPath = node.FullPath + "/..."
            });
            return;
        }

        // 2. 文件数截断 (CompactMode 且子节点超过 MaxItemsPerDirectory)
        if (node.Children.Count > MaxItemsPerDirectory)
        {
            var excess = node.Children.Count - MaxItemsPerDirectory;
            var truncated = node.Children.Skip(MaxItemsPerDirectory).ToList();

            node.Children = node.Children.Take(MaxItemsPerDirectory).ToList();

            var extraFiles = truncated.Count(c => c is PreviewTreeNode pt && pt.Children.Count == 0);
            var extraDirs = truncated.Count(c => c is PreviewTreeNode pt && pt.Children.Count > 0);

            var label = extraDirs > 0
                ? LocalizationManager.T("Preview_Result_TruncatedMixed", excess, extraDirs, extraFiles)
                : LocalizationManager.T("Preview_Result_TruncatedItems", excess);

            node.Children.Add(new PreviewTreeNode
            {
                Name = label,
                DisplayLabel = label,
                IsTruncated = true,
                TruncatedCount = excess,
                FullPath = node.FullPath + "/..."
            });
        }

        // 3. 递归处理子节点
        foreach (var child in node.Children.ToList())
        {
            if (child is PreviewTreeNode pt && !pt.IsTruncated)
            {
                ApplyDisplayRules(pt, depth + 1);
            }
        }
    }

    /// <summary>
    /// 计算深层子孙总数（跳过中间层级的计数）。
    /// </summary>
    private static int CountDeepDescendants(PreviewTreeNode node)
    {
        int count = 0;
        foreach (var child in node.Children)
        {
            count++; // count the child
            if (child is PreviewTreeNode pt)
            {
                count += CountDeepDescendants(pt);
            }
        }
        return count;
    }

    /// <summary>
    /// 统计节点下所有文件（非目录项）的数量。
    /// </summary>
    private static int CountTotalFiles(PreviewTreeNode node)
    {
        int files = 0;
        foreach (var child in node.Children)
        {
            if (child is PreviewTreeNode pt)
            {
                if (pt.Children.Count == 0 && !pt.IsTruncated)
                    files++;
                files += CountTotalFiles(pt);
            }
        }
        return files;
    }

    /// <summary>
    /// 统计总文件数和总大小，更新摘要文本。
    /// </summary>
    private void UpdateSummary(PreviewTreeNode root)
    {
        var totalFiles = CountTotalFiles(root);
        var totalSize = CalculateTotalSize(root);

        SummaryText = LocalizationManager.T("Preview_Result_Summary", totalFiles, FormatSize(totalSize));

        // Also update individual text blocks
        if (FileCountText != null)
            FileCountText.Text = $"{totalFiles} 个文件";
        if (TotalSizeText != null)
            TotalSizeText.Text = FormatSize(totalSize);
    }

    /// <summary>
    /// 针对多个顶级项汇总统计（虚拟根节点场景）。
    /// </summary>
    private void UpdateSummaryMulti(IEnumerable<PreviewTreeNode> roots)
    {
        int totalFiles = 0;
        long totalSize = 0;
        foreach (var node in roots)
        {
            totalFiles += CountTotalFiles(node);
            totalSize += CalculateTotalSize(node);
        }

        SummaryText = LocalizationManager.T("Preview_Result_Summary", totalFiles, FormatSize(totalSize));

        if (FileCountText != null)
            FileCountText.Text = $"{totalFiles} 个文件";
        if (TotalSizeText != null)
            TotalSizeText.Text = FormatSize(totalSize);
    }

    /// <summary>
    /// 统计冲突文件数并更新 UI。
    /// </summary>
    private void UpdateConflictCount()
    {
        if (_originalRoot == null) return;

        var conflictCount = CountConflicts(_originalRoot);
        if (ConflictCountText != null)
        {
            if (conflictCount > 0)
            {
                ConflictCountText.Text = $"⚠️ {conflictCount} 个冲突";
                ConflictCountText.IsVisible = true;
            }
            else
            {
                ConflictCountText.IsVisible = false;
            }
        }
    }

    /// <summary>
    /// 递归统计所有 ExistsAtDestination=true 的节点。
    /// </summary>
    private static int CountConflicts(PreviewTreeNode node)
    {
        int count = node.ExistsAtDestination ? 1 : 0;
        foreach (var child in node.Children)
        {
            if (child is PreviewTreeNode pt)
                count += CountConflicts(pt);
        }
        return count;
    }

    /// <summary>
    /// 计算节点下所有文件的总大小。
    /// </summary>
    private static long CalculateTotalSize(PreviewTreeNode node)
    {
        long size = node.Size;
        foreach (var child in node.Children)
        {
            if (child is PreviewTreeNode pt)
                size += CalculateTotalSize(pt);
        }
        return size;
    }

    /// <summary>
    /// Compact/Full 模式切换按钮事件。
    /// </summary>
    private void OnCompactToggleChanged(object? sender, RoutedEventArgs e)
    {
        CompactMode = CompactToggle?.IsChecked ?? true;
        RebuildDisplayTree();
    }

    private static string FormatSize(long bytes) => Core.Utils.FormatUtil.FormatSize(bytes);

    private void OnExpandAllClick(object? sender, RoutedEventArgs e)
    {
        if (_originalRoot == null) return;
        _originalRoot.ExpandAll();
        RebuildDisplayTree();
    }

    /// <summary>
    /// 树选中项变化时更新定位按钮状态。
    /// </summary>
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LocateButton != null)
            LocateButton.IsEnabled = PreviewTreeView?.SelectedItems?.Count > 0;
    }

    /// <summary>
    /// 定位到选中项：折叠全部，然后展开所有选中项的祖先路径。
    /// </summary>
    private void OnLocateClick(object? sender, RoutedEventArgs e)
    {
        var displayRoot = DisplayNodes.FirstOrDefault();
        if (displayRoot == null || PreviewTreeView?.SelectedItems == null) return;

        // 1. 折叠全部，保留根展开
        displayRoot.CollapseAll();
        displayRoot.IsExpanded = true;

        // 2. 展开每个选中项的祖先路径
        foreach (var item in PreviewTreeView.SelectedItems)
        {
            if (item is PreviewTreeNode pt && !string.IsNullOrEmpty(pt.FullPath))
                ExpandAncestors(displayRoot, pt.FullPath);
        }
    }

    /// <summary>
    /// 从根节点开始，按 FullPath 的分段逐层展开祖先节点。
    /// 如果某层节点因截断不存在则停止（不报错）。
    /// </summary>
    private static void ExpandAncestors(PreviewTreeNode root, string fullPath)
    {
        var parts = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var part in parts)
        {
            var child = current.Children
                .OfType<PreviewTreeNode>()
                .FirstOrDefault(c => c.Name == part && !c.IsTruncated);
            if (child == null) break;
            child.IsExpanded = true;
            current = child;
        }
    }
}
