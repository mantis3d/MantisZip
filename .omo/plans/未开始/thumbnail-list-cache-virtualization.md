# 缩略图/图标缓存与列表惰性分批 (thumbnail-list-cache-virtualization)

> **档位**: B 档 → 待排期（2026-11-06 立项；排期决定权在用户，可升入某版本阶段）
> status: 📋 **未开始**（Phase 1 为必做部分，Phase 2 为可选部分，见文末）
> 创建日期: 2026-11-06
> 适用范围: 仅 `MantisZip.UI.Avalonia`（规则 11）
> 依赖: 无（均为 file-list-thumbnails 已完成功能的直接修订，见其「已知改进口子」三条）
> 执行分支: `beta`（缺陷修复/性能优化分支，规则 19）
> 阶段核对（规则 1）: 本计划不绑定版本阶段、**不触发版本号变更**（规则 2）；开工前不需核对版本，但若用户决定把它升入某 A 档阶段，则按该阶段规则核对 `AppConstants.Version`

## 背景与目标

`file-list-thumbnails.md`（v0.5.6 已完成）的实施记录末尾登记了三个「已知改进口子」：

| # | 问题 | 现象 |
|---|------|------|
| 1 | 缩略图缓存不跨包共享 | VM 每次换包 `new ThumbnailService(..., cache: null)` 自建 LRU，切目录/切包丢缓存；来回切两个包、返回访问过的目录，所有缩略图重新解码 |
| 2 | Tile/Content 无 UI 虚拟化 | Avalonia 12 没有 ItemsRepeater（实施时硬推翻了原计划），`ItemsControl + WrapPanel` 一次性实例化全部条目容器；超大目录切进卡片布局瞬间铺死 |
| 3 | ICO 列表图标无缓存 | `IcoIconLoader` 静态无状态；筛选输入每敲一个字重建列表 → 最多 20 个 `.ico` 条目重新读包 + 解码 |

**目标**：Phase 1 修掉 #1 与 #3，把 #2 用「分批惰性铺条」缓解到实际不可感；Phase 2（可选、单独决策）补真虚拟化面板。

**为什么这么分段**（2026-11-06 用户访谈结论）：
- #1 与 #3 是纯缓存层改动（各 ~0.5 天），前置管道都已存在（`ThumbnailService` 本来就有 `cache:` 注入参数、`PreviewService` 已有可照抄的 head cache LRU 模式），先落袋。
- #2 方案 A（分批批条）不依赖 Avalonia 内部虚拟化机制、不碰渲染管线，1~1.5 天见效；方案 B（自写 VirtualizingPanel）3~5 天且是最深水区。A 无论如何不是 wasted work（B 也要同样的批量暴露层——至少 VM 侧契约会复用）。
- B 是否值得投入，等 Phase 1 真机 QA 的数据说了算（见文末 Phase 2 章）。

## 已确认决策（2026-11-06 访谈）

| # | 决策 | 结论 |
|---|------|------|
| 1 | 修复范围 | #1 + #3 + #2 方案 A，同一计划分阶段；Phase 2（方案 B）留档待用户在 Phase 1 QA 后决策 |
| 2 | #2 形态 | 分批惰性铺条（每批 200 项，滚动接近底部追加），不是真虚拟化；详情布局（DataGrid 自带行虚拟化）整表照绑不变 |
| 3 | 版本号 | 不变更（规则 2） |

## 已验证事实（2026-11-06 逐项核对）

| 项 | 事实 | 位置 |
|----|------|------|
| 缩略图服务缓存参数（**已有，勿重写**） | `ThumbnailService` 构造第 4 参 `ThumbnailCache? cache = null`；`Dispose()` 只 `Cancel()` + `_batchCts?.Dispose()`，**不碰 `_cache`** ⇒ 注入共享缓存即插即用 | `Services/ThumbnailService.cs:94-109` / `:445-463` |
| 缓存 key 维度（**够用**） | `BuildCacheKey` = `包路径|条目全路径|缩略图边长` ⇒ 一个缓存实例跨包、跨尺寸都安全 | `ThumbnailService.cs:431-432` |
| `ClearCache()` 生产调用点 | **0 个**（grep 全 src 只命中定义处） ⇒ 共享化不会破坏任何现有换包清缓存逻辑 | `ThumbnailService.cs:167` |
| VM 现状（**要改的一处**） | `StartThumbnailBatchIfNeeded` 内 `cache: null`（:1986）；字段 `_thumbnailService`/`_thumbnailServiceArchivePath`/`_thumbnailServiceSize` | `ViewModels/MainWindowViewModel.cs:1953-1993` |
| 批次数据源 | `StartThumbnailBatchIfNeeded` 现传**全量** `CurrentEntries`（:1992） ⇒ 方案 A 落地后应改为已铺前缀 `VisibleEntries`，否则铺条没有任何省 I/O 效果 | `MainWindowViewModel.cs:1974/1992` |
| `Clear` 语义 | `ThumbnailCache.Clear()` 不 Dispose 位图（视图可能仍持有引用）⇒ 共享共享的 Bitmap 无释放风险（先例） | `Services/ThumbnailCache.cs:29-31` |
| ICO 加载钩子 | `PopulateEntries` 末尾 `ReloadOwnIcoIcons()`；每次调用 `_iconLoadCts` 取消上一批；候选循环在 `Task.Run` 里逐个 `ExtractEntryToMemoryAsync` + `IcoIconHelper.ExtractIcon(raw)`，取消时对解出的 `icon.Dispose()` | `ViewModels/MainWindowViewModel.Navigation.cs:236-258`；`Services/IcoIconLoader.cs:76-128` |
| ICO 缓存安全要点 | **取消路径会 `icon.Dispose()`** —— 缓存命中的位图是共享对象，绝不能走这个 Dispose ⇒ 命中路径必须与解码路径分开标记 | `IcoIconLoader.cs:99-114` |
| LRU 先例（**可照抄**） | `PreviewService` 的 head cache：`Dictionary + LinkedList` + 单锁 + 容量逐出 + `ResetHeadCacheForTests` 访问器模式 | `Services/PreviewService.cs:243-256`、`646-760` |
| 卡片布局绑定 | Tile 与 Content 的 `ItemsControl` 均绑 `ItemsSource="{Binding CurrentEntries}"` | `Controls/ArchiveContentView.axaml:643` / `:743` |
| 容器接线 | `HookThumbnailLayoutContainer(name)` 按 x:Name 订阅 Pressed/Moved/Released/KeyDown/ContextRequested/DoubleTapped；容器即 `TileLayoutScrollViewer` / `ContentLayoutScrollViewer` 两个 `ScrollViewer` | `Views/MainWindow.Thumbnails.cs:68-103` |
| UI 集合重建方式 | `CurrentEntries` 是**单实例** `ObservableCollection`，重填走 `Clear()` + `Add()`；所有重填最后都走到 `PopulateEntries` | `MainWindowViewModel.cs:855`；`Navigation.cs` |
| 测试基建 | `tests/MantisZip.UI.Avalonia.Tests/ThumbnailTests.cs` 47 例（分类/截断/缓存/选择），纯逻辑测试无需真实档案；`InternalsVisibleTo` 已通（`PreviewService.ResetHeadCacheForTests` 被测试访问） | `tests/.../ThumbnailTests.cs` |
| 构建测试命令 | `pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia`；`-Task test -Target ui-tests`（规则 12） | `scripts/verify-repo.ps1` |

## 设计

### 1. #1：共享 ThumbnailCache（T1）

VM 持有一个 **App 生命周期**的 `ThumbnailCache` 实例，换包传入。缓存 key 已含包路径与尺寸 ⇒ 不需要任何「换包清缓存」动作；上游 `ThumbnailService.Subscribe` 语义零改动。

```csharp
// MainWindowViewModel.cs —— 字段区（_thumbnailService，1953 行附近）

/// <summary>
/// 跨包共享的缩略图 LRU 缓存（App 生命周期）：缓存 key =「包路径|条目|尺寸」，
/// 切包 / 切目录不会互相串图，重进访问过的包/目录直接命中。容量 300 上限，内存可控。
/// </summary>
private readonly ThumbnailCache _sharedThumbnailCache = new();
```

```csharp
// StartThumbnailBatchIfNeeded 内（1982-1987 行），cache: null → 注入
_thumbnailService = new ThumbnailService(
    archivePath,
    GetSessionPassword(archivePath) ?? _currentPassword,
    IsCurrentSevenZipSolid(archivePath),
    cache: _sharedThumbnailCache,
    thumbnailSize: size);
```

注释同步：`_thumbnailService`（1950-1952 行）与 `CancelThumbnailBatch`（2024-2026 行）的「缓存归服务所有，Dispose 后即释放」改成「缓存为 VM 共享实例，服务 Dispose 不影响缓存」。

`ThumbnailService.cs` 补一个测试访问器（照 `PreviewService.HeadCacheCountForTests` 先例）：

```csharp
/// <summary>测试/诊断用：当前实例使用的缓存实例（注入共享缓存时即调用方实例）。</summary>
internal ThumbnailCache CacheForTests => _cache;
```

`cache:` 参数的 XML 注释（:89）补一句：「传入的缓存所有权在调用方，本服务 Dispose 不会清它」。

### 2. #3：IcoIconCache（T2）

新增 `Services/IcoIconCache.cs`——比 `ThumbnailCache` 简单的 LRU（无并发去重：加载本身就是单批串行 + 跨批被 `Cancel` 串行化）：

```csharp
using Avalonia.Media.Imaging;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// ICO 列表图标（自身图标，非卡片缩略图）的有界 LRU 缓存。
/// key =「压缩包路径|条目全路径」。命中时不读包：筛选输入每敲一个字都重建列表，
/// 没有这层缓存时最多 20 个 .ico 条目每次筛选都重新解压 + 解码。
/// 容量默认 128；单枚 ICO 位图只有几 KB，容量上界内存可忽略，关包无需手动清。
/// 线程模型：加载单批串行、跨批被取消串行化；锁只是防御边界，不做并发去重。
/// </summary>
internal sealed class IcoIconCache
{
    /// <summary>默认容量（项）。</summary>
    public const int DefaultCapacity = 128;

    private readonly int _capacity;
    private readonly object _gate = new();

    // 命中数据 + LRU 顺序：链表头 = 最近使用，链表尾 = 最久未用（逐出目标）。
    private readonly Dictionary<string, Bitmap> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();

    public IcoIconCache(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "缓存容量必须大于 0");
        _capacity = capacity;
    }

    /// <summary>当前缓存项数（诊断/测试用）。</summary>
    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>命中刷新 LRU 顺序。缓存内 Bitmap 为共享只读对象，逐出不 Dispose（视图可能仍持有引用）。</summary>
    public bool TryGet(string key, out Bitmap? bitmap)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var hit))
            {
                bitmap = null;
                return false;
            }
            var node = _order.Find(key);
            if (node is not null)
            {
                _order.Remove(node);
                _order.AddFirst(node);
            }
            bitmap = hit;
            return true;
        }
    }

    /// <summary>只缓存成功结果；超过容量逐出链表尾（最久未用）。</summary>
    public void Add(string key, Bitmap bitmap)
    {
        lock (_gate)
        {
            if (_entries.ContainsKey(key))
                return;
            _entries[key] = bitmap;
            _order.AddFirst(key);
            while (_order.Count > _capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _entries.Remove(last.Value);
            }
        }
    }
}
```

`IcoIconLoader` 改造要点（`Services/IcoIconLoader.cs`）：

1. 静态缓存字段 + 测试访问器：

```csharp
private static readonly IcoIconCache Cache = new();

/// <summary>测试/诊断用：加载器的共享图标缓存。</summary>
internal static IcoIconCache CacheForTests => Cache;
```

2. **候选分类提前到 `Task.Run` 之外**（`LoadAsync` 本身就在 UI 线程被调用）——命中的条目当场回填，没有后台线程往返，也没有取消时序问题：

```csharp
// 把现有 candidates 循环改成「命中即填 + 未命中进 pending」。
// 语义钉子：maxIcons 的口径是「考虑过的 .ico 条目数」——命中与未命中**都要**计数，
// 否则二次筛选会悄悄把上限放宽成「20 个未命中的」⇒ 实际回填数 > 现状。
int seen = 0;

foreach (var model in models)
{
    if (seen >= maxIcons)
        break;
    if (model.IsDirectory) continue;
    if (!string.Equals(Path.GetExtension(model.Name), IcoExtension, StringComparison.OrdinalIgnoreCase))
        continue;
    seen++;
    if (model.Size > MaxIcoBytes) continue;

    var cacheKey = string.Concat(archivePath, "|", model.FullPath);
    if (Cache.TryGet(cacheKey, out var cached) && cached is not null)
    {
        // 命中：不读包不解码。缓存位图是共享只读对象，任何路径都不得 Dispose 它
        Post(() => model.IconSource = cached);
        continue;
    }

    candidates.Add(model);   // 未命中：照旧交后台线程解码（原逻辑不变）
}

if (candidates.Count == 0) return;
```

3. `Task.Run` 循环保持原样（只有未命中条目会进来）。唯一安全改动：解码成功后**同时写入缓存**：

```csharp
var icon = IcoIconHelper.ExtractIcon(raw);
if (icon == null)
    continue;

Cache.Add(cacheKey, icon);   // 新增：解码成功才入缓存（失败不缓存，复刻 ThumbnailCache 语义）
```

   之后的取消/回填逻辑（`Dispatcher.UIThread.Post` + 取消时 `icon.Dispose()`）**保持不变**——刚解码的位图是本批独占对象，可以 Dispose；缓存里的对象共享，绝不能 Dispose。这条红线在两类代码处都要有注释钉住。

4. 把第 105 行的裸 `Dispatcher.UIThread.Post` 换成与 `ThumbnailService.Post` 同型的辅助方法（命中路径在 UI 线程调用时同步执行，测试才可断言）：

```csharp
private static void Post(Action action)
{
    if (Dispatcher.UIThread.CheckAccess())
        action();
    else
        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
}
```

   行为等价性：生产里 `LoadAsync` 从 UI 线程调用（`ReloadOwnIcoIcons`），命中路径本来就同步；`Task.Run` 循环里的浮动回填经 Post 语义和原来一致（仅优先级从 Background 显式化，无变化）。
   若 `DispatcherPriority` 已在现有 using 内（第 1 行已有 `Avalonia.Threading`）无需新增。

### 3. #2 方案 A：分批惰性铺条（T3–T5）

**分层**：`VisibleEntries`（视图实绑）= `CurrentEntries` 的已铺前缀。选择/勾选/排序/删除/键盘/右键的**数据事实源仍是 CurrentEntries 全量**——`ThumbnailSelectionController` 接 `CurrentEntries`、`RestoreCheckedState` 等一律不动，所以跨批的 Shift 范围选择天然正确。

新增 `Services/VisibleChunkSource.cs`（纯逻辑，可单测）：

```csharp
namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Tile / Content 布局的惰性铺条源：把 CurrentEntries 分批暴露给视图。
/// ItemsControl + WrapPanel 没有 UI 虚拟化 ⇒ 万级条目一次性实例化全部容器会卡死；
/// 分批后任何时刻只有已铺前缀有容器，滚动接近底部再追加下一批。
/// 详情布局不经过本类：DataGrid 自带行虚拟化，恒绑 CurrentEntries 全量。
/// </summary>
public sealed class VisibleChunkSource
{
    /// <summary>每批铺多少条。</summary>
    public const int ChunkSize = 200;

    /// <summary>距底部多少像素内触发追加（约两行卡片）。</summary>
    public const double AppendScrollThresholdPx = 400;

    private readonly Func<IReadOnlyList<ArchiveItemModel>> _source;
    private readonly Action<ArchiveItemModel> _onMaterialize;
    private int _count;

    /// <param name="source">全量数据源（工厂形式取快照：集合会被重填 Clear/Add）。</param>
    /// <param name="onMaterialize">把一条已铺条目暴露给视图（VM 侧 = VisibleEntries.Add）。</param>
    public VisibleChunkSource(
        Func<IReadOnlyList<ArchiveItemModel>> source,
        Action<ArchiveItemModel> onMaterialize)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _onMaterialize = onMaterialize ?? throw new ArgumentNullException(nameof(onMaterialize));
    }

    /// <summary>当前已铺数量（诊断/测试用）。</summary>
    public int MaterializedCount => _count;

    /// <summary>
    /// 重置并铺第一批。**不负责清空目标集合**：调用方要先 Clear（见 VM 的 ResetVisibleEntries），
    /// 因为 CurrentEntries 是单实例集合被重填，Reset 必须读到重填后的列表。
    /// </summary>
    public void Reset()
    {
        var source = _source();
        _count = Math.Min(ChunkSize, source.Count);
        for (var i = 0; i < _count; i++)
            _onMaterialize(source[i]);
    }

    /// <summary>追加下一批；返回 false 表示已全部铺完（本次没有任何追加）。</summary>
    public bool AppendNext()
    {
        var source = _source();
        if (_count >= source.Count)
            return false;

        var next = Math.Min(_count + ChunkSize, source.Count);
        for (var i = _count; i < next; i++)
            _onMaterialize(source[i]);
        _count = next;
        return true;
    }
}
```

VM 接线（`MainWindowViewModel.cs`）：

```csharp
// 字段区（1947 行前后）

/// <summary>Tile/Content 反绑的数据集 = CurrentEntries 的已铺前缀（惰性分批，详情布局未使用）。</summary>
public ObservableCollection<ArchiveItemModel> VisibleEntries { get; } = [];

private readonly VisibleChunkSource _visibleChunk;

// 构造函数（或零参字段初始化不具备的位置）：CurrentEntries 在 :855 定义后即可构造
_visibleChunk = new VisibleChunkSource(() => CurrentEntries, item => VisibleEntries.Add(item));

/// <summary>重铺已铺前缀：列表重填 / 切卡片布局后调用（详情布局未使用，调用无害）。</summary>
public void ResetVisibleEntries()
{
    VisibleEntries.Clear();
    _visibleChunk.Reset();
}

/// <summary>滚动接近底部时追加下一批（MainWindow.Thumbnails.cs 的 ScrollChanged 回调入口）。</summary>
public void AppendVisibleEntriesChunk()
{
    if (_visibleChunk.AppendNext())
        StartThumbnailBatchIfNeeded();   // 新铺条目补缩略图（已缓存项在服务内直接跳过）
}
```

`StartThumbnailBatchIfNeeded` 两处改动（批次只处理**已铺**条目，铺条才能省掉屏幕外解码）：

```csharp
// :1974  if (CurrentEntries.Count == 0) return;   →
if (VisibleEntries.Count == 0) return;

// :1992  _thumbnailService.StartBatch(CurrentEntries);   →
// 传快照：批次在后台线程遍历，UI 线程的滚动追加会并发改集合
_thumbnailService.StartBatch(VisibleEntries.ToArray());
```

`PopulateEntries` 末尾钩子（`MainWindowViewModel.Navigation.cs:217-221`，`ReloadOwnIcoIcons()` 与 `StartThumbnailBatchIfNeeded()` 之间插入）：

```csharp
// 卡片布局惰性铺条：先按新列表重铺前缀，再按需启动批次（批次只处理已铺条目）
// 位置必须在 StartThumbnailBatchIfNeeded 之前，否则批次会按旧集合跑
ResetVisibleEntries();
```

切换布局模式（`OnLayoutModeChanged`，现有属性通知集中处追加）：

```csharp
// 切到卡片布局时重铺（详情切回或模式间循环）；详情布局 VisibleEntries 未使用、调用无害
if (value != FileListLayoutMode.Details)
{
    ResetVisibleEntries();
    StartThumbnailBatchIfNeeded();
}
```

XAML（`Controls/ArchiveContentView.axaml`）：第 643 行与第 743 行的 `ItemsSource="{Binding CurrentEntries}"` → `ItemsSource="{Binding VisibleEntries}"`；两处容器头注释（590-597 / 695 起）补一句「条目为惰性分批铺条：ScrollViewer 接近底部时经 `AppendVisibleEntriesChunk` 追加下一批」。

代码后置（`Views/MainWindow.Thumbnails.cs`，`HookThumbnailLayoutContainer` 内 :103 的 `viewer.DoubleTapped` 之前追加）：

```csharp
// 惰性铺条：滚动接近底部时向 VM 追加下一批（阈值 ~两行卡片，见 VisibleChunkSource）
viewer.ScrollChanged += (_, _) =>
{
    if (viewer.ExtentHeight <= 0)
        return;
    if (viewer.VerticalOffset + viewer.ViewportHeight >= viewer.ExtentHeight - VisibleChunkSource.AppendScrollThresholdPx
        && DataContext is MainWindowViewModel lazyVm)
    {
        lazyVm.AppendVisibleEntriesChunk();
    }
};
```

回归语义核对（实施时逐条过一遍，均应保持不变）：

- `CheckAllVisible`（复选框模式「勾选所有可见项」）按**全列表**（`CurrentEntries`）勾选——「可见」在此语境=「筛选后列表内」，不是「已铺」；不改。
- 选择控制器、拖拽命中、删除、双击、Enter/Back/Delete 全部工作在模型层数据，与容器是否已铺无关。
- 已铺条目被删除后的列表重填永远走 `PopulateEntries ⇒ ResetVisibleEntries`，不存在「已铺前缀过期」的实现路径。

## 任务分解

| # | 任务 | 文件 | 验收标准 |
|---|------|------|---------|
| T1 | 共享 `ThumbnailCache` 注入 + 注释同步 + `CacheForTests` + 2 例单测 | `ViewModels/MainWindowViewModel.cs:1953-1993`、`:2024-2037`；`Services/ThumbnailService.cs:89`、`:431` 附近；`tests/.../ThumbnailTests.cs` | 重复打开同一个包第二次不重新解码（DebugLog 无重复「生成缩略图」输出）；切包来回命中；`service.Dispose()` 后缓存仍在 |
| T2 | `IcoIconCache` 新类 + `IcoIconLoader` 命中短路 + 缓存回写 + Post 辅助方法 + 单测 | `Services/IcoIconCache.cs`（新）、`Services/IcoIconLoader.cs`、`tests/.../ThumbnailTests.cs`（或同目录新文件 `IcoIconCacheTests.cs`） | 单测通过：未命中/命中/LRU 逐出/共享位图不被 Dispose 语义；「archive 不存在但缓存命中 ⇒ 回填成功」的集成位；筛选连续输入时日志出现 `IcoIconLoader: 跳过` 数骤减（原第 2 次起命中不读包） |
| T3 | `VisibleChunkSource` 新类 + VM `VisibleEntries`/`ResetVisibleEntries`/`AppendVisibleEntriesChunk` + 批次源切换 + `PopulateEntries`/`OnLayoutModeChanged` 钩子 + 4 例单测 | `Services/VisibleChunkSource.cs`（新）、`ViewModels/MainWindowViewModel.cs`、`MainWindowViewModel.Navigation.cs`、`tests/.../VisibleChunkSourceTests.cs` | 单测通过：Reset 铺 min(200,count)、AppendNext 追加/到尾 false、前缀有序 |
| T4 | XAML 两处绑定换 `VisibleEntries` + 容器 `ScrollChanged` 接线 | `Controls/ArchiveContentView.axaml:643`、`:743`、`:590-597` 注释、`:695` 注释；`Views/MainWindow.Thumbnails.cs:78-103` | Tile/Content 首屏只实例化 ≤200 容器；滚动到底分批出现新条目；Details 全量不变 |
| T5 | 缩略图批次改持有已铺前缀（并入 T3 实施，验收单列） | 同 T3 | 打开 ≥2000 条目的包：Tile 切入瞬间可交互；缩略图只对已铺条目生成；滚动追加后新条目补图 |
| T6 | 文档双轨（规则 3）+ 构建/测试/QA + 本计划标注 ✅ + PHASE2 决策材料 | `docs/PROGRESS.md`、`docs/progress-avalonia-detail.md`、本文件 | 见下方 QA 清单 |

**预估工时**：

| 部分 | 工时 |
|------|------|
| T1 + T2（缓存类） | ~1 天 |
| T3–T5（惰性铺条） | 1~1.5 天 |
| T6（文档 + QA） | ~0.5 天 |
| **Phase 1 合计** | **2~3 天**（含验证不含变更日志成文时间） |

提交粒度：每个 T 一次 conventional commit（`refactor(avalonia)` / `perf(avalonia)` / `fix(avalonia)` 前缀），TDD 红灯先行（先写测试跑失败再实现）。

## Must-NOT-Have

- **不做**真虚拟化面板（Phase 2 单列；未获用户明确决策不得开工）。
- **不改**选择/勾选/排序/筛选行为；`CheckAllVisible` 语义 = 全列表，不变。
- **不新增** AppSettings 设置项、**不新增** i18n key（三条修复均无新文案；规则 13/22 无触碰面）。
- **不动** `ThumbnailService` 的批处理契约（`StartBatch(IReadOnlyList)`/`Cancel`/`Dispose`）、`ThumbnailSelectionController`、拖拽管线（`ThumbnailLayout_Pointer*`）。
- **不动** `MantisZip.Core`（`ArchiveEntryExtractor`/`TextEncodingDetector`/`IcoIconHelper` 均仅调用）。
- 缓存命中路径**禁止** Dispose 共享位图（红线，见 T2 设计）。

## QA 清单（人工验证）

1. `pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia`（Release + `--no-incremental`）0 警告；`-Task test -Target ui-tests` 全绿；`-Task guards` 全绿。
2. 造 ≥2000 条目 zip（其中图像 50+、txt 数十、.ico ≥5）：Tile 切入**秒级可交互**（首屏 ≤200 容器），滚动到底分批出现新条目且缩略图渐进补上，无卡顿。
3. 回滚到顶再滚到底数轮：不卡顿、无重复解码日志刷屏（缓存命中）。
4. 选择跨批正确：第 1 项与第 2500 项 Shift 范围选择（部分未铺条），SelectedEntries 数正确；切回 Details 高亮一致。
5. 双击/Enter/Back/Delete/右键菜单/拖出：Tile 与 Content 与 v0.5.6 行为一致。
6. 复选框模式「勾选所有可见项」仍作用于全列表。
7. 筛选输入（含 .ico 的包）：打字过程 UI 不顿挫；第二次筛选起 .ico 图标即时出现。
8. 换包来回：缩略图二次进入直接出现（不闪图标→图的两段式过程）；重启后（缓存天然内存级，不持久）表现为正常首次冷加载——**预期行为，不是回归**。
9. 96/128/160 尺寸切换（`ReloadThumbnailsForStyleChange`）后按新尺寸重新解码，缓存 key 含尺寸、旧尺寸条目自然失配。
10. 加密包 / 固实 7z / >8MB 图像条目：兜底行为与 v0.5.6 一致（会话密码生效、固实包全图标）。
11. 关包/空目录：无异常，`VisibleEntries` 为空。

## 已知风险与回退

1. **追加节奏抖动**：快速滚轮到接近底部反复触发 `AppendNext` → `StartBatch` 重建批次。雇佣代价极低（缓存命中项秒过、限流 4 并发不丢）；若真机观察到明显抖动，在 `AppendVisibleEntriesChunk` 加 50ms 防抖（`DispatcherTimer`）——先不加。
2. **ObservableCollection 逐条 Add 的布局压力**：每批 200 次 `Add` 触发 200 个集合变更通知。真机若感到追加一顿一顿，回退方案为自定义 `IList + INotifyCollectionChanged` 每批只发一个 `Reset`（实现 30 行，但排序/选择联动需重验）——先不做，实测说话。
3. **共享缓存内存**：LRU 300 项上界不变，跨包复用只是让命中率上升，不增加上界。
4. **VisibleEntries 与 CurrentEntries 失步**：所有已铺路径都收敛于 `PopulateEntries ⇒ ResetVisibleEntries`；若发现某处绕过重填直接改 `CurrentEntries`（实施时 grep 一遍 `CurrentEntries.Clear` 与 `EntriesRefreshed`），必须补 `ResetVisibleEntries()` 调用。
5. **ICO 缓存共享位图被误 Dispose**（回归红线）：命中路径与解码路径分开标记；单测用「archive 不存在但命中 ⇒ 回填成功」钉住。

---

## Phase 2 附录：真虚拟化面板（可选，3~5 天，需用户单独决策）

> **进入条件**：Phase 1 QA 后，用户确认「分批铺条的真机表现仍不满意」或 5000+ 条目包是常态负载。**未获明确决策不得开工**（本计划 Must-NOT-Have 第一条）。

### 设计草案

- 自写 `VirtualizingWrapPanel : VirtualizingPanel`（Avalonia 11/12 有该抽象基类，`VirtualizingStackPanel` 即先例），只服务 Tile 布局（Content 每行高度不均，虚拟化收益低、案例也更复杂，可先不做）。
- 卡片尺寸**已知且统一**（`ThumbnailTileCellWidth` × 卡片高 + `SpacingXxs`）⇒ 视口内可见索引可纯算术得出，是「最简单的自定义虚拟化面板」形态。
- 实现要素：`MeasureOverride` 按 viewport 计算已实现区间 → `ItemContainerGenerator` 建立/回收容器 → `Width/Height` spacer 校正滚动锚定。
- 硬前提 spike（半天）：在 12.1.2 参考程序集里实测 `VirtualizingPanel` 公共 API 面（`ItemsControl` 是否把它当虚拟化面板对待、generator 是否可用），把实测结论写进本节；**spike 结论是「不可行」就直接闭案**，不存在降级路径——方案 A 已在 Phase 1 落地。

### 工作边界

- `ItemsControl` 的 `ItemsPanel` 换成新面板（仅 Tile）；`ItemTemplate`、事件接线、选择层零改动（容器换由 generator 生成，交互路径不变）。
- 风险点：滚动锚定抖动、生成器 API 版本缺口、悬停/焦点态与回收容器的组合 bug。这些是 3~5 天预估的主要来源。

### 验收口径

- 5000 条目 zip：Tile 切入秒开、总内存与 500 条目包同量级、往返滚动不卡。
- 与 Phase 1 的分批层关系：真虚拟化落地后 `VisibleEntries` 分批层可整体退场（回绑 `CurrentEntries`），`ScrollChanged` 追加逻辑删除——迁移路径在动手时细化，不在本计划内展开。
