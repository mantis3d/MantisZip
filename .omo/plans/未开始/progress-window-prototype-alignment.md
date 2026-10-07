# 进度窗口原型对齐改造（progress-window-prototype-alignment）

> **Agentic Execution Note** — 本计划是 [progress-window-enhancement.md](progress-window-enhancement.md)（修订版 v2）的**纠偏续作**。v2 的 T1–T9 已实施（除 F3 手工 QA），但其「原型对应表」误读了 v6 原型的两组切换器，导致成品语义与原型不符。本计划只做**对齐**，不复用 v2 的错误映射。所有文件路径、行号、API 均已对当前仓库（Avalonia 版）逐字节核实。
>
> 执行方式：按 Wave 顺序执行；波次内标注 `∥` 的任务可并行。每任务完成后必须运行该任务指定的验证命令（Rule 12），通过后才能标记完成。

> **📌 当前状态（2026-10-07 整理标注）**
>
> - **T1–T11 全部实施完成**；F1 ✅ 构建、F2 ✅ 测试、F4 ✅ 性能（六场景中位数回退 ≤0.3%）；**F3 自动化取证 8✅ / 4◐ / 0✗**，尚未收口。
> - **F3 余项**（需人眼或构造场景，收口时逐条验）：
>   1. 纯观感：配色美观度、动画流畅度、裁切观感、失败行红色
>   2. 条目 6 态中的 4 态（○等待 / ⏭跳过 / 已覆盖 等）取证
>   3. 密码徽标终态与入场动画、Flyout、复制 toast
>   4. 主窗口解压路径的「并行」卡正向取证
>   5. TAR/GZ **列表模式**渐进建行 + 10 万条目 UI 流畅度
>   6. 从 v2 计划并入的有效条目：输错密码循环、取消 → 行 ✗ 且批继续、批次切换 ETA/速度归零重起、统计数字抽样比对
> - **Deferred 变动**：#2（语言刷新）、#3（7z/TAR 速度与 ETA）已转入 [progress-window-bytes-i18n.md](progress-window-bytes-i18n.md)；**#7（通用压缩播种）已于 commit `7505952` 实施**，详见文末 Deferred 节。
> - 非阻塞瑕疵（AutomationId 旧语义命名、可访问名等）见「本轮自动化取证发现的待办」。

---

## TL;DR

### Scope Check（先做范围检查）

- **需要写代码** ✓（Core 契约 + 引擎埋点 + Avalonia UI 布局/VM + 测试 + 三语文案）
- **涉及多个子系统** ✓（`MantisZip.Core` 与 `MantisZip.UI.Avalonia`）
- **但耦合点是单一的**：唯一跨层依赖是「列表」模式需要的**逐条目状态遥测**——UI 拿不到引擎逐条目上报，列表模式就是空壳；引擎埋点没做，Core 改动就是死代码。
- **判定：单计划分 4 波**，不拆。理由：拆成两个计划会让其中一个不可独立验收（违反「每个 commit 都能编译通过」），且 v2 已确立单计划风格。
- ⚠ **每个 commit 必须能编译**：Core 契约（T1）是向后兼容的可选字段，单独提交安全；引擎埋点（T3）在 UI 消费（T6）之前提交也安全（无消费方）。

### Quick Summary

把 `ProgressWindow` 从「v2 误读版」改回 v6 原型的真实语义：

1. **删除** `TopDisplayMode`（全路径/仅目录/仅文件名）与 `DensityMode`（紧凑/标准/宽松）两组开关 → 换为原型的**内容模式**（简约/详细/列表）与**信息量分级**（少/中/完整）。二者语义完全不同：前者是「间距松紧」，后者是「显示多少信息」。
2. **布局重排**：批处理列表从底部**上移**到顶部（原型 `.file-info-section`），整体信息区（密度切换 + 统计 + 总进度 + 时间）下沉并加独立底色（原型 `.overall-info-section`）。
3. **统计卡**：原型是 5 张卡（已处理/跳过/出错/已覆盖/进程）带图标 + 值的卡片式布局；当前是一行纯文本。新增第 5 张卡 = **真实并行度**（非原型的假「8 线程」）。
4. **列表模式**：原型逐文件追踪（✓完成 / ✗出错 / ⏭跳过 / ⏳n% / ○等待 + 文件大小）。需**引擎新增逐条目状态上报**（`ArchiveProgress` 扩展 `EntryKey`/`EntryStatus`）。
5. **详细模式**：原型「线程进度」列表 → 实际数据源是 **ZIP 并行批次**（`ParallelBatchItems` 已存在），标签用「批次」而非「线程」（诚实性，见 D5）。
6. **补漏**：批处理行**失败错误消息当前完全未绑定到 XAML**（`BatchItem.ErrorMessage` 只写不显示）；复制密码缺 toast/按钮反馈；密码徽标缺入场动画。

### Deliverables

- `ArchiveProgress` 扩展 2 个可选字段（`EntryKey`/`EntryStatus`）+ 新枚举 `ArchiveEntryStatus`（4 成员，纯事实态）
- `EntryProgressItem` 行模型（`Core/Models/`，`INotifyPropertyChanged`，含 `EntryRowState` 6 态）
- 三引擎 **9 处**逐条目埋点（解压 5 + 压缩 4），复用已有 `ConflictStatsCounter` 埋点点的结果，不重复记账
- `ProgressDisplayMode.cs` 重写为 `ProgressContentMode`（`Simple/Detailed/List`）+ `ProgressInfoDensity`（`Minimal/Medium/Full`）
- `ProgressViewModel`：删 2 枚举 6 派生属性，新增内容/密度/条目集合/并行度 + 集中通知
- `ProgressWindow.axaml` 486 行 → 7 行网格重写（批处理上移 / 内容区三态 / 下方信息区）
- `ProgressWindow.axaml.cs`：删 6 个 radio 处理器 + `ApplyDensityMode` + `GetResourceDouble`；新增 6 个处理器 + toast + 徽标入场
- 三语文案：新增 32 key（全部 `Progress_*`）、净删除 0（计划点名的 6 个旧 key 在基线 `9551e18` 中已不存在，非本任务删除）
- 测试：`ProgressWindowXamlTests` 10 行断言改 7 行 + 删 `TopDisplayMode` 用例 + 新增 6 用例；引擎新增逐条目上报用例

### Effort

**12–15h**（Core 遥测 4–5h + UI 对齐 6–8h + 测试/文案 2h）

### Parallel Execution

```
Wave 1 (Core)  ── T1 ── T2                      (契约先行，行模型独立 → ∥)
                  T3 ── T4                       (埋点 + 其测试，串行)
Wave 2 (VM)    ── T5 ── T6                      (属性 → 消费，顺序)
Wave 3 (View)  ── T7 ── T8 ∥ T9                 (骨架 → 详细/列表 ∥)
                  T10                            (同文件尾部装饰，串行)
Wave 4 (收尾)  ── T11
Final          ── F1 → F2 → F3 → F4
```

### Critical Path

**T1 → T3 → T6 → T7 → T9**（约 9h）；T2/T4/T5/T8/T10 可提前并入。**T9（列表模式）是本计划风险最高的任务**，依赖 T1+T2+T3+T6 全部完成。

---

## Context

### Original Request

用户在 `progress-window-enhancement` v2 实施完成后发现成品与 `docs/prototypes/progress-window-enhancement.html`（v6 原型）不符，选择 **方案 C：完整对齐原型**，并要求「对齐后不能影响压缩/解压速度」。

### Root Cause（本计划存在的原因）

**根因在计划，不在实现。** v2 计划 `progress-window-enhancement.md:147-159` 的「原型对应表」写错了：

| v2 计划声称 | v6 原型实际（已逐行核实） |
|---|---|
| 上方三模式 = 全路径 / 仅目录 / 仅文件名 | 上方三模式 = **简约 / 详细 / 列表**（`:530-532`） |
| 下方三档 = 紧凑 / 标准 / 宽松（间距） | 下方三档 = **少 / 中 / 完整**（信息量，`:637-639`） |
| 统计在上方、批处理列表在下方 | 批处理列表在**上**，整体信息区在**下**（`:518` vs `:634`） |
| 无统计卡、无图标 | 5 张**卡片**带图标 + 值（`:643-669`） |
| 无逐文件列表 | **列表模式**逐文件状态 + 大小（`:591-630`） |

实现严格遵循了错误的计划，因此「跑偏 8 个点」是必然的，不是执行走样。

### Prototype 事实（已核实）

- **`.mode-switcher#bottomModeSwitcher`（标题栏内的密度切换器）是死代码**：`:508-512` 渲染了它，但整个 `<script>` 中**没有任何 listener 绑定它**（`:1100` 只绑定 `.density-switcher`）。点击无反应。
- **真正生效的密度切换器**在下方区顶部（`.density-switcher`，`:636-640`），JS `:1100-1133` 定义了三档语义：
  - `少`（minimal）= 进度条 + 时间
  - `中`（medium）= 中等单行（已处理 + 速度 + 进程）+ 进度条 + 时间
  - `完整`（full）= 统计栏全部 + 进度条 + 时间
- **`.speed-display` 的 CSS（`:442-449`）是死样式**：HTML 中无对应元素。速度只在「中」档的单行里出现。
- **原型模式说明**（`:702-703`）：上方 = 简约（文件概览）| 详细（线程进度）| 列表（文件追踪）；下方 = 少（进度+时间）| 中（+已处理+速度）| 完整（+已覆盖+线程数）
- **逐文件状态 6 态**（`:390-396` CSS + `:593-628` DOM）：`completed`✓ / `error`✗ / `skipped`⏭ / `active`⏳（带 n%）/ `pending`○（文案「等待」）
- **批次/线程行结构**（`.thread-item`，`:365-378`）：`线程 N` | 文件名 | 进度条 | 百分比

### Interview Decisions（用户已拍板，8 项）

| # | 决策 |
|---|------|
| **D1** | **接受删除旧双开关**，完全对齐原型。`TopDisplayMode`（全路径/仅目录/仅文件名）与 `DensityMode`（紧凑/标准/宽松）连同其无头测试一并删除；窗口间距回归全局 `AppSettings.CompactnessMode` 单一来源；路径/文件名在「简约」模式**恒定双行显示**（`DirVisible`/`NameVisible` 派生属性删除，XAML 直接绑定） |
| **D2** | **列表模式数据源 = 引擎逐条目上报**。`ArchiveProgress` 扩展 `EntryKey` + `EntryStatus` 可选字段（**不改 `IArchiveEngine` 签名**），三引擎循环内上报。选此方案而非 UI 侧推断，因并行解压无法推断完成态、TAR/GZ 预列需全流扫描 |
| **D3** | **第 5 张统计卡 = 真实并行度**。显示 `ArchiveOptions.ParallelExtractDegree`（标签「并行」），仅在并行解压（degree ≥ 2）时可见。**不显示** `Environment.ProcessorCount`（与本次任务实际并行度无关）。历史评审已明确杀掉假「进程 8 线程」统计，不得回退 |
| **D4** | **不实现标题栏密度切换器**。原型自身的 JS 从未接线该元素（死 UI），实现一个点不动的控件比重构更糟。密度切换器只出现在下方信息区顶部（与原型生效行为一致） |
| **D5** | **详细模式行标签用「批次 N」而非「线程 N」**。底层真实单位是 Round-Robin 分配的执行批次（见 AGENTS.md 并行解压架构），不是 OS 线程；标「线程」与 D3 杀掉的假统计是同一类不诚实。**这是对原型的有意偏离**，评审时可挑战 |
| **D6** | **详细模式按可用性隐藏（Rule 6）**。可用性 = `HasParallelBatches`。7z/TAR/单文件无批次数据 → 「详细」单选项 `IsVisible=false`；若当前处于详细模式且批次集合变空（如切换档案），**自动回落简约**而非显示空列表 |
| **D7** | **列表模式播种策略分档**。ZIP/7z 解压 + 压缩：从 `ListEntriesAsync` / 源文件列表播种（含「○等待」行，ZIP/7z 列表仅读头，廉价）；**TAR/GZ 不播种**（其 `ListEntriesAsync` 是全流扫描，成本≈解压一次），改为条目随上报渐进出现（无等待行）；**任何路径条目数 > 5000 时跳过播种**转渐进模式（避免 10 万行内存 + UI 长停顿） |
| **D8** | **逐条目上报不受进度节流约束，但必须锁外 Report**。每个条目事件都承载真实语义（不可节流丢弃）；但 ZIP 并行路径**禁止在 `reportLock` 内调用 `progress?.Report()`**——先在锁内拷贝共享状态、释放锁后再报（AGENTS.md 记录过锁内 Report 致 100×1MB 解压 0.3s→8.5s，25× 回退）。UI 侧继续走既有 `BackgroundDispatcherProgress`（`DispatcherPriority.Background`） |

### Research Findings（代码事实，已逐字节核实）

- `ArchiveProgress` = class（`ArchiveEngine.cs:294-323`），15 个字段，**新增可选字段向后兼容**；`IArchiveEngine` 在 `:347`，**签名冻结**。
- **三引擎都已上报逐文件进度**（AGENTS.md 中「7z/TarGz 仅完成时上报」已过时）：
  - `SevenZipEngine.ExtractAsync` — 中间上报在 `:379` / `:405` / `:430`
  - `TarGzEngine.ExtractAsync` — 上报在 `:82` / `:128`
  - 但都只报 `CurrentFile`（当前文件名）+ 计数，**不报逐条目身份与终态**
- **只有 ZIP 报字节计数**（`ProcessedBytes`/`TotalBytes`）→ **速度与 ETA 目前只对 ZIP 有效**，7z/TAR 的速度/ETA 恒空。列表模式不改变这一点（进度条仍显示引擎给的百分比）。
- **只有 ZIP 上报批次字段**（`BatchIndex`/`BatchCount`/`BatchPercentComplete`/`BatchProcessedFiles`/`BatchTotalFiles`）→ 详细模式只有 ZIP 可用（D6 据此）。
- **列表成本**：`TarGzEngine.ListEntriesAsync:357` 是全流扫描；ZIP/7z 仅读头。→ D7 分档依据。
- **冲突结果埋点已存在**：`ConflictStatsCounter`（`Core/Utils/ConflictStatsCounter.cs`）有 `RecordSkipped/RecordOverwritten/RecordFailed`，调用点在 `FileConflictHelper.ResolvePathAsync`（`:67`）的 10 个引擎调用处（ZipEngine ×4 / TarGzEngine ×4 / SevenZipEngine ×2）。**逐条目埋点复用这些已知道结果的位置**，不新增记账逻辑。
- `BatchItem.ErrorMessage`（`ProgressBatchItem.cs:55`）**只写不显示**——`ProgressWindow.axaml` 批处理行模板（`:253-385`）无任何 `ErrorMessage` 绑定。当前失败行只显示状态图标 + 0% 进度，错误原因丢失。
- `ParallelBatchProgressItem`（`Models/`）现有字段：`Index`/`Percent`/`DetailText`/`StatusBrushName`；**缺当前文件名**（详细模式需要）。
- `ProgressViewModel.LocalizedStrings`（`:62-100`，30 个 key）**在构造函数中一次性构建，无 `OnCultureChanged` 刷新**——本次不修（既有缺陷，超出范围），但新增 key 必须登记到此数组，否则 XAML 绑定空白且构建不报错。
- 派生属性集中通知模式（AGENTS.md）：新增派生属性只改 `NotifyDisplayProperties()` 一处。
- 现有主题键：`ThemeSplitterBgBrush`（**存在**，`ThemeSplitterBrush` 不存在，上轮已修）、`ThemeStatusSuccessBrush`/`ThemeStatusFailedBrush`/`ThemeStatusSkippedBrush`/`ThemeStatusWarningBrush`/`ThemeStatusErrorBrush`/`ThemeProgressFillBrush`/`ThemeProgressBgBrush`/`ThemeSurfaceBgBrush`/`ThemeBorderBrush`/`ThemeTextPrimaryBrush`/`ThemeTextSecondaryBrush`。
- 测试会破：`ProgressWindowXamlTests.cs:35`（`Assert.Equal(10, root.RowDefinitions.Count)`）、`:156-175`（`TopDisplayMode_TogglesPathAndNameVisibility` 引用即将删除的枚举）、`:121`（`BatchIndex = null`）；`ProgressViewModelTests.cs` 涉密度/模式属性。
- LSP `lsp_diagnostics` 对 C# 持续 3000ms 超时 → **`dotnet build` 是权威验证**。
- 两个 `dotnet test` **必须顺序执行**：并行会争用 `MantisZip.UI.Avalonia.dll` 触发 `CS0009`。

---

## Goal

`ProgressWindow` 的视觉结构与交互语义与 v6 原型一致，且不引入任何可测量的压缩/解压吞吐回退。

## Architecture

```
Core（框架无关）
  ArchiveProgress += EntryKey / EntryStatus   (可选字段，契约向后兼容)
  ArchiveEntryStatus { Completed, Skipped, Failed, Overwritten }   (纯事实态)
  EntryProgressItem / EntryRowState           (行模型，6 态含 UI 态)
      ▲ 三引擎在「已知结果」的埋点上报 EntryKey+EntryStatus（复用 ConflictStatsCounter 站点）
      │ 数据通道仍是唯一的 IProgress<ArchiveProgress>（IArchiveEngine 签名冻结）
UI（Avalonia）
  ProgressViewModel: ContentMode / InfoDensity / EntryItems / ParallelDegree
      + 集中通知 NotifyDisplayProperties()
  ProgressWindow.axaml: 7 行网格（批处理上移 → 内容模式 → 内容区 → 状态 → 下方信息区 → 错误 → 按钮）
```

**分层纪律**：Core 只产事实（枚举 + 数值），中文格式化与 6 态视觉映射全在 UI 层（`EntryRowState` + `LocalizationManager.T`）。

## Tech Stack

- .NET 10 / Avalonia 12 / CommunityToolkit.Mvvm（`ProgressViewModel` 现有手动属性风格）
- SharpCompress（ZipEngine / TarGzEngine）、SharpSevenZip（SevenZipEngine）
- xUnit + Avalonia.Headless.XUnit 12.1.2（`ProgressWindowXamlTests` 无头运行时）

---

## File Structure

```
src/
├── MantisZip.Core/
│   ├── Abstractions/ArchiveEngine.cs          # [M] T1: EntryKey + EntryStatus + ArchiveEntryStatus 枚举
│   ├── Models/
│   │   ├── ProgressBatchItem.cs               # (ref) ErrorMessage 已有，T10 绑定到 XAML
│   │   └── EntryProgressItem.cs               # [NEW] T2: 行模型 + EntryRowState 6 态 + MapEntryStatus
│   └── Engines/
│       ├── ZipEngine.cs                       # [M] T3: 5 处（串行/并行/过滤解压/压缩 store/压缩 7z 组）
│       ├── SevenZipEngine.cs                  # [M] T3: 2 处（解压/压缩）
│       └── TarGzEngine.cs                     # [M] T3: 2 处（解压/压缩）
├── MantisZip.UI.Avalonia/
│   ├── Models/
│   │   ├── ProgressDisplayMode.cs             # [M] T5: 重写为 ProgressContentMode + ProgressInfoDensity
│   │   └── ParallelBatchProgressItem.cs       # [M] T5: 加 CurrentFile（详细模式行）
│   ├── ViewModels/ProgressViewModel.cs        # [M] T5+T6: 删旧 6 属性，加新属性 + 条目消费 + 播种
│   ├── Dialogs/
│   │   ├── ProgressWindow.axaml               # [M] T7-T10: 486 行 → 7 行网格重写
│   │   └── ProgressWindow.axaml.cs            # [M] T7+T10: 删 6 处理器 + ApplyDensityMode，加 6 处理器 + toast
│   ├── Localization/                          # [M] T11: 三语 +32 key（全部 Progress_*），净删除 0
│   ├── strings.zh-CN.json / strings.zh-TW.json / strings.en.json
│   └── Services/ExtractFlow.cs                # [M] T6: 调 SetParallelDegree（:174 已设 options.ParallelExtractDegree）
tests/
├── MantisZip.Tests/Engines/
│   ├── ZipEngineTests.cs                      # [M] T4: 逐条目上报用例
│   ├── ParallelExtractTests.cs                # [M] T4: 并行逐条目上报用例
│   └── SevenZipEngineTests.cs                 # [M] T4: 7z 逐条目上报用例
└── MantisZip.UI.Avalonia.Tests/
    ├── ProgressWindowXamlTests.cs             # [M] T11: 10 行→7 行、删 TopDisplayMode 用例、新增 6 用例
    └── ProgressViewModelTests.cs              # [M] T11: 密度/模式属性用例
```

---

## Wave 1 — Core 逐条目遥测

### Task T1: `ArchiveProgress` 扩展逐条目契约

**文件**: `src/MantisZip.Core/Abstractions/ArchiveEngine.cs`

**改动**:
1. 在 `ArchiveProgress`（`:294`）新增两个可选字段：
   ```csharp
   /// <summary>逐条目事件：压缩包内条目键（解压）或源相对路径（压缩）。非逐条目报告时为 null。</summary>
   public string? EntryKey { get; set; }

   /// <summary>逐条目终态。非逐条目报告时为 null（此时本对象是常规进度报告）。</summary>
   public ArchiveEntryStatus? EntryStatus { get; set; }
   ```
2. 在 `ArchiveProgress` 之前新增枚举（**只含引擎会真实上报的 4 个事实态**；`Pending`/`Active` 是纯 UI 态，不得污染 Core）：
   ```csharp
   /// <summary>条目终态（引擎只上报事实终态；等待/进行中由 UI 播种与推导）。</summary>
   public enum ArchiveEntryStatus { Completed, Skipped, Failed, Overwritten }
   ```
3. XML 注释说明：两个字段为可选、向后兼容；`EntryStatus != null` 即表示这是一次**逐条目事件报告**，可与常规字段（`ProcessedFiles` 等）同时存在。

**约束**: `IArchiveEngine`（`:347`）**签名零改动**。

**Acceptance Criteria**:
- `ArchiveProgress` 新增 2 个属性 + `ArchiveEntryStatus` 枚举存在于 `MantisZip.Core.Abstractions` 命名空间
- 既有 15 个字段与所有 `IArchiveEngine` 方法签名逐字节不变
- 新字段为可空，`new ArchiveProgress()` 老式初始化仍能编译

**Verification**: `dotnet build src\MantisZip.Core\MantisZip.Core.csproj` → 0 错误

---

### Task T2: `EntryProgressItem` 行模型

**文件**: `src/MantisZip.Core/Models/EntryProgressItem.cs`（新建）

**改动**: 实现 `INotifyPropertyChanged` 行模型 + UI 侧 6 态枚举 + 映射：

```csharp
/// <summary>列表模式行状态（UI 侧；Pending/Active 为播种/推导态，其余来自引擎上报）。</summary>
public enum EntryRowState { Pending, Active, Completed, Skipped, Failed, Overwritten }

public class EntryProgressItem : INotifyPropertyChanged
{
    public string EntryKey { get; set; }      // 与 ArchiveProgress.EntryKey 对应（播种时的唯一键）
    public string Name { get; set; }          // 显示名（Path.GetFileName(EntryKey)）
    public long Size { get; set; }            // 字节（播种时来自 ArchiveItem.Size；未知为 0）
    public double Percent { get; set; }       // 0–100（Active 行显示 n%）
    // + EntryRowState State（setter 内集中通知派生属性）
    // + 派生：IsPending / IsActive / IsTerminal / StatusBrushName / StatusIcon
    //   StatusBrushName → Completed:"ThemeStatusSuccessBrush" / Skipped:"ThemeStatusSkippedBrush"
    //                     / Failed:"ThemeStatusFailedBrush" / Overwritten:"ThemeProgressFillBrush"
    //                     / Active:"ThemeStatusWarningBrush" / Pending:"ThemeBorderBrush"
    // + static EntryRowState MapEntryStatus(ArchiveEntryStatus) 四态 switch

    public string SizeText { get; }           // FormatUtil.FormatSize(Size)，Size==0 → 空串（不显示 "0 B"）
}
```

**约束**:
- 严格镜像 `BatchItem`（`ProgressBatchItem.cs`）的 `INotifyPropertyChanged` 手写风格 + `Set<T>` 辅助 + `Notify*Properties()` 集中通知（**禁 `[NotifyPropertyChangedFor]`**，AGENTS.md 规则）
- `MapEntryStatus` 用 `switch` 表达式，**穷举 4 成员无 default**（新增枚举成员时编译报错即为提醒）

**Acceptance Criteria**:
- `EntryRowState` 6 成员，`MapEntryStatus` 覆盖 `ArchiveEntryStatus` 全部 4 成员
- `State` setter 触发 `IsPending`/`IsActive`/`IsTerminal`/`StatusBrushName`/`StatusIcon` 全部 `PropertyChanged`
- `Size == 0` 时 `SizeText` 为空串

**Verification**: `dotnet build src\MantisZip.Core\MantisZip.Core.csproj` → 0 错误

---

### Task T3: 三引擎逐条目埋点

**文件**: `src/MantisZip.Core/Engines/ZipEngine.cs`、`SevenZipEngine.cs`、`TarGzEngine.cs`

**通用规则（9 处全部适用）**:
1. **在已经知道结果的位置上报**，不新增记账：`ConflictStatsCounter.RecordSkipped/RecordOverwritten/RecordFailed` 的现有调用点已经知道每个条目的终态，在同一处追加一次 `progress?.Report(...)`，`EntryStatus` 直接取该处的判定结果。
2. **正常写盘成功**的条目在写盘完成后上报 `Completed`。
3. **`ArchiveProgress` 对象复用**：把 `EntryKey`/`EntryStatus` 填进该次已在构造的 `ArchiveProgress`（避免每条目 new 一个对象）；若该路径当前没有在构造 `ArchiveProgress`，则构造一个**只带条目字段的轻量实例**（`new ArchiveProgress { EntryKey = …, EntryStatus = … }`），UI 侧靠 `EntryStatus != null` 判别，不会误读计数。
4. **D8 铁律**：ZIP 并行路径**禁止在 `reportLock` 内 `Report`**。锁内只拷贝 `entryKey` + `status` 到局部变量，出锁后 Report。
5. **`EntryKey` 用引擎内部的条目键**（ZIP 用 `IArchiveEntry.Key`，与 `ExtractAsyncParallel` 里 `archive.Entries.First(e => e.Key == key)` 同一 key；7z/TAR 用条目的相对路径），**不要用 `Path.GetFileName` 截断**——截断会导致同名条目互相覆盖（`a/x.txt` 与 `b/x.txt`）。

**逐处清单**:

| # | 文件 | 位置 | 上报时机 | EntryStatus |
|---|---|---|---|---|
| 1 | `ZipEngine` | `ExtractAsyncSequential:258` 内条目循环 | 每条目写盘完成 | `Completed` / 跳过取埋点判定 / 失败取埋点判定 |
| 2 | `ZipEngine` | `ExtractAsyncParallel:407` 内批次 `foreach` | 同上，**锁外** | 同上 |
| 3 | `ZipEngine` | `ExtractEntriesAsync` 条目循环 | 同上 | 同上 |
| 4 | `ZipEngine` | `CompressAsync` store 复制循环 | 每个源文件复制完成 | `Completed`（压缩无冲突终态） |
| 5 | `ZipEngine` | `CompressGroupWithSevenZip:2707` 的 `FileCompressionStarted` 之后的完成点 | 每个文件压缩完成 | `Completed` |
| 6 | `SevenZipEngine` | `ExtractAsync:308` 条目循环 | 每条目写盘完成 | `Completed` / 埋点判定 |
| 7 | `SevenZipEngine` | `CompressAsync` 压缩器事件 | 每个文件压缩完成 | `Completed` |
| 8 | `TarGzEngine` | `ExtractAsync:25` 条目循环 | 每条目写盘完成 | `Completed` |
| 9 | `TarGzEngine` | `CompressAsync:217` 文件循环 | 每个文件写完 | `Completed` |

**实施要求**:
- 现有 `grep -n "ResolvePathAsync\|ConflictStatsCounter" src/MantisZip.Core/Engines/` 得到的调用点即为埋点锚点；**行号会随编辑漂移，一律以方法名 + 计数器方法名为准**
- 逐条目上报**不受** `ProgressThrottle`（100ms）约束——那是百分比节流，条目事件不可丢
- `EntryKey` 在压缩路径用**源相对路径**（与 `CompressGroupWithSevenZip` 的 `relativePath` 一致）

**Acceptance Criteria**:
- 9 处埋点全部落地，每处注释标明 `// 逐条目状态（D2）`
- 无任何 `Report` 调用位于 `reportLock` 临界区内
- 三个引擎 `ExtractAsync` 对 N 个文件的上报次数精确为 N（过滤解压为过滤后的条数）
- 既有进度上报行为零回退（`ProcessedBytes`/`BatchIndex`/百分比语义不变）

**Verification**:
```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
```
→ 0 错误；随后跑 T4 测试。

---

### Task T4: 引擎逐条目上报测试

**文件**: `tests/MantisZip.Tests/Engines/ZipEngineTests.cs`、`ParallelExtractTests.cs`、`SevenZipEngineTests.cs`

**改动** — 新增用例（沿用文件内既有 fixture 与临时归档构造方式，不新建测试工程）:

1. `ExtractAsync_ReportsPerEntryCompleted_OncePerFile`（`ZipEngineTests`）
   构造含 5 个文件的 ZIP → `ExtractAsync` 收集所有 `ArchiveProgress` → 断言：`EntryStatus != null` 的报告数 == 5，且 5 个 `EntryKey` 互不相同、与归档内 key 集合相等，全部为 `Completed`。
2. `ExtractAsync_SkippedEntry_ReportsSkippedStatus`（`ZipEngineTests`）
   目标目录预置同名文件 + `options` 设为跳过 → 断言该条目 `EntryStatus == Skipped`，且仍出现在报告中（不静默丢）。
3. `ExtractAsyncParallel_ReportsPerEntry_ExactlyOncePerFile`（`ParallelExtractTests`）
   100 个文件 + `ParallelExtractDegree = 4` → 断言条目报告总数 == 100 且 `EntryKey` 无重复（**并行去重的核心回归测试**）。
4. `ExtractAsync_ReportsEntryKeyWithoutPathTruncation`（`ZipEngineTests`）
   构造 `a/x.txt` 与 `b/x.txt` → 断言两条报告的 `EntryKey` **不相等**（防同名覆盖）。
5. `SevenZip_ExtractAsync_ReportsPerEntry`（`SevenZipEngineTests`）
   复用该文件内已有的 7z 构造 fixture → 断言条目报告数 == 文件数。

**约束**: 新用例**不得**修改既有断言；不得引入 sleep 式等待（用 `TaskCompletionSource` 或直接 await 返回后检查收集集合）。

**Acceptance Criteria**: 5 个新用例全绿；既有 `MantisZip.Tests` 全绿。

**Verification**:
```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```
→ 全绿（基线 544 通过 / 3 跳过，新增 5 → 549 通过 / 3 跳过）

---

## Wave 2 — ViewModel

### Task T5: 枚举重写 + VM 属性替换

**文件**: `src/MantisZip.UI.Avalonia/Models/ProgressDisplayMode.cs`、`Models/ParallelBatchProgressItem.cs`、`ViewModels/ProgressViewModel.cs`

**改动**:

1. `ProgressDisplayMode.cs` **整体重写**（文件名保留，内容替换）:
   ```csharp
   /// <summary>进度窗口上方内容模式（v6 原型三模式）。</summary>
   public enum ProgressContentMode { Simple, Detailed, List }

   /// <summary>进度窗口下方信息量分级（v6 原型三档：控制「显示多少信息」，非间距）。</summary>
   public enum ProgressInfoDensity { Minimal, Medium, Full }
   ```
2. `ParallelBatchProgressItem` 新增 `CurrentFile`（string，`INotifyPropertyChanged`）——详细模式行需要显示批次当前文件名。
3. `ProgressViewModel` 删除（D1）:
   - `TopDisplayMode`/`DensityMode` 属性、`_topDisplayMode`/`_densityMode` 字段
   - 派生 `DirVisible`/`NameVisible`/`IsCompactDensity`/`IsLooseDensity`
   - `LocalizedStrings` 中 6 个旧 key 登记（`Progress_Mode_FullPath`/`DirOnly`/`NameOnly`/`Progress_Density_Compact`/`Normal`/`Loose`）
4. `ProgressViewModel` 新增:
   - `ContentMode`（默认 `Simple`）+ `InfoDensity`（默认 `Medium`，对齐原型默认档 `:638`）
   - `IsSimpleMode` / `IsDetailedMode` / `IsListMode` 派生
   - `IsDetailedAvailable => _parallelBatchItems.Count > 0`（D6；`CollectionChanged` 已驱动 `HasParallelBatches` 通知，复用同一入口）
   - `ObservableCollection<EntryProgressItem> EntryItems` + `HasEntryItems`
   - `ParallelDegree`（`int?`）+ `HasParallelDegree => ParallelDegree is >= 2`
   - `SeedEntryItems(IReadOnlyList<(string Key, string Name, long Size)>)` — D7 播种；`ClearEntryItems()`
   - `UpdateEntryStatus(string entryKey, EntryRowState state, double? percent)` — 按 key upsert（未命中则新建行，兼容 TAR/GZ 渐进模式 D7）
5. `NotifyDisplayProperties()` 扩充通知：`IsSimpleMode`/`IsDetailedMode`/`IsListMode`/`IsDetailedAvailable`/`HasEntryItems`/`HasParallelDegree`。**新增派生属性只改这一处**（AGENTS.md 集中通知模式）。
6. `LocalizedStrings` 新增 key 登记（T11 落地文案，**本任务先登记占位调用**，漏登记 = XAML 空白且构建不报错）。此处列举的是 **T2 首批 13 个**；T7–T10 继续扩充，**最终全集 32 个**（见 T11 表格）：
   `Progress_Mode_Label`、`Progress_Mode_Simple`、`Progress_Mode_Detailed`、`Progress_Mode_List`、`Progress_Density_Minimal`、`Progress_Density_Medium`、`Progress_Density_Full`、`Progress_Stats_Parallel`、`Progress_Batch_SectionTitle`、`Progress_Batch_Count`、`Progress_Entry_Pending`、`Progress_Entry_Active`、`Progress_Toast_Copied`
7. **XAML 最小同步（保构建绿色）**：Avalonia 在**构建期**编译 XAML 绑定，删掉 VM 属性后 `ProgressWindow.axaml` 会立即编译失败，违反本计划「每个任务都能编译」的不变式。故本任务**同批**把 XAML 拉到可编译的最小一致状态（**不做 T7 的结构重排**）：
   - 两个 `RadioButton` 组的 `Command`/`IsChecked` 绑定改指 `ContentMode` / `InfoDensity` 对应成员（选项文案仍是旧文案，T7 再统一）
   - `DirVisible` / `NameVisible` 绑定**直接删除**（D1：恒显示）
   - 旧 6 个 `Progress_Mode_*` / `Progress_Density_*` 绑定改绑到新 key 占位（T11 换文案）
   - `ProgressWindow.axaml.cs` 的 6 个旧 radio 处理器改为写 `ContentMode` / `InfoDensity`；`ApplyDensityMode` 改为读 `InfoDensity`（行距逻辑本身留到 T7 删除）
   - `ProgressViewModelTests.cs` 中依赖旧枚举的用例**本任务即删除**（否则测试项目编译失败）

**Acceptance Criteria**:
- `grep -rn "TopDisplayMode\|DensityMode" src/ tests/` → 零命中
- `ProgressContentMode` / `ProgressInfoDensity` 各 3 成员
- 切 `ContentMode` 三档，三组 `Is*Mode` 派生同时翻转
- `IsDetailedAvailable` 随批次集合增删翻转
- **UI 项目与测试项目此刻均可编译**（10 行布局保持不变，结构重排留给 T7）

**Verification**:
```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
```
→ **0 错误、0 警告**（本任务后构建必须保持绿色，不得留给 T7 修）

---

### Task T6: `SetProgress` 消费逐条目 + 并行度接线

**文件**: `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs`、`Dialogs/ProgressWindow.axaml.cs`、`Services/ExtractFlow.cs`

**改动**:

1. `SetProgress(ArchiveProgress p)` **在最前面**插入条目事件分支（早于所有计数处理，保证条目状态不被百分比节流影响）:
   ```csharp
   if (p.EntryStatus.HasValue && !string.IsNullOrEmpty(p.EntryKey))
   {
       UpdateEntryStatus(p.EntryKey,
           EntryProgressItem.MapEntryStatus(p.EntryStatus.Value),
           null);
       return;   // 逐条目事件独立通道，不参与百分比/速度/ETA 计算
   }
   ```
2. **「当前文件 → Active 行」推导**（列表模式 ⏳n%）：在既有 `if (!string.IsNullOrEmpty(p.CurrentFile))` 分支内，追加按 `Path.GetFileName(p.CurrentFile)` 匹配播种行的推导逻辑，命中则置 `Active`（并把 `p.FilePercentComplete` 写入该行 `Percent`）。**每次只允许一行处于 `Active`**（新行激活时把旧行回落为 `Completed`，若旧行是 `Pending` 则直接替换）。
3. `UpsertParallelBatch` 内追加 `row.CurrentFile = p.CurrentFile ?? row.CurrentFile`。
4. `SetCurrentBatchItem` 内 `_parallelBatchItems.Clear()` 之后：若 `ContentMode == Detailed` 而集合已空，按 D6 回落 `ContentMode = Simple`。
5. `InitBatchMode` 内追加 `_entryItems.Clear()`（新批次不得残留上一批次条目行）+ `ParallelDegree = null`。
6. `ProgressWindow.axaml.cs` 新增 `SetParallelDegree(int degree)` 包装（`DispatchIfNeeded` → VM），并新增 `SeedEntries(IReadOnlyList<(string,string,long)>)` / `ClearEntries()` 包装。
7. `ExtractFlow.cs` 在**已持有 `ProgressWindow` 实例**的方法内（`grep -n "InitBatchMode\|SetBatchPasswordState" src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs` 的同一处），紧随设置 `options.ParallelExtractDegree`（`:174`）之后调用 `pw.SetParallelDegree(degree)`。**非并行路径（degree ≤ 1）传 1 → `HasParallelDegree` 为 false → 卡片与中等单行里的「并行」自动隐藏**（Rule 6）。

**Acceptance Criteria**:
- 逐条目报告**不改变** `PercentComplete`/`SpeedText`/`RemainingText`/`BatchItems` 任何一项
- 未播种时收到逐条目报告 → 自动新建行（D7 渐进模式）
- `degree = 1` → `HasParallelDegree == false`；`degree = 8` → `true`
- 归档切换（`SetCurrentBatchItem`）后 `EntryItems` 为空

**Verification**:
```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
```
→ **0 错误、0 警告**（T5 已把构建拉绿，本任务为纯增量消费逻辑，必须继续保持绿色）；行为覆盖由 T11 的 VM 单测补齐。

---

## Wave 3 — 视图

### Task T7: 布局骨架重写（7 行网格）

**文件**: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`（当前 486 行）

**目标结构**（严格对齐原型 `:504-694` 的自上而下顺序）:

```
Row 0  Auto   批处理区：Border(SectionTitle「压缩包列表」+ BatchCountText | BatchFileList)
Row 1  Auto   内容模式切换：「显示:」+ 简约/详细/列表（详细按 D6 隐藏）
Row 2  *      内容区：简约(SimplePanel) / 详细(DetailedPanel) / 列表(ListPanel) 三者 IsVisible 互斥
Row 3  Auto   状态消息行
Row 4  Auto   下方信息区 Border（底色区别于窗口底色，对齐 .overall-info-section）：
                密度切换（少/中/完整）→ 统计卡(完整) / 中等单行(中) → 总体进度条 → 已用/剩余时间
Row 5  Auto   错误摘要 ErrorSummaryBox（仅有错误时可见）
Row 6  Auto   按钮行（📌 / 暂停 / 取消，保持不变）
```

**本任务范围**: 只做骨架 + Row 4 的密度切换 + 统计卡 + 中等单行 + Row 2 的**简约**面板。详细面板在 T8、列表面板在 T9。

**关键改动**:
1. 删除 Row 0 的旧双单选区（`:45-74`，6 个 RadioButton 全删）
2. **删除旧 Row 1 统计栏 Border**（`:79-120`，纯文本单行）→ 移入新 Row 4 并改为**卡片式**（原型 `.stats-bar` `:425-432`）：`SurfaceBg` 底 + `BorderRadius` + 5 个 `StackPanel`（图标 TextBlock / label / value），图标用 `✅⏭❌🔄⚙` 字形（`Foreground` 取状态画刷）。第 5 张卡绑定 `HasParallelDegree` + `ParallelDegree`，label = `Progress_Stats_Parallel`（D3）
3. **删除旧 Row 2/3（`DirName`/`FileName` 两行）的独立行** → 移入 Row 2 的简约面板（`:536-550` 原型结构：路径行 / 文件名行 / 「文件进度」标签+百分比+进度条）。**`DirVisible`/`NameVisible` 绑定删除**（D1：恒显示）
4. 旧 Row 4 内的**总体进度条 + 批次序号**从简约面板中移出 → 新 Row 4 底部（原型把总进度放在下方区，`:679-687`）
5. 旧 Row 8 的批处理 `ListBox` **整块移到新 Row 0**，并加 `SectionTitle` + `BatchCountText` 表头（原型 `:518-524`）
6. 旧 Row 8 的并行批次 `ItemsControl`（`:390-436`）→ **从批处理区移出**，改名为「详细」面板内容，**留在 Row 2 的位置待 T8 填充**（本任务先保留原样放在原位会导致布局错乱 → **本任务直接把它整体搬进新 Row 2 作为 DetailedPanel，占位即 T8 前的过渡态**）
7. 密度三选绑定 `InfoDensity`，`IsVisible`：`Minimal` → 只显示总进度+时间；`Medium` → 加 `MediumStatsPanel`；`Full` → 加 `StatsCardsPanel`（原型 JS `:1117-1131` 的互斥语义）
8. `RootGrid.RowSpacing` 改为固定 `{DynamicResource SpacingXs}`（**删除 `ApplyDensityMode` 的动态行距**——D1：间距回归全局紧凑度单一来源，**符合 Rule 5，不再硬编码**）
9. 所有新增控件套用全局类样式与主题画刷（`Theme*Brush` 结尾），**禁止系统默认色**（Rule 4）
10. 每个主要区域加**中文注释**（Rule 14）

**Acceptance Criteria**:
- `RootGrid.RowDefinitions.Count == 7`
- `grep -c "TopDisplayMode\|DensityMode" ProgressWindow.axaml` → 0
- 切换密度三档，统计卡/中等单行/时间行按原型语义互斥显隐
- 简约面板路径行 + 文件名行 + 文件进度条三者齐备；总进度条**不在**简约面板内

**Verification**:
```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
```
→ **0 错误、0 警告**（本任务后 XAML 与 VM 重新对齐，旧的编译错误应全部消失）

---

### Task T8: 详细模式面板

**文件**: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`

**改动**: 把 T7 搬入的并行批次 `ItemsControl` 改造为原型 `.thread-item`（`:365-378`）结构，并受 `IsDetailedMode` 控制显隐:
- 行结构: `批次 N`（`ParallelBatchProgressItem.Index`）| 当前文件名（**新增 `CurrentFile`**，T5 已加）| 批次进度条 | 百分比
- 「批次 N」文案：新增 key `Progress_Batch_Label`（值 `批次`），与 `Index` 并排两个 `TextBlock`（避免 `StringFormat` 转义，与现有 `#` 前缀写法一致）
- 容器 `IsVisible="{Binding IsDetailedMode}"`；`详细` 单选项 `IsVisible="{Binding IsDetailedAvailable}"`（D6）
- `CurrentFile` 为空时该 `TextBlock` 用 `StringNotEmptyConverter` 隐藏（批次刚起还没上报文件）
- 行高 `MinHeight="{DynamicResource ControlHeightSm}"`（Rule 7）

**Acceptance Criteria**:
- `详细` 单选项在 `ParallelBatchItems` 为空时不可见；集合非空时可见
- 批次行显示 `批次 N` + 文件名 + 进度条 + 百分比四段
- `CurrentFile` 缺失时文件名段不显示且不报布局异常

**Verification**: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误

---

### Task T9: 列表模式面板（风险最高）

**文件**: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`、`ViewModels/ProgressViewModel.cs`、`Services/ExtractFlow.cs`

**改动**:

1. **XAML**：Row 2 新增 `ListPanel`（`IsVisible="{Binding IsListMode}"`），内含虚拟化 `ListBox`：
   - 列结构严格对齐原型 `.file-item`（`:382-387`）：`状态图标 | 文件名(flex:1, Ellipsis) | 大小(右对齐, 60px) | 状态文案(右对齐)`
   - **虚拟化必做**：`ListBox` 默认虚拟化；`ItemContainerTheme` 设 `MinHeight="{DynamicResource ControlHeightSm}"`（Rule 7）；外层包 `ScrollViewer` 设 `MaxHeight`（约 150px，对齐原型 `.file-list` max-height）
   - 行背景按状态着色（原型 `.file-item.error` 红底 / `.skipped` 半透明）：用 `ProgressStatusBackgroundConverter` 或新增 `EntryRowState` → 画刷映射（**用 `StatusBrushName` + `BrushResourceConverter` 统一机制**，不新增转换器）
   - 状态文案：`Completed`→`Progress_Stats_*` 复用、`Skipped`/`Failed`/`Overwritten` 复用、`Pending`→`Progress_Entry_Pending`、`Active`→`{Percent:F0}%`
2. **播种接线（D7）**:
   - 解压路径：在 `ExtractFlow` 已持有 `ProgressWindow` 处，若引擎 `SupportsParallelExtract` 或格式为 zip/7z → 调 `engine.ListEntriesAsync` 播种（`EntryKey = item.Key`，`Name = Path.GetFileName(item.Key)`，`Size = item.Size`），**播种前检查 `count > 5000` 则跳过播种**
   - **TAR/GZ 一律不播种**（`ListEntriesAsync` 全流扫描）
   - 压缩路径：用调用方已知的源文件列表播种
   - 播种必须在后台线程完成后 `SeedEntries`（内部已 `DispatchIfNeeded`）
3. **渐进模式**：TAR/GZ 或超 5000 条时，行随 `UpdateEntryStatus` 首次收到报告时创建（状态直接为 `Completed`/`Failed`…，无 `Pending`/`Active` 视觉态）

**Acceptance Criteria**:
- 列表模式显示 4 列：图标 / 文件名 / 大小 / 状态
- 10 万条目 ZIP（超阈值）→ 不播种、不卡 UI，操作可正常完成
- TAR/GZ 解压切列表模式 → 行随进度渐进出现，无空白等待行，**总耗时与不切列表模式相比无可测量差异**
- 已完成/跳过/出错/覆盖四种终态各行状态图标、颜色、文案正确

**Verification**:
```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```
→ 0 错误；测试全绿（此时 `ProgressWindowXamlTests` 仍断言 10 行 → **本任务后该断言必须已修**，故 T11 的行数断言修改**前移**到本任务末尾执行）

---

### Task T10: 失败行内错误 + 复制反馈 + 徽标入场动画

**文件**: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`、`.axaml.cs`

**改动**:

1. **失败行内错误消息**（补当前完全缺失的绑定）：批处理行第 4 列改为「失败时显示红色错误消息，非失败时显示进度百分比」——用两个 `TextBlock` + 互斥 `IsVisible`（`Status == Failed` 用 `IsVisible="{Binding IsFailed}"`，需在 `BatchItem` 加 `IsFailed` 派生属性 + 集中通知）：
   - 错误文本 `Text="{Binding ErrorMessage}"`，`Foreground="{DynamicResource ThemeStatusErrorBrush}"`，`FontSize=11`，`MaxWidth` 60%，右对齐，`TextTrimming="CharacterEllipsis"`（原型 `.batch-error` `:121-130`）
2. **复制 toast**：`PwdFlyoutCopyBtn_Click` 成功后：
   - 按钮图标短暂切换为 ✓（1.2s 后复原，对齐原型 `:1011`）
   - 窗口底部弹出 toast「已复制到剪贴板」（`Progress_Toast_Copied`，1.6s 后移除，对齐原型 `:1066-1073`）
   - toast 用 `DispatcherTimer` 生命周期管理，**关闭时清理**（防泄漏）
3. **密码徽标入场动画**：`PasswordState` → `Matched` 瞬间播放 0.25s 缩放+淡入（原型 `.badge-enter` `:172-176`）。实现：Avalonia `Transitions`（`ScaleTransition` + `OpacityTransition`，`Duration=0:0:0.25`）+ code-behind 在 `SetBatchPasswordState` 包装里对命中行触发动画（**不逐行写死动画属性**，用一次性的 `Transitions` 切换实现，避免为动画引入新的 VM 状态）

**Acceptance Criteria**:
- 批处理行失败时显示红色错误消息、非失败时显示百分比，两者互斥
- 点击复制 → 按钮变 ✓ + toast 出现，1.2s/1.6s 后各自复原
- 窗口关闭后无残留计时器（`OnClosed` 清理）
- 密码徽标点亮有 0.25s 淡入缩放

**Verification**: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误

---

## Wave 4 — 测试与文案

### Task T11: 测试重写 + 三语文案同步

**文件**: `tests/MantisZip.UI.Avalonia.Tests/ProgressWindowXamlTests.cs`、`ProgressViewModelTests.cs`、`Localization/strings.{zh-CN,zh-TW,en}.json`

**改动 — 测试**:
1. `ProgressWindowXamlTests.cs:35`：`Assert.Equal(10, ...)` → `Assert.Equal(7, ...)`，注释更新为「原型对齐后 7 行结构」
2. **删除** `TopDisplayMode_TogglesPathAndNameVisibility`（`:152-175`）——枚举已删
3. **新增 6 个用例**:
   - `ContentMode_TogglesThreeDerivedVisibility`：三档切换，`IsSimpleMode`/`IsDetailedMode`/`IsListMode` 互斥
   - `InfoDensity_TogglesStatsAndMediumPanels`：三档切换对应派生可见性
   - `SetProgress_WithEntryStatus_CreatesRowWhenNotSeeded`：未播种收到条目报告 → `EntryItems.Count == 1` 且状态正确（D7 渐进）
   - `SetProgress_WithEntryStatus_UpdatesSeededRowWithoutDuplicating`：播种 3 行 → 3 次条目报告后仍为 3 行（**去重回归**）
   - `SetProgress_WithEntryStatus_DoesNotAlterPercentOrSpeed`：条目报告不污染总进度/速度文本
   - `ParallelDegree_HidesParallelStat_WhenDegreeIsOne`：`SetParallelDegree(1)` → `HasParallelDegree == false`；`8` → `true`
4. `ProgressViewModelTests.cs`：删除依赖 `DensityMode`/`TopDisplayMode` 的用例；如有 `DirVisible`/`NameVisible` 断言一并清理
5. 保留全部既有密码徽标/批次行/ETA 用例（`:44-150`、`:177-195`）**逐字不动**（回归保护）

**改动 — 三语文案**（Rule 13）:
- 新增 32 key 到**三语文件**（插到文件头 `{` 之后，UTF-8 无 BOM + CRLF + 2 空格缩进）:

| key | zh-CN | zh-TW | en |
|---|---|---|---|
| `Progress_Batch_ArchiveOf` | {0} / {1} | {0} / {1} | {0} / {1} |
| `Progress_Batch_Count` | {0} 个压缩包 | {0} 個壓縮檔 | {0} archives |
| `Progress_Batch_FilesProgress` | {0}/{1} 文件 | {0}/{1} 個檔案 | {0}/{1} files |
| `Progress_Batch_Label` | 批次 | 批次 | Batch |
| `Progress_Batch_Pwd_Desc` | 描述 {0} | 描述 {0} | Description {0} |
| `Progress_Batch_Pwd_MatchedTip` | 已匹配密码，点击查看 | 已匹配密碼，點擊查看 | Password matched, click to view |
| `Progress_Batch_Pwd_Matching` | 匹配中 | 匹配中 | Matching |
| `Progress_Batch_Pwd_Rule` | 规则 {0} | 規則 {0} | Rule {0} |
| `Progress_Batch_SectionTitle` | 压缩包列表 | 壓縮檔列表 | Archive list |
| `Progress_CurrentFileLabel` | 当前文件 | 目前檔案 | Current file |
| `Progress_Density_Full` | 完整 | 完整 | Full |
| `Progress_Density_Medium` | 标准 | 標準 | Medium |
| `Progress_Density_Minimal` | 精简 | 精簡 | Minimal |
| `Progress_Entry_Active` | 进行中 | 進行中 | In progress |
| `Progress_Entry_Completed` | 已完成 | 已完成 | Completed |
| `Progress_Entry_Failed` | 失败 | 失敗 | Failed |
| `Progress_Entry_Overwritten` | 已覆盖 | 已覆蓋 | Overwritten |
| `Progress_Entry_Pending` | 等待中 | 等待中 | Pending |
| `Progress_Entry_Skipped` | 已跳过 | 已跳過 | Skipped |
| `Progress_Mode_Detailed` | 详细 | 詳細 | Detailed |
| `Progress_Mode_Label` | 显示： | 顯示： | Show: |
| `Progress_Mode_List` | 列表 | 列表 | List |
| `Progress_Mode_Simple` | 简约 | 簡約 | Simple |
| `Progress_Stats_Failed` | 出错 {0} | 出錯 {0} | Failed {0} |
| `Progress_Stats_Overwritten` | 已覆盖 {0} | 已覆蓋 {0} | Overwritten {0} |
| `Progress_Stats_Parallel` | 并行 | 並行 | Parallel |
| `Progress_Stats_Processed` | 已处理 {0} | 已處理 {0} | Processed {0} |
| `Progress_Stats_Skipped` | 跳过 {0} | 跳過 {0} | Skipped {0} |
| `Progress_Stats_Speed` | {0}/s | {0}/s | {0}/s |
| `Progress_Time_Elapsed` | 已用 {0} | 已用 {0} | Elapsed {0} |
| `Progress_Time_Remaining` | 剩余 {0} | 剩餘 {0} | Remaining {0} |
| `Progress_Toast_Copied` | 已复制到剪贴板 | 已複製到剪貼簿 | Copied to clipboard |

- T8 追加的 `Progress_Batch_Label` 等条目已并入上表（上表为最终 32 key 全集）
- **净删除 0 个 key**。计划原先点名的 6 个旧 key（`Progress_Mode_FullPath`、`Progress_Mode_DirOnly`、`Progress_Mode_NameOnly`、`Progress_Density_Compact`、`Progress_Density_Normal`、`Progress_Density_Loose`）经基线 `9551e18` 核对**在本任务开始前就已不存在**，故非本任务删除项；保留零命中断言作为回归保护
- 三语 key 集必须完全一致（`AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 会校验）

**Acceptance Criteria**:
- `dotnet test` 两套全绿
- 三语 key 集一致；32 新增全部到位（三语各 1257 key，基线 1225 → 1257）
- `grep -rn "Progress_Mode_FullPath\|Progress_Density_Compact" src/ tests/` → 零命中

**Verification**（**必须顺序执行，禁止并行**）:
```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```

---

## Final Verification

### F1: 构建

```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
```
→ 均 0 错误、0 警告

### F2: 测试（顺序执行）

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```
→ 全绿，失败数为 0（基线 544+5 / 104+6）

### F3: 交互式手工 QA（对照原型逐项勾选）

> **本轮已用 UI Automation + 真实鼠标点击（SendInput）+ 像素采样完成可自动化部分**（2026-10-04，
> 300MB × 1MB 数据集，`--compress-quick` 驱动）。标记含义：**[x] 已客观验证** / **[~] 部分验证** /
> **[ ] 未验证**。**纯观感项（配色美观度、动画流畅度、裁切/对齐观感）无法由自动化判定 —— 本次会话的
> 模型不具备图像输入，仍需人眼过一遍。**

逐项操作并与 `docs/prototypes/progress-window-enhancement.html` 实际点击对照:

- [x] 内容模式切换生效：简约 `elements=37/listItems=1` → 真实点击「列表」→ `elements=74/listItems=8`，回简约恢复 `39/1`（面板双向切换正常）
- [x] 详细模式：**负向已验证**（单压缩包压缩场景仅 `简约`/`列表` 两个 RadioButton）；**正向已验证**（100k 条目并行解压场景 `详细=False` 出现于 radio 组，即可见可用）
- [◐] 列表模式：**压缩路径已验证**（ZIP 逐行转态 `✅ \| file_000.bin \| 已完成`；300 文件仅暴露 8 行 + 滚动条 → 虚拟化生效）；**ZIP 解压 CLI 路径播种 ✅ 已证实（`SizeText` 判据）** —— 两个直连引擎的 CLI 叶子均已接线（见待办 1）。判据：行第 3 列绑 `EntryProgressItem.SizeText`，`SizeText => _size > 0 ? FormatSize(_size) : ""`（`Core/Models/EntryProgressItem.cs:88`）；`SeedEntryItems` 建行设 `Size`，`UpdateEntryStatus` 的 upsert 兜底建行**不设** `Size` ⇒ 兜底行 `SizeText` 恒空。实测 `--extract-here` 单文件 51 条目 ZIP（1×1000MB + 50×1MB）渲染出 `big.bin | 1000 MB | 40%`、`small_000.bin | 1 MB | 已完成`，`SizeText` 非空 ⇒ 行必来自 `SeedEntryItems`。此前「归档顺序无法区分播种与 upsert」的顾虑已由该判据取代；**TAR/GZ 进度窗口已验证存在**（`t.tar.gz` 于 t+1.7s 出现真实 `ProgressWindow`，300/300 解压成功、退出码 0），但**列表模式下的渐进建行仍未验证**（该轮窗口停在默认「简约」模式，无列表可测）；**10 万条目 UI 流畅度未验证**
- [~] 逐文件状态 6 态：条目级已观察到 **`✅ 已完成`** 与 **`⏳n%`**（51 条目 ZIP 探针中 `big.bin` 行渲染 `⏳ | big.bin | 1000 MB | 40%`，同一屏内 6 个小文件行为 `✅ 已完成`，证明播种行 + 原地转态同时生效）；`○等待 / ⏭跳过 / 已覆盖` 仍未取证——其中 `○等待` **已判定为结构性不可采**（并行按体积降序启动，列表头部恒为已启动条目，Pending 恒在视口外，且虚拟化 ListBox 仅实体化约 7–8 行），**不要再以「未捕到 ○等待」为缺口**；`⏭跳过 / 已覆盖` 需冲突场景构造。**批次级 `❌ 出错` 已验证**（见下条）
- [x] 下方密度三档：`精简` 隐藏「已处理」→ `标准` 出现「已处理 N」→ `完整` 拆为 `已处理` + `165` 独立标签/值（即 5 张卡布局，非缺陷）
- [✅] 「并行」卡：**负向已验证**（压缩场景正确不出现）；**CLI 解压路径正向已证实** —— 此前 100k 条目并行解压 + `完整` 密度下逐条枚举全部 14 个文本元素，`并行` / `批次` 关键词均为空（非精确匹配假阴性，已改为子元素全量枚举），根因同待办 1（两个 CLI 直连叶子未接入并行度元数据）；修复后同一路径采到 `⚙ / 并行 / 20`，数值与 `ParallelExtractDegree`=20 及 `ProcessorCount` 一致，**构成决定性运行时证据**（修复前 `HasParallelDegree=false`，该卡不可能渲染）。主窗口解压路径的并行卡正向仍未取证
- [~] 批处理列表位于窗口**上方**：**位置已验证**（几何 `压缩包列表 y=520` / `BatchFileList y=541` / 内容模式 y=580 / 密度 y=727）；**失败行错误消息已验证** —— 4KB 随机字节伪装 `corrupt.zip`，批次行渲染 `❌` + **行内错误消息 `Failed to locate the Zip Header`** + 统计 `完成 0 项，失败 1 项`，证明 T 项「`ErrorMessage` 只写不显示」已修复；**红色**需人眼确认
- [~] 密码徽标：**`🔄`（`BatchPasswordState.Matching`）已实测出现在批次行**（伴随 `●●●●`）；未捕获 `🔑`（Matched）与熄灭（None）终态 —— 非加密包的匹配过程瞬态即逝；入场动画与 Flyout/复制 toast 仍需加密包场景 + 人眼
- [x] 标题栏**无**密度切换器（D4）：TitleBar 子元素仅 `SystemMenuBar` / `Minimize` / `Maximize` / `Close`
- [x] 明暗主题均有主题色（Rule 4）：像素直方图主色与声明调色板**逐一吻合**，无系统默认蓝
      - Light：`#F5F5F5` 52.5% = `ThemeWindowBg`、`#FFFFFF` 26.0% = `ThemeSurfaceBg`、`#E0E0E0` 10.6% = `ThemeButtonBg`、`#F0F0F0` = `ThemeHeaderBg`、`#1A1A1A` = `ThemeTextPrimary`
      - Dark：`#1E1E1E` 53.6% = `ThemeWindowBg`、`#252525` 19.4% = `ThemeSurfaceBg`、`#3E3E3E` 10.5% = `ThemeButtonBg`、`#E0E0E0` = `ThemeTextPrimary`
- [x] 三档紧凑度行高走资源键（Rule 7，实测）：批次行 **28 / 32 / 38**（= `ControlHeightMd`），条目行 **22 / 26 / 30**（= `ControlHeightSm`），三档全部随设置缩放，**无硬编码行高**
- [x] 压缩与解压两条路径：**均已验证**。压缩路径进度真实推进（已处理 31→205、17%→56%，逐条目遥测生效）；解压路径经 `--extract-here` 于 1.85s 捕获到真实 `ProgressWindow`
      - TAR/GZ 亦已验证：300 文件 `t.tar.gz` 经 `--extract-here` 于 t+1.7s 出现 `ProgressWindow`（标题「正在解压…」），300/300 解压成功、退出码 0
      - 取证方法教训：30k 条目那轮报「NO WINDOW」是**测试假象**（单实例 mutex 把参数转交给残留进程，按 pid 枚举自然查不到窗口），非产品缺陷；后续脚本须先确认进程已退出再判定
      - 注意：`--extract <path>` 会遵循 `AppSettings.ExtractDestination`（默认 `ask`）而停在目录选择对话框上，取证须用 `--extract-here`（目标目录 = 压缩包所在目录，且无对话框）

#### 本轮自动化取证发现的待办

**功能缺口（建议修复）**

1. ~~**CLI 解压路径未接入 `ExtractFlow` 的逐条目播种与并行度元数据**~~ **✅ 已修复**（「并行」卡运行时已证实；播种 ◐ 强证据、缺决定性证据）。
   `ProgressWindow.SeedEntries` / `SetParallelDegree` 仅由 `ExtractFlow` 建立，而 CLI 解压叶子**直连
   `engine.ExtractAsync`**、绕过 `ExtractFlow.ExtractAsync`。同一根因导致两处可观测缺失：
    - **ZIP 列表模式为空**：切列表模式后实测 `listItems=1`、等待/已完成计数均为 0（条目行从未播种）。
      **该结论仅对 ZIP 成立** —— TAR/GZ 因 D7 本就不播种、行走渐进建行，其列表模式行为尚未取证。
    - **「并行」卡恒不出现**：`完整` 密度下逐条枚举全部文本元素也无 `并行` / `批次`（该卡取值依赖
      `ExtractFlow` 建立的并行度元数据）。

   **⚠️ 原记录的范围有误（已修正）**：缺口不只在多文件批处理叶子。凡是**直连 `engine.ExtractAsync`** 的
   CLI 叶子都未接线，实为**两处**，修一处只能覆盖一半：
    - `RunCliExtractWithProgressAsync`（`App.axaml.cs:1083`，"叶子 5/5"）—— **单文件**叶子，
      `--extract-here` / `--extract-to-name` / `--extract-smart` 在**恰好 1 个压缩包**时走这里
      （路由分叉见 `App.axaml.cs:249-254`：`Count == 1` → `RunExtractCliAsync` → 本方法；
      `Count > 1` → 批处理叶子）。**上一轮自动化取证正是用单包做的，因此实际命中的就是这一条**，
      这解释了为何最初只盯着批处理叶子会「改完仍无变化」。
    - `RunCliDirectExtractBatchAsync`（`App.axaml.cs:1415`）—— **多文件**叶子（ShellExt 多选）。

   已实施（用户 2026-10-04 确认补齐）：把 `ExtractFlow.ResolveDisplayParallelDegree` 与
   `TrySeedEntryItemsInBackground` 由 `private` 放宽为 `internal` 复用，并在**两个叶子**内于解压启动前
   接线：单文件叶子复用 `InitBatchMode`/`SetCurrentBatchItem` 已做的清空，多文件叶子额外
   `ClearEntries()`（`InitBatchMode` 每批只清一次，逐包必须清，否则第 2 包会显示第 1 包的行）、
   并行度**逐包重算**（非 zip/7z 返回 1 → `HasParallelDegree=false` 自动隐藏该卡）。
   验证：构建 0 警告 0 错误；Avalonia 测试 114/0/2（新增 5 条播种路径用例，见下）、Core 549/0/3。
   **运行时复验（2026-10-04 已执行，结论分项）**：
   - ✅ **「并行」卡已证实**。`--extract-here` 单文件（15,000 × 32KB ZIP，`ParallelExtractDegree=20`）
     切「完整」密度后，UIA 全量枚举文本元素得到 `⚙ / 并行 / 20`——数值 20 与
     `Environment.ProcessorCount` 及设置值一致。修复前 `HasParallelDegree=false`，该卡**不可能**渲染，
     故此项构成 `SetParallelDegree` 接线的决定性运行时证据（同一叶子内的姊妹接线）。
   - ◐ **播种已强证据支持，但未取得决定性证据**。列表模式确实出现条目行（单测同路径另有 5 条覆盖）。
     两次对照：15,000 × 32KB 采到 `f_000007, f_000010, f_000003, f_000001…`（**散乱 = 完成顺序**，
     与未播种时的 upsert 兜底一致）；60 × 6MB 采到 `f_000000…f_000005`（**严格归档顺序**，且
     `已处理 60`/`100%` 时 60 行齐全）。但**归档顺序不足以区分播种与 upsert**——60 个等大 6MB 文件的
     完成顺序本就≈归档顺序。决定性证据应是 `○等待`（Pending）行：**只有播种才会产生**，而两轮采样
     均未捕获（32KB 文件瞬时完成、6MB 文件被 20 线程并发直接推入 `⏳ Active`）。
     故播种接线**不能宣称已闭环**，建议构造「单条超大 + 多条小文件」并在 t≈0.2s 采样以暴露 Pending 行。
   - ⚠️ **探针自身两处坑（已修正，勿重蹈）**：
     ① 输出目录若复用且残留上一次解压产物，会触发模态冲突框把窗口冻在 0%，导致「窗口瞬现即灭、
     轮询抓不到」——必须**每次用唯一目录**；
     ② 用 `ControlType` 的 `PropertyCondition` 枚举顶层窗口**匹配不到**本应用的 `ProgressWindow`，
     须改用 `TrueCondition` + `ProcessId`/`ClassName` 过滤（`x:Name` 确实映射为 `AutomationId`，这部分可用）。
   - ⚠️ 另记一次**自造假象**：某版探针把 `activeRowsVisible` 报成 47，但 dump 里 `⏳` 实际为 0 个——
     47 是元素总数漏进正则匹配。该指标已作废，不可作为「多行 Active」证据。

**非阻塞瑕疵**

2. **AutomationId 仍用旧语义命名**：`ModeFullPathRadio` / `ModeDirOnlyRadio` / `ModeNameOnlyRadio` /
   `DensityCompactRadio` / `DensityNormalRadio` / `DensityLooseRadio`。枚举类型 `TopDisplayMode` / `DensityMode`
   确已删除，但 `x:Name` 未随之改名 —— 三个内容模式 RadioButton 仍叫 `FullPath` / `DirOnly` / `NameOnly`，
   与现行标签「简约 / 详细 / 列表」及枚举 `TopDisplayMode` 的语义脱节，与「旧命名彻底删除」的字面表述不符
   （仅内部 AutomationId，用户不可见，但会持续误导自动化测试与后续维护）。
3. **`PauseButton` / `CancelButton` 的可访问名**为 `Avalonia.Controls.StackPanel`（Button 内容 StackPanel 被当作
   Name），实际文案在子 `PauseButtonText` / `CancelButtonText`。屏幕阅读器会读出类型名而非「暂停」/「取消」。

#### 自动化方法论备忘（供后续复现）

- RadioButton 用 **`Click=` 事件处理器**而非 `Command=` 绑定，故 UIA `SelectionItemPattern.Select()` **只改视觉选中态、不触发 Click**，会得出「切换器失效」的假结论；必须用 `SetCursorPos` + `mouse_event` 真实点击。
- ProgressWindow 模态冲突对话框（`目标文件 "X" 已存在`）会冻结在 0%，使元素计数在所有模式下完全相同 —— 取证前务必清理目标 archive。
- 并行/串行解压窗口存活远小于 1s，需极小轮询间隔或超大数据集才能捕获。

### F4: 性能基准（D8 兑现，用户明确关切）

在同一台机器、同一数据集下对比改造前后（各跑 3 次取中位数）:

| 场景 | 数据集 | 判据 |
|---|---|---|
| ZIP 并行解压 | 100 × 1MB 随机数据，`ParallelExtractDegree = 8` | 中位数耗时 **回退 ≤ 3%**（逐条目上报为每文件 1 次轻量 `Report`，理论开销 < 0.1%） |
| ZIP 串行解压 | 同上，`ParallelExtractDegree = 1` | 回退 ≤ 3% |
| 7z 解压 | 100 × 1MB | 回退 ≤ 3% |
| ZIP 压缩 | 100 × 1MB | 回退 ≤ 3% |
| **列表模式开关对比** | 同上，切/不切列表模式 | 差异 ≤ 3%（虚拟化 + 后台播种已保证） |
| **TAR/GZ 列表模式** | 同上 | **不得因播种触发全流扫描**；确认走渐进模式，总耗时与不开列表一致 |

若任一项回退 > 3%，定位是否违反 D8（锁内 `Report`）并回修。

#### F4 实测结果（已执行）

基线：独立 `git worktree` 于改造前提交 `9551e18`（detached，`C:\...\opencode\mz-baseline`，13 行旧布局、无 `ProgressContentMode`）。
数据集：`100 × 1MB` 固定种子随机数据（两侧字节一致），每项 3 轮 × 7 次 = 每侧 21 样本，**两侧交替运行且每轮翻转先后顺序**以抵消热与缓存漂移；判定**仅用中位数**（判据见上表），显著性检验不作为判据。

| 场景 | baseline | current | 中位数差 | 判定 |
|---|---|---|---|---|
| ZIP 串行解压（degree=1） | 249.1ms | 247.8ms | −0.5% | ✅ PASS |
| ZIP 并行解压（degree=8，`progress=null`） | 77.6ms | 78.8ms | +1.5% | ✅ PASS |
| ZIP 并行解压（degree=8，仅 `Interlocked` 消费者） | 83.0ms | 81.2ms | −2.2% | ✅ PASS |
| 7z 解压 | 3887.7ms | 3800.4ms | −2.2% | ✅ PASS |
| ZIP 压缩 | 4443.4ms | 4390.5ms | −1.2% | ✅ PASS |
| 列表模式 关 vs 开（并发播种语义） | 80.2ms | 80.8ms | +0.7% | ✅ PASS |

**结论：F4 通过，全部场景回退 ≤ 3%，D8（锁外 `Report`）兑现。**

**判据表第 6 行「TAR/GZ 列表模式」未做基准 —— 由 D7 静态门控保证，强于基准：**

- **不得因播种触发全流扫描**：由代码结构**静态保证**，无需测量。`CanSeedEntries`（`ExtractFlow.cs:319-327`）对 `engine is TarGzEngine` 直接返回 `false`；`TrySeedEntryItemsInBackground`（`:363-364`）随即提前返回，注释明示「TAR/GZ / 非 zip/7z：不产生任何 `ListEntriesAsync` 调用」；其后的格式兜底（`:325-326`）亦仅放行 `Zip` / `SevenZip`。故 TAR/GZ **在任何列表模式状态下都不存在播种调用**，开关列表模式时核心解压路径逐字节相同。
- **总耗时与不开列表一致**：仅对 **Core 解压吞吐**成立（见上）。列表模式**开启**时 TAR/GZ 仍会经 `UpdateEntryStatus`（`ProgressViewModel.cs:659`）逐条 upsert 建行（渐进模式，由 `TarGzEngine` 的 2 处埋点驱动），这部分 **UI 侧**开销未做基准，与「10 万条目不卡 UI」同属 **F3 GUI 响应性**范畴。
- 基准侧的等价证据：列表模式 ZIP 对照（并发播种语义）为 `+0.7%`，说明播种/建行路径本身的量级在噪声内。

**两处必须记录的测量陷阱（曾产生假回退，均为基准自身缺陷而非代码缺陷）：**

1. **多线程共享字段消费者 → 伪共享放大。** 初版基准的消费者在 8 个工作线程上写 4 个共享实例字段，测得并行解压 `+10.1%`，但该结果**不可复现**（复跑为 `+4.8%`，且跨轮波动远大于 `+1.5%` 的真实差异）。真实 UI 消费者 `ProgressViewModel.BackgroundDispatcherProgress`（`ProgressViewModel.cs:964`）是 `Dispatcher.UIThread.Post(...)` **入队**，工作线程侧不写共享字段，故无此争用。改用 `progress=null`（纯 Core 成本）与仅 `Interlocked` 的消费者后，回退分别为 `+1.5%` 与 `−2.2%`。**判定并行解压回退必须以无共享字段消费者为准。**
2. **列表模式必须按 fire-and-forget 语义建模。** 初版代理在解压前 `await ListEntriesAsync`（串行计费），测得 `+7.3%`；但 `ExtractFlow.TrySeedEntryItemsInBackground`（`ExtractFlow.cs:366`）实为 `_ = SeedEntryItemsAsync(...)` 不 await，播种与解压**并发**。改为并发建模后为 `+0.7%`。

> ⚠️ **不引用显著性检验作为判据。** 基准脚本 `analyze-f4.ps1` 的 `Get-NormCdf` 实际返回的是 **erf** 而非标准正态 CDF（缺 `/√2` 与 `1/2`），导致 p 值失真、部分结果 `p > 1`。本计划的判据是**中位数回退 ≤ 3%**（见上表），不依赖 p 值；脚本与 p 值在修正前**不得引用**，`+10.1%` 与 `+7.3%` 两个"显著回退"结论也一并作废（其成因已由上两点定位为基准缺陷）。

> 备注：逐条目上报相对基线多约 100 次 `Report`/100 文件（D2 要求，基线仅节流后约 1–8 次）。其真实代价是 UI 线程侧多 100 次 dispatcher 回调与行 upsert，属 **UI 响应性**范畴（100k 条目由 F3 GUI 验证），不属于 F4 的解压吞吐判据。

---

## Deliverable Acceptance

> **当前状态：F1 / F2 / F4 已通过；F3（交互式手工 QA）待用户执行，故整体尚未收口。**

- [ ] F1–F4 全部通过（性能回退 ≤ 3%）— **F1 ✅ / F2 ✅ / F4 ✅（六项判据：5 项实测中位数达标，第 6 项「TAR/GZ 列表模式」由 D7 静态门控保证无播种调用，其 UI 侧渐进建行开销归 F3），F3 ⏳ 待用户 GUI 验收**
- [x] 原型三组语义全部落地：内容模式（简约/详细/列表）+ 信息量分级（少/中/完整）+ 列表模式 6 态
- [x] 旧双开关（`TopDisplayMode`/`DensityMode`）彻底删除，全仓库零引用
- [x] 无任何假统计：第 5 张卡是真实并行度，详细模式标签是「批次」而非「线程」
- [x] 三语文案 key 集一致（32 新增 / 净删除 0；三语各 1257 key）
- [x] 全部测试通过（含 11 个新用例）— Avalonia 109/0/2、Core 549/0/3
- [ ] 中文注释齐备（Rule 14）、主题色齐备（Rule 4）、间距走资源键（Rule 5）、列表行高走资源键（Rule 7）— 结构门禁已过（7 行布局 / 3 面板 / 无 ScrollViewer / 32 key 全注册）；**T9b 状态底色与徽标缩放动画的观感留待 F3 判定**

---

## Deferred（明确不做）

> **2026-10-07 整理注**：#2/#3 已转入 [progress-window-bytes-i18n.md](progress-window-bytes-i18n.md)，#7 已实施（不再是 Deferred）。仍为「明确不做」的仅：标题栏密度切换器、`.speed-display` 死样式、场景演示控制条、Size 格式化缓存。

- **标题栏密度切换器**：原型自身 JS 未接线（死 UI），实现点不动的控件无意义（D4）
- ~~**`LocalizedStrings` 语言切换刷新**~~ → **已转入 [progress-window-bytes-i18n.md](progress-window-bytes-i18n.md) 任务 5**（`ProgressViewModel` 订阅 `CultureChanged` + `OnClosed` 退订防静态事件泄漏；2026-10-07 标注，原判断「本次不修」仍适用于本计划）
- ~~**7z / TAR 的速度与 ETA**~~ → **已转入 [progress-window-bytes-i18n.md](progress-window-bytes-i18n.md) 任务 1–4**（引擎全路径填充 `ProcessedBytes`/`TotalBytes`；2026-10-07 标注，列表模式不改变此现状）
- **`.speed-display` 死样式**：原型无对应元素，不实现
- **原型场景演示控制条**（`scenario-bar`）：原型辅助 UI，非窗口本体
- **`EntryProgressItem` 大文件的 Size 格式化缓存**：`FormatUtil.FormatSize` 每次调用，当前规模无需缓存
- ~~**通用压缩路径的列表模式播种（用户已决定正式延期）**~~ → **已实施（commit `7505952`，2026-10-07）**：新增 `Core/Services/SourceEntryEnumerator`（复用 `FileScanner`，Key 与引擎 `EntryKey` 同源），`CompressFlow.TrySeedEntryItemsInBackground`（`CompressFlow.cs:332`）接线主窗口（`MainWindow.axaml.cs:277`）与 CLI（`App.axaml.cs:2233`）两条压缩路径，列表显示全部条目（Pending）并随终态更新。**历史记录**：当初延期是因压缩路径要拿全量条目必须先列目录再压缩——对 TAR/GZ 意味着全流扫描（D7 明令禁止）、对 zip/7z 多一次 `ListEntriesAsync` I/O；后以「公开枚举器按需取用」方式解决（原调查结论「ZIP 3 处 + 7z `AttachCompressorEntryTelemetry` + TAR/GZ 2 处均已上报 `EntryStatus`」仍成立）。**解压路径的播种自本计划起即已完成，未受影响。**
