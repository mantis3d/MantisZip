using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 单个缩略图结果：图像卡用 <see cref="Bitmap"/>，文字卡用 <see cref="Text"/>。
/// 两者都为空的结果视为「这次没做出来」（失败），调用方应当回退到文件图标。
/// </summary>
/// <param name="Bitmap">图像类条目的 128px 缩略图；文字类条目为 <see langword="null"/>。</param>
/// <param name="Text">文字类条目的文字片段；图像类条目为 <see langword="null"/>。</param>
public sealed record ThumbnailResult(Bitmap? Bitmap, string? Text);

/// <summary>
/// 文件列表缩略图的有界 LRU 缓存。
///
/// <para>
/// 容量默认 <see cref="DefaultCapacity"/> 项：300 × 128 × 128 × 4 字节 ≈ 19MB，
/// 对「一张缩略图 ~64KB」的保守估算可以接受；超出后按最近最少使用逐出最旧的一项。
/// </para>
/// <para>
/// 并发去重：同一 key 同时有多个请求时只调用一次工厂（用 <see cref="Lazy{T}"/> 包住 Task，
/// 保证即使 <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})"/>
/// 在竞争下重复执行 value 工厂，真正的解码工厂也只跑一次）。
/// 失败（<see langword="null"/>）结果不缓存，下次请求会重新尝试。
/// </para>
/// <para>
/// 线程模型：条目表与 LRU 链表由单把锁保护；工厂在锁外执行，不会阻塞其它 key。
/// 缓存内的 <see cref="Bitmap"/> 是共享只读对象，<see cref="Clear"/> 不做 Dispose
/// （视图可能仍持有引用，释放交给 GC）。
/// </para>
/// </summary>
public sealed class ThumbnailCache
{
    /// <summary>默认容量（项）。</summary>
    public const int DefaultCapacity = 300;

    private readonly int _capacity;
    private readonly object _gate = new();

    // 命中数据 + LRU 顺序：链表头 = 最近使用，链表尾 = 最久未用（逐出目标）。
    private readonly Dictionary<string, ThumbnailResult> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();

    // 同 key 并发去重：value 是「只执行一次的 Task 工厂」。
    private readonly ConcurrentDictionary<string, Lazy<Task<ThumbnailResult?>>> _inflight =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 创建缓存。
    /// </summary>
    /// <param name="capacity">容量（项），必须 &gt; 0；默认 300。</param>
    public ThumbnailCache(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "缓存容量必须大于 0");

        _capacity = capacity;
    }

    /// <summary>当前缓存项数（诊断/测试用）。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    /// <summary>
    /// 取缓存；未命中时调用 <paramref name="factory"/> 生成并缓存（成功结果）。
    /// </summary>
    /// <param name="key">缓存键（调用方负责保证唯一，例如「压缩包路径|条目全路径」）。</param>
    /// <param name="factory">生成缩略图的工厂；在后台线程执行，返回 <see langword="null"/> 表示失败。</param>
    /// <param name="ct">取消令牌：只影响当前等待，不影响同 key 的其它等待者。</param>
    /// <returns>缓存命中或工厂生成的结果；失败为 <see langword="null"/>。</returns>
    public Task<ThumbnailResult?> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<ThumbnailResult?>> factory,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ct.ThrowIfCancellationRequested();

        if (TryGetCached(key, out var cached))
            return Task.FromResult(cached);

        var lazy = _inflight.GetOrAdd(
            key,
            static (k, state) => new Lazy<Task<ThumbnailResult?>>(
                () => state.Cache.CreateAsync(k, state.Factory, state.Token),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (Cache: this, Factory: factory, Token: ct));

        return AwaitSharedAsync(lazy, ct);
    }

    /// <summary>
    /// 清空缓存（换包 / 关包时调用）。不影响仍在生成中的任务：它们完成后会重新写入。
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
        }
    }

    /// <summary>读缓存并刷新 LRU 顺序（命中即变为最近使用）。</summary>
    private bool TryGetCached(string key, out ThumbnailResult? result)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var hit))
            {
                result = null;
                return false;
            }

            var node = _order.Find(key);
            if (node is not null)
            {
                _order.Remove(node);
                _order.AddFirst(node);
            }

            result = hit;
            return true;
        }
    }

    /// <summary>
    /// 真正执行工厂并回填缓存；同一 key 的所有等待者共享这一个 Task。
    /// </summary>
    private async Task<ThumbnailResult?> CreateAsync(
        string key,
        Func<CancellationToken, Task<ThumbnailResult?>> factory,
        CancellationToken ct)
    {
        try
        {
            // 二次检查：进入工厂前别人可能刚好填好（避免抢占失败时白解码一次）。
            if (TryGetCached(key, out var cached))
                return cached;

            var result = await factory(ct).ConfigureAwait(false);

            // 失败结果（null / 两个字段都空）不缓存：下次请求重试，不把「这次没解出来」固化。
            if (result is null || (result.Bitmap is null && string.IsNullOrEmpty(result.Text)))
                return null;

            lock (_gate)
                Store(key, result);

            return result;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    /// <summary>写入新条目并逐出最久未用的条目（调用方必须持有 <see cref="_gate"/>）。</summary>
    private void Store(string key, ThumbnailResult value)
    {
        if (_entries.ContainsKey(key))
        {
            var existing = _order.Find(key);
            if (existing is not null)
                _order.Remove(existing);
        }

        _entries[key] = value;
        _order.AddFirst(key);

        while (_order.Count > _capacity)
        {
            var last = _order.Last;
            if (last is null)
                break;

            _order.RemoveLast();
            _entries.Remove(last.Value);
        }
    }

    /// <summary>
    /// 等待共享任务，同时让当前调用方的 <paramref name="ct"/> 能立刻脱身
    /// （等待者取消不牵连其它等待者，也不杀共享的解码任务）。
    /// </summary>
    private static async Task<ThumbnailResult?> AwaitSharedAsync(
        Lazy<Task<ThumbnailResult?>> lazy,
        CancellationToken ct)
    {
        var task = lazy.Value;
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }
}
