using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// <see cref="ArchiveItemModel.LastModifiedShortDisplay"/>（thumbnail-card-badge-and-row-bars T4）：
/// Tile 迷你行宽度放不下 19 字符时间戳，取 yyyy-MM-dd 前缀切片。
/// 语义钉子：MinValue/空显示 → 空串原样返回；正常时间戳 → 前 10 字符。
/// 该属性依赖 LastModifiedDisplay 固定 "yyyy-MM-dd HH:mm:ss" 格式的**前缀稳定性**
/// （模型注释有同样的钉子），若日期格式将来变更必须同步切片。
/// </summary>
public class LastModifiedShortDisplayTests
{
    private static ArchiveItemModel Create(DateTime lastModified)
    {
        return new ArchiveItemModel { Name = "a.zip", LastModified = lastModified, Size = 1 };
    }

    [Fact]
    public void ShortDisplay_TakesFirst10Chars_OfLongTimestamp()
    {
        var item = Create(new DateTime(2026, 11, 7, 15, 29, 55));

        Assert.Equal("2026-11-07", item.LastModifiedShortDisplay);
    }

    [Fact]
    public void ShortDisplay_EmptyForMinValue()
    {
        var item = Create(DateTime.MinValue);

        Assert.Equal(string.Empty, item.LastModifiedShortDisplay);
    }
}
