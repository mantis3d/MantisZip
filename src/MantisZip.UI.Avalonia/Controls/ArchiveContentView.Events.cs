using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform;
using MantisZip.UI.Avalonia.Converters;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Controls;

// ═══════════════════════════════════════════════════════════
// 内容区事件处理器（随内容区一并迁入）
// ═══════════════════════════════════════════════════════════

public partial class ArchiveContentView
{

    private void ColumnHeaderContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Items.Clear();

        foreach (var column in FileListGrid.Columns)
        {
            var header = GetColumnHeaderText(column);
            // 名称列不允许隐藏（图标列无表头文字，同样跳过）
            if (header == LocalizationManager.T("DataGrid_Name") || string.IsNullOrEmpty(header))
                continue;

            // 与主菜单切换图标同构：ToggleIconBox（可见=强调色底，隐藏=透明空心），16×16 适配 Icon 槽位
            var iconBox = new Border
            {
                Classes = { "ToggleIconBox" },
                Background = ToggleIconBackground(column.IsVisible),
                Child = GetColumnHeaderIcon(column) is { } iconData
                    ? new PathIcon
                    {
                        Data = iconData,
                        Width = 10,
                        Height = 10,
                        Foreground = GetThemeBrush("ThemeTextPrimaryBrush")
                    }
                    : null
            };

            // 与主菜单切换项同构：Icon 槽位（ToggleIconBox）+ Header 字符串
            var menuItem = new MenuItem
            {
                Icon = iconBox,
                Header = header,
                Tag = column
            };
            menuItem.Click += (s, args) =>
            {
                column.IsVisible = !column.IsVisible;
                iconBox.Background = ToggleIconBackground(column.IsVisible);
            };
            menu.Items.Add(menuItem);
        }
    }

    /// <summary>可见 → ThemeToggleBrush（强调色底），隐藏 → Transparent（空心），与主菜单切换图标一致。</summary>
    private static IBrush ToggleIconBackground(bool isVisible)
    {
        return new BoolToToggleBgBrushConverter().Convert(isVisible, typeof(IBrush), null, CultureInfo.InvariantCulture) as IBrush
            ?? Brushes.Transparent;
    }

    /// <summary>从列标题的 StackPanel 中提取 PathIcon 的几何，与列头图标同款，保证视觉一致。</summary>
    private static Geometry? GetColumnHeaderIcon(DataGridColumn column)
    {
        if (column.Header is StackPanel panel)
        {
            var icon = panel.Children.OfType<PathIcon>().FirstOrDefault();
            if (icon != null)
                return icon.Data;
        }
        return null;
    }

    /// <summary>从当前应用资源中取主题画刷（主题切换后动态解析）。</summary>
    private static IBrush GetThemeBrush(string key)
    {
        if (Application.Current?.TryFindResource(key, out var brush) == true && brush is IBrush b)
            return b;
        return Brushes.Gray;
    }

    private static string GetColumnHeaderText(DataGridColumn column)
    {
        if (column.Header is string s)
            return s.TrimEnd('▲', '▼', ' ').TrimEnd();
        if (column.Header is StackPanel panel)
        {
            // 取第一个 TextBlock（列标题文字）。最后一个 TextBlock 是排序箭头指示
            // （NameHeaderArrow 等，初始 Text=""），LastOrDefault 会误取到空箭头导致菜单空白
            var tb = panel.Children.OfType<TextBlock>().FirstOrDefault();
            if (tb != null)
                return tb.Text ?? "";
        }
        return "";
    }
}