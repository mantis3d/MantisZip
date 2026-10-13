using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 进度窗口显示偏好（内容模式 / 信息量分级）的持久化解析测试：
/// 枚举 ↔ AppSettings 字符串 双向映射 + 未知/空值回退默认（详细 / 中）。
/// </summary>
public class ProgressDisplayModeParserTests
{
    [Theory]
    [InlineData(ProgressContentMode.Simple, "simple")]
    [InlineData(ProgressContentMode.Detailed, "detailed")]
    [InlineData(ProgressContentMode.List, "list")]
    public void ContentMode_RoundTrips(ProgressContentMode mode, string expected)
    {
        Assert.Equal(expected, ProgressDisplayModeParser.ToSetting(mode));
        Assert.Equal(mode, ProgressDisplayModeParser.ParseContentMode(expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("Detailed")] // 大小写敏感：非精确匹配回退默认
    public void ParseContentMode_UnknownOrNull_FallsBackToDetailed(string? value)
        => Assert.Equal(ProgressContentMode.Detailed, ProgressDisplayModeParser.ParseContentMode(value));

    [Theory]
    [InlineData(ProgressInfoDensity.Minimal, "minimal")]
    [InlineData(ProgressInfoDensity.Medium, "medium")]
    [InlineData(ProgressInfoDensity.Full, "full")]
    public void Density_RoundTrips(ProgressInfoDensity density, string expected)
    {
        Assert.Equal(expected, ProgressDisplayModeParser.ToSetting(density));
        Assert.Equal(density, ProgressDisplayModeParser.ParseDensity(expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    public void ParseDensity_UnknownOrNull_FallsBackToMedium(string? value)
        => Assert.Equal(ProgressInfoDensity.Medium, ProgressDisplayModeParser.ParseDensity(value));

    [Fact]
    public void AppSettings_DisplayPreferenceDefaults_AreDetailedAndMedium()
    {
        var s = new AppSettings();
        Assert.Equal("detailed", s.ProgressContentMode);
        Assert.Equal("medium", s.ProgressInfoDensity);
    }
}
