# 文件选择器多选（文件+目录）— CustomFilePickerDialog PickItems 模式

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 Avalonia 的 `CustomFilePickerDialog` 增加「文件+目录混合多选」能力（`PickerMode.PickItems`），并让 `CompressSettingsWindow` 用它替代当前「原生多选文件」+「自建单选目录」的双按钮方案，合并为一个「添加文件/文件夹」按钮。

**决策记录（2026-07-31 与用户讨论确认）：**
1. **选择范围**：跨目录累积 —— 导航切换目录时已选项目保留，右侧「已选项目」面板展示（数量 + 批量添加/移除 + 逐项移除 + 清空）
2. **累积机制**：**勾选框方案**（Windows 11 文件对话框风格）—— 单击仅高亮，勾选才累积，双击目录进入导航无副作用（根除「选中 vs 进入」冲突）
3. **按钮布局**：CompressSettingsWindow 合并为一个按钮（「添加文件/文件夹」）
4. **双击行为**：文件 → 切换勾选状态不关闭；目录 → 进入导航
5. **系统浏览按钮**：PickItems / ExtractFolder 模式下隐藏（原生对话框无法文件+目录混选，OS 级限制）
6. **批量操作**：右侧面板提供「＋添加所选 (N)」（把当前高亮项加入累积）与「－移除所选 (M)」（从累积中移除高亮中已累积项，M=0 时禁用）两个批量按钮，替代逐项勾选
7. **面板位置**：**右侧面板**取代早期方案的底部「已选项目条」—— PickItems 模式右栏为累积列表，ExtractFolder 模式右栏为原底部 PreviewArea（ResultTreeView 冲突预览）移入

**Why not 单击即累积：** 双击目录 = 两次单击，单击即累积会把双击进入的每个目录都误加进压缩列表（如 `C:\` → `Docs` → `Work` 全被累积）。勾选框把「选中」与「进入」彻底解耦。

**Why 右侧面板：** 底部条把「预览区」和「已选列表」挤在同一垂直空间；右侧面板让两种模式共享同一布局骨架（右栏内容按模式切换），PreviewArea 从底部移除后底部只剩文件名输入行 + OK/Cancel，垂直布局更紧凑。

**Tech Stack:** .NET 9, Avalonia 12.x（无新增依赖）

**新增依赖：** 无

---

## 文件映射

| 文件 | 操作 | 职责 |
|------|------|------|
| `Dialogs/CustomFilePickerDialog.axaml` | 修改 | Row 1 改三列布局 `220,5,*,5,Auto`；删 Row 2（PreviewSplitter 废弃）；FileList 勾选框列 + Extended 选择；右栏（PickItems 面板 + PreviewArea 迁入）；FileNameArea Grid.Row 3→2、OK/Cancel Grid.Row 4→3；窗口宽 800→900、MinWidth 700→800 |
| `Dialogs/CustomFilePickerDialog.axaml.cs` | 修改 | 新增 `PickerMode.PickItems`、`SelectedPaths` 属性、`ShowOpenItemsAsync` 静态入口、`FileBrowserItem` 包装类（勾选状态）、`ToggleAccumulated` 统一累积入口、批量添加/移除处理、OK/双击/Enter 分支 |
| `Dialogs/CompressSettingsWindow.axaml` | 修改 | 删除「添加文件夹」按钮（L51–56）；「添加文件」按钮文案改 `Compress_AddItems`（L45–50） |
| `Dialogs/CompressSettingsWindow.axaml.cs` | 修改 | `PickFiles` 回调改调 `ShowOpenItemsAsync`；删除 `ViewModel.PickFolder` 赋值（L70–73） |
| `ViewModels/CompressSettingsViewModel.cs` | 修改 | 删除 `PickFolder` 属性（L44）+ `AddFolder` 命令（L1001–1010）；`AddFiles` 命令体零改动（返回 `Task<IReadOnlyList<string>?>` 签名兼容） |
| `Localization/strings.zh-CN.json` / `strings.en.json` | 修改 | 新增 8 个 key（见 Task 5） |

**范围外（明确不做）：** WPF 遗留版（维护模式）；MainWindow 的 `AddFiles`（往已打开压缩包加文件，纯文件场景保持原生多选）；不做勾选框以外的替代交互。

---

## 布局总览（PickItems / ExtractFolder 双模式共享骨架）

```
┌────────────────────────────┬──────────────────────────┐
│  AddressBar / 导航区         │  右栏（按模式切换）        │
│  [←][↑] [路径] [搜索] [类型]  │                          │
├────────────────────────────┤  PickItems：              │
│  FileList                  │   「已选项目 (N)」标题      │
│  ☑ 名称     大小   类型   修改 │   [＋添加所选 (N)][－移除所选 (M)]│
│  ☑ dir1     —     文件夹    │   [清空]                  │
│  ☐ file.zip  1KB   压缩文件  │   · item1        ×        │
│  ☐ dir2     —     文件夹    │   · item2        ×        │
│  ☐ file.txt  2KB   文本文件  │   （空态占位文案）          │
│                            │                          │
│                            │  ExtractFolder：          │
│                            │   PreviewArea 迁入         │
│                            │   （ResultTreeView 冲突预览）│
├────────────────────────────┴──────────────────────────┤
│  FileNameArea（文件名输入行，Grid.Row=2）                │
│  OK / Cancel（Grid.Row=3）                              │
└────────────────────────────────────────────────────────┘
```

**行/列定义：**
- RowDefinitions：`Auto,*,Auto,Auto`（删原 Row 2 PreviewArea 行，共 4 行）
- Row 1 ColumnDefinitions：`220,5,*,5,Auto`（目录树 / 分隔条 / 文件列表 / 分隔条 / 右栏）
- 右栏宽 Auto，内容为 `Grid` 双面板叠放，按 `_mode` 切换 `IsVisible`

**窗口尺寸：** Width 800→900；MinWidth 700→800；ExtractFolder 高 620→500；PickItems 面板高 420（MinHeight 300）。

**右栏两面板：**
- **PickItemsPanel（新）**：标题「已选项目 (N)」+ 双按钮行 + 清空 + `ItemsControl`（每行：路径 + × 按钮）+ 空态占位（`Picker_AccumulatedEmpty`）
- **ExtractFolderPanel**：原 PreviewArea 内容整体迁入（`ResultTreeView` 冲突预览，`Picker_ExtractPreviewTitle` 标题）

**现有结构参考（CustomFilePickerDialog.axaml.cs 行号）：** 导航 `NavigateTo` L366、`LoadDirectory` L441、确认 `TryConfirmFile` L641、`Ok_Click` L669、系统浏览 `SystemBrowse_Click` L763。

---

### Task 1: 对话框 — 新模式与入口

**文件：** `Dialogs/CustomFilePickerDialog.axaml.cs`

#### 1.1 新增枚举成员

```csharp
public enum PickerMode
{
    PickFolder,
    SaveFile,
    OpenFile,
    ExtractFolder,
    /// <summary>多选模式：文件+目录混合选择，勾选累积，跨目录保留。</summary>
    PickItems
}
```

#### 1.2 新增属性

```csharp
/// <summary>PickItems 模式累积选中的路径列表（按路径排序，FullPath 去重）。</summary>
public IReadOnlyList<string> SelectedPaths { get; private set; } = Array.Empty<string>();

/// <summary>PickItems 模式累积中的路径（内部可变集合，路径排序去重）。</summary>
private readonly List<string> _accumulatedPaths = new();

/// <summary>当前可移除数 = 高亮选中项中已累积的项数（决定「－移除所选」按钮可用性）。</summary>
public int RemovableCount { get; private set; }
```

#### 1.3 新增静态入口

```csharp
/// <summary>打开文件/文件夹（多选，PickItems 模式）。返回选中路径列表，取消返回 null。</summary>
public static Task<IReadOnlyList<string>?> ShowOpenItemsAsync(Window owner, string? initialPath = null)
    => ShowOpenItemsInternal(owner, initialPath);
```

`ShowInternal` 保持单路径签名不动（现有 4 个入口零改动）；新增独立的 `ShowOpenItemsInternal`：

```csharp
private static async Task<IReadOnlyList<string>?> ShowOpenItemsInternal(Window owner, string? initialPath)
{
    var dialog = new CustomFilePickerDialog { _mode = PickerMode.PickItems };
    dialog.Initialize();
    await dialog.ShowDialog(owner);
    return dialog.SelectedPaths;
}
```

#### 1.4 模式分支

`LoadDirectory` 里按 `_mode` 分支（PickItems 沿用 OpenFile 的显示文件逻辑 + 显示目录）：

```csharp
// LoadDirectory 内现有逻辑之外：
if (_mode == PickerMode.PickItems)
{
    // showFiles 条件扩展：PickItems 显示文件+目录（沿用 OpenFile 文件判断 + 目录始终显示）
    // 文件项包装为 FileBrowserItem（IsSelected = _accumulatedPaths.Contains(fullPath) 回填）
    // 目录项：PickItems 模式也用 FileBrowserItem 包装（无勾选/始终显示）
}
```

**新增包装类（统一勾选状态）：**

```csharp
public sealed class FileBrowserItem : ObservableObject
{
    private bool _isSelected;
    public string FullPath { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public bool CanCheck { get; }   // 文件=true；目录=PickItems 模式=true（可勾选加入压缩）；纯浏览模式=false
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
```

勾选框 `IsChecked` TwoWay 绑定 `IsSelected`，其 setter 不直接改累积——统一走 `ToggleAccumulated`（见 Task 2），保证勾选框/批量/清空/回填共用同一累积逻辑。

**现有 `FileSystemItem` 若已存在则在其上加属性或新增包装类，以代码现状为准（最小侵入）。**

#### 1.5 OK 按钮分支

`Ok_Click`（L669）：PickItems 模式 → `SelectedPaths = _accumulatedPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()`（保持排序一致性），`Close(true)`；不做 `TryConfirmFile`（无文件名输入语义）。

---

### Task 2: 对话框 — 勾选累积 + 批量按钮

**文件：** `Dialogs/CustomFilePickerDialog.axaml.cs`

#### 2.1 统一累积入口

```csharp
/// <summary>统一累积入口：勾选框/双击/批量/清空/回填全部经过此方法。</summary>
private void ToggleAccumulated(string path, bool isChecked)
{
    if (isChecked)
    {
        if (!_accumulatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            _accumulatedPaths.Add(path);
    }
    else
    {
        _accumulatedPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    }
    UpdatePickPanel();
}
```

排序去重策略：**累积时按路径字典序去重插入**（OrdinalIgnoreCase），始终有序；`SelectedPaths` 导出时不再重复排序（仍做一次防御性排序）。

#### 2.2 批量添加所选

```csharp
private void AddSelected_Click(object? sender, RoutedEventArgs e)
{
    foreach (var item in FileList.SelectedItems.OfType<FileBrowserItem>().Where(i => i.CanCheck))
    {
        item.IsSelected = true;                 // 勾选框同步
        ToggleAccumulated(item.FullPath, true); // 累积
    }
    UpdatePickPanel();
}
```

#### 2.3 批量移除所选

```csharp
private void RemoveSelected_Click(object? sender, RoutedEventArgs e)
{
    foreach (var item in FileList.SelectedItems.OfType<FileBrowserItem>())
    {
        item.IsSelected = false;                 // 勾选框同步
        ToggleAccumulated(item.FullPath, false); // 从累积移除
    }
    UpdatePickPanel();
}
```

「－移除所选 (M)」按钮 `IsEnabled = RemovableCount > 0`；M = 高亮选中项 ∩ 累积项的计数。

#### 2.4 逐项移除

ItemsControl 每行 × 按钮：`ToggleAccumulated(item.FullPath, false)` + 同步回填对应 `FileBrowserItem.IsSelected = false`（若该项仍显示在当前列表）。

#### 2.5 清空

`ClearAccumulated_Click`：`_accumulatedPaths.Clear()` + 回填所有可见项 `IsSelected = false` + `UpdatePickPanel()`。

#### 2.6 面板刷新

```csharp
private void UpdatePickPanel()
{
    // 1. 更新右栏 ItemsControl 数据源（_accumulatedPaths 投影为显示项）
    // 2. 更新标题计数「已选项目 (N)」
    // 3. 更新「＋添加所选 (N)」计数 = 当前高亮中未累积的可加项数
    // 4. 更新 RemovableCount + 「－移除所选 (M)」IsEnabled
    // 5. 空态占位 IsVisible = _accumulatedPaths.Count == 0
}
```

#### 2.7 双击 / Enter 分支

`DoubleTapped`（文件）：切换该行 `FileBrowserItem.IsSelected`（setter 触发累积），**不关闭对话框**；（目录）：`NavigateTo` 进入（保持现状，无副作用）。
`KeyDown` Enter：PickItems 模式不触发 `TryConfirmFile`（无确认语义），维持双击切换勾选的等价行为。

#### 2.8 SelectionChanged 监听

FileList `SelectionChanged` → `UpdatePickPanel()`（刷新批量按钮计数）。注意与现有「选中触发预览」守卫（`_isProgrammaticFilter`）不冲突——PickItems 模式无预览。

---

### Task 3: 对话框 — XAML 三列布局 + 右栏

**文件：** `Dialogs/CustomFilePickerDialog.axaml`

#### 3.1 行/列重构

```xml
<Grid RowDefinitions="Auto,*,Auto,Auto">
    <!-- Row 0: AddressBar / 导航区（不变） -->
    <Grid Grid.Row="1" ColumnDefinitions="220,5,*,5,Auto">
        <!-- 目录树（Column 0，不变） -->
        <!-- GridSplitter（Column 1） -->
        <!-- FileList（Column 2，SelectionMode=Extended） -->
        <!-- GridSplitter（Column 3） -->
        <!-- 右栏 Grid（Column 4，Auto）：PickItemsPanel + ExtractFolderPanel 叠放切换 -->
    </Grid>
    <!-- Row 2: FileNameArea（原 Grid.Row=3 顺移） -->
    <!-- Row 3: OK/Cancel 按钮行（原 Grid.Row=4 顺移） -->
</Grid>
```

- 删除原 Row 2（PreviewArea 行）及 `PreviewSplitter`（如需保留分栏能力则改为右栏内部 GridSplitter，以代码现状为准）
- 右栏两个面板以 `IsVisible` 切换（PickItems 模式显示 PickItemsPanel；ExtractFolder 模式显示 ExtractFolderPanel）

#### 3.2 FileList 勾选框列

```xml
<ListBox SelectionMode="Extended" ...>
  <ListBox.ItemTemplate>
    <DataTemplate x:DataType="...:FileBrowserItem">
      <Grid ColumnDefinitions="24,20,*,80,110">
        <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}"
                  IsVisible="{Binding CanCheck}"
                  VerticalAlignment="Center" />
        <PathIcon Grid.Column="1" ... />            <!-- 类型图标 -->
        <TextBlock Grid.Column="2" Text="{Binding Name}" ... />
        <TextBlock Grid.Column="3" Text="{Binding SizeDisplay}" ... />
        <TextBlock Grid.Column="4" Text="{Binding ModifiedDisplay}" ... />
      </Grid>
    </DataTemplate>
  </ListBox.ItemTemplate>
</ListBox>
```

`CanCheck=false` 时勾选框隐藏、整行仍可单击高亮 + 双击进入（目录在非 PickItems 模式）。

#### 3.3 PickItems 面板（Column 4 内）

```xml
<StackPanel Grid.Row="1" Grid.Column="4" Width="260" Margin="12,0,0,0"
            IsVisible="{Binding ...PickItemsMode}">
  <TextBlock Text="{DynamicResource ...Picker_PickItemsTitle}" />  <!-- 「已选项目」 -->
  <StackPanel Orientation="Horizontal">
    <Button Content="{DynamicResource ...Picker_AddSelected}" Command="{Binding ...}" />    <!-- ＋添加所选 (N) -->
    <Button Content="{DynamicResource ...Picker_RemoveSelected}" ... />                     <!-- －移除所选 (M) -->
  </StackPanel>
  <Button Content="{DynamicResource ...Picker_ClearSelection}" ... />                       <!-- 清空 -->
  <ItemsControl ItemsSource="{Binding AccumulatedItems}">
    <DataTemplate>
      <Grid ColumnDefinitions="*,Auto">
        <TextBlock Text="{Binding Path}" ToolTip.Tip="{Binding Path}" TextTrimming="CharacterEllipsis" />
        <Button Grid.Column="1" Content="×" Tag="{Binding Path}" Click="RemoveItem_Click" ... />
      </Grid>
    </DataTemplate>
  </ItemsControl>
  <TextBlock Text="{DynamicResource ...Picker_AccumulatedEmpty}" IsVisible="{Binding ...Empty}" />  <!-- 空态占位 -->
</StackPanel>
```

绑定实现方式以现有 code-behind 风格为准（对话框是 code-behind，用命名元素 + 事件处理，不强行引入 VM；`x:DataType` 仅为编译绑定，元素用 `Name` 直接操作）。

#### 3.4 ExtractFolder 面板（Column 4 内）

原 PreviewArea 全部内容迁入，标题换 `Picker_ExtractPreviewTitle`，高度 620→500（MinHeight 300）。

#### 3.5 窗口尺寸

`Width="900"`、`MinWidth="800"`。

---

### Task 4: 对话框 — OK/双击/Enter 分支细节

**文件：** `Dialogs/CustomFilePickerDialog.axaml.cs`

- `Ok_Click`（L669）：PickItems → `SelectedPaths = _accumulatedPaths.OrderBy(...).ToArray()` + `Close(true)`；其他模式保持现状
- `DoubleTapped`：PickItems 且命中文件行 → 切换 `IsSelected`（累积），不关闭；目录 → `NavigateTo`（现状）
- `KeyDown` Enter：PickItems → 不确认、不关闭（等价双击切换语义；或对当前高亮项批量「添加所选」——以用户最终体验为准，默认不关闭）
- `SystemBrowse_Click`（L763）：PickItems / ExtractFolder → 隐藏（按钮 `IsVisible=false`），其余模式保持现状
- 目录树导航切换目录后：`LoadDirectory` 重建 FileList → 新列表项勾选状态从 `_accumulatedPaths` 回填（跨目录累积可见）

---

### Task 5: 本地化 key

**文件：** `Localization/strings.zh-CN.json` / `strings.en.json`

| Key | zh-CN | en |
|-----|-------|----|
| `Picker_PickItemsTitle` | 已选项目 | Selected items |
| `Picker_SelectedCount` | 已选项目 ({0}) | Selected ({0}) |
| `Picker_AddSelected` | ＋添加所选 ({0}) | ＋Add selected ({0}) |
| `Picker_RemoveSelected` | －移除所选 ({0}) | －Remove selected ({0}) |
| `Picker_ClearSelection` | 清空 | Clear |
| `Picker_AccumulatedEmpty` | 尚未选择任何项目 | Nothing selected yet |
| `Picker_ExtractPreviewTitle` | 解压预览 | Extract preview |
| `Compress_AddItems` | 添加文件/文件夹 | Add files/folders |

（key 命名与现有 `Picker_*` 前缀一致；计数占位符 {0} 由代码 `string.Format` / 现有 L() 机制填充。）

---

### Task 6: CompressSettingsWindow 合并按钮

**文件：** `Dialogs/CompressSettingsWindow.axaml` / `.cs`、`ViewModels/CompressSettingsViewModel.cs`

1. `CompressSettingsWindow.axaml` L45–50：「添加文件」按钮文案改 `Compress_AddItems`（ToolTip 同步）；L51–56：删除「添加文件夹」按钮
2. `CompressSettingsWindow.axaml.cs` L70–73：删除 `ViewModel.PickFolder` 赋值；`PickFiles` 回调改调 `ShowOpenItemsAsync`（返回 `IReadOnlyList<string>?`，非 null 时 `AddFiles` 追加——现有 `AddFiles` 命令体零改动）
3. `CompressSettingsViewModel.cs`：删除 `PickFolder` 属性（L44）+ `AddFolder` 命令（L1001–1010）；删除后若 `PickFolderAsync` 回调无人引用则一并清理

**注意：** `DragDropService.PickFolderAsync`（`Services/DragDropService.cs` L238）是无关方法，**不动**。

---

### Task 7: 验证

**命令：**

```powershell
# 构建 Avalonia 版
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj

# Avalonia 测试
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```

**验证清单：**
- [ ] 构建零错误零警告（新增代码）
- [ ] 测试通过
- [ ] `lsp_diagnostics` 对 5 个改动文件干净
- [ ] 手动验收：PickItems 模式勾选/批量添加/批量移除/逐项移除/清空/跨目录累积/双击文件不关闭/双击目录进入/OK 返回 `SelectedPaths`/取消返回 null
- [ ] CompressSettingsWindow：单按钮、文件+目录都能加进压缩列表、原「添加文件夹」无残留引用
- [ ] ExtractFolder 模式回归：右栏显示冲突预览、行为与旧底部 PreviewArea 一致
- [ ] OpenFile / PickFolder / SaveFile 模式回归（布局顺移后 Row 号正确、系统浏览按钮恢复显示）

**风险与备注：**
- 布局顺移（FileNameArea Grid.Row 3→2、OK/Cancel 4→3）是纯机械改动，但必须与 axaml/cs 中所有 Grid.Row 引用同步
- `FileBrowserItem` 与现有 `FileSystemItem` 的关系以代码现状为准（若现有类已是 ObservableObject 且被多模板共用，优先在其上加属性，避免第二套列表项类型引发模板混乱）
- 勾选框列宽 24px + 图标列 20px 使行模板从 3 列变 5 列，`SizeDisplay`/`ModifiedDisplay` 列宽按现状微调
- 跨目录累积依赖「导航后回填」：`LoadDirectory` 重建列表时必须按 `_accumulatedPaths` 回填 `IsSelected`，否则切目录后勾选状态丢失（视觉不一致）
