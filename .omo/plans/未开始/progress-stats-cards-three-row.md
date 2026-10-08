# 进度窗口统计卡三行结构（数量 + 大小）

> **状态**: 📋 设计已确认 + 任务分解已生成（2026-10-08），待实施
>
> **For agentic workers:** 本文档是设计规格 + 完整任务分解（brainstorming 四段设计已获用户逐段确认，见「已确认设计决策」；文末「任务分解」含 6 任务 3 波次 checkbox 步骤）。执行时使用 superpowers:subagent-driven-development 或 superpowers:executing-plans。

## TL;DR

> **Quick Summary**: 「完整」密度档的 6 张统计卡从「图标 / 标签 / 数值」竖排，改为**三行表格语义**：行 1 = 图标 + 标题（横排）、行 2 = 文件数量、行 3 = 文件大小。缺数据的行显示灰色 `—`（不隐藏单行、不隐藏卡——卡级显隐沿用现状）。
>
> **Deliverables**:
> - `Dialogs/ProgressWindow.axaml` 统计卡区（:573-703）6 卡逐卡重构为三行结构
> - `ViewModels/ProgressViewModel.cs` 新增 `StatsProcessedSize`、`StatsTotalCount` 两个只写不清的 `[ObservableProperty]`
> - 新增 `Converters/DashBrushConverter.cs`（`—`/空 → 灰，真实值 → 强调色）
> - `ProgressViewModelTests.cs` 新增 3 条单测
> - `docs/PLAN.md` 登记行（规则 1）
>
> **Estimated Effort**: Small（2-3h）
> **Parallel Execution**: NO — 单窗口 + 单 VM，顺序执行
> **Critical Path**: 用户审阅本设计 → writing-plans 任务分解 → XAML/VM/转换器 → 测试 → GUI 验收

---

## Context

### Original Request

用户原话（2026-10-08）：「下面切换到"完整"那里，我希望每个信息都显示两行，分别表示文件数量和大小，也就是说，应该是个三行的表格，第一行是图标和标题，第二行表示文件数量，第三行表示文件大小。」

### Research Findings

- **现状结构**：`ProgressWindow.axaml:573-703` 6 张统计卡（已处理/跳过/出错/已覆盖/并行度/总大小），每卡为竖排 StackPanel：图标（FontSize 18）+ 标签（11, secondary）+ 数值（16, SemiBold）；卡容器 `StatsCardsPanel` 仅 `IsFullDensity` 显示（规则 6）
- **数据可用性**（决定行 3 能否有真实值）：
  - `ArchiveProgress.TotalBytes/ProcessedBytes` **仅 ZipEngine 上报**；7z/TAR 不上报 → `SetProgress` 的 `TotalBytes > 0` 块不进入
  - `TotalFiles/ProcessedFiles`：ZIP 上报；已处理卡可见性依赖 `StatsProcessedText` 非空
  - 总大小卡可见性依赖 `StatsTotalSizeValue` 非空
- **VM 现状**：`StatsProcessedCount` 派生分数（:345，`NotifyStatsProperties` 集中通知 :360）；`_statsTotalSizeText/_statsTotalSizeValue`（:283/:287）；写入点为 `SetProgress` 的 `TotalFiles > 0` 块（:630-635）与 `TotalBytes > 0` 块（:638-643）
- **相邻测试基线**：`SetProgress_WithTotalBytes_PopulatesTotalSizeMembers`(:547)、`SetProgress_WithoutTotalBytes_LeavesTotalSizeEmpty`(:567)、`SetProgress_WithTotalFiles_ShowsProcessedOverTotal`(:491)、`StatsProcessedCount_ShowsFractionString`(:534)、`SetProgress_ZeroTotalFiles_KeepsPreviousFraction`(:516)
- **窗口特性**：`x:CompileBindings="False"`；转换器在 `Window.Resources`（:14-27）注册，新转换器按同模式注册

---

## 已确认设计决策（2026-10-08 用户逐段确认）

### D1 范围与缺数据表现

- **全部 6 张卡统一三行**（用户选择「全部卡统一三行，缺数据显示 —」，否决「仅部分卡三行」）
- 行级缺数据 → 显示 `—`（`ThemeTextSecondaryBrush` 灰）；**不**隐藏单行
- **卡级可见性完全不动**（规则 6）：
  - 卡 1–4：对应 `Stats*Text` 非空
  - 卡 5（并行度）：`HasParallelDegree`
  - 卡 6（总大小）：`StatsTotalSizeValue` 非空

### D2 行值映射表

| 卡 | 行 1：图标 + 标题 | 行 2：文件数量 | 行 3：文件大小 |
|---|---|---|---|
| ✅ 已处理 | `✅` + `StatsProcessedLabel` | `StatsProcessedCount`（"60/100" 分数，保留现本地化文案） | **新字段** `StatsProcessedSize` = `FormatUtil.FormatSize(ProcessedBytes)`（仅 `TotalBytes > 0` 时写入，否则 `—`） |
| ⏭ 跳过 | `⏭` + `StatsSkippedLabel` | `StatsSkippedCount` | 硬编码 `—`（不进 VM） |
| ❌ 出错 | `❌` + `StatsFailedLabel` | `StatsFailedCount` | 硬编码 `—` |
| 🔄 已覆盖 | `🔄` + `StatsOverwrittenLabel` | `StatsOverwrittenCount` | 硬编码 `—` |
| ⚙ 并行度 | `⚙` + `LocalizedStrings[Progress_Stats_Parallel]` | `ParallelDegree`（线程数） | 硬编码 `—` |
| 📏 总大小 | `📏` + `StatsTotalSizeLabel` | **新字段** `StatsTotalCount` = `_statsTotalFiles.ToString()`（仅 `TotalFiles > 0` 时写入，否则 `—`） | `StatsTotalSizeValue` = `FormatSize(TotalBytes)`（卡可见即必有值） |

> 注（用户决策）：已处理行 3 用**单值**（当前已处理字节数），不显示「已用/总量」复合值。

### D3 结构与样式（方案 A：逐卡 XAML 重构；方案 B 模板化已否决）

每卡内层 StackPanel（`Spacing="{DynamicResource SpacingXxs}"` 保持）三行：

1. **行 1（横排）**：`StackPanel Orientation="Horizontal"`，水平居中、`Spacing="{DynamicResource SpacingXxs}"`：
   - 图标 `TextBlock` FontSize **16**（原 18 收窄，横排占宽）
   - 标题 `TextBlock` FontSize **11**、`ThemeTextSecondaryBrush`
2. **行 2（数量）**：FontSize **15**、`FontWeight="SemiBold"`，颜色 = 该卡**原数值色**（✅→`ThemeStatusSuccessBrush`、⏭→`ThemeStatusWarningBrush`、❌→`ThemeStatusErrorBrush`、🔄→`ThemeTextPrimaryBrush`、⚙→`ThemeProgressFillBrush`、📏→`ThemeTextPrimaryBrush`）
3. **行 3（大小）**：FontSize **13**：
   - 硬编码 `—`（卡 2/3/4/5）→ 直接 `ThemeTextSecondaryBrush`
   - 动态值（卡 1 行 3、卡 6 行 2）→ 新转换器 `DashBrushConverter`：值为 `—`/null/空 → `ThemeTextSecondaryBrush`，真实值 → `ConverterParameter` 指定的强调色资源键（解析失败回退 secondary）
   - 动态 `—` 单元格全场景仅 2 处：卡 1 行 3（`StatsProcessedSize`）、卡 6 行 2（`StatsTotalCount`）

### D4 新 VM 字段（`ProgressViewModel.cs`）

| 字段 | 类型/默认 | 写入点（只写不清，与既有统计字段同模式） |
|---|---|---|
| `StatsProcessedSize` | `string`，默认 `"—"` | `SetProgress` 既有 `TotalBytes > 0` 块内追加 `StatsProcessedSize = FormatUtil.FormatSize(p.ProcessedBytes);` |
| `StatsTotalCount` | `string`，默认 `"—"` | `SetProgress` 既有 `TotalFiles > 0` 块内追加 `StatsTotalCount = _statsTotalFiles.ToString();` |

- 均为 `[ObservableProperty]`（字段名 `_statsProcessedSize` / `_statsTotalCount`），绑定自刷新；**不**加入 `NotifyStatsProperties`（该方法只管派生属性，本设计未新增派生属性）
- `ProcessedBytes = 0` 但 `TotalBytes > 0` 的初始阶段行 3 显示 `0 B`（真实实时值，非缺陷）

### D5 可见性规则（规则 6 维持）

- **卡级**：见 D1，一律沿用现状绑定
- **行级**：永不隐藏，缺数据显 `—`
- 中等/精简两档（`MediumStatsPanel` :706 起、简约面板）**零改动**

### D6 测试计划（`tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 新增 3 条）

1. `SetProgress_WithTotalBytes_PopulatesProcessedSize` — `TotalBytes > 0` 且 `ProcessedBytes = X` → `StatsProcessedSize == FormatUtil.FormatSize(X)`
2. `SetProgress_WithoutTotalBytes_ProcessedSizeStaysDash` — 不报字节 → `StatsProcessedSize == "—"`（默认值保持）
3. `SetProgress_WithTotalFiles_PopulatesTotalCount` — 初始断言 `"—"` → `TotalFiles > 0` 后等于总数字符串

- 既有测试须全绿（尤其 `SetProgress_WithoutTotalBytes_LeavesTotalSizeEmpty`——新字段与 `StatsTotalSizeValue` 互不干扰）

---

## Work Objectives

### Core Objective

「完整」档统计卡升级为三行表格语义（图标+标题 / 文件数量 / 文件大小），缺数据以灰 `—` 占位，并为 `progress-window-bytes-i18n`（7z/TAR 字节埋点）预留自动填充——该计划完成后本设计**零改动**即让已处理大小行由 `—` 变真实值。

### Concrete Deliverables

- `ProgressWindow.axaml`：统计卡区 6 卡三行重构 + `DashBrushConverter` 资源注册 + 中文注释（规则 14）
- `Converters/DashBrushConverter.cs`：`—`/null/空 → `ThemeTextSecondaryBrush`，否则按 `ConverterParameter` 资源键取强调色画刷
- `ProgressViewModel.cs`：`_statsProcessedSize`、`_statsTotalCount` 两个 `[ObservableProperty]`
- `ProgressViewModelTests.cs`：D6 的 3 条测试
- `docs/PLAN.md`：P2 登记行（规则 1）

### Definition of Done

- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误 0 警告
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 既有测试全绿 + 新增 3 条通过（基线：5 个 `PreviewWebViewLazyInitTests` 预存失败不计入）
- [ ] `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全绿（本设计不改 Core，应零变化）
- [ ] GUI 手动验收：完整档 6 卡三行、`—` 灰显、真实值强调色、中/精简两档无回归
- [ ] `docs/PLAN.md` 已同步（规则 1）

### Must Have

- 6 卡全部三行；行 2 数量、行 3 大小语义正确（严格按 D2 表）
- 缺数据行显 `—` 且灰显（`ThemeTextSecondaryBrush`）
- 卡级显隐（规则 6）与中/精简两档零改动
- 新增 axaml 控件中文注释（规则 14）、间距全部 `{DynamicResource SpacingXxs}`（规则 5）

### Must NOT Have (Guardrails)

- **不得**改任何引擎（`ArchiveProgress` 字节上报归 `progress-window-bytes-i18n` 计划）
- **不得**改卡级可见性绑定；**不得**模板化重构统计卡（方案 B 已否决）
- **不得**新增本地化 key（`—` 是符号；卡片标题复用既有 `Stats*Label` / `Progress_Stats_Parallel`）
- **不得**动中等/精简两档布局
- 版本号不变（规则 2）

---

## Verification Strategy (MANDATORY)

### Test Decision

- **Infrastructure exists**: YES（xunit.v3 + Avalonia.Headless.XUnit，`tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 已有 12+ 条 `SetProgress_*` 相邻测试）
- **Automated tests**: Unit（VM 属性写入行为，D6 3 条）
- **Framework**: xunit.v3，沿用现有测试文件与 `[Fact]` 模式

### AI-Driven QA

- 本任务为 Avalonia 桌面 UI，视觉验收（D3 三行布局、`—` 灰显、强调色）走**人工 GUI 验收**（DoD 第 4 条），不做浏览器 QA

### 自动化验证命令

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```

---

## 依赖与后续

- **依赖**：无硬前置（6 卡结构已由 prototype-alignment 计划落地）
- **后续受益**：`progress-window-bytes-i18n` 完成 7z/TAR 字节上报后，卡 1 行 3（已处理大小）**自动**由 `—` 变真实值，本设计零改动
- **文档同步**：本文件新增 → `docs/PLAN.md` P2 区登记（规则 1）

## 任务分解

> **执行方式**: superpowers:subagent-driven-development（推荐，每任务独立子代理）或 superpowers:executing-plans（本会话批量执行），checkbox 逐步勾选。
> **提交策略**: 任务 1–5 默认**不 commit**（未经用户明确要求不提交）；仅当用户要求提交时执行任务 6，且按规则 3 先更新进度文档。

### 执行波次

```
Wave 1（VM 层，顺序 — 与任务 2 同文件）
├── 任务 1: StatsProcessedSize 字段（TDD）
└── 任务 2: StatsTotalCount 字段（TDD，Blocked By: 任务 1）

Wave 2（UI 层）
├── 任务 3: DashBrushConverter（可与 Wave 1 并行，无文件交集）
└── 任务 4: ProgressWindow.axaml 6 卡三行重构（Blocked By: 1, 2, 3）

Wave FINAL
├── 任务 5: 构建 + 双测试套 + GUI 人工验收（Blocked By: 1–4）
└── 任务 6: 进度文档同步与提交（仅用户要求时；Blocked By: 5）
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

  (1d) 新增字段 —— `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs` 在 `_statsTotalSizeValue` 字段声明（约 line 287）之后追加：

  ```csharp
      /// <summary>统计卡行 3（已处理）：文件大小（引擎上报 TotalBytes 后随进度更新；默认 "—" 行级占位，D2/D4）。</summary>
      [ObservableProperty]
      private string _statsProcessedSize = "—";
  ```

  (1e) 写入点 —— `SetProgress` 的 `if (p.TotalBytes > 0)` 块（约 line 638-643）改为：

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
  - **Can Run In Parallel**: NO（与任务 2 改同一测试文件与 VM 文件）
  - **Blocks**: 任务 4、任务 5
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

  (2d) 写入点 —— `SetProgress` 的 `if (p.TotalFiles > 0)` 块（约 line 630-635）改为：

  ```csharp
          if (p.TotalFiles > 0)
          {
              _statsProcessed = p.ProcessedFiles;
              _statsTotalFiles = p.TotalFiles;
              StatsProcessedText = LocalizationManager.T("Progress_Stats_Processed", $"{_statsProcessed}/{_statsTotalFiles}");
              StatsTotalCount = _statsTotalFiles.ToString();
          }
  ```

  (2e) 重跑 (2b) 同命令，预期：**全部 Passed**（含任务 1 的 2 条与既有 `SetProgress_ZeroTotalFiles_KeepsPreviousFraction`——TotalFiles=0 不进块，不清零）。

  **Must NOT do**:
  - 不加入 `NotifyStatsProperties`（D4）
  - 不改 `StatsProcessedCount` 分数派生逻辑
  - 不改任何引擎

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO（与任务 1 同文件，必须在其后）
  - **Blocks**: 任务 4、任务 5
  - **Blocked By**: 任务 1

  **Acceptance Criteria**:
  - [ ] 1 条新测试存在且通过；任务 1 及既有测试无回归
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

- [ ] 3. `DashBrushConverter` 转换器 + ProgressWindow 资源注册

  **What to do**:

  (3a) 新建 `src/MantisZip.UI.Avalonia/Converters/DashBrushConverter.cs`（完整文件；资源键解析模式对齐既有 `BrushResourceConverter`）：

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
  /// 用于三行统计卡的动态 — 单元格（已处理卡行 3、总大小卡行 2，D3）。
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

  (3b) 注册到 `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` 的 `<Window.Resources>` 内（line 14-27，`RatioToWidthConverter` 之后、`</Window.Resources>` 之前）：

  ```xml
      <!-- 三行统计卡动态 — 画刷：—/空 → 灰 secondary，真实值 → ConverterParameter 强调色（D3） -->
      <converters:DashBrushConverter x:Key="DashBrush"/>
  ```

  (3c) 构建验证：

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  ```

  预期：Build succeeded，0 error 0 warning。

  **Must NOT do**:
  - 不改 `BrushResourceConverter` / 其他既有转换器
  - 不新增本地化 key（`—` 是符号）
  - 转换器不写单测（D6 固定 3 条 VM 测试；行为由任务 5 GUI 验收覆盖）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES（仅与任务 1/2 并行，无文件交集）
  - **Blocks**: 任务 4、任务 5
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
    Evidence: .omo/evidence/task-3-converter-build.txt
  ```

  **Commit**: NO — 未经用户明确要求不提交

---

- [ ] 4. ProgressWindow.axaml —— 6 张统计卡三行重构

  **What to do**:

  替换 `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` 中 6 个卡片 StackPanel（line 583-701，位于统计卡容器 `<StackPanel Orientation="Horizontal" Spacing="{DynamicResource SpacingLg}">` 与其 `</StackPanel>` 之间）。**外层容器（`StatsCardsPanel` Border、水平 StackPanel）与卡级 `IsVisible` 绑定全部保持不动**，仅重构卡内部为三行（D3：行 1 图标 16 + 标题 11 横排 / 行 2 数量 15 SemiBold 原数值色 / 行 3 大小 13）。完整替换代码：

  ```xml
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
                <!-- 行 2：文件数量（N/M 分子分母） -->
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

              <!-- 卡 2：跳过（⏭；引擎上报过冲突统计才出现）— 三行：图标+标题 / 文件数量 / 文件大小（无数据恒 —） -->
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
                <!-- 行 3：文件大小（跳过卡无字节数据，恒 — 灰显） -->
                <TextBlock Text="—" FontSize="13"
                           HorizontalAlignment="Center"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
              </StackPanel>

              <!-- 卡 3：出错（❌；引擎上报过冲突统计才出现）— 三行：图标+标题 / 文件数量 / 文件大小（无数据恒 —） -->
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
                <!-- 行 3：文件大小（出错卡无字节数据，恒 — 灰显） -->
                <TextBlock Text="—" FontSize="13"
                           HorizontalAlignment="Center"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
              </StackPanel>

              <!-- 卡 4：已覆盖（🔄；引擎上报过冲突统计才出现）— 三行：图标+标题 / 文件数量 / 文件大小（无数据恒 —） -->
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
                <!-- 行 3：文件大小（已覆盖卡无字节数据，恒 — 灰显） -->
                <TextBlock Text="—" FontSize="13"
                           HorizontalAlignment="Center"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
              </StackPanel>

              <!-- 卡 5：并行度（⚙；真实 ParallelExtractDegree，degree < 2 串行时整卡隐藏，D3/Rule 6）— 三行：图标+标题 / 线程数 / 无大小数据恒 — -->
              <StackPanel Spacing="{DynamicResource SpacingXxs}"
                          HorizontalAlignment="Center"
                          IsVisible="{Binding HasParallelDegree}">
                <!-- 行 1：图标 + 标题（横排居中，本地化「并行」） -->
                <StackPanel Orientation="Horizontal"
                            Spacing="{DynamicResource SpacingXxs}"
                            HorizontalAlignment="Center">
                  <TextBlock Text="⚙" FontSize="16"
                             VerticalAlignment="Center"
                             Foreground="{DynamicResource ThemeProgressFillBrush}"/>
                  <TextBlock Text="{Binding LocalizedStrings[Progress_Stats_Parallel]}" FontSize="11"
                             VerticalAlignment="Center"
                             Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
                </StackPanel>
                <!-- 行 2：并行线程数 -->
                <TextBlock Text="{Binding ParallelDegree}"
                           FontSize="15" FontWeight="SemiBold"
                           HorizontalAlignment="Center"
                           Foreground="{DynamicResource ThemeProgressFillBrush}"/>
                <!-- 行 3：文件大小（并行度卡无字节数据，恒 — 灰显） -->
                <TextBlock Text="—" FontSize="13"
                           HorizontalAlignment="Center"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"/>
              </StackPanel>

              <!-- 卡 6：总大小（📏；引擎上报 TotalBytes 才出现，7z/TAR 不上报时整卡隐藏，D3/Rule 6）— 三行：图标+标题 / 文件总数（未上报时 — 灰显） / 文件大小（卡可见即必有值） -->
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
  ```

  然后构建验证：

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  ```

  预期：Build succeeded，0 error 0 warning。

  **Must NOT do**:
  - 不改 6 个卡级 `IsVisible` 绑定（规则 6 / D1/D5）
  - 不动中等档 `MediumStatsPanel`（line 705 起）与简约面板（D5 零改动）
  - 不模板化重构统计卡（方案 B 已否决）
  - 不新增本地化 key（规则 13；卡片标题复用既有 `Stats*Label` / `Progress_Stats_Parallel`）
  - 间距全部 `{DynamicResource SpacingXxs}`（规则 5）；新增控件均有中文注释（规则 14）

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO（依赖任务 1、2 的 VM 字段与任务 3 的 `DashBrush` 资源键）
  - **Blocks**: 任务 5
  - **Blocked By**: 任务 1、任务 2、任务 3

  **Acceptance Criteria**:
  - [ ] 6 卡各 3 行；卡 1 行 3 绑定 `StatsProcessedSize` + `DashBrush`（参数 `ThemeStatusSuccessBrush`）
  - [ ] 卡 6 行 2 绑定 `StatsTotalCount` + `DashBrush`（参数 `ThemeTextPrimaryBrush`）；卡 2/3/4/5 行 3 为静态 `—`
  - [ ] Build succeeded

  **QA Scenarios**:

  ```
  Scenario: 构建验证
    Tool: Bash (PowerShell)
    Steps: dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error 0 warning）
    Evidence: .omo/evidence/task-4-xaml-build.txt
  ```

  **Commit**: NO — 未经用户明确要求不提交

---

- [ ] 5. 全量验证：构建 + 双测试套 + GUI 人工验收

  **What to do**:

  (5a) 构建：

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  ```

  预期：0 error 0 warning。

  (5b) Avalonia 测试套：

  ```powershell
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
  ```

  预期：**166 通过**（基线 163 + 新增 3）/ **5 失败**（既有 `PreviewWebViewLazyInitTests` 预存失败，与本计划无关，不计入）。

  (5c) Core 测试套（本计划不改 Core，应零变化）：

  ```powershell
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
  ```

  预期：全绿（基线 615 通过）。

  (5d) GUI 人工验收（运行 `dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`，执行一次解压/压缩进度，密度切「完整」）：

  - 6 卡全部三行；行 1 图标+标题横排居中
  - ZIP 解压（引擎上报字节）→ 卡 1 行 3 显示真实已处理大小（绿色）；7z/TAR → 同格灰 `—`
  - 卡 6 行 2 在引擎上报 TotalFiles 后显示总数，否则灰 `—`
  - 卡 2/3/4/5 行 3 静态灰 `—`
  - 中等/精简两档视觉零变化
  - 暗色主题下 `—` 灰、真实值强调色均正确

  **Must NOT do**:
  - 不为通过测试而删改既有测试

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: 任务 6
  - **Blocked By**: 任务 1、2、3、4

  **Acceptance Criteria**:
  - [ ] DoD 全部勾选（设计文档 Definition of Done 五条）
  - [ ] GUI 验收清单逐条通过

  **QA Scenarios**:

  ```
  Scenario: 双测试套回归
    Tool: Bash (PowerShell)
    Steps: dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj; dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
    Expected Result: Avalonia 166 过 / 5 预存失败；Core 615 过 / 0 失败
    Evidence: .omo/evidence/task-5-test-suites.txt
  ```

  **Commit**: NO — 未经用户明确要求不提交

---

- [ ] 6. 进度文档同步与提交（仅用户明确要求提交时执行）

  **What to do**:

  (6a) **先**按规则 3 更新进度文档（提交前必须完成，双轨）：

  - `docs/PROGRESS.md` → `### MantisZip.UI.Avalonia（主力版）` → `#### 2026-10` 分组（无则新建）顶部追加（从新到旧）：
    ```
    - **10-08** — 进度窗口统计卡三行结构：「完整」档 6 张统计卡改三行表格（图标+标题横排 / 文件数量 / 文件大小），缺数据行灰显 `—`；VM 新增 `StatsProcessedSize`/`StatsTotalCount` + `DashBrushConverter` 动态强调色
    ```
  - `docs/progress-avalonia-detail.md` 顶部 `**2026-10-08**` 分组追加详细条目（VM 两字段写入点、转换器、6 卡 XAML、3 条新测试、中/精简档零改动）

  (6b) 按规则 1 将 `docs/PLAN.md` 中本任务行（line 36）移至 `docs/PROGRESS.md` 的【历史设计方案索引】章节（计划完成归档）。

  (6c) 提交：

  ```powershell
  git add src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs src/MantisZip.UI.Avalonia/Converters/DashBrushConverter.cs src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs docs/PROGRESS.md docs/progress-avalonia-detail.md docs/PLAN.md
  git commit -m "feat(avalonia): 进度窗口统计卡三行结构（数量+大小）"
  ```

  注意：若工作树还有本计划之外的未提交改动（如手工测试 3 修复），不得混入本 commit，只 stage 上列文件。

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
  - **Blocked By**: 任务 5

  **Acceptance Criteria**:
  - [ ] 双轨进度文档已更新且条目排序从新到旧
  - [ ] PLAN.md 任务行已归档至 PROGRESS.md 历史索引
  - [ ] commit 仅含上列文件，信息符合规则 10

  **QA Scenarios**:

  ```
  Scenario: 提交前文档与暂存区核查
    Tool: Bash (PowerShell)
    Steps: git status; git diff --cached --stat
    Expected Result: 仅上列文件暂存；PROGRESS.md 与 progress-avalonia-detail.md 均含 10-08 新条目
    Evidence: .omo/evidence/task-6-commit.txt
  ```

  **Commit**: YES（条件执行）— `feat(avalonia): 进度窗口统计卡三行结构（数量+大小）`
