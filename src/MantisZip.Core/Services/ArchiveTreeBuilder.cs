using System.ComponentModel;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Services;

/// <summary>
/// 文件夹树节点。
/// </summary>
public class FolderNode : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Icon => string.IsNullOrEmpty(FullPath) ? "📦" : "📁";
    public List<FolderNode> Children { get; set; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ExpandAll()
    {
        IsExpanded = true;
        foreach (var child in Children)
            child.ExpandAll();
    }

    public void CollapseAll()
    {
        // Keep root expanded
        foreach (var child in Children)
            child.CollapseSelfAndDescendants();
    }

    private void CollapseSelfAndDescendants()
    {
        IsExpanded = false;
        foreach (var child in Children)
            child.CollapseSelfAndDescendants();
    }
}

/// <summary>
/// 从 ArchiveItem 列表构建文件夹树。
/// </summary>
public static class ArchiveTreeBuilder
{
    /// <summary>
    /// 构建文件夹树，返回根节点。
    /// </summary>
    public static FolderNode BuildTree(IEnumerable<ArchiveItem> items, string? rootName = null)
    {
        var root = new FolderNode { Name = rootName ?? "压缩包", FullPath = "" };
        var dirsAdded = new HashSet<string>();

        // 先从目录条目构建
        foreach (var item in items.Where(i => i.IsDirectory))
        {
            var path = item.FullPath.TrimEnd('/');
            if (dirsAdded.Add(path))
                AddFolderNode(root.Children, path.Split('/'), 0, path);
        }

        // 再从文件路径推断缺失的中间目录
        foreach (var item in items.Where(i => !i.IsDirectory))
        {
            var fullPath = item.FullPath;
            var lastSlash = fullPath.LastIndexOf('/');
            while (lastSlash >= 0)
            {
                var dirPath = fullPath[..lastSlash];
                if (dirsAdded.Add(dirPath))
                    AddFolderNode(root.Children, dirPath.Split('/'), 0, dirPath);
                lastSlash = dirPath.LastIndexOf('/');
            }
        }

        return root;
    }

    private static void AddFolderNode(List<FolderNode> nodes, string[] parts, int index, string fullPath)
    {
        if (index >= parts.Length) return;
        var name = parts[index];
        var currentPath = string.Join("/", parts.Take(index + 1));
        var existing = nodes.FirstOrDefault(n => n.FullPath == currentPath);
        if (existing == null)
        {
            existing = new FolderNode { Name = name, FullPath = currentPath };
            nodes.Add(existing);
        }
        if (index < parts.Length - 1)
            AddFolderNode(existing.Children, parts, index + 1, fullPath);
    }
}
