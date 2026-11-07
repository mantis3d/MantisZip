using System.Globalization;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using SkiaSharp;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 文件列表缩略图服务层测试：分类、文字片段、LRU 缓存、选区控制器。
/// </summary>
public class ThumbnailTests
{
    // ─────────────────────────── ClassifyKind ───────────────────────────

    [Theory]
    [InlineData(PreviewType.Image, ThumbnailKind.Image)]
    [InlineData(PreviewType.AnimatedImage, ThumbnailKind.Image)]
    [InlineData(PreviewType.Svg, ThumbnailKind.Image)]
    [InlineData(PreviewType.IcoGallery, ThumbnailKind.Image)]
    [InlineData(PreviewType.Text, ThumbnailKind.Text)]
    [InlineData(PreviewType.Csv, ThumbnailKind.Text)]
    [InlineData(PreviewType.Markdown, ThumbnailKind.Text)]
    [InlineData(PreviewType.Html, ThumbnailKind.Text)]
    [InlineData(PreviewType.None, ThumbnailKind.None)]
    [InlineData(PreviewType.Unsupported, ThumbnailKind.None)]
    [InlineData(PreviewType.Pe, ThumbnailKind.None)]
    [InlineData(PreviewType.Font, ThumbnailKind.None)]
    [InlineData(PreviewType.Audio, ThumbnailKind.None)]
    [InlineData(PreviewType.Sqlite, ThumbnailKind.None)]
    [InlineData(PreviewType.Iso, ThumbnailKind.None)]
    [InlineData(PreviewType.Torrent, ThumbnailKind.None)]
    [InlineData(PreviewType.Office, ThumbnailKind.None)]
    [InlineData(PreviewType.Docx, ThumbnailKind.None)]
    [InlineData(PreviewType.Xlsx, ThumbnailKind.None)]
    [InlineData(PreviewType.Pptx, ThumbnailKind.None)]
    [InlineData(PreviewType.Video, ThumbnailKind.None)]
    [InlineData(PreviewType.Pdf, ThumbnailKind.None)]
    [InlineData(PreviewType.NeedsPassword, ThumbnailKind.None)]
    public void ClassifyKind_MapsEveryPreviewType(PreviewType type, ThumbnailKind expected)
    {
        Assert.Equal(expected, ThumbnailService.ClassifyKind(type));
    }

    // ─────────────────────────── TruncateTextSnippet ───────────────────────────

    [Fact]
    public void TruncateTextSnippet_ShortTextIsReturnedVerbatim()
    {
        var head = Encoding.UTF8.GetBytes("hello world");

        Assert.Equal("hello world", ThumbnailService.TruncateTextSnippet(head));
    }

    [Fact]
    public void TruncateTextSnippet_TrimsSurroundingWhitespace()
    {
        var head = Encoding.UTF8.GetBytes("  \r\n  hello  \t ");

        Assert.Equal("hello", ThumbnailService.TruncateTextSnippet(head));
    }

    [Fact]
    public void TruncateTextSnippet_EmptyOrWhitespace_ReturnsNull()
    {
        Assert.Null(ThumbnailService.TruncateTextSnippet(Array.Empty<byte>()));
        Assert.Null(ThumbnailService.TruncateTextSnippet(Encoding.UTF8.GetBytes("   \r\n\t  ")));
    }

    [Fact]
    public void TruncateTextSnippet_TruncatesTo150Chars()
    {
        var head = Encoding.UTF8.GetBytes(new string('a', 400));

        var snippet = ThumbnailService.TruncateTextSnippet(head);

        Assert.NotNull(snippet);
        Assert.Equal(ThumbnailService.TextSnippetMaxChars, snippet.Length);
        Assert.Equal(new string('a', ThumbnailService.TextSnippetMaxChars), snippet);
    }

    [Fact]
    public void TruncateTextSnippet_ChineseTextIsNotCorrupted()
    {
        // 240 个汉字（UTF-8 下 720 字节）→ 截到 150 字，不能出现半个多字节字符（U+FFFD）。
        var head = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("中文测试", 60)));

        var snippet = ThumbnailService.TruncateTextSnippet(head);

        Assert.NotNull(snippet);
        Assert.Equal(ThumbnailService.TextSnippetMaxChars, snippet.Length);
        Assert.DoesNotContain('\uFFFD', snippet);
        Assert.All(snippet, ch => Assert.InRange(ch, '\u4E00', '\u9FFF'));
    }

    [Fact]
    public void TruncateTextSnippet_DoesNotSplitSurrogatePairs()
    {
        // 每个 emoji 是 2 个 UTF-16 code unit；按文本元素截断才不会留下孤立代理项。
        var head = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("😀", 200)));

        var snippet = ThumbnailService.TruncateTextSnippet(head);

        Assert.NotNull(snippet);
        Assert.True(new StringInfo(snippet).LengthInTextElements <= ThumbnailService.TextSnippetMaxChars);
        for (var i = 0; i < snippet.Length; i++)
        {
            if (char.IsHighSurrogate(snippet[i]))
            {
                Assert.True(i + 1 < snippet.Length && char.IsLowSurrogate(snippet[i + 1]),
                    $"位置 {i} 的代理对被截断");
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(snippet[i]), $"位置 {i} 出现孤立低代理");
            }
        }
    }

    [Fact]
    public void TruncateTextSnippet_DropsTrailingReplacementAndNulChars()
    {
        // 头部按字节截断时，末尾可能留下半个多字节字符（解码成 U+FFFD）或 NUL 填充，应被去掉。
        var head = Encoding.UTF8.GetBytes("abc\uFFFD\0\0");

        Assert.Equal("abc", ThumbnailService.TruncateTextSnippet(head));
    }

    // ─────────────────────────── ThumbnailCache ───────────────────────────

    [Fact]
    public async Task Cache_HitDoesNotInvokeFactory()
    {
        var cache = new ThumbnailCache();
        var calls = 0;

        Task<ThumbnailResult?> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<ThumbnailResult?>(new ThumbnailResult(null, "v"));
        }

        var first = await cache.GetOrCreateAsync("k", Factory, CancellationToken.None);
        var second = await cache.GetOrCreateAsync("k", Factory, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cache_ConcurrentSameKey_InvokesFactoryOnce()
    {
        var cache = new ThumbnailCache();
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<ThumbnailResult?> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await gate.Task.ConfigureAwait(false);
            return new ThumbnailResult(null, "v");
        }

        var pending = Enumerable.Range(0, 16)
            .Select(_ => cache.GetOrCreateAsync("same", Factory, CancellationToken.None))
            .ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(pending);

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.NotNull(r));
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public async Task Cache_EvictsOldestEntryBeyondCapacity()
    {
        var cache = new ThumbnailCache();
        Assert.Equal(ThumbnailCache.DefaultCapacity, 300);

        for (var i = 0; i < ThumbnailCache.DefaultCapacity + 1; i++)
        {
            var value = $"v{i}";
            await cache.GetOrCreateAsync(
                $"k{i}",
                _ => Task.FromResult<ThumbnailResult?>(new ThumbnailResult(null, value)),
                CancellationToken.None);
        }

        Assert.Equal(ThumbnailCache.DefaultCapacity, cache.Count);

        // 最旧的 k0 已被逐出 → 工厂会再被调用。
        var rebuilt = 0;
        await cache.GetOrCreateAsync(
            "k0",
            _ =>
            {
                rebuilt++;
                return Task.FromResult<ThumbnailResult?>(new ThumbnailResult(null, "rebuilt"));
            },
            CancellationToken.None);

        Assert.Equal(1, rebuilt);

        // 最新的 k300 仍在缓存里 → 工厂不被调用。
        var latestCalls = 0;
        var latest = await cache.GetOrCreateAsync(
            $"k{ThumbnailCache.DefaultCapacity}",
            _ =>
            {
                latestCalls++;
                return Task.FromResult<ThumbnailResult?>(new ThumbnailResult(null, "miss"));
            },
            CancellationToken.None);

        Assert.Equal(0, latestCalls);
        Assert.Equal($"v{ThumbnailCache.DefaultCapacity}", latest!.Text);
    }

    [Fact]
    public async Task Cache_HitRefreshesLruOrder()
    {
        var cache = new ThumbnailCache(capacity: 2);

        await cache.GetOrCreateAsync("a", TextFactory("a"), CancellationToken.None);
        await cache.GetOrCreateAsync("b", TextFactory("b"), CancellationToken.None);

        // 命中 a，a 变为最近使用 → 再插入 c 时被逐出的是 b。
        await cache.GetOrCreateAsync("a", TextFactory("a2"), CancellationToken.None);
        await cache.GetOrCreateAsync("c", TextFactory("c"), CancellationToken.None);

        Assert.Equal(2, cache.Count);

        var aCalls = 0;
        var a = await cache.GetOrCreateAsync("a", _ =>
        {
            aCalls++;
            return Task.FromResult<ThumbnailResult?>(new ThumbnailResult(null, "miss"));
        }, CancellationToken.None);
        Assert.Equal(0, aCalls);
        Assert.Equal("a", a!.Text);

        var bCalls = 0;
        await cache.GetOrCreateAsync("b", _ =>
        {
            bCalls++;
            return Task.FromResult<ThumbnailResult?>(new ThumbnailResult(null, "b-again"));
        }, CancellationToken.None);
        Assert.Equal(1, bCalls);
    }

    [Fact]
    public async Task Cache_NullResultIsNotCached()
    {
        var cache = new ThumbnailCache();
        var calls = 0;

        var first = await cache.GetOrCreateAsync("n", _ =>
        {
            calls++;
            return Task.FromResult<ThumbnailResult?>(null);
        }, CancellationToken.None);

        var second = await cache.GetOrCreateAsync("n", _ =>
        {
            calls++;
            return Task.FromResult<ThumbnailResult?>(new ThumbnailResult(null, "ok"));
        }, CancellationToken.None);

        Assert.Null(first);
        Assert.Equal("ok", second!.Text);
        Assert.Equal(2, calls);
        Assert.Equal(1, cache.Count);
    }

    [AvaloniaFact]
    public async Task Cache_StoresBitmapResult()
    {
        var cache = new ThumbnailCache();
        using var stream = new MemoryStream(CreatePngBytes(8, 8));
        var bitmap = new Bitmap(stream);

        var first = await cache.GetOrCreateAsync(
            "img",
            _ => Task.FromResult<ThumbnailResult?>(new ThumbnailResult(bitmap, null)),
            CancellationToken.None);

        var second = await cache.GetOrCreateAsync(
            "img",
            _ => throw new InvalidOperationException("命中缓存时不应再调用工厂"),
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(bitmap, second!.Bitmap);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task Cache_ClearRemovesAllEntries()
    {
        var cache = new ThumbnailCache();
        await cache.GetOrCreateAsync("a", TextFactory("a"), CancellationToken.None);
        await cache.GetOrCreateAsync("b", TextFactory("b"), CancellationToken.None);
        Assert.Equal(2, cache.Count);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        var calls = 0;
        await cache.GetOrCreateAsync("a", _ =>
        {
            calls++;
            return Task.FromResult<ThumbnailResult?>(new ThumbnailResult(null, "a"));
        }, CancellationToken.None);
        Assert.Equal(1, calls);
    }

    // ─────────────────────────── ThumbnailSelectionController ───────────────────────────

    [Fact]
    public void Selection_PlainClickSelectsSingleRow_EvenInsideMultiSelection()
    {
        var items = CreateItems(5);
        var current = new[] { items[1], items[2], items[3] };

        var result = ThumbnailSelectionController.HandlePointerPressed(
            items[2], ctrl: false, shift: false, items, anchor: items[1], current);

        Assert.Same(items[2], result.Primary);
        Assert.Same(items[2], Assert.Single(result.Selection));
        Assert.Same(items[2], result.Anchor);
    }

    [Fact]
    public void Selection_CtrlClickAddsRowAndKeepsAnchor()
    {
        var items = CreateItems(5);
        var current = new[] { items[0] };

        var result = ThumbnailSelectionController.HandlePointerPressed(
            items[2], ctrl: true, shift: false, items, anchor: items[0], current);

        Assert.Equal(new[] { items[0], items[2] }, result.Selection);
        Assert.Same(items[2], result.Primary);
        Assert.Same(items[0], result.Anchor);
    }

    [Fact]
    public void Selection_CtrlClickRemovesSelectedRow()
    {
        var items = CreateItems(5);
        var current = new[] { items[0], items[2] };

        var result = ThumbnailSelectionController.HandlePointerPressed(
            items[0], ctrl: true, shift: false, items, anchor: items[0], current);

        Assert.Same(items[2], Assert.Single(result.Selection));
        Assert.Same(items[2], result.Primary);
        Assert.Same(items[0], result.Anchor);
    }

    [Fact]
    public void Selection_CtrlClickRemovingLastRowClearsSelection()
    {
        var items = CreateItems(3);
        var current = new[] { items[1] };

        var result = ThumbnailSelectionController.HandlePointerPressed(
            items[1], ctrl: true, shift: false, items, anchor: items[1], current);

        Assert.Empty(result.Selection);
        Assert.Null(result.Primary);
    }

    [Fact]
    public void Selection_ShiftClickSelectsRangeForward()
    {
        var items = CreateItems(6);

        var result = ThumbnailSelectionController.HandlePointerPressed(
            items[3], ctrl: false, shift: true, items, anchor: items[1], currentSelection: Array.Empty<ArchiveItemModel>());

        Assert.Equal(new[] { items[1], items[2], items[3] }, result.Selection);
        Assert.Same(items[3], result.Primary);
        Assert.Same(items[1], result.Anchor);
    }

    [Fact]
    public void Selection_ShiftClickSelectsRangeBackward()
    {
        var items = CreateItems(6);

        var result = ThumbnailSelectionController.HandlePointerPressed(
            items[1], ctrl: false, shift: true, items, anchor: items[4], currentSelection: Array.Empty<ArchiveItemModel>());

        Assert.Equal(new[] { items[1], items[2], items[3], items[4] }, result.Selection);
        Assert.Same(items[1], result.Primary);
        Assert.Same(items[4], result.Anchor);
    }

    [Fact]
    public void Selection_ShiftClickWithoutAnchorDegradesToSingleSelect()
    {
        var items = CreateItems(4);
        var current = new[] { items[0] };

        var result = ThumbnailSelectionController.HandlePointerPressed(
            items[2], ctrl: false, shift: true, items, anchor: null, current);

        Assert.Same(items[2], Assert.Single(result.Selection));
        Assert.Same(items[2], result.Primary);
        Assert.Same(items[2], result.Anchor);
    }

    [Fact]
    public void Selection_ShiftClickWithAnchorOutsideListDegradesToSingleSelect()
    {
        var items = CreateItems(4);
        var staleAnchor = CreateItems(2, offset: 100)[0];

        var result = ThumbnailSelectionController.HandlePointerPressed(
            items[2], ctrl: false, shift: true, items, anchor: staleAnchor, currentSelection: Array.Empty<ArchiveItemModel>());

        Assert.Same(items[2], Assert.Single(result.Selection));
        Assert.Same(items[2], result.Anchor);
    }

    [Fact]
    public void Selection_NullPressedItemKeepsSelectionAndClearsPrimary()
    {
        var items = CreateItems(4);
        var current = new[] { items[0], items[1] };

        var result = ThumbnailSelectionController.HandlePointerPressed(
            pressedItem: null, ctrl: false, shift: false, items, anchor: items[1], current);

        Assert.Null(result.Primary);
        Assert.Same(current, result.Selection);
        Assert.Same(items[1], result.Anchor);
    }

    [Fact]
    public void Selection_ShiftWinsOverCtrl()
    {
        var items = CreateItems(4);

        var result = ThumbnailSelectionController.HandlePointerPressed(
            items[2], ctrl: true, shift: true, items, anchor: items[0], currentSelection: new[] { items[3] });

        Assert.Equal(new[] { items[0], items[1], items[2] }, result.Selection);
        Assert.Same(items[2], result.Primary);
        Assert.Same(items[0], result.Anchor);
    }

    // ─────────────────────────── 复选框模式：快捷多选（2026-11-03） ───────────────────────────

    [Fact]
    public void Check_PlainClickChecksUncheckedRow()
    {
        var items = CreateItems(4);

        var result = ThumbnailSelectionController.HandleCheckPointerPressed(
            items[2], shift: false, items, anchor: null);

        Assert.Same(items[2], result.Primary);
        Assert.Same(items[2], Assert.Single(result.ToCheck));
        Assert.Empty(result.ToUncheck);
        Assert.Same(items[2], result.Anchor);
    }

    [Fact]
    public void Check_PlainClickOnCheckedRowUnchecks()
    {
        var items = CreateItems(4);
        items[1].IsChecked = true;

        var result = ThumbnailSelectionController.HandleCheckPointerPressed(
            items[1], shift: false, items, anchor: items[1]);

        Assert.Same(items[1], Assert.Single(result.ToUncheck));
        Assert.Empty(result.ToCheck);
        Assert.Same(items[1], result.Anchor);
    }

    [Fact]
    public void Check_ShiftClickChecksWholeRangeFromAnchor()
    {
        var items = CreateItems(6);

        var result = ThumbnailSelectionController.HandleCheckPointerPressed(
            items[4], shift: true, items, anchor: items[1]);

        Assert.Equal(new[] { items[1], items[2], items[3], items[4] }, result.ToCheck);
        Assert.Empty(result.ToUncheck);
        Assert.Same(items[1], result.Anchor); // 锚点不变：连续 Shift 以首次点击为起点
    }

    [Fact]
    public void Check_ShiftClickBackwardsChecksRange()
    {
        var items = CreateItems(6);

        var result = ThumbnailSelectionController.HandleCheckPointerPressed(
            items[1], shift: true, items, anchor: items[4]);

        Assert.Equal(new[] { items[1], items[2], items[3], items[4] }, result.ToCheck);
        Assert.Same(items[4], result.Anchor);
    }

    [Fact]
    public void Check_ShiftClickNeverUnchecksAlreadyCheckedRows()
    {
        var items = CreateItems(5);
        items[2].IsChecked = true;

        var result = ThumbnailSelectionController.HandleCheckPointerPressed(
            items[3], shift: true, items, anchor: items[1]);

        Assert.Equal(new[] { items[1], items[3] }, result.ToCheck);
        Assert.Empty(result.ToUncheck);
    }

    [Fact]
    public void Check_ShiftClickWithoutValidAnchorChecksOnlyPressedRow()
    {
        var items = CreateItems(5);

        var result = ThumbnailSelectionController.HandleCheckPointerPressed(
            items[3], shift: true, items, anchor: null);

        Assert.Same(items[3], Assert.Single(result.ToCheck));
        Assert.Same(items[3], result.Anchor);
    }

    [Fact]
    public void Check_NullPressedItemLeavesEverythingUntouched()
    {
        var items = CreateItems(3);
        items[0].IsChecked = true;

        var result = ThumbnailSelectionController.HandleCheckPointerPressed(
            pressedItem: null, shift: true, items, anchor: items[0]);

        Assert.Null(result.Primary);
        Assert.Empty(result.ToCheck);
        Assert.Empty(result.ToUncheck);
        Assert.Same(items[0], result.Anchor);
    }

    // ─────────────────────────── 大图缩略图（2026-11-07 用户报障） ───────────────────────────

    /// <summary>
    /// 端到端回归：压缩包内 **大于旧 8MB 上限** 的 JPG 必须生成缩略图。
    ///
    /// 用户报障：&gt;10MB 左右的 jpg 没有缩略图。根因是 <c>RunBatchAsync</c> 对
    /// <c>item.Size &gt; MaxThumbnailEntrySize</c> 的图像条目**直接 continue 跳过**
    /// （旧上限 8MB，原意是内存保护）。修法：上限提到 32MB + 解码改 SKCodec 降采样
    /// （不物化全分辨率位图），本用例钉住「大图也能出缩略图」。
    /// </summary>
    [AvaloniaFact]
    public async Task Batch_GeneratesThumbnail_ForJpegLargerThanOldEightMbCap()
    {
        var jpeg = CreateNoiseJpeg(3200, 2600, quality: 100);
        Assert.True(jpeg.Length > 8 * 1024 * 1024,
            $"测试图必须大于旧上限 8MB 才能复现，实际 {jpeg.Length:N0} 字节");

        var dir = Path.Combine(Path.GetTempPath(), "mantiszip-thumb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var archivePath = Path.Combine(dir, "big.zip");
        try
        {
            using (var fs = File.Create(archivePath))
            using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
            {
                using var entry = zip.CreateEntry("big.jpg", System.IO.Compression.CompressionLevel.Fastest).Open();
                entry.Write(jpeg, 0, jpeg.Length);
            }

            using var service = new ThumbnailService(archivePath, password: null, isSevenZipSolid: false);
            var item = new ArchiveItemModel { Name = "big.jpg", FullPath = "big.jpg", Size = jpeg.Length };
            service.StartBatch([item]);

            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (item.ThumbnailSource is null && DateTime.UtcNow < deadline)
                await Task.Delay(50);

            Assert.NotNull(item.ThumbnailSource);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 清理失败不影响断言 */ }
        }
    }

    /// <summary>
    /// 降采样解码正确性：大图的缩略图最长边 == 请求尺寸，且不物化全分辨率位图
    /// （实现走 <c>SKCodec.GetScaledDimensions</c> + <c>GetPixels</c>）。
    /// </summary>
    [AvaloniaFact]
    public void DecodeImage_DownscalesLargeJpegToRequestedSize()
    {
        var jpeg = CreateNoiseJpeg(1600, 1200, quality: 90);

        using var bitmap = ThumbnailService.DecodeImage(jpeg, ".jpg", 128);

        Assert.NotNull(bitmap);
        Assert.Equal(128, Math.Max(bitmap!.PixelSize.Width, bitmap.PixelSize.Height));
    }

    // ─────────────────────────── helpers ───────────────────────────

    /// <summary>用随机噪声生成 JPEG（噪声不可压缩 → 文件尺寸接近真实照片，3200×2600 q100 ≈ 10MB+）。</summary>
    private static byte[] CreateNoiseJpeg(int width, int height, int quality)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var pixels = new byte[width * height * 4];
        new Random(20261107).NextBytes(pixels);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    private static Func<CancellationToken, Task<ThumbnailResult?>> TextFactory(string text) =>
        _ => Task.FromResult<ThumbnailResult?>(new ThumbnailResult(null, text));

    private static ArchiveItemModel[] CreateItems(int count, int offset = 0)
    {
        var items = new ArchiveItemModel[count];
        for (var i = 0; i < count; i++)
        {
            var name = $"item{offset + i}.txt";
            items[i] = new ArchiveItemModel { Name = name, FullPath = name, Size = 1 };
        }

        return items;
    }

    private static byte[] CreatePngBytes(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.CornflowerBlue);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
