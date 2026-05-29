# 批量处理进度窗口文件列表

> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜⬜] (0/9)

## 任务总览

- [ ] **Wave 1: 基础模型** — BatchItem 数据模型 + 本地化 (Task 1-2)
- [ ] **Wave 2: ProgressWindow UI** — 文件列表 XAML + 批处理模式 API + 测试 (Task 3-5)
- [ ] **Wave 3: 批量压缩集成** — 共享压缩核心 + CLI/GUI 两边集成 + 测试 (Task 6)
- [ ] **Wave 4: 批处理解压** — IPC 合并 + ExtractSettingsWindow + HandleExtract* 改造 + 测试 (Task 7-9)

## TL;DR

> **Quick Summary**: 在批量操作（多压缩包压缩/解压）的进度窗口中添加文件列表，每项显示名称 + 状态（待处理/进行中/已完成/失败），实时刷新。
>
> **Deliverables**:
> - ProgressWindow 增加文件列表 UI（固定高度滚动）
> - `--compress-separate` 集成文件列表
> - 现有四个解压 CLI（`--extract-here`/`--smart`/`--to-name`/`--extract`）内部增加 IPC 合并，多文件选择自动进入批处理模式
> - `ExtractSettingsWindow` — 统一替换 `--extract` 的文件夹选择对话框，单文件/多文件都用此窗口
> - TDD 测试覆盖
>
> **Estimated Effort**: Medium
> **Parallel Execution**: YES - 3 waves
> **Critical Path**: Task 1 → Task 2 → Task 3 → Task 6 → Task 7 → Task 8 → Task 9 → F1-F4

---

## Context

### Original Request
用户希望在批量压缩或解压多个文件时，进度窗口中显示文件列表，每个文件附带状态标签。

### Interview Summary
**Key Discussions**:
- **场景**: `--compress-separate` + 右键多选压缩包解压
- **条目**: 名称 + 状态（待处理/进行中/已完成/失败）
- **布局**: 固定高度滚动，窗口约 400×450px
- **完成**: 全部成功自动关，有失败保持打开
- **失败**: 继续下一项，不中断
- **测试**: TDD

### 相关文件
- `src/MantisZip.Core/Models/ProgressBatchItem.cs` — `BatchItem` 数据模型 + `BatchItemStatus` 枚举（新建）
- `src/MantisZip.UI/ProgressWindow.xaml` — 进度窗口 UI
- `src/MantisZip.UI/ProgressWindow.xaml.cs` — 进度窗口逻辑
- `src/MantisZip.UI/Converters/BatchStatusConverters.cs` — `BatchStatusToTextConverter` + `BatchStatusToIconConverter`（新建）
- `src/MantisZip.UI/ExtractSettingsWindow.xaml` — 解压设置窗口 UI（新建）
- `src/MantisZip.UI/ExtractSettingsWindow.xaml.cs` — 解压设置窗口逻辑（新建）
- `src/MantisZip.UI/App.Cli.cs` — `RunCompressSeparateBatch` + `RunExtract` + `HandleExtractBatch` + CLI 入口
- `src/MantisZip.UI/App.PipeServer.cs` — IPC Mutex + NamedPipeServer
- `src/MantisZip.UI/ShellIntegration.cs` — 右键菜单注册
- `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` — `ArchiveProgress` 数据模型 + `ArchiveEngineFactory`
- `src/MantisZip.UI/Localization/L.cs` — 本地化 key 常量
- `src/MantisZip.UI/Resources/strings.zh.json` — 中文翻译
- `src/MantisZip.UI/Resources/strings.en.json` — 英文翻译

---

## Work Objectives

### Core Objective
在批量操作进度窗口中添加状态文件列表，提供清晰的处理进度感知。

### Concrete Deliverables
- ProgressWindow 可切换批处理模式，显示文件列表
- `--compress-separate` 使用新列表
- `CompressSettingsWindow` → "压缩到各自名字" 使用新列表
- 多压缩包解压（现有 CLI 内部 IPC 合并）使用新列表
- `ExtractSettingsWindow` — 统一替换 `--extract` 的文件夹选择对话框，单文件/多文件均弹此窗口
- 右键菜单中选择多项时触发批处理解压模式

### Definition of Done
- [ ] `dotnet build` 0 errors
- [ ] `dotnet test` all tests pass
- [ ] `--compress-separate` 显示文件列表且带正确状态
- [ ] CompressSettingsWindow "压缩到各自名字" 显示文件列表且带正确状态
- [ ] 多压缩包右键解压进入批处理模式并显示列表
- [ ] 全部成功自动关闭，有失败保持打开

### Must Have
- 文件列表中每项显示名称 + 状态
- 状态实时更新（待处理→进行中→已完成/失败）
- 列表固定高度，超出可滚动
- 批处理解压 IPC 机制：多进程合并为单进程单窗口
- `--extract`（单文件或多文件）弹出 `ExtractSettingsWindow`，包含文件列表 + 输出路径模式选择
- `--extract-here`/`--smart`/`--to-name` 多文件 IPC 合并后直接进入 ProgressWindow 批处理模式（不弹设置窗口）

### Must NOT Have (Guardrails)
- 不实现单项重试功能
- 不实现暂停后跳过当前项
- 不实现拖拽排序
- 不修改 `--extract-here` / `--extract-smart` / `--extract-to-name` 的**单文件** CLI 行为
- `--extract` 单文件行为改为弹 `ExtractSettingsWindow`（这是有意变更，统一 UX）
- `--compress`（弹出压缩对话框）和 `--compress-quick` 暂不覆盖批处理列表，留到后续迭代

---

## Verification Strategy

### Test Decision
- **Infrastructure exists**: YES (xUnit)
- **Automated tests**: TDD
- **Framework**: xUnit

### QA Policy
Every task MUST include agent-executed QA scenarios (see TODO template below).
- Backend logic: Bash (dotnet test)
- UI: Playwright (if GUI testing needed)
- Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.{ext}`

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Foundation — all parallel):
├── Task 1: BatchItem 数据模型 + BatchItemStatus 枚举 + 单元测试 (Core)
├── Task 2: 本地化字符串 (L.cs + JSON)

Wave 2 (ProgressWindow UI — depends on T1, T2):
├── Task 3: ProgressWindow.xaml 文件列表 UI (XAML + IValueConverter)
├── Task 4: ProgressWindow.xaml.cs 批处理模式 API
├── Task 5: 单元测试 — ProgressWindow 批处理模式（不阻塞后续任务）

Wave 3 (Integration — depends on T3, T4):
├── Task 6: RunCompressSeparateBatch 集成文件列表 + 测试

Wave 4 (Batch Extract — depends on T6):
├── Task 7: IPC 合并基础设施 + RunExtract 核心方法
├── Task 8: ExtractSettingsWindow XAML + 逻辑（文件列表 + 输出路径模式）
├── Task 9: 改造 HandleExtractHere/Smart/ToNamed/Extract + 测试

Wave FINAL:
├── F1-F4: Verification
```

### Dependency Matrix
```
Task            Depends On          Blocks
───             ──────────          ──────
1 (model)       —                   3, 4, 5
2 (i18n)        —                   3, 4, 5
3 (XAML)        1, 2                6
4 (API)         1, 2                6
5 (tests)       1, 2                —（不阻塞实现）
6 (compress)    3, 4                7
7 (IPC infra)  6                   8, 9
8 (settings Win) 7                  9
9 (handlers)   7, 8                 F1-F4
F1-F4           all                 —
```

---

## TODOs

- [ ] 1. **创建 BatchItem 数据模型 + BatchItemStatus 枚举 + 单元测试**

  **What to do**:
  - 在 `MantisZip.Core` 项目中新建 `Models/ProgressBatchItem.cs` 文件（`INotifyPropertyChanged` 来自 `System.ComponentModel`，不是 WPF 类型，放 Core 无问题）
  - 定义 `BatchItemStatus` 枚举：
    ```csharp
    public enum BatchItemStatus
    {
        Pending,     // 待处理
        InProgress,  // 进行中
        Completed,   // 已完成
        Failed       // 失败
    }
    ```
  - 定义 `BatchItem` 类（实现 `INotifyPropertyChanged`）：
    ```csharp
    public class BatchItem : INotifyPropertyChanged
    {
        public string Name { get; set; }           // 显示名称（文件名）
        public string? FullPath { get; set; }     // 完整路径
        public BatchItemStatus Status { get; set; }
        public string? ErrorMessage { get; set; } // 失败时的错误信息
        // ⚠️ 注意：不在这里添加 StatusText / StatusIcon 的硬编码属性
        // UI 层通过 IValueConverter + L.T() 做本地化显示（见 Task 3 XAML）
    }
    ```
  - `Status` 属性变更时触发 `PropertyChanged`
  - 在 `tests/MantisZip.Tests/` 下新建 `ProgressBatchItemTests.cs`，测试覆盖：
    - 默认状态为 Pending
    - Status 变更触发 PropertyChanged
    - ErrorMessage 为空时不抛出

  **Must NOT do**:
  - ❌ 不添加 StatusText / StatusIcon 硬编码属性（会导致英文 UI 下状态文本仍是中文）
  - 不引用 WPF 特定类型
  - 不测试 UI 交互（只测纯逻辑）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1)
  - **Blocks**: Tasks 3, 4, 5
  - **Blocked By**: None

  **References**:
  - `MantisZip.Core/Abstractions/ArchiveEngine.cs:1-18` — 类似的数据模型（`ArchiveItem`）
  - `MantisZip.Core/Models/` — Core 项目已有 Models 目录（如不存在则新建）

  **Acceptance Criteria**:
  - [ ] TDD: 先写失败测试 → 实现 → 全部通过
  - [ ] `dotnet build` 0 errors（测试项目引用 `MantisZip.Core`，无需改 .csproj）
  - [ ] `dotnet test` passes

  **QA Scenarios**:
  ```
  Scenario: BatchItem 初始状态为 Pending
    Tool: Bash (dotnet test)
    Steps:
      1. 创建 BatchItem 实例，不设置 Status
      2. 验证 Status == BatchItemStatus.Pending
    Expected Result: 默认状态正确
    Evidence: .sisyphus/evidence/task-1-default-status.txt

  Scenario: BatchItem 状态变更触发 PropertyChanged
    Tool: Bash (dotnet test)
    Steps:
      1. 创建 BatchItem 实例，订阅 PropertyChanged
      2. 设置 Status = BatchItemStatus.Completed
      3. 验证 PropertyChanged 被触发
    Expected Result: 属性变化通知正常工作
    Evidence: .sisyphus/evidence/task-1-property-changed.txt
  ```

  **Commit**: YES（实现 + 测试同一次提交）
  - Message: `feat(core): add BatchItem model and unit tests`
  - Files: `src/MantisZip.Core/Models/ProgressBatchItem.cs`, `tests/MantisZip.Tests/ProgressBatchItemTests.cs`

---

- [ ] 2. **添加本地化字符串**

  **What to do**:
  - 在 `L.cs` 中添加以下常量：
    - `Batch_Header = "Batch_Header"` — "文件列表 ({0}/{1})"
    - `Batch_Pending = "Batch_Pending"` — "待处理"
    - `Batch_InProgress = "Batch_InProgress"` — "进行中..."
    - `Batch_Completed = "Batch_Completed"` — "已完成"
    - `Batch_Failed = "Batch_Failed"` — "失败"
    - `Batch_CompleteWithErrors = "Batch_CompleteWithErrors"` — "完成 {0} 个，失败 {1} 个"
  - 在 `strings.zh.json` 中添加中文翻译
  - 在 `strings.en.json` 中添加英文翻译

  **Must NOT do**:
  - 不修改现有 key

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1)
  - **Blocks**: Tasks 3, 4, 5（XAML 的 converter 需要 L.T() 本地化 key）
  - **Blocked By**: None

  **References**:
  - `MantisZip.UI/Localization/L.cs` — 现有 key 常量
  - `MantisZip.UI/Resources/strings.zh.json` — 中文翻译文件
  - `MantisZip.UI/Resources/strings.en.json` — 英文翻译文件

  **Acceptance Criteria**:
  - [ ] 6 个新 key 都在 L.cs 中（按字母顺序插入到现有 Batch_ 或 Progress_ 分组）
  - [ ] 6 个 key 在 strings.zh.json 和 strings.en.json 中都有翻译
  - [ ] `L.T(L.Batch_Pending)` 返回正确的翻译文本

  **QA Scenarios**:
  ```
  Scenario: 新本地化字符串可正常加载
    Tool: Bash (dotnet test)
    Steps:
      1. 调用 L.T(L.Batch_Header) — 带参数测试
      2. 调用 L.T(L.Batch_Pending)
      3. 调用 L.T(L.Batch_InProgress)
      4. 调用 L.T(L.Batch_Completed)
      5. 调用 L.T(L.Batch_Failed)
    Expected Result: 每个 key 返回非空字符串（非 "[KEY]" 标记）
    Evidence: .sisyphus/evidence/task-2-localization.txt
  ```

  **Commit**: YES
  - Message: `feat(ui): add batch progress localization strings`
  - Files: `src/MantisZip.UI/Localization/L.cs`, `src/MantisZip.UI/Resources/strings.zh.json`, `src/MantisZip.UI/Resources/strings.en.json`

---

- [ ] 3. **ProgressWindow.xaml 文件列表 UI**

  **What to do**:
  - 在 `ProgressWindow.xaml` 的现有 Grid 中添加行：
    - 在文件计数行（当前 Row 4）和底部按钮行（Row 6）之间插入新行 Row 5
    - 添加文件列表区域：
      ```xml
      <!-- Row 5: 批处理文件列表 -->
      <Border Grid.Row="5" ... Visibility="Collapsed" x:Name="BatchListSection">
          <Grid>
              <Grid.RowDefinitions>
                  <RowDefinition Height="Auto"/>
                  <RowDefinition Height="*"/>
              </Grid.RowDefinitions>
              <!-- 标题栏：文件列表 (已完成 3/8) -->
              <TextBlock x:Name="BatchListHeader" Grid.Row="0" .../>
              <!-- 列表 -->
              <ListBox x:Name="BatchItemList" Grid.Row="1" ...>
                  <ListBox.ItemTemplate>
                      <DataTemplate>
                          <Grid>
                              <TextBlock Text="{Binding Status, Converter={StaticResource BatchStatusToIconConverter}}" />
                              <TextBlock Text="{Binding Name}" />
                              <TextBlock Text="{Binding Status, Converter={StaticResource BatchStatusToTextConverter}}" />
                          </Grid>
                      </DataTemplate>
                  </ListBox.ItemTemplate>
              </ListBox>
          </Grid>
      </Border>
      ```
    - 添加两个 `IValueConverter` 资源（可在 XAML Window.Resources 或 App.xaml 中）：
      - `BatchStatusToTextConverter`：`BatchItemStatus` → `L.T()` 本地化文本（Pending→"待处理"/"Pending"）
      - `BatchStatusToIconConverter`：`BatchItemStatus` → emoji（Pending→"⬜"、InProgress→"⏳"、Completed→"✅"、Failed→"❌"）
    - 注意：`Status` 属性（枚举本身）变更时，转换器会自动重新求值
  - 列表中每行显示：状态图标 + 名称（左对齐）+ 状态文本（右对齐）
  - 列表固定高度（约 180px = 6 行 + 标题）
  - 窗口高度策略：
    - **移除** `SizeToContent="Height"`（当前 XAML 使用此属性，但批处理模式需手动控制高度）
    - **非批处理模式**：`MinHeight="175"`，窗口由内容自然撑起
    - **批处理模式**：`InitBatchMode()` 中设置 `Height = 450`，`MinHeight = 250`
    - 普通模式下窗口尺寸不变，批处理模式固定高度 450px

  **Must NOT do**:
  - 不修改现有控件（PasswordSection, ProgressBar, buttons 等）
  - 不破坏现有非批处理模式的显示
  - ❌ 不在 `BatchItem` 数据模型中添加 `StatusText`/`StatusIcon` 硬编码属性

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Task 1, 2)
  - **Parallel Group**: Wave 2
  - **Blocks**: Tasks 6
  - **Blocked By**: Tasks 1, 2

  **References**:
  - `MantisZip.UI/ProgressWindow.xaml` — 当前完整的 XAML（`SizeToContent="Height"` 行为）
  - `MantisZip.UI/ProgressWindow.xaml.cs:85` — `SetProgress` 方法入口
  - `MantisZip.Core/Models/ProgressBatchItem.cs` — `BatchItem` 数据模型（只有 Status 枚举，无 StatusText）
  - `MantisZip.UI/Localization/L.cs` — 使用 `L.T(L.Batch_Pending)` 等做本地化

  **Acceptance Criteria**:
  - [ ] 列表区域默认隐藏（非批处理模式）
  - [ ] `BatchListSection.Visibility = Visible` 时显示列表
  - [ ] `BatchItemList` 绑定到 `BatchItem` 集合
  - [ ] 每一项显示状态 emoji + 名称 + 本地化状态文本
  - [ ] BatchStatusToTextConverter 使用 `L.T()` 获取翻译，中文/英文切换测试通过
  - [ ] BatchStatusToIconConverter 返回对应 emoji

  **QA Scenarios**:
  ```
  Scenario: 非批处理模式列表隐藏
    Tool: Playwright
    Steps:
      1. 创建 ProgressWindow 实例（非批处理模式）
      2. 验证 BatchListSection.Visibility == Collapsed
    Expected Result: 列表不显示
    Evidence: .sisyphus/evidence/task-3-hidden.png

  Scenario: 批处理模式列表显示
    Tool: Playwright
    Steps:
      1. 创建 ProgressWindow 实例
      2. 调用 InitBatchMode(["a.zip", "b.zip"])
      3. 验证 BatchListSection.Visibility == Visible
      4. 验证列表中有 2 项
    Expected Result: 列表正确显示
    Evidence: .sisyphus/evidence/task-3-visible.png
  ```

  **Commit**: YES
  - Message: `feat(ui): add file list UI to ProgressWindow`
  - Files: `src/MantisZip.UI/ProgressWindow.xaml`

---

- [ ] 4. **ProgressWindow.xaml.cs 批处理模式 API**

  **What to do**:
  - 新增 `ObservableCollection<BatchItem> _batchItems` 属性
  - 新增方法 `InitBatchMode(IReadOnlyList<string> items)`：
    - 设置 `IsBatchMode = true`
    - 创建 `_batchItems` 集合，每项初始化为 Pending 状态
    - 设置 `BatchItemList.ItemsSource`
    - 显示 `BatchListSection`
    - **设置窗口高度**：`Height = 450`，`MinHeight = 250`（对应 Task 3 高度策略）
  - 新增方法 `UpdateBatchItemStatus(int index, BatchItemStatus status, string? error = null)`：
    - 更新 `_batchItems[index]` 的 Status 和 ErrorMessage
  - 新增方法 `SetCurrentBatchItem(int index)`：
    - 更新 `_batchItems[index]` 到 InProgress
    - 更新列表滚动到当前项
  - 在 `SetComplete` 中更新 BatchListHeader 统计
  - 如果存在失败项，`SetComplete` 不自动关闭窗口
  - 新增属性 `HasFailures`（检查是否有 Failed 项目）
  - **注意**：不需要修改 `InitCancellation`——批处理模式和单文件模式共享同一个 CancellationTokenSource
  - **注意**：不处理 StatusText/StatusIcon 字符串派生——这些在 XAML 通过 `IValueConverter` 处理

  **Must NOT do**:
  - 不修改现有非批处理 API 的行为
  - 不添加业务逻辑（仅 UI 管理）
  - ❌ 不在 code-behind 中计算或设置 StatusText/StatusIcon 字符串

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Task 3, Wave 2)
  - **Blocks**: Tasks 6
  - **Blocked By**: Tasks 1, 2

  **References**:
  - `MantisZip.UI/ProgressWindow.xaml.cs:85` — `SetProgress` 模式
  - `MantisZip.UI/ProgressWindow.xaml.cs:128` — `SetComplete` 逻辑
  - `MantisZip.UI/ProgressWindow.xaml.cs:282` — `InitCancellation`（不需修改）
  - `MantisZip.UI/ProgressWindow.xaml` — 新增的 BatchListSection 和 BatchItemList
  - `MantisZip.Core/Models/ProgressBatchItem.cs` — `BatchItem` 数据模型

  **Acceptance Criteria**:
  - [ ] InitBatchMode 正确初始化列表，窗口高度设为 450
  - [ ] UpdateBatchItemStatus 正确更新状态
  - [ ] SetCurrentBatchItem 将指定项设为 InProgress
  - [ ] SetComplete 在无失败项时自动关闭；有失败项时保持打开
  - [ ] HasFailures 属性正确反映状态

  **QA Scenarios**:
  ```
  Scenario: InitBatchMode 初始化列表
    Tool: Playwright
    Steps:
      1. 创建 ProgressWindow
      2. InitBatchMode(["a.zip", "b.zip", "c.zip"])
      3. 验证列表显示 3 项，所有状态为 Pending
      4. 验证窗口 Height == 450
    Expected Result: 正确初始化
    Evidence: .sisyphus/evidence/task-4-init.png

  Scenario: 更新单项状态
    Tool: Playwright
    Steps:
      1. InitBatchMode(["a.zip", "b.zip"])
      2. SetCurrentBatchItem(0)
      3. UpdateBatchItemStatus(0, Completed)
      4. 验证列表项状态正确
    Expected Result: 状态正确更新
    Evidence: .sisyphus/evidence/task-4-update.png
  ```

  **Commit**: YES (groups with 3)
  - Message: `feat(ui): add batch mode API to ProgressWindow`
  - Files: `src/MantisZip.UI/ProgressWindow.xaml.cs`

---

- [ ] 5. **单元测试 — ProgressWindow 批处理模式**

  **What to do**:
  - 在 tests 项目中添加 `ProgressWindowBatchTests.cs`
  - 测试可纯逻辑部分：
    - BatchItem 状态枚举默认值
    - HasFailures 判断逻辑
    - CompleteWithErrors 统计逻辑
    - **不测试 StatusText/StatusIcon**（这些在 UI 层通过 converter 处理）
  - 注意：WPF UI 控件需在 STA 线程测试（使用 `[STAThread]` 或 `Apartment(ApartmentState.STA)`）

  **Must NOT do**:
  - 不测试 XAML 渲染（超出单元测试范围）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 2)
  - **Blocks**: None（⚠️ 不阻塞 Task 6——测试是验证手段，不应阻塞实现）
  - **Blocked By**: Tasks 1, 2

  **References**:
  - `tests/MantisZip.Tests/` — 现有测试结构

  **Acceptance Criteria**:
  - [ ] `dotnet test` — 所有测试通过

  **QA Scenarios**:
  ```
  Scenario: 全部测试通过
    Tool: Bash (dotnet test)
    Steps:
      1. dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
    Expected Result: 所有测试通过
    Evidence: .sisyphus/evidence/task-5-test-results.txt
  ```

  **Commit**: YES
  - Message: `test(ui): add ProgressWindow batch mode tests`
  - Files: `tests/MantisZip.Tests/ProgressWindowBatchTests.cs`

---

- [ ] 6. **批量压缩（CLI + 窗口）集成文件列表 + 单元测试**

  **设计原则**：
  - GUI（`CompressSettingsWindow`）和 CLI（`--compress-separate`）的 per-item 压缩循环都使用批处理文件列表
  - 提取共享的 per-item 循环框架 `RunSeparateCompressCore`，两边调用同一个方法
  - 参数获取（GUI 从控件/CLI 从 AppSettings）和冲突处理仍在各自层做，不混入共享逻辑

  **What to do**:

  1. **提取 `RunSeparateCompressCore(List<string> paths, CompressOptions options, ...)`**：
     共享方法（放在 `App.Cli.cs` 中）：
     - 创建 ProgressWindow，`InitBatchMode(paths)`
     - `ShutdownMode = OnExplicitShutdown`
     - 循环处理每项：
       - `SetCurrentBatchItem(i)`, `UpdateBatchItemStatus(i, InProgress)`
       - 确定输出路径（所在目录 + 文件名 + 扩展名）
       - 调用 `engine.CompressAsync(...)`
       - 成功 → `Completed`，失败 → `Failed` + 日志，继续下一项
     - `SetComplete(...)`，有失败保持打开，全成功自动关
     - 方法签名接受以下差异点作为参数（或委托）：
       - 压缩选项（`CompressOptions`）
       - 输出路径生成函数（CLI/GUI 可能不同：`KeepOriginalExtension` vs 直接 `Path.GetFileNameWithoutExtension`）
       - 冲突处理回调（CLI 和 GUI 冲突对话框不同但逻辑类似）

  2. **改造 `RunCompressSeparateBatch`（`App.Cli.cs:123`）**：
     - 读取 `AppSettings` 构造 `CompressOptions`
     - 调用 `RunSeparateCompressCore`，传入 CLI 版本的选项和冲突处理

  3. **改造 `RunSeparateCompressAsync`（`CompressSettingsWindow.xaml.cs:458`）**：
     - 从 UI 控件读取格式/级别/密码/注释等构造 `CompressOptions`
     - 调用 `RunSeparateCompressCore`，传入 GUI 版本的选项和冲突处理
     - 注释分配逻辑（`AllSame`/`FirstOnly`/`PerLine`）仍留在 GUI 层，每轮循环前设置 `options.Comment`

  4. **单元测试**（`tests/MantisZip.Tests/`）：
     - 由于 `RunSeparateCompressCore` 在 UI 项目，tests 只引用 Core
     - 测试覆盖：输出路径生成逻辑、状态转换逻辑（通过将纯逻辑提取到 Core 可测方法）
     - 集成测试通过 `dotnet test` 验证

  **Must NOT do**:
  - 不修改压缩引擎本身
  - 不改变 IPC 收集路径机制
  - 不合并 GUI 的注释分配逻辑到 CLI（仅在 GUI 层保留）

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 3, 4)
  - **Parallel Group**: Wave 3
  - **Blocks**: Tasks 7
  - **Blocked By**: Tasks 3, 4（⚠️ 只依赖实现，不依赖 Task 5 测试）

  **References**:
  - `MantisZip.UI/App.Cli.cs:123` — `RunCompressSeparateBatch`（CLI 版压缩循环）
  - `MantisZip.UI/CompressSettingsWindow.xaml.cs:458` — `RunSeparateCompressAsync`（GUI 版压缩循环）
  - `MantisZip.UI/ProgressWindow.xaml.cs` — 新增的批处理 API

  **Acceptance Criteria**:
  - [ ] `--compress-separate` 运行时 ProgressWindow 显示文件列表
  - [ ] 窗口选"压缩到各自名字"运行时也显示文件列表
  - [ ] 每项状态从"待处理"→"进行中"→"已完成"/"失败"实时变化
  - [ ] 全部成功时自动关闭
  - [ ] 有失败时保持打开
  - [ ] 注释分配（GUI）和 KeepOriginalExtension（CLI）各自正常工作
  - [ ] `dotnet test` — 所有测试通过

  **QA Scenarios**:
  ```
  Scenario: --compress-separate 全部成功
    Tool: Bash (direct invoke)
    Steps:
      1. 创建 3 个临时文件
      2. 运行 --compress-separate 传入这 3 个文件
      3. 验证 ProgressWindow 文件列表：3 项全部变为"已完成"
    Expected Result: CLI 批量压缩成功
    Evidence: .sisyphus/evidence/task-6-cli-all-success.txt

  Scenario: CompressSettingsWindow 选"压缩到各自名字"
    Tool: Playwright
    Steps:
      1. 创建 2 个文件
      2. 通过 --compress 打开窗口，选"压缩到各自名字"
      3. 点击压缩，验证 ProgressWindow 显示 2 项文件列表
    Expected Result: GUI 批量压缩也显示列表
    Evidence: .sisyphus/evidence/task-6-gui-batch.png

  Scenario: 部分失败
    Tool: Bash (direct invoke)
    Steps:
      1. 模拟一项写入失败
      2. 运行 --compress-separate
      3. 失败项显示"失败"，其余"已完成"，窗口保持打开
    Expected Result: 失败项正确标记
    Evidence: .sisyphus/evidence/task-6-some-failed.txt
  ```

  **Commit**: YES（实现 + 测试同一次提交）
  - Message: `feat(ui): integrate batch file list with CLI and GUI compress paths`
  - Files: `src/MantisZip.UI/App.Cli.cs`, `src/MantisZip.UI/CompressSettingsWindow.xaml.cs`, `tests/MantisZip.Tests/CompressBatchTests.cs`

---

- [ ] 7. **IPC 合并基础设施 + RunExtract 核心方法**

   **设计原则**：
  - 不新增 CLI 入口，现有 `--extract-here/smart/to-name/extract` 内部增加 IPC 合并
  - 复用现有 `StartPipeServer` / `SendPathsThroughPipe`，**管道协议不改动**
  - 原因：第一个实例已从自己的 CLI 参数（`args[1]`）知道 mode，后续实例只需发路径
  - 新增 Mutex+Pipe 名称：`MantisZip-ExtractMutex` / `MantisZip-ExtractPipe`
  - `RunExtract` 作为批处理循环核心，供所有 HandleExtract* 调用

  **What to do**:

  1. **App.PipeServer.cs — 新增 Mutex+Pipe 常量**：
     ```csharp
     private static string ExtractMutexName = "MantisZip-ExtractMutex";
     private static string ExtractPipeName = "MantisZip-ExtractPipe";
     private static readonly ManualResetEventSlim _extractPipeReady = new(false);
     ```
     不需要改 `SendPathsThroughPipe` 或 `StartPipeServer` 的方法体。

  2. **App.Cli.cs — `RunExtract` 核心方法**：
      ```csharp
      private async Task RunExtract(List<string> paths, ExtractOutputMode mode, string? customDest = null)
      ```
      流程：
      - 创建 ProgressWindow，`InitBatchMode(paths)`
      - `Current.ShutdownMode = ShutdownMode.OnExplicitShutdown`

      **密码策略（方案 C）**：
      - 先遍历所有压缩包，收集需要加密但无已保存密码的路径，存入 `List<string> needPwdItems`
      - 如果 `needPwdItems` 不为空，弹出一次密码输入框（`PromptForPassword`），得到的密码用于所有需要密码的项目
      - 循环处理每个压缩包：
        - `SetCurrentBatchItem(i)`, `UpdateBatchItemStatus(i, InProgress)`
        - 获取引擎：`ArchiveEngineFactory.GetEngineByExtension(path)`
        - 按优先级确认密码：
          1. 非加密 → 直接解压，传 `null`
          2. `TryMatchPassword` 匹配到了 → 用匹配到的密码
          3. 在 `needPwdItems` 中 → 用第一步输入框得到的密码
          4. 以上都不是 → 尝试无密码解压（部分压缩包部分文件加密但可列出）
        - `QuickVerifyPassword` 验证无误后执行解压
        - **根据 mode 确定目标路径**：
          - `Here`：`Path.GetDirectoryName(path)`
          - `ToName`：`Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path))`
          - `Smart`：调用 `ArchiveStructureAnalyzer`
          - `Manual`：所有压缩包解压到 `customDest`
        - 引擎执行解压
        - 成功 → `Completed`，失败 → `Failed` + 日志，继续下一项
      - `SetComplete(...)`，有失败保持打开，全成功自动关

      **需要提取的公共方法**（`RunExtractStatic` 中的密码逻辑）：
      - `ExtractSingleArchive(archivePath, dest, password, progress, ct)` — 在 ProgressWindow 下解压单个压缩包的循环（含完成处理、文件夹打开、自动关闭）
      - `ResolvePasswordForArchive(archivePath, engine, batchPassword)` — 返回 `(password, isFromBatch)`，按策略 C 的优先级判断

  3. **ExtractOutputMode 枚举**（放 `App.Cli.cs` 或 `MantisZip.Core/Models/`）：
     ```csharp
     public enum ExtractOutputMode { Here, Smart, ToName, Manual }
     ```

  **Must NOT do**:
  - 不新增 CLI 入口
  - 不修改现有 `--compress-*` IPC 行为
  - 不改 HandleExtract*（Task 9 做）

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 6)
  - **Parallel Group**: Wave 4
  - **Blocks**: Tasks 8, 9
  - **Blocked By**: Tasks 6

  **References**:
  - `MantisZip.UI/App.Cli.cs:80` — `HandleCompressSeparate` IPC 模式
  - `MantisZip.UI/App.Cli.cs:123` — `RunCompressSeparateBatch`（批量循环参考）
  - `MantisZip.UI/App.Cli.cs:617` — `RunExtractStatic`（密码流程参考）
  - `MantisZip.UI/App.PipeServer.cs:89` — `StartPipeServer`
  - `MantisZip.UI/App.PipeServer.cs:130` — `SendPathsThroughPipe`

  **Acceptance Criteria**:
  - [ ] 新增 Mutex+Pipe 常量，Mutex 竞争逻辑正确
  - [ ] `RunExtract` 执行批处理循环，状态正确更新
  - [ ] 4 种模式路径解析正确
  - [ ] 密码策略 C 正确：先自动匹配，未匹配的集中弹一次输入框
  - [ ] 有失败项保持窗口打开
  - [ ] 现有 `--compress-separate/combined` 的 IPC 不受影响（管道协议未改）

  **QA Scenarios**:
  ```
  Scenario: RunExtract Here 模式
    Tool: Bash (direct invoke)
    Steps:
      1. 2 个在不同目录的 ZIP
      2. RunExtract([a.zip, b.zip], Here)
      3. 各解压到各自所在目录，列表全 Completed
    Expected Result: 路径解析正确
    Evidence: .sisyphus/evidence/task-7-extract.txt

  Scenario: 混合成功/失败
    Tool: Bash (direct invoke)
    Steps:
      1. 正常 ZIP + 损坏 ZIP
      2. good→Completed, bad→Failed, 窗口保持打开
    Expected Result: 失败标记正确
    Evidence: .sisyphus/evidence/task-7-mixed.txt
  ```

  **Commit**: YES
  - Message: `feat(ui): add IPC merge infra and RunExtract method`
  - Files: `src/MantisZip.UI/App.PipeServer.cs`, `src/MantisZip.UI/App.Cli.cs`, `src/MantisZip.UI/App.xaml.cs`

---

- [ ] 8. **创建 ExtractSettingsWindow 解压设置窗口**

  **设计目的**：
  - 统一替换 `--extract`（解压到……）当前的简单文件夹选择对话框
  - 类似 `CompressSettingsWindow` 的布局，让用户在一次窗口中完成文件选择 + 输出路径选择
  - 不论 1 个文件还是多个文件（IPC 合并后），都用此窗口
  - `--extract-here` / `--smart` / `--to-name` 不弹此窗口，直接自动解压

  **What to do**:

  1. **创建 `ExtractSettingsWindow.xaml`**（参考 `CompressSettingsWindow.xaml` 布局）：
     ```
     Window (400×500, SizeToContent="Height", CenterOwner)
     ├── 标题: "解压设置"
      ├── 文件列表区（上半部分）
      │   ├── 文件计数: "已选择 {n} 个压缩包"
      │   ├── [添加] 按钮 — 打开文件选择对话框，添加更多压缩包
      │   ├── ListBox (固定高度可滚动)
      │   │   └── ItemTemplate: 文件名 + [移除] 按钮
      │   └── 每项可移除（ObservableCollection），移除到 0 项时[解压]禁用
     ├── 分隔线
     ├── 输出路径区（下半部分）
     │   ├── RadioButton 组:
     │   │   ├── "手动输入" — TextBox + Browse 按钮
     │   │   ├── "智能解压" — 自动判断
     │   │   ├── "解压到此处" — 各压缩包所在目录
     │   │   └── "解压到压缩包名" — 所在目录/压缩包名/
     │   ├── 手动模式下: TextBox + 浏览按钮 (VistaFolderBrowserDialog)
     │   └── 其他模式: 显示自动路径预览（只读 TextBlock）
     └── 底部按钮行
         ├── [取消] — 关闭窗口，不执行
         └── [解压] — 关闭窗口，返回配置给调用方
     ```

  2. **`ExtractSettingsWindow.xaml.cs`**：
     - 参考 `CompressSettingsWindow` 的代码模式（已有 Add/Remove 逻辑）
     - 属性：
       - `public List<string> SelectedPaths { get; }` — 最终保留的文件列表（添加/移除操作后）
       - `public ExtractOutputMode OutputMode { get; }` — 选择的输出模式
       - `public string? CustomDestination { get; }` — 手动输入模式下用户选的路径
     - 构造函数接收 `IReadOnlyList<string> allPaths`，填充到 `ObservableCollection` 中
     - **[添加]按钮**：打开文件选择对话框（`OpenFileDialog`，过滤器设置为 ArchiveEngineFactory 支持的所有格式），选中文件添加到列表末尾
     - **[移除]按钮**：从 ObservableCollection 中删除对应项
     - Radio 切换时更新 UI 状态（手动模式下启用 TextBox + Browse）
     - 若用户移除所有项则[解压]按钮禁用（允许空列表，但不允许执行）
     - 关闭时通过 `DialogResult` 判断用户是否确认

  3. **本地化**（`ExtractOutputMode` 枚举在 Task 7 中定义）：
     - `ExtractSettings_Title` — "解压设置"
     - `ExtractSettings_FileCount` — "已选择 {0} 个压缩包"
     - `ExtractSettings_Add` — "添加"
     - `ExtractSettings_AddFilter` — "压缩包文件|*.zip;*.7z;*.rar;*.tar;*.gz;*.tgz|所有文件|*.*"（硬编码，与 `ShellIntegration.ArchiveExtensions` 保持一致）
     - `ExtractSettings_Remove` — "移除"
     - `ExtractSettings_Mode_Manual` — "手动输入"
     - `ExtractSettings_Mode_Smart` — "智能解压"
     - `ExtractSettings_Mode_Here` — "解压到此处"
     - `ExtractSettings_Mode_ToName` — "解压到压缩包名"
     - `ExtractSettings_Browse` — "浏览…"
     - `ExtractSettings_Extract` — "解压"
     - `ExtractSettings_Cancel` — "取消"
     - `ExtractSettings_EmptyList` — "没有待解压的文件"
     - `ExtractSettings_ManualPathPlaceholder` — "选择或输入解压目标目录"

  **Must NOT do**:
  - 不在此窗口中执行解压（只收集配置）
  - 不修改现有的四个 CLI 命令

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 7)
  - **Parallel Group**: Wave 4
  - **Blocks**: Tasks 9
  - **Blocked By**: Tasks 7

  **References**:
  - `MantisZip.UI/CompressSettingsWindow.xaml` — 布局参考（TabControl + 文件列表 + 输出路径）
  - `MantisZip.UI/CompressSettingsWindow.xaml.cs` — 代码模式参考（Radio + TextBox 状态联动）
  - `MantisZip.UI/App.Cli.cs:489` — `HandleExtractHere`（解压到此处行为参考）
  - `MantisZip.UI/App.Cli.cs:508` — `HandleExtractToNamed`（解压到压缩包名行为参考）
  - `MantisZip.Core/Utils/ArchiveStructureAnalyzer.cs` — 智能解压逻辑

  **Acceptance Criteria**:
  - [ ] 窗口显示文件列表，每项可移除
  - [ ] [添加]按钮打开文件选择对话框，选中文件加入列表
  - [ ] 4 种输出模式 Radio 切换正确
  - [ ] 手动模式下 TextBox + Browse 可用，其他模式仅预览
  - [ ] 移除所有项后[解压]按钮禁用
  - [ ] 选择手动 + Browse 选目录后，路径填入 TextBox
  - [ ] 点击[解压]返回选中的文件和模式配置
  - [ ] 点击[取消]关闭不执行

  **QA Scenarios**:
  ```
  Scenario: ExtractSettingsWindow 正常启动
    Tool: Playwright
    Steps:
      1. 创建 ExtractSettingsWindow(["a.zip", "b.zip", "c.zip"])
      2. 验证显示 3 项文件列表
      3. 验证默认选中"解压到此处"
      4. 验证[解压]按钮可用
    Expected Result: 窗口正确初始化
    Evidence: .sisyphus/evidence/task-8-init.png

  Scenario: 手动输入路径
    Tool: Playwright
    Steps:
      1. 选中"手动输入" Radio
      2. TextBox 和 Browse 按钮变为可用
      3. 点击 Browse，选择一个目录
      4. 验证 TextBox 显示所选路径
    Expected Result: 手动模式工作正常
    Evidence: .sisyphus/evidence/task-8-manual.png

  Scenario: 移除全部文件
    Tool: Playwright
    Steps:
      1. 窗口显示 2 项
      2. 分别点击两个[移除]按钮
      3. 验证列表为空，[解压]按钮禁用
      4. 验证显示"没有待解压的文件"
    Expected Result: 空列表时按钮禁用
    Evidence: .sisyphus/evidence/task-8-empty.png
  ```

  **Commit**: YES
  - Message: `feat(ui): add ExtractSettingsWindow for batch extract config`
  - Files: `src/MantisZip.UI/ExtractSettingsWindow.xaml`, `src/MantisZip.UI/ExtractSettingsWindow.xaml.cs`

---

- [ ] 9. **改造 HandleExtractHere/Smart/ToNamed/Extract 支持 IPC 合并 + 批处理**

   **设计原则**：
  - Shell 注册动词完全不变（无需修改 ShellIntegration.cs）
  - 每个 `HandleExtract*` 方法内部增加 IPC 合并逻辑（参考 `HandleCompressSeparate`）
  - 当前四个 Handler 均接收 `string?`（单路径），需改为内部 IPC 流程
  - 提取公共 `TryIpMergeExtract` 方法避免 4 次重复 IPC 代码
  - `--extract`：单文件跳过 IPC，直接弹 `ExtractSettingsWindow`；多文件 IPC 合并后弹窗口
  - `--extract-here / --smart / --to-name`：单文件行为不变，多文件 IPC 合并后直接调用 `RunExtract`

  **What to do**:

  1. **提取公共方法 `TryExtractIpcMerge`**（在 `App.Cli.cs` 中）：
     签名：
     ```csharp
     // 尝试 IPC 合并。返回 null 表示后续实例（已退出），
     // 返回列表表示合并后的所有路径（可能只有 1 项）
     private static List<string>? TryExtractIpcMerge(string myPath)
     ```
     流程（参考 `HandleCompressSeparate` 80-122 行）：
     - 设置 `Current.ShutdownMode = OnExplicitShutdown`
     - `File.Exists(myPath)` 验证
     - 尝试获取 Mutex `MantisZip-ExtractMutex`
     - 第一个实例：
       - `allPaths = new List<string> { myPath }`
       - `_extractPipeReady.Reset()`
       - `StartPipeServer(allPaths, cts.Token, ExtractPipeName, _extractPipeReady)`
       - `_extractPipeReady.Wait(3000)`
       - DispatcherTimer 800ms 后：返回 `allPaths`
     - 后续实例：
       - `SendPathsThroughPipe([myPath], ExtractPipeName)`
       - `Current.Shutdown()`
       - 返回 `null`
     - **与 `HandleCompressSeparate` 共享 `StartPipeServer` / `SendPathsThroughPipe`**，不需要改 PipeServer.cs

  2. **改造 `HandleExtractHere`（`App.Cli.cs:489`）**：
     ```csharp
     private static void HandleExtractHere(string? archivePath)
     {
         if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) { ...; Current.Shutdown(); return; }
         var allPaths = TryExtractIpcMerge(archivePath);
         if (allPaths == null) return; // 后续实例已退出
         if (allPaths.Count == 1) // 单文件，走原有逻辑
         {
             var dest = Path.GetDirectoryName(allPaths[0]) ?? Desktop;
             RunExtractStatic(allPaths[0], dest);
         }
         else // 多文件，走批处理
         {
             RunExtract(allPaths, ExtractOutputMode.Here);
         }
     }
     ```

  3. **改造 `HandleExtractSmart`（`App.Cli.cs:533`）**：
     - 同上 IPC 合并
     - 单文件：走原有智能解压逻辑（含 `ListEntriesAsync` + `ArchiveStructureAnalyzer` 分析）
     - 多文件：`RunExtract(allPaths, Smart)`

  4. **改造 `HandleExtractToNamed`（`App.Cli.cs:508`）**：
     - 同上 IPC 合并
     - 单文件：走原有逻辑（解压到 `{所在目录}/{压缩包名}/`）
     - 多文件：`RunExtract(allPaths, ToName)`

  5. **改造 `HandleExtract`（`App.Cli.cs:592`）**：
     - 单文件（`e.Args.Length == 2`）：**跳过 IPC 合并**，直接弹 `ExtractSettingsWindow([archivePath])`
     - 多文件：走 IPC 合并，合并后弹 `ExtractSettingsWindow(allPaths)`：
       - 用户可添加/移除文件，选择输出模式
       - 最后得到 `selectedPaths` + 模式 + 自定义路径
       - 调用 `RunExtract(selectedPaths, mode, customDest)`
       - 用户取消 → `Current.Shutdown()`
     - 原 `ResolveExtractDestinationStatic`（弹 `VistaFolderBrowserDialog`）不再被 `--extract` 调用

  6. **App.xaml.cs switch 路由**：
     - 保留 `--extract-here` / `--extract-smart` / `--extract-to-name` / `--extract` 的路由不变
     - 不需要新增 `--extract-batch` 路由

  6. **单元测试**：
     - 在 `tests/MantisZip.Tests/` 添加 `BatchExtractTests.cs`
     - 由于 `HandleExtract*` 和 `RunExtract` 在 UI 项目中，tests 只引用 Core
     - 测试覆盖：
       - `ArchiveEngineFactory.GetEngineByExtension()` 路径验证逻辑
       - `RunExtract` 的 4 种模式路径解析算法（提取为 Core 可测试的方法，或通过 public static 方法暴露）
       - IPC 合并后的 `ExtractOutputMode` 路由决策逻辑（纯逻辑测试）

  **Must NOT do**:
  - 不修改 ShellIntegration.cs（动词不变）
  - 不修改 `--compress-*` 相关代码
  - 不修改 `RunExtractStatic` 方法（单文件路径解析可复用）
  - 不改密码管理器逻辑

  **Recommended Agent Profile**:
  - **Category**: `deep`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 7, 8)
  - **Parallel Group**: Wave 4
  - **Blocks**: F1-F4
  - **Blocked By**: Tasks 7, 8

  **References**:
  - `MantisZip.UI/App.Cli.cs:80` — `HandleCompressSeparate` IPC 模式
  - `MantisZip.UI/App.Cli.cs:489` — `HandleExtractHere`
  - `MantisZip.UI/App.Cli.cs:508` — `HandleExtractToNamed`
  - `MantisZip.UI/App.Cli.cs:533` — `HandleExtractSmart`
  - `MantisZip.UI/App.Cli.cs:592` — `HandleExtract`
  - `MantisZip.UI/App.Cli.cs:617` — `RunExtractStatic`
  - `MantisZip.UI/App.PipeServer.cs:89` — `StartPipeServer` / `SendPathsThroughPipe`
  - `MantisZip.Core/Utils/ArchiveStructureAnalyzer.cs` — Smart 模式分析

  **Acceptance Criteria**:
  - [ ] `--extract-here` 单选 1 个文件 → 同现有行为（直接解压）
  - [ ] `--extract-here` 多选（IPC 合并后 2+ 文件）→ ProgressWindow 批处理列表
  - [ ] `--extract` 单选 1 个文件 → 弹 ExtractSettingsWindow → 确认后解压
  - [ ] `--extract` 多选 → IPC 合并 → 弹 ExtractSettingsWindow 带多文件列表
  - [ ] `--extract` ExtractSettingsWindow 中移除文件 + 选 Manual 路径 → 解压到指定目录
  - [ ] `--extract-smart` / `--to-name` 多文件 → 批处理
  - [ ] 现有 `--compress-*` 行为不受影响
  - [ ] ShellIntegration 动词未修改
  - [ ] 路径含空格正确处理
  - [ ] `dotnet test` — 所有测试通过

  **QA Scenarios**:
  ```
  Scenario: --extract 单文件弹 ExtractSettingsWindow
    Tool: Bash (direct invoke)
    Steps:
      1. 运行 --extract "D:\a.zip"
      2. 验证弹出 ExtractSettingsWindow 显示 1 个文件
      3. 选择"手动输入"，选目标目录，点击[解压]
      4. 验证解压到目标目录
    Expected Result: 单文件也弹设置窗口
    Evidence: .sisyphus/evidence/task-9-extract-single.txt

  Scenario: --extract 多文件合并
    Tool: Bash (simulate IPC)
    Steps:
      1. 创建 2 个 ZIP
      2. 模拟 IPC 合并后 2 个路径
      3. 验证弹 ExtractSettingsWindow 显示 2 项
      4. 选"解压到此处"，[解压]
      5. 各解压到各自所在目录
    Expected Result: 多文件批处理
    Evidence: .sisyphus/evidence/task-9-extract-multi.txt

  Scenario: --extract-here 多文件批处理
    Tool: Bash (simulate IPC)
    Steps:
      1. 创建 2 个 ZIP 在不同目录
      2. IPC 合并路径后 2 条
      3. 直接进 ProgressWindow 不弹设置窗
      4. 各解压到各自目录
    Expected Result: here 模式不弹窗口
    Evidence: .sisyphus/evidence/task-9-here-batch.txt

  Scenario: --extract-here 多文件批处理
    Tool: Bash (simulate IPC)
    Steps:
      1. 创建 2 个 ZIP 在不同目录
      2. IPC 合并后 mode=--here, 2 paths
      3. 直接进 ProgressWindow 不弹设置窗
      4. 各解压到各自目录
    Expected Result: here 模式不弹窗口
    Evidence: .sisyphus/evidence/task-9-here-batch.txt
  ```

  **Commit**: YES（实现 + 测试同一次提交）
  - Message: `feat(ui): add IPC merge to HandleExtract* methods`
  - Files: `src/MantisZip.UI/App.Cli.cs`, `tests/MantisZip.Tests/BatchExtractTests.cs`

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. Verify each Must Have is implemented. Check must-not-have guardrails. Count evidence files. Compare deliverables.

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build` + `dotnet test`. Check for `as any`/null-forgiving, empty catches, unused imports. Check test coverage.

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Execute ALL QA scenarios from ALL tasks. Test cross-task integration. Test edge cases: single item, empty list, all fail, mixed success/failure.

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read spec vs actual diff. Verify 1:1 — everything built, nothing beyond scope.

---

## Commit Strategy
- **1**: `feat(core): add BatchItem model and unit tests`（实现 + 测试合并)
- **2**: `feat(ui): add batch progress localization strings`
- **3**: `feat(ui): add file list UI to ProgressWindow`（XAML + converter)
- **4**: `feat(ui): add batch mode API to ProgressWindow`（code-behind，与 3 同次 squash 可选)
- **5**: `test(ui): add ProgressWindow batch mode tests`
- **6**: `feat(ui): integrate batch file list with CLI and GUI compress paths`（提取共享核心 + CLI + GUI + 测试合并）
- **7**: `feat(ui): add IPC merge infra and RunExtract method`
- **8**: `feat(ui): add ExtractSettingsWindow for extract config`
- **9**: `feat(ui): add IPC merge to HandleExtract* methods`（实现 + 测试合并)
- **F1-F4**: `chore: final verification wave`

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.UI\MantisZip.UI.csproj      # 0 errors
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj  # all pass
```

### Final Checklist
- [ ] All "Must Have" present
- [ ] All "Must NOT Have" absent
- [ ] All tests pass
