# 智能解压两档（加根 / 去根）与解压落点统一

**状态**: ✅ 已完成（2026-11-13 归档，随 v0.5.8）
> **定位**: 设置里给「智能解压」加一个两档选项（智能加根 / 智能去根），并把解压落点统一成「父目录 + 根策略」
> **阶段归属**: 并入 **v0.5.8**（第二项）。用户 2026-11-13 拍板：「直接加到现在 v0.5.8 里面，要不然现在的 v0.5.8 里面是个半成品的计划，用户体验反而下降」
> **上一期**: [compress-extract-root-mode.md](../已完成/compress-extract-root-mode.md)（四态语义与 `ArchiveRootPolicy` / `ExtractRootPlan` 决策层沿用，不重述）

---

## 一、背景

v0.5.8 第一期把「包内根目录」四态做进了两个设置窗，但**右键 / 菜单的「智能解压」仍是 v0.5.8 之前的老逻辑**：它自己算 dest（单根 → 父目录、散装 → `父目录\包名`），不读任何根策略。结果同一个包有三种落点语义并存：

| 入口 | dest | 根策略 |
|---|---|---|
| 智能解压（旧） | 单根 → `父目录`；散装 → `父目录\包名` | 无（不剥不加） |
| 解压设置对话框 | `父目录\包名` | 读 `ExtractRootMode`，默认「原样」 |
| 右键「解压选中项到…」/ 拖拽兜底 | `父目录\包名` | 同上（见 [KI-018](../../docs/KNOWN_ISSUES.md)） |

用户判断这属于「半成品」：四态做出来了，但最常用的无窗入口没接上，且「散装包建包名目录」这个动作是靠 **dest 里塞包名** 凑出来的，与四态里的「智能添加唯一根」重复实现。结论——把 dest 统一成父目录，让根策略负责那一层。

## 二、本计划推翻上一期的两点

| 上一期（`compress-extract-root-mode.md`）写的 | 本计划改成 | 依据 |
|---|---|---|
| 「两侧选项的**默认值都是「原样」** ⇒ 不选它时结果与今天逐字节一致」 | 解压端 `ExtractRootMode` 默认值改为 **智能加根** | 用户 m18594「接受」；这是本功能的主要目的 |
| 「**右键「智能解压」行为保持不变**，不读这两个选项」 | 智能解压改为**读新设置**，分加根 / 去根两档 | 用户 m18395 / m18442 |

> 上一期文件**保持归档不改正文**（它记录的是 v0.5.8 第一期当日的事实），只在 §7.6 留白后追加三行指向本计划。

## 三、设计决策（用户逐条确认）

| # | 问题 | 定案 |
|---|------|------|
| 1 | 档位数量 | **两档**：`智能加根`（默认） / `智能去根`。不是三档（无「智能自动」），不复用四态下拉的全部四项 |
| 2 | 两档的 dest | **都是父目录**（`Path.GetDirectoryName(archivePath)`），只换根策略 |
| 3 | 根策略 | 加根 → `RootDirectoryMode.SmartWrap`；去根 → `RootDirectoryMode.SmartStrip` |
| 4 | 「加根」语义 | **= 今天的行为**，逐型等价（见下表）。单根包复用包自带的根、散装/多根包补一层包名 |
| 5 | 「去根」语义 | = 上一期「智能去除唯一根」的原义：**包没有唯一根时什么都不做**（= 原地平铺到父目录），不是「仍建包名子目录」 |
| 6 | 解压设置对话框默认 dest | 也改成父目录（`ExtractSettingsViewModel` 构造器），与智能解压一致 |
| 7 | 已知的两处可见偏差 | ①单根包默认落点少一层（`父目录\photos\…` 而不是 `父目录\包名\photos\…`）②自定义目标目录下散装包多一层（`C:\out\包名\…`）。用户明确认可（m18567：「这两处偏差是对的，这个改变是开发这个功能的原因之一」） |
| 8 | 另两个入口 | **本计划不改**（右键「解压选中项到…」`MainWindowViewModel.Extract.cs:165`、拖拽兜底 `DragDropService.cs:84`），已登记 **KI-018** 待用户另有计划 |
| 9 | 状态栏文案 | 按**实际发生的动作**三条：加根 / 去根 / 未动。现有 `Status_SmartExtractSingleRoot`「已剥离根文件夹」是错的（代码从没剥过），一并纠正 |
| 10 | 解压后打开目录 | 智能解压用「已知计划」重载直接算出要打开的目录，不再二次列包 |

### 落点真值表（`backup.zip` 在 `D:\dl\`）

| 包型 | 今天 | 改后·智能加根（默认） | 改后·智能去根 |
|---|---|---|---|
| 单根 `photos\a.jpg` | `D:\dl\photos\a.jpg` | `D:\dl\photos\a.jpg` ✅ 等价 | `D:\dl\a.jpg` |
| 散装 `x.txt` | `D:\dl\backup\x.txt` | `D:\dl\backup\x.txt` ✅ 等价 | `D:\dl\x.txt` |
| 多根 `a\x`+`b\y` | `D:\dl\backup\a\x` | `D:\dl\backup\a\x` ✅ 等价 | `D:\dl\a\x` + `D:\dl\b\y` |

**不变式**（向用户解释、已认可）：每个包解压后必然落在一个顶层文件夹里，那个文件夹要么是包自带的根名，要么是包名。加根档恒满足；去根档是该不变式的反面选择（用户主动要平铺）。

## 四、实现草案

### 4.1 设置项

| # | 内容 | 落点 |
|---|------|------|
| S1 | `AppSettings` 新增 `RootDirectoryMode SmartExtractRootMode = RootDirectoryMode.SmartWrap`（放在 `ExtractRootMode` 之后，同一「包内根目录」段） | `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` |
| S2 | 值域收敛函数 `RootDirectoryMode NormalizeSmartExtractMode(RootDirectoryMode)`：只认 `SmartStrip`，其余（含越界/`Original`/`AlwaysWrap`）一律回落 `SmartWrap` —— 两档之外的值从 JSON 手改进来不能让智能解压退化成「原样」 | `src/MantisZip.Core/Utils/ArchiveRootPolicy.cs` |
| S3 | `ExtractRootMode` 默认值 `Original` → `SmartWrap` | `AppSettings.cs:99` |
| S4 | 设置页「解压 → 常规」新增卡片「智能解压」：两行说明 + `ComboBox`（`SmartExtractRootModeOptions` / `SelectedSmartExtractRootModeOption`，Value 存 `nameof(...)` 与既有 `CompressRootModeOptions` 同构）；载入 / 保存 / `UpdateLocalizedTexts` 三处同步 | `ViewModels/SettingsWindowViewModel.cs` + `Views/SettingsWindow.axaml` |

> **注意**：`AppSettings` 是全量序列化的（无 `DefaultIgnoreCondition`），所以**已有的本地 `settings.json` 里 `"ExtractRootMode": 0` 会继续生效**。该字段从未随版本发布（`git tag` 只到 v0.5.6），不做迁移；开发机想验默认值需手改或删掉该行。

### 4.2 智能解压（GUI）

| # | 内容 | 落点 |
|---|------|------|
| G1 | `dest` 恒为父目录：删掉 `hasSingleRoot ? parentDir : Path.Combine(parentDir, name)` 三元，保留 `parentDir` 的空值兜底（桌面目录） | `ViewModels/MainWindowViewModel.Extract.cs:318-325` |
| G2 | 根策略 → 两段：`ExtractRootPlan.Resolve(mode, _allRawItems, CurrentArchivePath)`（**用已加载的 `_allRawItems`，不二次列包**） | 同上 |
| G3 | `LegacyExtractOptions()` / `CreateLegacyExtractOptions(...)` 各加两个可选形参 `string? stripRoot = null, string? addRoot = null`，透传给已有的 `SelectedItemsExtractService.CreateExtractOptions(...)`（该方法 4 条返回路径已全部写这两个字段） | 同上 :430 / :441 |
| G4 | 状态栏按实际动作三选一（`addRoot != null` → 加根；`stripRoot != null` → 去根；都不动 → 未动） | 同上 :348-350 |
| G5 | 解压后打开目录：`SmartOpenPathResolver` 新增「已知计划」重载 `ResolveOpenPath(dest, stripRoot, addRoot, commonRoot)`，**纯函数不列包**；`OpenExtractedFolderAsync` 加两个可选形参，只有智能解压传 | `Services/SmartOpenPathResolver.cs:45` + `MainWindowViewModel.Extract.cs:253` |

### 4.3 智能解压（CLI，覆盖右键与双击关联）

| # | 内容 | 落点 |
|---|------|------|
| C1 | `ResolveSmartDestCliAsync` 的 dest 恒为父目录（删掉散装的 `Directory.CreateDirectory` 与包名子目录）；同函数解析出 `(stripRoot, addRoot)` | `App.CliExtract.cs:228-251` |
| C2 | 返回载体换成 `sealed record SmartExtractPlan(string TargetDir, string? StripRoot, string? AddRoot)`；密码重试循环改返回 `(SmartExtractPlan Plan, string? Password)` | `App.CliExtract.cs:271-289` |
| C3 | 单文件叶子 `RunCliExtractWithProgressAsync` 加 `SmartExtractPlan? plan = null`，`CreateExtractOptions(..., plan?.StripRoot, plan?.AddRoot)`（当前该处 `:675` 完全没传，是「去根在 CLI 侧无效」的直接原因） | `App.CliExtract.cs:623/675` |
| C4 | 多文件批处理 `RunCliDirectExtractBatchAsync`：`conflictOptions` **从循环外挪进循环内**（每包按自己的结构算 plan，`smart` 之外的 mode 传 `null`） | `App.CliExtract.cs:841/883-887` |
| C5 | `GetArchiveBaseName` 仅剩 `toname` 在用，保留不动 | 同上 :799 |

> CLI 的「解压后打开文件夹」不在本计划（属 [cli-extract-open-folder.md](cli-extract-open-folder.md)，v0.5.9）。

### 4.4 解压设置对话框

| # | 内容 | 落点 |
|---|------|------|
| D1 | 构造器默认 dest：`Path.Combine(dir, name)` → `dir`（`name` 变量随之删除） | `ViewModels/ExtractSettingsViewModel.cs:380-386` |
| D2 | 根策略默认值跟随 S3（代码已 `if (Enum.IsDefined(settings.ExtractRootMode)) RootMode = ...`，无需改） | 同上 :403-405 |

> **副作用（已在决策 7 认可）**：对话框把它自己的「包内根目录」下拉选成「原样」时，由于 dest 已是父目录，散装包会平铺到包的父目录里。预览树照实显示，属 WYSIWYG。

### 4.5 i18n

| # | 内容 |
|---|------|
| I1 | 状态栏三条：`Status_SmartExtractAddedRoot`（已添加顶层文件夹）/ `Status_SmartExtractStrippedRoot`（已去除顶层文件夹）/ `Status_SmartExtractUnchanged`（未改动目录结构）；删除旧的 `Status_SmartExtractSingleRoot` / `Status_SmartExtractNamed`，同步 `MainWindowViewModel.cs:293` 的登记数组 |
| I2 | 设置页卡「智能解压」：标题 + 两行说明 + 两档文案（`Settings_SmartExtract_*`，4 条） |
| I3 | 三份 `strings.*.json` 成对增删，跑 `-Task i18n` |

### 4.6 测试

| # | 内容 | 落点 |
|---|------|------|
| ✅ T1 | `NormalizeSmartExtractMode` 四值 × 越界值 | `tests/MantisZip.Tests/Utils/ArchiveRootPolicyTests.cs` |
| ✅ T2 | 两档 × 三种包型 → `ExtractRootPlan.Resolve` 產出的 `(stripRoot, addRoot)`（复用既有 `ExtractRootPlanTests` 的夹具风格） | `tests/MantisZip.UI.Avalonia.Tests/ExtractRootPlanTests.cs` |
| ✅ T3 | **删掉** `tests/MantisZip.Tests/Engines/SmartExtractTests.cs:149-176` 两个「把 dest 公式抄一遍再断言」的用例（测的是测试自己的算术），改为断言 `ArchiveRootPolicy.Resolve` 的指令 | 同左 |
| ✅ T4 | `SmartOpenPathResolver` 新重载：加根 → `dest\根名`、去根 → `dest`、未动 → 回落到列条目逻辑 | `tests/MantisZip.UI.Avalonia.Tests/SmartOpenPathResolverTests.cs` |
| ✅ T5 | 状态栏三选一映射（抽成纯函数 `SmartExtractSummary.KeyFor(stripRoot, addRoot)` 后测） | 新增 UI 测试 |
| ✅ T6 | `SmartExtractPasswordTests` 适配新返回类型（5 例仍在，断言不变） | `tests/MantisZip.UI.Avalonia.Tests/SmartExtractPasswordTests.cs` |

## 五、任务清单

| # | 任务 |
|---|------|
| ✅ T0 | S1–S3 + T1（设置项 + 值域收敛 + 默认值） |
| ✅ T1 | G1–G4（GUI dest 统一 + 根策略 + 状态文案） |
| ✅ T2 | C1–C4 + T6（CLI 侧 plan 贯通，单文件与多文件两个叶子） |
| ✅ T3 | D1（对话框默认 dest） |
| ✅ T4 | G5 + T4（解压后打开目录用已知计划） |
| ✅ T5 | S4 + I1–I3（设置页卡片 + 三语文案 + 登记数组） |
| ✅ T6 | T2/T3/T5 补测 + `-Task check` / `build` / `tests` / `ui-tests` 全绿 |
| ✅ T7 | 真机矩阵：GUI 智能解压两档 × 三种包型；右键 `--extract-smart` 两档；双击关联 `smart-extract`；对话框默认落点 |
| ✅ T8 | 文档：`docs/PROGRESS.md` 里程碑、`docs/progress-avalonia-detail.md` 条目、`docs/PLAN.md` v0.5.8 收口、上一期 §7.6 指针、`RELEASE_NOTES.md` v0.5.8 章节 |

## 六、验收口径

- [x] 设置页「解压 → 常规」有「智能解压」两档下拉，默认「智能加根」；改完重启仍生效
- [x] 智能解压·加根：单根包 → `父目录\photos\a.jpg`；散装包 → `父目录\包名\x.txt`；多根包 → `父目录\包名\a\x`（逐字节等于改动前）
- [x] 智能解压·去根：单根包 → `父目录\a.jpg`；散装包 → `父目录\x.txt`（原地平铺）；多根包 → `父目录\a\x` + `父目录\b\y`
- [x] 右键盘「智能解压」与双击关联 `smart-extract` 与 GUI 菜单**三条路径落点一致**（同一设置、同一策略）
- [x] 状态栏三条文案与真实动作一致，不再出现「已剥离根文件夹」这种与代码不符的表述
- [x] 解压后「打开目录」打开的是内容真正所在的那一层（加根 → 打加出来的那层；去根 → 打发目录；未动 → 今天的行为）
- [x] 解压设置对话框默认落点 = 父目录，单根包解出 `父目录\photos\…`、散装包解出 `父目录\包名\…`
- [x] `-Task check` / `-Target core` / `-Target avalonia`（Release `--no-incremental`）/ `-Task test` ×2 / `-Task i18n` 全绿

## 七、范围外（明确不做）

- **右键「解压选中项到…」与拖拽兜底的初始目录**（仍在 `父目录\包名`）—— 用户另有计划，见 **KI-018**
- **CLI 解压后打开文件夹** —— [cli-extract-open-folder.md](cli-extract-open-folder.md)（v0.5.9）
- **`--compress-root-mode` / `--extract-root-mode` 等 CLI 参数** —— 上一期即列范围外
- **跨进程队列 / 持久化「上次选了哪档」以外的记忆** —— 无需求

## 八、实施记录（2026-11-13 落地，随 v0.5.8）

### 8.1 任务完成情况

| 任务 | 落地情况 |
|------|---------|
| T0 设置项 | `AppSettings.SmartExtractRootMode`（默认 `SmartWrap`），`ExtractRootMode` 默认值由 `Original` 改成 `SmartWrap`；`ArchiveRootPolicy.NormalizeSmartExtractMode` 把任何非 `SmartStrip` 的值收敛成 `SmartWrap` |
| T1 GUI | `MainWindowViewModel.Extract.cs` 的 `SmartExtract()` 改成 `dest = parentDir` + `ExtractRootPlan.Resolve(mode, _allRawItems, CurrentArchivePath)`；状态文案改按**实际动作**三选一 |
| T2 CLI | `ResolveSmartDestCliAsync` 返回 `(TargetDir, StripRoot, AddRoot)` 三元组、`ResolveSmartDestWithPasswordAsync` 返回四元组（密码重试一次取全）；单文件叶子 `RunCliExtractWithProgressAsync` 与多文件批处理 `RunCliDirectExtractBatchAsync` 均透传，批处理的冲突 options 挪进循环逐包重建 |
| T3 对话框 | `ExtractSettingsViewModel` 构造器默认 `DestinationPath = Path.GetDirectoryName(archivePaths[0])` |
| T4 打开目录 | `SmartOpenPathResolver` 新增纯函数 `ResolveOpenPath` + 已知计划重载；`OpenExtractedFolderAsync` 加 `stripRoot`/`addRoot` 两参，其余三处三参调用不动 |
| T5 设置页 | `Views/SettingsWindow.axaml` 新增「智能解压」卡片（两档下拉，复用解压端四态文案）；三语文案 1513 key |
| T6 测试 | 新增 `SmartExtractSummaryKeyTests`（4 例）、`SmartOpenPathResolverTests` 补 6 例 `ResolveOpenPath`；`ArchiveRootPolicyTests` 补 10 例；`SmartExtractTests` 两个自证式假用例改成断言指令；`SmartExtractPasswordTests` 3 处解构适配四元组；`ExtractSettingsViewModelTests` 默认落点断言按新语义更新 |
| T7 真机 | 见 8.3 |
| T8 文档 | PROGRESS / 细节 / PLAN / KI-018 / 本文件 |

### 8.2 与计划的偏差

1. **状态文案抽的是 `MainWindowViewModel.SmartExtractSummaryKey` 静态方法，不是计划里写的 `SmartExtractSummary.KeyFor` 独立类型** —— 只有一个调用方，为它单开一个类没有收益；`internal static` 足以让测试直接断言映射。
2. **`ExtractRootPlanTests` 没有新增「两档 × 三包型」的用例** —— 该文件已有 `SmartStrip_*` / `SmartWrap_*` 各四种包型的完整覆盖（`Original_*` 也全），两档就是从四态里挑两档，重复覆盖无意义。改用 `SmartExtractTests` 断言「两档 × 单根/多根」的指令，避免两处重复。
3. **CLI 落点统一后 `GetArchiveBaseName` 只剩 `toname` 在用** —— 保留未删（`toname` 需要它处理 `.tar.gz` 双扩展名）。
4. **多文件批处理的冲突 options 从循环外挪进循环内** —— 计划只写了「透传 strip/add」，实际必须逐包重建，否则上一包的剥根/加根会串到下一包。

### 8.3 真机验证（2026-11-13）

夹具 `%TEMP%\mz-smartqa\`：`single-root.zip`（`photos/a.jpg`、`photos/b.jpg`）、`loose.zip`（`x.txt`、`y.txt`）、`multi-root.zip`（`a/p.txt`、`b/q.txt`），每包各放一份到 `run-*` 独立目录。

**CLI（`--extract-smart`）**

| 档位 | 包型 | 实际落点 |
|------|------|---------|
| SmartWrap | 单根 | `run-single-root\photos\a.jpg`（不叠两层） |
| SmartWrap | 散装 | `run-loose\loose\x.txt`（加包名根） |
| SmartWrap | 多根 | `run-multi-root\multi-root\a\p.txt` |
| SmartStrip | 单根 | `run-single-root-2\a.jpg`（剥掉 `photos`） |
| SmartStrip | 散装 | `run-loose-2\x.txt`（原地平铺） |
| SmartStrip | 多根 | `run-multi-root-2\a\p.txt` |

**GUI（工具栏「智能解压」）**

| 档位 | 包型 | 落点 | 状态栏 |
|------|------|------|--------|
| SmartWrap | 散装 | `gui\loose\x.txt` | 「智能解压完成（已添加顶层文件夹）」 |
| SmartWrap | 单根 | `gui3\photos\a.jpg` | 「智能解压完成（目录结构未改动）」 |
| SmartStrip | 单根 | `gui2\a.jpg` | 「智能解压完成（已去除顶层文件夹）」 |

**解压设置对话框默认落点**（`ExtractRootMode` = SmartWrap）

| 包型 | 目标目录（打开时就是父目录） | 实际落点 | 预览树 |
|------|------|------|------|
| 单根 | `…\gui4` | `gui4\photos\a.jpg` | 根节点 = 父目录，下面 `photos` 一层 |
| 散装 | `…\gui5` | `gui5\loose\x.txt` | 根节点 = 父目录，下面 `loose` 一层；选「智能去除」时置灰并提示「该压缩包没有唯一顶层目录」 |

现场已清理：`Stop-Process` 掉所有实例，`settings.json` 从 `%TEMP%\settings.bak-smart.json` 还原（`ExtractRootMode` / `SmartExtractRootMode` 回到 2）。

### 8.4 验证

- `-Task build -Target core -c Release` ✅ 17.1s 0 警告
- `-Task build -Target avalonia -c Release` ✅ 多次（最后一次 24.8s）0 警告；`--no-incremental` 亦通过
- `-Task test -Target tests -c Release` ✅ 11.9s（2 条既有警告）
- `-Task test -Target ui-tests -c Release` ✅ 82.6s 全绿（`Passed 914 / Failed 0`）
- `-Task i18n` ✅ 1513 key 三语一致
- `-Task check` ✅ 0.5.7 六处 / 1513 key / 10 条守卫全绿

### 8.5 留白

1. **`ExtractDestination` 设置项（`ask`/`same-dir`/`desktop`/`last`）与对话框默认落点仍无关联** —— 本次只把对话框默认改成父目录，那个下拉实际只影响更早的路径分支，属既有债务，未动。
2. **右键「解压选中项到…」与拖拽兜底** 仍用 `父目录\包名` —— 用户明确要求先不改，已登记 **KI-018**。
3. **CLI 解压后打开文件夹** 仍无实现 —— 见 `cli-extract-open-folder.md`（v0.5.9）。
4. **`--compress-root-mode` / `--extract-root-mode` / `--smart-extract-root-mode` 三个 CLI 参数** 未加，CLI 一律读设置文件。
