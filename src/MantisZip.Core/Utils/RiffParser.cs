using System;
using System.IO;

namespace MantisZip.Core.Utils;

/// <summary>
/// RIFF/WAV 文件头部解析器。
/// </summary>
public static class RiffParser
{
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            // RIFF header
            if (reader.ReadUInt32() != 0x46464952) return null; // "RIFF"
            reader.ReadUInt32(); // file size
            if (reader.ReadUInt32() != 0x45564157) return null; // "WAVE"

            int? sampleRate = null, channels = null, bitsPerSample = null;
            long dataSize = 0;

            // Scan chunks
            while (fs.Position < fs.Length - 8)
            {
                uint chunkId = reader.ReadUInt32();
                uint chunkSize = reader.ReadUInt32();
                long chunkEnd = fs.Position + chunkSize;

                if (chunkId == 0x20746D66) // "fmt "
                {
                    if (chunkSize >= 16)
                    {
                        reader.ReadUInt16(); // audioFormat
                        channels = reader.ReadUInt16();
                        sampleRate = (int)reader.ReadUInt32();
                        reader.ReadUInt32(); // byteRate
                        reader.ReadUInt16(); // blockAlign
                        bitsPerSample = reader.ReadUInt16();
                    }
                }
                else if (chunkId == 0x61746164) // "data"
                {
                    dataSize = chunkSize;
                }

                fs.Seek(chunkEnd, SeekOrigin.Begin);
            }

            // Calculate duration
            TimeSpan? duration = null;
            if (sampleRate > 0 && channels > 0 && bitsPerSample > 0 && dataSize > 0)
            {
                double totalSamples = dataSize / (double)(channels.Value * bitsPerSample.Value / 8);
                duration = TimeSpan.FromSeconds(totalSamples / sampleRate.Value);
            }

            return new FileFormatInfo
            {
                Format = FileFormat.Wav,
                DisplayName = "WAV 音频",
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
