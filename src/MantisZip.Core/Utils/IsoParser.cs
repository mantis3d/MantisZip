using System;
using System.IO;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// ISO 9660 文件头部解析器。
/// </summary>
public static class IsoParser
{
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            // Primary Volume Descriptor at offset 0x8000
            fs.Seek(0x8000, SeekOrigin.Begin);
            if (reader.ReadByte() != 1) return null; // descriptor type 1 = Primary
            if (Encoding.ASCII.GetString(reader.ReadBytes(5)) != "CD001") return null;
            reader.ReadByte(); // version

            reader.ReadBytes(32); // system identifier

            byte[] volLabelBytes = reader.ReadBytes(32);
            string volumeLabel = Encoding.ASCII.GetString(volLabelBytes).TrimEnd();

            reader.ReadBytes(8); // zeros
            long? diskSize = reader.ReadUInt32() * 2048L; // volume space size

            // Format type detection (UDF)
            string format = "ISO 9660";
            fs.Seek(0x10000, SeekOrigin.Begin);
            if (fs.Length >= 0x10000 + 6)
            {
                byte[] b = new byte[6];
                fs.ReadExactly(b, 0, 6);
                if (Encoding.ASCII.GetString(b) == "*UDF*\0")
                    format = "UDF";
            }

            return new FileFormatInfo
            {
                Format = FileFormat.Iso,
                DisplayName = "光盘映像",
                Extension = Path.GetExtension(filePath),
                FileSize = new FileInfo(filePath).Length,
                VolumeLabel = string.IsNullOrWhiteSpace(volumeLabel) ? null : volumeLabel,
                DiskSize = diskSize,
                AdditionalInfo = format,
            };
        }
        catch (Exception ex) { CoreLog.Info($"IsoParser.Parse failed: {ex.Message}"); return null; }
    }
}
