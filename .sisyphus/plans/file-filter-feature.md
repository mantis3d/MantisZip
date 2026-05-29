# 文件过滤功能

> 在压缩与解压时，支持按条件过滤文件：文件类型、文件名、文件大小、修改日期。
> **状态**: 📋 待定（依赖 SharpCompress 迁移）| **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜] (0/12)
> **前置依赖**: SharpCompress 引擎迁移完成（`.sisyphus/plans/engine-unification-sharpcompress.md`），`IArchiveEngine.ExtractEntriesAsync` 接口已就绪。

---

## 前置条件

必须完成以下迁移计划中的任务：

- [ ] Phase 1-3: SharpCompress 迁移（TarGzEngine + ZipEngine + ArchiveEntryExtractor）
- [ ] Phase 3.4: `IArchiveEngine` 新增 `ExtractEntriesAsync` 方法完成
- [ ] 各引擎 `ArchiveItem.Name` 编码一致性已验证

---

## Context

### 设计变更记录（用户讨论确认）

| 项目 | 原计划 | 新方案（讨论后确认） |
|------|--------|---------------------|
| 压缩 UI 入口 | Expander 折叠在 General Tab 底部 | **Tab 3 "文件过滤"** — TabControl 直接显示 |
| 快捷模式 | 弹轻量 FileFilterEditor 对话框 | **快捷模式不参与过滤**（纯手动在对话框启用） |
| 解压过滤方式 | 弹出 ExtractFilterDialog（单窗口） | **ExtractSettingsWindow** — 带 TabControl 的设置窗口 |
| 解压触发时机 | 所有提取模式都弹窗 | **仅 MainWindow Extract_Click + --extract** 弹窗 |
| 预设系统 | 用户自定义（上限 20） | 用户自定义 + **8 个内置预设** |
| 压缩配置预设 | 未规划 | **另开计划**：保存全部压缩设置 → 右键菜单一键压缩（包含过滤条件） |

### ExtractSettingsWindow 结构（与 CompressSettingsWindow 对等设计）

```
ExtractSettingsWindow (TabControl)
├── Tab 1: "通用" (General)
│   ├── 目标路径（同目录 / 桌面 / 上一次 / 手动选择）
│   ├── 文件冲突处理（询问 / 覆盖 / 重命名 / 跳过）
│   └── 解压后打开文件夹
└── Tab 2: "文件过滤" (Filter)
    ├── 启用过滤开关
    ├── 预设栏（下拉框 + 保存/删除按钮）
    ├── 扩展名区（常用类型勾选 + 自定义扩展名）
    ├── 文件名区（通配符 `*` `?`）
    ├── 大小区（最小值 / 最大值 + 单位选择）
    └── 日期区（起始日期 / 结束日期 DatePicker）
```

### 内置预设（共 8 个）

| 预设名 | 条件 |
|--------|------|
| 📷 仅图片 | `.jpg .jpeg .png .gif .bmp .webp .svg` |
| 🎵 仅音频 | `.mp3 .wav .flac .aac .ogg .wma` |
| 🎬 仅视频 | `.mp4 .avi .mkv .mov .wmv .flv` |
| 📄 仅文档 | `.pdf .doc .docx .xls .xlsx .ppt .pptx .txt` |
| 🗜 仅压缩包 | `.zip .7z .rar .tar .gz .tgz` |
| 📦 大文件(>100MB) | 大小 ≥ 100MB |
| 📅 本月修改 | 日期 ≥ 当月 1 日 |
| 🗑 排除缓存/临时文件 | 文件名模式：`*.tmp` `*.cache` `*.log` `*.bak` |

---

## Work Objectives

### Core Objective
在压缩和解压流程中插入可配置的文件过滤步骤，支持按扩展名、文件名模式、大小范围、日期范围筛选。压缩端使用 CompressSettingsWindow Tab 3，解压端新建 ExtractSettingsWindow。

### Concrete Deliverables
- Core: `FileFilterCriteria` 数据模型
- Core: `FileFilterMatcher` 匹配逻辑（文件路径 + ArchiveItem）
- Core/UI: `FileFilterPreset` 预设模型 + 8 个内置预设
- UI: `FileFilterEditor` 用户控件（XAML + code-behind）
- UI: CompressSettingsWindow Tab 3 "文件过滤"
- UI: ExtractSettingsWindow（TabControl：通用 + 过滤）
- UI: `FileFilterHelper` 静态辅助类
- UI: 压缩所有入口集成过滤
- UI: 解压入口集成过滤（MainWindow Extract_Click + --extract）
- AppSettings: 预设持久化
- 本地化: zh + en 字符串

### Must Have
- [ ] 四种过滤维度全部可用（扩展名、文件名、大小、日期）
- [ ] 预设可保存和加载（8 个内置 + 用户自定义，上限 20）
- [ ] 压缩时对所选文件/目录应用过滤（Tab 3 配置，需用户手动启用）
- [ ] 解压时对压缩包内条目应用过滤（ExtractSettingsWindow Tab 2，需用户手动启用）
- [ ] ExtractSettingsWindow 只对 MainWindow Extract_Click + --extract 弹出
- [ ] 过滤器默认不激活（不干扰现有操作）

### Must NOT Have
- 不做正则表达式匹配（只用通配符 `*` `?`）
- 不做文件内容过滤（只基于文件元数据）
- 不做过滤器组合逻辑（AND 逻辑，不做 OR/NOT）
- 目录本身不过滤，只过滤目录内的文件（目录保留结构）
- 快捷模式（compress-quick / extract-here / extract-to-name / extract-smart）**不参与过滤**
- 过滤仅通过对话框手动启用，**不自动应用**

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
- **UI tests**: Build + launch app, verify controls render correctly
- **Integration tests**: Create test files, compress with filter, verify archive contents; extract with filter, verify output

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Core data model — all parallel):
├── Task 1: FileFilterCriteria data model
├── Task 2: FileFilterMatcher matching logic
├── Task 3: FileFilterPreset model + built-in presets + AppSettings integration
└── Task 4: Unit validation (manual script-based)

Wave 2 (UI control — depends on Wave 1):
├── Task 5: FileFilterEditor XAML layout
├── Task 6: FileFilterEditor code-behind (logic + preset management)
├── Task 7: Localization strings for all filter keys
└── Task 8: ExtractSettingsWindow XAML (General tab + Filter tab)

Wave 3 (Compress integration — depends on Wave 1 + 2):
├── Task 9: Embed FileFilterEditor as Tab 3 in CompressSettingsWindow
├── Task 10: FileFilterHelper static class
└── Task 11: Hook filter into --compress (dialog) flow (quick modes bypass filter)

Wave 4 (Extract integration — depends on Wave 1 + 2):
├── Task 12: ExtractSettingsWindow code-behind (logic + filter integration)
└── Task 13: Hook filter into Extract_Click + --extract flow

Wave FINAL (Verification):
├── Task F1: Plan compliance audit
├── Task F2: Code quality review
├── Task F3: Real manual QA (all filter dimensions + presets + extract flow)
└── Task F4: Scope fidelity check
```

### Critical Path
Task 1 → Task 2 → Task 5 → Task 6 → Task 9 → Task 11 → Task 12 → Task 13 → F1-F4

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
  - 扩展名比较统一小写

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

---

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
  - 不要依赖 UI 层
  - 不要文件 I/O 副作用（纯函数）

  **References**:
  - Core/FileFilter/FileFilterCriteria.cs (Task 1) — 数据模型

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

---

- [ ] 3. `FileFilterPreset` + 内置预设 + `AppSettings` 持久化

  **What to do**:
  - 新建 `src/MantisZip.Core/FileFilter/FileFilterPreset.cs`
    - `string Name` — 预设名称
    - `FileFilterCriteria Criteria` — 过滤条件
    - `bool IsBuiltIn` — 是否为内置预设（用户不可删除）
  - 新增 `FileFilterPreset.GetBuiltInPresets()` 静态方法返回 8 个内置预设
  - 修改 `AppSettings.cs`：
    - 新增 `List<FileFilterPreset> FilterPresets { get; set; }`，默认空列表（不含内置预设，内置预设由 GetBuiltInPresets() 提供）
  - `AppSettings.SaveSettings()` / `LoadSettings()` 中序列化/反序列化预设
  - 预设数量上限 20 个（不含内置），防止 settings.json 膨胀
  - 内置预设定义：

    | 名称 | IncludeExtensions | NamePattern | MinSize | MinDate |
    |------|-------------------|-------------|---------|---------|
    | 仅图片 | .jpg .jpeg .png .gif .bmp .webp .svg | | | |
    | 仅音频 | .mp3 .wav .flac .aac .ogg .wma | | | |
    | 仅视频 | .mp4 .avi .mkv .mov .wmv .flv | | | |
    | 仅文档 | .pdf .doc .docx .xls .xlsx .ppt .pptx .txt | | | |
    | 仅压缩包 | .zip .7z .rar .tar .gz .tgz | | | |
    | 大文件(>100MB) | | | 104857600 | |
    | 本月修改 | | | | DateTime(now.Year, now.Month, 1) |
    | 排除缓存/临时文件 | | *.tmp *.cache *.log *.bak | | |

  **Must NOT do**:
  - 不要修改预设的持久化格式
  - 内置预设不可被用户删除或修改（IsBuiltIn = true）

  **References**:
  - UI/AppSettings.cs — 现有设置模型
  - Core/FileFilter/FileFilterCriteria.cs (Task 1) — 数据模型

  **Parallelization**:
  - Can Run In Parallel: YES
  - Parallel Group: Wave 1
  - Blocked By: Task 1

  **Acceptance Criteria**:
  - [ ] Preset serializes/deserializes correctly in settings.json
  - [ ] Empty presets list works
  - [ ] Max 20 user presets enforced
  - [ ] 8 built-in presets returned by GetBuiltInPresets()
  - [ ] Built-in presets have IsBuiltIn = true
  - [ ] ActiveFilterPresetName persists correctly

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

  Scenario: Built-in presets accessible
    Tool: Bash
    Preconditions: FileFilterPreset compiled
    Steps:
      1. Call FileFilterPreset.GetBuiltInPresets()
      2. Assert count == 8
      3. Assert all have IsBuiltIn == true
    Expected Result: 8 built-in presets available
    Evidence: .sisyphus/evidence/task-3-builtin-presets.txt
  ```

  **Commit**: YES (groups with 1)
  - Message: `feat(core): add FileFilterPreset, built-in presets, and AppSettings persistence`
  - Files:
    - `src/MantisZip.Core/FileFilter/FileFilterPreset.cs`
    - `src/MantisZip.UI/AppSettings.cs`

---

- [ ] 4. `FileFilterEditor` 用户控件 — XAML

  **What to do**:
  - 新建 `src/MantisZip.UI/FileFilterEditor.xaml` + `FileFilterEditor.xaml.cs`
  - XAML 布局：
    - **预设栏**：预设下拉框（内置 + 用户预设）+ 保存按钮 + 删除按钮
    - **扩展名区**：
      - 常用类型勾选框（音频、视频、图片、文档、压缩包）
      - 自定义扩展名输入框（逗号分隔，自动加 `.` 前缀）
    - **文件名区**：文件名模式输入框（提示：支持 `*` 和 `?` 通配符）
    - **大小区**：最小值 + 最大值 + 单位下拉（B/KB/MB/GB）
    - **日期区**：起始日期 + 结束日期（DatePicker）
    - 顶部"启用过滤"开关（ToggleSwitch），默认关闭
    - 启用时控件可编辑，禁用时灰色
  - 使用 `Expander` 或 `GroupBox` 分组，避免视觉杂乱
  - 内置预设在下拉框中用特殊样式（如斜体或图标）区分于用户预设

  **Must NOT do**:
  - 不要硬编码字符串，用 `{l:L ...}` 本地化，key 前缀 `FileFilter_*`

  **Parallelization**:
  - Can Run In Parallel: NO (sequential with Task 5)
  - Parallel Group: Wave 2
  - Blocked By: Task 1, Task 3 (for preset data binding)

  **References**:
  - UI/CompressConflictDialog.xaml — 现有对话框样式参考
  - UI/SettingsWindow.xaml — 设置面板布局参考
  - UI/CompressSettingsWindow.xaml — TabControl 样式参考

  **Acceptance Criteria**:
  - [ ] XAML compiles without errors
  - [ ] All 4 filter sections rendered
  - [ ] Preset dropdown functional (bound to presets list)
  - [ ] Enable toggle disables all controls when off
  - [ ] Built-in presets visually distinct in dropdown

  **Commit**: YES
  - Message: `feat(ui): add FileFilterEditor user control XAML`
  - Files:
    - `src/MantisZip.UI/FileFilterEditor.xaml`
    - `src/MantisZip.UI/FileFilterEditor.xaml.cs`

---

- [ ] 5. `FileFilterEditor` 控件逻辑 — 代码后端

  **What to do**:
  - 实现 `FileFilterEditor` code-behind：
    - `FileFilterCriteria GetFilter()` — 从 UI 控件读取当前过滤条件
    - `void SetFilter(FileFilterCriteria filter)` — 将过滤条件填入 UI
    - `void LoadPresets(List<FileFilterPreset> presets)` — 填充预设下拉框（内置 + 用户）
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
  - 预设下拉选中某预设时，自动填充 4 个过滤区
  - 内置预设不可删除（删除按钮对内置预设禁用）

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
  - [ ] Preset dropdown populated with built-in + user presets
  - [ ] Selecting a preset fills the filter UI
  - [ ] Enable toggle disables/enables all input controls
  - [ ] Custom extension input parses correctly
  - [ ] Built-in presets cannot be deleted

  **Commit**: YES (groups with 4)
  - Message: `feat(ui): implement FileFilterEditor logic`
  - Files: `src/MantisZip.UI/FileFilterEditor.xaml.cs`

---

- [ ] 6. 本地化字符串

  **What to do**:
  - 在 `L.cs` 中添加所有新 key：
    - Tab 标题: `Compress_Tab_Filter`, `Extract_Tab_General`, `Extract_Tab_Filter`
    - 过滤控件: `FileFilter_Enable`, `FileFilter_Disable`
    - 过滤区: `FileFilter_Extensions`, `FileFilter_NamePattern`, `FileFilter_Size`, `FileFilter_Date`
    - 预设: `FileFilter_PresetSelect`, `FileFilter_PresetSave`, `FileFilter_PresetDelete`
    - 扩展名预设: `FileFilter_Audio`, `FileFilter_Video`, `FileFilter_Image`, `FileFilter_Document`, `FileFilter_Archive`
    - 自定义扩展名: `FileFilter_CustomExtensions`, `FileFilter_NamePatternHint`
    - 大小: `FileFilter_MinSize`, `FileFilter_MaxSize`, `FileFilter_SizeUnit`
    - 日期: `FileFilter_StartDate`, `FileFilter_EndDate`
    - 提取窗口: `ExtractSettings_Title`, `ExtractSettings_DestPath`, `ExtractSettings_ConflictAction`, `ExtractSettings_OpenFolder`
    - 冲突选项: `ExtractSettings_Conflict_Ask`, `ExtractSettings_Conflict_Overwrite`, `ExtractSettings_Conflict_Rename`, `ExtractSettings_Conflict_Skip`
    - 提取目标: `ExtractSettings_Dest_SameDir`, `ExtractSettings_Dest_Desktop`, `ExtractSettings_Dest_Last`, `ExtractSettings_Dest_Choose`
    - 过滤统计: `ExtractFilter_CountLabel`（"共 {0} 个文件，过滤后将提取 {1} 个"）
    - 压缩过滤 Tab: `Compress_Filter`
  - 在 `strings.zh.json` 和 `strings.en.json` 中添加翻译

  **Parallelization**:
  - Can Run In Parallel: YES
  - Parallel Group: Wave 2 (can run alongside UI tasks)
  - Blocked By: None

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

- [ ] 7. `ExtractSettingsWindow` XAML（General Tab + Filter Tab）

  **What to do**:
  - 新建 `src/MantisZip.UI/ExtractSettingsWindow.xaml` + `ExtractSettingsWindow.xaml.cs`
  - 窗口风格与 CompressSettingsWindow 保持一致（相同尺寸、主题、按钮布局）
  - **Tab 1：通用（General）**
    - 目标路径选择：
      - RadioButton 组：同目录 / 桌面 / 上一次 / 手动选择
      - 手动选择时显示路径输入框 + 浏览按钮
    - 文件冲突处理：
      - ComboBox 或 RadioButton：询问 / 覆盖 / 重命名 / 跳过
    - 解压后打开文件夹：CheckBox
  - **Tab 2：文件过滤（Filter）**
    - 内嵌 `FileFilterEditor` 控件
    - 过滤统计标签（提取时动态更新）："共 N 个文件，过滤后将提取 M 个"
  - 底部按钮：解压（OK）+ 取消

  **Must NOT do**:
  - 不要硬编码字符串，用 `{l:L ...}` 本地化
  - 不要复制 CompressSettingsWindow 的样式代码（用相同 StaticResource）

  **References**:
  - UI/CompressSettingsWindow.xaml — 窗口样式、TabControl 样式
  - UI/FileFilterEditor.xaml (Task 4) — 内嵌控件

  **Parallelization**:
  - Can Run In Parallel: YES
  - Parallel Group: Wave 2
  - Blocked By: Task 4, Task 6 (for local keys)

  **Acceptance Criteria**:
  - [ ] XAML compiles without errors
  - [ ] General tab renders all options (destination, conflict, open folder)
  - [ ] Filter tab embeds FileFilterEditor correctly
  - [ ] Window size and style matches CompressSettingsWindow

  **Commit**: YES
  - Message: `feat(ui): add ExtractSettingsWindow XAML`
  - Files:
    - `src/MantisZip.UI/ExtractSettingsWindow.xaml`
    - `src/MantisZip.UI/ExtractSettingsWindow.xaml.cs`

---

- [ ] 8. 嵌入 FileFilterEditor 作为 CompressSettingsWindow Tab 3

  **What to do**:
  - 修改 `CompressSettingsWindow.xaml`：
    - 在现有两个 TabItem（通用 + 注释）之后，添加第 3 个 TabItem
    - `Header="{l:L Compress_Tab_Filter}"`
    - 内容：内嵌 `FileFilterEditor` 控件
  - 修改 `CompressSettingsWindow.xaml.cs`：
    - 新增字段 `private FileFilterCriteria? _currentFilter`
    - 构造函数或 Loaded 事件中加载预设到 FileFilterEditor
    - 对话框确认时（OK 按钮）调用 `FileFilterEditor.GetFilter()` 保存过滤条件
    - `public FileFilterCriteria? GetActiveFilter()` — 供外部调用获取过滤条件
    - 切换 Tab 时自动记住上一次激活的 Tab
  - 确保 General Tab 和 Comment Tab 的现有布局完全不受影响

  **Must NOT do**:
  - 不要改变 General Tab 和 Comment Tab 的任何内容
  - 不要修改现有对话框按钮的确认逻辑

  **References**:
  - UI/FileFilterEditor.xaml (Task 4-5)
  - UI/CompressSettingsWindow.xaml — 现有 TabControl
  - UI/CompressSettingsWindow.xaml.cs — 现有代码后端

  **Parallelization**:
  - Can Run In Parallel: NO
  - Parallel Group: Wave 3
  - Blocked By: Task 5

  **Acceptance Criteria**:
  - [ ] CompressSettingsWindow shows 3 tabs (General / Comment / Filter)
  - [ ] Filter tab embeds FileFilterEditor
  - [ ] Filter state accessible via GetActiveFilter()
  - [ ] Build succeeds with 0 errors
  - [ ] General and Comment tabs unchanged

  **Commit**: YES
  - Message: `feat(ui): add filter Tab 3 to CompressSettingsWindow`
  - Files:
    - `src/MantisZip.UI/CompressSettingsWindow.xaml`
    - `src/MantisZip.UI/CompressSettingsWindow.xaml.cs`

---

- [ ] 9. `FileFilterHelper` 静态辅助类

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
  - [ ] Matched files preserve relative path structure from directory

  **QA Scenarios**:
  ```
  Scenario: Null/Inactive filter returns all paths
    Tool: Bash (dotnet script / REPL)
    Preconditions: FileFilterHelper compiled
    Steps:
      1. Call ApplyFilter(["a.txt", "b.mp3"], null)
      2. Assert result contains both "a.txt" and "b.mp3"
      3. Call ApplyFilter(["a.txt", "b.mp3"], new FileFilterCriteria())
      4. Assert result contains both files (empty filter = inactive)
    Expected Result: No filtering when filter is null or inactive
    Evidence: .sisyphus/evidence/task-9-null-filter.txt

  Scenario: Directory recursively filtered
    Tool: Bash
    Preconditions: Temp dir with files (a.mp3, sub/b.txt, sub/c.mp3)
    Steps:
      1. Create filter with IncludeExtensions=[".mp3"]
      2. Call ApplyFilter(["tempDir"], filter)
      3. Assert result contains "a.mp3" and "sub\\c.mp3"
      4. Assert result does NOT contain "sub\\b.txt"
    Expected Result: Directory recursively filtered, relative paths preserved
    Evidence: .sisyphus/evidence/task-9-dir-filter.txt
  ```

  **Commit**: YES
  - Message: `feat(ui): add FileFilterHelper for filtered compression`
  - Files: `src/MantisZip.UI/FileFilterHelper.cs`

---

- [ ] 10. 压缩入口集成 — `--compress` 对话框模式

  **What to do**:
  - 在 `ShowCompressWindow` / `HandleCompress`（App.xaml.cs）中：
    - CompressSettingsWindow 弹出后，用户确认时读取 `GetActiveFilter()`
    - 如果 filter 不为 null 且 `IsActive`，调用 `FileFilterHelper.ApplyFilter(paths, filter)` 过滤路径
    - 过滤后的路径传入 `CompressAsync`
  - 确保 AppSettings.LastCompressFilter 在对话框确认后保存

  **Must NOT do**:
  - 不要修改 `IArchiveEngine.CompressAsync` 签名（过滤在调用前完成）

  **References**:
  - UI/App.xaml.cs — HandleCompress / ShowCompressWindow
  - UI/FileFilterHelper.cs (Task 9)
  - UI/CompressSettingsWindow.xaml.cs — GetActiveFilter()

  **Parallelization**:
  - Can Run In Parallel: NO
  - Parallel Group: Wave 3
  - Blocked By: Task 8, Task 9

  **Acceptance Criteria**:
  - [ ] Filter from CompressSettingsWindow applied before compression
  - [ ] No filter = original behavior unchanged
  - [ ] Filter saved to AppSettings.LastCompressFilter

  **Commit**: YES (groups with 11)
  - Message: `feat(ui): integrate filter into compress entry points`
  - Files: `src/MantisZip.UI/App.xaml.cs`

---

- [ ] 11. `ExtractSettingsWindow` 代码后端

  **What to do**:
  - 实现 `ExtractSettingsWindow` code-behind：
    - 构造函数接收 `string archivePath` + `IReadOnlyList<ArchiveItem> entries`
    - 属性：
      - `string? SelectedDestination` — 用户选择的目标路径
      - `string SelectedConflictAction` — 用户选择的冲突处理
      - `bool OpenFolderAfterExtract` — 是否打开文件夹
      - `FileFilterCriteria? GetFilter()` — 获取过滤条件
      - `List<string>? GetFilteredEntryKeys()` — 获取过滤后的条目 key 列表
    - General Tab 逻辑：
      - 加载 AppSettings 中的默认值
      - "手动选择"时启用路径输入框 + 浏览按钮
    - Filter Tab 逻辑：
      - 内嵌 FileFilterEditor
      - 过滤统计标签实时更新：监听 FileFilterEditor.FilterChanged 事件
      - 对 entries 应用 FileFilterMatcher 计算过滤前后的数量
    - OK 按钮：
      - 保存当前选择到 AppSettings
      - 设置 DialogResult = true
  - 窗口启动时根据 `AppSettings.ExtractFilterEnabled` 决定默认是否切换到 Filter Tab

  **Must NOT do**:
  - 不要执行实际的提取操作（只返回配置参数，由调用方执行）

  **References**:
  - UI/ExtractSettingsWindow.xaml (Task 7)
  - UI/FileFilterEditor.xaml.cs (Task 5)
  - Core/FileFilter/FileFilterMatcher.cs
  - UI/AppSettings.cs

  **Parallelization**:
  - Can Run In Parallel: NO
  - Parallel Group: Wave 4
  - Blocked By: Task 7

  **Acceptance Criteria**:
  - [ ] Window opens with archive entries loaded
  - [ ] Filter tab shows correct count: "共 N 个文件，过滤后将提取 M 个"
  - [ ] Count updates when filter criteria change
  - [ ] OK button saves settings and returns selected config
  - [ ] Cancel returns null / DialogResult = false

  **Commit**: YES (groups with 7)
  - Message: `feat(ui): implement ExtractSettingsWindow logic`
  - Files: `src/MantisZip.UI/ExtractSettingsWindow.xaml.cs`

---

- [ ] 12. 提取入口集成 — MainWindow Extract_Click + --extract

  **What to do**:
  - 修改 `MainWindow.Menu.cs:Extract_Click`：
    - 原本：调用 `ResolveExtractDestination` → `ExtractAsync`
    - 改为：调用 `ListEntriesAsync` 获取条目 → 弹出 `ExtractSettingsWindow`
    - 从 ExtractSettingsWindow 获取目标路径 + 过滤条件
    - 如果有过滤条件：调用 `engine.ExtractEntriesAsync(entryKeys, ...)` 只提取过滤后的条目
    - 如果无过滤条件：保持原有 `ExtractAsync` 行为
  - 修改 `App.Cli.cs:HandleExtract`（`--extract` CLI 模式）：
    - 同样改为弹出 ExtractSettingsWindow（获取目标路径 + 过滤条件）
  - **以下入口保持原行为，不加过滤**：
    - `--extract-here`
    - `--extract-to-name`
    - `--extract-smart`
    - `SmartExtract_Click`

  **Must NOT do**:
  - 不要修改 --extract-here / --extract-to-name / --extract-smart / SmartExtract 的行为
  - 不要修改原有的 ExtractAsync 方法签名

  **References**:
  - UI/MainWindow.Menu.cs:33 — Extract_Click
  - UI/App.Cli.cs:592 — HandleExtract
  - UI/ExtractSettingsWindow (Task 7, 11)

  **Parallelization**:
  - Can Run In Parallel: NO
  - Parallel Group: Wave 4
  - Blocked By: Task 11

  **Acceptance Criteria**:
  - [ ] Extract_Click shows ExtractSettingsWindow before extraction
  - [ ] --extract shows ExtractSettingsWindow before extraction
  - [ ] With filter: ExtractEntriesAsync called with filtered entry keys
  - [ ] Without filter: original ExtractAsync called (backward compatible)
  - [ ] --extract-here / --extract-to-name / --extract-smart unchanged
  - [ ] SmartExtract_Click unchanged

  **Commit**: YES
  - Message: `feat(ui): integrate filter into MainWindow extract and --extract`
  - Files:
    - `src/MantisZip.UI/MainWindow.Menu.cs`
    - `src/MantisZip.UI/App.Cli.cs`

---

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read plan end-to-end. Verify all Must Have tasks exist. Check no Must NOT have been violated. Verify evidence files exist. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Build check. Check for: no unused imports, no commented-out code, no magic numbers, proper disposal of streams. Verify FileFilterMatcher is pure (no I/O side effects).
  Output: `Build [PASS/FAIL] | Issues [N] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Execute QA scenarios for ALL tasks. Test cross-task integration:
  - Compress: build filter → compress with filter → verify archive only contains filtered files
  - Extract: open archive → ExtractSettingsWindow → filter entries → verify output
  - Quick compress: set preset → quick compress → verify preset auto-applied
  - Edge cases: empty directory, all files excluded, 0-byte file, null dates, built-in presets
  Save to `.sisyphus/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | Integration [N/N] | Edge Cases [N tested] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  Verify every task deliverable matches spec. Check no scope creep (no regex support, no OR logic, no quick-mode dialogs, no extract-here/smart modification). Detect cross-task contamination.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | VERDICT`

---

## Commit Strategy

- **1-3**: `feat(core): add FileFilter data model, matcher, preset, and built-in presets`
- **4-5**: `feat(ui): add FileFilterEditor user control`
- **6**: `feat(i18n): add file filter localization strings`
- **7**: `feat(ui): add ExtractSettingsWindow XAML`
- **8**: `feat(ui): add filter Tab 3 to CompressSettingsWindow`
- **9**: `feat(ui): add FileFilterHelper`
- **10**: `feat(ui): integrate filter into compress entry points`
- **11**: `feat(ui): implement ExtractSettingsWindow logic`
- **12**: `feat(ui): integrate filter into MainWindow Extract_Click + --extract`

---

## Success Criteria

### Verification Commands
```bash
# Build
dotnet build src/MantisZip.UI/MantisZip.UI.csproj

# Quick validation (manual script - no test project)
# Test FileFilterMatcher with various filter combinations
# Test compress filter: compress with filter, list archive entries
# Test extract filter: open archive, apply filter, verify extracted files
```

### Final Checklist
- [ ] All "Must Have" present
- [ ] All "Must NOT Have" absent
- [ ] Build succeeds
- [ ] All QA scenarios pass
- [ ] MainWindow Extract shows ExtractSettingsWindow with filter Tab
- [ ] --extract shows ExtractSettingsWindow with filter Tab
- [ ] --compress shows 3 tabs (General / Comment / Filter)
- [ ] Quick modes (compress-quick / extract-here / extract-to-name / extract-smart) **unchanged**
- [ ] Filter only active when manually enabled in dialogs
- [ ] 8 built-in presets available in FileFilterEditor
