using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「插入占位符」的纯函数测试：不依赖窗口，只覆盖文本拼接与光标兜底的边界。
/// </summary>
public class PlaceholderInsertionTests
{
    [Fact]
    public void Insert_IntoEmptyText_ProducesTokenAndCaretAfterIt()
    {
        var result = PlaceholderInsertion.Insert(string.Empty, 0, 0, "{name}", out var caret);

        Assert.Equal("{name}", result);
        Assert.Equal(6, caret);
    }

    [Fact]
    public void Insert_NullText_TreatedAsEmpty()
    {
        var result = PlaceholderInsertion.Insert(null, 0, 0, "{seq}", out var caret);

        Assert.Equal("{seq}", result);
        Assert.Equal(5, caret);
    }

    [Fact]
    public void Insert_AtMiddle_KeepsBothSides()
    {
        var result = PlaceholderInsertion.Insert("report.zip", 6, 6, "_{date}", out var caret);

        Assert.Equal("report_{date}.zip", result);
        Assert.Equal(13, caret);
    }

    [Fact]
    public void Insert_AtEnd_AppendsToken()
    {
        var result = PlaceholderInsertion.Insert("report", 6, 6, "_{date}", out var caret);

        Assert.Equal("report_{date}", result);
        Assert.Equal(13, caret);
    }

    [Fact]
    public void Insert_WithSelection_ReplacesIt()
    {
        var result = PlaceholderInsertion.Insert("report_{date}", 7, 13, "{name}", out var caret);

        Assert.Equal("report_{name}", result);
        Assert.Equal(13, caret);
    }

    [Fact]
    public void Insert_ReversedSelection_IsNormalized()
    {
        var forward = PlaceholderInsertion.Insert("abcdef", 1, 3, "X", out var forwardCaret);
        var reversed = PlaceholderInsertion.Insert("abcdef", 3, 1, "X", out var reversedCaret);

        Assert.Equal("aXdef", forward);
        Assert.Equal(forward, reversed);
        Assert.Equal(forwardCaret, reversedCaret);
    }

    [Fact]
    public void Insert_NegativeOrOversizedIndices_AreClamped()
    {
        var negative = PlaceholderInsertion.Insert("abc", -5, -1, "X", out var negativeCaret);
        var oversized = PlaceholderInsertion.Insert("abc", 99, 99, "X", out var oversizedCaret);

        Assert.Equal("Xabc", negative);
        Assert.Equal(1, negativeCaret);
        Assert.Equal("abcX", oversized);
        Assert.Equal(4, oversizedCaret);
    }

    [Fact]
    public void ResolveInsertIndex_NegativeCaret_FallsBackToEnd()
    {
        Assert.Equal(6, PlaceholderInsertion.ResolveInsertIndex("report", -1));
    }

    [Fact]
    public void ResolveInsertIndex_CaretPastEnd_FallsBackToEnd()
    {
        Assert.Equal(6, PlaceholderInsertion.ResolveInsertIndex("report", 99));
    }

    [Fact]
    public void ResolveInsertIndex_ValidCaret_IsKept()
    {
        Assert.Equal(3, PlaceholderInsertion.ResolveInsertIndex("report", 3));
    }

    [Fact]
    public void ResolveInsertIndex_NullText_TreatedAsEmpty()
    {
        Assert.Equal(0, PlaceholderInsertion.ResolveInsertIndex(null, 0));
    }
}
