using System.Diagnostics;
using System.IO;
using System.Linq;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using SharpCompress.Archives;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Readers;
using SharpSevenZip;
using SharpSevenZip.Exceptions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 从压缩包中提取单个条目到文件，用于预览等场景
/// </summary>
public static class ArchiveEntryExtractor
{
    /// <summary>
    /// 当加密文件名压缩包（EncryptHeaders=true）无密码时尝试读取条目头部，抛出此异常。
    /// 调用方（如 ClassifyPreviewByMagicAsync）可捕获并返回 PreviewType.NeedsPassword。
    /// </summary>
    public sealed class PasswordRequiredException : Exception
    {
        public PasswordRequiredException(string message) : base(message) { }
    }
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
                case ArchiveFormat.Iso:
                    ExtractSevenZipEntry(archivePath, entryName, outputPath, password);
                    break;

                case ArchiveFormat.Tar:
                    if (IsPlainGZipFile(archivePath))
                        ExtractGZipEntry(archivePath, entryName, outputPath);
                    else
                        ExtractTarGzEntry(archivePath, entryName, outputPath);
                    break;

                case ArchiveFormat.GZip:
                    ExtractGZipEntry(archivePath, entryName, outputPath);
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
        // 先确保 7z.dll 路径已配置：本工具类可能在引擎方法入口之前就触达 SharpSevenZip
        // （库是进程级单例，加载后无法换路径 → issue #52）
        SevenZipEngine.EnsureLibraryPath();
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

    /// <summary>
    /// 判断是否为纯 GZip 单文件（.gz，非 .tar.gz / .tgz）。
    /// 纯 GZip 无 tar 容器，TarReader 无法解析，需走 GZipStream 直接解压。
    /// 与 <see cref="TarGzEngine.ExtractAsync"/> 的 .gz 分支保持一致。
    /// </summary>
    private static bool IsPlainGZipFile(string archivePath)
    {
        var ext = Path.GetExtension(archivePath).ToLowerInvariant();
        return ext == ".gz" && !archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 提取纯 GZip 单文件（.gz）：将压缩包内容直接解压到输出文件。
    /// 条目名（解压后的文件名）仅用于匹配，实际内容来自整个 gzip 流。
    /// </summary>
    private static void ExtractGZipEntry(string archivePath, string entryName, string outputPath)
    {
        CoreLog.Info($"ExtractGZipEntry: archive={archivePath}, entry={entryName}");

        // 最终路径安全检查
        ValidateOutputPath(outputPath);

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var inputStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(inputStream, SharpCompress.Compressors.CompressionMode.Decompress);
        using var outStream = File.Create(outputPath);
        gzipStream.CopyTo(outStream);
        CoreLog.Info($"ExtractGZipEntry: done");
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
    ///
    /// <para>
    /// 内存策略：ZIP 走「读满 maxBytes 即停」；非固实 7z/RAR/ISO 走「截断写入」
    /// （<see cref="TruncatingWriteStream"/>，底层解压跑完但只保留前 maxBytes）；
    /// Tar/Gz 与固实 7z 走落盘慢路径（<see cref="ExtractHeadViaFullExtractAsync"/>）。
    /// 返回值长度 = min(maxBytes, 条目实际大小)，不会为 0 之外的空洞补齐。
    /// </para>
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
                case ArchiveFormat.Iso:
                    // 构造 extractor 前先配库路径（issue #52）
                    SevenZipEngine.EnsureLibraryPath();
                    using (var extractor = string.IsNullOrEmpty(password)
                        ? new SharpSevenZipExtractor(archivePath)
                        : new SharpSevenZipExtractor(archivePath, password))
                    {
                        if (format == ArchiveFormat.SevenZip && IsSevenZipSolid(extractor))
                        {
                            CoreLog.Info("ExtractHeadAsync: 7z is solid, falling back to full temp extract");
                            return await ExtractHeadViaFullExtractAsync(archivePath, entryName, maxBytes, format, password, ct);
                        }
                        // 检测加密文件名：EncryptHeaders=true 且无密码时无法读取条目数据
                        if (format == ArchiveFormat.SevenZip && string.IsNullOrEmpty(password) && IsSevenZipEncryptHeaders(extractor))
                        {
                            throw new PasswordRequiredException("加密文件名 7z 压缩包需要密码才能读取条目数据");
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
    /// 提取压缩包内单个条目的**完整内容**到内存（不是 head 截断）：给必须吃完整字节的消费方用
    /// （SkiaSharp 解码位图 / PdfPig 打开文档）。
    ///
    /// <para>
    /// 返回 <see cref="byte"/> 数组而不是 <see cref="Stream"/>，理由：
    /// ① 消费方接口需求是分叉的——SkiaSharp 要流（<c>SKBitmap.Decode</c> / <c>SKCodec.Create</c>），
    ///    PdfPig 有 <c>PdfDocument.Open(byte[], ParsingOptions)</c> 重载；<c>byte[]</c> 两边都满足
    ///    （要流时 <c>new MemoryStream(bytes, writable: false)</c> 包一层，零拷贝），
    ///    反过来 <c>MemoryStream → byte[]</c> 必须再整拷一次；
    /// ② <c>byte[]</c> 没有所有权与 Dispose 义务——PdfPig 的 <c>Open(Stream, …)</c> 约定由调用方
    ///    负责流的生命周期、文档本身不负责，返回 <c>MemoryStream</c> 会把「谁 dispose」变成三方扯皮；
    /// ③ 与 <see cref="ExtractHeadAsync"/> 的返回类型一致，head 的 LRU 可复用同一套容量/失效模型；
    /// ④ 拷贝成本有界：由调用方传入的 <paramref name="maxBytes"/> 兜住，一次拷贝可接受。
    /// </para>
    /// <para>
    /// 取数路径按格式分流（共同铁律：超限时**绝不**返回截断数据）：
    /// ZIP 真流式（<c>OpenEntryStream()</c>，读满上限后再探测 1 字节即判超限）；
    /// 非固实 7z/RAR/ISO 先按条目元数据判上限（超限**不启动解压**），否则用
    /// <see cref="TruncatingWriteStream"/> 兜底、事后 <c>DiscardedBytes &gt; 0</c> 即失败；
    /// tar/tar.gz 与固实 7z 先按元数据判上限，再走落盘兜底
    /// （<see cref="ExtractEntryToMemoryViaFullExtractAsync"/>，与 head 慢路径共用临时目录）；
    /// 纯 .gz 没有条目大小元数据，直接流式解压读满即停、不落盘。
    /// </para>
    /// <para>
    /// 上限**只来自参数**：本方法不出现预览常量、不引用 AppSettings / UI 类型，
    /// 保证 Core 的分层不被预览层污染。
    /// </para>
    /// </summary>
    /// <param name="archivePath">压缩包路径。</param>
    /// <param name="entryName">包内条目名（与 <see cref="ExtractHeadAsync"/> 同一命名规则）。</param>
    /// <param name="format">压缩格式。</param>
    /// <param name="maxBytes">允许返回的最大字节数，必须 &gt; 0；超限抛 <see cref="EntryTooLargeException"/>。</param>
    /// <param name="password">密码（仅加密压缩包需要）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>条目完整字节；目录条目返回空数组。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> ≤ 0。</exception>
    /// <exception cref="ArchiveEntryNotFoundException">包内没有该条目。</exception>
    /// <exception cref="EntryTooLargeException">条目实际大小超过 <paramref name="maxBytes"/>，
    /// 或超过单个 <c>byte[]</c> 的容量（<see cref="Array.MaxLength"/>）。</exception>
    /// <exception cref="PasswordRequiredException">加密文件名的 7z 且未提供密码。</exception>
    public static async Task<byte[]> ExtractEntryToMemoryAsync(
        string archivePath, string entryName, ArchiveFormat format, long maxBytes,
        string? password = null, CancellationToken ct = default)
    {
        CoreLog.Entry();
        if (maxBytes <= 0)
        {
            // 不静默返回空数组：调用方给错上限（0 / 负数）是编程错误，必须显式炸掉
            CoreLog.Info($"ExtractEntryToMemoryAsync: 拒绝 maxBytes={maxBytes}（必须 > 0）");
            throw new ArgumentOutOfRangeException(
                nameof(maxBytes), maxBytes, "maxBytes 必须大于 0");
        }
        CoreLog.Info($"ExtractEntryToMemoryAsync: format={format}, maxBytes={maxBytes}, entry={ArchivePath.GetFileName(entryName)}");
        ct.ThrowIfCancellationRequested();

        return await Task.Run(async () =>
        {
            switch (format)
            {
                case ArchiveFormat.Zip:
                    return ExtractZipEntryToMemory(archivePath, entryName, maxBytes, password, ct);

                case ArchiveFormat.SevenZip:
                case ArchiveFormat.Rar:
                case ArchiveFormat.Iso:
                    // 构造 extractor 前先配库路径（issue #52）
                    SevenZipEngine.EnsureLibraryPath();
                    using (var extractor = string.IsNullOrEmpty(password)
                        ? new SharpSevenZipExtractor(archivePath)
                        : new SharpSevenZipExtractor(archivePath, password))
                    {
                        // 加密文件名：无密码时连条目列表都读不出来，先按密码缺失失败（与 head 一致）
                        if (format == ArchiveFormat.SevenZip
                            && string.IsNullOrEmpty(password)
                            && IsSevenZipEncryptHeaders(extractor))
                        {
                            CoreLog.Info("ExtractEntryToMemoryAsync: 加密文件名 7z 且无密码，拒绝读取");
                            throw new PasswordRequiredException("加密文件名 7z 压缩包需要密码才能读取条目数据");
                        }

                        if (format == ArchiveFormat.SevenZip && IsSevenZipSolid(extractor))
                        {
                            // 固实块无法单独解一条：先按元数据判上限（超限绝不落盘，见 T7 约束），
                            // 再走整条目落盘兜底。
                            var solidEntry = FindSevenZipEntry(extractor, entryName);
                            if (solidEntry.IsDirectory)
                                return [];

                            long solidSize = ToActualSize(solidEntry.Size);
                            if (solidSize > maxBytes)
                            {
                                CoreLog.Info($"ExtractEntryToMemoryAsync: 固实 7z entry={ArchivePath.GetFileName(entryName)} 元数据 {solidSize} 字节 > 上限 {maxBytes}，不落盘直接拒绝");
                                throw new EntryTooLargeException(entryName, solidSize, maxBytes);
                            }

                            CoreLog.Info("ExtractEntryToMemoryAsync: 7z 固实，回落整条目落盘兜底");
                            return await ExtractEntryToMemoryViaFullExtractAsync(
                                archivePath, entryName, maxBytes, format, password, ct);
                        }

                        return ExtractSevenZipEntryToMemory(extractor, entryName, maxBytes);
                    }

                case ArchiveFormat.Tar:
                    if (IsPlainGZipFile(archivePath))
                        return ExtractGZipEntryToMemory(archivePath, entryName, maxBytes, ct);

                    // tar / tar.gz：先用 tar header 的 Size 判上限，超限时**不落盘**
                    // （把 2GB 条目写进 %TEMP% 是事故，而 header 大小就是解压后的真实大小）
                    var tarEntry = FindTarEntry(archivePath, entryName);
                    if (tarEntry.IsDirectory)
                        return [];
                    if (tarEntry.Size > maxBytes)
                    {
                        CoreLog.Info($"ExtractEntryToMemoryAsync: tar entry={ArchivePath.GetFileName(entryName)} 大小 {tarEntry.Size} 字节 > 上限 {maxBytes}，不落盘直接拒绝");
                        throw new EntryTooLargeException(entryName, tarEntry.Size, maxBytes);
                    }

                    CoreLog.Info("ExtractEntryToMemoryAsync: tar 是流式容器，回落整条目落盘兜底");
                    return await ExtractEntryToMemoryViaFullExtractAsync(
                        archivePath, entryName, maxBytes, format, password, ct);

                case ArchiveFormat.GZip:
                    return ExtractGZipEntryToMemory(archivePath, entryName, maxBytes, ct);

                default:
                    CoreLog.Info($"ExtractEntryToMemoryAsync: 格式 {format} 不支持读取整条目到内存");
                    throw new NotSupportedException($"格式 {format} 不支持读取整条目到内存");
            }
        }, ct);
    }

    /// <summary>
    /// 提取头部 + 尾部各指定字节数。tailBytes=null 时仅返回 head。
    ///
    /// <para>
    /// head 复用 <see cref="ExtractHeadAsync"/>（ZIP/7z 已流式）；tail 需要读到条目末尾，
    /// 仍为整条目解压（ZIP 分支受 10MB 上限保护，超过则返回 null）。
    /// </para>
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
    /// 从 ZIP 压缩包中提取条目的前 maxBytes 字节到内存（**真流式：读满即停**）。
    ///
    /// <para>
    /// 旧实现是 <c>entry.WriteTo(memoryStream)</c> 之后再截断——为了拿 4KB 头部，
    /// 会先把整个条目（预览场景可能是几百 MB）解压进内存，内存 O(条目大小)。
    /// 现在改用 <c>entry.OpenEntryStream()</c> 逐块读取，读满 maxBytes 立即停，
    /// 内存 O(maxBytes)，与条目大小无关。
    /// </para>
    /// <para>
    /// 注意：
    /// 1) 解压流是 SharpCompress 的裸流，<b>不校验 CRC</b>（见 ZipEngine.cs 的同类说明）——
    ///    head 本来就不读完条目，CRC 校验必然不通过，这里不需要也不应该校验；
    /// 2) 不能依赖流的 <c>Length</c>（不是所有解压流都支持），统一用循环 <c>Read</c>；
    /// 3) 密码沿用 <see cref="ZipEngine.OpenArchiveWithEncodingFallback"/> 在
    ///    <c>ReaderOptions.Password</c> 上设置的方式（与 <c>ExtractZipEntry</c> 完全一致），
    ///    entry 打开后会自行解密，无需再往 entry 上挂密码。
    /// </para>
    /// </summary>
    private static byte[] ExtractZipHeadToMemory(
        string archivePath, string entryName, int maxBytes, string? password)
    {
        if (maxBytes <= 0)
            return [];

        using var archive = ZipEngine.OpenArchiveWithEncodingFallback(archivePath, password);
        var entry = archive.Entries.FindEntry(entryName);
        if (entry == null)
            throw new FileNotFoundException($"在压缩包中未找到条目: {entryName}");
        if (entry.IsDirectory)
            return [];

        using var entryStream = entry.OpenEntryStream();
        return ReadHeadFromStream(entryStream, maxBytes);
    }

    /// <summary>
    /// 从任意只读流中读取前 maxBytes 字节；不足 maxBytes（条目本身更小）时返回实际长度。
    /// 不依赖 <see cref="Stream.Length"/>，逐块读、读满即停。
    /// </summary>
    private static byte[] ReadHeadFromStream(Stream stream, int maxBytes)
    {
        var buffer = new byte[maxBytes];
        int total = 0;
        while (total < maxBytes)
        {
            int read = stream.Read(buffer, total, maxBytes - total);
            if (read <= 0)
                break;
            total += read;
        }

        if (total == maxBytes)
            return buffer;

        var result = new byte[total];
        Array.Copy(buffer, result, total);
        return result;
    }

    /// <summary>
    /// 从 7z/RAR 压缩包中提取条目的前 maxBytes 字节到内存（**截断写入，避免整条目驻留内存**）。
    ///
    /// <para>
    /// SharpSevenZip 只提供「把条目完整解压到某个流」的入口，固实（solid）块甚至无法
    /// 中途停止，所以不能用 ZIP 那种「读满即停」。这里把目标流换成
    /// <see cref="TruncatingWriteStream"/>：底层照常解压完整个条目，但流只保留前
    /// maxBytes 字节、其余静默丢弃 → 内存固定为 maxBytes 量级。
    /// 旧实现写进 MemoryStream，同样存在内存 O(条目大小) 的问题。
    /// </para>
    /// </summary>
    private static byte[] ExtractSevenZipHeadToMemory(
        SharpSevenZipExtractor extractor, string entryName, int maxBytes)
    {
        if (maxBytes <= 0)
            return [];

        var entry = extractor.ArchiveFileData.FirstOrDefault(e =>
            ArchivePath.Normalize(e.FileName) == entryName);
        if (entry.FileName == null)
            throw new FileNotFoundException($"在压缩包中未找到条目: {entryName}");
        if (entry.IsDirectory)
            return [];

        using var truncating = new TruncatingWriteStream(maxBytes);
        extractor.ExtractFile(entry.Index, truncating);
        if (truncating.DiscardedBytes > 0)
        {
            CoreLog.Info($"ExtractSevenZipHeadToMemory: entry={entryName} 解压时丢弃 {truncating.DiscardedBytes} 字节（只保留前 {maxBytes}）");
        }
        return truncating.ToArray();
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
    /// 检测 7z 压缩包是否启用了加密文件名。
    /// 无密码时尝试访问 ArchiveFileData，若抛出异常则视为加密文件名。
    /// </summary>
    private static bool IsSevenZipEncryptHeaders(SharpSevenZipExtractor extractor)
    {
        try
        {
            // 尝试访问条目列表——加密文件名的 7z 在无密码时会抛出 SharpSevenZipArchiveException
            _ = extractor.ArchiveFileData.Count;
            return false;
        }
        catch (Exception ex) when (ex is SharpSevenZipArchiveException or InvalidOperationException)
        {
            CoreLog.Trace("IsSevenZipEncryptHeaders: exception accessing ArchiveFileData, assuming encrypted headers: {0}", ex.Message);
            return true;
        }
    }

    /// <summary>
    /// 通过完整提取到临时文件的方式读取头部（用于固实 7z 或 Tar/Gz）。
    ///
    /// <para>
    /// 这是头部提取的**慢路径**：Tar/Gz 是流式容器、固实 7z 无法单独解一个文件，
    /// 两者都只能先把整条目落盘再读前 maxBytes 字节，内存 O(maxBytes) 但磁盘/耗时 O(条目大小)。
    /// 非固实 7z 与 ZIP 已走「读满即停 / 截断写入」的快路径，不经过这里。
    /// </para>
    /// </summary>
    private static async Task<byte[]> ExtractHeadViaFullExtractAsync(
        string archivePath, string entryName, int maxBytes,
        ArchiveFormat format, string? password, CancellationToken ct)
    {
        string reason = format switch
        {
            ArchiveFormat.Tar or ArchiveFormat.GZip => "tar/gz 是流式容器，无法随机访问单条目",
            ArchiveFormat.SevenZip => "7z 固实压缩，无法单独解压单条目",
            _ => "该格式不支持流式 head 读取",
        };
        CoreLog.Info($"ExtractHeadViaFullExtractAsync: 走慢路径（{reason}），落盘整条目后取前 {maxBytes} 字节: {entryName}");

        Directory.CreateDirectory(HeadExtractTempDir);
        var tempFile = Path.Combine(HeadExtractTempDir, Guid.NewGuid().ToString());
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
    /// 落盘兜底共用的临时目录（head 慢路径 / 整条目读回）。集中一处，
    /// 便于「整条目读回」测试统一断言「超限时这里没有留下大文件」。
    /// </summary>
    private static string HeadExtractTempDir =>
        Path.Combine(Path.GetTempPath(), "MantisZip", "HeadExtract");

    /// <summary>
    /// 整条目落盘兜底（tar / tar.gz、固实 7z）：把整条目提取到临时文件再整体读回。
    /// 与 head 慢路径共用 <see cref="HeadExtractTempDir"/>，区别是读回**全部**字节而不是前 N 字节。
    ///
    /// <para>
    /// 调用方**必须先按条目元数据判上限**（tar header 的 Size / 7z 的 Size），
    /// 否则会把几 GB 的条目写进 %TEMP%。这里再按**真实落盘大小**校验一次：
    /// 元数据可能撒谎（伪造 header / 压缩炸弹），而这条路径绝不允许「截断后当成功」
    /// ——超限直接抛 <see cref="EntryTooLargeException"/>，临时文件由 finally 删除。
    /// </para>
    /// </summary>
    private static async Task<byte[]> ExtractEntryToMemoryViaFullExtractAsync(
        string archivePath, string entryName, long maxBytes,
        ArchiveFormat format, string? password, CancellationToken ct)
    {
        Directory.CreateDirectory(HeadExtractTempDir);
        var tempFile = Path.Combine(HeadExtractTempDir, Guid.NewGuid().ToString());
        try
        {
            await ExtractEntryAsync(archivePath, entryName, tempFile, format, password, ct);
            var fileInfo = new FileInfo(tempFile);

            if (fileInfo.Length > maxBytes)
            {
                CoreLog.Info($"ExtractEntryToMemoryViaFullExtractAsync: entry={ArchivePath.GetFileName(entryName)} 真实落盘 {fileInfo.Length} 字节 > 上限 {maxBytes}，拒绝返回截断数据");
                throw new EntryTooLargeException(entryName, fileInfo.Length, maxBytes);
            }

            if (fileInfo.Length > Array.MaxLength)
            {
                // 单个 byte[] 最多 Array.MaxLength 字节（约 2GB）；只有 maxBytes 允许更大时才会到这里
                CoreLog.Info($"ExtractEntryToMemoryViaFullExtractAsync: entry={ArchivePath.GetFileName(entryName)} 真实 {fileInfo.Length} 字节超过单个数组上限 {Array.MaxLength}");
                throw new EntryTooLargeException(entryName, fileInfo.Length, Array.MaxLength);
            }

            var result = new byte[fileInfo.Length];
            using (var fs = File.OpenRead(tempFile))
                await fs.ReadExactlyAsync(result, 0, result.Length, ct);
            return result;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>
    /// 从任意只读流读**整条**条目到内存：读到流结束为止，但一旦读满 <paramref name="maxBytes"/>
    /// 仍在出数据就判超限（<see cref="EntryTooLargeException"/>）。返回长度一定 ≤ maxBytes。
    ///
    /// <para>
    /// 内存 O(min(条目大小, maxBytes))：容量按 <paramref name="sizeHint"/>（元数据大小）与上限的
    /// 较小者一次性预分配，未知大小时从 64KB 起步倍增。不用 <see cref="MemoryStream"/>，
    /// 因为它的倍增会额外分配 2~3 倍缓冲、<c>ToArray()</c> 最后还要整拷一次。
    /// </para>
    /// <para>
    /// <paramref name="sizeHint"/> 只用于**预估容量**；是否超限一律按实际读到的字节数判定
    /// （central directory / tar header 都可能撒谎）。
    /// </para>
    /// </summary>
    private static byte[] ReadWholeEntryToMemory(
        Stream stream, string entryName, long maxBytes, long sizeHint, CancellationToken ct)
    {
        // 单个 byte[] 最多装 Array.MaxLength 字节；maxBytes 允许更大时以数组容量为硬上限
        long hardLimit = Math.Min(maxBytes, Array.MaxLength);

        long initialCapacity = sizeHint > 0
            ? Math.Min(sizeHint, hardLimit)
            : Math.Min(hardLimit, 64 * 1024);
        var buffer = new byte[initialCapacity];
        int total = 0;

        while (true)
        {
            if (total == buffer.Length)
            {
                if (buffer.Length >= hardLimit)
                {
                    // 读满上限后仍在出数据 ⇒ 超限；刚好等于上限时探测到 EOF 就正常返回
                    if (!HasMoreData(stream))
                        return buffer;

                    // 只有元数据明确大于上限时才敢报 ActualSize，否则诚实给 -1（元数据撒谎）
                    long actual = sizeHint > buffer.Length ? sizeHint : -1;
                    CoreLog.Info($"ReadWholeEntryToMemory: entry={ArchivePath.GetFileName(entryName)} 超过上限 {buffer.Length} 字节（元数据 {sizeHint}），拒绝返回截断数据");
                    throw new EntryTooLargeException(entryName, actual, hardLimit);
                }

                // 元数据大小正好等于当前容量时先探测 1 字节：准确的话省掉一次扩容 + 拷贝
                if (sizeHint > 0 && buffer.Length == sizeHint && !HasMoreData(stream))
                    return buffer;

                // 倍增但绝不越过 hardLimit（避免超分配）
                long grown = Math.Min(hardLimit, Math.Max(4096L, (long)buffer.Length * 2));
                Array.Resize(ref buffer, (int)grown);
            }

            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
                break;
            total += read;
            ct.ThrowIfCancellationRequested();
        }

        if (total == buffer.Length)
            return buffer;

        // 条目比上限小：剪掉预分配的尾巴（返回长度必须等于真实字节数）
        var result = new byte[total];
        Array.Copy(buffer, result, total);
        return result;
    }

    /// <summary>探测流里是否还有数据（读 1 字节）；用于区分「刚好等于上限」与「超限」。</summary>
    private static bool HasMoreData(Stream stream)
    {
        Span<byte> probe = stackalloc byte[1];
        return stream.Read(probe) > 0;
    }

    /// <summary>
    /// 从 ZIP 条目流式读出整条内容（**真流式：读满即停**）。
    ///
    /// <para>
    /// 不要改回 <c>entry.WriteTo(memoryStream)</c> 再校验长度——那等于先把整条目解压进内存，
    /// 正是本 API 要消灭的写法。密码沿用 <c>ReaderOptions.Password</c>（与
    /// <see cref="ZipEngine.OpenArchiveWithEncodingFallback"/> 一致），entry 打开后自行解密。
    /// </para>
    /// </summary>
    private static byte[] ExtractZipEntryToMemory(
        string archivePath, string entryName, long maxBytes, string? password, CancellationToken ct)
    {
        using var archive = ZipEngine.OpenArchiveWithEncodingFallback(archivePath, password);
        var entry = archive.Entries.FindEntry(entryName);
        if (entry == null)
        {
            CoreLog.Info($"ExtractZipEntryToMemory: entry={ArchivePath.GetFileName(entryName)} 不存在");
            throw new ArchiveEntryNotFoundException(entryName);
        }
        if (entry.IsDirectory)
            return [];

        using var entryStream = entry.OpenEntryStream();
        return ReadWholeEntryToMemory(entryStream, entryName, maxBytes, entry.Size, ct);
    }

    /// <summary>
    /// 从 7z/RAR/ISO 读出整条条目到内存：**先按元数据判上限**，再截断流兜底 + 事后校验。
    ///
    /// <para>
    /// SharpSevenZip 只提供「把条目完整解压到某个流」，固实块甚至无法中途停止，
    /// 所以先用 <c>ArchiveFileData</c> 的 <c>Size</c> 判上限：超限时**不启动解压**。
    /// 元数据不可全信（RAR/ISO 尤其），因此仍用 <see cref="TruncatingWriteStream"/> 兜底，
    /// 事后按 <c>DiscardedBytes</c> 判定：一旦有丢弃即视为超限失败，
    /// **绝不**把截断后的数据当成功返回。
    /// </para>
    /// </summary>
    private static byte[] ExtractSevenZipEntryToMemory(
        SharpSevenZipExtractor extractor, string entryName, long maxBytes)
    {
        var entry = FindSevenZipEntry(extractor, entryName);
        if (entry.IsDirectory)
            return [];

        long metadataSize = ToActualSize(entry.Size);
        if (metadataSize > maxBytes)
        {
            CoreLog.Info($"ExtractSevenZipEntryToMemory: entry={ArchivePath.GetFileName(entryName)} 元数据 {metadataSize} 字节 > 上限 {maxBytes}，不解压直接拒绝");
            throw new EntryTooLargeException(entryName, metadataSize, maxBytes);
        }

        // 截断流容量是 int，取上限与数组容量上限的较小值
        int capacity = (int)Math.Min(maxBytes, Array.MaxLength);
        using var truncating = new TruncatingWriteStream(capacity);
        extractor.ExtractFile(entry.Index, truncating);
        if (truncating.DiscardedBytes > 0)
        {
            long actual = truncating.WrittenBytes + truncating.DiscardedBytes;
            CoreLog.Info($"ExtractSevenZipEntryToMemory: entry={ArchivePath.GetFileName(entryName)} 实际 {actual} 字节 > 上限 {maxBytes}，拒绝返回截断数据");
            throw new EntryTooLargeException(entryName, actual, maxBytes);
        }
        return truncating.ToArray();
    }

    /// <summary>
    /// 在 7z/RAR/ISO 的条目列表里按规范化路径查找条目；找不到抛
    /// <see cref="ArchiveEntryNotFoundException"/>（默认值结构的 <c>FileName</c> 为 null）。
    /// </summary>
    private static ArchiveFileInfo FindSevenZipEntry(SharpSevenZipExtractor extractor, string entryName)
    {
        var entry = extractor.ArchiveFileData.FirstOrDefault(e =>
            ArchivePath.Normalize(e.FileName) == entryName);
        if (entry.FileName == null)
        {
            CoreLog.Info($"FindSevenZipEntry: entry={ArchivePath.GetFileName(entryName)} 不存在");
            throw new ArchiveEntryNotFoundException(entryName);
        }
        return entry;
    }

    /// <summary>
    /// 在 tar 容器里定位条目并返回其 **tar header 记录的 Size**（不落盘）。
    /// 找不到抛 <see cref="ArchiveEntryNotFoundException"/>。
    ///
    /// <para>
    /// TarReader 只能顺序前进，所以这一步会解压到目标条目为止——省的是磁盘，
    /// 不是 CPU；换来的收益是超限条目**一眼就拒**，不会先写满 %TEMP%。
    /// </para>
    /// </summary>
    private static (long Size, bool IsDirectory) FindTarEntry(string archivePath, string entryName)
    {
        using var inputStream = File.OpenRead(archivePath);
        using var reader = SharpCompress.Readers.Tar.TarReader.OpenReader(
            inputStream, new ReaderOptions { LookForHeader = true });
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;
            if (entry.Key != entryName)
                continue;
            return entry.IsDirectory ? (0, true) : (entry.Size, false);
        }

        CoreLog.Info($"FindTarEntry: entry={ArchivePath.GetFileName(entryName)} 不存在");
        throw new ArchiveEntryNotFoundException(entryName);
    }

    /// <summary>
    /// 纯 .gz（非 tar.gz）读出整条内容：没有条目大小元数据，无法解压前判上限，
    /// 因此直接流式解压、读满即停——既不落盘，也不会为了判大小先把整条解压一遍。
    /// </summary>
    private static byte[] ExtractGZipEntryToMemory(
        string archivePath, string entryName, long maxBytes, CancellationToken ct)
    {
        using var inputStream = File.OpenRead(archivePath);
        var gzipStream = new GZipStream(
            inputStream, SharpCompress.Compressors.CompressionMode.Decompress);
        try
        {
            return ReadWholeEntryToMemory(gzipStream, entryName, maxBytes, sizeHint: -1, ct);
        }
        finally
        {
            // 读满上限提前停止时 gzip 尾部 trailer 还没读，SharpCompress 的 Dispose 会抛
            // ZlibException("Protocol error")。这只是「没读完」的副作用，绝不能让它盖掉
            // 真正的超限异常（否则调用方拿到的是解析错误而不是「文件太大」）。
            try
            {
                gzipStream.Dispose();
            }
            catch (Exception ex) when (ex is SharpCompress.Compressors.Deflate.ZlibException
                                          or InvalidDataException or IOException)
            {
                CoreLog.Info($"ExtractGZipEntryToMemory: 提前停止读取，Dispose 抛 {ex.GetType().Name}（忽略，已读字节不受影响）");
            }
        }
    }

    /// <summary>
    /// SharpSevenZip 的 <c>ArchiveFileInfo.Size</c> 是 <c>ulong</c>；
    /// 转成 <see cref="EntryTooLargeException.ActualSize"/> 用的 <c>long</c>，
    /// 超出 <c>long</c> 上限时按约定给 -1（未知）。
    /// </summary>
    private static long ToActualSize(ulong size) =>
        size > (ulong)long.MaxValue ? -1 : (long)size;

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

                // 注意：tail 必须整条目解压后再截取末尾（走的是「读到最后 N 字节」的语义，
                // 无法像 head 那样读满即停），因此这里保持原有的整条目解压行为不变，
                // 只靠上面的 MaxTailExtractSize 上限控制内存。
                using var ms = new MemoryStream();
                entry.WriteTo(ms);
                return ExtractTailFromBuffer(ms.ToArray(), tailBytes);
            }

            case ArchiveFormat.SevenZip:
            case ArchiveFormat.Rar:
            case ArchiveFormat.Iso:
            {
                // 构造 extractor 前先配库路径（issue #52）
                SevenZipEngine.EnsureLibraryPath();
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
        catch (Exception ex)
        {
            CoreLog.Info($"ParseMvhdBox failed: {ex.Message}");
        }
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
        catch (Exception ex)
        {
            CoreLog.Info($"ParseTkhdBox failed: {ex.Message}");
        }
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
