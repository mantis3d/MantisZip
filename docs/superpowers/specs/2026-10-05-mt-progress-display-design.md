# ZIP 并行压缩（N 组自主并行）设计

- 日期：2026-10-05
- 状态：设计已定稿，待用户评审
- 关联原型：`C:\Users\Admin\AppData\Local\Temp\opencode\compress-channel-demo.html`
- 关联代码：`ZipEngine.CompressAsync`、`ZipBinaryRewriter`、`ProgressViewModel.UpsertParallelBatch`

---

## 1. 背景与动机

当前 ZIP 的 MT 压缩走 **7z 原生多线程**（`mt=on`），存在两个已实测的问题：

1. **性能负收益**：`scripts/bench-zip-mt.cs` 实测 `mt=on` 比纯自适应路径**慢 29%–56%**。
2. **无细粒度进度**：`CompressGroupWithSevenZip` 中 `Compressing` 事件的 `PercentDelta` 在
   `text/media/mixed` 三个语料上 **96% 的采样为空**（实测 `started=60, compressing=0, finished=60`）。
   7z 把并行压缩放在 DLL 内部，调用方拿不到任何 per-thread 归因。

因此 UI 无法为压缩端画出与解压端等价的「通道视图」（多条真实进度条）：
解压端的并行由 MantisZip 自己掌控（`ExtractAsyncParallel` 的 `Parallel.ForEachAsync` 分批），
每批独立上报 `BatchIndex`；而压缩端的并行在 7z 内部，无对应事件。

**用户决策**：放弃 7z 原生 MT，改为 **MantisZip 自主管理 N 组并行**——
每组一个独立 `SharpSevenZipCompressor`（`mt=off`），N 组并行执行，每组都有真实的
`Compressing` 百分比与文件归因，从而复用现成的通道视图 UI。

---

## 2. 目标

| # | 目标 | 验收方式 |
|---|------|---------|
| G1 | 压缩端 UI 展示 N 条**真实**进度条（通道视图） | 进度窗口「详细」模式显示 N 行，每行百分比随实际压缩推进 |
| G2 | 整体性能优于现有 `mt=on` | bench：N=4/8/16 对比 serial 与 `mt=on` |
| G3 | 压缩产物**解压后内容与串行路径逐字节一致** | 新增 round-trip 测试。注意：ZIP 文件本身的字节**不要求**与串行路径相同（分组不同 → 条目顺序与 deflate 块边界不同），G3 约束的是解压后的内容 |
| G4 | 不破坏加密 / 分卷 / Deflate64 / copy-mode 语义 | 现有 572 项 Core 测试全绿 |

## 3. 非目标（本次明确不做）

- **加密 ZIP 不纳入 N 组并行**。原因有二：其一，现行加密路径（`ZipEngine.cs:1288` 附近）
  根本没有设置 `mt=on`，本就是单次 7z 调用；其二，`ZipBinaryRewriter` 的 copy-mode
  不支持加密条目。加密 ZIP 保持现状（单次 7z 调用 + AES）。
- **分卷（`SplitSize > 0`）不纳入**。分卷依赖 `SplitOutputStream` 包装输出流，
  与「N 个独立临时 ZIP 再合并」的模型冲突。保持现状走串行路径。
- **非 copy-mode 压缩方法不纳入**（BZip2 / LZMA / PPMd）。见 §4.6 回退条件。
- 不改 7z 格式压缩路径（`SevenZipEngine`）的 `mt=on` 行为——本次只动 ZIP。
- 不改进度窗口的**版式与视觉语言**。通道视图复用 `ProgressWindow.axaml` 既有的
  `DetailedPanel` / `ParallelBatchItems` / `RatioToWidthConverter` / `ProgressBarSizeBrush`
  / `Progress_Batch_FilesProgress`，**不新增任何资源、转换器或本地化 key**。
  但**确有 4 处代码改动**：通道行加 `FileRatio` 属性 + `UpsertParallelBatch` 赋值 +
  文件名格换 `Grid[Rectangle, TextBlock]` + 模板加详情列绑 `DetailText`。
  逐条见 §5.2.1。
  设置窗口另需 1 个并行度选择控件（§6），同样复用既有控件样式。

---

## 4. 架构

### 4.1 当前管线（`ZipEngine.CompressAsync` MT 分支）

```
FileScanner.CollectFiles
  → storeGroup (level==0) / compressGroup (level>0)   [ZipEntryClassifier.GetAdaptiveLevel]
  → IsMultiThreadedEligible?  ── 否 ──→ 标准串行 ZipWriter 路径
  → CompressGroupWithSevenZip(compressGroup, tempZip, mt=on)   ← 唯一 7z 调用点
  → storeGroup 为空 ? File.Move(tempZip → output) : ZipBinaryRewriter.RewriteAsync(tempZip, +storeEntries)
  → ZipCommentHelper.WriteComment
```

### 4.2 目标管线

```
FileScanner.CollectFiles
  → storeGroup / compressGroup                     [不变]
  → IsMultiThreadedEligible?  ── 否 ──→ 标准串行 ZipWriter 路径      [不变]
  → SplitCompressGroup(compressGroup, N)          [新增：按大小降序 round-robin]
  → Parallel.ForEachAsync(N 组, MaxDegreeOfParallelism = N)
        每组：CompressGroupWithSevenZip(subGroup_i, tempZip_i, mt=off, progress→BatchIndex=i)
  → 收集 tempZip 路径列表
  → ZipBinaryRewriter.RewriteAsync(sourcePaths: [tempZip_0..N-1], +storeEntries)
  → ZipCommentHelper.WriteComment
```

**关键不变式**：`compressGroup` 内所有文件使用同一个 `options.CompressionLevel`
（自适应只决定 store / compress 二分，不在组内分级），因此按大小轮转分组
不会破坏自适应语义，各组级别天然统一。

### 4.3 分组策略

`SplitCompressGroup(compressGroup, N)`：

1. 按 `FileInfo.Length` **降序**排序。
2. **轮转分配**：`groups[i % N].Add(file)`，其中 `i` 为排序后下标。

选择轮转而非连续切块的理由，与 `ExtractAsyncParallel` 一致：大文件天然分散到不同组，
不需要额外的贪心装箱。降序排列同样是为了打散长尾。

**空组裁剪**：若某组分不到文件（`N > compressGroup.Count`），从组列表中移除并
重新编号，避免通道视图出现 0% 且永不推进的空行。有效组数记为 `effectiveN`。

### 4.4 七 Zip 实例并发

- `SevenZipEngine.EnsureLibraryPath()`（`SevenZipEngine.cs:105`）**已确认线程安全**：
  双检 + `lock (_libraryLock)`，且失败路径也置 `_libraryPathInitialized = true`（`:145`），
  避免重复弹窗。因此并发调用是安全的。
  但仍**要求在 `Parallel.ForEachAsync` 扇出之前调用一次**，而不是每组各调一次——
  语义更清晰，且避免 N 次无谓的锁竞争与重复日志。
- 每个组持有**独立的** `SharpSevenZipCompressor` 实例；7z.dll 支持多实例并发，
  每实例内部状态独立。
- `CompressGroupWithSevenZip` 现有的 `reportLock` 只保护**本组内**的共享变量，
  组间无共享状态——这是该方法可以安全并发复用的前提。
- **必须修改**：`:3063` 的 `compr.CustomParameters["mt"] = "on"` 改为按参数传入，
  N 组路径传 `"off"`。串行回退路径仍传 `"on"`（保持现有行为不变）。

### 4.5 重写器扩展（主要改动点）

`ZipBinaryRewriter.RewriteAsync` 当前签名只接受单个 `sourcePath`
（`ZipBinaryRewriter.cs:287`）。N 组需要 N 个源。

**方案**：新增多源重载，保留原单源签名并令其委托到多源实现（`sourcePaths.Count == 1`
时走原有快路径，零额外开销，向后兼容）。

多源语义：

- 按 `sourcePaths` 顺序**串行**遍历各源，逐条目原样复制 LFH + 压缩数据 + CDFH。
  合并是纯字节拷贝（单线程顺序 I/O），不构成分并行瓶颈——并行收益在压缩阶段，
  合并阶段本来就是 IO bound 的追加写。
- **条目名冲突防御**：若同一 `EntryName` 出现在多个源中，视为重复条目。
  正常流程下 `compressGroup` 内文件名唯一（来自同一份 `FileScanner` 结果），
  故实际不会触发；但必须显式检测并抛清晰异常，不能静默产生重复 ZIP 条目。
- 进度：`BatchIndex = null`（合并阶段不分批），按已完成条目字节 / 总条目字节上报
  真实百分比。

### 4.6 回退条件（任一命中 → 标准串行 `ZipWriter` 路径，行为与今天完全一致）

| 条件 | 来源 |
|------|------|
| `options.MultiThreadedCompression == false` | `IsMultiThreadedEligible:2952` |
| 加密 ZIP（`Encrypt && Password` 非空） | `IsMultiThreadedEligible:2956` |
| `SplitSize > 0` | `IsMultiThreadedEligible:2960` |
| 条目数 > 65535 − 256（ZIP32 上限） | `IsMultiThreadedEligible:2968` |
| 总字节 ≥ `uint.MaxValue − 1`（ZIP32 上限） | `IsMultiThreadedEligible:2977` |
| `compressGroup.Count == 0`（无可压内容） | `ZipEngine.cs:1422` |
| 混合场景且 `!CanCopyModeRewrite(method)` | `CanCopyModeRewrite:3007`，仅放行 `deflate` / `deflate64` / `store` |
| **`N <= 1`**（新增） | `ParallelCompressDegree` 解析后 ≤ 1 |
| **有效组数 < 2**（新增） | 分组后裁剪掉的空组导致只剩一组 |

回退时必须输出与现有路径同风格的 `CoreLog.Info` 原因行，便于诊断。

---

## 5. 进度契约

### 5.1 复用既有 `ArchiveProgress` 批次字段（不新增契约字段）

`ArchiveProgress` 已有字段（`ArchiveEngine.cs:312`）足以表达 N 组进度，
**不新增任何字段**：

| 字段 | 压缩端填充语义 |
|------|--------------|
| `BatchIndex` | 组索引（0-based） |
| `BatchCount` | 有效组数 N |
| `BatchPercentComplete` | 该组已压字节 / 该组总字节 × 100 |
| `BatchProcessedFiles` / `BatchTotalFiles` | 该组已完成 / 总文件数（通道行 "N/M 文件"） |
| `FilePercentComplete` | 该组**当前文件**的字节进度（驱动文件名底纹） |
| `CurrentFile` | 该组当前文件名 |
| `PercentComplete` / `ProcessedBytes` / `TotalBytes` | 全局：已完成组字节 + 各组当前字节之和 / 全部待压字节 |
| `EntryKey` + `EntryStatus` | 该组内单文件完成时上报 `Completed` |

### 5.2 UI 侧复用既有通道视图（4 处小改动）

- **进度窗口的「详细」面板已存在且已绑定**（已读源码核实）：
  - `ProgressWindow.axaml:332` `DetailedPanel`，`IsVisible="{Binding IsDetailedMode}"`
    （`IsDetailedMode` ⇒ `_contentMode == ProgressContentMode.Detailed`，`ProgressViewModel:207`）。
  - `:339` `<ItemsControl ItemsSource="{Binding ParallelBatchItems}">`。
  - `UpsertParallelBatch`（`:603`）消费 `BatchIndex` / `BatchPercentComplete` /
    `BatchProcessedFiles` / `BatchTotalFiles`，写入 `ParallelBatchProgressItem`。
  - **`HasParallelBatches`（`:235`）与 `IsDetailedAvailable`（`:222`）均以
    `_parallelBatchItems.Count > 0` 判定**，由 `CollectionChanged` 驱动通知，与操作类型无关。
    压缩端一旦上报 `BatchIndex` 即自动满足。
  - **「详细」是 opt-in 单选项，且按钮本身受 `IsDetailedAvailable` 门控**
    （`ProgressWindow.axaml:284` `IsVisible="{Binding IsDetailedAvailable}"`）。
    集合为空时该单选项**不渲染**，用户无法切到详细模式——
    这才是「不显示空详细面板」的真实机制。
  - **`IsDetailedAvailable` 只在集合非空后变true，不会自动选中详细模式**：
    通道行到达后用户仍需手动点「详细」。压缩端接线时须确认这个交互
    （是否要 N≥2 时自动切 Detailed），见 §5.4.1。
  - **更正**：本设计此前称 `:838-839` 有「集合为空自动切回 Simple」的保护——
    经核实该代码位于 `_batchItems[index].Status = ...`（`:826`）之后的
    **多档案切换私有路径**（Separate 模式），**不是通用兜底**。
    通用保障依赖的是上面的 `IsDetailedAvailable` 门控。
- **门控属性名更正**：本设计此前写作 `_contentMode == ProgressContentMode.Detailed`，
  实际 XAML 绑定的是派生属性 `IsDetailedMode`（`:207`）。语义等价，但实施时
  不应去找 `_contentMode` 的可见性判断。
- **进度窗口需要 4 处小改动（不是"零改动"）**——逐条见 §5.2.1。

### 5.2.1 通道行的 4 处改动（复用既有资源）

原型通道行比现有 XAML 模板多两样东西，都需要改：

| # | 文件 | 改动 | 对应原型元素 |
|---|------|------|------|
| 1 | `Models/ParallelBatchProgressItem.cs` | 新增 `[ObservableProperty] private double _fileRatio;`（**0–1**，非 0–100，与 `SizeRatio` 口径一致） | 文件名底纹 |
| 2 | `ProgressViewModel.UpsertParallelBatch`（`:603`） | `row.FileRatio = Math.Clamp(p.FilePercentComplete ?? 0, 0, 100) / 100.0;` | 文件名底纹 |
| 3 | `ProgressWindow.axaml:365-371` | 通道行 Column 1 的纯 `TextBlock` 换成 `Grid[Rectangle, TextBlock]` | 文件名底纹 |
| 4 | `ProgressWindow.axaml:343` | 模板由 `Auto,*,*,Auto`（4 列）扩为 `Auto,*,*,Auto,Auto`（5 列），新增 Column 4 绑定 `DetailText` | 「N/M 文件」详情列 |

**改动 4 的依据**：`UpsertParallelBatch:616` 已经用
`LocalizationManager.T("Progress_Batch_FilesProgress", processed, total)`
算好 `DetailText`，而该 key 在 `strings.zh-CN.json` / `strings.en.json` /
`strings.zh-TW.json` 三语**均已存在**（值分别为 `{0}/{1} 文件` / `{0}/{1} files` /
`{0}/{1} 個檔案`）——但 `ProgressWindow.axaml` **从未绑定 `DetailText`**，
即这份数据算出来后被丢弃。新增一列绑定即可让既有计算生效，无需改 VM。

**照搬文件列表既有写法**（`MainWindow.axaml:1144-1158` 已验证）：

```xml
<Grid>
  <Rectangle Fill="{DynamicResource ProgressBarSizeBrush}" HorizontalAlignment="Left">
    <Rectangle.Width>
      <MultiBinding Converter="{StaticResource RatioToWidthConverter}">
        <Binding Path="FileRatio" />
        <Binding Path="Bounds.Width" RelativeSource="{RelativeSource AncestorType=ContentPresenter}" />
      </MultiBinding>
    </Rectangle.Width>
  </Rectangle>
  <TextBlock Text="{Binding CurrentFile}" VerticalAlignment="Center" Margin="4,0" />
</Grid>
```

**复用清单（无新增资源）**：`RatioToWidthConverter`、`ProgressBarSizeBrush`
均已存在于 App 层级。遵守 Rule 4（主题样式）与 Rule 6（显隐而非禁用）。

### 5.3 必须修改的 UI 逻辑缺陷

`ProgressViewModel.SetProgress` 中 `EntryStatus` 的早返回分支（**`:457-462`**，已读源码确认）：

```csharp
if (p.EntryStatus.HasValue && !string.IsNullOrEmpty(p.EntryKey))
{
    UpdateEntryStatus(p.EntryKey, ...);
    return;                      // ← 在此返回
}
...
if (p.BatchIndex.HasValue)      // ← :582，远在早返回之后
    UpsertParallelBatch(p);      //    :603
```

携带 `EntryStatus` 的逐条目报告（`EntryKey` 非空）在 `:461` 就返回，
**永远走不到 `:582` 的批次 upsert**。后果按可见性排序：

1. **`row.Percent` 停止刷新（主因，可见）** —— `:375` 绑定了 `Value="{Binding Percent}"`。
   通道进度条会**卡在旧值不动**，直到下一条不带 `EntryStatus` 的报告到来。
   对 N 组并行压缩而言这是硬伤：每组都在持续产出带 `EntryStatus` 的逐文件完成事件，
   早返回会**系统性冻结所有通道条**。
2. `row.DetailText`（"N/M 文件"）同样不刷新——但改动 4（§5.2.1）之前它根本没被渲染，
   属于潜在问题而非当前可见缺陷。

**修复**：把 `if (p.BatchIndex.HasValue) UpsertParallelBatch(p);` 上移到 `:457` 早返回**之前**，
使批次字段在任何退出路径前都被消费。`p.BatchIndex` 为 null 时行为不变。

### 5.3.1 新发现的约束：`_isBatchMode` 必须保持 false（已读源码确认）

`SetProgress:481-486` 有两条互斥的总百分比分支：

```csharp
if (_isBatchMode && _batchItems != null && _batchItems.Count > 1)   // :481
    PercentComplete = ComputeOverallPercent(...);                  // 多档案「串行」加权
else
    PercentComplete = (int)p.PercentComplete;                       // :489 引擎值直通
```

`_isBatchMode` / `_batchItems` 服务的是**多压缩包逐个压缩**（Separate 模式）的档案切换，
与本设计的**并行通道**是两套独立机制（后者走 `_parallelBatchItems`）。

**约束**：N 组并行压缩时 `_isBatchMode` 必须保持 `false`，从而走 `:489` 的直通分支。
§5.1 定义的全局字节加权 `PercentComplete` 因此天然被正确采纳，**总进度无需任何改动**。

> 反面风险：若误让压缩路径进入 `_isBatchMode`，`:483` 的
> `ComputeOverallPercent(currentBatchIndex, ...)` 会用档案序号重新加权，
> 把我们上报的全局百分比覆盖成错误的档案级进度。实施时须在
> §7.1 增加一条断言：N 组并行压缩产出的总进度 == 全局字节加权值。

### 5.4 阶段划分（仅用于日志诊断，**不实现 UI**）

> 原型 `compress-channel-demo.html` 里的「阶段视图」切换**本次不实现**。
> 原型的作用是让你在两个方案间做选择；既然选了通道视图（N 条真实进度条），
> 阶段清单就不进产品。阶段信息只写进 `CoreLog`，便于排查"卡在哪一段"。

| 阶段 | 权重（仅诊断参考） | 进度来源 |
|------|------------------|---------|
| 扫描收集 | 5% | `FileScanner` 逐文件（真实） |
| N 组并行压缩 | 80% | 各组 `Compressing`（真实，N 条独立） |
| 合并写入 | 15% | `ZipBinaryRewriter` 逐条目字节（真实） |
| 收尾 | 0% | 瞬时（写注释 / 清理 temp） |

对比旧 `mt=on` 路径：阶段 2 是黑盒（只能 Started / Finished 两个点），
阶段 3 同样是真实字节进度。**新路径的三个阶段全部有真实数据。**

**UI 呈现**：压缩全程显示底部单条总进度（`PercentComplete`，全局字节加权）+
N 行通道进度。总进度因此**全程单调、可信**，不再出现 `mt=on` 时代
"进度条停在某个值不动" 的观感——这是 G1 附带解决的老问题。

### 5.4.1 串行 / 回退时的通道视图（**已定：方案 B**，改动仅一处）

已核实的代码事实：

- `BatchIndex` 全仓库仅 4 处赋值（`ZipEngine.cs:625` / `:703` / `:1141` / `:1220`），
  **全部在并行解压路径内**；压缩路径当前**一处都没有**。
- 串行压缩的上报形态（`ZipEngine.cs:1545-1551`）不含任何批次字段：
  ```csharp
  new ArchiveProgress {
      CurrentFile = relativePath,
      PercentComplete = pct,
      FilePercentComplete = 100,
      TotalFiles = totalFiles,
      ProcessedFiles = processedFiles   // 无 BatchIndex / BatchCount / BatchPercentComplete
  }
  ```
- 无 `BatchIndex` → `if (p.BatchIndex.HasValue)` 不成立 → `ParallelBatchItems` 恒空
  → **一行通道都不渲染**（不是「1 个通道」）。

因此串行或回退时**不会**出现「单通道视图」，只有简约模式。两种情形：

| 情形 | BatchIndex | 通道行 | 「详细」单选项 | 窗口呈现 |
|---|---|---|---|---|
| N ≥ 2 且无回退 | 有 | N 行 | 出现，**且自动选中** | **通道视图（默认）** |
| N = 1（串行） | 无 | 0 行 | **不渲染**（`:284` 门控） | 简约 |
| N ≥ 2 但命中回退<br>（加密 ZIP / 分卷 / BZip2 / LZMA / PPMd） | 无 | 0 行 | **不渲染** | 简约 |

**决策：方案 B（自动切详细）。** 依据：

- `IsDetailedAvailable` 变 true 后**不会**自动选中（`:222` 只驱动按钮可见性），
  即现状下 A 成立。
- 但 A 与 G1 冲突：并行压缩的**全部目的**就是让用户看到多条真实进度条。
  选 A 时，窗口开启后的默认画面与本次改动之前完全相同（简约= 路径行 +
  当前文件名 + 单条当前文件进度条，见 `ProgressWindow.axaml:294-328`），
  通道成果默认不可见，用户须自行发现「详细」多出来的那个单选项。
- B 的实现成本已核实为**一个守卫 + 一处赋值**：`ContentMode` 是 public setter
  （`ProgressViewModel.cs:190`），全仓库当前仅由三个单选项 Click 写入
  （`ProgressWindow.axaml.cs:425` / `:431` / `:437`），无其他订阅者，
  故从 VM 内部写入无副作用、无重入风险。

**接线点**（VM 内部）：`ProgressViewModel.UpsertParallelBatch` 中，
首个 `BatchIndex` 到达、集合由空转非空时自动切换
（**arrival-triggered**，非启动时判定）：

```
// ProgressViewModel.UpsertParallelBatch 内，集合由空转为非空的那一次
if (_parallelBatchItems.Count == 1 && _contentMode == ProgressContentMode.Simple)
    ContentMode = ProgressContentMode.Detailed;
```

**为何不能用「启动时一次成型」判定**（已核实，属实现不可行）：

`IsMultiThreadedEligible(options, totalEntryCount, totalSize)`（`ZipEngine.cs:2950`）
需要 `files.Count` 与 `totalBytes`，二者来自 `FileScanner.CollectFiles`
（`ZipEngine.cs:1247`），位于 `CompressAsync` 内 `Task.Run`（`:1244`）中；
而进度窗口早在 `MainWindow.axaml.cs:275` `pw.Show()` 就已显示，`:284` 才 await 操作。
§4.6 的 9 个回退条件里有**4 个在窗口打开时不可知**：

| 依赖 | 条件 | 启动时可知 |
|---|---|---|
| `options` | `MultiThreadedCompression==false`、`Encrypt&&Password`、`SplitSize>0`、`!CanCopyModeRewrite`、`N<=1` | ✅ |
| `files.Count` | 条目数 > 65279 | ❌ |
| `totalBytes` | 总量 ≥ `uint.MaxValue−1` | ❌ |
| 分组结果 | `compressGroup.Count==0`、有效组数 < 2 | ❌ |

若强行乐观预设为 `Detailed` 而后命中上述 4 条回退，则 `ParallelBatchItems` 恒空
→「详细」单选项被 `:284` 隐藏，但 `_contentMode` 仍是 `Detailed`
→ `IsDetailedMode` 为真、DetailedPanel 空且SimplePanel 被遮蔽
→ **空白的进度窗口**。故启动时判定不仅不可行，乐观预设本身也不安全。

**arrival-triggered 天然自愈**：回退路径**永不上报 `BatchIndex`**
（`ZipEngine.cs` 中 4 处 `BatchIndex` 赋值全在并行解压路径），
故回退时集合永不变非空，自动切换永不触发，窗口稳定停在简约模式。
无需任何回退兜底逻辑，也不依赖 `:838-839`（§5.2 已更正其为多档案私有路径）。

**不覆盖用户显式选择**：守卫条件 `_contentMode == ProgressContentMode.Simple`
已足够，**无需新增「用户是否手动切换过」标志**。因默认即 `Simple`
（`ProgressViewModel.cs:54`），而三个 Click 处理器只把它改写为
`Simple` / `Detailed` / `List`（`ProgressWindow.axaml.cs:425` / `:431` / `:437`）：

| 用户在首个批次到达前的动作 | `_contentMode` | 自动切换 | 是否符合预期 |
|---|---|---|---|
| 无（默认） | `Simple` | 触发 | ✅ |
| 点「列表」 | `List` | 不触发 | ✅ 不顶掉用户选择 |
| 点「详细」 | `Detailed` | 不触发 | ✅ 已是详细 |
| 再点一次已选中的「简约」 | `Simple` | 触发 | ⚠ 见下 |

残留边界：末行用户在首个批次到达前主动点了一次已默认选中的「简约」。
因 `IsChecked="True"` 硬编码于 `ModeFullPathRadio`（`ProgressWindow.axaml:279`）
且同属 `GroupName="ContentMode"`，重复点击已选中项不改变状态，
故不构成有意义的「显式选择」；且此时「详细」单选项尚未出现
（`IsDetailedAvailable` 仍为 false，`:284` 隐藏），用户无从表达
「我想留在简约」的意图。判定为可接受，不加标志位。

**若改选方案 A**：删除上述 `UpsertParallelBatch` 内的守卫与赋值
（共 2 行代码）即可，其余全部设计（§5.1–§5.3 通道行、§4 并行引擎、
§6 设置）不变，差异仅这一处。

---

## 6. 设置

新增 `AppSettings.ParallelCompressDegree`：

- 类型 `int`，默认 `Environment.ProcessorCount`（**与既有 `AppSettings.ParallelExtractDegree`
  同为 `ProcessorCount`**，`AppSettings.cs:78`；`ArchiveOptions` 侧默认 `0` 表示运行时解析，
  两者不对称是既有约定，勿"修正"）。
- `1` = 串行。按 §4.6，`N < 2` **回退标准串行 `ZipWriter` 路径**，
  与 `MultiThreadedCompression=false` 走**同一条代码路径**，
  既不创建独立 7z 压缩器，也不做多源合并（不产生 tempZip）。
- 取值范围 `0`–`16`，设置窗口 UI 限制 1–16，与 `ParallelExtractDegree` 对齐。
- 解析规则（与解压一致）：
  ```
  N = options.ParallelCompressDegree
  if (N <= 0) N = Environment.ProcessorCount
  if (N > 16)  N = 16
  ```

**传递链**（对齐 `ParallelExtractDegree` 的既有链路，逐层加字段）：

```
AppSettings.ParallelCompressDegree
  → CompressSettingsViewModel          （默认值取自 AppSettings，NumericUpDown 限制 1..16）
  → CompressFlow.BuildRequest → CompressRequest.ParallelCompressDegree
  → CompressService.BuildOptions → ArchiveOptions.ParallelCompressDegree
  → ZipEngine.CompressAsync / AddToArchiveAsync
```

> **已核实：`SettingsWindowViewModel` 中不存在任何 degree 属性。** extract 侧的
> `ParallelExtractDegree` UI 在 `Dialogs/ExtractSettingsWindow.axaml:127-138`，绑定的是
> `ViewModels/ExtractSettingsViewModel.cs:45`；其运行时消费点在 `ExtractFlow.cs:182`
> 与 `SelectedItemsExtractService.cs:58`。压缩侧按**对称位置**落地到
> `CompressSettingsWindow` + `CompressSettingsViewModel`，**不经过 `SettingsWindowViewModel`**。

UI 位置：压缩设置窗口 ZIP 格式面板，紧邻现有的自适应压缩开关；
7z 面板的多线程复选框（`SevenZipMultithreaded`）**保持不变**（那是 7z 格式的功能）。

---

## 7. 测试与验证

### 7.1 单元测试（`tests/MantisZip.Tests`）

| 测试 | 断言 |
|------|------|
| `SplitCompressGroup_DescendingSortMakesGroup0TheHeaviest` | 降序 + 轮转 ⇒ 组 0 逐元素支配其他组，故其字节和 == 全组最大值（可证性质，非经验断言） |
| `SplitCompressGroup_SumImbalanceBoundedByGroupSizeRatio` | 设 `m` = 文件数、`N` = 组数、`q = floor(m/N)`，则 `maxSum / minSum ≤ (q+1)/q`（组大小最多差 1 个元素） |
| `SplitCompressGroup_TwoLargestFilesLandInDifferentGroups` | 最大与次大文件（降序后位于下标 0 和 1）分别落在组 0 与组 1 |
| `SplitCompressGroup_DropsEmptyGroups` | `N > 文件数` 时返回组数 == 文件数，无空组；`BatchIndex` 重新压实为 `0..N-1` |
| `SplitCompressGroup_SingleFile_ReturnsOneGroup` | 触发 §4.6 的「有效组数 < 2」回退 |
| `RewriteAsync_MultiSource_ProducesIdenticalEntriesToSingleSource` | 两源合并结果与单源 + store 追加结果，条目名集合与顺序一致 |
| `RewriteAsync_MultiSource_DuplicateEntryName_Throws` | 跨源重名抛清晰异常，不静默产生重复条目 |
| `CompressAsync_Degree1_ProducesSerialEquivalentOutput` | N=1 产出与 N=8 产出**解压后逐字节相同** |
| `CompressAsync_Parallel_MixedSource_RoundTripsExactly` | 混合 Store + Deflate64 + 文本 + 媒体，解压逐字节比对 + `TestArchiveAsync` |
| `CompressAsync_Parallel_ReportsGlobalByteWeightedPercent` | 全程采集中断点断言：`PercentComplete` 严格等于「已完成组字节 + 各组当前字节」/ 全部待压字节；**且**不得被 `ComputeOverallPercent` 覆盖（防 §5.3.1 的 `_isBatchMode` 误入） |

### 7.2 回归

- `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 572 项全绿（基线）。
- `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 114 项全绿，
  **并新增以下 UI 层测试**（VM 级，无需真实压缩）：
  - `UpsertParallelBatch_WithBatchFields_CreatesRowWithFileRatio`：
    喂入 `BatchIndex=2` / `BatchPercentComplete=40` / `FilePercentComplete=25` /
    `BatchProcessedFiles=3` / `BatchTotalFiles=8`，断言 `ParallelBatchItems.Count==2`（1-based `Index==2`）、
    `Percent==40`、`FileRatio==0.25`、`DetailText` 含 `3/8`。
  - `SetProgress_WithEntryStatus_StillUpsertsParallelBatch`：
    喂入**同时**含 `EntryKey`+`EntryStatus` 与 `BatchIndex` 的 `ArchiveProgress`，
    断言 `ParallelBatchItems` 仍被更新——直接锁死 §5.3 的回归。
  - `FileRatio_ClampedToUnitRange`：`FilePercentComplete` 为 null / 0 / 100 / 150 时
    `FileRatio` 分别为 0 / 0 / 1 / 1（`Math.Clamp` 生效，防 150 撑破底纹宽度）。
- 加密 ZIP / 分卷 / BZip2 / LZMA / PPMd 各自保持现有行为（回退路径必须被测试覆盖）。
- **XAML 编译期验证**：`ProgressWindow.axaml` 改完后 `RatioToWidthConverter` 必须能被解析到——
  该转换器定义在 App 层级资源字典，若不可见会在 `dotnet build` 阶段报错，
  属"编译即失败"的高信号检查，不需要运行时验证。

### 7.3 性能验证（`scripts/bench-zip-mt.cs` 扩展）

已读源码核实，扩展点与既有结构：

| 位置 | 现状 | 需要的改动 |
|------|------|-----------|
| `:52` `Config` record | `(Id, Label, Adaptive, MultiThreaded)` | 加 `int Degree`（0 = 不适用） |
| `:61` 选项装配 | 只设 `MultiThreadedCompression` | 设 `ParallelCompressDegree = Degree` |
| `:65-74` `Configs.All` | **4 项固定枚举** `Config[]` | 见下方「维度展开策略」 |
| `:793-807` 参数解析 | 扁平 `switch (a)` | 加 `case "--degree"`（轻量） |
| `:313`/`:324` `ConfigSummary`/`ProfileSummary` | 输出无 degree | 加 degree 列，否则表格无法归因 |

**维度展开策略（关键，避免运行时爆炸）**：
`degree` 是**连续维度**，而 `Configs.All` 是固定枚举。若把 4 个布尔配置 × 5 个 degree
交叉展开成 20 项，再乘 3 语料 × N reps，单次 bench 会跑到不可接受。

采用**两级结构**：`Configs.All` 保持不变（仍是自适应/多线程的布尔矩阵），
`--degree` 作为**外层循环**，对每个 config 依次跑 degree ∈ {1,2,4,8,16}，
`ConfigSummary` 按 `(config.Id, degree)` 为键聚合。

**基线定义（避免重复统计）**：
- `--degree 1` ≡ 现有 serial。按 §4.6，`N < 2` 回退标准串行 `ZipWriter` 路径，
  与 `MultiThreadedCompression=false` **走同一条代码路径**，因此只需跑一次，
  bench 脚本内以 `degree=1` 结果同时充当 serial 基线，不再单列 config D。
- `MultiThreadedCompression=true` + 默认 degree（现有 `mt=on` 实现）为 **G2 对照基线**。

**语料与门槛**：在 `text` / `media` / `mixed` 三个语料上对比，
每个配置仍执行字节一致性校验（`OK` 才算通过）。
验收门槛：至少一个 N 值在三个语料上**快于 `mt=on`**（G2）。

> 运行时预估：4 config 中只有 2 个（B/C）需要跑完整 degree 扫描，
> 其余复用 serial 基线；即 1 + 5（serial degree 扫描）+ 5（mt=on 对照）
> ≈ 11 组 × 3 语料 × reps。实施时先以 `--reps 3` 粗测，确认可接受再提高精度。

---

## 8. 风险与缓解

| 风险 | 影响 | 缓解 |
|------|------|------|
| 7z.dll 多实例并发不稳定 | 崩溃 / 数据损坏 | §7.1 的 N=8 混合语料 round-trip 测试；bench 前先跑压力测试 |
| N 个 tempZip 同时占磁盘 | 峰值磁盘 = 完整压缩包体积（N 个文件，总量不变） | 复用现有 `tempZip` 命名 + `finally` 清理；清理覆盖取消路径 |
| 进度报告锁竞争 | 性能回退（AGENTS.md 记录过 25x 回退） | 组间无共享状态；沿用现有「锁内拷贝、锁外 Report」模式；bench 实测确认 |
| 合并阶段成为新瓶颈 | 整体加速不及预期 | 合并是顺序字节拷贝，IO bound；若 bench 显示占比 > 25% 则重新评估 |
| 条目名跨源冲突 | 产出损坏 ZIP | §7.1 显式异常测试 |
| `ProgressViewModel` 早返回缺陷 | 通道行文件数不刷新 | §5.3 修复 + 通道视图手动验证 |

---

## 9. 实施顺序

1. `SplitCompressGroup` 纯函数 + 单元测试（无外部依赖，先落地）
2. `ZipBinaryRewriter` 多源重载 + 单元测试
3. `CompressGroupWithSevenZip` 的 `mt` 参数化
4. `ZipEngine.CompressAsync` 接线 N 组并行 + 回退条件
5. `ArchiveOptions.ParallelCompressDegree` + 传递链 + 设置窗口 UI
6. 进度窗口接线（UI 层，可与 5 并行）：
   - `ProgressViewModel` 早返回缺陷修复（§5.3，主因是 `row.Percent` 被冻结）
   - §5.2.1 改动 1–2：`ParallelBatchProgressItem.FileRatio` + `UpsertParallelBatch` 赋值
   - §5.2.1 改动 3：通道行文件名格换 `Grid[Rectangle, TextBlock]`
   - §5.2.1 改动 4：模板扩 5 列 + 绑定既有 `DetailText`
   - §5.4.1 方案 B：`UpsertParallelBatch` 内集合由空转非空时，若用户未手动切换过
     内容模式则置`Detailed`（arrival-triggered，**不得**改为启动时乐观预设——
     §4.6 有 4/9 条件在窗口打开时不可知，乐观预设会导致空白窗口）
7. bench 扩展 `--degree` + G2 验证
8. 全量测试 + 手动通道视图验证

第 1–4 步是 Core 层，不依赖 UI，可独立验证与提交。

第 6 步完成后**不能**用现有 7z 路径（`mt=on`）验证通道视图：
已核实 `SevenZipEngine.cs` 中 `BatchIndex` 赋值数为 **0**（全仓库仅
`ZipEngine.cs` 有 4 处，且全在解压路径），故 `mt=on` 只上报
Started/Finished 两个点，通道行会渲染 **0 行**——这不是"1 行"，
而是与串行路径同样不可见。通道视图的手动验证必须等第 4 步
（N 组并行接线）完成后进行。