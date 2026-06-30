using System.IO.Compression;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// 魔数检测引擎。通过文件头部/尾部字节判断 FileFormat。
/// </summary>
public static class FileFormatDetector
{
    /// <summary>
    /// 根据文件头部字节（以及可选的尾部字节）检测文件格式。
    /// </summary>
    /// <param name="head">文件头部字节数组（至少前 64 字节）。</param>
    /// <param name="length">实际使用的字节数（head 的有效长度）。</param>
    /// <param name="tail">可选的尾部字节（当前主要用于 RIFF 和 PE 校验）。</param>
    /// <returns>检测到的 <see cref="FileFormat"/>，无法识别时返回 Unknown。</returns>
    public static FileFormat Detect(byte[] head, int length, byte[]? tail = null)
    {
        if (head == null || length < 2)
            return FileFormat.Unknown;

        // ── 图像 ──────────────────────────────────────────────────────

        // 1. PNG: 89 50 4E 47 0D 0A 1A 0A (8 bytes)
        if (length >= 8 &&
            head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 &&
            head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
        {
            CoreLog.Info("Detect: PNG magic matched");
            return FileFormat.Png;
        }

        // 2. JPEG: FF D8 FF (3 bytes)
        if (length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            CoreLog.Info("Detect: JPEG magic matched");
            return FileFormat.Jpeg;
        }

        // 3. GIF: 47 49 46 38 37 61 (GIF87a) / 47 49 46 38 39 61 (GIF89a) — 6 bytes
        if (length >= 6 &&
            head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x38 &&
            ((head[4] == 0x37 && head[5] == 0x61) || (head[4] == 0x39 && head[5] == 0x61)))
        {
            CoreLog.Info("Detect: GIF magic matched");
            return FileFormat.Gif;
        }

        // 4. BMP: 42 4D (2 bytes, "BM")
        if (length >= 2 && head[0] == 0x42 && head[1] == 0x4D)
        {
            CoreLog.Info("Detect: BMP magic matched");
            return FileFormat.Bmp;
        }

        // 5. ICO: 00 00 01 00 (4 bytes)
        if (length >= 4 &&
            head[0] == 0x00 && head[1] == 0x00 && head[2] == 0x01 && head[3] == 0x00)
        {
            CoreLog.Info("Detect: ICO magic matched");
            return FileFormat.Ico;
        }

        // 6. WebP: RIFF (52 49 46 46) + 4 bytes + WEBP (57 45 42 50) — 12 bytes
        if (length >= 12 &&
            head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46 &&
            head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50)
        {
            CoreLog.Info("Detect: WebP magic matched");
            return FileFormat.WebP;
        }

        // 7. EXR: 76 2F 31 01 (4 bytes, "\x76\x2f\x31\x01")
        if (length >= 4 &&
            head[0] == 0x76 && head[1] == 0x2F && head[2] == 0x31 && head[3] == 0x01)
        {
            CoreLog.Info("Detect: EXR magic matched");
            return FileFormat.Exr;
        }

        // 8. HDR: 23 3F 52 41 44 49 41 4E 43 45 (10 bytes, "#?RADIANCE")
        if (length >= 10 &&
            head[0] == 0x23 && head[1] == 0x3F && head[2] == 0x52 && head[3] == 0x41 &&
            head[4] == 0x44 && head[5] == 0x49 && head[6] == 0x41 && head[7] == 0x4E &&
            head[8] == 0x43 && head[9] == 0x45)
        {
            CoreLog.Info("Detect: HDR magic matched");
            return FileFormat.Hdr;
        }

        // ── 文档 ──────────────────────────────────────────────────────

        // 9. PDF: 25 50 44 46 2D (5 bytes, "%PDF-")
        if (length >= 5 &&
            head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46 &&
            head[4] == 0x2D)
        {
            CoreLog.Info("Detect: PDF magic matched");
            return FileFormat.Pdf;
        }

        // ── 压缩包 ────────────────────────────────────────────────────

        // 10. ZIP: 50 4B 03 04 (4 bytes) → check subtype
        if (length >= 4 &&
            head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04)
        {
            var subtype = DetectZipSubtype(head, length);
            CoreLog.Info($"Detect: ZIP magic matched, subtype={subtype}");
            return subtype;
        }

        // 11. 7z: 37 7A BC AF 27 1C (6 bytes)
        if (length >= 6 &&
            head[0] == 0x37 && head[1] == 0x7A && head[2] == 0xBC && head[3] == 0xAF &&
            head[4] == 0x27 && head[5] == 0x1C)
        {
            CoreLog.Info("Detect: 7z magic matched");
            return FileFormat.SevenZip;
        }

        // 12. RAR: 52 61 72 21 1A 07 00 (7 bytes, RAR1.5)
        //     or:  52 61 72 21 1A 07 01 00 (8 bytes, RAR5)
        if (length >= 7 &&
            head[0] == 0x52 && head[1] == 0x61 && head[2] == 0x72 && head[3] == 0x21 &&
            head[4] == 0x1A && head[5] == 0x07 &&
            (head[6] == 0x00 || (length >= 8 && head[6] == 0x01 && head[7] == 0x00)))
        {
            CoreLog.Info("Detect: RAR magic matched");
            return FileFormat.Rar;
        }

        // 13. GZ: 1F 8B (2 bytes)
        if (length >= 2 && head[0] == 0x1F && head[1] == 0x8B)
        {
            CoreLog.Info("Detect: GZ magic matched");
            return FileFormat.Gz;
        }

        // 14. BZ2: 42 5A 68 (3 bytes, "BZh")
        if (length >= 3 && head[0] == 0x42 && head[1] == 0x5A && head[2] == 0x68)
        {
            CoreLog.Info("Detect: BZ2 magic matched");
            return FileFormat.Bz2;
        }

        // 15. XZ: FD 37 7A 58 5A 00 (6 bytes)
        if (length >= 6 &&
            head[0] == 0xFD && head[1] == 0x37 && head[2] == 0x7A && head[3] == 0x58 &&
            head[4] == 0x5A && head[5] == 0x00)
        {
            CoreLog.Info("Detect: XZ magic matched");
            return FileFormat.Xz;
        }

        // 16. Zstd: 28 B5 2F FD (4 bytes)
        if (length >= 4 &&
            head[0] == 0x28 && head[1] == 0xB5 && head[2] == 0x2F && head[3] == 0xFD)
        {
            CoreLog.Info("Detect: Zstd magic matched");
            return FileFormat.Zstd;
        }

        // ── 音频 ──────────────────────────────────────────────────────

        // 17. MP3 (ID3v2): 49 44 33 (3 bytes, "ID3")
        if (length >= 3 && head[0] == 0x49 && head[1] == 0x44 && head[2] == 0x33)
        {
            CoreLog.Info("Detect: MP3 (ID3v2) magic matched");
            return FileFormat.Mp3;
        }

        // 18. FLAC: 66 4C 61 43 (4 bytes, "fLaC")
        if (length >= 4 &&
            head[0] == 0x66 && head[1] == 0x4C && head[2] == 0x61 && head[3] == 0x43)
        {
            CoreLog.Info("Detect: FLAC magic matched");
            return FileFormat.Flac;
        }

        // 19. WAV: RIFF (52 49 46 46) + 4 bytes + WAVE (57 41 56 45) — 12 bytes
        if (length >= 12 &&
            head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46 &&
            head[8] == 0x57 && head[9] == 0x41 && head[10] == 0x56 && head[11] == 0x45)
        {
            CoreLog.Info("Detect: WAV magic matched");
            return FileFormat.Wav;
        }

        // 20. OGG: 4F 67 67 53 (4 bytes, "OggS")
        if (length >= 4 &&
            head[0] == 0x4F && head[1] == 0x67 && head[2] == 0x67 && head[3] == 0x53)
        {
            CoreLog.Info("Detect: OGG magic matched");
            return FileFormat.Ogg;
        }

        // ── 视频 ──────────────────────────────────────────────────────

        // 21. MP4: 'ftyp' box at offset 4 — 66 74 79 70 (4 bytes)
        if (length >= 8 &&
            head[4] == 0x66 && head[5] == 0x74 && head[6] == 0x79 && head[7] == 0x70)
        {
            CoreLog.Info("Detect: MP4 (ftyp) magic matched");
            return FileFormat.Mp4;
        }

        // 22. MKV/WebM (EBML): 1A 45 DF A3 (4 bytes)
        if (length >= 4 &&
            head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3)
        {
            // WebM is a subset of MKV. Without parsing DocType, default to MKV.
            CoreLog.Info("Detect: Matroska/EBML magic matched");
            return FileFormat.Mkv;
        }

        // 23. FLV: 46 4C 56 01 (4 bytes, "FLV\x01")
        if (length >= 4 &&
            head[0] == 0x46 && head[1] == 0x4C && head[2] == 0x56 && head[3] == 0x01)
        {
            CoreLog.Info("Detect: FLV magic matched");
            return FileFormat.Flv;
        }

        // 24. WMV (ASF): 30 26 B2 75 8E 66 CF 11 (8 bytes)
        if (length >= 8 &&
            head[0] == 0x30 && head[1] == 0x26 && head[2] == 0xB2 && head[3] == 0x75 &&
            head[4] == 0x8E && head[5] == 0x66 && head[6] == 0xCF && head[7] == 0x11)
        {
            CoreLog.Info("Detect: WMV/ASF magic matched");
            return FileFormat.Wmv;
        }

        // ── 字体 ──────────────────────────────────────────────────────

        // 25. TTF (TrueType): 00 01 00 00 (4 bytes, sfVersion=0x00010000)
        if (length >= 4 &&
            head[0] == 0x00 && head[1] == 0x01 && head[2] == 0x00 && head[3] == 0x00)
        {
            CoreLog.Info("Detect: TTF magic matched");
            return FileFormat.Ttf;
        }

        // 26. OTF (OpenType with CFF): 4F 54 54 4F (4 bytes, "OTTO")
        if (length >= 4 &&
            head[0] == 0x4F && head[1] == 0x54 && head[2] == 0x54 && head[3] == 0x4F)
        {
            CoreLog.Info("Detect: OTF magic matched");
            return FileFormat.Otf;
        }

        // 27. WOFF: 77 4F 46 46 (4 bytes, "wOFF")
        if (length >= 4 &&
            head[0] == 0x77 && head[1] == 0x4F && head[2] == 0x46 && head[3] == 0x46)
        {
            CoreLog.Info("Detect: WOFF magic matched");
            return FileFormat.Woff;
        }

        // 28. WOFF2: 77 4F 46 32 (4 bytes, "wOF2")
        if (length >= 4 &&
            head[0] == 0x77 && head[1] == 0x4F && head[2] == 0x46 && head[3] == 0x32)
        {
            CoreLog.Info("Detect: WOFF2 magic matched");
            return FileFormat.Woff2;
        }

        // ── 可执行文件 ────────────────────────────────────────────────

        // 29. PE: 4D 5A (MZ) at offset 0 + PE signature at offset from 0x3C
        if (length >= 2 && head[0] == 0x4D && head[1] == 0x5A)
        {
            if (IsPe(head, length))
            {
                CoreLog.Info("Detect: PE magic matched");
                return FileFormat.Pe;
            }
        }

        // ── 数据库 ────────────────────────────────────────────────────

        // 30. SQLite: 53 51 4C 69 74 65 (6 bytes, "SQLite")
        if (length >= 6 &&
            head[0] == 0x53 && head[1] == 0x51 && head[2] == 0x4C && head[3] == 0x69 &&
            head[4] == 0x74 && head[5] == 0x65)
        {
            CoreLog.Info("Detect: SQLite magic matched");
            return FileFormat.Sqlite;
        }

        // ── 其他 ──────────────────────────────────────────────────────

        // 31. LNK: 4C 00 00 00 01 14 02 00 (8 bytes)
        if (length >= 8 &&
            head[0] == 0x4C && head[1] == 0x00 && head[2] == 0x00 && head[3] == 0x00 &&
            head[4] == 0x01 && head[5] == 0x14 && head[6] == 0x02 && head[7] == 0x00)
        {
            CoreLog.Info("Detect: LNK magic matched");
            return FileFormat.Lnk;
        }

        // 32. CER (OLE2/CFB): D0 CF 11 E0 A1 B1 1A E1 (8 bytes)
        if (length >= 8 &&
            head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0 &&
            head[4] == 0xA1 && head[5] == 0xB1 && head[6] == 0x1A && head[7] == 0xE1)
        {
            CoreLog.Info("Detect: OLE2/CFB (CER) magic matched");
            return FileFormat.Cer;
        }

        // 33. STL (ASCII solid): 73 6F 6C 69 64 (5 bytes, "solid")
        if (length >= 5 &&
            head[0] == 0x73 && head[1] == 0x6F && head[2] == 0x6C && head[3] == 0x69 &&
            head[4] == 0x64)
        {
            CoreLog.Info("Detect: STL (solid) magic matched");
            return FileFormat.Stl;
        }

        // 34. Torrent (Bencode dictionary): 64 (1 byte, 'd')
        if (length >= 1 && head[0] == 0x64)
        {
            CoreLog.Info("Detect: Torrent (bencode dict) magic matched");
            return FileFormat.Torrent;
        }

        // 35. AVI: RIFF (52 49 46 46) + 4 bytes + AVI (41 56 49 20) — 12 bytes
        if (length >= 12 &&
            head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46 &&
            head[8] == 0x41 && head[9] == 0x56 && head[10] == 0x49 && head[11] == 0x20)
        {
            CoreLog.Info("Detect: AVI magic matched");
            return FileFormat.Avi;
        }

        return FileFormat.Unknown;
    }

    /// <summary>
    /// 根据文件扩展名猜测文件格式。
    /// </summary>
    /// <param name="extension">扩展名，如 ".jpg"、".png"。大小写不敏感。</param>
    /// <returns>对应的 <see cref="FileFormat"/>，未识别时返回 Unknown。</returns>
    public static FileFormat DetectByExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return FileFormat.Unknown;

        return (extension[0] == '.' ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant()) switch
        {
            ".jpg" or ".jpeg" => FileFormat.Jpeg,
            ".png" => FileFormat.Png,
            ".gif" => FileFormat.Gif,
            ".bmp" => FileFormat.Bmp,
            ".webp" => FileFormat.WebP,
            ".ico" => FileFormat.Ico,
            ".tga" => FileFormat.Tga,
            ".hdr" => FileFormat.Hdr,
            ".exr" => FileFormat.Exr,
            ".svg" => FileFormat.Svg,
            ".wav" => FileFormat.Wav,
            ".flac" => FileFormat.Flac,
            ".mp3" => FileFormat.Mp3,
            ".ogg" => FileFormat.Ogg,
            ".mp4" => FileFormat.Mp4,
            ".mkv" => FileFormat.Mkv,
            ".webm" => FileFormat.WebM,
            ".wmv" => FileFormat.Wmv,
            ".mov" => FileFormat.Mov,
            ".avi" => FileFormat.Avi,
            ".flv" => FileFormat.Flv,
            ".pdf" => FileFormat.Pdf,
            ".docx" => FileFormat.Docx,
            ".xlsx" => FileFormat.Xlsx,
            ".pptx" => FileFormat.Pptx,
            ".epub" => FileFormat.Epub,
            ".mobi" => FileFormat.Mobi,
            ".azw3" => FileFormat.Azw3,
            ".txt" or ".log" => FileFormat.Text,
            ".html" or ".htm" => FileFormat.Html,
            ".md" or ".markdown" => FileFormat.Markdown,
            ".exe" or ".dll" or ".ocx" or ".sys" or ".scr" => FileFormat.Pe,
            ".elf" => FileFormat.Elf,
            ".zip" => FileFormat.Zip,
            ".7z" => FileFormat.SevenZip,
            ".rar" => FileFormat.Rar,
            ".tar" => FileFormat.Tar,
            ".gz" or ".tgz" => FileFormat.Gz,
            ".bz2" => FileFormat.Bz2,
            ".xz" => FileFormat.Xz,
            ".zst" => FileFormat.Zstd,
            ".iso" => FileFormat.Iso,
            ".sqlite" or ".sqlite3" or ".db" => FileFormat.Sqlite,
            ".dbf" => FileFormat.Dbf,
            ".stl" => FileFormat.Stl,
            ".dxf" => FileFormat.Dxf,
            ".step" or ".stp" => FileFormat.Step,
            ".fbx" => FileFormat.Fbx,
            ".ttf" => FileFormat.Ttf,
            ".otf" => FileFormat.Otf,
            ".woff" => FileFormat.Woff,
            ".woff2" => FileFormat.Woff2,
            ".torrent" => FileFormat.Torrent,
            ".dcm" or ".dicom" => FileFormat.Dicom,
            ".cer" or ".der" => FileFormat.Cer,
            ".pfx" or ".p12" => FileFormat.Pfx,
            ".lnk" => FileFormat.Lnk,
            ".vhd" => FileFormat.Vhd,
            ".vhdx" => FileFormat.Vhdx,
            ".vmdk" => FileFormat.Vmdk,
            ".icl" => FileFormat.Icl,
            ".srt" or ".sub" or ".ass" or ".ssa" or ".vtt" => FileFormat.Subtitle,
            ".odt" => FileFormat.Odt,
            ".ods" => FileFormat.Ods,
            ".odp" => FileFormat.Odp,
            ".rtf" => FileFormat.Rtf,
            ".djvu" or ".djv" => FileFormat.DjVu,
            ".xps" => FileFormat.Xps,
            ".fits" => FileFormat.Fits,
            ".parquet" => FileFormat.Parquet,
            _ => FileFormat.Unknown,
        };
    }

    /// <summary>
    /// ZIP 子类型检测：在 PK\x03\x04 头部中扫描已知文件名以区分 DOCX/XLSX/PPTX/EPUB/ODT/ODS/ODP 等。
    /// 支持 Store 和 Deflate 压缩条目的内容读取以进行精确子类型判定。
    /// </summary>
    private static FileFormat DetectZipSubtype(byte[] head, int length)
    {
        const int LocalHeaderSize = 30;
        int pos = 0;

        while (pos + LocalHeaderSize + 2 <= length)
        {
            // Validate local file header signature
            if (head[pos] != 0x50 || head[pos + 1] != 0x4B ||
                head[pos + 2] != 0x03 || head[pos + 3] != 0x04)
                break;

            int compressionMethod = head[pos + 8] | (head[pos + 9] << 8); // 0=Store, 8=Deflate
            int nameLen = head[pos + 26] | (head[pos + 27] << 8);
            int extraLen = head[pos + 28] | (head[pos + 29] << 8);
            int fileNameStart = pos + LocalHeaderSize;
            int dataStart = fileNameStart + nameLen + extraLen;
            int totalEntry = LocalHeaderSize + nameLen + extraLen;

            if (fileNameStart + nameLen <= length)
            {
                string fileName = Encoding.UTF8.GetString(head, fileNameStart, nameLen);

                if (fileName.Equals("mimetype", StringComparison.OrdinalIgnoreCase))
                {
                    // mimetype is almost always Store (uncompressed)
                    if (compressionMethod == 0 && dataStart + 30 <= length)
                    {
                        string mime = Encoding.ASCII.GetString(head, dataStart, 30);
                        if (mime.StartsWith("application/epub+zip", StringComparison.Ordinal))
                            return FileFormat.Epub;
                    }
                }
                else if (fileName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                {
                    // Read content (Store or Deflate) to distinguish DOCX vs XLSX vs PPTX
                    string? content = null;
                    if (compressionMethod == 0 && dataStart < length)
                    {
                        int remaining = length - dataStart;
                        content = Encoding.UTF8.GetString(head, dataStart, Math.Min(remaining, 800));
                    }
                    else if (compressionMethod == 8)
                    {
                        int compressedSize = BitConverter.ToInt32(head, pos + 18);
                        if (dataStart + compressedSize <= length && compressedSize > 0)
                        {
                            content = DecompressDeflateBlock(head, dataStart, compressedSize);
                        }
                    }

                    if (content != null)
                    {
                        if (content.Contains("application/vnd.openxmlformats-officedocument.wordprocessingml", StringComparison.OrdinalIgnoreCase))
                            return FileFormat.Docx;
                        if (content.Contains("application/vnd.openxmlformats-officedocument.spreadsheetml", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
                            return FileFormat.Xlsx;
                        if (content.Contains("application/vnd.openxmlformats-officedocument.presentationml", StringComparison.OrdinalIgnoreCase))
                            return FileFormat.Pptx;
                    }
                    return FileFormat.OfficeOpenXml; // generic OOXML fallback
                }
                else if (fileName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                {
                    // OpenDocument format — check manifest.xml for ODT/ODS/ODP
                    if (fileName.Equals("META-INF/manifest.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        string? content = null;
                        if (compressionMethod == 0 && dataStart < length)
                        {
                            int remaining = length - dataStart;
                            content = Encoding.UTF8.GetString(head, dataStart, Math.Min(remaining, 800));
                        }
                        else if (compressionMethod == 8)
                        {
                            int compressedSize = BitConverter.ToInt32(head, pos + 18);
                            if (dataStart + compressedSize <= length && compressedSize > 0)
                            {
                                content = DecompressDeflateBlock(head, dataStart, compressedSize);
                            }
                        }

                        if (content != null)
                        {
                            if (content.Contains("application/vnd.oasis.opendocument.text", StringComparison.Ordinal))
                                return FileFormat.Odt;
                            if (content.Contains("application/vnd.oasis.opendocument.spreadsheet", StringComparison.Ordinal))
                                return FileFormat.Ods;
                            if (content.Contains("application/vnd.oasis.opendocument.presentation", StringComparison.Ordinal))
                                return FileFormat.Odp;
                        }
                    }
                    // If we see any META-INF/ entry, it's an OpenDocument format
                    return FileFormat.Odt;
                }
            }

            pos += totalEntry;
            if (totalEntry == 0) break;
        }

        return FileFormat.Zip; // regular ZIP
    }

    /// <summary>
    /// 尝试对原始 Deflate 数据块进行解压缩（无 zlib 头部）。
    /// </summary>
    private static string? DecompressDeflateBlock(byte[] data, int offset, int compressedSize)
    {
        try
        {
            using var ms = new MemoryStream(data, offset, compressedSize);
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate);
            return reader.ReadToEnd();
        }
        catch
        {
            return null; // Decompression failed
        }
    }

    /// <summary>
    /// 验证 MZ 头部后是否有有效的 PE 签名。
    /// </summary>
    private static bool IsPe(byte[] head, int length)
    {
        // Need at least 0x40 bytes to read e_lfanew at offset 0x3C
        if (length < 0x40)
            return false;

        // Read PE signature offset from IMAGE_DOS_HEADER.e_lfanew (offset 0x3C)
        int peOffset = head[0x3C] | (head[0x3D] << 8);
        if (peOffset < 0 || peOffset + 4 > length)
            return false;

        // Check "PE\0\0" signature
        return head[peOffset] == 'P' && head[peOffset + 1] == 'E' &&
               head[peOffset + 2] == 0 && head[peOffset + 3] == 0;
    }
}
