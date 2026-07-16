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
| `Models/PreviewTreeNode.cs` | 新建 - 树节点数据模型（继承/扩展 FolderNode） |
| `Services/ResultPreviewService.cs` | 新建 - 构建预览树的逻辑 |
| `Dialogs/ExtractSettingsWindow.axaml` | 布局改造：加 TabControl + 右侧预览面板 |
| `Dialogs/ExtractSettingsWindow.axaml.cs` | 预览面板联动设置 |
| `ViewModels/ExtractSettingsViewModel.cs` | 新增预览树属性、刷新逻辑 |
| `Dialogs/CompressSettingsWindow.axaml` | 布局改造：加右侧预览面板 |
| `Dialogs/CompressSettingsWindow.axaml.cs` | 预览面板联动设置 |
| `ViewModels/CompressSettingsViewModel.cs` | 新增预览树属性、刷新逻辑 |
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

```csharp
public static class ResultPreviewService
{
    /// <summary>
    /// 构建解压预览树
    /// </summary>
    /// <param name="archivePath">压缩包路径</param>
    /// <param name="entries">归档条目列表（ArchiveItem）</param>
    /// <param name="destDir">目标解压目录</param>
    /// <param name="smartExtract">是否智能解压</param>
    /// <param name="filters">文件过滤条件（可选）</param>
    public static PreviewTreeNode BuildExtractPreview(
        string archivePath,
        IEnumerable<ArchiveItem> entries,
        string destDir,
        bool smartExtract = false,
        SearchFilters? filters = null)
    {
        // 1. 用 ArchiveTreeBuilder 构建原始树（基于归档内路径）
        // 2. 对每个节点，将 FullPath 转换为真实文件系统路径（destDir + relative）
        // 3. 对每个文件节点，检查 File.Exists(realPath) 标记 ExistsAtDestination
        // 4. 如果传入了 filters，标记 IsFilteredOut
        // 5. 统计 TotalDescendantCount / MaxChildDepth
    }

    /// <summary>
    /// 构建压缩预览树
    /// </summary>
    /// <param name="sourcePaths">用户选择的源路径列表</param>
    /// <param name="filters">文件过滤条件（可选）</param>
    public static PreviewTreeNode BuildCompressPreview(
        IReadOnlyList<string> sourcePaths,
        SearchFilters? filters = null)
    {
        // 1. 扫描源路径的文件系统，构建树
        // 2. 如果传入了 filters，标记 IsFilteredOut
        // 3. 统计 TotalDescendantCount / MaxChildDepth
    }
}
```

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

## 实施步骤

| 步骤 | 内容 | 文件 |
|------|------|------|
| 1 | 新建 PreviewTreeNode 模型 | `Models/PreviewTreeNode.cs` |
| 2 | 新建 ResultPreviewService（构建原始树 + 冲突检测 + 过滤标记） | `Services/ResultPreviewService.cs` |
| 3 | 新建 ResultTreeView 控件（显示树构建 + 折叠逻辑 + 省略号渲染 + 摘要计算） | `Controls/ResultTreeView.axaml` / `.cs` |
| 4 | ExtractSettingsWindow 右侧加入 ResultTreeView | `Dialogs/ExtractSettingsWindow.axaml` / `.cs` + `ViewModels/ExtractSettingsViewModel.cs` |
| 5 | CompressSettingsWindow 右侧加入 ResultTreeView | `Dialogs/CompressSettingsWindow.axaml` / `.cs` + `ViewModels/CompressSettingsViewModel.cs` |
| 6 | i18n key 写入 | `strings.*.json` |
| 7 | 构建验证 | `dotnet build` |

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
