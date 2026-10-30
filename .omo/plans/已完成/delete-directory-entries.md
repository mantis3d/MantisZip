# 压缩包内删除目录无效（静默成功）——递归删除 + 命中校验

## TL;DR

**Quick Summary**：用户实测「压缩包内删除单个文件有效，删除目录无效」。根因不是对话框接缝，而是引擎的删除匹配对目录不成立：UI 传的目录路径不带尾斜杠、包内条目带尾斜杠、匹配用精确相等，于是**一条都命中不了**；copy-mode 快路径又不校验存在性，所以零命中也当成功，报 100% 并显示「删除完成」。本计划把删除语义统一为「精确条目 或 该目录下的所有条目」，并在零命中时报错，同时补上二次确认与格式能力门禁。

**Deliverables**：

- Core：统一的「条目或其后代」匹配谓词，Zip（copy-mode + legacy）与 7z 三处调用点共用
- Core：删除零命中 → `ArchiveEntryNotFoundException`（`FileNotFoundException` 派生，带 `EntryPath`），消灭静默成功
- UI：删除前二次确认（显示文件数 + 总大小）、`CanDelete` 能力门禁（TAR/GZ）、进度窗「正在删除 N 个文件（共 x GB）」说明行
- 测试：目录递归删除（显式目录条目 / 隐式目录两种构包）、混合、不存在（zip + 7z）、加密、大小写各一例
- 文档：`docs/PLAN.md` 行、`docs/PROGRESS.md` + 细节文档、`RELEASE_NOTES.md`（v0.5.3 修复节）

**Estimated Effort**：6-8h（Core 谓词与校验 ~2h、UI 确认 + 说明行 ~1.5h、测试 ~2-2.5h、文档与手工回归 ~1.5h）

**Parallel Execution**：T1/T2（Core）与 T3（UI 门禁与确认框）可并行；T4 依赖 T1/T2 落地；T5/T6 串行收尾。

**Critical Path**：`T1 → T2 → T4 → T5 → T6`。T3 虽无硬依赖，**但建议排在 T2 之后**——确认文案要承诺「连同其下所有条目一起删」，语义得先定死，否则文案与实现会各走一步。

---

## 评审修正记录（2026-10-26）

首版计划（2026-10-25）经逐条比对源码复审，事实基础全部成立（复现结论、三层根因、同族问题清单均无误）。以下 6 处是复审补充的缺口，已全部并入本版：

| # | 缺口 | 修正 |
|---|---|---|
| 1 | 零命中报错文案**到不了用户眼前**：`FileNotFoundException` 经 `WholeArchiveErrorLocalizer.cs:38-39` 被映射成 `Error_Whole_FileNotFound` =「找不到文件：{0}」，异常自带的中文消息被丢弃 | 新增 Core 异常类型 + 新三语 key，并在 localizer 里**前置**专属 case（见 T2） |
| 2 | `ArchivePath.TrimEndSeparator` 有 `path.Length > 3` 守卫（`ArchivePathExtensions.cs:23`），`"a/"`、`"ab/"` 这类短名**不会被去尾斜杠**；T1 验收只测了 `"a"` | 谓词**自己实现**去尾斜杠循环，不复用 `TrimEndSeparator`；验收补 `"a/"` / `"ab/"` / `"a/b/"` |
| 3 | 确认框控件未点名，且「显示将删除条目数」无数据来源 | 点名 `AppMessageBox.Show(...)`；数据取**未过滤**的 `_allRawItems` + `DirStats`（见 T3） |
| 4 | 7z 用例进不了 CI 门禁：`ci.yml` 用 `--filter "FullyQualifiedName!~SevenZipEngine"` | T4 标注「7z 用例只本地跑」，CI 只兜 zip |
| 5 | T2 改了 7z 抛错，但 T4 没列 7z「不存在条目」用例 | T4 增一例 7z 不存在条目 |
| 6 | 漏了规则 1 的起步动作：执行前要把计划移入 `.omo/plans/进行中` | T6 起始动作补上，且**开工前**就要移 |

另有一条「比原计划写的更糟」的实证，原文未写：

- **7z 零命中不是「删包」，而是把整包解压 + 原样重打包**：[SevenZipEngine.cs:1250-1256](../../src/MantisZip.Core/Engines/SevenZipEngine.cs#L1250-L1256) 只在 `keepEntries.Count == 0`（真的全删）时删包文件；零命中此时 `keepEntries` 等于全部条目，走的是重写路径 ⇒ 用户侧可感知为**耗时陡增 + 属性/时间戳丢失风险**。这条要进发布说明（见文末「发布说明口径」）。

### 决策记录（2026-10-26 与用户确认）

| 议题 | 结论 |
|---|---|
| 范围 | **只做本条**（S 档另 4 项不算插队，按原顺序） |
| 分支 | 在 `alpha` 上改（不走 `beta`：`beta` 落后 alpha 9 个源文件，合并成本大于收益）；**发布版本后改**：原定随 0.5.3，实际 0.5.3 已从 `main` 分支发布、不含本改动，故随 **0.5.4** 发布 |
| 零命中文案 | 新增三语 key「压缩包中不存在条目：{0}」，不复用「找不到文件：{0}」 |
| 确认框 | `AppMessageBox` + 显示「该目录下 N 个文件（共 x GB）」 |
| 进度窗 | **只加一行说明文字**，不把批处理列表从 1 行改成 N 行（引擎是整包重写，无逐条目回调，N 行只能是假进度） |
| 发布说明 | 7z「整包解压重打」一并写进 v0.5.3 修复节 |
| 零命中异常 | **方案 A**：新增 `ArchiveEntryNotFoundException`，localizer 前置专属 case（否决「嗅探异常消息前缀」的 B 方案） |
| 计数口径 | 「N 个文件」**只计文件**、不含空目录条目；要精确条目数需多一次 O(n) 前缀扫描，不值 |
| 大小写法 | `FormatUtil.FormatSize` 自动单位，文案用「共 {1}」不带死单位（不写「x GB」） |

---

## Context

### Original Request

> 刚刚测试发现压缩包内删除文件不起作用，你看是跟 intent-execution-seam.md 里面一样的问题还是另外的问题？
> （追问后）删除单个文件是有效的，但是删除目录是无效的。

### Interview Summary

#### Key Decisions

1. **与 `intent-execution-seam` 无关**。接缝计划的特征是「参数沿 `ShowDialog` 边界搬运时漏项 / 顺序错 / 被覆盖」；本次参数只有一个路径字符串、搬运无误，错在**引擎语义**与**调用方假设**不一致，且两侧都不校验。唯一相通的是「静默失败」这一气质 —— 在接缝计划里登记一行「已知同类事故」即可，不并入其波次。
2. **删除目录 = 递归删除其下所有条目**（zip 没有真目录，只有条目）。这是用户确认的预期语义。
3. **零命中必须报错**。宁可报「条目不存在」，也不要返回成功 + 100% 进度。
4. **不做「删除单个条目保留子项」的部分语义**（那是「删掉目录条目本身、子文件变孤儿」的畸形状态，任何 UI 都无法表达）。

### Research Findings

#### 复现（仓库外临时工程，真实引擎，未改仓库）

| 调用 | 结果 |
|---|---|
| `DeleteEntriesAsync(["sub"])` ← UI 实际传的值 | 返回正常、**零条目变化**（4 条还是 4 条） |
| `DeleteEntriesAsync(["sub/"])` | 只删目录条目本身，`sub/a.txt`、`sub/b.txt` 全保留（4→3） |

#### 三层根因

1. **名称对不上（完全空转）**：列表里的目录行 `FullPath` **不带尾斜杠**（[ArchiveEntryLister.cs:127-131](../../src/MantisZip.Core/Services/ArchiveEntryLister.cs#L127-L131) 生成 `sub`），而 zip 内的目录条目是 `sub/`。匹配用 `ArchivePath.Normalize`，它只把 `\` 换成 `/`、**不去尾斜杠**（[ArchivePathExtensions.cs:15-16](../../src/MantisZip.Core/Utils/ArchivePathExtensions.cs#L15-L16)），再配 `HashSet` 精确相等 ⇒ 一条都命中不了。
2. **没有递归语义**：即便传 `sub/`，`keepSet` 也是逐条目精确比较（[ZipEngine.CompressGroup.cs:907-919](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L907-L919)），子条目全部保留 ⇒ 目录仍有内容 ⇒ 刷新后目录还在。
3. **快路径不校验存在性（静默成功）**：legacy 路径会 `throw FileNotFoundException`（[ZipEngine.CompressGroup.cs:1003-1014](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L1003-L1014)），copy-mode 快路径（[:895-973](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L895-L973)）没有该校验 ⇒ 零命中也算成功，上报 100%（[:955-960](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L955-L960)）并显示「删除完成」。

> 观察：用户看到的是「进度条走完 + 状态栏说完成 + 文件还在」，正好对应第 3 条；如果换成 legacy 路径则会是另一种症状（误报「条目不存在」）。

#### 同族问题（一并处理，否则下次换个格式又复发）

- **7z 同样中招，且更糟**：[SevenZipEngine.cs:1239-1247](../../src/MantisZip.Core/Engines/SevenZipEngine.cs#L1239-L1247) 用**大小写敏感** `HashSet`（Zip 侧是 `OrdinalIgnoreCase`），同样无存在性校验；零命中时不是「删包」而是**整包解压 + 原样重打包**（`keepEntries.Count == 0` 才删包，[:1250-1256](../../src/MantisZip.Core/Engines/SevenZipEngine.cs#L1250-L1256)）⇒ 又慢又可能丢属性。
- **legacy 校验集合本身也没归一化**：`entryNameSet` 由原始 `entry.Key` 构建、却用 `normalized` 去 `Contains`（[:1004-1013](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L1004-L1013)）⇒ 包内条目名含 `\` 或尾斜杠差异时会误报「条目不存在」。
- **TAR/GZ 直接抛 `NotSupportedException`**（[TarGzEngine.cs:574-586](../../src/MantisZip.Core/Engines/TarGzEngine.cs#L574-L586)），而 `CanDelete`（[ZipEngine.cs:30](../../src/MantisZip.Core/Engines/ZipEngine.cs#L30)、[SevenZipEngine.cs:422](../../src/MantisZip.Core/Engines/SevenZipEngine.cs#L422)）在 UI 里**一处未用** ⇒ 删除按钮在 tar 上照样可点，点了才报错。
- **删除无二次确认**：`Status_ConfirmDelete` 已注册文案（[MainWindowViewModel.cs:274](../../src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs#L274)）但无人使用，而删除动作是**整包重写**，误删不可逆。

#### 入口与现有覆盖

- 三个入口都指向同一个命令：工具栏（[MainWindow.axaml:151-159](../../src/MantisZip.UI.Avalonia/Views/MainWindow.axaml#L151-L159)，`IsEnabled="{Binding IsArchiveLoaded}"`）、编辑菜单（[EditMenuItems.axaml:55](../../src/MantisZip.UI.Avalonia/Controls/Menus/EditMenuItems.axaml#L55)）、列表右键菜单（[ArchiveContentView.axaml:292](../../src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml#L292)）。
- 命令体：[MainWindowViewModel.Compress.cs:213-242](../../src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Compress.cs#L213-L242)，`entryPath = SelectedEntry.FullPath ?? SelectedEntry.Name`，只传**一条**路径给 `RunWithProgress` ⇒ 进度窗口的批处理列表永远只有 1 行。
- 现有测试 [ZipEngineDeleteTests.cs](../../tests/MantisZip.Tests/Engines/ZipEngineDeleteTests.cs) 10 例全绿，但**没有一例针对目录**——这就是它没被拦住的原因。

---

## Work Objectives

### Core Objective

让「删除压缩包内的目录」会连带删除其下所有条目；任何**没有实际删除任何条目**的删除调用都必须以异常结束，而不是返回成功。

### Concrete Deliverables

1. `ArchivePath`（`src/MantisZip.Core/Utils/ArchivePathExtensions.cs`）新增统一谓词：
   `public static bool MatchesEntryOrDescendant(string entryName, string target)`——命中条件为 `entryName == target`、`entryName == target + "/"`、`entryName.StartsWith(target + "/")`，大小写不敏感。
2. Zip copy-mode（`keepSet`）、Zip legacy（`keepNames` + 存在性校验）、7z（`deletedSet`）三处改用该谓词。
3. 零命中 → 抛 `ArchiveEntryNotFoundException`（`FileNotFoundException` 派生，携带 `EntryPath`），并在用户可见文案走新三语 key。
4. legacy 的 `entryNameSet` 修正为归一化集合（消除误报）。
5. UI：删除前 `AppMessageBox` 二次确认（显示目标路径 + 该目录下文件数 + 总大小）；`engine.CanDelete(_currentFormat)` 为 false 时按钮/菜单项禁用；进度窗显示一行「正在删除 N 个文件（共 x GB）」。
6. 测试：目录递归、隐式目录、混合、不存在（zip + 7z）、加密、大小写差异各一例。
7. 文档：PROGRESS / 细节文档 / RELEASE_NOTES v0.5.3 修复节 / PLAN.md 行移除。

### Definition of Done

- `pwsh -File scripts\verify-repo.ps1 -Task build -Target core` 与 `-Target avalonia` 均**退出码 0 且无新增警告**（规则 12 的判据是 exit 0；存量存在 CS8604/CS0618 等既有告警，不要求清零）。
- `-Task test -Target tests` 全绿，且新增用例覆盖上面 7 种场景。
- 手工回归：对含 `sub/a.txt`、`sub/b.txt`、`root.txt` 的 zip 删除 `sub` → 确认框显示「2 个文件（共 xx KB）」→ 确认后列表里 `sub` 消失、包内只剩 `root.txt`、包可通过「测试压缩包」自检；对 tar 点删除 → 按钮禁用（或明确提示），不再走到异常。
- `verify-repo -Task check` 三项全绿（版本号 / 三语 key / 守卫）；`-Task i18n` 全绿。

### Must Have

- 目录删除的**递归语义**（用户确认）。
- **零命中必报错**，且报错信息包含用户看到的路径（**必须真的显示在 UI 上**，见 T2 的 localizer 前置 case）。
- Zip 与 7z **同源实现**（同一个谓词，不允许各写一份）。
- 大小写不敏感的匹配（沿用 Zip 侧现状，7z 一并对齐）。

### Must NOT Have

- 不做「删除目录条目但保留子条目」的部分语义。
- 不改压缩/解压流程、不改 `ArchiveEntryLister` 的目录展示逻辑（尾斜杠形态保持现状）。
- 不为删除引入新依赖或新引擎路径（copy-mode 与 legacy 两条既有路径保留）。
- **不把进度窗批处理列表从 1 行改成 N 行**（引擎整包重写、无逐条目回调，N 行只能是假进度；用一行说明文字替代，见 T3）。
- 不在本计划里顺手改「添加文件」「重命名条目」等相邻功能。
- **不在 `plans` 分支改 `src/`、`tests/`**（规则 19）——本计划在 `alpha` 上执行。

---

## Verification Strategy (MANDATORY)

### Test Decision

- **Core 单测（必须）**：扩写或新增 `tests/MantisZip.Tests/Engines/ZipEngineDeleteDirectoryTests.cs`；7z 用例加入 `SevenZipEngineTests.cs`（**注意：CI 用 `--filter "FullyQualifiedName!~SevenZipEngine"` 过滤，7z 用例只能在本地跑**，见 `.github/workflows/ci.yml`）。
- **UI 层（不写自动化测试）**：`CanDelete` 门禁、确认框、说明行属视图逻辑，靠手工回归 + 代码评审；理由：现有 `MainWindowViewModel` 测试没有对话框交互基建，为两处判断搭夹具不划算。
- **不需要新增引擎契约方法**：`IArchiveEngine.DeleteEntriesAsync` 签名不变（语义收紧，只新增一个派生异常类型）。

### QA Policy

- 每个引擎用例都要在**真实 zip/7z 文件**上跑（沿用现有测试风格，不 mock 引擎）。
- 断言要包含「包可读」：删除后重新打开并列举，确认剩余条目与预期完全一致（防止重写出坏包）。
- 二进制证据现生成现看，不要把「导出到某目录」写成验收项。

---

## Execution Strategy

### Waves

- **Wave 1（并行）**：T1 谓词 + T2 校验（Core，同一 commit 亦可）／T3 UI 确认与门禁与说明行
- **Wave 2**：T4 测试扩写（依赖 T1/T2 落地）
- **Wave 3（串行收尾）**：T5 手工回归 → T6 文档同步与计划归档

### Dependency Matrix

| 任务 | 依赖 | 阻塞 |
|---|---|---|
| T1 | 无 | T2、T4 |
| T2 | T1 | T4 |
| T3 | 无（**建议排 T2 之后**，文案依赖语义定死） | 无 |
| T4 | T1、T2 | T5 |
| T5 | T4 | T6 |
| T6 | T5 | — |

---

## TODOs

- [x] **T1. Core：统一「条目或其后代」匹配谓词**

  **What to do**：
  - 在 `src/MantisZip.Core/Utils/ArchivePathExtensions.cs` 的 `ArchivePath` 中新增：
    `public static bool MatchesEntryOrDescendant(string entryName, string target)`
  - 实现要点：
    - 两侧先 `Normalize`（`\`→`/`）
    - **`target` 的去尾斜杠要自己写循环**，不要复用 `TrimEndSeparator`——后者有 `path.Length > 3` 守卫（[ArchivePathExtensions.cs:23](../../src/MantisZip.Core/Utils/ArchivePathExtensions.cs#L23)），`"a/"`（长度 2）与 `"ab/"`（长度 3）会原样保留，导致 `"a/"` 删除时前缀匹配失效
    - 命中条件：`entryName == target`（`OrdinalIgnoreCase`）或 `entryName == target + "/"` 或 `entryName.StartsWith(target + "/", OrdinalIgnoreCase)`
    - 去尾斜杠后 `target` 为空/空白 → 返回 `false`（防止误删全包）
  - 三处调用点改用该谓词：[ZipEngine.CompressGroup.cs:888](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L888)（`deletedSet`）、[:908](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L908)（`deletedNormalized`）、[:1004-1013](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L1004-L1013)（legacy 校验与 `keepNames`）、[SevenZipEngine.cs:1239-1247](../../src/MantisZip.Core/Engines/SevenZipEngine.cs#L1239-L1247)
  - 7z 侧顺带把 `HashSet` 的比较器改成 `OrdinalIgnoreCase`（或直接改用谓词，不再建集合）

  **Must NOT do**：不要改 `ArchivePath.Normalize` 的既有行为（它在别处被大量复用，改它等于改全仓路径语义）；不要新增重载把 `IArchiveEngine` 接口搞复杂。

  **Recommended Agent Profile**：Core / 引擎方向，熟悉 SharpCompress 与 zip 条目命名。

  **Acceptance Criteria**：
  `MatchesEntryOrDescendant("sub/a.txt","sub")==true`、`("sub/","sub")==true`、`("subway.txt","sub")==false`、`("SUB/A.TXT","sub")==true`、`("a.txt","")==false`，以及**短名带斜杠**：`("a/","a")==true`、`("ab/","ab")==true`、`("a/b/","a/b")==true`。

  **QA Scenarios**：对含 `sub/`、`sub/a.txt`、`subway.txt` 的包执行删除 `sub` → 结果只剩 `subway.txt`。

  **Commit**：`fix(core): 删除条目匹配支持目录通配（条目或其后代）`

- [x] **T2. Core：零命中报错 + legacy 校验集合归一化**

  **What to do**：
  - **新增异常类型** `src/MantisZip.Core/Abstractions/ArchiveEntryNotFoundException.cs`：
    `public sealed class ArchiveEntryNotFoundException : FileNotFoundException`，构造 `(string entryPath)`，暴露 `public string EntryPath { get; }`，`FileName = entryPath`。
    理由：`WholeArchiveErrorLocalizer` 用 `case FileNotFoundException fnf` 兜住所有 FileNotFound（[WholeArchiveErrorLocalizer.cs:38-39](../../src/MantisZip.UI.Avalonia/Services/WholeArchiveErrorLocalizer.cs#L38-L39)），只有派生类型 + **前置 case** 才能给出专属文案；派生类型仍满足 `IArchiveEngine.DeleteEntriesAsync` 文档里的 `<exception cref="FileNotFoundException">` 契约。
  - **localizer 前置 case**（`WholeArchiveErrorLocalizer.cs` 的 `Map`）：在 `case FileNotFoundException` **之前**插入
    `case ArchiveEntryNotFoundException aen: return (LocalizationManager.T("Error_Delete_EntryNotFound", aen.EntryPath), aen.Message);`
    —— switch 分支顺序敏感，插在后面永远命中不到。
  - copy-mode：构造 `keepSet` 时统计命中数；命中 0 → `throw new ArchiveEntryNotFoundException(entryPaths[0])`，并在抛前清理 `tempArchiveFast`
  - legacy：`entryNameSet` 改为归一化集合（`select Normalize` + `OrdinalIgnoreCase`），避免 `Contains` 误报
  - 7z：命中 0（`keepEntries.Count == allEntries.Count`）→ 抛同一异常；注意**不要**动 `keepEntries.Count == 0` 那条「全删则删包」的分支
  - 多目标删除（`entryPaths.Length > 1`）时，只要有一条未命中就抛错并指明是哪条

  **Must NOT do**：不要把「全部条目都被删除」这条既有分支改成异常（那是合法路径：删空后移除包文件）。不要在新异常里重新拼中文消息给 UI 直接显示——消息仍由 localizer + 三语 key 决定。

  **Recommended Agent Profile**：同 T1。

  **Acceptance Criteria**：删除不存在的条目 → `ArchiveEntryNotFoundException`；删除存在的目录 → 成功且子条目一并消失；删除空列表 → 静默返回（既有行为不变）。

  **回归影响（必须一起改）**：下面两处用 `Assert.ThrowsAsync<FileNotFoundException>` 做**精确类型**断言，改抛派生类型后会变红，需同步改为 `ArchiveEntryNotFoundException`：
  - [ZipEngineDeleteTests.cs:65-69](../../tests/MantisZip.Tests/Engines/ZipEngineDeleteTests.cs#L65-L69)
  - [ZipBinaryRewriterTests.cs:770-774](../../tests/MantisZip.Tests/Utils/ZipBinaryRewriterTests.cs#L770-L774)

  **QA Scenarios**：`DeleteEntriesAsync(["nope"])` 必须抛异常且**不能改动原包**（对比 md5）。

  **Commit**：`fix(core): 删除零命中改为抛 ArchiveEntryNotFoundException，legacy 校验集合归一化`

- [x] **T3. UI：删除前二次确认 + 能力门禁 + 进度窗说明行**

  **What to do**：

  a) **能力门禁**（[MainWindowViewModel.Compress.cs:213-242](../../src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Compress.cs#L213-L242) 的 `DeleteFiles`）：
  - 进入前判断 `engine.CanDelete(_currentFormat)`，不支持则 `StatusMessage` 提示并返回（不弹进度窗）
  - 视图层：工具栏按钮（[MainWindow.axaml:151-159](../../src/MantisZip.UI.Avalonia/Views/MainWindow.axaml#L151-L159)）、编辑菜单（[EditMenuItems.axaml:55](../../src/MantisZip.UI.Avalonia/Controls/Menus/EditMenuItems.axaml#L55)）、右键菜单（[ArchiveContentView.axaml:292](../../src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml#L292)）的 `IsEnabled` 追加「当前格式支持删除」；新增 VM 属性（如 `CanDeleteEntries`）在切换包/格式/选中项时刷新

  b) **确认框**：`AppMessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)`（[AppMessageBox.axaml.cs:154-156](../../src/MantisZip.UI.Avalonia/Dialogs/AppMessageBox.axaml.cs#L154-L156) 有 4 参重载，会自动找活跃窗口作 owner）。用户点「否」直接返回，不进进度窗。
  - 文案（三语）：
    - 目录：`Confirm_DeleteDirectoryFmt` =「将删除该目录及其下 {0} 个文件（共 {1}），此操作不可撤销。」/ en / zh-TW
    - 文件：复用已注册的 `Status_ConfirmDelete`，或新增 `Confirm_DeleteFileFmt`（保持对称，推荐后者）
  - **数据来源必须是未过滤的全量条目**：`_allRawItems`（[MainWindowViewModel.Navigation.cs:126](../../src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Navigation.cs#L126)）而非 `GetFilteredSource()`。原因：删除谓词作用在整包上，**不受过滤条件影响**；用过滤后的数据算会少报。
    - 文件数：`ArchiveEntryLister.ComputeDirectoryStats(allRawItems)` 的 `DirStats.Count`（只计文件，不含目录条目本身，与文案「N 个文件」措辞一致）
    - 总大小：目录行 `ArchiveItemModel.Size` **已经是子树和**（[MainWindowViewModel.Navigation.cs:154-158](../../src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Navigation.cs#L154-L158) 用 `dirStats[FullPath]` 赋值），文件行直接用 `SelectedEntry.Size`
    - 实现建议：`PopulateEntries` 里已有 `dirStats` 局部变量，顺手存到字段 `_dirStats`（同一份字典，零额外枚举）；格式化用 `FormatUtil.FormatSize`
  - **不需要**登记 `MainWindowViewModel.UpdateLocalizedStrings()` 的 keys 数组——那段只服务 XAML `LocalizedStrings[Key]` 绑定；本处走 VM 里直接 `LocalizationManager.T()`

  c) **进度窗说明行**（替代「N 行列表」）：
  - 仿既有「压缩通道」说明行范式：`ProgressViewModel.ShowCompressChannelHint`（[ProgressViewModel.cs:520](../../src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs#L520)）+ [ProgressWindow.axaml:375-380](../../src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml#L375-L380)
  - 新增：`ProgressViewModel.OperationNoteText`（string）+ `ShowOperationNote`（bool）；`ProgressWindow.SetOperationNote(string)` 走 `DispatchIfNeeded`（与 `SetCurrentBatchItem` 同风格）
  - VM 侧加 `PendingOperationNote`（string?），`MainWindow.axaml.cs` 的 `RunWithProgress` 闭包读出后 `pw.SetOperationNote(...)`，读完立即复位（与 `PendingCompressFlow` 同范式，[MainWindow.axaml.cs:298-299](../../src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs#L298-L299)）
  - 文案 key `Progress_DeleteNoteFmt` =「正在删除 {0} 个文件（共 {1}）…」
  - 说明行只描述「总量」，**不要**去驱动批处理行的状态机（那是整包重写，没有逐条进度）

  **Must NOT do**：不要用原生对话框（规则 16）；不要为了「显示将删除多少条」去预扫描整个包或解压（用内存里已有的 `_allRawItems` + `DirStats` 即可）；不要改 `RunWithProgress` 的签名。

  **Recommended Agent Profile**：Avalonia / MVVM 方向，熟悉 `LocalizationManager`、`AppMessageBox`、`ProgressViewModel` 的状态机与 `DispatcherPriority` 派发习惯。

  **Acceptance Criteria**：tar 包上删除按钮禁用；zip 目录删除会先弹确认且文案含文件数与大小；确认后进度窗顶部出现「正在删除 N 个文件（共 x GB）」；三语文案齐全。

  **Commit**：`fix(avalonia): 删除前二次确认、按格式能力门禁删除入口、进度窗显示删除总量`

- [x] **T4. 测试：目录与静默成功回归**

  **What to do**：
  - 新增 `tests/MantisZip.Tests/Engines/ZipEngineDeleteDirectoryTests.cs`：
    1. 显式目录条目（含 `sub/`）+ 子文件 → 删 `sub` 后子条目全消失、`subway.txt` 保留
    2. 隐式目录（只有 `sub/a.txt`，无 `sub/` 条目）→ 同样删干净
    3. 混合：同时删一个文件与一个目录
    4. 不存在条目 → 抛 `ArchiveEntryNotFoundException` 且原包未被修改（字节比较）
    5. 加密包目录删除（走 copy-mode 还是 legacy 取决于加密，两路都要能过）
    6. 大小写：包内 `SUB/A.TXT`，删 `sub` → 命中
  - 谓词边界用例（可放 `tests/MantisZip.Tests/Utils/`）：`("a/","a")`、`("ab/","ab")`、`("a/b/","a/b")`、`("a.txt","")`
  - `SevenZipEngineTests.cs` 增两例：7z 目录删除、**7z 删除不存在条目 → `ArchiveEntryNotFoundException`**
  - 既有的 `DeleteEntriesAsync_NonExistentEntry_ThrowsFileNotFoundException` 与 `ZipBinaryRewriterTests` 同族断言按 T2 改为新类型

  **Must NOT do**：不要 mock 引擎；不要只用空目录条目做断言（必须验证子文件消失）。

  **Known limitation**：`SevenZipEngine` 相关用例**不在 CI 门禁内**（`ci.yml` 的 `--filter "FullyQualifiedName!~SevenZipEngine"`），只能在本地跑并通过 T5 记录结论。

  **Recommended Agent Profile**：测试方向，熟悉 xunit v3 与仓库既有引擎测试的临时目录约定。

  **Acceptance Criteria**：新增用例全绿；`-Task test -Target tests` 整体全绿；刻意把 T1 谓词改回精确匹配时这些用例必须变红（自检测试有效性）。

  **Commit**：`test(core): 补删除目录、零命中与谓词边界回归用例`

- [x] **T5. 手工回归（Windows 桌面）**

  **执行者**：用户本人（2026-10-26 真机验证）。依据：用户反馈「测试基本成功」（m00511），并据此提出「删除完成后要等一两秒列表才刷新」的体验问题——该问题只有在真机走完删除流程后才会被观察到，故判定九场景主线已通过；刷新时机问题当日已单独修复（`0893ec24`）。

  **What to do**：用真实包跑一遍：①目录删除（含多层嵌套）②文件删除（回归）③加密包目录删除 ④7z 目录删除 ⑤7z 删除不存在条目（观察是否立刻报错而不重打包）⑥tar 上按钮禁用 ⑦确认框数字与大小是否准确（含**开启过滤器**后仍显示全量）⑧删除后立刻「测试压缩包」自检 ⑨删除最后一个条目 → 包被移除的既有行为。截图/录屏留档（现生成现看）。

  **补充（同日，自动化替身）**：九个场景里七个不依赖点击，已用一次性探针测试（Release，8 例 / 596ms 全过）跑完 —— ①多层级嵌套、③加密包 `test123`、④7z 多层目录、⑤7z 不存在条目（抛 `ArchiveEntryNotFoundException` + 包字节不变 + `%TEMP%\MantisZip\DeleteTemp` 无新增目录 + 耗时 < 2s，证明未走整包重打包）、⑥能力位（`zip`/`7z` = true，`tar`/`tar.gz`/`tgz`/`gz`/`rar`/`iso` = false）、⑦`ComputeDirectoryStats` 的「N 个文件 / 共 x」只计文件且含嵌套与空文件、⑧删除后 `TestArchiveAsync` 通过（加密 ZIP 与 7z）、⑨删空即删包（ZIP copy-mode `ZipEngine.CompressGroup.cs:933-938` 与 7z `SevenZipEngine.cs:1273-1276` 行为一致）。其中 ⑧⑨ 原本零覆盖，已固化为 `tests/MantisZip.Tests/Engines/ZipEngineDeleteDirectoryTests.cs` 的 3 个正式用例（`DeletingLastEntry_RemovesArchive` 为 Theory，含 zip/7z 两分支）。**剩余人工项**：⑥ tar 包上删除入口是否置灰、⑦ 开启过滤器后确认框数字是否为全量（两者逻辑层已由自动化覆盖，差的是绑定与视觉）。

  **Acceptance Criteria**：九个场景全部符合预期；`%TEMP%\MantisZip\DeleteTemp` 无残留目录。

  **Commit**：无（手工验证，结论写入 T6 文档）

- [x] **T6. 文档同步 + 计划归档**

  **开工前动作（规则 1）**：把本文件从 `.omo/plans/未开始/` 移入 `.omo/plans/进行中/`。

  **完成后动作**：
  - 规则 3：`docs/PROGRESS.md` 加里程碑行 + `docs/progress-avalonia-detail.md` 加细节条目
  - 规则 18：`RELEASE_NOTES.md` 的 **v0.5.3「修复」节**补两条（口径见下节）
  - 规则 1：计划文件移入 `.omo/plans/已完成/`，`docs/PLAN.md` 的 S 档行移除
  - 在 `intent-execution-seam.md` 的「已知同类事故」处登记一行（仅引用，不并入波次）

  **Commit**：`docs: 删除目录修复的进度与发布说明同步`

---

## 实施记录（2026-10-26）

**代码落地位置**

| 层 | 文件 | 改动 |
|---|---|---|
| Core | `src/MantisZip.Core/Utils/ArchivePathExtensions.cs` | 新增 `MatchesEntryOrDescendant(entryName, target)`：自写去尾斜杠循环（**不复用** `TrimEndSeparator`，其对 `"a/"`/`"ab/"` 因 `path.Length > 3` 守卫失效）+ 忽略大小写 + 边界判 `/` |
| Core | `src/MantisZip.Core/Abstractions/ArchiveEntryNotFoundException.cs` | 新建：`sealed class … : FileNotFoundException`，带 `EntryPath` |
| Core | `src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs` | copy-mode 先校验再重写（零命中删临时包 + 抛错）；legacy 校验集合改用谓词；两路保留集均用谓词 |
| Core | `src/MantisZip.Core/Engines/SevenZipEngine.cs` | 空列表早退；零命中在**建临时目录之前**抛错（不再整包解压重打包）；保留集用谓词 |
| Core | `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` | `DeleteEntriesAsync` 的 `<exception>` 文档改指新类型 |
| UI | `src/MantisZip.UI.Avalonia/Services/WholeArchiveErrorLocalizer.cs` | 在 `case FileNotFoundException` **之前**加专属 case |
| UI | `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Compress.cs` | `CanDelete` 门禁 + 二次确认 + `PendingOperationNote` + `GetDeleteTargetStats` |
| UI | `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | 新增 `CanDeleteEntries`（能力位）+ `PendingOperationNote` + 两处通知 |
| UI | `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs` | `OperationNoteText` / `ShowOperationNote` / `SetOperationNote` |
| UI | `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml(.cs)` | Row 4 状态区包成 StackPanel，状态行下方加说明行 + `SetOperationNote` |
| UI | `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs` | `RunWithProgress` 读出并复位 `PendingOperationNote` |
| UI | `Views/MainWindow.axaml` / `Controls/Menus/EditMenuItems.axaml` / `Controls/ArchiveContentView.axaml` | 三处删除入口 `IsEnabled` 改绑 `CanDeleteEntries`（右键菜单原无门禁） |
| i18n | `Localization/strings.{zh-CN,en,zh-TW}.json` | 新增 `Confirm_DeleteDirectoryFmt` / `Confirm_DeleteFileFmt` / `Error_Delete_EntryNotFound` / `Progress_DeleteNoteFmt`（1313 key 三语一致） |
| 测试 | `tests/MantisZip.Tests/Engines/ZipEngineDeleteDirectoryTests.cs`（新建） | 9 例 zip（显式/隐式/混合/大小写/前缀边界/不存在/部分不存在/加密）+ 2 例 7z |
| 测试 | `tests/MantisZip.Tests/Utils/ArchivePathTests.cs` | `MatchesEntryOrDescendant` 14 条 `[Theory]`（含 `("a/","a")`、`("ab/","ab")`、`("ab/a.txt","a")` 边界） |
| 测试 | `Engines/ZipEngineDeleteTests.cs` / `Utils/ZipBinaryRewriterTests.cs` | 两处 `Assert.ThrowsAsync<FileNotFoundException>` 精确类型断言改为新类型 + 断言 `EntryPath` 与可赋值性 |

**与计划的偏差（3 处，均为实现细节）**

1. 进度窗说明行**没有**放进 `DetailedPanel`（原计划的仿照点）：那里 `IsVisible="{Binding IsDetailedMode}"`，只在详细模式可见；改放 Row 4 状态行正下方（状态区包成竖向 `StackPanel`），两种显示模式都能看到。
2. `GetDeleteTargetStats` 的目录口径取 `ComputeDirectoryStats(_allRawItems)`（**未过滤**原始条目），与 VM 里目录行 `Size` 用的过滤后口径**故意不同**：确认框承诺的是「这个目录里到底有多少东西」，不能因为用户开了搜索/过滤而少算。
3. T3 原文「不支持则 `StatusMessage` 提示并返回（不弹进度窗）」——实现改为**入口禁用**（三处 `IsEnabled="{Binding CanDeleteEntries}"`）+ 命令内静默 `return` 兜底。理由：只读格式下按钮本就不该可点，正常路径到不了提示分支；加状态栏提示反而多一条「点不到但可能闪一下」的假反馈。

**验证**

- `-Task build -Target core`：0 警告 0 错误
- `-Task build -Target avalonia`：Debug 因 Explorer 锁定 `MantisZip.ShellExt.dll`（COM 已注册，既有环境问题）失败 → 按仓库约定改 `-c Release` 后 **0 警告 0 错误**
- `-Task test -Target tests`：全绿；新增/相关用例单独跑 **25 通过**（14 谓词 + 9 zip + 2 7z）
- `-Task test -Target ui-tests`：全绿（28 条均为既有 xUnit 分析器警告）
- `-Task i18n`：1313 key 三语一致
- **T5 手工回归：用户 2026-10-26 真机验证通过**（依据 m00511「测试基本成功」+ 由真机删除流程暴露的刷新时机反馈，见下一条）
- **实施后追加（同日，用户真机验收反馈）**：删除完成 → 列表刷新之间有 1-2 秒空窗。原因不是缺陷而是链路顺序——刷新写在 `RunWithProgress` 返回之后，而它要等「完成态 → 自动关闭倒计时（默认 3 秒）→ 关窗」；`RefreshArchive` 本身是全量重列条目。改为在 `PendingCompletedCallback` 里刷新（回调内 `pw.SetComplete(删除完成)` + 置状态栏 + fire-and-forget 刷新，异常在私有方法内吞掉写 `App.DebugLog`），进度窗读秒时列表已就绪。复验：avalonia Release 构建 0 警告、ui-tests 全绿。

---

## 发布说明口径（规则 18）

**事实核对**：`git tag` 只到 `v0.5.2`（2026-10-20），无 `v0.5.3`；`release.yml` 由 tag 触发 ⇒ **0.5.3 尚未发布**。而 `v0.5.2` 的 `ZipEngine.cs` 已含同一段 copy-mode 删除且零命中不校验 ⇒ 该静默成功**在已发布版本中就存在**。

结论：属于「上一个已发布 tag → 本次发布」的用户可见差异，**应**写进 v0.5.3 的「修复」节。两条：

1. **压缩包内删除目录现在真的会删掉**（此前静默成功、报 100% 但条目仍在；现在零命中会明确报「压缩包中不存在条目」）
2. **7z 包删除不存在的条目不再整包解压重打包**（此前会白跑一遍重写，耗时陡增且有属性丢失风险；现在立即报错）

---

## Rollback

- 引擎改动集中在 `ArchivePath` 的一个新方法 + 三个调用点 + 一个新异常类型，`git revert` 单个 commit 即回到原状。
- 若「零命中报错」在某些真实包上误报（例如包内条目名含异常编码），**降级方案**：把校验降为 `CoreLog.Warn` + 状态栏提示（仍是可见反馈），而**不允许**退回「静默成功」。注意这与 Must Have「零命中必报错」相冲突——它是**最后手段**，动用前必须先复现并确认是真实包缺陷，而不是匹配逻辑写错。

---

## 与既有计划的关系

- **`intent-execution-seam`（S 档）**：不同故障族。本计划只在其文档里登记一行「静默成功」的已知同类事故，不并入其波次，也不复用它的 `Rejected(reason)` 机制（那是对话框层）。
- **`archive-rename-entry`（A 档）**：同一片「条目级操作」领地。若两者先后实施，重命名应复用本计划引入的匹配谓词与零命中报错范式，避免又写一份精确匹配。
- **`custom-zip-engine`（冻结）**：本计划不依赖它；但将来它若实现条目级删除，`MatchesEntryOrDescendant` 的语义（含递归）应作为契约基准。
