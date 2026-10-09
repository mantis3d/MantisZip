# 进度窗口详细模式通道信息增强（信息列 + 常显 + 两列路径 + 底纹修复）

> **状态**: 📋 设计已确认 + 任务分解已生成（2026-10-08），待实施
>
> **For agentic workers:** 本文档是设计规格 + 完整任务分解（澄清 5 问 + Rule 0 补问已定稿，见「已确认设计决策」；文末「任务分解」含 8 任务 checkbox 步骤）。执行时使用 superpowers:subagent-driven-development 或 superpowers:executing-plans。

## TL;DR

> **Quick Summary**: 进度窗「详细」模式通道行**重构为左右分组**：左=单文件域（目录中间省略 + 文件名恒完整 + 文件底纹 + 文件大小），右=批次域（批次序号 + 批次底纹 + 合并百分比明细 + 字节进度/压缩率 + ToolTip）。核心增强：① **批级进度底纹化**——右区背景底纹（不同色相）取代独立 ProgressBar 控件；② **合并百分比+明细**为单字段 `45% (12/40)`；③ 新增信息列（**字节进度 · 实时压缩率**，文件大小已移至左区）；④ **ToolTip** 悬停右区解释字段；⑤ 详细模式始终可用（推翻详细门禁），非并行显示单行；⑥ 路径两列（目录中间省略 + 文件名恒完整）；⑦ 修复底纹双缺陷（几何宽度基准错误 + N 组适配层漏拷 `FilePercentComplete`）。**不加列标题**（左右分组本身即语义分区）。
>
> **Deliverables**:
> - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` —— `ArchiveProgress` 新增 `FileTotalBytes`/`BatchProcessedBytes`/`BatchTotalBytes`/`CompressionRatio`
> - `src/MantisZip.Core/Engines/ZipEngine.cs` —— 全部报告站点填充新字段；N 组 adapter 修复漏拷 `FilePercentComplete`
> - `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs` —— 移除详细门禁（`IsDetailedAvailable`）；非并行单行合成；`MiddleEllipsis` 接线；`UpsertParallelBatch`/`UpsertSingleChannelRow` 新字段填充
> - `src/MantisZip.UI.Avalonia/Models/ParallelBatchProgressItem.cs` —— 行模型扩展（`IsParallel`/`DirectoryText`/`FileNameText`/`FileSizeText`/`PctDetailText`/`InfoText`/`BatchRatio`）
> - `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` —— 底纹几何修复、通道行模板**左右分组重构**（批进度底纹化、合并百分比、ToolTip、单行隐藏规则）
> - `Core/Utils/ProgressDisplayCalculator.cs` —— 新增中间省略工具 + 路径两列拆分
> - `ProgressViewModelTests.cs` / `ProgressWindowXamlTests.cs` / `ProgressWindowBatchLogicTests.cs` —— 详细门禁重写 + 新测试
> - 三语 i18n：ToolTip 标签 key（豁免原「无新增 key」护栏，见 D7）
> - `docs/PLAN.md` 登记行（规则 1）
>
> **Estimated Effort**: Medium（2–3 天）
> **Parallel Execution**: YES — Wave 1 任务 1/2 并列（不同文件 XAML）；任务 3 依赖 archive 进度契约
> **Critical Path**: 用户审阅本设计 → 任务 1/2（底纹双修）→ 任务 3（ArchiveProgress 契约+引擎填充）→ 任务 4（门禁移除+单行）→ 任务 5/6（UI 布局）→ GUI 验收

---

## Context

### Original Request

用户原话（2026-10-08，5 问澄清后定稿；2026-10-09 追加讨论修订）：

1. 「详细」模式下每个通道/批次行应该有信息列：文件大小、已处理/总量、实时压缩率。7z 压缩不显示压缩率；解压不显示压缩率；非并行（单行）时也要显示信息列。`FormatUtil.FormatSize` 格式化，缺子字段按 Rule 6 隐藏。
2. 「详细」模式应该始终显示（推翻「详细门禁」）：非并行时显示单条通道行复用批次行样式，但隐藏：批次序号、批明细、压缩通道说明行——进度表达交给底纹+信息列数字。
3. 长路径显示两列：目录（中间省略）+ 文件名（恒完整）；根级文件目录列隐藏。
4. 底纹有 bug：① 几何错误（宽度基准取自 `ContentPresenter.Bounds.Width`=整行宽度，应为最近 Grid）② N 组适配层（adapter）漏拷 `FilePercentComplete`。

**2026-10-09 讨论追加（用户提出，均已确认）**：

5. **左右分组**：单文件信息（目录/文件名/文件底纹/文件大小）归左，批次信息（序号/进度/字节/压缩率）归右——解决"单文件信息与批信息混在一起"。
6. **批级进度底纹化**：批级进度条由独立 `ProgressBar` 控件改为右区背景底纹，与文件底纹统一视觉语言、色相区分。
7. **百分比与明细合并**为单字段 `45% (12/40)`。
8. **ToolTip**：悬停右区浮层解释"进度/字节/压缩率"。
9. **不加列标题**（左右分组本身即语义分区，进度窗口为瞬态 UI）。

### Research Findings

- **`IsDetailedAvailable`（详细门禁，继承自旧计划）分布**：XAML `ProgressWindow.axaml:270`(注释)/`:288`(`IsVisible="{Binding IsDetailedAvailable}"`)；VM `ProgressViewModel.cs:231`（`_parallelBatchItems.Count > 0`）、通知 `:143`/`:316`（`NotifyDisplayProperties:311-322` 经 ContentMode/InfoDensity setter `:199-210` 调用）；门禁回落约 `SetCurrentBatchItem:934-936`（详细模式且集合空 → 回退 Simple）；测试 `ProgressViewModelTests.cs:160-166` 断言该回落（门禁移除后必改）。grep 确认测试中无其他 `IsDetailedAvailable` 引用。
- **`UpsertParallelBatch`**（`ProgressViewModel.cs:690-714`）：Percent 钳制写入、`FileRatio = FilePercentComplete/100`、CurrentFile 保持、StatusBrushName、DetailText（`T("Progress_Batch_FilesProgress")`）、自动切详细守卫 `:712`（保留）。
- **`SetProgress:516`**：`EndPreparing` → `:524` BatchIndex upsert → `:529` EntryStatus 早返回 → `:541-550` `SplitFilePath` → `:553` 批次总进度。
- **批次行 XAML**（`ProgressWindow.axaml`，**改造前现状**，任务 5/6 将重构为左右分组）：列 `Auto,*,*,Auto,Auto`（批次序号 | 当前文件 | 批级进度条 | 百分比 | 明细）；内层 Grid `:383`；底纹 Rectangle `:386-396` 绑 `Bounds.Width` + `RelativeSource AncestorType=ContentPresenter`（bug：ContentPresenter 是整行容器，应取内层 Grid）；批进度条 `:407`；窗口 `Width=560`（`:7`）。
- **`ParallelBatchProgressItem.cs`**（30 行）：Index(init)/Percent/StatusBrushName/DetailText/CurrentFile/FileRatio（本计划追加 IsParallel/DirectoryText/FileNameText/FileSizeText/PctDetailText/InfoText/BatchRatio/TooltipText，见任务 4）。
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

## 已确认设计决策（2026-10-08 逐段确认 → 2026-10-09 讨论修订）

> **2026-10-09 修订摘要**：通道行由「6 列平铺」改为「**左右分组**」——左=单文件域、右=批次域；批级进度条**底纹化**（色相区分）；百分比与明细**合并**；新增 **ToolTip**；**不加列标题**。原型 `docs/prototypes/progress-window-channel-info.html` 已更新为 v2 并经用户确认。

### D1 左右分组与信息列口径（方案 A 批次级）

- **通道行重构为左右两组**（2026-10-09 讨论定稿）：
  - **左 = 单文件域**：目录（中间省略，D3）+ 文件名（恒完整）+ 文件底纹（`FileRatio`）+ 文件大小（`FileTotalBytes`）。
  - **右 = 批次域**：批次序号 + 批次底纹（`BatchRatio`，D5）+ 合并百分比明细（`PctDetailText`，D6）+ 信息列（字节进度 · 压缩率）+ ToolTip（D7）。
- **信息列只保留两段**：`已处理/总量 · 实时压缩率`（文件大小已移至左区，不在信息列重复）。
- 「已处理/总量」= **批次级**：`ArchiveProgress` 新增 `BatchProcessedBytes`/`BatchTotalBytes`（非并行单行时批次=整体，填同一值）。
- 「文件大小」= `ArchiveProgress.FileTotalBytes`（当前条目/文件总大小），**显示在左区**（非信息列）。
- 「实时压缩率」= 批次累计输出字节/输入字节×100（0–100），`CompressionRatio` 为 `double?`；7z 格式压缩与所有解压路径置 null → 该段按 Rule 6 隐藏。Store 模式/不可压文件可能 >100% → `Math.Clamp(...,0,100)` 显示 100%。
- 所有显示走 `FormatUtil.FormatSize(long)`（`FormatUtil.cs:13`）；`FileProcessedBytes` 已废弃（不采用）。
- 缺子字段按 Rule 6 隐藏：整段（压缩率段）为空则该段连同前置分隔符隐藏。
- 信息列纯数值+分隔符（`·`/`/`），**信息列本身无新增 i18n key**；ToolTip 标签见 D7（豁免原护栏）。

### D2 详细模式常显（推翻「详细门禁」）

> 注：本决策推翻的是**旧计划的「详细模式可用性门禁」**（原 `IsDetailedAvailable`：无并行数据即隐藏「详细」），与该门禁的旧编号 D6 同名仅属继承标签，**非本文件 D6**（本文件 D6 已改为「合并百分比+明细」，见下）。

- 移除 `IsDetailedAvailable`（属性 `:231`、两处通知 `:143`/`:316`、XAML `:288` + 注释 `:270/:285`、`SetCurrentBatchItem:934-936` 回落、测试 `:160-166` 重写）；「详细」单选按钮不再受可用性门禁。
- 非并行（`BatchIndex` 为 null）时，VM 在 `SetProgress`（`:529` EntryStatus 早返回之后、`:541` 路径拆分之前）合成单条 `ParallelBatchProgressItem`（`IsParallel=false`）并 upsert 进集合，复用批次行样式：
  - 隐藏：批次序号、批明细（`DetailText`，文件个数分数）；**保留**右区百分比（`PctDetailText` 仅 `45%` 无括注）与批次底纹——单行时二者 = 整体进度（`PercentComplete`），是有效信息。
  - 压缩通道说明行（`IsCompressFlow`）：绑定改为「压缩流程 **且** 有并行通道」（D8 `HasParallelChannel`）——单行即无并行通道，说明行无意义。
  - 进度表达：左区文件底纹（`FileRatio`）+ 右区批次底纹（整体 `Percent`）+ 信息列数字。
  - 注意：`ProgressWindowXamlTests:110-149` 断言非并行时 `ParallelBatchItems` 空集合——该等价断言改测「无批次数据时区分单行与批次行」：新增字段 `IsParallel`（批次行 true / 单行 false），测试改为断言 `ParallelBatchItems` 中所有项 `IsParallel == false` 或数量≤1（语义保留「不伪装并行批次」）。
- 保留 Task 6 并行自动切详细（`UpsertParallelBatch:712`）。
- `HasParallelBatches:244`（`_parallelBatchItems.Count > 0`）语义**不足以**区分「有并行通道」——单行也会进入该集合使其为 true。新增派生属性 `HasParallelChannel`（`_parallelBatchItems.Any(x => x.IsParallel)`，D8），实施任务 4 时 grep 全部 `HasParallelBatches` 引用按语义替换。

### D3 路径两列（目录中间省略 + 文件名恒完整）

- 复用 `ProgressDisplayCalculator.SplitFilePath:22`（剥离引擎前缀后按最后一个 `/` 拆目录/文件名）。
- 新增 `ProgressDisplayCalculator.MiddleEllipsis(string path, int maxLength)`：超长目录保留首段+末段，中间 `…`；根级文件目录为空串 → 目录 TextBlock `IsVisible=false`（Rule 6）。
- 文件名恒完整显示（不裁剪）；目录列与文件名列中间给固定间距。
- `ParallelBatchProgressItem` 新增 `DirectoryText`/`FileNameText` 字段，VM 在 `SetProgress`/`UpsertParallelBatch` 填充。

### D4 底纹双缺陷修复

- **几何**：`ProgressWindow.axaml` 底纹 Rectangle 的 `MultiBinding` 第二输入由 `RelativeSource AncestorType=ContentPresenter`（整行宽度）改为最近 `Grid`（文件名格内层 Grid `:383`），内层 Grid 加 `ClipToBounds="True"`。**右区批次底纹同理**（容器取右区自身 Grid + `ClipToBounds`）。
- **漏拷**：`ZipEngine.cs` N 组 adapter `:1516-1541` 与 `:2459-2483` 两处 `new ArchiveProgress` 补上 `FilePercentComplete = local.FilePercentComplete`（及新增的 4 字段，见任务 3）。
- 解压实测已完成：不再单独做「复现→排查」，几何修复后直接进入五场景 GUI 验收（D11 变更清单 ②）。

### D5 批级进度底纹化（取代原「缩窄」）

- **移除**独立批次进度条 `ProgressBar` 控件，改为**右区背景底纹**：`Rectangle` 宽度 = `BatchRatio`（批次 `Percent`/100）× 右区实际宽度，复用 `RatioToWidthConverter`（与左区文件底纹同一机制，D9）。
- **色相区分**：左区文件底纹用现有 `ProgressBarSizeBrush`（蓝系）；右区批次底纹换**不同色相**（紫系），新增主题画刷（如 `ThemeProgressBatchBrush`），成对写入 `ThemeLight.axaml`/`ThemeDark.axaml`（规则 4）。
- 底纹层置于文字层之下（层级顺序 / `Panel.ZIndex`），保证文字可读。

### D6 合并百分比 + 明细、不加列标题

- 原独立「百分比列」（`Percent` → `45%`）与「批明细列」（`DetailText` → `12/40 文件`）**合并为单字段** `PctDetailText`：格式 `45% (12/40)`（进度优先、分数括注）；单行时仅 `45%`。
- **不加列标题**：左右分组本身即语义分区（左=文件、右=批次），进度窗口为瞬态 UI，加标题徒增噪音且需 6+ i18n key；用户困惑由 ToolTip（D7）兜底。

### D7 ToolTip（悬停右区解释字段）

- 右区（批次域）挂 `ToolTip.Tip`，悬停显示三行：进度 / 字节 / 压缩率（压缩率段在 7z/解压时同步隐藏，Rule 6）。
- 承载：`ParallelBatchProgressItem.TooltipText`（VM 拼好，`\n` 分隔）或 `ToolTip` 内容模板 — 实施任务 6 定。
- **豁免原「信息列无新增 i18n key」护栏**：新增 3 个 key（如 `Progress_Tooltip_Progress`/`Progress_Tooltip_Bytes`/`Progress_Tooltip_Ratio`）三语成对添加（规则 13）。

### D8 单行隐藏规则 + `HasParallelChannel`

- 非并行单行（`IsParallel=false`）隐藏：批次序号、批明细；**保留**右区百分比（=整体进度）与批次底纹（=整体进度）。
- 压缩通道说明行：`IsCompressFlow && HasParallelChannel` → 单行时不显示。
- 新增派生属性 `HasParallelChannel => _parallelBatchItems.Any(x => x.IsParallel)`（走 `NotifyDisplayProperties` 集中通知）；实施任务 4 时 grep `HasParallelBatches` 全部引用点，按语义决定替换为 `HasParallelChannel`（并行专属 UI）或保留（批次列表区显示）。
- 实现：XAML 元素直接 `IsVisible="{Binding IsParallel}"`（正向绑，无需反转转换器）。

### D9 双底纹机制（file vs batch）

- 左区文件底纹：`FileRatio`（0-1，现有）= `FilePercentComplete/100`。
- 右区批次底纹：新增 `BatchRatio`（0-1）= `Percent/100`。
- 二者均经 `RatioToWidthConverter`（双输入：ratio + 容器宽）驱动 `Rectangle.Width`；容器分别取左区文件名格 / 右区自身实际宽度（`Bounds.Width`，`RelativeSource AncestorType=Grid` + `ClipToBounds`，见 D4）。

### D10 右区 MinWidth

- 右区（批次域）`MinWidth`（约 180px）锁底线，防内容长度变化导致相邻行宽度跳动（与 stats-cards 卡片 `MinWidth=80` 同一诉求）。

### D11 变更清单（已批准 7 条）

1. 底纹修复拆两任务（几何 + 漏拷）分别独立 checkbox；
2. ~~解压实测复现再排查~~ 删除，几何修复后直接五场景 GUI 验收；
3. 信息列 `FormatUtil.FormatSize` + Rule 6 隐藏；
4. 与 stats-cards 计划耦合：stats-cards 先实施，本计划行号落地后校准；
5. 归新计划 `progress-window-channel-info.md`（本文件）；
6. **左右分组 + 批进度底纹化 + 合并百分比 + ToolTip + 不加列标题**（2026-10-09 讨论定稿，原型 v2 已确认）；
7. i18n 护栏豁免（仅 ToolTip 新增 3 key，信息列仍纯数值）。

---

## Work Objectives

### Core Objective

「详细」模式始终可用且信息分区清晰：通道行**左右分组**（左=单文件域、右=批次域），左区展示当前文件的目录/文件名/字节底纹/大小，右区展示批次序号/批次底纹/进度（合并百分比明细）/字节进度/压缩率；批级进度以底纹（色相区分）表达，悬停右区有 ToolTip 解释；路径两列可读，非并行单行不伪装成并行批次，底纹（当前文件字节进度）准确反映真实文件粒度。

### Concrete Deliverables

- `ArchiveEngine.cs`：`ArchiveProgress` 新增 `FileTotalBytes` / `BatchProcessedBytes` / `BatchTotalBytes` / `CompressionRatio` 四字段。
- `ZipEngine.cs`：全部 `ArchiveProgress` 报告站点填充新字段；N 组 adapter 两处补拷 `FilePercentComplete` 与新字段；`CompressionRatio` 按 D1 口径填充/置 null。
- `ProgressViewModel.cs`：删除「详细门禁」（`IsDetailedAvailable` 及通知点、回落）；非并行单行合成（`UpsertSingleChannelRow`）；`MiddleEllipsis` 接线；`UpsertParallelBatch`/`UpsertSingleChannelRow` 新字段填充；新增派生 `HasParallelChannel`。
- `ParallelBatchProgressItem.cs`：新增 `IsParallel`、`DirectoryText`、`FileNameText`、`FileSizeText`、`PctDetailText`、`InfoText`、`BatchRatio`（+ ToolTip 承载字段）。
- `ProgressWindow.axaml`：底纹几何修复；模式单选区门禁绑定移除；通道行模板**左右分组重构**（左=目录/文件名两列+文件底纹+大小；右=批次序号+批次底纹+合并百分比+信息列+ToolTip，`MinWidth` 锁底线）；中文注释（规则 14）。
- 主题：`ThemeLight.axaml`/`ThemeDark.axaml` 成对新增批次底纹画刷（`ThemeProgressBatchBrush` 或等价，规则 4）。
- 三语 i18n：ToolTip 3 key（`strings.zh-CN.json`/`strings.en.json`/`strings.zh-TW.json` 成对，规则 13）。
- `ProgressDisplayCalculator.cs`：新增 `MiddleEllipsis`。
- 测试：`ProgressViewModelTests` 门禁移除重写 + 单行合成断言；`ProgressWindowXamlTests` 非并行语义调整；`ProgressWindowBatchLogicTests` 新字段断言；`ProgressDisplayCalculatorTests` 中间省略用例。

### Definition of Done

- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误 0 警告
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿（既有预存失败除外）
- [ ] `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全绿
- [ ] GUI 手动验收五场景：并行 ZIP 解压 / 非并行 ZIP 压缩 / 7z 压缩（压缩率隐藏）/ 单条解压（底纹正确）/ **分卷 ZIP 压缩（压缩率隐藏）**
- [ ] 左右分组视觉平衡、批次底纹色相可辨、ToolTip 悬停正常

### Must Have

- 详细模式不再有「无数据即消失」门禁；非并行显示单行且不出现批次序号/批明细/压缩说明行（保留右区百分比与批次底纹）。
- 通道行左右分组：左=单文件域（目录/文件名/文件底纹/大小），右=批次域（序号/批次底纹/合并百分比/字节进度/压缩率/ToolTip）。
- 批级进度以**右区底纹**表达（色相区别于左区文件底纹），不再用独立 `ProgressBar`。
- 百分比与明细合并为 `45% (12/40)` 单字段。
- ToolTip 悬停右区解释进度/字节/压缩率。
- 信息列两段齐全（字节进度 + 压缩率），值来自 `ArchiveProgress` 新字段，`FormatSize` 格式化；7z 压缩与解压隐藏压缩率段；分卷 ZIP（SplitOutputStream）同样隐藏。
- 路径两列：目录中间省略、文件名完整；根文件名场景目录列隐藏。
- 底纹宽度 = 当前文件节字节比例 × 文件名格实际宽度（不再取整行宽）；批次底纹 = 批次比例 × 右区宽度。
- 右区 `MinWidth` 锁底线防跳动；不加列标题。
- 新增 axaml 控件中文注释（规则 14）；间距用 `{DynamicResource Spacing*}`（规则 5）。

### Must NOT Have (Guardrails)

- **不得**新增信息列相关 i18n key（信息列纯符号/数值）；**唯一豁免**：ToolTip 的 3 个标签 key（D7）。
- **不得**把非并行场景伪装成多批次（`IsParallel=false` 单行，数量恒 1）。
- **不得**改 `ArchiveProgress` 既有字段语义（只加字段）；`ConflictStats.ApplyTo` 不丢新字段（已核实原地改）。
- **不得**破坏并行自动切详细（`UpsertParallelBatch:712` 保留）。
- **不得**混入 stats-cards 改动（行号漂移来源，先后落地）。
- 版本号不变（规则 2）。

---

## Verification Strategy (MANDATORY)

### Test Decision

- **Infrastructure exists**: YES（xunit.v3 + Avalonia.Headless.XUnit，`ProgressViewModelTests`/`ProgressWindowXamlTests`/`ProgressWindowBatchLogicTests` 已有 SetProgress 系列用例）。
- **Automated tests**: Unit（VM 单行合成、`MiddleEllipsis`、压缩率 null 隐藏、详细门禁移除后断言、adapter 漏拷回归、`PctDetailText`/`InfoText` 拼接、`BatchRatio`）。
- **Framework**: xunit.v3，沿用既有文件与 `[Fact]`/`[AvaloniaFact]` 模式。

### AI-Driven QA

- Avalonia 桌面 UI，视觉验收（左右分组、双底纹色相/对齐、批次底纹、ToolTip、单行隐藏规则）走**人工 GUI 验收**，不做浏览器 QA。

### 自动化验证命令

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```

---

## 依赖与后续

- **依赖**：`progress-stats-cards-three-row` 计划**已实施**（commit `6fc2b01`，2026-10-09；两者都改 `ProgressViewModel.cs`，该计划落地后本计划行号需校准，见附录）；`progress-window-bytes-i18n` 为软依赖（7z/TAR 字节埋点补齐后，信息列「已处理/总量」在 7z/TAR 下由隐藏变真实，本设计零改动）。
- **前向兼容契约（stats-cards 累加器依赖）**：本计划任务 3 落地 `ArchiveProgress.FileTotalBytes` 后，**逐条目终态报告（EntryStatus != null）也必须携带 `FileTotalBytes`**（当前条目原始尺寸），使 stats-cards 的跳过/出错/已覆盖累加器在渐进模式（TAR/GZ、>5000 条目未播种）下自动升级为 100% 覆盖；stats-cards 侧接线只需把 `SetProgress` 的 `EntryStatus` 块调用末参从 `null` 改传 `p.FileTotalBytes`（该计划已预留参数并已接线 `null`，落地后改传即生效，零结构改动）。
- **后续受益**：信息列与统计卡同源 `ArchiveProgress`，字节埋点到位即自动生效。
- **文档同步**：本文件新增 → `docs/PLAN.md` P2 区登记（规则 1）；2026-10-09 设计修订需同步 `docs/PLAN.md` 登记行说明与 `docs/progress-avalonia-detail.md`。

---

## 任务分解

> **执行方式**: superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，checkbox 逐步勾选。
> **提交策略**: 默认**不 commit**（未经用户明确要求）；仅用户要求时执行末尾提交任务，并按规则 3 先更新进度文档。

### 执行波次

```
Wave 1（底纹双修，并列）
├── 任务 1: 底纹几何修复（XAML 绑定侧 + ClipToBounds，左右两底纹）
└── 任务 2: N 组 adapter 漏拷 FilePercentComplete 修复

Wave 2（契约）
└── 任务 3: ArchiveProgress 新字段 + ZipEngine 全站点填充（Blocked By: 无，可与 Wave 1 并行，但字段拷贝依赖任务 2 的约定）

Wave 3（VM 语义）
└── 任务 4: 详细门禁移除 + 非并行单行合成 + 行模型扩展（Blocked By: 3）

Wave 4（UI 布局，同 XAML 文件需顺序）
├── 任务 5: 左区（目录/文件名两列 + 中间省略 + 文件大小）（Blocked By: 4）
└── 任务 6: 右区（批次底纹化 + 合并百分比 + 信息列 + ToolTip）+ 单行隐藏规则（Blocked By: 4）

Wave FINAL
├── 任务 7: 构建 + 双测试套 + GUI 五场景验收（Blocked By: 1–6）
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

  (3b) ZipEngine 各站点填充（目标是每条常规报告都带这 4 字段；逐条目终态报告（EntryStatus != null）时 `BatchProcessedBytes`/`BatchTotalBytes`/`CompressionRatio` 不必填，但 **`FileTotalBytes` 必须携带**——前向兼容 stats-cards 终态字节累加器，见「依赖与后续」契约）：

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

- [ ] 4. 详细门禁移除 + 非并行单行合成 + 行模型扩展（`ProgressViewModel.cs` + `ParallelBatchProgressItem.cs`）

  **What to do**:

  (4a) 写失败测试 —— `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 现有门禁测试（`:160-166`）后改写为：

  ```csharp
  // 详细门禁已移除（2026-10-09 方案）：SetCurrentBatchItem 不再回落 ContentMode；
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
          FileTotalBytes = 400,
          BatchTotalBytes = 1000,
          BatchProcessedBytes = 400,
      });
      var rows = vm.ParallelBatchItems;
      Assert.Single(rows);
      Assert.False(rows[0].IsParallel);
      Assert.Equal("docs", rows[0].DirectoryText);
      Assert.Equal("a.txt", rows[0].FileNameText);
      Assert.Equal("400 B", rows[0].FileSizeText);        // 左区文件大小
      Assert.Contains("400 B/1000 B", rows[0].InfoText);  // 右区字节进度
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

  运行确认编译级红（`IsParallel`/`DirectoryText`/`FileNameText`/`FileSizeText`/`InfoText` 尚不存在）：

  ```powershell
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests"
  ```

  (4b) 移除门禁代码 —— `ProgressViewModel.cs`：

  - 删除 `IsDetailedAvailable` 属性（`:230-231` 含注释）。
  - 删除通知点：CollectionChanged 处理器内 `OnPropertyChanged(nameof(IsDetailedAvailable));`（`:143`）、`NotifyDisplayProperties` 内同名行（`:316`）。
  - 删除 `SetCurrentBatchItem` 内回落（`:934-936` 的 `if (_contentMode == ProgressContentMode.Detailed && _parallelBatchItems.Count == 0) ContentMode = ProgressContentMode.Simple;`）。

  (4c) XAML 移除门禁绑定 —— `ProgressWindow.axaml`：模式单选区注释 `:270`/`:285` 改为描述「始终显示三模式」；`:288` 的 `IsVisible="{Binding IsDetailedAvailable}"` 删除。

  (4d) 单行合成 —— `ProgressViewModel.cs` `SetProgress`（`:529` EntryStatus 早返回之后、`:541` SplitFilePath 之前插入）：

  ```csharp
      // 非并行报告（无 BatchIndex 且非逐条目终态）：合成/更新单条通道行（IsParallel=false），复用批次行样式。
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
      existing.BatchRatio = Math.Clamp(p.PercentComplete / 100.0, 0.0, 1.0);      // 右区批次底纹
      existing.FileRatio = Math.Clamp((p.FilePercentComplete ?? 0) / 100.0, 0.0, 1.0); // 左区文件底纹
      existing.CurrentFile = string.IsNullOrEmpty(p.CurrentFile) ? existing.CurrentFile : p.CurrentFile;
      var (dir, name) = ProgressDisplayCalculator.SplitFilePath(existing.CurrentFile);
      existing.DirectoryText = ProgressDisplayCalculator.MiddleEllipsis(dir);     // 中间省略（任务 5）
      existing.FileNameText = name;
      existing.FileSizeText = p.FileTotalBytes > 0 ? FormatUtil.FormatSize(p.FileTotalBytes) : string.Empty; // 左区
      existing.PctDetailText = p.PercentComplete > 0 ? $"{Math.Round(p.PercentComplete)}%" : string.Empty;   // 单行无括注
      existing.InfoText = BuildInfoText(p);                                       // 右区：字节进度 + 压缩率
      existing.TooltipText = BuildTooltipText(p);                                 // 右区 ToolTip
  }

  /// <summary>右区信息列：字节进度 · 压缩率（缺段省略，规则 6）。</summary>
  private static string BuildInfoText(ArchiveProgress p)
  {
      var parts = new List<string>();
      if (p.BatchTotalBytes > 0) parts.Add($"{FormatUtil.FormatSize(p.BatchProcessedBytes)}/{FormatUtil.FormatSize(p.BatchTotalBytes)}");
      if (p.CompressionRatio is { } r) parts.Add($"{r:0.#}%");
      return string.Join(" · ", parts);
  }

  /// <summary>右区 ToolTip：进度 / 字节 / 压缩率（压缩率缺省则省略该行）。</summary>
  private static string BuildTooltipText(ArchiveProgress p)
  {
      var lines = new List<string>
      {
          $"{LocalizationManager.T("Progress_Tooltip_Progress")}: {Math.Round(p.PercentComplete)}%",
          $"{LocalizationManager.T("Progress_Tooltip_Bytes")}: {FormatUtil.FormatSize(p.BatchProcessedBytes)}/{FormatUtil.FormatSize(p.BatchTotalBytes)}",
      };
      if (p.CompressionRatio is { } r) lines.Add($"{LocalizationManager.T("Progress_Tooltip_Ratio")}: {r:0.#}%");
      return string.Join("\n", lines);
  }
  ```

  `UpsertParallelBatch`（`:690-714`）内同步填充同名新字段：`DirectoryText`/`FileNameText`（`SplitFilePath` + `MiddleEllipsis`）、`FileSizeText`、`InfoText`（`BuildInfoText`）、`TooltipText`；`BatchRatio = Percent/100`；`PctDetailText = $"{Percent:0}% ({doneFiles}/{totalFiles})"`（文件个数分数）；新行上标 `IsParallel = true`。

  (4e) `ParallelBatchProgressItem.cs` 追加字段（`[ObservableProperty]`，与 `Percent`/`DetailText` 一致；`IsParallel` 行创建后不变用 init）：

  ```csharp
  /// <summary>该行是真实并行批次（true）还是非并行合成单行（false）。</summary>
  public bool IsParallel { get; init; }

  /// <summary>左区：目录部分（已剥前缀 + 中间省略由 VM 写入；根文件为空串）。</summary>
  [ObservableProperty] private string _directoryText = "";

  /// <summary>左区：文件名部分（恒完整）。</summary>
  [ObservableProperty] private string _fileNameText = "";

  /// <summary>左区：文件大小（空=隐藏，规则 6）。</summary>
  [ObservableProperty] private string _fileSizeText = "";

  /// <summary>右区：合并百分比+明细（"45% (12/40)"；单行仅 "45%"）。</summary>
  [ObservableProperty] private string _pctDetailText = "";

  /// <summary>右区：信息列（字节进度 · 压缩率；空=隐藏）。</summary>
  [ObservableProperty] private string _infoText = "";

  /// <summary>右区：批次底纹比例 0-1（= Percent/100）。</summary>
  [ObservableProperty] private double _batchRatio;

  /// <summary>右区：ToolTip 文本（VM 拼好，\n 分隔）。</summary>
  [ObservableProperty] private string _tooltipText = "";
  ```

  注意：`IsParallel` 为普通 init 属性；其余均为 `[ObservableProperty]`（与 `Percent`/`DetailText` 通知一致）。

  (4f) `HasParallelChannel` 派生属性：`public bool HasParallelChannel => _parallelBatchItems.Any(x => x.IsParallel);`，在 `NotifyDisplayProperties` 追加 `OnPropertyChanged(nameof(HasParallelChannel));`。实施时 grep `HasParallelBatches` 全部引用点，按语义决定替换（并行专属 UI 用 `HasParallelChannel`；批次列表区显示保留原属性）。

  (4g) 清空语义：`InitBatchMode:881` 与 `SetCurrentBatchItem:931` 的 `_parallelBatchItems.Clear()` 不变（单行随批次切换重置）。`HasParallelBatches` 语义维持「集合非空」。

  (4h) 验证：`dotnet build` → 0 错误；`dotnet test tests\MantisZip.UI.Avalonia.Tests` → 新测试绿 + 既有门禁语义相关断言全绿（`ProgressWindowBatchLogicTests:485-531` 不受影响，其操作的仍是 BatchIndex 驱动的行）。

- [ ] 5. 左区（目录/文件名两列 + 中间省略 + 文件大小）（`ProgressDisplayCalculator` + XAML 模板）

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

  (5b) 两个 upsert（`UpsertSingleChannelRow`/`UpsertParallelBatch`）填充 `DirectoryText` 时包一层 `ProgressDisplayCalculator.MiddleEllipsis(dir)`（4d 已含）。根目录文件 `dir` 为空 → `DirectoryText=""` → XAML 目录 TextBlock `IsVisible="{Binding DirectoryText, Converter={StaticResource StringNotEmpty}}"` 隐藏。

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

  (5d) `ProgressWindow.axaml` 通道行模板改为**左右分组**结构（外层 Grid 见任务 6）。本任务先落**左区**（原单格 `:383-404` 整段替换）：目录列（中间省略，根文件隐藏）+ 文件名列（恒完整，底纹随字节进度）+ 文件大小。

  ```xml
  <!-- 外层通道行：左区（*，弹性）| 右区（Auto + MinWidth，见任务 6） -->
  <Grid ColumnDefinitions="*,Auto" ColumnSpacing="{DynamicResource SpacingXs}"
        MinHeight="{DynamicResource ControlHeightSm}">
    <!-- 左区：单文件域（目录 | 文件名+底纹 | 文件大小） -->
    <Grid Grid.Column="0" ColumnDefinitions="Auto,*,Auto" ColumnSpacing="{DynamicResource SpacingXs}">
      <!-- 目录列（中间省略；根文件 DirectoryText="" → 隐藏） -->
      <TextBlock Text="{Binding DirectoryText}"
                 IsVisible="{Binding DirectoryText, Converter={StaticResource StringNotEmpty}}"
                 FontSize="11" VerticalAlignment="Center"
                 Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
      <!-- 文件名列（恒完整，底纹随字节进度；底纹只覆盖本格） -->
      <Grid Grid.Column="1" ClipToBounds="True">
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
                   FontSize="11" VerticalAlignment="Center"
                   Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
      </Grid>
      <!-- 文件大小（左区右端；空=隐藏，规则 6） -->
      <TextBlock Grid.Column="2" Text="{Binding FileSizeText}"
                 IsVisible="{Binding FileSizeText, Converter={StaticResource StringNotEmpty}}"
                 FontSize="10" VerticalAlignment="Center"
                 Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
    </Grid>
    <!-- 右区：批次域 → 任务 6 -->
  </Grid>
  ```

  底纹宽度基准取文件名格内层 Grid（`AncestorType=Grid`，任务 1 已修）；`ClipToBounds` 防溢出；间距用 `{DynamicResource Spacing*}`（规则 5）。

  (5e) 验证：build 0 错误 + `ProgressDisplayCalculatorTests` 新 3 条绿。

- [ ] 6. 右区（批次底纹化 + 合并百分比 + 信息列 + ToolTip）+ 单行隐藏规则（`ProgressWindow.axaml` + 主题 + i18n）

  **What to do**：

  (6a) 主题画刷（规则 4，成对）：`ThemeLight.axaml`/`ThemeDark.axaml` 新增批次底纹画刷 `ThemeProgressBatchBrush`（紫系，与左区文件底纹 `ProgressBarSizeBrush` 蓝系色相区分）；按两主题各给协调色值。

  (6b) i18n（规则 13，三语成对）：`strings.zh-CN.json`/`strings.en.json`/`strings.zh-TW.json` 新增 ToolTip 3 key：`Progress_Tooltip_Progress`/`Progress_Tooltip_Bytes`/`Progress_Tooltip_Ratio`。插入文件头 `{` 之后（key 不排序），UTF-8 无 BOM + CRLF。

  (6c) 右区结构（在任务 5 的外层 Grid `Grid.Column="1"`，取代旧「批进度条 + 百分比 + 批明细」三列）：

  - 右区容器 `MinWidth`（约 180px，D10）。
  - 背景：批次底纹 `Rectangle`（宽 = `BatchRatio` × 右区宽，色 `ThemeProgressBatchBrush`，经 `RatioToWidthConverter`，容器 `AncestorType=Grid`+`ClipToBounds`），置于文字层之下。
  - 内容：批次序号 + 合并百分比明细 + 信息列；`ToolTip.Tip` 挂右区。

  ```xml
  <!-- 右区：批次域（批次底纹 + 序号 + 合并百分比 + 信息列 + ToolTip） -->
  <Grid Grid.Column="1" MinWidth="180" ClipToBounds="True" Margin="{DynamicResource SpacingXsThk}"
        ToolTip.Tip="{Binding TooltipText}">
    <!-- 批次底纹：宽 = BatchRatio × 本格宽（紫系，区别于左区文件底纹） -->
    <Rectangle Fill="{DynamicResource ThemeProgressBatchBrush}"
               HorizontalAlignment="Left"
               IsVisible="{Binding CurrentFile, Converter={StaticResource StringNotEmpty}}">
      <Rectangle.Width>
        <MultiBinding Converter="{StaticResource RatioToWidthConverter}">
          <Binding Path="BatchRatio" />
          <Binding Path="Bounds.Width" RelativeSource="{RelativeSource AncestorType=Grid}" />
        </MultiBinding>
      </Rectangle.Width>
    </Rectangle>
    <!-- 文字层：序号 | 合并百分比 | 信息列（上叠于底纹） -->
    <StackPanel Orientation="Horizontal" Spacing="{DynamicResource SpacingXs}"
                HorizontalAlignment="Right" VerticalAlignment="Center">
      <!-- 批次序号（非并行单行隐藏） -->
      <TextBlock Text="{Binding Index}" IsVisible="{Binding IsParallel}"
                 FontSize="11" VerticalAlignment="Center"
                 Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
      <!-- 合并百分比 + 明细：45% (12/40)；单行仅 45% -->
      <TextBlock Text="{Binding PctDetailText}"
                 FontSize="11" VerticalAlignment="Center"
                 Foreground="{DynamicResource ThemeStatusSuccessBrush}" />
      <!-- 信息列：字节进度 · 压缩率（空=隐藏，规则 6） -->
      <TextBlock Text="{Binding InfoText}"
                 IsVisible="{Binding InfoText, Converter={StaticResource StringNotEmpty}}"
                 FontSize="10" VerticalAlignment="Center"
                 Foreground="{DynamicResource ThemeTextSecondaryBrush}"
                 TextTrimming="CharacterEllipsis" />
    </StackPanel>
  </Grid>
  ```

  （`TooltipText` 为 `\n` 分隔多行字符串，Avalonia 默认 `TextBlock` 保留换行。）

  (6d) 单行隐藏规则（`IsParallel` 正向绑，无需反转转换器）：

  - 批次序号：`IsVisible="{Binding IsParallel}"`（已含于 6c）。
  - **删除**旧独立「批级进度条 `ProgressBar`」列（约 `:407`）——已被批次底纹取代。
  - **删除**旧独立「百分比」列（约 `:416`）与「批明细」列（约 `:425`）——已合并入 `PctDetailText`。
  - 压缩通道说明行：VM 派生 `ShowCompressChannelHint => IsCompressFlow && HasParallelChannel`（D8，通知挂 `NotifyDisplayProperties`），XAML 绑 `ShowCompressChannelHint` 取代原 `IsCompressFlow`。

  (6e) 验证：build 0 错误；人工 GUI 验收并入任务 7。

- [ ] 7. 构建 + 双测试套 + GUI 五场景验收

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
  ```

  GUI 五场景：① 并行 ZIP 解压（多通道；左区文件底纹 + 目录/文件名/大小，右区批次底纹 + 序号 + 合并百分比，无压缩率，ToolTip 正常）/ ② 非并行 ZIP 压缩（单行、无批次序号、右区百分比 = 整体进度、信息列含压缩率）/ ③ 7z 压缩（信息列无压缩率段，ToolTip 无压缩率行）/ ④ 单条解压（左区底纹与当前文件字节比例一致、非整行宽）/ ⑤ 分卷 ZIP 压缩（压缩率隐藏）。

- [ ] 8. 进度文档同步与提交（仅用户明确要求提交时）

  按规则 3 更新 `docs/PROGRESS.md`（里程碑）+ `docs/progress-avalonia-detail.md`（细节），再按规则 10 conventional commits 提交。

---

## 附：关键行号索引（stats-cards 已于 commit `6fc2b01` 落地，下列行号已漂移，**实施前须重新 grep 定位**）

| 符号 | 文件 | 行（落笔时，待校准） |
|------|------|-----|
| `ArchiveProgress` 字段区 | Core/Abstractions/ArchiveEngine.cs | :318-354（+新字段） |
| `SetProgress` 主体 | UI.Avalonia/ViewModels/ProgressViewModel.cs | :516 |
| EntryStatus 早返回 | 同上 | :529 |
| `UpsertParallelBatch` | 同上 | :690-714 |
| 自动切详细守卫 | 同上 | :712 |
| 详细门禁回落（删除） | 同上 | :934-936 |
| `IsDetailedAvailable`（删除） | 同上 | :230-231 |
| 通知点（删除） | 同上 | :143 / :316 |
| 模式单选门禁绑定（删除） | UI.Avalonia/Dialogs/ProgressWindow.axaml | :288（注释 :270/:285） |
| 批次行模板 | 同上 | :361-435 |
| 底纹 Rectangle bug（修复） | 同上 | :386-396 |
| `ParallelBatchProgressItem` 字段区 | UI.Avalonia/Models/ParallelBatchProgressItem.cs | 全文件（约 30 行） |
| 主题画刷（新增批次底纹） | UI.Avalonia/Themes/ThemeLight.axaml / ThemeDark.axaml | 待定 |
| 串行 ZipWriter 报告 | Core/Engines/ZipEngine.cs | :1748-1761 |
| N 组 adapter ×2 | 同上 | :1516-1541 / :2459-2483 |
| `CompressGroupWithSevenZip` | 同上 | :3481 |
| `SplitOutputStream.Length` 抛异常 | Core/Utils/SplitOutputStream.cs | :91 |
