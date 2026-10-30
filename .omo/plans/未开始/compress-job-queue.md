# Work Plan: 压缩任务队列（TeraCopy 式串行队列 + 常驻队列窗）

> **Status**: 📋 待开始 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜] (0/12)
> **预估工时**: 🔴高 **46-66h**（P0+P1 ≈ 12-18h 就能独立发版，P3 是高风险大头）
> **档位**: **B 档**（有价值、排队不排期）；但 **P0 建议视作插队项**——它同时修掉 issue #66 那类「按钮点一次就永久灰」
> **关联规则**: 规则 13（新增文案三语同步）、规则 20（意图-执行接缝：入队只能用设置窗返回的 `ConfirmOutcome<CompressSettings>`，不许弹窗后回读）、规则 3 / 18（进度文档与发布说明）、规则 21（本轮不做的项登记 `KNOWN_ISSUES.md`）、规则 4 / 7 / 14（新增窗口的主题样式、行高与中文注释）
> **关联 issue**: #66（新建压缩包按钮永久灰）、#55（多选解压开多窗，已修 CLI 侧，本计划覆盖 GUI 侧）

## Problem

现在的「压缩」是一次性阻塞调用：点按钮 → 弹设置窗 → **就地跑完** → 进度窗自动关 → 按钮才恢复。这个形状带来四个已发生或可预期的问题。

### 一、按钮灰化 = 「这一轮跑完了吗」

`src/MantisZip.UI.Avalonia/Views/MainWindow.axaml:97` 的按钮只绑 `Command="{Binding NewArchiveCommand}"`，没有 `IsEnabled`；灰化完全来自 `AsyncRelayCommand` 的「执行中禁用」（CommunityToolkit.Mvvm 8.4.2，默认 `AllowConcurrentExecutions = false`）。而 `NewArchive`（`src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Compress.cs:25-32`）await 的是**整轮压缩**（设置窗 + 进度窗 + 收尾）。于是：

- 只要有一个 `await` 没回来，按钮就永久灰，只有重启能恢复 —— 这正是 issue #66「用一次就灰化」。
- 目前的疑因（未复现确认）：`src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs:420-452` 的 `WaitForManualCloseAsync` 存在 TOCTOU —— `if (!IsVisible) return Closed;` 之后才 `Closed += onClosed;`，窗口若在这两步之间被关掉（取消收尾的 `window.Close()`、自动关窗、密码取消收尾都可能同时在跑），信号量永远不会被 Set，`await Task.Run(() => gate.Wait())` 永不返回。
- 2026-10-29 探针实测（临时文件，已删除）：命令委托**抛异常**不会卡禁用（`CanExecute` 已复位）；委托**永不完成**则 `CanExecute` 永久 false。⇒ 灰化 = 「任务没完成」，不是异常、不是模态禁用。

### 二、想再压一次只能等

「忙就拒绝」对用户就是「点了没反应/按钮是灰的」。同一时刻只能有一个压缩任务，第二个包的配置窗都打不开。

### 三、多选 / 多入口各开一个进程

右键菜单多选 N 个包 → N 个进程 → N 个设置窗（#55 的 CLI 侧已在 `App.CliInfra.RunWithPathCollector` 收口，GUI 侧没有对应机制；GUI 连单实例互斥量都没有）。

### 四、并发的真实摩擦点（不是假设）

| # | 共享物 | 现状 | 后果 |
|---|---|---|---|
| 1 | `ProgressWindow.CurrentVisible`（`Dialogs/ProgressWindow.axaml.cs:50` 静态单例） | 只有一个「当前进度窗」 | 两个任务同时在跑会互相覆盖，冲突/密码弹窗 owner 取错 |
| 2 | `MainWindowProgressPresenter.ActiveWindow` | 同上，单实例 | 后启动的一轮把前一轮的窗口引用顶掉 |
| 3 | 设置 / 密码库 / 最近文件 / 路径历史 / 窗口状态 JSON | 进程内缓存 + 原子写，**无跨进程锁** | 两个进程后写覆盖先写（原子写只防写坏，不防丢更新） |
| 4 | 输出 `<包名>.zip.tmp`（`src/MantisZip.Core/Utils/ZipBinaryRewriter.cs:329`） | 固定后缀 | 两个进程压同一路径会互相踩 |
| 5 | 引擎执行线程 | `await` 续体回到 UI 线程（`ZipEngine.Extract.cs` 全文只有 2 处 `ConfigureAwait(false)`） | 任何「阻塞式等待」都会冻界面 —— 2026-10-26「真暂停」就是因此回滚 |

## Objective

1. **点「开始压缩」即入队，按钮立刻恢复**，同一时刻只跑一个任务（串行），队列里排多少都能看得到。
2. **失败不阻断**：单个任务失败只标红该行，队列继续跑下一个。
3. **取消粒度两级**：取消单个任务 / 取消整个队列。
4. **队列窗常驻**：随时能看到「在跑什么、后面还排了什么」。
5. **跨进程并入同一队列**：右键多选 N 个包 → 一个队列窗、N 行任务。

## 用户口径（2026-10-29 已确认）

1. **入队时机**：点「开始压缩」才入队 —— 设置窗仍然是每次弹（配置阶段不入队）。
2. **队列窗常驻**（不是跑完就关）。
3. **跨进程也要并入同一队列**（⇒ 必须先做 GUI 单实例）。
4. **单任务失败后继续跑后续**（不停队列等你处理）。
5. **单任务取消 + 整体取消都要**。

## 设计

### 一、五层推进（每层可独立交付、独立回滚）

| 阶段 | 交付物 | 估时 | 风险 |
|---|---|---|---|
| **P0 串行队列** | `CompressJob` + `CompressJobQueue`（无 UI 的调度器）+ 入队接缝 + 「第 N/M 个任务」显示；顺带修 `WaitForManualCloseAsync` 竞态 | 4-6h | 低 |
| **P1 队列窗** | 常驻 `JobQueueWindow`：任务列表（状态/进度/取消）、底部「暂停全部 / 取消全部 / 清除已完成」 | 8-12h | 中 |
| **P2 失败隔离与弹窗归属** | 失败行显示原因 +「重试」；冲突/密码弹窗 owner 从 `ProgressWindow.CurrentVisible` 改为会话级引用 | 6-8h | 中 |
| **P3 跨进程 + 后台执行** | GUI 单实例互斥 + 队列管道协议（带版本）；引擎移出 UI 线程（与「真暂停」同一前置，打包做） | 16-24h | **高** |
| **P4 持久化与增强** | 队列跨重启、拖拽重排、压缩后校验 | 12-16h | 低-中 |

> **建议切法**：先做 P0+P1（≈12-18h）就能发一个「能用」的版本；P3 与「真暂停」合并成一条「引擎后台化」任务一起评估。

### 二、任务模型

```
CompressJob
├─ Id                Guid
├─ DisplayName       输出包名（来自 CompressSettings，供列表显示）
├─ SourcePaths       IReadOnlyList<string>（冻结）
├─ Settings          CompressSettings（冻结快照；入队后不再被 UI 改）
├─ State             Queued | Running | Completed | Failed | Cancelled
├─ Outcome           CompressOutcome?（终态才有；沿用现有四态）
├─ CreatedAt / StartedAt / FinishedAt
├─ CancelSource      CancellationTokenSource（单任务取消）
└─ Origin            Local | Remote（P3 来自其他进程）
```

不变式：

- **入队即冻结**：`Settings` 是副本，用户事后改设置不影响已入队任务。
- **每个任务独立 `CancellationTokenSource`**，队列自身另有 `CancellationTokenSource`（整体取消）。
- `Outcome` 只由调度器写，UI 只读 —— 与规则 20 的「不回读可变状态」一致。

### 三、调度器（P0 核心）

```
CompressJobQueue
├─ Enqueue(CompressJob) → job.Id
├─ StartAsync()            单消费者循环：出队 → 跑 → 记终态 → 下一个
├─ PauseAllAsync() / ResumeAllAsync()   只在**任务边界**生效（任务内部暂停属「真暂停」，另议）
├─ CancelJob(Guid)         运行中 → cts.Cancel()；等待中 → 直接移出并标 Cancelled
├─ CancelAllAsync()        取消当前 + 清空等待项（语义待确认，见下）
└─ events: JobStateChanged / JobProgress / QueueChanged
```

要点：

- **并发度固定为 1**（常量，将来要放开再评估摩擦点表）。
- **失败隔离**：`catch` 后写 `Outcome = Failed(reason)`，**不 break**。
- **执行体复用现有会话**：`CompressSession.RunAsync(settings, context, ct)`（`src/MantisZip.UI.Avalonia/Services/CompressSession.cs:117`）不动，调度器只负责「何时调用、把结果记到哪」。这层接缝已经存在（引擎/冲突弹窗/密码库/路径历史全是注入的委托，可在无引擎的测试里跑）。
- **进度呈现**：P0 直接复用 `MainWindowProgressPresenter`（`src/MantisZip.UI.Avalonia/Services/MainWindowProgressPresenter.cs:47 RunAsync`），只是在 `ProgressRunRequest.Title` 里加「第 N/M 个任务」；P1 换成队列窗自己的呈现器。

### 四、队列窗（P1）

- **常驻形态**：默认隐藏，队列非空时自动出现；主窗口工具栏加一枚「队列」按钮（带未完成数量徽标）。
- **列**：输出名 / 源数量 / 状态（等待·运行·完成·失败·取消）/ 进度（仅运行项）/ 取消按钮。
- **底部**：暂停全部、取消全部、清除已完成。
- **复用**：紧凑度资源（`ControlHeightMd` 行高、`SpacingXxsThk` 等）、`ThemeStatus*Brush` 状态色；**不复用** `ProgressWindow` 控件本身 —— 那个窗口已经承载「单次运行」的全部语义，硬塞队列会把它变成万能窗。
- 新 `.axaml` 必须写中文注释（规则 14）、走主题资源（规则 4）、列表行高走紧凑度键（规则 7）。

### 五、跨进程并入同一队列（P3）

两道工序，顺序不能换：

1. **GUI 单实例**：新增 `MantisZipGuiMutex`；第二个实例把已有窗口激活到前台，然后把「任务意图」转交过去而不是自己跑。**必须与现有 CLI 压缩互斥量区分开**（`App.CliCompress.cs:53/:208/:310`、`App.CliInfra.cs:262`），否则右键菜单那批「有意开新进程」的分支会被一起挡掉。
2. **队列管道**：复用 `App.CliInfra.StartPipeServer`（`App.CliInfra.cs:229`）与 `SendPathsThroughPipe`（`:269`）的形状，但一行不再只传路径，改为传 JSON `QueueJobEnvelope { Version, SourcePaths, Settings, Origin }`。**必须带版本**：新旧版本并存时，首实例解析失败要能降级为「另起一个正常进程」，不能静默丢任务。

### 六、取消与失败语义

| 操作 | 运行中任务 | 等待中任务 | 队列 |
|---|---|---|---|
| 取消单任务 | `cts.Cancel()`，标 Cancelled | 直接移出，标 Cancelled | 继续下一个 |
| 取消全部 | 取消当前 | 全部标 Cancelled 并清空 | 停止（需用户再点「开始」） |
| 暂停全部 | **等当前任务跑到边界**（不打断） | 保持排队 | 停止出队 |
| 单任务失败 | 标 Failed + 原因 | 不受影响 | **继续下一个**（用户口径 4） |

> 「取消全部」是否连带清空等待项，建议**清空**（TeraCopy 也是这个语义），但这会丢用户已配置的任务，需要用户点头；替代方案是「只停当前、保留队列」。**P1 开工前必须确认。**

### 七、与现有接缝的关系（规则 20）

- `MainWindow.axaml.cs:279-311` 现在 `await dialog.RunConfirmedAsync(confirmed.Value)` —— 阻塞到跑完。改成 `queue.Enqueue(new CompressJob(...))` 后立即返回 ⇒ **#66 那条路径从根本上消失**（按钮不再等待任何一次运行）。
- 入队参数来自设置窗已经返回的 `confirmed.Value`（`ConfirmOutcome<CompressSettings>`），**不新增任何弹窗后回读**。
- 调度器是新增端口，`IProgressPresenter`（`src/MantisZip.UI.Avalonia/Services/IProgressPresenter.cs:25`）契约不动；P1 引入 `IQueuePresenter` 新实现，`CompressSession` 一行不改。

### 八、P0 必须顺带修的既有缺陷

`src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs:420-452` 的 `WaitForManualCloseAsync`：

- 现状：`IsVisible` 检查 → `Closed += onClosed` → `await Task.Run(() => gate.Wait())`，两步之间窗口可能已关 → 信号量永不 Set。
- 修法：**先订阅再复查**（订阅后立刻 `if (!IsVisible) onClosed();`），并给 `gate.Wait()` 加一个足够长的上限（如 10 分钟）作为最后兜底 + 写 `conflict.log`/诊断日志。
- 这条即使队列不上，也值得单独修（它会让任何一次「等窗口关闭」的调用永久挂住）。

## 实施步骤

1. **P0-a**：修 `WaitForManualCloseAsync` 竞态（先订阅再复查 + 兜底上限 + 日志），跑 `ui-tests` 全量；单独可提交。
2. **P0-b**：新增 `src/MantisZip.UI.Avalonia/Services/CompressJobQueue.cs` + `Models/CompressJob.cs`（无 UI，纯调度），配单元测试（串行、失败继续、取消单任务、取消全部、暂停边界）。
3. **P0-c**：`MainWindowViewModel.Compress.cs` 的 `NewArchive` 改为入队后立即返回；`MainWindow.axaml.cs:279-311` 的 `ShowCompressSettingsDialog` 在确认后入队；队列在 `MainWindowViewModel` 里持有一个实例（P3 再改成 App 级单例）。
4. **P0-d**：进度窗标题显示「第 N/M 个任务」（新增 i18n key，三语同步）。
5. **P1-a**：`Views/JobQueueWindow.axaml(.cs)` + `ViewModels/JobQueueViewModel.cs`；列表行高、状态色、中文注释按规则 4/7/14。
6. **P1-b**：主窗口工具栏「队列」入口 + 未完成徽标；队列非空时自动显示队列窗。
7. **P1-c**：暂停/取消按钮接线；确认「取消全部」的语义（清空 or 保留）。
8. **P2-a**：冲突/密码弹窗 owner 改为会话级引用（去掉对 `ProgressWindow.CurrentVisible` 的静态依赖），补测试。
9. **P2-b**：失败行「重试」按钮（复制 job 重新入队）。
10. **P3-a**：GUI 单实例互斥 + 队列管道协议（带 `Version`）+ 降级路径。
11. **P3-b**：引擎移出 UI 线程（与「真暂停」合并成一条「引擎后台化」任务，含进度回放、冲突弹窗封送、暂停安全点）。
12. **P4**：队列持久化（`Data/queue.json`，重启可恢复未完成任务）、拖拽重排、压缩后校验。
13. 每阶段收尾：`-Task check` / `-Task build -Target avalonia` / `-Task test -Target tests` / `-Task test -Target ui-tests` / `-Task guards`，并按规则 3 更新 `docs/PROGRESS.md` 与 `docs/progress-avalonia-detail.md`。

## 测试计划

- **调度器单测**（无 UI、假引擎）：串行不变（同一时刻只有一个 Running）、失败后继续、取消单任务不移除后续、取消全部清空、暂停后不再出队。
- **会话集成测试**：`CompressSession` 注入假 `compressAsync`，断言队列按序调用、结局映射正确（完成/失败/取消三态）。
- **UI 测试**（headless）：`JobQueueViewModel` 行状态与徽标数；「取消」把行变 Cancelled 且不删行。
- **守卫**：新增 i18n key 进三语；新增设置项（若有）必须同提交接上消费者（规则 23 `settingswiring`）。
- **手工验收**（P0）：连续入队 3 个任务 → 按钮始终可点 → 队列窗一次只跑一个 → 取消第 2 个 → 第 3 个照跑；中途关主窗口的行为要明确（建议：提示「队列还有任务」）。

## 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| P3 引擎移出 UI 线程回归面大 | 进度跳动/冻结、弹窗死锁（「真暂停」已踩过一次，见 #61 与 `docs/progress-avalonia-detail.md` 2026-10-26 条目） | 先做 P0-P2（不碰线程模型）；P3 单独立项、分批验证、与「真暂停」共用安全点与封送设施 |
| 队列窗常驻但找不到/关不掉 | 用户以为卡住 | 默认随队列非空出现、空队列隐藏；主窗口有固定入口 + 徽标 |
| 「取消全部」语义选错 | 丢用户已配置的任务 | 开工前确认；默认取保守语义（只停当前、保留队列）也可 |
| 跨进程协议版本不一致 | 旧版收到新版任务解析失败 | 协议带 `Version`，解析失败降级为「独立进程照常跑」 |
| `ProgressWindow.CurrentVisible` 静态单例被队列绕过 | 冲突弹窗 owner 错乱 | P2 就把它改成会话级引用，不等 P3 |
| 内存：多个 job 各持一份 `CompressSettings` 与源路径列表 | 任务多时占用上升 | 设置是小型 record；上限设 100 个任务并提示 |

## Definition of Done

- [ ] P0：点「开始压缩」即入队、按钮立刻可用；同一时刻只跑一个；「第 N/M 个任务」可见
- [ ] P0：`WaitForManualCloseAsync` 竞态修复，有回归测试
- [ ] P1：常驻队列窗（列表 + 单任务取消 + 暂停全部 + 取消全部 + 清除已完成），三语文案齐全
- [ ] P2：失败行显示原因且不阻断后续；冲突/密码弹窗 owner 不再依赖 `ProgressWindow.CurrentVisible`
- [ ] P3（单独决定是否本轮）：GUI 单实例 + 队列管道 + 引擎后台化
- [ ] `-Task check` / `build -Target avalonia` / `tests` / `ui-tests` / `guards`（8 条）全绿
- [ ] 进度文档（`docs/PROGRESS.md` + `docs/progress-avalonia-detail.md`）已更新
- [ ] 按规则 18 与用户确认 `RELEASE_NOTES.md` 范围
- [ ] 提交并 push 到 `beta`，CI 绿

## 附录

### A. 相关 issue 与历史

- **#66** 主工具栏「新建压缩包」用一次就灰化（2026-10-28，`mantis3d`）：本计划 P0 顺带根治 —— 入队后按钮不再等待任何一次运行。
- **#55** 多选压缩包右键「解压到……」为每个包各开一个窗口（已修 CLI 侧，`6ce2c453` / `accf3082`）：GUI 侧还有一个版本 —— 同一时刻只能有一个任务。
- **#61** 冲突提示链路卡死 + `conflict.log`（`5311cb43`）：其中「真暂停」部分回滚（`7f1e7e58`），原因正是引擎跑在 UI 线程上 —— 与本计划 P3 同一个前置条件。

### B. 术语

- **任务（Job）**：一次「输出一个（或一组）压缩包」的完整意图，含冻结的设置快照。
- **队列（Queue）**：按入队顺序串行执行任务的调度器。
- **任务边界**：两个任务之间；暂停只在这里生效（任务内部暂停需要引擎配合，属「真暂停」）。
