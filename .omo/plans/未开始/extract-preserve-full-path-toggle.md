# 解压选择器「保留完整路径」开关 (Extract Preserve Full Path Toggle)

> **状态**: 📋 待实施 | **创建日期**: 2026-09-30 | **最近修正**: 2026-10-01（第二轮：Oracle 架构评审）
> **关联计划**: `preview-show-content-toggle.md`（✅ 同类「预览面板开关」范式：预览控件内 toggle + 状态驱动重建）、`drag-drop-direct-extract.md`（✅ 拖拽解压后置流程，本计划改动其兜底分支）
> **前置**: 无（Avalonia 直接实施；规则 11：新功能只进 Avalonia。`AppSettings.ExtractPreserveFullPath` 已存在且是本开关的**默认值来源**，不新增字段、不改设置窗口）
> **决策来源**: 2026-09-30 brainstorming 确认（生效范围=A「仅本次解压，不回写设置」、根目录行为=a「禁用 + 提示」）；2026-10-01 追加决策 3「参数区独立成左下通用区域」
> **设计文档**: [`docs/superpowers/specs/2026-09-30-picker-preserve-full-path-toggle-design.md`](../../../docs/superpowers/specs/2026-09-30-picker-preserve-full-path-toggle-design.md)（**已过时**，差异见 §10）
> **交互原型**: [`docs/prototypes/extract-preserve-full-path-toggle.html`](../../../docs/prototypes/extract-preserve-full-path-toggle.html)
> **⚠ 2026-10-01 第一轮修订**：修正自查 7 处缺陷（D1–D7），并**改变 UI 方案** —— 开关不再放预览面板标题行，改为左下**通用参数区**（决策 3）。
> **⚠ 2026-10-01 第二轮修订**：经 Oracle 架构评审再修正 **11 项**（B1–B5 / H1–H2 / M1–M7 / L1–L6），
> 其中 **5 项编译阻塞**：`ExtractSettingsWindow.axaml.cs` 两处调用遗漏（B1）、`RaiseChanged` API 不存在（B2）、
> `init`-only 属性越权赋值（B3）、`x:Name` 与同名属性重复定义 CS0102（B4）、结构测试无访问权（B5）。
> 汇总见 §5.1。

## TL;DR

`CustomFilePickerDialog` 解压模式（`ShowExtractFolderAsync`）新增**左下参数区**，本次解压的「保留完整路径」作为该区的第一个参数项。勾选变化经既有 `SchedulePreviewRebuild`（300ms 防抖）**实时重建预览树**；关键修复是让**实际解压使用本次勾选值**——当前对话框只返回目标路径字符串，调用方回头独立读 `settings.ExtractPreserveFullPath`，导致「预览所见 ≠ 实际落盘」。新增 `ExtractPickResult(DestPath, PreserveFullPath)` 作为返回通道贯穿三个消费点。

参数区是**通用宿主**而非硬编码控件：渲染层只遍历「参数注册表」生成标签 + 控件，不认识任何具体 key。新增参数、别的调用情形（模式）需要参数、空注册表整区隐藏，三种情况都不改布局。

---

## 1. 现状（2026-09-30 逐行核实）

### 1.1 缺陷链路

```
CustomFilePickerDialog.ShowExtractFolderAsync(:136-139)  → Task<string?>   ← 只返回路径，丢弃勾选意图
  MainWindowViewModel.ExtractSelectedTo(:2341)
    :2354  var dest = await ShowExtractFolderPicker(entries, defaultDest, CurrentFolder ?? "", settings.ExtractPreserveFullPath);
    :2357  await ExtractSelectedEntriesCoreAsync(entries, dest);
  MainWindowViewModel.ExtractSelectedEntriesCoreAsync(:2365)
    :2375  CurrentFolder ?? "", settings.ExtractPreserveFullPath, settings.FileConflictAction,   ← ★ 缺陷：回读设置
              ↓
    ExtractFlow.RunSelectedItemsExtractionAsync(:67, static)
      → SelectedItemsExtractService.ExtractEntriesAsync(:29)   ← 真正决定落盘路径
```

```
DragDropService 拖拽解压(:75-107)
  :75   if (string.IsNullOrEmpty(targetDir))        ← 目标检测失败才弹对话框（兜底分支）
  :84     targetDir = await CustomFilePickerDialog.ShowExtractFolderAsync(...)   ← 同样是 string
  :103  ExtractFlow.RunSelectedItemsExtractionAsync(
  :105    _currentFolder, _settings.ExtractPreserveFullPath, _settings.FileConflictAction,   ← ★ 同一缺陷
```

### 1.2 对话框现状

| 事实 | 位置 |
|------|------|
| `_extractPreserveFullPath` | `Dialogs/CustomFilePickerDialog.axaml.cs:79`，`private readonly bool` |
| `_extractCurrentFolder` | 同文件 `:78`，`private readonly string` |
| 是否实现 `INotifyPropertyChanged` | **否**（`:249 DataContext = this`）→ 参数项模型须自带 `ObservableObject`（§3.4 `PickerOptionItem`） |
| 预览重建入口 | `SchedulePreviewRebuild(string destDir)` @ `:772`；既有调用 `:683` |
| 当前目录字段 | `_currentDir`（由 `:1089 SelectedPath = _currentDir;` 佐证） |
| 预览调用 | `:791-792` `preserveFullPath: _extractPreserveFullPath, currentFolder: _extractCurrentFolder` |
| 确认结果字段 | `public string? SelectedPath { get; private set; }` @ `:82` |
| `ShowInternal` | `:153-164`，`return dialog.SelectedPath;` @ `:163` |
| 根 `RowDefinitions` | `RootGrid` = `Auto,*,Auto,Auto`（地址栏 / 浏览器 / 文件名行 / 按钮）→ 决策 3 在索引 3 前插入参数行 |
| 文件名行可见性 | `FileNameArea` 仅 `SaveFile`/`OpenFile` 可见（`:310`），其余模式 `IsVisible=false` → 参数区可复用同一「按模式显隐」范式 |
| ExtractFolderPanel 选项占位 | **无**（原以为有占位区，实为不存在）→ 决策 3 改为 `RootGrid` 新增行 |
| 既有面板标题绑定 | `ExtractPreviewTitle` @ `:112`（取值 `Picker_ExtractPreviewTitle`，**该 key 已存在于三语文件**） |
| 测试项目源码定位设施 | **无**（csproj 无 `None Include`，无 `File.ReadAllText`）→ §4.2-B 守卫测试须自行实现仓库根发现 |

### 1.3 消费点全清单

> ⚠ **返回类型改动波及 4 个文件、6 处调用**（`ShowExtractFolderAsync` 返回类型一变，所有调用点都要改）。

| # | 调用点 | 是否有对话框 | 新行为 |
|:-:|--------|:-----------:|--------|
| 1 | `MainWindowViewModel.ExtractSelectedHere` `:2333` | ❌ 无 | 传 `null` → `:2375` 走设置默认兜底（§3.5） |
| 2 | `MainWindowViewModel.ExtractSelectedTo` `:2357` | ✅ 有 | 用 `pick.PreserveFullPath` |
| 3 | `DragDropService` 目标已检测到 分支 | ❌ 无（不弹窗） | **保持 `_settings.ExtractPreserveFullPath`**（`:75` 前局部变量初值） |
| 4 | `DragDropService` 兜底分支 `:84` | ✅ 有 | 用 `pick.PreserveFullPath` |
| 5 | **`ExtractSettingsWindow.axaml.cs:61`** `ViewModel.BrowseFolder` | ✅ 有 | **只取 `.DestPath`**（见下） |
| 6 | **`ExtractSettingsWindow.axaml.cs:83-87`** `DestinationPicker.BrowseAction` | ✅ 有 | **只取 `.DestPath`**（见下） |

> `MainWindow.axaml.cs:183-184` 闭包是表达式体且无显式返回类型，返回类型随 `ShowExtractFolderAsync` 自动流转 —— **已核实无需改动**（2026-09-30 逐行确认，依据见 §6 注）。

**调用点 5/6 必须同步改，否则编译失败**（原方案遗漏 —— 已实测确认）：

| 位置 | 现状签名 | 改后 |
|------|----------|------|
| `:56-62` | `ViewModel.BrowseFolder` 是 `Func<Task<string?>>`（`ExtractSettingsViewModel.cs:29`）；lambda 内 `return await ShowExtractFolderAsync(...)` 变成 `ExtractPickResult?` → **CS0029** | `return (await ShowExtractFolderAsync(...))?.DestPath;` |
| `:83-87` | `DestinationPicker.BrowseAction` 是 `Func<Window?, string?, Task<string?>>`（`QuickPathPicker.axaml.cs:35`）；三元 `Task.FromResult<string?>(null) : ShowExtractFolderAsync(...)` 失去公共类型 → **CS0173** | 改为 `async` lambda：`(owner, current) => (await ShowExtractFolderAsync(owner ?? this, …))?.DestPath` |

> 这两条链路是**整包解压**（`ExtractSettingsWindow` 选整体目标目录），`PreserveFullPath`
> 与它们无关（`TrimCurrentFolderPrefix` 在 `currentFolder` 为空时直接早退，
> `.axaml.cs:136-139` 这两处未传 `currentFolder` → 取默认 `""`），故只取 `.DestPath`、丢弃
> `PreserveFullPath` 是**正确**的，不是妥协。副作用：该弹窗的参数区会显示一个
> **常驻禁用**的「保留完整路径」（见 §9 边界说明与 DoD 21）。

---

## 2. 决策（已确认）

### 2.1 生效范围 = A（仅本次，不回写设置）

勾选只作用于本次解压，不写 `AppSettings.ExtractPreserveFullPath`。理由：对话框是单次操作入口，持久化会让「临时改一次」污染全局默认。设置窗口仍是该值的唯一来源，作为对话框的**初始勾选值**传入。

### 2.2 根目录行为 = a（禁用 + ToolTip）

`currentFolder` 为空（压缩包根目录）时**禁用**该参数项并显示提示。理由：`ExtractPathResolver.TrimCurrentFolderPrefix` 在根目录无可裁剪前缀，两种设置生成**完全相同**的路径——开关无效会造成用户困惑。（此等价性由 §4.2-A 契约测试第 3 条锁定，原型已用 resolver JS 移植版实测输出逐字一致。）

> **实现约束（D3）**：禁用提示**不能**挂在 `IsEnabled=false` 的 CheckBox 上 —— Avalonia 中禁用控件不参与命中测试，ToolTip 依赖 pointer-over 事件，提示很可能永不弹出。改为挂在同参数项内独立可命中的 `?` 节点上（§3.3）。

### 2.3 参数区 = 独立通用宿主（2026-10-01 追加，取代原「标题行右侧复选框」方案）

用户需求：参数**独立成区域**放在窗口左下，供本次调用的参数集中承载；将来更多参数、以及别的调用情形需要的参数都放这里；没有参数则整区隐藏。

| 项 | 决定 |
|----|------|
| 位置 | `RootGrid` 新增一行，位于浏览器网格之后、确定/取消之前，左对齐 |
| 容器 | 边框面板 + `WrapPanel`（参数增多自动换行） |
| 扩展方式 | **参数注册表**：`AddOption(key, labelKey, initial, onChanged, isEnabled)`；渲染层不认识具体 key |
| 空态 | `IsVisible = false` 整区隐藏（规则 6：隐藏而非留空，不占布局空间） |
| 归属 | 由**模式/调用方在构造期注册**，对话框渲染层不感知业务 |

**副作用（正向）**：
- 原 spec §11 列的「260px 窄面板内标题 + 复选框溢出」风险**随之消失** —— 标题行恢复纯标题
- 原缺陷 D3（禁用态 CheckBox 的 ToolTip 可能不显示）**自然解决** —— 提示挂在独立可命中的 `?` 触发器上，不依赖禁用控件收事件

**已否决的备选**：

| 备选 | 否决原因 |
|------|---------|
| 仍放预览面板标题行 | 260px 窄面板挤标题 + 控件；将来参数超过 2 个即溢出，且与预览面板职责混淆 |
| 参数区放在确定/取消同一行 | 参数多时会把按钮挤到换行，破坏主操作位置稳定性 |
| 参数区右对齐 | 与「浏览 → 选目标 → 调参数 → 确认」的左→右阅读顺序相反 |
| 各模式各写一套参数 UI | 渲染逻辑重复，加参数要改多处；注册表方案一处生效 |

### 2.4 原 2.3 已否决备选（留档）

| 备选 | 否决原因 |
|------|---------|
| 持久化到 `AppSettings` | 违反决策 A；且设置窗口已有入口，无需两处 |
| 根目录仍可勾选（只不改结果） | 无效控件比禁用更糟，用户会以为勾选生效了 |
| 引入 `INotifyPropertyChanged` 支持运行中切语言 | YAGNI：对话框每次 `ShowExtractFolderAsync` 都 `new` 实例，`LocalizationManager.T(...)` 构造期求值即为当前语言；模态生命周期短，运行中切语言非本功能引入的问题 |
| 为可测性给 `ExtractFlow` 加接口/DI | 与本功能无关的架构重构，成本远超收益 |

---

## 3. 实现方案

### 3.1 新增返回通道 `ExtractPickResult`

新建 `Dialogs/ExtractPickResult.cs`（从 1263 行的对话框文件中拆出，聚焦职责）：

```csharp
namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 解压目录选择结果：目标目录 + 本次解压是否保留压缩包内完整路径。
/// 决策 A：仅本次生效，不回写 <c>AppSettings.ExtractPreserveFullPath</c>。
/// </summary>
/// <remarks>
/// 参数区（决策 3）可承载任意多个参数，但**已知参数仍用强类型字段**承载 ——
/// 类型系统保证「值一定传到解压侧」，这是本计划的核心诉求；
/// 未来确需动态参数时，另行扩展为携带 <c>IReadOnlyDictionary&lt;string, bool&gt;</c>，
/// 不在本次预先泛化（YAGNI）。
/// </remarks>
public sealed record ExtractPickResult(string DestPath, bool PreserveFullPath);
```

### 3.2 `ShowExtractFolderAsync` 改自建窗

不再走 `ShowInternal`（后者返回 `string?`，无法承载勾选值）：

```csharp
public static async Task<ExtractPickResult?> ShowExtractFolderAsync(
    Window owner, IReadOnlyList<ArchiveItem> entries, string? initialPath = null,
    string currentFolder = "", bool preserveFullPath = true)
{
    var dialog = new CustomFilePickerDialog(PickerMode.ExtractFolder, entries, null, initialPath, null,
        currentFolder, preserveFullPath)
    { WindowStartupLocation = WindowStartupLocation.CenterOwner };
    await dialog.ShowDialog(owner);
    // ★ D6 修正：Ok_Click 无条件 SelectedPath = _currentDir，理论上可能为空串
    return !string.IsNullOrEmpty(dialog.SelectedPath)
        ? new ExtractPickResult(dialog.SelectedPath, dialog.SelectedPreserveFullPath)
        : null;
}
```

> **D6 修正**：原判断 `dialog.SelectedPath is { } path` 对**空字符串也会命中**（`is { }` 只排除 null），
> 可能返回 `ExtractPickResult("", …)`。改用 `!string.IsNullOrEmpty(...)`。

`ShowInternal` 函数体保持不动（已无调用方传 `ExtractFolder`，其 `currentFolder`/`preserveFullPath` 参数保留供未来模式）。

### 3.3 对话框 UI（RootGrid 新增参数区行）

`ExtractFolderPanel` **完全不改**（标题行保持原样的单个 `TextBlock`，不再塞任何控件）。改动全在 `RootGrid`。

**RowDefinitions**：`"Auto,*,Auto,Auto,Auto"` —— 在索引 3 插入参数行，**原「确定/取消」`StackPanel`（`.axaml:429`）的 `Grid.Row="3"` 必须同步改为 `Grid.Row="4"`**，否则两者重叠（无编译错误，只会视觉错乱）。

```xml
<Grid x:Name="RootGrid" RowDefinitions="Auto,*,Auto,Auto,Auto" Margin="{DynamicResource SpacingMdThk}">
    <!-- Row 0: 地址栏 -->
    <!-- Row 1: BrowserGrid -->
    <!-- Row 2: FileNameArea（SaveFile/OpenFile 模式可见，其余 IsVisible=false） -->

    <!-- ═══ Row 3: 参数区（通用参数宿主 · 无参数时整体隐藏） ═══ -->
    <Grid Grid.Row="3" x:Name="OptionsRow" IsVisible="False">
        <Border BorderBrush="{DynamicResource ThemeBorderBrush}"
                BorderThickness="1"
                CornerRadius="{DynamicResource BorderRadius}"
                Padding="{DynamicResource SpacingSmThk}"
                Background="{DynamicResource ThemeSurfaceBgBrush}">
            <!-- ★ 必须用 Grid 而非横向 StackPanel：StackPanel 沿 orientation 方向给子项无穷宽度，
                 WrapPanel 拿到 ∞ 可用宽度后不会换行，参数变多时整区溢出窗口（DoD 20 会失败） -->
            <Grid ColumnDefinitions="Auto,*" ColumnSpacing="{DynamicResource SpacingSm}">
                <!-- 左：区域标题（仅作分组视觉标识；不加 x:Name，避免与同名属性重复定义） -->
                <TextBlock Grid.Column="0"
                           VerticalAlignment="Center"
                           FontSize="11"
                           Foreground="{DynamicResource ThemeTextSecondaryBrush}"
                           Text="{Binding OptionsCaptionText}" />

                <!-- 右：参数项容器（ItemsControl + WrapPanel，参数增多自动换行）。
                     ItemsSource 只由 XAML 绑定提供，code-behind 不再重复赋值（避免双数据源）。 -->
                <ItemsControl Grid.Column="1" ItemsSource="{Binding Options}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel Orientation="Horizontal" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <!-- 参数项：标签 + 控件。MinHeight 用紧凑度资源键（规则 7） -->
                            <StackPanel Orientation="Horizontal"
                                        Spacing="{DynamicResource SpacingXs}"
                                        Margin="{DynamicResource SpacingXsThk}"
                                        MinHeight="{DynamicResource ControlHeightSm}">
                                <!-- 禁用提示：ToolTip.ShowOnDisabled=True 让禁用态也能弹出（D3 修法，
                                     无需独立「?」节点、无需派生属性、无需手动 RaiseChanged -->
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
</Grid>
```

**开关初始勾选态**由 `AddOption` 的 `initial` 参数在**构造 `PickerOptionItem` 时**写入（对象初始化器内），而非在 `ItemsSource` 赋值时 —— 见 §3.4。

> **D3 修法依据（已核实）**：Avalonia 对 `IsEnabled=false` 的控件不派发指针事件（禁用控件上的
> ToolTip 默认永不弹出）。本项目使用 **Avalonia 12.0.4**，已提供 `ToolTip.ShowOnDisabled`
> （`ToolTip.ShowOnDisabledProperty` / `SetShowOnDisabled(Control, bool)`，已在
> `Avalonia.Controls.xml` 中确认存在）。故直接给 CheckBox 加 `ShowOnDisabled="True"` +
> `Tip="{Binding DisabledHint}"`；`DisabledHint` 为 null 时提示服务不打开，天然满足
> 「可用时不显示提示」。

### 3.4 对话框 code-behind：参数注册表

`_extractPreserveFullPath`（`:79`）去 `readonly`。新增**参数注册表**分区（渲染层与业务层解耦）：

```csharp
// ── 通用参数区（宿主，不认识任何具体参数） ────────────────────────

/// <summary>单个参数项的显示模型。仅描述「标签 + 值 + 可用性」，不含业务语义。</summary>
/// <remarks>
/// 派生 <see cref="ObservableObject"/> 而非普通类：当前只有 CheckBox 双向回推
/// <see cref="IsChecked"/>，无需 PropertyChanged 也能工作；但若将来参数需要
/// 相互联动（如「全选」批量改 others），INPC 是唯一无需改造的路径。
/// </remarks>
public sealed class PickerOptionItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    /// <summary>禁用时提示文案；null = 不提示（ToolTip 服务遇 null 不打开）。</summary>
    public string? DisabledHint { get; init; }

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }

    private bool _isEnabled = true;
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
}

/// <summary>对话框「本次调用需要显示哪些参数」的注册表。空 → 参数区隐藏。</summary>
private readonly ObservableCollection<PickerOptionItem> _options = new();

/// <summary>参数区自身状态（供 XAML 绑定）。</summary>
public string OptionsCaptionText => LocalizationManager.T("Picker_OptionsCaption");
public IReadOnlyList<PickerOptionItem> Options => _options;

/// <summary>
/// 注册一个参数项。新增参数只需调用此方法，不改渲染层与布局（决策 3）。
/// </summary>
/// <param name="key">参数键，同时作为返回通道取值键。</param>
/// <param name="labelKey">标签 i18n key（规则 13）。</param>
/// <param name="initial">初值（调用方传入的设置值）。</param>
/// <param name="onChanged">值变化回调（参数自身消费）。</param>
/// <param name="isEnabled">可用性判定；null = 始终可用。<b>生命周期内只求值一次</b>。</param>
/// <param name="disabledHintKey">禁用提示 i18n key；null = 不提示。</param>
private PickerOptionItem AddOption(
    string key, string labelKey, bool initial,
    Action<bool> onChanged, Func<bool>? isEnabled = null,
    string? disabledHintKey = null)
{
    var item = new PickerOptionItem
    {
        Key = key,
        Label = LocalizationManager.T(labelKey),
        IsChecked = initial,                                   // ★ D1 修法：初值在此一次性写入
        IsEnabled = isEnabled?.Invoke() ?? true,                // 可用性静态，只求值一次（见下方说明）
        DisabledHint = disabledHintKey == null ? null : LocalizationManager.T(disabledHintKey),
    };
    item.PropertyChanged += (_, e) =>
    {
        // 只有 IsChecked 会变化（CheckBox 双向绑定回推）；无派生属性，故无需手动 RaiseChanged
        if (e.PropertyName == nameof(PickerOptionItem.IsChecked))
            onChanged(item.IsChecked);
    };
    _options.Add(item);
    OptionsRow.IsVisible = true;                              // ★ 空态：任一参数注册即显示
    return item;
}
```

**参数区自身状态**（同上方）：

**ExtractFolder 模式注册「保留完整路径」**（构造函数内，`InitializeComponent()` 之后）：

```csharp
// ── Extract preserve-full-path toggle（参数区的一个参数项） ────────

/// <summary>本次解压是否保留完整路径。仅本次生效，不回写 AppSettings（决策 A）。</summary>
public bool SelectedPreserveFullPath => _extractPreserveFullPath;

/// <summary>根目录时无前缀可裁，两模式结果相同 → 禁用（决策 a）。</summary>
public bool IsPreserveFullPathToggleAvailable => !string.IsNullOrEmpty(_extractCurrentFolder);

AddOption(
    key: "preserveFullPath",
    labelKey: "Picker_PreserveFullPath",
    initial: _extractPreserveFullPath,                        // ← D1：初值即字段值
    onChanged: v => { _extractPreserveFullPath = v; SchedulePreviewRebuild(_currentDir); },
    isEnabled: () => IsPreserveFullPathToggleAvailable,
    disabledHintKey: "Picker_PreserveFullPathDisabledHint");

// ★ 不在此处赋 OptionsItemsControl.ItemsSource —— XAML 已绑定 {Binding Options}，
//   两者指向同一 ObservableCollection，ItemsControl 自行订阅 INotifyCollectionChanged，
//   注册顺序（无论前后）不影响渲染结果；code-behind 再赋一次反而用本地值覆盖掉 XAML 绑定。
```

> **D1 修正说明（关键）**：原方案只说「构造函数订阅事件」，从未把 `initial` 写进 `IsChecked`，
> 导致字段保留设置值而复选框恒显示未勾选 —— 视觉与实际背离（D1）。
> 现由 `AddOption` 的 `initial` 在**对象初始化器内**一次性写入，`_options.Add` 之前即已就位。
>
> **D3 修正**：用 `ToolTip.ShowOnDisabled="True"`（见 §3.3），因此**不需要**独立的 `?` 节点、
> **不需要** `ShowDisabledHint` 派生属性、**不需要**任何手动 `RaiseChanged`
> （`CommunityToolkit.Mvvm` 8.4.2 的 `ObservableObject` 也没有 `RaiseChanged` 方法，
> 且其 `OnPropertyChanged` 是 protected，从对话框的 lambda 外部根本无法调用）。
>
> **可用性只求值一次是正确的**：`_extractCurrentFolder` 是 `readonly` 构造期固定字段
> （`.axaml.cs:78`，`:245` 赋值），`IsPreserveFullPathToggleAvailable` 只依赖它，
> 对话框生命周期内不可能变化。**不要**写「IsEnabled 由参数自身刷新（如 currentFolder 变化）」
> 这类注释 —— 那描述了一条不存在的刷新路径。若将来可用性真需动态化，最小改动是
> 改 `item.IsEnabled`（`PickerOptionItem` 有 INPC），不需要给对话框加 INPC。
>
> **D5 修正**：不新增 `ExtractPanelTitleText`。既有 `ExtractPreviewTitle`（`:112`）取值相同，
> 标题行沿用原绑定。**并注意 `x:Name` 不得与同名公开属性重复** —— Avalonia 的 name generator
> 会在同一 partial 类生成同名 `internal` 成员，`x:Name="OptionsCaptionText"` 与
> `public string OptionsCaptionText` 并存会导致 CS0102。现有代码
> （属性 `ExtractPreviewTitle` vs `x:Name="ExtractPreviewTitleText"`）正是刻意避开此模式。
### 3.5 消费点改造

**`MainWindowViewModel`**：

- `:78` 委托改返回类型 → `Func<IReadOnlyList<ArchiveItem>, string?, string, bool, Task<ExtractPickResult?>>?`
- `:2354`（`ExtractSelectedTo`，有对话框）→ 消费 `pick`，语义 `if (pick is null || string.IsNullOrEmpty(pick.DestPath)) return;`，调用传 `pick.PreserveFullPath`
- `:2333`（`ExtractSelectedHere`，**无对话框**）→ 传设置值保持原行为
- `:2365` `ExtractSelectedEntriesCoreAsync` 签名加 `bool? preserveFullPath = null`
- `:2375` 改传 `preserveFullPath ?? settings.ExtractPreserveFullPath`
- `var settings = AppSettings.Load()`（`:2371`）**保留**——`FileConflictAction`（`:2375`）、`OpenFolderAfterExtract`（`:2385`）与上述默认值兜底仍在用

> **D6 修正（双重默认值，避免重复反序列化）**：`ExtractSelectedHere` 无对话框，若按原方案在 `:2333` 写
> `AppSettings.Load().ExtractPreserveFullPath`，则 `:2333` 与 `:2371` 各反序列化一次 settings.json
> （同一次解压读两遍）。改用 `bool? preserveFullPath = null` 表达「本次无勾选值，走设置默认」，
> 默认值解析只在 `:2375` 一处发生。
>
> **D6 修正（空值守卫统一）**：原方案只给 `ExtractSelectedTo` 写了
> `string.IsNullOrEmpty(pick.DestPath)` 守卫，`DragDropService` 侧没有。两个消费点**统一**采用该守卫。

**`DragDropService`**：

1. 在 `:75` 的 `if` **之前**声明 `var preserveFullPath = _settings.ExtractPreserveFullPath;`（非兜底分支默认值）
2. 兜底分支（`:84-92`）消费 `pick` 覆盖 `targetDir` 与 `preserveFullPath`，守卫与上面统一：
   ```csharp
   var pick = await CustomFilePickerDialog.ShowExtractFolderAsync(
       _ownerWindow, itemsToExtract, initialPath,
       _currentFolder, _settings.ExtractPreserveFullPath);
   if (pick is null || string.IsNullOrEmpty(pick.DestPath))
   {
       App.DebugLog("[DragDropService] User cancelled extract folder picker");
       return;
   }
   targetDir = pick.DestPath;
   preserveFullPath = pick.PreserveFullPath;
   ```
3. `:105` 改传局部变量 `preserveFullPath`（**不是** `_settings.ExtractPreserveFullPath`）

> `:105` 同时还位于**目标已检测到**的分支（不经对话框）—— 该分支 `preserveFullPath` 保留 `:75` 前的
> 设置默认值。**两处不可混用**，这是本改动最容易出错的地方。

---

## 4. 测试策略（含对设计文档 §10.1 的纠正）

> ⚠ **设计文档 §10.1 承诺的三条 mock 透传测试不可写**，实施时按本节替代方案执行，并回写 spec §10.1。

### 4.1 不可写的原因（已核验）

| 原承诺 | 阻塞原因 |
|--------|---------|
| mock 委托断言 `ExtractSelectedTo` 透传 | `ExtractFlow.RunSelectedItemsExtractionAsync` 是 `static` 且内部 `new ProgressWindow(...)`（`:78`），无 DI 缝隙无法拦截 |
| mock 断言 `DragDropService` 兜底透传 | `DragDropService` 是 `internal class`；UI 项目 `InternalsVisibleTo` 仅授予 `MantisZip.Tests`，**测试项目编译期即不可引用** |
| mock 断言非兜底用 settings | 同上 |

### 4.2 替代方案

**A. 行为契约测试**（真实可跑，新建 `tests/MantisZip.UI.Avalonia.Tests/ExtractPreserveFullPathContractTests.cs`）

用 `SelectedItemsExtractService`（`public sealed`，类声明 `:13`；`ExtractEntriesAsync` 在 `:29`）真解压到临时目录，断言磁盘布局：

| 用例 | 断言 |
|------|------|
| `preserveFullPath=true, currentFolder="docs"` | `["docs/img/logo.png", "docs/readme.txt"]` |
| `preserveFullPath=false, currentFolder="docs"` | `["img/logo.png", "readme.txt"]` |
| `currentFolder=""` 两模式 | 两者**完全相等**且为 `["docs/img/logo.png", "docs/readme.txt"]` ← 锁定决策 a |

第三条同时是「两模式在根目录等价」的**可执行证明**，比文字论证更硬。
（以上三行断言已用原型中的 resolver JS 移植版实测验证，输出逐字一致。）

**夹具准备（原方案遗漏，实施必需）** —— 三条断言都需要一个含 `docs/img/logo.png` + `docs/readme.txt`
的压缩包 + 对应的 `ArchiveItem` 列表 + 明确的 `conflictAction`：

```csharp
[AvaloniaFact]
public async Task PreserveFullPath_True_KeepsPrefix()
{
    var tmp = Path.Combine(Path.GetTempPath(), $"mz_pfp_{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(tmp, "src", "docs", "img"));
    await File.WriteAllTextAsync(Path.Combine(tmp, "src", "docs", "readme.txt"), "hi");
    await File.WriteAllBytesAsync(Path.Combine(tmp, "src", "docs", "img", "logo.png"), [1, 2, 3]);

    var archive = Path.Combine(tmp, "a.zip");
    var engine = new ZipEngine();                                  // public class，Core/Engines/ZipEngine.cs:21
    await engine.CompressAsync(archive, [Path.Combine(tmp, "src")], new ArchiveOptions());

    var dest = Path.Combine(tmp, "out");
    var entries = await engine.ListEntriesAsync(archive, null);    // 产出 ArchiveItem 列表
    var service = new SelectedItemsExtractService();
    await service.ExtractEntriesAsync(
        archive, null, entries, dest,
        conflictAction: "overwrite",        // → CreateExtractOptions 返回 null，不走冲突弹窗链路
        currentFolder: "docs",
        preserveFullPath: true,
        conflictDialog: null, progress: null, cancellationToken: TestContext.Current.CancellationToken);

    Assert.True(File.Exists(Path.Combine(dest, "docs", "img", "logo.png")));
    // …清理 finally
}
```

> `conflictAction: "overwrite"` 是有意选择：`CreateExtractOptions` 对 `overwrite` 返回 `null`
> （`SelectedItemsExtractService.cs:94`），从而不引入 `ConflictResolverAsync` 弹窗分支。
>
> **引擎取法修正（原方案此处事实有误）**：`ArchiveEngineFactory` 除 `GetEngineByExtension(string, IArchiveEngine)`
> 外，**还有**单参重载 `GetEngineByExtension(string)`（`Core/Abstractions/ArchiveEngine.cs:423`，被
> `DragDropService.cs:128` 使用）与 `GetEngine(ArchiveFormat)`（`:397`）。原注「唯一工厂方法…需额外
> fallback 参数」不成立。本测试仍用 `new ZipEngine()`（最直接），但理由是「无需工厂、无需参数」，
> 不是「只有这一个入口」。

> **D7 修正（测试属性与隔离）**：`SelectedItemsExtractService` 内部调用 `AppSettings.Load()`（`:58`）
> 与 `LocalizationManager.T()`（`:61`），因此测试必须用 **`[AvaloniaFact]`**（非 `[Fact]`），
> 由既有 `TestAppBuilder`（`[assembly: AvaloniaTestApplication]`）提供 headless 平台 ——
> 先例见 `MainWindowViewModelCommentTests`。
> 另需在测试内**隔离 `AppSettings`**（`%LOCALAPPDATA%\MantisZip\settings.json`）：用 `try/finally`
> 备份并恢复原文件，避免读写开发者真实设置导致污染本机配置或用例互相干扰。

**B. 架构守卫测试**（新建 `ExtractPreserveFullPathWiringTests.cs`）

> **D4 修正（两个致命前提，原方案均未规定）**：
> ① **仓库根定位已有先例可直接照抄** —— `tests/MantisZip.Tests/AboutWindowTests.cs:20-32`
> 从 `AppContext.BaseDirectory` 向上查找直到命中 `src` 目录。照此模式实现，**不要**自创
> `MantisZip.sln` 标记。找不到则 `Assert.Fail` 给出明确信息（**不要**静默跳过）。
> ② 原断言是**对空白敏感的精确子串匹配**，会把「换行格式化」误判为通过 ——
> 一旦有人把调用拆成多行，受保护的字符串消失，**测试通过但缺陷仍在**，比没有测试更危险。
> 修法：匹配前归一化空白 `Regex.Replace(src, @"\s+", " ")`。

| 守卫 | 断言（归一化空白后匹配） |
|------|------|
| `RepoRoot_IsLocatable` | 仓库根可发现；三个源文件存在（前置条件 Fail-fast） |
| `MainWindowViewModel_CoreMethodTakesNullablePreserveFullPathParameter` | `ExtractSelectedEntriesCoreAsync` 签名含 `bool? preserveFullPath = null`（D6 修正后的形态） |
| `MainWindowViewModel_CoreMethodBodyDoesNotReadSettings` | 花括号配平截取该方法体，断言其中**完全不出现** `settings.ExtractPreserveFullPath`（默认值兜底只在 `:2375` 一处） |
| `DragDropService_PassesLocalPreserveFullPathToExtractFlow` | 括号配平截取 `RunSelectedItemsExtractionAsync(` 实参列表：断言含局部变量 `preserveFullPath`、**不含** `_settings.ExtractPreserveFullPath` |

> **守卫的作用要诚实界定**：它们证明的是「坏读取没有回来」，**不是**「正确的值流过去了」。
> 后者由 §4.2-A 契约测试 + 人工验证 15/17/18 兜底。
> 第四条守卫刻意**不用**三元组子串（原方案的 `_currentFolder, _settings.ExtractPreserveFullPath,
> _settings.FileConflictAction`）：任何参数换序都会让该子串消失而**缺陷仍在**，属于假通过；
> 改为括号配平后只看该调用的实参列表，与顺序无关地判定「传的是局部变量」。
>
> 另注：`ExtractFlow.RunSelectedItemsExtractionAsync` 参数顺序是强类型签名
> （`ExtractFlow.cs:67-76`，已核实与 `:105` 的改动一致），因此**参数换序本身是编译错误** ——
> 守卫只需盯「值来源」，不必重复盯顺序。
>
> **不需要引入 Roslyn**（`Microsoft.CodeAnalysis.CSharp`）：守卫定位是回归绊线（tripwire），
> 且人工验证 15/17/18 在后面兜底，不值得为测试项目新增一个依赖。

**C. 参数区结构测试**（新增，新建 `PickerOptionsRegionTests.cs`）

> **B5 修正（原方案 5 条里有 4 条编译不过）**：
> - `OptionsRow` 是 Avalonia name generator 生成的 **`internal`** 成员；
> - UI 项目 `InternalsVisibleTo` 仅授予 `MantisZip.Tests`（`.csproj:17`），
>   `MantisZip.UI.Avalonia.Tests` **无 internal 访问权**；
> - `AddOption` 是 **private**。
>
> **修法（采用最小改动方案 a）**：在 `MantisZip.UI.Avalonia.csproj:17` 旁边补一行
> `<InternalsVisibleTo Include="MantisZip.UI.Avalonia.Tests" />`（与既有授予同款，直接先例），
> 测试只针对**公开面** + 构造对话框，不反射私有成员。

决策 3 的宿主行为必须被锁住，否则「通用参数区」退化为硬编码：

| 用例 | 断言 |
|------|------|
| `ExtractFolder_RegistersPreserveFullPath_WithInitialValue` | 构造 ExtractFolder 模式对话框 → `Options.Count == 1`、`Options[0].Key == "preserveFullPath"`、`IsChecked == 传入的 initial` ← **直接锁 D1 回归** |
| `OptionsRow_IsHidden_WhenModeRegistersNoOptions` | PickFolder / OpenFile / SaveFile / PickItems 模式 → `Options.Count == 0` 且 `OptionsRow.IsVisible == false`（规则 6） |
| `OptionsRow_IsVisible_AfterFirstAddOption` | ExtractFolder 模式 → `OptionsRow.IsVisible == true` |
| `OptionItem_DisabledHint_NullWhenNotDisabled` | 非根目录 → `Options[0].DisabledHint` 仍可非 null，但 `IsEnabled == true` |
| `OptionItem_Disabled_AtArchiveRoot` | `currentFolder == ""` 构造 → `Options[0].IsEnabled == false` 且 `DisabledHint != null` ← **锁决策 a** |

> 全部用例均以 `[AvaloniaFact]` 构造真实对话框（headless），走公开属性 `Options`、
> `IsPreserveFullPathToggleAvailable`、`SelectedPreserveFullPath` 断言。
> **`CollectOptions` 相关用例已删除** —— 该方法本身是死代码（见 §3.4 说明与 L5）。

**D. 人工验证**（无法单测的分支走向，见 §7 DoD）

---

## 5. i18n 新增 key（规则 13，三语成对）

| Key | zh-CN | zh-TW | en |
|-----|-------|-------|-----|
| `Picker_PreserveFullPath` | 保留完整路径 | 保留完整路徑 | Preserve full paths |
| `Picker_PreserveFullPathDisabledHint` | 在压缩包根目录，勾选与否结果相同 | 在壓縮包根目錄，勾選與否結果相同 | At the archive root, both settings produce the same result |
| `Picker_OptionsCaption` | 本次参数 | 本次參數 | Options |

> **D2 说明**：`Picker_ExtractPreviewTitle` **已存在于三语文件第 1060 行**
> （`CustomFilePickerDialog.axaml.cs:112` 一直在用），因此本计划**不重复添加**该 key。
> 注意其真实机制与原判断不同：重复 JSON key 在
> `JsonSerializer.Deserialize<Dictionary<string,string>>` 下是**后值覆盖、不抛异常**
> （已实测，见 §5.1），所以这是整洁性问题而非会破坏本地化的阻塞项。
> 真正会出问题的是 `AboutWindowTests.AllThreeLanguages_HaveSameKeySet`（三语 key 集校验）
> 与 DoD 第 4 条的「无重复 key」自查 —— 重复添加会让三语文件出现两份同 key。
> 新增的 `Picker_OptionsCaption` 是决策 3 的参数区标题，原方案中不存在。

> 插入 `strings.zh-CN.json` / `strings.zh-TW.json` / `strings.en.json` 文件头 `{` 之后，UTF-8 无 BOM + CRLF + 2 空格缩进。三语 key 集由 `AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 校验。
> 对话框用 code-behind 属性绑定（§3.4），**无需**登记进 `MainWindowViewModel.UpdateLocalizedStrings()`。

### 5.1 缺陷修正汇总（2026-10-01 审阅）

以下经逐行核实代码后确认，已在本文档各处修正。原型 `docs/prototypes/extract-preserve-full-path-toggle.html`
的「对照模式」可交互复现 D1 与 D3。

**第一轮（自查）7 项：**

| # | 级别 | 问题 | 修正位置 |
|---|------|------|---------|
| D1 | 高 | 初始勾选态从未回填 —— 字段保留设置值而复选框恒显示未勾选，视觉与实际背离 | §3.4 `AddOption(initial:)`；§4.2-C 锁回归 |
| D2 | 中 | `Picker_ExtractPreviewTitle` 已存在，不应重复添加 | §5 只新增 3 key |
| D3 | 高 | 禁用态 CheckBox 的 ToolTip 默认永不显示（禁用控件不派发指针事件） | §3.3 `ToolTip.ShowOnDisabled="True"` |
| D4 | 高 | 守卫测试无源码定位设施 + 断言对空白敏感 → 换行即假通过 | §4.2-B 照抄 `AboutWindowTests` 仓库根模式 + 空白归一化 + 括号/花括号配平截取 |
| D5 | 中 | `ExtractPanelTitleText` 与既有 `ExtractPreviewTitle` 重复；且 `x:Name` 与同名属性并存会 CS0102 | §3.4 沿用既有绑定、去掉多余 `x:Name` |
| D6 | 中 | 两消费点空值守卫不一致；`is { } path` 对空串命中；`AppSettings.Load()` 重复调用 | §3.2 `!string.IsNullOrEmpty`；§3.5 统一守卫 + `bool?` 双重默认值 |
| D7 | 中 | DoD 丢失规则 3 进度文档步骤；契约测试未指定 `[AvaloniaFact]`、未隔离 settings.json、**且未说明夹具压缩包如何生成** | §4.2-A 补夹具步骤 + `[AvaloniaFact]` + 隔离；§7 补回步骤 |

> **D2 严重性更正（实测）**：原判为「阻塞」并称「重复 JSON key 使
> `JsonSerializer.Deserialize<Dictionary<string,string>>` 抛 `ArgumentException` → 整张本地化表
> 加载失败 → 波及全应用文案」。**该机制描述经实测为假**：在本机 .NET 10 上
> `JsonSerializer.Deserialize<Dictionary<string,string>>("{\"a\":\"1\",\"a\":\"2\"}")`
> 返回 `{"a":"2"}`（**后值覆盖，不抛异常**，count=1）。`LocalizationManager.cs:85` 正是该调用。
> 故重复 key 只会**静默覆盖**，不会破坏本地化 —— 降级为整洁性问题。
> **行动不变**（仍不重复添加该 key），但不应再作为阻塞项陈述。

**第二轮（Oracle 架构评审）11 项：**

| # | 级别 | 问题 | 修正位置 |
|---|------|------|---------|
| **B1** | **阻塞** | `ShowExtractFolderAsync` 返回类型改动波及 **6 处调用**，原方案只覆盖 4 处 —— `ExtractSettingsWindow.axaml.cs:61`（CS0029）与 `:83-87`（CS0173）会**编译失败** | §1.3 补全清单；§6 加入该文件；§3.5 附改法 |
| **B2** | **阻塞** | `item.RaiseChanged(...)` **不存在**（CommunityToolkit.Mvvm 8.4.2 的 `ObservableObject` 无此方法；仓库内 0 处用法，正确范式是类内 `OnPropertyChanged`），且 `OnPropertyChanged` 是 protected，从对话框 lambda 外部不可调 | §3.4 整体删除该机制 |
| **B3** | **阻塞** | `_options[0].DisabledHint = ...` 违反 `init`-only（CS8852），且 `[0]` 索引隐含「preserveFullPath 第一个注册」的脆弱假设 | §3.4 改为 `AddOption(disabledHintKey:)` 参数，在对象初始化器内设置 |
| **B4** | **阻塞** | `x:Name="OptionsCaptionText"` 与 `public string OptionsCaptionText` 并存 → Avalonia name generator 在同一 partial 类生成同名 internal 成员 → **CS0102** | §3.3 去掉该 `x:Name` |
| **B5** | **阻塞** | §4.2-C 5 条结构测试有 4 条**编译不过**：`OptionsRow` 是 internal、`InternalsVisibleTo` 仅授予 `MantisZip.Tests`、`AddOption` 是 private | §4.2-C 补 `InternalsVisibleTo` + 只测公开面 |
| **H1** | **高** | 外层用横向 `StackPanel` → 沿 orientation 给子项**无穷宽度**，`WrapPanel` 永不换行 → 参数变多时**整区溢出窗口**（DoD 20 必失败） | §3.3 改 `Grid ColumnDefinitions="Auto,*"`（本文件 `:22` 已有 `ColumnSpacing` 先例） |
| **H2** | **高** | 确定/取消 `StackPanel` 的 `Grid.Row="3"` → `"4"` 只是注释暗示，未列入改动清单；照 §6 做会与参数区重叠且**不报错** | §3.3 + §6 显式列为一步 |
| M1 | — | D2 的机制与严重性描述有误 | §5.1 已更正（实测） |
| M2 | 中 | 契约测试未说明夹具压缩包 / `ArchiveItem` 列表 / `conflictAction` 如何准备 | §4.2-A 补完整夹具代码 |
| M3 | 中 | 「IsEnabled 由参数自身刷新（如 currentFolder 变化）」注释描述了**不存在的刷新路径**；`IsPreserveFullPathToggleAvailable` 是死属性 | §3.4 删注释 + 该属性改为实际被 lambda 使用 |
| M4 | 中 | 强制「先赋 `ItemsSource` 再 `AddOption`」的顺序**无必要**（`ObservableCollection` 两种顺序渲染相同）；code-behind 赋值反而用本地值**覆盖掉 XAML 绑定** | §3.4 删除该行，保留单一数据源 |
| M5 | 中 | DragDrop 守卫断言三元组子串 → 参数换序即假通过；且「无仓库根设施」只对 UI 测试项目成立，`AboutWindowTests` 已有现成模式 | §4.2-B 改括号配平 + 照抄先例 |
| M6 | 中 | `ExtractSettingsWindow` 链路（`currentFolder` 取默认 `""`）会让参数区显示**常驻禁用**项，原方案未讨论 | §1.3 + §9 + DoD 21 |
| M7 | 低 | 「唯一工厂方法是 `GetEngineByExtension(string, IArchiveEngine)`」不成立 —— 还有单参重载 `:423` 与 `GetEngine(ArchiveFormat)` `:397` | §4.2-A 已更正 |
| L1 | 低 | §3.3「字段是唯一事实来源，反向只从字段读」与 §3.4 `onChanged` 回写字段矛盾 | §3.3 删除该措辞 |
| L2 | 低 | 规则 7：`ItemsControl` 项模板未设 `MinHeight` | §3.3 补 `ControlHeightSm` |
| L3 | 低 | 规则 5：`CornerRadius="4"` 硬编码 | §3.3 改 `{DynamicResource BorderRadius}` |
| L4 | 低 | `MainWindowViewModel.cs:78` 的 XML 文档需同步更新 | §6 |
| L5 | 低 | 删除 `CollectOptions`（死代码）后 DoD 计数与 §4.2-C 需同步 | §3.4 / §4.2-C / §7 已同步 |
| L6 | 低 | §4.2-A 引「`public sealed`, `:29`」—— 类声明在 `:13`，`:29` 是方法 | §4.2-A 已更正 |

**`CollectOptions()` 死代码裁定（Oracle 问题 6）**：删除该方法及其测试用例。
理由：① `ExtractPickResult` 按设计保持强类型，字典无消费者；② 它是 private，无测试可达；
③ `_options.ToDictionary` 对重复键抛异常，而重复注册本就不该发生；
④ **可扩展性由 `AddOption` 注册表本身提供**，等真的出现第二个消费者时再提取字典。

**已核实无误**（不需改动）：全部行号（`:13` / `:78` / `:79` / `:112` / `:136-139` / `:153-164` / `:163` /
`:249` / `:310` / `:683` / `:772` / `:791-792` / `:82` / `:245`、`RootGrid` = `Auto,*,Auto,Auto`、
VM `:78` / `:2333` / `:2354` / `:2357` / `:2365` / `:2371` / `:2375` / `:2385`、
DragDrop `:75` / `:84` / `:105` / `:128`、`ExtractSettingsWindow.axaml.cs` `:56-62` / `:83-87`）；
`MainWindow.axaml.cs:183-184` 零改动成立；§4.1 三条「mock 不可写」理由全部成立；
决策 a 的等价性由 `TrimCurrentFolderPrefix`（`ExtractPathResolver.cs:25-31`）证实；
`ExtractFlow` 参数顺序与 `:105` 的改动一致（`ExtractFlow.cs:67-76`）；
规则 4/6/13/14/15 合规（`ThemeSurfaceBgBrush`/`ThemeTextSecondaryBrush`/`ThemeBorderBrush`
在 `ThemeLight.axaml:54,58` 与 `ThemeDark.axaml:54,58` **成对存在**；
`AllThreeLanguages_HaveSameKeySet` 存在于 `AboutWindowTests.cs:99`）；
规则 1 已合规（`docs/PLAN.md:37` 已有 P2 条目）。

**未由 Oracle 复核的两项**（本文档自行实测声明）：105 通过 / 2 跳过 的测试基线（`dotnet test`，2026-10-01）；
原型 resolver 的 Node 实测输出。

---

## 6. 变更文件清单

| 文件 | 改动 |
|------|------|
| `Dialogs/ExtractPickResult.cs` | **新建**：返回通道 record（强类型两字段） |
| `Dialogs/PickerOptionItem.cs` | **新建**：参数项显示模型（`public sealed` + `ObservableObject`，键/标签/值/可用性/禁用提示） |
| `Dialogs/CustomFilePickerDialog.axaml` | ① `RootGrid` RowDefinitions `Auto,*,Auto,Auto` → `Auto,*,Auto,Auto,Auto`；② 新增 `OptionsRow`（Grid.Row=3）+ `OptionsItemsControl`（`Grid ColumnDefinitions="Auto,*"` 容器 + `WrapPanel` 项面板 + `ToolTip.ShowOnDisabled`）；③ **`Grid.Row="3"` → `Grid.Row="4"`（确定/取消 StackPanel，`.axaml:429`）** —— 漏这步会重叠且不报错；④ `ExtractFolderPanel` 零改动 |
| `Dialogs/CustomFilePickerDialog.axaml.cs` | `:79` 去 `readonly`；`:136-139` `ShowExtractFolderAsync` 改自建窗 + `!string.IsNullOrEmpty`；新增 `PickerOptionItem` 注册表（`_options` / `AddOption` / `Options` / `OptionsCaptionText`）/ `SelectedPreserveFullPath` / `IsPreserveFullPathToggleAvailable`；构造函数 ExtractFolder 分支注册 `preserveFullPath` |
| `Dialogs/ExtractSettingsWindow.axaml.cs` | **⚠ 编译必需（B1）**：`:56-62` `BrowseFolder` lambda 改 `return (await …)?.DestPath;`；`:83-87` `BrowseAction` 改 `async` lambda 取 `.DestPath`（否则 CS0029 / CS0173） |
| `ViewModels/MainWindowViewModel.cs` | `:78` 委托返回类型 **+ 该委托的 XML 文档同步更新（L4）**；`:2354`/`:2357` 消费 `pick`（含空值守卫）；`:2365` 签名加 `bool? preserveFullPath = null`；`:2375` 改传 `preserveFullPath ?? settings.ExtractPreserveFullPath` |
| `Services/DragDropService.cs` | `:75` 前声明 `preserveFullPath` 局部变量；`:84-92` 消费 `pick`（含空值守卫）；`:105` 改传局部变量 |
| `MantisZip.UI.Avalonia.csproj` | **⚠ 测试必需（B5）**：`:17` 旁补 `<InternalsVisibleTo Include="MantisZip.UI.Avalonia.Tests" />`（与既有授予 `MantisZip.Tests` 同款） |
| `Localization/strings.{zh-CN,zh-TW,en}.json` | **+3 key**（`Picker_PreserveFullPath` / `Picker_PreserveFullPathDisabledHint` / `Picker_OptionsCaption`）；**不重复添加已存在的 `Picker_ExtractPreviewTitle`** |
| `tests/MantisZip.UI.Avalonia.Tests/ExtractPreserveFullPathContractTests.cs` | **新建**：契约测试 ×3（`[AvaloniaFact]` + 夹具压缩包 + settings 隔离） |
| `tests/MantisZip.UI.Avalonia.Tests/ExtractPreserveFullPathWiringTests.cs` | **新建**：架构守卫 ×4（照抄 `AboutWindowTests` 仓库根模式 + 空白归一化 + 括号/花括号配平） |
| `tests/MantisZip.UI.Avalonia.Tests/PickerOptionsRegionTests.cs` | **新建**：参数区结构测试 ×5（含 D1 回归锁、决策 a 锁） |

> `Views/MainWindow.axaml.cs:183-184` **零改动**（✅ 已核实 2026-09-30，非推测）：表达式体 lambda `(entries, initialPath, currentFolder, preserveFullPath) => ShowExtractFolderAsync(...)` 无显式返回类型，其自然类型跟随被调方法；§3.2 改 `ShowExtractFolderAsync` 返回值与 §3.5 改委托类型是**同一个动作的两端**，二者恒等匹配，故此闭包天然编译通过、无需触碰。
>
> `PickerOptionItem` 单独成文件而非内嵌对话框：它是对话框的**公开可测契约**（结构测试直接构造），
> 且 `Dialogs/` 下已有 `FileTypeOption` / `PickerMode` 等小类型同域先例。

---

## 7. 验收标准（DoD）

### 自动验证

1. `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 0 errors
   —— **首要信号**：`ExtractSettingsWindow.axaml.cs`（B1）与对话框是同一编译单元外的强依赖，
   返回类型改动若漏改必然在此暴露
2. `dotnet test tests\MantisZip.UI.Avalonia.Tests\...` — 现有 105 + 契约 3 + 守卫 4 + 参数区 5 = **117 passed / 0 failed / 2 skipped**
3. `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` 无新增失败
4. 三语 key 集一致（`AllThreeLanguages_HaveSameKeySet` PASS），且**无重复 key**（`Picker_ExtractPreviewTitle` 仍只有 1 处/语言）
5. 原生 Picker 自查仅 3 处豁免（规则 15）：
   ```powershell
   git grep -n -E "OpenFilePickerAsync|SaveFilePickerAsync|OpenFolderPickerAsync" -- 'src/*.cs'
   ```
6. 架构守卫 4 条全 PASS（含 `RepoRoot_IsLocatable` 前置条件）
7. 参数区结构测试 5 条全 PASS（含 `ExtractFolder_RegistersPreserveFullPath_WithInitialValue` —— D1 回归锁、
   `OptionItem_Disabled_AtArchiveRoot` —— 决策 a 锁）
8. **规则 3 进度文档**：更新 `docs/PROGRESS.md`（里程碑）与 `docs/progress-avalonia-detail.md`（细节）
9. **规则 1 计划同步**：若实施中再次变更本计划，同步 `docs/PLAN.md:37` 说明

### 人工验证（逐项记录结果）

10. **核心验收点** —— 勾选/取消勾选后，**逐条比对最终落盘路径与预览树完全一致**
11. 取消勾选后预览树在 ~300ms 内重排（`CurrentFolder` 前缀被裁掉）
12. 压缩包**根目录**解压 → 该参数呈**禁用**态 + **悬停 CheckBox 本体**（无需点 `?`）显示提示文案，且勾选状态不影响结果
13. 决策 A 正向：设置 `ExtractPreserveFullPath=true`，子目录解压时**取消勾选** → 实际按**裁剪后**路径落盘（未回读设置）
14. 决策 A 反向：操作后重开设置窗口 → `ExtractPreserveFullPath` 仍为 `true`（未落盘）
15. `ExtractSelectedHere`（右键「解压选中项到此处」）路径**行为不变**（按设置值）
16. 拖拽到已检测到的目标目录（不弹窗）→ 按设置值落盘（兜底分支之外未受影响）
17. 拖拽目标检测失败 → 弹对话框 → 取消勾选 → 确定 → 落盘为裁剪后路径（兜底透传生效）
18. 三语切换后重开对话框 → 参数标签 / 参数区标题 / 禁用提示 / 面板标题 均为对应语言
19. **参数区空态**：PickFolder / SaveFile / OpenFile / PickItems 情形 → 参数区**整体不显示**，确定/取消行不因此错位
20. **参数区扩展**：临时加第 2、第 3 个参数 → 自动换行不溢出、不挤压按钮（对应原型「＋ 添加示例参数」）
21. **`ExtractSettingsWindow` 链路（B1/M6）**：解压设置窗口点「浏览」→ 弹窗仍能正常选目录并回填；
    参数区显示一个**常驻禁用**的「保留完整路径」（该链路 `currentFolder` 取默认 `""`，
    预览与实际都不受该值影响 —— 确认可接受，不出现「能点但无效」的错觉）
22. **布局无重叠（H2）**：确认「确定/取消」按钮行与参数区不重叠（`Grid.Row` 已 3→4）

---

## 8. 工时估算

**3.5-4h**（🟡中）

| 项 | 工时 |
|----|------|
| 契约测试 + 架构守卫 + 参数区结构测试（§4.2 A/B/C） | 0.9h |
| `ExtractPickResult` + `ShowExtractFolderAsync` 返回通道 | 0.3h |
| 参数区宿主（`PickerOptionItem` + `AddOption` + AXAML 含 Grid 换行修正） | 0.8h |
| 注册「保留完整路径」参数项 + 预览联动 | 0.2h |
| 三个消费点改造（含两个调用点区分） | 0.5h |
| **`ExtractSettingsWindow.axaml.cs` 两处调用点适配（B1）** | 0.2h |
| **测试项目 `InternalsVisibleTo` 授予（B5）** | 0.1h |
| 三语 key | 0.2h |
| 构建 + 全量测试 + 13 项人工验证 | 0.7h |

> 相比原估算（2-3h）增加约 1h：①参数区通用宿主（可扩展 + 空态隐藏 + 结构测试）是原方案没有的
> 增量（原「标题行 + 复选框」约 0.5h，现分解为宿主 0.8h + 注册 0.2h）；
> ②Oracle 评审暴露的 B1 调用点适配与 B5 测试授权各需一小步。

---

## 9. 边界

- **不改** `AppSettings` 字段、不改设置窗口（`ExtractPreserveFullPath` 已是既有字段，仅作为初始勾选值来源）
- **不改** `ExtractSettingsWindow` 的**行为**（压缩/解压设置窗口无此开关）—— 但其 `.axaml.cs` **必须改两处调用点**以适配返回类型（B1）。副作用见下
- **不改** `ExtractPathResolver` / `ResultPreviewService.BuildExtractPreview` / `SelectedItemsExtractService` —— 三者已正确消费 `preserveFullPath`，缺陷纯在对话框返回通道与调用方回读
- **不改** `ExtractFolderPanel` 内部结构（决策 3 移出参数后此处零改动）
- **不改** WPF（规则 11）
- 拖拽**非兜底**分支不弹对话框，保持按设置值（无勾选值可用）
- 不做「根目录下开关仍可勾选」的折中（决策 a）
- **不预先泛化返回类型**：`ExtractPickResult` 保持两字段强类型；参数区虽支持多参数，
  但只有已知参数进强类型字段（§3.1 remarks，YAGNI）
- **不引入 Roslyn**：守卫测试定位为回归绊线，人工验证兜底，不为测试项目新增依赖
- **已知可接受副作用（M6）**：`ExtractSettingsWindow` 的两条链路不传 `currentFolder`
  （取默认 `""`）与 `preserveFullPath`（取默认 `true`，而 `AppSettings` 默认是 `false`）。
  因 `TrimCurrentFolderPrefix` 在 `currentFolder` 为空时直接早退，该值在这两条链路里
  **确实无影响**，预览与实际都正确；但参数区会显示一个**常驻禁用**的「保留完整路径」。
  这是如实反映语义（不是坏 UI），故接受并列入 DoD 21 人工确认。
  若日后认为噪声过大，可在 `AddOption` 上加「无 currentFolder 上下文则不注册」的显式条件 ——
  但那是独立的产品决策，不在本计划范围。

---

## 10. 与设计文档的差异（实施后回写 spec）

| spec §10.1 原承诺 | 实际处置 |
|------------------|---------|
| mock 委托断言 `ExtractSelectedTo` 透传 | ❌ 不可写（`ExtractFlow` static 无 DI）→ 守卫 #2/#3 + 人工验证 10/13 |
| mock 断言 `DragDropService` 兜底透传 | ❌ 不可写（internal + 无 `InternalsVisibleTo`）→ 守卫 #4 + 人工验证 17 |
| mock 断言非兜底用 settings | ❌ 不可写 → 人工验证 16 |
| 追加：契约测试 | ✅ `ExtractPreserveFullPathContractTests` ×3 |
| §5.2 开关实时驱动预览 | ✅ 保留（复用 `SchedulePreviewRebuild` 300ms 防抖） |

**spec 需回写的 8 处**（本计划已先行修正，spec 保持过时）：

| spec 章节 | 原内容 | 修正后 |
|----------|--------|--------|
| §5.2 | 复选框在 `ExtractFolderPanel` 标题行，`IsVisible="False"` 由 code-behind 开启 | 移至 `RootGrid` 第 3 行参数区；`ExtractFolderPanel` 零改动；按钮行 `Grid.Row` 3→4 |
| §5.1 | `SelectedPreserveFullPath { get; private set; }`，另有独立 `IsEnabled` 赋值语句 | 由 `AddOption(initial:, isEnabled:, disabledHintKey:)` 统一承载（修正 D1/D3） |
| §5.1 | `Ok_Click` 返回 `ExtractPickResult`（「`:1085`/`:1089` 等 ExtractFolder 分支」） | `Ok_Click` **不动**，仍只设 `SelectedPath`；返回对象由 `ShowExtractFolderAsync` 构造（唯一构造点，避免两处不一致） |
| §5.2 | 禁用提示挂 CheckBox 的 `ToolTip.Tip` | 需加 `ToolTip.ShowOnDisabled="True"`（本项目 Avalonia 12.0.4 已提供），否则禁用态永不弹出 |
| §2.3 / §6.3 | 只列 `MainWindowViewModel` 与 `DragDropService` 两个消费方 | 另有 **`ExtractSettingsWindow.axaml.cs:61` / `:83-87`** 两处调用，改返回类型即编译失败（B1） |
| §7 | 新增 2 个 `Picker_` key | 3 个（新增 `Picker_OptionsCaption`）；`Picker_ExtractPreviewTitle` 已存在不可重复添加 |
| §10.1 | 三条 mock 测试 | 不可写，改为契约 3 + 守卫 4 + 参数区结构 5（§4.2） |
| §11 | 风险「面板 260px 下标题 + 复选框溢出」 | **风险消失** —— 参数移出预览面板；但**新增**「横向 StackPanel 使 WrapPanel 不换行」的溢出风险，已在 §3.3 改用 Grid 消除 |
| §12 | DoD 10 项 | 扩为 22 项（§7），含规则 1/3 文档同步、参数区空态/扩展、`ExtractSettingsWindow` 链路、布局无重叠 |

**决策 3 的原型验证**：`docs/prototypes/extract-preserve-full-path-toggle.html`
- 「调用情形」切换验证不同情形注册不同参数、无参数时整区隐藏
- 「＋ 添加示例参数」验证多参数自动换行
- 「对照模式」可交互复现 D1（初值未回填）与 D3（禁用态提示）
- 该原型的 resolver 为 `ExtractPathResolver` 的 JS 移植，预览与落盘共用同一函数 ——
  §4.2-A 的三条契约断言已用它在 Node 中实测，输出逐字一致
- **原型未体现 B1/H1**（原型是浏览器模拟，用 `flex-wrap` 天然换行，故 H1 的
  `StackPanel`+`WrapPanel` 问题在原型里不会出现；B1 属编译期问题，原型不涉及）。
  实现时以本文档 §3.3 的 AXAML 为准，不要照抄原型的 CSS 结构
