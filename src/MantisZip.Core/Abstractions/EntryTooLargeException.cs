namespace MantisZip.Core.Abstractions;

/// <summary>
/// 把**整条**压缩包条目读进内存时，实际大小超过调用方给定的上限
/// （<see cref="MantisZip.Core.Utils.ArchiveEntryExtractor.ExtractEntryToMemoryAsync"/> 的 <c>maxBytes</c>），
/// 或者超过单个 <see cref="byte"/> 数组能容纳的长度。
/// <para>
/// 存在的意义是**消灭「静默返回截断数据」**：head 截断（<c>ExtractHeadAsync</c>）语义上就是
/// 「给我前 N 字节」，多出来的丢掉没问题；而整条目进内存的消费方（SkiaSharp 解码、PdfPig 打开文档）
/// 拿到残缺字节只会得到一个无法解释的解析失败。因此超限必须显式失败，并且带上
/// <see cref="ActualSize"/> / <see cref="Limit"/>，让上层能决定提示文案（「文件太大」）而不是当成损坏。
/// </para>
/// <para>
/// 与 <see cref="ArchiveEntryNotFoundException"/> 一样声明为 <c>sealed</c>、路径属性名为
/// <see cref="EntryPath"/>，便于调用方按同一套模式识别「条目级」失败。
/// 消息用英文：这是给开发者看的诊断异常（Core 其余面向用户的异常消息是中文，
/// 两者并存；上层若要给用户看，应按 <see cref="ActualSize"/> / <see cref="Limit"/> 自行本地化）。
/// </para>
/// </summary>
public sealed class EntryTooLargeException : Exception
{
    /// <summary>初始化「条目超过内存上限」异常。</summary>
    /// <param name="entryPath">超限的条目路径（包内路径）。</param>
    /// <param name="actualSize">条目实际字节数；无法确定时传 <c>-1</c>。</param>
    /// <param name="limit">调用方给出的上限（或单个 <c>byte[]</c> 的容量上限，取较小者）。</param>
    public EntryTooLargeException(string entryPath, long actualSize, long limit)
        : base($"Entry too large to load into memory: {entryPath} ({actualSize} bytes, limit {limit})")
    {
        EntryPath = entryPath;
        ActualSize = actualSize;
        Limit = limit;
    }

    /// <summary>超限的条目路径（包内路径，与调用方传入的 <c>entryName</c> 一致）。</summary>
    public string EntryPath { get; }

    /// <summary>条目实际字节数；取数路径无法得知真实大小时为 <c>-1</c>。</summary>
    public long ActualSize { get; }

    /// <summary>本次调用允许的字节上限；条目大于单个 <c>byte[]</c> 容量时为该容量。</summary>
    public long Limit { get; }
}
