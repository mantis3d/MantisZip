# 压缩/解压出错信息增强（整包错误本地化 + 逐条目详情透传 + 失败原因分布）

> **状态**: 🚧 进行中（2026-10-15 立项；**任务 1–8 已实施**，任务 9 GUI 人工验收待做，任务 10 提交按用户指示）
>
> **For agentic workers:** 执行时使用 superpowers:subagent-driven-development 或 superpowers:executing-plans。Steps use checkbox (`- [ ]`) syntax for tracking。提交策略：默认**不 commit**；仅当用户明确要求提交时执行收尾任务，且按规则 3 先更新双轨进度文档。

## TL;DR

> **Quick Summary**: 让用户在进度窗口**看懂失败原因**——三阶段：**P1 修 bug**（App.axaml.cs 两处硬编码中文违反规则 13 + TarGz/7z 失败事件缺 FileTotalBytes 导致统计卡字节缺失）；**P2 低成本 UI 增强**（整包错误 `ex.Message` 直传改本地化映射、红/黄摘要段头部加失败原因分布行）；**P3 Core 契约扩展**（`ArchiveProgress.EntryErrorDetail` 透传 ex.Message 原文——**修订既有 spec D2 的"不透传"决策**，仅进 tooltip 不进摘要正文；IOException 按 Win32 错误码细分出磁盘满/文件占用/路径过长可操作提示）。高成本项（详情面板/重试/导出）明确不做，见 Follow-ups。
>
> **Deliverables**:
> - `Core/Abstractions/ArchiveEngine.cs`：`ArchiveProgress.EntryErrorDetail`（`string?`）
> - 三引擎 catch 块填充 detail + TarGz/SevenZip Failed 事件补 `FileTotalBytes`
> - `UI/Services/WholeArchiveErrorLocalizer.cs`（新）：Exception → 整包错误本地化文案
> - `UI/Services/IoErrorHintResolver.cs`（新）：HResult → 可操作提示（磁盘满/占用/路径过长）
> - `ViewModels/ProgressViewModel.cs`：失败原因分布行 + detail 存储 + `SetErrorSummary` 增 detail 参
> - `Models/EntryProgressItem.cs` + 条目列表 XAML：Failed/Skipped 行 tooltip 显示原因+详情
> - `App.axaml.cs` / `ExtractFlow.cs` / `MainWindow.axaml.cs`：硬编码修复 + 整包错误本地化接线
> - 三语 JSON 新 key ~10 组；`WholeArchiveErrorLocalizerTests`/`ProgressViewModelTests`/引擎测试扩充
> - `docs/PLAN.md` 登记行更新（规则 1）
>
> **Estimated Effort**: Medium-Large（10–13h：P1 ≈2h，P2 ≈3–4h，P3 ≈5–7h）
> **Parallel Execution**: PARTIAL — Wave P1 内 Task 1/2 可并行（无文件交集）；其余顺序
> **Critical Path**: P1 修复 → P2 i18n → P2 VM 分布行 → P3 Core 契约 → P3 VM tooltip/提示 → FINAL 验证

---

## Context

### Original Request

2026-10-15 用户要求调研「压缩/解压链路的出错信息，评估还能向用户展示哪些细节」，调研完成后用户选定计划范围为 **P1+P2+P3 分阶段全量**（不动 P4 高成本项）。

### Research Findings（2026-10-15 快照，执行时以 rg/读文件复核为准）

**现状链路**：引擎 catch → `ArchiveProgress{EntryKey, EntryStatus, EntryFailureReason(4类), FileTotalBytes}` → `ProgressViewModel.CollectEntryOutcome` 红/黄段聚合 → 摘要行 `{key} 解压失败：{4类本地化文案之一}`（20 行封顶 + 复制按钮）。`ex.Message` **只进 CoreLog→debug.log**，不进摘要（既有 spec `progress-window-status-line.md` D2 决策："不透传 ex.Message"——本计划 **P3 修订该决策**，但仅加字段+tooltip，摘要正文格式不变）。

**已确认的缺陷/缺口**（rg 2026-10-15）：

1. **规则 13 违规 ×2**：`App.axaml.cs:1218-1219` 与 `:2352-2353` 硬编码 `"{N} 个文件未能写入到 {dir}（可能权限不足）"`——未走 `LocalizationManager.T()`，且"可能权限不足"是猜测，可能与下方逐条目实际原因（CRC 损坏等）矛盾。
2. **失败字节统计缺斤短两**：`rg -n "ArchiveEntryStatus.Failed" src\MantisZip.Core\Engines` 显示 TarGzEngine 8 处（:160/:169/:241/:249/:919/:928/:1014/:1022）与 SevenZipEngine 4 处（:599/:622/:1134/:1157）的 Failed 上报**均不带 `FileTotalBytes`**（ZipEngine 带）→ 统计卡"失败大小"对 TAR/7z 恒缺失。
3. **整包错误中英混杂**：`SetErrorSummary` 约 8 个调用点直传 `ex.Message` 英文原文（`App.axaml.cs:1282/:1458/:1660/:2394`、`MainWindow.axaml.cs:365`、`ExtractFlow.cs:138`），与逐条目行的本地化文案同块显示。
4. **IoFailed 是信息黑洞**：磁盘满/文件被 Word 占用/路径过长全部归 `Error_Reason_IoFailed`（"I/O 错误（文件被占用或磁盘异常）"），用户无法据此行动。
5. **注释误导（顺手修）**：`ZipEngine.cs:682` 注释"异常对象不在作用域"实为条目丢失（`entry == null`）分支，根本无异常。
6. **交互点信息不对称**：压缩预检 `ErrorDialog`（`ErrorDialog.axaml.cs:41`）已展示完整 `ex.Message`，但最终摘要只剩 4 类粗分类。

**既有契约锚点**（`progress-window-status-line.md` 已实施，勿回退）：
- 红段 = 整包头 + Failed 行；黄段 = 压缩端**带原因** Skipped；黄段不触发 `HasErrors`（D4）；解压冲突跳过（无原因）不进黄段（D5）
- 摘要行格式 `Progress_Failed_EntryFmt` = `"{0} 解压失败：{1}"`；`SummaryMaxLines = 20`；`CollectEntryOutcome` 按 EntryKey 去重 first-wins
- `FailureReasonMapper.FromException`（Core）：UnauthorizedAccess→PermissionDenied，IOException→IoFailed，InvalidFormatException 含"CRC"→CrcMismatch，其余→Other
- `ReadErrorHandler.FilterUnreadableFiles(..., onSkipped)` 预检剔除已上报带原因 Skipped

**测试基线锚点**：`tests/MantisZip.Tests/Utils/FailureReasonMapperTests.cs`（3 条）、`tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs`（红黄段 ~12 条）、`AboutWindowTests.AllThreeLanguages_HaveSameKeySet`（三语 key 集校验）。

---

## Work Objectives

### Core Objective

失败时用户能**立即看懂发生了什么、下一步怎么做**：整包错误说人话（本地化）、逐条目摘要给出原因分布、tooltip 可见异常原文与可操作提示，且统计卡字节对三引擎一致。

### Definition of Done

- [ ] `dotnet build src\MantisZip.Core\MantisZip.Core.csproj` → 0 错误
- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误
- [ ] `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全绿（含新增引擎测试）
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿（含新增 VM/本地化测试）
- [ ] `git grep -n "未能写入到" -- 'src/*.cs'` → 无命中（硬编码清除，改由 key 承载）
- [ ] `rg -n "ArchiveEntryStatus.Failed" src\MantisZip.Core\Engines` → 每处 `ArchiveProgress` 初始化器含 `FileTotalBytes`（能取得字节处）且含 `EntryErrorDetail`（异常在作用域处）
- [ ] GUI 手动验收清单（Task 9）逐条通过
- [ ] `docs/PLAN.md` 已同步（规则 1）

### Must NOT Have（护栏）

- **Core 不新增任何中文用户可见文案**（规则 13）；`EntryErrorDetail` 只允许塞库/系统异常英文原文
- **摘要正文行格式不变**：仍是 `key + 本地化原因`；detail 只进 tooltip，不进 `ErrorSummaryText` 正文行
- 不改 `IArchiveEngine` 签名、不动 `EntryFailureReason` 4 类枚举成员集（细分靠 detail + UI 侧 hint，不扩枚举）
- 不破坏 D4/D5：黄段不触发 `HasErrors`；解压冲突跳过仍不进黄段
- 取消（`OperationCanceledException`）路径不写摘要、不上报 Failed detail
- 不做 P4 高成本项：详情面板、从失败重试、报告导出（见 Follow-ups）
- 不回退 `progress-window-status-line.md` 已实施行为（20 行上限、去重、自动钉 📌、复制=红+黄）
- 压缩预检 `ErrorDialog` 行为不变（它已展示 ex.Message，无需动）

### Verification Strategy (MANDATORY)

**Test Decision**: TDD（Task 4/5/6/8 有新单测；Task 1/2/7 为机械修复，以 rg 断言 + 构建 + 既有测试回归验证）。

```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
rg -n "ArchiveEntryStatus.Failed" src\MantisZip.Core\Engines
git grep -n "未能写入到" -- 'src/*.cs'
```

---

## 设计决策表（D1–D8）

| # | 决策 | 内容 | 理由 |
|---|------|------|------|
| **D1** | 修订既有 D2 | `ArchiveProgress` 新增 `EntryErrorDetail`（`string?`），仅 `Failed`/带原因 `Skipped` 时填充，值 = `ex.Message` 原文 | 原 D2 丢弃消息导致用户无法行动；本修订**只加字段不动既有语义**，摘要正文仍只显示 4 类分类 |
| **D2** | detail 只进 tooltip | 红/黄摘要 TextBox tooltip = reason 本地化 + detail 原文（如有）；`ErrorSummaryText` 正文不变 | 保持摘要紧凑可扫读；原文按需 hover 查看 |
| **D3** | 整包错误本地化 | 新 `WholeArchiveErrorLocalizer.ToUserText(Exception)`：FileNotFound/DirectoryNotFound/UnauthorizedAccess 各有专属文案，`InvalidDataException` 直接透传（thrower 已本地化 Status_ArchiveCorrupted），其余回退 `ex.Message` | 消除中英混杂；类型映射不动 thrower 侧，避免双重本地化 |
| **D4** | 分布行 | 红段在整包头（可无）之后插入一行 `共 N 个失败：权限不足 2 · 数据损坏 1`；黄段同构；从既有 `_failedEntries`/`_skippedEntries` 计数，**不新增数据结构** | 用户一眼看到失败构成；纯 VM 计算零 Core 改动 |
| **D5** | IoFailed 细分不扩枚举 | UI 侧新 `IoErrorHintResolver`：`ex.HResult & 0xFFFF` 匹配 Win32 码（112/39 磁盘满、32/33 占用、206 路径过长）→ 本地化可操作提示，拼进 tooltip | Core 保持事实分类；枚举扩成员会波及 mapper/三语/测试全链路，收益低 |
| **D6** | TarGz/7z 补字节 | Failed 上报补 `FileTotalBytes`（条目字节数在各引擎 catch 作用域内均已知或可取） | 统计卡一致性 bug 修复 |
| **D7** | SetErrorSummary 增参 | `SetErrorSummary(string message, string? detail = null)`——既有调用零改动；本地化改造后的调用点传 detail=ex.Message，红段整包行 tooltip 显示原文 | 向后兼容 |
| **D8** | 条目行 tooltip | `EntryProgressItem` 增 `FailureReasonText`/`FailureDetail`（VM 在 CollectEntryOutcome 时同步写入），列表 Failed/Skipped 行 tooltip 显示 | 与摘要 tooltip 一致，列表视图用户同样能查原文 |

### Follow-ups（本计划不做，另立）

- 摘要行可点击展开详情面板 / 从失败条目重试 / 结构化失败报告导出（P4）
- CRC expected/actual 具体值提取（依赖 SharpCompress 异常消息格式，当前仅透传原文）
- `App.axaml.cs` 两处修复后，排查其余 `SetErrorSummary` 调用点是否有同类猜测性文案

---

## 任务分解

> **执行方式**: superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，checkbox 逐步勾选。
> **提交策略**: Task 1–9 默认不 commit；仅用户要求时执行 Task 10 收尾（规则 3 先更新进度文档）。

### 执行波次

```
Wave P1（bug 修复，可并行）
├── 任务 1: App.axaml.cs 硬编码中文 → 本地化 key（规则 13 修复）
└── 任务 2: TarGz/7z Failed 事件补 FileTotalBytes + Zip 注释修正

Wave P1.5（打开阶段分类修复 · ✅ 已实施 2026-10-15）
└── 任务 11: 打开阶段损坏/加密分类（方案 B：7z StartHeader / RAR 签名结构探测）
            + 损坏语料入库（tests/corpus/corrupt-archives/ + tools/CorruptProbe/）

Wave P2（UI/i18n）
├── 任务 3: 三语本地化 key 全量（P2+P3 共用，一次加齐；Blocked By: 1）
├── 任务 4: WholeArchiveErrorLocalizer + 整包错误调用点接线（TDD；Blocked By: 3）
└── 任务 5: ProgressViewModel 失败原因分布行（TDD；Blocked By: 3）

Wave P3（Core 契约扩展）
├── 任务 6: ArchiveProgress.EntryErrorDetail 契约 + 三引擎 catch 填充（TDD；Blocked By: 2）
└── 任务 7: VM detail 存储 + SetErrorSummary 增参 + 摘要/条目行 tooltip（TDD；Blocked By: 5/6）
    └── 任务 8: IoErrorHintResolver HResult 细分提示（TDD；Blocked By: 7）

Wave FINAL
├── 任务 9: 全量验证 + GUI 人工验收（Blocked By: 1–8）
└── 任务 10: 进度文档同步与提交（仅用户要求时；Blocked By: 9）
```

---

### 任务 1：App.axaml.cs 硬编码中文 → 本地化 key（规则 13 修复）

**Files**:
- 修改: `src/MantisZip.UI.Avalonia/App.axaml.cs`（:1218-1219、:2352-2353 两处）
- 修改: `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`（新 key ×3，与任务 3 合并去重）

**What to do**:
- [ ] 两处 `"{N} 个文件未能写入到 {dir}（可能权限不足）"` 改为 `LocalizationManager.T("Progress_WriteFailed_Fmt", n, dir)`（key 名可按既有 Progress_ 前缀惯例调整）
- [ ] 删除"可能权限不足"猜测措辞——失败原因由下方逐条目摘要行的真实分类承担，整包头行只陈述"未能写入到 {dir}"这一事实
- [ ] 三语 JSON 添加 key：zh-CN `"有 {0} 个文件未能写入到 {1}"`；en `"{0} file(s) failed to write to {1}"`；zh-TW 同 zh-CN 结构
- [ ] 两处调用点若有 `SetErrorSummary(detail: ex.Message)` 可行改动空间则交任务 4 统一处理，本任务**只改硬编码文案**

**Must NOT do**:
- 不改这两处的控制流/异常捕获逻辑
- 不在此任务顺带做整包错误本地化（任务 4）

**Parallelization**: 与任务 2 无文件交集，可并行（Wave P1）。

**验收**: `git grep -n "未能写入到" -- 'src/*.cs'` 无命中；`git grep -n "Progress_WriteFailed_Fmt" -- 'src/*.cs'` 命中 2 处调用 + 三语 JSON 各 1 处。

---

### 任务 2：TarGz/7z Failed 事件补 FileTotalBytes + Zip 注释修正

**Files**:
- 修改: `src/MantisZip.Core/Engines/TarGzEngine.cs`（:160/:169/:241/:249/:919/:928/:1014/:1022 共 8 处）
- 修改: `src/MantisZip.Core/Engines/SevenZipEngine.cs`（:599/:622/:1134/:1157 共 4 处）
- 修改: `src/MantisZip.Core/Engines/ZipEngine.cs`（:682 注释）

**What to do**:
- [ ] 逐处检查 catch 作用域内是否有条目大小可用（TarGz: entry 长度/流剩余；7z: `ArchiveFileData.Size`）；能取到则在 `new ArchiveProgress{ EntryStatus = Failed, ... }` 初始化器中补 `FileTotalBytes = <size>`
- [ ] 确实取不到大小的极少数分支（如 entry 解析失败前）填 `FileTotalBytes = 0` 或保持缺省，但**每处需注释说明原因**（`// 大小不可知：entry 尚未解析`）
- [ ] `ZipEngine.cs:682` 注释"异常对象不在作用域"改为"条目丢失（entry == null），无异常对象"——顺手修正误导性注释，不改代码逻辑

**Must NOT do**:
- 不改 catch 内的上报状态（Failed/Skipped 不变）
- 不引入新的异常捕获或重抛
- 不在本任务填充 EntryErrorDetail（任务 6）

**TDD（既有测试回归）**: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` 全绿，重点跑 `TarGzEngineTests`/`SevenZipEngineTests`。

**Parallelization**: 与任务 1 无文件交集，可并行（Wave P1）。

**验收**: `rg -n -A6 "ArchiveEntryStatus.Failed" src\MantisZip.Core\Engines\TarGzEngine.cs src\MantisZip.Core\Engines\SevenZipEngine.cs` 每个 `ArchiveProgress` 初始化器均出现 `FileTotalBytes` 或紧邻注释。

---

### 任务 3：三语本地化 key 全量（P2+P3 共用，一次加齐）

**Files**:
- 修改: `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`

**What to do**:
- [ ] 一次性添加本计划全部新 key（与任务 1 的 key 合并，避免重复添加），插入文件头 `{` 之后（key 不排序），维持 UTF-8 无 BOM + CRLF + 2 空格缩进
- [ ] 建议 key 清单（执行时可微调命名，但必须三语同步）：

| Key | zh-CN 示例 | 用途 |
|-----|-----------|------|
| `Progress_WriteFailed_Fmt` | 有 {0} 个文件未能写入到 {1} | 任务 1 硬编码替换 |
| `Progress_FailedBreakdown_Fmt` | 共 {0} 个失败：{1} | 任务 5 分布行（{1} 为 "权限不足 2 · 数据损坏 1" 拼接串） |
| `Progress_SkippedBreakdown_Fmt` | 共 {0} 个跳过：{1} | 任务 5 黄段分布行 |
| `Error_Whole_FileNotFound` | 找不到文件：{0} | 任务 4 整包错误 |
| `Error_Whole_DirectoryNotFound` | 找不到目录：{0} | 任务 4 |
| `Error_Whole_UnauthorizedAccess` | 拒绝访问：{0}（请检查文件夹权限） | 任务 4 |
| `Error_Whole_FallbackFmt` | 操作失败：{0} | 任务 4 兜底（含 ex.Message） |
| `Error_Hint_DiskFull` | 磁盘空间不足，请清理后重试 | 任务 8 |
| `Error_Hint_FileInUse` | 文件正被其他程序占用，请关闭后重试 | 任务 8 |
| `Error_Hint_PathTooLong` | 路径过长，请将目标移动到更短的目录 | 任务 8 |

- [ ] XAML 绑定用到的 key（tooltip 除外——tooltip 走 C# `ToolTip.Tip` 赋值，无需登记）若走 `LocalizedStrings[Key]` 需登记 `MainWindowViewModel.UpdateLocalizedStrings()`；**本计划新 key 全部为 C# `T()` 调用，无需登记该数组**
- [ ] `dotnet build` 后跑 `AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 确认三语 key 集一致

**Must NOT do**:
- 不改动任何既有 key 的值
- 不删除任何既有 key

**验收**: 三语各文件新增 key 数量一致；key 集测试通过。

---

### 任务 4：WholeArchiveErrorLocalizer + 整包错误调用点接线（TDD）

**Files**:
- 新建: `src/MantisZip.UI.Avalonia/Services/WholeArchiveErrorLocalizer.cs`
- 新建: `tests/MantisZip.UI.Avalonia.Tests/WholeArchiveErrorLocalizerTests.cs`
- 修改: `src/MantisZip.UI.Avalonia/App.axaml.cs`（:1282/:1458/:1660/:2394）、`Views/MainWindow.axaml.cs`（:365）、`Services/ExtractFlow.cs`（:138）

**What to do**（TDD——先写失败测试）:
- [ ] RED: `WholeArchiveErrorLocalizerTests` 至少覆盖：
  - `FileNotFoundException` → `Error_Whole_FileNotFound`（含 FileName）
  - `DirectoryNotFoundException` → `Error_Whole_DirectoryNotFound`
  - `UnauthorizedAccessException` → `Error_Whole_UnauthorizedAccess`
  - `InvalidDataException("...")` → **透传原文**（thrower 已本地化 Status_ArchiveCorrupted，避免双重本地化）
  - 未知异常 `InvalidOperationException("boom")` → `Error_Whole_FallbackFmt` 含 "boom"
  - `FileNotFoundException` 但 `FileName == null` → 回退 `Error_Whole_FallbackFmt`
- [ ] GREEN: 实现 `static class WholeArchiveErrorLocalizer { public static (string Message, string? Detail) ToUserText(Exception ex) }`——返回本地化文案 + detail（ex.Message 原文，供任务 7 tooltip）
- [ ] 将 6 个 `SetErrorSummary(ex.Message)` 调用点改为：
  ```csharp
  var (msg, detail) = WholeArchiveErrorLocalizer.ToUserText(ex);
  progressVm.SetErrorSummary(msg, detail); // detail 参在任务 7 落地前先忽略/置 null，本任务可先只改 msg
  ```
  **过渡说明**：任务 7 才给 `SetErrorSummary` 加 detail 参；本任务先只把 message 换成本地化文案，detail 暂不传（避免阻塞），任务 7 统一补上。

**Must NOT do**:
- 不改 `InvalidDataException` 的 thrower 侧（Status_ArchiveCorrupted 已本地化，透传即可）
- 不吞异常——localizer 只做映射，catch 边界不变
- Core 层不引用 `LocalizationManager`（localizer 在 UI 层）

**Parallelization**: 与任务 5 可并行（不同文件；任务 5 只动 VM/XAML）。

**验收**: 新测试全绿；6 个调用点不再直接传 `ex.Message` 作为 summary 正文（`rg -n "SetErrorSummary\(ex" src\MantisZip.UI.Avalonia` 无命中）。

---

### 任务 5：ProgressViewModel 失败原因分布行（TDD）

**Files**:
- 修改: `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs`（`RebuildSummaryTexts`/`BuildSummaryText`/新增 `BuildBreakdownLine`）
- 修改: `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs`

**What to do**（TDD）:
- [ ] RED: 新测试至少覆盖：
  - 3 Failed（2×PermissionDenied + 1×CrcMismatch）→ 红段含分布行 `权限不足 2 · 数据损坏 1`（经三语 key 拼接；测试用 zh-CN 或注入 fake L）
  - 纯整包错误（无 entry）→ **不显示**分布行（0 个失败不值得一行）
  - Skipped 带原因 → 黄段分布行同构
  - 解压冲突 Skipped（无原因，D5）→ 不进黄段、不进分布行
- [ ] GREEN: `BuildBreakdownLine(IReadOnlyDictionary<...>)` 从既有 `_failedEntries`/`_skippedEntries` 的 reason 计数生成 `Error_Reason_*` 本地化短名 + 数量，格式 `A 2 · B 1`（按数量降序，同数量按枚举序）；红/黄段构建时在整包头（可无）之后、entry 行之前插入该行
- [ ] 分布行计入 `SummaryMaxLines = 20` 上限（即分布行占 1 行配额）

**Must NOT do**:
- 不新增持久化数据结构（分布是即时计算）
- 不改红/黄段触发条件（HasErrors 语义、D4/D5 不动）
- 不改 entry 行格式

**Must do**: 分布行本身也要进复制内容（复制=红+黄全量文本，含分布行）。

**验收**: 新测试全绿；既有红黄段测试不回归。

---

### 任务 6：ArchiveProgress.EntryErrorDetail 契约 + 三引擎 catch 填充（TDD）

**Files**:
- 修改: `src/MantisZip.Core/Abstractions/ArchiveEngine.cs`（`ArchiveProgress` 加字段）
- 修改: `src/MantisZip.Core/Engines/{ZipEngine,TarGzEngine,SevenZipEngine}.cs`（catch 块填充）
- 新建/修改: `tests/MantisZip.Tests/Engines/*` 相关测试

**What to do**（TDD）:
- [ ] `ArchiveProgress` 新增：
  ```csharp
  /// <summary>失败/跳过原因的原始异常消息（仅供 UI tooltip；勿展示于摘要正文）</summary>
  public string? EntryErrorDetail { get; init; }
  ```
- [ ] RED: 引擎级测试——构造可复现失败（如 CRC 损坏 ZIP、目标只读目录），断言 progress 回调中 `EntryFailureReason` 匹配且 `EntryErrorDetail` 非空且含可辨识子串（如 "CRC" 或 IO 消息片段）
- [ ] GREEN: 三引擎所有 `ArchiveEntryStatus.Failed` / 带原因 Skipped 上报处，在异常作用域内补 `EntryErrorDetail = ex.Message`；无异常的分支（条目丢失）不填（保持 null）
- [ ] 验证不破坏并行解压（ZipEngine 并行路径的 progress 线程安全——detail 是 string 不可变，无共享写）

**Must NOT do**:
- 不改 `IArchiveEngine` 方法签名
- 不在 Core 写中文（detail 是库/系统英文原文）
- 取消路径（OCE）不上报 Failed detail

**验收**: 新引擎测试全绿；`rg -n "EntryErrorDetail" src\MantisZip.Core` 命中契约定义 + ≥10 处填充点。

---

### 任务 7：VM detail 存储 + SetErrorSummary 增参 + 摘要/条目行 tooltip（TDD）

**Files**:
- 修改: `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs`
- 修改: `src/MantisZip.UI.Avalonia/Models/EntryProgressItem.cs`（若列表项模型在此；否则对应文件）
- 修改: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`（红/黄摘要 TextBox tooltip + 条目列表行 tooltip）
- 修改: `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs`
- 补任务 4 遗留: `App.axaml.cs` 等 6 个调用点传 `detail: ex.Message`

**What to do**（TDD）:
- [ ] `SetErrorSummary(string message, string? detail = null)`——既有调用零改动（D7）
- [ ] RED: 测试 `CollectEntryOutcome` 收到 `EntryErrorDetail` 后 `EntryProgressItem.FailureDetail` 非空；整包 error detail 存入 VM 私有字段并在摘要 tooltip 组装时使用
- [ ] GREEN:
  - 摘要红/黄 TextBox `ToolTip.Tip` = reason 本地化 + detail（多行：正文 + "—— " + detail）；detail 为空则只显示正文
  - `EntryProgressItem` 增 `FailureReasonText`/`FailureDetail`；`CollectEntryOutcome` 同步写入
  - 条目列表 Failed/Skipped 行 tooltip 绑定上述两字段
  - 任务 4 遗留的 6 个调用点补传 detail
- [ ] tooltip 内容本地化走 `LocalizationManager.T`（模板/转换器或 VM 侧组装字符串，避免 XAML 硬编码）

**Must NOT do**:
- 摘要**正文**行格式不变（key + 本地化 4 类原因），detail 只进 tooltip（D2）
- 不做可点击展开/重试（P4）

**验收**: 新测试全绿；`ProgressWindow.axaml` 红/黄摘要区与条目列表行均有 tooltip 绑定。

---

### 任务 8：IoErrorHintResolver HResult 细分提示（TDD）

**Files**:
- 新建: `src/MantisZip.UI.Avalonia/Services/IoErrorHintResolver.cs`
- 新建: `tests/MantisZip.UI.Avalonia.Tests/IoErrorHintResolverTests.cs`
- 修改: tooltip 组装处（任务 7 的 VM 或 converter，接线 hint）

**What to do**（TDD）:
- [ ] RED: 测试 `TryGetHint(Exception)`：
  - HResult 低 16 位 = 112（ERROR_DISK_FULL）或 39（ERROR_HANDLE_DISK_FULL）→ `Error_Hint_DiskFull`
  - 32（ERROR_SHARING_VIOLATION）或 33（ERROR_LOCK_VIOLATION）→ `Error_Hint_FileInUse`
  - 206（ERROR_FILENAME_EXCED_RANGE）→ `Error_Hint_PathTooLong`
  - 其他 IOException / 非 IOException → null
  - 构造 HResult：`Marshal.GetHRForWin32Error(112)` 或手动 `(0x80070000 | 112)`
- [ ] GREEN: 实现 `static class IoErrorHintResolver { public static string? TryGetHint(Exception ex) }`——仅当 `ex is IOException` 时按 `ex.HResult & 0xFFFF` 匹配，命中返回本地化 key 对应文案，未命中 null
- [ ] 接线：tooltip 组装时若 `FailureReason == IoFailed` 则尝试 `TryGetHint`，命中则在 detail 后追加 hint 行（如 `I/O 错误\nThe disk is full.\n磁盘空间不足，请清理后重试`）

**Must NOT do**:
- 不扩 `EntryFailureReason` 枚举（D5）
- Core 不感知 Win32 码（hint 全在 UI 层）

**验收**: 新测试全绿；非 Windows/未知码安全返回 null 不抛。

---

### 任务 9：全量验证 + GUI 人工验收

**Files**:
- 无代码改动（验证为主）

**What to do**:
- [ ] 全量构建 + 测试（见 Verification Strategy 全部命令）
- [ ] 硬编码扫描：`git grep -n "未能写入到" -- 'src/*.cs'` 无命中
- [ ] 引擎扫描：`rg -n "ArchiveEntryStatus.Failed" src\MantisZip.Core\Engines` 逐处核对 FileTotalBytes/EntryErrorDetail
- [ ] GUI 人工验收清单（Windows，需准备测试素材）：
  1. **CRC 损坏 ZIP**：解压 → 红段摘要行显示"数据损坏"，tooltip 显示 SharpCompress 原文（含 CRC 字样），条目行 tooltip 同
  2. **只读目标目录**：解压 → "权限不足" + UnauthorizedAccess 本地化整包头（若整包级）+ tooltip 原文
  3. **磁盘满**（可用 subst 小容量 VHD 模拟）：解压 → IoFailed 行 tooltip 追加"磁盘空间不足"hint
  4. **被占用文件**（Word 打开目标 txt）：解压 → tooltip 追加"文件正被占用"hint
  5. **TAR/GZ 失败**：统计卡"失败大小"不再缺失（对照修复前）
  6. **7z 失败**：同上
  7. **分布行**：≥2 种失败原因混合场景，红段头部出现 `共 N 个失败：A x · B y`
  8. **复制按钮**：复制内容含分布行 + 全部 entry 行
  9. **黄段**：压缩只读文件 → 跳过原因分布行出现；解压冲突跳过 → 黄段不出现（D5 回归）
  10. **纯整包错误**（打开损坏包）：整包头为本地化文案（zh 环境无英文裸露），tooltip 有原文

**Must NOT do**:
- 验收发现问题回到对应任务修复，不在本任务直接改代码（除非是一行级 typo）

**验收**: Definition of Done 全部勾选。

---

### 任务 10：进度文档同步与提交（仅用户要求时执行）

**Files**:
- 修改: `docs/PROGRESS.md`（Avalonia 里程碑 + 共享层，按规则 3 格式）
- 修改: `docs/progress-avalonia-detail.md`（细节条目）
- 修改: `docs/PLAN.md`（任务行状态/移至 PROGRESS 历史索引，规则 1）

**What to do**:
- [x] **先**更新双轨进度文档（规则 3）：PROGRESS 里程碑 + detail 条目已同步（commit `a257947f` / `0accf8c4`）
- [x] `docs/PLAN.md` 状态已更新；**计划文件暂留「进行中」**——任务 9 GUI 人工验收未完成（规则 1 完成后再移入 `.omo/plans/已完成/`）
- [x] conventional commit 已执行：`feat(core,avalonia): 压缩/解压出错信息增强 Task 1-8（整包错误本地化+条目详情透传+失败原因分布）`（commit `7799e56a`）
- [x] 未推送

**Must NOT do**:
- 未经用户明确要求不 commit / 不 push / 不改版本号（规则 2）

---

### 任务 11：打开阶段损坏/加密分类（方案 B）· ✅ 已实施（2026-10-15）

**背景**：`ArchiveService.IsPasswordRelatedError` 纯关键词启发式会把**损坏的 7z/RAR** 误判为「需要密码」而弹密码框。经损坏语料实测（`tests/corpus/corrupt-archives/`）：SharpSevenZip 对「7z/RAR 加密头无密码」与「7z/RAR 头部损坏」抛出**完全相同**的异常——同类型 `SharpSevenZipArchiveException`、同 HRESULT `0x80131500`、同消息 `Invalid archive: open/read error! Is it encrypted and a wrong password was provided?...`——**异常层无法区分**。故改用**异常之外的字节级结构探测**。

**Files**:
- 新建: `src/MantisZip.Core/Utils/ArchiveHeaderProbe.cs`（`HeaderStructure{Unknown,Intact,Broken}`）
- 修改: `src/MantisZip.UI.Avalonia/Services/ArchiveService.cs`（`IsPasswordRelatedError(ex, archivePath=null)` + 结构探测兜底）
- 修改: `ViewModels/ExtractSettingsViewModel.cs`、`Services/PasswordRetryLoop.cs`（传 path）
- 新建: `tests/MantisZip.Tests/Utils/ArchiveHeaderProbeTests.cs`、`tests/MantisZip.UI.Avalonia.Tests/SourceArchiveValidationTests.cs`（path-aware 用例）
- 新增: 语料 `tests/corpus/corrupt-archives/`、harness `tools/CorruptProbe/`

**What to do（已实施）**:
- [x] `ArchiveHeaderProbe.Probe(path)`：魔数分派（不依赖扩展名）+ 7z StartHeader 校验（签名 / 版本主号 / StartHeaderCRC / next-header 越界，分卷按兄弟卷求和）+ RAR 签名（RAR4/RAR5）；魔数不符但扩展名声称 7z/RAR → Broken
- [x] `IsPasswordRelatedError` 三段式：损坏关键词 → false；无密码关键词 → false；否则歧义 → 结构探测 Broken → false，否则 true
- [x] 三处调用方传 path（浏览加载 / 解压设置逐包校验 / 密码重试循环）
- [x] 语料入库 + harness 工具化（相对 ProjectReference）

**实测结论**（`tools/CorruptProbe` 108 行矩阵 + HRESULT 探针）：cross-check 通过——corrupt-08/14（7z 损坏）、corrupt-24（RAR 签名损坏）、vol-02（分卷缺卷）→ `Broken`（不弹密码框）；enc-04/06（加密头）、valid.7z/rar、vol-01（完整分卷）→ `Intact`（走密码流程）。

**Must NOT do**:
- 不在 Core 引入 7z.dll 依赖（纯字节解析，可无引擎单测）
- 不漏判 ZIP/TAR（其消息清晰，返回 `Unknown` 交现有启发式）
- 不因结构探测把**真正的加密头**判成损坏（enc-04/06 必须 `Intact`）

**验证**：`ArchiveHeaderProbeTests` 24/24；`SourceArchiveValidationTests` 全绿；UI 263✓ / Core 708✓（`-p:SkipShellExtCopy=true`）。

**已知边界**：RAR **截断**（签名完好、corrupt-25）结构探测判 `Intact`——但其 ListEntries 成功、不抛歧义消息，不影响本修复；尾部损坏的 RAR 仍由「数据错误」异常路径处理。

---

## 验收标准（汇总）

| # | 标准 | 验证方式 |
|---|------|---------|
| 1 | 双项目构建 0 错误 | dotnet build ×2 |
| 2 | 两测试项目全绿 | dotnet test ×2 |
| 3 | 硬编码中文清除 | git grep 无命中 |
| 4 | TarGz/7z Failed 带字节 | rg 逐处核对 |
| 5 | EntryErrorDetail 三引擎填充 | rg + 引擎测试 |
| 6 | 整包错误本地化 | localizer 测试 + GUI #10 |
| 7 | 分布行正确 | VM 测试 + GUI #7 |
| 8 | tooltip 含原文/hint | GUI #1/#3/#4 |
| 9 | 既有行为不回退 | 既有测试 + GUI #9 |
| 10 | 三语 key 一致 | AllThreeLanguages_HaveSameKeySet |

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| HResult 在非 Windows/.NET Core 上语义差异 | `TryGetHint` 只做位掩码匹配，未命中返回 null；UI 目前仅 Windows |
| SharpCompress 异常消息格式变化导致测试脆弱 | 引擎测试断言用 `Contains("CRC")` 等宽匹配，不锚定完整句子 |
| `SetErrorSummary` 既有调用点遗漏 detail | 任务 7 用 `rg -n "SetErrorSummary" src` 全量核对 |
| 并行解压 progress 线程安全 | detail 为不可变 string，init-only 随 ArchiveProgress 快照发布，无共享写 |

## 提交策略

默认**不 commit**。任务 1–9 完成且验收通过后向用户汇报；仅当用户明确要求提交时执行任务 10（规则 3 前置 + 规则 10 消息格式 + 不 push）。
