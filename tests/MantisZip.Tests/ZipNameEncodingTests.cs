using System.Text;
using MantisZip.Core.Abstractions;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// 文件名代码页（Bandizip 式切换）的端到端行为：Big5 命名的 ZIP 在指定代码页后能正确列出，
/// 且「删除条目重写中央目录」不会把存活条目的名字改成乱码（改造前硬编码 GBK，必坏）。
/// </summary>
public class ZipNameEncodingTests : IDisposable
{
    private readonly string _root;

    public ZipNameEncodingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MantisZipNameEncoding", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 清理失败不影响断言 */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ListEntriesAsync_ForcedBig5_DecodesEntryNames()
    {
        var archivePath = CreateBig5Zip();
        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        Assert.NotNull(engine);

        string? resolved = null;
        var items = await engine!.ListEntriesAsync(
            archivePath, fileNameEncoding: "big5", onEncodingResolved: key => resolved = key);

        var names = items.Where(i => !i.IsDirectory).Select(i => i.Name).ToList();
        Assert.Contains(TraditionalName, names);
        Assert.Contains("測試二.txt", names);
        Assert.Equal("big5", resolved);
    }

    [Fact]
    public async Task ListEntriesAsync_ForcedWrongCodePage_DoesNotProduceOriginalNames()
    {
        var archivePath = CreateBig5Zip();
        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        Assert.NotNull(engine);

        // 用 GBK 强解 Big5 字节必然对不上：证明「用户选的代码页真的生效了」，
        // 而不是被内部启发式覆盖掉了
        var wrong = await engine!.ListEntriesAsync(archivePath, fileNameEncoding: "gbk");
        var wrongNames = wrong.Where(i => !i.IsDirectory).Select(i => i.Name).ToList();

        Assert.DoesNotContain(TraditionalName, wrongNames);
    }

    [Fact]
    public async Task DeleteEntries_WithBig5Encoding_KeepsSurvivingNamesIntact()
    {
        var archivePath = CreateBig5Zip();
        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        Assert.NotNull(engine);

        var before = await engine!.ListEntriesAsync(archivePath, fileNameEncoding: "big5");
        var victim = before.First(i => !i.IsDirectory && i.Name == "測試二.txt");

        await engine.DeleteEntriesAsync(
            archivePath,
            new[] { victim.FullPath ?? victim.Name },
            options: new ArchiveOptions { FileNameEncoding = "big5", ReadEncoding = "big5" });

        var after = await engine.ListEntriesAsync(archivePath, fileNameEncoding: "big5");
        var afterNames = after.Where(i => !i.IsDirectory).Select(i => i.Name).ToList();

        // 存活条目仍是原名（改造前这里会变成 UTF-8 重写的乱码）
        Assert.Contains(TraditionalName, afterNames);
        Assert.DoesNotContain("測試二.txt", afterNames);
    }

    private const string TraditionalName = "中文檔案.txt";

    // ── 用户手动选的代码页必须「真的生效」（含带 UTF-8 标志位的现代包） ──

    [Fact]
    public async Task ListEntriesAsync_Utf8FlaggedArchive_ForcedWrongCodePage_ShowsMojibake()
    {
        var archivePath = CreateUtf8FlaggedZip();
        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        Assert.NotNull(engine);

        // 包内条目名自带 UTF-8 声明：只要不手动干预就该是正确名字
        var auto = await engine!.ListEntriesAsync(archivePath);
        var autoNames = auto.Where(i => !i.IsDirectory).Select(i => i.Name).ToList();
        Assert.Contains(TraditionalName, autoNames);

        // 手动选了错代码页 ⇒ 必须盖过包内 bit 11，看到乱码（否则用户会以为按钮失灵）
        var wrong = await engine.ListEntriesAsync(archivePath, fileNameEncoding: "gbk");
        var wrongNames = wrong.Where(i => !i.IsDirectory).Select(i => i.Name).ToList();
        Assert.DoesNotContain(TraditionalName, wrongNames);
        Assert.NotEqual(autoNames, wrongNames);
    }

    [Fact]
    public async Task DeleteEntries_Utf8FlaggedArchive_WithForcedWrongCodePage_KeepsSurvivingNamesIntact()
    {
        var archivePath = CreateUtf8FlaggedZip();
        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        Assert.NotNull(engine);

        // 界面按用户选的（错）代码页列出，用户点删其中一个 —— 名字是乱码，但删除本身要能对上号
        var display = (await engine!.ListEntriesAsync(archivePath, fileNameEncoding: "gbk"))
            .Where(i => !i.IsDirectory)
            .ToList();
        Assert.Equal(2, display.Count);
        var victim = display[1];

        await engine.DeleteEntriesAsync(
            archivePath,
            new[] { victim.FullPath ?? victim.Name },
            options: new ArchiveOptions { ReadEncoding = "gbk", FileNameEncoding = "gbk" });

        // 写回必须按包内声明（UTF-8）做：存活条目重新按自动检测读出来仍是原名，没被写成 GBK 乱码
        var after = await engine.ListEntriesAsync(archivePath);
        var afterNames = after.Where(i => !i.IsDirectory).Select(i => i.Name).ToList();
        Assert.Single(afterNames);
        Assert.Equal(TraditionalName, afterNames[0]);
        Assert.DoesNotContain('\uFFFD', afterNames[0]);
    }

    /// <summary>造一个「条目名带 UTF-8 标志位（bit 11）」的现代包：.NET 的 ZipArchive 对非 ASCII 名会自动置位。</summary>
    private string CreateUtf8FlaggedZip()
    {
        var path = Path.Combine(_root, "utf8-flagged.zip");
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var (name, payload) in new[] { (TraditionalName, "alpha"), ("測試二.txt", "beta") })
        {
            var entry = zip.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(payload);
        }

        return path;
    }

    private string CreateBig5Zip()
    {
        var path = Path.Combine(_root, "big5.zip");
        var big5 = Encoding.GetEncoding(950);

        using var stream = File.Create(path);
        using var writer = new SharpCompress.Writers.Zip.ZipWriter(
            stream,
            new SharpCompress.Writers.Zip.ZipWriterOptions(SharpCompress.Common.CompressionType.Deflate)
            {
                ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding { Default = big5 }
            });

        WriteEntry(writer, TraditionalName, "alpha");
        WriteEntry(writer, "測試二.txt", "beta");
        return path;
    }

    private static void WriteEntry(SharpCompress.Writers.IWriter writer, string entryName, string content)
    {
        using var payload = new MemoryStream(Encoding.UTF8.GetBytes(content));
        writer.Write(entryName, payload, DateTime.UtcNow);
    }
}
