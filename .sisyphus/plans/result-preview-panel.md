# 解压/压缩结果预览面板

## 概述

在 ExtractSettingsWindow 和 CompressSettingsWindow 的右侧新增一个预览面板，实时显示解压/压缩后的文件目录树。实现一个可复用的 `ResultTreeView` 控件，也用于拖拽导出等场景。

## 最终布局

```
┌──────────────────────────────────────────────┐
│  TabControl (左)        │  预览面板 (右)       │
│                         │                     │
│ ┌─────────────────┐     │ ┌─────────────────┐ │
│ │ 通用 │ 密码 │ 注释│     │ │ 📂 精简/完整  ▲ │ │
│ │                 │     │ │ 📊 23文件/156MB │ │
│ │  目标路径：___   │     │ │                 │ │
│ │  冲突：___      │     │ │ C:\Dest\        │ │
│ │                 │     │ │ ├── docs\       │ │
│ └─────────────────┘     │ │ │   report.docx │ │
│                         │ │ │   invoice.pdf⚠️│ │
│ [开始]  [取消]           │ │ └── … 3个文件   │ │
│                         │ └─────────────────┘ │
└──────────────────────────────────────────────┘
```

## 变更范围

| 文件 | 变更类型 |
|------|---------|
| `Controls/ResultTreeView.axaml` | 新建 - 可复用预览树控件 |
| `Controls/ResultTreeView.axaml.cs` | 新建 - 控件逻辑、折叠展开、冲突标记、过滤可视化 |
| `Models/PreviewTreeNode.cs` | 新建 - 树节点数据模型（继承/扩展 FolderNode）；新增 `IsArchiveNode` |
| `Services/ResultPreviewService.cs` | 新建 - 构建预览树逻辑；重构压缩/解压预览 |
| `Resources/Icons/AppIcons.axaml` | 新增 - `IconArchive` 压缩包图标 |
| `Dialogs/ExtractSettingsWindow.axaml` | 布局改造：加 TabControl + 右侧预览面板 |
| `Dialogs/ExtractSettingsWindow.axaml.cs` | 预览面板联动设置 |
| `ViewModels/ExtractSettingsViewModel.cs` | 新增预览树属性、刷新逻辑 |
| `Dialogs/CompressSettingsWindow.axaml` | 布局改造：加右侧预览面板 |
| `Dialogs/CompressSettingsWindow.axaml.cs` | 预览面板联动设置 |
| `ViewModels/CompressSettingsViewModel.cs` | 新增预览树属性、刷新逻辑；`BuildCompressPreview` 传输出模式/路径/格式 |
| `Localization/strings.zh-CN.json` | 新增 i18n key |
| `Localization/strings.en.json` | 新增 i18n key |

## 详细设计

### 1. PreviewTreeNode 数据模型

在 `Models/PreviewTreeNode.cs` 中定义。继承 `FolderNode`（Core/Services/ArchiveTreeBuilder.cs）的 Name/FullPath/Children/IsExpanded/IsSelected 属性，新增预览专用属性：

```csharp
public class PreviewTreeNode : FolderNode
{
    /// <summary>该节点在目标位置是否已存在文件</summary>
    public bool ExistsAtDestination { get; set; }

    /// <summary>该节点是否被过滤排除（显示为灰色）</summary>
    public bool IsFilteredOut { get; set; }

    /// <summary>是否为压缩包节点（显示归档图标）</summary>
    public bool IsArchiveNode { get; set; }

    /// <summary>子孙节点总数（用于折叠预览）</summary>
    public int TotalDescendantCount { get; set; }

    /// <summary>子孙最大深度</summary>
    public int MaxChildDepth { get; set; }

    /// <summary>是否被截断显示（超过 MaxItemsPerDirectory）</summary>
    public bool IsTruncated { get; set; }

    /// <summary>被截断的额外条目数</summary>
    public int TruncatedCount { get; set; }

    /// <summary>被截断的额外层数</summary>
    public int TruncatedDepth { get; set; }
}
```

**`IconKey` 属性新增分支：**

```csharp
public string? IconKey
{
    get
    {
        if (IsArchiveNode) return "IconArchive";
        if (IsTruncated) return null;
        if (ExistsAtDestination && Children.Count == 0 && !string.IsNullOrEmpty(FullPath)) return "IconWarning";
        if (Children.Count > 0 || string.IsNullOrEmpty(FullPath)) return "IconFolder";
        return "IconDocument";
    }
}
```

**`IconArchive`** 新增在 `Resources/Icons/AppIcons.axaml`，一个简洁的压缩包 PathIcon（带拉链的文档轮廓）。

### 2. ResultTreeView 控件

`Controls/ResultTreeView.axaml` + `.axaml.cs`——可复用的 UserControl。

#### 2a. 属性

```csharp
public partial class ResultTreeView : UserControl
{
    // ── 数据 ──
    /// <summary>根节点（树的起点，含所有子节点）</summary>
    public static readonly StyledProperty<PreviewTreeNode?> RootProperty =
        AvaloniaProperty.Register<ResultTreeView, PreviewTreeNode?>(nameof(Root));

    // ── 显示选项 ──
    /// <summary>每个目录最多平铺文件数（超出的折叠成 … 还有 N 个）</summary>
    public static readonly StyledProperty<int> MaxItemsPerDirectoryProperty =
        AvaloniaProperty.Register<ResultTreeView, int>(nameof(MaxItemsPerDirectory), 5);

    /// <summary>最大显示深度（超出的折叠成 … 还有 N 层）</summary>
    public static readonly StyledProperty<int> MaxDepthProperty =
        AvaloniaProperty.Register<ResultTreeView, int>(nameof(MaxDepth), 5);

    /// <summary>是否启用精简模式</summary>
    public static readonly StyledProperty<bool> CompactModeProperty =
        AvaloniaProperty.Register<ResultTreeView, bool>(nameof(CompactMode), true);

    /// <summary>是否显示被过滤排除的文件（灰色显示）</summary>
    public static readonly StyledProperty<bool> ShowFilteredGhostsProperty =
        AvaloniaProperty.Register<ResultTreeView, bool>(nameof(ShowFilteredGhosts), false);

    // ── 统计 ──
    public static readonly StyledProperty<string> SummaryTextProperty =
        AvaloniaProperty.Register<ResultTreeView, string>(nameof(SummaryText), "");

    /// <summary>统计概要："📂 23 个文件 / 156 MB · 12 张图片 · 8 个文档"</summary>
    // 由控件内部计算
}
```

#### 2b. 布局（ResultTreeView.axaml）

```xml
<UserControl …>
    <Grid RowDefinitions="Auto,Auto,*">
        <!-- Row 0: Top toolbar -->
        <Border Grid.Row="0" Padding="8,4"
                Background="{DynamicResource ThemeHeaderBgBrush}">
            <StackPanel Orientation="Horizontal" Spacing="4">
                <ToggleButton IsChecked="{Binding CompactMode, RelativeSource={RelativeSource AncestorType=UserControl}}">
                    <TextBlock Text="📂" />
                </ToggleButton>
                <TextBlock Text="{Binding SummaryText, RelativeSource={RelativeSource AncestorType=UserControl}}"
                           FontSize="12"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"
                           VerticalAlignment="Center" />
            </StackPanel>
        </Border>

        <!-- Row 2: Tree -->
        <ScrollViewer Grid.Row="1"
                      Background="{DynamicResource ThemeSurfaceBgBrush}">
            <TreeView ItemsSource="{Binding Nodes, RelativeSource={RelativeSource AncestorType=UserControl}}"
                      BorderThickness="0"
                      Background="Transparent">
                <TreeView.Styles>
                    <Style Selector="TreeViewItem">
                        <Setter Property="IsExpanded" Value="{Binding IsExpanded}" />
                    </Style>
                </TreeView.Styles>
                <TreeView.ItemTemplate>
                    <TreeDataTemplate ItemsSource="{Binding Children}">
                        <!-- 节点模板：区分普通项 / 截断项 / 冲突项 / 过滤项 -->
                        <ContentControl Content="{Binding}">
                            <ContentControl.Styles>
                                <!-- Normal item -->
                                <Style Selector="ContentControl">
                                    <Setter Property="ContentTemplate">
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" Spacing="4">
                                                <TextBlock Text="{Binding Icon}" FontSize="14" />
                                                <TextBlock Text="{Binding DisplayName}" />
                                            </StackPanel>
                                        </DataTemplate>
                                    </Setter>
                                </Style>
                                <!-- Truncation placeholder "… 还有 N 个" -->
                                ...
                                <!-- Conflict item "filename⚠️" -->
                                ...
                                <!-- Filtered-out item (gray italic) -->
                                ...
                            </ContentControl.Styles>
                        </ContentControl>
                    </TreeDataTemplate>
                </TreeView.ItemTemplate>
            </TreeView>
        </ScrollViewer>
    </Grid>
</UserControl>
```

#### 2c. 核心逻辑（ResultTreeView.axaml.cs）

**构建显示树**：接受 `PreviewTreeNode` 原始树，根据 `CompactMode` / `MaxItemsPerDirectory` / `MaxDepth` / `ShowFilteredGhosts` 生成一个「显示树」。原始树和显示树分离，切换精简模式时只重建显示树，不重建原始树。

```csharp
/// <summary>
/// 从原始树生成显示树（应用折叠、过滤等规则）
/// </summary>
private List<PreviewTreeNode> BuildDisplayTree(PreviewTreeNode? root)
{
    if (root == null) return new();
    
    var displayRoot = CloneNode(root);
    ApplyDisplayRules(displayRoot, currentDepth: 0);
    return new() { displayRoot };
}

private void ApplyDisplayRules(PreviewTreeNode node, int depth)
{
    // 1. 深度截断
    if (CompactMode && depth >= MaxDepth && node.Children.Count > 0)
    {
        var totalDeep = CountDeepDescendants(node);
        node.Children.Clear();
        node.Children.Add(new PreviewTreeNode
        {
            DisplayName = $"… 还有 {totalDeep} 层，{CountTotalFiles(node)} 个文件",
            IsTruncated = true,
            TruncatedDepth = totalDeep,
            Icon = "…"
        });
        return;
    }

    // 2. 文件数截断
    if (CompactMode && node.Children.Count > MaxItemsPerDirectory)
    {
        var excess = node.Children.Count - MaxItemsPerDirectory;
        var truncated = node.Children.Skip(MaxItemsPerDirectory).ToList();
        node.Children = node.Children.Take(MaxItemsPerDirectory).ToList();
        
        // 统计被截断的文件/目录数
        var extraFiles = truncated.Count(c => !c.IsDirectory);
        var extraDirs = truncated.Count(c => c.IsDirectory);
        var label = extraDirs > 0
            ? $"… 还有 {excess} 项（{extraDirs} 个目录，{extraFiles} 个文件）"
            : $"… 还有 {excess} 个文件";
        
        node.Children.Add(new PreviewTreeNode
        {
            DisplayName = label,
            IsTruncated = true,
            TruncatedCount = excess,
            Icon = "…"
        });
    }

    // 3. 过滤项灰显（由 IsFilteredOut 决定, 由外部设置）
    //    如果 ShowFilteredGhosts = false，直接移除 IsFilteredOut 的子节点
    if (!ShowFilteredGhosts)
    {
        node.Children = node.Children.Where(c => !c.IsFilteredOut).ToList();
    }

    // 4. 递归子节点
    foreach (var child in node.Children.Where(c => !c.IsTruncated))
        ApplyDisplayRules(child, depth + 1);
}
```

**点击截断占位符展开**：
```csharp
// 当用户点击 "… 还有 N 个" 时，移除截断占位符并重新展开
private void OnTruncationClick(PreviewTreeNode placeholder)
{
    // 重新生成该节点的完整子节点列表（取消截断）
    var fullNode = FindOriginalNode(placeholder);
    RebuildNode(fullNode);
}
```

**冲突标记**：在构建原始树时，对每个文件节点检查目标路径下是否存在同名文件。逻辑放在 `ResultPreviewService` 中。

**统计摘要**：
```csharp
private void UpdateSummary(PreviewTreeNode root)
{
    var totalFiles = CountFiles(root);
    var totalSize = CalculateTotalSize(root); // 需要从原始数据传入size信息
    // … 按扩展名分组统计 …
    SummaryText = $"📂 {totalFiles} 个文件 / {FormatUtil.FormatSize(totalSize)}";
}
```

### 3. ResultPreviewService

`Services/ResultPreviewService.cs`——从原始数据构建 PreviewTreeNode 树。

两个方法均采用「概念容器 root → 输出节点 → 内容」的层级结构：

```
┌─ root (概念容器，FullPath = "")
├─ 输出节点（压缩包 / 目标目录，完整路径，冲突检测）
│  └─ 内容（源文件/提取条目的目录树）
```

#### BuildCompressPreview — 构建压缩预览树

根据 `CompressOutputMode` 分两种布局：

**Manual / Combined（单压缩包）：**

```
📦 压缩内容                      ← root
└── 📦 archive.zip               ← 输出压缩包，IsArchiveNode=true, ExistsAtDestination=File.Exists(输出路径)
    ├── 📁 Docs\                 ← 源文件目录树
    │   ├── report.docx
    │   └── invoice.pdf
    └── 📁 vacation\
        └── IMG_001.jpg
```

**Separate（每源文件独立压缩包）：**

```
📦 压缩内容
├── 📁 C:\Users\Docs\            ← 按输出父目录分组，DisplayLabel=完整路径
│   ├── 📦 report.zip  ⚠️        ← 已存在冲突
│   │   └── report.docx
│   └── 📦 invoice.zip
│       └── invoice.pdf
└── 📁 D:\Photos\vacation\       ← 不同输出目录
    └── 📦 IMG_001.zip
        └── IMG_001.jpg
```

**压缩包名称计算（与 Core 层 `ComputeSeparateOutputPath` 保持一致）：**

```csharp
private static string ComputeArchiveName(string sourcePath, string format)
{
    string baseName;
    if (Directory.Exists(sourcePath))
        baseName = Path.GetFileName(sourcePath.TrimEnd('\\'));
    else
        baseName = Path.GetFileNameWithoutExtension(sourcePath);
    string ext = format == "tar.gz" ? ".tar.gz" : "." + format;
    return baseName + ext;
}
```

**方法签名：**

```csharp
public static PreviewTreeNode BuildCompressPreview(
    IReadOnlyList<string> sourcePaths,
    string? rootName = null,
    FileFilterCriteria? filter = null,
    CompressOutputMode outputMode = CompressOutputMode.Manual,
    string? outputPath = null,
    string format = "zip")
```

**构建逻辑：**

1. 根据 `outputMode` 和源路径计算各压缩包的完整输出路径
   - Manual：单路径 = `outputPath`
   - Combined：单路径 = 自动计算（`RefreshOutputPathState` 逻辑）
   - Separate：逐源路径调用 `ComputeArchiveName` + 父目录 → 多路径
2. Separate 模式下按输出路径的 `Path.GetDirectoryName` 分组
3. 对每个压缩包调用 `File.Exists(输出路径)` → 设置 `ExistsAtDestination`
4. 创建 `PreviewTreeNode { IsArchiveNode = true }`，源文件/目录作为子节点
5. 调用 `CalculateDescendantStats(root)` 统计

#### BuildExtractPreview — 构建解压预览树

当前 tree 缺少目标目录节点，root 直接挂载提取内容。改为与压缩统一：

**Normal 模式解压到 D:\Dest\：**

```
📦 解压结果                      ← root（概念容器）
└── 📁 D:\Dest\                  ← 目标目录节点（完整路径，DirectoryInfoText 显示统计）
    ├── 📁 Docs\
    │   ├── report.docx  ⚠️      ← 文件冲突检测（已实现）
    │   └── invoice.pdf
    └── 📁 vacation\
        └── IMG_001.jpg
```

**Smart 模式解压到 D:\Dest\archive_name\：**

```
📦 解压结果
└── 📁 D:\Dest\archive_name\     ← DestinationPath 已含智能子目录
    ├── 📁 Docs\
    │   ├── report.docx  ⚠️
    │   └── invoice.pdf
    └── 📁 vacation\
        └── IMG_001.jpg
```

**方法签名（与当前一致，只改内部逻辑）：**

```csharp
public static PreviewTreeNode BuildExtractPreview(
    IEnumerable<ArchiveItem> entries,
    string destDir,
    string? rootName = null,
    bool checkExists = false,
    FileFilterCriteria? filter = null)
```

**构建逻辑（调整项）：**

1. root 的 `DisplayLabel` 从 `destDir` 改为固定"解压结果"
2. 新增目标目录子节点：`PreviewTreeNode { FullPath = destDir, DisplayLabel = destDir }`
3. 原有 tree 构建逻辑改为挂载到目标目录节点下，而非 root 下
4. 文件冲突检测逻辑不变（`File.Exists(Path.Combine(destDir, fullPath))`）

### 4. ExtractSettingsWindow 布局改造

当前是单列纯滚动布局。改为两列 Grid：

```xml
<Window ...>
    <Grid RowDefinitions="*,Auto" ColumnDefinitions="*,Auto">
        <!-- Left: TabControl -->
        <TabControl Grid.Column="0" Grid.Row="0">
            <TabItem Header="通用">
                <!-- 现有内容移到此处 -->
            </TabItem>
            <TabItem Header="过滤">
                <!-- 文件过滤设置（来自 file-filter-feature 计划） -->
            </TabItem>
        </TabControl>
        
        <!-- Right: Preview panel -->
        <GridSplitter Grid.Column="1" Grid.Row="0"
                      Width="5" />
        <controls:ResultTreeView Grid.Column="2" Grid.Row="0"
                                 x:Name="PreviewTree"
                                 Root="{Binding PreviewRoot}"
                                 CompactMode="{Binding PreviewCompactMode}"
                                 MaxItemsPerDirectory="5" MaxDepth="5"
                                 ShowFilteredGhosts="{Binding ShowFilteredGhosts}"
                                 Width="280" />

        <!-- Bottom: Buttons -->
        <StackPanel Grid.Row="1" Grid.ColumnSpan="3" ...>
            <Button Command="{Binding ExtractCommand}" />
            <Button Command="{Binding CancelCommand}" />
        </StackPanel>
    </Grid>
</Window>
```

### 5. CompressSettingsWindow 布局改造

同样加右侧列，Tab 保持现有 3 个不变，右侧加 ResultTreeView。

```xml
<Window ...>
    <Grid RowDefinitions="*,Auto" ColumnDefinitions="*,Auto">
        <!-- Left: 现有 TabControl（保持不变） -->
        <TabControl Grid.Column="0" Grid.Row="0" ... />
        
        <!-- Right: Preview panel -->
        <GridSplitter Grid.Column="1" ... />
        <controls:ResultTreeView Grid.Column="2" ... />
        
        <!-- Bottom -->
        <StackPanel Grid.Row="1" Grid.ColumnSpan="3" ... />
    </Grid>
</Window>
```

### 6. 窗口尺寸调整

两个窗口当前 `CanResize="False"`、`SizeToContent="Height"`。增加右侧面板后：
- 设为 `CanResize="True"`
- 固定宽度增加到 ~800-850 以容纳左侧 Tab + 右侧 280px 面板
- 允许用户拖拽 GridSplitter 调整左右比例
- 窗口高度仍需自适应内容

### 7. i18n Key

```json
// strings.zh-CN.json
"Preview_Result_Title": "预览",
"Preview_Result_Compact": "精简",
"Preview_Result_Full": "完整",
"Preview_Result_Summary": "📂 {0} 个文件 / {1}",
"Preview_Result_TruncatedItems": "… 还有 {0} 个文件",
"Preview_Result_TruncatedMixed": "… 还有 {0} 项（{1} 个目录，{2} 个文件）",
"Preview_Result_TruncatedDepth": "… 还有 {0} 层",
"Preview_Result_ConflictSuffix": "⚠️",
"Preview_Result_ShowFiltered": "显示过滤项",

// strings.en.json
"Preview_Result_Title": "Preview",
"Preview_Result_Compact": "Compact",
"Preview_Result_Full": "Full",
"Preview_Result_Summary": "📂 {0} files / {1}",
"Preview_Result_TruncatedItems": "… {0} more files",
"Preview_Result_TruncatedMixed": "… {0} more items ({1} dirs, {2} files)",
"Preview_Result_TruncatedDepth": "… {0} more levels",
"Preview_Result_ConflictSuffix": "⚠️",
"Preview_Result_ShowFiltered": "Show filtered",
```

## 压缩端预览设计（Separate 模式）

选择「每项独立压缩包」时，预览树按输出目录分组，每组下是压缩包壳节点：

```
📦 输出位置
├── 📁 E:\tool\              ← 输出目录（来源路径的父目录）
│   ├── 🗜 src.zip           ← 压缩包壳（IsArchive=true, icon=IconArchive）
│   │   ├── 📁 components\   ← 压缩包内目录
│   │   ├── 📁 utils\
│   │   └── 📁 styles\
│   └── 🗜 README.zip
│       └── README.md
└── 📁 E:\download\          ← 不同输出目录
    └── 🗜 config.json.zip
        └── config.json
```

- 同一来源目录的压缩包归在一个输出目录下
- 压缩包壳节点使用 `IconArchive`（区别于 `IconFolder`）
- Combined / Manual 模式保持当前单棵树不变
- 刷新时机：输出模式切换、源文件列表变化、过滤条件变化

## 解压端预览设计（多压缩包）

多个压缩包解压时，按来源目录分组：

```
📁 解压到: E:\Extracted\
├── 📁 D:\downloads\              ← 压缩包来源目录
│   ├── 🗜 photo_albums\          ← 压缩包名去扩展名（解压后根目录）
│   │   ├── 📁 2024\
│   │   │   └── DSC_001.jpg  ⚠️  ← 冲突标记
│   │   └── README.txt
│   └── 🗜 documents\
│       └── report.docx
└── 📁 E:\backup\                 ← 不同来源目录
    └── 🗜 project\
        └── src\
```

- 第一层：压缩包来源目录（相同路径归一起）
- 第二层：压缩包名去扩展名（解压后根目录），图标 `IconArchive`
- 第三层：压缩包内实际的目录/文件
- 冲突标记 `ExistsAtDestination` 显示 ⚠️
- 刷新时机：目标目录变化、压缩包列表变化、过滤条件变化

## 文件过滤集成

两端均已嵌入 `FileFilterEditor`（Tab: 筛选），需要连接：

1. `FileFilterControl.FilterChanged` 事件 → 触发预览重建
2. `FileFilterControl.GetFilter()` → 获取 `FileFilterCriteria`
3. 构建树后对每个文件节点检查匹配条件，不匹配的标记 `IsFilteredOut = true`
4. `ResultTreeView` 内根据 `ShowFilteredGhosts` 灰显或隐藏
5. 切换过滤预设/修改条件/启用关闭过滤均触发刷新

## 实施步骤

| 步骤 | 内容 | 文件 |
|------|------|------|
| 1 | ✅ PreviewTreeNode 模型 | `Models/PreviewTreeNode.cs` |
| 2 | ✅ ResultPreviewService（构建原始树 + 冲突检测） | `Services/ResultPreviewService.cs` |
| 3 | ✅ ResultTreeView 控件（显示树构建 + 折叠 + 冲突标记 + 摘要） | `Controls/ResultTreeView.axaml` / `.cs` |
| 4 | ✅ CompressSettingsWindow 嵌入 ResultTreeView | `Dialogs/CompressSettingsWindow.axaml` |
| 5 | ✅ ExtractSettingsWindow 嵌入 ResultTreeView | `Dialogs/ExtractSettingsWindow.axaml` |
| 6 | ✅ 两 ViewModel 已有 PreviewRoot / PreviewCompactMode / ShowFilteredGhosts | `ViewModels/CompressSettingsViewModel.cs`, `ViewModels/ExtractSettingsViewModel.cs` |
| 7 | 🔲 PreviewTreeNode 新增 `IsArchive` 属性 + `IconKey` 返回 `"IconArchive"` | `Models/PreviewTreeNode.cs` |
| 8 | 🔲 AppIcons.axaml 新增 `IconArchive` Geometry | `Resources/Icons/AppIcons.axaml` |
| 9 | 🔲 ResultPreviewService.BuildCompressPreview 支持 Separate 模式（输出目录分组 + 压缩包壳节点） | `Services/ResultPreviewService.cs` |
| 10 | 🔲 ResultPreviewService.BuildExtractPreview 支持多压缩包来源目录分组 | `Services/ResultPreviewService.cs` |
| 11 | 🔲 ResultPreviewService 两方法增加 filter 参数，构建后标记 IsFilteredOut | `Services/ResultPreviewService.cs` |
| 12 | 🔲 CompressSettingsWindow 连接 FileFilterControl.FilterChanged → ViewModel 重建预览 | `Dialogs/CompressSettingsWindow.axaml.cs` |
| 13 | 🔲 ExtractSettingsWindow 连接 FileFilterControl.FilterChanged → ViewModel 重建预览 | `Dialogs/ExtractSettingsWindow.axaml.cs` |
| 14 | 🔲 CompressSettingsViewModel 在输出模式切换时重建预览 | `ViewModels/CompressSettingsViewModel.cs` |
| 15 | 🔲 ExtractSettingsViewModel 在目标路径变化时自动重建预览（`OnDestinationPathChanged` + 缓存 entries） | `ViewModels/ExtractSettingsViewModel.cs` |
| 16 | 🔲 i18n key 写入 | `strings.*.json` |
| 17 | 🔲 构建验证 | `dotnet build` |

## 复用场景

| 场景 | 使用方式 |
|------|---------|
| ExtractSettingsWindow 解压预览 | 内嵌 ResultTreeView，传入 PreviewTreeNode（由 ResultPreviewService 构建） |
| CompressSettingsWindow 压缩预览 | 同上 |
| 拖拽导出（未来） | 内嵌 ResultTreeView，传入拖拽文件的预览树 |
| 任何需要展示文件树 + 冲突检测的地方 | 直接复用 ResultTreeView |

## 注意事项

- **原始树 vs 显示树分离**：`ResultPreviewService` 构建一次原始树，`ResultTreeView` 内部根据 CompactMode 变化反复重建显示树。切换精简/完整模式时无需重新调用 Service。
- **冲突检测性能**：对解压预览批量调用 `File.Exists()` 可能很慢（目标目录在 NAS/网络驱动器时）。应该提供两种模式：
  - 快速模式：仅检查目标目录是否存在，不逐文件检测（无 ⚠️ 标记）
  - 完整模式：逐文件检测（可能有延迟）
  - 默认使用快速模式，用户可点击「检测冲突」按钮触发完整扫描
- **精简模式的截断优先级**：深度截断优先于文件数截断（先「切掉」深层节点，再在同一层内做文件数折叠）
- **点击省略号展开**：点击截断占位符 → 在当前位置「展开」完整内容。本质上是在当前节点下方插入被截断的子节点，移除占位符并重建渲染
- **ShowFilteredGhosts 切换**：仅影响显示树的重建，不需要重新调用 Service。切换时 `ResultTreeView` 内部重新遍历原始树并应用过滤规则

## 工具栏扩展

### 1. 工具栏布局

现有工具栏（第 0 行 Grid）：

```
[🔘 精简/完整] [+] [摘要文字]
```

改为：

```
[🔘 精简/完整] [+] [🔍 定位到选中] [👁 显示过滤项] [摘要文字]
```

### 2. ShowFilteredGhosts 切换按钮

- 类型：`ToggleButton`，绑定 `ShowFilteredGhosts`（ResultTreeView 已有 `StyledProperty<bool>`，默认 `false`）
- 行为：
  - 选中（`true`）：不匹配过滤条件的节点保留在树中，`TextOpacity = 0.4` 灰色虚影
  - 未选中（`false`，默认）：`ApplyDisplayRules()` 中移除 `IsFilteredOut` 节点
- `IsChecked` 绑定到 `ShowFilteredGhosts`（`RelativeSource AncestorType=UserControl`）
- 图标：`PathIcon` + `Data="{StaticResource ...}"`，需在 `AppIcons.axaml` 新增筛选/眼睛图标 Geometry

### 3. "定位到选中项"按钮

- 类型：普通 `Button`，放在 ExpandAll 按钮右侧
- 交互：
  - **无选中项** → `IsEnabled = false`（灰色禁用）
  - **有选中项** → 按钮可用
  - **点击后**：
    1. 获取 `DisplayNodes` 的根节点，调用 `CollapseAll()`（`FolderNode` 内置方法，折叠除根以外所有节点）
    2. 遍历 `TreeView.SelectedItems`，对每个 `PreviewTreeNode`：
       - 取其 `FullPath`，按 `/` 分割
       - 从显示树根节点开始，逐层遍历找到每层祖先
       - 设每层祖先的 `IsExpanded = true`
       - 如果路径中的某节点因截断不存在 → 展开到最近存在的祖先后停止（不报错）
- 多选支持：同时展开所有选中项的完整路径
- 树重建（过滤/紧凑度切换）后选中自然丢失，按钮自动恢复禁用

### 4. 实现细节

#### 4a. TreeView 启用多选

```xml
<TreeView x:Name="PreviewTreeView"
          SelectionMode="Multiple"
          ...>
```

```csharp
// 监听选中变化更新按钮状态
private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
{
    LocateButton.IsEnabled = PreviewTreeView.SelectedItems.Count > 0;
}
```

#### 4b. 展开到路径

```csharp
private void OnLocateClick()
{
    var displayRoot = DisplayNodes.FirstOrDefault();
    if (displayRoot == null) return;
    
    // 1. 折叠所有（保留根展开）
    displayRoot.CollapseAll(); 
    displayRoot.IsExpanded = true;
    
    // 2. 对每个选中项展开其祖先路径
    foreach (var item in PreviewTreeView.SelectedItems)
    {
        if (item is PreviewTreeNode pt && !string.IsNullOrEmpty(pt.FullPath))
            ExpandAncestors(displayRoot, pt.FullPath);
    }
}

private static void ExpandAncestors(PreviewTreeNode root, string fullPath)
{
    var parts = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var current = root;
    foreach (var part in parts)
    {
        var child = current.Children
            .OfType<PreviewTreeNode>()
            .FirstOrDefault(c => c.Name == part && !c.IsTruncated);
        if (child == null) break; // 截断或路径不存在则停止
        child.IsExpanded = true;
        current = child;
    }
}
```

#### 4c. 新 i18n Keys

```json
// strings.zh-CN.json
"Preview_Result_Locate": "定位到选中",
"Preview_Result_ShowFiltered": "显示过滤项",
"Preview_Result_HideFiltered": "隐藏过滤项",

// strings.en.json
"Preview_Result_Locate": "Locate",
"Preview_Result_ShowFiltered": "Show filtered",
"Preview_Result_HideFiltered": "Hide filtered",
```

### 5. 变更文件

| 文件 | 变更 |
|------|------|
| `Controls/ResultTreeView.axaml` | 工具栏新增两个按钮 + TreeView SelectionMode="Multiple" |
| `Controls/ResultTreeView.axaml.cs` | 定位按钮点击逻辑 + 选中状态同步 |
| `Resources/Icons/AppIcons.axaml` | 新增 ShowFilteredGhosts/Locate 按钮的 PathIcon Geometry |

## 实施步骤（更新）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 18 | ✅ ResultTreeView 工具栏新增 ShowFilteredGhosts ToggleButton + 绑定 | `Controls/ResultTreeView.axaml` |
| 19 | ✅ TreeView 启用 SelectionMode="Multiple" + 选中状态同步 | `Controls/ResultTreeView.axaml` + `.cs` |
| 20 | ✅ 定位按钮 + ExpandAncestors 展开逻辑 | `Controls/ResultTreeView.axaml` + `.cs` |
| 21 | ✅ AppIcons.axaml 新增 PathIcon Geometry（筛选/定位图标） | `Resources/Icons/AppIcons.axaml` |
| 22 | ✅ i18n key 写入两语言文件 | `strings.*.json` |
| 23 | ✅ 构建验证 | `dotnet build` |
