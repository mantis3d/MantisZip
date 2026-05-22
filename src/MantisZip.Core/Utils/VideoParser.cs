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

            // ── MP4 / M4V / MOV ──
            // ftyp box: 4 bytes box size, 4 bytes "ftyp", 4 bytes major brand
            if (header[4] == (byte)'f' && header[5] == (byte)'t' &&
                header[6] == (byte)'y' && header[7] == (byte)'p')
            {
                string brand = Encoding.ASCII.GetString(header, 8, 4);
                return ParseMp4(fs, r, brand);
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
        catch { return null; }
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
                                r.ReadBytes(8); // reserved + layer
                                r.ReadBytes(4); // volume + reserved
                                r.ReadBytes(4 * 9); // matrix
                                width = (int)ReadBE32(r) >> 16;
                                height = (int)ReadBE32(r) >> 16;
                                fs.Seek(trakEnd, SeekOrigin.Begin);
                                break;
                            }
                            else if (trType == "mdia")
                            {
                                long mdiaEnd = fs.Position + trSize - 8;
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
                                        if (handler == "vide" && codec == null)
                                            codec = "video";
                                        else if (handler == "soun" && codec == null)
                                            codec = "audio";
                                        fs.Seek(Math.Min(fs.Position + mdSize - 16, mdiaEnd), SeekOrigin.Begin);
                                    }
                                    else if (mdType == "minf")
                                    {
                                        fs.Seek(Math.Min(fs.Position + mdSize - 8, mdiaEnd), SeekOrigin.Begin);
                                    }
                                    else
                                    {
                                        fs.Seek(Math.Min(fs.Position + mdSize - 12, mdiaEnd), SeekOrigin.Begin);
                                    }
                                }
                                fs.Seek(mdiaEnd, SeekOrigin.Begin);
                                continue;
                            }

                            fs.Seek(Math.Min(fs.Position + trSize - 12, trakEnd), SeekOrigin.Begin);
                        }
                        fs.Seek(trakEnd, SeekOrigin.Begin);
                        continue;
                    }

                    fs.Seek(Math.Min(fs.Position + subSize - 12, moovEnd), SeekOrigin.Begin);
                }
                fs.Seek(moovEnd, SeekOrigin.Begin);
                continue;
            }

            fs.Seek(Math.Min(fs.Position + boxSize - 12, fs.Length), SeekOrigin.Begin);
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
                case 0x1549A966: // Segment > Info
                    while (fs.Position < dataEnd - 4)
                    {
                        if (!TryReadEbmlId(r, out uint subId)) break;
                        if (!TryReadEbmlSize(r, out ulong subSize)) break;

                        if (subId == 0x4489) // Segment > Info > Duration
                        {
                            durationSec = ReadEbmlFloat(r);
                        }

                        fs.Seek(Math.Min(fs.Position + (long)subSize, dataEnd), SeekOrigin.Begin);
                    }
                    break;

                case 0x1654AE6B: // Segment > Tracks
                    while (fs.Position < dataEnd - 4)
                    {
                        if (!TryReadEbmlId(r, out uint trId)) break;
                        if (!TryReadEbmlSize(r, out ulong trSize)) break;
                        long trEnd = fs.Position + (long)trSize;

                        if (trId == 0xAE) // TrackEntry
                        {
                            while (fs.Position < trEnd - 4)
                            {
                                if (!TryReadEbmlId(r, out uint teId)) break;
                                if (!TryReadEbmlSize(r, out ulong teSize)) break;
                                long teEnd = fs.Position + (long)teSize;

                                if (teId == 0x86) // Track > CodecID
                                {
                                    codec = Encoding.ASCII.GetString(r.ReadBytes((int)teSize));
                                }
                                else if (teId == 0xE0) // Track > Video
                                {
                                    while (fs.Position < teEnd - 4)
                                    {
                                        if (!TryReadEbmlId(r, out uint vId)) break;
                                        if (!TryReadEbmlSize(r, out ulong vSize)) break;
                                        if (vId == 0xB0) width = (int)ReadEbmlUInt(r, vSize);
                                        else if (vId == 0xBA) height = (int)ReadEbmlUInt(r, vSize);
                                        else fs.Seek(Math.Min(fs.Position + (long)vSize, teEnd), SeekOrigin.Begin);
                                    }
                                }

                                fs.Seek(teEnd, SeekOrigin.Begin);
                            }
                        }

                        fs.Seek(trEnd, SeekOrigin.Begin);
                    }
                    break;
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
        double? fps = null;
        string? codec = null;

        // Skip RIFF header (already read)
        // Start scanning LIST chunks
        fs.Seek(12, SeekOrigin.Begin);
        while (fs.Position < fs.Length - 8)
        {
            uint chunkId = r.ReadUInt32(); // LE
            // FourCC
            byte[] idBytes = BitConverter.GetBytes(chunkId);
            string id = Encoding.ASCII.GetString(idBytes);
            uint chunkSize = r.ReadUInt32();

            if (chunkSize > fs.Length - fs.Position) break;
            long chunkEnd = fs.Position + chunkSize;

            if (id == "avih") // Main AVI header
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

    private static double ReadEbmlFloat(BinaryReader r)
    {
        byte[] b = r.ReadBytes(4);
        if (b.Length < 4) return 0;
        if (BitConverter.IsLittleEndian)
            Array.Reverse(b);
        return BitConverter.ToSingle(b, 0);
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
