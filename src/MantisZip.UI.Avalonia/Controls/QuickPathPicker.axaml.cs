using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// 自包含可复用路径速选控件：路径输入框（AutoCompleteBox）+ ⭐🕐🪟 单 Tab 快捷浮层 + 📁 浏览。
/// 控件永远只收「目录」——浏览/输入定位到文件时统一收敛为父目录；文件名属其它控件职责。
/// </summary>
public partial class QuickPathPicker : UserControl
{
    /// <summary>路径值（TwoWay：宿主 VM 的路径属性）。</summary>
    public static readonly StyledProperty<string> PathProperty =
        AvaloniaProperty.Register<QuickPathPicker, string>(nameof(Path), defaultValue: string.Empty, defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    /// <summary>路径值。</summary>
    public string Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>
    /// 可选浏览动作。签名 (owner, 当前路径) → 新路径或 null。
    /// 为 null 时用内置纯目录选择。<see cref="CustomFilePickerDialog.ShowFolderAsync"/>。
    /// 返回的文件路径会自动收敛为父目录。
    /// </summary>
    public Func<Window?, string?, Task<string?>>? BrowseAction { get; set; }

    public QuickPathPicker()
    {
        InitializeComponent();
        if (!Design.IsDesignMode)
        {
            InitControls();
        }
    }

    private void InitControls()
    {
        // ToolTip：快捷 Tab 复用 QuickPathControl 既有 key（已存在于字符串文件）
        ToolTip.SetTip(QuickFavButton, LocalizationManager.T("QuickPath_TabFavorites"));
        ToolTip.SetTip(QuickHistButton, LocalizationManager.T("QuickPath_TabHistory"));
        ToolTip.SetTip(QuickWinButton, LocalizationManager.T("QuickPath_TabWindows"));
        ToolTip.SetTip(QuickBrowseButton, LocalizationManager.T("QuickPath_Browse"));

        // 三个单 Tab 面板
        FavControl.SingleTab = PathTab.Favorites;
        FavControl.ApplySingleTabMode();
        HistControl.SingleTab = PathTab.History;
        HistControl.ApplySingleTabMode();
        WinControl.SingleTab = PathTab.Windows;
        WinControl.ApplySingleTabMode();

        // 面包屑地址栏（段点击 / 编辑态 Enter → 宿主归一化 + 记录历史）
        InitBreadcrumb();

        // 手动 light-dismiss：点击控件外任意处先关全部浮层
        AddHandler(PointerPressedEvent,
            (_, _) => CloseAllPopups(),
            RoutingStrategies.Tunnel);
    }

    /// <summary>由 Path routed changes 反向同步到文本输入框。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PathProperty)
        {
            // 面包屑自己订阅 Path（OneWay）刷新分段；这里只同步左侧速选面板的当前路径
            SyncQuickPathControl();
        }
    }

    private void SyncQuickPathControl()
    {
        var dir = CoerceToDirectory(Path);
        FavControl.SetCurrentPath(dir);
        HistControl.SetCurrentPath(dir);
        WinControl.SetCurrentPath(dir);
    }

    // ── Row 0: 面包屑地址栏 ─────────────────────────────────────────────────

    private void InitBreadcrumb()
    {
        // 编辑态补全：历史匹配 + 父目录下以输入为前缀的子目录（与原 AutoCompleteBox 行为一致）
        Breadcrumb.EditCompletionProvider = text =>
        {
            var t = (text ?? string.Empty).Trim();
            var suggestions = new List<string>();

            if (t.Length >= 2)
            {
                suggestions.AddRange(PathHistoryManager.GetRecent(50)
                    .Select(h => h.Path)
                    .Where(p => p.Contains(t, StringComparison.OrdinalIgnoreCase))
                    .Take(10));

                try
                {
                    var parent = System.IO.Path.GetDirectoryName(t.TrimEnd('\\', '/'));
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        var prefix = System.IO.Path.GetFileName(t.TrimEnd('\\', '/'));
                        suggestions.AddRange(Directory.EnumerateDirectories(parent, prefix + "*", SearchOption.TopDirectoryOnly)
                            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                            .Take(20));
                    }
                }
                catch
                {
                    // 非法路径等，忽略
                }
            }
            else
            {
                // 空文本时给历史记录（等价于旧 ItemsSource 初值）
                suggestions.AddRange(PathHistoryManager.GetRecent(50).Select(h => h.Path).Take(50));
            }

            return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        };

        // 段点击 / 编辑态 Enter：空串守卫 → 归一化为目录 → 记录历史（对齐原 Enter 行为）
        Breadcrumb.NavigateRequested += (_, raw) =>
        {
            var text = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text)) return;

            Path = CoerceToDirectory(text);
            SyncQuickPathControl();
            PathHistoryManager.Record(Path);
        };
    }
    // ── Row 快捷按钮 ────────────────────────────────────────────────────────

    private void QuickFavButton_Click(object? sender, RoutedEventArgs e)
    {
        Breadcrumb.CloseEdit();
        CloseAllPopups();
        FavPopup.IsOpen = true;
    }

    private void QuickHistButton_Click(object? sender, RoutedEventArgs e)
    {
        Breadcrumb.CloseEdit();
        CloseAllPopups();
        HistPopup.IsOpen = true;
    }

    private void QuickWinButton_Click(object? sender, RoutedEventArgs e)
    {
        Breadcrumb.CloseEdit();
        CloseAllPopups();
        WinPopup.IsOpen = true;
    }

    private void FavControl_PathSelected(object? sender, string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        Path = CoerceToDirectory(path);
        FavPopup.IsOpen = false;
    }

    private void HistControl_PathSelected(object? sender, string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        Path = CoerceToDirectory(path);
        HistPopup.IsOpen = false;
    }

    private void WinControl_PathSelected(object? sender, string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        Path = CoerceToDirectory(path);
        WinPopup.IsOpen = false;
    }

    // ── 📁 Browse ───────────────────────────────────────────────────────────

    private async void QuickBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        Breadcrumb.CloseEdit();
        CloseAllPopups();

        var owner = TopLevel.GetTopLevel(this) as Window;
        var picked = BrowseAction != null
            ? await BrowseAction(owner, Path)
            : await CustomFilePickerDialog.ShowFolderAsync(owner!, Path);

        if (!string.IsNullOrEmpty(picked))
        {
            Path = CoerceToDirectory(picked);
                SyncQuickPathControl();
            PathHistoryManager.Record(Path);
        }
    }

    private void CloseAllPopups()
    {
        FavPopup.IsOpen = false;
        HistPopup.IsOpen = false;
        WinPopup.IsOpen = false;
    }

    /// <summary>
    /// 目录归一化：输入框/浏览永远只收目录。
    /// 目录 → 原样；文件 → 父目录；非法/其它/空 → 原样透传（null 安全）。
    /// </summary>
    public static string CoerceToDirectory(string picked)
    {
        if (string.IsNullOrEmpty(picked)) return picked;
        try
        {
            if (Directory.Exists(picked)) return picked;
            if (File.Exists(picked))
            {
                var dir = System.IO.Path.GetDirectoryName(picked);
                return string.IsNullOrEmpty(dir) ? picked : dir;
            }
            return picked;
        }
        catch
        {
            return picked;
        }
    }
}