# QuickPathControl — 路径快捷选择组件

## TL;DR

> **Quick Summary**: 创建一个可复用的 QuickPathControl UserControl（TextBox + 收藏/历史/已打开窗口/浏览 四个按钮），配合 QuickPathDialog 模态弹窗和 FavoriteManagerWindow，统一替换所有路径选择场景。收藏系统新增三个硬编码系统路径（桌面/文档/下载），用户可隐藏不可删除。
>
> **Deliverables**:
> - `Core/Utils/ExplorerWindowTracker.cs` — COM 封装枚举已打开的资源管理器窗口
> - `Core/Utils/FavoritePathManager.cs` — favorites.json 读写管理
> - `Core/Utils/PathHistoryManager.cs` — 历史记录自动追踪（50条，去重，移至顶部）
> - `UI/QuickPathControl.xaml` + `.cs` — 可复用的路径快捷输入组件
> - `UI/QuickPathDialog.xaml` + `.cs` — 模态弹窗，包装 QuickPathControl
> - `UI/FavoriteManagerWindow.xaml` + `.cs` — 收藏管理窗口
> - `tests/MantisZip.Tests/` — 单元测试 + 集成测试
>
> **Estimated Effort**: Medium
> **Parallel Execution**: YES — 11 tasks across 4 waves
> **Critical Path**: T1 → T4 → T7/T8/T9 → T10/T11/T12

---

## Context

### Original Request
将压缩/解压对话框中的路径输入框替换为带收藏、历史、已打开资源管理器窗口三个快捷按钮的组件，所有路径选择场景统一。

### Interview Summary
**Key Discussions**:
- **双组件方案**: QuickPathControl（内嵌式）+ QuickPathDialog（模态弹窗）
- **三个快捷按钮**: ⭐收藏 / 🕐历史 / 🪟已打开资源管理器窗口
- **布局**: TextBox 左侧，右侧横向排列 [⭐][🕐][🪟][浏览]
- **QuickPathControl 双模式**: 文件保存模式（压缩输出）+ 文件夹模式（其余场景）
- **非手动模式按钮状态**: 禁用（不隐藏）
- **收藏管理**: 独立窗口 FavoriteManagerWindow，支持增删改排序
- **收藏属性**: 友好名称 + 路径
- **收藏存储**: 独立 favorites.json（%LOCALAPPDATA%\MantisZip\）
- **历史记录**: 全局统一，50条，去重移至顶部，提交时记录
- **AddFolderButton**: 也替换为 QuickPathDialog
- **替换范围**: 一次性全部替换（CompressSettingsWindow / ExtractSettingsWindow / ResolveExtractDestinationStatic / AddFolderButton）
- **系统路径（收藏增强）**: 桌面/文档/下载三个硬编码系统路径，用户可"隐藏"不可"删除"；FavoriteManagerWindow 中系统路径显示 🔒系统 标签 + 隐藏/显示按钮
- **测试**: TDD for 数据管理器 + 集成测试 for COM + Agent QA

**Research Findings**:
- 仅 2 个可编辑路径 TextBox：`OutputPathTextBox`（压缩）和 `ManualPathTextBox`（解压）— 均使用 Grid(TextBox + Browse) 布局
- 压缩的「浏览」使用 SaveFileDialog（文件保存），解压的「浏览」使用 VistaFolderBrowserDialog（文件夹选择器）
- AddFolderButton 使用 VistaFolderBrowserDialog（源文件夹选择）
- ResolveExtractDestinationStatic 使用 VistaFolderBrowserDialog
- 主题资源 Theme_WindowBg / Theme_TextPrimary / Theme_Border 已存在于 Light.xaml / Dark.xaml
- 本地化模式 L.T() / L.TF() 已建立
- JSON 持久化模式（PasswordManager.cs）可作为参考

### Metis Review
**Identified Gaps** (addressed):
- **压缩输出 SaveFileDialog**: QuickPathControl 支持双模式（文件保存 + 文件夹）解决
- **AddFolderButton 范围**: 用户确认纳入范围，统一体验
- **非手动模式按钮状态**: 禁用不隐藏，匹配解压当前行为
- **历史记录时机**: 提交时记录，不追踪自动计算路径
- **COM 错误处理**: ExplorerWindowTracker 必须 try/catch COMException/SecurityException

---

## Work Objectives

### Core Objective
创建 QuickPathControl 路径快捷选择组件系统，统一 MantisZip 中所有路径选择场景的用户体验。

### Concrete Deliverables
- `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs` — COM 封装
- `src/MantisZip.Core/Utils/FavoritePathManager.cs` — 收藏管理器（含系统路径桌面/文档/下载）
- `src/MantisZip.Core/Utils/PathHistoryManager.cs` — 历史管理器
- `src/MantisZip.UI/Controls/QuickPathControl.xaml` + `.cs` — 快捷路径控件（下拉含系统路径🔒标识）
- `src/MantisZip.UI/Dialogs/QuickPathDialog.xaml` + `.cs` — 模态路径选择弹窗
- `src/MantisZip.UI/Dialogs/FavoriteManagerWindow.xaml` + `.cs` — 收藏管理窗口（含系统路径隐藏/显示）
- 修改 `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml` + `.cs`
- 修改 `src/MantisZip.UI/Dialogs/ExtractSettingsWindow.xaml` + `.cs`
- 修改 `src/MantisZip.UI/App.xaml.cs`
- 新增 `tests/MantisZip.Tests/FavoritePathManagerTests.cs`
- 新增 `tests/MantisZip.Tests/FavoritePathManagerSystemPathsTests.cs`
- 新增 `tests/MantisZip.Tests/PathHistoryManagerTests.cs`
- 新增 `tests/MantisZip.Tests/ExplorerWindowTrackerTests.cs`

### Definition of Done
- [ ] QuickPathControl 显示 TextBox + 4 个按钮（收藏/历史/已打开/浏览）
- [ ] 收藏下拉菜单列出系统路径 + 用户收藏（合并），底部有「管理收藏…」
- [ ] 系统路径（桌面/文档/下载）硬编码，运行时通过 Environment.SpecialFolder 获取
- [ ] 收藏下拉菜单中系统路径显示 🔒 图标标识
- [ ] 隐藏的系统路径不出现于收藏下拉菜单
- [ ] FavoriteManagerWindow 中系统路径显示"隐藏/显示"按钮，用户收藏显示"删除"按钮
- [ ] 隐藏状态持久化到 favorites.json
- [ ] 历史下拉菜单列出最近 50 条路径，去重，最新在前
- [ ] 已打开下拉菜单列出当前所有资源管理器窗口路径，当前窗口高亮
- [ ] 浏览按钮打开 SaveFileDialog（文件模式）或 VistaFolderBrowserDialog（文件夹模式）
- [ ] 点击下拉菜单项自动填充到 TextBox
- [ ] QuickPathDialog 模态弹窗包装 QuickPathControl + 确认/取消按钮
- [ ] FavoriteManagerWindow 支持添加/编辑/删除/排序用户收藏 + 隐藏/显示系统路径
- [ ] CompressSettingsWindow 输出路径使用 QuickPathControl
- [ ] ExtractSettingsWindow 手动路径使用 QuickPathControl
- [ ] ResolveExtractDestinationStatic 使用 QuickPathDialog
- [ ] AddFolderButton 使用 QuickPathDialog
- [ ] 亮/暗主题均正常
- [ ] `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` 通过
- [ ] `dotnet test tests\MantisZip.Tests` 全部通过

### Must Have
- 双模式支持：文件保存模式（压缩输出）+ 文件夹模式（其余场景）
- Late binding COM（dynamic + Type.GetTypeFromProgID），不添加 COM 引用
- COM 错误处理：try/catch COMException/SecurityException，失败时返回空列表
- 收藏属性：友好名称 + 路径（字符串对）
- **系统路径（桌面/文档/下载）**: 硬编码通过 Environment.SpecialFolder 获取，不存路径本身到 JSON
- **系统路径隐藏状态**: 持久化到 favorites.json（存 SpecialFolder 枚举名如 "Desktop"）
- **FavoriteManagerWindow**: 系统路径显示 🔒系统 标签 + 隐藏/显示按钮；用户收藏显示删除按钮
- **收藏下拉菜单**: 系统路径 + 用户收藏合并展示，系统路径有 🔒 标识，隐藏的系统路径不出现
- 历史去重：相同路径移至顶部，不重复存储
- 非手动模式：按钮禁用不隐藏
- 新控件全部绑定主题资源键（AGENTS.md 规则 3）
- 所有新 UI 字符串本地化（zh + en）
- 兼容 Win10/Win11

### Must NOT Have (Guardrails)
- 不修改现有测试逻辑（仅新增测试文件）
- 不添加 NuGet 包依赖
- 不修改 MainWindow 主窗口
- 不监控/轮询资源管理器窗口变化（仅快照）
- 不添加路径验证/存在性检查（调用者职责）
- 不添加路径自动补全/建议功能
- 不做 Shell 上下文菜单集成
- 不跟踪自动计算路径的历史记录（仅提交时记录）
- 不修改压缩输出的 SaveFileDialog 行为（仅增加快捷按钮）
- **系统路径不支持删除/编辑**（用户只能隐藏/显示）
- **系统路径不存完整路径到 JSON**（仅存 SpecialFolder 枚举名用于隐藏状态跟踪）

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (xUnit)
- **Automated tests**: TDD for data managers + integration tests for COM
- **Framework**: xUnit
- **Agent-Executed QA**: 所有任务均包含

### QA Policy
- **Core 逻辑**（FavoritePathManager, PathHistoryManager, URL 解析）: `dotnet test` 单元测试
- **COM 封装**（ExplorerWindowTracker）: 集成测试 + 手动编译运行验证（需真实资源管理器窗口）
- **UI 控件**（QuickPathControl, QuickPathDialog, FavoriteManagerWindow）: 构建并启动应用验证
- **集成**（替换后的对话框）: 启动应用 → 验证控件显示 → 验证按钮点开下拉菜单 → 选择路径 → 验证填入

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Core — 数据层 + COM，MAX PARALLEL):
├── T1: ExplorerWindowTracker — COM 封装枚举资源管理器窗口
├── T2: FavoritePathManager — favorites.json 读写 + 系统路径（桌面/文档/下载）
└── T3: PathHistoryManager — 历史记录自动追踪（50条，去重，移至顶部）

Wave 2 (UI 组件 — 并行构建):
├── T4: QuickPathControl — WPF UserControl（TextBox + 4 按钮，双模式，下拉含🔒系统路径）
├── T5: QuickPathDialog — 模态弹窗包装 QuickPathControl
└── T6: FavoriteManagerWindow — 收藏管理（含系统路径隐藏/显示）

Wave 3 (集成 — 替换所有路径选择点，MAX PARALLEL):
├── T7: CompressSettingsWindow — 替换 OutputPathTextBox + 双模式
├── T8: ExtractSettingsWindow — 替换 ManualPathTextBox
├── T9: App.xaml.cs + AddFolderButton — 替换 VistaFolderBrowserDialog 调用

Wave 4 (测试 — 全部后端验证):
├── T10: FavoritePathManager（含系统路径）+ PathHistoryManager 单元测试
├── T11: ExplorerWindowTracker 集成测试
└── T12: 端到端 QA
```

### Dependency Matrix
- T1: — T4, T5, T11
- T2: — T4, T5, T6, T10
- T3: — T4, T5, T10
- T4: T1, T2, T3 — T7, T8, T9
- T5: T1, T2, T3, T4 — T7, T8, T9
- T6: T2 — (独立，无下游依赖)
- T7: T4, T5 — T12
- T8: T4, T5 — T12
- T9: T4, T5 — T12
- T10: T2, T3 — T12
- T11: T1 — T12
- T12: T7, T8, T9, T10, T11 — FINAL

---

## TODOs

- [ ] 1. ExplorerWindowTracker — COM 封装枚举资源管理器窗口

  **What to do**:
  - 在 `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs` 创建静态类
  - 使用 `Type.GetTypeFromProgID("Shell.Application")` + `dynamic` 枚举所有资源管理器窗口
  - 通过 `InternetExplorer.FullName` 过滤 `explorer.exe` 进程
  - 解析 `LocationURL`：`file:///C:/My%20Folder` → `C:\My Folder`
  - `::{GUID}` 特殊文件夹 → 显示友好名称或过滤
  - 通过 `GetForegroundWindow` + 匹配 HWND 获取当前前台窗口
  - **关键**: 全部 COM 调用包裹在 try/catch（COMException, SecurityException, UnauthorizedAccessException）中，失败时返回空列表
  - 公开 API:
    ```csharp
    public class ExplorerWindowInfo
    {
        public string Path { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public IntPtr HWND { get; set; }
        public bool IsActive { get; set; }
    }
    public static class ExplorerWindowTracker
    {
        public static List<ExplorerWindowInfo> GetOpenExplorerWindows();
        public static string? GetActiveExplorerPath();
    }
    ```

  **Must NOT do**:
  - 不添加 SHDocVw.dll COM 引用（使用 late binding）
  - 不监控/轮询窗口变化（仅快照）
  - 不处理非 explorer.exe 窗口

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 单个静态类，无复杂逻辑
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with T2, T3)
  - **Blocks**: T4, T5, T11
  - **Blocked By**: None

  **References**:
  - `.sisyphus/plans/archived/explorer-path-switcher.md` — 参考 COM 调用模式和 URL 解析逻辑
  - `src/MantisZip.UI/SystemIconHelper.cs:36-41` — 现有 P/Invoke 模式（`[DllImport]` 用法）
  - `src/MantisZip.Core/Utils/CoreLog.cs:13` — CoreLog.Trace() 日志记录

  **Acceptance Criteria**:
  - [ ] `GetOpenExplorerWindows()` 返回已打开的资源管理器窗口列表
  - [ ] 路径解析正确：`file:///C:/My%20Folder` → `C:\My Folder`
  - [ ] 当前前台窗口的 `IsActive == true`
  - [ ] 无资源管理器打开时返回空列表（不抛异常）
  - [ ] COM 不可用时（权限不足等）返回空列表（不崩溃）

  **QA Scenarios**:
  ```
  Scenario: 正常枚举资源管理器窗口
    Tool: Bash (编写测试程序)
    Preconditions: 打开至少一个资源管理器窗口（如 D:\）
    Steps:
      1. 编写简短控制台程序调用 ExplorerWindowTracker.GetOpenExplorerWindows()
      2. 输出结果
    Expected Result: 返回非空列表，路径正确（如 D:\）
    Evidence: .sisyphus/evidence/task-1-explorer-list.txt

  Scenario: COM 不可用时优雅降级
    Tool: 代码审查 + dotnet test
    Preconditions: N/A
    Steps:
      1. 检查 COM 调用是否包裹在 try/catch 中
      2. 单元测试验证空列表返回
    Expected Result: COMException、SecurityException 时返回空列表
    Evidence: .sisyphus/evidence/task-1-com-error-handling.txt
  ```

  **Commit**: YES
  - Message: `feat(core): add ExplorerWindowTracker for enumerating open Explorer windows`
  - Files: `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs`

---

- [ ] 2. FavoritePathManager — favorites.json 读写管理 + 系统路径

  **What to do**:
  - 在 `src/MantisZip.Core/Utils/FavoritePathManager.cs` 创建静态类
  - 收藏夹数据模型:
    ```csharp
    public class FavoritePathItem
    {
        public string Name { get; set; } = "";    // 友好名称
        public string Path { get; set; } = "";    // 完整路径
        public DateTime AddedAt { get; set; }     // 添加时间
        public bool IsSystem { get; set; }        // true=系统内置路径，不可删除/编辑
        public string? SystemKey { get; set; }    // 系统路径标识: "Desktop"/"Documents"/"Downloads"
    }
    ```
  - favorites.json 存储路径: `%LOCALAPPDATA%\MantisZip\favorites.json`
  - JSON 文件结构:
    ```json
    {
      "hiddenSystemPaths": ["Desktop", "Downloads"],
      "userFavorites": [
        { "name": "项目文件", "path": "D:\\Projects", "addedAt": "2026-06-09T..." }
      ]
    }
    ```
  - **系统路径定义**（硬编码，不存路径到 JSON）:
    ```csharp
    private static readonly (string key, SpecialFolder folder, string defaultName)[] BuiltInSystemPaths =
    {
        ("Desktop",    SpecialFolder.Desktop,    "桌面"),
        ("Documents",  SpecialFolder.MyDocuments, "文档"),
        ("Downloads",  SpecialFolder.UserProfile, "下载"),  // 实际路径: UserProfile\Downloads
    };
    ```
    - 注意: Downloads 没有直接的 SpecialFolder 枚举，使用 `Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), "Downloads")`
  - 接口:
    ```csharp
    public static class FavoritePathManager
    {
        public static List<FavoritePathItem> GetAll();        // 系统路径(未隐藏) + 用户收藏合并
        public static List<FavoritePathItem> GetSystemPaths(); // 所有系统路径(含隐藏的)
        public static List<FavoritePathItem> GetUserFavorites(); // 仅用户收藏
        public static void Add(string name, string path);
        public static void Remove(string path);                // 删除用户收藏
        public static void Update(string oldPath, string newName, string newPath);
        public static void Reorder(int oldIndex, int newIndex);
        public static bool Exists(string path);                // 去重检查
        public static bool IsSystemPath(string path);          // 判断是否为系统路径
        public static void SetSystemPathHidden(string key, bool hidden); // 隐藏/显示系统路径
        public static bool IsSystemPathHidden(string key);     // 查询系统路径隐藏状态
        public static void Save();
        public static void Load();
    }
    ```
  - `GetAll()` 逻辑:
    1. 从 BuiltInSystemPaths 生成系统路径列表，调用 `Environment.GetFolderPath()` 获取真实路径
    2. 过滤掉 `hiddenSystemPaths` 中的项
    3. 与 `userFavorites` 合并返回（系统路径在前，用户收藏在后）
  - JSON 序列化使用 `System.Text.Json`（与 PasswordManager 一致）
  - 文件不存在时初始化空状态（hiddenSystemPaths=[], userFavorites=[]），不抛异常
  - 写入时使用 `File.WriteAllText`（与现有其他 Manager 一致）

  **Must NOT do**:
  - 不添加 NuGet 包依赖（使用 System.Text.Json，已存在）
  - 不存储密码等敏感信息
  - 系统路径的完整路径不存 JSON（仅存 SpecialFolder 枚举名用于隐藏状态）
  - 系统路径不允许删除/编辑——由调用者（UI 层）负责禁用按钮

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 简单的 JSON 持久化类
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with T1, T3)
  - **Blocks**: T4, T5, T6, T10
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.Core/Utils/PasswordManager.cs` — 现有 JSON 持久化模式参考（加密部分除外）
  - `src/MantisZip.UI/AppSettings.cs` — JSON 配置读写模式参考

  **Acceptance Criteria**:
  - [ ] 添加收藏 → 保存到 favorites.json
  - [ ] 删除收藏 → 从 JSON 中移除
  - [ ] 修改收藏 → JSON 更新
  - [ ] 重排序 → 顺序保持
  - [ ] 重复路径检测 → Add 时返回 false 或覆盖确认
  - [ ] favorites.json 不存在 → 初始化空状态，不抛异常
  - [ ] 重启后数据持续存在
  - [ ] `GetAll()` 返回系统路径（桌面/文档/下载）+ 用户收藏合并列表
  - [ ] `IsSystemPath(path)` 对桌面/文档/下载返回 true
  - [ ] `SetSystemPathHidden("Desktop", true)` → `GetAll()` 不包含桌面
  - [ ] `SetSystemPathHidden("Desktop", false)` → `GetAll()` 恢复桌面
  - [ ] 隐藏状态持久化到 JSON，重启后保持

  **QA Scenarios**:
  ```
  Scenario: 收藏夹完整 CRUD
    Tool: Bash (dotnet test)
    Preconditions: 干净的 favorites.json（或临时测试目录）
    Steps:
      1. Add("项目", "D:\\Projects")
      2. Add("照片", "E:\\Photos")
      3. GetAll() → 返回 2 条用户收藏 + 3 条系统路径 = 5 条
      4. Exists("D:\\Projects") → true
      5. Update("D:\\Projects", "新项目", "D:\\Dev\\Project")
      6. Reorder(1, 0)
      7. Remove("E:\\Photos")
    Expected Result: 所有操作成功，JSON 文件内容正确
    Evidence: .sisyphus/evidence/task-2-favorite-crud.txt

  Scenario: 系统路径隐藏/显示
    Tool: Bash (dotnet test)
    Preconditions: 干净的 favorites.json
    Steps:
      1. GetAll().Count → 3（桌面/文档/下载都可见）
      2. SetSystemPathHidden("Desktop", true) → Save()
      3. GetAll().Count → 2（桌面隐藏了）
      4. 重新 Load() → GetAll().Count → 2（持久化正确）
      5. SetSystemPathHidden("Desktop", false) → Save()
      6. GetAll().Count → 3
    Expected Result: 隐藏状态持久化，重启后保持
    Evidence: .sisyphus/evidence/task-2-system-path-hide.txt

  Scenario: 跨重启持久化
    Tool: Bash
    Preconditions: 同上
    Steps:
      1. Add 几条收藏 + 隐藏桌面
      2. Save()
      3. 重新 Load()
    Expected Result: 用户收藏和隐藏状态都正确持久化
    Evidence: .sisyphus/evidence/task-2-favorite-persist.txt
  ```

  **Commit**: YES
  - Message: `feat(core): add FavoritePathManager for favorites persistence`
  - Files: `src/MantisZip.Core/Utils/FavoritePathManager.cs`

---

- [ ] 3. PathHistoryManager — 历史记录自动追踪

  **What to do**:
  - 在 `src/MantisZip.Core/Utils/PathHistoryManager.cs` 创建静态类
  - 历史记录模型:
    ```csharp
    public class PathHistoryEntry
    {
        public string Path { get; set; } = "";
        public DateTime LastUsedAt { get; set; }
    }
    ```
  - 接口:
    ```csharp
    public static class PathHistoryManager
    {
        public static List<PathHistoryEntry> GetRecent(int maxCount = 50);
        public static void Record(string path);           // 记录/更新路径使用
        public static void Clear();
        public static void Save();
        public static void Load();
    }
    ```
  - Record 行为:
    - 路径已存在 → 移到最前，更新 LastUsedAt
    - 路径不存在 → 添加到最后，超过 50 条移除最旧的
    - 忽略空字符串和 null
  - 与 favorites.json 同文件存储（或独立 history.json，取决于实现方便）
  - **记录时机**: 仅当用户提交操作时由调用者主动调用 Record()，自动计算的路径不记录

  **Must NOT do**:
  - 不自动监听 TextBox 变化（只在提交时记录）
  - 不记录空路径或无效路径

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 简单的列表管理类
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with T1, T2)
  - **Blocks**: T4, T5, T10
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.Core/Utils/FavoritePathManager.cs` — 相似的 JSON 持久化模式（完成 T2 后引用）

  **Acceptance Criteria**:
  - [ ] Record("D:\\A") → 列表包含 D:\A
  - [ ] Record("D:\\A") 重复 → 列表仍为 1 条（去重），移到最前
  - [ ] 记录 55 条不同路径 → 只保留最近 50 条（移除最旧的 5 条）
  - [ ] Load/Save 持久化正确
  - [ ] 记录空字符串 → 忽略

  **QA Scenarios**:
  ```
  Scenario: 历史记录基本功能
    Tool: Bash (dotnet test)
    Preconditions: 干净的历史记录
    Steps:
      1. Record("D:\\A"), Record("D:\\B"), Record("D:\\C")
      2. GetRecent() → [C, B, A]
      3. Record("D:\\A") → [A, C, B]（移到顶部）
      4. 添加 52 条 → 只保留 50 条
    Expected Result: 去重正确，排序正确，数量限制正确
    Evidence: .sisyphus/evidence/task-3-history-basic.txt
  ```

  **Commit**: YES
  - Message: `feat(core): add PathHistoryManager for recent path tracking`
  - Files: `src/MantisZip.Core/Utils/PathHistoryManager.cs`

---

- [ ] 4. QuickPathControl — WPF UserControl（TextBox + 4 按钮，双模式）

  **What to do**:
  - 在 `src/MantisZip.UI/Controls/QuickPathControl.xaml` + `.cs` 创建 UserControl
  - 不要在现有目录建新的 Controls 文件夹——如果不存在则新建 `src/MantisZip.UI/Controls/`
  - **XAML 布局**:
    ```xml
    <UserControl ...>
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="Auto"/>
          <ColumnDefinition Width="Auto"/>
          <ColumnDefinition Width="Auto"/>
          <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <!-- TextBox -->
        <TextBox x:Name="PathTextBox" Grid.Column="0" Height="24"
                 Text="{Binding PathText, RelativeSource={RelativeSource AncestorType=UserControl}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"/>
        <!-- Fav button -->
        <Button x:Name="FavButton" Grid.Column="1" Width="28" Height="24"
                ToolTip="{l:L QuickPath_Favorites}" Click="FavButton_Click"
                Background="{DynamicResource Theme_ButtonBg}"
                Foreground="{DynamicResource Theme_TextPrimary}"/>
        <!-- History button -->
        <Button x:Name="HistoryButton" Grid.Column="2" Width="28" Height="24"
                ToolTip="{l:L QuickPath_History}" Click="HistoryButton_Click"
                Background="{DynamicResource Theme_ButtonBg}"
                Foreground="{DynamicResource Theme_TextPrimary}"/>
        <!-- Explorer button -->
        <Button x:Name="ExplorerButton" Grid.Column="3" Width="28" Height="24"
                ToolTip="{l:L QuickPath_Explorer}" Click="ExplorerButton_Click"
                Background="{DynamicResource Theme_ButtonBg}"
                Foreground="{DynamicResource Theme_TextPrimary}"/>
        <!-- Browse button -->
        <Button x:Name="BrowseButton" Grid.Column="4" MinWidth="60" Height="24"
                Content="{l:L Settings_Advanced_Browse}" Click="BrowseButton_Click"
                Background="{DynamicResource Theme_ButtonBg}"
                Foreground="{DynamicResource Theme_TextPrimary}"
                BorderBrush="{DynamicResource Theme_Border}"/>
      </Grid>
    </UserControl>
    ```
  - **按钮图标**: 使用 emoji 或文本符号（⭐ / 🕐 / 🪟）作为按钮 Content，确保不依赖外部图标文件
  - **依赖属性**:
    - `PathText` (string): 双向绑定的目录路径文本。文件夹模式=完整路径，文件模式=仅目录部分
    - `FileName` (string): 文件模式下的文件名（含扩展名，如 "archive.zip"）。文件夹模式不使用
    - `IsFolderMode` (bool): true=文件夹模式(VistaFolderBrowserDialog); false=文件模式(SaveFileDialog)
    - `FileTypeFilter` (string): 文件模式时的 SaveFileDialog 过滤条件（如 "ZIP files|*.zip"）
    - `DefaultFileName` (string): 文件模式时的默认文件名（如 "archive.zip"）
    - `IsReadOnly` (bool): 非手动模式时设为 true，禁用所有按钮
  - **双模式下 PathText 与 FileName 的关系**:
    - 文件夹模式: `PathText` = 完整文件夹路径, `FileName` 不使用
    - 文件模式: `PathText` = 仅目录部分（如 "D:\Backups"）, `FileName` = 文件名含扩展名（如 "archive.zip"）
    - 调用者组合完整路径: `Path.Combine(PathText, FileName)`
  - **下拉菜单逻辑**:
    - FavButton → 创建 ContextMenu，列出 FavoritePathManager.GetAll()（系统路径 + 用户收藏合并）
    - 系统路径项在菜单中显示 🔒 前缀图标（或小锁图标），用户收藏不显示
    - 菜单底部固定添加「管理收藏…」分隔线 + 菜单项
    - HistoryButton → 创建 ContextMenu，列出 PathHistoryManager.GetRecent(50)
    - ExplorerButton → 创建 ContextMenu，列出 ExplorerWindowTracker.GetOpenExplorerWindows()
    - 点击任一菜单项 → 文件夹模式设置 PathText；文件模式仅设置 PathText（目录），FileName 保持不变
    - 每个下拉菜单在打开时动态刷新数据
  - **空状态处理**:
    - 收藏（系统路径+用户收藏）全部为空或全部隐藏时 → FavButton 下拉显示「暂无收藏」
    - 历史为空 → HistoryButton 下拉显示「暂无历史记录」
    - 无资源管理器窗口 → ExplorerButton 下拉显示「没有打开的文件夹」
  - **浏览按钮**:
    - 文件夹模式 → 打开 VistaFolderBrowserDialog，选中后设置 PathText
    - 文件模式 → 打开 SaveFileDialog（带 FileTypeFilter），选中后:
      - 解析完整路径 → 目录部分写入 PathText，文件名部分写入 FileName
      - 如选中 "D:\Backups\myarchive.zip" → PathText="D:\Backups", FileName="myarchive.zip"
  - **「管理收藏…」** 点击 → 打开 FavoriteManagerWindow 模态弹窗
  - **主题绑定**: 所有控件显式绑定 Theme_* 资源键

  **Localization 新字符串**（需要添加到 Localization 文件）:
  - `QuickPath_Favorites` = "收藏" / "Favorites"
  - `QuickPath_History` = "历史" / "History"
  - `QuickPath_Explorer` = "已打开" / "Open Windows"
  - `QuickPath_ManageFavorites` = "管理收藏…" / "Manage Favorites…"
  - `QuickPath_NoFavorites` = "暂无收藏" / "No favorites"
  - `QuickPath_NoHistory` = "暂无历史记录" / "No history"
  - `QuickPath_NoExplorer` = "没有打开的文件夹" / "No open folders"
  - `QuickPath_CurrentWindow` = "当前窗口" / "Current window"
  - `Main_Menu_FavoriteManager` = "管理收藏…" / "Manage Favorites…"
  - `QuickPath_FolderMode` = "选择文件夹" / "Select folder"
  - `QuickPath_FileMode` = "选择保存路径" / "Select save path"
  - `QuickPath_Desktop` = "桌面" / "Desktop"
  - `QuickPath_Documents` = "文档" / "Documents"
  - `QuickPath_Downloads` = "下载" / "Downloads"
  - `QuickPath_SystemLabel` = "系统" / "System"
  - `QuickPath_Hide` = "隐藏" / "Hide"
  - `QuickPath_Show` = "显示" / "Show"

  **Must NOT do**:
  - 不使用外部图标文件（按钮用文本/emoji）
  - 不添加 WPF 控件库依赖
  - 不修改 Global 主题资源文件

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering` — WPF UserControl 布局和交互
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with T5, T6)
  - **Blocks**: T7, T8, T9
  - **Blocked By**: T1, T2, T3

  **References**:
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml:84-91` — 当前路径输入布局结构
  - `src/MantisZip.UI/Dialogs/ExtractSettingsWindow.xaml:84-96` — 同上，解压版本
  - `src/MantisZip.UI/SystemIconHelper.cs` — 现有 Win32 交互模式
  - `src/MantisZip.Core/Utils/FavoritePathManager.cs` — 收藏数据源（T2）
  - `src/MantisZip.Core/Utils/PathHistoryManager.cs` — 历史数据源（T3）
  - `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs` — 资源管理器窗口数据源（T1）
  - `src/MantisZip.UI/Localization/strings.zh.json` + `strings.en.json` — 本地化文件

  **Acceptance Criteria**:
  - [ ] UserControl 正确渲染：TextBox + 4 个按钮横排
  - [ ] 四个按钮有正确的 ToolTip
  - [ ] FavButton 下拉显示系统路径（🔒） + 用户收藏 + 底部「管理收藏…」
  - [ ] 系统路径项在菜单中带 🔒 标识
  - [ ] 被隐藏的系统路径不在下拉菜单中出现
  - [ ] HistoryButton 下拉显示历史列表
  - [ ] ExplorerButton 下拉显示已打开窗口列表，当前窗口高亮
  - [ ] 点击下拉项 → PathTextBox 自动填充
  - [ ] 「管理收藏…」→ 打开 FavoriteManagerWindow
  - [ ] BrowseButton：文件夹模式打开 VistaFolderBrowserDialog，文件模式打开 SaveFileDialog
  - [ ] IsReadOnly=true → 所有按钮禁用
  - [ ] 空状态显示友好提示文字
  - [ ] 亮/暗主题均正常

  **QA Scenarios**:
  ```
  Scenario: 控件基本显示
    Tool: Bash（构建+启动）
    Preconditions: 在 CompressSettingsWindow 中临时替换为 QuickPathControl
    Steps:
      1. dotnet build
      2. 启动应用，打开压缩对话框
    Expected Result: QuickPathControl 正常显示，TextBox + [⭐][🕐][🪟][浏览] 横排
    Evidence: .sisyphus/evidence/task-4-control-appearance.png

  Scenario: 收藏下拉含系统路径
    Tool: Bash（启动+截图）
    Preconditions: 已添加 2 条收藏（如「项目」→ D:\Projects），未隐藏系统路径
    Steps:
      1. 点击 [⭐] 按钮
    Expected Result: 下拉菜单显示 🔒桌面、🔒文档、🔒下载，然后「项目」「管理收藏…」
    Evidence: .sisyphus/evidence/task-4-fav-dropdown.png

  Scenario: 隐藏的系统路径不出现
    Tool: Bash（启动+截图）
    Preconditions: 通过 FavoriteManagerWindow 隐藏了「桌面」
    Steps:
      1. 点击 [⭐] 按钮
    Expected Result: 下拉菜单不显示桌面，只显示文档、下载和用户收藏
    Evidence: .sisyphus/evidence/task-4-hidden-path.png

  Scenario: 选择路径自动填入
    Tool: Bash（启动+截图）
    Preconditions: 同上
    Steps:
      1. 点击 [⭐] → 点击「桌面」
    Expected Result: PathTextBox 内容变为 C:\Users\Admin\Desktop
    Evidence: .sisyphus/evidence/task-4-path-filled.png

  Scenario: 浏览按钮（文件夹模式）
    Tool: Bash（启动+截图）
    Preconditions: IsFolderMode=true
    Steps:
      1. 点击 [浏览]
    Expected Result: VistaFolderBrowserDialog 弹出
    Evidence: .sisyphus/evidence/task-4-browse-folder.png

  Scenario: 空状态显示
    Tool: Bash（启动+截图）
    Preconditions: 无收藏、无历史、无资源管理器窗口
    Steps:
      1. 分别点击三个按钮
    Expected Result: 显示「暂无收藏」「暂无历史记录」「没有打开的文件夹」
    Evidence: .sisyphus/evidence/task-4-empty-states.png
  ```

  **Commit**: YES
  - Message: `feat(ui): add QuickPathControl UserControl with dual-mode path input`
  - Files: `src/MantisZip.UI/Controls/QuickPathControl.xaml`, `src/MantisZip.UI/Controls/QuickPathControl.cs`

---

- [ ] 5. QuickPathDialog — 模态弹窗包装 QuickPathControl

  **What to do**:
  - 在 `src/MantisZip.UI/Dialogs/QuickPathDialog.xaml` + `.cs` 创建 Window
  - **XAML 布局**:
    ```xml
    <Window ...
            Title="{l:L QuickPath_SelectFolder}"
            WindowStartupLocation="CenterOwner"
            SizeToContent="WidthAndHeight"
            MinWidth="500" MinHeight="300"
            ResizeMode="CanResize"
            Background="{DynamicResource Theme_WindowBg}">
      <Grid Margin="10">
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>   <!-- 标题 -->
          <RowDefinition Height="*"/>      <!-- QuickPathControl -->
          <RowDefinition Height="Auto"/>   <!-- 按钮行 -->
        </Grid.RowDefinitions>
        <TextBlock Grid.Row="0" Text="{l:L QuickPath_SelectFolder}"
                   Foreground="{DynamicResource Theme_TextPrimary}"
                   FontSize="14" FontWeight="SemiBold" Margin="0,0,0,8"/>
        <!-- 中间：QuickPathControl 撑满宽度 -->
        <controls:QuickPathControl x:Name="PathControl" Grid.Row="1"
                                    IsFolderMode="True"/>
        <!-- 底部：确认/取消 -->
        <StackPanel Grid.Row="2" Orientation="Horizontal"
                    HorizontalAlignment="Right" Margin="0,10,0,0">
          <Button Content="{l:L Common_OK}" Width="80" Height="28"
                  Click="OkButton_Click"
                  Background="{DynamicResource Theme_Accent}"
                  Foreground="White" Margin="0,0,8,0"/>
          <Button Content="{l:L Common_Cancel}" Width="80" Height="28"
                  Click="CancelButton_Click"
                  Background="{DynamicResource Theme_ButtonBg}"
                  Foreground="{DynamicResource Theme_TextPrimary}"/>
        </StackPanel>
      </Grid>
    </Window>
    ```
  - **属性**:
    - `SelectedPath` (string?) — 用户选择的路径，取消时为 null
    - `DialogResult` (bool) — true 确认，false 取消
  - **行为**:
    - OkButton_Click: 验证 PathControl.PathText 非空 → 设置 SelectedPath + DialogResult=true → Close
    - CancelButton_Click: DialogResult=false → Close
    - 回车 → 相当于点击确认
    - Esc → 相当于点击取消
  - **Owner**: 由调用者设置，通常在 CompressSettingsWindow/ExtractSettingsWindow 中设为 `this`，或在 App.xaml.cs 中设为当前活动窗口
  - **主题绑定**: Window 背景、所有控件绑定 Theme_* 资源

  **Must NOT do**:
  - 不添加路径验证逻辑（如目录是否存在）
  - 不修改 PathControl 之外的核心逻辑

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering` — WPF 弹窗 UI
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with T4, T6)
  - **Blocks**: T7, T8, T9
  - **Blocked By**: T4 (QuickPathControl)

  **References**:
  - `src/MantisZip.UI/Controls/QuickPathControl.xaml` + `.cs` — 依赖的控件（T4）
  - `src/MantisZip.UI/Dialogs/PasswordDialog.xaml` — 现有模态弹窗模式参考

  **Acceptance Criteria**:
  - [ ] 弹窗正常显示，居中于 Owner
  - [ ] QuickPathControl 内嵌在弹窗中，正常工作
  - [ ] 确认按钮 → 设置 SelectedPath + 关闭弹窗
  - [ ] 取消按钮 / Esc → DialogResult=false
  - [ ] 回车 → 相当于确认
  - [ ] 亮/暗主题正常

  **QA Scenarios**:
  ```
  Scenario: 快速路径对话框交互
    Tool: Bash（启动+截图）
    Preconditions: 通过 AddFolderButton 触发 QuickPathDialog
    Steps:
      1. 在压缩对话框中点击「添加文件夹」
      2. QuickPathDialog 弹出
      3. 通过收藏/历史/资源管理器选择一个路径
      4. 点击确认
    Expected Result: 路径回传到调用者，对话框关闭
    Evidence: .sisyphus/evidence/task-5-dialog-flow.png

  Scenario: 取消操作
    Tool: Bash
    Preconditions: 弹出 QuickPathDialog
    Steps:
      1. 点击取消 / 按 Esc
    Expected Result: DialogResult=false，SelectedPath=null
    Evidence: .sisyphus/evidence/task-5-dialog-cancel.png
  ```

  **Commit**: YES
  - Message: `feat(ui): add QuickPathDialog modal for standalone path selection`
  - Files: `src/MantisZip.UI/Dialogs/QuickPathDialog.xaml`, `src/MantisZip.UI/Dialogs/QuickPathDialog.cs`

---

- [ ] 6. FavoriteManagerWindow — 收藏管理窗口（含系统路径隐藏/显示）

  **What to do**:
  - 在 `src/MantisZip.UI/Dialogs/FavoriteManagerWindow.xaml` + `.cs` 创建 Window
  - **XAML 布局**: 标题「管理收藏」+ ListView（类型、名称、路径、操作）+ 底部按钮行
  - ListView 每行（混排，系统路径在前，用户收藏在后）:
    - 类型列：系统路径显示 🔒 +「系统」标签；用户收藏显示 📁 图标
    - 友好名称
    - 完整路径
    - 操作列：
      - **系统路径** → 「隐藏」/「显示」按钮（切换隐藏状态）
      - **用户收藏** → 「删除」按钮（确认后删除）
  - **系统路径视觉区分**:
    - 系统路径行背景色略不同（如 `#F5F5F5` 亮色 / `#2D2D2D` 暗色，使用现成主题资源）
    - 系统路径行名称不可编辑，用户收藏可双击编辑
    - 🔒 图标 + `QuickPath_SystemLabel` 标签标识
  - **底部按钮行**:
    - 「添加收藏」→ 弹出输入框（名称 + 路径），默认路径为上一次 QuickPathControl 的 PathText
    - 「上移」「下移」→ 调整用户收藏排序顺序（系统路径固定排在前面，不可排序）
  - **数据源**: 通过 `FavoritePathManager.GetSystemPaths()` + `FavoritePathManager.GetUserFavorites()` 分别加载
    ```csharp
    // 合并显示逻辑:
    var systemPaths = FavoritePathManager.GetSystemPaths();  // 所有系统路径（含隐藏的）
    var userFavorites = FavoritePathManager.GetUserFavorites();
    // UI 中系统路径在前，用户收藏在后，中间可选分隔线
    ```
  - **隐藏/显示处理**:
    ```csharp
    private void ToggleSystemPath_Click(string key)
    {
        var isHidden = FavoritePathManager.IsSystemPathHidden(key);
        FavoritePathManager.SetSystemPathHidden(key, !isHidden);
        FavoritePathManager.Save();
        RefreshList();  // 刷新列表显示
    }
    ```
  - **添加/删除/编辑处理**（仅限用户收藏）:
    - 添加时：名称和路径均不能为空，路径重复时提示
    - 编辑时：验证名称非空，路径非空
    - 删除时：确认后删除（系统路径不可删除，此按钮不出现）
  - **修改后自动保存**: 每次增删改排序/隐藏切换后调用 `FavoritePathManager.Save()`
  - **主题绑定**: 所有控件绑定 Theme_* 资源键
  - **多入口**:
    1. QuickPathControl ⭐ 下拉菜单 → 「管理收藏…」
    2. **主窗口菜单 → 工具 → 「管理收藏…」**（新增 `Main_Menu_FavoriteManager` 菜单项，放在密码管理器下方）
  - **主菜单入口实现**:
    - 在 MainWindow.xaml 的工具菜单中密码管理器下方添加:
      ```xml
      <MenuItem Header="{l:L Main_Menu_FavoriteManager}" Click="FavoriteManager_Click">
          <MenuItem.Icon><emoji:TextBlock Text="⭐" FontSize="14" Margin="2,0,4,0" VerticalAlignment="Center"/></MenuItem.Icon>
      </MenuItem>
      ```
    - 在 `MainWindow.xaml.cs` 添加 `FavoriteManager_Click` 处理器:
      ```csharp
      private void FavoriteManager_Click(object sender, RoutedEventArgs e)
      {
          var dialog = new FavoriteManagerWindow { Owner = this };
          dialog.ShowDialog();
      }
      ```

  **Must NOT do**:
  - 不添加拖拽排序（使用上下移按钮即可）
  - 不批量操作（单条增删改）
  - 系统路径不可删除、不可编辑名称/路径
  - 不修改系统路径的排序（固定排在用户收藏前面）

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering` — WPF 数据管理窗口
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with T4, T5)
  - **Blocks**: None（独立窗口，无下游依赖）
  - **Blocked By**: T2 (FavoritePathManager)

  **References**:
  - `src/MantisZip.Core/Utils/FavoritePathManager.cs` — 数据源（T2）
  - `src/MantisZip.UI/Dialogs/PasswordManagerWindow.xaml` — 现有管理窗口模式参考
  - `src/MantisZip.UI/Dialogs/PasswordManagerWindow.xaml.cs` — 交互逻辑参考

  **Acceptance Criteria**:
  - [ ] 窗口打开，显示系统路径（🔒 系统）+ 用户收藏混合列表
  - [ ] 系统路径行：点击「隐藏」→ 路径从 GetAll() 中消失；点击「显示」→ 恢复
  - [ ] 用户收藏：添加/编辑/删除/排序全部正常
  - [ ] 系统路径不可编辑名称、不可删除
  - [ ] 隐藏状态持久化，关闭重开后保持
  - [ ] 上移/下移仅影响用户收藏顺序，系统路径固定在前
  - [ ] 重复路径提示
  - [ ] 亮/暗主题正常

  **QA Scenarios**:
  ```
  Scenario: 收藏管理完整生命周期（含系统路径）
    Tool: Bash（启动+截图）
    Preconditions: 从 QuickPathControl 点击「管理收藏…」
    Steps:
      1. 看到 3 条系统路径（🔒桌面、🔒文档、🔒下载）+ 分隔线 + 空用户收藏区
      2. 隐藏桌面 → 桌面行显示「显示」按钮，行变灰
      3. 添加用户收藏（名称="项目" 路径="D:\Projects"）
      4. 添加用户收藏（名称="照片" 路径="E:\Photos"）
      5. 列表显示 2 条系统路径（桌面不可见）+ 2 条用户收藏
      6. 编辑「项目」→ 改为「开发项目」
      7. 下移「照片」
      8. 再次显示桌面
    Expected Result: 每一步操作后列表和数据正确同步
    Evidence: .sisyphus/evidence/task-6-favmanager-full.png

  Scenario: 系统路径不可删除
    Tool: Bash
    Preconditions: FavoriteManagerWindow 打开
    Steps:
      1. 选中系统路径行
    Expected Result: 没有「删除」按钮，只有「隐藏/显示」按钮，名称不可编辑
    Evidence: .sisyphus/evidence/task-6-system-path-protected.png

  Scenario: 重复路径提示
    Tool: Bash
    Preconditions: 已存在用户收藏「D:\Projects」
    Steps:
      1. 再次添加同名路径
    Expected Result: 提示「该路径已存在」
    Evidence: .sisyphus/evidence/task-6-favmanager-duplicate.png
  ```

  **Commit**: YES
  - Message: `feat(ui): add FavoriteManagerWindow for favorites management`
  - Files: `src/MantisZip.UI/Dialogs/FavoriteManagerWindow.xaml`, `src/MantisZip.UI/Dialogs/FavoriteManagerWindow.cs`

---

- [ ] 7. CompressSettingsWindow — 替换 OutputPathTextBox 为 QuickPathControl

  **What to do**:
  - 修改 `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml`
  - **压缩对话框改为双行布局（文件模式：目录 + 文件名分开）**:
    - 原布局:
      ```xml
      <Grid Grid.Row="1" Grid.Column="1">
          <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*"/>
              <ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <TextBox x:Name="OutputPathTextBox" Grid.Column="0" .../>
          <Button x:Name="BrowseOutputButton" Content="浏览" Width="60" .../>
      </Grid>
      ```
    - 替换为:
      ```xml
      <!-- 第一行: 目录选择（嵌入标准 QuickPathControl，文件模式） -->
      <Grid Grid.Row="1" Grid.Column="1" Margin="0,0,0,4">
          <Grid.ColumnDefinitions>
              <ColumnDefinition Width="Auto"/>
              <ColumnDefinition Width="*"/>
          </Grid.ColumnDefinitions>
          <TextBlock Grid.Column="0" Text="{l:L Compress_OutputDir}"
                     VerticalAlignment="Center" Margin="0,0,8,0"
                     Foreground="{DynamicResource Theme_TextPrimary}"/>
          <controls:QuickPathControl x:Name="OutputPathControl" Grid.Column="1"
                                      IsFolderMode="False"
                                      FileTypeFilter="ZIP files|*.zip|7z files|*.7z|TarGz files|*.tar.gz"
                                      DefaultFileName="archive.zip"/>
      </Grid>

      <!-- 第二行: 文件名输入 -->
      <Grid Grid.Row="2" Grid.Column="1">
          <Grid.ColumnDefinitions>
              <ColumnDefinition Width="Auto"/>
              <ColumnDefinition Width="*"/>
              <ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <TextBlock Grid.Column="0" Text="{l:L Compress_FileName}"
                     VerticalAlignment="Center" Margin="0,0,8,0"
                     Foreground="{DynamicResource Theme_TextPrimary}"/>
          <TextBox Grid.Column="1" Height="24"
                   Text="{Binding ElementName=OutputPathControl, Path=FileName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                   Background="{DynamicResource Theme_WindowBg}"
                   Foreground="{DynamicResource Theme_TextPrimary}"
                   BorderBrush="{DynamicResource Theme_Border}"/>
          <TextBlock x:Name="ExtensionLabel" Grid.Column="2" VerticalAlignment="Center" Margin="4,0,0,0"
                     Text=".zip" Foreground="{DynamicResource Theme_TextSecondary}"/>
      </Grid>
      ```
  - 注意：第一行嵌入的是**标准 QuickPathControl**，所有按钮（⭐🕐🪟浏览）完整保留，无需手动重新绑定
  - 修改 `CompressSettingsWindow.xaml.cs`:
    - 移除对 `OutputPathTextBox` 和 `BrowseOutputButton` 的直接引用
    - 输出路径 = `OutputPathControl.PathText`（目录部分）+ 反斜杠 + `OutputPathControl.FileName`（文件名含扩展名）
    - `OutputMode_Changed` → 非手动模式设置 `OutputPathControl.IsReadOnly=true`，禁用文件名 TextBox 和 ExtensionLabel
    - `FormatComboBox_SelectionChanged` → **自动更新扩展名逻辑**:
      ```csharp
      private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
      {
          // 1. 获取选中格式对应的扩展名
          var ext = GetExtensionFromFormat(selectedFormat); // ".zip" / ".7z" / ".tar.gz"

          // 2. 更新 QuickPathControl 的默认文件名
          var currentFileName = OutputPathControl.FileName;
          if (!string.IsNullOrEmpty(currentFileName))
          {
              // 去掉旧扩展名，加上新扩展名
              var baseName = Path.GetFileNameWithoutExtension(currentFileName);
              OutputPathControl.FileName = baseName + ext;
          }
          else
          {
              // 无文件名时设默认
              OutputPathControl.FileName = "archive" + ext;
          }

          // 3. 更新 ExtensionLabel 视觉指示器
          ExtensionLabel.Text = ext;

          // 4. 更新 QuickPathControl 的 SaveFileDialog 过滤条件
          OutputPathControl.FileTypeFilter = GetFilterFromFormat(selectedFormat);
      }
      ```
    - 移除 add/remove handler 中对旧 `OutputPathTextBox` 的直接操作
  - **完整路径组合**: `Path.Combine(OutputPathControl.PathText, OutputPathControl.FileName)`
  - **历史记录**: 用户点击「压缩」按钮成功时，调用 `PathHistoryManager.Record(Path.Combine(OutputPathControl.PathText, OutputPathControl.FileName))`
  - 保留 `x:Name="OutputPathTextBox"` 的兼容引用（如有其他代码引用）或统一清理
  - 新增本地化字符串:
    - `Compress_OutputDir` = "输出目录" / "Output directory"
    - `Compress_FileName` = "文件名" / "File name"

  **Must NOT do**:
  - 不修改 CompressSettingsWindow.xaml 的整体结构（仅替换路径输入区域）
  - 不改变压缩逻辑（仅替换 UI 输入方式）
  - 不破坏现有快捷键（Ctrl+G 已被替换方案取代，无需添加）

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 简单的 XAML 替换 + code-behind 调整
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with T8, T9)
  - **Blocks**: T12
  - **Blocked By**: T4, T5

  **References**:
  - `src/MantisZip.UI/Controls/QuickPathControl.xaml` + `.cs` — 依赖的控件（T4）
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml:84-91` — 当前路径输入布局
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs` — 现有事件处理

  **Acceptance Criteria**:
  - [ ] 压缩对话框显示双行布局：第一行「输出目录」内嵌标准 QuickPathControl 完整控件，第二行「文件名」
  - [ ] 手动模式：QuickPathControl 可操作（⭐🕐🪟浏览均正常），文件名可编辑
  - [ ] 分卷/合并模式：QuickPathControl 按钮禁用（IsReadOnly=true），文件名和扩展名标签也禁用
  - [ ] 格式切换时自动改文件名扩展名（"archive.zip" → "archive.7z" → "archive.tar.gz"）
  - [ ] 格式切换时更新 ExtensionLabel（.zip → .7z → .tar.gz）
  - [ ] 收藏/历史/资源管理器下拉正常工作（仅设置目录部分，文件名不变）
  - [ ] 浏览按钮打开 SaveFileDialog，选中后目录和文件名分别填入 PathText 和 FileName
  - [ ] 压缩成功后组合完整路径记入历史

  **QA Scenarios**:
  ```
  Scenario: 压缩对话框双行布局显示
    Tool: Bash（启动+截图）
    Preconditions: 打开 MantisZip，添加源文件，打开压缩对话框
    Steps:
      1. 观察路径输入区域
      2. 确认第一行：标准 QuickPathControl（TextBox + [⭐][🕐][🪟][浏览]）
      3. 确认第二行：文件名 TextBox + ".zip" 后缀标签
      4. 点击 [⭐] 验证收藏下拉
      5. 点击 [浏览] 验证 SaveFileDialog
    Expected Result: 双行布局正确显示，QuickPathControl 完整功能正常
    Evidence: .sisyphus/evidence/task-7-compress-layout.png

  Scenario: 手动模式 → 完整保存流程
    Tool: Bash
    Preconditions: 压缩对话框，手动模式，默认文件名为 "archive.zip"
    Steps:
      1. 从收藏选择目录 D:\Output → QuickPathControl 目录填入 D:\Output
      2. 文件名栏将 "archive" 改为 "mybackup"
      3. 点击压缩
    Expected Result: 压缩包保存到 D:\Output\mybackup.zip
    Evidence: .sisyphus/evidence/task-7-compress-flow.png

  Scenario: 分卷模式禁用
    Tool: Bash
    Preconditions: 压缩对话框
    Steps:
      1. 切换输出模式为「分卷压缩」
    Expected Result: QuickPathControl 全部按钮禁用，文件名 TextBox 禁用
    Evidence: .sisyphus/evidence/task-7-compress-readonly.png

  Scenario: 格式切换自动更新扩展名
    Tool: Bash
    Preconditions: 压缩对话框，手动模式，默认文件名 "archive.zip"
    Steps:
      1. 切换到 7z → FileName 变为 "archive.7z"，ExtensionLabel 变为 .7z
      2. 用户手动改名为 "backup.7z"
      3. 切换到 tar.gz → FileName 变为 "backup.tar.gz"
      4. 再切回 ZIP → FileName 变为 "backup.zip"
    Expected Result: 每次切换格式时，文件名中的扩展名自动替换，保留用户输入的文件基本名
    Evidence: .sisyphus/evidence/task-7-format-switch.png
  ```

  **Commit**: YES
  - Message: `feat(ui): integrate QuickPathControl into CompressSettingsWindow`
  - Files: `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml`, `src.MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs`

---

- [ ] 8. ExtractSettingsWindow — 替换 ManualPathTextBox 为 QuickPathControl

  **What to do**:
  - 修改 `src/MantisZip.UI/Dialogs/ExtractSettingsWindow.xaml:84-96`
  - 原布局:
    ```xml
    <Grid Grid.Row="1" Grid.Column="1">
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
        <TextBox x:Name="ManualPathTextBox" .../>
        <Button x:Name="BrowseButton" Content="浏览" .../>
    </Grid>
    ```
  - 替换为:
    ```xml
    <controls:QuickPathControl x:Name="ManualPathControl" Grid.Row="1" Grid.Column="1"
                                IsFolderMode="True"
                                PathText="{Binding ManualPath, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                IsReadOnly="{Binding IsManualPathReadOnly}"/>
    ```
  - 修改 `ExtractSettingsWindow.xaml.cs`:
    - 移除对 `ManualPathTextBox` 的直接引用，改为使用 `ManualPathControl.PathText`
    - `BrowseButton_Click` → 由 QuickPathControl 内部处理
    - `ManualPathTextBox_TextChanged` → 改为 `ManualPathControl.PathText` 变化监听
    - 输出模式切换时（Here/Smart/ToName/Manual）更新 `ManualPathControl.IsReadOnly`
      - 非 Manual 模式：IsReadOnly=true，路径显示自动计算值
      - Manual 模式：IsReadOnly=false，用户可编辑
  - **历史记录**: 用户点击解压成功时，调用 `PathHistoryManager.Record(ManualPathControl.PathText)`
  - 保留 `x:Name="ManualPathTextBox"` 兼容引用或统一清理清理

  **Must NOT do**:
  - 不修改 ExtractSettingsWindow.xaml 的整体结构
  - 不改变解压逻辑

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 简单的 XAML 替换
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with T7, T9)
  - **Blocks**: T12
  - **Blocked By**: T4, T5

  **References**:
  - `src/MantisZip.UI/Dialogs/ExtractSettingsWindow.xaml:84-96` — 当前路径输入布局
  - `src/MantisZip.UI/Dialogs/ExtractSettingsWindow.xaml.cs` — 现有事件处理
  - `src/MantisZip.UI/Controls/QuickPathControl.xaml` + `.cs` — 依赖的控件（T4）
  - T7 的修改模式可作为参考（两个替换模式基本一致，T7 是文件模式，T8 是文件夹模式）

  **Acceptance Criteria**:
  - [ ] 解压对话框显示 QuickPathControl 替代原路径输入区域
  - [ ] 手动模式：文件夹模式（VistaFolderBrowserDialog），TextBox 可编辑
  - [ ] Here/Smart/ToName 模式：按钮禁用，路径计算/显示
  - [ ] 收藏/历史/资源管理器下拉正常工作
  - [ ] 浏览按钮打开 VistaFolderBrowserDialog
  - [ ] 解压成功后路径记入历史

  **QA Scenarios**:
  ```
  Scenario: 解压对话框 QuickPathControl 集成
    Tool: Bash（启动+截图）
    Preconditions: 通过 --extract 打开 ExtractSettingsWindow
    Steps:
      1. 选择手动模式
      2. 观察路径输入区域显示 QuickPathControl
      3. 点击 [⭐][🕐][🪟] 验证下拉菜单
      4. 点击 [浏览] 验证 VistaFolderBrowserDialog
    Expected Result: QuickPathControl 正常工作，文件夹模式
    Evidence: .sisyphus/evidence/task-8-extract-qpc.png

  Scenario: 非手动模式按钮禁用
    Tool: Bash
    Preconditions: ExtractSettingsWindow
    Steps:
      1. 选择「解压到此处」
    Expected Result: QuickPathControl 按钮禁用，路径显示自动计算的同名目录
    Evidence: .sisyphus/evidence/task-8-extract-readonly.png
  ```

  **Commit**: YES
  - Message: `feat(ui): integrate QuickPathControl into ExtractSettingsWindow`
  - Files: `src/MantisZip.UI/Dialogs/ExtractSettingsWindow.xaml`, `src/MantisZip.UI/Dialogs/ExtractSettingsWindow.xaml.cs`

---

- [ ] 9. App.xaml.cs + AddFolderButton — 替换 VistaFolderBrowserDialog 为 QuickPathDialog

  **What to do**:
  - **A. ResolveExtractDestinationStatic (App.xaml.cs:406-426)**:
    - 修改方法: 当 ExtractDestination == "ask" 时
    - 原行为: `new VistaFolderBrowserDialog().ShowDialog()`
    - 新行为:
      ```csharp
      internal static string? ResolveExtractDestinationStatic(string archivePath, AppSettings settings)
      {
          var destSetting = settings.ExtractDestination;
          if (destSetting == "same-dir") { ... }
          else if (destSetting == "desktop") { ... }

          // "ask" → 先弹 QuickPathDialog
          var dialog = new QuickPathDialog();
          dialog.Owner = Application.Current.MainWindow;
          dialog.Title = L.T(L.App_SelectExtractFolder);
          if (dialog.ShowDialog() == true)
              return dialog.SelectedPath;
          return null;  // 用户取消
      }
      ```
  - **B. AddFolderButton_Click (CompressSettingsWindow.xaml.cs)**:
    - 原行为: `new VistaFolderBrowserDialog().ShowDialog()` → 选中路径添加到 SourceListBox
    - 新行为: 弹出 QuickPathDialog（文件夹模式）→ 选中路径 `PathHistoryManager.Record(path)` → 添加到源列表
    - 路径选择后添加到源文件夹列表的逻辑不变

  **Must NOT do**:
  - 不改变 "same-dir" 和 "desktop" 模式的行为
  - 不改变 AddFolderButton 添加源文件夹的后续逻辑
  - 不修改 MainWindow 的提取路径（Extract_Click 使用 ResolveExtractDestination 间接调用，自动受益）

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 简单方法调用替换
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with T7, T8)
  - **Blocks**: T12
  - **Blocked By**: T4, T5

  **References**:
  - `src/MantisZip.UI/App.xaml.cs:406-426` — ResolveExtractDestinationStatic
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs` — AddFolderButton_Click
  - `src/MantisZip.UI/Dialogs/QuickPathDialog.xaml` + `.cs` — 依赖的弹窗（T5）

  **Acceptance Criteria**:
  - [ ] ExtractDestination="ask" 时弹出 QuickPathDialog 替代 VistaFolderBrowserDialog
  - [ ] QuickPathDialog 确认 → 返回选中路径
  - [ ] QuickPathDialog 取消 → 返回 null
  - [ ] AddFolderButton 弹出 QuickPathDialog
  - [ ] AddFolderButton 选中路径后添加到源列表
  - [ ] same-dir / desktop 模式不受影响

  **QA Scenarios**:
  ```
  Scenario: ResolveExtractDestinationStatic QuickPathDialog
    Tool: Bash
    Preconditions: AppSettings.ExtractDestination="ask"，打开资源管理器窗口
    Steps:
      1. MainWindow → 工具栏 → 解压
    Expected Result: 弹出 QuickPathDialog 而非 VistaFolderBrowserDialog
    Evidence: .sisyphus/evidence/task-9-extract-dialog.png

  Scenario: AddFolderButton 替换
    Tool: Bash（截图）
    Preconditions: 打开压缩对话框
    Steps:
      1. 点击「添加文件夹」
    Expected Result: QuickPathDialog 弹出
    Evidence: .sisyphus/evidence/task-9-addfolder-dialog.png
  ```

  **Commit**: YES
  - Message: `feat(core): replace VistaFolderBrowserDialog with QuickPathDialog`
  - Files: `src/MantisZip.UI/App.xaml.cs`, `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs`

---

- [ ] 10. 单元测试 — FavoritePathManager（含系统路径）+ PathHistoryManager

  **What to do**:
  - 在 `tests/MantisZip.Tests/FavoritePathManagerTests.cs` 创建测试
  - 测试 FavoritePathManager 用户收藏:
    - Add + GetAll 完整流程（验证返回列表包含系统路径 + 用户收藏）
    - Remove 单个和全部（仅用户收藏）
    - Update 更新名称和路径
    - Reorder 排序
    - Exists 检测
    - Load/Save 持久化（使用临时目录）
    - 文件不存在时初始化空状态
  - 在 `tests/MantisZip.Tests/FavoritePathManagerSystemPathsTests.cs` 创建系统路径专项测试:
    - `GetAll()` 默认返回 3 条系统路径（桌面/文档/下载）
    - `IsSystemPath(桌面路径)` → true
    - `IsSystemPath(用户自定义路径)` → false
    - `SetSystemPathHidden("Desktop", true)` → `GetAll()` 不含桌面
    - `SetSystemPathHidden("Desktop", false)` → `GetAll()` 恢复桌面
    - 隐藏状态持久化到 JSON → Load 后恢复
    - 多次隐藏/显示切换状态正确
    - 全部三个系统路径各自独立控制隐藏
  - 在 `tests/MantisZip.Tests/PathHistoryManagerTests.cs` 创建测试
  - 测试 PathHistoryManager:
    - Record 基本添加
    - Record 重复路径 → 去重移至顶部
    - 超过 50 条 → 只保留 50 条
    - Record 空字符串 → 忽略
    - Clear 清空
    - GetRecent 正确排序（最新在前）
  - 所有测试使用独立临时文件路径，不干扰生产数据
  - 使用 xUnit 的 `[Fact]`，不需要 Mock 框架

  **Must NOT do**:
  - 不添加 Moq/NSubstitute 依赖
  - 不修改现有测试文件
  - 不测试 COM 交互（留给 T11）

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 标准 xUnit 单元测试
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 4 (with T11)
  - **Blocks**: T12
  - **Blocked By**: T2, T3

  **References**:
  - `tests/MantisZip.Tests/Engines/ZipEngineTests.cs` — 现有测试模式参考
  - `src/MantisZip.Core/Utils/FavoritePathManager.cs` — 测试目标（T2）
  - `src/MantisZip.Core/Utils/PathHistoryManager.cs` — 测试目标（T3）

  **Acceptance Criteria**:
  - [ ] `dotnet test tests/MantisZip.Tests` 全部通过
  - [ ] FavoritePathManager 测试覆盖 CRUD + 持久化 + 系统路径合并
  - [ ] FavoritePathManagerSystemPathsTests 覆盖隐藏/显示 + 状态持久化
  - [ ] PathHistoryManager 测试覆盖去重 + FIFO + 排序
  - [ ] 至少 25 个测试用例（收藏基础 10 + 系统路径 8 + 历史 7）

  **QA Scenarios**:
  ```
  Scenario: 运行全部单元测试
    Tool: Bash (dotnet test)
    Preconditions: T2, T3 已完成，测试文件已创建
    Steps:
      1. dotnet test tests/MantisZip.Tests
    Expected Result: 全部测试通过（新增 + 原有）
    Evidence: .sisyphus/evidence/task-10-test-results.txt
  ```

  **Commit**: YES
  - Message: `test: add FavoritePathManager and PathHistoryManager unit tests`
  - Files: `tests/MantisZip.Tests/FavoritePathManagerTests.cs`, `tests/MantisZip.Tests/PathHistoryManagerTests.cs`

---

- [ ] 11. 集成测试 — ExplorerWindowTracker

  **What to do**:
  - 在 `tests/MantisZip.Tests/ExplorerWindowTrackerTests.cs` 创建测试
  - 测试 URL 路径解析（可单元测试的部分）:
    - `file:///C:/My%20Folder` → `C:\My Folder`
    - `file:///D:/Projects` → `D:\Projects`
    - `file:///E:/测试/文件` → `E:\测试\文件`
  - 测试特殊文件夹 `::{GUID}` 格式的处理（过滤或显示友好名称）
  - COM 调用部分通过真实环境验证（有窗口时返回列表，无窗口时空列表）
  - 如果 COM 调用失败（权限不足），验证返回空列表

  **Must NOT do**:
  - 不添加 Moq/NSubstitute 依赖
  - 不尝试 mock COM（真实调用即可）

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low` — 简单集成测试
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 4 (with T10)
  - **Blocks**: T12
  - **Blocked By**: T1

  **References**:
  - `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs` — 测试目标（T1）
  - `tests/MantisZip.Tests/FavoritePathManagerTests.cs` — 测试结构模式参考（T10）

  **Acceptance Criteria**:
  - [ ] URL 路径解析测试全部通过
  - [ ] 特殊文件夹处理正确
  - [ ] COM 不可用时优雅降级
  - [ ] `dotnet test` 全部通过

  **QA Scenarios**:
  ```
  Scenario: 运行全部测试
    Tool: Bash (dotnet test)
    Preconditions: T1 已完成
    Steps:
      1. dotnet test tests/MantisZip.Tests
    Expected Result: 全部测试通过
    Evidence: .sisyphus/evidence/task-11-test-results.txt
  ```

  **Commit**: YES
  - Message: `test: add ExplorerWindowTracker integration tests`
  - Files: `tests/MantisZip.Tests/ExplorerWindowTrackerTests.cs`

---

- [ ] 12. 端到端 QA 验证

  **What to do**:
  - 全面验证所有替换点正常工作
  - 验证清单:
    - [ ] CompressSettingsWindow: QuickPathControl 显示、各按钮正常、文件模式浏览、压缩成功
    - [ ] ExtractSettingsWindow: QuickPathControl 显示、文件夹模式浏览、解压成功
    - [ ] AddFolderButton: QuickPathDialog 弹出、选择路径后添加到源列表
    - [ ] ResolveExtractDestinationStatic: QuickPathDialog 弹出、确认/取消正确
    - [ ] FavoriteManagerWindow: 全部 CRUD 操作、持久化
    - [ ] 历史记录：压缩/解压成功后自动记录
    - [ ] 亮色/暗色主题切换：全部 UI 正常
    - [ ] 空状态：无收藏/无历史/无资源管理器窗口时按钮显示友好提示
    - [ ] 跨重启：favorites.json 持久化验证

  **Must NOT do**:
  - 不修改任何代码（仅验证）
  - 不引入新的测试

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high` — 全面验证
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 4 (sequential — 最后验证)
  - **Blocks**: FINAL
  - **Blocked By**: T7, T8, T9, T10, T11

  **References**:
  - 所有 T1-T11 的交付物
  - `.sisyphus/evidence/` 下所有之前任务的证据

  **Acceptance Criteria**:
  - [ ] 所有 QA 场景通过
  - [ ] 所有 UI 在亮/暗主题下正常
  - [ ] 构建通过，测试通过

  **QA Scenarios**:
  ```
  Scenario: 端到端压缩流程
    Tool: Bash（启动应用 + 截图）
    Preconditions: 已有收藏、历史记录、打开至少一个资源管理器窗口
    Steps:
      1. 启动 MantisZip
      2. 添加源文件 → 打开压缩对话框
      3. 验证 QuickPathControl 显示四个按钮
      4. 点击收藏 → 选择收藏路径 → 自动填入
      5. 切换到分卷模式 → 按钮禁用
      6. 切回手动模式 → 按钮恢复
      7. 点击浏览 → SaveFileDialog 弹出
      8. 选择路径 → 确认压缩
    Expected Result: 压缩成功，路径记入历史
    Evidence: .sisyphus/evidence/task-12-e2e-compress.png

  Scenario: 端到端解压流程
    Tool: Bash（截图）
    Preconditions: 同上
    Steps:
      1. 打开压缩包 → 选中文件 → 右键解压到
      2. QuickPathDialog 弹出
      3. 通过已打开窗口选择路径
      4. 确认解压
    Expected Result: 解压成功
    Evidence: .sisyphus/evidence/task-12-e2e-extract.png

  Scenario: 主题切换
    Tool: Bash（截图）
    Steps:
      1. 在亮色和暗色之间切换
      2. 检查 QuickPathControl、QuickPathDialog、FavoriteManagerWindow
    Expected Result: 两种主题下 UI 均正常
    Evidence: .sisyphus/evidence/task-12-theme-light.png + task-12-theme-dark.png

  Scenario: 收藏管理
    Tool: Bash（截图）
    Steps:
      1. 点击「管理收藏…」
      2. 增删改排序全部操作一次
      3. 关闭窗口 → 重新打开
    Expected Result: 数据持久化正确
    Evidence: .sisyphus/evidence/task-12-favmanager.png

  Scenario: 空状态
    Tool: Bash（截图）
    Preconditions: 清空收藏和历史，关闭所有资源管理器窗口
    Steps:
      1. 打开压缩对话框
      2. 分别点击三个按钮
    Expected Result: 显示友好提示文字
    Evidence: .sisyphus/evidence/task-12-empty-states.png
  ```

  **Commit**: YES
  - Message: `test: add end-to-end QA verification for QuickPathControl system`
  - Files: `.sisyphus/evidence/task-12-*`

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. Verify all Must Have implemented, Must NOT Have absent. Check evidence files exist.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Build check: `dotnet build src\MantisZip.UI\MantisZip.UI.csproj`. Tests: `dotnet test tests\MantisZip.Tests`. Check code quality (theme binding, localization, no NuGet, no AI slop).
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | Theme [PASS/FAIL] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high` (+ `playwright` skill if available)
  Execute EVERY QA scenario from EVERY task. Full end-to-end: start app → compress dialog → verify QuickPathControl → test each button → select path → verify filled → verify QuickPathDialog → test FavoriteManagerWindow.
  Output: `Scenarios [N/N pass] | Integration [PASS/FAIL] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff. Verify 1:1 — everything in spec was built, nothing beyond was built (no scope creep). Check "Must NOT do" compliance. Detect cross-task contamination.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | VERDICT`

---

## Commit Strategy

| Task | Message |
|------|---------|
| 1 | `feat(core): add ExplorerWindowTracker for enumerating open Explorer windows` |
| 2 | `feat(core): add FavoritePathManager with built-in system paths (Desktop/Documents/Downloads)` |
| 3 | `feat(core): add PathHistoryManager for recent path tracking` |
| 4 | `feat(ui): add QuickPathControl UserControl with dual-mode path input` |
| 5 | `feat(ui): add QuickPathDialog modal for standalone path selection` |
| 6 | `feat(ui): add FavoriteManagerWindow with system path hide/show support` |
| 7 | `feat(ui): integrate QuickPathControl into CompressSettingsWindow` |
| 8 | `feat(ui): integrate QuickPathControl into ExtractSettingsWindow` |
| 9 | `feat(core): replace VistaFolderBrowserDialog with QuickPathDialog` |
| 10 | `test: add FavoritePathManager (incl. system paths) and PathHistoryManager unit tests` |
| 11 | `test: add ExplorerWindowTracker integration tests` |

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
dotnet test tests\MantisZip.Tests
```

### Final Checklist
- [ ] QuickPathControl 显示 TextBox + 4 按钮，布局正确
- [ ] 收藏下拉菜单：系统路径（🔒）+ 用户收藏合并 + 底部「管理收藏…」
- [ ] 系统路径硬编码（桌面/文档/下载），运行时 Environment.SpecialFolder 获取
- [ ] 隐藏的系统路径不在下拉菜单出现
- [ ] 历史下拉菜单：最近 50 条，去重，最新在前
- [ ] 已打开下拉菜单：资源管理器窗口列表，当前窗口高亮
- [ ] 浏览按钮：文件模式打开 SaveFileDialog，文件夹模式打开 VistaFolderBrowserDialog
- [ ] 点击下拉项 → 自动填充到 TextBox
- [ ] QuickPathDialog 模态弹窗：确认返回路径，取消返回 null
- [ ] FavoriteManagerWindow：系统路径显示 🔒 + 隐藏/显示；用户收藏可增删改排序
- [ ] 系统路径隐藏状态持久化到 favorites.json
- [ ] 压缩对话框：QuickPathControl 工作正常
- [ ] 解压对话框：QuickPathControl 工作正常
- [ ] 所有 VistaFolderBrowserDialog 被 QuickPathDialog 替换
- [ ] 亮/暗主题均正常
- [ ] `dotnet build` 通过
- [ ] `dotnet test` 全部通过
