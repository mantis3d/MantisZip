# 压缩包内删除目录无效（静默成功）——递归删除 + 命中校验

## TL;DR

**Quick Summary**：用户实测「压缩包内删除单个文件有效，删除目录无效」。根因不是对话框接缝，而是引擎的删除匹配对目录不成立：UI 传的目录路径不带尾斜杠、包内条目带尾斜杠、匹配用精确相等，于是**一条都命中不了**；copy-mode 快路径又不校验存在性，所以零命中也当成功，报 100% 并显示「删除完成」。本计划把删除语义统一为「精确条目 或 该目录下的所有条目」，并在零命中时报错，同时补上二次确认与格式能力门禁。

**Deliverables**：

- Core：统一的「条目或其后代」匹配谓词，Zip（copy-mode + legacy）与 7z 三处调用点共用
- Core：删除零命中 → `FileNotFoundException`（消灭静默成功）
- UI：删除前二次确认（含条目数量）、`CanDelete` 能力门禁（TAR/GZ）
- 测试：目录递归删除（显式目录条目 / 隐式目录两种构包）、混合、不存在、加密、7z 各一例
- 文档：`docs/PLAN.md` 行、`docs/PROGRESS.md` + 细节文档、`RELEASE_NOTES.md`（按规则 18 确认范围）

**Estimated Effort**：4-6h（Core 谓词与校验 ~1.5h、UI ~1h、测试 ~1.5-2h、文档与手工回归 ~1h）

**Parallel Execution**：T1/T2（Core）与 T3（UI 门禁与确认框）可并行；T4 依赖 T1/T2 落地；T5/T6 串行收尾。

**Critical Path**：`T1 → T2 → T4 → T5 → T6`

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

- **7z 同样中招，且更糟**：[SevenZipEngine.cs:1239-1247](../../src/MantisZip.Core/Engines/SevenZipEngine.cs#L1239-L1247) 用**大小写敏感** `HashSet`（Zip 侧是 `OrdinalIgnoreCase`），同样无存在性校验，`keepEntries.Count == 0` 时直接删包（[:1250-1256](../../src/MantisZip.Core/Engines/SevenZipEngine.cs#L1250-L1256)）。
- **legacy 校验集合本身也没归一化**：`entryNameSet` 由原始 `entry.Key` 构建、却用 `normalized` 去 `Contains`（[:1004-1013](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L1004-L1013)）⇒ 包内条目名含 `\` 或尾斜杠差异时会误报「条目不存在」。
- **TAR/GZ 直接抛 `NotSupportedException`**（[TarGzEngine.cs:574-586](../../src/MantisZip.Core/Engines/TarGzEngine.cs#L574-L586)），而 `CanDelete`（[ZipEngine.cs:30](../../src/MantisZip.Core/Engines/ZipEngine.cs#L30)、[SevenZipEngine.cs:422](../../src/MantisZip.Core/Engines/SevenZipEngine.cs#L422)）在 UI 里**一处未用** ⇒ 删除按钮在 tar 上照样可点，点了才报错。
- **删除无二次确认**：`Status_ConfirmDelete` 已注册文案（[MainWindowViewModel.cs:274](../../src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs#L274)）但无人使用，而删除动作是**整包重写**（copy-mode 重写全部保留条目，legacy 解压再重压），误删不可逆。

#### 入口与现有覆盖

- 三个入口都指向同一个命令：工具栏（[MainWindow.axaml:151-159](../../src/MantisZip.UI.Avalonia/Views/MainWindow.axaml#L151-L159)，`IsEnabled="{Binding IsArchiveLoaded}"`）、编辑菜单（[EditMenuItems.axaml:55](../../src/MantisZip.UI.Avalonia/Controls/Menus/EditMenuItems.axaml#L55)）、列表右键菜单（[ArchiveContentView.axaml:292](../../src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml#L292)）。
- 命令体：[MainWindowViewModel.Compress.cs:213-242](../../src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Compress.cs#L213-L242)，`entryPath = SelectedEntry.FullPath ?? SelectedEntry.Name`，只传**一条**路径给 `RunWithProgress` ⇒ 进度窗口的批处理列表永远只有 1 行（即使实际删 300 个条目）。
- 现有测试 [ZipEngineDeleteTests.cs](../../tests/MantisZip.Tests/Engines/ZipEngineDeleteTests.cs) 10 例全绿（实测 274ms），但**没有一例针对目录**——这就是它没被拦住的原因。

---

## Work Objectives

### Core Objective

让「删除压缩包内的目录」会连带删除其下所有条目；任何**没有实际删除任何条目**的删除调用都必须以异常结束，而不是返回成功。

### Concrete Deliverables

1. `ArchivePath`（或 `ArchiveEntryLister`）新增统一谓词：条目名 == 目标、或 == 目标 + `/`、或 `StartsWith(目标 + "/")`（大小写不敏感，两侧先 `Normalize` 且目标去尾斜杠）。
2. Zip copy-mode（`keepSet`）、Zip legacy（`keepNames` + 存在性校验）、7z（`deletedSet`）三处改用该谓词。
3. copy-mode 与 7z：命中条目数为 0 → `FileNotFoundException`，消息沿用 legacy 的中文文案。
4. legacy 的 `entryNameSet` 修正为归一化集合（消除误报）。
5. UI：删除前弹确认（复用 `Status_ConfirmDelete`，显示将被删除的条目数）；`engine.CanDelete(_currentFormat)` 为 false 时按钮/菜单项禁用。
6. 测试：目录递归、隐式目录、混合、不存在、加密、7z、大小写差异各一例。

### Definition of Done

- `pwsh -File scripts\verify-repo.ps1 -Task build -Target core` 与 `-Target avalonia` 均 0 警告、退出码 0。
- `-Task test -Target tests` 全绿，且新增用例覆盖上面 7 种场景。
- 手工回归：对含 `sub/a.txt`、`sub/b.txt`、`root.txt` 的 zip 删除 `sub` → 列表里 `sub` 消失、包内只剩 `root.txt`、包可通过「测试压缩包」自检；对 tar 点删除 → 按钮禁用（或明确提示），不再走到异常。
- `verify-repo -Task check` 三项全绿（版本号 / 三语 key / 守卫）。

### Must Have

- 目录删除的**递归语义**（用户确认）。
- **零命中必报错**，且报错信息包含用户看到的路径。
- Zip 与 7z **同源实现**（同一个谓词，不允许各写一份）。
- 大小写不敏感的匹配（沿用 Zip 侧现状，7z 一并对齐）。

### Must NOT Have

- 不做「删除目录条目但保留子条目」的部分语义。
- 不改压缩/解压流程、不改 `ArchiveEntryLister` 的目录展示逻辑（尾斜杠形态保持现状）。
- 不为删除引入新依赖或新引擎路径（copy-mode 与 legacy 两条既有路径保留）。
- 不在本计划里顺手改「添加文件」「重命名条目」等相邻功能。
- 不在 `plans` 分支改 `src/`、`tests/`（规则 19）。

---

## Verification Strategy (MANDATORY)

### Test Decision

- **Core 单测（必须）**：`tests/MantisZip.Tests/Engines/ZipEngineDeleteTests.cs` 扩写或新增 `ZipEngineDeleteDirectoryTests.cs`；7z 用例加入 `SevenZipEngineTests.cs` 同族分区。
- **UI 层（不写自动化测试）**：`CanDelete` 门禁与确认框属视图逻辑，靠手工回归 + 代码评审；理由：现有 `MainWindowViewModel` 测试没有对话框交互基建，为两处判断搭夹具不划算。
- **不需要新增引擎契约**：`IArchiveEngine.DeleteEntriesAsync` 签名不变（语义收紧，不新增 API）。

### QA Policy

- 每个引擎用例都要在**真实 zip/7z 文件**上跑（沿用现有测试风格，不 mock 引擎）。
- 断言要包含「包可读」：删除后重新打开并列举，确认剩余条目与预期完全一致（防止重写出坏包）。
- 二进制证据现生成现看，不要把「导出到某目录」写成验收项。

---

## Execution Strategy

### Waves

- **Wave 1（并行）**：T1 谓词 + T2 校验（Core，同一 commit 亦可）／T3 UI 确认与门禁
- **Wave 2**：T4 测试扩写（依赖 T1/T2 落地）
- **Wave 3（串行收尾）**：T5 手工回归 → T6 文档同步与计划归档

### Dependency Matrix

| 任务 | 依赖 | 阻塞 |
|---|---|---|
| T1 | 无 | T2、T4 |
| T2 | T1 | T4 |
| T3 | 无 | 无（可与 T1/T2 并行） |
| T4 | T1、T2 | T5 |
| T5 | T4 | T6 |
| T6 | T5 | — |

---

## TODOs

- [ ] **T1. Core：统一「条目或其后代」匹配谓词**

  **What to do**：
  - 在 `src/MantisZip.Core/Utils/ArchivePathExtensions.cs` 的 `ArchivePath` 中新增：
    `public static bool MatchesEntryOrDescendant(string entryName, string target)`，实现要点：
    - 两侧先 `Normalize`（`\`→`/`），`target` 再用 `TrimEndSeparator` 去尾斜杠（注意 `TrimEndSeparator` 对长度 ≤3 的路径保留原状，对条目路径够用但要验证 `"a"` 这种单段名不被误伤）
    - 命中条件：`entryName == target`（大小写不敏感）或 `entryName == target + "/"` 或 `entryName.StartsWith(target + "/")`
    - `target` 为空/空白时返回 false（防止误删全包）
  - 三处调用点改用该谓词：[ZipEngine.CompressGroup.cs:888](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L888)（`deletedSet`）、[:908](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L908)（`deletedNormalized`）、[:1004-1013](../../src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs#L1004-L1013)（legacy 校验与 `keepNames`）、[SevenZipEngine.cs:1239-1247](../../src/MantisZip.Core/Engines/SevenZipEngine.cs#L1239-L1247)
  - 7z 侧顺带把 `HashSet` 的比较器改成 `OrdinalIgnoreCase`（或直接改用谓词，不再建集合）

  **Must NOT do**：不要改 `ArchivePath.Normalize` 的既有行为（它在别处被大量复用，改它等于改全仓路径语义）；不要新增重载把 `IArchiveEngine` 接口搞复杂。

  **Recommended Agent Profile**：Core / 引擎方向，熟悉 SharpCompress 与 zip 条目命名。

  **Acceptance Criteria**：`MatchesEntryOrDescendant("sub/a.txt","sub")==true`、`("sub/","sub")==true`、`("subway.txt","sub")==false`、`("SUB/A.TXT","sub")==true`、`("a.txt","")==false`。

  **QA Scenarios**：对含 `sub/`、`sub/a.txt`、`subway.txt` 的包执行删除 `sub` → 结果只剩 `subway.txt`。

  **Commit**：`fix(core): 删除条目匹配支持目录通配（条目或其后代）`

- [ ] **T2. Core：零命中报错 + legacy 校验集合归一化**

  **What to do**：
  - copy-mode：构造 `keepSet` 时统计命中数；命中 0 → `throw new FileNotFoundException($"压缩包中不存在条目: {entryPaths[0]}", entryPaths[0])`（与 legacy 文案一致），并在抛前清理 `tempArchiveFast`
  - legacy：`entryNameSet` 改为归一化集合（`select Normalize` + `OrdinalIgnoreCase`），避免 `Contains` 误报
  - 7z：命中 0（`keepEntries.Count == allEntries.Count`）→ 同样抛 `FileNotFoundException`；注意**不要**动 `keepEntries.Count == 0` 那条「全删则删包」的分支
  - 在多目标删除（`entryPaths.Length > 1`）时，只要有一条未命中就抛错并指明是哪条

  **Must NOT do**：不要把「全部条目都被删除」这条既有分支改成异常（那是合法路径：删空后移除包文件）。

  **Recommended Agent Profile**：同 T1。

  **Acceptance Criteria**：删除不存在的条目 → `FileNotFoundException`；删除存在的目录 → 成功且子条目一并消失；删除空列表 → 静默返回（既有行为不变）。

  **QA Scenarios**：`DeleteEntriesAsync(["nope"])` 必须抛异常且**不能改动原包**（对比 md5）。

  **Commit**：`fix(core): 删除零命中改为报错，legacy 校验集合归一化`

- [ ] **T3. UI：删除前二次确认 + 能力门禁**

  **What to do**：
  - [MainWindowViewModel.Compress.cs:213-242](../../src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Compress.cs#L213-L242) 的 `DeleteFiles`：
    - 进入前判断 `engine.CanDelete(_currentFormat)`，不支持则 `StatusMessage` 提示并返回（不弹进度窗）
    - 用已注册的 `Status_ConfirmDelete` 文案弹确认框，消息里带上「将删除该目录及其下所有条目」（目录时）
  - 视图层门禁：工具栏按钮（[MainWindow.axaml:151-159](../../src/MantisZip.UI.Avalonia/Views/MainWindow.axaml#L151-L159)）、编辑菜单（[EditMenuItems.axaml:55](../../src/MantisZip.UI.Avalonia/Controls/Menus/EditMenuItems.axaml#L55)）、右键菜单（[ArchiveContentView.axaml:292](../../src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml#L292)）的 `IsEnabled` 增加「当前格式支持删除」
  - 新增 VM 属性（如 `CanDeleteEntries`）在切换包/格式/选中项时刷新；新增 i18n key 三语成对添加（规则 13），并登记到 `UpdateLocalizedStrings()` 的 keys 数组

  **Must NOT do**：不要用原生对话框；不要为了「显示将删除多少条」去预扫描整个包（大包会卡；目录数量用 `ComputeDirectoryStats` 已有的聚合数即可，或干脆只显示路径）。

  **Recommended Agent Profile**：Avalonia / MVVM 方向，熟悉 `LocalizationManager` 与 `CustomFilePickerDialog` 之外的 `ConfirmDialog` 族。

  **Acceptance Criteria**：tar 包上删除按钮禁用；zip 目录删除会先确认再执行；确认文案三语齐全。

  **Commit**：`fix(avalonia): 删除前二次确认并按格式能力门禁删除入口`

- [ ] **T4. 测试：目录与静默成功回归**

  **What to do**：
  - 新增 `tests/MantisZip.Tests/Engines/ZipEngineDeleteDirectoryTests.cs`：
    1. 显式目录条目（含 `sub/`）+ 子文件 → 删 `sub` 后子条目全消失、`subway.txt` 保留
    2. 隐式目录（只有 `sub/a.txt`，无 `sub/` 条目）→ 同样删干净
    3. 混合：同时删一个文件与一个目录
    4. 不存在条目 → 抛 `FileNotFoundException` 且原包未被修改（字节比较）
    5. 加密包目录删除（走 copy-mode 还是 legacy 取决于加密，两路都要能过）
    6. 大小写：包内 `SUB/A.TXT`，删 `sub` → 命中
  - `SevenZipEngineTests.cs` 增一例 7z 目录删除
  - 既有的 `DeleteEntriesAsync_NonExistentEntry_ThrowsFileNotFoundException` 要确认仍然通过（它走的是 legacy 还是 copy-mode 需实测，若走 copy-mode 则由 T2 兜住）

  **Must NOT do**：不要 mock 引擎；不要只用空目录条目做断言（必须验证子文件消失）。

  **Recommended Agent Profile**：测试方向，熟悉 xunit v3 与仓库既有引擎测试的临时目录约定。

  **Acceptance Criteria**：新增用例全绿；`-Task test -Target tests` 整体全绿；刻意把 T1 谓词改回精确匹配时这些用例必须变红（自检测试有效性）。

  **Commit**：`test(core): 补删除目录与零命中回归用例`

- [ ] **T5. 手工回归（Windows 桌面）**

  **What to do**：用真实包跑一遍：①目录删除（含多层嵌套）②文件删除（回归）③加密包目录删除 ④7z 目录删除 ⑤tar 上按钮禁用 ⑥删除后立刻「测试压缩包」自检 ⑦删除最后一个条目 → 包被移除的既有行为。截图/录屏留档（现生成现看）。

  **Acceptance Criteria**：七个场景全部符合预期；`%TEMP%\MantisZip\DeleteTemp` 无残留目录。

  **Commit**：无（手工验证，结论写入 T6 文档）

- [ ] **T6. 文档同步 + 计划归档**

  **What to do**：
  - 规则 3：`docs/PROGRESS.md` 加里程碑行 + `docs/progress-avalonia-detail.md` 加细节条目
  - 规则 18：向用户确认 `RELEASE_NOTES.md` 是否收录（判据：0.5.3 已发布 → 该 bug 在已发布版本中存在，属用户可见差异，**应**收；若本次修复随 0.5.4 发布则写在 0.5.4 的「修复」节）
  - 规则 1：计划文件移入 `.omo/plans/已完成/`，`docs/PLAN.md` 行移除
  - 在 `intent-execution-seam.md` 的「已知同类事故」处登记一行（仅引用，不并入波次）

  **Commit**：`docs: 删除目录修复的进度与发布说明同步`

---

## Rollback

- 引擎改动集中在 `ArchivePath` 的一个新方法与三个调用点，`git revert` 单个 commit 即回到原状。
- 若「零命中报错」在某些真实包上误报（例如包内条目名含异常编码），降级方案：把校验降为 `CoreLog.Warn` + 状态栏提示，而不是吞掉——但**不允许**退回「静默成功」。

---

## 与既有计划的关系

- **`intent-execution-seam`（S 档）**：不同故障族。本计划只在其文档里登记一行「静默成功」的已知同类事故，不并入其波次，也不复用它的 `Rejected(reason)` 机制（那是对话框层）。
- **`archive-rename-entry`（A 档）**：同一片「条目级操作」领地。若两者先后实施，重命名应复用本计划引入的匹配谓词与零命中报错范式，避免又写一份精确匹配。
- **`custom-zip-engine`（冻结）**：本计划不依赖它；但将来它若实现条目级删除，`MatchesEntryOrDescendant` 的语义（含递归）应作为契约基准。
