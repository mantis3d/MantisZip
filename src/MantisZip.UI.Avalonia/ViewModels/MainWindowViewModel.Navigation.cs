using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Threading;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// MainWindowViewModel 的 导航/过滤部分（自 MainWindowViewModel.cs 拆分，纯移动）。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// 统一导航入口：记录历史，更新 CurrentFolder，刷新条目列表，同步目录树选择。
    /// </summary>
    private void NavigateAndPushHistory(string path)
    {
        if (_allRawItems == null) return;
        if (CurrentFolder == path) return;

        if (CurrentFolder != null)
            _backStack.Add(CurrentFolder);
        _forwardStack.Clear();

        CurrentFolder = path;
        PopulateEntries();
        UpdatePreviewForFolder();

        var node = FindNode(FolderTreeRoot, path);
        if (node != null)
            SelectedFolder = node;

        NotifyNavigationState();
    }

    private void NavigateToFolder(FolderNode node)
    {
        if (_allRawItems == null) return;
        // OnSelectedFolderChanged calls NavigateToFolder — we push history here
        // but skip push if already at the same folder
        if (CurrentFolder == node.FullPath) return;

        if (CurrentFolder != null)
            _backStack.Add(CurrentFolder);
        _forwardStack.Clear();

        CurrentFolder = node.FullPath;
        PopulateEntries();
        UpdatePreviewForFolder();

        NotifyNavigationState();
    }

    /// <summary>
    /// 目录导航后同步预览区：根目录且有压缩包注释时展示注释；
    /// 否则若文件列表无选中项则清空预览（避免残留上个目录的内容）。
    /// 注释展示覆盖一切（文件列表选中条目时由 ShowPreviewAsync 覆盖注释）。
    /// </summary>
    private void UpdatePreviewForFolder()
    {
        if (string.IsNullOrEmpty(CurrentFolder) && !string.IsNullOrEmpty(_archiveComment))
        {
            Preview.ShowArchiveComment(_archiveComment,
                Path.GetFileName(CurrentArchivePath ?? string.Empty));
            return;
        }

        if (SelectedEntry == null)
        {
            Preview.Clear();
        }
    }

    /// <summary>
    /// 从文件列表双击目录时按路径导航（无需 FolderNode）。
    /// </summary>
    public void NavigateToFolderPath(string path)
    {
        if (_allRawItems == null) return;

        if (CurrentFolder != null)
            _backStack.Add(CurrentFolder);
        _forwardStack.Clear();

        _isProgrammaticFilter = true;
        try
        {
            CurrentFolder = path;
            PopulateEntries();
            UpdatePreviewForFolder();
            // 同步选中目录树中的对应节点
            var node = FindNode(FolderTreeRoot, path);
            if (node != null)
                SelectedFolder = node;

            NotifyNavigationState();
        }
        finally
        {
            _isProgrammaticFilter = false;
        }
    }

    /// <summary>
    /// 应用过滤条件并刷新当前目录的条目显示。
    /// </summary>
    private void FilterFiles() => PopulateEntries();

    /// <summary>
    /// 根据当前过滤条件 + 当前文件夹刷新 CurrentEntries。
    /// </summary>
    private void PopulateEntries()
    {
        if (_allRawItems == null) return;

        _isProgrammaticFilter = true;
        try
        {
            var filteredSource = GetFilteredSource();
            var entries = ArchiveEntryLister.GetEntriesInFolder(
                filteredSource, CurrentFolder ?? "", ShowSubfolders);

            // 计算目录聚合统计（基于过滤后的数据源，与显示一致）：
            // 目录大小 = 子树内所有文件大小之和，日期 = 子树内最新文件修改时间
            var dirStats = ArchiveEntryLister.ComputeDirectoryStats(filteredSource);

            // Apply view mode filter
            if (ViewMode == FileListViewMode.FilesOnly)
                entries = entries.Where(e => !e.IsDirectory).ToList();
            else if (ViewMode == FileListViewMode.DirectoriesOnly)
                entries = entries.Where(e => e.IsDirectory).ToList();

            var compressedSizeAvailable = GetCompressedSizeAvailable(_currentFormat);

            CurrentEntries.Clear();
            foreach (var item in entries)
            {
                var model = ArchiveItemModel.FromCore(item);
                if (model.IsDirectory)
                {
                    // 应用目录聚合：大小 = 子树和，日期 = 子树最新，压缩后大小 = 子树和
                    if (dirStats.TryGetValue(model.FullPath, out var stat))
                    {
                        model.Size = stat.Size;
                        model.CompressedSize = stat.CompressedSize;
                        model.LastModified = stat.NewestModified;
                    }
                    model.IconSource = IconService.GetFolderIcon();
                }
                else
                {
                    var ext = Path.GetExtension(model.Name);
                    model.IconSource = IconService.GetFileIcon(ext);
                }
                // 按当前格式设置压缩后大小可用性（文件 + 目录一致）
                model.CompressedSizeAvailable = compressedSizeAvailable;
                model.ProgressBarEnabled = ShowProgressBars;
                CurrentEntries.Add(model);
            }

            // Populate DirStats
            var dirCount = CurrentEntries.Count(e => e.IsDirectory);
            var fileCount = CurrentEntries.Count - dirCount;
            DirStats = $"{dirCount} dirs, {fileCount} files";

            // Populate FilterStats — only show when filter bar is open AND conditions are active
            if (IsFilterBarVisible &&
                (!string.IsNullOrWhiteSpace(FilterText) || !string.IsNullOrWhiteSpace(FilterExcludeText) ||
                 FilterDateFrom.HasValue || FilterDateTo.HasValue ||
                 FilterSizeMin.HasValue || FilterSizeMax.HasValue))
            {
                var totalItems = _allRawItems?.Count ?? 0;
                FilterStats = $"{CurrentEntries.Count}/{totalItems}";
            }
            else
            {
                FilterStats = string.Empty;
            }

            // Compute progress bar ratios
            if (ShowProgressBars)
            {
                ComputeProgressBarRatios();
            }

            // 条目重填完成，通知视图重应用列排序
            EntriesRefreshed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isProgrammaticFilter = false;
        }
    }

    /// <summary>
    /// 当前格式是否能提供逐项压缩后大小。
    /// Zip 可用；ISO/.tar 未压缩（引擎以原大小填充，等价可用）；
    /// .tgz/.tar.gz/.gz 有压缩但无法获得逐项压缩后大小（不可用，压缩后大小列显示空）；
    /// 7z/RAR 同样不可用。与 WPF 版 GetCompressedDisplayMode 语义对齐。
    /// </summary>
    private bool GetCompressedSizeAvailable(ArchiveFormat format)
    {
        if (format == ArchiveFormat.Zip) return true;
        if (format == ArchiveFormat.Iso) return true;
        if (format == ArchiveFormat.Tar)
        {
            // .tar 是未压缩的容器格式；.tgz/.tar.gz/.gz 有压缩但引擎只填原大小（TarGzEngine ListEntriesAsync
            // 对所有 tar 系条目填 CompressedSize = Size），视为不可用以免显示误导性的"压缩后=原大小"
            var ext = Path.GetExtension(CurrentArchivePath ?? string.Empty).ToLowerInvariant();
            return ext == ".tar";
        }
        return false; // 7z, RAR, .tgz/.tar.gz/.gz
    }

    /// <summary>
    /// 清零所有条目的数据条比例（关闭「数据条」开关时调用）。
    /// 数据条 Rectangle 的宽度绑定这些比例 → 归零即隐藏（等价 WPF 版切换开关后
    /// 重填当前目录、比例归零的效果，但保留当前条目与选中项）。
    /// </summary>
    private void ClearProgressBarRatios()
    {
        foreach (var item in CurrentEntries)
        {
            item.SizeRatio = 0;
            item.CompressedSizeRatio = 0;
            item.DateRatio = 0;
            item.RatioBarValue = 0;
        }
    }

    /// <summary>
    /// 计算所有条目的数据条比例值（相对大小、压缩比、日期分布等）。
    /// 基准语义与 WPF 版 FilterFiles 对齐：
    /// - 目录独立基准开：文件组、目录组各自取组内最大值做基准，两组各有满格代表；
    /// - 关：全部条目（含目录）统一取最大值做基准。
    /// </summary>
    private void ComputeProgressBarRatios()
    {
        if (CurrentEntries.Count == 0) return;

        long maxSize, maxCompressed;
        long maxDirSize = 0, maxDirCompressed = 0;

        if (SeparateDirBaseline)
        {
            var files = CurrentEntries.Where(e => !e.IsDirectory).ToList();
            var dirs = CurrentEntries.Where(e => e.IsDirectory).ToList();
            maxSize = files.Count > 0 ? files.Max(e => (long)e.Size) : 0;
            maxCompressed = files.Count > 0 ? files.Max(e => (long)e.CompressedSize) : 0;
            maxDirSize = dirs.Count > 0 ? dirs.Max(e => (long)e.Size) : 0;
            maxDirCompressed = dirs.Count > 0 ? dirs.Max(e => (long)e.CompressedSize) : 0;
        }
        else
        {
            maxSize = CurrentEntries.Max(e => (long)e.Size);
            maxCompressed = CurrentEntries.Max(e => (long)e.CompressedSize);
        }

        // 日期条与 WPF 一致：全部条目（含目录）参与 min/max；仅一条有效日期时满格
        var datedItems = CurrentEntries.Where(e => e.LastModified > DateTime.MinValue).ToList();
        var minDate = datedItems.Count > 0 ? datedItems.Min(e => e.LastModified) : DateTime.Now;
        var maxDate = datedItems.Count > 0 ? datedItems.Max(e => e.LastModified) : DateTime.Now;
        var dateRange = (maxDate - minDate).TotalSeconds;

        foreach (var item in CurrentEntries)
        {
            bool useDirBase = SeparateDirBaseline && item.IsDirectory;
            long baseSize = useDirBase ? maxDirSize : maxSize;
            long baseCompressed = useDirBase ? maxDirCompressed : maxCompressed;

            item.SizeRatio = baseSize > 0 ? (double)item.Size / baseSize : 0;
            item.CompressedSizeRatio = baseCompressed > 0 ? (double)item.CompressedSize / baseCompressed : 0;
            item.DateRatio = dateRange > 0
                ? (item.LastModified - minDate).TotalSeconds / dateRange
                : (datedItems.Count == 1 ? 1.0 : 0);
            // 压缩率条（绝对比例）：目录与文件一视同仁显示聚合压缩率（与 RatioDisplay 文本一致，
            // Avalonia 对 WPF 的有意差异）；格式无法提供逐项压缩后大小时为 0
            item.RatioBarValue = item.Size > 0 && item.CompressedSizeAvailable
                ? Math.Min((double)item.CompressedSize / item.Size, 1.0)
                : 0;
            item.UseDirProgressColor = item.IsDirectory && SeparateDirBaseline;
        }
    }

    /// <summary>
    /// 对 _allRawItems 应用文本/日期/大小过滤器，返回过滤后的列表。
    /// </summary>
    private IReadOnlyList<ArchiveItem> GetFilteredSource()
    {
        if (_allRawItems == null) return Array.Empty<ArchiveItem>();

        // 筛选栏收起 = 筛选整体停用：条件保留在输入框中不丢失，重新展开即自动恢复生效
        if (!IsFilterBarVisible) return _allRawItems;

        IEnumerable<ArchiveItem> filtered = _allRawItems;

        return ArchiveFilter.ApplyFilters(filtered.ToList(), new SearchFilters
        {
            Text = string.IsNullOrWhiteSpace(FilterText) ? null : FilterText,
            ExcludeText = string.IsNullOrWhiteSpace(FilterExcludeText) ? null : FilterExcludeText,
            MatchMode = FilterMatchMode,
            DateFrom = FilterDateFrom,
            DateTo = FilterDateTo,
            SizeMin = FilterSizeMin.HasValue ? FilterSizeMin.Value * GetSizeMultiplier() : null,
            SizeMax = FilterSizeMax.HasValue ? FilterSizeMax.Value * GetSizeMultiplier() : null,
        });
    }

    private long GetSizeMultiplier() => FilterSizeUnit?.ToUpperInvariant() switch
    {
        "KB" => 1024L,
        "MB" => 1024L * 1024,
        "GB" => 1024L * 1024 * 1024,
        _ => 1L
    };

    // ── Navigation commands ──

    [RelayCommand]
    private void GoUp()
    {
        if (SelectedFolder?.FullPath == "") return;

        var currentPath = SelectedFolder?.FullPath ?? "";
        var lastSlash = currentPath.LastIndexOf('/');
        var parentPath = lastSlash >= 0 ? currentPath[..lastSlash] : "";

        NavigateAndPushHistory(parentPath);
    }

    [RelayCommand]
    private void GoRoot()
    {
        if (FolderTreeRoot == null) return;
        NavigateAndPushHistory("");
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_backStack.Count == 0) return;
        var target = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);

        if (CurrentFolder != null)
            _forwardStack.Add(CurrentFolder);

        _isProgrammaticFilter = true;
        try
        {
            CurrentFolder = target;
            PopulateEntries();
            var node = FindNode(FolderTreeRoot, target) ?? FolderTreeRoot;
            if (node != null)
                SelectedFolder = node;
        }
        finally
        {
            _isProgrammaticFilter = false;
        }

        NotifyNavigationState();
    }

    [RelayCommand]
    private void GoForward()
    {
        if (_forwardStack.Count == 0) return;
        var target = _forwardStack[^1];
        _forwardStack.RemoveAt(_forwardStack.Count - 1);

        if (CurrentFolder != null)
            _backStack.Add(CurrentFolder);

        _isProgrammaticFilter = true;
        try
        {
            CurrentFolder = target;
            PopulateEntries();
            var node = FindNode(FolderTreeRoot, target) ?? FolderTreeRoot;
            if (node != null)
                SelectedFolder = node;
        }
        finally
        {
            _isProgrammaticFilter = false;
        }

        NotifyNavigationState();
    }

    // ── Tree expand / collapse commands ──

    [RelayCommand]
    private void ExpandAll()
    {
        FolderTreeRoot?.ExpandAll();
    }

    [RelayCommand]
    private void CollapseAll()
    {
        FolderTreeRoot?.CollapseAll();
    }

    [RelayCommand]
    private void ExpandToCurrent()
    {
        if (FolderTreeRoot == null || SelectedFolder == null) return;
        ExpandTreeToPath(SelectedFolder.FullPath);
    }

    /// <summary>
    /// 收起整棵树（保留根展开），仅展开到指定路径的祖先链。
    /// 手动"定位到当前目录"与"自动展开"开关共用同一实现。
    /// </summary>
    private void ExpandTreeToPath(string path)
    {
        if (FolderTreeRoot == null) return;
        // Collapse all first
        FolderTreeRoot.CollapseAll();
        // Expand root
        FolderTreeRoot.IsExpanded = true;
        // 根目录（空路径）：收起所有分支即可，无需展开祖先链
        if (string.IsNullOrEmpty(path)) return;
        // Then expand ancestors of the target path
        ExpandAncestorsOf(FolderTreeRoot, path);
    }

    /// <summary>
    /// 切换目录时若开启"自动展开"，收起其他分支并展开到新目录的祖先链。
    /// 挂在 CurrentFolder 变化上，天然覆盖树点击 / 文件列表双击 / 地址栏 / 前进后退等所有导航路径。
    /// 仅 null（清空压缩包，树将重置）跳过；空字符串（根目录）也执行收起。
    /// </summary>
    partial void OnCurrentFolderChanged(string? value)
    {
        if (!AutoExpandTree) return;
        if (value == null) return;
        ExpandTreeToPath(value);
    }

    /// <summary>
    /// 自动展开开关变化时持久化到 AppSettings。
    /// </summary>
    partial void OnAutoExpandTreeChanged(bool value)
    {
        _appSettings.AutoExpandTreeToCurrent = value;
        SaveSetting(s => s.AutoExpandTreeToCurrent = value);
        // 开启时立即执行一次，让当前目录立刻在树中定位
        if (value)
            ExpandTreeToPath(CurrentFolder ?? "");
    }

    private static void ExpandAncestorsOf(FolderNode node, string targetPath)
    {
        if (node.FullPath == targetPath) return;
        foreach (var child in node.Children)
        {
            if (IsAncestorOf(child, targetPath))
            {
                child.IsExpanded = true;
                ExpandAncestorsOf(child, targetPath);
                return;
            }
        }
    }

    private static bool IsAncestorOf(FolderNode node, string targetPath)
    {
        if (node.FullPath == targetPath) return true;
        return node.Children.Any(c => IsAncestorOf(c, targetPath));
    }

    // ── Selection commands ──

    [RelayCommand]
    private void SelectAll()
    {
        SelectAllEntriesAction?.Invoke();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        InvertSelectionAction?.Invoke();
    }

    private static FolderNode? FindNode(FolderNode? node, string path)
    {
        if (node == null) return null;
        if (node.FullPath == path) return node;
        foreach (var child in node.Children)
        {
            var found = FindNode(child, path);
            if (found != null) return found;
        }
        return null;
    }
}
