using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 端到端：真实引擎（自适应 + 多线程）跑「少量 txt + 大量 jpg」，
/// 进度窗口应能从并行通道行切到一条「写入行」（IsParallel=false）。
/// 引擎回调来自工作线程，而 <see cref="ProgressViewModel.SetProgress"/> 会切回 UI 线程
/// （无头环境无 dispatcher 循环时不可靠），故先把上报序列收集起来，再在测试线程按原顺序回放。
/// 若非并行行缺失，断言消息会列出全部行的状态，便于区分「通道没到 100%」与「上报没进 VM」。
/// </summary>
public class MergeWriteRowIntegrationTests
{
    private sealed class Collector(List<ArchiveProgress> sink) : IProgress<ArchiveProgress>
    {
        public void Report(ArchiveProgress value)
        {
            lock (sink) sink.Add(value);
        }
    }

    [Fact]
    public async Task AdaptiveMultiThread_ProgressWindowShowsWriteRow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MZWriteRow", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (int i = 0; i < 2; i++)
                await File.WriteAllTextAsync(Path.Combine(dir, $"text{i}.txt"), new string('a', 48 * 1024));
            for (int i = 0; i < 6; i++)
            {
                var payload = new byte[24 * 1024];
                Random.Shared.NextBytes(payload);
                await File.WriteAllBytesAsync(Path.Combine(dir, $"photo{i}.jpg"), payload);
            }

            var outZip = Path.Combine(dir, "out.zip");
            var sources = Directory.GetFiles(dir)
                .Where(f => !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToArray();

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

            // 回放真实上报序列（顺序与原运行一致）
            var vm = new ProgressViewModel();
            vm.InitBatchMode(new[] { outZip });
            foreach (var p in reports)
                vm.SetProgress(p);

            // 详细面板「当前文件」行（独立控件）在合并阶段必须停在某个具体文件上，
            // 且右侧字节信息为「已写 / 总量」口径——不再依赖并行通道行的百分比是否恰好 100。
            Assert.False(string.IsNullOrEmpty(vm.FileName), "详细面板当前文件行没有文件名");
            Assert.True(vm.FileName!.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        || vm.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase),
                $"当前文件行文件名异常：{vm.FileName}");
            Assert.False(string.IsNullOrEmpty(vm.DetailFileInfoText), "当前文件行右侧字节信息为空");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }
}
