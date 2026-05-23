using System;
using System.IO;

namespace MantisZip.Core.Utils;

/// <summary>
/// FLAC 文件头部解析器（STREAMINFO 元数据块）。
/// </summary>
public static class FlacParser
{
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            // "fLaC" signature
            if (reader.ReadUInt32() != 0x43614C66) return null;

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

            TimeSpan? duration = sampleRate > 0 && totalSamples > 0
                ? TimeSpan.FromSeconds((double)totalSamples / sampleRate)
                : null;

            return new FileFormatInfo
            {
                Format = FileFormat.Flac,
                DisplayName = "FLAC 音频",
                Extension = Path.GetExtension(filePath),
                FileSize = new FileInfo(filePath).Length,
                SampleRate = sampleRate,
                Channels = channels,
                BitDepth = bitsPerSample,
                Duration = duration,
            };
        }
        catch (Exception ex) { CoreLog.Info($"FlacParser.Parse failed: {ex.Message}"); return null; }
    }
}
