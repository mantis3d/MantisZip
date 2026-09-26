using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 验证 APNG 魔数检测：通过扫描 acTL chunk 区分 APNG 与静态 PNG。
/// </summary>
public class FileFormatDetectorApngTests
{
    /// <summary>
    /// 最小 APNG：PNG 签名 + IHDR + acTL + IEND
    /// acTL chunk: length=8, type=acTL, data=numFrames=1, numPlays=0
    /// </summary>
    [Fact]
    public void Detect_Apng_ReturnsApng()
    {
        var apngHead = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk (length=13, type=IHDR)
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // width=1, height=1
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, // bit depth=8, color type=2 (RGB), compression=0, filter=0, interlace=0, CRC
            0x00, 0x00, 0x00, 0x08, 0x61, 0x63, 0x54, 0x4C, // acTL chunk (length=8, type=acTL)
            0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x00, // numFrames=10, numPlays=0
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // CRC (placeholder)
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, // IEND chunk (length=0, type=IEND)
            0xAE, 0x42, 0x60, 0x82 // IEND CRC
        };

        var result = FileFormatDetector.Detect(apngHead, apngHead.Length);
        Assert.Equal(FileFormat.Apng, result);
    }

    /// <summary>
    /// 静态 PNG：PNG 签名 + IHDR + IEND（无 acTL chunk）
    /// </summary>
    [Fact]
    public void Detect_StaticPng_ReturnsPng()
    {
        var pngHead = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk (length=13, type=IHDR)
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // width=1, height=1
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, // bit depth=8, color type=2 (RGB), compression=0, filter=0, interlace=0, CRC
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, // IEND chunk (length=0, type=IEND)
            0xAE, 0x42, 0x60, 0x82 // IEND CRC
        };

        var result = FileFormatDetector.Detect(pngHead, pngHead.Length);
        Assert.Equal(FileFormat.Png, result);
    }

/// <summary>
    /// APNG 带 PLTE chunk：PNG 签名 + IHDR + PLTE + acTL + IEND
    /// 验证 acTL 在 PLTE 之后也能被正确检测
    /// </summary>
    [Fact]
    public void Detect_Apng_WithPlte_BeforeActl_ReturnsApng()
    {
        // 精确构造的 APNG 字节流，确保 chunk 对齐正确
        // Offset 0-7: PNG signature
        // Offset 8-32: IHDR chunk (length=13, type=IHDR, 13 data + 4 CRC = 25 bytes total)
        // Offset 33-47: PLTE chunk (length=3, type=PLTE, 3 data + 4 CRC = 15 bytes total)
        // Offset 48-67: acTL chunk (length=8, type=acTL, 8 data + 4 CRC = 20 bytes total)
        // Offset 68-79: IEND chunk (length=0, type=IEND, 0 data + 4 CRC = 12 bytes total)
        var apngHead = new byte[]
        {
            // PNG signature (8 bytes)
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            // IHDR chunk: length=13 (4 bytes), type=IHDR (4 bytes)
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            // IHDR data (13 bytes): width=1, height=1, bit_depth=8, color_type=3, compression=0, filter=0, interlace=0
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x03, 0x00, 0x00, 0x00,
            // IHDR CRC (4 bytes, placeholder)
            0x00, 0x00, 0x00, 0x00,
            // PLTE chunk: length=3 (4 bytes), type=PLTE (4 bytes)
            0x00, 0x00, 0x00, 0x03, 0x50, 0x4C, 0x54, 0x45,
            // PLTE data (3 bytes): 1 palette entry = red (FF 00 00)
            0xFF, 0x00, 0x00,
            // PLTE CRC (4 bytes, placeholder)
            0x00, 0x00, 0x00, 0x00,
            // acTL chunk: length=8 (4 bytes), type=acTL (4 bytes)
            0x00, 0x00, 0x00, 0x08, 0x61, 0x63, 0x54, 0x4C,
            // acTL data (8 bytes): numFrames=5, numPlays=0
            0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00,
            // acTL CRC (4 bytes, placeholder)
            0x00, 0x00, 0x00, 0x00,
            // IEND chunk: length=0 (4 bytes), type=IEND (4 bytes)
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44,
            // IEND CRC (4 bytes)
            0xAE, 0x42, 0x60, 0x82
        };

        var result = FileFormatDetector.Detect(apngHead, apngHead.Length);
        Assert.Equal(FileFormat.Apng, result);
    }
}