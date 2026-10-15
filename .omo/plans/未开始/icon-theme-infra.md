# 图标主题系统 — 计划 1：基础设施（IconView + DrawingImage 契约迁移 + 三层字典骨架）

> **状态**: 📋 未开始 | **创建**: 2026-10-15 | **优先级**: P2 | **预估工时**: 12-16h
> **来源**: [docs/superpowers/specs/2026-10-15-icon-theme-system-design.md](../../docs/superpowers/specs/2026-10-15-icon-theme-system-design.md)（已批准）
> **拆分**: 本系统拆 3 份串行计划 —— **① 基础设施（本文）** → ② 导入管线 + 单图标覆盖 + 设置 UI → ③ IconGen + C/D/E/F/G 内容生产。
> **前置**: 无。**后续**: 计划 2 依赖本文交付的 `IconView` / `SourceKey` / 三层字典契约。

## 1. 目标与交付标准

把应用内图标契约从「`PathIcon` + `Geometry` 资源」升级为「`IconView` + `DrawingImage` 资源」，并搭好三层资源字典骨架（默认 A / 当前主题 / 覆盖）。**本计划不引入任何设置 UI、不做导入、不生产 C-G 套内容。**

**交付标准（DoD）**：

1. 全仓不再有 `<PathIcon Data="{StaticResource Icon…}` 形态（Avalonia 自带控件模板内的 PathIcon 不属于我方代码，不算）。
2. 运行 `dotnet run --project tools/IconGen` 可确定性生成 `Icons.A.axaml`；测试强制其键集与几何和 `AppIcons.axaml` 逐字节同步。
3. 新增 `IconView` 控件，行为与 `PathIcon` 等价（尺寸/均匀缩放/`Foreground` 上色），并额外支持 `SourceKey` 键订阅刷新。
4. `ApplyTheme()` 装配三层图标字典，`SelectedIconTheme`（默认 `"A"`）持久化于 `AppSettings`。
5. **A 套零视觉差异**：迁移后全 UI 与迁移前像素级一致（浅色 + 深色 + 选中态）。
6. 构建 0 错误；`MantisZip.Tests` + `MantisZip.UI.Avalonia.Tests` 全绿。

## 2. 对规格的两处实施偏差（实施阶段可定项，已在计划头显式标注）

规格 §12 声明「实施计划阶段可再定的细节，不需回访设计」。以下两项属该类细节，但与原文字面略有出入，**记录理由供评审**：

| # | 规格原文 | 实施采用 | 理由 |
|---|----------|----------|------|
| **D-A** | §4.4：`GeometryResourceConverter` **重命名** `IconResourceConverter`（键→DrawingImage） | 删除 `GeometryResourceConverter`，改由 `IconView.SourceKey`（内部 `GetResourceObservable` 订阅）承担键→图解析 | converter 输出是一次性求值；字典②层被替换后 binding 源（VM 属性/附加属性）不变 → **不会重算**，违反 §4.2「切主题自动全 UI 刷新」。`SourceKey` 订阅资源变化，字典交换时正确重解析。`BoolToIconConverter` 同步改为输出键字符串。 |
| **D-B** | §4.3：`Foreground: IBrush?` StyledProperty「注册 `inherits:true`」，null 回退 `ThemeTextPrimaryBrush` | `IconView.Foreground` **复用** `TextElement.ForegroundProperty`（`AddOwner<IconView>(new StyledPropertyMetadata<IBrush?>(null))`），默认值 per-owner 覆盖为 `null` | §4.3/§10 要求「像 TextBlock 一样继承父级文字色」。Avalonia 继承按 **property 实例**解析（`ValueStore.GetValue` 以 `_effectiveValues` 即属性实例为键），**只有复用同一实例**才能真正继承祖先的 `TextElement.Foreground`；自注册的新属性（含 `inherits:true`）无法跨实例继承，达不到规格意图。默认值经 `[ShortFact]` 覆盖为 `null` 后，渲染回退 `ThemeTextPrimaryBrush`（经资源订阅缓存），语义与 §4.3 一致。 |

> 关键框架事实（2026-10-15 源码核实，作为上表依据）：
> - `TextElement.ForegroundProperty = AvaloniaProperty.RegisterAttached<TextElement, TextElement, IBrush?>(nameof(Foreground), Brushes.Black, inherits: true)`（`src/Avalonia.Controls/Documents/TextElement.cs:68`）。
> - `StyledProperty<TValue>.AddOwner<TOwner>(metadata)` 内部 `AvaloniaPropertyRegistry.Register(owner, this)` + `OverrideMetadata<TOwner>(metadata)`，**返回 `this`（同一实例）**（`src/Avalonia.Base/StyledProperty.cs:59`）。故 `TemplatedControl/TextBlock/ContentPresenter.ForegroundProperty` 与 `TextElement.ForegroundProperty` 引用相同。
> - 值优先级（`BindingPriority`，数值越小优先级越高）：`Animation=-1 < LocalValue=0 < StyleTrigger=1 < Template=2 < Style=3 < Inherited=4 < Unset=int.MaxValue`（`src/Avalonia.Base/Data/BindingPriority.cs`）。即 **Style/ControlTheme setter（≤3）会压过继承值（4）** —— 这是 PathIcon「显式选择器才上色」与本案「`ToggleButton:checked IconView` 选择器仍生效」的机制根源。
> - `ResourceNodeExtensions.GetResourceObservable(this IResourceHost, object key, Func<object?,object?>? converter = null)`（`src/Avalonia.Base/Controls/ResourceNodeExtensions.cs:104`）。
> - 合并字典解析 **倒序遍历，last-wins**（`ResourceDictionary.cs:221`）→ 三层按 ①→②→③ 顺序 Add 即可让③压②、②压①。

## 3. 现状核实（2026-10-15 实测）

| 项 | 事实 | 位置 |
|----|------|------|
| 图标资源 | `AppIcons.axaml`：**116** 个 `<Geometry x:Key="IconXxx">…</Geometry>`（`IconPascalCase`） | `Resources/Icons/AppIcons.axaml` |
| 资源嵌入方式 | `Resources\**` 全部 `AvaloniaResource`（运行时 `avares://` 加载）；`AvaloniaXaml Include **\*.axaml Exclude Resources\**\*.axaml` → `Resources/Icons/*.axaml` **不**走 XamlIl 预编译 | `MantisZip.UI.Avalonia.csproj:49,54-55` |
| 静态消费面 | `<PathIcon …` 共 **182** 处（MainWindow 99 / PreviewPanel 13 / CustomFilePickerDialog 8 / PasswordManagerWindow 8 / ConflictDialog 7 / ResultTreeView 7 / ProgressWindow 6 / CompressConflictDialog 6 / QuickPathControl 5 / SettingsWindow 5 / FavoriteManagerWindow 5 / QuickPathPicker 4 / MatchedPasswordDialog 4 / ExtractSettingsWindow 2 / PasswordDialog 1 / IconTestWindow 1 / App.axaml 1）；其中 **155/182 带 `Width=`**（27 处依赖默认尺寸 20） | 全仓 |
| 闭合标签 | `</PathIcon>` **零命中** → 全部自闭合，可纯标签替换 | 全仓 |
| converter 消费 | `GeometryResourceConverter`：声明 `App.axaml:15`、`IconTestWindow.axaml:16`；真使用 5 处 = `App.axaml:272`(TabIcon) / `IconTestWindow.axaml:89` / `ResultTreeView.axaml:125` / `QuickPathControl.axaml:122` / `ExtractSettingsWindow.axaml:63` | — |
| converter 消费 | `BoolToIconConverter`：声明 `ProgressWindow.axaml:20`、使用 `ProgressWindow.axaml:140`（`ConverterParameter=IconEye\|IconEyeOff`） | — |
| C# 动态设置 | `ProgressWindow.axaml.cs:154/235/346/395/401`、`PasswordManagerWindow.axaml.cs:62-63`、`MatchedPasswordDialog.axaml.cs:89`、`ResultTreeView.axaml.cs:257`、`MainWindowViewModel.cs:1157-1158/1194/1202/1210` | — |
| VM 图标属性 | `MainWindowViewModel._toolbarPasswordIcon`（`object?`）、`_passwordStatusIcon`（`object?`）——均为 `FindResource` 得到的 `Geometry` | `MainWindow.axaml:641,1341` 消费 |
| 缺 `xmlns:controls` 的消费文件 | PasswordDialog / PreviewPanel / FavoriteManagerWindow / MainWindow / CompressConflictDialog / ResultTreeView / MatchedPasswordDialog / PasswordManagerWindow / ConflictDialog / ProgressWindow（10 个）；其余 9 个消费文件已有 | — |
| 主题装配 | `App.axaml.cs`：`IconResourcesUri`(L35)、`ApplyTheme()`(L498-532：`Resources.MergedDictionaries.Clear()` → 加主题 → 加图标 → `RequestedThemeVariant`)、调用点 L66/L74/L540 | `App.axaml.cs` |
| 静态字典 | `App.axaml:8-13` MergedDictionaries = ThemeLight + AppIcons；`App.axaml:15` 全局 converter 声明 | `App.axaml` |
| 选中态样式 | `App.axaml:132-134` `ToggleButton:checked PathIcon → Foreground=White`（含上方注释说明 PathIcon 不继承） | `App.axaml` |
| 设置 | `AppSettings` 为纯 POCO，外观节 L112-122，`Load()`/`Save()` | `Models/AppSettings.cs` |
| 测试桩 | `TestAppBuilder`：`UseSkia().UseHeadless(new(){ UseHeadlessDrawing=false })` → 真实像素渲染可行 | `tests/…/TestAppBuilder.cs` |
| 测试图标合并 | 4 处 `EnsureIconResources()` 直接 `AvaloniaXamlLoader.Load(avares://…/Resources/Icons/AppIcons.axaml)` 并并入 `Application.Current.Resources` | `ExtractPreserveFullPathPreviewMatchTests.cs:264`、`PickerOptionsRegionTests.cs:40`、`PickerOptionsRegionLayoutTests.cs:39`、`ResultTreeViewFilterTests.cs:37` |
| 覆盖率契约 | `IconTestViewModel.LoadAllIcons()`（约 100+ 条 `Add(category, semanticName, emoji, resourceKey, status, location, notes)`）——AGENTS 规则 8 强制维护 | `ViewModels/IconTestViewModel.cs` |
| 版本/InternalsVisibleTo | 无 `tools/` 目录；csproj `InternalsVisibleTo` = `MantisZip.Tests` + `MantisZip.UI.Avalonia.Tests` | `MantisZip.UI.Avalonia.csproj:17,19` |

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
- `Foreground: IBrush?` —— 复用 `TextElement.ForegroundProperty`，per-owner 默认值覆盖为 `null`；null 时回退 `ThemeTextPrimaryBrush`（`GetResourceObservable("ThemeTextPrimaryBrush")` 缓存）。
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

同时把 `App.axaml:12` 的静态 merge 由 `AppIcons.axaml` 改为 `Icons.A.axaml`（设计期/首帧需要；运行时由 `ApplyTheme` 重建）。

### 4.5 消费端迁移（机械替换）

| 现状 | 迁移后 |
|------|--------|
| `<PathIcon Data="{StaticResource IconXxx}" …/>` | `<controls:IconView Source="{DynamicResource IconXxx}" …/>`（保留其余属性；`Width`/`Height`/`Foreground`/`Classes` 原样） |
| `Data="{Binding X, Converter={StaticResource GeometryResourceConverter}}"` | `SourceKey="{Binding X}"` |
| `Data="{Binding B, Converter={StaticResource BoolToIconConverter}, ConverterParameter=IconEye\|IconEyeOff}"` | `SourceKey="{Binding B, Converter={StaticResource BoolToIconKeyConverter}, ConverterParameter=IconEye\|IconEyeOff}"` |
| C# `pathIcon.Data = (Geometry?)FindResource("IconX")` | `iconView.SourceKey = "IconX"` |
| VM `object?` 存 `Geometry` | VM `string?` 存键名（`"IconKey"`/`"IconLockClosed"`/`"IconLockOpen"`），XAML 绑 `SourceKey` |
| `App.axaml:132` `ToggleButton:checked PathIcon` | 扩为 `ToggleButton:checked PathIcon, ToggleButton:checked controls:IconView` |

- converter：**删除** `Converters/GeometryResourceConverter.cs`，移除 `App.axaml:15` 与 `IconTestWindow.axaml:16` 声明；`BoolToIconConverter.cs` 改为 `BoolToIconKeyConverter`（返回键字符串，不再 `TryGetResource`）。
- 10 个缺命名空间的文件补 `xmlns:controls="clr-namespace:MantisZip.UI.Avalonia.Controls"`。
- `IconTestWindow.axaml:89`（Debug 工具）改用 `<controls:IconView SourceKey="{Binding ResourceKey}" …/>`。

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
  - 新建 `tools/IconGen/IconGen.csproj`（net10.0 console，不入 sln）+ `Program.cs`：`XDocument` 读 `AppIcons.axaml`，按序输出 `Icons.A.axaml`（`GeometryDrawing` 无 Brush）。
  - **测试（`MantisZip.Tests` 或 `MantisZip.UI.Avalonia.Tests`，纯逻辑）**：`AppIcons` 键集 == `Icons.A` 键集；每键几何字符串逐字节相等。测试若需运行 IconGen，改为直接读取两文件比对（不走进程）。
  - 运行 `dotnet run --project tools/IconGen` 生成并入库 `Icons.A.axaml`。commit。

- [ ] **T2 — 三层字典 + `SelectedIconTheme`（无 UI）**
  - `AppSettings` 加 `SelectedIconTheme`（默认 `"A"`）；`App.axaml.cs` 加 `IconsAUri` + `AddMergedDictionary`/`ResolveIconThemeUri`/`AddIconOverrideLayer`（空），`ApplyTheme()` 末尾按 ①→②→③ 装配；`App.axaml:12` 改挂 `Icons.A.axaml`。
  - **测试（Avalonia headless）**：设 `SelectedIconTheme="A"` → `ApplyTheme` 后 `Application.Current.Resources.TryGetResource("IconFolder", null, out var r)` 命中且 `r is DrawingImage`；未知 id（`"ZZZ"`）→ 仍命中 A（静默回退）。commit。

- [ ] **T3 — `IconView` 控件**
  - `Controls/IconView.cs`：`Source`/`SourceKey`/`Foreground`(AddOwner+null 默认)/`Stretch`；`Render` 展平 + 均匀缩放 + null 部件用 Foreground；`OnAttachedToVisualTree` 订阅 `ThemeTextPrimaryBrush` 与 `SourceKey`（自定义 `IObserver<T>`，不引 System.Reactive），`OnDetachedFromVisualTree` 释放。
  - **测试（headless 真实像素）**：① `UseHeadlessDrawing=false` 下渲染 `DrawingImage`（单 GeometryDrawing 无 Brush）+ `Foreground=Red` → `RenderTargetBitmap` 命中红色像素；② 同图 `Foreground=Blue` → 蓝色；③ 部件带 baked `SolidColorBrush(Green)` → 忽略 Foreground 保持绿；④ `SourceKey` 指向 `Application` 资源 → 渲染非空，替换资源后重渲染像素变化（订阅刷新）。commit。

- [ ] **T4 — ControlTheme + 选中态样式**
  - `App.axaml` 加 `IconView` ControlTheme（4.6）+ 扩 `ToggleButton:checked` 选择器（4.5）。
  - **测试**：`IconView` 未设尺寸时 `DesiredSize` ≈ 20×20（读 `IconElementThemeWidth/Height`）。commit。

- [ ] **T5 — 新增 `BoolToIconKeyConverter`（保留旧 converter，保持编译绿）**
  - 新建 `Converters/BoolToIconKeyConverter.cs`：`Convert` 返回键字符串（`value is true ? parts[1] : parts[0]`），**不**再 `TryGetResource`。
  - **测试**：`Convert(true, typeof(string), "IconEye|IconEyeOff", …)` 返回 `"IconEyeOff"`；`false` → `"IconEye"`。commit。

- [ ] **T6 — 静态消费面迁移（182 处 → `IconView`）**
  - 按文件逐批：标签替换 + `StaticResource`→`DynamicResource` + 补 `xmlns:controls`（10 文件）。可用脚本辅助，但**必须逐文件 review**（保留 `Width`/`Height`/`Foreground`/`Classes`/内联属性）。
  - **门禁测试/检查**：全仓 grep `PathIcon Data="{StaticResource Icon` = 0 命中；`grep -n "PathIcon" src/**/*.axaml` 仅剩 Avalonia 无关命中（预期 0）。commit（可分批多次 commit）。

- [ ] **T7 — converter / VM / C# 动态站点迁移（切换到新 converter）**
  - `App.axaml:272` TabIcon → `<controls:IconView SourceKey="{Binding $parent[TabItem].(controls:TabItemExtensions.TabIcon)}" …/>`（保留 `IsVisible`/`Width`/`Height`/`Foreground`）。
  - `ResultTreeView.axaml:125`、`QuickPathControl.axaml:122`、`ExtractSettingsWindow.axaml:63`、`IconTestWindow.axaml:89` → `SourceKey`。
  - `ProgressWindow.axaml:140` → `SourceKey` + `{StaticResource BoolToIconKeyConverter}`；`ProgressWindow.axaml` 资源区声明新 converter 实例。
  - C# 站点：`ProgressWindow.axaml.cs:154/235/346/395/401`、`PasswordManagerWindow.axaml.cs:62-63`、`MatchedPasswordDialog.axaml.cs:89`、`ResultTreeView.axaml.cs:257` → 改设 `SourceKey`；对应命名元素类型 `PathIcon`→`IconView`。
  - `MainWindowViewModel`：`_toolbarPasswordIcon`/`_passwordStatusIcon` 由 `object?`(Geometry) 改 `string?`(键值)；`MainWindow.axaml:641,1341` 改绑 `SourceKey`。
  - **测试**：构造 `MatchedPasswordDialog`/`ResultTreeView` 等，无「Unable to find resource」异常；VM 设密码态后 `ToolbarPasswordIcon == "IconLockOpen"`。commit。

- [ ] **T8 — 删除已无引用的旧 converter**
  - 确认零引用后删除 `Converters/GeometryResourceConverter.cs` + `Converters/BoolToIconConverter.cs`，移除 `App.axaml:15`、`IconTestWindow.axaml:16` 声明与 `ProgressWindow.axaml:20` 旧声明。
  - **门禁**：`git grep -n 'GeometryResourceConverter\|BoolToIconConverter' -- 'src/*'` = 0 命中（注意 `BoolToIconKeyConverter` 含子串，用词边界 `\bBoolToIconConverter\b`）。commit。

- [ ] **T9 — 测试桩图标合并改挂 `Icons.A.axaml`**
  - 4 处 `EnsureIconResources()` 的 URI 由 `AppIcons.axaml` 改为 `Icons.A.axaml`。
  - **测试**：`dotnet test tests\MantisZip.UI.Avalonia.Tests` 全绿（原依赖 `StaticResource Icon` 的对话框测试不再抛异常）。commit。

- [ ] **T10 — 文档与验收**
  - `docs/PLAN.md` 加 3 行（本文 + 计划 2/3），格式对齐既有表。
  - `docs/progress-avalonia-detail.md` 以 `**2026-10-15**` 追加条目（规则 3）。
  - 手动 QA：浅色/深色/选中态全 UI 截图对照（A 套零视觉差异）；构建 0 错误。commit。

## 6. 涉及文件

**新增**
- `tools/IconGen/IconGen.csproj`、`tools/IconGen/Program.cs`
- `src/MantisZip.UI.Avalonia/Resources/Icons/Icons.A.axaml`
- `src/MantisZip.UI.Avalonia/Controls/IconView.cs`
- 测试：IconGen 同步测试、`IconView` 渲染测试、（新）图标字典装配测试

**修改**
- `src/MantisZip.UI.Avalonia/App.axaml`（MergedDictionaries、converter 声明、ControlTheme、选中态选择器、TabIcon 模板）
- `src/MantisZip.UI.Avalonia/App.axaml.cs`（`ApplyTheme` 三层装配、常量、helpers）
- `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`（`SelectedIconTheme`）
- `src/MantisZip.UI.Avalonia/Converters/BoolToIconConverter.cs`（→ Key 版）
- `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`（2 个图标属性类型）
- 消费面 17 个 `.axaml` + 4 个 `.axaml.cs`（ProgressWindow / PasswordManagerWindow / MatchedPasswordDialog / ResultTreeView）
- `tests/MantisZip.UI.Avalonia.Tests/{ExtractPreserveFullPathPreviewMatchTests, PickerOptionsRegionTests, PickerOptionsRegionLayoutTests, ResultTreeViewFilterTests}.cs`
- `docs/PLAN.md`

**删除**
- `src/MantisZip.UI.Avalonia/Converters/GeometryResourceConverter.cs`

## 7. 验证

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test  tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test  tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
# 残留门禁（期望 0）
git grep -n 'PathIcon Data="{StaticResource Icon' -- 'src/*.axaml'
git grep -n 'GeometryResourceConverter' -- 'src/*'
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
| 182 处机械替换遗漏 / 改坏属性 | 分文件 review + 残留门禁 grep + 迁移前后截图对照 |
| `Foreground` 默认值从 `Brushes.Black`（PathIcon）变为 `null`→`ThemeTextPrimaryBrush`（IconView） | 实测 27 处无 `Width` 的 PathIcon 是否也无显式 `Foreground`：若有，评估深色主题观感；A 套零差异对照兜底 |
| `SourceKey` 订阅泄漏 | `OnDetachedFromVisualTree` 必须 `Dispose` 订阅；测试覆盖 attach/detach |
| `AddOwner` per-owner 元数据覆盖副作用 | 仅影响 `IconView` 类型；`TextElement/TemplatedControl/TextBlock` 默认值不变；测试锁定 `new TextBlock().Foreground != null` |
| 键冲突（AppIcons 与 Icons.A 同名） | 运行时**只** merge Icons.A 三层，绝不 merge AppIcons |
