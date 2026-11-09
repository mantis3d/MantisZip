using System.Collections.Concurrent;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using SharpSevenZip;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// 加密 solid 7z 解压走批量接口的回归测试（计划 §5.10 2026-11-05 复评放开）。
///
/// 背景：加密包此前被 <c>BuildBulkPlanAsync</c> 的闸门整条拦掉，退回逐条目 —— solid 包里
/// 每个文件都要重解整块，48MB/309 文件实测 295s；批量一次解码只需 3s。放开后同样要保证
/// ① 带密码能解出全部文件且内容正确；② 逐条目状态上报一条不落；③ 冲突策略（Skip）依旧生效。
///
/// 类名带 <c>SevenZipEngine</c> 是为了沿用 CI 的 <c>FullyQualifiedName!~SevenZipEngine</c> 过滤
/// （这些用例依赖本机 7z.dll，CI runner 上不保证存在）。
/// </summary>
public sealed class SevenZipEngineEncryptedBulkExtractTests : IDisposable
{
    private const string Password = "Mantis!2026";
    private const int FileCount = 30;
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
    /// 造一个**加密** solid 7z：<c>root/secret-{i}.bin</c> + <c>root/sub/deep-{i}.bin</c>。
    /// 返回 null 表示环境里没有 7z.dll。
    /// </summary>
    private (string ArchivePath, List<string> RelativePaths)? CreateEncryptedSolidArchive()
    {
        var dllPath = Resolve7zDll();
        if (dllPath == null)
            return null;

        SharpSevenZipBase.SetLibraryPath(dllPath);

        var workDir = TrackTempDir(Path.Combine(Path.GetTempPath(), "MantisZipEncBulk", Guid.NewGuid().ToString("N")));
        var sourceDir = Path.Combine(workDir, "src");
        var nestedDir = Path.Combine(sourceDir, "sub");
        Directory.CreateDirectory(nestedDir);

        var relativePaths = new List<string>();
        for (var i = 0; i < FileCount; i++)
        {
            var name = $"secret-{i:D3}.bin";
            File.WriteAllBytes(Path.Combine(sourceDir, name), MakeCompressible(FileSizeBytes));
            relativePaths.Add(name);

            var nestedName = $"deep-{i:D3}.bin";
            File.WriteAllBytes(Path.Combine(nestedDir, nestedName), MakeCompressible(FileSizeBytes));
            relativePaths.Add(Path.Combine("sub", nestedName));
        }

        var archivePath = TrackTempFile(Path.Combine(workDir, "encrypted-solid.7z"));
        var compressor = new SharpSevenZipCompressor
        {
            ArchiveFormat = OutArchiveFormat.SevenZip,
            CompressionLevel = CompressionLevel.Fast,
            CompressionMethod = CompressionMethod.Lzma2,
            IncludeEmptyDirectories = false,
            DirectoryStructure = true,
            // 只加密条目数据、不加密头部：让「有密码但头部可读」这一最常见形态参与测试
            EncryptHeaders = false,
        };
        // 密码走 CompressDirectory 重载（SharpSevenZipBase.Password 的 setter 是 internal）
        compressor.CompressDirectory(sourceDir, archivePath, Password);

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
        => TrackTempDir(Path.Combine(Path.GetTempPath(), "MantisZipEncBulk", Guid.NewGuid().ToString("N")));

    /// <summary>
    /// 前提校验：夹具必须是 solid **且**确实带加密条目，否则这些用例证明不了新放开的路径。
    /// </summary>
    private static (bool IsSolid, bool HasEncrypted) InspectFixture(string archivePath)
    {
        using var extractor = new SharpSevenZipExtractor(archivePath, Password);
        return (extractor.IsSolid, extractor.ArchiveFileData.Any(e => !e.IsDirectory && e.Encrypted));
    }

    [Fact]
    public async Task ExtractAsync_EncryptedSolidArchive_ExtractsEveryFileWithPassword()
    {
        var fixture = CreateEncryptedSolidArchive();
        if (fixture == null) return; // 无 7z.dll：跳过
        var (archivePath, relativePaths) = fixture.Value;
        var (isSolid, hasEncrypted) = InspectFixture(archivePath);
        if (!isSolid || !hasEncrypted) return; // 夹具不满足前提：本用例无意义

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
            archivePath, destination, password: Password, progress: progress,
            cancellationToken: CancellationToken.None);

        Assert.Equal(relativePaths.Count, result.SucceededEntries);
        Assert.Equal(0, result.FailedEntries);
        foreach (var relative in relativePaths)
        {
            var extracted = Path.Combine(destination, relative);
            Assert.True(File.Exists(extracted), $"缺少文件：{relative}");
            Assert.Equal(FileSizeBytes, new FileInfo(extracted).Length);
        }

        // 逐条目状态：批量段未选中条目也会触发 Finished，引擎必须按 indexMap 过滤后逐条上报
        Assert.Equal(relativePaths.Count, statuses.Count);
        foreach (var relative in relativePaths)
            Assert.True(statuses.ContainsKey(relative.Replace('\\', '/')), $"缺少逐条目状态：{relative}");
    }

    [Fact]
    public async Task ExtractAsync_EncryptedSolidArchive_SkipStrategy_KeepsExistingFiles()
    {
        var fixture = CreateEncryptedSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        var (isSolid, hasEncrypted) = InspectFixture(archivePath);
        if (!isSolid || !hasEncrypted) return;

        var destination = CreateDestination();
        foreach (var relative in relativePaths)
        {
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "keep-me");
        }

        var engine = new SevenZipEngine();
        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Skip };
        var result = await engine.ExtractAsync(
            archivePath, destination, password: Password, progress: null,
            cancellationToken: CancellationToken.None, options: options);

        // 冲突预解析在批量之前完成：带密码不影响 Skip 语义
        Assert.Equal(relativePaths.Count, result.SkippedEntries);
        Assert.Equal(0, result.SucceededEntries);
        foreach (var relative in relativePaths)
            Assert.Equal("keep-me", File.ReadAllText(Path.Combine(destination, relative)));
    }

    [Fact]
    public async Task ExtractEntriesAsync_EncryptedSolidArchive_ExtractsOnlySelectedEntries()
    {
        var fixture = CreateEncryptedSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        var (isSolid, hasEncrypted) = InspectFixture(archivePath);
        if (!isSolid || !hasEncrypted) return;

        var destination = CreateDestination();
        var engine = new SevenZipEngine();
        // 跨整包取前 1/3：solid 块里必然包含未选中条目（验证 indexMap 过滤与暂存清理）
        var selected = relativePaths
            .Where((_, index) => index % 3 == 0)
            .Select(p => p.Replace('\\', '/'))
            .ToList();

        await engine.ExtractEntriesAsync(
            archivePath, selected, destination, password: Password,
            cancellationToken: CancellationToken.None);

        foreach (var relative in selected)
        {
            var extracted = Path.Combine(destination, relative);
            Assert.True(File.Exists(extracted), $"缺少选中文件：{relative}");
            Assert.Equal(FileSizeBytes, new FileInfo(extracted).Length);
        }

        // 未选中条目一个都不许落盘
        var landed = Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length;
        Assert.Equal(selected.Count, landed);

        // 暂存目录必须清干净
        Assert.Empty(Directory.GetDirectories(destination, ".mantiszip-staging-*"));
    }

    /// <summary>同步转发进度的实现：批量段事件回调在 7z.dll 线程，断言前必须已写入。</summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        public event EventHandler<T>? ProgressChanged;

        public void Report(T value) => ProgressChanged?.Invoke(this, value);
    }
}
