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
