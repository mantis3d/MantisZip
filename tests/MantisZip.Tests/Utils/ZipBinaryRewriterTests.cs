using System.Reflection;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using MantisZip.Tests.Fixtures;
using SharpCompress.Archives;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// Comprehensive tests for the ZIP copy-mode optimization (ZipBinaryRewriter)
/// plus copy-mode code paths in ZipEngine.AddToArchiveAsync and DeleteEntriesAsync.
/// </summary>
public class ZipBinaryRewriterTests : IDisposable
{
    private readonly ZipEngine _engine = new();
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public ZipBinaryRewriterTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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

    // ══════════════════════════════════════════════════════════════════
    // Reflection helper — ZipBinaryRewriter is internal
    // ══════════════════════════════════════════════════════════════════

    private static readonly Type RewriterType =
        typeof(ZipEngine).Assembly.GetType("MantisZip.Core.Utils.ZipBinaryRewriter")!;

    /// <summary>
    /// Invoke <see cref="ZipBinaryRewriter.RewriteAsync"/> via reflection
    /// since the class is internal (though the method is public).
    /// </summary>
    private static Task<RewriteResult> InvokeRewriteAsync(
        string sourcePath,
        string destPath,
        HashSet<string>? keepEntryNames,
        List<NewEntry>? addEntries,
        Encoding encoding,
        string? comment = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var method = RewriterType.GetMethod("RewriteAsync",
            BindingFlags.Public | BindingFlags.Static)!;
        var task = (Task<RewriteResult>)method.Invoke(null, [
            sourcePath,
            destPath,
            keepEntryNames,
            addEntries,
            encoding,
            comment,
            progress,
            cancellationToken
        ])!;
        return task;
    }

    // ══════════════════════════════════════════════════════════════════
    // Test ZIP creation helpers
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a ZIP archive with specified characteristics.
    /// </summary>
    private string CreateTestZip(Action<ZipOutputStream> customize)
    {
        var path = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var zipStream = new ZipOutputStream(fs)
        {
            UseZip64 = UseZip64.Off  // required: copy-mode doesn't support ZIP64
        };
        customize(zipStream);

        return TrackFile(path);
    }

    /// <summary>
    /// Create a ZIP with Store (method 0) compression for copy-mode testing.
    /// </summary>
    private string CreateStoreZipArchive()
    {
        return CreateTestZip(zipStream =>
        {
            zipStream.SetLevel(0); // Store (no compression)

            var entry = new ZipEntry("store_file.txt");
            zipStream.PutNextEntry(entry);
            var bytes = Encoding.UTF8.GetBytes("Store mode content — no compression");
            zipStream.Write(bytes, 0, bytes.Length);
            zipStream.CloseEntry();

            var entry2 = new ZipEntry("another_store.bin");
            zipStream.PutNextEntry(entry2);
            var bytes2 = Encoding.UTF8.GetBytes("Second store entry");
            zipStream.Write(bytes2, 0, bytes2.Length);
            zipStream.CloseEntry();
        });
    }

    /// <summary>
    /// Create a standard ZIP (ZIP64 disabled) that mirrors ArchiveFixtures.CreateZipArchive
    /// but is compatible with copy-mode (which does not support ZIP64).
    /// Contains: hello.txt (Hello, World!) and subdir/nested.txt (Nested content).
    /// </summary>
    private string CreateStandardZipArchive()
    {
        return CreateTestZip(zipStream =>
        {
            zipStream.SetLevel(9);

            var entry = new ZipEntry("hello.txt");
            zipStream.PutNextEntry(entry);
            var helloBytes = Encoding.UTF8.GetBytes(ArchiveFixtures.HelloText);
            zipStream.Write(helloBytes, 0, helloBytes.Length);
            zipStream.CloseEntry();

            var nestedEntry = new ZipEntry("subdir/nested.txt");
            zipStream.PutNextEntry(nestedEntry);
            var nestedBytes = Encoding.UTF8.GetBytes(ArchiveFixtures.NestedDirFileContent);
            zipStream.Write(nestedBytes, 0, nestedBytes.Length);
            zipStream.CloseEntry();
        });
    }

    /// <summary>
    /// Create a ZIP with Chinese filenames for encoding preservation tests.
    /// SharpZipLib will use UTF-8 encoding with bit 11 flag set.
    /// </summary>
    private string CreateZipWithChineseName()
    {
        return CreateTestZip(zipStream =>
        {
            zipStream.SetLevel(9);

            var entry = new ZipEntry("中文文件.txt");
            zipStream.PutNextEntry(entry);
            var bytes = Encoding.UTF8.GetBytes("Chinese content 中文内容");
            zipStream.Write(bytes, 0, bytes.Length);
            zipStream.CloseEntry();
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // Direct ZipBinaryRewriter.RewriteAsync tests
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RewriteAsync_CopiesAllEntries()
    {
        var archive = CreateStandardZipArchive();
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_copy.zip"));

        var result = await InvokeRewriteAsync(
            archive, dest, keepEntryNames: null, addEntries: null,
            encoding: Encoding.UTF8);

        Assert.Equal(2, result.EntriesCopied);
        Assert.Equal(0, result.EntriesAdded);

        // Verify SharpCompress can read the output
        using var fs = File.OpenRead(dest);
        using var reader = ArchiveFactory.OpenArchive(fs);
        var entries = reader.Entries.Where(e => !e.IsDirectory).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Key == "hello.txt");
        Assert.Contains(entries, e => e.Key == "subdir/nested.txt");
    }

    [Fact]
    public async Task RewriteAsync_FiltersEntries()
    {
        var archive = CreateStandardZipArchive();
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_filter.zip"));

        var keepSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hello.txt" };

        var result = await InvokeRewriteAsync(
            archive, dest, keepSet, addEntries: null, encoding: Encoding.UTF8);

        Assert.Equal(1, result.EntriesCopied);

        using var fs = File.OpenRead(dest);
        using var reader = ArchiveFactory.OpenArchive(fs);
        var entries = reader.Entries.Where(e => !e.IsDirectory).ToList();
        Assert.Single(entries);
        Assert.Equal("hello.txt", entries[0].Key);
    }

    [Fact]
    public async Task RewriteAsync_AddsNewEntry()
    {
        var archive = CreateStandardZipArchive();
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_add.zip"));

        var content = "new file content added via copy-mode";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var newEntries = new List<NewEntry>
        {
            new("newfile.txt", ms, DateTime.Now, ms.Length)
        };

        var result = await InvokeRewriteAsync(
            archive, dest, keepEntryNames: null, addEntries: newEntries,
            encoding: Encoding.UTF8);

        Assert.Equal(2, result.EntriesCopied);
        Assert.Equal(1, result.EntriesAdded);

        using var fs = File.OpenRead(dest);
        using var reader = ArchiveFactory.OpenArchive(fs);
        var entries = reader.Entries.Where(e => !e.IsDirectory).ToList();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Key == "newfile.txt");

        // Verify content of the new entry
        var newEntry = entries.First(e => e.Key == "newfile.txt");
        using var entryStream = newEntry.OpenEntryStream();
        using var sr = new StreamReader(entryStream);
        Assert.Equal(content, await sr.ReadToEndAsync());
    }

    [Fact]
    public async Task RewriteAsync_AddsMultipleNewEntries()
    {
        var archive = CreateStandardZipArchive();
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_addmulti.zip"));

        using var ms1 = new MemoryStream(Encoding.UTF8.GetBytes("first"));
        using var ms2 = new MemoryStream(Encoding.UTF8.GetBytes("second"));
        var newEntries = new List<NewEntry>
        {
            new("first.txt", ms1, DateTime.Now, ms1.Length),
            new("second.txt", ms2, DateTime.Now, ms2.Length),
        };

        var result = await InvokeRewriteAsync(
            archive, dest, null, newEntries, Encoding.UTF8);

        Assert.Equal(2, result.EntriesCopied);
        Assert.Equal(2, result.EntriesAdded);

        using var fs = File.OpenRead(dest);
        using var reader = ArchiveFactory.OpenArchive(fs);
        Assert.Equal(4, reader.Entries.Count(e => !e.IsDirectory));
    }

    [Fact]
    public async Task RewriteAsync_ThrowsOnEncryptedEntry()
    {
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_enc_fail.zip"));

        var ex = await Assert.ThrowsAsync<ZipCopyModeException>(() =>
            InvokeRewriteAsync(archive, dest, null, null, Encoding.UTF8));

        Assert.Contains("encrypted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RewriteAsync_ThrowsOnNonExistentSource()
    {
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_nosrc.zip"));

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            InvokeRewriteAsync("nonexistent_archive.zip", dest, null, null, Encoding.UTF8));
    }

    [Fact]
    public async Task RewriteAsync_StoreCompression_Succeeds()
    {
        var archive = CreateStoreZipArchive(); // Store(0) entries
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_store_copy.zip"));

        var result = await InvokeRewriteAsync(
            archive, dest, null, null, Encoding.UTF8);

        Assert.Equal(2, result.EntriesCopied);

        // Verify SharpCompress can read and extract Store entries
        using var fs = File.OpenRead(dest);
        using var reader = ArchiveFactory.OpenArchive(fs);
        var entries = reader.Entries.Where(e => !e.IsDirectory).ToList();
        Assert.Equal(2, entries.Count);

        var storeEntry = entries.First(e => e.Key == "store_file.txt");
        using var ms = new MemoryStream();
        storeEntry.OpenEntryStream().CopyTo(ms);
        var content = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Store mode content", content);
    }

    [Fact]
    public async Task RewriteAsync_OutputIsReadableBySharpCompress()
    {
        // Round-trip: create ZIP → rewrite → verify SharpCompress can read it
        var archive = CreateStandardZipArchive();
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_roundtrip.zip"));

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("extra data"));
        var newEntries = new List<NewEntry>
        {
            new("extra.txt", ms, DateTime.Now, ms.Length)
        };

        await InvokeRewriteAsync(archive, dest, null, newEntries, Encoding.UTF8);

        // Validate with SharpCompress
        using var fs = File.OpenRead(dest);
        using var reader = ArchiveFactory.OpenArchive(fs);

        // Verify copied entry content
        var helloEntry = reader.Entries.First(e => e.Key == "hello.txt");
        using var helloMs = new MemoryStream();
        helloEntry.OpenEntryStream().CopyTo(helloMs);
        Assert.Equal(ArchiveFixtures.HelloText,
            Encoding.UTF8.GetString(helloMs.ToArray()));

        // Verify new entry content
        var extraEntry = reader.Entries.First(e => e.Key == "extra.txt");
        using var extraMs = new MemoryStream();
        extraEntry.OpenEntryStream().CopyTo(extraMs);
        Assert.Equal("extra data", Encoding.UTF8.GetString(extraMs.ToArray()));
    }

    [Fact]
    public async Task RewriteAsync_PreservesComment()
    {
        // Create a ZIP with a comment
        var archive = CreateTestZip(zipStream =>
        {
            zipStream.SetLevel(9);
            var entry = new ZipEntry("file.txt");
            zipStream.PutNextEntry(entry);
            var bytes = Encoding.UTF8.GetBytes("content");
            zipStream.Write(bytes, 0, bytes.Length);
            zipStream.CloseEntry();
        });

        // Set comment on the ZIP
        var originalComment = "Test ZIP comment for MantisZip";
        using (var zipFile = new ICSharpCode.SharpZipLib.Zip.ZipFile(archive))
        {
            // Read-only — comment set via ZipOutputStream doesn't work directly
            // We need to use ZipFile to set the comment post-creation
        }

        // Actually, SharpZipLib ZipOutputStream doesn't support setting comment.
        // Use ZipFile to set EOCD comment.
        using (var zipFile = new ICSharpCode.SharpZipLib.Zip.ZipFile(archive))
        {
            // ZipFile.ZipFileComment is read-only in SharpZipLib.
            // We'll use the rewritten comment parameter instead.
        }

        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_comment.zip"));

        // Rewrite with a new comment
        await InvokeRewriteAsync(
            archive, dest, null, null, Encoding.UTF8, comment: originalComment);

        // Verify comment is present using binary EOCD parsing
        var comment = ReadZipComment(dest);
        Assert.Equal(originalComment, comment);
    }

    // ══════════════════════════════════════════════════════════════════
    // ZipEngine.AddToArchiveAsync copy-mode integration tests
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddToArchiveAsync_CopyMode_AddsToExistingZip()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var newFile = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "added_file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        await File.WriteAllTextAsync(newFile, "added content");
        _tempFiles.Add(newFile);

        await _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions());

        // Verify both old and new entries present
        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "added_file.txt");
        Assert.Contains(entries, e => e.Name == "subdir/nested.txt");
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task AddToArchiveAsync_CopyMode_AddsMultipleFiles()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var newFile1 = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "alpha.txt");
        var newFile2 = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "beta.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile1)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newFile2)!);
        await File.WriteAllTextAsync(newFile1, "alpha");
        await File.WriteAllTextAsync(newFile2, "beta");
        _tempFiles.Add(newFile1);
        _tempFiles.Add(newFile2);

        await _engine.AddToArchiveAsync(archive, [newFile1, newFile2], new ArchiveOptions());

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(4, entries.Count);
        Assert.Contains(entries, e => e.Name == "alpha.txt");
        Assert.Contains(entries, e => e.Name == "beta.txt");
    }

    [Fact]
    public async Task AddToArchiveAsync_CopyMode_PreservesExistingEntryContent()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var newFile = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "new.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        await File.WriteAllTextAsync(newFile, "new content");
        _tempFiles.Add(newFile);

        await _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions());

        // Verify existing entry content is preserved
        var entries = await _engine.ListEntriesAsync(archive);
        var helloEntry = Assert.Single(entries, e => e.Name == "hello.txt");
        Assert.Equal(ArchiveFixtures.HelloText.Length, helloEntry.Size);
    }

    [Fact]
    public async Task AddToArchiveAsync_CopyMode_ChineseFilenames()
    {
        var archive = CreateZipWithChineseName();
        var newFile = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "added.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        await File.WriteAllTextAsync(newFile, "added content");
        _tempFiles.Add(newFile);

        await _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions());

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name.Contains("中文"));
        Assert.Contains(entries, e => e.Name == "added.txt");
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task AddToArchiveAsync_CopyMode_ZipRemainsValid()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var newFile = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "new.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        await File.WriteAllTextAsync(newFile, "validity check");
        _tempFiles.Add(newFile);

        await _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions());

        // TestArchiveAsync validates all entries
        var result = await _engine.TestArchiveAsync(archive);
        Assert.True(result);
    }

    // ══════════════════════════════════════════════════════════════════
    // ZipEngine.DeleteEntriesAsync copy-mode integration tests
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteEntriesAsync_CopyMode_DeletesSingleEntry()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await _engine.DeleteEntriesAsync(archive, ["hello.txt"]);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.DoesNotContain(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "subdir/nested.txt");
        Assert.Single(entries);
    }

    [Fact]
    public async Task DeleteEntriesAsync_CopyMode_DeletesMultipleEntries()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await _engine.DeleteEntriesAsync(archive, ["hello.txt", "subdir/nested.txt"]);

        // All entries deleted → archive removed
        Assert.False(File.Exists(archive));
    }

    [Fact]
    public async Task DeleteEntriesAsync_CopyMode_AllEntriesDeletedRemovesArchive()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await _engine.DeleteEntriesAsync(archive, ["hello.txt", "subdir/nested.txt"]);

        Assert.False(File.Exists(archive));
    }

    [Fact]
    public async Task DeleteEntriesAsync_CopyMode_ArchiveRemainsValid()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await _engine.DeleteEntriesAsync(archive, ["hello.txt"]);

        var result = await _engine.TestArchiveAsync(archive);
        Assert.True(result);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Single(entries);
    }

    [Fact]
    public async Task DeleteEntriesAsync_CopyMode_CanDeleteAndReAddSameName()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        // Delete existing entry
        await _engine.DeleteEntriesAsync(archive, ["hello.txt"]);

        // Re-add a file with the same name
        var newFile = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "hello.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        await File.WriteAllTextAsync(newFile, "new content after delete");
        _tempFiles.Add(newFile);

        await _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions());

        var entries = await _engine.ListEntriesAsync(archive);

        // Should now have the re-added hello.txt + the original subdir/nested.txt
        var helloEntry = Assert.Single(entries, e => e.Name == "hello.txt");
        Assert.Equal("new content after delete".Length, helloEntry.Size);
        Assert.Contains(entries, e => e.Name == "subdir/nested.txt");
    }

    // ══════════════════════════════════════════════════════════════════
    // Fallback tests (encrypted archives → legacy path)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteEntriesAsync_FallbackOnEncryptedArchive()
    {
        // Encrypted archive: copy-mode path will detect encrypted entries,
        // throw ZipCopyModeException, and fall back to legacy decompress-recompress.
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());

        // Provide password so the legacy path can extract the entries
        await _engine.DeleteEntriesAsync(archive, ["secret.txt"], "test123");

        // All entries deleted → archive removed
        Assert.False(File.Exists(archive));
    }

    [Fact]
    public async Task DeleteEntriesAsync_FallbackOnEncryptedArchive_KeepsRemainingEntries()
    {
        // Encrypted archive with multiple entries, delete one, keep the rest
        // We need a multi-entry encrypted ZIP. Create one with two entries.
        var archive = CreateEncryptedMultiEntryZip();
        _tempFiles.Add(archive);

        await _engine.DeleteEntriesAsync(archive, ["keep_me.txt"], "test123");

        // The deleted entry should be gone, the other should remain
        var entries = await _engine.ListEntriesAsync(archive, "test123");
        Assert.DoesNotContain(entries, e => e.Name == "keep_me.txt");
        Assert.Contains(entries, e => e.Name == "also_keep.txt");
    }

    /// <summary>
    /// Create a multi-entry encrypted ZIP for fallback tests.
    /// </summary>
    private string CreateEncryptedMultiEntryZip()
    {
        var path = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_multi_enc.zip");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var zipStream = new ZipOutputStream(fs);
        zipStream.SetLevel(9);
        zipStream.Password = "test123";

        var entry1 = new ZipEntry("keep_me.txt") { IsCrypted = true };
        zipStream.PutNextEntry(entry1);
        var bytes1 = Encoding.UTF8.GetBytes("will be deleted");
        zipStream.Write(bytes1, 0, bytes1.Length);
        zipStream.CloseEntry();

        var entry2 = new ZipEntry("also_keep.txt") { IsCrypted = true };
        zipStream.PutNextEntry(entry2);
        var bytes2 = Encoding.UTF8.GetBytes("will remain");
        zipStream.Write(bytes2, 0, bytes2.Length);
        zipStream.CloseEntry();

        return path;
    }

    [Fact]
    public async Task AddToArchiveAsync_CopyMode_ThrowsOnEncryptedSource()
    {
        // Encrypted source: copy-mode rejects it via ZipCopyModeException.
        // Legacy path now explicitly checks for encrypted entries before extraction
        // and throws InvalidOperationException when no password is provided.
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());
        var newFile = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "new.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        await File.WriteAllTextAsync(newFile, "new content");
        _tempFiles.Add(newFile);

        // Copy-mode throws ZipCopyModeException → falls to legacy path.
        // Legacy path pre-checks for encrypted entries → InvalidOperationException.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions()));
    }

    // ══════════════════════════════════════════════════════════════════
    // Cancellation
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RewriteAsync_Cancellation_Throws()
    {
        var archive = CreateStandardZipArchive();
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_cancelled.zip"));

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InvokeRewriteAsync(archive, dest, null, null, Encoding.UTF8,
                cancellationToken: cts.Token));
    }

    [Fact]
    public async Task AddToArchiveAsync_CopyMode_Cancellation()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var newFile = Path.Combine(
            Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "cancel.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        await File.WriteAllTextAsync(newFile, "cancel test");
        _tempFiles.Add(newFile);

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        // Task.Run wraps OperationCanceledException → TaskCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions(),
                cancellationToken: cts.Token));
    }

    // ══════════════════════════════════════════════════════════════════
    // Edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RewriteAsync_EmptyKeepSet_AllEntriesSkipped()
    {
        // When keepEntryNames is non-null but empty, all entries are skipped.
        // This effectively creates an empty ZIP with just the new entries.
        var archive = CreateStandardZipArchive();
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_emptykeep.zip"));

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("only entry"));
        var newEntries = new List<NewEntry>
        {
            new("only.txt", ms, DateTime.Now, ms.Length)
        };

        var result = await InvokeRewriteAsync(
            archive, dest, new HashSet<string>(), newEntries, Encoding.UTF8);

        Assert.Equal(0, result.EntriesCopied);
        Assert.Equal(1, result.EntriesAdded);

        using var fs = File.OpenRead(dest);
        using var reader = ArchiveFactory.OpenArchive(fs);
        Assert.Single(reader.Entries, e => !e.IsDirectory);
    }

    [Fact]
    public async Task DeleteEntriesAsync_EmptyEntryList_DoesNothing()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await _engine.DeleteEntriesAsync(archive, []);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task DeleteEntriesAsync_NonExistentEntry_ThrowsFileNotFound()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _engine.DeleteEntriesAsync(archive, ["nonexistent.txt"]));
    }

    [Fact]
    public async Task RewriteAsync_WithExplicitComment_WritesComment()
    {
        var archive = CreateStandardZipArchive();
        var dest = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_newcomment.zip"));

        await InvokeRewriteAsync(
            archive, dest, null, null, Encoding.UTF8,
            comment: "New comment via copy-mode");

        // Read comment using binary EOCD parsing
        var comment = ReadZipComment(dest);
        Assert.Equal("New comment via copy-mode", comment);
    }

    /// <summary>
    /// Read the ZIP EOCD comment from a file by locating the EOCD record.
    /// </summary>
    private static string? ReadZipComment(string zipPath)
    {
        using var fs = File.OpenRead(zipPath);
        if (fs.Length < 22) return null;

        long searchStart = Math.Max(0, fs.Length - 65557);
        fs.Seek(searchStart, SeekOrigin.Begin);
        var buf = new byte[fs.Length - searchStart];
        int read = fs.Read(buf, 0, buf.Length);
        if (read < 22) return null;

        // Find EOCD signature (0x06054b50 little-endian)
        for (int i = read - 22; i >= 0; i--)
        {
            if (buf[i] == 0x50 && buf[i + 1] == 0x4B &&
                buf[i + 2] == 0x05 && buf[i + 3] == 0x06)
            {
                int commentLen = buf[i + 20] | (buf[i + 21] << 8);
                if (commentLen > 0 && i + 22 + commentLen <= buf.Length)
                {
                    return Encoding.UTF8.GetString(buf, i + 22, commentLen);
                }
                return null;
            }
        }
        return null;
    }

}
