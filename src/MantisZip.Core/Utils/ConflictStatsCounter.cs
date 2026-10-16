using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>解压冲突统计（Interlocked 线程安全；ZIP 并行批次内多线程共享同一实例）。</summary>
public sealed class ConflictStatsCounter
{
    private int _skipped, _overwritten, _failed;

    /// <summary>记录跳过文件（ResolvePathAsync 返回 null，策略=跳过/取消）。</summary>
    public void RecordSkipped() => Interlocked.Increment(ref _skipped);

    /// <summary>记录覆盖已有文件（existedBefore 且解析路径与原路径一致）。</summary>
    public void RecordOverwritten() => Interlocked.Increment(ref _overwritten);

    /// <summary>记录条目处理失败（既有 per-entry catch 内调用）。</summary>
    public void RecordFailed() => Interlocked.Increment(ref _failed);

    /// <summary>无锁快照（三字段均为线程安全读取的 int，锁外调用安全）。</summary>
    public (int Skipped, int Overwritten, int Failed) Snapshot =>
        (_skipped, _overwritten, _failed);

    /// <summary>把当前统计写入进度对象并返回之（供 progress?.Report(...) 链式调用，避免每处快照局部变量）。</summary>
    public ArchiveProgress ApplyTo(ArchiveProgress progress)
    {
        progress.SkippedFiles = _skipped;
        progress.OverwrittenFiles = _overwritten;
        progress.FailedFiles = _failed;
        return progress;
    }

    /// <summary>清零（单测/复用场景）。</summary>
    public void Reset()
    {
        _skipped = 0;
        _overwritten = 0;
        _failed = 0;
    }
}
