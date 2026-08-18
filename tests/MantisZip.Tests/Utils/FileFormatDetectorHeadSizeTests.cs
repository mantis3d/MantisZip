using System.Text;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 验证 PreviewHeadSize（格式检测头部字节数）对魔数检测结果的实际影响。
///
/// 结论依据：
/// - 绝大多数魔数位于文件头 2~12 字节内（PNG 8B、JPEG 3B、GIF 6B、PDF 5B、7z 6B、ZIP 4B 等）
/// - 少数格式需要读取较深的位置：
///   * ZIP 子类型（DOCX/XLSX/PPTX）需遍历首条目 Local Header 并读取 [Content_Types].xml 内容
///   * PE 需从偏移 0x3C 读 e_lfanew 再跳转到 PE 签名
///   * 文本启发式仅检查前 512 字节（LooksLikeText 硬性上限）
///
/// 因此 1~64KB 的 PreviewHeadSize 滑条范围对绝大多数格式无感知差异；
/// 它真正限制的是「最多读取多少字节」，属于 IO 资源保护参数。
/// </summary>
public class FileFormatDetectorHeadSizeTests
{
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, FileFormat.Png)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF }, FileFormat.Jpeg)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, FileFormat.Gif)]
    [InlineData(new byte[] { 0x42, 0x4D }, FileFormat.Bmp)]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00 }, FileFormat.Ico)]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, FileFormat.Pdf)]
    [InlineData(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }, FileFormat.SevenZip)]
    [InlineData(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 }, FileFormat.Rar)]
    [InlineData(new byte[] { 0x1F, 0x8B }, FileFormat.Gz)]
    [InlineData(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, FileFormat.Mkv)]
    [InlineData(new byte[] { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65 }, FileFormat.Sqlite)]
    public void Detect_MagicBytesWithinFirstBytes_ReturnsCorrectFormat(byte[] magic, FileFormat expected)
    {
        // 用魔数 + 100 字节填充模拟 headSize=magic.Length+100 的情况
        var head = new byte[magic.Length + 100];
        Array.Copy(magic, head, magic.Length);

        Assert.Equal(expected, FileFormatDetector.Detect(head, head.Length));
    }

    [Fact]
    public void Detect_HeadSizeShorterThanMagic_ReturnsUnknown()
    {
        // 只给 4 字节头部，PNG 魔数需要 8 字节 → 无法识别
        var pngMagic = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var shortHead = new byte[4];
        Array.Copy(pngMagic, shortHead, 4);

        Assert.Equal(FileFormat.Unknown, FileFormatDetector.Detect(shortHead, shortHead.Length));
    }

    /// <summary>
    /// ZIP 子类型检测需要读取首条目 Local Header + [Content_Types].xml 内容。
    /// 构造一个真实的最小 DOCX（ZIP 容器，首条目为 [Content_Types].xml），
    /// 验证：headSize 太小（只够魔数）→ 识别为普通 Zip；headSize 足够 → 识别为 Docx。
    /// </summary>
    [Fact]
    public void Detect_ZipSubtype_RequiresEnoughHeadSize()
    {
        // 构造最小 DOCX 字节流：ZIP Local Header + [Content_Types].xml（Store，无压缩）
        // [Content_Types].xml 内容含 wordprocessingml 标识 → 识别为 Docx
        var contentTypeXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" " +
            "ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>";

        var xmlBytes = Encoding.UTF8.GetBytes(contentTypeXml);
        // 真实 DOCX 结构中 [Content_Types].xml 是首个条目（ZIP 条目顺序即文件顺序）
        var zipBytes = BuildMinimalZip(
            ("[Content_Types].xml", xmlBytes),
            ("_rels/.rels", Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>")));

        // headSize 只够魔数（前 4 字节）→ 只能判断为普通 ZIP（无法细分 DOCX/XLSX）
        var tinyHead = new byte[4];
        Array.Copy(zipBytes, tinyHead, 4);
        var tinyResult = FileFormatDetector.Detect(tinyHead, tinyHead.Length);
        Assert.Equal(FileFormat.Zip, tinyResult);

        // headSize 不足 30+8 字节（Local Header 大小）→ DetectZipSubtype 内部 break，退回普通 Zip
        var smallHead = new byte[20];
        Array.Copy(zipBytes, smallHead, 20);
        var smallResult = FileFormatDetector.Detect(smallHead, smallHead.Length);
        Assert.Equal(FileFormat.Zip, smallResult);

        // headSize 覆盖首条目（Local Header 30B + 文件名 + 内容）→ 正确识别 DOCX
        var headSize = 30 + "[Content_Types].xml".Length + xmlBytes.Length;
        var fullHead = new byte[headSize];
        Array.Copy(zipBytes, fullHead, headSize);
        var fullResult = FileFormatDetector.Detect(fullHead, fullHead.Length);
        Assert.Equal(FileFormat.Docx, fullResult);
    }

    /// <summary>
    /// 文本启发式只检查前 512 字节（LooksLikeText 内部 Math.Min(length, 512)）。
    /// 因此超过 512 字节的 headSize 对文本分类结果无任何影响。
    /// </summary>
    [Fact]
    public void Detect_TextHeuristic_CappedAt512Bytes()
    {
        // 构造一个 2000 字节的纯文本，其中包含 JSON 特征
        var sb = new StringBuilder();
        sb.Append("{\"name\": \"test\", \"items\": [");
        for (int i = 0; i < 50; i++) sb.Append($"{{\"id\": {i}, \"value\": \"item number {i}\"}}, ");
        sb.Append("]}");
        while (sb.Length < 2000) sb.Append(" padding content to extend beyond 512 bytes. ");

        var textBytes = Encoding.UTF8.GetBytes(sb.ToString());

        // headSize = 512（启发式上限）与 headSize = 2000（完整）结果一致
        var head512 = new byte[512];
        Array.Copy(textBytes, head512, 512);
        var headFull = textBytes;

        Assert.Equal(
            FileFormatDetector.Detect(headFull, headFull.Length),
            FileFormatDetector.Detect(head512, head512.Length));
    }

    /// <summary>
    /// PE 检测需从 0x3C 读 e_lfanew，再跳转到 PE 签名（通常在文件头几百字节内）。
    /// headSize 覆盖 e_lfanew + PE 签名即可识别。
    /// </summary>
    [Fact]
    public void Detect_Pe_RequiresDosHeaderAndE_lfanew()
    {
        // 构造最小 PE 头：MZ 魔数 + e_lfanew(0x3C)=0x80 + PE\0\0 签名
        var head = new byte[0x84];
        head[0] = 0x4D; head[1] = 0x5A;              // "MZ"
        head[0x3C] = 0x80;                            // e_lfanew = 0x80
        head[0x80] = (byte)'P'; head[0x81] = (byte)'E'; // "PE\0\0"
        head[0x82] = 0; head[0x83] = 0;

        Assert.Equal(FileFormat.Pe, FileFormatDetector.Detect(head, head.Length));

        // headSize 不够读 e_lfanew（<0x40）→ 无法识别 PE
        var shortHead = new byte[0x3E];
        Array.Copy(head, shortHead, 0x3E);
        Assert.NotEqual(FileFormat.Pe, FileFormatDetector.Detect(shortHead, shortHead.Length));
    }

    /// <summary>
    /// 构造最小 ZIP 字节流（Store 存储），条目按给定顺序写入。
    /// </summary>
    private static byte[] BuildMinimalZip(params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        var offsets = new List<(int Offset, int Crc, int CompSize, int UncompSize)>();

        foreach (var (name, data) in entries)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            int localHeaderOffset = (int)ms.Position;

            // Local File Header
            writer.Write(0x04034B50);                    // signature "PK\x03\x04"
            writer.Write((ushort)20);                    // version needed
            writer.Write((ushort)0);                     // flags
            writer.Write((ushort)0);                     // method = Store
            writer.Write((ushort)0);                     // mod time
            writer.Write((ushort)0x21);                  // mod date
            writer.Write((uint)0);                       // crc32 (fake, not validated here)
            writer.Write((uint)data.Length);             // compressed size
            writer.Write((uint)data.Length);             // uncompressed size
            writer.Write((ushort)nameBytes.Length);      // name length
            writer.Write((ushort)0);                     // extra length
            writer.Write(nameBytes);
            writer.Write(data);

            offsets.Add((localHeaderOffset, 0, data.Length, data.Length));
        }

        int centralDirOffset = (int)ms.Position;
        foreach (var ((name, data), (offset, crc, compSize, uncompSize)) in entries.Zip(offsets))
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            writer.Write(0x02014B50);                    // central header signature "PK\x01\x02"
            writer.Write((ushort)20);                    // version made by
            writer.Write((ushort)20);                    // version needed
            writer.Write((ushort)0);                     // flags
            writer.Write((ushort)0);                     // method
            writer.Write((ushort)0);                     // mod time
            writer.Write((ushort)0x21);                  // mod date
            writer.Write((uint)crc);
            writer.Write((uint)compSize);
            writer.Write((uint)uncompSize);
            writer.Write((ushort)nameBytes.Length);
            writer.Write((ushort)0);                     // extra length
            writer.Write((ushort)0);                     // comment length
            writer.Write((ushort)0);                     // disk number
            writer.Write((ushort)0);                     // internal attrs
            writer.Write((uint)0);                       // external attrs
            writer.Write((uint)offset);
            writer.Write(nameBytes);
        }

        int centralDirSize = (int)ms.Position - centralDirOffset;
        writer.Write(0x06054B50);                        // EOCD signature "PK\x05\x06"
        writer.Write((ushort)0);                         // disk number
        writer.Write((ushort)0);                         // disk with central dir
        writer.Write((ushort)entries.Length);            // entries on this disk
        writer.Write((ushort)entries.Length);            // total entries
        writer.Write((uint)centralDirSize);
        writer.Write((uint)centralDirOffset);
        writer.Write((ushort)0);                         // comment length

        writer.Flush();
        return ms.ToArray();
    }
}
