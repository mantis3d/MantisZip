using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using MantisZip.UI.Avalonia.Services;
using SkiaSharp;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// issue #89 回归：带调色板的 DIB 帧 ICO（1/4/8bpp）必须能解出。
///
/// <para>
/// 成因：<see cref="IcoParser"/> 把 ICO 里的裸 DIB 重新包一层 BMP 文件头再交给 SkiaSharp，
/// 旧实现把 <c>bfOffBits</c> 写成 <c>14 + headerSize</c>，漏了调色板长度；
/// 于是 BMP 解码器把调色板前 N 字节当成像素读，带调色板的帧必然解不出 ⇒
/// <c>ExtractFrames</c> 返回空 ⇒ 预览面板走「不支持的格式」。
/// 样本 Test_01/Test_02.ico 均为 4bpp + 16 色调色板，100% 命中。
/// </para>
///
/// <para>
/// 这些用例不需要真实 Skia 也能构造字节，但解码本身要 Skia（<c>UseHeadlessDrawing = false</c>），
/// 故统一用 <c>[AvaloniaFact]</c>。
/// </para>
/// </summary>
public class IcoParserPaletteTests
{
    /// <summary>4bpp + 16 色调色板的 DIB 帧：修好 bfOffBits 后必须解出 32×32。</summary>
    [AvaloniaFact]
    public void ExtractFrames_FourBppPaletteDib_DecodesFrame()
    {
        var icoData = PackIcoWithDibFrame(CreateDib(32, 32, 4));

        var frames = IcoParser.ExtractFrames(icoData);

        Assert.Single(frames);
        Assert.Equal(32, frames[0].Width);
        Assert.Equal(32, frames[0].Height);
        Assert.Equal("32 × 32", frames[0].Label);
    }

    /// <summary>8bpp + 256 色调色板的 DIB 帧同样必须解出（另一个 bpp 分档）。</summary>
    [AvaloniaFact]
    public void ExtractFrames_EightBppPaletteDib_DecodesFrame()
    {
        var icoData = PackIcoWithDibFrame(CreateDib(24, 24, 8));

        var frames = IcoParser.ExtractFrames(icoData);

        Assert.Single(frames);
        Assert.Equal(24, frames[0].Width);
        Assert.Equal(24, frames[0].Height);
    }

    /// <summary>
    /// 像素级核对：解出的颜色必须是调色板里登记的颜色。
    /// 若 bfOffBits 仍漏 paletteSize，解出的要么是 null、要么是错位噪声，本断言都过不了。
    /// </summary>
    [AvaloniaFact]
    public void ExtractFrames_FourBppPaletteDib_UsesPaletteColors()
    {
        var icoData = PackIcoWithDibFrame(CreateDib(32, 32, 4));

        var frames = IcoParser.ExtractFrames(icoData);
        Assert.Single(frames);

        using var skia = ToSkia(frames[0].Bitmap);
        Assert.Equal(32, skia.Width);
        Assert.Equal(32, skia.Height);

        // DIB 是 bottom-up：文件里第一行对应图像最下一行。取左上角像素核对调色板索引 1 的颜色。
        var topLeft = skia.GetPixel(0, 0);
        Assert.Equal(ExpectedPaletteColor(1), (topLeft.Red, topLeft.Green, topLeft.Blue));
    }

    /// <summary>
    /// 1bpp 帧（2 色调色板）是调色板分档里最容易漏的一档，单独钉一次。
    /// </summary>
    [AvaloniaFact]
    public void ExtractFrames_OneBppPaletteDib_DecodesFrame()
    {
        var icoData = PackIcoWithDibFrame(CreateDib(32, 32, 1));

        var frames = IcoParser.ExtractFrames(icoData);

        Assert.Single(frames);
        Assert.Equal(32, frames[0].Width);
        Assert.Equal(32, frames[0].Height);
    }

    /// <summary>空/垃圾数据仍然只返回空列表（修复不能把「拒绝坏数据」弄丢）。</summary>
    [AvaloniaFact]
    public void ExtractFrames_GarbageBytes_ReturnsEmpty()
    {
        Assert.Empty(IcoParser.ExtractFrames([]));
        Assert.Empty(IcoParser.ExtractFrames(new byte[] { 0, 0, 1, 0, 1, 0 }));
    }

    /// <summary>调色板里索引 <paramref name="index"/> 的颜色（与 <see cref="CreateDib"/> 的填充规则一致）。</summary>
    private static (byte Red, byte Green, byte Blue) ExpectedPaletteColor(int index) =>
        ((byte)(index * 16 + 8), (byte)(index * 16 + 8), (byte)(index * 16 + 8));

    /// <summary>
    /// 构造一帧裸 DIB（BITMAPINFOHEADER + 调色板 + XOR 位 + AND 掩码），
    /// 布局与 issue #89 样本一致（<c>biHeight</c> = 高度 × 2）。
    /// </summary>
    private static byte[] CreateDib(int width, int height, short bpp)
    {
        var paletteCount = 1 << bpp;                 // 1→2、4→16、8→256
        var headerSize = 40;
        var paletteSize = paletteCount * 4;
        var xorRowSize = ((width * bpp + 31) / 32) * 4;
        var andRowSize = ((width + 31) / 32) * 4;
        var dib = new byte[headerSize + paletteSize + xorRowSize * height + andRowSize * height];

        // BITMAPINFOHEADER
        WriteInt32(dib, 0, headerSize);
        WriteInt32(dib, 4, width);
        WriteInt32(dib, 8, height * 2);              // ICO 规范：XOR + AND 合计
        WriteInt16(dib, 12, 1);                      // planes
        WriteInt16(dib, 14, bpp);
        WriteInt32(dib, 20, xorRowSize * height);    // biSizeImage
        WriteInt32(dib, 32, paletteCount);           // clrUsed

        // 调色板：BGRA 四元组，索引 i 的三通道都是 i*16+8（便于像素断言）
        for (var i = 0; i < paletteCount; i++)
        {
            var offset = headerSize + i * 4;
            var value = (byte)(i * 16 + 8);
            dib[offset] = value;         // B
            dib[offset + 1] = value;     // G
            dib[offset + 2] = value;     // R
            dib[offset + 3] = 0;         // reserved
        }

        // XOR 位：全部写索引 1（灰色），保证像素可预期
        var xorStart = headerSize + paletteSize;
        for (var y = 0; y < height; y++)
        {
            var rowStart = xorStart + y * xorRowSize;
            if (bpp == 1)
            {
                for (var x = 0; x < width; x += 8)
                    dib[rowStart + x / 8] = 0b0101_0101;
            }
            else if (bpp == 4)
            {
                for (var x = 0; x < width; x += 2)
                    dib[rowStart + x / 2] = 0x11;
            }
            else
            {
                for (var x = 0; x < width; x++)
                    dib[rowStart + x] = 1;
            }
        }

        // AND 掩码全 0 = 全不透明（不透明像素走不到 alpha=0 分支，颜色断言才有意义）
        return dib;
    }

    /// <summary>把一帧 DIB 打包成单帧 ICO（ICONDIR + 目录项 + 帧数据）。</summary>
    private static byte[] PackIcoWithDibFrame(byte[] dib)
    {
        var headerSize = 6 + 16;
        var ico = new byte[headerSize + dib.Length];

        ico[2] = 1;                                  // type = 1（图标）
        ico[4] = 1;                                  // count = 1

        ico[6] = 32;                                 // 目录项宽
        ico[7] = 32;                                 // 目录项高
        WriteInt16(ico, 6 + 4, 1);                   // planes
        WriteInt16(ico, 6 + 6, 4);                   // 位深（仅作展示，解码走 DIB 头）
        WriteInt32(ico, 6 + 8, dib.Length);          // 帧数据长度
        WriteInt32(ico, 6 + 12, headerSize);         // 帧数据偏移

        Buffer.BlockCopy(dib, 0, ico, headerSize, dib.Length);
        return ico;
    }

    /// <summary>把 Avalonia <see cref="Bitmap"/> 转回 SkiaSharp 以便逐像素核对。</summary>
    private static SKBitmap ToSkia(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        // 显式给编码器参数：Bitmap.Save(Stream) 的无参重载已标记过时（CS0618）
        bitmap.Save(ms, new PngBitmapEncoderOptions());
        ms.Position = 0;
        return SKBitmap.Decode(ms) ?? throw new InvalidOperationException("位图无法转回 SkiaSharp");
    }

    private static void WriteInt16(byte[] data, int offset, short value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
