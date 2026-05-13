using System.Diagnostics;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using SevenZipExtractor;

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
                    ExtractSevenZipEntry(archivePath, entryName, outputPath, password);
                    break;

                case ArchiveFormat.Tar:
                case ArchiveFormat.GZip:
                case ArchiveFormat.Rar:
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
        using var zipFile = new ZipFile(archivePath);
        if (!string.IsNullOrEmpty(password))
        {
            zipFile.Password = password;
        }

        var entry = zipFile.GetEntry(entryName);
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

        using var inStream = zipFile.GetInputStream(entry);
        using var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        inStream.CopyTo(outStream);
        CoreLog.Info($"ExtractZipEntry: done");
    }

    private static void ExtractSevenZipEntry(string archivePath, string entryName, string outputPath, string? password)
    {
        CoreLog.Info($"ExtractSevenZipEntry: archive={archivePath}, entry={entryName}, password={(password != null ? "***" : "null")}");
        using var archiveFile = string.IsNullOrEmpty(password)
            ? new ArchiveFile(archivePath)
            : new ArchiveFile(archivePath, password);
        var entry = archiveFile.Entries.FirstOrDefault(e => e.FileName == entryName);
        if (entry == null)
        {
            throw new FileNotFoundException($"在压缩包中未找到条目: {entryName}");
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        entry.Extract(fileStream);
        CoreLog.Info($"ExtractSevenZipEntry: done");
    }
}
