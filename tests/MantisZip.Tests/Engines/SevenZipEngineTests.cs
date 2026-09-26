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

    // ===== Multi-threaded Compression (mt=on) =====

    /// <summary>
    /// 验证 SharpSevenZip mt=on 多线程压缩生成的 7z 归档可正常解压和验证。
    /// 测试内容：压缩 → 解压 → 逐字节比对 → TestArchiveAsync 完整性校验。
    /// </summary>
    [Fact]
    public async Task CompressAsync_MultiThreaded_CreatesValidArchive()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_mt.7z"));

        // 多文件多级目录压缩（利用 mt=on）
        var options = new ArchiveOptions
        {
            PreserveDirectoryRoot = false,
            SevenZipMultithreaded = true,
        };

        await _engine.CompressAsync([srcDir], outputPath, options);

        Assert.True(File.Exists(outputPath));

        // 解压并逐字节比对
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(outputPath, dest);

        var srcFiles = Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        var dstFiles = Directory.GetFiles(dest, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        Assert.Equal(srcFiles.Count, dstFiles.Count);

        for (int i = 0; i < srcFiles.Count; i++)
        {
            var relativePath = Path.GetRelativePath(srcDir, srcFiles[i]);
            var dstFile = Path.Combine(dest, relativePath);
            Assert.True(File.Exists(dstFile), $"Missing file: {relativePath}");
            Assert.Equal(await File.ReadAllBytesAsync(srcFiles[i]), await File.ReadAllBytesAsync(dstFile));
        }

        // 7z.dll 完整性校验
        var valid = await _engine.TestArchiveAsync(outputPath);
        Assert.True(valid, "7z.dll integrity check failed for multi-threaded archive");
    }

    /// <summary>
    /// 对比：无 mt=on 压缩的 7z 归档也能正常解压（基线对照）。
    /// </summary>
    [Fact]
    public async Task CompressAsync_SingleThreaded_CreatesValidArchive()
    {
        if (!Is7zDllAvailable()) return;

        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_st.7z"));

        var options = new ArchiveOptions
        {
            PreserveDirectoryRoot = false,
            SevenZipMultithreaded = false, // 单线程
        };

        await _engine.CompressAsync([srcDir], outputPath, options);

        Assert.True(File.Exists(outputPath));

        // 解压并比对
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(outputPath, dest);
        Assert.True(File.Exists(Path.Combine(dest, "hello.txt")));
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));

        var valid = await _engine.TestArchiveAsync(outputPath);
        Assert.True(valid, "7z.dll integrity check failed for single-threaded archive");
    }

    /// <summary>
    /// 基准测试：单线程 vs 多线程压缩耗时对比（需手动取消 Skip 运行）。
    /// </summary>
    [Fact(Skip = "性能基准测试，环境依赖强，需手动运行验证")]
    public async Task Benchmark_CompressMTVsST()
    {
        if (!Is7zDllAvailable()) return;

        // 创建 100 × 1MB 测试数据
        var srcDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipBench", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(srcDir);
        var rng = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            var data = new byte[1024 * 1024]; // 1MB
            rng.NextBytes(data);
            await File.WriteAllBytesAsync(Path.Combine(srcDir, $"file_{i:D3}.bin"), data);
        }

        var stPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipBench", $"{Guid.NewGuid()}_st.7z"));
        var mtPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipBench", $"{Guid.NewGuid()}_mt.7z"));

        // 单线程
        var stSw = System.Diagnostics.Stopwatch.StartNew();
        await _engine.CompressAsync([srcDir], stPath, new ArchiveOptions
        {
            PreserveDirectoryRoot = false,
            SevenZipMultithreaded = false,
        });
        stSw.Stop();

        // 多线程
        var mtSw = System.Diagnostics.Stopwatch.StartNew();
        await _engine.CompressAsync([srcDir], mtPath, new ArchiveOptions
        {
            PreserveDirectoryRoot = false,
            SevenZipMultithreaded = true,
        });
        mtSw.Stop();

        Console.WriteLine($"=== 7z Compression Benchmark (100×1MB) ===");
        Console.WriteLine($"  Single-thread: {stSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Multi-thread:  {mtSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Speedup: {(double)stSw.ElapsedMilliseconds / mtSw.ElapsedMilliseconds:F2}x");
    }

    // ===== 探针：7z.dll 的 ZIP 输出是否支持 mt 并行 =====

    /// <summary>
    /// 探针测试：用 SharpSevenZip 的 OutArchiveFormat.Zip + Deflate 输出 ZIP，
    /// 对比 mt=off / mt=on 的墙钟时间与 CPU 时间，判断 7z.dll 的 ZIP 编码器是否并行。
    /// 判据：CPU时间/墙钟时间 ≈ 1 → 单线程；> 1 → 已并行（约等于并发核数）。
    /// 场景 A（100×1MB 多文件）探测「文件级并行」，场景 B（1×100MB 单文件）探测「文件内并行」。
    /// </summary>
    [Fact(Skip = "性能探针，环境依赖强，需手动运行验证；结论：多文件 6.75-7.29x，单文件无收益")]
    public async Task Probe_SevenZip_Zip_MultiThread()
    {
        if (!Is7zDllAvailable()) return;

        static byte[] MakeCompressible(int size)
        {
            var pattern = System.Text.Encoding.UTF8.GetBytes(
                "MantisZip zip mt probe data 0123456789 abcdefghijklmnopqrstuvwxyz ");
            var buf = new byte[size];
            for (int i = 0; i < size; i++) buf[i] = pattern[i % pattern.Length];
            return buf;
        }

        var root = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipZipMtProbe", Guid.NewGuid().ToString("N")));

        // 场景 A：100 × 1MB 多文件（可探测跨文件并行）
        var manyDir = Path.Combine(root, "many");
        Directory.CreateDirectory(manyDir);
        for (int i = 0; i < 100; i++)
            await File.WriteAllBytesAsync(Path.Combine(manyDir, $"f{i:D3}.bin"), MakeCompressible(1024 * 1024));

        // 场景 B：1 × 100MB 单文件（可探测文件内并行）
        var singleDir = Path.Combine(root, "single");
        Directory.CreateDirectory(singleDir);
        await File.WriteAllBytesAsync(Path.Combine(singleDir, "big.bin"), MakeCompressible(100 * 1024 * 1024));

        var proc = System.Diagnostics.Process.GetCurrentProcess();

        (long Wall, double Cpu, long Size) Run(string dir, bool mt, string tag)
        {
            var outPath = Path.Combine(root, $"{tag}.zip");
            if (File.Exists(outPath)) File.Delete(outPath);

            var c = new SharpSevenZipCompressor
            {
                ArchiveFormat = OutArchiveFormat.Zip,
                CompressionMethod = CompressionMethod.Deflate,
                CompressionLevel = CompressionLevel.Normal,
                IncludeEmptyDirectories = true,
                DirectoryStructure = true,
            };
            c.CustomParameters["mt"] = mt ? "on" : "off";

            var cpu0 = proc.TotalProcessorTime;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            c.CompressDirectory(dir, outPath);
            sw.Stop();
            var cpu1 = proc.TotalProcessorTime;

            return (sw.ElapsedMilliseconds, (cpu1 - cpu0).TotalMilliseconds, new FileInfo(outPath).Length);
        }

        var aOff = Run(manyDir, false, "a_off");
        var aOn = Run(manyDir, true, "a_on");
        var bOff = Run(singleDir, false, "b_off");
        var bOn = Run(singleDir, true, "b_on");

        Console.WriteLine("=== PROBE: 7z.dll ZIP + mt (Deflate Normal) ===");
        Console.WriteLine($"A 100x1MB  off: wall={aOff.Wall}ms cpu={aOff.Cpu:F0}ms size={aOff.Size}");
        Console.WriteLine($"A 100x1MB  on : wall={aOn.Wall}ms cpu={aOn.Cpu:F0}ms size={aOn.Size}");
        Console.WriteLine($"A          speedup={(double)aOff.Wall / aOn.Wall:F2}x  parallelism(cpu/wall)={aOn.Cpu / aOn.Wall:F2}");
        Console.WriteLine($"B 1x100MB  off: wall={bOff.Wall}ms cpu={bOff.Cpu:F0}ms size={bOff.Size}");
        Console.WriteLine($"B 1x100MB  on : wall={bOn.Wall}ms cpu={bOn.Cpu:F0}ms size={bOn.Size}");
        Console.WriteLine($"B          speedup={(double)bOff.Wall / bOn.Wall:F2}x  parallelism(cpu/wall)={bOn.Cpu / bOn.Wall:F2}");

        // 正确性校验：mt=on 产物必须可正常解压（7z.dll 解压时校验 CRC）且文件数一致
        var verifyDir = Path.Combine(root, "verify");
        Directory.CreateDirectory(verifyDir);
        using (var ex = new SharpSevenZipExtractor(Path.Combine(root, "a_on.zip")))
        {
            ex.ExtractArchive(verifyDir);
        }
        var srcCount = Directory.GetFiles(manyDir).Length;
        var outCount = Directory.GetFiles(verifyDir, "*", SearchOption.AllDirectories).Length;
        Console.WriteLine($"VERIFY     src={srcCount} extracted={outCount} match={srcCount == outCount}");
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
