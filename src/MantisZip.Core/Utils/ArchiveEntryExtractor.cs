using System.Diagnostics;
using System.IO;
using System.Linq;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SharpSevenZip;

namespace MantisZip.Core.Utils;

/// <summary>
/// 从压缩包中提取单个条目到文件，用于预览等场景
/// </summary>
public static class ArchiveEntryExtractor
{
    /// <summary>
    /// 将压缩包中的指定条目提取到目标文件
    /// </summary>
    public static Task ExtractEntryAsync(
        string archivePath,
        string entryName,
        string outputPath,
        ArchiveFormat format,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractEntryAsync: {archivePath} ! {entryName} -> {outputPath}, format={format}, password={(password != null ? "***" : "null")}");
        var sw = Stopwatch.StartNew();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (format)
            {
                case ArchiveFormat.Zip:
                    ExtractZipEntry(archivePath, entryName, outputPath, password);
                    break;

                case ArchiveFormat.SevenZip:
                case ArchiveFormat.Rar:
                    ExtractSevenZipEntry(archivePath, entryName, outputPath, password);
                    break;

                case ArchiveFormat.Tar:
                case ArchiveFormat.GZip:
                    ExtractTarGzEntry(archivePath, entryName, outputPath);
                    break;

                default:
                    CoreLog.Info($"ExtractEntryAsync: format {format} not supported for single-entry extract");
                    throw new NotSupportedException($"格式 {format} 不支持单文件预览提取");
            }

            CoreLog.Info($"ExtractEntryAsync: done, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken);
        // Note: CoreLog.Exit() not reached on exception path; OK for DEBUG-only logging
    }

    private static void ExtractZipEntry(string archivePath, string entryName, string outputPath, string? password)
    {
        CoreLog.Info($"ExtractZipEntry: archive={archivePath}, entry={entryName}");

        // 最终路径安全检查：规范化后验证无路径穿越
        ValidateOutputPath(outputPath);

        // 使用与 ZipEngine.ListEntriesAsync 相同的编码回退逻辑，
        // 确保 GBK/CP437 编码的遗留 ZIP 也能正确匹配条目名
        using var archive = ZipEngine.OpenArchiveWithEncodingFallback(archivePath, password);

        var entry = archive.Entries.FirstOrDefault(e => e.Key == entryName);
        if (entry == null)
        {
            throw new FileNotFoundException($"在压缩包中未找到条目: {entryName}");
        }

        if (entry.IsDirectory)
            return;

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using (var outStream = File.Open(outputPath, FileMode.Create, FileAccess.Write))
        {
            entry.WriteTo(outStream);
        }
        CoreLog.Info($"ExtractZipEntry: done");
    }

    private static void ExtractSevenZipEntry(string archivePath, string entryName, string outputPath, string? password)
    {
        CoreLog.Info($"ExtractSevenZipEntry: archive={archivePath}, entry={entryName}, password={(password != null ? "***" : "null")}");

        // 最终路径安全检查：规范化后验证无路径穿越
        ValidateOutputPath(outputPath);
        using var extractor = string.IsNullOrEmpty(password)
            ? new SharpSevenZipExtractor(archivePath)
            : new SharpSevenZipExtractor(archivePath, password);
        // 统一路径分隔符为 /（RAR 文件可能使用 \），与 SevenZipEngine.ListEntriesAsync 保持一致
        var entry = extractor.ArchiveFileData.FirstOrDefault(e => e.FileName.Replace('\\', '/') == entryName);
        if (entry.FileName == null)
        {
            throw new FileNotFoundException($"在压缩包中未找到条目: {entryName}");
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        extractor.ExtractFile(entry.Index, fileStream);
        CoreLog.Info($"ExtractSevenZipEntry: done");
    }

    private static void ExtractTarGzEntry(string archivePath, string entryName, string outputPath)
    {
        CoreLog.Info($"ExtractTarGzEntry: archive={archivePath}, entry={entryName}");

        // 最终路径安全检查
        ValidateOutputPath(outputPath);

        using var inputStream = File.OpenRead(archivePath);
        using var reader = SharpCompress.Readers.Tar.TarReader.OpenReader(inputStream, new ReaderOptions { LookForHeader = true });
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;
            if (entry.IsDirectory) continue;
            if (entry.Key == entryName)
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                using var outStream = File.Create(outputPath);
                using var entryStream = reader.OpenEntryStream();
                entryStream.CopyTo(outStream);
                CoreLog.Info($"ExtractTarGzEntry: done");
                return;
            }
        }
        throw new FileNotFoundException($"在压缩包中未找到条目: {entryName}");
    }

    /// <summary>
    /// 最终路径安全检查：规范化后验证输出路径不包含路径穿越攻击 (Zip Slip)。
    /// 由 ExtractZipEntry / ExtractSevenZipEntry 在写入前调用。
    /// 注意：此检查为防御纵深；调用方（UI）应已通过 SanitizeEntryPath + GetSafePath 确保路径安全。
    /// </summary>
    private static void ValidateOutputPath(string outputPath)
    {
        var normalized = Path.GetFullPath(outputPath);
        // 检查规范化后的路径是否仍包含 ".." 段（Path.GetFullPath 一般会解析，
        // 但作为防御纵深仍检查之；主保护在调用方的 SanitizeEntryPath + GetSafePath）
        var segments = normalized.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(s => s == ".."))
            throw new InvalidOperationException($"输出路径包含非法路径穿越: {outputPath}");
    }
}
