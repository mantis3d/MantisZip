# 元数据显示位置可选：侧边面板 / 正文区 (Metadata Display Location)

> **状态**: 📋 待实施（方案已与用户确认，等用户决定启动）| **创建日期**: 2026-10-31
> **关联计划**: `preview-extended-formats.md`（PE 预览的来源）、`floating-preview-panel.md`（面板停靠，本计划不改变停靠行为）
> **前置**: 无（纯 Avalonia + Core 元数据模型改动；规则 11：新功能只进 Avalonia）
> **决策来源**: 2026-10-31 与用户讨论确认 —— ① 范围只给「元数据即全部内容」的格式（当前仅 `pe`）；② 面板行为取 B1（面板不整块消失，只把「XX 信息」分区搬到正文区，面板保留「文件信息」）；③ 入口放设置 →「元数据面板」页、类型下拉下方加「显示位置」；④ 正文区用竖排「标签：值」多行。

## TL;DR

exe 预览的正文区只有一行条目名标题，下面大片空白；真正有用的信息全在右侧元数据面板，阅读时要左右来回看。本计划给**按类型**增加「显示位置」：`侧边面板`（现状，默认）或 `正文区`。选「正文区」后，该格式的元数据分区（如「程序信息」）改为在正文区竖排渲染，面板里只剩「文件信息」，不再重复显示格式字段。仅对**没有其它正文内容**的格式开放（当前只有 `pe`，图片/文本/PDF 等不出现该选项）。

---

## 1. 背景

exe/dll 这类文件没有可预览的正文，`PreviewPanel` 的正文区除标题外是空的（`src\MantisZip.UI.Avalonia\Views\PreviewPanel.axaml:306` 的 `PeTitle`）。用户在设计评审里提出的诉求原话（m02096）：

> 既然 exe 文件只有元数据可以显示，内容面板相当于是空的，那么能不能加个选项，让用户可以选择把 exe 文件元数据显示在内容面板，同时隐藏掉元数据面板。

讨论后收窄为下面的定义（避免把图片/文档的正文挤变形，也避免「文件信息」凭空消失）。

## 2. 已确认的产品定义

| 项 | 决定 | 说明 |
|---|---|---|
| 开放范围 | **只给「元数据即全部内容」的格式** | 当前仅 `pe`。其它类型（image / text / pdf / video / audio / docx / sqlite / iso / torrent / font / csv / gif）不出现「正文区」选项，单选禁用并给一行提示 |
| 面板行为 | **B1：面板不整块消失** | 格式分区（如「程序信息」）搬到正文区；面板里保留「文件信息」（文件名 / 大小 / 压缩后大小 / 压缩率 / 修改日期），格式字段不在面板重复出现 |
| 默认值 | **侧边面板** | 老配置（`metadata-panel.json` 无该键）反序列化后自动取默认，行为与现在完全一致；不擅自改所有人的界面 |
| 入口 | 设置 →「元数据面板」页 | 类型下拉（`MetadataPanelSettings.TypeOptions`）下方新增一行「显示位置」：⬤ 侧边面板 / ○ 正文区 |
| 正文区排版 | **竖排「标签：值」多行** | 分区标题（「程序信息」）+ 字段按现有 `Order` 顺序；宽度吃满；不压缩成两列网格（著作权等长文本要能看全） |
| contentTop | 该模式下**并入正文列表** | 本机旧配置里 `ProductName` 被放在 `contentTop`；若继续画内容区顶部横条会与正文列表重复，故 `contentArea` 模式下 `ContentTopItems` 为空，所有非 `hidden` 字段统一进正文分期 |
| 全局面板开关 | 仍然有效 | `AppSettings.ShowPreviewInfoPanel` 关掉时面板整块隐藏（含「文件信息」）；正文区元数据属于「内容」，不受该开关影响 |
| 分支 | `alpha`（新功能） | 规则 19：`beta` 是除错分支 |

## 3. 现状锚点

**数据模型**（`src\MantisZip.Core\Models\MetadataPanelSettings.cs`）
- `MetadataPanelSettings { SectionOrder, Types, FieldLayoutMode }`
- `TypeMetadataConfig { Enabled = true, Fields }` ← 本计划在这里加 `Location`
- `FieldConfig { Position = "infoPanel", Order = 10, Row = 0 }`，`Position` ∈ `infoPanel` / `contentTop` / `hidden`

**注册表**（`src\MantisZip.Core\Utils\MetadataRegistry.cs`）
- `Register(typeKey, MetadataFieldDef[])`，PE 组在 `:138` 起注册 10 个字段（产品名称 / 公司 / 文件版本 / 产品版本 / 架构 / 子系统 / 说明 / 原始文件名 / 语言 / 著作权）
- `GetAllTypeKeys()` / `GetFields(typeKey)`；**没有**「该类型是否只有元数据」的概念 → 本计划要加

**渲染管线**
- `src\MantisZip.UI.Avalonia\Services\MetadataRenderEngine.cs`：`RenderCommon(values, settings)` / `RenderFormat(values, settings, typeKey)` → `MetadataRenderResult { InfoPanelSections, ContentTopItems }`；`BuildFormatSection` 分区标题 = `T($"Metadata_Type_{typeKey}")`，字段名 = `T($"Metadata_Key_{fieldKey}")`；`BuildRows` 按 `FieldConfig.Row` 分行、`Order` 排序、只取 `Position == "infoPanel"`
- `src\MantisZip.UI.Avalonia\ViewModels\MetadataHelper.cs`：`RenderCommonToViewModel` / `RenderFormatToViewModel`（两者都 `MetadataSettingsManager.Load()` 读磁盘，**没有可注入的 settings 参数** → 测试需要加可选参数）
- `src\MantisZip.UI.Avalonia\Services\MetadataSettingsManager.cs`：`Load()` / `Save()` 读写 `DataDir\metadata-panel.json`；`InitializeDefaultConfig` 会为缺失字段补 `FieldConfig`（默认 `infoPanel` / `Order = fields.Length*10`）

**视图**
- `src\MantisZip.UI.Avalonia\Views\PreviewPanel.axaml`：`PreviewContentScroller`（`:194`）→ `ContentTopBorder` + `ItemsControl(ContentTopItems)`（`:198`/`:202`）→ CSV / PE 标题（`:306`）/ 图片 / GIF / 字体 / SQLite / Office / WebView 等正文块；面板 `PreviewInfoBorder`（`:793`，`IsVisible=IsInfoPanelEffectiveVisible`）内 `FormatSections`（`:803`）+ `CommonSections`（`:856`）
- `src\MantisZip.UI.Avalonia\ViewModels\PreviewViewModel.cs`：`ShowPe(filePath, displayTitle)` 填 `FormatSections`；属性 `FormatSections` / `CommonSections` / `ContentTopItems` / `HasFormatSections` / `IsFormatPending` / `IsInfoPanelEffectiveVisible`
- `src\MantisZip.UI.Avalonia\ViewModels\MainWindowViewModel.Preview.cs:349`：PE 分支 `Preview.ShowPe(tempFile, entry.NameDisplay)`
- 设置页：`Views\SettingsWindow.axaml:1112` 起「元数据面板」Tab（`:1124` 类型下拉，`:1153` 字段表，`:1208` 面板预览），VM `MetadataPanelSettingsViewModel`

## 4. 设计

### 4.1 Core：`TypeMetadataConfig.Location`

```csharp
/// <summary>
/// 元数据显示位置："infoPanel"（侧边/下方面板，默认）或 "contentArea"（正文区）。
/// 只有 MetadataRegistry 登记为「元数据即全部内容」的类型才允许 contentArea。
/// </summary>
public string Location { get; set; } = "infoPanel";
```

- 反序列化用 `JsonDefaults.CaseInsensitive`（规则 23），旧文件缺键 → 保持默认 `infoPanel`。
- 不做字段级迁移：`InitializeDefaultConfig` 只补 `Fields`，`Location` 靠属性默认值即可。

### 4.2 Core：能力登记

`MetadataRegistry` 增加：

```csharp
/// <summary>只有这些类型的预览没有其它正文内容，允许把元数据放到正文区。</summary>
public static bool IsMetadataOnly(string typeKey);

/// <summary>登记类型时声明是否「元数据即全部内容」。当前只有 "pe"。</summary>
private static void Register(string typeKey, MetadataFieldDef[] fields, bool metadataOnly = false);
```

- 全部现有 `Register(...)` 调用不动（`metadataOnly` 默认 false），只给 `pe` 传 `true`。
- 将来若出现别的纯元数据格式（如某些只显示信息的解析器），它在注册处加一个 `true` 就自动获得选项。

### 4.3 渲染引擎：新增正文区分区

`MetadataRenderResult` 增加：

```csharp
/// <summary>「正文区」模式下要渲染的分区（contentArea 之外恒为空）。</summary>
public ObservableCollection<MetadataSection> ContentAreaSections { get; set; } = [];
```

`RenderFormat` 内部按 `settings.Types[typeKey].Location` 分流：

| Location | InfoPanelSections | ContentTopItems | ContentAreaSections |
|---|---|---|---|
| `infoPanel`（默认） | 现有逻辑 | 现有逻辑 | 空 |
| `contentArea` | **空** | **空** | 1 个分区（标题 `Metadata_Type_pe`），行 = 所有 `Position != "hidden"` 的字段，按 `Order` 升序、**忽略 `Row`**（竖排一行一字段） |

- 非 `metadataOnly` 类型即使配置里被写成 `contentArea`，也按 `infoPanel` 渲染并 `CoreLog.Trace`（手工改 JSON 的兜底，不崩、不误判）。

### 4.4 VM / 视图

- `PreviewViewModel` 新增 `ContentMetadataSections`（`ObservableCollection<MetadataSection>`）与 `HasContentMetadata`；在 `Clear()` / `ShowLoading()` / `ShowUnsupported()` 里复位。
- `MetadataHelper.RenderFormatToViewModel`：设置 `vm.ContentMetadataSections = result.ContentAreaSections`、`HasContentMetadata = Count > 0`；其余赋值保持不变（contentArea 模式下 `FormatSections` 自然是空集合 → 面板只显示「文件信息」，B1 无需额外代码）。
- 顺手给 `RenderCommonToViewModel` / `RenderFormatToViewModel` 加可选参数 `MetadataPanelSettings? settings = null`（null 时 `MetadataSettingsManager.Load()`），供测试与设置页预览复用（试运行不读磁盘）。
- `PreviewPanel.axaml`：在 PE 标题之后新增 `ItemsControl`（`IsVisible="{Binding HasContentMetadata}"`），模板与面板一致（分区标题小字 + 「标签：值」两列竖排），放在 `PreviewContentScroller` 内让它跟随正文滚动；加中文注释说明这是「元数据显示位置 = 正文区」时的渲染位。

### 4.5 设置页

`MetadataPanelSettingsViewModel`（`src\MantisZip.UI.Avalonia\ViewModels\MetadataPanelSettingsViewModel.cs`）新增：

```csharp
public bool IsContentAreaAvailable => SelectedType is { } t && MetadataRegistry.IsMetadataOnly(t.Key);
public bool IsLocationInfoPanel { get; set; }     // 绑定单选
public bool IsLocationContentArea { get; set; }
public string LocationText, LocationInfoPanelText, LocationContentAreaText, LocationUnavailableText;
```

- `SelectedType` 变化时：同步 `IsLocationInfoPanel` / `IsLocationContentArea`（从该类型 `TypeMetadataConfig.Location` 读）、刷新 `IsContentAreaAvailable`、`RebuildPreview()`。
- 写入：切换单选即写 `_settings.Types[key].Location`，随后 `RebuildPreview()`；保存仍走现有确认流程。
- `SettingsWindow.axaml:1124` 类型下拉那一行下方插入一行 radio；`IsContentAreaAvailable == false` 时两个 radio 禁用（退回侧边面板）并显示提示文案。
- 预览：`RebuildPreview()` 里 `contentArea` 时按「文件信息 + XX 信息（竖排）」样式生成 `PreviewSections`，与正文区实际效果一致；`infoPanel` 时维持现状。

### 4.6 本地化（规则 13，三语各 4 个 key）

| key | zh-CN | zh-TW | en |
|---|---|---|---|
| `Metadata_Panel_Location` | 显示位置 | 顯示位置 | Display location |
| `Metadata_Panel_LocationInfoPanel` | 侧边面板 | 側邊面板 | Side panel |
| `Metadata_Panel_LocationContentArea` | 正文区 | 內容區 | Content area |
| `Metadata_Panel_LocationUnavailable` | 该格式有正文内容，元数据固定在侧边面板 | 該格式有內容，中繼資料固定在側邊面板 | This format has content, so metadata stays in the side panel |

- 插入位置：`Metadata_Panel_FieldLayout` 之后；三份文件 UTF-8 **with BOM** + CRLF（**禁止** `WriteAllLines` 整文件重写，历史上导致过 4200 行假 diff）；改完跑 `-Task i18n`。

## 5. 实现步骤

| # | 文件 | 改动 | 验证 |
|---|---|---|---|
| 1 | `Core\Models\MetadataPanelSettings.cs` | `TypeMetadataConfig.Location` | `-Task build -Target core` |
| 2 | `Core\Utils\MetadataRegistry.cs` | `Register(..., metadataOnly)` + `IsMetadataOnly`，`pe` 传 `true` | Core 单测 |
| 3 | `UI\Services\MetadataRenderEngine.cs` | `ContentAreaSections` + 按 `Location` 分流 | 单测：contentArea 下 InfoPanelSections/ContentTopItems 为空、ContentAreaSections 1 分区 |
| 4 | `UI\ViewModels\MetadataHelper.cs` | 新集合赋值 + `settings` 可选参数 | 单测 |
| 5 | `UI\ViewModels\PreviewViewModel.cs` | `ContentMetadataSections` / `HasContentMetadata` + 复位 | 单测 |
| 6 | `UI\Views\PreviewPanel.axaml` | 正文区 `ItemsControl` + 中文注释 | `-Task build -Target avalonia` |
| 7 | `UI\ViewModels\MetadataPanelSettingsViewModel.cs` + `Views\SettingsWindow.axaml` | 「显示位置」单选 + 不可用提示 + 预览切换 | 手动 + `-Task guards` |
| 8 | `Localization\strings.{zh-CN,zh-TW,en}.json` | 4 个 key × 3 语言 | `-Task i18n` |
| 9 | `tests\MantisZip.Tests\Utils\MetadataDisplayLocationTests.cs`（新）、`tests\MantisZip.UI.Avalonia.Tests\MetadataContentAreaTests.cs`（新） | 见 §6 | `-Task test -Target tests` / `-Target ui-tests` |
| 10 | 文档 | PROGRESS / progress-avalonia-detail / ARCHITECTURE 的元数据面板小节 / RELEASE_NOTES（按规则 18 先问范围） | `-Task check` |

## 6. 测试计划

**Core**
- `MetadataPanelSettings` 缺 `Location` 键反序列化 → `"infoPanel"`（防止老配置被静默置空）。
- `MetadataRegistry.IsMetadataOnly("pe") == true`；`"image" == false`。

**渲染（不读磁盘，走 §4.4 的 `settings` 参数）**
- `contentArea` 配置：`InfoPanelSections.Count == 0`、`ContentTopItems.Count == 0`、`ContentAreaSections.Count == 1`、行序按 `Order`、`hidden` 字段不出现、`contentTop` 字段出现（并入）。
- `infoPanel` 配置：`ContentAreaSections.Count == 0`，其余与现在一致（回归）。
- 非 `metadataOnly` 类型被配成 `contentArea` → 退回 `infoPanel` 渲染且不抛异常。

**UI（无头 xunit）**
- `PreviewViewModel.ShowPe` 在 `contentArea` 配置下：`HasContentMetadata == true`、`FormatSections.Count == 0`、`HasFormatSections == false`、`IsFormatPending == false`、`PeTitle == 条目名`。
- `infoPanel` 配置下：`FormatSections.Count > 0`、`ContentMetadataSections.Count == 0`（回归）。
- 设置页 VM：`SelectedType = pe` → `IsContentAreaAvailable == true`；`SelectedType = image` → false 且强制 `IsLocationInfoPanel`。

## 7. 风险与边界

- **设置页预览与真实渲染漂移**：预览用同一份 `MetadataRenderEngine`（注入 settings），避免两套逻辑。
- **旧配置里 `contentTop` 的字段**（本机 `ProductName` 就是）：`contentArea` 模式下并入正文列表；切回 `infoPanel` 时恢复原样，不擅自改用户的 `Position`。
- **正文区滚动嵌套**：新 `ItemsControl` 放在已有的 `PreviewContentScroller` 内（不新增 ScrollViewer），避免嵌套滚动。
- **全局开关语义**：`ShowPreviewInfoPanel = false` 只隐藏面板，正文区元数据照常显示（正文是内容）。这与「类型级 Location」不冲突，文档里写清楚。
- **JSON 写入事故**：三份 `strings.*.json` 只允许 `ReadAllText` → 正则替换 → `WriteAllText(UTF8Encoding($true))`。
- **`.partN.rar` 无关**：本计划不碰引擎与分卷逻辑。

## 8. 工作量

约 **6–8h**：Core 模型 + 登记 1h（含单测）、渲染分流 1.5h、VM + XAML 1.5h、设置页 1.5h、i18n 0.3h、UI 测试 1.5h、文档 0.5h。

## 9. 未决事项（实施前确认）

1. 正文区是否保留字段的「分行（Row）」概念 —— 本计划按「竖排一律一行一字段」处理（与 §2 的竖排决定一致），若以后想要并排再说。
2. 是否顺手把 `font`（字体预览目前有样本文字 + 元数据）纳入开放名单 —— 本计划**不纳入**，只 `pe`。
3. 设置页「显示位置」两条单选是否需要独立的小标题分组 —— 实施时按现有设置页风格决定（优先复用 `SpacingXsThk` 等紧凑度资源）。
