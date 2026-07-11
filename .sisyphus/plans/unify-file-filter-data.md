# 文件过滤与文件列表筛选数据统一

> **状态**: 📋 新计划 | **阶段**: 设计中
> **前置依赖**: file-filter-feature ✅ (v0.4.5+), file-list-filter-search ✅ (v0.3.8)

## TL;DR

> **Quick Summary**: 将两套独立的过滤系统（压缩/解压用 `FileFilterCriteria` + 文件列表浏览用 `SearchFilters`）统一为单一的 `FileFilterCriteria` 模型。MainWindow 筛选栏增强：新增预设下拉框、扩展名 Tag 选择器、「以当前过滤解压」复选框。Core 层纯 .NET，WPF 实现，Avalonia 迁移备注。
>
> **Deliverables**:
> - `FileFilterCriteria` 扩展 TextSearch/ExcludeText/TextMatchMode
> - `SearchFilters`/`ArchiveFilter.ApplyFilters()` 删除
> - `FilterTagSelector` 新控件（WPF）
> - MainWindow 筛选栏增强（预设 + 扩展名 + 过滤解压）
> - ExtractSettingsWindow 预填过滤条件
> - Avalonia 迁移要点文档
>
> **Estimated Effort**: Medium（3-5h）
> **Parallel Execution**: YES — 3 waves
> **Critical Path**: Task 1 → Task 2 → Task 4 → Task 7 → Task 8

---

## Context

### Original Request
统一两个已完成计划的过滤数据模型，使文件列表筛选能直接调用文件过滤的预设，并且支持"以当前过滤解压"。

### Metis Review — 已解决的关键问题

| 问题 | 处理 |
|------|------|
| 目录过滤行为差异 | `FileFilterMatcher.IsMatch` 对目录：TextSearch/ExcludeText 匹配 DisplayName，其余条件跳过 |
| TextSearch vs NamePattern 语义 | TextSearch=子串匹配 DisplayName，NamePattern=通配符匹配文件名（不含扩展名） |
| 内置预设"排除缓存/临时文件"有 bug | `NamePattern = "*.tmp;*.cache;*.log;*.bak"` 不拆分分号，属已有 bug，**另案修复** |
| `ParseSizeWithUnit` 去向 | 移至 `FileFilterHelper`（UI 层） |
| 预设 + 手动过滤合并策略 | 预设填充扩展名/大小/日期控件，但不覆盖文字搜索；手动修改后预设下拉置空 |
| 测试代码 | 检查确认无 `SearchFilters` 引用，无需修改 |

---

## Work Objectives

### Core Objective
统一 `FileFilterCriteria` 和 `SearchFilters` 为一个模型，MainWindow 筛选栏增强，支持过滤解压。

### Concrete Deliverables
- `Core/FileFilter/FileFilterCriteria.cs` — 扩展 3 个字段
- `Core/FileFilter/FileFilterMatcher.cs` — 增加文字搜索匹配
- `Core/FileFilter/FilterMatchMode.cs` — 枚举迁移至此命名空间
- `Core/Utils/ArchiveFilter.cs` — 删除 SearchFilters 和 ApplyFilters，保留 ParseSizeWithUnit
- `UI/Controls/FilterTagSelector.xaml(.cs)` — 新建 Tag 选择器控件（WPF）
- `UI/MainWindow/MainWindow.xaml` — 筛选栏增强
- `UI/MainWindow/MainWindow.UI.cs` — RefreshFilter 改用 FileFilterMatcher
- `UI/MainWindow/MainWindow.Menu.cs` — 过滤解压集成
- `UI/Dialogs/ExtractSettingsWindow.xaml.cs` — 预填过滤
- `UI/FileFilterHelper.cs` — 增加 ParseSizeWithUnit
- `Resources/strings.zh.json` / `strings.en.json` — 本地化字符串

### Definition of Done
- [ ] `FileFilterCriteria` 包含所有原有 SearchFilters 字段，`IsActive` 正确反映
- [ ] `FileFilterMatcher.IsMatch(FileFilterCriteria, ArchiveItem)` 支持文字搜索
- [ ] `SearchFilters` record 被删除，所有调用方改用 FileFilterCriteria
- [ ] FilterTagSelector 弹出菜单勾选扩展名类别
- [ ] MainWindow 筛选栏显示预设下拉框、Tag 选择器、「过滤解压」复选框
- [ ] 选中预设自动填充条件，文字搜索词不受影响
- [ ] 勾选"过滤解压"后，解压按钮只提取匹配条目
- [ ] ExtractSettingsWindow 接收外部预填过滤条件
- [ ] `ParseSizeWithUnit` 在 FileFilterHelper 中可用
- [ ] Avalonia 迁移注意事项已在计划中标注

### Must Have
- 数据模型统一，无重复模型
- 文件列表筛选栏可用预设系统
- 过滤解压功能可用
- 向后兼容：现有 ExtractSettingsWindow 行为不变（无预填时）

### Must NOT Have (Guardrails)
- 不修改压缩引擎/解压引擎代码
- 不影响已有的快捷模式（compress-quick 等）
- 不改 FileFilterEditor 的 UI 布局
- 不改预设持久化方式（AppSettings）
- 不修复"排除缓存/临时文件"预设的已有 bug（另案处理）

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — 所有验证由 agent 执行。

### Test Decision
- **Infrastructure exists**: YES（xUnit, tests/MantisZip.Tests/）
- **Automated tests**: Tests-after
- **Framework**: xUnit (.NET 9)

### QA Policy
- **Core 逻辑**: Bash 运行 `dotnet test` 验证 FileFilterMatcher 匹配正确
- **UI 控件**: 构建 + 启动应用验证控件渲染
- **集成测试**: 带过滤解压验证

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Core — 3 tasks parallel):
├── Task 1: FileFilterCriteria 扩展字段
├── Task 2: FileFilterMatcher 扩展 + FilterMatchMode 迁移
└── Task 3: ArchiveFilter 清理 + ParseSizeWithUnit 迁移

Wave 2 (WPF 控件 — 依赖 Wave 1, 4 tasks parallel):
├── Task 4: FilterTagSelector 控件（WPF）
├── Task 5: MainWindow.xaml 筛选栏 UI 改造
├── Task 6: MainWindow.UI.cs 过滤逻辑替换
└── Task 7: 本地化字符串

Wave 3 (集成 — 依赖 Wave 2, 3 tasks parallel):
├── Task 8: Extract 集成（ExtractSettingsWindow + MainWindow.Menu）
├── Task 9: ClearFiltersBtn + 预设联动逻辑
└── Task 10: 单元测试

Wave FINAL (Review — 4 tasks parallel):
├── F1-F4: Final verification
```

### Critical Path
Task 1 → Task 2 → Task 4 → Task 5 → Task 8

---

## TODOs

- [ ] 1. `FileFilterCriteria` 扩展字段

  **What to do**:
  - `src/MantisZip.Core/FileFilter/FileFilterCriteria.cs` 中新增字段：
    - `TextSearch: string?` — 子串匹配 DisplayName（文件列表浏览用）
    - `ExcludeText: string?` — 排除匹配的文字
    - `TextMatchMode: FilterMatchMode` — 匹配模式（默认 Substring）
  - 同步更新 `IsActive` getter：
    ```csharp
    public bool IsActive =>
        (IncludeExtensions.Count > 0) ||
        (ExcludeExtensions.Count > 0) ||
        !string.IsNullOrEmpty(NamePattern) ||
        MinSize.HasValue ||
        MaxSize.HasValue ||
        MinDate.HasValue ||
        MaxDate.HasValue ||
        !string.IsNullOrEmpty(TextSearch) ||
        !string.IsNullOrEmpty(ExcludeText);
    ```
  - 同步更新 `DisplaySummary` getter，新增 TextSearch/ExcludeText 描述
  - 确保 `[System.Text.Json.Serialization.JsonIgnore]` 注解在 `IsActive` 和 `DisplaySummary` 上保留

  **Must NOT do**:
  - 不改现有字段名（向后兼容）
  - 不改预设持久化逻辑

  **References**:
  - `Core/FileFilter/FileFilterCriteria.cs` — 现有数据模型
  - `Core/Utils/ArchiveFilter.cs:SearchFilters` — 要替代的记录
  - `Core/FileFilter/FileFilterMatcher.cs` — 下个任务

  **Parallelization**:
  - Can Run In Parallel: YES (Wave 1)
  - Blocked By: None

  **Acceptance Criteria**:
  - [ ] 文件编译通过
  - [ ] 3 个新字段可读写
  - [ ] `IsActive` 在 TextSearch 非空时返回 true
  - [ ] `IsActive` 在 ExcludeText 非空时返回 true

  **QA Scenarios**:
  ```
  Scenario: IsActive 反映新字段
    Tool: Bash
    Steps:
      1. var filter = new FileFilterCriteria();
      2. Assert.IsFalse(filter.IsActive);
      3. filter.TextSearch = "test";
      4. Assert.IsTrue(filter.IsActive);
      5. filter.TextSearch = null;
      6. filter.ExcludeText = "exclude";
      7. Assert.IsTrue(filter.IsActive);
    Expected Result: 所有断言通过
    Evidence: .sisyphus/evidence/task-1-isactive.txt
  ```

  **Commit**: NO (group with task 2-3)
  - Files: `src/MantisZip.Core/FileFilter/FileFilterCriteria.cs`

- [ ] 2. `FileFilterMatcher` 扩展 + `FilterMatchMode` 迁移

  **What to do**:
  - 在 `Core/FileFilter/FileFilterMatcher.cs` 的 `IsMatch(FileFilterCriteria, ArchiveItem)` 中增加：
    1. **目录特殊处理**：如果是目录 → 仅检查 TextSearch/ExcludeText（匹配 DisplayName），其余条件跳过
    2. **TextSearch 匹配**：对 `item.DisplayName`（回退到 `item.Name`）做子串/通配符匹配
    3. **ExcludeText 匹配**：同上，命中即排除
  - 新增 `IsMatch(FileFilterCriteria filter, string displayName)` 重载做纯文字匹配
  - 将 `FilterMatchMode` 枚举从 `Core/Utils/ArchiveFilter.cs` 迁移到 `Core/FileFilter/`：
    - 新建 `Core/FileFilter/FilterMatchMode.cs`
    - 原文件中的 `FilterMatchMode` 替换为 `using` 引用或直接删除
  - 命名空间：`namespace MantisZip.Core.FileFilter`

  **TextSearch vs NamePattern 语义**:
  - `TextSearch`: 匹配 `DisplayName`（完整相对路径如 `"subdir/file.cs"`），默认子串模式
  - `NamePattern`: 匹配纯文件名（不含扩展名如 `"file"`），仅通配符模式
  - 两者独立生效（AND 逻辑），不存在互斥

  **Must NOT do**:
  - 不改 `IsMatch(FileFilterCriteria, string filePath)` 文件系统重载
  - 不改 `MatchWildcard` 方法

  **References**:
  - `Core/FileFilter/FileFilterCriteria.cs` — 数据模型
  - `Core/Utils/ArchiveFilter.cs:MatchItem()` — 参考现有文字匹配逻辑
  - `Core/FileFilter/FileFilterMatcher.cs` — 现有代码

  **Parallelization**:
  - Can Run In Parallel: YES (Wave 1, with Task 1)
  - Blocked By: Task 1

  **Acceptance Criteria**:
  - [ ] FilterMatchMode 枚举在 Core/FileFilter 命名空间下可引用
  - [ ] IsMatch 对目录只检查文字搜索
  - [ ] IsMatch 对文件检查所有条件
  - [ ] TextSearch 子串匹配成功
  - [ ] ExcludeText 排除成功

  **QA Scenarios**:
  ```
  Scenario: 目录只匹配文字搜索
    Tool: Bash
    Steps:
      1. var dir = new ArchiveItem { Name = "src", IsDirectory = true, DisplayName = "src" };
      2. var file = new ArchiveItem { Name = "file.cs", DisplayName = "file.cs", Size = 100 };
      3. var filter = new FileFilterCriteria { TextSearch = "src" };
      4. Assert.IsTrue(FileFilterMatcher.IsMatch(filter, dir));
      5. filter.TextSearch = "file";
      6. Assert.IsFalse(FileFilterMatcher.IsMatch(filter, dir)); // 目录名不匹配
      7. Assert.IsTrue(FileFilterMatcher.IsMatch(filter, file));
    Expected Result: 所有断言通过
    Evidence: .sisyphus/evidence/task-2-match.txt

  Scenario: ExcludeText 排除
    Tool: Bash
    Steps:
      1. var filter = new FileFilterCriteria { ExcludeText = ".cs" };
      2. var item = new ArchiveItem { Name = "test.cs", DisplayName = "test.cs" };
      3. Assert.IsFalse(FileFilterMatcher.IsMatch(filter, item));
    Expected Result: 断言通过
    Evidence: .sisyphus/evidence/task-2-exclude.txt
  ```

  **Commit**: NO (group with task 1, 3)

- [ ] 3. `ArchiveFilter` 清理 + `ParseSizeWithUnit` 迁移

  **What to do**:
  - `Core/Utils/ArchiveFilter.cs`：
    - **删除** `SearchFilters` record
    - **删除** `FilterMatchMode` enum（已迁至 Core/FileFilter/）
    - **删除** `ApplyFilters()` 方法
    - **保留** `ParseSizeWithUnit()` 方法（暂时留存，后续用 `[Obsolete]` 标注或直接删除）
    - 移除对 `MantisZip.Core.Abstractions` 的 using（如果不再需要）
  - `UI/FileFilterHelper.cs`（MantisZip.UI 命名空间）：
    - 增加 `public static long? ParseSizeWithUnit(string? text, string? unit)` 方法
    - 从 `ArchiveFilter.ParseSizeWithUnit` 复制完整实现
  - 更新 `MainWindow.UI.cs` 中 4 处 `ArchiveFilter.ParseSizeWithUnit(...)` 调用 → `FileFilterHelper.ParseSizeWithUnit(...)`

  **Must NOT do**:
  - 不要删除 `ArchiveFilter` 类本身（可能还有其他引用）
  - 不要修改 `ParseSizeWithUnit` 的实现逻辑

  **References**:
  - `Core/Utils/ArchiveFilter.cs` — 要清理的文件
  - `UI/FileFilterHelper.cs` — 目标位置
  - `UI/MainWindow/MainWindow.UI.cs:590-615` — 4 处调用点

  **Parallelization**:
  - Can Run In Parallel: YES (Wave 1, with Task 1, 2)
  - Blocked By: Task 1

  **Acceptance Criteria**:
  - [ ] `SearchFilters` record 已被删除，项目中无残留引用
  - [ ] `ApplyFilters` 已被删除
  - [ ] 编译通过（dotnet build）
  - [ ] `FileFilterHelper.ParseSizeWithUnit("100", "KB")` 返回 102400
  - [ ] `FileFilterHelper.ParseSizeWithUnit("", "MB")` 返回 null

  **QA Scenarios**:
  ```
  Scenario: ParseSizeWithUnit 迁移正确
    Tool: Bash
    Preconditions: dotnet build 通过
    Steps:
      1. 检查项目中无 SearchFilters 引用（grep 确认）
      2. dotnet build src/MantisZip.UI/MantisZip.UI.csproj
    Expected Result: Build 成功, 0 errors
    Evidence: .sisyphus/evidence/task-3-build.txt
  ```

  **Commit**: YES (group with 1, 2)
  - Message: `refactor(core): unify filter data model, replace SearchFilters with FileFilterCriteria`
  - Files: `Core/FileFilter/FileFilterCriteria.cs`, `Core/FileFilter/FileFilterMatcher.cs`, `Core/FileFilter/FilterMatchMode.cs`, `Core/Utils/ArchiveFilter.cs`, `UI/FileFilterHelper.cs`, `UI/MainWindow/MainWindow.UI.cs`

- [ ] 4. `FilterTagSelector` 控件（WPF）

  **What to do**:
  - 新建 `src/MantisZip.UI/Controls/FilterTagSelector.xaml` 和 `FilterTagSelector.xaml.cs`
  - WPF Tag 选择器控件，行为：折叠时显示已选标签摘要（如 "图片, 音频 +2"），点击展开弹出 CheckBox 列表
  - XAML 结构：
    ```xml
    <UserControl x:Class="MantisZip.UI.Controls.FilterTagSelector">
      <Grid>
        <ToggleButton x:Name="ToggleBtn" Content="选择类型..." Click="ToggleBtn_Click"
                      Background="{DynamicResource Theme_WindowBg}"
                      Foreground="{DynamicResource Theme_TextPrimary}"
                      BorderBrush="{DynamicResource Theme_Border}"/>
        <Popup x:Name="TagPopup" PlacementTarget="{Binding ElementName=ToggleBtn}"
               StaysOpen="False" PopupAnimation="Slide">
          <Border Background="{DynamicResource Theme_WindowBg}"
                  BorderBrush="{DynamicResource Theme_Border}">
            <StackPanel>
              <CheckBox x:Name="ExtImageCheck" Content="图片 (.jpg .png …)" Checked="OnTagChanged" Unchecked="OnTagChanged"/>
              <CheckBox x:Name="ExtAudioCheck" Content="音频 (.mp3 .wav …)" Checked="OnTagChanged" Unchecked="OnTagChanged"/>
              <CheckBox x:Name="ExtVideoCheck" Content="视频 (.mp4 .avi …)" Checked="OnTagChanged" Unchecked="OnTagChanged"/>
              <CheckBox x:Name="ExtDocumentCheck" Content="文档 (.pdf .doc …)" Checked="OnTagChanged" Unchecked="OnTagChanged"/>
              <CheckBox x:Name="ExtArchiveCheck" Content="压缩包 (.zip .7z …)" Checked="OnTagChanged" Unchecked="OnTagChanged"/>
              <Separator/>
              <TextBox x:Name="CustomExtBox" ToolTip="自定义扩展名（逗号分隔）" Height="22"
                       Background="{DynamicResource Theme_WindowBg}"
                       Foreground="{DynamicResource Theme_TextPrimary}"
                       BorderBrush="{DynamicResource Theme_Border}"
                       TextChanged="CustomExtBox_TextChanged"/>
            </StackPanel>
          </Border>
        </Popup>
      </Grid>
    </UserControl>
    ```
  - 代码-behind：
    - 公开属性 `SelectedExtensions: List<string>` — 当前选中的扩展名列表
    - 公开事件 `SelectionChanged: EventHandler` — 选择变化时触发
    - `OnTagChanged` → 根据勾选状态从 `PresetExtensions` 映射解析扩展名
    - 自定义扩展名输入（逗号分隔）追加到结果中
    - `ToggleBtn_Click` → 切换 Popup 显隐
    - Popup 关闭时更新 ToggleBtn 显示文本（如 "图片, 音频 +2" 或 "选择类型…"）
  - 参考 `FileFilterEditor.xaml.cs` 中的 `PresetExtensions` 映射（`"image"→[".jpg",...]` 等）—— 直接引用或复制一份

  **Avalonia 迁移备注**:
  - `ToggleButton` → Avalonia `ToggleButton`（相同）
  - `Popup` → Avalonia `Popup`（属性 `StaysOpen` → 需注意 Avalonia 使用 `IsOpen` 属性控制）
  - `PlacementTarget` → 语法相同
  - `CheckBox.Checked/Unchecked` → 事件名相同
  - `Separator` → Avalonia 用 `Separator`（相同）

  **Must NOT do**:
  - 不依赖 FileFilterEditor 内部控件（保持独立可复用）
  - 不做动画（WPF PopupAnimation 在 Avalonia 中无对应项，避免依赖）
  - 不做搜索框（仅勾选，不搜索）

  **References**:
  - `UI/Controls/FileFilterEditor.xaml.cs:PresetExtensions` — 扩展名类别映射
  - `UI/Controls/FileFilterEditor.xaml.cs:GetSelectedExtensions()` — 参考其实现

  **Parallelization**:
  - Can Run In Parallel: YES (Wave 2, with Task 5, 6, 7)
  - Blocked By: Task 1

  **Acceptance Criteria**:
  - [ ] 控件编译通过
  - [ ] 折叠时显示已选标签摘要文本
  - [ ] 点击展开弹出 CheckBox 列表
  - [ ] 勾选 CheckBox → `SelectedExtensions` 正确返回对应扩展名
  - [ ] 自定义扩展名输入被正确解析
  - [ ] 控件主题色使用 `DynamicResource Theme_*`

  **QA Scenarios**:
  ```
  Scenario: Tag 选择器弹出和选择
    Tool: 构建 + 启动应用
    Preconditions: 控件嵌入到 MainWindow 中可见
    Steps:
      1. 点击 ToggleButton → Popup 弹出
      2. 勾选 "图片" → SelectedExtensions 包含 .jpg .jpeg .png .gif .bmp .webp .svg
      3. 勾选 "音频" → 增加 .mp3 .wav .flac .aac .ogg .wma
      4. 输入 "webp,ico" 到自定义框 → 扩展名列表增加 .webp .ico
      5. 点空白处关闭 Popup → ToggleButton 文本显示 "图片, 音频 +2"
    Expected Result: 所有操作按预期
    Evidence: .sisyphus/evidence/task-4-tag-selector.png
  ```

  **Commit**: NO (group with 5-7)
  - Files: `UI/Controls/FilterTagSelector.xaml`, `UI/Controls/FilterTagSelector.xaml.cs`

- [ ] 5. MainWindow.xaml 筛选栏 UI 改造

  **What to do**:
  - 修改 `src/MantisZip.UI/MainWindow/MainWindow.xaml` 中 FilterBar 区域（约 311-440 行）：
    - 在现有文字搜索/排除/日期/大小控件右侧，新增：
      1. **预设下拉框**：`ComboBox`，绑定 `FileFilterPreset` 列表，`SelectionChanged → PresetCombo_SelectionChanged`
      2. **扩展名 Tag 选择器**：嵌入 `FilterTagSelector` 控件
      3. **「以当前过滤解压」复选框**：`CheckBox`，绑定 `_filteredExtractEnabled`
    - 调整布局，用竖线分隔不同区域
    - 所有新增控件使用 `DynamicResource Theme_*` 主题色
    - 预设下拉框宽度 ~160，Tag 选择器宽度 ~180
  - 新增预设 ComboBox ItemTemplate 显示预设名称 + 内置/用户标记

  **XAML 布局示意（FilterBar 在现有基础上扩展）**:
  ```
  <Border x:Name="FilterBar" ...>
    <StackPanel Orientation="Horizontal">
      <!-- 区域 1: 文字搜索（现有，不变）-->
      <Grid ...> ... </Grid>
      
      <Separator Style="{StaticResource VerticalSeparator}"/>
      
      <!-- 区域 2: 预设 + Tag 选择器（新增）-->
      <StackPanel Orientation="Horizontal">
        <ComboBox x:Name="PresetCombo" Width="160" ... />
        <controls:FilterTagSelector x:Name="FilterTagSelector" Width="180" ... />
      </StackPanel>
      
      <Separator Style="{StaticResource VerticalSeparator}"/>
      
      <!-- 区域 3: 日期 + 大小（现有，调整位置）-->
      <StackPanel Orientation="Horizontal"> ... </StackPanel>
      
      <!-- 区域 4: 过滤解压复选框 + 清除按钮（新增+现有）-->
      <StackPanel Orientation="Horizontal">
        <CheckBox x:Name="FilteredExtractCheck" Content="{l:L Main_Filter_FilteredExtract}" ... />
        <Button x:Name="ClearFiltersBtn" ... />
      </StackPanel>
    </StackPanel>
  </Border>
  ```

  **Avalonia 迁移备注**:
  - WPF `Separator` → Avalonia `Separator`（相同，注意样式）
  - WPF `StackPanel Orientation="Horizontal"` → Avalonia `StackPanel Orientation="Horizontal"`（相同）
  - WPF `ComboBox` 的 `DisplayMemberPath` → Avalonia 使用 `ItemTemplate` + `TextBlock`
  - WPF `DynamicResource` → Avalonia 也支持 `DynamicResource`
  - **注意**：Avalonia 中 `ScrollViewer` 的横向滚动行为不同，如果筛选栏过长需要包裹

  **Must NOT do**:
  - 不删除现有的 FileSearchBox、ExcludeBox、DatePicker、SizeBox
  - 不改动 FilterBar 的 Visibility 逻辑（仍然是打开压缩包后显示）

  **References**:
  - `MainWindow.xaml:311-440` — 现有筛选栏 XAML
  - `MainWindow.UI.cs:RefreshFilter()` — 事件处理器

  **Parallelization**:
  - Can Run In Parallel: YES (Wave 2, with Task 4, 6, 7)
  - Blocked By: Task 1

  **Acceptance Criteria**:
  - [ ] 编译通过
  - [ ] 预设下拉框可见，加载内置预设
  - [ ] FilterTagSelector 控件可见
  - [ ] 「以当前过滤解压」复选框可见
  - [ ] 主题色正确应用（亮/暗模式）
  - [ ] 筛选栏横向布局合理，不溢出

  **QA Scenarios**:
  ```
  Scenario: 筛选栏控件渲染
    Tool: 构建 + 启动应用
    Steps:
      1. 打开一个压缩包 → FilterBar 可见
      2. 检查预设下拉框 — 显示 8 个内置预设
      3. 检查 Tag 选择器 — 折叠显示"选择类型…"
      4. 点击展开 — 弹出 CheckBox 列表
      5. 检查「以当前过滤解压」复选框 — 存在且未勾选
    Expected Result: 所有控件正常渲染
    Evidence: .sisyphus/evidence/task-5-filterbar.png
  ```

  **Commit**: NO (group with 4, 6, 7)
  - Files: `MainWindow.xaml`

- [ ] 6. MainWindow.UI.cs 过滤逻辑替换

  **What to do**:
  - 修改 `MainWindow.UI.cs`：
    1. `RefreshFilter()` 方法（约 442 行）：
       - **删除** `SearchFilters` 对象的构建
       - **改为**构建 `FileFilterCriteria`：
         ```csharp
         var filter = new FileFilterCriteria
         {
             TextSearch = _searchText,
             ExcludeText = _excludeText,
             TextMatchMode = _matchMode,
             // 从 FilterTagSelector 读取扩展名
             IncludeExtensions = FilterTagSelector.SelectedExtensions,
             // 预设已通过 SetFilter 填入日期/大小控件，从控件读取
             MinDate = DateFromPicker.SelectedDate,
             MaxDate = DateToPicker.SelectedDate,
             MinSize = _sizeMin,
             MaxSize = _sizeMax,
         };
         ```
       - **替换** `ArchiveFilter.ApplyFilters(_currentUnfilteredItems, filters)` → 内联 LINQ：
         ```csharp
         result = _currentUnfilteredItems
             .Where(i => FileFilterMatcher.IsMatch(filter, i))
             .ToList();
         ```
    2. `HasActiveFilters()` 方法（约 428 行）：
       - 保持现有检查逻辑，新增对 `TextSearch`/`ExcludeText` 的检查（已有 `_searchText`/`_excludeText`）
       - 新增对 `FilterTagSelector.SelectedExtensions.Count > 0` 的检查
    3. 更新 `_searchText`, `_excludeText`, `_matchMode` 字段类型（已有，不需改）
    4. `using MantisZip.Core.Utils;` → 如果不再引用 ArchiveFilter 的其他方法，可移除
    5. 确保 `using MantisZip.Core.FileFilter;` 已存在（引用 FileFilterMatcher）

  **Must NOT do**:
  - 不改动 FilterFiles 方法的逻辑（只改 RefreshFilter）
  - 不改动排序、进度条等无关逻辑

  **References**:
  - `MainWindow.UI.cs:442-568` — RefreshFilter 完整方法

  **Parallelization**:
  - Can Run In Parallel: YES (Wave 2, with Task 4, 5, 7)
  - Blocked By: Task 1

  **Acceptance Criteria**:
  - [ ] 编译通过
  - [ ] 已有过滤功能（文字搜索/日期/大小）行为不变
  - [ ] 新扩展名过滤通过 FilterTagSelector 生效
  - [ ] NoResultsText 在新过滤条件下正确显隐

  **QA Scenarios**:
  ```
  Scenario: 文字搜索 + 扩展名组合过滤
    Tool: 构建 + 启动应用
    Preconditions: 打开含多种文件的压缩包
    Steps:
      1. 在 Tag 选择器勾选 "图片" → 列表只显示 .jpg .png 等图片文件
      2. 文字搜索输入 "logo" → 只显示文件名含 logo 的图片
      3. 清除条件 → 恢复完整列表
    Expected Result: 组合过滤正常（AND 逻辑）
    Evidence: .sisyphus/evidence/task-6-filter-combo.png
  ```

  **Commit**: NO (group with 4, 5, 7)
  - Files: `MainWindow.UI.cs`

- [ ] 7. 本地化字符串

  **What to do**:
  - 在 `strings.zh.json` 和 `strings.en.json` 中新增以下键值对：

  | Key | zh | en |
  |-----|----|----|
  | `Main_Filter_PresetLabel` | 预设: | Preset: |
  | `Main_Filter_PresetPlaceholder` | 选择预设… | Select preset… |
  | `Main_Filter_TagPlaceholder` | 选择类型… | Select type… |
  | `Main_Filter_FilteredExtract` | 以当前过滤解压 | Extract with filter |
  | `Main_Filter_TagImage` | 图片 | Image |
  | `Main_Filter_TagAudio` | 音频 | Audio |
  | `Main_Filter_TagVideo` | 视频 | Video |
  | `Main_Filter_TagDocument` | 文档 | Document |
  | `Main_Filter_TagArchive` | 压缩包 | Archive |
  | `Main_Filter_CustomExtHint` | 自定义扩展名（逗号分隔） | Custom extensions (comma-separated) |
  | `Main_Filter_BuiltInPreset` | 内置 | Built-in |
  | `Main_Filter_UserPreset` | 自定义 | Custom |

  - 格式参考现有 JSON 条目，注意转义规则

  **Avalonia 迁移备注**:
  - 字符串资源文件（JSON）可以直接共用（WPF 和 Avalonia 都可以读取相同格式的 JSON 文件）
  - 或者将字符串迁移到 MantisZip.Core 中供两边共享

  **Must NOT do**:
  - 不修改或删除已有的字符串键

  **References**:
  - `Resources/strings.zh.json` — 中文 JSON
  - `Resources/strings.en.json` — 英文 JSON

  **Parallelization**:
  - Can Run In Parallel: YES (Wave 2, with Task 4, 5, 6)
  - Blocked By: None

  **Acceptance Criteria**:
  - [ ] JSON 文件格式正确（无解析错误）
  - [ ] `dotnet build` 编译通过
  - [ ] 新增字符串可通过 `L.T(L.Main_Filter_FilteredExtract)` 访问

  **QA Scenarios**:
  ```
  Scenario: JSON 格式校验
    Tool: Bash
    Steps:
      1. dotnet build src/MantisZip.UI/MantisZip.UI.csproj
    Expected Result: Build 成功
    Evidence: .sisyphus/evidence/task-7-l10n-valid.txt
  ```

  **Commit**: YES (group with 4, 5, 6)
  - Message: `feat(ui): add FilterTagSelector control and enhance filter bar`
  - Files: `Resources/strings.zh.json`, `Resources/strings.en.json`, `UI/Controls/FilterTagSelector.xaml`, `UI/Controls/FilterTagSelector.xaml.cs`, `MainWindow.xaml`, `MainWindow.UI.cs`

- [ ] 8. Extract 集成（ExtractSettingsWindow + MainWindow.Menu）

  **What to do**:
  - **`ExtractSettingsWindow.xaml.cs`**：
    - 构造函数新增可选参数 `FileFilterCriteria? prefillFilter = null`
    - 在窗口加载时（或初始化末尾），如果 `prefillFilter != null` 且 `FileFilterControl` 可用：
      ```csharp
      if (prefillFilter != null && FileFilterControl != null)
      {
          FileFilterControl.SetFilter(prefillFilter);
      }
      ```
    - 保持现有 `GetFilter()` / `GetFilteredEntryKeys()` 逻辑不变（预填后用户仍可手动修改）
  - **`MainWindow.Menu.cs`**：
    - `Extract_Click`（约 39 行）：
      - 在创建 `ExtractSettingsWindow` 之前，检查 `_filteredExtractEnabled`（复选框状态）
      - 如果启用且有激活的过滤条件：
        - 构建 `FileFilterCriteria` 从当前过滤状态
        - 传给 `ExtractSettingsWindow` 构造函数作为 `prefillFilter`
      - 如果未启用或无条件：不传参（保持现有行为）
    - `SmartExtract_Click`（约 133 行）：
      - 同样检查复选框
      - 如果启用且有激活的过滤条件：
        - 列出条目 → 用 `FileFilterMatcher.IsMatch` 过滤 → 只提取匹配条目
        - 使用 `engine.ExtractEntriesAsync()` 带过滤后的 key 列表
      - 如果未启用：保持现有行为

  **关于目录过滤行为（重要）**：
  - 在文件列表中，目录通过文字搜索匹配（以支持筛选到特定目录）
  - 但在对 `_allItems` 做提取过滤时，**目录条目自动排除**（只提取文件）
  - 逻辑：`_allItems.Where(i => !i.IsDirectory && FileFilterMatcher.IsMatch(filter, i))`

  **Avalonia 迁移备注**:
  - `ExtractSettingsWindow` → Avalonia `Window`（基类不同，逻辑可复用）
  - `FileFilterEditor.SetFilter()` → Avalonia 版本的同名方法
  - 预填逻辑（构造函数参数 → 初始化时调用 SetFilter）完全一样

  **Must NOT do**:
  - 不修改现有 ExtractSettingsWindow 的打开逻辑（无预填时行为不变）
  - 不改动 `--extract` CLI 路径（直接打开 ExtractSettingsWindow）

  **References**:
  - `Dialogs/ExtractSettingsWindow.xaml.cs` — 现有代码
  - `MainWindow/MainWindow.Menu.cs:39-132` — Extract_Click 流程
  - `MainWindow/MainWindow.Menu.cs:133-162` — SmartExtract_Click 流程
  - `AppPartials/App.Extract.cs` — CLI extract 路径（不修改）

  **Parallelization**:
  - Can Run In Parallel: YES (Wave 3, with Task 9, 10)
  - Blocked By: Task 5, Task 6

  **Acceptance Criteria**:
  - [ ] 编译通过
  - [ ] 未勾选「过滤解压」时，解压行为不变
  - [ ] 勾选 + 有过滤时，Extract_Click 打开 ExtractSettingsWindow 预填过滤条件
  - [ ] 勾选 + 有过滤时，SmartExtract_Click 只提取匹配文件
  - [ ] 无过滤时，复选框勾选不影响解压

  **QA Scenarios**:
  ```
  Scenario: 过滤解压 — Extract_Click
    Tool: 构建 + 启动应用
    Preconditions: 打开含混合文件的压缩包（如图片+文档）
    Steps:
      1. 筛选栏 Tag 选择器勾选 "图片"
      2. 勾选「以当前过滤解压」
      3. 点击 "解压到…"
      4. ExtractSettingsWindow 弹出，过滤 Tab 中已预填图片扩展名
      5. 确认解压 → 只解压出图片文件
    Expected Result: 解压内容与过滤条件一致
    Evidence: .sisyphus/evidence/task-8-filtered-extract.png

  Scenario: 过滤解压 — SmartExtract_Click
    Tool: 构建 + 启动应用
    Preconditions: 同上
    Steps:
      1. 勾选过滤解压 + 有过滤条件激活
      2. 点击 "智能解压"
      3. 直接提取匹配文件（无 ExtractSettingsWindow）
    Expected Result: 只提取匹配文件
    Evidence: .sisyphus/evidence/task-8-smart-extract.png
  ```

  **Commit**: NO (group with 9, 10)
  - Files: `Dialogs/ExtractSettingsWindow.xaml.cs`, `MainWindow/MainWindow.Menu.cs`

- [ ] 9. 预设联动 + ClearFiltersBtn 逻辑

  **What to do**:
  - **MainWindow.xaml.cs 新增字段**：
    ```csharp
    private bool _filteredExtractEnabled;  // 对应 FilteredExtractCheck.IsChecked
    private List<FileFilterPreset> _userPresets = new();
    ```
  - **窗口加载时初始化预设**：
    - 在 `LoadArchiveAsync` 或首次启动时，从 `AppSettings` 加载用户预设
    - 填充 `PresetCombo.ItemsSource`
  - **`PresetCombo_SelectionChanged`** 事件：
    - 选中预设 → 调用 `SetFilterFromPreset()`：
      - 将预设的 `Criteria` 中的扩展名/大小/日期填入筛选栏对应控件
      - **不覆盖** `FileSearchBox.Text` 和 `ExcludeBox.Text`（文字搜索独立）
      - 同步更新 `FilterTagSelector` 的勾选状态
    - 取消选择（null）→ 清空扩展名/大小/日期，文字搜索不变
  - **`FilterTagSelector.SelectionChanged`** 事件：
    - 用户手动更改扩展名 → 预设下拉置空（表示当前是自定义状态）
    - 触发 `RefreshFilter()`
  - **`ClearFiltersBtn_Click`** 增强：
    - 现有逻辑（清空文字/日期/大小）不变
    - **新增**：清空 `FilterTagSelector.SelectedExtensions`（取消全选）
    - **新增**：预设下拉置空
    - **新增**：取消勾选「以当前过滤解压」
  - **`HasActiveFilters()`** 增加 Tag 选择器状态检查：
    ```csharp
    private bool HasActiveFilters()
    {
        return !string.IsNullOrEmpty(_searchText)
            || !string.IsNullOrEmpty(_excludeText)
            || _dateFrom.HasValue
            || _dateTo.HasValue
            || _sizeMin.HasValue
            || _sizeMax.HasValue
            || FilterTagSelector.SelectedExtensions.Count > 0;
    }
    ```

  **预设 + 手动过滤合并策略**：
  - 预设 → 填充扩展名/大小/日期/文件名到筛选控件
  - 文字搜索（TextSearch/ExcludeText）→ 预设不覆盖
  - 用户手动修改任意过滤条件 → 预设下拉置空（显示"自定义"）
  - 这避免预设和手动编辑之间的状态冲突

  **Avalonia 迁移备注**:
  - 预设加载逻辑（从 AppSettings 读取 FileFilterPreset）完全复用
  - 事件处理器签名相同
  - CheckBox.IsChecked → Avalonia CheckBox.IsChecked（相同）

  **Must NOT do**:
  - 不改动 AppSettings 预设持久化逻辑
  - 不改动 FileFilterEditor 的预设管理

  **References**:
  - `MainWindow.UI.cs:629-651` — ClearFiltersBtn_Click
  - `MainWindow.UI.cs:428-436` — HasActiveFilters

  **Parallelization**:
  - Can Run In Parallel: YES (Wave 3, with Task 8, 10)
  - Blocked By: Task 5, Task 6

  **Acceptance Criteria**:
  - [ ] 预设下拉加载所有内置预设
  - [ ] 选中预设 → 扩展名/日期/大小控件自动填充
  - [ ] 预设不覆盖文字搜索框
  - [ ] 手动修改过滤条件 → 预设下拉置空
  - [ ] 清除按钮清空所有条件（含扩展名和预设）
  - [ ] 「以当前过滤解压」被清除按钮重置

  **QA Scenarios**:
  ```
  Scenario: 预设联动
    Tool: 构建 + 启动应用
    Steps:
      1. 在 Tag 选择器勾选 "图片", "音频" → 预设下拉显示"自定义"
      2. 选中预设 "📷 仅图片" → Tag 选择器清空并只勾选图片
      3. 文字搜索框内容保持不变（之前输入的内容不清除）
      4. 手动勾选 "文档" → 预设下拉回到"自定义"
      5. 点击清除按钮 → 所有条件清空，复选框取消勾选
    Expected Result: 联动逻辑正确
    Evidence: .sisyphus/evidence/task-9-preset-link.png
  ```

  **Commit**: NO (group with 8, 10)
  - Files: `MainWindow.xaml.cs`, `MainWindow.UI.cs`

- [ ] 10. 单元测试

  **What to do**:
  - 现有 `tests/MantisZip.Tests/` 中没有 `SearchFilters` 引用，检查确认
  - 如果存在 `FileListFilterTests.cs` 或其他引用 `SearchFilters`/`ArchiveFilter` 的测试，更新为 `FileFilterCriteria`/`FileFilterMatcher`
  - 新增测试覆盖新的 `FileFilterCriteria` 字段：
    - `TextSearch` + `ExcludeText` 对 `IsActive` 的影响
    - `FileFilterMatcher.IsMatch` 对目录的特殊处理
    - 预设 + 手动过滤组合场景
  - 运行 `dotnet test` 确保全部通过

  **Must NOT do**:
  - 不修改引擎测试（ZIP/7z/TarGz）

  **References**:
  - `tests/MantisZip.Tests/` — 测试项目目录

  **Parallelization**:
  - Can Run In Parallel: YES (Wave 3, with Task 8, 9)
  - Blocked By: Task 1 (需要 FileFilterCriteria 可用)

  **Acceptance Criteria**:
  - [ ] `dotnet test` 全部通过
  - [ ] 新增测试覆盖 TextSearch/ExcludeText/目录特殊处理

  **QA Scenarios**:
  ```
  Scenario: 测试覆盖
    Tool: Bash
    Steps:
      1. dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
    Expected Result: All tests pass
    Evidence: .sisyphus/evidence/task-10-tests.txt
  ```

  **Commit**: YES (group with 8, 9)
  - Message: `feat(ui): integrate filtered extraction and preset linking`
  - Files: `tests/MantisZip.Tests/...`

---

## Final Verification Wave

> 4 review agents run in PARALLEL. ALL must APPROVE.

- [ ] F1. **Plan Compliance Audit** — `oracle`
- [ ] F2. **Code Quality Review** — `unspecified-high`
- [ ] F3. **Real Manual QA** — `unspecified-high`
- [ ] F4. **Scope Fidelity Check** — `deep`

---

## Avalonia 迁移备注

> 以下标注每个 WPF 控件/功能在 Avalonia 中的对应关系和注意事项。

| WPF | Avalonia | 注意事项 |
|-----|----------|---------|
| `UserControl`（FilterTagSelector） | `UserControl` | Avalonia UserControl 类似，但基类在 `Avalonia.Controls` 命名空间 |
| `Popup` | `Popup` | Avalonia Popup 属性名略有不同：`PlacementTarget` → `PlacementTarget`（相同），`StaysOpen` → 需用 `IsOpen` 双向绑定 |
| `CheckBox` | `CheckBox` | 基本一致 |
| `ComboBox` | `ComboBox` | Avalonia ComboBox 的 ItemsSource 和 SelectedItem 用法相同 |
| `DatePicker` | `DatePicker` | Avalonia 有 DatePicker，API 类似 |
| `TextBox.TextChanged` | `TextBox.TextChanged` | 事件名相同 |
| `DynamicResource Theme_*` | `{DynamicResource}` | Avalonia 也支持 DynamicResource，但资源键名可能不同 |
| `StackPanel`, `Grid` | `StackPanel`, `Grid` | 布局控件相同 |
| `x:Name` 引用 | `x:Name` | 相同 |
| `Visibility.Collapsed` | `IsVisible = false` | Avalonia 用 `IsVisible` 属性替代 WPF 的 `Visibility` |
| `Window.GetWindow(this)` | `TopLevel.GetTopLevel(this)` | 获取父窗口的方式不同 |

### Core 层
`MantisZip.Core` 项目中的 `FileFilterCriteria`、`FileFilterMatcher`、`FileFilterPreset` 在 Avalonia 中**完全一样**，直接引用即可。`MantisZip.UI.Avalonia` 项目的 `.csproj` 中已应有 `ProjectReference` 指向 `MantisZip.Core`。

### FilterTagSelector 迁移要点
- XAML 中 `xmlns:controls="clr-namespace:MantisZip.UI.Controls"` → Avalonia 类似
- 事件处理器签名相同（`object sender, RoutedEventArgs e`）
- 代码-behind 逻辑（CheckBox 勾选→解析扩展名）完全复用

---

## Commit Strategy

- **1-3**: `refactor(core): unify filter data model, replace SearchFilters with FileFilterCriteria`
- **4-7**: `feat(ui): add FilterTagSelector control and enhance filter bar`
- **8-10**: `feat(ui): integrate filtered extraction and preset linking`
- **F1-F4**: `docs: update plans and migration notes`

---

## Success Criteria

### Verification Commands
```bash
dotnet build src/MantisZip.UI/MantisZip.UI.csproj  # 编译无错误
dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj  # 测试通过
```

### Final Checklist
- [ ] 所有 Must Have 已完成
- [ ] 所有 Must NOT Have 未被违反
- [ ] FileFilterCriteria 替代 SearchFilters，无遗留引用
- [ ] 预设下拉加载 FileFilterPreset.GetBuiltInPresets()
- [ ] FilterTagSelector 弹出菜单可勾选
- [ ] 「以当前过滤解压」功能正常
- [ ] 现有 ExtractSettingsWindow 行为不受影响（无预填时）
