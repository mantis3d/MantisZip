using System.Collections.Concurrent;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using SharpSevenZip;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// 选中条目 / 拖拽解压改走批量接口（<c>SharpSevenZipExtractor.ExtractFiles</c> + 暂存目录再移动）的回归测试。
///
/// 覆盖计划《选中条目 / 拖拽解压改走批量接口》§8 里最关键的那几条：
/// ① 只解选中的条目；② 输出路径 override（扁平化 / 保留完整路径）在批量路径里同样生效；
/// ③ 逐条目状态不丢；④ Skip 不动已有文件；⑤ 冲突改名 / 覆盖语义不变；
/// ⑥ 暂存目录不残留；⑦ 取消不留下半成品；⑧ 目录条目也遵守 override（D7）。
///
/// 类名带 <c>SevenZipEngine</c> 是为了沿用 CI 的 <c>FullyQualifiedName!~SevenZipEngine</c> 过滤
/// （这些用例依赖本机 7z.dll，CI runner 上不保证存在）。
/// </summary>
public sealed class SevenZipEngineSelectedBulkExtractTests : IDisposable
{
    private const int FileCount = 20;
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
    /// 造一个 solid 7z：<c>plain-{i}.bin</c> + <c>sub/nested-{i}.bin</c>，可选带一个空目录 <c>emptydir/</c>。
    /// 返回 null 表示环境里没有 7z.dll。
    /// </summary>
    private (string ArchivePath, List<string> RelativePaths)? CreateSolidArchive(bool includeEmptyDirectory = false)
    {
        var dllPath = Resolve7zDll();
        if (dllPath == null)
            return null;

        SharpSevenZipBase.SetLibraryPath(dllPath);

        var workDir = TrackTempDir(Path.Combine(Path.GetTempPath(), "MantisZipSelectedBulk", Guid.NewGuid().ToString("N")));
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
            relativePaths.Add($"sub/{nestedName}");
        }

        if (includeEmptyDirectory)
            Directory.CreateDirectory(Path.Combine(sourceDir, "emptydir"));

        var archivePath = TrackTempFile(Path.Combine(workDir, "solid.7z"));
        var compressor = new SharpSevenZipCompressor
        {
            ArchiveFormat = OutArchiveFormat.SevenZip,
            CompressionLevel = CompressionLevel.Fast,
            CompressionMethod = CompressionMethod.Lzma2,
            IncludeEmptyDirectories = includeEmptyDirectory,
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
        => TrackTempDir(Path.Combine(Path.GetTempPath(), "MantisZipSelectedBulk", Guid.NewGuid().ToString("N")));

    /// <summary>前提校验：夹具必须是 solid，否则证明不了批量路径被走到（逐条目路径同样能全解）。</summary>
    private static bool IsSolid(string archivePath)
    {
        using var extractor = new SharpSevenZipExtractor(archivePath);
        return extractor.IsSolid;
    }

    private static string[] SelectedSubset(List<string> relativePaths)
        => relativePaths.Where((_, index) => index % 2 == 0).Take(6).ToArray();

    private static void AssertNoStagingLeftovers(string destination)
    {
        if (!Directory.Exists(destination))
            return;
        Assert.Empty(Directory.GetDirectories(destination, ".mantiszip-staging-*"));
    }

    [Fact]
    public async Task ExtractEntriesAsync_SolidArchive_ExtractsOnlySelectedEntries()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var selected = SelectedSubset(relativePaths);
        var engine = new SevenZipEngine();

        await engine.ExtractEntriesAsync(archivePath, selected, destination, cancellationToken: CancellationToken.None);

        foreach (var relative in selected)
        {
            var extracted = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(extracted), $"缺少选中文件：{relative}");
            Assert.Equal(FileSizeBytes, new FileInfo(extracted).Length);
        }

        // 未选中的条目一条都不该出现
        var unselected = relativePaths.Except(selected).ToArray();
        Assert.NotEmpty(unselected);
        foreach (var relative in unselected)
            Assert.False(File.Exists(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar))), $"解出了未选中的文件：{relative}");

        AssertNoStagingLeftovers(destination);
    }

    [Fact]
    public async Task ExtractEntriesAsync_FlattenOverride_LandsAtOverriddenPath()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var nested = relativePaths.Where(p => p.StartsWith("sub/", StringComparison.Ordinal)).Take(4).ToArray();
        var overrides = nested.ToDictionary(
            p => p,
            p => Path.Combine(destination, Path.GetFileName(p)),
            StringComparer.OrdinalIgnoreCase);

        var engine = new SevenZipEngine();
        await engine.ExtractEntriesAsync(
            archivePath, nested, destination, password: null, progress: null,
            cancellationToken: CancellationToken.None, options: null, outputPathOverrides: overrides);

        foreach (var relative in nested)
        {
            var flattened = Path.Combine(destination, Path.GetFileName(relative));
            Assert.True(File.Exists(flattened), $"扁平化落点缺失：{flattened}");
            Assert.False(Directory.Exists(Path.Combine(destination, "sub")), "扁平化后不该再建 sub 目录");
        }

        AssertNoStagingLeftovers(destination);
    }

    [Fact]
    public async Task ExtractEntriesAsync_ReportsEverySelectedEntryStatus()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var selected = SelectedSubset(relativePaths);
        var engine = new SevenZipEngine();
        var progress = new SynchronousProgress<ArchiveProgress>();
        var statuses = new ConcurrentDictionary<string, ArchiveEntryStatus>(StringComparer.OrdinalIgnoreCase);
        progress.ProgressChanged += (_, p) =>
        {
            if (!string.IsNullOrEmpty(p.EntryKey) && p.EntryStatus.HasValue)
                statuses[p.EntryKey] = p.EntryStatus.Value;
        };

        await engine.ExtractEntriesAsync(
            archivePath, selected, destination, password: null, progress: progress,
            cancellationToken: CancellationToken.None);

        // D11：批量段（暂存解码 + 移动）也必须逐条上报状态
        Assert.Equal(selected.Length, statuses.Count);
        foreach (var relative in selected)
            Assert.True(statuses.ContainsKey(relative), $"缺少逐条目状态：{relative}");
    }

    [Fact]
    public async Task ExtractEntriesAsync_SkipStrategy_KeepsExistingFiles()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var selected = SelectedSubset(relativePaths);
        foreach (var relative in selected)
        {
            var target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "keep-me");
        }

        var engine = new SevenZipEngine();
        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Skip };
        await engine.ExtractEntriesAsync(
            archivePath, selected, destination, password: null, progress: null,
            cancellationToken: CancellationToken.None, options: options);

        foreach (var relative in selected)
            Assert.Equal("keep-me", File.ReadAllText(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar))));

        AssertNoStagingLeftovers(destination);
    }

    [Fact]
    public async Task ExtractEntriesAsync_OverwriteStrategy_ReplacesExistingFiles()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var selected = SelectedSubset(relativePaths);
        foreach (var relative in selected)
        {
            var target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "stale");
        }

        var engine = new SevenZipEngine();
        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite };
        await engine.ExtractEntriesAsync(
            archivePath, selected, destination, password: null, progress: null,
            cancellationToken: CancellationToken.None, options: options);

        foreach (var relative in selected)
        {
            var target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(new FileInfo(target).Length > 100, $"未覆盖：{relative}");
        }
    }

    [Fact]
    public async Task ExtractEntriesAsync_RenameStrategy_WritesRenamedCopiesAndCleansStaging()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var selected = SelectedSubset(relativePaths);
        var originals = new List<string>();
        foreach (var relative in selected)
        {
            var target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "original");
            originals.Add(target);
        }

        var engine = new SevenZipEngine();
        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Rename };
        await engine.ExtractEntriesAsync(
            archivePath, selected, destination, password: null, progress: null,
            cancellationToken: CancellationToken.None, options: options);

        foreach (var original in originals)
            Assert.Equal("original", File.ReadAllText(original));

        var allFiles = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
        Assert.Equal(selected.Length * 2, allFiles.Length);
        Assert.Contains(allFiles, f => Path.GetFileName(f).Contains('(', StringComparison.Ordinal));
        AssertNoStagingLeftovers(destination);
    }

    [Fact]
    public async Task ExtractEntriesAsync_CancelledToken_ThrowsAndLeavesNoStaging()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var selected = SelectedSubset(relativePaths);
        var engine = new SevenZipEngine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ExtractEntriesAsync(
            archivePath, selected, destination, password: null, progress: null,
            cancellationToken: cts.Token));

        AssertNoStagingLeftovers(destination);
    }

    [Fact]
    public async Task ExtractEntriesAsync_DirectoryEntry_HonoursOutputPathOverride()
    {
        var fixture = CreateSolidArchive(includeEmptyDirectory: true);
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        Directory.CreateDirectory(destination);
        var engine = new SevenZipEngine();

        // 目录条目在归档里的真实键名（7z 用反斜杠，且目录条目带尾分隔符），
        // 引擎内部的 keySet 与 override 查表都用 ArchivePath.Normalize 之后的名字
        string directoryKey;
        using (var probe = new SharpSevenZipExtractor(archivePath))
        {
            var rawKey = probe.ArchiveFileData
                .First(e => e.IsDirectory && e.FileName.Contains("emptydir", StringComparison.OrdinalIgnoreCase))
                .FileName;
            directoryKey = MantisZip.Core.Utils.ArchivePath.Normalize(rawKey);
        }

        // 只选空目录 + 一个文件，目录 override 到「renamed-dir」
        var selected = new[] { directoryKey, relativePaths[0] };
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [directoryKey] = Path.Combine(destination, "renamed-dir"),
        };

        await engine.ExtractEntriesAsync(
            archivePath, selected, destination, password: null, progress: null,
            cancellationToken: CancellationToken.None, options: null, outputPathOverrides: overrides);

        Assert.True(Directory.Exists(Path.Combine(destination, "renamed-dir")), "目录条目没有遵守 outputPathOverrides（D7 未修）");
        Assert.False(Directory.Exists(Path.Combine(destination, "emptydir")), "不该在原始位置建目录");
    }

    [Fact]
    public async Task ExtractEntriesAsync_SingleEntry_StillExtractsViaPerEntryPath()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var selected = new[] { relativePaths[0] };
        var engine = new SevenZipEngine();

        await engine.ExtractEntriesAsync(archivePath, selected, destination, cancellationToken: CancellationToken.None);

        var extracted = Path.Combine(destination, selected[0].Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(extracted));
        AssertNoStagingLeftovers(destination);
    }

    [Fact]
    public async Task ExtractEntriesAsync_UnknownKeys_AreIgnoredWithoutThrowing()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = CreateDestination();
        var selected = new[] { relativePaths[0], "does-not-exist.bin" };
        var engine = new SevenZipEngine();

        await engine.ExtractEntriesAsync(archivePath, selected, destination, cancellationToken: CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(destination, relativePaths[0])));
        AssertNoStagingLeftovers(destination);
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
