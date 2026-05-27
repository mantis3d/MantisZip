# 文件列表进度条扩展（大小 + 压缩率 + 日期）

> **状态**: 📋 待定 | **任务**: [⬜⬜⬜⬜⬜⬜⬜⬜] (0/8)

## 目标

在文件列表的「大小」「压缩率」「日期」三列中，用**背景进度条**直观展示每列的相对关系。让用户无需仔细读数字即可一眼看出：
- **大小列**：哪些是"大体积文件"（填充度高）
- **压缩率列**：哪些文件压缩效果好（填充度低 = 压缩率高）
- **日期列**：哪些文件是较新的（填充度高）

纯 UI 层改动，不涉及 Core 引擎。可通过 **视图 → 进度条** 菜单项一键开关。

---

## 安全分析（不会破坏现有功能）

### 逐项风险评估

| 改动点 | 风险 | 分析 |
|--------|------|------|
| **ArchiveItem 新增属性** | ✅ 安全 | 纯新增字段/属性，不修改任何既有成员签名 |
| **LoadArchiveAsync / FilterFiles 加计算循环** | ✅ 安全 | 在 items 组装完成后、ItemsSource 赋值前插入，不触及既有流程 |
| **XAML `DataGridTextColumn` → `DataGridTemplateColumn`** | ⚠️ 低风险 | 保留 `SortMemberPath` 确保排序不变；TextBlock 仍在原位显示文本，仅叠加背景 Rectangle |
| **`RatioToWidthConverter` 新文件** | ✅ 安全 | 纯新增，不影响现有转换器 |
| **主题资源（Light/Dark.xaml）** | ✅ 安全 | 纯新增颜色资源，不影响既有的 StaticResource 键 |
| **AppSettings 新增属性** | ✅ 安全 | 纯新增，JSON 序列化自动兼容 |

### 唯一需注意的渲染时序

MultiBinding 绑定 `ActualWidth` 时，在 DataGrid 首次加载/虚拟化回收阶段，`ContentPresenter.ActualWidth` 可能为 0，导致 Rectangle 短暂为 0 宽度。这是 WPF 绑定时序的正常行为，**不崩溃、不抛异常**，仅初次渲染时有极短暂瞬间（远小于 100ms，肉眼不可见）。

---

## 实现方案

### 视觉语义

| 列 | 语义 | 比例基准 | 颜色（亮色） | 颜色（暗色） |
|----|------|----------|-------------|-------------|
| **大小** | 相对体积 | 当前视图最大文件的 Size | `#E0E7FF` (蓝) | `#1E3A5F` (暗蓝) |
| **压缩率** | 压缩效率 | **绝对**：`CompressedSize/Size`，上限 1.0 | `#FDE68A` (琥珀) | `#5C4A1E` (暗琥珀) |
| **日期** | 相对新旧 | 当前视图最后修改日期跨度 | `#D1FAE5` (绿) | `#14532D` (暗绿) |

大小和日期用**相对比例**（同一视图中互相比较才有意义），压缩率用**绝对比例**（0% = 极致压缩，100% = 未压缩/膨胀，独立于列表上下文）。

---

### 1. `ArchiveItem` 新增属性 + 门控

`MainWindow.UI.cs` → `ArchiveItem` 类增加属性和门控字段：

```csharp
// ——— 进度条全局开关（由菜单切换） ———
public bool ProgressBarEnabled { get; set; } = true;

// ——— 大小（相对比例，列表加载/过滤时赋值） ———
public double SizeRatio { get; set; }   // 0.0 ~ 1.0

// ——— 压缩率（绝对比例，始终可计算） ———
public double RatioBarValue
{
    get
    {
        if (!ProgressBarEnabled || IsDirectory || Size == 0) return 0;
        var ratio = (double)CompressedSize / Size;
        return Math.Min(ratio, 1.0); // 封顶 1.0
    }
}

// ——— 日期（相对比例，列表加载/过滤时赋值） ———
public double DateRatio { get; set; }   // 0.0 ~ 1.0
```

### 2. 加载/过滤时计算 SizeRatio 和 DateRatio

两处都需要修改：`LoadArchiveAsync`（`MainWindow.xaml.cs ~line 445`）和 `FilterFiles`（`MainWindow.UI.cs ~line 156`）。

在组装好当前视图条目列表后、赋值 `ItemsSource` 之前，批量计算：

```csharp
bool showBars = AppSettings.Instance.ShowProgressBars;

// === 计算 SizeRatio ===
var maxSize = sortedItems.Max(i => i.Size);
if (maxSize > 0)
{
    foreach (var item in sortedItems)
    {
        item.ProgressBarEnabled = showBars;
        item.SizeRatio = showBars ? (double)item.Size / maxSize : 0;
    }
}

// === 计算 DateRatio ===
var dates = sortedItems.Where(i => i.LastModified > DateTime.MinValue).ToList();
if (dates.Count > 1)
{
    var minDate = dates.Min(i => i.LastModified);
    var maxDate = dates.Max(i => i.LastModified);
    var span = maxDate - minDate;
    if (span.TotalSeconds > 0)
    {
        foreach (var item in sortedItems)
        {
            if (item.LastModified > DateTime.MinValue)
                item.DateRatio = showBars
                    ? (item.LastModified - minDate).TotalSeconds / span.TotalSeconds
                    : 0;
        }
    }
}
else if (dates.Count == 1)
{
    dates[0].DateRatio = showBars ? 1.0 : 0;
}
// 无效日期的 DateRatio 保持默认 0.0 → 无进度条
```

**位置关键**：此计算必须在 `sortedItems` 放入 `FileListGrid.ItemsSource` 之前完成，确保绑定值已就绪。

#### 目录条目处理

在 `FilterFiles` 中，合成目录条目（`new ArchiveItem { ... }`）没有 `LastModified`，`Size=0`，`CompressedSize=0`：
- `SizeRatio` = `0/ maxSize` → 0.0（无进度条）✅
- `RatioBarValue` = 0（`IsDirectory=true` 提前返回）✅
- `DateRatio` = 0.0（`LastModified == DateTime.MinValue`）→ 0.0（无进度条）✅

### 3. XAML 三列模板替换 + Converter

#### RatioToWidthConverter（多值转换器，三列共用）

新建 `UI/Converters/RatioToWidthConverter.cs`：

```csharp
using System.Globalization;
using System.Windows.Data;

namespace MantisZip.UI.Converters;

public class RatioToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is double ratio && values[1] is double width)
            return ratio * width;
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

在 `MainWindow.xaml` 的 `Window.Resources` 中注册（用已有的 `app` / `local` 命名空间引入）：

```xml
<app:RatioToWidthConverter x:Key="RatioToWidthConverter"/>
```

#### 三列 XAML 模板

把三列的 `DataGridTextColumn`（line 318、320、322）替换为 `DataGridTemplateColumn`，Rectangle 用 MultiBinding：

```xml
<!-- 大小列（Blue） -->
<DataGridTemplateColumn Header="{l:L Main_Col_Size}" Width="100" SortMemberPath="Size">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <Grid>
                <Rectangle Fill="{StaticResource ProgressBarSizeBg}" HorizontalAlignment="Left">
                    <Rectangle.Width>
                        <MultiBinding Converter="{StaticResource RatioToWidthConverter}">
                            <Binding Path="SizeRatio"/>
                            <Binding Path="ActualWidth"
                                     RelativeSource="{RelativeSource AncestorType=ContentPresenter}"/>
                        </MultiBinding>
                    </Rectangle.Width>
                </Rectangle>
                <TextBlock Text="{Binding SizeDisplay}" VerticalAlignment="Center" Margin="4,0"/>
            </Grid>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>

<!-- 压缩率列（Amber） -->
<DataGridTemplateColumn Header="{l:L Main_Col_Ratio}" Width="80" SortMemberPath="RatioSort">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <Grid>
                <Rectangle Fill="{StaticResource ProgressBarRatioBg}" HorizontalAlignment="Left">
                    <Rectangle.Width>
                        <MultiBinding Converter="{StaticResource RatioToWidthConverter}">
                            <Binding Path="RatioBarValue"/>
                            <Binding Path="ActualWidth"
                                     RelativeSource="{RelativeSource AncestorType=ContentPresenter}"/>
                        </MultiBinding>
                    </Rectangle.Width>
                </Rectangle>
                <TextBlock Text="{Binding RatioDisplay}" VerticalAlignment="Center" Margin="4,0"/>
            </Grid>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>

<!-- 日期列（Green） -->
<DataGridTemplateColumn Header="{l:L Main_Col_Date}" Width="150" SortMemberPath="LastModified">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <Grid>
                <Rectangle Fill="{StaticResource ProgressBarDateBg}" HorizontalAlignment="Left">
                    <Rectangle.Width>
                        <MultiBinding Converter="{StaticResource RatioToWidthConverter}">
                            <Binding Path="DateRatio"/>
                            <Binding Path="ActualWidth"
                                     RelativeSource="{RelativeSource AncestorType=ContentPresenter}"/>
                        </MultiBinding>
                    </Rectangle.Width>
                </Rectangle>
                <TextBlock Text="{Binding DateDisplay}" VerticalAlignment="Center" Margin="4,0"/>
            </Grid>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

**关键保留项**：
- `SortMemberPath` 不变 → 排序不受影响
- `Width` 值不变 → 列宽度不变
- 文本（`SizeDisplay` / `RatioDisplay` / `DateDisplay`）仍在原位置显示

### 5. 菜单开关

#### AppSettings 新增属性（`AppSettings.cs`，外观区块）

```csharp
// ===== 外观 =====
public string Theme { get; set; } = "Light";
public string Language { get; set; } = "zh";
public bool ShowProgressBars { get; set; } = true;   // ← 新增
```

#### MainWindow.xaml 视图菜单

`MainWindow.xaml` line 69-73 视图菜单中，在「刷新」下方新增：

```xml
<MenuItem Header="{l:L Main_Menu_View}">
    <MenuItem Header="{l:L Main_Menu_Refresh}" Click="Refresh_Click" InputGestureText="F5">
        <MenuItem.Icon><emoji:TextBlock Text="🔄" FontSize="14" Margin="2,0,4,0" VerticalAlignment="Center"/></MenuItem.Icon>
    </MenuItem>
    <Separator/>
    <MenuItem x:Name="ShowProgressBarsMenu" Header="{l:L Main_Menu_ProgressBars}"
              IsCheckable="True" IsChecked="True"
              Click="ToggleProgressBars_Click">
        <MenuItem.Icon><emoji:TextBlock Text="📊" FontSize="14" Margin="2,0,4,0" VerticalAlignment="Center"/></MenuItem.Icon>
    </MenuItem>
</MenuItem>
```

#### 点击事件（`MainWindow.Menu.cs`）

```csharp
private void ToggleProgressBars_Click(object sender, RoutedEventArgs e)
{
    AppSettings.Instance.ShowProgressBars = ShowProgressBarsMenu.IsChecked;
    AppSettings.Instance.Save();

    // 重新计算当前视图的比例值，触发进度条显隐
    if (!string.IsNullOrEmpty(_currentFolder))
        FilterFiles(_currentFolder);
    else if (!string.IsNullOrEmpty(_currentArchivePath))
        _ = LoadArchiveAsync(_currentArchivePath);
}
```

#### 国际化翻译

**strings.zh.json**（添加在 `Main_Menu_Refresh` 附近）：
```json
"Main_Menu_ProgressBars": "进度条(_B)"
```

**strings.en.json**：
```json
"Main_Menu_ProgressBars": "_Progress Bars"
```

`L.cs` 是自动生成的，添加字符串常量后运行生成脚本即可。

---

### 6. 主题资源

在主题资源字典（`Themes/Light.xaml` 和 `Themes/Dark.xaml`）中新增三个颜色资源：

**Light.xaml：**
```xml
<Color x:Key="ProgressBarSizeColor">#E0E7FF</Color>
<Color x:Key="ProgressBarRatioColor">#FDE68A</Color>
<Color x:Key="ProgressBarDateColor">#D1FAE5</Color>
<SolidColorBrush x:Key="ProgressBarSizeBg" Color="{StaticResource ProgressBarSizeColor}"/>
<SolidColorBrush x:Key="ProgressBarRatioBg" Color="{StaticResource ProgressBarRatioColor}"/>
<SolidColorBrush x:Key="ProgressBarDateBg" Color="{StaticResource ProgressBarDateColor}"/>
```

**Dark.xaml：**
```xml
<Color x:Key="ProgressBarSizeColor">#1E3A5F</Color>
<Color x:Key="ProgressBarRatioColor">#5C4A1E</Color>
<Color x:Key="ProgressBarDateColor">#14532D</Color>
<SolidColorBrush x:Key="ProgressBarSizeBg" Color="{StaticResource ProgressBarSizeColor}"/>
<SolidColorBrush x:Key="ProgressBarRatioBg" Color="{StaticResource ProgressBarRatioColor}"/>
<SolidColorBrush x:Key="ProgressBarDateBg" Color="{StaticResource ProgressBarDateColor}"/>
```

---

## 交互细节

| 场景 | 大小列 | 压缩率列 | 日期列 |
|------|--------|----------|--------|
| **切换目录（FilterFiles）** | 重新计算 maxSize，更新 SizeRatio；showBars=false 时设为 0 | **无需重算**（绝对比例，计算属性通过 ProgressBarEnabled 门控） | 重新计算 minDate/maxDate，更新 DateRatio；showBars=false 时设为 0 |
| **新打开压缩包（LoadArchiveAsync）** | 同上 | 同上 | 同上 |
| **排序** | 进度条随行移动，不单独处理 | 同左 | 同左 |
| **暗色 / 亮色切换** | 资源字典切换，Rectangle Fill 自动跟随 `{StaticResource}` | 同左 | 同左 |
| **空视图 / 单文件** | 单文件 = 100% 填充；空视图 = 无数据 | 同上逻辑 | 单文件 = 100% 填充（无比较对象） |
| **目录条目** | Size=0 → 0% 无进度条 | IsDirectory → 0 无进度条 | LastModified=MinValue → 0.0 无进度条 |
| **膨胀文件（CompressedSize > Size）** | 不涉及 | 封顶 1.0，全满显示（颜色不变） | 不涉及 |
| **菜单关闭进度条** | 所有列 SizeRatio/RatioBarValue/DateRatio = 0 → Rectangle 宽度 0 → 完全隐藏 | 同左 | 同左 |

---

## 任务清单

- [ ] **1. `ArchiveItem` 新增属性 + 门控** — `SizeRatio`(set)、`DateRatio`(set)、`ProgressBarEnabled`、修改 `RatioBarValue` 计算属性加门控
- [ ] **2. 加载/过滤时计算逻辑** — 修改 `LoadArchiveAsync` 和 `FilterFiles`，在 ItemsSource 赋值前批量计算三列比例值，加入 `showBars` 门控
- [ ] **3. `RatioToWidthConverter`（IMultiValueConverter）** — 新建 `UI/Converters/RatioToWidthConverter.cs`，注册到 `MainWindow.xaml` Resources
- [ ] **4. XAML「大小」列模板替换** — `DataGridTextColumn` → `DataGridTemplateColumn` + 蓝色 Rectangle + MultiBinding(SizeRatio, ActualWidth)
- [ ] **5. XAML「压缩率」列模板替换** — `DataGridTextColumn` → `DataGridTemplateColumn` + 琥珀色 Rectangle + MultiBinding(RatioBarValue, ActualWidth)
- [ ] **6. XAML「日期」列模板替换** — `DataGridTextColumn` → `DataGridTemplateColumn` + 绿色 Rectangle + MultiBinding(DateRatio, ActualWidth)
- [ ] **7. 主题颜色资源** — Light.xaml / Dark.xaml 各添加 6 个资源（3 Color + 3 SolidColorBrush），验证切换时自动刷新
- [ ] **8. 菜单开关** — `AppSettings.ShowProgressBars` + View 菜单 MenuItem(IsCheckable) + `ToggleProgressBars_Click` 事件 + 国际化翻译

---

## 工作量

- **代码改动**: ~45 行 C#（属性+计算逻辑+菜单事件+AppSettings）+ ~70 行 XAML（模板+资源）
- **新文件**: 1 个值转换器（`UI/Converters/RatioToWidthConverter.cs`）
- **国际化**: strings.zh.json / strings.en.json 各 +1 条，L.cs 自动生成
- **测试**: 无需自动化测试，纯视觉改动。构建验证 + 切换主题/开关验证即可

---

## Definition of Done

- [ ] 大小列背景进度条随文件大小比例显示（最大文件 = 100%）
- [ ] 压缩率列背景进度条随压缩率显示（0% = 空，100% = 满）
- [ ] 日期列背景进度条随文件新老比例显示（最新 = 100%）
- [ ] 切换目录 / 过滤文件时大小和日期进度条重新计算
- [ ] 排序操作进度条随行移动，不受影响
- [ ] 暗色 / 亮色切换时三列进度条颜色正常跟随
- [ ] 视图 → 进度条 菜单可正确开关三列进度条显隐
- [ ] 目录条目三列均无进度条
- [ ] `dotnet build` 通过，无 XAML 绑定错误

### Final Checklist

- [ ] 大小列：肉眼可区分大文件 ➡ 深蓝进度条，小文件 ➡ 浅蓝/无
- [ ] 压缩率列：低压缩率（如 20%）= 短琥珀条，高压缩率（如 95%）= 长琥珀条
- [ ] 日期列：新文件 = 深绿长条，旧文件 = 短/无绿色
- [ ] 排序 / 筛选时进度条正确跟随
- [ ] 暗色主题下三色进度条均可辨识
- [ ] 菜单开关关闭后三列进度条完全消失，数值文本正常显示
- [ ] 菜单开关重新打开后进度条恢复
