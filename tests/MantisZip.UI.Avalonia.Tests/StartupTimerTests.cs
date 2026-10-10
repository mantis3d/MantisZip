using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// StartupTimer 打点器单测。
/// 注意：打点器是进程级单例，每个测试通过 ResetForTest() 隔离（internal，经 InternalsVisibleTo）。
/// </summary>
public class StartupTimerTests
{
    public StartupTimerTests() => StartupTimer.ResetForTest();

    [Fact]
    public void Begin_ThenMark_RecordsEntryAndMarks()
    {
        StartupTimer.Begin();
        StartupTimer.Mark("PhaseA");

        var marks = StartupTimer.SnapshotForTest();
        Assert.Equal(new[] { "Main.Entry", "PhaseA" }, marks.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Mark_DeltasAreNonNegative()
    {
        StartupTimer.Begin();
        Thread.Sleep(10); // 制造真实间隔
        StartupTimer.Mark("Slow");

        var marks = StartupTimer.SnapshotForTest();
        var slow = marks.Single(m => m.Name == "Slow");
        Assert.True(slow.DeltaMs >= 5, $"DeltaMs={slow.DeltaMs}");
        Assert.True(slow.CumulativeMs >= slow.DeltaMs);
    }

    [Fact]
    public void Mark_BeforeBegin_IsIgnored()
    {
        StartupTimer.Mark("TooEarly");
        Assert.Empty(StartupTimer.SnapshotForTest());
    }

    [Fact]
    public void Mark_AfterFlush_IsIgnored()
    {
        StartupTimer.Begin();
        StartupTimer.Mark("Before");
        StartupTimer.FlushForTest(); // 标记已 flush，不写文件
        StartupTimer.Mark("After");

        Assert.DoesNotContain(StartupTimer.SnapshotForTest(), m => m.Name == "After");
    }

    [Fact]
    public void Begin_Twice_IsIdempotent()
    {
        StartupTimer.Begin();
        var first = StartupTimer.SnapshotForTest().Count;
        StartupTimer.Begin();
        Assert.Equal(first, StartupTimer.SnapshotForTest().Count);
    }

    [Fact]
    public void Disabled_AfterError_NeverThrows()
    {
        StartupTimer.SimulateFailureForTest();
        StartupTimer.Begin();
        StartupTimer.Mark("X");
        StartupTimer.FlushForTest(); // 全部静默 no-op，不抛异常
        Assert.Empty(StartupTimer.SnapshotForTest());
    }

    [Fact]
    public void Snapshot_ContainsProcessStartBaseline()
    {
        StartupTimer.Begin();
        var marks = StartupTimer.SnapshotForTest();
        // Main.Entry 的 CumulativeMs 含 pre-Main 进程启动基线（≥0），DeltaMs 为相对 T0 的首段增量
        var entry = marks.Single(m => m.Name == "Main.Entry");
        Assert.True(entry.CumulativeMs >= 0);
        Assert.True(entry.DeltaMs >= 0);
    }
}
