using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using SharpSevenZip;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// 选中项批量解压（7z solid，两段式：解码到暂存 → 移动到最终位置）的进度序列回归。
///
/// 背景（2026-11-04 用户实测）：解压过程分三段，但第一段进度条就走完 100%、第二段进度条与
/// 状态卡片都不动、第三段才更新卡片。根因是解码段的 <c>FileExtractionFinished</c> 把**整块**
/// 被解码的条目（含未选中的）都算进了进度单位，而分母只有选中条数 ⇒ 第 N 个选中文件解码完
/// 进度就满格，其余条目在 100% 下静默解码；同时解码段没有文件内推进、统计卡也没有「暂存」项。
/// 这些用例锁住修复后的三条硬约束：解码段不越过 50%、段内单调不减、未选中条目不参与。
/// </summary>
public sealed class SevenZipEngineSelectedBulkProgressTests : IDisposable
{
    private const int FileCount = 12;
    private const int FileSizeBytes = 64 * 1024;

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

    private (string ArchivePath, List<string> RelativePaths)? CreateSolidArchive()
    {
        var dllPath = Resolve7zDll();
        if (dllPath == null)
            return null;

        SharpSevenZipBase.SetLibraryPath(dllPath);

        var workDir = TrackTempDir(Path.Combine(Path.GetTempPath(), "MantisZipBulkProgress", Guid.NewGuid().ToString("N")));
        var sourceDir = Path.Combine(workDir, "src");
        Directory.CreateDirectory(sourceDir);

        var relativePaths = new List<string>();
        for (var i = 0; i < FileCount; i++)
        {
            var name = $"file-{i:D3}.bin";
            File.WriteAllBytes(Path.Combine(sourceDir, name), MakeCompressible(FileSizeBytes));
            relativePaths.Add(name);
        }

        var archivePath = TrackTempFile(Path.Combine(workDir, "solid.7z"));
        var compressor = new SharpSevenZipCompressor
        {
            ArchiveFormat = OutArchiveFormat.SevenZip,
            CompressionLevel = CompressionLevel.Fast,
            CompressionMethod = CompressionMethod.Lzma2,
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

    private static bool IsSolid(string archivePath)
    {
        using var extractor = new SharpSevenZipExtractor(archivePath);
        return extractor.IsSolid;
    }

    [Fact]
    public async Task ExtractEntriesAsync_SolidBulk_DecodePhaseNeverFillsPastHalf()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = TrackTempDir(Path.Combine(Path.GetTempPath(), "MantisZipBulkProgress", Guid.NewGuid().ToString("N")));
        // 每 3 条选 1 条：选中 4 条、整块 12 条 ⇒ 修复前第 4 条解码完就 100%（另有 8 条在满格下静默解码）
        var selected = relativePaths.Where((_, index) => index % 3 == 0).ToArray();
        var reports = new List<ArchiveProgress>();

        var engine = new SevenZipEngine();
        await engine.ExtractEntriesAsync(
            archivePath, selected, destination, password: null,
            progress: new SynchronousProgress<ArchiveProgress>(reports.Add),
            cancellationToken: CancellationToken.None, options: null);

        Assert.NotEmpty(reports);

        // 只看「进度样本」：带 EntryStatus/EntryKey 的条上报是逐条目事件通知（重名、失败原因等），
        // 它们不携带主进度，混进来会把条形图误判成回退。
        var samples = reports.Where(r => !r.EntryStatus.HasValue).ToList();
        Assert.NotEmpty(samples);

        // 解码段（还没有任何条目落地）主进度最多到一半：另一半留给「移动到最终位置」
        var decodeReports = samples.Where(r => r.ProcessedFiles == 0 && r.TotalFiles > 0).ToList();
        Assert.NotEmpty(decodeReports);
        Assert.All(decodeReports, r => Assert.True(
            r.PercentComplete <= 55,
            $"解码段主进度冲过半数：{r.PercentComplete:F1}%（当前 {r.CurrentFile}）"));

        // 整条序列单调不减，且最终到 100%
        var percents = samples.Select(r => r.PercentComplete).ToList();
        for (var i = 1; i < percents.Count; i++)
            Assert.True(percents[i] >= percents[i - 1] - 0.001,
                $"主进度回退：{percents[i - 1]:F1}% → {percents[i]:F1}%（第 {i} 个进度样本；" +
                $"前：phase={samples[i - 1].Phase} file={samples[i - 1].CurrentFile}；" +
                $"后：phase={samples[i].Phase} file={samples[i].CurrentFile}）");
        Assert.Equal(100, samples[^1].PercentComplete, 3);

        // 每个选中文件都落盘
        foreach (var relative in selected)
        {
            var extracted = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(extracted), $"缺少选中文件：{relative}");
        }
    }

    [Fact]
    public async Task ExtractEntriesAsync_SolidBulk_StagedCountRisesThenFalls()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = TrackTempDir(Path.Combine(Path.GetTempPath(), "MantisZipBulkProgress", Guid.NewGuid().ToString("N")));
        var selected = relativePaths.Where((_, index) => index % 3 == 0).ToArray();
        var reports = new List<ArchiveProgress>();

        var engine = new SevenZipEngine();
        await engine.ExtractEntriesAsync(
            archivePath, selected, destination, password: null,
            progress: new SynchronousProgress<ArchiveProgress>(reports.Add),
            cancellationToken: CancellationToken.None, options: null);

        var staged = reports.Where(r => r.StagedFiles.HasValue).Select(r => r.StagedFiles!.Value).ToList();

        // 「暂存」统计只在两段式批量解压里带；解码段递增（最高到选中条数）、移动段递减，最后不再携带
        Assert.NotEmpty(staged);
        Assert.Equal(selected.Length, staged.Max());
        Assert.All(staged, v => Assert.InRange(v, 0, selected.Length));

        // 首个带 staged 的采样是上升沿（=1），减到 0 时该字段变 null 不再出现，
        // 所以「回落」要看末次采样低于峰值，而不是低于首次采样。
        Assert.True(staged[^1] < staged.Max(),
            $"暂存数没有回落：峰值 {staged.Max()} → 末次 {staged[^1]}");

        // 落地完成后的收尾上报不再携带该统计（UI 据此把「暂存」卡整卡隐藏）
        Assert.Null(reports[^1].StagedFiles);
    }

    [Fact]
    public async Task ExtractEntriesAsync_SolidBulk_NeverReportsUnselectedFileNames()
    {
        var fixture = CreateSolidArchive();
        if (fixture == null) return;
        var (archivePath, relativePaths) = fixture.Value;
        if (!IsSolid(archivePath)) return;

        var destination = TrackTempDir(Path.Combine(Path.GetTempPath(), "MantisZipBulkProgress", Guid.NewGuid().ToString("N")));
        var selected = relativePaths.Where((_, index) => index % 3 == 0).ToArray();
        var unselected = relativePaths.Except(selected, StringComparer.OrdinalIgnoreCase).ToArray();
        var reports = new List<ArchiveProgress>();

        var engine = new SevenZipEngine();
        await engine.ExtractEntriesAsync(
            archivePath, selected, destination, password: null,
            progress: new SynchronousProgress<ArchiveProgress>(reports.Add),
            cancellationToken: CancellationToken.None, options: null);

        // solid 块会把未选中的条目也解码出来，但它们不得出现在「当前文件」里（否则用户会看到没选的文件在走）
        var leaked = reports
            .Select(r => r.CurrentFile)
            .Where(name => !string.IsNullOrEmpty(name) && unselected.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.Empty(leaked);
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SynchronousProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
