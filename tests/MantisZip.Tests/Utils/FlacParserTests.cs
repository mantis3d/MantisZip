using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// FlacParser 单元测试：验证 STREAMINFO 解析与 PICTURE(block type 6) 封面提取。
/// FLAC 夹具以程序化字节序列构造，不检查入二进制 fixture 文件。
/// </summary>
public class FlacParserTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var d in _tempDirs.Where(Directory.Exists))
            try { Directory.Delete(d, true); } catch { }
    }

    // ===== 覆盖率/采样率等 STREAMINFO 字段解析 =====

    /// <summary>
    /// (a) STREAMINFO + PICTURE 块 → CoverArtData 等于嵌入图片的精确字节，
    /// 且 duration/sampleRate/channels/bitDepth 仍解析正确。
    /// </summary>
    [Fact]
    public void Parse_FlacWithPictureBlock_ExtractsCoverArtAndStreamInfo()
    {
        // 模拟 JPEG 图片数据（封面原始字节断言必须逐字节相等）
        byte[] imageBytes =
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C,
            0xFF, 0xD9
        };

        string path = WriteFlac(
            BuildMetadataBlock(blockType: 0, isLast: false,
                BuildStreamInfo(sampleRate: 44100, channels: 2, bitDepth: 16, totalSamples: 44100)),
            BuildMetadataBlock(blockType: 6, isLast: true,
                BuildPictureBlock(imageBytes)));

        var info = FlacParser.Parse(path);

        Assert.NotNull(info);
        // STREAMINFO 解析行为不变
        Assert.Equal(FileFormat.Flac, info!.Format);
        Assert.Equal(44100, info.SampleRate);
        Assert.Equal(2, info.Channels);
        Assert.Equal(16, info.BitDepth);
        Assert.NotNull(info.Duration);
        Assert.Equal(1.0, info.Duration!.Value.TotalSeconds, 3);
        // PICTURE 块封面提取
        Assert.NotNull(info.CoverArtData);
        Assert.Equal(imageBytes, info.CoverArtData);
    }

    /// <summary>
    /// (b) 仅 STREAMINFO 块 → CoverArtData 为 null。
    /// </summary>
    [Fact]
    public void Parse_FlacWithoutPictureBlock_CoverArtIsNull()
    {
        string path = WriteFlac(
            BuildMetadataBlock(blockType: 0, isLast: true,
                BuildStreamInfo(sampleRate: 44100, channels: 2, bitDepth: 16, totalSamples: 88200)));

        var info = FlacParser.Parse(path);

        Assert.NotNull(info);
        Assert.Equal(44100, info!.SampleRate);
        Assert.Null(info.CoverArtData);
    }

    /// <summary>
    /// (c) PICTURE 块声明的数据长度远超块实际载荷 → CoverArtData 为 null，
    /// 但 Parse 仍返回有效信息（sampleRate 正确）——封面失败不得让整体解析失败。
    /// </summary>
    [Fact]
    public void Parse_PictureBlockWithOversizedDataLength_DoesNotFailParse()
    {
        string path = WriteFlac(
            BuildMetadataBlock(blockType: 0, isLast: false,
                BuildStreamInfo(sampleRate: 48000, channels: 2, bitDepth: 24, totalSamples: 96000)),
            BuildMetadataBlock(blockType: 6, isLast: true,
                BuildTruncatedPictureBlock(declaredDataLength: 1000)));

        var info = FlacParser.Parse(path);

        Assert.NotNull(info);
        Assert.Equal(48000, info!.SampleRate);
        Assert.Null(info.CoverArtData);
    }

    /// <summary>
    /// 非 fLaC 魔数 → 返回 null（既有语义，防止重构破坏）。
    /// </summary>
    [Fact]
    public void Parse_NonFlacMagic_ReturnsNull()
    {
        var path = Path.Combine(WriteTempDir(), "notflac.flac");
        File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });

        Assert.Null(FlacParser.Parse(path));
    }

    /// <summary>
    /// 首个块（STREAMINFO）载荷不足 34 字节 → 返回 null（既有有效性语义）。
    /// </summary>
    [Fact]
    public void Parse_ShortStreamInfoBlock_ReturnsNull()
    {
        string path = WriteFlac(
            BuildMetadataBlock(blockType: 0, isLast: true, new byte[10]));

        Assert.Null(FlacParser.Parse(path));
    }

    // ===== 夹具构造辅助 =====

    /// <summary>
    /// 构造 FLAC 元数据块：1 字节头（bit7 = isLast，bits6..0 = block type）
    /// + 3 字节大端载荷长度 + 载荷。
    /// </summary>
    private static byte[] BuildMetadataBlock(byte blockType, bool isLast, byte[] payload)
    {
        var block = new byte[4 + payload.Length];
        block[0] = (byte)((isLast ? 0x80 : 0x00) | (blockType & 0x7F));
        block[1] = (byte)((payload.Length >> 16) & 0xFF);
        block[2] = (byte)((payload.Length >> 8) & 0xFF);
        block[3] = (byte)(payload.Length & 0xFF);
        Buffer.BlockCopy(payload, 0, block, 4, payload.Length);
        return block;
    }

    /// <summary>
    /// 构造 34 字节 STREAMINFO 载荷（type 0）。
    /// 前 10 字节为 min/max block/frame size，第 10 字节起为 8 字节位打包字段：
    /// 20-bit 采样率 + 3-bit(声道数-1) + 5-bit(位深-1) + 36-bit 总样本数，
    /// 末尾 16 字节为 MD5 签名（全零即可）。
    /// </summary>
    private static byte[] BuildStreamInfo(int sampleRate, int channels, int bitDepth, long totalSamples)
    {
        var payload = new byte[34];
        payload[0] = 0x10; payload[1] = 0x00; // minBlockSize = 4096
        payload[2] = 0x10; payload[3] = 0x00; // maxBlockSize = 4096
        // minFrameSize (3B) / maxFrameSize (3B)：保持 0

        ulong packed = ((ulong)sampleRate << 44)
                     | ((ulong)(channels - 1) << 41)
                     | ((ulong)(bitDepth - 1) << 36)
                     | (ulong)totalSamples;
        for (int i = 0; i < 8; i++) // 大端写入 8 位打包字段
            payload[10 + i] = (byte)(packed >> (56 - 8 * i));
        return payload;
    }

    /// <summary>
    /// 构造合法 PICTURE 块（type 6）载荷：
    /// 4B 图片类型 | 4B MIME 长度 + MIME | 4B 描述长度 + 描述 |
    /// 4B 宽 | 4B 高 | 4B 色深 | 4B 索引色数 | 4B 数据长度 + 图片数据。
    /// </summary>
    private static byte[] BuildPictureBlock(byte[] imageData, uint pictureType = 3, string mime = "image/jpeg")
    {
        var payload = new List<byte>(64 + imageData.Length);
        AppendU32Be(payload, pictureType);          // 图片类型：3 = 封面正面
        AppendString(payload, mime);                // MIME 类型
        AppendU32Be(payload, 0);                    // 描述长度：空描述
        AppendU32Be(payload, 0);                    // 宽
        AppendU32Be(payload, 0);                    // 高
        AppendU32Be(payload, 24);                   // 色深
        AppendU32Be(payload, 0);                    // 索引色数（非索引图为 0）
        AppendU32Be(payload, (uint)imageData.Length); // 数据长度
        payload.AddRange(imageData);                // 图片数据
        return payload.ToArray();
    }

    /// <summary>
    /// 构造损坏的 PICTURE 块载荷：头字段完整但声明的数据长度远超块实际载荷，
    /// 且不写入任何图片数据（模拟截断/恶意长度字段）。
    /// </summary>
    private static byte[] BuildTruncatedPictureBlock(uint declaredDataLength)
    {
        var payload = new List<byte>();
        AppendU32Be(payload, 3);                      // 图片类型
        AppendString(payload, "image/jpeg");          // MIME 类型
        AppendU32Be(payload, 0);                      // 描述长度：空描述
        AppendU32Be(payload, 0);                      // 宽
        AppendU32Be(payload, 0);                      // 高
        AppendU32Be(payload, 24);                     // 色深
        AppendU32Be(payload, 0);                      // 索引色数
        AppendU32Be(payload, declaredDataLength);     // 数据长度：远超实际载荷
        // 故意不追加图片数据
        return payload.ToArray();
    }

    private static void AppendU32Be(List<byte> list, uint value)
    {
        list.Add((byte)(value >> 24));
        list.Add((byte)(value >> 16));
        list.Add((byte)(value >> 8));
        list.Add((byte)value);
    }

    private static void AppendString(List<byte> list, string value)
    {
        AppendU32Be(list, (uint)value.Length);
        list.AddRange(System.Text.Encoding.ASCII.GetBytes(value));
    }

    /// <summary>
    /// 将 "fLaC" 魔数 + 各元数据块写入临时文件并返回路径。
    /// </summary>
    private string WriteFlac(params byte[][] blocks)
    {
        string dir = WriteTempDir();
        string path = Path.Combine(dir, "sample.flac");
        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes("fLaC"));
        foreach (var block in blocks)
            ms.Write(block);
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private string WriteTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
