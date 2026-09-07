# 面包屑地址栏（PathBreadcrumb）改造

> **状态**: 📋 计划中（待实施）
> **范围**: Avalonia 版三处地址栏统一改造（主窗口 / QuickPathPicker / CustomFilePickerDialog）

## TL;DR

将现有三处「AutoCompleteBox 文本框 + Enter 确认」地址栏改造为 Windows 资源管理器式**面包屑导航**（路径分段显示、每段可点击直达、点末尾空白进入编辑态）。新增通用控件 `PathBreadcrumb`，通过 `Path` 双向绑定 + 事件回调接入三个宿主，宿主导航逻辑（`NavigateToFolderPath` / `NavigateTo` / `CoerceToDirectory`）保持单一事实来源不变。

**设计决定**（已确认）：
1. **编辑态进入**：点击面包屑末尾空白区（资源管理器惯例），另支持 `Ctrl+L` 键盘直达
2. **折叠策略**：段数阈值（`MaxSegments = 6`，超过时中间段合并为 `…`），第一版不做宽度测量
3. **分隔符同级目录下拉**（`›` 点击弹同级子目录）：**第一版不做**，仅预留 `EnumerationRequested` 事件，后续增强
4. **虚拟路径根段**：显示 📦 图标段（ToolTip「压缩包根目录」），点击回根（`NavigateToFolderPath("")`）
5. **编辑态保留补全**：编辑态用 `AutoCompleteBox`，补全源由宿主注入**文本响应式 Provider**（`Func<string?, IEnumerable<string>>`，参数 = 编辑框文本；主窗口 = `FolderPaths` 内存集合，磁盘两处 = 各自现有历史+文件系统枚举逻辑改写版）
6. 磁盘路径**不做**「此电脑」虚拟根段（首段 = 盘符 / UNC 根，不可点击）
7. 磁盘路径段点击天然是目录（无归一化需求）；编辑态 Enter 文本由宿主处理——**CustomFilePickerDialog 保留「输入文件路径 → `TryConfirmFile` 确认选中」分支**（OpenFile/SaveFile 模式，现状 `PathAutoComplete_KeyDown` 行为），其余 `CoerceToDirectory`

## 现状盘点

| 位置 | 文件 | 当前实现 | 路径类型 |
|---|---|---|---|
| 主窗口文件列表工具栏 | `Views/MainWindow.axaml:1020-1034` | `AddressBar` AutoCompleteBox，`Text={Binding CurrentFolder}`、`ItemsSource={Binding FolderPaths}`、`FilterMode=StartsWith`，Enter → `AddressBar_KeyDown` → `vm.NavigateToFolderPath(box.Text)` | 压缩包内虚拟路径（`/` 分隔，空串 = 根） |
| QuickPathPicker | `Controls/QuickPathPicker.axaml:11-20` + `.axaml.cs` | Row 0 `PathInput` AutoCompleteBox（`MinHeight=ControlHeightMd`），`TextChanged`/`KeyDown(Enter)` 双向同步 `Path`，`InitAutoComplete()` 提供历史+文件系统补全；Row 1 收藏/历史/窗口/浏览四按钮 | 磁盘路径 |
| CustomFilePickerDialog | `Dialogs/CustomFilePickerDialog.axaml:78-86` + `.axaml.cs` | 地址行列 4 `PathAutoComplete` AutoCompleteBox（`MinHeight=ControlHeightSm`），Enter → `PathAutoComplete_KeyDown` → `NavigateTo`；列 0-3 后退/前进/向上按钮、列 5 收藏按钮保留；Row 1 `CurrentPathText` 显示当前路径 | 磁盘路径 |

关键宿主 API（全部保留，面包屑只替换输入控件）：
- `MainWindowViewModel.NavigateToFolderPath(string path)` — 虚拟路径导航（压栈 + `CurrentFolder=path` + `PopulateEntries` + `UpdatePreviewForFolder` + 目录树同步）
- `MainWindowViewModel.FolderPaths`（`ObservableCollection<string>`）— 全量唯一目录路径（含 `""` 根），已在 `LoadArchiveAsync` 填充，**同时是编辑态补全源与（未来）同级目录枚举源**
- `QuickPathPicker.CoerceToDirectory(string)` — 磁盘路径目录归一化（静态）
- `CustomFilePickerDialog.NavigateTo(string dir)` — 磁盘导航（back/forward 栈 + 历史记录 + QuickPath 同步 + 收藏状态）
- `PathHistoryManager.Record(path)` — 历史记录（两处磁盘宿主在导航时各自调用）

## 控件设计：`PathBreadcrumb`

新文件：`Controls/PathBreadcrumb.axaml` + `.axaml.cs`（自包含控件，无新依赖）。

### API

```csharp
public enum BreadcrumbPathKind { Virtual, Disk }

public class PathBreadcrumb : UserControl
{
    public string Path                    // TwoWay 绑定；外部导航（树/列表/前进后退）驱动刷新段
    public BreadcrumbPathKind PathKind    // 路径解析分支（Virtual=/ 分段，Disk=\ 分段）
    public Func<string?, IEnumerable<string>>? EditCompletionProvider  // 编辑态补全源（文本响应式，宿主注入）
    public AutoCompleteFilterMode FilterMode { get; set; } = AutoCompleteFilterMode.StartsWith  // 编辑态过滤模式
    public int MaxSegments { get; set; } = 6           // 折叠阈值
    public event EventHandler<string>? NavigateRequested;   // 段点击 / 编辑态 Enter 提交
    public string? ArchiveRootTooltip     // 虚拟根段 ToolTip（主窗口注入本地化文案）
    public void EnterEditMode()           // 公开进入编辑态（宿主窗口级 Ctrl+L 接线调用；控件内 KeyDown 也响应）
    public void CloseEdit()               // 公开退出编辑态（宿主 Popup 打开前调用，防失焦竞争）
}
```

- 控件**不直接导航**：所有跳转动作 → `NavigateRequested(路径)` → 宿主决定（保持现有导航为单一事实来源）
- 编辑态 Enter 提交的原始文本原样上抛，归一化（`CoerceToDirectory`）由宿主做
- `Path` 属性变化（`OnPropertyChanged`，同 QuickPathPicker 现有模式）→ 刷新段集合 + 强制回浏览态

### 内部结构（单行 Grid，浏览/编辑双态互斥）

```xml
<Grid RowDefinitions="Auto">
  <!-- 浏览态：段列表 -->
  <ItemsControl x:Name="SegmentsHost" IsVisible="{Binding ...}">
    <!-- 每项：StackPanel{段按钮, 分隔符按钮}；末段无分隔符 -->
  </ItemsControl>
  <!-- 编辑态：AutoCompleteBox 覆盖同一区域 -->
  <AutoCompleteBox x:Name="EditBox" IsVisible="False" />
</Grid>
```

- 段项模板：段按钮（`Button`，内容 = 段名，ToolTip = 段完整路径，末段加粗/强调色）+ 分隔符按钮（`›`，第一版不可点仅展示，预留 `EnumerationRequested` 钩子）
- 虚拟根段：段按钮内容 = 📦 `PathIcon`（`IconArchive` 之类），点击 → `NavigateRequested("")`
- 尺寸：不写死高度，宿主容器控制（主窗口 `ControlHeightSm`、QuickPathPicker 保持 `ControlHeightMd` 对齐现有 Row 0）
- 主题（规则 4/5）：`ThemeSurfaceBgBrush`/`ThemeBorderBrush`/`ThemeTextPrimaryBrush`/`ThemeTextSecondaryBrush`/`ThemeButtonBgBrush`/`ThemeButtonHoverBrush`；间距 `SpacingXxxThk`，圆角 `BorderRadius`；段按钮 `Padding=SpacingXsThk`
- `…` 折叠段：`Button` 内容 `…`，ToolTip = 被折叠首段的完整路径；**点击忽略**（仅 ToolTip，不引入歧义导航）

### 双态状态机

| 当前态 | 事件 | 下一态 | 动作 |
|---|---|---|---|
| Browsing | 段点击（非末段） | Browsing | `NavigateRequested(段路径)`；宿主导航后 `Path` 变化 → 刷新段 |
| Browsing | 末尾空白区点击 | **Editing** | `EditBox.Text = Path`；显示 EditBox 隐藏 SegmentsHost；`EditBox.SelectAll()` + `Focus()` |
| Browsing | 宿主窗口级 `Ctrl+L` → `EnterEditMode()`（控件内 KeyDown 兜底） | Editing | 同上 |
| Browsing | 外部 `Path` 变化 | Browsing | 刷新段集合 |
| Editing | `Enter` | Browsing | `NavigateRequested(EditBox.Text)`；还原浏览态（导航成功后 Path 由宿主更新） |
| Editing | `Esc` | Browsing | 还原浏览态（不导航，Path 不变） |
| Editing | 失焦（`LostFocus`） | Browsing | 还原浏览态（不导航） |
| Editing | 外部 `Path` 变化 | Browsing | 刷新段集合 |

**边界注意**：
- 编辑态失焦 vs 宿主 Popup 打开（QuickPathPicker 收藏/历史弹窗）：Popup 打开前宿主先强制 `CloseEdit()`（暴露公开方法），避免失焦还原与 Popup 焦点竞争
- 编辑态 Enter 导航失败（非法路径）：宿主按现有行为处理（`NavigateToFolderPath` 内部 `_allRawItems == null` 静默返回；磁盘宿主 `NavigateTo` 校验 `Directory.Exists` 失败静默返回）→ 控件仍回浏览态显示原 Path，行为与现状一致
- `Path` 为空（虚拟根）：单段 📦

### 路径解析规则

**Virtual**（`/` 分隔，空段跳过）：
```
""              → [📦]
"docs/images"   → [📦, docs, images]
```
段路径拼接：`""` → `docs` → `docs/images`（`TrimEnd('/')` + 前缀拼接，无尾斜杠）。

**Disk**（`\` 分隔）：
```
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

- `MainWindow.axaml:1020-1034`：Border 内 `AutoCompleteBox` → `<controls:PathBreadcrumb x:Name="PathBreadcrumb" PathKind="Virtual" Path="{Binding CurrentFolder}" FilterMode="StartsWith" />`（`FilterMode` 保持现状 StartsWith 不变）
- `MainWindow.axaml.cs`：删除 `AddressBar_KeyDown`；`NavigateRequested` 处理器 → `vm.NavigateToFolderPath(e)`；`PathBreadcrumb.EditCompletionProvider = _ => vm.FolderPaths`（内存集合，无需 FS 枚举，code-behind 赋值而非 XAML 绑定 Func）；`ArchiveRootTooltip` 注入 `LocalizationManager.T("Breadcrumb_ArchiveRoot")`
- **窗口级 `Ctrl+L`**：`MainWindow` KeyDown（焦点在列表/树时）→ `PathBreadcrumb.EnterEditMode()`，实现「任意位置直达地址栏」
- `CurrentFolder` 为 `[ObservableProperty]`，TwoWay 绑定天然成立；外部导航（树点击/前进后退）经 `OnPropertyChanged` → 控件刷新段
- **同级目录枚举（未来增强预留）**：`EnumerationRequested(path)` → 从 `FolderPaths` 前缀过滤：`p.StartsWith(path + "/") && p.IndexOf('/', path.Length + 1) < 0`（直接子目录，O(n)，数据已在内存）

### 2. QuickPathPicker（磁盘）

- `QuickPathPicker.axaml:11-20`：Row 0 `AutoCompleteBox` → `<controls:PathBreadcrumb PathKind="Disk" Path="{Binding #RootControl.Path, Mode=TwoWay}" FilterMode="Contains" MinimumPrefixLength="0" />`（`Path` 保持宿主 `QuickPathPicker.Path` 依赖属性语义，`FilterMode` 保持现状 Contains）
- `QuickPathPicker.axaml.cs`：
  - 删除 `PathInput` 相关：`InitAutoComplete()`、`PathInput_TextChanged`、`PathInput_KeyDown`、`OnPropertyChanged` 中的 `PathInput.Text` 同步
  - 现有建议逻辑（历史匹配 + 文件系统枚举，:108-131）改写为文本响应式 `Func<string?, IEnumerable<string>>`（参数 = 编辑框文本），赋给 `EditCompletionProvider`
  - `NavigateRequested` 处理器：`Path = CoerceToDirectory(e)` + `SyncQuickPathControl()` + `PathHistoryManager.Record(Path)`（对齐现有 Enter 行为）；再保留 `PathInput.Text` 反向同步逻辑的等价物（`OnPropertyChanged` → 控件自动刷新段，宿主无需手工）
  - 收藏/历史/窗口 Popup 打开前调用 `Breadcrumb.CloseEdit()`
  - 窗口级 `Ctrl+L`（宿主对话框 KeyDown）→ `EnterEditMode()`；QuickPathPicker 控件自身 KeyDown 兜底
  - Row 1 快捷按钮行不动
- 提示：`OnPropertyChanged` 现有 `PathInput.Text` 同步删除后，`Path` 变化由面包屑控件内部 `OnPropertyChanged` 接管

### 3. CustomFilePickerDialog（磁盘）

- `CustomFilePickerDialog.axaml:78-86`：列 4 `AutoCompleteBox` → `<controls:PathBreadcrumb PathKind="Disk" FilterMode="Contains" MinimumPrefixLength="0" ... />`；列 0-3（后退/前进/向上）、列 5（收藏）不动；`CurrentPathText` 保留
- `CustomFilePickerDialog.axaml.cs`：
  - `NavigateRequested` 处理器：**先保留文件确认分支**——`File.Exists(text) && _mode is OpenFile or SaveFile → TryConfirmFile(text)`（现状 `PathAutoComplete_KeyDown` 行为，OpenFile/SaveFile 模式地址栏输文件路径直接确认）；否则 `NavigateTo(CoerceToDirectory(e))`（复用现有私有方法，back/forward 栈 + 历史 + QuickPath 同步 + 收藏状态全保留）
  - `NavigateTo`/`Back_Click`/`Forward_Click`/`Up_Click` 中 `PathAutoComplete.Text = dir` 同步删除（由面包屑 `Path` 属性承担——对话框需暴露/绑定当前目录到面包屑 `Path`，实施时在 `NavigateTo` 等四处设置面包屑 `Path` 属性）
  - 补全源注入：现有 `PathAutoComplete` 补全逻辑（:431-460，历史+文件系统枚举）改写为 `Func<string?, IEnumerable<string>>` 传入 `EditCompletionProvider`；无则 `_ => 历史建议`
  - 窗口级 `Ctrl+L`（对话框 KeyDown）→ `EnterEditMode()`
- 注意：`NavigateTo` 有四处在 `_currentDir` 变更后同步 `PathAutoComplete.Text`（`NavigateTo`/`Back`/`Forward`/`Up`），统一替换为面包屑 `Path` 赋值

## 实施步骤

1. **控件**：`Controls/PathBreadcrumb.axaml(.cs)`（双态结构 + 状态机 + 路径解析 + 折叠）
2. **主窗口接入**：替换 AddressBar + `NavigateRequested` 接线 + `Breadcrumb_ArchiveRoot` 本地化 key（中/英）+ 删除 `AddressBar_KeyDown`
3. **QuickPathPicker 接入**：替换 Row 0 + 补全逻辑提取 + Popup 前 `CloseEdit()` + 清理旧处理器
4. **CustomFilePickerDialog 接入**：替换列 4 + `NavigateRequested` → `NavigateTo` + 四处 `_currentDir` 同步替换
5. **本地化**：新增 `Breadcrumb_*` key 成对写入 `strings.zh-CN.json` / `strings.en.json`（UTF-8 无 BOM + CRLF + 2 空格缩进），**同时删除孤儿 key** `Nav_AddressBar` / `Picker_AddressPlaceholder` / `QuickPath_SelectFolder`（zh/en + `MainWindowViewModel.cs:236` 硬编码 key 列表同步，参考 PROGRESS.md 2026-08-10 的 `UpdateLocalizedStrings()` 教训）
6. **UiTestWindow 补充**：自定义控件页签加入 `PathBreadcrumb` 两态展示（Virtual 示例），注册 IconTest 图标（若新增 Geometry 图标，规则 8）
7. **测试**：`tests/MantisZip.UI.Avalonia.Tests/` 新增 `PathBreadcrumbTests.cs` 无头测试——Virtual/Disk 段解析、折叠阈值、空路径根段、编辑态状态转换（可测逻辑层；控件事件无法无头触发则测路径解析+折叠纯函数）
8. **验证**：`dotnet build` 0 错误 + `dotnet test` 全绿 + 手动冒烟（三处地址栏：段点击/编辑态 Enter/ESC/失焦/长路径折叠/亮暗主题/三档紧凑度）

## 风险与边界

| 风险 | 缓解 |
|---|---|
| 编辑态失焦与宿主 Popup 焦点竞争 | `CloseEdit()` 公开方法，Popup 打开前调用 |
| `Path` 双向绑定环（控件设 Path → 宿主响应 → 宿主设 Path） | 沿用 QuickPathPicker 现有 `OnPropertyChanged` 相等性比较防环模式 |
| 虚拟路径根（空串）与 TwoWay 绑定 | 根段点击 → `NavigateRequested("")` → `NavigateToFolderPath("")`（现有方法已支持空串 = 根） |
| 磁盘非绝对路径退化 | 解析规则已定义，段点击上抛原样文本，宿主 `CoerceToDirectory` 兜底 |
| Avalonia 12 `Padding`+DynamicResource AVLN2000 | 段按钮 Padding 用 `SpacingXxxThk`（Thickness 资源，已有先例）；**禁止**在 StackPanel/Panel 上用 DynamicResource Padding（改 Margin 或 Thickness 资源） |
| 现有 `AddressBar` 命名元素被替换导致 code-behind 引用断裂 | 搜索 `AddressBar` 全部引用（XAML + cs），一并替换 |
| 折叠 `…` 段点击行为歧义 | 第一版点击忽略（仅 ToolTip），不引入歧义导航 |

## 验收标准

- [ ] 三处地址栏段点击导航与树/列表/前进后退结果一致（同一导航入口）
- [ ] 编辑态：Enter 导航、ESC/失焦还原、补全可用（文本响应式）、`Ctrl+L`（窗口任意焦点位置）直达
- [ ] 对话框 OpenFile/SaveFile 模式地址栏输入文件路径 Enter → 直接确认选中（回归）
- [ ] 虚拟路径根段 📦 点击回根；磁盘根段（盘符/UNC）不可点
- [ ] 长路径折叠 `…` 正常（点击忽略、ToolTip 显示完整路径）
- [ ] 亮暗主题 + Compact/Normal/Loose 三档紧凑度均正常
- [ ] `dotnet build` 0 错误、Avalonia 测试全绿
