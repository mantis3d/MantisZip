using MantisZip.UI.Avalonia.Utils;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 倒计时行内编辑输入解析（spec §10.4）：0–60 夹紧、非法回退当前剩余值。
/// 纯函数，无 UI 依赖。
/// </summary>
public class CountdownParserTests
{
    [Theory]
    [InlineData("5", 5)]
    [InlineData(" 12 ", 12)]      // 空白容忍
    [InlineData("0", 0)]          // 0 = 立即关闭，合法
    [InlineData("60", 60)]        // 上界
    [InlineData("999", 60)]       // 超上界 → 夹紧
    [InlineData("-3", 0)]         // 负数 → 夹紧 0
    public void ValidNumbers_ClampToRange(string input, int expected)
        => Assert.Equal(expected, CountdownParser.Parse(input, 1));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("3.5")]           // 非整数 → 回退
    public void InvalidInput_FallsBackToRemaining(string? input)
        => Assert.Equal(7, CountdownParser.Parse(input, 7));
}
