# 唯一根目录处理（压缩端四态 + 解压端四态）

> **状态**: ✅ 已完成（2026-11-12 归档，随 v0.5.8；实施记录见文末） | **工时**: 实际约 16h | **来源**: [GitHub issue #35](https://github.com/mantis3d/MantisZip/issues/35)
> **定位**: 压缩设置窗与解压设置窗各新增「包内根目录」四态选项，默认「原样」，不影响现有所见即所得习惯
> **阶段归属**: 2026-11-12 单开 **v0.5.8** 只做本项（用户拍板）；原 v0.5.8「拖拽收尾与碎片清仓」顺延为 v0.5.9，其后两段依次 +1（队列 → v0.5.10、新功能与债务 → v0.5.11）

---

## 一、背景

issue #35（报告人 peter8777555）缠了两条线：

- **线 1（本计划）**：「唯一根目录」可选处理——压缩时可以「不包含所选目录本身」或「给多项套一层唯一根目录」；解压时可以「去掉压缩包顶层那层多余目录」。报告人本人是所见即所得习惯、明确不会用，只要保证**默认行为不变**（插件式选项）。Bandizip 已有同类功能。
- **线 2（选择器手势，不在本计划）**：Ctrl+A 后还要点「添加所选」的两步吐槽——**已由 2026-11-03 的 `checkbox-mode-consistency`（v0.5.6）解决**。issue 回复闭环由 maintainer 自行处理。

**与既有功能的边界**：智能解压（smart-extract，✅）是右键入口的**自动判定**（单根 → 剥离 / 散装 → 建包名目录）；本计划是设置窗里的**手动选项**，二者共享同一个「唯一根判定」，不另起一套。**右键「智能解压」行为保持不变**（用户 2026-11-12 拍板）：它继续自动判定，不读取设置窗的四态值。`extract-preserve-full-path-toggle` 管的是「条目完整路径 vs 解到目标目录」，与本选项正交（剥根 + 保留完整路径可同时开）。

## 二、设计决策（用户逐项确认）

| # | 问题 | 定案 |
|---|------|------|
| 1 | 选项形态 | **四态下拉（ComboBox）**（不是两个独立开关，避免组合出非法态）：`原样 / 智能去除唯一根 / 智能添加唯一根 / 始终添加唯一根`，默认「原样」。~~初版是竖排四单选~~，2026-11-12 用户拍板改为下拉：不可用档位**从选项里直接拿掉**（不是置灰），下方用一行灰字说明「为什么少了几项」，既不占四行高度，也不留「选了却什么都不发生」的死选项 |
| 2 | 「智能」的含义 | ②③ 各自**只在条件成立时动手、否则保持原样**；②③ 永不叠加（一个输入要么「是唯一根」要么「不是」） |
| 3 | ④ 与 ③ 的区别 | ④ **无条件**加一层（原本已是唯一根也会再加，得到 `<根名>/<原根>`）；③ 只在「原样结果不是唯一根」时加 |
| 4 | 压缩端 ② 的条件 | **`Sources` 恰为一个目录**；多目录 / 文件+目录混合 / 纯文件时该档位不出现在下拉里（等价于「原样结果恰好有一个顶层目录」） |
| 5 | 压缩端 ③④ 的条件 | `OutputMode != Separate`；Separate 下两档不出现（一包一个源，套根无意义），② 仍可用 |
| 6 | 根名 | **输入框，默认值为压缩包文件名去扩展名**（`archive.7z` → 包内根 `archive/`），用户可改；解压端不加输入框，固定用压缩包名去扩展名 |
| 7 | 解压端 ②③ 的条件 | ② 仅在**包确有唯一根**时出现在选项里（否则隐藏 + 一行说明）；③④ 恒可用。判定复用 `ArchiveStructureAnalyzer.HasSingleRootDirectory` |
| 8 | 右键「智能解压」 | **保持不变**，不读本选项（否则默认「原样」会让右键行为变化） |
| 9 | 默认值持久化 | 与其它设置一致：**设置窗每次加载默认、确认不回写**；默认值在主设置窗口改（`AppSettings` 新增项当次提交必须接上消费者——settingswiring 守卫） |
| 10 | GUI / CLI | 首版只做 GUI；CLI 参数列为后续（见「范围外」） |

### 四态语义定义

**压缩端**（`Sources` = 设置窗里选中的源）：

| 四态 | 可用条件 | 包内条目名（`7zip/` 单目录源 ／ `a.txt`+`docs/` 混合源） |
|------|----------|--------------------------------------------------|
| ① 原样 | 恒可用 | `7zip/a.txt` ／ `a.txt` + `docs/b.txt`（现状） |
| ② 智能去除唯一根 | `Sources` 恰为一个目录 | `a.txt`（那层目录本身不进包，子树以该目录为基准保留） |
| ③ 智能添加唯一根 | `OutputMode != Separate`，且原样结果**不是**唯一根 | 单目录源 → **不动**（仍是 `7zip/a.txt`）；混合源 → `<根名>/a.txt` + `<根名>/docs/b.txt` |
| ④ 始终添加唯一根 | `OutputMode != Separate` | `<根名>/7zip/a.txt` ／ `<根名>/a.txt` + `<根名>/docs/b.txt` |

**解压端**（目标目录 `D:\out`，包 `x.7z`）：

| 四态 | 可用条件 | 落盘位置（有唯一根 `7zip/` ／ 散装 `a.txt`+`docs/b.txt`） |
|------|----------|--------------------------------------------------|
| ① 原样 | 恒可用 | `D:\out\7zip\a.txt` ／ `D:\out\a.txt` + `D:\out\docs\b.txt` |
| ② 智能去除唯一根 | 包确有唯一根（否则不进选项） | `D:\out\a.txt` ／ —— |
| ③ 智能添加唯一根 | 恒可用 | ——（已唯一根，不动）／ `D:\out\x\a.txt` + `D:\out\x\docs\b.txt` |
| ④ 始终添加唯一根 | 恒可用 | `D:\out\x\7zip\a.txt` ／ `D:\out\x\a.txt` + `D:\out\x\docs\b.txt` |

**叠序（硬约束）**：先做结构调整（剥根 / 加根前缀 → 决定包内 entry 名与落盘相对路径），**再做** Filter 过滤与 Plan 白名单；否则过滤路径要重算两遍。解压端：**先剥根 / 加根，再裁浏览前缀**（`ExtractPathResolver` 内部顺序）。

## 三、实现草案

### 3.1 统一决策层（两侧共用）

| # | 内容 | 落点 |
|---|------|------|
| R1 | 新增 `RootDirectoryMode { Original = 0, SmartStrip = 1, SmartWrap = 2, AlwaysWrap = 3 }` + `RootAdjustment(bool Strip, bool Wrap)` + `Resolve(mode, hasSingleRoot)` 纯函数 + `GetWrapName`/`GetStripName` 辅助 | 新建 `src/MantisZip.Core/Utils/ArchiveRootPolicy.cs` |
| R2 | 单元测试覆盖 4 态 × hasSingleRoot 2 值 = 8 组合，以及枚举值稳定性（设置序列化依赖数值） | `tests/MantisZip.Tests/Utils/ArchiveRootPolicyTests.cs` |

### 3.2 压缩端

| # | 内容 | 落点 |
|---|------|------|
| C1 | `AppSettings` 新增压缩根模式默认值（int，对应 `RootDirectoryMode`）+ 设置窗口「压缩」区默认值选择组 | 设置模型 + `AppSettingsJsonContext` + `SettingsWindowViewModel`/`.axaml` |
| C2 | `CompressSettings` record 末尾追加 `RootDirectoryMode RootDirectoryMode = Original` + `string? RootDirectoryName = null`（带默认值，不破坏既有构造调用） | `Models/CompressSettings.cs` + `CompressSettingsViewModel` 载入默认 / `CaptureSettings` |
| C3 | 压缩设置窗：四态 `ComboBox` + 根名 `TextBox`（③④ 才显示）+ 选项裁剪（② 非单目录源、③④ Separate 时该档不出现）；载入默认、确认**不回写** | `Dialogs/CompressSettingsWindow.*` + VM |
| C4 | entry 名统一入口：`FileScanner.CollectFiles` 增加 `RootDirectoryMode` + `string? wrapName` 参数，按 `RootAdjustment` 决定前缀（剥根 → 无前缀；加根 → `<根名>/<原前缀>`） | `src/MantisZip.Core/Utils/FileScanner.cs` |
| C5 | 7z 路径：剥根 → `PreserveDirectoryRoot = false` 走 `CompressDirectory`；加根 → 改走 `CompressFileDictionary`（字典 key 即 entry 名）；两者都不发生 → 现状不变 | `src/MantisZip.Core/Engines/SevenZipEngine.cs` `CompressAsync` + `BuildSourceRelativeKey` |
| C6 | `CompressRequest` / `ArchiveOptions` 透传（`RootDirectoryMode` + `RootDirectoryName`），`CompressService.BuildOptions` 与两处 `CollectFiles` 调用点对齐 | `CompressService.cs` / `ArchiveEngine.cs` / `ZipEngine.Compress.cs` / `TarGzEngine.cs` / `SourceEntryEnumerator.cs` |
| C7 | 预览侧：`ResultPreviewService` 按同一 `RootAdjustment` 生成预览条目名（预览 = 实际） | `src/MantisZip.UI.Avalonia/Services/ResultPreviewService.cs` |

### 3.3 解压端

| # | 内容 | 落点 |
|---|------|------|
| E1 | `AppSettings` 新增解压根模式默认值（int）+ 设置窗口「解压」区 | 同规则 23 接线 |
| E2 | `ExtractPathResolver` 增加「剥根 / 加根」两参：`ResolveRelativePath(entryKey, currentFolder, preserveFullPath, stripRoot: null, addRoot: null)`，内部顺序 = 剥/加根 → 裁浏览前缀 → `SanitizeEntryPath`；根目录条目本身被剥掉时返回 `null`（不建该目录） | `src/MantisZip.Core/Utils/ExtractPathResolver.cs` |
| E3 | 引擎全量解压也走同一规则：`ArchiveOptions` 新增 `StripRootDirectory` / `AddRootDirectory`，各引擎 `FileConflictHelper.GetSafePath(destinationPath, X)` 改经新增的统一入口 `EntryOutputPath.Resolve(destinationPath, entryKey, options)`（内部 = 剥/加根 + `GetSafePath`），目录条目同样处理 | `ArchiveEngine.cs` + `ZipEngine.Extract.cs` / `TarGzEngine.cs` / `SevenZipEngine.cs` |
| E4 | 选区/过滤解压走 `SelectedItemsExtractService.CreateExtractOptions` 透传两个新字段；`ExtractFlow.ExtractAsync` 与 `ExtractSession` 从载荷取值 | `SelectedItemsExtractService.cs` / `ExtractFlow.cs` / `ExtractSession.cs` |
| E5 | `ExtractSettings` 新增 `RootDirectoryMode` 字段（执行侧读它解析出 strip/add，避免两处各存一份）；解压设置窗四态下拉 + ② 不可用时不出现在选项 + 载入默认、确认不回写 | `Models/ExtractSettings.cs` / `Dialogs/ExtractSettingsWindow.*` + VM |
| E6 | 预览树一致性：`ResultPreviewService` 的解压预览同样传两参 | `ResultPreviewService.cs` |

### 3.4 i18n / 测试

| # | 内容 | 落点 |
|---|------|------|
| I1 | 三语 key：四态文案（4 条复用两侧）、选项组标题、根名标签、当前档位说明 ToolTip、裁剪说明行三条、设置窗默认值两组文案 | 三份 `strings.*.json` + 两个 VM 的登记 |
| T6 | 测试：`ArchiveRootPolicy` 8 组合；`FileScanner` 四态 entry 名；`ExtractPathResolver` 四态（含剥根 + preserveFullPath 并存、根目录条目被丢弃）；预览 = 实际一致性；`verify-repo` 全绿 | 两个测试项目 |
| T7 | 真机手动验证（①单目录源 ②四项 × 四态 ③解压四态 ④散装包 ② 不出现） | 手工 |

## 四、任务清单

| # | 任务 | 状态 |
|---|------|------|
| T0 | 统一决策层 R1–R2（`ArchiveRootPolicy`） | ✅ |
| T1 | 压缩端 C1–C3（设置默认 + record 字段 + 设置窗 UI 与选项裁剪） | ✅ |
| T2 | 压缩端 C4–C7（entry 名统一入口 + 7z 路径 + 透传 + 预览） | ✅ |
| T3 | 解压端 E1–E3（设置默认 + resolver 四态 + 引擎全量路径） | ✅ |
| T4 | 解压端 E4–E6（载荷透传 + 设置窗 + 预览一致） | ✅ |
| T5 | i18n I1 + 三语 key 校验 | ✅ |
| T6 | 测试 T6 + `verify-repo` 构建 / ui-tests / guards 全绿 | ✅ |
| T7 | 真机手动验证 | ✅ |

## 五、验收口径
> 全部达成（2026-11-12 真机矩阵验证）。

- [x] 默认行为零变化：两侧均为「原样」时，所有现有流程（含右键智能解压）结果与改动前逐字节一致；
- [x] 压缩 ②：选中 `7zip` 目录压缩 → 包内无 `7zip/` 层、子项结构与目录内一致；多源 / 混合源 / 单文件时该态不出现在下拉里；
- [x] 压缩 ③：单目录源选 ③ → 包内结构不变（`7zip/…`）；混合源选 ③ → 全部项都在 `<根名>/` 下；
- [x] 压缩 ④：单目录源选 ④ → `<根名>/7zip/…`；Separate 模式下 ③④ 不出现在下拉里、② 仍可用；
- [x] 根名输入框默认包文件名去扩展名，可改；改后包结构按新根名；
- [x] 解压 ②：唯一根包 → 预览树与实解一致少一层；散装包 → 该态不出现在下拉里；
- [x] 解压 ③④：散装包 → 全部落 `<包名>/` 下；唯一根包选 ④ → `<包名>/<原根>/…`；
- [x] 解压侧「剥根 + 保留完整路径」可同时开且叠序正确（先剥根再裁前缀）；
- [x] 重启应用后设置窗加载的是主设置里改过的默认值；单次运行的选择不回写；
- [x] `pwsh -File scripts\verify-repo.ps1 -Task check` / `-Task build -Target avalonia` / `-Target core` / `-Task test` / `-Task i18n` / `-Task guards` 全部通过。

## 六、范围外（明确不做）

- **CLI 参数**（`--compress-root-mode` / `--extract-root-mode` 之类）——首版不做，后续可与 `compress-preset` 或 CLI 路由批量统一加。
- **右键「智能解压」跟随本选项**——用户拍板保持不变（它自己的「单根剥 / 散装建包名目录」就是智能版行为）。
- ShellExt COM 右键免窗直通（右键压缩仍走设置窗，Windows 11 一级菜单计划另行）。
- 线 2（选择器手势）问题回归 / issue 回复——已由 v0.5.6 覆盖，闭环由 maintainer 处理。

## 七、实施记录（2026-11-12，v0.5.8 落地）

### 7.1 任务完成情况

| # | 内容 | 结果 |
|---|------|------|
| T0 | `ArchiveRootPolicy` 决策层 | ✅ `Utils/ArchiveRootPolicy.cs` 192 行；`RootDirectoryMode` 数值即持久化契约 |
| T1 | 压缩端设置默认 + record 字段 + 设置窗 UI | ✅ `AppSettings.CompressRootMode/CompressRootName`、`CompressSettings.RootMode/WrapRoot`、压缩窗「包内根目录」四态下拉 + 根名输入框 |
| T2 | 压缩端 entry 名统一入口 + 7z + 透传 + 预览 | ✅ `FileScanner.CollectFiles` / `SourceEntryEnumerator` / `SevenZipEngine.CompressFileDictionary` / `ResultPreviewService.ProjectForRootMode` |
| T3 | 解压端 resolver 四态 + 引擎全量路径 | ✅ `ExtractPathResolver` 两个方法各加两参（叠序先剥/加根、再裁前缀）、`EntryOutputPath` 同源 |
| T4 | 解压端载荷透传 + 设置窗 + 预览一致 | ✅ `ExtractSettings.RootMode`、三类执行链 + `ExtractRootPlan`、解压窗四态下拉 |
| T5 | i18n | ✅ 三语各 13 个新 key，`-Task i18n` 1510 key |
| T6 | 测试 + 门禁 | ✅ 新增 107 例（Core 75 / UI 32），`tests` 与 `ui-tests` 全绿、`check` 11 守卫全绿 |
| T7 | 真机四态矩阵 | ✅ 压缩端四态 + 多源；解压端唯一根包与散装包 |

### 7.2 与草案的偏差

1. **选项形态：竖排四单选 → 四态下拉**（用户 2026-11-12 拍板）。不可用档位**从选项里拿掉**而不是置灰，下方一行灰字说明原因；理由是不占四行高度、且不留「选了却什么都不发生」的死选项。决策表第 1 条与 §3.2 C3 / §3.3 E5 已同步改词。
2. **解压端根名不设输入框**（草案即如此，实施确认）：固定用包名去扩展名；压缩端才有 `WrapRoot` 输入框。
3. **`GetWrapName` 多一个 `fallbackArchivePath` 形参**（草案只写 `wrapRoot`）。原设计默认回落 `archive`，实施时发现这让「预览传包名、实际不传」分叉，见 7.3 缺陷 ②。
4. **`ExtractRootPlan` 是实施期新增的文件**（草案把它塞在 `ExtractFlow` 里）。三条执行链各写一次「模式 → 剥/加根」判定迟早不一致，抽成单一入口后预览与执行共用同一函数。
5. **`CompressSettings` 的新字段追加在 record 末尾并带默认值**（草案未提）。该 record 有 21 个位置参数，插在中间会炸掉所有既有构造调用。

### 7.3 实施中抓出的两个真缺陷

1. **`ArchiveRootPolicy.Apply` 破坏了「原样逐字节不变」**：无条件走 `ArchivePath.Normalize` 把分隔符统一成 `/`，`SourceEntryEnumeratorTests.Enumerate_DirectorySource_UsesDirNamePrefixAndLeafName` 断言 `data\a.txt` 时直接失败；真机表现是进度窗「列表」模式的 Pending 行永远等不到终态（Key 对不上）。修法 = 两个根段都没给时**原样返回、跳过归一化**，并补三个用例钉住。
2. **加根时根名回落成字面量 `archive`**：`FileScanner.CollectFiles` 调 `GetWrapName(wrapRoot)` 没传 `fallbackArchivePath`，而预览侧 `ResultPreviewService` 传了 `effectiveOutputPath` ⇒ 预览显示 `mz-rootqa/7zip/…`、实压出来 `archive/7zip/…`。只有 ZIP / TarGz 有此问题（7z 那条路径本来就传了 `outputPath`）。修法 = `CollectFiles` 加 `archivePath` 参数并由两个引擎各自传 `outputPath`，`CompressFlow.SeedEntryItemsAsync` 改为接整个 `CompressRequest`。

### 7.4 验证

- `-Task build -Target core` / `-Target avalonia` Release `--no-incremental`：**0 警告**（avalonia 15.6s）。
- `-Task test -Target tests -c Release`：15.9s 全绿（2 条既有警告）；`-Target ui-tests`：57.1s 全绿。
- 新增用例逐个计数：`ArchiveRootPolicyTests` 39、`ExtractPathResolverRootModeTests` 14、`FileScannerRootModeTests` 13、`CompressRootModeTests` 9、`ExtractRootPlanTests` 19、`RootModeDropdownTests` 13 ⇒ 共 107。
- `-Task i18n` 1510 key（三语一致、无重复、占位符对齐）；`-Task check` 10 条守卫全绿。

### 7.5 真机记录

- 夹具：`%TEMP%\mz-rootqa\`（`7zip/a.txt` + `7zip/inner/b.txt`、`other/c.txt`）、`%TEMP%\mz-rootqa2\`（`single-root.zip` 含 `photos/a.jpg` + `photos/b.jpg`、`loose.zip` 含 `x.txt` + `y.txt`）。
- 压缩端：①原样保留 `7zip/`；②单目录源去掉该层（包内 `a.txt` + `inner/b.txt`）；③单目录源结构不变、多源变 `<根名>/7zip、other`；④单目录源变两层 `<根名>/7zip/`。多源时下拉里②消失并给说明。根名留空时用包名。
- 解压端：唯一根包②预览与实解都少一层、③不动、④多得 `<包名>/photos/`；散装包②消失并显示「该压缩包没有唯一顶层目录…」。
- 环境坑：`--open <包>` 才打开主窗口，单给路径只会弹压缩设置窗；`GetWindowRect` 含标题栏 ⇒ 截图坐标 ≠ 屏幕坐标；改 XAML 必须 `--no-incremental` 构建。

### 7.6 留白

- CLI 参数（`--compress-root-mode` 等）按草案属范围外，未做。
- 右键「智能解压」保持自身自动判定，不读这两个设置（用户拍板）。
- 便携模式下的默认根名仍依赖输出路径，跨盘多源时回落首源名（`{name:auto}` 同规则）。
