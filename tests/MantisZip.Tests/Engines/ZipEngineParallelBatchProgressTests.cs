using System;
using System.IO;
using System.IO.Compression;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// 回归：并行解压时批次的字节进度必须包含「当前正在复制的文件」的已拷字节。
/// 旧实现只累加「已完成条目」字节（<c>batchProcessedBytes</c>），导致批次行进度在
/// 处理该批次首个（也是最大的）文件期间恒为 0；且末文件的完成日永远进不了该行（到不了 100%）。
/// </summary>
public class ZipEngineParallelBatchProgressTests
{
    [Fact]
    public async Task ExtractAsync_Parallel_FirstBatchRow_Reaches100()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zip-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "t.zip");
        try
        {
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                void Add(string n, int size)
                {
                    var e = z.CreateEntry(n, CompressionLevel.Optimal);
                    using var s = e.Open();
                    var buf = new byte[size];
                    for (int i = 0; i < size; i++) buf[i] = (byte)('A' + (i % 16));
                    s.Write(buf, 0, buf.Length);
                }
                Add("b0.bin", 40000);   // 较大 → Round-Robin 落 batch0（单独一个文件）
                Add("b1.bin", 4000);    // 较小 → batch1
            }

            var reports = new List<ArchiveProgress>();
            await new ZipEngine().ExtractAsync(
                zip, Path.Combine(dir, "out"), null, new Capture(reports), CancellationToken.None,
                new ArchiveOptions { ParallelExtractDegree = 2 });

            // batch0 恰含 1 个文件；其最终 in-loop 上报应携带该文件字节 → BatchPercentComplete=100。
            // 修复前 batchProcessedBytes 仍为 0 → 该报告 BatchPercentComplete=0，断言失败。
            Assert.Contains(reports, r =>
                r.BatchIndex == 0 && r.FilePercentComplete == 100 && r.BatchPercentComplete == 100);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 临时目录尽力清理 */ }
        }
    }

    private sealed class Capture : IProgress<ArchiveProgress>
    {
        private readonly List<ArchiveProgress> _l;
        public Capture(List<ArchiveProgress> l) => _l = l;
        public void Report(ArchiveProgress v) { lock (_l) _l.Add(v); }
    }
}
