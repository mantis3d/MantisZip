# 解压预览冲突检测优化（短路 + 过滤跳过 + 两阶段）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

## TL;DR

> **Quick Summary**: 解压设置窗口预览树的瓶颈是**文件冲突检测**（每个条目一次 `File.Exists`/`Directory.Exists`，串行、无短路、无缓存）。本计划做三件确定性优化 + 一件 UX 优化：① **目标根不存在 → 整批跳过**（解压到新目录的常见场景 I/O 归零）；② **被过滤的条目不做冲突检查**（视图会移除、计数也跳过）；③ **自顶向下检测 + 父目录不存在则子树短路**（把文件先查、目录后查的顺序倒过来）；④ **两阶段：先检前两层 → 上屏 → 后台补深层 → 刷新**（前两层通常足够用户判断是否覆盖）。
>
> **Deliverables**:
> - `Services/ResultPreviewService.cs`：新增 `ApplyConflictMarkers(root, destDir, maxDepth, ct)`（含 ①②③），`BuildExtractPreview` 的冲突检测改为调用它；删除 `MarkDirectoryConflicts`
> - `ViewModels/ExtractSettingsViewModel.cs`：单包路径改为两阶段（建结构 → 前两层冲突上屏 → 后台深层冲突）；新增 `_conflictCts` + `PreviewTreeInvalidated` 事件 + 取消入口
> - `Dialogs/ExtractSettingsWindow.axaml.cs`：订阅 `PreviewTreeInvalidated` → `PreviewTree.RefreshDisplay()`；窗口关闭时取消后台冲突检测
> - 单元测试（短路 / 过滤跳过 / 深度限制 / 全量等价）
> - `docs/PLAN.md` 同步（规则 1）
>
> **Estimated Effort**: Medium
> **Parallel Execution**: NO（强顺序依赖）— 4 个实施任务 + final review wave
> **Critical Path**: Task 1 (服务) → Task 2 (VM) → Task 3 (窗口接线) → Task 4 (测试) → F1-F4 → user okay

---

## Context

### Original Request

用户观察到压缩设置窗口预览树的等待问题后，要求检查解压端。经排查，解压端**多压缩包路径已经是渐进式的**（`AssembleSkeleton` 骨架 + 逐包 `_subTreeCache` 原位转正），但**单压缩包路径**（`BuildAndAssignSingleAsync`）整包一个单元构建、期间显示覆层。用户进一步观察确认：**瓶颈在文件冲突检测**。用户同意优化项 ①②③，并要求追加 ④「先检测两层文件冲突，后台再加载深层」。

### Interview Summary

**Key Discussions**:

- **瓶颈定位**：用户实测确认瓶颈是冲突检测（`checkExists: true` 的逐条 `File.Exists`/`Directory.Exists`），不是树构建本身。
- **① 目标根不存在 → 整批跳过**：`File.Exists`/`Directory.Exists` 对父目录不存在的路径必然返回 false（不抛异常）→ 一次 `Directory.Exists(destDir)` 即可判定全部无冲突。
- **② 过滤掉的条目不检查**：被过滤文件在视图被 `RemoveFilteredNodes` 移除、`CountConflicts` 也跳过 `IsFilteredOut`，故无需为其做 I/O。副作用：`ShowFilteredGhosts` 灰显模式下被过滤文件不再显示 ⚠️（用户已接受）。
- **③ 目录先行 + 父目录短路**：现状是文件先查（Phase 2）、目录后查（Phase 2b `MarkDirectoryConflicts`）；改为自顶向下一趟，父目录不存在则整棵子树判 false。
- **④ 两阶段（深度 ≤ 2）**：先建完整树结构（快）→ 检测前两层冲突 → 上屏 → 后台检测深层 → 完成后刷新。**「两层」定义为相对 root 的深度 ≤ 2**（root=0，直接子项=1，孙辈=2）。用户认为两层冲突通常已足够判断。
- **④ 范围**：本次**仅做单包路径**（`ExtractSettingsWindow` 的 `BuildAndAssignSingleAsync`）。①②③ 做在共享服务里，多包（`RebuildMergedPreview` 逐包）与 `CustomFilePickerDialog` 自动受益；它们的 ④ 刷新接线留作后续。
- **刷新机制**：视图 `RebuildDisplayTree` 会深拷贝整树，后台更新模型后必须显式刷新 → VM 抛 `PreviewTreeInvalidated` 事件 → 窗口 code-behind 调 `PreviewTree.RefreshDisplay()`。

**Research Findings**（已核实，勿重复调研）:

- 冲突检测实现：`ResultPreviewService.BuildExtractPreview` 文件内联 `File.Exists`（`:126-130`，在过滤判断**之前**）；`MarkDirectoryConflicts`（`:630-641`）递归每目录 `Directory.Exists`。
- `checkExists` 调用方：`ExtractSettingsViewModel.BuildAndAssignSingleAsync`（`:707`）与 `RebuildMergedPreview`（`:585`）固定 `true`；`CustomFilePickerDialog.SchedulePreviewRebuild`（`:711-716`）固定 `true`；`DragPreviewBitmapBuilder`（`:41`）为 `false`（不受影响）。
- 单包路径：`BuildAndAssignSingleAsync`（`ExtractSettingsViewModel.cs:697-725`），无版本号守卫，但外层 `RunRebuildLoopAsync`（`:511-526`）用 `_isRebuilding`/`_rebuildPending` 串行化重建。
- 多包路径已渐进：`AssembleSkeleton`（`:617-640`）+ `_subTreeCache`（`:96-97`）+ 每包校验完成 `RebuildMergedPreview`（`:359-363`）。
- 视图：`ExtractSettingsWindow.axaml` 的 `ResultTreeView x:Name="PreviewTree"`（`:149-162`）；`ResultTreeView.RefreshDisplay()`（`ResultTreeView.axaml.cs:305`）公开可用。
- 窗口订阅点：`OnLoaded` 订阅 `ViewModel.PropertyChanged`（`:154`）；`Closed` 取消 `_validationCts`（`:75`）。
- `PreviewTreeNode.ExistsAtDestination`（`:30`）为普通属性；视图深拷贝经 `ShallowClone`（`:163`）复制。
- 现有测试 `ExtractSettingsViewModelTests` 只测构造/命令，不受影响。
- **已知但本次不做**：`FindOrCreateParent`/`AddFolderNode`（`ResultPreviewService.cs:561-625`）与 Core `ArchiveTreeBuilder.AddFolderNode` 的兄弟线性扫描在超宽目录下为 O(n²)。用户确认瓶颈是冲突检测，此项列为后续候选。

### Metis Review（预审要点，已纳入）

- **`OperationCanceledException` 必须穿透**：`BuildExtractPreview` 的 `BuildDirectoryNode` 已有 `catch { }`；新增的 `MarkConflicts` 递归无 catch，但调用方（VM 后台任务）必须 `catch (OperationCanceledException)` 静默退出。
- **刷新必须显式**：视图深拷贝，后台改模型不会自动反映 → 事件 + `RefreshDisplay()`。
- **`PreviewRoot` 引用守卫**：后台深层检测完成时，若 `PreviewRoot` 已被新一轮重建替换，丢弃结果（`ReferenceEquals` 守卫）。
- **过滤跳过与 `ShowFilteredGhosts`**：接受"灰显项无 ⚠️"。
- **门禁**：前两层冲突检测完成后即可 `IsBuildPending=false`（冲突是展示信息，不影响提取语义 `FilteredEntryKeys`）。

---

## Work Objectives

### Core Objective

把解压预览的冲突检测从"逐条串行全量 I/O"改造为"短路 + 过滤跳过 + 自顶向下 + 两阶段（前两层先上屏）"，消除解压设置窗口单包预览的等待。

### Concrete Deliverables

- `Services/ResultPreviewService.cs`：`ApplyConflictMarkers(root, destDir, maxDepth, ct)`（含 ①②③）；`BuildExtractPreview` 冲突检测改调它；删除 `MarkDirectoryConflicts`
- `ViewModels/ExtractSettingsViewModel.cs`：`_conflictCts`、`PreviewTreeInvalidated` 事件、`CancelPendingConflictCheck()`、`ShallowConflictDepth = 2`；重写 `BuildAndAssignSingleAsync` 为两阶段
- `Dialogs/ExtractSettingsWindow.axaml.cs`：订阅 `PreviewTreeInvalidated`；`Closed` 取消后台冲突检测
- `tests/MantisZip.UI.Avalonia.Tests/ExtractConflictMarkerTests.cs`：新增单测
- `docs/PLAN.md`：新增 P2 行（规则 1）

### Definition of Done

- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded（0 error）
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿（含新增单测 + 既有 `ExtractSettingsViewModelTests`/`ResultTreeViewFilterTests` 无回归）
- [ ] `lsp_diagnostics` 对全部改动文件无错误
- [ ] Agent 运行 App 验证：解压到**不存在的新目录** → 预览树几乎瞬间出现（无冲突）；解压到**已有大量文件的目录** → 前两层冲突先出现、深层 ⚠️ 随后补上
- [ ] `docs/PLAN.md` 已同步（规则 1）

### Must Have

- ① `ApplyConflictMarkers` 在 `destDir` 不存在时直接返回（全部 `ExistsAtDestination=false`），不做逐条 I/O
- ② `IsFilteredOut` 的节点跳过检测
- ③ 自顶向下一趟；父目录 `ExistsAtDestination=false` 时其子树全部跳过 I/O
- ④ 单包路径两阶段：`maxDepth=2` 先上屏，`int.MaxValue` 后台补齐后刷新
- `BuildExtractPreview(checkExists:true)` 语义与改造前等价（除"被过滤项不再标记冲突"这一有意差异）
- 后台深层检测可取消（新重建/关窗）；完成时 `PreviewRoot` 引用守卫
- 新增用户可见文案：无（不新增 key）

### Must NOT Have (Guardrails)

- **不得**改解压提取语义（`FilteredEntryKeys`/`ComputeFilteredEntryKeys` 与冲突检测无关）
- **不得**改多包骨架/`_subTreeCache` 机制（本次只在其内部复用优化后的 `BuildExtractPreview`）
- **不得**改 `CustomFilePickerDialog`/`DragPreviewBitmapBuilder`（前者自动受益 ①②③，后者 `checkExists:false` 不受影响）
- **不得**改 WPF `MantisZip.UI`（规则 11）
- **不得**在本计划内做 O(n²) 建树索引化（列为后续候选，避免范围蔓延）
- **不得**在 UI 线程做冲突检测（必须 `Task.Run`）
- **不得**新增未请求的动画/打磨（AI slop）
- 版本号不变（规则 2）

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** - ALL verification is agent-executed.

### Test Decision

- **Infrastructure exists**: YES（`tests/MantisZip.UI.Avalonia.Tests/`，xunit.v3 + Avalonia.Headless.XUnit）
- **Automated tests**: Unit tests（真实临时目录 + 手工构造 `PreviewTreeNode` 树）
- **Framework**: xUnit v3，涉及 `LocalizationManager`/Avalonia 的用 `[AvaloniaFact]`
- **Agent QA**: 构建 + 单测 + 可选 App 截图（无头环境受限时以单测 + 构建为准并注明）

### QA Policy

Every task MUST include agent-executed QA scenarios. Evidence saved to `.omo/evidence/task-{N}-{scenario-slug}.{ext}`.

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1:
└── Task 1: ResultPreviewService 冲突检测优化 + 拆分 ApplyConflictMarkers [deep]

Wave 2 (After Task 1):
└── Task 2: ExtractSettingsViewModel 单包两阶段 + 取消 + 刷新事件 [deep]

Wave 3 (After Task 2):
└── Task 3: ExtractSettingsWindow 订阅刷新事件 + 关窗取消 [quick]

Wave 4 (After Task 3):
└── Task 4: 单元测试 + PLAN.md 同步 [deep]

Wave FINAL (After ALL tasks — 4 parallel reviews, then user okay):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Real manual QA (unspecified-high)
└── Task F4: Scope fidelity check (deep)
-> Present results -> Get explicit user okay

Critical Path: Task 1 → Task 2 → Task 3 → Task 4 → F1-F4 → user okay
Max Concurrent: 1（强顺序依赖）
```

### Dependency Matrix

- **1**: - → blocks 2
- **2**: 1 → blocks 3
- **3**: 2 → blocks 4
- **4**: 1,2,3 → blocks FINAL
- **F1-F4**: 1-4 → blocks user okay

### Agent Dispatch Summary

- **Wave 1**: T1 → `deep`
- **Wave 2**: T2 → `deep`
- **Wave 3**: T3 → `quick`
- **Wave 4**: T4 → `deep`
- **FINAL**: F1 → `oracle`, F2 → `unspecified-high`, F3 → `unspecified-high`, F4 → `deep`

---

## TODOs

- [ ] 1. `ResultPreviewService` 冲突检测优化 + 拆分 `ApplyConflictMarkers`

  **What to do**:

  在 `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs` 新增公共方法（含 ①②③）：

  ```csharp
  /// <summary>
  /// 目标位置冲突检测：设置树中各节点的 <see cref="PreviewTreeNode.ExistsAtDestination"/>。
  /// 优化：① destDir 不存在则整批跳过；② 被过滤项不检查；③ 自顶向下、父目录不存在则子树短路。
  /// </summary>
  /// <param name="maxDepth">相对 root 的最大检查深度。1=仅直接子项；2=直接子项+孙辈；int.MaxValue=全量。</param>
  public static void ApplyConflictMarkers(
      PreviewTreeNode root, string destDir, int maxDepth = int.MaxValue, CancellationToken ct = default)
  {
      // ① 目标根不存在 → 所有 ExistsAtDestination 保持默认 false，零 I/O
      if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
          return;

      MarkConflicts(root, destDir, parentExists: true, depth: 0, maxDepth, ct);
  }

  private static void MarkConflicts(
      PreviewTreeNode node, string destDir, bool parentExists, int depth, int maxDepth, CancellationToken ct)
  {
      if (depth >= maxDepth) return;

      foreach (var child in node.Children.OfType<PreviewTreeNode>())
      {
          ct.ThrowIfCancellationRequested();

          // ② 被过滤项不检查（视图会移除、CountConflicts 也跳过）
          if (child.IsFilteredOut)
          {
              child.ExistsAtDestination = false;
              continue;
          }

          var realPath = Path.Combine(destDir, child.FullPath.Replace('/', Path.DirectorySeparatorChar));

          if (child.IsDirectory)
          {
              // ③ 父不存在 → 子树短路
              child.ExistsAtDestination = parentExists && Directory.Exists(realPath);
              MarkConflicts(child, destDir, child.ExistsAtDestination, depth + 1, maxDepth, ct);
          }
          else
          {
              child.ExistsAtDestination = parentExists && File.Exists(realPath);
          }
      }
  }
  ```

  **改造 `BuildExtractPreview`**（`:27-157`）：
  - **删除**文件循环内的内联冲突块（`:126-130` 的 `if (checkExists) { ... File.Exists ... }`）
  - **删除** Phase 2b 的 `MarkDirectoryConflicts` 调用（`:143-146`）与私有方法 `MarkDirectoryConflicts`（`:630-641`）
  - 在 `CalculateDescendantStats(destNode)` **之前**（或之后，顺序无关）加入：
    ```csharp
    if (checkExists)
        ApplyConflictMarkers(destNode, destDir);
    ```

  > 语义变化（有意）：被过滤文件不再被标记 `ExistsAtDestination`。视图会移除它们、`CountConflicts` 也跳过，故显示等价（`ShowFilteredGhosts` 灰显项不再有 ⚠️，已与用户确认）。

  **Must NOT do**:
  - 不改 `BuildExtractPreview` 的树构建/路径解析/过滤标记逻辑
  - 不改 `progress` 上报
  - 不改 `BuildCompressPreview`/`AssembleCompressPreview`（压缩端独立）
  - 不做 O(n²) 建树索引化（后续候选）
  - 不新增本地化 key

  **Recommended Agent Profile**:
  - **Category**: `deep` — 需理解冲突检测语义、深度边界、短路正确性
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 1
  - **Blocks**: Task 2
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs:27-157`（`BuildExtractPreview`）、`:126-130`（内联冲突块，删除）、`:142-146`（Phase 2b 调用，删除）、`:630-641`（`MarkDirectoryConflicts`，删除）
  - `src/MantisZip.UI.Avalonia/Models/PreviewTreeNode.cs:30`（`ExistsAtDestination`）、`:33`（`IsFilteredOut`）、`:48`（`IsDirectory`）

  **Acceptance Criteria**:
  - [ ] `ApplyConflictMarkers` 存在且实现 ①②③
  - [ ] `BuildExtractPreview` 的 `checkExists` 改为调用 `ApplyConflictMarkers(destNode, destDir)`
  - [ ] `MarkDirectoryConflicts` 已删除，无残留引用
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios**:

  ```
  Scenario: ① 目标根不存在 → 全 false 且无逐条 I/O
    Tool: Bash (dotnet test，Task 4 单测前置；本任务先保证方法存在)
    Preconditions: ApplyConflictMarkers 已实现
    Steps:
      1. 构造树含 dir/file；destDir 指向一个不存在的路径
      2. 调 ApplyConflictMarkers(root, destDir, int.MaxValue)
      3. 断言所有节点 ExistsAtDestination == false
    Expected Result: 全 false
    Failure Indicators: 任一节点为 true、抛异常
    Evidence: .omo/evidence/task-1-rootmissing.txt

  Scenario: ③ 父目录短路
    Tool: Bash (dotnet test)
    Steps:
      1. 构造 destDir/{a/file.txt} 使 a 不存在但 destDir 存在
      2. 调 ApplyConflictMarkers；断言 a.ExistsAtDestination==false 且 a/file.txt.ExistsAtDestination==false
    Expected Result: 子树全 false
    Failure Indicators: 子文件被单独判定为 true
    Evidence: .omo/evidence/task-1-shortcircuit.txt

  Scenario: 深度限制
    Tool: Bash (dotnet test)
    Steps:
      1. 树 root/d1/d2/d3/file（file 在 depth 4）
      2. maxDepth=2 → 断言 depth≤2 节点被检查（值正确）、depth>2 保持 false
    Expected Result: 仅前两层被赋值
    Failure Indicators: 深层被检查
    Evidence: .omo/evidence/task-1-depth.txt

  Scenario: 构建验证
    Tool: Bash
    Steps: dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）
    Failure Indicators: 编译错误（残留 MarkDirectoryConflicts 引用）
    Evidence: .omo/evidence/task-1-build.txt
  ```

  **Evidence to Capture:** `task-1-rootmissing.txt`, `task-1-shortcircuit.txt`, `task-1-depth.txt`, `task-1-build.txt`

  **Commit**: YES — `perf(avalonia): 解压预览冲突检测短路+过滤跳过+深度限制（ApplyConflictMarkers）`

---

- [ ] 2. `ExtractSettingsViewModel` 单包两阶段冲突检测 + 取消 + 刷新事件

  **What to do**:

  在 `src/MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs`：

  **(2a) 新增字段/常量/事件**（放在 `_subTreeCache` 附近 `:96`）：

  ```csharp
  /// <summary>前两层冲突检测深度（相对 root：直接子项 + 孙辈）。</summary>
  private const int ShallowConflictDepth = 2;

  /// <summary>深层冲突检测的取消源（新重建/关窗时取消）。</summary>
  private CancellationTokenSource? _conflictCts;

  /// <summary>后台深层冲突检测完成后触发，宿主（窗口）应调用 ResultTreeView.RefreshDisplay()。</summary>
  public event EventHandler? PreviewTreeInvalidated;

  /// <summary>取消在途的深层冲突检测（窗口关闭时调用）。</summary>
  public void CancelPendingConflictCheck() => _conflictCts?.Cancel();
  ```

  **(2b) 重写 `BuildAndAssignSingleAsync`**（替换 `:697-725`）：

  ```csharp
  private async Task BuildAndAssignSingleAsync(
      IReadOnlyList<ArchiveItem> entries, string destDir, FileFilterCriteria? filter)
  {
      IsListingPending = false;
      IsBuildPending = true;
      PreviewBuildProgress = -1;
      var progress = new Progress<double>(v => PreviewBuildProgress = v);

      // 取消上一轮深层冲突检测
      _conflictCts?.Cancel();
      _conflictCts = new CancellationTokenSource();
      var ct = _conflictCts.Token;

      PreviewTreeNode root;
      try
      {
          // 阶段 1：建结构（不检测冲突，快）
          var buildTask = Task.Run(() => ResultPreviewService.BuildExtractPreview(
              entries, destDir, checkExists: false, filter: filter, progress: progress));

          var delayTask = Task.Delay(250); // 不传 ct（与原实现一致，避免取消时误判）
          if (await Task.WhenAny(buildTask, delayTask) == delayTask)
          {
              if (ct.IsCancellationRequested) return;
              IsPreviewBuilding = true;
          }

          root = await buildTask;
          ct.ThrowIfCancellationRequested();

          // 阶段 2：前两层冲突（快）→ 上屏
          await Task.Run(() => ResultPreviewService.ApplyConflictMarkers(
              root, destDir, ShallowConflictDepth, ct), ct);
          ct.ThrowIfCancellationRequested();

          PreviewRoot = root;
      }
      catch (OperationCanceledException)
      {
          return; // 被新重建取代：静默退出
      }
      catch (Exception ex)
      {
          App.DebugLog($"single preview build failed: {ex.Message}");
          return;
      }
      finally
      {
          IsPreviewBuilding = false;
          IsBuildPending = false; // 前两层就绪即可解压（冲突仅展示，不影响 FilteredEntryKeys）
      }

      // 阶段 3：深层冲突（后台，不阻塞重建循环）
      var capturedRoot = root;
      _ = Task.Run(() =>
      {
          try
          {
              ResultPreviewService.ApplyConflictMarkers(capturedRoot, destDir, int.MaxValue, ct);
              if (ct.IsCancellationRequested) return;
              if (!ReferenceEquals(PreviewRoot, capturedRoot)) return; // 已被新一轮替换 → 丢弃
              global::Avalonia.Threading.Dispatcher.UIThread.Post(
                  () => PreviewTreeInvalidated?.Invoke(this, EventArgs.Empty));
          }
          catch (OperationCanceledException) { }
          catch (Exception ex)
          {
              App.DebugLog($"deep conflict check failed: {ex.Message}");
          }
      }, ct);
  }
  ```

  > 注意：`BuildExtractPreview(checkExists:false)` 不再传 `preserveFullPath`/`currentFolder`（与原单包调用一致，用默认值）。

  **Must NOT do**:
  - 不改 `RebuildOnceAsync` 的单包/多包分支结构（多包仍走 `AssembleSkeleton`）
  - 不改 `ExtractCommand`/`CanExecuteExtract` 的门禁语义
  - 不在 UI 线程做冲突检测
  - 不改 `_subTreeCache`/`InvalidatePreviewCache`
  - 不新增本地化 key

  **Recommended Agent Profile**:
  - **Category**: `deep` — 异步两阶段 + 取消 + 引用守卫 + UI 线程边界
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2
  - **Blocks**: Task 3
  - **Blocked By**: Task 1

  **References**:
  - `src/MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs:697-725`（`BuildAndAssignSingleAsync` 现实现）、`:96-103`（缓存字段）、`:511-526`（重建串行化）、`:544-558`（单包分支调用点）
  - `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs`（Task 1 的 `ApplyConflictMarkers`）
  - `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml.cs:305`（`RefreshDisplay`）

  **Acceptance Criteria**:
  - [ ] `_conflictCts`/`PreviewTreeInvalidated`/`CancelPendingConflictCheck`/`ShallowConflictDepth` 存在
  - [ ] `BuildAndAssignSingleAsync` 为两阶段（结构 → 前两层 → 上屏 → 后台深层 → 事件）
  - [ ] 新重建取消旧深层检测；`OperationCanceledException` 静默退出
  - [ ] 深层完成时 `ReferenceEquals(PreviewRoot, capturedRoot)` 守卫
  - [ ] `IsBuildPending` 在前两层完成后即清
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios**:

  ```
  Scenario: 两阶段结构
    Tool: read
    Preconditions: Task 1 完成
    Steps:
      1. read BuildAndAssignSingleAsync 断言：BuildExtractPreview(checkExists:false) → ApplyConflictMarkers(ShallowConflictDepth) → PreviewRoot=root → 后台 ApplyConflictMarkers(int.MaxValue) → 事件
      2. 断言 finally 清 IsBuildPending/IsPreviewBuilding；深层任务有 ReferenceEquals 守卫
    Expected Result: 断言为真
    Failure Indicators: 深层仍在 UI 线程、无引用守卫、取消后仍赋值
    Evidence: .omo/evidence/task-2-phases.txt

  Scenario: 构建验证
    Tool: Bash
    Steps: dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）
    Failure Indicators: 编译错误
    Evidence: .omo/evidence/task-2-build.txt
  ```

  **Evidence to Capture:** `task-2-phases.txt`, `task-2-build.txt`

  **Commit**: YES — `perf(avalonia): 解压单包预览改为两阶段冲突检测（前两层先上屏）`

---

- [ ] 3. `ExtractSettingsWindow` 订阅刷新事件 + 关窗取消

  **What to do**:

  在 `src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml.cs`：

  **(3a)** 在 `OnLoaded`（`:143-164`）订阅刷新事件（与 `ViewModel.PropertyChanged += ...` 同处）：

  ```csharp
  // 后台深层冲突检测完成 → 刷新显示树（视图深拷贝，必须显式刷新才能看到新增 ⚠️）
  ViewModel.PreviewTreeInvalidated += OnPreviewTreeInvalidated;
  ```

  **(3b)** 新增处理方法：

  ```csharp
  private void OnPreviewTreeInvalidated(object? sender, EventArgs e)
      => PreviewTree.RefreshDisplay();
  ```

  **(3c)** 在构造函数已注册的 `Closed`（`:75`）中追加取消 + 退订：

  ```csharp
  Closed += (_, _) =>
  {
      _validationCts?.Cancel();
      ViewModel.CancelPendingConflictCheck();
      ViewModel.PreviewTreeInvalidated -= OnPreviewTreeInvalidated;
  };
  ```

  **Must NOT do**:
  - 不改 `OnViewModelPropertyChanged`（`DestinationPath` 重建逻辑）
  - 不改 `InitFileFilter`/`OnFileFilterChanged`
  - 不改 XAML

  **Recommended Agent Profile**:
  - **Category**: `quick` — 单文件三处小改
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 3
  - **Blocks**: Task 4
  - **Blocked By**: Task 2（事件定义）

  **References**:
  - `src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml.cs:74-75`（`Closed`）、`:143-164`（`OnLoaded`）、`:149`（`PreviewTree` 控件引用）
  - `src/MantisZip.UI.Avalonia/Controls/ResultTreeView.axaml.cs:305`（`RefreshDisplay`）
  - `src/MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs`（Task 2 的 `PreviewTreeInvalidated`/`CancelPendingConflictCheck`）

  **Acceptance Criteria**:
  - [ ] `OnLoaded` 订阅 `PreviewTreeInvalidated`
  - [ ] 处理器调用 `PreviewTree.RefreshDisplay()`
  - [ ] `Closed` 调 `CancelPendingConflictCheck()` 并退订
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios**:

  ```
  Scenario: 事件接线
    Tool: read
    Steps:
      1. read ExtractSettingsWindow.axaml.cs 断言订阅/处理器/关窗退订三处存在
      2. 断言处理器调用 PreviewTree.RefreshDisplay()
    Expected Result: 断言为真
    Failure Indicators: 未退订（泄漏）、未取消（关窗后仍在扫盘）
    Evidence: .omo/evidence/task-3-wire.txt

  Scenario: 构建验证
    Tool: Bash
    Steps: dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）
    Failure Indicators: 编译错误
    Evidence: .omo/evidence/task-3-build.txt
  ```

  **Evidence to Capture:** `task-3-wire.txt`, `task-3-build.txt`

  **Commit**: YES — `feat(avalonia): 解压设置窗口订阅深层冲突检测刷新事件`

---

- [ ] 4. 单元测试 + `docs/PLAN.md` 同步

  **What to do**:

  新增 `tests/MantisZip.UI.Avalonia.Tests/ExtractConflictMarkerTests.cs`（xunit.v3，涉及 `LocalizationManager`/Avalonia 用 `[AvaloniaFact]`）。用 `Path.GetTempPath()` 下临时目录，`try/finally` 清理。

  必测用例：
  1. **① 目标根不存在**：`destDir = 临时路径/不存在`；树含多节点 → 全部 `ExistsAtDestination == false`。
  2. **② 过滤跳过**：构造 `IsFilteredOut=true` 的文件节点，其真实文件存在于目标 → 仍为 `false`。
  3. **③ 父目录短路**：`destDir` 存在、`destDir/a` 不存在；文件 `a/f.txt` 在磁盘上不存在 → `a=false` 且 `a/f.txt=false`；且当 `destDir/a` 存在、`a/f.txt` 存在 → 两者均为 `true`。
  4. **③ 文件存在判定**：`destDir/f.txt` 真实存在 → `f.txt.ExistsAtDestination == true`。
  5. **深度限制**：`root/d1/d2/d3`（d3 下文件）→ `maxDepth=2` 时 depth≤2 正确赋值、depth>2 保持 `false`；`maxDepth=int.MaxValue` 时深层正确赋值。
  6. **全量等价**：同一树，`BuildExtractPreview(entries, destDir, checkExists:true)` 与「`checkExists:false` + `ApplyConflictMarkers(...,int.MaxValue)`」结果一致（节点 `ExistsAtDestination` 逐一相等）。
  7. **取消穿透**：已 cancel 的 token → `ApplyConflictMarkers` 抛 `OperationCanceledException`（在需要检查节点的树上）。
  8. **空树/空 destDir 不抛异常**。

  运行 `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` 全绿（含既有测试无回归）。

  同步 `docs/PLAN.md`：在「待实现设计方案」表格 **P2** 区新增一行（格式对齐现有行）：

  ```
  | **P2** | 解压预览冲突检测优化（短路 + 两阶段） | [extract-preview-conflict-detection-optimization.md](.omo/plans/未开始/extract-preview-conflict-detection-optimization.md) | 🟡中 | 3-4h | 冲突检测短路（目标根不存在整批跳过 / 过滤跳过 / 父目录不存在子树短路）+ 单包两阶段（前两层先上屏，后台补深层）；O(n²) 建树索引化列为后续候选 |
  ```

  **Must NOT do**:
  - 不写依赖真实窗口渲染的测试
  - 不 mock 文件系统（用真实临时目录）
  - 不新增本地化 key

  **Recommended Agent Profile**:
  - **Category**: `deep` — 临时目录生命周期 + 树构造 + 深度/短路断言
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 4
  - **Blocks**: FINAL
  - **Blocked By**: Task 1, 2, 3

  **References**:
  - `tests/MantisZip.UI.Avalonia.Tests/ResultTreeViewFilterTests.cs`（测试模式：`[AvaloniaFact]`、节点遍历辅助）
  - `tests/MantisZip.UI.Avalonia.Tests/MantisZip.UI.Avalonia.Tests.csproj`（xunit.v3 + Avalonia.Headless.XUnit 12.0.4）
  - `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs`（被测 API）
  - `docs/PLAN.md`（表格格式锚点）

  **Acceptance Criteria**:
  - [ ] 新增测试文件覆盖上述 8 组用例
  - [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿（含既有测试无回归）
  - [ ] `docs/PLAN.md` 已加 P2 行
  - [ ] `lsp_diagnostics` 无错误

  **QA Scenarios**:

  ```
  Scenario: 单测全绿（含新增 + 既有无回归）
    Tool: Bash
    Preconditions: Task 1-3 产物已编译
    Steps:
      1. dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
      2. 观察新增用例通过率与既有用例
    Expected Result: 新增 8 组全 PASS，既有测试无回归
    Failure Indicators: 任一断言失败（短路错误、深度越界、取消被吞）
    Evidence: .omo/evidence/task-4-test-run.txt

  Scenario: 覆盖完整性
    Tool: read + grep
    Steps: 枚举测试方法，断言覆盖 8 组用例
    Expected Result: 覆盖清单与任务一致
    Failure Indicators: 缺任一组
    Evidence: .omo/evidence/task-4-coverage.txt
  ```

  **Evidence to Capture:** `task-4-test-run.txt`, `task-4-coverage.txt`

  **Commit**: YES — `test(avalonia): 解压冲突检测短路/过滤/深度单测 + PLAN.md 同步`

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.

- [ ] F1. **Plan Compliance Audit** — `oracle`
  For each "Must Have": verify implementation exists. For each "Must NOT Have": search for forbidden patterns — reject with file:line. Check evidence files exist.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run build + tests. Review changed files for: 类型压制、空 catch（除既有"跳过不可访问目录"）、注释代码、未用 using。检查 AI slop。
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | Files [N clean/N issues] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Execute EVERY QA scenario. 重点：解压到新目录（无冲突瞬间上屏）、解压到已有目录（前两层先出、深层补齐）、过滤场景、取消/关窗。保存到 `.omo/evidence/final-qa/`。
  Output: `Scenarios [N/N pass] | Integration [N/N] | Edge Cases [N tested] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff. Verify 1:1 — no missing, no creep. Check "Must NOT do". Detect contamination（如误改多包路径/压缩端）。
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

- **1**: `perf(avalonia): 解压预览冲突检测短路+过滤跳过+深度限制（ApplyConflictMarkers）` — ResultPreviewService.cs, dotnet build
- **2**: `perf(avalonia): 解压单包预览改为两阶段冲突检测（前两层先上屏）` — ExtractSettingsViewModel.cs, dotnet build
- **3**: `feat(avalonia): 解压设置窗口订阅深层冲突检测刷新事件` — ExtractSettingsWindow.axaml.cs, dotnet build
- **4**: `test(avalonia): 解压冲突检测短路/过滤/深度单测 + PLAN.md 同步` — tests, docs/PLAN.md, dotnet test

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
- [ ] All tests pass（含既有 `ExtractSettingsViewModelTests`/`ResultTreeViewFilterTests` 无回归）
- [ ] `docs/PLAN.md` 已同步（规则 1）
- [ ] `docs/PROGRESS.md` + `docs/progress-avalonia-detail.md` 已更新（规则 3，提交前）
