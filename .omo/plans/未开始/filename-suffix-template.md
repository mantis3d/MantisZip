# 压缩文件名后缀模板

## TL;DR

> **Quick Summary**: 在压缩文件名中添加灵活的后缀模板（日期/时间/序号），防止不同日期的同文件名压缩包互相覆盖。采用占位符替换机制，支持 `{date}`、`{datetime}`、`{seq}`。
>
> **Deliverables**:
> - `FileNameTemplate` 占位符解析器（`Core/Utils/FileNameTemplate.cs`）
> - `CompressRequest.FileNameSuffixTemplate` 属性
> - `AppSettings.FileNameSuffixTemplate` 持久化
> - CompressSettingsWindow 模板文本框 + 帮助按钮（通用页）
> - 占位符帮助弹窗（类似 LogPrivacyHelpDialog）
> - HandleCompressQuick(CLI) 模板支持
> - 本地化字符串（zh + en）
> - xUnit 单元测试（`FileNameTemplateTests.cs`）
>
> **Estimated Effort**: Medium
> **Parallel Execution**: YES — 3 waves
> **Critical Path**: Task 1 → Task 2 → Task 4 → Task 7 → F1-F4

---

## Context

### Original Request
为压缩文件名添加后缀功能，支持日期/时间/序号占位符，使同名文件在不同日期压缩时不会互相覆盖。

### Interview Summary
**Key Discussions**:
- **模板语法**: 占位符替换（`{date}`、`{datetime}`、`{seq}`），非占位符文本保持原样
- **后缀位置**: 扩展名前（文档_2026-07-10.docx.zip），tar.gz 视为双扩展名
- **作用范围**: Separate 独立压缩模式 + --compress-quick 快速压缩
- **UI 位置**: CompressSettingsWindow 通用页 → 模板文本框 + "?" 帮助按钮
- **模板记忆**: 存 AppSettings，后续整合到 CompressPreset
- **测试**: FileNameTemplate 解析器需要 xUnit 单元测试

**Research Findings**:
- `ComputeSeparateOutputPath` (CompressService.cs:308-334) — 唯一 Separate 模式文件名生成点
- `GetOutputPaths` (CompressService.cs:93-108) — 委托给 `ComputeSeparateOutputPath`
- `HandleCompressQuick` (App.Open.cs:44) — 使用 Manual 模式，有独立路径计算逻辑
- `UpdateCommentDistributionState()` — UI 控件可见性跟随输出模式切换的模式参考

### Metis Review
**Identified Gaps** (addressed):
- **Quick Compress 架构差异**: HandleCompressQuick 使用 Manual 模式，需单独修改路径计算
- **共享逻辑**: 提取 `FileNameTemplate.ApplySuffix()` 供两处共同调用
- **DateTime 一致性**: 批次开始时捕获一次，防午夜边界跳动
- **占位符集合锁定**: 仅 {date} {datetime} {seq} {seq:NNN}，不扩展
- **模板文本框状态**: 仅在 Separate 模式下启用，遵循 `UpdateCommentDistributionState()` 模式

---

## Work Objectives

### Core Objective
在 MantisZip 的独立压缩模式和快速压缩模式中，支持用户自定义文件名后缀模板，使用占位符替换生成日期/时间/序号后缀。

### Concrete Deliverables
- `Core/Utils/FileNameTemplate.cs` — 占位符解析与渲染类
- `CompressRequest.FileNameSuffixTemplate` — 请求参数传递
- `CompressService.ComputeSeparateOutputPath` — 后缀应用
- `AppSettings.FileNameSuffixTemplate` — 模板持久化
- CompressSettingsWindow 通用页 — 模板文本框 + "?" 帮助按钮
- 占位符帮助弹窗
- `HandleCompressQuick` — CLI 模板支持
- `tests/MantisZip.Tests/Utils/FileNameTemplateTests.cs` — 单元测试
- Localization strings (zh + en)

### Definition of Done
- [ ] `FileNameTemplate.Render()` 正确处理所有占位符组合
- [ ] Separate 模式压缩文件自动应用后缀模板
- [ ] --compress-quick 应用 AppSettings 中保存的模板
- [ ] 模板文本框在 Separate 模式启用，其他模式禁用/隐藏
- [ ] "?" 帮助按钮打开占位符说明弹窗
- [ ] 模板值在 AppSettings 中持久化，再次打开自动填入
- [ ] 空/空白模板不改变现有行为

### Must Have
- `{date}` → `2026-07-10`（ISO 日期）
- `{datetime}` → `2026-07-10_153024`（日期时间，下划线分隔）
- `{seq}` → `001`（补零到 3 位）
- `{seq:0000}` → 自定义宽度 `0001`
- 纯文本占位符保持原样（`_backup` → `_backup`）
- 后缀加在扩展名前（KeepOriginalExtension=on 时在 .ext 前，off 时在 .zip 前）
- tar.gz 视为双扩展名
- 空模板 = 零行为变化

### Must NOT Have (Guardrails)
- 不添加 {originalname}、{ext}、{time}、{year} 等额外占位符
- 不创建 CompressPreset 系统
- 不修改 ArchiveOptions 或引擎内部
- 不修改 Manual/Combined 模式的文件名生成
- 不添加实时预览、自动完成或验证图标
- 不引入 mocking 框架或修改测试基础设施
- 不校验 Windows 非法文件名（用户输入的文本保持原样）

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (xUnit 2.9.2 in tests/MantisZip.Tests/)
- **Automated tests**: YES (TDD for FileNameTemplate parser)
- **Framework**: xUnit [Fact]
- **TDD**: FileNameTemplate 类采用 RED（编写测试）→ GREEN（实现）→ REFACTOR

### QA Policy
Every task MUST include agent-executed QA scenarios.

- **Library/Module tests**: Bash commands via `dotnet test` for unit tests
- **UI**: Verify via code review — template text box visibility follows output mode
- **CLI**: Verify via `dotnet run -- --compress-quick` with template set in AppSettings
- **Evidence**: Each scenario saves output/log to `.omo/evidence/task-{N}-{slug}.{ext}`

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Core — parallel foundation):
├── Task 1: FileNameTemplate 解析器 + 单元测试 (TDD)
└── Task 2: CompressRequest 属性 + ComputeSeparateOutputPath 修改

Wave 2 (Settings + UI — parallel):
├── Task 3: AppSettings 持久化
├── Task 4: CompressSettingsWindow 模板文本框 + 帮助按钮 (XAML)
├── Task 5: CompressSettingsWindow code-behind 集成
└── Task 6: HandleCompressQuick CLI 集成

Wave 3 (Polish — parallel):
├── Task 7: 占位符帮助弹窗
├── Task 8: 本地化字符串 (zh + en)
└── Task 9: 集成验证 — dotnet test + UI 流程检查

Wave FINAL (Parallel verification):
├── F1: Plan Compliance Audit (oracle)
├── F2: Code Quality Review (unspecified-high)
├── F3: Real Manual QA (unspecified-high)
└── F4: Scope Fidelity Check (deep)
```

### Dependency Matrix
- **Task 1**: None — Wave 1, Blocks: Task 2
- **Task 2**: Task 1 — Wave 1, Blocks: Task 3, 5, 6
- **Task 3**: Task 2 — Wave 2, Blocks: None
- **Task 4**: None — Wave 2, Blocks: Task 5
- **Task 5**: Task 4 — Wave 2, Blocks: None
- **Task 6**: Task 2 — Wave 2, Blocks: None
- **Task 7**: None — Wave 3, Blocks: None
- **Task 8**: None — Wave 3, Blocks: None
- **Task 9**: Task 1-8 — Wave 3
- **F1-F4**: Task 9 — Final Wave

### Critical Path
Task 1 → Task 2 → Task 5 → Task 9 → F1-F4

---

## TODOs

- [ ] 1. `FileNameTemplate` 解析器 + 单元测试 (TDD)

  **What to do**:
  - 新建 `src/MantisZip.Core/Utils/FileNameTemplate.cs`：
    - 静态类 `FileNameTemplate`，包含 `ApplySuffix(string template, int sequenceIndex, DateTime? now = null)` 方法
    - `now` 参数为可选（测试时注入固定时间，生产传 null 使用 `DateTime.Now`）
    - 占位符替换规则：
      - `{date}` → `DateTime.Now.ToString("yyyy-MM-dd")`
      - `{datetime}` → `DateTime.Now.ToString("yyyy-MM-dd_HHmmss")`
      - `{seq}` → `sequenceIndex.ToString("D3")`（补零 3 位）
      - `{seq:000}` → `sequenceIndex.ToString("D3")`，`{seq:0000}` → `D4`，以此类推
      - 不匹配任何占位符的文本保持原样
      - 空/空白模板返回空字符串
    - 全部纯静态方法，无状态，无 I/O
  - 新建 `tests/MantisZip.Tests/Utils/FileNameTemplateTests.cs`（TDD: 先写测试再实现）：
    - `[Fact]` `ApplySuffix_EmptyTemplate_ReturnsEmpty`
    - `[Fact]` `ApplySuffix_DatePlaceholder_RendersCorrectly`
    - `[Fact]` `ApplySuffix_DateTimePlaceholder_RendersCorrectly`
    - `[Fact]` `ApplySuffix_SeqPlaceholder_RendersPadded`
    - `[Fact]` `ApplySuffix_SeqWithCustomWidth_RendersCorrectly`
    - `[Fact]` `ApplySuffix_MixedPlaceholders_RendersCorrectly`
    - `[Fact]` `ApplySuffix_LiteralTextOnly_ReturnsAsIs`
    - `[Fact]` `ApplySuffix_UnknownPlaceholder_KeptAsLiteral`
    - `[Fact]` `ApplySuffix_NullTemplate_ReturnsEmpty`
    - 使用 `DateTime(2026, 7, 10, 15, 30, 24)` 固定时间注入测试

  **Must NOT do**:
  - 不要处理占位符大小写（仅小写 `{date}` 有效）
  - 不要校验 Windows 非法文件名
  - 不要添加 `{originalname}`、`{ext}` 等额外占位符
  - 不要使用 `String.Format` 解析 `{seq:000}` — 仅解析 `:` 后紧接的 0 序列

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Task 2)
  - **Blocks**: Task 2
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] `dotnet test --filter "FullyQualifiedName~FileNameTemplate"` — all 9 tests pass
  - [ ] `{date}` with fixed `DateTime(2026,7,10,15,30,24)` → `"2026-07-10"`
  - [ ] `{datetime}` with same time → `"2026-07-10_153024"`
  - [ ] `{seq}` with index=1 → `"001"`, index=12 → `"012"`, index=123 → `"123"`
  - [ ] `{seq:0000}` with index=5 → `"0005"`
  - [ ] `"_{date}"` with date → `"_2026-07-10"`
  - [ ] `"_backup"` → `"_backup"`
  - [ ] `"_{unknown}"` → `"_{unknown}"`
  - [ ] `""` → `""`, `"  "` → `""`

  **QA Scenarios**:
  ```
  Scenario: Unit tests pass
    Tool: Bash
    Preconditions: FileNameTemplateTests.cs written with 9 [Fact] methods
    Steps:
      1. Run: dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~FileNameTemplate" --configuration Release
    Expected Result: Test run succeeded (all 9 pass, 0 fail, 0 skip)
    Evidence: .omo/evidence/task-1-unit-tests.txt
  ```

  **Commit**: YES (groups with Task 2)
  - Message: `feat(core): add FileNameTemplate parser with unit tests`

- [ ] 2. `CompressRequest` 属性 + `ComputeSeparateOutputPath` 修改

  **What to do**:
  - 在 `CompressRequest` 类中新增属性（位于第 51 行 `KeepOriginalExtension` 之后）：
    ```csharp
    /// <summary>文件名后缀模板（支持 {date}/{datetime}/{seq} 占位符），null/空=不添加</summary>
    public string? FileNameSuffixTemplate { get; init; }
    ```
  - 修改 `ComputeSeparateOutputPath(CompressRequest request, string sourcePath)`：
    - 增加 `int itemIndex = 0` 参数（用于 `{seq}` 占位符，1-based）
    - 在计算完 `baseName` 后（第 326 行附近）、拼接扩展名前：
      ```csharp
      // 应用文件名后缀模板
      if (!string.IsNullOrEmpty(request.FileNameSuffixTemplate))
      {
          baseName += FileNameTemplate.ApplySuffix(
              request.FileNameSuffixTemplate,
              itemIndex + 1,  // 1-based
              DateTime.Now);
      }
      ```
  - 修改 `GetOutputPaths` 中对 `ComputeSeparateOutputPath` 的调用（第 100 行）：
    - 遍历时需要传递 `index`：`.Select((p, i) => ComputeSeparateOutputPath(request, p, i))`
  - 修改 `CompressSeparateAsync` 中对 `ComputeSeparateOutputPath` 的调用（第 171 行）：
    - 传入 `i`：`var outputPath = ComputeSeparateOutputPath(request, sourcePath, i);`
  - 重要：在 `CompressSeparateAsync` 开头捕获一次 `DateTime.Now`，传递给 `ComputeSeparateOutputPath` 确保批次内一致性

  **Must NOT do**:
  - 不修改 `ComputeSingleAsync`（Manual/Combined 模式不走此路径）
  - 不修改 `ComputeRenamedPath`（冲突重命名基于已加后缀的文件名）
  - 不修改 ArchiveOptions 或引擎层

  **References**:
  - `src/MantisZip.Core/Services/CompressService.cs:308-334` — `ComputeSeparateOutputPath` 方法体
  - `src/MantisZip.Core/Services/CompressService.cs:93-108` — `GetOutputPaths` 遍历调用
  - `src/MantisZip.Core/Services/CompressService.cs:170-171` — `CompressSeparateAsync` 调用点
  - `src/MantisZip.Core/Services/CompressService.cs:18-64` — `CompressRequest` 类定义

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Task 1)
  - **Blocks**: Task 3, Task 5, Task 6
  - **Blocked By**: Task 1

  **Acceptance Criteria**:
  - [ ] `CompressRequest.FileNameSuffixTemplate` property exists (string?, init-only)
  - [ ] `ComputeSeparateOutputPath` applies suffix when template is non-empty
  - [ ] `ComputeSeparateOutputPath` unchanged when template is null/empty
  - [ ] `GetOutputPaths` returns suffixed paths
  - [ ] `{seq}` increments correctly across items in batch
  - [ ] tar.gz: suffix placed before ".tar.gz", not between ".tar" and ".gz"
  - [ ] KeepOriginalExtension=true: `doc.txt` + `_{date}` → `doc_2026-07-10.txt.zip`
  - [ ] KeepOriginalExtension=false: `doc.txt` + `_{date}` → `doc_2026-07-10.zip`
  - [ ] DateTime captured once per batch (not per-file)

  **QA Scenarios**:
  ```
  Scenario: Separate mode applies suffix template correctly
    Tool: Bash
    Preconditions: dotnet build succeeds; test helper available
    Steps:
      1. Create temp file "test_doc.txt"
      2. Construct CompressRequest with Mode=Separate, FileNameSuffixTemplate="_{date}", SourcePaths=["test_doc.txt"]
      3. Call GetOutputPaths → verify result contains "test_doc_2026-07-10.zip"
    Expected Result: Output path includes the date suffix before .zip
    Evidence: .omo/evidence/task-2-output-path.txt

  Scenario: Empty template produces same path as before
    Tool: Bash
    Preconditions: Same as above, FileNameSuffixTemplate = null
    Steps:
      1. Construct CompressRequest without FileNameSuffixTemplate
      2. Call GetOutputPaths → verify result is "test_doc.zip"
    Expected Result: Output path unchanged from existing behavior
    Evidence: .omo/evidence/task-2-empty-template.txt
  ```

  **Commit**: YES (groups with Task 1)
  - Message: `feat(core): add FileNameSuffixTemplate to CompressRequest and output path`
  - Files: `src/MantisZip.Core/Services/CompressService.cs`

- [ ] 3. `AppSettings` 持久化

  **What to do**:
  - 在 `AppSettings.cs` 的压缩设置区域（第 18 行 `KeepOriginalExtension` 之后）新增：
    ```csharp
    /// <summary>文件名后缀模板（支持 {date}/{datetime}/{seq} 占位符），空=不添加</summary>
    public string FileNameSuffixTemplate { get; set; } = "";
    ```
  - 无需额外的序列化配置（`JsonSerializer` 自动处理 string 属性）
  - 加载/保存由 `AppSettings.Load()`/`Save()` 自动处理

  **Must NOT do**:
  - 不要修改 `SyncContextMenuToRegistry`
  - 不要添加预设系统

  **References**:
  - `src/MantisZip.UI/AppSettings.cs:12-23` — 压缩设置区域，在 `KeepOriginalExtension` 行后插入

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Task 4, 5, 6)
  - **Blocks**: None
  - **Blocked By**: Task 2

  **Acceptance Criteria**:
  - [ ] `AppSettings.FileNameSuffixTemplate` property exists, default `""`
  - [ ] Save → restart → Load preserves the value
  - [ ] New install = `""` (zero behavioral change)

  **QA Scenarios**:
  ```
  Scenario: AppSettings persistence round-trip
    Tool: Bash
    Preconditions: AppSettings with FileNameSuffixTemplate set to "_{date}"
    Steps:
      1. Call AppSettings.Instance.Save()
      2. Read settings.json from %LOCALAPPDATA%\MantisZip\settings.json
      3. Verify FileNameSuffixTemplate exists with value "_{date}"
    Expected Result: Setting persisted correctly
    Evidence: .omo/evidence/task-3-settings.txt
  ```

  **Commit**: YES (groups with 4-6)
  - Message: `feat(ui): add filename suffix template UI, settings, and CLI support`
  - Files: `src/MantisZip.UI/AppSettings.cs`

- [ ] 4. `CompressSettingsWindow` 模板文本框 + 帮助按钮 (XAML)

  **What to do**:
  - 在 `CompressSettingsWindow.xaml` 的通用页（General Tab）中，`Archive` GroupBox 内的**第 6 行（分卷大小下方，Grid.Row="7"）**添加后缀模板行：
    ```xml
    <!-- Row 7: FileNameSuffixTemplate -->
    <TextBlock Grid.Row="7" Grid.Column="0"
               x:Name="SuffixTemplateLabel"
               Text="{l:L Compress_TemplateSuffix}"
               VerticalAlignment="Center" Margin="0,10,0,0"/>
    <Grid Grid.Row="7" Grid.Column="1" Margin="0,10,0,0">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <TextBox x:Name="SuffixTemplateTextBox" Grid.Column="0" Height="24"
                 Text="_YYYY-MM-DD"
                 TextChanged="SuffixTemplateTextBox_TextChanged"/>
        <Button x:Name="SuffixTemplateHelpBtn" Grid.Column="1" Width="28" Height="24"
                Margin="6,0,0,0" Content="?" Click="SuffixTemplateHelpBtn_Click"/>
    </Grid>
    ```
  - 在 `Grid.RowDefinitions` 中增加一行 `<RowDefinition Height="Auto"/>`（在分卷行之后）
  - 更新 `RefreshOutputPathState()` 中的模式切换逻辑（在 `FileNameTextBox` 之后添加）：
    - Separate 模式: `SuffixTemplateTextBox` 和 `SuffixTemplateHelpBtn` 启用（`IsEnabled=true`）
    - Manual/Combined 模式: 禁用（`IsEnabled=false`，灰色但可见—让用户知道为什么不能用）

  **Must NOT do**:
  - 不要设置固定的 `Width`（已有统一高度 24）
  - 不要用 `Visibility.Collapsed`—使用 `IsEnabled` 禁用即可（用户可看到功能但知道当前模式不可用）
  - 不要使用系统默认颜色—所有控件绑定 `{DynamicResource Theme_*}`

  **References**:
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml:52-146` — Archive GroupBox 布局
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs:76-104` — `RefreshOutputPathState` 方法
  - `AGENTS.md` 中「规则 4：新 UI 控件必须应用主题样式」

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Task 3, 5, 6)
  - **Blocks**: Task 5
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] SuffixTemplateTextBox 和帮助按钮在 Separate 模式下可编辑
  - [ ] SuffixTemplateTextBox 在 Manual/Combined 模式下禁用（灰色）
  - [ ] 所有新控件绑定 `Theme_WindowBg`/`Theme_TextPrimary`/`Theme_Border`
  - [ ] XAML 编译通过，无绑定警告

  **QA Scenarios**:
  ```
  Scenario: UI mode switching affects template textbox state
    Tool: Playwright
    Preconditions: CompressSettingsWindow open with 2+ source files
    Steps:
      1. Click "手动输入路径" radio → verify SuffixTemplateTextBox is disabled
      2. Click "每项独立的压缩包" radio → verify SuffixTemplateTextBox is enabled
      3. Click "自动（父目录名）" radio → verify SuffixTemplateTextBox is disabled
    Expected Result: TextBox IsEnabled follows output mode
    Evidence: .omo/evidence/task-4-ui-mode-switch.png
  ```

  **Commit**: NO (groups with Task 5)
  - Files: `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml`

- [ ] 5. `CompressSettingsWindow` code-behind 集成

  **What to do**:
  - 在 `CompressSettingsWindow.xaml.cs` 中：
    - 初始化：从 `AppSettings.Instance.FileNameSuffixTemplate` 加载模板到 `SuffixTemplateTextBox.Text`
    - `LoadDefaultsFromSettings()` 之后读取 AppSettings 填充文本框
    - `UpdateSuffixTemplateState()` — 根据输出模式切换文本框启用状态
    - `SuffixTemplateTextBox_TextChanged` — 无特殊逻辑（纯文本框）
    - `SuffixTemplateHelpBtn_Click` — 打开占位符帮助弹窗
  - 在 `RunSeparateCompressAsync` 中传递模板：
    ```csharp
    FileNameSuffixTemplate = SuffixTemplateTextBox.Text?.Trim(),
    ```
    添加到 `CompressRequest` 初始化块中（第 496 行 `KeepOriginalExtension` 之后）
  - 在 `CompressButton_Click` 后保存模板到 `AppSettings`（压缩完成时或关闭时）：
    ```csharp
    AppSettings.Instance.FileNameSuffixTemplate = SuffixTemplateTextBox.Text?.Trim() ?? "";
    AppSettings.Instance.Save();
    ```
    （放在 `SavePasswordAfterCompress` 附近或关闭前）
  - 调用 `UpdateSuffixTemplateState()` 的位置：
    - `OutputMode_Changed` 中（切换模式时更新状态）
    - `Loaded` 事件中（初始化状态）

  **Must NOT do**:
  - 不要修改 Manual/Combined 模式的 CompressRequest 构造（它们不使用模板）
  - 不要保存空字符串到 AppSettings（避免写入默认值）

  **References**:
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs:60-74` — `OutputMode_Changed` 事件处理
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs:76-104` — `RefreshOutputPathState`
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs:475-501` — `RunSeparateCompressAsync` request 创建

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Task 3, 4, 6)
  - **Blocks**: None
  - **Blocked By**: Task 2, Task 4

  **Acceptance Criteria**:
  - [ ] SuffixTemplateTextBox 从 AppSettings 加载初始值
  - [ ] Separate 模式请求中包含 `FileNameSuffixTemplate`
  - [ ] 压缩后模板值保存到 AppSettings
  - [ ] `UpdateSuffixTemplateState()` 在模式切换时正确更新

  **QA Scenarios**:
  ```
  Scenario: Template value flows from UI to CompressRequest
    Tool: Playwright + Bash
    Preconditions: CompressSettingsWindow open in Separate mode
    Steps:
      1. Type "_{date}" into SuffixTemplateTextBox
      2. Click Compress button
      3. Verify CompressRequest passed to CompressService contains FileNameSuffixTemplate = "_{date}"
    Expected Result: Template value passed through correctly
    Evidence: .omo/evidence/task-5-request-flow.txt
  ```

  **Commit**: YES (groups with 4)
  - Message: `feat(ui): integrate filename suffix template with CompressSettingsWindow`
  - Files: `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs`

- [ ] 6. `HandleCompressQuick` CLI 集成

  **What to do**:
  - 在 `HandleCompressQuick` (App.Open.cs:44) 中修改自动输出路径计算：
    - 读取 `AppSettings.Instance.FileNameSuffixTemplate`
    - 计算完 `baseName` 后（第 75 行）、拼接扩展名前，应用模板：
      ```csharp
      var suffixTemplate = settings.FileNameSuffixTemplate;
      if (!string.IsNullOrEmpty(suffixTemplate))
      {
          baseName += FileNameTemplate.ApplySuffix(suffixTemplate, 1, DateTime.Now);
      }
      ```
    - 注意：Quick Compress 始终是单压缩包，所以 `{seq}` 固定为 `001`

  **Must NOT do**:
  - 不修改 `HandleCompressQuick` 的压缩逻辑（仍然是 Manual 模式单压缩包）
  - 不改变 CLI 参数解析

  **References**:
  - `src/MantisZip.UI/AppPartials/App.Open.cs:63-78` — Quick Compress 输出路径计算

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Task 3, 4, 5)
  - **Blocks**: None
  - **Blocked By**: Task 2

  **Acceptance Criteria**:
  - [ ] `--compress-quick single.txt` + 模板 `_{date}` → 输出 `single_2026-07-10.zip`
  - [ ] 模板为空时行为与之前完全一致
  - [ ] `{seq}` 在 Quick Compress 中渲染为 `001`

  **QA Scenarios**:
  ```
  Scenario: Quick Compress applies suffix template
    Tool: Bash
    Preconditions: AppSettings.FileNameSuffixTemplate = "_{date}"; temp file exists
    Steps:
      1. dotnet run --project src\MantisZip.UI\MantisZip.UI.csproj -- --compress-quick E:\temp\testfile.txt
      2. Check output path includes _2026-07-10 suffix before extension
    Expected Result: testfile_2026-07-10.zip created
    Evidence: .omo/evidence/task-6-quick-compress.txt
  ```

  **Commit**: YES (groups with 3-5)
  - Message: `feat(ui): add filename suffix template UI, settings, and CLI support`
  - Files: `src/MantisZip.UI/AppPartials/App.Open.cs`

- [ ] 7. 占位符帮助弹窗

  **What to do**:
  - 仿照 `LogPrivacyHelpDialog` 的轻量弹窗样式，创建 `SuffixTemplateHelpDialog`：
    - 新建 `src/MantisZip.UI/Dialogs/SuffixTemplateHelpDialog.xaml` + `.cs`
    - Window 属性：`Width="400" Height="Auto" SizeToContent="Height" ResizeMode="NoResize"`
    - 内容：一个简易表格显示 placeholder → 说明 → 示例
    - 布局：
      ```
      ┌─────────────────────────────────────┐
      │ 📋 可用占位符                        │
      │─────────────────────────────────────│
      │ {date}     日期      2026-07-10    │
      │ {datetime} 日期时间  2026-07-10_…  │
      │ {seq}      序号      001, 002…     │
      │ {seq:000}  自定义宽度 0001, 0002…  │
      │─────────────────────────────────────│
      │ 💡 其他文本保持原样。示例：           │
      │ _backup → 文档_backup.docx.zip      │
      │ _{date} → 文档_2026-07-10.docx.zip  │
      │─────────────────────────────────────│
      │                            [知道了]  │
      └─────────────────────────────────────┘
      ```
    - 所有控件绑定主题色
  - `SuffixTemplateHelpBtn_Click` 中打开此弹窗：
    ```csharp
    var dlg = new SuffixTemplateHelpDialog();
    dlg.Owner = this;
    dlg.ShowDialog();
    ```

  **Must NOT do**:
  - 不要添加交互式预览或示例输入框
  - 不要引用 `System.Windows.Documents`（无富文本）

  **References**:
  - `src/MantisZip.UI/Dialogs/LogPrivacyHelpDialog.xaml` — 参考样式和结构

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Task 8, 9)
  - **Blocks**: None
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] Help button opens dialog
  - [ ] Dialog shows all 4 placeholder entries with descriptions
  - [ ] Dialog shows examples
  - [ ] "知道了" button closes dialog
  - [ ] Dialog uses theme colors

  **QA Scenarios**:
  ```
  Scenario: Help dialog opens and displays correctly
    Tool: Playwright
    Preconditions: CompressSettingsWindow open, Separate mode selected
    Steps:
      1. Click "?" button next to SuffixTemplateTextBox
      2. Verify dialog shows with all placeholders listed
      3. Click "知道了" button
    Expected Result: Dialog opens, shows placeholders, closes on button click
    Evidence: .omo/evidence/task-7-help-dialog.png
  ```

  **Commit**: YES (groups with 8)
  - Message: `feat(ui): add placeholder help dialog and localization strings`
  - Files:
    - `src/MantisZip.UI/Dialogs/SuffixTemplateHelpDialog.xaml`
    - `src/MantisZip.UI/Dialogs/SuffixTemplateHelpDialog.xaml.cs`

- [ ] 8. 本地化字符串

  **What to do**:
  - 在 `L.cs`（或 `Localization/L.cs`）中添加 key 常量：
    ```csharp
    public const string Compress_TemplateSuffix = "Compress_TemplateSuffix";
    public const string Compress_TemplateHelp = "Compress_TemplateHelp";
    public const string Compress_TemplateHelp_Title = "Compress_TemplateHelp_Title";
    public const string Compress_TemplatePlaceholder_Date = "Compress_TemplatePlaceholder_Date";
    public const string Compress_TemplatePlaceholder_DateTime = "Compress_TemplatePlaceholder_DateTime";
    public const string Compress_TemplatePlaceholder_Seq = "Compress_TemplatePlaceholder_Seq";
    public const string Compress_TemplatePlaceholder_SeqCustom = "Compress_TemplatePlaceholder_SeqCustom";
    public const string Compress_TemplateHelp_Example = "Compress_TemplateHelp_Example";
    ```
  - 在 `strings.zh.json` 中添加翻译：
    ```json
    "Compress_TemplateSuffix": "文件名后缀:",
    "Compress_TemplateHelp": "占位符说明",
    "Compress_TemplateHelp_Title": "可用占位符",
    "Compress_TemplatePlaceholder_Date": "当前日期",
    "Compress_TemplatePlaceholder_DateTime": "当前日期时间",
    "Compress_TemplatePlaceholder_Seq": "序号（补零3位）",
    "Compress_TemplatePlaceholder_SeqCustom": "序号（自定义宽度）",
    "Compress_TemplateHelp_Example": "示例：{1}"
    ```
    （注意：固定模板示例需要用字符串拼接或 `L.TF`）
  - 在 `strings.en.json` 中添加英文翻译：
    ```json
    "Compress_TemplateSuffix": "Filename suffix:",
    "Compress_TemplateHelp": "Placeholder reference",
    "Compress_TemplateHelp_Title": "Available placeholders",
    "Compress_TemplatePlaceholder_Date": "Current date",
    "Compress_TemplatePlaceholder_DateTime": "Current date/time",
    "Compress_TemplatePlaceholder_Seq": "Sequence number (3-digit padded)",
    "Compress_TemplatePlaceholder_SeqCustom": "Sequence number (custom width)",
    "Compress_TemplateHelp_Example": "Examples: {1}"
    ```
  - 在 XAML 中使用 `{l:L Compress_TemplateSuffix}` 绑定标签文本
  - 在帮助弹窗中使用 `L.T(L.Compress_TemplatePlaceholder_Date)` 等

  **Must NOT do**:
  - 不要在中英文字符串中包含具体的日期示例（由代码动态生成）

  **References**:
  - `src/MantisZip.UI/Resources/strings.zh.json` — 中文翻译
  - `src/MantisZip.UI/Resources/strings.en.json` — 英文翻译
  - `src/MantisZip.UI/Localization/L.cs` — Key 常量定义

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Task 7, 9)
  - **Blocks**: Task 7 (XAML 绑定依赖 key)
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] All new keys defined in L.cs
  - [ ] zh.json contains all keys with Chinese translation
  - [ ] en.json contains all keys with English translation
  - [ ] XAML `{l:L Compress_TemplateSuffix}` renders correctly

  **QA Scenarios**:
  ```
  Scenario: Localization strings loaded correctly
    Tool: Bash
    Preconditions: Project builds
    Steps:
      1. Run: dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected Result: Build succeeds (no missing resource errors)
    Evidence: .omo/evidence/task-8-localization-build.txt
  ```

  **Commit**: YES (groups with 7)
  - Message: `feat(i18n): add placeholder help dialog and localization strings`
  - Files:
    - `src/MantisZip.UI/Resources/strings.zh.json`
    - `src/MantisZip.UI/Resources/strings.en.json`
    - `src/MantisZip.UI/Localization/L.cs`

- [ ] 9. 集成验证

  **What to do**:
  - 验证整个功能链的端到端正确性：
    1. `dotnet build` — 编译通过
    2. `dotnet test --filter "FullyQualifiedName~FileNameTemplate"` — 9 个单元测试通过
    3. 验证 `GetOutputPaths` 返回正确带后缀的路径
    4. 验证空模板 = 零回归
    5. 验证 `ComputeSeparateOutputPath` 边界情况：tar.gz、KeepOriginalExtension 开关
  - 编写一个简单的集成验证脚本（可选）：
    ```powershell
    # 验证模板应用
    dotnet test --filter "FullyQualifiedName~FileNameTemplate"
    if ($LASTEXITCODE -ne 0) { throw "Unit tests failed" }
    ```

  **Must NOT do**:
  - 不要修改已有的测试
  - 不需要端到端的 UI 自动化测试（Agent QA 已覆盖）

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Task 7, 8)
  - **Blocks**: F1-F4
  - **Blocked By**: Task 1-8

  **Acceptance Criteria**:
  - [ ] `dotnet build` — succeeded
  - [ ] `dotnet test --filter "FullyQualifiedName~FileNameTemplate"` — all pass
  - [ ] `dotnet test` — no regressions (existing tests still pass)
  - [ ] Existing `CompressServiceTests` unchanged

  **Commit**: NO

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. Verify all Must Have implemented, all Must NOT Have absent. Check evidence files exist. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build` + `dotnet test`. Review changed files for: `as any`/`@ts-ignore` equivalents, empty catches, console.log in prod, unused imports, excessive comments, over-abstraction.
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | Files [N clean/N issues] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Start from clean state. Execute EVERY QA scenario from EVERY task. Test cross-task integration. Save to `.omo/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | Integration [N/N] | Edge Cases [N tested] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff. Verify 1:1 — everything built, nothing beyond spec. Check "Must NOT do" compliance.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

- **1-2**: `feat(core): add FileNameTemplate parser and CompressRequest integration`
- **3-6**: `feat(ui): add filename suffix template UI, settings, and CLI support`
- **7-8**: `feat(ui): add placeholder help dialog and localization strings`
- **9**: `test: integration verification`

---

## Success Criteria

### Verification Commands
```bash
dotnet test --filter "FullyQualifiedName~FileNameTemplate"  # Expected: All pass
dotnet build src\MantisZip.UI\MantisZip.UI.csproj  # Expected: Build succeeded
```

### Final Checklist
- [ ] All 9 FileNameTemplate unit tests pass
- [ ] Separate mode compression applies suffix template
- [ ] HandleCompressQuick applies suffix from AppSettings
- [ ] Template text box visible only in Separate mode
- [ ] "?" help button opens placeholder reference dialog
- [ ] Template value persists across app restarts
- [ ] Empty template = no behavioral change
- [ ] tar.gz suffix placed before `.tar.gz`, not between `.tar` and `.gz`
