# 选中条目 / 拖拽解压改走批量接口（solid 性能）

> **状态**：✅ 实现完成（2026-11-02；等真机 HDD 实测后归档）| 创建 2026-11-02
> **关联**：issue #75「解压海量小文件到机械硬盘时速度极慢」
> **前置**：[sevenzip-solid-extract-batch.md](sevenzip-solid-extract-batch.md)（全量解压批量分流）。本计划复用它的 `BulkExtractPlan` 与 staging 生命周期，**建议先做那份**
> **分支**：`beta`

## 1. 一句话

`SelectedItemsExtractService` → `SevenZipEngine.ExtractEntriesAsync` 现在是「遍历整个归档、只提取选中的条目、每条一次 `ExtractFile`」。solid 归档里每次 `ExtractFile` 都要把所在块重新解压一遍 ⇒ 选中 N 条就是 N 次块解码，选中一个大目录（几千个文件）时和全量解压一样慢。改成「先按命中条目批量解码到暂存目录，再按各自落点移动」后，多次解码变成一次。

## 2. 现状锚点

- **入口只有一个**：`src/MantisZip.UI.Avalonia/Services/SelectedItemsExtractService.cs:29-102`。勾选解压与拖拽解压共用它（`src/MantisZip.UI.Avalonia/Services/DragDropItemExpander.cs` 只负责把选中的目录展开成文件列表，文件头注释即写明「输出路径计算已统一到 `SelectedItemsExtractService`（右键/拖拽同一语义）」）⇒ 一份计划覆盖两条路径。
- **落点已经算好**：`SelectedItemsExtractService.cs:42-53` 构造 `pathOverrides[key] = FileConflictHelper.GetSafePath(destinationPath, ExtractPathResolver.ResolveRelativePath(key, currentFolder, preserveFullPath))`。扁平化 / 保留完整路径 / 同名改名的差异全在这张表里，引擎只管照着写。
- **引擎逐条目**：`src/MantisZip.Core/Engines/SevenZipEngine.cs:1057-1283`。`await Task.Run(...)` 内 `using var extractor`，遍历**全部**归档条目、`keySet` 跳过未选中的，每条 `new FileStream(...)` + `extractor.ExtractFile(entry.Index, progressStream)`。进度口径是**文件计数**（`handled / totalTarget`），单文件百分比来自 `WriteProgressStream` 的字节。
- **目录条目忽略 override（既有缺陷，顺带修）**：`:1103-1109` 用 `FileConflictHelper.GetSafePath(destinationPath, fileName)` 建目录，**没查 `outputPathOverrides`** ⇒ 扁平化时选中的空目录仍按归档路径落盘。
- **其它引擎不用动**：`src/MantisZip.Core/Engines/ZipEngine.Extract.cs:753` 已有顺序/并行两套（`:1004 ExtractEntriesAsyncParallel`），ZIP 无 solid 二次方问题；`src/MantisZip.Core/Engines/TarGzEngine.cs:777` 同理 ⇒ 只碰 7z。
- **测试现状**：`SevenZipEngine.ExtractEntriesAsync` 没有直接单测，只有 `tests/MantisZip.Tests/Engines/ParallelExtractTests.cs` 与几个 `ExtractPreserveFullPath*Tests.cs` 间接覆盖。

## 3. 三个硬约束

1. **不能直接落到最终路径**：批量 API 只接受「一个目录 + 一组索引」，写出来的是归档内布局；而选中路径每条都有任意 `targetPath`（扁平化可能把 `sub/a.txt` 和 `sub2/a.txt` 都映射到 `<dest>\a.txt`）。⇒ 必须「批量落暂存 → 逐个移动到目标」。
2. **批量写盘会静默覆盖**：`ExtractFiles` 不做冲突判定，`skip` / `rename` / `OverwriteIfOlder` 语义会全丢。⇒ 冲突必须在批量**之前** 100% 预解析。
3. **`File.Move` 只有同卷才是瞬移**：暂存目录必须在目标目录内（`<dest>\.mantiszip-staging-<guid>\`），跨卷就退化成真拷贝，比逐条目还慢。

## 4. 方案

### 4.1 分流条件（全部满足才走批量）

`engine is SevenZipEngine` && `IsSolid` && 选中的非目录条目数 ≥ 2 && 无密码 && 无越界落点 && `options` 里没有 `ErrorResolver`（有 resolver 时逐条目才能保证「每条失败可单独重试」的现有语义）。

单条选中直接走原路径 —— 为 1 个文件建暂存目录、做一次移动没有收益。

### 4.2 流程

1. **解析命中条目**：复用现有 `keySet` 逻辑，产出 `(index, archiveName, targetPath)` 三元组；`targetPath` 取自 `outputPathOverrides`，没有 override 时回退 `GetSafePath(destinationPath, fileName)`。
2. **冲突预解析**：复用全量解压计划的 `BulkExtractPlan.BuildPlan(...)`，新增「显式落点」模式（全量解压用相对布局，选中用显式落点）：
   - 每条直接对 `targetPath` 做 `File.Exists` + `FileConflictHelper.ResolvePathAsync`；
   - 维护**已认领落点集合**（`HashSet<string>`，OrdinalIgnoreCase）：多个条目落到同一路径时，第二个起按冲突策略处理，`Rename` 算出的 `a (1).txt` 也要写回集合；
   - `Ask` 在批量前集中问完 —— `SelectedItemsExtractService.CreateExtractOptions:127-168` 的 `applyToAll` / `chosenAction` 闭包已经有这个记忆，直接沿用。
3. **批量解码到暂存**：`extractor.ExtractFiles(stagingDir, indexes)`（保持归档相对布局）。
4. **逐条移动**：`staging\<archiveName>` → 预解析出的最终路径（先建父目录，`File.Move(src, dst, overwrite: true)`），随后 `File.SetLastWriteTime(finalPath, entry.LastWriteTime)`。
5. **清理暂存**：`try/finally` + 启动时按前缀清扫上次残留（与全量解压计划 §5.2.1 同一实现）。
6. **失败处理**：移动失败按 `EntryFailureReason.IoFailed` / `PermissionDenied` 逐条上报并继续；批量段整体失败时只对「未完成的条目」（`bucket.Direct − finished`）走逐条目重试。

### 4.3 空目录

选中的目录条目按 override 计算落点后 `Directory.CreateDirectory(resolved)`，顺带修掉 §2 里「忽略 override」的不一致。

### 4.4 进度口径

主百分比改成 `max(字节比, 文件计数比)`（防倒退，与全量解压计划同口径）；批量阶段 `CurrentFile` 显示「正在解码 N 个条目」，移动阶段显示「正在移动 N/M」。

### 4.5 取消与暂停

批量阶段在 `FileExtractionStarted` 里查 `cancellationToken` → `e.Cancel = true`；移动阶段逐条查。暂停沿用阻塞式进度回调 —— `ExtractEntriesAsync` 整体已在 `Task.Run` 内（`:1076`），卡的是线程池线程，不是 UI 线程。

## 5. 决策表

| # | 决策 | 理由 |
|---|------|------|
| D1 | 只做 7z solid | ZIP 已有并行、TAR 无 solid，改了没收益还多一套逻辑 |
| D2 | 选中 ≥ 2 条才批量 | 单条建暂存 + 移动是纯开销 |
| D3 | 暂存目录在目标目录内 | 同卷 `File.Move` 是元数据操作 |
| D4 | 冲突 100% 预解析 | 批量不会替我们判 `skip` / `rename` |
| D5 | `Ask` 在批量前集中问 | 与全量解压计划一致，避免解到一半弹框 |
| D6 | 复用 `BulkExtractPlan`，加「显式落点」模式 | 两套分流逻辑会各自长歪 |
| D7 | 目录条目也走 override | 顺带修既有缺陷，属同一处代码 |
| D8 | 进度取 `max(字节, 计数)` | 批量阶段只有字节信息 |
| D9 | 批量失败只重试未完成条目 | 已落地的重跑会覆盖用户刚改的文件 |
| D10 | 不动 ZIP / TAR | 见 D1 |
| D11 | 保留逐条状态上报 | 进度窗批次列表与「跳过 / 覆盖」计数都靠它；批量后按预解析结果补报，成本可忽略 |
| D12 | 目录条目 override 修复纳入本计划 | 与 D7 同一处代码，单独提交更碎 |

## 6. 实施步骤

| 步骤 | 内容 | 工时 |
|---|---|:--:|
| S1 | `SevenZipEngine` 拆三段：`ExtractEntriesOneByOneAsync`（现有逻辑原样搬出）、`ExtractEntriesBulkAsync`（新增）、分流入口；`BulkExtractPlan` 加显式落点模式 | 3-4h |
| S2 | `SelectedItemsExtractService` 把「是否加密 / 是否有 resolver / override 表」传给引擎（`ArchiveOptions` 已经带着，必要时加一个 internal 重载） | 1-2h |
| S3 | 暂存生命周期（建 / 清 / 前缀清扫）复用全量解压计划实现 | 1h |
| S4 | 测试 `tests/MantisZip.Tests/Engines/SevenZipSelectedBulkExtractTests.cs`（语料复用 `tests/corpus/sevenzip/solid-small.7z`） | 4-5h |
| S5 | 全量门禁 + HDD 真机复测 + 文档 | 2-3h |

合计 **2–2.5 天**（单独做）。**顺序已定：先做 [sevenzip-solid-extract-batch.md](sevenzip-solid-extract-batch.md) 的全量解压，看真机实测收益再决定本计划何时启动。**

## 7. 测试用例（12 条）

1. 选中 100 条（solid）→ 只做一次批量解码，落点与逐条目结果一致
2. 扁平化后两个条目同落点 → `skip` 只留一个 / `rename` 得 `a (1).txt`
3. 保留完整路径 → 落点含子目录
4. 选中空目录 → 目录按 override 建出（§2 缺陷的回归用例）
5. `Skip` 冲突 → 不落盘且上报 `Skipped`
6. `Rename` 冲突 → `(1)` 名 + 时间戳保留
7. 批量阶段取消 → 目标目录不留半成品
8. 单条选中 → 走原路径，不创建暂存目录
9. `ErrorResolver` 存在 → 走原路径
10. 加密包 → 走原路径
11. 拖拽路径（`preserveFullPath = false`）与勾选路径落点一致（扩展现有 `ExtractPreserveFullPathContractTests`）
12. 上次残留的暂存目录被清扫

## 8. 风险

- **收益依赖「暂存与目标同卷」**：由 D3 保证；若目标目录只读导致暂存建不出来 → 整批退回逐条目。
- **进度颗粒度变粗**：批量阶段看不到单条目名，用「正在解码 N 个条目」表达，避免假装有细粒度进度。
- **与全量解压计划耦合**：两份计划共用 `BulkExtractPlan`，必须同批或先后紧接实施，否则会并存两套分流。**建议顺序：全量解压先落，本计划只加显式落点模式。**
- **移动阶段不是零成本**：几千个小文件的 `File.Move` 仍是几千次元数据操作；验收要把「批量解码 + N 次 rename」与「N 次解码」实测对比，不能只看理论。

## 9. 验收

1. 选中 200 条（solid，目标 HDD）耗时落进「一次批量解码 + 200 次 rename」量级，明显优于逐条目。
2. 落点与改造前 **100% 一致**（含扁平化、重名、空目录）。
3. 单条选中、加密包、`ErrorResolver`、非 solid 的行为与耗时不变。
4. `CoreLog` 打出 `ExtractEntriesAsync: bulk solid selected <N> entries`，能事后判断走没走批量。

## 10. 文档与发版

- `docs/ARCHITECTURE.md` 补一句「选中条目分流条件与暂存目录」；
- `docs/progress-avalonia-detail.md` + `docs/PROGRESS.md` 按性能条目记；
- `RELEASE_NOTES.md` 与全量解压合并成一条（规则 18：一到两句）；
- issue #75 一并回复（**待你确认后再发**）。

## 11. 不做

- 不动 ZIP 并行与 HDD 并发节流（全量解压计划的 §13 遗留）；
- 不做「选中条目跨包批量」（一次只解一个包）；
- 不新增任何用户开关 —— 分流是内部决策。

## 12. 2026-11-02 拍板（3 条已定）

| 问题 | 决定 | 落地 |
|---|---|---|
| 同批还是分批 | **先只做全量解压，看实测再决定本计划** | 保持「待实施」，§6 已写明顺序 |
| 逐条状态上报 | **保留** | 见 D11：批量后按预解析结果补报每条 `Completed` / `Skipped` / `Overwritten` |
| 空目录 override 修复 | **放进本计划** | 见 D7 / D12 / §4.3 |

## 13. 实施记录（2026-11-02）

### 13.1 与计划的差异

| 计划 | 实际 | 原因 |
|---|---|---|
| 新增 `BulkExtractPlan.BuildPlan(...)` 的「显式落点模式」 | 在 `ExtractEntriesAsync` 内写局部函数 `BuildBulkPlanAsync()`，直接复用现有 `BulkEntryPlan` / `BulkExtractPlan` 两个类型 | 落点表在 `SelectedItemsExtractService` 里、引擎只拿到 `outputPathOverrides`；局部函数能直接闭包访问 `keySet` / conflictStats / unitsDone，不必给类型加一堆参数 |
| 批量阶段用「正在解码 N 个条目」概括进度 | 用 `FileExtractionStarted.FileInfo.FileName` 报真实条目名 + 真实文件百分比 | 事件里本来就有名字与 `PercentDelta`，假装粗颗粒反而丢信息；进度**不会**倒退，因为 `unitsDone` 只在解码完成 / 移动成功 / 跳过 / 失败时 +1 |
| §4.1「无越界落点」作为闸门 | 越界条目改成 `Unsafe` 桶、只把该条目退回逐条目，不整体禁用批量 | 用户选 500 条时有一条落点异常不该让整批退回逐条目 |

### 13.2 实现要点

- 闸门 `CanUseBulk()`：`EnableBulkSolidExtract && extractor.IsSolid && password == null && options?.ErrorResolver == null && selectedFiles.Count >= 2 && !任何选中条目 Encrypted`。
- 预解析 `BuildBulkPlanAsync()`：`ArchivePhase.CheckingConflicts` → 目录进 `Directories`（**用 override 落点**，D7 修复）→ 越界进 `Unsafe` → `ResolvePathAsync` 为 null 记 `RecordSkipped()` + 只上报 `Skipped` → 落点重复进 `Duplicate` → ≥64MB 进 `BigFile` → 其余进 `Direct`。
- 执行 `RunSelectedBulkAsync()`：补建空目录 → 上报跳过项 → `CleanupStaleStagingDirectories` → `ExtractFiles(<dest>\.mantiszip-staging-<guid>, indexes)` → 逐条 `File.Move(overwrite: true)` + `SetLastWriteTime`，**移动成功才**计数与上报 `Completed` / `Overwritten` → `finally` 删暂存 → `BigFile ∪ Unsafe ∪ Duplicate ∪ manualFallback` 走逐条目。
- 进度单位 `unitsDone`（跳过 / 失败 / 已解码待移动 / 已移动 / 逐条目各 1），`ProcessedFiles` 与 `ExtractResult.SucceededEntries` 仍只统计真正落到最终路径的文件。
- 计划 §9.4 要求的日志已加：`ExtractEntriesAsync: bulk solid selected {N} entries, per-entry {M}, skipped {K}`；另有每段 `ExtractEntriesAsync: bulk segment {N} entries -> {stagingRoot}`。

### 13.3 实测

- 800×64KB（52MB）solid 7z，全选解压：批量 **2250ms** / 逐条目 9957ms ⇒ **4.4×**（逐条目这一侧用「设置 `ErrorResolver` ⇒ 闸门不成立」强制走回旧路径，两侧落盘文件数都是 800）。

### 13.4 验证

- `build -Target core/avalonia -c Release`：均 0 警告；新增 `SevenZipEngineSelectedBulkExtractTests` 10 例全过；`tests` / `ui-tests` / `i18n`（1398）/ `check` / `guards`（8）全绿。

### 13.5 仍未验收

- 真机机械硬盘上的收益（当前实测盘为 SSD）；
- 「先只做全量解压」的原始拍板已被用户 2026-11-02 推翻为「两个入口都做」。
