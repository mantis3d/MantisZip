# ProgressWindow 总进度条分段着色（按压缩包状态）

## TL;DR

> **Quick Summary**: 将 ProgressWindow 的总进度条（Row 4）从普通 `ProgressBar` 替换为自定义 `SegmentProgressBar` 控件，按批处理项状态将进度条分成 N 段并着色：失败段红色、成功段绿色、跳过段青色、进行中段蓝色（按该项进度填充）、未处理段背景色，段间以**动态预算制缝隙**（gap = min(2px, 条宽×15%/(N-1))）分隔，大批次数时缝隙自动收缩至不可见，内容不被挤占。所有模式（含单文件）均分段。
> 
> **Deliverables**:
> - 新增 `src/MantisZip.UI.Avalonia/Controls/SegmentProgressBar.cs` 自定义绘制控件
> - `ProgressWindow.axaml` Row 4 总进度条替换为 SegmentProgressBar
> - `Converters/BatchStatusConverters.cs` 抽取公共不透明颜色映射 `GetColor()`
> - `Services/DragDropService.cs` 失败路径补齐批处理状态上报
> - 单元测试：颜色映射 / 段区间计算 / 退化场景（count==0/1）
> 
> **Estimated Effort**: Medium
> **Parallel Execution**: YES - 2 waves + final review wave
> **Critical Path**: Task 1 (颜色映射) → Task 2 (控件) → Task 3 (XAML 接线) → Task 4 (DragDrop 补状态) → 测试 → F1-F4 → user okay
> **Parallel Speedup**: ~40% vs sequential

---

## Context

### Original Request
用户希望在 `src\MantisZip.UI.Avalonia\Dialogs\ProgressWindow.axaml` 的总进度条上，根据压缩/解压状态分段改变颜色。例如 5%~10% 段代表一个压缩包，该压缩包失败则此段显示红色。用户以疑问方式提出（"你看能实现吗？"），按 AGENTS.md 规则 0 先沟通确认后再规划。

### Interview Summary
**Key Discussions**:
- 作用范围：**所有模式都分段**（单文件视为 1 段；单文件失败整条变红）
- 完成段颜色：**绿色**（与现有 `BatchStatusConverters` Completed=绿 #4CAF50 一致）
- 边界状态：到达的段全部按状态上色；Pending（未处理）段显示进度条背景色
- 分段细节：段间 **动态预算制缝隙**（用户确认方案）——缝隙总和恒 ≤ 条宽 15%，单条缝隙 `gap = min(2px, 条宽×15%/(N-1))`；N 大时缝隙自动收缩（N≈50 时 ~1.4px，N≈100 时 ~0.7px，N≥200 时 <0.5px 实际无缝），任何批次数下内容至少占 85% 不被挤占；圆角与现有 ProgressBar 一致（~4px）
- 实现方案：**方案 A —— 自定义 `SegmentProgressBar : Control`**（Render/DrawingContext 绘制）
- 颜色来源：**复用现有状态色，取不透明版本**（红 #F44336 / 绿 #4CAF50 / 青 #00BCD4 / 蓝 #42A5F5，alpha=FF；现有 35% 半透明只用于列表行叠加，不适用于实心条）
- 无列表失败标红：**支持** —— 仅需在 `DragDropService` 失败路径补齐 `SetCurrentBatchItem(0) + UpdateBatchItemStatus(0, Failed)`（该路径是目前唯一失败时不上报项状态的地方；`RunWithProgress`/`CompressWithProgress` 的 catch 已上报）。**无需新增 HasOverallFailed 标志**
- 测试策略：**单元测试**（颜色映射/段区间/退化逻辑）+ **Agent QA**（App 截图验证各状态）

**Research Findings**:
- `BatchItem`/`BatchItemStatus`（Pending/InProgress/Completed/Skipped/Failed）定义在 **Core 层** `src\MantisZip.Core\Models\ProgressBatchItem.cs`，含 `Progress`(0-100)、`ErrorMessage`，实现 `INotifyPropertyChanged`；Avalonia 通过 `ProgressViewModel.BatchItems`（ObservableCollection）暴露
- 批量加权公式（ProgressViewModel.SetProgress:221-229）：每项恰好占 `100/N` 百分比段 → 与用户的"5%-10% 一段"语义完全吻合
- **`ProgressViewModel.IsIndeterminate` 是死绑定**：全项目仅 `ResultTreeView` 加载覆层（ResultTreeView.axaml.cs:166）对自身进度条置 true，ProgressWindow 总条从未启用，批量模式始终确定进度 → 新控件无需处理 indeterminate
- **关键陷阱**：`SetComplete`（ProgressViewModel:275-278）强制 `PercentComplete = 100` → 新控件**必须从 `BatchItems` 推导段，不能绑 `PercentComplete`**，否则失败时显示满条绿
- 失败上报路径：
  - GUI：`MainWindow.axaml.cs` RunWithProgress catch(:231-234) → `UpdateBatchItemStatus(0, Failed)`（丢消息）
  - CLI 压缩：`App.axaml.cs` CompressWithProgress(:1949-1957) → item0 Failed + SetErrorSummary + CompleteWithErrors
  - CLI 解压批量：`RunCliExtractBatchWithProgressAsync`(:1026/:1055) / `RunCliDirectExtractBatchAsync`(:1162/:1197) → 逐项 Failed(含 ex.Message)
  - **`DragDropService.cs`(:118-123)：唯一失败不上报项状态的路径**（只弹错误窗、清状态，且从未调 `SetCurrentBatchItem(0)`）
- 现有 `HasFailures`（ProgressViewModel:134）= `_batchItems.Any(i => i.Status == Failed)` → 不新增重叠标志
- 项目内无任何现成自定义绘制/分段进度条控件（无 ProgressBar 子类、无 Render/DrawingContext 覆盖）；主题提供 `ThemeProgressFillBrush`/`ThemeProgressBgBrush`

### Metis Review
**Identified Gaps** (addressed):
- 数据源必须绑 `BatchItems` 而非 `PercentComplete`（否则 SetComplete 强制 100% 导致失败满条绿）→ 已采纳
- 颜色需用不透明版本（现有 35% alpha 为列表行叠加设计）→ 已与用户确认用不透明版
- `HasOverallFailed` 是过度设计：仅 DragDropService 需要补齐状态上报，RunWithProgress/CompressWithProgress 已上报 → 改为只需补 DragDropService
- `IsIndeterminate` 死绑定 → 控件无需处理
- count==0/1 边界、主题切换时渲染时读取 brush（不缓存）、resize/DPI 需 ArrangeOverride → 已纳入任务要求
- `HasFailures` 已存在，不造重叠标志 → 已采纳

---

## Work Objectives

### Core Objective
将 ProgressWindow 总进度条替换为按批处理项状态分段着色的自定义控件（失败红/成功绿/跳过青/进行中蓝/未处理背景），并补齐 DragDropService 失败状态上报，使所有模式下失败段均能标红。

### Concrete Deliverables
- 新增 `src/MantisZip.UI.Avalonia/Controls/SegmentProgressBar.cs`：`Control` 子类，`ItemsSource` 绑定 `ObservableCollection<BatchItem>`，Render 按段绘制
- 新增公共颜色映射（从 `BatchStatusConverters.cs` 抽取）：`BatchItemStatus → 不透明 Color`
- `ProgressWindow.axaml` Row 4 总进度条替换（保留右侧 42px 百分比数字）
- `DragDropService.cs` 失败路径补充 `SetCurrentBatchItem(0) + UpdateBatchItemStatus(0, BatchItemStatus.Failed)`
- 单元测试（`tests/MantisZip.UI.Avalonia.Tests/`）：颜色映射、段区间、退化逻辑
- 更新 `docs/PLAN.md`（规则 1）与 `docs/PROGRESS.md`（规则 3）

### Definition of Done
- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 通过
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` 新增单测全绿
- [ ] Agent 运行 App 截图验证：批量压缩 3 文件（1 个无效）→ 2 绿 + 1 红段；单文件失败 → 全红；单文件成功 → 全绿
- [ ] `lsp_diagnostics` 无错误

### Must Have
- 分段由 `BatchItems`（每项 Status + Progress）推导，**不依赖 PercentComplete**
- 所有模式分段（count==1 → 单段全宽；count==0 → 空轨道不崩溃）
- 颜色：不透明版 #F44336/#4CAF50/#00BCD4/#42A5F5；Pending=轨道背景色
- 段间动态预算缝隙（gap = min(2px, 条宽×15%/(N-1))，缝隙总占用 ≤ 条宽 15%，大批次数时自动收缩）；圆角与现有 ProgressBar 一致
- DragDropService 失败 → 单段红（补状态上报）
- 主题切换正确（DynamicResource 渲染时读取，不缓存）
- resize/DPI 正确（ArrangeOverride/Bounds 变化重排）

### Must NOT Have (Guardrails)
- **不得**修改 Core `BatchItem`/`BatchItemStatus`（数据已足够）
- **不得**触碰 WPF `MantisZip.UI`（规则 11）
- **不得**将 `PercentComplete` 绑到新控件（SetComplete 强制 100% 的陷阱）
- **不得**重写 `ProgressStatusToBackgroundConverter` 的渐变逻辑（只抽取最小公共颜色辅助方法）
- **不得**动 Row 3 文件进度条
- **不得**新增无请求的动画/过渡打磨（AI slop）
- **不新增用户可见文案**（规则 13 无需新 key）
- 版本号不变（规则 2：未经用户许可不改版本）

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** - ALL verification is agent-executed. No exceptions.
> Acceptance criteria requiring "user manually tests/confirms" are FORBIDDEN.

### Test Decision
- **Infrastructure exists**: YES（`tests/MantisZip.UI.Avalonia.Tests/MantisZip.UI.Avalonia.Tests.csproj`）
- **Automated tests**: Unit tests (implementation后写)——颜色映射、段区间计算、退化逻辑（count==0/1）
- **Framework**: xUnit（沿用测试项目现有框架；若项目为 MSTest/NUnit 则跟随）
- **Agent QA**: Desktop App 截图验证（通过运行 App + 构造批次场景 + 截图断言像素色）

### QA Policy
Every task MUST include agent-executed QA scenarios (see TODO template below).
Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.{ext}`.

- **TUI/CLI**: 通过 Bash 运行 `dotnet run --project src\MantisZip.UI.Avalonia\...` + CLI 参数构造批量场景
- **API/Backend / 类库逻辑**: 单元测试（`dotnet test`）
- **UI 渲染**: 截图分析（Windows 平台用 PowerShell 截图或 Avalonia 渲染测试）。注意：桌面 UI 自动化截图在无头环境受限，务实方案 = 单测覆盖全部纯逻辑（颜色/段区间/退化），Agent QA 以单测 + 构建 + 可选截图为准。若截图不可行，Agent 需在 QA 记录中说明并改以 `dotnet test` 断言全部逻辑 + 手工启动验证码路径。（期望：单测全绿即视为渲染逻辑正确——绘制代码与段区间计算完全解耦）

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Start Immediately):
├── Task 1: 抽取公共颜色映射 GetColor() 到 BatchStatusConverters.cs [quick]
└── Task 2: 新建 SegmentProgressBar.cs 控件（几何/绘制/重绘逻辑，颜色从 GetColor 引用）[deep]

Wave 2 (After Wave 1 - 接线 + 补状态 + 测试):
├── Task 3: ProgressWindow.axaml Row 4 替换为 SegmentProgressBar [quick]
├── Task 4: DragDropService.cs 失败路径补状态上报 [quick]
└── Task 5: 单元测试（颜色映射/段区间/退化）[deep]

Wave FINAL (After ALL tasks — 4 parallel reviews, then user okay):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Real manual QA (unspecified-high)
└── Task F4: Scope fidelity check (deep)
-> Present results -> Get explicit user okay

Critical Path: Task 1 → Task 2 → Task 3/4 → Task 5 → F1-F4 → user okay
Parallel Speedup: ~40% faster than sequential
Max Concurrent: 3 (Wave 2)
```

### Dependency Matrix
- **1**: - → blocks 2 (颜色方法被控件引用)
- **2**: 1 → blocks 3 (控件需先存在)
- **3**: 2 → blocks 5 (XAML 接线后才有集成测试依据)
- **4**: - （可与 2/3 并行，独立修复）→ blocks 5
- **5**: 2, 3, 4 → blocks FINAL
- **F1-F4**: 1-5 → blocks user okay

### Agent Dispatch Summary
- **Wave 1**: 2 tasks — T1 → `quick`, T2 → `deep`
- **Wave 2**: 3 tasks — T3 → `quick`, T4 → `quick`, T5 → `deep`
- **FINAL**: 4 tasks — F1 → `oracle`, F2 → `unspecified-high`, F3 → `unspecified-high`, F4 → `deep`

---

## TODOs

> Implementation + Test = ONE Task. Never separate.
> EVERY task MUST have: Recommended Agent Profile + Parallelization info + QA Scenarios.
> **A task WITHOUT QA Scenarios is INCOMPLETE. No exceptions.**

- [ ] 1. 抽取公共状态颜色映射 `BatchItemStatus → Color`

  **What to do**:
  - 在 `src/MantisZip.UI.Avalonia/Converters/BatchStatusConverters.cs` 中新增一个静态辅助类或静态方法（如 `BatchStatusColors.GetColor(BatchItemStatus)`），返回**不透明** Color：
    - `Failed` → `Color.FromRgb(0xF4, 0x43, 0x36)`（红）
    - `Completed` → `Color.FromRgb(0x4C, 0xAF, 0x50)`（绿）
    - `Skipped` → `Color.FromRgb(0x00, 0xBC, 0xD4)`（青）
    - `InProgress` → `Color.FromRgb(0x42, 0xA5, 0xF5)`（蓝）
    - `Pending` → 返回 null（由调用方/控件用轨道背景色填充）
  - **注意**：现有 `ProgressStatusToBackgroundConverter.Convert`（:77-83）用的是 35% alpha（`const byte alpha = 0x59`）叠加色。**不要**修改这个转换器的渐变逻辑；只在其旁新增一个纯映射辅助。可以重构让转换器内部复用它（把 alpha 叠加留作转换器自身逻辑）——但以最小改动为准，新增辅助方法即可，转换器可保持原样
  - 加 XML doc 注释说明语义（不透明版用于实心段；半透明版用于列表行叠加）
  - 命名空间：`MantisZip.UI.Avalonia.Converters`（与现有转换器同文件同命名空间即可）

  **Must NOT do**:
  - 不改 `ProgressStatusToBackgroundConverter` 的渐变/alpha 逻辑（除非无重构的最小复用）
  - 不改 `BatchItem`/`BatchItemStatus`（Core 层）
  - 不新增本地化 key（纯逻辑代码，无用户可见字符串）

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 单一文件内新增一个纯映射静态方法，无跨模块影响
  - **Skills**: `[]`
  - **Skills Evaluated but Omitted**:
    - 无需技能

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Task 2)
  - **Blocks**: Task 2（控件引用此方法）
  - **Blocked By**: None (can start immediately)

  **References** (CRITICAL - Be Exhaustive):
  > The executor has NO context from this interview. References are their ONLY guide.

  **Pattern References** (existing code to follow):
  - `src/MantisZip.UI.Avalonia/Converters/BatchStatusConverters.cs:67-102` - `ProgressStatusToBackgroundConverter.Convert` 的状态→颜色 switch（:77-83）——复用它同款 hex 值，但改成不透明 RGB

  **API/Type References** (contracts to implement against):
  - `src/MantisZip.Core/Models/ProgressBatchItem.cs:8-16` - `BatchItemStatus` 枚举（Pending/InProgress/Completed/Skipped/Failed）
  - `Avalonia.Media.Color` / `Color.FromRgb(byte, byte, byte)` - 目标 API（不透明色）

  **Test References** (testing patterns to follow):
  - `tests/MantisZip.UI.Avalonia.Tests/` - 现有测试项目结构（Task 5 会添加本方法的单测，本任务只实现方法）

  **WHY Each Reference Matters**:
  - 转换器 switch 是现有 hex 值的唯一来源，必须与之保持一致（除 alpha 外）
  - BatchItemStatus 枚举值决定 switch 分支

  **Acceptance Criteria**:

  > **AGENT-EXECUTABLE VERIFICATION ONLY** - No human action permitted.

  - [ ] 新增的 `GetColor(BatchItemStatus)`（或等价命名）方法存在于 BatchStatusConverters.cs
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded，无新增警告错误
  - [ ] `lsp_diagnostics` 对 BatchStatusConverters.cs → 无错误

  **QA Scenarios (MANDATORY - task is INCOMPLETE without these):**

  ```
  Scenario: 颜色映射完整性与不透明度
    Tool: Bash (dotnet build) + 后续单测引用
    Preconditions: 方法已实现并编译通过
    Steps:
      1. 查找 GetColor 方法签名（lsp_symbols 或 read）
      2. 断言 Failed→(0xF4,0x43,0x36)、Completed→(0x4C,0xAF,0x50)、Skipped→(0x00,0xBC,0xD4)、InProgress→(0x42,0xA5,0xF5) 且 A=255（不透明）
      3. 断言 Pending→null（或调用方约定的背景语义）
    Expected Result: 4 色映射精确匹配 hex 值、alpha=255；Pending 返回 null
    Failure Indicators: 任一 hex 值不符、返回了半透明色（A<255）、Pending 返回非 null
    Evidence: .sisyphus/evidence/task-1-color-mapping.txt（粘贴方法源码 + 断言结果）

  Scenario: 构建验证
    Tool: Bash
    Preconditions: 无
    Steps:
      1. 运行 dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）
    Failure Indicators: 编译错误（缺失 using、语法错误）
    Evidence: .sisyphus/evidence/task-1-build.txt
  ```

  **Evidence to Capture:**
  - [ ] task-1-color-mapping.txt
  - [ ] task-1-build.txt

  **Commit**: YES (groups with 2) — `refactor(avalonia): 抽取批处理状态颜色映射为公共方法`
  - Pre-commit: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`

---

- [ ] 2. 新建 `SegmentProgressBar` 自定义绘制控件

  **What to do**:
  - 新建 `src/MantisZip.UI.Avalonia/Controls/SegmentProgressBar.cs`，`public class SegmentProgressBar : Control`（Avalonia）
  - **关键约束（Metis/Oracle 双重确认）**：数据源 = `ItemsSource`（绑 `ObservableCollection<BatchItem>`）；**不要**定义/使用 `PercentComplete` 绑定——段几何与填充全部由 `item.Status` + `item.Progress` 推导
  - 依赖属性：
    - `ItemsSource`（`IEnumerable<BatchItem>` 或直接 ObservableCollection）——订阅 `CollectionChanged`；对每项订阅 `PropertyChanged`（BatchItem 已实现 INotifyPropertyChanged）
    -（可选）`TrackBackground` / `CornerRadius` 样式化属性，默认从 DynamicResource `ThemeProgressBgBrush` / `BorderRadius` 读取
  - 绘制逻辑（`Render(DrawingContext)`）：
    - N = Items 数；段 i 水平区间 = `[i/N, (i+1)/N)` × 控件宽度
    - **段间细缝（动态预算制，用户确认）**：`gap = min(2px, Bounds.Width × 0.15 / (N-1))`（N≤1 时 gap=0）。先算 gap 再算每段宽：`segW = (Bounds.Width - gap × (N-1)) / N`，段 i 起点 `x = i × (segW + gap)`。缝隙用轨道背景色绘制（同 `ThemeProgressBgBrush`）或直接留空——N 大时 gap 自动收缩（N≈50→~1.4px，N≈100→~0.7px，N≥200→<0.5px 无缝），**保证内容至少占条宽 85%**
    - 圆角：与现有 ProgressBar 一致（App.axaml 全局 `Style Selector="ProgressBar"` 的圆角；约为 2-4px，读主题 `BorderRadius` 或现有 ProgressBar 模板确认）
    - 每段填充与颜色：
      - `Pending` → 轨道背景色（TrackBackground）
      - `InProgress` → 蓝 `GetColor()`，填充宽度 = `item.Progress/100 × 段宽`（部分填充，实时推进）
      - `Completed` → 绿 `GetColor()`，实心
      - `Failed` → 红 `GetColor()`，实心
      - `Skipped` → 青 `GetColor()`，实心
    - 轨道背景：整条背景 = `ThemeProgressBgBrush`
  - 重绘触发：`CollectionChanged` 或任一 item `PropertyChanged`（Progress 变化驱动 InProgress 段）→ `InvalidateVisual()`
  - 尺寸/DPI：重写 `MeasureOverride`/`ArrangeOverride` 或处理 `Bounds` 变化（Render 时用 `Bounds` 而非缓存尺寸）；默认 Height 建议用紧凑度资源（规则 5），但控件自身不硬编码高度——由 XAML 侧给 Height（见 Task 3）
  - 主题正确性：Render 时**每次**通过 `TryGetResource("ThemeProgressBgBrush", ...)` 读取轨道背景（或绑定 DynamicResource 属性），不得缓存 brush（主题切换后重绘）
  - 清理：`OnDetachedFromVisualTree` 时退订 CollectionChanged/PropertyChanged 防泄漏
  - **不得**使用 `PercentComplete`/`Value`；`IsIndeterminate` 无需处理（死绑定，见 Context）

  **Must NOT do**:
  - 不绑 PercentComplete（SetComplete 强制 100% 陷阱）
  - 不处理 IsIndeterminate（ProgressWindow 总条从未启用）
  - 不改 Core BatchItem
  - 不写死高度/间距数值（用主题/紧凑度资源，规则 4/5）
  - 不新增本地化 key
  - 不加未请求的动画

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: 自定义 Avalonia 控件，涉及依赖属性、事件订阅、DrawingContext 绘制、主题资源、DPI/布局多个技术面，需要自主探索 Avalonia 控件编写模式
  - **Skills**: `[]`
  - **Skills Evaluated but Omitted**:
    - 无匹配技能（Avalonia 桌面控件绘制无现有万能技能）

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Task 1)
  - **Blocks**: Task 3 (XAML 接线), Task 5 (集成测试)
  - **Blocked By**: Task 1（GetColor 被引用；若 Task 1 未完成可在控件内临时内联，但正式实现应引用公共方法）

  **References** (CRITICAL - Be Exhaustive):

  **Pattern References** (existing code to follow):
  - `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml.cs` - 现有 Avalonia 自定义控件结构参考（样式属性定义、OnXxxChanged 模式）
  - `src/MantisZip.UI.Avalonia/Controls/QuickPathPicker.axaml.cs` - 自包含控件参考
  - `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml:186-204` - 现有总进度条结构（替换对象）+ 右侧 42px 数字 TextBlock（保留）

  **API/Type References** (contracts to implement against):
  - `src/MantisZip.Core/Models/ProgressBatchItem.cs` - `BatchItem`（含 `Status`/`Progress`/INotifyPropertyChanged）与 `BatchItemStatus`
  - `Avalonia.Controls.Control` / `Avalonia.Media.DrawingContext` - 控件基类与绘制 API
  - `Avalonia.AvaloniaObject.RegisterProperty` - 依赖属性注册
  - Task 1 产出的 `GetColor(BatchItemStatus)` 方法

  **Test References** (testing patterns to follow):
  - `tests/MantisZip.UI.Avalonia.Tests/` - 测试项目（Task 5 添加逻辑单测；本任务控件结构需让段区间计算可测——建议将「段区间计算」抽成内部静态方法或 public 方法供测试）

  **External References**:
  - Avalonia 官方文档：自定义控件绘制（DrawingContext）—— `https://docs.avaloniaui.net/docs/guides/custom-controls/`（如不可达，按现有 ResultTreeView/QuickPathPicker 模式即可，它们已是权威范例）

  **WHY Each Reference Matters**:
  - ResultTreeView/QuickPathPicker 是本仓库 Avalonia 自定义控件的现实范式（属性/事件/布局写法）
  - 段区间计算抽为可测方法（如 `internal static (double start, double end)[] ComputeSegments(int count)`），让 Task 5 单测直接覆盖纯逻辑

  **Acceptance Criteria**:

  > **AGENT-EXECUTABLE VERIFICATION ONLY** - No human action permitted.

  - [ ] SegmentProgressBar.cs 存在且继承 `Control`
  - [ ] 定义 `ItemsSource` 属性并订阅 CollectionChanged + 每项 PropertyChanged → InvalidateVisual
  - [ ] 段区间计算抽为可测试的纯方法（internal/public static）——**含缝隙公式**：`ComputeSegments(int count, double width)` 返回含 gap 的段几何（或单独 `ComputeGap(int count, double width)` 可测）
  - [ ] Render 中 Failed→红/Completed→绿/Skipped→青/InProgress→蓝（引用 Task 1 GetColor）/Pending→轨道背景；InProgress 填充至 item.Progress
  - [ ] 段间动态预算缝隙（gap = min(2px, 宽×15%/(N-1))）、圆角使用主题资源
  - [ ] 无 PercentComplete/Value 绑定、无 IsIndeterminate 处理
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios (MANDATORY - task is INCOMPLETE without these):**

  ```
  Scenario: 段区间计算纯逻辑正确性（count==3 与 count==1/0，含缝隙公式）
    Tool: Bash (dotnet run 临时验证或单元测试前置断言；本任务先人工确认方法存在)
    Preconditions: ComputeSegments/ComputeGap 方法存在
    Steps:
      1. read SegmentProgressBar.cs 定位 ComputeSegments/ComputeGap 签名
      2. 人工演算: count=3 → 3 段各 ~33.33% 且 gap=min(2px, ...)；count=1 → 1 段 0-100%（gap=0）；count=0 → 空数组不抛异常
      3. 演算大批次数 gap 收缩: count=100, width=476px → gap ≈ min(2, 476×0.15/99) ≈ min(2, 0.72) ≈ 0.72px（内容 ≈ 85%+）
    Expected Result: 方法与预期语义一致（含缝隙动态收缩；具体数值断言交给 Task 5 单测）
    Failure Indicators: 除零、区间重叠/跳空、count==0 抛异常、gap 未收缩（固定 2px 导致内容被挤占）
    Evidence: .sisyphus/evidence/task-2-segments.txt

  Scenario: 依赖属性与事件订阅存在
    Tool: read + lsp_symbols
    Preconditions: 无
    Steps:
      1. lsp_symbols 检查 SegmentProgressBar 类成员
      2. 断言存在 ItemsSource 属性、OnCollectionChanged/OnItemPropertyChanged 处理、InvalidateVisual 调用
    Expected Result: 成员齐全、事件订阅/退订成对（DetachedFromVisualTree 清理）
    Failure Indicators: 缺事件退订（内存泄漏）、属性未定义
    Evidence: .sisyphus/evidence/task-2-control-structure.txt

  Scenario: 构建验证
    Tool: Bash
    Preconditions: 无
    Steps:
      1. dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）
    Failure Indicators: 编译错误（DrawingContext API 误用、依赖属性注册错误）
    Evidence: .sisyphus/evidence/task-2-build.txt
  ```

  **Evidence to Capture:**
  - [ ] task-2-segments.txt
  - [ ] task-2-control-structure.txt
  - [ ] task-2-build.txt

  **Commit**: YES (groups with 1) — `feat(avalonia): 新增分段状态进度条 SegmentProgressBar 控件`
  - Pre-commit: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`

---

- [ ] 3. ProgressWindow.axaml Row 4 总进度条替换为 SegmentProgressBar

  **What to do**:
  - 在 `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` 的 Row 4（:186-204）把现有 `<ProgressBar ...>`（:192-197）替换为 `<controls:SegmentProgressBar ...>`
  - 保留 Grid 列结构与右侧 42px 百分比 TextBlock（:198-203，`Text="{Binding PercentComplete, StringFormat=\{0\}%}"`）——数字继续由 PercentComplete 驱动，**不改动**
  - 新增 xmlns：在 Window 根元素加 `xmlns:controls="clr-namespace:MantisZip.UI.Avalonia.Controls"`
  - 绑定与属性：
    - `ItemsSource="{Binding BatchItems}"`（ProgressViewModel.BatchItems 已存在）
    - `Height="22"`（保持与现状一致；现有硬编码 22——按规则 5 可改用紧凑度资源，但控件高度变更可能影响布局，默认保持 22 并注释说明，或映射到 `ControlHeightSm/Md`；以最小视觉回归为准）
    - 移除 `Value`/`Minimum`/`Maximum`/`IsIndeterminate`/`Foreground` 绑定（新控件不需要）
  - 布局说明注释：`<!-- Row 4: 总体进度条（分段状态着色）-->`
  - 验证现有 XAML 有无 x:CompileBindings 影响（当前 Window 根是 `x:CompileBindings="False"`，ItemsSource 绑定直接用即可）

  **Must NOT do**:
  - 不动 Row 3 文件进度条（:172-177 的 ProgressBar 保留）
  - 不动 Row 4 右侧数字 TextBlock
  - 不改 ProgressViewModel 的 PercentComplete 计算逻辑
  - 不新增本地化 key

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 单一 XAML 文件局部替换 + xmlns 声明，模式清晰
  - **Skills**: `[]`
  - **Skills Evaluated but Omitted**:
    - 无需技能

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Task 4)
  - **Blocks**: Task 5（控件接线后集成测试）
  - **Blocked By**: Task 2（SegmentProgressBar 需先编译通过）

  **References** (CRITICAL - Be Exhaustive):

  **Pattern References**:
  - `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml:1-17` - Window 根元素 + xmlns 声明（加 controls 命名空间的锚点）
  - `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml:186-204` - Row 4 现有结构（替换目标 + 保留的右侧数字）
  - `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml:1-15` - 自定义控件在 XAML 中的 xmlns 用法参考（若 ResultTreeView 用在某 axaml 中，直接照抄其 xmlns 写法）

  **API/Type References**:
  - `src/MantisZip.UI.Avalonia/Controls/SegmentProgressBar.cs` - 新控件（Task 2 产出）的公开属性（ItemsSource）

  **WHY Each Reference Matters**:
  - xmlns 声明语法参照现有项目内自定义控件用法，避免命名空间拼写错误
  - 右侧数字 TextBlock 必须逐字保留

  **Acceptance Criteria**:

  > **AGENT-EXECUTABLE VERIFICATION ONLY** - No human action permitted.

  - [ ] ProgressWindow.axaml Row 4 使用 `<controls:SegmentProgressBar ItemsSource="{Binding BatchItems}" .../>`
  - [ ] 右侧 42px 数字 TextBlock 原样保留（PercentComplete 绑定未变）
  - [ ] Row 3 文件进度条未动
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded（XAML 编译通过）
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios (MANDATORY - task is INCOMPLETE without these):**

  ```
  Scenario: XAML 接线正确性
    Tool: Bash (dotnet build) + read
    Preconditions: SegmentProgressBar.cs 已编译（Task 2 完成）
    Steps:
      1. read ProgressWindow.axaml Row 4 区域
      2. 断言: 控件类型为 SegmentProgressBar、ItemsSource 绑 BatchItems、无 Value/IsIndeterminate 残留绑定、右侧数字 TextBlock 原样存在
      3. 断言 xmlns:controls 声明存在
    Expected Result: 上述断言全部为真
    Failure Indicators: 控件类型错误、残留旧绑定、数字 TextBlock 丢失、xmlns 缺失
    Evidence: .sisyphus/evidence/task-3-xaml.txt

  Scenario: 构建验证（XAML 编译通道）
    Tool: Bash
    Preconditions: 无
    Steps:
      1. dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）——证明 XAML 中控件引用有效
    Failure Indicators: XAML 编译错误（控件类型解析失败、属性不存在）
    Evidence: .sisyphus/evidence/task-3-build.txt
  ```

  **Evidence to Capture:**
  - [ ] task-3-xaml.txt
  - [ ] task-3-build.txt

  **Commit**: YES (groups with 2) — `feat(avalonia): 进度窗口总进度条替换为分段状态进度条`
  - Pre-commit: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`

---

- [ ] 4. DragDropService 失败路径补齐批次状态上报

  **What to do**:
  - 在 `src/MantisZip.UI.Avalonia/Services/DragDropService.cs` 的 `catch (Exception ex)` 块（:118-123，当前设置 `failed=true; failureMessage=ex.Message;`）中追加：
    - `pw.SetCurrentBatchItem(0);`（该文件从未调用过 SetCurrentBatchItem，_currentBatchIndex 始终 -1，需初始化）
    - `pw.UpdateBatchItemStatus(0, BatchItemStatus.Failed);`（可使分段条单段红；RunWithProgress 同款风格，不传错误消息也行，或传 ex.Message 增强——建议传 ex.Message，与 CLI 解压路径一致）
  - 位置语义：在 `failed = true` 后、`finally { pw.Close(); }` 之前（catch 块内）
  - 线程安全：这些是 ProgressWindow 的线程安全封装（DispatchIfNeeded），直接调用即可
  - 说明注释：`// 失败时上报批次状态，使总进度条失败段标红（对齐 RunWithProgress catch 行为）`
  - 注意：DragDropService 的 `pw` 变量在此作用域可用（try 之前声明，见 catch 上下文）

  **Must NOT do**:
  - 不改成功路径（拖拽成功当前不刷新状态，行为保持——本次只解决失败标红）
  - 不新增本地化 key（错误弹窗文案已有 Status_DragFailed）
  - 不改 RunWithProgress / CompressWithProgress（它们已上报）

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 单文件 catch 块内追加 2-3 行状态调用
  - **Skills**: `[]`
  - **Skills Evaluated but Omitted**:
    - 无需技能

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Task 3)
  - **Blocks**: Task 5（集成测试场景需要此路径）
  - **Blocked By**: None（不依赖 Task 2 控件；即使控件未替换，状态上报本身独立成立）

  **References** (CRITICAL - Be Exhaustive):

  **Pattern References**:
  - `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs:231-236` - RunWithProgress 的 catch：`if (hasFileList) pw.UpdateBatchItemStatus(0, BatchItemStatus.Failed);` —— 本任务复刻此模式
  - `src/MantisZip.UI.Avalonia/Services/DragDropService.cs:99-127` - 目标 catch 块（:118-123）

  **API/Type References**:
  - `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs:236-248` - `SetCurrentBatchItem(int)` / `UpdateBatchItemStatus(int, BatchItemStatus, string?)` 线程安全封装签名

  **WHY Each Reference Matters**:
  - 与 RunWithProgress 行为对齐，保证所有失败路径（GUI/CLI/拖拽）状态上报语义一致
  - 确认 pw 封装方法签名与线程安全语义

  **Acceptance Criteria**:

  > **AGENT-EXECUTABLE VERIFICATION ONLY** - No human action permitted.

  - [ ] DragDropService.cs catch(Exception) 块包含 `pw.SetCurrentBatchItem(0);` + `pw.UpdateBatchItemStatus(0, BatchItemStatus.Failed)`（及可选错误消息）
  - [ ] 失败时批次项 0 状态为 Failed（可运行单测覆盖——Task 5 添加；本任务先保证代码存在）
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios (MANDATORY - task is INCOMPLETE without these):**

  ```
  Scenario: 失败路径状态上报代码存在
    Tool: read
    Preconditions: 无
    Steps:
      1. read DragDropService.cs :114-127
      2. 断言 catch(Exception) 内 failed=true 之后调用了 SetCurrentBatchItem(0) 和 UpdateBatchItemStatus(0, Failed)
    Expected Result: 两调用存在且位于 finally(pw.Close()) 之前
    Failure Indicators: 未调用、调用顺序错误（finally 之后）、参数错误
    Evidence: .sisyphus/evidence/task-4-dragdrop.txt

  Scenario: 构建验证
    Tool: Bash
    Preconditions: 无
    Steps:
      1. dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）
    Failure Indicators: 编译错误（BatchItemStatus using 缺失等）
    Evidence: .sisyphus/evidence/task-4-build.txt
  ```

  **Evidence to Capture:**
  - [ ] task-4-dragdrop.txt
  - [ ] task-4-build.txt

  **Commit**: YES (groups with 3) — `fix(avalonia): 拖拽解压失败时上报批次状态以便进度条标红`
  - Pre-commit: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`

---

- [ ] 5. 单元测试：颜色映射 / 段区间 / 退化逻辑

  **What to do**:
  - 在 `tests/MantisZip.UI.Avalonia.Tests/` 下新增测试文件（如 `SegmentProgressBarTests.cs`），框架跟随现有测试项目（先查 csproj 用 xUnit/MSTest/NUnit）
  - 测试用例（纯逻辑，不依赖真实 UI 线程）：
    1. **颜色映射**（Task 1 的 GetColor）：Failed→(F4,43,36)，Completed→(4C,AF,50)，Skipped→(00,BC,D4)，InProgress→(42,A5,F5)，全部 A=255；Pending→null
    2. **段区间**（Task 2 的 ComputeSegments）：
       - count=3 → 3 段：[(0,1/3),(1/3,2/3),(2/3,1)]
       - count=1 → 1 段 (0,1)
       - count=0 → 空数组
       - 区间不重叠、无跳空、总和=1
    3. **缝隙公式**（Task 2 的 ComputeGap）：
       - 小批量（count=10, width=476）→ gap=2px（上限生效，缝隙总占 ≤15%）
       - 大批量（count=100, width=476）→ gap≈0.72px（缝隙收缩，内容 ≥85%）
       - count=200, width=476 → gap≈0.36px（<0.5px，实际无缝）
       - count≤1 → gap=0；**断言缝隙总占用 ≤ 条宽 15%（关键不变量）**
    4. **状态→段颜色组合**（若控件提供可测的段渲染决策方法，如 `Color? ResolveSegmentColor(status)`，测其映射；否则跳过此条并说明）
    5. **退化**：count==0 不抛异常；count==1 单段全宽
  - 若控件内部方法为 internal，测试项目需 `InternalsVisibleTo` 或把方法设为 public/internal 并加 `[assembly: InternalsVisibleTo("MantisZip.UI.Avalonia.Tests")]`（按现有项目约定，查 csproj 是否已有）
  - 运行 `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` 全绿

  **Must NOT do**:
  - 不写依赖真实 UI 线程/窗口渲染的测试（如需要 Avalonia.Headless 才可跑，若测试项目未配置则跳过渲染层测试，聚焦纯逻辑）
  - 不 mock BatchItemStatus（用真实枚举值）
  - 不新增本地化 key

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: 需要理解测试项目结构、可能的 InternalsVisibleTo 配置、以及把控件逻辑正确暴露给测试
  - **Skills**: `[]`
  - **Skills Evaluated but Omitted**:
    - 无需技能（项目已有测试约定，跟随现存测试文件模式即可）

  **Parallelization**:
  - **Can Run In Parallel**: NO（依赖 Task 1/2/3/4 产物）
  - **Parallel Group**: Sequential - Wave 2 末端
  - **Blocks**: FINAL review wave
  - **Blocked By**: Task 2（ComputeSegments）、Task 3（XAML 接线）、Task 4（拖拽路径）——纯逻辑测试至少依赖 Task 1/2

  **References** (CRITICAL - Be Exhaustive):

  **Test References** (patterns to follow):
  - `tests/MantisZip.UI.Avalonia.Tests/` - 现有测试文件（先读 1-2 个，照抄框架/断言风格/项目配置）
  - `tests/MantisZip.Tests/` - Core 层测试（如需要跨项目引用参考）

  **Pattern References**:
  - `src/MantisZip.UI.Avalonia/Converters/BatchStatusConverters.cs` - GetColor（Task 1 产物，被测对象）
  - `src/MantisZip.UI.Avalonia/Controls/SegmentProgressBar.cs` - ComputeSegments 等可测方法（Task 2 产物，被测对象）

  **WHY Each Reference Matters**:
  - 跟随现有测试项目结构避免框架配置错误；被测方法签名以实际实现为准

  **Acceptance Criteria**:

  > **AGENT-EXECUTABLE VERIFICATION ONLY** - No human action permitted.

  - [ ] 新增测试文件存在，命名 SegmentProgressBarTests.cs（或等价）
  - [ ] 覆盖颜色映射（5 状态）、段区间（3/1/0 count）、缝隙公式（小/大/超大批量 + 15% 不变量）、退化（count==0/1）
  - [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿（含既有测试无回归）
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios (MANDATORY - task is INCOMPLETE without these):**

  ```
  Scenario: 单测全绿（含新增 + 既有无回归）
    Tool: Bash
    Preconditions: Task 1-4 产物已编译
    Steps:
      1. 运行 dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
      2. 观察输出: 新增测试数量与通过率
    Expected Result: 新增用例全部 PASS，既有用例无回归失败
    Failure Indicators: 任一断言失败（颜色 hex 不符、段区间错误、count==0 异常）
    Evidence: .sisyphus/evidence/task-5-test-run.txt（含测试计数输出）

  Scenario: 覆盖完整性检查
    Tool: read + grep
    Preconditions: 无
    Steps:
      1. read 测试文件，枚举 [Fact]/[Test] 方法
      2. 断言至少覆盖: 颜色映射(5 状态)、段区间(3/1/0)、缝隙公式(小/大/超大批量+15% 不变量)、退化(count==0/1) 四组
    Expected Result: 覆盖清单与任务要求一致
    Failure Indicators: 缺任一分组（如只有颜色没有段区间）
    Evidence: .sisyphus/evidence/task-5-coverage.txt
  ```

  **Evidence to Capture:**
  - [ ] task-5-test-run.txt
  - [ ] task-5-coverage.txt

  **Commit**: YES (groups with 4) — `test(avalonia): 分段进度条颜色映射/段区间/退化逻辑单测`
  - Pre-commit: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.
>
> **Do NOT auto-proceed after verification. Wait for user's explicit approval before marking work complete.**
> **Never mark F1-F4 as checked before getting user's okay.** Rejection or user feedback -> fix -> re-run -> present again -> wait for okay.

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists (read file, curl endpoint, run command). For each "Must NOT Have": search codebase for forbidden patterns — reject with file:line if found. Check evidence files exist in .sisyphus/evidence/. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` + `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`. Review changed files for: `as any`/`@ts-ignore` (C#: null! / 压制), empty catches, console.log in prod (Console.WriteLine), commented-out code, unused imports. Check AI slop: excessive comments, over-abstraction, generic names (data/result/item/temp).
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | Files [N clean/N issues] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Execute EVERY QA scenario from EVERY task — follow exact steps, capture evidence. Test cross-task integration (控件渲染 + 状态上报协同). Test edge cases: count==0, count==1, all-failed, all-skipped. Save to `.sisyphus/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | Integration [N/N] | Edge Cases [N tested] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff (git log/diff). Verify 1:1 — everything in spec was built (no missing), nothing beyond spec was built (no creep). Check "Must NOT do" compliance. Detect cross-task contamination: Task N touching Task M's files. Flag unaccounted changes.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

- **1**: `refactor(avalonia): 抽取批处理状态颜色映射为公共方法` - BatchStatusConverters.cs, dotnet build
- **2**: `feat(avalonia): 新增分段状态进度条 SegmentProgressBar 控件` - SegmentProgressBar.cs, dotnet build
- **3**: `feat(avalonia): 进度窗口总进度条替换为分段状态进度条` - ProgressWindow.axaml, dotnet build
- **4**: `fix(avalonia): 拖拽解压失败时上报批次状态以便进度条标红` - DragDropService.cs, dotnet build
- **5**: `test(avalonia): 分段进度条颜色映射/段区间/退化逻辑单测` - tests, dotnet test

---

## Success Criteria

### Verification Commands
```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj   # Expected: Build succeeded
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj  # Expected: all tests pass (含新增单测)
```

### Final Checklist
- [ ] All "Must Have" present
- [ ] All "Must NOT Have" absent
- [ ] All tests pass
- [ ] docs/PLAN.md 已同步（规则 1）
- [ ] docs/PROGRESS.md 已更新（规则 3，提交前）