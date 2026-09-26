# Learnings — Preview Magic Detection

## P1-S1: FileFormat 枚举 + FileFormatDetector 核心

### Convention
- `CoreLog.Info()` is DEBUG-only (`[Conditional("DEBUG")]`) and safe for internal logging in the detector
- `FileFormat` enum is defined in `FileFormatInfo.cs` and is shared between Plan A (format parsers) and Plan B (magic detection)
- New enum values must be appended AFTER the last existing value (`Udf`), never inserted or reordered
- All files in Core use file-scoped namespace `namespace MantisZip.Core.Utils;`

### Design choices
- Magic byte detection follows priority order: most specific (longest/reliable) signatures first, more generic ones last
- Torrent detection (bencode `d` prefix) is intentionally last due to being the weakest single-byte signature
- PE detection is a two-step process: MZ at offset 0, then read `e_lfanew` at 0x3C to find `PE\0\0` signature
- ZIP subtype detection (`DetectZipSubtype`) scans local file headers for known filenames: `mimetype` → Epub, `[Content_Types].xml` → OfficeOpenXml, `META-INF/` → Odt
- RIFF-based formats (WebP/WAV/AVI) share the same header signature `52 49 46 46` at offset 0 and differ at offset 8
- `using System;` is a global using in the Core project — don't add it explicitly to new files
- `DetectByExtension()` normalizes the extension by first ensuring it starts with `.` for consistency

### File structure
- `FileFormatDetector.cs` (new) — static class, ~571 lines with 35+ magic byte patterns + DetectZipSubtype + DecompressDeflateBlock
- `FileFormatHelper.cs` (new) — static class with `GetDisplayName()` switch expression
- `FileFormatInfo.cs` (modified) — 11 enum values appended: `Ogg`, `Odt`, `Ods`, `Odp`, `Rtf`, `DjVu`, `Xps`, `Woff2`, `Fits`, `Vhdx`, `Parquet`
- `ArchiveEntryExtractor.cs` (modified) — +224 lines: ExtractHeadAsync, ExtractHeadTailAsync, 7z solid fallback, MP4 moov box parsing (FindBox, ParseMvhdDuration, ParseTkhdResolution, TryParseMp4TailMetadata)
- `AppSettings.cs` (modified) — +PreviewHeadSize property (default 4096)

## P1-S2: ExtractHeadAsync + ExtractHeadTailAsync

- SharpSevenZipExtractor.IsSolid property exists and works for 7z solid detection — cleaner than try-catch approach
- Tail extraction limited to entries <10MB to avoid decompressing entire large archives for tail bytes
- ExtractTailSync skips tail for: solid 7z, Tar/Gz, entries >10MB

## P1-S3: ZIP Subtype Detection

- Deflate decompression via `System.IO.Compression.DeflateStream` (no zlib header)
- [Content_Types].xml content matching via MIME type strings for DOCX/XLSX/PPTX
- META-INF/manifest.xml content matching for ODT/ODS/ODP
- mimetype entry is always Store (uncompressed) in EPUB files

## P1-S4: MP4 Tail Detection

- moov box often at end of MP4 files; contains mvhd (duration) and tkhd (resolution)
- FindBox searches ISOBMFF box hierarchy by type name (big-endian)
- mvhd supports version 0 (32-bit) and version 1 (64-bit) timescale/duration

## Phase 2 — WPF Implementation

### Integration Point
- Magic detection inserted in `ShowPreviewAsync` (MainWindow.Preview.cs) right after `ShowPreviewLoading()` and before the 17 ext-based `if/else if` branches
- Detection runs only when `AppSettings.EnableFormatDetection == true` (default: true)
- Uses `ArchiveEntryExtractor.ExtractHeadAsync()` with `AppSettings.PreviewHeadSize` (default 4096 bytes)
- Calls `FileFormatDetector.Detect(head, head.Length)` → `FileFormatHelper.GetDisplayName(format)`
- Sets `PreviewHeader.Text` to `"📄 {item.Name} → {realFormatName}"`
- Format-specific methods (ShowPePreview, ShowImagePreviewAsync, etc.) overwrite PreviewHeader.Text with their own format-specific headers — this is intentional and correct
- For unsupported formats (the final `else { ShowUnsupportedPreview(item); }`), the magic detection header persists, showing the real format name
- `OperationCanceledException` is rethrown to support file switching cancellation
- Other exceptions are caught and logged via `App.LogDebug`

### AppSettings
- `EnableFormatDetection` added to the 预览 (Preview) section of AppSettings.cs (type: bool, default: true)
- No SettingsWindow UI toggle added yet — Avalonia port should add this in the settings UI
- `PreviewHeadSize` already existed from Phase 1 (default 4096)

### File Modified
- `src/MantisZip.UI/MainWindow/Preview/MainWindow.Preview.cs` — +~35 lines of magic detection code
- `src/MantisZip.UI/AppSettings.cs` — +1 property (EnableFormatDetection)

### No Avalonia Blockage
- Avalonia `MantisZip.UI.Avalonia/` still has zero source files
- Phase 2 was successfully implemented in WPF as WPF is the active UI framework
- If Avalonia port happens, the same ~50 lines of integration code must be replicated in the Avalonia `MainWindow.Preview.cs`

## Bugfix: Plain text files not detected by magic detection

### Root cause
`FileFormatDetector.Detect()` returns `FileFormat.Unknown` for plain text files because text has no magic byte signature. The `ShowPreviewAsync` code also didn't call `DetectByExtension()` as fallback. So text files with wrong/no extension showed no format name.

### Fix (two parts)

**Part A — `FileFormatDetector.Detect()` text heuristics**:
Added `LooksLikeText()` heuristic as the final check before returning `Unknown`:
- Scans up to 512 bytes
- Rejects if null byte ratio > 1% (binary indicator)
- Counts printable ASCII (0x20-0x7E), whitespace (TAB/LF/CR/FF), and UTF-8 multi-byte sequences
- For files with mostly UTF-8 sequences (e.g., Chinese text): allows up to 30% non-printable
- For ASCII text: allows up to 20% non-printable
- Rejects files < 8 bytes

**Part B — `ShowPreviewAsync` extension fallback**:
When `Detect()` returns `Unknown`, now calls `FileFormatDetector.DetectByExtension(ext)` as fallback.
This way, a `.txt` file (even without magic bytes) shows "文本" in the header.

### Files modified
- `src/MantisZip.Core/Utils/FileFormatDetector.cs` — +~50 lines: `LooksLikeText()` + text heuristics check
- `src/MantisZip.UI/MainWindow/Preview/MainWindow.Preview.cs` — +5 lines: `DetectByExtension` fallback
