using Xunit;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// RecentFilesManager 单元测试
/// </summary>
public class RecentFilesManagerTests
{
    /// <summary>
    /// ResolveMaxEntries: 配置值 <= 0 时回退到默认值 10
    /// </summary>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(-1, 10)]
    public void ResolveMaxEntries_ConfiguredValueZeroOrNegative_ReturnsDefault(int configured, int expected)
    {
        // Act
        var result = RecentFilesManager.ResolveMaxEntries(configured);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// ResolveMaxEntries: 配置值 > 0 时直接使用该值
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(50, 50)]
    public void ResolveMaxEntries_ConfiguredValuePositive_ReturnsConfigured(int configured, int expected)
    {
        // Act
        var result = RecentFilesManager.ResolveMaxEntries(configured);

        // Assert
        Assert.Equal(expected, result);
    }
}