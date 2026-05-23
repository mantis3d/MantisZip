using System;
using System.Collections.Generic;
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

            // Page size: big-endian uint16
            byte[] psBuf = reader.ReadBytes(2);
            int pageSize = (psBuf[0] << 8) | psBuf[1];
            if (pageSize == 1) pageSize = 65536;

            reader.ReadByte(); // writeVersion
            reader.ReadByte(); // readVersion
            reader.ReadByte(); // reservedSpace
            reader.ReadByte(); // maxEmbeddedFrac

            fs.Seek(0, SeekOrigin.Begin);
            byte[] fullHeader = reader.ReadBytes(100);

            // Text encoding: offset 56, 4 bytes big-endian (1=UTF-8, 2=UTF-16LE, 3=UTF-16BE)
            uint encVal = 0;
            for (int i = 0; i < 4; i++)
                encVal = (encVal << 8) | fullHeader[56 + i];
            string encoding = encVal switch
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
            // Page 1 layout: 100-byte db header, then B-tree page header.
            int tableCount = 0;
            var tableNames = new List<string>();
            try
            {
                fs.Seek(100, SeekOrigin.Begin);
                byte pageType = reader.ReadByte();
                reader.ReadBytes(2); // freeblock
                byte[] cellCountBuf = reader.ReadBytes(2);
                int cellCount = (cellCountBuf[0] << 8) | cellCountBuf[1];
                if (pageType is 0x0D or 0x05) // leaf table or interior table
                {
                    tableCount = cellCount;
                    // 尝试从 cell 指针区读取表名（仅 leaf table pageType 0x0D）
                    if (pageType == 0x0D)
                    {
                        reader.ReadBytes(2); // contentStart
                        reader.ReadByte();   // fragFreeBytes
                        for (int i = 0; i < Math.Min(cellCount, 20); i++)
                        {
                            byte[] cellPtr = reader.ReadBytes(2);
                            int ptr = (cellPtr[0] << 8) | cellPtr[1];
                            long saved = fs.Position;
                            fs.Seek(ptr, SeekOrigin.Begin);
                            var name = ReadTableName(fs, reader);
                            if (name != null) tableNames.Add(name);
                            fs.Seek(saved, SeekOrigin.Begin);
                        }
                    }
                }
            }
            catch
            {
                // Non-critical
            }

            var fi = new FileInfo(filePath);
            string pageSizeDisplay = pageSize >= 1024 ? $"{pageSize / 1024} KB" : $"{pageSize} B";
            string additional = $"编码: {encoding} | 页大小: {pageSizeDisplay}";
            if (pageCount > 0) additional += $" | 总页数: {pageCount}";

            return new FileFormatInfo
            {
                Format = FileFormat.Sqlite,
                DisplayName = "SQLite 数据库",
                Extension = Path.GetExtension(filePath),
                FileSize = fi.Length,
                TextEncoding = encoding,
                EntryCount = (int)pageCount,
                TableCount = tableCount,
                TableNames = tableNames,
                AdditionalInfo = additional,
            };
        }
        catch (Exception ex) { CoreLog.Info($"SQLiteParser.Parse failed: {ex.Message}"); return null; }
    }

    /// <summary>
    /// 从 sqlite_master 的 cell payload 中读取表名。
    /// Payload 包含: (varint) payloadLen, (varint) rowid, header(serialTypes...), data...
    /// 先解析整个 header 得到 serialType 数组，再用 serialType 计算偏移。
    /// sqlite_master 列: type, name, tbl_name, rootpage, sql
    /// </summary>
    private static string? ReadTableName(FileStream fs, BinaryReader reader)
    {
        try
        {
            // Skip varint payloadLen
            SkipVarint(reader);
            // Skip varint rowid
            SkipVarint(reader);

            // 读取 header 长度
            int headerLen = ReadVarint(reader);

            // 读取 header 内的 serial type 并记录每个字段的长度
            long headerEnd = fs.Position + headerLen - 1; // -1 because we already consumed the headerLen byte
            var fieldLengths = new List<int>();
            while (fs.Position < headerEnd)
            {
                int serialType = ReadVarint(reader);
                int len;
                if (serialType >= 13 && serialType % 2 == 1)
                    len = (serialType - 13) / 2; // text
                else if (serialType >= 12 && serialType % 2 == 0)
                    len = (serialType - 12) / 2; // blob
                else if (serialType <= 9)
                    len = new[] { 0, 1, 2, 3, 4, 6, 8, 8, 0, 0 }[serialType];
                else
                    len = 0;
                fieldLengths.Add(len);
            }

            // 跳到 data 区起始（紧接在 header 之后）
            long dataStart = headerEnd;
            fs.Seek(dataStart, SeekOrigin.Begin);

            // 跳过第 1 个字段（type）
            if (fieldLengths.Count > 0)
                reader.ReadBytes(fieldLengths[0]);

            // 读第 2 个字段（name）
            if (fieldLengths.Count > 1 && fieldLengths[1] > 0)
            {
                byte[] buf = reader.ReadBytes(fieldLengths[1]);
                return Encoding.UTF8.GetString(buf);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 跳过 1 个 varint。
    /// </summary>
    private static void SkipVarint(BinaryReader reader)
    {
        while ((reader.ReadByte() & 0x80) != 0) { }
    }

    /// <summary>
    /// 读取 1 个 varint。
    /// </summary>
    private static int ReadVarint(BinaryReader reader)
    {
        int val = 0;
        int shift = 0;
        byte b;
        do
        {
            b = reader.ReadByte();
            val = (val << 7) | (b & 0x7F);
            shift += 7;
        } while ((b & 0x80) != 0 && shift < 56);
        return val;
    }
}
