using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using MantisZip.Tests.Fixtures;
using SharpSevenZip;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// <see cref="ArchiveEntryExtractor.ExtractEntryToMemoryAsync"/>（整条目读进内存，T7）的回归测试。
/// <list type="bullet">
/// <item>ZIP：真流式（<c>OpenEntryStream()</c> 读满上限即停），超限抛 <see cref="EntryTooLargeException"/>；</item>
/// <item>7z/RAR/ISO：先按元数据判上限，超限**不启动解压**，截断流只做兜底且事后必须失败；</item>
/// <item>tar/tar.gz 与固实 7z：先按 tar header / 7z 元数据判上限，再走落盘兜底；</item>
/// <item>契约：<c>maxBytes ≤ 0</c>、条目不存在、目录条目、加密条目的行为。</item>
/// </list>
///
/// <para>
/// 与 <see cref="ArchiveEntryExtractorTests"/> 同属 <c>DisableParallelization = true</c> 的
/// <c>ArchiveHeadMemory</c> 集合：内存断言读的是**进程级** <c>GC.GetTotalAllocatedBytes</c>，
/// 不能混入其它集合的分配；同时两个类串行执行，避免同时压缩大文件互相拖慢。
/// 所有临时文件都用 Guid 命名（不复用固定 <c>%TEMP%\MantisZipTest\temp.tar</c>，那是已知 flake 源）。
/// </para>
/// </summary>
[Collection("ArchiveHeadMemory")]
public sealed class ArchiveEntryToMemoryTests : IClassFixture<ArchiveEntryExtractorFixture>
{
    private readonly ArchiveEntryExtractorFixture _fx;

    public ArchiveEntryToMemoryTests(ArchiveEntryExtractorFixture fx) => _fx = fx;

    // ===== 1. ZIP 小条目：与完整解压逐字节一致 =====

    [Fact]
    public async Task ExtractEntryToMemoryAsync_Zip_SmallEntry_MatchesFullExtract()
    {
        var bytes = await ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
            _fx.ZipPath, "hello.txt", ArchiveFormat.Zip, maxBytes: 4096);

        Assert.Equal(Encoding.UTF8.GetBytes(ArchiveFixtures.HelloText), bytes);
        Assert.Equal(bytes, await ExtractFullEntryAsync(_fx.ZipPath, "hello.txt", ArchiveFormat.Zip, "zip"));
    }

    /// <summary>
    /// 验收：整条目读入的结果必须等于 <see cref="ArchiveEntryExtractor.ExtractHeadAsync"/>
    /// 以「条目大小」为上限取到的 head（同一取数口径）。
    /// </summary>
    [Fact]
    public async Task ExtractEntryToMemoryAsync_Zip_MatchesExtractHeadAsync_ForWholeEntry()
    {
        var bytes = await ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
            _fx.ZipPath, "hello.txt", ArchiveFormat.Zip, maxBytes: 4096);
        var head = await ArchiveEntryExtractor.ExtractHeadAsync(
            _fx.ZipPath, "hello.txt", 4096, ArchiveFormat.Zip);

        Assert.Equal(head, bytes);
    }

    // ===== 2. ZIP 超上限：EntryTooLargeException，绝不返回截断数据 =====

    [Fact]
    public async Task ExtractEntryToMemoryAsync_Zip_OverLimit_ThrowsEntryTooLarge()
    {
        const int maxBytes = 4096;

        var ex = await Assert.ThrowsAsync<EntryTooLargeException>(() =>
            ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                _fx.ZipPath, ArchiveEntryExtractorFixture.ZipBigEntryName,
                ArchiveFormat.Zip, maxBytes));

        Assert.Equal(maxBytes, ex.Limit);
        Assert.Equal(ArchiveEntryExtractorFixture.ZipBigEntryName, ex.EntryPath);
        // ZIP central directory 的 Size 是权威的，ActualSize 应为真实大小
        Assert.Equal(ArchiveEntryExtractorFixture.BigEntrySize, ex.ActualSize);
    }

    /// <summary>
    /// 内存断言：32MB 条目配 64KB 上限，托管分配必须远小于条目大小。
    /// 任何「先 WriteTo(MemoryStream) 再判长度」的实现都会分配 ≥ 32MB，本断言会稳稳判失败。
    /// </summary>
    [Fact]
    public async Task ExtractEntryToMemoryAsync_Zip_OverLimit_DoesNotAllocateWholeEntry()
    {
        const int maxBytes = 64 * 1024;
        const int entrySize = ArchiveEntryExtractorFixture.BigEntrySize;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetTotalAllocatedBytes(precise: true);
        var ex = await Record.ExceptionAsync(() => ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
            _fx.ZipPath, ArchiveEntryExtractorFixture.ZipBigEntryName,
            ArchiveFormat.Zip, maxBytes));
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.IsType<EntryTooLargeException>(ex);

        // 阈值取条目大小的 1/4（8MB）：实测流式读取 + 抛异常在 1MB 以内，
        // 留足余量容忍 SharpCompress 首次初始化 / JIT 的一次性分配；整条目实现（32MB）区分度 > 4 倍。
        Assert.True(
            allocated < entrySize / 4,
            $"整条目读取抛异常时分配了 {allocated:N0} 字节，超过条目大小 {entrySize:N0} 的 1/4（{entrySize / 4:N0}），疑似仍在整条目解压");
    }

    // ===== 3. ZIP 加密：正确密码返回明文；错误/缺失密码必须失败 =====

    [Fact]
    public async Task ExtractEntryToMemoryAsync_Zip_EncryptedEntry_WithPassword_ReturnsPlaintext()
    {
        var bytes = await ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
            _fx.EncryptedZipPath, "secret.txt", ArchiveFormat.Zip, maxBytes: 4096,
            password: "test123");

        Assert.Equal("secret data", Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// 如实断言现有语义：**缺失密码**与**错误密码**在 SharpCompress 下都表现为
    /// <see cref="SharpCompress.Common.CryptographicException"/>（"The password did not match."）。
    /// 注意这里**不是** <see cref="ArchiveEntryExtractor.PasswordRequiredException"/>——
    /// 后者只用于「加密文件名」的 7z（连条目列表都读不出来），本夹具是加密条目内容、文件名可见。
    /// </summary>
    [Fact]
    public async Task ExtractEntryToMemoryAsync_Zip_EncryptedEntry_MissingPassword_Throws()
    {
        var ex = await Record.ExceptionAsync(() => ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
            _fx.EncryptedZipPath, "secret.txt", ArchiveFormat.Zip, maxBytes: 4096));

        var crypto = Assert.IsType<SharpCompress.Common.CryptographicException>(ex);
        Assert.Contains("password", crypto.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>同 <see cref="ExtractEntryToMemoryAsync_Zip_EncryptedEntry_MissingPassword_Throws"/>：错误密码同类型失败。</summary>
    [Fact]
    public async Task ExtractEntryToMemoryAsync_Zip_EncryptedEntry_WrongPassword_Throws()
    {
        var ex = await Record.ExceptionAsync(() => ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
            _fx.EncryptedZipPath, "secret.txt", ArchiveFormat.Zip, maxBytes: 4096,
            password: "wrong-password"));

        var crypto = Assert.IsType<SharpCompress.Common.CryptographicException>(ex);
        Assert.Contains("password", crypto.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ===== 4. 7z：内容与完整解压一致；超限不返回截断数据 =====

    [Fact]
    public async Task ExtractEntryToMemoryAsync_SevenZip_MatchesFullExtract()
    {
        if (_fx.SevenZipPath == null)
            return; // 本机没有 7z.dll：跳过（与 SevenZipEngineTests 的既有写法一致）

        // 夹具显式关闭固实压缩；若这里变成固实，本用例走的就是落盘兜底而非快路径
        using (var extractor = new SharpSevenZipExtractor(_fx.SevenZipPath))
            Assert.False(extractor.IsSolid, "本用例要求非固实 7z，否则走的是落盘慢路径");

        var bytes = await ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
            _fx.SevenZipPath, ArchiveEntryExtractorFixture.SevenZipBigEntryName,
            ArchiveFormat.SevenZip, ArchiveEntryExtractorFixture.SevenZipEntrySize);

        Assert.Equal(ArchiveEntryExtractorFixture.MakeCompressible(ArchiveEntryExtractorFixture.SevenZipEntrySize), bytes);
        Assert.Equal(
            bytes,
            await ExtractFullEntryAsync(_fx.SevenZipPath, ArchiveEntryExtractorFixture.SevenZipBigEntryName, ArchiveFormat.SevenZip, "7z"));
    }

    [Fact]
    public async Task ExtractEntryToMemoryAsync_SevenZip_OverLimit_ThrowsWithoutTruncatedData()
    {
        if (_fx.SevenZipPath == null)
            return; // 本机没有 7z.dll：跳过

        const int maxBytes = 4096;
        var ex = await Assert.ThrowsAsync<EntryTooLargeException>(() =>
            ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                _fx.SevenZipPath, ArchiveEntryExtractorFixture.SevenZipBigEntryName,
                ArchiveFormat.SevenZip, maxBytes));

        Assert.Equal(maxBytes, ex.Limit);
        Assert.Equal(ArchiveEntryExtractorFixture.SevenZipBigEntryName, ex.EntryPath);
        // 元数据判上限就一定带得出真实大小（这条同时证明是「解压前」拒绝，而不是截断后才发现）
        Assert.Equal(ArchiveEntryExtractorFixture.SevenZipEntrySize, ex.ActualSize);
    }

    // ===== 4b. 固实 7z：走落盘兜底；超限必须在解压前拒绝、不落盘 =====

    [Fact]
    public async Task ExtractEntryToMemoryAsync_SolidSevenZip_FallsBackToFullExtract()
    {
        var archive = CreateSolidSevenZip(out var entryName, out var content);
        if (archive == null)
            return; // 本机没有 7z.dll：跳过

        try
        {
            // 本用例必须命中「固实 → 落盘兜底」分支，否则测的是非固实快路径
            using (var extractor = new SharpSevenZipExtractor(archive))
                Assert.True(extractor.IsSolid, "本用例要求固实 7z");

            // 恰好等于上限：可以完整返回，且与构造内容逐字节一致
            var bytes = await ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                archive, entryName, ArchiveFormat.SevenZip, maxBytes: content.Length);
            Assert.Equal(content, bytes);

            var before = SnapshotHeadExtractDir();
            var ex = await Assert.ThrowsAsync<EntryTooLargeException>(() =>
                ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                    archive, entryName, ArchiveFormat.SevenZip, maxBytes: 4096));
            var after = SnapshotHeadExtractDir();

            Assert.Equal(4096, ex.Limit);
            Assert.Equal(entryName, ex.EntryPath);
            // 按元数据判上限就一定带得出真实大小，说明拒绝发生在解压之前
            Assert.Equal(content.Length, ex.ActualSize);
            // 固实兜底也绝不能先把超限条目写进 %TEMP%
            Assert.DoesNotContain(after.Keys.Except(before.Keys), path => after[path] > 4096);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(archive)!, recursive: true);
        }
    }

    // ===== 5. tar.gz：落盘兜底内容正确；超限不落盘、临时目录不留大文件 =====
    [Fact]
    public async Task ExtractEntryToMemoryAsync_TarGz_MatchesFullExtract()
    {
        var bytes = await ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
            _fx.TarGzPath, "hello.txt", ArchiveFormat.Tar, maxBytes: 4096);

        Assert.Equal(Encoding.UTF8.GetBytes(ArchiveFixtures.HelloText), bytes);
        Assert.Equal(bytes, await ExtractFullEntryAsync(_fx.TarGzPath, "hello.txt", ArchiveFormat.Tar, "tgz"));
    }

    [Fact]
    public async Task ExtractEntryToMemoryAsync_TarGz_OverLimit_ThrowsAndLeavesNoBigTempFile()
    {
        const int maxBytes = 5; // hello.txt 实际 13 字节

        var before = SnapshotHeadExtractDir();
        var ex = await Assert.ThrowsAsync<EntryTooLargeException>(() =>
            ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                _fx.TarGzPath, "hello.txt", ArchiveFormat.Tar, maxBytes));
        var after = SnapshotHeadExtractDir();

        Assert.Equal(maxBytes, ex.Limit);
        Assert.Equal("hello.txt", ex.EntryPath);
        Assert.Equal(Encoding.UTF8.GetByteCount(ArchiveFixtures.HelloText), ex.ActualSize);
        // 超限条目绝不能以任何形式落到 %TEMP%（哪怕随后被删，也不该先写进去）
        Assert.DoesNotContain(after.Keys.Except(before.Keys), path => after[path] > maxBytes);
    }

    // ===== 5b. 纯 .gz：无条目名/无大小元数据，靠「读满即停」判上限 =====

    [Fact]
    public async Task ExtractEntryToMemoryAsync_PlainGZip_MatchesFullExtract()
    {
        var gzPath = CreatePlainGZip();
        try
        {
            var bytes = await ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                gzPath, "hello.txt", ArchiveFormat.GZip, maxBytes: 4096);

            Assert.Equal(Encoding.UTF8.GetBytes(ArchiveFixtures.HelloText), bytes);
            Assert.Equal(bytes, await ExtractFullEntryAsync(gzPath, "hello.txt", ArchiveFormat.GZip, "gz"));
        }
        finally
        {
            File.Delete(gzPath);
        }
    }

    [Fact]
    public async Task ExtractEntryToMemoryAsync_PlainGZip_OverLimit_ThrowsWithUnknownActualSize()
    {
        var gzPath = CreatePlainGZip();
        try
        {
            var ex = await Assert.ThrowsAsync<EntryTooLargeException>(() =>
                ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                    gzPath, "hello.txt", ArchiveFormat.GZip, maxBytes: 5));

            Assert.Equal(5, ex.Limit);
            // 纯 gzip 流没有大小元数据：ActualSize 如实给 -1，不猜
            Assert.Equal(-1, ex.ActualSize);
        }
        finally
        {
            File.Delete(gzPath);
        }
    }

    // ===== 6. 参数契约：maxBytes ≤ 0 =====

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1024)]
    public async Task ExtractEntryToMemoryAsync_NonPositiveMaxBytes_ThrowsArgumentOutOfRange(int maxBytes)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                _fx.ZipPath, "hello.txt", ArchiveFormat.Zip, maxBytes));
    }

    // ===== 7. 目录条目：返回空数组 =====

    [Fact]
    public async Task ExtractEntryToMemoryAsync_Zip_DirectoryEntry_ReturnsEmpty()
    {
        var zipPath = CreateZipWithDirectoryEntry("folder/");
        try
        {
            var bytes = await ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                zipPath, "folder/", ArchiveFormat.Zip, maxBytes: 4096);

            Assert.Empty(bytes);
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    // ===== 8. 不存在的条目：ArchiveEntryNotFoundException =====

    [Fact]
    public async Task ExtractEntryToMemoryAsync_Zip_MissingEntry_ThrowsArchiveEntryNotFound()
    {
        const string missing = "no-such-entry.txt";

        var ex = await Assert.ThrowsAsync<ArchiveEntryNotFoundException>(() =>
            ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                _fx.ZipPath, missing, ArchiveFormat.Zip, maxBytes: 4096));

        Assert.Equal(missing, ex.EntryPath);
    }

    [Fact]
    public async Task ExtractEntryToMemoryAsync_TarGz_MissingEntry_ThrowsArchiveEntryNotFound()
    {
        const string missing = "no-such-entry.txt";

        var ex = await Assert.ThrowsAsync<ArchiveEntryNotFoundException>(() =>
            ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                _fx.TarGzPath, missing, ArchiveFormat.Tar, maxBytes: 4096));

        Assert.Equal(missing, ex.EntryPath);
    }

    // ===== 辅助 =====

    /// <summary>
    /// 用既有 <see cref="ArchiveEntryExtractor.ExtractEntryAsync"/> 完整解压一次作为黄金参照，
    /// 临时文件带 Guid（不复用固定 temp 路径，避免与并行测试类抢文件）。
    /// </summary>
    private static async Task<byte[]> ExtractFullEntryAsync(
        string archivePath, string entryName, ArchiveFormat format, string tag)
    {
        var fullPath = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_full_{tag}.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            await ArchiveEntryExtractor.ExtractEntryAsync(archivePath, entryName, fullPath, format);
            return await File.ReadAllBytesAsync(fullPath);
        }
        finally
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
    }

    /// <summary>自建一个只含目录条目（<c>folder/</c>）的 ZIP。</summary>
    private static string CreateZipWithDirectoryEntry(string dirEntryName)
    {
        var path = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_dir.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var fs = File.Create(path);
        using var zip = new ZipOutputStream(fs);
        zip.PutNextEntry(new ZipEntry(dirEntryName));
        zip.CloseEntry();

        return path;
    }

    /// <summary>自建一个只有一条内容的纯 <c>.gz</c>（无条目名、无解压后大小元数据）。</summary>
    private static string CreatePlainGZip()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_plain.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var fs = File.Create(path);
        using var gzip = new GZipOutputStream(fs);
        var hello = Encoding.UTF8.GetBytes(ArchiveFixtures.HelloText);
        gzip.Write(hello, 0, hello.Length);

        return path;
    }

    /// <summary>
    /// 自建固实 7z（两个条目压进同一个 solid 块），用于覆盖「固实 7z → 落盘兜底」分支。
    /// 本机没有 7z.dll 时返回 null（调用方按既有惯例跳过）。
    /// </summary>
    private static string? CreateSolidSevenZip(out string entryName, out byte[] content)
    {
        entryName = ArchiveEntryExtractorFixture.SevenZipBigEntryName;
        content = ArchiveEntryExtractorFixture.MakeCompressible(512 * 1024);

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

        var root = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}_solid7z");
        var srcDir = Path.Combine(root, "src");
        Directory.CreateDirectory(srcDir);
        // 至少两个条目，7z 才会把 s=on 落成真正的固实块
        File.WriteAllBytes(Path.Combine(srcDir, "small.bin"),
            ArchiveEntryExtractorFixture.MakeCompressible(256 * 1024));
        File.WriteAllBytes(Path.Combine(srcDir, entryName), content);

        var path = Path.Combine(root, "solid.7z");
        var compressor = new SharpSevenZipCompressor
        {
            ArchiveFormat = OutArchiveFormat.SevenZip,
            CompressionLevel = CompressionLevel.Fast,
            CompressionMethod = SharpSevenZip.CompressionMethod.Lzma2,
            DirectoryStructure = true,
        };
        // 显式要求固实压缩（与夹具的 s=off 相反）
        compressor.CustomParameters["s"] = "on";
        compressor.CompressDirectory(srcDir, path);

        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 快照落盘兜底的临时目录（Core 的 <c>%TEMP%\MantisZip\HeadExtract</c>），
    /// 用于断言超限条目**没有**在该目录留下超过上限的文件。
    /// </summary>
    private static Dictionary<string, long> SnapshotHeadExtractDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZip", "HeadExtract");
        var snapshot = new Dictionary<string, long>();
        if (!Directory.Exists(dir))
            return snapshot;

        foreach (var file in Directory.GetFiles(dir))
        {
            try
            {
                snapshot[file] = new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // 并发写入中的文件读不到大小：不纳入比较（它不可能是本用例刚创建的）
            }
        }

        return snapshot;
    }
}
