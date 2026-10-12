using System.Diagnostics;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.Core.Services;

/// <summary>
/// 大包预测试优化器：解压 ≥1GB 压缩包前，用小样本对比不同线程数耗时，选出最优线程数。
/// 测试写入目标目录下的唯一临时根目录（__parallel_test_{GUID}，避免与预已存在的
/// 用户目录/并发实例冲突），测完整树删除，绝不触碰自己未创建的路径。
/// 任何异常/取消 → 返回负数（调用方回退磁盘类型默认值）。
/// </summary>
public static class ParallelDegreeOptimizer
{
    /// <summary>触发预测试的压缩包大小阈值（1GB）。</summary>
    public const long LargeArchiveThreshold = 1L * 1024 * 1024 * 1024;

    /// <summary>样本目标大小（约 50MB）。</summary>
    private const long SampleTargetBytes = 50L * 1024 * 1024;

    /// <summary>样本硬上限（500MB）：防止少量超大条目使样本规模失控。</summary>
    private const long HardCapBytes = 500L * 1024 * 1024;

    /// <summary>样本最少文件数（避免单文件大包主导结果）。</summary>
    private const int MinSampleFiles = 5;

    /// <summary>样本最多文件数（避免文件数过多影响测试）。</summary>
    private const int MaxSampleFiles = 50;

    /// <summary>
    /// 判断是否应执行预测试：压缩包 ≥1GB 且预测试开启且引擎支持并行。
    /// </summary>
    public static bool ShouldPreTest(long archiveSize, bool preTestEnabled, bool supportsParallel)
        => preTestEnabled && supportsParallel && archiveSize >= LargeArchiveThreshold;

    /// <summary>
    /// 根据 CPU 核数生成测试配置（去重、排序、≤4 个）：
    /// ≥16 核: [4,8,12,16]；8-15 核: [2,4,8,N]；&lt;8 核: [1,2,4,N]。
    /// </summary>
    public static int[] GetTestConfigs(int processorCount)
    {
        int[] raw = processorCount >= 16
            ? [4, 8, 12, 16]
            : processorCount >= 8
                ? [2, 4, 8, processorCount]
                : [1, 2, 4, processorCount];
        return raw.Distinct().OrderBy(x => x).Take(4).ToArray();
    }

    /// <summary>
    /// 快速测试找出最优并行度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 返回值未按用户设置的最大线程数钳制 —— 调用方负责 clamp
    /// （本方法只输出候选最优值，UserMaxThreads 等策略属调用方职责）。
    /// </para>
    /// <para>
    /// progress 参数暂不转发给引擎（预测试不向 UI 上报进度，避免与正式解压进度冲突）。
    /// </para>
    /// </remarks>
    /// <returns>最优线程数；失败/取消返回负数（-1），调用方回退磁盘类型默认值。</returns>
    public static async Task<int> FindOptimalDegreeAsync(
        string archivePath,
        string destinationPath,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct)
    {
        CoreLog.Entry();
        // 预取消令牌守卫：ZipEngine.ListEntriesAsync 不检查 CancellationToken，
        // 不加此守卫则已取消的调用会完整跑完预测试才返回
        ct.ThrowIfCancellationRequested();

        var configs = GetTestConfigs(Environment.ProcessorCount);
        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null || !engine.SupportsParallelExtract)
        {
            CoreLog.Info("FindOptimalDegreeAsync: engine does not support parallel, skip");
            return -1;
        }

        // 1. 选取测试样本（按压缩包内条目顺序累加到 ~50MB，500MB 硬上限）
        IReadOnlyList<ArchiveItem> allEntries;
        try
        {
            allEntries = await engine.ListEntriesAsync(archivePath, password, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CoreLog.Info($"FindOptimalDegreeAsync: ListEntriesAsync failed: {ex.Message}");
            return -1;
        }

        var sampleKeys = SelectSampleEntries(allEntries);
        if (sampleKeys.Count == 0)
        {
            CoreLog.Info("FindOptimalDegreeAsync: no sample entries");
            return -1;
        }

        CoreLog.Info($"FindOptimalDegreeAsync: {sampleKeys.Count} sample files, configs=[{string.Join(",", configs)}]");

        // 2. 逐配置测试。
        //    唯一运行根目录：同名 __parallel_test_{degree} 可能是用户已有内容
        //    （或并发实例所有），只在 GUID 命名的 runRoot 内创建子目录，只删 runRoot。
        var runRoot = Path.Combine(destinationPath, $"__parallel_test_{Guid.NewGuid():N}");
        int bestDegree = -1;
        long bestElapsed = long.MaxValue;

        try
        {
            foreach (var degree in configs)
            {
                ct.ThrowIfCancellationRequested();

                var tempDir = Path.Combine(runRoot, degree.ToString());
                try
                {
                    Directory.CreateDirectory(tempDir);

                    var sw = Stopwatch.StartNew();
                    await engine.ExtractEntriesAsync(
                        archivePath, sampleKeys, tempDir,
                        password, progress: null, ct,
                        options: new ArchiveOptions { ParallelExtractDegree = degree });
                    sw.Stop();

                    CoreLog.Info($"FindOptimalDegreeAsync: degree={degree}, elapsed={sw.ElapsedMilliseconds}ms");
                    if (sw.ElapsedMilliseconds < bestElapsed)
                    {
                        bestElapsed = sw.ElapsedMilliseconds;
                        bestDegree = degree;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    CoreLog.Info($"FindOptimalDegreeAsync: degree={degree} failed: {ex.Message}");
                    // 本配置失败跳过，继续下一配置
                }
                finally
                {
                    CleanupTempDir(tempDir);
                }
            }
        }
        finally
        {
            CleanupTempDir(runRoot);
        }

        CoreLog.Exit();
        return bestDegree; // -1 = 全部失败
    }

    /// <summary>
    /// 按压缩包内条目顺序累加文件大小到 ~50MB 选样本。
    /// 至少 MinSampleFiles 个（仅在 50MB 增长阶段内保证；硬上限触发即停，不足也照收），
    /// 最多 MaxSampleFiles 个，总量硬上限 HardCapBytes（防止超大条目使样本失控）。
    /// </summary>
    private static List<string> SelectSampleEntries(IReadOnlyList<ArchiveItem> entries)
    {
        var files = entries.Where(e => !e.IsDirectory).ToList();
        var result = new List<string>();
        long accumulated = 0;

        foreach (var entry in files)
        {
            if (result.Count >= MaxSampleFiles) break;
            result.Add(string.IsNullOrEmpty(entry.FullPath) ? entry.Name : entry.FullPath);
            accumulated += entry.Size;
            if (accumulated >= SampleTargetBytes && result.Count >= MinSampleFiles) break;
            if (accumulated >= HardCapBytes) break; // 硬上限，防止超大条目样本失控
        }

        // 文件太少时全取（硬上限已触发则不再回填，保持当前样本）
        if (result.Count < MinSampleFiles && accumulated < HardCapBytes)
            result = files.Take(MaxSampleFiles)
                .Select(e => string.IsNullOrEmpty(e.FullPath) ? e.Name : e.FullPath)
                .ToList();

        return result;
    }

    private static void CleanupTempDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            CoreLog.Info($"CleanupTempDir: failed to delete {dir}: {ex.Message}");
        }
    }
}
