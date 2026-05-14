using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Tests.Fixtures;
using Xunit;

namespace MantisZip.Tests.Engines;

public class ZipEngineTests : IDisposable
{
    private readonly ZipEngine _engine = new();
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public ZipEngineTests()
    {
        // Ensure GBK encoding support for ZIP (same as App.InitializeApp)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ZipStrings.CodePage = 936;
    }

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
    public void CanHandle_Zip_ReturnsTrue()
    {
        Assert.True(_engine.CanHandle(ArchiveFormat.Zip));
    }

    [Fact]
    public void CanHandle_OtherFormats_ReturnsFalse()
    {
        Assert.False(_engine.CanHandle(ArchiveFormat.SevenZip));
        Assert.False(_engine.CanHandle(ArchiveFormat.Tar));
        Assert.False(_engine.CanHandle(ArchiveFormat.GZip));
        Assert.False(_engine.CanHandle(ArchiveFormat.Rar));
        Assert.False(_engine.CanHandle(ArchiveFormat.Iso));
    }

    // ===== ListEntriesAsync =====

    [Fact]
    public async Task ListEntriesAsync_ReturnsEntries()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        var entries = await _engine.ListEntriesAsync(archive);

        Assert.NotEmpty(entries);
        var helloEntry = Assert.Single(entries, e => e.Name == "hello.txt");
        Assert.Equal(ArchiveFixtures.HelloText.Length, helloEntry.Size);
        Assert.False(helloEntry.IsDirectory);

        var nestedEntry = Assert.Single(entries, e => e.Name == "subdir/nested.txt");
        Assert.Equal(ArchiveFixtures.NestedDirFileContent.Length, nestedEntry.Size);
    }

    [Fact]
    public async Task ListEntriesAsync_EncryptedWithoutPassword_StillReturnsEntries()
    {
        // SharpZipLib can enumerate entries without decrypting
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());

        var entries = await _engine.ListEntriesAsync(archive);

        Assert.NotEmpty(entries);
        Assert.Single(entries, e => e.Name == "secret.txt");
    }

    // ===== ExtractAsync =====

    [Fact]
    public async Task ExtractAsync_ExtractsFiles()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));

        await _engine.ExtractAsync(archive, dest);

        Assert.True(File.Exists(Path.Combine(dest, "hello.txt")));
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "subdir", "nested.txt")));
        Assert.Equal(ArchiveFixtures.NestedDirFileContent, await File.ReadAllTextAsync(Path.Combine(dest, "subdir", "nested.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithPassword_ExtractsEncrypted()
    {
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));

        await _engine.ExtractAsync(archive, dest, "test123");

        Assert.True(File.Exists(Path.Combine(dest, "secret.txt")));
        Assert.Equal("secret data", await File.ReadAllTextAsync(Path.Combine(dest, "secret.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithoutPassword_ThrowsOnEncrypted()
    {
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _engine.ExtractAsync(archive, dest));
    }

    [Fact]
    public async Task ExtractAsync_WithConflictOverwrite_Overwrites()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "hello.txt"), "old content");

        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite };
        await _engine.ExtractAsync(archive, dest, options: options);

        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithConflictSkip_SkipsExisting()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "hello.txt"), "old content");

        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Skip };
        await _engine.ExtractAsync(archive, dest, options: options);

        // Should keep old content
        Assert.Equal("old content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithConflictRename_RenamesNewFile()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "hello.txt"), "old content");

        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Rename };
        await _engine.ExtractAsync(archive, dest, options: options);

        // Original preserved, new file renamed
        Assert.Equal("old content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "hello (1).txt")));
    }

    // ===== CompressAsync =====

    [Fact]
    public async Task CompressAsync_CreatesValidArchive()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip"));

        await _engine.CompressAsync([srcDir], outputPath, new ArchiveOptions());

        Assert.True(File.Exists(outputPath));

        // Verify by listing entries
        var entries = await _engine.ListEntriesAsync(outputPath);
        Assert.Contains(entries, e => e.Name.Contains("hello.txt"));
        Assert.Contains(entries, e => e.Name.Contains("subdir/nested.txt"));
    }

    [Fact]
    public async Task CompressAsync_RespectsCompressionLevel()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip"));

        await _engine.CompressAsync([srcDir], outputPath, new ArchiveOptions { CompressionLevel = 1 });

        Assert.True(File.Exists(outputPath));
        var entries = await _engine.ListEntriesAsync(outputPath);
        Assert.NotEmpty(entries);
    }

    // ===== TestArchiveAsync =====

    [Fact]
    public async Task TestArchiveAsync_ValidArchive_ReturnsTrue()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        var result = await _engine.TestArchiveAsync(archive);

        Assert.True(result);
    }

    [Fact]
    public async Task TestArchiveAsync_InvalidArchive_ReturnsFalse()
    {
        var badPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip"));
        File.WriteAllText(badPath, "not a zip file");

        var result = await _engine.TestArchiveAsync(badPath);

        Assert.False(result);
    }

    [Fact]
    public async Task TestArchiveAsync_EncryptedArchive_ReturnsTrue()
    {
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());

        var result = await _engine.TestArchiveAsync(archive, "test123");

        Assert.True(result);
    }

    // ===== AddToArchiveAsync =====

    [Fact]
    public async Task AddToArchiveAsync_AddsFiles()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var newFile = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "added.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        await File.WriteAllTextAsync(newFile, "added content");
        _tempFiles.Add(newFile);

        await _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions());

        // Verify the file was added
        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "added.txt");
    }

    // ===== Progress Reporting =====

    [Fact]
    public async Task ExtractAsync_ReportsProgress()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        var progressItems = new List<ArchiveProgress>();

        await _engine.ExtractAsync(archive, dest, progress: new Progress<ArchiveProgress>(p =>
        {
            progressItems.Add(p);
        }));

        // Should have at least initial and final progress reports
        Assert.NotEmpty(progressItems);
        Assert.Contains(progressItems, p => p.PercentComplete == 100);
    }
}
