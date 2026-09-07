# 预览"显示内容"开关 (Preview Show Content Toggle)

> **状态**: 📋 待实施 | **创建日期**: 2026-08-06 | **最近修正**: 2026-08-06
> **关联计划**: `result-preview-panel.md`（✅ 已完成的结果预览树，本计划叠加其上）、`preview-two-phase-loading.md`（✅ 已完成的预览两阶段加载，构建进度机制复用）
> **前置**: 无（Avalonia 直接实施；规则 11：新功能只进 Avalonia，WPF 仅通过 Core 逻辑被动受益，不做 UI 适配；WPF 的 AppSettings 不加字段，settings.json 反序列化忽略未知字段无兼容问题）
> **决策来源**: 2026-08-06 与用户 brainstorming 确认（形态=彻底隐藏不可展开、持久化=AppSettings、位置=ResultTreeView 工具栏、范围=压缩+解压、摘要=显示输出路径）

## TL;DR

压缩/解压设置窗口的预览树新增"显示内容"开关（ResultTreeView 工具栏）。关闭后只显示压缩包节点层（输出路径骨架），内容彻底隐藏且不可展开——同时**跳过 `BuildDirectoryNode` 的磁盘递归扫描**，大型源目录预览构建时间从 O(全部文件) 降到 O(压缩包数)。状态持久化到 `AppSettings.PreviewShowContent`。

---

## 1. 现状

### 1.1 预览树构建链路

```
CompressSettingsViewModel.BuildCompressPreview(:498)
  → BuildCompressPreviewCoreAsync(:501)  [后台线程 + _previewBuildVersion 版本号守卫 + 250ms 加载阈值]
    → ResultPreviewService.BuildCompressPreview(:170)
      → BuildSingleArchivePreview(:262)      [Manual/Combined]
      → BuildSeparateArchivesPreview(:334)   [Separate]
        → BuildDirectoryNode(:451)           [★ 递归磁盘扫描：GetDirectories/GetFiles，性能瓶颈]
```

```
ExtractSettingsViewModel.BuildExtractPreview(:116)
  → BuildExtractPreviewCoreAsync(:119)
    → ResultPreviewService.BuildExtractPreview(:27)
      → AddFolderNode / 文件节点构建
```

### 1.2 树结构形态

| 场景 | 结构 |
|------|------|
| 压缩 Manual/Combined | `root(父目录) → archiveNode(IsArchiveNode=true) → 源内容树` |
| 压缩 Separate | `root(虚拟根,空标签) → groupNode(输出目录) → archiveNode → 源内容` |
| 解压 | `destNode(目标目录) → 解压内容树` |

### 1.3 ResultTreeView 现状

- **9 个 StyledProperty**（`:23-58`）：Root / CompactMode / MaxItemsPerDirectory / MaxDepth / ShowFilteredGhosts / IsLoading / BuildProgress 等
- **显示规则**：`RebuildDisplayTree`（`:212`）→ `DeepCloneNode` 深拷贝原始树 → `ApplyDisplayRules`（`:281`，过滤→深度截断→数量截断）→ 恢复展开状态 → 汇总统计
- **工具栏**（ResultTreeView.axaml Row 0）：`CompactToggle` + `ExpandAllButton` + `LocateButton` + `FilterToggle` + 右侧摘要区
- **摘要栏**：`UpdateSummary`（`:397`）用 `_originalRoot` 统计文件数/总大小 → `Preview_Result_Summary`；冲突计数 `UpdateConflictCount`

---

## 2. 开关关闭后的行为（已确认）

| 场景 | 关闭前 | 关闭后 |
|------|--------|--------|
| 压缩 Manual/Combined | `父目录 → 📦xxx.zip → 源内容树` | `父目录 → 📦xxx.zip`（无子节点，不可展开） |
| 压缩 Separate | `虚拟根 → 分组目录 → 📦xxx.zip → 源内容` | `虚拟根 → 分组目录 → 📦xxx.zip` |
| 解压 | `目标目录 → 解压内容树` | `目标目录`（仅显示目标路径一行） |

- 内容**彻底隐藏**：子节点从树中移除，无展开箭头，无法展开
- **构建层跳过磁盘扫描**：关闭时 `BuildDirectoryNode` 完全不调用（性能收益核心）
- **摘要栏显示输出路径**：关闭时摘要显示压缩包完整路径（而非文件计数）

---

## 3. 实现方案

### 3.1 `AppSettings`（Avalonia Models/AppSettings.cs）

新增字段（放入**预览**分类）：

```csharp
/// <summary>结果预览树是否显示内容（false 时只显示压缩包/目标路径骨架）。默认 true。</summary>
public bool PreviewShowContent { get; set; } = true;
```

WPF 版 AppSettings 不加字段（规则 11；JSON 反序列化忽略未知字段）。

### 3.2 `ResultPreviewService`（构建层跳过，性能核心）

`BuildCompressPreview` 增加参数 `bool showContent = true`：

```csharp
public static PreviewTreeNode BuildCompressPreview(
    IReadOnlyList<string> sourcePaths,
    string? rootName = null,
    FileFilterCriteria? filter = null,
    CompressOutputMode outputMode = CompressOutputMode.Manual,
    string? outputPath = null,
    string format = "zip",
    bool keepOriginalExtension = false,
    bool showContent = true)
```

- `showContent=false` 时：
  - `BuildSingleArchivePreview`：**跳过** `foreach (var path in sourcePaths)` 的源子节点构建（不调 `BuildDirectoryNode`，不 `File.Exists` 检查）；archiveNode 仍创建（Name/FullPath/DisplayLabel/IsArchiveNode/ExistsAtDestination）
  - `BuildSeparateArchivesPreview`：分组与 archiveNode 照常构建，**跳过**每个 archive 的源子节点
  - `IsArchiveEmpty` / `NodeHasVisibleContent`：showContent=false 时不计算（archiveNode 无子节点，语义上不适用）

`BuildExtractPreview` 增加参数 `bool showContent = true`：

- `showContent=false` 时：仅创建 `destNode`（目标目录节点），**跳过** Phase 1（目录树）与 Phase 2（文件节点）全部构建

### 3.3 ViewModel 透传

**CompressSettingsViewModel**：
- 新增 `[ObservableProperty] private bool _showContent = true;`
- 构造函数从 `AppSettings.Load()` 读取 `PreviewShowContent` 初始化
- `OnShowContentChanged`：写回 `AppSettings.PreviewShowContent` + 调用 `BuildCompressPreviewCoreAsync(GetFilter(), newValue)`（重建时走构建层跳过逻辑）
- `BuildCompressPreview(FileFilterCriteria? filter = null)` 签名不变，内部把 `ShowContent` 传给 Service

**ExtractSettingsViewModel**：
- 同样新增 `_showContent` + `OnShowContentChanged` 写回 + 重建
- `BuildExtractPreview(entries, filter, checkExists)` 内部透传 `ShowContent`

### 3.4 `ResultTreeView`（显示层兜底 + 工具栏开关）

**新增 StyledProperty**：

```csharp
public static readonly StyledProperty<bool> ShowContentProperty =
    AvaloniaProperty.Register<ResultTreeView, bool>(nameof(ShowContent), true);

public bool ShowContent
{
    get => GetValue(ShowContentProperty);
    set => SetValue(ShowContentProperty, value);
}
```

静态构造注册变更回调 → `RebuildDisplayTree()`（与 CompactMode 同模式）。

**`ApplyDisplayRules` 兜底裁剪**（规则 0.5，置于现有规则 0 之前）：

```csharp
// 0.5 显示内容关闭时：清除所有压缩包节点的子节点（不可展开）
if (!ShowContent)
{
    foreach (var child in node.Children.OfType<PreviewTreeNode>().ToList())
    {
        if (child.IsArchiveNode)
            child.Children.Clear();
    }
}
```

> 说明：构建层已跳过时此兜底为空操作；但当 Root 由外部传入完整树（如解压树、未来其他调用方）时保证一致隐藏。

**摘要栏**：`UpdateSummary` 中 `!ShowContent` 时改显示输出路径：

- 收集 `_originalRoot` 下所有 `IsArchiveNode` 节点的 `FullPath`
  - 1 个 → `Preview_Result_OutputTo`（`输出到 {0}`）
  - 多个 → `Preview_Result_OutputToCount`（`输出到 {0} 个压缩包`，ToolTip 列路径）
- 无 archive 节点（解压树）→ 显示根节点 `FullPath`（目标目录），用 `Preview_Result_OutputTo`
- 冲突计数仍显示（archiveNode 的 `ExistsAtDestination` 冲突信息在隐藏内容时依然有价值）

**工具栏**：新增 `ShowContentToggle`（`ToggleButton`，与 CompactToggle 并列，PathIcon 图标）：

```xml
<ToggleButton x:Name="ShowContentToggle"
              IsChecked="{Binding ShowContent, RelativeSource={RelativeSource AncestorType=UserControl}}"
              Width="28" Height="{DynamicResource ControlHeightXs}"
              Background="{DynamicResource ThemeButtonBgBrush}"
              BorderBrush="{DynamicResource ThemeBorderBrush}">
  <PathIcon Data="{StaticResource IconEye}" Width="14" Height="14" />
</ToggleButton>
```

- `IsChecked` 绑定自身 `ShowContent` 属性（`RelativeSource AncestorType=UserControl`，与 CompactToggle 同模式）→ 属性变更自动触发 `RebuildDisplayTree`
- 外部宿主通过 `ShowContent="{Binding ShowContent}"` 双向绑定到 VM（VM 负责持久化+重建）
- ToolTip 在构造函数中设置：`ToolTip.SetTip(ShowContentToggle, LocalizationManager.T("Preview_Result_ShowContent"))`

**图标**：`AppIcons.axaml` 新增眼睛类 Geometry（`IconEye`，参考 Material Design "eye" 路径）；同步注册到 `IconTestViewModel.LoadAllIcons()`（规则 8）。

### 3.5 宿主窗口 XAML 绑定

`CompressSettingsWindow.axaml` 与 `ExtractSettingsWindow.axaml` 中的 `<controls:ResultTreeView>`：

```xml
ShowContent="{Binding ShowContent, Mode=TwoWay}"
```

窗口构造时 VM 已从 AppSettings 读入初始值 → 首次构建即按用户上次选择生成骨架树。

---

## 4. 交互与边界

| 场景 | 行为 |
|------|------|
| 开关切换 | VM `OnShowContentChanged` → 写 AppSettings + 重建（构建层跳过磁盘扫描，秒出；<250ms 不闪加载覆层，复用现有机制） |
| 过滤条件变更（压缩） | `BuildPreview()` 重建，透传当前 `ShowContent` |
| 解压目标路径变更 | `BuildExtractPreviewCoreAsync` 重建，透传当前 `ShowContent` |
| 摘要栏 | `!ShowContent` 时显示输出路径（见 3.4）；`ShowContent=true` 恢复文件计数/大小 |
| 冲突检测 | archiveNode 层 `ExistsAtDestination` 仍计算显示；隐藏内容时文件级冲突不可见（可接受，冲突计数仍提示） |
| `RefreshDisplay` | 不受影响（仍走 RebuildDisplayTree） |
| LocateButton / ExpandAllButton | 树只剩骨架时展开无意义——`ExpandAll` 对无子节点树为空操作，无需特判；Locate 仍可定位压缩包 |

## 5. i18n 新增 key（规则 13，成对添加 zh-CN/en）

| Key | zh-CN | en |
|-----|-------|----|
| `Preview_Result_ShowContent` | 显示内容 | Show content |
| `Preview_Result_HideContent` | 隐藏内容 | Hide content |
| `Preview_Result_OutputTo` | 输出到 {0} | Output to {0} |
| `Preview_Result_OutputToCount` | 输出到 {0} 个压缩包 | Output to {0} archives |

> 插入位置：`strings.zh-CN.json` / `strings.en.json` 文件头 `{` 之后，UTF-8 无 BOM + CRLF + 2 空格缩进。

## 6. 变更文件清单

| 文件 | 改动 |
|------|------|
| `Models/AppSettings.cs` | +`PreviewShowContent` 字段（预览分类） |
| `Services/ResultPreviewService.cs` | `BuildCompressPreview`/`BuildExtractPreview` +`showContent` 参数，跳过源子节点/内容构建 |
| `ViewModels/CompressSettingsViewModel.cs` | +`_showContent` + `OnShowContentChanged`（写回+重建）+ 透传 |
| `ViewModels/ExtractSettingsViewModel.cs` | 同上 |
| `Controls/ResultTreeView.axaml.cs` | +`ShowContentProperty` + `ApplyDisplayRules` 规则 0.5 + `UpdateSummary` 输出路径分支 + ToolTip |
| `Controls/ResultTreeView.axaml` | +`ShowContentToggle` 工具栏按钮 |
| `Dialogs/CompressSettingsWindow.axaml` | ResultTreeView +`ShowContent="{Binding ShowContent, Mode=TwoWay}"` |
| `Dialogs/ExtractSettingsWindow.axaml` | 同上 |
| `Resources/Icons/AppIcons.axaml` | +`IconEye` Geometry（规则 8） |
| `ViewModels/IconTestViewModel.cs` | +`IconEye` 注册（规则 8） |
| `Localization/strings.zh-CN.json` / `strings.en.json` | +4 key（规则 13） |

## 7. 验收标准（DoD）

1. 压缩设置窗口（Manual/Combined + Separate）关闭开关 → 树只剩压缩包骨架，无展开箭头；打开 → 完整内容恢复
2. 解压设置窗口关闭开关 → 树只剩目标目录一行；打开 → 内容恢复
3. 大型源目录（如 1000+ 文件）关闭开关后预览构建明显加速（跳过磁盘扫描）
4. 开关状态重启应用后保持（AppSettings 持久化）
5. 摘要栏：隐藏时显示输出路径，显示时恢复文件计数/大小；冲突计数两态均显示
6. 过滤条件/目标路径变更后重建，开关状态保持
7. 新增 4 个 i18n key 两文件成对、无遗漏硬编码
8. `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 通过，`lsp_diagnostics` 无错误
9. 图标测试窗口（IconTestWindow）显示 IconEye

## 8. 工时估算

**2-3h**（🟡中）

| 项 | 工时 |
|----|------|
| Service 层 `showContent` 参数 + 跳过逻辑 | 0.5h |
| VM 层属性 + 持久化 + 重建联动 | 0.5h |
| ResultTreeView 属性 + 兜底规则 + 摘要分支 | 0.5h |
| 工具栏 + 图标 + i18n | 0.5h |
| 窗口绑定 + 构建验证 + 手工测试 | 0.5h |
