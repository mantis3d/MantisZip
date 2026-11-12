using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// 「包内根目录」四态在路径换算层的回归测试（<see cref="ExtractPathResolver"/> 的剥根 / 加根）。
/// <para>
/// 预览树与实际解压共用这一层，所以这里锁的是「双方都会看到同一层级」这件事；
/// 叠序（先剥/加根，再裁浏览前缀）也是本套用例的一部分。
/// </para>
/// </summary>
public class ExtractPathResolverRootModeTests
{
    private static ArchiveItem Item(string fullPath, bool isDir = false) => new()
    {
        FullPath = fullPath,
        Name = fullPath[(fullPath.LastIndexOf('/') + 1)..],
        IsDirectory = isDir
    };

    // ── 剥根 ──

    [Fact]
    public void ResolveRelativePath_StripRoot_DropsThatLayer()
    {
        Assert.Equal("a.txt", ExtractPathResolver.ResolveRelativePath(
            "7zip/a.txt", "", preserveFullPath: true, stripRoot: "7zip"));
    }

    [Fact]
    public void ResolveRelativePath_StripRoot_KeepsDeeperStructure()
    {
        Assert.Equal("inner/b.txt", ExtractPathResolver.ResolveRelativePath(
            "7zip/inner/b.txt", "", preserveFullPath: true, stripRoot: "7zip"));
    }

    [Fact]
    public void ResolveRelativePath_StripRoot_RootEntryItselfDisappears()
    {
        // 根目录条目（"7zip/"）剥完什么都不剩 ⇒ null，调用方跳过它
        Assert.Null(ExtractPathResolver.ResolveRelativePath(
            "7zip/", "", preserveFullPath: true, stripRoot: "7zip"));
    }

    [Fact]
    public void ResolveRelativePath_StripRoot_OtherRootUntouched()
    {
        // 不是唯一根的另一层不该被误剥
        Assert.Equal("other/a.txt", ExtractPathResolver.ResolveRelativePath(
            "other/a.txt", "", preserveFullPath: true, stripRoot: "7zip"));
    }

    // ── 加根 ──

    [Fact]
    public void ResolveRelativePath_AddRoot_PrependsOneLayer()
    {
        Assert.Equal("pkg/a.txt", ExtractPathResolver.ResolveRelativePath(
            "a.txt", "", preserveFullPath: true, addRoot: "pkg"));
    }

    [Fact]
    public void ResolveRelativePath_AddRootAndStripRoot_ApplyInOneShot()
    {
        // 理论上 ②③ 互斥，但换算层两个参数都给了也要按「先剥再加」得出确定结果
        Assert.Equal("pkg/a.txt", ExtractPathResolver.ResolveRelativePath(
            "7zip/a.txt", "", preserveFullPath: true, stripRoot: "7zip", addRoot: "pkg"));
    }

    // ── 叠序：剥/加根必须在裁浏览前缀之前 ──

    [Fact]
    public void ResolveRelativePath_StripRootBeforeTrim_TrimsAfterStripping()
    {
        // 剥掉 7zip 后剩下 inner/b.txt，浏览前缀 inner 仍能裁到 b.txt
        Assert.Equal("b.txt", ExtractPathResolver.ResolveRelativePath(
            "7zip/inner/b.txt", "inner", preserveFullPath: false, stripRoot: "7zip"));
    }

    [Fact]
    public void ResolveRelativePath_TrimBeforeStripWouldFail_Regression()
    {
        // 反序会先按 7zip 裁前缀（currentFolder="7zip" → "a.txt"），再剥根就找不到 7zip 了；
        // 正确叠序下同样是 a.txt，此用例锁住「剥根后仍按剥后的路径裁前缀」。
        Assert.Equal("a.txt", ExtractPathResolver.ResolveRelativePath(
            "7zip/a.txt", "7zip", preserveFullPath: false, stripRoot: "7zip"));
    }

    [Fact]
    public void ResolveRelativePath_AddRootBeforeTrim_TrimSeesWrappedPath()
    {
        // 加根后是 pkg/a.txt，浏览前缀 pkg 能裁掉它（说明加根先于裁剪）
        Assert.Equal("a.txt", ExtractPathResolver.ResolveRelativePath(
            "a.txt", "pkg", preserveFullPath: false, addRoot: "pkg"));
    }

    // ── ResolveAll 批量 ──

    [Fact]
    public void ResolveAll_StripRoot_DropsRootEntryAndRewritesFiles()
    {
        var entries = new[]
        {
            Item("7zip", isDir: true),
            Item("7zip/a.txt"),
            Item("7zip/inner/b.txt")
        };

        var result = ExtractPathResolver.ResolveAll(entries, "", preserveFullPath: true, stripRoot: "7zip");

        // 根目录条目本身被剥空 ⇒ 不进字典
        Assert.False(result.ContainsKey("7zip"));
        Assert.Equal("a.txt", result["7zip/a.txt"]);
        Assert.Equal("inner/b.txt", result["7zip/inner/b.txt"]);
    }

    [Fact]
    public void ResolveAll_AddRoot_PrependsToEveryEntry()
    {
        var entries = new[] { Item("a.txt"), Item("inner/b.txt") };
        var result = ExtractPathResolver.ResolveAll(entries, "", preserveFullPath: true, addRoot: "pkg");
        Assert.Equal("pkg/a.txt", result["a.txt"]);
        Assert.Equal("pkg/inner/b.txt", result["inner/b.txt"]);
    }

    [Fact]
    public void ResolveAll_OriginalModeByDefault_Unchanged()
    {
        // 不传 stripRoot/addRoot = 原样模式，逐条与引入本功能前一致
        var entries = new[] { Item("docs/a.txt") };
        var result = ExtractPathResolver.ResolveAll(entries, "", preserveFullPath: true);
        Assert.Equal("docs/a.txt", result["docs/a.txt"]);
    }

    // ── 与预览/实解共用的 EntryOutputPath 保持一致 ──

    [Fact]
    public void EntryOutputPath_MirrorsResolverSemantics()
    {
        var options = new ArchiveOptions { StripRootDirectory = "7zip" };
        Assert.Equal("a.txt", EntryOutputPath.ResolveRelative("7zip/a.txt", options));
        Assert.Null(EntryOutputPath.ResolveRelative("7zip/", options));
    }

    [Fact]
    public void EntryOutputPath_AddRoot_AppliesToRelativePath()
    {
        var options = new ArchiveOptions { AddRootDirectory = "pkg" };
        Assert.Equal("pkg/a.txt", EntryOutputPath.ResolveRelative("a.txt", options));
    }
}
