# Phase 1: ZipEngine CompressAsync Migration to SharpCompress ZipWriter

## SharpCompress ZipWriter API (v0.48.1)

### Class: `SharpCompress.Writers.Zip.ZipWriter`
- Public constructor: `ZipWriter(Stream stream, ZipWriterOptions options)`
- Implements `IWriter`, `IAsyncWriter`, `IDisposable`, `IAsyncDisposable`
- `Stream WriteToStream(string entryPath, ZipWriterEntryOptions options)` — returns writable stream for manual write (used for progress reporting)
- `IWriter OpenWriter(Stream stream, ZipWriterOptions options)` — static factory (returns IWriter interface)

### Class: `ZipWriterOptions`
- Constructor: `ZipWriterOptions(CompressionType)`, `ZipWriterOptions(CompressionType, int)`, `ZipWriterOptions(WriterOptions)`
- Properties: `CompressionType`, `CompressionLevel`, `LeaveStreamOpen`, `ArchiveEncoding`, `Progress`, `Providers`, `ArchiveComment`, `UseZip64`
- **No encryption/password property**

### Class: `ZipWriterEntryOptions`
- Properties: `CompressionType?`, `CompressionLevel?`, `EntryComment`, `ModificationDateTime?`, `EnableZip64?`
- **No encryption/password property**

### Encryption support
- **SharpCompress ZipWriter does NOT support writing encrypted ZIPs**
- `PkwareTraditionalCryptoStream` and `WinzipAesCryptoStream` exist but only for reading/decryption
- `CryptoMode` enum: `Encrypt = 0`, `Decrypt = 1`
- `WinzipAesKeySize` enum: `KeySize128 = 1`, `KeySize192 = 2`, `KeySize256 = 3`

## Implementation decisions
- **Unencrypted path**: Uses `new ZipWriter(fsOut, writerOptions)` with `WriteToStream` for per-file progress
- **Encrypted path**: Falls back to SharpZipLib `ZipOutputStream` (since ZipWriter lacks encryption)
- Entry paths use forward slashes for SharpCompress (`relativePath.Replace('\\', '/')`)
- Comment via `ZipWriterOptions.ArchiveComment` (not ZipCommentHelper)
- `ReadFileWithRetry` renamed to `ReadFileWithRetryZipOutputStream` for the old SharpZipLib path
- New `ReadFileWithRetry` overload takes `ZipWriter` parameter

## Verification
- `dotnet build src\MantisZip.Core\MantisZip.Core.csproj` — passes (0 errors, 0 warnings)
- `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` — 183/183 pass

## [2026-06-12] Phase 2: AddToArchiveAsync Migration

### Key changes
- Old entry info collection: `OpenZipFile`/`ZipEntry` → `OpenArchiveWithEncodingFallback`/`IArchiveEntry`
- Phase 1 extraction: `zipFile.GetInputStream(entry)` → `entry.OpenEntryStream()`
- Entry names: `entry.Name` → `entry.Key ?? string.Empty`
- Timestamps: `entry.DateTime` → `entry.LastModifiedTime ?? DateTime.MinValue`
- Phase 3 recompression: Branched into encrypted (ZipOutputStream fallback) and unencrypted (ZipWriter)
- Encrypted branch follows same pattern as Phase 1 CompressAsync

### Critical fix: OpenArchiveWithEncodingFallback
Changed from `ArchiveFactory.OpenArchive(string path, ReaderOptions)` to `ArchiveFactory.OpenArchive(Stream stream, ReaderOptions)` using `File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)` — allows atomic `File.Delete` + `File.Move` in Phase 4 (SharpCompress's string-based overload holds exclusive file lock that prevents deletion on Windows).

# Phase 2: AddToArchiveAsync Migration

## Changes made
1. **Old entry info collection** (lines 618→now 610): Changed from `OpenZipFile`/`ZipEntry` to `OpenArchiveWithEncodingFallback`/`IArchiveEntry`
2. **Phase 1 extraction** (lines 658→now 647): Changed from `OpenZipFile`/`zipFile.GetInputStream(entry)` to `OpenArchiveWithEncodingFallback`/`entry.OpenEntryStream()`. Entry name now via `entry.Key ?? string.Empty`, timestamp via `entry.LastModifiedTime ?? DateTime.MinValue`
3. **Phase 3 recompression** (lines 738→now 727): Changed from `ZipOutputStream` to branched approach — `ZipWriter` for unencrypted, `ZipOutputStream` (SharpZipLib) fallback for encrypted. Same pattern as `CompressAsync` in Phase 1.

## Critical bug discovered
- `OpenArchiveWithEncodingFallback` used `ArchiveFactory.OpenArchive(string path, ...)` which opens a `FileStream` internally. Even after `IArchive.Dispose()`, the file handle wasn't released in time for `File.Delete(archivePath)` in the atomic replace phase (Phase 4).
- **Fix**: Changed `OpenArchiveWithEncodingFallback` to open a `FileStream` with `FileShare.Read | FileShare.Delete` and pass it to `ArchiveFactory.OpenArchive(Stream, ReaderOptions)`. This allows `File.Delete` to succeed even while the archive stream is still held.
- Both the initial UTF-8 open and the GBK fallback open use `FileShare.Delete`.

## API mapping (SharpZipLib → SharpCompress for AddToArchiveAsync)
| Old | New |
|-----|-----|
| `OpenZipFile(archivePath)` | `OpenArchiveWithEncodingFallback(archivePath)` |
| `ZipEntry entry` | `var entry` (IArchiveEntry) |
| `entry.Name` | `entry.Key ?? string.Empty` |
| `entry.DateTime` | `entry.LastModifiedTime ?? DateTime.MinValue` |
| `zipFile.GetInputStream(entry)` | `entry.OpenEntryStream()` |

# Phase 3: DeleteEntriesAsync Migration

## Changes made
1. **Pass 1** (verification + keep list): `OpenZipFile`/`ZipEntry` → `OpenArchiveWithEncodingFallback`/`IArchiveEntry`. Entry name via `entry.Key ?? string.Empty`.
2. **Pass 2** (count keep bytes): Same replacements. Also fixed: old code passed `password` to `OpenZipFile`, now uses same with `OpenArchiveWithEncodingFallback`.
3. **Pass 3** (extract kept entries): `zipFile.GetInputStream(entry)` → `entry.OpenEntryStream()`, `entry.DateTime` → `entry.LastModifiedTime ?? DateTime.MinValue`.
4. **Recompress phase**: `ZipOutputStream` → `ZipWriter` (no encryption branching needed — `DeleteEntriesAsync` has no `ArchiveOptions` parameter, so encryption was never used in this path).

## Key differences from AddToArchiveAsync
- No `ArchiveOptions` parameter — no comment, no encryption for recompress. Pure `ZipWriter` with fixed `CompressionLevel = 6`.
