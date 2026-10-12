using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.Core.Services;

/// <summary>
/// 并行解压设置快照（Core 层不依赖 UI 的 AppSettings，由调用方传入）。
/// </summary>
public record ParallelExtractSettings
{
    /// <summary>全局并行开关：false = 始终串行（线程数=1）。</summary>
    public bool EnableParallel { get; init; } = true;

    /// <summary>智能并行度开关：true = 按磁盘类型/预测试选线程数。</summary>
    public bool SmartParallel { get; init; } = true;

    /// <summary>智能模式 SSD 线程数（1-16）。</summary>
    public int SmartSsdDegree { get; init; } = 12;

    /// <summary>智能模式 HDD 线程数（1-16）。</summary>
    public int SmartHddDegree { get; init; } = 8;

    /// <summary>大包预测试开关（仅智能模式参与决策链第 4 步）。</summary>
    public bool LargeArchivePreTest { get; init; } = true;

    /// <summary>手动线程数（智能关闭时使用；0 = ProcessorCount）。</summary>
    public int ManualDegree { get; init; }
}

/// <summary>
/// 并行解压线程数决策链（5 步）：
/// 1. callerDegree &gt; 0 → 直接使用（调用方覆盖）
/// 2. EnableParallel = false → 1（串行）
/// 3. SmartParallel = false → ManualDegree 或 ProcessorCount
/// 4. 大包 + 预测试开 → FindOptimalDegreeAsync（异步路径）
/// 5. 磁盘类型默认值（Ssd→SmartSsdDegree / Hdd→SmartHddDegree / Unknown→min(CPU,12)）
/// </summary>
public static class ParallelDegreeResolver
{
    /// <summary>未知磁盘回退上限。</summary>
    private const int UnknownDiskCap = 12;

    /// <summary>线程数上限。</summary>
    private const int MaxDegree = 16;

    /// <summary>
    /// 同步决策（步骤 1-3 + 5；步骤 4 由异步封装在满足条件时先执行预测试再落到本方法）。
    /// 供测试与非大包路径使用。
    /// </summary>
    /// <param name="callerDegree">调用方覆盖（null 或 ≤0 = 无覆盖）。</param>
    /// <param name="settings">并行解压设置快照。</param>
    /// <param name="diskType">目标磁盘类型（步骤 5 输入）。</param>
    /// <param name="archiveSize">压缩包大小（当前同步路径不触发预测试，仅记录；预测试由 ResolveAsync 处理）。</param>
    public static int Resolve(
        int? callerDegree,
        ParallelExtractSettings settings,
        DiskType diskType,
        long archiveSize = 0)
    {
        // 步骤 1：调用方明确指定（最高优先级）
        if (callerDegree is > 0)
            return Math.Clamp(callerDegree.Value, 1, MaxDegree);

        // 步骤 2：全局关 → 串行
        if (!settings.EnableParallel)
            return 1;

        // 步骤 3：智能关 → 手动值或 ProcessorCount
        if (!settings.SmartParallel)
        {
            var manual = settings.ManualDegree > 0 ? settings.ManualDegree : Environment.ProcessorCount;
            return Math.Clamp(manual, 1, MaxDegree);
        }

        // 步骤 5：磁盘类型默认值（步骤 4 预测试不适用/失败时也落到这里）
        return diskType switch
        {
            DiskType.Ssd => Math.Clamp(settings.SmartSsdDegree, 1, MaxDegree),
            DiskType.Hdd => Math.Clamp(settings.SmartHddDegree, 1, MaxDegree),
            _ => Math.Clamp(Math.Min(Environment.ProcessorCount, UnknownDiskCap), 1, MaxDegree)
        };
    }

    /// <summary>
    /// 异步决策：步骤 1-3 同步短路；满足预测试条件（步骤 4）时先执行预测试，
    /// 预测试失败/不适用回退磁盘类型默认值（步骤 5）。
    /// </summary>
    /// <param name="callerDegree">调用方覆盖（解压对话框单次指定）。</param>
    /// <param name="settings">设置快照。</param>
    /// <param name="archivePath">压缩包路径（预测试列目录/取样本）。</param>
    /// <param name="destinationPath">目标目录（磁盘检测优先用它——写盘是瓶颈）。</param>
    /// <param name="password">密码（预测试列加密包需要）。</param>
    /// <param name="engineSupportsParallel">引擎是否支持并行（仅 ZipEngine 为 true）。</param>
    /// <param name="progress">进度回调（预测试期间上报状态文案）。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<int> ResolveAsync(
        int? callerDegree,
        ParallelExtractSettings settings,
        string archivePath,
        string destinationPath,
        string? password,
        bool engineSupportsParallel,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct)
    {
        // 步骤 1-3 同步短路（不进入 I/O 路径）
        if (callerDegree is > 0)
            return Math.Clamp(callerDegree.Value, 1, MaxDegree);
        if (!settings.EnableParallel)
            return 1;
        if (!settings.SmartParallel)
        {
            var manual = settings.ManualDegree > 0 ? settings.ManualDegree : Environment.ProcessorCount;
            return Math.Clamp(manual, 1, MaxDegree);
        }

        // 磁盘检测（优先目标目录——写盘是瓶颈；不可用回退压缩包路径）
        var diskType = DiskTypeDetector.GetDiskType(destinationPath);
        if (diskType == DiskType.Unknown)
            diskType = DiskTypeDetector.GetDiskType(archivePath);

        // 步骤 4：大包预测试
        long archiveSize = 0;
        try { archiveSize = new FileInfo(archivePath).Length; }
        catch (Exception ex)
        {
            // 仓库规则：禁止静默 catch，记录日志后按 0 处理
            CoreLog.Info($"ParallelDegreeResolver: cannot read archive size: {ex.Message}");
        }

        if (ParallelDegreeOptimizer.ShouldPreTest(archiveSize, settings.LargeArchivePreTest, engineSupportsParallel))
        {
            try
            {
                // 上报预测试状态（不显示百分比，CurrentFile 携带状态文案）
                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = "STATUS_TESTING_PARALLEL_DEGREE", // 调用方可替换为本地化文案
                    PercentComplete = 0
                });

                var optimal = await ParallelDegreeOptimizer.FindOptimalDegreeAsync(
                    archivePath, destinationPath, password, progress, ct);
                if (optimal > 0)
                {
                    CoreLog.Info($"ParallelDegreeResolver: pre-test optimal degree = {optimal}");
                    return Math.Clamp(optimal, 1, MaxDegree);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // 用户取消直接向上传播
            }
            catch (Exception ex)
            {
                CoreLog.Info($"ParallelDegreeResolver: pre-test failed, fallback to disk type: {ex.Message}");
            }
        }

        // 步骤 5：磁盘类型默认值
        return Resolve(callerDegree: null, settings, diskType, archiveSize);
    }
}
