# 进度窗口字节埋点 + 语言刷新 + 计划回写 — 设计规格

- **日期**: 2026-10-06
- **状态**: 已获用户确认（分节呈现，第 1-5 节全部通过）
- **关联计划**: `.omo/plans/未开始/progress-window-prototype-alignment.md`（Deferred 第 7 项回写）

## 1. 目标与非目标

### 目标

1. **速度/ETA 全格式生效**：`SevenZipEngine`、`TarGzEngine` 的所有进度报告（解压/压缩/追加/删除重建/单文件 .gz）填充 `ArchiveProgress.ProcessedBytes/TotalBytes`，使 `ProgressViewModel.SpeedText`/`RemainingText` 从「仅 ZIP 显示」变为全格式显示。`ZipEngine` 已完成，不改动。
2. **进度窗口语言即时刷新**：`ProgressViewModel` 订阅 `LocalizationManager.CultureChanged`，运行中切换语言时静态标签立即刷新，对齐 `PreviewViewModel` 等 5 个既有先例。
3. **计划文本如实回写**：`progress-window-prototype-alignment.md` Deferred 第 7 项已被 commit `7505952` 实施，更新为已完成状态并记录偏差；同步 `docs/PLAN.md`（规则 1）。

### 非目标

- 不动 `ZipEngine`（已完成）。
- 不动 `ProgressViewModel` 的速度/ETA 算法（`SpeedTracker`/`ComputeEtaSeconds` 契约不变，只是喂数据）。
- 不做 `progress-bar-segments` 计划（`SegmentProgressBar`，P2 另行处理）。
- 不做跨格式速度基准统一（TAR/GZ 解压显示压缩基准速度，见 §2.5-3）。
- 不新增本地化 key（速度/剩余时间文案模板已存在，本次纯数据侧）。
- 不提交代码——本规格获批后先出实施计划；任何 `git commit` 先按规则 3 更新进度文档并经用户确认。

### 成功判据

- 7z / TAR / GZ 三种格式的解压与压缩，进度窗口均显示速度与剩余时间；
- 运行中切换语言，进度窗口静态标签即时变化；
- 构建 0 错误、既有测试全过（Core 608 + Avalonia 114 基线），新增埋点测试通过。

## 2. 引擎埋点细节（方案 A：引擎就地埋点）

选型过程：对比「A 引擎就地埋点 / B Core 共享抽象 / C ViewModel 层反推」，用户选定 **A**——改动集中在 3 个引擎文件、零新抽象、与 ZipEngine 既有 100+ 处模式一致、各报告点知道自己手里的字节基准，最准确。

> **行号约定**：本文所有 `Lxxx` 均为 2026-10-06 代码快照，实施时以**方法名/符号定位**为准，行号仅作参考线索。

### 2.1 SevenZipEngine 解压（ExtractAsync L482 / ExtractEntriesAsync L892）

- `TotalBytes` = 条目未压缩大小之和（`allEntries` L421 已持有，纯算术）。
- `ProcessedBytes` = `WriteProgressStream` 的 `bytesWritten` 跨文件累计。
- 报告点 L468（常规报告，无 EntryStatus）、L494（写入中节流）、L523（完成）均带上累计值。
- 中途跳过/失败条目按 ZipEngine L315 先例：跳过时 `processedBytes += entry.Size`，避免进度卡死。
- 基准：**未压缩**，与 Zip 一致。

### 2.2 SevenZipEngine 压缩（AttachCompressorProgress L202-227）

- 签名增加 `totalSourceBytes`：
  - `TotalBytes = totalSourceBytes`
  - `ProcessedBytes = (long)(totalSourceBytes * accumulatedPercent / 100.0)`（`accumulatedPercent` 即现有 `PercentDelta` 累加器；百分比推算已获用户批准，ZipEngine L1564 先例）。
- 三个调用点接线：
  - **L591 CompressAsync**：当前在 `ExpandSourcePaths`（L596）之前挂接 → **重排**为先展开源路径求总字节、再挂接。
  - **L1160 AddToArchiveAsync**：源文件列表已知，直接传总字节。
  - **L1090 DeleteEntriesAsync**：现有包装用 `50 + 0.5×p` 映射 → 字节同步映射 `totalSource × (50+0.5p)/100`，保持百分比与字节一致。
- 基准：**未压缩源文件**，与 Zip/7z 解压一致。

### 2.3 TarGzEngine 解压

- **tar 路径**：`TotalBytes = inputStream.Length`、`ProcessedBytes = inputStream.Position`（与既有 PercentComplete 同源，直接复用到报告点）——**压缩基准**。
- **单文件 .gz**（L194 / L799 的 `gzipStream.CopyTo(output)`）：改为手动节流复制循环（100ms 节流，参照 ZipEngine 缓冲复制模式），按 `inputStream.Position/Length` 报告，约 15 行。
- 注：gzip 缓冲使 `Position` 略领先于实际解压进度，与现有 PercentComplete 同基准，可接受。

### 2.4 TarGzEngine 压缩

- `TotalBytes` = 源文件大小总和（`files` 列表 + `TarWriteFileWithRetry` L353 既有 `FileInfo`）。
- **粒度**：源读取流包一层计数流（100ms 节流回调），大文件压缩中速度连续更新而非逐文件跳变；`.gz` 单文件压缩（`gzipWriter.Write` L303-315）同样用计数流包装输入。
- 报告点：计数流回调（100ms 节流）内构造**不带 `EntryStatus` 的常规 `ArchiveProgress` 报告**（携带 `ProcessedBytes/TotalBytes` + `PercentComplete`/文件计数），即穿进 `SetProgress` L517 之后采样代码的那类；既有 L290-300 带 `EntryStatus` 的状态报告**不能**作为字节采样点（见 §2.5-1）。
- 基准：**未压缩源文件**，与 Zip/7z 压缩一致。

### 2.5 关键陷阱（实施时必须遵守）

1. **EntryStatus 早返回**：`ProgressViewModel.SetProgress` L512-517 对带 `EntryStatus` 的报告早返回，不进速度采样。字节只能挂在**穿过 L517 之后采样代码**的常规报告上（判定标准即此；7z L468 符合，TarGz L290-300 状态报告不符合）。
2. **字节单调性**：`ProcessedBytes` 只增不减；跨条目累加，禁止用当前条目内的局部值覆盖全局累计。
3. **速度基准差异（接受）**：TAR/GZ 解压速度显示为压缩流基准（gzip 5:1 时数字约为 Zip 的 1/5），同格式内 processed/total 基准一致故 ETA 正确；统一基准需解压前全扫，不做。
4. **单实例过滤**：`ExtractEntriesAsync`（过滤解压）的 `TotalBytes` 只计入选条目，不是全量。

## 3. 进度窗口语言刷新（#2）

- `ProgressViewModel` 构造中订阅 `LocalizationManager.CultureChanged += OnCultureChanged`（先例 `PreviewViewModel.cs:166`）。
- `OnCultureChanged`：重灌 `LocalizedStrings` 字典 + `OnPropertyChanged(nameof(LocalizedStrings))`。
- **派生文案立即刷新**：缓存最近一次 `SetProgress` 的**原始数值**（速度 字节/秒、剩余秒数、文件计数），语言切换时用新语言的模板重新渲染成字符串（不缓存成品字符串——缓存字符串则无从换模板；否则暂停/完成状态下要等下次进度 tick 才变）。
- **退订（泄漏陷阱）**：`ProgressViewModel` 随 `ProgressWindow` 每次操作新建、非模态 `.Show()`，必须在 `ProgressWindow.OnClosed`（现有 L699-710 清理段）退订静态事件，否则累积死 VM。
- 非静态、随进度动态更新的文案（当前文件名等）下次 tick 自然刷新，不特殊处理。

## 4. 计划文本回写（#3）

- `.omo/plans/未开始/progress-window-prototype-alignment.md` Deferred 第 7 项 → 状态改为**已实施**（commit `7505952`）：
  - 实际接线：`CompressFlow.TrySeedEntryItemsInBackground`（L332）→ `SourceEntryEnumerator.Enumerate`（后台目录枚举）；GUI `MainWindow.axaml.cs:277` + CLI `App.axaml.cs:2233`；`MaxSeedEntryCount=5000` 对齐。
  - **偏差如实注明**：原文恢复条件是「零额外 I/O（压缩请求已持有完整文件列表）」，实际付出一次后台目录扫描。
- 同步 `docs/PLAN.md` 对应行（规则 1）。
- 不动 Deferred 其余项（#1/#4/#5/#6 维持不做；#2 属实；#3 最有价值——判断维持）。

## 5. 验证与测试

- **构建**：Core + Avalonia `dotnet build` 0 错误；`lsp_diagnostics` 干净。
  - 配方：`dotnet build tests\<proj> -p:BuildProjectReferences=false` + `dotnet test ... --no-build`；UI 构建加 `-p:SkipShellExtCopy=true`。
- **测试**：
  - 既有测试全过（Core 608/0/3、Avalonia 114/0/2 基线）。
  - 新增：7z/TarGz 压缩与解压产出的 `ArchiveProgress` 序列断言 `TotalBytes > 0` 且 `ProcessedBytes` 单调不减（复用 `SevenZipEngineTests` 既有压缩测试模式，解压回读比对）。
  - 语言刷新测试对齐 `PreviewViewModel` 既有 CultureChanged 模式（无先例则以 key 集断言兜底）。
- **手工 QA**（用户执行）：7z/TAR/GZ 压缩与解压观察速度/ETA 出现且数字合理；运行中切语言看标签即时变化。
- **提交**：实施完成、验证通过后，按规则 3 先更新进度文档、规则 10 写 conventional commit，经用户确认再提交。
