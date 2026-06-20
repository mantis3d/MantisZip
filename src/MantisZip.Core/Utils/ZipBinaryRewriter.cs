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
    int LfhExtraLength
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
public readonly record struct NewEntry(
    string EntryName,
    Stream Data,
    DateTime LastModified,
    long Size);

/// <summary>
/// ZIP binary rewriter providing low-level parsing and copy-mode rewrite capabilities.
/// </summary>
internal static partial class ZipBinaryRewriter
{
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
    internal static List<CdEntry> ReadCentralDirectory(Stream stream, long cdOffset, int entryCount)
    {
        if (cdOffset >= stream.Length)
            throw new ZipCopyModeException(
                $"Central directory offset {cdOffset} is past stream length {stream.Length}");

        var entries = new List<CdEntry>(entryCount);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

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

            // Decode filename for the CdEntry property; raw bytes are not
            // round-tripped here (they come from LFH during copy).
            string fileName = Encoding.UTF8.GetString(fileNameBytes);

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
                LfhExtraLength: 0
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
            List<CdEntry> entries = ReadCentralDirectory(source, cdOffset, entryCount);

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

                // ── Copy-mode validation ─────────────────────────────
                if (entry.CompressionMethod != 0 && entry.CompressionMethod != 8)
                {
                    CoreLog.Info($"Entry '{entry.FileName}': unsupported compression method {entry.CompressionMethod}");
                    throw new ZipCopyModeException(
                        $"Entry '{entry.FileName}' uses unsupported compression method ({entry.CompressionMethod}). " +
                        "Only Store (0) and Deflate (8) are supported by copy-mode.");
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

                double basePct = totalEntries > 0
                    ? (double)processedEntries / totalEntries * 100
                    : 0;

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = "复制: " + entry.FileName,
                    PercentComplete = basePct,
                    FilePercentComplete = 0
                });

                // ── Read and optionally rewrite LFH ──────────────────
                LfhInfo lfhInfo = ReadAndMaybeRewriteLfh(
                    source, entry.LocalHeaderOffset, entry, out byte[] lfhHeader);

                long entryOffset = output.Position;

                // Write LFH header to output
                output.Write(lfhHeader, 0, lfhHeader.Length);

                // Stream-copy compressed data
                await CopyStreamRangeAsync(
                    source, output, entry.CompressedSize, cancellationToken);

                bytesCopied += lfhHeader.Length + entry.CompressedSize;

                // If bit 3 was cleared in the LFH rewrite, propagate the flag change
                // to the CDFH so it matches the LFH (no data descriptor present).
                var entryForCd = lfhInfo.Flags != entry.Flags
                    ? entry with { Flags = lfhInfo.Flags }
                    : entry;
                entriesToWrite.Add((entryForCd, entryOffset, false, lfhHeader));
                processedEntries++;

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = "复制: " + entry.FileName,
                    PercentComplete = (double)processedEntries / totalEntries * 100,
                    FilePercentComplete = 100
                });

                CoreLog.Trace("ZipBinaryRewriter: copied entry '{0}' ({1} bytes)",
                    entry.FileName, entry.CompressedSize);
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

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = "压缩: " + newEntry.EntryName,
                        PercentComplete = basePct,
                        FilePercentComplete = 0
                    });

                    long entryOffset = output.Position;

                    // Compress and write the new entry's LFH + data
                    var (lfhBytes, compressedSize, crc32) =
                        CompressNewEntry(output, newEntry, encoding);

                    bytesAdded += lfhBytes.Length + compressedSize;

                    // Build a synthetic CdEntry for the central directory
                    var (dosDate, dosTime) = DateTimeToDos(newEntry.LastModified);
                    byte[] fileNameBytes = encoding.GetBytes(newEntry.EntryName);

                    var syntheticEntry = new CdEntry(
                        FileName: newEntry.EntryName,
                        Crc32: crc32,
                        CompressedSize: compressedSize,
                        UncompressedSize: newEntry.Size,
                        CompressionMethod: 8, // Deflate
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

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = "压缩: " + newEntry.EntryName,
                        PercentComplete = (double)processedEntries / totalEntries * 100,
                        FilePercentComplete = 100
                    });

                    CoreLog.Trace("ZipBinaryRewriter: added new entry '{0}' ({1} bytes compressed)",
                        newEntry.EntryName, compressedSize);
                }
            }

            // ══════════════════════════════════════════════════════════
            // Phase 3: Write central directory
            // ══════════════════════════════════════════════════════════
            long centralDirStart = output.Position;
            WriteCentralDirectory(output, entriesToWrite, encoding, progress);

            // ══════════════════════════════════════════════════════════
            // Phase 4: Write EOCD
            // ══════════════════════════════════════════════════════════
            string effectiveComment = comment ?? existingComment ?? string.Empty;
            WriteEocd(output, centralDirStart, entriesToWrite.Count, effectiveComment);

            // ── Finalize (close then atomically replace) ─────────────
            output.Dispose();
            output = null;

            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(tempDestPath, destPath);

            int copyCount = entriesToWrite.Count(e => !e.IsNew);
            int addCount = entriesToWrite.Count - copyCount;

            CoreLog.Info(
                $"ZipBinaryRewriter: rewrite complete — {copyCount} entries copied ({bytesCopied} bytes), " +
                $"{addCount} entries added ({bytesAdded} bytes)");
            CoreLog.Exit();

            return new RewriteResult(copyCount, bytesCopied, addCount, bytesAdded);
        }
        catch (OperationCanceledException)
        {
            CoreLog.Info("ZipBinaryRewriter: cancelled");
            CleanupFile(tempDestPath);
            throw;
        }
        catch (Exception ex)
        {
            if (ex is ZipCopyModeException)
                CoreLog.Info("ZipBinaryRewriter: copy-mode not supported, falling back");
            else
                CoreLog.Error("ZipBinaryRewriter: error during rewrite", ex);
            CleanupFile(tempDestPath);
            throw;
        }
        finally
        {
            source?.Dispose();
            output?.Dispose();
        }
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
        Stream output, in NewEntry entry, Encoding encoding)
    {
        // ── Read source data ──────────────────────────────────────────
        byte[] data = new byte[entry.Size];
        int totalRead = 0;
        while (totalRead < entry.Size)
        {
            int read = entry.Data.Read(data, totalRead, (int)(entry.Size - totalRead));
            if (read <= 0) break;
            totalRead += read;
        }

        // ── CRC32 ────────────────────────────────────────────────────
        uint crc32 = ComputeCrc32(data);

        // ── Deflate compress ──────────────────────────────────────────
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(data, 0, data.Length);
            }
            compressed = ms.ToArray();
        }

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
        // Flags (0 = no encryption, no data descriptor)
        BitConverter.GetBytes((ushort)0).CopyTo(lfh, 6);
        // Compression method (8 = Deflate)
        BitConverter.GetBytes((ushort)8).CopyTo(lfh, 8);
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

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "正在写入中央目录: " + entry.FileName,
                PercentComplete = 90 + (int)((double)i / total * 10),
                FilePercentComplete = 100
            });

            // Encode filename using the detected encoding
            byte[] fileNameBytes = encoding.GetBytes(entry.FileName);
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

    /// <summary>
    /// Compute CRC-32 (PKZip variant) for a byte array.
    /// Uses the standard polynomial 0xEDB88320 with a pre-computed lookup table.
    /// </summary>
    private static uint ComputeCrc32(byte[] data)
    {
        // Pre-computed CRC-32 lookup table
        ReadOnlySpan<uint> table = Crc32LookupTable;
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            crc = table[(int)((crc ^ data[i]) & 0xFF)] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFF;
    }

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
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long remaining = count;
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
