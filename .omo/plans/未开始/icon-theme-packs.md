# 图标主题系统 — 计划 2：图标包导入 + 单图标覆盖 + 设置 UI

> **状态**: 📋 未开始 | **创建**: 2026-10-15 | **优先级**: P2 | **预估工时**: 26-34h（2026-10-21 复核后由 16-22h 上调至 20-26h：保留字 id、删包回退、provider 缓存失效、缩略预览、`IconCatalog` 抽取；2026-10-24 复核再上调：stroke/`Pen` 支持、规则 8 守卫改造、启动预算测量、测试底座）
> **最近复核**: 2026-10-24（对照仓库实测订正普查数字与全部行号；新增 D-E stroke 能力；修正 Zip Slip 语义与「写盘失败」测试方案；补齐规则 8 守卫改造与启动预算）
> **来源**: [docs/superpowers/specs/2026-10-15-icon-theme-system-design.md](../../docs/superpowers/specs/2026-10-15-icon-theme-system-design.md)（已批准，§6/§7/§8）
> **前置**: **计划 1**（`icon-theme-infra.md`）必须已完成 —— 本文依赖其交付的 `IconView`（`SourceKey`）/ `AppSettings.SelectedIconTheme` / `App.axaml.cs` 的 `GetIconThemeProvider(string id)` + `ResolveIconThemeUri(string? id)` + `_iconThemeProviders` 缓存 / `AddIconOverrideLayer()` 空桩。
> **后续**: 计划 3（C/D/E/F/G 内容生产）可选独立进行；导入管线与之无耦合。
> **规格偏差（D-C，2026-10-15 决策）**: 规格 §6.4/§8 原为「任一 SVG 不兼容 → **整包原子拒绝**」。现调整：**渐变/图案填充**类不兼容 → **跳过该图标 + 计入警告清单（非静默）**，不阻断整包；**结构性不兼容**（`clipPath`/`mask`/`filter`/`text`/`use`/嵌套 `<svg>`/外链）、解析损坏、XXE/DTD、Zip Slip 仍按**原子拒绝**。理由：真实图标包常用渐变，整包拒绝体验过差；且渐变属「填充画刷」而非「元素结构」，分类处理合理。渐变解析支持留作后续小计划（届时取消该降级）。
> **规格偏差（D-D，2026-10-21 复核决策）**: 规格 §6.5 的落盘形态为「`icon-packs/<id>/` 解压内容 + `icon-overrides/IconXxx.svg`」。本文改为**解析产物缓存**：`icon-packs/<id>/Icons.json`（`IconPackData`）+ `icon-overrides/IconXxx.json`（`ParsedIconData`）。理由：避免每次启动重解析全部 SVG（大包启动耗时与解析失败面）；ZIP 解压目录仅在导入期作临时目录使用，成功后删除。副作用：用户无法直接改落盘文件生效（须经图标管理器重新替换），已在 §3.5 说明。
> **能力扩展（D-E，2026-10-24 复核）**: v1 解析器**必须支持 `stroke` 描边**（→ `GeometryDrawing.Pen`）。原因：主流开源图标库（Lucide / Feather / Tabler / Bootstrap）以 `fill="none" + stroke="currentColor"` 为主；若不支持，这些包会「导入成功」却被当作填充几何渲染成错误图形——比直接报错更难发现。连带契约：计划 1 的 `IconView.Render` 必须透传 `Pen`（`Pen.Brush == null` → `Foreground`）并处理 `DrawingGroup.Opacity/Transform/ClipGeometry`，已同步回填计划 1 §4.3。

## 1. 目标与交付标准

在计划 1 的三层字典骨架上补齐 **第②层（导入包）** 与 **第③层（单图标覆盖）** 的数据生产与用户入口：

1. SVG 图标包（文件夹 / ZIP）导入 → 覆盖率校验 → 落盘 → 即时替换第②层 → 持久化。
2. 单个图标覆盖（图标管理器逐键替换 / 恢复）→ 即时生效于第③层。
3. 设置「外观」Tab 新增**图标风格分区**（风格下拉 + 导入 + 已导入列表 + 打开图标管理器）。
4. 新增 **`IconManagerWindow`**（搜索 / 过滤 / 网格卡片 / 替换 / 恢复，数据源 = `IconTestViewModel` 注册表契约）。
5. 全部错误路径按 §8 处理（原子拒绝 / 静默回退 / TraceLog 脱敏）。

**DoD**：导入 happy 路径与全部失败路径通过（**含 `fill="none" + stroke` 的描边图标、Zip Slip 逃逸条目**）；覆盖/恢复回路生效；三语 key 成对；`pwsh -File scripts\verify-repo.ps1 -Task guards` 通过（117 条图标登记全绿）；构建 0 错误；新增测试全绿；完成时按规则 18 确认 `RELEASE_NOTES.md` 范围。

## 2. 现状核实

| 项 | 事实 | 位置 |
|----|------|------|
| 覆盖率契约 | `IconTestViewModel.LoadAllIcons()` 共 **138 条 `Add(...)`**：1 条 `resourceKey=null` + 137 条带键 → **117 个去重键**；重复分布在 **13 组、共 20 条冗余行**（`IconFolder` 出现 7 次，`IconEye`/`IconWarning` 各 3 次）。`Add` 是 **private** 辅助方法（`:387`）；可枚举的是 `Icons`（`ObservableCollection<IconTestItem>`，`ResourceKey` 可空）。**任何覆盖率/差异计算必须 `Distinct()`** | `ViewModels/IconTestViewModel.cs:14`（属性）/`:387`（`Add`）/`Models/IconTestItem.cs:21` |
| 解压依赖 | `SharpCompress 0.50.4` **只在 Core**（`MantisZip.Core.csproj:24`）；**UI 不加 `PackageReference`**——UI 已在 `Services/PasswordService.cs:6` 直接消费 SharpCompress API（靠 Core 传递引用），而解 ZIP 用 in-box `System.IO.Compression.ZipFile` 更简单（`ViewModels/PreviewViewModel.cs:2789` 已有先例）。Zip Slip 语义要注意：`FileConflictHelper.SanitizeEntryPath`（`:213`）是**净化**（把 `folder/../../evil.txt` 改写成 `evil.txt`，不抛异常），只有 `GetSafePath(dir, entryName)`（`:193`）才抛 `InvalidOperationException` | `MantisZip.Core.csproj:24` / `Core/Utils/FileConflictHelper.cs:193/213` |
| 选择器（规则 **16**） | `CustomFilePickerDialog.ShowOpenFileAsync(Window owner, string? initialPath = null, string[]? fileExtensions = null, IReadOnlyList<FileTypeOption>? fileTypes = null)`（ZIP）/ `ShowOpenItemsAsync(Window owner, string? initialPath = null)`（文件夹） | `Dialogs/CustomFilePickerDialog.axaml.cs:195` / `:224` |
| 设置持久化 | `AppSettings` POCO + `Load()`/`Save()`；设置窗口保存后调 `App.RefreshTheme()`（`internal static`，**`App.Theming.cs:99`**，非 `App.axaml.cs:547`——后者是 `MaxDebugLogFileSize` 常量） | `Models/AppSettings.cs` / `App.Theming.cs:99` |
| 外观 Tab | **`SettingsWindow.axaml:681-759`**（主题/紧凑度/最近文件/界面字体）；锚点 `controls:TabItemExtensions.TabIcon="IconPaintBrush"`（`:682`），`IconPaintBrush` 为 `AppIcons.axaml:33` 真键。**注意 `:305-477` 是「压缩格式」Tab，勿误改** | `Views/SettingsWindow.axaml:681-759` |
| 存储目录 | 必须用 **`AppSettings.DataDir`（`:257`）**，禁止硬编码 `%LOCALAPPDATA%`：便携模式（exe 旁 `Portable.txt` 存在时启用，`IsPortableMode:254`）与常规模式（`%LOCALAPPDATA%\MantisZip`）由静态构造 `:271` 解析；临时目录 `GetTempDir()`（`:263`） | `Models/AppSettings.cs:254/257/263/271` |
| 本地化 | 三语 key 集必须对称（`AboutWindowTests.AllThreeLanguages_HaveSameKeySet`）。**`SettingsWindowViewModel` 无 `LocalizedStrings` 索引器**：用个体属性（`public string Xxx => LocalizationManager.T("Key")`）+ `OnCultureChanged` 逐属性 `OnPropertyChanged`（`:1204` 订阅、`:1431` 刷新）。仅 `MainWindowViewModel` 有 `LocalizedStrings` 字典（`_localizedStrings:200`、`UpdateLocalizedStrings():232`）→ **该登记只对 MainWindow 系 XAML 生效**；设置页与 `IconManagerWindow` 走个体属性 | 规则 13 / `ViewModels/SettingsWindowViewModel.cs:1204,1431` |
| 元数据面板先例 | `MetadataSettingsManager`（静态类 + 独立 json `metadata-panel.json` 于 `AppSettings.DataDir` + `SettingsChanged` 事件驱动刷新）是「独立持久化 + 事件刷新」的既有模式，可对齐 | `Services/MetadataSettingsManager.cs:13,22` |
| 仓库自检（规则 **8**） | `scripts/verify-repo.ps1` 的 `Test-GuardIcons` **只读 `GUARD_FILES.iconTestVm` = `IconTestViewModel.cs`** 里的 `"Icon*"` 字面量（`RE_ICON_RESOURCE_REF`）。注册表若抽到 `Models/IconCatalog.cs`，该文件不再含字面量 → `registered` 集合变空 → 117 条「未登记」全红。**T7 必须同步改 `GUARD_FILES`（指向 `IconCatalog.cs`，或两处都扫）** | `scripts/verify-repo.ps1:897,1164-1194` |

## 3. 架构

### 3.1 数据模型

```csharp
// Models/IconPackManifest.cs
public sealed class IconPackManifest
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? License { get; set; }
    public Dictionary<string, string>? Mapping { get; set; } // IconPascalCase -> svg 文件名
}

// Models/IconPackEntry.cs  —— 一个已导入包在 registry.json 中的记录
// 缩略图不落盘：设置页/图标管理器直接用 IconView 渲染该包的若干代表键（见 §3.7）
public sealed class IconPackEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Author { get; set; }
    public string? License { get; set; }
    public int CoveredKeys { get; set; }
    public int MissingKeys { get; set; }
}

// 落盘的可复现解析产物（避免启动期重解析 SVG）：icon-packs/<id>/Icons.json
public sealed class IconPackData
{
    public string Id { get; set; } = "";
    public Dictionary<string, ParsedIconData> Icons { get; set; } = new();
}
public sealed class ParsedIconData
{
    public List<ParsedPart> Parts { get; set; } = new();
}
public sealed class ParsedPart
{
    public string Path { get; set; } = "";   // 归一化到 24×24 的 path 数据
    public string? Fill { get; set; }        // null = 跟随 Foreground；"#RRGGBB"/"#AARRGGBB"
    public string? Stroke { get; set; }      // null = 无描边；非 null 语义同 Fill（baked 或跟随 Foreground）
    public double StrokeWidth { get; set; }  // 描边宽度，24×24 坐标系（随 viewBox 同比例缩放）
    public string? LineCap { get; set; }     // "round" | "square" | "flat"（SVG 默认 flat）
    public string? LineJoin { get; set; }    // "round" | "bevel" | "miter"（SVG 默认 miter）
}
```

### 3.2 SVG 解析器（`Services/SvgIconParser.cs`）

**只提取几何 + 填充色，不渲染不执行。** 用 `XmlReader`（`DtdProcessing.Prohibit`、`XmlResolver = null`）防 XXE。

```csharp
public static class SvgIconParser
{
    public enum Outcome { Ok, SkippedUnsupportedPaint, Error }

    public sealed record Result(Outcome Outcome, IReadOnlyList<ParsedPart>? Parts, string? Message);

    /// <summary>
    /// 解析单个 SVG 文本为归一化(24×24)几何部件。
    /// Ok=成功；SkippedUnsupportedPaint=仅因渐变/图案填充不支持而跳过（不阻断整包，D-C）；
    /// Error=结构性不兼容或解析损坏（触发整包原子拒绝）。
    /// </summary>
    public static Result Parse(string svgText);
}
```

- **支持**：`path`(d) / `circle` / `rect` / `line` / `polygon` / `polyline`；solid 填充（`#rgb`/`#rrggbb`/`#aarrggbb`/`none`/`currentColor`）；**描边**（`stroke` / `stroke-width` / `stroke-linecap` / `stroke-linejoin` → `ParsedPart.Stroke*`，`IconThemeBuilder` 转 `GeometryDrawing.Pen`，见 D-E）；`viewBox` 归一化到 24×24（`Geometry.Transform` 或 `TransformGroup`）；多部件多色。
- **不支持 → `SkippedUnsupportedPaint`（D-C，跳过该图标 + 警告，不阻断）**：`linearGradient` / `radialGradient` / 图案（`pattern`）填充。仅当**该图标**用到这些画刷时跳过它；同包其它图标照常导入。
- **不支持 → `Error`（结构性，触发原子拒绝）**：`clipPath` / `mask` / `filter` / `text` / `use` / 嵌套 `<svg>` / 外链（`href`/`xlink:href`）。
- `fill` 缺省或 `none`/`currentColor` → `ParsedPart.Fill = null`（跟随 `Foreground`）；**`fill="none"` 且带 `stroke` → 只产出描边部件**（Fill 保持 null，靠 Pen 渲染）——这是 Lucide/Feather/Tabler 的主流形态，必须走通。
- **CSS 兜底**：`class` 属性与 `<style>` 块**不求值**（只按元素属性解析）；若某元素既无 `fill` 也无 `stroke` 属性，按 SVG 默认 `fill=black` 处理并在 `Warnings` 记录，避免静默空白。
- 归一化：计算 `viewBox` 尺寸 → 缩放 + 平移，使内容映射进 24×24；`stroke-width` 同步按同一比例缩放（否则缩放后线宽失真）。

### 3.3 包导入（`Services/IconPackImporter.cs`）

```csharp
public sealed class IconPackImporter
{
    public sealed record ImportResult(
        bool Ok, string? PackId, int Covered, int Missing,
        IReadOnlyList<string> Warnings,   // 非阻断：渐变跳过、manifest 损坏等（D-C）
        IReadOnlyList<string> Failures,   // 阻断：结构性不兼容/损坏（触发整包拒绝）
        string? Error);

    /// <summary>导入文件夹或 ZIP（同构）。原子性：任一 Failures → 不落盘 + 返回失败清单。</summary>
    public Task<ImportResult> ImportAsync(string sourcePath, bool overwriteExisting, CancellationToken ct);
}
```

数据流（对应 §6.3）：

1. 源为 ZIP → 用 in-box `System.IO.Compression.ZipFile`（**不新增包引用**，见 §2）解压到 `AppSettings.GetTempDir()`（`:263`）下的临时目录；源为文件夹 → 直接读。**每个条目名先 `FileConflictHelper.SanitizeEntryPath`（净化）再 `GetSafePath(tempDir, name)` 复核——后者抛异常即整包拒绝**（`SanitizeEntryPath` 只净化不拒绝，单用会静默改写路径）。**导入结束（成功或失败）必须删除临时目录**。
2. 读 `manifest.json`（可选）：损坏 → 记 `Warnings` + 按键名约定继续（不阻断）。
3. 解析包 id：`manifest.id` 归一化（仅 `[a-zA-Z0-9_-]`）；缺省 → 源文件夹/ZIP 文件名归一化。**保留字校验：归一化结果若命中内置主题 id `A`–`G`（忽略大小写）→ 直接返回失败「该 id 为内置主题保留」**（否则 `ResolveIconThemeUri` 先匹配内置，包永远解析不到且 registry 同 id 冲突）。
4. 枚举 `icons/*.svg`（或按 `manifest.mapping`）→ 键名 = `IconPascalCase`（文件名去 `.svg`，或映射目标）。
5. 每个 SVG 过 `SvgIconParser.Parse`：
   - `Ok` → 收集部件（填充部件与描边部件都可能出现）；
   - `SkippedUnsupportedPaint`（渐变/图案）→ 记 `Warnings`（文件 + 原因）、**跳过该键**，继续处理其余；
   - `Error`（结构性/解析损坏）→ 记 `Failures`，**整包原子拒绝**。
6. 覆盖率校验：对照 **117 键**清单（`IconCatalog` 的 `Distinct(ResourceKey)`，见 §2/§3.8）→ `Covered`/`MissingKeys`（缺键只警告，含被跳过的渐变键）。
7. id 冲突且 `!overwriteExisting` → 返回「询问覆盖/取消」信号（UI 层弹窗；本服务不弹窗）。覆盖时**先删旧目录再写**，并清 `App._iconThemeProviders[id]` 缓存。
8. 落盘：`icon-packs/<id>/Icons.json`（`IconPackData`）+ 更新 `registry.json`（均经 `AppSettings.DataDir`）。
9. 返回 `ImportResult`；UI 触发 `App.RefreshIconTheme()`（见 3.6）→ 第②层替换 → 全 UI 刷新 + `SelectedIconTheme = id` 持久化。若 `Warnings` 非空 → 弹「部分图标已跳过」提示（列出键名）。

### 3.4 注册表与存储（`Services/IconPackRegistry.cs`）

```csharp
public sealed class IconPackRegistry
{
    public IReadOnlyList<IconPackEntry> Packs { get; }
    public void Load();
    public void Save();
    public void Upsert(IconPackEntry entry);
    public bool Remove(string id); // 删 registry 条目 + 删 icon-packs/<id> 目录 + 清 App._iconThemeProviders[id]
}
```

- `Remove(id)` 若 `id == AppSettings.SelectedIconTheme` → **必须同时把 `SelectedIconTheme` 置回 `"A"` 并调用 `App.RefreshIconTheme()`**（否则界面停留在已删包，下次启动才回退）。
- `registry.json` 序列化 round-trip 测试（T2）。
- 存储布局（规格 §6.5，**落盘形态见偏差 D-D**；根目录一律 `AppSettings.DataDir`，便携模式自动跟随）：
  ```
  <AppSettings.DataDir>\
  ├── settings.json          # + SelectedIconTheme（计划 1）
  ├── icon-packs\
  │   ├── registry.json
  │   └── <id>\Icons.json
  └── icon-overrides\
      └── IconXxx.json       # 单图标覆盖（键 → ParsedIconData）
  ```

### 3.5 单图标覆盖（`Services/IconOverrideService.cs`）

```csharp
public sealed class IconOverrideService
{
    public IReadOnlySet<string> OverriddenKeys { get; }
    public Task<string?> ReplaceAsync(string iconKey, string svgPath, CancellationToken ct); // null=成功
    public void Restore(string iconKey);        // 删条目 + 删文件 → 回落②层
    public ResourceDictionary BuildOverrideDictionary(); // 供 App 第③层
}
```

- 替换 → `SvgIconParser.Parse` → 写 `icon-overrides/<Key>.json` → 第③层重建（**先清空再 Add，禁止叠加**）→ 即时生效。
- 恢复 → 删条目 + 删文件 → 第③层重建。
- 损坏 override → 跳过该键 + TraceLog，不阻断启动（§8）。
- 落盘为**解析产物**（偏差 D-D）：用户直接编辑 `icon-overrides/*.json` 不保证生效（无文件监听），须经图标管理器重新「替换…」。

### 3.6 接线（`App.axaml.cs` 扩展计划 1 的桩）

> 计划 1 的实际契约见 `icon-theme-infra.md` 的 **§4.4（三层装配与缓存）** 与 **§3 末的「当前源码事实」**：`private readonly Dictionary<string, IResourceProvider> _iconThemeProviders` + `GetIconThemeProvider(string id)`（内部 `ResolveIconThemeUri` → `AvaloniaXamlLoader.Load` → 缓存）+ `ResolveIconThemeUri(string? id)` + `AddIconOverrideLayer()` 空桩；装配主体已抽到 `IconThemeAssembler.Assemble/ShouldSkip` 供测试。**不存在 `AddMergedDictionary(string uri)`**。本文一律按**章节锚点**引用——计划 1 于 2026-10-24 复核后行号已整体变动，勿按行号查找。

- `AddIconOverrideLayer()` 真实实现 → `IconOverrideService.BuildOverrideDictionary()` 结果 Add 为第③层。计划 1 的 `ApplyTheme` 每次都是「`MergedDictionaries.Clear()` → 按序 Add 三层」，本身不会累积；T5 的「连续刷新 3 次层数不增长」断言就是锁这条性质（**不要**再另加「移除上一次③层实例」的逻辑，与 Clear 重复维护反而容易错）。
- `GetIconThemeProvider(string id)` 改为**双来源**（保持返回 `IResourceProvider` 的签名不变，`ApplyTheme` 装配代码零改动）：
  - 内置 `"A"`…`"G"` → 现有路径 `AvaloniaXamlLoader.Load(new Uri(ResolveIconThemeUri(id)))`；
  - 已导入包 → `IconThemeBuilder.BuildDictionary(IconPackData)`（内存 `ResourceDictionary`，天然是 `IResourceProvider`）；
  - 未知 id / 文件缺失 / 反序列化异常 → **静默回退 A** + TraceLog（§8，启动路径不弹窗）。
- `ResolveIconThemeUri(id)` 只负责**内置套的 avares URI**（`"A"`…`"G"`，计划 3 扩 C–G）；导入包不产生 URI，由上面分支直接构造。
- **新增 `internal static void RefreshIconTheme()`**（对齐 `RefreshTheme()`，`App.Theming.cs:99`）：设置窗口切风格 / 导入 / 覆盖 / 删包后调用，无需重启。实现必须做两件事，缺一不生效：
  1. `_iconThemeProviders.Remove(id)`（导入覆盖、删除、或当前 id 的字典已变）——否则命中旧 provider；
  2. 把 `_loadedIconThemeId` **置为 `null`**（计划 1 的 `IconThemeAssembler.ShouldSkip` 双条件早退依据，其初值已改为 `null`）——否则 `ApplyTheme()` 直接 return，界面不刷新。
- 新增 `Services/IconThemeBuilder.cs`：`ParsedIconData` → `Geometry.Parse` → `GeometryDrawing`（`Fill == null` 时**不设 Brush**，跟随 `IconView.Foreground`；非 null → `SolidColorBrush(Color.Parse(fill))`；`Stroke != null` 时设 `Pen`：`Pen.Brush` 语义同 Fill（null = 跟随 Foreground）、`Thickness = StrokeWidth`、`LineCap/LineJoin` 从 `ParsedPart` 映射）→ `DrawingImage` → `ResourceDictionary`（键 = `IconPascalCase`）。这是运行时唯一「字符串 path → 资源字典」的转换点，导入包、覆盖层、缩略预览三处共用。

### 3.7 设置 UI（`SettingsWindow.axaml`「外观」Tab 新增分区）

在 `SettingsWindow.axaml:681-759` 外观 Tab 内追加「图标风格」`StackPanel`（规则 4/5/6/13/14；锚点 `TabIcon="IconPaintBrush"` 在 `:682`，该键实为 `AppIcons.axaml:33`）。**注意 `:305-477` 是「压缩格式」Tab，勿误改**。

- 下拉 `ComboBox`：A + 已存在的内置套（C–G 由计划 3 产出）+ 已导入包；`SelectedItem` 双向绑定 → `RefreshIconTheme()`，即时生效无「应用」按钮。**候选列表动态拼装**（按 `ResolveIconThemeUri` 是否命中内置、registry 是否有该包），禁止硬编码六个 id 后显示空白项——计划 3 未落地时 C–G 不应出现在下拉里。
- **缩略预览（4 图标）**：`ItemTemplate` 用 `IconView`，但 `SourceKey` 只能解析**当前已装载**的第②层 → **非选中包预览必须走 `IconThemeBuilder`**：VM 为每个候选 id 预先构建 `PreviewImages: DrawingImage[]`（内置套从对应 `Icons.X.axaml` 取、导入包从 `Icons.json` 构建），模板绑 `IconView Source="{Binding ...}"`。**不要**依赖 `DynamicResource` 查非当前层（查不到会空白）。
- `[导入图标包…]` 按钮 → 规则 **16** `CustomFilePickerDialog`（`ShowOpenFileAsync` 限 ZIP / `ShowOpenItemsAsync` 允许文件夹）→ `IconPackImporter` → 失败弹「失败文件清单」对话框；`Warnings` 非空弹「部分图标已跳过」。
- **已导入列表** `ListBox`：`name（覆盖率 x%，缺 N 键）` + 删除按钮（删前确认）→ `IconPackRegistry.Remove`（内含删当前选中包时回退 A，见 §3.4）→ 刷新。行高按规则 7 设 `ControlHeightMd`。
- `[打开图标管理器…]` 按钮 → `IconManagerWindow`（显示已覆盖数量徽章）。
- 说明文案：「图标风格只影响应用内图标；文件关联与右键菜单图标在各自设置中单独管理」。
- **本地化**：`SettingsWindowViewModel` **没有** `LocalizedStrings` 索引器（§2），新增文案用个体属性 `public string Xxx => LocalizationManager.T("Key")`，并在 `OnCultureChanged`（`:1416`）补 `OnPropertyChanged(nameof(Xxx))`；三语 json 成对添加。

### 3.8 图标管理器（`Views/IconManagerWindow.axaml(.cs)` + `ViewModels/IconManagerViewModel.cs`）

- 顶部：搜索框（键名/语义名）+ 过滤徽章（全部 **117** / 已覆盖 N / 当前风格缺失 N）+ 覆盖率警告横幅（当前风格缺键时）。
- **数据源 = 新增 `Models/IconCatalog.cs`**：从 `IconTestViewModel.LoadAllIcons()` 抽出的**静态**注册表（`IReadOnlyList<IconCatalogItem>`）。**不能直接用 `IconTestViewModel.Icons`**：它含 **138 条（117 个去重键 + 13 组共 20 条冗余行 + 1 条 null）**，直接枚举会出现重复卡片、徽章计数虚高。
- **去重不能简单丢掉重复项**：`IconFolder` 有 7 处使用点，去重成一张卡片会丢掉其余上下文 → `IconCatalogItem` 保留 `Contexts: IReadOnlyList<IconUsage>`（category / 语义名 / 位置 / 备注，来自 `Add(...)` 的四个字段），卡片展示首个使用点、可展开看全部。`IconCatalog.Keys` = `Distinct(ResourceKey)`（null 排除）。
- `IconTestViewModel` 改为消费 `IconCatalog`（保持规则 8「注册即入测试窗口」语义）。**同一任务内必须改 `scripts/verify-repo.ps1` 的 `GUARD_FILES.iconTestVm` 指向 `IconCatalog.cs`**（守卫只扫 `IconTestViewModel.cs` 的 `"Icon*"` 字面量，不改会 117 条全红，见 §2 末行），并跑 `-Task guards` 确认。
- 网格（`WrapPanel`/`ItemsControl`）卡片：`IconView` 预览 + 键名 + 语义名；覆盖率 = `IconCatalog` 键集与第②层键集求差。
- 每卡 `[替换…]`（规则 **16** `ShowOpenFileAsync` 限 `.svg`）+ `[恢复]`。
- 行高遵守规则 7（卡片外层 `MinHeight` 用 `ControlHeightSm`）；文案走规则 13，且 `IconManagerViewModel` 同样用个体属性 + `OnCultureChanged` 刷新（无 `LocalizedStrings` 索引器）。
- `IconManagerWindow` 必须有**公共无参构造**（`x:Class` 的 `AVLN3000` 约束）；与既有 `ViewModels/IconTestViewModel` + `Views/IconTestWindow.axaml`（开发者诊断工具，展示 emoji/状态/位置）划清职责：管理器面向用户，只做替换/恢复。

## 4. 任务分解（TDD；每任务一 commit）

- [ ] **T1 — `SvgIconParser`**
  - 实现 `Parse`（`Outcome` 三态）；`XmlReader` 禁 DTD/实体；解析 `fill` 与 **`stroke` 家族**（`stroke`/`stroke-width`/`stroke-linecap`/`stroke-linejoin`）。
  - **测试**：合法 `path`/`circle`/`rect`/`polygon` → `Ok` + 期望部件数 + `Fill`（含 `none`→null、`currentColor`→null）；**`fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"` → 产出描边部件且 `Fill==null`、`StrokeWidth==2`、`LineCap=="round"`**（Lucide/Feather 形态，D-E 回归）；`viewBox="0 0 48 48"` → 归一化到 24×24（断言包围盒 + `StrokeWidth` 同比例减半）；`linearGradient`/`radialGradient` 填充 → `SkippedUnsupportedPaint`（D-C）；`clipPath`/`text`/`use`/嵌套 `svg`/含 DTD 的样本 → `Error`。commit。

- [ ] **T2 — 覆盖率计算 + registry 序列化 + `IconCatalog`**
  - 新增 `Models/IconCatalog.cs` 并从 `IconTestViewModel.LoadAllIcons()` 抽出注册表（**提前到本任务**：T3/T4/T7 都要用键集与使用点）；`IconTestViewModel` 改为消费它。
  - **同一 commit 改 `scripts/verify-repo.ps1` 的 `GUARD_FILES.iconTestVm`** → 指向 `IconCatalog.cs`，并跑 `-Task guards`（否则 117 条未登记全红）。
  - `IconPackRegistry`（load/save/upsert/remove + 删当前选中包回退 A）+ 覆盖率（全套/部分/空）。
  - **测试**：`IconCatalog.Keys.Count == 117`（与 `Icons` 的 138 条、20 条冗余行无关）；`IconFolder` 的 `Contexts.Count == 7`；`registry.json` round-trip；覆盖率对照 117 键；空包 = 全缺；`Remove` 当前选中 id → `SelectedIconTheme == "A"` 且触发刷新。commit。

- [ ] **T3 — `IconPackImporter`（文件夹 + ZIP，原子性 + 渐变降级）**
  - 解压到 `GetTempDir()`（in-box `ZipFile`；条目名**先 `SanitizeEntryPath` 净化、再 `GetSafePath` 复核，抛异常即整包拒绝**）+ manifest 容错 + 保留字 id 校验 + 逐 SVG 解析（三态分流）+ 原子拒绝 + 落盘 + 临时目录清理。
  - **测试**：合法文件夹/ZIP → `Ok` + `Covered/Missing`；含 1 个结构性非法 SVG → `Ok=false` 且**不落盘** + `Failures` 含该文件 + 临时目录已删；含 1 个渐变图标 → `Ok=true` + 该键**不在**结果中（`Missing++`）+ `Warnings` 含该文件，其余键正常导入；**含 `../../evil.txt` 条目（逃逸到 temp 之外）→ `Ok=false` 且目标路径不存在**（注意：仅 `foo/../bar.txt` 这种净化后仍安全的条目应被净化放行，不能一律拒绝）；manifest 损坏 → 仍按键名导入（记 `Warnings`）；**`manifest.id="C"` → `Ok=false` + 错误文案「保留 id」**；`id="My Icons!"` → 归一化为 `My-Icons`。commit。

- [ ] **T4 — 第②层接线（导入包 → 资源字典）**
  - 新增 `Services/IconThemeBuilder.cs`（`ParsedIconData` → `DrawingImage`（含 `Pen`）→ `ResourceDictionary`）；`GetIconThemeProvider(id)` 改双来源（内置 XAML / 导入包内存字典），`ResolveIconThemeUri` 只管内置；`RefreshIconTheme()`（清 provider 缓存 + `_loadedIconThemeId = null` + 重装）。
  - **启动预算测量**（§2/计划 1 §4.7）：117 键 × 每键若干 `Geometry.Parse` 在建字典时执行，须实测「导入包装配」耗时；若单次装配 > 60ms，则在导入期落盘 `Geometry` 之外再缓存序列化后的 `DrawingImage` 不可行时，改为**启动只装当前主题**、其余按需。
  - **测试（headless）**：导入 mock 包 → `SelectedIconTheme=id` → `RefreshIconTheme()` 后 `TryGetResource("IconFolder")` 返回该包版本（与 A 不同）；未知 id → 回退 A；**同 id 覆盖导入后再次刷新 → 取到新版本（缓存已失效）**；`Fill==null` 的部件产出 `GeometryDrawing.Brush == null`；`Stroke!=null` 的部件产出 `GeometryDrawing.Pen.Brush == null` 且 `Thickness == StrokeWidth`。commit。

- [ ] **T5 — `IconOverrideService` + 第③层**
  - 实现替换/恢复/BuildOverrideDictionary；`AddIconOverrideLayer()` 接线（依赖 `ApplyTheme` 的 `Clear → 按序 Add` 不累积，不另加移除逻辑）。
  - **测试**：替换 1 键 → 第③层命中该键且优先级压过②层；恢复 → 回落②层；损坏 override json → 跳过 + 不抛；**连续调用 `RefreshIconTheme()` 3 次 → `Application.Current.Resources.MergedDictionaries` 层数不增长**（锁 Clear 语义）。commit。

- [ ] **T6 — 设置「外观」Tab 图标风格分区**
  - `SettingsWindow.axaml:681-759` 内新增分区 + `SettingsWindowViewModel`（下拉/缩略预览/导入/已导入列表/删除/打开管理器/说明）。
  - 文案用**个体属性 + `OnCultureChanged`**（`:1431`，非 `UpdateLocalizedStrings()` 登记）+ 三语 key 成对。
  - **测试**：三语 key 对称（现有 `AboutWindowTests`）；VM 切换风格调用 `RefreshIconTheme` 并持久化 `SelectedIconTheme`；**非当前包的缩略预览 `DrawingImage` 非空**（不经资源查找）。commit。

- [ ] **T7 — `IconManagerWindow` + `IconManagerViewModel`**
  - 网格/搜索/过滤徽章/替换/恢复；`IconCatalogItem.Contexts` 展开；公共无参构造。
  - **测试**：`IconCatalog.Keys` == 117；过滤（已覆盖/缺失）计数正确；替换后徽章 +1、恢复后 -1。commit。

- [ ] **T8 — 错误处理与启动回退**
  - §8 全表：启动时 `SelectedIconTheme` 指向已删包 / 字典装载异常 → 静默回退 A + TraceLog；导入写盘失败 → 错误框 + 清理半成品目录；运行期删当前选中包 → 立即回退 A（不等重启）。
  - **测试**：`SelectedIconTheme="missing"` → `ApplyTheme` 不抛且回退 A；**写盘失败注入点用可注入的 `IFileSystem`/委托，或对目标文件加 `FileShare.None` 独占句柄使写入抛 `IOException`**（Windows 下「临时目录设为只读」对新建/覆盖写入不生效，不要用该手法）；`Remove(当前 id)` → 界面层立即为 A。commit。

- [ ] **T9 — 文档与验收**
  - `docs/progress-avalonia-detail.md` 追加（规则 3）；`docs/PLAN.md` 状态更新；**按规则 18 向用户确认 `RELEASE_NOTES.md` 是否需要更新及范围**（不得擅自写入或遗漏）。
  - 手动 QA：导入 happy + 全部失败路径；替换/恢复回路；6 图标缩略预览。commit。

## 5. 涉及文件

**新增**
- `Models/IconPackManifest.cs`、`Models/IconPackEntry.cs`、`Models/IconPackData.cs`、`Models/IconCatalog.cs`（**T2 提前创建**，后续 T3/T4/T7 共用）
- `Services/SvgIconParser.cs`、`Services/IconPackImporter.cs`、`Services/IconPackRegistry.cs`、`Services/IconOverrideService.cs`、`Services/IconThemeBuilder.cs`
- `ViewModels/IconManagerViewModel.cs`、`Views/IconManagerWindow.axaml(.cs)`
- 测试：SvgIconParser（含 stroke）/ IconCatalog / registry / importer / override / IconThemeBuilder / 装配 各一套

**修改**
- `App.axaml.cs`（`GetIconThemeProvider` 双来源、`AddIconOverrideLayer` 真实实现、`RefreshIconTheme` 含缓存失效 + `_loadedIconThemeId = null`）
- `Controls/IconView.cs`（**计划 1 阶段**已改为透传 `Pen` 与 `DrawingGroup` 的 Opacity/Transform/ClipGeometry；本计划只依赖该契约，不再改它）
- `Views/SettingsWindow.axaml:681-759` + `ViewModels/SettingsWindowViewModel.cs`（外观 Tab 图标风格分区）
- `ViewModels/IconTestViewModel.cs`（改为消费 `IconCatalog`；规则 8 的注册入口随之落在 `IconCatalog.cs`）
- `Localization/strings.{zh-CN,en,zh-TW}.json`
- `scripts/verify-repo.ps1`（`GUARD_FILES.iconTestVm` 改指向 `IconCatalog.cs`，规则 8 守卫随之校验新文件）
- `docs/PLAN.md`、`docs/progress-avalonia-detail.md`

**不新增**
- `MantisZip.UI.Avalonia.csproj` **不加** `SharpCompress` 显式引用（UI 已通过 Core 传递引用使用它；ZIP 解包走 in-box `System.IO.Compression.ZipFile`，见 §2）

## 6. 验证

```powershell
# 构建 + 测试（走仓库唯一实现，退出码 0 = 通过）
pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia
pwsh -File scripts\verify-repo.ps1 -Task test  -Target ui-tests
# 规则 8 守卫（IconCatalog 抽取后必跑：117 条登记必须仍全绿）
pwsh -File scripts\verify-repo.ps1 -Task guards
# 规则 16 自查（期望仅 CustomFilePickerDialog.SystemBrowse_Click 命中）
git grep -n -E "OpenFilePickerAsync|SaveFilePickerAsync|OpenFolderPickerAsync" -- 'src/*.cs'
```

- `lsp_diagnostics` 无错误。
- 手动：导入合法包 / 非法 SVG 包 / Zip Slip 包 / 损坏 manifest / 保留 id 包；切风格即时生效；覆盖/恢复；删除当前选中包立即回退 A；重启后选中风格保持；三语文案；便携模式（`Portable.txt`）下写入 `Data/`。

## 7. 边界 / 非目标

- **不做**：C/D/E/F/G 内置套内容（计划 3）；`assoc/*.ico`（D6 留口，本期不接）。
- **渐变/图案填充**：v1 **不渲染** → 该图标**跳过 + 警告**（D-C，非整包拒绝）；渐变解析支持留作后续小计划，届时取消降级。
- **不引依赖**：SVG 解析自实现（只取几何+填充，不引 Svg.Skia 渲染；Svg.Skia 已是预览依赖但此处**不用**它执行 SVG，避免执行面）。
- **不改**：第①层 A（永远可用，缺键兜底）；`AppSettings` 其它字段。
- **不做文件监听**：落盘为解析产物（D-D），外部直接改 json 不自动生效。

## 8. 风险

| 风险 | 缓解 |
|------|------|
| SVG 解析覆盖不足致大量合法包被拒 | v1 明确结构支持集（§6.4）；**渐变降级跳过**（D-C）避免常见的整包误拒；失败/警告清单对话框让用户可定位；后续扩渐变/`use` |
| 导入包 id / 文件名注入 | id 归一化（仅 `[a-zA-Z0-9_-]`）+ **保留字 `A`–`G` 拒绝**；`SanitizeEntryPath` 防 Zip Slip；落盘目录固定前缀 |
| 第②层内存字典 vs avares 字典混装 | 统一用 `IResourceProvider` 接口 Add；测试覆盖两种来源 |
| 启动期同步解析大包拖慢启动 | 落盘 `Icons.json` 缓存解析产物；启动仅反序列化 + 构建 DrawingImage |
| 覆盖/删除写盘失败留半成品 | 原子写（临时目录 → rename）；失败清理 + 错误框 |
| provider / `_loadedIconThemeId` 缓存导致「改了不生效」 | `RefreshIconTheme()` 强制清 `_iconThemeProviders[id]` + 清 `_loadedIconThemeId`；T4/T5 各有一条缓存回归测试 |
| `IconTestViewModel` 含 13 组 / 20 条重复行 → 计数虚高/重复卡片 | 统一走 `IconCatalog`（`Distinct(ResourceKey)`，117 键）；`Contexts` 保留全部使用点；T2/T7 断言 == 117 |
| 非当前包缩略预览空白 | 预览用 `IconThemeBuilder` 预构建 `DrawingImage`，不经 `DynamicResource` 查找；T6 断言非空 |
| **描边类图标（Lucide/Feather/Tabler）被当填充渲染** | 解析器 v1 支持 `stroke` → `GeometryDrawing.Pen`（D-E）；计划 1 `IconView.Render` 透传 Pen；T1 有 `fill="none" stroke=…` 回归断言 |
| **注册表抽到 `IconCatalog` 后规则 8 守卫全红** | T2 同一 commit 改 `GUARD_FILES.iconTestVm` 并跑 `-Task guards`；未改则 117 条「未登记」会立刻暴露（不会静默通过） |
| **`Geometry.Parse` × 117 键拖慢 ApplyTheme** | T4 实测「导入包装配」耗时；> 60ms 则启动只装当前主题、其余按需 |
| **`SanitizeEntryPath` 只净化不拒绝 → 以为防住了 Zip Slip** | 净化后再 `GetSafePath` 复核、抛异常即整包拒绝；T3 用 `../../evil.txt` 断言不落盘 |
| Windows 下「只读目录」测不出写盘失败 | 改用独占句柄（`FileShare.None`）或可注入 `IFileSystem` 制造 `IOException`；T8 明确该手法 |
