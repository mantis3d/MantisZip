# QuickPathControl + Custom Save Dialog 统一实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement plan task-by-task.

**Goal:** 统一 MantisZip 所有路径选择场景，用 QuickPathControl（⭐收藏/🕐历史/🪟资源管理器/📁浏览）替换全部 13 个路径选择对话框，并在 CompressSettingsWindow 中添加 DynamicFormatOptionsPanel（格式特有设置选项随格式动态切换）。

**Architecture:** QuickPathControl WPF UserControl（三种模式：文件夹/文件保存/文件打开）+ 数据管理器层（FavoritePathManager/PathHistoryManager/ExplorerWindowTracker）+ 嵌入所有现有对话框。DynamicFormatOptionsPanel 通过 ContentControl 固定槽位切换格式选项。

**Tech Stack:** .NET 9 WPF, SharpSevenZip (7z.dll), SharpCompress, System.Text.Json, Ookii.Dialogs.Wpf

---

## TL;DR

> **Quick Summary**: 整合原有的 QuickPathControl 计划和新的自定义保存对话框设计，统一所有路径选择交互。
>
> **Deliverables**:
> - Core 数据层：ExplorerWindowTracker, FavoritePathManager, PathHistoryManager
> - UI 组件：QuickPathControl（增强三种模式）, QuickPathDialog, FavoriteManagerWindow, DynamicFormatOptionsPanel
> - 集成：CompressSettingsWindow（QuickPathControl + 格式动态选项）, UnifiedExtractDialog, SettingsWindow, PasswordManagerWindow, ArchiveSaveAsDialog
> - AGENTS.md 修正（7z 加密预览已验证支持）
>
> **Estimated Effort**: Large
> **Parallel Execution**: YES — 6 waves
> **Critical Path**: T1 → T4 → T7/T8 → ... → Final Verification

---

## Context

### 原计划引用
- **`quick-path-control.md`** — QuickPathControl 基础组件、数据管理器、CompressSettingsWindow/ExtractSettingsWindow 嵌入
- **`custom-save-dialog-design.md`** — 新增文件打开模式、DynamicFormatOptionsPanel、UnifiedExtractDialog、ArchiveSaveAsDialog、SettingsWindow 7z.dll 替换

### 验证结果（已测试）
- **7z**: 所有压缩方法（LZMA2/LZMA/PPMd/BZip2/Deflate）+ 固实压缩 + AES-256 均支持单项预览
- **ZIP**: 所有压缩方法（Store/Deflate/BZip2/LZMA）+ 各级别 0-9 + ZipCrypto/AES-256 均支持单项预览
- **结论**: DynamicFormatOptionsPanel 不需要预览警告

### 范围调整
- T8（ExtractSettingsWindow 嵌入）**推迟**，留给未来的多压缩包设计
- 多文件选择场景（#5 #9 #10 的 OpenFileDialog 部分）保留标准对话框

---

## Work Objectives

### Core Objective
统一 MantisZip 所有路径选择场景为 QuickPathControl 组件，并增加格式动态选项。

### Concrete Deliverables
- `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs`
- `src/MantisZip.Core/Utils/FavoritePathManager.cs`
- `src/MantisZip.Core/Utils/PathHistoryManager.cs`
- `src/MantisZip.UI/Controls/QuickPathControl.xaml` + `.cs` （三种模式）
- `src/MantisZip.UI/Dialogs/QuickPathDialog.xaml` + `.cs`
- `src/MantisZip.UI/Dialogs/FavoriteManagerWindow.xaml` + `.cs`
- `src/MantisZip.UI/Controls/DynamicFormatOptionsPanel.xaml` + `.cs`
- `src/MantisZip.UI/Dialogs/UnifiedExtractDialog.xaml` + `.cs`
- `src/MantisZip.UI/Dialogs/ArchiveSaveAsDialog.xaml` + `.cs`
- 修改 CompressSettingsWindow / SettingsWindow / PasswordManagerWindow / App.xaml.cs / MainWindow.Menu.cs

### Must Have
- QuickPathControl 三种模式：文件夹 / 文件保存 / 文件打开
- 收藏管理器（含系统路径桌面/文档/下载，🔒标识，可隐藏不可删除）
- 历史记录 50 条，去重移至顶部
- 资源管理器窗口枚举（COM late binding，try/catch 降级）
- FavoriteManagerWindow 管理收藏 + 隐藏/显示系统路径
- DynamicFormatOptionsPanel 随格式切换动态选项（固定槽位不跳动）
- UnifiedExtractDialog 嵌入 QuickPathControl + 解压选项，替换 MainWindow 提取路径
- SettingsWindow 7z.dll 路径替换为 QuickPathControl（文件打开模式）
- PasswordManagerWindow 导出路径替换为 QuickPathControl（文件保存模式）
- 所有新控件绑定主题资源键

### Must NOT Have
- 不添加 NuGet 包依赖
- 不修改 MainWindow 主窗口结构
- 不监控/轮询资源管理器窗口变化（仅快照）
- 不替换多文件选择的 OpenFileDialog
- 不替换 ExtractSettingsWindow（留给未来设计）
- 系统路径不支持删除/编辑（仅隐藏/显示）

---

## Verification Strategy

### Test Decision
- **Infrastructure exists**: YES (xUnit)
- **Automated tests**: TDD for data managers
- **Agent-Executed QA**: 所有 UI 任务均包含

### QA Policy
- **Core 逻辑**: `dotnet test` 单元测试
- **COM 封装**: 集成测试 + 手动运行验证
- **UI 控件**: 构建 + 启动应用验证

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Core 数据层 — MAX PARALLEL):
├── T1: ExplorerWindowTracker — COM 枚举资源管理器窗口
├── T2: FavoritePathManager — favorites.json 读写 + 系统路径
└── T3: PathHistoryManager — 历史记录 50 条去重

Wave 2 (UI 组件 — 并行构建):
├── T4: QuickPathControl — WPF UserControl（三种模式）
├── T5: QuickPathDialog — 模态弹窗包装 QuickPathControl
├── T6: FavoriteManagerWindow — 收藏管理窗口
└── T7: DynamicFormatOptionsPanel — 格式动态选项面板

Wave 3 (集成 A — CompressSettingsWindow + UnifiedExtractDialog):
├── T8: CompressSettingsWindow — QuickPathControl 嵌入 + 格式选项
├── T9: UnifiedExtractDialog — 统一提取对话框（替换 MainWindow 提取路径）
└── T10: SettingsWindow 7z.dll — 替换为 QuickPathControl

Wave 4 (集成 B — 其他路径场景):
├── T11: PasswordManagerWindow — 导出路径替换
├── T12: App.xaml.cs 启动 — 7z.dll 选择替换为 QuickPathDialog
└── T13: MainWindow Compress — 压缩路径替换

Wave 5 (高级功能):
└── T14: ArchiveSaveAsDialog — 压缩包另存为格式转换

Wave 6 (测试 — 全部后端验证):
├── T15: FavoritePathManager + PathHistoryManager 单元测试
├── T16: ExplorerWindowTracker 集成测试
└── T17: 端到端 QA

Wave FINAL (验证):
├── F1: 计划合规审计
├── F2: 代码质量 + 构建检查
├── F3: 真实 QA（执行所有任务场景）
└── F4: 范围一致性检查
```

### Dependency Matrix
- T1: — T4, T5, T16
- T2: — T4, T5, T6, T15
- T3: — T4, T5, T15
- T4: T1, T2, T3 — T5, T8, T9, T10, T11, T12, T13, T14
- T5: T4 — T9, T12
- T6: T2 — (独立)
- T7: — T8, T14
- T8: T4, T7 — T17
- T9: T4 — T17
- T10: T4 — T17
- T11: T4 — T17
- T12: T5 — T17
- T13: T4 — T17
- T14: T4, T7 — T17
- T15: T2, T3 — T17
- T16: T1 — T17
- T17: T8-T16 — FINAL

---

## TODOs

- [ ] 1. ExplorerWindowTracker — COM 封装枚举资源管理器窗口

  **What to do**:
  - 在 `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs` 创建静态类
  - 使用 `Type.GetTypeFromProgID("Shell.Application")` + `dynamic` 枚举所有资源管理器窗口
  - 解析 `LocationURL`：`file:///C:/My%20Folder` → `C:\My Folder`；`::{GUID}` 特殊文件夹过滤
  - 通过 `GetForegroundWindow` + 匹配 HWND 获取当前前台窗口
  - 所有 COM 调用包裹在 try/catch（COMException, SecurityException, UnauthorizedAccessException）中
  - API:
    ```csharp
    public record ExplorerWindowInfo(string Path, string DisplayName, IntPtr HWND, bool IsActive);
    public static class ExplorerWindowTracker {
        public static List<ExplorerWindowInfo> GetOpenExplorerWindows();
        public static string? GetActiveExplorerPath();
    }
    ```

  **Must NOT do**: 不添加 SHDocVw.dll COM 引用；不监控/轮询窗口变化；不处理非 explorer.exe 窗口

  **Parallelization**:
  - Wave 1, with T2, T3
  - Blocks: T4, T5, T16
  - Blocked By: None

  **References**: `.sisyphus/plans/archived/explorer-path-switcher.md` — COM 调用模式

  **QA Scenarios**:
  ```
  Scenario: 正常枚举资源管理器窗口
    Tool: Bash（编写测试程序）
    Steps: 打开至少一个资源管理器窗口（如 D:\），调用 GetOpenExplorerWindows()
    Expected: 返回非空列表，路径正确
    Evidence: .sisyphus/evidence/task-1-explorer-list.txt

  Scenario: COM 不可用时优雅降级
    Steps: 检查 COM 调用是否包裹在 try/catch 中
    Expected: COMException/SecurityException 时返回空列表
  ```

  **Commit**: `feat(core): add ExplorerWindowTracker for enumerating open Explorer windows`
  Files: `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs`

---

- [ ] 2. FavoritePathManager — favorites.json 读写管理 + 系统路径

  **What to do**:
  - 在 `src/MantisZip.Core/Utils/FavoritePathManager.cs` 创建静态类
  - 数据模型:
    ```csharp
    public record FavoritePathItem(string Name, string Path, DateTime AddedAt, bool IsSystem, string? SystemKey);
    ```
  - JSON 存储: `%LOCALAPPDATA%\MantisZip\favorites.json`
  - 系统路径（硬编码）: 桌面(SpecialFolder.Desktop)、文档(MyDocuments)、下载(UserProfile\Downloads)
  - 隐藏状态持久化到 JSON（存 SpecialFolder 枚举名）
  - API:
    ```csharp
    public static class FavoritePathManager {
        public static List<FavoritePathItem> GetAll();
        public static List<FavoritePathItem> GetSystemPaths();
        public static List<FavoritePathItem> GetUserFavorites();
        public static void Add(string name, string path);
        public static void Remove(string path);
        public static void Update(string oldPath, string newName, string newPath);
        public static void Reorder(int oldIndex, int newIndex);
        public static bool Exists(string path);
        public static bool IsSystemPath(string path);
        public static void SetSystemPathHidden(string key, bool hidden);
        public static bool IsSystemPathHidden(string key);
        public static void Save(); public static void Load();
    }
    ```

  **Must NOT do**: 不存完整系统路径到 JSON（仅存枚举名）；系统路径不允许删除/编辑

  **Parallelization**: Wave 1, with T1, T3. Blocks: T4, T5, T6, T15

  **References**: `src/MantisZip.Core/Utils/PasswordManager.cs` — JSON 持久化模式

  **QA Scenarios**:
  ```
  Scenario: 收藏夹 CRUD
    Steps: Add/Update/Remove/Reorder → GetAll()
    Expected: 所有操作正确，JSON 持久化
  Scenario: 系统路径隐藏/显示
    Steps: SetSystemPathHidden("Desktop",true/false) → GetAll()
    Expected: 隐藏后不出现，显示后恢复，重启后保持
  ```

  **Commit**: `feat(core): add FavoritePathManager for favorites persistence`
  Files: `src/MantisZip.Core/Utils/FavoritePathManager.cs`

---

- [ ] 3. PathHistoryManager — 历史记录自动追踪

  **What to do**:
  - 在 `src/MantisZip.Core/Utils/PathHistoryManager.cs` 创建静态类
  - 50 条上限，去重：相同路径移至顶部
  - API:
    ```csharp
    public record PathHistoryEntry(string Path, DateTime LastUsedAt);
    public static class PathHistoryManager {
        public static List<PathHistoryEntry> GetRecent(int maxCount = 50);
        public static void Record(string path);
        public static void Clear();
        public static void Save(); public static void Load();
    }
    ```

  **Must NOT do**: 不自动监听 TextBox 变化；不记录空路径

  **Parallelization**: Wave 1, with T1, T2. Blocks: T4, T5, T15

  **QA Scenarios**:
  ```
  Scenario: 历史记录功能
    Steps: Record("D:\A"), Record("D:\B"), Record("D:\C") → GetRecent()
           Record("D:\A") → 移到顶部
           添加 52 条 → 只保留 50 条
    Expected: 去重正确，排序正确，数量限制正确
  ```

  **Commit**: `feat(core): add PathHistoryManager for recent path tracking`
  Files: `src/MantisZip.Core/Utils/PathHistoryManager.cs`

---

- [ ] 4. QuickPathControl — WPF UserControl（TextBox + 4 按钮，三种模式）

  **What to do**:
  - 在 `src/MantisZip.UI/Controls/QuickPathControl.xaml` + `.cs` 创建 UserControl
  - **三种模式**（通过依赖属性切换）:
    - 文件夹模式（`IsFolderMode=true`）: 选目录，浏览→VistaFolderBrowserDialog
    - 文件保存模式（`IsFolderMode=false, IsFileOpenMode=false`）: 选目录+文件名，浏览→SaveFileDialog
    - 文件打开模式（`IsFileOpenMode=true`）: 🆕 选已存在文件，浏览→OpenFileDialog（单文件）
  - **依赖属性**:
    - `PathText` (string): 双向绑定路径
    - `FileName` (string): 文件模式下的文件名
    - `IsFolderMode` (bool): true=文件夹模式
    - `IsFileOpenMode` (bool): true=文件打开模式 🆕
    - `FileTypeFilter` (string): 文件模式的对话框过滤条件
    - `FileOpenFilter` (string): 🆕 文件打开模式的 OpenFileDialog 筛选
    - `DefaultFileName` (string): 文件保存模式的默认文件名
    - `IsReadOnly` (bool): 禁用所有交互
  - **XAML 布局**: TextBox + [⭐] [🕐] [🪟] [📁] 四个按钮横排
  - **下拉菜单**:
    - ⭐: FavoritePathManager.GetAll()（系统路径🔒 + 用户收藏 + 底部「管理收藏…」）
    - 🕐: PathHistoryManager.GetRecent(50)
    - 🪟: ExplorerWindowTracker.GetOpenExplorerWindows()（当前窗口高亮）
    - 📁: 根据模式打开对应系统对话框
  - **空状态**: 无收藏→「暂无收藏」；无历史→「暂无历史记录」；无窗口→「没有打开的文件夹」
  - **文件打开模式特殊处理**: Browse→OpenFileDialog(CheckFileExists=true, Multiselect=false)；PathText=完整文件路径；FileName 只读显示

  **Must NOT do**: 不使用外部图标文件；不添加 WPF 控件库依赖；不修改全局主题资源

  **Parallelization**: Wave 2, with T5, T6, T7. Blocks: T8-T14. Blocked By: T1, T2, T3

  **References**: `CompressSettingsWindow.xaml:84-91` 当前路径布局；`ExtractSettingsWindow.xaml:84-96` 提取路径布局

  **QA Scenarios**:
  ```
  Scenario: 控件三种模式显示
    Steps: 构建并启动，观察文件夹/文件保存/文件打开模式下布局
    Expected: TextBox + 4 按钮正确渲染，Browse 按钮行为随模式变化

  Scenario: 收藏/历史/资源管理器下拉
    Steps: 点击 ⭐ 🕐 🪟 分别验证下拉
    Expected: 数据正确，空状态友好提示

  Scenario: 文件打开模式
    Steps: IsFileOpenMode=true，点击浏览
    Expected: OpenFileDialog 弹出，选择后 PathText=完整文件路径

  Scenario: 选择路径自动填入
    Steps: 从收藏/历史/窗口选择一个路径
    Expected: PathTextBox 自动填充
  ```

  **Commit**: `feat(ui): add QuickPathControl with three modes (folder/save/open)`
  Files: `src/MantisZip.UI/Controls/QuickPathControl.xaml`, `.cs`

---

- [ ] 5. QuickPathDialog — 模态弹窗包装 QuickPathControl

  **What to do**:
  - 在 `src/MantisZip.UI/Dialogs/QuickPathDialog.xaml` + `.cs` 创建 Window
  - 内嵌 QuickPathControl，底部确认/取消按钮
  - 用于独立弹窗场景：AddFolderButton、打开压缩包、启动时 7z.dll 选择
  - 属性: `SelectedPath` (string?), `DialogResult` (bool)
  - 回车=确认，Esc=取消

  **Must NOT do**: 不添加路径验证逻辑（如目录是否存在）

  **Parallelization**: Wave 2, with T4, T6, T7. Blocks: T9, T12. Blocked By: T4

  **References**: `PasswordDialog.xaml` 现有模态弹窗模式

  **QA Scenarios**:
  ```
  Scenario: QuickPathDialog 交互
    Steps: 通过 AddFolderButton 触发，选择路径→确认/取消
    Expected: 路径回传，关闭弹窗
  ```

  **Commit**: `feat(ui): add QuickPathDialog modal for standalone path selection`
  Files: `src/MantisZip.UI/Dialogs/QuickPathDialog.xaml`, `.cs`

---

- [ ] 6. FavoriteManagerWindow — 收藏管理窗口

  **What to do**:
  - 在 `src/MantisZip.UI/Dialogs/FavoriteManagerWindow.xaml` + `.cs` 创建 Window
  - ListView 混排：系统路径（🔒+「系统」标签）+ 用户收藏
  - 系统路径: 「隐藏」/「显示」按钮（不可删除/编辑名称）
  - 用户收藏: 添加/编辑/删除/排序（上移/下移）
  - 底部「添加收藏」按钮，点击弹出输入框（名称+路径）
  - 多入口: QuickPathControl ⭐下拉「管理收藏…」+ 主窗口工具菜单
  - 每次修改后自动调用 FavoritePathManager.Save()
  - 主菜单入口: MainWindow.xaml 工具菜单中添加 "FavoriteManager_Click"

  **Must NOT do**: 不添加拖拽排序；系统路径不可编辑/删除

  **Parallelization**: Wave 2, with T4, T5, T7. Blocked By: T2

  **QA Scenarios**:
  ```
  Scenario: 收藏管理完整生命周期
    Steps: 隐藏桌面→添加收藏→排序→删除→重新显示桌面
    Expected: 每一步操作后列表和数据正确同步

  Scenario: 系统路径不可删除
    Steps: 选中系统路径行
    Expected: 无删除按钮，只有隐藏/显示
  ```

  **Commit**: `feat(ui): add FavoriteManagerWindow for favorites management`
  Files: `src/MantisZip.UI/Dialogs/FavoriteManagerWindow.xaml`, `.cs` + MainWindow 菜单修改

---

- [ ] 7. DynamicFormatOptionsPanel — 格式动态选项面板 🆕

  **What to do**:
  - 在 `src/MantisZip.UI/Controls/DynamicFormatOptionsPanel.xaml` + `.cs` 创建 UserControl
  - 使用 `ContentControl` + `MinHeight="100"` 固定槽位（防止 UI 跳动）
  - 格式切换时通过代码切换显示不同内容（ZIP 面板 / 7z 面板 / TAR.GZ 占位）
  - 从 `AppSettings` 加载默认值
  - 格式选项:
    - ZIP: 文件名编码 ComboBox（UTF-8 / GBK / 保留原始），默认 UTF-8
    - 7z: 压缩方法 ComboBox（LZMA / LZMA2 / PPMd / BZip2 / Deflate），默认 LZMA2；固实压缩 CheckBox，默认 true
    - TAR.GZ: 占位提示文字"TAR.GZ 无额外格式选项"
  - 依赖属性:
    - `SelectedFormat` (string): "zip" / "7z" / "tar.gz" — 切换时自动更新显示
  - 输出属性:
    - `FileNameEncoding` (string?): ZIP 模式下可选编码
    - `SevenZipCompressionMethod` (string?): 7z 压缩方法
    - `SevenZipSolid` (bool): 7z 固实压缩

  **Must NOT do**: 不包含加密选项（由现有的加密 Tab 处理）

  **Parallelization**: Wave 2, with T4, T5, T6. Blocks: T8, T14. Blocked By: None

  **References**: `CompressSettingsWindow.xaml` — 嵌入位置；`AppSettings.cs` — 默认值

  **QA Scenarios**:
  ```
  Scenario: 格式切换不跳动
    Steps: 快速切换 ZIP→7z→TAR.GZ→ZIP
    Expected: 面板内容切换但整体高度不变（MinHeight=100 固定）

  Scenario: 7z 选项显示
    Steps: 格式选 7z
    Expected: 显示压缩方法 ComboBox + 固实压缩 CheckBox

  Scenario: ZIP 选项显示
    Steps: 格式选 ZIP
    Expected: 显示文件名编码 ComboBox

  Scenario: TAR.GZ 占位
    Steps: 格式选 TAR.GZ
    Expected: 显示"TAR.GZ 无额外格式选项"占位文字
  ```

  **Commit**: `feat(ui): add DynamicFormatOptionsPanel for format-specific settings`
  Files: `src/MantisZip.UI/Controls/DynamicFormatOptionsPanel.xaml`, `.cs`

---

- [ ] 8. CompressSettingsWindow — QuickPathControl 嵌入 + DynamicFormatOptionsPanel

  **What to do**:
  - 替换压缩对话框的路径输入区域为 QuickPathControl + 文件名行（双行布局）
  - 第一行: QuickPathControl（文件保存模式，含 ⭐🕐🪟📁）
  - 第二行: 文件名 TextBox + 扩展名标签
  - 嵌入 DynamicFormatOptionsPanel 到 FormatComboBox 下方
  - FormatComboBox_SelectionChanged → 自动更新扩展名 + 同步到 QuickPathControl.FileName
  - 手动模式: QuickPathControl 可用；非手动模式: 禁用
  - 组合完整路径: `Path.Combine(PathText, FileName)`
  - 压缩成功后调用 `PathHistoryManager.Record(fullPath)`
  - 新增本地化字符串: `Compress_OutputDir`, `Compress_FileName`

  **Must NOT do**: 不修改 CompressSettingsWindow 整体结构；不改变压缩逻辑

  **Parallelization**: Wave 3, with T9, T10. Blocked By: T4, T7

  **References**: `CompressSettingsWindow.xaml` — 当前布局；`quick-path-control.md Task 7`

  **QA Scenarios**:
  ```
  Scenario: 手动模式完整保存流程
    Steps: 从收藏选目录→输入文件名→选格式→看扩展名变化→压缩
    Expected: 路径正确，格式选项动态变化，压缩成功记入历史

  Scenario: 分卷模式禁用
    Steps: 切换到分卷输出
    Expected: QuickPathControl 禁用，文件名禁用

  Scenario: 格式切换扩展名更新
    Steps: 文件名 "backup"，格式从 ZIP 切到 7z
    Expected: 显示 "backup.7z"；切到 TAR.GZ 显示 "backup.tar.gz"
  ```

  **Commit**: `feat(ui): integrate QuickPathControl + format options into CompressSettingsWindow`
  Files: `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml`, `.cs`

---

- [ ] 9. UnifiedExtractDialog — 统一解压提取对话框 🆕

  **What to do**:
  - 在 `src/MantisZip.UI/Dialogs/UnifiedExtractDialog.xaml` + `.cs` 创建 Window
  - 内嵌 QuickPathControl（文件夹模式）+ 解压选项
  - 解压选项: 文件冲突 ComboBox（覆盖/跳过/重命名/覆盖旧文件/覆盖小文件）+ 保留目录结构 CheckBox
  - 取代 MainWindow 提取路径场景:
    - #2 解压到…… → UnifiedExtractDialog
    - #3 解压到此处 → 直接使用当前目录（不弹窗）
    - #4 解压到 {name} → 自动填充 archiveName 到 QuickPathControl
    - #6 解压到选中的文件夹 → 预设选中文件夹路径
  - 属性: `SelectedPath` (string), `ConflictAction` (ConflictAction), `PreserveDirectoryRoot` (bool)
  - 压缩成功后调用 `PathHistoryManager.Record(SelectedPath)`
  - 新增本地化字符串: `Extract_Destination`, `Extract_Options`

  **Must NOT do**: 不替换 ExtractSettingsWindow（留给未来多压缩包设计）

  **Parallelization**: Wave 3, with T8, T10. Blocked By: T4

  **References**: `MainWindow/MainWindow.Menu.cs` — 提取菜单处理；`ExtractSettingsWindow.xaml` — 现有解压选项布局

  **QA Scenarios**:
  ```
  Scenario: 提取到指定目录
    Steps: 右键压缩包→解压到……→QuickPathControl 选目录→确认
    Expected: 文件提取到所选目录

  Scenario: 提取到此处
    Steps: 右键→解压到此处
    Expected: 直接提取到压缩包所在目录，不弹窗

  Scenario: 提取到 {name}
    Steps: 右键→解压到 {name}
    Expected: QuickPathControl 自动填充为 \archiveName\，可修改
  ```

  **Commit**: `feat(ui): add UnifiedExtractDialog with QuickPathControl`
  Files: `src/MantisZip.UI/Dialogs/UnifiedExtractDialog.xaml`, `.cs` + `src/MantisZip.UI/MainWindow/MainWindow.Menu.cs`

---

- [ ] 10. SettingsWindow 7z.dll 路径 — 替换为 QuickPathControl 🆕

  **What to do**:
  - 替换 Settings 窗口中 7z.dll 路径行（TextBox + BrowseButton）为 QuickPathControl
  - QuickPathControl 模式: `IsFileOpenMode=true`, `FileOpenFilter="7z DLL files|7z.dll"`, `IsReadOnly=false`
  - 绑定到 `AppSettings.SevenZipPath`
  - 当路径变化时调用 `SevenZipBase.SetLibraryPath(newPath)`
  - 路径验证: 文件存在 + 是有效 7z.dll（通过 `SevenZipBase.GetLibraryVersion()` 检查）

  **Must NOT do**: 不修改 Settings 窗口整体布局

  **Parallelization**: Wave 3, with T8, T9. Blocked By: T4

  **References**: `Dialogs/SettingsWindow.xaml` — 7z.dll 路径行

  **QA Scenarios**:
  ```
  Scenario: 7z.dll 路径选择
    Steps: 打开设置→点击 QuickPathControl 浏览→选择有效 7z.dll
    Expected: 路径正确填充，点击应用后生效

  Scenario: 无效路径验证
    Steps: 输入错误路径或非 7z.dll 文件
    Expected: 路径验证失败提示
  ```

  **Commit**: `feat(ui): replace 7z.dll path in SettingsWindow with QuickPathControl`
  Files: `src/MantisZip.UI/Dialogs/SettingsWindow.xaml`, `.cs`

---

- [ ] 11. PasswordManagerWindow 导出路径 — 替换为 QuickPathControl 🆕

  **What to do**:
  - 替换密码管理窗口的导出路径行为 QuickPathControl（文件保存模式）
  - QuickPathControl 模式: `IsFileOpenMode=false`（保存模式）, `FileTypeFilter="JSON files|*.json"`
  - 原有导出逻辑: 点击导出→弹出 SaveFileDialog → 选择路径→保存
  - 改为: QuickPathControl 显示在窗口底部固定位置；点击浏览→SaveFileDialog 选择→路径填入 QuickPathControl

  **Must NOT do**: 不修改 PasswordManagerWindow 整体布局

  **Parallelization**: Wave 4, with T12, T13. Blocked By: T4

  **References**: `Dialogs/PasswordManagerWindow.xaml` — 导出按钮逻辑

  **QA Scenarios**:
  ```
  Scenario: 密码导出路径选择
    Steps: 打开密码管理→点击 QuickPathControl 浏览→选择导出路径和文件名
    Expected: 路径正确填入，导出文件到该路径
  ```

  **Commit**: `feat(ui): replace export path in PasswordManagerWindow with QuickPathControl`
  Files: `src/MantisZip.UI/Dialogs/PasswordManagerWindow.xaml`, `.cs`

---

- [ ] 12. App.xaml.cs 启动 — 7z.dll 选择替换为 QuickPathDialog 🆕

  **What to do**:
  - App.xaml.cs 中：当 `SevenZipPath` 为空或 7z.dll 不存在时，弹出 QuickPathDialog 选择
  - QuickPathDialog 配置: 文件打开模式，`FileOpenFilter="7z DLL files|7z.dll"`
  - 替换现有代码：`ShowOpenFileDialog()` + `while (!File.Exists(fileName))` 循环逻辑
  - 流程: QuickPathDialog.ShowDialog() → 取消→继续提示→确定了退出；路径无效→提示继续
  - 选择完成后调用 `SevenZipBase.SetLibraryPath(path)`

  **Must NOT do**: 不修改启动流程结构（仅替换对话框）

  **Parallelization**: Wave 4, with T11, T13. Blocked By: T5

  **References**: `App.xaml.cs` — 7z.dll 选择逻辑

  **QA Scenarios**:
  ```
  Scenario: 启动选择 7z.dll
    Steps: 删除 7z.dll 路径设置→启动应用
    Expected: 弹出 QuickPathDialog，选择后正常启动

  Scenario: 取消选择
    Steps: 启动后 QuickPathDialog 点击取消
    Expected: 应用弹出继续提示，确定后退出
  ```

  **Commit**: `feat(ui): replace 7z.dll startup selection with QuickPathDialog`
  Files: `src/MantisZip.UI/App.xaml.cs`

---

- [ ] 13. MainWindow Compress 路径 — 替换压缩路径选择 🆕

  **What to do**:
  - #7 压缩到此处 → MainWindow 选中文件后通过压缩菜单 → UnifiedExtractDialog 类似的双行布局
  - 在 MainWindow/MainWindow.Menu.cs 中：当显示压缩路径选择时，使用 QuickPathDialog 替代标准 FolderBrowserDialog
  - 对 #7「压缩到此处」场景: 直接使用当前目录（同解压到此处），不弹窗
  - 对需要用户选择路径的场景: QuickPathDialog 打开，路径回传

  **Must NOT do**: 不修改 MainWindow 整体布局；只有路径选择部分替换

  **Parallelization**: Wave 4, with T11, T12. Blocked By: T4

  **References**: `MainWindow/MainWindow.Menu.cs` — 压缩菜单处理

  **QA Scenarios**:
  ```
  Scenario: 压缩到指定路径
    Steps: 选中文件→压缩→选择路径→QuickPathControl 选目录
    Expected: 路径正确，压缩到该目录
  ```

  **Commit**: `feat(ui): replace compress path selection in MainWindow with QuickPathControl`
  Files: `src/MantisZip.UI/MainWindow/MainWindow.Menu.cs`

---

- [ ] 14. ArchiveSaveAsDialog — 压缩包另存为格式转换 🆕

  **What to do**:
  - 在 `src/MantisZip.UI/Dialogs/ArchiveSaveAsDialog.xaml` + `.cs` 创建 Window
  - 用于 MainWindow 编辑菜单「另存为」或格式转换
  - 布局: QuickPathControl（文件保存模式）+ DynamicFormatOptionsPanel + 密码选项
  - 输入: 当前压缩包路径，文件名预填原压缩包名
  - 输出: 目标路径 (string) + 目标格式 (string) + 选项
  - 格式转换提示: 转换后格式支持可能不同（RAR 不能压缩等），用浅色文字标注
  - 格式缓存: 如果当前是 ZIP，另存为默认 ZIP；可切换

  **Must NOT do**: 不包含压缩逻辑（仅负责路径+格式选择）

  **Parallelization**: Wave 5, alone. Blocked By: T4, T7

  **References**: `CompressSettingsWindow.xaml` — 参考嵌入模式

  **QA Scenarios**:
  ```
  Scenario: 另存为格式转换
    Steps: 打开压缩包→另存为→QuickPathControl 选路径+文件名
    Expected: 路径正确回传，格式选项动态切换

  Scenario: 格式切换影响扩展名
    Steps: 选 7z→文件名变为 {name}.7z；切到 ZIP→{name}.zip
    Expected: 扩展名随格式同步更新
  ```

  **Commit**: `feat(ui): add ArchiveSaveAsDialog with QuickPathControl + format options`
  Files: `src/MantisZip.UI/Dialogs/ArchiveSaveAsDialog.xaml`, `.cs`

---

- [ ] 15. FavoritePathManager + PathHistoryManager 单元测试

  **What to do**:
  - 创建 `tests/MantisZip.Tests/Managers/FavoritePathManagerTests.cs`
  - 测试用例: Add/Remove/Update/Reorder, 系统路径隐藏/显示, JSON 序列化/反序列化, 持久化验证
  - 创建 `tests/MantisZip.Tests/Managers/PathHistoryManagerTests.cs`
  - 测试用例: Record/GetRecent, 去重, 50 条限制, Clear
  - 使用临时目录存储 JSON 文件避免污染用户数据

  **Parallelization**: Wave 6, with T16, T17. Blocked By: T2, T3

  **QA Scenarios**:
  ```
  Scenario: 所有测试通过
    Steps: dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
    Expected: PASS (所有测试)
    Evidence: .sisyphus/evidence/task-15-test-pass.txt
  ```

  **Commit**: `test: add unit tests for FavoritePathManager and PathHistoryManager`
  Files: `tests/MantisZip.Tests/Managers/FavoritePathManagerTests.cs`, `PathHistoryManagerTests.cs`

---

- [ ] 16. ExplorerWindowTracker 集成测试

  **What to do**:
  - 创建 `tests/MantisZip.Tests/Managers/ExplorerWindowTrackerTests.cs`
  - 测试: 枚举当前已打开的窗口（至少确保返回对象结构完整）
  - COM 降级测试: 模拟 COMException 验证返回空列表

  **Parallelization**: Wave 6, with T15, T17. Blocked By: T1

  **QA Scenarios**:
  ```
  Scenario: 集成测试通过
    Steps: dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
    Expected: PASS
  ```

  **Commit**: `test: add integration tests for ExplorerWindowTracker`
  Files: `tests/MantisZip.Tests/Managers/ExplorerWindowTrackerTests.cs`

---

- [ ] 17. 端到端集成 QA — 启动应用验证所有替换场景

  **What to do**:
  - 构建完整应用 (`dotnet build src/MantisZip.UI/MantisZip.UI.csproj`)
  - 启动应用验证所有路径选择场景:
    - CompressSettingsWindow: 收藏/历史/资源管理器下拉正常，格式选项切换
    - UnifiedExtractDialog: 路径选择正常
    - SettingsWindow: 7z.dll 路径 QuickPathControl 打开
    - PasswordManagerWindow: 导出路径 QuickPathControl 保存
    - QuickPathDialog: AddFolderButton + OpenArchive 弹窗
  - 验证 AGENTS.md 修正内容

  **Parallelization**: Wave 6, with T15, T16. Blocked By: T8-T14

  **QA Scenarios**:
  ```
  Scenario: 构建成功
    Steps: dotnet build src/MantisZip.UI/MantisZip.UI.csproj
    Expected: 构建成功 0 errors

  Scenario: 应用启动正常
    Steps: 启动应用
    Expected: 主窗口正常显示，所有 QuickPathControl 渲染正确
  ```

  **Commit**: `fix(docs): correct 7z encrypted preview claim in AGENTS.md`
  Files: `AGENTS.md` (7z 加密预览修正)

---

## Final Verification Wave

- [ ] F1. **计划合规审计** — `oracle`
  Read plan end-to-end. For each Must Have: verify implementation exists. For each Must NOT Have: search codebase for forbidden patterns. Check evidence files in `.sisyphus/evidence/`.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **代码质量 + 构建检查** — `unspecified-high`
  Run `dotnet build` + review for: `as any`/`@ts-ignore`(not applicable), empty catches, commented-out code, unused imports. Check AI slop: excessive comments, over-abstraction.
  Output: `Build [PASS/FAIL] | Files [N clean/N issues] | VERDICT`

- [ ] F3. **真实 QA** — `unspecified-high`
  Start from clean state. Execute EVERY QA scenario from EVERY task. Test cross-task integration: QuickPathControl dropdowns → UnifiedExtractDialog → CompressSettingsWindow format options.
  Output: `Scenarios [N/N pass] | Integration [N/N] | VERDICT`

- [ ] F4. **范围一致性检查** — `deep`
  Verify 1:1 — everything in spec was built (no missing), nothing beyond spec was built (no creep). Check "Must NOT do" compliance.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | VERDICT`

---

## Commit Strategy

| Task | Message | Files |
|------|---------|-------|
| 1 | `feat(core): add ExplorerWindowTracker for enumerating open Explorer windows` | `src/MantisZip.Core/Utils/ExplorerWindowTracker.cs` |
| 2 | `feat(core): add FavoritePathManager for favorites persistence` | `src/MantisZip.Core/Utils/FavoritePathManager.cs` |
| 3 | `feat(core): add PathHistoryManager for recent path tracking` | `src/MantisZip.Core/Utils/PathHistoryManager.cs` |
| 4 | `feat(ui): add QuickPathControl with three modes (folder/save/open)` | `src/MantisZip.UI/Controls/QuickPathControl.xaml`, `.cs` |
| 5 | `feat(ui): add QuickPathDialog modal for standalone path selection` | `src/MantisZip.UI/Dialogs/QuickPathDialog.xaml`, `.cs` |
| 6 | `feat(ui): add FavoriteManagerWindow for favorites management` | `src/MantisZip.UI/Dialogs/FavoriteManagerWindow.xaml`, `.cs` + MainWindow |
| 7 | `feat(ui): add DynamicFormatOptionsPanel for format-specific settings` | `src/MantisZip.UI/Controls/DynamicFormatOptionsPanel.xaml`, `.cs` |
| 8 | `feat(ui): integrate QuickPathControl + format options into CompressSettingsWindow` | `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml`, `.cs` |
| 9 | `feat(ui): add UnifiedExtractDialog with QuickPathControl` | `src/MantisZip.UI/Dialogs/UnifiedExtractDialog.xaml`, `.cs` + `src/MantisZip.UI/MainWindow/MainWindow.Menu.cs` |
| 10 | `feat(ui): replace 7z.dll path in SettingsWindow with QuickPathControl` | `src/MantisZip.UI/Dialogs/SettingsWindow.xaml`, `.cs` |
| 11 | `feat(ui): replace export path in PasswordManagerWindow with QuickPathControl` | `src/MantisZip.UI/Dialogs/PasswordManagerWindow.xaml`, `.cs` |
| 12 | `feat(ui): replace 7z.dll startup selection with QuickPathDialog` | `src/MantisZip.UI/App.xaml.cs` |
| 13 | `feat(ui): replace compress path selection in MainWindow with QuickPathControl` | `src/MantisZip.UI/MainWindow/MainWindow.Menu.cs` |
| 14 | `feat(ui): add ArchiveSaveAsDialog with QuickPathControl + format options` | `src/MantisZip.UI/Dialogs/ArchiveSaveAsDialog.xaml`, `.cs` |
| 15 | `test: add unit tests for FavoritePathManager and PathHistoryManager` | `tests/...` |
| 16 | `test: add integration tests for ExplorerWindowTracker` | `tests/...` |
| 17 | `fix(docs): correct 7z encrypted preview claim in AGENTS.md` | `AGENTS.md` |

---

## Success Criteria

### Verification Commands
```bash
dotnet build src/MantisZip.UI/MantisZip.UI.csproj  # Expected: Build succeeded
dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj  # Expected: all tests pass
```

### Final Checklist
- [ ] 所有 Must Have 全部完成
- [ ] 所有 Must NOT Have N/A
- [ ] 所有测试通过
- [ ] 应用启动正常，所有 QuickPathControl 场景可用
- [ ] AGENTS.md 已修正 7z 加密预览描述
