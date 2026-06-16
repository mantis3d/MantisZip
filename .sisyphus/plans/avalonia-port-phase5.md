# Avalonia 移植 Phase 5：档案编辑 + 缺失功能补齐

> **分支**: `avalonia-port`（从 Phase 4 继续）
> **目标**: 补齐 Avalonia 版相对 WPF 缺失的 12 项功能，使 Avalonia 版具备完整的档案管理能力（添加/删除/测试/智能解压等）
> **约束**: ⚠️ 所有修改仅限于 `src/MantisZip.UI.Avalonia/` 及 `tests/MantisZip.UI.Avalonia.Tests/`
> **设计决策**:
>   - 新增功能全部走 ViewModel + RelayCommand 模式，不写代码后置
>   - 添加/删除/注释功能委托给 Core 层的 `IArchiveEngine` 方法（SharpCompress 已支持）
>   - 对话框用 Avalonia 原生 `Window`（与现有模式一致）
>   - 智能解压复用 `ArchiveStructureAnalyzer`（Core 层已有）
>   - 拖拽打开复用 Avalonia `DragDrop` API（与现有拖出逻辑对称）
>   - 最近文件列表持久化到 JSON（与 WPF 逻辑一致）
> **不做的事情**:
>   - 不修改 `MantisZip.Core/`（智能解压分析器已在 Core，直接调用）
>   - 不修改 `MantisZip.UI/`（WPF 版不动）
>   - 不做 Donate 页面（非核心功能）
>   - 不做 ShowProgressBars/SepDirBaseline 切换按钮（WPF 遗留功能，非必要）
> **创建日期**: 2026-06-16
> **状态**: 📋 计划 | **任务**: [⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜]

---

## 功能缺口总览

| # | 功能 | WPF 位置 | 优先级 | 说明 |
|---|------|---------|:------:|------|
| 1 | 向压缩包添加文件 | Edit → AddFiles / 工具栏 | **P0** | 核心档案编辑能力 |
| 2 | 从压缩包删除文件 | Edit → DeleteFiles / 工具栏 | **P0** | 核心档案编辑能力 |
| 3 | 测试压缩包完整性 | 工具栏 Test 按钮 | **P0** | 核心验证能力 |
| 4 | 智能解压（SmartExtract） | 工具栏按钮 | **P1** | 重要 UX 改进 |
| 5 | 拖拽打开压缩包 | Window Drop 事件 | **P1** | 重要 UX 改进 |
| 6 | 编辑压缩包注释 | Edit → ArchiveComment | **P2** | ZIP-only |
| 7 | 最近文件列表 | File → RecentFiles | **P2** | 方便回访 |
| 8 | 显示子文件夹切换（工具栏） | 工具栏 ToggleButton | **P2** | 当前在筛选栏内已有 |
| 9 | 压缩包密码输入（工具栏） | 工具栏密码按钮 | **P1** | 当前走对话框，但工具栏快捷按钮更方便 |
| 10 | 右键菜单增强 | DataGrid ContextMenu | **P1** | 当前只有 Extract，缺 CopyName/Test/Delete/ExtractTo |
| 11 | 工具栏 Tooltip | 每个按钮有 ToolTip | **P2** | 当前按钮无 tooltip |
| 12 | 列排序箭头 ▲/▼ | 点击列头时显示 | **P2** | DataGrid 列头排序方向指示 |
| 13 | 列标题 emoji 图标 | 如 📏大小、📦压缩后 | **P3** | 纯视觉改进 |
| 14 | 空状态拖拽引导 | 未加载压缩包时显示 📂 拖拽提示 | **P1** | 当前空窗口无引导，用户不知道可以拖拽 |
| 15 | 标题栏显示条目统计 | "MantisZip - archive.zip (42 项)" | **P2** | 当前标题只有文件名 |
| 16 | 键盘快捷键 | Ctrl+O/W/E/C、F5 等 | **P2** | 当前无快捷键 |

---

## 实现步骤

### Task 1：添加文件到压缩包（P0）

> 用户在浏览压缩包时，可以向其中添加新文件。引擎层使用 SharpCompress `ZipArchive.AddEntry()` / `SevenZipArchive.AddEntry()`（Windows 保留 SharpSevenZip）。

**文件变更**（`src/MantisZip.UI.Avalonia/`）:
```
ViewModels/MainWindowViewModel.cs  ← 添加 AddFilesCommand
Views/MainWindow.axaml             ← 添加菜单项 + 工具栏按钮
Localization/strings.*.json        ← 添加 i18n 键
```

- [ ] **1.1** `MainWindowViewModel` 添加：
  - `AddFilesCommand`：打开文件选择器（通过 `GetOpenFilePaths` 回调），将选中文件添加到当前压缩包
  - 调用 `_archiveService.AddFilesAsync(archivePath, filesToAdd, password, progress, ct)`
  - 添加完成后自动刷新（调用 `RefreshArchive()`）
  - 仅压缩包已加载时启用
- [ ] **1.2** `MainWindow.axaml` 添加：
  - 文件菜单：新增"添加文件"菜单项（Ctrl+A），仅 `IsArchiveLoaded` 时启用，带 emoji `➕`
  - 工具栏：分隔线后添加添加文件按钮（emoji `➕`）
- [ ] **1.3** `MainWindowViewModel` 添加 `Func<Task<IReadOnlyList<string>?>>? GetOpenFilePaths` 回调
  - View 层用 `StorageProvider.OpenFilePickerAsync` 实现
- [ ] **1.4** `ArchiveService.cs` 或直接调用引擎：确认已有 `IArchiveEngine.AddEntriesAsync()` 或提供封装
  - 如果 Core 没有暴露 AddEntries 方法，走 SharpCompress 直接操作
- [ ] **1.5** 验证：打开 ZIP 文件 → 添加文件 → 刷新后新文件出现在列表中

---

### Task 2：从压缩包删除文件（P0）

> 用户在列表中选择文件后，可以将其从压缩包中删除。

- [ ] **2.1** `MainWindowViewModel` 添加：
  - `DeleteFilesCommand`：确认对话框 → 调用 `_archiveService.DeleteFilesAsync(archivePath, selectedPaths, password, ct)`
  - 完成后自动刷新
  - 仅选中条目且压缩包已加载时启用
  - 确认对话框复用 `IArchiveEngine` 的删除能力
- [ ] **2.2** `MainWindow.axaml` 添加：
  - 文件菜单："删除文件"菜单项（Del），带 emoji `✖`
  - 工具栏：删除按钮（emoji `✖`），紧挨添加文件按钮
- [ ] **2.3** 验证：选择 ZIP 中文件 → 删除 → 文件从列表中消失

---

### Task 3：测试压缩包完整性（P0）

> 验证压缩包是否损坏。引擎层使用 `IArchiveEngine.TestArchiveAsync()`。

- [ ] **3.1** `MainWindowViewModel` 添加：
  - `TestArchiveCommand`：调用 `engine.TestArchiveAsync()` 带进度窗口
  - 结果显示在 `StatusMessage`（"压缩包完整 ✅" / "压缩包已损坏 ❌: ..."）
  - 仅压缩包已加载时启用
- [ ] **3.2** `MainWindow.axaml` 添加：
  - 工具栏：Test 按钮（emoji `✅` 或 `🔍`），在删除按钮之后
  - 菜单：添加到文件菜单或视图菜单
- [ ] **3.3** 验证：打开 ZIP → 点击 Test → 状态栏显示结果

---

### Task 4：智能解压（P1）

> 自动检测压缩包内文件结构——如果所有文件都在一个顶级目录内，解压时自动剥离该目录层。复用 Core 的 `ArchiveStructureAnalyzer`。

- [ ] **4.1** `MainWindowViewModel` 添加：
  - `SmartExtractCommand`：调用 `ArchiveEngineFactory.SmartExtractEntriesAsync()`
  - 目标路径：压缩包所在目录（与 WPF 一致）
  - 带进度窗口
- [ ] **4.2** `MainWindow.axaml` 添加：
  - 编辑菜单：智能解压菜单项（在解压到命名之后）
  - 工具栏：智能解压按钮（emoji `🤖`），在解压按钮旁边
- [ ] **4.3** 验证：打开多层嵌套的 ZIP → 智能解压 → 文件不产生多余文件夹层

---

### Task 5：拖拽打开压缩包（P1）

> 从文件管理器拖拽 .zip/.7z/.tar 等到窗口上，直接打开该压缩包。

- [ ] **5.1** `Views/MainWindow.axaml` 添加：
  - `AllowDrop="True"` 属性
- [ ] **5.2** `Views/MainWindow.axaml.cs` 或 ViewModel 添加：
  - `DragDrop.DropEvent` 处理：检查拖入的文件是否为支持的压缩包格式
  - 是 → 调用 `LoadArchiveAsync(filePath)`
  - 否 → 状态栏提示"不支持的文件格式"
  - 多个文件 → 只打开第一个匹配的压缩包
- [ ] **5.3** `DragDrop.DragOverEvent`：设置 `DragDropEffects.Copy`（或 `Link`）表示接受
- [ ] **5.4** 验证：从资源管理器拖拽 .zip 到窗口 → 自动打开

---

### Task 6：编辑压缩包注释（P2）

> 编辑 ZIP 压缩包的注释（仅 ZIP 格式支持）。使用 `SharpCompress.Writers.Zip.ZipWriter` 或直接操作 SharpCompress 的 `ZipArchive`。

- [ ] **6.1** 新建 `Dialogs/CommentDialog.axaml` + `.axaml.cs`：
  - 多行 TextBox 显示当前注释
  - 保存/取消按钮
  - 主题色绑定
- [ ] **6.2** `MainWindowViewModel` 添加：
  - `EditCommentCommand`：打开 `CommentDialog` → 读取当前注释 → 保存新注释
  - 仅 ZIP 格式时启用（`_currentFormat == ArchiveFormat.Zip`）
  - 使用 SharpCompress `ZipArchive.SetComment()` 或逐文件操作
- [ ] **6.3** `MainWindow.axaml` 添加：
  - 编辑菜单："压缩包注释"菜单项（在删除之后），带 emoji `💬`
- [ ] **6.4** 验证：打开 ZIP → 编辑注释 → 关闭重新打开 → 注释持久化

---

### Task 7：最近文件列表（P2）

> 记录最近打开的压缩包路径，在文件菜单中显示快速入口。

- [ ] **7.1** `Models/RecentFilesManager.cs`（NEW）：
  - 存储：JSON 文件 `%APPDATA%/MantisZip/recent.json`，最多 10 条
  - `AddPath(string path)`、`GetPaths() → List<string>`、`Clear()`
  - 检查路径是否存在，不存在则跳过
- [ ] **7.2** `MainWindowViewModel` 添加：
  - `RecentFiles` 集合属性（`ObservableCollection<string>`）
  - `OpenRecentFileCommand(string path)`
  - 在 `LoadArchiveAsync` 成功时调用 `RecentFilesManager.AddPath()`
  - 打开压缩包时自动更新列表
- [ ] **7.3** `MainWindow.axaml` 添加：
  - 文件菜单："最近文件"子菜单（动态生成）
  - 如果列表为空，显示"无最近文件"（禁用）
  - 底部"清除最近文件"按钮
- [ ] **7.4** 验证：打开几个压缩包 → 文件菜单显示最近文件 → 点击可重新打开

---

### Task 8：工具栏显示子文件夹切换（P2）

> 当前"显示子文件夹"只在筛选栏内，WPF 工具栏也有此按钮。

- [ ] **8.1** `MainWindow.axaml` 工具栏：
  - 在预览按钮后添加分隔线 + ToggleButton（emoji `📂`），绑定 `ShowSubfolders`
- [ ] **8.2** 与筛选栏内已有 `ShowSubfolders` CheckBox 保持同步（绑定同一属性）

---

### Task 10：右键菜单增强（P1）

> 当前 DataGrid 的 ContextMenu 只有"Extract"一项。WPF 版有：提取选定、复制文件名、测试条目、删除等。

- [ ] **10.1** 替换 `MainWindow.axaml` 中 DataGrid 的 ContextMenu：
  ```xml
  <DataGrid.ContextMenu>
    <ContextMenu>
      <MenuItem Header="Extract" Command="{Binding ExtractArchiveCommand}" />
      <MenuItem Header="Smart Extract" Command="{Binding SmartExtractCommand}" />
      <MenuItem Header="Extract to…" Command="{Binding ExtractToCommand}" />
      <Separator />
      <MenuItem Header="Copy Name" Command="{Binding CopyFileNameCommand}" />
      <MenuItem Header="Test" Command="{Binding TestEntryCommand}" />
      <Separator />
      <MenuItem Header="Delete" Command="{Binding DeleteFilesCommand}" />
    </ContextMenu>
  </DataGrid.ContextMenu>
  ```
- [ ] **10.2** `MainWindowViewModel` 添加命令：
  - `CopyFileNameCommand`：复制选中条目文件名到剪贴板
  - `TestEntryCommand`：仅测试选中的单一条目
  - `ExtractToCommand`：打开解压设置对话框（预填选中条目路径）
- [ ] **10.3** 命令仅在有选中条目时启用
- [ ] **10.4** 验证：右键文件列表 → 各项功能正常

---

### Task 11：工具栏 Tooltip（P2）

> WPF 每个工具栏按钮都有 `ToolTip`，Avalonia 当前无 tooltip。

- [ ] **11.1** `MainWindow.axaml` 为每个工具栏 Button/ToggleButton 添加 `ToolTip.Tip` 属性：
  - New: "创建新压缩包 (Ctrl+N)"
  - Open: "打开压缩包 (Ctrl+O)"
  - Extract: "解压到… (Ctrl+E)"
  - Compress: "压缩文件 (Ctrl+C)"
  - SmartExtract: "智能解压"
  - AddFiles: "添加文件"
  - DeleteFiles: "删除文件"
  - Filter: "显示/隐藏筛选栏"
  - Preview: "显示/隐藏预览面板"
  - Test: "测试压缩包完整性"
  - Password: "输入压缩包密码"
  - Subfolders: "显示子文件夹内容"
- [ ] **11.2** Tooltip 文字绑定到 `LocalizedStrings`（支持多语言）

---

### Task 12：列排序箭头（P2）

> 当前 DataGrid 列头点击排序但没有方向指示。WPF 显示 ▲/▼。

- [ ] **12.1** `MainWindowViewModel` 跟踪当前排序列和方向（`SortColumn`、`SortDirection`）
- [ ] **12.2** 在列头绑定中动态显示 ▲/▼：
  - 绑定 `SortMemberPath="Name"` 的列头 → 根据当前排序列追加 " ▲" 或 " ▼"
- [ ] **12.3** 或直接启用 Avalonia DataGrid 内置的 `DataGridColumnHeader` 排序箭头样式（检查 `AreSortIndicatorsEnabled`）

---

### Task 13：列标题 emoji 图标（P3）

> WPF 列头显示 emoji：📋名称、📏大小、📦压缩后、📊压缩率、📅日期。

- [ ] **13.1** `MainWindow.axaml` DataGrid 列头 Header 改为 StackPanel（emoji + 文字）：
  - 名称列：`📋` + "名称"
  - 大小列：`📏` + "大小"
  - 压缩后列：`📦` + "压缩后"
  - 修改日期列：`📅` + "修改日期"
- [ ] **13.2** emoji 文字从 `LocalizedStrings` 读取，不影响排序

---

### Task 14：空状态拖拽引导（P1）

> 未加载压缩包时，窗口中央显示一个大图标 + 提示文字："拖拽压缩包到此以打开"。

- [ ] **14.1** `MainWindow.axaml` 添加空状态层（在主 Grid 的顶层）：
  - 仅 `IsArchiveLoaded == false` 时显示
  - 居中显示 📂 emoji（FontSize 64）+ 提示文字
  - 背景透明，带虚线边框（`Border` + `BorderBrush` + `BorderThickness="2"`）
- [ ] **14.2** 空状态区域也支持拖拽（`AllowDrop` + 绑定拖入事件）
- [ ] **14.3** 验证：启动应用 → 看到拖拽引导 → 拖入 .zip → 自动打开 → 引导消失

---

### Task 15：标题栏显示条目统计（P2）

- [ ] **15.1** `MainWindowViewModel` 中 `Title` 属性在加载压缩包后显示：
  - `"MantisZip - archive.zip (42 项)"`
  - 从 `_allRawItems.Count` 获取条目数
- [ ] **15.2** 当前已实现基本文件名标题，只需追加条目计数

---

### Task 16：键盘快捷键（P2）

> WPF 版支持：Ctrl+N/O/W/E/C、F5 刷新、Delete 删除。

- [ ] **16.1** `MainWindow.axaml` 为菜单项添加 `Gesture`（Avalonia 的快捷键机制）：
  - Open: `Ctrl+O`
  - Close: `Ctrl+W`
  - Refresh: `F5`
  - New: `Ctrl+N`
  - Extract: `Ctrl+E`
  - Compress: `Ctrl+C`
  - Delete: `Delete`
  - Add: `Ctrl+A`
- [ ] **16.2** 快捷键通过菜单项的 `Command` + `InputGesture` 实现

---

### Task 9：i18n 补全 + 收尾（已更新）

> 原 Task 9 扩展为包含所有新增功能的本地化键。

- [ ] **9.1** 新增功能所需的 i18n 键写入 `strings.en.json` 和 `strings.zh-CN.json`：
  - `Menu_AddFiles`、`Menu_DeleteFiles`、`Menu_TestArchive`
  - `Menu_SmartExtract`、`Menu_ArchiveComment`、`Menu_RecentFiles`
  - `Toolbar_AddFiles`、`Toolbar_DeleteFiles`、`Toolbar_Test`、`Toolbar_SmartExtract`
  - `Tooltip_*`：全部工具栏按钮 tooltip
  - `Status_AddComplete`、`Status_DeleteComplete`
  - `Status_TestOK`、`Status_TestFailed`
  - `Main_NoRecentFiles`、`Main_ClearRecentFiles`
  - `Main_DropHint`：拖拽引导文字
  - `CtxMenu_*`：右键菜单各项
- [ ] **9.2** `MainWindowViewModel.UpdateLocalizedStrings()` 添加新键
- [ ] **9.3** 验证：所有新增菜单/工具栏/tooltip 显示正确本地化文字

> 补全新增功能所需的本地化键。

- [ ] **9.1** 新增功能所需的 i18n 键写入 `strings.en.json` 和 `strings.zh-CN.json`：
  - `Menu_AddFiles`、`Menu_DeleteFiles`、`Menu_TestArchive`
  - `Menu_SmartExtract`、`Menu_ArchiveComment`、`Menu_RecentFiles`
  - `Toolbar_AddFiles`、`Toolbar_DeleteFiles`、`Toolbar_Test`、`Toolbar_SmartExtract`
  - `Status_AddComplete`、`Status_DeleteComplete`
  - `Status_TestOK`、`Status_TestFailed`
  - `Main_NoRecentFiles`、`Main_ClearRecentFiles`
- [ ] **9.2** `MainWindowViewModel.UpdateLocalizedStrings()` 添加新键
- [ ] **9.3** 验证：所有新增菜单/工具栏显示正确本地化文字

---

## 验证清单

- [ ] 添加文件到 ZIP — 文件出现在列表中
- [ ] 删除 ZIP 中的文件 — 文件从列表中消失
- [ ] 测试 ZIP 完整性 — 状态栏显示结果
- [ ] 智能解压嵌套 ZIP — 不产生多余文件夹层
- [ ] 拖拽 .zip 到窗口 — 自动打开
- [ ] 编辑 ZIP 注释 — 保存后重新打开可见
- [ ] 最近文件列表 — 打开文件后出现在菜单中
- [ ] 工具栏子文件夹切换 — 与筛选栏同步
- [ ] 工具栏密码按钮 — 点击弹出密码输入，状态图标变化
- [ ] 右键菜单 — Extract/CopyName/Test/Delete 全部可用
- [ ] 工具栏 Tooltip — 鼠标悬停显示提示文字
- [ ] 列排序箭头 — 点击列头时 ▲/▼ 可见
- [ ] 列标题 emoji — 📋📏📦📅 显示在列头
- [ ] 空状态引导 — 未加载时显示拖拽提示
- [ ] 标题栏条目统计 — "MantisZip - archive.zip (42 项)"
- [ ] 快捷键 — Ctrl+O/W/E/C、F5、Delete 生效
- [ ] `dotnet build` 0 errors
- [ ] `dotnet test` 全部通过
- [ ] 所有新增菜单显示 emoji 图标
- [ ] 所有新增控件绑定主题色

---

## 边界情况与注意事项

1. **添加文件到加密压缩包**：需要先输入密码（复用现有密码对话框），再用密码打开 ZipFile 实例进行添加。WPF 版的处理逻辑是在添加前检查是否加密，有密码缓存就直接用。

2. **删除文件的不可逆性**：删除后不可撤销。确认对话框风格与 WPF 一致（"确定要删除选中的 N 个文件吗？此操作不可撤销。"）。

3. **ZIP 注释仅 ZIP**：`ArchiveFormat.Zip` 之外的其他格式（7z/tar.gz）不支持注释，菜单项禁用 + 工具提示"仅 ZIP 格式支持注释"。

4. **SmartExtract 非 Windows 行为**：`ArchiveStructureAnalyzer` 是纯托管代码已跨平台，无需改动。

5. **拖拽打开 vs 拖出**：现有拖拽代码只实现了从列表拖出到文件管理器（Phase 2）。拖入打开是新功能，不与现有逻辑冲突。注意 `_isOwnDrag` 标志区分。

6. **最近文件路径校验**：打开最近文件时检查文件是否存在，不存在则从列表中移除并提示"文件已移动或删除"。

7. **TestArchive 进度**：大压缩包的测试可能需要时间，带进度窗口（复用现有 ProgressWindow）。取消时停止测试。

8. **工具栏紧凑性**：添加多个按钮后工具栏可能过长。考虑在 Phase 4 样式统一的基础上，保持按钮高度 42px 不变，必要时用 `WrapPanel` 替代 `StackPanel`。
