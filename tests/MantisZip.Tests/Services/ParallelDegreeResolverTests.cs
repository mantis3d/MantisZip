using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Services;

public class ParallelDegreeResolverTests
{
    private static ParallelExtractSettings DefaultSettings => new()
    {
        EnableParallel = true,
        SmartParallel = true,
        SmartSsdDegree = 12,
        SmartHddDegree = 8,
        LargeArchivePreTest = true,
        ManualDegree = Environment.ProcessorCount
    };

    // ── 步骤 1：调用方指定 ──

    [Fact]
    public void Resolve_CallerOverride_WinsOverEverything()
    {
        var s = DefaultSettings with { EnableParallel = false }; // 全局关也应被覆盖
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: 6, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(6, result);
    }

    [Fact]
    public void Resolve_CallerOverride_Zero_NotAnOverride()
    {
        var s = DefaultSettings;
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: 0, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(12, result); // 落到智能 SSD
    }

    // ── 步骤 2：全局关 → 串行 ──

    [Fact]
    public void Resolve_GlobalDisabled_Returns1()
    {
        var s = DefaultSettings with { EnableParallel = false };
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(1, result);
    }

    // ── 步骤 3：智能关 → 手动值 / ProcessorCount ──

    [Fact]
    public void Resolve_SmartOff_ManualDegree6_Returns6()
    {
        var s = DefaultSettings with { SmartParallel = false, ManualDegree = 6 };
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(6, result);
    }

    [Fact]
    public void Resolve_SmartOff_ManualDegree0_ReturnsProcessorCount()
    {
        var s = DefaultSettings with { SmartParallel = false, ManualDegree = 0 };
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, s, DiskType.Ssd, archiveSize: 0);
        // 代码会 clamp 到 16，>16 核机器上原始 ProcessorCount 会越界
        Assert.Equal(Math.Min(Environment.ProcessorCount, 16), result);
    }

    // ── 步骤 5（同步路径，无预测试触发时）：磁盘类型默认值 ──

    [Fact]
    public void Resolve_SmartOn_Ssd_ReturnsSmartSsdDegree()
    {
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, DefaultSettings, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(12, result);
    }

    [Fact]
    public void Resolve_SmartOn_Hdd_ReturnsSmartHddDegree()
    {
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, DefaultSettings, DiskType.Hdd, archiveSize: 0);
        Assert.Equal(8, result);
    }

    [Fact]
    public void Resolve_SmartOn_Unknown_ReturnsMinProcessorCount12()
    {
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, DefaultSettings, DiskType.Unknown, archiveSize: 0);
        Assert.Equal(Math.Min(Environment.ProcessorCount, 12), result);
    }

    // ── 越界 clamp ──

    [Fact]
    public void Resolve_SsdDegreeOutOfRange_ClampsTo16()
    {
        var s = DefaultSettings with { SmartSsdDegree = 99 };
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(16, result);
    }

    [Fact]
    public void Resolve_CallerOverrideOutOfRange_ClampsTo16()
    {
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: 99, DefaultSettings, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(16, result);
    }

    [Fact]
    public void Resolve_CallerOverrideNegative_NotAnOverride_FallsToGlobalOff()
    {
        // 负数不视为 override（<=0 落入决策链），走步骤 2 全局关
        var s = DefaultSettings with { EnableParallel = false };
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: -5, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(1, result); // 全局关
    }

    // ── ResolveAsync 短路路径（步骤 1-3 不触碰 I/O，null 路径安全） ──

    [Fact]
    public async Task ResolveAsync_CallerOverride_ReturnsClampedWithoutIo()
    {
        var result = await ParallelDegreeResolver.ResolveAsync(
            callerDegree: 6, DefaultSettings,
            archivePath: null!, destinationPath: null!,
            password: null, engineSupportsParallel: true,
            progress: null, CancellationToken.None);
        Assert.Equal(6, result);
    }

    [Fact]
    public async Task ResolveAsync_GlobalDisabled_Returns1WithoutIo()
    {
        var s = DefaultSettings with { EnableParallel = false };
        var result = await ParallelDegreeResolver.ResolveAsync(
            callerDegree: null, s,
            archivePath: null!, destinationPath: null!,
            password: null, engineSupportsParallel: true,
            progress: null, CancellationToken.None);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task ResolveAsync_SmartOffManual_ReturnsManualWithoutIo()
    {
        var s = DefaultSettings with { SmartParallel = false, ManualDegree = 5 };
        var result = await ParallelDegreeResolver.ResolveAsync(
            callerDegree: null, s,
            archivePath: null!, destinationPath: null!,
            password: null, engineSupportsParallel: true,
            progress: null, CancellationToken.None);
        Assert.Equal(5, result);
    }
}
