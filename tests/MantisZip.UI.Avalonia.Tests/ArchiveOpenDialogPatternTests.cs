using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「打开压缩包」对话框筛选模式：内置扩展名 + 分卷首卷 + 设置里的自定义扩展名。
/// </summary>
public class ArchiveOpenDialogPatternTests
{
    [Fact]
    public void DefaultPatterns_CoverBuiltinExtensionsAndFirstVolume()
    {
        var patterns = ArchiveFormatHelper.BuildOpenDialogPatterns();

        Assert.Contains("*.zip", patterns);
        Assert.Contains("*.7z", patterns);
        Assert.Contains("*.rar", patterns);
        // issue #70③：分卷包只有第一卷（*.001）能选，其余卷由引擎回退。
        Assert.Contains("*.001", patterns);
    }

    [Fact]
    public void CustomExtensions_AreNormalizedAndAppended()
    {
        // ".xyz" 原样可用、"bin" 缺前导点、".BIN" 大写 —— 都应归一化成小写带点的模式。
        var patterns = ArchiveFormatHelper.BuildOpenDialogPatterns([".xyz", "bin", ".BIN"]);

        Assert.Contains("*.xyz", patterns);
        Assert.Contains("*.bin", patterns);
        Assert.Single(patterns, p => p.Equals("*.bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidCustomExtensions_AreIgnored()
    {
        var expected = ArchiveFormatHelper.BuildOpenDialogPatterns().Count;

        var patterns = ArchiveFormatHelper.BuildOpenDialogPatterns(["", " ", ".", "..", "a*b", "x/y", "dir\\file"]);

        Assert.Equal(expected, patterns.Count);
        Assert.DoesNotContain(patterns, p => p is "*" or "*.");
    }

    [Fact]
    public void BuiltinDuplicates_AreNotRepeated()
    {
        // 用户可能把 .zip / *.zip 也填进自定义列表，不能出现重复模式。
        var patterns = ArchiveFormatHelper.BuildOpenDialogPatterns([".zip", ".ZIP", "*.zip"]);

        Assert.Single(patterns, p => p.Equals("*.zip", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(".xyz", ".xyz")]
    [InlineData("xyz", ".xyz")]
    [InlineData("  .XyZ  ", ".xyz")]
    [InlineData("", null)]
    [InlineData(".", null)]
    [InlineData("..", null)]
    [InlineData("a*b", null)]
    public void NormalizeCustomExtension_HandlesCommonInputs(string raw, string? expected) =>
        Assert.Equal(expected, ArchiveFormatHelper.NormalizeCustomExtension(raw));
}
