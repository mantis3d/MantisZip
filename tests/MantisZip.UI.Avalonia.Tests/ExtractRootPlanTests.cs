using System.Collections.Generic;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 解压端「包内根目录」四态的判定回归。
/// <para>
/// 锁的是「窗里能选的那一档，跑起来究竟剥不剥 / 加不加」：<see cref="ExtractRootPlan.Resolve"/>
/// 是预览树与实解共用的唯一判定入口，一旦它判错，两处会一起错（预览看不出来）。
/// </para>
/// </summary>
public class ExtractRootPlanTests
{
    private const string ArchivePath = @"D:\downloads\photos.zip";

    private static ArchiveItem File(string fullPath) => new()
    {
        Name = fullPath,
        FullPath = fullPath,
        DisplayName = fullPath,
        IsDirectory = false,
        Size = 1,
    };

    private static ArchiveItem Dir(string fullPath) => new()
    {
        Name = fullPath,
        FullPath = fullPath,
        DisplayName = fullPath,
        IsDirectory = true,
    };

    /// <summary>有唯一根的包：photos/a.jpg + photos/b.jpg。</summary>
    private static List<ArchiveItem> SingleRootEntries() =>
    [
        Dir("photos"),
        File("photos/a.jpg"),
        File("photos/b.jpg"),
    ];

    /// <summary>散装包：a.jpg + b.jpg 直接躺在根下。</summary>
    private static List<ArchiveItem> LooseEntries() =>
    [
        File("a.jpg"),
        File("b.jpg"),
    ];

    /// <summary>多根包：one/a.txt + two/b.txt。</summary>
    private static List<ArchiveItem> MultiRootEntries() =>
    [
        File("one/a.txt"),
        File("two/b.txt"),
    ];

    // ── ① 原样 ──

    [Fact]
    public void Original_WithSingleRoot_ReturnsNothing()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.Original, SingleRootEntries(), ArchivePath);
        Assert.Null(strip);
        Assert.Null(add);
    }

    [Fact]
    public void Original_WithLooseEntries_ReturnsNothing()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.Original, LooseEntries(), ArchivePath);
        Assert.Null(strip);
        Assert.Null(add);
    }

    [Fact]
    public void Original_WithNullEntries_ReturnsNothing()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.Original, null, ArchivePath);
        Assert.Null(strip);
        Assert.Null(add);
    }

    // ── ② 智能去除唯一根 ──

    [Fact]
    public void SmartStrip_WithSingleRoot_StripsThatRoot()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.SmartStrip, SingleRootEntries(), ArchivePath);
        Assert.Equal("photos", strip);
        Assert.Null(add);
    }

    [Fact]
    public void SmartStrip_WithLooseEntries_DoesNothing()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.SmartStrip, LooseEntries(), ArchivePath);
        Assert.Null(strip);
        Assert.Null(add);
    }

    [Fact]
    public void SmartStrip_WithMultiRoot_DoesNothing()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.SmartStrip, MultiRootEntries(), ArchivePath);
        Assert.Null(strip);
        Assert.Null(add);
    }

    [Fact]
    public void SmartStrip_WithOnlyDirectoryEntries_DoesNothingRatherThanHalfApply()
    {
        // 只有目录条目时 HasSingleRootDirectory 为 true，但取不到可剥的文件根 ⇒ 宁可整体不动
        var entries = new List<ArchiveItem> { Dir("photos") };
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.SmartStrip, entries, ArchivePath);
        Assert.Null(strip);
        Assert.Null(add);
    }

    [Fact]
    public void SmartStrip_WithEmptyEntries_DoesNothing()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.SmartStrip, new List<ArchiveItem>(), ArchivePath);
        Assert.Null(strip);
        Assert.Null(add);
    }

    // ── ③ 智能添加唯一根 ──

    [Fact]
    public void SmartWrap_WithSingleRoot_DoesNotAddASecondLayer()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.SmartWrap, SingleRootEntries(), ArchivePath);
        Assert.Null(strip);
        Assert.Null(add);
    }

    [Fact]
    public void SmartWrap_WithLooseEntries_AddsArchiveNameRoot()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.SmartWrap, LooseEntries(), ArchivePath);
        Assert.Null(strip);
        Assert.Equal("photos", add);
    }

    [Fact]
    public void SmartWrap_WithMultiRoot_AddsArchiveNameRoot()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.SmartWrap, MultiRootEntries(), ArchivePath);
        Assert.Null(strip);
        Assert.Equal("photos", add);
    }

    // ── ④ 始终添加唯一根 ──

    [Fact]
    public void AlwaysWrap_WithSingleRoot_AddsAnotherLayer()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.AlwaysWrap, SingleRootEntries(), ArchivePath);
        Assert.Null(strip);
        Assert.Equal("photos", add);
    }

    [Fact]
    public void AlwaysWrap_WithLooseEntries_AddsRoot()
    {
        var (strip, add) = ExtractRootPlan.Resolve(RootDirectoryMode.AlwaysWrap, LooseEntries(), ArchivePath);
        Assert.Null(strip);
        Assert.Equal("photos", add);
    }

    // ── 根名来源：包主名（去扩展名），非法字符净化 ──

    [Fact]
    public void WrapRootName_ComesFromArchiveBaseName()
    {
        var (_, add) = ExtractRootPlan.Resolve(
            RootDirectoryMode.AlwaysWrap, LooseEntries(), @"D:\downloads\my backup.tar.gz");
        Assert.Equal("my backup", add);
    }

    [Fact]
    public void WrapRootName_IllegalCharactersAreSanitized()
    {
        var (_, add) = ExtractRootPlan.Resolve(
            RootDirectoryMode.AlwaysWrap, LooseEntries(), @"C:\tmp\a:b*c.zip");
        Assert.Equal("a_b_c", add);
    }

    // ── 剥与加永不同时出现 ──

    [Theory]
    [InlineData(RootDirectoryMode.Original)]
    [InlineData(RootDirectoryMode.SmartStrip)]
    [InlineData(RootDirectoryMode.SmartWrap)]
    [InlineData(RootDirectoryMode.AlwaysWrap)]
    public void Resolve_NeverStripsAndWrapsTogether(RootDirectoryMode mode)
    {
        foreach (var entries in new[] { SingleRootEntries(), LooseEntries(), MultiRootEntries() })
        {
            var (strip, add) = ExtractRootPlan.Resolve(mode, entries, ArchivePath);
            Assert.False(strip != null && add != null, $"{mode} 同时剥根({strip})与加根({add})");
        }
    }
}
