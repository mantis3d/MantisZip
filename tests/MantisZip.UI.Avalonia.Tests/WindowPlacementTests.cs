using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// <see cref="WindowPlacement"/> 的边界行为：非法保存值必须回落，合法值不得小于窗口最小尺寸。
/// 这几个判断是「布局记忆」的安全阀——少一个就会让窗口在下次打开时变成 0×0 或跑到屏幕外。
/// </summary>
public class WindowPlacementTests
{
    [Theory]
    [InlineData(0, 1080)]
    [InlineData(-5, 1080)]
    [InlineData(double.NaN, 1080)]
    [InlineData(double.PositiveInfinity, 1080)]
    [InlineData(1234, 1234)]
    public void Capture_FallsBackForUnusableValues(double current, double expected)
        => Assert.Equal(expected, WindowPlacement.Capture(current, 1080));

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(0.5, true)]
    public void IsUsable_RejectsNonPositiveAndNonFinite(double value, bool expected)
        => Assert.Equal(expected, WindowPlacement.IsUsable(value));

    [AvaloniaFact]
    public void Apply_ClampsToWindowMinimum()
    {
        var window = new Window { MinWidth = 200, MinHeight = 100, Width = 900, Height = 700 };

        WindowPlacement.Apply(window, 150, 80);

        Assert.Equal(200, window.Width);
        Assert.Equal(100, window.Height);
    }

    [AvaloniaFact]
    public void Apply_KeepsXamlDefaultsWhenSavedValuesUnusable()
    {
        var window = new Window { MinWidth = 200, MinHeight = 100, Width = 900, Height = 700 };

        WindowPlacement.Apply(window, 0, double.NaN);

        Assert.Equal(900, window.Width);
        Assert.Equal(700, window.Height);
    }

    [AvaloniaFact]
    public void LeftColumn_RoundTripsThroughCaptureAndApply()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("500,5,*") };

        WindowPlacement.ApplyLeftColumn(grid, 620, 500);
        Assert.Equal(620, grid.ColumnDefinitions[0].Width.Value);

        Assert.Equal(620, WindowPlacement.CaptureLeftColumn(grid, 500));
    }

    [AvaloniaFact]
    public void LeftColumn_CaptureFallsBackForStarColumn()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,5,*") };

        Assert.Equal(500, WindowPlacement.CaptureLeftColumn(grid, 500));
    }

    [AvaloniaFact]
    public void LeftColumn_IgnoresNonGrid()
    {
        var panel = new StackPanel();

        // 不抛异常、不做任何事：调用点传错控件时也要安静降级
        WindowPlacement.ApplyLeftColumn(panel, 620, 500);
        Assert.Equal(500, WindowPlacement.CaptureLeftColumn(panel, 500));
    }
}
