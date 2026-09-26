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
}
