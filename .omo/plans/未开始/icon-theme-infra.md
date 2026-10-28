# 图标主题系统 — 计划 1：基础设施（IconView + DrawingImage 契约迁移 + 三层字典骨架）

> **状态**: 📋 未开始 | **创建**: 2026-10-15 | **最近复核**: 2026-10-24（三次复核：2026-10-21~23 四次启动优化重构后重普查 §3/§6/§7 数字；新增 T0 重普查、首帧哨兵修复、测试底座改造、`verify-repo` 图标守卫三项设计修订） | **优先级**: P2 | **预估工时**: 22-28h（原 16-20h 上调：消费面 215→**224**、涉及 axaml 17→**25**、外加测试底座与外置样式改造）
> **来源**: [docs/superpowers/specs/2026-10-15-icon-theme-system-design.md](../../docs/superpowers/specs/2026-10-15-icon-theme-system-design.md)（已批准）
> **拆分**: 本系统拆 3 份串行计划 —— **① 基础设施（本文）** → ② 导入管线 + 单图标覆盖 + 设置 UI → ③ IconGen + C/D/E/F/G 内容生产。
> **前置**: 无。**后续**: 计划 2 依赖本文交付的 `IconView` / `SourceKey` / 三层字典契约。

## 1. 目标与交付标准

把应用内图标契约从「`PathIcon` + `Geometry` 资源」升级为「`IconView` + `DrawingImage` 资源」，并搭好三层资源字典骨架（默认 A / 当前主题 / 覆盖）。**本计划不引入任何设置 UI、不做导入、不生产 C-G 套内容。**

**交付标准（DoD）**：

1. 全仓不再有 `<PathIcon Data="{StaticResource Icon…}` 形态（Avalonia 自带控件模板内的 PathIcon 不属于我方代码，不算）。
2. 运行 `dotnet run --project tools/IconGen` 可确定性生成 `Icons.A.axaml`；测试强制其**键集**与**每键 Geometry 字符串**和 `AppIcons.axaml` 完全同步（比资源条目内容，不比文件字节）。
3. 新增 `IconView` 控件，行为与 `PathIcon` 等价（尺寸/均匀缩放/`Foreground` 上色），并额外支持 `SourceKey` 键订阅刷新；**默认尺寸不依赖 ControlTheme 也能成立**（`MeasureOverride` 兜底 20×20，见 §4.3）。
4. `ApplyTheme()` 装配三层图标字典，`SelectedIconTheme`（默认 `"A"`）持久化于 `AppSettings`；**首次启动（默认浅色 + A 套）也必须真正装配**，`ApplyTheme` 早退不得吞掉首次装配（见 §4.4 哨兵）。
5. **A 套零视觉差异**：迁移后全 UI 与迁移前像素级一致（浅色 + 深色 + 选中态），含非正方形图标（`IconEmptyFolder`、各 chevron）不被拉伸。
6. 构建 0 错误；`MantisZip.Tests` + `MantisZip.UI.Avalonia.Tests` 全绿。
7. **首帧图标非空**：headless 断言「默认设置下装配一次后 `IconFolder` 可解析为 `DrawingImage`」，防止早退路径静默留白。
8. **键名门禁进 CI**：`pwsh -File scripts\verify-repo.ps1 -Task guards` 新增第五条守卫——axaml 中每个 `{DynamicResource Icon…}` 键必须存在于 `Icons.A.axaml`（见 T9b）。

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

## 3. 现状核实（2026-10-24 三次复核，全仓 grep + guards 实测）

> 2026-10-21~23 的四次启动优化重构（`804889db` / `ed082c4b` / `46d8475f` / `86e5139a` / `81d86ba2` / `8d539a33` / `1000367a`）把 MainWindow 内容拆成 7 个独立视图（`Controls/Menus/{File,Edit,View,Tools}MenuItems.axaml` + `TestMenuView.axaml` + `Controls/ArchiveContentView.axaml` + `Controls/MainFilterBar.axaml`），**2026-10-21 版普查对这些拆分文件完全未覆盖**——下表已按 2026-10-24 实况重算（旧值 215/206/17 文件全部作废）。
> 行号为复核当日快照，执行时以行内**锚点符号**为准（行号可能继续漂移）。

| 项 | 事实 | 锚点（参考行号） |
|----|------|------|
| 图标资源 | `AppIcons.axaml`：**117** 个 `<Geometry x:Key="IconXxx">…</Geometry>`（2026-10-24 实测；与 `verify-repo.ps1 -Task guards` 的 icons 守卫 `definedCount=117` 同源，旧版写 116 少算 1）；部分 d 串带 fill-rule 前缀（如 `IconEmptyFolder` 以 `F0 ` 开头），IconGen 必须**原样保留**整个 d 串 | `Resources/Icons/AppIcons.axaml` |
| 资源嵌入方式 | `Resources\**` 全部 `AvaloniaResource`（运行时 `avares://` 加载）；`AvaloniaXaml Include **\*.axaml Exclude Resources\**\*.axaml` → `Resources/Icons/*.axaml` **不**走 XamlIl 预编译。**已验证可行**：`AvaloniaXamlLoader.Load(new Uri("avares://…/Resources/Icons/*.axaml"))` 目前就在用（`App.Theming.cs:77` + 4 处测试桩） | `MantisZip.UI.Avalonia.csproj:51,56-57` |
| PathIcon 总量 | `<PathIcon` 元素共 **224** 个（**25** 个 axaml 文件），全部自闭合（`</PathIcon>` 零命中），且**全部同时显式带 `Width` + `Height`**（缺 `Width` = 0 处、缺 `Height` = 0 处，已逐一核对）——**无一处依赖默认尺寸 20** | 全仓 |
| 文件分布（PathIcon 数） | EditMenuItems 9 / FileMenuItems 8 / TestMenuView 26 / ToolsMenuItems 3 / ViewMenuItems 7 / ArchiveContentView 22 / MainFilterBar 9 / QuickPathControl 6 / QuickPathPicker 4 / ResultTreeView 7 / CompressConflictDialog 6 / ConflictDialog 7 / CustomFilePickerDialog 8 / ExtractSettingsWindow 2 / FavoriteManagerWindow 5 / IconTestWindow 1 / MatchedPasswordDialog 4 / PasswordManagerWindow 8 / ProgressWindow 11 / MainWindow 15 / PasswordDialog 1 / PreviewPanel 17 / SettingsWindow 11 / UiTestWindow 26 / `App.axaml` 1（TabIcon 模板） | 全仓 |
| 静态消费面 | `Data="{StaticResource IconXxx}"` 共 **213 处（23 文件）**：EditMenuItems 9 / FileMenuItems 8 / TestMenuView 26 / ToolsMenuItems 3 / ViewMenuItems 7 / ArchiveContentView 21 / MainFilterBar 9 / QuickPathControl 4 / QuickPathPicker 4 / ResultTreeView 5 / CompressConflictDialog 6 / ConflictDialog 7 / CustomFilePickerDialog 8 / ExtractSettingsWindow 1 / FavoriteManagerWindow 5 / MatchedPasswordDialog 4 / PasswordManagerWindow 8 / ProgressWindow 10 / MainWindow 13 / PasswordDialog 1 / PreviewPanel 17 / SettingsWindow 11 / UiTestWindow 26（其中 5 个菜单视图 + ArchiveContentView + MainFilterBar + MainWindow 拆分后的内容区 = **启动优化重构引入的新战场，最易漏**） | 全仓 |
| converter 消费 | `GeometryResourceConverter` 真使用 **8 处**：`App.axaml:280`(TabIcon) / **`ArchiveContentView.axaml:100`（新增，启动优化拆分后迁入）** / `QuickPathControl.axaml:122` / **`QuickPathControl.axaml:196`（新增）** / `ResultTreeView.axaml:125` / `ExtractSettingsWindow.axaml:63` / `IconTestWindow.axaml:89` / `ProgressWindow.axaml:1033`（`StatusIconKey` 绑定，本地 key 为 `"GeometryResource"`；旧写 :888 已漂移） | 全仓 |
| VM 直接绑定 | `MainWindow.axaml:207` `<PathIcon Data="{Binding ToolbarPasswordIcon}" …>`、`MainWindow.axaml:263` `<PathIcon Data="{Binding PasswordStatusIcon}" …>`（2026-10-24 实测；旧写 :640/:1344 已随菜单延迟创建重构漂移） | 全仓 |
| 代码后置专属元素 | `ResultTreeView.axaml:201` `x:Name="StateIconPath"`（XAML 无 Data，由 code-behind 赋值） | 全仓 |
| converter 声明 | **6 处，key 名不统一**：5 处 `x:Key="GeometryResourceConverter"`（`App.axaml:15` / `ResultTreeView.axaml:8` / `QuickPathControl.axaml:9` / `ExtractSettingsWindow.axaml:17` / `IconTestWindow.axaml:16`）+ 1 处 `x:Key="GeometryResource"`（`ProgressWindow.axaml:29`）——删 `GeometryResourceConverter.cs` 时须同删，否则 6 个 XAML 编译报错；消费点须按各自 key 名引用，不可统一按 `GeometryResource` 写 | 全仓 |
| BoolToIconConverter | **已零引用**（2026-10-15 版普查「ProgressWindow.axaml:20/140」已过期：ProgressWindow 眼睛切换早已改为 flyout + code-behind `FindResource`）。T5 不再新增 Key 版 converter，直接删除即可 | `Converters/BoolToIconConverter.cs` |
| C# 动态设置 | **`Controls/ArchiveContentView.Events.cs:53`（`new PathIcon{Data=…, Width=10, Height=10, Foreground=GetThemeBrush("ThemeTextPrimaryBrush")}`）+ `:91`（`GetColumnHeaderIcon` 内 `panel.Children.OfType<PathIcon>().FirstOrDefault()`，方法签名 `private static Geometry? GetColumnHeaderIcon(DataGridColumn column)`）——2026-10-24 修正：旧版指向 `MainWindow.axaml.cs:1580/1618`，该代码已随内容区拆分迁入 `ArchiveContentView.Events.cs`，**`MainWindow.axaml.cs` 现无任何 PathIcon 代码**。T6 把列头 `PathIcon` 迁成 `IconView` 后此模式**永假** → `iconBox.Child=null`，列头右键菜单图标静默消失（编译通过、无门禁覆盖）**；`ProgressWindow.axaml.cs:238`(CancelButtonIcon) / `415`、`573`、`907`、`913`(PauseButtonIcon) / `732-733` + `755-761`(PwdFlyoutEyeIcon，`FindControl<PathIcon>` 得句柄) / `861-863`(**`sender is Button { Content: PathIcon copyIcon }` 模式匹配，改 IconView 时连类型模式一起改**)；`PasswordManagerWindow.axaml.cs:62-63`；`MatchedPasswordDialog.axaml.cs:87-92`(new PathIcon 对象初始化器 + Foreground)；`ResultTreeView.axaml.cs:246,257`(StateIconPath) | 全仓 |
| VM 图标属性 | `_passwordStatusIcon` 声明在 `MainWindowViewModel.cs:360`（`:363` `HasPasswordStatus`、`:1068` 置 null）、`_toolbarPasswordIcon` 声明在 **`MainWindowViewModel.Password.cs:83`**（partial 拆分）；赋值点共 **6 处**：`MainWindowViewModel.cs:1068` + `Password.cs:65/70/108/116/124`；`app?.FindResource("IconKey/IconLockClosed/IconLockOpen")` 得 `Geometry` | `MainWindow.axaml:207,263` 消费 |
| 缺 `xmlns:controls` 的消费文件 | **18 个**（2026-10-24 实测，旧版 11）：5 个菜单视图 / ArchiveContentView / MainFilterBar / ResultTreeView / PreviewPanel / PasswordDialog / MainWindow / CompressConflictDialog / ConflictDialog / FavoriteManagerWindow / IconTestWindow / MatchedPasswordDialog / PasswordManagerWindow / ProgressWindow。**已有**：QuickPathControl / QuickPathPicker / CustomFilePickerDialog / ExtractSettingsWindow / SettingsWindow / UiTestWindow / `App.axaml` | — |
| 主题装配 | `App.axaml.cs:35` `IconResourcesUri`（**迁移时删除/替换**）；**`App.axaml.cs:50` `_loadedThemeUri = new Uri(LightThemeUri)`（初值刻意 = Light，契约注释在 `:45-53`：初值必须等于 `App.axaml` 静态合并的默认主题）**；`ApplyTheme()` 实际在 **`App.Theming.cs:47`**（partial 拆分）：`:64-70` 同主题早退（`_loadedThemeUri == targetUri` → 只设 `RequestedThemeVariant` 后 `return`）、`:73-81` 加载主题 + `_cachedIconProvider` + `MergedDictionaries.Clear()` → 加主题 → 加图标、`:83` 更新 `_loadedThemeUri`、`:86-88` `RequestedThemeVariant`；调用点 `App.axaml.cs:83`（`OnFrameworkInitializationCompleted` 内，早于 MainWindow 创建）+ `App.Theming.cs:99`（`RefreshTheme`）。**⇒ 默认「浅色 + A 套」首次启动会命中早退分支；改为双条件早退后必须同步改哨兵初值，否则首帧图标层永不装配（见 §4.4 ⚠ 硬伤）** | `App.axaml.cs` + `App.Theming.cs` |
| 静态字典 | `App.axaml:8-13` MergedDictionaries = ThemeLight(`:10`) + AppIcons(`:12`)；`App.axaml:15-16` 全局 converter 声明（2026-10-21 修正行号） | `App.axaml` |
| 选中态样式 | `App.axaml:140` `ToggleButton:checked PathIcon` —— **唯一**针对 PathIcon 的选择器（descendant 组合子，覆盖全部 checked 子状态，含 pointerover/pressed）；`:143` `ToggleButton:checked:pointerover`、`:147` `ToggleButton:checked:pressed` **针对 ToggleButton 自身**（设按钮 `Foreground`/`Background`），与 IconView 无关（2026-10-21 修正：原描述误称三者都是 PathIcon 选择器） | `App.axaml:131-149` |
| 设置 | `AppSettings` 为纯 POCO（外观节 + `SelectedIconTheme` 新属性），但 `Load()` 走**进程级静态缓存**（`_cached` 命中即返同实例）、`Save()` **写真实用户配置文件**（`SettingsFile` static readonly 不可注入，测试注释明确）→ 测试里**不得 Save**，也不要依赖「改实例字段再 ApplyTheme」的时序 | `Models/AppSettings.cs` |
| 测试桩 | `TestAppBuilder` = **`AppBuilder.Configure<Application>()` 裸 Application，不加载 `App.axaml`** → 无全局 Style、无 ControlTheme、无 ThemeLight/Dark、无 `TextControlForeground`、无 `IconElementThemeWidth/Height`；`UseSkia().UseHeadless(new(){ UseHeadlessDrawing=false })` → 真实像素渲染可行。**⚠ 直接决定 T2/T4 测试写法（见 §4.6 与 T4）** | `tests/…/TestAppBuilder.cs` |
| 测试图标合并 | 4 处 `EnsureIconResources()` 直接 `AvaloniaXamlLoader.Load(avares://…/Resources/Icons/AppIcons.axaml)` 并并入 `Application.Current.Resources`：`ExtractPreserveFullPathPreviewMatchTests.cs:264,273`、`PickerOptionsRegionTests.cs:40,49`、`PickerOptionsRegionLayoutTests.cs:39,48`、`ResultTreeViewFilterTests.cs:37,43` | tests |
| 应用级资源守卫 | `MainArchiveContentLazyLoadTests.cs`：`AppLevelResourceKeys()`(`:216-234`) 静态读 `App.axaml` + ThemeLight + ThemeDark + **`AppIcons.axaml`** 的 `x:Key`；三守卫（`ArchiveContentView_/MainWindow_/MenuViews_ReferencedStaticResources_AreResolvable`）用 `ReferencedStaticResources()` **只匹配 `{StaticResource key}`** → 迁移后对图标**归零（假绿）**，且仍在校验旧字典。`MenuViewRelPaths` 只列 4 个菜单视图（不含 `TestMenuView`） | `tests/…/MainArchiveContentLazyLoadTests.cs` |
| 仓库自检守卫 | `scripts/verify-repo.ps1`（唯一实现，CI 必跑）：四守卫 keybindings / i18n-bindings / filepicker / **icons**（`Test-GuardIcons` 比对 `AppIcons.axaml` 的 `x:Key` 与 `IconTestViewModel.cs` 里的 `"Icon*"` 字符串，当前 defined=registered=117，`ok:true`）。**图标引用键名目前零门禁** → T9b 新增第五条 | `scripts/verify-repo.ps1` |
| 覆盖率契约 | `IconTestViewModel.LoadAllIcons()`（117 条 `Add(category, semanticName, emoji, resourceKey, status, location, notes)`）——AGENTS 规则 8 强制维护 | `ViewModels/IconTestViewModel.cs` |
| 版本/InternalsVisibleTo | **`tools/` 已存在**（`tools/CorruptProbe`：net10.0 console、不入 sln、只 ProjectReference Core，IconGen 可直接沿用该先例；另有 `tools/dsh-plugin-mantiszip`、`tools/measure-startup.ps1`）；csproj `InternalsVisibleTo` = `MantisZip.Tests` + `MantisZip.UI.Avalonia.Tests` | `MantisZip.UI.Avalonia.csproj:19,21` |

> **计数口径（避免歧义，2026-10-24 实测自洽）**：**224** = 213（StaticResource）+ 8（converter 绑定）+ 2（VM 直接绑定）+ 1（StateIconPath）；另有 `App.axaml:280` 的 TabIcon（converter 那 8 处之一）与 AppIcons 的 117 键为资源侧。`UiTestWindow` 26 处属 Debug 专用窗但**随正式构建编译**，必须迁移（否则 DoD 门禁不过）。

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
  <!-- …其余 116 键（共 117，与 AppIcons.axaml 键集一一对应），顺序与 AppIcons.axaml 一致… -->
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
- `Stretch: Stretch`（默认 `Uniform`）—— 尺寸对齐 `PathIcon`（均匀缩放、居中、不裁剪）。**2026-10-24 实测（Avalonia 12.1.2 反射）**：`PathIcon` **没有** `Stretch` 属性（它只声明 `Data`，`IconElement` 无自有成员，`Foreground` 来自 `TemplatedControl`），Fluent 模板内是**写死的等比例缩放**——所以 `Stretch` 是 IconView 的超集能力，`Uniform` 必须成为默认值，`Fill` 只作可选（非默认）。
- **`MeasureOverride` 兜底（必须实现）**：`Width`/`Height` 未设时返回 `new Size(20, 20)`（或读 `IconElementThemeWidth/Height`，缺省 20），**不得只依赖 §4.6 ControlTheme 的 Width/Height setter**——自绘 `Control` 默认 `MeasureOverride` 返回 `0×0`，一旦进入「无 ControlTheme」环境（测试宿主、样式覆盖、后续在别处 in-place 复用）就变成 0 尺寸**隐形控件**。ControlTheme 与 MeasureOverride 双保险。
- `Render`：展平 `Source.Drawing`（`GeometryDrawing` / `DrawingGroup.Children` 递归），部件 `Brush == null` 用 `Foreground`，否则用部件 Brush；**先算整体 `Drawing.Bounds`，取 `scale = min(Bounds.W / drawing.W, Bounds.H / drawing.H)` 等比例缩放并居中**——**严禁按 X/Y 独立拉伸铺满**（`IconEmptyFolder`、chevron 类非正方形图标会被拉扁，直接违反 DoD #5）。

### 4.4 三层字典 + `ApplyTheme` 装配

`AppSettings` 外观节新增（默认 `"A"`）：

```csharp
/// <summary>图标风格主题：A=现状默认 / C/D/E/F/G=内置套 / 导入包 id。仅影响应用内矢量图标。</summary>
public string SelectedIconTheme { get; set; } = "A";
```

`App.axaml.cs` 新增 `IconsAUri` 常量、`_iconThemeProviders` 缓存与 `GetIconThemeProvider`/`ResolveIconThemeUri`/`AddIconOverrideLayer`；**装配与早退判定抽到 `IconThemeAssembler`**（`Assemble` + `ShouldSkip`，见 T2：纯函数、可单测，因为 `ApplyTheme` 是 private 且测试宿主里没有真 `App` 实例）；`ApplyTheme()`（**实际位于 `App.Theming.cs:47`**）在既有「加主题字典」之后调它，按 ①→②→③ 顺序装配。下方代码块给出**装配体的形态**（`_iconThemeProviders`/`_loadedIconThemeId` 字段留在 `App.axaml.cs`，方法体归 `IconThemeAssembler`）。

> ⚠ **必须先改造既有缓存/早退逻辑**（`App.Theming.cs:63-83`）：现有实现「同主题早退」（`_loadedThemeUri == targetUri` 直接 `return`）会**吞掉图标主题切换**——改 `SelectedIconTheme` 时基础主题未变 → 直接 return → 图标层不重建。现有 `_cachedIconProvider`（`App.axaml.cs:52-53`）只缓存单个图标字典，也无法承载三层。
> 1. **⚠ 首帧哨兵（硬伤，必须与双条件早退一起改）**：`_loadedThemeUri` 当前初值是 `new Uri(LightThemeUri)`（`App.axaml.cs:50`，当初就是为「App.axaml 静态合并 ThemeLight」而设，注释 `:45-53` 写明此契约）。若只照抄「`_loadedIconThemeId` 初值 `"A"` + 双条件早退」，则**默认组合（浅色 + A 套）首次 `ApplyTheme()` 直接 return，三层字典一次都不装配**——今天不炸只是因为 `App.axaml:12` 还静态 merge 了 AppIcons；一旦按 §4.7 硬约束 1 **(a) 去掉静态 merge，首帧全部图标消失，而深色主题用户完全正常**（一半人复现的静默 bug）。改法：两个哨兵初值都用 `null`（`private Uri? _loadedThemeUri;` / `private string? _loadedIconThemeId;`），早退条件补非空判断：`if (_loadedThemeUri == targetUri && _loadedIconThemeId == iconThemeId && _loadedIconThemeId != null) { …; return; }`；或增加 `bool _iconThemeAssembled` 参与判定。**T2 必须补首帧断言**（默认设置 + 一次 `ApplyTheme` → `IconFolder` 可解析为 `DrawingImage`）。
> 2. 图标层按 id 缓存 `Dictionary<string, IResourceProvider>`（**已确认加入**）：① 层 `Icons.A` 只解析一次，② 层按 id 缓存 —— 避免每次 `ApplyTheme` 重复 `AvaloniaXamlLoader.Load` 解析 117 键字典。
> 3. `App.axaml.cs:45-53` 的契约注释（依赖 `MergedDictionaries[1]` 取图标字典）必须同步改写为「[主题, ①A, ②当前, ③覆盖]」；`_cachedIconProvider` 字段由 `_iconThemeProviders` 取代。

```csharp
private const string IconsAUri = "avares://MantisZip.UI.Avalonia/Resources/Icons/Icons.A.axaml";

// 按图标主题 id 缓存已解析字典（"A" / "C"… / 导入包 id）
private readonly Dictionary<string, IResourceProvider> _iconThemeProviders = new(StringComparer.Ordinal);
private string? _loadedIconThemeId;   // ⚠ 初值必须 null（配合 _loadedThemeUri 也改 null）：否则「浅色 + A 套」首次装配被早退吞掉

// ApplyTheme() 内（早退判定改为「基础主题 + 图标主题」双条件），字典替换段：
var settings = AppSettings.Load();
var iconThemeId = string.IsNullOrWhiteSpace(settings.SelectedIconTheme) ? "A" : settings.SelectedIconTheme;

// 双条件早退：两个哨兵都必须非空，否则首帧跳过装配（note 1 硬伤）
if (_loadedThemeUri != null && _loadedThemeUri == targetUri
    && _loadedIconThemeId != null && _loadedIconThemeId == iconThemeId)
{
    RequestedThemeVariant = isDark ? global::Avalonia.Styling.ThemeVariant.Dark : global::Avalonia.Styling.ThemeVariant.Light;
    return;
}

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
4. **缓存失效归计划 2**：计划 1 只建缓存，不建失效入口。计划 2 新增的 `RefreshIconTheme()` 必须 `_iconThemeProviders.Remove(id)` + 把 `_loadedIconThemeId` 置回 `null`（不是置成当前的 id），否则 §4.4 note 1 的双条件早退会直接 return（改包/改覆盖后界面不刷新）。

### 4.5 消费端迁移（机械替换）

| 现状 | 迁移后 |
|------|--------|
| `<PathIcon Data="{StaticResource IconXxx}" …/>` | `<controls:IconView Source="{DynamicResource IconXxx}" …/>`（保留其余属性；`Width`/`Height`/`Foreground`/`Classes` 原样） |
| `Data="{Binding X, Converter={StaticResource GeometryResourceConverter}}"` | `SourceKey="{Binding X}"` |
| `Data="{Binding ToolbarPasswordIcon}"` / `Data="{Binding PasswordStatusIcon}"` | `SourceKey="{Binding …}"`（配合 VM 属性改存键字符串） |
| `Data="{Binding StatusIconKey, Converter={StaticResource GeometryResource}}"`（**`ProgressWindow.axaml:1033`**，本地 key 名是 `GeometryResource` 不是 `GeometryResourceConverter`） | `SourceKey="{Binding StatusIconKey}"`（VM 已经是键字符串，无缝） |
| C# `pathIcon.Data = (Geometry?)FindResource("IconX")` | `iconView.SourceKey = "IconX"` |
| VM `object?` 存 `Geometry` | VM `string?` 存键名（`"IconKey"`/`"IconLockClosed"`/`"IconLockOpen"`），XAML 绑 `SourceKey` |
| `App.axaml:140` `ToggleButton:checked PathIcon`（**唯一** PathIcon 选择器） | 旁增**一条**并列 `<Style Selector="ToggleButton:checked controls:IconView">` + `Foreground=White`。`:143`/`:147` 针对 ToggleButton 自身（非 PathIcon），**无需**扩 IconView 选择器（`:140` 已覆盖全部 checked 子状态）。Avalonia 对单条 Style 内逗号分组支持不明，不依赖；不合并选择器 |

- converter：完成 **8 处**绑定迁移（清单见 §3 表）后，**删除** `Converters/GeometryResourceConverter.cs` 及全部 **6 处声明**；**`Converters/BoolToIconConverter.cs` 当前已零引用，直接删除、不新增 Key 版**（2026-10-15 版计划的 `BoolToIconKeyConverter` 随普查过期一并取消）。
- **18 个**缺命名空间的文件补 `xmlns:controls="clr-namespace:MantisZip.UI.Avalonia.Controls"`（清单见 §3 表；5 个菜单视图 + ArchiveContentView + MainFilterBar + ResultTreeView + PreviewPanel + PasswordDialog + MainWindow + CompressConflictDialog + ConflictDialog + FavoriteManagerWindow + IconTestWindow + MatchedPasswordDialog + PasswordManagerWindow + ProgressWindow）。
- `IconTestWindow.axaml:89`（Debug 工具）改用 `<controls:IconView SourceKey="{Binding ResourceKey}" …/>`。
- `ProgressWindow.axaml.cs:861-863` 的 `sender is Button { Content: PathIcon copyIcon }` 类型模式与 `:732-733`/`:755-761` 的 `FindControl<PathIcon>("PwdFlyoutEyeIcon")` 须同步改 `IconView`（元素标签迁移后 old 模式永假，属编译外静默 bug）。
- **`Controls/ArchiveContentView.Events.cs` 是唯一 C# 动态创建点，必须一并改**：`:53` 的 `new PathIcon { Data = …, Width = 10, Height = 10, Foreground = GetThemeBrush(…) }` → `new IconView { SourceKey = …, Width = 10, Height = 10 }`（`Foreground` 由 ControlTheme 提供，**不要**再手工 `GetThemeBrush`，否则切主题不跟随）；`:91` 的 `GetColumnHeaderIcon` 从 `panel.Children.OfType<PathIcon>().FirstOrDefault()` 取 `Geometry?` 改为取 `SourceKey` 字符串（签名 `private static Geometry? GetColumnHeaderIcon(DataGridColumn column)` → `private static string? GetColumnHeaderIcon(DataGridColumn column)`），调用链同步。

### 4.6 IconView 默认样式（ControlTheme，**外置成独立资源文件**）

新增 `Resources/Icons/IconViewStyles.axaml`（`ResourceDictionary`，与 AppIcons / Icons.A 同域、同「AvaloniaResource + 运行时 `AvaloniaXamlLoader.Load`」机制），由 `App.axaml` 合并；尺寸与前景色均与 `PathIcon` 默认一致：

```xml
<!-- IconView 默认样式：尺寸/前景色与 Fluent PathIcon 逐字对齐（20×20 + TextControlForeground）；局部 Width/Height/Foreground 可覆盖 -->
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:controls="clr-namespace:MantisZip.UI.Avalonia.Controls">
  <ControlTheme x:Key="{x:Type controls:IconView}" TargetType="controls:IconView">
    <Setter Property="Width" Value="{DynamicResource IconElementThemeWidth}" />
    <Setter Property="Height" Value="{DynamicResource IconElementThemeHeight}" />
    <Setter Property="Foreground" Value="{DynamicResource TextControlForeground}" />
  </ControlTheme>
</ResourceDictionary>
```

> **为什么外置（2026-10-24 修订）**：测试宿主 `TestAppBuilder` 是**裸 `Application`，不加载 `App.axaml`**（§3 表），把 ControlTheme 写在 `App.axaml` 里 → T4 的两条断言（`DesiredSize≈20×20`、`Foreground == TextControlForeground`）**无法通过，除非把整个 App.axaml 塞进测试**。外置后测试只需两步：`Application.Current.Resources.MergedDictionaries.Add(AvaloniaXamlLoader.Load(new Uri("avares://MantisZip.UI.Avalonia/Resources/Icons/IconViewStyles.axaml")) as IResourceProvider)` + 同法并入 `Themes/ThemeLight.axaml`（`TextControlForeground` 的来源）。**若为省事而在测试里手动 `new ControlTheme`，等于绕过被测物 → 门禁假绿，禁止。**
> **注意**：`Width/Height` 的 setter 引用 Fluent 的 `IconElementThemeWidth/Height`，测试宿主没有 `FluentTheme` → 这两个键解析为 Unset，尺寸仍应由 §4.3 的 `MeasureOverride` 兜底 20×20（双保险的意义就在这里）；T4 对 ControlTheme 的断言改断言「`Type`→ControlTheme 可查到」而非依赖 Fluent 资源。`Foreground` setter 的 `TextControlForeground` 由 ThemeLight/Dark 提供（`:103`），合并 ThemeLight 后可解析。
> `Foreground` setter 是 A 套零视觉差异的关键：它与 Fluent `PathIcon` 的 ControlTheme 默认值同 key，且 `Application.Styles` 的 `ToggleButton:checked controls:IconView` 仍能压过它（与现有 `ToggleButton:checked PathIcon` 压过 Fluent PathIcon ControlTheme 同一机制）。

### 4.7 启动性能约束（2026-10-21 增补，2026-10-24 复核）

本计划是启动路径上唯一有实质风险的改动（`ApplyTheme()` 启动期执行 + **224** 处静态图标替换 + 图标字典重解析）。启动优化二期（`startup-theme-dedup`）已建立「同主题早退 + 图标字典缓存」机制，本计划**不得破坏**它——§4.4 note 1-3 已覆盖大部分（双条件早退 / `_iconThemeProviders` 按 id 缓存 / A 时跳过②层），此处只补**尚未覆盖的启动期解析次数**与**量化门控**。

> ⚠ **两项基线不属于「已实测」**，T0 必须补测（否则门控是在猜）：
> ① 下表测的是 `Geometry` 字典（`PathIcon` 的 `Data`），而运行时解析的是 `DrawingImage`+`GeometryDrawing` 字典——成本预期同量级但**无数据**；
> ② `IconView` 是自绘 `Control`，只有 200×`Button` 作近似。
> **T0 探针（10 分钟）**：解析 `Icons.A.axaml` 计入时长；T3 完成后补 200×`IconView` 实例化。若 `DrawingImage` 解析 >60ms，需重新议定 20ms 门控（先报数再决定，不得硬顶通过）。

**实测基线（2026-10-21 无头探针，Geometry 字典）**

| 测量项 | 结果 |
|---|---|
| `AppIcons.axaml` 解析（**117** Geometry） | **32.9ms**（占启动 `App.Initialize` 78ms 的 42%） |
| `ThemeLight.axaml` 解析（对照） | 1.8ms |
| 200× `PathIcon` 实例化 | 33.6ms（冷，含 JIT）；热态 200 个仅 1.2ms → 单实例 ≈0.006ms |
| 200× `Button`（`Control` 近似 `IconView`） | 7.8ms → 控件级实例化开销可忽略 |
| 200× `PathIcon` + `GetResourceObservable` | 1.2ms → `SourceKey` 订阅机制本身很便宜 |

**硬约束（实施时必须满足，违反即打回）**

1. **启动期图标字典解析次数 = 1**（与当前基线持平，**不得变成 2**）：§4.4「同时」第 1 条要求 `App.axaml:12` 静态 merge 改挂 `Icons.A.axaml`，而 `ApplyTheme()` 里的 `GetIconThemeProvider("A")` 会**再 `Load` 一次** → 启动多解析 **33ms**。二选一：
   - (a) **推荐**：`App.axaml` **不再静态 merge 图标字典**——迁移后图标键全为 `DynamicResource`（静态引用已清零，DoD #1），首帧不依赖静态 merge；`ApplyTheme()` 在 `OnFrameworkInitializationCompleted` 内、早于 `MainWindow` 创建 → 首帧不缺资源。**⚠ 选 (a) 必须同时完成 §4.4 note 1 的首帧哨兵修复**（`_loadedThemeUri` / `_loadedIconThemeId` 初值改 `null`），否则默认「浅色 + A 套」首次 `ApplyTheme()` 早退 return → **首帧图标全空**（这是本方案最容易被漏掉的一步）；
   - (b) `GetIconThemeProvider("A")` 首次命中时**复用** `App.axaml` 静态 merge 进来的同一 provider 实例（而非重新 `Load`）。选 (b) 则 `App.axaml.cs:45-53` 的 `MergedDictionaries[1]` 契约要改成「[主题, ①A, …]」，且依赖索引位置脆弱。
2. **切主题/切图标主题不得重复解析**：§4.4 note 2 的 `_iconThemeProviders` 按 id 缓存必须真正命中（不得每次 `ApplyTheme` 重新 `Load`），否则每次换肤 +33ms。
3. **`IconView` 保持轻量**：§4.6 已定 `ControlTheme` 只含 3 个 Setter、**无 `Template` 内容**——不得引入 `TemplatedControl` 模板或每实例可视图；实测 **224** 个实例的控件级开销 ≤10ms，可接受。
4. **224 处迁移后 XAML 体积增大** → `Win.Xaml` 段（当前 ~330ms）预期 +5~15ms，属可接受区间。

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

- [ ] **T0 — 重新普查 + 补测启动基线（先做，10-30 分钟，防止按过期行号改错）**
  - 重跑三组 grep 核对 §3 表：`<PathIcon`（预期 224 / 25 文件）、`Data="{StaticResource Icon`（213 / 23 文件）、`GeometryResourceConverter|{"GeometryResource"}`（8 用 / 6 声明）；逐文件数字对不上就以实测为准回填 §3，**不要**按表里的旧行号动手。
  - 补 §4.7 的两项未测基线：`Icons.A.axaml`（T1 产出）解析时长；200×`IconView` 实例化（T3 完成后追加）。探针脚本放 `tools/` 下临时工程，测完删除或不入库均可。
  - 产出：§3/§4.7 数字回填 commit（`docs(plans): …`）。

- [ ] **T1 — `tools/IconGen` 生成 `Icons.A.axaml`**
  - 新建 `tools/IconGen/IconGen.csproj`（net10.0 console，不入 sln）+ `Program.cs`：`XDocument` 读 `AppIcons.axaml`，按序输出 `Icons.A.axaml`（`GeometryDrawing` 无 Brush）。注意部分 d 串带 fill-rule 前缀（如 `IconEmptyFolder` 的 `F0 `），**整串原样写入**，不得裁剪/归一化空白。
  - **测试（`MantisZip.Tests` 或 `MantisZip.UI.Avalonia.Tests`，纯逻辑）**：`AppIcons` 键集 == `Icons.A` 键集；每键 Geometry 字符串（原样 trim 后）完全相等。测试不运行 IconGen 进程，直接读取两文件比对。
  - 运行 `dotnet run --project tools/IconGen` 生成并入库 `Icons.A.axaml`。commit。

- [ ] **T2 — 三层字典 + `SelectedIconTheme`（无 UI）**
  - `AppSettings` 加 `SelectedIconTheme`（默认 `"A"`，非法/空值按 `"A"` 处理）。
  - **装配逻辑必须抽成可测的纯函数（本次修订新增的硬要求）**：新增 `internal static class IconThemeAssembler`，至少两个成员：
    - `internal static void Assemble(ResourceDictionary target, IDictionary<string, IResourceProvider> cache, string iconThemeId, Func<string, string> resolveUri)` —— 清空 target、按 ①→②→③ 顺序 Add、cache 命中即复用；
    - `internal static bool ShouldSkip(Uri? loadedThemeUri, Uri targetUri, string? loadedIconThemeId, string iconThemeId)` —— 承载 §4.4 note 1 的双条件早退判定（**两个哨兵都非空且相等才 true**）。
    `App.Theming.cs` 的 `ApplyTheme()` 退化为「读 settings → 调 `ShouldSkip` 早退 → 调 `Assemble` → 设 `RequestedThemeVariant`」，本身不再持有装配细节。理由：`ApplyTheme` 是 private 实例方法且测试宿主里没有真 `App` 实例（`Application.Current` 是 `TestAppBuilder` 的裸 `Application`），不抽函数就**无法写 T2 的断言**；同时避免测试去调 `AppSettings.Save()`（会写真实用户目录）。
  - `App.axaml.cs` 加 `IconsAUri` + `GetIconThemeProvider`/`ResolveIconThemeUri`/`AddIconOverrideLayer`（空）；`App.axaml:12` 按 §4.7 硬约束 1 处理（推荐 (a)：不再静态 merge 图标字典）；同步改写 `App.axaml.cs:45-53` 的 `MergedDictionaries[1]` 契约注释。
  - **测试（纯逻辑，不需要 App 实例）**：① `Assemble` 后 `target.TryGetResource("IconFolder", null, out var r)` 命中且 `r is DrawingImage`；② 未知 id（`"ZZZ"`）→ 仍命中 A（静默回退），且 `target.MergedDictionaries.Count == 1`；③ `"A"` 连续装配两次 → `cache.Count == 1`（解析只发生一次）；④ **首帧回归断言**：`ShouldSkip(null, LightUri, null, "A") == false`（哨兵为 null 时不得早退——正对 §4.4 note 1 的硬伤）、`ShouldSkip(LightUri, LightUri, "A", "A") == true`、`ShouldSkip(LightUri, LightUri, "A", "B") == false`。commit。

- [ ] **T3 — `IconView` 控件**
  - `Controls/IconView.cs`：`Source`/`SourceKey`/`Foreground`（**自有 StyledProperty**，`inherits: true`，默认 `null`，默认值由 ControlTheme 提供）/`Stretch`；**显式注册 `AffectsRender` + `AffectsMeasure`**（`Source`/`SourceKey`/`Foreground`/`Stretch`，自绘控件必须手动声明）；**实现 `MeasureOverride` 尺寸兜底**（`Width`/`Height` 未设时 20×20，见 §4.3）；`Render` 展平 + **`scale = min(...)` 等比例缩放居中** + 部件 `Brush == null` 时用 `Foreground ?? TextControlForeground`；`OnAttachedToVisualTree` 订阅 `SourceKey`（自定义 `IObserver<T>`，不引 System.Reactive），`OnDetachedFromVisualTree` 释放；`SourceKey` 变更时重建订阅并 `InvalidateVisual()`；向前兼容「attach 前 SourceKey 已赋值」与「detach 后 re-attach」两种时序（attach 时无条件重建订阅）。
  - **测试（headless 真实像素）**：① `UseHeadlessDrawing=false` 下渲染 `DrawingImage`（单 GeometryDrawing 无 Brush）+ `Foreground=Red` → `RenderTargetBitmap` 命中红色像素；② 同图 `Foreground=Blue` → 蓝色；③ 部件带 baked `SolidColorBrush(Green)` → 忽略 Foreground 保持绿；④ `SourceKey` 指向 `Application` 资源 → 渲染非空，替换资源后重渲染像素变化（订阅刷新）；⑤ **非正方形图**（如 10×20 的图）→ 渲染结果等比（高宽比保持，不铺满方框）。commit。

- [ ] **T4 — ControlTheme + 选中态样式**
  - 新增 `Resources/Icons/IconViewStyles.axaml`（§4.6：`ResourceDictionary` + `ControlTheme` 3 个 Setter），`App.axaml` 合并该字典；再在 `App.axaml` 的 `Application.Styles` 内加 **仅 1 条**并列 Style `ToggleButton:checked controls:IconView`（`Foreground=White`，紧随 `:140` 的 PathIcon 条目）。`:143`/`:147` 针对 ToggleButton 自身，**不扩** IconView（见 4.5）。
  - **测试（headless，两段式）**：① 先 `MergedDictionaries.Add(Load(ThemeLight.axaml))` + `Add(Load(IconViewStyles.axaml))`，断言 `TryGetResource(typeof(IconView), null, out var ct)` → `ct is ControlTheme`；② 建 `IconView`（不设尺寸/不设 Foreground）挂进 `Window` 并跑一次 layout → `DesiredSize` ≈ 20×20（**由 `MeasureOverride` 兜底成立**，测试宿主没有 FluentTheme 的 `IconElementThemeWidth`），`Foreground` 解析值 == ThemeLight 的 `TextControlForeground` 笔刷。**禁止**在测试里 `new ControlTheme { … }` 手搓（绕过被测物 = 假绿）。
  - 选中态选择器由**源码文本守卫**断言（参考 `AppShutdownWiringTests` 读源码的既有模式）：`App.axaml` 文本包含 `ToggleButton:checked controls:IconView`；`IconTestWindow` 目检兜底。commit。

- [ ] **T5 — 删除已零引用的 `BoolToIconConverter`（普查更新，2026-10-15 版「新增 Key 版 converter」作废）**
  - 普查核实 `BoolToIconConverter` 全仓唯一出现处是自身定义文件（ProgressWindow 眼睛切换早已改为 code-behind `FindResource`）。**不新增** `BoolToIconKeyConverter`；直接删除 `Converters/BoolToIconConverter.cs`。
  - **门禁**：`git grep -n '\bBoolToIconConverter\b' -- src` = 0 命中。commit。

- [ ] **T6 — 静态消费面迁移（213 处 / 23 文件 → `IconView`）**
  - 覆盖 §3 表的 23 个静态文件（含 **UiTestWindow 26 处**——Debug 专项窗但随正式构建编译）；按文件逐批：标签替换 + `StaticResource`→`DynamicResource` + 补 `xmlns:controls`（**18** 文件，见 §3）。可用脚本辅助，但**必须逐文件 review**（保留 `Width`/`Height`/`Foreground`/`Classes`/内联属性；实测 224 处**全部**已显式 `Width`+`Height`，无默认尺寸依赖）。
  - ResultTreeView / QuickPathControl / ExtractSettingsWindow / IconTestWindow / ProgressWindow 内有多静态、converter、VM 绑定混合站点：本任务只处理 `Data="{StaticResource …}"`，动态站点留给 T7，标记避免双改。
  - **UiTestWindow 不是纯机械替换**：其 A/E 两块是「`PathIcon` 不继承 Foreground」的教学演示（自绘示意 + 说明文字），换成 `IconView` 后演示结论会失真——必须**改写为该控件的前景色/继承行为演示**（并顺带演示 `IconView` 的 `Stretch`/尺寸兜底），否则留下一条误导性文档。
  - **门禁测试/检查**：`git grep -n 'PathIcon Data="{StaticResource Icon' -- 'src/*.axaml'` = 0 命中（同时覆盖 UiTestWindow 处）。commit（可分批多次 commit）。

- [ ] **T7 — converter / VM / C# 动态站点迁移（8 converter 绑定 + 2 VM 绑定 + C# 站点）**
  - `App.axaml:280` TabIcon → `<controls:IconView SourceKey="{Binding $parent[TabItem].(controls:TabItemExtensions.TabIcon)}" …/>`（保留 `IsVisible`/`Width`/`Height`/`Foreground`）。
  - 其余 7 处 converter 绑定 → `SourceKey`：`ArchiveContentView.axaml:100`、`QuickPathControl.axaml:122`、`QuickPathControl.axaml:196`、`ResultTreeView.axaml:125`、`ExtractSettingsWindow.axaml:63`、`IconTestWindow.axaml:89`、`ProgressWindow.axaml:1033`（`StatusIconKey` 本就是键字符串，直接换属性即可）。
  - 2 处 VM 直接绑定：`MainWindow.axaml:207`/`:263` 的 `Data="{Binding ToolbarPasswordIcon/PasswordStatusIcon}"` → `SourceKey="{Binding …}"`（原计划的 640/1344 是启动优化重构前的行号，**已漂移**）。
  - C# 站点（2026-10-24 实测行号）：`ProgressWindow.axaml.cs:238/415/573/907/913`(x:Name 元素处理函数)、`:732-733`+`:755-761`(PwdFlyoutEyeIcon：`FindControl<PathIcon>` → `FindControl<IconView>`)、`:861-863`(模式匹配 `Button { Content: PathIcon copyIcon }` → `Content: IconView`)；`PasswordManagerWindow.axaml.cs:62-63`；`MatchedPasswordDialog.axaml.cs:87-92`(new PathIcon 对象初始化器)；`ResultTreeView.axaml.cs:246,257`(StateIconPath)。全部改设 `SourceKey`；元素 `x:Name` 类型改 `IconView`。
  - **`Controls/ArchiveContentView.Events.cs`（原计划写 `MainWindow.axaml.cs:1579-1623`，启动优化重构后已搬到此处；T6 迁移后必坏且编译不报错）**：`:91` `GetColumnHeaderIcon` 的 `panel.Children.OfType<PathIcon>().FirstOrDefault()` 对 `IconView` 永假 → 列头右键菜单图标消失。改为 `GetColumnHeaderIconSource(DataGridColumn) → string?`（`OfType<IconView>().FirstOrDefault()?.SourceKey`）；`:53` 的 `new PathIcon { Data=…, Width=10, Height=10, Foreground=GetThemeBrush(…) }` 改为 `new IconView { SourceKey=…, Width=10, Height=10 }`（**删掉 `Foreground=GetThemeBrush`**，前景色交 ControlTheme，否则切主题不跟随）。方法改 `internal static` 以便测试。
  - `MainWindowViewModel`：`_passwordStatusIcon`（`MainWindowViewModel.cs:360`）/`_toolbarPasswordIcon`（`MainWindowViewModel.Password.cs:83`）由 `object?`(Geometry) 改 `string?`(键值)，赋值点 `MainWindowViewModel.cs:1068` + `Password.cs:65/70/108/116/124` 同步去 `FindResource`（`HasPasswordStatus`(`:363`) 语义不变）。
  - **测试**：构造 `MatchedPasswordDialog`/`ResultTreeView` 等，无「Unable to find resource」异常；VM 设密码态后 `ToolbarPasswordIcon == "IconLockOpen"`；`GetColumnHeaderIconSource` 对含 `IconView` 的 `StackPanel` 表头返回非空键字符串（防列头菜单图标回归）。commit。

- [ ] **T8 — 删除 `GeometryResourceConverter`（含 6 处 XAML 声明）**
  - T7 完成后确认零引用，删除 `Converters/GeometryResourceConverter.cs` + 全部 6 处声明：`App.axaml:15`、`ResultTreeView.axaml:8`、`QuickPathControl.axaml:9`、`ExtractSettingsWindow.axaml:17`、`IconTestWindow.axaml:16`、`ProgressWindow.axaml:29`（该处 key 名是 `GeometryResource`，**不是** `GeometryResourceConverter`）。**漏删任一声明 → XAML 编译报错**，故声明清单逐项勾销。
  - **门禁**：`git grep -n 'GeometryResourceConverter\|GeometryResource"' -- src` = 0 命中。commit。

- [ ] **T9 — 测试桩图标合并改挂 `Icons.A.axaml` + 应用级资源守卫支持 DynamicResource**
  - 4 处 `EnsureIconResources()` 的 URI 由 `AppIcons.axaml` 改为 `Icons.A.axaml`（`ExtractPreserveFullPathPreviewMatchTests.cs:273`、`PickerOptionsRegionTests.cs:49`、`PickerOptionsRegionLayoutTests.cs:48`、`ResultTreeViewFilterTests.cs:43`）。
  - **第 5 处更关键**：`MainArchiveContentLazyLoadTests.cs` 的 `AppLevelResourceKeys()`(`:216-234`) 仍读 `AppIcons.axaml` → 迁移后键集来源错位（图标键将来自 `Icons.A.axaml`），必须改字典来源；同时三个守卫 `ReferencedStaticResources()` 只匹配 `{StaticResource key}`，图标迁成 `DynamicResource` 后**对图标彻底归零（假绿）** → 扩成同时匹配 `\{StaticResource\s|\{DynamicResource\s`，并加一条「图标键（`Icon*`）**必须**用 DynamicResource」的反向断言。
  - **测试**：`dotnet test tests\MantisZip.UI.Avalonia.Tests` 全绿。commit。

- [ ] **T9b — `verify-repo.ps1` 新增第五条守卫（图标引用守卫）**
  - 在 `scripts/verify-repo.ps1` 的 `Test-Guard*` 家族里新增 `Test-GuardIconRefs`（与既有 keybindings / i18n-bindings / filepicker / icons 并列，`-Task check` 与 CI 自动覆盖；若 `intent-execution-seam` 计划先落地了它的 `dialog-seam` 守卫，本条顺延为第六条，不要互相覆盖）：
    ① `src` 下 `.axaml` 不得出现 `PathIcon Data="{StaticResource Icon`（残留静态图标引用）；
    ② `.cs` 不得出现 `new PathIcon|OfType<PathIcon>|FindControl<PathIcon>|Content: PathIcon`；
    ③ `.axaml` 里 `{DynamicResource Icon…}` 的键必须存在于 `Icons.A.axaml`（**这是当前唯一能抓住「图标键名拼错→静默空白」的门禁**；`TryGetResource` 失败不抛异常）；
    ④ `AppIcons.axaml` 的键集 ⊆ `Icons.A.axaml`。
  - `-Json` 输出结构与既有守卫一致（pad/ok/messages），`docs/MAINTENANCE.md` 的守卫清单同步加一行（AGENTS「唯一实现」约定要求文档跟着改）。
  - **测试/验收**：`pwsh -File scripts\verify-repo.ps1 -Task check -Json` → ok:true 且五条守卫全部出现；故意插入一条错误键名跑一次确认能报错（手工验证后还原）。commit。

- [ ] **T10 — 文档与验收**
  - `docs/PLAN.md` 校对 P2 区 3 行（本文 + 计划 2/3）：数字 `215→224`、键 `116→117`、工时、守卫说明，格式对齐既有表。
  - `AGENTS.md` **规则 4 与规则 8 必须同步改**：规则 4 的「`PathIcon` 不继承父控件 Foreground / 必须显式设置」改为针对 `IconView`（前景色由 ControlTheme 提供，**不要**再手写 `Foreground`），规则 8 的「新增图标注册到 `AppIcons.axaml` + `IconTestViewModel`」改为「新增图标同时进 `AppIcons.axaml`（IconGen 输入）与 `Icons.A.axaml`（运行时字典）」——**不改这两条，后续 agent 会照着旧规则重新引入 `PathIcon`**。`docs/ARCHITECTURE.md` 的图标章节同步。
  - `docs/progress-avalonia-detail.md` 以**执行当日日期**追加条目（规则 3；每个 T 任务 commit 前都要补对应条目，不是全部堆到 T10）。
  - 手动 QA：浅色/深色/选中态全 UI 截图对照（A 套零视觉差异，重点覆盖 `ToggleButton:checked` 工具栏选中态、**DataGrid 列头右键菜单图标**（`ArchiveContentView.Events.cs` 补漏点）、以及显式设 `Foreground` 的站点、非正方形图标如 `IconEmptyFolder` 的等比缩放）；构建 0 错误。commit。

## 6. 涉及文件

**新增**
- `tools/IconGen/IconGen.csproj`、`tools/IconGen/Program.cs`
- `src/MantisZip.UI.Avalonia/Resources/Icons/Icons.A.axaml`
- `src/MantisZip.UI.Avalonia/Resources/Icons/IconViewStyles.axaml`（ControlTheme，T4）
- `src/MantisZip.UI.Avalonia/Controls/IconView.cs`
- `src/MantisZip.UI.Avalonia/IconThemeAssembler.cs`（`Assemble` + `ShouldSkip`，T2；放根目录与 `App.*.cs` 同级，或 `Services/` 下由实现者定，不引依赖）
- 测试：IconGen 同步测试、`IconView` 渲染测试、`IconThemeAssembler`/`ShouldSkip` 装配测试、`verify-repo.ps1` 图标引用守卫（脚本内，见 T9b）

**修改**
- `src/MantisZip.UI.Avalonia/App.axaml`（**移除**图标字典静态 merge（§4.7 (a)）、合并 `IconViewStyles.axaml`、converter 声明删除、选中态选择器（**新增 1 条** `ToggleButton:checked controls:IconView`）、TabIcon 模板）
- `src/MantisZip.UI.Avalonia/App.axaml.cs`（`IconsAUri` 新增 + `IconResourcesUri` 删除、`_loadedThemeUri` 初值改 `null`（§4.4 note 1）、`_iconThemeProviders` 缓存、`MergedDictionaries[1]` 契约注释改写、helpers）
- `src/MantisZip.UI.Avalonia/App.Theming.cs`（`ApplyTheme()` 改为调 `IconThemeAssembler.ShouldSkip`/`Assemble`，`_cachedIconProvider` → `_iconThemeProviders`）
- `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`（`SelectedIconTheme`）
- `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`（`_passwordStatusIcon` 类型 + 1 处赋值点）
- `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Password.cs`（`_toolbarPasswordIcon` 类型 + 5 处赋值点）
- 消费面 **24 个 `.axaml`（224 − App.axaml 1 = 223）+ `App.axaml`**：UiTestWindow 26 / TestMenuView 26 / ArchiveContentView 22 / PreviewPanel 17 / MainWindow 15 / ProgressWindow 11 / SettingsWindow 11 / EditMenuItems 9 / MainFilterBar 9 / FileMenuItems 8 / PasswordManagerWindow 8 / CustomFilePickerDialog 8 / ViewMenuItems 7 / ResultTreeView 7 / ConflictDialog 7 / QuickPathControl 6 / CompressConflictDialog 6 / FavoriteManagerWindow 5 / QuickPathPicker 4 / MatchedPasswordDialog 4 / ToolsMenuItems 3 / ExtractSettingsWindow 2 / IconTestWindow 1 / PasswordDialog 1 = **223**；再加 `App.axaml:280` TabIcon 1 处 = **224**
- **5 个 `.axaml.cs`**：ProgressWindow / PasswordManagerWindow / MatchedPasswordDialog / ResultTreeView / **`Controls/ArchiveContentView.Events.cs`**（列头图标动态创建点，原计划误记为 `MainWindow.axaml.cs`）
- `tests/MantisZip.UI.Avalonia.Tests/{ExtractPreserveFullPathPreviewMatchTests, PickerOptionsRegionTests, PickerOptionsRegionLayoutTests, ResultTreeViewFilterTests}.cs`
- `tests/MantisZip.UI.Avalonia.Tests/MainArchiveContentLazyLoadTests.cs`（`AppLevelResourceKeys` 换字典来源 + 三守卫支持 `DynamicResource`，T9）
- `scripts/verify-repo.ps1`（第五条守卫）、`docs/MAINTENANCE.md`（守卫清单一行）
- `AGENTS.md`（规则 4 / 规则 8 的 `PathIcon` 表述改为 `IconView`，T10）、`docs/ARCHITECTURE.md`（图标章节）
- `docs/PLAN.md`、`docs/progress-avalonia-detail.md`

**删除**
- `src/MantisZip.UI.Avalonia/Converters/GeometryResourceConverter.cs`（含 6 处 XAML 声明，见 T8）
- `src/MantisZip.UI.Avalonia/Converters/BoolToIconConverter.cs`（已零引用，T5）

## 7. 验证

```powershell
# ① 首选：仓库自检（含新增图标引用守卫，T9b 完成后这一步就能兜住绝大部分回归）
pwsh -File scripts\verify-repo.ps1 -Task check
pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia
pwsh -File scripts\verify-repo.ps1 -Task test  -Target ui-tests
pwsh -File scripts\verify-repo.ps1 -Task test  -Target tests

# ② 兜底人工核对（守卫未覆盖或 guard 脚本异常时）
git grep -n 'PathIcon Data="{StaticResource Icon' -- 'src/*.axaml'                 # 期望 0
git grep -n -E 'new PathIcon|OfType<PathIcon>|FindControl<PathIcon>|Content: PathIcon' -- 'src/*.cs'  # 期望 0
git grep -n 'GeometryResourceConverter' -- 'src/*'                                 # 期望 0
git grep -n '\bBoolToIconConverter\b' -- 'src/*'                                   # 期望 0
git grep -c 'DynamicResource Icon' -- 'src/*.axaml'                                # 期望 ≈213（= 迁移前的静态数）
```

- **图标键名正确性**是唯一无法靠编译器发现的点（`TryGetResource` 失败只是空白，不抛异常）：必须由 T9b 的 `Test-GuardIconRefs` ③（`{DynamicResource Icon…}` 键 ⊆ `Icons.A.axaml`）覆盖，**不得只靠手工目检**（224 处目检不可能穷尽）。
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
| **首帧图标全空**（最易漏）：按 §4.7 (a) 去掉 `App.axaml` 静态 merge 后，若哨兵初值仍是「Light + "A"」，默认组合首次 `ApplyTheme()` 被早退 return → **浅色用户首帧无图标、深色用户完全正常** | §4.4 note 1：两个哨兵初值改 `null` + `ShouldSkip` 双非空判定；T2 ④ `ShouldSkip(null, LightUri, null, "A") == false`；DoD #7 首帧图标非空 |
| **缩放语义错**：按 X/Y 独立拉伸 → 非正方形图标（`IconEmptyFolder`、chevron）被拉扁 | §4.3 明确 `scale = min(Bounds.W/drawing.W, Bounds.H/drawing.H)` 等比例居中；T3 ⑤ 非正方形图等比断言；DoD #5 |
| **尺寸兜底缺失**：自绘 `Control` 的 `MeasureOverride` 默认返回 `0×0` → 无 ControlTheme 的环境里是隐形控件 | §4.3 `MeasureOverride` 兜底 20×20（与 ControlTheme 双保险）；T4 ② 断言（测试宿主恰好没有 FluentTheme，等于天然覆盖「主题资源缺失」路径） |
| **图标键名拼错 = 静默空白**：`TryGetResource`/`DynamicResource` 找不到键不抛异常；迁成 DynamicResource 后现有三守卫对图标归零（假绿） | T9b 新增 `verify-repo.ps1` 第五条守卫：`{DynamicResource Icon…}` 的键 ⊆ `Icons.A.axaml`；T9 把 `ReferencedStaticResources()` 扩成同时认 `DynamicResource`；§7 以 guards 为主、手工 grep 仅兜底 |
| **测试宿主缺资源**：`TestAppBuilder` 是裸 `Application`（无 App.axaml / ControlTheme / ThemeLight / Fluent 资源）→ T2/T4 按原方案必然红 | ControlTheme 外置 `IconViewStyles.axaml`（§4.6）+ 装配抽 `IconThemeAssembler` 纯函数（T2）；**禁止**测试里 `new ControlTheme` 手搓 |
| **测试污染开发机配置**：`AppSettings.Load()` 是进程级缓存、`Save()` 写真实用户目录且路径不可注入 | T2 断言走纯函数、不调 `Save()`；必要时用既有 `ResetCacheForTests()` 收尾 |
| 213 处静态（23 文件）+ 8 处 converter + 5 个 `.axaml.cs` 机械替换遗漏 / 改坏属性 | 分文件 review + 残留门禁 grep（**UiTestWindow 26 处、TestMenuView 26 处**最易漏）+ 迁移前后截图对照 |
| `.cs` 侧 PathIcon 站点漏迁（编译通过、运行期静默失效） | §3 已补 `Controls/ArchiveContentView.Events.cs:53/91`（原计划误记在 `MainWindow.axaml.cs`）；§7 增 `.cs` 门禁（`new PathIcon`/`OfType<PathIcon>`/`FindControl<PathIcon>`/`Content: PathIcon` = 0）；T7 增列头菜单回归测试 |
| `IconView` 前景色语义与 `PathIcon` 不一致 | 已对齐：ControlTheme 默认 `Foreground={DynamicResource TextControlForeground}`（与 Fluent PathIcon 同 key/同优先级），祖先继承实际不生效（与 PathIcon 行为一致）；`ToggleButton:checked controls:IconView` 选择器可压过 ControlTheme（同现有 PathIcon 机制）；T4 断言解析值 + A 套截图兜底 |
| `SourceKey` 订阅泄漏 | `OnDetachedFromVisualTree` 必须 `Dispose` 订阅；测试覆盖 attach/detach/re-attach |
| ControlTheme setter 与 Style 选择器优先级判断错误 | 与 Fluent `PathIcon` 同机制（ControlTheme 给默认值、`Application.Styles` 选择器覆盖）；T4 断言未设值时的解析结果，手动 QA 覆盖选中态 |
| 键冲突（AppIcons 与 Icons.A 同名） | 运行时**只** merge Icons.A 三层，绝不 merge AppIcons |
| `ApplyTheme` 同主题早退吞掉图标主题切换 | 早退条件改双条件 + 非空判定（`_loadedThemeUri` + `_loadedIconThemeId`）；T2 ②④ 覆盖「基础主题不变、仅图标 id 变」与「首帧」两条路径 |
| 行号漂移导致漏站 | §3 行号为 **2026-10-24 实测**快照（仍会因后续重构漂移）；执行时以锚点符号（x:Name、方法名、模式）为准，T0 先重跑 grep 校准 |
| **启动劣化**：图标字典在 App.axaml 静态 merge 与 ApplyTheme() 各解析一次 → 启动 +33ms；切主题若未命中缓存 → 每次换肤 +33ms | §4.7 硬约束 1/2：启动解析次数 = 1（去掉静态 merge 或复用同一 provider 实例）+ `_iconThemeProviders` 缓存必须命中；§7 启动 A/B 门控 |
| **`DrawingImage` 字典解析成本未测**（现有 32.9ms 是 `Geometry` 字典的数） | T0 补探针；若 >60ms 先报数再决定是否放宽 20ms 门控，不得静默通过 |
| **IconView 控件化开销**：224 个实例若引入模板匹配/可视图 → Win.Xaml（当前 ~330ms）明显劣化 | §4.7 硬约束 3：ControlTheme 仅 3 个 Setter、无 Template；实测控件级开销 ≤10ms |
| **RELEASE_NOTES 范围**（规则 18） | 计划完成时按「已发布版本之间的用户可见差异」判定（A 套零视觉差异 → 大概率无需用户可见条目），**必须先向用户确认**再动 |
