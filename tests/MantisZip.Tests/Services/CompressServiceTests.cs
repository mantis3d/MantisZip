using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
using MantisZip.Tests.Fixtures;
using Xunit;

namespace MantisZip.Tests.Services;

public class CompressServiceTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public CompressServiceTests()
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

    private class TestProgress : IProgress<ArchiveProgress>
    {
        public List<ArchiveProgress> Reports { get; } = new();
        public void Report(ArchiveProgress value) => Reports.Add(value);
    }

    /// <summary>
    /// 读取 ZIP 文件的注释文本。
    /// </summary>
    private static string ReadZipComment(string zipPath)
    {
        using var zf = new ZipFile(zipPath);
        return zf.ZipFileComment ?? string.Empty;
    }

    /// <summary>
    /// 列出 ZIP 文件中的条目名。
    /// </summary>
    private static HashSet<string> ListZipEntries(string zipPath)
    {
        var entries = new HashSet<string>();
        using var zf = new ZipFile(zipPath);
        foreach (ZipEntry entry in zf)
        {
            if (!entry.IsDirectory)
                entries.Add(entry.Name);
        }
        return entries;
    }

    // ==================================================================
    // Separate Mode Tests
    // ==================================================================

    [Fact]
    public async Task BasicCompress_NoConflict_Succeeds()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var progress = new TestProgress();

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
        };

        var result = await CompressService.CompressAsync(request, null, progress, CancellationToken.None);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);

        // Separate mode: output = parent/{dirName}.zip
        var outputPath = Path.Combine(
            Path.GetDirectoryName(srcDir)!,
            Path.GetFileName(srcDir) + ".zip");
        TrackFile(outputPath);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task Conflict_Overwrite()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var progress = new TestProgress();

        // Pre-create the output file at the computed path
        var outputPath = Path.Combine(
            Path.GetDirectoryName(srcDir)!,
            Path.GetFileName(srcDir) + ".zip");
        TrackFile(outputPath);
        await File.WriteAllTextAsync(outputPath, "pre-existing junk");

        int resolverCalls = 0;
        CompressConflictResolver resolver = info =>
        {
            resolverCalls++;
            return new CompressConflictResolution(CompressConflictAction.Overwrite, null);
        };

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
        };

        var result = await CompressService.CompressAsync(request, resolver, progress, CancellationToken.None);

        Assert.Equal(1, resolverCalls);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
        Assert.True(File.Exists(outputPath));

        // Should now be a valid zip, not junk
        var entries = ListZipEntries(outputPath);
        Assert.NotEmpty(entries);
    }

    [Fact]
    public async Task Conflict_Cancel()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var progress = new TestProgress();

        var outputPath = Path.Combine(
            Path.GetDirectoryName(srcDir)!,
            Path.GetFileName(srcDir) + ".zip");
        TrackFile(outputPath);
        await File.WriteAllTextAsync(outputPath, "preserved content");

        CompressConflictResolver resolver = _ =>
            new CompressConflictResolution(CompressConflictAction.Cancel, null);

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
        };

        var result = await CompressService.CompressAsync(request, resolver, progress, CancellationToken.None);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        // File content unchanged
        Assert.Equal("preserved content", await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task Conflict_Rename()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var progress = new TestProgress();

        var outputPath = Path.Combine(
            Path.GetDirectoryName(srcDir)!,
            Path.GetFileName(srcDir) + ".zip");
        TrackFile(outputPath);
        await File.WriteAllTextAsync(outputPath, "original");

        CompressConflictResolver resolver = _ =>
            new CompressConflictResolution(CompressConflictAction.Rename, null);

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
        };

        var result = await CompressService.CompressAsync(request, resolver, progress, CancellationToken.None);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
        // Original file unchanged
        Assert.Equal("original", await File.ReadAllTextAsync(outputPath));
        // Renamed file: "{name} (1).zip"
        var dir = Path.GetDirectoryName(srcDir)!;
        var name = Path.GetFileName(srcDir);
        var renamedPath = Path.Combine(dir, $"{name} (1).zip");
        TrackFile(renamedPath);
        Assert.True(File.Exists(renamedPath));
    }

    [Fact]
    public async Task Conflict_Rename_CustomName()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var progress = new TestProgress();

        var outputPath = Path.Combine(
            Path.GetDirectoryName(srcDir)!,
            Path.GetFileName(srcDir) + ".zip");
        TrackFile(outputPath);
        await File.WriteAllTextAsync(outputPath, "original");

        var customName = "custom-output.zip";
        CompressConflictResolver resolver = _ =>
            new CompressConflictResolution(CompressConflictAction.Rename, customName);

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
        };

        var result = await CompressService.CompressAsync(request, resolver, progress, CancellationToken.None);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
        // Custom-named file
        var customPath = Path.Combine(Path.GetDirectoryName(srcDir)!, customName);
        TrackFile(customPath);
        Assert.True(File.Exists(customPath));
    }

    [Fact]
    public async Task Conflict_Add()
    {
        var progress = new TestProgress();

        // Create a temp directory containing a source file and a pre-existing zip
        var tempDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(tempDir);

        // Source file: source.txt (named so that output name matches: source.zip)
        var sourceFile = TrackFile(Path.Combine(tempDir, "source.txt"));
        await File.WriteAllTextAsync(sourceFile, "new file content");

        // Pre-create a ZIP at the computed output path: tempDir\source.zip
        var existingZip = Path.Combine(tempDir, "source.zip");
        TrackFile(existingZip);
        using (var fs = File.Create(existingZip))
        using (var zos = new ZipOutputStream(fs))
        {
            var entry = new ZipEntry("pre_existing.txt");
            zos.PutNextEntry(entry);
            var bytes = Encoding.UTF8.GetBytes("pre-existing content");
            zos.Write(bytes, 0, bytes.Length);
            zos.CloseEntry();
        }

        CompressConflictResolver resolver = _ =>
            new CompressConflictResolution(CompressConflictAction.Add, null);

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { sourceFile },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
        };

        var result = await CompressService.CompressAsync(request, resolver, progress, CancellationToken.None);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);

        // Verify the existing archive now contains entries from the source
        var entries = ListZipEntries(existingZip);
        Assert.Contains(entries, e => e == "pre_existing.txt" || e.Contains("pre_existing"));
        Assert.Contains(entries, e => e == "source.txt" || e.Contains("source"));
    }

    [Fact]
    public async Task InvalidSource_Skipped()
    {
        var progress = new TestProgress();

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { @"X:\nonexistent_file_xyz.txt" },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
        };

        var result = await CompressService.CompressAsync(request, null, progress, CancellationToken.None);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task KeepOriginalExtension_True()
    {
        var progress = new TestProgress();
        var tempDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(tempDir);
        var sourceFile = TrackFile(Path.Combine(tempDir, "hello.txt"));
        await File.WriteAllTextAsync(sourceFile, "test");

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { sourceFile },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
            KeepOriginalExtension = true,
        };

        var result = await CompressService.CompressAsync(request, null, progress, CancellationToken.None);

        Assert.Equal(1, result.Succeeded);
        var outputPath = Path.Combine(tempDir, "hello.txt.zip");
        TrackFile(outputPath);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task KeepOriginalExtension_False()
    {
        var progress = new TestProgress();
        var tempDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(tempDir);
        var sourceFile = TrackFile(Path.Combine(tempDir, "hello.txt"));
        await File.WriteAllTextAsync(sourceFile, "test");

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { sourceFile },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
            KeepOriginalExtension = false,
        };

        var result = await CompressService.CompressAsync(request, null, progress, CancellationToken.None);

        Assert.Equal(1, result.Succeeded);
        var outputPath = Path.Combine(tempDir, "hello.zip");
        TrackFile(outputPath);
        Assert.True(File.Exists(outputPath));
    }

    // ==================================================================
    // Single Mode Tests (Manual / Combined)
    // ==================================================================

    [Fact]
    public async Task SingleMode_BasicCompress()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var progress = new TestProgress();
        var outputPath = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip"));

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir },
            Mode = CompressOutputMode.Manual,
            Format = "zip",
            OutputPath = outputPath,
        };

        var result = await CompressService.CompressAsync(request, null, progress, CancellationToken.None);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task SingleMode_MissingOutputPath_Throws()
    {
        var progress = new TestProgress();

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { "dummy.txt" },
            Mode = CompressOutputMode.Manual,
            // OutputPath deliberately not set
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CompressService.CompressAsync(request, null, progress, CancellationToken.None));
    }

    [Fact]
    public async Task SingleMode_Conflict_Overwrite()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var progress = new TestProgress();
        var outputPath = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip"));

        // Pre-create the output file
        await File.WriteAllTextAsync(outputPath, "junk");

        int resolverCalls = 0;
        CompressConflictResolver resolver = info =>
        {
            resolverCalls++;
            return new CompressConflictResolution(CompressConflictAction.Overwrite, null);
        };

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir },
            Mode = CompressOutputMode.Manual,
            Format = "zip",
            OutputPath = outputPath,
        };

        var result = await CompressService.CompressAsync(request, resolver, progress, CancellationToken.None);

        Assert.Equal(1, resolverCalls);
        Assert.Equal(1, result.Succeeded);
        Assert.True(File.Exists(outputPath));
    }

    // ==================================================================
    // Comment Distribution Tests
    // ==================================================================

    [Fact]
    public async Task Comment_AllSame_Separate()
    {
        var progress = new TestProgress();
        var srcDir1 = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var srcDir2 = TrackDir(ArchiveFixtures.CreateSourceDirectory());

        const string comment = "shared comment";

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir1, srcDir2 },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
            Comment = comment,
            CommentDistribution = CommentDistribution.AllSame,
        };

        var result = await CompressService.CompressAsync(request, null, progress, CancellationToken.None);

        Assert.Equal(2, result.Succeeded);

        // Both archives should have the comment
        var output1 = Path.Combine(Path.GetDirectoryName(srcDir1)!, Path.GetFileName(srcDir1) + ".zip");
        var output2 = Path.Combine(Path.GetDirectoryName(srcDir2)!, Path.GetFileName(srcDir2) + ".zip");
        TrackFile(output1);
        TrackFile(output2);

        Assert.Equal(comment, ReadZipComment(output1));
        Assert.Equal(comment, ReadZipComment(output2));
    }

    [Fact]
    public async Task Comment_FirstOnly()
    {
        var progress = new TestProgress();
        var srcDir1 = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var srcDir2 = TrackDir(ArchiveFixtures.CreateSourceDirectory());

        const string comment = "only first";

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir1, srcDir2 },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
            Comment = comment,
            CommentDistribution = CommentDistribution.FirstOnly,
        };

        var result = await CompressService.CompressAsync(request, null, progress, CancellationToken.None);

        Assert.Equal(2, result.Succeeded);

        var output1 = Path.Combine(Path.GetDirectoryName(srcDir1)!, Path.GetFileName(srcDir1) + ".zip");
        var output2 = Path.Combine(Path.GetDirectoryName(srcDir2)!, Path.GetFileName(srcDir2) + ".zip");
        TrackFile(output1);
        TrackFile(output2);

        Assert.Equal(comment, ReadZipComment(output1));
        Assert.Equal(string.Empty, ReadZipComment(output2));
    }

    [Fact]
    public async Task Comment_PerLine()
    {
        var progress = new TestProgress();
        var srcDir1 = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var srcDir2 = TrackDir(ArchiveFixtures.CreateSourceDirectory());

        const string commentText = "line1\nline2\nline3";

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir1, srcDir2 },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
            Comment = commentText,
            CommentDistribution = CommentDistribution.PerLine,
        };

        var result = await CompressService.CompressAsync(request, null, progress, CancellationToken.None);

        Assert.Equal(2, result.Succeeded);

        var output1 = Path.Combine(Path.GetDirectoryName(srcDir1)!, Path.GetFileName(srcDir1) + ".zip");
        var output2 = Path.Combine(Path.GetDirectoryName(srcDir2)!, Path.GetFileName(srcDir2) + ".zip");
        TrackFile(output1);
        TrackFile(output2);

        Assert.Equal("line1", ReadZipComment(output1));
        Assert.Equal("line2", ReadZipComment(output2));
    }

    // ==================================================================
    // Progress Reporting Test
    // ==================================================================

    [Fact]
    public async Task Progress_ReportsDuringCompress()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var progress = new TestProgress();

        var request = new CompressRequest
        {
            SourcePaths = new List<string> { srcDir },
            Mode = CompressOutputMode.Separate,
            Format = "zip",
        };

        var result = await CompressService.CompressAsync(request, null, progress, CancellationToken.None);

        Assert.Equal(1, result.Succeeded);

        // Progress should have been reported at least once
        Assert.NotEmpty(progress.Reports);

        // Final progress should show 100%
        var lastReport = progress.Reports[^1];
        Assert.Equal(100, lastReport.PercentComplete);
    }
}
