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
        var badPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.tar.gz"));
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
}
