# 批量处理进度窗口文件列表

> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜] (0/12)

## 任务总览

- [ ] **Wave 1: 基础模型** — BatchItem 数据模型 + 本地化 + 单元测试 (Task 1-3)
- [ ] **Wave 2: ProgressWindow UI** — 文件列表 XAML + 批处理模式 API + 测试 (Task 4-6)
- [ ] **Wave 3: 批处理压缩集成** — RunCompressSeparateBatch + 测试 (Task 7-8)
- [ ] **Wave 4: 批处理解压** — --extract-batch CLI + IPC + 右键菜单 + 测试 (Task 9-12)

## TL;DR

> **Quick Summary**: 在批量操作（多压缩包压缩/解压）的进度窗口中添加文件列表，每项显示名称 + 状态（待处理/进行中/已完成/失败），实时刷新。
>
> **Deliverables**:
> - ProgressWindow 增加文件列表 UI（固定高度滚动）
> - `--compress-separate` 集成文件列表
> - 新增 `--extract-batch` CLI + IPC 机制，解压菜单支持批量模式
> - TDD 测试覆盖
>
> **Estimated Effort**: Medium
> **Parallel Execution**: YES - 3 waves
> **Critical Path**: Task 1 → Task 2 → Task 4 → Task 8 → Task 10 → F1-F4

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
- `src/MantisZip.UI/ProgressWindow.xaml` — 进度窗口 UI
- `src/MantisZip.UI/ProgressWindow.xaml.cs` — 进度窗口逻辑
- `src/MantisZip.UI/App.xaml.cs` — `RunCompressSeparateBatch` (line 422) + `RunExtractStatic` (line 1037) + CLI 入口
- `src/MantisZip.UI/ShellIntegration.cs` — 右键菜单注册
- `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` — `ArchiveProgress` 数据模型
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
- 多压缩包解压（新 CLI `--extract-batch` + IPC）使用新列表
- 右键菜单中选择多项时触发批处理解压模式

### Definition of Done
- [ ] `dotnet build` 0 errors
- [ ] `dotnet test` all tests pass
- [ ] `--compress-separate` 显示文件列表且带正确状态
- [ ] 多压缩包右键解压进入批处理模式并显示列表
- [ ] 全部成功自动关闭，有失败保持打开

### Must Have
- 文件列表中每项显示名称 + 状态
- 状态实时更新（待处理→进行中→已完成/失败）
- 列表固定高度，超出可滚动
- 批处理解压 IPC 机制：多进程合并为单进程单窗口

### Must NOT Have (Guardrails)
- 不实现单项重试功能
- 不实现暂停后跳过当前项
- 不实现拖拽排序
- 不修改现有的 `--extract-here` / `--extract-smart` / `--extract-to-name` / `--extract` 单文件 CLI 行为

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
├── Task 1: BatchItem 数据模型 + BatchItemStatus 枚举
├── Task 2: 本地化字符串 (L.cs + JSON)
├── Task 3: 单元测试 — BatchItem 模型

Wave 2 (ProgressWindow UI — depends on T1, T2):
├── Task 4: ProgressWindow.xaml 文件列表 UI
├── Task 5: ProgressWindow.xaml.cs 批处理模式 API
├── Task 6: 单元测试 — ProgressWindow 批处理模式

Wave 3 (Integration — depends on T4, T5):
├── Task 7: RunCompressSeparateBatch 集成文件列表
├── Task 8: 单元测试 — 批处理压缩集成

Wave 4 (Batch Extract — depends on T7):
├── Task 9: --extract-batch CLI + IPC (Mutex + NamedPipeServer)
├── Task 10: RunExtractBatch 方法实现
├── Task 11: ShellIntegration 多选菜单支持
├── Task 12: 单元测试 — 批处理解压

Wave FINAL:
├── F1-F4: Verification
```

### Dependency Matrix
- **1-3**: — 4-6, 1
- **4-5**: 1, 2 — 7, 2
- **6**: 1, 2 — 7, 2
- **7**: 4, 5, 6 — 9, 3
- **8**: 7 — 9, 3
- **9**: 7, 8 — 10, 11, 12, 4
- **10**: 9 — 12, 4
- **11**: 9 — F1-F4, 4
- **12**: 9, 10 — F1-F4, 4
- **F1-F4**: all — done

---

## TODOs

- [ ] 1. **创建 BatchItem 数据模型 + BatchItemStatus 枚举**

  **What to do**:
  - 在 `MantisZip.UI` 项目中新建 `ProgressBatchItem.cs` 文件
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
        
        // 状态文本（从 Status 派生，用于 UI 绑定）
        public string StatusText => Status switch { ... };
        // 状态图标（用于 UI 显示）
        public string StatusIcon => Status switch { ... };
    }
    ```
  - 属性变化时触发 `PropertyChanged`（Status, StatusText, StatusIcon）

  **Must NOT do**:
  - 不添加任何 UI 逻辑
  - 不引用 WPF 特定类型

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1)
  - **Blocks**: Tasks 4, 5, 6
  - **Blocked By**: None

  **References**:
  - `MantisZip.UI/MainWindow.UI.cs:351-371` — `FolderNode` 的 `INotifyPropertyChanged` 模式
  - `MantisZip.Core/Abstractions/ArchiveEngine.cs:1-18` — 类似的数据模型（`ArchiveItem`）

  **Acceptance Criteria**:
  - [ ] TDD: 先写失败测试 → 实现 → 全部通过
  - [ ] `dotnet test` passes

  **QA Scenarios**:
  ```
  Scenario: BatchItem 初始状态为 Pending
    Tool: Bash (dotnet test)
    Steps:
      1. 创建 BatchItem 实例，不设置 Status
      2. 验证 Status == BatchItemStatus.Pending
      3. 验证 StatusText == "待处理"
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

  **Commit**: YES
  - Message: `feat(core): add BatchItem model and BatchItemStatus enum`
  - Files: `src/MantisZip.UI/ProgressBatchItem.cs`

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
  - **Blocks**: Tasks 4, 5
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

- [ ] 3. **单元测试 — BatchItem 模型**

  **What to do**:
  - 在 `tests/MantisZip.Tests/` 下新建 `ProgressBatchItemTests.cs`
  - 测试覆盖：
    - 默认状态为 Pending
    - Status 变更触发 PropertyChanged
    - StatusText 随 Status 变化
    - StatusIcon 随 Status 变化
    - ErrorMessage 为空时不抛出

  **Must NOT do**:
  - 不测试 UI 交互（只测纯逻辑）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1)
  - **Blocks**: Tasks 7
  - **Blocked By**: Task 1

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
    Evidence: .sisyphus/evidence/task-3-test-results.txt
  ```

  **Commit**: YES
  - Message: `test(core): add BatchItem model unit tests`
  - Files: `tests/MantisZip.Tests/ProgressBatchItemTests.cs`

---

- [ ] 4. **ProgressWindow.xaml 文件列表 UI**

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
                              <TextBlock Text="{Binding StatusIcon}" />
                              <TextBlock Text="{Binding Name}" />
                              <TextBlock Text="{Binding StatusText}" />
                          </Grid>
                      </DataTemplate>
                  </ListBox.ItemTemplate>
              </ListBox>
          </Grid>
      </Border>
      ```
  - 列表中每行显示：状态图标 + 名称（左对齐）+ 状态文本（右对齐）
  - 列表固定高度（约 180px = 6 行 + 标题）
  - 窗口高度从 `MinHeight="175"` 改为 `MinHeight="250"`（批处理模式时增大到约 450px）
    - 提示：窗口 `SizeToContent="Height"`，批处理模式下动态增大

  **Must NOT do**:
  - 不修改现有控件（PasswordSection, ProgressBar, buttons 等）
  - 不破坏现有非批处理模式的显示

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Task 1, 2)
  - **Parallel Group**: Wave 2
  - **Blocks**: Tasks 7, 9
  - **Blocked By**: Tasks 1, 2

  **References**:
  - `MantisZip.UI/ProgressWindow.xaml` — 当前完整的 XAML
  - `MantisZip.UI/ProgressWindow.xaml.cs:85-106` — `SetProgress` 方法（了解 UI 更新模式）

  **Acceptance Criteria**:
  - [ ] 列表区域默认隐藏（非批处理模式）
  - [ ] `BatchListSection.Visibility = Visible` 时显示列表
  - [ ] `BatchItemList` 绑定到 `BatchItem` 集合
  - [ ] 每一项显示状态图标 + 名称 + 状态文本

  **QA Scenarios**:
  ```
  Scenario: 非批处理模式列表隐藏
    Tool: Playwright
    Steps:
      1. 创建 ProgressWindow 实例（非批处理模式）
      2. 验证 BatchListSection.Visibility == Collapsed
    Expected Result: 列表不显示
    Evidence: .sisyphus/evidence/task-4-hidden.png

  Scenario: 批处理模式列表显示
    Tool: Playwright
    Steps:
      1. 创建 ProgressWindow 实例
      2. 调用 InitBatchMode(["a.zip", "b.zip"])
      3. 验证 BatchListSection.Visibility == Visible
      4. 验证列表中有 2 项
    Expected Result: 列表正确显示
    Evidence: .sisyphus/evidence/task-4-visible.png
  ```

  **Commit**: YES
  - Message: `feat(ui): add file list UI to ProgressWindow`
  - Files: `src/MantisZip.UI/ProgressWindow.xaml`

---

- [ ] 5. **ProgressWindow.xaml.cs 批处理模式 API**

  **What to do**:
  - 新增 ObservableCollection<BatchItem> \`_batchItems\` 属性
  - 新增方法 \`InitBatchMode(IReadOnlyList<string> items)\`：
    - 设置 \`IsBatchMode = true\`
    - 创建 \`_batchItems\` 集合，每项初始化为 Pending 状态
    - 设置 BatchItemList.ItemsSource
    - 显示 BatchListSection
    - 增大窗口高度（设置 Height 或 MinHeight）
  - 新增方法 \`UpdateBatchItemStatus(int index, BatchItemStatus status, string? error = null)\`：
    - 更新 \`_batchItems[index]\` 的 Status 和 ErrorMessage
  - 新增方法 \`SetCurrentBatchItem(int index)\`：
    - 更新 \`_batchItems[index]\` 到 InProgress
    - 更新列表滚动到当前项
  - 在 \`SetComplete\` 中更新 BatchListHeader 统计
  - 如果存在失败项，\`SetComplete\` 不自动关闭窗口
  - 新增属性 \`HasFailures\`（检查是否有 Failed 项目）
  - 修改 \`InitCancellation\` 的签名或重载以支持批处理模式

  **Must NOT do**:
  - 不修改现有非批处理 API 的行为
  - 不添加业务逻辑（仅 UI 管理）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Task 4, Wave 2)
  - **Blocks**: Tasks 7, 9, 10
  - **Blocked By**: Tasks 1, 2

  **References**:
  - `MantisZip.UI/ProgressWindow.xaml.cs:85-106` — \`SetProgress\` 模式
  - `MantisZip.UI/ProgressWindow.xaml.cs:295-301` — \`OnClosed\` 清理模式
  - `MantisZip.UI/ProgressWindow.xaml` — 新增的 BatchListSection 和 BatchItemList

  **Acceptance Criteria**:
  - [ ] InitBatchMode 正确初始化列表
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
      3. 验证列表显示 3 项，所有状态为 "待处理"
    Expected Result: 正确初始化
    Evidence: .sisyphus/evidence/task-5-init.png

  Scenario: 更新单项状态
    Tool: Playwright
    Steps:
      1. InitBatchMode(["a.zip", "b.zip"])
      2. SetCurrentBatchItem(0)
      3. 验证索引 0 状态为 "进行中...", 索引 1 为 "待处理"
      4. UpdateBatchItemStatus(0, Completed)
      5. 验证索引 0 状态为 "已完成"
    Expected Result: 状态正确更新
    Evidence: .sisyphus/evidence/task-5-update.png
  ```

  **Commit**: YES (groups with 4)
  - Message: `feat(ui): add batch mode API to ProgressWindow`
  - Files: `src/MantisZip.UI/ProgressWindow.xaml.cs`

---

- [ ] 6. **单元测试 — ProgressWindow 批处理模式**

  **What to do**:
  - 在 tests 项目中添加 ProgressWindowBatchTests.cs
  - 测试可纯逻辑部分：
    - BatchItem 状态转换逻辑
    - HasFailures 判断逻辑
    - CompleteWithErrors 统计逻辑
  - 注意：WPF UI 控件需在 STA 线程测试（使用 `[STAThread]` 或 `Apartment(ApartmentState.STA)`）

  **Must NOT do**:
  - 不测试 XAML 渲染（超出单元测试范围）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 2)
  - **Blocks**: Tasks 7
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
    Evidence: .sisyphus/evidence/task-6-test-results.txt
  ```

  **Commit**: YES
  - Message: `test(ui): add ProgressWindow batch mode tests`
  - Files: `tests/MantisZip.Tests/ProgressWindowBatchTests.cs`

---

- [ ] 7. **RunCompressSeparateBatch 集成文件列表**

  **What to do**:
  - 修改 `App.xaml.cs` 中的 `RunCompressSeparateBatch` 方法：
    1. 创建 ProgressWindow 后，立即调用 `progressWindow.InitBatchMode(allPaths)`
    2. 在循环开始前调用 `progressWindow.SetCurrentBatchItem(i)`
    3. 开始压缩前调用 `progressWindow.UpdateBatchItemStatus(i, InProgress)`
    4. 压缩完成后调用 `progressWindow.UpdateBatchItemStatus(i, Completed)`
    5. 压缩失败后调用 `progressWindow.UpdateBatchItemStatus(i, Failed, ex.Message)`
  - 当前进度消息（"正在压缩 (1/8)"）保留，通过 `FileNameText` 显示
  - 修改 `SetComplete` 行为：ProgressWindow 根据是否有失败项决定自动关闭还是保持打开

  **Must NOT do**:
  - 不修改压缩引擎本身的逻辑
  - 不改变 IPC 收集路径的机制

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 4, 5, 6)
  - **Parallel Group**: Wave 3
  - **Blocks**: Tasks 9, 10
  - **Blocked By**: Tasks 4, 5, 6

  **References**:
  - `MantisZip.UI/App.xaml.cs:422-541` — `RunCompressSeparateBatch` 完整代码
  - `MantisZip.UI/App.xaml.cs:378-420` — `HandleCompressSeparate` IPC 入口
  - `MantisZip.UI/ProgressWindow.xaml.cs` — 新增的批处理 API

  **Acceptance Criteria**:
  - [ ] `--compress-separate` 运行时 ProgressWindow 显示文件列表
  - [ ] 每项状态从"待处理"→"进行中"→"已完成"/"失败"实时变化
  - [ ] 全部成功时自动关闭
  - [ ] 有失败时保持打开，显示失败数量

  **QA Scenarios**:
  ```
  Scenario: --compress-separate 全部成功
    Tool: Bash (direct invoke)
    Steps:
      1. 创建 3 个临时文件
      2. 运行 --compress-separate 传入这 3 个文件
      3. 验证 ProgressWindow 文件列表：3 项全部变为"已完成"
      4. 验证窗口在完成 2s 后自动关闭
      5. 验证 3 个压缩包文件已生成
    Expected Result: 批量压缩成功，列表正确
    Evidence: .sisyphus/evidence/task-7-all-success.txt

  Scenario: --compress-separate 部分失败
    Tool: Bash (direct invoke)
    Steps:
      1. 模拟一项写入失败
      2. 运行 --compress-separate
      3. 观察列表：失败项显示"失败"，其余显示"已完成"
      4. 验证窗口保持打开
    Expected Result: 失败项正确标记
    Evidence: .sisyphus/evidence/task-7-some-failed.txt
  ```

  **Commit**: YES
  - Message: `feat(ui): integrate batch file list with --compress-separate`
  - Files: `src/MantisZip.UI/App.xaml.cs`

---

- [ ] 8. **单元测试 — 批处理压缩集成**

  **What to do**:
  - 在 tests 项目中添加 `CompressSeparateBatchTests.cs`
  - 测试覆盖：
    - `RunCompressSeparateBatch` 调用 `InitBatchMode`（通过 Mock/Fake ProgressWindow 验证）
    - 状态更新顺序：Pending → InProgress → Completed/Failed
    - 正常路径（所有文件可压缩）
    - 路径含空格、中文文件名
    - 空路径列表（应优雅跳过）

  **Must NOT do**:
  - 不运行真实压缩（使用 Mock 引擎）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 3)
  - **Blocks**: Tasks 9
  - **Blocked By**: Tasks 7

  **References**:
  - `tests/MantisZip.Tests/` — 现有测试结构
  - `MantisZip.UI/App.xaml.cs:422-541` — 被测试方法

  **Acceptance Criteria**:
  - [ ] `dotnet test` — 所有测试通过

  **QA Scenarios**:
  ```
  Scenario: 全部测试通过
    Tool: Bash (dotnet test)
    Steps:
      1. dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
    Expected Result: 所有测试通过
    Evidence: .sisyphus/evidence/task-8-test-results.txt
  ```

  **Commit**: YES (squash with 7)
  - Message: `test(ui): add compress-separate integration tests`
  - Files: `tests/MantisZip.Tests/CompressSeparateBatchTests.cs`

---

- [ ] 9. **新增 --extract-batch CLI + IPC**

  **What to do**:
  - 在 `App.xaml.cs` 中的 `OnStartup` switch 块添加 `case "--extract-batch":`
  - 参考 `--compress-separate` 的 IPC 模式：
    - 声明 Mutex 名称: `MantisZipExtractBatchMutex`
    - 声明 Pipe 名称: `MantisZipExtractBatchPipe`
    - 声明 `_extractBatchPipeReady` ManualResetEventSlim
    - `HandleExtractBatch(string[] paths)` 方法：
      - 收集有效压缩包路径
      - 第一个实例：启动 PipeServer 收集所有路径，800ms 后调用 `RunExtractBatch`
      - 后续实例：通过 Pipe 发送路径后退出
  - PipeServer 循环接收多客户端连接（复用 `StartPipeServer` 或新建专门方法）
  - 验证所有路径是存在的压缩包文件（`.zip`/`.7z`/`.rar`/`.tar`/`.tgz`/`.gz`/`.iso`）
  - 日志记录

  **Must NOT do**:
  - 不修改现有解压 CLI（--extract, --extract-here 等）
  - 不修改 ShellIntegration（在 Task 11 做）

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 7, 8)
  - **Parallel Group**: Wave 4
  - **Blocks**: Tasks 10, 11, 12
  - **Blocked By**: Tasks 7, 8

  **References**:
  - `MantisZip.UI/App.xaml.cs:209-264` — `HandleCompress` IPC 模式（完整参考实现）
  - `MantisZip.UI/App.xaml.cs:266-301` — `StartCompressPipeServer`
  - `MantisZip.UI/App.xaml.cs:303-318` — `SendPathsToFirstInstance`
  - `MantisZip.UI/App.xaml.cs:324-376` — `StartPipeServer` / `SendPathsThroughPipe`（通用版本）

  **Acceptance Criteria**:
  - [ ] `--extract-batch a.zip b.zip c.zip` 启动单个 ProgressWindow
  - [ ] 多实例 IPC 合并路径（模拟 Windows 多选文件）
  - [ ] 只有存在的压缩包文件被接受
  - [ ] 日志记录所有路径

  **QA Scenarios**:
  ```
  Scenario: --extract-batch 多压缩包单窗口
    Tool: Bash (direct invoke)
    Steps:
      1. 创建 2 个临时 ZIP 压缩包
      2. 运行 --extract-batch archive1.zip archive2.zip
      3. 验证只弹出 1 个 ProgressWindow
      4. 验证列表显示 2 个压缩包名称
    Expected Result: 单窗口批量处理
    Evidence: .sisyphus/evidence/task-9-batch.txt

  Scenario: IPC 多进程合并
    Tool: Bash (simulate via mutex)
    Steps:
      1. 先用 mutex 占用，模拟第一个实例
      2. 启动第二个 --extract-batch 实例
      3. 验证第二个实例发送路径后退出
    Expected Result: IPC 正常工作
    Evidence: .sisyphus/evidence/task-9-ipc.txt
  ```

  **Commit**: YES
  - Message: `feat(ui): add --extract-batch CLI with IPC`
  - Files: `src/MantisZip.UI/App.xaml.cs`

---

- [ ] 10. **实现 RunExtractBatch 方法**

  **What to do**:
  - 在 `App.xaml.cs` 中新增 `RunExtractBatch(List<string> allPaths)` 方法
  - 流程：
    1. 创建 ProgressWindow，调用 `InitBatchMode(allPaths)`
    2. 设置 `ShutdownMode = OnExplicitShutdown`
    3. 循环处理每个压缩包：
       a. `progressWindow.SetCurrentBatchItem(i)`
       b. `progressWindow.UpdateBatchItemStatus(i, InProgress)`
       c. 获取引擎: `ArchiveEngineFactory.GetEngineByExtension(path)`
       d. 检查是否需要密码（复用 `HasEncryptedEntries` + `QuickVerifyPassword`）
       e. 若需密码：尝试已保存密码 → 失败则弹输入框 → 正确则继续
       f. 确定解压目标目录（使用 AppSettings.ExtractDestination）
       g. 执行解压：`engine.ExtractAsync(...)`
       h. 成功 → `UpdateBatchItemStatus(i, Completed)`
       i. 失败 → `UpdateBatchItemStatus(i, Failed, ex.Message)` + 记录日志
    4. 完成后调用 `progressWindow.SetComplete(...)`
    5. 有失败项时窗口保持打开；全部成功延迟后自动关闭

  **Must NOT do**:
  - 不修改 RunExtractStatic（单文件解压行为不变）
  - 不触及密码管理器逻辑（复用现有 API）

  **Recommended Agent Profile**:
  - **Category**: `deep`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 9)
  - **Parallel Group**: Wave 4
  - **Blocks**: Tasks 12
  - **Blocked By**: Tasks 9

  **References**:
  - `MantisZip.UI/App.xaml.cs:1037-1175` — `RunExtractStatic`（单文件解压完整流程参考）
  - `MantisZip.UI/App.xaml.cs:422-541` — `RunCompressSeparateBatch`（批量循环模式参考）
  - `MantisZip.UI/App.xaml.cs:929-954` — `TryMatchPassword`
  - `MantisZip.UI/App.xaml.cs:960-989` — `PromptForPassword`
  - `MantisZip.UI/App.xaml.cs:1315-1337` — `HasEncryptedEntries`
  - `MantisZip.UI/App.xaml.cs:1339-1366` — `QuickVerifyPassword`

  **Acceptance Criteria**:
  - [ ] 批量解压 N 个压缩包，全部成功
  - [ ] 批量解压中部分失败，正确标记
  - [ ] 加密压缩包自动匹配已保存密码
  - [ ] 加密压缩包无已保存密码时弹出输入框

  **QA Scenarios**:
  ```
  Scenario: 批量解压全部成功
    Tool: Bash (direct invoke)
    Steps:
      1. 创建 2 个普通 ZIP 压缩包包含测试文件
      2. 运行 --extract-batch archive1.zip archive2.zip
      3. 观察列表：2 项变为"已完成"
      4. 验证文件被正确解压
    Expected Result: 全部解压成功
    Evidence: .sisyphus/evidence/task-10-all-success.txt

  Scenario: 批量解压部分失败（损坏压缩包）
    Tool: Bash (direct invoke)
    Steps:
      1. 创建 1 个正常 ZIP + 1 个损坏 ZIP
      2. 运行 --extract-batch good.zip bad.zip
      3. 观察列表：good 变为"已完成"，bad 变为"失败"
      4. 验证窗口保持打开显示失败信息
    Expected Result: 失败项正确标记
    Evidence: .sisyphus/evidence/task-10-some-failed.txt
  ```

  **Commit**: YES (groups with 9)
  - Message: `feat(ui): implement RunExtractBatch`
  - Files: `src/MantisZip.UI/App.xaml.cs`

---

- [ ] 11. **ShellIntegration 多选解压菜单支持**

  **What to do**:
  - 分析：Windows 右键菜单动词传递文件时，`%1` 只传递第一个文件，`%*` 传递所有选中文件
  - 修改 ShellIntegration 中的解压菜单项，使用 `%*` 代替 `%1`，并指向新 CLI
  - 在 App.OnStartup 中增强 `--extract-here`, `--extract-smart`, `--extract-to-name`, `--extract` 处理：当 args.Length > 2（第一条是命令，后续是路径）时，自动路由到批处理模式
  - 也就是说 Shell 注册动词改为 `--extract-here "%*"`，App 端检测多参数时启动 IPC 收集流程
  - 若只有单文件路径，走现有单文件处理路径（不破坏现有行为）

  **Must NOT do**:
  - 不修改 `--compress-*` 的菜单注册
  - 不破坏单文件解压（右键菜单或 CLI 直接调用）

  **Recommended Agent Profile**:
  - **Category**: `deep`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 9, 10)
  - **Parallel Group**: Wave 4
  - **Blocks**: F1-F4
  - **Blocked By**: Tasks 9, 10

  **References**:
  - `MantisZip.UI/ShellIntegration.cs` — 完整 ShellIntegration 实现
  - `MantisZip.UI/App.xaml.cs:106-181` — `OnStartup` 的 CLI 处理
  - `MantisZip.UI/App.xaml.cs:795-890` — 各 HandleExtract* 方法

  **Acceptance Criteria**:
  - [ ] 多选压缩包右键 → 解压到此处 → 单个 ProgressWindow 带文件列表
  - [ ] 多选压缩包右键 → 智能解压 → 单个 ProgressWindow 带文件列表
  - [ ] 单选压缩包右键 → 现有行为不变（单窗口无列表）

  **QA Scenarios**:
  ```
  Scenario: 多选解压到此处
    Tool: Bash (simulate)
    Steps:
      1. 创建 2 个 ZIP 压缩包
      2. 运行 --extract-here "a.zip" "b.zip"
      3. 验证 ProgressWindow 显示 2 项列表
      4. 验证两个压缩包都正确解压到各自目录
    Expected Result: 批处理模式工作
    Evidence: .sisyphus/evidence/task-11-batch-extract.txt

  Scenario: 单选行为不变
    Tool: Bash (simulate)
    Steps:
      1. 运行 --extract-here "single.zip"
      2. 验证行为同现有单文件解压
    Expected Result: 单选不触发批处理模式
    Evidence: .sisyphus/evidence/task-11-single.txt
  ```

  **Commit**: YES
  - Message: `feat(ui): support batch extract in shell context menu`
  - Files: `src/MantisZip.UI/ShellIntegration.cs`, `src/MantisZip.UI/App.xaml.cs`

---

- [ ] 12. **单元测试 — 批处理解压**

  **What to do**:
  - 在 tests 项目中添加 `ExtractBatchTests.cs`
  - 测试覆盖：
    - `RunExtractBatch` 调用 `InitBatchMode`
    - 状态更新路径：Pending → InProgress → Completed/Failed
    - 正常路径（多个有效压缩包）
    - 混合路径（有效 + 无效路径）
    - 空列表（优雅处理）

  **Must NOT do**:
  - 不运行真实解压（使用 Mock 引擎）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 4)
  - **Blocks**: F1-F4
  - **Blocked By**: Tasks 9, 10

  **References**:
  - `tests/MantisZip.Tests/` — 现有测试结构
  - `MantisZip.UI/App.xaml.cs` — `RunExtractBatch`

  **Acceptance Criteria**:
  - [ ] `dotnet test` — 所有测试通过

  **QA Scenarios**:
  ```
  Scenario: 全部测试通过
    Tool: Bash (dotnet test)
    Steps:
      1. dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
    Expected Result: 所有测试通过
    Evidence: .sisyphus/evidence/task-12-test-results.txt
  ```

  **Commit**: YES (squash with 11)
  - Message: `test(ui): add batch extract tests`
  - Files: `tests/MantisZip.Tests/ExtractBatchTests.cs`

---

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
- **1**: `feat(core): add BatchItem model and BatchItemStatus enum`
- **2**: `feat(ui): add batch progress localization strings`
- **3**: `test(core): add BatchItem model unit tests`
- **4**: `feat(ui): add file list UI to ProgressWindow`
- **5**: `feat(ui): add batch mode API to ProgressWindow`
- **6**: `test(ui): add ProgressWindow batch mode tests`
- **7**: `feat(ui): integrate batch file list with --compress-separate`
- **8**: `test(ui): add compress-separate integration tests`
- **9**: `feat(ui): add --extract-batch CLI with IPC`
- **10**: `feat(ui): implement RunExtractBatch`
- **11**: `feat(ui): support batch extract in shell context menu`
- **12**: `test(ui): add batch extract tests`
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
