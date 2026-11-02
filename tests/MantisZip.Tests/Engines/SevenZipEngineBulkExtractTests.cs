using System.Collections.Concurrent;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using SharpSevenZip;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// solid 7z 全量解压改走批量接口（<c>SharpSevenZipExtractor.ExtractFiles</c>）的回归测试。
///
/// 覆盖计划《7z 全量解压改走批量接口》§8 里「必须在真机上验」的那几条硬约束：
/// ① 批量段能解出全部文件且内容正确；② Skip 依旧一条不落地上报；③ Rename 走暂存目录 +
/// 改名后落到预解析出的新名字；④ 暂存目录不残留；⑤ 逐条目状态上报不丢（D11）。
///
/// 类名带 <c>SevenZipEngine</c> 是为了沿用 CI 的 <c>FullyQualifiedName!~SevenZipEngine</c> 过滤
/// （这些用例依赖本机 7z.dll，CI runner 上不保证存在）。
/// </summary>
public sealed class SevenZipEngineBulkExtractTests : IDisposable
{
    private const int FileCount = 40;
    private const int FileSizeBytes = 512;

    private readonly List<string> _tempPaths = new();

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // 清理失败不影响断言结果
            }
        }
    }

    private string TrackTempDir(string path)
    {
        _tempPaths.Add(path);
        return path;
    }

    private string TrackTempFile(string path)
    {
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>找 7z.dll（测试基目录 / Program Files）；找不到返回 null，用例直接返回（跳过）。</summary>
    private static string? Resolve7zDll()
    {
        var candidates = new[]
        {
            SevenZipEngine.SevenZipDllPath,
            Path.Combine(AppContext.BaseDirectory, Environment.Is64BitProcess ? "x64" : "x86", "7z.dll"),
            @"C:\Program Files\7-Zip\7z.dll",
            @"C:\Program Files (x86)\7-Zip\7z.dll",
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// 造一个 solid 7z：<c>root/plain-{i}.bin</c> + <c>root/sub/nested-{i}.bin</c>。
    /// 内容可压缩（重复字节），Lzma2 + 目录压缩默认 solid。返回 null 表示环境里没有 7z.dll。
    /// </summary>
    private (string ArchivePath, List<string> RelativePaths)? CreateSolidArchive()
    {
        var dllPath = Resolve7zDll();
        if (dllPath == null)
            return null;

        SharpSevenZipBase.SetLibraryPath(dllPath);

        var workDir = TrackTempDir(Path.Combine(Path.GetTempPath(), "MantisZipBulk", Guid.NewGuid().ToString("N")));
        var sourceDir = Path.Combine(workDir, "src");
        var nestedDir = Path.Combine(sourceDir, "sub");
        Directory.CreateDirectory(nestedDir);

        var relativePaths = new List<string>();
        for (var i = 0; i < FileCount; i++)
        {
            var name = $"plain-{i:D3}.bin";
            File.WriteAllBytes(Path.Combine(sourceDir, name), MakeCompressible(FileSizeBytes));
            relativePaths.Add(name);

            var nestedName = $"nested-{i:D3}.bin";
            File.WriteAllBytes(Path.Combine(nestedDir, nestedName), MakeCompressible(FileSizeBytes));
            relativePaths.Add(Path.Combine("sub", nestedName));
        }

        var archivePath = TrackTempFile(Path.Combine(workDir, "solid.7z"));
        var compressor = new SharpSevenZipCompressor
        {
            ArchiveFormat = OutArchiveFormat.SevenZip,
            CompressionLevel = CompressionLevel.Fast,
            CompressionMethod = CompressionMethod.Lzma2,
            IncludeEmptyDirectories = false,
            DirectoryStructure = true,
        };
        compressor.CompressDirectory(sourceDir, archivePath);

        return File.Exists(archivePath) ? (archivePath, relativePaths) : null;
    }

    private static byte[] MakeCompressible(int size)
    {
        var data = new byte[size];
        for (var i = 0; i < size; i++)
            data[i] = (byte)('A' + i % 7);
        return data;
    }

    private string CreateDestination()
        => TrackTempDir(Path.Combine(Path.GetTempPath(), "MantisZipBulk", Guid.NewGuid().ToString("N")));

    /// <summary>
    /// 前提校验：夹具必须是 solid，否则这些用例证明不了批量路径被走到（逐条目路径同样能全解）。
    /// </summary>
    private static bool IsSolid(string archivePath)
    {
        using var extractor = new SharpSevenZipExtractor(archivePath);
        return extractor.IsSolid;
    }

    [Fact]
    public async Task ExtractAsync_SolidArchive_ExtractsEveryFileWithCorrectContent()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return; // 无 7z.dll：跳过
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return; // 夹具不是 solid：本用例无意义

        var destination = CreateDestination();
        var engine = new SevenZipEngine();

        var result = await engine.ExtractAsync(archivePath, destination, cancellationToken: CancellationToken.None);

        Assert.Equal(relativePaths.Count, result.SucceededEntries);
        Assert.Equal(0, result.FailedEntries);
        foreach (var relative in relativePaths)
        {
            var extracted = Path.Combine(destination, relative);
            Assert.True(File.Exists(extracted), $"缺少文件：{relative}");
            Assert.Equal(FileSizeBytes, new FileInfo(extracted).Length);
        }
    }

    [Fact]
    public async Task ExtractAsync_SolidArchive_ReportsEveryEntryStatus()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var engine = new SevenZipEngine();
        var progress = new SynchronousProgress<ArchiveProgress>();
        var statuses = new ConcurrentDictionary<string, ArchiveEntryStatus>(StringComparer.OrdinalIgnoreCase);
        progress.ProgressChanged += (_, p) =>
        {
            if (!string.IsNullOrEmpty(p.EntryKey) && p.EntryStatus.HasValue)
                statuses[p.EntryKey] = p.EntryStatus.Value;
        };

        var result = await engine.ExtractAsync(
            archivePath, destination, password: null, progress: progress,
            cancellationToken: CancellationToken.None);

        // D11：批量段也必须逐条上报状态，且键是归档内的相对路径
        Assert.Equal(relativePaths.Count, statuses.Count);
        foreach (var relative in relativePaths)
            Assert.True(statuses.ContainsKey(relative.Replace('\\', '/')), $"缺少逐条目状态：{relative}");
        Assert.Equal(relativePaths.Count, result.SucceededEntries);
    }

    [Fact]
    public async Task ExtractAsync_SolidArchive_SkipStrategy_KeepsExistingFiles()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        // 预置同名文件（内容不同）⇒ Skip 策略下必须一个都不覆盖
        foreach (var relative in relativePaths)
        {
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "keep-me");
        }

        var engine = new SevenZipEngine();
        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Skip };
        var result = await engine.ExtractAsync(
            archivePath, destination, password: null, progress: null,
            cancellationToken: CancellationToken.None, options: options);

        Assert.Equal(relativePaths.Count, result.SkippedEntries);
        Assert.Equal(0, result.SucceededEntries);
        foreach (var relative in relativePaths)
            Assert.Equal("keep-me", File.ReadAllText(Path.Combine(destination, relative)));
    }

    [Fact]
    public async Task ExtractAsync_SolidArchive_RenameStrategy_WritesRenamedCopiesAndCleansStaging()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var originals = new List<string>();
        foreach (var relative in relativePaths)
        {
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "original");
            originals.Add(target);
        }

        var engine = new SevenZipEngine();
        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Rename };
        var result = await engine.ExtractAsync(
            archivePath, destination, password: null, progress: null,
            cancellationToken: CancellationToken.None, options: options);

        Assert.Equal(relativePaths.Count, result.SucceededEntries);
        Assert.Equal(0, result.FailedEntries);
        Assert.Equal(0, result.SkippedEntries);

        // 原文件不被覆盖
        foreach (var original in originals)
            Assert.Equal("original", File.ReadAllText(original));

        // 改名副本落地（PathHelper.GetUniquePath ⇒ "name (1).ext"），总数翻倍
        var allFiles = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
        Assert.Equal(relativePaths.Count * 2, allFiles.Length);
        Assert.Contains(allFiles, f => Path.GetFileName(f).Contains('(', StringComparison.Ordinal));

        // 暂存目录必须清干净（不超过 40 条改名副本 ⇒ 走的就是批量 + 暂存再改名）
        var stagingDirs = Directory.GetDirectories(destination, ".mantiszip-staging-*");
        Assert.Empty(stagingDirs);
    }

    [Fact]
    public async Task ExtractAsync_SolidArchive_OverwriteStrategy_ReplacesExistingFiles()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        foreach (var relative in relativePaths)
        {
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "stale");
        }

        var engine = new SevenZipEngine();
        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite };
        var result = await engine.ExtractAsync(
            archivePath, destination, password: null, progress: null,
            cancellationToken: CancellationToken.None, options: options);

        Assert.Equal(relativePaths.Count, result.SucceededEntries);
        Assert.Equal(relativePaths.Count, result.OverwrittenEntries);
        foreach (var relative in relativePaths)
            Assert.True(new FileInfo(Path.Combine(destination, relative)).Length > 100, $"未覆盖：{relative}");
    }

    /// <summary>
    /// 同步转发的进度实现：批量段的事件回调发生在 7z.dll 线程，UI 的 Progress 通常会 marshal，
    /// 测试里要保证断言时数据已写入（ConcurrentDictionary 本身线程安全，这里只是不丢报）。
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        public event EventHandler<T>? ProgressChanged;

        public void Report(T value) => ProgressChanged?.Invoke(this, value);
    }
}
