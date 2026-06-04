using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
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
            _currentPasswordDescription = null;
            _currentPasswordPatterns = null;
            UpdateEnterPasswordBtnState();
            UpdateAddDeleteBtnState();
            return;
        }
        if (!_hasEncryptedArchive)
        {
            PasswordStatusText.Text = "";
            _currentPasswordDescription = null;
            _currentPasswordPatterns = null;
            UpdateEnterPasswordBtnState();
            UpdateAddDeleteBtnState();
            return;
        }
        PasswordStatusText.Text = _currentPassword != null ? L.T(L.Main_PwdMatchedIndicator) : L.T(L.Main_IsEncrypted);
        UpdateEnterPasswordBtnState();
        UpdateAddDeleteBtnState();
    }

    private void UpdateEnterPasswordBtnState()
    {
        if (string.IsNullOrEmpty(_currentArchivePath) || !_hasEncryptedArchive)
        {
            // 无加密
            PwdIcon.Text = "🔑";
            PwdIcon.ClearValue(TextBlock.ForegroundProperty);
            PwdLabel.ClearValue(TextBlock.ForegroundProperty);
            EnterPasswordBtn.ClearValue(Button.BackgroundProperty);
            EnterPasswordBtn.IsEnabled = false;
            EnterPasswordBtn.ToolTip = L.T(L.Main_Tooltip_Password);
        }
        else if (_currentPassword == null)
        {
            // 已加密，密码未匹配
            PwdIcon.Text = "🔒";
            PwdIcon.Foreground = (Brush)FindResource("Theme_StatusError");
            PwdLabel.ClearValue(TextBlock.ForegroundProperty);
            EnterPasswordBtn.ClearValue(Button.BackgroundProperty);
            EnterPasswordBtn.IsEnabled = true;
            EnterPasswordBtn.ToolTip = L.T(L.Main_Tooltip_PasswordMissing);
        }
        else
        {
            // 已加密，密码已匹配
            PwdIcon.Text = "🔓";
            PwdIcon.Foreground = (Brush)FindResource("Theme_StatusSuccess");
            PwdLabel.ClearValue(TextBlock.ForegroundProperty);
            EnterPasswordBtn.Background = (Brush)FindResource("Theme_StatusSuccessBg");
            EnterPasswordBtn.IsEnabled = true;
            EnterPasswordBtn.ToolTip = L.T(L.Main_Tooltip_PasswordMatched);
        }
    }

    private void UpdateSmartExtractBtnState()
    {
        SmartExtractBtn.IsEnabled = !string.IsNullOrEmpty(_currentArchivePath);
    }

    private void UpdateFilterBtnState()
    {
        bool hasArchive = !string.IsNullOrEmpty(_currentArchivePath);
        ShowSubfoldersBtn.IsEnabled = hasArchive;
        ToggleFilterBarBtn.IsEnabled = hasArchive;
    }

    /// <summary>
    /// 更新「显示子目录」按钮的 ToolTip 和检查状态（由外部事件调用时使用）
    /// </summary>
    private void UpdateShowSubfoldersBtnToolTip()
    {
        ShowSubfoldersBtn.ToolTip = _showSubfolders
            ? L.T(L.Main_Tooltip_ShowSubfoldersOn)
            : L.T(L.Main_Tooltip_ShowSubfoldersOff);
    }

    private void ShowSubfoldersBtn_Click(object sender, RoutedEventArgs e)
    {
        _showSubfolders = ShowSubfoldersBtn.IsChecked == true;
        UpdateShowSubfoldersBtnToolTip();
        FilterFiles(_currentFolder);
    }

    private void ToggleFilterBarBtn_Click(object sender, RoutedEventArgs e)
    {
        bool show = ToggleFilterBarBtn.IsChecked == true;
        FilterBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 根据当前加载的压缩包格式，更新添加/删除按钮的启用状态。
    /// </summary>
    private void UpdateAddDeleteBtnState()
    {
        var engine = !string.IsNullOrEmpty(_currentArchivePath)
            ? ArchiveEngineFactory.GetEngineByExtension(_currentArchivePath)
            : null;

        if (engine == null)
        {
            AddFilesBtn.IsEnabled = false;
            DeleteFilesBtn.IsEnabled = false;
            if (EditMenuAddFiles != null) EditMenuAddFiles.IsEnabled = false;
            if (EditMenuDeleteFiles != null) EditMenuDeleteFiles.IsEnabled = false;
            if (EditMenuArchiveComment != null) EditMenuArchiveComment.IsEnabled = false;
            if (CloseArchiveMenu != null) CloseArchiveMenu.IsEnabled = false;
            return;
        }

        AddFilesBtn.IsEnabled = engine.CanAdd(_currentFormat);
        DeleteFilesBtn.IsEnabled = engine.CanDelete(_currentFormat);
        if (EditMenuAddFiles != null) EditMenuAddFiles.IsEnabled = engine.CanAdd(_currentFormat);
        if (EditMenuDeleteFiles != null) EditMenuDeleteFiles.IsEnabled = engine.CanDelete(_currentFormat);
        if (CloseArchiveMenu != null) CloseArchiveMenu.IsEnabled = true;
        // 注释仅 ZIP 格式支持
        if (EditMenuArchiveComment != null) EditMenuArchiveComment.IsEnabled = _currentFormat == ArchiveFormat.Zip;
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
            existing = new FolderNode { Name = name, FullPath = currentPath };
            nodes.Add(existing);
        }
        if (index < parts.Length - 1) AddFolderNode(existing.Children, parts, index + 1, fullPath);
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (FolderTree.SelectedItem is FolderNode node)
        {
            FilterFiles(node.FullPath);
            // 根目录显示压缩包总览（含注释）；子目录显示目录预览
            if (string.IsNullOrEmpty(node.FullPath))
            {
                ShowArchiveInfo();
            }
            else
            {
                var dirItem = new ArchiveItem
                {
                    Name = node.FullPath + "/",
                    FullPath = node.FullPath,
                    IsDirectory = true,
                    Size = 0,
                    CompressedSize = 0,
                };
                if (!string.IsNullOrEmpty(_currentArchivePath))
                    dirItem.CompressedDisplay = GetCompressedDisplayMode(_currentArchivePath, _currentFormat);
                ShowDirectoryPreview(dirItem);
            }
        }
    }

    private void FilterFiles(string folderPath, bool? showSubfoldersOverride = null)
    {
        // 在替换 ItemsSource 之前捕获当前排序状态，以便重新应用
        CaptureCurrentSort();

        bool show = showSubfoldersOverride ?? _showSubfolders;

        _isProgrammaticFilter = true;
        try
        {
            _currentFolder = folderPath;
            var directItems = new List<ArchiveItem>();
            string prefix = string.IsNullOrEmpty(folderPath) ? "" : folderPath + "/";

            if (show)
            {
                // ========== 扁平模式：收集所有递归子目录的文件 ==========
                foreach (var item in _allItems)
                {
                    if (item.IsDirectory) continue; // 不包含目录自身
                    if (!item.FullPath.StartsWith(prefix)) continue; // 不在当前目录下则跳过

                    // 根目录或子目录匹配
                    if (string.IsNullOrEmpty(folderPath) || item.Name.StartsWith(prefix))
                    {
                        directItems.Add(item);
                    }
                }

                // 设置 DisplayName 为相对路径
                foreach (var item in directItems)
                {
                    item.DisplayName = string.IsNullOrEmpty(folderPath)
                        ? item.Name
                        : item.Name.StartsWith(prefix) ? item.Name[prefix.Length..] : item.Name;
                }
            }
            else
            {
                // ========== 默认模式：直接条目 + 隐式目录合成（原有逻辑） ==========
                var implicitDirs = new HashSet<string>();

                foreach (var item in _allItems)
                {
                    if (string.IsNullOrEmpty(folderPath))
                    {
                        if (!item.Name.Contains("/")) { directItems.Add(item); continue; }
                        var firstSlash = item.Name.IndexOf('/');
                        var dirName = item.Name[..firstSlash];
                        if (!implicitDirs.Add(dirName)) continue;
                        if (item.IsDirectory && item.FullPath == dirName)
                        { directItems.Add(item); continue; }
                        var syntheticDir = new ArchiveItem { Name = dirName + "/", FullPath = dirName, Size = 0, IsDirectory = true, IconSource = SystemIconHelper.GetFolderIcon() };
                        if (!string.IsNullOrEmpty(_currentArchivePath))
                            syntheticDir.CompressedDisplay = GetCompressedDisplayMode(_currentArchivePath, _currentFormat);
                        directItems.Add(syntheticDir);
                    }
                    else
                    {
                        if (!item.Name.StartsWith(prefix)) continue;
                        if (item.FullPath == folderPath) continue;
                        var rest = item.Name[prefix.Length..].TrimEnd('/');
                        var restParts = rest.Split('/');
                        if (restParts.Length == 1)
                        {
                            directItems.Add(item);
                            if (item.IsDirectory) implicitDirs.Add(restParts[0]);
                        }
                        else
                        {
                            var subDir = restParts[0];
                            if (implicitDirs.Add(subDir))
                            {
                                var subDirFullPath = folderPath + "/" + subDir;
                                var subDirItem = new ArchiveItem { Name = subDirFullPath + "/", FullPath = subDirFullPath, Size = 0, IsDirectory = true, IconSource = SystemIconHelper.GetFolderIcon() };
                                if (!string.IsNullOrEmpty(_currentArchivePath))
                                    subDirItem.CompressedDisplay = GetCompressedDisplayMode(_currentArchivePath, _currentFormat);
                                directItems.Add(subDirItem);
                            }
                        }
                    }
                }

                // 通用去重：相同 FullPath 的条目只保留第一个
                var seen = new HashSet<string>();
                var deduped = new List<ArchiveItem>();
                foreach (var item in directItems)
                {
                    if (seen.Add(item.FullPath)) deduped.Add(item);
                }
                directItems = deduped;

                // 为目录条目填充统计信息
                foreach (var item in directItems)
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

                // 设置 DisplayName
                foreach (var item in directItems)
                {
                    item.DisplayName = string.IsNullOrEmpty(folderPath)
                        ? item.Name.TrimEnd('/')
                        : item.Name.StartsWith(prefix) ? item.Name[prefix.Length..].TrimEnd('/') : item.Name;
                }
            }

            var sortedItems = directItems.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToList();

            // ========== 进度条比例计算 ==========
            bool showBars = AppSettings.Instance.ShowProgressBars;
            bool separateBaseline = AppSettings.Instance.SeparateDirBaseline;

            foreach (var item in sortedItems)
            {
                item.ProgressBarEnabled = showBars;
                item.SeparateDirBaseline = separateBaseline;
            }

            if (showBars && sortedItems.Count > 0)
            {
                long maxSize = 0, maxCompressed = 0;
                long maxDirSize = 0, maxDirCompressed = 0;

                Func<ArchiveItem, long> effectiveCompressed = i =>
                    i.CompressedDisplay == ArchiveItem.CompressedDisplayMode.NotCompressed ? i.Size : i.CompressedSize;

                if (separateBaseline)
                {
                    var files = sortedItems.Where(i => !i.IsDirectory);
                    var dirs = sortedItems.Where(i => i.IsDirectory);
                    if (files.Any())
                    {
                        maxSize = files.Max(i => i.Size);
                        maxCompressed = files.Max(effectiveCompressed);
                    }
                    if (dirs.Any())
                    {
                        maxDirSize = dirs.Max(i => i.Size);
                        maxDirCompressed = dirs.Max(effectiveCompressed);
                    }
                }
                else
                {
                    maxSize = sortedItems.Max(i => i.Size);
                    maxCompressed = sortedItems.Max(effectiveCompressed);
                }

                foreach (var item in sortedItems)
                {
                    long baseSize = separateBaseline && item.IsDirectory ? maxDirSize : maxSize;
                    if (baseSize > 0)
                        item.SizeRatio = (double)item.Size / baseSize;

                    long baseCompressed = separateBaseline && item.IsDirectory ? maxDirCompressed : maxCompressed;
                    if (baseCompressed > 0)
                        item.CompressedSizeRatio = (double)effectiveCompressed(item) / baseCompressed;
                }

                var datedItems = sortedItems.Where(i => i.LastModified > DateTime.MinValue).ToList();
                if (datedItems.Count > 1)
                {
                    var minDate = datedItems.Min(i => i.LastModified);
                    var maxDate = datedItems.Max(i => i.LastModified);
                    var span = maxDate - minDate;
                    if (span.TotalSeconds > 0)
                    {
                        foreach (var item in datedItems)
                            item.DateRatio = (item.LastModified - minDate).TotalSeconds / span.TotalSeconds;
                    }
                }
                else if (datedItems.Count == 1)
                {
                    datedItems[0].DateRatio = 1.0;
                }
            }
            else
            {
                foreach (var item in sortedItems)
                {
                    item.SizeRatio = 0;
                    item.CompressedSizeRatio = 0;
                    item.DateRatio = 0;
                }
            }

            // 存储无过滤的完整列表供 RefreshFilter 使用
            _currentUnfilteredItems = sortedItems;

            // 更新 ItemsSource 和排序
            FileListGrid.ItemsSource = sortedItems;
            ApplySavedSort();
            FileListGrid.Items.Refresh();

            // 更新状态栏统计
            var fileCount = sortedItems.Count(i => !i.IsDirectory);
            var dirCount = sortedItems.Count(i => i.IsDirectory);
            if (show)
                DirStatsText.Text = $"{sortedItems.Count} 个文件（含子目录）";
            else
                DirStatsText.Text = L.TF(L.Main_DirStats, sortedItems.Count, fileCount, dirCount);

            // 更新选中统计
            UpdateSelectionStats();
        }
        finally { _isProgrammaticFilter = false; }

        // 过滤后无选中项 → 显示压缩包总览（仅在非过滤触发时）
        if (FileListGrid.SelectedItems.Count == 0 && !string.IsNullOrEmpty(_currentArchivePath)
            && HasActiveFilters() == false)
            ShowArchiveInfo();
    }

    /// <summary>
    /// 检查当前是否有激活的过滤条件
    /// </summary>
    private bool HasActiveFilters()
    {
        return !string.IsNullOrEmpty(_searchText)
            || _dateFrom.HasValue
            || _dateTo.HasValue
            || _sizeMin.HasValue
            || _sizeMax.HasValue;
    }

    /// <summary>
    /// 从当前 unfiltered 列表重建过滤后的视图。
    /// 由 FilterFiles 末尾调用，或者由任一过滤控件的事件处理器调用。
    /// </summary>
    private void RefreshFilter()
    {
        if (_currentUnfilteredItems == null) return;

        var filters = new SearchFilters
        {
            Text = _searchText,
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            SizeMin = _sizeMin,
            SizeMax = _sizeMax,
        };

        List<Core.Abstractions.ArchiveItem> result;
        if (HasActiveFilters())
        {
            result = ArchiveFilter.ApplyFilters(_currentUnfilteredItems, filters);
        }
        else
        {
            result = new List<Core.Abstractions.ArchiveItem>(_currentUnfilteredItems);
        }

        // 设置 ItemsSource 并应用排序
        FileListGrid.ItemsSource = result;
        ApplySavedSort();
        FileListGrid.Items.Refresh();

        // 更新 NoResultsText 显隐
        NoResultsText.Visibility = (result.Count == 0 && HasActiveFilters())
            ? Visibility.Visible
            : Visibility.Collapsed;

        // 更新状态栏
        int totalCount = _currentUnfilteredItems.Count;
        int fileCount = result.Count(i => !i.IsDirectory);
        int dirCount = result.Count(i => i.IsDirectory);

        if (_showSubfolders && !HasActiveFilters())
        {
            DirStatsText.Text = $"{totalCount} 个文件（含子目录）";
        }
        else if (HasActiveFilters())
        {
            DirStatsText.Text = L.TF(L.Main_Filter_StatsFormat, result.Count, totalCount);
        }
        else
        {
            DirStatsText.Text = L.TF(L.Main_DirStats, totalCount, fileCount, dirCount);
        }

        UpdateSelectionStats();
    }

    // ===== 过滤控件事件处理器 =====

    private void FileSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = string.IsNullOrWhiteSpace(FileSearchBox.Text) ? null : FileSearchBox.Text;
        RefreshFilter();
    }

    private void DateFromPicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        _dateFrom = DateFromPicker.SelectedDate;
        RefreshFilter();
    }

    private void DateToPicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        _dateTo = DateToPicker.SelectedDate;
        RefreshFilter();
    }

    private void SizeMinBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _sizeMin = ArchiveFilter.ParseSizeWithUnit(SizeMinBox.Text, (SizeMinUnit.SelectedItem as ComboBoxItem)?.Content?.ToString());
        RefreshFilter();
    }

    private void SizeMaxBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _sizeMax = ArchiveFilter.ParseSizeWithUnit(SizeMaxBox.Text, (SizeMaxUnit.SelectedItem as ComboBoxItem)?.Content?.ToString());
        RefreshFilter();
    }

    private void SizeMinUnit_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SizeMinBox.Text))
        {
            _sizeMin = ArchiveFilter.ParseSizeWithUnit(SizeMinBox.Text, (SizeMinUnit.SelectedItem as ComboBoxItem)?.Content?.ToString());
            RefreshFilter();
        }
    }

    private void SizeMaxUnit_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SizeMaxBox.Text))
        {
            _sizeMax = ArchiveFilter.ParseSizeWithUnit(SizeMaxBox.Text, (SizeMaxUnit.SelectedItem as ComboBoxItem)?.Content?.ToString());
            RefreshFilter();
        }
    }

    private void ClearFiltersBtn_Click(object sender, RoutedEventArgs e)
    {
        // 清空所有过滤控件
        FileSearchBox.Text = "";
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        SizeMinBox.Text = "";
        SizeMaxBox.Text = "";
        SizeMinUnit.SelectedIndex = 0;
        SizeMaxUnit.SelectedIndex = 0;

        // 清空过滤字段
        _searchText = null;
        _dateFrom = null;
        _dateTo = null;
        _sizeMin = null;
        _sizeMax = null;

        RefreshFilter();
    }

    /// <summary>
    /// 搜索框 Escape 键处理：仅清文字搜索框（不清除日期/大小）
    /// </summary>
    private void FileSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FileSearchBox.Text = "";
            _searchText = null;
            RefreshFilter();
            e.Handled = true;
        }
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

    /// <summary>
    /// DataGrid 排序事件：在列标题上显示 ▲/▼ 排序标记
    /// </summary>
    private void FileListGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // 清除所有列标题上的排序标记，恢复原始文字
        foreach (var column in FileListGrid.Columns)
        {
            if (column.Header is string headerText)
                column.Header = headerText.TrimEnd('▲', '▼', ' ').TrimEnd();
        }

        // 排序事件触发时 col.SortDirection 还是旧值，需要自己推算即将应用的新方向
        var col = e.Column;
        ListSortDirection? newDir = null;
        if (col.SortDirection == null)
            newDir = ListSortDirection.Ascending;
        else if (col.SortDirection == ListSortDirection.Ascending)
            newDir = ListSortDirection.Descending;
        // else (Descending) → null（回到未排序）

        // 同步更新保存的排序状态（用于 FilterFiles 切目录时恢复）
        _savedSortColumnPath = newDir.HasValue ? col.SortMemberPath : null;
        _savedSortDirection = newDir.HasValue
            ? (newDir.Value == ListSortDirection.Ascending ? 1 : 2)
            : 0;
        App.LogDebug("FileListGrid_Sorting: col={0}, sortPath={1}, dir={2}",
            col.Header?.ToString()?.TrimEnd('▲', '▼', ' ').TrimEnd(),
            _savedSortColumnPath ?? "(null)",
            _savedSortDirection);

        if (col.Header is string header)
        {
            var clean = header.TrimEnd('▲', '▼', ' ').TrimEnd();
            if (newDir == ListSortDirection.Ascending)
                col.Header = clean + " ▲";
            else if (newDir == ListSortDirection.Descending)
                col.Header = clean + " ▼";
        }

        // 目录独立基准开启时：目录永远排在上面，文件排在下面
        if (AppSettings.Instance.SeparateDirBaseline)
        {
            e.Handled = true; // 阻止默认排序，改为手动
            // 手动更新 col.SortDirection（e.Handled=true 后 WPF 不再自动更新）
            col.SortDirection = newDir;
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(FileListGrid.ItemsSource);
            if (view != null)
            {
                view.SortDescriptions.Clear();
                if (newDir.HasValue)
                {
                    // 第一排序键：目录/文件分离（始终升序，目录在上面）
                    view.SortDescriptions.Add(
                        new System.ComponentModel.SortDescription("SortOrder", ListSortDirection.Ascending));
                    // 第二排序键：用户点击的列
                    view.SortDescriptions.Add(
                        new System.ComponentModel.SortDescription(col.SortMemberPath, newDir.Value));
                }
                // newDir == null（回到未排序）→ 清空 SortDescriptions 后视图回到 FilterFiles 的默认排列
            }
        }
    }

    /// <summary>
    /// 列标题右键菜单打开时：动态生成各列的显隐切换项
    /// </summary>
    private void ColumnHeaderContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        menu.Items.Clear();

        foreach (var column in FileListGrid.Columns)
        {
            // 获取原始标题文字（去掉排序标记）
            var raw = column.Header?.ToString() ?? "";
            raw = raw.TrimEnd('▲', '▼', ' ').TrimEnd();

            // 跳过名称列（不允许隐藏）
            if (raw == L.T(L.Main_Col_Name))   // "名称" / "Name"
                continue;

            var isVisible = column.Visibility == Visibility.Visible;

            var item = new MenuItem
            {
                Header = raw,
                Tag = column
            };

            // 各列对应的彩色 emoji 图标；可见=不透明，隐藏=半透明
            var icon = "📋";
            if (raw == L.T(L.Main_Col_Size)) icon = "📏";
            else if (raw == L.T(L.Main_Col_Compressed)) icon = "📦";
            else if (raw == L.T(L.Main_Col_Ratio)) icon = "📊";
            else if (raw == L.T(L.Main_Col_Crc32)) icon = "🔐";
            else if (raw == L.T(L.Main_Col_Date)) icon = "📅";
            else if (raw == L.T(L.Main_Col_Encrypted)) icon = "🔒";

            item.Icon = new Emoji.Wpf.TextBlock
            {
                Text = icon,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 4, 0),
                Opacity = isVisible ? 1.0 : 0.2
            };

            item.Click += ColumnVisibilityMenuItem_Click;
            menu.Items.Add(item);
        }
    }

    /// <summary>
    /// 点击列显隐菜单项：切换对应列的可见性 + 图标透明度
    /// </summary>
    private void ColumnVisibilityMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is DataGridColumn column)
        {
            var newVisible = column.Visibility != Visibility.Visible;
            column.Visibility = newVisible ? Visibility.Visible : Visibility.Collapsed;

            // 更新图标透明度：可见=不透明，隐藏=半透明
            if (item.Icon is Emoji.Wpf.TextBlock iconBlock)
                iconBlock.Opacity = newVisible ? 1.0 : 0.2;
        }
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
    public string Icon => string.IsNullOrEmpty(FullPath) ? "📦" : "📁";
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
    /// <summary>压缩后大小的显示模式</summary>
    public enum CompressedDisplayMode
    {
        /// <summary>正常显示实际的 CompressedSize（ZIP）</summary>
        Normal,
        /// <summary>格式本身不压缩（ISO, TAR），用 Size 作为压缩后大小，压缩率始终 100%</summary>
        NotCompressed,
        /// <summary>有压缩但无法获取逐项压缩后大小（7z, RAR, TGZ/GZ），显示 ---</summary>
        Unavailable
    }

    public string DisplayName { get; set; } = string.Empty;
    public string NameForSort { get; set; } = string.Empty;
    public ImageSource? IconSource { get; set; }

    public CompressedDisplayMode CompressedDisplay { get; set; } = CompressedDisplayMode.Normal;

    public string SizeDisplay => FormatSize(Size);

    public string CompressedSizeDisplay => CompressedDisplay switch
    {
        CompressedDisplayMode.Unavailable => "---",
        CompressedDisplayMode.NotCompressed => FormatSize(Size),
        _ => FormatSize(CompressedSize)
    };

    public string NameDisplay
    {
        get { return !string.IsNullOrEmpty(DisplayName) ? DisplayName : Name; }
    }

    public int SortOrder => IsDirectory ? 0 : 1;

    public string DateDisplay => LastModified > DateTime.MinValue
        ? LastModified.ToString("yyyy-MM-dd HH:mm")
        : "---";

    public string Crc32Display
    {
        get { return Crc32 != 0 ? $"{(uint)Crc32:X8}" : "---"; }
    }

    public string RatioDisplay
    {
        get
        {
            if (IsDirectory || Size == 0) return "---";
            return CompressedDisplay switch
            {
                CompressedDisplayMode.Unavailable => "---",
                CompressedDisplayMode.NotCompressed => "100%",
                _ when CompressedSize == 0 => "---",
                _ => $"{Math.Min((double)CompressedSize / Size, 1.0) * 100:F1}%"
            };
        }
    }

    public double RatioSort
    {
        get
        {
            if (IsDirectory || Size == 0) return double.MaxValue;
            return CompressedDisplay switch
            {
                CompressedDisplayMode.Unavailable => double.MaxValue,
                CompressedDisplayMode.NotCompressed => 1.0,
                _ when CompressedSize == 0 => double.MaxValue,
                _ => Math.Min((double)CompressedSize / Size, 1.0)
            };
        }
    }

    // ——— 进度条属性 ———
    /// <summary>全局开关（由菜单切换）</summary>
    public bool ProgressBarEnabled { get; set; } = true;
    /// <summary>目录独立基准模式（由菜单切换）</summary>
    public bool SeparateDirBaseline { get; set; } = false;

    /// <summary>目录在分列基准模式下使用深色进度条</summary>
    public bool UseDirProgressColor => IsDirectory && SeparateDirBaseline;

    /// <summary>大小相对比例（0.0 ~ 1.0，FilterFiles 中计算赋值）</summary>
    public double SizeRatio { get; set; }
    /// <summary>压缩后大小相对比例（0.0 ~ 1.0，FilterFiles 中计算赋值）</summary>
    public double CompressedSizeRatio { get; set; }
    /// <summary>日期相对比例（0.0 ~ 1.0，FilterFiles 中计算赋值）</summary>
    public double DateRatio { get; set; }

    /// <summary>压缩率进度条值（绝对比例，复用 RatioSort，门控 IsDirectory/Size=0/Unavailable）</summary>
    public double RatioBarValue => ProgressBarEnabled && !IsDirectory && Size > 0
        && CompressedDisplay != CompressedDisplayMode.Unavailable
        ? Math.Min(RatioSort, 1.0)
        : 0;

    internal static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }
}
