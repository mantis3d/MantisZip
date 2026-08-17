# 文件列表「返回上级目录」行

> 在文件列表 DataGrid 顶部固定显示一行 `..`，点击可导航到上级目录，不受排序和过滤影响。
> **状态**: ✅ 已完成 | **阶段**: [████████████████████] (全部完成)

## 涉及文件

| 文件 | 改动 |
|------|------|
| `src/MantisZip.UI/MainWindow/MainWindow.Types.cs` | 新增 `IsNavigationEntry` 属性 |
| `src/MantisZip.UI/MainWindow/MainWindow.UI.cs` | `FilterFiles()` 插入 `..` 行；`RefreshFilter()` 追加 `..` 行；排序改为 `CustomSort`；双击 `..` 导航；统计排除 |
| `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs` | `CaptureCurrentSort` / `ApplySavedSort` 改为 `CustomSort` 路径；新增 `GetParentFolderPath()` 辅助 |
| `src/MantisZip.UI/MainWindow/MainWindow.xaml` | 可选：`..` 行特殊样式（文本颜色/背景微调） |
| `docs/PROGRESS.md` | 完成时记录 |
| `docs/PLAN.md` | 新增引用条目 |

## 设计决策

- **只在子目录显示**：`_currentFolder` 非空（非根目录）时才显示 `..` 行
- **排序**：放弃 `SortDescriptions`，改用 `ICollectionView.CustomSort` + 自定义 `IComparer<ArchiveItem>`。比较逻辑：导航行第一 → `SortOrder`（目录/文件分离，兼容 `SeparateDirBaseline`）→ 列排序
- **过滤**：`RefreshFilter()` 在 `ApplyFilters` 结果后手动追加 `..`，不受文字/日期/大小过滤影响
- **选中处理**：`..` 行可被点中但双击导航后自动清除选中；选中时状态栏统计排除它
- **拖拽**：`..` 行不可拖拽（`PreviewMouseMove` 中跳过）
- **右键菜单**：`..` 行不响应文件操作右键菜单

## 实施步骤

### Step 1：添加 `IsNavigationEntry` 属性

**文件**: `src/MantisZip.UI/MainWindow/MainWindow.Types.cs`

在 `ArchiveItem` UI 子类中添加：

```csharp
/// <summary>是否为"返回上级目录"导航行</summary>
public bool IsNavigationEntry { get; set; }
```

### Step 2：自定义排序器

**文件**: `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs`

新增 `NavigationEntryFirstComparer : IComparer<ArchiveItem>`：

```
规则：
1. IsNavigationEntry == true 的项永远返回 -1（排最前）
2. 启用 SeparateDirBaseline 时：目录（SortOrder=0）排在文件（SortOrder=1）前面
3. 按列排序（从 _savedSortColumnPath / _savedSortDirection 读取排序属性）
```

### Step 3：修改 `FilterFiles` 插入 `..` 行

**文件**: `src/MantisZip.UI/MainWindow/MainWindow.UI.cs`

在 `FilterFiles()` 方法中，`directItems` 构建完成后：

```csharp
if (!string.IsNullOrEmpty(_currentFolder))
{
    directItems.Insert(0, new ArchiveItem
    {
        Name = "..",
        FullPath = GetParentFolderPath(_currentFolder),
        IsDirectory = true,
        IsNavigationEntry = true,
        DisplayName = "..",
        IconSource = SystemIconHelper.GetFolderIcon(),
    });
}
```

### Step 4：修改排序机制（SortDescriptions → CustomSort）

**文件**: `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs` + `MainWindow.UI.cs`

- `ApplySavedSort()`：不再设置 `view.SortDescriptions`，改为 `view.CustomSort = new NavigationEntryFirstComparer(...)`
- `FileListGrid_Sorting()`：不再操作 `SortDescriptions`，只更新 `_savedSortColumnPath` / `_savedSortDirection`，然后重建 `CustomSort`
- `CaptureCurrentSort()`：逻辑不变（仍然从列头捕获排序状态）

### Step 5：修改 `RefreshFilter` 追加 `..` 行

**文件**: `src/MantisZip.UI/MainWindow/MainWindow.UI.cs`

过滤结果后，在设置 `ItemsSource` 之前追加：

```csharp
if (!string.IsNullOrEmpty(_currentFolder))
{
    result.Insert(0, new ArchiveItem
    {
        Name = "..",
        FullPath = GetParentFolderPath(_currentFolder),
        IsDirectory = true,
        IsNavigationEntry = true,
        DisplayName = "..",
        IconSource = SystemIconHelper.GetFolderIcon(),
    });
}
```

### Step 6：双击 `..` 导航

**文件**: `src/MantisZip.UI/MainWindow/MainWindow.UI.cs`

在 `FileListGrid_PreviewMouseDoubleClick` 中增加分支：

```csharp
if (item.IsNavigationEntry)
{
    var parentPath = GetParentFolderPath(item.FullPath);
    FilterFiles(parentPath);
    SelectFolderInTree(parentPath);
    e.Handled = true;
    return;
}
```

新增 `GetParentFolderPath()` 辅助方法（取路径的最后一级 `/` 之前的内容，根目录返回空字符串）。

### Step 7：排除 `..` 行的副作用

**文件**: `src/MantisZip.UI/MainWindow/MainWindow.UI.cs`

- `UpdateSelectionStats()`：跳过 `IsNavigationEntry` 的项
- `FileListGrid_PreviewMouseMove()`（拖拽）：跳过 `IsNavigationEntry` 的项
- `FileListGrid_SelectionChanged`：选中 `..` 时不触发预览
- 右键菜单：在 `ContextMenuOpening` 或各个 Click handler 中判断跳过

### Step 8：编译验证

```powershell
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
```

确认无编译错误，运行确认：
- 进入子目录后看到 `..` 行
- 点击列排序（升序/降序/取消）时 `..` 始终在最顶
- 输入过滤条件时 `..` 仍然显示
- 双击 `..` 回到上级目录
- 根目录不显示 `..` 行
- 拖拽操作不触发 `..` 行
- 选中统计排除 `..` 行

---

## 注意事项

- `ICustomComparer` 设置后会覆盖 `SortDescriptions`，需要一次性把所有排序逻辑（导航优先 + 目录/文件分离 + 列排序）都实现在 comparer 内
- `SeparateDirBaseline` 的开关状态变化时需要重建 `CustomSort`（`FileListGrid.Items.Refresh()` 可能不够，需要重新设 `view.CustomSort`）
- `CollectionViewSource.GetDefaultView()` 返回的 `ICollectionView` 在不同 .NET 版本中可能有差异，需确认 `.NET 9` 下的行为
- `..` 的 `FullPath` 计算：只需去掉 `_currentFolder` 最后一个 `/` 及之后的内容。若去掉后为空，表示回到根目录（`_currentFolder = ""`）
