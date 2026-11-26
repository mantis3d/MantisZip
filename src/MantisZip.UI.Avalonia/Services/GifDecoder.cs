using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Cross-platform 动画解码器（GIF / 动画 WebP），底层为 SkiaSharp <see cref="SKCodec"/>。
/// Replaces System.Drawing-based GIF decoding (Windows-only).
/// <para>
/// 2026-11-16（issue #89「预览 WEBP 慢」）两处关键改动：
/// ① <b>链式增量解码</b>：第 i 帧用 <c>SKCodecOptions(i, i - 1)</c> 以第 i-1 帧为 priorFrame，
///    复用同一块像素缓冲。实测样本 Test_03.webp（14 帧 1284x518）全量解码 1092ms → 182ms、
///    Test_04.webp（26 帧 1280x531）4314ms → 361ms，且与逐帧独立解码<b>逐字节完全一致</b>。
/// ② <b>去掉 PNG 往返</b>：原来每帧 <c>SKImage.FromBitmap</c> + <c>Encode(Png)</c> + <c>new Bitmap(ms)</c>，
///    实测单是 PNG 编码就要 2184ms / 3873ms；现在直接把 BGRA 像素拷进 <see cref="WriteableBitmap"/>。
/// </para>
/// <para>
/// 另加帧内存闸门（<see cref="MaxAnimationFrames"/> / <see cref="MaxAnimationBytes"/>）：
/// 超限时只解首帧并在 UI 上明示「仅显示首帧」，避免几十帧全分辨率位图常驻内存。
/// </para>
/// </summary>
internal static class GifDecoder
{
    /// <summary>动画最多保留的帧数上限（超出即只解首帧）。</summary>
    public const int MaxAnimationFrames = 600;

    /// <summary>动画全部帧的像素内存预算（BGRA 合计），超出即只解首帧。</summary>
    public const long MaxAnimationBytes = 192L * 1024 * 1024;

    /// <summary>
    /// 解码动画文件（GIF / 动画 WebP）的全部帧与时延。
    /// </summary>
    /// <param name="filePath">动画文件路径。</param>
    /// <returns>解码结果；文件非法、无帧或单帧即超内存预算时返回 null。</returns>
    public static GifDecodeResult? DecodeFrames(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return DecodeFramesFromStream(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从流中链式解码动画帧。
    /// </summary>
    private static GifDecodeResult? DecodeFramesFromStream(Stream stream)
    {
        using var codec = SKCodec.Create(stream);
        if (codec == null) return null;

        int frameCount = codec.FrameCount;
        if (frameCount <= 0) return null;

        int width = codec.Info.Width;
        int height = codec.Info.Height;
        if (width <= 0 || height <= 0) return null;

        long frameBytes = (long)width * height * 4;

        // 单帧本身已超预算：不渲染（避免一次分配出几百 MB），交由调用方走「无法解码」路径
        if (frameBytes > MaxAnimationBytes) return null;

        // 闸门：帧数或总像素内存任一超限 → 只解首帧，其余由 UI 提示
        bool overBudget = ExceedsFrameBudget(frameCount, frameBytes);
        int decodeCount = overBudget ? 1 : frameCount;

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvasBitmap = new SKBitmap(info);
        if (canvasBitmap.GetPixels() == IntPtr.Zero) return null;

        int stride = width * 4;
        var pixelBuffer = new byte[stride * height];

        var result = new GifDecodeResult
        {
            TotalFrameCount = frameCount,
            IsFrameLimitApplied = overBudget,
        };

        for (int i = 0; i < decodeCount; i++)
        {
            // 第 0 帧独立解码；后续帧以第 i-1 帧为 priorFrame 链式增量合成
            var options = i == 0 ? new SKCodecOptions(0) : new SKCodecOptions(i, i - 1);
            var status = codec.GetPixels(info, canvasBitmap.GetPixels(), options);
            if (status != SKCodecResult.Success && status != SKCodecResult.IncompleteInput)
                continue;

            CopyPixels(canvasBitmap, pixelBuffer, stride, height);

            var frameBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using (var locked = frameBitmap.Lock())
            {
                Marshal.Copy(pixelBuffer, 0, locked.Address, pixelBuffer.Length);
            }

            int delayMs = 100; // Default
            var frameInfo = codec.FrameInfo?[i];
            if (frameInfo.HasValue)
            {
                // FrameInfo.Duration is in milliseconds for SKCodec
                delayMs = Math.Max(50, (int)frameInfo.Value.Duration);
            }

            result.Frames.Add(new GifFrameData
            {
                Bitmap = frameBitmap,
                DelayMs = delayMs,
            });
        }

        return result.Frames.Count > 0 ? result : null;
    }

    /// <summary>
    /// 判断动画是否超出帧内存预算（供 <see cref="DecodeFramesFromStream"/> 与回归测试共用）。
    /// </summary>
    /// <param name="frameCount">文件里的帧总数。</param>
    /// <param name="frameBytes">单帧像素字节数（宽 × 高 × 4）。</param>
    internal static bool ExceedsFrameBudget(int frameCount, long frameBytes) =>
        frameCount > MaxAnimationFrames || frameBytes * frameCount > MaxAnimationBytes;

    /// <summary>
    /// 把 SKBitmap 像素拷进紧排的 BGRA 缓冲（<see cref="SKBitmap.RowBytes"/> 与 4 字节/像素紧排一致时一次性拷贝）。
    /// </summary>
    private static void CopyPixels(SKBitmap source, byte[] destination, int stride, int height)
    {
        var src = source.GetPixels();
        int rowBytes = source.RowBytes;
        if (rowBytes == stride)
        {
            Marshal.Copy(src, destination, 0, stride * height);
            return;
        }

        // 行间距有额外填充：逐行拷，避免把填充字节当像素写进 WriteableBitmap
        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(IntPtr.Add(src, y * rowBytes), destination, y * stride, stride);
        }
    }

    /// <summary>
    /// Gets the total frame count of an animation without decoding all frames.
    /// </summary>
    public static int GetFrameCount(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var codec = SKCodec.Create(stream);
            return codec?.FrameCount ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>
/// 动画解码结果：帧列表 + 原始帧总数 + 是否因内存闸门只解了首帧。
/// </summary>
public sealed class GifDecodeResult
{
    /// <summary>已解码的帧（闸门生效时只有首帧）。</summary>
    public List<GifFrameData> Frames { get; } = [];

    /// <summary>文件里的原始帧总数（不受闸门影响，用于提示与元数据）。</summary>
    public int TotalFrameCount { get; init; }

    /// <summary>是否因帧数 / 像素内存超预算而只解了首帧。</summary>
    public bool IsFrameLimitApplied { get; init; }
}

/// <summary>
/// Represents a single animation frame.
/// </summary>
public class GifFrameData
{
    public Bitmap Bitmap { get; set; } = null!;
    public int DelayMs { get; set; }
}
