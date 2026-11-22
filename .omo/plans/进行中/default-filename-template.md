# 出厂默认命名模板 + 删除「保留源扩展名」开关

> **状态**：实施中（2026-11-22，`beta` 分支）
> **关联代码**：`Models/AppSettings`、`Core/Utils/CompressPathPlanner`、`Core/Services/CompressService`、`UI.Avalonia/Services/ResultPreviewService`、`UI.Avalonia/ViewModels/{CompressSettingsViewModel,SettingsWindowViewModel}`、`UI.Avalonia/Views/SettingsWindow.axaml`
> **前置**：`filename-template`（v0.5.7 已归档）

---

## TL;DR

两件事一起做，因为它们互相耦合：

1. **新用户不再看到空模板**：`AppSettings.CreateWithDefaults()` 里预置压缩对话框默认模板 `{name:auto}` 与两条内置预设，新装即带上；已有 `settings.json` 的用户完全不受影响。
2. **删掉「保留源扩展名」开关**：它名义是全局选项，实际只影响分别压缩；语义已被 `{ext}` 覆盖，默认值与行业惯例相反。删掉后不再有「出厂即死开关」的问题。

---

## Context

### 起因（2026-11-22）

> 「你看看我刚刚保存了两个预设和一个默认值，我希望新用户能默认就加载我刚刚修改的状态而不是什么都是空的状态。」

用户当前 `settings.json` 里是：`FileNameTemplateDialog = "{name:auto}"`、两条预设（`Auto Name` / `Name & Date`）、Quick 与 Separate 仍为空。

### 为什么顺手删开关

模板非空时 `keepOriginalExt` 被完全忽略（`CompressPathPlanner.cs:20-21`、`CompressService.cs:55` 均有注释）。一旦 `{name:auto}` 成为出厂默认，新用户勾上那个开关将毫无效果 —— 出厂即死开关，违反规则 23。

而它的真实作用面比标签暗示的窄得多：

| 入口 | 是否受 `KeepOriginalExtension` 影响 |
|---|---|
| 分别压缩（`CompressOutputMode.Separate`） | ✅ 唯一生效场景 |
| 单包模式（Manual） | ❌ 名字来自 `OutputFileName` 或模板 |
| 合并预览 | ❌ `ResultPreviewService.cs:271` 硬编码 `keepOriginalExt: false` |
| `--compress-quick` | ❌ `App.CliCompress.cs:280` 硬编码 `false` |

WinRAR / 7-Zip 的「每个文件单独归档」默认也是去扩展名（`hello.txt` → `hello.rar` / `hello.zip`），要保留写 `{ext}` 更明确。

### 已定决策（2026-11-22，用户「都按你的想法来吧」）

| # | 问题 | 决定 |
|---|---|---|
| 1 | 出厂默认范围 | **只放 Dialog 模板 `{name:auto}` + 两条内置预设**；Quick / Separate 保持空 |
| 2 | 预设名语言 | **固定字符串，不走 i18n**：预设名是用户可编辑的数据，不是界面文案（规则 13 豁免：内容数据）。内置两条用 `Auto Name` / `Name & Date`，与用户已存的一致 |
| 3 | 删了能否恢复 | **不提供恢复入口**：删了就不再回来，符合「出厂默认」直觉 |
| 4 | 老用户迁移 | **不迁移**。写进模板会把行为扩散到单包模式（模板会接管命名、把「输出文件名」变只读），比「行为不变」更糟。分别压缩保留扩展名的诉求改在发行说明里指向 `{ext}` |
| 5 | `{name:auto}` 是否加 `{ext}` | **不加**。保持用户存下的原样；`{ext}` 对目录源为空，写进去会让目录命名出现多余分隔符的风险，且「默认去扩展名」与行业惯例一致 |
| 6 | 删除开关的连带处理 | 三语 i18n key `Settings_Compress_KeepExt` 一并删除；`Compress_FileNameTemplate_Markdown` 里「模板非空时保留源文件扩展名失效」一句改写为「需要保留请写 `{ext}`」 |

---

## 实施

### 1. Core：去掉 `keepOriginalExt` 参数

`src/MantisZip.Core/Utils/CompressPathPlanner.cs`：

- `ComputeArchiveName(string sourcePath, string format, bool keepOriginalExt, ...)` → 删掉 `keepOriginalExt` 形参，旧命名分支一律 `Path.GetFileNameWithoutExtension`（目录源本来就忽略它）。
- `ComputeOutputPath(...)` 同步删形参并透传。
- `PlanSeparate(...)` 同步删形参。
- 类头注释第 16 行「文件源：keepOriginalExt 为 true 保留扩展名」与第 20 行「忽略 keepOriginalExt」两句改写：明确「主名恒去扩展名，保留扩展名用 `{ext}`」。

`src/MantisZip.Core/Services/CompressService.cs`：

- `CompressRequest.KeepOriginalExtension`（:51）删除。
- `ComputeSeparateOutputPath`（:443）去掉 `request.KeepOriginalExtension` 实参。
- `FileNameTemplate` 的 XML 注释里「忽略 `KeepOriginalExtension`」一句改写。

### 2. UI 模型层

`Models/AppSettings.cs`：

- 删 `public bool KeepOriginalExtension { get; set; } = false;`（:20）。
- `CreateWithDefaults()`（:799）追加出厂默认：

```csharp
private static AppSettings CreateWithDefaults()
{
    var settings = new AppSettings();
    settings.AdaptiveOverrides = GetDefaultAdaptiveOverrides();

    // 出厂默认命名（2026-11-22）：新装即带上「按源名命名」与两条常用预设，
    // 避免用户第一次打开压缩对话框时模板、预设全空。只在设置文件**不存在**时生效，
    // 已有 settings.json 的用户不受影响（也不会覆盖用户主动清空的模板）。
    settings.FileNameTemplateDialog = "{name:auto}";
    settings.FileNameTemplatePresets.Add(new FileNameTemplatePreset("Auto Name", "{name:auto}"));
    settings.FileNameTemplatePresets.Add(new FileNameTemplatePreset("Name & Date", "{name:auto}-{date}"));
    return settings;
}
```

`Models/CompressSettings.cs`：

- `CompressEngineOptions`（:72，位置参数 record）删 `bool KeepOriginalExtension` 一项 + 对应 `<param>` 注释。构造点只有 `CompressSettingsViewModel.cs:1903` 与 `tests/MantisZip.UI.Avalonia.Tests/CompressOpenFolderAfterTests.cs:193` 两处。

### 3. UI 消费方

| 文件 | 改动 |
|---|---|
| `ViewModels/CompressSettingsViewModel.cs` | 删 `_keepOriginalExtension` 字段、`KeepOriginalExtension` 属性、`LoadFromSettings` 里 :751 的赋值、`BuildCompressSettings` 里 :1915 的实参；`ValidateTemplateNow` 里 `PlanSeparate(...)` 实参少一个；`AssembleAsync` / `BuildCompressPreview` 链路去掉 `keepOriginalExtension` 局部变量与实参 |
| `ViewModels/SettingsWindowViewModel.cs` | 删 `_keepOriginalExtension`（:107）、`KeepOriginalExtensionText`（:957）、读（:1443）、文化变更通知（:1907）、写回（:2106） |
| `Views/SettingsWindow.axaml` | 删 :83-84 的 CheckBox 及其上方注释（若有） |
| `Services/CompressSession.cs` | `BuildRequest` 里 :315 删 `KeepOriginalExtension = ...` |
| `Services/ResultPreviewService.cs` | `AssembleCompressPreview` / `BuildCompressPreview` / 私有 `ComputeArchiveName` / `BuildSeparateArchivesPreview` 四处删形参并透传 |
| `App.CliCompress.cs` | `RunCompressSeparate` 里 :280 删 `KeepOriginalExtension = false` |

### 4. i18n（三语同步，规则 13）

- **删** `Settings_Compress_KeepExt`（zh-CN / en / zh-TW 三份都在 `:962`）。
- **改** `Compress_FileNameTemplate_Markdown` 里那条规则：

| 语言 | 原文片段 | 改为 |
|---|---|---|
| zh-CN | `模板非空时「保留源文件扩展名」失效，需要就写 {ext}；` | `需要保留源的原始扩展名，请在模板里写 {ext}；` |
| en | `when the template is non-empty the "keep original extension" option has no effect — write {ext} if you need it;` | `to keep the source extension, write {ext} in the template;` |
| zh-TW | 对应句 | `需要保留來源的原始副檔名，請在範本裡寫 {ext}；` |

### 5. 测试

| 文件 | 改动 |
|---|---|
| `tests/MantisZip.Tests/Utils/CompressPathPlannerTemplateTests.cs` | `ComputeArchiveName_NoTemplate_MatchesLegacyBehavior` 的 `[InlineData]` 去掉 keepOriginalExt 维与对应的 `report.txt.zip` 行；`ComputeArchiveName_Template_IgnoresKeepOriginalExt` 改名/改断言；`PlanSeparate` 相关调用去掉实参 |
| `tests/MantisZip.Tests/Services/CompressServiceTests.cs` | `KeepOriginalExtension_True`（:302）删除；`KeepOriginalExtension_False`（:327）改名为 `Separate_SingleFile_DropsSourceExtension`，只断言 `hello.zip` |
| `tests/MantisZip.UI.Avalonia.Tests/CompressOpenFolderAfterTests.cs` | `CompressEngineOptions` 构造删一个实参 |
| `tests/MantisZip.UI.Avalonia.Tests/CompressConflictDefaultActionTests.cs` / `CompressPreviewProgressiveTests.cs` | 调 `AssembleCompressPreview` / 预览入口时删 `keepOriginalExtension` 实参 |
| **新增** `tests/MantisZip.Tests/Services/AppSettingsDefaultTemplateTests.cs`（放 UI 测试项目，因 `AppSettings` 在 UI 层） | 见下 |

新增用例（`tests/MantisZip.UI.Avalonia.Tests/AppSettingsDefaultTemplateTests.cs`）：

1. `CreateWithDefaults_SeedsDialogTemplateAndTwoPresets` —— `AppSettings.LoadCore(不存在路径)` 返回的实例：`FileNameTemplateDialog == "{name:auto}"`、`FileNameTemplatePresets` 两条且名字/模板正确、`FileNameTemplateQuick` 与 `FileNameTemplateSeparate` 仍为空。
2. `CreateWithDefaults_DoesNotOverrideExistingFile` —— 写一个 `FileNameTemplateDialog = ""` 的 settings.json 到沙箱（`AppSettingsTestSandbox`，规则 24），`LoadCore` 后仍是空，证明只对「文件不存在」注入。
3. `KeepOriginalExtension_SettingIsGone` —— 反射断言 `typeof(AppSettings).GetProperty("KeepOriginalExtension") == null`，防止有人「顺手加回来」。

### 6. 文档

- `docs/ARCHITECTURE.md:692` 的设置项清单里删 `KeepOriginalExtension`。
- `docs/PROGRESS.md` + `docs/progress-avalonia-detail.md` 按规则 3 记录。
- `RELEASE_NOTES.md`：**先向用户确认范围**（规则 18）。倾向写两条：① 新装自带默认命名模板与两条预设；② 「保留源扩展名」开关移除，分别压缩保留扩展名请用 `{ext}`。后者是上一版本已发布、用户可能依赖的能力，属「修复/变更」而非「本版本内新引入又修掉」。

---

## 风险

| 风险 | 处置 |
|---|---|
| 老用户勾过「保留源扩展名」，升级后分别压缩变成 `hello.zip` | 可见行为变化，写进 RELEASE_NOTES，并指向 `{ext}`；不做静默迁移（决策 4） |
| 位置参数 record 少一项，漏改某个构造点 | 编译器直接报错（C# 位置参数 record 不支持跳过），`-Task build -Target solution` 必查 |
| `AppSettings` 里的预设名硬编码英文 | 属「内容数据」豁免；中文用户可自行改名/删除 |
| 出厂默认 `{name:auto}` 让单包模式的「输出文件名」框变只读 | 这是模板的既有设计（v0.5.7 已确认），不是本次引入；首次打开对话框即看到渲染结果，反而更有引导性 |

---

## 验收

- `pwsh -File scripts/verify-repo.ps1 -Task build -Target solution` 0 警告
- `-Task test -Target tests`、`-Task test -Target ui-tests` 全绿
- `-Task check` 全绿（三语 key 集一致 —— 删了 `Settings_Compress_KeepExt` 三份都要删）
- 手工：把 `%LOCALAPPDATA%\MantisZip\settings.json` 改名后启动，压缩对话框模板框应为 `{name:auto}`、预设下拉两条；设置页「压缩选项」卡片里不再有「保留原文件扩展名」
