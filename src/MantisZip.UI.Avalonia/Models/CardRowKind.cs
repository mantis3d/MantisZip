namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 卡片信息行的四行标识（thumbnail-card-badge-and-row-bars 计划 T2）。
/// 与 Tile / Content 两模式的「大小 / 压缩后大小 / 压缩率 / 修改日期」四个迷你行一一对应，
/// 也是假列头右键菜单（ArchiveContentView.CardRowHeaderContextMenu_Opening）的固定数据源。
/// 枚举值只作标识，具体字段映射集中在 MainWindowViewModel.GetCardRowValue / ApplyCardRow（不许反射）。
/// </summary>
public enum CardRowKind
{
    Size,
    CompressedSize,
    Ratio,
    Date,
}
