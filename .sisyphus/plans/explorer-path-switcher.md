# Explorer Path Switcher — 资源管理器路径快速选择

## TL;DR

> **Quick Summary**: 在压缩/解压对话框中通过 `Ctrl+G` 快捷键唤出快速路径选择器，显示所有已打开的资源管理器窗口路径，双击即可填入目标位置。
>
> **Deliverables**:
> - `ExplorerWindowTracker` — COM 封装，获取已打开的资源管理器路径列表
> - `PathQuickSelectWindow` — WPF 路径选择弹窗（搜索过滤、高亮当前窗口、双击选择）
> - `CompressSettingsWindow` 集成 — Ctrl+G 唤出选择器填充输出路径
> - `App.xaml.cs` 提取流程集成 — 路径选择后跳过系统文件夹对话框
> - 单元测试
>
> **Estimated Effort**: Quick
> **Parallel Execution**: YES — 5 tasks across 3 waves
> **Critical Path**: Task 1 → Task 2 → Task 3 → Task 4 → Task 5

---

## Context

### Original Request
为 MantisZip 添加类似 Listary 的功能：获取当前所有打开的资源管理器文件夹路径，在压缩/解压对话框中快速选择目标路径。

### Interview Summary
**Key Discussions**:
- **主要用途**: 压缩/解压时快速选择目标路径（非主窗口导航）
- **交互方式**: `Ctrl+G` 快捷键唤起路径选择弹窗
- **弹窗功能**: 搜索过滤 + 高亮当前激活窗口 + 双击直接执行
- **弹窗样式**: 跟随应用主题，鼠标位置弹出
- **空列表处理**: 无打开窗口时显示友好提示

**Research Findings**:
- `Shell.Application` COM 组件的 `Windows()` 集合可枚举所有资源管理器窗口
- 通过 `LocationURL` 获取路径，需从 `file:///C:/path` 格式解析为本地路径
- 特殊文件夹返回 `::{GUID}` 格式，需处理
- 地址栏/特殊视图（如"我的电脑"）应显示友好名称
- 使用 late binding (`dynamic`) 无需添加 COM 项目引用

---

## Work Objectives

### Core Objective
添加 `Ctrl+G` 快捷键在对话框中唤出快速路径选择器的功能。

### Concrete Deliverables
- `Core/Utils/ExplorerWindowTracker.cs` — COM 封装组件
- `UI/PathQuickSelectWindow.xaml` + `.cs` — 路径选择弹窗
- `UI/CompressSettingsWindow.xaml.cs` — Ctrl+G 集成
- `UI/App.xaml.cs` — 提取流程集成
- `tests/MantisZip.Tests/ExplorerWindowTrackerTests.cs` — 单元测试

### Definition of Done
- [ ] Ctrl+G 在压缩对话框中唤起路径选择弹窗
- [ ] 弹窗显示当前所有已打开的资源管理器路径
- [ ] 当前激活的资源管理器窗口被高亮
- [ ] 搜索框过滤路径列表
- [ ] 双击路径 → 自动填入目标路径并关闭弹窗
- [ ] 无打开窗口时提示"没有打开的资源管理器窗口"
- [ ] `dotnet test` — 新增单元测试通过

### Must Have
- 使用 late binding（`dynamic` + `Type.GetTypeFromProgID`），不添加外部 COM 引用
- 兼容 Win10/Win11
- ExcelDE 等特殊文件夹显示友好名称
- 弹窗位置跟随鼠标指针

### Must NOT Have (Guardrails)
- 不修改现有测试逻辑
- 不添加 NuGet 包依赖
- 不在主窗口添加（仅对话框场景）
- 不监控/轮询资源管理器窗口变化（仅在唤出时快照）

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (xUnit)
- **Automated tests**: TDD — ExplorerWindowTracker 解析逻辑单元测试
- **Framework**: xUnit 2.9.2
- **Agent-Executed QA**: 所有任务均包含

### QA Policy
- **UI 弹窗**: 通过 Playwright 或截图验证（由于是 WPF 原生，用 bash 启动应用，模拟快捷键触发）
- **COM 封装**: 单元测试 + 手动编译运行验证
- **集成**: 启动应用 → 按 Ctrl+G → 截图验证弹窗 → 选择路径 → 验证路径被填入

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Start Immediately — Core + Tests):
├── Task 1: ExplorerWindowTracker — COM 封装 + 单元测试

Wave 2 (After Wave 1 — UI 组件):
├── Task 2: PathQuickSelectWindow — WPF 弹窗
├── Task 3: PathQuickSelectWindow 键盘/搜索/高亮逻辑

Wave 3 (After Wave 2 — 集成):
├── Task 4: CompressSettingsWindow Ctrl+G 集成
├── Task 5: 提取流程集成 + 最终验证
```

### Dependency Matrix
- T1: — T2, T3
- T2: T1 — T4, T5
- T3: T1 — T4, T5
- T4: T2, T3 — T5
- T5: T4 — FINAL

---

## TODOs

- [ ] 1. ExplorerWindowTracker — COM 封装组件

  **What to do**:
  - 在 `Core/Utils/ExplorerWindowTracker.cs` 创建静态类
  - 使用 `Type.GetTypeFromProgID("Shell.Application")` + `dynamic` 枚举资源管理器窗口
  - 过滤 `explorer.exe` 进程，排除 IE 等
  - 解析 `LocationURL` 的 `file:///C:/path` → 本地路径 `C:\path`
  - `::{GUID}` 特殊文件夹 → 显示友好名称（通过 `Shell.Application.NameSpace()` 或跳过）
  - 获取当前前台窗口的路径（`GetForegroundWindow` + 匹配 HWND）

  **公开 API**:
  ```csharp
  public class ExplorerWindowInfo
  {
      public string Path { get; set; } = "";        // 本地路径
      public string DisplayName { get; set; } = "";  // 显示名称
      public IntPtr HWND { get; set; }               // 窗口句柄
      public bool IsActive { get; set; }              // 是否前台窗口
  }

  public static class ExplorerWindowTracker
  {
      // 获取所有已打开的资源管理器窗口
      public static List<ExplorerWindowInfo> GetOpenExplorerWindows();

      // 获取当前激活的资源管理器窗口路径
      public static string? GetActiveExplorerPath();
  }
  ```

  **Must NOT do**:
  - 不添加 SHDocVw.dll COM 引用
  - 不处理不相关问题（浏览器窗口等）

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 单个简单类，无复杂逻辑
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (alone — foundation)
  - **Blocks**: T2, T3
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI/SystemIconHelper.cs:36-41` — 现有 P/Invoke 模式（`[DllImport]` 用法参考）
  - 无类似 COM 组件的现有代码可参考（这是首个此类组件）

  **Acceptance Criteria**:
  - [ ] `ExplorerWindowTracker.GetOpenExplorerWindows()` 返回当前所有资源管理器窗口
  - [ ] 路径解析正确：`file:///C:/My%20Folder` → `C:\My Folder`
  - [ ] 特殊文件夹不报错（过滤 `::{GUID}`）
  - [ ] `GetActiveExplorerPath()` 返回当前前台窗口路径
  - [ ] 无资源管理器打开时返回空列表（不抛异常）

  **QA Scenarios**:

  ```
  Scenario: 正常情况 — 有已打开的资源管理器窗口
    Tool: Bash (dotnet test + 手动验证)
    Preconditions: 至少打开一个资源管理器窗口
    Steps:
      1. 编写快速测试程序引用 ExplorerWindowTracker
      2. 调用 GetOpenExplorerWindows()
    Expected Result: 返回非空列表，每个元素包含有效路径
    Evidence: .sisyphus/evidence/task-1-explorer-list.txt

  Scenario: 无资源管理器窗口 — 返回空列表
    Tool: 单元测试
    Preconditions: 关闭所有资源管理器窗口
    Steps:
      1. 调用 GetOpenExplorerWindows()
    Expected Result: 返回空列表
    Evidence: .sisyphus/evidence/task-1-empty-list.txt
  ```

  **Commit**: YES
  - Message: `feat(core): add ExplorerWindowTracker for enumerating open Explorer windows`
  - Files: `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs`

- [ ] 2. PathQuickSelectWindow — WPF 路径选择弹窗布局

  **What to do**:
  - 创建 `src/MantisZip.UI/PathQuickSelectWindow.xaml` + `.cs`
  - XAML 布局：
    - 顶部：搜索文本框（带提示文字）
    - 中间：路径列表（ListView/ListBox）
      - 每行：文件夹图标 + 路径文本 + "当前窗口" 标签（高亮时显示）
    - 底部：状态栏提示 "Ctrl+G 切换 | Enter 选择 | Esc 关闭"
  - 窗口样式：
    - 小尺寸（约 450x350）
    - 无边框（WindowStyle=None），带阴影
    - 跟随应用主题（使用 `StaticResource Theme_*`）
    - 鼠标位置弹出（`WindowStartupLocation=Manual` + 鼠标坐标）
  - 属性：
    - `SelectedPath`（string）— 用户选择的路径
    - `DialogResult`（bool）— 是否选中

  **Must NOT do**:
  - 不添加 WPF 控件库依赖
  - 不修改 Global 主题资源文件

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering` — WPF UI 布局
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with T3)
  - **Blocks**: T4, T5
  - **Blocked By**: T1 (ExplorerWindowTracker 类型)

  **References**:
  - `src/MantisZip.UI/CompressSettingsWindow.xaml` — 主题资源使用方法（`StaticResource Theme_WindowBg`）
  - `src/MantisZip.UI/MainWindow.xaml` — 主窗口布局和样式参考

  **Acceptance Criteria**:
  - [ ] 弹窗能正常显示（手动 Ctrl+G 触发验证）
  - [ ] 跟随应用亮/暗色主题
  - [ ] 在鼠标位置弹出
  - [ ] 关闭时不影响父窗口

  **QA Scenarios**:

  ```
  Scenario: 弹窗显示正常
    Tool: Bash (启动应用 + 手动触发)
    Preconditions: 打开 MantisZip 压缩对话框
    Steps:
      1. 在代码中添加测试按钮临时触发窗口
      2. 查看窗口尺寸、样式、主题匹配
    Expected Result: 弹窗正确显示，主题匹配
    Evidence: .sisyphus/evidence/task-2-popup-appearance.png
  ```

  **Commit**: YES (groups with T3)
  - Message: `feat(ui): add PathQuickSelectWindow popup for quick path selection`



- [ ] 3. PathQuickSelectWindow — 搜索/高亮/选择交互逻辑

  **What to do**:
  - 在 Task 2 基础上添加交互逻辑
  - **数据绑定**: 从 `ExplorerWindowTracker.GetOpenExplorerWindows()` 加载数据
  - **搜索过滤**: 搜索框 `TextChanged` → 实时过滤路径列表（匹配路径和显示名称）
  - **当前窗口高亮**: 
    - `ExplorerWindowInfo.IsActive == true` → 背景色不同 + 显示 "当前窗口" 标签
    - 在 `Loaded` 事件中获取数据（快照模式，不轮询）
  - **键盘交互**:
    - `Enter` → 选择当前选中项，关闭窗口，设置 `SelectedPath`
    - `Esc` → 关闭窗口，`SelectedPath = null`
    - `Up/Down` → 列表项导航
  - **鼠标交互**:
    - 双击列表项 → 选择该项
    - 单击选中
  - **列出无窗口时**: 
    - 搜索框隐藏或禁用
    - 列表显示 "没有打开的资源管理器窗口，请先打开一个文件夹"
  - **特殊文件夹处理**: `::{GUID}` 路径显示友好名称（如 "此电脑"、"回收站"）而非路径
    - 用 `new ShellFolderViewDual()` 或跳过（非文件系统路径不显示）

  **Must NOT do**:
  - 不要轮询/实时刷新资源管理器窗口列表（只获取一次快照）
  - 不要添加额外 NuGet 包

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 简单数据绑定和事件处理
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with T2)
  - **Blocks**: T4, T5
  - **Blocked By**: T1

  **References**:
  - `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs` — 依赖的数据源
  - `src/MantisZip.UI/MainWindow.xaml.cs` — WPF 事件处理模式参考

  **Acceptance Criteria**:
  - [ ] 列表显示正确（路径、图标、高亮标记）
  - [ ] 搜索框实时过滤
  - [ ] 回车选择、Esc 关闭
  - [ ] 双击选择路径
  - [ ] 无窗口时显示提示文字
  - [ ] 高亮的当前窗口在列表中有视觉区分

  **QA Scenarios**:

  ```
  Scenario: 搜索过滤功能
    Tool: Bash (启动应用 + 截图)
    Preconditions: 打开 2+ 个资源管理器窗口（如 `D:\Projects` 和 `C:\Windows`）
    Steps:
      1. 在压缩对话框按 Ctrl+G 打开路径选择器
      2. 在搜索框输入 "Proj"
    Expected Result: 列表过滤为只包含 "D:\Projects" 的项
    Evidence: .sisyphus/evidence/task-3-search-filter.png

  Scenario: 高亮当前激活窗口
    Tool: Bash (启动应用 + 截图)
    Preconditions: 打开多个资源管理器窗口
    Steps:
      1. 确保前台是某个特定资源管理器窗口
      2. 按 Ctrl+G 打开路径选择器
    Expected Result: 当前前台资源管理器路径有高亮背景 + "当前窗口" 标签
    Evidence: .sisyphus/evidence/task-3-active-highlight.png

  Scenario: 无窗口时显示友好提示
    Tool: Bash (启动应用 + 截图)
    Preconditions: 关闭所有资源管理器窗口
    Steps:
      1. 在压缩对话框按 Ctrl+G
    Expected Result: 弹窗显示 "没有打开的资源管理器窗口"
    Evidence: .sisyphus/evidence/task-3-empty-state.png
  ```

  **Commit**: YES (groups with T2)
  - Message: `feat(ui): add search, highlight and selection logic to PathQuickSelectWindow`



- [ ] 4. CompressSettingsWindow — Ctrl+G 快捷键集成

  **What to do**:
  - 在 `CompressSettingsWindow.xaml.cs` 中添加 `PreviewKeyDown` 事件处理
  - 快捷键 `Ctrl+G`（Key.G 且 Keyboard.Modifiers == ModifierKeys.Control）
  - 处理逻辑：
    1. 调用 `ExplorerWindowTracker.GetOpenExplorerWindows()` 获取路径列表
    2. 如列表为空 → 显示友好提示框（不打开弹窗）
    3. 如列表有数据 → 创建 `PathQuickSelectWindow` 实例
    4. 设置 Owner = this
    5. 在鼠标位置弹出: `Left = mouseX`, `Top = mouseY`
    6. 调用 `ShowDialog()`
    7. 如果返回 true 且 `SelectedPath` 不为空
       - 填充到 `OutputPathTextBox.Text`
       - 触发 `UpdateCompressButton()`
  - 确保快捷键在焦点在 TextBox/ComboBox 中时也能工作（`PreviewKeyDown` 而非 `KeyDown`）
  - 不影响 TextBox 中的 Ctrl+G 文本编辑（仅当全局范围内触发）

  **Must NOT do**:
  - 不修改 CompressSettingsWindow.xaml（仅 code-behind 修改）
  - 不破坏现有快捷键

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 简单事件处理集成
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with T5)
  - **Blocks**: T5 (extract 集成逻辑可并行)
  - **Blocked By**: T2, T3 (UI 组件完成)

  **References**:
  - `src/MantisZip.UI/CompressSettingsWindow.xaml.cs` — 现有事件处理模式
  - `src/MantisZip.UI/PathQuickSelectWindow` — 依赖的弹窗
  - `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs` — 数据源

  **Acceptance Criteria**:
  - [ ] Ctrl+G 在 CompressSettingsWindow 中唤出路径选择器
  - [ ] 选择路径后自动填入 OutputPathTextBox
  - [ ] 无资源管理器窗口时弹出提示，不打开空弹窗
  - [ ] 不影响其他 Ctrl+G 操作（如输入框内文本操作）

  **QA Scenarios**:

  ```
  Scenario: Ctrl+G 唤出路径选择器
    Tool: Bash (启动应用 + 截图)
    Preconditions: 打开 1+ 个资源管理器窗口，启动 MantisZip → 打开压缩对话框
    Steps:
      1. 在压缩对话框中按 Ctrl+G
    Expected Result: 路径选择器弹窗出现，显示已打开的资源管理器路径
    Evidence: .sisyphus/evidence/task-4-ctrl-g-popup.png

  Scenario: 选择路径后自动填入
    Tool: Bash
    Preconditions: 弹出路径选择器，有路径列表
    Steps:
      1. 双击某个路径
    Expected Result: 弹窗关闭，OutputPathTextBox 中显示所选路径
    Evidence: .sisyphus/evidence/task-4-path-filled.png

  Scenario: 无窗口时显示提示
    Tool: Bash
    Preconditions: 关闭所有资源管理器，打开压缩对话框
    Steps:
      1. 按 Ctrl+G
    Expected Result: 弹窗显示 "没有打开的资源管理器窗口"
    Evidence: .sisyphus/evidence/task-4-no-explorer.png
  ```

  **Commit**: YES
  - Message: `feat(ui): add Ctrl+G path quick-select to CompressSettingsWindow`
  - Files: `src/MantisZip.UI/CompressSettingsWindow.xaml.cs`

---

- [ ] 5. 提取流程集成 — App.xaml.cs + 最终验证

  **What to do**:
  - 在 `App.xaml.cs` 的提取流程中集成快速路径选择
  - **关键认识**: `--extract` 运行在 CLI/静态上下文中，没有 WPF 窗口来监听 Ctrl+G 快捷键
  - **方案**: 在弹出系统 `VistaFolderBrowserDialog` 之前，**自动检测**是否有已打开的资源管理器窗口
    - 如果有 → 自动弹出 `PathQuickSelectWindow` 让用户选择
    - 用户选了一个路径 → 直接使用它（跳过系统对话框）
    - 用户取消 → 回退到系统 `VistaFolderBrowserDialog`
  - **实现点**:
    - 修改 `HandleExtract()`: 在调用 `ResolveExtractDestinationStatic` 前插入快速选择逻辑
    - 创建辅助方法 `TryQuickPickExplorerPath(out string? path)`:
      - 调用 `ExplorerWindowTracker.GetOpenExplorerWindows()`
      - 有窗口 → `Application.Current.Dispatcher.Invoke` 创建 PathQuickSelectWindow
      - 用户选择 → 返回 true + path
      - 用户取消/无窗口 → 返回 false
    - 修改 `ResolveExtractDestinationStatic`(或调用路径):
      - 当 `destSetting == "ask"` 时：先尝试快速选择，失败再弹系统对话框
  - **代码示意**:
  ```
  private static string? ResolveExtractDestinationStatic(string? archivePath, AppSettings settings)
  {
      var destSetting = settings.ExtractDestination;
      if (destSetting == "same-dir") /* ... */;
      else if (destSetting == "desktop") /* ... */;
  
      // "ask" 模式：先尝试快速选择
      if (TryQuickPickExplorerPath(out var quickPath))
          return quickPath;
  
      // 回退到系统对话框
      var dialog = new VistaFolderBrowserDialog { ... };
      return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
  }
  
  private static bool TryQuickPickExplorerPath(out string? path)
  {
      path = null;
      var windows = ExplorerWindowTracker.GetOpenExplorerWindows();
      if (windows.Count == 0) return false;
  
      Application.Current.Dispatcher.Invoke(() =>
      {
          var picker = new PathQuickSelectWindow();
          picker.WindowStartupLocation = WindowStartupLocation.CenterScreen;
          if (picker.ShowDialog() == true)
          {
              path = picker.SelectedPath;
          }
      });
      return path != null;
  }
  ```

  **最终验证**: 确保整个流程端到端正常工作
  - 压缩对话框：Ctrl+G → 选择路径 → 填入输出路径 → 压缩
  - 提取流程（--extract ask 模式）: 先弹出快速选择 → 选路径 → 直接解压
  - 提取流程（无窗口时）: 正常弹出 VistaFolderBrowserDialog，无变化

  **Must NOT do**:
  - 不引入死锁（Dispatcher 使用需在主线程）
  - 不破坏现有的 `--extract`, `--extract-here`, `--extract-smart`, `--extract-to-name` 流程
  - 不影响非 "ask" 模式的提取行为

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 简单集成逻辑
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with T4)
  - **Blocks**: Final verification
  - **Blocked By**: T2, T3, T4

  **References**:
  - `src/MantisZip.UI/App.xaml.cs:901-921` — `HandleExtract` 方法
  - `src/MantisZip.UI/App.xaml.cs:1418-1424` — `ResolveExtractDestinationStatic` + VistaFolderBrowserDialog
  - `src/MantisZip.UI/PathQuickSelectWindow` — 依赖的弹窗
  - `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs` — 数据源

  **Acceptance Criteria**:
  - [ ] 提取设置="ask" 且打开资源管理器窗口时：自动先弹出快速选择器
  - [ ] 选择了路径 → 直接解压到该路径（跳过系统对话框）
  - [ ] 取消快速选择 → 回退到 VistaFolderBrowserDialog
  - [ ] 无资源管理器窗口 → 正常弹出 VistaFolderBrowserDialog
  - [ ] 不影响 CLI 模式和非 ask 模式

  **QA Scenarios**:

  ```
  Scenario: 提取时自动弹出快速选择器
    Tool: Bash + 截图
    Preconditions: 
      - AppSettings.ExtractDestination = "ask"
      - 打开一个资源管理器窗口 D:\Target
      - 准备一个测试压缩包 test.7z
    Steps:
      1. 执行 MantisZip.UI.exe --extract "test.7z"
      2. 观察：快速选择器自动弹出
      3. 双击 D:\Target
    Expected Result: 直接解压到 D:\Target，未弹出 VistaFolderBrowserDialog
    Evidence: .sisyphus/evidence/task-5-extract-quickpick.png

  Scenario: 无窗口时正常回退到系统对话框
    Tool: Bash + 截图
    Preconditions:
      - 关闭所有资源管理器窗口
      - AppSettings.ExtractDestination = "ask"
    Steps:
      1. 执行 MantisZip.UI.exe --extract "test.7z"
    Expected Result: 直接弹出 VistaFolderBrowserDialog
    Evidence: .sisyphus/evidence/task-5-extract-fallback.png

  Scenario: 端到端压缩测试
    Tool: Bash + 截图
    Preconditions: 打开资源管理器窗口到 D:\Output
    Steps:
      1. 打开 MantisZip → 压缩对话框
      2. 添加源文件
      3. 按 Ctrl+G → 选择 D:\Output
      4. 确认目标路径已填入 → 点击压缩
    Expected Result: 压缩包创建在 D:\Output
    Evidence: .sisyphus/evidence/task-5-compress-endtoend.png
  ```

  **Commit**: YES
  - Message: `feat(ui): integrate quick path selector into extract flow (ask mode)`
  - Files: `src/MantisZip.UI/App.xaml.cs`

---

- [ ] 6. 单元测试 — ExplorerWindowTracker 测试

  **What to do**:
  - 在 `tests/MantisZip.Tests/ExplorerWindowTrackerTests.cs` 创建单元测试
  - 测试内容（由于无法 mock COM，测试聚焦于可单元测试的方法）:
    1. **URL 路径解析**: `Uri.UnescapeDataString(uri.LocalPath)` 的正确性
       - `file:///C:/My%20Folder` → `C:\My Folder`
       - `file:///D:/Projects` → `D:\Projects`
       - `file:///E:/测试/文件` → `E:\测试\文件`
    2. **特殊文件夹过滤**: `::{GUID}` 格式字符串不报错
    3. **空列表处理**: 无 COM 返回值时表现正常
  - 测试 `ExplorerWindowInfo` 模型类的正确性

  **Must NOT do**:
  - 不尝试 mock COM（使用真实系统状态测试或跳过 COM 相关测试）
  - 不添加 Moq/NSubstitute 依赖

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 简单单元测试
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with T1 — 可并行或紧随其后)
  - **Blocks**: None
  - **Blocked By**: T1

  **References**:
  - `tests/MantisZip.Tests/Engines/ZipEngineTests.cs` — 现有测试模式
  - `tests/MantisZip.Tests/MantisZip.Tests.csproj` — 项目文件参考

  **Acceptance Criteria**:
  - [ ] `dotnet test tests/MantisZip.Tests` 全部通过（不破坏现有测试）
  - [ ] 至少 5 个测试用例覆盖路径解析、特殊文件夹处理、空列表

  **QA Scenarios**:

  ```
  Scenario: 运行单元测试
    Tool: Bash (dotnet test)
    Preconditions: 所有代码编译通过
    Steps:
      1. dotnet test tests/MantisZip.Tests
    Expected Result: 全部测试通过（新增 + 原测试）
    Evidence: .sisyphus/evidence/task-6-test-results.txt
  ```

  **Commit**: YES
  - Message: `test: add ExplorerWindowTracker unit tests for path parsing`
  - Files: `tests/MantisZip.Tests/ExplorerWindowTrackerTests.cs`

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. Verify all Must Have implemented, Must NOT Have absent. Check evidence files exist.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Build检查: `dotnet build src\MantisZip.UI\MantisZip.UI.csproj`。测试: `dotnet test tests\MantisZip.Tests`。检查代码质量。
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | VERDICT`

- [ ] F3. **Manual QA** — `unspecified-high`
  执行所有 QA 场景。端到端测试：启动应用 → 压缩对话框 → Ctrl+G → 选择路径 → 压缩 → 验证。
  Output: `Scenarios [N/N pass] | Integration [PASS/FAIL] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  对比实现与计划，验证没有遗漏也没有超范围。
  Output: `Tasks [N/N compliant] | VERDICT`

---

## Commit Strategy

| Task | Message |
|------|---------|
| 1 | `feat(core): add ExplorerWindowTracker for enumerating open Explorer windows` |
| 2+3 | `feat(ui): add PathQuickSelectWindow popup for quick path selection` |
| 4 | `feat(ui): add Ctrl+G path quick-select to CompressSettingsWindow` |
| 5 | `feat(ui): integrate Ctrl+G path quick-select into extract flow` |
| 6 | `test: add ExplorerWindowTracker unit tests for path parsing` |

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
dotnet test tests\MantisZip.Tests
```

### Final Checklist
- [ ] Ctrl+G 在压缩对话框唤出路径选择器
- [ ] 搜索过滤正常工作
- [ ] 双击路径正确填入目标路径
- [ ] 当前窗口高亮显示
- [ ] 无窗口时友好提示
- [ ] 提取流程也支持 Ctrl+G
- [ ] 所有测试通过
- [ ] 亮/暗色主题均正常
