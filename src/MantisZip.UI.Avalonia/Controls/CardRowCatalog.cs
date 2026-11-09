using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// 卡片信息行固定清单（thumbnail-card-badge-and-row-bars T3）：Tile / Content 假列头右键菜单
/// 与列头单元共用的映射表——每行 = 标题 key / 列头图标 Geometry key / 行可见性资源名。
/// 文字与图标全部复用 Details 列头的既有三语 key 与 Geometry（计划决策 9 + Must-NOT-Have：
/// 不新增 i18n key、不新增几何图标资源）。枚举值 → 字段的读写映射在
/// MainWindowViewModel.GetCardRowValue / ApplyCardRow，此表只放展示与绑定标识。
/// </summary>
internal static class CardRowCatalog
{
    /// <summary>四行固定顺序（菜单与列头单元排布一致）。</summary>
    public static readonly CardRowKind[] Kinds = [CardRowKind.Size, CardRowKind.CompressedSize, CardRowKind.Ratio, CardRowKind.Date];

    /// <summary>行的三语标题 key（复用 DataGrid_* 列头文案）。</summary>
    public static string TitleKey(CardRowKind kind) => kind switch
    {
        CardRowKind.Size => "DataGrid_Size",
        CardRowKind.CompressedSize => "DataGrid_Compressed",
        CardRowKind.Ratio => "DataGrid_Ratio",
        CardRowKind.Date => "DataGrid_Modified",
        _ => "DataGrid_Size",
    };

    /// <summary>行的列头图标 Geometry key（与 Details 列头 PathIcon 同款）。</summary>
    public static string IconKey(CardRowKind kind) => kind switch
    {
        CardRowKind.Size => "IconRuler",
        CardRowKind.CompressedSize => "IconCompress",
        CardRowKind.Ratio => "IconDataBarVertical",
        CardRowKind.Date => "IconCalendar",
        _ => "IconRuler",
    };

    /// <summary>行可见性资源名（Tile 前缀 ThumbnailRow… / Content 前缀 ThumbnailContentRow…，
    /// 与 App.Theming.InjectThumbnailRowResources 注入的资源键一一对应）。</summary>
    public static string ResourceKey(bool isTile, CardRowKind kind)
    {
        var prefix = isTile ? "ThumbnailRow" : "ThumbnailContentRow";
        var suffix = kind switch
        {
            CardRowKind.Size => "Size",
            CardRowKind.CompressedSize => "CompressedSize",
            CardRowKind.Ratio => "Ratio",
            CardRowKind.Date => "Date",
            _ => "Size",
        };
        return $"{prefix}{suffix}Visible";
    }
}
