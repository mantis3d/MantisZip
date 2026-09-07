# Phase 9: 文件列表交互补齐 — 双击/键盘/排序

## 目标

将 Avalonia 版 MainWindow 文件列表（DataGrid）的交互行为补齐到 WPF 版同等水平：双击目录进入、Enter/Backspace/Delete 键盘操作、列排序（NavigationEntryFirstComparer：`..` 始终最前 + 目录优先于文件 + 排序状态跨目录恢复）。

## 前置条件

- Phase 8（设置窗口完整标签页）已完成
- 当前分支：`avalonia-port`

## 当前状态

**Avalonia ViewModel 已有基础设施：**
- ✅ `SelectedEntry` → `OnSelectedEntryChanged()` → 自动触发 `ShowPreviewAsync()`（预览已工作）
- ✅ `GoUp()` RelayCommand — 返回上级目录（通过 FolderTree 节点操作）
- ✅ `NavigateToFolder(FolderNode node)` — 通过树节点导航
- ✅ `PopulateEntries()` — 刷新文件列表
- ✅ `FilterFiles()` — 重应用过滤条件
- ✅ `DeleteFilesCommand` RelayCommand — 删除选中条目
- ✅ `_isProgrammaticFilter` guard — 防止程序化刷新触发 SelectionChanged 循环
- ❌ **缺少 `NavigateToFolderPath(string path)`** — 从文件列表双击目录进入时，只有 path 字符串，没有 FolderNode

**Avalonia 代码后置缺失：**
- ❌ DataGrid 无任何事件绑定（WPF 有 6 个：SelectionChanged, PreviewMouseDoubleClick, PreviewMouseLeftButtonDown, PreviewMouseMove, PreviewKeyDown, Sorting）
- ❌ 拖拽已实现（PointerPressed + PointerMoved），但双击/键盘/排序全部缺失

## 涉及文件

| 文件 | 改动量 | 说明 |
|------|:------:|------|
| `ViewModels/MainWindowViewModel.cs` | ~15 行新增 | 添加 `NavigateToFolderPath(string)` 方法 |
| `Views/MainWindow.axaml` | ~10 行 | DataGrid 添加事件绑定：`DoubleTapped`/`KeyDown`/`Sorting` |
| `Views/MainWindow.axaml.cs` | ~80 行新增 | 3 个事件处理程序 |

## 任务分解

### Task 1: ViewModel 添加 `NavigateToFolderPath(string path)`

**文件：** `ViewModels/MainWindowViewModel.cs`

**内容：**
```csharp
/// <summary>
/// 从文件列表双击目录时按路径导航（无需 FolderNode）。
/// </summary>
public void NavigateToFolderPath(string path)
{
    if (_allRawItems == null) return;
    _isProgrammaticFilter = true;
    try
    {
        CurrentFolder = path;
        PopulateEntries();
        // 同步选中目录树中的对应节点
        var node = FindNode(FolderTreeRoot, path);
        SelectedFolder = node; // 会触发 OnSelectedFolderChanged → NavigateToFolder
    }
    finally
    {
        _isProgrammaticFilter = false;
    }
}
```

**注意：** `SelectedFolder` 的 setter 已经通过 `OnSelectedFolderChanged` → `NavigateToFolder` 链调用 `PopulateEntries`。设置 `_isProgrammaticFilter` 可以防止 `OnSelectedFolderChanged` 路径产生重复刷新。

实际上更好的实现：直接设置 `CurrentFolder = path` 然后 `PopulateEntries()`，不需要经过 `SelectedFolder` 的 setter（因为树节点可能不存在于根目录 `""` 的 children 中）。

```csharp
public void NavigateToFolderPath(string path)
{
    if (_allRawItems == null) return;
    _isProgrammaticFilter = true;
    try
    {
        CurrentFolder = path;
        PopulateEntries();
        // 可选：尝试选中树节点
        var node = FindNode(FolderTreeRoot, path);
        if (node != null)
            SelectedFolder = node;
    }
    finally
    {
        _isProgrammaticFilter = false;
    }
}
```

### Task 2: DataGrid 事件绑定

**文件：** `Views/MainWindow.axaml`

**改动：** 在 DataGrid (x:Name="FileListGrid") 上添加事件绑定：

```xml
<DataGrid x:Name="FileListGrid"
          ...
          DoubleTapped="FileListGrid_DoubleTapped"
          KeyDown="FileListGrid_KeyDown"
          Sorting="FileListGrid_Sorting">
```

保留现有 ItemsSource/SelectedItem/ContextMenu 不变。

### Task 3: 双击目录进入 + 打开文件

**文件：** `Views/MainWindow.axaml.cs`

**方法：**
```csharp
private void FileListGrid_DoubleTapped(object? sender, TappedEventArgs e)
{
    var grid = sender as DataGrid;
    if (grid?.SelectedItem is ArchiveItemModel item)
    {
        if (item.IsDirectory)
        {
            // 进入目录
            if (DataContext is MainWindowViewModel vm)
                vm.NavigateToFolderPath(item.FullPath);
        }
        // 文件：可以打开/预览（Avalonia 的 SelectedItem 绑定已触发 OnSelectedEntryChanged → ShowPreviewAsync）
    }
}
```

### Task 4: 键盘导航（Enter / Backspace / Delete）

**文件：** `Views/MainWindow.axaml.cs`

**方法：**
```csharp
private void FileListGrid_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
{
    var grid = sender as DataGrid;
    if (grid?.SelectedItem is not ArchiveItemModel item) return;
    if (DataContext is not MainWindowViewModel vm) return;

    switch (e.Key)
    {
        case global::Avalonia.Input.Key.Enter:
            if (item.IsDirectory)
            {
                vm.NavigateToFolderPath(item.FullPath);
                e.Handled = true;
            }
            break;

        case global::Avalonia.Input.Key.Back:
            // 返回上级目录
            vm.GoUpCommand.Execute(null);
            e.Handled = true;
            break;

        case global::Avalonia.Input.Key.Delete:
            vm.DeleteFilesCommand.Execute(null);
            e.Handled = true;
            break;
    }
}
```

**注意：** 使用 `vm.GoUpCommand.Execute(null)` 需要检查 `GoUp` 是否为 RelayCommand。如果是 `[RelayCommand] private void GoUp()`，生成的属性名为 `GoUpCommand`。

如果 GoUp 是 private method 而不是命令，需要改为直接调用 ViewModel 方法或通过 XAML 命令绑定。查看 ViewModel 后确认为：
- `[RelayCommand] private void GoUp()` → 生成 `GoUpCommand`
- `[RelayCommand] private async Task DeleteFiles()` → 生成 `DeleteFilesCommand`

确认 GoUp 是 RelayCommand。可以安全使用 `vm.GoUpCommand.Execute(null)`。

### Task 5: 列排序（`..` 置顶 + 目录优先 + 排序持久化）

**难度：** 中。Avalonia DataGrid 的排序机制与 WPF 不同：
- WPF 有 `Sorting` 事件 + `CustomSort`（`ListCollectionView.CustomSort`）
- Avalonia 的 `DataGrid` 支持 `Sorting` 事件，但自定义排序需要通过 `DataGrid.Sorting` 事件 + 手动管理

**核心逻辑（参考 WPF `FileListGrid_Sorting` 和 `NavigationEntryFirstComparer`）：**

```csharp
private void FileListGrid_Sorting(object? sender, DataGridSortingEventArgs e)
{
    var grid = sender as DataGrid;
    if (grid == null) return;

    // 清除所有列标题上的排序标记
    foreach (var column in grid.Columns)
    {
        if (column.Header is string headerText)
            column.Header = headerText.TrimEnd('▲', '▼', ' ').TrimEnd();
    }

    var col = e.Column;
    // 推算新方向（排序事件触发时 col.SortDirection 还是旧值）
    DataGridSortDirection? newDir = col.SortDirection switch
    {
        null => DataGridSortDirection.Ascending,
        DataGridSortDirection.Ascending => DataGridSortDirection.Descending,
        DataGridSortDirection.Descending => null,
        _ => null
    };

    // 阻止默认排序（Avalonia 默认按值排序，我们需要自定义）
    e.Handled = true;
    col.SortDirection = newDir;

    // 更新列头箭头
    if (col.Header is string header)
    {
        var clean = header.TrimEnd('▲', '▼', ' ').TrimEnd();
        col.Header = newDir switch
        {
            DataGridSortDirection.Ascending => clean + " ▲",
            DataGridSortDirection.Descending => clean + " ▼",
            _ => clean
        };
    }

    // 对 CurrentEntries 手动排序
    if (DataContext is MainWindowViewModel vm)
    {
        var entries = vm.CurrentEntries.ToList();
        var sortMemberPath = col.SortMemberPath;
        var sortDesc = newDir == DataGridSortDirection.Descending;

        // 自定义比较器：.. 导航行 → 目录 → 文件，每层内部按排序列排序
        var sorted = entries
            .OrderBy(e => e.Name == ".." ? 0 : e.IsDirectory ? 1 : 2)  // Type group
            .ThenBy(e => sortDesc
                ? GetSortValue(e, sortMemberPath).Item2  // 降序时反向排序
                : GetSortValue(e, sortMemberPath).Item1)  // 升序
            .ToList();

        vm.CurrentEntries.Clear();
        foreach (var item in sorted)
            vm.CurrentEntries.Add(item);
    }
}

// 从 ArchiveItemModel 按 SortMemberPath 提取排序值
private static (IComparable asc, IComparable desc) GetSortValue(ArchiveItemModel item, string memberPath)
{
    return memberPath switch
    {
        "Name" or "NameDisplay" => (item.NameDisplay, item.NameDisplay),  // 反向时字符串反转
        "Size" => (item.Size, -item.Size),
        "CompressedSize" => (item.CompressedSize, -item.CompressedSize),
        "LastModified" => (item.LastModified, DateTime.MaxValue - item.LastModified),
        "CompressionRatio" => (item.CompressionRatio, -item.CompressionRatio),
        _ => (item.NameDisplay, item.NameDisplay)
    };
}
```

**简化方案：** 由于 Avalonia DataGrid 的默认排序不支持自定义 IComparer，Task 5 可以考虑先实现一个简化版：
1. `Sorting` 事件 → 阻止默认排序 → 手动在 CurrentEntries 上做 `OrderBy` + `ThenBy`
2. `..` 导航行通过 `ArchiveItemModel.HasNavigationEntry`（或通过 Name == ".." 判断）永远置顶
3. 目录（`IsDirectory == true`）在文件前
4. 保存排序列 + 方向到 ViewModel 属性，`PopulateEntries()` 时恢复

**持久化排序状态：** WPF 版有 `_savedSortColumnPath` / `_savedSortDirection`，跨目录切换时恢复排序。Avalonia 版需要：
- ViewModel 添加 `SortColumnPath` 和 `SortDirection` 属性
- 在 `PopulateEntries()` 末尾应用保存的排序

### Task 6: 过滤/搜索面板验证

检查 Avalonia MainWindow.axaml 是否有以下过滤控件：
- `FilterTextBox`（文本搜索）
- `ShowSubfoldersCheck`（显示子目录开关）
- `ExcludeBox`（排除文本）

对比 WPF `MainWindow.xaml` 的完整过滤面板，验证是否有缺失。

### 验证

1. `dotnet build` — 0 错误 0 警告
2. 打开压缩包 → 双击目录 → 进入该目录
3. 选中目录后按 Enter → 进入该目录
4. 按 Backspace → 返回上级目录
5. 选中文件后按 Delete → 显示删除确认 → 删除
6. 点击列头 → 排序生效，`..` 在最前，目录在文件前
7. 切换目录 → 排序状态保持（同一列的排序方向和方式不变）
8. 运行 `dotnet run` 确认应用正常启动

## 参考

- WPF 双击处理：`MainWindow.UI.cs` line 746-754
- WPF 键盘处理：`MainWindow.Menu.cs` line 325-358
- WPF 排序处理：`MainWindow.UI.cs` line 849-888
- WPF 排序比较器：`MainWindow.xaml.cs` line 457-500（`NavigationEntryFirstComparer`）
- Avalonia ViewModel 目录导航：`MainWindowViewModel.cs` line 514-518（`NavigateToFolder`）
- Avalonia ViewModel 上级目录：`MainWindowViewModel.cs` line 593-604（`GoUp`）
- Avalonia 现有事件绑定参考：`MainWindow.axaml.cs` line 148-222（拖拽实现）
