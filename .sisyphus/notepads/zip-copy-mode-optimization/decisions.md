# Decisions - ZIP Copy-Mode Optimization

## Step 4 — ZipEngine.DeleteEntriesAsync copy-mode + encrypted support

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 23 | Copy-mode insertion point | After empty-entry guard, before legacy path setup | Same rationale as AddToArchiveAsync — if copy-mode succeeds, skip all heavy extraction/recompression; if it fails with ZipCopyModeException, fall through cleanly |
| 24 | No explicit encryption guard before copy-mode | Let ZipBinaryRewriter.RewriteAsync throw ZipCopyModeException naturally | Copy-mode validates each entry internally (bit 0 check); no need for duplicate pre-check; encrypted entries cause clean fallback |
| 25 | Keep set derived from `archive.Entries` | Re-open archive with `OpenArchiveWithEncodingFallback` to enumerate all entries, filter out deleted ones | `archive.Entries` provides the canonical list of entry names; same pattern as Pass 1 validation |
| 26 | `keepSet.Count == 0` check before RewriteAsync | Delete archive and return early | Avoids calling RewriteAsync with an empty keep set (which would produce an empty central directory); aligns with existing `keepEntryCount == 0` guard |
| 27 | `sourceIsEncrypted` detection | Separate `using` block after Pass 1, before Pass 2 | Opens archive a third time but keeps concerns clean; Pass 1 archive is already disposed; no need to keep archive open across phases |
| 28 | SharpSevenZip for encrypted recompression | Same pattern as AddToArchiveAsync and CompressAsync encrypted paths | Verified pattern with `CompressFilesEncrypted`, `commonRoot` calculation, and progress events; reuses `SevenZipEngine.EnsureLibraryPath()` |
| 29 | Compression level for encrypted path | `SharpSevenZip.CompressionLevel.Normal` (hardcoded) | DeleteEntriesAsync has no `ArchiveOptions` parameter to read the user's preferred level; Normal is sensible default matching existing behavior (`MapCompressionLevelToS7Z(6)` → Normal) |
| 30 | Compression level for non-encrypted path | `6` (unchanged from original) | Preserves existing behavior per task requirement |
| 31 | `comment: null` in copy-mode block | Explicitly passes null to preserve original comment | Same as AddToArchiveAsync pattern; ZipBinaryRewriter uses existing EOCD comment when `comment` is null |

## Step 3 — ZipEngine.AddToArchiveAsync copy-mode integration

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 17 | Insertion point | After `newFiles` collection block, before Phase 1 (legacy extract-recompress) | Copy-mode fast path executes before work estimation / temp dir creation; if it succeeds, we skip all heavy operations |
| 18 | Encryption check | Inline `options.Encrypt && !string.IsNullOrEmpty(options.Password)` | Matches existing `isEncrypted` expression at original line 871; avoids introducing a new variable for shared use |
| 19 | Sync over async | `.GetAwaiter().GetResult()` on `RewriteAsync` | Code runs inside a synchronous `Task.Run(() => { ... })` lambda; changing it to `async` would require altering the entire method structure |
| 20 | Temp file cleanup on fallback | Delete `tempArchiveFast` in `ZipCopyModeException` catch before falling through | Prevents orphan `.tmp` files when copy-mode is rejected; follows same pattern as `ZipBinaryRewriter`'s own `CleanupFile` |
| 21 | Stream disposal | Explicit `streamsToDispose` list with `finally` block | `NewEntry.Data` streams are caller-owned per API contract; must be disposed before falling through to legacy path; inner try-finally ensures cleanup even if other exceptions occur |
| 22 | Encoding detection | `ZipHasUtf8Flag(archivePath) ? Encoding.UTF8 : Encoding.GetEncoding("gbk")` | Reuses existing private method from `ZipEngine` class; matches the encoding fallback pattern used in `OpenArchiveWithEncodingFallback` |

## Step 1 — ZipBinaryRewriter parsing infrastructure

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 1 | EOCD scanning approach | Buffer scan (read window into byte[] + backward scan) | Matches existing `ZipHasUtf8Flag` and `ZipCommentHelper.FindEocdOffset` patterns; single read is more efficient than per-byte BinaryReader seeks |
| 2 | CDFH parsing | `BinaryReader` for structured field reads | Task requirement; follows ZipEngine.cs convention; clean field-by-field parsing |
| 3 | ZIP64 detection | Check for ZIP64 EOCD locator signature (0x07064b50) at `eocdPos - 20` | Per ZIP spec, the locator is stored immediately before the EOCD record (20 bytes); only checked when entryCount=0xFFFF or cdOffset=0xFFFFFFFF |
| 4 | Comment fallback | UTF-8 decode with fallback to `Encoding.Default` | Default .NET UTF8 encoder replaces invalid sequences silently; explicit catch of `DecoderFallbackException` ensures forward-compat if custom fallback is configured |
| 5 | Raw bytes for filenames | CDFH filename stored as `byte[]` in `CdEntry`; decoded string only for `FileName` property | Avoids GBK/UTF-8 encoding distortion — raw bytes are used during LFH comparison in Step 2 |
| 6 | Partial class | `ZipBinaryRewriter` declared `partial` | Step 2 `RewriteAsync` will be in a separate partial declaration file |
| 7 | `ZipCopyModeException` | Simple exception with two constructors (message + message+inner) | Lightweight; no serialization support needed for this use case |

## Step 2 — RewriteAsync implementation

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 8 | CRC32 implementation | Manual implementation using standard polynomial 0xEDB88320 with 256-entry lookup table | `System.IO.Hashing.Crc32` requires a NuGet package add; manual CRC32 is trivial and well-known; avoids adding dependencies |
| 9 | Bit 3 LFH rewrite | Rewrite LFH bytes in memory: clear bit 3 in flags, write correct CRC/CompressedSize/UncompressedSize from CDFH | Produces compliant ZIP where every LFH has correct CRC/sizes; Data Descriptor is naturally skipped by copying exactly CompressedSize bytes after LFH |
| 10 | New entry compression | Deflate via `System.IO.Compression.DeflateStream` to MemoryStream, then write LFH + compressed data to output | DeflateStream produces raw deflate (no zlib header), matching ZIP format spec; MemoryStream used to obtain compressed size before writing LFH |
| 11 | Encoding for filenames | Use detected `encoding` parameter (UTF-8/GBK) for both LFH and CDFH filenames | Consistency between LFH and CDFH; re-encode CdEntry.FileName (decoded as UTF-8 in ReadCentralDirectory) back using detected encoding |
| 12 | BinaryWriter for CDFH/EOCD | BinaryWriter with Encoding.UTF8 (leaveOpen:true) | Clean structured writes for many fixed-size fields; LFH uses raw byte[] for flexibility |
| 13 | Temp file + atomic replace | Write to {destPath}.tmp, rename on success, delete on error | Prevents corrupt semi-products; original file untouched on crash |
| 14 | Streaming copy | 81920-byte buffer async streaming for compressed data copy | Entries up to ~4GB (non-ZIP64 limit); avoids large memory allocations |
| 15 | Synthetic CdEntry for new entries | Construct CdEntry from compression results with CompressionMethod=8, Flags=0 | Allows unified central directory writing path for both kept and new entries |
| 16 | Error cleanup | Clean up .tmp file on ALL exceptions (including ZipCopyModeException) before rethrowing | Caller's fallback path doesn't know about the temp file |

