using System;
using System.IO;
using System.Linq;
using MantisZip.Core.Services;
using Xunit;

namespace MantisZip.Tests.Services;

/// <summary>
/// 压缩侧「列表」播种枚举器：Key 必须与引擎上报的 ArchiveProgress.EntryKey（相对路径）同源，
/// 否则播种行永远等不到终态 upsert。这里用真实临时目录校验 Key/Name/Size 与白名单过滤。
/// </summary>
public sealed class SourceEntryEnumeratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mz_seed_" + Guid.NewGuid().ToString("N"));

    public SourceEntryEnumeratorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Enumerate_DirectorySource_UsesDirNamePrefixAndLeafName()
    {
        var dir = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllBytes(Path.Combine(dir, "a.txt"), new byte[10]);
        File.WriteAllBytes(Path.Combine(dir, "sub", "b.bin"), new byte[20]);

        var items = SourceEntryEnumerator.Enumerate(new[] { dir });

        Assert.Equal(2, items.Count);
        var a = items.Single(i => i.Name == "a.txt");
        Assert.Equal(Path.Combine("data", "a.txt"), a.Key);
        Assert.Equal(10, a.Size);
        var b = items.Single(i => i.Name == "b.bin");
        Assert.Equal(Path.Combine("data", "sub", "b.bin"), b.Key);
        Assert.Equal(20, b.Size);
    }

    [Fact]
    public void Enumerate_FileSource_UsesLeafNameAsKey()
    {
        var f = Path.Combine(_root, "solo.dat");
        File.WriteAllBytes(f, new byte[7]);

        var items = SourceEntryEnumerator.Enumerate(new[] { f });

        var only = Assert.Single(items);
        Assert.Equal("solo.dat", only.Key);
        Assert.Equal("solo.dat", only.Name);
        Assert.Equal(7, only.Size);
    }

    [Fact]
    public void Enumerate_Whitelist_FiltersFiles()
    {
        var dir = Path.Combine(_root, "wl");
        Directory.CreateDirectory(dir);
        var keep = Path.Combine(dir, "keep.txt");
        var drop = Path.Combine(dir, "drop.txt");
        File.WriteAllBytes(keep, new byte[3]);
        File.WriteAllBytes(drop, new byte[4]);

        var items = SourceEntryEnumerator.Enumerate(new[] { dir }, new[] { keep });

        var only = Assert.Single(items);
        Assert.Equal("keep.txt", only.Name);
        Assert.Equal(3, only.Size);
    }
}
