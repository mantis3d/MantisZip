using MantisZip.Core.Services;
using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 预览树节点，继承自 <see cref="FolderNode"/>，添加预览专用属性。
/// 用于 ResultTreeView 控件显示解压/压缩结果预览。
/// </summary>
public class PreviewTreeNode : FolderNode
{
    /// <summary>自定义显示名称（截断节点等场景用覆盖默认 Name）。未显式设置时回退到 Name。</summary>
    public string DisplayLabel
    {
        get => string.IsNullOrEmpty(_displayLabel) ? Name : _displayLabel;
        set => _displayLabel = value;
    }
    private string _displayLabel = string.Empty;

    /// <summary>文件大小（字节）。目录时为 0。</summary>
    public long Size { get; set; }

    /// <summary>格式化的文件大小字符串。</summary>
    public string SizeDisplay { get; set; } = string.Empty;

    /// <summary>该节点在目标位置是否已存在同名文件。</summary>
    public bool ExistsAtDestination { get; set; }

    /// <summary>该节点是否被过滤排除（显示为灰色/半透明）。</summary>
    public bool IsFilteredOut { get; set; }

    /// <summary>是否为压缩包节点（显示归档图标）。</summary>
    public bool IsArchiveNode { get; set; }

    /// <summary>是否为目录节点（由构建代码在创建时标记，区别于文件节点）。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>是否为空目录（目录类型且无子节点）。</summary>
    public bool IsEmptyDirectory => IsDirectory && Children.Count == 0;

    /// <summary>缩进深度（0 为顶级）。由 RebuildDisplayTree 的 SetIndentGuides 设置。</summary>
    public int IndentDepth { get; set; }

    /// <summary>每层祖先是否有下一兄弟节点（用于绘制缩进竖线）。</summary>
    public bool[] AncestorHasNextSibling { get; set; } = [];

    /// <summary>子孙节点总数（含所有层级的文件和目录）。</summary>
    public int TotalDescendantCount { get; set; }

    /// <summary>子孙节点文件大小总和（字节）。</summary>
    public long TotalDescendantSize { get; set; }

    /// <summary>子孙最大深度（用于截断判断）。</summary>
    public int MaxChildDepth { get; set; }

    /// <summary>目录统计摘要文本，仅目录节点有值。</summary>
    public string DirectoryInfoText =>
        Children.Count > 0 && !string.IsNullOrEmpty(FullPath)
            ? $"{TotalDescendantCount} 项 · {FormatUtil.FormatSize(TotalDescendantSize)}"
            : string.Empty;

    /// <summary>是否被截断显示（超过 MaxItemsPerDirectory 或 MaxDepth）。</summary>
    public bool IsTruncated { get; set; }

    /// <summary>被截断的额外条目数。</summary>
    public int TruncatedCount { get; set; }

    /// <summary>被截断的额外层数。</summary>
    public int TruncatedDepth { get; set; }

    /// <summary>是否为目录节点。</summary>
    public bool IsDirectoryNode => IsDirectory || Children.Count > 0 || string.IsNullOrEmpty(FullPath);

    /// <summary>图标资源键（IconFolder / IconDocument / IconWarning / IconArchive），用于 PathIcon 绑定。</summary>
    public string? IconKey
    {
        get
        {
            if (IsArchiveNode) return "IconArchive";
            if (IsTruncated) return null;
            if (ExistsAtDestination && !IsDirectory && !string.IsNullOrEmpty(FullPath)) return "IconWarning";
            if (IsEmptyDirectory || Children.Count > 0 || string.IsNullOrEmpty(FullPath)) return "IconFolder";
            return "IconDocument";
        }
    }

    /// <summary>是否为截断节点（显示 … 文本）。</summary>
    public bool IsTruncatedNode => IsTruncated;

    /// <summary>
    /// 节点的前景色不透明度（过滤项半透明，其他正常）。
    /// </summary>
    public double TextOpacity => IsFilteredOut ? 0.4 : 1.0;

    /// <summary>
    /// 深拷贝该节点（仅第一层，不拷贝子节点）。
    /// </summary>
    public PreviewTreeNode ShallowClone()
    {
        return new PreviewTreeNode
        {
            Name = Name,
            FullPath = FullPath,
            DisplayLabel = DisplayLabel,
            Size = Size,
            SizeDisplay = SizeDisplay,
            ExistsAtDestination = ExistsAtDestination,
            IsFilteredOut = IsFilteredOut,
            IsArchiveNode = IsArchiveNode,
            IsDirectory = IsDirectory,
            IndentDepth = IndentDepth,
            AncestorHasNextSibling = AncestorHasNextSibling,
            TotalDescendantCount = TotalDescendantCount,
            TotalDescendantSize = TotalDescendantSize,
            MaxChildDepth = MaxChildDepth,
            IsTruncated = IsTruncated,
            TruncatedCount = TruncatedCount,
            TruncatedDepth = TruncatedDepth,
            IsExpanded = IsExpanded,
            IsSelected = IsSelected
        };
    }
}
