using System;
using System.IO;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// SQLite 数据库文件头部解析器。
/// </summary>
public static class SQLiteParser
{
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            // Header string
            byte[] magic = reader.ReadBytes(16);
            if (Encoding.ASCII.GetString(magic) != "SQLite format 3\0")
                return null;

            ushort pageSize = reader.ReadUInt16();
            reader.ReadByte(); // writeVersion
            reader.ReadByte(); // readVersion
            reader.ReadByte(); // reservedSpace
            byte encByte = reader.ReadByte(); // maxEmbeddedFrac

            fs.Seek(0, SeekOrigin.Begin);
            byte[] fullHeader = reader.ReadBytes(100);

            // Encoding: offset 18 (after pageSize at 16)
            byte textEncoding = fullHeader[18]; // 1=UTF-8, 2=UTF-16le, 3=UTF-16be
            string encoding = textEncoding switch
            {
                1 => "UTF-8",
                2 => "UTF-16 LE",
                3 => "UTF-16 BE",
                _ => "Unknown"
            };

            // Page count from header (offset 28, 4 bytes big-endian)
            uint pageCount = 0;
            for (int i = 0; i < 4; i++)
                pageCount = (pageCount << 8) | fullHeader[28 + i];

            // Try to count tables by reading first page's sqlite_master

            var fi = new FileInfo(filePath);
            return new FileFormatInfo
            {
                Format = FileFormat.Sqlite,
                DisplayName = "SQLite 数据库",
                Extension = Path.GetExtension(filePath),
                FileSize = fi.Length,
                TextEncoding = encoding,
                EntryCount = (int)pageCount, // reuse as table approximation
                AdditionalInfo = $"页大小: {pageSize}",
            };
        }
        catch { return null; }
    }
}
