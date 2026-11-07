using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using SkiaSharp;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 缩略图分类：决定文件列表里某个条目该显示什么。
/// </summary>
public enum ThumbnailKind
{
    /// <summary>不做缩略图（保留文件图标）。</summary>
    None = 0,

    /// <summary>图像缩略图（写到 ArchiveItemModel.ThumbnailSource）。</summary>
    Image = 1,

    /// <summary>文字片段（写到 ArchiveItemModel.TextSnippet）。</summary>
    Text = 2,
}

/// <summary>
/// 文件列表缩略图服务：为一个压缩包内的条目批量生成「图像缩略图」或「文字片段」。
///
/// <para>
/// 分类只按扩展名（<see cref="PreviewService.ClassifyPreview"/>）走，不读文件头做魔数探测——
/// 列表里每个条目都解一次头会让滚动明显变卡，而扩展名分类已经覆盖了绝大多数常见情况。
/// </para>
/// <para>
/// 并发：每批 <see cref="MaxConcurrency"/> 个条目并行（<see cref="SemaphoreSlim"/> 限流），
/// 解码在后台线程完成，模型属性一律经 <see cref="Dispatcher.UIThread"/> 写回。
/// 结果先写 <see cref="ThumbnailCache"/> 再写模型属性：缓存是共享的，滚动出视口再滚回来不会重复解压。
/// </para>
/// <para>
/// 失败即静默降级：任何异常只写一行 <see cref="App.DebugLog"/>，条目退回文件图标；
/// 取消（切包 / 关包）不记日志。
/// </para>
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    /// <summary>缩略图最长边的**默认值**（像素）；实际边长由设置 → 外观 → 缩略图决定（96/128/160）。</summary>
    public const int DefaultThumbnailSize = 128;

    /// <summary>从 ICO 里挑帧时「期望边长」的下限——**故意大于默认缩略图尺寸**。
    /// <remarks>
    /// 老图标常见「只有 256 与 16 两帧」，若按 128 找最接近的帧，256 差 128、16 差 112 ⇒ 会选中 16 帧，
    /// 放大到 128 糊成一团。取 160 时 256 只差 96、16 差 144 ⇒ 选中 256 帧，缩到 128 反而清晰。
    /// 一句话：宁可拿大帧缩小，也不要拿小帧放大。
    /// </remarks>
    public const int IcoThumbnailPreferredSide = 160;

    /// <summary>单个条目允许生成缩略图的大小上限（32MB），超过直接跳过。
    /// <para>图像条目要**整份解压到字节缓冲**再解码，这个常量就是缓冲上界（并发 4 ⇒ 最坏
    /// 128MB 瞬时字节内存）。2026-11-07 从 8MB 提到 32MB：用户真机反馈 &gt;8MB 的相机 JPG
    /// 没有缩略图；同时 <see cref="DecodeImage"/> 改「SKCodec 降采样解码」，不再物化全分辨率
    /// 位图（旧路径一张 4200×3200 大图解码峰值 ≈54MB × 并发 4 才是真正风险）。</para></summary>
    public const long MaxThumbnailEntrySize = 32L * 1024 * 1024;

    /// <summary>文字类条目读取的头部字节数。</summary>
    public const int TextHeadSize = 4096;

    /// <summary>文字片段最大字符数。</summary>
    public const int TextSnippetMaxChars = 150;

    /// <summary>批量处理的并发上限。</summary>
    public const int MaxConcurrency = 4;

    private readonly string _archivePath;
    private readonly string? _password;
    private readonly bool _isSevenZipSolid;
    private readonly ArchiveFormat _format;
    private readonly ThumbnailCache _cache;
    private readonly int _thumbnailSize;
    private readonly SemaphoreSlim _throttle = new(MaxConcurrency, MaxConcurrency);

    /// <summary>本实例的缩略图最长边（像素）。</summary>
    public int ThumbnailSize => _thumbnailSize;

    private CancellationTokenSource? _batchCts;
    private Task? _batch;
    private volatile bool _disposed;

    /// <summary>
    /// 创建服务。
    /// </summary>
    /// <param name="archivePath">压缩包路径（提取头部时用）。</param>
    /// <param name="password">压缩包密码，无密码传 <see langword="null"/>。</param>
    /// <param name="isSevenZipSolid">是否 7z 固实包：固实包读单条要整包解压，缩略图一律跳过。</param>
    /// <param name="cache">共享缓存；<see langword="null"/> 时内部自建（容量 <see cref="ThumbnailCache.DefaultCapacity"/>）。</param>
    /// <param name="thumbnailSize">
    /// 缩略图最长边（像素，来自设置 → 外观 → 缩略图；默认 <see cref="DefaultThumbnailSize"/>）。
    /// 会进缓存 key ⇒ 改尺寸后不会命中旧尺寸的缓存条目。
    /// </param>
    public ThumbnailService(
        string archivePath,
        string? password,
        bool isSevenZipSolid,
        ThumbnailCache? cache = null,
        int thumbnailSize = DefaultThumbnailSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        _archivePath = archivePath;
        _password = password;
        _isSevenZipSolid = isSevenZipSolid;
        _cache = cache ?? new ThumbnailCache();
        _thumbnailSize = thumbnailSize > 0 ? thumbnailSize : DefaultThumbnailSize;
        _format = ArchiveFormatHelper.GetFormat(archivePath);
    }

    /// <summary>
    /// 启动一批缩略图生成（取消上一批）。
    /// </summary>
    /// <param name="items">当前列表的条目（目录会被跳过）。</param>
    public void StartBatch(IReadOnlyList<ArchiveItemModel> items)
    {
        if (_disposed)
            return;

        ArgumentNullException.ThrowIfNull(items);

        // 先取消旧批：避免「切了包还往旧列表里写图」的错位。
        Cancel();

        var cts = new CancellationTokenSource();
        var previous = _batch;
        _batchCts = cts;

        _batch = Task.Run(async () =>
        {
            // 等上一批真正收尾（它在收到取消后会尽快结束），再开始新的一批，
            // 这样两批的写入不会交叉、限流信号量也不会被旧批占满。
            if (previous is not null)
            {
                try
                {
                    await previous.ConfigureAwait(false);
                }
                catch
                {
                    // 旧批被取消时的异常：新批不关心。
                }
            }

            await RunBatchAsync(items, cts.Token).ConfigureAwait(false);
        });
    }

    /// <summary>取消当前批（切包 / 关包 / 关闭缩略图功能时调用）。</summary>
    public void Cancel()
    {
        var cts = _batchCts;
        if (cts is null)
            return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经 Dispose 掉的批：无事可做。
        }
    }

    /// <summary>清空共享缓存（换包时由调用方触发）。</summary>
    public void ClearCache() => _cache.Clear();

    /// <summary>
    /// 静态分类：把预览类型映射到缩略图类别。
    /// </summary>
    /// <param name="type">扩展名分类结果。</param>
    /// <returns>图像类 → <see cref="ThumbnailKind.Image"/>；文本类 → <see cref="ThumbnailKind.Text"/>；其余 <see cref="ThumbnailKind.None"/>。</returns>
    public static ThumbnailKind ClassifyKind(PreviewType type) => type switch
    {
        PreviewType.Image or PreviewType.AnimatedImage or PreviewType.Svg or PreviewType.IcoGallery
            => ThumbnailKind.Image,
        PreviewType.Text or PreviewType.Csv or PreviewType.Markdown or PreviewType.Html
            => ThumbnailKind.Text,
        _ => ThumbnailKind.None,
    };

    /// <summary>
    /// 从条目头部字节里截出一段文字片段。
    /// </summary>
    /// <param name="head">条目头部字节（通常 ≤ <see cref="TextHeadSize"/>）。</param>
    /// <returns>去首尾空白后的片段（最长 <see cref="TextSnippetMaxChars"/> 个文本元素）；空/空白返回 <see langword="null"/>。</returns>
    public static string? TruncateTextSnippet(byte[] head)
    {
        if (head is null || head.Length == 0)
            return null;

        var text = TextEncodingDetector.DecodeText(head);
        if (string.IsNullOrEmpty(text))
            return null;

        // 头部按字节截断时末尾可能留下半个多字节字符（解码成 U+FFFD），一并去掉。
        text = text.TrimEnd('\uFFFD', '\0').Trim();
        if (text.Length == 0)
            return null;

        if (text.Length <= TextSnippetMaxChars)
            return text;

        // 用文本元素（grapheme）截断：不会把代理对（emoji / 生僻字）或组合字符切一半。
        var info = new StringInfo(text);
        var take = Math.Min(TextSnippetMaxChars, info.LengthInTextElements);
        return info.SubstringByTextElements(0, take).TrimEnd();
    }

    private async Task RunBatchAsync(IReadOnlyList<ArchiveItemModel> items, CancellationToken ct)
    {
        var running = new List<Task>(items.Count);

        try
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                if (item.IsDirectory)
                    continue;

                var kind = ClassifyKind(PreviewService.ClassifyPreview(Path.GetExtension(item.Name)));
                if (kind == ThumbnailKind.None)
                    continue;

                // 图像：超大条目（>32MB）/ 7z 固实包直接跳过（固实包读一条 = 整包解压）。
                // 32MB 内的相机 JPG 现在会生成缩略图（2026-11-07 从 8MB 提高 + 降采样解码）。
                // 文字：没有大小上限，只读 4KB 头部，代价可控。
                if (kind == ThumbnailKind.Image && (item.Size > MaxThumbnailEntrySize || _isSevenZipSolid))
                    continue;

                // 在启动前拿信号量：并发上限 4，且不会为几千个条目一次性创建几千个 Task。
                await _throttle.WaitAsync(ct).ConfigureAwait(false);
                running.Add(ProcessItemAsync(item, kind, ct));
            }

            await Task.WhenAll(running).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常取消：不记日志。
        }
    }

    private async Task ProcessItemAsync(ArchiveItemModel item, ThumbnailKind kind, CancellationToken ct)
    {
        try
        {
            var key = BuildCacheKey(item);
            var result = await _cache
                .GetOrCreateAsync(key, token => CreateAsync(item, kind, token), ct)
                .ConfigureAwait(false);

            if (result is null || ct.IsCancellationRequested)
                return;

            Post(() =>
            {
                if (kind == ThumbnailKind.Image)
                    item.ThumbnailSource = result.Bitmap;
                else
                    item.TextSnippet = result.Text;
            });
        }
        catch (OperationCanceledException)
        {
            // 取消是预期路径，静默。
        }
        catch (Exception ex)
        {
            Log($"生成缩略图失败：{item.FullPath} — {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _throttle.Release();
        }
    }

    private Task<ThumbnailResult?> CreateAsync(ArchiveItemModel item, ThumbnailKind kind, CancellationToken ct) =>
        kind == ThumbnailKind.Image ? CreateImageAsync(item, ct) : CreateTextAsync(item, ct);

    private async Task<ThumbnailResult?> CreateTextAsync(ArchiveItemModel item, CancellationToken ct)
    {
        var head = await ArchiveEntryExtractor
            .ExtractHeadAsync(_archivePath, item.FullPath, TextHeadSize, _format, _password, ct)
            .ConfigureAwait(false);

        var snippet = TruncateTextSnippet(head);
        return snippet is null ? null : new ThumbnailResult(null, snippet);
    }

    private async Task<ThumbnailResult?> CreateImageAsync(ArchiveItemModel item, CancellationToken ct)
    {
        // 已知大小就按大小取（小图一次读完，避免无谓的多读）；大小未知（0）按上限取。
        var wanted = item.Size > 0 ? item.Size : MaxThumbnailEntrySize;
        var maxBytes = (int)Math.Min(wanted, MaxThumbnailEntrySize);

        var bytes = await ArchiveEntryExtractor
            .ExtractHeadAsync(_archivePath, item.FullPath, maxBytes, _format, _password, ct)
            .ConfigureAwait(false);

        if (bytes.Length == 0)
            return null;

        var bitmap = DecodeImage(bytes, Path.GetExtension(item.Name), _thumbnailSize);
        return bitmap is null ? null : new ThumbnailResult(bitmap, null);
    }

    /// <summary>
    /// 字节 → 卡片尺寸的缩略图（边长由设置 → 外观 → 缩略图决定）。<c>.ico</c> 走多尺寸图标提取（取边长最接近 160 的帧，不是列表图标的 16），
    /// <c>.svg</c> 走矢量渲染（矢量按目标边长重绘，不受其声明尺寸限制），其余走 Skia 位图解码。
    /// <para>位图走 <c>SKCodec</c> 降采样解码（<c>GetScaledDimensions</c> + <c>GetPixels</c>）：
    /// 大图不物化全分辨率位图，内存只与目标缩略图尺寸相关。</para>
    /// <para>测试钩子：<c>internal</c> 供 ThumbnailTests 直接验证大图降采样解码（经 InternalsVisibleTo）。</para>
    /// </summary>
    internal static Bitmap? DecodeImage(byte[] bytes, string extension, int size)
    {
        // 缩略图卡是 128px，但这里**故意要 160**：ico 里常见「只有 256 和 16 两帧」的老图标，
        // 若按 128 找最接近，256 差 128、16 差 112 ⇒ 会选中 16 帧（放大到 128 糊成一团）；
        // 要 160 时 256 只差 96、16 差 144 ⇒ 选中 256 帧（缩小到 128，清晰）。
        // 一句话：宁可拿大帧缩小，也不要拿小帧放大。
        if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            return IcoIconHelper.ExtractIcon(bytes, Math.Max(IcoThumbnailPreferredSide, size));

        // 项目已引用 Svg.Skia（MantisZip.UI.Avalonia.csproj），矢量图单独渲染成位图，
        // 否则 SKBitmap.Decode 对 SVG 只会返回 null。
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            return RenderSvg(bytes, size);

        // 位图解码走 SKCodec 降采样：大图直接解到接近目标尺寸的小位图，不物化全分辨率位图。
        // 旧实现 SKBitmap.Decode(bytes) 对 4200×3200 照片先分配 ≈54MB 位图（×并发 4 才是内存风险）。
        using var stream = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
            return null;

        var source = codec.Info;
        if (source.Width <= 0 || source.Height <= 0)
            return null;

        var (targetWidth, targetHeight) = ScaleToThumbnail(source.Width, source.Height, size);

        // 选解码尺寸：源图不超过目标 → 原尺寸解码（沿用「小图不放大」语义）；否则请求
        // 「受支持缩放档位里仍不低于目标尺寸」的那一档（档位是离散的，例如 300px 源请求
        // 128 只给到 113，要退一档到 1/2=150 再画布缩到 128，否则缩略图会比以前小）。
        var decodeWidth = source.Width;
        var decodeHeight = source.Height;
        var longestSide = Math.Max(source.Width, source.Height);
        if (longestSide > size)
        {
            var scale = size / (float)longestSide;
            var scaled = codec.GetScaledDimensions(scale);
            if (scaled.Width < targetWidth || scaled.Height < targetHeight)
                scaled = codec.GetScaledDimensions(Math.Min(1f, scale * 2f));

            decodeWidth = scaled.Width;
            decodeHeight = scaled.Height;
        }

        var decodeInfo = new SKImageInfo(decodeWidth, decodeHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());

        // IncompleteInput：条目按 MaxThumbnailEntrySize 截断后缺尾部扫描数据，已解出的部分
        // 仍可当缩略图（当成失败会让大图全部没图——2026-11-07 修复前 &gt;8MB JPG 就是没图）。
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            return null;

        // 解码尺寸恰好等于目标尺寸：直接编码，省一次重绘。
        if (decodeWidth == targetWidth && decodeHeight == targetHeight)
            return Encode(decoded);

        using var target = new SKBitmap(new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(targetWidth / (float)decodeWidth, targetHeight / (float)decodeHeight);
            canvas.DrawBitmap(decoded, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            canvas.Flush();
        }

        return Encode(target);
    }

    /// <summary>
    /// 用 Svg.Skia 把矢量图渲染成最长边恰好 <paramref name="size"/> 的位图。
    /// </summary>
    /// <remarks>
    /// 矢量图**必须按目标尺寸重绘**，不能沿用 <see cref="ScaleToThumbnail"/> 的「小图不放大」规则：
    /// SVG 的 CullRect 通常是它自己的坐标系（`viewBox="0 0 24 24"` 这种），按 24 渲染再显示到 128
    /// 就是「低分辨率放大」，边缘糊、细线断。矢量放大没有画质损失，所以这里反过来 —— 小于目标尺寸也放大。
    /// </remarks>
    private static Bitmap? RenderSvg(byte[] bytes, int size)
    {
        using var svgStream = new MemoryStream(bytes, writable: false);

        var svg = new Svg.Skia.SKSvg();
        svg.Load(svgStream);

        var picture = svg.Picture;
        if (picture is null)
            return null;

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        var (targetWidth, targetHeight) = ScaleToSize(
            (int)Math.Ceiling(bounds.Width),
            (int)Math.Ceiling(bounds.Height),
            size);

        using var surface = SKSurface.Create(new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
            return null;

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(targetWidth / bounds.Width, targetHeight / bounds.Height);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = surface.Snapshot();
        return Encode(image);
    }

    /// <summary>等比缩放到最长边不超过 <paramref name="size"/>（位图小图不放大）。</summary>
    private static (int Width, int Height) ScaleToThumbnail(int width, int height, int size)
    {
        var longest = Math.Max(width, height);
        if (longest <= size)
            return (Math.Max(1, width), Math.Max(1, height));

        return ScaleToSize(width, height, size);
    }

    /// <summary>等比缩放到最长边恰好等于 <paramref name="longestSide"/>（小于也放大，供矢量图用）。</summary>
    private static (int Width, int Height) ScaleToSize(int width, int height, int longestSide)
    {
        var longest = Math.Max(width, height);
        if (longest <= 0)
            return (Math.Max(1, longestSide), Math.Max(1, longestSide));

        var scale = (double)longestSide / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static Bitmap? Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        return Encode(image);
    }

    private static Bitmap? Encode(SKImage image)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
            return null;

        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    /// <summary>缓存键：压缩包路径 + 条目全路径 + 缩略图边长（不同包里同名条目、不同尺寸都不能互相串）。</summary>
    private string BuildCacheKey(ArchiveItemModel item) =>
        string.Concat(_archivePath, "|", item.FullPath, "|", _thumbnailSize.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private static void Log(string message) => App.DebugLog(message);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Cancel();

        try
        {
            _batchCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // 忽略重复释放。
        }

        _batchCts = null;
    }
}
