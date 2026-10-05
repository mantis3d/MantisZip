using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
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
        // Registration needed for StringCodec on code-page encoded ZIPs (used by ZipEngine.OpenZipFile)
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

    [Fact]
    public async Task ListEntriesAsync_CorruptZeroFilledFile_Throws()
    {
        // 全零填充的损坏 .zip（下载失败/复制中断产物）：必须报错，而不是静默返回空列表。
        // 回归：OpenArchiveWithEncodingFallback 曾用 ArchiveFactory.OpenArchive 魔数嗅探，
        // 全零文件被误判为 Tar（0 条目）导致损坏被吞掉。
        var corrupt = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);
        File.WriteAllBytes(corrupt, new byte[4096]); // all zeros

        await Assert.ThrowsAsync<SharpCompress.Common.ArchiveException>(() => _engine.ListEntriesAsync(corrupt));
    }

    [Fact]
    public async Task TestArchiveAsync_CorruptZeroFilledFile_ReturnsFalse()
    {
        var corrupt = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);
        File.WriteAllBytes(corrupt, new byte[4096]); // all zeros

        var ok = await _engine.TestArchiveAsync(corrupt);

        Assert.False(ok, "损坏的压缩包测试必须返回 false，不能瞬时通过");
    }

    [Fact]
    public async Task ListEntriesAsync_ValidEmptyZip_ReturnsEmpty()
    {
        // 合法空压缩包（仅 EOCD，无条目）不应被严格解析误伤。
        var empty = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(empty)!);
        // EOCD 签名 PK\x05\x06 + 18 字节其余字段 = 22 字节
        byte[] eocd = { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        File.WriteAllBytes(empty, eocd);

        var entries = await _engine.ListEntriesAsync(empty);

        Assert.Empty(entries);
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

    // ===== 冲突处理集成测试（copy-mode，默认不走加密路径） =====

    private async Task<string> CreateDupFileAsync(string name, string content)
    {
        var file = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content);
        _tempFiles.Add(file);
        return file;
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Overwrite_ReplacesContent()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive()); // hello.txt + subdir/nested.txt
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(1, entries.Count(e => e.Name == "hello.txt"));

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal("duplicate content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
        // 非冲突条目必须保留（keepEntryNames 排除逻辑的正确性验证）
        Assert.Equal(ArchiveFixtures.NestedDirFileContent, await File.ReadAllTextAsync(Path.Combine(dest, "subdir", "nested.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Skip_KeepsOriginal()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
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
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Rename });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "hello (1).txt");
        // 非冲突条目必须保留（keepEntryNames == null 分支仍走完整重写）
        Assert.Contains(entries, e => e.Name == "subdir/nested.txt");
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_ResolverSkip_KeepsOriginal()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = _ => Task.FromResult(FileConflictAction.Skip),
        };
        await _engine.AddToArchiveAsync(archive, [dupFile], options);

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_ResolverCustomName()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
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
        // 非冲突条目必须保留（keepEntryNames == null 分支仍走完整重写）
        Assert.Contains(entries, e => e.Name == "subdir/nested.txt");
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_Cancel_AbortsOperation()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = _ => throw new OperationCanceledException("用户取消"),
        };
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _engine.AddToArchiveAsync(archive, [dupFile], options));
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

    // ===== 逐条目状态遥测（EntryStatus / EntryKey） =====

    /// <summary>
    /// 同步线程安全的进度收集器：解压（含并行路径）会从多个工作线程直接 Report，
    /// 不能使用 System.Progress&lt;T&gt;（它经 SynchronizationContext/线程池异步派发，
    /// 测试在 await 返回时尚未收到回调，导致报告时序性丢失）。
    /// </summary>
    private sealed class ProgressCollector : IProgress<ArchiveProgress>
    {
        private readonly ConcurrentBag<ArchiveProgress> _items = new();
        public void Report(ArchiveProgress value) => _items.Add(value);
        /// <summary>当前收集到的报告快照（await 引擎完成后再读取，无时序依赖）。</summary>
        public List<ArchiveProgress> Items => _items.ToList();
    }

    /// <summary>
    /// (a) 全新空目录解压 5 文件压缩包：每个非目录条目恰好上报一次 Completed 终态，
    /// EntryKey 两两不同且集合等于压缩包内条目键集合（无丢失/无重复/无截断）。
    /// </summary>
    [Fact]
    public async Task ExtractAsync_ReportsPerEntryCompleted_OncePerFile()
    {
        var archive = TrackFile(ArchiveFixtures.CreateMultiFileZipArchive(5)); // file0000.dat ~ file0004.dat
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        var collector = new ProgressCollector();

        await _engine.ExtractAsync(archive, dest, progress: collector);

        var entryReports = collector.Items.Where(p => p.EntryStatus.HasValue).ToList();
        // 5 个非目录条目 → 恰好 5 条逐条目报告（不多不少）
        Assert.Equal(5, entryReports.Count);
        // EntryKey 两两不同
        var reportedKeys = entryReports.Select(p => ArchivePath.Normalize(p.EntryKey)).ToList();
        Assert.Equal(5, reportedKeys.Distinct(StringComparer.Ordinal).Count());
        // 键集合 == 压缩包内非目录条目键集合（键以 ListEntriesAsync 的 FullPath 为准）
        var archiveKeys = (await _engine.ListEntriesAsync(archive))
            .Where(e => !e.IsDirectory)
            .Select(e => ArchivePath.Normalize(e.FullPath))
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(archiveKeys.SetEquals(reportedKeys),
            $"EntryKey 集合不匹配! archive=[{string.Join(", ", archiveKeys.OrderBy(k => k, StringComparer.Ordinal))}] " +
            $"reported=[{string.Join(", ", reportedKeys.OrderBy(k => k, StringComparer.Ordinal))}]");
        // 无冲突 → 全部 Completed（不得出现 Skipped/Failed/Overwritten）
        Assert.All(entryReports, p => Assert.Equal(ArchiveEntryStatus.Completed, p.EntryStatus));
    }

    /// <summary>
    /// (b) Skip 冲突策略：被跳过的条目必须上报 Skipped 终态（出现在收集结果中，而非被静默丢弃），
    /// 且同一键不得同时上报 Completed/Overwritten（终态互斥）。
    /// </summary>
    [Fact]
    public async Task ExtractAsync_SkippedEntry_ReportsSkippedStatus()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive()); // hello.txt + subdir/nested.txt
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "hello.txt"), "old content"); // 制造同名冲突

        var collector = new ProgressCollector();
        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Skip };
        await _engine.ExtractAsync(archive, dest, options: options, progress: collector);

        var entryReports = collector.Items.Where(p => p.EntryStatus.HasValue).ToList();
        // 冲突被跳过的 hello.txt 必须以 Skipped 状态出现在逐条目报告中
        Assert.Contains(entryReports, p =>
            ArchivePath.Normalize(p.EntryKey) == "hello.txt" &&
            p.EntryStatus == ArchiveEntryStatus.Skipped);
        // 无冲突的另一条目正常完成
        Assert.Contains(entryReports, p =>
            ArchivePath.Normalize(p.EntryKey) == "subdir/nested.txt" &&
            p.EntryStatus == ArchiveEntryStatus.Completed);
        // Skipped 与 Completed/Overwritten 互斥：跳过的条目绝不能同时上报写入终态
        Assert.DoesNotContain(entryReports, p =>
            ArchivePath.Normalize(p.EntryKey) == "hello.txt" &&
            p.EntryStatus is ArchiveEntryStatus.Completed or ArchiveEntryStatus.Overwritten);
    }

    /// <summary>
    /// (d) EntryKey 必须是完整压缩包内相对路径，不做 Path.GetFileName 截断：
    /// a/x.txt 与 b/x.txt 同名不同目录，必须产生两个不同的 EntryKey。
    /// </summary>
    [Fact]
    public async Task ExtractAsync_ReportsEntryKeyWithoutPathTruncation()
    {
        // 构造含 a/x.txt + b/x.txt 的 ZIP（两个同名文件位于不同目录）
        var archive = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        using (var fs = File.Create(archive))
        using (var zip = new ZipOutputStream(fs))
        {
            zip.SetLevel(1);
            foreach (var name in new[] { "a/x.txt", "b/x.txt" })
            {
                zip.PutNextEntry(new ZipEntry(name));
                var bytes = Encoding.UTF8.GetBytes($"content of {name}");
                zip.Write(bytes, 0, bytes.Length);
                zip.CloseEntry();
            }
        }

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        var collector = new ProgressCollector();

        await _engine.ExtractAsync(archive, dest, progress: collector);

        var reportedKeys = collector.Items
            .Where(p => p.EntryStatus.HasValue)
            .Select(p => ArchivePath.Normalize(p.EntryKey))
            .ToList();
        Assert.Equal(2, reportedKeys.Count);
        // 两个 EntryKey 必须不同（若被 Path.GetFileName 截断，两者都会变成 "x.txt" 而相等）
        Assert.Equal(2, reportedKeys.Distinct(StringComparer.Ordinal).Count());
        // 且必须携带目录前缀 = 完整相对路径
        Assert.True(new HashSet<string>(reportedKeys, StringComparer.Ordinal).SetEquals(new[] { "a/x.txt", "b/x.txt" }),
            $"EntryKey 应为完整相对路径而非文件名，实际: [{string.Join(", ", reportedKeys)}]");
    }

    // ===== MultiThreadedCompression（ZIP 外壳 + 7z mt=on 压缩组）=====

    /// <summary>7z.dll 是否可用（MultiThreaded 压缩依赖 SharpSevenZip）。</summary>
    private static bool Is7zDllAvailable() =>
        File.Exists(SevenZipEngine.SevenZipDllPath);

    /// <summary>
    /// 创建混合源目录：可压缩文本 + 已压缩二进制 + 子目录嵌套。
    /// 用于验证 MultiThreadedCompression + AdaptiveCompression 分流（StoreGroup / CompressGroup）。
    /// </summary>
    private static string CreateMixedSourceDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        // 可压缩文本 → CompressGroup（7z mt=on）
        File.WriteAllText(Path.Combine(dir, "hello.txt"), ArchiveFixtures.HelloText);

        // 已压缩 JPEG（伪内容，仅扩展名触发分类）→ StoreGroup
        var jpegBytes = new byte[4096];
        new Random(42).NextBytes(jpegBytes);
        File.WriteAllBytes(Path.Combine(dir, "photo.jpg"), jpegBytes);

        // 子目录嵌套文本 → CompressGroup
        var subDir = Path.Combine(dir, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), ArchiveFixtures.NestedDirFileContent);

        return dir;
    }

    /// <summary>压缩 → 解压 → 逐字节比对 + 完整性校验（MultiThreaded 测试共用验证链）。</summary>
    /// <param name="rootPrefixInEntries">
    /// true = 条目带 {源目录名}/ 根前缀（FileScanner 约定，MT 非加密路径）；
    /// false = 条目为平铺相对路径（加密路径：7z 收绝对路径数组自动剥离最长公共前缀）。
    /// </param>
    private async Task AssertRoundTripAsync(string srcDir, string outputPath, ArchiveOptions options, string? password = null, bool rootPrefixInEntries = true)
    {
        await _engine.CompressAsync([srcDir], outputPath, options);
        Assert.True(File.Exists(outputPath), "compressed archive should exist");

        // 产物条目路径必须与 FileScanner 收集的相对路径完全一致
        // （FileScanner 无条件以源目录名为根前缀 —— 7-Zip/WinRAR 同款约定；
        //  此断言验证 MT 模式的临时目录/7z 前缀不得泄漏进最终 ZIP 条目）
        var scan = MantisZip.Core.Utils.FileScanner.CollectFiles([srcDir], null, CancellationToken.None);
        var expectedKeys = scan.Files
            .Select(f => rootPrefixInEntries
                ? f.RelativePath.Replace('\\', '/')
                : Path.GetRelativePath(srcDir, f.FullPath).Replace('\\', '/'))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 目录条目（如加密路径 7z 写的 subdir 占位）不计入文件键比对
        var actualKeys = (await _engine.ListEntriesAsync(outputPath))
            .Where(e => !e.IsDirectory)
            .Select(e => e.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expectedKeys.Where(k => !actualKeys.Contains(k)).ToArray();
        var extra = actualKeys.Where(k => !expectedKeys.Contains(k)).ToArray();
        Assert.True(missing.Length == 0 && extra.Length == 0,
            $"Entry key mismatch! missing=[{string.Join(", ", missing)}] extra=[{string.Join(", ", extra)}]");

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        if (password != null)
            await _engine.ExtractAsync(outputPath, dest, password);
        else
            await _engine.ExtractAsync(outputPath, dest);

        // 解压结构：rootPrefixInEntries=true → 条目带 {源目录名}/ 根前缀 → 落到 dest/{源目录名}/相对路径
        // rootPrefixInEntries=false → 条目平铺 → 落到 dest/相对路径
        var srcFiles = Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        var dstFiles = Directory.GetFiles(dest, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        Assert.Equal(srcFiles.Count, dstFiles.Count);

        var rootName = Path.GetFileName(srcDir);
        foreach (var srcFile in srcFiles)
        {
            var relativePath = Path.GetRelativePath(srcDir, srcFile);
            var dstFile = rootPrefixInEntries
                ? Path.Combine(dest, rootName, relativePath)
                : Path.Combine(dest, relativePath);
            Assert.True(File.Exists(dstFile), $"Missing file: {(rootPrefixInEntries ? Path.Combine(rootName, relativePath) : relativePath)}");
            Assert.Equal(await File.ReadAllBytesAsync(srcFile), await File.ReadAllBytesAsync(dstFile));
        }

        var valid = password != null
            ? await _engine.TestArchiveAsync(outputPath, password)
            : await _engine.TestArchiveAsync(outputPath);
        Assert.True(valid, "integrity check failed");
    }

    /// <summary>
    /// B1：MultiThreadedCompression + AdaptiveCompression 混合源端到端。
    /// 验证 StoreGroup（已压缩直写）与 CompressGroup（7z mt=on）分流产物合法、逐字节可逆。
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreadedAdaptive_MixedSource_RoundTrips()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(CreateMixedSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_adaptive.zip"));

        await AssertRoundTripAsync(srcDir, outputPath, new ArchiveOptions
        {
            MultiThreadedCompression = true,
            AdaptiveCompression = true,
        });
    }

    /// <summary>
    /// C1：仅 MultiThreadedCompression（AdaptiveCompression=false）→ 全部文件进 CompressGroup。
    /// 验证 7z mt=on 压缩组不依赖 Adaptive 也能共存于 Deflate ZIP。
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreadedOnly_AdaptiveOff_RoundTrips()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(CreateMixedSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_only.zip"));

        await AssertRoundTripAsync(srcDir, outputPath, new ArchiveOptions
        {
            MultiThreadedCompression = true,
            AdaptiveCompression = false, // 仅多线程：全部文件走 CompressGroup
        });
    }

    /// <summary>
    /// C2：MultiThreadedCompression + Encrypt=true → 跳过 MT 分流（仅 Deflate/非加密触发）。
    /// 验证加密走标准 SharpSevenZip 加密路径，产物可加密解压。
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreadedWithEncrypt_DevidesToEncryptedPath()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(CreateMixedSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_enc.zip"));

        await AssertRoundTripAsync(srcDir, outputPath, new ArchiveOptions
        {
            MultiThreadedCompression = true,
            Encrypt = true,
            Password = "test123",
        }, password: "test123", rootPrefixInEntries: false);
    }

    /// <summary>
    /// C3：MultiThreadedCompression + MultiThreadedStoreFormatIds 自定义格式 → 该扩展名走 StoreGroup。
    /// 验证用户自定义仅存储格式在 MT 模式下生效（Wav 不在内置已压缩列表，仅经自定义列表命中）。
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreaded_CustomStoreFormatIds_StoredUncompressed()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(CreateMixedSourceDirectory());
        // 追加一个 .wav（不在内置已压缩列表），指定自定义 Store 格式 ID 使其走 StoreGroup。
        // 用大体积可压缩数据：若被错误压缩（bug 路径 Deflate），体积会显著缩小，断言可强区分。
        File.WriteAllText(Path.Combine(srcDir, "sound.wav"), new string('A', 256 * 1024));
        // hello.txt 对照组也换大体积可压缩数据，使「确实被压缩」断言可靠（13 字节小文本无区分度）
        File.WriteAllText(Path.Combine(srcDir, "hello.txt"), new string('B', 64 * 1024));
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_custom.zip"));

        await AssertRoundTripAsync(srcDir, outputPath, new ArchiveOptions
        {
            MultiThreadedCompression = true,
            AdaptiveCompression = true,
            MultiThreadedStoreFormatIds = new HashSet<string> { "Wav" },
        });

        // 验证 Store 语义 = Deflate stored-block（SharpCompress level-0）：压缩后 ≈ 原始大小。
        // 注意：level-0 后方法字段仍是 Deflated，不能断言 CompressionMethod == Stored，
        // 应断言体积未被显著压缩（参见 AdaptiveCompressionFeasibilityTests 探针 2 结论）。
        // 条目名带 {源目录名}/ 根前缀（FileScanner 约定），故按 EndsWith 匹配
        using (var zipFile = new ICSharpCode.SharpZipLib.Zip.ZipFile(outputPath))
        {
            var wavEntry = zipFile.Cast<ZipEntry>().First(e => e.Name.EndsWith("/sound.wav"));
            // 自定义 Store 格式生效：256KB 可压缩数据未被压缩（stored-block ≈ 原始大小）
            Assert.True(wavEntry.CompressedSize >= wavEntry.Size,
                $"sound.wav 应仅存储未压缩；Size={wavEntry.Size} CompressedSize={wavEntry.CompressedSize}");
            // photo.jpg 走内置已压缩分类 → 仅存储（随机字节 Deflate 不缩小，≈ 原始大小）
            var jpgEntry = zipFile.Cast<ZipEntry>().First(e => e.Name.EndsWith("/photo.jpg"));
            Assert.True(jpgEntry.CompressedSize >= jpgEntry.Size,
                $"photo.jpg 应仅存储未压缩；Size={jpgEntry.Size} CompressedSize={jpgEntry.CompressedSize}");
            // hello.txt 对照组应被真实压缩（Deflate 显著缩小）
            var txtEntry = zipFile.Cast<ZipEntry>().First(e => e.Name.EndsWith("/hello.txt"));
            Assert.True(txtEntry.CompressedSize < txtEntry.Size * 0.5,
                $"hello.txt 应被压缩；Size={txtEntry.Size} CompressedSize={txtEntry.CompressedSize}");
        }
    }

    /// <summary>
    /// D1：MultiThreaded 压缩进度回归（针对 09-23 修复）。
    /// 验证多文件 mt=on 下 FileCompressionStarted 事件触发进度报告：非空、最终 100%、无异常。
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreaded_ReportsProgress()
    {
        if (!Is7zDllAvailable()) return;

        // 多文件低压缩率数据（随机字节），确保 mt=on 多线程实际分拣到多个文件
        var srcDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(srcDir);
        var rng = new Random(12345);
        for (int i = 0; i < 20; i++)
        {
            var data = new byte[128 * 1024]; // 128KB each ≈ 2.5MB total
            rng.NextBytes(data);
            await File.WriteAllBytesAsync(Path.Combine(srcDir, $"file{i:D3}.bin"), data);
        }

        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_progress.zip"));
        var progressItems = new List<ArchiveProgress>();

        await _engine.CompressAsync([srcDir], outputPath, new ArchiveOptions
        {
            MultiThreadedCompression = true,
        }, progress: new Progress<ArchiveProgress>(p => progressItems.Add(p)));

        Assert.True(File.Exists(outputPath));
        Assert.NotEmpty(progressItems);
        // 最终到达 100%
        var last = progressItems.Last();
        Assert.Equal(100, last.PercentComplete);
        // 至少有一次中间进度（非初始即终态）
        Assert.Contains(progressItems, p => p.PercentComplete > 0 && p.PercentComplete < 100);
        // 无异常则自然走到这里（异常会由 xUnit 捕获）
    }

    // ===== MT 压缩字节保真（回归：merge 阶段不得解压重压）=====

    /// <summary>
    /// 创建纯可压缩语料（仅 .txt），确保 storeGroup 为空、全部条目走 CompressGroup（7z mt=on）。
    /// 条目内容足够大且高度可压缩，保证 Deflate 能显著缩小（保证断言有区分度）。
    /// </summary>
    private static string CreateCompressibleOnlyDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        for (int i = 0; i < 5; i++)
        {
            var sb = new StringBuilder();
            for (int j = 0; j < 3000; j++)
                sb.Append("MantisZip deflate sample line ").Append(j).Append(" lorem ipsum dolor sit amet consectetur\n");
            File.WriteAllText(Path.Combine(dir, $"doc{i}.txt"), sb.ToString());
        }
        return dir;
    }

    /// <summary>读取 ZIP 内每个文件条目的 CompressedSize（键为条目名，分隔符已归一化为 '/'）。</summary>
    private static Dictionary<string, long> ReadEntryCompressedSizes(string zipPath)
    {
        using var zipFile = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipPath);
        return zipFile.Cast<ZipEntry>()
            .Where(e => !e.IsDirectory)
            .ToDictionary(e => e.Name.Replace('\\', '/'), e => e.CompressedSize, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 回归：MT 路径必须保留 7z 的压缩字节。
    /// 修复前 <c>MergeTempZipToWriter</c> 走 <c>OpenEntryStream()</c>（解压）→
    /// <c>ZipWriter.WriteToStream()</c>（重压），7z mt=on 的成果 100% 被丢弃，
    /// 且 <c>CompressionLevel = null</c> 让最终压缩级别与用户设置脱钩。
    /// 本测试断言 compressible 条目的 CompressedSize 与 7z 原生产物逐条目完全一致。
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreaded_PreservesSevenZipCompressedBytes()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(CreateCompressibleOnlyDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_bytes.zip"));
        var refZip = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_ref.zip"));

        var options = new ArchiveOptions
        {
            MultiThreadedCompression = true,
            AdaptiveCompression = true,
            ZipCompressionMethod = "deflate",
            CompressionLevel = 5,
        };

        // 参考产物：MT 路径内部对 compressGroup 执行的同一次 7z 原生压缩
        var (files, _) = FileScanner.CollectFiles([srcDir], null, CancellationToken.None, null);
        var compressGroup = files
            .Where(f => ZipEntryClassifier.GetAdaptiveLevel(f.FullPath, options.CompressionLevel,
                          options.AdaptiveCompression, options.MultiThreadedStoreFormatIds) != 0)
            .ToList();
        Assert.NotEmpty(compressGroup);

        var dummyReportTime = DateTime.MinValue;
        ZipEngine.CompressGroupWithSevenZip(compressGroup, refZip, options, null, 0, 0, 0, 0, null, null, ref dummyReportTime);

        await _engine.CompressAsync([srcDir], outputPath, options);

        var refSizes = ReadEntryCompressedSizes(refZip);
        var actualSizes = ReadEntryCompressedSizes(outputPath);

        Assert.Equal(refSizes.Count, actualSizes.Count);
        foreach (var (key, expected) in refSizes)
        {
            Assert.True(actualSizes.TryGetValue(key, out var actual), $"MT 产物缺少条目 {key}");
            // 修复前：merge 把 7z 的字节解压后用 .NET Deflate 重压 → 与参考值不一致
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// 读取 ZIP 中央目录，返回 条目名 → (压缩方法, 压缩大小, 原始大小)。
    /// 用内部二进制解析器而非 SharpCompress —— SharpCompress 不暴露 CompressionMethod，
    /// 而「真 Store（method 0）」正是本测试要区分的语义。
    /// </summary>
    private static Dictionary<string, (ushort Method, long CompressedSize, long Size)> ReadEntryMethods(string zipPath)
    {
        using var fs = File.OpenRead(zipPath);
        var (cdOffset, entryCount, _) = ZipBinaryRewriter.ReadEocd(fs);
        return ZipBinaryRewriter.ReadCentralDirectory(fs, cdOffset, entryCount)
            .Where(e => !e.FileName.EndsWith('/'))
            .ToDictionary(
                e => e.FileName.Replace('\\', '/'),
                e => (e.CompressionMethod, e.CompressedSize, e.UncompressedSize),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 回归：MT 混合语料 —— compressGroup 必须保留 7z 压缩字节，storeGroup 必须以 method 0 直存。
    /// 覆盖 <c>RewriteAsync(tempZip 为源)</c> + <c>NewEntry.Store=true</c> 这条新路径
    /// （纯可压缩语料只走 File.Move，不经过本测试覆盖的分支）。
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreaded_MixedSource_StoresRawAndPreservesSevenZipBytes()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(CreateMixedSourceDirectory());
        // 放大可压缩文本，使「确实被压缩」与「method=0 直存」具备明确区分度
        File.WriteAllText(Path.Combine(srcDir, "hello.txt"), new string('B', 64 * 1024));

        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_mixed_bytes.zip"));
        var refZip = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_mixed_ref.zip"));

        var options = new ArchiveOptions
        {
            MultiThreadedCompression = true,
            AdaptiveCompression = true,
            ZipCompressionMethod = "deflate",
            CompressionLevel = 5,
        };

        // 参考产物：MT 路径内部对 compressGroup 执行的同一次 7z 原生压缩
        var (files, _) = FileScanner.CollectFiles([srcDir], null, CancellationToken.None, null);
        var compressGroup = files
            .Where(f => ZipEntryClassifier.GetAdaptiveLevel(f.FullPath, options.CompressionLevel,
                          options.AdaptiveCompression, options.MultiThreadedStoreFormatIds) != 0)
            .ToList();
        Assert.NotEmpty(compressGroup);

        var dummyReportTime = DateTime.MinValue;
        ZipEngine.CompressGroupWithSevenZip(compressGroup, refZip, options, null, 0, 0, 0, 0, null, null, ref dummyReportTime);

        // 压缩 + 解压逐字节比对 + 完整性校验（共用验证链）
        await AssertRoundTripAsync(srcDir, outputPath, options);

        var refMethods = ReadEntryMethods(refZip);
        var actual = ReadEntryMethods(outputPath);

        // ① compressGroup：逐条目压缩字节与压缩方法均与 7z 原生完全一致（copy-mode 保真）。
        //    注意不能写死 method == 8：7z 对小块/不可压缩数据会自动选 Copy(method 0)，
        //    保真契约是「与参考逐字段相同」，而非「一定是 Deflate」。
        Assert.NotEmpty(refMethods);
        foreach (var (key, expected) in refMethods)
        {
            Assert.True(actual.TryGetValue(key, out var got), $"MT 产物缺少条目 {key}");
            Assert.Equal(expected.CompressedSize, got.CompressedSize);
            Assert.Equal(expected.Method, got.Method);
        }

        // ② storeGroup：真 Store（method 0），压缩大小 == 原始大小，字节未被加工
        var jpgMatches = actual.Where(kv => kv.Key.EndsWith("photo.jpg")).ToList();
        Assert.Single(jpgMatches);
        var jpg = jpgMatches[0];
        Assert.Equal((ushort)0, jpg.Value.Method);
        Assert.Equal(jpg.Value.Size, jpg.Value.CompressedSize);
    }

    /// <summary>
    /// 回归：混合场景（StoreGroup 非空）选中 <b>Deflate64</b> 时，必须走 copy-mode 重写且
    /// <b>方法码保持 9</b>。
    /// <para>
    /// 根因：SharpCompress 的 ZipWriter 根本无法写 Deflate64（<c>ToZipCompressionMethod</c> 无该映射，
    /// 抛 <c>InvalidFormatException: Invalid compression method: Deflate64</c>）。因此 Deflate64 一旦落入
    /// 混合场景并回落到串行路径，就必然硬失败——copy-mode 是它唯一可用的产出路径。
    /// </para>
    /// <para>
    /// 断言双重：① 可压缩条目 method == 9（未被压平成 Store）；② 与 7z 参考包的 method + 压缩后大小逐条相等
    /// （copy-mode 原样复制压缩字节，不重压）。
    /// </para>
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreaded_MixedSource_Deflate64_PreservesMethod9()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(CreateMixedSourceDirectory());
        // 大段高压缩文本，确保 7z 对 Deflate64 产出 method 9 而非退化为 Store
        File.WriteAllText(Path.Combine(srcDir, "hello.txt"), new string('B', 64 * 1024));
        File.WriteAllText(Path.Combine(srcDir, "subdir", "nested.txt"), new string('C', 64 * 1024));

        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_mixed_d64.zip"));
        var refZip = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_mixed_d64_ref.zip"));

        var options = new ArchiveOptions
        {
            MultiThreadedCompression = true,
            AdaptiveCompression = true,
            ZipCompressionMethod = "deflate64",
            CompressionLevel = 5,
        };

        // 参考压缩包：MT 混合流程中对同一 compressGroup 执行的同一份 7z 原始压缩
        var (files, _) = FileScanner.CollectFiles([srcDir], null, CancellationToken.None, null);
        var compressGroup = files
            .Where(f => ZipEntryClassifier.GetAdaptiveLevel(f.FullPath, options.CompressionLevel,
                options.AdaptiveCompression, options.MultiThreadedStoreFormatIds) != 0)
            .ToList();
        Assert.NotEmpty(compressGroup);

        var dummyReportTime = DateTime.MinValue;
        ZipEngine.CompressGroupWithSevenZip(compressGroup, refZip, options, null, 0, 0, 0, 0, null, null, ref dummyReportTime);

        // 压缩 + 解压逐字节比对（同时覆盖 7z 与重写器两条路径）
        await AssertRoundTripAsync(srcDir, outputPath, options);

        var refMethods = ReadEntryMethods(refZip);
        var actual = ReadEntryMethods(outputPath);

        // 对 compressGroup：method 与压缩后大小与 7z 参考包完全一致
        Assert.NotEmpty(refMethods);
        foreach (var (key, expected) in refMethods)
        {
            Assert.True(actual.TryGetValue(key, out var got), $"MT 输出缺少条目 {key}");
            Assert.Equal(expected.CompressedSize, got.CompressedSize);
            Assert.Equal(expected.Method, got.Method);
        }

        // 核心回归断言：文本条目在重写后仍是 Deflate64（method 9），绝不能被压平成 Store（method 0）
        var textMatches = actual
            .Where(kv => kv.Key.EndsWith("hello.txt") || kv.Key.EndsWith("nested.txt"))
            .ToList();
        Assert.Equal(2, textMatches.Count);
        foreach (var (_, value) in textMatches)
            Assert.Equal((ushort)9, value.Method);

        // 对 storeGroup：仍为原样 Store（method 0），未经压缩
        var jpgMatches = actual.Where(kv => kv.Key.EndsWith("photo.jpg")).ToList();
        Assert.Single(jpgMatches);
        var jpgEntry = jpgMatches[0];
        Assert.Equal((ushort)0, jpgEntry.Value.Method);
        Assert.Equal(jpgEntry.Value.Size, jpgEntry.Value.CompressedSize);
    }

    /// <summary>
    /// 回归：<c>AddToArchiveAsync</c>（添加到已有压缩包）的 MT 路径同样必须保留 7z 压缩字节。
    /// 该方法先把已有压缩包整体解压到 tempDir、再连同新文件一起从零重压缩，
    /// 因此修复前同样走 MergeTempZipToWriter（解压 → .NET Deflate 重压），mt=on 成果被丢弃。
    /// 本测试用「按原压缩包内容重建 tempDir 镜像」的方式生成 7z 参考产物，避免硬编码 fixture 内部结构。
    /// </summary>
    [Fact]
    public async Task AddToArchiveAsync_MultiThreaded_PreservesSevenZipCompressedBytes()
    {
        if (!Is7zDllAvailable()) return;

        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        // 新增：一个可压缩文本（进 CompressGroup）+ 一个随机二进制（进 StoreGroup）
        var addDir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(addDir);
        TrackDir(addDir);
        var addedText = Path.Combine(addDir, "added-big.txt");
        await File.WriteAllTextAsync(addedText, new string('A', 96 * 1024));
        var addedJpg = Path.Combine(addDir, "photo.jpg");
        await File.WriteAllBytesAsync(addedJpg, RandomNumberGenerator.GetBytes(48 * 1024));

        var options = new ArchiveOptions
        {
            MultiThreadedCompression = true,
            AdaptiveCompression = true,
            ZipCompressionMethod = "deflate",
            CompressionLevel = 5,
        };

        await _engine.AddToArchiveAsync(archive, [addedText, addedJpg], options);

        // ── 构造 7z 参考产物：镜像 AddToArchiveAsync 的 tempDir 内容 ──
        var refDir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(refDir);
        TrackDir(refDir);
        using (var zf = new ICSharpCode.SharpZipLib.Zip.ZipFile(archive))
        {
            // 注意：此处 archive 已被 AddToArchiveAsync 覆写，故从调用前的副本读取更稳妥；
            // 但 AddToArchiveAsync 保留原有条目内容，因此读改后的 archive 仍能得到同样的文件集合。
            foreach (var e in zf.Cast<ZipEntry>())
            {
                if (e.IsDirectory) continue;
                var outPath = Path.Combine(refDir, e.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                using var input = zf.GetInputStream(e);
                using var output = File.Create(outPath);
                input.CopyTo(output);
            }
        }

        // 追加新增文件（AddToArchiveAsync entryBasePath=null → 条目名为文件名）
        File.Copy(addedText, Path.Combine(refDir, "added-big.txt"), overwrite: true);
        File.Copy(addedJpg, Path.Combine(refDir, "photo.jpg"), overwrite: true);

        // 生产代码用 Path.GetRelativePath(tempDir, file) 构造 RelativePath，此处完全对齐
        var refFiles = Directory.GetFiles(refDir, "*", SearchOption.AllDirectories)
            .Select(f => (FullPath: f, RelativePath: ArchivePath.Normalize(Path.GetRelativePath(refDir, f))))
            .ToList();

        var refZip = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_add_ref.zip"));
        var dummyReportTime = DateTime.MinValue;
        ZipEngine.CompressGroupWithSevenZip(refFiles, refZip, options, null, 0, 0, 0, 0, null, null, ref dummyReportTime);

        // ── 断言 ──
        var refMethods = ReadEntryMethods(refZip);
        var actual = ReadEntryMethods(archive);

        // ① CompressGroup：逐条目压缩字节与压缩方法均与 7z 原生一致
        Assert.NotEmpty(refMethods);
        foreach (var (key, expected) in refMethods)
        {
            Assert.True(actual.TryGetValue(key, out var got), $"AddToArchiveAsync 产物缺少条目 {key}");
            Assert.Equal(expected.CompressedSize, got.CompressedSize);
            Assert.Equal(expected.Method, got.Method);
        }

        // ② StoreGroup：真 Store（method 0）
        var jpgMatches = actual.Where(kv => kv.Key.EndsWith("photo.jpg")).ToList();
        Assert.Single(jpgMatches);
        var jpg = jpgMatches[0];
        Assert.Equal((ushort)0, jpg.Value.Method);
        Assert.Equal(jpg.Value.Size, jpg.Value.CompressedSize);

        // ③ 产物完整性：可正常列出且原有条目未丢失
        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name.EndsWith("hello.txt"));
        Assert.Contains(entries, e => e.Name == "added-big.txt");
        Assert.Contains(entries, e => e.Name == "photo.jpg");
    }

    // ===== MultiThreaded 准入守卫（IsMultiThreadedEligible / CanCopyModeRewrite） =====

    /// <summary>
    /// 全可压缩 + Deflate64：StoreGroup 为空 → 走 <c>File.Move</c>，根本不经过 copy-mode 重写，
    /// 因此必须仍然走 7z mt=on。
    /// <para>
    /// 本用例锁定「方法判定必须发生在分组之后」这一次序：若在前置预检里对 deflate64 一刀切拒绝，
    /// 该组合会被错误踢回串行路径（而串行 ZipWriter 恰恰不支持 Deflate64，直接抛
    /// InvalidFormatException），造成真实回归。
    /// </para>
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreaded_Deflate64_AllCompressible_StaysOnSevenZipPath()
    {
        if (!Is7zDllAvailable()) return;

        // 纯可压缩源：StoreGroup 必为空
        var srcDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "a.txt"), new string('A', 64 * 1024));
        await File.WriteAllTextAsync(Path.Combine(srcDir, "b.txt"), new string('B', 64 * 1024));

        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_d64.zip"));

        await AssertRoundTripAsync(srcDir, outputPath, new ArchiveOptions
        {
            MultiThreadedCompression = true,
            AdaptiveCompression = true,
            ZipCompressionMethod = "deflate64",
            CompressionLevel = 5,
        });
    }

    /// <summary>
    /// AddToArchiveAsync 侧的同序次保证：全可压缩 + Deflate64 仍走 7z，不被守卫误伤。
    /// </summary>
    [Fact]
    public async Task AddToArchiveAsync_MultiThreaded_Deflate64_AllCompressible_StaysOnSevenZipPath()
    {
        if (!Is7zDllAvailable()) return;

        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        // 只加可压缩文件 → 原有条目 + 新增条目全进 CompressGroup，StoreGroup 为空
        var addedText = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.txt"));
        await File.WriteAllTextAsync(addedText, new string('B', 64 * 1024));

        await _engine.AddToArchiveAsync(archive, [addedText], new ArchiveOptions
        {
            MultiThreadedCompression = true,
            AdaptiveCompression = true,
            ZipCompressionMethod = "deflate64",
            CompressionLevel = 5,
        });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == Path.GetFileName(addedText));
        Assert.Contains(entries, e => e.Name.EndsWith("hello.txt"));
    }

    /// <summary>
    /// 分卷（SplitSize &gt; 0）由 SplitOutputStream 负责，MT 路径不参与。
    /// 验证守卫把 MT 挡在门外、分卷链路仍正常工作（产物按 {name}{ext}.{NNN} 命名）。
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreaded_SplitSize_ProducesSplitVolumes()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(CreateMixedSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt_split.zip"));

        await _engine.CompressAsync([srcDir], outputPath, new ArchiveOptions
        {
            MultiThreadedCompression = true,
            AdaptiveCompression = true,
            SplitSize = 1024, // 1KB 分卷 → 混合源必然产生多个卷
        });

        // 分卷产物命名为 {name}{ext}.{NNN}，不会生成 base 文件本身
        Assert.False(File.Exists(outputPath), "split archive should not produce a file at the base path");
        var dir = Path.GetDirectoryName(outputPath)!;
        var volumes = Directory.GetFiles(dir, Path.GetFileName(outputPath) + ".*");
        Assert.True(volumes.Length >= 2,
            $"expected multiple split volumes under MT-disabled serial path, got {volumes.Length}");
    }

    /// <summary>
    /// ZIP32 上限守卫（copy-mode 不支持 ZIP64）。
    /// 条目数与 4GB 总量都无法用小文件真实构造（需 65280 个文件 / 4GB 数据），
    /// 因此直接对私有准入函数做边界断言，避免把重型 IO 塞进单元测试。
    /// </summary>
    [Theory]
    [InlineData(10_000, 1L * 1024 * 1024 * 1024, true)]
    // 条目数恰好等于 ZIP32 上限（ushort.MaxValue - 256 = 65279）→ 准入（边界闭合）
    [InlineData(65_279, 1024, true)]
    // 条目数越过 ZIP32 上限 → 不准入
    [InlineData(65_280, 1024, false)]
    [InlineData(70_000, 1024, false)]
    // 总量贴近 4GB 上限 → 准入（边界闭合）
    [InlineData(10, 4_294_967_293L, true)]
    // 总量到达 ZIP32 安全上限（0xFFFFFFFE）→ 保守拒绝。
    // copy-mode 遇到 0xFFFFFFFF 哨兵会抛 ZipCopyModeException 且无内部回落，
    // 守卫宁紧勿松：差一个字节最多回落串行，放宽则可能直接压缩失败。
    [InlineData(10, 4_294_967_294L, false)]
    [InlineData(10, 4_294_967_295L, false)]
    [InlineData(10, 5L * 1024 * 1024 * 1024, false)]
    public void IsMultiThreadedEligible_Zip32Limits_GuardBoundaries(int entryCount, long totalSize, bool expected)
    {
        var method = typeof(ZipEngine).GetMethod(
            "IsMultiThreadedEligible",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var eligible = (bool)method!.Invoke(null, [
            new ArchiveOptions { MultiThreadedCompression = true, AdaptiveCompression = true },
            entryCount,
            totalSize,
        ])!;

        Assert.Equal(expected, eligible);
    }

    /// <summary>
    /// copy-mode 重写器承载 method 0（Store）、method 8（Deflate）与 method 9（Deflate64）。
    /// <para>
    /// Deflate64 必须放行：SharpCompress 的 ZipWriter 无法写 Deflate64，混合场景回落串行必然硬失败。
    /// BZip2/LZMA/PPMd 仍拒绝（回落串行可正常写出，只是失去 MT 加速）。
    /// </para>
    /// </summary>
    [Fact]
    public void CanCopyModeRewrite_AcceptsStoreDeflateAndDeflate64()
    {
        var method = typeof(ZipEngine).GetMethod(
            "CanCopyModeRewrite",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        bool Can(string? zipMethod) => (bool)method!.Invoke(null, [zipMethod])!;

        Assert.True(Can(null));          // 未指定 → 默认 Deflate
        Assert.True(Can(""));            // 空串同上
        Assert.True(Can("deflate"));     // Deflate (method 8)
        Assert.True(Can("DEFLATE"));     // 大小写不敏感
        Assert.True(Can("store"));       // Store (method 0) —— 显式全 Store 可被重写器承载
        Assert.True(Can("deflate64"));   // method 9 —— 串行路径无法写 Deflate64，只能靠 copy-mode

        Assert.False(Can("bzip2"));      // method 12 —— 回落串行可写出，仅失去 MT 加速
        Assert.False(Can("lzma"));       // method 14
        Assert.False(Can("ppmd"));       // method 98
    }

    /// <summary>
    /// 加密包由串行 ZipWriter 负责 AES，MT 前置预检必须直接拒绝。
    /// </summary>
    [Fact]
    public void IsMultiThreadedEligible_RejectsEncryptedArchive()
    {
        var method = typeof(ZipEngine).GetMethod(
            "IsMultiThreadedEligible",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var options = new ArchiveOptions
        {
            MultiThreadedCompression = true,
            Encrypt = true,
            Password = "secret",
        };

        var eligible = (bool)method!.Invoke(null, [options, 10, 1024L])!;
        Assert.False(eligible);
    }

    /// <summary>
    /// MultiThreadedCompression=false 时前置预检必须拒绝（用户未开启多线程）。
    /// </summary>
    [Fact]
    public void IsMultiThreadedEligible_RejectsWhenSwitchOff()
    {
        var method = typeof(ZipEngine).GetMethod(
            "IsMultiThreadedEligible",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var options = new ArchiveOptions { MultiThreadedCompression = false };
        var eligible = (bool)method!.Invoke(null, [options, 10, 1024L])!;

        Assert.False(eligible);
    }

    /// <summary>
    /// 分卷（SplitSize &gt; 0）由 SplitOutputStream 负责，MT 路径不参与，前置预检必须拒绝。
    /// 端到端行为由 <c>CompressAsync_MultiThreaded_SplitSize_ProducesSplitVolumes</c> 覆盖。
    /// </summary>
    [Fact]
    public void IsMultiThreadedEligible_RejectsSplitArchive()
    {
        var method = typeof(ZipEngine).GetMethod(
            "IsMultiThreadedEligible",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var options = new ArchiveOptions
        {
            MultiThreadedCompression = true,
            SplitSize = 1024 * 1024,
        };

        var eligible = (bool)method!.Invoke(null, [options, 10, 1024L])!;

        Assert.False(eligible);
    }

    /// <summary>
    /// 回归：非 Deflate 压缩方法在**默认级别 5** 下曾必然崩溃。
    /// SharpCompress 在 <c>ZipWriterOptions.CompressionLevel</c> 的 setter 内即校验
    /// （<c>CompressionLevelValidation.Validate</c>）：只有 Deflate / Deflate64 接受可配置级别，
    /// BZip2 / LZMA / PPMd / None 传入非 0 直接抛 <c>ArgumentOutOfRangeException</c>。
    /// 修复：这些方法统一传 0（0 = 该方法的默认设置，不是「不压缩」）。
    /// <para>
    /// 走串行路径：<c>writerOptions</c> 在 MT 判定**之前**构造，修复前 MT 用户同样崩溃，
    /// 故此处显式 <c>MultiThreadedCompression = false</c> 以直接命中修复点。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("bzip2", (ushort)12)] // ZIP 方法码 12
    [InlineData("lzma", (ushort)14)]  // ZIP 方法码 14
    [InlineData("ppmd", (ushort)98)]  // ZIP 方法码 98
    [InlineData("store", (ushort)0)]  // ZIP 方法码 0
    public async Task CompressAsync_NonDeflateMethod_AtDefaultLevel_WritesExpectedMethodCode(
        string zipMethod, ushort expectedMethod)
    {
        var srcDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(srcDir);
        // 足量可压缩文本：使「确实被压缩」与「直存」具备明确区分度
        await File.WriteAllTextAsync(Path.Combine(srcDir, "a.txt"), new string('A', 64 * 1024));
        await File.WriteAllTextAsync(Path.Combine(srcDir, "b.txt"), new string('B', 64 * 1024));

        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_{zipMethod}.zip"));

        var options = new ArchiveOptions
        {
            MultiThreadedCompression = false, // 串行：直接覆盖 writerOptions 构造处的修复
            AdaptiveCompression = false,
            ZipCompressionMethod = zipMethod,
            CompressionLevel = 5, // 默认级别 —— 修复前正是在此崩溃
        };

        // 修复前：ArgumentOutOfRangeException:
        //   Compression type BZip2 does not support configurable compression levels. Use 0.
        await _engine.CompressAsync([srcDir], outputPath, options);

        // 方法码必须与用户选择一致（而非静默降级为 Deflate / Store）
        var entries = ReadEntryMethods(outputPath);
        Assert.NotEmpty(entries);
        foreach (var (name, info) in entries)
        {
            Assert.Equal(expectedMethod, info.Method);
        }

        // 解压逐字节比对 + 完整性校验
        await AssertRoundTripAsync(srcDir, outputPath, options);
    }

}
