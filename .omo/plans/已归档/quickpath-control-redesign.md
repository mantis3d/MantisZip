# QuickPathControl 重构计划 — Tab 式路径速选 + CustomFilePickerDialog 统一

> **基于讨论（2026-07-14 起）**: 用户提出将 ⭐收藏/🕐历史/🪟窗口 从按钮下拉改为 Tab 式布局，搜索作为独立控件常驻，地址栏做文件系统实时补全，额外内容插槽（只读显示）。
> **2026-07-31 修订**: 取消 QuickPathPreDialog 过渡方案，直接实现 CustomFilePickerDialog（左 QuickPath + 右文件浏览）。宿主一律弹窗调用，不做内嵌——压缩/解压设置窗口本身已拥挤，内嵌 Tab 面板太占空间。
> **2026-07-31 审查修正**（探索代理 + 全量 grep 验证）: Avalonia 版 4 个 QuickPathControl 宿主对话框（QuickPathDialog/QuickPathPreDialog/ArchiveSaveAsDialog/UnifiedExtractDialog）**全部只挂在测试菜单**（`MainWindow.axaml` L458-495 + `TestWindow_Click` L863-867），4 个 VM 委托（ShowQuickPathDialog 等，L109-124）**零 Invoke 调用点**——生产代码零引用。结论：Task 1 重构控件无生产兼容包袱；Task 6 清理范围扩大为全部 4 个对话框。另修正：格式联动为新增功能、由宿主传入 defaultExtension（Task 3）；地址栏补全/历史数据源明确（Task 2）。
> **2026-07-31 布局决策（方案 1）**: CustomFilePickerDialog 内建 ResultTreeView 解压预览区（用户提出「选择解压路径时应有 ResultTreeView」）。布局：底部横铺 + GridSplitter（默认 160px，120–280px），仅解压模式（`ShowExtractFolderAsync`）显示，非解压模式整行隐藏（800×420）。数据跟随当前浏览路径防抖重建（`checkExists:true` 实时冲突检测）。原「ExtraContent 宿主注入」方案取消（宿主注入只能给静态树，无法随路径更新）。ExtractSettingsWindow 右侧现有树保留（`checkExists:false`，语义互补）。详见 ADR-3/ADR-7。
> **2026-07-31 合并影响（分支合并 a80933f）**: ① Task 6 行号漂移（MainWindow.axaml L471-508、MainWindowViewModel L110-125/L236-239；MainWindow.axaml.cs 未改仍准确）已更新。② ExtractSettingsWindow 宿主树已升级 `checkExists:true`（合并改），ADR-7 语义调整为「确认后 vs 探索中」。③ `BuildExtractPreview` 增强：根节点改为 destDir 自身 + 新增目录级冲突检测——弹窗内树语义更准确，计划受益无需改。④ Task 3 格式联动结论不变（BrowseOutput 仍硬编码 `.zip`）。

## TL;DR

重构 QuickPathControl 为 Tab 式路径速选面板 + 地址栏文件系统补全 + 只读额外内容插槽，并实现 CustomFilePickerDialog（QuickPath 面板 + 文件浏览区），统一替换 Avalonia 版中 5 处路径选择场景的系统对话框调用。**不再引入 QuickPathPreDialog 过渡层**（其弹窗外壳职责并入 CustomFilePickerDialog）。

**产出**:
- `Controls/QuickPathControl.axaml` + `.cs` — 核心面板（Tab 速选 + 搜索 + 路径列表，无地址栏）
- `Dialogs/CustomFilePickerDialog.axaml` + `.cs` — 完整文件选择器（左 QuickPath + 右文件浏览 + 顶部地址栏 + 确定/取消 + 额外插槽）
- 修改 3 个宿主窗口的路径选择调用（CompressSettingsWindow / ExtractSettingsWindow / AddFavoriteDialog，统一弹窗）
- 删除 4 个测试菜单对话框 + 4 个僵尸委托（Task 6）
- 保留 3 处系统对话框场景不动

---

## 架构

```
CustomFilePickerDialog (Window) — 解压模式 800×620 / 其他模式 800×420
──────────────────────────────
┌───────────────────────────────────────────────────────┐
│ [AutoCompleteBox_______________] [◀][▶][▲][📁]      │ ← 行 1：地址栏 + 导航（供右侧浏览区）40px
├────────────────────────┬──────────────────────────────┤
│ QuickPathControl       │ 文件/目录浏览列表             │ ← 行 2：左面板 + 右浏览区 360px
│ ┌───────────────────┐  │ ┌──────────────────────────┐ │
│ │ [⭐][🕐][🪟] 🔍   │  │ │ 📁 项目A                 │ │
│ │ ───────────────── │  │ │ 📁 项目B                 │ │
│ │ 当前 tab 列表      │  │ │ 📄 readme.txt            │ │
│ │ 点选→右侧跳转     │  │ │ 📄 notes.md               │ │
│ └───────────────────┘  │ └──────────────────────────┘ │
│       220px             │       540px                 │
├────────────────────────┴──────────────────────────────┤
│ ◆ GridSplitter（可拖）                                  │
│ ┌───────────────────────────────────────────────────┐ │
│ │ 解压预览：📦 解压到 {当前路径}   [Compact/Full][过滤]│ │ ← 行 3：ResultTreeView（解压模式专属）
│ │  📁 项目A/                    ⚠️ 2 个冲突           │ │   默认 160px，范围 120–280px
│ │  ├─ 📄 readme.txt  ⚠️ 已存在                      │ │
│ │  └─ + 18 项…（截断）                               │ │
│ └───────────────────────────────────────────────────┘ │
├───────────────────────────────────────────────────────┤
│                    [确定]              [取消]          │ ← 48px
└───────────────────────────────────────────────────────┘
```

**解压预览区（方案 1，2026-07-31 确认）**:
- 仅解压模式（`ShowExtractFolderAsync`）显示；其他模式整行隐藏，窗口回落到 800×420
- 数据：`ResultPreviewService.BuildExtractPreview(entries, 当前浏览路径, checkExists: true)` —— **冲突检测实时**
- 联动：跟随当前浏览路径，路径变化 → 防抖 ~300ms 重建树；无冲突正常显示，有冲突 ⚠️ 高亮 + 底部「N 个冲突」计数
- `MaxItemsPerDirectory=8 / MaxDepth=4`（横铺空间较 ExtractSettingsWindow 280px 竖条宽松）

### 两个复用单元

```
QuickPathControl（UserControl，左面板）
    ├── 内嵌到 CustomFilePickerDialog 左侧
    │    路径选择 → 通知右侧浏览区跳转
    │
    └── 未来可独立复用（如嵌入主窗口工具栏）

CustomFilePickerDialog（Window）
    包裹 QuickPathControl + 文件浏览区 + 地址栏 + 确定/取消 + 解压预览区（内建 ResultTreeView，仅解压模式）
    弹窗模式，与宿主解耦
```

### 替换范围

| # | 位置 | 当前方式 | 替换方式 |
|---|------|---------|---------|
| 1 | QuickPathControl.[浏览] | StorageProvider 系统对话框 | 保留兜底，但用户主要走 ⭐🕐🪟+搜索+文件浏览区 |
| 2 | CompressSettingsWindow.BrowseOutput | SaveFilePickerAsync | CustomFilePickerDialog.SaveFile() 弹窗（格式联动） |
| 3 | CompressSettingsWindow.PickFolder | OpenFolderPickerAsync | CustomFilePickerDialog.PickFolder() 弹窗 |
| 4 | ExtractSettingsWindow.浏览解压路径 | OpenFolderPickerAsync | CustomFilePickerDialog.ShowExtractFolderAsync() 弹窗（解压模式，内建 ResultTreeView 实时冲突检测） |
| 5 | AddFavoriteDialog.浏览路径 | OpenFolderPickerAsync | CustomFilePickerDialog.PickFolder() 弹窗 |

**不替换**（保留系统对话框）:
- MainWindow → 打开压缩包（选具体文件，需要文件筛选器）
- CompressSettingsWindow.PickFiles（多选文件）
- MainWindow.GetOpenFilePaths（添加文件到压缩包）

### 解压预览区（内建 ResultTreeView）

解压模式（`ShowExtractFolderAsync`）在底部横铺 ResultTreeView，替代原「ExtraContent 宿主注入」方案（2026-07-31 布局确认，方案 1）：

```csharp
// CustomFilePickerDialog 解压模式入口（替代通用 ExtraContent 注入）
public static Task<string?> ShowExtractFolderAsync(
    Window owner,
    IReadOnlyList<ArchiveItem> entries,   // 解压条目 → 弹窗内 BuildExtractPreview(entries, 当前路径, checkExists: true)
    string? initialPath = null)
```

- 原 ExtraContent 通用插槽（ContentPresenter）**取消**——无其他消费者，解压场景走专用入口更简单
- 宿主（ExtractSettingsWindow）只传 `_entries`，不构造树、不监听路径变化——全部由弹窗内部完成

---

## 架构决策记录

### ADR-1: Tab + 搜索一体化布局
- **状态**: 已确认
- **理由**: 按钮下拉每次只看一种类别，Tab 可随时切换 + 搜索框常驻输入即过滤，体验更统一
- **Tab 内容**: ⭐收藏 / 🕐历史 / 🪟窗口 各占一个 tab，切换后内容区显示对应列表，搜索框打字时跨三个来源聚合过滤

### ADR-2: 搜索只搜本地数据源，不扫文件系统
- **状态**: 已确认
- **理由**: 搜索范围 = 收藏 + 历史 + 窗口数据，不搜全盘（不是 Everything）。文件系统实时枚举从地址栏 AutoCompleteBox 做，互相不干扰

### ADR-3: 解压预览区内建（原「ExtraContent 只读宿主注入」→ 修订）
- **状态**: 已修订（2026-07-31）
- **原方案**: ContentPresenter 通用插槽，宿主注入只读树
- **修订理由**: 用户提出内建 ResultTreeView 可行性——解压路径选择的核心决策是「该路径会不会覆盖已有文件」，树必须**跟随弹窗内当前路径实时重建**（`checkExists:true` 冲突检测）。宿主注入方案只能注入「打开时构建一次的静态树」，无法随路径变化更新。内建后弹窗自管理：路径变化 → 防抖重建，宿主只传 `_entries`
- **布局**: 底部横铺（方案 1，用户确认），GridSplitter 可拖 120–280px，默认 160px
- **通用 ExtraContent 插槽取消**——无其他消费者，解压场景走 `ShowExtractFolderAsync` 专用入口

### ADR-4: 跨平台策略
- **状态**: 天然跨平台
- **理由**: FavoritePathManager + PathHistoryManager 纯 JSON 读写无平台依赖；ExplorerWindowTracker Windows 用 COM 其他平台 try/catch 返回空列表 → UI 层自动隐藏 🪟 tab；地址栏 AutoCompleteBox 的 `Directory.EnumerateDirectories` 跨平台

### ADR-5: CustomFilePickerDialog 采纳（原搁置 → 采纳）
- **状态**: 已采纳（2026-07-31）
- **理由**: 用户目标为 Listary 式「吸附切换路径」体验。系统对话框（WinRT Picker / Win32 IFileDialog）均无法注入自定义 UI，唯一干净方案是自建对话框，把路径速选与文件浏览放进同一个窗口。QuickPathPreDialog（纯路径选择无浏览）因此被淘汰——它只是 CustomFilePickerDialog 之前的过渡形态，不重复实现
- **文件浏览区职责**: 目录枚举（异步）、系统图标（复用 Win32IconProvider）、排序、双击进入、键盘导航（Enter 进入/确认、Backspace 上级、Alt+←/→ 前进后退）

### ADR-6: 宿主一律弹窗调用，不内嵌
- **状态**: 已确认（2026-07-31）
- **理由**: CompressSettingsWindow / ExtractSettingsWindow 本身已拥挤，内嵌 Tab 面板（两行高）太占空间且需改宿主布局（繁琐、易破坏现有 UI）。统一为 `await CustomFilePickerDialog.ShowAsync(...)` 弹窗调用，宿主改动最小、视觉不变
- **QuickPathControl 因此不需要地址栏**——地址栏归 CustomFilePickerDialog 顶部统一管理，左面板只留 Tab + 搜索 + 列表

### ADR-7: 解压预览区跟随当前路径实时重建（冲突检测）
- **状态**: 已确认（2026-07-31）
- **理由**: ResultTreeView 的 `checkExists` 冲突检测（`ExistsAtDestination = File.Exists(destDir + entry.FullPath)`，合并后新增目录级 `MarkDirectoryConflicts`）依赖目标路径——路径不同，冲突标记不同。内建后跟随浏览区当前路径，用户翻到哪棵树就重建为「解压到该路径」的冲突预览
- **性能防护**: 路径变化 → 防抖 ~300ms 重建（`checkExists:true` 构建阶段逐文件 `File.Exists`，大包不能每跳一次全量算）；树显示阶段由 ResultTreeView 的 `MaxItemsPerDirectory/MaxDepth` 截断兜底
- **与宿主树的关系**: ExtractSettingsWindow 右侧现有 ResultTreeView 已升级为 `checkExists:true`（2026-07-31 合并）。两棵树的区别从「静态 vs 实时」变为「**确认后 vs 探索中**」——宿主树在 DestinationPath 变化（关闭弹窗后）才重建，弹窗内树跟随浏览路径实时重建，语义互补不冲突

---

## 实施任务

### Task 1: 重构 QuickPathControl（左面板核心）

**文件**: `Controls/QuickPathControl.axaml` + `.axaml.cs`

**做什么**:
- 面板结构：行 1 = Tab 行（⭐收藏/🕐历史/🪟窗口 toggle + 搜索框），行 2 = 当前 tab 路径列表
- Tab 切换：点 ⭐ → 内容区显示 `FavoritePathManager.GetAll()` 列表；点 🕐 → 历史列表；点 🪟 → 窗口列表
- 搜索框：输入时实时聚合过滤三个来源（按路径名/名字匹配），来源用标签/颜色区分
- 路径选中事件：`PathSelected` 事件（string 参数）——由宿主（CustomFilePickerDialog）监听并驱动文件浏览区跳转
- 选中路径后自动调用 `PathHistoryManager.Record(path)`
- 所有控件绑定主题资源键（Theme_*）
- 紧凑度感知行高（规则 7：`ControlHeightMd`/`ControlHeightSm`）

**宿主处置（审查确认）**: 控件现有 4 个宿主（QuickPathDialog/QuickPathPreDialog/ArchiveSaveAsDialog/UnifiedExtractDialog）全部为测试菜单对话框，**随 Task 6 一并删除**。本任务只面向 CustomFilePickerDialog 单一需求重构，无兼容包袱。

**不做**:
- ~~地址栏~~（归 CustomFilePickerDialog）
- ~~[浏览] 按钮~~（归 CustomFilePickerDialog 顶部）
- 不搜整个文件系统
- 不做文件浏览列表
- 不添加 NuGet 依赖
- 不修改打开压缩包/添加多文件等系统对话框场景
- ~~兼容旧测试宿主（全部删除）~~

**参考**:
- WPF `QuickPathControl.xaml.cs` — ⭐🕐🪟 下拉和 FavoritePathManager/PathHistoryManager/ExplorerWindowTracker 集成逻辑（`MantisZip.UI/Controls/`）
- 现有 Avalonia `QuickPathControl.axaml` + `.cs` — 当前简化版结构
- `MantisZip.Core/Utils/FavoritePathManager.cs`
- `MantisZip.Core/Utils/PathHistoryManager.cs`
- `MantisZip.Core/Utils/ExplorerWindowTracker.cs`

---

### Task 2: 实现 CustomFilePickerDialog（完整文件选择器）

**文件**: `Dialogs/CustomFilePickerDialog.axaml` + `.axaml.cs`

**做什么**:
- 窗口布局（方案 1，2026-07-31 确认）：
  - 顶部：地址栏 AutoCompleteBox + 导航按钮（◀ 后退 ▶ 前进 ▲ 上级 📁 系统对话框兜底）40px
  - 中部：左侧内嵌 QuickPathControl（220px）+ 右侧文件/目录浏览列表（ListView，系统图标 + 名称 + 大小 + 修改日期，双击目录进入）540px，共 360px
  - 底部：**解压预览区**（仅解压模式显示）：GridSplitter + ResultTreeView（默认 160px，范围 120–280px），非解压模式整行隐藏、窗口回落到 800×420
  - 最底：确定/取消 48px
- 地址栏数据源（明确）: 补全 = `Directory.EnumerateDirectories(parentDir, prefix + "*")` 实时文件系统枚举，Take(20)；**历史建议 = `PathHistoryManager`（Core 持久化，首次接入 Avalonia）**，回车/选中后自动 `Record(path)`
- 四种模式（enum `PickerMode`）：
  - `PickFolder`：仅显示目录，确定返回目录路径
  - `SaveFile`：显示文件+目录，支持文件名输入，确定返回完整保存路径；格式联动自动更新扩展名
  - `OpenFile`：显示文件+目录，文件筛选器，确定返回文件路径（单文件）
  - `ExtractFolder`（解压模式）：PickFolder + 底部解压预览区。构造时接收 `IReadOnlyList<ArchiveItem> entries`；**路径变化 → 防抖 ~300ms → `ResultPreviewService.BuildExtractPreview(entries, 当前路径, checkExists: true)`** → `ResultTreeView.Root`；`MaxItemsPerDirectory=8 / MaxDepth=4`
- 交互联动：QuickPathControl `PathSelected` → 浏览区跳转该目录；浏览区导航 → 地址栏同步；QuickPathControl 中命中收藏/历史的当前路径时高亮；解压模式下浏览区导航同样触发预览区重建
- 键盘：Enter=确认（目录上=进入，文件/根=确定）、Backspace=上级、Esc=取消
- 静态入口：
  ```csharp
  public static Task<string?> ShowFolderAsync(Window owner, string? initialPath = null)
  public static Task<string?> ShowSaveFileAsync(Window owner, string? initialPath = null, string? defaultExtension = null)
  public static Task<string?> ShowOpenFileAsync(Window owner, string? initialPath = null)
  public static Task<string?> ShowExtractFolderAsync(Window owner, IReadOnlyList<ArchiveItem> entries, string? initialPath = null)
  ```
- 确定后返回 `SelectedPath`，取消返回 null

**不做**:
- 不重复实现 QuickPathControl 已有的功能
- 不添加路径验证逻辑（如目录是否存在）
- 不做此电脑/库等虚拟 Shell 命名空间（只认真实文件系统路径，特殊目录用环境变量展开）

---

### Task 3: CompressSettingsWindow 集成

**文件**: `Dialogs/CompressSettingsWindow.axaml` + `.axaml.cs`

**做什么**:
- 替换 BrowseOutput（保存路径）为 `CustomFilePickerDialog.ShowSaveFileAsync` 弹窗
- 替换 PickFolder（选要压缩的文件夹）为 `CustomFilePickerDialog.ShowFolderAsync` 弹窗
- PickFiles（选要添加的文件）暂时保留系统对话框
- ViewModel 中 `BrowseOutput` 回调改为读取弹窗返回值
- 格式联动（新增功能，归属明确）: 现状 SaveFilePicker 硬编码 `.zip`、与 DefaultFormat 无关。CustomFilePickerDialog.SaveFile 模式接收 `defaultExtension` 参数（保存文件名初始扩展名），**由宿主在弹窗前根据 `DefaultFormat` 计算传入**；弹窗内格式切换时同步更新文件名扩展名。VM 不参与弹窗内部逻辑

---

### Task 4: ExtractSettingsWindow 集成

**文件**: `Dialogs/ExtractSettingsWindow.axaml` + `.axaml.cs`

**做什么**:
- 替换浏览解压路径为 `CustomFilePickerDialog.ShowExtractFolderAsync(_entries)` 弹窗（解压模式）
- **宿主只传 `_entries`**（已有字段），不构造树、不监听路径变化——弹窗内部完成解压预览区（ResultTreeView + 防抖冲突检测）
- ExtractSettingsWindow 已有 `_entries` 字段 + `SetEntries()` 全套接线（`Dialogs/ExtractSettingsWindow.axaml.cs` L21, L71-75），Task 4 只需改 BrowseFolder 回调为 `ShowExtractFolderAsync` 调用
- ExtractSettingsWindow 右侧现有 ResultTreeView（已升级 `checkExists:true`，2026-07-31 合并）**保留不动**——与弹窗内树语义互补（已确认路径 vs 探索中路径）

---

### Task 5: AddFavoriteDialog 集成

**文件**: `Dialogs/AddFavoriteDialog.axaml` + `.axaml.cs`

**做什么**:
- 替换 [浏览] 按钮为 `CustomFilePickerDialog.ShowFolderAsync` 弹窗（选目录模式）
- 保留名称输入框 + 路径文本框 + 确定/取消

---

### Task 6: 清理 4 个测试对话框 + 僵尸委托

**文件**: `Dialogs/QuickPathPreDialog.axaml(.cs)`、`Dialogs/QuickPathDialog.axaml(.cs)`、`Dialogs/ArchiveSaveAsDialog.axaml(.cs)`、`Dialogs/UnifiedExtractDialog.axaml(.cs)`、`Controls/QuickPathControl.axaml(.cs)`（视 Task 1 重构结果）、引用它们的宿主

**背景（审查确认）**: 这 4 个对话框全部只在测试菜单实例化，生产代码零引用；4 个 VM 委托零 Invoke。

**做什么**:
- 删除 4 个对话框文件（QuickPathPreDialog / QuickPathDialog / ArchiveSaveAsDialog / UnifiedExtractDialog）
- 删除 MainWindowViewModel 中 4 个僵尸委托（L110-125：ShowQuickPathDialog / ShowArchiveSaveAsDialog / ShowUnifiedExtractDialog / ShowQuickPathPreDialog）
- 删除 MainWindow.axaml.cs 委托接线（L127-152）
- 删除 MainWindow.axaml 测试菜单 4 项（L471-508：QuickPathDialog / QuickPathPreDialog / ArchiveSaveAsDialog / UnifiedExtractDialog）
- 删除 `TestWindow_Click` switch 对应分支（MainWindow.axaml.cs L863-867）
- 删除 MainWindowViewModel 测试 key 列表中的 4 个 key（L236-239）+ 两个 strings json 中的对应翻译

**行号说明**: 2026-07-31 合并分支后行号已漂移（MainWindow.axaml +13 行、MainWindowViewModel +1 行），上述为合并后行号。MainWindow.axaml.cs 未在合并中改动（L127-152 / L863-867 仍准确）。

---

## 不做的（记录） / 已知已信用的

- **QuickPathPreDialog** — 废弃，不再实现（弹窗外壳职责并入 CustomFilePickerDialog）
- **QuickPathDialog / ArchiveSaveAsDialog / UnifiedExtractDialog** — 测试菜单对话框，随 Task 6 一并删除（生产零引用，被 CustomFilePickerDialog 取代）
- **ExplorerWindowTracker 跨平台** — Windows COM 枚举，其他平台返回空列表自动隐藏 🪟 tab，不做各平台兼容
- **MainWindow 打开压缩包/添加文件** — 保留系统对话框
- **CompressSettingsWindow.PickFiles**（多选文件添加）— 保留系统对话框
- **此电脑/库等 Shell 虚拟命名空间** — 文件浏览只认真实文件系统路径
- **密码导入/导出、SettingsWindow 7z.dll、DragDropService 选目录** — 本次范围外（unified 计划提及但未拍板，后续单独决策）
- 不在 WPF 项目（`MantisZip.UI`）上做任何改动
