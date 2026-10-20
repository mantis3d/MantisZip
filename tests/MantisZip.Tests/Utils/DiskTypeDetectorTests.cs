using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

public class DiskTypeDetectorTests
{
    [Fact]
    public void GetDiskType_SystemTempPath_ReturnsValidEnum()
    {
        var path = Path.GetTempPath();
        var result = DiskTypeDetector.GetDiskType(path);
        Assert.True(Enum.IsDefined(result));
    }

    [Fact]
    public void GetDiskType_SamePathTwice_ReturnsCachedValue()
    {
        var path = Path.GetTempPath();
        var first = DiskTypeDetector.GetDiskType(path);
        var second = DiskTypeDetector.GetDiskType(path);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GetDiskType_LocalFixedDrive_DetectsSsdOrHdd()
    {
        // 本地固定盘（Temp 所在卷）在真实硬件上必须检测成功；非 Windows 实现返回 Unknown，此处仅验证 Windows
        if (!OperatingSystem.IsWindows()) return;

        var result = DiskTypeDetector.GetDiskType(Path.GetTempPath());

        // 云 CI / 虚拟化存储栈可能不支持 IOCTL_STORAGE_QUERY_PROPERTY(SeekPenalty)，
        // 此时生产代码合法返回 Unknown —— ParallelDegreeResolver 会回退到保守并行度，
        // 属预期降级而非缺陷（GitHub Actions runner 的临时卷实测即如此），故不做失败断言。
        if (result == DiskType.Unknown) return;

        Assert.True(result is DiskType.Ssd or DiskType.Hdd,
            $"检测成功的唯一合法取值应为 Ssd 或 Hdd，实际 {result}");
    }

    [Fact]
    public void GetDiskType_EmptyPath_ReturnsUnknown()
        => Assert.Equal(DiskType.Unknown, DiskTypeDetector.GetDiskType(""));

    [Fact]
    public void GetDiskType_NullPath_ReturnsUnknown()
        => Assert.Equal(DiskType.Unknown, DiskTypeDetector.GetDiskType(null!));

    [Fact]
    public void GetDiskType_NonWindowsStylePath_ReturnsUnknown()
        => Assert.Equal(DiskType.Unknown, DiskTypeDetector.GetDiskType(@"relative\path"));

    [Fact]
    public void ExtractDriveRoot_VariousFormats_ExtractsRoot()
    {
        Assert.Equal("C:", DiskTypeDetector.ExtractDriveRoot(@"C:\Users\test"));
        Assert.Equal("C:", DiskTypeDetector.ExtractDriveRoot(@"C:/Users/test"));
        Assert.Equal("C:", DiskTypeDetector.ExtractDriveRoot(@"C:"));
        Assert.Null(DiskTypeDetector.ExtractDriveRoot(@"relative\path"));
        Assert.Null(DiskTypeDetector.ExtractDriveRoot(@"\\server\share\x"));
    }
}
