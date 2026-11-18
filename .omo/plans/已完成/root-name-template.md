# 根目录名模板（「包内根目录」加根名支持占位符）

**状态**: ✅ 已完成（2026-11-18 归档，随 v0.5.8）
> **定位**: 让「包内根目录」四态里的**根目录名**也支持占位符渲染，语义与文件名模板一致，但只暴露对目录名有意义的 5 个占位符
> **定位**: 让「包内根目录」四态里的**根目录名**也支持占位符渲染，语义与文件名模板一致，但只暴露对目录名有意义的 5 个占位符

---

## 一、背景

v0.5.8 的「包内根目录」四态（原样 / 智能去根 / 智能加根 / 始终加根）里，③④ 两档需要一个根目录名。现在的行为是：

- 留空 → 回落到「压缩包文件名去扩展名」（`ArchiveRootPolicy.GetWrapName` 的 `fallbackArchivePath`）
- 填了就当**字面量**用（只做非法字符净化）

用户提出：这个输入框能不能像「文件名模板」那样用占位符？典型诉求是「每次加根都用当天日期」「保留源目录名而不是压缩包名」。

**能，而且结构上是现成的**：文件名模板（`src/MantisZip.Core/Utils/FileNameTemplate.cs`）已经把词法（`{{`/`}}` 转义、未闭合判定、大小写不敏感）、结果校验（非法字符 / 尾点空格 / 保留设备名 / 长度）做成了可复用的形态。根目录名只差三件事：占位符集合不同、{name} 的取值规则不同、渲染结果必须是**单段目录名**。

## 二、设计决策（用户 2026-11-17 拍板）

| # | 决策 | 取值 | 理由 |
|---|---|---|---|
| 1 | 占位符范围 | **精简专用 5 个**：`{name}` `{parent}` `{date}` `{datetime}` `{archive}` | 去掉对目录无意义的 `{ext}`（目录源恒为空）与 `{seq}`（加根只加一层）；补上现在只能靠「留空」实现的 `{archive}`（压缩包主名） |
| 2 | 多源时 `{name}` 取谁 | 单源 = 源主名；**多源 = 公共父目录名**（取不到才回落首源主名） | 与文件名模板 `{name:auto}` 现行为一致，名字不随「先选谁」变化 |
| 3 | 渲染失败处理 | 对话框**报错并在「开始压缩」前拦下**（`ValidateForExecution` 新增一步） | 与文件名模板 ⑥ 一致；静默回落会让用户以为模板生效了 |
| 4 | 引擎侧失败 | 静默回落「压缩包文件名」并写 `CoreLog` | 执行期不能因为一个占位符让整批压缩失败；CLI 无预览阶段，必须能跑 |

### 语义细节

- 模板为空/纯空白 = **未启用**，行为与现在逐字节一致（回落压缩包名）。
- `{archive}` = 压缩包主名（`Path.GetFileNameWithoutExtension(outputPath)`）。预览与执行各自持有自己的 outputPath，天然同源。
- 时间戳 `{date}` / `{datetime}` 与文件名模板共用同一个 `CompressSettings.TemplateTimestamp`（对话框打开时冻结），避免「预览 23:59、执行 00:01」这种跨午夜不一致。
- 渲染结果是**单段目录名**：含 `/` `\` 一律 InvalidChar（不猜测用户想建多级目录），与文件名模板「模板渲染的是主名不是路径」同一口径。
- 字面量花括号：`{{` `}}` 仍是转义。只想写字面 `{2026}` 请写 `{{2026}}`。

## 三、实现草案

### 3.1 Core

| 文件 | 改动 |
|---|---|
| `src/MantisZip.Core/Utils/FileNameTemplate.cs` | 把词法与校验拆出可复用入口：`internal static bool TryTokenizeTemplate(string, Func<string,bool> isKnown, out List<Segment>, out error)`、`internal static bool HasInvalidChar(string)`、`internal static bool IsReservedDeviceName(string)`、`internal static string GetPrimaryName(string)`（已是 internal） |
| `src/MantisZip.Core/Utils/RootNameTemplate.cs`（新建） | `RootNameTemplateContext(IReadOnlyList<string> SourcePaths, DateTime Timestamp, string? ArchiveName)`；`Placeholders`（5 项，唯一事实源）；`TryValidateSyntax`；`TryRender(string?, ctx, out string? rootName, out FileNameTemplateError?)`；`MaxNameLength = 200`；`DefaultExample = "{name}_{date}"` |
| `src/MantisZip.Core/Utils/ArchiveRootPolicy.cs` | 新增 `ResolveWrapName(string? templateOrName, IReadOnlyList<string> sourcePaths, string? archivePath, DateTime? timestamp = null)`：空白 → 旧回落；否则 `RootNameTemplate.TryRender`，成功用渲染值（过一遍 `SanitizeDirectorySegment` 兜底），失败回落压缩包名 + 无返回值标记 |
| `src/MantisZip.Core/Utils/FileScanner.cs` | `CollectFiles` 里 `GetWrapName(wrapRoot, archivePath)` → `ResolveWrapName(wrapRoot, sourcePaths, archivePath, timestamp)`；新增 `DateTime? wrapTimestamp = null` 尾参 |
| `src/MantisZip.Core/Engines/ZipEngine.Compress.cs` / `TarGzEngine.cs` | 传 `options.CompressWrapRootTimestamp` |
| `src/MantisZip.Core/Engines/SevenZipEngine.cs` | `GetWrapName` → `ResolveWrapName(..., sourcePaths, outputPath, options.CompressWrapRootTimestamp)` |
| `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` | `ArchiveOptions` 加 `DateTime? CompressWrapRootTimestamp { get; set; }` |
| `src/MantisZip.Core/Services/CompressService.cs` | `BuildOptions` 把 `request.TemplateTimestamp` 写进 `CompressWrapRootTimestamp` |

### 3.2 UI

| 文件 | 改动 |
|---|---|
| `ViewModels/CompressSettingsViewModel.cs` | `RootNameError` / `IsRootNameErrorVisible`；`ValidateRootNameNow()`（渲染试跑）；`ValidateForExecution` 新增 ⑦；`RootNameExampleText`（预览本批会用的根名）；`AssembleAsync` 快照里把 timestamp 传给根名解析 |
| `Controls/PlaceholderInsertButton.cs` | 支持按目标输入框切换占位符集合（新增 `Placeholders` 属性，默认取 `FileNameTemplate.Placeholders`） |
| `Dialogs/CompressSettingsWindow.axaml` | 根名行右侧加「插入占位符」按钮 + 下方示例/错误行；`x:Name="WrapRootBox"` |
| `ViewModels/SettingsWindowViewModel.cs` + `Views/SettingsWindow.axaml` | 设置页「默认根目录名」的说明补一句「支持占位符」，不做实时校验（那里没有本批源上下文） |

### 3.3 i18n（三语）

新增 key：`Compress_RootMode_WrapName_Placeholder`（示例 watermark）、`Compress_RootMode_WrapName_Insert`、`Compress_RootMode_WrapName_InsertTip`、`Compress_RootMode_WrapName_Example`、`Compress_RootName_Token_{Name,Parent,Date,DateTime,Archive}`、`Compress_RootName_Help_{Title,Tip,Markdown}`、`Settings_Compress_RootName_Help`。

### 3.4 测试

- `tests/MantisZip.Tests/Utils/RootNameTemplateTests.cs`：5 个占位符、大小写、转义、未闭合、未知占位符、多源 `{name}` 走公共父目录、`{archive}` 空值、结果校验四类、长度边界。
- `tests/MantisZip.Tests/Utils/ArchiveRootPolicyWrapTemplateTests.cs`：空白回落、模板成功、模板失败回落、非法字符校验而非净化。
- `tests/MantisZip.UI.Avalonia.Tests/CompressSettingsViewModelRootNameTests.cs`：合法模板无错、未知占位符在 `ValidateForExecution` 被拦、留空不报错。

## 四、验收口径
1. ✅ 根名留空 → 产出与 v0.5.8 完全一致（回落压缩包名）。
2. ✅ 根名填 `{date}` → 包内根目录是当天日期；填 `{name}` → 单源是源目录名、多源是公共父目录名。
3. ✅ 填 `{foo}` → 点「开始压缩」时被拦下并给出「未知占位符 {foo}」；填 `a/b` → 被拦下并说明不能含路径分隔符。
4. ✅ 预览树里显示的根名 == 实际包内那层目录名。
5. ✅ `-Task check`（三语 key / 守卫）与两套测试全绿。

> 全部达成（2026-11-18 真机验证 + 测试）。
## 五、范围外

- 解压端的根名（只来自压缩包名，无输入框）不接模板。
- 「智能解压」两档不接模板（同上）。
- 设置页默认根名不做实时校验（无本批上下文，只在对话框里校验）。
- 文件名模板的 8 个占位符不跟着改（`{ext}`/`{seq}` 在根名上下文无意义）。

## 六、实施记录（2026-11-18 落地，随 v0.5.8）

### 6.1 任务完成情况

| 任务 | 结果 |
|---|---|
| Core 词法抽取 | `Utils/TemplateLexing.cs`（`internal`）：`Segment` / `TryTokenize(string, Func<string,bool>, out ...)` / `TryValidateResult(rendered, maxLength, out ...)` / `HasInvalidChar` / `IsReservedDeviceName`；`FileNameTemplate` 删掉自己那 5 份副本改为委托，**零行为变更** |
| Core 根名模板 | `Utils/RootNameTemplate.cs`：`RootNameTemplateContext(IReadOnlyList<string> SourcePaths, DateTime Timestamp, string? ArchiveName)`、5 个占位符、`TryValidateSyntax` / `TryRender`、`MaxNameLength = 200` |
| Core 策略接入 | `ArchiveRootPolicy.ResolveWrapName(templateOrName, sourcePaths, fallbackArchivePath, timestamp)`：空白 → `GetWrapName(null, …)`；**不含 `{` 的字面名走旧路径**（净化不报错）；含 `{` 才渲染，失败回落压缩包名 + `CoreLog.Info` |
| Core 调用点 | `FileScanner.CollectFiles` 新增 `DateTime? wrapTimestamp` 尾参并用 `ResolveWrapName`；`ZipEngine.Compress.cs` / `TarGzEngine.cs` / `SevenZipEngine.cs` 传 `options.CompressWrapRootTimestamp`；`SourceEntryEnumerator.Enumerate` 同步透传；`ArchiveOptions` 新增 `CompressWrapRootTimestamp`；`CompressService.BuildOptions` 取 `request.TemplateTimestamp` |
| UI 预览 | `ResultPreviewService.AssembleCompressPreview` 的 `wrapName` 改走 `ResolveWrapName(wrapRoot, sourcePaths, effectiveOutputPath, templateTimestamp)` —— 预览与实际同源 |
| UI 对话框 | `CompressSettingsViewModel`：`RootNameError` / `IsRootNameTemplateActive` / `IsRootNameErrorVisible` / `RootNameExampleText` / `IsRootNameExampleError` / `ValidateRootNameNow()` / `DescribeRootNameError()` / `ShowRootNameHelpCommand`；`ValidateForExecution` 新增第 ⑦ 步；`OnRootModeChanged` / `OnWrapRootChanged` / `NotifyRootModeAvailability` 全部触发重算（切走加根要清掉无主红字）|
| UI 控件 | `Controls/PlaceholderSet.cs`（`FileName` / `RootName`）+ `PlaceholderInsertButton` 新增 `Placeholders` 属性，按集合取 token；`"{archive}"` 映射到 `Compress_RootName_Token_Archive` |
| UI XAML | `Dialogs/CompressSettingsWindow.axaml` 根名行 `Auto,*,Auto` + `PlaceholderInsertButton`；新增示例/错误同格互斥行 + 帮助按钮（`Classes="ToolbarIcon"`，规则 4 守卫生效）；`Views/SettingsWindow.axaml` 默认根名下方加说明行 |
| i18n | 13 个 `Compress_RootName_*` key + 1 个 `Settings_Compress_RootName_Hint`，三语 → 1529 key |
| 测试 | `RootNameTemplateTests`（58 例）、`ArchiveRootPolicyWrapNameTests`（19 例）、`CompressSettingsViewModelRootNameTests`（13 例）|

### 6.2 偏差（相对 §三 草案）

1. **占位符 key 名简化**：草案的 `Compress_RootMode_WrapName_Insert` 等改成了 `Compress_RootName_Insert`（与 `Compress_FileNameTemplate_Insert` 对称），避免同一个功能区两套前缀。
2. **示例行与错误行同格互斥**而非两行：`IsVisible` 反相绑定，出错时只换字色不额外长高，对话框在 900px 高时不会被挤。
3. **`DescribeRootNameError` 复用文件名模板的兜底 key**（`Compress_FileNameTemplate_InvalidResult`）：根名的 6 类错误各有专属 key，只有未知类别才回落到共用的那条。
4. **设置页不做实时校验但加了说明行**：那里的 `CompressRootName` 没有本批源上下文，渲染不了 `{name}`；说明行只列出可用 token 并注明非法模板会在对话框拦下。
5. **`ResolveWrapName` 而非改造 `GetWrapName`**：`GetWrapName` 保留原样（含解压端调用点 `ExtractRootPlan.cs:52`），新的模板入口单开一层，老语义逐字节不变。

### 6.3 真机验证（2026-11-18）

| 场景 | 结果 |
|---|---|
| 设置页 `CompressRootMode=AlwaysWrap` + `CompressRootName={name}_{date}` | 对话框打开即显示「始终加根 / `{name}_{date}` / 示例行 `7zip_2026-11-18`」|
| 点「开始压缩」 | 产出 `7zip.zip`，包内条目 = `7zip_2026-11-18/7zip/a.txt` + `7zip_2026-11-18/7zip/inner/b.txt` —— **预览树显示的根名与实际一致** |
| 根名改 `{foo}` | 示例行转红「未知占位符 foo，可用：{name} {parent} {date} {datetime} {archive}」；点「开始压缩」弹出同样文案并**不产出包**（`开始压缩` 同时置灰）|
| 根名 `{foo}` 时点「插入占位符」 | 菜单只列 5 项（`{name}` `{parent}` `{date}` `{datetime}` `{archive}`），**没有 `{ext}`/`{seq}`** |

### 6.4 验证数据

- `-Task build -Target core -c Release` 多次 0 警告；`-Target avalonia --no-incremental` 46.0s / 0 警告。
- `-Task test -Target tests -c Release` ✅ 17.7s（2 条既有警告）；`-Target ui-tests -c Release` ✅ 全绿（32 条既有警告）。
- `-Task i18n` ✅ 1529 key 三语一致、`issueCount: 0`；`-Task check` ✅ version 六处 0.5.8 / 10 条守卫 `failedCount: 0`。

### 6.5 留白

- CLI（`--compress` 系列）仍无 RootMode/WrapRoot 参数 ⇒ 根名模板在 CLI 侧不可达；要接得先补 CLI 参数（见 `docs/KNOWN_ISSUES.md`）。
- 解压端的根名（`ExtractRootPlan`）只从压缩包名推导，不接模板（§五范围外）。
- 设置页的默认根名不做实时校验，粘贴非法模板要到对话框才报。
