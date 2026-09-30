namespace MantisZip.Core.Utils;

/// <summary>进度窗口显示计算：路径分离、总进度、时长格式化。
/// 纯函数，不依赖任何 UI 框架；文案格式化由 UI 层经 LocalizationManager 完成。</summary>
public static class ProgressDisplayCalculator
{
    // 引擎 CurrentFile 可能携带的历史硬编码前缀（防御性剥离用数据，非用户可见文案）
    private static readonly string[] EnginePrefixes =
        ["正在压缩: ", "正在解压: ", "Compressing: ", "Extracting: "];

    /// <summary>剥离引擎 CurrentFile 可能携带的硬编码状态前缀（防御性；T4 已在源头清除）。</summary>
    public static string StripEnginePrefix(string? currentFile)
    {
        if (string.IsNullOrEmpty(currentFile)) return currentFile ?? string.Empty;
        foreach (var p in EnginePrefixes)
            if (currentFile.StartsWith(p, StringComparison.Ordinal))
                return currentFile[p.Length..];
        return currentFile;
    }

    /// <summary>路径/文件名分离（兼容 ZIP 条目 '/' 分隔符；已先剥前缀）。</summary>
    public static (string Dir, string FileName) SplitFilePath(string? currentFile)
    {
        var raw = StripEnginePrefix(currentFile);
        if (string.IsNullOrEmpty(raw)) return (string.Empty, string.Empty);
        var trimmed = raw.Replace('\\', '/').TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        return idx < 0 ? (string.Empty, trimmed)
                       : (trimmed[..idx], trimmed[(idx + 1)..]);
    }

    /// <summary>总进度 = (已完成档案数 + 当前档案百分比/100) / 总档案数。</summary>
    public static double ComputeOverallPercent(int completedArchives, double currentPercent, int totalArchives)
    {
        if (totalArchives <= 0) return Math.Clamp(currentPercent, 0, 100);
        var frac = Math.Clamp(currentPercent, 0, 100) / 100.0;
        return Math.Clamp((completedArchives + frac) / totalArchives * 100.0, 0, 100);
    }

    /// <summary>时长格式化（文化中性：1:02:03；>24h 启用天段）。负值归零。</summary>
    public static string FormatDuration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 24
            ? t.ToString(@"d\.hh\:mm\:ss")
            : t.ToString(@"h\:mm\:ss");
    }
}

/// <summary>速度/ETA 追踪器：EMA 平滑；批次切换必须重置字节基线（ETA 跨批守卫）。</summary>
public sealed class ProgressSpeedTracker
{
    private long _archiveStartBytes;   // 当前档案开始时的累计字节（跨批基线）
    private long _lastSampleBytes;
    private DateTime _lastSampleTime = DateTime.MinValue;
    private double _emaBytesPerSecond;

    /// <summary>批次/档案切换时调用：重置字节基线，防止上一档案速度污染 ETA。</summary>
    public void OnArchiveSwitch(long totalProcessedBytesBeforeArchive, DateTime now)
    {
        _archiveStartBytes = totalProcessedBytesBeforeArchive;
        _lastSampleBytes = totalProcessedBytesBeforeArchive;
        _lastSampleTime = now;
        _emaBytesPerSecond = 0;
    }

    /// <summary>采样当前累计字节，返回平滑速度（bytes/s；0 = 无效样本）。</summary>
    public double RecordSample(long totalProcessedBytes, DateTime now)
    {
        if (_lastSampleTime == DateTime.MinValue)
        {
            _lastSampleTime = now;
            _lastSampleBytes = totalProcessedBytes;
            return _emaBytesPerSecond;
        }
        var dt = (now - _lastSampleTime).TotalSeconds;
        if (dt < 0.1) return _emaBytesPerSecond;   // 100ms 节流
        var delta = totalProcessedBytes - _lastSampleBytes;
        if (delta < 0) { OnArchiveSwitch(totalProcessedBytes, now); return 0; }  // 计数回退(换档案)→重置
        var inst = delta / dt;
        _emaBytesPerSecond = _emaBytesPerSecond <= 0 ? inst : _emaBytesPerSecond * 0.7 + inst * 0.3;
        _lastSampleBytes = totalProcessedBytes;
        _lastSampleTime = now;
        return _emaBytesPerSecond;
    }

    /// <summary>ETA 秒数（null = 速度无效；0 = 已完成）。</summary>
    public double? ComputeEtaSeconds(long processedInArchive, long totalInArchive)
    {
        if (_emaBytesPerSecond <= 0 || totalInArchive <= 0) return null;
        var remain = totalInArchive - processedInArchive;
        return remain <= 0 ? 0 : remain / _emaBytesPerSecond;
    }

    /// <summary>当前档案内已处理字节（相对本档案开始，clamp ≥ 0）。</summary>
    public long ProcessedInArchive(long totalProcessedBytes) =>
        Math.Max(0, totalProcessedBytes - _archiveStartBytes);
}
