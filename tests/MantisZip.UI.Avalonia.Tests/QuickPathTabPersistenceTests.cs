using MantisZip.UI.Avalonia.Controls;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「左侧路径切换面板记住上次标签」的还原逻辑：持久化值是枚举成员名字符串，
/// 空值/未知值/越界数值必须回退到「收藏」，保证首次启动与旧配置文件的行为
/// 与改动前的硬编码默认值一致（见 AppSettings.PickerSourceTab）。
/// </summary>
public class QuickPathTabPersistenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotATab")]
    [InlineData("99")]
    public void ParseTabOrDefault_EmptyOrUnknown_FallsBackToFavorites(string? stored)
    {
        Assert.Equal(PathTab.Favorites, QuickPathControl.ParseTabOrDefault(stored));
    }

    [Theory]
    [InlineData("Favorites", PathTab.Favorites)]
    [InlineData("History", PathTab.History)]
    [InlineData("Windows", PathTab.Windows)]
    [InlineData("Tree", PathTab.Tree)]
    [InlineData("tree", PathTab.Tree)]
    [InlineData("HISTORY", PathTab.History)]
    public void ParseTabOrDefault_KnownName_ReturnsThatTab(string stored, PathTab expected)
    {
        Assert.Equal(expected, QuickPathControl.ParseTabOrDefault(stored));
    }
}
