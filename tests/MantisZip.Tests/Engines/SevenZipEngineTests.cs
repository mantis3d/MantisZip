using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Tests.Fixtures;
using SharpSevenZip;
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

    /// <summary>Check if 7z.dll is available for SharpSevenZip compression tests.</summary>
    private static bool Is7zDllAvailable() =>
        File.Exists(SevenZipEngine.SevenZipDllPath);

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

    // ===== CompressAsync =====

    [Fact]
    public async Task CompressAsync_CreatesValidArchive()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.7z"));

        await _engine.CompressAsync([srcDir], outputPath, new ArchiveOptions { PreserveDirectoryRoot = false });

        Assert.True(File.Exists(outputPath));

        // Verify by re-extracting
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(outputPath, dest);
        Assert.True(File.Exists(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public void EnsureLibraryPath_InvalidPath_DoesNotThrow()
    {
        var originalPath = SevenZipEngine.SevenZipDllPath;
        SevenZipEngine.SevenZipDllPath = @"C:\Nonexistent\7z.dll";
        try
        {
            // Should not throw even if path is invalid — just logs a warning and falls back.
            var method = typeof(SevenZipEngine).GetMethod("EnsureLibraryPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, null);
        }
        finally
        {
            SevenZipEngine.SevenZipDllPath = originalPath;
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

    // ===== AddToArchiveAsync =====

    [Fact]
    public async Task AddToArchiveAsync_NoEntryBasePath_AddsToRoot()
    {
        if (!Is7zDllAvailable()) return;

        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var newFile = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_added.txt"));
        await File.WriteAllTextAsync(newFile, "root content");

        await _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions());

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.FullPath == Path.GetFileName(newFile));
        Assert.Contains(entries, e => e.FullPath == "hello.txt"); // 既有条目必须保留（Append 模式）
    }

    [Fact]
    public async Task AddToArchiveAsync_WithEntryBasePath_AddsToSubfolder()
    {
        if (!Is7zDllAvailable()) return;

        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var newFile = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_added.txt"));
        await File.WriteAllTextAsync(newFile, "subfolder content");

        await _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions(), entryBasePath: "docs");

        var entries = await _engine.ListEntriesAsync(archive);
        // 文件应出现在 docs/ 子目录下，而非压缩包根目录
        Assert.Contains(entries, e => e.FullPath == "docs/" + Path.GetFileName(newFile));
        Assert.DoesNotContain(entries, e => e.FullPath == Path.GetFileName(newFile));
    }

    [Fact]
    public async Task AddToArchiveAsync_WithEntryBasePath_DirectorySource_KeepsFolderStructure()
    {
        if (!Is7zDllAvailable()) return;

        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory()); // 含 hello.txt + binary.dat + subdir/nested.txt

        await _engine.AddToArchiveAsync(archive, [srcDir], new ArchiveOptions(), entryBasePath: "docs");

        var entries = await _engine.ListEntriesAsync(archive);
        var dirName = Path.GetFileName(srcDir);
        Assert.Contains(entries, e => e.FullPath == $"docs/{dirName}/hello.txt");
        Assert.Contains(entries, e => e.FullPath == $"docs/{dirName}/subdir/nested.txt");
        // 新添加的源目录不应落在根目录（无 docs/ 前缀；根目录 hello.txt 是夹具预置的旧条目）
        Assert.DoesNotContain(entries, e => e.FullPath == $"{dirName}/hello.txt");
    }

    [Fact]
    public async Task AddToArchiveAsync_DirectorySource_NoEntryBasePath_PrefixesDirName()
    {
        if (!Is7zDllAvailable()) return;

        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        // 源目录含子目录结构
        var sourceDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        var subDir = Path.Combine(sourceDir, "sub");
        Directory.CreateDirectory(subDir);
        var newFile = Path.Combine(subDir, "hello.txt");
        await File.WriteAllTextAsync(newFile, "nested content");

        await _engine.AddToArchiveAsync(archive, [sourceDir], new ArchiveOptions()); // entryBasePath = null

        var entries = await _engine.ListEntriesAsync(archive);
        // 目录源无 entryBasePath → 条目名带 {目录名}/ 前缀（与 ZipEngine 语义一致）
        Assert.Contains(entries, e => e.FullPath == $"{Path.GetFileName(sourceDir)}/sub/hello.txt");
        // 既有条目保留
        Assert.Contains(entries, e => e.FullPath == "hello.txt");
    }

    // ===== 冲突处理集成测试 =====

    private async Task<string> CreateDupFileAsync(string name, string content)
    {
        var file = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content);
        TrackFile(file);
        return file;
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Overwrite_ReplacesContent()
    {
        if (!Is7zDllAvailable()) return;
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(1, entries.Count(e => e.Name == "hello.txt"));

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal("duplicate content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Skip_KeepsOriginal()
    {
        if (!Is7zDllAvailable()) return;
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Skip });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(1, entries.Count(e => e.Name == "hello.txt"));

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Rename_AddsUniqueEntry()
    {
        if (!Is7zDllAvailable()) return;
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Rename });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "hello (1).txt");
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_ResolverCustomName()
    {
        if (!Is7zDllAvailable()) return;
        var archive = ArchiveFixtures.CreateSevenZipArchive();
        if (archive == null) return;
        TrackFile(archive);

        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = info =>
            {
                info.CustomName = "my-rename.txt";
                return Task.FromResult(FileConflictAction.Rename);
            },
        };
        await _engine.AddToArchiveAsync(archive, [dupFile], options);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "my-rename.txt");
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
