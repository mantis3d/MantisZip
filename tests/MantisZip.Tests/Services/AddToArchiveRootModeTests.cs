using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Services;

/// <summary>
/// 「追加到已有压缩包」路径的四态回归。
/// <para>
/// 新建压缩包走 <c>FileScanner</c> / 7z 的 <c>CompressFileDictionary</c>，早已按四态改写条目名；
/// 而 <c>AddToArchiveAsync</c> 过去只按 <c>路径.Combine(目录名, 相对路径)</c> 起名，完全不看
/// <see cref="ArchiveOptions.CompressRootMode"/> —— 用户在对话框选了「去根」、冲突策略又恰好是
/// 「追加」时，包内层级与预览树不一致。这里锁住「追加与新建同口径」。
/// </para>
/// </summary>
public sealed class AddToArchiveRootModeTests : IDisposable
{
    private readonly string _tempRoot;

    public AddToArchiveRootModeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MantisZipAddRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结论
        }
    }

    private string CreateSourceDir(string name = "7zip")
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
        var inner = Path.Combine(dir, "inner");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "b.txt"), "b");
        return dir;
    }

    /// <summary>建一个只含 <c>seed.txt</c> 的 ZIP，返回包路径；追加后它必须仍在。</summary>
    private string CreateSeedZip()
    {
        var archive = Path.Combine(_tempRoot, "seed.zip");
        using var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create);
        var entry = zip.CreateEntry("seed.txt");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("seed");
        return archive;
    }

    private static async Task<string[]> ListZipEntriesAsync(string archive)
    {
        var engine = new ZipEngine();
        var entries = await engine.ListEntriesAsync(archive, cancellationToken: CancellationToken.None);
        return entries.Where(e => !e.IsDirectory)
            .Select(e => e.FullPath.Replace('\\', '/'))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // ── ZIP ──

    [Fact]
    public async Task Zip_Add_Original_KeepsTopDirectory()
    {
        var archive = CreateSeedZip();
        var dir = CreateSourceDir();

        await new ZipEngine().AddToArchiveAsync(
            archive, [dir], new ArchiveOptions { CompressRootMode = RootDirectoryMode.Original });

        Assert.Equal(new[] { "7zip/a.txt", "7zip/inner/b.txt", "seed.txt" }, await ListZipEntriesAsync(archive));
    }

    [Fact]
    public async Task Zip_Add_SmartStrip_DropsTopDirectory()
    {
        var archive = CreateSeedZip();
        var dir = CreateSourceDir();

        await new ZipEngine().AddToArchiveAsync(
            archive, [dir], new ArchiveOptions { CompressRootMode = RootDirectoryMode.SmartStrip });

        Assert.Equal(new[] { "a.txt", "inner/b.txt", "seed.txt" }, await ListZipEntriesAsync(archive));
    }

    [Fact]
    public async Task Zip_Add_AlwaysWrap_PrependsRootName()
    {
        var archive = CreateSeedZip();
        var dir = CreateSourceDir();

        await new ZipEngine().AddToArchiveAsync(
            archive, [dir],
            new ArchiveOptions { CompressRootMode = RootDirectoryMode.AlwaysWrap, CompressWrapRoot = "wrap" });

        Assert.Equal(
            new[] { "seed.txt", "wrap/7zip/a.txt", "wrap/7zip/inner/b.txt" },
            await ListZipEntriesAsync(archive));
    }

    [Fact]
    public async Task Zip_Add_EntryBasePath_ComposesWithWrapRoot()
    {
        // 追加到「包内某个目录」时，浏览前缀在外、四态加根在内 —— 两段都要保留
        var archive = CreateSeedZip();
        var dir = CreateSourceDir();

        await new ZipEngine().AddToArchiveAsync(
            archive, [dir],
            new ArchiveOptions { CompressRootMode = RootDirectoryMode.AlwaysWrap, CompressWrapRoot = "wrap" },
            entryBasePath: "docs");

        Assert.Equal(
            new[] { "docs/wrap/7zip/a.txt", "docs/wrap/7zip/inner/b.txt", "seed.txt" },
            await ListZipEntriesAsync(archive));
    }

    // ── 7z ──

    [Fact]
    public async Task SevenZip_Add_SmartStrip_DropsTopDirectory()
    {
        var archive = Path.Combine(_tempRoot, "seed.7z");
        File.WriteAllText(Path.Combine(_tempRoot, "placeholder.txt"), "x");
        await new SevenZipEngine().CompressAsync(
            [Path.Combine(_tempRoot, "placeholder.txt")], archive, new ArchiveOptions());

        var dir = CreateSourceDir();

        await new SevenZipEngine().AddToArchiveAsync(
            archive, [dir], new ArchiveOptions { CompressRootMode = RootDirectoryMode.SmartStrip });

        var entries = await new SevenZipEngine().ListEntriesAsync(archive, cancellationToken: CancellationToken.None);
        var names = entries.Where(e => !e.IsDirectory)
            .Select(e => e.FullPath.Replace('\\', '/'))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Contains("a.txt", names);
        Assert.Contains("inner/b.txt", names);
        Assert.DoesNotContain("7zip/a.txt", names);
        Assert.Contains("placeholder.txt", names);
    }

    // ── 根名回落：追加时也必须用真实包名，而不是 "archive" ──

    [Fact]
    public async Task Zip_Add_WrapWithoutExplicitName_FallsBackToArchiveName()
    {
        var archive = CreateSeedZip();
        var dir = CreateSourceDir();

        await new ZipEngine().AddToArchiveAsync(
            archive, [dir], new ArchiveOptions { CompressRootMode = RootDirectoryMode.AlwaysWrap });

        var entries = await ListZipEntriesAsync(archive);
        Assert.Contains(entries, e => e.StartsWith("seed/", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.StartsWith("archive/", StringComparison.OrdinalIgnoreCase));
    }
}
