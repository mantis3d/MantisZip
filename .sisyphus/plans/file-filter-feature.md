# 文件过滤功能

> 在压缩与解压时，支持按条件过滤文件：文件类型、文件名、文件大小、修改日期。
> **前置依赖**：SharpCompress 引擎迁移完成（`.sisyphus/plans/engine-unification-sharpcompress.md`），`IArchiveEngine.ExtractEntriesAsync` 接口已就绪。

---

## 前置条件

必须完成以下迁移计划中的任务：

- [ ] Phase 1-3: SharpCompress 迁移（TarGzEngine + ZipEngine + ArchiveEntryExtractor）
- [ ] Phase 3.4: `IArchiveEngine` 新增 `ExtractEntriesAsync` 方法完成
- [ ] 各引擎 `ArchiveItem.Name` 编码一致性已验证

---

## Context

### Original Request
压缩与解压时选择只处理符合要求的文件：只要音频、文件名带某个单词、大小满足范围、日期满足范围。

### Interview Summary
- **应用范围**：压缩 + 解压都需要
- **预设系统**：既支持临时设定，也能保存常用规则为预设
- **UI 风格**：嵌入现有设置对话框（`CompressSettingsDialog`），解压时弹窗
- **实现顺序**：先完成 SharpCompress 迁移，再实施本计划

### Architecture Dependencies
- **Core**：新增 `FileFilterCriteria` / `FileFilterMatcher` / `FileFilterPreset`
- **UI**：新增 `FileFilterEditor` 用户控件
- **AppSettings**：新增 `FilterPresets` 列表
- **Compress**：钩入 `CompressSettingsDialog` + 4 个 `RunCompress*` 入口
- **Extract**：依赖 `IArchiveEngine.ExtractEntriesAsync`，钩入提取入口

---

## Work Objectives

### Core Objective
在压缩和解压流程中插入可配置的文件过滤步骤，支持按扩展名、文件名模式、大小范围、日期范围筛选。

### Concrete Deliverables
- Core: `FileFilterCriteria` 数据模型
- Core: `FileFilterMatcher` 匹配逻辑（文件 + ArchiveItem）
- Core/UI: `FileFilterPreset` 预设模型
- UI: `FileFilterEditor` 用户控件（扩展名、文件名、大小、日期、预设）
- UI: `CompressSettingsDialog` 嵌入过滤面板
- UI: 提取前过滤对话框（独立弹窗）
- 所有压缩入口：应用过滤器
- 所有提取入口：应用过滤器 + `ExtractEntriesAsync`
- `AppSettings` 预设持久化

### Must Have
- [ ] 四种过滤维度全部可用（扩展名、文件名、大小、日期）
- [ ] 预设可保存和加载
- [ ] 压缩时对所选文件/目录应用过滤
- [ ] 解压时对压缩包内条目应用过滤
- [ ] 过滤器默认不激活（不干扰现有操作）

### Must NOT Have
- 不做正则表达式匹配（只用通配符 `*` `?`，降低用户认知负担）
- 不做文件内容过滤（只基于文件元数据）
- 不做过滤器组合逻辑（AND 逻辑，不做 OR/NOT）
- 目录本身不过滤，只过滤目录内的文件（目录保留结构）

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: NO (no test project)
- **Automated tests**: NO
- **Framework**: none
- **Agent-Executed QA**: ALWAYS

### QA Policy
- **Logic tests** (Core): Use `dotnet script` / REPL to instantiate `FileFilterMatcher`, call `IsMatch` with test data, assert results
- **UI tests**: Build + launch app, verify dialog renders correctly
- **Integration tests**: Create test files, compress with filter, verify archive contents; extract with filter, verify output

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Core data model — no dependencies):
├── Task 1: FileFilterCriteria data model
├── Task 2: FileFilterMatcher matching logic
├── Task 3: FileFilterPreset model + AppSettings integration
└── Task 4: Unit validation (manual script-based)

Wave 2 (UI control — depends on Wave 1):
├── Task 5: FileFilterEditor XAML layout
├── Task 6: FileFilterEditor code-behind (logic + preset management)
└── Task 7: Embed in CompressSettingsDialog

Wave 3 (Compress integration — depends on Wave 1 + 2):
├── Task 8: Extract filter helper (FileFilterHelper.FilterFiles)
├── Task 9: Hook into --compress (settings dialog flow)
├── Task 10: Hook into --compress-quick / --compress-separate / --compress-combined
└── Task 11: Directory recursion + filtering

Wave 4 (Extract integration — depends on Wave 1 + 2):
├── Task 12: Extract filter dialog
├── Task 13: Hook into extract handlers (here / to-name / smart / to)
└── Task 14: Integration test: filter + ExtractEntriesAsync

Wave FINAL (Verification):
├── Task F1: Plan compliance audit
├── Task F2: Code quality review
├── Task F3: Real manual QA (all filter dimensions + presets)
└── Task F4: Scope fidelity check
```

### Critical Path
Task 1 → Task 2 → Task 5 → Task 6 → Task 7 → Task 9 → Task 12 → Task 13 → F1-F4

---

## TODOs

- [ ] 1. `FileFilterCriteria` 数据模型

  **What to do**:
  - 新建 `src/MantisZip.Core/FileFilter/FileFilterCriteria.cs`
  - 字段：
    - `IncludeExtensions: List<string>` — 包含的扩展名（`".mp3", ".wav"`），空 = 全部
    - `ExcludeExtensions: List<string>` — 排除的扩展名
    - `NamePattern: string?` — 文件名通配符（`*报告*`），null = 不限制
    - `MinSize: long?` / `MaxSize: long?` — 字节，null = 不限制
    - `MinDate: DateTime?` / `MaxDate: DateTime?` — null = 不限制
  - `bool IsActive` 属性：至少一个条件非空/非默认值
  - `string DisplaySummary` 属性：人类可读的过滤条件摘要（用于 UI 显示）

  **Must NOT do**:
  - 不要正则表达式支持
  - 不要 OR/NOT 组合逻辑

  **Recommended Agent Profile**:
  - Category: `quick`
  - Skills: `[]`

  **Parallelization**:
  - Can Run In Parallel: YES
  - Parallel Group: Wave 1
  - Blocked By: None

  **Acceptance Criteria**:
  - [ ] File created at correct path
  - [ ] All 4 filter dimensions implemented
  - [ ] `IsActive` returns false when all filters are default/empty
  - [ ] `IsActive` returns true when any filter is set
  - [ ] `DisplaySummary` returns non-empty string when active

  **QA Scenarios**:
  ```
  Scenario: IsActive correctly reflects filter state
    Tool: Bash (dotnet script / csharp REPL)
    Preconditions: FileFilterCriteria compiled
    Steps:
      1. Create new FileFilterCriteria with no filters set
      2. Assert IsActive == false
      3. Set IncludeExtensions to [".mp3"]
      4. Assert IsActive == true
    Expected Result: All assertions pass
    Evidence: .sisyphus/evidence/task-1-isactive.txt

  Scenario: DisplaySummary returns readable text
    Tool: Bash
    Preconditions: FileFilterCriteria compiled
    Steps:
      1. Create filter with NamePattern="*报告*", MinSize=1024
      2. Assert DisplaySummary contains "报告" and "1 KB"
    Expected Result: DisplaySummary is non-empty and descriptive
    Evidence: .sisyphus/evidence/task-1-summary.txt
  ```

  **Commit**: YES
  - Message: `feat(core): add FileFilterCriteria data model`
  - Files: `src/MantisZip.Core/FileFilter/FileFilterCriteria.cs`

- [ ] 2. `FileFilterMatcher` 匹配逻辑

  **What to do**:
  - 新建 `src/MantisZip.Core/FileFilter/FileFilterMatcher.cs`
  - `static bool IsMatch(FileFilterCriteria filter, string filePath)` — 对文件系统路径匹配
    - 扩展名匹配：`IncludeExtensions` 包含则通过，`ExcludeExtensions` 包含则排除
    - 文件名模式：`NamePattern` 用 `Path.GetFileNameWithoutExtension` 匹配通配符（`*` 和 `?`）
    - 大小：`new FileInfo(path).Length` 比较
    - 日期：`FileInfo.LastWriteTime` 比较
  - `static bool IsMatch(FileFilterCriteria filter, ArchiveItem entry)` — 对压缩包条目匹配
    - 扩展名：`Path.GetExtension(entry.Name)`
    - 文件名：`Path.GetFileNameWithoutExtension(entry.Name)`
    - 大小：`entry.Size`
    - 日期：`entry.LastModified`
  - 扩展名比较统一小写
  - 所有条件 AND 逻辑（全部满足才返回 true）

  **Must NOT do**:
  - 不要依赖 `ShellIntegration.cs` 或其他 UI 层
  - 不要文件 I/O 副作用（纯函数）

  **References**:
  - Core/FileFilter/FileFilterCriteria.cs (Task 1) — 数据模型

  **Recommended Agent Profile**:
  - Category: `quick`
  - Skills: `[]`

  **Parallelization**:
  - Can Run In Parallel: YES
  - Parallel Group: Wave 1
  - Blocked By: Task 1

  **Acceptance Criteria**:
  - [ ] `IsMatch(filePath, filter)` correctly filters by extension, name, size, date
  - [ ] `IsMatch(entry, filter)` correctly filters ArchiveItem
  - [ ] Empty filter (all defaults) matches everything
  - [ ] Extension comparison is case-insensitive

  **QA Scenarios**:
  ```
  Scenario: Extension filter works for file path
    Tool: Bash (dotnet script)
    Preconditions: Test files exist (.mp3, .txt, .jpg)
    Steps:
      1. Create filter with IncludeExtensions=[".mp3"]
      2. Assert IsMatch(filter, "test.mp3") == true
      3. Assert IsMatch(filter, "test.txt") == false
    Expected Result: Only .mp3 matches
    Evidence: .sisyphus/evidence/task-2-ext-filter.txt

  Scenario: Name pattern with wildcard
    Tool: Bash
    Preconditions: None
    Steps:
      1. Create filter with NamePattern="*report*"
      2. Assert IsMatch(filter, "monthly_report_2024.xlsx") == true
      3. Assert IsMatch(filter, "readme.txt") == false
    Expected Result: Name pattern matches correctly
    Evidence: .sisyphus/evidence/task-2-name-filter.txt

  Scenario: Size range filter
    Tool: Bash
    Preconditions: None
    Steps:
      1. Create filter with MinSize=1000, MaxSize=5000
      2. Assert IsMatch(filter, small.txt (500 bytes)) == false
      3. Assert IsMatch(filter, medium.txt (2000 bytes)) == true
      4. Assert IsMatch(filter, large.txt (10000 bytes)) == false
    Expected Result: Size range respected
    Evidence: .sisyphus/evidence/task-2-size-filter.txt
  ```

  **Commit**: YES (groups with 1)
  - Message: `feat(core): add FileFilterMatcher matching logic`
  - Files: `src/MantisZip.Core/FileFilter/FileFilterMatcher.cs`

- [ ] 3. `FileFilterPreset` + `AppSettings` 持久化

  **What to do**:
  - 新建 `src/MantisZip.Core/FileFilter/FileFilterPreset.cs`
    - `string Name` — 预设名称
    - `FileFilterCriteria Criteria` — 过滤条件
  - 修改 `AppSettings.cs`：
    - 新增 `List<FileFilterPreset> FilterPresets { get; set; }`，默认空列表
    - 新增 `string? ActiveFilterPresetName { get; set; }`，默认 null
  - `AppSettings.SaveSettings()` / `LoadSettings()` 中序列化/反序列化预设
  - 预设数量上限 20 个，防止 settings.json 膨胀

  **Must NOT do**:
  - 不要修改预设的持久化格式（用 JSON 数组，与现有 settings.json 保持一致）

  **References**:
  - UI/AppSettings.cs — 现有设置模型

  **Recommended Agent Profile**:
  - Category: `quick`
  - Skills: `[]`

  **Parallelization**:
  - Can Run In Parallel: YES
  - Parallel Group: Wave 1
  - Blocked By: Task 1

  **Acceptance Criteria**:
  - [ ] Preset serializes/deserializes correctly in settings.json
  - [ ] Empty presets list works
  - [ ] Max 20 presets enforced

  **QA Scenarios**:
  ```
  Scenario: Preset persists across settings save/load
    Tool: Bash
    Preconditions: AppSettings loaded
    Steps:
      1. Add preset with name "Audio Only", filter IncludeExtensions=[".mp3", ".wav"]
      2. SaveSettings()
      3. Reload settings
      4. Assert FilterPresets[0].Name == "Audio Only"
      5. Assert FilterPresets[0].Criteria.IncludeExtensions contains ".mp3"
    Expected Result: Preset survives round-trip
    Evidence: .sisyphus/evidence/task-3-preset-persistence.txt
  ```

  **Commit**: YES (groups with 1)
  - Message: `feat(ui): add FileFilterPreset and AppSettings persistence`
  - Files:
    - `src/MantisZip.Core/FileFilter/FileFilterPreset.cs`
    - `src/MantisZip.UI/AppSettings.cs`

- [ ] 4. `FileFilterEditor` 用户控件 — XAML

  **What to do**:
  - 新建 `src/MantisZip.UI/FileFilterEditor.xaml` + `FileFilterEditor.xaml.cs`
  - XAML 布局：
    - **预设栏**：预设下拉框 + 保存按钮 + 删除按钮
    - **扩展名区**：常用类型勾选框（音频、视频、图片、文档、压缩包）+ 自定义扩展名输入
    - **文件名区**：文件名模式输入框（提示：支持 `*` 和 `?` 通配符）
    - **大小区**：最小值 + 最大值 + 单位选择（B/KB/MB/GB）
    - **日期区**：起始日期 + 结束日期（DatePicker）
    - 顶部"启用过滤"开关（ToggleSwitch），默认关闭
    - 启用时控件可编辑，禁用时灰色
  - 使用 `Expander` 或 `GroupBox` 分组，避免视觉杂乱

  **Must NOT do**:
  - 不要硬编码字符串，用 `{l:L ...}` 本地化，key 前缀 `FileFilter_*`

  **Parallelization**:
  - Can Run In Parallel: NO (sequential with Task 5)
  - Parallel Group: Wave 2
  - Blocked By: Task 1, Task 3 (for preset data binding)

  **References**:
  - UI/CompressConflictDialog.xaml — 现有对话框样式参考
  - UI/SettingsWindow.xaml — 设置面板布局参考

  **Acceptance Criteria**:
  - [ ] XAML compiles without errors
  - [ ] All 4 filter sections rendered
  - [ ] Preset dropdown functional (bindable)
  - [ ] Enable toggle disables all controls when off

  **Commit**: YES
  - Message: `feat(ui): add FileFilterEditor user control XAML`
  - Files:
    - `src/MantisZip.UI/FileFilterEditor.xaml`
    - `src/MantisZip.UI/FileFilterEditor.xaml.cs`

- [ ] 5. `FileFilterEditor` 控件逻辑 — 代码后端

  **What to do**:
  - 实现 `FileFilterEditor` code-behind：
    - `FileFilterCriteria GetFilter()` — 从 UI 控件读取当前过滤条件
    - `void SetFilter(FileFilterCriteria filter)` — 将过滤条件填入 UI
    - `void LoadPresets(List<FileFilterPreset> presets)` — 填充预设下拉框
    - `FileFilterPreset? SelectedPreset` — 当前选中的预设
    - `event Action? FilterChanged` — 过滤条件变更时通知父窗口
    - `bool IsFilterEnabled` — 绑定到顶部开关
  - 扩展名预设：
    - 音频: `.mp3, .wav, .flac, .aac, .ogg, .wma`
    - 视频: `.mp4, .avi, .mkv, .mov, .wmv, .flv`
    - 图片: `.jpg, .jpeg, .png, .gif, .bmp, .webp, .svg`
    - 文档: `.pdf, .doc, .docx, .xls, .xlsx, .ppt, .pptx, .txt`
    - 压缩包: `.zip, .7z, .rar, .tar, .gz, .tgz`
  - 自定义扩展名输入支持逗号分隔，自动加 `.` 前缀

  **Must NOT do**:
  - 不要直接操作 AppSettings（通过属性/事件让父窗口处理）

  **References**:
  - Core/FileFilter/FileFilterCriteria.cs — 数据模型
  - Core/FileFilter/FileFilterPreset.cs — 预设模型
  - UI/FileFilterEditor.xaml (Task 4) — XAML

  **Parallelization**:
  - Can Run In Parallel: NO (depends on XAML)
  - Parallel Group: Wave 2
  - Blocked By: Task 4

  **Acceptance Criteria**:
  - [ ] GetFilter/SetFilter round-trips all 4 dimensions correctly
  - [ ] Preset dropdown populates, selecting loads criteria
  - [ ] Enable toggle disables/enables all input controls
  - [ ] Custom extension input parses correctly

  **Commit**: YES (groups with 4)
  - Message: `feat(ui): implement FileFilterEditor logic`
  - Files: `src/MantisZip.UI/FileFilterEditor.xaml.cs`

- [ ] 6. 嵌入 `CompressSettingsDialog`

  **What to do**:
  - 修改 `CompressSettingsDialog.xaml`：
    - 在现有选项下方添加 `FileFilterEditor` 控件
    - 用 `Expander` 包裹，标题为"文件过滤条件"（本地化 key `Compress_Filter`）
  - 修改 `CompressSettingsDialog.xaml.cs`：
    - 构造函数中加载预设到 `FileFilterEditor`
    - `CompressSettingsDialog_Loaded` 或等效事件中恢复上次使用的过滤状态
    - 对话框确认时读取 `FileFilterEditor.GetFilter()` 存入 `AppSettings.LastCompressFilter`
  - 确保现有布局不受影响（Expander 默认折叠）

  **Must NOT do**:
  - 不要改变现有对话框的布局/行为（Expander 默认折叠，不影响不使用的用户）

  **References**:
  - UI/FileFilterEditor.xaml (Task 4-5)
  - UI/CompressSettingsDialog.xaml

  **Parallelization**:
  - Can Run In Parallel: NO
  - Parallel Group: Wave 3
  - Blocked By: Task 5

  **Acceptance Criteria**:
  - [ ] CompressSettingsDialog shows filter expander at bottom
  - [ ] Expander default collapsed
  - [ ] Filter state persists across dialog open/close
  - [ ] Build succeeds with 0 errors

  **Commit**: YES
  - Message: `feat(ui): embed FileFilterEditor in CompressSettingsDialog`
  - Files:
    - `src/MantisZip.UI/CompressSettingsDialog.xaml`
    - `src/MantisZip.UI/CompressSettingsDialog.xaml.cs`

- [ ] 7. 压缩过滤 Helper 函数

  **What to do**:
  - 在 UI 层新增 `FileFilterHelper` 静态类：
    - `static string[] ApplyFilter(string[] paths, FileFilterCriteria? filter)` — 应用过滤到路径数组
    - 如果 filter 为 null 或 `!filter.IsActive`，直接返回原数组
    - 对于目录路径：递归枚举目录内所有文件，分别匹配过滤条件
    - 匹配的文件保留相对路径结构
    - `static bool ShouldInclude(string path, FileFilterCriteria filter)` — 单文件判断

  **Must NOT do**:
  - 不要在 Core 层实现目录递归（Core 的匹配逻辑只处理单文件）

  **References**:
  - Core/FileFilter/FileFilterCriteria.cs
  - Core/FileFilter/FileFilterMatcher.cs

  **Parallelization**:
  - Can Run In Parallel: NO
  - Parallel Group: Wave 3
  - Blocked By: Task 1, Task 2

  **Acceptance Criteria**:
  - [ ] Null filter returns all paths unchanged
  - [ ] Inactive filter returns all paths unchanged
  - [ ] Active filter correctly filters file paths
  - [ ] Directory path is recursively enumerated and filtered

  **Commit**: YES
  - Message: `feat(ui): add FileFilterHelper for compress filtering`
  - Files: `src/MantisZip.UI/FileFilterHelper.cs`

- [ ] 8. 压缩入口集成 — `--compress` 对话模式

  **What to do**:
  - 修改 `HandleCompress` / `ShowCompressWindow` 在 App.xaml.cs 中
  - `ShowCompressWindow` 中：对话框确认后，读取 `AppSettings.LastCompressFilter`
  - 调用 `FileFilterHelper.ApplyFilter(allPaths, filter)` 过滤路径
  - 过滤后的路径传入 `CompressAsync`

  **Must NOT do**:
  - 不要修改 `IArchiveEngine.CompressAsync` 签名（过滤在调用前完成）

  **References**:
  - UI/App.xaml.cs — HandleCompress / ShowCompressWindow
  - UI/FileFilterHelper.cs (Task 7)

  **Parallelization**:
  - Can Run In Parallel: NO
  - Parallel Group: Wave 3
  - Blocked By: Task 6, Task 7

  **Acceptance Criteria**:
  - [ ] Filter applied before compression
  - [ ] No filter = original behavior unchanged

  **Commit**: YES (groups with 9, 10)
  - Message: `feat(ui): integrate filter into compress entry points`
  - Files: `src/MantisZip.UI/App.xaml.cs`

- [ ] 9. 压缩入口集成 — 快捷模式（quick / separate / combined）

  **What to do**:
  - 修改 `HandleCompressQuick`, `RunCompressSeparateBatch`, `RunCompressCombined`
  - 这些模式没有对话框，需要决定过滤策略：
    - 方案：如果 `ActiveFilterPresetName` 非空且预设存在，自动应用该预设
    - 否则：弹出 `FileFilterEditor` 轻量对话框（预设+一次性过滤）
    - 用户选"取消"则保持原始路径
  - 新增 `FileFilterQuickDialog` — 一个简化的 `FileFilterEditor` 弹窗（Window + OK/Cancel）

  **Must NOT do**:
  - 不要重复创建 `FileFilterEditor` 实例（复用控件）

  **Parallelization**:
  - Can Run In Parallel: NO
  - Parallel Group: Wave 3
  - Blocked By: Task 7

  **Acceptance Criteria**:
  - [ ] Active preset auto-applied in quick modes
  - [ ] Quick filter dialog shows before operation when no preset
  - [ ] Cancel = all files included (backward compatible)

  **Commit**: YES (groups with 8)

- [ ] 10. 提取过滤对话框

  **What to do**:
  - 新建 `ExtractFilterDialog`（Window + XAML + cs）：
    - 嵌入 `FileFilterEditor` 控件
    - 显示压缩包内条目数量统计（如"共 42 个文件，过滤后将提取 12 个"）
    - 确认/取消按钮
  - 打开前调用 `ListEntriesAsync` 获取全量条目列表
  - 对话框确认后应用过滤，返回匹配的 `entryKeys`（`ArchiveItem.FullPath` 列表）

  **Must NOT do**:
  - 不要修改 `IArchiveEngine.ListEntriesAsync`（不需要过滤参数）

  **References**:
  - Core/Abstractions/ArchiveEngine.cs — ListEntriesAsync
  - Core/FileFilter/FileFilterMatcher.cs — ArchiveItem 匹配

  **Parallelization**:
  - Can Run In Parallel: NO
  - Parallel Group: Wave 4
  - Blocked By: Task 5

  **Acceptance Criteria**:
  - [ ] Dialog shows file count before and after filter
  - [ ] Filter applied correctly to ArchiveItem entries
  - [ ] Returned entryKeys match filtered entries

  **Commit**: YES
  - Message: `feat(ui): add ExtractFilterDialog`
  - Files:
    - `src/MantisZip.UI/ExtractFilterDialog.xaml`
    - `src/MantisZip.UI/ExtractFilterDialog.xaml.cs`

- [ ] 11. 提取入口集成

  **What to do**:
  - 修改提取入口（在 App.xaml.cs 和 MainWindow.xaml.cs 中）：
    - `HandleExtract` / `HandleExtractHere` / `HandleExtractToNamed` / `HandleExtractSmart`
    - 以及 MainWindow 中的提取按钮/菜单
  - 提取流程改为：
    ```
    ListEntriesAsync → 判断是否启用过滤
      ├── 启用：弹出 ExtractFilterDialog → 获取 entryKeys → ExtractEntriesAsync
      └── 禁用：原行为 ExtractAsync（全量）
    ```
  - `ExtractEntriesAsync` 调用方式：
    ```csharp
    var engine = ArchiveEngineFactory.GetEngine(archivePath);
    await engine.ExtractEntriesAsync(archivePath, entryKeys, destinationPath, ...);
    ```

  **Must NOT do**:
  - 不要修改原有提取函数签名（内部加分支）

  **References**:
  - Core/Abstractions/ArchiveEngine.cs — ExtractEntriesAsync (Phase 3.4)
  - UI/App.xaml.cs — extract handlers

  **Parallelization**:
  - Can Run In Parallel: NO
  - Parallel Group: Wave 4
  - Blocked By: Task 10, Task 6

  **Acceptance Criteria**:
  - [ ] Extract with filter extracts only matching entries
  - [ ] Extract without filter = original behavior
  - [ ] All 4 extract modes work with filter

  **Commit**: YES
  - Message: `feat(ui): integrate filter into extract entry points`
  - Files:
    - `src/MantisZip.UI/App.xaml.cs`
    - `src/MantisZip.UI/MainWindow.xaml.cs` (if applicable)

- [ ] 12. 本地化字符串

  **What to do**:
  - 在 `L.cs` 中添加所有新 key：
    - `FileFilter_Enable` / `FileFilter_Disable`
    - `FileFilter_Extensions`, `FileFilter_NamePattern`, `FileFilter_Size`, `FileFilter_Date`
    - `FileFilter_PresetSelect`, `FileFilter_PresetSave`, `FileFilter_PresetDelete`
    - `FileFilter_Audio`, `FileFilter_Video`, `FileFilter_Image`, `FileFilter_Document`, `FileFilter_Archive`
    - `FileFilter_CustomExtensions`, `FileFilter_NamePatternHint`
    - `FileFilter_MinSize`, `FileFilter_MaxSize`, `FileFilter_SizeUnit`
    - `FileFilter_StartDate`, `FileFilter_EndDate`
    - `Compress_Filter`（Expander 标题）
    - `ExtractFilter_Title`, `ExtractFilter_Count`, `ExtractFilter_AfterFilter`
  - 在 `strings.zh.json` 和 `strings.en.json` 中添加翻译

  **Parallelization**:
  - Can Run In Parallel: YES
  - Parallel Group: Wave 2 (can run alongside UI tasks)
  - Blocked By: None (can be done anytime before build)

  **Acceptance Criteria**:
  - [ ] All keys defined in L.cs
  - [ ] Chinese translations added
  - [ ] English translations added
  - [ ] Build succeeds

  **Commit**: YES
  - Message: `feat(i18n): add file filter localization strings`
  - Files:
    - `src/MantisZip.UI/Localization/L.cs`
    - `src/MantisZip.UI/Resources/strings.zh.json`
    - `src/MantisZip.UI/Resources/strings.en.json`

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read plan end-to-end. Verify all Must Have tasks exist. Check no Must NOT have been violated. Verify evidence files exist. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Build check. Check for: no unused imports, no commented-out code, no magic numbers, proper disposal of streams. Verify FileFilterMatcher is pure (no I/O side effects).
  Output: `Build [PASS/FAIL] | Issues [N] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Execute QA scenarios for ALL tasks. Test cross-task integration: build filter → compress with filter → list archive entries → verify only filtered files included. Test edge cases: empty directory, all files excluded, 0-byte file, null dates. Save to `.sisyphus/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | Integration [N/N] | Edge Cases [N tested] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  Verify every task deliverable matches spec. Check no scope creep (no regex support, no OR logic). Detect cross-task contamination.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | VERDICT`

---

## Commit Strategy

- **1-3**: `feat(core): add FileFilter data model, matcher, and presets`
- **4-5**: `feat(ui): add FileFilterEditor user control`
- **6**: `feat(ui): embed FileFilterEditor in CompressSettingsDialog`
- **7**: `feat(ui): add FileFilterHelper`
- **8-9**: `feat(ui): integrate filter into compress entry points`
- **10**: `feat(ui): add ExtractFilterDialog`
- **11**: `feat(ui): integrate filter into extract entry points`
- **12**: `feat(i18n): add file filter localization strings`

---

## Success Criteria

### Verification Commands
```bash
# Build
dotnet build src/MantisZip.UI/MantisZip.UI.csproj

# Quick validation (manual script - no test project)
# Test FileFilterMatcher with various filter combinations
# Test compress filter with known directory structure
# Test extract filter on multipart archive
```

### Final Checklist
- [ ] All 4 filter dimensions implemented and working
- [ ] Compress: all 4 entry points apply filter
- [ ] Extract: all 4 modes support filtered extraction
- [ ] Presets: save, load, and auto-apply work
- [ ] No change to existing non-filtered behavior
- [ ] Build: 0 errors, 0 warnings
