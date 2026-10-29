# 面包屑地址栏（PathBreadcrumb）改造

> **状态**: 📋 计划中（待实施）
> **档位**: **A 档 · v0.5.5 第一项**（2026-10-29 定为文件选择与导航阶段的首项：与内嵌选择器共用同一批回归面，一次跑完省一次全量验证）
> **范围**: Avalonia 版三处地址栏统一改造（主窗口 / QuickPathPicker / CustomFilePickerDialog）
> **修订**: 2026-10-21 — 按代码事实核对修正行号与行为描述（`TryConfirmFile` 无模式判断、`PathAutoComplete.Text` 实际 3 处直接同步）；`Path` 定为 StyledProperty + OneWay；补磁盘空路径态、`Breadcrumb_EditPlaceholder` 与三语同步；QuickPathPicker 恢复 4 宿主回归范围

## TL;DR

将现有三处「AutoCompleteBox 文本框 + Enter 确认」地址栏改造为 Windows 资源管理器式**面包屑导航**（路径分段显示、每段可点击直达、点末尾空白进入编辑态）。新增通用控件 `PathBreadcrumb`，通过 `Path`（OneWay，宿主→控件）+ `NavigateRequested` 事件回调接入三个宿主，宿主导航逻辑（`NavigateToFolderPath` / `NavigateTo` / `CoerceToDirectory`）保持单一事实来源不变。

**设计决定**（已确认）：
1. **编辑态进入**：点击面包屑末尾空白区（资源管理器惯例），另支持 `Ctrl+L` 键盘直达
2. **折叠策略**：段数阈值（`MaxSegments = 6`，超过时中间段合并为 `…`），第一版不做宽度测量
3. **分隔符同级目录下拉**（`›` 点击弹同级子目录）：**第一版不做**，仅预留 `EnumerationRequested` 事件，后续增强
4. **虚拟路径根段**：显示 📦 图标段（ToolTip「压缩包根目录」），点击回根（`NavigateToFolderPath("")`）
5. **编辑态保留补全**：编辑态用 `AutoCompleteBox`，补全源由宿主注入**文本响应式 Provider**（`Func<string?, IEnumerable<string>>`，参数 = 编辑框文本；主窗口 = `FolderPaths` 内存集合，磁盘两处 = 各自现有历史+文件系统枚举逻辑改写版）
6. 磁盘路径**不做**「此电脑」虚拟根段（首段 = 盘符 / UNC 根，不可点击）
7. 磁盘路径段点击天然是目录（无归一化需求）；编辑态 Enter 文本由宿主处理——**CustomFilePickerDialog 保留「输入文件路径 → `TryConfirmFile` 确认选中」分支**（现状 `PathAutoComplete_KeyDown` 行为，**任意模式**都生效，见下）
8. **`Path` 用 StyledProperty + OneWay（宿主→控件）**：控件自身从不回写 `Path`，所有跳转只发 `NavigateRequested` 由宿主写回 → 从根上消除双向绑定环
9. **磁盘空路径态**：浏览态显示 `Breadcrumb_EditPlaceholder` 占位文本，整行可点击进入编辑态（`QuickPathPicker.Path` 默认空串，其 4 个宿主的初始值也可能为空）
10. **`TryConfirmFile` 分支严格照抄现状**（`CustomFilePickerDialog.axaml.cs:664-680`）：`Directory.Exists → NavigateTo`；`File.Exists → TryConfirmFile`；**没有 `_mode` 判断**，`TryConfirmFile`（:1146-1152）同样不校验模式。**禁止**擅自加 OpenFile/SaveFile 模式门——那会改掉文件夹模式下的现有行为；若确需收紧，另立 bugfix 计划

## 现状盘点

| 位置 | 文件 | 当前实现 | 路径类型 |
|---|---|---|---|
| 主窗口文件列表工具栏 | `Views/MainWindow.axaml:1046-1055`（外层 Border `1042-1056`） | `AddressBar` AutoCompleteBox，`Text={Binding CurrentFolder}`、`ItemsSource={Binding FolderPaths}`、`FilterMode=StartsWith`，Enter → `AddressBar_KeyDown`（`.axaml.cs:1549-1560`）→ `vm.NavigateToFolderPath(box.Text ?? "")` | 压缩包内虚拟路径（`/` 分隔，空串 = 根） |
| QuickPathPicker | `Controls/QuickPathPicker.axaml:11-20` + `.axaml.cs` | Row 0 `PathInput` AutoCompleteBox（`MinHeight=ControlHeightMd`），`TextChanged`/`KeyDown(Enter)` 双向同步 `Path`，`InitAutoComplete()`（`:96-133`）提供历史+文件系统补全；Row 1 收藏/历史/窗口/浏览四按钮 | 磁盘路径 |
| CustomFilePickerDialog | `Dialogs/CustomFilePickerDialog.axaml:84-92` + `.axaml.cs` | 地址行列 4 `PathAutoComplete` AutoCompleteBox（`MinHeight=ControlHeightSm`），Enter → `PathAutoComplete_KeyDown`（`:664-680`）→ `NavigateTo`；列 0-3 后退/前进/向上按钮、列 5 收藏按钮保留；Row 1 `CurrentPathText` 显示当前路径 | 磁盘路径 |

关键宿主 API（全部保留，面包屑只替换输入控件，行号已核对）：
- `MainWindowViewModel.NavigateToFolderPath(string path)` — `MainWindowViewModel.Navigation.cs:89`，虚拟路径导航（压栈 + `CurrentFolder=path` + `PopulateEntries` + `UpdatePreviewForFolder` + 目录树同步）
- `MainWindowViewModel.FolderPaths` — `MainWindowViewModel.cs:548`（`ObservableCollection<string>`），全量唯一目录路径（含 `""` 根），已在 `LoadArchiveAsync` 填充，**同时是编辑态补全源与（未来）同级目录枚举源**
- `QuickPathPicker.CoerceToDirectory(string)` — `QuickPathPicker.axaml.cs:232`，磁盘路径目录归一化（静态）
- `CustomFilePickerDialog.NavigateTo(string dir)` — `CustomFilePickerDialog.axaml.cs:684`（private），磁盘导航（back/forward 栈 + `PathHistoryManager.Record` + `QuickPath.SetCurrentPath` + 收藏状态）
- `PathHistoryManager.Record(path)` — `Core/Utils/PathHistoryManager.cs:38`；`CustomFilePickerDialog.NavigateTo` 内部已调用，`QuickPathPicker` 由 Enter 处理器调用（改造后由 `NavigateRequested` 处理器调用），**不要重复记录**

> **QuickPathPicker 是复用控件**：`CompressSettingsWindow.axaml:159`（`Path` ← `OutputDirectory`）、`ExtractSettingsWindow.axaml:106`（`DestinationPath`）、`SettingsWindow.axaml:1553`（`CustomPath`）、`UiTestWindow.axaml:1350` 共 4 处宿主。替换其内部输入控件等于同时改这四个窗口，验收必须逐个回归。

## 控件设计：`PathBreadcrumb`

新文件：`Controls/PathBreadcrumb.axaml` + `.axaml.cs`（自包含控件，无新依赖）。

### API

```csharp
public enum BreadcrumbPathKind { Virtual, Disk }

public class PathBreadcrumb : UserControl
{
    // ⚠ 必须是 AvaloniaProperty：宿主用 Path="{Binding ...}" 绑定，普通 CLR 属性无法承接绑定。
    // 范式：QuickPathPicker.PathProperty（QuickPathPicker.axaml.cs:21）
    public static readonly StyledProperty<string> PathProperty =
        AvaloniaProperty.Register<PathBreadcrumb, string>(
            nameof(Path), defaultValue: string.Empty, defaultBindingMode: BindingMode.OneWay);
    public string Path { get; set; }   // 宿主→控件单向；控件从不回写，跳转只发 NavigateRequested

    public static readonly StyledProperty<BreadcrumbPathKind> PathKindProperty =
        AvaloniaProperty.Register<PathBreadcrumb, BreadcrumbPathKind>(nameof(PathKind));
    public BreadcrumbPathKind PathKind { get; set; }   // 路径解析分支（Virtual=/ 分段，Disk=\ 分段）

    public static readonly StyledProperty<AutoCompleteFilterMode> FilterModeProperty =
        AvaloniaProperty.Register<PathBreadcrumb, AutoCompleteFilterMode>(
            nameof(FilterMode), AutoCompleteFilterMode.StartsWith);
    public AutoCompleteFilterMode FilterMode { get; set; }  // 编辑态过滤模式；控件内部固定 MinimumPrefixLength=0

    public Func<string?, IEnumerable<string>>? EditCompletionProvider  // 编辑态补全源（文本响应式，宿主 code-behind 注入）
    public int MaxSegments { get; set; } = 6           // 折叠阈值
    public string? ArchiveRootTooltipKey  // 虚拟根段 ToolTip 的本地化 key（主窗口注入 "Breadcrumb_ArchiveRoot"）
    public string? EditPlaceholderKey     // 编辑态 + 磁盘空路径占位文案 key（"Breadcrumb_EditPlaceholder"）
    public event EventHandler<string>? NavigateRequested;      // 段点击 / 编辑态 Enter 提交
    public event EventHandler<string>? EnumerationRequested;   // 预留：分隔符同级目录下拉（第一版不接）
    public void EnterEditMode()           // 公开进入编辑态（宿主窗口级 Ctrl+L 接线调用；控件内 KeyDown 也响应）
    public void CloseEdit()               // 公开退出编辑态（宿主 Popup 打开前调用，防失焦竞争）
}
```

- 控件**不直接导航**：所有跳转动作 → `NavigateRequested(路径)` → 宿主决定（保持现有导航为单一事实来源）
- 编辑态 Enter 提交的原始文本原样上抛，归一化（`CoerceToDirectory`）由宿主做（空串由宿主守卫，`QuickPathPicker` 现有 Enter 逻辑同样跳过空串）
- **本地化 key 而非文案**：`ArchiveRootTooltipKey` / `EditPlaceholderKey` 内部经 `LocalizationManager.T(key)` 解析，并订阅 `LocalizationManager.CultureChanged`（`Services/LocalizationManager.cs:19`）刷新；控件 `DetachedFromVisualTree` 时退订，避免静态事件根引用泄漏（参考 `ProgressWindow.axaml.cs:941` 的教训）
- `Path` 变化（override `OnPropertyChanged`，同 `QuickPathPicker.axaml.cs:72-84` 现有模式，含相等性判断）→ 刷新段集合 + 强制回浏览态

### 内部结构（单行 Grid，浏览/编辑双态互斥）

```xml
<Grid RowDefinitions="Auto" ClipToBounds="True">
  <!-- 点击承接层：全宽透明 Button，专收「末尾空白区」与「磁盘空路径整行」点击（ItemsControl 只命中段按钮） -->
  <Button x:Name="BlankClickTarget" Background="Transparent" />
  <!-- 浏览态：段列表（超长裁剪，完整路径靠各段 ToolTip 保底） -->
  <ItemsControl x:Name="SegmentsHost" IsVisible="{Binding ...}">
    <!-- 每项：StackPanel{段按钮, 分隔符按钮}；末段无分隔符 -->
  </ItemsControl>
  <!-- 浏览态空位：PathKind=Disk 且 Path 为空 → 显示 EditPlaceholderKey 文案 -->
  <TextBlock x:Name="EmptyHint" IsVisible="False" Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
  <!-- 编辑态：AutoCompleteBox（MinimumPrefixLength 固定 0）覆盖同一区域 -->
  <AutoCompleteBox x:Name="EditBox" IsVisible="False" MinimumPrefixLength="0" />
</Grid>
```

- **点击承接**：`ItemsControl` 的命中区只覆盖已渲染的段，末尾空白必须由底层透明 `Button` 承接（否则「点末尾空白进编辑态」在长路径/空路径下失效）；段按钮自身 `Click` 需 `e.Handled` 语义上优先于底层按钮（Avalonia 命中测试按 Z 序，段按钮在上层即可）
- **超长段/窄窗口溢出**：不做宽度测量（设计决定 2），靠 `ClipToBounds=True` 裁剪 + 每段 `ToolTip` 显示完整路径保底；不引入横向 `ScrollViewer`（会与「点空白进编辑」争手势）
- 段项模板：段按钮（`Button`，内容 = 段名，ToolTip = 段完整路径，末段加粗/强调色）+ 分隔符按钮（`›`，第一版不可点仅展示，预留 `EnumerationRequested` 钩子）
- 虚拟根段：段按钮内容 = 📦 `PathIcon`（`Data="{StaticResource IconArchive}"`，`AppIcons.axaml:9` 已存在），点击 → `NavigateRequested("")`
- 尺寸：不写死高度，由宿主设置（主窗口 `Height="{DynamicResource ControlHeightSm}"` 对齐现状 `MainWindow.axaml:1046`；QuickPathPicker 用 `MinHeight="{DynamicResource ControlHeightMd}"` 对齐现有 Row 0）
- 主题（规则 4/5）：`ThemeSurfaceBgBrush`/`ThemeBorderBrush`/`ThemeTextPrimaryBrush`/`ThemeTextSecondaryBrush`/`ThemeButtonBgBrush`/`ThemeButtonHoverBrush`；间距 `SpacingXxxThk`，圆角 `BorderRadius`；段按钮 `Padding=SpacingXsThk`
- `…` 折叠段：`Button` 内容 `…`，ToolTip = 被折叠首段的完整路径；**点击忽略**（仅 ToolTip，不引入歧义导航）

### 双态状态机

| 当前态 | 事件 | 下一态 | 动作 |
|---|---|---|---|
| Browsing | 段点击（非末段） | Browsing | `NavigateRequested(段路径)`；宿主导航后 `Path` 变化 → 刷新段 |
| Browsing（Disk 且 `Path` 为空） | 点击整行（`BlankClickTarget` / `EmptyHint`） | **Editing** | `EditBox.Text = ""`；显示 EditBox、隐藏 SegmentsHost + EmptyHint；`Focus()` |
| Browsing | 末尾空白区点击（`BlankClickTarget`） | **Editing** | `EditBox.Text = Path`；显示 EditBox 隐藏 SegmentsHost；`EditBox.SelectAll()` + `Focus()` |
| Browsing | 宿主窗口级 `Ctrl+L` → `EnterEditMode()`（控件内 KeyDown 兜底） | Editing | 同上 |
| Browsing | 外部 `Path` 变化 | Browsing | 刷新段集合 |
| Editing | `Enter` | Browsing | `NavigateRequested(EditBox.Text)`；还原浏览态（导航成功后 Path 由宿主更新） |
| Editing | `Esc` | Browsing | 还原浏览态（不导航，Path 不变） |
| Editing | 失焦（`LostFocus`） | Browsing | 还原浏览态（不导航） |
| Editing | 外部 `Path` 变化 | Browsing | 刷新段集合 |

**边界注意**：
- 编辑态失焦 vs 宿主 Popup 打开（QuickPathPicker 收藏/历史弹窗）：Popup 打开前宿主先强制 `CloseEdit()`（暴露公开方法），避免失焦还原与 Popup 焦点竞争
- 编辑态 Enter 导航失败（非法路径）：宿主按现有行为处理（`NavigateToFolderPath` 内部 `_allRawItems == null` 静默返回；磁盘宿主 `NavigateTo` 校验 `Directory.Exists` 失败静默返回）→ 控件仍回浏览态显示原 Path，行为与现状一致
- 编辑态 Enter 文本为空（trim 后空）：**不上抛** `NavigateRequested`（对齐 `QuickPathPicker.axaml.cs:147` 现有空串守卫），直接回浏览态
- `Path` 为空：Virtual → 单段 📦；Disk → 空浏览态（`EmptyHint` 显示 `EditPlaceholderKey` 文案，整行可点进编辑）

### 路径解析规则

**Virtual**（`/` 分隔，空段跳过）：
```
""              → [📦]
"docs/images"   → [📦, docs, images]
```
段路径拼接：`""` → `docs` → `docs/images`（`TrimEnd('/')` + 前缀拼接，无尾斜杠）。

**Disk**（`\` 分隔）：
```
""                              → []                               （空浏览态：EmptyHint，整行可点进编辑）
"C:\Users\Admin\Downloads"      → [C:, Users, Admin, Downloads]   （C: 为根，不可点击）
"\\server\share\docs\readme"    → [\\server\share, docs, readme]  （UNC：server+share 为整体根，不可点击）
"relative\path"                 → [relative, path]                （非绝对路径退化显示，段点击上抛原样）
```
- 根段（盘符 / UNC 根）不触发 `NavigateRequested`（点击忽略）
- 段路径拼接用 `Path.Combine` 语义（`TrimEnd('\\')` 前缀拼接）

### 折叠逻辑（段数阈值）

```
段数 ≤ MaxSegments → 全显示
段数 >  MaxSegments → [首段, …, 最后 (MaxSegments-2) 段]
```
`…` 段 ToolTip = 被折叠首段的完整路径。

## 三处接入

### 1. 主窗口（虚拟路径）

- `MainWindow.axaml:1046-1055`：Border 内 `AutoCompleteBox` → `<controls:PathBreadcrumb x:Name="PathBreadcrumb" PathKind="Virtual" Height="{DynamicResource ControlHeightSm}" Path="{Binding CurrentFolder, Mode=OneWay}" FilterMode="StartsWith" />`（`FilterMode` 保持现状 StartsWith 不变；`Height` 对齐现状 `AutoCompleteBox Height=ControlHeightSm`）
- 外层 Border（`:1043-1045`，`Padding="6,3"`、`Background=ThemeHeaderBgBrush`）**原样保留**；面包屑自身不加外层边框/背景，只给段按钮上 `ThemeSurfaceBgBrush` / 悬浮 `ThemeButtonHoverBrush`，避免双框双底
- `MainWindow.axaml.cs:1549-1560`：删除 `AddressBar_KeyDown`；`NavigateRequested` 处理器 → `vm.NavigateToFolderPath(e)`；`PathBreadcrumb.EditCompletionProvider = _ => vm.FolderPaths`（内存集合，无需 FS 枚举，code-behind 赋值而非 XAML 绑定 Func）；`ArchiveRootTooltipKey = "Breadcrumb_ArchiveRoot"`、`EditPlaceholderKey = "Breadcrumb_EditPlaceholder"`
- **窗口级 `Ctrl+L`**：`MainWindow` KeyDown（焦点在列表/树时）→ `PathBreadcrumb.EnterEditMode()`，实现「任意位置直达地址栏」；已确认全仓无 `Key.L` / `Gesture="Ctrl+L"` 既有绑定，无冲突
- `CurrentFolder` 为 `[ObservableProperty]`，**OneWay**（宿主→控件）即可：外部导航（树点击/前进后退）改 `CurrentFolder` → `OnPropertyChanged` → 控件刷新段；控件只经 `NavigateRequested` 反向请求
- **同级目录枚举（未来增强预留）**：`EnumerationRequested(path)` → 从 `FolderPaths` 前缀过滤：`p.StartsWith(path + "/") && p.IndexOf('/', path.Length + 1) < 0`（直接子目录，O(n)，数据已在内存）

### 2. QuickPathPicker（磁盘）

- `QuickPathPicker.axaml:11-20`：Row 0 `AutoCompleteBox` → `<controls:PathBreadcrumb x:Name="Breadcrumb" PathKind="Disk" Path="{Binding #RootControl.Path, Mode=OneWay}" FilterMode="Contains" MinHeight="{DynamicResource ControlHeightMd}" />`
  - `#RootControl` 已存在（`QuickPathPicker.axaml:5`）；`QuickPathPicker.Path` 是 `StyledProperty`（`.axaml.cs:21`，默认 `TwoWay`），面包屑侧显式 `OneWay` 即可显示、由宿主写入
  - **`PathBreadcrumb` 无 `MinimumPrefixLength` 属性**（编辑态内部固定 `0`），不要照抄旧 XAML 的该属性
- `QuickPathPicker.axaml.cs`：
  - 删除 `PathInput` 相关：`InitAutoComplete()`（`:96-133`）、`PathInput_TextChanged`（`:135-140`）、`PathInput_KeyDown`（`:142-155`）、`OnPropertyChanged` 中的 `PathInput.Text` 同步（`:72-84`）
  - 现有建议逻辑（历史匹配 + 文件系统枚举，位于 `InitAutoComplete` 内的 `TextChanged` 闭包 `:103-132`）改写为文本响应式 `Func<string?, IEnumerable<string>>`（参数 = 编辑框文本），赋给 `EditCompletionProvider`
  - `NavigateRequested` 处理器：空串守卫 → `Path = CoerceToDirectory(e)` + `SyncQuickPathControl()` + `PathHistoryManager.Record(Path)`（对齐现有 Enter 行为 `:142-155`）
  - `EditPlaceholderKey = "Breadcrumb_EditPlaceholder"`（替代原 `QuickPath_SelectFolder`）
  - 收藏/历史/窗口 Popup 打开前调用 `Breadcrumb.CloseEdit()`
  - 窗口级 `Ctrl+L`（宿主对话框 KeyDown）→ `EnterEditMode()`；QuickPathPicker 控件自身 KeyDown 兜底
  - Row 1 快捷按钮行不动
- ⚠ **行为变化（需回归确认）**：现 `PathInput_TextChanged` 会在**每次击键**把文本回写 `Path`，宿主 `OutputDirectory` / `DestinationPath` / `CustomPath` 因此逐字符更新。改造后只在 Enter 提交时更新。实施前需确认下列宿主不依赖「逐字符 Path 联动」，若有依赖则补 `PathChanged` 语义或在编辑态额外上抛：
  - `CompressSettingsWindow`（`OutputDirectory`，可能驱动校验/按钮可用性）
  - `ExtractSettingsWindow`（`DestinationPath`）
  - `SettingsWindow`（`CustomPath`，`DefaultPathHint` 实时提示）
  - `UiTestWindow`（示例页）
- 提示：`OnPropertyChanged` 现有 `PathInput.Text` 同步删除后，`Path` 变化由面包屑控件内部 `OnPropertyChanged` 接管，宿主无需手工同步

### 3. CustomFilePickerDialog（磁盘）

- `CustomFilePickerDialog.axaml:84-92`：列 4 `AutoCompleteBox` → `<controls:PathBreadcrumb x:Name="PathCrumbs" PathKind="Disk" FilterMode="Contains" ... />`（**无 `MinimumPrefixLength`**）；列 0-3（后退/前进/向上）、列 5（收藏）不动；`CurrentPathText` 保留
- `CustomFilePickerDialog.axaml.cs`：
  - `NavigateRequested` 处理器：**严格复制现状分支**（`PathAutoComplete_KeyDown:664-680`，含 `text.Trim()`）——`Directory.Exists(text) → NavigateTo(text)`；否则 `File.Exists(text) → TryConfirmFile(text)`；其余忽略。注意现状**没有** `_mode` 判断，`TryConfirmFile:1146-1152` 也不校验模式（**任意模式**输入已存在文件都会确认并关闭对话框）；**禁止**额外加 OpenFile/SaveFile 模式门，否则等于改动现有行为
  - `PathAutoComplete.Text = dir` 直接赋值实际只有 **3 处**：`NavigateTo:697`、`Back_Click:714`、`Forward_Click:729`（`Up_Click:739` 调 `NavigateTo` 间接覆盖，无需单独处理）——统一替换为 `PathCrumbs.Path = dir`
  - 补全源注入：现有 `InitPathAutoComplete()`（`:624-662`，内部已是 `PathAutoComplete.TextChanged` 响应式 + 历史 + 文件系统枚举）抽成 `Func<string?, IEnumerable<string>>` 传入 `EditCompletionProvider`；`EditPlaceholderKey = "Breadcrumb_EditPlaceholder"`（替代原 `Picker_AddressPlaceholder`）
  - 窗口级 `Ctrl+L`（对话框 KeyDown）→ `EnterEditMode()`
- 注意：`PathAutoComplete.TextChanged` 上还有一处补全刷新订阅（`:632-661`），随 `InitPathAutoComplete` 一起移除，避免与控件内部编辑态文本变化重复触发

## 实施步骤

1. **控件**：`Controls/PathBreadcrumb.axaml(.cs)`（双态结构 + 状态机 + 路径解析 + 折叠）
2. **主窗口接入**：替换 AddressBar + `NavigateRequested` 接线 + 删除 `AddressBar_KeyDown` + 注入 `ArchiveRootTooltipKey` / `EditPlaceholderKey`（两个 key 见步骤 5）
3. **QuickPathPicker 接入**：替换 Row 0 + 补全逻辑提取 + Popup 前 `CloseEdit()` + 清理旧处理器（`InitAutoComplete` / `PathInput_TextChanged` / `PathInput_KeyDown` / `OnPropertyChanged` 文本同步）
4. **CustomFilePickerDialog 接入**：替换列 4 + `NavigateRequested` → `NavigateTo`/`TryConfirmFile` + 3 处 `PathAutoComplete.Text = dir` 改 `PathCrumbs.Path`（`Up_Click` 经 `NavigateTo` 间接覆盖）
5. **本地化**（规则 13）：新增 `Breadcrumb_ArchiveRoot`（虚拟根段 ToolTip）+ `Breadcrumb_EditPlaceholder`（编辑态/磁盘空路径占位），**三语**全部写入 `strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`（UTF-8 无 BOM + CRLF + 2 空格缩进，插在文件头 `{` 之后）；随后**三语同步删除**旧占位 key `Nav_AddressBar`（"路径..."）/ `Picker_AddressPlaceholder` / `QuickPath_SelectFolder`；`MainWindowViewModel.cs:236` keys 数组移除 `Nav_AddressBar`（两个新 key 走控件内 `LocalizationManager.T()`，不进 XAML 绑定数组）；改完跑 `mantiszip_i18n` 校验三语 key 集/占位符/重复 key（现有测试只校验解析后的 key 集，测不出重复 key）
6. **UiTestWindow 补充**：自定义控件页签加入 `PathBreadcrumb` 两态展示（Virtual + Disk 空态示例）。`IconArchive` 已存在于 `Resources/Icons/AppIcons.axaml:9` 且已在 `IconTestViewModel.LoadAllIcons()`（:286-297）注册，**无需新增图标**，规则 8 不触发
7. **测试**：`tests/MantisZip.UI.Avalonia.Tests/` 新增 `PathBreadcrumbTests.cs` 无头测试——段解析（Virtual/Disk/空/UNC/相对路径）、折叠阈值、空路径态。测试用 `[AvaloniaFact]` 可操作视觉树（参考 `HelpDialogRenderTests.cs:20-37` 的 `Show()` + `GetVisualDescendants()` 模式），因此**双态切换与 `NavigateRequested` 上抛也可测**，不必退化成纯函数
8. **验证**：`dotnet build` 0 错误 + `dotnet test` 全绿 + 手动冒烟（三处地址栏 + QuickPathPicker 4 宿主：段点击/编辑态 Enter/ESC/失焦/空磁盘路径/长路径折叠/亮暗主题/三档紧凑度）

## 风险与边界

| 风险 | 缓解 |
|---|---|
| 编辑态失焦与宿主 Popup 焦点竞争 | `CloseEdit()` 公开方法，Popup 打开前调用 |
| `Path` 绑定环（控件写 Path → 宿主响应 → 宿主写回） | `Path` 定为 `StyledProperty` + **OneWay**，控件从不回写；跳转只走 `NavigateRequested`，宿主写入后由 `OnPropertyChanged` 相等性比较防重复刷新 |
| `Path` 写成普通 CLR 属性 → TwoWay 绑定静默失效 | 必须 `AvaloniaProperty.Register`（范式 `QuickPathPicker.axaml.cs:21`）；验收时手动切段验证段集合随之刷新 |
| 空串（虚拟根 / 磁盘未选路径） | Virtual：📦 段 → `NavigateRequested("")` → `NavigateToFolderPath("")`；Disk：空浏览态 `EmptyHint`，整行可点进编辑 |
| 磁盘非绝对路径退化 | 解析规则已定义，段点击上抛原样文本，宿主 `CoerceToDirectory` 兜底 |
| 编辑态失去占位文案 | 新增 `Breadcrumb_EditPlaceholder` 三语 key，经 `EditPlaceholderKey` 注入（含磁盘空路径占位） |
| QuickPathPicker 被 4 宿主复用，删掉 `PathInput_TextChanged` 后失去逐字符 `Path` 回写 | 实施前逐个确认 `CompressSettingsWindow` / `ExtractSettingsWindow` / `SettingsWindow` / `UiTestWindow` 不依赖逐字符联动；验收清单覆盖 4 宿主 |
| 超长段 / 窄窗口横向溢出 | `ClipToBounds=True` 裁剪 + 每段 ToolTip 保底；不做宽度测量、不引入横向 ScrollViewer（与空白区点击手势冲突） |
| 「点末尾空白进编辑」在 ItemsControl 空白处失效 | 底层加全宽透明 `Button`（`BlankClickTarget`）承接命中，段按钮位于上层优先命中 |
| `LocalizationManager.CultureChanged` 静态事件导致控件泄漏 | 控件订阅后在 `DetachedFromVisualTree` 退订（参考 `ProgressWindow.axaml.cs:941` 教训） |
| Avalonia 12 `Padding`+DynamicResource AVLN2000 | 段按钮 Padding 用 `SpacingXxxThk`（Thickness 资源，已有先例）；**禁止**在 StackPanel/Panel 上用 DynamicResource Padding（改 Margin 或 Thickness 资源） |
| 现有 `AddressBar` 命名元素被替换导致 code-behind 引用断裂 | 已核对：`AddressBar` 仅出现在 `MainWindow.axaml:1047/1051/1052` 与 `MainWindow.axaml.cs:1549`，无其他引用；替换后删除 `AddressBar_KeyDown` 即可 |
| 折叠 `…` 段点击行为歧义 | 第一版点击忽略（仅 ToolTip），不引入歧义导航 |

## 验收标准

- [ ] 三处地址栏段点击导航与树/列表/前进后退结果一致（同一导航入口）
- [ ] 编辑态：Enter 导航、ESC/失焦还原、补全可用（文本响应式）、`Ctrl+L`（窗口任意焦点位置）直达
- [ ] **行为不变**：CustomFilePickerDialog 任意模式下地址栏输入已存在文件路径 Enter → `TryConfirmFile` 直接确认（与改造前 `PathAutoComplete_KeyDown` 完全一致，不加模式门）
- [ ] QuickPathPicker 的 4 个宿主（压缩设置 / 解压设置 / 设置窗口 / UiTestWindow）路径选择、Enter 提交、历史记录均正常
- [ ] 磁盘空路径：`QuickPathPicker.Path` 为空时显示占位文案，点击整行可进编辑态
- [ ] 虚拟路径根段 📦 点击回根；磁盘根段（盘符/UNC）不可点
- [ ] 长路径折叠 `…` 正常（点击忽略、ToolTip 显示完整路径）；超长单段被裁剪不溢出
- [ ] 亮暗主题 + Compact/Normal/Loose 三档紧凑度均正常；运行中切换语言后 ToolTip/占位文案同步刷新
- [ ] `dotnet build` 0 错误、Avalonia 测试全绿、`mantiszip_i18n` 三语校验通过
