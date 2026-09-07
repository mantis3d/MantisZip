# 解压/压缩结果预览面板（已完成）

> **状态**: ✅ 已完成（2026-07）
> 本文档记录**实际实现**（与最初计划有差异的部分已按真实代码修正）。原计划中的三项未实现内容保留在文末 [未实现项（后续可做）](#未实现项后续可做) 章节。

## 概述

在 ExtractSettingsWindow 和 CompressSettingsWindow 的右侧新增一个预览面板，实时显示解压/压缩后的文件目录树。实现一个可复用的 `ResultTreeView` 控件，并复用于 `CustomFilePickerDialog`（解压目标选择）和 `DragPreviewBitmapBuilder`（拖拽预览位图）。

> **后续补充（2026-08）— 预览=实际路径一致性**：`ResultPreviewService.BuildExtractPreview` 新增 `preserveFullPath`（默认 `true`）/`currentFolder`（默认 `""`）两个参数。解压目标选择场景（`CustomFilePickerDialog.ShowExtractFolderAsync`）由 `MainWindowViewModel.ExtractSelectedTo` 透传 `CurrentFolder` + 设置，预览树与实际解压（`SelectedItemsExtractService`）共用 `ExtractPathResolver`（Core/Utils）计算输出路径，输入相同故预览必然等于实际。详见 AGENTS.md「Extract path resolution」小节。

## 实际最终布局

两个设置窗口统一为三列布局，预览面板内部为三行（工具栏 / 摘要栏 / 树）：

```
┌───────────────────────────────┬─────┬────────────────────────────┐
│  TabControl (左列)            │  ▮  │  预览面板 (右列)            │
│  解压: 通用 / 筛选             │ 分  │ ┌────────────────────────┐ │
│  压缩: 通用/高级/密码/注释/筛选 │ 割  │ │ [⤢精简] [🗁] [◎] [⇕] 摘要 │ │ ← Row 0 工具栏
│                               │ 线  │ ├────────────────────────┤ │
│                               │     │ │ 23 个文件 · 156 MB      │ │ ← Row 1 摘要栏
│                               │     │ │ (⚠️ 3 个冲突 红色)       │ │
│                               │     │ ├────────────────────────┤ │
│  [开始] [取消]                 │     │ │ C:\Dest\               │ │ ← Row 2 树
│                               │     │ │ ├─ docs\ (3 项·1.2MB)  │ │
│                               │     │ │ │  report.docx          │ │
│                               │     │ │ │  invoice.pdf ⚠️       │ │
│                               │     │ │ └─ … 还有 3 个文件      │ │
└───────────────────────────────┴─────┴────────────────────────────┘
```

**窗口布局参数（两窗口一致）**：

| 属性 | 值 |
|------|-----|
| Width | 850 |
| MinWidth / MinHeight | 700 / 500 |
| SizeToContent | Height |
| CanResize | True |
| Grid RowDefinitions | `*,Auto`（内容区 + 底部按钮） |
| Grid ColumnDefinitions | `450(MinWidth 400), Auto, *` |
| GridSplitter | `Grid.Column=1`，`Width=5` |
| ResultTreeView | `Grid.Column=2`，`MinWidth=200` |

预览面板内的 `ResultTreeView` 绑定：

```xml
<controls:ResultTreeView x:Name="PreviewTree" Grid.Column="2" Grid.Row="0"
                         Root="{Binding PreviewRoot}"
                         CompactMode="{Binding PreviewCompactMode}"
                         MaxItemsPerDirectory="5" MaxDepth="5"
                         ShowFilteredGhosts="{Binding ShowFilteredGhosts}"
                         MinWidth="200" />
```

## 变更范围（实际）

| 文件 | 变更类型 |
|------|---------|
| `Controls/ResultTreeView.axaml` | 新建 - 可复用预览树控件（工具栏 + 摘要栏 + 树） |
| `Controls/ResultTreeView.axaml.cs` | 新建 - 显示树构建、折叠/展开、冲突标记、过滤可视化、主题切换刷新 |
| `Models/PreviewTreeNode.cs` | 新建 - 树节点数据模型（继承 `FolderNode`） |
| `Services/ResultPreviewService.cs` | 新建 - 构建预览树（无概念容器，root=目标目录/父目录/虚拟根） |
| `Services/DragPreviewBitmapBuilder.cs` | 复用 - 拖拽预览位图（ResultTreeView 离屏渲染） |
| `Dialogs/ExtractSettingsWindow.axaml` | 布局改造：三列 Grid + TabControl + 右侧预览面板 |
| `Dialogs/ExtractSettingsWindow.axaml.cs` | 预览面板联动：`SetEntries`、PropertyChanged 订阅、`checkExists:true` |
| `ViewModels/ExtractSettingsViewModel.cs` | 新增 `PreviewRoot`/`PreviewCompactMode`/`ShowFilteredGhosts`、刷新逻辑 |
| `Dialogs/CompressSettingsWindow.axaml` | 布局改造：三列 Grid + TabControl + 右侧预览面板 |
| `Dialogs/CompressSettingsWindow.axaml.cs` | 预览面板联动：`OnFileFilterChanged` → 重建 |
| `ViewModels/CompressSettingsViewModel.cs` | 预览属性 + 7 处刷新调用点 |
| `Converters/PreviewTreeConverters.cs` | 新建 - 预览树专用 5 个转换器 |
| `Resources/Icons/AppIcons.axaml` | 新增 `IconArchive`/`IconFolder`/`IconDocument`/`IconWarning`/`IconChevronDownUp`/`IconArrowExpandAll`/`IconLocation`/`IconFilter` |
| `Themes/ThemeLight.axaml` + `ThemeDark.axaml` | 新增 `ThemeHeaderBgBrush`、`ThemeSplitterBgBrush` |
| `Localization/strings.zh-CN.json` | 新增 15 个 `Preview_Result_*` key（其中 `Preview_Result_Title`/`Preview_Result_ConflictSuffix` 2 个未使用，见文末 i18n 清理附注） |
| `Localization/strings.en.json` | 同上 |
| `Views/MainWindow.axaml.cs` | `ShowExtractSettingsDialog` → `dialog.SetEntries(vm.GetAllRawItems())` |

## 详细设计

### 1. PreviewTreeNode 数据模型（实际）

`Models/PreviewTreeNode.cs`，继承 `FolderNode`（Core/Services/ArchiveTreeBuilder.cs）的 `Name`/`FullPath`/`Children`/`IsExpanded`/`IsSelected`，新增预览专用属性：

```csharp
public class PreviewTreeNode : FolderNode
{
    public string DisplayLabel { get; }        // 自定义显示名，未显式设置时回退 Name
    public long Size { get; set; }             // 文件大小（字节），目录为 0
    public string SizeDisplay { get; set; }    // 格式化大小
    public bool ExistsAtDestination { get; set; }  // 目标位置已存在同名 → 冲突标记
    public bool IsFilteredOut { get; set; }    // 被过滤排除（灰显/半透明）
    public bool IsArchiveNode { get; set; }    // 压缩包节点（归档图标 + 加粗放大）
    public bool IsArchiveEmpty { get; set; }   // 压缩包内全部被过滤 → 紫色 + 提示
    public bool IsDirectory { get; set; }      // 目录节点标记（区别于文件）
    public bool IsEmptyDirectory => IsDirectory && Children.Count == 0;  // 空目录 → 透明图标
    public int IndentDepth { get; set; }
    public bool[] AncestorHasNextSibling { get; set; }
    public int TotalDescendantCount { get; set; }      // 子孙总数（含文件+目录）
    public long TotalDescendantSize { get; set; }      // 子孙大小总和
    public int MaxChildDepth { get; set; }             // 子孙最大深度
    public string DirectoryInfoText { get; }           // "3 项 · 1.2 MB"（目录节点）
    public bool IsTruncated { get; set; }              // 截断占位节点
    public int TruncatedCount { get; set; }
    public int TruncatedDepth { get; set; }
    public bool IsDirectoryNode { get; }               // 目录判断（IsDirectory || 有子节点 || FullPath 空）
    public string? IconKey { get; }                    // 计算属性，见下
    public bool IsTruncatedNode => IsTruncated;
    public string? ForegroundKey { get; }              // 计算属性：空存档紫 / 冲突红 / null 主题色
    public double TextOpacity => IsFilteredOut ? 0.4 : 1.0;
    public void RaiseForegroundKeyChangedRecursive();  // 主题切换后递归刷新绑定
    public PreviewTreeNode ShallowClone();             // 浅拷贝（深拷贝用递归）
}
```

**`IconKey` 实际实现**（与最初计划差异：冲突分支改用 `!IsDirectory` 而非 `Children.Count == 0`；新增空目录分支）：

```csharp
public string? IconKey
{
    get
    {
        if (IsArchiveNode) return "IconArchive";
        if (IsTruncated) return null;                                    // 截断节点无图标（"…" 文本）
        if (ExistsAtDestination && !IsDirectory && !string.IsNullOrEmpty(FullPath)) return "IconWarning";
        if (IsEmptyDirectory || Children.Count > 0 || string.IsNullOrEmpty(FullPath)) return "IconFolder";
        return "IconDocument";
    }
}
```

**`ForegroundKey`**（后加，`af6e7e4` 主题修复配套）：

```csharp
public string? ForegroundKey
{
    get
    {
        if (IsArchiveEmpty) return "Purple";       // 过滤全排除 → 紫色
        if (ExistsAtDestination) return "ConflictRed";  // 冲突 → 红 (#FFD32F2F)
        return null;                               // null → 回退主题 ThemeTextPrimaryBrush
    }
}
```

### 2. ResultTreeView 控件（实际）

`Controls/ResultTreeView.axaml` + `.axaml.cs`——可复用 UserControl。

#### 2a. StyledProperties（实际 9 个）

```csharp
public static readonly StyledProperty<PreviewTreeNode?> RootProperty;            // 根节点
public static readonly StyledProperty<int> MaxItemsPerDirectoryProperty;         // 每目录最多平铺数，默认 5
public static readonly StyledProperty<int> MaxDepthProperty;                     // 最大深度，默认 5
public static readonly StyledProperty<bool> CompactModeProperty;                 // 精简模式，默认 true
public static readonly StyledProperty<bool> ShowFilteredGhostsProperty;          // 显示过滤灰显项，默认 false
public static readonly StyledProperty<string> SummaryTextProperty;               // 摘要文本（控件内部计算）
public static readonly StyledProperty<bool> ShowSummaryBarProperty;              // 显示摘要栏，默认 true
public static readonly StyledProperty<bool> IsLoadingProperty;                   // 加载覆层显示（构建中）
public static readonly StyledProperty<double> BuildProgressProperty;             // 构建进度（-1 = 不定进度条）
```

Root/CompactMode/MaxItemsPerDirectory/MaxDepth/ShowFilteredGhosts 任一变化 → `RebuildDisplayTree()`；`IsLoading`/`BuildProgress` 驱动加载覆层（`OnIsLoadingChanged`/`OnBuildProgressChanged`，见 2c）。

#### 2b. 布局（三行 Grid）

- **Row 0 工具栏**（`ThemeHeaderBgBrush` 底）：`CompactToggle`（IconChevronDownUp，ToolTip `Preview_Result_Compact`/`Preview_Result_Full` 按模式切换）、`ExpandAllButton`（IconArrowExpandAll，ToolTip `Tree_ExpandAll`）、`LocateButton`（IconLocation，ToolTip `Preview_Result_Locate`，初始禁用）、`FilterToggle`（IconFilter，ToolTip `Preview_Result_HideFiltered`/`Preview_Result_ShowFiltered` 按状态切换）、`SummaryTextBlock`（绑定 `SummaryText`）
- **Row 1 摘要栏**（`ShowSummaryBar` 控制可见，底部 1px `ThemeBorderBrush`）：`FileCountText`（"23 个文件"，i18n 键 `Preview_Result_FileCount`）、`TotalSizeText`（"156 MB"）、`ConflictCountText`（红色 `#FFD32F2F`，"⚠️ 3 个冲突"，0 时隐藏）
- **Row 2 树**：`TreeView SelectionMode="Multiple"`，节点模板 = PathIcon(`IconKey`→`GeometryResourceConverter`) + "…"（`IsTruncatedNode` 可见）+ `DisplayLabel`（`IsArchiveNode` 加粗 + 字号 14）+ `SizeDisplay` + `DirectoryInfoText`（`IsDirectoryNode` 可见）+ 冲突 ⚠️ PathIcon（`ExistsAtDestination` 可见，红色，ToolTip = 节点属性 `ConflictToolTip`，即 `Preview_Result_FileExists`）

所有按钮 ToolTip 均已在 ctor 中通过 `LocalizationManager.T(...)` 本地化（`CompactToggle`/`ExpandAllButton`/`LocateButton`/`FilterToggle`/`LoadingTextBlock`；CompactToggle 与 FilterToggle 在模式/状态切换时更新 ToolTip 文本）。

#### 2c. 核心逻辑（实际，含后加项）

**原始树 vs 显示树分离**（保留最初设计）：`_originalRoot` 保存 Service 构建的原始树；`RebuildDisplayTree()` 每次重建显示树：

1. **保存展开状态**：从当前 `DisplayNodes` 收集所有 `IsExpanded=true` 节点的 `FullPath`（`CollectExpandedPaths`）
2. **深拷贝**：`DeepCloneNode` 递归 `ShallowClone` 原始树
3. **应用显示规则**：`ApplyDisplayRules(displayRoot, 0)`
4. **恢复展开状态**：`RestoreExpandedPaths` 按 FullPath 还原
5. **虚拟根处理**：`displayRoot.DisplayLabel` 为空（Separate 模式）→ 子节点直接作为顶级项
6. **统计**：`UpdateSummary()` + `UpdateConflictCount()`

**`ApplyDisplayRules` 实际规则顺序**（与最初计划的差异：过滤移除在截断**之前**，且 Full 模式也递归生效）：

```csharp
// 0. 先移除过滤项（不受 CompactMode 影响，Full 模式也递归生效）
if (!ShowFilteredGhosts) node.Children = node.Children.Where(c => !(c as PreviewTreeNode)?.IsFilteredOut ?? true).ToList();

if (!CompactMode) { /* Full 模式：仅递归过滤子目录，跳过截断 */ return; }

// 1. 深度截断：depth >= MaxDepth → 替换为 "… 还有 {totalDeep} 层"
if (depth >= MaxDepth && node.Children.Count > 0) { ... }

// 2. 文件数截断：Children.Count > MaxItemsPerDirectory
//    → 保留前 MaxItemsPerDirectory 个 + "… 还有 N 项（D 个目录，F 个文件）"
//    → 纯文件时 "… 还有 N 个文件"
// 3. 递归处理未截断子节点
```

**截断节点 label 实际实现**（与计划差异：深度截断只有层数，**没有** "M 个文件"）：

```csharp
// 深度截断：仅层数
var depthLabel = LocalizationManager.T("Preview_Result_TruncatedDepth", totalDeep); // "… 还有 {0} 层"
// 数量截断：混合（目录+文件）或纯文件
var label = extraDirs > 0
    ? LocalizationManager.T("Preview_Result_TruncatedMixed", excess, extraDirs, extraFiles)
    : LocalizationManager.T("Preview_Result_TruncatedItems", excess);
```

**摘要统计**（后改为**原始树**统计，避免截断导致计数偏小；**始终排除过滤项**）：

```csharp
private void UpdateSummary()
{
    // 使用 _originalRoot（而非显示树），includeFiltered: false
    totalFiles = CountTotalFiles(_originalRoot, includeFiltered: false);
    totalSize  = CalculateTotalSize(_originalRoot, includeFiltered: false);
    SummaryText = LocalizationManager.T("Preview_Result_Summary", totalFiles, FormatSize(totalSize));
    FileCountText.Text = $"{totalFiles} 个文件";   // 硬编码中文
    TotalSizeText.Text = FormatSize(totalSize);
}
```

**冲突计数**：`CountConflicts` 递归统计 `ExistsAtDestination=true` 节点，**跳过过滤项**；`ConflictCountText` 红色 "⚠️ N 个冲突"，0 时 `IsVisible=false`。

**主题切换刷新**（`af6e7e4` 后加）：订阅 `ActualThemeVariantChanged` → `_originalRoot.RaiseForegroundKeyChangedRecursive()`，使 `NodeForegroundConverter` 重新从 `Application.Current.TryGetResource` 解析主题画刷。

**加载覆层**（`04229be`/`31e041e` 合入后加）：预览树异步构建期间显示加载覆层（`LoadingOverlay` + `LoadingProgressBar`，文案 `Preview_Result_Building`）：
- `IsLoading=true` → 显示覆层；`false` → 隐藏并复位进度条（避免下次残留进度）
- `BuildProgress`：`-1` → 不定进度条（`IsIndeterminate`）；`0–100` → 确定进度条（`Math.Clamp`）
- 驱动方：`ExtractSettingsViewModel.BuildExtractPreviewCoreAsync` 快构建（<250ms）不显示加载态，慢构建置 `IsPreviewBuilding=true`；`ResultPreviewService.BuildExtractPreview` 按条目数经 `IProgress<double>` 上报（1% 节流），进度回调经版本号守卫丢弃过期构建

**展开状态保持**（后加）：`CollectExpandedPaths` / `RestoreExpandedPaths` 按 `FullPath` 哈希集实现（见上文第 1/4 步）。

**工具栏动作**：
- `OnExpandAllClick` → `_originalRoot.ExpandAll()` + 重建
- `OnLocateClick` → `CollapseAll()` 保留根展开 → 对每个选中项 `ExpandAncestors`（按 `FullPath` 分段逐层展开，截断导致某层缺失则安全停止）
- `OnTreeSelectionChanged` → `LocateButton.IsEnabled = SelectedItems.Count > 0`

### 3. ResultPreviewService（实际，无概念容器）

`Services/ResultPreviewService.cs`——静态服务，从原始数据构建 PreviewTreeNode 树。

**重要差异（`a9659c4` 已实现）**：最初计划的「概念容器 root → 输出节点 → 内容」三层结构**已被移除**。实际结构：

- **解压**：root = **目标目录自身**（`Name`=目录名，`DisplayLabel`=完整路径，`IsExpanded=true`）
- **压缩 Manual/Combined**：root = **输出路径父目录**，其下挂压缩包节点
- **压缩 Separate**：root = **虚拟根**（`DisplayLabel=""`），子节点按输出目录分组，由 ResultTreeView 平铺展示

#### BuildExtractPreview — 实际签名（rootName 参数保留但未使用）

```csharp
public static PreviewTreeNode BuildExtractPreview(
    IEnumerable<ArchiveItem> entries,
    string destDir,
    string? rootName = null,        // 未使用，保留参数兼容
    bool checkExists = false,
    FileFilterCriteria? filter = null,
    IProgress<double>? progress = null,   // 后加（04229be）：构建进度上报（1% 节流），驱动加载覆层
    bool preserveFullPath = true,         // 后加（2026-08）：路径裁剪开关，与解压侧 ExtractPathResolver 同语义
    string currentFolder = "")            // 后加（2026-08）：当前浏览层锚点，路径裁剪用
```

构建流程：
1. root = `destDir` 自身（无概念容器层）
2. Phase 1：目录条目先行（`dirsAdded` HashSet 去重，`AddFolderNode` 逐段建链）；目录/文件路径统一经 `ExtractPathResolver.ResolveRelativePath` 计算，恶意路径条目 try-catch 跳过不毁整树
3. Phase 2：文件条目（`FindOrCreateParent` 建父目录链）：
   - `checkExists=true` → `File.Exists(Path.Combine(destDir, fullPath))` 标记冲突
   - `filter.IsActive` → `FileFilterMatcher.IsMatch` 不匹配标 `IsFilteredOut`
   - 进度：每处理 1% 上报一次（`progress.Report`，`totalFiles==0` 时跳过）
4. Phase 2b：`checkExists=true` → `MarkDirectoryConflicts`（目录级冲突检测）
5. Phase 3：`CalculateDescendantStats`（TotalDescendantCount/Size/MaxChildDepth）

#### BuildCompressPreview — 实际签名

```csharp
public static PreviewTreeNode BuildCompressPreview(
    IReadOnlyList<string> sourcePaths,
    string? rootName = null,
    FileFilterCriteria? filter = null,
    CompressOutputMode outputMode = CompressOutputMode.Manual,
    string? outputPath = null,
    string format = "zip",
    bool keepOriginalExtension = false)
```

**Manual / Combined**（单压缩包）：

```
C:\Output\                      ← root（输出路径父目录，DisplayLabel=完整路径）
└── 📦 backup.zip               ← archive 节点（IsArchiveNode，ExistsAtDestination=File.Exists）
    ├── 📁 Docs\                ← 源目录树（BuildDirectoryNode 递归磁盘）
    └── 📄 README.md
```

- 输出路径未指定时自动计算：`Path.Combine(首个源所在目录, ComputeArchiveName(...))`
- archive 节点 `IsArchiveEmpty = !NodeHasVisibleContent(archiveNode)`（所有文件被过滤 → 紫）

**Separate**（每源独立压缩包）：

```
(虚拟根 DisplayLabel="")        ← root，ResultTreeView 将其子节点平铺为顶级
├── 📁 C:\Users\Docs\           ← 输出父目录分组（DisplayLabel=完整路径）
│   ├── 📦 report.zip           ← archive 节点
│   └── 📦 invoice.zip
└── 📁 D:\Photos\vacation\      ← 不同输出目录
    └── 📦 IMG_001.zip
```

**`ComputeArchiveName`** 与 Core 层 `ComputeSeparateOutputPath` 保持一致：
- 目录源 → `GetFileName(trimmed)`；文件源 → `keepOriginalExt ? GetFileName : GetFileNameWithoutExtension`
- 扩展名：`tar.gz` 双段，否则 `.` + format

### 4. 窗口集成（实际联动方式）

#### ExtractSettingsWindow（code-behind 主导）

- `MainWindow.ShowExtractSettingsDialog` → `dialog.SetEntries(vm.GetAllRawItems())`（**多压缩包条目合并平铺**，未做来源分组）
- `SetEntries(IReadOnlyList<ArchiveItem>)` 缓存 `_entries`；窗口订阅 `ViewModel.PropertyChanged`，`DestinationPath`/`ConflictAction`/`OpenFolderAfterExtract` 变化时重建
- 重建调用：`ViewModel.BuildExtractPreview(_entries, filter, checkExists: true)` —— **解压端固定全量冲突检测**
- 预览构建异步化（`04229be`/`31e041e`）：VM `BuildExtractPreviewCoreAsync` 后台线程构建 + 版本号丢弃过期结果 + 250ms 加载阈值（快构建不闪加载态）→ `IsPreviewBuilding`/`PreviewBuildProgress` 驱动 ResultTreeView 加载覆层
- `FileFilterControl.FilterChanged` → 重建预览树 + 更新过滤统计
- `BrowseFolder` → `CustomFilePickerDialog.ShowExtractFolderAsync(this, _entries, ViewModel.DestinationPath)`
- **VM 的 `OnDestinationPathChanged` partial method 为空实现**——实际刷新逻辑在窗口 code-behind（计划中的「VM 缓存 entries + 自动重建」未按原样实现）

#### CompressSettingsWindow（VM partial method 主导）

- `OnFileFilterChanged`（code-behind）→ `ViewModel.BuildCompressPreview(filter)`
- VM 在 **7 处调用点**触发重建：输出模式切换、输出路径变化、格式切换、源文件列表变化、过滤变化、KeepOriginalExtension 切换等（`BuildCompressPreview` 调用点：324/435/447/468/524/532/637 行）
- `BrowseOutput` 默认扩展名：`tar.gz` → `.tar.gz`，`7z` → `.7z`，其余 → `.zip`

### 5. 复用场景（实际，含后加）

| 场景 | 使用方式 |
|------|---------|
| ExtractSettingsWindow 解压预览 | 内嵌 ResultTreeView，`checkExists:true` |
| CompressSettingsWindow 压缩预览 | 内嵌 ResultTreeView |
| `CustomFilePickerDialog` ExtractFolder 模式 | 800×620 布局；`BuildExtractPreview(_entries, destDir, checkExists:true)` + `SchedulePreviewRebuild` 防抖 ~300ms；`MaxItemsPerDirectory=8` / `MaxDepth=4`；`preserveFullPath`/`currentFolder` 由 `MainWindowViewModel.ExtractSelectedTo` 透传（预览=实际路径一致性，见概述后补） |
| `DragPreviewBitmapBuilder` 拖拽位图 | `DragDropItemExpander.ExpandItems` 后 `BuildExtractPreview(expanded, rootName, rootName, checkExists:false)`（**快速模式**）；外包一层空 `DisplayLabel` wrapper 根 → 实例化 ResultTreeView 离屏渲染 BGRA 位图给 `DragPreviewPopup` |

## 未实现项（后续可做）

以下三项在最初计划中设计但**未实现**，与已完成部分无冲突，可作独立后续任务：

### 1. 解压多压缩包按来源目录分组

- **现状**：`MainWindow` 调用 `SetEntries(vm.GetAllRawItems())` 将所有压缩包条目**合并平铺**为单棵树，无来源区分
- **计划设计**：第一层按压缩包来源目录分组 → 第二层压缩包名去扩展名（`IconArchive`）→ 第三层内容；冲突标记照常
- **涉及**：`ResultPreviewService.BuildExtractPreview` 签名需接收「压缩包→条目」分组数据；`SetEntries` 调用链

### 2. 点击截断占位符就地展开

- **现状**：截断占位符是静态 `IsTruncatedNode` 的 "…" 文本，无交互
- **计划设计**：点击占位符 → 取消该节点截断，在当前位置展开完整子节点（`OnTruncationClick` + `FindOriginalNode` 定位原始树）
- **涉及**：`ResultTreeView.axaml` 占位符加点击事件 + `RebuildDisplayTree` 局部重建

### 3. 快速/完整冲突检测双模式

- **现状**：`checkExists` 布尔参数固定——解压窗口/文件选择器 `true`（全量 `File.Exists`），拖拽位图 `false`；无用户开关
- **计划设计**：默认快速模式（仅目录级检测）→ 用户点击「检测冲突」按钮触发逐文件完整扫描（NAS/网络驱动器性能考虑）
- **涉及**：`ResultPreviewService` 两阶段检测 + ResultTreeView 工具栏按钮 + 窗口联动

### 附：i18n 清理（可选）

**2026-08-06 修正**：原记录"5 个 `Preview_Result_*` key 从未引用 + 5 处 ToolTip/文本硬编码中文"已大部分过时——硬编码中文已全部清除（ctor 统一走 `LocalizationManager.T`），5 个 key 中 `Preview_Result_Compact`/`Preview_Result_Full`/`Preview_Result_HideFiltered` 已随切换 ToolTip 引用。当前仍**未引用**仅 2 个：

- `Preview_Result_Title` — 0 引用
- `Preview_Result_ConflictSuffix` — 0 引用

可清理（从 strings 两文件删除）或补用，二选一。

## 实施记录

### 提交历史（按时间顺序）

| 提交 | 内容 |
|------|------|
| `0e4a06a` | 结果预览面板 + ResultTreeView 可复用控件（初版） |
| `fb097a9` | Phase 2 emoji→PathIcon：预览工具栏/冲突对话框/文件树/过滤栏 |
| `5d6d573` | ShowFilteredGhosts 切换 + 定位到选中按钮 |
| `b25f7c1` | 连接 FileFilter → 预览树 + 目录节点信息 |
| `2697e3f` | docs: 更新计划文档（工具栏扩展设计）+ PROGRESS.md |
| `b0a89ac` | PreviewTreeNode 新增 IsArchiveNode + IconArchive |
| `a9659c4` | 移除概念容器，改为压缩包壳节点结构 |
| `37f75f7` | 新增 IsDirectory / IsEmptyDirectory 标记 + 样式转换器 |
| `acb72cb` | 压缩包加粗放大、冲突红字、空目录透明图标 |
| `f0f9e5c` | 过滤全排除预览树紫色 + 压缩时弹提示 + Manual 自动填充路径 |
| `3ff63fe` | 设置窗口布局统一 + ResultTreeView 冲突/过滤/摘要计数修正 |
| `135db5e` | 菜单 Toggle 改用 ToggleIconBox（无关重构，同批合入） |
| `af6e7e4` | 暗色模式文件名黑色修复：NodeForegroundConverter 动态解析主题画刷 |

### 相关窗口侧提交

- `a7debac` — ResultTreeView 预览面板宽度可调（GridSplitter + `ResizeBehavior="PreviousAndNext"`）
- `a23512a` — 虚拟根支持 + 移除 destDir 跳过 hack
- `69ff2be` — ExtractSettings 默认宽度/最小尺寸调整
- `9d6fdd7` — 输出路径无效检测 + 预览树显示提示 + 窗口超出屏幕自动上移
- `fde51e2` — Compress/ExtractSettings 接入文件过滤
- `6f27b11` — 拖拽直接解压（含 DragPreviewBitmapBuilder 复用）

### PROGRESS.md 对应条目

- 2026-07-25 — 预览树工具栏扩展：过滤显示切换 + 定位选中 + 过滤连接解压预览
- 2026-07-28 — 压缩设置加密面板对齐 + ResultTreeView 宽度可调
- 2026-07-31 — 暗色模式预览树文件名黑色修复

> **注**：`04229be`（解压预览构建进度上报 — 可选 IProgress 参数逐文件上报 1% 节流）与 `31e041e`（ResultTreeView 加载覆层 — IsLoading/BuildProgress 属性 + 进度条不定/确定自动切换 + `Preview_Result_Building` 文案）已随 `AvaloniaFromWpf` 分支合入（2026-07-31），当前 HEAD 已包含。二者为本文档实现的一部分，正文 §2 已补记（见 2a 的 IsLoadingProperty/BuildProgressProperty 与 2c 的加载覆层逻辑）。
