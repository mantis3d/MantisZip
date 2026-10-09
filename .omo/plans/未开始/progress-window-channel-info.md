# 进度窗口详细模式通道信息增强（信息列 + 常显 + 两列路径 + 底纹修复）

> **状态**: 📋 设计已确认 + 任务分解已生成（2026-10-08），待实施
>
> **For agentic workers:** 本文档是设计规格 + 完整任务分解（澄清 5 问 + Rule 0 补问已定稿，见「已确认设计决策」；文末「任务分解」含 8 任务 checkbox 步骤）。执行时使用 superpowers:subagent-driven-development 或 superpowers:executing-plans。

## TL;DR

> **Quick Summary**: 进度窗「详细」模式做 4 点增强：① 通道/批次行新增信息列（文件大小 · 已处理/总量 · 实时压缩率）；② 详细模式始终可用（推翻 D6 门禁），非并行操作显示单条通道行复用批次行样式但隐藏批次序号/批级进度条/百分比/批明细/压缩通道说明；③ 当前路径拆分为目录（中间省略）+ 文件名（恒完整）两列；④ 修复底纹双缺陷（几何宽度基准错误 + N 组适配层漏拷 `FilePercentComplete`）。同时批级进度条缩窄。
>
> **Deliverables**:
> - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` —— `ArchiveProgress` 新增 `FileTotalBytes`/`BatchProcessedBytes`/`BatchTotalBytes`/`CompressionRatio`
> - `src/MantisZip.Core/Engines/ZipEngine.cs` —— 全部报告站点填充新字段；N 组 adapter 修复漏拷 `FilePercentComplete`
> - `src/MantisZip.UI.Avalonia/ProgressViewModel.cs` —— 移除 D6（`IsDetailedAvailable`）；非并行单行合成
> - `src/MantisZip.UI.Avalonia/Models/ParallelBatchProgressItem.cs` —— 行模型扩展（目录/文件名/信息列字段 + 单行可见性）
> - `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` —— 底纹几何修复、批次行模板三列→四列+信息列、批进度条缩窄、单行布局
> - `Core/Utils/ProgressDisplayCalculator.cs` —— 新增中间省略工具 + 路径两列拆分
> - `ProgressViewModelTests.cs` / `ProgressWindowXamlTests.cs` / `ProgressWindowBatchLogicTests.cs` —— D6 重写 + 新测试
> - `docs/PLAN.md` 登记行（规则 1）
>
> **Estimated Effort**: Medium（1.5–2.5 天）
> **Parallel Execution**: YES — Wave 1 任务 1/2 并列（不同文件 XAML）；任务 3 依赖 archive 进度契约
> **Critical Path**: 用户审阅本设计 → 任务 1/2（底纹双修）→ 任务 3（ArchiveProgress 契约+引擎填充）→ 任务 4（D6 移除+单行）→ 任务 5/6（UI 布局）→ GUI 验收

---

## Context

### Original Request

用户原话（2026-10-08，5 问澄清后定稿）：

1. 「详细」模式下每个通道/批次行应该有信息列：文件大小、已处理/总量、实时压缩率。7z 压缩不显示压缩率；解压不显示压缩率；非并行（单行）时也要显示信息列。`FormatUtil.FormatSize` 格式化，缺子字段按 Rule 6 隐藏。
2. 「详细」模式应该始终显示（推翻 D6）：非并行时显示单条通道行复用批次行样式，但隐藏：批次序号、批级进度条、百分比、批明细、压缩通道说明行——进度表达交给底纹+信息列数字。
3. 长路径显示两列：目录（中间省略）+ 文件名（恒完整）；根级文件目录列隐藏。
4. 底纹有 bug：① 几何错误（宽度基准取自 `ContentPresenter.Bounds.Width`=整行宽度，应为最近 Grid）② N 组适配层（adapter）漏拷 `FilePercentComplete`。

### Research Findings

- **`IsDetailedAvailable`（D6 门禁）分布**：XAML `ProgressWindow.axaml:270`(注释)/`:288`(`IsVisible="{Binding IsDetailedAvailable}"`)；VM `ProgressViewModel.cs:231`（`_parallelBatchItems.Count > 0`）、通知 `:143`/`:316`（`NotifyDisplayProperties:311-322` 经 ContentMode/InfoDensity setter `:199-210` 调用）；D6 回落约 `SetCurrentBatchItem:934-936`（详细模式且集合空 → 回退 Simple）；测试 `ProgressViewModelTests.cs:160-166` 断言该回落（D6 移除后必改）。grep 确认测试中无其他 `IsDetailedAvailable` 引用。
- **`UpsertParallelBatch`**（`ProgressViewModel.cs:690-714`）：Percent 钳制写入、`FileRatio = FilePercentComplete/100`、CurrentFile 保持、StatusBrushName、DetailText（`T("Progress_Batch_FilesProgress")`）、自动切详细守卫 `:712`（保留）。
- **`SetProgress:516`**：`EndPreparing` → `:524` BatchIndex upsert → `:529` EntryStatus 早返回 → `:541-550` `SplitFilePath` → `:553` 批次总进度。
- **批次行 XAML**（`ProgressWindow.axaml`）：列 `Auto,*,*,Auto,Auto`（批次序号 | 当前文件 | 批级进度条 | 百分比 | 明细）；内层 Grid `:383`；底纹 Rectangle `:386-396` 绑 `Bounds.Width` + `RelativeSource AncestorType=ContentPresenter`（bug：ContentPresenter 是整行容器，应取内层 Grid）；批进度条 `:407`；窗口 `Width=560`（`:7`）。
- **`ParallelBatchProgressItem.cs`**（30 行）：Index(init)/Percent/StatusBrushName/DetailText/CurrentFile/FileRatio。
- **`ArchiveProgress`**（`ArchiveEngine.cs:318-354`）：CurrentFile/TotalBytes/ProcessedBytes/TotalFiles/ProcessedFiles/PercentComplete/FilePercentComplete/BatchIndex/BatchCount/SkippedFiles/FailedFiles/OverwrittenFiles/BatchPercentComplete/BatchProcessedFiles/BatchTotalFiles/EntryKey/EntryStatus —— 待加 4 字段。
- **ZipEngine 填充点**：批次字节变量 `batchTotalBytes`（`:541` sync / `:1058` async）、`batchProcessedBytes`（`:543` / `:1060`）；并行报告 `:640-654`/`:1156-1170`、批次完成 `:719-731`/`:1236-1249`；串行提取 `:376-385`；异步提取 `:1143-1162`；串行 ZipWriter 节流报告 `:1748-1761` + `ReadFileWithRetry` 内 `:3273-3280`（均缺 TotalBytes/ProcessedBytes，唯一调用点 `:1735`）；`CompressGroupWithSevenZip:3481`（mt 设置 `:3507`、站点 `:3553`/`:3603`/`:3662`、mirror copy fileLen `:3651-3654`、7z 事件 FilePercentComplete=null、`pendingEntryKeys` FIFO `:3516`）；N 组 adapter `:1516-1541` 与 `:2459-2483`（已核实两处均漏拷 `FilePercentComplete`）。
- **`SplitOutputStream.Length`** 抛 `NotSupportedException`（`SplitOutputStream.cs:91`）→ 分卷压缩率置 null 隐藏。
- **`CompressionRatio` 数据源**：串行 ZIP = `fsOut`（声明点 `:1291`/`:2286`/`:3054`/`:3111`）FileStream.Length/processedBytes；N 组 = `tempZips[i].Length`/localDone；7z 格式与解压 = null。
- **`ConflictStats.ApplyTo`**（`ConflictStatsCounter.cs:24-30`）原地改字段并返回同一对象 → 新增字段不会被丢弃。
- **`RatioToWidthConverter`**（17 行）：两输入双精度→`ratio*availableWidth`，否则 0.0 —— 修复在 XAML 绑定侧 + ClipToBounds。
- **规则 6 隐藏模式**：既有用 `StringNotEmpty` 转换器 + `IsVisible`（参考 `FileFilterEditor.axaml`/压缩对话框密码区）。
- **无中间省略现成工具**：grep `Ellipsis|省略` 无匹配；`ProgressDisplayCalculator.SplitFilePath:22` 与 `StripEnginePrefix:12` 可直接复用做两列拆分；新工具放同类（Core/Utils，可单测）。
- **stats-cards 耦合**：`progress-stats-cards-three-row.md` 为先行计划，两计划不同时动同一控件区但都改 `ProgressViewModel.cs` → **stats-cards 先实施，本计划行号届时校准**。

---

## 已确认设计决策（2026-10-08 用户逐段确认）

### D1 信息列口径（方案 A 批次级）

- 每行信息列三段：`文件大小 · 已处理/总量 · 实时压缩率`。
- 「已处理/总量」= **批次级**：`ArchiveProgress` 新增 `BatchProcessedBytes`/`BatchTotalBytes`（非并行单行时批次=整体，填同一值）。
- 「文件大小」= `ArchiveProgress.FileTotalBytes`（当前条目/文件总大小）。
- 「实时压缩率」= 批次累计输出字节/输入字节×100（0–100），`CompressionRatio` 为 `double?`；7z 格式压缩与所有解压路径置 null → 该字段按 Rule 6 隐藏。
- 所有显示走 `FormatUtil.FormatSize(long)`（`FormatUtil.cs:13`）；`FileProcessedBytes` 已废弃（不采用）。
- 缺子字段按 Rule 6 隐藏：整段（压缩率/总量段）为空则该段连同分隔符隐藏。
- **无新增 i18n key**：信息列纯数值+分隔符（`·`/`/`）。

### D2 详细模式常显（推翻 D6）

- 移除 `IsDetailedAvailable`（属性 `:231`、两处通知 `:143`/`:316`、XAML `:288` + 注释 `:270/:285`、`SetCurrentBatchItem:934-936` 回落、测试 `:160-166` 重写）；「详细」单选按钮不再受可用性门禁。
- 非并行（`BatchIndex` 为 null）时，VM 在 `SetProgress`（`:529` EntryStatus 早返回之后、`:541` 路径拆分之前）合成单条 `ParallelBatchProgressItem` 并 upsert 进集合，复用批次行样式：
  - 隐藏：批次序号、批级进度条、百分比、批明细、压缩通道说明行（`IsCompressFlow` 说明行绑定增加「非并行时隐藏」条件——单行即无并行通道，说明行无意义）。
  - 进度表达：底纹（`FileRatio`）+ 信息列数字。
  - 注意：`ProgressWindowXamlTests:110-149` 断言非并行时 `ParallelBatchItems` 空集合——该等价断言改测「无批次数据时区分单行与批次行」：新增字段 `IsParallel`（批次行 true / 单行 false），测试改为断言 `ParallelBatchItems` 中所有项 `IsParallel == false` 或数量≤1（语义保留「不伪装并行批次」）。
- 保留 Task 6 并行自动切详细（`UpsertParallelBatch:712`）。
- `HasParallelBatches:244`（`_parallelBatchItems.Count > 0`）语义不变；单行也会进入该集合，但可用 `IsParallel` 区分。若 UI 需要「有并行」才显示批次条，应新增派生属性 `HasParallelChannel`（Count(项.IsParallel) > 0），XAML 绑定同步改。

### D3 路径两列（目录中间省略 + 文件名恒完整）

- 复用 `ProgressDisplayCalculator.SplitFilePath:22`（剥离引擎前缀后按最后一个 `/` 拆目录/文件名）。
- 新增 `ProgressDisplayCalculator.MiddleEllipsis(string path, int maxLength)`：超长目录保留首段+末段，中间 `…`；根级文件目录为空串 → 目录 TextBlock `IsVisible=false`（Rule 6）。
- 文件名恒完整显示（不裁剪）；目录列与文件名列中间给固定间距。
- `ParallelBatchProgressItem` 新增 `DirectoryText`/`FileNameText` 字段，VM 在 `SetProgress`/`UpsertParallelBatch` 填充。

### D4 底纹双缺陷修复

- **几何**：`ProgressWindow.axaml` 底纹 Rectangle 的 `MultiBinding` 第二输入由 `RelativeSource AncestorType=ContentPresenter`（整行宽度）改为最近 `Grid`（文件名格内层 Grid `:383`），内层 Grid 加 `ClipToBounds="True"`。
- **漏拷**：`ZipEngine.cs` N 组 adapter `:1516-1541` 与 `:2459-2483` 两处 `new ArchiveProgress` 补上 `FilePercentComplete = local.FilePercentComplete`（及新增的 4 字段，见任务 3）。
- 解压实测已完成：不再单独做「复现→排查」，几何修复后直接进入四场景 GUI 验收（变更清单 ②）。

### D5 批级进度条缩窄

- 批次行 `Grid.ColumnDefinitions="Auto,*,*,Auto,Auto"` 中第 3 列（批级进度条）`*` 改固定/较小宽度（如 `100`，窗口 `Width=560` 内为信息列腾出空间）；具体宽度在任务 6 落地时按视觉微调。

### D6 变更清单（已批准 5 条）

1. 底纹修复拆两任务（几何 + 漏拷）分别独立 checkbox；
2. ~~解压实测复现再排查~~ 删除，几何修复后直接四场景 GUI 验收；
3. 信息列 `FormatUtil.FormatSize` + Rule 6 隐藏；
4. 与 stats-cards 计划耦合：stats-cards 先实施，本计划行号落地后校准；
5. 归新计划 `progress-window-channel-info.md`（本文件）。

---

## Work Objectives

### Core Objective

「详细」模式始终可用且信息完整：通道行展示压缩/解压的实时字节与压缩率，路径两列可读，非并行单行不伪装成并行批次，底纹（当前文件字节进度）准确反映真实文件粒度。

### Concrete Deliverables

- `ArchiveEngine.cs`：`ArchiveProgress` 新增 `FileTotalBytes` / `BatchProcessedBytes` / `BatchTotalBytes` / `CompressionRatio` 四字段。
- `ZipEngine.cs`：全部 `ArchiveProgress` 报告站点填充新字段；N 组 adapter 两处补拷 `FilePercentComplete` 与新字段；`CompressionRatio` 按 D1 口径填充/置 null。
- `ProgressViewModel.cs`：删除 D6（`IsDetailedAvailable` 及通知点、D6 回落）；非并行单行合成；`MiddleEllipsis` 接线；`UpsertParallelBatch` 新字段填充。
- `ParallelBatchProgressItem.cs`：新增 `IsParallel`、`DirectoryText`、`FileNameText`、`FileSizeText`、`ProcessedTotalText`、`RatioText`。
- `ProgressWindow.axaml`：底纹几何修复；模式单选区 D6 绑定移除；批次行模板（目录/文件名两列 + 信息列 + 进度条缩窄 + 单行隐藏规则）；中文注释（规则 14）。
- `ProgressDisplayCalculator.cs`：新增 `MiddleEllipsis`。
- 测试：`ProgressViewModelTests` D6 重写 + 单行合成断言；`ProgressWindowXamlTests` 非并行语义调整；`ProgressWindowBatchLogicTests` 新字段断言；`ProgressDisplayCalculatorTests` 中间省略用例。

### Definition of Done

- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误 0 警告
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿（既有预存失败除外）
- [ ] `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全绿
- [ ] GUI 手动验收四场景：并行 ZIP 解压 / 非并行 ZIP 压缩 / 7z 压缩（信息列压缩率隐藏）/ 单条解压（底纹正确）

### Must Have

- 详细模式不再有「无数据即消失」门禁；非并行显示单行且不出现批次序号/批进度条/百分比/批明细/压缩说明行。
- 信息列三段齐全，值来自 `ArchiveProgress` 新字段，`FormatSize` 格式化；7z 压缩与解压隐藏压缩率段；分卷 ZIP（SplitOutputStream）同样隐藏。
- 路径两列：目录中间省略、文件名完整；根文件名场景目录列隐藏。
- 底纹宽度 = 当前文件节字节比例 × 文件名格实际宽度（不再取整行宽）。
- 新增 axaml 控件中文注释（规则 14）；间距用 `{DynamicResource Spacing*}`（规则 5）。

### Must NOT Have (Guardrails)

- **不得**新增 i18n key（信息列纯符号/数值；沿用既有 `Progress_Batch_*`）。
- **不得**把非行并场景伪装成多批次（`IsParallel=false` 单行，数量恒 1）。
- **不得**改 `ArchiveProgress` 既有字段语义（只加字段）；`ConflictStats.ApplyTo` 不丢新字段（已核实原地改）。
- **不得**破坏并行自动切详细（`UpsertParallelBatch:712` 保留）。
- **不得**混入 stats-cards 改动（行号漂移来源，先后落地）。
- 版本号不变（规则 2）。

---

## Verification Strategy (MANDATORY)

### Test Decision

- **Infrastructure exists**: YES（xunit.v3 + Avalonia.Headless.XUnit，`ProgressViewModelTests`/`ProgressWindowXamlTests`/`ProgressWindowBatchLogicTests` 已有 SetProgress 系列用例）。
- **Automated tests**: Unit（VM 单行合成、`MiddleEllipsis`、压缩率 null 隐藏、D6 移除后断言、adapter 漏拷回归）。
- **Framework**: xunit.v3，沿用既有文件与 `[Fact]`/`[AvaloniaFact]` 模式。

### AI-Driven QA

- Avalonia 桌面 UI，视觉验收（底纹对齐、进度条宽度、单行隐藏规则）走**人工 GUI 验收**，不做浏览器 QA。

### 自动化验证命令

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```

---

## 依赖与后续

- **依赖**：`progress-stats-cards-three-row` 计划**先实施**（两者都改 `ProgressViewModel.cs`，避免行号冲突）；`progress-window-bytes-i18n` 为软依赖（7z/TAR 字节埋点补齐后，信息列「已处理/总量」在 7z/TAR 下由隐藏变真实，本设计零改动）。
- **后续受益**：信息列与统计卡同源 `ArchiveProgress`，字节埋点到位即自动生效。
- **文档同步**：本文件新增 → `docs/PLAN.md` P2 区登记（规则 1）。

---

## 任务分解

> **执行方式**: superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，checkbox 逐步勾选。
> **提交策略**: 默认**不 commit**（未经用户明确要求）；仅用户要求时执行末尾提交任务，并按规则 3 先更新进度文档。

### 执行波次

```
Wave 1（底纹双修，并列）
├── 任务 1: 底纹几何修复（XAML 绑定侧 + ClipToBounds）
└── 任务 2: N 组 adapter 漏拷 FilePercentComplete 修复

Wave 2（契约）
└── 任务 3: ArchiveProgress 新字段 + ZipEngine 全站点填充（Blocked By: 无，可与 Wave 1 并行，但字段拷贝依赖任务 2 的约定）

Wave 3（VM 语义）
└── 任务 4: D6 移除 + 非并行单行合成（Blocked By: 3）

Wave 4（UI 布局）
├── 任务 5: 目录/文件名两列 + 中间省略（Blocked By: 4）
└── 任务 6: 信息列 + 批进度条缩窄 + 单行隐藏规则（Blocked By: 4，与任务 5 同 XAML 文件需顺序）

Wave FINAL
├── 任务 7: 构建 + 双测试套 + GUI 四场景验收（Blocked By: 1–6）
└── 任务 8: 进度文档同步与提交（仅用户要求时；Blocked By: 7）
```

---

- [ ] 1. 底纹几何修复（`ProgressWindow.axaml`）

  **What to do**:

  (1a) `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` 底纹 Rectangle 所在内层 Grid（约 `:383`）上加 `ClipToBounds="True"`。

  (1b) 同区块 `MultiBinding`（约 `:392-393`）第二输入：

  ```xml
  <!-- 改前（错误：取整行宽度的 ContentPresenter） -->
  <Binding Path="Bounds.Width" RelativeSource="{RelativeSource AncestorType=ContentPresenter}" />
  <!-- 改后：取文件名格内层 Grid（最近父级） -->
  <Binding Path="Bounds.Width" RelativeSource="{RelativeSource AncestorType=Grid}" />
  ```

  内层 Grid 是 Rectangle 直接父容器（约 `:383`-`:404`），`AncestorType=Grid` 即该格自身；外层整行 Grid 不受影响。

  (1c) 在内层 Grid 起始标签补一行中文注释：

  ```xml
  <!-- 文件名格：底纹（当前文件字节进度）+ 文件名文本。底纹宽度基准取本 Grid 实际宽度；ClipToBounds 防止底纹溢出格外。 -->
  ```

  (1d) 验证：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误。GUI 验收并入任务 7（变更清单 ②：不单独实测复现）。

- [ ] 2. N 组 adapter 漏拷 `FilePercentComplete` 修复（`ZipEngine.cs`）

  **What to do**:

  (2a) 第一处 `:1516-1541` 的 `new ArchiveProgress` 中，在 `BatchProcessedFiles`/`BatchTotalFiles` 赋值区之后追加：

  ```csharp
  FilePercentComplete = local.FilePercentComplete,
  ```

  (2b) 第二处 `:2459-2483` 的 `new ArchiveProgress` 做同样追加。

  (2c) 回归断言（可选，若 `ParallelCompressTests` 已有 N 组路径用例）：该用例内捕获的进度序列中应至少出现一次非 null 的 `FilePercentComplete`；无现成用例则跳过，行为靠任务 7 的 GUI 验收确认。

  (2d) 验证：`dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全绿。

- [ ] 3. `ArchiveProgress` 新字段 + ZipEngine 全站点填充

  **What to do**:

  (3a) `src/MantisZip.Core/Abstractions/ArchiveEngine.cs:354`（`EntryStatus` 属性后）追加：

  ```csharp
      /// <summary>当前条目/文件总字节（信息列「文件大小」）。未上报为 0。</summary>
      public long FileTotalBytes { get; set; }

      /// <summary>批次已处理字节数（信息列「已处理/总量」分子；非并行时等于 ProcessedBytes 含义）。未上报为 0。</summary>
      public long BatchProcessedBytes { get; set; }

      /// <summary>批次总字节数（信息列「已处理/总量」分母；非并行时等于 TotalBytes 含义）。未上报为 0。</summary>
      public long BatchTotalBytes { get; set; }

      /// <summary>实时压缩率 0–100（批次累计输出字节/输入字节×100）。7z 格式压缩/解压/分卷 ZIP 置 null → 该段隐藏。</summary>
      public double? CompressionRatio { get; set; }
  ```

  (3b) ZipEngine 各站点填充（目标是每条常规报告都带这 4 字段；逐条目终态报告（EntryStatus != null）不必）：

  - 并行解压 `:640-654`：`FileTotalBytes = entry.Size`（条目为 ArchiveEntry 时）/ `entrySize`；`BatchProcessedBytes = batchProcessedBytes`；`BatchTotalBytes = batchTotalBytes`；`CompressionRatio = null`。
  - 并行批次完成 `:719-731` / `:1236-1249`：`BatchProcessedBytes = batchTotalBytes`（批次完成）；`BatchTotalBytes = batchTotalBytes`；`FileTotalBytes = 0`（批次交汇点无单文件概念——用 0 触发 Rule 6 隐藏文件大小段）；`CompressionRatio = null`。
  - 异步提取 `:1156-1170`：同并行解压，变量为 async 版 `batchProcessedBytes(:1060)`/`batchTotalBytes(:1058)`。
  - 串行提取 `:376-385`：`FileTotalBytes = entrySize`；`BatchProcessedBytes = processedBytes + entryProcessed`；`BatchTotalBytes = totalBytes`；`CompressionRatio = null`。
  - 异步解压 `:1143-1162`：同串行提取口径。
  - 串行 ZipWriter 节流报告 `:1748-1761`：补 `TotalBytes = totalBytes`、`ProcessedBytes = processedBytes + entryProcessed`；`FileTotalBytes = entryTotalBytes`（循环内当前文件总长）；`BatchProcessedBytes = processedBytes + entryProcessed`、`BatchTotalBytes = totalBytes`；`CompressionRatio = TryGetOutputRatio(fsOut, processedBytes + entryProcessed)`。
  - `ReadFileWithRetry` 内报告 `:3273-3280`：补 `TotalBytes`/`ProcessedBytes`（需向方法签名透传或让调用点 `:1735` 后的站点覆盖——优先：给 `ReadFileWithRetry` 增加 `totalFiles` 已有，再加 `fsOut`/输出长度源参数，内部报告填同口径字段与压缩率）。
  - N 组 adapter 两处（任务 2 已补 `FilePercentComplete`）：再补 `FileTotalBytes = local.FileTotalBytes`、`BatchProcessedBytes = localDone`、`BatchTotalBytes = groupTotal`、`CompressionRatio = TryGroupOutputRatio(tempZipPath, localDone)`。
  - `CompressGroupWithSevenZip:3481` 站点 `:3553`/`:3603`/`:3662`：`BatchProcessedBytes = doneBytes`、`BatchTotalBytes = totalBytes`、`FileTotalBytes = pendingEntryKeys.Peek=当前文件 size`（FIFO 有条目时取，空则 0）；`CompressionRatio`：若 temp 输出路径可用（mirror `:3651` 时 tempPath）则 `FileInfo.Length/localDone*100`，try/catch → null。

  (3c) 辅助方法（放 `ZipEngine.cs` 私有静态）：

  ```csharp
  private static double? TryStreamOutputRatio(Stream output, long inputBytes)
  {
      if (inputBytes <= 0) return null;
      try
      {
          var len = output.CanSeek ? output.Length : (long?)null;
          if (len is null or <= 0) return null;
          return Math.Clamp((double)len.Value / inputBytes * 100.0, 0, 100);
      }
      catch (NotSupportedException) { return null; }   // SplitOutputStream.Length 抛异常 → 分卷隐藏
      catch (IOException) { return null; }
  }

  private static double? TryFileOutputRatio(string path, long inputBytes)
  {
      if (inputBytes <= 0 || string.IsNullOrEmpty(path)) return null;
      try
      {
          var len = new FileInfo(path).Length;
          return len > 0 ? Math.Clamp((double)len / inputBytes * 100.0, 0, 100) : null;
      }
      catch (Exception) { return null; }   // 文件尚未创建/占用 → 隐藏压缩率段
  }
  ```

  注：`Math.Clamp(value, 0, 100)` 的 int 重载需 `(int)` 转换或使用 double 重载（`CompressionRatio` 为 double，用 `Math.Clamp(double, double, double)`）。

  (3d) 验证：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误；`dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全绿（`SevenZipEngineTests.CompressAsync_MultiThreaded_CreatesValidArchive` 等引擎测试监测报告站点改动未破坏行为）。

- [ ] 4. D6 移除 + 非并行单行合成（`ProgressViewModel.cs`）

  **What to do**:

  (4a) 写失败测试 —— `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 现有 D6 测试（`:160-166`）后改写为：

  ```csharp
  // D6 已移除（2026-10-08 方案）：SetCurrentBatchItem 不再回落 ContentMode；
  // 详细模式在非并行时保留，由单行通道承载。
  [AvaloniaFact]
  public void SetCurrentBatchItem_DetailedMode_NoLongerFallsBackToSimple()
  {
      var vm = new ProgressViewModel();
      vm.InitBatchMode(new[] { "a.zip", "b.zip" });
      vm.ContentMode = ProgressContentMode.Detailed;
      vm.SetCurrentBatchItem(0);
      Assert.Equal(ProgressContentMode.Detailed, vm.ContentMode);
  }

  [AvaloniaFact]
  public void SetProgress_NonParallel_SynthesizesSingleChannelRow()
  {
      var vm = new ProgressViewModel();
      vm.SetProgress(new ArchiveProgress
      {
          CurrentFile = "docs/a.txt",
          PercentComplete = 40,
          TotalBytes = 1000,
          ProcessedBytes = 400,
          FileTotalBytes = 400,
          BatchTotalBytes = 1000,
          BatchProcessedBytes = 400,
      });
      var rows = vm.ParallelBatchItems;
      Assert.Single(rows);
      Assert.False(rows[0].IsParallel);
      Assert.Equal("docs", rows[0].DirectoryText);
      Assert.Equal("a.txt", rows[0].FileNameText);
  }

  [AvaloniaFact]
  public void SetProgress_Parallel_MarksRowsIsParallel()
  {
      var vm = new ProgressViewModel();
      vm.SetProgress(new ArchiveProgress
      {
          CurrentFile = "a.txt",
          BatchIndex = 0,
          BatchCount = 2,
          BatchPercentComplete = 50,
          TotalBytes = 10, ProcessedBytes = 5,
      });
      Assert.True(vm.ParallelBatchItems[0].IsParallel);
  }
  ```

  运行确认编译级红（`IsParallel`/`DirectoryText`/`FileNameText` 尚不存在）：

  ```powershell
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests"
  ```

  (4b) 移除 D6 代码 —— `ProgressViewModel.cs`：

  - 删除 `IsDetailedAvailable` 属性（`:230-231` 含注释）。
  - 删除通知点：CollectionChanged 处理器内 `OnPropertyChanged(nameof(IsDetailedAvailable));`（`:143`）、`NotifyDisplayProperties` 内同名行（`:316`）。
  - 删除 `SetCurrentBatchItem` 内 D6 回落（`:934-936` 的 `if (_contentMode == ProgressContentMode.Detailed && _parallelBatchItems.Count == 0) ContentMode = ProgressContentMode.Simple;`）。

  (4c) XAML 移除 D6 绑定 —— `ProgressWindow.axaml`：模式单选区注释 `:270`/`:285` 改为描述「始终显示三模式」；`:288` 的 `IsVisible="{Binding IsDetailedAvailable}"` 删除。

  (4d) 单行合成 —— `ProgressViewModel.cs` `SetProgress`（`:529` EntryStatus 早返回之后、`:541` SplitFilePath 之前插入）：

  ```csharp
      // 非并行报告（无 BatchIndex）：合成/更新单条通道行（IsParallel=false），复用批次行样式。
      if (p.BatchIndex == null && p.EntryKey == null)
      {
          UpsertSingleChannelRow(p);
      }
  ```

  并新增方法（放 `UpsertParallelBatch` 之后）：

  ```csharp
  /// <summary>非并行单行 upsert：集合恒保持至多 1 条 IsParallel=false 的行。</summary>
  private void UpsertSingleChannelRow(ArchiveProgress p)
  {
      var existing = _parallelBatchItems.FirstOrDefault(x => !x.IsParallel);
      if (existing == null)
      {
          existing = new ParallelBatchProgressItem { IsParallel = false };
          _parallelBatchItems.Add(existing);
      }
      existing.Percent = Math.Clamp(p.PercentComplete, 0, 100);
      existing.FileRatio = Math.Clamp((p.FilePercentComplete ?? 0) / 100.0, 0.0, 1.0);
      existing.CurrentFile = string.IsNullOrEmpty(p.CurrentFile) ? existing.CurrentFile : p.CurrentFile;
      var (dir, name) = ProgressDisplayCalculator.SplitFilePath(existing.CurrentFile);
      existing.DirectoryText = dir;
      existing.FileNameText = name;
      existing.FileSizeText = p.FileTotalBytes > 0 ? FormatUtil.FormatSize(p.FileTotalBytes) : string.Empty;
      existing.ProcessedTotalText = p.BatchTotalBytes > 0
          ? $"{FormatUtil.FormatSize(p.BatchProcessedBytes)}/{FormatUtil.FormatSize(p.BatchTotalBytes)}"
          : string.Empty;
      existing.RatioText = p.CompressionRatio is { } r ? $"{r:0.#}%" : string.Empty;
  }
  ```

  `UpsertParallelBatch`（`:690-714`）内同步填充 `DirectoryText/FileNameText`（`SplitFilePath(p.CurrentFile)`）、`FileSizeText`、`ProcessedTotalText`（`BatchProcessedBytes/BatchTotalBytes`）、`RatioText`（`CompressionRatio`），并在新行上标 `IsParallel = true`。

  (4e) `ParallelBatchProgressItem.cs` 追加字段（模型）：

  ```csharp
  /// <summary>该行是真实并行批次（true）还是非并行合成单行（false）。</summary>
  public bool IsParallel { get; init; }

  /// <summary>目录部分（已剥前缀+中间省略由 VM 写入；根文件为空串）。</summary>
  public string DirectoryText { get; set; } = "";

  /// <summary>文件名部分（恒完整）。</summary>
  public string FileNameText { get; set; } = "";

  /// <summary>信息列：文件大小（空=隐藏，Rule 6）。</summary>
  public string FileSizeText { get; set; } = "";

  /// <summary>信息列：已处理/总量（空=隐藏）。</summary>
  public string ProcessedTotalText { get; set; } = "";

  /// <summary>信息列：实时压缩率（空=隐藏，7z/解压/分卷时为空）。</summary>
  public string RatioText { get; set; } = "";
  ```

  注意：这些 setter 不是 `[ObservableProperty]` —— 与 `DetailText` 同为简单 set 会丢通知。应改为 `[ObservableProperty] private string _directoryText = "";` 等形式（与 `Percent`/`DetailText` 一致）。完整写法：

  ```csharp
  [ObservableProperty]
  private string _directoryText = "";

  [ObservableProperty]
  private string _fileNameText = "";

  [ObservableProperty]
  private string _fileSizeText = "";

  [ObservableProperty]
  private string _processedTotalText = "";

  [ObservableProperty]
  private string _ratioText = "";
  ```

  (4f) 清空语义：`InitBatchMode:881` 与 `SetCurrentBatchItem:931` 的 `_parallelBatchItems.Clear()` 不变（单行随批次切换重置）。`HasParallelBatches` 语义维持「集合非空」。

  (4g) 验证：`dotnet build` → 0 错误；`dotnet test tests\MantisZip.UI.Avalonia.Tests` → 新测试绿 + 既有 D6 语义相关断言全绿（`ProgressWindowBatchLogicTests:485-531` 不受 Affect，因为其操作的仍是 BatchIndex 驱动的行）。

- [ ] 5. 目录/文件名两列（`ProgressDisplayCalculator` + XAML 模板）

  **What to do**:

  (5a) `Core/Utils/ProgressDisplayCalculator.cs` 新增中间省略工具：

  ```csharp
  /// <summary>目录中间省略：保留首段与末段，中间以 "…" 连接。超短或不超长时原样返回。</summary>
  public static string MiddleEllipsis(string? path, int maxLength = 24)
  {
      if (string.IsNullOrEmpty(path)) return string.Empty;
      if (path.Length <= maxLength) return path;
      var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length <= 2)
      {
          // 段数太少无法保留首末：退化为前 maxLength-1 字符 + "…"
          return path[..(maxLength - 1)] + "…";
      }
      var first = parts[0];
      var last = parts[^1];
      var skeleton = $"{first}/…/{last}";
      return skeleton.Length <= maxLength ? skeleton : $"{first[..Math.Max(1, maxLength / 4)]}/…/{last[..Math.Min(last.Length, maxLength / 2)]}";
  }
  ```

  （5a 注意：末段截断时应保留末尾字符而非首部——末段是最有辨识度的尾部目录名；改用 `last[^Math.Min(last.Length, maxLength / 2)..]` 保尾。执行时按此修正。）

  (5b) `SetProgress`/`UpsertParallelBatch` 填充 `DirectoryText` 时包一层：`existing.DirectoryText = ProgressDisplayCalculator.MiddleEllipsis(dir);`。根目录文件 `dir` 为空 → `DirectoryText=""` → XAML 目录 TextBlock `IsVisible="{Binding DirectoryText, Converter={StaticResource StringNotEmpty}}"`。

  (5c) `tests/MantisZip.Tests/ProgressDisplayCalculatorTests.cs` 追加用例：

  ```csharp
  [Fact]
  public void MiddleEllipsis_ShortPath_ReturnsAsIs()
      => Assert.Equal("docs", ProgressDisplayCalculator.MiddleEllipsis("docs", 24));

  [Fact]
  public void MiddleEllipsis_LongPath_KeepsFirstAndLastSegments()
      => Assert.Equal("very-long-top/…/leaf-dir", ProgressDisplayCalculator.MiddleEllipsis("very-long-top/sub1/sub2/sub3/leaf-dir", 24));

  [Fact]
  public void MiddleEllipsis_Empty_ReturnsEmpty()
      => Assert.Equal("", ProgressDisplayCalculator.MiddleEllipsis("", 24));
  ```

  运行确认三条在实现前失败（方法不存在即编译级红），实现后绿。

  (5d) `ProgressWindow.axaml` 批次行模板（第二列 `:376-404` 当前是「当前文件单格」）改为两列两格：

  - 保留外层 Grid `:383`（底纹+文本叠加）但内部改为目录+文件名两 TextBlock 并排；或更简单：内层 Grid 改为两行？——按 D3 采用**同行两列**：目录 TextBlock（`ThemeTextSecondaryBrush`）+ 文件名 TextBlock（`ThemeTextPrimaryBrush`）。底纹 `Rectangle` 仍绑文件名格宽度（`AncestorType=Grid` 任务 1 已修）——若两列同处一 Grid，底纹将覆盖两列；要求底纹只对文件名格生效，则目录与文件名需**分两列**：外层 Grid `ColumnDefinitions="Auto,*"`（目录 Auto | 文件名 *），文件名格内层 Grid 承载底纹+文本。

  ```xml
  <!-- 批次当前文件区：目录列（中间省略，根文件隐藏）+ 文件名列（恒完整，底纹随字节进度） -->
  <Grid Grid.Column="1" ColumnDefinitions="Auto,*">
    <TextBlock Text="{Binding DirectoryText}"
               IsVisible="{Binding DirectoryText, Converter={StaticResource StringNotEmpty}}"
               FontSize="11"
               VerticalAlignment="Center"
               Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
    <Grid Grid.Column="1" ClipToBounds="True" Margin="{DynamicResource SpacingXxsThk}">
      <Rectangle Fill="{DynamicResource ProgressBarSizeBrush}"
                 HorizontalAlignment="Left"
                 IsVisible="{Binding CurrentFile, Converter={StaticResource StringNotEmpty}}">
        <Rectangle.Width>
          <MultiBinding Converter="{StaticResource RatioToWidthConverter}">
            <Binding Path="FileRatio" />
            <Binding Path="Bounds.Width" RelativeSource="{RelativeSource AncestorType=Grid}" />
          </MultiBinding>
        </Rectangle.Width>
      </Rectangle>
      <TextBlock Text="{Binding FileNameText}"
                 FontSize="11"
                 VerticalAlignment="Center"
                 Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
    </Grid>
  </Grid>
  ```

  原单格（`:383-404`）整段替换为上面的结构；`Margin` 用 `SpacingXxsThk`（规则 5）。

  (5e) 验证：build 0 错误 + `ProgressDisplayCalculatorTests` 新 3 条绿。

- [ ] 6. 信息列 + 批进度条缩窄 + 单行隐藏规则（`ProgressWindow.axaml`）

  **What to do**:

  (6a) 批次行 `Grid.ColumnDefinitions`（约当前 `Auto,*,*,Auto,Auto`）调整：

  - 第 3 列（批级进度条）由 `*` 改为固定窄列，如 `100`（D5；窗口 `Width=560`，为信息列腾位）。
  - 新增第 6 列 `Auto` 承载信息列（用 `SpacingXs` 与前列隔开）。

  ```xml
  <Grid ColumnDefinitions="Auto,*,*,100,Auto,Auto" ...>
  ```

  （目录/文件名两列在第 2 列内部，不占外层列数；底纹已在任务 5 限定到文件名格。）

  (6b) 信息列 TextBlock 插在批明细列（约 `:425`）之后：

  ```xml
  <!-- 信息列：文件大小 · 已处理/总量 · 实时压缩率（格式化见 D1；缺段隐藏）。单行时无压缩通道说明行。 -->
  <TextBlock Grid.Column="5" FontSize="10" VerticalAlignment="Center"
             Foreground="{DynamicResource ThemeTextSecondaryBrush}"
             TextTrimming="CharacterEllipsis">
    <TextBlock.Inlines>
      <Run Text="{Binding FileSizeText}" />
      <Run Text=" · " />
      <Run Text="{Binding ProcessedTotalText}" />
      <Run Text=" · " />
      <Run Text="{Binding RatioText}" />
    </TextBlock.Inlines>
  </TextBlock>
  ```

  注：缺段隐藏需按段拆开——更稳妥做法是三个独立 Run+IsVisible 在 TextBlock 上不可行（Inlines 无 IsVisible）。改为：三个独立 `TextBlock` 横排（`StackPanel Orientation="Horizontal"`），每段 `IsVisible` 绑 `StringNotEmpty`，分隔符 `" · "` 用单独 Run 且在两端段都可见时才显示——最简：分隔符直接拼入段文本（`FileSizeText` 非空时自带尾随 ` · ` 由 VM 拼）。**执行选定：VM 拼好带分隔符的完整 `InfoText`**，一段文本一个 TextBlock：

  ```csharp
  // ProgressViewModel.UpsertSingleChannelRow / UpsertParallelBatch 中（替代上文分段字段直写）：
  existing.InfoText = BuildInfoText(p);

  private static string BuildInfoText(ArchiveProgress p)
  {
      var parts = new List<string>();
      if (p.FileTotalBytes > 0) parts.Add(FormatUtil.FormatSize(p.FileTotalBytes));
      if (p.BatchTotalBytes > 0) parts.Add($"{FormatUtil.FormatSize(p.BatchProcessedBytes)}/{FormatUtil.FormatSize(p.BatchTotalBytes)}");
      if (p.CompressionRatio is { } r) parts.Add($"{r:0.#}%");
      return string.Join(" · ", parts);
  }
  ```

  ```xml
  <TextBlock Grid.Column="5" Text="{Binding InfoText}"
             IsVisible="{Binding InfoText, Converter={StaticResource StringNotEmpty}}"
             FontSize="10" VerticalAlignment="Center"
             Foreground="{DynamicResource ThemeTextSecondaryBrush}"
             TextTrimming="CharacterEllipsis" />
  ```

  （`InfoText` 为 `[ObservableProperty]`，加入 `ParallelBatchProgressItem`，替代 4e 中 `FileSizeText`/`ProcessedTotalText`/`RatioText` 三字段——若已按 4e 写入三字段，则改为单 `InfoText` 并同步改 4d 的 upsert 代码与测试断言 `rows[0].DirectoryText`/`FileNameText` 不变、信息段断言改为 `InfoText` 包含 "400 B"。）

  (6c) 单行隐藏规则 —— 批次行内以下元素补绑定（用 `IsParallel` 取反需要转换器；无现成 `!` 转换器时新增一个 bool 反转 `BoolNotConverter` 注册到 `Window.Resources`，或复用 `StringNotEmpty` 的反面——**执行新增**）：

  - 批次序号 StackPanel（约 `:364-380`）：`IsVisible="{Binding IsParallel}"`。
  - 批级进度条 ProgressBar（约 `:407`）：`IsVisible="{Binding IsParallel}"`。
  - 百分比 TextBlock（约 `:416`）：`IsVisible="{Binding IsParallel}"`。
  - 批明细 TextBlock（约 `:425`）：`IsVisible="{Binding IsParallel}"`。
  - 压缩通道说明行（`IsCompressFlow` 说明 TextBlock，约批次行模板外/面板内）：绑定改为「压缩流程 且 有并行批次」——VM 新增派生 `ShowCompressChannelHint => IsCompressFlow && _parallelBatchItems.Any(x => x.IsParallel)`，通知挂 `NotifyDisplayProperties`。

  bool 反转需求：若 6c 直接绑 `IsParallel`（正向）则隐藏规则天然满足（单行 IsParallel=false → IsVisible=false），无需反转转换器。`ShowCompressChannelHint` 派生属性走集中通知（`NotifyDisplayProperties` 追加一行 `OnPropertyChanged(nameof(ShowCompressChannelHint));`）。

  (6d) 验证：build 0 错误；人工 GUI 验收并入任务 7。

- [ ] 7. 构建 + 双测试套 + GUI 四场景验收

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
  ```

  GUI 四场景：并行 ZIP 解压（多通道+底纹+信息列齐全）/ 非并行 ZIP 压缩（单行、无批次序号/进度条/百分比/批明细/说明行、信息列含压缩率）/ 7z 压缩（信息列无压缩率段）/ 单条解压（底纹与当前文件字节比例一致、非整行宽）。

- [ ] 8. 进度文档同步与提交（仅用户明确要求提交时）

  按规则 3 更新 `docs/PROGRESS.md`（里程碑）+ `docs/progress-avalonia-detail.md`（细节），再按规则 10 conventional commits 提交。

---

## 附：关键行号索引（落笔时核实，stats-cards 落地后需校准）

| 符号 | 文件 | 行
|------|------|-----|
| `ArchiveProgress` 字段区 | Core/Abstractions/ArchiveEngine.cs | :318-354 |
| `SetProgress` 主体 | UI.Avalonia/ViewModels/ProgressViewModel.cs | :516 |
| EntryStatus 早返回 | 同上 | :529 |
| `UpsertParallelBatch` | 同上 | :690-714 |
| 自动切详细守卫 | 同上 | :712 |
| D6 回落（删除） | 同上 | :934-936 |
| `IsDetailedAvailable`（删除） | 同上 | :230-231 |
| 通知点（删除） | 同上 | :143 / :316 |
| 模式单选 D6 绑定（删除） | UI.Avalonia/Dialogs/ProgressWindow.axaml | :288（注释 :270/:285） |
| 批次行模板 | 同上 | :361-435 |
| 底纹 Rectangle bug（修复） | 同上 | :386-396 |
| 串行 ZipWriter 报告 | Core/Engines/ZipEngine.cs | :1748-1761 |
| N 组 adapter ×2 | 同上 | :1516-1541 / :2459-2483 |
| `CompressGroupWithSevenZip` | 同上 | :3481 |
| `SplitOutputStream.Length` 抛异常 | Core/Utils/SplitOutputStream.cs | :91 |
