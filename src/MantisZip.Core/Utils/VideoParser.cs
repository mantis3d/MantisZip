using System;
using System.IO;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// 视频文件元数据解析器（MP4 / MKV / AVI）。
/// 只读取文件头获取基本信息，不解码画面。
/// </summary>
public static class VideoParser
{
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var r = new BinaryReader(fs);

            // Read first 12 bytes to detect format
            if (fs.Length < 12) return null;
            byte[] header = r.ReadBytes(12);

            // ── FLV ──
            // Signature: 3 bytes "FLV"
            if (header[0] == (byte)'F' && header[1] == (byte)'L' && header[2] == (byte)'V')
                return ParseFlv(fs, r);

            // ── MP4 / M4V / MOV ──
            // ftyp box: 4 bytes box size, 4 bytes "ftyp", 4 bytes major brand
            if (header[4] == (byte)'f' && header[5] == (byte)'t' &&
                header[6] == (byte)'y' && header[7] == (byte)'p')
            {
                string brand = Encoding.ASCII.GetString(header, 8, 4);
                return ParseMp4(fs, r, brand);
            }

            // MOV fallback: some MOV files omit ftyp and start with moov directly
            uint firstBoxSize = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
            if (firstBoxSize >= 8 && firstBoxSize <= fs.Length &&
                header[4] == (byte)'m' && header[5] == (byte)'o' &&
                header[6] == (byte)'o' && header[7] == (byte)'v')
            {
                return ParseMp4(fs, r, "qt  ");
            }

            // ── MKV / WebM ──
            // EBML header starts with 0x1A45DFA3
            if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
                return ParseMkv(fs, r);

            // ── AVI ──
            // RIFF header: 4 bytes "RIFF", 4 bytes size, 4 bytes "AVI "
            if (header[0] == (byte)'R' && header[1] == (byte)'I' &&
                header[2] == (byte)'F' && header[3] == (byte)'F' &&
                header[8] == (byte)'A' && header[9] == (byte)'V' &&
                header[10] == (byte)'I' && header[11] == (byte)' ')
                return ParseAvi(fs, r);

            return null;
        }
        catch (Exception ex) { CoreLog.Info($"VideoParser.Parse failed: {ex.Message}"); return null; }
    }

    // ═══════════════════════════════════
    //  MP4 / M4V / MOV
    // ═══════════════════════════════════

    private static FileFormatInfo? ParseMp4(FileStream fs, BinaryReader r, string brand)
    {
        string formatName = brand switch
        {
            "isom" or "mp42" or "mp41" => "MP4 视频",
            "M4V " or "M4VH" => "M4V 视频",
            "qt  " => "MOV 视频",
            _ => $"MP4 ({brand})",
        };

        int? width = null, height = null;
        double? durationSec = null;
        int? bitrate = null;
        string? codec = null;

        fs.Seek(0, SeekOrigin.Begin);
        while (fs.Position < fs.Length - 8)
        {
            uint boxSize = ReadBE32(r);
            if (boxSize < 8) break;
            string boxType = Encoding.ASCII.GetString(r.ReadBytes(4));

            if (boxType == "moov")
            {
                long moovEnd = fs.Position + boxSize - 8;
                while (fs.Position < moovEnd - 8)
                {
                    uint subSize = ReadBE32(r);
                    if (subSize < 8) break;
                    string subType = Encoding.ASCII.GetString(r.ReadBytes(4));

                    if (subType == "mvhd")
                    {
                        long mvhdEnd = fs.Position + subSize - 8;
                        r.ReadByte(); // version
                        r.ReadBytes(3); // flags
                        uint creationTime = ReadBE32(r);
                        uint modificationTime = ReadBE32(r);
                        uint timescale = ReadBE32(r);
                        uint duration = ReadBE32(r);
                        if (timescale > 0)
                            durationSec = (double)duration / timescale;
                        r.ReadBytes(4); // rate
                        r.ReadBytes(1); // volume
                        fs.Seek(mvhdEnd, SeekOrigin.Begin);
                        continue;
                    }

                    if (subType == "trak")
                    {
                        long trakEnd = fs.Position + subSize - 8;
                        long saved = fs.Position;
                        while (fs.Position < trakEnd - 8)
                        {
                            uint trSize = ReadBE32(r);
                            if (trSize < 8) break;
                            string trType = Encoding.ASCII.GetString(r.ReadBytes(4));

                            if (trType == "tkhd")
                            {
                                int ver = r.ReadByte(); // version
                                r.ReadBytes(3); // flags
                                r.ReadBytes(ver == 1 ? 8 : 4); // creation time
                                r.ReadBytes(ver == 1 ? 8 : 4); // modification time
                                r.ReadBytes(4); // track id
                                r.ReadBytes(4); // reserved
                                r.ReadBytes(ver == 1 ? 8 : 4); // duration
                                r.ReadBytes(12); // reserved[2] + layer + alternate_group
                                r.ReadBytes(4); // volume + reserved
                                r.ReadBytes(4 * 9); // matrix
                                int w = (int)ReadBE32(r) >> 16;
                                int h = (int)ReadBE32(r) >> 16;
                                // Only set from video track (audio track tkhd has 0x0)
                                if (w > 0 && h > 0) { width = w; height = h; }
                                // Position is now at next sub-box after tkhd.
                                // Continue to find mdia/hdlr for codec detection.
                                continue;
                            }
                            else if (trType == "mdia")
                            {
                                long mdiaEnd = fs.Position + trSize - 8;
                                bool isVideoTrack = false;
                                while (fs.Position < mdiaEnd - 8)
                                {
                                    uint mdSize = ReadBE32(r);
                                    if (mdSize < 8) break;
                                    string mdType = Encoding.ASCII.GetString(r.ReadBytes(4));

                                    if (mdType == "hdlr")
                                    {
                                        r.ReadByte(); // version
                                        r.ReadBytes(3); // flags
                                        r.ReadBytes(4); // pre-defined
                                        string handler = Encoding.ASCII.GetString(r.ReadBytes(4));
                                        isVideoTrack = handler == "vide";
                                        fs.Seek(Math.Min(fs.Position + mdSize - 20, mdiaEnd), SeekOrigin.Begin);
                                    }
                                    else if (mdType == "minf" && isVideoTrack && codec == null)
                                    {
                                        long minfEnd = fs.Position + mdSize - 8;
                                        while (fs.Position < minfEnd - 8)
                                        {
                                            uint stSize = ReadBE32(r);
                                            if (stSize < 8) break;
                                            string stType = Encoding.ASCII.GetString(r.ReadBytes(4));
                                            if (stType == "stbl")
                                            {
                                                long stblEnd = fs.Position + stSize - 8;
                                                while (fs.Position < stblEnd - 8)
                                                {
                                                    uint sdSize = ReadBE32(r);
                                                    if (sdSize < 8) break;
                                                    string sdType = Encoding.ASCII.GetString(r.ReadBytes(4));
                                                    if (sdType == "stsd" && codec == null)
                                                    {
                                                        r.ReadBytes(4); // version(1) + flags(3)
                                                        uint entryCount = ReadBE32(r);
                                                        if (entryCount > 0)
                                                        {
                                                            r.ReadBytes(4); // entry_size (4B)
                                                            string fourCC = Encoding.ASCII.GetString(r.ReadBytes(4));
                                                            codec = fourCC switch
                                                            {
                                                                "avc1" => "H.264",
                                                                "hvc1" or "hev1" => "H.265",
                                                                "mp4v" => "MPEG-4",
                                                                "av01" => "AV1",
                                                                "vp09" => "VP9",
                                                                _ => fourCC.TrimEnd(' ').ToUpperInvariant(),
                                                            };
                                                        }
                                                    }
                                                    fs.Seek(Math.Min(fs.Position + sdSize - 8, stblEnd), SeekOrigin.Begin);
                                                }
                                            }
                                            fs.Seek(Math.Min(fs.Position + stSize - 8, minfEnd), SeekOrigin.Begin);
                                        }
                                    }
                                    else
                                    {
                                        fs.Seek(Math.Min(fs.Position + mdSize - 8, mdiaEnd), SeekOrigin.Begin);
                                    }
                                }
                                fs.Seek(mdiaEnd, SeekOrigin.Begin);
                                continue;
                            }

                                fs.Seek(Math.Min(fs.Position + trSize - 8, trakEnd), SeekOrigin.Begin);
                        }
                        fs.Seek(trakEnd, SeekOrigin.Begin);
                        continue;
                    }

                    fs.Seek(Math.Min(fs.Position + subSize - 8, moovEnd), SeekOrigin.Begin);
                }
                fs.Seek(moovEnd, SeekOrigin.Begin);
                continue;
            }

            fs.Seek(Math.Min(fs.Position + boxSize - 8, fs.Length), SeekOrigin.Begin);
        }

        return new FileFormatInfo
        {
            Format = FileFormat.Mp4,
            DisplayName = formatName,
            Extension = ".mp4",
            FileSize = fs.Length,
            VideoWidth = width,
            VideoHeight = height,
            Duration = durationSec.HasValue ? TimeSpan.FromSeconds(durationSec.Value) : null,
            Codec = codec,
            Bitrate = bitrate,
        };
    }

    // ═══════════════════════════════════
    //  FLV
    // ═══════════════════════════════════

    private static FileFormatInfo? ParseFlv(FileStream fs, BinaryReader r)
    {
        int? width = null, height = null;
        double? durationSec = null;
        string? codec = null;
        int? bitrate = null;

        fs.Seek(13, SeekOrigin.Begin); // skip header (9) + PreviousTagSize0 (4)

        while (fs.Position < fs.Length - 15)
        {
            long tagStart = fs.Position;

            // Tag header: 11 bytes
            byte[] tagHdr = r.ReadBytes(11);
            if (tagHdr.Length < 11) break;

            byte tagType = (byte)(tagHdr[0] & 0x1F);
            uint dataSize = (uint)((tagHdr[1] << 16) | (tagHdr[2] << 8) | tagHdr[3]);
            if (dataSize == 0 || dataSize > fs.Length - fs.Position) break;

            if (tagType == 18) // Script data → onMetaData
            {
                long dataEnd = fs.Position + dataSize;
                byte amfType = r.ReadByte();
                if (amfType == 0x02) // AMF0 String
                {
                    ushort strLen = (ushort)((r.ReadByte() << 8) | r.ReadByte());
                    if (strLen <= dataSize - 3 && fs.Position + strLen <= dataEnd)
                    {
                        string strVal = Encoding.ASCII.GetString(r.ReadBytes(strLen));
                        if (strVal == "onMetaData")
                        {
                            ReadAmf0Metadata(r, dataEnd,
                                out width, out height, out durationSec, out codec, out bitrate);
                        }
                    }
                }
                fs.Seek(dataEnd, SeekOrigin.Begin);
            }
            else if (tagType == 9 && codec == null) // Video tag → codec ID fallback
            {
                byte frameInfo = r.ReadByte();
                codec = (frameInfo & 0x0F) switch
                {
                    2 => "H.263",
                    3 => "Screen Video",
                    4 => "VP6",
                    5 => "VP6 (Alpha)",
                    7 => "H.264 / AVC",
                    12 => "H.265 / HEVC",
                    var id => $"CodecID={id}",
                };
            }

            // Advance past data + PreviousTagSize
            fs.Seek(tagStart + 15 + dataSize, SeekOrigin.Begin); // 11 header + data + 4 prevSize
        }

        return new FileFormatInfo
        {
            Format = FileFormat.Flv,
            DisplayName = "FLV 视频",
            Extension = ".flv",
            FileSize = fs.Length,
            VideoWidth = width,
            VideoHeight = height,
            Duration = durationSec.HasValue ? TimeSpan.FromSeconds(durationSec.Value) : null,
            Codec = codec,
            Bitrate = bitrate,
        };
    }

    /// <summary>解析 AMF0 ECMA Array / Object 中的元数据键值对。</summary>
    private static void ReadAmf0Metadata(BinaryReader r, long dataEnd,
        out int? width, out int? height, out double? durationSec,
        out string? codec, out int? bitrate)
    {
        width = height = null;
        durationSec = null;
        codec = null;
        bitrate = null;

        byte containerType = r.ReadByte();
        if (containerType == 0x08) // ECMA Array (has 4-byte count prefix)
            r.ReadBytes(4);
        else if (containerType != 0x03) // Not an Object either
            return;

        while (r.BaseStream.Position < dataEnd - 2)
        {
            // Key: 2-byte length + UTF-8 string
            ushort keyLen = (ushort)((r.ReadByte() << 8) | r.ReadByte());
            if (keyLen == 0) { r.ReadByte(); break; } // ObjectEnd marker

            if (r.BaseStream.Position + keyLen > dataEnd) break;
            string key = Encoding.UTF8.GetString(r.ReadBytes(keyLen));

            if (r.BaseStream.Position >= dataEnd) break;

            byte valType = r.ReadByte();
            switch (valType)
            {
                case 0x00: // Number (8-byte IEEE 754 big-endian)
                    if (r.BaseStream.Position + 8 > dataEnd) break;
                    byte[] numB = r.ReadBytes(8);
                    if (BitConverter.IsLittleEndian) Array.Reverse(numB);
                    double numVal = BitConverter.ToDouble(numB, 0);
                    switch (key)
                    {
                        case "duration":        durationSec = numVal; break;
                        case "width":            width = (int)numVal; break;
                        case "height":           height = (int)numVal; break;
                        case "videodatarate":    if (bitrate == null) bitrate = (int)(numVal * 1000); break;
                        case "audiodatarate":    if (bitrate == null) bitrate = (int)(numVal * 1000); break;
                        case "videocodecid" when codec == null:
                            codec = numVal switch
                            {
                                2 => "H.263",
                                3 => "Screen Video",
                                4 => "VP6",
                                7 => "H.264 / AVC",
                                12 => "H.265 / HEVC",
                                _ => $"CodecID={numVal}",
                            };
                            break;
                    }
                    break;

                case 0x02: // String (2-byte length + UTF-8)
                    if (r.BaseStream.Position + 2 > dataEnd) break;
                    ushort strLen = (ushort)((r.ReadByte() << 8) | r.ReadByte());
                    if (r.BaseStream.Position + strLen > dataEnd) break;
                    string strVal = Encoding.UTF8.GetString(r.ReadBytes(strLen));
                    if (key == "videocodecid" && codec == null)
                        codec = strVal;
                    break;

                case 0x01: // Boolean (1 byte)
                    r.ReadByte(); // skip
                    break;

                default: // Can't determine size → stop parsing
                    return;
            }
        }
    }

    // ═══════════════════════════════════
    //  MKV / WebM
    // ═══════════════════════════════════

    private static FileFormatInfo? ParseMkv(FileStream fs, BinaryReader r)
    {
        int? width = null, height = null;
        double? durationSec = null;
        string? codec = null;

        fs.Seek(0, SeekOrigin.Begin);
        while (fs.Position < fs.Length - 4)
        {
            if (!TryReadEbmlId(r, out uint id)) break;
            if (!TryReadEbmlSize(r, out ulong size)) break;
            if (size > (ulong)(fs.Length - fs.Position)) break;

            long dataEnd = fs.Position + (long)size;

            switch (id)
            {
                case 0x08538067: // Segment — contains Info and Tracks (VINT: 0x18538067)
                case 0x0549A966: // Segment > Info (VINT: 0x1549A966)
                case 0x0654AE6B: // Segment > Tracks (VINT: 0x1654AE6B)
                {
                    long segEnd = dataEnd;
                    while (fs.Position < segEnd - 4)
                    {
                        if (!TryReadEbmlId(r, out uint subId)) break;
                        if (!TryReadEbmlSize(r, out ulong subSize)) break;
                        long subDataEnd = fs.Position + (long)subSize;
                        if (subSize > (ulong)(segEnd - fs.Position)) break;

                        if (subId == 0x0549A966) // Info (VINT: 0x1549A966)
                        {
                            long infoEnd = subDataEnd;
                            while (fs.Position < infoEnd - 4)
                            {
                                if (!TryReadEbmlId(r, out uint infoId)) break;
                                if (!TryReadEbmlSize(r, out ulong infoSize)) break;

                                if (infoId == 0x0489) // Duration (VINT: 0x4489)
                                {
                                    durationSec = ReadEbmlFloat(r, infoSize);
                                }

                                fs.Seek(Math.Min(fs.Position + (long)infoSize, infoEnd), SeekOrigin.Begin);
                            }
                        }
                        else                         if (subId == 0x0654AE6B) // Tracks (VINT: 0x1654AE6B)
                        {
                            long tracksEnd = subDataEnd;
                            while (fs.Position < tracksEnd - 4)
                            {
                                if (!TryReadEbmlId(r, out uint trId)) break;
                                if (!TryReadEbmlSize(r, out ulong trSize)) break;
                                long trEnd = fs.Position + (long)trSize;

                                if (trId == 0x2E) // TrackEntry (VINT: 0xAE)
                                {
                                    while (fs.Position < trEnd - 4)
                                    {
                                        if (!TryReadEbmlId(r, out uint teId)) break;
                                        if (!TryReadEbmlSize(r, out ulong teSize)) break;
                                        long teEnd = fs.Position + (long)teSize;

                                        if (teId == 0x06) // CodecID (VINT: 0x86)
                                        {
                                            codec = Encoding.ASCII.GetString(r.ReadBytes((int)teSize));
                                        }
                                        else if (teId == 0x60) // Video (VINT: 0xE0)
                                        {
                                            while (fs.Position < teEnd - 4)
                                            {
                                                if (!TryReadEbmlId(r, out uint vId)) break;
                                                if (!TryReadEbmlSize(r, out ulong vSize)) break;
                                        if (vId == 0x30) width = (int)ReadEbmlUInt(r, vSize); // PixelWidth (VINT: 0xB0)
                                        else if (vId == 0x3A) height = (int)ReadEbmlUInt(r, vSize); // PixelHeight (VINT: 0xBA)
                                                else fs.Seek(Math.Min(fs.Position + (long)vSize, teEnd), SeekOrigin.Begin);
                                            }
                                        }

                                        fs.Seek(teEnd, SeekOrigin.Begin);
                                    }
                                }

                                fs.Seek(trEnd, SeekOrigin.Begin);
                            }
                        }

                        fs.Seek(subDataEnd, SeekOrigin.Begin);
                    }
                    break;
                }
            }

            fs.Seek(dataEnd, SeekOrigin.Begin);
        }

        return new FileFormatInfo
        {
            Format = FileFormat.Mkv,
            DisplayName = "MKV 视频",
            Extension = ".mkv",
            FileSize = fs.Length,
            VideoWidth = width,
            VideoHeight = height,
            Duration = durationSec.HasValue ? TimeSpan.FromSeconds(durationSec.Value) : null,
            Codec = codec,
        };
    }

    // ═══════════════════════════════════
    //  AVI
    // ═══════════════════════════════════

    private static FileFormatInfo? ParseAvi(FileStream fs, BinaryReader r)
    {
        int? width = null, height = null;
        string? codec = null;

        // Skip RIFF header (already read)
        fs.Seek(12, SeekOrigin.Begin);
        ParseAviChunks(r, fs, fs.Length, ref width, ref height, ref codec);

        return new FileFormatInfo
        {
            Format = FileFormat.Avi,
            DisplayName = "AVI 视频",
            Extension = ".avi",
            FileSize = fs.Length,
            VideoWidth = width,
            VideoHeight = height,
            Codec = codec,
        };
    }

    /// <summary>
    /// Recursive AVI chunk parser. Handles LIST nesting so that
    /// avih (inside LIST hdrl) and strh (inside LIST strl) are found.
    /// </summary>
    private static void ParseAviChunks(BinaryReader r, FileStream fs, long end,
        ref int? width, ref int? height, ref string? codec)
    {
        while (fs.Position <= end - 8)
        {
            uint chunkId = r.ReadUInt32(); // LE
            byte[] idBytes = BitConverter.GetBytes(chunkId);
            string id = Encoding.ASCII.GetString(idBytes);
            uint chunkSize = r.ReadUInt32();

            if (chunkSize > end - fs.Position) break;
            long chunkEnd = fs.Position + chunkSize;

            if (id == "LIST")
            {
                if (chunkSize < 4) break;
                string listType = Encoding.ASCII.GetString(r.ReadBytes(4));
                // Recurse into LIST children (they are at the same logical level)
                ParseAviChunks(r, fs, chunkEnd, ref width, ref height, ref codec);
            }
            else if (id == "avih") // Main AVI header
            {
                r.ReadBytes(8); // microSecPerFrame + maxBytesPerSec
                r.ReadBytes(4); // paddingGranularity
                r.ReadBytes(4); // flags
                r.ReadBytes(4); // totalFrames
                r.ReadBytes(4); // initialFrames
                r.ReadBytes(4); // streams
                r.ReadBytes(4); // suggestedBufferSize
                width = (int)r.ReadUInt32();
                height = (int)r.ReadUInt32();
            }
            else if (id == "strh") // Stream header
            {
                string streamType = Encoding.ASCII.GetString(r.ReadBytes(4));
                string handler = Encoding.ASCII.GetString(r.ReadBytes(4));
                if (streamType == "vids" && codec == null)
                    codec = handler;
            }

            fs.Seek(chunkEnd, SeekOrigin.Begin);
        }
    }

    // ═══════════════════════════════════
    //  EBML helpers (MKV)
    // ═══════════════════════════════════

    private static bool TryReadEbmlId(BinaryReader r, out uint id)
    {
        id = 0;
        byte first = r.ReadByte();
        int len = 1;
        if (first == 0) return false;
        // Determine length from leading zero bits
        uint mask = 0x80;
        while ((first & mask) == 0 && len <= 4) { mask >>= 1; len++; }
        id = (uint)(first & (mask - 1));
        for (int i = 1; i < len; i++)
            id = (id << 8) | r.ReadByte();
        return true;
    }

    private static bool TryReadEbmlSize(BinaryReader r, out ulong size)
    {
        size = 0;
        byte first = r.ReadByte();
        int len = 1;
        uint mask = 0x80;
        while ((first & mask) == 0 && len <= 8) { mask >>= 1; len++; }
        size = (ulong)(first & (mask - 1));
        for (int i = 1; i < len; i++)
            size = (size << 8) | r.ReadByte();
        return true;
    }

    private static double ReadEbmlFloat(BinaryReader r, ulong size = 4)
    {
        int byteSize = (int)Math.Min(size, 8UL);
        byte[] b = r.ReadBytes(byteSize);
        if (b.Length < byteSize) return 0;
        if (BitConverter.IsLittleEndian)
            Array.Reverse(b);
        if (byteSize == 4)
            return BitConverter.ToSingle(b, 0);
        return BitConverter.ToDouble(b, 0);
    }

    private static ulong ReadEbmlUInt(BinaryReader r, ulong size)
    {
        ulong val = 0;
        for (ulong i = 0; i < size; i++)
            val = (val << 8) | r.ReadByte();
        return val;
    }

    // ═══════════════════════════════════
    //  Big-endian helpers
    // ═══════════════════════════════════

    private static uint ReadBE32(BinaryReader r)
    {
        var b = r.ReadBytes(4);
        if (b.Length < 4) return 0;
        return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
    }
}
