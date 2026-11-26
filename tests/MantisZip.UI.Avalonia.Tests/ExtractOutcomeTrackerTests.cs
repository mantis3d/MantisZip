using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 解压「到底写出了几个文件」的旁路统计回归（issue #91）。
/// <para>
/// 引擎对<b>单个条目</b>失败只上报 <see cref="ArchiveEntryStatus.Failed"/> 进度事件、**不抛异常**，
/// 调用方若只看 Task 是否抛错，就会出现「进度窗口显示完成、目标目录空无一物」。
/// 这类静默失败最典型的就是 WinZip AES 密码错误（逐条目被吞掉，最终 0 文件仍报成功）。
/// </para>
/// </summary>
public class ExtractOutcomeTrackerTests
{
    private static ArchiveProgress Status(ArchiveEntryStatus status)
        => new() { EntryStatus = status };

    [Fact]
    public void WrittenEntries_DoNotThrow()
    {
        var tracker = new ExtractOutcomeTracker(null);
        tracker.Report(Status(ArchiveEntryStatus.Completed));
        tracker.ThrowIfNothingWritten();
    }

    [Fact]
    public void OverwrittenCountsAsWritten()
    {
        var tracker = new ExtractOutcomeTracker(null);
        tracker.Report(Status(ArchiveEntryStatus.Overwritten));
        Assert.Equal(1, tracker.WrittenCount);
        tracker.ThrowIfNothingWritten();
    }

    [Fact]
    public void AllFailed_Throws()
    {
        var tracker = new ExtractOutcomeTracker(null);
        tracker.Report(Status(ArchiveEntryStatus.Failed));
        tracker.Report(Status(ArchiveEntryStatus.Failed));

        Assert.Equal(2, tracker.FailedCount);
        var ex = Assert.Throws<InvalidDataException>(() => tracker.ThrowIfNothingWritten());
        Assert.Contains("2", ex.Message);
    }

    /// <summary>
    /// 全部被跳过是用户的明确选择（冲突策略选「跳过」），不算失败，不能抛错。
    /// </summary>
    [Fact]
    public void AllSkipped_DoesNotThrow()
    {
        var tracker = new ExtractOutcomeTracker(null);
        tracker.Report(Status(ArchiveEntryStatus.Skipped));
        tracker.Report(Status(ArchiveEntryStatus.Skipped));
        tracker.ThrowIfNothingWritten();
    }

    /// <summary>
    /// 部分成功、部分失败：不属于「什么都没解出来」，只走既有逐条目失败统计，不抛错。
    /// </summary>
    [Fact]
    public void PartialFailure_DoesNotThrow()
    {
        var tracker = new ExtractOutcomeTracker(null);
        tracker.Report(Status(ArchiveEntryStatus.Completed));
        tracker.Report(Status(ArchiveEntryStatus.Failed));
        tracker.ThrowIfNothingWritten();
    }

    /// <summary>
    /// 兜底失败数（引擎返回的 <c>ExtractResult.FailedEntries</c>）在进度事件缺失时仍能触发报错。
    /// </summary>
    [Fact]
    public void FallbackFailedEntries_TriggersThrowWhenNoProgressSeen()
    {
        var tracker = new ExtractOutcomeTracker(null);
        var ex = Assert.Throws<InvalidDataException>(() => tracker.ThrowIfNothingWritten(3));
        Assert.Contains("3", ex.Message);
    }

    /// <summary>
    /// 密码重试会重新跑一轮引擎：上一轮写到一半的计数若不清零，
    /// 「上一轮写了几个、本轮全失败」会被旧计数掩盖成成功。
    /// </summary>
    [Fact]
    public void Reset_ClearsCountsSoRetryIsJudgedIndependently()
    {
        var tracker = new ExtractOutcomeTracker(null);
        tracker.Report(Status(ArchiveEntryStatus.Completed));
        tracker.Reset();

        Assert.Equal(0, tracker.WrittenCount);
        Assert.Equal(0, tracker.FailedCount);
        Assert.Equal(0, tracker.SkippedCount);
    }

    [Fact]
    public void InnerProgress_StillReceivesEveryEvent()
    {
        var inner = new Collector();
        var tracker = new ExtractOutcomeTracker(inner);
        tracker.Report(Status(ArchiveEntryStatus.Completed));
        tracker.Report(Status(ArchiveEntryStatus.Failed));

        Assert.Equal(2, inner.Events.Count);
    }

    private sealed class Collector : IProgress<ArchiveProgress>
    {
        public List<ArchiveProgress> Events { get; } = [];
        public void Report(ArchiveProgress value) => Events.Add(value);
    }
}
