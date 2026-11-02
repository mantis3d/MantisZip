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
    /// <summary>缩略图最长边（像素）。</summary>
    public const int ThumbnailSize = 128;

    /// <summary>
    /// 从 ICO 里挑帧时「期望边长」——**故意大于 <see cref="ThumbnailSize"/>**。
    /// </summary>
    /// <remarks>
    /// 老图标常见「只有 256 与 16 两帧」，若按 128 找最接近的帧，256 差 128、16 差 112 ⇒ 会选中 16 帧，
    /// 放大到 128 糊成一团。取 160 时 256 只差 96、16 差 144 ⇒ 选中 256 帧，缩到 128 反而清晰。
    /// 一句话：宁可拿大帧缩小，也不要拿小帧放大。
    /// </remarks>
    public const int IcoThumbnailPreferredSide = 160;

    /// <summary>单个条目允许生成缩略图的大小上限（8MB），超过直接跳过。</summary>
    public const long MaxThumbnailEntrySize = 8L * 1024 * 1024;

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
    private readonly SemaphoreSlim _throttle = new(MaxConcurrency, MaxConcurrency);

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
    public ThumbnailService(string archivePath, string? password, bool isSevenZipSolid, ThumbnailCache? cache = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        _archivePath = archivePath;
        _password = password;
        _isSevenZipSolid = isSevenZipSolid;
        _cache = cache ?? new ThumbnailCache();
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

                // 图像：超大条目 / 7z 固实包直接跳过（固实包读一条 = 整包解压）。
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

        var bitmap = DecodeImage(bytes, Path.GetExtension(item.Name));
        return bitmap is null ? null : new ThumbnailResult(bitmap, null);
    }

    /// <summary>
    /// 字节 → 128px 缩略图。<c>.ico</c> 走多尺寸图标提取（取边长最接近 160 的帧，不是列表图标的 16），
    /// <c>.svg</c> 走矢量渲染（矢量按 128 重绘，不受其声明尺寸限制），其余走 Skia 位图解码。
    /// </summary>
    private static Bitmap? DecodeImage(byte[] bytes, string extension)
    {
        // 缩略图卡是 128px，但这里**故意要 160**：ico 里常见「只有 256 和 16 两帧」的老图标，
        // 若按 128 找最接近，256 差 128、16 差 112 ⇒ 会选中 16 帧（放大到 128 糊成一团）；
        // 要 160 时 256 只差 96、16 差 144 ⇒ 选中 256 帧（缩小到 128，清晰）。
        // 一句话：宁可拿大帧缩小，也不要拿小帧放大。
        if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            return IcoIconHelper.ExtractIcon(bytes, IcoThumbnailPreferredSide);

        // 项目已引用 Svg.Skia（MantisZip.UI.Avalonia.csproj），矢量图单独渲染成位图，
        // 否则 SKBitmap.Decode 对 SVG 只会返回 null。
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            return RenderSvg(bytes);

        using var source = SKBitmap.Decode(bytes);
        if (source is null || source.Width <= 0 || source.Height <= 0)
            return null;

        var (targetWidth, targetHeight) = ScaleToThumbnail(source.Width, source.Height);

        // 不需要缩放（小图）就直接编码，省一次重绘。
        if (targetWidth == source.Width && targetHeight == source.Height)
            return Encode(source);

        using var target = new SKBitmap(new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(targetWidth / (float)source.Width, targetHeight / (float)source.Height);
            canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            canvas.Flush();
        }

        return Encode(target);
    }

    /// <summary>
    /// 用 Svg.Skia 把矢量图渲染成最长边恰好 <see cref="ThumbnailSize"/> 的位图。
    /// </summary>
    /// <remarks>
    /// 矢量图**必须按目标尺寸重绘**，不能沿用 <see cref="ScaleToThumbnail"/> 的「小图不放大」规则：
    /// SVG 的 CullRect 通常是它自己的坐标系（`viewBox="0 0 24 24"` 这种），按 24 渲染再显示到 128
    /// 就是「低分辨率放大」，边缘糊、细线断。矢量放大没有画质损失，所以这里反过来 —— 小于目标尺寸也放大。
    /// </remarks>
    private static Bitmap? RenderSvg(byte[] bytes)
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
            ThumbnailSize);

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

    /// <summary>等比缩放到最长边不超过 <see cref="ThumbnailSize"/>（位图小图不放大）。</summary>
    private static (int Width, int Height) ScaleToThumbnail(int width, int height)
    {
        var longest = Math.Max(width, height);
        if (longest <= ThumbnailSize)
            return (Math.Max(1, width), Math.Max(1, height));

        return ScaleToSize(width, height, ThumbnailSize);
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

    /// <summary>缓存键：压缩包路径 + 条目全路径（不同包里同名条目不能互相串）。</summary>
    private string BuildCacheKey(ArchiveItemModel item) =>
        string.Concat(_archivePath, "|", item.FullPath);

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
