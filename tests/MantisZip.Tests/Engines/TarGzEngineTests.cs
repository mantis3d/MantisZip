using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Tests.Fixtures;
using Xunit;

namespace MantisZip.Tests.Engines;

public class TarGzEngineTests : IDisposable
{
    private readonly TarGzEngine _engine = new();
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles.Where(File.Exists))
            try { File.Delete(f); } catch { }
        foreach (var d in _tempDirs.Where(Directory.Exists))
            try { Directory.Delete(d, true); } catch { }
    }

    private string TrackFile(string path) { _tempFiles.Add(path); return path; }
    private string TrackDir(string path) { _tempDirs.Add(path); return path; }

    // ===== CanHandle =====

    [Fact]
    public void CanHandle_TarAndGZip_ReturnsTrue()
    {
        Assert.True(_engine.CanHandle(ArchiveFormat.Tar));
        Assert.True(_engine.CanHandle(ArchiveFormat.GZip));
    }

    [Fact]
    public void CanHandle_OtherFormats_ReturnsFalse()
    {
        Assert.False(_engine.CanHandle(ArchiveFormat.Zip));
        Assert.False(_engine.CanHandle(ArchiveFormat.SevenZip));
        Assert.False(_engine.CanHandle(ArchiveFormat.Rar));
        Assert.False(_engine.CanHandle(ArchiveFormat.Iso));
    }

    // ===== ListEntriesAsync =====

    [Fact]
    public async Task ListEntriesAsync_ReturnsEntries()
    {
        var archive = TrackFile(ArchiveFixtures.CreateTarGzArchive());

        var entries = await _engine.ListEntriesAsync(archive);

        Assert.NotEmpty(entries);
        Assert.Single(entries, e => e.Name == "hello.txt" && !e.IsDirectory);
        Assert.Single(entries, e => e.Name == "subdir/nested.txt" && !e.IsDirectory);
    }

    [Fact]
    public async Task ListEntriesAsync_EntryHasCorrectSize()
    {
        var archive = TrackFile(ArchiveFixtures.CreateTarGzArchive());

        var entries = await _engine.ListEntriesAsync(archive);
        var helloEntry = Assert.Single(entries, e => e.Name == "hello.txt");

        Assert.Equal(ArchiveFixtures.HelloText.Length, helloEntry.Size);
    }

    // ===== ExtractAsync =====

    [Fact]
    public async Task ExtractAsync_ExtractsFiles()
    {
        var archive = TrackFile(ArchiveFixtures.CreateTarGzArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));

        await _engine.ExtractAsync(archive, dest);

        Assert.True(File.Exists(Path.Combine(dest, "hello.txt")));
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "subdir", "nested.txt")));
        Assert.Equal(ArchiveFixtures.NestedDirFileContent, await File.ReadAllTextAsync(Path.Combine(dest, "subdir", "nested.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithConflictSkip_SkipsExisting()
    {
        var archive = TrackFile(ArchiveFixtures.CreateTarGzArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(dest);
        await File.WriteAllTextAsync(Path.Combine(dest, "hello.txt"), "old content");

        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Skip };
        await _engine.ExtractAsync(archive, dest, options: options);

        Assert.Equal("old content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithConflictRename_Renames()
    {
        var archive = TrackFile(ArchiveFixtures.CreateTarGzArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(dest);
        await File.WriteAllTextAsync(Path.Combine(dest, "hello.txt"), "old content");

        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Rename };
        await _engine.ExtractAsync(archive, dest, options: options);

        Assert.Equal("old content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "hello (1).txt")));
    }

    // ===== CompressAsync =====

    [Fact]
    public async Task CompressAsync_CreatesValidArchive()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.tar.gz"));

        // Compress individual files (not the whole dir) so archive entries have no dir prefix
        await _engine.CompressAsync([Path.Combine(srcDir, "hello.txt")], outputPath, new ArchiveOptions());

        Assert.True(File.Exists(outputPath), $"Archive not created at {outputPath}");

        // Verify by re-extracting
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(outputPath, dest);

        var helloPath = Path.Combine(dest, "hello.txt");
        Assert.True(File.Exists(helloPath), $"hello.txt not extracted at {helloPath}");

        // Verify content
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(helloPath));
    }

    // ===== TestArchiveAsync =====

    [Fact]
    public async Task TestArchiveAsync_ValidArchive_ReturnsTrue()
    {
        var archive = TrackFile(ArchiveFixtures.CreateTarGzArchive());

        var result = await _engine.TestArchiveAsync(archive);

        Assert.True(result);
    }

    [Fact]
    public async Task TestArchiveAsync_InvalidArchive_ReturnsFalse()
    {
        var badDir = Path.Combine(Path.GetTempPath(), "MantisZipTest");
        Directory.CreateDirectory(badDir);
        var badPath = TrackFile(Path.Combine(badDir, $"{Guid.NewGuid()}.tar.gz"));
        // Write data with GZip magic header but corrupt payload to ensure TestArchive fails
        var badGzip = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00 };
        await File.WriteAllBytesAsync(badPath, badGzip);

        var result = await _engine.TestArchiveAsync(badPath);

        Assert.False(result);
    }

    // ===== Progress Reporting =====

    [Fact]
    public async Task ExtractAsync_ReportsProgress()
    {
        var archive = TrackFile(ArchiveFixtures.CreateTarGzArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        var progressItems = new List<ArchiveProgress>();

        await _engine.ExtractAsync(archive, dest, progress: new Progress<ArchiveProgress>(p =>
        {
            progressItems.Add(p);
        }));

        Assert.NotEmpty(progressItems);
        Assert.Contains(progressItems, p => p.PercentComplete == 100);
    }

    // ===== 字节埋点（ProcessedBytes / TotalBytes） =====

    /// <summary>
    /// 有序进度收集器：同步 Report 按报告顺序记录，供字节单调性断言。
    /// 刻意不用 System.Progress&lt;T&gt;（其经线程池异步派发会打乱顺序）。
    /// </summary>
    private sealed class OrderedProgressCollector : IProgress<ArchiveProgress>
    {
        private readonly List<ArchiveProgress> _items = new();
        public void Report(ArchiveProgress value) { lock (_items) _items.Add(value); }
        public IReadOnlyList<ArchiveProgress> Snapshot()
        {
            lock (_items) return _items.ToList();
        }
        public IProgress<ArchiveProgress> Progress => this;
    }

    /// <summary>
    /// 断言：所有带 TotalBytes &gt; 0 的报告中 ProcessedBytes 单调不减，
    /// 且最后一个（最终 100%）报告满足 ProcessedBytes == TotalBytes。
    /// </summary>
    private static void AssertByteProgressMonotonic(
        IReadOnlyList<ArchiveProgress> items, string label)
    {
        var withBytes = items.Where(p => p.TotalBytes > 0).ToList();
        Assert.True(withBytes.Count > 0,
            $"{label}: 未捕获任何带 TotalBytes 的进度报告");
        long last = -1;
        foreach (var p in withBytes)
        {
            Assert.True(p.ProcessedBytes >= last,
                $"{label}: ProcessedBytes 回退 {last} -> {p.ProcessedBytes} (Percent={p.PercentComplete})");
            last = p.ProcessedBytes;
        }
        var final = withBytes[^1];
        Assert.Equal(final.TotalBytes, final.ProcessedBytes);
    }

    [Fact]
    public async Task ExtractAsync_ReportsByteProgress()
    {
        var archive = TrackFile(ArchiveFixtures.CreateTarGzArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        var collector = new OrderedProgressCollector();

        await _engine.ExtractAsync(archive, dest,
            progress: collector.Progress, cancellationToken: CancellationToken.None);

        AssertByteProgressMonotonic(collector.Snapshot(), "TarGz.ExtractAsync");
    }

    [Fact]
    public async Task CompressAsync_ReportsByteProgress()
    {
        var dir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        var src = Path.Combine(dir, "src");
        Directory.CreateDirectory(src);
        await File.WriteAllBytesAsync(Path.Combine(src, "a.bin"), new byte[128 * 1024]);
        await File.WriteAllBytesAsync(Path.Combine(src, "b.bin"), new byte[64 * 1024]);

        var archivePath = TrackFile(Path.Combine(dir, "out.tar"));
        var collector = new OrderedProgressCollector();
        await _engine.CompressAsync(new[] { src }, archivePath,
            new ArchiveOptions(), collector.Progress, CancellationToken.None);

        AssertByteProgressMonotonic(collector.Snapshot(), "TarGz.CompressAsync");
    }

    [Fact]
    public async Task CompressExtract_GzRoundtrip_ReportsByteProgressBothWays()
    {
        var dir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(dir);
        var srcFile = Path.Combine(dir, "a.bin");
        await File.WriteAllBytesAsync(srcFile, new byte[256 * 1024]);
        var archivePath = TrackFile(Path.Combine(dir, "out.gz"));

        var c = new OrderedProgressCollector();
        await _engine.CompressAsync(new[] { srcFile }, archivePath,
            new ArchiveOptions(), c.Progress, CancellationToken.None);
        AssertByteProgressMonotonic(c.Snapshot(), "Compress.gz");

        var dest = TrackDir(Path.Combine(dir, "out"));
        Directory.CreateDirectory(dest);
        var e = new OrderedProgressCollector();
        await _engine.ExtractAsync(archivePath, dest,
            progress: e.Progress, cancellationToken: CancellationToken.None);
        AssertByteProgressMonotonic(e.Snapshot(), "Extract.gz");
    }
}
