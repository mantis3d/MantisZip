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

        var entry = archive.Entries.FindEntry(entryName);
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
        var entry = extractor.ArchiveFileData.FirstOrDefault(e => ArchivePath.Normalize(e.FileName) == entryName);
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
    
    /// <summary>
    /// 提取压缩包内条目的前 maxBytes 字节到内存。
    /// </summary>
    public static async Task<byte[]> ExtractHeadAsync(
        string archivePath, string entryName, int maxBytes,
        ArchiveFormat format, string? password = null,
        CancellationToken ct = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractHeadAsync: {archivePath} ! {entryName}, maxBytes={maxBytes}, format={format}");
        ct.ThrowIfCancellationRequested();

        return await Task.Run(async () =>
        {
            switch (format)
            {
                case ArchiveFormat.Zip:
                    return ExtractZipHeadToMemory(archivePath, entryName, maxBytes, password);

                case ArchiveFormat.SevenZip:
                case ArchiveFormat.Rar:
                    using (var extractor = string.IsNullOrEmpty(password)
                        ? new SharpSevenZipExtractor(archivePath)
                        : new SharpSevenZipExtractor(archivePath, password))
                    {
                        if (format == ArchiveFormat.SevenZip && IsSevenZipSolid(extractor))
                        {
                            CoreLog.Info("ExtractHeadAsync: 7z is solid, falling back to full temp extract");
                            return await ExtractHeadViaFullExtractAsync(archivePath, entryName, maxBytes, format, password, ct);
                        }
                        return ExtractSevenZipHeadToMemory(extractor, entryName, maxBytes);
                    }

                case ArchiveFormat.Tar:
                case ArchiveFormat.GZip:
                    return await ExtractHeadViaFullExtractAsync(archivePath, entryName, maxBytes, format, password, ct);

                default:
                    throw new NotSupportedException($"格式 {format} 不支持头部提取");
            }
        }, ct);
    }

    /// <summary>
    /// 提取头部 + 尾部各指定字节数。tailBytes=null 时仅返回 head。
    /// </summary>
    public static async Task<(byte[] head, byte[]? tail)> ExtractHeadTailAsync(
        string archivePath, string entryName, int headBytes, int? tailBytes = null,
        ArchiveFormat format = ArchiveFormat.Zip, string? password = null,
        CancellationToken ct = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractHeadTailAsync: {archivePath} ! {entryName}, head={headBytes}, tail={tailBytes}, format={format}");

        var head = await ExtractHeadAsync(archivePath, entryName, headBytes, format, password, ct);

        byte[]? tail = null;
        if (tailBytes.HasValue && tailBytes.Value > 0)
        {
            tail = await Task.Run(() =>
                ExtractTailSync(archivePath, entryName, tailBytes.Value, format, password), ct);
        }

        return (head, tail);
    }

    /// <summary>
    /// 从 ZIP 压缩包中提取条目的前 maxBytes 字节到内存。
    /// </summary>
    private static byte[] ExtractZipHeadToMemory(
        string archivePath, string entryName, int maxBytes, string? password)
    {
        using var archive = ZipEngine.OpenArchiveWithEncodingFallback(archivePath, password);
        var entry = archive.Entries.FindEntry(entryName);
        if (entry == null)
            throw new FileNotFoundException($"在压缩包中未找到条目: {entryName}");
        if (entry.IsDirectory)
            return [];

        using var ms = new MemoryStream();
        entry.WriteTo(ms);
        ms.Position = 0;
        byte[] result = new byte[Math.Min(maxBytes, (int)ms.Length)];
        ms.Read(result, 0, result.Length);
        return result;
    }

    /// <summary>
    /// 从 7z/RAR 压缩包中提取条目的前 maxBytes 字节到内存。
    /// </summary>
    private static byte[] ExtractSevenZipHeadToMemory(
        SharpSevenZipExtractor extractor, string entryName, int maxBytes)
    {
        var entry = extractor.ArchiveFileData.FirstOrDefault(e =>
            ArchivePath.Normalize(e.FileName) == entryName);
        if (entry.FileName == null)
            throw new FileNotFoundException($"在压缩包中未找到条目: {entryName}");
        if (entry.IsDirectory)
            return [];

        using var ms = new MemoryStream();
        extractor.ExtractFile(entry.Index, ms);
        ms.Position = 0;
        byte[] result = new byte[Math.Min(maxBytes, (int)ms.Length)];
        ms.Read(result, 0, result.Length);
        return result;
    }

    /// <summary>
    /// 检测 7z 压缩包是否使用固实压缩（Solid）。
    /// 固实时无法高效单独提取单个条目，需要降级为整体解压。
    /// </summary>
    private static bool IsSevenZipSolid(SharpSevenZipExtractor extractor)
    {
        try
        {
            return extractor.IsSolid;
        }
        catch (Exception ex) when (ex is InvalidOperationException or SharpSevenZipArchiveException)
        {
            CoreLog.Trace("IsSevenZipSolid: exception checking IsSolid, assuming solid: {0}", ex.Message);
            return true;
        }
    }

    /// <summary>
    /// 通过完整提取到临时文件的方式读取头部（用于固实 7z 或 Tar/Gz）。
    /// </summary>
    private static async Task<byte[]> ExtractHeadViaFullExtractAsync(
        string archivePath, string entryName, int maxBytes,
        ArchiveFormat format, string? password, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "HeadExtract");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, Guid.NewGuid().ToString());
        try
        {
            await ExtractEntryAsync(archivePath, entryName, tempFile, format, password, ct);
            var fileInfo = new FileInfo(tempFile);
            int bytesToRead = (int)Math.Min(maxBytes, fileInfo.Length);
            byte[] result = new byte[bytesToRead];
            using (var fs = File.OpenRead(tempFile))
                await fs.ReadExactlyAsync(result, 0, bytesToRead, ct);
            return result;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>
    /// 同步提取尾部数据（仅支持可随机访问的格式）。
    /// </summary>
    private static byte[]? ExtractTailSync(
        string archivePath, string entryName, int tailBytes,
        ArchiveFormat format, string? password)
    {
        const long MaxTailExtractSize = 10 * 1024 * 1024; // 10 MB

        switch (format)
        {
            case ArchiveFormat.Zip:
            {
                using var archive = ZipEngine.OpenArchiveWithEncodingFallback(archivePath, password);
                var entry = archive.Entries.FindEntry(entryName);
                if (entry == null || entry.IsDirectory)
                    return null;

                // 只对小条目提取尾部（大于 10MB 的跳过）
                if (entry.Size > MaxTailExtractSize)
                    return null;

                using var ms = new MemoryStream();
                entry.WriteTo(ms);
                return ExtractTailFromBuffer(ms.ToArray(), tailBytes);
            }

            case ArchiveFormat.SevenZip:
            case ArchiveFormat.Rar:
            {
                using var extractor = string.IsNullOrEmpty(password)
                    ? new SharpSevenZipExtractor(archivePath)
                    : new SharpSevenZipExtractor(archivePath, password);

                // 固实 7z 不提取尾部
                if (format == ArchiveFormat.SevenZip && IsSevenZipSolid(extractor))
                    return null;

                var entry = extractor.ArchiveFileData.FirstOrDefault(e =>
                    ArchivePath.Normalize(e.FileName) == entryName);
                if (entry.FileName == null || entry.IsDirectory)
                    return null;

                using var ms = new MemoryStream();
                extractor.ExtractFile(entry.Index, ms);
                return ExtractTailFromBuffer(ms.ToArray(), tailBytes);
            }

            case ArchiveFormat.Tar:
            case ArchiveFormat.GZip:
            default:
                // 流式格式不支持随机访问尾部
                return null;
        }
    }

    /// <summary>
    /// 从完整缓冲区中提取尾部 N 字节。
    /// </summary>
    private static byte[]? ExtractTailFromBuffer(byte[] buffer, int tailBytes)
    {
        if (buffer.Length == 0)
            return null;

        int take = Math.Min(tailBytes, buffer.Length);
        var tail = new byte[take];
        Array.Copy(buffer, buffer.Length - take, tail, 0, take);
        return tail;
    }

    /// <summary>
    /// 在字节缓冲区中查找指定类型的 ISOBMFF Box。
    /// ISOBMFF Box 格式: [4 bytes size][4 bytes type][payload...]
    /// </summary>
    /// <param name="data">字节缓冲区（通常是 MP4 文件的头部或尾部）</param>
    /// <param name="boxType">要查找的 box 类型，如 "moov", "mvhd", "tkhd"</param>
    /// <param name="startOffset">搜索起始偏移</param>
    /// <returns>找到的 box 中 payload 的起始偏移（即 type 后的位置），未找到返回 -1</returns>
    private static int FindBox(byte[] data, string boxType, int startOffset = 0)
    {
        int typeBytes = BitConverter.ToInt32(
            new[] { (byte)boxType[0], (byte)boxType[1], (byte)boxType[2], (byte)boxType[3] }, 0);

        for (int i = startOffset; i <= data.Length - 8; i++)
        {
            int boxSize = (data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3];
            int type = (data[i + 4] << 24) | (data[i + 5] << 16) | (data[i + 6] << 8) | data[i + 7];

            if (type == typeBytes)
            {
                return i + 8; // return offset to box payload (after size + type = 8 bytes)
            }

            // Skip to next box
            if (boxSize < 8) break; // invalid size
            i += boxSize - 1; // -1 because loop increments
        }

        return -1;
    }

    /// <summary>
    /// 从 mvhd box 中解析时长（秒）。
    /// mvhd box payload structure (version 0, 32-bit timescale/duration):
    ///   [1 byte version][3 bytes flags][4 bytes creation time][4 bytes modification time]
    ///   [4 bytes timescale][4 bytes duration][...]
    /// mvhd box payload structure (version 1, 64-bit):
    ///   [1 byte version][3 bytes flags][8 bytes creation time][8 bytes modification time]
    ///   [4 bytes timescale][8 bytes duration][...]
    /// </summary>
    /// <param name="data">包含 mvhd payload 的完整字节数组</param>
    /// <param name="offset">mvhd payload 在数组中的偏移</param>
    /// <returns>时长（秒），解析失败返回 null</returns>
    private static double? ParseMvhdDuration(byte[] data, int offset)
    {
        try
        {
            if (offset + 20 > data.Length) return null;

            int version = data[offset];
            if (version == 0)
            {
                // 32-bit version
                if (offset + 16 > data.Length) return null;
                int timescale = (data[offset + 12] << 24) | (data[offset + 13] << 16) |
                                (data[offset + 14] << 8) | data[offset + 15];
                int duration = (data[offset + 16] << 24) | (data[offset + 17] << 16) |
                               (data[offset + 18] << 8) | data[offset + 19];
                if (timescale > 0)
                    return (double)duration / timescale;
            }
            else if (version == 1)
            {
                // 64-bit version
                if (offset + 28 > data.Length) return null;
                int timescale = (data[offset + 20] << 24) | (data[offset + 21] << 16) |
                                (data[offset + 22] << 8) | data[offset + 23];
                long duration = ((long)data[offset + 24] << 56) | ((long)data[offset + 25] << 48) |
                                ((long)data[offset + 26] << 40) | ((long)data[offset + 27] << 32) |
                                ((long)data[offset + 28] << 24) | ((long)data[offset + 29] << 16) |
                                ((long)data[offset + 30] << 8) | data[offset + 31];
                if (timescale > 0)
                    return (double)duration / timescale;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 从 tkhd box 中解析视频分辨率（宽×高）。
    /// tkhd box payload structure (version 0):
    ///   [1 byte version][3 bytes flags][4 bytes creation time][4 bytes modification time]
    ///   [4 bytes track ID][4 bytes reserved][8 bytes duration][8 bytes reserved]
    ///   [4 bytes layer][4 bytes alternate group][4 bytes volume][8 bytes reserved]
    ///   [36 bytes matrix structure][4 bytes width][4 bytes height]
    ///   Width/Height are fixed-point 16.16 values at offset 76 from payload start (version 0)
    ///   or offset 84 from payload start (version 1, has 64-bit timestamps)
    /// </summary>
    /// <param name="data">包含 tkhd payload 的完整字节数组</param>
    /// <param name="offset">tkhd payload 在数组中的偏移</param>
    /// <returns>(宽, 高) 元组，解析失败返回 null</returns>
    private static (int width, int height)? ParseTkhdResolution(byte[] data, int offset)
    {
        try
        {
            if (offset + 4 > data.Length) return null;
            int version = data[offset];

            // Width/Height are at different offsets depending on version
            // Version 0: 32-bit timestamps -> width at offset 76 from payload start
            // Version 1: 64-bit timestamps -> width at offset 84 from payload start
            int widthOffset = (version == 1) ? offset + 84 : offset + 76;

            if (widthOffset + 8 > data.Length) return null;

            // Width and height are 16.16 fixed-point numbers
            int width = (data[widthOffset] << 24) | (data[widthOffset + 1] << 16) |
                        (data[widthOffset + 2] << 8) | data[widthOffset + 3];
            int height = (data[widthOffset + 4] << 24) | (data[widthOffset + 5] << 16) |
                         (data[widthOffset + 6] << 8) | data[widthOffset + 7];

            // Convert from 16.16 fixed-point to integer
            width >>= 16;
            height >>= 16;

            if (width > 0 && height > 0)
                return (width, height);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 尝试从 MP4 文件的尾部数据中解析元数据（时长、分辨率）。
    /// 如果 tail 为 null 或无法解析，返回 null。
    /// </summary>
    public static (double? duration, int? width, int? height)? TryParseMp4TailMetadata(byte[] tail)
    {
        if (tail == null || tail.Length < 8)
            return null;

        // Find moov box in tail
        int moovPayloadOffset = FindBox(tail, "moov");
        if (moovPayloadOffset < 0)
            return null;

        // Find mvhd inside moov
        int mvhdPayloadOffset = FindBox(tail, "mvhd", moovPayloadOffset);
        double? duration = mvhdPayloadOffset >= 0
            ? ParseMvhdDuration(tail, mvhdPayloadOffset)
            : null;

        // Find tkhd inside moov
        int tkhdPayloadOffset = FindBox(tail, "tkhd", moovPayloadOffset);
        (int w, int h)? resolution = tkhdPayloadOffset >= 0
            ? ParseTkhdResolution(tail, tkhdPayloadOffset)
            : null;

        if (duration == null && resolution == null)
            return null;

        return (duration, resolution?.w, resolution?.h);
    }
}
