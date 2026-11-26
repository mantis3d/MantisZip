using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Avalonia UI layer wrapper for archive extraction.
/// Uses <see cref="ArchiveEngineFactory"/> to resolve the appropriate engine
/// and delegates to <see cref="IArchiveEngine.ExtractAsync"/> with progress reporting.
/// </summary>
public class ExtractService
{
    /// <summary>
    /// Extract an archive to the specified destination path with progress reporting.
    /// </summary>
    /// <param name="archivePath">Full path to the archive file.</param>
    /// <param name="destinationPath">Directory to extract into.</param>
    /// <param name="password">Optional archive password.</param>
    /// <param name="progress">Optional progress reporter for <see cref="ArchiveProgress"/> updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="options">Optional extract options (conflict action, conflict resolver, etc.).
    /// When null, existing files are silently overwritten.</param>
    /// <exception cref="InvalidOperationException">Thrown when the archive format is not supported.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the archive file does not exist.</exception>
    public async Task ExtractAsync(
        string archivePath,
        string destinationPath,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken ct = default,
        ArchiveOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("Archive path cannot be null or empty.", nameof(archivePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path cannot be null or empty.", nameof(destinationPath));
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive file not found.", archivePath);

        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null)
            throw new InvalidOperationException(
                $"Unsupported archive format: {Path.GetExtension(archivePath)}");

        // 叶子 1/5：密码弹窗兜底（本方法是 UI 层解压的公共叶子，弹窗只在此处出现一次，
        // 编排层 ExtractFlow 不再二次包装，避免双弹窗）。
        // 本服务无状态栏通道，提示走 no-op；CLI/主窗口各自的进度窗口或状态栏另行接线。
        // 旁路统计逐条目结果：引擎对单个条目失败只上报状态、不抛异常，
        // 不检查就会出现「界面显示解压完成、目标目录却一个文件都没有」的静默失败
        // （issue #91 的 GBK 密码包正是如此：密码错误被逐条目吞掉，最终 0 文件却报成功）。
        var tracker = new ExtractOutcomeTracker(progress);
        ExtractResult? result = null;
        var (outcome, _) = await PasswordRetryLoop.RunAsync(
            archivePath, password, engine,
            owner: null,
            setStatus: _ => { },
            attempt: async (pwd, token) =>
            {
                // 每轮重试先清零：上一轮的完成/失败计数不能带进本轮判定
                tracker.Reset();
                result = await engine.ExtractAsync(archivePath, destinationPath, pwd, tracker, token, options);
            },
            ct: ct);

        if (outcome == PasswordRetryOutcome.Cancelled)
            throw new PasswordRetryCancelledException(archivePath);
        // 损坏/无效：弹窗循环已判定继续重弹无意义 → 抛错走既有失败收尾，
        // 禁止落成功路径（否则调用方记录路径历史且 CLI 直解批处理会误删损坏源包）
        if (outcome == PasswordRetryOutcome.CorruptedOrInvalid)
            throw new InvalidDataException(LocalizationManager.T("Status_ArchiveCorrupted"));

        // 全条目失败 = 实质什么都没解出来：必须报错，绝不能落成功路径
        // （否则进度窗口显示「完成」、CLI 还会记录路径历史甚至删掉源包）。
        tracker.ThrowIfNothingWritten(result?.FailedEntries ?? 0);
    }
}
