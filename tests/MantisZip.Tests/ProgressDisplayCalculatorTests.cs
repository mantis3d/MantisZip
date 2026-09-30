using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>ProgressDisplayCalculator 纯函数 + ProgressSpeedTracker 边界单测
/// （前缀剥离/路径分离/总进度/时长/ETA 守卫全覆盖）。</summary>
public class ProgressDisplayCalculatorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ── StripEnginePrefix ──

    [Theory]
    [InlineData("正在压缩: a/b.zip", "a/b.zip")]
    [InlineData("正在解压: a/b.zip", "a/b.zip")]
    [InlineData("Compressing: a/b.zip", "a/b.zip")]
    [InlineData("Extracting: a/b.zip", "a/b.zip")]
    [InlineData("plain/path.txt", "plain/path.txt")]
    [InlineData("", "")]
    public void StripEnginePrefix_ReturnsExpected(string input, string expected)
        => Assert.Equal(expected, ProgressDisplayCalculator.StripEnginePrefix(input));

    [Fact]
    public void StripEnginePrefix_Null_ReturnsEmpty()
        => Assert.Equal(string.Empty, ProgressDisplayCalculator.StripEnginePrefix(null));

    // ── SplitFilePath ──

    [Theory]
    [InlineData("dir/sub/file.txt", "dir/sub", "file.txt")]
    [InlineData("file.txt", "", "file.txt")]
    [InlineData(@"a\b\c.txt", "a/b", "c.txt")]
    [InlineData("", "", "")]
    [InlineData("正在压缩: dir/file.txt", "dir", "file.txt")]  // 内部先剥前缀
    public void SplitFilePath_ReturnsExpected(string input, string expectedDir, string expectedName)
    {
        var (dir, name) = ProgressDisplayCalculator.SplitFilePath(input);
        Assert.Equal(expectedDir, dir);
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void SplitFilePath_Null_DoesNotThrow_ReturnsEmpty()
    {
        var (dir, name) = ProgressDisplayCalculator.SplitFilePath(null);
        Assert.Equal(string.Empty, dir);
        Assert.Equal(string.Empty, name);
    }

    // ── ComputeOverallPercent ──

    [Theory]
    [InlineData(0, 50, 4, 12.5)]
    [InlineData(3, 100, 4, 100)]
    [InlineData(0, 50, 0, 50)]    // total=0 回退：clamp(currentPercent)
    [InlineData(0, 150, 0, 100)]  // total=0 回退 + 越界 clamp
    [InlineData(0, -50, 0, 0)]    // total=0 回退 + 负值 clamp
    [InlineData(0, 150, 4, 25)]   // 当前百分比越界 → clamp 后参与计算
    [InlineData(10, 50, 4, 100)]  // 结果越界 → clamp 到 100
    public void ComputeOverallPercent_ReturnsClampedValue(int completed, double current, int total, double expected)
        => Assert.Equal(expected, ProgressDisplayCalculator.ComputeOverallPercent(completed, current, total), 6);

    // ── FormatDuration ──

    [Theory]
    [InlineData(5, "0:00:05")]
    [InlineData(0, "0:00:00")]
    [InlineData(3723, "1:02:03")]     // 1h2m3s
    [InlineData(7320, "2:02:00")]     // 2h2m
    public void FormatDuration_Under24h_UsesHms(double seconds, string expected)
        => Assert.Equal(expected, ProgressDisplayCalculator.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void FormatDuration_25h_UsesDaySegment()
        => Assert.Equal("1.01:00:00", ProgressDisplayCalculator.FormatDuration(TimeSpan.FromHours(25)));

    [Fact]
    public void FormatDuration_Negative_ClampsToZero()
        => Assert.Equal("0:00:00", ProgressDisplayCalculator.FormatDuration(TimeSpan.FromSeconds(-5)));

    // ── ProgressSpeedTracker ──

    [Fact]
    public void SpeedTracker_FirstSampleWithoutSwitch_InitializesBaseline_ReturnsZero()
    {
        var t = new ProgressSpeedTracker();
        Assert.Equal(0, t.RecordSample(1000, T0));
    }

    [Fact]
    public void SpeedTracker_SamplingAndEmaSmoothing()
    {
        var t = new ProgressSpeedTracker();
        t.OnArchiveSwitch(0, T0);
        var s1 = t.RecordSample(1000, T0.AddSeconds(1));   // inst=1000 → ema=1000
        Assert.Equal(1000, s1, 3);
        var s2 = t.RecordSample(3000, T0.AddSeconds(2));   // inst=2000 → ema=1000*0.7+2000*0.3=1300
        Assert.Equal(1300, s2, 3);
    }

    [Fact]
    public void SpeedTracker_Throttle_IgnoresSamplesWithin100ms()
    {
        var t = new ProgressSpeedTracker();
        t.OnArchiveSwitch(0, T0);
        var s1 = t.RecordSample(1000, T0.AddSeconds(1));
        var s2 = t.RecordSample(2000, T0.AddMilliseconds(1050));  // dt=0.05 < 0.1 → 返回既有 ema
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void SpeedTracker_ArchiveSwitch_ResetsEmaAndBaseline()
    {
        var t = new ProgressSpeedTracker();
        t.OnArchiveSwitch(0, T0);
        t.RecordSample(5000, T0.AddSeconds(5));   // ema=1000
        t.OnArchiveSwitch(5000, T0.AddSeconds(10));  // 切档：基线=5000、ema 归零
        Assert.Null(t.ComputeEtaSeconds(0, 1000));   // ema=0 → null（归零生效）
        var s = t.RecordSample(6000, T0.AddSeconds(11));  // dt=1, delta=1000 → ema=1000 重起
        Assert.Equal(1000, s, 3);
        Assert.Equal(1000, t.ProcessedInArchive(6000));   // 相对新基线
    }

    [Fact]
    public void SpeedTracker_ByteRegression_ResetsBaseline_ReturnsZero()
    {
        var t = new ProgressSpeedTracker();
        t.OnArchiveSwitch(0, T0);
        t.RecordSample(5000, T0.AddSeconds(1));
        var s = t.RecordSample(1000, T0.AddSeconds(2));   // 字节回退 → 内部重置并返回 0
        Assert.Equal(0, s);
        Assert.Equal(0, t.ProcessedInArchive(1000));      // 新基线=1000 → 0
    }

    [Fact]
    public void SpeedTracker_Eta_EdgeCases()
    {
        var t = new ProgressSpeedTracker();
        Assert.Null(t.ComputeEtaSeconds(0, 100));   // 尚无速度 → null

        t.OnArchiveSwitch(0, T0);
        t.RecordSample(1000, T0.AddSeconds(1));     // ema=1000
        Assert.Equal(0, t.ComputeEtaSeconds(100, 100));          // 剩 0 → 0
        Assert.Equal(0.5, t.ComputeEtaSeconds(500, 1000)!.Value, 3);  // 500/1000
        Assert.Null(t.ComputeEtaSeconds(0, 0));                  // 总量 0 → null
    }

    [Fact]
    public void SpeedTracker_ProcessedInArchive_ClampsNonNegative()
    {
        var t = new ProgressSpeedTracker();
        t.OnArchiveSwitch(5000, T0);
        Assert.Equal(0, t.ProcessedInArchive(3000));   // 低于基线 → 0
        Assert.Equal(2000, t.ProcessedInArchive(7000));
    }
}
