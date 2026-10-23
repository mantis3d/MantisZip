using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// 路径速选面板（左面板）：
/// - Tab 行：⭐收藏 / 🕐历史 / 🪟窗口 三个来源切换
/// - 搜索框：输入时跨三个来源聚合过滤
/// - 列表：当前 Tab 的路径列表，选中即触发 <see cref="PathSelected"/> 事件
/// 不含地址栏/文件浏览（归宿主 CustomFilePickerDialog 管理）。
/// </summary>
public partial class QuickPathControl : UserControl
{
    /// <summary>选中路径后触发（参数为路径字符串）。列表 Tab 单击、目录树单击均触发。</summary>
    public event EventHandler<string>? PathSelected;

    /// <summary>双击确认路径后触发（目录树双击节点；列表双击由宿主自行处理）。</summary>
    public event EventHandler<string>? PathConfirmed;

    /// <summary>当前激活的 Tab（宿主可据此区分单击导航 vs 双击确认）。</summary>
    public PathTab CurrentTab => _currentTab;

    private PathTab _currentTab = PathTab.Favorites;

    /// <summary>当前 Tab 展示的列表项。</summary>
    private readonly ObservableCollection<QuickPathItem> _items = new();

    /// <summary>目录树根节点（盘符列表）。</summary>
    private readonly ObservableCollection<DirectoryTreeNode> _treeRoots = new();

    /// <summary>上一次构建盘符根节点时的「盘符 + 卷标」签名，用于判断是否需要重建（没变化就不重建，保住展开状态）。</summary>
    private string _treeRootSignature = string.Empty;

    /// <summary>搜索框非空时的聚合过滤结果缓存（用于快速重建）。</summary>
    private List<QuickPathItem> _favorites = new();
    private List<QuickPathItem> _history = new();
    private List<QuickPathItem> _windows = new();

    public QuickPathControl()
    {
        InitializeComponent();

        PathList.ItemsSource = _items;
        SearchBox.PlaceholderText = LocalizationManager.T("QuickPath_SearchPlaceholder");
        ToolTip.SetTip(FavoritesTab, LocalizationManager.T("QuickPath_TabFavorites"));
        ToolTip.SetTip(HistoryTab, LocalizationManager.T("QuickPath_TabHistory"));
        ToolTip.SetTip(WindowsTab, LocalizationManager.T("QuickPath_TabWindows"));
        ToolTip.SetTip(TreeTab, LocalizationManager.T("QuickPath_TabTree"));

        LoadTreeRoots(force: true);
        DirTree.ItemsSource = _treeRoots;
        // 惰性加载：展开节点时才枚举子目录
        DirTree.AddHandler(TreeViewItem.ExpandedEvent, DirTree_Expanded);

        LoadSources();
        ShowCurrentTab();
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// 单 Tab 模式：设置为非 null 时隐藏 Tab 行 + 搜索框，只显示指定来源。
    /// 用于宿主弹出独立面板（每个按钮一个 Popup 场景）。
    /// </summary>
    public PathTab? SingleTab { get; set; }

    /// <summary>
    /// 设置当前浏览路径，用于在列表中高亮匹配项。
    /// </summary>
    public void SetCurrentPath(string path)
    {
        var norm = NormalizePath(path);
        foreach (var item in _items)
        {
            item.IsCurrent = string.Equals(item.Path, norm, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>强制刷新数据源（宿主在收藏/历史变化后可调用）。</summary>
    public void RefreshSources()
    {
        LoadSources();
        ShowCurrentTab();
    }

    /// <summary>
    /// 由宿主（如压缩窗口的快捷按钮行）调用，切换到指定 Tab。
    /// </summary>
    public void SelectTab(PathTab tab)
    {
        SetTab(tab);
    }

    /// <summary>启用单 Tab 模式（在 SingleTab 赋值后调用）。</summary>
    public void ApplySingleTabMode()
    {
        if (SingleTab == null) return;
        TabRow.IsVisible = false;
        // 搜索框在 TabRow 内，随 TabRow 一起隐藏
        SetTab(SingleTab.Value);
    }

    /// <summary>
    /// 还原持久化的来源标签名（<see cref="PathTab"/> 成员名）：空值、未知值、越界数值一律回退
    /// <see cref="PathTab.Favorites"/>，保证首次启动与旧配置文件的行为与硬编码默认值一致。
    /// </summary>
    public static PathTab ParseTabOrDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return PathTab.Favorites;
        return Enum.TryParse<PathTab>(value, ignoreCase: true, out var tab) && Enum.IsDefined(tab)
            ? tab
            : PathTab.Favorites;
    }

    // ── Tab switching ───────────────────────────────────────────────────────

    private void FavoritesTab_Click(object? sender, RoutedEventArgs e)
    {
        SetTab(PathTab.Favorites);
    }

    private void HistoryTab_Click(object? sender, RoutedEventArgs e)
    {
        SetTab(PathTab.History);
    }

    private void WindowsTab_Click(object? sender, RoutedEventArgs e)
    {
        SetTab(PathTab.Windows);
    }

    private void TreeTab_Click(object? sender, RoutedEventArgs e)
    {
        SetTab(PathTab.Tree);
    }

    private void SetTab(PathTab tab)
    {
        _currentTab = tab;
        FavoritesTab.IsChecked = tab == PathTab.Favorites;
        HistoryTab.IsChecked = tab == PathTab.History;
        WindowsTab.IsChecked = tab == PathTab.Windows;
        TreeTab.IsChecked = tab == PathTab.Tree;

        // 每次切 Tab 时刷新窗口来源（窗口列表是动态的）
        if (tab == PathTab.Windows)
            LoadWindowsSource();

        // 切到目录树时重扫盘符（U 盘热插拔 / 新挂载无需重开选择器）；
        // 盘符与卷标都没变化时 LoadTreeRoots 会直接跳过，避免把已展开的树折叠回去
        if (tab == PathTab.Tree)
            LoadTreeRoots();

        // 内容区可见性：目录树 Tab 显示 TreeView，其余显示 ListBox
        PathList.IsVisible = tab != PathTab.Tree;
        DirTree.IsVisible = tab == PathTab.Tree;
        // 搜索框仅覆盖收藏/历史/窗口三来源，目录树 Tab 下隐藏
        SearchBox.IsVisible = tab != PathTab.Tree;

        ShowCurrentTab();
    }

    // ── Data loading ────────────────────────────────────────────────────────

    private void LoadSources()
    {
        var folderIcon = IconService.GetFolderIcon();
        _favorites = FavoritePathManager.GetAll().Select(f => new QuickPathItem
        {
            DisplayName = f.Name,
            Path = f.Path,
            Icon = folderIcon,
            IconKey = "IconFolder",
            SourceTag = "⭐",
            ShowSourceTag = false
        }).ToList();

        _history = PathHistoryManager.GetRecent(50).Select(h => new QuickPathItem
        {
            DisplayName = GetDisplayName(h.Path),
            Path = h.Path,
            Icon = folderIcon,
            IconKey = "IconHistory",
            SourceTag = "🕐",
            ShowSourceTag = false
        }).ToList();

        LoadWindowsSource();
    }

    private void LoadWindowsSource()
    {
        try
        {
            var folderIcon = IconService.GetFolderIcon();
            _windows = ExplorerWindowTracker.GetOpenExplorerWindows().Select(w => new QuickPathItem
            {
                DisplayName = !string.IsNullOrEmpty(w.DisplayName) ? w.DisplayName : GetDisplayName(w.Path),
                Path = w.Path,
                Icon = folderIcon,
                IconKey = "IconHome",
                SourceTag = "🪟",
                ShowSourceTag = false,
                IsActive = w.IsActive
            }).ToList();
        }
        catch
        {
            _windows = new List<QuickPathItem>();
        }
    }

    private void ShowCurrentTab()
    {
        // 目录树 Tab 不操作列表
        if (_currentTab == PathTab.Tree)
        {
            EmptyText.IsVisible = false;
            return;
        }

        _items.Clear();
        List<QuickPathItem> source = _currentTab switch
        {
            PathTab.Favorites => _favorites,
            PathTab.History => _history,
            _ => _windows
        };
        foreach (var item in source)
            _items.Add(item);

        UpdateEmptyState();
    }

    // ── Directory tree (Tree tab) ───────────────────────────────────────────

    /// <summary>
    /// 加载（或按需重建）目录树根节点：所有就绪盘符（平铺，无「此电脑」虚拟根）。
    /// 显示名为「卷标 (X:)」，无卷标时按驱动器类型兜底；
    /// 图标优先取系统真实驱动器图标（硬盘 / 可移动盘 / 网络盘 / 光驱自动区分），
    /// 非 Windows 平台回退到矢量 <c>IconDrive</c>。
    /// 每个盘符预置占位子节点，使展开箭头可见。
    /// </summary>
    /// <param name="force">true = 无条件重建；false = 仅在盘符集合或卷标变化时重建</param>
    private void LoadTreeRoots(bool force = false)
    {
        try
        {
            var signature = BuildDriveSignature();
            if (!force && signature == _treeRootSignature && _treeRoots.Count > 0)
                return; // 盘符没变化：保留用户已展开的层级
            _treeRootSignature = signature;

            _treeRoots.Clear();
            foreach (var drive in EnumerateReadyDrives())
            {
                var root = drive.RootDirectory.FullName; // "C:\"
                var node = new DirectoryTreeNode
                {
                    Name = FormatDriveName(GetVolumeLabelSafe(drive), GetDriveLetter(root), drive.DriveType),
                    FullPath = root,
                    // Windows 上是资源管理器里那颗真实盘图标；其它平台为 null → 用 IconKey 矢量兜底
                    Icon = IconService.GetDriveIcon(root),
                    IconKey = "IconDrive"
                };
                // 预置占位子节点 → 显示展开箭头
                node.Children.Add(new DirectoryTreeNode { IsPlaceholder = true });
                _treeRoots.Add(node);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickPathControl] LoadTreeRoots failed: {ex.Message}");
        }
    }

    /// <summary>枚举当前就绪的驱动器（未就绪 / 探测抛异常的一律跳过）。</summary>
    private static IEnumerable<DriveInfo> EnumerateReadyDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            bool ready;
            try { ready = drive.IsReady; }
            catch { ready = false; }
            if (ready) yield return drive;
        }
    }

    /// <summary>盘符字母：<c>C:\</c> → <c>C:</c>。</summary>
    public static string GetDriveLetter(string rootPath) => rootPath.TrimEnd('\\', '/');

    /// <summary>
    /// 安全读取卷标：空卡槽、无权限、BitLocker 锁定、网络盘掉线都会抛异常，一律按「无卷标」处理。
    /// </summary>
    public static string? GetVolumeLabelSafe(DriveInfo drive)
    {
        try
        {
            var label = drive.VolumeLabel;
            return string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 盘符显示名（资源管理器风格）：有卷标 → 「系统 (C:)」；
    /// 无卷标 → 按驱动器类型兜底「本地磁盘 (C:)」「可移动磁盘 (E:)」「网络驱动器 (Z:)」「光盘 (D:)」。
    /// </summary>
    /// <param name="volumeLabel">卷标，null/空白表示无卷标</param>
    /// <param name="driveLetter">盘符（已带冒号，如 <c>C:</c>）</param>
    /// <param name="driveType">驱动器类型，决定无卷标时的兜底名称</param>
    public static string FormatDriveName(string? volumeLabel, string driveLetter, DriveType driveType)
    {
        var name = string.IsNullOrWhiteSpace(volumeLabel)
            ? LocalizationManager.T(DriveTypeNameKey(driveType))
            : volumeLabel.Trim();
        return $"{name} ({driveLetter})";
    }

    /// <summary>驱动器类型 → 无卷标时兜底名称的本地化 key（规则 13：文案不得硬编码）。</summary>
    public static string DriveTypeNameKey(DriveType driveType) => driveType switch
    {
        DriveType.Fixed => "QuickPath_Drive_Fixed",
        DriveType.Removable => "QuickPath_Drive_Removable",
        DriveType.Network => "QuickPath_Drive_Network",
        DriveType.CDRom => "QuickPath_Drive_Optical",
        DriveType.Ram => "QuickPath_Drive_Ram",
        _ => "QuickPath_Drive_Generic"
    };

    /// <summary>「盘符 + 卷标」签名，用于判断根节点是否需要重建。</summary>
    private static string BuildDriveSignature()
    {
        var parts = new List<string>();
        foreach (var drive in EnumerateReadyDrives())
        {
            var root = drive.RootDirectory.FullName;
            parts.Add(root + "|" + (GetVolumeLabelSafe(drive) ?? string.Empty));
        }
        return string.Join("\n", parts);
    }

    /// <summary>
    /// 节点展开时惰性加载其子目录（异步枚举，防 UI 卡顿；已加载过则跳过）。
    /// 先清掉占位节点，再填充真实子目录（每个也预置占位让下一层箭头可见）。
    /// </summary>
    private async void DirTree_Expanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TreeViewItem item || item.DataContext is not DirectoryTreeNode node)
            return;
        if (node.IsLoaded) return;
        node.IsLoaded = true;

        // 移除占位节点
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            if (node.Children[i].IsPlaceholder)
                node.Children.RemoveAt(i);
        }

        try
        {
            var dirs = await Task.Run(() =>
                Directory.EnumerateDirectories(node.FullPath)
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            var folderIcon = IconService.GetFolderIcon();
            foreach (var dir in dirs)
            {
                var child = new DirectoryTreeNode
                {
                    Name = Path.GetFileName(dir),
                    FullPath = dir,
                    Icon = folderIcon,
                    IconKey = "IconFolder"
                };
                // 预置占位子节点 → 下一层展开箭头可见
                child.Children.Add(new DirectoryTreeNode { IsPlaceholder = true });
                node.Children.Add(child);
            }
        }
        catch
        {
            // 无权限/不可访问目录：保持空（不弹错）
        }
    }

    private void DirTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DirTree.SelectedItem is not DirectoryTreeNode node) return;
        if (string.IsNullOrEmpty(node.FullPath)) return;

        PathHistoryManager.Record(node.FullPath);
        PathSelected?.Invoke(this, node.FullPath);
    }

    /// <summary>目录树节点双击 → 确认选择该目录。</summary>
    private void DirTree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DirTree.SelectedItem is not DirectoryTreeNode node) return;
        if (string.IsNullOrEmpty(node.FullPath)) return;

        PathHistoryManager.Record(node.FullPath);
        PathConfirmed?.Invoke(this, node.FullPath);
    }

    // ── Search filtering ────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            ShowCurrentTab();
            return;
        }

        // 跨三个来源聚合过滤
        var filtered = new List<QuickPathItem>();
        foreach (var item in _favorites.Concat(_history).Concat(_windows))
        {
            if (item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                // 搜索时显示来源标签
                var clone = item.Clone();
                clone.ShowSourceTag = true;
                filtered.Add(clone);
            }
        }

        _items.Clear();
        foreach (var item in filtered)
            _items.Add(item);

        UpdateEmptyState();
    }

    // ── Selection ───────────────────────────────────────────────────────────

    private void PathList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PathList.SelectedItem is not QuickPathItem item) return;

        var path = item.Path;
        PathHistoryManager.Record(path);
        PathSelected?.Invoke(this, path);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void UpdateEmptyState()
    {
        if (_items.Count == 0)
        {
            EmptyText.Text = _currentTab switch
            {
                PathTab.Favorites => LocalizationManager.T("QuickPath_EmptyFavorites"),
                PathTab.History => LocalizationManager.T("QuickPath_EmptyHistory"),
                _ => LocalizationManager.T("QuickPath_EmptyWindows")
            };
            EmptyText.IsVisible = true;
        }
        else
        {
            EmptyText.IsVisible = false;
        }
    }

    private static string GetDisplayName(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        while (path.Length > 3 && (path[^1] == '\\' || path[^1] == '/'))
            path = path[..^1];
        return path;
    }
}

/// <summary>路径列表项（来源标签 + 图标 + 路径）。</summary>
public class QuickPathItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    /// <summary>真实文件系统图标（文件夹图标等）。null 时回退 <see cref="IconKey"/> 矢量图标。</summary>
    public Bitmap? Icon { get; set; }
    /// <summary>Icon 为 null 时用于 PathIcon 的矢量资源键（IconFolder / IconHistory / IconHome 等）。</summary>
    public string? IconKey { get; set; }
    /// <summary>Icon 是否为 null（决定显示 Image 还是 PathIcon）。</summary>
    public bool IsNullIcon => Icon == null;
    public string SourceTag { get; set; } = string.Empty;
    public bool ShowSourceTag { get; set; }
    public bool IsActive { get; set; }
    public bool IsCurrent { get; set; }

    public QuickPathItem Clone() => new()
    {
        DisplayName = DisplayName,
        Path = Path,
        Icon = Icon,
        IconKey = IconKey,
        SourceTag = SourceTag,
        ShowSourceTag = ShowSourceTag,
        IsActive = IsActive,
        IsCurrent = IsCurrent
    };
}

/// <summary>路径速选来源 Tab。</summary>
public enum PathTab
{
    Favorites,
    History,
    Windows,
    Tree
}

/// <summary>目录树节点（惰性加载子目录）。</summary>
public class DirectoryTreeNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;

    /// <summary>真实文件系统图标（文件夹图标 / 盘符图标）。null 时回退 <see cref="IconKey"/> 矢量图标。</summary>
    public Bitmap? Icon { get; set; }

    /// <summary>矢量兜底图标的资源 key（如 <c>IconDrive</c> / <c>IconFolder</c>），仅在 <see cref="Icon"/> 为 null 时显示。</summary>
    public string? IconKey { get; set; }

    /// <summary>是否没有真实图标（决定矢量兜底是否可见）。</summary>
    public bool IsNullIcon => Icon == null;

    /// <summary>子目录（展开时异步填充）。</summary>
    public ObservableCollection<DirectoryTreeNode> Children { get; } = new();

    /// <summary>该层是否已枚举过（防止展开重复加载）。</summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// 是否为占位节点：Avalonia TreeView 只有节点含子节点时才显示展开箭头，
    /// 因此每个未加载目录预置一个占位子节点让箭头出现；展开时被替换为真实子目录。
    /// </summary>
    public bool IsPlaceholder { get; set; }
}
