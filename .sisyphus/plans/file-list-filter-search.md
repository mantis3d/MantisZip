# 文件列表筛选/搜索

## TL;DR

> **Quick Summary**: 在 MainWindow 文件列表区域添加三个独立但互相关联的功能：1) 主工具栏上的「显示所有子目录文件」ToggleButton，切换文件列表为递归扁平视图；2) 筛选工具栏（文字搜索 + 日期范围 + 大小范围），实时多维过滤；3) 通用的过滤引擎，支持组合过滤条件。
>
> **Deliverables**:
> - `MainWindow.xaml` — 新增 ToggleButton + 筛选工具栏 UI
> - `MainWindow.UI.cs` — 修改 FilterFiles + 新增过滤引擎
> - `MainWindow.xaml.cs` — 新增字段和事件处理
> - `strings.zh.json` / `strings.en.json` — 新增本地化字符串
> - `Core/Utils/ArchiveFilter.cs` — 可测试的过滤引擎
> - `MantisZip.Tests` — 单元测试覆盖所有过滤逻辑
>
> **Estimated Effort**: Medium（2-3h）
> **Parallel Execution**: YES — 3 waves
> **Critical Path**: Task 1 → Task 5 → Task 6 → Task 8

---

## Context

### Original Request
`docs/PLAN.md` 中已有条目「文件列表筛选/搜索 | -- | 🟢低 | 1-2h | 搜索框实时过滤」，但没有对应的计划文档。用户要求为此功能出计划文档。

### Interview Summary
**Key Discussions**:
- 功能拆分为两套独立系统：工具栏 ToggleButton（显示所有子目录）+ 搜索框（实时过滤）
- ToggleButton 改变文件列表视图模式；搜索框在当前视图上二次过滤
- 搜索实时触发（TextChanged），大小写不敏感，匹配所有可见列
- 扁平视图仅显示文件（不含目录条目）
- 搜索始终保留当前目录上下文，切换目录时自动重新应用搜索

**Research Findings**:
- `FilterFiles(folderPath)` 在 `MainWindow.UI.cs:192`，是当前文件列表的核心填充方法
- `_allItems` (List&lt;ArchiveItem>) 存储压缩包全部条目
- `FileListGrid` 是 DataGrid，位于 InnerContentGrid 的 Column 2
- 当前无任何搜索/筛选 UI 控件
- 排序状态通过 `_savedSortColumnPath` / `_savedSortDirection` 记忆和恢复
- 状态栏有 `DirStatsText` 显示条目统计
- 压缩设置窗口已有 PwdSearchBox 搜索模式可供参考

---

## Work Objectives

### Core Objective
在 MainWindow 的文件列表区域添加多维筛选过滤（文字+日期+大小）和子目录展开视图功能。

### Concrete Deliverables
- 主工具栏上的「显示所有子目录文件」ToggleButton（🌲 图标）
- 筛选工具栏（文字搜索 + 日期范围 DatePicker × 2 + 大小范围数字输入 × 2）
- 扁平视图模式（显示当前目录所有递归文件）
- 通用过滤引擎（支持文字/日期/大小组合过滤，可单元测试）
- 空结果提示（DataGrid 区域文字 + 状态栏同步）
- 中/英文本地化字符串
- 单元测试覆盖所有过滤逻辑

### Definition of Done
- [x] ToggleButton 在主工具栏可见，仅在加载压缩包后可用
- [x] ToggleButton 开启后文件列表显示当前目录所有子目录的文件（扁平、无目录）
- [x] ToggleButton 关闭后恢复为默认行为（当前目录直接条目 + 隐式目录）
- [x] 筛选工具栏在文件列表上方显示，包含文字搜索/日期范围/大小范围三个过滤区
- [x] 文字搜索：🔍 图标 + 水印"筛选文件…"，实时过滤，大小写不敏感，匹配所有可见列
- [x] 日期范围：两个 DatePicker（开始/结束，竖排），过滤 LastModified 区间
- [x] 大小范围：两个数字输入 + 单位 ComboBox（最小/最大，竖排），过滤 Size
- [x] 过滤组间竖线分隔，布局清晰
- [x] 清除按钮（×）重置所有过滤条件
- [x] 组合过滤：文字+日期+大小同时生效（AND 逻辑）
- [x] 无匹配时显示空结果提示（DataGrid 区域文字 + 状态栏）
- [x] 切换目录时所有过滤条件保持，新视图自动应用
- [x] 所有单元测试通过

### Must Have
- FilterFiles 支持 showSubfolders 参数
- 所有过滤均在内存中进行（不重新读取压缩包）
- ToggleButton 状态变化时重新调用 FilterFiles
- 三种过滤条件（文字/日期/大小）支持组合使用（AND）
- 过滤引擎可提取为独立方法，不依赖 WPF UI 线程
- 所有过滤控件实时响应，无确认按钮

### Must NOT Have (Guardrails)
- 不修改压缩/解压文件筛选（已有独立计划 file-filter-feature.md）
- 不修改预览系统的任何逻辑
- 不修改左侧目录树（FolderTree）的行为
- 不做服务器端搜索或跨压缩包搜索
- 不做模糊/正则搜索（仅简单的子字符串包含匹配）
- 日期过滤不改 ArchiveItem.LastModified 的值，仅在显示时过滤

### Future Extension（本期不实现）
- **扩展名过滤**：弹出式下拉复选框列表，多选扩展名过滤。当前文字搜索已可通过输入 `.ext` 实现扩展名过滤，专用控件作为未来增强

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES（xUnit, tests/MantisZip.Tests/）
- **Automated tests**: Tests-after
- **Framework**: xUnit (.NET 9)

### QA Policy
Every task MUST include agent-executed QA scenarios. Evidence saved to `.sisyphus/evidence/`.

- **UI changes**: Build and launch the app (`dotnet run --project src/MantisZip.UI/MantisZip.UI.csproj`), then manually verify control rendering (MantisZip 是 WPF 桌面应用，不能使用 Playwright 等 Web 工具)
- **Logic changes**: Use Bash to run unit tests
- **End-to-end**: Bash to build and launch the app, verify behaviors

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Foundation — can start in parallel):
├── Task 1: 本地化字符串 + ArchiveItem 辅助属性 [quick]
├── Task 2: 单元测试基础设施 [quick]

Wave 2a (Core — prerequisite, sequential):
├── Task 3: FilterFiles 增加 showSubfolders 模式 [quick]
│
Wave 2b (Core — parallel after Task 3):
├── Task 4: 主工具栏 ToggleButton [quick]       ← 依赖 Task 3，但可写 XAML+占位事件
├── Task 5: 搜索框 UI (XAML + 事件) [quick]      ← 依赖 Task 1, Task 3
│
Wave 2c (Core — sequential after Task 5):
├── Task 6: 搜索过滤逻辑 [quick]                 ← 依赖 Task 1, Task 3, Task 5

Wave 3 (Integration + Tests):
├── Task 7: 单元测试 — FilterFiles showSubfolders [quick]
├── Task 8: 单元测试 — 搜索过滤逻辑 [quick]
├── Task F1-F4: Final verification
```

### Dependency Matrix
- **1**: - → 5, 6
- **2**: - → 7, 8
- **3**: - → 4, 5, 6, 7
- **4**: 3 → 5, 6（Task 4 可在 Task 3 完成前写 XAML + 占位事件，但事件处理器涉及 FilterFiles 签名变化需等待 Task 3）
- **5**: 1, 3 → 6, 8
- **6**: 1, 3, 5 → 8
- **7**: 2, 3 → F1-F4
- **8**: 2, 6 → F1-F4

---

## TODOs

- [x] 1. 添加本地化字符串

  **What to do**:
  - 在 `strings.zh.json` 和 `strings.en.json` 中添加以下键值对：

    | Key | zh | en |
    |-----|----|----|
    | `Main_FilterSearch_Placeholder` | 筛选文件… | Filter files… |
    | `Main_Toolbar_ShowSubfolders` | 全部子目录 | All subfolders |
    | `Main_Tooltip_ShowSubfoldersOn` | 展开所有子目录的文件（当前：开启） | Show all subfolder files (On) |
    | `Main_Tooltip_ShowSubfoldersOff` | 仅当前目录（当前：关闭） | Current directory only (Off) |
    | `Main_Filter_NoResults` | 无匹配的文件 | No matching files |
    | `Main_Filter_ClearAll` | 清除全部 | Clear all |
    | `Main_Filter_DateLabel` | 日期: | Date: |
    | `Main_Filter_DateFrom` | 从 | From |
    | `Main_Filter_DateTo` | 到 | To |
    | `Main_Filter_SizeLabel` | 大小: | Size: |
    | `Main_Filter_SizeMin` | 最小 | Min |
    | `Main_Filter_SizeMax` | 最大 | Max |
    | `Main_Filter_UnitB` | B | B |
    | `Main_Filter_UnitKB` | KB | KB |
    | `Main_Filter_UnitMB` | MB | MB |
    | `Main_Filter_UnitGB` | GB | GB |
    | `Main_Filter_StatsFormat` | 显示 {0}/{1} 个文件 | Showing {0}/{1} files |
    | `Main_Toolbar_ToggleFilter` | 筛选 | Filter |
    | `Main_Tooltip_ToggleFilter` | 显示/隐藏筛选栏 | Show/hide filter bar |

  - 确认格式与其他字符串一致（注意 JSON 文件已有转义规则）

  **Must NOT do**:
  - 不修改或删除已有的字符串键

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none needed

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Task 2)
  - **Blocks**: Task 5, Task 6
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI/Resources/strings.zh.json` — 中文 JSON 字符串文件
  - `src/MantisZip.UI/Resources/strings.en.json` — 英文 JSON 字符串文件
  - 参考现有搜索相关字符串 `Compress_Pwd_Search` 和 `Compress_Pwd_EmptySearch` 的格式

  **Acceptance Criteria**:
  - [ ] `strings.zh.json` 新增 19 个键值对，格式正确（无 JSON 解析错误）
  - [ ] `strings.en.json` 新增对应的 19 个键值对
  - [ ] 运行 `dotnet build` 无编译错误

  **QA Scenarios**:
  ```
  Scenario: 验证 JSON 格式正确
    Tool: Bash
    Preconditions: 已修改两个 JSON 文件
    Steps:
      1. dotnet build src/MantisZip.UI/MantisZip.UI.csproj
    Expected Result: Build succeeds (0 errors)
    Evidence: .sisyphus/evidence/task-1-json-valid.txt
  ```

  **Commit**: YES
  - Message: `feat(l10n): add file list search/filter localization strings`
  - Files: `src/MantisZip.UI/Resources/strings.zh.json`, `src/MantisZip.UI/Resources/strings.en.json`
  - Pre-commit: `dotnet build`

---

- [x] 2. 单元测试基础设施 — 测试辅助方法和数据

  **What to do**:
  - 在 `tests/MantisZip.Tests/` 下创建或扩展现有测试文件
  - 新增 `FileListFilterTests.cs`，添加测试辅助方法：
    - `CreateTestItems()`: 创建模拟的 `List<ArchiveItem>` 测试数据集，包含多层目录结构
    - 数据集示例：
      - `readme.txt` (root)
      - `src/main.cs` (子目录)
      - `src/utils/helper.cs` (深层子目录)
      - `docs/index.html` (子目录)
      - `images/logo.png` (子目录)
    - 每个条目包含：Name, FullPath, Size, LastModified, IsDirectory, IsEncrypted 等字段

  **Must NOT do**:
  - 不修改现有的测试逻辑
  - 不引入外部 Mock 框架（用内存数据即可）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none needed

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Task 1)
  - **Blocks**: Task 7, Task 8
  - **Blocked By**: None

  **References**:
  - `tests/MantisZip.Tests/MantisZip.Tests.csproj` — 测试项目配置
  - 参考现有测试的 `[Fact]` 用法和 Assert 风格

  **Acceptance Criteria**:
  - [ ] `FileListFilterTests.cs` 存在且编译通过
  - [ ] `CreateTestItems()` 返回至少 8 个不同层级/类型的条目
  - [ ] `dotnet test tests/MantisZip.Tests/` 通过（测试桩可以 skip 或 pass）

  **QA Scenarios**:
  ```
  Scenario: 测试项目构建和运行
    Tool: Bash
    Preconditions: FileListFilterTests.cs 已创建
    Steps:
      1. dotnet test tests/MantisZip.Tests/
    Expected Result: Build + test run succeeds (0 failures)
    Evidence: .sisyphus/evidence/task-2-test-infra.txt
  ```

  **Commit**: YES（与 Task 7 或 Task 8 合并时提交）
  - Message: `test: add test helpers for file list filter/search`
  - Files: `tests/MantisZip.Tests/FileListFilterTests.cs`

---

- [x] 3. FilterFiles 增加 showSubfolders 模式

  **What to do**:
  - 在 `MainWindow.xaml.cs` 添加字段 `private bool _showSubfolders;`（默认 false）
  - 修改 `FilterFiles(string folderPath)` 签名 → `FilterFiles(string folderPath, bool? showSubfoldersOverride = null)`
    - 用 showSubfoldersOverride ?? _showSubfolders 决定模式
  - 当 `showSubfolders == false`（默认）：保持现有行为不变（直接条目 + 隐式目录合成）
  - 当 `showSubfolders == true` 时：
    - 从 `_allItems` 中搜集所有 `FullPath` 以当前 prefix 开头且 `IsDirectory == false` 的条目
    - **不创建任何隐式目录条目**
    - **不包含目录条目自身**（仅文件）
    - `DisplayName` 设置为相对于当前目录的相对路径（例如当前在 `src/`，文件 `src/utils/helper.cs` → `utils/helper.cs`）
    - 如果文件在当前目录的直接子层级，则仅显示文件名（与现有行为一致）
    - 跳过进度条比例计算中的目录相关逻辑
  - 更新 `DirStatsText` 在扁平模式下的显示格式：「{总数} 个文件（含子目录）」
  - 调用方检查：目录树切换、双击目录、Refresh 等场景需要传递正确的 showSubfolders 状态
  - 集成 `UpdateSelectionStats()`：`FilterFiles` 末尾在设置 ItemsSource 后应调用 `UpdateSelectionStats()`，确保过滤后选中统计正确（当前依赖 `SelectionChanged` 事件间接更新，但筛选栏变更事件中 `_isProgrammaticFilter == false`）

  **Must NOT do**:
  - 不破坏现有的排序恢复逻辑（`_savedSortColumnPath` / `_savedSortDirection`）
  - 不修改 `_allItems` 的内容（只为过滤读取，不修改）
  - 不修改左侧目录树的行为

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none needed

  **Parallelization**:
  - **Can Run In Parallel**: NO（FilterFiles 签名变化是 Task 4/5 事件处理器的前提）
  - **Parallel Group**: Wave 2a (sequential)
  - **Blocks**: Task 4, Task 6
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI/MainWindow.UI.cs:192-374` — `FilterFiles` 完整实现，需理解其逻辑
  - `src/MantisZip.UI/MainWindow.xaml.cs:40` — `_allItems` 字段定义
  - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs:8` — `ArchiveItem` 类定义（Name, FullPath, IsDirectory 等属性）

  **Acceptance Criteria**:
  - [ ] `_showSubfolders` 字段存在，默认 false
  - [ ] `FilterFiles` 接受可选参数 `showSubfoldersOverride`
  - [ ] 扁平模式下只显示文件（`IsDirectory == false`）
  - [ ] 扁平模式下 `DisplayName` 正确显示相对路径
  - [ ] `dotnet build` 无编译错误

  **QA Scenarios**:
  ```
  Scenario: 扁平模式只显示文件
    Tool: Bash (使用测试代码验证)
    Preconditions: CreateTestItems() 数据集可用
    Steps:
      1. 创建测试代码调用 FilterFiles 逻辑（showSubfolders=true）
      2. 断言结果不含 IsDirectory == true 的条目
    Expected Result: 所有返回条目的 IsDirectory == false
    Evidence: .sisyphus/evidence/task-3-flat-view.txt

  Scenario: 默认模式行为不变
    Tool: Bash
    Preconditions: CreateTestItems() 数据集
    Steps:
      1. 调用 FilterFiles 逻辑（showSubfolders=false）
      2. 检查结果包含目录条目
    Expected Result: 默认模式产生隐式目录，与现有行为一致
    Evidence: .sisyphus/evidence/task-3-default-mode.txt
  ```

  **Commit**: NO（随 Task 4 或 Task 7 一起提交）

---

- [x] 4. 主工具栏 ToggleButton × 2（「全部子目录」+「筛选栏显隐」）

  **What to do — 按钮 A：🌲 显示所有子目录文件**：
  - 在 `MainWindow.xaml` 的 `<ToolBar>` 中添加 ToggleButton：
    - 位置：在 SmartExtractBtn 之后、Separator 之前（或其他合适位置，与现有按钮风格一致）
    - `x:Name="ShowSubfoldersBtn"`
    - `IsChecked` 属性在代码-behind 中管理
    - 内容：🌲 emoji + 「全部子目录」文字标签（使用 `{l:L Main_Toolbar_ShowSubfolders}`）
    - ToolTip 根据状态动态切换（Main_Tooltip_ShowSubfoldersOn / Main_Tooltip_ShowSubfoldersOff）
    - `IsEnabled="False"`（默认禁用，压缩包加载后启用）
  - 事件处理：
    - `ShowSubfoldersBtn_Checked` / `ShowSubfoldersBtn_Unchecked`
    - 设置 `_showSubfolders` 并重新调用 `FilterFiles(_currentFolder)`
    - 新建 `UpdateShowSubfoldersBtnState()` 管理 IsEnabled
  - ⚠️ `_isProgrammaticFilter` 语义：ToggleButton 是用户操作，但 `FilterFiles` 执行时仍然设置 `_isProgrammaticFilter = true`，避免 `SelectionChanged` 触发非预期的预览。用户期待行为是：Toggle 后文件列表刷新，需要主动点击条目才触发预览。

  **What to do — 按钮 B：🔍 切换筛选栏显隐**：
  - 在 `ShowSubfoldersBtn` 旁边添加另一个 ToggleButton：
    - `x:Name="ToggleFilterBarBtn"`
    - 内容：🔍 emoji + 「筛选」文字标签（使用 `{l:L Main_Toolbar_ToggleFilter}`）
    - ToolTip：显示/隐藏筛选栏
    - `IsChecked = true`（默认展开筛选栏）
    - `IsEnabled="False"`（默认禁用，压缩包加载后启用）
  - 事件处理：
    - `ToggleFilterBarBtn_Checked` → 显示筛选工具栏（`FilterBar.Visibility = Visible`）
    - `ToggleFilterBarBtn_Unchecked` → 隐藏筛选工具栏（`FilterBar.Visibility = Collapsed`）
    - 筛选栏隐藏时，当前已应用的过滤条件**继续保持生效**（不清除）
  - 状态管理：
    - 在 `LoadArchiveAsync` 成功后启用两个按钮
    - 在 `CloseArchive` / 新压缩包加载时重置所有状态:
      - `_showSubfolders = false`
      - `ShowSubfoldersBtn.IsChecked = false`
      - `ToggleFilterBarBtn.IsChecked = true`（默认展开）
      - `_searchText = null; _dateFrom = _dateTo = null; _sizeMin = _sizeMax = null; _currentUnfilteredItems = null;`
      - 清空所有过滤控件的值

  **Must NOT do**:
  - 不修改现有按钮的布局和事件
  - 不在 ToggleButton 中嵌入复杂动画或样式（保持与现有 ToolBar 按钮一致）

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: none needed
  - Reason: WPF XAML UI 修改

  **Parallelization**:
  - **Can Run In Parallel**: YES（写 XAML + 事件注册时用占位处理，待 Task 3 完成后补充事件处理器体）
  - **Parallel Group**: Wave 2b (with Task 5)
  - **Blocks**: Task 6
  - **Blocked By**: Task 3

  **References**:
  - `src/MantisZip.UI/MainWindow.xaml:128-189` — ToolBar 区域，参考 SmartExtractBtn 的样式和布局
  - `src/MantisZip.UI/MainWindow.xaml.cs:68-80` — MainWindow 构造函数和按钮初始化
  - `src/MantisZip.UI/MainWindow.UI.cs:29-120` — UpdateSmartExtractBtnState / UpdateAddDeleteBtnState 等状态管理方法

  **Acceptance Criteria**:
  - [ ] ToggleButton 在 ToolBar 上可见，图标和文字正确
  - [ ] 未加载压缩包时按钮禁用
  - [ ] 加载压缩包后按钮启用
  - [ ] 点击切换状态，文件列表自动刷新
  - [ ] 关闭压缩包时按钮重置为未选中并禁用
  - [ ] `dotnet build` 无编译错误

  **QA Scenarios**:
  ```
  Scenario: ToggleButton 状态切换刷新视图
    Tool: Bash (dotnet build)
    Preconditions: 应用构建成功
    Steps:
      1. dotnet build
      2. 验证 XAML 中 ShowSubfoldersBtn 存在且有已定义的事件处理
    Expected Result: 构建通过，控件正确连接事件
    Evidence: .sisyphus/evidence/task-4-toggle-btn.txt
  ```

  **Commit**: YES
  - Message: `feat(ui): add "show all subfolders" ToggleButton to toolbar`
  - Files: `src/MantisZip.UI/MainWindow.xaml`, `src/MantisZip.UI/MainWindow.UI.cs`, `src/MantisZip.UI/MainWindow.xaml.cs`
  - Pre-commit: `dotnet build`

---

- [x] 5. 筛选工具栏 UI（XAML + 事件绑定）

  **What to do**:
  - 修改 `InnerContentGrid` 中 Column 2（FileListGrid 所在列）的布局：
    - 将 Column 2 的内容包装在一个嵌套 Grid 中：
      ```xml
      <Grid Grid.Column="2">
          <Grid.RowDefinitions>
              <RowDefinition Height="Auto"/>  <!-- 筛选工具栏行（约 2x 高度） -->
              <RowDefinition Height="*"/>     <!-- DataGrid 行 -->
          </Grid.RowDefinitions>
      ```
    - Row 0：筛选工具栏容器 Border（`x:Name="FilterBar"`，默认 `Visibility="Visible"`）：
      - Visibility 受工具栏 `ToggleFilterBarBtn` 的 IsChecked 控制
      - 水平布局：使用 `WrapPanel` 或 `Grid` 按比例分配
      - 包含以下 4 个过滤区域（用竖线 Separator 分隔）：

      **区域 1 — 文字搜索**（左侧，占用弹性宽度）：
      - 🔍 emoji TextBlock + TextBox (`x:Name="FileSearchBox"`)
      - 水印文本 `{l:L Main_FilterSearch_Placeholder}`（"筛选文件…"）

      **区域 2 — 日期范围**（竖线分隔后）：
      - 标签 "日期:" (TextBlock)
      - 两个 DatePicker (`x:Name="DateFromPicker"` / `x:Name="DateToPicker"`) **上下叠放**
      - 上边：开始日期；下边：结束日期

      **区域 3 — 大小范围**（竖线分隔后）：
      - 标签 "大小:" (TextBlock)
      - 两行**上下叠放**，每行：数字 TextBox + 单位 ComboBox (B/KB/MB/GB)
      - 上边：最小值 (`x:Name="SizeMinBox"` + `x:Name="SizeMinUnit"`)
      - 下边：最大值 (`x:Name="SizeMaxBox"` + `x:Name="SizeMaxUnit"`)

      **区域 4 — 清除全部**（最右侧）：
      - × 清除按钮 (`x:Name="ClearFiltersBtn"`)

    - Row 1：现有的 `<DataGrid x:Name="FileListGrid" .../>` 移入此 Grid 的 Row 1
    - 空结果提示：在嵌套 Grid 中添加 TextBlock `x:Name="NoResultsText"`（默认 Collapsed），居中显示 `{l:L Main_Filter_NoResults}`

  - 事件绑定：
    - `FileSearchBox.TextChanged` → 触发文字过滤
    - `DateFromPicker.SelectedDateChanged` / `DateToPicker.SelectedDateChanged` → 触发日期过滤
    - `SizeMinBox.TextChanged` / `SizeMaxBox.TextChanged` → 触发大小过滤
    - `SizeMinUnit.SelectionChanged` / `SizeMaxUnit.SelectionChanged` → 单位切换后触发重过滤
    - `ClearFiltersBtn.Click` → 清空所有过滤条件（文字、日期、大小），恢复完整列表
    - `FileSearchBox.PreviewKeyDown` → Escape 键细节：当焦点在 `FileSearchBox` 中时，Escape 仅清除文字搜索框内容（不清除日期/大小）；如果在其他区域按 Escape，则作为全局清除（全部重置）。行为与 Windows 搜索惯例一致。

  - 所有新控件仅在加载压缩包后可见（与 FileListPanel 的 Visibility 联动）
  - ⚠️ **嵌套布局兼容性**：此嵌套 Grid 放在 `InnerContentGrid` 的 Column 2 中。`InnerPreviewSplitter`（Grid.Row=1, ColumnSpan=5）和 `InnerPreviewRow`（Grid.Row=2, ColumnSpan=5）与该嵌套 Grid 共存在 `InnerContentGrid` 中——预览行和分隔线不受影响。不同预览位置（底部/右侧/树下方）下 filter bar 行为一致，无需特殊处理。

  **Must NOT do**:
  - 不修改 FileListGrid 的现有列定义
  - 不修改 TreeView 或 Preview 区域的布局
  - 不使用 MVVM 绑定（项目惯例是 code-behind）
  - 日期/大小过滤不改 ArchiveItem 的原始数据

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: none needed
  - Reason: WPF XAML UI 布局修改，含 DatePicker/ComboBox 等控件

  **Parallelization**:
  - **Can Run In Parallel**: YES（写 XAML + 事件注册时用占位处理，待 Task 3 完成后补充事件处理器体）
  - **Parallel Group**: Wave 2b (with Task 4)
  - **Blocks**: Task 6
  - **Blocked By**: Task 1, Task 3

  **References**:
  - `src/MantisZip.UI/MainWindow.xaml:273-454` — InnerContentGrid 布局，FileListGrid 定义
  - `src/MantisZip.UI/CompressSettingsWindow.xaml:198-201` — 参考 PwdSearchBox 的 TextBox 样式
  - `src/MantisZip.UI/MainWindow.UI.cs:362` — `FileListGrid.ItemsSource = sortedItems` 设置点
  - WPF DatePicker: `System.Windows.Controls.DatePicker` 内置控件

  **Acceptance Criteria**:
  - [ ] 筛选工具栏在文件列表上方渲染，包含 4 个区域（文字/日期/大小/清除）
  - [ ] 文字搜索框有水印"筛选文件…"，左侧 🔍 图标
  - [ ] 两个 DatePicker 上下叠放，可选择日期
  - [ ] 两个数字输入 + 单位 ComboBox（B/KB/MB/GB）上下叠放
  - [ ] 过滤组间有竖线分隔
  - [ ] × 清除按钮点击清除所有过滤条件
  - [ ] 空结果时居中显示"无匹配的文件"
  - [ ] `dotnet build` 无编译错误

  **QA Scenarios**:
  ```
  Scenario: 筛选工具栏 UI 构建验证
    Tool: Bash
    Preconditions: XAML 和代码已修改
    Steps:
      1. dotnet build
    Expected Result: 构建通过，无 XAML 解析错误
    Evidence: .sisyphus/evidence/task-5-filter-bar-ui.txt
  ```

  **Commit**: NO（与 Task 6 一起提交）

---

- [x] 6. 多维度过滤引擎

  **What to do**:
  - 设计过滤条件数据结构：
    ```csharp
    public record SearchFilters
    {
        public string? Text { get; init; }       // 文字搜索词
        public DateTime? DateFrom { get; init; } // 日期范围开始
        public DateTime? DateTo { get; init; }   // 日期范围结束
        public long? SizeMin { get; init; }      // 大小下限（字节）
        public long? SizeMax { get; init; }      // 大小上限（字节）
    }
    ```
  - 新增静态过滤方法（可提取到 `Core/Utils/ArchiveFilter.cs` 便于单元测试）：
    ```csharp
    public static List<ArchiveItem> ApplyFilters(
        IReadOnlyList<ArchiveItem> items, SearchFilters filters)
    ```
    - 文字过滤：`filters.Text` 为 null/空时跳过，否则检查所有列（同上）
    - 日期过滤：`filters.DateFrom` / `filters.DateTo`，与 `item.LastModified` 比较
    - 大小过滤：`filters.SizeMin` / `filters.SizeMax`，与 `item.Size` 比较
    - 三种过滤条件同时生效（AND 逻辑）
  - 在 `MainWindow.xaml.cs` 添加字段：
    - `private string? _searchText;`
    - `private DateTime? _dateFrom;` / `private DateTime? _dateTo;`
    - `private long? _sizeMin;` / `private long? _sizeMax;`
    - `private List<ArchiveItem>? _currentUnfilteredItems;` — `FilterFiles` 在 showSubfolders 处理和排序之后、过滤之前，把当前视图的完整列表存入此字段。`RefreshFilter()` 从它读取，避免重复 showSubfolders 的目录合成/去重逻辑
  - 单位换算辅助方法：
    - `ParseSizeWithUnit(string text, string unit) -> long?`：将"1.5" + "MB" → 1572864
    - 支持 B/KB/MB/GB 四个单位
    - ⚠️ **异常安全**：所有输入错误（空字符串、非数字文本、负值等）均返回 `null`（跳过该过滤条件），绝不抛异常。使用 `double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _)` 安全解析
  - 事件处理器（每个过滤控件的变化触发）：
    - `FileSearchBox.TextChanged` → 更新 `_searchText` → 调用 `RefreshFilter()`
    - `DateFromPicker.SelectedDateChanged` → 更新 `_dateFrom` → 调用 `RefreshFilter()`
    - `DateToPicker.SelectedDateChanged` → 更新 `_dateTo` → 调用 `RefreshFilter()`
    - `SizeMinBox.TextChanged` + `SizeMinUnit.SelectionChanged` → 更新 `_sizeMin` → 调用 `RefreshFilter()`
    - `SizeMaxBox.TextChanged` + `SizeMaxUnit.SelectionChanged` → 更新 `_sizeMax` → 调用 `RefreshFilter()`
    - `ClearFiltersBtn.Click` → 清空所有过滤字段、清空所有控件、调用 `RefreshFilter()`
  - `RefreshFilter()` 方法：
    - **数据来源**：统一从 `_currentUnfilteredItems` 读取（`FilterFiles` 已完成 showSubfolders 处理和排序，将结果存入此字段）
    - **不依赖 `FileListGrid.ItemsSource` 当前值**（避免排序被破坏时数据不一致）
    - 调用 `ApplyFilters(_currentUnfilteredItems, currentFilters)` 得到过滤列表
    - **在设置 ItemsSource 之前**完成过滤，确保排序逻辑在最终列表上执行
  - 集成到 `FilterFiles`：
    - **移除**原 `FilterFiles` 末尾的 `FileListGrid.ItemsSource = sortedItems;` + `ApplySavedSort()` + `Items.Refresh()`
    - **改为** `FilterFiles` 构建 `sortedItems` 后：
      1. `_currentUnfilteredItems = sortedItems;`（存下无过滤的完整列表）
      2. 末尾调用 `RefreshFilter()`（无参数），由它从 `_currentUnfilteredItems` 读取、决定是否应用过滤和设置 ItemsSource
    - 这样：UI 事件触发的 `RefreshFilter()` 也走同一路径（从 `_currentUnfilteredItems` 读），不再重复 showSubfolders 的目录合成/去重逻辑

  - **`DirStatsText` 优先级规则**（解决三套格式冲突）：
    ```
    hasSubfolders = _showSubfolders && !hasActiveFilters:
        「{total} 个文件（含子目录）」
    hasActiveFilters:
        L.TF(L.Main_Filter_StatsFormat, filteredCount, total)  // 显示 N/M 个文件
    default:
        L.TF(L.Main_DirStats, total, fileCount, dirCount)     // N 项 (文件 X, 目录 Y)
    ```
    优先级：过滤信息 > showSubfolders 信息 > 默认格式。过滤条件清空后自动降级。

  - **`ShowArchiveInfo` 保护**：过滤后无选中项时，**不**触发 `ShowArchiveInfo()`（避免覆盖用户当前预览）。仅在首次加载且无任何过滤条件时显示归档总览。

  **Must NOT do**:
  - 不修改 `_allItems` 的内容（仅过滤不修改）
  - 不向 ArchiveItem 类添加过滤相关属性
  - 不进行异步/延迟过滤（同步实时即可）
  - 不引入外部过滤库

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none needed

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Sequential (after Task 5)
  - **Blocks**: Task 8
  - **Blocked By**: Task 1, Task 3, Task 5

  **References**:
  - `src/MantisZip.UI/MainWindow.xaml.cs:40` — `_allItems` 字段
  - `src/MantisZip.UI/MainWindow.UI.cs:192-374` — FilterFiles 完整逻辑
  - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs:8` — ArchiveItem（.LastModified, .Size）
  - `src/MantisZip.UI/MainWindow.cs:FormatSize()` — 参考现有大小格式化

  **Acceptance Criteria**:
  - [ ] 文字搜索匹配文件名（"readme" → "readme.txt"）
  - [ ] 文字搜索大小写不敏感（"README" → "readme.txt"）
  - [ ] 文字搜索空字符串返回全部
  - [ ] 日期过滤：选开始日期后，只显示该日期之后的文件
  - [ ] 日期过滤：选结束日期后，只显示该日期之前的文件
  - [ ] 日期过滤：同时选开始+结束，显示区间内的文件
  - [ ] 大小过滤：设最小值为 1KB，只显示 >= 1KB 的文件
  - [ ] 大小过滤：设最大值为 1MB，只显示 <= 1MB 的文件
  - [ ] 组合过滤：文字 + 日期 + 大小同时生效
  - [ ] 清除全部按钮重置所有过滤条件
  - [ ] 切换目录时过滤条件保持且自动重新应用
  - [ ] `dotnet build` + 运行测试通过

  **QA Scenarios**:
  ```
  Scenario: 文字搜索按文件名过滤
    Tool: Bash（测试代码验证）
    Preconditions: CreateTestItems() 数据集
    Steps:
      1. 调用 ApplyFilters(items, new SearchFilters { Text = "helper" })
      2. 断言结果包含 "src/utils/helper.cs"
      3. 断言结果不包含 "readme.txt"
    Expected Result: 仅匹配文件名的条目保留
    Evidence: .sisyphus/evidence/task-6-text-filter.txt

  Scenario: 日期范围过滤
    Tool: Bash
    Preconditions: CreateTestItems()—包含多日期的文件
    Steps:
      1. 调用 ApplyFilters(items, new SearchFilters { DateFrom = 2026-01-01, DateTo = 2026-06-30 })
      2. 断言只有此区间内的文件被保留
    Expected Result: 日期过滤正确
    Evidence: .sisyphus/evidence/task-6-date-filter.txt

  Scenario: 大小范围过滤
    Tool: Bash
    Preconditions: CreateTestItems()—包含不同大小的文件
    Steps:
      1. 调用 ApplyFilters(items, new SearchFilters { SizeMin = 1000, SizeMax = 100000 })
      2. 断言只有此大小区间内的文件被保留
    Expected Result: 大小过滤正确
    Evidence: .sisyphus/evidence/task-6-size-filter.txt

  Scenario: 组合过滤（AND）
    Tool: Bash
    Preconditions: CreateTestItems()
    Steps:
      1. 调用 ApplyFilters(items, new SearchFilters { Text = "helper", SizeMin = 500 })
      2. 断言只有同时满足文字+大小的条目保留
    Expected Result: 组合 AND 过滤正确
    Evidence: .sisyphus/evidence/task-6-combined-filter.txt

  Scenario: 空过滤返回全部
    Tool: Bash
    Preconditions: CreateTestItems(), 共 8 个条目
    Steps:
      1. 调用 ApplyFilters(items, new SearchFilters())
      2. 断言结果数量 == 8
    Expected Result: 不设置任何过滤时返回全部
    Evidence: .sisyphus/evidence/task-6-empty-filters.txt
  ```

  **Commit**: YES（与 Task 5 一起）
  - Message: `feat(ui): add multi-dimensional file list filter bar (text, date range, size range)`
  - Files: `src/MantisZip.UI/MainWindow.xaml`, `src/MantisZip.UI/MainWindow.xaml.cs`, `src/MantisZip.UI/MainWindow.UI.cs`, `Core/Utils/ArchiveFilter.cs`
  - Pre-commit: `dotnet build`

---

- [x] 7. 单元测试 — FilterFiles showSubfolders 模式

  **What to do**:
  - 在 `FileListFilterTests.cs` 中添加测试方法，使用 `CreateTestItems()` 数据集
  - 测试用例：
    - `FilterFiles_Root_ShowSubfolders_ReturnsAllFiles`: 根目录扁平模式下，返回所有非目录条目（不含合成目录）
    - `FilterFiles_Subdir_ShowSubfolders_ReturnsNestedFiles`: 子目录 `src/` 扁平模式下，返回 `src/main.cs` 和 `src/utils/helper.cs`
    - `FilterFiles_ShowSubfolders_ExcludesDirectories`: 扁平模式下不包含任何目录条目
    - `FilterFiles_ShowSubfolders_DisplayName_RelativePath`: 子目录扁平模式下，DisplayName 为相对于当前目录的路径
    - `FilterFiles_Root_DefaultMode_ReturnsDirectories`: 默认模式下根目录包含合成目录
  - 注意：`FilterFiles` 是 MainWindow 的实例方法且依赖 UI 状态。如果难以直接实例化，可以对过滤逻辑进行**提取和单元测试**：
    - 提取静态辅助方法到 `MainWindow.UI.cs` 或新建静态工具类
    - 推荐：将扁平模式的过滤逻辑提取为静态方法 `FilterItemsForDisplay(IReadOnlyList<ArchiveItem> allItems, string folderPath, bool showSubfolders)`

  **Must NOT do**:
  - 不测试 UI 部分（ToggleButton 交互等）
  - 不依赖 WPF UI 线程

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none needed

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Task 8)
  - **Blocks**: F1-F4
  - **Blocked By**: Task 2, Task 3

  **References**:
  - `tests/MantisZip.Tests/FileListFilterTests.cs` — 测试文件（Task 2 创建）
  - `src/MantisZip.UI/MainWindow.UI.cs:192-374` — FilterFiles 逻辑

  **Acceptance Criteria**:
  - [ ] 5 个测试用例全部实现并通过
  - [ ] `dotnet test tests/MantisZip.Tests/` → ALL PASS
  - [ ] 测试不依赖 UI 线程

  **QA Scenarios**:
  ```
  Scenario: 所有扁平模式测试通过
    Tool: Bash
    Preconditions: FileListFilterTests.cs 包含测试方法
    Steps:
      1. dotnet test tests/MantisZip.Tests/ --filter "FullyQualifiedName~ShowSubfolders"
    Expected Result: 5 passed, 0 failed
    Evidence: .sisyphus/evidence/task-7-subfolder-tests.txt
  ```

  **Commit**: YES（与 Task 2、3、8 一起提交→Commit 2）
  - Message: (see Commit Strategy table)
  - Files: `tests/MantisZip.Tests/FileListFilterTests.cs`, `src/MantisZip.UI/MainWindow.UI.cs`
  - Pre-commit: `dotnet test tests/MantisZip.Tests/`

---

- [x] 8. 单元测试 — 多维度过滤引擎

  **What to do**:
  - 在 `FileListFilterTests.cs` 中添加测试方法，覆盖三类过滤
  - **文字搜索测试**（使用 `SearchFilters { Text = "..." }`）：
    - `Filter_Text_ByName`: 搜索 "helper" 匹配文件
    - `Filter_Text_CaseInsensitive`: 搜索 "README" 匹配 "readme.txt"
    - `Filter_Text_PartialMatch`: 搜索 "main" 匹配 "main.cs"
    - `Filter_Text_NoMatch_ReturnsEmpty`: 搜索 "nonexistent" 返回空
  - **日期测试**（使用 `SearchFilters { DateFrom / DateTo }`）：
    - `Filter_Date_After`: DateFrom 设置后只返回之后的文件
    - `Filter_Date_Before`: DateTo 设置后只返回之前的文件
    - `Filter_Date_Range`: DateFrom + DateTo 区间过滤
    - `Filter_Date_NoMatch`: 日期区间不包含任何文件时返回空
  - **大小测试**（使用 `SearchFilters { SizeMin / SizeMax }`）：
    - `Filter_Size_Min`: 最小大小过滤
    - `Filter_Size_Max`: 最大大小过滤
    - `Filter_Size_Range`: 大小区间过滤
    - `Filter_Size_UnitConversion`: 单位转换正确性（1KB=1024）
  - **组合测试**：
    - `Filter_Combined_TextAndDate`: 文字 + 日期 AND
    - `Filter_Combined_AllThree`: 文字 + 日期 + 大小 AND
  - **空过滤**：
    - `Filter_Empty_ReturnsAll`: 无任何过滤条件返回全部

  **Must NOT do**:
  - 不依赖 WPF 控件

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none needed

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Task 7)
  - **Blocks**: F1-F4
  - **Blocked By**: Task 2, Task 6

  **References**:
  - `tests/MantisZip.Tests/FileListFilterTests.cs` — 测试文件
  - 过滤引擎中定义的 `SearchFilters` record 和 `ApplyFilters` 方法

  **Acceptance Criteria**:
  - [ ] 至少 12 个测试用例（文字 4 + 日期 4 + 大小 4 + 组合 2 + 空 1 = 15）
  - [ ] `dotnet test tests/MantisZip.Tests/` → ALL PASS
  - [ ] 所有测试不依赖 UI 线程

  **QA Scenarios**:
  ```
  Scenario: 所有过滤引擎测试通过
    Tool: Bash
    Preconditions: FileListFilterTests.cs 包含所有测试方法
    Steps:
      1. dotnet test tests/MantisZip.Tests/ --filter "FullyQualifiedName~Filter_"
    Expected Result: 15 passed, 0 failed
    Evidence: .sisyphus/evidence/task-8-all-filter-tests.txt
  ```

  **Commit**: YES（与 Task 2、3、7 一起提交→Commit 2；不随 Task 5、6 提交，避免文件冲突）
  - Message: (see Commit Strategy table)
  - Files: `tests/MantisZip.Tests/FileListFilterTests.cs`, `Core/Utils/ArchiveFilter.cs`
  - Pre-commit: `dotnet test tests/MantisZip.Tests/`

---

## Final Verification Wave

- [x] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists (read file, run command). For each "Must NOT Have": search codebase for forbidden patterns. Check evidence files exist in .sisyphus/evidence/. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [x] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build` + `dotnet test`. Review all changed files for: empty catches, console.log in prod, commented-out code, unused imports.
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | Files [N clean/N issues] | VERDICT`

- [x] F3. **Real Manual QA** — 手动启动应用 / Bash
  Start from clean state. Execute EVERY QA scenario from EVERY task. Test cross-task integration (toggle + search working together). Test edge cases: toggle while searching, search then toggle, rapid typing.
  Output: `Scenarios [N/N pass] | Integration [N/N] | Edge Cases [N tested] | VERDICT`

- [x] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff. Verify 1:1 — everything in spec was built, nothing beyond spec. Check "Must NOT do" compliance.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | Unaccounted [CLEAN/N files] | VERDICT`

---

## 扩展功能：筛选值吸管（Pick Filter Value）

### TL;DR

> **Quick Summary**: 在筛选工具栏的日期区间（各 DatePicker）和大小区间（各 Size TextBox）前面，添加吸管 🧪 按钮。点击从当前文件列表选中条目自动提取对应的值填入控件，支持多选时自动取极值（min/max）。
>
> **Design Change Summary**:
> - 布局调整：日期区 `📅` + 大小区 `📏` 替代原有文字标签，位置从上方改到左侧水平排列
> - 新控件：4 个吸管按钮（PickDateFrom/PickDateTo/PickSizeMin/PickSizeMax）
> - 无选择或选中均为目录时，对应吸管按钮禁用 + 视觉区分

### 最终布局

```
◀── 区域 1 ──▶ | ◀────────── 区域 2 ─────────▶ | ◀────────── 区域 3 ─────────▶ | ◀─ 4 ─▶
🔍 [FileSearchBox] | 📅 [🧪] 从 [DateFromPicker] | 📏 [🧪] 最小 [SizeMinBox] [B] | ✕
                      [🧪] 到 [DateToPicker]       [🧪] 最大 [SizeMaxBox] [B]
```

### 布局变更

#### 日期区（当前 → 新）

```
当前:                         新:
┌──────────────┐             ┌─────────────────────┐
│ 日期:        │             │ 📅                   │
│ 从 [DP]      │      →      │  [🧪] 从 [DP]        │
│ 到 [DP]      │             │  [🧪] 到 [DP]        │
└──────────────┘             └─────────────────────┘
```

- 外层从 `Vertical` → `Horizontal`（📅 在左，控件列在右）
- 每行：`[吸管] "从"/"到" [DatePicker]`

#### 大小区（当前 → 新）

```
当前:                         新:
┌──────────────┐             ┌──────────────────────────┐
│ 大小:        │             │ 📏                        │
│ 最小 [B]     │      →      │  [🧪] 最小 [SizeMin] [B] │
│ 最大 [B]     │             │  [🧪] 最大 [SizeMax] [B] │
└──────────────┘             └──────────────────────────┘
```

### 吸管行为规则

#### 取值逻辑

| 吸管按钮 | 选中文件数 | 取值方式 |
|----------|-----------|----------|
| `PickDateFromBtn`（日期开始） | = 0 | **禁用** |
| | = 1 | 取该文件的 `LastModified` |
| | ≥ 2 | 取这些文件中 `LastModified` 的**最小值** |
| `PickDateToBtn`（日期结束） | = 0 | **禁用** |
| | = 1 | 取该文件的 `LastModified` |
| | ≥ 2 | 取这些文件中 `LastModified` 的**最大值** |
| `PickSizeMinBtn`（大小最小） | = 0 | **禁用** |
| | = 1 | 取该文件的 `Size` |
| | ≥ 2 | 取这些文件中 `Size` 的**最小值** |
| `PickSizeMaxBtn`（大小最大） | = 0 | **禁用** |
| | = 1 | 取该文件的 `Size` |
| | ≥ 2 | 取这些文件中 `Size` 的**最大值** |

#### 过滤规则

- **目录日期排除**：计算日期极值时，跳过 `IsDirectory == true` 的条目（目录 LastModified 无意义或为 MinValue）
- **目录大小包含**：计算大小极值时，**包含**目录条目（目录在压缩包中具有 `Size` = 子文件大小总和，是有意义的值）
- **加密条目包含**：加密条目正常参与取值（`LastModified` 和 `Size` 对加密条目仍然可读）
- **LastModified 无效值排除**：`LastModified == DateTime.MinValue` 的条目不参与日期极值计算
- **纯目录选择**：如果选中条目全是目录：
  - 日期吸管全部**禁用**（目录没有有效修改时间可吸取）
  - 大小吸管**可用**（目录 Size 有值，可以取 min/max）

#### 禁用状态

| 条件 | `PickDateFromBtn` | `PickDateToBtn` | `PickSizeMinBtn` | `PickSizeMaxBtn` |
|------|:---:|:---:|:---:|:---:|
| 未选中任何条目 | 🚫 | 🚫 | 🚫 | 🚫 |
| 选中全是目录 | 🚫 | 🚫 | ✅ | ✅ |
| 选中全是 Date=MinValue | 🚫 | 🚫 | ✅ | ✅ |
| 正常选中 | ✅ | ✅ | ✅ | ✅ |

- 禁用时：`IsEnabled = false`，系统默认的灰色显示即可
- `FileListGrid_SelectionChanged` 事件中更新所有 4 个吸管按钮的启用状态

#### 取值后的效果

- 点击吸管后，对应控件的值**立即填入**，触发对应的 `TextChanged` / `SelectedDateChanged` 事件，自动应用过滤
- 如果该控件的值已经与吸管结果相同，则不触发重复过滤（避免 UI 抖动）
- 清除按钮（✕）可一键清空过滤条件，吸管填入的值也会被清除

### 本地化

**新增本地化键**（可选，吸管按钮用 ToolTip 说明功能）：

| Key | zh | en |
|-----|----|----|
| `Main_Filter_PickDateFrom` | 从选中文件取最小日期 | Pick min date from selection |
| `Main_Filter_PickDateTo` | 从选中文件取最大日期 | Pick max date from selection |
| `Main_Filter_PickSizeMin` | 从选中文件取最小大小 | Pick min size from selection |
| `Main_Filter_PickSizeMax` | 从选中文件取最大大小 | Pick max size from selection |
| `Main_Filter_NoFileSelected` | 请先选择文件 | Select files first |

**移除/废弃的键**：

| Key | 说明 |
|-----|------|
| `Main_Filter_DateLabel` | "日期:" 文字不再需要，改为 📅 emoji |
| `Main_Filter_SizeLabel` | "大小:" 文字不再需要，改为 📏 emoji |

> 注意：这两个键保留在 JSON 中不删除，避免破坏已有引用。可以标记为 `// TODO: 废弃` 注释，后续统一清理。

### 实现任务

- [x] T9. **布局调整 + 吸管按钮 UI** — XAML 中修改日期/大小区外层 Orientation，替换文字为 emoji，添加 4 个吸管按钮
- [x] T10. **吸管按钮 Click 事件** — 实现取值逻辑（单文件填值 / 多文件极值 / 目录跳过 / 无效日期跳过）
- [x] T11. **吸管按钮启用状态联动** — `FileListGrid_SelectionChanged` 中更新 4 个按钮的状态
- [x] T12. **单元测试** — 吸管取值逻辑测试（单文件、多文件 min/max、纯目录选择、混合选择）

### 依赖

- 依赖现有 `FileListGrid` 的 `SelectedItems`（已有多选支持）
- 不依赖额外 NuGet 包
- 不修改 Core 层逻辑

---

## Commit Strategy

| Commit | Tasks | Message |
|--------|-------|---------|
| 1 | 1 | `feat(l10n): add file list search/filter localization strings` |
| 2 | 2, 3, 7, 8 | `feat(core): add file list filtering (showSubfolders + multi-dim filter) with tests` |
| 3 | 4 | `feat(ui): add toolbar toggle buttons (show subfolders + toggle filter bar)` |
| 4 | 5, 6 | `feat(ui): add multi-dimensional file list filter bar (text, date range, size range)` |

---

## Success Criteria

### Verification Commands
```bash
dotnet build  # Expected: Build succeeded (0 errors)
dotnet test tests/MantisZip.Tests/  # Expected: All tests pass (0 failures)
```

### Final Checklist
- [x] 「显示所有子目录文件」ToggleButton（🌲）在主工具栏工作正常
- [x] 「切换筛选栏」ToggleButton（🔍）在主工具栏工作正常，显隐切换正确
- [x] 筛选工具栏显示在文件列表上方，包含文字/日期/大小三个过滤区
- [x] 文字搜索实时过滤，大小写不敏感
- [x] 日期范围 DatePicker × 2 过滤正确
- [x] 大小范围数字输入 + 单位 ComboBox 过滤正确
- [x] 三种过滤组合（AND）工作正常
- [x] 清除全部重置所有过滤条件
- [x] 显示所有子目录 + 过滤同时使用时互不冲突
- [x] 切换目录时所有过滤条件保持
- [x] 所有本地化字符串正确显示
- [x] 所有单元测试通过
- [x] 构建无错误
