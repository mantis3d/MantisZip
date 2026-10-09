# 进度窗口统计卡三行结构（数量 + 大小）

> **状态**: 📋 设计已确认 + 任务分解已生成（2026-10-08，2026-10-09 按用户拍板修订 D2/并行后置/方案 A 去分母），待实施
>
> **For agentic workers:** 本文档是设计规格 + 完整任务分解（brainstorming 四段设计已获用户逐段确认；2026-10-09 用户确认三处修订：卡 2/3/4 行 3 动态累加（前向兼容）+ 并行度移出表格置后 + 已处理行 2 去分母改纯计数（方案 A））。文末「任务分解」含 7 任务 3 波次 checkbox 步骤。执行时使用 superpowers:subagent-driven-development 或 superpowers:executing-plans。

## TL;DR

> **Quick Summary**: 「完整」密度档的 5 张**文件**统计卡从「图标 / 标签 / 数值」竖排，改为**三行表格语义**：行 1 = 图标 + 标题（横排）、行 2 = 文件数量、行 3 = 文件大小；缺数据的行显示灰色 `—`（不隐藏单行）。**已处理卡行 2 为纯计数（方案 A 去分母）**——原「N/M」分数的分母与总大小卡行 2 的文件总数语义重复，且完成比例已由总进度条承担。**「并行」不属于文件统计，移出表格、置于统计卡表格之后**（独立单行元素）。跳过/出错/已覆盖卡行 3 为**动态累加值**（终态事件逐条累加字节，前向兼容 channel-info 的 `FileTotalBytes`），无法得知字节时显 `—`。
>
> **Deliverables**:
> - `Dialogs/ProgressWindow.axaml` 统计卡区（:573-703）5 卡三行重构 + 并行度后置单行元素
> - `ViewModels/ProgressViewModel.cs` 新增 `StatsProcessedSize`/`StatsTotalCount`/`StatsSkippedSize`/`StatsFailedSize`/`StatsOverwrittenSize` 五个只写不清的 `[ObservableProperty]` + 终态字节累加器 + `UpdateEntryStatus` 增加可选 `fileSize` 参数
> - 新增 `Converters/DashBrushConverter.cs`（`—`/空 → 灰，真实值 → 强调色；动态格从 2 处增至 5 处）
> - `ProgressViewModelTests.cs` 新增 7 条单测
> - `docs/PLAN.md` 登记行更新（规则 1）
>
> **Estimated Effort**: Small+（3-4h）
> **Parallel Execution**: NO — 单窗口 + 单 VM，顺序执行
> **Critical Path**: 用户审阅本设计 → writing-plans 任务分解 → XAML/VM/转换器 → 测试 → GUI 验收

---

## Context

### Original Request

用户原话（2026-10-08）：「下面切换到"完整"那里，我希望每个信息都显示两行，分别表示文件数量和大小，也就是说，应该是个三行的表格，第一行是图标和标题，第二行表示文件数量，第三行表示文件大小。」

用户修订（2026-10-09）：「还是按你的来，不过还有一点，把"并行"从那个表格里面拿出来，他不应该跟文件列表放到同一个表格里。我觉得可以放到表格的后面。」（此前同轮已确认：卡 2/3/4 行 3 从硬编码 `—` 改为动态累加、前向兼容；本计划先于 channel-info 实施。）

用户修订二（2026-10-09）：「A吧」——确认**方案 A**：已处理卡行 2 去掉「/总计」分母改纯计数（原「60/100」的分母与总大小卡行 2 的文件总数是同一数字，并排重复；方案 B 合并两卡会复活已否决的「已用/总量」复合值，被排除）。同轮核查了解压条目状态完备性：**结论是 4 终态契约完备，统计卡三行无需新增状态**；「重命名」的可见性缺口（Rename 上报 Completed 无痕迹）记入下方边界备忘，如需补 `ArchiveEntryStatus.Renamed` 另立计划（Core 契约变更，不在本计划范围）。

### Research Findings

- **现状结构**：`ProgressWindow.axaml:573-703` 6 张统计卡（已处理/跳过/出错/已覆盖/并行度/总大小），每卡为竖排 StackPanel：图标（FontSize 18）+ 标签（11, secondary）+ 数值（16, SemiBold）；卡容器 `StatsCardsPanel` 仅 `IsFullDensity` 显示（规则 6）
- **数据可用性**（决定行 3 能否有真实值）：
  - `ArchiveProgress.TotalBytes/ProcessedBytes` **仅 ZipEngine 上报**；7z/TAR 不上报 → `SetProgress` 的 `TotalBytes > 0` 块不进入
  - `TotalFiles/ProcessedFiles`：ZIP 上报；已处理卡可见性依赖 `StatsProcessedText` 非空
  - 总大小卡可见性依赖 `StatsTotalSizeValue` 非空
  - **跳过/出错/已覆盖字节**：`ArchiveProgress` 无现成字段，但终态事件携带 `EntryKey`，UI 层可经 `_entryIndex[key].Size`（`SeedEntryItems` 播种行自带 Size）逐条累加——**用户确认此路径可行**（完整列表在 UI 已有）
  - **累积语义**：「跳过/出错/已覆盖的大小」= 压缩包内条目**原始（未压缩）尺寸**之和，即「本来要写多少字节」
- **VM 现状**：`StatsProcessedCount` 现派生分数（`_statsTotalFiles > 0 ? $"{_statsProcessed}/{_statsTotalFiles}" : _statsProcessed.ToString()`，经 `NotifyStatsProperties` 集中通知）——**方案 A 将其简化为纯计数 `_statsProcessed.ToString()`**（分母冗余：与总大小卡行 2 `StatsTotalCount` 同源同值；比例已由总进度条承担）；`_statsTotalSizeText/_statsTotalSizeValue`；写入点为 `SetProgress` 的 `TotalFiles > 0` 块与 `TotalBytes > 0` 块；条目终态入口为 `SetProgress` 的 `EntryStatus` 块（:529-532，调用 `UpdateEntryStatus`）；`SeedEntryItems`(:724) 播种行含 Size，`ClearEntryItems`(:744) 清集合与索引
- **条目终态完备性核查（2026-10-09，方案 A 同轮）**：`ArchiveEntryStatus` 4 终态（Completed/Skipped/Failed/Overwritten）对解压全流程**完备**——每个成功上报终态的条目要么 Completed、要么落入统计卡三行之一，无第五种；冲突策略 6 种（Overwrite/Rename/Skip/Ask/OverwriteIfOlder/OverwriteIfSmaller）中 Ask 只是中间态（最终映射到其余策略），条件覆盖未命中归 Skipped。**不进条目终态的情形**（均不影响统计卡）：目录条目（`IsDirectory` 直接建目录 continue）、取消/暂停（操作级中止，剩余条目停 Pending/Active）。**唯一语义缺口：重命名**——`Rename` 策略下 `resolvedPath != outputPath`，引擎上报 `Completed`（不计 Overwritten），行内列表与统计卡均无「重命名」痕迹；补 `ArchiveEntryStatus.Renamed` 属 Core 契约变更（三引擎上报点 + `MapEntryStatus` 穷举映射 + 行图标/三语文案），另立计划，本计划不实施
- **前向兼容目标**：`progress-window-channel-info` 计划将给 `ArchiveProgress` 增加 `FileTotalBytes`（当前条目总大小）。本计划累加器**事件优先**：`FileTotalBytes` 非空时用之，否则回退播种行 `Size`——channel-info 落地后本设计零改动即获得渐进模式（TAR/GZ、>5000 条目未播种）下的完整累加覆盖
- **窗口特性**：`x:CompileBindings="False"`；转换器在 `Window.Resources`（:14-27）注册，新转换器按同模式注册

---

## 已确认设计决策（2026-10-08 用户逐段确认；2026-10-09 修订标注）

### D1 范围与缺数据表现（2026-10-09 修订）

- **5 张文件统计卡统一三行**：已处理 / 跳过 / 出错 / 已覆盖 / 总大小（用户选择「全部卡统一三行，缺数据显示 —」，否决「仅部分卡三行」）
- **并行度卡移出表格**：并行度不是文件统计（计数是线程数、无文件大小），不与文件列表共用表格；**置于统计卡表格之后**，独立单行元素（非三行卡）
- 行级缺数据 → 显示 `—`（`ThemeTextSecondaryBrush` 灰）；**不**隐藏单行
- **卡级可见性完全不动**（规则 6）：
  - 卡 1–4：对应 `Stats*Text` 非空
  - 卡 5（总大小）：`StatsTotalSizeValue` 非空
  - 并行度后置元素：`HasParallelDegree`（degree < 2 串行时隐藏，同现状）

### D2 行值映射表（2026-10-09 修订：卡 2/3/4 行 3 动态化 + 并行后置）

**统计卡表格（5 卡三行）**：

| 卡 | 行 1：图标 + 标题 | 行 2：文件数量 | 行 3：文件大小 |
|---|---|---|---|
| ✅ 已处理 | `✅` + `StatsProcessedLabel` | `StatsProcessedCount` = `_statsProcessed.ToString()` **纯计数**（2026-10-09 方案 A 去分母：原「60/100」分数的分母与卡 5 行 2 `StatsTotalCount` 同源重复；中等档一句话文案 `StatsProcessedText` 仍保留「已处理 60/100」不受影响） | **新字段** `StatsProcessedSize` = `FormatUtil.FormatSize(ProcessedBytes)`（仅 `TotalBytes > 0` 时写入，否则 `—`） |
| ⏭ 跳过 | `⏭` + `StatsSkippedLabel` | `StatsSkippedCount` | **新字段** `StatsSkippedSize` = 终态字节累加器（`FileTotalBytes` 事件优先 ↘ 播种行 `Size` 回退；从未累加到字节时 `—`） |
| ❌ 出错 | `❌` + `StatsFailedLabel` | `StatsFailedCount` | **新字段** `StatsFailedSize`（同上累加规则） |
| 🔄 已覆盖 | `🔄` + `StatsOverwrittenLabel` | `StatsOverwrittenCount` | **新字段** `StatsOverwrittenSize`（同上累加规则） |
| 📏 总大小 | `📏` + `StatsTotalSizeLabel` | **新字段** `StatsTotalCount` = `_statsTotalFiles.ToString()`（仅 `TotalFiles > 0` 时写入，否则 `—`） | `StatsTotalSizeValue` = `FormatSize(TotalBytes)`（卡可见即必有值） |

**表格后置元素（非卡、非三行）**：

| 元素 | 内容 | 显隐 |
|---|---|---|
| ⚙ 并行 | 单行横排：`⚙` + `LocalizedStrings[Progress_Stats_Parallel]` + `ParallelDegree`（线程数） | `HasParallelDegree`（同现状卡 5） |

> 注 1（用户决策）：已处理行 3 用**单值**（当前已处理字节数），不显示「已用/总量」复合值。
> 注 2（用户决策 2026-10-09）：跳过/出错/已覆盖行 3 为**动态累加值**——字节来源优先级 = 事件 `FileTotalBytes`（channel-info 落地后）↘ 否则 `_entryIndex[key].Size`（播种行）。累加为 0 或完全未知时显诚实 `—`（不显 `0 B`）。
> 注 3（用户决策 2026-10-09）：并行度不属于文件列表统计，从表格移出，放表格后面（单行元素）。
> 注 4（用户决策 2026-10-09 方案 A）：已处理行 2 用**纯计数**（如「60」），不显示「N/M」分数——分母与总大小卡行 2 的文件总数是同一数字，并排即重复；完成比例已由总进度条百分比承担，分数不损失洞察。方案 B（合并已处理与总大小为一张卡）因必然复活注 1 已否决的复合值形态或丢失字节信息，被排除。

**累加器防御规则**（D4 详述）：

- 同一条目行**重复上报同一终态只计一次**（累加前检查 `row.State`）
- `ClearEntryItems` / `SeedEntryItems` 时累加器**清零**（新批次/新操作语义）
- 未播种路径（渐进 upsert 行 Size = 0 且无 `FileTotalBytes`）→ 该条贡献 0 字节；若整个类别从未累加到任何字节 → 显 `—`

### D3 结构与样式（方案 A：逐卡 XAML 重构；方案 B 模板化已否决；2026-10-09 修订并行后置 + 动态格 5 处）

**统计卡表格**：外层仍为 `StatsCardsPanel` Border（SurfaceBg 底、圆角、内边距），内部改**竖排两段**：

1. **段 1 —— 卡片行**：现有横向 `StackPanel`（`Spacing="{DynamicResource SpacingLg}"`，居中，不换行）内放 5 张三行卡
2. **段 2 —— 并行度后置元素**：卡片行下方新增单行横排元素（`Spacing="{DynamicResource SpacingMd}"` 段间距，居中）：
   - `⚙` 图标 FontSize **14**（比卡内 16 略小，弱化为附属信息）+ `ThemeProgressFillBrush`
   - 标题 `LocalizedStrings[Progress_Stats_Parallel]` FontSize **11** + `ThemeTextSecondaryBrush`
   - 数值 `ParallelDegree` FontSize **13** SemiBold + `ThemeProgressFillBrush`
   - `IsVisible="{Binding HasParallelDegree}"`（同现状卡 5 绑定）

每卡内层 StackPanel（`Spacing="{DynamicResource SpacingXxs}"` 保持）三行：

1. **行 1（横排）**：`StackPanel Orientation="Horizontal"`，水平居中、`Spacing="{DynamicResource SpacingXxs}"`：
   - 图标 `TextBlock` FontSize **16**（原 18 收窄，横排占宽）
   - 标题 `TextBlock` FontSize **11**、`ThemeTextSecondaryBrush`
2. **行 2（数量）**：FontSize **15**、`FontWeight="SemiBold"`，颜色 = 该卡**原数值色**（✅→`ThemeStatusSuccessBrush`、⏭→`ThemeStatusWarningBrush`、❌→`ThemeStatusErrorBrush`、🔄→`ThemeTextPrimaryBrush`、📏→`ThemeTextPrimaryBrush`）
3. **行 3（大小）**：FontSize **13**：
   - 动态值 → 新转换器 `DashBrushConverter`：值为 `—`/null/空 → `ThemeTextSecondaryBrush`，真实值 → `ConverterParameter` 指定的强调色资源键（解析失败回退 secondary）
   - **动态 `—` 单元格全场景共 5 处**（2026-10-09 从 2 处扩充）：卡 1 行 3（`StatsProcessedSize`）、卡 2 行 3（`StatsSkippedSize`）、卡 3 行 3（`StatsFailedSize`）、卡 4 行 3（`StatsOverwrittenSize`）、卡 5（总大小）行 2（`StatsTotalCount`）

### D4 新 VM 字段与累加器（`ProgressViewModel.cs`；2026-10-09 修订）

| 字段 | 类型/默认 | 写入点（只写不清，与既有统计字段同模式） |
|---|---|---|
| `StatsProcessedSize` | `string`，默认 `"—"` | `SetProgress` 既有 `TotalBytes > 0` 块内追加 `StatsProcessedSize = FormatUtil.FormatSize(p.ProcessedBytes);` |
| `StatsTotalCount` | `string`，默认 `"—"` | `SetProgress` 既有 `TotalFiles > 0` 块内追加 `StatsTotalCount = _statsTotalFiles.ToString();` |
| `StatsSkippedSize` | `string`，默认 `"—"` | 终态累加器（见下） |
| `StatsFailedSize` | `string`，默认 `"—"` | 终态累加器（见下） |
| `StatsOverwrittenSize` | `string`，默认 `"—"` | 终态累加器（见下） |

- 均为 `[ObservableProperty]`（字段名 `_statsXxx`），绑定自刷新；**不**加入 `NotifyStatsProperties`（该方法只管派生属性，本设计未新增派生属性）
- `ProcessedBytes = 0` 但 `TotalBytes > 0` 的初始阶段行 3 显示 `0 B`（真实实时值，非缺陷）
- **既有派生属性简化（方案 A）**：`StatsProcessedCount`（:345-347）从三元分数派生改为 `_statsProcessed.ToString()` 纯计数——`NotifyStatsProperties` 对它的通知保持不动（`_statsProcessed` 变化仍需刷新）；中等档 `StatsProcessedText`（一句话文案「已处理 60/100」，:634 写入）**保留分数格式不变**

**终态字节累加器**（2026-10-09 新增）：

```csharp
// 私有累加计数（不绑定，仅驱动三个 Size 字符串）
private long _statsSkippedBytes;
private long _statsFailedBytes;
private long _statsOverwrittenBytes;
```

- **入口**：`UpdateEntryStatus` 增加可选参数 `long? fileSize = null`——签名改为
  `public void UpdateEntryStatus(string entryKey, EntryRowState state, double? percent, long? fileSize = null)`
- **调用点**：`SetProgress` 的 `EntryStatus` 块（:529-532）改为 `UpdateEntryStatus(key, mapped, null, p.FileTotalBytes)`——`FileTotalBytes` 为 channel-info 新增字段（前向兼容：本计划实施时该字段尚不存在，先传 `null`；channel-info 落地后仅需改此处一行传值，累加器自动升级为事件优先）
- **字节来源**：`fileSize` 非空且 `> 0` → 用事件值；否则回退 `_entryIndex.TryGetValue(entryKey, out var row) ? row.Size : 0`（播种行 Size；渐进 upsert 行为 0 = 未知，贡献 0）
- **去重守卫**：累加前检查该行**旧状态**——仅当 `row.State` **不是**目标终态时累加（同一行重复上报同一终态只计一次）；行状态变更仍照常执行
- **类别分发**：`Skipped` → `_statsSkippedBytes += size; StatsSkippedSize = _statsSkippedBytes > 0 ? FormatUtil.FormatSize(_statsSkippedBytes) : "—";`（Failed/Overwritten 同构）
- **清零**：`ClearEntryItems` 内三个累加器归零 + 三个 Size 字符串复位 `"—"`（`SeedEntryItems` 已先调 `ClearEntryItems`，自动覆盖）

### D5 可见性规则（规则 6 维持；2026-10-09 并行后置）

- **卡级**：5 张卡见 D1，一律沿用现状绑定；并行度后置元素沿用 `HasParallelDegree`
- **行级**：永不隐藏，缺数据显 `—`
- 中等/精简两档（`MediumStatsPanel`、简约面板）**零改动**

### D6 测试计划（`tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 新增 7 条；2026-10-09 从 3 条扩充）

1. `SetProgress_WithTotalBytes_PopulatesProcessedSize` — `TotalBytes > 0` 且 `ProcessedBytes = X` → `StatsProcessedSize == FormatUtil.FormatSize(X)`
2. `SetProgress_WithoutTotalBytes_ProcessedSizeStaysDash` — 不报字节 → `StatsProcessedSize == "—"`（默认值保持）
3. `SetProgress_WithTotalFiles_PopulatesTotalCount` — 初始断言 `"—"` → `TotalFiles > 0` 后等于总数字符串
4. `UpdateEntryStatus_SkippedWithSeededSize_AccumulatesSkippedSize` — 播种 Size 后上报 Skipped 终态 → `StatsSkippedSize == FormatSize(该行 Size)`
5. `UpdateEntryStatus_FailedWithSeededSize_AccumulatesFailedSize` — Failed 终态 → `StatsFailedSize` 累加正确
6. `UpdateEntryStatus_SameTerminalStateReportedTwice_CountsOnce` — 同一行重复上报 Skipped → 字节只计一次
7. `ClearEntryItems_ResetsAccumulatedSizes` — 累加后 `ClearEntryItems` → 三个 Size 复位 `"—"`

- **既有测试修改 1 条（方案 A，有意行为变更，非回归）**：`StatsProcessedCount_ShowsFractionString`（:534）断言 `"60/100"` → 改为断言 `"60"`（纯计数），方法名同步改 `StatsProcessedCount_ShowsPureCount`，注释说明方案 A 去分母决策；`SetProgress_WithTotalFiles_ShowsProcessedOverTotal`（:491，断言 `StatsProcessedText` 含分数）与 `SetProgress_ZeroTotalFiles_KeepsPreviousFraction`（:516）**不动**——它们锁的是中等档一句话文案，分数保留
- 既有测试须全绿（尤其 `SetProgress_WithoutTotalBytes_LeavesTotalSizeEmpty`——新字段与 `StatsTotalSizeValue` 互不干扰）

---

## Work Objectives

### Core Objective

「完整」档 5 张文件统计卡升级为三行表格语义（图标+标题 / 文件数量 / 文件大小），并行度移出表格置后为单行元素；缺数据以灰 `—` 占位；跳过/出错/已覆盖大小经终态字节累加器动态显示（前向兼容 `progress-window-channel-info` 的 `FileTotalBytes`——该计划完成后累加器在渐进模式下自动获得完整覆盖，本设计零改动）。`progress-window-bytes-i18n`（7z/TAR 字节埋点）完成后卡 1 行 3 与卡 5（总大小）同样自动填充。

### Concrete Deliverables

- `ProgressWindow.axaml`：5 卡三行重构 + 并行度后置单行 + `DashBrushConverter` 资源注册 + 中文注释（规则 14）
- `Converters/DashBrushConverter.cs`：`—`/null/空 → `ThemeTextSecondaryBrush`，否则按 `ConverterParameter` 资源键取强调色画刷
- `ProgressViewModel.cs`：`_statsProcessedSize`、`_statsTotalCount`、`_statsSkippedSize`、`_statsFailedSize`、`_statsOverwrittenSize` 五个 `[ObservableProperty]` + 三个私有累加计数 + `UpdateEntryStatus` 可选 `fileSize` 参数 + `ClearEntryItems` 清零
- `ProgressViewModelTests.cs`：D6 的 7 条测试
- `docs/PLAN.md`：登记行更新（规则 1）

### Definition of Done

- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误 0 警告
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 既有测试全绿 + 新增 7 条通过（基线：5 个 `PreviewWebViewLazyInitTests` 预存失败不计入）
- [ ] `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全绿（本设计不改 Core，应零变化）
- [ ] GUI 手动验收：完整档 5 卡三行 + 并行度后置单行、`—` 灰显、真实值强调色、中/精简两档无回归
- [ ] `docs/PLAN.md` 已同步（规则 1）

### Must Have

- 5 张文件统计卡全部三行；行 2 数量、行 3 大小语义正确（严格按 D2 表）
- 已处理行 2 为**纯计数**（方案 A 去分母，`StatsProcessedCount` = `_statsProcessed.ToString()`）；中等档一句话文案保留 N/M 分数
- 并行度**不在**卡片行内，位于表格（卡片行）之后，单行横排，显隐仍绑 `HasParallelDegree`
- 跳过/出错/已覆盖行 3 为累加器动态值：有字节 → 强调色真实值；无字节 → 灰 `—`
- 累加器防御：同终态重复上报只计一次；`ClearEntryItems`/`SeedEntryItems` 清零
- 缺数据行显 `—` 且灰显（`ThemeTextSecondaryBrush`）
- 卡级显隐（规则 6）与中/精简两档零改动
- 新增 axaml 控件中文注释（规则 14）、间距全部 `{DynamicResource Spacing*}`（规则 5）

### Must NOT Have (Guardrails)

- **不得**改任何引擎（`ArchiveProgress` 字节上报归 `progress-window-bytes-i18n` / `progress-window-channel-info` 计划）
- **不得**改卡级可见性绑定；**不得**模板化重构统计卡（方案 B 已否决）
- **不得**新增本地化 key（`—` 是符号；卡片标题复用既有 `Stats*Label` / `Progress_Stats_Parallel`）
- **不得**动中等/精简两档布局（中等档 `StatsProcessedText` 的「已处理 N/M」分数文案保留）
- **不得**让并行度回到卡片行内
- **不得**为累加器引入线程同步——`UpdateEntryStatus` 全部经 UI 线程 dispatch（与既有条目行同路径），无跨线程累加
- **不得**为「重命名」新增条目终态或统计卡行——`ArchiveEntryStatus` 4 终态契约完备（2026-10-09 核查结论，见 Research Findings）；Rename 上报 `Completed` 是写入成功语义，行内/统计卡无「重命名」痕迹属已知边界；如需可见性补 `ArchiveEntryStatus.Renamed` 另立计划（Core 契约变更：三引擎上报点 + `MapEntryStatus` 穷举 + 行图标/三语文案），本计划范围外
- 版本号不变（规则 2）

---

## Verification Strategy (MANDATORY)

### Test Decision

- **Infrastructure exists**: YES（xunit.v3 + Avalonia.Headless.XUnit，`tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 已有 12+ 条 `SetProgress_*` 相邻测试）
- **Automated tests**: Unit（VM 属性写入行为 + 累加器，D6 7 条）
- **Framework**: xunit.v3，沿用现有测试文件与 `[Fact]` 模式

### AI-Driven QA

- 本任务为 Avalonia 桌面 UI，视觉验收（D3 三行布局、并行后置、`—` 灰显、强调色）走**人工 GUI 验收**（DoD 第 4 条），不做浏览器 QA

### 自动化验证命令

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```

---

## 依赖与后续

- **依赖**：无硬前置（5 卡结构已由 prototype-alignment 计划落地；本计划**先于** `progress-window-channel-info` 实施——两者都改 `ProgressViewModel.cs`，channel-info 届时校准行号）
- **前向兼容**：`progress-window-channel-info` 落地 `ArchiveProgress.FileTotalBytes` 后，仅需把 `SetProgress` 的 `EntryStatus` 块调用改为传 `p.FileTotalBytes`（本计划已预留 `long? fileSize` 参数），跳过/出错/已覆盖累加即在渐进模式（TAR/GZ、>5000 条目）下完整覆盖；**该计划的终态报告须携带 `FileTotalBytes`**（见其任务 3 备注）
- **后续受益**：`progress-window-bytes-i18n` 完成 7z/TAR 字节上报后，卡 1 行 3（已处理大小）与卡 5（总大小）**自动**由 `—` 变真实值，本设计零改动
- **文档同步**：本文件修订 → `docs/PLAN.md` P2 区登记行更新说明（规则 1）

## 任务分解

> **执行方式**: superpowers:subagent-driven-development（推荐，每任务独立子代理）或 superpowers:executing-plans（本会话批量执行），checkbox 逐步勾选。
> **提交策略**: 任务 1–6 默认**不 commit**（未经用户明确要求不提交）；仅当用户要求提交时执行任务 7，且按规则 3 先更新进度文档。

### 执行波次

```
Wave 1（VM 层，顺序 — 同文件）
├── 任务 1: StatsProcessedSize 字段（TDD）
├── 任务 2: StatsTotalCount 字段（TDD，Blocked By: 任务 1）
└── 任务 3: 终态字节累加器 + 3 个 Size 字段（TDD，Blocked By: 任务 2）

Wave 2（UI 层）
├── 任务 4: DashBrushConverter（可与 Wave 1 并行，无文件交集）
└── 任务 5: ProgressWindow.axaml 5 卡三行 + 并行后置（Blocked By: 1–4）

Wave FINAL
├── 任务 6: 构建 + 双测试套 + GUI 人工验收（Blocked By: 1–5）
└── 任务 7: 进度文档同步与提交（仅用户要求时；Blocked By: 6）
```

---

- [ ] 1. 「已处理」卡行 3 —— `StatsProcessedSize` 字段（TDD）

  **What to do**:

  (1a) 写失败测试 —— `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 顶部 usings 在 `using MantisZip.Core.Models;` 之后加一行：

  ```csharp
  using MantisZip.Core.Utils;
  ```

  (1b) 同文件末尾 `}` 之前追加（2 条测试，编译级红：`StatsProcessedSize` 尚不存在）：

  ```csharp
      // ════════════════════════════════════════════
      //  统计卡三行结构（progress-stats-cards-three-row）：行 3 文件大小
      // ════════════════════════════════════════════

      /// <summary>
      /// 引擎上报 TotalBytes 时，已处理卡行 3 必须填充文件大小（编译级红：
      /// StatsProcessedSize 为新增成员）。行值映射见设计 D2。
      /// </summary>
      [Fact]
      public void SetProgress_WithTotalBytes_PopulatesProcessedSize()
      {
          var vm = new ProgressViewModel();
          vm.SetProgress(new ArchiveProgress { TotalBytes = 1048576, ProcessedBytes = 524288 });

          Assert.Equal(FormatUtil.FormatSize(524288), vm.StatsProcessedSize);
      }

      /// <summary>
      /// 引擎未上报 TotalBytes（7z/TAR）时，已处理卡行 3 保持默认 — 占位
      /// （行级永不隐藏，缺数据显 —，设计 D1）。
      /// </summary>
      [Fact]
      public void SetProgress_WithoutTotalBytes_ProcessedSizeStaysDash()
      {
          var vm = new ProgressViewModel();
          vm.SetProgress(new ArchiveProgress { TotalFiles = 10, ProcessedFiles = 1 });

          Assert.Equal("—", vm.StatsProcessedSize);
      }
  ```

  (1c) 运行确认编译级红：

  ```powershell
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests"
  ```

  预期：编译失败 `CS1061: 'ProgressViewModel' does not contain a definition for 'StatsProcessedSize'`（与既有「波 B 编译级红」同模式，属预期红）。

  (1d) 新增字段 —— `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs` 在 `_statsTotalSizeValue` 字段声明之后追加：

  ```csharp
      /// <summary>统计卡行 3（已处理）：文件大小（引擎上报 TotalBytes 后随进度更新；默认 "—" 行级占位，D2/D4）。</summary>
      [ObservableProperty]
      private string _statsProcessedSize = "—";
  ```

  (1e) 写入点 —— `SetProgress` 的 `if (p.TotalBytes > 0)` 块改为：

  ```csharp
          if (p.TotalBytes > 0)
          {
              var sizeText = FormatUtil.FormatSize(p.TotalBytes);
              StatsTotalSizeValue = sizeText;
              StatsTotalSizeText = LocalizationManager.T("Progress_Stats_TotalSize", sizeText);
              StatsProcessedSize = FormatUtil.FormatSize(p.ProcessedBytes);
          }
  ```

  (1f) 重跑 (1c) 同命令，预期：**全部 Passed**（含既有 12+ 条 `SetProgress_*`）。

  **Must NOT do**:
  - 不加入 `NotifyStatsProperties`（`[ObservableProperty]` 自刷新，D4）
  - 不改 `StatsTotalSizeValue` / `StatsTotalSizeText` 既有语义
  - 不改任何引擎

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO（与任务 2/3 改同一测试文件与 VM 文件）
  - **Blocks**: 任务 5、任务 6
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] 2 条新测试存在且通过；既有测试无回归
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误

  **QA Scenarios**:

  ```
  Scenario: 编译级红 → 绿
    Tool: Bash (PowerShell)
    Steps: dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests"
    Expected Result: 首跑报 CS1061（预期红）；实现后全部 Passed
    Evidence: .omo/evidence/task-1-vm-processed-size.txt
  ```

  **Commit**: NO — 未经用户明确要求不提交

---

- [ ] 2. 「总大小」卡行 2 —— `StatsTotalCount` 字段（TDD）

  **What to do**:

  (2a) 写失败测试 —— `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 末尾 `}` 之前（任务 1 测试之后）追加：

  ```csharp
      /// <summary>
      /// 引擎上报 TotalFiles 时，总大小卡行 2 必须填充文件总数；
      /// 初始默认 —（行级占位，D2/D4）。编译级红：StatsTotalCount 为新增成员。
      /// </summary>
      [Fact]
      public void SetProgress_WithTotalFiles_PopulatesTotalCount()
      {
          var vm = new ProgressViewModel();
          Assert.Equal("—", vm.StatsTotalCount);

          vm.SetProgress(new ArchiveProgress { TotalFiles = 100, ProcessedFiles = 60 });

          Assert.Equal("100", vm.StatsTotalCount);
      }
  ```

  (2b) 运行确认编译级红：

  ```powershell
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests"
  ```

  预期：编译失败 `CS1061: 'ProgressViewModel' does not contain a definition for 'StatsTotalCount'`。

  (2c) 新增字段 —— `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs` 在 `_statsProcessedSize`（任务 1 新增）之后追加：

  ```csharp
      /// <summary>统计卡行 2（总大小卡）：文件总数（引擎上报 TotalFiles 后填充；默认 "—" 行级占位，D2/D4）。</summary>
      [ObservableProperty]
      private string _statsTotalCount = "—";
  ```

  (2d) 写入点 —— `SetProgress` 的 `if (p.TotalFiles > 0)` 块改为：

  ```csharp
          if (p.TotalFiles > 0)
          {
              _statsProcessed = p.ProcessedFiles;
              _statsTotalFiles = p.TotalFiles;
              StatsProcessedText = LocalizationManager.T("Progress_Stats_Processed", $"{_statsProcessed}/{_statsTotalFiles}");
              StatsTotalCount = _statsTotalFiles.ToString();
          }
  ```

  （`StatsProcessedText` 一句话文案保留分数——中等档绑定用，方案 A 只改完整档行 2 派生。）

  (2e) 方案 A —— `StatsProcessedCount` 派生从分数简化为纯计数（:345-347 附近）：

  ```csharp
      /// <summary>统计卡行 2（已处理）：已处理文件数纯计数（方案 A 去分母——分母与总大小卡行 2 StatsTotalCount 同源重复，比例由总进度条承担；中等档 StatsProcessedText 保留 N/M 分数）。</summary>
      public string StatsProcessedCount => _statsProcessed.ToString();
  ```

  (2f) 既有测试修改 —— `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 的 `StatsProcessedCount_ShowsFractionString`（:534 附近）改为：

  ```csharp
      /// <summary>
      /// 统计卡计数槽显示已处理数纯计数（2026-10-09 方案 A 去分母：原 N/M 分数
      /// 的分母与总大小卡行 2 的文件总数同源重复，完成比例由总进度条承担）。
      /// 中等档 StatsProcessedText 仍为 N/M 分数，见 SetProgress_WithTotalFiles_ShowsProcessedOverTotal。
      /// </summary>
      [Fact]
      public void StatsProcessedCount_ShowsPureCount()
      {
          var vm = new ProgressViewModel();
          vm.SetProgress(new ArchiveProgress { TotalFiles = 100, ProcessedFiles = 60 });

          Assert.Equal("60", vm.StatsProcessedCount);
      }
  ```

  (2g) 重跑 (2b) 同命令，预期：**全部 Passed**（含任务 1 的 2 条与既有 `SetProgress_ZeroTotalFiles_KeepsPreviousFraction`——TotalFiles=0 不进块，不清零）。

  **Must NOT do**:
  - 不加入 `NotifyStatsProperties`（D4；`StatsProcessedCount` 既有通知保持不动）
  - 不改 `StatsProcessedText` 中等档一句话文案的分数格式（:634 写入点）
  - 不改任何引擎

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO（与任务 1 同文件，必须在其后）
  - **Blocks**: 任务 5、任务 6
  - **Blocked By**: 任务 1

  **Acceptance Criteria**:
  - [ ] 1 条新测试（`StatsTotalCount`）+ 1 条修改测试（`StatsProcessedCount_ShowsPureCount`）存在且通过；任务 1 及既有测试无回归
  - [ ] `StatsProcessedCount` 派生为 `_statsProcessed.ToString()` 纯计数
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误

  **QA Scenarios**:

  ```
  Scenario: 编译级红 → 绿
    Tool: Bash (PowerShell)
    Steps: dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests"
    Expected Result: 首跑报 CS1061（预期红）；实现后全部 Passed
    Evidence: .omo/evidence/task-2-vm-total-count.txt
  ```

  **Commit**: NO — 未经用户明确要求不提交

---

- [ ] 3. 终态字节累加器 —— `StatsSkippedSize`/`StatsFailedSize`/`StatsOverwrittenSize`（TDD；2026-10-09 新增任务）

  **What to do**:

  (3a) 写失败测试 —— `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 末尾 `}` 之前（任务 2 测试之后）追加（4 条，编译级红：`UpdateEntryStatus` 第 4 参数与三个 Size 属性尚不存在）：

  ```csharp
      /// <summary>
      /// 播种带 Size 的条目行后上报 Skipped 终态，跳过卡行 3 必须累加该行
      /// 原始（未压缩）尺寸（D2 注 2 / D4 累加器：播种行 Size 回退路径）。
      /// </summary>
      [Fact]
      public void UpdateEntryStatus_SkippedWithSeededSize_AccumulatesSkippedSize()
      {
          var vm = new ProgressViewModel();
          vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 1048576L), ("b.txt", "b.txt", 2097152L) });

          vm.UpdateEntryStatus("a.txt", EntryRowState.Skipped, null);
          Assert.Equal(FormatUtil.FormatSize(1048576), vm.StatsSkippedSize);

          vm.UpdateEntryStatus("b.txt", EntryRowState.Skipped, null);
          Assert.Equal(FormatUtil.FormatSize(1048576 + 2097152), vm.StatsSkippedSize);
      }

      /// <summary>
      /// Failed 终态累加到出错卡行 3（与 Skipped 通道互不干扰）。
      /// </summary>
      [Fact]
      public void UpdateEntryStatus_FailedWithSeededSize_AccumulatesFailedSize()
      {
          var vm = new ProgressViewModel();
          vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 4096L) });

          vm.UpdateEntryStatus("a.txt", EntryRowState.Failed, null);

          Assert.Equal(FormatUtil.FormatSize(4096), vm.StatsFailedSize);
          Assert.Equal("—", vm.StatsSkippedSize);
      }

      /// <summary>
      /// 同一条目行重复上报同一终态只计一次（D4 去重守卫：累加前检查旧状态）。
      /// </summary>
      [Fact]
      public void UpdateEntryStatus_SameTerminalStateReportedTwice_CountsOnce()
      {
          var vm = new ProgressViewModel();
          vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 1048576L) });

          vm.UpdateEntryStatus("a.txt", EntryRowState.Skipped, null);
          vm.UpdateEntryStatus("a.txt", EntryRowState.Skipped, null);

          Assert.Equal(FormatUtil.FormatSize(1048576), vm.StatsSkippedSize);
      }

      /// <summary>
      /// ClearEntryItems 清零累加器并复位三个 Size 字符串为 —
      /// （SeedEntryItems 内部先调 ClearEntryItems，新批次语义自动覆盖）。
      /// </summary>
      [Fact]
      public void ClearEntryItems_ResetsAccumulatedSizes()
      {
          var vm = new ProgressViewModel();
          vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 1048576L) });
          vm.UpdateEntryStatus("a.txt", EntryRowState.Overwritten, null);
          Assert.Equal(FormatUtil.FormatSize(1048576), vm.StatsOverwrittenSize);

          vm.ClearEntryItems();

          Assert.Equal("—", vm.StatsSkippedSize);
          Assert.Equal("—", vm.StatsFailedSize);
          Assert.Equal("—", vm.StatsOverwrittenSize);
      }
  ```

  (3b) 运行确认编译级红：

  ```powershell
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests"
  ```

  预期：编译失败（`StatsSkippedSize` / `fileSize` 参数不存在）。

  (3c) 新增三个 `[ObservableProperty]` —— 在 `_statsTotalCount`（任务 2 新增）之后追加：

  ```csharp
      /// <summary>统计卡行 3（跳过）：终态字节累加值（FileTotalBytes 事件优先 ↘ 播种行 Size 回退；从未累加到字节保持 "—"，D2 注 2/D4）。</summary>
      [ObservableProperty]
      private string _statsSkippedSize = "—";

      /// <summary>统计卡行 3（出错）：终态字节累加值（同 StatsSkippedSize 规则）。</summary>
      [ObservableProperty]
      private string _statsFailedSize = "—";

      /// <summary>统计卡行 3（已覆盖）：终态字节累加值（同 StatsSkippedSize 规则）。</summary>
      [ObservableProperty]
      private string _statsOverwrittenSize = "—";
  ```

  (3d) 私有累加计数 —— 在上述字段附近追加（不绑定，仅驱动字符串）：

  ```csharp
      // 终态字节累加器（D4）：驱动 StatsSkippedSize/StatsFailedSize/StatsOverwrittenSize；
      // 仅 UI 线程访问（SetProgress/UpdateEntryStatus 均经 dispatch），无需同步
      private long _statsSkippedBytes;
      private long _statsFailedBytes;
      private long _statsOverwrittenBytes;
  ```

  (3e) `UpdateEntryStatus` 签名扩展 + 去重累加 —— 现方法改为：

  ```csharp
      public void UpdateEntryStatus(string entryKey, EntryRowState state, double? percent, long? fileSize = null)
      {
          if (!_entryIndex.TryGetValue(entryKey, out var row))
          {
              row = new EntryProgressItem
              {
                  EntryKey = entryKey,
                  Name = System.IO.Path.GetFileName(entryKey),
                  State = EntryRowState.Pending,
              };
              _entryItems.Add(row);
              _entryIndex[entryKey] = row;
          }

          // 终态字节累加（D4）：同终态重复上报只计一次（检查旧状态）；
          // 字节来源 = fileSize 事件值（前向兼容 FileTotalBytes，非空且 >0）↘ 播种行 Size 回退
          if (state is EntryRowState.Skipped or EntryRowState.Failed or EntryRowState.Overwritten
              && row.State != state)
          {
              var size = fileSize is > 0 ? fileSize.Value : row.Size;
              if (size > 0)
                  AccumulateTerminalBytes(state, size);
          }

          row.State = state;
          // T9: 行内状态文案（本地化）；Active 返回 null 不赋值——视图层改显 PercentText
          string? statusText = ResolveEntryStatusText(state);
          if (statusText != null)
              row.StatusText = statusText;
          if (percent.HasValue)
              row.Percent = percent.Value;
      }

      /// <summary>按终态类别累加字节并刷新对应统计卡行 3（0 字节保持 "—"：未知不装真，D2 注 2）。</summary>
      private void AccumulateTerminalBytes(EntryRowState state, long size)
      {
          switch (state)
          {
              case EntryRowState.Skipped:
                  _statsSkippedBytes += size;
                  StatsSkippedSize = FormatUtil.FormatSize(_statsSkippedBytes);
                  break;
              case EntryRowState.Failed:
                  _statsFailedBytes += size;
                  StatsFailedSize = FormatUtil.FormatSize(_statsFailedBytes);
                  break;
              case EntryRowState.Overwritten:
                  _statsOverwrittenBytes += size;
                  StatsOverwrittenSize = FormatUtil.FormatSize(_statsOverwrittenBytes);
                  break;
          }
      }
  ```

  (3f) `SetProgress` 的 `EntryStatus` 块改调用（:529-532 附近）——`UpdateEntryStatus(p.EntryKey, ..., null)` 末参暂传 `null`（`FileTotalBytes` 属 channel-info 计划，届时改传 `p.FileTotalBytes` 即完成前向兼容接线）：

  ```csharp
          if (p.EntryStatus.HasValue && !string.IsNullOrEmpty(p.EntryKey))
          {
              // fileSize 末参预留前向兼容：channel-info 落地 FileTotalBytes 后改传 p.FileTotalBytes（D4）
              UpdateEntryStatus(p.EntryKey,
                  EntryProgressItem.MapEntryStatus(p.EntryStatus.Value), null, null);
          }
  ```

  (3g) `ClearEntryItems` 清零 —— 现方法改为：

  ```csharp
      /// <summary>清空条目行、key 索引与终态字节累加器（新批次/新操作前调用，三者必须同步清）。</summary>
      public void ClearEntryItems()
      {
          _entryItems.Clear();
          _entryIndex.Clear();
          _statsSkippedBytes = 0;
          _statsFailedBytes = 0;
          _statsOverwrittenBytes = 0;
          StatsSkippedSize = "—";
          StatsFailedSize = "—";
          StatsOverwrittenSize = "—";
      }
  ```

  (3h) 重跑 (3b) 同命令，预期：**全部 Passed**（含任务 1/2 与既有全部）。

  **Must NOT do**:
  - 不改引擎（`ArchiveProgress` 加字段属 channel-info 计划）
  - 不为累加器加锁/Dispatcher（`UpdateEntryStatus` 已在 UI 线程路径）
  - 不把累加入口改到 `EntryProgressItem` 内（保持 VM 集中统计，与 `Stats*Count` 同层）
  - 渐进行（未播种、无 fileSize）贡献 0 字节——**不得**伪造 Size 或用 0 显 `0 B`

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO（与任务 1/2 同文件，必须在其后）
  - **Blocks**: 任务 5、任务 6
  - **Blocked By**: 任务 2

  **Acceptance Criteria**:
  - [ ] 4 条新测试存在且通过；任务 1/2 及既有测试无回归
  - [ ] `UpdateEntryStatus` 签名含 `long? fileSize = null`；`ClearEntryItems` 清零累加器与三个字符串
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误

  **QA Scenarios**:

  ```
  Scenario: 编译级红 → 绿
    Tool: Bash (PowerShell)
    Steps: dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests"
    Expected Result: 首跑编译失败（预期红）；实现后全部 Passed
    Evidence: .omo/evidence/task-3-accumulators.txt
  ```

  **Commit**: NO — 未经用户明确要求不提交

---

- [ ] 4. `DashBrushConverter` 转换器 + ProgressWindow 资源注册

  **What to do**:

  (4a) 新建 `src/MantisZip.UI.Avalonia/Converters/DashBrushConverter.cs`（完整文件；资源键解析模式对齐既有 `BrushResourceConverter`）：

  ```csharp
  using Avalonia;
  using Avalonia.Data.Converters;
  using Avalonia.Media;
  using Avalonia.Styling;
  using System.Globalization;

  namespace MantisZip.UI.Avalonia.Converters;

  /// <summary>
  /// 统计卡行值画刷转换：值为 —/null/空 → ThemeTextSecondaryBrush（灰显占位），
  /// 真实值 → ConverterParameter 指定的强调色资源键（解析失败回退 secondary，
  /// secondary 也失败返回 null 由 Foreground 继承父控件）。
  /// 用于三行统计卡的动态 — 单元格（5 处：卡1/2/3/4 行 3 与总大小卡行 2，D3）。
  /// </summary>
  public class DashBrushConverter : IValueConverter
  {
      public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
      {
          var isDash = value is not string s || string.IsNullOrEmpty(s) || s == "—";
          var key = isDash || parameter is not string accentKey
              ? "ThemeTextSecondaryBrush"
              : accentKey;
          if (Application.Current?.Resources.TryGetResource(key, ThemeVariant.Default, out var resource) == true
              && resource is IBrush brush)
              return brush;
          // 回退 secondary（若仍失败返回 null，Foreground 继承父控件）
          if (!isDash
              && Application.Current?.Resources.TryGetResource("ThemeTextSecondaryBrush", ThemeVariant.Default, out var fallback) == true
              && fallback is IBrush secondary)
              return secondary;
          return null;
      }

      public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
      {
          throw new NotSupportedException();
      }
  }
  ```

  (4b) 注册到 `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` 的 `<Window.Resources>` 内（`RatioToWidthConverter` 之后、`</Window.Resources>` 之前）：

  ```xml
      <!-- 三行统计卡动态 — 画刷：—/空 → 灰 secondary，真实值 → ConverterParameter 强调色（D3） -->
      <converters:DashBrushConverter x:Key="DashBrush"/>
  ```

  (4c) 构建验证：

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  ```

  预期：Build succeeded，0 error 0 warning。

  **Must NOT do**:
  - 不改 `BrushResourceConverter` / 其他既有转换器
  - 不新增本地化 key（`—` 是符号）
  - 转换器不写单测（D6 固定 7 条 VM 测试；行为由任务 6 GUI 验收覆盖）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES（仅与 Wave 1 并行，无文件交集）
  - **Blocks**: 任务 5、任务 6
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] `DashBrushConverter.cs` 存在，Build succeeded
  - [ ] `ProgressWindow.axaml` 内 `x:Key="DashBrush"` 注册存在

  **QA Scenarios**:

  ```
  Scenario: 构建验证
    Tool: Bash (PowerShell)
    Steps: dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error 0 warning）
    Evidence: .omo/evidence/task-4-converter-build.txt
  ```

  **Commit**: NO — 未经用户明确要求不提交

---

- [ ] 5. ProgressWindow.axaml —— 5 张统计卡三行重构 + 并行度后置（2026-10-09 修订）

  **What to do**:

  替换 `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` 中 6 个卡片 StackPanel（line 583-701，位于统计卡容器 `<StackPanel Orientation="Horizontal" Spacing="{DynamicResource SpacingLg}">` 与其 `</StackPanel>` 之间）。**外层 `StatsCardsPanel` Border 与 5 个文件卡的 `IsVisible` 绑定全部保持不动**；容器内部改为竖排两段（5 卡卡片行 + 并行度后置单行）。完整替换代码：

  ```xml
            <!-- 段 1：文件统计卡行（5 张三行卡；并行度不在此行——非文件统计，见段 2） -->
            <StackPanel Orientation="Horizontal"
                        Spacing="{DynamicResource SpacingLg}"
                        HorizontalAlignment="Center">

            <!-- 卡 1：已处理（✅；引擎上报后才出现）— 三行：图标+标题横排 / 文件数量 / 文件大小（未上报字节时 — 灰显） -->
            <StackPanel Spacing="{DynamicResource SpacingXxs}"
                        HorizontalAlignment="Center"
                        IsVisible="{Binding StatsProcessedText, Converter={StaticResource StringNotEmpty}}">
              <!-- 行 1：图标 + 标题（横排居中） -->
              <StackPanel Orientation="Horizontal"
                          Spacing="{DynamicResource SpacingXxs}"
                          HorizontalAlignment="Center">
                <TextBlock Text="✅" FontSize="16"
                           VerticalAlignment="Center"
                           Foreground="{DynamicResource ThemeStatusSuccessBrush}"/>
                <TextBlock Text="{Binding StatsProcessedLabel}" FontSize="11"
                           VerticalAlignment="Center"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
              </StackPanel>
              <!-- 行 2：文件数量（已处理数纯计数，方案 A 去分母——比例由总进度条承担） -->
              <TextBlock Text="{Binding StatsProcessedCount}"
                         FontSize="15" FontWeight="SemiBold"
                         HorizontalAlignment="Center"
                         Foreground="{DynamicResource ThemeStatusSuccessBrush}"/>
              <!-- 行 3：文件大小（— 灰 / 真实值绿，DashBrush 动态切换） -->
              <TextBlock Text="{Binding StatsProcessedSize}"
                         FontSize="13"
                         HorizontalAlignment="Center"
                         Foreground="{Binding StatsProcessedSize, Converter={StaticResource DashBrush}, ConverterParameter=ThemeStatusSuccessBrush}"/>
            </StackPanel>

            <!-- 卡 2：跳过（⏭；引擎上报过冲突统计才出现）— 三行：图标+标题 / 文件数量 / 文件大小（终态累加值，无字节 — 灰显） -->
            <StackPanel Spacing="{DynamicResource SpacingXxs}"
                        HorizontalAlignment="Center"
                        IsVisible="{Binding StatsSkippedText, Converter={StaticResource StringNotEmpty}}">
              <!-- 行 1：图标 + 标题（横排居中） -->
              <StackPanel Orientation="Horizontal"
                          Spacing="{DynamicResource SpacingXxs}"
                          HorizontalAlignment="Center">
                <TextBlock Text="⏭" FontSize="16"
                           VerticalAlignment="Center"
                           Foreground="{DynamicResource ThemeStatusWarningBrush}"/>
                <TextBlock Text="{Binding StatsSkippedLabel}" FontSize="11"
                           VerticalAlignment="Center"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
              </StackPanel>
              <!-- 行 2：跳过数量 -->
              <TextBlock Text="{Binding StatsSkippedCount}"
                         FontSize="15" FontWeight="SemiBold"
                         HorizontalAlignment="Center"
                         Foreground="{DynamicResource ThemeStatusWarningBrush}"/>
              <!-- 行 3：文件大小（终态字节累加值；— 灰 / 真实值 warning，DashBrush 动态切换） -->
              <TextBlock Text="{Binding StatsSkippedSize}"
                         FontSize="13"
                         HorizontalAlignment="Center"
                         Foreground="{Binding StatsSkippedSize, Converter={StaticResource DashBrush}, ConverterParameter=ThemeStatusWarningBrush}"/>
            </StackPanel>

            <!-- 卡 3：出错（❌；引擎上报过冲突统计才出现）— 三行：图标+标题 / 文件数量 / 文件大小（终态累加值，无字节 — 灰显） -->
            <StackPanel Spacing="{DynamicResource SpacingXxs}"
                        HorizontalAlignment="Center"
                        IsVisible="{Binding StatsFailedText, Converter={StaticResource StringNotEmpty}}">
              <!-- 行 1：图标 + 标题（横排居中） -->
              <StackPanel Orientation="Horizontal"
                          Spacing="{DynamicResource SpacingXxs}"
                          HorizontalAlignment="Center">
                <TextBlock Text="❌" FontSize="16"
                           VerticalAlignment="Center"
                           Foreground="{DynamicResource ThemeStatusErrorBrush}"/>
                <TextBlock Text="{Binding StatsFailedLabel}" FontSize="11"
                           VerticalAlignment="Center"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
              </StackPanel>
              <!-- 行 2：出错数量 -->
              <TextBlock Text="{Binding StatsFailedCount}"
                         FontSize="15" FontWeight="SemiBold"
                         HorizontalAlignment="Center"
                         Foreground="{DynamicResource ThemeStatusErrorBrush}"/>
              <!-- 行 3：文件大小（终态字节累加值；— 灰 / 真实值 error，DashBrush 动态切换） -->
              <TextBlock Text="{Binding StatsFailedSize}"
                         FontSize="13"
                         HorizontalAlignment="Center"
                         Foreground="{Binding StatsFailedSize, Converter={StaticResource DashBrush}, ConverterParameter=ThemeStatusErrorBrush}"/>
            </StackPanel>

            <!-- 卡 4：已覆盖（🔄；引擎上报过冲突统计才出现）— 三行：图标+标题 / 文件数量 / 文件大小（终态累加值，无字节 — 灰显） -->
            <StackPanel Spacing="{DynamicResource SpacingXxs}"
                        HorizontalAlignment="Center"
                        IsVisible="{Binding StatsOverwrittenText, Converter={StaticResource StringNotEmpty}}">
              <!-- 行 1：图标 + 标题（横排居中） -->
              <StackPanel Orientation="Horizontal"
                          Spacing="{DynamicResource SpacingXxs}"
                          HorizontalAlignment="Center">
                <TextBlock Text="🔄" FontSize="16"
                           VerticalAlignment="Center"
                           Foreground="{DynamicResource ThemeStatusSuccessBrush}"/>
                <TextBlock Text="{Binding StatsOverwrittenLabel}" FontSize="11"
                           VerticalAlignment="Center"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
              </StackPanel>
              <!-- 行 2：已覆盖数量 -->
              <TextBlock Text="{Binding StatsOverwrittenCount}"
                         FontSize="15" FontWeight="SemiBold"
                         HorizontalAlignment="Center"
                         Foreground="{DynamicResource ThemeTextPrimaryBrush}"/>
              <!-- 行 3：文件大小（终态字节累加值；— 灰 / 真实值 primary，DashBrush 动态切换） -->
              <TextBlock Text="{Binding StatsOverwrittenSize}"
                         FontSize="13"
                         HorizontalAlignment="Center"
                         Foreground="{Binding StatsOverwrittenSize, Converter={StaticResource DashBrush}, ConverterParameter=ThemeTextPrimaryBrush}"/>
            </StackPanel>

            <!-- 卡 5：总大小（📏；引擎上报 TotalBytes 才出现，7z/TAR 不上报时整卡隐藏，D3/Rule 6）— 三行：图标+标题 / 文件总数（未上报时 — 灰显） / 文件大小（卡可见即必有值） -->
            <StackPanel Spacing="{DynamicResource SpacingXxs}"
                        HorizontalAlignment="Center"
                        IsVisible="{Binding StatsTotalSizeValue, Converter={StaticResource StringNotEmpty}}">
              <!-- 行 1：图标 + 标题（横排居中） -->
              <StackPanel Orientation="Horizontal"
                          Spacing="{DynamicResource SpacingXxs}"
                          HorizontalAlignment="Center">
                <TextBlock Text="📏" FontSize="16"
                           VerticalAlignment="Center"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
                <TextBlock Text="{Binding StatsTotalSizeLabel}" FontSize="11"
                           VerticalAlignment="Center"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
              </StackPanel>
              <!-- 行 2：文件总数（— 灰 / 真实值 primary，DashBrush 动态切换） -->
              <TextBlock Text="{Binding StatsTotalCount}"
                         FontSize="15" FontWeight="SemiBold"
                         HorizontalAlignment="Center"
                         Foreground="{Binding StatsTotalCount, Converter={StaticResource DashBrush}, ConverterParameter=ThemeTextPrimaryBrush}"/>
              <!-- 行 3：文件大小（卡可见即必有值，静态 primary） -->
              <TextBlock Text="{Binding StatsTotalSizeValue}"
                         FontSize="13"
                         HorizontalAlignment="Center"
                         Foreground="{DynamicResource ThemeTextPrimaryBrush}"/>
            </StackPanel>

            </StackPanel>

            <!-- 段 2：并行度后置元素（2026-10-09 用户修订：并行度非文件统计，移出卡片行、置于表格之后；单行横排，显隐仍绑 HasParallelDegree） -->
            <StackPanel Orientation="Horizontal"
                        Spacing="{DynamicResource SpacingXxs}"
                        HorizontalAlignment="Center"
                        Margin="0,8,0,0"
                        IsVisible="{Binding HasParallelDegree}">
              <!-- 图标（14 略小于卡内 16，弱化为附属信息） -->
              <TextBlock Text="⚙" FontSize="14"
                         VerticalAlignment="Center"
                         Foreground="{DynamicResource ThemeProgressFillBrush}"/>
              <!-- 标题（本地化「并行」） -->
              <TextBlock Text="{Binding LocalizedStrings[Progress_Stats_Parallel]}" FontSize="11"
                         VerticalAlignment="Center"
                         Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
              <!-- 线程数 -->
              <TextBlock Text="{Binding ParallelDegree}" FontSize="13" FontWeight="SemiBold"
                         VerticalAlignment="Center"
                         Foreground="{DynamicResource ThemeProgressFillBrush}"/>
            </StackPanel>
  ```

  注意实现要点：

  - `StatsCardsPanel` Border 内层需要从单一横向 StackPanel 改为**竖排 StackPanel**（包住段 1 + 段 2）；段 1 保持原横向布局与 `SpacingLg`
  - 并行度后置元素用 `Margin="0,8,0,0"` 与卡片行分隔（不引入新资源键；8px 固定值是段间距而非控件间距——若评审要求可用 `SpacingMdThk` 顶边距替代，实施者按现有窗口内 Margin 用法就近对齐）

  然后构建验证：

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  ```

  预期：Build succeeded，0 error 0 warning。

  **Must NOT do**:
  - 不改 5 个文件卡级 `IsVisible` 绑定与并行度 `HasParallelDegree` 绑定（规则 6 / D1/D5）
  - 不动中等档 `MediumStatsPanel` 与简约面板（D5 零改动）
  - 不模板化重构统计卡（方案 B 已否决）
  - 不新增本地化 key（规则 13；卡片标题复用既有 `Stats*Label` / `Progress_Stats_Parallel`）
  - 间距全部 `{DynamicResource Spacing*}`（规则 5）；新增控件均有中文注释（规则 14）

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO（依赖任务 1–3 的 VM 字段与任务 4 的 `DashBrush` 资源键）
  - **Blocks**: 任务 6
  - **Blocked By**: 任务 1、任务 2、任务 3、任务 4

  **Acceptance Criteria**:
  - [ ] 5 张文件卡各 3 行；卡 1/2/3/4 行 3 分别绑定 `StatsProcessedSize`/`StatsSkippedSize`/`StatsFailedSize`/`StatsOverwrittenSize` + `DashBrush`（参数分别为 Success/Warning/Error/Primary）
  - [ ] 总大小卡行 2 绑定 `StatsTotalCount` + `DashBrush`（参数 `ThemeTextPrimaryBrush`）
  - [ ] 并行度**不在**卡片行内，为表格之后的单行元素，`IsVisible="{Binding HasParallelDegree}"`
  - [ ] Build succeeded

  **QA Scenarios**:

  ```
  Scenario: 构建验证
    Tool: Bash (PowerShell)
    Steps: dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error 0 warning）
    Evidence: .omo/evidence/task-5-xaml-build.txt
  ```

  **Commit**: NO — 未经用户明确要求不提交

---

- [ ] 6. 全量验证：构建 + 双测试套 + GUI 人工验收

  **What to do**:

  (6a) 构建：

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  ```

  预期：0 error 0 warning。

  (6b) Avalonia 测试套：

  ```powershell
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
  ```

  预期：**170 通过**（基线 163 + 新增 7）/ **5 失败**（既有 `PreviewWebViewLazyInitTests` 预存失败，与本计划无关，不计入）。

  (6c) Core 测试套（本计划不改 Core，应零变化）：

  ```powershell
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
  ```

  预期：全绿（基线 615 通过）。

  (6d) GUI 人工验收（运行 `dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`，执行一次解压/压缩进度，密度切「完整」）：

  - 5 张文件卡全部三行；行 1 图标+标题横排居中
  - 已处理卡行 2 显示已处理数**纯计数**（如 `60`，无分母）；中等档一句话文案仍为「已处理 N/M」
  - 并行度为卡片行**之后**的单行元素（⚙ 并行 N），不在卡片行内；串行操作时整段隐藏
  - ZIP 解压（引擎上报字节）→ 卡 1 行 3 显示真实已处理大小（绿色）；7z/TAR → 同格灰 `—`
  - ZIP 解压有跳过/出错/已覆盖时 → 对应卡行 3 显示累加大小（各卡强调色）；冲突统计为空 → 对应卡整卡隐藏
  - 总大小卡行 2 在引擎上报 TotalFiles 后显示总数，否则灰 `—`
  - 中等/精简两档视觉零变化
  - 暗色主题下 `—` 灰、真实值强调色均正确

  **Must NOT do**:
  - 不为通过测试而删改既有测试

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: 任务 7
  - **Blocked By**: 任务 1–5

  **Acceptance Criteria**:
  - [ ] DoD 全部勾选（设计文档 Definition of Done 五条）
  - [ ] GUI 验收清单逐条通过

  **QA Scenarios**:

  ```
  Scenario: 双测试套回归
    Tool: Bash (PowerShell)
    Steps: dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj; dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
    Expected Result: Avalonia 170 过 / 5 预存失败；Core 615 过 / 0 失败
    Evidence: .omo/evidence/task-6-test-suites.txt
  ```

  **Commit**: NO — 未经用户明确要求不提交

---

- [ ] 7. 进度文档同步与提交（仅用户明确要求提交时执行）

  **What to do**:

  (7a) **先**按规则 3 更新进度文档（提交前必须完成，双轨）：

  - `docs/PROGRESS.md` → `### MantisZip.UI.Avalonia（主力版）` → `#### 2026-10` 分组（无则新建）顶部追加（从新到旧）：
    ```
    - **10-09** — 进度窗口统计卡三行结构：「完整」档 5 张文件统计卡改三行表格（图标+标题横排 / 文件数量 / 文件大小），并行度移出表格置后为单行元素；跳过/出错/已覆盖大小经终态字节累加器动态显示（前向兼容 FileTotalBytes）；VM 新增 5 个统计字段 + `DashBrushConverter` 动态强调色
    ```
  - `docs/progress-avalonia-detail.md` 顶部 `**2026-10-09**` 分组追加详细条目（VM 五字段 + 累加器、转换器、5 卡 XAML + 并行后置、7 条新测试、中/精简档零改动）

  (7b) 按规则 1 将 `docs/PLAN.md` 中本任务行归档至 `docs/PROGRESS.md` 的【历史设计方案索引】章节（计划完成）。

  (7c) 提交：

  ```powershell
  git add src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs src/MantisZip.UI.Avalonia/Converters/DashBrushConverter.cs src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs docs/PROGRESS.md docs/progress-avalonia-detail.md docs/PLAN.md
  git commit -m "feat(avalonia): 进度窗口统计卡三行结构（数量+大小，并行度后置）"
  ```

  注意：若工作树还有本计划之外的未提交改动，不得混入本 commit，只 stage 上列文件。

  **Must NOT do**:
  - 不在更新进度文档之前执行 commit（规则 3）
  - 不动版本号（规则 2）
  - 不提交本计划外文件

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `["git-master"]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: None
  - **Blocked By**: 任务 6

  **Acceptance Criteria**:
  - [ ] 双轨进度文档已更新且条目排序从新到旧
  - [ ] PLAN.md 任务行已归档至 PROGRESS.md 历史索引
  - [ ] commit 仅含上列文件，信息符合规则 10

  **QA Scenarios**:

  ```
  Scenario: 提交前文档与暂存区核查
    Tool: Bash (PowerShell)
    Steps: git status; git diff --cached --stat
    Expected Result: 仅上列文件暂存；PROGRESS.md 与 progress-avalonia-detail.md 均含 10-09 新条目
    Evidence: .omo/evidence/task-7-commit.txt
  ```

  **Commit**: YES（条件执行）— `feat(avalonia): 进度窗口统计卡三行结构（数量+大小，并行度后置）`
