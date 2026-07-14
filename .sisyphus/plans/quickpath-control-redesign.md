# QuickPathControl 重构计划 — Tab 式路径速选 + QuickPathPreDialog 统一

> **基于讨论（2026-07-14）**: 用户提出将 ⭐收藏/🕐历史/🪟窗口 从按钮下拉改为 Tab 式布局，搜索作为独立控件常驻，地址栏做文件系统实时补全，额外内容插槽（只读显示）。

## TL;DR

重构 QuickPathControl 为两行布局 + Tab 式路径速选 + 地址栏文件系统补全 + 只读额外内容插槽。QuickPathPreDialog 作为弹窗包装器复用同一控件。最终替换 Avalonia 版中 5 处路径选择场景的系统对话框调用。

**产出**:
- `Controls/QuickPathControl.axaml` + `.cs` — 核心组件（Tab + 搜索 + 地址栏 + 额外插槽）
- `Dialogs/QuickPathPreDialog.axaml` + `.cs` — 增强版弹窗包装器（替换现有实现）
- 修改 4 个宿主窗口内嵌 QuickPathControl
- 保留 3 处系统对话框场景不动

---

## 架构

```
核心控件：QuickPathControl (UserControl)
────────
┌──────────────────────────────────────────────┐
│ [⭐收藏▾] [🕐历史▾] [🪟窗口▾]   🔍 [搜索___] │  ← 行 1：Tab + 搜索
│                                              │
│ (Tab 内容区)                                  │  ← 当前 tab 的列表
│  ⭐ 收藏     → FavoritePathManager.GetAll()  │     点选→填入地址栏
│  🕐 历史     → PathHistoryManager.GetRecent() │
│  🪟 窗口     → ExplorerWindowTracker.XXX()    │
│  搜索打字    → 聚合过滤三个来源 + 名字匹配     │
│ ─────────────────────────────────────────── │
│ [AutoCompleteBox ___________________] [📁]  │  ← 行 2：地址栏
│  打字 → Directory.EnumerateDirectories()     │     文件系统实时补全
│ ─────────────────────────────────────────── │
│ ↓ ExtraContent (ContentPresenter)           │  ← 只读额外内容插槽
│   宿主注入，不注入则 Collapsed               │     解压场景：显示已选的目录结构
└──────────────────────────────────────────────┘
```

### 两个宿主

```
QuickPathControl（UserControl）
    ├── 内嵌到窗口（CompressSettingsWindow / ExtractSettingsWindow / etc.）
    │    路径双向同步，占用两行空间
    │
    └── QuickPathPreDialog（Window）
          包裹 QuickPathControl + 确定/取消/额外控件
          弹窗模式，与内嵌版行为一致
```

### 替换范围

| # | 位置 | 当前方式 | 替换方式 |
|---|------|---------|---------|
| 1 | QuickPathControl.[浏览] | StorageProvider 系统对话框 | 保留兜底，但用户主要走 ⭐🕐🪟+搜索+地址栏 |
| 2 | CompressSettingsWindow.BrowseOutput | SaveFilePickerAsync | 内嵌 QuickPathControl |
| 3 | CompressSettingsWindow.PickFolder | OpenFolderPickerAsync | QuickPathPreDialog 弹窗 |
| 4 | ExtractSettingsWindow.浏览解压路径 | OpenFolderPickerAsync | 内嵌 QuickPathControl |
| 5 | AddFavoriteDialog.浏览路径 | OpenFolderPickerAsync | QuickPathPreDialog 弹窗 |

**不替换**（保留系统对话框）:
- MainWindow → 打开压缩包（选具体文件，需要文件筛选器）
- CompressSettingsWindow.PickFiles（多选文件）
- MainWindow.GetOpenFilePaths（添加文件到压缩包）

### 额外内容插槽

```csharp
// QuickPathControl 开放属性
public object? ExtraContent
{
    get => ExtraContentSlot.Content;
    set => ExtraContentSlot.Content = value;
}
```

宿主编写只读内容注入，不注入则 ContentPresenter 自动 `Collapsed`。

---

## 架构决策记录

### ADR-1: Tab + 搜索一体化布局
- **状态**: 已确认
- **理由**: 按钮下拉每次只看一种类别，Tab 可随时切换 + 搜索框常驻输入即过滤，体验更统一
- **Tab 内容**: ⭐收藏 / 🕐历史 / 🪟窗口 各占一个 tab，切换后内容区显示对应列表，搜索框打字时跨三个来源聚合过滤

### ADR-2: 搜索只搜本地数据源，不扫文件系统
- **状态**: 已确认
- **理由**: 搜索范围 = 收藏 + 历史 + 窗口数据，不搜全盘（不是 Everything）。文件系统实时枚举从地址栏 AutoCompleteBox 做，互相不干扰

### ADR-3: ExtraContent 只读，宿主注入
- **状态**: 已确认
- **理由**: 解压场景需要显示已选文件目录结构，但不可修改。用户要修改就取消返回源窗口。留 ContentPresenter 接口通用化，未来其他场景复用

### ADR-4: 跨平台策略
- **状态**: 天然跨平台
- **理由**: FavoritePathManager + PathHistoryManager 纯 JSON 读写无平台依赖；ExplorerWindowTracker Windows 用 COM 其他平台 try/catch 返回空列表 → UI 层自动隐藏 🪟 tab；地址栏 AutoCompleteBox 的 `Directory.EnumerateDirectories` 跨平台

### ADR-5: CustomFilePickerDialog 暂时搁置
- **状态**: 搁置备选
- **理由**: 当前设计只做路径选择不做文件浏览，不叫"文件选择器"。以后如需完整文件浏览列表，可在 QuickPathControl 地址栏下方插入 ListBox 作为文件浏览区，架构上不冲突

---

## 实施任务

### Task 1: 重构 QuickPathControl（核心组件）

**文件**: `Controls/QuickPathControl.axaml` + `.axaml.cs`

**做什么**:
- 两行布局：行 1 = Tab 行（⭐收藏/🕐历史/🪟窗口 toggle + 搜索框），行 2 = AutoCompleteBox 地址栏 + [浏览] 按钮
- Tab 切换：点 ⭐ → 内容区显示 `FavoritePathManager.GetAll()` 列表；点 🕐 → 历史列表；点 🪟 → 窗口列表
- 搜索框：输入时实时聚合过滤三个来源（按路径名/名字匹配），来源用标签/颜色区分，点选 → 填入地址栏
- 地址栏 AutoCompleteBox：打字时 `Directory.EnumerateDirectories(parentDir, prefix + "*")` 实时补全，Take(20)
- [浏览] 按钮：保留 StorageProvider 系统对话框兜底
- `SelectedPath` 属性：双向绑定，地址栏内容变化时自动更新
- `ExtraContent` 属性：`ContentPresenter` + `Collapsed` 当 null
- 依赖属性: `IsFolderMode`, `IsFileOpenMode`, `FileTypeFilter`, `DefaultFileName`
- 选中路径后自动调用 `PathHistoryManager.Record(path)`
- 所有控件绑定主题资源键（Theme_*）

**不做**:
- 不搜整个文件系统
- 不做文件浏览列表（CustomFilePickerDialog 搁置）
- 不添加 NuGet 依赖
- 不修改打开压缩包/添加多文件等系统对话框场景

**参考**:
- WPF `QuickPathControl.xaml.cs` — ⭐🕐🪟 下拉和 FavoritePathManager/PathHistoryManager/ExplorerWindowTracker 集成逻辑（`MantisZip.UI/Controls/`）
- 现有 Avalonia `QuickPathControl.axaml` + `.cs` — 当前简化版结构
- `MantisZip.Core/Utils/FavoritePathManager.cs`
- `MantisZip.Core/Utils/PathHistoryManager.cs`
- `MantisZip.Core/Utils/ExplorerWindowTracker.cs`

---

### Task 2: 重构 QuickPathPreDialog（弹窗包装器）

**文件**: `Dialogs/QuickPathPreDialog.axaml` + `.axaml.cs`

**做什么**:
- 包裹 QuickPathControl + 确定/取消按钮 + 额外控件插槽（格式选项/解压选项等）
- 模式：`IsPickFolderMode`（选目录直接返回）/ `IsFileMode`（选目录后弹系统对话框选文件，兜底）
- 确定后返回 `SelectedPath`，取消返回 null
- 回车=确定，Esc=取消

**不做**:
- 不重复实现 QuickPathControl 已有的功能
- 不添加路径验证逻辑

---

### Task 3: CompressSettingsWindow 集成

**文件**: `Dialogs/CompressSettingsWindow.axaml` + `.axaml.cs`

**做什么**:
- 替换 BrowseOutput（保存路径）为内嵌 QuickPathControl
- 替换 PickFolder（选要压缩的文件夹）为 QuickPathPreDialog 弹窗
- PickFiles（选要添加的文件）暂时保留系统对话框
- ViewModel 中 `BrowseOutput` 回调改为读取 QuickPathControl.SelectedPath
- 与格式联动：格式切换时自动更新文件名扩展名

---

### Task 4: ExtractSettingsWindow 集成

**文件**: `Dialogs/ExtractSettingsWindow.axaml` + `.axaml.cs`

**做什么**:
- 替换浏览解压路径为内嵌 QuickPathControl
- 注入 ExtraContent：显示已选文件的目录结构（只读 TextBlock/TreeView）

---

### Task 5: AddFavoriteDialog 集成

**文件**: `Dialogs/AddFavoriteDialog.axaml` + `.axaml.cs`

**做什么**:
- 替换 [浏览] 按钮为 QuickPathPreDialog 弹窗（选目录模式）
- 保留名称输入框 + 路径文本框 + 确定/取消

---

### Task 6: QuickPathPreDialog 替换已有 QuickPathDialog 测试菜单调用

**文件**: `Views/MainWindow.axaml.cs`

**做什么**:
- 测试菜单中 `QuickPathDialog` 保持可用（作为弹窗测试入口）
- 确保新旧版本共存测试

---

## 不做的（记录） / 已知已信用的

- **CustomFilePickerDialog**（完整文件浏览）— 搁置，未来如需可在 QuickPathControl 地址栏下方插入 ListBox
- **ExplorerWindowTracker 跨平台** — Windows COM 枚举，其他平台返回空列表自动隐藏 🪟 tab，不做各平台兼容
- **MainWindow 打开压缩包/添加文件** — 保留系统对话框
- **CompressSettingsWindow.PickFiles**（多选文件添加）— 保留系统对话框
- 不在 WPF 项目（`MantisZip.UI`）上做任何改动
