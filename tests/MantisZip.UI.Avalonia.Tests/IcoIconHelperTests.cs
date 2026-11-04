using Avalonia;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using SkiaSharp;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// <see cref="IcoIconHelper"/> 的用例：手工打包 ICO 容器，验证「挑边长最接近目标的帧」与「坏数据返回 null」。
///
/// <para>
/// 这里用真实 Skia 生成 PNG 帧（见 <c>TestAppBuilder</c> 的 <c>UseHeadlessDrawing=false</c>）：
/// 生成位图帧的用例必须标 <c>[AvaloniaFact]</c>（需要 Skia 解码真实像素尺寸），
/// 只验证「非法数据返回 null」的用例标 <c>[Fact]</c>（在解码之前就返回了，不需要 Skia）。
/// </para>
/// </summary>
public class IcoIconHelperTests
{
    [AvaloniaFact]
    public void ExtractIcon_SingleFrame16Png_ReturnsNonNull16x16()
    {
        var icoData = PackIco((CreatePngBytes(16, 16), 16, 16));

        using var icon = IcoIconHelper.ExtractIcon(icoData);

        Assert.NotNull(icon);
        Assert.Equal(new PixelSize(16, 16), icon!.PixelSize);
    }

    [AvaloniaFact]
    public void ExtractIcon_MultiFrame_ChoosesClosestTo16x16()
    {
        // 故意把 32×32 放在前面：面积更接近 256 的 16×16 帧必须胜出
        var icoData = PackIco(
            (CreatePngBytes(32, 32), 32, 32),
            (CreatePngBytes(16, 16), 16, 16));

        using var icon = IcoIconHelper.ExtractIcon(icoData);

        Assert.NotNull(icon);
        Assert.Equal(new PixelSize(16, 16), icon!.PixelSize);
    }

    [AvaloniaFact]
    public void ExtractIcon_256FrameEncodedAsZero_Returns256x256()
    {
        // ICO 目录项里宽高字节为 0 表示 256；只有一帧时仍应解码出真实尺寸
        var icoData = PackIco((CreatePngBytes(256, 256), 0, 0));

        using var icon = IcoIconHelper.ExtractIcon(icoData);

        Assert.NotNull(icon);
        Assert.Equal(new PixelSize(256, 256), icon!.PixelSize);
    }

    [AvaloniaFact]
    public void ExtractIcon_WithPreferredSide160_ChoosesClosestTo160x160()
    {
        // 缩略图卡是 128px，但挑帧要按 160 找（见 ThumbnailService.IcoThumbnailPreferredSide）：
        // 多帧 ico 必须取 128 帧，不能取 16 帧再放大（会糊）
        var icoData = PackIco(
            (CreatePngBytes(16, 16), 16, 16),
            (CreatePngBytes(128, 128), 128, 128),
            (CreatePngBytes(32, 32), 32, 32));

        using var icon = IcoIconHelper.ExtractIcon(icoData, ThumbnailService.IcoThumbnailPreferredSide);

        Assert.NotNull(icon);
        Assert.Equal(new PixelSize(128, 128), icon!.PixelSize);
    }

    [AvaloniaFact]
    public void ExtractIcon_Only16And256_WithPreferredSide160_Chooses256()
    {
        // 回归：老图标常见「只有 256 与 16 两帧」。若按面积差找最接近 128 的帧，
        // 16 帧（面积差 16128）会赢过 256 帧（面积差 49152）⇒ 选中 16 再放大，糊。
        // 按边长差找 160 时 256 差 96、16 差 144 ⇒ 必须选中 256。
        var icoData = PackIco(
            (CreatePngBytes(16, 16), 16, 16),
            (CreatePngBytes(256, 256), 0, 0));

        using var icon = IcoIconHelper.ExtractIcon(icoData, ThumbnailService.IcoThumbnailPreferredSide);

        Assert.NotNull(icon);
        Assert.Equal(new PixelSize(256, 256), icon!.PixelSize);
    }

    [AvaloniaFact]
    public void ExtractIcon_WithPreferredSide160_FallsBackToLargestWhenNoLargeFrame()
    {
        // 只有 16/48 两帧时，按边长差找 160 仍是 48（而不是 16）
        var icoData = PackIco(
            (CreatePngBytes(16, 16), 16, 16),
            (CreatePngBytes(48, 48), 48, 48));

        using var icon = IcoIconHelper.ExtractIcon(icoData, ThumbnailService.IcoThumbnailPreferredSide);

        Assert.NotNull(icon);
        Assert.Equal(new PixelSize(48, 48), icon!.PixelSize);
    }

    [Fact]
    public void ExtractIcon_NonIcoPayloads_ReturnNull()
    {
        Assert.Null(IcoIconHelper.ExtractIcon([]));
        Assert.Null(IcoIconHelper.ExtractIcon(new byte[] { 0, 0, 0, 0 }));

        // 裸 PNG：没有 ICO 头，reserved/type 校验必须当场拒绝
        Assert.Null(IcoIconHelper.ExtractIcon(CreatePngBytes(16, 16)));
    }

    [Fact]
    public void ExtractIcon_ZeroFrameCount_ReturnsNull()
    {
        var icoData = new byte[22];
        icoData[2] = 1;   // type = 1（图标）

        Assert.Null(IcoIconHelper.ExtractIcon(icoData));
    }

    [Fact]
    public void ExtractIcon_FrameBeyondEnd_ReturnsNull()
    {
        // 目录项声明的数据长度远超文件实际长度：越界必须拒绝，不能截断后硬解
        var icoData = PackIco((CreatePngBytes(16, 16), 16, 16));
        WriteUInt32(icoData, 6 + 8, (uint)(icoData.Length * 4));

        Assert.Null(IcoIconHelper.ExtractIcon(icoData));
    }

    /// <summary>用 Skia 生成一张纯色 PNG，作为 ICO 的 PNG 帧。</summary>
    private static byte[] CreatePngBytes(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// 把若干 PNG 帧手工打包成 ICO 文件：ICONDIR(6 字节) + 每帧 16 字节目录项 + 帧数据。
    /// </summary>
    /// <param name="frames">帧数据与目录项里记录的宽高（宽高字节为 0 表示 256）。</param>
    private static byte[] PackIco(params (byte[] Frame, byte Width, byte Height)[] frames)
    {
        var directorySize = 6 + 16 * frames.Length;
        var icoData = new byte[directorySize + frames.Sum(f => f.Frame.Length)];

        // ICONDIR：reserved=0、type=1（图标）、count
        icoData[2] = 1;
        icoData[4] = (byte)(frames.Length & 0xFF);
        icoData[5] = (byte)((frames.Length >> 8) & 0xFF);

        var dataOffset = directorySize;
        for (var i = 0; i < frames.Length; i++)
        {
            var entryOffset = 6 + 16 * i;
            icoData[entryOffset] = frames[i].Width;        // 0 表示 256
            icoData[entryOffset + 1] = frames[i].Height;
            icoData[entryOffset + 2] = 0;                  // 色数（0 = 未指定）
            icoData[entryOffset + 3] = 0;                  // 保留字节
            WriteUInt16(icoData, entryOffset + 4, 1);      // planes
            WriteUInt16(icoData, entryOffset + 6, 32);     // 位深
            WriteUInt32(icoData, entryOffset + 8, (uint)frames[i].Frame.Length);
            WriteUInt32(icoData, entryOffset + 12, (uint)dataOffset);

            Buffer.BlockCopy(frames[i].Frame, 0, icoData, dataOffset, frames[i].Frame.Length);
            dataOffset += frames[i].Frame.Length;
        }

        return icoData;
    }

    /// <summary>按小端序写 2 字节无符号整数。</summary>
    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    /// <summary>按小端序写 4 字节无符号整数。</summary>
    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
