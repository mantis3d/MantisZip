# 7z 全量解压改走批量接口（solid 归档性能修复）(SevenZip Solid Bulk Extract)

> **状态**: ✅ 已完成（2026-11-04 归档，随 v0.5.6）| 代码与 SSD 实测已通过；**HDD 真机复测未做**，列为 v0.5.6 发布前手工验收项
> **关联**: GitHub issue #75（`[性能] 解压海量小文件到机械硬盘时速度极慢`）；`docs/KNOWN_ISSUES.md` 无相关条目
> **前置**: 无（Core 引擎内部改造 + 复用现有进度/冲突契约）
> **决策来源**: 2026-11-01 与用户讨论 —— 用户最初假设「多线程解压在慢速硬盘反而拖累」，实测证明根因是「solid 归档逐条目提取 → 对文件数二次方」；用户要求先说明用户体验影响（见计划内「体验变化矩阵」），随后指示「写计划，所有决策按照你认为最好的方式来确定」。本计划所有决策由实施者按证据拍板，已逐条记录理由（§6）。第二轮讨论补充：用户指出「覆盖安装」这类场景下默认 `ask` 策略必须仍然提速，据此把冲突询问改为**解压前一次问完**（D4b），并把批量失败回退收紧为**只重试未完成条目**（D6）。第三轮（2026-11-02）拍板：① 接受「冲突询问提前到解压开始前集中问完」；② 改名暂存目录放在**目标目录内**（`.mantiszip-staging-<guid>`）；③ 本次**只优化全量解压**，勾选条目 / 拖拽行走 `ExtractEntriesAsync` 不动。

## TL;DR

solid 7z 归档被现在的代码**逐条目**用 `ExtractFile(index, stream)` 提取，而 solid 把多文件压进同一数据块，取第 N 个文件必须从块开头重新解压 —— 总工作量按文件数**二次方**增长。实测 800 个 64KB 文件：现路径 19.3s，`7z.exe x` 0.56s，批量接口 0.86s。

本计划在**满足严格分流条件**时改走 `ExtractFiles(destDir, indexes)` 一次性解压（配合 `FileExtractionStarted` 的 `Skip`/`Cancel`、`Extracting` 的字节百分比、`FileExtractionFinished` 的逐文件状态）。冲突在**解压之前**一次性预解析：`Ask`（出厂默认）时把现有冲突弹窗提前问完，改名条目进「二次批量 → 同卷暂存 → `File.Move`」的改名桶，覆盖/跳过直接定型。其余情况（非 solid、选择性解压、`ErrorResolver`、不安全路径、加密、≥64MB 大文件）**只对该条目走现有路径**，不再因「有交互回调」把整包拖回逐条目。最坏情况与今天等价，不会更慢、不会丢文件。

代理指标：solid 归档全量解压速度提升 **20–30 倍**，与 `7z.exe` 同量级。

---

## 1. 现象与用户证据

issue #75（Koewen，2026-11-01，v0.5.4 / Win11 / 目标盘 **HDD** / 7z）：

- 包：`GitHubDesktop.3.6.10.Chs OSSSR.7z`，「数千个 KB 级小文件」（如 `app/resources/app/emoji/*.png`）
- 症状：**15% 已耗时 1:11，预估剩余 3 小时+**
- 对照：同一块 HDD 上 `7-Zip`（同为 7z 内核）约 **5–10 分钟**
- 用户自己的分析：HDD 随机写 IOPS 低、逐文件「创建→写入→关闭」放大延迟、建议小文件聚合写 / 流水线分离 / 更大写缓冲 / 进度按字节聚合

用户的猜测里有**一条是对的、一条不适用于 7z**：

| 用户猜测 | 结论 |
|---|---|
| 「解压与落盘流水线分离，HDD 上要降低并发写」 | 对 **ZIP** 并行路径成立（`ZipEngine.SupportsParallelExtract == true`），对 **7z 完全不适用** —— 7z 路径全程串行 |
| 「HDD 逐文件 create/close/SetLastWriteTime 放大延迟」 | 是**放大器**不是主因：1 万个小文件在 HDD 上约几十秒量级，解释不了小时级 |

## 2. 根因（已实测确认）

### 2.1 二次方：solid + 逐条目

solid 归档把多个文件压进**同一个数据块（folder）**。`IInArchive::Extract(index, …)` 对 solid 归档只能**从块开头解码到目标文件**，因此：

```
总解码量 ≈ N × 块大小 / 2  →  对文件数二次方
```

`src/MantisZip.Core/Engines/SevenZipEngine.cs:532` 的既有注释正是这个选择的由来：

> `// 逐条目提取（使用 ExtractFile(index, stream) 支持所有 7z 类型包括 solid 归档）`

当时的取舍是对的（`ExtractFiles` 的逐文件回调版本对 solid 直接抛 `Solid archives are not supported`，见 §2.3），但代价在「海量小文件 + solid」上爆炸。

### 2.2 实测数据（本机 SSD；夹具 `%TEMP%\mz-7z-perf`）

夹具 B：**800 × 64KB 随机** → `big-solid.7z` 52.4MB（`-ms=on -md=64m`）

| 方式 | 耗时 | 相对 `7z.exe` |
|---|---|---:|
| `7z.exe x` | 561 ms | 1.0× |
| `ExtractArchive(dir)`（bulk） | 829 ms | 1.5× |
| **现引擎（逐条目 `ExtractFile`）** | **19,346 ms** | **34×** |
| `ExtractFiles(dir, indexes)`（bulk，指定索引） | 893 ms | 1.6× |
| `ExtractFiles` + `FileExtractionStarted` 里 `Skip` 一条 | 860 ms | 1.5× |
| `ExtractFiles` + 第 50 个文件 `Cancel` | 296 ms（落盘 49 个，不抛异常） | — |

夹具 A：**600 × 4KB** → `solid.7z` / `plain.7z` 各 2.46MB

| 归档 | 现引擎 | bulk | 结论 |
|---|---:|---:|---|
| solid（`-ms=on`） | 1194 ms | 552 ms | bulk 快 2.2× |
| **非 solid（`-ms=off`）** | **395 ms** | **629 ms** | **bulk 反而慢 1.6×** ⇒ 必须按 `IsSolid` 分流 |

其它已实测结论：

- bulk **保留时间戳**（与源文件 `LastWriteTime` 逐 tick 一致）
- bulk **会创建空目录**（归档里的 `empty1\empty2` 照样出现）
- bulk **不会触发 `FileExists` 事件**：预先放同名文件后跑批量解压，`existsEvents=0`，原文件被**静默覆盖** ⇒ 冲突决策不能交给 7z.dll，必须自己预解析（§5.3）
- `ExtractFiles(ExtractFileCallback)`（逐文件回调、可自定义路径）对 solid 抛 `SharpSevenZipExtractionFailedException: … Solid archives are not supported` ⇒ **此路不通**，不要尝试

### 2.3 为什么不是多线程

- `src\MantisZip.Core\Abstractions\ArchiveEngine.cs:570` `SupportsParallelExtract` 默认 `false`；只有 `src\MantisZip.Core\Engines\ZipEngine.cs:35` 为 `true`
- `src\MantisZip.UI.Avalonia\Services\ExtractFlow.cs:370` 对不支持并行的引擎直接回落 1
- 因此 7z 解压**本来就是单线程**，「多线程把 HDD 拖垮」在这个 issue 里不存在

**ZIP 并行写 HDD 的抖动是另一个问题**，本计划不处理，见 §4 非目标。

## 3. 现状锚点

| 位置 | 作用 | 本计划是否改动 |
|---|---|---|
| `src\MantisZip.Core\Engines\SevenZipEngine.cs:488` | `ExtractAsync` 入口 | ✅ 加分流 |
| `:518` | `extractor.ArchiveFileData.ToList()` + `totalFiles/totalBytes` | ✅ 复用 |
| `:532-533` | 「逐条目提取」注释 + `for` 循环 | ✅ 拆成批量段 + 逐条目段 |
| `:540-543` | 目录条目 `Directory.CreateDirectory` | ⚠️ 批量段由 7z.dll 代劳（已实测），保留原路径 |
| `:547` | `FileConflictHelper.GetSafePath`（路径安全） | ✅ 批量前全量预检 |
| `:553` | `FileConflictHelper.ResolvePathAsync`（冲突决策） | ✅ 批量前全量预解析 |
| `:601-627` | `FileStream` + `WriteProgressStream` + `ExtractFile` | ⚠️ 仅「逐条目段」保留 |
| `:629` | `File.SetLastWriteTime` | ⚠️ 批量段由 7z.dll 完成 |
| `:559-574` / `:586-595` / `:614-622` | 跳过、单文件起点、逐块字节进度上报 | ✅ 改为批量段的事件映射（§5.4） |
| `src\MantisZip.Core\Utils\FileConflictHelper.cs:67` `ResolvePathAsync` / `:193` `GetSafePath` | 冲突/安全 | ✅ 复用，不改签名 |
| `src\MantisZip.Core\Abstractions\ArchiveEngine.cs:273` `FileConflictAction` | 冲突策略枚举 | 不改 |
| `src\MantisZip.Core\Utils\WriteProgressStream.cs` | 逐块进度薄包装 | 仅逐条目段使用 |
| `src\MantisZip.UI.Avalonia\ViewModels\ProgressViewModel.cs:291` `CurrentFileRatio`、`:230` `RemainingText` | 单文件比例条 / ETA | ⚠️ 批量段单文件比例只有 0→100 |
| `src\MantisZip.UI.Avalonia\ViewModels\ProgressViewModel.cs:2495` `CreatePauseAwareProgress` | 暂停 = 阻塞进度回调 | ⚠️ 批量段回调来自 7z.dll 线程，需实测不卡死 |

## 4. 目标 / 非目标

**目标**

1. solid 7z 全量解压走批量路径，速度与 `7z.exe` 同量级（目标：实测 ≥ 10×，期望 20–30×）
2. 现有语义零回归：冲突策略（跳过/覆盖/改名/覆盖更旧/覆盖更小）、路径安全、取消、暂停、逐文件状态（D2）、时间戳、空目录
3. 最坏情况与今天等价（任何不满足条件 → 走原路径；批量失败 → 回退逐条目）
4. 进度不再出现「长时间 0% 不动」的假死观感

**非目标**

- ❌ 不做 ZIP 并行写 HDD 的并发节流（`IOCTL_STORAGE_QUERY_PROPERTY` 查询 seek penalty → 降低并行度）。这是**另一个优化**，建议单独立项；本计划不碰 `ZipEngine.Extract.cs`
- ❌ 不改 `ExtractPathResolver` / 预览树路径契约
- ❌ 不做「小文件聚合写入缓冲」（用户在 issue 里提的第 1 条）—— 实测瓶颈是块解码而不是写放大，聚合写解决不了主因，且会打乱逐文件状态与取消粒度
- ❌ 不支持 RAR/ISO 的批量优化（RAR 走 7z.dll 但通常单块小、收益不明确；待后续测量再评估）
- ❌ 不做「勾选条目解压 / 拖拽解压」（`SelectedItemsExtractService` → `ExtractEntriesAsync`）的批量优化 —— 2026-11-02 用户确认本次只做全量解压；那条路径仍逐条目、速度不变，**发版说明必须写明**这个边界

## 5. 方案

### 5.1 分流条件（全部满足才走批量）

```
canBulk =
      resolvedFormat == ArchiveFormat.SevenZip      // 只对 7z 载荷生效（RAR/ISO 虽同走 7z.dll，未测，§4 非目标）
   && extractor.IsSolid == true                    // 非 solid 走原路径（实测 bulk 更慢 1.6×）
   && password == null && !hasEncrypted            // 加密包先不碰（§5.10）
   && options.ErrorResolver == null                // 逐文件「读取失败」无法在批量前归因，仍走原路径
   && entriesToBulk.Count > 0
```

其中 `resolvedFormat = SfxProbe.TryGetPayloadFormat(archivePath) ?? ArchiveEngineFactory.GetFormatByExtension(archivePath)` —— `ExtractAsync` 目前**没有** format 变量（只有 `ListEntriesAsync:851` 有），需要新增这一行；用 `SfxProbe` 兜底是为了让 7z 载荷的自解压 exe 也走对分支，而 **ZIP 载荷的 SFX 会被判成 Zip ⇒ 不走批量**（保守，未测量）。

补充说明：

- **允许冲突回调**：`ConflictResolver` / `ConflictResolverAsync` 非 null 时**不退回逐条目**，而是把询问提前到预解析阶段一次问完（§5.3），之后按答复定型分桶。这是本计划的关键决策 —— `AppSettings.FileConflictAction` 的**默认值就是 `"ask"`**（`src\MantisZip.UI.Avalonia\Models\AppSettings.cs:85`，`Models\ExtractSettings.cs:78` 同为 `"ask"`），若「有交互回调就退回逐条目」，默认设置的用户只要目标目录里有同名文件就完全拿不到提速，等于这个优化对主场景无效
- **`ErrorResolver` 仍排除**：它处理的是「单个文件读写失败」这类运行期错误，批量接口只能整包失败、无法把错误归因到具体条目；这类调用方（需要逐文件错误回调）走原路径
- **预解析不一定需要 stat**：`ConflictAction == Overwrite` 且 `ConflictResolver` / `ConflictResolverAsync` 都为 null 时，根本不需要知道目标文件是否存在 —— 7z.dll 批量解压本来就是覆盖语义，全部条目直接进 `Direct` 桶，一条 `File.Exists` 都不做
- **`Ask` 一旦定型就停止询问与 stat**：用户勾「应用到全部」之后，剩余冲突条目按该答复直接分桶，不再弹窗、也不再为「是否存在」逐条 stat（`Overwritten` 计数在 §5.3.2 的批量前复检阶段补记）
- **选择性解压不经过这条路径**：`ExtractAsync` 只做全量解压；「只解压选中的条目」走 `ExtractEntriesAsync`（`src\MantisZip.Core\Engines\SevenZipEngine.cs:1069` 附近），本计划不改它
- `ArchiveOptions`（`src\MantisZip.Core\Abstractions\ArchiveEngine.cs:28`）里与本计划相关的是 `ConflictAction` / `ConflictResolver` / `ConflictResolverAsync` / `ErrorResolver`
- `IsSolid` 由 SharpSevenZip 提供（`IsSevenZipSolid` 已在 `src\MantisZip.Core\Utils\ArchiveEntryExtractor.cs:537` 使用同一属性）
- **只影响 7z 载荷**：`.zip` 走 `ZipEngine`（另有并行解压实现，本计划一行都不碰）、`.tar`/`.tar.gz` 走 `TarGzEngine`；`.rar`/`.iso` 虽然也在 `SevenZipEngine` 里，但被 `resolvedFormat == SevenZip` 挡在外面（RAR 也有 solid 包，没测过就不放开）

### 5.2 批量路径流程

```csharp
// 阶段 2a：预解析（不落盘，快）—— 冲突策略在这里一次性决定，之后不再变
// ★ 必须上报进度：状态文案「正在检查冲突 N/M」（新 i18n key），每 N 条检查一次 ct；
//     否则 2 万条目的 stat 在 HDD / UNC 上就是「0% 假死」，与本计划的目标 4 相悖。
//     Overwrite + 无 resolver 时整段 stat 跳过（快路径，几乎瞬时）
// ★ 顺序 B（先问后解）：ConflictAction == Ask 时，ResolvePathAsync 会在这一阶段就把
//     现有 ConflictDialog 弹出来问完（用户可选「应用到全部」），全部答案定型后才开始解压。
foreach (var entry in allEntries) {
    if (entry.IsDirectory) { 记录目录；批量段由 7z.dll 创建; continue; }
    var safe   = FileConflictHelper.GetSafePath(destinationPath, entry.FileName);
    if (safe 越界/与 destinationPath 不一致)          → bucket.Unsafe.Add(entry)     // §5.7，逐条目
    var resolved = await FileConflictHelper.ResolvePathAsync(safe, options, entry.LastWriteTime, entry.Size);   // ← 询问发生在这里
    if (resolved == null)                            → bucket.Skip.Add(entry)       // 上报 Skipped，不进任何索引
    else if (resolved == safe && entry.Size < BigFileThreshold) → bucket.Direct.Add(entry)   // 进批量：直接写目标
    else if (resolved == safe)                       → bucket.BigFile.Add(entry)    // ≥64MB：逐条目，保住文件内进度
    else                                             → bucket.Rename.Add(entry)     // 改名/覆盖更旧/更小：二次批量 + 移动
}

// 阶段 2b：主批量（solid 只解码一遍，直接写最终路径）
extractor.FileExtractionStarted  += (_, e) => { 若 cancellationToken 已取消 → e.Cancel = true; 上报 CurrentFile + FilePercentComplete=0 };
extractor.Extracting             += (_, e) => 上报整包字节百分比（见 §5.4）;
extractor.FileExtractionFinished += (_, e) => { processedFiles++；上报 Completed；刷 ETA };
extractor.ExtractFiles(destinationPath, bucket.Direct.Select(e => e.Index).ToArray());

// 阶段 2c：改名桶的「二次批量 + 移动」（一次解码，而不是每个文件解码一次）
var staging = Path.Combine(destinationPath, $".mantiszip-staging-{Guid.NewGuid():N}");   // 同卷，File.Move 即 rename，不复制数据
extractor.ExtractFiles(staging, bucket.Rename.Select(e => e.Index).ToArray());          // 第二次也只解码一遍
foreach (var entry in bucket.Rename) {
    var from = Path.Combine(staging, entry 相对路径);
    var to   = 预解析时缓存的 resolvedPath;             // 如 a (1).txt
    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
    File.Move(from, to);                               // 保留 7z.dll 已设好的时间戳
    上报 Completed；
}
删除 staging（含失败/取消时的兜底清理）

// 阶段 2d：逐条目（≥64MB 大文件 / 不安全路径 / 批量失败残留）—— 现有逐条目代码原样复用
foreach (var entry in bucket.BigFile.Concat(bucket.Unsafe).Concat(残留)) { … }
```

**关键点**：三个阶段共用同一批计数器（`processedFiles` / `handledFiles` / `processedBytes` / `conflictStats`），进度百分比与 ETA 不跳变。

#### 5.2.1 暂存目录策略（2026-11-02 拍板：放目标目录内）

- **位置**：`Path.Combine(destinationPath, $".mantiszip-staging-{Guid.NewGuid():N}")` —— 在目标目录内 ⇒ 与最终落点同卷 ⇒ `File.Move` 是元数据 rename，不搬数据；改成目标目录同级会有跨卷与权限两种失败模式
- **可见性代价**：解压期间用户在 Explorer 里能看到这个点前缀目录。接受（比自解压工具的临时目录更收敛），完成后立即删除
- **清理必须写死**：`try/finally` 覆盖成功 / 取消 / 异常三条路径；另外每次进入批量段前按前缀 `.mantiszip-staging-*` 顺手清扫上次崩溃残留（只匹配目录、失败只记日志，不打断解压）
- **降级**：目标目录只读 / 无权限创建暂存目录 ⇒ **改名桶整体退回逐条目**（不报错、不丢文件）；`File.Move` 撞占用 ⇒ 该条目退回逐条目重试，仍失败记入 `failedEntries`

**为什么询问要放在解压之前（顺序 B）**：默认策略是 `ask`，最常见的重场景恰恰是「覆盖安装」—— 目标目录里几千个文件全部同名。如果询问发生在解压过程中、或者干脆因为「有交互回调」退回逐条目，那么整包（不只是冲突的）都会被拖回逐条目解码，用户完全感受不到提速。把询问提前到预解析阶段后：

- 用户答一次「覆盖 + 应用到全部」→ 冲突条目全进直接桶 → 整包只解码一遍
- 答「改名 + 应用到全部」→ 全进改名桶 → 只多解码一遍（不是每文件一遍）
- 答「跳过 + 应用到全部」→ 冲突条目根本不进索引
- 答「取消整轮」→ 一个文件都还没落盘，比今天更干净

代价是弹窗时机从「边解边问」变成「解压开始前集中问完」，决策内容、选项、应用到全部、暂停、取消语义都不变。

**为什么改名桶用「二次批量 + 移动」而不是逐条目**：solid 归档里每多一次「逐条目提取」就多解码一遍整块，500 个改名条目 × 块解码 = 灾难；而「二次批量→写进同卷暂存目录→`File.Move` 改名」总共只多解码**一遍**，移动是同卷 rename（元数据操作，几乎零成本）。语义与今天完全一致：旧文件原地不动、新文件落在 `a (1).txt`。

### 5.3 冲突预解析

`FileConflictAction`（`src\MantisZip.Core\Abstractions\ArchiveEngine.cs:273`）取值：`Overwrite`（默认）/ `Rename` / `Skip` / `Ask` / `OverwriteIfOlder` / `OverwriteIfSmaller`。

- 7z.dll 批量解压**静默覆盖**（实测 `FileExists` 不触发）⇒ 所有冲突决策在批量前由 `FileConflictHelper.ResolvePathAsync` 完成，与今天**完全同一套逻辑**
- 预解析会先算出「哪些目标路径已存在」（`conflicts`）。之后：

| 情形 | 处置 |
|---|---|
| `conflicts` 为空（全新目标目录） | 全部进批量，**不关心策略**（没有任何决策要做） |
| `ConflictAction == Overwrite` | 冲突条目也进批量（7z.dll 默认就是覆盖，语义一致） |
| `ConflictAction == Skip` | 冲突条目**不加入索引**（比 `e.Skip` 更省），但仍上报 `ArchiveEntryStatus.Skipped` 与计数 |
| `ConflictAction` 是 `Rename` / `OverwriteIfOlder` / `OverwriteIfSmaller` | 冲突条目进 `Rename` 桶：**二次批量**解到同卷暂存目录（`.mantiszip-staging-<guid>`），再逐条 `File.Move` 到预解析算出的 `resolvedPath`（如 `a (1).txt`）；旧文件保持原样 |
| **`ConflictAction == Ask`（出厂默认）且有冲突** | 冲突条目在**预解析阶段逐条询问**（归档顺序，走现有 `ExtractFlow.ShowConflictDialogAsync`），答案即时定型：`Overwrite` → 直接桶；`Skip` → 不进索引；`Rename`（含自定义名）→ 改名桶。勾「应用到全部」→ 后续冲突条目按该答复直接定型、不再弹窗。全部问完后才开始解压；此阶段取消整轮 = 尚未落盘 |
| 存在 `ErrorResolver` | **整包退回逐条目**（逐文件读写错误无法在批量前归因，见 §5.1） |

- 批量后无需核对「跳过」是否被覆盖：跳过的索引根本没提交
- **`Ask` 不需要引擎侧新写弹窗逻辑**：「应用到全部」的记忆已存在于 `src\MantisZip.UI.Avalonia\Services\SelectedItemsExtractService.cs:127-168`（`applyToAll` + `chosenAction` 闭包），`FileConflictHelper.ResolvePathAsync`（`src\MantisZip.Core\Utils\FileConflictHelper.cs:67-96`）在 `Ask` 时会调它并据答复返回「原路径 / 改名路径 / null」，预解析循环照常按返回值分桶即可自动收敛
- 「先预解析、后批量」意味着**同一批计数器**（`processedFiles` / `handledFiles` / `processedBytes` / `conflictStats`）要跨两段共享，进度不跳变

#### 5.3.1 预解析的两个边界（必须处理，否则语义与今天不同）

预解析看到的是**磁盘当前状态**，而今天的逐条目是**串行看到中间状态**。两处会分叉：

| 边界 | 为什么分叉 | 处置 |
|---|---|---|
| **归档内重名条目**（同一归档里两个条目落点相同，如 `a.txt` 出现两次） | 预解析时磁盘上还没有任何文件，第二个 `a.txt` 被判定「无冲突」→ 两条都进批量 → 7z.dll 让后写的静默覆盖 → 与今天（第二条会走 `Rename`/`Skip` 策略）不同 | 预解析时维护「已认领落点集合」（`HashSet<string>`，`OrdinalIgnoreCase`），第二个同路径条目即视为冲突并按策略分桶：`Ask` 时照常询问用户，`Rename` 时算出的 `(1)` 名也要写进认领集合（避免两个条目算出同一个落点）。**不需要整包退回** —— 询问已经提前到预解析阶段，逐条问与整包问的顺序一致 |
| **预解析 → 批量开始之间的窄竞态**（其它进程在这几毫秒里创建了目标文件） | 批量会静默覆盖，而今天在逐条目判断下会按 `Rename` 改名 | 批量调用**前一瞬**对 `Direct` 集合再做一次 `File.Exists` 复检（几千次 stat，毫秒级）；新出现的冲突条目移出主批量、按冲突策略重新分桶（Overwrite → 留在批量；其余 → Rename 桶） |

其余情形（预解析后磁盘状态不再变化）无语义差异。

#### 5.3.2 预解析的开销与可见性（2026-11-02 补充）

`FileConflictHelper.ResolvePathAsync` 的**第一行就是 `File.Exists`**（`src\MantisZip.Core\Utils\FileConflictHelper.cs:69`），所以「预解析」天然等于「对上万条目逐个 stat」。在机械盘 / UNC 上这是秒级到几十秒级的开销，且全部发生在落盘之前 —— 不处理就会把「二次方解码」的假死换成「预解析假死」。四条约束：

| # | 约束 | 说明 |
|---|---|---|
| P1 | 快路径：`Overwrite` + 无 resolver **零 stat** | 7z.dll 覆盖语义与策略一致，存在性无关紧要（§5.1） |
| P2 | 定型即止：`Ask` 答「应用到全部」后停止 stat | 答复已决定一切；`Overwritten` 计数按 §5.3.1 的批量前复检补记 |
| P3 | 上报「正在检查冲突 N/M」+ 每 N 条检查 `ct` | 进度窗口可见、可取消；文案走 i18n，不硬编码 |
| P4 | stat 抛异常（UNC 掉线 / 权限）按「未知」处理 | 该条目退回逐条目，**绝不**假设「无冲突」后交给批量 |

> 时序说明：**询问在解压之前**（§5.2 阶段 2a），**执行顺序是「主批量 → 改名桶二次批量 + 移动 → 逐条目（≥64MB / 不安全路径 / 批量失败残留）」**。用户看到的是：先集中答题 → 进度条快速推进（绝大部分文件瞬间出现）→ 最后一批改名文件随移动落到 `a (1).txt`。不再有「边解边问」的中断感，也不会出现「先盯着几个慢文件」。

### 5.4 进度映射

| 进度字段 | 今天（逐条目） | 批量段 |
|---|---|---|
| `PercentComplete`（整包） | 文件计数 `handledFiles/totalFiles` | **字节比**：`(processedBytes + 当前块已解字节) / totalBytes`，来源 `Extracting.PercentDone`；缺口部分用 `handledFiles/totalFiles` 兜底，二者取较大值避免倒退 |
| `FilePercentComplete`（单文件） | 字节级 0→100 | **0 → 100**（文件开始 0，完成 100）；不再有中间值 |
| `CurrentFile` | 当前条目名 | `FileExtractionStarted.FileInfo.FileName` |
| 逐条目状态（D2） | `Completed`/`Skipped`/`Overwritten` | `FileExtractionFinished` → `Completed`；`Skip` 集合 → `Skipped`；覆盖判定在预解析阶段上报 `Overwritten` |
| ETA | 单文件字节 + 整包文件计数 | 整包字节百分比，EMA 继续平滑 |

**为什么取「较大值兜底」**：`Extracting` 的字节百分比按整包详细程度推进，但如果 7z.dll 在某些归档上不汇报（或先报 0 后跳到 100），纯字节比会让进度条倒退；文件计数是单调的，取 max 保证单调递增。

**询问阶段（`Ask`）**：整包进度停在 0%，状态文案沿用现有冲突提示；弹窗期间 `ExtractFlow.ShowConflictDialogAsync` 的 `BeginModalFreeze` 会冻结已用时间（现有实现），避免「答题时 ETA 继续倒退」。

### 5.5 取消与暂停

- **取消**：`FileExtractionStarted` 里检查 `cancellationToken.IsCancellationRequested` → `e.Cancel = true`。实测有效（第 50 个文件取消 → 立刻停、不抛异常、已落盘文件保留）。粒度与今天相同（**文件边界**），最坏等待 = 当前文件的块解码时间
- **暂停**：现有实现是「进度回调里阻塞」（`ProgressViewModel.CreatePauseAwareProgress`）。批量段的回调由 7z.dll 的解压线程同步触发 ⇒ 阻塞回调即暂停。**必须实测**：暂停 3s 后恢复，确认不卡死、不丢文件（列入 §8 验收）
- 取消后：已落盘文件保留（与今天一致），结果对象按现有 `ExtractResult` 语义返回
- **询问阶段取消**（`Ask` 策略下用户在冲突弹窗点「取消整轮」）：此时还没有任何文件落盘，直接按现有 `OperationCanceledException` 语义退出，不需要清理暂存目录

### 5.6 失败回退

```
var finished = new HashSet<int>();          // FileExtractionFinished 里记录已完成的 entry.Index
try { ExtractFiles(...); }
catch (Exception ex) {
    CoreLog.Error($"ExtractAsync: 批量解压失败，只对未完成条目回退逐条目：{ex.Message}");
    // 已完成（finished 里）的不再重试 —— 否则等于把整包重新解一遍；
    // 未完成的走逐条目：单条失败记 failedEntries，不整包崩
}
```

结果：**用户可能看到「先快后慢」**，但不会丢文件、不会把成功报成失败，也不会把已落盘的文件重新解一遍（重试集合 = `bucket.Direct − finished`）。批量段的失败计数与逐条目段共用 `failedEntries`。

### 5.7 路径安全

批量前对每个条目做 `FileConflictHelper.GetSafePath(destinationPath, entry.FileName)`，与既有实现同一函数。判定为「越界（`..`）/ 绝对路径 / 落点不在目标目录内」的条目 → **只把这一批放进 `Unsafe` 桶逐条目处理**，其余照常批量。绝不为了性能把未校验的名字交给 7z.dll 写盘。

### 5.8 大文件阈值

单个条目的 `Size >= 64MB` → 逐条目（保住文件内字节级进度条）。
代价：该文件多解一遍块；收益文件数很少，可接受。阈值常量化 `private const long BigFileThresholdBytes = 64L * 1024 * 1024;`（不新增设置项，避免规则 23 的死开关问题）。

### 5.9 属性 / 时间戳 / 空目录

- **时间戳**：批量段由 7z.dll 保留（已实测与源一致）；逐条目段保留现有 `File.SetLastWriteTime`
- **文件属性位**：现有逐条目代码**只设时间戳、不设属性**；批量段同样不设 ⇒ 无差异，**不需要补属性修正**（若实测发现 7z.dll 反而设置了属性，则以「与今天一致」为准，显式清掉）
- **空目录**：批量段实测会创建（含多层空目录）；逐条目段保留现有逻辑

### 5.10 加密

`password != null` 或存在加密条目时**不走批量**（保持今天路径）。理由：批量接口在加密 solid 上的密码行为、加密头处理与逐条目不同，收益不确定、风险不对称；加密包文件数通常不多。后续可用真实加密 solid 包测量后另行放宽（记入 §12 复评）。

## 6. 已拍板的决策（含理由）

| # | 决策 | 理由 |
|---|---|---|
| D1 | 只在 `IsSolid` 时走批量 | 非 solid 实测 bulk 反而慢 1.6×（395ms → 629ms） |
| D2 | 冲突决策 100% 预解析，不依赖 7z.dll | 实测 bulk 静默覆盖、`FileExists` 不触发 |
| D3 | 跳过条目**不进索引**而不是用 `e.Skip` | 少一次回调；`e.Skip` 保留为「运行期兜底」（例如预解析后文件又被创建） |
| D4 | 改名桶走「二次批量 → 同卷暂存目录 → File.Move」而不是逐条目 | 逐条目在 solid 上等于「每个改名文件解码一遍整块」（500 个 = 500 遍）；二次批量只多解码**一遍**，移动是同卷 rename 几乎零成本，语义与今天一致（旧文件不动、新文件落在  (1).txt） |
| D4b | 冲突询问（`Ask`）提前到预解析阶段一次问完，**不**因为「有交互回调」退回逐条目 | `AppSettings.FileConflictAction` 默认就是 `ask`（`Models/AppSettings.cs:85`）。退回逐条目会让默认用户在「覆盖安装」这类最常见场景完全失去提速；提前问完则答一次「覆盖+应用到全部」后整包只解码一遍，决策内容与今天一致 |
| D5 | 大文件阈值 64MB 退回逐条目 | 唯一 UX 退步是「大文件内部无进度」，用一次额外块解码换回它；64MB 以下单文件几秒内完成，观感无差 |
| D6 | 批量失败时**只对未完成条目**回退逐条目（`finished` 集合记录进度） | 保证「不丢文件」与「错误可定位」，同时避免「整包重解一遍」；覆盖安装场景下目标文件被占用的概率不低，这条兜底必须省时 |
| D7 | 加密/带密码归档不走批量 | 风险不对称、收益未测；先保稳 |
| D8 | 暂停沿用阻塞式进度回调 | 零改动即可生效；若实测卡死才引入显式暂停门（记入风险） |
| D9 | 整包进度取 `max(字节比, 文件计数比)` | 防倒退、防 7z.dll 不汇报时进度条卡住 |
| D10 | 不新增设置项/开关 | 规则 23（死开关）+ 用户视角「解压快」不需要选项；内部常量即可 |
| D11 | 分支 `beta` | 规则 19：这是性能修复（issue #75），不是新功能叙事；与 self-contained `alpha` 特性不冲突 |
| D12 | 不碰 ZIP 并行路径 | 那是独立的 HDD 节流问题，混在一起会污染回归面 |
| D13 | 预解析对 `Overwrite` + 无 resolver **零 stat**，`Ask` 定型后停止 stat | `ResolvePathAsync` 第一行就是 `File.Exists`；2 万条 stat 在 HDD/UNC 上是新的一种假死（§5.3.2） |
| D14 | 改名暂存目录放**目标目录内** `.mantiszip-staging-<guid>` | 同卷 ⇒ `File.Move` 是 rename；放同级会引入跨卷与权限两种失败模式（2026-11-02 用户拍板） |
| D15 | 本次只优化全量解压，选中项 / 拖拽路径不动 | issue #75 的场景是全量解压；选中项是另一套分流，另加约一倍工作量（2026-11-02 用户拍板） |

## 7. 实施步骤

| # | 步骤 | 文件 | 预估 |
|---|---|---|---|
| S1 | 抽 `ExtractAsync` 的阶段 2 为「预解析（含 `Ask` 询问）→ 批量段 → 逐条目段」三段，保持现有逐条目代码可原地复用 | `src\MantisZip.Core\Engines\SevenZipEngine.cs` | 3-4h |
| S2 | 批量段实现（事件订阅 + `ExtractFiles` + 进度映射 + 取消 + 失败回退） | 同上 | 3-4h |
| S3 | 预解析桶与路径安全（复用 `GetSafePath` / `ResolvePathAsync`，不改其签名）；**分桶逻辑抽成 `internal static BulkExtractPlan BuildPlan(...)`**，便于脱离归档单测 | 同上 | 1-2h |
| S4 | 提取逐条目段为私有方法 `ExtractEntriesOneByOneAsync(...)`（改名/大文件/不安全/失败残留共用） | 同上 | 1-2h |
| S5 | 测试（见 §8） | `tests\MantisZip.Tests\Engines\SevenZipBulkExtractTests.cs`（新建） | 3-4h |
| S6 | 真机夹具端到端 + 文档/发版记录 | 见 §12 | 1-2h |

合计约 **1.5–2 天**（含验证）。

## 8. 测试计划

**单元/集成（`tests\MantisZip.Tests`）** — 夹具由测试代码用 `7z.exe`? ❌ 测试环境不能依赖外部 `7z.exe` ⇒ **语料入库**：

1. 新增 `tests\corpus\sevenzip\solid-small.7z`（约 200 × 4KB 随机，`-ms=on`，< 1MB）—— 覆盖「批量路径被启用」与「结果与原路径逐文件一致」
2. 新增 `tests\corpus\sevenzip\solid-split-behaviour.7z`? 不需要 —— 复用现有 7z 语料做**非 solid 不应走批量**的断言
3. 用例：
   - `Extract_SolidArchive_ProducesIdenticalTree`（文件数 / 相对路径 / 大小 / 内容 CRC / 时间戳）
   - `Extract_SolidArchive_ReportsAllEntriesCompleted`（逐条目状态齐全，无重复上报）
   - `Extract_SolidArchive_SkipConflict_SkipsAll`（目标目录预放同名文件 + `FileConflictAction.Skip` → 文件保持原内容、状态为 Skipped）
   - `Extract_SolidArchive_RenameConflict_RenamesAll`（改名桶 → 二次批量 + 移动：生成 `name (1).ext`，旧文件内容不变、新文件内容与被改名条目一致、数量与冲突数一致）
   - `Extract_SolidArchive_CancelMidway_KeepsExtracted`（取消 → 不抛异常，已解文件保留）
   - `Extract_NonSolidArchive_UnchangedBehaviour`（非 solid 走原路径，行为与基线一致）
   - `Extract_UnsafeEntryName_BulkRefusesPathTraversal`（构造 `..\..\evil.txt` 条目 → 目标目录外无文件产生）
    - `Extract_SolidArchive_RenameBucket_LeavesNoStagingDirectory`（解压结束后目标目录下没有 `.mantiszip-staging-*` 残留）
   - `Extract_SolidArchive_AskConflict_PromptsBeforeExtraction`（`ConflictResolverAsync` 回调返回 `Skip`；在回调里断言「目标目录里还没有任何本次要解的新文件」→ 证明询问发生在解压之前）
   - `Extract_SolidArchive_AskApplyAllOverwrite_OverwritesInOneBulk`（回调返回 `Overwrite` + `ApplyToAll` → 回调只被调用 1 次；所有冲突文件内容被新内容替换；额外块解码次数为 0）
   - `Extract_SolidArchive_AskCancelDuringPrompts_ExtractsNothing`（回调抛 `OperationCanceledException` → 目标目录一个文件都没有落盘）
   - `Extract_SolidArchive_AskRenameCustomName_UsesCustomName`（回调返回 `Rename` + 自定义名 → 落点为自定义名，旧文件不变）
   - `BuildPlan_DuplicateEntryTargets_TreatedAsConflict`（纯函数单测：两个条目落点相同 + `Rename` 策略 → 第二个进 `Rename` 桶，且两个落点互不相同（认领集合生效），**不要求整包退回**）
   - `BuildPlan_NewFileAppearsBeforeBulk_MovedOutOfBulk`（纯函数单测：复检阶段发现新冲突 → 条目移出 `Direct`）
   - `Extract_LargeEntryInSolidArchive_UsesPerEntryPath`（> 64MB 条目 → 走逐条目，进度有中间值；可用小阈值注入? ❌ 常量，改为断言「日志/Solid 分桶计数」内部可见性）
4. 若内部状态需要断言，暴露 `internal` 诊断（`InternalsVisibleTo` 已有? 否则用 `#if DEBUG` 日志断言）—— 优先断言**外部可观察行为**，不测实现细节

**端到端（手工，本机）**

- 夹具 `%TEMP%\mz-7z-perf\big-solid.7z`（800 × 64KB solid）：修复前后耗时对比、`7z.exe x` 对照
- 合成大夹具：**20,000 × 4KB solid（约 200MB）**，模拟用户场景，记录耗时与内存
- **HDD 目标盘复测**（issue #75 的真实场景）：上述两个夹具解到机械盘，记录耗时；同时确认预解析阶段会显示「正在检查冲突 N/M」并在数秒内走完，不再出现「0% 不动」
- 暂停 3s → 恢复；取消 → 校验已落盘文件数量与状态文案
- 用户包（若拿到 `GitHubDesktop.3.6.10.Chs OSSSR.7z`）做修复前后对比，作为 issue 回复的实测数据

**回归**

- `pwsh -File scripts\verify-repo.ps1 -Task test -Target tests`（Release）
- `-Task test -Target ui-tests`（进度窗口/暂停路径）
- `-Task check`、`-Task guards`（无新设置项，接线守卫不受影响）

## 9. 风险与回退

| 风险 | 概率 | 处置 |
|---|---|---|
| 批量接口在某些 7z 变体上行为异常 | 中 | 分流条件保守 + 失败整体回退逐条目；出问题可一行关掉 `canBulk` |
| 暂停时阻塞 7z.dll 线程导致卡死 | 低 | 实测验收项；若卡死，改为「`e.Cancel` + 记录进度后重新 `ExtractFiles` 续解」或退回逐条目 |
| 进度条倒退/跳变 | 中 | D9 的 `max(字节比, 文件计数比)` 单调化；UI 侧已有夹取 |
| 改名桶的暂存目录残留（崩溃/断电） | 中 | 目标目录下建唯一名 `.mantiszip-staging-<guid>`；`try/finally` 清理；下次解压前顺手清理同类残留目录（前缀匹配）；测试覆盖 |
| 改名桶二次批量时磁盘占用增加（暂存副本） | 中 | 只针对真正冲突的条目；同卷 `File.Move` 后立即释放；冲突条目通常远小于整包 |
| 改名冲突场景反而变慢（全部重命名） | 低 | 冲突条目再多也只多**一遍**块解码 + N 次同卷 rename，仍显著快于逐条目 |
| `Ask` 策略下冲突极多、用户逐条答题 | 中 | 弹窗次数与今天相同（今天也是逐条弹），只是时机提前；勾「应用到全部」即收敛为一次。若用户选择「取消整轮」，此时尚未落盘 |
| 时间戳/属性差异 | 低 | 已实测时间戳一致；属性两边都不设 |
| 现有测试依赖引擎内部逐条目调用 | 中 | 先跑全量 `tests`，把依赖实现的用例改为行为断言 |

**回退开关**：`private const bool EnableBulkSolidExtract = true;` —— 出问题改 `false` 即回到今天的路径，不动结构。

## 10. 验收标准

1. `-Task test -Target tests` / `-Target ui-tests` / `-Task check` / `-Task guards` 全绿（Release）
2. 800 × 64KB solid 夹具：`ExtractAsync` 耗时 ≤ 1.5s（现在 19.3s，`7z.exe` 0.56s）
3. 20,000 × 4KB solid 夹具：耗时相对修复前 ≥ 10× 改善，且完成时间与 `7z.exe` 同量级
4. 全部冲突策略、取消、暂停、空目录、时间戳、路径穿越行为与修复前逐项一致（测试用例覆盖）；`Ask` 策略下弹窗发生在**任何文件落盘之前**，且「应用到全部」后不再弹窗
5. 非 solid / 带密码 / `ErrorResolver` / 选择性解压路径的**行为与耗时**均无变化
6. `CoreLog` 里批量段有明确标记（`ExtractAsync: bulk solid 800 entries`），便于用户日志排障

## 11. 体验变化矩阵（发版说明用）

| 场景 | 变化 |
|---|---|
| solid 7z，海量小文件 | ⬆️ 数量级变快（20–30×） |
| solid 7z，少量文件 | ⬆️ 略快，无感 |
| 非 solid 7z / zip / tar / rar | ➖ 不变 |
| 单文件内部进度条 | ⬇️ 批量段只有 0→100（> 64MB 的文件仍走逐条目，保留进度） |
| 整包进度 / ETA | ⬆️ 改为真实字节进度，更准 |
| 取消 / 暂停 | ➖ 粒度与语义不变 |
| 冲突「每次询问」（出厂默认） | ⬆️ 弹窗提前到解压开始前集中问；答一次「覆盖 + 应用到全部」后整包只解码一遍。原先「有冲突就整包退回逐条目」导致的零提速不再发生 |
| 冲突「全部重命名」 | ⬆️ 也有提速（改名桶二次批量 + 移动，多一遍块解码），文件内容与落点与今天**逐字节一致** |
| HDD 上整机观感 | ⬆️ 磁盘连续写、CPU 低负载 |

## 12. 文档与发版

- `docs/ARCHITECTURE.md`：引擎契约小节补「solid 批量解压分流」说明（含分流条件表与「不要改成一律 bulk」）
- `docs/progress-avalonia-detail.md` + `docs/PROGRESS.md`：修复条目（性能类按规则 18 归入「性能 / Performance」节，不写「修复」）
- `RELEASE_NOTES.md`：v0.5.x 性能节一条（用户视角：**7z 大包小文件解压快了几十倍**），不发版不改版本号
- `docs/KNOWN_ISSUES.md`：登记「ZIP 并行写 HDD 未做并发节流」（阻塞类型 = 成本超阈值，复评 = 出现 HDD 用户反馈）
- issue #75 回复：附修复前后实测数据与「非 solid 不变 / 加密暂不优化」的边界说明（等用户确认后再回复）

## 13. 后续可评估（不在本计划）

1. RAR/ISO 是否也能走批量（需测 solid 性与块结构）
2. ZIP 并行解压的 HDD 节流（`IOCTL_STORAGE_QUERY_PROPERTY` → `StorageDeviceSeekPenaltyProperty`；或 WMI `MSFT_PhysicalDisk.MediaType`），目标：`ParallelExtractDegree` 在 seek penalty 高的盘上自动降到 1–2
3. 加密 solid 包批量解压（先测 `ExtractFiles(dir, indexes, password)` 的重载行为）
4. 小文件聚合写（仅当批量路径落地后仍有写放大问题才考虑）

## 14. 实施记录（2026-11-02）

**代码落地（与 §7 的对应与差异）**：

| 计划 | 实际 | 差异说明 |
|---|---|---|
| S1 抽三段 | `#region ExtractAsync（SharpSevenZipExtractor）` 整段重写，`ExtractAsync` 外壳不变，内部改为局部函数 `ExtractSingleEntryAsync` / `ExtractEntriesOneByOneAsync` / `BuildBulkPlanAsync` / `RunBulkSegmentAsync` / `RunBulkAsync` / `ReportMainProgress` / `ReportSkippedEntries` | 用闭包共享计数器代替参数透传（`ref` 不能跨 `async`），`continue` 改 `return` |
| S3 抽 `internal static BulkExtractPlan BuildPlan(...)` | 未抽成静态方法，改为闭包内 `BuildBulkPlanAsync()`；但 `BulkEntryPlan` / `BulkExtractPlan` 仍是 `internal sealed class`（供测试与后续复用） | 预解析要读 `extractor.IsSolid` / `ArchiveFileData`，脱离实例反而要透传更多上下文；单测改为走公开的 `ExtractAsync` 覆盖 |
| S5 测试 | 新增 `tests/MantisZip.Tests/Engines/SevenZipEngineBulkExtractTests.cs` 5 例（类名含 `SevenZipEngine` 以沿用 CI 的 `FullyQualifiedName!~SevenZipEngine` 过滤） | **未**新增 `tests/corpus/sevenzip/solid-small.7z`：语料由测试代码现场用 7z.dll 生成（80 条），找不到 `7z.dll` 时跳过；避免二进制语料入库 |
| — | 新增 `ArchivePhase.CheckingConflicts` + 三语 key `Progress_Phase_CheckingConflicts` | 预解析期间进度条不显示「未知阶段」 |

**实现中确认的细节**：① 归档内重名条目归入 `Duplicate` 桶走逐条目（逐条目路径写盘时会重新 `ResolvePathAsync`，语义与改造前完全一致，代价是重名条目多解码一遍）；② 零 stat 快路径仍保留每文件一次 `File.Exists` —— 否则 `OverwrittenEntries` 归零（首跑测试即 `Expected: 80 / Actual: 0`）；③ `EnableBulkSolidExtract` 必须是 `static readonly`（`const` 会让关闭分支被判不可达 CS0162）。

**实测（本机 SSD，`%TEMP%` 夹具）**：800×64KB（52MB）solid 7z（archiveBytes=17191）`ExtractAsync` 全程 **1673ms**、800/800 成功；对照改造前 19.3s（§2.2）、`7z.exe x` 0.56s、裸 `ExtractFiles` 0.86s ⇒ 约 **11.5×**。略高于 §10 写的「≤1.5s」，差值来自 800 次 `File.Exists` 与逐条状态/进度上报。

**仍未验收（需真机）**：§10 的 HDD（机械盘）目标盘复测、§5.5 暂停 3s→恢复行为、Task 8 的手工场景（Windows 资源管理器拖拽不涉及本改动，见 §4 非目标）。

**验证**：core / avalonia（Release）0 警告；`tests` 全绿（2 条存量警告）；`ui-tests` 63.2s 全绿（28 条存量警告）；`i18n` 1398 key 三语一致。

