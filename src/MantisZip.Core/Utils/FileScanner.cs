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
    /// <param name="sourcePaths">源路径集合（文件或目录）。</param>
    /// <param name="progress">扫描进度回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="whitelist">文件白名单（绝对路径，大小写不敏感）；null = 收集全部。</param>
    /// <param name="rootMode">
    /// 「包内根目录」四态（见 <see cref="RootDirectoryMode"/>）；默认 <see cref="RootDirectoryMode.Original"/>
    /// = 与引入本功能前逐字节一致。
    /// </param>
    /// <param name="wrapRoot">加一层根目录时使用的根名；空 = 由引擎回落到压缩包文件名。</param>
    /// <param name="archivePath">
    /// 目标压缩包路径：<paramref name="wrapRoot"/> 为空时用它去扩展名当根名。
    /// 传 null 会回落到 <c>"archive"</c> —— 预览树与实解就会不一致，因此引擎侧**必须**传。
    /// </param>
    public static (List<(string FullPath, string RelativePath)> Files, long TotalBytes) CollectFiles(
        string[] sourcePaths,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? whitelist = null,
        RootDirectoryMode rootMode = RootDirectoryMode.Original,
        string? wrapRoot = null,
        string? archivePath = null)
    {
        var files = new List<(string FullPath, string RelativePath)>();
        long totalBytes = 0;
        var lastReportTime = DateTime.Now;
        var reportInterval = TimeSpan.FromMilliseconds(100);

        // 「包内根目录」四态：一次判定，逐条目套用（②剥根 / ③④加根 / ①原样）。
        // 剥根的候选段 = 唯一那个目录源的叶子名；加根段来自调用方给的根名。
        var hasSingleSourceDir = ArchiveRootPolicy.HasSingleSourceDirectory(sourcePaths);
        var adjustment = ArchiveRootPolicy.Resolve(rootMode, hasSingleSourceDir);
        var stripRoot = adjustment.Strip && hasSingleSourceDir
            ? ArchiveRootPolicy.GetSourceStripName(sourcePaths[0])
            : null;
        var effectiveWrap = adjustment.Wrap ? ArchiveRootPolicy.GetWrapName(wrapRoot, archivePath) : null;
        if (adjustment.IsActive)
        {
            CoreLog.Info($"FileScanner.CollectFiles: rootMode={rootMode}, strip='{stripRoot}', wrap='{effectiveWrap}'");
        }

        CoreLog.Info($"FileScanner.CollectFiles: scanning {sourcePaths.Length} source paths, whitelist={(whitelist != null ? $"{whitelist.Count} files" : "null")}");

        foreach (var sourcePath in sourcePaths)
        {
            CoreLog.Info($"FileScanner.CollectFiles: scanning '{sourcePath}' ({(Directory.Exists(sourcePath) ? "dir" : File.Exists(sourcePath) ? "file" : "missing")})");
            if (Directory.Exists(sourcePath))
            {
                var dirName = Path.GetFileName(sourcePath);
                foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (whitelist != null && !whitelist.Contains(file))
                        continue;
                    var relativePath = Path.Combine(dirName, Path.GetRelativePath(sourcePath, file));
                    relativePath = ArchiveRootPolicy.Apply(relativePath, stripRoot, effectiveWrap) ?? relativePath;
                    files.Add((file, relativePath));

                    // 累计总大小
                    try { totalBytes += new FileInfo(file).Length; } catch { /* 跳过无法读取的文件 */ }

                    // 每 100ms 报告进度
                    ReportScanProgress(progress, relativePath, files.Count, totalBytes, ref lastReportTime, reportInterval);
                }
            }
            else if (File.Exists(sourcePath))
            {
                if (whitelist != null && !whitelist.Contains(sourcePath))
                    continue;
                var singleRelative = ArchiveRootPolicy.Apply(Path.GetFileName(sourcePath), null, effectiveWrap)
                    ?? Path.GetFileName(sourcePath);
                files.Add((sourcePath, singleRelative));
                try { totalBytes += new FileInfo(sourcePath).Length; } catch { /* 跳过无法读取的文件 */ }
            }
        }

        CoreLog.Info($"FileScanner.CollectFiles: done, {files.Count} files, {totalBytes} bytes");
        return (files, totalBytes);
    }

    private static void ReportScanProgress(IProgress<ArchiveProgress>? progress, string currentFile, int totalFound, long scannedBytes, ref DateTime lastReportTime, TimeSpan interval)
    {
        var now = DateTime.Now;
        if (now - lastReportTime >= interval)
        {
            // 扫描阶段：总数未知 → PercentComplete 恒 0（UI 据 Phase 走不定进度条）；
            // TotalFiles 承载"已发现"数量，ScannedBytes 承载已发现文件的尺寸合计
            // （UI 在「扫描」统计卡里显示，不塞进状态行）。
            // CurrentFile 只放相对路径本身（不拼中文前缀，规则 13：文案一律走本地化）。
            progress?.Report(new ArchiveProgress
            {
                Phase = ArchivePhase.Scanning,
                CurrentFile = currentFile,
                TotalFiles = totalFound,
                ScannedBytes = scannedBytes,
                PercentComplete = 0,
                FilePercentComplete = 0
            });
            lastReportTime = now;
        }
    }
}
