using System.Text;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Tests.Fixtures;
using Xunit;

namespace MantisZip.Tests.Engines;

public class ZipEngineDeleteTests : IDisposable
{
    private readonly ZipEngine _engine = new();
    private readonly List<string> _tempFiles = new();

    public ZipEngineDeleteTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles.Where(File.Exists))
            try { File.Delete(f); } catch { }
    }

    private string TrackFile(string path) { _tempFiles.Add(path); return path; }

    // ===== DeleteEntriesAsync =====

    [Fact]
    public async Task DeleteEntriesAsync_DeletesSingleEntry()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await _engine.DeleteEntriesAsync(archive, ["hello.txt"]);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.DoesNotContain(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "subdir/nested.txt");
    }

    [Fact]
    public async Task DeleteEntriesAsync_DeletesSubdirEntry()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await _engine.DeleteEntriesAsync(archive, ["subdir/nested.txt"]);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.DoesNotContain(entries, e => e.Name == "subdir/nested.txt");
        Assert.Contains(entries, e => e.Name == "hello.txt");
    }

    [Fact]
    public async Task DeleteEntriesAsync_DeletesMultipleEntries()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await _engine.DeleteEntriesAsync(archive, ["hello.txt", "subdir/nested.txt"]);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task DeleteEntriesAsync_NonExistentEntry_ThrowsFileNotFoundException()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _engine.DeleteEntriesAsync(archive, ["nonexistent.txt"]));
    }

    [Fact]
    public async Task DeleteEntriesAsync_EmptyEntryList_DoesNothing()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await _engine.DeleteEntriesAsync(archive, []);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.NotEmpty(entries);
    }

    [Fact]
    public async Task DeleteEntriesAsync_CanDeleteAndReAddSameName()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        // Delete then re-add a file with the same name
        await _engine.DeleteEntriesAsync(archive, ["hello.txt"]);

        var newFile = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "hello.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        await File.WriteAllTextAsync(newFile, "new content");
        _tempFiles.Add(newFile);

        await _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions());

        var entries = await _engine.ListEntriesAsync(archive);
        var helloEntry = Assert.Single(entries, e => e.Name == "hello.txt");
        // SharpZipLib reports Size of the newly added entry
        Assert.Equal("new content".Length, helloEntry.Size);
    }

    [Fact]
    public async Task DeleteEntriesAsync_ArchiveRemainsValid()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await _engine.DeleteEntriesAsync(archive, ["hello.txt"]);

        // Archive should still be valid (can list and extract remaining entries)
        var result = await _engine.TestArchiveAsync(archive);
        Assert.True(result);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Single(entries);
    }

    // ===== Format capability =====

    [Fact]
    public void CanDelete_Zip_ReturnsTrue()
    {
        Assert.True(_engine.CanDelete(ArchiveFormat.Zip));
    }

    [Fact]
    public void CanDelete_OtherFormats_ReturnsFalse()
    {
        Assert.False(_engine.CanDelete(ArchiveFormat.SevenZip));
        Assert.False(_engine.CanDelete(ArchiveFormat.Tar));
        Assert.False(_engine.CanDelete(ArchiveFormat.GZip));
        Assert.False(_engine.CanDelete(ArchiveFormat.Rar));
        Assert.False(_engine.CanDelete(ArchiveFormat.Iso));
    }

    [Fact]
    public void CanAdd_Zip_ReturnsTrue()
    {
        Assert.True(_engine.CanAdd(ArchiveFormat.Zip));
    }
}
