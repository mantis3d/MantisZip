using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// PathCommonality —— 公共父目录 / 盘符根判定。
/// 这套算法原先只在 UI 层（App.FindCommonParent），下沉到 Core 后由
/// CompressPathPlanner.ResolveAutoName 与 FileNameTemplate 共用。
/// </summary>
public class PathCommonalityTests
{
    [Fact]
    public void FindCommonParent_EmptyList_ReturnsNull()
        => Assert.Null(PathCommonality.FindCommonParent([]));

    [Fact]
    public void FindCommonParent_SingleFile_ReturnsItsDirectory()
        => Assert.Equal(@"D:\a", PathCommonality.FindCommonParent([@"D:\a\x.txt"]));

    [Fact]
    public void FindCommonParent_TwoFilesSameDirectory_ReturnsThatDirectory()
        => Assert.Equal(@"D:\a", PathCommonality.FindCommonParent([@"D:\a\x.txt", @"D:\a\y.txt"]));

    [Fact]
    public void FindCommonParent_SiblingDirectories_ReturnsSharedParent()
        => Assert.Equal(@"D:\a", PathCommonality.FindCommonParent(
            [@"D:\a\sub\x.txt", @"D:\a\other\y.txt"]));

    [Fact]
    public void FindCommonParent_DeeplyNested_ReturnsCommonAncestor()
        => Assert.Equal(@"D:\a\b", PathCommonality.FindCommonParent(
            [@"D:\a\b\c\d\x.txt", @"D:\a\b\e\y.txt"]));

    [Fact]
    public void FindCommonParent_DirectorySourceWithTrailingSeparator_TrimsBeforeComparing()
        => Assert.Equal(@"D:\a", PathCommonality.FindCommonParent(
            [@"D:\a\b\", @"D:\a\c.txt"]));

    [Fact]
    public void FindCommonParent_DriveRootCommon_ReturnsDriveRoot()
        => Assert.Equal(@"D:\", PathCommonality.FindCommonParent([@"D:\x.txt", @"D:\y.txt"]));

    [Theory]
    [InlineData(@"C:\")]
    [InlineData("C:")]
    [InlineData("c:\\")]
    [InlineData(@"D:/")]
    [InlineData("d:")]
    public void IsDriveRoot_DriveRoots_ReturnTrue(string path)
        => Assert.True(PathCommonality.IsDriveRoot(path));

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("C")]
    [InlineData("CD:")]
    public void IsDriveRoot_NonRoots_ReturnFalse(string? path)
        => Assert.False(PathCommonality.IsDriveRoot(path));
}
