using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Services;

/// <summary>
/// 「包内根目录」四态的端到端回归：真正压出一个 ZIP，再读回**包里真实的条目名**。
/// <para>
/// 单元层（<c>FileScannerRootModeTests</c>）只证明相对路径算对了；这里锁的是
/// 「<see cref="CompressRequest.RootMode"/> 一路透传到引擎、落到中央目录」这条链，
/// 漏一环就会表现为「设置里选了去根，包里还是老样子」。
/// </para>
/// </summary>
public sealed class CompressRootModeTests : IDisposable
{
    private readonly string _tempRoot;

    public CompressRootModeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MantisZipCompressRoot_" + Guid.NewGuid().ToString("N"));
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

    // ── 夹具 ──

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

    private string CreateLooseFile(string name = "solo.txt")
    {
        var file = Path.Combine(_tempRoot, name);
        File.WriteAllText(file, "s");
        return file;
    }

    /// <summary>压缩并返回包内条目名（排序后，'/' 分隔）。</summary>
    private async Task<string[]> CompressAndList(
        IReadOnlyList<string> sources, RootDirectoryMode mode, string? wrapRoot = null, string format = "zip")
    {
        var outputPath = Path.Combine(_tempRoot, "out_" + Guid.NewGuid().ToString("N") + "." + format);
        var request = new CompressRequest
        {
            SourcePaths = new List<string>(sources),
            Mode = CompressOutputMode.Manual,
            Format = format,
            OutputPath = outputPath,
            RootMode = mode,
            WrapRoot = wrapRoot,
        };

        var result = await CompressService.CompressAsync(request, null, new Progress<ArchiveProgress>(), CancellationToken.None);
        Assert.Equal(1, result.Succeeded);

        var entries = new List<string>();
        using var zf = new ZipFile(outputPath);
        foreach (ZipEntry entry in zf)
        {
            if (!entry.IsDirectory)
                entries.Add(entry.Name.Replace('\\', '/'));
        }
        return entries.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // ── ① 原样 ──

    [Fact]
    public async Task Original_SingleDirectory_KeepsTopDirectoryInArchive()
    {
        var dir = CreateSourceDir();
        Assert.Equal(new[] { "7zip/a.txt", "7zip/inner/b.txt" }, await CompressAndList([dir], RootDirectoryMode.Original));
    }

    // ── ② 智能去除唯一根 ──

    [Fact]
    public async Task SmartStrip_SingleDirectory_DropsTopDirectoryInArchive()
    {
        var dir = CreateSourceDir();
        Assert.Equal(new[] { "a.txt", "inner/b.txt" }, await CompressAndList([dir], RootDirectoryMode.SmartStrip));
    }

    [Fact]
    public async Task SmartStrip_TwoDirectories_KeepsBothTopDirectories()
    {
        var a = CreateSourceDir("one");
        var b = CreateSourceDir("two");
        Assert.Equal(
            new[] { "one/a.txt", "one/inner/b.txt", "two/a.txt", "two/inner/b.txt" },
            await CompressAndList([a, b], RootDirectoryMode.SmartStrip));
    }

    // ── ③ 智能添加唯一根 ──

    [Fact]
    public async Task SmartWrap_SingleDirectory_DoesNotAddASecondLayer()
    {
        var dir = CreateSourceDir();
        Assert.Equal(new[] { "7zip/a.txt", "7zip/inner/b.txt" }, await CompressAndList([dir], RootDirectoryMode.SmartWrap, "wrap"));
    }

    [Fact]
    public async Task SmartWrap_TwoDirectories_AddsSharedRoot()
    {
        var a = CreateSourceDir("one");
        var b = CreateSourceDir("two");
        Assert.Equal(
            new[] { "wrap/one/a.txt", "wrap/one/inner/b.txt", "wrap/two/a.txt", "wrap/two/inner/b.txt" },
            await CompressAndList([a, b], RootDirectoryMode.SmartWrap, "wrap"));
    }

    [Fact]
    public async Task SmartWrap_SingleFile_AddsRoot()
    {
        var file = CreateLooseFile();
        Assert.Equal(new[] { "wrap/solo.txt" }, await CompressAndList([file], RootDirectoryMode.SmartWrap, "wrap"));
    }

    // ── ④ 始终添加唯一根 ──

    [Fact]
    public async Task AlwaysWrap_SingleDirectory_ProducesTwoLayers()
    {
        var dir = CreateSourceDir();
        Assert.Equal(new[] { "wrap/7zip/a.txt", "wrap/7zip/inner/b.txt" }, await CompressAndList([dir], RootDirectoryMode.AlwaysWrap, "wrap"));
    }

    // ── 根名缺省：引擎侧必须用真实输出包名，而不是回落到 "archive" ──

    [Fact]
    public async Task Wrap_WithoutExplicitName_FallsBackToOutputArchiveName()
    {
        var a = CreateSourceDir("one");
        var b = CreateSourceDir("two");
        var entries = await CompressAndList([a, b], RootDirectoryMode.SmartWrap, wrapRoot: null);
        Assert.All(entries, e => Assert.StartsWith("out_", e, StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.StartsWith("archive/", StringComparison.OrdinalIgnoreCase));
    }

    // ── tar.gz 走另一条引擎分支（TarGzEngine）──

    [Fact]
    public async Task SmartStrip_TarGz_SingleDirectory_DropsTopDirectory()
    {
        var dir = CreateSourceDir();
        var outputPath = Path.Combine(_tempRoot, "out_" + Guid.NewGuid().ToString("N") + ".tar.gz");
        var request = new CompressRequest
        {
            SourcePaths = new List<string> { dir },
            Mode = CompressOutputMode.Manual,
            Format = "tar.gz",
            OutputPath = outputPath,
            RootMode = RootDirectoryMode.SmartStrip,
        };

        var result = await CompressService.CompressAsync(request, null, new Progress<ArchiveProgress>(), CancellationToken.None);
        Assert.Equal(1, result.Succeeded);
        Assert.True(File.Exists(outputPath));

        var engine = new TarGzEngine();
        var entries = await engine.ListEntriesAsync(outputPath, cancellationToken: CancellationToken.None);
        var names = entries.Where(e => !e.IsDirectory).Select(e => e.FullPath.Replace('\\', '/')).ToArray();

        Assert.Contains("a.txt", names);
        Assert.Contains("inner/b.txt", names);
        Assert.DoesNotContain("7zip/a.txt", names);
    }
}
