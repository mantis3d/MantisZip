# 文件大小进度条

## 目标

在文件列表的「大小」列中，用背景进度条直观展示文件之间的相对大小关系。填充度 = 当前文件大小 / 列表中最大文件大小，无需额外交互即可一眼看出哪些是"大文件"。

## 实现方案

纯 UI 层改动，不涉及 Core 引擎：

### 1. ArchiveItem 新增属性 `SizeRatio`

`MainWindow.UI.cs` → `ArchiveItem` 类增加只读计算属性：

```csharp
public double SizeRatio { get; set; } // 0.0 ~ 1.0
```

### 2. 列表加载/过滤时计算

在 `LoadArchiveAsync` 和 `FilterFiles` 中，遍历 `_allItems` / 当前展示列表，取 `maxSize`，为每个 item 赋值：

```csharp
var maxSize = items.Max(i => i.Size);
if (maxSize > 0)
{
    foreach (var item in items)
        item.SizeRatio = (double)item.Size / maxSize;
}
```

### 3. XAML 模板替换

把 `DataGridTextColumn Header="大小"` 替换为 `DataGridTemplateColumn`：

```xml
<DataGridTemplateColumn Header="大小" Width="100">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <Grid>
                <Rectangle Fill="#E0E7FF" HorizontalAlignment="Left"
                           Width="{Binding SizeRatio, Converter={StaticResource RatioToWidthConverter}}"/>
                <TextBlock Text="{Binding SizeDisplay}" VerticalAlignment="Center"
                           Margin="4,0"/>
            </Grid>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

`RatioToWidthConverter` 接收 `SizeRatio`（0~1）和 `Width` 参数（从列宽或 ActualWidth 来），返回 `ratio * width`。

或者更简单——`Rectangle` 绑定 `ColumnDefinition.Width` 为 `*{ratio}`（Star 比例绑定），或用 `ActualWidth` 在 converter 中计算。

## 交互细节

| 场景 | 行为 |
|------|------|
| 切换目录 | 重新计算 maxSize 和所有 item 的 SizeRatio |
| 过滤文件 | 仅对当前可见列表计算；子目录视为特殊的条目但 Size=0 → 0% |
| 排序 | 进度条随行移动，不单独处理 |
| 暗色主题 | Rectangle Fill 颜色需跟随主题 |

## 工作量

- **代码改动**: ~10 行 C# + ~15 行 XAML
- **新文件**: 1 个值转换器（`RatioToWidthConverter`）或复用现有转换器
- **测试**: 无需，纯视觉
