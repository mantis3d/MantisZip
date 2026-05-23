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

            ushort pageSizeRaw = reader.ReadUInt16();
            int pageSize = pageSizeRaw == 1 ? 65536 : pageSizeRaw;

            reader.ReadByte(); // writeVersion
            reader.ReadByte(); // readVersion
            reader.ReadByte(); // reservedSpace
            reader.ReadByte(); // maxEmbeddedFrac

            fs.Seek(0, SeekOrigin.Begin);
            byte[] fullHeader = reader.ReadBytes(100);

            // Text encoding: offset 56 (1=UTF-8, 2=UTF-16le, 3=UTF-16be)
            byte textEncoding = fullHeader[56];
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

            // Count entries in sqlite_master from page 1 B-tree header.
            // Page 1 layout: 100-byte db header, then B-tree page.
            // B-tree page header: 1 byte pageType, 2 freeblock, 2 cellCount, 2 contentStart, 1 fragFreeBytes
            int tableCount = 0;
            try
            {
                fs.Seek(100, SeekOrigin.Begin);
                byte pageType = reader.ReadByte();
                reader.ReadBytes(2); // freeblock
                byte[] cellCountBuf = reader.ReadBytes(2);
                if (pageType is 0x0D or 0x05) // leaf table or interior table
                    tableCount = (cellCountBuf[0] << 8) | cellCountBuf[1];
            }
            catch
            {
                // Non-critical; leave tableCount at 0
            }

            var fi = new FileInfo(filePath);
            return new FileFormatInfo
            {
                Format = FileFormat.Sqlite,
                DisplayName = "SQLite 数据库",
                Extension = Path.GetExtension(filePath),
                FileSize = fi.Length,
                TextEncoding = encoding,
                EntryCount = (int)pageCount,
                TableCount = tableCount,
                AdditionalInfo = $"页大小: {pageSize}",
            };
        }
        catch (Exception ex) { CoreLog.Info($"SQLiteParser.Parse failed: {ex.Message}"); return null; }
    }
}
