# ProgressWindow 增强改造 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 增强 ProgressWindow 的显示信息——路径/文件名分离、文件级计数、实时统计栏（已处理/跳过/出错）、批处理每包摘要、并行进度显示、模式切换（简约/详细/列表）、时间估算。

**Architecture:** 
- Core 层统一改动：`ArchiveProgress`/`ArchiveOptions`/`ExtractResult` 加字段，`FileConflictHelper` 加回调触发，引擎加跳过计数
- 新增 `ProgressDisplayCalculator` 将显示计算逻辑抽到 Core 层，Avalonia 共享
- UI 层做 XAML 布局调整、模式切换、三种视图（简约/详细/列表）
- 支持并行解压时的多线程进度显示

**Tech Stack:** .NET 10, Avalonia, SharpCompress, SharpSevenZip

---

## TL;DR

> **Quick Summary**: 重构 ProgressWindow 布局，支持三种显示模式（简约/详细/列表）；新增路径分离、文件级计数、实时统计栏、并行线程进度、时间估算；所有计算逻辑抽到 Core 层。
>
> **Deliverables**:
> - ProgressWindow 三种显示模式（简约/详细/列表）
> - 路径/文件名分离显示
> - 文件级计数（ProcessedFiles/TotalFiles）实时更新
> - 跳过文件计数（通过 FileConflictHelper 回调统计）
> - 并行线程进度显示（详细模式）
> - 时间估算（已用时间 + 预计剩余）
> - ProgressDisplayCalculator 新工具类（无 UI 依赖）
>
> **Estimated Effort**: Medium (~5-6h)
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

Wave 2 (UI 层 — 5 任务):
├── Task 6: ProgressWindow XAML 布局改动 + 模式切换
├── Task 7: ProgressDisplayCalculator 时间估算 + 并行进度方法
├── Task 8: ProgressWindow.cs 代码逻辑（SetProgress + 模式切换 + 时间显示）
├── Task 9: ThreadProgressItem/FileInfoItem 模型类（新增）
└── Task 10: App.Extract.cs 统计更新 + 引擎并行进度上报
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
    Evidence: .omo/evidence/task-1-build.txt
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
    Evidence: .omo/evidence/task-2-build.txt
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
    Evidence: .omo/evidence/task-3-build.txt
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
    Evidence: .omo/evidence/task-4-build.txt
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
    Evidence: .omo/evidence/task-5-build.txt
  ```

  **Commit**: NO (groups with Wave 1 — 全部 Wave 1 任务一起提交)

---

### Wave 2: UI 层（5 任务）

- [ ] 6. **ProgressWindow XAML 布局改动 + 双模式切换**

  **What to do**:
  修改 `ProgressWindow.xaml`，采用上下两区域布局，支持独立模式切换：

  **布局结构**：
  ```
  ┌─────────────────────────────────────┐
  │ 标题栏                              │
  ├─────────────────────────────────────┤
  │ 【上方 - 文件信息区】                │
  │  • 批处理列表（固定显示）            │
  │  • 显示: [简约]|详细|列表            │ ← 上方内容切换
  │  • 模式切换内容                     │
  ├─────────────────────────────────────┤
  │ 【下方 - 整体信息区】（深色背景）     │
  │  【少】|中|完整                      │ ← 下方密度切换
  │  • 统计栏（完整模式）               │
  │  • 中等模式: 已处理+速度（单行）     │
  │  • 总体进度条                       │
  │  • 时间显示                         │
  └─────────────────────────────────────┘
  ```

  **Grid 行调整**：
  ```
  Row 0: TitleBar (标题栏)
  Row 1: FileInfoSection (上方区域)
    ├── BatchFileList (固定显示)
    ├── TopModeSwitcher (内联切换: 简约/详细/列表)
    ├── TopSimpleContent (简约模式: 当前文件+进度条)
    ├── TopDetailedContent (详细模式: 线程进度列表)
    └── TopListContent (列表模式: 文件列表+状态标记)
  Row 2: OverallInfoSection (下方区域)
    ├── DensitySwitcher (少/中/完整 三按钮)
    ├── StatsBar (完整模式: 已处理/跳过/出错/已覆盖 四项)
    ├── MediumStats (中等模式: 已处理+速度 单行)
    ├── TotalProgressBar
    └── TimeDisplay (所有模式)
  Row 3: ErrorSummaryBox
  Row 4: 弹性填充
  Row 5: 按钮行
  ```

  **上方区域 - 批处理列表（固定显示）**：
  ```xml
  <!-- 批处理列表 - 始终可见 -->
  <StackPanel Grid.Row="1" Margin="0,0,0,8">
      <TextBlock Text="压缩包列表" FontSize="11" Foreground="{StaticResource Theme_TextSecondary}"
                 Margin="0,0,0,4"/>
      <ListBox x:Name="BatchFileList" MaxHeight="120"
               ItemsSource="{Binding BatchItems}">
          <ListBox.ItemTemplate>
              <DataTemplate>
                  <StackPanel Orientation="Horizontal">
                      <TextBlock Text="{Binding StatusIcon}" Width="20"/>
                      <TextBlock Text="{Binding Name}" Width="200" TextTrimming="CharacterEllipsis"/>
                      <TextBlock Text="{Binding ProgressText}" Width="60" HorizontalAlignment="Right"/>
                  </StackPanel>
              </DataTemplate>
          </ListBox.ItemTemplate>
      </ListBox>
  </StackPanel>
  ```

  **上方区域 - 内联模式切换器**：
  ```xml
  <!-- 内联模式切换 - 紧凑按钮组 -->
  <StackPanel Orientation="Horizontal" Margin="0,4,0,8">
      <TextBlock Text="显示:" VerticalAlignment="Center" FontSize="11"
                 Foreground="{StaticResource Theme_TextSecondary}" Margin="0,0,6,0"/>
      <Button Content="简约" Tag="Simple" Click="OnTopModeChanged"
              Classes="ModeButton" />
      <Button Content="详细" Tag="Detailed" Click="OnTopModeChanged"
              Classes="ModeButton"/>
      <Button Content="列表" Tag="List" Click="OnTopModeChanged"
              Classes="ModeButton"/>
  </StackPanel>
  ```

  **上方区域 - 列表模式（虚拟化 + 状态标记）**：
  ```xml
  <ListBox x:Name="FileListPanel" Grid.Row="1"
           Visibility="Collapsed" MaxHeight="150"
           ItemsSource="{Binding FileListItems}">
      <!-- 虚拟化支持 -->
      <ListBox.ItemsPanel>
          <ItemsPanelTemplate>
              <VirtualizingStackPanel />
          </ItemsPanelTemplate>
      </ListBox.ItemsPanel>
      <ListBox.ItemTemplate>
          <DataTemplate>
              <StackPanel Orientation="Horizontal" Margin="0,2">
                  <!-- 状态图标 -->
                  <TextBlock Text="{Binding StatusIcon}" Width="20" FontSize="12"
                             Foreground="{Binding StatusBrush}"/>
                  <!-- 文件名 -->
                  <TextBlock Text="{Binding FileName}" Width="200" FontSize="12"
                             TextTrimming="CharacterEllipsis"
                             Foreground="{StaticResource Theme_TextPrimary}"/>
                  <!-- 文件大小 -->
                  <TextBlock Text="{Binding SizeText}" Width="60" FontSize="11"
                             HorizontalAlignment="Right"
                             Foreground="{StaticResource Theme_TextSecondary}"/>
                  <!-- 状态文本 -->
                  <TextBlock Text="{Binding StatusText}" Width="50" FontSize="11"
                             HorizontalAlignment="Right"
                             Foreground="{Binding StatusBrush}"/>
              </StackPanel>
          </DataTemplate>
      </ListBox.ItemTemplate>
  </ListBox>
  ```

  **下方区域 - 密度模式切换器（下方区域顶部）**：
  ```xml
  <!-- 密度切换按钮组 - 在下方区域内部 -->
  <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Center"
              Margin="0,0,0,8">
      <Button Content="少" Tag="Minimal" Click="OnDensityChanged"
              Classes="ModeButton"/>
      <Button Content="中" Tag="Medium" Click="OnDensityChanged"
              Classes="ModeButton"/>
      <Button Content="完整" Tag="Full" Click="OnDensityChanged"
              Classes="ModeButton"/>
  </StackPanel>
  ```

  **下方区域 - 三种密度模式显示内容**：
  ```xml
  <!-- 统计栏（完整模式：五项统计） -->
  <StackPanel x:Name="StatsBar" Grid.Row="2" Orientation="Horizontal"
              JustifyContent="SpaceAround" IsVisible="False">
      <StackPanel Orientation="Vertical" HorizontalAlignment="Center">
          <TextBlock Text="✅" FontSize="18"/>
          <TextBlock Text="已处理" FontSize="11" Foreground="..."/>
          <TextBlock Text="60/100" FontSize="16" FontWeight="SemiBold"/>
      </StackPanel>
      <StackPanel Orientation="Vertical" HorizontalAlignment="Center">
          <TextBlock Text="⏭" FontSize="18"/>
          <TextBlock Text="跳过" FontSize="11"/>
          <TextBlock Text="3" FontSize="16" FontWeight="SemiBold"/>
      </StackPanel>
      <StackPanel Orientation="Vertical" HorizontalAlignment="Center">
          <TextBlock Text="❌" FontSize="18"/>
          <TextBlock Text="出错" FontSize="11"/>
          <TextBlock Text="1" FontSize="16" FontWeight="SemiBold"/>
      </StackPanel>
      <StackPanel Orientation="Vertical" HorizontalAlignment="Center">
          <TextBlock Text="🔄" FontSize="18"/>
          <TextBlock Text="已覆盖" FontSize="11"/>
          <TextBlock Text="5" FontSize="16" FontWeight="SemiBold"/>
      </StackPanel>
      <StackPanel Orientation="Vertical" HorizontalAlignment="Center">
          <TextBlock Text="⚙️" FontSize="18"/>
          <TextBlock Text="进程" FontSize="11"/>
          <TextBlock Text="8 线程" FontSize="16" FontWeight="SemiBold"/>
      </StackPanel>
  </StackPanel>

  <!-- 中等模式：已处理+速度+进程数（单行） -->
  <StackPanel x:Name="MediumStats" Grid.Row="2" Orientation="Horizontal"
              JustifyContent="SpaceBetween" IsVisible="False"
              Padding="8,4" Background="...">
      <TextBlock FontSize="12">
          <Run Text="已处理:" Foreground="..."/>
          <Run Text="60/100" FontWeight="SemiBold"/>
      </TextBlock>
      <TextBlock FontSize="12">
          <Run Text="速度:" Foreground="..."/>
          <Run Text="12.5 MB/s" FontWeight="SemiBold"/>
      </TextBlock>
      <TextBlock FontSize="12">
          <Run Text="进程:" Foreground="..."/>
          <Run Text="8 线程" FontWeight="SemiBold"/>
      </TextBlock>
  </StackPanel>

  <!-- 时间显示（所有模式） -->
  <StackPanel x:Name="TimeDisplay" Grid.Row="2" Orientation="Horizontal"
              JustifyContent="SpaceBetween" Margin="0,8,0,0"
              Padding="0,8,0,0" BorderBrush="..." BorderThickness="0,1,0,0">
      <TextBlock FontSize="13" Foreground="...">
          <Run Text="已用:"/>
          <Run Text="00:00:12" FontWeight="SemiBold"/>
      </TextBlock>
      <TextBlock FontSize="13" Foreground="...">
          <Run Text="剩余:"/>
          <Run Text="00:00:08" FontWeight="SemiBold"/>
      </TextBlock>
  </StackPanel>
  ```

  **文件列表项状态样式**：
  ```xml
  <Style Selector="ListBoxItem">
      <Setter Property="MinHeight" Value="{DynamicResource ControlHeightSm}"/>
  </Style>
  
  <!-- 状态颜色 -->
  <Style Selector="TextBlock.StatusCompleted">
      <Setter Property="Foreground" Value="#6bd46b"/>
  </Style>
  <Style Selector="TextBlock.StatusError">
      <Setter Property="Foreground" Value="#ff6b6b"/>
  </Style>
  <Style Selector="TextBlock.StatusSkipped">
      <Setter Property="Foreground" Value="#888"/>
      <Setter Property="Opacity" Value="0.6"/>
  </Style>
  ```

  **Must NOT do**:
  - 不要修改现有控件的除 `Grid.Row` 外的属性
  - 不要改变窗口尺寸、Title、Topmost 等基本属性

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
    - Avalonia XAML 布局调整 + 模式切换
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (with Tasks 7, 8, 9)
  - **Blocks**: None
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` — 现有布局
  - HTML 原型: `docs/prototypes/progress-window-enhancement.html`

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
    Expected Result: 编译通过
    Evidence: .omo/evidence/task-6-build.txt

  Scenario: 模式切换验证
    Tool: Manual
    Steps:
      1. 启动应用，触发压缩/解压操作
      2. 在 ProgressWindow 中切换上方模式（简约→详细→列表）
      3. 在 ProgressWindow 中切换下方密度（少→中→完整）
      4. 验证各模式下显示正确的控件
    Expected Result: 两组模式独立切换，显示对应内容
    Evidence: .omo/evidence/task-6-mode-switch.png

  Scenario: 文件列表性能验证
    Tool: Manual
    Steps:
      1. 选择包含 1000+ 文件的压缩包解压
      2. 切换到列表模式
      3. 滚动列表，观察性能
    Expected Result: 滚动流畅，无卡顿
    Evidence: .omo/evidence/task-6-list-perf.png
  ```

  **Commit**: NO (groups with Wave 2 at the end)

- [ ] 7. **ProgressDisplayCalculator 时间估算 + 并行进度方法**

  **What to do**:
  在 `ProgressDisplayCalculator.cs` 中新增时间估算和并行进度相关方法：

  **7a. 时间估算方法**：
  ```csharp
  /// <summary>
  /// 计算已用时间和预计剩余时间。
  /// </summary>
  public static (TimeSpan elapsed, TimeSpan? estimated) CalculateTimeEstimate(
      DateTime startTime, double overallPercent, long processedBytes, long totalBytes)
  {
      var elapsed = DateTime.UtcNow - startTime;
      
      if (overallPercent <= 0 || overallPercent >= 100)
          return (elapsed, null);
      
      // 基于字节数估算（更准确）
      if (totalBytes > 0 && processedBytes > 0)
      {
          var bytesPerSecond = processedBytes / elapsed.TotalSeconds;
          if (bytesPerSecond > 0)
          {
              var remainingBytes = totalBytes - processedBytes;
              var estimated = TimeSpan.FromSeconds(remainingBytes / bytesPerSecond);
              return (elapsed, estimated);
          }
      }
      
      // 基于百分比估算（回退方案）
      var percentPerSecond = overallPercent / elapsed.TotalSeconds;
      if (percentPerSecond > 0)
      {
          var remainingPercent = 100 - overallPercent;
          var estimated = TimeSpan.FromSeconds(remainingPercent / percentPerSecond);
          return (elapsed, estimated);
      }
      
      return (elapsed, null);
  }

  /// <summary>
  /// 格式化时间显示文本。
  /// </summary>
  public static string FormatTimeDisplay(TimeSpan elapsed, TimeSpan? estimated)
  {
      var elapsedStr = FormatTimeSpan(elapsed);
      
      if (estimated.HasValue)
      {
          var estimatedStr = FormatTimeSpan(estimated.Value);
          return $"已用 {elapsedStr}  |  预计剩余 {estimatedStr}";
      }
      
      return $"已用 {elapsedStr}";
  }

  private static string FormatTimeSpan(TimeSpan ts)
  {
      if (ts.TotalHours >= 1)
          return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
      return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
  }
  ```

  **7b. 并行进度方法**（支持多线程进度上报）：
  ```csharp
  /// <summary>
  /// 线程进度信息（用于详细模式显示）。
  /// </summary>
  public class ThreadProgressInfo
  {
      public int ThreadId { get; set; }
      public string FileName { get; set; } = "";
      public double Progress { get; set; }
      public string PercentText => $"{Progress:F0}%";
  }

  /// <summary>
  /// 文件列表项信息（用于列表模式显示）。
  /// </summary>
  public class FileListItemInfo
  {
      public string StatusIcon { get; set; } = "";
      public string FileName { get; set; } = "";
      public string StatusText { get; set; } = "";
      public Brush? StatusBrush { get; set; }
  }

  /// <summary>
  /// 计算文件列表项的状态信息。
  /// </summary>
  public static FileListItemInfo CalculateFileListItem(
      string fileName, bool isActive, bool isCompleted, bool isFailed, 
      bool isSkipped, double? progress = null)
  {
      var info = new FileListItemInfo { FileName = fileName };
      
      if (isActive)
      {
          info.StatusIcon = "⏳";
          info.StatusText = progress.HasValue ? $"{progress:F0}%" : "进行中";
          info.StatusBrush = Brushes.Yellow;
      }
      else if (isCompleted)
      {
          info.StatusIcon = "✓";
          info.StatusText = "完成";
          info.StatusBrush = Brushes.Green;
      }
      else if (isFailed)
      {
          info.StatusIcon = "✗";
          info.StatusText = "出错";
          info.StatusBrush = Brushes.Red;
      }
      else if (isSkipped)
      {
          info.StatusIcon = "⏭";
          info.StatusText = "跳过";
          info.StatusBrush = Brushes.Gray;
      }
      else
      {
          info.StatusIcon = "○";
          info.StatusText = "等待";
          info.StatusBrush = Brushes.LightGray;
      }
      
      return info;
  }
  ```

  **Must NOT do**:
  - 不要修改现有 ProgressDisplayCalculator 方法的签名
  - 不要引入 Avalonia 依赖（保持 Core 层独立）

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - 纯工具方法新增
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (with Tasks 6, 8, 9)
  - **Blocks**: Task 8 (code-behind needs these methods)
  - **Blocked By**: Task 3 (existing ProgressDisplayCalculator)

  **References**:
  - `src/MantisZip.Core/Utils/ProgressDisplayCalculator.cs` — 新建的工具类

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.Core\MantisZip.Core.csproj
    Expected Result: 编译通过
    Evidence: .omo/evidence/task-7-build.txt

  Scenario: 时间估算验证
    Tool: Unit Test
    Steps:
      1. 编写单元测试验证 CalculateTimeEstimate 方法
      2. 验证不同输入下的输出正确性
    Expected Result: 所有测试通过
    Evidence: .omo/evidence/task-7-test.txt
  ```

  **Commit**: NO (groups with Wave 2 at the end)

- [ ] 8. **ThreadProgressItem/FileInfoItem 模型类（新增）**

  **What to do**:
  在 `Core/Models/` 下新增两个模型类，用于 ProgressWindow 的并行进度显示：

  **8a. ThreadProgressItem.cs**：
  ```csharp
  namespace MantisZip.Core.Models;

  /// <summary>
  /// 单个线程的进度信息（用于 ProgressWindow 详细模式）。
  /// </summary>
  public class ThreadProgressItem
  {
      public int ThreadId { get; set; }
      public string FileName { get; set; } = "";
      public double Progress { get; set; }
      public string PercentText => $"{Progress:F0}%";
  }
  ```

  **8b. FileListItem.cs**：
  ```csharp
  namespace MantisZip.Core.Models;

  /// <summary>
  /// 文件列表项信息（用于 ProgressWindow 列表模式）。
  /// </summary>
  public class FileListItem
  {
      public string StatusIcon { get; set; } = "";
      public string FileName { get; set; } = "";
      public string StatusText { get; set; } = "";
      public string StatusBrushName { get; set; } = "Theme_TextSecondary";
  }
  ```

  **Must NOT do**:
  - 不要引入 Avalonia 依赖（使用字符串表示颜色，UI 层转换）
  - 不要添加复杂的业务逻辑

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - 简单模型类新增
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 6, 7)
  - **Blocks**: Task 9 (code-behind uses these models)
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.Core/Models/` — 现有模型类目录

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.Core\MantisZip.Core.csproj
    Expected Result: 编译通过
    Evidence: .omo/evidence/task-8-build.txt
  ```

  **Commit**: NO (groups with Wave 2 at the end)

- [ ] 9. **ProgressWindow.cs 代码逻辑（SetProgress + 模式切换 + 时间显示）**

  **What to do**:
  修改 `ProgressWindow.xaml.cs`，重构 `SetProgress` 方法，新增模式切换、时间显示、并行进度支持：

  **9a. 新增字段**：
  ```csharp
  // 时间估算相关
  private DateTime _startTime;
  private long _processedBytes;
  private long _totalBytes;
  
  // 并行进度相关
  private readonly ObservableCollection<ThreadProgressItem> _threadProgressItems = new();
  private readonly ObservableCollection<FileListItem> _fileListItems = new();
  
  // 当前显示模式
  private enum DisplayMode { Simple, Detailed, List }
  private DisplayMode _currentMode = DisplayMode.Simple;
  ```

  **9b. `SetProgress` 方法重构**：
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

      // 时间估算
      var (elapsed, estimated) = ProgressDisplayCalculator.CalculateTimeEstimate(
          _startTime, overallPct, _processedBytes, _totalBytes);
      var timeDisplayText = ProgressDisplayCalculator.FormatTimeDisplay(elapsed, estimated);

      // ---- 赋值（WPF 特有，迁移时替换） ----
      DirPathText.Text = dirPath;
      FileNameText.Text = fileName;
      FileProgressCountText.Text = fileCountText;
      StatsBarText.Text = statsText;
      ElapsedTimeText.Text = $"已用 {elapsed:hh\\:mm\\:ss}";
      EstimatedTimeText.Text = estimated.HasValue 
          ? $"预计剩余 {estimated:hh\\:mm\\:ss}" 
          : "";

      // 总进度
      TotalProgressBar.Value = overallPct;
      PercentText.Text = $"{overallPct:F1}%";

      // 文件进度条
      if (p.FilePercentComplete.HasValue)
      {
          FileProgressBar.Value = p.FilePercentComplete.Value;
          FilePercentText.Text = $"{p.FilePercentComplete.Value:F0}%";
      }

      // 更新字节计数（用于时间估算）
      if (p.TotalBytes > 0) _totalBytes = p.TotalBytes;
      if (p.ProcessedBytes > 0) _processedBytes = p.ProcessedBytes;

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

      // 更新模式特定的视图
      UpdateModeSpecificView(p);
  }
  ```

  **9c. 模式切换方法**：
  ```csharp
  // 上方内容模式切换（简约/详细/列表）
  private void OnTopModeChanged(object sender, RoutedEventArgs e)
  {
      if (sender is Button btn && btn.Tag is string tag)
      {
          _currentTopMode = tag switch
          {
              "Simple" => TopDisplayMode.Simple,
              "Detailed" => TopDisplayMode.Detailed,
              "List" => TopDisplayMode.List,
              _ => TopDisplayMode.Simple
          };
          UpdateTopModeVisibility();
      }
  }

  private void UpdateTopModeVisibility()
  {
      // 隐藏所有模式内容
      TopSimpleContent.IsVisible = false;
      TopDetailedContent.IsVisible = false;
      TopListContent.IsVisible = false;
      
      // 显示当前模式内容
      switch (_currentTopMode)
      {
          case TopDisplayMode.Simple:
              TopSimpleContent.IsVisible = true;
              break;
          case TopDisplayMode.Detailed:
              TopDetailedContent.IsVisible = true;
              break;
          case TopDisplayMode.List:
              TopListContent.IsVisible = true;
              break;
      }
  }

  // 下方密度模式切换（少/中/完整 三按钮）
  private void OnDensityChanged(object sender, RoutedEventArgs e)
  {
      if (sender is Button btn && btn.Tag is string tag)
      {
          _currentDensityMode = tag switch
          {
              "Minimal" => DensityMode.Minimal,
              "Medium" => DensityMode.Medium,
              "Full" => DensityMode.Full,
              _ => DensityMode.Medium
          };
          UpdateDensityVisibility();
      }
  }

  private void UpdateDensityVisibility()
  {
      // 全部隐藏
      StatsBar.IsVisible = false;
      MediumStats.IsVisible = false;
      TimeDisplay.IsVisible = false;

      switch (_currentDensityMode)
      {
          case DensityMode.Minimal:
              // 少：进度条 + 时间
              TimeDisplay.IsVisible = true;
              break;
          case DensityMode.Medium:
              // 中：已处理+速度+进程数（单行） + 进度条 + 时间
              MediumStats.IsVisible = true;
              TimeDisplay.IsVisible = true;
              break;
          case DensityMode.Full:
              // 完整：统计栏(五项: 已处理/跳过/出错/已覆盖/进程数) + 进度条 + 时间
              StatsBar.IsVisible = true;
              TimeDisplay.IsVisible = true;
              break;
      }
  }
  ```

  **9d. 并行进度更新方法**：
  ```csharp
  /// <summary>
  /// 更新线程进度（从引擎调用）。
  /// </summary>
  public void UpdateThreadProgress(int threadId, string fileName, double progress)
  {
      void Update()
      {
          var existing = _threadProgressItems.FirstOrDefault(t => t.ThreadId == threadId);
          if (existing != null)
          {
              existing.FileName = fileName;
              existing.Progress = progress;
          }
          else
          {
              _threadProgressItems.Add(new ThreadProgressItem
              {
                  ThreadId = threadId,
                  FileName = fileName,
                  Progress = progress
              });
          }
      }
      DispatchIfNeeded(Update, DispatcherPriority.Background);
  }

  /// <summary>
  /// 更新文件列表（从引擎调用）。
  /// </summary>
  public void UpdateFileList(string fileName, bool isActive, bool isCompleted, 
      bool isFailed, bool isSkipped, double? progress = null)
  {
      void Update()
      {
          var info = ProgressDisplayCalculator.CalculateFileListItem(
              fileName, isActive, isCompleted, isFailed, isSkipped, progress);
          
          _fileListItems.Add(new FileListItem
          {
              StatusIcon = info.StatusIcon,
              FileName = info.FileName,
              StatusText = info.StatusText,
              StatusBrushName = info.StatusBrushName
          });
      }
      DispatchIfNeeded(Update, DispatcherPriority.Background);
  }
  ```

  **Must NOT do**:
  - 不要删除现有 `SetProgress` 的任何功能（进度条、计数、密码区逻辑保持不变）
  - 不要改动 `BackgroundDispatcherProgress` 和 `PauseAwareProgress` 类
  - 不要引入线程安全问题（使用 `DispatchIfNeeded` 更新 UI）

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - 涉及多个方法的重构和新增
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (sequential, after Tasks 6, 7, 8)
  - **Blocks**: Task 10 (App.Extract.cs needs these methods)
  - **Blocked By**: Tasks 6, 7, 8

  **References**:
  - `src/MantisZip.UI/Dialogs/ProgressWindow.xaml.cs` — 现有 `SetProgress` 方法
  - `src/MantisZip.Core/Utils/ProgressDisplayCalculator.cs` — 新建的工具类
  - `src/MantisZip.Core/Models/ThreadProgressItem.cs` — 新建的模型类
  - `src/MantisZip.Core/Models/FileListItem.cs` — 新建的模型类

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected Result: 编译通过
    Evidence: .omo/evidence/task-9-build.txt

  Scenario: 模式切换验证
    Tool: Manual
    Steps:
      1. 启动应用，触发压缩/解压操作
      2. 在 ProgressWindow 中切换模式（简约→详细→列表）
      3. 验证各模式下显示正确的控件
    Expected Result: 三种模式正确切换，显示对应内容
    Evidence: .omo/evidence/task-9-mode-switch.png
  ```

  **Commit**: YES (groups with Task 6)
  - Message: `feat(ui): enhance ProgressWindow with mode switching, time display, and parallel progress support`
  - Files: `ProgressWindow.xaml.cs`

- [ ] 10. **App.Extract.cs 统计更新 + 引擎并行进度上报**

  **What to do**:
  修改 `App.Extract.cs` 和三个引擎，支持并行进度上报和统计更新：

  **10a. `App.Extract.cs` — 批处理完成后更新统计**：
  ```csharp
  // 在批处理循环中，每个压缩包提取完成后
  var extractResult = await engine.ExtractAsync(...);

  // 新增：推送跳过计数到 ProgressWindow 的当前批处理项
  if (progressWindow.IsBatchMode)
  {
      progressWindow.UpdateBatchItemSkipCount(i, extractResult.SkippedEntries);
  }
  ```

  **10b. 引擎并行进度上报**（在 ZipEngine 中添加）：
  ```csharp
  // 在 ZipEngine.ExtractAsync 中，启动并行线程时
  var threadProgressAction = new Action<int, string, double>((threadId, fileName, progress) =>
  {
      progressReporter?.Invoke(threadId, fileName, progress);
  });

  // 在并行循环中调用
  Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, entry =>
  {
      var threadId = Thread.CurrentThread.ManagedThreadId;
      threadProgressAction(threadId, entry.Key, 0); // 开始
      // ... 提取逻辑
      threadProgressAction(threadId, entry.Key, 100); // 完成
  });
  ```

  **10c. ProgressWindow 新增方法**：
  ```csharp
  public void UpdateBatchItemSkipCount(int index, int skippedEntries)
  {
      void Update()
      {
          if (_batchItems == null || index < 0 || index >= _batchItems.Count)
              return;
          _batchItems[index].SkippedFiles = skippedEntries;
      }
      DispatchIfNeeded(Update, DispatcherPriority.Background);
  }
  ```

  **Must NOT do**:
  - 不要改动提取循环的主体逻辑
  - 不要在 `continue` 之外加新的副作用
  - 不要引入线程安全问题

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - 涉及多个文件和引擎修改
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (sequential, after Task 9)
  - **Blocks**: None
  - **Blocked By**: Tasks 5, 9

  **References**:
  - `src/MantisZip.UI/AppPartials/App.Extract.cs` — 批处理循环
  - `src/MantisZip.Core/Engines/ZipEngine.cs` — 并行提取逻辑
  - `src/MantisZip.UI/Dialogs/ProgressWindow.xaml.cs` — 新增方法

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.Core\MantisZip.Core.csproj
      2. dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected Result: 编译通过
    Evidence: .omo/evidence/task-10-build.txt

  Scenario: 运行验证（并行提取场景）
    Tool: Manual
    Steps:
      1. 创建包含多个文件的压缩包
      2. 触发解压操作
      3. 验证 ProgressWindow 显示线程进度
    Expected Result: 详细模式下显示多个线程的进度
    Evidence: .omo/evidence/task-10-parallel.png
  ```

  **Commit**: YES (groups with Task 6)
  - Message: `feat(ui): enhance ProgressWindow with mode switching, time display, and parallel progress support`
  - Files: `App.Extract.cs`, `ZipEngine.cs`

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each task: verify the described changes exist in code. Check: ArchiveProgress has SkippedFiles/FailedFiles? FileConflictHelper calls ConflictActionCallback? Engines count skips? ProgressDisplayCalculator exists? ProgressWindow XAML has new controls? SetProgress uses ProgressDisplayCalculator? Mode switching works? Time display works? Parallel progress display works? Evidence files in .omo/evidence/.
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
  - Mode switching works (简约→详细→列表)
  - Time display shows elapsed and estimated remaining
  - Parallel progress display shows thread progress in detailed mode
  Save evidence to `.omo/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do" + actual diff. Verify 1:1 — everything in spec was built, nothing beyond spec was built. Check "Must NOT do" compliance. Detect cross-task contamination.
  Output: `Tasks [N/N compliant] | VERDICT`

---

## Commit Strategy

| Step | Message | Scope |
|------|---------|-------|
| Wave 1 (after Tasks 1-5) | `feat(core): add skip counting and conflict action callback infrastructure` | ArchiveProgress, ArchiveOptions, ExtractResult, FileConflictHelper, ProgressDisplayCalculator, BatchItem, 3 engines |
| Wave 2 (after Tasks 6-10) | `feat(ui): enhance ProgressWindow with mode switching, time display, and parallel progress support` | ProgressWindow.xaml, ProgressWindow.xaml.cs, App.Extract.cs, ZipEngine.cs, new model classes |

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```

### Final Checklist
- [ ] `ProgressWindow` 显示批处理压缩包列表（固定在上方顶部）
- [ ] 上方区域支持模式切换（简约/详细/列表）
- [ ] 下方区域支持密度切换（少/中/完整）
- [ ] 简约模式：显示当前文件 + 文件进度条
- [ ] 详细模式：显示线程进度列表
- [ ] 列表模式：显示文件列表 + 状态标记（✓⏳○✗⏭）
- [ ] 列表模式使用虚拟化，支持大量文件
- [ ] 少模式：进度条 + 时间
- [ ] 中模式：已处理+速度（单行） + 进度条 + 时间
- [ ] 完整模式：统计栏（已处理/跳过/出错/已覆盖） + 进度条 + 时间
- [ ] `ProgressDisplayCalculator` 无任何 WPF/Avalonia 依赖
- [ ] 所有计算逻辑抽到 Core 层
