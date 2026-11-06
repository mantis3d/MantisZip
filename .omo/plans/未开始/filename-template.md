# 压缩文件名模板（FileName Template）

> **状态**：未开始（2026-11-06 由「文件名后缀模板」改写为「文件名模板」）
> **目标版本**：v0.5.10 尾部缓冲（B 档）
> **预估工时**：8-10h（原后缀模板 2-3h；因新增「三入口默认值 + 命名预设 + 三模式接管」而扩大）
> **前置**：无。`rg "FileNameTemplate|FileNameSuffixTemplate"` 在 `src/`、`tests/` 零命中，没有兼容包袱。

---

## TL;DR

> 用一套占位符模板生成压缩包的**主名**，格式扩展名（`.zip` / `.7z` / `.tar.gz`）由所选格式自动追加，用户不用写扩展名。
>
> - 三个入口各有一个默认模板：**压缩对话框**、`--compress-quick`、`--compress-separate`
> - 命名收敛在 Core 的 `CompressPathPlanner`，**预览树 = 实际输出**
> - 模板非空即接管命名（忽略全局「保留源文件扩展名」开关）；对话框里手动/合并模式的「输出文件名」框变成只读预览
> - 模板可在对话框里存成**命名预设**（名字 + 模板），随时切换
> - 确认时校验：未知占位符 / 非法字符 / 渲染为空 / 多源重名 → `Rejected` + 窗口内原因（规则 20）

---

## Context

### 原始需求（2026-11-06）

> 「它只是后缀模板，我现在想给它改成文件名模板。」

动机不变（同日期同名的压缩包互相覆盖），但表达方式从「在原名后面加尾巴」变成「整个名字由模板决定」，因此必须新增 `{name}`（原名从哪来）与 `{ext}`（原扩展名怎么办）。

### 已定决策（2026-11-06 问答，逐条确认过）

| # | 问题 | 决定 |
|---|---|---|
| 1 | 模板产物 | **主名**，格式扩展名自动补 |
| 2 | 占位符 | `{name}` `{ext}` `{parent}` `{date}` `{datetime}` `{seq}` `{seq:0000}` |
| 3 | 与「保留源文件扩展名」的关系 | `{name}` **不含**扩展名，原扩展名用 `{ext}` 表达；**模板非空时忽略该全局开关** |
| 4 | 非法名 / 重名 | **确认时校验并拒绝**，原因在窗口内显示（`Rejected<CompressSettings>`） |
| 5 | 实时示例 | **要**：模板框下方显示「示例：report_2026-11-06.zip」，取第一个源、随源/格式/模板变化刷新 |
| 6 | 未知占位符 | **确认时拒绝**，提示「未知占位符 {foo}」 |
| 7 | 模板框位置 | **三种输出模式都出现**（模板非空时接管手动/合并的「输出文件名」） |
| 8 | 预设 | **命名预设（名字 + 模板）**，在对话框内增删与切换 |
| 9 | 按入口默认模板 | **三档**：压缩对话框、`--compress-quick`、`--compress-separate`（在设置页配置） |
| 10 | `--compress-quick` 的 `{name}` | 取**第一个源**的主名（与现有 `App.CliCompress.cs:164` 一致） |

### 与旧计划的差异（为什么整篇重写）

| 旧计划写法 | 现在的实际情况 |
|---|---|
| 在 `CompressService.ComputeSeparateOutputPath` 里加后缀 | 那只是薄包装（`CompressService.cs:429-430`）；唯一命名入口是 `CompressPathPlanner.ComputeArchiveName / ComputeOutputPath / PlanSeparate` |
| 执行时给 `baseName` 拼后缀 | 预览与实际共用 B 数据集 `CompressPlan`（`GetOutputPaths` 有 Plan 就不重算，`CompressService.cs:162-175`）。模板必须在**建 plan 时**生效，否则「预览一个名、落盘另一个名」 |
| `CompressRequest.FileNameSuffixTemplate` + 窗口属性回读 | 规则 20：只能走 `CompressSettings` record → `CompressSession.BuildRequest`（`CompressSession.cs:265`），窗口关掉后不许补读 |
| `src/MantisZip.UI.Avalonia/AppSettings.cs` | 实际在 `Models/AppSettings.cs` |
| `HandleCompressQuick` 在 `App.axaml.cs:2052` | 实际在 `App.CliCompress.cs:147`（名字计算在 `:164-166`） |
| XAML code-behind 文本框 + `RefreshOutputPathState` + `{l:L}` | 现在是 `CompressSettingsViewModel` + `LocalizedStrings[...]` 绑定（规则 13） |
| `KeepOriginalExtension` 在压缩对话框里 | 它是**设置窗的全局开关**（`Views/SettingsWindow.axaml:83`），压缩对话框里没有 |
| 只支持 `{date}/{datetime}/{seq}` | 文件名模板必须有 `{name}`，否则多源会全部同名 |

---

## 设计

### 1. 占位符表（唯一事实源：帮助文档 + `FileNameTemplate.Placeholders`）

| 占位符 | 渲染结果 | 说明 |
|---|---|---|
| `{name}` | 源主名 | 文件 = 不含扩展名的文件名；目录 = 目录名；**不含**原扩展名 |
| `{ext}` | 源扩展名（含点） | `report.txt` → `.txt`；目录 → 空串；`.tar.gz` → `.gz`（`Path.GetExtension` 语义） |
| `{parent}` | 父目录名 | `D:\工作\报告\a.txt` → `报告`；盘符根 → 空串 |
| `{date}` | `2026-11-06` | 本批开始时刻，整批共用 |
| `{datetime}` | `2026-11-06_153024` | 同上，精确到秒 |
| `{seq}` | `001` | 1 基序号，3 位补零；按 `SourcePaths` / `SelectedPaths` 顺序 |
| `{seq:0000}` | `0001` | 自定义补零宽度，1-9 位 |
| `{{` / `}}` | `{` / `}` | 字面花括号转义 |

- 占位符**大小写不敏感**（`{NAME}` 等价 `{name}`），渲染前统一转小写匹配。
- 未知占位符（`{foo}`）与未闭合（`{name`）→ 校验失败，不猜测。
- `{seq:N}` 的宽度只接受 1-9 位数字；`{seq:abc}` 视为未知占位符。

### 2. Core 渲染器（新文件）

`src/MantisZip.Core/Utils/FileNameTemplate.cs`

```csharp
namespace MantisZip.Core.Utils;

/// <summary>渲染一次模板所需的上下文（与批绑定：同一批共用一个 Timestamp）。</summary>
public sealed record FileNameTemplateContext(string SourcePath, int Sequence, DateTime Timestamp);

public enum FileNameTemplateErrorKind
{
    UnknownPlaceholder, Unterminated, EmptyResult, InvalidChar, InvalidName, TooLong
}

public sealed record FileNameTemplateError(FileNameTemplateErrorKind Kind, string? Token, string? Rendered);

public static class FileNameTemplate
{
    public const string DefaultExample = "{name}_{date}";
    public const int MaxBaseNameLength = 200;

    /// <summary>只做语法校验（不渲染）：未知占位符 / 未闭合。</summary>
    public static bool TryValidateSyntax(string? template, out FileNameTemplateError? error);

    /// <summary>
    /// 渲染主名（不含格式扩展名）。
    /// 模板为空/纯空白 → 返回 false 且 baseName = null（= 未启用模板，调用方走旧行为）。
    /// 渲染后仍做结果校验：空串、非法字符、结尾点/空格、Windows 保留设备名、超长。
    /// </summary>
    public static bool TryRender(string? template, FileNameTemplateContext ctx,
        out string? baseName, out FileNameTemplateError? error);
}
```

`src/MantisZip.Core/Utils/FileNameTemplatePreset.cs`

```csharp
/// <summary>用户保存的文件名模板预设（对齐 Core/FileFilter/FileFilterPreset 的形态）。</summary>
public class FileNameTemplatePreset
{
    public string Name { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public FileNameTemplatePreset() { }
    public FileNameTemplatePreset(string name, string template) { ... }
    public override string ToString() => Name;
}
```

校验口径（`TryRender` 内，按顺序短路）：

1. 渲染结果为空或纯空白 → `EmptyResult`
2. 含非法字符 → `InvalidChar`（`Path.GetInvalidFileNameChars()` ∪ `\ / : * ? " < > |` 与控制字符）
3. 结尾是 `.` 或空格 → `InvalidName`
4. 主名（去掉最后一个扩展名前）命中保留设备名 `CON/PRN/AUX/NUL/COM1-9/LPT1-9`（不区分大小写）→ `InvalidName`
5. 长度 > 200 → `TooLong`

### 3. 命名管线（唯一入口，预览=实际）

`src/MantisZip.Core/Utils/CompressPathPlanner.cs` 增加可选模板参数，**默认值保持现有行为**：

```csharp
public static string ComputeArchiveName(string sourcePath, string format, bool keepOriginalExt,
    string? template = null, int sequence = 1, DateTime? timestamp = null)
{
    if (!string.IsNullOrWhiteSpace(template)
        && FileNameTemplate.TryRender(template, new FileNameTemplateContext(sourcePath, sequence, timestamp ?? DateTime.Now),
            out var baseName, out _))
    {
        return baseName + GetFormatExtension(format);   // 模板非空 ⇒ 忽略 keepOriginalExt
    }
    // 旧逻辑原样保留（模板为空、或渲染失败时的兜底）
    ...
}

public static string ComputeOutputPath(string sourcePath, string format, bool keepOriginalExt,
    string? template = null, int sequence = 1, DateTime? timestamp = null);

public static IReadOnlyList<CompressPlanItem> PlanSeparate(
    IReadOnlyList<string> sourcePaths, string format, bool keepOriginalExt,
    string? template = null, DateTime? timestamp = null);   // sequence = index + 1
```

- **渲染失败时静默回退旧命名**：预览树不能因为用户正在打字（半截 `{na`）就崩掉或空掉；错误提示由 VM 单独显示（见 §5）。
- `Timestamp` 由调用方在**批开始时取一次** `DateTime.Now`，整批共用 → 示例行与实际输出必然一致（跨午夜也不会一半新日期一半旧日期）。

### 4. 执行侧接线

`CompressRequest`（`CompressService.cs:18`）新增：

```csharp
/// <summary>文件名模板；null/空 = 沿用原有命名。</summary>
public string? FileNameTemplate { get; init; }

/// <summary>模板时间戳（批开始时刻）；null = 执行时取一次 Now。</summary>
public DateTime? TemplateTimestamp { get; init; }
```

`CompressService`：

- `GetOutputPaths`（`:157`）：`Plan == null && Mode == Separate && 模板非空` 时先 `PlanSeparate(...)` 造命名计划再返回；其余分支不动。
- `CompressSeparateAsync`（`:230`）：在方法开头解析一次命名计划（`request.Plan?.Items ?? PlanSeparate(...)`），循环内按 `SourcePath` 查，替换现在的 `ComputeSeparateOutputPath` 调用（`:264`）。
- `ComputeSeparateOutputPath`（`:429`）保留为兼容包装，透传模板参数。

GUI 侧（B 数据集是唯一事实源，模板必须在**建 plan 时**传入）：

- `ResultPreviewService.AssembleCompressPreview(...)`（`ResultPreviewService.cs:231`）加 `string? fileNameTemplate` 与 `DateTime templateTimestamp` 参数；
  - Separate 分支 `PlanSeparate(...)`（`:323`）透传；
  - Manual/Combined 分支在 `outputPath` 为空时的兜底命名（`:270`）也用 `ComputeArchiveName(..., template, 1, timestamp)`。
- `CompressSettingsViewModel.AssembleAsync`（`:874`）把 VM 的 `FileNameTemplate` 与缓存的批时间戳传进去。
- `CompressSettings`（`Models/CompressSettings.cs:174`）新增 `string? FileNameTemplate`（默认 `null`）；`CompressSession.BuildRequest`（`:265`）映射到 `CompressRequest`。
  - 有 B 数据集时模板不参与执行（路径来自 plan），但**必须照常传**：`--compress-separate` 等无 plan 的路径靠它。

### 5. 三种输出模式的行为

| 模式 | 模板非空 | 模板为空 |
|---|---|---|
| **Manual** | 主名 = 模板渲染结果；`OutputFileName` 变成**只读**并显示渲染结果（不含扩展名，右侧 `.zip` 标签不变）；`OutputPath = 目录 + 主名 + 扩展名` | 现有行为，完全不变 |
| **Separate** | 每源一条，`{seq}` 按源列表顺序；输出目录仍是源所在目录 | 现有行为 |
| **Combined** | `{name}` = **公共父目录名**（无公共父目录时用盘符字母，与 `RefreshCombinedPath` 现有命名一致）；跨盘符已回退 Manual | 现有行为 |

> Combined 的 `{name}` 取公共父目录名而非第一个源，是为了让「模板非空」时与「模板为空」时的默认命名指向同一个东西（`CompressSettingsViewModel.cs:1794-1807`），避免同一个包里 `{name}` 突然换了含义。`--compress-quick` 不同：它压的是「一堆散路径 → 一个包」，没有公共父目录概念，所以取第一个源（现有行为）。

新增 VM 属性：

```csharp
public string FileNameTemplate { get; set; }              // 与 AppSettings.FileNameTemplateDialog 初值同步
public bool IsTemplateActive => !string.IsNullOrWhiteSpace(FileNameTemplate);
public bool IsOutputFileNameReadOnly => OutputMode != CompressOutputMode.Manual || IsTemplateActive;
```

`IsOutputFileNameReadOnly` 替换 XAML 里现有的 `IsOutputPathReadOnly` 绑定（`CompressSettingsWindow.axaml:132`），并把 `IsOutputPathReadOnly` 的语义收敛到这一个属性（避免两个只读来源）。

### 6. 校验（规则 20 的接缝）

在 `CompressSettingsViewModel.ValidateForExecution()`（`:1552`）末尾加第 ⑥ 步，**只在模板非空时执行**：

| 检查 | 错误码（本地化 key） |
|---|---|
| 语法（未知占位符 / 未闭合） | `Compress_FileNameTemplate_UnknownPlaceholder`（`{0}` = 令牌） |
| 任一源渲染为空 / 非法字符 / 保留名 / 超长 | `Compress_FileNameTemplate_InvalidResult`（`{0}` = 源名 + 原因） |
| Separate 模式多个源渲染出同一个输出路径 | `Compress_FileNameTemplate_Duplicate`（`{0}` = 重复的文件名） |

- 返回 `Rejected<CompressSettings>` → 窗口保持打开并显示原因（现有 `TryConfirmAsync` 已是这套结构，`:1510-1512`）。
- 重名比较用 `Path.GetFullPath(...)` + `OrdinalIgnoreCase`（Windows 大小写不敏感）。
- **不做自动修**（不换 `_`、不自动加序号）：模板是用户写的，静默改写会让产物与用户预期不一致。

### 7. 对话框 UI

`Dialogs/CompressSettingsWindow.axaml`：在「输出模式」（Row 0）与「输出目录」（Row 1）之间插入新分组，并把后续 4 行 `Grid.Row` 顺延 +1（1→2、2→3、3→4、4→5），`RowDefinitions` 同步加一行 `Auto`。

```
┌ 文件名模板 ──────────────────────────────────────────────┐
│ 文件名模板 [ {name}_{date}          ] [ ? ] [ 预设 ▾ ]    │
│ 示例：report_2026-11-06.zip        [保存为预设] [删除]     │
│ （点「保存为预设」时展开）名称 [            ] [确定] [取消] │
└──────────────────────────────────────────────────────────┘
```

- 输入框：`Text="{Binding FileNameTemplate}"`，`Watermark="{Binding LocalizedStrings[Compress_FileNameTemplate_Placeholder]}"`（`{name}_{date}`）。
- 帮助按钮 `?`：`HelpDialog` + `MarkdownHelpContent(LocalizationManager.T("Compress_FileNameTemplate_Markdown"))`（规则 17）。
- 预设 `ComboBox`：`ItemsSource="{Binding TemplatePresets}"`、`DisplayMemberBinding="{Binding Name}"`；选中 → 把 `Template` 填进输入框（本次会话，不写回默认值）。
- 示例行（两个 `TextBlock` 互斥 `IsVisible`，避免 VM 里造 Brush）：
  - 正常：`Foreground="{DynamicResource ThemeTextSecondaryBrush}"`，文本「示例：report_2026-11-06.zip」
  - 错误：`Foreground="{DynamicResource ThemeStatusErrorBrush}"`，文本「⚠ 未知占位符：{foo}」
  - 模板为空：显示「示例：保持现有命名（模板为空）」——明确告诉用户「不填 = 不启用」。
- 手动/合并模式下模板非空时，「输出文件名」框只读（`IsOutputFileNameReadOnly`），用户想手改就清空模板。
- 所有新控件按规则 4 显式设主题画刷；间距/高度用 `SpacingXxxThk` / `ControlHeightSm`（规则 5）；新增区域加中文注释（规则 14）。

### 8. 设置页（三档默认模板）

`Views/SettingsWindow.axaml` 压缩 → 常规，在「压缩选项」分组（`:79-91`）后新增分组「文件名模板」：

| 行 | 绑定 | 说明文案 |
|---|---|---|
| 压缩对话框 | `FileNameTemplateDialog` | 三模式共用；空 = 不启用 |
| 快速压缩（`--compress-quick`） | `FileNameTemplateQuick` | `{name}` 取第一个源 |
| 分别压缩（`--compress-separate`） | `FileNameTemplateSeparate` | 每个源一条，`{seq}` 递增 |

每行 = `TextBox`（可编辑）+ 右侧 `ComboBox`（列出预设名，选中即把模板文本填进左侧输入框，选完清空选择）。预设的**增删只在压缩对话框里做**（按用户选择），设置页只负责「填默认值」。

`SettingsWindowViewModel` 新增 6 个属性（3 个值 + 3 个标题）+ `TemplatePresetOptions`，并在 `SaveSettings`（`:1915` 一带）写回。

### 9. 持久化

`Models/AppSettings.cs`（压缩默认值区，`:18-20` 附近）：

```csharp
/// <summary>压缩对话框的默认文件名模板；空 = 不启用（沿用原命名）。</summary>
public string FileNameTemplateDialog { get; set; } = "";

/// <summary>--compress-quick 的默认文件名模板。</summary>
public string FileNameTemplateQuick { get; set; } = "";

/// <summary>--compress-separate 的默认文件名模板。</summary>
public string FileNameTemplateSeparate { get; set; } = "";

/// <summary>用户保存的文件名模板预设（上限 20 个，对齐 FilterPresets）。</summary>
public List<FileNameTemplatePreset> FileNameTemplatePresets { get; set; } = new();

public void AddFileNameTemplatePreset(FileNameTemplatePreset preset);  // 上限 20，超限返回 false
public bool RemoveFileNameTemplatePreset(string name);
```

- 对话框**不自动写回** `FileNameTemplateDialog`（沿用 `CompressSettingsViewModel.cs:192-193` 的既有约定：压缩设置不污染全局默认值）。
- 想长期保留 → 点「保存为预设」；预设增删是**显式用户动作**，允许立即 `AppSettings.Load()` → 改列表 → `Save()`。
- 三档默认值只在设置页改。
- 规则 23 的 `settingswiring` 守卫会检查新属性有运行时消费者：`FileNameTemplateDialog`（对话框初值）、`FileNameTemplateQuick`（`App.CliCompress.cs`）、`FileNameTemplateSeparate`（`RunCompressSeparate`）、`FileNameTemplatePresets`（对话框/设置页）四处都在同一提交内接上。

### 10. CLI 两个入口

- `--compress-quick`（`App.CliCompress.cs:147`）：删掉 `:164-166` 的手算，改
  ```csharp
  var archiveName = CompressPathPlanner.ComputeArchiveName(
      first, settings.DefaultFormat, keepOriginalExt: false,
      settings.FileNameTemplateQuick, sequence: 1);
  var outputPath = Path.Combine(dir, archiveName);
  ```
  保留 `dir ??= Desktop` 的兜底（`ComputeOutputPath` 会退到 `"."`，不想要这个差异）。
- `--compress-separate`（`RunCompressSeparate`，`:258`）：`CompressRequest` 增加
  ```csharp
  FileNameTemplate = settings.FileNameTemplateSeparate,
  TemplateTimestamp = DateTime.Now,
  ```
  命名由 `CompressService` 内部的 `PlanSeparate` 统一处理（§4）。

### 11. 帮助内容（规则 17）

新增 `Compress_FileNameTemplate_Markdown`（三语），内容：占位符表 + 三个例子（`{name}_{date}` / `{parent}_{name}` / `{name}_{seq:0000}`）+ 三条规则：

1. 扩展名由格式自动补，不要在模板里写 `.zip`；
2. 模板非空时「保留源文件扩展名」开关不生效，要保留原扩展名就写 `{ext}`；
3. 分别压缩时模板里没有 `{name}` / `{seq}` / `{parent}` 会让多个源重名，确认时会被拦下。

---

## 任务清单

### W1 — Core 渲染器（可独立完成，2-3h）

**T1. `FileNameTemplate` + `FileNameTemplatePreset` + 单元测试**

- 新增 `src/MantisZip.Core/Utils/FileNameTemplate.cs`、`src/MantisZip.Core/Utils/FileNameTemplatePreset.cs`
- 新增 `tests/MantisZip.Tests/FileNameTemplateTests.cs`（覆盖见「测试计划」）
- 验收：`dotnet test tests/MantisZip.Tests` 全绿（7z 相关既有失败除外）

### W2 — 命名管线（1-2h，依赖 T1）

**T2. `CompressPathPlanner` 接入模板**

- 三个方法加可选参数，默认值保持旧行为；渲染失败回退旧命名
- 新增 `tests/MantisZip.Tests/CompressPathPlannerTemplateTests.cs`
- 验收：模板为空时全部既有用例原样通过（零行为变更）

**T3. `CompressRequest` / `CompressService` / `CompressSettings` / `CompressSession` 透传**

- `CompressRequest.FileNameTemplate` + `TemplateTimestamp`
- `GetOutputPaths` / `CompressSeparateAsync` 解析命名计划
- `CompressSettings` record 加字段（**追加在末尾并带默认值**，避免破坏现有构造调用）
- `CompressSession.BuildRequest` 映射
- 验收：`-Task build -Target core` + `-Target avalonia` 通过

### W3 — 预览与对话框（3-4h，依赖 T2/T3）

**T4. `ResultPreviewService` 透传模板**

- `AssembleCompressPreview` 加参数，Separate / Manual / Combined 三处命名都走模板
- 验收：手写一个临时用例或在 T6 的 VM 测试里覆盖「预览路径 == `CompressPathPlanner` 结果」

**T5. 对话框 UI + VM**

- `CompressSettingsWindow.axaml` 新增模板分组（Row 重排 + 中文注释）
- `CompressSettingsViewModel`：`FileNameTemplate`、`IsTemplateActive`、`IsOutputFileNameReadOnly`、示例属性、预设集合、保存/删除预设、帮助按钮
- `ValidateForExecution` 第 ⑥ 步
- 验收：`-Task build -Target avalonia` + `-Task i18n` 通过

### W4 — 设置页（1-2h，依赖 T5）

**T6. 三档默认模板 + 预设填入**

- `AppSettings` 四个属性 + 两个方法
- `SettingsWindow.axaml` 新分组（三行）
- `SettingsWindowViewModel` 属性 + 保存
- 验收：设置页改完保存 → 重启后对话框/CLI 带出新值

### W5 — CLI（1h，依赖 T3）

**T7. 两个 CLI 入口**

- `HandleCompressQuick` 改用 `ComputeArchiveName`
- `RunCompressSeparate` 传 `FileNameTemplate` / `TemplateTimestamp`
- 验收：手工跑 `--compress-quick` 与 `--compress-separate`，产物名符合模板

### W6 — 本地化与文档（1-2h，可与 W3/W4 并行）

**T8. 三语 key + 帮助 + 进度文档**

- `Localization/strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json` 成对添加（UTF-8 BOM + CRLF + 2 空格）
- `Compress_FileNameTemplate_Markdown` 帮助正文
- `docs/PROGRESS.md`（里程碑，若算新功能）+ `docs/progress-avalonia-detail.md`（细节）
- 验收：`-Task i18n` 通过；`-Task check` 通过

---

## 测试计划

| 测试文件 | 覆盖点 |
|---|---|
| `tests/MantisZip.Tests/FileNameTemplateTests.cs` | 每个占位符渲染；`{seq}` / `{seq:0000}`；大小写不敏感；`{{` `}}` 转义；未知占位符；未闭合；空模板返回「未启用」；`{parent}` 盘符根为空；目录源 `{ext}` 为空；非法字符；结尾点/空格；保留设备名；超长 |
| `tests/MantisZip.Tests/CompressPathPlannerTemplateTests.cs` | 模板为空 = 旧行为逐字节一致；模板非空忽略 `keepOriginalExt`；`PlanSeparate` 的 `{seq}` 按顺序递增；渲染失败回退旧命名；`tar.gz` 双段扩展名 |
| `tests/MantisZip.Tests/CompressServiceTemplateTests.cs`（可并入上一条） | `GetOutputPaths` 在「无 Plan + 模板非空 + Separate」时产出模板名；有 Plan 时以 Plan 为准 |
| `tests/MantisZip.UI.Avalonia.Tests/CompressSettingsViewModelTemplateTests.cs` | 示例行随源/格式/模板刷新；模板非空 → `IsOutputFileNameReadOnly == true` 且 `OutputFileName` 为渲染结果；确认时四类拒绝原因各一条；重名检测（Separate 多源同名） |
| `tests/MantisZip.UI.Avalonia.Tests/AboutWindowTests.cs`（既有） | 三语 key 集自动校验 |

运行方式（本机 `dotnet test` 有 testhost 问题，见 `docs/MAINTENANCE.md`）：

```powershell
pwsh -File scripts\verify-repo.ps1 -Task test -Target tests
pwsh -File scripts\verify-repo.ps1 -Task test -Target ui-tests
```

---

## 验收标准

- [ ] 模板为空时，所有入口的行为与现在**完全一致**（既有测试零改动通过）
- [ ] `{name}_{date}` 在分别压缩 3 个文件时产出 `a_2026-11-06.zip` / `b_...` / `c_...`
- [ ] 对话框里模板非空 → 手动模式的「输出文件名」变只读，且预览树里的包名与之一致
- [ ] 确认时输入 `{foo}` → 窗口不关，提示「未知占位符：{foo}」
- [ ] 确认时输入 `{name}` 且 Separate 多源 → 窗口不关，提示重名
- [ ] 设置页三档默认值分别对三个入口生效
- [ ] 预设：保存 → 出现在下拉里 → 重启后仍在 → 可删除；上限 20 时有提示
- [ ] 示例行在切换源/格式/模板时同步刷新
- [ ] `pwsh -File scripts\verify-repo.ps1 -Task check` / `-Task i18n` / `-Task guards` 全绿

---

## 风险与取舍

| 风险 | 处置 |
|---|---|
| 模板与全局「保留扩展名」语义重叠 | 模板非空即接管，帮助文档写清；不在设置页置灰（少一处联动逻辑） |
| Combined 的 `{name}` 是公共父目录名，与 quick 的「第一个源」不一致 | 各自贴合现有默认命名；帮助文档分模式说明。**这是本计划里唯一没有单独提问就定下的点**，实施前可再确认 |
| 预览期间模板语法错误 | 渲染失败静默回退旧命名，错误只在示例行与确认时提示；预览树不空 |
| `DateTime.Now` 跨午夜 | 批开始时取一次，整批共用 |
| 预设写入 `AppSettings` 与「压缩设置不污染全局默认值」的既有约定 | 预设是显式动作（用户点「保存为预设」），不写回任何默认值字段 |

---

## 影响文件清单

| 文件 | 改动 |
|---|---|
| `src/MantisZip.Core/Utils/FileNameTemplate.cs` | 新增 |
| `src/MantisZip.Core/Utils/FileNameTemplatePreset.cs` | 新增 |
| `src/MantisZip.Core/Utils/CompressPathPlanner.cs` | 三个方法加可选模板参数 |
| `src/MantisZip.Core/Services/CompressService.cs` | `CompressRequest` 两字段 + 命名计划解析 |
| `src/MantisZip.UI.Avalonia/Models/CompressSettings.cs` | record 加 `FileNameTemplate` |
| `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` | 三档默认值 + 预设列表 + 增删方法 |
| `src/MantisZip.UI.Avalonia/Services/CompressSession.cs` | `BuildRequest` 映射 |
| `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs` | `AssembleCompressPreview` 加参数并透传 |
| `src/MantisZip.UI.Avalonia/ViewModels/CompressSettingsViewModel.cs` | 模板属性 / 示例 / 预设 / 校验 / 只读接管 |
| `src/MantisZip.UI.Avalonia/Dialogs/CompressSettingsWindow.axaml(.cs)` | 新分组 + Row 重排 + 帮助按钮 |
| `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs` | 三档默认值 + 预设填入 |
| `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml` | 新分组（三行） |
| `src/MantisZip.UI.Avalonia/App.CliCompress.cs` | quick 命名 + separate 传参 |
| `src/MantisZip.UI.Avalonia/Localization/strings.{zh-CN,en,zh-TW}.json` | 新增 key（成对） |
| `tests/MantisZip.Tests/FileNameTemplateTests.cs` | 新增 |
| `tests/MantisZip.Tests/CompressPathPlannerTemplateTests.cs` | 新增 |
| `tests/MantisZip.UI.Avalonia.Tests/CompressSettingsViewModelTemplateTests.cs` | 新增 |
| `docs/PROGRESS.md` / `docs/progress-avalonia-detail.md` | 进度记录 |

---

## 附录：新增本地化 key 清单（三语成对）

对话框：`Compress_FileNameTemplate`、`_Placeholder`、`_Example`、`_ExampleNone`、`_ExampleError`、`_Help`、`_Preset`、`_SavePreset`、`_DeletePreset`、`_PresetName`、`_PresetNameEmpty`、`_PresetLimit`、`_PresetExists`、`_DeleteConfirm`、`_UnknownPlaceholder`、`_InvalidResult`、`_Duplicate`、`_Markdown`

设置页：`Settings_Compress_FileNameTemplate_Group`、`_Dialog`、`_Quick`、`_Separate`、`_Hint`

> 以上均以 `Compress_FileNameTemplate` / `Settings_Compress_FileNameTemplate` 为前缀，实施时按实际拼接完整 key。
