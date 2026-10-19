# 图标主题系统 — 计划 1：基础设施（IconView + DrawingImage 契约迁移 + 三层字典骨架）

> **状态**: 📋 未开始 | **创建**: 2026-10-15 | **最近复核**: 2026-10-19（全仓痕迹核对，§3 数字为当日实况） | **优先级**: P2 | **预估工时**: 14-18h
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
| **D-B** | §4.3：`Foreground: IBrush?` StyledProperty「注册 `inherits:true`」，null 回退 `ThemeTextPrimaryBrush` | `IconView.Foreground` **复用** `TextElement.ForegroundProperty`（`AddOwner<IconView>(new StyledPropertyMetadata<IBrush?>(null))`），默认值 per-owner 覆盖为 `null` | §4.3/§10 要求「像 TextBlock 一样继承父级文字色」。Avalonia 继承按 **property 实例**解析（`ValueStore.GetValue` 以 `_effectiveValues` 即属性实例为键），**只有复用同一实例**才能真正继承祖先的 `TextElement.Foreground`；自注册的新属性（含 `inherits:true`）无法跨实例继承，达不到规格意图。默认值经 per-owner `StyledPropertyMetadata<IBrush?>` 的 `DefaultValue` 覆盖为 `null` 后，渲染回退 `TextControlForeground`（经资源订阅缓存；与 Fluent PathIcon 对齐，见下方框架事实第 5 条），语义与 §4.3 一致。 |

> 关键框架事实（2026-10-15 源码核实，作为上表依据）：
> - `TextElement.ForegroundProperty = AvaloniaProperty.RegisterAttached<TextElement, TextElement, IBrush?>(nameof(Foreground), Brushes.Black, inherits: true)`（`src/Avalonia.Controls/Documents/TextElement.cs:68`）。
> - `StyledProperty<TValue>.AddOwner<TOwner>(metadata)` 内部 `AvaloniaPropertyRegistry.Register(owner, this)` + `OverrideMetadata<TOwner>(metadata)`，**返回 `this`（同一实例）**（`src/Avalonia.Base/StyledProperty.cs:59`）。故 `TemplatedControl/TextBlock/ContentPresenter.ForegroundProperty` 与 `TextElement.ForegroundProperty` 引用相同。
> - 值优先级（`BindingPriority`，数值越小优先级越高）：`Animation=-1 < LocalValue=0 < StyleTrigger=1 < Template=2 < Style=3 < Inherited=4 < Unset=int.MaxValue`（`src/Avalonia.Base/Data/BindingPriority.cs`）。即 **Style/ControlTheme setter（≤3）会压过继承值（4）** —— 这是 PathIcon「显式选择器才上色」与本案「`ToggleButton:checked IconView` 选择器仍生效」的机制根源。
> - `ResourceNodeExtensions.GetResourceObservable(this IResourceHost, object key, Func<object?,object?>? converter = null)`（`src/Avalonia.Base/Controls/ResourceNodeExtensions.cs:104`）。
> - 当前 `PathIcon` 的默认 `Foreground` **不是** `Brushes.Black`，而是 Fluent ControlTheme 的 `{DynamicResource TextControlForeground}`（`Avalonia.Themes.Fluent` `Controls/PathIcon.xaml`，本项目 Avalonia 12.1.2 已核实）。本项目 `ThemeLight/Dark.axaml` 里 `TextControlForeground` 与 `ThemeTextPrimaryBrush` 同源（都取 `ThemeTextPrimary`），故回退二者视觉等价；**IconView 回退统一用 `TextControlForeground`**，与 PathIcon 语义逐字对齐。`IconElementThemeWidth/Height` 存在于 Fluent `Accents/BaseResources.xaml`（值 20/20），T4 的 ControlTheme 引用成立。
> - 合并字典解析 **倒序遍历，last-wins**（`ResourceDictionary.cs:221`）→ 三层按 ①→②→③ 顺序 Add 即可让③压②、②压①。

## 3. 现状核实（2026-10-19 复核，全仓 grep 实测）

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
| converter 声明 | **6 处**：`App.axaml:15` / `ResultTreeView.axaml:8` / `QuickPathControl.axaml:9` / `ExtractSettingsWindow.axaml:17` / `IconTestWindow.axaml:16` / `ProgressWindow.axaml:29`（key=`"GeometryResource"`）——删 `GeometryResourceConverter.cs` 时须同删，否则 6 个 XAML 编译报错 | 全仓 |
| BoolToIconConverter | **已零引用**（2026-10-15 版普查「ProgressWindow.axaml:20/140」已过期：ProgressWindow 眼睛切换早已改为 flyout + code-behind `FindResource`）。T5 不再新增 Key 版 converter，直接删除即可 | `Converters/BoolToIconConverter.cs` |
| C# 动态设置 | `ProgressWindow.axaml.cs:238`(CancelButtonIcon) / `415`(PauseButtonIcon) / `564`(PauseButtonIcon) / `723-724` + `746-752`(PwdFlyoutEyeIcon，`FindControl<PathIcon>` 得句柄) / `838-849`(**`sender is Button { Content: PathIcon copyIcon }` 模式匹配，改 IconView 时连类型模式一起改**) / `885`,`891`(PauseButtonIcon)；`PasswordManagerWindow.axaml.cs:62-63`；`MatchedPasswordDialog.axaml.cs:89-92`(new PathIcon 对象初始化器 + Foreground)；`ResultTreeView.axaml.cs:257`(StateIconPath) | 全仓 |
| VM 图标属性 | `MainWindowViewModel._passwordStatusIcon`（`:384`）、`_toolbarPasswordIcon`（`:1196`），赋值点 `1178/1183/1221/1229/1237/2263`；`app?.FindResource("IconKey/IconLockClosed/IconLockOpen")` 得 `Geometry` | `MainWindow.axaml:640,1344` 消费 |
| 缺 `xmlns:controls` 的消费文件 | **11 个**：ResultTreeView / PreviewPanel / PasswordDialog / MainWindow / CompressConflictDialog / ConflictDialog / FavoriteManagerWindow / IconTestWindow / MatchedPasswordDialog / PasswordManagerWindow / ProgressWindow（UiTestWindow / 其余 6 个已有） | — |
| 主题装配 | `App.axaml.cs`：`IconResourcesUri`(L35，**迁移时删除/替换**)、`ApplyTheme()`(L516-539：`Resources.MergedDictionaries.Clear()` → 加主题 → 加图标 → `RequestedThemeVariant`)、调用点 L70/L79/L558 | `App.axaml.cs` |
| 静态字典 | `App.axaml:7-14` MergedDictionaries = ThemeLight + AppIcons；`App.axaml:15-16` 全局 converter 声明 | `App.axaml` |
| 选中态样式 | `App.axaml` **3 个选择器**都要扩：`:140` `ToggleButton:checked PathIcon`、`:143` `:pointerover`、`:147` `:pressed`（含上方注释说明 PathIcon 不继承） | `App.axaml` |
| 设置 | `AppSettings` 为纯 POCO，外观节，`Load()`/`Save()` | `Models/AppSettings.cs` |
| 测试桩 | `TestAppBuilder`：`UseSkia().UseHeadless(new(){ UseHeadlessDrawing=false })` → 真实像素渲染可行 | `tests/…/TestAppBuilder.cs` |
| 测试图标合并 | 4 处 `EnsureIconResources()` 直接 `AvaloniaXamlLoader.Load(avares://…/Resources/Icons/AppIcons.axaml)` 并并入 `Application.Current.Resources`：`ExtractPreserveFullPathPreviewMatchTests.cs:273`、`PickerOptionsRegionTests.cs:49`、`PickerOptionsRegionLayoutTests.cs:48`、`ResultTreeViewFilterTests.cs:43` | tests |
| 覆盖率契约 | `IconTestViewModel.LoadAllIcons()`（约 100+ 条 `Add(category, semanticName, emoji, resourceKey, status, location, notes)`）——AGENTS 规则 8 强制维护 | `ViewModels/IconTestViewModel.cs` |
| 版本/InternalsVisibleTo | 无 `tools/` 目录；csproj `InternalsVisibleTo` = `MantisZip.Tests` + `MantisZip.UI.Avalonia.Tests` | `MantisZip.UI.Avalonia.csproj:17,19` |

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
- `Foreground: IBrush?` —— 复用 `TextElement.ForegroundProperty`，per-owner 默认值覆盖为 `null`；null 时回退 `TextControlForeground`（`GetResourceObservable("TextControlForeground")` 缓存；与 Fluent PathIcon 默认 Foreground 同 key，语义逐字对齐）。
- `Stretch: Stretch`（默认 `Uniform`）—— 尺寸对齐 `PathIcon`（均匀缩放、居中、不裁剪）。
- `Render`：展平 `Source.Drawing`（`GeometryDrawing` / `DrawingGroup.Children` 递归），部件 `Brush == null` 用 `Foreground`，否则用部件 Brush；按整体 `Drawing.Bounds` 均匀缩放平移到 `Bounds`。

### 4.4 三层字典 + `ApplyTheme` 装配

`AppSettings` 外观节新增（默认 `"A"`）：

```csharp
/// <summary>图标风格主题：A=现状默认 / C/D/E/F/G=内置套 / 导入包 id。仅影响应用内矢量图标。</summary>
public string SelectedIconTheme { get; set; } = "A";
```

`App.axaml.cs` 新增常量与装配方法；`ApplyTheme()` 在既有「加主题字典」之后，按 ①→②→③ 顺序：

```csharp
private const string IconsAUri = "avares://MantisZip.UI.Avalonia/Resources/Icons/Icons.A.axaml";

// ApplyTheme() 内、加完 theme 字典之后：
var settings = AppSettings.Load();
// ① 默认 A（永远垫底，缺键兜底 → 永不空白）
AddMergedDictionary(IconsAUri);
// ② 当前主题层（计划 1 仅 "A"；未知/异常 id 静默回退 A）
AddMergedDictionary(ResolveIconThemeUri(settings.SelectedIconTheme));
// ③ Override 层（计划 1 为空占位；计划 2 填 icon-overrides）
AddIconOverrideLayer();   // 计划 1：空实现（不 Add 任何字典）

// helpers
private void AddMergedDictionary(string uri)
{
    if (AvaloniaXamlLoader.Load(new Uri(uri)) is IResourceProvider p)
        Resources.MergedDictionaries.Add(p);
}
private static string ResolveIconThemeUri(string? id) => id switch
{
    // 计划 2/3 扩充：C…G、导入包 id
    _ => IconsAUri, // 未知/异常 → 静默回退 A（§8：启动路径不弹窗）
};
private static void AddIconOverrideLayer() { /* 计划 2 实现 */ }
```

同时：
1. 把 `App.axaml:12` 的静态 merge 由 `AppIcons.axaml` 改为 `Icons.A.axaml`（设计期/首帧需要；运行时由 `ApplyTheme` 重建）。
2. **删除** `App.axaml.cs:35` 的旧常量 `IconResourcesUri`（其引用点即 `ApplyTheme()` 内"Re-add icon resources"段 L536-539，被本计划的三层装配替代），避免死代码 / 双常量漂移。

### 4.5 消费端迁移（机械替换）

| 现状 | 迁移后 |
|------|--------|
| `<PathIcon Data="{StaticResource IconXxx}" …/>` | `<controls:IconView Source="{DynamicResource IconXxx}" …/>`（保留其余属性；`Width`/`Height`/`Foreground`/`Classes` 原样） |
| `Data="{Binding X, Converter={StaticResource GeometryResourceConverter}}"` | `SourceKey="{Binding X}"` |
| `Data="{Binding ToolbarPasswordIcon}"` / `Data="{Binding PasswordStatusIcon}"` | `SourceKey="{Binding …}"`（配合 VM 属性改存键字符串） |
| `Data="{Binding StatusIconKey, Converter={StaticResource GeometryResource}}"`（ProgressWindow:888） | `SourceKey="{Binding StatusIconKey}"`（VM 已经是键字符串，无缝） |
| C# `pathIcon.Data = (Geometry?)FindResource("IconX")` | `iconView.SourceKey = "IconX"` |
| VM `object?` 存 `Geometry` | VM `string?` 存键名（`"IconKey"`/`"IconLockClosed"`/`"IconLockOpen"`），XAML 绑 `SourceKey` |
| `App.axaml` 选中态 3 个选择器（`:140` checked / `:143` pointerover / `:147` pressed） | 各扩一条**并列 `<Style>`**：原 PathIcon 选择器保留，旁增同形态 `controls:IconView` 选择器（setter 与原条目相同，均为 `Foreground=White`）。Avalonia 对单条 Style 内逗号分组支持不明，不依赖；不合并选择器 |

- converter：完成 6 处绑定迁移后，**删除** `Converters/GeometryResourceConverter.cs` 及全部 **6 处声明**（§3 表）；**`Converters/BoolToIconConverter.cs` 当前已零引用，直接删除、不新增 Key 版**（2026-10-15 版计划的 `BoolToIconKeyConverter` 随普查过期一并取消）。
- 11 个缺命名空间的文件补 `xmlns:controls="clr-namespace:MantisZip.UI.Avalonia.Controls"`。
- `IconTestWindow.axaml:89`（Debug 工具）改用 `<controls:IconView SourceKey="{Binding ResourceKey}" …/>`。
- `ProgressWindow.axaml.cs:838-849` 的 `sender is Button { Content: PathIcon copyIcon }` 类型模式与 `:723/746` 的 `FindControl<PathIcon>("PwdFlyoutEyeIcon")` 须同步改 `IconView`（元素标签迁移后 old 模式永假，属编译外静默 bug）。

### 4.6 IconView 默认样式（ControlTheme）

`App.axaml` 资源内新增（供 `IconView` 实例自动套用，尺寸与 `PathIcon` 默认一致）：

```xml
<!-- IconView 默认尺寸：与 Fluent PathIcon 一致（20×20）；局部 Width/Height 可覆盖 -->
<ControlTheme x:Key="{x:Type controls:IconView}" TargetType="controls:IconView">
  <Setter Property="Width" Value="{DynamicResource IconElementThemeWidth}" />
  <Setter Property="Height" Value="{DynamicResource IconElementThemeHeight}" />
</ControlTheme>
```

## 5. 任务分解（TDD；每任务一个 commit）

> 规则：每个任务先写失败测试 → 实现 → 测试转绿 → `dotnet build`/`dotnet test` → conventional commit（规则 10）。

- [ ] **T1 — `tools/IconGen` 生成 `Icons.A.axaml`**
  - 新建 `tools/IconGen/IconGen.csproj`（net10.0 console，不入 sln）+ `Program.cs`：`XDocument` 读 `AppIcons.axaml`，按序输出 `Icons.A.axaml`（`GeometryDrawing` 无 Brush）。注意部分 d 串带 fill-rule 前缀（如 `IconEmptyFolder` 的 `F0 `），**整串原样写入**，不得裁剪/归一化空白。
  - **测试（`MantisZip.Tests` 或 `MantisZip.UI.Avalonia.Tests`，纯逻辑）**：`AppIcons` 键集 == `Icons.A` 键集；每键 Geometry 字符串（原样 trim 后）完全相等。测试不运行 IconGen 进程，直接读取两文件比对。
  - 运行 `dotnet run --project tools/IconGen` 生成并入库 `Icons.A.axaml`。commit。

- [ ] **T2 — 三层字典 + `SelectedIconTheme`（无 UI）**
  - `AppSettings` 加 `SelectedIconTheme`（默认 `"A"`）；`App.axaml.cs` 加 `IconsAUri` + `AddMergedDictionary`/`ResolveIconThemeUri`/`AddIconOverrideLayer`（空），`ApplyTheme()` 末尾按 ①→②→③ 装配；`App.axaml:12` 改挂 `Icons.A.axaml`。
  - **测试（Avalonia headless）**：设 `SelectedIconTheme="A"` → `ApplyTheme` 后 `Application.Current.Resources.TryGetResource("IconFolder", null, out var r)` 命中且 `r is DrawingImage`；未知 id（`"ZZZ"`）→ 仍命中 A（静默回退）。commit。

- [ ] **T3 — `IconView` 控件**
  - `Controls/IconView.cs`：`Source`/`SourceKey`/`Foreground`(AddOwner+null 默认)/`Stretch`；`Render` 展平 + 均匀缩放 + null 部件用 Foreground；`OnAttachedToVisualTree` 订阅 `TextControlForeground` 与 `SourceKey`（自定义 `IObserver<T>`，不引 System.Reactive），`OnDetachedFromVisualTree` 释放；向前兼容「attach 前 SourceKey 已赋值」与「detach 后 re-attach」两种时序（attach 时无条件重建订阅）。
  - **测试（headless 真实像素）**：① `UseHeadlessDrawing=false` 下渲染 `DrawingImage`（单 GeometryDrawing 无 Brush）+ `Foreground=Red` → `RenderTargetBitmap` 命中红色像素；② 同图 `Foreground=Blue` → 蓝色；③ 部件带 baked `SolidColorBrush(Green)` → 忽略 Foreground 保持绿；④ `SourceKey` 指向 `Application` 资源 → 渲染非空，替换资源后重渲染像素变化（订阅刷新）。commit。

- [ ] **T4 — ControlTheme + 选中态样式**
  - `App.axaml` 加 `IconView` ControlTheme（4.6）+ 为 3 个选中态选择器各旁增一条 `controls:IconView` 并列 Style（`:140` checked / `:pointerover` / `:pressed`，setter 与原 PathIcon 条目相同）（4.5）。
  - **测试**：`IconView` 未设尺寸时 `DesiredSize` ≈ 20×20（读 `IconElementThemeWidth/Height`，Fluent `BaseResources` 已存在该键）。commit。

- [ ] **T5 — 删除已零引用的 `BoolToIconConverter`（普查更新，2026-10-15 版「新增 Key 版 converter」作废）**
  - 普查核实 `BoolToIconConverter` 全仓唯一出现处是自身定义文件（ProgressWindow 眼睛切换早已改为 code-behind `FindResource`）。**不新增** `BoolToIconKeyConverter`；直接删除 `Converters/BoolToIconConverter.cs`。
  - **门禁**：`git grep -n '\bBoolToIconConverter\b' -- src` = 0 命中。commit。

- [ ] **T6 — 静态消费面迁移（206 处 → `IconView`）**
  - 覆盖全部 16 个静态文件（含 **UiTestWindow 26 处**——Debug 专项窗但随正式构建编译）；按文件逐批：标签替换 + `StaticResource`→`DynamicResource` + 补 `xmlns:controls`（11 文件，见 §3）。可用脚本辅助，但**必须逐文件 review**（保留 `Width`/`Height`/`Foreground`/`Classes`/内联属性；全量已显式 `Width`，无默认尺寸依赖）。
  - ResultTreeView / QuickPathControl / ExtractSettingsWindow / IconTestWindow / ProgressWindow 内有多静态、converter、VM 绑定混合站点：本任务只处理 `Data="{StaticResource …}"`，动态站点留给 T7，标记避免双改。
  - **门禁测试/检查**：`git grep -n 'PathIcon Data="{StaticResource Icon' -- 'src/*.axaml'` = 0 命中（同时覆盖 UiTestWindow 处）。commit（可分批多次 commit）。

- [ ] **T7 — converter / VM / C# 动态站点迁移（6 converter 绑定 + 2 VM 绑定 + 11 组 C# 站点）**
  - `App.axaml:280` TabIcon → `<controls:IconView SourceKey="{Binding $parent[TabItem].(controls:TabItemExtensions.TabIcon)}" …/>`（保留 `IsVisible`/`Width`/`Height`/`Foreground`）。
  - 其余 5 处 converter 绑定 → `SourceKey`：`ResultTreeView.axaml:125`、`QuickPathControl.axaml:122`、`ExtractSettingsWindow.axaml:63`、`IconTestWindow.axaml:89`、`ProgressWindow.axaml:888`（`StatusIconKey` 本就是键字符串，直接换属性即可）。
  - 2 处 VM 直接绑定：`MainWindow.axaml:640`/`:1344` `Data="{Binding ToolbarPasswordIcon/PasswordStatusIcon}"` → `SourceKey="{Binding …}"`。
  - C# 站点：`ProgressWindow.axaml.cs:238/415/564/885/891`(x:Name 元素处理函数)、`723-724/746-752`(PwdFlyoutEyeIcon：`FindControl<PathIcon>` → `FindControl<IconView>`)、`838-849`(模式匹配 `Button { Content: PathIcon copyIcon }` → `Content: IconView`)；`PasswordManagerWindow.axaml.cs:62-63`；`MatchedPasswordDialog.axaml.cs:89-92`(new PathIcon 对象初始化器)；`ResultTreeView.axaml.cs:257`(StateIconPath)。全部改设 `SourceKey`；元素 `x:Name` 类型改 `IconView`。
  - `MainWindowViewModel`：`_toolbarPasswordIcon`（`:1196`）/`_passwordStatusIcon`（`:384`）由 `object?`(Geometry) 改 `string?`(键值)，赋值点 `1178/1183/1221/1229/1237/2263` 同步去 `FindResource`。
  - **测试**：构造 `MatchedPasswordDialog`/`ResultTreeView` 等，无「Unable to find resource」异常；VM 设密码态后 `ToolbarPasswordIcon == "IconLockOpen"`。commit。

- [ ] **T8 — 删除 `GeometryResourceConverter`（含 6 处 XAML 声明）**
  - T7 完成后确认零引用，删除 `Converters/GeometryResourceConverter.cs` + 全部 6 处声明：`App.axaml:15`、`ResultTreeView.axaml:8`、`QuickPathControl.axaml:9`、`ExtractSettingsWindow.axaml:17`、`IconTestWindow.axaml:16`、`ProgressWindow.axaml:29`（key=`"GeometryResource"`）。**漏删任一声明 → XAVL 编译报错**，故声明清单逐项勾销。
  - **门禁**：`git grep -n 'GeometryResourceConverter\|GeometryResource"' -- src` = 0 命中。commit。

- [ ] **T9 — 测试桩图标合并改挂 `Icons.A.axaml`**
  - 4 处 `EnsureIconResources()` 的 URI 由 `AppIcons.axaml` 改为 `Icons.A.axaml`（`ExtractPreserveFullPathPreviewMatchTests.cs:273`、`PickerOptionsRegionTests.cs:49`、`PickerOptionsRegionLayoutTests.cs:48`、`ResultTreeViewFilterTests.cs:43`）。
  - **测试**：`dotnet test tests\MantisZip.UI.Avalonia.Tests` 全绿（原依赖 `StaticResource Icon` 的对话框测试不再抛异常）。commit。

- [ ] **T10 — 文档与验收**
  - `docs/PLAN.md` 校对 P2 区 3 行（本文 + 计划 2/3，含本修订后的数字/工时），格式对齐既有表。
  - `docs/progress-avalonia-detail.md` 以**执行当日日期**追加条目（规则 3）。
  - 手动 QA：浅色/深色/选中态全 UI 截图对照（A 套零视觉差异，重点覆盖选中态 3 选择器与非默认 `Foreground` 站点）；构建 0 错误。commit。

## 6. 涉及文件

**新增**
- `tools/IconGen/IconGen.csproj`、`tools/IconGen/Program.cs`
- `src/MantisZip.UI.Avalonia/Resources/Icons/Icons.A.axaml`
- `src/MantisZip.UI.Avalonia/Controls/IconView.cs`
- 测试：IconGen 同步测试、`IconView` 渲染测试、（新）图标字典装配测试

**修改**
- `src/MantisZip.UI.Avalonia/App.axaml`（MergedDictionaries、converter 声明、ControlTheme、选中态选择器（3 个）、TabIcon 模板）
- `src/MantisZip.UI.Avalonia/App.axaml.cs`（`ApplyTheme` 三层装配、`IconsAUri` 新增 + `IconResourcesUri` 删除、helpers）
- `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`（`SelectedIconTheme`）
- `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`（2 个图标属性类型 + 6 处赋值点）
- 消费面 17 个 `.axaml`（MainWindow 99 / **UiTestWindow 26** / PreviewPanel 13 / ProgressWindow 11 / PasswordManagerWindow 8 / CustomFilePickerDialog 8 / ConflictDialog 7 / ResultTreeView 7 / SettingsWindow 7 / CompressConflictDialog 6 / FavoriteManagerWindow 5 / QuickPathControl 5 / QuickPathPicker 4 / MatchedPasswordDialog 4 / ExtractSettingsWindow 2 / IconTestWindow 1 / PasswordDialog 1 = 215）+ 4 个 `.axaml.cs`（ProgressWindow / PasswordManagerWindow / MatchedPasswordDialog / ResultTreeView）
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
git grep -n 'GeometryResourceConverter' -- 'src/*'
git grep -n '\bBoolToIconConverter\b' -- 'src/*'
```

- `lsp_diagnostics` 无错误。
- 手动：浅色/深色/选中态逐一目检（A 套零差异）；`SelectedIconTheme` 改值重启后仍指向 A（唯一可选项）。

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
| 206 处静态 + 11 组 C# 站点机械替换遗漏 / 改坏属性 | 分文件 review + 残留门禁 grep（含 UiTestWindow，26 处最易漏）+ 迁移前后截图对照 |
| `Foreground` 回退由 Fluent `TextControlForeground` 改为 IconView 自算 | 已对齐：回退 key 相同（`TextControlForeground`）；仅「无显式 Foreground + 无祖先继承」站点受影响，A 套零差异截图兜底 |
| `SourceKey` 订阅泄漏 | `OnDetachedFromVisualTree` 必须 `Dispose` 订阅；测试覆盖 attach/detach/re-attach |
| `AddOwner` per-owner 元数据覆盖副作用 | 仅影响 `IconView` 类型；`TextElement/TemplatedControl/TextBlock` 默认值不变；测试锁定 `new TextBlock().Foreground != null` |
| 键冲突（AppIcons 与 Icons.A 同名） | 运行时**只** merge Icons.A 三层，绝不 merge AppIcons |
| 行号漂移导致漏站 | §3 行号为 2026-10-19 快照；执行时以锚点符号（x:Name、方法名、模式）为准，动手前重新 grep 校准 |
