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
