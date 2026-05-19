using System.IO;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 文件扫描工具：递归收集待打包的文件列表及总大小。
/// 供 ZipEngine/TarGzEngine 共享，消除重复的文件枚举逻辑。
/// </summary>
internal static class FileScanner
{
    /// <summary>
    /// 从源路径集合中收集所有文件（递归目录），返回相对路径映射及字节总数。
    /// 边扫描边通过 <paramref name="progress"/> 报告进度（每 100ms 节流）。
    /// </summary>
    public static (List<(string FullPath, string RelativePath)> Files, long TotalBytes) CollectFiles(
        string[] sourcePaths,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = new List<(string FullPath, string RelativePath)>();
        long totalBytes = 0;
        var lastReportTime = DateTime.Now;
        var reportInterval = TimeSpan.FromMilliseconds(100);

        foreach (var sourcePath in sourcePaths)
        {
            if (Directory.Exists(sourcePath))
            {
                var dirName = Path.GetFileName(sourcePath);
                foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativePath = Path.Combine(dirName, Path.GetRelativePath(sourcePath, file));
                    files.Add((file, relativePath));

                    // 累计总大小
                    try { totalBytes += new FileInfo(file).Length; } catch { /* 跳过无法读取的文件 */ }

                    // 每 100ms 报告进度
                    ReportScanProgress(progress, relativePath, files.Count, ref lastReportTime, reportInterval);
                }
            }
            else if (File.Exists(sourcePath))
            {
                files.Add((sourcePath, Path.GetFileName(sourcePath)));
                try { totalBytes += new FileInfo(sourcePath).Length; } catch { /* 跳过无法读取的文件 */ }
            }
        }

        return (files, totalBytes);
    }

    private static void ReportScanProgress(IProgress<ArchiveProgress>? progress, string currentFile, int totalFound, ref DateTime lastReportTime, TimeSpan interval)
    {
        var now = DateTime.Now;
        if (now - lastReportTime >= interval)
        {
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = $"正在扫描: {currentFile} ({totalFound} 个文件)",
                PercentComplete = 0,
                FilePercentComplete = 0
            });
            lastReportTime = now;
        }
    }
}
