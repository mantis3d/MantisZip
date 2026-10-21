# 图标主题系统 — 计划 1：基础设施（IconView + DrawingImage 契约迁移 + 三层字典骨架）

> **状态**: 📋 未开始 | **创建**: 2026-10-15 | **最近复核**: 2026-10-21（二次核对：补 `.cs` 侧漏站、partial 文件拆分、既有图标字典缓存冲突；§3 数字为当日实况） | **优先级**: P2 | **预估工时**: 16-20h
> **来源**: [docs/superpowers/specs/2026-10-15-icon-theme-system-design.md](../../docs/superpowers/specs/2026-10-15-icon-theme-system-design.md)（已批准）
> **拆分**: 本系统拆 3 份串行计划 —— **① 基础设施（本文）** → ② 导入管线 + 单图标覆盖 + 设置 UI → ③ IconGen + C/D/E/F/G 内容生产。
> **前置**: 无。**后续**: 计划 2 依赖本文交付的 `IconView` / `SourceKey` / 三层字典契约。

## 1. 目标与交付标准

把应用内图标契约从「`PathIcon` + `Geometry` 资源」升级为「`IconView` + `DrawingImage` 资源」，并搭好三层资源字典骨架（默认 A / 当前主题 / 覆盖）。**本计划不引入任何设置 UI、不做导入、不生产 C-G 套内容。**

**交付标准（DoD）**：

1. 全仓不再有 `<PathIcon Data="{StaticResource Icon…}` 形态（Avalonia 自带控件模板内的 PathIcon 不属于我方代码，不算）。
2. 运行 `dotnet run --project tools/IconGen` 可确定性生成 `Icons.A.axaml`；测试强制其**键集**与**每键 Geometry 字符串**和 `AppIcons.axaml` 完全同步（比资源条目内容，不比文件字节）。
3. 新增 `IconView` 控件，行为与 `PathIcon` 等价（尺寸/均匀缩放/`Foreground` 上色），并额外支持 `SourceKey` 键订阅刷新。
4. `ApplyTheme()` 装配三层图标字典，`SelectedIconTheme`（默认 `"A"`）持久化于 `AppSettings`。
5. **A 套零视觉差异**：迁移后全 UI 与迁移前像素级一致（浅色 + 深色 + 选中态）。
6. 构建 0 错误；`MantisZip.Tests` + `MantisZip.UI.Avalonia.Tests` 全绿。

## 2. 对规格的两处实施偏差（实施阶段可定项，已在计划头显式标注）

规格 §12 声明「实施计划阶段可再定的细节，不需回访设计」。以下两项属该类细节，但与原文字面略有出入，**记录理由供评审**：

| # | 规格原文 | 实施采用 | 理由 |
|---|----------|----------|------|
| **D-A** | §4.4：`GeometryResourceConverter` **重命名** `IconResourceConverter`（键→DrawingImage） | 删除 `GeometryResourceConverter`，改由 `IconView.SourceKey`（内部 `GetResourceObservable` 订阅）承担键→图解析 | converter 输出是一次性求值；字典②层被替换后 binding 源（VM 属性/附加属性）不变 → **不会重算**，违反 §4.2「切主题自动全 UI 刷新」。`SourceKey` 订阅资源变化，字典交换时正确重解析。 |
| **D-B** | §4.3：`Foreground: IBrush?` StyledProperty「注册 `inherits:true`」，null 回退 `ThemeTextPrimaryBrush` | `IconView` 注册**自有** `Foreground`（`inherits: true`，默认 `null`），并由 **ControlTheme 设 `Foreground="{DynamicResource TextControlForeground}"`**（与 Fluent `PathIcon` 同 key、同优先级） | **2026-10-21 修订（A 套零差异优先）**：一旦 ControlTheme 提供默认值（Style 优先级 3 压过继承 4），**祖先继承实际不生效**——这与 `PathIcon` 今日行为完全一致（`App.axaml:135-139` 注释「PathIcon 不继承 Foreground」正是此机制）。故**放弃** `AddOwner<IconView>(TextElement.ForegroundProperty)` 复用：它只在「无 ControlTheme 默认值」时才有意义，而那种配置必然产生 A 套视觉差异。`inherits: true` 仅作「无 ControlTheme 时的兜底」，渲染层仍保留 `Foreground ?? TextControlForeground` 防御。 |

> 关键框架事实（2026-10-15 源码核实，作为上表依据）：
> - `TextElement.ForegroundProperty = AvaloniaProperty.RegisterAttached<TextElement, TextElement, IBrush?>(nameof(Foreground), Brushes.Black, inherits: true)`（`src/Avalonia.Controls/Documents/TextElement.cs:68`）。
> - `StyledProperty<TValue>.AddOwner<TOwner>(metadata)` 内部 `AvaloniaPropertyRegistry.Register(owner, this)` + `OverrideMetadata<TOwner>(metadata)`，**返回 `this`（同一实例）**（`src/Avalonia.Base/StyledProperty.cs:59`）。故 `TemplatedControl/TextBlock/ContentPresenter.ForegroundProperty` 与 `TextElement.ForegroundProperty` 引用相同。
> - 值优先级（`BindingPriority`，数值越小优先级越高）：`Animation=-1 < LocalValue=0 < StyleTrigger=1 < Template=2 < Style=3 < Inherited=4 < Unset=int.MaxValue`（`src/Avalonia.Base/Data/BindingPriority.cs`）。即 **Style/ControlTheme setter（≤3）会压过继承值（4）** —— 这是 PathIcon「显式选择器才上色」与本案「`ToggleButton:checked IconView` 选择器仍生效」的机制根源。
> - `ResourceNodeExtensions.GetResourceObservable(this IResourceHost, object key, Func<object?,object?>? converter = null)`（`src/Avalonia.Base/Controls/ResourceNodeExtensions.cs:104`）。
> - 当前 `PathIcon` 的默认 `Foreground` **不是** `Brushes.Black`，而是 Fluent ControlTheme 的 `{DynamicResource TextControlForeground}`（`Avalonia.Themes.Fluent` `Controls/PathIcon.xaml`，本项目 Avalonia 12.1.2 已核实）。本项目 `ThemeLight/Dark.axaml` 里 `TextControlForeground` 与 `ThemeTextPrimaryBrush` 同源（都取 `ThemeTextPrimary`），故二者视觉等价；**IconView 的 ControlTheme 默认值统一用 `TextControlForeground`**（见 D-B），与 PathIcon 语义逐字对齐。`IconElementThemeWidth/Height` 存在于 Fluent `Accents/BaseResources.xaml`（值 20/20），T4 的 ControlTheme 引用成立。
> - **ControlTheme setter（Style 级）压过继承（Inherited 级）**，故 ControlTheme 提供默认 `Foreground` 后祖先继承不再生效；而 `Application.Styles` 里的选择器又压过 ControlTheme setter —— 这正是现有 `ToggleButton:checked PathIcon`（`App.axaml:140`）能盖掉 Fluent PathIcon ControlTheme 默认值的原因，IconView 沿用同一机制。
> - 合并字典解析 **倒序遍历，last-wins**（`ResourceDictionary.cs:221`）→ 三层按 ①→②→③ 顺序 Add 即可让③压②、②压①。

## 3. 现状核实（2026-10-21 二次复核，全仓 grep 实测）

> 行号为复核当日快照，执行时以行内**锚点符号**为准（行号可能继续漂移）。

| 项 | 事实 | 锚点（参考行号） |
|----|------|------|
| 图标资源 | `AppIcons.axaml`：**116** 个 `<Geometry x:Key="IconXxx">…</Geometry>`（`IconPascalCase`）；部分 d 串带 fill-rule 前缀（如 `IconEmptyFolder` 以 `F0 ` 开头），IconGen 必须**原样保留**整个 d 串 | `Resources/Icons/AppIcons.axaml` |
| 资源嵌入方式 | `Resources\**` 全部 `AvaloniaResource`（运行时 `avares://` 加载）；`AvaloniaXaml Include **\*.axaml Exclude Resources\**\*.axaml` → `Resources/Icons/*.axaml` **不**走 XamlIl 预编译 | `MantisZip.UI.Avalonia.csproj:51,56-57` |
| PathIcon 总量 | `<PathIcon` 元素共 **215** 个，全部自闭合（`</PathIcon>` 零命中），且**全部显式带 `Width`**（多行者 Width 在续行，已逐一核对）——**无一处依赖默认尺寸 20**。分布见下行 | 全仓 |
| 静态消费面 | `Data="{StaticResource IconXxx}"` 共 **206** 处：MainWindow 97 / **UiTestWindow 26（2026-10-15 版普查遗漏，Debug 测试窗，已带 xmlns:controls）** / PreviewPanel 13 / ProgressWindow 10 / PasswordManagerWindow 8 / CustomFilePickerDialog 8 / ConflictDialog 7 / SettingsWindow 7 / CompressConflictDialog 6 / FavoriteManagerWindow 5 / ResultTreeView 5 / QuickPathPicker 4 / QuickPathControl 4 / MatchedPasswordDialog 4 / ExtractSettingsWindow 1 / PasswordDialog 1 | 全仓 |
| converter 消费 | `GeometryResourceConverter` 真使用 **6** 处：`App.axaml:280`(TabIcon) / `ResultTreeView.axaml:125` / `QuickPathControl.axaml:122` / `ExtractSettingsWindow.axaml:63` / `IconTestWindow.axaml:89` / **`ProgressWindow.axaml:888`（2026-10-15 版普查遗漏，`StatusIconKey` 绑定，本地 key 为 `"GeometryResource"`）** | 全仓 |
| VM 直接绑定 | `MainWindow.axaml:640` `<PathIcon Data="{Binding ToolbarPasswordIcon}" …>`、`MainWindow.axaml:1344` `<PathIcon Data="{Binding PasswordStatusIcon}" …>` | 全仓 |
| 代码后置专属元素 | `ResultTreeView.axaml:201` `x:Name="StateIconPath"`（XAML 无 Data，由 code-behind 赋值） | 全仓 |
| converter 声明 | **6 处，key 名不统一**：5 处 `x:Key="GeometryResourceConverter"`（`App.axaml:15` / `ResultTreeView.axaml:8` / `QuickPathControl.axaml:9` / `ExtractSettingsWindow.axaml:17` / `IconTestWindow.axaml:16`）+ 1 处 `x:Key="GeometryResource"`（`ProgressWindow.axaml:29`）——删 `GeometryResourceConverter.cs` 时须同删，否则 6 个 XAML 编译报错；消费点须按各自 key 名引用，不可统一按 `GeometryResource` 写 | 全仓 |
| BoolToIconConverter | **已零引用**（2026-10-15 版普查「ProgressWindow.axaml:20/140」已过期：ProgressWindow 眼睛切换早已改为 flyout + code-behind `FindResource`）。T5 不再新增 Key 版 converter，直接删除即可 | `Converters/BoolToIconConverter.cs` |
| C# 动态设置 | **`MainWindow.axaml.cs:1580`(`new PathIcon{Data=…}`) + `:1618`(`panel.Children.OfType<PathIcon>().FirstOrDefault()` 提取列头几何)——2026-10-21 复核补漏：T6 把列头 `PathIcon` 迁成 `IconView` 后此模式**永假**，`iconBox.Child=null`，列头右键菜单图标静默消失（编译通过、无门禁覆盖）**；`ProgressWindow.axaml.cs:238`(CancelButtonIcon) / `415`(PauseButtonIcon) / `564`(PauseButtonIcon) / `723-724` + `746-752`(PwdFlyoutEyeIcon，`FindControl<PathIcon>` 得句柄) / `838-849`(**`sender is Button { Content: PathIcon copyIcon }` 模式匹配，改 IconView 时连类型模式一起改**) / `885`,`891`(PauseButtonIcon)；`PasswordManagerWindow.axaml.cs:62-63`；`MatchedPasswordDialog.axaml.cs:87-92`(new PathIcon 对象初始化器 + Foreground)；`ResultTreeView.axaml.cs:246,257`(StateIconPath) | 全仓 |
| VM 图标属性 | `_passwordStatusIcon` 声明在 `MainWindowViewModel.cs:353`、`_toolbarPasswordIcon` 声明在 **`MainWindowViewModel.Password.cs:83`**（partial 拆分，2026-10-21 修正）；赋值点共 **6 处**：`MainWindowViewModel.cs:1056` + `Password.cs:65/70/108/116/124`；`app?.FindResource("IconKey/IconLockClosed/IconLockOpen")` 得 `Geometry` | `MainWindow.axaml:640,1344` 消费 |
| 缺 `xmlns:controls` 的消费文件 | **11 个**：ResultTreeView / PreviewPanel / PasswordDialog / MainWindow / CompressConflictDialog / ConflictDialog / FavoriteManagerWindow / IconTestWindow / MatchedPasswordDialog / PasswordManagerWindow / ProgressWindow（UiTestWindow / 其余 6 个已有） | — |
| 主题装配 | `App.axaml.cs:35` `IconResourcesUri`（**迁移时删除/替换**）；`ApplyTheme()` 实际在 **`App.Theming.cs:47`**（partial 拆分，2026-10-21 修正）：`:63-70` 同主题早退（`_loadedThemeUri`）、`:73-81` 加载主题 + `_cachedIconProvider`（字段见 `App.axaml.cs:52-53`）+ `MergedDictionaries.Clear()` → 加主题 → 加图标、`:86-88` `RequestedThemeVariant`；调用点 `App.axaml.cs:80/89` + `App.Theming.cs:102`（`RefreshTheme`）→ `MainWindowViewModel.cs:1085` / `SettingsWindow.axaml.cs:44` | `App.axaml.cs` + `App.Theming.cs` |
| 静态字典 | `App.axaml:8-13` MergedDictionaries = ThemeLight(`:10`) + AppIcons(`:12`)；`App.axaml:15-16` 全局 converter 声明（2026-10-21 修正行号） | `App.axaml` |
| 选中态样式 | `App.axaml:140` `ToggleButton:checked PathIcon` —— **唯一**针对 PathIcon 的选择器（descendant 组合子，覆盖全部 checked 子状态，含 pointerover/pressed）；`:143` `ToggleButton:checked:pointerover`、`:147` `ToggleButton:checked:pressed` **针对 ToggleButton 自身**（设按钮 `Foreground`/`Background`），与 IconView 无关（2026-10-21 修正：原描述误称三者都是 PathIcon 选择器） | `App.axaml:131-149` |
| 设置 | `AppSettings` 为纯 POCO，外观节，`Load()`/`Save()` | `Models/AppSettings.cs` |
| 测试桩 | `TestAppBuilder`：`UseSkia().UseHeadless(new(){ UseHeadlessDrawing=false })` → 真实像素渲染可行 | `tests/…/TestAppBuilder.cs` |
| 测试图标合并 | 4 处 `EnsureIconResources()` 直接 `AvaloniaXamlLoader.Load(avares://…/Resources/Icons/AppIcons.axaml)` 并并入 `Application.Current.Resources`：`ExtractPreserveFullPathPreviewMatchTests.cs:273`、`PickerOptionsRegionTests.cs:49`、`PickerOptionsRegionLayoutTests.cs:48`、`ResultTreeViewFilterTests.cs:43` | tests |
| 覆盖率契约 | `IconTestViewModel.LoadAllIcons()`（约 100+ 条 `Add(category, semanticName, emoji, resourceKey, status, location, notes)`）——AGENTS 规则 8 强制维护 | `ViewModels/IconTestViewModel.cs` |
| 版本/InternalsVisibleTo | **`tools/` 已存在**（`tools/CorruptProbe`：net10.0 console、不入 sln，IconGen 可直接沿用该先例；另有 `tools/dsh-plugin-mantiszip`）——2026-10-21 修正「无 `tools/` 目录」；csproj `InternalsVisibleTo` = `MantisZip.Tests` + `MantisZip.UI.Avalonia.Tests` | `MantisZip.UI.Avalonia.csproj:17,19` |

> **计数口径（避免歧义）**：215 = 206（StaticResource）+ 6（converter 绑定）+ 2（VM 直接绑定）+ 1（StateIconPath）。`UiTestWindow` 26 处属 Debug 专用窗但**随正式构建编译**，必须迁移（否则 DoD 门禁不过）。

## 4. 方案

### 4.1 资源契约：`Icons.A.axaml`（DrawingImage）

新增 `Resources/Icons/Icons.A.axaml`，每个键为 `DrawingImage`，内部单部件 `GeometryDrawing`（**无 Brush** → 跟随 `Foreground`）：

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <DrawingImage x:Key="IconFolder">
    <DrawingImage.Drawing>
      <GeometryDrawing Geometry="M3.5 6.25V8H8.12868…Z" />
    </DrawingImage.Drawing>
  </DrawingImage>
  <!-- …其余 115 键，顺序与 AppIcons.axaml 一致… -->
</ResourceDictionary>
```

`AppIcons.axaml` **保留入库**，仅作 `IconGen` 的输入与「键集/几何单一事实来源」；运行时**不再**被 merge（见 4.4，避免与 Icons.A 同名键冲突）。

### 4.2 `tools/IconGen`（dev-time C# console，不入 sln）

`tools/IconGen/IconGen.csproj`（`net10.0`，`OutputType=Exe`，独立于 sln）读取 `src/MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml`（`XDocument` 解析 `<Geometry x:Key>`），输出 `Icons.A.axaml`。完全确定性（无时间戳/随机），可重复运行且 git 无 diff。

A 套转换规则：`<Geometry x:Key="K">D</Geometry>` → `<DrawingImage x:Key="K"><DrawingImage.Drawing><GeometryDrawing Geometry="D" /></DrawingImage.Drawing></DrawingImage>`，键顺序保持不变。

### 4.3 `IconView` 控件（`Controls/IconView.cs`）

- `Source: DrawingImage?` —— 消费端静态写法 `Source="{DynamicResource IconXxx}"`（DynamicResource 自带字典交换刷新）。
- `SourceKey: string?` —— 键字符串；非空时**优先**，内部 `this.GetResourceObservable(SourceKey, r => r as DrawingImage)` 订阅，字典②层替换时自动重解析（解决 D-A）。
- `Foreground: IBrush?` —— **自有 StyledProperty**（`AvaloniaProperty.Register<IconView, IBrush?>(nameof(Foreground), defaultValue: null, inherits: true)`）；默认值由 §4.6 ControlTheme 提供（`{DynamicResource TextControlForeground}`，与 Fluent `PathIcon` 同 key），渲染层兜底 `Foreground ?? TextControlForeground`。**不再 `AddOwner` 复用 `TextElement.ForegroundProperty`**（理由见 §2 D-B）。
- **失效通知（必须显式声明；`PathIcon` 由 `StyledProperty` 内置 `AffectsRender`，自绘控件没有）**：`AffectsRender<IconView>(SourceProperty, SourceKeyProperty, ForegroundProperty, StretchProperty)` + `AffectsMeasure<IconView>(SourceProperty, SourceKeyProperty, StretchProperty)`；`SourceKey` 变更时在 `OnPropertyChanged` 内重建资源订阅并 `InvalidateVisual()`。漏声明 → 换主题/换前景色后留旧像素。
- `Stretch: Stretch`（默认 `Uniform`）—— 尺寸对齐 `PathIcon`（均匀缩放、居中、不裁剪）。
- `Render`：展平 `Source.Drawing`（`GeometryDrawing` / `DrawingGroup.Children` 递归），部件 `Brush == null` 用 `Foreground`，否则用部件 Brush；按整体 `Drawing.Bounds` 均匀缩放平移到 `Bounds`。

### 4.4 三层字典 + `ApplyTheme` 装配

`AppSettings` 外观节新增（默认 `"A"`）：

```csharp
/// <summary>图标风格主题：A=现状默认 / C/D/E/F/G=内置套 / 导入包 id。仅影响应用内矢量图标。</summary>
public string SelectedIconTheme { get; set; } = "A";
```

`App.axaml.cs` 新增常量与装配方法；`ApplyTheme()`（**实际位于 `App.Theming.cs:47`**）在既有「加主题字典」之后，按 ①→②→③ 顺序装配。

> ⚠ **必须先改造既有缓存/早退逻辑**（`App.Theming.cs:63-83`）：现有实现「同主题早退」（`_loadedThemeUri == targetUri` 直接 `return`）会**吞掉图标主题切换**——改 `SelectedIconTheme` 时基础主题未变 → 直接 return → 图标层不重建。现有 `_cachedIconProvider`（`App.axaml.cs:52-53`）只缓存单个图标字典，也无法承载三层。
> 1. 新增 `_loadedIconThemeId`（初值 `"A"`），早退条件改为 `_loadedThemeUri == targetUri && _loadedIconThemeId == settings.SelectedIconTheme`。
> 2. 图标层按 id 缓存 `Dictionary<string, IResourceProvider>`（**已确认加入**）：① 层 `Icons.A` 只解析一次，② 层按 id 缓存 —— 避免每次 `ApplyTheme` 重复 `AvaloniaXamlLoader.Load` 解析 ~116 键字典。
> 3. `App.axaml.cs:45-53` 的契约注释（依赖 `MergedDictionaries[1]` 取图标字典）必须同步改写为「[主题, ①A, ②当前, ③覆盖]」；`_cachedIconProvider` 字段由 `_iconThemeProviders` 取代。

```csharp
private const string IconsAUri = "avares://MantisZip.UI.Avalonia/Resources/Icons/Icons.A.axaml";

// 按图标主题 id 缓存已解析字典（"A" / "C"… / 导入包 id）
private readonly Dictionary<string, IResourceProvider> _iconThemeProviders = new(StringComparer.Ordinal);
private string _loadedIconThemeId = "A";

// ApplyTheme() 内（早退判定改为「基础主题 + 图标主题」双条件），字典替换段：
var settings = AppSettings.Load();
var iconThemeId = string.IsNullOrWhiteSpace(settings.SelectedIconTheme) ? "A" : settings.SelectedIconTheme;
var themeProvider = AvaloniaXamlLoader.Load(targetUri) as IResourceProvider;

Resources.MergedDictionaries.Clear();
if (themeProvider != null) Resources.MergedDictionaries.Add(themeProvider);
Resources.MergedDictionaries.Add(GetIconThemeProvider("A"));            // ① 默认 A（永远垫底，缺键兜底 → 永不空白）
if (iconThemeId != "A")
    Resources.MergedDictionaries.Add(GetIconThemeProvider(iconThemeId)); // ② 当前主题层（未知/异常 id 静默回退 A）
AddIconOverrideLayer();                                                 // ③ 覆盖层（计划 1：空实现，但调用位必须保留）
_loadedIconThemeId = iconThemeId;

private IResourceProvider GetIconThemeProvider(string id)
{
    if (_iconThemeProviders.TryGetValue(id, out var cached)) return cached;
    var loaded = AvaloniaXamlLoader.Load(new Uri(ResolveIconThemeUri(id))) as IResourceProvider
                 ?? throw new InvalidOperationException($"图标字典加载失败: {id}");
    _iconThemeProviders[id] = loaded;
    return loaded;
}
private static string ResolveIconThemeUri(string? id) => id switch
{
    // 计划 3 扩充 C…G（仅内置套有 avares URI）；导入包不产生 URI，
    // 由计划 2 在 GetIconThemeProvider 内加「内存 ResourceDictionary」分支。
    _ => IconsAUri, // 未知/异常 → 静默回退 A（§8：启动路径不弹窗）
};
private static void AddIconOverrideLayer() { /* 计划 2 实现 */ }
```

同时：
1. 把 `App.axaml:12` 的静态 merge 由 `AppIcons.axaml` 改为 `Icons.A.axaml`（设计期/首帧需要；运行时由 `ApplyTheme` 重建）。⚠ **启动期解析次数约束见 §4.7 硬约束 1**：静态 merge 与 `ApplyTheme()` 各解析一次 = 多付 33ms，二者必须共用同一 provider 实例或去掉静态 merge。
2. **删除** `App.axaml.cs:35` 的旧常量 `IconResourcesUri`（唯一引用点在 `App.Theming.cs:77` 的 `_cachedIconProvider ??=` 分支，随缓存改造一并移除），避免死代码 / 双常量漂移。
3. 计划 1 仅存在 A 套，`iconThemeId == "A"` 时跳过 ② 层（避免同一 provider 重复 Add）；计划 2/3 引入真实主题后才走三层。
4. **缓存失效归计划 2**：计划 1 只建缓存，不建失效入口。计划 2 新增的 `RefreshIconTheme()` 必须 `_iconThemeProviders.Remove(id)` + 清 `_loadedIconThemeId`，否则第 115 行的双条件早退会直接 return（改包/改覆盖后界面不刷新）。

### 4.5 消费端迁移（机械替换）

| 现状 | 迁移后 |
|------|--------|
| `<PathIcon Data="{StaticResource IconXxx}" …/>` | `<controls:IconView Source="{DynamicResource IconXxx}" …/>`（保留其余属性；`Width`/`Height`/`Foreground`/`Classes` 原样） |
| `Data="{Binding X, Converter={StaticResource GeometryResourceConverter}}"` | `SourceKey="{Binding X}"` |
| `Data="{Binding ToolbarPasswordIcon}"` / `Data="{Binding PasswordStatusIcon}"` | `SourceKey="{Binding …}"`（配合 VM 属性改存键字符串） |
| `Data="{Binding StatusIconKey, Converter={StaticResource GeometryResource}}"`（ProgressWindow:888） | `SourceKey="{Binding StatusIconKey}"`（VM 已经是键字符串，无缝） |
| C# `pathIcon.Data = (Geometry?)FindResource("IconX")` | `iconView.SourceKey = "IconX"` |
| VM `object?` 存 `Geometry` | VM `string?` 存键名（`"IconKey"`/`"IconLockClosed"`/`"IconLockOpen"`），XAML 绑 `SourceKey` |
| `App.axaml:140` `ToggleButton:checked PathIcon`（**唯一** PathIcon 选择器） | 旁增**一条**并列 `<Style Selector="ToggleButton:checked controls:IconView">` + `Foreground=White`。`:143`/`:147` 针对 ToggleButton 自身（非 PathIcon），**无需**扩 IconView 选择器（`:140` 已覆盖全部 checked 子状态）。Avalonia 对单条 Style 内逗号分组支持不明，不依赖；不合并选择器 |

- converter：完成 6 处绑定迁移后，**删除** `Converters/GeometryResourceConverter.cs` 及全部 **6 处声明**（§3 表）；**`Converters/BoolToIconConverter.cs` 当前已零引用，直接删除、不新增 Key 版**（2026-10-15 版计划的 `BoolToIconKeyConverter` 随普查过期一并取消）。
- 11 个缺命名空间的文件补 `xmlns:controls="clr-namespace:MantisZip.UI.Avalonia.Controls"`。
- `IconTestWindow.axaml:89`（Debug 工具）改用 `<controls:IconView SourceKey="{Binding ResourceKey}" …/>`。
- `ProgressWindow.axaml.cs:838-849` 的 `sender is Button { Content: PathIcon copyIcon }` 类型模式与 `:723/746` 的 `FindControl<PathIcon>("PwdFlyoutEyeIcon")` 须同步改 `IconView`（元素标签迁移后 old 模式永假，属编译外静默 bug）。

### 4.6 IconView 默认样式（ControlTheme）

`App.axaml` 资源内新增（供 `IconView` 实例自动套用，尺寸与前景色均与 `PathIcon` 默认一致）：

```xml
<!-- IconView 默认样式：尺寸/前景色与 Fluent PathIcon 逐字对齐（20×20 + TextControlForeground）；局部 Width/Height/Foreground 可覆盖 -->
<ControlTheme x:Key="{x:Type controls:IconView}" TargetType="controls:IconView">
  <Setter Property="Width" Value="{DynamicResource IconElementThemeWidth}" />
  <Setter Property="Height" Value="{DynamicResource IconElementThemeHeight}" />
  <Setter Property="Foreground" Value="{DynamicResource TextControlForeground}" />
</ControlTheme>
```

> `Foreground` setter 是 A 套零视觉差异的关键：它与 Fluent `PathIcon` 的 ControlTheme 默认值同 key，且 `Application.Styles` 的 `ToggleButton:checked controls:IconView` 仍能压过它（与现有 `ToggleButton:checked PathIcon` 压过 Fluent PathIcon ControlTheme 同一机制）。

### 4.7 启动性能约束（2026-10-21 增补，基于 2026-10-21 修订版）

本计划是启动路径上唯一有实质风险的改动（`ApplyTheme()` 启动期执行 + 215 处静态图标替换 + 图标字典重解析）。启动优化二期（`startup-theme-dedup`）已建立「同主题早退 + 图标字典缓存」机制，本计划**不得破坏**它——§4.4 note 1-3 已覆盖大部分（双条件早退 / `_iconThemeProviders` 按 id 缓存 / A 时跳过②层），此处只补**尚未覆盖的启动期解析次数**与**量化门控**。

**实测基线（2026-10-21 无头探针）**

| 测量项 | 结果 |
|---|---|
| `AppIcons.axaml` 解析（116 Geometry） | **32.9ms**（占启动 `App.Initialize` 78ms 的 42%） |
| `ThemeLight.axaml` 解析（对照） | 1.8ms |
| 200× `PathIcon` 实例化 | 33.6ms（冷，含 JIT）；热态 200 个仅 1.2ms → 单实例 ≈0.006ms |
| 200× `Button`（`Control` 近似 `IconView`） | 7.8ms → 控件级实例化开销可忽略 |
| 200× `PathIcon` + `GetResourceObservable` | 1.2ms → `SourceKey` 订阅机制本身很便宜 |

**硬约束（实施时必须满足，违反即打回）**

1. **启动期图标字典解析次数 = 1**（与当前基线持平，**不得变成 2**）：§4.4「同时」第 1 条要求 `App.axaml:12` 静态 merge 改挂 `Icons.A.axaml`，而 `ApplyTheme()` 里的 `GetIconThemeProvider("A")` 会**再 `Load` 一次** → 启动多解析 **33ms**。二选一：
   - (a) **推荐**：`App.axaml` **不再静态 merge 图标字典**——迁移后图标键全为 `DynamicResource`（静态引用已清零，DoD #1），首帧不依赖静态 merge；`ApplyTheme()` 在 `OnFrameworkInitializationCompleted` 内、早于 `MainWindow` 创建 → 首帧不缺资源；
   - (b) `GetIconThemeProvider("A")` 首次命中时**复用** `App.axaml` 静态 merge 进来的同一 provider 实例（而非重新 `Load`）。
2. **切主题/切图标主题不得重复解析**：§4.4 note 2 的 `_iconThemeProviders` 按 id 缓存必须真正命中（不得每次 `ApplyTheme` 重新 `Load`），否则每次换肤 +33ms。
3. **`IconView` 保持轻量**：§4.6 已定 `ControlTheme` 只含 3 个 Setter、**无 `Template` 内容**——不得引入 `TemplatedControl` 模板或每实例可视图；实测 215 个实例的控件级开销 ≤10ms，可接受。
4. **215 处迁移后 XAML 体积增大** → `Win.Xaml` 段（当前 ~330ms）预期 +5~15ms，属可接受区间。

**验收门控（§7 增补）**

```powershell
# 交错 A/B（基线 = 迁移前 HEAD 的 Release 构建），n=10
.\tools\measure-startup.ps1 -ExePath @($baselineExe, $newExe) -Runs 10 -WarmupRuns 1
```

- 门控：`TOTAL` 中位数劣化 ≤ **20ms**（约 1.5%）且 `Win.Xaml` 段劣化 ≤ 30ms → 通过；
- 不达门控：优先排查「图标字典重复解析（约束 1/2）」与「`IconView` 引入模板（约束 3）」两项（占预期风险 90%+），而非放宽门控；
- 迁移是纯 UI 替换，任何启动劣化超门控必须在本计划内解决（不得留待后续计划）。
## 5. 任务分解（TDD；每任务一个 commit）

> 规则：每个任务先写失败测试 → 实现 → 测试转绿 → `dotnet build`/`dotnet test` → conventional commit（规则 10）。

- [ ] **T1 — `tools/IconGen` 生成 `Icons.A.axaml`**
  - 新建 `tools/IconGen/IconGen.csproj`（net10.0 console，不入 sln）+ `Program.cs`：`XDocument` 读 `AppIcons.axaml`，按序输出 `Icons.A.axaml`（`GeometryDrawing` 无 Brush）。注意部分 d 串带 fill-rule 前缀（如 `IconEmptyFolder` 的 `F0 `），**整串原样写入**，不得裁剪/归一化空白。
  - **测试（`MantisZip.Tests` 或 `MantisZip.UI.Avalonia.Tests`，纯逻辑）**：`AppIcons` 键集 == `Icons.A` 键集；每键 Geometry 字符串（原样 trim 后）完全相等。测试不运行 IconGen 进程，直接读取两文件比对。
  - 运行 `dotnet run --project tools/IconGen` 生成并入库 `Icons.A.axaml`。commit。

- [ ] **T2 — 三层字典 + `SelectedIconTheme`（无 UI）**
  - `AppSettings` 加 `SelectedIconTheme`（默认 `"A"`）；`App.axaml.cs` 加 `IconsAUri` + `GetIconThemeProvider`/`ResolveIconThemeUri`/`AddIconOverrideLayer`（空）；**`App.Theming.cs:47` `ApplyTheme()`** 改为「基础主题 + 图标主题」双条件早退，并按 ①→②→③ 装配（见 4.4）；`App.axaml:12` 改挂 `Icons.A.axaml`；同步改写 `App.axaml.cs:45-53` 的 `MergedDictionaries[1]` 契约注释。
  - **测试（Avalonia headless）**：设 `SelectedIconTheme="A"` → `ApplyTheme` 后 `Application.Current.Resources.TryGetResource("IconFolder", null, out var r)` 命中且 `r is DrawingImage`；未知 id（`"ZZZ"`，基础主题不变）→ 再次 `ApplyTheme` 仍命中 A（静默回退），且 `MergedDictionaries` 已按 ①(+②) 重建（验证早退未吞掉图标层切换）。commit。

- [ ] **T3 — `IconView` 控件**
  - `Controls/IconView.cs`：`Source`/`SourceKey`/`Foreground`（**自有 StyledProperty**，`inherits: true`，默认 `null`，默认值由 ControlTheme 提供）/`Stretch`；**显式注册 `AffectsRender` + `AffectsMeasure`**（`Source`/`SourceKey`/`Foreground`/`Stretch`，自绘控件必须手动声明）；`Render` 展平 + 均匀缩放 + 部件 `Brush == null` 时用 `Foreground ?? TextControlForeground`；`OnAttachedToVisualTree` 订阅 `SourceKey`（自定义 `IObserver<T>`，不引 System.Reactive），`OnDetachedFromVisualTree` 释放；`SourceKey` 变更时重建订阅并 `InvalidateVisual()`；向前兼容「attach 前 SourceKey 已赋值」与「detach 后 re-attach」两种时序（attach 时无条件重建订阅）。
  - **测试（headless 真实像素）**：① `UseHeadlessDrawing=false` 下渲染 `DrawingImage`（单 GeometryDrawing 无 Brush）+ `Foreground=Red` → `RenderTargetBitmap` 命中红色像素；② 同图 `Foreground=Blue` → 蓝色；③ 部件带 baked `SolidColorBrush(Green)` → 忽略 Foreground 保持绿；④ `SourceKey` 指向 `Application` 资源 → 渲染非空，替换资源后重渲染像素变化（订阅刷新）。commit。

- [ ] **T4 — ControlTheme + 选中态样式**
  - `App.axaml` 加 `IconView` ControlTheme（4.6：Width/Height + `Foreground="{DynamicResource TextControlForeground}"`）+ **仅 1 条**并列 Style `ToggleButton:checked controls:IconView`（`Foreground=White`，紧随 `:140` 的 PathIcon 条目）。`:143`/`:147` 针对 ToggleButton 自身，**不扩** IconView（见 4.5）。
  - **测试**：`IconView` 未设尺寸时 `DesiredSize` ≈ 20×20（读 `IconElementThemeWidth/Height`，Fluent `BaseResources` 已存在该键）；未设 `Foreground` 时其解析值 == `TextControlForeground` 资源（A 套对齐 PathIcon 的直接证据）。commit。

- [ ] **T5 — 删除已零引用的 `BoolToIconConverter`（普查更新，2026-10-15 版「新增 Key 版 converter」作废）**
  - 普查核实 `BoolToIconConverter` 全仓唯一出现处是自身定义文件（ProgressWindow 眼睛切换早已改为 code-behind `FindResource`）。**不新增** `BoolToIconKeyConverter`；直接删除 `Converters/BoolToIconConverter.cs`。
  - **门禁**：`git grep -n '\bBoolToIconConverter\b' -- src` = 0 命中。commit。

- [ ] **T6 — 静态消费面迁移（206 处 → `IconView`）**
  - 覆盖全部 16 个静态文件（含 **UiTestWindow 26 处**——Debug 专项窗但随正式构建编译）；按文件逐批：标签替换 + `StaticResource`→`DynamicResource` + 补 `xmlns:controls`（11 文件，见 §3）。可用脚本辅助，但**必须逐文件 review**（保留 `Width`/`Height`/`Foreground`/`Classes`/内联属性；全量已显式 `Width`，无默认尺寸依赖）。
  - ResultTreeView / QuickPathControl / ExtractSettingsWindow / IconTestWindow / ProgressWindow 内有多静态、converter、VM 绑定混合站点：本任务只处理 `Data="{StaticResource …}"`，动态站点留给 T7，标记避免双改。
  - **门禁测试/检查**：`git grep -n 'PathIcon Data="{StaticResource Icon' -- 'src/*.axaml'` = 0 命中（同时覆盖 UiTestWindow 处）。commit（可分批多次 commit）。

- [ ] **T7 — converter / VM / C# 动态站点迁移（6 converter 绑定 + 2 VM 绑定 + 12 组 C# 站点）**
  - `App.axaml:280` TabIcon → `<controls:IconView SourceKey="{Binding $parent[TabItem].(controls:TabItemExtensions.TabIcon)}" …/>`（保留 `IsVisible`/`Width`/`Height`/`Foreground`）。
  - 其余 5 处 converter 绑定 → `SourceKey`：`ResultTreeView.axaml:125`、`QuickPathControl.axaml:122`、`ExtractSettingsWindow.axaml:63`、`IconTestWindow.axaml:89`、`ProgressWindow.axaml:888`（`StatusIconKey` 本就是键字符串，直接换属性即可）。
  - 2 处 VM 直接绑定：`MainWindow.axaml:640`/`:1344` `Data="{Binding ToolbarPasswordIcon/PasswordStatusIcon}"` → `SourceKey="{Binding …}"`。
  - C# 站点：`ProgressWindow.axaml.cs:238/415/564/885/891`(x:Name 元素处理函数)、`723-724/746-752`(PwdFlyoutEyeIcon：`FindControl<PathIcon>` → `FindControl<IconView>`)、`838-849`(模式匹配 `Button { Content: PathIcon copyIcon }` → `Content: IconView`)；`PasswordManagerWindow.axaml.cs:62-63`；`MatchedPasswordDialog.axaml.cs:87-92`(new PathIcon 对象初始化器)；`ResultTreeView.axaml.cs:246,257`(StateIconPath)。全部改设 `SourceKey`；元素 `x:Name` 类型改 `IconView`。
  - **`MainWindow.axaml.cs:1579-1623`（2026-10-21 补漏，T6 迁移后必坏且编译不报错）**：`GetColumnHeaderIcon` 的 `panel.Children.OfType<PathIcon>().FirstOrDefault()` 永假 → 列头右键菜单图标消失。改为 `GetColumnHeaderIconSource(DataGridColumn) → DrawingImage?`（`OfType<IconView>().FirstOrDefault()?.Source`），`:1580` 的 `new PathIcon{Data=…}` 改为 `new IconView{Source=…, Width=10, Height=10, Foreground=GetThemeBrush("ThemeTextPrimaryBrush")}`（不再需要 Geometry 提取）。方法改 `internal static` 以便测试。
  - `MainWindowViewModel`：`_passwordStatusIcon`（`MainWindowViewModel.cs:353`）/`_toolbarPasswordIcon`（`MainWindowViewModel.Password.cs:83`）由 `object?`(Geometry) 改 `string?`(键值)，赋值点 `MainWindowViewModel.cs:1056` + `Password.cs:65/70/108/116/124` 同步去 `FindResource`。
  - **测试**：构造 `MatchedPasswordDialog`/`ResultTreeView` 等，无「Unable to find resource」异常；VM 设密码态后 `ToolbarPasswordIcon == "IconLockOpen"`；`GetColumnHeaderIconSource` 对含 `IconView` 的 `StackPanel` 表头返回非空 `DrawingImage`（防列头菜单图标回归）。commit。

- [ ] **T8 — 删除 `GeometryResourceConverter`（含 6 处 XAML 声明）**
  - T7 完成后确认零引用，删除 `Converters/GeometryResourceConverter.cs` + 全部 6 处声明：`App.axaml:15`、`ResultTreeView.axaml:8`、`QuickPathControl.axaml:9`、`ExtractSettingsWindow.axaml:17`、`IconTestWindow.axaml:16`、`ProgressWindow.axaml:29`（key=`"GeometryResource"`）。**漏删任一声明 → XAVL 编译报错**，故声明清单逐项勾销。
  - **门禁**：`git grep -n 'GeometryResourceConverter\|GeometryResource"' -- src` = 0 命中。commit。

- [ ] **T9 — 测试桩图标合并改挂 `Icons.A.axaml`**
  - 4 处 `EnsureIconResources()` 的 URI 由 `AppIcons.axaml` 改为 `Icons.A.axaml`（`ExtractPreserveFullPathPreviewMatchTests.cs:273`、`PickerOptionsRegionTests.cs:49`、`PickerOptionsRegionLayoutTests.cs:48`、`ResultTreeViewFilterTests.cs:43`）。
  - **测试**：`dotnet test tests\MantisZip.UI.Avalonia.Tests` 全绿（原依赖 `StaticResource Icon` 的对话框测试不再抛异常）。commit。

- [ ] **T10 — 文档与验收**
  - `docs/PLAN.md` 校对 P2 区 3 行（本文 + 计划 2/3，含本修订后的数字/工时），格式对齐既有表。
  - `docs/progress-avalonia-detail.md` 以**执行当日日期**追加条目（规则 3）。
  - 手动 QA：浅色/深色/选中态全 UI 截图对照（A 套零视觉差异，重点覆盖 `ToggleButton:checked` 工具栏选中态、**DataGrid 列头右键菜单图标**（`MainWindow.axaml.cs` 补漏点）、以及显式设 `Foreground` 的站点）；构建 0 错误。commit。

## 6. 涉及文件

**新增**
- `tools/IconGen/IconGen.csproj`、`tools/IconGen/Program.cs`
- `src/MantisZip.UI.Avalonia/Resources/Icons/Icons.A.axaml`
- `src/MantisZip.UI.Avalonia/Controls/IconView.cs`
- 测试：IconGen 同步测试、`IconView` 渲染测试、（新）图标字典装配测试

**修改**
- `src/MantisZip.UI.Avalonia/App.axaml`（MergedDictionaries、converter 声明、`IconView` ControlTheme（3 setter）、选中态选择器（**新增 1 条** `ToggleButton:checked controls:IconView`）、TabIcon 模板）
- `src/MantisZip.UI.Avalonia/App.axaml.cs`（`IconsAUri` 新增 + `IconResourcesUri` 删除、`_iconThemeProviders` 缓存、`MergedDictionaries[1]` 契约注释改写、helpers）
- `src/MantisZip.UI.Avalonia/App.Theming.cs`（`ApplyTheme()` 双条件早退 + 三层装配，`_cachedIconProvider` → `_iconThemeProviders`）
- `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`（`SelectedIconTheme`）
- `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`（`_passwordStatusIcon` 类型 + 1 处赋值点）
- `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Password.cs`（`_toolbarPasswordIcon` 类型 + 5 处赋值点）
- 消费面 17 个 `.axaml`（MainWindow 99 / **UiTestWindow 26** / PreviewPanel 13 / ProgressWindow 11 / PasswordManagerWindow 8 / CustomFilePickerDialog 8 / ConflictDialog 7 / ResultTreeView 7 / SettingsWindow 7 / CompressConflictDialog 6 / FavoriteManagerWindow 5 / QuickPathControl 5 / QuickPathPicker 4 / MatchedPasswordDialog 4 / ExtractSettingsWindow 2 / IconTestWindow 1 / PasswordDialog 1 = **214**；再加 `App.axaml:280` TabIcon 1 处 = 215）+ **5 个 `.axaml.cs`**（ProgressWindow / PasswordManagerWindow / MatchedPasswordDialog / ResultTreeView / **MainWindow**）
- `tests/MantisZip.UI.Avalonia.Tests/{ExtractPreserveFullPathPreviewMatchTests, PickerOptionsRegionTests, PickerOptionsRegionLayoutTests, ResultTreeViewFilterTests}.cs`
- `docs/PLAN.md`

**删除**
- `src/MantisZip.UI.Avalonia/Converters/GeometryResourceConverter.cs`（含 6 处 XAML 声明，见 T8）
- `src/MantisZip.UI.Avalonia/Converters/BoolToIconConverter.cs`（已零引用，T5）

## 7. 验证

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test  tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test  tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
# 残留门禁（期望 0）
git grep -n 'PathIcon Data="{StaticResource Icon' -- 'src/*.axaml'
git grep -n -E 'new PathIcon|OfType<PathIcon>|FindControl<PathIcon>|Content: PathIcon' -- 'src/*.cs'
git grep -n 'GeometryResourceConverter' -- 'src/*'
git grep -n '\bBoolToIconConverter\b' -- 'src/*'
```

- `lsp_diagnostics` 无错误。
- 手动：浅色/深色/选中态逐一目检（A 套零差异）；`SelectedIconTheme` 改值重启后仍指向 A（唯一可选项）。
- **启动 A/B 门控**：见 §4.7 —— 迁移前后交错 n=10，TOTAL 中位劣化 ≤ 20ms、Win.Xaml 劣化 ≤ 30ms 方可合入（基线数字见 §4.7 表）。

## 8. 边界 / 非目标

- **不做**：设置 UI（计划 2）、导入/覆盖（计划 2）、C-G 套内容与 IconGen 多套产出（计划 3）。
- **不动**：文件类型系统图标（`IconService`/`IconProvider`）、Shell 右键 .ico、文件关联注册表图标（D2/D6 机制分离）。
- **不动**：`ToolbarButtonIcon` 字符文本（13 处，非资源图标）。
- **不改**：`AppIcons.axaml`（保留为 IconGen 输入与键集事实来源；仅在 `Icons.A.axaml` 与之不同步时由测试报警）。
- **不改**：`AppSettings` 默认值语义（除新增 `SelectedIconTheme`）；不触发版本号变更（规则 2）。
- **不引依赖**：IconView 用自定义 `IObserver<T>` 而非 System.Reactive。

## 9. 风险

| 风险 | 缓解 |
|------|------|
| 206 处静态 + 12 组 C# 站点机械替换遗漏 / 改坏属性 | 分文件 review + 残留门禁 grep（含 UiTestWindow，26 处最易漏）+ 迁移前后截图对照 |
| `.cs` 侧 PathIcon 站点漏迁（编译通过、运行期静默失效） | §3 已补 `MainWindow.axaml.cs:1580/1618`；§7 增 `.cs` 门禁（`new PathIcon`/`OfType<PathIcon>`/`FindControl<PathIcon>`/`Content: PathIcon` = 0）；T7 增列头菜单回归测试 |
| `IconView` 前景色语义与 `PathIcon` 不一致 | 已对齐：ControlTheme 默认 `Foreground={DynamicResource TextControlForeground}`（与 Fluent PathIcon 同 key/同优先级），祖先继承实际不生效（与 PathIcon 行为一致）；`ToggleButton:checked controls:IconView` 选择器可压过 ControlTheme（同现有 PathIcon 机制）；T4 断言解析值 + A 套截图兜底 |
| `SourceKey` 订阅泄漏 | `OnDetachedFromVisualTree` 必须 `Dispose` 订阅；测试覆盖 attach/detach/re-attach |
| ControlTheme setter 与 Style 选择器优先级判断错误 | 与 Fluent `PathIcon` 同机制（ControlTheme 给默认值、`Application.Styles` 选择器覆盖）；T4 断言未设值时的解析结果，手动 QA 覆盖选中态 |
| 键冲突（AppIcons 与 Icons.A 同名） | 运行时**只** merge Icons.A 三层，绝不 merge AppIcons |
| `ApplyTheme` 同主题早退吞掉图标主题切换 | 早退条件改双条件（`_loadedThemeUri` + `_loadedIconThemeId`）；T2 覆盖「基础主题不变、仅图标 id 变」路径 |
| 行号漂移导致漏站 | §3 行号为 2026-10-21 快照；执行时以锚点符号（x:Name、方法名、模式）为准，动手前重新 grep 校准 |
| **启动劣化**：图标字典在 App.axaml 静态 merge 与 ApplyTheme() 各解析一次 → 启动 +33ms；切主题若未命中缓存 → 每次换肤 +33ms | §4.7 硬约束 1/2：启动解析次数 = 1（去掉静态 merge 或复用同一 provider 实例）+ _iconThemeProviders 缓存必须命中；§7 启动 A/B 门控 |
| **IconView 控件化开销**：215 个实例若引入模板匹配/可视图 → Win.Xaml（当前 ~330ms）明显劣化 | §4.7 硬约束 3：ControlTheme 仅 3 个 Setter、无 Template；实测控件级开销 ≤10ms |
