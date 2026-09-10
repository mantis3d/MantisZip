# 压缩预览渐进式加载（浅层先行 + 按源缓存 + 渐进上屏）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

## TL;DR

> **Quick Summary**: 压缩设置窗口的预览树目前对大源目录是"整棵递归枚举完成后一次性上屏"，期间显示不定进度覆层。本计划改为**浅层先行**（所有源先枚举 2 层、每目录宽度上限 20、深层挂「… 加载中」占位）→ **按源缓存**（已枚举完的源不重扫）→ **渐进上屏**（每个源全量完成后立即挂完整子树）→ **取消机制**（新构建取消旧构建，已完成源先落缓存）。顺带修复两个性能问题：① 往文件列表加新目录会丢弃已完成源的全量枚举；② 输出文件名**每按一个键**都会触发全量磁盘重扫。
>
> **Deliverables**:
> - `Models/PreviewTreeNode.cs` 新增 `IsLoadingPlaceholder`（含 `DisplayLabel`/`IconKey`/`IsTruncatedNode`/`DirectoryInfoText`/`ShallowClone` 适配）
> - `Services/ResultPreviewService.cs` 拆分为 `BuildSourceSubtree`（单源构建，含 `maxDepth`/`maxWidthPerDir`/`CancellationToken`）+ `AssembleCompressPreview`（装配），`BuildCompressPreview` 退化为薄包装
> - `ViewModels/CompressSettingsViewModel.cs` 两阶段编排 + 按源缓存 + 过滤签名失效 + 取消 + 节流
> - `Controls/ResultTreeView.axaml(.cs)` 重组前后保存/恢复 `ScrollViewer.Offset`
> - 本地化新增 `Preview_Result_LoadingMore`
> - 单元测试（浅层结构 / 宽度上限 / 占位 / 全量不变 / 过滤一致性）
> - `docs/PLAN.md` 同步（规则 1）
>
> **Estimated Effort**: Large
> **Parallel Execution**: YES - 2 waves + sequential tail + final review wave
> **Critical Path**: Task 1 (占位节点) → Task 2 (服务拆分) → Task 4 (VM 编排) → Task 5 (测试) → F1-F4 → user okay
> **Parallel Speedup**: ~30% vs sequential

---

## Context

### Original Request

用户以疑问方式提出（"你看能不能改成每隔 0.5 秒把已经加载那部分显示出来？先跟我讨论"），目标是**压缩设置窗口**（`CompressSettingsWindow`）的 `ResultTreeView` 预览树在大源目录下不要长时间停在加载覆层。经讨论确定方案：浅层先行（源目录 + 直接子项，深层挂「… 加载中」占位）+ 宽度上限 20 + 按源缓存 + 每个源全量完成后渐进上屏 + 取消机制 + 滚动位置保留。

### Interview Summary

**Key Discussions**:

- **作用范围**：**仅压缩设置窗口**。解压预览从条目列表建树、瓶颈不同，不在本次范围。
- **浅层语义**：以 Manual/Combined 树为例，`root(输出父目录) → archive 节点 → 源目录(depth 0) → 直接子项(depth 1)`；即 **`maxDepth = 1`**。深层子目录挂「… 加载中」占位。
- **宽度上限**：浅层阶段**每个目录最多 20 个直接子项（目录优先，合计 20）**，超出挂占位；必须用惰性枚举 `EnumerateDirectories()/EnumerateFiles()` 提前停，不能全取再截断。
- **按源缓存**：缓存 `源路径 → (子树节点, IncludedFiles 白名单)`；加新源只枚举新源；改输出路径/模式/格式只重组不重扫；过滤条件变化则全部失效；删除源丢弃对应缓存。
- **渐进上屏**：全量阶段**串行**逐源枚举，每完成一个源就把它的完整子树替换进缓存并重组上屏。
- **取消机制**：VM 每次新建构建时 cancel 上一个 `CancellationTokenSource`；`BuildDirectoryNode` 枚举循环检查 token；**已完成的源先落缓存再取消**（用户场景：5 个源枚举完 4 个时加第 6 个，前 4 个不丢）。
- **门禁**：`IsBuildPending` 保持到**最后一个源**全量完成（`IncludedFiles` 白名单必须全量枚举才完整），期间「开始压缩」禁用。
- **滚动保留**：重组前后保存/恢复 `ScrollViewer.Offset`（渐进上屏会重组 N 次，不保留会 N 次回顶）。
- **线程**：装配 + `CalculateDescendantStats` 在后台线程算，仅 `PreviewRoot` 赋值切回 UI 线程。
- **节流**：全量阶段多个源快速连续完成时，250ms 内的中间重组可跳过（最后一次组装必然执行，不丢源）。
- **占位文案**：复用模板既有「…」前缀，`DisplayLabel` 取「加载中」→ 渲染为「… 加载中」。

**Research Findings**（已核实，勿重复调研）:

- `ResultTreeView` 是纯被动控件：`Root` 变化 → `RebuildDisplayTree()`（深拷贝整树 + 过滤 + 精简 + 摘要 + 冲突计数，全在 UI 线程）。控件本身不做加载。加载覆层由 `IsLoading`（绑 VM `IsPreviewBuilding`）驱动（`ResultTreeView.axaml.cs:200-224`）。
- 压缩预览链路：`CompressSettingsViewModel.BuildCompressPreview(filter)` → `_pendingBuildTask = BuildCompressPreviewCoreAsync(filter)`（`:561-565`）→ `Task.Run(ResultPreviewService.BuildCompressPreview(...))`（`:608`）→ 250ms 阈值后 `IsPreviewBuilding = true`（`:619-625`）→ 完成才 `PreviewRoot = root`（`:630`）。
- 枚举瓶颈：`ResultPreviewService.BuildDirectoryNode`（`:506-556`）整棵递归 `GetDirectories()/GetFiles()` + 每文件 `FileInfo.Length`；`catch { }` 吞所有异常（`:550-553`）——**必须让 `OperationCanceledException` 穿透**。
- 触发重建的调用点（`CompressSettingsViewModel.cs`）：`SelectedPaths.CollectionChanged`（`:415-425`）、输出模式切换（`:699`）、`OnOutputPathChanged`（`:707`，**无条件**）、格式切换、过滤变化（`CompressSettingsWindow.axaml.cs:236 OnFileFilterChanged` → `BuildPreview()` → `:233 BuildCompressPreview(filter)`）。
- **打字卡顿根因**：`OutputFileName` 绑定在 TextBox（`CompressSettingsWindow.axaml:167`），Avalonia 默认逐键更新 → `ComposeManualOutputPath()`（`:770-790`）设置 `OutputPath` → `OnOutputPathChanged`（`:702`）无条件 `BuildCompressPreview()` → 每键一次全量磁盘重扫。
- `FileFilterCriteria`（`MantisZip.Core/FileFilter/FileFilterCriteria.cs:13`）是**普通 class，无值相等语义**；`GetFilter()` 每次新建实例 → 缓存失效必须用**签名比较**，不能用引用比较。
- `AdoptPlan`（`:372-378`）：主窗口从对话框接管 Plan（`MainWindow.axaml.cs:229`），接管后源集可能已变 → 需 cancel 在途 + 清缓存。
- `EnsurePlanReadyAsync`（`:1130-1150`）await `_pendingBuildTask` 并循环等待最新构建；`CanExecuteStartCompress`（`:1079-1087`）用 `IsBuildPending` 门禁。
- `PreviewTreeNode.IsEmptyDirectory => IsDirectory && TotalDescendantCount == 0`（`:51`）；`CalculateDescendantStats`（`ResultPreviewService.cs:468-497`）把 `!IsDirectory` 子节点计为文件 → 占位节点设 `IsDirectory=false` 即让父目录不被判为空。
- 测试项目：`tests/MantisZip.UI.Avalonia.Tests/`，**xunit.v3 3.2.2 + Avalonia.Headless.XUnit 12.0.4**，用 `[AvaloniaFact]`（见 `ResultTreeViewFilterTests.cs:83`）。
- 现有 `ResultTreeViewFilterTests` 只用 `BuildExtractPreview`，不受本计划影响。

### Metis Review（预审要点，已纳入）

- **B 数据集门禁**：浅树/渐进树期间 `IncludedFiles` 不完整 → `IsBuildPending` 必须保持到最后一源完成，`Plan` 只在全量完成后赋值。
- **取消必须穿透 catch**：`BuildDirectoryNode` 的 `catch { }` 会吞 `OperationCanceledException`，必须先 `catch (OperationCanceledException) { throw; }`。
- **过滤签名**：`FileFilterCriteria` 无值相等 → 用签名字符串比较，否则每键/每次重建都误清缓存。
- **占位节点与空目录判定**：占位节点 `IsDirectory=false` → 被 `CalculateDescendantStats` 计为 1 个"文件" → 父目录 `IsEmptyDirectory=false`（显示 folder 图标而非空文件夹）；但摘要计数（`CountTotalFiles`）与目录信息行（`DirectoryInfoText`）要显式排除占位，避免"1 项"与虚高文件数。

---

## Work Objectives

### Core Objective

把压缩设置窗口预览树从"全量枚举后一次性上屏"改造为"浅层先行 + 按源缓存 + 渐进上屏 + 可取消"，并顺带修复"加目录丢弃已完成枚举"与"输出文件名逐键全量重扫"两个性能问题。

### Concrete Deliverables

- `Models/PreviewTreeNode.cs`：新增 `IsLoadingPlaceholder` 及全部派生属性适配
- `Services/ResultPreviewService.cs`：新增 `SourceSubtree` record、`BuildSourceSubtree`、`AssembleCompressPreview`；重构 `BuildDirectoryNode`（`depth`/`maxDepth`/`maxWidthPerDir`/`ct`/占位）；`BuildCompressPreview` 退化为包装
- `ViewModels/CompressSettingsViewModel.cs`：`_sourceCache`、`_cacheFilterSignature`、`_previewCts`、`FilterSignature`、`InvalidateSourceCache`；重写 `BuildCompressPreviewCoreAsync` 为两阶段；`AdoptPlan` 增加 cancel + 清缓存
- `Controls/ResultTreeView.axaml` + `.axaml.cs`：`ScrollViewer` 命名 + 重组前后 Offset 保存/恢复
- `Localization/strings.zh-CN.json` + `strings.en.json`：`Preview_Result_LoadingMore`
- `tests/MantisZip.UI.Avalonia.Tests/CompressPreviewProgressiveTests.cs`：新增单测
- `docs/PLAN.md`：新增 P2 行（规则 1）

### Definition of Done

- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded（0 error）
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿（含新增单测 + 既有 `ResultTreeViewFilterTests` 无回归）
- [ ] `lsp_diagnostics` 对全部改动文件无错误
- [ ] Agent 运行 App 截图验证：大源目录压缩预览 → 先出现「源目录 + 一级子项 + … 加载中」，随后逐源变完整；输出文件名框连续输入不卡顿
- [ ] `docs/PLAN.md` 已同步（规则 1）

### Must Have

- 浅层阶段：所有源 `maxDepth=1`、`maxWidthPerDir=20`（目录优先），深层/超宽挂「… 加载中」占位
- 按源缓存：`源路径 → SourceSubtree`，加新源不重扫已完成源
- 渐进上屏：全量阶段串行逐源，每源完成即重组上屏
- 取消：新构建 cancel 旧构建；`OperationCanceledException` 穿透 `BuildDirectoryNode`；已完成源保留在缓存
- 过滤条件变化 → 缓存全量失效（签名比较）
- `IsBuildPending` 保持到最后一源完成；`Plan` 仅在全部完成后赋值
- 装配 + `CalculateDescendantStats` 后台线程，`PreviewRoot` 赋值回 UI 线程
- `ScrollViewer.Offset` 重组前后保存/恢复
- 占位节点不计入摘要文件数，且不让父目录被判为空目录
- 新增用户可见文案走本地化（规则 13），zh/en 成对

### Must NOT Have (Guardrails)

- **不得**修改 `ResultTreeView` 的 `RebuildDisplayTree` 语义（过滤/精简/摘要/冲突逻辑保持）
- **不得**触碰解压预览（`BuildExtractPreview`）、`CustomFilePickerDialog`、`DragPreviewBitmapBuilder`
- **不得**触碰 WPF `MantisZip.UI`（规则 11）
- **不得**改 Core 层 `FileFilterCriteria`/`FileFilterMatcher`/`CompressPathPlanner` 的语义
- **不得**把占位节点用 `IsTruncated=true` 复用（语义与精简截断混用，且现有 `ResultTreeViewFilterTests` 断言 `IsTruncated`）
- **不得**在 UI 线程跑全量枚举/装配
- **不得**新增未请求的动画/过渡打磨（AI slop）
- **不得**硬编码间距/高度（规则 5）
- 版本号不变（规则 2：未经用户许可不改版本）

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** - ALL verification is agent-executed.

### Test Decision

- **Infrastructure exists**: YES（`tests/MantisZip.UI.Avalonia.Tests/`，xunit.v3 + Avalonia.Headless.XUnit）
- **Automated tests**: Unit tests（服务层纯逻辑 + 真实临时目录枚举 + 占位节点属性）
- **Framework**: xUnit v3，UI 相关用 `[AvaloniaFact]`
- **Agent QA**: 构建 + 单测 + 可选 App 截图（无头环境受限时以单测 + 构建为准，需在证据中说明）

### QA Policy

Every task MUST include agent-executed QA scenarios. Evidence saved to `.omo/evidence/task-{N}-{scenario-slug}.{ext}`.

- **类库逻辑**：单元测试（`dotnet test`），用 `Path.GetTempPath()` 下临时目录构造真实文件树
- **UI 渲染**：构建通过 + 单测覆盖纯逻辑；桌面截图在无头环境受限时以单测为准并注明

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Start Immediately):
├── Task 1: PreviewTreeNode 占位节点支持 + 本地化 key [quick]
└── Task 3: ResultTreeView 滚动位置保留 [quick]

Wave 2 (After Task 1):
└── Task 2: ResultPreviewService 拆分（BuildSourceSubtree + AssembleCompressPreview + 深度/宽度/取消）[deep]

Wave 3 (After Task 2, Task 3):
└── Task 4: CompressSettingsViewModel 两阶段编排 + 按源缓存 + 渐进上屏 + 取消 [deep]

Wave 4 (After Task 4):
└── Task 5: 单元测试（浅层/宽度/占位/全量/过滤）+ 文档同步 [deep]

Wave FINAL (After ALL tasks — 4 parallel reviews, then user okay):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Real manual QA (unspecified-high)
└── Task F4: Scope fidelity check (deep)
-> Present results -> Get explicit user okay

Critical Path: Task 1 → Task 2 → Task 4 → Task 5 → F1-F4 → user okay
Parallel Speedup: ~30% faster than sequential
Max Concurrent: 2 (Wave 1)
```

### Dependency Matrix

- **1**: - → blocks 2（占位节点类型被服务引用）
- **2**: 1 → blocks 4（VM 依赖新服务 API）
- **3**: - → blocks 4（VM 渐进上屏依赖滚动保留才有意义，但不阻塞编译；可与 1/2 并行）
- **4**: 2, 3 → blocks 5
- **5**: 1,2,3,4 → blocks FINAL
- **F1-F4**: 1-5 → blocks user okay

### Agent Dispatch Summary

- **Wave 1**: 2 tasks — T1 → `quick`, T3 → `quick`
- **Wave 2**: 1 task — T2 → `deep`
- **Wave 3**: 1 task — T4 → `deep`
- **Wave 4**: 1 task — T5 → `deep`
- **FINAL**: 4 tasks — F1 → `oracle`, F2 → `unspecified-high`, F3 → `unspecified-high`, F4 → `deep`

---

## TODOs

> Implementation + Test = ONE Task. Every task MUST have: Recommended Agent Profile + Parallelization + QA Scenarios.

- [ ] 1. `PreviewTreeNode` 加载占位节点支持 + 本地化 key

  **What to do**:

  在 `src/MantisZip.UI.Avalonia/Models/PreviewTreeNode.cs` 新增 `IsLoadingPlaceholder` 并适配所有派生属性：

  ```csharp
  /// <summary>是否为「深层未加载」占位节点（浅层先行阶段的 "… 加载中"）。</summary>
  public bool IsLoadingPlaceholder { get; set; }
  ```

  `DisplayLabel` getter 覆盖为占位时返回本地化文案（保持 setter 不变）：

  ```csharp
  public string DisplayLabel
  {
      get => IsLoadingPlaceholder
          ? LocalizationManager.T("Preview_Result_LoadingMore")
          : (string.IsNullOrEmpty(_displayLabel) ? Name : _displayLabel);
      set => _displayLabel = value;
  }
  ```

  `IsTruncatedNode`（让模板渲染既有「…」前缀）：

  ```csharp
  public bool IsTruncatedNode => IsTruncated || IsLoadingPlaceholder;
  ```

  `IconKey` 占位无图标（与 `IsTruncated` 同分支）：

  ```csharp
  if (IsTruncated || IsLoadingPlaceholder) return null;
  ```

  `DirectoryInfoText` 排除"仅含占位子节点"的目录（避免显示 "1 项 · 0 B"），并新增私有辅助：

  ```csharp
  public string DirectoryInfoText =>
      !IsEmptyDirectory && Children.Count > 0 && !string.IsNullOrEmpty(FullPath) && !HasLoadingPlaceholderChild
          ? LocalizationManager.T("Preview_Result_DirInfo", TotalDescendantCount, FormatUtil.FormatSize(TotalDescendantSize))
          : string.Empty;

  private bool HasLoadingPlaceholderChild =>
      Children.OfType<PreviewTreeNode>().Any(c => c.IsLoadingPlaceholder);
  ```

  `ShallowClone()` 中补 `IsLoadingPlaceholder = IsLoadingPlaceholder,`。

  > 注意：**不要**改 `IsEmptyDirectory`。占位节点 `IsDirectory=false`，会被 `CalculateDescendantStats` 计为 1 个"文件"，使父目录 `TotalDescendantCount >= 1` → 父目录不显示空文件夹图标。这是刻意设计（见 Metis Review）。

  在 `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` 与 `strings.en.json` **成对**新增（插入到文件头 `{` 之后，保持 UTF-8 无 BOM + CRLF + 2 空格缩进）：

  ```json
  "Preview_Result_LoadingMore": "加载中",
  "Preview_Result_LoadingMore": "Loading",
  ```

  **Must NOT do**:
  - 不改 `IsEmptyDirectory`（占位靠 `IsDirectory=false` 参与统计）
  - 不把占位节点设成 `IsTruncated=true`（与精简截断语义冲突，破坏 `ResultTreeViewFilterTests`）
  - 不改 `TextOpacity`/`ForegroundKey`/`SizeDisplay` 语义
  - 不新增除 `Preview_Result_LoadingMore` 外的 key

  **Recommended Agent Profile**:
  - **Category**: `quick` — 单文件属性适配 + 两个 JSON 各加一行
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Task 3)
  - **Blocks**: Task 2
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI.Avalonia/Models/PreviewTreeNode.cs:16-21`（DisplayLabel）、`:78`（IsTruncated）、`:90-105`（IconKey/IsTruncatedNode）、`:69-72`（DirectoryInfoText）、`:154-181`（ShallowClone）
  - `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml:115-121`（模板「…」前缀 + DisplayLabel 绑定）——占位渲染为「… 加载中」的依据
  - `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` / `strings.en.json`（现有 `Preview_Result_*` key 锚点）

  **Acceptance Criteria**:
  - [ ] `PreviewTreeNode` 存在 `IsLoadingPlaceholder` 属性
  - [ ] `IsTruncatedNode`、`IconKey`、`DisplayLabel`、`DirectoryInfoText`、`ShallowClone` 已适配
  - [ ] 两个 strings 文件均含 `Preview_Result_LoadingMore`，key 集保持同步
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios**:

  ```
  Scenario: 占位节点属性语义
    Tool: read + 后续单测（Task 5）
    Preconditions: 属性已实现
    Steps:
      1. read PreviewTreeNode.cs 断言 IsLoadingPlaceholder 存在、IsTruncatedNode/IconKey 含该分支、ShallowClone 复制该字段
      2. 断言 IsEmptyDirectory 未被修改
    Expected Result: 上述断言为真
    Failure Indicators: 占位用 IsTruncated 复用、IsEmptyDirectory 被改、ShallowClone 漏复制
    Evidence: .omo/evidence/task-1-node.txt

  Scenario: 构建 + i18n key 成对
    Tool: Bash
    Steps:
      1. 运行 dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
      2. grep 两个 strings 文件 Preview_Result_LoadingMore
    Expected Result: Build succeeded；两文件各 1 处命中
    Failure Indicators: 编译错误、key 只加了一个文件
    Evidence: .omo/evidence/task-1-build.txt
  ```

  **Evidence to Capture:** `task-1-node.txt`, `task-1-build.txt`

  **Commit**: YES (groups with 3) — `feat(avalonia): PreviewTreeNode 新增深层加载占位节点支持`

---

- [ ] 2. `ResultPreviewService` 拆分：`BuildSourceSubtree` + `AssembleCompressPreview` + 深度/宽度/取消

  **What to do**:

  重构 `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs`，把「单源构建」与「装配」分离。

  **(2a) 新增 `SourceSubtree` record（public，同文件）**：

  ```csharp
  /// <summary>单个源（文件或目录）的构建结果。</summary>
  /// <param name="Node">该源的子树节点（浅层或全量）。</param>
  /// <param name="IncludedFiles">该源内经过滤的匹配文件绝对路径清单（供 B 数据集白名单回填）。</param>
  /// <param name="IsFull">是否为全量构建（false = 浅层阶段产物，后续需被全量替换）。</param>
  public sealed record SourceSubtree(PreviewTreeNode Node, List<string> IncludedFiles, bool IsFull);
  ```

  **(2b) 新增 `BuildSourceSubtree`**（不存在路径返回 `null`，与原逻辑"跳过不存在源"一致）：

  ```csharp
  public static SourceSubtree? BuildSourceSubtree(
      string sourcePath,
      FileFilterCriteria? filter = null,
      int maxDepth = int.MaxValue,
      int maxWidthPerDir = int.MaxValue,
      CancellationToken ct = default)
  {
      ct.ThrowIfCancellationRequested();
      var included = new List<string>();

      if (Directory.Exists(sourcePath))
      {
          var node = BuildDirectoryNode(sourcePath, sourcePath, filter, included, maxDepth, maxWidthPerDir, ct);
          return new SourceSubtree(node, included, IsFull: maxDepth == int.MaxValue && maxWidthPerDir == int.MaxValue);
      }

      if (File.Exists(sourcePath))
      {
          var fi = new FileInfo(sourcePath);
          var isFiltered = filter != null && filter.IsActive && !FileFilterMatcher.IsMatch(filter, sourcePath);
          var fileNode = new PreviewTreeNode
          {
              Name = fi.Name,
              FullPath = fi.Name,
              Size = fi.Length,
              SizeDisplay = FormatUtil.FormatSize(fi.Length),
              IsExpanded = false,
              IsFilteredOut = isFiltered
          };
          if (!isFiltered) included.Add(sourcePath);
          return new SourceSubtree(fileNode, included, IsFull: true);
      }

      return null;
  }
  ```

  **(2c) 重构 `BuildDirectoryNode`**（签名新增 `maxDepth`/`maxWidthPerDir`/`ct`/`depth`；边界挂占位；取消穿透 catch）：

  ```csharp
  private static PreviewTreeNode BuildDirectoryNode(
      string rootPath, string currentPath, FileFilterCriteria? filter, List<string>? includedFiles,
      int maxDepth, int maxWidthPerDir, CancellationToken ct, int depth = 0)
  {
      ct.ThrowIfCancellationRequested();

      var dirInfo = new DirectoryInfo(currentPath);
      var relativePath = currentPath.Length >= rootPath.Length
          ? currentPath[rootPath.Length..].TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/')
          : dirInfo.Name;

      var node = new PreviewTreeNode
      {
          Name = dirInfo.Name,
          FullPath = string.IsNullOrEmpty(relativePath) ? dirInfo.Name : relativePath,
          IsExpanded = false,
          IsDirectory = true
      };

      try
      {
          // 深度边界：不再下钻；非空则挂「… 加载中」占位
          if (depth >= maxDepth)
          {
              if (HasAnyEntry(dirInfo))
                  node.Children.Add(CreateLoadingPlaceholder(node.FullPath));
              return node;
          }

          var capped = maxWidthPerDir != int.MaxValue;
          int added = 0;

          // 子目录优先（保证目录结构不被海量文件挤占）
          foreach (var subDir in dirInfo.EnumerateDirectories())
          {
              ct.ThrowIfCancellationRequested();
              if (capped && added >= maxWidthPerDir) { node.Children.Add(CreateLoadingPlaceholder(node.FullPath)); break; }
              node.Children.Add(BuildDirectoryNode(rootPath, subDir.FullName, filter, includedFiles, maxDepth, maxWidthPerDir, ct, depth + 1));
              added++;
          }

          // 文件（在剩余宽度预算内）
          foreach (var file in dirInfo.EnumerateFiles())
          {
              ct.ThrowIfCancellationRequested();
              if (capped && added >= maxWidthPerDir) { node.Children.Add(CreateLoadingPlaceholder(node.FullPath)); break; }
              var fileRelPath = string.IsNullOrEmpty(relativePath) ? file.Name : $"{relativePath}/{file.Name}";
              var isFiltered = filter != null && filter.IsActive && !FileFilterMatcher.IsMatch(filter, file.FullName);
              node.Children.Add(new PreviewTreeNode
              {
                  Name = file.Name,
                  FullPath = fileRelPath,
                  Size = file.Length,
                  SizeDisplay = FormatUtil.FormatSize(file.Length),
                  IsExpanded = false,
                  IsFilteredOut = isFiltered
              });
              if (!isFiltered && includedFiles != null) includedFiles.Add(file.FullName);
              added++;
          }
      }
      catch (OperationCanceledException)
      {
          throw; // 取消必须向上传播，不能被"跳过不可访问目录"吞掉
      }
      catch
      {
          // Skip inaccessible directories
      }

      return node;
  }

  private static bool HasAnyEntry(DirectoryInfo dir)
  {
      try { return dir.EnumerateFileSystemInfos().Any(); }
      catch { return false; }
  }

  private static PreviewTreeNode CreateLoadingPlaceholder(string parentFullPath) => new()
  {
      Name = string.Empty,
      FullPath = parentFullPath + "/…",
      IsLoadingPlaceholder = true
  };
  ```

  **(2d) 新增 `AssembleCompressPreview`**：把原 `BuildCompressPreview` 的装配逻辑改为消费已构建的 `subtrees`（与 `sourcePaths` 同序，可含 null）：

  ```csharp
  public static (PreviewTreeNode Root, CompressPlan Plan) AssembleCompressPreview(
      IReadOnlyList<string> sourcePaths,
      IReadOnlyList<SourceSubtree?> subtrees,
      CompressOutputMode outputMode,
      string? outputPath,
      string format,
      bool keepOriginalExtension,
      FileFilterCriteria? filter)
  ```

  逻辑：
  - `includedBySource`：遍历 `sourcePaths`/`subtrees`，非 null 时 `[sourcePaths[i]] = subtrees[i].IncludedFiles`
  - **Manual/Combined**：沿用原 `:194-225` 逻辑算 `effectiveOutputPath`；root = 输出父目录；建 archive 节点（`ExistsAtDestination = File.Exists(effectiveOutputPath)`）；`foreach subtree (非 null) archiveNode.Children.Add(subtree.Node)`；`archiveNode.IsArchiveEmpty = !NodeHasVisibleContent(archiveNode)`；`planItems = CompressPathPlanner.PlanSingle(...)`
  - **Separate**：沿用原 `:228-243` 逻辑；`planItems = CompressPathPlanner.PlanSeparate(...)`；装配改为消费 `subtrees`（把原 `BuildSeparateArchivesPreview` 中"`BuildDirectoryNode(sourcePath,...)`/文件节点"替换为 `subtrees[i].Node`，分组与 archive 节点构建逻辑不变）
  - `CalculateDescendantStats(root)`；`root.IsExpanded = true`
  - `filterActive = filter?.IsActive == true`；为 true 时回填 `planItems = planItems.Select(item => includedBySource.TryGetValue(item.SourcePath, out var list) ? item with { IncludedFiles = list } : item).ToList()`（语义与原 `:262-269` 一致）
  - 返回 `(root, new CompressPlan(outputMode, planOutputPath, planItems))`

  **(2e) `BuildCompressPreview` 退化为薄包装**（保持现有签名，供既有调用/测试）：

  ```csharp
  public static (PreviewTreeNode Root, CompressPlan Plan) BuildCompressPreview(
      IReadOnlyList<string> sourcePaths, string? rootName = null, FileFilterCriteria? filter = null,
      CompressOutputMode outputMode = CompressOutputMode.Manual, string? outputPath = null,
      string format = "zip", bool keepOriginalExtension = false)
  {
      var subtrees = sourcePaths.Select(p => BuildSourceSubtree(p, filter)).ToList();
      return AssembleCompressPreview(sourcePaths, subtrees, outputMode, outputPath, format, keepOriginalExtension, filter);
  }
  ```

  删除原 `BuildSingleArchivePreview`（其逻辑并入 `AssembleCompressPreview` 的 Manual/Combined 分支）；把原 `BuildSeparateArchivesPreview` 改造为消费 `subtrees`：

  ```csharp
  private static void BuildSeparateArchivesPreview(
      PreviewTreeNode root, IReadOnlyList<string> sourcePaths, IReadOnlyList<SourceSubtree?> subtrees,
      IReadOnlyList<CompressPlanItem> planItems, string format, bool keepOriginalExtension)
  ```

  其分组（按输出父目录）、archive 节点构建、`IsArchiveEmpty` 判定逻辑**逐字保留**，只把「`BuildDirectoryNode(sourcePath,...)` / 文件节点」两处替换为「`subtrees[i].Node`」（非 null 时才挂）。

  **Must NOT do**:
  - 不改 `CalculateDescendantStats`（占位 `IsDirectory=false` 自动计为文件，父目录不判空）
  - 不改 `BuildExtractPreview` 及其相关方法（解压预览不在范围）
  - 不改 `CompressPathPlanner`/`FileFilterMatcher`（Core 层）
  - 不删 `catch { }`（不可访问目录仍跳过），但必须让 `OperationCanceledException` 先 `throw`
  - 不新增 public API 之外的本地化 key

  **Recommended Agent Profile**:
  - **Category**: `deep` — 跨方法重构 + 取消语义 + 浅层/全量双路径，需自主理解原装配逻辑
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2
  - **Blocks**: Task 4
  - **Blocked By**: Task 1（占位节点 `IsLoadingPlaceholder`）

  **References**:
  - `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs:172-272`（`BuildCompressPreview` 装配逻辑）、`:283-346`（`BuildSingleArchivePreview`）、`:362-455`（`BuildSeparateArchivesPreview`）、`:506-556`（`BuildDirectoryNode` 原实现）、`:468-497`（`CalculateDescendantStats`，不改）
  - `src/MantisZip.UI.Avalonia/Models/PreviewTreeNode.cs`（Task 1 产出的 `IsLoadingPlaceholder`）
  - `src/MantisZip.Core/Services/CompressPathPlanner.cs`（`PlanSingle`/`PlanSeparate`/`ComputeArchiveName`，只读消费）

  **Acceptance Criteria**:
  - [ ] `SourceSubtree`、`BuildSourceSubtree`、`AssembleCompressPreview` 存在且签名如上
  - [ ] `BuildDirectoryNode` 支持 `maxDepth`/`maxWidthPerDir`/`ct`；`OperationCanceledException` 穿透；边界/超宽挂占位
  - [ ] `BuildCompressPreview` 行为与改造前等价（默认 `maxDepth`/`maxWidthPerDir` = `int.MaxValue`）
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios**:

  ```
  Scenario: 浅层结构 + 占位（真实临时目录）
    Tool: Bash (dotnet test，Task 5 单测前置；本任务先保证 API 存在)
    Preconditions: BuildSourceSubtree 已实现
    Steps:
      1. 构造临时目录 root/{a/b/c.txt, a/d.txt, e.txt}（a/b 为深层）
      2. 调 BuildSourceSubtree(root, maxDepth:1, maxWidthPerDir:20)
      3. 断言: 节点含 a、e.txt；a 下含 d.txt 与占位（b 为非空目录 → 占位）；b 不出现在浅树
    Expected Result: 结构与预期一致，深层挂 IsLoadingPlaceholder
    Failure Indicators: 递归未停（出现 b/c.txt）、占位缺失、空目录也挂占位
    Evidence: .omo/evidence/task-2-shallow.txt

  Scenario: 取消穿透
    Tool: Bash (dotnet test)
    Steps:
      1. 传入已 cancel 的 CancellationToken 调 BuildSourceSubtree
      2. 断言抛 OperationCanceledException（而非被 catch 吞掉返回空节点）
    Expected Result: 抛 OperationCanceledException
    Failure Indicators: 静默返回、返回部分树
    Evidence: .omo/evidence/task-2-cancel.txt

  Scenario: 构建验证
    Tool: Bash
    Steps: 运行 dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）
    Failure Indicators: 编译错误
    Evidence: .omo/evidence/task-2-build.txt
  ```

  **Evidence to Capture:** `task-2-shallow.txt`, `task-2-cancel.txt`, `task-2-build.txt`

  **Commit**: YES (groups with 1) — `refactor(avalonia): ResultPreviewService 拆分单源构建与装配并支持深度/宽度/取消`

---

- [ ] 3. `ResultTreeView` 滚动位置保留

  **What to do**:

  **(3a)** 在 `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml:90` 的 `<ScrollViewer>` 加 `x:Name="TreeScrollViewer"`：

  ```xml
  <ScrollViewer x:Name="TreeScrollViewer" Background="{DynamicResource ThemeSurfaceBgBrush}">
  ```

  **(3b)** 在 `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml.cs` 的 `RebuildDisplayTree()`（`:320-363`）中，重建前保存、重建后恢复滚动偏移：

  ```csharp
  private void RebuildDisplayTree()
  {
      // 保存当前显示树的展开状态（用户手动展开的节点）
      var expandedPaths = new HashSet<string>();
      foreach (var node in DisplayNodes)
          CollectExpandedPaths(node, expandedPaths);

      // 保存滚动位置（渐进式上屏会多次重组，不保留会反复回顶）
      var savedOffset = TreeScrollViewer?.Offset ?? default;

      DisplayNodes.Clear();
      ...（既有逻辑不变）...

      // 恢复滚动位置：布局完成后（Loaded 优先级）设置，避免被 TreeView 重建覆盖
      if (TreeScrollViewer != null && savedOffset != default)
      {
          var target = savedOffset;
          Dispatcher.UIThread.Post(() => TreeScrollViewer.Offset = target, DispatcherPriority.Loaded);
      }
  }
  ```

  需要的 using：`Avalonia.Threading`（`Dispatcher`/`DispatcherPriority`）。

  **Must NOT do**:
  - 不改 `RebuildDisplayTree` 的过滤/精简/摘要/冲突逻辑
  - 不改树节点模板
  - 不引入固定高度/间距硬编码

  **Recommended Agent Profile**:
  - **Category**: `quick` — 单控件两处小改（x:Name + 保存/恢复）
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Task 1)
  - **Blocks**: Task 4（渐进上屏依赖滚动保留的 UX，但不阻塞编译）
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml:89-144`（ScrollViewer + TreeView 结构）
  - `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml.cs:320-363`（`RebuildDisplayTree`）
  - `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml.cs:171-195`（静态构造函数/属性变更回调）

  **Acceptance Criteria**:
  - [ ] `TreeScrollViewer` 已命名
  - [ ] `RebuildDisplayTree` 重组前后保存/恢复 `Offset`
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios**:

  ```
  Scenario: 滚动保留代码存在
    Tool: read
    Steps:
      1. read ResultTreeView.axaml 断言 ScrollViewer 有 x:Name="TreeScrollViewer"
      2. read RebuildDisplayTree 断言重建前捕获 Offset、重建后 Dispatcher.Post 恢复
    Expected Result: 断言为真
    Failure Indicators: 未命名、未保存/未恢复、恢复在 DisplayNodes 更新前（无效）
    Evidence: .omo/evidence/task-3-scroll.txt

  Scenario: 构建验证
    Tool: Bash
    Steps: dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）
    Failure Indicators: 编译错误（缺 using Avalonia.Threading）
    Evidence: .omo/evidence/task-3-build.txt
  ```

  **Evidence to Capture:** `task-3-scroll.txt`, `task-3-build.txt`

  **Commit**: YES (groups with 1) — `feat(avalonia): ResultTreeView 重组前后保留滚动位置`

---

- [ ] 4. `CompressSettingsViewModel` 两阶段编排 + 按源缓存 + 渐进上屏 + 取消

  **What to do**:

  重写 `src/MantisZip.UI.Avalonia/ViewModels/CompressSettingsViewModel.cs` 的预览构建链路。

  **(4a) 新增字段**（放在 `_previewBuildVersion` 附近 `:353`）：

  ```csharp
  private const int ShallowMaxDepth = 1;
  private const int ShallowMaxWidthPerDir = 20;

  /// <summary>按源缓存：源路径 → 最近一次构建结果（浅层或全量）。</summary>
  private readonly Dictionary<string, SourceSubtree> _sourceCache = new();

  /// <summary>缓存对应的过滤条件签名；变化则整表失效。</summary>
  private string _cacheFilterSignature = string.Empty;

  /// <summary>在途构建的取消源（新构建取消旧构建）。</summary>
  private CancellationTokenSource? _previewCts;

  /// <summary>上次渐进重组的时刻（节流用）。</summary>
  private DateTime _lastProgressiveAssemble = DateTime.MinValue;
  ```

  **(4b) 新增过滤签名 + 缓存失效辅助**：

  ```csharp
  private static string FilterSignature(FileFilterCriteria? f)
  {
      if (f == null || !f.IsActive) return string.Empty;
      return string.Join("|",
          string.Join(",", f.IncludeExtensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
          string.Join(",", f.ExcludeExtensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
          f.NamePattern ?? string.Empty,
          f.MinSize?.ToString() ?? string.Empty,
          f.MaxSize?.ToString() ?? string.Empty,
          f.MinDate?.Ticks.ToString() ?? string.Empty,
          f.MaxDate?.Ticks.ToString() ?? string.Empty);
  }

  /// <summary>清空按源缓存（过滤变化 / 接管 Plan 时调用）。</summary>
  public void InvalidateSourceCache()
  {
      _sourceCache.Clear();
      _cacheFilterSignature = string.Empty;
  }
  ```

  **(4c) 重写 `BuildCompressPreviewCoreAsync`**（替换 `:567-651`）：

  ```csharp
  private async Task<CompressPlan?> BuildCompressPreviewCoreAsync(FileFilterCriteria? filter)
  {
      var version = ++_previewBuildVersion;
      IsBuildPending = true;
      LastBuildError = null;

      // 取消上一次在途构建（已完成源已在 _sourceCache 中保留）
      _previewCts?.Cancel();
      _previewCts = new CancellationTokenSource();
      var ct = _previewCts.Token;

      if (SelectedPaths.Count == 0)
      {
          PreviewRoot = null; IsPreviewBuilding = false; IsBuildPending = false; Plan = null;
          return null;
      }

      if (!IsOutputPathValid())
      {
          PreviewRoot = new PreviewTreeNode
          {
              Name = LocalizationManager.T("Compress_OutputPathInvalid"),
              FullPath = "", DisplayLabel = LocalizationManager.T("Compress_OutputPathInvalid"), IsExpanded = true
          };
          IsPreviewBuilding = false; IsBuildPending = false; Plan = null;
          return null;
      }

      // 快照输入
      var paths = SelectedPaths.ToList();
      var outputMode = OutputMode;
      var outputPath = OutputPath;
      var format = DefaultFormat;
      var keepOriginalExtension = KeepOriginalExtension;

      // 过滤签名变化 → 整表失效；源集变化 → 清理已移除源的缓存
      var sig = FilterSignature(filter);
      if (sig != _cacheFilterSignature) InvalidateSourceCache();
      _cacheFilterSignature = sig;
      foreach (var stale in _sourceCache.Keys.Where(k => !paths.Contains(k)).ToList())
          _sourceCache.Remove(stale);

      try
      {
          // ── Phase A：浅层（所有源 maxDepth=1 / width=20；已完成源直接命中缓存）──
          var shallowTask = Task.Run(() =>
          {
              foreach (var p in paths)
              {
                  ct.ThrowIfCancellationRequested();
                  if (_sourceCache.TryGetValue(p, out var cached)) continue; // 已有（浅或全）
                  var st = ResultPreviewService.BuildSourceSubtree(p, filter, ShallowMaxDepth, ShallowMaxWidthPerDir, ct);
                  if (st != null) _sourceCache[p] = st;
              }
          }, ct);

          var delayTask = Task.Delay(250); // 不传 ct：与原实现一致，避免取消时误判为"慢构建"
          if (await Task.WhenAny(shallowTask, delayTask) == delayTask)
          {
              if (version != _previewBuildVersion) return null;
              PreviewBuildProgress = -1;
              IsPreviewBuilding = true; // 浅层慢时才显示覆层；浅层上屏后立即关闭
          }
          await shallowTask;
          if (version != _previewBuildVersion) return null;

          var assembled = await AssembleAsync(paths, outputMode, outputPath, format, keepOriginalExtension, filter, ct);
          if (version != _previewBuildVersion) return null;
          PreviewRoot = assembled.Root;
          IsPreviewBuilding = false;
          _lastProgressiveAssemble = DateTime.UtcNow;

          // ── Phase B：全量（串行逐源；每源完成即渐进上屏）──
          foreach (var p in paths)
          {
              ct.ThrowIfCancellationRequested();
              if (_sourceCache.TryGetValue(p, out var c) && c.IsFull) continue; // 已全量

              var full = await Task.Run(() => ResultPreviewService.BuildSourceSubtree(p, filter, int.MaxValue, int.MaxValue, ct), ct);
              if (version != _previewBuildVersion) return null;
              if (full != null) _sourceCache[p] = full;

              // 节流：250ms 内的中间重组跳过（最后一次组装必然执行，不丢源）
              if ((DateTime.UtcNow - _lastProgressiveAssemble).TotalMilliseconds >= 250)
              {
                  var progressive = await AssembleAsync(paths, outputMode, outputPath, format, keepOriginalExtension, filter, ct);
                  if (version != _previewBuildVersion) return null;
                  PreviewRoot = progressive.Root;
                  _lastProgressiveAssemble = DateTime.UtcNow;
              }
          }

          // ── Phase C：最终组装（含完整 Plan）──
          var final = await AssembleAsync(paths, outputMode, outputPath, format, keepOriginalExtension, filter, ct);
          if (version != _previewBuildVersion) return null;
          PreviewRoot = final.Root;
          Plan = final.Plan;
          return final.Plan;
      }
      catch (OperationCanceledException)
      {
          return null; // 被新构建取代：静默退出，不做任何赋值
      }
      catch (Exception ex)
      {
          App.DebugLog($"BuildCompressPreview failed: {ex.Message}");
          LastBuildError = ex.Message;
          Plan = null;
          return null;
      }
      finally
      {
          if (version == _previewBuildVersion)
          {
              IsPreviewBuilding = false;
              IsBuildPending = false;
          }
      }
  }

  /// <summary>后台装配预览树（含 stats）+ Plan，仅把结果赋值切回 UI 线程。</summary>
  private async Task<(PreviewTreeNode Root, CompressPlan Plan)> AssembleAsync(
      List<string> paths, CompressOutputMode outputMode, string? outputPath,
      string format, bool keepOriginalExtension, FileFilterCriteria? filter, CancellationToken ct)
  {
      var subtrees = paths.Select(p => _sourceCache.TryGetValue(p, out var st) ? st : (SourceSubtree?)null).ToList();
      return await Task.Run(() => ResultPreviewService.AssembleCompressPreview(
          paths, subtrees, outputMode, outputPath, format, keepOriginalExtension, filter), ct);
  }
  ```

  **(4d) `AdoptPlan`（`:372-378`）增加 cancel + 清缓存**：

  ```csharp
  public void AdoptPlan(CompressPlan? plan)
  {
      _previewBuildVersion++;      // 作废在途重建
      _previewCts?.Cancel();       // 停止在途枚举
      InvalidateSourceCache();     // 接管后源集可能已变
      Plan = plan;
      IsBuildPending = false;
      UpdateCanCompress();
  }
  ```

  **Must NOT do**:
  - 不在 UI 线程跑枚举/装配（必须 `Task.Run`）
  - 不在浅层/渐进阶段赋值 `Plan`（仅 Phase C 赋值）——防止用不完整白名单压缩
  - 不改 `CanExecuteStartCompress`/`EnsurePlanReadyAsync`/`StartCompress` 的门禁语义
  - 不改 `FileFilter` 属性、`OnOutputPathChanged` 等触发点（缓存已让重扫变廉价）
  - 不新增本地化 key（文案在 Task 1 已加）

  **Recommended Agent Profile**:
  - **Category**: `deep` — 异步两阶段 + 缓存失效 + 取消 + 节流 + UI 线程边界，需自主处理竞态
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 3
  - **Blocks**: Task 5
  - **Blocked By**: Task 2（服务 API）、Task 3（滚动保留）

  **References**:
  - `src/MantisZip.UI.Avalonia/ViewModels/CompressSettingsViewModel.cs:296-360`（预览属性/字段）、`:372-378`（AdoptPlan）、`:561-651`（现构建逻辑，被重写）、`:1079-1150`（门禁与 EnsurePlanReadyAsync，不改）、`:415-425`（SelectedPaths.CollectionChanged）
  - `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs`（Task 2 产出的 `SourceSubtree`/`BuildSourceSubtree`/`AssembleCompressPreview`）
  - `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs:227-229`（AdoptPlan 调用点，确认清缓存必要性）

  **Acceptance Criteria**:
  - [ ] `_sourceCache`/`_cacheFilterSignature`/`_previewCts`/`FilterSignature`/`InvalidateSourceCache` 存在
  - [ ] `BuildCompressPreviewCoreAsync` 为两阶段（浅层上屏 → 串行全量渐进上屏 → 最终 Plan）
  - [ ] 新构建 cancel 旧构建；`OperationCanceledException` 静默退出
  - [ ] 过滤签名变化清缓存；移除的源清缓存
  - [ ] `Plan` 仅 Phase C 赋值；`IsBuildPending` 保持到 Phase C
  - [ ] 装配在后台线程，`PreviewRoot` 赋值在 UI 线程
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios**:

  ```
  Scenario: 两阶段与门禁
    Tool: read + Bash (build)
    Preconditions: Task 2 完成
    Steps:
      1. read BuildCompressPreviewCoreAsync 断言：Phase A 浅层（ShallowMaxDepth/Width）→ Phase B 串行全量 → Phase C 赋值 Plan
      2. 断言 Plan 赋值只在 Phase C；IsBuildPending 在 finally（版本匹配时）才清
      3. 断言 OperationCanceledException catch 直接 return（无赋值）
    Expected Result: 断言为真
    Failure Indicators: Plan 在浅层/渐进阶段被赋值、IsBuildPending 提前清、取消后仍赋值
    Evidence: .omo/evidence/task-4-phases.txt

  Scenario: 缓存复用（加源不重扫）
    Tool: read
    Steps:
      1. 断言 Phase A/B 对已缓存源（IsFull 或已存在）直接 continue
      2. 断言过滤签名变化 → InvalidateSourceCache；移除源 → 清对应项
    Expected Result: 断言为真
    Failure Indicators: 每轮无条件重扫全部源、签名未参与失效判断
    Evidence: .omo/evidence/task-4-cache.txt

  Scenario: 构建验证
    Tool: Bash
    Steps: dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）
    Failure Indicators: 编译错误（using、类型不匹配）
    Evidence: .omo/evidence/task-4-build.txt
  ```

  **Evidence to Capture:** `task-4-phases.txt`, `task-4-cache.txt`, `task-4-build.txt`

  **Commit**: YES — `perf(avalonia): 压缩预览改为浅层先行+按源缓存+渐进上屏+可取消`

---

- [ ] 5. 单元测试（浅层/宽度/占位/全量/过滤）+ 文档同步

  **What to do**:

  新增 `tests/MantisZip.UI.Avalonia.Tests/CompressPreviewProgressiveTests.cs`（xunit.v3，UI 相关用 `[AvaloniaFact]`）。测试用 `Path.GetTempPath()` 下临时目录构造真实文件树，`try/finally` 清理。

  必测用例：
  1. **浅层结构**：`root/{a/b/c.txt, a/d.txt, e.txt}`，`BuildSourceSubtree(root, maxDepth:1, maxWidthPerDir:20)` → 含 `a`、`e.txt`；`a` 下含 `d.txt` + 1 个 `IsLoadingPlaceholder`；不含 `b/c.txt`。
  2. **空目录不挂占位**：`root/{emptydir/}` → `emptydir` 无占位子节点。
  3. **宽度上限**：单目录 30 个文件，`maxWidthPerDir:20` → 20 个文件节点 + 1 个占位（共 21 子节点）；目录优先：5 子目录 + 30 文件 → 5 目录 + 15 文件 + 占位。
  4. **占位节点属性**：`IsTruncatedNode == true`、`IconKey == null`、`IsTruncated == false`、`DisplayLabel == LocalizationManager.T("Preview_Result_LoadingMore")`、`ShallowClone` 复制 `IsLoadingPlaceholder`。
  5. **父目录不判空**：含占位子节点的目录 `IsEmptyDirectory == false`；`DirectoryInfoText == ""`（`HasLoadingPlaceholderChild` 生效）。
  6. **全量不变**：`BuildSourceSubtree(root)`（默认 maxDepth/maxWidth = MaxValue）结构 = 改造前语义（含 `b/c.txt`，无占位）。
  7. **取消穿透**：已 cancel 的 token → `BuildSourceSubtree` 抛 `OperationCanceledException`。
  8. **过滤一致性**：`filter = ExcludeExtensions=[".log"]`，浅层与全量对 `.log` 文件均 `IsFilteredOut==true`。
  9. **装配等价**：`BuildCompressPreview(paths, outputMode: Manual)` 与「`BuildSourceSubtree` 全量 + `AssembleCompressPreview`」结果结构一致（Plan 的 `IncludedFiles` 在过滤激活时一致）。

  运行 `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` 全绿（含既有 `ResultTreeViewFilterTests` 无回归）。

  同步 `docs/PLAN.md`：在「待实现设计方案」表格 **P2** 区新增一行（格式对齐现有行）：

  ```
  | **P2** | 压缩预览渐进式加载（浅层先行 + 按源缓存） | [compress-preview-progressive-loading.md](.omo/plans/未开始/compress-preview-progressive-loading.md) | 🟡中 | 6-8h | 压缩设置窗口预览树浅层先行（2 层 + 宽度上限 20 + 「… 加载中」占位）→ 按源缓存 → 逐源渐进上屏；顺带修复加目录丢弃已完成枚举、输出文件名逐键全量重扫 |
  ```

  **Must NOT do**:
  - 不写依赖真实窗口渲染的测试（用 `[AvaloniaFact]` 提供 Avalonia 环境即可）
  - 不 mock 文件系统（用真实临时目录）
  - 不新增本地化 key

  **Recommended Agent Profile**:
  - **Category**: `deep` — 需理解测试项目结构、临时目录生命周期、服务 API 语义
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 4
  - **Blocks**: FINAL
  - **Blocked By**: Task 1, 2, 3, 4

  **References**:
  - `tests/MantisZip.UI.Avalonia.Tests/ResultTreeViewFilterTests.cs`（测试模式：`[AvaloniaFact]`、`EnsureIconResources`、节点遍历辅助）
  - `tests/MantisZip.UI.Avalonia.Tests/MantisZip.UI.Avalonia.Tests.csproj`（xunit.v3 + Avalonia.Headless.XUnit 12.0.4）
  - `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs`（被测 API）
  - `src/MantisZip.UI.Avalonia/Models/PreviewTreeNode.cs`（被测属性）
  - `docs/PLAN.md`（表格格式锚点）

  **Acceptance Criteria**:
  - [ ] 新增测试文件，覆盖上述 9 组用例
  - [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿（含既有测试无回归）
  - [ ] `docs/PLAN.md` 已加 P2 行
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios**:

  ```
  Scenario: 单测全绿（含新增 + 既有无回归）
    Tool: Bash
    Preconditions: Task 1-4 产物已编译
    Steps:
      1. dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
      2. 观察新增用例通过率与既有用例
    Expected Result: 新增 9 组全 PASS，既有 ResultTreeViewFilterTests 无回归
    Failure Indicators: 任一断言失败（浅层结构错、占位缺失、宽度上限失效、取消被吞）
    Evidence: .omo/evidence/task-5-test-run.txt

  Scenario: 覆盖完整性
    Tool: read + grep
    Steps:
      1. 枚举测试方法，断言覆盖 9 组用例
    Expected Result: 覆盖清单与任务一致
    Failure Indicators: 缺任一组（如缺取消或过滤）
    Evidence: .omo/evidence/task-5-coverage.txt
  ```

  **Evidence to Capture:** `task-5-test-run.txt`, `task-5-coverage.txt`

  **Commit**: YES — `test(avalonia): 压缩预览浅层/宽度/占位/取消/过滤单测 + PLAN.md 同步`

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.
> **Do NOT auto-proceed after verification. Wait for user's explicit approval before marking work complete.**

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists (read file, run command). For each "Must NOT Have": search codebase for forbidden patterns — reject with file:line if found. Check evidence files exist in `.omo/evidence/`. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` + `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`. Review changed files for: 类型压制、空 catch（除既有"跳过不可访问目录"且不吞取消）、注释掉的代码、未用 using。检查 AI slop：过度注释、过度抽象、泛化命名。
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | Files [N clean/N issues] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Execute EVERY QA scenario from EVERY task. 重点验证跨任务集成：浅层上屏 → 逐源渐进 → 最终 Plan；加源/删源/改过滤/改输出路径的重建行为；取消穿透。保存到 `.omo/evidence/final-qa/`。
  Output: `Scenarios [N/N pass] | Integration [N/N] | Edge Cases [N tested] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff. Verify 1:1 — no missing, no creep. Check "Must NOT do" compliance. Detect cross-task contamination（如 Task 4 误改解压预览）。Flag unaccounted changes.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

- **1**: `feat(avalonia): PreviewTreeNode 新增深层加载占位节点支持` — PreviewTreeNode.cs, strings.*.json, dotnet build
- **2**: `refactor(avalonia): ResultPreviewService 拆分单源构建与装配并支持深度/宽度/取消` — ResultPreviewService.cs, dotnet build
- **3**: `feat(avalonia): ResultTreeView 重组前后保留滚动位置` — ResultTreeView.axaml(.cs), dotnet build
- **4**: `perf(avalonia): 压缩预览改为浅层先行+按源缓存+渐进上屏+可取消` — CompressSettingsViewModel.cs, dotnet build
- **5**: `test(avalonia): 压缩预览浅层/宽度/占位/取消/过滤单测 + PLAN.md 同步` — tests, docs/PLAN.md, dotnet test

> 提交前按 AGENTS.md 规则 3 更新 `docs/PROGRESS.md`（里程碑）与 `docs/progress-avalonia-detail.md`（细节）。

---

## Success Criteria

### Verification Commands

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj   # Expected: Build succeeded
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj  # Expected: all pass
```

### Final Checklist

- [ ] All "Must Have" present
- [ ] All "Must NOT Have" absent
- [ ] All tests pass（含既有 `ResultTreeViewFilterTests` 无回归）
- [ ] `docs/PLAN.md` 已同步（规则 1）
- [ ] `docs/PROGRESS.md` + `docs/progress-avalonia-detail.md` 已更新（规则 3，提交前）
