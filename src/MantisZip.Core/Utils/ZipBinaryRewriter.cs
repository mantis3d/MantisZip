using System.IO.Compression;
using System.Text;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// Thrown when an entry or archive does not support copy-mode rewriting.
/// Callers should fall back to the legacy decompress-recompress path.
/// </summary>
public class ZipCopyModeException : Exception
{
    /// <summary>Initializes a new instance with a specified error message.</summary>
    public ZipCopyModeException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a specified error message and inner exception.</summary>
    public ZipCopyModeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Parsed Central Directory File Header entry — raw bytes preserved for lossless rewrite.
/// </summary>
internal readonly record struct CdEntry(
    string FileName,
    uint Crc32,
    long CompressedSize,
    long UncompressedSize,
    ushort CompressionMethod,
    ushort Flags,
    ushort LastModifiedDate,
    ushort LastModifiedTime,
    uint LocalHeaderOffset,
    byte[] RawExtraField,
    byte[] RawFileExtra,
    int LfhFilenameLength,
    int LfhExtraLength,
    /// <summary>
    /// 中央目录里文件名的<b>原始字节</b>。copy-mode 重写时原样写回，
    /// 使删除/添加操作不改变既有条目的文件名编码（避免「解码→重编码」往返损坏）。
    /// 新增条目（<c>RawFileNameBytes = null</c>）才按传入编码写出。
    /// </summary>
    byte[]? RawFileNameBytes = null
);

/// <summary>
/// Summary of a rewrite operation.
/// </summary>
public readonly record struct RewriteResult(
    int EntriesCopied,
    long BytesCopied,
    int EntriesAdded,
    long BytesAdded);

/// <summary>
/// New entry to add during rewrite. Caller must keep <see cref="Data"/> stream alive
/// until <c>RewriteAsync</c> completes.
/// </summary>
/// <param name="EntryName">Entry path inside the archive.</param>
/// <param name="Data">Uncompressed source data.</param>
/// <param name="LastModified">Entry timestamp (converted to MS-DOS date/time).</param>
/// <param name="Size">Uncompressed byte count of <paramref name="Data"/>.</param>
/// <param name="Store">
/// <c>true</c> = write with compression method 0 (Store) — bytes are passed through
/// untouched, which is what already-compressed payloads need.
/// <c>false</c> (default) = Deflate at <see cref="System.IO.Compression.CompressionLevel.Optimal"/>.
/// </param>
public readonly record struct NewEntry(
    string EntryName,
    Stream Data,
    DateTime LastModified,
    long Size,
    bool Store = false);

/// <summary>
/// ZIP binary rewriter providing low-level parsing and copy-mode rewrite capabilities.
/// </summary>
internal static partial class ZipBinaryRewriter
{
    private const int CopyBufferSize = 4194304;

    // ────────────────────────────── EOCD ──────────────────────────────

    /// <summary>
    /// Locate and parse the End of Central Directory record.
    /// </summary>
    /// <param name="stream">Seekable stream positioned at the start of the ZIP file.</param>
    /// <returns>
    /// A tuple containing: the byte offset of the central directory (<paramref name="cdOffset"/>),
    /// the total number of entries in the central directory (<paramref name="entryCount"/>),
    /// and the ZIP file comment (<paramref name="comment"/>, <c>null</c> if absent).
    /// </returns>
    /// <exception cref="ZipCopyModeException">EOCD signature not found, or ZIP64 detected.</exception>
    internal static (long cdOffset, int entryCount, string? comment) ReadEocd(Stream stream)
    {
        // EOCD minimum fixed size is 22 bytes; max comment length is 65535.
        const int EocdFixedSize = 22;
        const int MaxCommentLength = 65535;
        const int SearchWindow = MaxCommentLength + EocdFixedSize; // 65557

        if (stream.Length < EocdFixedSize)
            throw new ZipCopyModeException("EOCD signature not found");

        long searchStart = Math.Max(0, stream.Length - SearchWindow);
        int searchLen = (int)(stream.Length - searchStart);

        stream.Seek(searchStart, SeekOrigin.Begin);
        byte[] buf = new byte[searchLen];
        int read = stream.Read(buf, 0, searchLen);
        if (read < EocdFixedSize)
            throw new ZipCopyModeException("EOCD signature not found");

        // Scan backward for the EOCD signature 0x06054b50 (little-endian: 50 4b 05 06)
        long eocdPos = -1;
        for (int i = read - EocdFixedSize; i >= 0; i--)
        {
            if (buf[i] == 0x50 && buf[i + 1] == 0x4B &&
                buf[i + 2] == 0x05 && buf[i + 3] == 0x06)
            {
                eocdPos = searchStart + i;
                break;
            }
        }

        if (eocdPos < 0)
            throw new ZipCopyModeException("EOCD signature not found");

        CoreLog.Trace("ZipBinaryRewriter: EOCD found at offset {0}", eocdPos);

        int bufOffset = (int)(eocdPos - searchStart);

        // entryCount at EOCD offset 10 (2 bytes, uint16)
        int entryCount = buf[bufOffset + 10] | (buf[bufOffset + 11] << 8);

        // cdOffset at EOCD offset 16 (4 bytes, uint32)
        uint cdOffsetRaw = BitConverter.ToUInt32(buf, bufOffset + 16);
        long cdOffset = cdOffsetRaw;

        // commentLen at EOCD offset 20 (2 bytes, uint16)
        int commentLen = buf[bufOffset + 20] | (buf[bufOffset + 21] << 8);

        // ── ZIP64 detection ──────────────────────────────────────────
        if (entryCount == 0xFFFF || cdOffsetRaw == 0xFFFFFFFF)
        {
            // ZIP64 EOCD locator is stored immediately before the EOCD record.
            // Its signature is 0x07064b50 and its fixed size is 20 bytes.
            long zip64LocatorPos = eocdPos - 20;
            if (zip64LocatorPos >= searchStart)
            {
                int locOffset = (int)(zip64LocatorPos - searchStart);
                if (locOffset + 4 <= buf.Length &&
                    buf[locOffset] == 0x50 && buf[locOffset + 1] == 0x4B &&
                    buf[locOffset + 2] == 0x06 && buf[locOffset + 3] == 0x07)
                {
                    throw new ZipCopyModeException("ZIP64 ZIP not supported by copy-mode");
                }
            }
        }

        // ── Comment ──────────────────────────────────────────────────
        string? comment = null;
        if (commentLen > 0 && bufOffset + EocdFixedSize + commentLen <= buf.Length)
        {
            try
            {
                comment = Encoding.UTF8.GetString(buf, bufOffset + EocdFixedSize, commentLen);
            }
            catch (DecoderFallbackException)
            {
                comment = Encoding.Default.GetString(buf, bufOffset + EocdFixedSize, commentLen);
            }
        }

        return (cdOffset, entryCount, comment);
    }

    // ────────────────────── Central Directory ────────────────────────

    /// <summary>
    /// Read all entries from the ZIP central directory.
    /// </summary>
    /// <param name="stream">Seekable stream positioned at the start of the ZIP file.</param>
    /// <param name="cdOffset">Byte offset of the central directory (from <see cref="ReadEocd"/>).</param>
    /// <param name="entryCount">Number of entries to read.</param>
    /// <returns>List of parsed central directory entries.</returns>
    /// <exception cref="ZipCopyModeException">
    /// Thrown if <paramref name="cdOffset"/> is past the stream length,
    /// or a CDFH signature is invalid mid-parse.
    /// </exception>
    internal static List<CdEntry> ReadCentralDirectory(
        Stream stream, long cdOffset, int entryCount,
        Encoding? fallbackEncoding = null)
    {
        if (cdOffset >= stream.Length)
            throw new ZipCopyModeException(
                $"Central directory offset {cdOffset} is past stream length {stream.Length}");

        var entries = new List<CdEntry>(entryCount);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        // bit 11 未置位的条目名按此编码解码（中文 Windows 默认 GBK）
        fallbackEncoding ??= Encoding.GetEncoding("gbk");

        stream.Seek(cdOffset, SeekOrigin.Begin);

        for (int i = 0; i < entryCount; i++)
        {
            // ── Signature ────────────────────────────────────────────
            uint sig = reader.ReadUInt32();
            if (sig != 0x02014b50)
                throw new ZipCopyModeException(
                    $"Unexpected CDFH signature at entry {i}: expected 0x02014b50, got 0x{sig:X8}");

            // ── Fixed fields (42 bytes after signature) ──────────────
            /*  0-1 */ reader.ReadUInt16(); // VersionMadeBy
            /*  2-3 */ reader.ReadUInt16(); // VersionNeeded
            /*  4-5 */ ushort flags = reader.ReadUInt16();
            /*  6-7 */ ushort compressionMethod = reader.ReadUInt16();
            /*  8-9 */ ushort lastModTime = reader.ReadUInt16();
            /* 10-11 */ ushort lastModDate = reader.ReadUInt16();
            /* 12-15 */ uint crc32 = reader.ReadUInt32();
            /* 16-19 */ uint compressedSizeRaw = reader.ReadUInt32();
            /* 20-23 */ uint uncompressedSizeRaw = reader.ReadUInt32();
            /* 24-25 */ ushort fileNameLength = reader.ReadUInt16();
            /* 26-27 */ ushort extraFieldLength = reader.ReadUInt16();
            /* 28-29 */ ushort fileCommentLength = reader.ReadUInt16();
            /* 30-31 */ reader.ReadUInt16(); // DiskNumberStart
            /* 32-33 */ reader.ReadUInt16(); // InternalAttributes
            /* 34-37 */ reader.ReadUInt32(); // ExternalAttributes
            /* 38-41 */ uint localHeaderOffset = reader.ReadUInt32();

            // ── Variable-length fields ───────────────────────────────
            byte[] fileNameBytes = reader.ReadBytes(fileNameLength);
            byte[] extraField = reader.ReadBytes(extraFieldLength);
            /* skip file comment */ reader.ReadBytes(fileCommentLength);

            // 文件名解码：APPNOTE 6.4.4 —— bit 11 置位表示 UTF-8；未置位时按调用方
            // 提供的编码（默认 GBK，中文 Windows 兼容旧工具）解码。
            // 关键：**同时保留原始字节**，使 copy-mode 能原样写回，
            // 无需「解码→重编码」往返（往返会破坏非 UTF-8 编码的包）。
            Encoding nameEncoding = (flags & 0x0800) != 0 ? Encoding.UTF8 : fallbackEncoding;
            string fileName = nameEncoding.GetString(fileNameBytes);

            entries.Add(new CdEntry(
                FileName: fileName,
                Crc32: crc32,
                CompressedSize: compressedSizeRaw,
                UncompressedSize: uncompressedSizeRaw,
                CompressionMethod: compressionMethod,
                Flags: flags,
                LastModifiedDate: lastModDate,
                LastModifiedTime: lastModTime,
                LocalHeaderOffset: localHeaderOffset,
                RawExtraField: extraField,
                RawFileExtra: [],
                LfhFilenameLength: fileNameLength,
                LfhExtraLength: 0,
                RawFileNameBytes: fileNameBytes
            ));
        }

        CoreLog.Trace("ZipBinaryRewriter: read {0} central directory entries", entries.Count);

        return entries;
    }

    // ────────────────────── LFH Info ──────────────────────

    /// <summary>
    /// Parsed Local File Header information, including the (possibly rewritten) raw header bytes.
    /// </summary>
    private readonly record struct LfhInfo(
        byte[] RawHeader,
        long CompressedSize,
        long UncompressedSize,
        uint Crc32,
        ushort Flags,
        int ExtraLength
    );

    // ────────────────────── RewriteAsync ──────────────────

    /// <summary>
    /// Rewrite a ZIP file using compressed-stream copy-mode.
    /// Copies kept entries by directly copying their LFH + compressed data,
    /// then appends new entries and writes a new central directory + EOCD.
    /// </summary>
    /// <param name="sourcePath">Path to the source ZIP file.</param>
    /// <param name="destPath">Path for the rewritten output ZIP file.</param>
    /// <param name="keepEntryNames">
    /// Set of entry names to keep. <c>null</c> means keep all existing entries.
    /// </param>
    /// <param name="addEntries">New entries to add, or <c>null</c> for none.</param>
    /// <param name="encoding">Encoding for ZIP filenames (UTF-8 or GBK).</param>
    /// <param name="comment">Optional ZIP comment. If <c>null</c>, the original comment is preserved.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="RewriteResult"/> summarizing the operation.</returns>
    /// <exception cref="ZipCopyModeException">
    /// Thrown when an entry or archive doesn't support copy-mode rewriting.
    /// Callers should fall back to the legacy decompress-recompress path.
    /// </exception>
    public static async Task<RewriteResult> RewriteAsync(
        string sourcePath,
        string destPath,
        HashSet<string>? keepEntryNames,
        List<NewEntry>? addEntries,
        Encoding encoding,
        string? comment = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ZipBinaryRewriter.RewriteAsync: source='{sourcePath}', dest='{destPath}'");

        Stream? source = null;
        Stream? output = null;
        string tempDestPath = destPath + ".tmp";
        // 只有成功原子替换后才置 true；finally 里据此决定是否清理临时文件。
        bool committed = false;

        try
        {
            // ── Open source ──────────────────────────────────────────
            source = File.Open(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);

            // ── SFX detection ────────────────────────────────────────
            byte[] magic = new byte[2];
            source.ReadExactly(magic, 0, 2);
            source.Seek(0, SeekOrigin.Begin);
            if (magic[0] == 'M' && magic[1] == 'Z')
                throw new ZipCopyModeException("SFX ZIP not supported by copy-mode");

            // ── Parse existing archive ───────────────────────────────
            var (cdOffset, entryCount, existingComment) = ReadEocd(source);
            List<CdEntry> entries = ReadCentralDirectory(source, cdOffset, entryCount, encoding);

            CoreLog.Info($"ZipBinaryRewriter: source has {entries.Count} entries");

            // ── Open output (write to .tmp for atomic replace) ───────
            output = File.Create(tempDestPath);

            // Determine which entries to keep
            bool keepAll = keepEntryNames == null;
            HashSet<string> keepSet = keepEntryNames ?? new HashSet<string>();

            // ── Build the list that feeds into central directory ─────
            var entriesToWrite = new List<(CdEntry Entry, long NewOffset, bool IsNew, byte[]? NewLfh)>();

            int totalEntries = (keepAll ? entries.Count : (keepEntryNames?.Count ?? 0))
                               + (addEntries?.Count ?? 0);
            if (totalEntries == 0) totalEntries = 1; // avoid division by zero

            int processedEntries = 0;
            long bytesCopied = 0;
            long bytesAdded = 0;

            // ═══════════════════════════════════════════════════════════
            // Phase 1: Copy kept entries (binary copy-mode)
            // ═══════════════════════════════════════════════════════════
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip entries not in the keep set (when keepAll == false)
                if (!keepAll && !keepSet.Contains(entry.FileName))
                    continue;

                double basePct = totalEntries > 0
                    ? (double)processedEntries / totalEntries * 100
                    : 0;
                double entryWeight = 90.0 / totalEntries;

                bytesCopied += await CopyEntryAsync(source, output, entry,
                    entriesToWrite, basePct, entryWeight, progress, cancellationToken);
                processedEntries++;
            }

            // ═══════════════════════════════════════════════════════════
            // Phase 2: Add new entries
            // ═══════════════════════════════════════════════════════════
            if (addEntries != null)
            {
                foreach (var newEntry in addEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    double basePct = totalEntries > 0
                        ? (double)processedEntries / totalEntries * 100
                        : 0;
                    double entryWeight = 90.0 / totalEntries;

                    long entryOffset = output.Position;

                    // Compress and write the new entry's LFH + data (streaming with progress)
        var (lfhBytes, compressedSize, crc32) =
                            CompressNewEntry(output, newEntry, encoding,
                                basePct, entryWeight, progress, cancellationToken);
                    ushort lfhInfoFlags = BitConverter.ToUInt16(lfhBytes, 6);

                    bytesAdded += lfhBytes.Length + compressedSize;

                    // Build a synthetic CdEntry for the central directory
                    var (dosDate, dosTime) = DateTimeToDos(newEntry.LastModified);
                    byte[] fileNameBytes = encoding.GetBytes(newEntry.EntryName);

                    var syntheticEntry = new CdEntry(
                        FileName: newEntry.EntryName,
                        Crc32: crc32,
                        CompressedSize: compressedSize,
                        UncompressedSize: newEntry.Size,
                        CompressionMethod: (ushort)(newEntry.Store ? 0 : 8),
                        Flags: lfhInfoFlags,
                        LastModifiedDate: dosDate,
                        LastModifiedTime: dosTime,
                        LocalHeaderOffset: 0, // unused; NewOffset in the tuple is used instead
                        RawExtraField: [],
                        RawFileExtra: [],
                        LfhFilenameLength: fileNameBytes.Length,
                        LfhExtraLength: 0
                    );

                    entriesToWrite.Add((syntheticEntry, entryOffset, true, lfhBytes));
                    processedEntries++;

                    CoreLog.Trace("ZipBinaryRewriter: added new entry '{0}' ({1} bytes compressed)",
                        newEntry.EntryName, compressedSize);
                }
            }

            // ══════════════════════════════════════════════════════════
            // Phase 3: Write central directory
            // ══════════════════════════════════════════════════════════
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "正在写入中央目录...",
                PercentComplete = 92,
                FilePercentComplete = 100
            });

            long centralDirStart = output.Position;
            WriteCentralDirectory(output, entriesToWrite, encoding, progress);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "正在写入目录结束标记...",
                PercentComplete = 94,
                FilePercentComplete = 100
            });

            // ══════════════════════════════════════════════════════════
            // Phase 4: Write EOCD
            // ══════════════════════════════════════════════════════════
            string effectiveComment = comment ?? existingComment ?? string.Empty;
            WriteEocd(output, centralDirStart, entriesToWrite.Count, effectiveComment);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "正在保存到磁盘...",
                PercentComplete = 97,
                FilePercentComplete = 100
            });

            // ── Finalize (close then atomically replace) ─────────────
            output.Dispose();
            output = null;

            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(tempDestPath, destPath);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                FilePercentComplete = 100
            });

            int copyCount = entriesToWrite.Count(e => !e.IsNew);
            int addCount = entriesToWrite.Count - copyCount;

            CoreLog.Info(
                $"ZipBinaryRewriter: rewrite complete — {copyCount} entries copied ({bytesCopied} bytes), " +
                $"{addCount} entries added ({bytesAdded} bytes)");
            CoreLog.Exit();

            committed = true;
            return new RewriteResult(copyCount, bytesCopied, addCount, bytesAdded);
        }
        catch (OperationCanceledException)
        {
            CoreLog.Info("ZipBinaryRewriter: cancelled");
            throw;
        }
        catch (Exception ex)
        {
            if (ex is ZipCopyModeException)
                CoreLog.Info("ZipBinaryRewriter: copy-mode not supported, aborting (caller must fall back to the serial path)");
            else
                CoreLog.Error("ZipBinaryRewriter: error during rewrite", ex);
            throw;
        }
        finally
        {
            source?.Dispose();
            output?.Dispose();
            // 临时文件清理必须放在 Dispose 之后：输出流以 FileShare.Read 打开
            // （不含 FileShare.Delete），句柄未释放时 Windows 会拒绝删除该文件，
            // 而 CleanupFile 是 best-effort 吞异常的——结果就是半成品 .tmp 残留。
            if (!committed)
                CleanupFile(tempDestPath);
        }
    }

    /// <summary>
    /// 多源 copy-mode 拼接：把多个 ZIP 的条目<b>原样复制</b>（LFH + 压缩字节 + CDFH）到单一输出。
    /// <para>
    /// 从不解压也从不重压，因此各源的压缩方法码无需统一。按 <paramref name="sourcePaths"/>
    /// 顺序写出，条目顺序即源顺序。任一源解析失败即整体失败（不产出半成品：输出先写
    /// <c>destPath + ".tmp"</c>，全部成功后原子替换）。
    /// </para>
    /// <para>
    /// 供 N 组并行 ZIP 压缩使用——各组各自产出一个 ZIP，再由此处字节级拼成最终包。
    /// 单源时直接委托 <see cref="RewriteAsync(string, string, HashSet{string}, List{NewEntry}, Encoding, string, IProgress{ArchiveProgress}, CancellationToken)"/>，
    /// 避免 copy-mode 逻辑出现第二份实现。
    /// </para>
    /// </summary>
    /// <param name="sourcePaths">待拼接的源 ZIP 路径，至少 1 个。</param>
    /// <param name="destPath">拼接后的输出 ZIP 路径。</param>
    /// <param name="keepEntryNames">
    /// Set of entry names to keep. <c>null</c> means keep all existing entries.
    /// </param>
    /// <param name="addEntries">New entries to add, or <c>null</c> for none.</param>
    /// <param name="encoding">Encoding for ZIP filenames (UTF-8 or GBK).</param>
    /// <param name="comment">
    /// Optional ZIP comment. If <c>null</c>, the first non-empty source comment is preserved.
    /// </param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="RewriteResult"/> summarizing the operation.</returns>
    /// <exception cref="ArgumentException"><paramref name="sourcePaths"/> is empty.</exception>
    /// <exception cref="ZipCopyModeException">
    /// Thrown when any source or entry doesn't support copy-mode rewriting.
    /// Callers should fall back the serial path.
    /// </exception>
    public static async Task<RewriteResult> RewriteAsync(
        IReadOnlyList<string> sourcePaths,
        string destPath,
        HashSet<string>? keepEntryNames,
        List<NewEntry>? addEntries,
        Encoding encoding,
        string? comment = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ZipBinaryRewriter.RewriteAsync(multi): sources={sourcePaths.Count}, dest='{destPath}'");

        if (sourcePaths is null || sourcePaths.Count == 0)
            throw new ArgumentException("sourcePaths must not be empty", nameof(sourcePaths));

        // 单源直接委托既有实现，避免重复实现 copy-mode 逻辑
        if (sourcePaths.Count == 1)
        {
            return await RewriteAsync(sourcePaths[0], destPath, keepEntryNames,
                addEntries, encoding, comment, progress, cancellationToken);
        }

        var parsed = new List<(Stream Stream, List<CdEntry> Entries)>();
        Stream? output = null;
        string tempDestPath = destPath + ".tmp";
        // 只有成功原子替换后才置 true；finally 里据此决定是否清理临时文件。
        bool committed = false;

        var entriesToWrite = new List<(CdEntry Entry, long NewOffset, bool IsNew, byte[]? NewLfh)>();
        int processedEntries = 0;
        long bytesCopied = 0;
        long bytesAdded = 0;
        string? existingComment = null;
        int totalEntries;

        try
        {
            // 源流在 Phase 1 结束时统一释放：解析中途抛错时已入队的流也在此覆盖，
            // 避免 N 组并行时同时持 N 个句柄跨越 Phase 2/3。
            try
            {
                // ── Parse every source up front ─────────────────────
                foreach (var src in sourcePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var source = File.Open(src, FileMode.Open, FileAccess.Read,
                        FileShare.Read | FileShare.Delete);

                    // 解析失败时不能把流漏在列表外
                    try
                    {
                        // ── SFX detection ────────────────────────────
                        byte[] magic = new byte[2];
                        source.ReadExactly(magic, 0, 2);
                        source.Seek(0, SeekOrigin.Begin);
                        if (magic[0] == 'M' && magic[1] == 'Z')
                            throw new ZipCopyModeException("SFX ZIP not supported by copy-mode");

                        var (cdOffset, srcEntryCount, srcComment) = ReadEocd(source);
                        parsed.Add((source, ReadCentralDirectory(source, cdOffset, srcEntryCount)));
                        existingComment ??= srcComment;
                    }
                    catch
                    {
                        source.Dispose();
                        throw;
                    }
                }

                CoreLog.Info($"ZipBinaryRewriter: {parsed.Count} sources parsed, " +
                             $"{parsed.Sum(p => p.Entries.Count)} entries total");

                // Determine which entries to keep
                bool keepAll = keepEntryNames == null;
                HashSet<string> keepSet = keepEntryNames ?? new HashSet<string>();

                // 权重按「实际要写出的条目数」计算，与单源路径语义一致——
                // 否则 keep 过滤掉大半条目时进度会提前触顶。
                totalEntries = (keepAll
                        ? parsed.Sum(p => p.Entries.Count)
                        : parsed.Sum(p => p.Entries.Count(e => keepSet.Contains(e.FileName))))
                                   + (addEntries?.Count ?? 0);
                if (totalEntries == 0) totalEntries = 1; // avoid division by zero

                // ── Open output (write to .tmp for atomic replace) ───────
                output = File.Create(tempDestPath);

                // ═══════════════════════════════════════════════════════════
                // Phase 1: 按源顺序复制条目 (binary copy-mode)
                // ═══════════════════════════════════════════════════════════
                foreach (var (source, entries) in parsed)
                {
                    foreach (var entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Skip entries not in the keep set (when keepAll == false)
                        if (!keepAll && !keepSet.Contains(entry.FileName))
                            continue;

                        double basePct = (double)processedEntries / totalEntries * 100;
                        double entryWeight = 90.0 / totalEntries;

                        bytesCopied += await CopyEntryAsync(source, output, entry,
                            entriesToWrite, basePct, entryWeight, progress, cancellationToken);
                        processedEntries++;
                    }
                }
            }
            finally
            {
                foreach (var (s, _) in parsed) s.Dispose();
            }

            // ══════════════════════════════════════════════════════════
            // Phase 2: Add new entries
            // ══════════════════════════════════════════════════════════
            if (addEntries != null)
            {
                foreach (var newEntry in addEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    double basePct = (double)processedEntries / totalEntries * 100;
                    double entryWeight = 90.0 / totalEntries;

                    long entryOffset = output.Position;

                    // Compress and write the new entry's LFH + data (streaming with progress)
                    var (lfhBytes, compressedSize, crc32) =
                        CompressNewEntry(output, newEntry, encoding,
                            basePct, entryWeight, progress, cancellationToken);

                    bytesAdded += lfhBytes.Length + compressedSize;

                    // Build a synthetic CdEntry for the central directory
                    var (dosDate, dosTime) = DateTimeToDos(newEntry.LastModified);
                    byte[] fileNameBytes = encoding.GetBytes(newEntry.EntryName);

                    var syntheticEntry = new CdEntry(
                        FileName: newEntry.EntryName,
                        Crc32: crc32,
                        CompressedSize: compressedSize,
                        UncompressedSize: newEntry.Size,
                        CompressionMethod: (ushort)(newEntry.Store ? 0 : 8),
                        Flags: 0,
                        LastModifiedDate: dosDate,
                        LastModifiedTime: dosTime,
                        LocalHeaderOffset: 0, // unused; NewOffset in the tuple is used instead
                        RawExtraField: [],
                        RawFileExtra: [],
                        LfhFilenameLength: fileNameBytes.Length,
                        LfhExtraLength: 0
                    );

                    entriesToWrite.Add((syntheticEntry, entryOffset, true, lfhBytes));
                    processedEntries++;

                    CoreLog.Trace("ZipBinaryRewriter: added new entry '{0}' ({1} bytes compressed)",
                        newEntry.EntryName, compressedSize);
                }
            }

            // ══════════════════════════════════════════════════════════
            // Phase 3: Write central directory
            // ══════════════════════════════════════════════════════════
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "正在写入中央目录...",
                PercentComplete = 92,
                FilePercentComplete = 100
            });

            long centralDirStart = output.Position;
            WriteCentralDirectory(output, entriesToWrite, encoding, progress);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "正在写入目录结束标记...",
                PercentComplete = 94,
                FilePercentComplete = 100
            });

            // ══════════════════════════════════════════════════════════
            // Phase 4: Write EOCD
            // ══════════════════════════════════════════════════════════
            // 注释规则：显式传入优先；否则沿用第一个非空源的注释；都没有则空串。
            string effectiveComment = comment ?? existingComment ?? string.Empty;
            WriteEocd(output, centralDirStart, entriesToWrite.Count, effectiveComment);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "正在保存到磁盘...",
                PercentComplete = 97,
                FilePercentComplete = 100
            });

            // ── Finalize (close then atomically replace) ─────────────
            output.Dispose();
            output = null;

            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(tempDestPath, destPath);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                FilePercentComplete = 100
            });

            int copyCount = entriesToWrite.Count(e => !e.IsNew);
            int addCount = entriesToWrite.Count - copyCount;

            CoreLog.Info(
                $"ZipBinaryRewriter: multi-source rewrite complete — {copyCount} entries copied ({bytesCopied} bytes), " +
                $"{addCount} entries added ({bytesAdded} bytes)");
            CoreLog.Exit();

            committed = true;
            return new RewriteResult(copyCount, bytesCopied, addCount, bytesAdded);
        }
        catch (OperationCanceledException)
        {
            CoreLog.Info("ZipBinaryRewriter: cancelled");
            throw;
        }
        catch (Exception ex)
        {
            if (ex is ZipCopyModeException)
                CoreLog.Info("ZipBinaryRewriter: copy-mode not supported, aborting (caller must fall back to the serial path)");
            else
                CoreLog.Error("ZipBinaryRewriter: error during multi-source rewrite", ex);
            throw;
        }
        finally
        {
            output?.Dispose();
            // 同单源路径：Dispose 之后再清理，否则句柄未释放时 Windows 拒绝删除，
            // best-effort 的 CleanupFile 会静默留下半成品 .tmp。
            if (!committed)
                CleanupFile(tempDestPath);
        }
    }

    // ────────────────────── Entry Copy ───────────────────

    /// <summary>
    /// Copy a single entry (LFH + already-compressed data) to the output stream and
    /// append its CDFH record to <paramref name="entriesToWrite"/>.
    /// <para>
    /// Shared by the single-source and multi-source paths so that copy-mode validation
    /// (Store/Deflate/Deflate64, unencrypted, non-ZIP64) and the LFH rewrite exist in
    /// exactly one place — otherwise they would drift between the two paths.
    /// </para>
    /// </summary>
    /// <param name="source">Source archive stream, positioned anywhere (seeks as needed).</param>
    /// <param name="output">Destination archive stream.</param>
    /// <param name="entry">Central directory entry to copy.</param>
    /// <param name="entriesToWrite">Collector that receives the CDFH record and its new offset.</param>
    /// <param name="basePct">Overall progress percentage before this entry starts.</param>
    /// <param name="entryWeight">Overall progress percentage weight of this entry.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bytes written for this entry (LFH header + compressed data).</returns>
    /// <exception cref="ZipCopyModeException">
    /// Thrown when the entry uses an unsupported compression method, is encrypted,
    /// or requires ZIP64.
    /// </exception>
    private static async Task<long> CopyEntryAsync(
        Stream source,
        Stream output,
        CdEntry entry,
        List<(CdEntry Entry, long NewOffset, bool IsNew, byte[]? NewLfh)> entriesToWrite,
        double basePct,
        double entryWeight,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        // ── Copy-mode validation ─────────────────────────────
        if (entry.CompressionMethod != 0 && entry.CompressionMethod != 8 && entry.CompressionMethod != 9)
        {
            CoreLog.Info($"Entry '{entry.FileName}': unsupported compression method {entry.CompressionMethod}");
            throw new ZipCopyModeException(
                $"Entry '{entry.FileName}' uses unsupported compression method ({entry.CompressionMethod}). " +
                "Only Store (0), Deflate (8) and Deflate64 (9) are supported by copy-mode.");
        }

        if ((entry.Flags & 0x0001) != 0) // bit 0 = encrypted
        {
            CoreLog.Info($"Entry '{entry.FileName}': encrypted, not supported by copy-mode");
            throw new ZipCopyModeException(
                $"Entry '{entry.FileName}' is encrypted. Encrypted entries are not supported by copy-mode.");
        }

        if (entry.CompressedSize >= 0xFFFFFFFF)
        {
            CoreLog.Info($"Entry '{entry.FileName}': ZIP64 compressed size, not supported by copy-mode");
            throw new ZipCopyModeException(
                $"Entry '{entry.FileName}' uses ZIP64 compressed size. ZIP64 is not supported by copy-mode.");
        }

        if (entry.LocalHeaderOffset >= 0xFFFFFFFF)
        {
            CoreLog.Info($"Entry '{entry.FileName}': ZIP64 local header offset, not supported by copy-mode");
            throw new ZipCopyModeException(
                $"Entry '{entry.FileName}' uses ZIP64 local header offset. ZIP64 is not supported by copy-mode.");
        }

        // ── Read and optionally rewrite LFH ──────────────────
        LfhInfo lfhInfo = ReadAndMaybeRewriteLfh(
            source, entry.LocalHeaderOffset, entry, out byte[] lfhHeader);

        long entryOffset = output.Position;

        // Write LFH header to output
        output.Write(lfhHeader, 0, lfhHeader.Length);

        // Stream-copy compressed data with per-chunk progress
        await CopyStreamRangeAsync(
            source, output, entry.CompressedSize,
            entry.FileName, basePct, entryWeight,
            progress, cancellationToken);

        // If bit 3 was cleared in the LFH rewrite, propagate the flag change
        // to the CDFH so it matches the LFH (no data descriptor present).
        var entryForCd = lfhInfo.Flags != entry.Flags
            ? entry with { Flags = lfhInfo.Flags }
            : entry;
        entriesToWrite.Add((entryForCd, entryOffset, false, lfhHeader));

        CoreLog.Trace("ZipBinaryRewriter: copied entry '{0}' ({1} bytes)",
            entry.FileName, entry.CompressedSize);

        return lfhHeader.Length + entry.CompressedSize;
    }

    // ────────────────────── LFH Parsing ───────────────────

    /// <summary>
    /// Read a Local File Header from the source stream. If bit 3 (data descriptor)
    /// is set, rewrite the LFH bytes in-place: clear bit 3 and fill the correct
    /// CRC / CompressedSize / UncompressedSize from the CDFH entry.
    /// </summary>
    /// <param name="source">Seekable source stream at any position (will seek to <paramref name="localHeaderOffset"/>).</param>
    /// <param name="localHeaderOffset">Byte offset of the LFH in the source stream.</param>
    /// <param name="entry">The corresponding CDFH entry.</param>
    /// <param name="headerBytes">The complete (possibly rewritten) LFH header bytes.</param>
    /// <returns>Parsed LFH metadata.</returns>
    private static LfhInfo ReadAndMaybeRewriteLfh(
        Stream source, long localHeaderOffset, in CdEntry entry, out byte[] headerBytes)
    {
        source.Seek(localHeaderOffset, SeekOrigin.Begin);

        // Read the fixed 30-byte portion of the LFH
        byte[] fixedHeader = new byte[30];
        int read = source.Read(fixedHeader, 0, 30);
        if (read < 30)
            throw new ZipCopyModeException(
                $"Unexpected end of stream reading LFH at offset {localHeaderOffset}");

        // Verify signature
        uint sig = BitConverter.ToUInt32(fixedHeader, 0);
        if (sig != 0x04034b50)
            throw new ZipCopyModeException(
                $"Invalid LFH signature at offset {localHeaderOffset}: 0x{sig:X8}");

        // Parse variable-length field sizes
        ushort fileNameLength = BitConverter.ToUInt16(fixedHeader, 26);
        ushort extraLength = BitConverter.ToUInt16(fixedHeader, 28);
        int lfhTotalSize = 30 + fileNameLength + extraLength;

        // Read the full LFH header (fixed + filename + extra)
        byte[] fullHeader = new byte[lfhTotalSize];
        Buffer.BlockCopy(fixedHeader, 0, fullHeader, 0, 30);

        if (lfhTotalSize > 30)
        {
            int remaining = lfhTotalSize - 30;
            read = source.Read(fullHeader, 30, remaining);
            if (read < remaining)
                throw new ZipCopyModeException(
                    $"Unexpected end of stream reading LFH variable fields at offset {localHeaderOffset}");
        }

        ushort flags = BitConverter.ToUInt16(fullHeader, 6);

        // If bit 3 (data descriptor) is set, rewrite the LFH
        if ((flags & 0x0008) != 0)
        {
            // Clear bit 3 in flags (offset 6-7)
            byte[] newFlags = BitConverter.GetBytes((ushort)(flags & ~0x0008));
            Buffer.BlockCopy(newFlags, 0, fullHeader, 6, 2);

            // Write correct CRC32 from CDFH (offset 14-17)
            byte[] crcBytes = BitConverter.GetBytes(entry.Crc32);
            Buffer.BlockCopy(crcBytes, 0, fullHeader, 14, 4);

            // Write correct compressed size from CDFH (offset 18-21)
            byte[] compSizeBytes = BitConverter.GetBytes((uint)entry.CompressedSize);
            Buffer.BlockCopy(compSizeBytes, 0, fullHeader, 18, 4);

            // Write correct uncompressed size from CDFH (offset 22-25)
            byte[] uncompSizeBytes = BitConverter.GetBytes((uint)entry.UncompressedSize);
            Buffer.BlockCopy(uncompSizeBytes, 0, fullHeader, 22, 4);

            flags = (ushort)(flags & ~0x0008);

            CoreLog.Trace(
                "ZipBinaryRewriter: rewrote LFH for '{0}' (cleared bit 3, CRC=0x{1:X8}, compSize={2}, uncompSize={3})",
                entry.FileName, entry.Crc32, entry.CompressedSize, entry.UncompressedSize);
        }

        headerBytes = fullHeader;

        return new LfhInfo(
            RawHeader: fullHeader,
            CompressedSize: entry.CompressedSize,
            UncompressedSize: entry.UncompressedSize,
            Crc32: entry.Crc32,
            Flags: flags,
            ExtraLength: extraLength
        );
    }

    // ────────────────────── New Entry Compression ─────────

    /// <summary>
    /// Compress a new entry and write its LFH + compressed data to the output stream.
    /// Uses Deflate compression and computes CRC32 via <see cref="Crc32"/>.
    /// </summary>
    /// <param name="output">Output stream to write to.</param>
    /// <param name="entry">The new entry to add.</param>
    /// <param name="encoding">Encoding for the entry filename in the LFH.</param>
    /// <returns>
    /// A tuple of (lfhBytes, compressedSize, crc32).
    /// The caller is responsible for tracking the output offset before calling this method.
    /// </returns>
    private static (byte[] lfhBytes, long compressedSize, uint crc32) CompressNewEntry(
        Stream output, in NewEntry entry, Encoding encoding,
        double basePercent, double entryWeight,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var lastReportTime = DateTime.Now;
        var reportInterval = TimeSpan.FromMilliseconds(100);

        // ── Stream source data through CRC32 + Deflate in one pass ──
        using var ms = new MemoryStream();
        uint crc = 0xFFFFFFFF;

        // APPNOTE 6.4.4：文件名含高位字符时必须置 bit 11（UTF-8 标志），
        // 否则解码器回退到 CP437 解释 → 第三方工具（7-Zip/WinRAR/资源管理器/unzip）显示乱码。
        // 仅 UTF-8 编码需要置位；GBK 等单字节代码页靠约定识别，不设此位。
        bool needsUtf8Flag = encoding is UTF8Encoding && entry.EntryName.Any(c => c > 127);
        ushort generalFlags = needsUtf8Flag ? (ushort)0x0800 : (ushort)0;

        Stream? deflateStream = entry.Store
            ? null
            : new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true);
        Stream sink = deflateStream ?? ms;
        try
        {
            byte[] buffer = new byte[CopyBufferSize];
            long totalRead = 0;

            while (totalRead < entry.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int toRead = (int)Math.Min(buffer.Length, entry.Size - totalRead);
                int read = entry.Data.Read(buffer, 0, toRead);
                if (read <= 0) break;

                // Update CRC32 incrementally
                for (int i = 0; i < read; i++)
                    crc = Crc32LookupTable[(int)((crc ^ buffer[i]) & 0xFF)] ^ (crc >> 8);

                sink.Write(buffer, 0, read);
                totalRead += read;

                var now = DateTime.Now;
                if (now - lastReportTime >= reportInterval || totalRead >= entry.Size)
                {
                    var filePct = entry.Size > 0 ? (double)totalRead / entry.Size * 100 : 100;
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = "压缩: " + entry.EntryName,
                        PercentComplete = Math.Min(90.0, basePercent + totalRead * entryWeight / Math.Max(entry.Size, 1)),
                        FilePercentComplete = filePct
                    });
                    lastReportTime = now;
                }
            }
        }
        finally
        {
            deflateStream?.Dispose(); // flush → ms 内为压缩数据；Store 路径无需 flush
        }

        uint crc32 = crc ^ 0xFFFFFFFF;
        byte[] compressed = ms.ToArray();
        int compressedSize = compressed.Length;

        // ── Build LFH ─────────────────────────────────────────────────
        byte[] fileNameBytes = encoding.GetBytes(entry.EntryName);
        ushort fileNameLen = (ushort)fileNameBytes.Length;

        var (dosDate, dosTime) = DateTimeToDos(entry.LastModified);

        byte[] lfh = new byte[30 + fileNameLen];

        // Signature (0x04034b50)
        BitConverter.GetBytes((uint)0x04034b50).CopyTo(lfh, 0);
        // Version needed (2.0)
        BitConverter.GetBytes((ushort)20).CopyTo(lfh, 4);
        // Flags（bit 11 = UTF-8 文件名标志，见上方 needsUtf8Flag 推导；无加密、无 data descriptor）
        BitConverter.GetBytes(generalFlags).CopyTo(lfh, 6);
        // Compression method (0 = Store, 8 = Deflate)
        BitConverter.GetBytes((ushort)(entry.Store ? 0 : 8)).CopyTo(lfh, 8);
        // Last modified time
        BitConverter.GetBytes(dosTime).CopyTo(lfh, 10);
        // Last modified date
        BitConverter.GetBytes(dosDate).CopyTo(lfh, 12);
        // CRC32
        BitConverter.GetBytes(crc32).CopyTo(lfh, 14);
        // Compressed size
        BitConverter.GetBytes((uint)compressedSize).CopyTo(lfh, 18);
        // Uncompressed size
        BitConverter.GetBytes((uint)entry.Size).CopyTo(lfh, 22);
        // Filename length
        BitConverter.GetBytes(fileNameLen).CopyTo(lfh, 26);
        // Extra field length (0)
        BitConverter.GetBytes((ushort)0).CopyTo(lfh, 28);
        // Filename bytes
        fileNameBytes.CopyTo(lfh, 30);

        // ── Write to output ───────────────────────────────────────────
        output.Write(lfh, 0, lfh.Length);
        output.Write(compressed, 0, compressed.Length);

        return (lfh, compressedSize, crc32);
    }

    // ────────────────────── DOS DateTime ─────────────────

    /// <summary>
    /// Convert a <see cref="DateTime"/> to MS-DOS date and time values.
    /// </summary>
    private static (ushort date, ushort time) DateTimeToDos(DateTime dt)
    {
        int year = dt.Year;
        if (year < 1980) year = 1980;
        if (year > 2107) year = 2107;

        ushort timeVal = (ushort)((dt.Hour << 11) | (dt.Minute << 5) | (dt.Second / 2));
        ushort dateVal = (ushort)(((year - 1980) << 9) | (dt.Month << 5) | dt.Day);

        return (dateVal, timeVal);
    }

    // ────────────────────── Central Directory ─────────────

    /// <summary>
    /// Write the Central Directory File Headers (CDFH) for all entries.
    /// Each CDFH uses the <paramref name="encoding"/> for the filename bytes
    /// and the <c>newOffset</c> from the tracking list as the local header offset.
    /// </summary>
    private static void WriteCentralDirectory(
        Stream output,
        List<(CdEntry Entry, long NewOffset, bool IsNew, byte[]? NewLfh)> entriesToWrite,
        Encoding encoding,
        IProgress<ArchiveProgress>? progress)
    {
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

        int total = entriesToWrite.Count;
        for (int i = 0; i < total; i++)
        {
            var (entry, newOffset, isNew, _) = entriesToWrite[i];

            // Encode filename: existing entries reuse their **original raw bytes**
            // so copy-mode never re-encodes them (deleting one entry must not alter
            // the encoding of unrelated entries). New entries use the given encoding.
            byte[] fileNameBytes = entry.RawFileNameBytes ?? encoding.GetBytes(entry.FileName);
            ushort fileNameLen = (ushort)fileNameBytes.Length;
            ushort extraLen = isNew ? (ushort)0 : (ushort)entry.RawExtraField.Length;

            // CDFH fixed fields (46 bytes total)
            writer.Write(0x02014b50);       // Signature
            writer.Write((ushort)20);       // Version made by (2.0)
            writer.Write((ushort)20);       // Version needed (2.0)
            writer.Write(entry.Flags);      // General purpose bit flag
            writer.Write(entry.CompressionMethod); // Compression method
            writer.Write(entry.LastModifiedTime);  // Last mod time
            writer.Write(entry.LastModifiedDate);  // Last mod date
            writer.Write(entry.Crc32);      // CRC32
            writer.Write((uint)entry.CompressedSize);  // Compressed size
            writer.Write((uint)entry.UncompressedSize); // Uncompressed size
            writer.Write(fileNameLen);      // Filename length
            writer.Write(extraLen);         // Extra field length
            writer.Write((ushort)0);        // File comment length
            writer.Write((ushort)0);        // Disk number start
            writer.Write((ushort)0);        // Internal attributes
            writer.Write((uint)0);          // External attributes
            writer.Write((uint)newOffset);  // Local header offset (updated!)

            // Variable-length fields
            writer.Write(fileNameBytes);
            if (!isNew && entry.RawExtraField.Length > 0)
                writer.Write(entry.RawExtraField);

            CoreLog.Trace(
                "ZipBinaryRewriter: CDFH for '{0}' — offset={1}, compMethod={2}, flags=0x{3:X4}",
                entry.FileName, newOffset, entry.CompressionMethod, entry.Flags);
        }
    }

    // ────────────────────── EOCD ─────────────────────────

    /// <summary>
    /// Write the End of Central Directory record.
    /// </summary>
    /// <param name="output">Output stream positioned after the last CDFH.</param>
    /// <param name="centralDirOffset">Byte offset of the first CDFH in the output stream.</param>
    /// <param name="entryCount">Total number of entries in the archive.</param>
    /// <param name="comment">ZIP file comment (UTF-8).</param>
    private static void WriteEocd(
        Stream output,
        long centralDirOffset,
        int entryCount,
        string? comment)
    {
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

        long cdSize = output.Position - centralDirOffset;

        byte[]? commentBytes = null;
        ushort commentLen = 0;
        if (!string.IsNullOrEmpty(comment))
        {
            commentBytes = Encoding.UTF8.GetBytes(comment);
            if (commentBytes.Length > ushort.MaxValue)
            {
                Array.Resize(ref commentBytes, ushort.MaxValue);
            }
            commentLen = (ushort)commentBytes.Length;
        }

        // EOCD fixed fields (22 bytes)
        writer.Write(0x06054b50);             // Signature
        writer.Write((ushort)0);              // Disk number
        writer.Write((ushort)0);              // Disk with central directory
        writer.Write((ushort)entryCount);     // Entry count on this disk
        writer.Write((ushort)entryCount);     // Total entry count
        writer.Write((uint)cdSize);           // Central directory size
        writer.Write((uint)centralDirOffset); // Central directory offset
        writer.Write(commentLen);             // Comment length

        if (commentBytes != null)
            writer.Write(commentBytes);
    }

    // ────────────────────── CRC32 ───────────────────────

    private static readonly uint[] Crc32LookupTable = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
            {
                if ((c & 1) != 0)
                    c = 0xEDB88320 ^ (c >> 1);
                else
                    c >>= 1;
            }
            table[i] = c;
        }
        return table;
    }

    // ────────────────────── Helpers ──────────────────────

    /// <summary>
    /// Copy exactly <paramref name="count"/> bytes from <paramref name="source"/>
    /// to <paramref name="dest"/> in streaming fashion.
    /// </summary>
    private static async Task CopyStreamRangeAsync(
        Stream source,
        Stream dest,
        long count,
        string entryName,
        double basePercent,
        double entryWeight,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[CopyBufferSize];
        long remaining = count;
        long totalRead = 0;
        var lastReportTime = DateTime.Now;
        var reportInterval = TimeSpan.FromMilliseconds(100);

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken)
                .ConfigureAwait(false);
            if (read <= 0)
                throw new EndOfStreamException(
                    $"Unexpected EOF while copying compressed data (expected {count} bytes, got {count - remaining})");
            await dest.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
            remaining -= read;
            totalRead += read;

            var now = DateTime.Now;
            if (now - lastReportTime >= reportInterval || remaining == 0)
            {
                var filePct = count > 0 ? (double)totalRead / count * 100 : 100;
                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = "复制: " + entryName,
                    PercentComplete = Math.Min(90.0, basePercent + totalRead * entryWeight / Math.Max(count, 1)),
                    FilePercentComplete = filePct
                });
                lastReportTime = now;
            }
        }
    }

    /// <summary>
    /// Delete a file, swallowing any I/O errors.
    /// </summary>
    private static void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
