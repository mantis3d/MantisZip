using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
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

    // ═══════════════════════════════════════════════════════════
    // 卡片信息行假列头（Tile / Content，thumbnail-card-badge-and-row-bars T3）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 假列头左键排序请求（列头单元 Tapped → MainWindow 订阅 → 复用窗口级排序状态）。
    /// 参数 = 排序成员路径（与 Details 同名：Name/Size/CompressedSize/RatioSort/LastModified）。
    /// </summary>
    internal event EventHandler<string>? CardHeaderSortRequested;

    /// <summary>
    /// 列头单元左键（Tapped）：从 Tag（"Tile:Size" 之类）解析排序路径后抛给 MainWindow。
    /// 右键走整条 Border 的 ContextMenu（语义自然分流，不会同时触发）。
    /// </summary>
    private void CardHeaderUnit_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { Tag: string tag }) return;
        var sep = tag.IndexOf(':');
        if (sep < 0) return;
        CardHeaderSortRequested?.Invoke(this, tag[(sep + 1)..]);
    }

    /// <summary>
    /// 卡片信息行假列头右键菜单：Opening 动态构建，与 Details 列头菜单同构
    /// （ToggleIconBox 可见态图标 + 标题文字 + Click 即切）。数据源 = CardRowCatalog 四行固定清单；
    /// Tile 与 Content 两个列头各一份菜单、各行独立状态（PlacementTarget 的 Tag 区分）。
    /// 菜单挂在整条列头 Border 上 ⇒ 名称单元 / 空条右键也能开（入口永在）。
    /// </summary>
    private void CardRowHeaderContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var isTile = menu.PlacementTarget is Border { Tag: "TileRowHeader" };
        var vm = DataContext as MainWindowViewModel;

        menu.Items.Clear();
        foreach (var kind in CardRowCatalog.Kinds)
        {
            var visible = MainWindowViewModel
                .GetCardRowValue(MantisZip.UI.Avalonia.Models.AppSettings.Load(), isTile, kind);

            // 与 Details 列头菜单同构：ToggleIconBox（可见=强调色底 / 空心）+ Details 同款列头图标
            var iconBox = new Border
            {
                Classes = { "ToggleIconBox" },
                Background = ToggleIconBackground(visible),
                Child = Application.Current?.TryFindResource(CardRowCatalog.IconKey(kind), out var geo)
                        is true && geo is Geometry g
                    ? new PathIcon
                    {
                        Data = g,
                        Width = 10,
                        Height = 10,
                        Foreground = GetThemeBrush("ThemeTextPrimaryBrush")
                    }
                    : null
            };

            var menuItem = new MenuItem
            {
                Icon = iconBox,
                Header = LocalizationManager.T(CardRowCatalog.TitleKey(kind)),
                Tag = kind
            };

            menuItem.Click += (_, _) =>
            {
                if (vm == null) return;
                // 单参命令直调（CommunityToolkit 不生成多参命令，T2 已按 Tile/Content 拆两条）
                if (isTile) vm.ToggleTileRowCommand.Execute(kind);
                else vm.ToggleContentRowCommand.Execute(kind);
                // 只把刚构建的图标翻面即可；菜单每次 Opening 全量重建，无陈旧态问题
                iconBox.Background = ToggleIconBackground(!visible);
            };
            menu.Items.Add(menuItem);
        }
    }

    /// <summary>本地排序箭头刷新（与 MainWindow.SetSortArrow 同规则：激活 ▲/▼，其余清空）。</summary>
    private static void SetCardArrow(TextBlock? arrow, bool isActive, bool descending)
    {
        if (arrow == null) return;
        arrow.Text = isActive ? (descending ? "▼" : "▲") : "";
    }

    /// <summary>
    /// 刷新 Tile / Content 假列头共 10 个排序箭头（精度与 Details 同一份
    /// _lastSortMemberPath/_lastSortDescending 同步——由 MainWindow.UpdateSortArrows 统一调用）。
    /// Name 单元固定映射 "Name"；其余四个 = Details 列头同路径（走既有 GetSortValue）。
    /// </summary>
    internal void UpdateCardSortArrows(string? sortPath, bool descending)
    {
        SetCardArrow(TileSortArrowName, sortPath == "Name", descending);
        SetCardArrow(TileSortArrowSize, sortPath == "Size", descending);
        SetCardArrow(TileSortArrowCompressedSize, sortPath == "CompressedSize", descending);
        SetCardArrow(TileSortArrowRatioSort, sortPath == "RatioSort", descending);
        SetCardArrow(TileSortArrowLastModified, sortPath == "LastModified", descending);
        SetCardArrow(ContentSortArrowName, sortPath == "Name", descending);
        SetCardArrow(ContentSortArrowSize, sortPath == "Size", descending);
        SetCardArrow(ContentSortArrowCompressedSize, sortPath == "CompressedSize", descending);
        SetCardArrow(ContentSortArrowRatioSort, sortPath == "RatioSort", descending);
        SetCardArrow(ContentSortArrowLastModified, sortPath == "LastModified", descending);
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