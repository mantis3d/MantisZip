# 大文件 partial 拆分（App / ZipEngine / MainWindowViewModel）

> **状态**: ✅ 已完成（2026-10-20） | **创建**: 2026-10-19 | **优先级**: P2 | **预估工时**: 4-6h
> **来源**: 2026-10-19 会话讨论——用户提出 `App.axaml.cs` 过长，调查后确认第一梯队 3 文件均需拆分，三节设计已获用户批准（形态 A：partial class 分文件）
> **决策**: 纯移动、零行为变化；不抽服务类（YAGNI）；`PreviewViewModel` 明确排除（等 HTML WebView 双轨重构一起做）

## 背景

全仓 `>1000 行` 的 `.cs` 文件按真实行数排行（2026-10-19 核实，`Get-Content | Measure-Object -Line` 忽略空行会少算 ~10%，本文以 `[IO.File]::ReadAllLines().Count` 为准）：

| 文件 | 真实行数 | 状态 |
|---|---|---|
| `Core/Engines/ZipEngine.cs` | 4422 | 串行+并行解压+编码探测+压缩混在一起 |
| `UI/ViewModels/PreviewViewModel.cs` | 3752 | 25+ 个 `ShowXxx` 全格式堆叠（**本期不拆**） |
| `UI/ViewModels/MainWindowViewModel.cs` | 3185 | 90 方法 + 39 属性，横跨浏览/过滤/解压/密码/历史 |
| `UI/App.axaml.cs` | 2455 | 单文件无 partial；CLI 路由 + 初始化 + 主题 + 密码会话 |
| `UI/ViewModels/SettingsWindowViewModel.cs` | 2256 | 123 个 `[ObservableProperty]`（第二梯队，本期不拆） |
| `UI/ViewModels/ProgressViewModel.cs` | 1815 | 刚重构过（第二梯队，本期不拆） |
| `UI/Views/MainWindow.axaml.cs` | 1715 | code-behind（第二梯队，本期不拆） |
| `Core/Engines/SevenZipEngine.cs` | 1494 | 职责单一、低频改动（不拆） |
| `ShellExt/ContextMenuHandler.cs` | ~1231 | COM 组件职责单一（不拆） |

问题证据：

- 这些大文件 `#region` 全为 0，靠方法序堆叠，扩展就是纯 append
- AGENTS.md 里引用的 `App.Password.cs` / `App.Extract.cs`（含具体行号 `App.Extract.cs:662-701`）是 **WPF 时代 partial 拆分的残留描述**，Avalonia 移植时被合并回单文件——文档未跟上合并动作
- 项目已有 partial 拆分先例：`ShellIntegration.cs` / `ShellIntegration.Menu.cs` / `ShellIntegration.Assoc.cs`，团队约定接受 partial 命名 `<Type>.<功能区>.cs`

## 方案

统一**形态 A**：partial class 按功能家族分文件，命名与 `ShellIntegration.*` 一致；private 成员同类内天然共享，零接线成本。**不抽服务类**（拿一堆状态上下文传参，收益 < 接线风险）。

### 节 1：`App.axaml.cs`（2455 行 → 主干 ~740 行，4 个新 partial）

| 新文件 | 行号锚点 | 内容 | 行数 |
|---|---|---|---|
| `App.axaml.cs`（主干） | 1-515 + 726-789 | 字段/构造/`OnFrameworkInitializationCompleted`/CLI 分派 switch/CLI 帮助窗/SevenZip 路径解析/DebugLog | ~560 |
| `App.Theming.cs` | 516-725 | Theme/Font/Compactness 的 Apply/Refresh | ~210 |
| `App.CliExtract.cs` | 790-1479 | 静态 CLI 解压族（`ResolveCliPassword` → `RunCliDirectExtractBatchAsync`） | ~690 |
| `App.CliCompress.cs` | 1480-2320 | 静态 CLI 压缩族（`HandleCompress*` → `CompressWithProgress` → `FindCommonParent`） | ~840 |
| `App.CliInfra.cs` | 1690-1930 | `HandleShellCommand`/`IsDirectoryWritable`/`IsElevated`/`RestartAsAdmin`/`HandleElevationAsync`/`StartPipeServer`/`SendPathsThroughPipe`（压缩+解压两族共用基础设施，按方法归属切非纯行区间） | ~250 |

### 节 2：`ZipEngine.cs`（4422 行 → 主干 ~800 行，4 个新 partial）

| 新文件 | 行号锚点 | 内容 | 行数 |
|---|---|---|---|
| `ZipEngine.cs`（主干） | 头部 + 240-250 + 2322-2481 | 类定义/字段/`CanHandle`/`ListEntriesAsync`/`TestArchiveAsync`/清理 | ~330 |
| `ZipEngine.Open.cs` | 33-239 | `OpenArchiveWithEncodingFallback` + 编码探测（`ResolveFileNameEncoding`/`ZipHasUtf8Flag`/`LooksLikeValidCjk`） | ~205 |
| `ZipEngine.Extract.cs` | 251-1720 | 全量+过滤解压（含 `ExtractAsyncParallel` 491-967 与 `ExtractEntriesAsyncParallel` 1211-1720 两大并行块） | ~1470 |
| `ZipEngine.Compress.cs` | 1721-2410 | `CompressAsync` 主流程 + `MapZipMethodToS7Z` + 分卷清理 | ~700 |
| `ZipEngine.CompressGroup.cs` | 2529-4450 | `AddToArchiveAsync`(820 行)/`DeleteEntriesAsync`/`ReadFileWithRetry`/多线程资格/`CompressGroupWithSevenZip` 等辅助 | ~1700 |

⚠ 并行解压锁竞争雷区（AGENTS.md 记录过 25x 退化事故）位于 `Extract.cs` 两大并行块内，**原样搬动不碰内部逻辑**。

### 节 3：`MainWindowViewModel.cs`（3185 行 → 主干 ~1250 行，5 个新 partial）

| 新文件 | 行号锚点 | 内容 |
|---|---|---|
| `MainWindowViewModel.cs`（主干） | 1-414 主流程 + 2235-2357 | 字段/39 属性/构造/`SwitchLanguage`/`OpenArchive`/`LoadArchiveAsync`/`OpenSettings`/`Toggle*` 等 |
| `…ViewModel.Password.cs` | 195-226 + 1173-1438 | 密码会话/密码状态/`EnterPasswordAsync`/`TryEnsureArchivePasswordAsync` |
| `…ViewModel.Preview.cs` | 425-521 + 1439-1765 | 条目预览打开/`ShowPreviewAsync`(328 行)/`RePreviewCurrentEntry` |
| `…ViewModel.Navigation.cs` | 683-888 + 1766-2234 | 树过滤/展开/导航历史/`GoUp`/`FindNode`/`ApplyFilter` |
| `…ViewModel.Extract.cs` | 2358-2620 + 2793-2956 | `ExtractArchive`/`ExtractSelectedTo`/`SmartExtract`/`TestEntry` 等 |
| `…ViewModel.Compress.cs` | 2621-2796 + 2994-3079 | `NewArchive`/`ExecuteCompressFromSettings`/`AddFilesToArchiveAsync`/`DeleteFiles` |

### 拆分纪律

1. **纯移动**：不改逻辑、不重命名、不 reformat、不合并巨方法；移动时仅调整 using
2. **拆分顺序**（独立 commit）：App → ZipEngine → MainWindowViewModel，每个文件拆完即 build（+test）验证，单文件失败不阻塞其余
3. **AGENTS.md 同步**：修正 `App.Password.cs`/`App.Extract.cs` 过期引用为新 partial 布局；架构节补一段 partial 布局说明
4. 行号锚点以 2026-10-19 核实为准，实施时以方法完整归属重切（服务方法按归属家族而非严格行区间）

## 涉及文件

- `src/MantisZip.UI.Avalonia/App.axaml.cs` → +`App.Theming.cs` +`App.CliExtract.cs` +`App.CliCompress.cs` +`App.CliInfra.cs`
- `src/MantisZip.Core/Engines/ZipEngine.cs` → +`ZipEngine.Open.cs` +`ZipEngine.Extract.cs` +`ZipEngine.Compress.cs` +`ZipEngine.CompressGroup.cs`
- `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` → +`MainWindowViewModel.Password.cs` `.Preview.cs` `.Navigation.cs` `.Extract.cs` `.Compress.cs`
- `AGENTS.md`（过期引用修正 + partial 布局说明）

## 验证

- `dotnet build src\MantisZip.UI.Avalonia`、`src\MantisZip.Core`、`build ShellExt`（Core 改动连带）0 错误
- `dotnet test tests\MantisZip.Tests`、`tests\MantisZip.UI.Avalonia.Tests` 全绿
- 逐文件 `git diff --stat` 确认只有新增文件 + 原文件缩减（移动量 ≈ 新增量）
- 手动冒烟（可选）：GUI 打开 zip → 浏览/过滤/解压 → CLI `--compress-quick` / `--extract-here` 各一条

## 边界

- **不拆**：`PreviewViewModel`（3752 行，等 `preview-extended-formats` / HTML WebView 双轨重构时一起模块化）、`SettingsWindowViewModel`/`ProgressViewModel`/`MainWindow.axaml.cs`（第二梯队缓拆）、`SevenZipEngine`/`ContextMenuHandler`（职责单一）、各 `.axaml`
- **不做**：抽服务类、合并巨方法（`LoadArchiveAsync` 270 行 / `ShowPreviewAsync` 328 行 / `AddToArchiveAsync` 820 行留待后续）、行为变更
- 两个 `.iss`、版本号无关本计划

## 实施结果（2026-10-20 回填）

**状态**：✅ 三阶段全部完成，纯移动零行为变化。

| 阶段 | 原文件 | 拆分后 | commit |
|---|---|---|---|
| 1 | `App.axaml.cs` 2507 行 | 主干 600 + `App.Theming.cs` 263 / `App.CliExtract.cs` 937 / `App.CliInfra.cs` 286 / `App.CliCompress.cs` 565 | `0c42026` |
| 2 | `ZipEngine.cs` 4428 行 | 主干 193 + `ZipEngine.Open.cs` 238 / `.Extract.cs` 1494 / `.Compress.cs` 676 / `.CompressGroup.cs` 1923 | 见 git log |
| 3 | `MainWindowViewModel.cs` 3201 行 | 主干 1352 + `.Password.cs` 326 / `.Preview.cs` 463 / `.Navigation.cs` 495 / `.Extract.cs` 452 / `.Compress.cs` 243 | 见 git log |

- **纯度校验**：阶段 1/2 用行数守恒（旧行数 = 新行数 − N×partial 头部）；阶段 3 额外做非空行逐行排序比对，除 5 个 partial 类闭合括号外完全一致。
- **守卫测试适配**（实施中发现，计划未预见）：拆分导致 3 个源码文本守卫失败——
  - `AppShutdownWiringTests` 只扫 `App.axaml.cs` → 新增 `AppRelPaths` 数组（测试 1/2/4 遍历全部 partial；测试 3 仍锁主干）；
  - `ExtractPreserveFullPathWiringTests` / `ExtractParallelDegreeWiringTests` 只扫 `MainWindowViewModel.cs` → 新增 `MainViewModelRelPaths` 并改为拼接源码扫描。
  - **约定**：新增 partial 必须同步这两个数组。
- **验证**：Core build 0/0、UI build 0/0；Core 测试 747 总 / 744 过 / 3 跳过 / 0 失败；Avalonia 测试 331 总 / 328 过 / 3 跳过 / 0 失败。
- **文档**：ARCHITECTURE.md 新增「大文件 partial 布局（2026-10-20）」；AGENTS.md 无 `App.Password.cs`/`App.Extract.cs` 的 Avalonia 引用需改（现存两处均为 WPF 移植来源的历史引用，保留）。
- **偏差**：行号锚点按当前文件重切（App 2507 / ZipEngine 4428 / MainWindowViewModel 3201，均比立项时多 40–50 行），方法归属按功能家族而非严格行区间，符合计划「实施时以方法完整归属重切」。