using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// <see cref="PeParser"/> 头部字段与版本资源解析（issue #74 回归）。
/// 全部用内存构造的最小 PE，不依赖本机文件。
/// </summary>
public sealed class PeParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "MantisZipPeParserTests", Guid.NewGuid().ToString("N"));

    public PeParserTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 尽力清理
        }
    }

    // ── 头部：PE32+ 的 Subsystem / NumberOfRvaAndSizes 偏移（此前整体错 4 字节） ──

    [Fact]
    public void Parse_Pe32Plus_ReadsSubsystemFromOffset68()
    {
        var info = PeParser.Parse(Write("pe32plus.exe", BuildPe(isPlus: true, subsystem: 2, resources: [])));

        Assert.NotNull(info);
        Assert.Equal("x64", info!.Architecture);
        Assert.Equal("GUI", info.Subsystem);
    }

    [Fact]
    public void Parse_Pe32Plus_CuiSubsystem()
    {
        var info = PeParser.Parse(Write("pe32plus-cui.exe", BuildPe(isPlus: true, subsystem: 3, resources: [])));

        Assert.NotNull(info);
        Assert.Equal("CUI", info!.Subsystem);
    }

    [Fact]
    public void Parse_Pe32_ReadsSubsystemFromOffset68()
    {
        var info = PeParser.Parse(Write("pe32.exe", BuildPe(isPlus: false, subsystem: 3, resources: [])));

        Assert.NotNull(info);
        Assert.Equal("x86", info!.Architecture);
        Assert.Equal("CUI", info.Subsystem);
    }

    [Fact]
    public void Parse_NonPeFile_ReturnsNull()
    {
        var path = Path.Combine(_dir, "readme.txt");
        File.WriteAllText(path, "not a PE file");

        Assert.Null(PeParser.Parse(path));
    }

    // ── 版本资源：条目 wLength 不是 4 的倍数时，后续条目不能被丢掉（issue #74 产品名称缺失） ──

    [Fact]
    public void Parse_VersionEntryLengthNotDwordAligned_LaterKeysStillParsed()
    {
        // 复现 7z.exe 的布局：CompanyName 条目 wLength=50（实际占 52 字节），
        // 旧实现直接跳到 start+50 落在填充字节上，把后面的 ProductName 一起丢掉。
        byte[] version = BuildVersionResource();
        byte[] pe = BuildPeWithVersion(version);

        var info = PeParser.Parse(Write("version.exe", pe));

        Assert.NotNull(info);
        Assert.Equal("Contoso2", info!.CompanyName);
        Assert.Equal("Widget", info.ProductName);
        Assert.Equal("1.2.3.4", info.FileVersion);
        Assert.Equal("5.6.7.8", info.ProductVersion);
    }

    [Fact]
    public void Parse_VersionResource_ArchitectureStillReported()
    {
        byte[] pe = BuildPeWithVersion(BuildVersionResource());

        var info = PeParser.Parse(Write("version-arch.exe", pe));

        Assert.NotNull(info);
        Assert.Equal("x64", info!.Architecture);
        Assert.Equal("GUI", info.Subsystem);
    }

    // ── 版本资源：补的四个字段（原始文件名 / 语言 / 著作权；文件描述走 AdditionalInfo） ──

    [Fact]
    public void Parse_VersionResource_ReadsExtendedStringFields()
    {
        byte[] pe = BuildPeWithVersion(BuildVersionResource());

        var info = PeParser.Parse(Write("version-extended.exe", pe));

        Assert.NotNull(info);
        // 文件描述（Windows 属性里的「檔案描述」）
        Assert.Equal("Widget application", info!.AdditionalInfo);
        // 原始文件名（Windows 属性里的「原始檔名」）
        Assert.Equal("widget.exe", info.OriginalFilename);
        // 著作权（Windows 属性里的「著作權」）
        Assert.Equal("© 2026 Contoso", info.LegalCopyright);
    }

    [Fact]
    public void Parse_VersionResource_LanguageDerivedFromResourceLeafId()
    {
        // 资源目录叶子 ID = 0x0409（英文-美国），语言名应由该 ID 推断
        byte[] pe = BuildPeWithVersion(BuildVersionResource());

        var info = PeParser.Parse(Write("version-lang.exe", pe));

        Assert.NotNull(info);
        Assert.Equal(
            System.Globalization.CultureInfo.GetCultureInfo(0x0409).DisplayName,
            info!.Language);
    }

    // ── 夹具构造 ──

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static ushort Machine(bool isPlus) => isPlus ? (ushort)0x8664 : (ushort)0x014C;

    private static ushort OptionalHeaderSize(bool isPlus) => isPlus ? (ushort)0xF0 : (ushort)0xE0;

    /// <summary>
    /// 最小 PE：DOS 头 + PE 头 + 可选头 + 节表（可为空）。0x400 字节对齐的裸文件。
    /// </summary>
    private static byte[] BuildPe(bool isPlus, ushort subsystem, (uint rva, uint size)[] resources)
    {
        const int peOffset = 0x80;
        int optSize = OptionalHeaderSize(isPlus);
        int sectionTable = peOffset + 24 + optSize;
        int fileSize = 0x800;
        var bytes = new byte[fileSize];

        // DOS 头
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        WriteU32(bytes, 0x3C, peOffset);

        // PE 签名 + COFF 头
        WriteU32(bytes, peOffset, 0x00004550);
        WriteU16(bytes, peOffset + 4, Machine(isPlus));
        WriteU16(bytes, peOffset + 6, (ushort)resources.Length);
        WriteU16(bytes, peOffset + 20, (ushort)optSize);      // SizeOfOptionalHeader
        WriteU16(bytes, peOffset + 22, 0x0022);               // Characteristics

        // 可选头
        int opt = peOffset + 24;
        WriteU16(bytes, opt, isPlus ? (ushort)0x20B : (ushort)0x10B);
        WriteU16(bytes, opt + 68, subsystem);                 // Subsystem：两种格式都在 +68
        WriteU32(bytes, opt + (isPlus ? 108 : 92), 16);       // NumberOfRvaAndSizes
        if (resources.Length > 0)
        {
            // DataDirectory[2] = 资源目录
            int dirBase = opt + (isPlus ? 112 : 96);
            WriteU32(bytes, dirBase + 2 * 8, resources[0].rva);
            WriteU32(bytes, dirBase + 2 * 8 + 4, resources[0].size);
        }

        // 节表（资源节固定放在文件 0x400）
        for (int i = 0; i < resources.Length; i++)
        {
            int sec = sectionTable + i * 40;
            bytes[sec] = (byte)'.';
            bytes[sec + 1] = (byte)'r';
            bytes[sec + 2] = (byte)'s';
            bytes[sec + 3] = (byte)'r';
            bytes[sec + 4] = (byte)'c';
            WriteU32(bytes, sec + 8, resources[i].size);      // VirtualSize
            WriteU32(bytes, sec + 12, resources[i].rva);      // VirtualAddress
            WriteU32(bytes, sec + 16, 0x400);                 // SizeOfRawData
            WriteU32(bytes, sec + 20, 0x400);                 // PointerToRawData
        }

        return bytes;
    }

    /// <summary>
    /// 带 RT_VERSION 资源的 PE32+：资源目录在 RVA 0x1000 / 文件 0x400，版本数据在 RVA 0x1100 / 文件 0x500。
    /// </summary>
    private static byte[] BuildPeWithVersion(byte[] versionData)
    {
        var bytes = BuildPe(isPlus: true, subsystem: 2, resources: [(0x1000, 0x400)]);
        const int rsrc = 0x400;
        const int versionOffset = 0x500;

        // 根目录 → 类型目录（RT_VERSION = 16）
        WriteDir(bytes, rsrc + 0x00, 16, 0x80000018);
        // 类型目录 → 名称目录（ID = 1）
        WriteDir(bytes, rsrc + 0x18, 1, 0x80000030);
        // 名称目录 → 语言目录（0x409，数据条目不再带高位）
        WriteDir(bytes, rsrc + 0x30, 0x409, 0x48);

        // IMAGE_RESOURCE_DATA_ENTRY
        WriteU32(bytes, rsrc + 0x48, 0x1100);                       // DataRVA
        WriteU32(bytes, rsrc + 0x4C, (uint)versionData.Length);     // Size
        WriteU32(bytes, rsrc + 0x50, 0);                            // CodePage
        WriteU32(bytes, rsrc + 0x54, 0);                            // Reserved

        versionData.CopyTo(bytes, versionOffset);
        return bytes;
    }

    /// <summary>写一个只有 1 个 ID 条目的资源目录。</summary>
    private static void WriteDir(byte[] bytes, int offset, uint id, uint target)
    {
        // 目录头 16 字节（Characteristics/TimeDateStamp/Version + Named + ID 计数）全 0，只填 ID 计数
        WriteU16(bytes, offset + 12, 0);   // NumberOfNamedEntries
        WriteU16(bytes, offset + 14, 1);   // NumberOfIdEntries
        WriteU32(bytes, offset + 16, id);
        WriteU32(bytes, offset + 20, target);
    }

    /// <summary>
    /// 构造 VS_VERSIONINFO：CompanyName 的 wLength 故意比实际占用少 2 字节
    /// （真实世界如 7z.exe 的 38/40 组合），后面的 ProductName 必须仍能解析到。
    /// </summary>
    private static byte[] BuildVersionResource()
    {
        var buf = new List<byte>();

        // ── 根块 ──
        int rootStart = buf.Count;
        AddU16(buf, 0);            // wLength（收尾回填）
        AddU16(buf, 52);           // wValueLength = sizeof(VS_FIXEDFILEINFO)
        AddU16(buf, 0);            // wType
        AddUtf16Z(buf, "VS_VERSION_INFO");
        Align4(buf);

        var fixedInfo = new byte[52];
        WriteU32(fixedInfo, 0, 0xFEEF04BD);   // dwSignature
        WriteU32(fixedInfo, 4, 0x00010000);   // dwStrucVersion
        WriteU32(fixedInfo, 8, 0x00010002);   // FileVersionMS → 1.2
        WriteU32(fixedInfo, 12, 0x00030004);  // FileVersionLS → 3.4
        WriteU32(fixedInfo, 16, 0x00050006);  // ProductVersionMS → 5.6
        WriteU32(fixedInfo, 20, 0x00070008);  // ProductVersionLS → 7.8
        WriteU32(fixedInfo, 24, 0x3F);        // dwFileFlagsMask
        WriteU32(fixedInfo, 32, 0x00040004);  // dwFileOS = VOS_NT_WINDOWS32
        WriteU32(fixedInfo, 36, 1);           // dwFileType = VFT_APP
        buf.AddRange(fixedInfo);

        // ── StringFileInfo ──
        int sfiStart = buf.Count;
        AddU16(buf, 0);
        AddU16(buf, 0);
        AddU16(buf, 1);
        AddUtf16Z(buf, "StringFileInfo");
        Align4(buf);

        // ── StringTable ──
        int tableStart = buf.Count;
        AddU16(buf, 0);
        AddU16(buf, 0);
        AddU16(buf, 1);
        AddUtf16Z(buf, "040904B0");
        Align4(buf);

        // ── CompanyName：wLength 不含末尾对齐填充（偶数个字符 → 长度 ≡2 mod 4） ──
        int companyStart = buf.Count;
        AddU16(buf, 0);
        AddU16(buf, 0);
        AddU16(buf, 1);
        AddUtf16Z(buf, "CompanyName");
        Align4(buf);
        AddUtf16Z(buf, "Contoso2");
        int beforePad = buf.Count;
        Align4(buf);
        WriteU16(buf, companyStart, (ushort)(beforePad - companyStart));

        // ── ProductName：排在 CompanyName 之后，旧实现会因错位而读不到 ──
        int productStart = buf.Count;
        AddU16(buf, 0);
        AddU16(buf, 0);
        AddU16(buf, 1);
        AddUtf16Z(buf, "ProductName");
        Align4(buf);
        AddUtf16Z(buf, "Widget");
        WriteU16(buf, productStart, (ushort)(buf.Count - productStart));

        // ── 文件描述 / 原始文件名 / 著作权：issue #74 补充的三个字符串字段 ──
        AddStringEntry(buf, "FileDescription", "Widget application");
        AddStringEntry(buf, "OriginalFilename", "widget.exe");
        AddStringEntry(buf, "LegalCopyright", "© 2026 Contoso");

        WriteU16(buf, tableStart, (ushort)(buf.Count - tableStart));
        WriteU16(buf, sfiStart, (ushort)(buf.Count - sfiStart));
        WriteU16(buf, rootStart, (ushort)(buf.Count - rootStart));

        return buf.ToArray();
    }

    /// <summary>
    /// 追加一条 String 条目（key → value）。入口先对齐，wLength 含末尾填充，
    /// 与真实编译器产物（wLength 不保证是 4 的倍数）混合，用来覆盖对齐逻辑。
    /// </summary>
    private static void AddStringEntry(List<byte> buf, string key, string value)
    {
        Align4(buf);
        int start = buf.Count;
        AddU16(buf, 0);            // wLength（收尾回填）
        AddU16(buf, 0);            // wValueLength
        AddU16(buf, 1);            // wType = 文本
        AddUtf16Z(buf, key);
        Align4(buf);
        AddUtf16Z(buf, value);
        Align4(buf);
        WriteU16(buf, start, (ushort)(buf.Count - start));
    }

    private static void AddU16(List<byte> buf, ushort value)
    {
        buf.Add((byte)(value & 0xFF));
        buf.Add((byte)(value >> 8));
    }

    private static void AddUtf16Z(List<byte> buf, string value)
    {
        foreach (char c in value)
        {
            buf.Add((byte)(c & 0xFF));
            buf.Add((byte)(c >> 8));
        }
        buf.Add(0);
        buf.Add(0);
    }

    /// <summary>按缓冲区绝对位置补齐到 4 字节边界（资源数据起点是 4 的倍数，等价于块内对齐）。</summary>
    private static void Align4(List<byte> buf)
    {
        while (buf.Count % 4 != 0)
            buf.Add(0);
    }

    private static void WriteU16(List<byte> buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteU16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteU32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}

