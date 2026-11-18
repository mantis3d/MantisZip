using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using MantisZip.UI.Avalonia.Services;
using SkiaSharp;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// issue #89 回归：动画解码器（GIF / 动画 WebP）的链式增量解码与帧内存闸门。
///
/// <para>
/// 两件事必须钉住，否则「优化」会静默变成「画面变了」：
/// ① <b>链式解码像素等价</b>：第 i 帧用第 i-1 帧作 priorFrame 合成，
///    结果必须与「逐帧独立解码」逐像素一致（旧实现就是独立解码，慢了约 12 倍）；
/// ② <b>闸门</b>：帧数 / 像素内存超预算时只解首帧，并把「原始帧总数」如实带出来，
///    UI 才能告诉用户为什么不动。
/// </para>
///
/// <para>
/// 素材取仓库自带的 <c>docs/images/*.gif</c>（无外部依赖），解码走真实 Skia，
/// 故统一用 <c>[AvaloniaFact]</c>。
/// </para>
/// </summary>
public class GifDecoderTests
{
    /// <summary>
    /// 链式解码结果必须与逐帧独立解码逐像素一致（issue #89 的核心验收条件）。
    /// </summary>
    [AvaloniaFact]
    public void DecodeFrames_ChainedDecode_MatchesIndependentDecode()
    {
        var path = GetFixture("docs/images/version/v0.5.0/DragExtract.gif");

        var result = GifDecoder.DecodeFrames(path);
        Assert.NotNull(result);
        Assert.True(result!.Frames.Count > 1, "样本必须是多帧 GIF，否则测不到链式解码");

        // 用独立的逐帧解码结果作基准
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        Assert.Equal(codec!.FrameCount, result.TotalFrameCount);

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul);

        for (var i = 0; i < result.Frames.Count; i++)
        {
            using var independent = new SKBitmap(info);
            var status = codec.GetPixels(info, independent.GetPixels(), new SKCodecOptions(i));
            Assert.True(status is SKCodecResult.Success or SKCodecResult.IncompleteInput,
                $"第 {i} 帧独立解码失败：{status}");

            var actual = ToSkia(result.Frames[i].Bitmap);
            try
            {
                Assert.Equal(independent.Width, actual.Width);
                Assert.Equal(independent.Height, actual.Height);

                var mismatched = 0;
                for (var y = 0; y < independent.Height && mismatched == 0; y++)
                {
                    for (var x = 0; x < independent.Width; x++)
                    {
                        if (independent.GetPixel(x, y) != actual.GetPixel(x, y))
                        {
                            mismatched++;
                            break;
                        }
                    }
                }

                Assert.Equal(0, mismatched);
            }
            finally
            {
                actual.Dispose();
            }
        }
    }

    /// <summary>
    /// 正常预算内的动画：解码帧数 = 文件帧数，闸门不生效，位图尺寸 = 画布尺寸。
    /// </summary>
    [AvaloniaFact]
    public void DecodeFrames_WithinBudget_ReturnsAllFrames()
    {
        var path = GetFixture("docs/images/version/v0.5.0/DragAdd.gif");

        var result = GifDecoder.DecodeFrames(path);

        Assert.NotNull(result);
        Assert.False(result!.IsFrameLimitApplied);
        Assert.Equal(result.TotalFrameCount, result.Frames.Count);
        Assert.All(result.Frames, frame =>
        {
            Assert.IsType<WriteableBitmap>(frame.Bitmap);
            Assert.Equal(new PixelSize(result.Frames[0].Bitmap.PixelSize.Width,
                result.Frames[0].Bitmap.PixelSize.Height), frame.Bitmap.PixelSize);
            Assert.True(frame.DelayMs >= 50, "时延下限必须 ≥ 50ms，否则播放会抖动");
        });
    }

    /// <summary>
    /// 闸门判定本身：帧数超限 或 总像素内存超限 → true；正常动画 → false。
    /// 纯算术，不需要解码，故用 <c>[Fact]</c>。
    /// </summary>
    [Fact]
    public void ExceedsFrameBudget_MatchesFrameCountAndMemoryLimits()
    {
        // 800x464 BGRA ≈ 1.48MB/帧：30 帧在 192MB 内、600 帧上限内 ⇒ 不过预算
        var smallFrameBytes = 800L * 464 * 4;
        Assert.False(GifDecoder.ExceedsFrameBudget(30, smallFrameBytes));

        // 帧数超 600 上限 ⇒ 过预算
        Assert.True(GifDecoder.ExceedsFrameBudget(GifDecoder.MaxAnimationFrames + 1, 16L));

        // 帧数不多但总内存超 192MB ⇒ 过预算（1280x720 BGRA ≈ 3.6MB/帧，60 帧 ≈ 221MB）
        var bigFrameBytes = 1280L * 720 * 4;
        Assert.True(GifDecoder.ExceedsFrameBudget(60, bigFrameBytes));
        Assert.False(GifDecoder.ExceedsFrameBudget(30, bigFrameBytes));

        // 边界正好等于上限时不算超（避免「刚好 600 帧」被误判为受限）
        Assert.False(GifDecoder.ExceedsFrameBudget(GifDecoder.MaxAnimationFrames, 1L));
        Assert.False(GifDecoder.ExceedsFrameBudget(1, GifDecoder.MaxAnimationBytes));
    }

    /// <summary>坏数据 / 空文件只返回 null，绝不抛异常（契约与旧实现一致）。</summary>
    [Fact]
    public void DecodeFrames_InvalidFiles_ReturnNull()
    {
        // 文本文件不是图片：SKCodec.Create 返回 null ⇒ null
        Assert.Null(GifDecoder.DecodeFrames(GetFixture("docs/images/polar_logotype_black.svg")));
        Assert.Null(GifDecoder.DecodeFrames(Path.Combine(Path.GetTempPath(), "mantiszip_missing_animation.gif")));

        var garbage = Path.Combine(Path.GetTempPath(),
            $"mantiszip_garbage_{Guid.NewGuid():N}.gif");
        File.WriteAllBytes(garbage, [0x00, 0x01, 0x02, 0x03]);
        try
        {
            Assert.Null(GifDecoder.DecodeFrames(garbage));
            Assert.Equal(0, GifDecoder.GetFrameCount(garbage));
        }
        finally
        {
            File.Delete(garbage);
        }
    }

    /// <summary>帧数探测与解码结果里的原始帧总数必须一致（元数据展示直接用它）。</summary>
    [AvaloniaFact]
    public void GetFrameCount_MatchesTotalFrameCount()
    {
        var path = GetFixture("docs/images/preview-media.gif");

        var result = GifDecoder.DecodeFrames(path);
        Assert.NotNull(result);
        Assert.Equal(GifDecoder.GetFrameCount(path), result!.TotalFrameCount);
    }

    /// <summary>从测试输出目录向上定位仓库根（同仓库其它测试的既有模式）。</summary>
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException(
            $"无法定位仓库根（祖先目录链中未找到 'src'）。Base: {AppContext.BaseDirectory}");
    }

    private static string GetFixture(string relativePath) =>
        Path.Combine(GetRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>把 Avalonia <see cref="Bitmap"/> 转回 SkiaSharp 以便逐像素比对。</summary>
    private static SKBitmap ToSkia(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        // 显式给编码器参数：Bitmap.Save(Stream) 的无参重载已标记过时（CS0618）
        bitmap.Save(ms, new PngBitmapEncoderOptions());
        ms.Position = 0;
        return SKBitmap.Decode(ms) ?? throw new InvalidOperationException("位图无法转回 SkiaSharp");
    }
}
