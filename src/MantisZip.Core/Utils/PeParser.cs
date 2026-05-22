using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// PE (Portable Executable) 文件解析器。
/// 解析 EXE/DLL/SYS/OCX 的头部信息和版本资源（VS_VERSIONINFO）。
/// </summary>
public static class PeParser
{
    /// <summary>
    /// 解析 PE 文件，返回格式信息；解析失败返回 null。
    /// </summary>
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            // ── DOS 头 ──
            if (reader.ReadUInt16() != 0x5A4D) // "MZ"
                return null;

            // e_lfanew
            fs.Seek(0x3C, SeekOrigin.Begin);
            uint peOffset = reader.ReadUInt32();

            // ── PE 头 ──
            fs.Seek(peOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550) // "PE\0\0"
                return null;

            ushort machine = reader.ReadUInt16();
            ushort numberOfSections = reader.ReadUInt16();
            // Skip: TimeDateStamp(4), PointerToSymbolTable(4), NumberOfSymbols(4)
            reader.ReadBytes(12);
            ushort sizeOfOptionalHeader = reader.ReadUInt16();
            ushort characteristics = reader.ReadUInt16();

            // ── Optional Header ──
            long optHeaderStart = fs.Position;
            ushort magic = reader.ReadUInt16(); // 0x10B=PE32, 0x20B=PE32+
            bool isPE32Plus = magic == 0x20B;

            // 读取 Subsystem（偏移不同）
            ushort subsystem;
            uint numberOfRvaAndSizes;
            if (isPE32Plus)
            {
                // PE32+：Subsystem 在 opt+72, NumberOfRvaAndSizes 在 opt+112
                fs.Seek(optHeaderStart + 72, SeekOrigin.Begin);
                subsystem = reader.ReadUInt16();
                fs.Seek(optHeaderStart + 112, SeekOrigin.Begin);
                numberOfRvaAndSizes = reader.ReadUInt32();
            }
            else
            {
                // PE32：Subsystem 在 opt+68, NumberOfRvaAndSizes 在 opt+92
                fs.Seek(optHeaderStart + 68, SeekOrigin.Begin);
                subsystem = reader.ReadUInt16();
                fs.Seek(optHeaderStart + 92, SeekOrigin.Begin);
                numberOfRvaAndSizes = reader.ReadUInt32();
            }

            // 资源目录 = DataDirectory 索引 2（仅当数量足够时）
            uint resourceDirRVA = 0;
            uint resourceDirSize = 0;
            if (numberOfRvaAndSizes > 2)
            {
                long dataDirEntry2Offset;
                if (isPE32Plus)
                    dataDirEntry2Offset = optHeaderStart + 116 + 2 * 8;
                else
                    dataDirEntry2Offset = optHeaderStart + 96 + 2 * 8;
                fs.Seek(dataDirEntry2Offset, SeekOrigin.Begin);
                resourceDirRVA = reader.ReadUInt32();
                resourceDirSize = reader.ReadUInt32();
            }

            // ── 节表 ──
            long sectionTableOffset = peOffset + 24 + sizeOfOptionalHeader;
            var sections = new List<(uint virtualAddress, uint virtualSize, uint rawOffset, uint rawSize)>();
            for (int i = 0; i < numberOfSections; i++)
            {
                long pos = sectionTableOffset + i * 40L;
                fs.Seek(pos + 8, SeekOrigin.Begin); // skip Name(8)
                uint virtualSize = reader.ReadUInt32();
                uint virtualAddress = reader.ReadUInt32();
                uint sizeOfRawData = reader.ReadUInt32();
                uint pointerToRawData = reader.ReadUInt32();
                sections.Add((virtualAddress, virtualSize, pointerToRawData, sizeOfRawData));
            }

            // ── 节表转换：RVA → 文件偏移 ──

            // ── 资源目录遍历：查找 RT_VERSION (ID=16) ──
            byte[]? versionData = null;
            if (resourceDirRVA != 0)
            {
                uint? rsrcOffset = ResolveRvaAbsolute(resourceDirRVA, sections);
                if (rsrcOffset.HasValue)
                    versionData = FindVersionResourceData(fs, reader, rsrcOffset.Value, resourceDirRVA, sections);
            }

            // ── 解析 VS_VERSIONINFO ──
            string? companyName = null;
            string? productName = null;
            string? fileVersion = null;
            string? productVersion = null;
            string? fileDescription = null;
            string? legalCopyright = null;

            if (versionData != null && versionData.Length > 0)
            {
                using var vms = new MemoryStream(versionData);
                using var vr = new BinaryReader(vms);
                ParseVersionInfo(vr, out companyName, out productName, out fileVersion,
                    out productVersion, out fileDescription, out legalCopyright);
            }

            // ── 构建 FileFormatInfo ──
            string arch = machine switch
            {
                0x014C => "x86",
                0x8664 => "x64",
                0xAA64 => "ARM64",
                0x01C4 => "ARM",
                _ => $"0x{machine:X4}"
            };

            string subSys = subsystem switch
            {
                1 => "Native",
                2 => "GUI",
                3 => "CUI",
                9 => "Native (WinCE)",
                _ => "Other"
            };

            // 如果没从版本资源取到 ProductVersion，用 FileVersion 代替
            productVersion ??= fileVersion;

            return new FileFormatInfo
            {
                Format = FileFormat.Pe,
                DisplayName = "PE 可执行文件",
                Extension = Path.GetExtension(filePath),
                FileSize = new FileInfo(filePath).Length,
                CompanyName = companyName,
                ProductName = productName,
                FileVersion = fileVersion,
                ProductVersion = productVersion,
                Architecture = arch,
                Subsystem = subSys,
                AdditionalInfo = fileDescription,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 将 RVA 转换为文件偏移（通过节表）。
    /// </summary>
    private static uint? ResolveRvaAbsolute(uint rva,
        List<(uint va, uint vs, uint rawOff, uint rawSize)> sections)
    {
        foreach (var (va, vs, rawOff, rawSize) in sections)
        {
            if (rva >= va && rva < va + Math.Max(vs, rawSize))
                return rawOff + (rva - va);
        }
        return null;
    }

    /// <summary>
    /// 在资源目录中查找 RT_VERSION 资源数据。
    /// </summary>
    private static byte[]? FindVersionResourceData(
        FileStream fs, BinaryReader reader, uint rsrcDirOffset, uint rsrcRVA,
        List<(uint va, uint vs, uint rawOff, uint rawSize)> sections)
    {
        // 1) 在类型目录中找 RT_VERSION (ID=16)
        uint? typeEntryOff = FindResourceDirectoryEntryById(fs, reader, rsrcDirOffset, 16);
        if (typeEntryOff == null) return null;

        // 读取类型条目的 OffsetToData，应为子目录
        fs.Seek(typeEntryOff.Value, SeekOrigin.Begin);
        reader.ReadUInt32(); // skip name/id
        uint offsetToData = reader.ReadUInt32();
        if ((offsetToData & 0x80000000) == 0) return null;
        uint subDirOff = offsetToData & 0x7FFFFFFF;

        // 逐层向下：Name → Language → ... 直到找到数据条目
        uint? currentDirAbs = RvaToOffsetInRsrc(rsrcDirOffset, subDirOff, rsrcRVA, sections);
        if (currentDirAbs == null) return null;

        // 最多下钻 4 层避免死循环
        for (int depth = 0; depth < 4; depth++)
        {
            uint? entryOff = FindFirstDirectoryEntry(fs, reader, currentDirAbs.Value);
            if (entryOff == null) return null;

            fs.Seek(entryOff.Value, SeekOrigin.Begin);
            reader.ReadUInt32(); // skip name/id
            uint dataField = reader.ReadUInt32();

            if ((dataField & 0x80000000) == 0)
            {
                // 数据条目 → 读取 IMAGE_RESOURCE_DATA_ENTRY
                uint? dataEntryAbs = RvaToOffsetInRsrc(rsrcDirOffset, dataField, rsrcRVA, sections);
                if (dataEntryAbs == null) return null;

                fs.Seek(dataEntryAbs.Value, SeekOrigin.Begin);
                uint dataRVA = reader.ReadUInt32();
                uint dataSize = reader.ReadUInt32();
                if (dataSize == 0 || dataSize > 1024 * 1024) return null;

                uint? dataOffset = ResolveRvaAbsolute(dataRVA, sections);
                if (dataOffset == null) return null;

                fs.Seek(dataOffset.Value, SeekOrigin.Begin);
                byte[] buf = new byte[dataSize];
                fs.ReadExactly(buf, 0, (int)dataSize);
                return buf;
            }

            // 子目录 → 下钻
            uint nextSubDir = dataField & 0x7FFFFFFF;
            currentDirAbs = RvaToOffsetInRsrc(rsrcDirOffset, nextSubDir, rsrcRVA, sections);
            if (currentDirAbs == null) return null;
        }

        return null; // 超过最大深度
    }

    /// <summary>
    /// 在指定偏移的资源目录中按 ID 查找条目，返回该条目偏移。
    /// </summary>
    private static uint? FindResourceDirectoryEntryById(
        FileStream fs, BinaryReader reader, long dirOffset, ushort targetId)
    {
        fs.Seek(dirOffset + 12, SeekOrigin.Begin); // 跳过前 12 字节 (Characteristics + TimeDateStamp + Version)
        ushort namedEntries = reader.ReadUInt16();
        ushort idEntries = reader.ReadUInt16();
        long firstEntryOffset = dirOffset + 16; // 目录头 16 字节

        // 先跳过 named entries，在 ID entries 中查找
        long idStart = firstEntryOffset + namedEntries * 8;
        for (int i = 0; i < idEntries; i++)
        {
            long entryOffset = idStart + i * 8L;
            fs.Seek(entryOffset, SeekOrigin.Begin);
            uint nameOrId = reader.ReadUInt32();
            if ((nameOrId & 0x80000000) == 0 && (ushort)nameOrId == targetId)
                return (uint)entryOffset;
        }

        return null;
    }

    /// <summary>
    /// 返回目录的第一个条目偏移。
    /// </summary>
    private static uint? FindFirstDirectoryEntry(
        FileStream fs, BinaryReader reader, long dirOffset)
    {
        fs.Seek(dirOffset + 12, SeekOrigin.Begin);
        ushort namedEntries = reader.ReadUInt16();
        ushort idEntries = reader.ReadUInt16();

        if (namedEntries + idEntries == 0) return null;
        return (uint)(dirOffset + 16); // 第一个条目
    }

    /// <summary>
    /// 将资源目录内的相对偏移转换为文件绝对偏移。
    /// </summary>
    private static uint? RvaToOffsetInRsrc(uint rsrcDirOffset, uint offsetInRsrc, uint rsrcRVA,
        List<(uint va, uint vs, uint rawOff, uint rawSize)> sections)
    {
        uint rva = rsrcRVA + offsetInRsrc;
        return ResolveRvaAbsolute(rva, sections);
    }

    /// <summary>
    /// 解析 VS_VERSIONINFO 块，提取字符串信息。
    /// </summary>
    private static void ParseVersionInfo(
        BinaryReader reader,
        out string? companyName, out string? productName,
        out string? fileVersion, out string? productVersion,
        out string? fileDescription, out string? legalCopyright)
    {
        companyName = null;
        productName = null;
        fileVersion = null;
        productVersion = null;
        fileDescription = null;
        legalCopyright = null;

        try
        {
            long startPos = reader.BaseStream.Position;

            // VS_VERSIONINFO header
            ushort wLength = reader.ReadUInt16(); // total length
            ushort wValueLength = reader.ReadUInt16();
            ushort wType = reader.ReadUInt16();
            string szKey = ReadUnicodeString(reader); // should be "VS_VERSION_INFO"
            Align32(reader); // pad to 32-bit

            // VS_FIXEDFILEINFO
            if (wValueLength >= 52)
            {
                long fixedInfoStart = reader.BaseStream.Position;
                uint signature = reader.ReadUInt32();
                if (signature == 0xFEEF04BD)
                {
                    reader.ReadBytes(4); // strucVersion
                    // dwFileVersionMS, dwFileVersionLS
                    uint fvMS = reader.ReadUInt32();
                    uint fvLS = reader.ReadUInt32();
                    fileVersion = $"{fvMS >> 16}.{fvMS & 0xFFFF}.{fvLS >> 16}.{fvLS & 0xFFFF}";

                    // dwProductVersionMS, dwProductVersionLS
                    uint pvMS = reader.ReadUInt32();
                    uint pvLS = reader.ReadUInt32();
                    productVersion = $"{pvMS >> 16}.{pvMS & 0xFFFF}.{pvLS >> 16}.{pvLS & 0xFFFF}";

                    // 剩余字段跳过
                }
                reader.BaseStream.Seek(fixedInfoStart + 52, SeekOrigin.Begin);
                Align32(reader);
            }

            // 子块：StringFileInfo, VarFileInfo
            long endPos = startPos + wLength;
            while (reader.BaseStream.Position < endPos - 4)
            {
                long childStart = reader.BaseStream.Position;
                ushort childLength = reader.ReadUInt16();
                if (childLength < 6) break;
                ushort childValueLen = reader.ReadUInt16();
                ushort childType = reader.ReadUInt16();
                string childKey = ReadUnicodeString(reader);
                Align32(reader);

                if (childKey == "StringFileInfo")
                {
                    // 遍历 StringTable
                    long sfiEnd = childStart + childLength;
                    while (reader.BaseStream.Position < sfiEnd - 4)
                    {
                        long tableStart = reader.BaseStream.Position;
                        ushort tableLength = reader.ReadUInt16();
                        if (tableLength < 6) break;
                        reader.ReadUInt16(); // wValueLength
                        reader.ReadUInt16(); // wType
                        ReadUnicodeString(reader); // language code (e.g. "080904b0")
                        Align32(reader);

                        // 遍历 String entries
                        long tableEnd = tableStart + tableLength;
                        while (reader.BaseStream.Position < tableEnd - 4)
                        {
                            long strStart = reader.BaseStream.Position;
                            ushort strLength = reader.ReadUInt16();
                            if (strLength < 6) break;
                            reader.ReadUInt16(); // wValueLength
                            reader.ReadUInt16(); // wType
                            string strKey = ReadUnicodeString(reader);
                            Align32(reader);
                            string strValue = ReadUnicodeString(reader);
                            Align32(reader);

                            // 映射到字段
                            switch (strKey)
                            {
                                case "CompanyName": companyName = strValue; break;
                                case "ProductName": productName = strValue; break;
                                case "FileVersion": fileVersion ??= strValue; break;
                                case "ProductVersion": productVersion ??= strValue; break;
                                case "FileDescription": fileDescription = strValue; break;
                                case "LegalCopyright": legalCopyright = strValue; break;
                            }

                            reader.BaseStream.Seek(strStart + strLength, SeekOrigin.Begin);
                        }
                        reader.BaseStream.Seek(tableStart + tableLength, SeekOrigin.Begin);
                    }
                }
                // VarFileInfo 跳过（不需要）
                reader.BaseStream.Seek(childStart + childLength, SeekOrigin.Begin);
            }
        }
        catch
        {
            // 解析版本信息中的部分失败不中断
        }
    }

    /// <summary>
    /// 读取以 null 结尾的 Unicode (UTF-16) 字符串。
    /// </summary>
    private static string ReadUnicodeString(BinaryReader reader)
    {
        var sb = new StringBuilder();
        while (true)
        {
            char c = (char)reader.ReadUInt16();
            if (c == '\0') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 对齐到 32 位边界（填充到 4 的倍数）。
    /// </summary>
    private static void Align32(BinaryReader reader)
    {
        long offset = reader.BaseStream.Position;
        long aligned = (offset + 3) & ~3;
        if (aligned > offset)
            reader.BaseStream.Seek(aligned, SeekOrigin.Begin);
    }
}
