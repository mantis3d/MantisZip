using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// TTF/OTF/WOFF 字体文件解析器。
/// TTF/OTF 规范为 big-endian。WOFF 解压后重建 TTF 再解析，供 FontFamily 渲染使用。
/// </summary>
public static class FontParser
{
    /// <summary>
    /// 解析字体文件，返回格式信息；解析失败返回 null。
    /// </summary>
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            uint sfVersion = ReadBEU32(reader);

            // WOFF signature = "wOFF"
            if (sfVersion == 0x774F4646)
            {
                fs.Close();
                return ParseWoff(filePath);
            }

            return ParseSfnt(filePath, fs, reader, sfVersion);
        }
        catch (Exception ex)
        {
            CoreLog.Info($"FontParser.Parse outer failed: {ex.Message}");
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  TTF / OTF (sfnt) 解析
    // ═══════════════════════════════════════════════════════

    private static FileFormatInfo? ParseSfnt(
        string filePath, FileStream fs, BinaryReader reader, uint sfVersion)
    {
        ushort numTables = ReadBEU16(reader);
        reader.ReadBytes(6); // searchRange + entrySelector + rangeShift

        uint nameOff = 0, nameLen = 0, maxpOff = 0, maxpLen = 0;
        bool foundName = false, foundMaxp = false;

        for (int i = 0; i < numTables; i++)
        {
            long ep = fs.Position;
            string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            reader.ReadBytes(4);
            uint off = ReadBEU32(reader);
            uint len = ReadBEU32(reader);
            fs.Seek(ep + 16, SeekOrigin.Begin);

            if (tag == "name") { nameOff = off; nameLen = len; foundName = true; }
            else if (tag == "maxp") { maxpOff = off; maxpLen = len; foundMaxp = true; }
        }

        string? family = null, subfamily = null, fullName = null;
        ushort familyPid = 0, subfamilyPid = 0, fullNamePid = 0;
        string? prefFamily = null; // name ID 16 (preferred family) fallback

        if (foundName && nameOff > 0 && nameLen >= 6
            && nameOff < fs.Length && nameOff + nameLen <= fs.Length)
        {
            fs.Seek(nameOff, SeekOrigin.Begin);
            ReadBEU16(reader); // format
            ushort nameCount = ReadBEU16(reader);
            ushort strOff = ReadBEU16(reader);

            for (int i = 0; i < nameCount; i++)
            {
                ushort pid = ReadBEU16(reader);
                ReadBEU16(reader); // eid
                ReadBEU16(reader); // lid
                ushort nid = ReadBEU16(reader);
                ushort len = ReadBEU16(reader);
                ushort off = ReadBEU16(reader);
                if (len == 0) continue;

                long saved = fs.Position;
                try
                {
                    long pos = nameOff + strOff + off;
                    if (pos < 0 || pos + len > fs.Length) continue;

                    fs.Seek(pos, SeekOrigin.Begin);
                    byte[] raw = reader.ReadBytes(len);

                    string val = pid == 3 ? Encoding.BigEndianUnicode.GetString(raw)
                        : pid == 1 ? Encoding.ASCII.GetString(raw)
                        : Encoding.UTF8.GetString(raw);

                    int nul = val.IndexOf('\0');
                    if (nul >= 0) val = val[..nul];

                    // Priority: pid=3 (Windows Unicode) > pid=1 (Mac) > other
                    if (nid == 1 && ShouldReplaceNameEntry(familyPid, pid))
                    { family = val; familyPid = pid; }
                    else if (nid == 2 && ShouldReplaceNameEntry(subfamilyPid, pid))
                    { subfamily = val; subfamilyPid = pid; }
                    else if (nid == 4 && ShouldReplaceNameEntry(fullNamePid, pid))
                    { fullName = val; fullNamePid = pid; }
                    // Collect Preferred Family (name ID 16) as fallback
                    else if (nid == 16 && ShouldReplaceNameEntry(familyPid, pid))
                    { prefFamily = val; }
                }
                finally { fs.Seek(saved, SeekOrigin.Begin); }
            }

            // If no pid=3 family name found, use preferred family (nid 16) as fallback
            if (family == null || (familyPid != 3 && prefFamily != null))
                family = prefFamily;
        }

        static bool ShouldReplaceNameEntry(ushort currentPid, ushort newPid)
        {
            return currentPid == 0 || (currentPid == 1 && newPid == 3);
        }

        int? glyphs = null;
        if (foundMaxp && maxpOff > 0 && maxpLen >= 6
            && maxpOff < fs.Length && maxpOff + maxpLen <= fs.Length)
        {
            fs.Seek(maxpOff + 4, SeekOrigin.Begin);
            glyphs = ReadBEU16(reader);
        }

        return new FileFormatInfo
        {
            Format = FileFormat.Ttf,
            DisplayName = sfVersion == 0x00010000 ? "TrueType 字体" : "OpenType 字体",
            Extension = Path.GetExtension(filePath),
            FileSize = new FileInfo(filePath).Length,
            FontName = family ?? fullName ?? Path.GetFileNameWithoutExtension(filePath),
            FontStyle = subfamily,
            GlyphCount = glyphs,
            AdditionalInfo = sfVersion switch
            {
                0x00010000 => "TrueType",
                0x4F54544F => "OpenType (CFF)",
                _ => $"Unknown ({sfVersion:X8})"
            },
        };
    }

    // ═══════════════════════════════════════════════════════
    //  WOFF 解析（解压 → 重建 TTF → 解析）
    // ═══════════════════════════════════════════════════════

    private static FileFormatInfo? ParseWoff(string filePath)
    {
        CoreLog.Info($"ParseWoff enter: {filePath}");
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);

        // WOFF 头 44 字节 (per W3C WOFF spec)
        reader.ReadUInt32(); // "wOFF"
        uint sfVersion = ReadBEU32(reader);
        ReadBEU32(reader);   // length (uint32 BE, unused)
        ushort numTables = ReadBEU16(reader);
        reader.ReadUInt16(); // reserved
        ReadBEU32(reader);   // totalSfntSize (uint32 BE, unused)
        reader.ReadUInt16(); // majorVersion
        reader.ReadUInt16(); // minorVersion
        reader.ReadBytes(20); // metaOffset + metaLength + metaOrigLength + privOffset + privLength

        // 表目录 20n 字节
        var entries = new (string tag, uint off, uint comp, uint orig)[numTables];
        for (int i = 0; i < numTables; i++)
        {
            string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint off = ReadBEU32(reader);
            uint comp = ReadBEU32(reader);
            uint orig = ReadBEU32(reader);
            ReadBEU32(reader); // checksum
            entries[i] = (tag, off, comp, orig);
        }

        // ── 重建 TTF ──
        string? outPath = null;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "MantisZip", "Fonts");
            Directory.CreateDirectory(dir);
            outPath = Path.Combine(dir, $"woff_{Path.GetFileNameWithoutExtension(filePath)}_{Guid.NewGuid():N}.ttf");

            using var ofs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
            using var w = new BinaryWriter(ofs);

            // Offset Table
            WriteBEU32(w, sfVersion);
            WriteBEU16(w, numTables);
            uint p2 = 1; while (p2 * 2 <= numTables) p2 *= 2;
            WriteBEU16(w, (ushort)(p2 * 16));
            WriteBEU16(w, (ushort)Math.Log(p2, 2));
            WriteBEU16(w, (ushort)(numTables * 16 - p2 * 16));

            // 预留 records
            long recStart = ofs.Position;
            w.Write(new byte[numTables * 16]);

            // 先解压所有表数据
            var decompressed = new byte[numTables][];
            for (int i = 0; i < numTables; i++)
            {
                var (_, woff, compLen, origLen) = entries[i];
                fs.Seek(woff, SeekOrigin.Begin);
                byte[] raw = reader.ReadBytes((int)compLen);

                decompressed[i] = (compLen == origLen || compLen == 0)
                    ? raw : DecompressDeflate(raw, (int)origLen);
            }

            // 写表数据
            long dataStart = ofs.Position;
            var offs = new uint[numTables];
            for (int i = 0; i < numTables; i++)
            {
                long pad = (4 - (ofs.Position % 4)) % 4;
                if (pad > 0) w.Write(new byte[pad]);
                offs[i] = (uint)(ofs.Position - dataStart);
                w.Write(decompressed[i]);
            }

            // 填写 records
            ofs.Seek(recStart, SeekOrigin.Begin);
            for (int i = 0; i < numTables; i++)
            {
                var (tag, _, _, origLen) = entries[i];
                uint cksum = CalcChecksum(decompressed[i]);

                w.Write(Encoding.ASCII.GetBytes(tag.Length == 4 ? tag : tag.PadRight(4)[..4]));
                WriteBEU32(w, cksum);
                WriteBEU32(w, (uint)(dataStart + offs[i]));
                WriteBEU32(w, origLen);
            }
        }
        catch (Exception ex)
        {
            CoreLog.Info($"ParseWoff inner failed: {ex.Message}");
            if (outPath != null) TryDeleteTempFile(outPath);
            return null;
        }

        // 解析重建的 TTF
        using var outFs = new FileStream(outPath!, FileMode.Open, FileAccess.Read);
        using var outReader = new BinaryReader(outFs);
        outReader.ReadBytes(4); // 跳过 sfVersion（ParseSfnt 假设 reader 已在 pos 4）
        var result = ParseSfnt(outPath!, outFs, outReader, sfVersion);
        if (result != null)
        {
            result.FontDecompressedPath = outPath;
            // 如果 name table 缺少 Family 名称（某些 WOFF 精简了名表），
            // ParseSfnt 会回退到临时文件名的 Path.GetFileNameWithoutExtension，
            // 这里改用原始 WOFF 文件名作为面板显示回退。
            if (result.FontName == Path.GetFileNameWithoutExtension(outPath))
                result.FontName = Path.GetFileNameWithoutExtension(filePath);
        }
        else
            TryDeleteTempFile(outPath);
        return result;
    }

    // ═══════════════════════════════════════════════════════
    //  Big-endian I/O
    // ═══════════════════════════════════════════════════════

    private static ushort ReadBEU16(BinaryReader r)
    {
        var b = r.ReadBytes(2);
        if (b.Length < 2) throw new EndOfStreamException();
        return (ushort)((b[0] << 8) | b[1]);
    }

    private static uint ReadBEU32(BinaryReader r)
    {
        var b = r.ReadBytes(4);
        if (b.Length < 4) throw new EndOfStreamException();
        return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
    }

    private static void WriteBEU16(BinaryWriter w, ushort v)
    {
        w.Write(new[] { (byte)(v >> 8), (byte)v });
    }

    private static void WriteBEU32(BinaryWriter w, uint v)
    {
        w.Write(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
    }

    // ═══════════════════════════════════════════════════════
    //  zlib deflate decompression (WOFF uses raw deflate
    //  wrapped in zlib: 2-byte header, optional tail)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 尝试删除临时文件，失败时记录日志但不抛出异常。
    /// </summary>
    private static void TryDeleteTempFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { CoreLog.Info($"FontParser temp file cleanup failed: {ex.Message}"); }
    }

    private static byte[] DecompressDeflate(byte[] compressed, int decompressedLength)
    {
        // Use ZLibStream which properly handles zlib framing (header + adler32 tail)
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(decompressedLength);
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>TrueType table checksum (sum of uint32s, pad with zeros)</summary>
    internal static uint CalcChecksum(byte[] data)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 3 < data.Length; i += 4)
            sum += (uint)((data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3]);
        if (i < data.Length)
        {
            uint last = 0;
            for (int j = i; j < data.Length; j++)
                last |= (uint)data[j] << (24 - (j - i) * 8);
            sum += last;
        }
        return sum;
    }
}
