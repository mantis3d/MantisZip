# 解压对话框「保留完整路径」实时开关设计规格

- 日期：2026-09-30（设计定稿）；2026-10-01（实施完成，全文回写与代码对齐）
- 状态：✅ 已实施。本文档已按实现回写；实现细节以实现计划 `.omo/plans/未开始/extract-preserve-full-path-toggle.md` 为准，两者表述冲突时以计划为准（回写差异清单见计划 §10）
- 关联提交：`1c60b4a`（选择器新增 fileTypes/suggestedFileName）、`663605d`（拖拽解压兜底改用带预览对话框）
- 交互原型：`docs/prototypes/extract-preserve-full-path-toggle.html`

## 1. 概述

### 1.1 目标

在 `CustomFilePickerDialog` 的解压模式（`PickerMode.ExtractFolder`）中提供一个「保留完整路径」开关，用户在**选择目标目录的过程中**可实时切换，右侧预览树立即反映裁剪/保留的差异（复用既有 `SchedulePreviewRebuild` 300ms 防抖重建）。

开关位于窗口**左下参数区**（`RootGrid` 新增行，决策见 §8 决策 3），不占用预览面板标题行。勾选值经返回通道 `ExtractPickResult` 传到实际解压，根治「预览所见 ≠ 实际落盘」：原实现的对话框只返回目标路径字符串，调用方回头独立读 `settings.ExtractPreserveFullPath`，对话框内的任何勾选都影响不了落盘结果。

### 1.2 设计决策摘要

| # | 决策 | 选择 | 理由（正文见 §8） |
|---|---|---|---|
| 1 | 开关语义 | **A：仅本次解压生效，不落盘** | 对话框是"这次解到哪、怎么解"的临时决策场所；设置窗口仍是唯一默认值来源（§8 决策 1） |
| 2 | 返回通道 | **强类型 `ExtractPickResult(DestPath, PreserveFullPath)`** | 由决策 1 推导；类型系统保证开关值一定传到解压侧，且不预先泛化为字典（§8 决策 4） |
| 3 | 开关位置 | **窗口左下参数区**（`RootGrid` 第 3 行），`ExtractFolderPanel` 零改动 | 参数独立成区域承载，预览面板职责不变，标题行不再挤控件（§8 决策 3） |
| 4 | 根目录下（`currentFolder` 为空） | **a：禁用参数项 + ToolTip 说明** | 此场景两种模式产出完全相同，开关无效；禁用优于让用户困惑（§8 决策 2） |
| 5 | i18n key | 新建 **3 个** `Picker_` key（三语） | 见 §7；`Picker_ExtractPreviewTitle` 已存在，不重复添加 |
| 6 | 参数区形态 | **注册表驱动的通用宿主**，空注册表整区隐藏 | 加参数、别的调用情形加参数、无参数三种情况都不改布局（§8 决策 3） |

### 1.3 非目标

- 不改 `ExtractPathResolver` / `ResultPreviewService.BuildExtractPreview` 的现有语义。它们已是纯函数且预览侧与解压侧共用同一 resolver
- 不动设置窗口已有的 `ExtractPreserveFullPath` 复选框（`SettingsWindow.axaml:274`），它只作为对话框的初始勾选值来源
- 不给 `ExtractSettingsWindow` 加此开关（它属于整包解压场景）。但其两处选择目录调用点**必须**适配新返回类型，只取 `.DestPath`（见 §6.4）；由此产生的常驻禁用参数项是已知可接受副作用
- 不改 CLI（`--extract-here` / `--extract-to-name`）路径行为
- 不预先泛化返回通道：`ExtractPickResult` 保持两字段强类型，参数区支持多参数也不改成字典（§8 决策 4）
- 不为可测性重构 `ExtractFlow` / `DragDropService`（因此原计划的 mock 测试不可写，见 §10.1）

## 2. 现状核实（设计期基线）

以下为 2026-09-30 实现前的逐行实测基线，用于说明"为什么要改"。实现完成后行号与结构已变化（`RootGrid` 已由 4 行变 5 行、`_extractPreserveFullPath` 已去 `readonly`、开关已入参数区），最终形态见 §5、§6；最新行号与实现细节以实现计划为准。

### 2.1 对话框内部

| 事实 | 位置 |
|---|---|
| `_extractPreserveFullPath` 是 `readonly bool`，由构造参数灌入 | `CustomFilePickerDialog.axaml.cs:79`、`:246` |
| 唯一消费点：预览重建 | `CustomFilePickerDialog.axaml.cs:791` |
| 预览重建方法已具备防抖（300ms）+ try/catch，可直接复用 | `CustomFilePickerDialog.axaml.cs:772-801` |
| ExtractFolder 模式隐藏「系统浏览」按钮与文件名行 | `CustomFilePickerDialog.axaml.cs:298-311` |
| 面板宽度来自设置（clamp 200-800，默认 260），高度 620 | `CustomFilePickerDialog.axaml.cs:314-319` |
| 对话框不实现 `INotifyPropertyChanged`（`DataContext = this`） | `CustomFilePickerDialog.axaml.cs:249`，因此参数项模型须自带 `ObservableObject`（§4.2） |

### 2.2 AXAML 结构

`CustomFilePickerDialog.axaml`（基线 452 行）：

```
RootGrid RowDefinitions="Auto,*,Auto,Auto"
├─ Row 0  地址栏 + 后退/前进/上级/系统浏览/收藏
├─ Row 1  BrowserGrid  ColumnDefinitions="220,5,*,5,Auto"
│         ├─ Col 0 快速路径面板
│         ├─ Col 2 文件列表
│         └─ Col 4 ExtractFolderPanel  RowDefinitions="Auto,Auto,*"
│                ├─ Row 0 ExtractPreviewTitleText（Auto，左右留白）
│                ├─ Row 1 PreviewSplitter（Auto，5px）
│                └─ Row 2 PreviewTree（*）
├─ Row 2  FileNameArea（仅 SaveFile / OpenFile 模式）
└─ Row 3  确定 / 取消
```

**结论：不存在预留的选项区占位**（无 `*OptionsPanel`、无 `IsVisible=False` 的空 panel）。这一事实决定了决策 3 的落点：不往 `ExtractFolderPanel` 里挤位置，而是给 `RootGrid` 新增一行（§5.2）。

### 2.3 返回通道断点与消费点全清单

| 事实 | 位置 |
|---|---|
| 委托签名 `Func<IReadOnlyList<ArchiveItem>, string?, string, bool, Task<string?>>`，**只返回目标路径** | `MainWindowViewModel.cs:78` |
| 委托在 View 侧闭包接线 | `MainWindow.axaml.cs:183` |
| 主窗口把设置值作为初值传入，确认后丢弃结果 | `MainWindowViewModel.cs:2354` |
| 实际解压时**独立重读** `settings.ExtractPreserveFullPath` | `MainWindowViewModel.cs:2375` |
| 拖拽兜底同样：`:86` 传初值，`:105` 传 `_settings.ExtractPreserveFullPath` | `DragDropService.cs:86`、`:105` |

**结论：即使对话框内让预览随开关变化，实际解压仍走设置值，这正是 AGENTS.md 中 `ExtractPathResolver` 契约禁止的"预览 ≠ 实际"。新增返回通道是本次改动的必要组成部分，不是可选项。**

返回类型一变，`ShowExtractFolderAsync` 的**全部 6 处调用**（4 个文件）都要适配，全清单如下：

| # | 调用点 | 有无对话框 | 新行为 |
|:-:|--------|:-----------:|--------|
| 1 | `MainWindowViewModel.ExtractSelectedHere` | 否 | 传 `null`，由 `?? settings.ExtractPreserveFullPath` 兜底（§6.1） |
| 2 | `MainWindowViewModel.ExtractSelectedTo` | 是 | 取 `pick.PreserveFullPath`（§6.1） |
| 3 | `DragDropService` 目标已检测到分支 | 否（不弹窗） | 保持设置值（局部变量初值，§6.3） |
| 4 | `DragDropService` 兜底分支 | 是 | 取 `pick.PreserveFullPath`（§6.3） |
| 5 | `ExtractSettingsWindow.axaml.cs:61` `ViewModel.BrowseFolder` | 是 | **只取 `.DestPath`**（§6.4） |
| 6 | `ExtractSettingsWindow.axaml.cs:83-87` `DestinationPicker.BrowseAction` | 是 | **只取 `.DestPath`**（§6.4） |

> 调用点 5/6 是实施期补上的（初版设计遗漏），不改会编译失败（CS0029 / CS0173）。
> `MainWindow.axaml.cs:183-184` 闭包是表达式体、无显式返回类型，自然类型跟随被调方法，零改动（§6.2）。

### 2.4 预览侧可行性

`ResultPreviewService.BuildExtractPreview`（`ResultPreviewService.cs:33-42`）为**纯静态同步函数**，内部走

```csharp
ExtractPathResolver.ResolveRelativePath(item.FullPath ?? item.Name, currentFolder, preserveFullPath)
```

与解压侧同一 resolver。切换开关重跑该函数无副作用，安全。

## 3. 数据流

### 3.1 改动后

```
用户勾选/取消参数区复选框
   └→ PickerOptionItem.IsChecked 双向回推（INPC）
   └→ AddOption 注册的 onChanged 回调
        ├→ _extractPreserveFullPath = 新值                  (字段为唯一存储)
        └→ SchedulePreviewRebuild(_currentDir)              (复用既有 300ms 防抖重建)
             └→ BuildExtractPreview(..., preserveFullPath: 新值, currentFolder: _extractCurrentFolder)
                  └→ ExtractPathResolver.ResolveRelativePath  ← 与解压侧同一实现
                       └→ 右侧预览树刷新

用户点「选择文件夹」
   └→ Ok_Click 只设置 SelectedPath 并关闭（不构造返回对象）
   └→ ShowExtractFolderAsync 构造 ExtractPickResult(DestPath, PreserveFullPath)   ← 返回通道，唯一构造点
        └→ ExtractSelectedTo / DragDropService 兜底分支取 pick.PreserveFullPath
             └→ ExtractFlow.RunSelectedItemsExtractionAsync(preserveFullPath)
                  └→ SelectedItemsExtractService.ExtractEntriesAsync(preserveFullPath)
                       └→ ExtractPathResolver.ResolveRelativePath  ← 同一实现，与预览逐字一致
```

### 3.2 单一事实来源不变

`ExtractPathResolver.ResolveRelativePath` 仍是唯一计算点。本次不新增任何路径拼接逻辑。

## 4. 新增类型

### 4.1 `ExtractPickResult`（返回通道）

```csharp
// Dialogs/ExtractPickResult.cs
namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>解压目标目录选择结果（路径 + 本次解压的路径保留语义）。</summary>
/// <param name="DestPath">目标目录绝对路径。</param>
/// <param name="PreserveFullPath">本次解压是否保留压缩包内完整路径（覆盖 AppSettings 默认值，仅本次生效）。</param>
public sealed record ExtractPickResult(string DestPath, bool PreserveFullPath);
```

放在 `Dialogs/` 下与对话框同域，不进 `Core`。它是 UI 层返回值契约，不含引擎逻辑；从 1300 余行的对话框文件中拆出，聚焦职责。

参数区（§8 决策 3）可承载任意多个参数，但**已知参数仍用强类型字段**承载：类型系统保证"值一定传到解压侧"是本功能的核心诉求，不预先泛化为字典（决策见 §8 决策 4，未来扩展点已写进该 record 的 XML remarks）。

### 4.2 `PickerOptionItem`（参数项显示模型）

```csharp
// Dialogs/PickerOptionItem.cs
public sealed class PickerOptionItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? DisabledHint { get; init; }   // null = 不提示（ToolTip 服务遇 null 不打开）
    public bool IsChecked { get; set; }          // 实际实现带 SetProperty 的完整属性
    public bool IsEnabled  { get; set; }         // 同上
}
```

仅描述"标签 + 值 + 可用性 + 禁用提示"，不含业务语义。派生 `ObservableObject` 而非普通类：当前只有 CheckBox 双向回推 `IsChecked`，但若将来参数需要相互联动（如"全选"批量改其他项），INPC 是唯一无需改造的路径。

单独成文件而非内嵌对话框：它是对话框的**公开可测契约**（结构测试直接构造断言），且 `Dialogs/` 下已有 `FileTypeOption` / `PickerMode` 等小类型同域先例。

## 5. 对话框改动

### 5.1 `CustomFilePickerDialog.axaml.cs`（参数注册表）

开关的初值、可用性、禁用提示、变化回调**统一由参数注册表承载**，没有独立的 `SelectedPreserveFullPath` setter，也没有单独的 `IsEnabled` 赋值语句：

| 改动 | 说明 |
|---|---|
| `_extractPreserveFullPath` 由 `readonly` 改为可写 | 由参数项的 `onChanged` 回写 |
| 新增参数注册表 `_options`（`ObservableCollection<PickerOptionItem>`）+ `AddOption(key, labelKey, initial, onChanged, isEnabled, disabledHintKey)` | 宿主不认识具体参数；`_options.Add` 时置 `OptionsRow.IsVisible = true`，空注册表保持隐藏 |
| `SelectedPreserveFullPath` 为只读表达式属性 `=> _extractPreserveFullPath` | 无 setter；供 `ShowExtractFolderAsync` 读取最终值，初值即构造参数 |
| ExtractFolder 构造分支注册 `preserveFullPath` 参数项 | `initial: _extractPreserveFullPath` 在对象初始化器内一次性写入 `IsChecked`（初值回填，否则复选框恒不勾选、视觉与实际背离）；`onChanged` 回写字段 + `SchedulePreviewRebuild(_currentDir)` |
| `IsPreserveFullPathToggleAvailable` | `!string.IsNullOrEmpty(_extractCurrentFolder)`；该字段构造期固定，故 `isEnabled` 在对话框生命周期内只求值一次（**不存在**"currentFolder 变化后刷新可用性"的路径） |
| `Ok_Click` **不改** | 仍只设置 `SelectedPath` 后关闭；返回对象**唯一**在 `ShowExtractFolderAsync` 内构造，避免两处逻辑不一致 |
| `ShowExtractFolderAsync` 改为自建窗 + `!string.IsNullOrEmpty(dialog.SelectedPath)` 守卫 | 不走 `ShowInternal`（后者只返回 `string?`，无法承载勾选值）；`Ok_Click` 无条件写 `SelectedPath = _currentDir`，理论上可能为空串，空串视为取消 |
| `OptionsCaptionText` / `Options` 只读属性 | code-behind 暴露供 XAML 绑定；**不配同名 `x:Name`**（Avalonia name generator 会在同一 partial 类生成同名 internal 成员，触发 CS0102） |

```csharp
private PickerOptionItem AddOption(
    string key, string labelKey, bool initial,
    Action<bool> onChanged, Func<bool>? isEnabled = null,
    string? disabledHintKey = null)
{
    var item = new PickerOptionItem
    {
        Key = key,
        Label = LocalizationManager.T(labelKey),
        IsChecked = initial,                     // 初值在此一次性写入
        IsEnabled = isEnabled?.Invoke() ?? true, // 可用性静态，只求值一次
        DisabledHint = disabledHintKey == null ? null : LocalizationManager.T(disabledHintKey),
    };
    item.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(PickerOptionItem.IsChecked))
            onChanged(item.IsChecked);
    };
    _options.Add(item);
    OptionsRow.IsVisible = true;                 // 空态：任一参数注册即显示
    return item;
}
```

ExtractFolder 模式的注册（构造函数内，`DataContext = this` 之后）：

```csharp
if (mode == PickerMode.ExtractFolder)
{
    AddOption(
        key: "preserveFullPath",
        labelKey: "Picker_PreserveFullPath",
        initial: _extractPreserveFullPath,
        onChanged: v => { _extractPreserveFullPath = v; SchedulePreviewRebuild(_currentDir); },
        isEnabled: () => IsPreserveFullPathToggleAvailable,
        disabledHintKey: "Picker_PreserveFullPathDisabledHint");
}
// ItemsSource 只由 XAML {Binding Options} 提供，code-behind 不再赋值（单一数据源）
```

**复用点**：`SchedulePreviewRebuild` 完全不改签名、不改逻辑；`ShowInternal` 函数体不动，其余模式仍走它。

### 5.2 `CustomFilePickerDialog.axaml`（RootGrid 参数区行）

`ExtractFolderPanel` **零改动**：标题行保持原样的单个 `TextBlock`（`ExtractPreviewTitle` 绑定沿用），`RowDefinitions` 保持 `Auto,Auto,*`。初版设计的"标题行改两列 Grid 右侧放复选框"方案作废（§8 决策 3）。

`RootGrid` 的 `RowDefinitions` 由 `Auto,*,Auto,Auto` 改为 `Auto,*,Auto,Auto,Auto`，在索引 3 插入参数区行；**原「确定/取消」`StackPanel` 的 `Grid.Row` 必须由 `3` 改为 `4`**，漏改会与参数区重叠，且无编译错误、只在视觉上错乱。

```xml
<Grid x:Name="RootGrid" RowDefinitions="Auto,*,Auto,Auto,Auto" ...>
    <!-- Row 0 地址栏 / Row 1 BrowserGrid / Row 2 FileNameArea：既有结构不动 -->

    <!-- ═══ Row 3: 参数区（通用参数宿主 · 无参数时整体隐藏，规则 6） ═══ -->
    <Grid Grid.Row="3" x:Name="OptionsRow" IsVisible="False">
        <Border BorderBrush="{DynamicResource ThemeBorderBrush}" BorderThickness="1"
                CornerRadius="{DynamicResource BorderRadius}"
                Padding="{DynamicResource SpacingSmThk}"
                Background="{DynamicResource ThemeSurfaceBgBrush}">
            <!-- ★ 容器必须用 Grid 而非横向 StackPanel：后者沿 orientation 给子项无穷宽度，
                 WrapPanel 拿到无限可用宽度后永不换行，参数变多会整区溢出窗口（§11 风险 5） -->
            <Grid ColumnDefinitions="Auto,*" ColumnSpacing="{DynamicResource SpacingSm}">
                <!-- 左：区域标题；刻意不加 x:Name，避免与同名属性重复定义 CS0102 -->
                <TextBlock Grid.Column="0" FontSize="11"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"
                           Text="{Binding OptionsCaptionText}" />
                <!-- 右：参数项容器；渲染层只遍历注册表，不认识任何具体 key -->
                <ItemsControl Grid.Column="1" ItemsSource="{Binding Options}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate><WrapPanel Orientation="Horizontal" /></ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal"
                                        MinHeight="{DynamicResource ControlHeightSm}">
                                <CheckBox IsChecked="{Binding IsChecked, Mode=TwoWay}"
                                          IsEnabled="{Binding IsEnabled}"
                                          Content="{Binding Label}"
                                          ToolTip.ShowOnDisabled="True"
                                          ToolTip.Tip="{Binding DisabledHint}" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Grid>
        </Border>
    </Grid>

    <!-- Row 4: 确定 / 取消（Grid.Row 由 3 改为 4） -->
    <StackPanel Grid.Row="4" ...>
</Grid>
```

**禁用提示必须开 `ToolTip.ShowOnDisabled="True"`**（Avalonia 12.0.4 提供 `ToolTip.ShowOnDisabledProperty`）：Avalonia 对 `IsEnabled=false` 的控件不派发指针事件，ToolTip 依赖 pointer-over，只绑 `ToolTip.Tip` 的提示在禁用态**永不弹出**。`DisabledHint` 为 null 时提示服务不打开，天然满足"可用时不提示"。初版设计设想的独立 `?` 触发 TextBlock 方案已放弃，无需额外节点，修法演进见 §8 决策 2。

参数区新增 AXAML 全部带中文注释（规则 14）。

### 5.3 绑定与本地化策略

沿用本文件既有做法：`x:CompileBindings="False"` + code-behind 暴露属性（`BackText` / `OkText` / `ExtractPreviewTitle` / `OptionsCaptionText` 均为此模式）。

与初版设计的两点更正：

- **没有 `OnCultureChanged` 刷新**。每次 `ShowExtractFolderAsync` 都 `new` 对话框实例，`LocalizationManager.T(...)` 构造期求值即为当前语言；模态生命周期短，运行中切语言刷新按 YAGNI 否决（§8 其他已否决备选）。参数区文案同理。
- code-behind 属性**无需**登记进 `MainWindowViewModel.UpdateLocalizedStrings()`（那是主窗口 `LocalizedStrings` 字典的显式 key 数组，见 AGENTS.md 规则 13）。

`ItemsControl.ItemsSource` 只由 XAML `{Binding Options}` 提供；code-behind 不再赋值，避免双数据源互相覆盖。

## 6. 调用方改动

### 6.1 `MainWindowViewModel.cs`

| 行 | 实现 |
|---|---|
| 78 | 委托返回类型 `Task<string?>` → `Task<ExtractPickResult?>`，该委托的 XML 文档同步更新 |
| 2342 `ExtractSelectedTo`（有对话框） | `var pick = await ShowExtractFolderPicker(...)`；`if (pick is null \|\| string.IsNullOrEmpty(pick.DestPath)) return;`；随后传 `pick.PreserveFullPath` |
| 2322 `ExtractSelectedHere`（无对话框） | 调用 `ExtractSelectedEntriesCoreAsync(entries, dest, null)`，不在此处读设置 |
| 2366 `ExtractSelectedEntriesCoreAsync` | 签名增加 `bool? preserveFullPath = null` |
| 2376 | 改传 `preserveFullPath ?? settings.ExtractPreserveFullPath`，默认值解析只此一处 |

> **可空参数的理由**：`ExtractSelectedHere` 走设置默认，若按初版方案在它内部 `AppSettings.Load()`，与 `:2372` 的加载会在同一次解压里把 settings.json 反序列化两遍。用 `bool? = null` 表达"本次无勾选值，走设置默认"，兜底收敛到 `:2376` 一处。`var settings = AppSettings.Load()`（`:2372`）保留，`FileConflictAction`、`OpenFolderAfterExtract` 仍在用。
>
> 两个有对话框的消费点（§6.1 的 `ExtractSelectedTo` 与 §6.3 的兜底分支）**统一**采用 `pick is null || string.IsNullOrEmpty(pick.DestPath)` 守卫：`Ok_Click` 无条件写 `SelectedPath`，空串必须视为取消。

### 6.2 `MainWindow.axaml.cs:183-184`

**零改动**。闭包 `(entries, initialPath, currentFolder, preserveFullPath) => ShowExtractFolderAsync(...)` 是表达式体且无显式返回类型，自然类型跟随被调方法；`ShowExtractFolderAsync` 返回值与委托类型是同一动作的两端，恒等匹配，天然编译通过。

### 6.3 `DragDropService.cs`

| 位置 | 实现 |
|---|---|
| `:77`（兜底 `if` 之前） | `var preserveFullPath = _settings.ExtractPreserveFullPath;`，作为非兜底分支的默认值 |
| `:87-97` 兜底分支 | 接 `ExtractPickResult`；`pick is null \|\| string.IsNullOrEmpty(pick.DestPath)` 则取消；否则 `targetDir = pick.DestPath;`、`preserveFullPath = pick.PreserveFullPath;` |
| `:110` | `RunSelectedItemsExtractionAsync(..., preserveFullPath, ...)` 传局部变量 |

**两分支不可混用**（本改动最易出错处）：`:110` 这个调用同时被"目标已检测到"（不弹对话框）与"兜底弹窗"两条路径执行。前者没有勾选值，保持 `:77` 的设置默认；后者已被兜底分支覆盖。因此两处共用同一局部变量：既不能改回直接读 `_settings.ExtractPreserveFullPath`（会吞掉兜底分支的勾选），也不能让非兜底分支凭空拿到对话框值。

### 6.4 `ExtractSettingsWindow.axaml.cs`（初版遗漏，编译必需）

| 位置 | 改前 | 实现 |
|---|---|---|
| `:61` `ViewModel.BrowseFolder` | `return await ShowExtractFolderAsync(...)`（委托 `Func<Task<string?>>`） | `return (await ShowExtractFolderAsync(...))?.DestPath;`（否则 CS0029） |
| `:83-87` `DestinationPicker.BrowseAction` | 三元 `Task.FromResult<string?>(null) : ShowExtractFolderAsync(...)`（委托 `Func<Window?, string?, Task<string?>>`） | 改 `async` lambda 取 `(await ...)?.DestPath`（否则 CS0173，三元失去公共类型） |

这两条链路是**整包解压**（选整体目标目录），调用时未传 `currentFolder`（取默认 `""`），而 `TrimCurrentFolderPrefix` 在根目录直接早退，`PreserveFullPath` 与结果无关，因此只取 `.DestPath`、丢弃 `PreserveFullPath` 是**正确语义而非妥协**。

已知副作用：该弹窗的参数区会显示一个**常驻禁用**的「保留完整路径」（`currentFolder` 为空触发决策 a 的禁用）。这是如实反映语义（不是坏 UI），故接受并列入 §10.2 人工确认。

### 6.5 不需要改动

`ExtractFlow.RunSelectedItemsExtractionAsync` 与 `SelectedItemsExtractService.ExtractEntriesAsync` **已接受 `preserveFullPath` 参数**，无需签名变更；`ExtractFolderPanel` 内部结构零改动；`ShowInternal` 保留供未来模式使用。

## 7. i18n（3 个新 key，三语成对）

| key | zh-CN | zh-TW | en |
|---|---|---|---|
| `Picker_PreserveFullPath` | 保留完整路径 | 保留完整路徑 | Preserve full paths |
| `Picker_PreserveFullPathDisabledHint` | 在压缩包根目录，勾选与否结果相同 | 在壓縮包根目錄，勾選與否結果相同 | At the archive root, both settings produce the same result |
| `Picker_OptionsCaption` | 本次参数 | 本次參數 | Options |

- 三份 JSON key 集必须完全一致（`AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 校验）；另有本功能自己的 `NewOptionKeys_PresentAndNonEmpty_InAllThreeLanguages` 断言新 key 非空（空标签会产生无标签的孤儿复选框，比缺失更隐蔽）
- 插入文件头 `{` 之后，UTF-8 无 BOM + CRLF + 2 空格缩进
- **`Picker_ExtractPreviewTitle` 已存在**于三语文件（当前 line 1063；本功能 3 个新 key 插入文件头后由原 line 1060 顺延），`ExtractPreviewTitle` 属性一直在用，**不得重复添加**。补充事实：重复 JSON key 在 `JsonSerializer.Deserialize<Dictionary<string,string>>` 下是后值覆盖、不抛异常（已实测），重复只会静默覆盖而非破坏本地化；真正的问题是三语 key 集出现两份同 key，属整洁性问题
- **不复用** `Settings_Extract_PreserveFullPath`：设置页与对话框可能需要不同措辞，耦合会产生隐性依赖
- 对话框用 code-behind 属性绑定（§5.3），无需登记 `MainWindowViewModel.UpdateLocalizedStrings()`

## 8. 决策记录

决策 1、2 于 2026-09-30 brainstorming 确认；决策 3、4 于 2026-10-01 实施期间追加。实现范围即 §4 至 §7 的基线 + 下列决策增量。

### 决策 1：A，仅本次解压生效，不落盘

勾选状态只写入对话框内存字段 `_extractPreserveFullPath`（经只读属性 `SelectedPreserveFullPath` 进入返回通道），**不触碰** `AppSettings.ExtractPreserveFullPath`。初值仍取自设置，用户在设置窗口的全局偏好不受影响。

**已否决的备选**（留档备查）：

| 备选 | 否决理由 |
|---|---|
| B 写回全局设置 | 会让用户在对话框的一次临时调整静默改掉命令行解压、拖到 Explorer、`ExtractSettingsWindow` 的行为 |
| C 临时 + 「记住此选择」二级勾选 | 两个 CheckBox 会挤爆参数区的单行空间；收益不足以抵消布局代价 |

**连带确定**：因决策为 A（而非 B），返回通道**必须**实现，§4 的 `ExtractPickResult`、§5.1、§6 全部在本设计范围内。

### 决策 2：a，根目录下禁用参数项 + ToolTip 说明

`ExtractPathResolver` 的裁剪语义是「`preserveFullPath=false` **且 `currentFolder` 非空时**裁剪前缀」。故在压缩包根目录时两种模式产出**完全相同**的树，开关形同失效。该等价性由契约测试第 3 条（`Extract_AtArchiveRoot_BothSettings_ProduceIdenticalFileSets`）锁定，比文字论证更硬。

实现（经由参数注册表，而非独立赋值语句）：

```csharp
AddOption(...,
    isEnabled: () => IsPreserveFullPathToggleAvailable,
    disabledHintKey: "Picker_PreserveFullPathDisabledHint");
```

XAML 侧给 CheckBox 加 `ToolTip.ShowOnDisabled="True"` + `ToolTip.Tip="{Binding DisabledHint}"`。`_extractCurrentFolder` 为构造期固定值，可用性只求值一次，对话框生命周期内无需动态刷新。

**修法演进（留档）**：初版假设"ToolTip 常驻绑定即可，禁用时自然显示"，该假设**错误**：Avalonia 对 `IsEnabled=false` 的控件不派发指针事件，ToolTip 依赖 pointer-over，提示永不弹出。计划中途曾改为参数项内独立可命中的 `?` 触发节点，最终采用 Avalonia 12.0.4 自带的 `ToolTip.ShowOnDisabled`（已核实 `ToolTip.ShowOnDisabledProperty` 存在），既不需要额外节点，也不需要派生属性或手动属性变更通知（`CommunityToolkit.Mvvm` 8.4.2 的 `ObservableObject` 没有 `RaiseChanged` 方法，其 `OnPropertyChanged` 是 protected、从对话框 lambda 外部不可调）。

**已否决的备选**：

| 备选 | 否决理由 |
|---|---|
| b 加小字说明 | 让用户点一个明知无效的控件，不如直接禁用 |
| c 不处理 | 会被误判为功能损坏 |
| 独立 `?` 触发节点承载提示 | 需要额外可命中节点与派生属性；`ShowOnDisabled` 一行解决（演进见上） |

注：此项**不适用** AGENTS.md 规则 6。规则 6 约束的是"开关控制面板显隐"，而此开关控制的是预览树内容，预览树必须始终可见才能达成"实时切换看效果"的目标。

### 决策 3：参数区独立成左下通用宿主（2026-10-01 追加）

取代初版的「预览面板标题行右侧复选框」方案。

用户需求：参数**独立成区域**放在窗口左下，供本次调用的参数集中承载；将来更多参数、以及别的调用情形需要的参数都放这里；没有参数则整区隐藏。

| 项 | 决定 |
|---|---|
| 位置 | `RootGrid` 新增一行，位于浏览器网格之后、确定/取消之前，左对齐（窗口左下） |
| 容器 | `Border` 外框 + 内层 `Grid ColumnDefinitions="Auto,*"`（左标题 / 右参数项），参数项面板为 `WrapPanel`（参数增多自动换行）。**不可**用横向 `StackPanel` 做外层容器，理由见 §11 风险 5 |
| 扩展方式 | **参数注册表** `AddOption(key, labelKey, initial, onChanged, isEnabled, disabledHintKey)`；渲染层只遍历注册表生成标签与控件，不认识任何具体 key |
| 空态 | 注册表为空时 `IsVisible = false` 整区隐藏（规则 6：隐藏而非留空，不占布局空间） |
| 归属 | 由模式在构造期注册；对话框渲染层不感知业务语义 |

**通用宿主而非一次性控件**：渲染层迭代注册表，不感知任何具体 key；新增参数、别的调用情形需要参数、空注册表整区隐藏，三种情况都不改布局。

**正向副作用**：原 §11 所列"260px 窄面板内标题 + 复选框溢出"风险随之消失，标题行恢复纯标题。

**已否决的备选**：

| 备选 | 否决理由 |
|---|---|
| 仍放预览面板标题行 | 260px 窄面板挤标题 + 控件；将来参数超过 2 个即溢出，且与预览面板职责混淆 |
| 参数区放在确定/取消同一行 | 参数多时会把按钮挤到换行，破坏主操作位置稳定性 |
| 参数区右对齐 | 与"浏览 → 选目标 → 调参数 → 确认"的左→右阅读顺序相反 |
| 各模式各写一套参数 UI | 渲染逻辑重复，加参数要改多处；注册表方案一处生效 |

### 决策 4：返回通道保持强类型，不预先泛化（2026-10-01 追加）

参数区是通用宿主，可承载任意多个参数，但 `ExtractPickResult` 只有 `DestPath`、`PreserveFullPath` 两个强类型字段，**不**改成 `IReadOnlyDictionary<string, bool>` 之类。

理由：本功能的核心诉求是"类型系统保证勾选值一定传到解压侧"（决策 1 的连带确定）；把已知参数降级为字典键会重新打开"值悄悄丢失"的口子。未来确需动态参数时再扩展，扩展点已写进 `ExtractPickResult` 的 XML remarks（YAGNI）。

**连带裁定**：`CollectOptions()`（把注册表转成字典的方法）判定为**死代码并删除**。理由：① 字典无消费者（返回通道按设计强类型）；② 它是 private，无测试可达；③ `_options.ToDictionary` 对重复键抛异常，而重复注册本就不该发生；④ 可扩展性由 `AddOption` 注册表本身提供，等真出现第二个消费者时再提取字典。

| 备选 | 否决理由 |
|---|---|
| 返回通道改为字典承载动态参数 | 削弱类型保证，与决策 1 的核心诉求冲突；YAGNI |
| 保留 `CollectOptions()` 备用 | 死代码，理由见上四条 |

### 其他已否决的备选（留档）

| 备选 | 否决理由 |
|---|---|
| 引入 `INotifyPropertyChanged` 支持对话框运行中切语言 | YAGNI：每次 `ShowExtractFolderAsync` 都 `new` 实例，`LocalizationManager.T(...)` 构造期求值即为当前语言；模态生命周期短，运行中切语言非本功能引入的问题 |
| 为可测性给 `ExtractFlow` 加接口 / DI | 与本功能无关的架构重构，成本远超收益；测试改走 §10.1 的替代方案 |

## 9. 规则合规检查

| 规则 | 落实 |
|---|---|
| 4 主题样式 | 参数区 `Border` 绑 `ThemeBorderBrush` / `ThemeSurfaceBgBrush`，标题绑 `ThemeTextSecondaryBrush`（均为亮/暗主题成对存在的资源键）；CheckBox 走全局主题样式 |
| 5 紧凑度 | `CornerRadius="{DynamicResource BorderRadius}"`、`Padding` 用 `SpacingSmThk`、间距 `SpacingXs` / `SpacingSm`，无硬编码数值 |
| 6 隐藏而非禁用 | 空注册表整区 `IsVisible=false`；决策 2 的禁用是单参数项的可用性而非面板显隐，有明确 UX 理由（§8 决策 2） |
| 7 行高 | 参数项模板 `MinHeight="{DynamicResource ControlHeightSm}"`（`ItemsControl` 项容器按规则 7 处理） |
| 12 构建验证 | `dotnet build` + `dotnet test`（实测结果见 §10.1） |
| 13 本地化 | 新增 3 个 `Picker_` key，三语成对；对话框 code-behind 属性绑定，不进 `UpdateLocalizedStrings()`（§5.3） |
| 14 AXAML 注释 | 新增参数区 AXAML 全部带中文注释 |
| 15 选择器统一 | 本改动不新增任何文件/目录选择入口（`ShowExtractFolderAsync` 属既有入口） |

## 10. 测试

### 10.1 自动化测试（实际落地：5 个测试文件，22 条用例）

初版承诺的三条 mock 透传测试**不可写**，原因已核验：

| 原承诺 | 阻塞原因 |
|---|---|
| mock 委托断言 `ExtractSelectedTo` 透传 | `ExtractFlow.RunSelectedItemsExtractionAsync` 是 `static` 且内部 `new ProgressWindow(...)`，无 DI 缝隙无法拦截 |
| mock 断言 `DragDropService` 兜底分支透传 | `DragDropService` 是 `internal class`；UI 项目 `InternalsVisibleTo` 仅授予 `MantisZip.Tests`，测试项目编译期即不可引用 |
| mock 断言非兜底分支用设置值 | 同上 |

替代方案已全部落地（`tests/MantisZip.UI.Avalonia.Tests/`）：

| 测试文件 | 用例数 | 锁定内容 |
|---|:---:|---|
| `ExtractPreserveFullPathContractTests` | 3 | **落盘契约**（真解压到临时目录断言磁盘布局）：`preserveFullPath=true` 保留 `docs/` 前缀；`false` 裁剪前缀；`currentFolder=""` 时两模式输出完全相等（决策 a 的可执行证明） |
| `ExtractPreserveFullPathWiringTests` | 4 | **架构守卫**（源码匹配，空白归一化 + 括号/花括号配平截取）：仓库根可定位；`ExtractSelectedEntriesCoreAsync` 签名含 `bool? preserveFullPath = null`；该方法体内完全不出现 `settings.ExtractPreserveFullPath`；`DragDropService` 调用传局部变量而非 `_settings.` 字段 |
| `PickerOptionsRegionTests` | 5 | **参数区结构**：ExtractFolder 注册 1 项且初值等于传入值（初值回填回归锁）；4 种无参模式空态隐藏；首注册即显示；根目录禁用且提示非空（决策 a）；子目录内可用 |
| `ExtractPreserveFullPathPreviewMatchTests` | 7 | **预览↔落盘对账**：`currentFolder × preserveFullPath` 全矩阵 4 条 theory + 前缀不匹配的条目保持原路径 + 切换参数不回写 `AppSettings`（决策 A）+ 3 个新 key 三语非空 |
| `PickerOptionsRegionLayoutTests` | 3 | **布局与提示配置**：运行时布局测量（参数区宽度有限且不溢出父容器）+ 容器形态为 Grid 而非横向 StackPanel + `ShowOnDisabled` 源码守卫（XAML 属性拼错会被编译器静默忽略，故锁源码形态） |

守卫的作用要诚实界定：它们证明的是"坏读取没有回来"，**不是**"正确的值流过去了"；后者由契约测试与预览对账测试覆盖，再由 §10.2 人工验收兜底渲染行为。

**负控制验证**（证明测试非空测）：

- 初值回填回归：把 `AddOption` 的 `initial:` 注入为 `false` → `ExtractFolder_RegistersPreserveFullPath_WithInitialValue` 立即 FAIL
- 预览对账：注入解压侧 `currentFolder: ""`（与预览侧不一致）→ `PreviewTreePaths_MatchActualExtraction(docs, False)` 立即 FAIL

由此可判定：「预览所见 ≠ 实际落盘」这一原始缺陷若重新出现，**必然被自动捕获**，不依赖人工观察。

**实测结果（2026-10-01）**：

- `dotnet build` → exit 0 / 0 error / 0 warning
- `dotnet test tests\MantisZip.UI.Avalonia.Tests` → **127 通过 / 0 失败 / 2 跳过**（基线 105 + 新增 22）
- `dotnet test tests\MantisZip.Tests` → **416 通过 / 0 失败 / 2 跳过**
- 三语 key 集一致（`AllThreeLanguages_HaveSameKeySet` PASS）+ 3 个新 key 三语均非空

> 测试基础设施配套：UI 测试项目 `Avalonia.Headless.XUnit` 由 12.0.4 升到 12.1.2（与 UI 项目 Avalonia 版本对齐，否则构造 Window 抛 `TypeLoadException`）；`MantisZip.UI.Avalonia.csproj` 补 `<InternalsVisibleTo Include="MantisZip.UI.Avalonia.Tests" />`（结构测试需访问 `internal` 的 `OptionsRow`）。

### 10.2 仍需人工 GUI 验收（自动化无法覆盖渲染行为）

| # | 验收内容 | 期望 | 现状 |
|:-:|---|---|---|
| 1 | 悬停**禁用态 CheckBox 本体**时提示框是否真的弹出 | 显示「在压缩包根目录，勾选与否结果相同」 | ⏳ 已验证 `ShowOnDisabled=True` 属性存在且提示文案非空，但 headless 不派发 hover 事件，需实机目视 |
| 2 | 多参数时参数区**换行后的视觉效果** | 换行整齐、不溢出窗口 | ⏳ 已用运行时测量证明容器宽度有限不溢出，换行观感仍需目视 |
| 3 | `ExtractSettingsWindow` 链路「浏览」按钮 | 弹窗正常回填目标目录；参数区显示常驻禁用项（已知可接受副作用） | ⏳ 待实机确认 |

**为什么这 3 项不能自动化**：headless Avalonia 不派发 hover 事件、不渲染像素；`ItemsControl` 的 item 容器在 headless 下不物化（已实测 `Measure/Arrange` 与 `Show()` 均无效）。

以下行为观察项建议实机走一遍，但核心断言已自动化：子目录内勾选/取消的 300ms 防抖刷新观感（路径一致性已由对账测试锁定）、拖拽兜底场景实机全流程、根目录禁用态（状态断言已由 `PickerOptionsRegionTests` 锁定）、设置值初始反映与不回写（不回写已由 `TogglingOption_DoesNotWriteBackToAppSettings` 锁定）。

## 11. 风险

| 风险 | 状态 / 缓解 |
|---|---|
| 漏改某条消费路径导致预览 ≠ 实际 | 架构守卫断言"坏读取没回来" + 预览对账测试断言"好值流过去了" + `ExtractPickResult` 强类型；两者均已做负控制验证（§10.1） |
| 拖拽两分支混用取值来源 | 守卫 `DragDropService_PassesLocalPreserveFullPathToExtractFlow`（括号配平截取实参列表，与参数换序无关）；人工验收兜底 |
| 新 key 漏加某语言或值为空 | `AllThreeLanguages_HaveSameKeySet` + `NewOptionKeys_PresentAndNonEmpty_InAllThreeLanguages` 双重拦截 |
| ~~面板 260px 默认宽度下标题 + 复选框溢出~~ | **风险已消除**：参数移出预览面板，标题行恢复纯标题（§8 决策 3） |
| 参数区外层误用横向 `StackPanel`，`WrapPanel` 拿到无限宽度永不换行，参数变多整区溢出窗口 | 已改用 `Grid ColumnDefinitions="Auto,*"` 消除；`OptionsItemsControl_GetsFiniteWidth_AndDoesNotOverflow`（运行时布局测量）+ `OptionsRegion_ContainerIsGrid_NotHorizontalStackPanel` 锁住回归 |
| 禁用态 CheckBox 的 ToolTip 不弹 | `ToolTip.ShowOnDisabled="True"` + 源码守卫 `OptionCheckBox_DeclaresShowOnDisabled_InXaml`；hover 是否真弹需实机目视（§10.2 第 1 项） |
| `ExtractSettingsWindow` 链路常驻禁用项成为视觉噪声 | 已知可接受副作用（如实反映语义），列入 §10.2 第 3 项人工确认；若日后嫌噪声过大，可在 `AddOption` 上加"无 currentFolder 上下文则不注册"的显式条件，那是独立的产品决策，不在本设计范围 |

## 12. Definition of Done

### 已完成（自动化验证，2026-10-01 实测通过）

- [x] `ExtractPickResult` 定义于 `Dialogs/`（强类型两字段 record，未泛化为字典）
- [x] `PickerOptionItem` + `AddOption` 参数注册表落地；`SelectedPreserveFullPath` 为只读表达式属性
- [x] ExtractFolder 模式注册「保留完整路径」参数项并显示参数区，其余 4 种模式空态整区隐藏
- [x] 初值、可用性、禁用提示由 `AddOption(initial:, isEnabled:, disabledHintKey:)` 统一承载（含初值回填）
- [x] 勾选/取消经 `onChanged` 回写字段并触发 `SchedulePreviewRebuild`，预览树实时重建
- [x] `Ok_Click` 保持不变；返回对象唯一由 `ShowExtractFolderAsync` 构造（`!string.IsNullOrEmpty` 守卫）
- [x] `ShowExtractFolderAsync` / `ShowExtractFolderPicker` 返回 `ExtractPickResult?`；6 处调用点全部适配（含 `ExtractSettingsWindow.axaml.cs` 两处）
- [x] `ExtractSelectedEntriesCoreAsync` 增加 `bool? preserveFullPath = null`，默认值只在 `?? settings.ExtractPreserveFullPath` 一处解析
- [x] `DragDropService` 两分支分别正确取值（`:77` 局部默认 / 兜底分支覆盖 / `:110` 传局部变量）
- [x] 确定/取消按钮行 `Grid.Row` 3→4，与参数区不重叠；`ExtractFolderPanel` 零改动
- [x] 3 个新 i18n key 三语齐备且非空；未重复添加已存在的 `Picker_ExtractPreviewTitle`
- [x] §10.1 的 22 条自动化用例全部通过，含两条负控制验证
- [x] `dotnet build` 0 错误 0 警告；`dotnet test` 两个测试项目全绿（127 / 416 通过）
- [x] 规则 1 同步：`docs/PLAN.md` 已登记本任务（P2 条目）

### 仍需人工 GUI 签字（自动化无法覆盖，见 §10.2）

- [ ] 禁用态 CheckBox 悬停提示实际弹出（DoD 12）
- [ ] 多参数换行后的视觉效果（DoD 20）
- [ ] `ExtractSettingsWindow` 浏览按钮回填 + 常驻禁用项确认（DoD 21）

### 提交前完成（规则 3）

- [ ] 更新 `docs/PROGRESS.md`（里程碑）与 `docs/progress-avalonia-detail.md`（细节）
- [ ] 实现计划状态由「待实施」更新为「已完成」，并把 `docs/PLAN.md` 对应条目状态同步

> 完整的 22 项验收编号与逐条覆盖映射见实现计划 §7；本文档的 DoD 按"已完成 / 待人工签字 / 待提交前"三档陈述当前事实。
