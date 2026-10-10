using System;
using System.IO;

namespace MantisZip.Core.Utils;

/// <summary>
/// FLAC 文件头部解析器：迭代全部元数据块，解析 STREAMINFO(type 0) 与
/// PICTURE(type 6) 封面；其余块（PADDING/APPLICATION/SEEKTABLE/VORBIS_COMMENT/
/// CUESHEET 等）按载荷长度跳过，不解析内容。
/// </summary>
public static class FlacParser
{
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            long fileSize = fs.Length;

            using var reader = new BinaryReader(fs);

            // "fLaC" signature
            if (reader.ReadUInt32() != 0x43614C66) return null;

            var streamInfo = ExtractStreamInfo(filePath, reader, fileSize);
            if (streamInfo == null) return null;
            streamInfo.CoverArtData = ExtractPictureBlock(reader, fs);
            return streamInfo;
        }
        catch (Exception ex) { CoreLog.Info($"FlacParser.Parse failed: {ex.Message}"); return null; }
    }

    /// <summary>
    /// 前置条件：已消费 "fLaC" 魔数。
    /// 解析首个 STREAMINFO(type 0) 元数据块，提取 duration/sampleRate/channels/bitDepth/bitrate。
    /// 若首个块不是有效 STREAMINFO（载荷 &lt;34 字节），返回 null（与历史行为一致）。
    /// </summary>
    private static FileFormatInfo? ExtractStreamInfo(string filePath, BinaryReader reader, long fileSize)
    {
        // Metadata block header
        reader.ReadByte(); // last-block flag + block type
        int blockSize = (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();

        // STREAMINFO block (usually first, 34 bytes)
        if (blockSize < 34) return null;

        reader.ReadBytes(2); // minBlock
        reader.ReadBytes(2); // maxBlock
        reader.ReadBytes(3); // minFrame (24-bit)
        reader.ReadBytes(3); // maxFrame (24-bit)

        // ── 接下来的 8 字节包含 20-bit 采样率 + 3-bit 声道数-1 + 5-bit 位深-1 + 36-bit 总样本数 ──
        // 采用 big-endian 位打包，MSB 优先：
        //   buf[0..1] 全 16 位 + buf[2] 高 4 位 = 20-bit 采样率
        //   buf[2] bits 1-3 = 声道数-1
        //   buf[2] bit 0 + buf[3] 高 4 位 = 5-bit 位深-1
        //   buf[3] 低 4 位 + buf[4..7] = 36-bit 总样本数
        byte[] buf = reader.ReadBytes(8);
        int sampleRate = (buf[0] << 12) | (buf[1] << 4) | (buf[2] >> 4);
        int channels = ((buf[2] & 0x0E) >> 1) + 1;
        int bitsPerSample = (((buf[2] & 0x01) << 4) | ((buf[3] & 0xF0) >> 4)) + 1;

        long totalSamples = ((long)(buf[3] & 0x0F) << 32)
                          | ((long)buf[4] << 24)
                          | ((long)buf[5] << 16)
                          | ((long)buf[6] << 8)
                          | buf[7];

        // 跳过 STREAMINFO 剩余 16 字节 MD5（34 - 18 已读）
        reader.ReadBytes(16);

        TimeSpan? duration = sampleRate > 0 && totalSamples > 0
            ? TimeSpan.FromSeconds((double)totalSamples / sampleRate)
            : null;

        // 计算码率: FLAC 的码率是压缩后的，用文件大小反推
        int? bitrate = duration.HasValue && duration.Value.TotalSeconds > 0
            ? (int)((fileSize * 8) / duration.Value.TotalSeconds / 1000)
            : null;

        return new FileFormatInfo
        {
            Format = FileFormat.Flac,
            DisplayName = "FLAC 音频",
            Extension = Path.GetExtension(filePath),
            FileSize = fileSize,
            SampleRate = sampleRate,
            Channels = channels,
            BitDepth = bitsPerSample,
            Bitrate = bitrate,
            Duration = duration,
        };
    }

    /// <summary>
    /// 前置条件：已消费 STREAMINFO 头字节（流位置位于 STREAMINFO 载荷起点）。
    /// 从该点起按块头（1 字节类型/标志 + 3 字节大端长度）向后游走，
    /// 定位首个 PICTURE(type 6) 块并读取其图片数据；其余块只跳过载荷。
    /// 任何异常/截断均返回 null（由外层 catch 兜底返回 null，保持 STREAMINFO 无损）。
    /// </summary>
    private static byte[]? ExtractPictureBlock(BinaryReader reader, FileStream fs)
    {
        // 前置条件：STREAMINFO 载荷已全部读完，流位置 = 下一个元数据块头起点
        long pos = fs.Position;

        while (pos + 4 <= fs.Length)
        {
            fs.Position = pos;
            byte header = reader.ReadByte();
            bool isLast = (header & 0x80) != 0;
            int blockType = header & 0x7F;
            int payloadLen = (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();

            // 读取整块载荷进入缓冲区（含必要越界检查；越界/截断则放弃封面，保持 STREAMINFO 成果）
            if (payloadLen < 0 || (long)pos + 4 + payloadLen > fs.Length)
                break;
            byte[] payload = reader.ReadBytes(payloadLen);

            if (payload.Length < payloadLen)
                break;

            if (blockType == 6)
            {
                return TryParsePicturePayload(payload);
            }

            pos += 4 + (long)payloadLen;

            if (isLast) break;
        }

        return null;
    }

    /// <summary>
    /// 解析 PICTURE 块载荷（FLAC spec）：
    /// 4B 图片类型 + 4B MIME 长度 + MIME + 4B 描述长度 + 描述（跳过）
    /// + 4B 宽 + 4B 高 + 4B 色深 + 4B 索引色数（跳过）
    /// + 4B 数据长度 + 数据。
    /// </summary>
    private static byte[]? TryParsePicturePayload(byte[] payload)
    {
        int offset = 0;

        // 图片类型 (4 bytes)
        if (!TryReadBe32(payload, ref offset, out _)) return null;

        // MIME 字符串
        if (!TryReadBe32(payload, ref offset, out int mimeLen)) return null;
        if (mimeLen < 0 || (long)offset + mimeLen > payload.Length) return null;
        offset += mimeLen;

        // 描述字符串
        if (!TryReadBe32(payload, ref offset, out int descLen)) return null;
        if (descLen < 0 || (long)offset + descLen > payload.Length) return null;
        offset += descLen;

        // 宽 / 高 / 色深 / 索引色数
        if (!TryReadBe32(payload, ref offset, out _)) return null; // width
        if (!TryReadBe32(payload, ref offset, out _)) return null; // height
        if (!TryReadBe32(payload, ref offset, out _)) return null; // depth
        if (!TryReadBe32(payload, ref offset, out _)) return null; // colors

        // 图片数据
        if (!TryReadBe32(payload, ref offset, out int dataLen)) return null;
        if (dataLen < 0 || (long)offset + dataLen > payload.Length) return null;

        byte[] data = new byte[dataLen];
        Buffer.BlockCopy(payload, offset, data, 0, dataLen);
        return data;
    }

    /// <summary>
    /// 从 Byte[] 中指定偏移读取 4 字节 big-endian 无符号整数（作为 int）。
    /// </summary>
    private static bool TryReadBe32(byte[] buf, ref int offset, out int value)
    {
        if (offset < 0 || (long)offset + 4 > buf.Length)
        {
            value = 0;
            return false;
        }
        value = (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];
        offset += 4;
        return true;
    }
}
