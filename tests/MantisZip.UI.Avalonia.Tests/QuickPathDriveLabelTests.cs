using System;
using System.IO;
using System.Linq;
using MantisZip.UI.Avalonia.Controls;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 目录树盘符显示名的纯函数测试（不依赖 Avalonia 窗口）：
/// 有卷标显示「卷标 (X:)」，无卷标按驱动器类型兜底，且文案必须走本地化 key。
/// </summary>
public class QuickPathDriveLabelTests
{
    [Fact]
    public void FormatDriveName_WithVolumeLabel_ShowsLabelThenLetter()
    {
        var name = QuickPathControl.FormatDriveName("System", "C:", DriveType.Fixed);

        Assert.Equal("System (C:)", name);
    }

    [Fact]
    public void FormatDriveName_TrimsSurroundingWhitespaceInLabel()
    {
        var name = QuickPathControl.FormatDriveName("  数据盘  ", "D:", DriveType.Fixed);

        Assert.Equal("数据盘 (D:)", name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatDriveName_WithoutVolumeLabel_FallsBackByDriveType(string? volumeLabel)
    {
        var name = QuickPathControl.FormatDriveName(volumeLabel, "E:", DriveType.Removable);

        // 无头测试里 LocalizationManager 可能未初始化（T 直接回 key），所以只断言结构而不锁文案
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.EndsWith("(E:)", name);
    }

    [Theory]
    [InlineData("C:\\")]
    [InlineData("C:/")]
    public void GetDriveLetter_StripsTrailingSeparator(string rootPath)
    {
        Assert.Equal("C:", QuickPathControl.GetDriveLetter(rootPath));
    }

    [Fact]
    public void DriveTypeNameKey_ReturnsDistinctLocalizationKeys()
    {
        var types = new[]
        {
            DriveType.Fixed, DriveType.Removable, DriveType.Network,
            DriveType.CDRom, DriveType.Ram, DriveType.Unknown
        };

        var keys = types.Select(QuickPathControl.DriveTypeNameKey).ToArray();

        Assert.All(keys, key => Assert.StartsWith("QuickPath_Drive_", key));
        // 磁盘 / 可移动磁盘 / 网络驱动器 / 光驱 / RAM 盘 必须各有各的文案，不能共用一个兜底
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void GetVolumeLabelSafe_OnSystemDrive_DoesNotThrow()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root))
            return; // 系统盘根路径不可用的极端环境：该用例无意义，直接通过

        var drive = new DriveInfo(root!);
        var ex = Record.Exception(() => QuickPathControl.GetVolumeLabelSafe(drive));

        Assert.Null(ex);
    }
}
