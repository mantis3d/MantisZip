# 上下文工具栏：目录树 & 文件列表

## 概述

在目录树上方和文件列表上方各新增一个上下文工具栏，同时重新组织现有全局工具栏（Row 1），将面板相关的按钮（筛选、展平）下放到对应的上下文工具栏中。

## 最终布局

```
Row 0: ████████████████████████ 菜单栏 ████████████████████████
Row 1: █████████████████ 精简后的全局工具栏 █████████████████
       (新建 | 打开 | 解压 | 智能解压 | 压缩 | ▏ 添加 | 删除 | 测试 | ▏ 预览 | 密码)
Row 2: █████████████████ 过滤栏 (可折叠, 保持不动) █████████████
Row 3: ┌────────────┬──┬──────────────────────────┬──┬────────────┐
       │ 🔍树过滤    │▌│ 🏠 ↑ ◀ ▶ 🔄              │▌│            │
       │ 📂展开全部  │▌│ [/dir1/sub]    ▼         │▌│  Preview   │
       │ 📂收起全部  │▌│ 📋复制名 ☰列 ▧视图       │▌│            │
       │ 📂展开到当前 │▌│ ◐全选 ⊗反选             │▌│            │
       │────────────│▌│ 🔍 📂 (筛选/展平)          │▌│            │
       │  TreeView  │▌│──────────────────────────│▌│            │
       │            │▌│    DataGrid              │▌│            │
       │            │▌│    (文件列表)              │▌│            │
       └────────────┴──┴──────────────────────────┴──┴────────────┘
Row 4: ████████████████████████ 状态栏 ████████████████████████
```

## 变更范围

| 文件 | 变更类型 |
|------|---------|
| `Views/MainWindow.axaml` | 布局改造：拆分面板，新增两个工具栏，移除全局中下放的按钮 |
| `Views/MainWindow.axaml.cs` | 地址栏 Enter 导航、列选择器 Popup 逻辑、树过滤搜索事件 |
| `ViewModels/MainWindowViewModel.cs` | 导航历史栈、ExpandAll/CollapseAll/GoRoot/GoBack/GoForward/Refresh/CopyName/SelectAll/InvertSelection 命令、FolderPaths 集合、树过滤逻辑、视图模式切换 |
| `Core/Services/ArchiveTreeBuilder.cs` (FolderNode) | 递归 ExpandAll/CollapseAll 方法 + 树过滤支持（可见性属性） |
| `Localization/strings.zh-CN.json` | 新增 i18n key |
| `Localization/strings.en.json` | 新增 i18n key |

## 详细设计

### 1. FolderNode 扩展（Core 层）

在 `FolderNode` 类新增递归方法：

```csharp
public void ExpandAll()
{
    IsExpanded = true;
    foreach (var child in Children)
        child.ExpandAll();
}

public void CollapseAll()
{
    // 根节点保留展开
    foreach (var child in Children)
        child.CollapseSelfAndDescendants();
}

private void CollapseSelfAndDescendants()
{
    IsExpanded = false;
    foreach (var child in Children)
        child.CollapseSelfAndDescendants();
}
```

`ExpandToCurrent` 逻辑在 ViewModel 中实现：从根向目标路径逐级展开祖先节点。

### 2. 导航历史栈（ViewModel）

新增私有字段：
```csharp
private readonly List<string> _backStack = new();
private readonly List<string> _forwardStack = new();
```

导航规则（在 `NavigateToFolder` / `NavigateToFolderPath` 的入口处统一记录）：
- 每次导航前，如果 `CurrentFolder` 不为 null，将其推入 `_backStack`
- 清空 `_forwardStack`
- `GoBack`: pop `_backStack` 顶部，将 `CurrentFolder` 推入 `_forwardStack`，然后导航
- `GoForward`: pop `_forwardStack` 顶部，将 `CurrentFolder` 推入 `_backStack`，然后导航
- `GoRoot`: 导航到 `CurrentFolder = ""`
- `GoUp`（已有）：改为调用 `NavigateToFolderPath` 以正确入栈

新增 RelayCommand 属性:
- `GoRootCommand`
- `GoBackCommand`（`CanExecute` = `_backStack.Count > 0`）
- `GoForwardCommand`（`CanExecute` = `_forwardStack.Count > 0`）

只读属性供绑定：
- `CanGoBack` / `CanGoForward`（通过 `PropertyChanged` 通知）

**注意**：`OnSelectedFolderChanged` 中调用 `NavigateToFolder` 也会触发导航历史的记录。需要在 `NavigateToFolder` 内部整合历史记录逻辑，或者用 flag 区

分程序化导航和用户交互。

### 3. TreeView IsExpanded 绑定

在 MainWindow.axaml 的 TreeView 中追加 Style:

```xml
<TreeView.Styles>
    <Style Selector="TreeViewItem">
        <Setter Property="IsExpanded" Value="{Binding IsExpanded}" />
    </Style>
</TreeView.Styles>
```

### 4. 地址栏（AutoCompleteBox）

使用 Avalonia 内置 `AutoCompleteBox` 控件：

```xml
<AutoCompleteBox x:Name="AddressBar"
                 Width="200"
                 Text="{Binding CurrentFolder}"
                 ItemsSource="{Binding FolderPaths}"
                 FilterMode="StartsWith"
                 Watermark="路径..."
                 Background="{DynamicResource ThemeWindowBgBrush}"
                 Foreground="{DynamicResource ThemeTextPrimaryBrush}"
                 BorderBrush="{DynamicResource ThemeBorderBrush}" />
```

ViewModel 新增：
```csharp
public ObservableCollection<string> FolderPaths { get; } = new();
```

在 `LoadArchiveAsync` 中填充 `FolderPaths`：从 `_allRawItems` 提取所有唯一的目录路径。

地址栏的 Enter/选择导航：绑定 `KeyDown` 事件或 `AutoCompleteBox.TextChanged` + Accept 行为。最简单的方式是在 View 中处理 `KeyDown`，如果按 Enter 则调用 `vm.NavigateToFolderPath(vm.CurrentFolder)`。

### 5. 布局改造（MainWindow.axaml）

#### 5a. 目录树面板改造

当前：
```xml
<Border Grid.Column="0" Width="220"
        Background="{DynamicResource ThemeSurfaceBgBrush}">
    <Grid RowDefinitions="Auto,*">
        <TextBlock Text="{Binding LocalizedStrings[Tree_Browse]}" ... />
        <TreeView Grid.Row="1" ... />
    </Grid>
</Border>
```

改成：
```xml
<Border Grid.Column="0" Width="220"
        Background="{DynamicResource ThemeSurfaceBgBrush}">
    <Grid RowDefinitions="Auto,Auto,*">
        <!-- Tree toolbar -->
        <Border Grid.Row="0" Padding="4,2"
                Background="{DynamicResource ThemeHeaderBgBrush}">
            <StackPanel Orientation="Horizontal" Spacing="2">
                <Button Classes="ToolbarButton"
                        Command="{Binding ExpandAllCommand}"
                        ToolTip.Tip="展开所有">
                    <TextBlock Text="📂+" FontSize="13" />
                </Button>
                <Button Classes="ToolbarButton"
                        Command="{Binding CollapseAllCommand}"
                        ToolTip.Tip="收起所有">
                    <TextBlock Text="📂−" FontSize="13" />
                </Button>
                <Button Classes="ToolbarButton"
                        Command="{Binding ExpandToCurrentCommand}"
                        ToolTip.Tip="只展开到当前目录">
                    <TextBlock Text="📂→" FontSize="13" />
                </Button>
            </StackPanel>
        </Border>
        <!-- Header -->
        <TextBlock Grid.Row="1" Text="{Binding LocalizedStrings[Tree_Browse]}"
                   Margin="8,4" FontWeight="SemiBold"
                   Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
        <!-- Tree -->
        <TreeView Grid.Row="2" ... />
    </Grid>
</Border>
```

#### 5b. 文件列表面板改造

当前 DataGrid 在 `Grid.Column="2"`，改为嵌套 Grid：

```xml
<!-- File list panel -->
<Grid Grid.Column="2">
    <Grid RowDefinitions="Auto,*">
        <!-- File list toolbar -->
        <Border Grid.Row="0" Padding="4,2"
                Background="{DynamicResource ThemeHeaderBgBrush}">
            <StackPanel Orientation="Horizontal" Spacing="2">
                <!-- 导航组 -->
                <Button Classes="ToolbarButton"
                        Command="{Binding GoRootCommand}"
                        ToolTip.Tip="回到根目录">
                    <TextBlock Text="🏠" FontSize="13" />
                </Button>
                <Button Classes="ToolbarButton"
                        Command="{Binding GoUpCommand}"
                        ToolTip.Tip="回到父目录">
                    <TextBlock Text="↑" FontSize="13" />
                </Button>
                <Button Classes="ToolbarButton"
                        Command="{Binding GoBackCommand}"
                        IsEnabled="{Binding CanGoBack}"
                        ToolTip.Tip="后退">
                    <TextBlock Text="◀" FontSize="13" />
                </Button>
                <Button Classes="ToolbarButton"
                        Command="{Binding GoForwardCommand}"
                        IsEnabled="{Binding CanGoForward}"
                        ToolTip.Tip="前进">
                    <TextBlock Text="▶" FontSize="13" />
                </Button>
                
                <Border Width="1" Height="20" Margin="2,0"
                        VerticalAlignment="Center"
                        Background="{DynamicResource ThemeBorderBrush}" />
                
                <!-- 地址栏 -->
                <AutoCompleteBox Width="160" Height="22"
                                 x:Name="AddressBar"
                                 Text="{Binding CurrentFolder}"
                                 ItemsSource="{Binding FolderPaths}"
                                 FilterMode="StartsWith"
                                 Watermark="路径..."
                                 KeyDown="AddressBar_KeyDown"
                                 Background="{DynamicResource ThemeWindowBgBrush}"
                                 Foreground="{DynamicResource ThemeTextPrimaryBrush}"
                                 BorderBrush="{DynamicResource ThemeBorderBrush}" />
                
                <Border Width="1" Height="20" Margin="2,0"
                        VerticalAlignment="Center"
                        Background="{DynamicResource ThemeBorderBrush}" />
                
                <!-- 筛选/展平 -->
                <ToggleButton Classes="ToolbarButton"
                              IsChecked="{Binding IsFilterBarVisible}"
                              ToolTip.Tip="{Binding LocalizedStrings[Tooltip_Filter]}">
                    <TextBlock Text="🔍" FontSize="13"
                               ToolTip.Tip="{Binding LocalizedStrings[Tooltip_Filter]}" />
                </ToggleButton>
                <ToggleButton Classes="ToolbarButton"
                              IsChecked="{Binding ShowSubfolders}"
                              ToolTip.Tip="{Binding LocalizedStrings[Tooltip_Subfolders]}">
                    <TextBlock Text="📂" FontSize="13"
                               ToolTip.Tip="{Binding LocalizedStrings[Tooltip_Subfolders]}" />
                </ToggleButton>
            </StackPanel>
        </Border>
        
        <!-- DataGrid -->
        <DataGrid Grid.Row="1" ... />
    </Grid>
</Grid>
```

#### 5c. 全局工具栏精简

从现有全局工具栏（Row 1）移除：
- `IsFilterBarVisible` ToggleButton（移到文件列表工具栏）
- `ShowSubfolders` ToggleButton（移到文件列表工具栏）

保留顺序：新建 | 打开 | 解压 | 智能 | 压缩 | ▏ 添加 | 删除 | 测试 | ▏ 预览 | 密码

### 6. 目录树搜索框（新增候选）

在目录树工具栏的展开/收起按钮下方（或同一行右侧），增加一个搜索文本框：

```xml
<!-- Tree search -->
<TextBox Width="180" Height="22"
         x:Name="TreeSearchBox"
         Text="{Binding TreeFilterText}"
         Watermark="过滤目录..."
         KeyUp="TreeSearchBox_KeyUp"
         Background="{DynamicResource ThemeWindowBgBrush}"
         Foreground="{DynamicResource ThemeTextPrimaryBrush}"
         BorderBrush="{DynamicResource ThemeBorderBrush}" />
```

ViewModel 新增：
```csharp
[ObservableProperty]
private string? _treeFilterText;

partial void OnTreeFilterTextChanged(string? value)
{
    ApplyTreeFilter(value);
}

private void ApplyTreeFilter(string? filter)
{
    if (FolderTreeRoot == null) return;
    ApplyTreeFilterRecursive(FolderTreeRoot, filter);
}

/// <returns>true = 本节点或其子孙匹配过滤条件</returns>
private static bool ApplyTreeFilterRecursive(FolderNode node, string? filter)
{
    if (string.IsNullOrWhiteSpace(filter))
    {
        node.IsVisible = true;
        foreach (var child in node.Children)
            ApplyTreeFilterRecursive(child, filter);
        return true;
    }
    
    var selfMatch = node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    var anyChildMatch = false;
    foreach (var child in node.Children)
    {
        if (ApplyTreeFilterRecursive(child, filter))
            anyChildMatch = true;
    }
    
    node.IsVisible = selfMatch || anyChildMatch;
    // Filter 命中时展开祖先节点
    if (selfMatch && !string.IsNullOrEmpty(filter))
        node.IsExpanded = true;
    
    return node.IsVisible;
}
```

`FolderNode` 新增 `IsVisible` 属性（绑定到 TreeViewItem 的 IsVisible）：
```csharp
private bool _isVisible = true;
public bool IsVisible
{
    get => _isVisible;
    set
    {
        if (_isVisible != value)
        {
            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }
}
```

TreeView DataTemplate 中已有 `ItemsSource="{Binding Children}"`——Avalonia 的 `TreeDataTemplate` 不会自动根据子节点可见性隐藏父节点。需要额外的容器样式或代码处理。两种方案：

**方案 A（推荐）**：绑定 `TreeViewItem.IsVisible` 到 `FolderNode.IsVisible`（Style Setter），由 UI 框架处理可见性。Avalonia 的 TreeView 递归处理父节点可见性——父节点自身 visible 但所有子节点 invisible 时仍然显示。
**方案 B**：在 `ApplyTreeFilterRecursive` 中物理移除/添加子节点——更彻底但破坏展开状态。

采用方案 A，追加 Style：
```xml
<Style Selector="TreeViewItem">
    <Setter Property="IsExpanded" Value="{Binding IsExpanded}" />
    <Setter Property="IsVisible" Value="{Binding IsVisible}" />
</Style>
```

**清除过滤**：当 `TreeFilterText` 为空时，重置所有节点 `IsVisible = true` 并保留当前展开状态。

### 7. 文件列表新增按钮（候选）

#### 7a. 复制名称

直接在文件列表工具栏绑定现有命令：

```xml
<Button Classes="ToolbarButton"
        Command="{Binding CopyFileNameCommand}"
        IsEnabled="{Binding IsArchiveLoaded}"
        ToolTip.Tip="{Binding LocalizedStrings[Toolbar_CopyName]}">
    <TextBlock Text="📋" FontSize="13" />
</Button>
```

`CopyFileNameCommand` 已存在，无需新代码。

#### 7b. 列选择器（☰）

按钮点击弹出 Popup 显示列的 CheckBox 列表。需要 View 层代码支持：

```xml
<Button x:Name="ColumnPickerButton" Classes="ToolbarButton"
        IsEnabled="{Binding IsArchiveLoaded}"
        ToolTip.Tip="{Binding LocalizedStrings[Toolbar_Columns]}">
    <TextBlock Text="☰" FontSize="13" />
</Button>
<Popup IsOpen="{Binding #ColumnPickerButton.IsPressed}"
       Placement="Bottom"
       StaysOpen="False">
    <Border Background="{DynamicResource ThemeSurfaceBgBrush}"
            BorderBrush="{DynamicResource ThemeBorderBrush}"
            BorderThickness="1"
            Padding="8,4">
        <StackPanel x:Name="ColumnCheckList">
            <!-- 运行时动态从 FileListGrid.Columns 生成 -->
        </StackPanel>
    </Border>
</Popup>
```

实现方式：在 MainWindow.axaml.cs 中处理 Popup 打开事件，遍历 `FileListGrid.Columns`（跳过名称列），生成 CheckBox 列表绑定列显隐。

更简单可靠的替代方案：用 `ContextMenu` 替代 Popup，通过按钮右键或左键触发：

```csharp
private void ColumnPickerButton_Click(object? sender, RoutedEventArgs e)
{
    var menu = new ContextMenu();
    foreach (var column in FileListGrid.Columns)
    {
        var header = GetColumnHeader(column);
        if (header == "Name") continue; // 名称列不可隐藏
        
        var menuItem = new MenuItem
        {
            Header = header,
            IsChecked = column.IsVisible,
            Tag = column
        };
        menuItem.Click += (s, args) => {
            column.IsVisible = !column.IsVisible;
            menuItem.IsChecked = column.IsVisible;
        };
        menu.Items.Add(menuItem);
    }
    menu.Open(ColumnPickerButton);
}
```

采用 **ContextMenu 方案**，按钮单击弹出列显隐菜单，与现有列标题右键菜单行为一致但更易发现。

#### 7c. 刷新

```xml
<Button Classes="ToolbarButton"
        Command="{Binding RefreshArchiveCommand}"
        IsEnabled="{Binding IsArchiveLoaded}"
        ToolTip.Tip="{Binding LocalizedStrings[Toolbar_Refresh]}">
    <TextBlock Text="🔄" FontSize="13" />
</Button>
```

`RefreshArchiveCommand` 已存在。

#### 7d. 选择模式：全选 / 反选

ViewModel 新增命令：

```csharp
[RelayCommand]
private void SelectAll()
{
    // 全选逻辑在 View 层实现（DataGrid.SelectAll）
    // 通过回调委托让 View 执行
    SelectAllEntriesAction?.Invoke();
}

[RelayCommand]
private void InvertSelection()
{
    InvertSelectionAction?.Invoke();
}

/// <summary>由 View 设置的全选回调</summary>
public Action? SelectAllEntriesAction { get; set; }

/// <summary>由 View 设置的反选回调</summary>
public Action? InvertSelectionAction { get; set; }
```

MainWindow.axaml.cs 中设置：
```csharp
vm.SelectAllEntriesAction = () => FileListGrid.SelectAll();
vm.InvertSelectionAction = () => {
    var selected = FileListGrid.SelectedItems.Cast<object>().ToHashSet();
    var allItems = FileListGrid.Items.Cast<object>().ToList();
    FileListGrid.SelectedItems.Clear();
    foreach (var item in allItems)
    {
        if (!selected.Contains(item))
            FileListGrid.SelectedItems.Add(item);
    }
};
```

工具栏按钮：
```xml
<Button Classes="ToolbarButton"
        Command="{Binding SelectAllCommand}"
        IsEnabled="{Binding IsArchiveLoaded}"
        ToolTip.Tip="{Binding LocalizedStrings[Toolbar_SelectAll]}">
    <TextBlock Text="◐" FontSize="13" />
</Button>
<Button Classes="ToolbarButton"
        Command="{Binding InvertSelectionCommand}"
        IsEnabled="{Binding IsArchiveLoaded}"
        ToolTip.Tip="{Binding LocalizedStrings[Toolbar_InvertSelection]}">
    <TextBlock Text="⊗" FontSize="13" />
</Button>
```

#### 7e. 视图过滤（All / Files / Directories）

ViewModel 新增枚举和三态切换命令：

```csharp
public enum FileListViewMode { All, FilesOnly, DirectoriesOnly }

[ObservableProperty]
private FileListViewMode _viewMode = FileListViewMode.All;

partial void OnViewModeChanged(FileListViewMode value)
{
    ApplyViewModeFilter();
}

private void ApplyViewModeFilter()
{
    // 刷新 PopulateEntries 时结合 _viewMode 过滤
    // 在 PopulateEntries 的 GetEntriesInFolder 之后进行二次过滤
}
```

在 `PopulateEntries` 中，对 `ArchiveEntryLister.GetEntriesInFolder` 的结果做二次过滤：
```csharp
var entries = ArchiveEntryLister.GetEntriesInFolder(
    filteredSource, CurrentFolder ?? "", ShowSubfolders);

if (ViewMode == FileListViewMode.FilesOnly)
    entries = entries.Where(e => !e.IsDirectory).ToList();
else if (ViewMode == FileListViewMode.DirectoriesOnly)
    entries = entries.Where(e => e.IsDirectory).ToList();
```

工具栏实现为三态按钮（循环切换）或三个 RadioButton：

**三态按钮方案（推荐）**：
```xml
<Button Classes="ToolbarButton"
        Command="{Binding CycleViewModeCommand}"
        IsEnabled="{Binding IsArchiveLoaded}"
        ToolTip.Tip="{Binding LocalizedStrings[Toolbar_ViewMode]}">
    <TextBlock Text="▧" FontSize="13" />
</Button>
<TextBlock Text="{Binding ViewModeLabel}"
           FontSize="10"
           Foreground="{DynamicResource ThemeTextSecondaryBrush}"
           VerticalAlignment="Center" />
```

ViewModel：
```csharp
[RelayCommand]
private void CycleViewMode()
{
    ViewMode = ViewMode switch
    {
        FileListViewMode.All => FileListViewMode.FilesOnly,
        FileListViewMode.FilesOnly => FileListViewMode.DirectoriesOnly,
        _ => FileListViewMode.All
    };
}

public string ViewModeLabel => ViewMode switch
{
    FileListViewMode.All => "All",
    FileListViewMode.FilesOnly => "Files",
    _ => "Dirs"
};
```

### 8. 文件列表工具栏完整布局（整合）

整合后文件列表工具栏按钮顺序（从左到右）：

```
🏠  ↑  ◀  ▶  ▏ [/dir1/sub] ▼  ▏ 📋  ☰  ▧  ▏ ◐  ⊗  ▏ 🔄  ▏ 🔍  📂
│  │  │  │    地址栏         │ 复制 列  视图│全选 反选│刷新│筛选 展平│
│  │  │  │                   │ 名称 选择 模式│         │    │         │
│  │  │  │                   └─操作组───────┴─选择组───┴刷新┴─显示组─┘
│  │  │  └─前进
│  │  └─后退
│  └─父目录
└─根目录
```

### 9. 新 i18n Key

在 `MainWindow.axaml.cs` 中添加事件处理：

```csharp
private void AddressBar_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
{
    if (e.Key == Avalonia.Input.Key.Enter)
    {
        if (sender is AutoCompleteBox box)
        {
            var vm = DataContext as MainWindowViewModel;
            vm?.NavigateToFolderPath(box.Text ?? "");
        }
        e.Handled = true;
    }
}
```

### 7. 导航历史集成到现有导航入口

现有三条导航入口都需要记录历史：

1. `OnSelectedFolderChanged` → `NavigateToFolder(node)` → 记录历史
2. `NavigateToFolderPath(path)` → 记录历史
3. `GoUp()` → 改为调用 `NavigateToFolderPath` 复用统一逻辑

方案：在 `PopulateEntries()` 中记录历史，或在 `NavigateToFolder`/`NavigateToFolderPath` 内部统一入口处记录。

最佳实践：封装一个统一导航方法 `private void NavigateAndPushHistory(string path)`，所有的公共导航入口最终都走此方法。`GoUp` 改为调用此方法。

```csharp
private void NavigateAndPushHistory(string path)
{
    if (_allRawItems == null) return;
    
    // 如果已经在导航目标位置，跳过
    if (CurrentFolder == path) return;
    
    // 推当前路径到后退栈
    if (CurrentFolder != null)
        _backStack.Add(CurrentFolder);
    _forwardStack.Clear();
    
    // 更新属性并刷新
    CurrentFolder = path;
    PopulateEntries();
    
    // 同步目录树选择
    var node = FindNode(FolderTreeRoot, path);
    if (node != null)
        SelectedFolder = node;
    
    OnPropertyChanged(nameof(CanGoBack));
    OnPropertyChanged(nameof(CanGoForward));
}
```

### 8. 新命令实现

```csharp
[RelayCommand]
private void ExpandAll()
{
    FolderTreeRoot?.ExpandAll();
}

[RelayCommand]
private void CollapseAll()
{
    FolderTreeRoot?.CollapseAll();
}

[RelayCommand]
private void ExpandToCurrent()
{
    if (FolderTreeRoot == null || SelectedFolder == null) return;
    // 先全部收起
    FolderTreeRoot.CollapseAll();
    // 再展开到当前选中路径
    ExpandAncestorsOf(FolderTreeRoot, SelectedFolder.FullPath);
}

private static void ExpandAncestorsOf(FolderNode node, string targetPath)
{
    if (node.FullPath == targetPath) return;
    foreach (var child in node.Children)
    {
        if (IsAncestorOf(child, targetPath))
        {
            child.IsExpanded = true;
            ExpandAncestorsOf(child, targetPath);
            return;
        }
    }
}

private static bool IsAncestorOf(FolderNode node, string targetPath)
{
    if (node.FullPath == targetPath) return true;
    return node.Children.Any(c => IsAncestorOf(c, targetPath));
}

[RelayCommand]
private void GoRoot()
{
    if (FolderTreeRoot == null) return;
    NavigateAndPushHistory("");
}

[RelayCommand]
private void GoBack()
{
    if (_backStack.Count == 0) return;
    var target = _backStack[^1];
    _backStack.RemoveAt(_backStack.Count - 1);
    
    // 保存当前路径到前进栈
    if (CurrentFolder != null)
        _forwardStack.Add(CurrentFolder);
    
    CurrentFolder = target;
    NavigateToFolder(FindNode(FolderTreeRoot, target) ?? FolderTreeRoot);
    
    OnPropertyChanged(nameof(CanGoBack));
    OnPropertyChanged(nameof(CanGoForward));
}

[RelayCommand]
private void GoForward()
{
    if (_forwardStack.Count == 0) return;
    var target = _forwardStack[^1];
    _forwardStack.RemoveAt(_forwardStack.Count - 1);
    
    if (CurrentFolder != null)
        _backStack.Add(CurrentFolder);
    
    CurrentFolder = target;
    NavigateToFolder(FindNode(FolderTreeRoot, target) ?? FolderTreeRoot);
    
    OnPropertyChanged(nameof(CanGoBack));
    OnPropertyChanged(nameof(CanGoForward));
}
```

### 10. 新 i18n Key

```json
// strings.zh-CN.json
"Tree_ExpandAll": "展开所有",
"Tree_CollapseAll": "收起所有",
"Tree_ExpandToCurrent": "展开到当前",
"Tree_Filter": "过滤目录...",
"Nav_GoRoot": "根目录",
"Nav_GoBack": "后退",
"Nav_GoForward": "前进",
"Nav_AddressBar": "路径...",
"Toolbar_CopyName": "复制名称",
"Toolbar_Columns": "列选择器",
"Toolbar_Refresh": "刷新",
"Toolbar_SelectAll": "全选",
"Toolbar_InvertSelection": "反选",
"Toolbar_ViewMode": "视图过滤",
"ViewMode_All": "All",
"ViewMode_Files": "Files",
"ViewMode_Dirs": "Dirs",

// strings.en.json
"Tree_ExpandAll": "Expand All",
"Tree_CollapseAll": "Collapse All",
"Tree_ExpandToCurrent": "Expand to Current",
"Tree_Filter": "Filter tree...",
"Nav_GoRoot": "Root",
"Nav_GoBack": "Back",
"Nav_GoForward": "Forward",
"Nav_AddressBar": "Path...",
"Toolbar_CopyName": "Copy Name",
"Toolbar_Columns": "Columns",
"Toolbar_Refresh": "Refresh",
"Toolbar_SelectAll": "Select All",
"Toolbar_InvertSelection": "Invert",
"Toolbar_ViewMode": "View",
"ViewMode_All": "All",
"ViewMode_Files": "Files",
"ViewMode_Dirs": "Dirs",
```

同时在 ViewModel 的 `UpdateLocalizedStrings` 中注册这些 key。

## 实施步骤

| 步骤 | 内容 | 文件 |
|------|------|------|
| 1 | FolderNode 添加 ExpandAll/CollapseAll/IsVisible | `Core/Services/ArchiveTreeBuilder.cs` |
| 2 | ViewModel 添加导航历史栈、Back/Forward/Root 命令 | `ViewModels/MainWindowViewModel.cs` |
| 3 | ViewModel 添加 FolderPaths 集合 + NavigateAndPushHistory | 同上 |
| 4 | ViewModel 添加 TreeFilterText + ApplyTreeFilter + IsVisible 重置 | 同上 |
| 5 | ViewModel 添加 ViewMode + CycleViewMode + SelectAll/InvertSelection 回调委托 | 同上 |
| 6 | i18n key 写入 + UpdateLocalizedStrings 注册 | `Localization/strings.*.json` + `MainWindowViewModel.cs` |
| 7 | MainWindow.axaml: 目录树面板（树工具栏 + 搜索框 + 树 + IsExpanded/IsVisible 样式） | `Views/MainWindow.axaml` |
| 8 | MainWindow.axaml: 文件列表面板（导航 + 地址栏 + 复制名 + 列选择器 + 刷新 + 选择 + 视图 + 筛选/展平） | 同上 |
| 9 | MainWindow.axaml: 全局工具栏精简（移除筛选、展平按钮） | 同上 |
| 10 | MainWindow.axaml.cs: AddressBar_KeyDown + ColumnPicker 逻辑 + SelectAll/InvertSelection 回调设置 | `Views/MainWindow.axaml.cs` |
| 11 | 构建验证 | `dotnet build` |

## 注意事项

- `CanGoBack` / `CanGoForward` 需要手动触发 `PropertyChanged` 通知，因为 `[ObservableProperty]` 不能用于只读计算属性
- `TreeView` 的 `SelectedItem` 绑定和 `IsExpanded` style 绑定不能冲突——通过 tree node 展开也算展开
- `GoUp` 命令现在应改为通过 `NavigateAndPushHistory` 走统一导航路径
- 全局工具栏现有的 `IsFilterBarVisible` ToggleButton 和 `ShowSubfolders` ToggleButton 完全移除
- 地址栏 `AutoCompleteBox` 的 `Text` 绑定是双向的——用户在地址栏输入后按 Enter 导航；程序导航后 `CurrentFolder` 变化会反映到地址栏
- 目录树工具栏按钮在没有加载压缩包时应该禁用（绑定 `IsArchiveLoaded`）
- 导航后退/前进按钮在没有历史时禁用（绑定 `CanGoBack`/`CanGoForward`）
- **树过滤性能**：`ApplyTreeFilterRecursive` 每次按键都遍历整个树，目录数量很大时可能有性能问题。考虑用 200ms Debounce（通过 `CancellationTokenSource` 实现）减少低效遍历
- **`IsVisible` 绑定在 Avalonia TreeView 中**：Avalonia 的 `TreeViewItem` 支持 `IsVisible` 绑定，但隐藏子节点不会自动隐藏父节点（父节点保留空白展开箭头）。这是可接受的行为——用户看到父节点但下面没有任何可见子节点时自然知道需要展开查看。如需更彻底隐藏，使用方案 B（物理移除），但会破坏展开状态
- **列选择器 ContextMenu**：每次打开都动态重建——简单可靠，不需要额外的状态管理
- **SelectAll/InvertSelection**：通过 Action 委托从 ViewModel 回调到 View 层，保持 ViewModel 对 Avalonia 控件的零引用
