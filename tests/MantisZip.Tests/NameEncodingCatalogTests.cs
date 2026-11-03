using System.Text;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// <see cref="NameEncodingCatalog"/>：文件名代码页键表的解析与回显。
/// 这张表是「工具栏下拉 / 设置页默认值 / 引擎读写」三处共用的单一事实源，
/// 任何一个键被解析错都会让用户选了的代码页静默失效，所以逐个键钉住。
/// </summary>
public class NameEncodingCatalogTests
{
    [Theory]
    [InlineData("utf-8", 65001)]
    [InlineData("gbk", 936)]
    [InlineData("gb18030", 54936)]
    [InlineData("big5", 950)]
    [InlineData("shift-jis", 932)]
    [InlineData("euc-kr", 949)]
    public void TryResolve_KnownKeys_MapToExpectedCodePage(string key, int expectedCodePage)
    {
        Assert.True(NameEncodingCatalog.TryResolve(key, out var encoding));
        Assert.NotNull(encoding);
        Assert.Equal(expectedCodePage, encoding!.CodePage);
    }

    [Fact]
    public void TryResolve_OtherPrefix_UsesExplicitCodePage()
    {
        Assert.True(NameEncodingCatalog.TryResolve("other:950", out var encoding));
        Assert.Equal(950, encoding!.CodePage);
    }

    [Fact]
    public void TryResolve_UnknownKey_ReturnsFalse_WithoutSilentFallback()
    {
        // 「认不出就换一个」会让误选的代码页看起来生效、实际写坏名字，所以必须显式失败
        Assert.False(NameEncodingCatalog.TryResolve("klingon-8", out var encoding));
        Assert.Null(encoding);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void IsAuto_TreatsEmptyAndAutoAsAutomatic(string? key)
    {
        Assert.True(NameEncodingCatalog.IsAuto(key));
    }

    [Fact]
    public void Describe_RoundTripsKnownAndUnknownCodePages()
    {
        Assert.Equal("big5", NameEncodingCatalog.Describe(Encoding.GetEncoding(950)));
        Assert.Equal("other:437", NameEncodingCatalog.Describe(Encoding.GetEncoding(437)));
        // null 表示「引擎自动检测」，回显为 auto 而不是空字符串
        Assert.Equal("auto", NameEncodingCatalog.Describe(null));
    }
}
