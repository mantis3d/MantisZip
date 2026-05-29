# ExtractSettingsWindow — 独立解压设置窗口

> **状态**: ✅ 已完成 | **阶段**: [■■■■] (4/4)
> **来源**: 从 [batch-progress-list.md](batch-progress-list.md) Task 8 提取独立执行

---

## TL;DR

> **Quick Summary**: 从批处理进度窗口计划中提取 ExtractSettingsWindow 独立先做。创建解压设置窗口，统一替换 `--extract` 命令的 VistaFolderBrowserDialog，支持单文件/多文件的 4 种输出模式选择。
>
> **Deliverables**:
> - `MantisZip.Core/Models/ExtractOutputMode.cs` — 输出模式枚举
> - `MantisZip.UI/ExtractSettingsWindow.xaml` — 窗口 UI
> - `MantisZip.UI/ExtractSettingsWindow.xaml.cs` — 窗口逻辑
> - `MantisZip.UI/Localization/L.cs` + `strings.zh.json` + `strings.en.json` — ~10 个本地化键
>
> **Estimated Effort**: ~1.5 小时
> **Parallel Execution**: YES — 2 waves
> **Critical Path**: Task 1/2 → Task 3/4 → F1-F4

---

## Context

### Original Request
从 `batch-progress-list.md` 计划中将 ExtractSettingsWindow 提取出来独立先做，不等待 IPC 合并基础设施和其他批处理任务。

### Interview Summary
**已确认的设计决策**（与用户讨论后敲定）：
- **适用范围**: 单文件 + 多文件统一弹此窗口（替换 VistaFolderBrowserDialog）
- **输出模式**: 全部 4 种可用 — 手动输入 / 解压到此处 / 智能解压 / 解压到压缩包名
- **多文件语义**: 逐项应用（per-archive），不限制模式
- **默认模式**: "解压到压缩包名"（最安全，天然隔离）
- **文件列表**: 只读显示（轻量版 A），后续可随时升级到可编辑版 B
- **主要交互**: 选输出模式 → 手动模式下选目录 → [解压]/[取消]

### 与原始计划的关系
```
batch-progress-list.md (原有):
  Task 7: IPC 合并基础设施 ──→  Task 8: ExtractSettingsWindow ──→  Task 9: HandleExtract*
                                      ↑
本计划（独立提取）: ExtractSettingsWindow 先做，不依赖 IPC 基础设施
  完成后：HandleExtract* 改造（Task 9）和 IPC 合并（Task 7）后续在原始计划中完成
```

---

## Work Objectives

### Core Objective
创建 ExtractSettingsWindow，提供一个统一的解压设置窗口，支持文件列表显示和 4 种输出模式选择。

### Concrete Deliverables
- `src/MantisZip.Core/Models/ExtractOutputMode.cs` — 枚举定义
- `src/MantisZip.UI/ExtractSettingsWindow.xaml` — 窗口 UI
- `src/MantisZip.UI/ExtractSettingsWindow.xaml.cs` — 窗口逻辑
- 更新 `L.cs` + `strings.zh.json` + `strings.en.json` — 本地化键

### Definition of Done
- [x] `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` — 0 errors
- [x] 窗口构造参数正确接收文件列表
- [x] 4 种输出模式 Radio 切换正确
- [x] 手动模式下 TextBox + Browse 可用，其他模式只读预览
- [x] DialogResult 返回 `(SelectedPaths, OutputMode, CustomDestination)`

### Must Have
- ExtractOutputMode 枚举（Here, Smart, ToName, Manual 4 个值）
- 窗口显示文件列表（只读，带文件计数）
- 4 种输出模式 RadioButton
- 手动模式：TextBox + 浏览按钮 (VistaFolderBrowserDialog)
- 非手动模式：只读路径预览 TextBlock
- [解压] 按钮 → DialogResult = true → 返回配置
- [取消] 按钮 → DialogResult = false → 关闭

### Must NOT Have (Guardrails)
- ❌ 不在窗口中执行解压（只收集配置，调用方负责执行）
- ❌ 不添加/移除文件功能（已决定先做轻量只读版 A）
- ❌ 不碰 IPC 合并、ProgressWindow、HandleExtract* 等代码
- ❌ 不修改现有的 4 个 CLI 入口（`--extract-here/smart/to-name/extract`）
- ❌ 不添加关于"为空列表禁用解压按钮"之外的额外校验逻辑

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (xUnit)
- **Automated tests**: None for this window (WPF UI 窗口不便于纯单元测试，验证通过构建 + 手动脚本检查)
- **Agent QA**: 通过构建编译 + 反射/代码审查验证公共成员正确性

### QA Policy
验证通过 `dotnet build` + 代码结构审查 + 公共接口一致性检查。

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Start Immediately — foundation, parallel):
├── Task 1: ExtractOutputMode 枚举 (Core) [~5 min]
└── Task 2: 本地化字符串 — L.cs + JSON [~10 min]

Wave 2 (After Wave 1 — window implementation):
├── Task 3: ExtractSettingsWindow.xaml 窗口布局 [~25 min]
└── Task 4: ExtractSettingsWindow.xaml.cs 窗口逻辑 [~30 min]

Wave FINAL (build + review):
├── F1-F4: Verification [~15 min]
```

### Dependency Matrix
```
Task                    Depends On          Blocks
───                     ──────────          ──────
1 (enum)                —                   4
2 (i18n)                —                   3
3 (XAML)                2                   4
4 (code-behind)         1, 3                F1-F4
F1-F4                   all                 —
```

### Time Estimate
| Task | Estimate | Notes |
|------|----------|-------|
| 1. ExtractOutputMode enum | 5 min | 4 个值的枚举，独立文件 |
| 2. Localization keys | 10 min | ~10 个键，改 3 个文件 |
| 3. ExtractSettingsWindow.xaml | 25 min | 参考 CompressSettingsWindow 布局 |
| 4. ExtractSettingsWindow.xaml.cs | 30 min | Radio 联动、Browse 对话框、DialogResult |
| F1-F4 Verification | 15 min | 构建 + 审查 |
| **Total** | **~1.5 小时** | |

---

## TODOs

- [x] 1. **创建 ExtractOutputMode 枚举**

  **What to do**:
  - 在 `src/MantisZip.Core/Models/` 下新建 `ExtractOutputMode.cs`
  - 定义枚举：
    ```csharp
    namespace MantisZip.Core.Models;

    public enum ExtractOutputMode
    {
        Here,    // 解压到此处（压缩包所在目录）
        Smart,   // 智能解压（分析结构后自动选择）
        ToName,  // 解压到压缩包名（所在目录/包名/）
        Manual   // 手动输入（用户指定目录）
    }
    ```
  - 放在 Core 项目的原因：UI 层（ExtractSettingsWindow）和 CLI 层（App.Cli.cs → HandleExtractBatch）都需要引用
  - 命名空间 `MantisZip.Core.Models`（Core 项目已有 `Models` 目录）

  **Must NOT do**:
  - 不添加额外属性或方法（纯枚举，保持简单）
  - 不加 `Description` attribute（本地化在 UI 层处理）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1)
  - **Parallel Group**: Wave 1 (with Task 2)
  - **Blocks**: Task 4
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.Core/Models/` — Core 项目已有 Models 目录，确认存在
  - `MantisZip.Core/MantisZip.Core.csproj` — 不需要修改 csproj（Models 目录已包含在默认编译中）

  **Acceptance Criteria**:
  - [ ] `dotnet build src/MantisZip.Core/MantisZip.Core.csproj` — 0 errors
  - [ ] 枚举包含 4 个值：Here, Smart, ToName, Manual
  - [ ] 命名空间 `MantisZip.Core.Models`

  **QA Scenarios**:
  ```
  Scenario: 枚举编译通过
    Tool: Bash (dotnet build)
    Steps:
      1. dotnet build src/MantisZip.Core/MantisZip.Core.csproj
    Expected Result: Build succeeded, 0 errors
    Evidence: .sisyphus/evidence/task-1-build.txt

  Scenario: 枚举值完整
    Tool: Bash (通过 grep 验证)
    Steps:
      1. 确认文件中包含 "Here,", "Smart,", "ToName,", "Manual"
    Expected Result: 4 个枚举值全部存在
    Evidence: .sisyphus/evidence/task-1-enum-values.txt
  ```

  **Commit**: YES
  - Message: `feat(core): add ExtractOutputMode enum`
  - Files: `src/MantisZip.Core/Models/ExtractOutputMode.cs`

---

- [x] 2. **添加本地化字符串 — L.cs + strings.zh.json + strings.en.json**

  **What to do**:
  - 在 `L.cs` 中添加以下常量（按字母顺序插入到 `Extract` 相关区段附近）：
    ```csharp
    public const string ExtractSettings_Title                = "ExtractSettings_Title";
    public const string ExtractSettings_FileCount             = "ExtractSettings_FileCount";
    public const string ExtractSettings_Mode_Manual           = "ExtractSettings_Mode_Manual";
    public const string ExtractSettings_Mode_Here             = "ExtractSettings_Mode_Here";
    public const string ExtractSettings_Mode_Smart            = "ExtractSettings_Mode_Smart";
    public const string ExtractSettings_Mode_ToName           = "ExtractSettings_Mode_ToName";
    public const string ExtractSettings_ModePreview           = "ExtractSettings_ModePreview";
    public const string ExtractSettings_Browse                = "ExtractSettings_Browse";
    public const string ExtractSettings_Extract               = "ExtractSettings_Extract";
    public const string ExtractSettings_Cancel                = "ExtractSettings_Cancel";
    public const string ExtractSettings_ManualPathPlaceholder = "ExtractSettings_ManualPathPlaceholder";
    ```

  - 在 `strings.zh.json` 中添加：
    ```json
    "ExtractSettings_Title": "解压设置",
    "ExtractSettings_FileCount": "已选择 {0} 个压缩包",
    "ExtractSettings_Mode_Manual": "手动输入",
    "ExtractSettings_Mode_Here": "解压到此处",
    "ExtractSettings_Mode_Smart": "智能解压",
    "ExtractSettings_Mode_ToName": "解压到压缩包名",
    "ExtractSettings_ModePreview": "输出路径预览",
    "ExtractSettings_Browse": "浏览…",
    "ExtractSettings_Extract": "解压",
    "ExtractSettings_Cancel": "取消",
    "ExtractSettings_ManualPathPlaceholder": "选择或输入解压目标目录"
    ```

  - 在 `strings.en.json` 中添加对应英文翻译：
    ```json
    "ExtractSettings_Title": "Extract Settings",
    "ExtractSettings_FileCount": "{0} archive(s) selected",
    "ExtractSettings_Mode_Manual": "Manual",
    "ExtractSettings_Mode_Here": "Extract Here",
    "ExtractSettings_Mode_Smart": "Smart Extract",
    "ExtractSettings_Mode_ToName": "Extract to Named Folder",
    "ExtractSettings_ModePreview": "Output Path Preview",
    "ExtractSettings_Browse": "Browse…",
    "ExtractSettings_Extract": "Extract",
    "ExtractSettings_Cancel": "Cancel",
    "ExtractSettings_ManualPathPlaceholder": "Select or enter extract destination"
    ```

  - ⚠️ 注意：`L.cs` 文件头有 `// <auto-generated />` 注释，但实际是手动维护的。保持现有格式风格，按字母顺序插入。

  **Must NOT do**:
  - 不修改现有的本地化键
  - 不改变 `L.cs` 的命名空间或类结构

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1)
  - **Parallel Group**: Wave 1 (with Task 1)
  - **Blocks**: Task 3
  - **Blocked By**: None

  **References**:
  - `MantisZip.UI/Localization/L.cs` — 现有键模式（全部 `public const string`）
  - `MantisZip.UI/Resources/strings.zh.json` — 中文翻译
  - `MantisZip.UI/Resources/strings.en.json` — 英文翻译

  **Acceptance Criteria**:
  - [ ] `dotnet build src/MantisZip.UI/MantisZip.UI.csproj` — 0 errors
  - [ ] 所有 11 个新键在 3 个文件中都存在
  - [ ] 中英文翻译完整

  **QA Scenarios**:
  ```
  Scenario: 构建通过
    Tool: Bash (dotnet build)
    Steps:
      1. dotnet build src/MantisZip.UI/MantisZip.UI.csproj
    Expected Result: Build succeeded, 0 errors
    Evidence: .sisyphus/evidence/task-2-build.txt

  Scenario: 键值完整性（验证文件行数）
    Tool: Bash (grep)
    Steps:
      1. 确认 L.cs 包含 ExtractSettings_Title 等 11 个新常量
      2. 确认 strings.zh.json 包含对应项
      3. 确认 strings.en.json 包含对应项
    Expected Result: 所有文件键完整
    Evidence: .sisyphus/evidence/task-2-keys.txt
  ```

  **Commit**: NO (groups with Task 3/4)

---

- [x] 3. **创建 ExtractSettingsWindow.xaml 窗口布局**

  **What to do**:

  新建 `src/MantisZip.UI/ExtractSettingsWindow.xaml`，参考 `CompressSettingsWindow.xaml` 的布局风格，但简化（无 TabControl）：

  ```
  Window (Width="480", SizeToContent="Height", MinHeight="400"
          WindowStartupLocation="CenterScreen", ResizeMode="NoResize")
  ├── Grid (Margin="15")
  │   ├── Row 0 (Auto): 标题 + 文件计数
  │   │   ├── TextBlock: 标题 "解压设置"
  │   │   └── TextBlock: "已选择 {n} 个压缩包"
  │   ├── Row 1 (*): 文件列表 ListBox（只读，可滚动）
  │   │   └── ItemTemplate 显示文件名（不带路径，完整路径在 ToolTip）
  │   ├── Row 2 (Auto): 分隔线 Separator
  │   ├── Row 3 (Auto): "输出模式" 标签
  │   ├── Row 4 (Auto): 4 个 RadioButton（StackPanel）
  │   │   ├── RadioButton: 手动输入（Checked → 模式: Manual）
  │   │   ├── RadioButton: 解压到此处（Checked → 模式: Here）
  │   │   ├── RadioButton: 智能解压（Checked → 模式: Smart）
  │   │   └── RadioButton: 解压到压缩包名（Checked → 模式: ToName）
  │   ├── Row 5 (Auto): 路径选择区域
  │   │   ├── Grid: TextBox + [浏览] 按钮（仅手动模式启用）
  │   │   └── TextBlock: 路径预览（非手动模式显示自动路径）
  │   └── Row 6 (Auto): 底部按钮行
  │       ├── Button: [取消] (IsCancel=true)
  │       └── Button: [解压] (IsDefault=true)
  ```

  **XAML 关键要素**：
  - 使用 `DynamicResource` 引用主题色（`Theme_WindowBg`, `Theme_TextPrimary`, `Theme_Border`, `Theme_Accent`, `Theme_ButtonHover`, `Theme_HeaderBg`）—— 与现有窗口一致
  - ListBox 使用 `ScrollViewer` 固定高度（MaxHeight="200"）
  - 本地化绑定：`Text="{l:L ExtractSettings_Title}"`（注意 l:L 标记扩展用法与 CompressSettingsWindow 一致）
  - RadioButton 的 `GroupName="OutputMode"`，`Checked` 事件统一由 `OutputMode_Changed` 处理
  - 手动输入行 Grid 包含 TextBox 和 Browse 按钮
  - 非手动模式下路径预览用只读 TextBlock
  - 底部按钮使用 `HorizontalAlignment="Right"` + `StackPanel Orientation="Horizontal"`

  **Must NOT do**:
  - 不引入新的样式或资源字典（复用现有主题资源）
  - 不添加 TabControl（保持单页简单布局）
  - 不在此 XAML 中添加文件添加/移除按钮（留给版本 B）

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2
  - **Blocks**: Task 4
  - **Blocked By**: Task 2

  **References**:
  - `MantisZip.UI/CompressSettingsWindow.xaml` — 主要布局参考（Grid 结构、主题资源引用、本地化绑定方式）
  - `MantisZip.UI/ProgressWindow.xaml` — 次要参考（ListBox 样式、动态资源使用）
  - `MantisZip.UI/Localization/L.cs` — 新加的本地化键

  **Acceptance Criteria**:
  - [ ] `dotnet build src/MantisZip.UI/MantisZip.UI.csproj` — 0 errors
  - [ ] XAML 编译通过，无绑定错误
  - [ ] ListBox 显示文件列表（测试用硬编码数据验证）
  - [ ] 4 个 RadioButton 同属 GroupName="OutputMode"
  - [ ] 手动模式行包含 TextBox + Browse 按钮

  **QA Scenarios**:
  ```
  Scenario: XAML 编译通过
    Tool: Bash (dotnet build)
    Steps:
      1. dotnet build src/MantisZip.UI/MantisZip.UI.csproj
    Expected Result: Build succeeded, 0 errors
    Evidence: .sisyphus/evidence/task-3-build.txt

  Scenario: XAML 结构校验
    Tool: Bash (grep 验证关键元素)
    Steps:
      1. grep -c "RadioButton" ExtractSettingsWindow.xaml → 至少 4 个
      2. grep -c "GroupName=.OutputMode." → 出现
      3. grep -c "ListBox" → 出现
      4. grep -c "Button" → 至少 2 个（解压 + 取消）
    Expected Result: 所有关键元素存在
    Evidence: .sisyphus/evidence/task-3-structure.txt
  ```

  **Commit**: NO (groups with Task 4)

---

- [x] 4. **创建 ExtractSettingsWindow.xaml.cs 窗口逻辑**

  **What to do**:

  ```csharp
  namespace MantisZip.UI;

  public partial class ExtractSettingsWindow : Window
  {
      // ── Public Properties (供调用方读取) ──
      public List<string> SelectedPaths { get; }
      public ExtractOutputMode OutputMode { get; private set; }
      public string? CustomDestination { get; private set; }

      // ── Internal State ──
      private readonly ObservableCollection<string> _files;

      public ExtractSettingsWindow(IReadOnlyList<string> archivePaths)
      {
          InitializeComponent();

          _files = new ObservableCollection<string>(archivePaths);
          FileListBox.ItemsSource = _files;

          // 更新文件计数显示
          UpdateFileCount();

          // 默认选中"解压到压缩包名"
          ToNameRadio.IsChecked = true;
          OutputMode = ExtractOutputMode.ToName;

          // 更新路径预览
          RefreshOutputPathPreview();
      }
  ```

  **方法清单**：
  1. **`OutputMode_Changed(object sender, RoutedEventArgs e)`** — Radio 切换事件
     - 根据哪个 Radio 被选中，设置 `OutputMode`
     - 切换手动/非手动状态（TextBox 只读/可用、Browse 可见/隐藏）
     - 调用 `RefreshOutputPathPreview()`

  2. **`RefreshOutputPathPreview()`** — 更新路径预览
     - 手动模式：启用 TextBox + Browse，清空预览 TextBlock
     - Here 模式：预览文本 "将解压到各压缩包所在目录"
     - Smart 模式：预览文本 "将根据压缩包结构自动选择"
     - ToName 模式：预览文本 "将解压到 {压缩包名}\ 子目录"

  3. **`BrowseButton_Click(object sender, RoutedEventArgs e)`** — 浏览按钮事件
     - 打开 `VistaFolderBrowserDialog`（使用 Ookii.Dialogs.Wpf）
     - 用户选择后：`CustomDestination = dialog.SelectedPath`
     - 填入 `OutputPathTextBox.Text`

  4. **`ExtractButton_Click(object sender, RoutedEventArgs e)`** — 解压按钮
     - 如果当前模式是 Manual 且 `CustomDestination` 为空，提示用户先选目录
     - 否则 `DialogResult = true`，关闭窗口

  5. **`CancelButton_Click(object sender, RoutedEventArgs e)`** — 取消按钮
     - `DialogResult = false`，关闭窗口

  6. **`UpdateFileCount()`** — 更新文件计数 TextBlock
     - `FileCountText.Text = L.TF(L.ExtractSettings_FileCount, _files.Count)`

  **调用方代码示例**（后续 Task 7/9 会使用，当前只需确保接口正确）：
  ```csharp
  var dialog = new ExtractSettingsWindow(paths);
  if (dialog.ShowDialog() == true)
  {
      var selectedPaths = dialog.SelectedPaths;
      var mode = dialog.OutputMode;
      var dest = dialog.CustomDestination;
      // ... 执行解压
  }
  ```

  **关于 Manual 模式下 TextBox 的说明**：
  - 用户直接在 TextBox 输入路径 → `TextChanged` 事件同步到 `CustomDestination`
  - 用户点 Browse 选目录 → 填入 TextBox → 同样触发同步
  - 这样可以：支持手动输入 + 浏览选目录两种方式

  **Must NOT do**:
  - ❌ 不执行解压逻辑
  - ❌ 不引用 IPC、ProgressWindow、App.Cli 等外部组件
  - ❌ 不添加文件列表编辑功能（留给版本 B）

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: none

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2
  - **Blocks**: F1-F4
  - **Blocked By**: Tasks 1, 3

  **References**:
  - `MantisZip.UI/CompressSettingsWindow.xaml.cs` — 代码模式参考（Radio 联动、Browse 按钮、DialogResult）
  - `MantisZip.Core/Models/ExtractOutputMode.cs` — 枚举引用（Task 1 创建）
  - `MantisZip.UI/Localization/L.cs` — 本地化键（Task 2 添加）
  - `MantisZip.UI/App.xaml.cs:395` — `VistaFolderBrowserDialog` 使用方式（现有 `ResolveExtractDestinationStatic`）

  **Acceptance Criteria**:
  - [ ] `dotnet build` — 0 errors
  - [ ] 构造 `new ExtractSettingsWindow(["a.zip", "b.zip"])` → 列表显示 2 项
  - [ ] 切换 Radio → `OutputMode` 属性正确变化
  - [ ] 手动模式：Browse → 选目录 → TextBox 显示路径
  - [ ] 点击[解压] → `DialogResult == true`
  - [ ] 点击[取消] → `DialogResult == false`

  **QA Scenarios**:
  ```
  Scenario: 窗口构建编译通过
    Tool: Bash (dotnet build)
    Steps:
      1. dotnet build src/MantisZip.UI/MantisZip.UI.csproj
    Expected Result: Build succeeded, 0 errors
    Evidence: .sisyphus/evidence/task-4-build.txt

  Scenario: 构造 + 属性验证（通过反射测试）
    Tool: Bash (编写简单控制台验证)
    Steps:
      1. 创建临时测试：new ExtractSettingsWindow(["a.zip", "b.zip"])
      2. 验证 SelectedPaths.Count == 2
      3. 验证默认 OutputMode == ToName
    Expected Result: 构造正确，默认值正确
    Evidence: .sisyphus/evidence/task-4-ctor.txt

  Scenario: Radio 切换验证
    Tool: 代码审查
    Steps:
      1. 审查 OutputMode_Changed 处理 4 种情况
      2. 确认手动模式启用 TextBox+Browse
      3. 确认非手动模式禁用 TextBox
    Expected Result: 所有 Radio 分支正确处理
    Evidence: .sisyphus/evidence/task-4-radio.txt
  ```

  **Commit**: YES (groups with Task 3)
  - Message: `feat(ui): add ExtractSettingsWindow with 4 output modes`
  - Files:
    - `src/MantisZip.UI/ExtractSettingsWindow.xaml`
    - `src/MantisZip.UI/ExtractSettingsWindow.xaml.cs`
    - `src/MantisZip.UI/Localization/L.cs`
    - `src/MantisZip.UI/Resources/strings.zh.json`
    - `src/MantisZip.UI/Resources/strings.en.json`

---

## Final Verification Wave

- [x] F1. **Plan Compliance Audit** — `oracle`
  验证所有 Must Have 已实现，Must NOT Have 未出现，证据文件完整。

- [x] F2. **Code Quality Review** — `unspecified-high`
  `dotnet build` 0 errors。检查代码质量：无空 catch、无 `as any` 等效代码、无控制台日志残留。

- [x] F3. **Real Manual QA** — `unspecified-high`
  执行所有 QA Scenario，逐项验证通过。

- [x] F4. **Scope Fidelity Check** — `deep`
  确认只实现了 ExtractSettingsWindow 相关文件，未改动 IPC、ProgressWindow、HandleExtract* 等范围外代码。

---

## Commit Strategy

| # | Message | Files |
|---|---------|-------|
| 1 | `feat(core): add ExtractOutputMode enum` | `ExtractOutputMode.cs` |
| 2-4 | `feat(ui): add ExtractSettingsWindow with 4 output modes` | `ExtractSettingsWindow.xaml`, `.cs`, `L.cs`, `strings.*.json` |

---

## Success Criteria

### Verification Commands
```bash
dotnet build src/MantisZip.UI/MantisZip.UI.csproj
# Expected: Build succeeded, 0 errors
```

### Final Checklist
- [x] ExtractOutputMode 枚举 4 个值完整
- [x] ExtractSettingsWindow 编译通过
- [x] 窗口支持 4 种输出模式切换
- [x] 手动模式 + Browse 正常工作
- [x] DialogResult 正确返回配置
- [x] 未修改范围外代码
