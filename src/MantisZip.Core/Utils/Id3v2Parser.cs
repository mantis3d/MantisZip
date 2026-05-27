using System;
using System.IO;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// ID3v2/v1 tag parser for MP3 files.
/// Extracts: title (TIT2), artist (TPE1), album (TALB), duration (TLEN),
/// cover art (APIC), plus MPEG frame header parsing for bitrate/sample rate.
/// </summary>
public static class Id3v2Parser
{
    private const int Id3v2HeaderSize = 10;
    private const int Id3v1TagSize = 128;
    private const int Id3v1HeaderMagic = 0x47414D54; // "TAG" (little-endian read as LE uint)

    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            long fileSize = fs.Length;

            // ── ID3v2 tag data (declared here for scope across if/else) ──
            int id3v2Size = 0;
            string? title = null, artist = null, album = null;
            int? durationMs = null;
            byte[]? coverArt = null;

            byte[]? rawHeader = reader.ReadBytes(Id3v2HeaderSize);
            if (rawHeader.Length >= 3 &&
                rawHeader[0] == 0x49 && rawHeader[1] == 0x44 && rawHeader[2] == 0x33) // "ID3"
            {
                int majorVer = rawHeader[3];
                // byte minorVer = rawHeader[4];
                // byte flags = rawHeader[5];
                id3v2Size = ReadSyncsafeInt(rawHeader, 6); // size of tag data (excl. header)
                // Skip the extended header if present (v2.3 flags bit 6 or v2.4 flags bit 6)
                long pos = Id3v2HeaderSize;
                if (majorVer >= 3 && (rawHeader[5] & 0x40) != 0)
                {
                    // Extended header: read 4-byte (v2.3) or syncsafe (v2.4) size
                    byte[] extBuf = reader.ReadBytes(4);
                    int extSize = majorVer == 4 ? ReadSyncsafeInt(extBuf, 0) : ReadBE32(extBuf, 0);
                    pos += extSize;
                }
                else
                {
                    pos = Id3v2HeaderSize;
                }

                // ── Parse ID3v2 frames ──

                long framesEnd = Id3v2HeaderSize + id3v2Size;
                while (pos + 10 <= framesEnd)
                {
                    reader.BaseStream.Seek(pos, SeekOrigin.Begin);
                    byte[] frameHeader = reader.ReadBytes(10);
                    if (frameHeader.Length < 10) break;

                    // Frame ID (4 bytes)
                    string frameId = Encoding.ASCII.GetString(frameHeader, 0, 4);

                    // Zero frame ID = padding, stop scanning
                    if (frameId[0] == '\0') break;

                    // Frame size: v2.4 uses syncsafe, v2.3 uses regular BE
                    int frameSize = majorVer == 4
                        ? ReadSyncsafeInt(frameHeader, 4)
                        : ReadBE32(frameHeader, 4);

                    if (frameSize <= 0 || pos + 10 + frameSize > framesEnd) break;

                    // Read frame data
                    byte[] frameData = new byte[frameSize];
                    int bytesRead = reader.Read(frameData, 0, frameSize);
                    if (bytesRead < frameSize) break;

                    switch (frameId)
                    {
                        case "TIT2":
                            title = DecodeTextFrame(frameData);
                            break;
                        case "TPE1":
                            artist = DecodeTextFrame(frameData);
                            break;
                        case "TALB":
                            album = DecodeTextFrame(frameData);
                            break;
                        case "TLEN":
                            string lenStr = DecodeTextFrame(frameData) ?? "";
                            if (long.TryParse(lenStr, out var lenMs))
                                durationMs = (int)lenMs;
                            break;
                        case "APIC":
                            coverArt = ExtractApicData(frameData);
                            break;
                    }

                    pos += 10 + frameSize;
                }
            }

            // ── Find first MPEG frame header for bitrate/sample rate ──
            int? bitrate = null;
            int? sampleRate = null;
            int? channels = null;
            int? mpegDurationSec = null;

            long searchStart = Math.Max(fs.Position, Id3v2HeaderSize + id3v2Size);
            SeekMpegFrameSync(fs, reader, searchStart);

            if (fs.Position < fs.Length - 4)
            {
                ParseMpegFrameHeader(reader, out bitrate, out sampleRate, out channels);

                // Try to find Xing/Info header for VBR bitrate and accurate frame count
                if (sampleRate > 0)
                {
                    long xingPos = GetXingHeaderOffset(fs, reader);
                    if (xingPos > 0)
                    {
                        reader.BaseStream.Seek(xingPos, SeekOrigin.Begin);
                        if (TryParseXingHeader(reader, sampleRate.Value, out var xingBitrate, out mpegDurationSec) && xingBitrate > 0)
                            bitrate = xingBitrate;
                    }
                }
            }

            // ── If no ID3v2, try ID3v1 ──
            if (title == null && artist == null && album == null && fileSize >= Id3v1TagSize)
            {
                reader.BaseStream.Seek(fileSize - Id3v1TagSize, SeekOrigin.Begin);
                ParseId3v1(reader, out title, out artist, out album);
            }

            // ── Duration: TLEN > MPEG/Xing estimate > bitrate estimate ──
            TimeSpan? duration = null;
            if (durationMs > 0)
                duration = TimeSpan.FromMilliseconds(durationMs.Value);
            else if (mpegDurationSec > 0)
                duration = TimeSpan.FromSeconds(mpegDurationSec.Value);
            else if (bitrate > 0)
            {
                // Estimate from file size after ID3v2 tag
                long audioDataSize = fileSize - (Id3v2HeaderSize + id3v2Size);
                if (audioDataSize > 0)
                {
                    double estSec = (audioDataSize * 8.0) / (bitrate.Value * 1000.0);
                    duration = TimeSpan.FromSeconds(estSec);
                }
            }

            return new FileFormatInfo
            {
                Format = FileFormat.Mp3,
                DisplayName = "MP3 音频",
                Extension = Path.GetExtension(filePath),
                FileSize = fileSize,
                Title = title,
                Artist = artist,
                Album = album,
                Duration = duration,
                Bitrate = bitrate,
                SampleRate = sampleRate,
                Channels = channels,
                CoverArtData = coverArt,
            };
        }
        catch (Exception ex)
        {
            CoreLog.Info($"Id3v2Parser.Parse failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Read a 4-byte syncsafe integer from buffer at offset.
    /// Each byte uses only 7 bits (MSB = 0), yielding a 28-bit value.
    /// </summary>
    private static int ReadSyncsafeInt(byte[] buf, int offset)
    {
        return (buf[offset] & 0x7F) << 21
             | (buf[offset + 1] & 0x7F) << 14
             | (buf[offset + 2] & 0x7F) << 7
             | (buf[offset + 3] & 0x7F);
    }

    /// <summary>
    /// Read a 4-byte big-endian integer.
    /// </summary>
    private static int ReadBE32(byte[] buf, int offset)
    {
        return (buf[offset] << 24)
             | (buf[offset + 1] << 16)
             | (buf[offset + 2] << 8)
             | buf[offset + 3];
    }

    /// <summary>
    /// Decode a text frame (ID3v2.3/2.4) considering text encoding byte.
    /// Encoding: $00=ISO-8859-1, $01=UTF-16 w/BOM, $02=UTF-16BE, $03=UTF-8
    /// </summary>
    private static string? DecodeTextFrame(byte[] data)
    {
        if (data.Length < 2) return null;

        int encoding = data[0];
        int textOffset = 1;

        // Skip null terminator(s) following encoding byte for empty-ish frames
        if (textOffset >= data.Length) return null;

        string result;
        switch (encoding)
        {
            case 0: // ISO-8859-1
                result = Encoding.Latin1.GetString(data, textOffset, data.Length - textOffset);
                break;
            case 1: // UTF-16 with BOM
                if (data.Length - textOffset < 2) return null;
                result = data[textOffset] == 0xFE && data[textOffset + 1] == 0xFF
                    ? Encoding.BigEndianUnicode.GetString(data, textOffset, data.Length - textOffset)
                    : Encoding.Unicode.GetString(data, textOffset, data.Length - textOffset);
                break;
            case 2: // UTF-16BE
                result = Encoding.BigEndianUnicode.GetString(data, textOffset, data.Length - textOffset);
                break;
            case 3: // UTF-8
                result = Encoding.UTF8.GetString(data, textOffset, data.Length - textOffset);
                break;
            default:
                return null;
        }

        // Trim null terminators and whitespace
        int nullPos = result.IndexOf('\0');
        if (nullPos >= 0) result = result[..nullPos];
        return result.Trim();
    }

    /// <summary>
    /// Extract binary picture data from an APIC (Attached Picture) frame.
    /// Frame layout: encoding(1) + MIME(null-term) + pictureType(1) + description(encoded null-term) + data
    /// </summary>
    private static byte[]? ExtractApicData(byte[] frameData)
    {
        if (frameData.Length < 6) return null;

        int offset = 1; // skip encoding byte

        // Read MIME type (null-terminated)
        int mimeEnd = FindNullByte(frameData, offset);
        if (mimeEnd < 0 || mimeEnd >= frameData.Length - 1) return null;
        offset = mimeEnd + 1;

        offset++; // skip picture type byte

        // Read description (encoded string - skip past null terminator)
        int encoding = frameData[0];
        int descEnd;
        if (encoding == 1 || encoding == 2)
        {
            // UTF-16: null terminator is 2 bytes (00 00)
            descEnd = FindNullUtf16(frameData, offset);
            if (descEnd < 0) return null;
            offset = descEnd + 2;
        }
        else
        {
            // ISO-8859-1 or UTF-8: null terminator is 1 byte
            descEnd = FindNullByte(frameData, offset);
            if (descEnd < 0) return null;
            offset = descEnd + 1;
        }

        if (offset >= frameData.Length) return null;

        // Remaining bytes are the picture data
        int picLen = frameData.Length - offset;
        byte[] picData = new byte[picLen];
        Buffer.BlockCopy(frameData, offset, picData, 0, picLen);
        return picData;
    }

    private static int FindNullByte(byte[] data, int start)
    {
        for (int i = start; i < data.Length; i++)
            if (data[i] == 0) return i;
        return -1;
    }

    private static int FindNullUtf16(byte[] data, int start)
    {
        for (int i = start; i < data.Length - 1; i += 2)
            if (data[i] == 0 && data[i + 1] == 0) return i;
        return -1;
    }

    /// <summary>
    /// Seek the stream to the first valid MPEG frame sync word (0xFFE...).
    /// Search begins from <paramref name="startOffset"/>.
    /// </summary>
    private static void SeekMpegFrameSync(FileStream fs, BinaryReader reader, long startOffset)
    {
        long end = Math.Min(fs.Length, startOffset + 16 * 1024); // search first 16KB after ID3 tags
        fs.Seek(startOffset, SeekOrigin.Begin);

        for (long pos = startOffset; pos < end - 1; pos++)
        {
            byte b1 = reader.ReadByte();
            byte b2 = reader.ReadByte();

            // Sync word: first 11 bits are 1s = 0xFF 0xE0...
            if (b1 == 0xFF && (b2 & 0xE0) == 0xE0)
            {
                // Validate by reading the THIRD byte (byte index 2) which contains
                // bitrate index (bits 7-4) and sample rate index (bits 3-2).
                // Byte 1 = AAABBCCD (version+layer), Byte 2 = EEEEFFGH (bitrate+samplerate).
                byte b3 = reader.ReadByte();
                int bitrateIdx = (b3 >> 4) & 0x0F;
                int sampleRateIdx = (b3 >> 2) & 0x03;
                if (bitrateIdx != 0x0F && bitrateIdx != 0 && sampleRateIdx != 0x03)
                {
                    // Found valid sync - seek back to frame header start
                    fs.Seek(pos, SeekOrigin.Begin);
                    return;
                }
                // Didn't match, seek back to pos+1 to continue sliding
                fs.Seek(pos + 1, SeekOrigin.Begin);
            }
            else
            {
                // Step back 1 byte (we read 2, we want to slide by 1)
                fs.Seek(pos + 1, SeekOrigin.Begin);
            }
        }
    }

    /// <summary>
    /// Parse MPEG frame header at current stream position (4 bytes).
    /// Sets out parameters from the header.
    ///
    /// MPEG frame header byte layout (big-endian):
    ///   Byte 0: AAAAAAAA  (sync word bits 0-7)
    ///   Byte 1: AAABBCCD  (sync bits 8-10, version BB, layer CC, protection D)
    ///   Byte 2: EEEEFFGH  (bitrate index EEEE, sample rate FF, padding G, private H)
    ///   Byte 3: IIJJKLMM  (channel mode II, mode ext JJ, copyright K, original L, emphasis MM)
    /// </summary>
    private static void ParseMpegFrameHeader(BinaryReader reader, out int? bitrate, out int? sampleRate, out int? channels)
    {
        bitrate = null;
        sampleRate = null;
        channels = null;

        if (reader.BaseStream.Length - reader.BaseStream.Position < 4) return;
        long startPos = reader.BaseStream.Position;

        int b0 = reader.ReadByte();  // byte 0: sync bits 0-7
        int b1 = reader.ReadByte();  // byte 1: sync+version+layer+protection
        int b2 = reader.ReadByte();  // byte 2: bitrate+samplerate+padding+private
        int b3 = reader.ReadByte();  // byte 3: channel+mode+copyright+original+emphasis

        // Byte 0 must be sync word 0xFF
        if (b0 != 0xFF) return;

        // Byte 1: upper 3 bits = sync continuation (must be 111)
        if ((b1 & 0xE0) != 0xE0) return;

        // Parse fields from byte 1:
        int versionBits = (b1 >> 3) & 0x03;   // bits 4-3 = BB = MPEG version
        int layerBits   = (b1 >> 1) & 0x03;   // bits 2-1 = CC = Layer

        // Parse fields from byte 2:
        int bitrateIdx    = (b2 >> 4) & 0x0F; // bits 7-4 = EEEE = bitrate index
        int sampleRateIdx = (b2 >> 2) & 0x03; // bits 3-2 = FF   = sample rate index

        // Parse fields from byte 3:
        int channelMode = (b3 >> 6) & 0x03;   // bits 7-6 = II = channel mode

        // Validate bitrate/sample rate indices
        if (bitrateIdx == 0 || bitrateIdx == 0x0F) return;
        if (sampleRateIdx == 0x03) return;

        // Determine MPEG version
        // BB: 11=MPEG1, 10=MPEG2, 01=reserved, 00=MPEG2.5
        int mpegVersion;
        switch (versionBits)
        {
            case 3: mpegVersion = 1; break;
            case 2: mpegVersion = 2; break;
            case 0: mpegVersion = 25; break; // MPEG 2.5
            default: return;
        }

        // Determine Layer
        // CC: 11=Layer I, 10=Layer II, 01=Layer III
        int layer;
        switch (layerBits)
        {
            case 3: layer = 1; break;
            case 2: layer = 2; break;
            case 1: layer = 3; break;
            default: return;
        }

        // Only care about Layer III (MP3)
        // Note: some MP3 files may use Layer II or I; accept all three.
        if (layer != 3) return;

        // Bitrate table [mpegVersion][bitrateIdx]
        // mpegVersion: 1=MPEG1, 2=MPEG2, 25=MPEG2.5 (Layer III only)
        int[,] bitrateTable = new int[3, 16]
        {
            // MPEG1  Layer III (index 0)
            { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 },
            // MPEG2  Layer III (index 1)
            { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 },
            // MPEG2.5 Layer III (index 2)
            { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 },
        };

        int verIdx = mpegVersion == 1 ? 0 : (mpegVersion == 2 ? 1 : 2);
        bitrate = bitrateTable[verIdx, bitrateIdx];

        // Sample rate table [mpegVersion][sampleRateIdx]
        int[,] sampleRateTable = new int[3, 3]
        {
            { 44100, 48000, 32000 },   // MPEG1
            { 22050, 24000, 16000 },   // MPEG2
            { 11025, 12000, 8000 },    // MPEG2.5
        };

        verIdx = mpegVersion == 1 ? 0 : (mpegVersion == 2 ? 1 : 2);
        sampleRate = sampleRateTable[verIdx, sampleRateIdx];

        // Channels: 3=mono, else stereo
        channels = channelMode == 3 ? 1 : 2;

        // Reset position
        reader.BaseStream.Seek(startPos, SeekOrigin.Begin);
    }

    /// <summary>
    /// After reading the frame header at current position, compute the offset
    /// of the Xing/Info header (if present) and seek to it.
    /// Returns the offset, or -1 if no Xing/Info header found.
    /// Only works for Layer III.
    /// </summary>
    private static long GetXingHeaderOffset(FileStream fs, BinaryReader reader)
    {
        long startPos = reader.BaseStream.Position;
        if (reader.BaseStream.Length - startPos < 4) return -1;

        int b0 = reader.ReadByte();  // byte 0: sync
        int b1 = reader.ReadByte();  // byte 1: version + layer + protection
        int b2 = reader.ReadByte();  // byte 2: bitrate + sample rate
        int b3 = reader.ReadByte();  // byte 3: channel + mode + ...

        if (b0 != 0xFF || (b1 & 0xE0) != 0xE0) return -1;

        int versionBits = (b1 >> 3) & 0x03;  // byte 1 bits 4-3
        int channelMode = (b3 >> 6) & 0x03;  // byte 3 bits 7-6

        int mpegVersion;
        switch (versionBits)
        {
            case 3: mpegVersion = 1; break;
            case 2: mpegVersion = 2; break;
            case 0: mpegVersion = 25; break;
            default: return -1;
        }

        // Side info size for Layer III
        int sideInfoSize;
        if (mpegVersion == 1)
            sideInfoSize = channelMode == 3 ? 17 : 32;
        else
            sideInfoSize = channelMode == 3 ? 9 : 17;

        long xingOffset = startPos + 4 + sideInfoSize;
        if (xingOffset + 4 > fs.Length) return -1;

        reader.BaseStream.Seek(xingOffset, SeekOrigin.Begin);
        byte[] sig = reader.ReadBytes(4);
        if (sig.Length < 4) return -1;

        if (sig[0] == 'X' && sig[1] == 'i' && sig[2] == 'n' && sig[3] == 'g')
            return xingOffset;
        if (sig[0] == 'I' && sig[1] == 'n' && sig[2] == 'f' && sig[3] == 'o')
            return xingOffset;

        return -1;
    }

    /// <summary>
    /// Parse Xing/Info header for VBR bitrate and duration.
    /// Returns true if the header was fully parsed (at least one field found).
    /// </summary>
    private static bool TryParseXingHeader(BinaryReader reader, int sampleRate, out int? avgBitrate, out int? durationSec)
    {
        avgBitrate = null;
        durationSec = null;

        // Read flags (4 bytes after "Xing"/"Info")
        byte[] flags = reader.ReadBytes(4);
        if (flags.Length < 4) return false;

        int frameFlag = (flags[0] >> 0) & 1;   // bit 0: number of frames present
        int bytesFlag = (flags[0] >> 1) & 1;   // bit 1: bytes present

        int numFrames = 0;
        long numBytes = 0;

        if (frameFlag != 0)
        {
            byte[] fb = reader.ReadBytes(4);
            if (fb.Length < 4) return false;
            numFrames = ReadBE32(fb, 0);
        }

        if (bytesFlag != 0)
        {
            byte[] bb = reader.ReadBytes(4);
            if (bb.Length < 4) return false;
            // Need to treat as unsigned
            numBytes = (long)(uint)ReadBE32(bb, 0);
        }

        // Calculate duration and average bitrate
        // MPEG1 Layer III = 1152 samples/frame, MPEG2/2.5 = 576
        // We don't know the version here; assume MPEG1 (1152) which is most common
        int samplesPerFrame = 1152;
        if (numFrames > 0 && sampleRate > 0)
        {
            durationSec = (int)((long)numFrames * samplesPerFrame / sampleRate);
        }

        if (numBytes > 0 && durationSec > 0 && durationSec.Value > 0)
        {
            avgBitrate = (int)((numBytes * 8) / durationSec.Value / 1000);
        }

        return true;
    }

    /// <summary>
    /// Parse ID3v1 tag from the last 128 bytes of the file.
    /// Only used as fallback when no ID3v2 tags are found.
    /// </summary>
    private static void ParseId3v1(BinaryReader reader, out string? title, out string? artist, out string? album)
    {
        title = null;
        artist = null;
        album = null;

        byte[] tag = reader.ReadBytes(Id3v1TagSize);
        if (tag.Length < Id3v1TagSize) return;

        // "TAG" magic
        if (tag[0] != 'T' || tag[1] != 'A' || tag[2] != 'G') return;

        title = ReadId3v1String(tag, 3, 30);
        artist = ReadId3v1String(tag, 33, 30);
        album = ReadId3v1String(tag, 63, 30);
    }

    private static string? ReadId3v1String(byte[] tag, int offset, int maxLen)
    {
        int end = offset + maxLen;
        int actualLen = 0;
        for (int i = offset; i < end && tag[i] != 0; i++)
            actualLen++;

        if (actualLen == 0) return null;
        return Encoding.Latin1.GetString(tag, offset, actualLen).Trim();
    }
}
