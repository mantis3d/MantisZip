using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class MainWindow
{
    #region UI 辅助

    private void SetStatus(string text) => StatusText.Text = text;

    private void ShowProgress(bool show)
    {
        ProgressBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ProgressText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private string? ResolveExtractDestination(string archivePath)
        => App.ResolveExtractDestinationStatic(archivePath, AppSettings.Instance);

    private void UpdatePasswordStatus()
    {
        if (string.IsNullOrEmpty(_currentArchivePath))
        {
            PasswordStatusText.Text = "";
            UpdateEnterPasswordBtnState();
            return;
        }
        if (!_hasEncryptedArchive)
        {
            PasswordStatusText.Text = "";
            UpdateEnterPasswordBtnState();
            return;
        }
        PasswordStatusText.Text = _currentPassword != null ? L.T(L.Main_PwdMatchedIndicator) : L.T(L.Main_IsEncrypted);
        UpdateEnterPasswordBtnState();
    }

    private void UpdateEnterPasswordBtnState()
    {
        EnterPasswordBtn.IsEnabled = !string.IsNullOrEmpty(_currentArchivePath)
            && _hasEncryptedArchive && _currentPassword == null;
    }

    private static void OpenInExplorer(string path) => App.OpenInExplorerStatic(path);

    private string FormatSize(long bytes) => ArchiveItem.FormatSize(bytes);

    private void BuildFolderTree()
    {
        var root = new FolderNode { Name = L.T(L.Main_RootNode), FullPath = "" };
        var dirsAdded = new HashSet<string>();
        foreach (var item in _allItems.Where(i => i.IsDirectory))
        {
            var path = item.FullPath.TrimEnd('/');
            if (dirsAdded.Add(path)) AddFolderNode(root.Children, path.Split('/'), 0, path);
        }
        foreach (var item in _allItems.Where(i => !i.IsDirectory))
        {
            var fullPath = item.FullPath;
            var lastSlash = fullPath.LastIndexOf('/');
            while (lastSlash >= 0)
            {
                var dirPath = fullPath[..lastSlash];
                if (dirsAdded.Add(dirPath)) AddFolderNode(root.Children, dirPath.Split('/'), 0, dirPath);
                lastSlash = dirPath.LastIndexOf('/');
            }
        }
        FolderTree.ItemsSource = new List<FolderNode> { root };
        Dispatcher.BeginInvoke(new Action(() => { root.IsExpanded = true; root.IsSelected = true; }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void AddFolderNode(List<FolderNode> nodes, string[] parts, int index, string fullPath)
    {
        if (index >= parts.Length) return;
        var name = parts[index];
        var currentPath = string.Join("/", parts.Take(index + 1));
        var existing = nodes.FirstOrDefault(n => n.FullPath == currentPath);
        if (existing == null)
        {
            existing = new FolderNode { Name = "📁 " + name, FullPath = currentPath };
            nodes.Add(existing);
        }
        if (index < parts.Length - 1) AddFolderNode(existing.Children, parts, index + 1, fullPath);
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (FolderTree.SelectedItem is FolderNode node) FilterFiles(node.FullPath);
    }

    private void FilterFiles(string folderPath)
    {
        _isProgrammaticFilter = true;
        try
        {
            _currentFolder = folderPath;
            var directItems = new List<ArchiveItem>();
            var implicitDirs = new HashSet<string>();
            string prefix = string.IsNullOrEmpty(folderPath) ? "" : folderPath + "/";

            foreach (var item in _allItems)
            {
                if (string.IsNullOrEmpty(folderPath))
                {
                    if (!item.Name.Contains("/")) { directItems.Add(item); continue; }
                    var firstSlash = item.Name.IndexOf('/');
                    var dirName = item.Name[..firstSlash];
                    // 记录此目录已存在，后续同名目录不再重复添加
                    if (!implicitDirs.Add(dirName)) continue;
                    // 此条目本身就是该目录的显式条目 → 直接添加
                    if (item.IsDirectory && item.FullPath == dirName)
                    { directItems.Add(item); continue; }
                    // 否则合成一个目录条目
                    directItems.Add(new ArchiveItem { Name = dirName + "/", FullPath = dirName, Size = 0, IsDirectory = true, IconSource = SystemIconHelper.GetFolderIcon() });
                }
                else
                {
                    if (!item.Name.StartsWith(prefix)) continue;
                    if (item.FullPath == folderPath) continue;
                    var rest = item.Name[prefix.Length..].TrimEnd('/');
                    var restParts = rest.Split('/');
                    if (restParts.Length == 1)
                    {
                        // 显式的文件或目录
                        directItems.Add(item);
                        if (item.IsDirectory) implicitDirs.Add(restParts[0]);
                    }
                    else
                    {
                        var subDir = restParts[0];
                        if (implicitDirs.Add(subDir))
                        {
                            var subDirFullPath = folderPath + "/" + subDir;
                            directItems.Add(new ArchiveItem { Name = subDirFullPath + "/", FullPath = subDirFullPath, Size = 0, IsDirectory = true, IconSource = SystemIconHelper.GetFolderIcon() });
                        }
                    }
                }
            }

            // 通用去重：相同 FullPath 的条目只保留第一个（防止显式+隐式重复）
            var seen = new HashSet<string>();
            var deduped = new List<ArchiveItem>();
            foreach (var item in directItems)
            {
                if (seen.Add(item.FullPath)) deduped.Add(item);
            }

            // 为目录条目填充统计信息（文件数、总大小、压缩后大小）
            foreach (var item in deduped)
            {
                if (!item.IsDirectory) continue;
                if (_dirStats.TryGetValue(item.FullPath, out var stat))
                {
                    item.Size = stat.Size;
                    item.CompressedSize = stat.CompressedSize;
                }
                else
                {
                    item.Size = 0;
                    item.CompressedSize = 0;
                }
            }

            var sortedItems = deduped.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToList();
            foreach (var item in sortedItems)
            {
                item.DisplayName = string.IsNullOrEmpty(folderPath)
                    ? item.Name.TrimEnd('/')
                    : item.Name.StartsWith(prefix) ? item.Name[prefix.Length..].TrimEnd('/') : item.Name;
            }
            FileListGrid.ItemsSource = sortedItems;
            FileListGrid.Items.Refresh();
            var fileCount = sortedItems.Count(i => !i.IsDirectory);
            var dirCount = sortedItems.Count(i => i.IsDirectory);
            DirStatsText.Text = L.TF(L.Main_DirStats, sortedItems.Count, fileCount, dirCount);
        }
        finally { _isProgrammaticFilter = false; }

        // 过滤后无选中项 → 显示压缩包总览
        if (FileListGrid.SelectedItems.Count == 0 && !string.IsNullOrEmpty(_currentArchivePath))
            ShowArchiveInfo();
    }

    private void FileListGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListGrid.SelectedItem is ArchiveItem item && item.IsDirectory)
        {
            FilterFiles(item.FullPath);
            SelectFolderInTree(item.FullPath);
            e.Handled = true;
        }
    }

    private async void FileListGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_isProgrammaticFilter) { UpdateSelectionStats(); return; }

            // 选择为空时显示压缩包总览
            if (FileListGrid.SelectedItems.Count == 0)
            {
                if (!string.IsNullOrEmpty(_currentArchivePath))
                    ShowArchiveInfo();
                UpdateSelectionStats();
                return;
            }

            ArchiveItem? lastClicked = e.AddedItems.Count > 0
                ? e.AddedItems[e.AddedItems.Count - 1] as ArchiveItem
                : e.RemovedItems.Count == 1 ? e.RemovedItems[0] as ArchiveItem : null;

            if (lastClicked != null && !string.IsNullOrEmpty(_currentArchivePath))
            {
                if (lastClicked.IsDirectory) ShowDirectoryPreview(lastClicked);
                else await ShowPreviewAsync(lastClicked);
            }
            UpdateSelectionStats();
        }
        catch (Exception ex)
        {
            App.LogDebug("FileListGrid_SelectionChanged: unexpected error: {0}", ex.Message);
        }
    }

    private void UpdateSelectionStats()
    {
        if (string.IsNullOrEmpty(_currentArchivePath)) { SelectionStatsText.Text = ""; return; }
        var count = FileListGrid.SelectedItems.Count;
        if (count == 0) { SelectionStatsText.Text = ""; return; }
        if (count == 1 && FileListGrid.SelectedItems[0] is ArchiveItem single)
        {
            SelectionStatsText.Text = single.IsDirectory
                ? $"📁 {single.Name.TrimEnd('/')}"
                : $"📄 {single.Name} ({FormatSize(single.Size)})";
            return;
        }
        int fileCount = 0, dirCount = 0; long totalSize = 0;
        foreach (ArchiveItem ai in FileListGrid.SelectedItems)
        {
            if (ai.IsDirectory) dirCount++;
            else { fileCount++; totalSize += ai.Size; }
        }
        SelectionStatsText.Text = L.TF(L.Main_SelectionStats, count, fileCount, dirCount, FormatSize(totalSize));
    }

    private void SelectFolderInTree(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (FolderTree.Items.Count == 0) return;
        var root = FolderTree.Items[0] as FolderNode;
        if (root == null) return;
        var target = FindFolderNode(root, path);
        if (target == null) return;
        ExpandAncestors(root, target);
        target.IsSelected = true;
    }

    private static FolderNode? FindFolderNode(FolderNode node, string targetPath)
    {
        if (node.FullPath == targetPath) return node;
        return node.Children.Select(c => FindFolderNode(c, targetPath)).FirstOrDefault(f => f != null);
    }

    private static void ExpandAncestors(FolderNode current, FolderNode target)
    {
        if (current == target) return;
        foreach (var child in current.Children)
        {
            if (ContainsNode(child, target)) { child.IsExpanded = true; ExpandAncestors(child, target); return; }
        }
    }

    private static bool ContainsNode(FolderNode parent, FolderNode target)
    {
        if (parent == target) return true;
        return parent.Children.Any(c => ContainsNode(c, target));
    }

    #endregion
}

/// <summary>
/// 文件夹树节点
/// </summary>
public class FolderNode : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public List<FolderNode> Children { get; set; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); } }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ArchiveItem : Core.Abstractions.ArchiveItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string NameForSort { get; set; } = string.Empty;
    public ImageSource? IconSource { get; set; }

    public string SizeDisplay => FormatSize(Size);
    public string CompressedSizeDisplay => FormatSize(CompressedSize);

    public string NameDisplay
    {
        get { return !string.IsNullOrEmpty(DisplayName) ? DisplayName : Name; }
    }

    public int SortOrder => IsDirectory ? 0 : 1;

    public string DateDisplay => LastModified > DateTime.MinValue
        ? LastModified.ToString("yyyy-MM-dd HH:mm")
        : "---";

    internal static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }
}
