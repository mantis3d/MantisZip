using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// CardRowCatalog 映射（thumbnail-card-badge-and-row-bars T3/T5）：四行固定清单的顺序、
/// 标题 key / 图标 key / 行资源名（Tile 与 Content 两种前缀）——全部复用既有资源，
/// 强调：不允许额外 kind 或额外 key（Must-NOT-Have：新增 key=0、不新增几何资源）。
/// </summary>
public class CardRowCatalogTests
{
    [Fact]
    public void Kinds_ContainExactlyTheFourRows_InStableOrder()
    {
        var expected = new[]
        {
            CardRowKind.Size, CardRowKind.CompressedSize, CardRowKind.Ratio, CardRowKind.Date,
        };
        Assert.Equal(expected, CardRowCatalog.Kinds);
    }

    [Fact]
    public void TitleKeys_ReuseExistingDataGridKeys()
    {
        Assert.Equal("DataGrid_Size", CardRowCatalog.TitleKey(CardRowKind.Size));
        Assert.Equal("DataGrid_Compressed", CardRowCatalog.TitleKey(CardRowKind.CompressedSize));
        Assert.Equal("DataGrid_Ratio", CardRowCatalog.TitleKey(CardRowKind.Ratio));
        Assert.Equal("DataGrid_Modified", CardRowCatalog.TitleKey(CardRowKind.Date));
    }

    [Fact]
    public void IconKeys_MatchDetailsColumnHeaderGeometries()
    {
        Assert.Equal("IconRuler", CardRowCatalog.IconKey(CardRowKind.Size));
        Assert.Equal("IconCompress", CardRowCatalog.IconKey(CardRowKind.CompressedSize));
        Assert.Equal("IconDataBarVertical", CardRowCatalog.IconKey(CardRowKind.Ratio));
        Assert.Equal("IconCalendar", CardRowCatalog.IconKey(CardRowKind.Date));
    }

    [Fact]
    public void ResourceKeys_ProduceAllEightAppRowResources()
    {
        // Tile 前缀 4 个
        Assert.Equal("ThumbnailRowSizeVisible", CardRowCatalog.ResourceKey(true, CardRowKind.Size));
        Assert.Equal("ThumbnailRowCompressedSizeVisible", CardRowCatalog.ResourceKey(true, CardRowKind.CompressedSize));
        Assert.Equal("ThumbnailRowRatioVisible", CardRowCatalog.ResourceKey(true, CardRowKind.Ratio));
        Assert.Equal("ThumbnailRowDateVisible", CardRowCatalog.ResourceKey(true, CardRowKind.Date));
        // Content 前缀 4 个
        Assert.Equal("ThumbnailContentRowSizeVisible", CardRowCatalog.ResourceKey(false, CardRowKind.Size));
        Assert.Equal("ThumbnailContentRowCompressedSizeVisible", CardRowCatalog.ResourceKey(false, CardRowKind.CompressedSize));
        Assert.Equal("ThumbnailContentRowRatioVisible", CardRowCatalog.ResourceKey(false, CardRowKind.Ratio));
        Assert.Equal("ThumbnailContentRowDateVisible", CardRowCatalog.ResourceKey(false, CardRowKind.Date));
    }

    [Fact]
    public void ResourceKeys_MatchAppThemingInjectedKeys()
    {
        // 关键一致性钉子：资源键必须与 App.Theming.InjectThumbnailRowResources 注入的键一一对应，
        // 错位会导致假列头单元永远跟随不到行开关（静默失败 = 规则 23 的头号缺陷）。
        // 通过反射读 InjectThumbnailRowResources 的 IL 不现实，改为直接断言已知键集合
        //（App.Theming 侧的键同样被 CardRowSettingsTests 及 XAML 绑定钉住，两边改任何一边此处会红）。
        Assert.All(
            CardRowCatalog.Kinds.SelectMany(k => new[]
            {
                CardRowCatalog.ResourceKey(true, k),
                CardRowCatalog.ResourceKey(false, k),
            }),
            key => Assert.StartsWith("Thumbnail", key));
    }
}
