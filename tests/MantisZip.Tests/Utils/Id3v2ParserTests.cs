using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// Id3v2Parser 单元测试：锁定 MP3 ID3v2 APIC 帧封面提取与基础标签解析。
/// MP3 夹具在测试内程序化构造（ID3v2.3 头 + TIT2 帧 + APIC 帧 + 最小 MPEG 帧）。
/// </summary>
public class Id3v2ParserTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var d in _tempDirs.Where(Directory.Exists))
            try { Directory.Delete(d, true); } catch { }
    }

    /// <summary>
    /// ID3v2.3 头 + TIT2 + APIC 帧 → CoverArtData 等于嵌入图片的精确字节，
    /// 且 Title 正确解析。
    /// </summary>
    [Fact]
    public void Parse_Mp3WithApicFrame_ExtractsCoverArtAndTitle()
    {
        // 模拟 PNG 图片数据（封面原始字节断言必须逐字节相等）
        byte[] imageBytes =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
            0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, // 样本数据
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, // IEND chunk
            0xAE, 0x42, 0x60, 0x82
        };

        string path = WriteMp3(
            BuildFrame("TIT2", WithEncoding("Test Title")),
            BuildFrame("APIC", BuildApicPayload(imageBytes)));

        var info = Id3v2Parser.Parse(path);

        Assert.NotNull(info);
        Assert.Equal(FileFormat.Mp3, info!.Format);
        Assert.Equal("Test Title", info.Title);
        Assert.NotNull(info.CoverArtData);
        Assert.Equal(imageBytes, info.CoverArtData);
    }

    // ===== 夹具构造辅助 =====

    /// <summary>
    /// 构造 ID3v2 文本帧数据：1 字节编码标识（0 = ISO-8859-1）+ 文本。
    /// </summary>
    private static byte[] WithEncoding(string text)
    {
        byte[] textBytes = System.Text.Encoding.Latin1.GetBytes(text);
        var data = new byte[1 + textBytes.Length];
        data[0] = 0x00;
        Buffer.BlockCopy(textBytes, 0, data, 1, textBytes.Length);
        return data;
    }

    /// <summary>
    /// 构造 APIC 帧数据：编码(1) + MIME(以 null 结尾) + 图片类型(1) + 描述(单 null 结尾) + 图片数据。
    /// </summary>
    private static byte[] BuildApicPayload(byte[] imageData)
    {
        byte[] mimeBytes = System.Text.Encoding.ASCII.GetBytes("image/png");
        // 布局：编码(1) + MIME(10) + MIME null(1) + 图片类型(1) + 描述 null(1) + 图片数据
        var data = new byte[1 + mimeBytes.Length + 1 + 1 + 1 + imageData.Length];
        int offset = 0;
        data[offset++] = 0x00; // 编码：ISO-8859-1
        Buffer.BlockCopy(mimeBytes, 0, data, offset, mimeBytes.Length);
        offset += mimeBytes.Length;
        data[offset++] = 0x00; // MIME null 结尾
        data[offset++] = 0x03; // 图片类型：封面正面
        data[offset++] = 0x00; // 空描述（单 null 结尾）
        Buffer.BlockCopy(imageData, 0, data, offset, imageData.Length);
        return data;
    }

    /// <summary>
    /// 构造 ID3v2.3 帧：帧 ID(4) + 帧大小(4, 大端非 syncsafe) + 帧标志(2, 全零) + 数据。
    /// </summary>
    private static byte[] BuildFrame(string frameId, byte[] frameData)
    {
        var frame = new byte[10 + frameData.Length];
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes(frameId), 0, frame, 0, 4);
        frame[4] = (byte)((frameData.Length >> 24) & 0xFF);
        frame[5] = (byte)((frameData.Length >> 16) & 0xFF);
        frame[6] = (byte)((frameData.Length >> 8) & 0xFF);
        frame[7] = (byte)(frameData.Length & 0xFF);
        frame[8] = 0x00; // flags: status
        frame[9] = 0x00; // flags: format
        Buffer.BlockCopy(frameData, 0, frame, 10, frameData.Length);
        return frame;
    }

    /// <summary>
    /// 将 ID3v2.3 头（10 字节，syncsafe 大小）+ 各帧 + 最小 MPEG 帧写入临时文件并返回路径。
    /// </summary>
    private string WriteMp3(params byte[][] frames)
    {
        string dir = WriteTempDir();
        string path = Path.Combine(dir, "sample.mp3");
        using var ms = new MemoryStream();

        // ── ID3v2.3 头（帧数据尺寸为 syncsafe 编码）──
        int tagDataSize = frames.Sum(f => f.Length);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("ID3"));
        ms.WriteByte(0x03); // major version
        ms.WriteByte(0x00); // minor version
        ms.WriteByte(0x00); // flags
        ms.WriteByte((byte)((tagDataSize >> 21) & 0x7F));
        ms.WriteByte((byte)((tagDataSize >> 14) & 0x7F));
        ms.WriteByte((byte)((tagDataSize >> 7) & 0x7F));
        ms.WriteByte((byte)(tagDataSize & 0x7F));

        foreach (var frame in frames)
            ms.Write(frame);

        // ── 最小 MPEG-1 Layer III 帧头（0xFFFB9000：128kbps/44100Hz/立体声）+ 零填充 ──
        ms.Write(new byte[] { 0xFF, 0xFB, 0x90, 0x00 });
        ms.Write(new byte[64]);

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
