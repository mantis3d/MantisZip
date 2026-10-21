# 图标主题系统 — 计划 2：图标包导入 + 单图标覆盖 + 设置 UI

> **状态**: 📋 未开始 | **创建**: 2026-10-15 | **优先级**: P2 | **预估工时**: 20-26h（2026-10-21 复核后由 16-22h 上调：保留字 id、删包回退、provider 缓存失效、缩略预览、`IconCatalog` 抽取）
> **来源**: [docs/superpowers/specs/2026-10-15-icon-theme-system-design.md](../../docs/superpowers/specs/2026-10-15-icon-theme-system-design.md)（已批准，§6/§7/§8）
> **前置**: **计划 1**（`icon-theme-infra.md`）必须已完成 —— 本文依赖其交付的 `IconView`（`SourceKey`）/ `AppSettings.SelectedIconTheme` / `App.axaml.cs` 的 `GetIconThemeProvider(string id)` + `ResolveIconThemeUri(string? id)` + `_iconThemeProviders` 缓存 / `AddIconOverrideLayer()` 空桩。
> **后续**: 计划 3（C/D/E/F/G 内容生产）可选独立进行；导入管线与之无耦合。
> **规格偏差（D-C，2026-10-15 决策）**: 规格 §6.4/§8 原为「任一 SVG 不兼容 → **整包原子拒绝**」。现调整：**渐变/图案填充**类不兼容 → **跳过该图标 + 计入警告清单（非静默）**，不阻断整包；**结构性不兼容**（`clipPath`/`mask`/`filter`/`text`/`use`/嵌套 `<svg>`/外链）、解析损坏、XXE/DTD、Zip Slip 仍按**原子拒绝**。理由：真实图标包常用渐变，整包拒绝体验过差；且渐变属「填充画刷」而非「元素结构」，分类处理合理。渐变解析支持留作后续小计划（届时取消该降级）。
> **规格偏差（D-D，2026-10-21 复核决策）**: 规格 §6.5 的落盘形态为「`icon-packs/<id>/` 解压内容 + `icon-overrides/IconXxx.svg`」。本文改为**解析产物缓存**：`icon-packs/<id>/Icons.json`（`IconPackData`）+ `icon-overrides/IconXxx.json`（`ParsedIconData`）。理由：避免每次启动重解析全部 SVG（大包启动耗时与解析失败面）；ZIP 解压目录仅在导入期作临时目录使用，成功后删除。副作用：用户无法直接改落盘文件生效（须经图标管理器重新替换），已在 §3.5 说明。

## 1. 目标与交付标准

在计划 1 的三层字典骨架上补齐 **第②层（导入包）** 与 **第③层（单图标覆盖）** 的数据生产与用户入口：

1. SVG 图标包（文件夹 / ZIP）导入 → 覆盖率校验 → 落盘 → 即时替换第②层 → 持久化。
2. 单个图标覆盖（图标管理器逐键替换 / 恢复）→ 即时生效于第③层。
3. 设置「外观」Tab 新增**图标风格分区**（风格下拉 + 导入 + 已导入列表 + 打开图标管理器）。
4. 新增 **`IconManagerWindow`**（搜索 / 过滤 / 网格卡片 / 替换 / 恢复，数据源 = `IconTestViewModel` 注册表契约）。
5. 全部错误路径按 §8 处理（原子拒绝 / 静默回退 / TraceLog 脱敏）。

**DoD**：导入 happy 路径与全部失败路径通过；覆盖/恢复回路生效；三语 key 成对；构建 0 错误；新增测试全绿。

## 2. 现状核实

| 项 | 事实 | 位置 |
|----|------|------|
| 覆盖率契约 | `IconTestViewModel.LoadAllIcons()` 共 **136 条 `Add(...)`**：1 条 `resourceKey=null` + **19 条重复键** → **116 个去重键**。`Add` 是 **private** 辅助方法；可枚举的是 `Icons`（`ObservableCollection<IconTestItem>`，`ResourceKey` 可空）。**任何覆盖率/差异计算必须 `Distinct()`** | `ViewModels/IconTestViewModel.cs:14`（属性）/`:383`（`Add`）/`Models/IconTestItem.cs:21` |
| 解压依赖 | `SharpCompress 0.50.4` **只在 Core**；UI 无直接 `PackageReference`，靠 `ProjectReference` 传递。Zip Slip 防护用 Core `FileConflictHelper.SanitizeEntryPath(string)`（另有 `GetSafePath(dir, entryName)`） | `MantisZip.Core.csproj:24` / `MantisZip.UI.Avalonia.csproj:61` / `Core/Utils/FileConflictHelper.cs:213`（`:193`） |
| 选择器（规则 **16**） | `CustomFilePickerDialog.ShowOpenFileAsync(Window owner, string? initialPath = null, string[]? fileExtensions = null, IReadOnlyList<FileTypeOption>? fileTypes = null)`（ZIP）/ `ShowOpenItemsAsync(Window owner, string? initialPath = null)`（文件夹） | `Dialogs/CustomFilePickerDialog.axaml.cs:192` / `:221` |
| 设置持久化 | `AppSettings` POCO + `Load()`/`Save()`；设置窗口保存后调 `App.RefreshTheme()`（`internal static`，**`App.Theming.cs:99`**，非 `App.axaml.cs:537`——后者是 `MaxDebugLogFileSize` 常量） | `Models/AppSettings.cs` / `App.Theming.cs:99` |
| 外观 Tab | **`SettingsWindow.axaml:675-754`**（主题/紧凑度/最近文件/界面字体）；锚点 `controls:TabItemExtensions.TabIcon="IconPaintBrush"`（`:677`），`IconPaintBrush` 为 `AppIcons.axaml:30` 真键。**注意 `:309-375` 是「压缩默认选项」，勿误改** | `Views/SettingsWindow.axaml:675-754` |
| 存储目录 | 必须用 **`AppSettings.DataDir`（`:232`）**，禁止硬编码 `%LOCALAPPDATA%`：便携模式（exe 旁 `Data/`，`IsPortableMode:229`）与常规模式（`%LOCALAPPDATA%\MantisZip`）由静态构造 `:246-266` 解析 | `Models/AppSettings.cs:229/232/246-266` |
| 本地化 | 三语 key 集必须对称（`AboutWindowTests.AllThreeLanguages_HaveSameKeySet`）。**`SettingsWindowViewModel` 无 `LocalizedStrings` 索引器**：用个体属性（`public string Xxx => LocalizationManager.T("Key")`）+ `OnCultureChanged` 逐属性 `OnPropertyChanged`（`:1201` 订阅、`:1416` 刷新）。仅 `MainWindowViewModel` 有 `LocalizedStrings` 字典（`:235-309`）→ **`UpdateLocalizedStrings()` 登记只对 MainWindow 系 XAML 生效**；设置页与 `IconManagerWindow` 走个体属性 | 规则 13 / `ViewModels/SettingsWindowViewModel.cs:1201,1416` |
| 元数据面板先例 | `MetadataSettingsManager`（静态类 + 独立 json `metadata-panel.json` 于 `AppSettings.DataDir` + `SettingsChanged` 事件驱动刷新）是「独立持久化 + 事件刷新」的既有模式，可对齐 | `Services/MetadataSettingsManager.cs:13,22` |

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
public sealed class IconPackEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Author { get; set; }
    public string? License { get; set; }
    public int CoveredKeys { get; set; }
    public int MissingKeys { get; set; }
    public string? PreviewPng { get; set; } // 可选缩略图相对路径
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
    public string Path { get; set; } = ""; // 归一化到 24×24 的 path 数据
    public string? Fill { get; set; }      // null = 跟随 Foreground；"#RRGGBB"/"#AARRGGBB"
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

- **支持**：`path`(d) / `circle` / `rect` / `line` / `polygon` / `polyline`；solid 填充（`#rgb`/`#rrggbb`/`#aarrggbb`/`none`/`currentColor`）；`viewBox` 归一化到 24×24（`Geometry.Transform` 或 `TransformGroup`）；多部件多色。
- **不支持 → `SkippedUnsupportedPaint`（D-C，跳过该图标 + 警告，不阻断）**：`linearGradient` / `radialGradient` / 图案（`pattern`）填充。仅当**该图标**用到这些画刷时跳过它；同包其它图标照常导入。
- **不支持 → `Error`（结构性，触发原子拒绝）**：`clipPath` / `mask` / `filter` / `text` / `use` / 嵌套 `<svg>` / 外链（`href`/`xlink:href`）。
- `fill` 缺省或 `none`/`currentColor` → `ParsedPart.Fill = null`（跟随 `Foreground`）。
- 归一化：计算 `viewBox` 尺寸 → 缩放 + 平移，使内容映射进 24×24。

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

1. 源为 ZIP → 解压到 `AppSettings.GetTempDir()`（`:238`）下的临时目录（SharpCompress；条目名经 `FileConflictHelper.SanitizeEntryPath` 防 Zip Slip）；源为文件夹 → 直接读。**导入结束（成功或失败）必须删除临时目录**。
2. 读 `manifest.json`（可选）：损坏 → 记 `Warnings` + 按键名约定继续（不阻断）。
3. 解析包 id：`manifest.id` 归一化（仅 `[a-zA-Z0-9_-]`）；缺省 → 源文件夹/ZIP 文件名归一化。**保留字校验：归一化结果若命中内置主题 id `A`–`G`（忽略大小写）→ 直接返回失败「该 id 为内置主题保留」**（否则 `ResolveIconThemeUri` 先匹配内置，包永远解析不到且 registry 同 id 冲突）。
4. 枚举 `icons/*.svg`（或按 `manifest.mapping`）→ 键名 = `IconPascalCase`（文件名去 `.svg`，或映射目标）。
5. 每个 SVG 过 `SvgIconParser.Parse`：
   - `Ok` → 收集部件；
   - `SkippedUnsupportedPaint`（渐变/图案）→ 记 `Warnings`（文件 + 原因）、**跳过该键**，继续处理其余；
   - `Error`（结构性/解析损坏）→ 记 `Failures`，**整包原子拒绝**。
6. 覆盖率校验：对照 116 键清单（`IconTestViewModel.Icons` 的 `ResourceKey` **`.Distinct()`**，见 §2）→ `Covered`/`MissingKeys`（缺键只警告，含被跳过的渐变键）。
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
- `registry.json` 序列化 round-trip 测试（§9.2）。
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

> 计划 1 的实际契约（`icon-theme-infra.md:123/139/147/152`）：`private readonly Dictionary<string, IResourceProvider> _iconThemeProviders` + `GetIconThemeProvider(string id)`（内部 `ResolveIconThemeUri` → `AvaloniaXamlLoader.Load` → 缓存）+ `ResolveIconThemeUri(string? id)` + `AddIconOverrideLayer()` 空桩。**不存在 `AddMergedDictionary(string uri)`**。

- `AddIconOverrideLayer()` 真实实现 → `IconOverrideService.BuildOverrideDictionary()` 结果 Add 为第③层。**每次重建前先移除上一次添加的第③层实例**（保存该实例引用或索引），避免 `RefreshIconTheme` 反复调用后层数无限增长。
- `GetIconThemeProvider(string id)` 改为**双来源**（保持返回 `IResourceProvider` 的签名不变，`ApplyTheme` 装配代码零改动）：
  - 内置 `"A"`…`"G"` → 现有路径 `AvaloniaXamlLoader.Load(new Uri(ResolveIconThemeUri(id)))`；
  - 已导入包 → `IconThemeBuilder.BuildDictionary(IconPackData)`（内存 `ResourceDictionary`，天然是 `IResourceProvider`）；
  - 未知 id / 文件缺失 / 反序列化异常 → **静默回退 A** + TraceLog（§8，启动路径不弹窗）。
- `ResolveIconThemeUri(id)` 只负责**内置套的 avares URI**（`"A"`…`"G"`，计划 3 扩 C–G）；导入包不产生 URI，由上面分支直接构造。
- **新增 `internal static void RefreshIconTheme()`**（对齐 `RefreshTheme()`，`App.Theming.cs:99`）：设置窗口切风格 / 导入 / 覆盖 / 删包后调用，无需重启。实现必须做两件事，缺一不生效：
  1. `_iconThemeProviders.Remove(id)`（导入覆盖、删除、或当前 id 的字典已变）——否则命中旧 provider；
  2. 清 `_loadedIconThemeId`（计划 1 的双条件早退依据）——否则 `ApplyTheme()` 直接 return，界面不刷新。
- 新增 `Services/IconThemeBuilder.cs`：`ParsedIconData` → `Geometry.Parse` → `GeometryDrawing`（`Fill == null` 时**不设 Brush**，跟随 `IconView.Foreground`；非 null → `SolidColorBrush(Color.Parse(fill))`）→ `DrawingImage` → `ResourceDictionary`（键 = `IconPascalCase`）。这是运行时唯一「字符串 path → 资源字典」的转换点，导入包、覆盖层、缩略预览三处共用。

### 3.7 设置 UI（`SettingsWindow.axaml`「外观」Tab 新增分区）

在 `SettingsWindow.axaml:675-754` 外观 Tab 内追加「图标风格」`StackPanel`（规则 4/5/6/13/14；锚点 `TabIcon="IconPaintBrush"` 在 `:677`）：

- 下拉 `ComboBox`：A/C/D/E/F/G + 已导入包；`SelectedItem` 双向绑定 → `RefreshIconTheme()`，即时生效无「应用」按钮。
- **缩略预览（4 图标）**：`ItemTemplate` 用 `IconView`，但 `SourceKey` 只能解析**当前已装载**的第②层 → **非选中包预览必须走 `IconThemeBuilder`**：VM 为每个候选 id 预先构建 `PreviewImages: DrawingImage[]`（内置套从对应 `Icons.X.axaml` 取、导入包从 `Icons.json` 构建），模板绑 `IconView Source="{Binding ...}"`。**不要**依赖 `DynamicResource` 查非当前层（查不到会空白）。
- `[导入图标包…]` 按钮 → 规则 **16** `CustomFilePickerDialog`（`ShowOpenFileAsync` 限 ZIP / `ShowOpenItemsAsync` 允许文件夹）→ `IconPackImporter` → 失败弹「失败文件清单」对话框；`Warnings` 非空弹「部分图标已跳过」。
- **已导入列表** `ListBox`：`name（覆盖率 x%，缺 N 键）` + 删除按钮（删前确认）→ `IconPackRegistry.Remove`（内含删当前选中包时回退 A，见 §3.4）→ 刷新。行高按规则 7 设 `ControlHeightMd`。
- `[打开图标管理器…]` 按钮 → `IconManagerWindow`（显示已覆盖数量徽章）。
- 说明文案：「图标风格只影响应用内图标；文件关联与右键菜单图标在各自设置中单独管理」。
- **本地化**：`SettingsWindowViewModel` **没有** `LocalizedStrings` 索引器（§2），新增文案用个体属性 `public string Xxx => LocalizationManager.T("Key")`，并在 `OnCultureChanged`（`:1416`）补 `OnPropertyChanged(nameof(Xxx))`；三语 json 成对添加。

### 3.8 图标管理器（`Views/IconManagerWindow.axaml(.cs)` + `ViewModels/IconManagerViewModel.cs`）

- 顶部：搜索框（键名/语义名）+ 过滤徽章（全部 116 / 已覆盖 N / 当前风格缺失 N）+ 覆盖率警告横幅（当前风格缺键时）。
- **数据源 = 新增 `Models/IconCatalog.cs`**：从 `IconTestViewModel.LoadAllIcons()` 抽出的**静态**注册表（`IReadOnlyList<IconCatalogItem>`，`Distinct(ResourceKey)`）。`IconTestViewModel` 改为消费 `IconCatalog`（保持规则 8 的「注册即入测试窗口」不变）。**不能直接用 `IconTestViewModel.Icons`**：它含 136 条（19 条重复 + 1 条 null），网格会出现重复卡片、徽章计数虚高。
- 网格（`WrapPanel`/`ItemsControl`）卡片：`IconView` 预览 + 键名 + 语义名；覆盖率 = `IconCatalog` 键集与第②层键集求差。
- 每卡 `[替换…]`（规则 **16** `ShowOpenFileAsync` 限 `.svg`）+ `[恢复]`。
- 行高遵守规则 7（卡片外层 `MinHeight` 用 `ControlHeightSm`）；文案走规则 13，且 `IconManagerViewModel` 同样用个体属性 + `OnCultureChanged` 刷新（无 `LocalizedStrings` 索引器）。

## 4. 任务分解（TDD；每任务一 commit）

- [ ] **T1 — `SvgIconParser`**
  - 实现 `Parse`（`Outcome` 三态）；`XmlReader` 禁 DTD/实体。
  - **测试**：合法 `path`/`circle`/`rect`/`polygon` → `Ok` + 期望部件数 + `Fill`（含 `none`→null、`currentColor`→null）；`viewBox="0 0 48 48"` → 归一化到 24×24（断言包围盒）；`linearGradient`/`radialGradient` 填充 → `SkippedUnsupportedPaint`（D-C）；`clipPath`/`text`/`use`/嵌套 `svg`/含 DTD 的样本 → `Error`。commit。

- [ ] **T2 — 覆盖率计算 + registry 序列化**
  - `IconPackRegistry`（load/save/upsert/remove + 删当前选中包回退 A）+ 覆盖率（全套/部分/空）。
  - 键集一律来自 `IconCatalog`（`Distinct(ResourceKey)`）。
  - **测试**：`registry.json` round-trip；覆盖率对照 116 键 `Distinct` 集；空包 = 全缺；`Remove` 当前选中 id → `SelectedIconTheme == "A"` 且触发刷新。commit。

- [ ] **T3 — `IconPackImporter`（文件夹 + ZIP，原子性 + 渐变降级）**
  - 解压到 `GetTempDir()`（Zip Slip 防护）+ manifest 容错 + 保留字 id 校验 + 逐 SVG 解析（三态分流）+ 原子拒绝 + 落盘 + 临时目录清理。
  - **测试**：合法文件夹/ZIP → `Ok` + `Covered/Missing`；含 1 个结构性非法 SVG → `Ok=false` 且**不落盘** + `Failures` 含该文件 + 临时目录已删；含 1 个渐变图标 → `Ok=true` + 该键**不在**结果中（`Missing++`）+ `Warnings` 含该文件，其余键正常导入；Zip Slip 条目名 → 拒绝；manifest 损坏 → 仍按键名导入（记 `Warnings`）；**`manifest.id="C"` → `Ok=false` + 错误文案「保留 id」**；`id="My Icons!"` → 归一化为 `My-Icons`。commit。

- [ ] **T4 — 第②层接线（导入包 → 资源字典）**
  - 新增 `Services/IconThemeBuilder.cs`（`ParsedIconData` → `DrawingImage` → `ResourceDictionary`）；`GetIconThemeProvider(id)` 改双来源（内置 XAML / 导入包内存字典），`ResolveIconThemeUri` 只管内置；`RefreshIconTheme()`（清 provider 缓存 + 清 `_loadedIconThemeId` + 重装）。
  - **测试（headless）**：导入 mock 包 → `SelectedIconTheme=id` → `RefreshIconTheme()` 后 `TryGetResource("IconFolder")` 返回该包版本（与 A 不同）；未知 id → 回退 A；**同 id 覆盖导入后再次刷新 → 取到新版本（缓存已失效）**；`Fill==null` 的部件产出 `GeometryDrawing.Brush == null`。commit。

- [ ] **T5 — `IconOverrideService` + 第③层**
  - 实现替换/恢复/BuildOverrideDictionary；`AddIconOverrideLayer()` 接线（重建前移除旧③层实例）。
  - **测试**：替换 1 键 → 第③层命中该键且优先级压过②层；恢复 → 回落②层；损坏 override json → 跳过 + 不抛；**连续调用 `RefreshIconTheme()` 3 次 → `MergedDictionaries` 层数不增长**。commit。

- [ ] **T6 — 设置「外观」Tab 图标风格分区**
  - `SettingsWindow.axaml:675-754` 内新增分区 + `SettingsWindowViewModel`（下拉/缩略预览/导入/已导入列表/删除/打开管理器/说明）。
  - 文案用**个体属性 + `OnCultureChanged`**（非 `UpdateLocalizedStrings()` 登记）+ 三语 key 成对。
  - **测试**：三语 key 对称（现有 `AboutWindowTests`）；VM 切换风格调用 `RefreshIconTheme` 并持久化 `SelectedIconTheme`；**非当前包的缩略预览 `DrawingImage` 非空**（不经资源查找）。commit。

- [ ] **T7 — `IconManagerWindow` + `IconManagerViewModel`**
  - 新增 `Models/IconCatalog.cs` 并让 `IconTestViewModel` 消费它；网格/搜索/过滤徽章/替换/恢复。
  - **测试**：`IconCatalog.Keys` 去重后 == 116（与 `Icons` 的 19 条重复无关）；过滤（已覆盖/缺失）计数正确；替换后徽章 +1、恢复后 -1。commit。

- [ ] **T8 — 错误处理与启动回退**
  - §8 全表：启动时 `SelectedIconTheme` 指向已删包 / 字典装载异常 → 静默回退 A + TraceLog；导入写盘失败 → 错误框 + 清理半成品目录；运行期删当前选中包 → 立即回退 A（不等重启）。
  - **测试**：`SelectedIconTheme="missing"` → `ApplyTheme` 不抛且回退 A；写盘失败用临时只读目录模拟 → 半成品目录被清；`Remove(当前 id)` → 界面层立即为 A。commit。

- [ ] **T9 — 文档与验收**
  - `docs/progress-avalonia-detail.md` 追加（规则 3）；`docs/PLAN.md` 状态更新。
  - 手动 QA：导入 happy + 全部失败路径；替换/恢复回路；6 图标缩略预览。commit。

## 5. 涉及文件

**新增**
- `Models/IconPackManifest.cs`、`Models/IconPackEntry.cs`、`Models/IconPackData.cs`、`Models/IconCatalog.cs`
- `Services/SvgIconParser.cs`、`Services/IconPackImporter.cs`、`Services/IconPackRegistry.cs`、`Services/IconOverrideService.cs`、`Services/IconThemeBuilder.cs`
- `ViewModels/IconManagerViewModel.cs`、`Views/IconManagerWindow.axaml(.cs)`
- 测试：SvgIconParser / registry / importer / override / IconThemeBuilder / 装配 各一套

**修改**
- `App.axaml.cs`（`GetIconThemeProvider` 双来源、`AddIconOverrideLayer` 真实实现、`RefreshIconTheme` 含缓存失效）
- `Views/SettingsWindow.axaml:675-754` + `ViewModels/SettingsWindowViewModel.cs`（外观 Tab 图标风格分区）
- `ViewModels/IconTestViewModel.cs`（改为消费 `IconCatalog`，规则 8 的注册入口保持不变）
- `Localization/strings.{zh-CN,en,zh-TW}.json`
- `MantisZip.UI.Avalonia.csproj`（显式 `PackageReference` SharpCompress，与 Core 版本对齐；不依赖传递引用）
- `docs/PLAN.md`、`docs/progress-avalonia-detail.md`

## 6. 验证

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test  tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
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
| `IconTestViewModel` 含 19 条重复键 → 计数虚高/重复卡片 | 统一走 `IconCatalog`（`Distinct`）；T7 断言去重后 == 116 |
| 非当前包缩略预览空白 | 预览用 `IconThemeBuilder` 预构建 `DrawingImage`，不经 `DynamicResource` 查找；T6 断言非空 |
