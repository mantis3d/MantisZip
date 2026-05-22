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
            reader.ReadBytes(4); // minFrame
            reader.ReadBytes(4); // maxFrame

            // 20 bits sample rate, 3 bits channels-1, 5 bits bitsPerSample-1, 36 bits totalSamples
            byte[] buf = reader.ReadBytes(8);
            int sampleRate = ((buf[0] & 0x0F) << 12) | (buf[1] << 4) | ((buf[2] & 0xF0) >> 4);
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
        catch { return null; }
    }
}
