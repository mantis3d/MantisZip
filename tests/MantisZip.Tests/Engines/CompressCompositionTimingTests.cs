using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// 「已压缩 / 仅存储」构成上报的时机：必须出现在任何压缩进度之前 ——
/// 无论走多线程路径（扫描分类后上报）还是串行路径（预先整体分类后上报），
/// 统计卡都应在第一帧就出现（用户反馈：串行回落时「仅存储」卡要等存储阶段才出现）。
/// </summary>
public class CompressCompositionTimingTests
{
    private sealed class Collector(List<(int Index, ArchiveProgress Report)> sink) : IProgress<ArchiveProgress>
    {
        public void Report(ArchiveProgress value)
        {
            lock (sink) sink.Add((sink.Count, value));
        }
    }

    private static async Task<(string Dir, string OutZip)> PrepareAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MZCompositionTiming", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        for (int i = 0; i < 2; i++)
            await File.WriteAllTextAsync(Path.Combine(dir, $"text{i}.txt"), new string('a', 64 * 1024));
        for (int i = 0; i < 6; i++)
        {
            var payload = new byte[32 * 1024];
            Random.Shared.NextBytes(payload);
            await File.WriteAllBytesAsync(Path.Combine(dir, $"pic{i}.png"), payload);
        }
        return (dir, Path.Combine(dir, "out.zip"));
    }

    [Theory]
    [InlineData(true)]   // 多线程路径：扫描分类后上报
    [InlineData(false)]  // 串行路径：预先整体分类后上报（本次修复）
    public async Task CompositionReport_ArrivesBeforeCompressionProgress(bool multiThreaded)
    {
        var (dir, outZip) = await PrepareAsync();
        try
        {
            var reports = new List<(int Index, ArchiveProgress Report)>();
            await new ZipEngine().CompressAsync(
                Directory.GetFiles(dir).Where(f => f != outZip).ToArray(), outZip,
                new ArchiveOptions
                {
                    CompressionLevel = 5,
                    AdaptiveCompression = true,
                    MultiThreadedCompression = multiThreaded,
                    ParallelCompressDegree = 2,
                },
                new Collector(reports));

            var composition = reports.Where(r => r.Report.StoreOnlyFiles.HasValue).ToList();
            Assert.NotEmpty(composition);

            int firstComposition = composition[0].Index;
            var firstProgress = reports.FirstOrDefault(r => r.Report.PercentComplete > 0);
            int firstProgressIndex = firstProgress.Report is null ? int.MaxValue : firstProgress.Index;

            Assert.True(firstComposition < firstProgressIndex,
                $"构成上报应早于压缩进度：composition@{firstComposition} vs progress@{firstProgressIndex}");

            // 构成值必须已经完整（2 txt 可压缩 + 6 png 仅存储），而不是先出现 0
            Assert.Equal(2, composition[0].Report.CompressedFiles);
            Assert.Equal(6, composition[0].Report.StoreOnlyFiles);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }
}
