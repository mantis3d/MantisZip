using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
    /// <summary>选中路径后触发（参数为路径字符串）。</summary>
    public event EventHandler<string>? PathSelected;

    private enum PathTab { Favorites, History, Windows }

    private PathTab _currentTab = PathTab.Favorites;

    /// <summary>当前 Tab 展示的列表项。</summary>
    private readonly ObservableCollection<QuickPathItem> _items = new();

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

        LoadSources();
        ShowCurrentTab();
    }

    // ── Public API ──────────────────────────────────────────────────────────

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

    private void SetTab(PathTab tab)
    {
        _currentTab = tab;
        FavoritesTab.IsChecked = tab == PathTab.Favorites;
        HistoryTab.IsChecked = tab == PathTab.History;
        WindowsTab.IsChecked = tab == PathTab.Windows;

        // 每次切 Tab 时刷新窗口来源（窗口列表是动态的）
        if (tab == PathTab.Windows)
            LoadWindowsSource();

        ShowCurrentTab();
    }

    // ── Data loading ────────────────────────────────────────────────────────

    private void LoadSources()
    {
        _favorites = FavoritePathManager.GetAll().Select(f => new QuickPathItem
        {
            DisplayName = f.Name,
            Path = f.Path,
            Icon = f.IsSystem ? FavoritePathManager.GetSystemIcon(f.SystemKey) : "📁",
            SourceTag = "⭐",
            ShowSourceTag = false
        }).ToList();

        _history = PathHistoryManager.GetRecent(50).Select(h => new QuickPathItem
        {
            DisplayName = GetDisplayName(h.Path),
            Path = h.Path,
            Icon = "🕐",
            SourceTag = "🕐",
            ShowSourceTag = false
        }).ToList();

        LoadWindowsSource();
    }

    private void LoadWindowsSource()
    {
        try
        {
            _windows = ExplorerWindowTracker.GetOpenExplorerWindows().Select(w => new QuickPathItem
            {
                DisplayName = !string.IsNullOrEmpty(w.DisplayName) ? w.DisplayName : GetDisplayName(w.Path),
                Path = w.Path,
                Icon = "🪟",
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
    public string Icon { get; set; } = "📁";
    public string SourceTag { get; set; } = string.Empty;
    public bool ShowSourceTag { get; set; }
    public bool IsActive { get; set; }
    public bool IsCurrent { get; set; }

    public QuickPathItem Clone() => new()
    {
        DisplayName = DisplayName,
        Path = Path,
        Icon = Icon,
        SourceTag = SourceTag,
        ShowSourceTag = ShowSourceTag,
        IsActive = IsActive,
        IsCurrent = IsCurrent
    };
}
