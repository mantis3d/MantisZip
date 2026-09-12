# 骨架态实时进度计数器

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

## TL;DR

> **Quick Summary**: 骨架加载期间，目录节点的统计区域（原本显示文件数/大小的地方）目前显示静态「加载中」。本计划改为**每 0.5s 更新一次实时进度**（如「加载中 1637 个文件，2.45GB」），让用户看到加载在推进。
>
> **Deliverables**:
> - `Services/ResultPreviewService.cs`：`BuildDirectoryNode` 新增 `IProgress<SkeletonProgress>` 回调，每处理一个文件报告增量
> - `Models/PreviewTreeNode.cs`：新增 `CurrentSkeletonProgress` 属性，`DirectoryInfoText` 改为动态计算
> - `ViewModels/CompressSettingsViewModel.cs`：`DispatcherTimer` 每 500ms 轮询进度，更新节点属性
> - 本地化新增 `Preview_Result_SkeletonProgress`
> - 单元测试（进度报告 + DirectoryInfoText 动态显示）
>
> **Estimated Effort**: Small
> **Parallel Execution**: NO — 顺序执行，依赖链清晰
> **Critical Path**: Task 1 (record) → Task 2 (节点属性) → Task 3 (回调) → Task 4 (定时器) → Task 5 (测试) → user okay

---

## Context

### Original Request

用户提出：「每隔一段时间，比如说0.5秒，"加载中"后面跟上此目录已经加载的文件数量，比如"加载中 1637个文件，2.45GB"这样」。目标是压缩设置窗口骨架加载期间显示实时进度。

### Research Findings

- `BuildDirectoryNode` 是同步方法，内部没有进度回调。单个源可能处理几万个文件，期间 UI 完全静止
- `DirectoryInfoText` 是计算属性（`get` accessor），需要改为动态以支持实时更新
- Phase B 每源完成后调用 `AssembleAsync` 更新 `PreviewRoot`，但期间无中间状态
- 现有 `IProgress<T>` 模式已用于 `BuildExtractPreview`，可复用

---

## Work Objectives

### Core Objective

骨架加载期间目录节点显示实时进度（文件数 + 大小），每 0.5s 更新一次。

### Concrete Deliverables

- `ResultPreviewService.cs`：`SkeletonProgress` record + `BuildDirectoryNode` 回调参数
- `PreviewTreeNode.cs`：`CurrentSkeletonProgress` 属性 + `DirectoryInfoText` 动态化
- `CompressSettingsViewModel.cs`：`DispatcherTimer` + 进度追踪字典 + 定时器生命周期管理
- `CompressPreviewProgressiveTests.cs`：进度报告测试 + DirectoryInfoText 动态测试
- `docs/PLAN.md`：新增 P2 行（规则 1）

### Definition of Done

- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded（0 error）
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿
- [ ] `docs/PLAN.md` 已同步（规则 1）

### Must Have

- `BuildDirectoryNode` 每处理一个文件回调 `IProgress<SkeletonProgress>`
- `PreviewTreeNode.CurrentSkeletonProgress` 非 null 时 `DirectoryInfoText` 显示「加载中 N 个文件，X B」
- `CompressSettingsViewModel` Phase B 期间 `DispatcherTimer` 每 500ms 轮询
- 定时器在 Phase B 结束/取消时正确停止并清理
- 新增用户可见文案走本地化（规则 13），zh/en 成对

### Must NOT Have (Guardrails)

- **不得**修改 `BuildExtractPreview`（解压预览不在范围）
- **不得**修改 `RebuildDisplayTree` 语义
- **不得**硬编码间距/高度（规则 5）
- 版本号不变（规则 2）

---

## Verification Strategy (MANDATORY)

### Test Decision

- **Infrastructure exists**: YES（`tests/MantisZip.UI.Avalonia.Tests/`，xunit.v3 + Avalonia.Headless.XUnit）
- **Automated tests**: Unit tests（服务层纯逻辑 + 真实临时目录枚举）

### QA Policy

Every task MUST include agent-executed QA scenarios. Evidence saved to `.omo/evidence/task-{N}-{scenario-slug}.{ext}`.

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Sequential):
├── Task 1: SkeletonProgress record
├── Task 2: PreviewTreeNode 属性
├── Task 3: BuildDirectoryNode 回调
├── Task 4: DispatcherTimer 轮询
└── Task 5: 单元测试 + 文档同步

Wave FINAL (After ALL tasks — user okay):
-> Present results -> Get explicit user okay
```

---

## TODOs

- [ ] 1. 定义 SkeletonProgress record

  **What to do**:

  在 `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs` 的 `SourceSubtree` record 之前新增：

  ```csharp
  /// <summary>
  /// 浅层构建过程中的增量进度报告。
  /// </summary>
  public sealed record SkeletonProgress(int FileCount, long TotalSize);
  ```

  **Must NOT do**:
  - 不改 `SourceSubtree` 的签名或语义

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: Task 2, 3, 4
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] `SkeletonProgress` record 存在
  - [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → Build succeeded

  **QA Scenarios**:

  ```
  Scenario: 构建验证
    Tool: Bash
    Steps: dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: Build succeeded（0 error）
    Evidence: .omo/evidence/task-1-build.txt
  ```

  **Commit**: YES — `chore: add SkeletonProgress record`

---

- [ ] 2. PreviewTreeNode 新增 CurrentSkeletonProgress + DirectoryInfoText 动态化

  **What to do**:

  在 `src/MantisZip.UI.Avalonia/Models/PreviewTreeNode.cs` 中：

  (2a) 新增字段（`SizeDisplay` 之后）：

  ```csharp
  /// <summary>
  /// 骨架构建过程中的实时进度（文件数 + 大小）。
  /// 非 null 时 DirectoryInfoText 显示动态进度而非最终统计。
  /// </summary>
  public SkeletonProgress? CurrentSkeletonProgress { get; set; }
  ```

  注意：需要添加 `using MantisZip.UI.Avalonia.Services;`。

  (2b) 修改 `DirectoryInfoText` 为动态计算属性：

  ```csharp
  /// <summary>目录统计摘要文本，仅目录节点有值（空目录不显示统计行）。
  /// 含占位子节点时显示实时进度或「加载中」。</summary>
  public string DirectoryInfoText
  {
      get
      {
          if (HasLoadingPlaceholderChild)
          {
              if (CurrentSkeletonProgress is { } sp && sp.FileCount > 0)
                  return LocalizationManager.T("Preview_Result_SkeletonProgress", sp.FileCount, FormatUtil.FormatSize(sp.TotalSize));
              return LocalizationManager.T("Preview_Result_LoadingMore");
          }
          if (!IsEmptyDirectory && Children.Count > 0 && !string.IsNullOrEmpty(FullPath))
              return LocalizationManager.T("Preview_Result_DirInfo", TotalDescendantCount, FormatUtil.FormatSize(TotalDescendantSize));
          return string.Empty;
      }
  }
  ```

  (2c) 在 `ShallowClone()` 中添加 `CurrentSkeletonProgress = CurrentSkeletonProgress,`

  (2d) 新增本地化 key：

  `strings.zh-CN.json`：
  ```json
  "Preview_Result_SkeletonProgress": "加载中 {0} 个文件，{1}",
  ```

  `strings.en.json`：
  ```json
  "Preview_Result_SkeletonProgress": "Loading {0} files, {1}",
  ```

  (2e) 在 `MainWindowViewModel.UpdateLocalizedStrings()` 的 keys 数组中添加 `"Preview_Result_SkeletonProgress"`

  **Must NOT do**:
  - 不改 `IsEmptyDirectory` 语义
  - 不改 `HasLoadingPlaceholderChild` 逻辑

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: Task 3, 4
  - **Blocked By**: Task 1

  **Acceptance Criteria**:
  - [ ] `CurrentSkeletonProgress` 属性存在
  - [ ] `DirectoryInfoText` 在有占位子节点时返回「加载中」或动态进度
  - [ ] 两个 strings 文件均含 `Preview_Result_SkeletonProgress`
  - [ ] `MainWindowViewModel.UpdateLocalizedStrings` 已注册
  - [ ] `dotnet build` → Build succeeded

  **QA Scenarios**:

  ```
  Scenario: 构建 + i18n key 成对
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
      2. grep 两个 strings 文件 Preview_Result_SkeletonProgress
    Expected Result: Build succeeded；两文件各 1 处命中
    Evidence: .omo/evidence/task-2-build.txt
  ```

  **Commit**: YES — `feat(avalonia): add SkeletonProgress property with dynamic DirectoryInfoText`

---

- [ ] 3. BuildDirectoryNode 新增 IProgress 回调

  **What to do**:

  (3a) 修改 `BuildDirectoryNode` 签名（`ResultPreviewService.cs:491-493`）：

  ```csharp
  private static PreviewTreeNode BuildDirectoryNode(
      string rootPath, string currentPath, FileFilterCriteria? filter, List<string>? includedFiles,
      int maxDepth = int.MaxValue, int maxWidthPerDir = int.MaxValue, CancellationToken ct = default, int depth = 0,
      IProgress<SkeletonProgress>? skeletonProgress = null, int initialFileCount = 0, long initialTotalSize = 0)
  ```

  (3b) 在 `int added = 0;` 之后添加进度变量：

  ```csharp
  int fileCount = initialFileCount;
  long totalSize = initialTotalSize;
  ```

  (3c) 在每个文件节点添加后（`added++;` 之前）报告进度：

  ```csharp
  if (skeletonProgress != null)
  {
      fileCount++;
      totalSize += file.Length;
      skeletonProgress.Report(new SkeletonProgress(fileCount, totalSize));
  }
  ```

  (3d) 递归调用时传递进度参数：

  ```csharp
  node.Children.Add(BuildDirectoryNode(rootPath, subDir.FullName, filter, includedFiles, maxDepth, maxWidthPerDir, ct, depth + 1, skeletonProgress, fileCount, totalSize));
  ```

  (3e) 修改 `BuildSourceSubtree` 签名，新增 `IProgress<SkeletonProgress>? skeletonProgress = null` 参数，并传递给 `BuildDirectoryNode`。

  **Must NOT do**:
  - 不改 `BuildExtractPreview` 签名
  - 不改 `OperationCanceledException` 穿透逻辑

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: Task 4, 5
  - **Blocked By**: Task 1, 2

  **Acceptance Criteria**:
  - [ ] `BuildDirectoryNode` 接受 `IProgress<SkeletonProgress>` 参数
  - [ ] 每处理一个文件报告一次进度
  - [ ] `BuildSourceSubtree` 接受并传递 `skeletonProgress`
  - [ ] `dotnet build` → Build succeeded
  - [ ] 既有测试全绿（96 passed）

  **QA Scenarios**:

  ```
  Scenario: 构建 + 回归测试
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
      2. dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
    Expected Result: Build succeeded；96 passed, 0 failed, 2 skipped
    Evidence: .omo/evidence/task-3-build-test.txt
  ```

  **Commit**: YES — `feat(avalonia): add IProgress<SkeletonProgress> to BuildDirectoryNode`

---

- [ ] 4. CompressSettingsViewModel 添加 DispatcherTimer 轮询

  **What to do**:

  (4a) 新增字段（`ShallowMaxWidthPerDir` 之后）：

  ```csharp
  private DispatcherTimer? _skeletonProgressTimer;
  private readonly Dictionary<string, (int FileCount, long TotalSize)> _skeletonProgressTrackers = new();
  ```

  注意：需要 `using Avalonia.Threading;`

  (4b) 新增定时器管理方法：

  ```csharp
  private void StartSkeletonProgressTimer()
  {
      StopSkeletonProgressTimer();
      _skeletonProgressTimer = new DispatcherTimer
      {
          Interval = TimeSpan.FromMilliseconds(500)
      };
      _skeletonProgressTimer.Tick += OnSkeletonProgressTick;
      _skeletonProgressTimer.Start();
  }

  private void StopSkeletonProgressTimer()
  {
      if (_skeletonProgressTimer != null)
      {
          _skeletonProgressTimer.Stop();
          _skeletonProgressTimer.Tick -= OnSkeletonProgressTick;
          _skeletonProgressTimer = null;
      }
  }

  private void OnSkeletonProgressTick(object? sender, EventArgs e)
  {
      if (PreviewRoot == null) return;
      UpdateSkeletonProgressRecursive(PreviewRoot);
  }

  private void UpdateSkeletonProgressRecursive(PreviewTreeNode node)
  {
      if (node.HasLoadingPlaceholderChild && _skeletonProgressTrackers.TryGetValue(node.FullPath, out var progress))
      {
          node.CurrentSkeletonProgress = new SkeletonProgress(progress.FileCount, progress.TotalSize);
          OnPropertyChanged(nameof(PreviewRoot));
      }
      foreach (var child in node.Children.OfType<PreviewTreeNode>())
          UpdateSkeletonProgressRecursive(child);
  }
  ```

  (4c) 修改 Phase B 使用进度回调：

  ```csharp
  // ── Phase B：全量（串行逐源；每源完成即渐进上屏）──
  StartSkeletonProgressTimer();
  try
  {
      foreach (var p in paths)
      {
          ct.ThrowIfCancellationRequested();
          if (_sourceCache.TryGetValue(p, out var c) && c.IsFull) continue;

          var progressTracker = new Progress<SkeletonProgress>(sp =>
          {
              _skeletonProgressTrackers[p] = (sp.FileCount, sp.TotalSize);
          });

          var full = await Task.Run(() => ResultPreviewService.BuildSourceSubtree(p, filter, int.MaxValue, int.MaxValue, ct, progressTracker), ct);
          if (version != _previewBuildVersion) return null;
          if (full != null) _sourceCache[p] = full;

          _skeletonProgressTrackers.Remove(p);

          if ((DateTime.UtcNow - _lastProgressiveAssemble).TotalMilliseconds >= 250)
          {
              var progressive = await AssembleAsync(paths, outputMode, outputPath, format, keepOriginalExtension, filter, ct);
              if (version != _previewBuildVersion) return null;
              PreviewRoot = progressive.Root;
              _lastProgressiveAssemble = DateTime.UtcNow;
          }
      }
  }
  finally
  {
      StopSkeletonProgressTimer();
  }
  ```

  (4d) 在 `BuildCompressPreviewCoreAsync` 的 `finally` 块中清理：

  ```csharp
  StopSkeletonProgressTimer();
  _skeletonProgressTrackers.Clear();
  ```

  **Must NOT do**:
  - 不改 Phase A/C 逻辑
  - 不改 `AdoptPlan` 语义

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: Task 5
  - **Blocked By**: Task 1, 2, 3

  **Acceptance Criteria**:
  - [ ] `_skeletonProgressTimer` 和 `_skeletonProgressTrackers` 字段存在
  - [ ] `StartSkeletonProgressTimer` / `StopSkeletonProgressTimer` 方法存在
  - [ ] Phase B 启动定时器，finally 停止
  - [ ] `dotnet build` → Build succeeded
  - [ ] 既有测试全绿

  **QA Scenarios**:

  ```
  Scenario: 构建 + 回归测试
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
      2. dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
    Expected Result: Build succeeded；96 passed, 0 failed, 2 skipped
    Evidence: .omo/evidence/task-4-build-test.txt
  ```

  **Commit**: YES — `feat(avalonia): add DispatcherTimer for skeleton progress polling`

---

- [ ] 5. 单元测试 + 文档同步

  **What to do**:

  在 `tests/MantisZip.UI.Avalonia.Tests/CompressPreviewProgressiveTests.cs` 新增：

  ```csharp
  [Fact]
  public void BuildSourceSubtree_ReportsProgress_Incrementally()
  {
      var dirA = Path.Combine(_rootDir, "a");
      var dirAB = Path.Combine(dirA, "b");
      Directory.CreateDirectory(dirAB);
      File.WriteAllText(Path.Combine(dirAB, "c.txt"), "content1");
      File.WriteAllText(Path.Combine(dirA, "d.txt"), "content2");
      File.WriteAllText(Path.Combine(_rootDir, "e.txt"), "content3");

      var reported = new List<SkeletonProgress>();
      var progress = new Progress<SkeletonProgress>(p => reported.Add(p));

      var result = ResultPreviewService.BuildSourceSubtree(_rootDir, maxDepth: 1, skeletonProgress: progress);

      Assert.NotNull(result);
      Assert.NotEmpty(reported);
      var last = reported.Last();
      Assert.Equal(2, last.FileCount); // d.txt + e.txt at depth 1
      Assert.True(last.TotalSize > 0);
  }

  [Fact]
  public void DirectoryInfoText_WithSkeletonProgress_ShowsProgressText()
  {
      var dirA = Path.Combine(_rootDir, "a");
      var dirAB = Path.Combine(dirA, "b");
      Directory.CreateDirectory(dirAB);
      File.WriteAllText(Path.Combine(dirAB, "c.txt"), "c");
      File.WriteAllText(Path.Combine(dirA, "d.txt"), "d");

      var result = ResultPreviewService.BuildSourceSubtree(_rootDir, maxDepth: 1);

      Assert.NotNull(result);
      var dirANode = result.Node.Children.OfType<PreviewTreeNode>().First(c => c.Name == "a");

      // Without progress: shows "加载中"
      Assert.Equal(LocalizationManager.T("Preview_Result_LoadingMore"), dirANode.DirectoryInfoText);

      // With progress: shows "加载中 N 个文件，X B"
      dirANode.CurrentSkeletonProgress = new SkeletonProgress(5, 1024);
      Assert.Contains("5", dirANode.DirectoryInfoText);
      Assert.Contains("1.0 KB", dirANode.DirectoryInfoText);
  }
  ```

  运行 `dotnet test` 全绿。

  同步 `docs/PLAN.md`：在「待实现设计方案」表格 P2 区新增一行：

  ```
  | **P2** | 骨架态实时进度计数器 | [skeleton-progress-counter.md](.omo/plans/未开始/skeleton-progress-counter.md) | 🟡中 | 3-4h | 骨架加载期间每 0.5s 更新目录节点的文件数/大小（"加载中 1637 个文件，2.45GB"）；IProgress<SkeletonProgress> 回调 + DispatcherTimer 轮询 |
  ```

  **Must NOT do**:
  - 不 mock 文件系统（用真实临时目录）
  - 不新增本地化 key

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: FINAL
  - **Blocked By**: Task 1, 2, 3, 4

  **Acceptance Criteria**:
  - [ ] 新增 2 个测试方法
  - [ ] `dotnet test` → 98 passed（96 existing + 2 new）, 0 failed, 2 skipped
  - [ ] `docs/PLAN.md` 已加 P2 行

  **QA Scenarios**:

  ```
  Scenario: 单测全绿
    Tool: Bash
    Steps: dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
    Expected Result: 98 passed, 0 failed, 2 skipped
    Evidence: .omo/evidence/task-5-test-run.txt
  ```

  **Commit**: YES — `test(avalonia): add tests for skeleton progress counter`

---

## Summary

| Task | Description | Files Changed |
|------|-------------|---------------|
| 1 | SkeletonProgress record | ResultPreviewService.cs |
| 2 | PreviewTreeNode 属性 + DirectoryInfoText 动态化 | PreviewTreeNode.cs, localization, MainWindowViewModel.cs |
| 3 | BuildDirectoryNode IProgress 回调 | ResultPreviewService.cs |
| 4 | DispatcherTimer 轮询 | CompressSettingsViewModel.cs |
| 5 | 单元测试 + 文档同步 | CompressPreviewProgressiveTests.cs, PLAN.md |
