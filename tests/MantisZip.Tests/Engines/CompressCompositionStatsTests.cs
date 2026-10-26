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
/// 自适应分类构成（统计卡「已压缩 / 仅存储」的数据源）：
/// 引擎必须在自适应 ZIP 路径上报 CompressedFiles / StoreOnlyFiles（含输入字节），
/// 且两组的文件数、字节数之和等于本次操作的总量；非自适应路径不得上报（null）。
/// </summary>
public class CompressCompositionStatsTests
{
    private sealed class Collector(List<ArchiveProgress> sink) : IProgress<ArchiveProgress>
    {
        public void Report(ArchiveProgress value)
        {
            lock (sink) sink.Add(value);
        }
    }

    [Fact]
    public async Task AdaptiveZip_ReportsCompositionSplit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MZComposition", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // 2 个可压缩文本 + 3 个「已压缩」PNG（随机字节 + 已压缩扩展名 → 自适应判为仅存储）
            for (int i = 0; i < 2; i++)
                await File.WriteAllTextAsync(Path.Combine(dir, $"text{i}.txt"), new string('a', 32 * 1024));
            for (int i = 0; i < 3; i++)
            {
                var payload = new byte[16 * 1024];
                Random.Shared.NextBytes(payload);
                await File.WriteAllBytesAsync(Path.Combine(dir, $"pic{i}.png"), payload);
            }

            var sources = Directory.GetFiles(dir).ToArray();
            long totalInputBytes = sources.Sum(f => new FileInfo(f).Length);
            var outZip = Path.Combine(dir, "out.zip");
            var reports = new List<ArchiveProgress>();

            await new ZipEngine().CompressAsync(
                sources, outZip,
                new ArchiveOptions
                {
                    CompressionLevel = 5,
                    AdaptiveCompression = true,
                    MultiThreadedCompression = true,
                    ParallelCompressDegree = 2,
                },
                new Collector(reports));

            var composition = reports.Where(r => r.CompressedFiles.HasValue).ToList();
            Assert.NotEmpty(composition);

            var last = composition[^1];
            Assert.Equal(2, last.CompressedFiles);      // 两个 txt
            Assert.Equal(3, last.StoreOnlyFiles);       // 三个 png
            Assert.Equal(5, last.CompressedFiles + last.StoreOnlyFiles);
            Assert.Equal(totalInputBytes, last.CompressedBytes + last.StoreOnlyBytes);
            Assert.True(last.CompressedBytes > 0);
            Assert.True(last.StoreOnlyBytes > 0);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task NonAdaptiveZip_DoesNotReportComposition()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MZComposition", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "a.txt"), "hello");
            await File.WriteAllBytesAsync(Path.Combine(dir, "b.png"), new byte[4096]);

            var outZip = Path.Combine(dir, "out.zip");
            var reports = new List<ArchiveProgress>();

            await new ZipEngine().CompressAsync(
                Directory.GetFiles(dir).Where(f => f != outZip).ToArray(), outZip,
                new ArchiveOptions { CompressionLevel = 5, AdaptiveCompression = false },
                new Collector(reports));

            // 关闭自适应 → 不分类 → 统计卡应保持隐藏（null）
            Assert.DoesNotContain(reports, r => r.CompressedFiles.HasValue || r.StoreOnlyFiles.HasValue);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }
}
