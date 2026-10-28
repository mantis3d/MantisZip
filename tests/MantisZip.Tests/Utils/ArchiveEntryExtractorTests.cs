using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using MantisZip.Tests.Fixtures;
using SharpSevenZip;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// <see cref="ArchiveEntryExtractor"/> 头部提取（head）的回归测试，覆盖 T1「真流式」改造：
/// <list type="bullet">
/// <item>ZIP：<c>entry.OpenEntryStream()</c> 读满 maxBytes 即停，不再把整条目解压进内存；</item>
/// <item>7z：<see cref="TruncatingWriteStream"/> 截断写入，底层解压照常跑完但只保留 maxBytes；</item>
/// <item>TarGz：仍走落盘慢路径，但 head 内容必须与完整解压逐字节一致。</item>
/// </list>
///
/// <para>
/// 放在 <c>DisableParallelization = true</c> 的集合里：内存断言读取的是**进程级**
/// <c>GC.GetTotalAllocatedBytes</c>，必须避免其它测试集合的分配混入统计窗口。
/// </para>
/// </summary>
[Collection("ArchiveHeadMemory")]
public sealed class ArchiveEntryExtractorTests : IClassFixture<ArchiveEntryExtractorFixture>
{
    private readonly ArchiveEntryExtractorFixture _fx;

    public ArchiveEntryExtractorTests(ArchiveEntryExtractorFixture fx) => _fx = fx;

    // ===== ZIP：读满即停 =====

    [Fact]
    public async Task ExtractHeadAsync_Zip_StopsAtMaxBytes_AndReturnsExactPrefix()
    {
        const int maxBytes = 65536;

        var head = await ArchiveEntryExtractor.ExtractHeadAsync(
            _fx.ZipPath, ArchiveEntryExtractorFixture.ZipBigEntryName, maxBytes, ArchiveFormat.Zip);

        Assert.Equal(maxBytes, head.Length);
        Assert.Equal(ArchiveEntryExtractorFixture.MakeCompressible(maxBytes), head);
    }

    [Fact]
    public async Task ExtractHeadAsync_Zip_LargerThanEntry_ReturnsWholeEntry()
    {
        var head = await ArchiveEntryExtractor.ExtractHeadAsync(
            _fx.ZipPath, "hello.txt", 4096, ArchiveFormat.Zip);

        Assert.Equal(Encoding.UTF8.GetBytes(ArchiveFixtures.HelloText), head);
    }

    [Fact]
    public async Task ExtractHeadAsync_Zip_ZeroMaxBytes_ReturnsEmpty()
    {
        var head = await ArchiveEntryExtractor.ExtractHeadAsync(
            _fx.ZipPath, ArchiveEntryExtractorFixture.ZipBigEntryName, 0, ArchiveFormat.Zip);

        Assert.Empty(head);
    }

    [Fact]
    public async Task ExtractHeadAsync_Zip_EncryptedEntry_ReturnsDecryptedPrefix()
    {
        var head = await ArchiveEntryExtractor.ExtractHeadAsync(
            _fx.EncryptedZipPath, "secret.txt", 4096, ArchiveFormat.Zip, password: "test123");

        Assert.Equal("secret data", Encoding.UTF8.GetString(head));
    }

    [Fact]
    public async Task ExtractHeadTailAsync_Zip_HeadAndTailBothCorrect()
    {
        var (head, tail) = await ArchiveEntryExtractor.ExtractHeadTailAsync(
            _fx.ZipPath, "hello.txt", headBytes: 5, tailBytes: 4, ArchiveFormat.Zip);

        Assert.Equal("Hello", Encoding.UTF8.GetString(head));
        Assert.NotNull(tail);
        Assert.Equal("rld!", Encoding.UTF8.GetString(tail!));
    }

    /// <summary>
    /// 回归：head 内容必须与 <see cref="ArchiveEntryExtractor.ExtractEntryAsync"/> 写出的完整文件前 N 字节逐字节一致。
    /// </summary>
    [Fact]
    public async Task ExtractHeadAsync_Zip_MatchesFullExtractPrefix()
    {
        const int maxBytes = 4096;

        var head = await ArchiveEntryExtractor.ExtractHeadAsync(
            _fx.ZipPath, ArchiveEntryExtractorFixture.ZipBigEntryName, maxBytes, ArchiveFormat.Zip);

        var fullPath = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_full.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            await ArchiveEntryExtractor.ExtractEntryAsync(
                _fx.ZipPath, ArchiveEntryExtractorFixture.ZipBigEntryName, fullPath, ArchiveFormat.Zip);

            var expected = new byte[maxBytes];
            using (var fs = File.OpenRead(fullPath))
                fs.ReadExactly(expected);
            Assert.Equal(expected, head);
        }
        finally
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
    }

    /// <summary>
    /// 内存断言：对 32MB 条目取 64KB head，托管分配必须远小于条目大小。
    /// 旧实现（<c>entry.WriteTo(MemoryStream)</c> 后截断）会分配 ≥ 32MB；流式只有 head + 解压缓冲区量级。
    /// </summary>
    [Fact]
    public async Task ExtractHeadAsync_Zip_DoesNotAllocateWholeEntry()
    {
        const int maxBytes = 64 * 1024;
        const int entrySize = ArchiveEntryExtractorFixture.BigEntrySize;

        // 先清干净，再取基线；窗口内不能有别的分配（本集合已禁用并行）
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetTotalAllocatedBytes(precise: true);
        var head = await ArchiveEntryExtractor.ExtractHeadAsync(
            _fx.ZipPath, ArchiveEntryExtractorFixture.ZipBigEntryName, maxBytes, ArchiveFormat.Zip);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.Equal(maxBytes, head.Length);

        // 阈值取条目大小的 1/4（8MB）：实测流式分配在 1MB 以内，
        // 留足余量以容忍 SharpCompress 首次初始化 / JIT 的一次性分配；
        // 旧实现的 32MB 仍会被稳稳判失败（区分度 > 4 倍）。
        Assert.True(
            allocated < entrySize / 4,
            $"head 提取分配了 {allocated:N0} 字节，超过条目大小 {entrySize:N0} 的 1/4（{entrySize / 4:N0}），疑似仍在整条目解压");
    }

    // ===== 7z：截断写入 =====

    [Fact]
    public async Task ExtractHeadAsync_SevenZip_TruncatesAtMaxBytes_AndMatchesFullExtract()
    {
        if (_fx.SevenZipPath == null)
            return; // 本机没有 7z.dll：跳过（与 SevenZipEngineTests 的既有写法一致）

        // 夹具显式关闭固实压缩；若这里变成固实，说明走的是落盘慢路径，本用例就失去意义
        using (var extractor = new SharpSevenZipExtractor(_fx.SevenZipPath))
            Assert.False(extractor.IsSolid, "本用例要求非固实 7z，否则 ExtractHeadAsync 会走落盘慢路径");

        const int maxBytes = 4096;
        var head = await ArchiveEntryExtractor.ExtractHeadAsync(
            _fx.SevenZipPath, ArchiveEntryExtractorFixture.SevenZipBigEntryName, maxBytes, ArchiveFormat.SevenZip);

        Assert.Equal(maxBytes, head.Length);
        Assert.Equal(ArchiveEntryExtractorFixture.MakeCompressible(maxBytes), head);

        // 回归：与完整解压结果的前 N 字节一致
        var fullPath = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_full7z.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            await ArchiveEntryExtractor.ExtractEntryAsync(
                _fx.SevenZipPath, ArchiveEntryExtractorFixture.SevenZipBigEntryName, fullPath, ArchiveFormat.SevenZip);

            var expected = new byte[maxBytes];
            using (var fs = File.OpenRead(fullPath))
                fs.ReadExactly(expected);
            Assert.Equal(expected, head);
        }
        finally
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
    }

    /// <summary>
    /// 直接验证「SharpSevenZip 可以把条目写进不可 seek 的只写流」这一前提——
    /// 这是 7z 分支改成 <see cref="TruncatingWriteStream"/> 的关键假设，
    /// 与固实/非固实无关，单独兜住。
    /// </summary>
    [Fact]
    public void SevenZipExtractor_ExtractFile_IntoTruncatingWriteStream_KeepsOnlyCapacity()
    {
        if (_fx.SevenZipPath == null)
            return; // 本机没有 7z.dll：跳过

        using var extractor = new SharpSevenZipExtractor(_fx.SevenZipPath);
        var entry = extractor.ArchiveFileData.First(e =>
            !e.IsDirectory && e.FileName.EndsWith(ArchiveEntryExtractorFixture.SevenZipBigEntryName, StringComparison.Ordinal));

        using var sink = new TruncatingWriteStream(4096);
        extractor.ExtractFile(entry.Index, sink);

        Assert.Equal(4096, sink.WrittenBytes);
        Assert.True(sink.DiscardedBytes > 0, "条目比 capacity 大，应当有被丢弃的字节");
        Assert.Equal(ArchiveEntryExtractorFixture.MakeCompressible(4096), sink.ToArray());
    }

    // ===== TarGz：落盘慢路径（内容回归） =====

    [Fact]
    public async Task ExtractHeadAsync_TarGz_FallsBackToFullExtract_AndMatches()
    {
        var head = await ArchiveEntryExtractor.ExtractHeadAsync(
            _fx.TarGzPath, "hello.txt", 4096, ArchiveFormat.Tar);

        Assert.Equal(Encoding.UTF8.GetBytes(ArchiveFixtures.HelloText), head);

        var fullPath = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_full.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            await ArchiveEntryExtractor.ExtractEntryAsync(
                _fx.TarGzPath, "hello.txt", fullPath, ArchiveFormat.Tar);

            var expected = File.ReadAllBytes(fullPath);
            Assert.Equal(expected, head);
        }
        finally
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
    }
}

/// <summary>
/// 测试夹具：一次性生成 32MB 可压缩 ZIP 条目、1MB 非固实 7z、tar.gz 与加密 ZIP，
/// 避免每个用例重复压缩大文件。所有临时文件在 <see cref="Dispose"/> 里清理。
/// </summary>
public sealed class ArchiveEntryExtractorFixture : IDisposable
{
    /// <summary>ZIP 大条目解压后大小（32MB 可压缩数据，压缩后只有几十 KB）。</summary>
    public const int BigEntrySize = 32 * 1024 * 1024;

    /// <summary>7z 大条目解压后大小（1MB，足以触发截断）。</summary>
    public const int SevenZipEntrySize = 1024 * 1024;

    public const string ZipBigEntryName = "big.bin";
    public const string SevenZipBigEntryName = "big7z.bin";

    private static readonly byte[] Pattern = Encoding.UTF8.GetBytes(
        "MantisZip head-stream probe data 0123456789 abcdefghijklmnopqrstuvwxyz ");

    private readonly List<string> _tempFiles = [];
    private readonly List<string> _tempDirs = [];

    /// <summary>含 <c>hello.txt</c>(13B) 与 <c>big.bin</c>(32MB 可压缩) 的 ZIP。</summary>
    public string ZipPath { get; }

    /// <summary>含 <c>secret.txt</c>（密码 <c>test123</c>）的加密 ZIP。</summary>
    public string EncryptedZipPath { get; }

    /// <summary>含 <c>hello.txt</c> 的 tar.gz（走落盘慢路径）。</summary>
    public string TarGzPath { get; }

    /// <summary>含 <c>big7z.bin</c>(1MB) 的非固实 7z；本机无 7z.dll 时为 null。</summary>
    public string? SevenZipPath { get; }

    public ArchiveEntryExtractorFixture()
    {
        ZipPath = CreateZipWithLargeEntry();
        EncryptedZipPath = Track(ArchiveFixtures.CreateEncryptedZipArchive());
        TarGzPath = CreateTarGz();
        SevenZipPath = CreateSevenZipWithLargeEntry();
    }

    /// <summary>按固定模式填充 <paramref name="size"/> 字节（高度可压缩，但非全零，便于逐字节比对）。</summary>
    public static byte[] MakeCompressible(int size)
    {
        var buffer = new byte[size];
        for (int i = 0; i < size; i++)
            buffer[i] = Pattern[i % Pattern.Length];
        return buffer;
    }

    /// <summary>
    /// 自建 tar.gz（含 <c>hello.txt</c>）：**故意不复用 <see cref="ArchiveFixtures.CreateTarGzArchive"/>** ——
    /// 后者把中间文件固定写到 <c>%TEMP%\MantisZipTest\temp.tar</c>，与 <c>TarGzEngineTests</c>
    /// 分属不同测试类（xUnit 并行执行），两边同时创建/删除同一个 temp.tar 会抛
    /// <c>IOException: The process cannot access the file ... temp.tar</c>。
    /// 这里所有路径都带 Guid，互不干扰。
    /// </summary>
    private string CreateTarGz()
    {
        var dir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_tarsrc"));
        Directory.CreateDirectory(dir);
        var tarPath = Path.Combine(dir, "hello.tar");
        var gzPath = Track(Path.Combine(dir, "hello.tar.gz"));

        using (var tarFs = File.Create(tarPath))
        using (var tarOut = new TarOutputStream(tarFs, Encoding.UTF8))
        {
            var entry = TarEntry.CreateTarEntry("hello.txt");
            var hello = Encoding.UTF8.GetBytes(ArchiveFixtures.HelloText);
            entry.Size = hello.Length;
            tarOut.PutNextEntry(entry);
            tarOut.Write(hello, 0, hello.Length);
            tarOut.CloseEntry();
        }

        using (var inputFs = File.OpenRead(tarPath))
        using (var outputFs = File.Create(gzPath))
        using (var gzip = new GZipOutputStream(outputFs))
        {
            inputFs.CopyTo(gzip);
        }

        File.Delete(tarPath);
        return gzPath;
    }

    private string CreateZipWithLargeEntry()
    {
        var path = Track(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_head.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var fs = File.Create(path);
        using var zip = new ZipOutputStream(fs);
        zip.SetLevel(9);

        zip.PutNextEntry(new ZipEntry("hello.txt"));
        var hello = Encoding.UTF8.GetBytes(ArchiveFixtures.HelloText);
        zip.Write(hello, 0, hello.Length);
        zip.CloseEntry();

        // 大条目：循环写入同一块压缩友好的数据，避免真的占用 32MB 磁盘
        zip.PutNextEntry(new ZipEntry(ZipBigEntryName));
        var chunk = MakeCompressible(1024 * 1024);
        for (int written = 0; written < BigEntrySize; written += chunk.Length)
            zip.Write(chunk, 0, chunk.Length);
        zip.CloseEntry();

        return path;
    }

    private string? CreateSevenZipWithLargeEntry()
    {
        // 7z.dll 缺失时返回 null，调用方按既有惯例跳过（见 SevenZipEngineTests.Is7zDllAvailable）
        if (!File.Exists(SevenZipEngine.SevenZipDllPath))
            return null;

        try
        {
            SevenZipEngine.EnsureLibraryPath();
        }
        catch
        {
            return null;
        }

        var srcDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_7zsrc"));
        Directory.CreateDirectory(srcDir);
        File.WriteAllBytes(Path.Combine(srcDir, SevenZipBigEntryName), MakeCompressible(SevenZipEntrySize));

        var path = Track(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_head.7z"));
        var compressor = new SharpSevenZipCompressor
        {
            ArchiveFormat = OutArchiveFormat.SevenZip,
            CompressionLevel = CompressionLevel.Fast,
            CompressionMethod = SharpSevenZip.CompressionMethod.Lzma2,
            DirectoryStructure = true,
        };
        // 显式关闭固实压缩：固实档案会被 IsSevenZipSolid 判定并转落盘慢路径，
        // 本夹具专门用来覆盖 7z 的「截断写入」快路径。
        compressor.CustomParameters["s"] = "off";
        compressor.CompressDirectory(srcDir, path);

        return File.Exists(path) ? path : null;
    }

    private string Track(string path)
    {
        _tempFiles.Add(path);
        return path;
    }

    private string TrackDir(string path)
    {
        _tempDirs.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
        foreach (var file in _tempFiles.Where(File.Exists))
        {
            try { File.Delete(file); } catch { }
        }
    }
}
