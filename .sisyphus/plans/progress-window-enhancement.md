# ProgressWindow 增强改造 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 增强 ProgressWindow 的显示信息——路径/文件名分离三行显示、文件级计数、实时统计栏（已处理/跳过/出错）、批处理每包摘要。

**Architecture:** 
- Core 层统一改动：`ArchiveProgress`/`ArchiveOptions`/`ExtractResult` 加字段，`FileConflictHelper` 加回调触发，引擎加跳过计数
- 新增 `ProgressDisplayCalculator` 将显示计算逻辑抽到 Core 层，WPF/Avalonia 共享
- UI 层仅做 XAML 布局调整和控件赋值（最薄的一层）

**Tech Stack:** .NET 9, WPF, SharpCompress, SharpSevenZip

---

## TL;DR

> **Quick Summary**: 重构 ProgressWindow 布局，把路径/文件名分离为三行显示；新增文件级计数和实时统计栏；批处理每项显示处理摘要；所有计算逻辑抽到 Core 层便于 Avalonia 迁移。
>
> **Deliverables**:
> - ProgressWindow 新布局（路径+文件名+文件计数+统计栏）
> - 批处理列表每项显示多行摘要
> - 文件级计数（ProcessedFiles/TotalFiles）实时更新
> - 跳过文件计数（通过 FileConflictHelper 回调统计）
> - ProgressDisplayCalculator 新工具类（无 UI 依赖）
>
> **Estimated Effort**: Medium (~3-4h)
> **Parallel Execution**: YES - 3 waves
> **Critical Path**: Task 1 → Task 4 → Task 6 → Task 8

---

## File Structure

### Core 层文件

| 文件 | 状态 | 职责 |
|------|------|------|
| `Core/Abstractions/ArchiveEngine.cs` | 修改 | `ArchiveProgress` 加 `SkippedFiles`/`FailedFiles`；`ArchiveOptions` 加 `ConflictActionCallback`；`ExtractResult` 加 `SkippedEntries` |
| `Core/Utils/FileConflictHelper.cs` | 修改 | `ResolvePath` 里回调 `ConflictActionCallback` |
| `Core/Models/ProgressBatchItem.cs` | 修改 | `BatchItem` 加 `TotalFiles`/`ProcessedFiles`/`SkippedFiles`/`FailedFiles` + `SummaryText` |
| `Core/Utils/ProgressDisplayCalculator.cs` | **新增** | 显示值计算工具类（纯计算，无 UI 依赖） |
| `Core/Engines/ZipEngine.cs` | 修改 | `ExtractAsync` 加跳过计数 |
| `Core/Engines/SevenZipEngine.cs` | 修改 | 同上 |
| `Core/Engines/TarGzEngine.cs` | 修改 | 同上 |

### UI 层文件

| 文件 | 状态 | 职责 |
|------|------|------|
| `UI/Dialogs/ProgressWindow.xaml` | 修改 | Grid 行调整，新增控件 |
| `UI/Dialogs/ProgressWindow.xaml.cs` | 修改 | `SetProgress` 调 `ProgressDisplayCalculator` 后赋控件 |
| `UI/AppPartials/App.Extract.cs` | 修改 | 批处理完成后从 `ExtractResult` 更新统计 |

---

## Execution Strategy

### Waves

```
Wave 1 (Core 数据层 — 5 任务):
├── Task 1: ArchiveEngine.cs 模型字段扩展
├── Task 2: ProgressBatchItem.cs 摘要字段
├── Task 3: ProgressDisplayCalculator.cs 新建
├── Task 4: FileConflictHelper.cs 回调
└── Task 5: 引擎跳过计数（ZipEngine + SevenZipEngine + TarGzEngine）

Wave 2 (UI 层 — 2 任务):
├── Task 6: ProgressWindow XAML 布局改动
└── Task 7: ProgressWindow.cs SetProgress + App.Extract.cs 统计更新
```

---

## TODOs

### Wave 1: Core 数据层（最大并行，5 任务）

- [ ] 1. **`ArchiveEngine.cs` 模型字段扩展**

  **What to do**:
  在 `ArchiveProgress` 中添加 `SkippedFiles` 和 `FailedFiles` 两个 int 字段（默认 0）。
  在 `ArchiveOptions` 中添加 `Action<FileConflictAction>? ConflictActionCallback` 回调属性。
  在 `ExtractResult` 中添加 `int SkippedEntries` 属性。

  **Must NOT do**:
  - 不要修改现有属性的 getter/setter 签名
  - 不要改动 `IArchiveEngine` 接口

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - 简单的字段新增，无逻辑变
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3, 4)
  - **Blocks**: Task 5 (engines need the new fields)
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` — 三个类 `ArchiveProgress`(L245)、`ArchiveOptions`(L27)、`ExtractResult`(L261) 都在此文件

  **Acceptance Criteria**:

  **QA Scenarios**:
  ```
  Scenario: 编译验证新增字段
    Tool: Bash
    Steps:
      1. 运行 dotnet build src\MantisZip.Core\MantisZip.Core.csproj
    Expected Result: 编译通过，无警告
    Evidence: .sisyphus/evidence/task-1-build.txt
  ```

  **Commit**: NO (groups with Wave 1 at the end)

- [ ] 2. **`ProgressBatchItem.cs` 添加摘要字段**

  **What to do**:
  在 `BatchItem` 类中添加：
  - `public int TotalFiles { get; set; }`
  - `public int ProcessedFiles { get; set; }`
  - `public int SkippedFiles { get; set; }`
  - `public int FailedFiles { get; set; }`
  - `public string SummaryText` 只读计算属性，返回格式化摘要文本（"已处理 45/200  ⏭跳过 3  ❌出错 1"）

  **Must NOT do**:
  - 不要修改现有属性的行为
  - 不要在字段 setter 中触发 `PropertyChanged`（因为目前直接从后台线程赋值，不需要通知，UI 通过外部机制刷新）

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - 纯模型字段新增
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 3, 4)
  - **Blocks**: None directly (used in UI wave)
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.Core/Models/ProgressBatchItem.cs` — `BatchItem` 类定义

  **Acceptance Criteria**:

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.Core\MantisZip.Core.csproj
    Expected Result: 编译通过
    Evidence: .sisyphus/evidence/task-2-build.txt
  ```

  **Commit**: NO (groups with Wave 1 at the end)

- [ ] 3. **新建 `ProgressDisplayCalculator.cs`**

  **What to do**:
  在 `Core/Utils/` 下新建文件 `ProgressDisplayCalculator.cs`，包含以下静态方法：

  ```csharp
  namespace MantisZip.Core.Utils;

  using System.IO; // for Path methods

  public static class ProgressDisplayCalculator
  {
      public static (string dirPath, string fileName) SplitFilePath(string currentFile)
      {
          if (string.IsNullOrEmpty(currentFile))
              return ("", "");
          var dir = Path.GetDirectoryName(currentFile);
          var name = Path.GetFileName(currentFile);
          return (dir ?? "", name);
      }

      public static double CalculateOverallPercent(
          ArchiveProgress p, bool isBatchMode,
          int currentBatchIndex, int batchCount)
      {
          if (isBatchMode && batchCount > 1)
          {
              double completedWeight = currentBatchIndex > 0
                  ? (double)currentBatchIndex / batchCount * 100
                  : 0;
              double currentWeight = p.PercentComplete / batchCount;
              return completedWeight + currentWeight;
          }
          return p.PercentComplete;
      }

      public static string FormatStatsText(
          int processed, int total, int skipped, int failed)
      {
          var parts = new List<string>();
          if (total > 0)
              parts.Add($"✅ 已处理 {processed}/{total}");
          if (skipped > 0)
              parts.Add($"⏭跳过 {skipped}");
          if (failed > 0)
              parts.Add($"❌出错 {failed}");
          return parts.Count > 0 ? string.Join("  ", parts) : "";
      }

      public static string FormatFileCount(int processed, int total)
          => total > 0 ? $"文件 {processed}/{total}" : "";
  }
  ```

  **Must NOT do**:
  - 不要引用任何 WPF/Avalonia 命名空间
  - 方法必须是纯函数（无副作用，无 UI 依赖）

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - 纯静态工具类，无复杂逻辑
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 4)
  - **Blocks**: Task 7 (ProgressWindow.cs uses this)
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` — `ArchiveProgress` 类的属性签名

  **Acceptance Criteria**:

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.Core\MantisZip.Core.csproj
    Expected Result: 编译通过
    Evidence: .sisyphus/evidence/task-3-build.txt
  ```

  **Commit**: NO (groups with Wave 1 at the end)

- [ ] 4. **`FileConflictHelper.cs` 添加回调触发**

  **What to do**:
  在 `FileConflictHelper.ResolvePath(string outputPath, ArchiveOptions? options, ...)` 中，在计算出最终 `action` 后、调用 `ResolveByAction` 之前，插入一行：

  ```csharp
  // 计算出最终 action 后（包括从 Ask 弹窗获取用户选择后），通知调用方
  options?.ConflictActionCallback?.Invoke(action);
  ```

  代码位置在 `ResolvePath` 方法中，大概在 `File.Exists` 检查之后、`ResolveByAction` 调用之前。具体：
  - 如果 `File.Exists(outputPath)` 为 false，直接 return（无冲突，不触发回调）
  - 如果存在冲突，计算出 action（包括走 Ask → ConflictResolver），然后插入回调调用，再调用 `ResolveByAction`

  **Must NOT do**:
  - 不要改变 `ResolvePath` 的返回值和行为
  - 不要修改 `ResolveByAction` 私有方法

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - 单行插入，逻辑简单
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 3)
  - **Blocks**: Task 5 (engines rely on the callback)
  - **Blocked By**: Task 1 (need `ArchiveOptions.ConflictActionCallback`)

  **References**:
  - `src/MantisZip.Core/Utils/FileConflictHelper.cs` — `ResolvePath` 方法，约第 17-60 行

  **Acceptance Criteria**:

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.Core\MantisZip.Core.csproj
    Expected Result: 编译通过
    Evidence: .sisyphus/evidence/task-4-build.txt
  ```

  **Commit**: NO (groups with Wave 1)

- [ ] 5. **引擎跳过计数（ZipEngine + SevenZipEngine + TarGzEngine）**

  **What to do**:
  在三个引擎的 `ExtractAsync` 方法中，添加跳过计数逻辑。模式三引擎通用：

  1. 方法开头声明局部变量 `int skippedCount = 0`
  2. 包装 `options.ConflictActionCallback` 来累计跳过数（保留原始回调链）
  3. 返回 `ExtractResult` 时带上 `SkippedEntries = skippedCount`

  **ZipEngine** 改动模式（在 ExtractAsync 中，options 使用前）：
  ```csharp
  int skippedCount = 0;
  var originalCallback = options?.ConflictActionCallback;
  var countingCallback = new Action<FileConflictAction>(action =>
  {
      if (action == FileConflictAction.Skip ||
          action == FileConflictAction.OverwriteIfOlder ||
          action == FileConflictAction.OverwriteIfSmaller)
      {
          Interlocked.Increment(ref skippedCount);
      }
      originalCallback?.Invoke(action);
  });
  if (options != null)
      options.ConflictActionCallback = countingCallback;

  // ... 提取循环不变（ResolvePath 内部已触发 countingCallback）

  return new ExtractResult
  {
      SucceededEntries = successCount,
      FailedEntries = failCount,
      SkippedEntries = skippedCount
  };
  ```

  **需注意的引擎差异**：
  - **ZipEngine**：跳过时已加 `processedBytes += entry.Size`，只需加 `skippedCount`
  - **SevenZipEngine**：同上，注意 `ArchivePath.Normalize` 后的 entryKey
  - **TarGzEngine**：有两个提取路径（TAR 条目和 .gz 文件），都需要加

  **Must NOT do**:
  - 不要改动提取循环的主体逻辑
  - 不要在 `continue` 之外加新的副作用

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - 涉及三个引擎，需理解每个引擎的提取循环结构
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO (one engine at a time)
  - **Parallel Group**: Wave 1 (sequential)
  - **Blocks**: Wave 2 (need engines to report counts)
  - **Blocked By**: Tasks 1, 4

  **References**:
  - `src/MantisZip.Core/Engines/ZipEngine.cs` — `ExtractAsync`，`ResolvePath` 调用处
  - `src/MantisZip.Core/Engines/SevenZipEngine.cs` — 同上
  - `src/MantisZip.Core/Engines/TarGzEngine.cs` — 同上（注意两个路径）

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.Core\MantisZip.Core.csproj
    Expected Result: 编译通过
    Evidence: .sisyphus/evidence/task-5-build.txt
  ```

  **Commit**: NO (groups with Wave 1 — 全部 Wave 1 任务一起提交)

---

### Wave 2: UI 层（2 任务）

- [ ] 6. **ProgressWindow XAML 布局改动**

  **What to do**:
  修改 `ProgressWindow.xaml`，调整 Grid 行布局，新增控件：

  **Grid 行调整**（当前→改动后）：
  ```
  Row 2: FileNameText → 拆分为 Row 2(DirPathText) + Row 3(FileNameText)
  Row 3: 文件进度条 → Row 4
  Row 4: 总进度条 → Row 6
  Row 5: FileCountText → Row 8
  Row 6: ErrorSummaryBox → Row 9
  Row 7: 弹性填充 → Row 10
  Row 8: 按钮行 → Row 11
  ```

  **新增控件**：

  Row 2（目录路径）：
  ```xml
  <TextBlock x:Name="DirPathText" Grid.Row="2"
             Text="" FontSize="12"
             Foreground="{StaticResource Theme_TextSecondary}"
             TextTrimming="PathEllipsis"/>
  ```

  Row 3（文件名——原 FileNameText 移过来）：
  ```xml
  <TextBlock x:Name="FileNameText" Grid.Row="3"
             Text="{l:L Progress_Processing}"
             TextTrimming="CharacterEllipsis"
             Foreground="{StaticResource Theme_TextPrimary}"/>
  ```

  Row 5（文件级计数，放在文件进度条下方）：
  ```xml
  <TextBlock x:Name="FileProgressCountText" Grid.Row="5"
             Text="" FontSize="12"
             Foreground="{StaticResource Theme_TextSecondary}"
             Margin="0,0,0,4"/>
  ```

  Row 7（统计栏，放在总进度条下方）：
  ```xml
  <Border x:Name="StatsBar" Grid.Row="7"
          Background="{StaticResource Theme_SurfaceBg}"
          CornerRadius="4" Padding="8,4" Margin="0,4,0,4"
          Visibility="Visible">
      <TextBlock x:Name="StatsBarText" Text=""
                 FontSize="12"
                 Foreground="{StaticResource Theme_TextPrimary}"/>
  </Border>
  ```

  **BatchFileList DataTemplate 扩展**（在现有 3 列基础上，第 2 列增加摘要文字）：
  列 2 的 TextBlock 外面包一个 StackPanel：
  ```xml
  <!-- 第 2 列: 文件名 + 进度条底色 + 摘要 -->
  <StackPanel Grid.Column="1" VerticalAlignment="Center">
      <TextBlock Text="{Binding Name}"
                 TextTrimming="CharacterEllipsis"
                 Foreground="{StaticResource Theme_TextPrimary}"
                 Padding="4,2,4,0">
          <TextBlock.Background> ...现有 MultiBinding... </TextBlock.Background>
      </TextBlock>
      <TextBlock Text="{Binding SummaryText}"
                 FontSize="11"
                 Foreground="{StaticResource Theme_TextSecondary}"
                 Padding="4,0,4,2"
                 Visibility="{Binding SummaryText, Converter={StaticResource StringNotEmptyToVisibilityConverter}}"/>
  </StackPanel>
  ```

  > 如果 `StringNotEmptyToVisibilityConverter` 不存在，需要在 `Window.Resources` 中添加一个简单的 IValueConverter。

  **Grid 所有行定义**更新后如下：
  ```
  Row 0: BatchFileList
  Row 1: PasswordSection
  Row 2: DirPathText (NEW)
  Row 3: FileNameText (Moved from Row 2)
  Row 4: FileProgressBar + FilePercentText
  Row 5: FileProgressCountText (NEW)
  Row 6: TotalProgressBar + PercentText
  Row 7: StatsBar (NEW)
  Row 8: FileCountText
  Row 9: ErrorSummaryBox
  Row 10: 弹性填充 (Auto/*)
  Row 11: 按钮行
  ```

  所有现有控件的 `Grid.Row` 值需要对应调整。

  **Must NOT do**:
  - 不要修改现有控件的除 `Grid.Row` 外的属性
  - 不要改变窗口尺寸、Title、Topmost 等基本属性

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
    - WPF XAML 布局调整
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (with Task 7)
  - **Blocks**: None
  - **Blocked By**: None（与 Task 7 可并行，但建议先 XAML 再 code-behind）

  **References**:
  - `src/MantisZip.UI/Dialogs/ProgressWindow.xaml` — 现有布局

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected Result: 编译通过
    Evidence: .sisyphus/evidence/task-6-build.txt
  ```

  **Commit**: NO (groups with Wave 2 at the end)

- [ ] 7. **ProgressWindow.cs SetProgress 重构 + App.Extract.cs 统计更新**

  **What to do**:

  **7a. `ProgressWindow.xaml.cs` — `SetProgress` 方法重构**

  将现有 `SetProgress(ArchiveProgress p)` 中的计算逻辑替换为调用 `ProgressDisplayCalculator`，新增更新 DirPathText/FileProgressCountText/StatsBarText 控件的代码。

  核心修改（约第 104 行开始）：
  ```csharp
  public void SetProgress(ArchiveProgress p)
  {
      App.LogDebug("[TRACE] ProgressWindow.SetProgress called: ...");

      // ---- 计算（Core 层，无 UI 依赖） ----
      var (dirPath, fileName) = ProgressDisplayCalculator.SplitFilePath(p.CurrentFile);
      var overallPct = ProgressDisplayCalculator.CalculateOverallPercent(
          p, _isBatchMode, _currentBatchIndex, _batchItems?.Count ?? 0);
      var statsText = ProgressDisplayCalculator.FormatStatsText(
          p.ProcessedFiles, p.TotalFiles, p.SkippedFiles, p.FailedFiles);
      var fileCountText = ProgressDisplayCalculator.FormatFileCount(
          p.ProcessedFiles, p.TotalFiles);

      // ---- 赋值（WPF 特有，迁移时替换） ----
      DirPathText.Text = dirPath;
      FileNameText.Text = fileName;
      FileProgressCountText.Text = fileCountText;
      StatsBarText.Text = statsText;

      // 总进度
      TotalProgressBar.Value = overallPct;
      PercentText.Text = $"{overallPct:F1}%";

      // 文件进度条
      if (p.FilePercentComplete.HasValue)
      {
          FileProgressBar.Value = p.FilePercentComplete.Value;
          FilePercentText.Text = $"{p.FilePercentComplete.Value:F0}%";
      }

      // 压缩包计数（批处理模式）
      if (_isBatchMode && _batchItems != null && _batchItems.Count > 0)
      {
          int current = _currentBatchIndex >= 0
              ? Math.Min(_currentBatchIndex + 1, _batchItems.Count)
              : Math.Min((int)p.PercentComplete / 100 * _batchItems.Count, _batchItems.Count);
          if (current < 1) current = 1;
          FileCountText.Text = L.TF(L.Progress_FileCount, current, _batchItems.Count);
      }
      else
      {
          FileCountText.Text = L.TF(L.Progress_FileCount, 1, 1);
      }
      FileCountText.Visibility = Visibility.Visible;

      // 批处理模式：更新当前 BatchItem 的摘要字段
      if (_isBatchMode && _currentBatchIndex >= 0 && _batchItems != null &&
          _currentBatchIndex < _batchItems.Count)
      {
          _batchItems[_currentBatchIndex].TotalFiles = p.TotalFiles;
          _batchItems[_currentBatchIndex].ProcessedFiles = p.ProcessedFiles;
          _batchItems[_currentBatchIndex].SkippedFiles = p.SkippedFiles;
          _batchItems[_currentBatchIndex].FailedFiles = p.FailedFiles;

          // 节流更新进度（原有逻辑）
          var now = DateTime.UtcNow;
          if (p.PercentComplete >= 100 || p.PercentComplete <= 0 ||
              (now - _lastProgressUpdate) >= ProgressThrottle)
          {
              _batchItems[_currentBatchIndex].Progress = p.PercentComplete;
              _lastProgressUpdate = now;
          }
      }
  }
  ```

  注意：`FileNameText.Text` 不再需要设置——现在只显示纯文件名，已经在前面通过 `SplitFilePath` 设置了。

  **7b. `App.Extract.cs` — 批处理完成后更新统计**

  在批处理循环中，每个压缩包提取完成后（`engine.ExtractAsync` 返回后），获取 `extractResult.SkippedEntries` 并推送到 ProgressWindow。

  大致在 `App.Extract.cs` 的批处理循环中，约第 383 行附近：
  ```csharp
  var extractResult = await engine.ExtractAsync(...);

  // 新增：推送跳过计数到 ProgressWindow 的当前批处理项
  if (progressWindow.IsBatchMode)
  {
      progressWindow.UpdateBatchItemSkipCount(i, extractResult.SkippedEntries);
  }
  ```

  在 `ProgressWindow.xaml.cs` 中新增方法：
  ```csharp
  /// <summary>
  /// 更新批处理项的跳过计数（从 ExtractResult 获取）。
  /// </summary>
  public void UpdateBatchItemSkipCount(int index, int skippedEntries)
  {
      void Update()
      {
          if (_batchItems == null || index < 0 || index >= _batchItems.Count)
              return;
          _batchItems[index].SkippedFiles = skippedEntries;
          // 如果此时 ProgressWindow 的 StatsBar 显示的是当前项，也更新它
      }
      DispatchIfNeeded(Update, DispatcherPriority.Background);
  }
  ```

  **Must NOT do**:
  - 不要删除现有 `SetProgress` 的任何功能（进度条、计数、密码区逻辑保持不变）
  - 不要改动 `BackgroundDispatcherProgress` 和 `PauseAwareProgress` 类

  **Recommended Agent Profile**:
  - **Category**: `deep` (for the code-behind refactoring) + `quick` (for App.Extract.cs)
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (with Task 6, sequential)
  - **Blocks**: None
  - **Blocked By**: Tasks 3, 5, 6 (need ProgressDisplayCalculator + engine counts + new XAML controls)

  **References**:
  - `src/MantisZip.UI/Dialogs/ProgressWindow.xaml.cs` — `SetProgress` 方法 (L104)
  - `src/MantisZip.UI/AppPartials/App.Extract.cs` — 批处理循环，约 L383
  - `src/MantisZip.Core/Utils/ProgressDisplayCalculator.cs` — 新建的工具类

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected Result: 编译通过
    Evidence: .sisyphus/evidence/task-7-build.txt

  Scenario: 运行验证（快速压缩场景）
    Tool: Bash
    Steps:
      1. 创建临时测试目录和文件
      2. dotnet run --project src\MantisZip.UI\MantisZip.UI.csproj --compress-quick <test-files> -- <output.zip>
    Expected Result: ProgressWindow 显示路径+文件名+统计栏
    Evidence: .sisyphus/evidence/task-7-run.txt
  ```

  **Commit**: YES (groups with Task 6)
  - Message: `feat(ui): enhance ProgressWindow with split path display, file progress count, and stats bar`
  - Files: `ProgressWindow.xaml`, `ProgressWindow.xaml.cs`, `App.Extract.cs`

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each task: verify the described changes exist in code. Check: ArchiveProgress has SkippedFiles/FailedFiles? FileConflictHelper calls ConflictActionCallback? Engines count skips? ProgressDisplayCalculator exists? ProgressWindow XAML has new controls? SetProgress uses ProgressDisplayCalculator? Evidence files in .sisyphus/evidence/.
  Output: `Must Have [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality & Build** — `unspecified-high`
  Global build check: `dotnet build`. Run `dotnet test tests/MantisZip.Tests/` (existing tests must not regress). Review changed files for: empty catches, `#warning`/`TODO` left in, commented-out code.
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Start clean. Run app, trigger a compress operation, verify:
  - ProgressWindow shows path (directory) + filename (separate)
  - File progress count updates (文件 50/200)
  - Stats bar shows processed/skipped/failed counts
  - Batch mode: each item in the list shows summary text
  - Error summary still appears on permission errors
  Save evidence to `.sisyphus/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do" + actual diff. Verify 1:1 — everything in spec was built, nothing beyond spec was built. Check "Must NOT do" compliance. Detect cross-task contamination.
  Output: `Tasks [N/N compliant] | VERDICT`

---

## Commit Strategy

| Step | Message | Scope |
|------|---------|-------|
| Wave 1 (after Tasks 1-5) | `feat(core): add skip counting and conflict action callback infrastructure` | ArchiveProgress, ArchiveOptions, ExtractResult, FileConflictHelper, ProgressDisplayCalculator, BatchItem, 3 engines |
| Wave 2 (after Tasks 6-7) | `feat(ui): enhance ProgressWindow with split path display, file progress count, and stats bar` | ProgressWindow.xaml, ProgressWindow.xaml.cs, App.Extract.cs |

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```

### Final Checklist
- [ ] `ProgressWindow` 显示目录路径（第一行）+ 纯文件名（第二行）
- [ ] `FileProgressCountText` 显示当前包的文件计数（文件 50/200）
- [ ] `StatsBar` 显示实时统计（✅ 已处理 N  ⏭跳过 N  ❌出错 N）
- [ ] 批处理模式下，BatchFileList 每项显示摘要文字
- [ ] 错误摘要（ErrorSummaryBox）在权限不足时仍正常显示
- [ ] 非批处理模式下统计栏也正常显示
- [ ] `ConflictActionCallback` 在 `FileConflictHelper.ResolvePath` 中触发
- [ ] 三个引擎正确累计跳过计数并返回 `ExtractResult.SkippedEntries`
- [ ] `ProgressDisplayCalculator` 无任何 WPF/Avalonia 依赖
