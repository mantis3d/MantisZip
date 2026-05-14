using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Tests.Fixtures;
using Xunit;

namespace MantisZip.Tests.Engines;

public class SevenZipEngineTests : IDisposable
{
    private readonly SevenZipEngine _engine = new();
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

    /// <summary>Check if 7z.exe is available for tests that need archive creation.</summary>
    private static bool Is7zExeAvailable() =>
        File.Exists(SevenZipEngine.SevenZipPath);

    // ===== CanHandle =====

    [Fact]
    public void CanHandle_SevenZipRarIso_ReturnsTrue()
    {
        Assert.True(_engine.CanHandle(ArchiveFormat.SevenZip));
        Assert.True(_engine.CanHandle(ArchiveFormat.Rar));
        Assert.True(_engine.CanHandle(ArchiveFormat.Iso));
    }

    [Fact]
    public void CanHandle_OtherFormats_ReturnsFalse()
    {
        Assert.False(_engine.CanHandle(ArchiveFormat.Zip));
        Assert.False(_engine.CanHandle(ArchiveFormat.Tar));
        Assert.False(_engine.CanHandle(ArchiveFormat.GZip));
    }

    // ===== ListEntriesAsync (requires 7z archive for meaningful test) =====

    [Fact]
    public async Task ListEntriesAsync_With7zArchive_ReturnsEntries()
    {
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return; // Skip if 7z.exe not available
        TrackFile(archive);

        var entries = await _engine.ListEntriesAsync(archive);

        Assert.NotEmpty(entries);
        Assert.Single(entries, e => e.Name.Contains("hello.txt"));
    }

    [Fact]
    public async Task ListEntriesAsync_RarCanHandle_IsTrue()
    {
        // Cannot create a RAR programmatically, but at least CanHandle returns true
        Assert.True(_engine.CanHandle(ArchiveFormat.Rar));
        await Task.CompletedTask;
    }

    // ===== ExtractAsync =====

    [Fact]
    public async Task ExtractAsync_ExtractsFiles()
    {
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));

        await _engine.ExtractAsync(archive, dest);

        Assert.True(File.Exists(Path.Combine(dest, "hello.txt")));
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithConflictRename_Renames()
    {
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(dest);
        await File.WriteAllTextAsync(Path.Combine(dest, "hello.txt"), "old content");

        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Rename };
        await _engine.ExtractAsync(archive, dest, options: options);

        Assert.Equal("old content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "hello (1).txt")));
    }

    [Fact]
    public async Task ExtractAsync_UnusedPassword_DoesNotFail()
    {
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));

        // Archive has no password, but passing a password shouldn't fail for unencrypted entries
        await _engine.ExtractAsync(archive, dest, "irrelevant_password");
        Assert.True(File.Exists(Path.Combine(dest, "hello.txt")));
    }

    // ===== CompressAsync (requires 7z.exe) =====

    [Fact]
    public async Task CompressAsync_CreatesValidArchive()
    {
        if (!Is7zExeAvailable()) return;

        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.7z"));

        await _engine.CompressAsync([srcDir], outputPath, new ArchiveOptions());

        Assert.True(File.Exists(outputPath));

        // Verify by re-extracting
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(outputPath, dest);
        Assert.True(File.Exists(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task CompressAsync_When7zExeMissing_Throws()
    {
        var originalPath = SevenZipEngine.SevenZipPath;
        SevenZipEngine.SevenZipPath = @"C:\Nonexistent\7z.exe";
        try
        {
            var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
            var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.7z"));

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                _engine.CompressAsync([srcDir], outputPath, new ArchiveOptions()));
        }
        finally
        {
            SevenZipEngine.SevenZipPath = originalPath;
        }
    }

    // ===== TestArchiveAsync =====

    [Fact]
    public async Task TestArchiveAsync_ValidArchive_ReturnsTrue()
    {
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var result = await _engine.TestArchiveAsync(archive);

        Assert.True(result);
    }

    [Fact]
    public async Task TestArchiveAsync_InvalidFile_ReturnsFalse()
    {
        var badPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.7z"));
        await File.WriteAllTextAsync(badPath, "not a 7z file");

        var result = await _engine.TestArchiveAsync(badPath);
        Assert.False(result);
    }

    // ===== Progress Reporting =====

    [Fact]
    public async Task ExtractAsync_ReportsProgress()
    {
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

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
