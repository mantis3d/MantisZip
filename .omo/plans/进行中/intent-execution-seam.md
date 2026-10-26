# 意图-执行接缝收口（消灭「点了没反应」类 bug）

> **状态**: 🚧 进行中（2026-10-26 开工，波 1–3 分批评审）| **创建**: 2026-10-24 | **优先级**: P1 | **预估工时**: 18-24h（三波）
> **实施分支（2026-10-26 调整）**: `alpha` —— §11 决策 1 原定 `beta`（除错分支），但 `beta` 已落后 `alpha` 大量提交（图标归一化、删除目录修复、选中态修复等），切回 `beta` 会丢掉这些基线，故沿用 2026-10-26 删除目录计划同款理由改在 `alpha` 上做。
> **来源**: 拖入文件点「压缩」无反应（`eaebcf60`）后续追问 —— 用户提出「窗口不该知道自己从哪个途径弹出然后做出不同行为」，要求一个彻底杜绝同类问题的规则而非单点修复。
> **已定案**（用户 2026-10-24 拍板）：主形态=① 窗口注入执行器；范围=③ 含全仓 `ShowDialog` 清账；规则形态=① AGENTS.md 新增规则 + 守卫脚本；`CopyExecutionSettingsFrom` 去留=**退休**（见 §4.6）。
> **跟踪**: GitHub issue [#47](https://github.com/mantis3d/MantisZip/issues/47) —「[设计提案] 收口对话框接缝」，正文为本计划摘要 + 全文折叠。

---

## 1. 问题定义：接缝 bug

用户意图从「界面」传到「执行」，中间有一道缝。缝两侧各自正确，缝里没人负责。

**三个固定特征**：编译器不管 / 运行时静默 / 测试不覆盖。故症状恒定：**界面一切正常，点下去什么也没发生，无异常、无日志、无提示。**

### 判准（本计划的总目标）

> 任何「用户明确点了确认、世界却没有变化」的状态，必须让它**无法被表达** —— 从「不该出现」变成「写不出来」。

### 类型学（七种子类，本仓库全部有实例）

| # | 子类 | 机制 | 实例 |
|---|---|---|---|
| ① | 结果丢弃 | `ShowDialog` 返回值无人消费 | 3b 拖入（`eaebcf60` 前）；`App.CliInfra.cs:172/185/218` |
| ② | 载荷旁路 | 结果只有 bool，真参数在窗口**公开可变属性**上被"补读" | `Views/MainWindow.axaml.cs:249-267`（`ViewModel`+`GetFilter()`+`GetPlanForExecution()` 三处分别取）；`App.CliExtract.cs:362-365`（直读 3 个 VM 属性） |
| ③ | 后门覆盖 | 公开可写委托被调用方整体替换 | `App.CliCompress.cs:118` 覆盖 `CloseAction`；`App.CliExtract.cs:352` 用 `DialogResult` 另立门户 |
| ④ | 静默早退 | 前置校验失败直接 `return` | `ViewModels/CompressSettingsViewModel.cs:1343`（两次密码不一致）；`ViewModels/MainWindowViewModel.Compress.cs:28/31/69`；`App.CliExtract.cs:367-371` |
| ⑤ | 双份真相 | 可见状态就绪 ≠ 执行载荷就绪 | `CanExecuteStartCompress` 不看 `Plan`；`Plan` 是异步派生的第二份真相 |
| ⑥ | 多份实现 | 同一动作 N 份手写，必然漏字段 | `new CompressRequest` ×4（`App.CliCompress.cs:184/281/389` + `Services/CompressFlow.cs:56`） |
| ⑦ | 阻断式接缝 | 同步阻塞等 UI 结果 + 属性读 | `Services/CompressFlow.cs:131-143`：`Dispatcher.UIThread.InvokeAsync(...).Result` 套 `ShowDialog(...).GetAwaiter().GetResult()`，再读 `dlg.ResultAction` |

---

## 2. 七次同族事故（为什么"修一处"永远不够）

| # | 事故 | 证据 |
|---|---|---|
| 1-3 | 压缩侧逐属性手抄，**漏过三次**：多线程/并行度恒 false、分卷大小恒 0、冲突策略恒「每次询问」 | `ViewModels/ExtractSettingsViewModel.cs:126-128` 注释自述 |
| 4 | 拖入分支无人接续，点压缩关窗无反应 | 提交 `eaebcf60` |
| 5 | CLI 压缩：B 数据集未就绪 + `request==null` 时 `desktop.Shutdown()` 静默退出 | 提交 `c76c3af9` |
| 6 | 解压 CLI 多包：`FilteredEntryKeys` **恒 null**，过滤条件静默失效 | `ViewModels/ExtractSettingsViewModel.cs:956-958`：「修复潜伏 bug…此前从未被赋值（恒 null）」 |
| 7 | 密码库保存在且仅在主窗口入口生效（右键菜单压缩勾了不保存） | `ViewModels/MainWindowViewModel.Compress.cs:98`（**仍然活着**） |

> **附：2026-10-26 登记的「形似但不同族」事故（不并入本计划波次）**：压缩包内删除目录静默无效（已修，见 [delete-directory-entries.md](../已完成/delete-directory-entries.md)）。它表现为同一个「点了没反应」，但**参数没丢**——调用方传的路径是对的，是引擎侧匹配语义（精确相等、无递归）与调用方假设（目录路径应含子条目）不一致，且 copy-mode 快路径不做存在性校验。属于**引擎语义缺陷**，靠连通性/结构收口治不了，故只在此登记引用。

对照 #1-3 与 #6 可见：**"收敛到一处 + 漂移守卫测试"这套已经用过两次的修法也救不了 #6**，因为 `FilteredEntryKeys` 根本不在被收敛的方法里（`:131` 明说"由 View 从过滤编辑器取，调用方单独赋值"）。

结论：**只要执行侧参数还需要沿接缝"搬运"一次，就一定会漏。要消灭的是"搬运"这个动作本身。**

---

## 3. 目标与非目标

### 目标

- **G1** 消灭搬运：设置窗口确认时一次产出不可变 DTO，调用方不再从窗口补读任何字段。
- **G2** 消灭漏接续：执行器作为**必填构造依赖**注入窗口，"忘写接续"变成编译错误。
- **G3** 消灭静默：确认动作变成事务，失败必须返回非空 reason 并留在窗口；`Rejected` 无无参构造。
- **G4** 消灭入口差异：密码库保存、历史记录、冲突/错误解析全部下沉到唯一执行器。
- **G5** 防退化：AGENTS.md 新增规则 20 + `verify-repo.ps1 -Task guards` 新增 `dialog-seam` 守卫 + 架构测试。

### 非目标

- **N1** 不引入 DI 容器（沿用现有显式传入/回调风格，与 `ShowMessage`/`BrowseOutput` 一致）。
- **N2** 不动 Core 层（`CompressRequest`/`ExtractRequest` 契约不变）。
- **N3** 不改 CLI 交互与退出语义（单实例 IPC、`ShutdownMode.OnExplicitShutdown`、退出码行为全部保持）。
- **N4** 不改模态模型（主窗口仍 `ShowDialog(owner)`；CLI 仍非模态 `Show()` + `Closed`）。
- **N5** 不顺手改视觉/布局/文案（除新增 i18n key）。

---

## 4. 设计

### 4.1 四条法则

1. **唯一落地者** —— 一个用户意图只能有一个执行器。发起侧只许「收集参数 + 交棒」。
2. **结果即载荷** —— 确认产物是不可变快照，随结果一起走。类型非空 ⇒ 参数完整可执行。
3. **确认即事务，关窗即成功** —— 校验 → 产出 → 成功才关窗。失败返回原因并留窗。**"关窗"从此只表示成功。**
4. **单一校验谓词** —— `CanExecute` 与确认动作共用同一 `Validate()`，且必须覆盖执行载荷就绪性（含版本新鲜度）。

### 4.2 目标形态（骨架，非最终签名）

```csharp
// ① 发起侧：三个压缩入口变得完全一样，差别只剩 ctx 里的值
var ctx = new CompressRunContext(
    owner:     this,
    presenter: new MainWindowProgressPresenter(this, vm),   // 进度呈现方式
    exitPolicy: ExitPolicy.None);                           // CLI 传 ShutdownAfterRun
var dlg = new CompressSettingsWindow(_compressSession, ctx, seeds: droppedPaths);
await dlg.ShowDialog(this);          // 返回值无人关心，也不该有人关心
if (_compressSession.LastOutcome == CompressOutcome.Completed) vm.StatusMessage = T("Status_Compressed");

// ② 窗口：确认是唯一入口，调用方无法绕过
private async Task ConfirmAsync()
{
    var result = await ViewModel.TryConfirmAsync();     // 校验 → 等 Plan → 快照 DTO
    switch (result)
    {
        case Rejected r:  ShowMessage(r.Reason); break;  // 留窗，按钮回弹
        case Confirmed<CompressSettings> c:
            Close();
            try { await Session.RunAsync(c.Value, _ctx); }   // 唯一执行器
            catch (Exception ex) { await ReportAsync(ex); }  // owner 已失效 → 无 owner 兜底弹窗
            break;
    }
}

// ③ 执行器：唯一一处做事的地方（含密码库/历史/冲突/错误/进度）
public interface ICompressSession { Task<CompressOutcome> RunAsync(CompressSettings s, CompressRunContext ctx); }
```

`Confirmed` / `Rejected(reason)` 用一个小型 `ConfirmOutcome<T>` 三态类型承载（`Cancelled` 是第三态，不弹提示）。`Rejected` 无无参构造 —— 这是把"静默"从语言层面删除的关键。

### 4.3 契约明细

| 类型 | 职责 | 硬约束 |
|---|---|---|
| `CompressSettings` / `ExtractSettings` | 确认时刻的完整执行参数快照（含 `SelectedPaths`、格式/级别/输出/密码/注释/冲突策略/全部高级选项、`FileFilter`、`Plan`） | `sealed record`；`required` 成员 + NRT；**只读**；构造后不可变 |
| `ICompressSession.RunAsync` / `IExtractSession.RunAsync` | 唯一执行器：构建 `CompressRequest` → 冲突/错误解析 → 进度 → 历史 → 密码库 → 返回结构化 outcome | 不许新建请求副本；不许调用方传部分参数 |
| `CompressRunContext` / `ExtractRunContext` | 宿主差异：`owner`、`presenter`、`exitPolicy`、`displayPaths`、`isCompressFlow` | 只装**因宿主不同**的东西；不得出现业务分支 |
| `IProgressPresenter` | 「起进度 → 跑 → 返回是否完成」的唯一入口 | 吸收现有 `RunWithProgress`（主窗口）与 `CompressWithProgress` / `RunCliExtractBatchWithProgressAsync`（CLI）两套实现 |
| `CompressOutcome` | 结构化结果：`Completed` / `Cancelled` / `Failed(reason)` / `NotStarted(reason)` | 调用方只据它决定"显示什么"，不再据 bool |
| `ConfirmOutcome<T>` | `Confirmed(T)` / `Rejected(reason)` / `Cancelled` | `Rejected` 必须非空 reason；`Debug.Fail` 兜底 |

**职责边界（重要）**：执行器只负责"做事"，调用方只负责"显示结果"。两者之间传结构化 outcome，不传 bool。主窗口的 `StatusMessage` 刷新、列表刷新属于**表现层**，留在调用方；密码库保存、历史写入属于**业务**，必须在执行器内。

### 4.4 为什么这样就能防住（事故 → 结构性不可能）

| 事故 | 新结构下的命运 |
|---|---|
| 3b 漏接续 | `session` 是必填构造参数 → **编译不通过** |
| 手抄漏字段（3 次） | 没有手抄动作，DTO 由窗口事务内一次产出；`required` + NRT 编译期兜底 |
| `CloseAction` 被覆盖 | 字段删除，**后门不存在** |
| B 数据集未就绪 | 校验与等待都在事务内；不就绪 → `Rejected`，窗口不关 |
| `FilteredEntryKeys` 恒 null | 它是 DTO 的 `required` 成员，构造时必填 |
| 密码库只在主窗口 | 唯一执行器内，所有入口必然一致 |
| 静默 return | `Rejected(reason)` 类型强制 + Debug 断言 |

### 4.5 生命周期与顺序决策

- **先关窗再跑**：事务成功后 `Close()`，再 `await Session.RunAsync(...)`。执行器内部自带 `try/catch`，异常走无 owner 兜底弹窗（owner 已失效）。
- **CLI 关闭语义保留**：`App.CliExtract.cs:345-355` 那套（`ShutdownMode.OnExplicitShutdown` + `Closed` 等待 + 显式 `Shutdown`）不动，退出时机改由 `ctx.exitPolicy` 承接。
- **进度窗口播种去副作用**：现有 `PendingCompressFlow` / `PendingCompressRequest`（`MainWindowViewModel.Compress.cs:70-71`）是又一个"可变状态侧通道"，改为 `ctx.displayPaths` / `ctx.isCompressFlow` 传入。
- **`AdoptPlan` 顺序契约消失**：`MainWindow.axaml.cs:261-264` 现有注释要求的"必须最后执行"不再需要 —— DTO 一次成型。

### 4.6 `CopyExecutionSettingsFrom` 退休（本次决定）

**退休**，不保留。理由：

1. 它存在的唯一理由就是"执行侧参数需要搬运"，而本计划的目标就是消灭搬运；留着它等于留一条可绕过 DTO 的后门。
2. 它的两份反射守卫测试（`CompressSettingsDialogCopyTests` / `ExtractSettingsDialogCopyTests`）是**事后兜底**，会给人"已受保护"的错觉 —— 事实是它兜住了 #1-3，却没兜住 #6。
3. 预填需求改用**反向单向** `ApplyPrefill(CompressSettings)`（DTO → VM，仅界面用途，不参与执行）。方向反过来后，它不可能成为执行路径。

替代守卫：`CompressSettingsDtoCoverageTests` 断言 DTO 属性集 ⊇ 旧拷贝清单字段集 —— 仍防漏字段，但靠的是编译期 + 断言，而非手抄。

---

## 5. 分波任务

### 波 1：压缩（6-8h）

- [ ] **T1** 新增 `Models/ConfirmOutcome.cs`（三态 + `Rejected` 非空 reason 校验 + `Debug.Fail`）。
- [ ] **T2** 新增 `Models/CompressSettings.cs`（`sealed record`，`required` 全字段），迁移 `CompressSettingsViewModel` 的执行侧属性为 DTO 构造来源；`CompressSettingsViewModel.TryConfirmAsync()` 落地（内部 `Validate()` → `EnsurePlanReadyAsync()` → 快照）。
  - 验收：`SelectedPaths` 空 / 输出路径非法 / Plan 构建失败 / deflate64+分卷未加密 / 空密码 / 两次密码不一致 —— 6 种失败态全部返回 `Rejected(非空 reason)`。
- [ ] **T3** 新增 `Services/CompressSession.cs` + `ICompressSession`（吸收 `ExecuteCompressFromSettings` 全部业务步骤与 CLI 的 `CompressWithProgress` 流程），密码库保存段整体迁入。
- [ ] **T4** 新增 `Services/IProgressPresenter` + `MainWindowProgressPresenter` / `CliProgressPresenter`；`RunWithProgress` 与 `CompressWithProgress` 收口到同一契约。
  - 注意：`RunWithProgress` 同时服务解压/测试流程，抽象必须保持通用，不得烤入压缩语义。
- [ ] **T5** `CompressSettingsWindow` 改造：构造函数注入 `ICompressSession` + `CompressRunContext`；删除 `CloseAction` 公开属性（VM 侧同步删）；`SnapshotFormatOptionsToViewModel()` 与 `GetFilter()` 并入事务内部，公开面收缩到 `ViewModel` + `ShowMessage` 注入。
- [ ] **T6** 四处入口改接：`MainWindowViewModel.Compress.cs:25-34`、`:42-50`、`App.CliCompress.cs:102-157`、`Controls/Menus/TestMenuView.axaml.cs:81`；删除 `ShowCompressSettingsDialog` / `ExecuteCompressFromSettings` 与 `App.CliCompress.cs:118` 的 `CloseAction` 覆盖。
- [ ] **T7** i18n：新增 `Compress_PasswordMismatch`（三语成对，UTF-8 BOM + CRLF），必要时 `Compress_NoSources`。

### 波 2：解压（5-6h）

- [ ] **T8** 新增 `Models/ExtractSettings.cs`；`ExtractSettingsViewModel.TryConfirmAsync()` 落地（含 `FilteredEntryKeys` 收进 DTO，从"调用方单独赋值"变成"事务内必产出"）。
- [ ] **T9** 新增 `Services/ExtractSession.cs` + `IExtractSession`（吸收 `MainWindowViewModel.Extract.cs:30/312` 与 `App.CliExtract.cs` 批处理两条链）。
- [ ] **T10** `ExtractSettingsWindow` 改造：注入 `IExtractSession` + `ExtractRunContext`；删除 `CloseAction` 与 `DialogResult` 双轨（`App.CliExtract.cs:352` 的非模态等待改用窗口的 `Task` 完成信号）。
- [ ] **T11** 五处入口改接：`MainWindowViewModel.Extract.cs:30`、`:312`、`Views/MainWindow.axaml.cs:225-244`、`App.CliExtract.cs:342-374`、`TestMenuView.axaml.cs:82`。
- [ ] **T12** 退休 `CopyExecutionSettingsFrom`（压缩/解压两侧）、`GetFilter()`、`GetFilteredEntryKeys()`、`GetPlanForExecution()` 的公开外泄用法；两份反射守卫测试改口径（见 §6）。

### 波 3：全仓清账 + 规则化（7-9h）

- [x] **T13** 逐点判定附录 A 的 68 处命中，产出**逐点判定表**回填本附录（A 改 / B 参照 / C 登记 / D 安全 / E 单独治 / F 随 A）。判定标准：①是否携带载荷 ②失败是否静默 ③是否存在属性补读。
- [ ] **T14** C 类（纯提示/帮助/管理器）统一为无返回值 API（`Task ShowAsync(...)`）—— 消除"有个 bool 但没人该看"的误导性契约。
- [ ] **T15** E 类：`Services/CompressFlow.cs:131-143` 去同步阻塞。改为 `await Dispatcher.UIThread.InvokeAsync(...)` 全 async 化；`ErrorDialog.ResultAction` 改为 `ShowDialog<ErrorDialogResult>(owner)` 返回值，删属性读。
- [ ] **T16** F 类：`Services/PasswordRetryLoop.cs:144`、`App.CliCompress.cs:155`、`App.CliExtract.cs:353` 统一走新契约。
- [ ] **T17** `scripts/verify-repo.ps1` 新增 `-Task guards` 第 5 条守卫 `dialog-seam`（实现要点见 §6.1），并同步 `docs/MAINTENANCE.md`（该文档明确要求"要改这套门禁先读"+ 记录反直觉实现与覆盖边界）。
- [ ] **T18** AGENTS.md 新增规则 20（草案见 §6.3），并在「维护脚本」表格补 `dialog-seam` 一行。

---

## 6. 守卫与测试

### 6.1 守卫 `dialog-seam`（源码文本扫描，风格对齐现有 4 条）

| 检查 | 规则 | 白名单 |
|---|---|---|
| 1 | `new (Compress\|Extract)SettingsWindow(` 必须出现在登记表 | 9 处入口 + UI 测试 |
| 2 | `Func<bool, ?Task>? CloseAction` / `DialogResult` 双轨 → **命中即失败** | 全部退休后应为 0 |
| 3 | `new CompressRequest` / `new ExtractRequest` 只许出现在 `Services/*Flow.cs` | `CompressFlow.cs`、`ExtractFlow.cs` |
| 4 | 设置窗口构造函数必须含 `ICompressSession`/`IExtractSession` 形参 | — |

输出格式与现有守卫一致：`文件:行 + 违例类型`，退出码 1 = 有问题。

### 6.2 测试矩阵

| 测试 | 断言 |
|---|---|
| `ConfirmTransactionTests`（新） | 6 种压缩失败态 + 3 种解压失败态 → `Rejected` 且 reason 非空；成功态 → `Confirmed` 且 DTO 全字段非默认 |
| `CompressSettingsDtoCoverageTests`（新） | DTO 属性集 ⊇ 旧 `CopyExecutionSettingsFrom` 字段清单（防漏字段，替代反射守卫） |
| `EntryMatrixTests`（新） | 4 压缩 + 5 解压入口，用 fake session 断言 `RunAsync` 恰被调用一次、收到的 DTO 与窗口内所见一致 |
| `DialogSeamGuardTests`（新） | 调用守卫脚本逻辑，断言当前仓库 OK |
| `DragInCompressWiringTests`（改） | 从"断言 `RunWithProgress` 被调用"改为"断言 session 收到同一 DTO"（不为过测试保留旧耦合） |
| `CompressSettingsDialogCopyTests` / `ExtractSettingsDialogCopyTests`（改口径） | 随 `CopyExecutionSettingsFrom` 退休，改为 DTO 完整性断言 |
| `HelpDialogRenderTests` / `MainWindowKeyBindingsTests` | 不动，作为回归 |

### 6.3 AGENTS.md 规则 20 草案

> **规则 20：意图-执行接缝（禁止把执行责任留给调用方）**
> ① 一个用户意图只能有一个执行器（服务层单点）。发起侧（窗口/菜单/拖拽/CLI/托盘）只许"收集参数 + 交棒"，禁止自己拼请求、自己开进度窗口、自己决定进程退出。
> ② 设置类对话框必须**注入执行器**并在确认事务内发起执行。禁止用 `Func<bool,Task>? CloseAction` + 公开可变 VM 属性回传载荷；确认产物必须是不可变 DTO（`required` + NRT）。
> ③ 确认是一次事务：校验 → 产出 → 成功才关窗；失败必须 `Rejected(reason)` 留窗提示。**禁止任何无用户可见反馈的 return**（Debug 下 `Debug.Fail`）。
> ④ `CanExecute` 与确认动作共用同一 `Validate()`，且必须覆盖执行载荷就绪性（含版本新鲜度）。
> ⑤ 新增/修改任何对话框入口，必须登记到 `verify-repo.ps1 -Task guards` 的 `dialog-seam` 白名单。

---

## 7. 涉及文件

**新增**

- `src/MantisZip.UI.Avalonia/Models/ConfirmOutcome.cs`
- `src/MantisZip.UI.Avalonia/Models/CompressSettings.cs`、`Models/ExtractSettings.cs`
- `src/MantisZip.UI.Avalonia/Services/CompressSession.cs`、`Services/ExtractSession.cs`
- `src/MantisZip.UI.Avalonia/Services/IProgressPresenter.cs`（+ 两个实现）
- `src/MantisZip.UI.Avalonia/Models/CompressRunContext.cs`、`ExtractRunContext.cs`
- `tests/MantisZip.UI.Avalonia.Tests/ConfirmTransactionTests.cs`、`CompressSettingsDtoCoverageTests.cs`、`EntryMatrixTests.cs`、`DialogSeamGuardTests.cs`

**改造**

- `Dialogs/CompressSettingsWindow.axaml.cs`、`Dialogs/ExtractSettingsWindow.axaml.cs`
- `ViewModels/CompressSettingsViewModel.cs`、`ViewModels/ExtractSettingsViewModel.cs`
- `ViewModels/MainWindowViewModel.Compress.cs`、`ViewModels/MainWindowViewModel.Extract.cs`
- `Views/MainWindow.axaml.cs`（`ShowCompressSettingsDialog` / `ShowExtractSettingsDialog` 两个回调块）
- `App.CliCompress.cs`、`App.CliExtract.cs`、`Controls/Menus/TestMenuView.axaml.cs`
- `Services/CompressFlow.cs`（T15）、`Services/PasswordRetryLoop.cs`（T16）
- `scripts/verify-repo.ps1`、`AGENTS.md`、`docs/MAINTENANCE.md`
- 三语 `strings.*.json`（新增 key）

---

## 8. 验证

```powershell
pwsh -File scripts\verify-repo.ps1 -Task check
pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia
pwsh -File scripts\verify-repo.ps1 -Task test  -Target ui-tests
pwsh -File scripts\verify-repo.ps1 -Task test  -Target tests
pwsh -File scripts\verify-repo.ps1 -Task i18n
pwsh -File scripts\verify-repo.ps1 -Task guards      # 含新 dialog-seam
```

**手动回归**（每波各跑一轮）

- 压缩 4 入口：主窗口「新建压缩包」/ 拖入非压缩包 / Explorer 右键（单文件 + 多文件）/ 测试菜单。
- 解压 5 入口：主窗口工具栏 / 拖入压缩包 / 右键 CLI 单包 / 右键 CLI 多包 / 测试菜单。
- **密码库**：右键菜单压缩勾选「保存到密码库」后，密码管理器内应可见新条目（当前失效，修好后必验）。
- **过滤**：右键 CLI 解压 + 文件过滤 → 只解出匹配项（对应事故 #6）。
- 进度语义：取消 / 暂停 / 托盘最小化 / 完成后自动关闭 / 失败弹窗 owner 正确。
- 单实例 IPC：多选文件连续右键 → 合并到一个进度窗口。

**收尾**：按规则 3 更新 `docs/PROGRESS.md`（里程碑：架构级变更）+ `docs/progress-avalonia-detail.md`；按规则 18 向用户确认 `RELEASE_NOTES.md` 范围（本计划为内部重构，大概率仅"性能/改进"节一条或零条）。

---

## 9. 边界 / 不做

- 不改 Core 的 `CompressRequest` / `ExtractRequest` 契约。
- 不引入 DI 容器、不改 MVVM 分层风格。
- 不动 `ProgressWindow` 的视觉与交互。
- 不动 CLI 退出码语义与 IPC 协议。
- 不顺手重排现存 700+ 空白/风格问题（`.editorconfig` 门禁不含 whitespace）。

---

## 10. 风险与回退

| # | 风险 | 对策 |
|---|---|---|
| R1 | CLI 非模态关闭语义被破坏（`ShutdownMode` 陷阱） | 保留 `App.CliExtract.cs:345-355` 现方案，退出只经 `exitPolicy` |
| R2 | `RunWithProgress` 被解压/测试流程复用，presenter 抽象走偏 | 先抽接口再替换，presenter 不得含压缩/解压语义 |
| R3 | T15（去同步阻塞）与主改造耦合导致排查困难 | T15 独立提交，可单独回退 |
| R4 | 无头测试无法构造 `CompressSettingsWindow` | 先确认 `tests/MantisZip.UI.Avalonia.Tests` 的 headless 基建（现有窗口测试可作参照）；不行则把事务逻辑留在 VM 层测（VM 不依赖窗口） |
| R5 | 一次性重写 9 入口，中途状态不可用 | 按波提交，每波结束即"可用 + 全绿"；波 1/2 之间不合并 |

---

## 11. 决策（2026-10-24 定案）

三个开放问题已由用户授权自行裁定，结论如下（实施时不再复议）：

| # | 问题 | 决策 | 理由 |
|---|---|---|---|
| 1 | 实施分支 | **`beta`** | 本改造的动机是除错（7 次同族缺陷），产物才是架构收口；按仓库分支角色（`beta`=调试除错），且每波都以"某个入口恢复正常"验收 |
| 2 | `CompressSettings` 是否与 Core `CompressRequest` 合并 | **不合并** | 生命周期不同：`CompressSettings` =「确认那一刻的用户意图快照」（含过滤/密码/说明），`CompressRequest` =「引擎执行参数」，中间还隔着 filter 折算与 plan 折算两步；合并会把引擎契约拖进 UI 事务层 |
| 3 | ~20 处 C 类纯提示窗口是否强制改无返回值 API | **(b) 仅登记、只禁新增** | 强制统一会产生零行为变化的改动噪音，而 C 类无载荷、本无静默风险；守卫只拦「新增 C 类却返回 bool」 |

> 决策 3 的边界：`dialog-seam` 对 C 类的检查只覆盖**新增**调用点（存量基线清单由 T13 生成并写入附录 A），不追溯改写。

---

## 12. 术语对照（供评审与检索）

> 本节只是**词汇表**，不引入额外流程。目的：评审时用同一套词讨论，以及让后来接手的人（含 AI）能按标准名检索到设计依据。

### 12.1 缺陷叫什么

| 标准名 | 出处 | 本项目对应 |
|---|---|---|
| CWE-252 Unchecked Return Value | MITRE CWE | 子类①：`ShowDialog<bool>` 的 `true` 被丢弃（事故 1/2/4/5/6） |
| CWE-391 Unchecked Error Condition | MITRE CWE | 子类④：`Validate` / `BuildRequest` 返回 null 后 `return` 无反馈（事故 3/7） |
| ODC「Checking」缺陷类 | IBM Orthogonal Defect Classification | 本族全部：不是算错，是"该检查没检查" |
| Gulf of Execution | Donald Norman《The Design of Everyday Things》 | 按了按钮而世界无响应 = 死按钮 |
| Visibility of system status / Help users recover | Nielsen 十项启发式 #1 / #9 | 静默早退同时违反这两条 |
| Connascence of Execution | Page-Jones《What Every Programmer Should Know About OO Design》 | `AdoptPlan` 必须最后调用（`src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs:249-267` 顺序契约） |
| Connascence of Value | 同上 | `Plan` 必须与 `SelectedPaths` / 过滤条件同版本 |

### 12.2 代码病叫什么（坏味道 → 重构手法）

| 坏味道（Fowler《重构》） | 本项目表现 | 采用的重构手法 |
|---|---|---|
| Shotgun Surgery | 同一语义在 4 个入口各写一遍 | Extract Class + Move Method（→ `ICompressSession`） |
| Duplicated Code | `new CompressRequest` 在 `src/MantisZip.UI.Avalonia/App.CliCompress.cs:184/281/389` 各写一次 | 同上 |
| Primitive Obsession | 结果类型是 `bool`，真载荷在旁边的对象上 | Introduce Parameter Object（→ `CompressSettings`）+ Preserve Whole Object |
| Feature Envy / Message Chains | 调用方连续读 `ViewModel` / `GetFilter()` / `GetPlanForExecution()` | Hide Delegate、Remove Middle Man（删掉这些外泄通道） |
| Temporal Coupling | `CopyExecutionSettingsFrom` 14 段顺序 + `AdoptPlan` 收官 | 让顺序不再必要（DTO 一次成型） |

### 12.3 设计原则叫什么（§4 四法则的标准名）

| §4 法则 | 标准原则 | 出处 |
|---|---|---|
| 法则 1 唯一落地者 | Tell, Don't Ask；Use Case Interactor | Hunt & Thomas《The Pragmatic Programmer》；R. C. Martin《Clean Architecture》 |
| 法则 2 结果即载荷 | Introduce Parameter Object；**Parse, don't validate** | Fowler；Alexis King (2019) |
| 法则 3 确认即事务、禁静默 | Fail Fast；Design by Contract（`Validate` = 前置条件，"关窗"= 后置条件） | Jim Shore, IEEE Software 2004；Bertrand Meyer |
| 法则 3/4 总判准 | **Make illegal states unrepresentable** | Yaron Minsky《Effective ML》(2010) |
| `ConfirmOutcome<T>` 三态 | Result / Either（C# 用可辨识联合表达，不引三方库） | 函数式惯例 |
| 宿主差异注入 | Ports & Adapters（`IProgressPresenter` = 端口） | Alistair Cockburn |

### 12.4 流程叫什么

| 标准做法 | 本项目落点 |
|---|---|
| Fix the class, not the case | 立项理由（§2：修一处永远不够） |
| 5 Whys / blameless postmortem（产出"缺陷类"而非"修这一处"） | §2 七次事故清单 |
| Architecture Fitness Function（Neal Ford《Building Evolutionary Architectures》） | §6.1 `dialog-seam` 守卫四条检查 |
| FMEA 五列：失效模式 / 影响 / 成因 / 预防 / 探测（IEC 60812） | 附录 A 分级表 |
| ADR（Architecture Decision Record）+ policy-as-code | §6.3 AGENTS 规则 20 + `scripts/verify-repo.ps1 -Task guards` |

### 12.5 耦合分级对照（before / after）

按 Yourdon/Constantine 七级耦合（内容 → 公共 → 外部 → 控制 → 标记 → 数据，从坏到好）衡量本计划实际改变的东西：

| 维度 | 改造前 | 改造后 | 变化 |
|---|---|---|---|
| 窗口 ↔ 调用方 | 共享**可变** `ViewModel` 属性（**公共耦合**） | 一次 `CompressSettings` 进 / `ConfirmOutcome` 出（**数据耦合**） | 降级 |
| 顺序依赖 | 14 段属性搬运 + `AdoptPlan` 收官（**时序耦合**） | DTO 一次成型，无顺序要求 | 消除 |
| 失败可见性 | 靠调用方"记得"查 bool（隐式契约） | `Rejected(reason)`，漏处理即编译失败 | 显式化 |
| 依赖方向 | 4 个入口依赖窗口内部细节 | 双方依赖 `ICompressSession` + DTO（依赖倒置 / SDP） | 稳定化 |
| 新增耦合 | — | 窗口 → `ICompressSession`；双方 → DTO 类型组 | **+1 条显性** |

> **净效果**：耦合总量未必减少，减少的是"不可检测的耦合"与"耦合面"（14 属性 × 顺序 × 4 处 → 1 接口 + 1 DTO）；真正提升的是**内聚**——同一语义从 4 份副本收敛为 1 处。故评审时不要只衡量"解耦度"（会低估本计划收益），应衡量**"同一语义还有没有第二份副本"**。
>
> **两条判据**：① 改 A 还得改谁？（改造后：只改 `CompressSession`）② 谁能静默破坏契约？（改造后：没人，破坏即编译失败或测试红灯）

### 12.6 明确未采纳

- **ArchUnitNET / NetArchTest**（C# 架构测试框架）替代现有 PowerShell 守卫：守卫已是仓库既有模式（4 条先例）、零依赖，且能覆盖"构造函数必须有某形参"这类文本级约定，架构测试框架反而管不了；遂不引入新依赖。
- **FsCheck 属性测试**（性质："任意 VM 状态 → `Confirmed` 且 DTO 完整，或 `Rejected` 且 reason 非空"）：表达力更强，但需新依赖 + 生成器；先枚举九种失败态（§6.2），稳定后再议。

> 一句话：名词不改代码，真正的判准只有三个——**fix the class**、**make illegal states unrepresentable**、**fitness function**。

---

## 附录 A：`ShowDialog` / `Show()` 逐点判定（T13 回填，2026-10-26）

> **以 2026-10-26 实测为准**：分支 `alpha`，扫描范围 `src/**/*.cs`，内置 grep 分别扫 `ShowDialog` 与 `\.Show\(`。
> 原始匹配 **69 行**（`ShowDialog` 49 + `.Show()` 20），剔除 **9 行注释**（`App.CliExtract.cs:345`、`Dialogs/CompressSettingsWindow.axaml.cs:17`、`Dialogs/AppMessageBox.axaml.cs:152`、`:224`、`Dialogs/EditAdaptiveRuleDialog.axaml.cs:56`、`ViewModels/MainWindowViewModel.Compress.cs:40`、`Dialogs/ExtractSettingsWindow.axaml.cs:17`、`:29`、`Dialogs/ProgressWindow.axaml.cs:29`）与 1 处非窗口静态辅助调用（`Program.cs:24` `NativeSplash.Show(...)`，带参重载）后，**真实窗口调用点 60 处**：`ShowDialog` 41 + `Show()` 19。
> `Show()` 19 处拆解：**3 处携带载荷**（F 类）+ **1 处 Debug 无 owner 降级**（`TestMenuView.axaml.cs:107`，D 类）+ **3 处 `App.CliInfra.cs` 无 owner 降级**（与同点 `ShowDialog` 计为同一提示接缝，C 类）+ **12 处非模态辅助窗**（本节末「守卫范围外」清单）。
> **守卫范围**：`dialog-seam` 的 C 类检查**只针对 `ShowDialog`**；非模态辅助窗（进度窗 / 拖放覆盖层 / 帮助窗 / 消息框无 owner 兜底）不在扫描范围内，仅在附录登记、不设基线。
> 已核对：全部 `Show()` 接收者均为 `Window` 派生类（`ProgressWindow` / `CollectingWindow` / `HelpDialog` / 各 `*Dialog` / 匿名 `Window`），**不存在非 Window 控件的 `.Show()`**；仓库已无 WinForms `MessageBox.Show`，消息框统一走 `AppMessageBox.Show(...)`（带参重载，不命中 `\.Show\(`）。
> 判定判据：①是否携带执行载荷 ②失败是否静默 ③是否存在属性补读。下表路径均相对 `src/MantisZip.UI.Avalonia/`，行号即当前工作区行号。
> 本文两张表：**逐点判定表（48 条）** + **守卫范围外清单（12 条）**，合计 60 处。

| 判定 | 含义 | 数量 | 处置 |
|---|---|---|---|
| A | 必须改造（带执行载荷 / 失败静默） | 2 | T9–T12 |
| B | 只读参照（有返回值 + 就地消费，无跨层搬运） | 16 | 不改，作为目标形态参照 |
| C | 纯提示/帮助/管理器（`ShowDialog` 形态） | 15 | 决策 3：只登记、只禁新增 |
| D | 安全（await 消费 / Debug 专用诊断） | 8 | 仅登记 |
| E | 单独治（阻断式 / 属性回读） | 3 | T15 |
| F | 随 A 一起改（载荷经窗口属性补读） | 4 | T16 |
| — | 守卫范围外（非模态辅助窗，非 `ShowDialog`） | 12 | 仅登记，不进 C 基线 |
| **合计** | — | **60** | — |

| 文件:行 | 调用形态 | 判定 | 依据 |
|---|---|---|---|
| `Views/MainWindow.axaml.cs:234` | `await dialog.ShowDialog<bool>(this)` | A | ①解压设置窗：确认后经 `CopyExecutionSettingsFrom(dialog.ViewModel)` + `GetFilteredEntryKeys()` 补读 ②`result==false` 直接返回、无反馈 ③补读 2 处 |
| `Views/MainWindow.axaml.cs:252` | `await dialog.ShowDialog<bool>(this)` | A | ①压缩设置窗：`CopyExecutionSettingsFrom` + `GetFilter()` + `AdoptPlan(GetPlanForExecution())` 三处补读 ②取消静默 ③补读 3 处 |
| `App.CliCompress.cs:155` | `dlg.Show()` | F | ①非模态设置窗：`ViewModel.CloseAction` 被整体覆盖（类型学③），窗内 `BuildRequest(vm)` 直读 VM ②`request==null` 已补提示 ③属性读经 VM 外泄 → 随 A（T16） |
| `App.CliExtract.cs:353` | `dialog.Show()` | F | ①非模态 + `Closed` 事件读 `DialogResult`，随后补读 `ViewModel.DestinationPath/ConflictAction/FilteredEntryKeys/MatchedPasswords` ②`dest` 空则直接 `Shutdown`（无反馈）③补读 4 处 → 随 A |
| `Services/PasswordRetryLoop.cs:139` | `await dialog.ShowDialog<bool>(dialogOwner) ? dialog.Password : null` | F | ①密码载荷经 `dialog.Password` **属性补读**，正是类型学②载荷旁路 ②取消返回 null 由外层重试消化 ③补读 1 处；与同方法 `:144` 同族同判（§5 T16，父 agent 2026-10-26 裁定） |
| `Services/PasswordRetryLoop.cs:144` | `dialog.Show()` | F | ①无 owner 退化路径：非模态 + 等 `Closed` ②密码错/取消由外层循环处理 ③`dialog.Password` 补读 → 随 A |
| `Services/CompressFlow.cs:141` | `dlg.ShowDialog(owner).GetAwaiter().GetResult()` | E | ①错误决策载荷 ②`InvokeAsync(...).Result` + 同步阻塞（类型学⑦）③`dlg.ResultAction` / `ApplyToAll` 补读 → T15 |
| `Services/CompressFlow.cs:280` | `await dlg.ShowDialog(dialogOwner)` | E | ①冲突决策载荷 ③`IsPaused`/`CancelOperation`/`ResultAction`/`CustomName`/`ApplyToAll` 五处属性回读 → 与 T15 同改法 |
| `Services/ExtractFlow.cs:301` | `await dlg.ShowDialog(dialogOwner)` | E | ①冲突决策载荷 ③同上五处属性回读（含 `info.CustomName` 回写）→ 初版记 D「待核」，核定为 E |
| `Views/MainWindow.axaml.cs:142` | `await dialog.ShowDialog<bool>(this)` | B | ①密码载荷但不外泄 ③补读 `Password`/`RememberInSession`/`SavePermanently`/`Description`/`Patterns`，**就地**封装为 `PasswordDialogResponse` |
| `Views/MainWindow.axaml.cs:419` | `await dialog.ShowDialog<bool>(this)` | B | ③补读 `dialog.Comment`，就地转 `string?` 返回 |
| `Controls/FileFilterEditor.axaml.cs:682` | `await dialog.ShowDialog<bool?>(window)` | B | ③补读 `InputText`（重命名预设），就地 `RenamePresetRequested` |
| `Controls/FileFilterEditor.axaml.cs:710` | `await dialog.ShowDialog<bool?>(window)` | B | ③补读 `InputText` 作为方法返回值 |
| `Dialogs/CustomFilePickerDialog.axaml.cs:217` | `await dialog.ShowDialog(owner)`（bool 弃用） | B | ③补读 `SelectedPath`+`SelectedPreserveFullPath` → 不可变 `ExtractPickResult`；②空串按取消处理 |
| `Dialogs/CustomFilePickerDialog.axaml.cs:231` | `await dialog.ShowDialog<bool>(owner)` | B | ③补读 `SelectedPaths` → `IReadOnlyList<string>?` |
| `Dialogs/CustomFilePickerDialog.axaml.cs:244` | `await dialog.ShowDialog(owner)` | B | ③补读 `SelectedPath` → `string?`（私有工厂 `ShowInternal`） |
| `Dialogs/CustomFilePickerDialog.axaml.cs:1131` | `if (await dialog.ShowDialog<bool>(this))` | B | ③补读 `FavoriteName`/`FavoritePath`，就地 `FavoritePathManager.Add` |
| `Dialogs/FavoriteManagerWindow.axaml.cs:105` | `if (await dialog.ShowDialog<bool>(this))` | B | ③补读 `FavoriteName`/`FavoritePath`，就地 Add + 重载列表 |
| `Dialogs/FavoriteManagerWindow.axaml.cs:118` | `if (await dialog.ShowDialog<bool>(this))` | B | ③同上，就地 `FavoritePathManager.Update` |
| `Dialogs/ExtractSettingsWindow.axaml.cs:93` | `await pwdDialog.ShowDialog<bool>(this)` | B | ③补读 5 个密码属性 → `PasswordDialogResponse`（与 `MainWindow.axaml.cs:142` 同构） |
| `ViewModels/SettingsWindowViewModel.cs:462` | `await dlg.ShowDialog<bool?>(ownerWindow)` | B | ③补读 `Extension` 就地写入设置；②无 owner 时提前 return（有 `DebugLog`） |
| `ViewModels/SettingsWindowViewModel.cs:2137` | `await dialog.ShowDialog<bool?>(...)` | B | ③补读 `FormatName`/`ExtensionsRaw`/`MagicHex` 就地建 `FormatDefinitionViewModel` |
| `ViewModels/SettingsWindowViewModel.cs:2158` | `await dialog.ShowDialog<bool?>(...)` | B | ③同上，就地回填 `fmt` |
| `ViewModels/SettingsWindowViewModel.cs:2180` | `await dialog.ShowDialog<bool?>(...)` | B | ③补读 `RuleName`/`SelectedLevel`/`SelectedFormatIds` 就地建规则 VM |
| `ViewModels/SettingsWindowViewModel.cs:2201` | `await dialog.ShowDialog<bool?>(...)` | B | ③同上，就地回填 `rule` |
| `Controls/Menus/TestMenuView.axaml.cs:107` | `window.Show()` | D | 无 owner 时降级为非模态（注释明示「不静默丢弃」）；Debug 专用诊断，无载荷/无补读。**若将来把诊断窗纳入守卫，则升 A** |
| `Controls/Menus/TestMenuView.axaml.cs:111` | `await window.ShowDialog(owner)` | D | ②Debug 专用诊断菜单**单点分发**：28 个窗口共用一个 `TestWindow_Click` + tag switch，无载荷/无补读。已核实 `TestMenuView.axaml:116/123` 的「压缩设置 / 解压设置」两项同挂 `TestWindow_Click`、同样落入该 switch → 与其余项**走同一处 `:111` 分发**，故它们是 **A 类入口（宿主只是 Debug 菜单）**，接线时同样必须改成新契约，不得因「只是诊断项」放过。**若将来把诊断窗纳入守卫，则升 A** |
| `App.CliInfra.cs:196` | `result = await elevationDlg.ShowDialog<bool?>(owner)` | D | ②结果决定是否提权重启；③无补读 |
| `App.CliInfra.cs:203` | `await ... ShowDialog<bool?>(tempOwner)` | D | CLI 无主窗时临时 owner；同上 |
| `Dialogs/AppMessageBox.axaml.cs:161` | `return await dialog.ShowDialog<MessageBoxResult>(owner)` | D | 结果作为值返回调用方；③无补读 |
| `Dialogs/AppMessageBox.axaml.cs:167` | `return await dialog.ShowDialog<MessageBoxResult>(activeWindow)` | D | 同上（活跃窗兜底） |
| `Dialogs/AppMessageBox.axaml.cs:186` | `await dialog.ShowDialog<MessageBoxResult>(owner)`（丢弃） | D | ②「打开日志目录」动作在窗内执行，结果无人需要 |
| `Dialogs/AppMessageBox.axaml.cs:193` | `await dialog.ShowDialog<MessageBoxResult>(activeWindow)`（丢弃） | D | 同上 |
| `Views/MainWindow.axaml.cs:133` | `await dialog.ShowDialog(this)` | C | 设置窗自行保存，无返回值/无补读 |
| `Views/MainWindow.axaml.cs:163` | `await dialog.ShowDialog<bool>(this)` | C | 已匹配密码查看窗，bool 被丢弃 |
| `Views/MainWindow.axaml.cs:272` | `await dialog.ShowDialog(this)` | C | 密码管理器 |
| `Views/MainWindow.axaml.cs:278` | `await dialog.ShowDialog(this)` | C | 关于窗 |
| `Views/MainWindow.axaml.cs:284` | `await dialog.ShowDialog(this)` | C | 赞助窗 |
| `Views/MainWindow.axaml.cs:290` | `await dialog.ShowDialog(this)` | C | 收藏管理器 |
| `Views/SettingsWindow.axaml.cs:31` | `dlg.ShowDialog(this)`（未 await） | C | 并行解压帮助窗 |
| `Views/SettingsWindow.axaml.cs:132` | `dialog.ShowDialog(this)`（未 await） | C | 自适应规则帮助窗 |
| `Dialogs/PasswordManagerWindow.axaml.cs:428` | `dialog.ShowDialog(this)`（未 await） | C | 密码帮助窗；即发即忘，结果无人消费 |
| `App.CliInfra.cs:172` | `await dlg.ShowDialog<bool?>(owner)` | C | 已提权仍失败提示窗，结果丢弃 |
| `App.CliInfra.cs:174` | `dlg.Show()` | C | 同上，无 owner 兜底（同一提示接缝的降级形态，并入该文件基线计数） |
| `App.CliInfra.cs:185` | `await infoDlg.ShowDialog<bool?>(owner)` | C | 不允许提权时的说明窗，结果丢弃 |
| `App.CliInfra.cs:187` | `infoDlg.Show()` | C | 同上，无 owner 兜底（同一接缝的降级形态） |
| `App.CliInfra.cs:218` | `await infoDlg.ShowDialog<bool?>(owner)` | C | 用户拒绝提权后的说明窗，结果丢弃 |
| `App.CliInfra.cs:220` | `infoDlg.Show()` | C | 同上，无 owner 兜底（同一接缝的降级形态） |

### 守卫范围外：非模态辅助窗（12 条，不进 C 基线）

> 这些是**非对话框**的辅助窗：用 `Show()` 独立挂在窗口之外显示，无返回值、无 owner 接管、无属性补读，也没有「失败静默」语义（进度由窗内状态呈现、覆盖层只服务拖放命中判定、帮助窗纯阅读）。
> `dialog-seam` 守卫的扫描对象是 `ShowDialog`，**本清单不参与 C 类基线**，仅登记以免下次扫描把它们误判为「新增 C 类」。

| 文件:行 | 形态 | 说明 |
|---|---|---|
| `Views/MainWindow.axaml.cs:335` | `pw.Show()` | 压缩/解压进度窗（异常由 `catch` → `SetErrorSummary` 呈现，不走弹窗） |
| `Views/MainWindow.axaml.cs:1578` | `overlayWin.Show()` | 拖放隐形覆盖层（1×1 辅助窗，只做命中判定） |
| `App.CliCompress.cs:63` | `collectingWindow.Show()` | 「正在收集文件」纯文字等待窗 |
| `App.CliCompress.cs:236` | `progressWindow.Show()` | 批处理进度窗（IPC 收集期） |
| `App.CliCompress.cs:438` | `progressWindow.Show()` | 压缩进度窗 |
| `App.CliExtract.cs:405` | `progressWindow.Show()` | CLI 单包解压进度窗 |
| `App.CliExtract.cs:572` | `progressWindow.Show()` | CLI 多包解压进度窗 |
| `App.CliExtract.cs:761` | `progressWindow.Show()` | CLI 直解批处理进度窗 |
| `Services/ExtractFlow.cs:94` | `pw.Show()` | 选中条目解压进度窗 |
| `Dialogs/AppMessageBox.axaml.cs:170` | `dialog.Show()` | 无 owner 兜底，直接返回 `MessageBoxResult.None` |
| `Dialogs/AppMessageBox.axaml.cs:197` | `dialog.Show()` | 同上（`ShowWithAction` 兜底） |
| `ViewModels/SettingsWindowViewModel.cs:2052` | `dialog.Show()` | 日志隐私帮助窗（纯阅读） |

### C 类存量基线（守卫白名单，**15 条 / 4 文件**）

> 用途：`dialog-seam` 守卫对 C 类只拦「新增」，这是**存量豁免基线**，核对口径为「当前条数 ≤ 基线条数」。
> 计数单位是**提示接缝**：同一提示窗的 `ShowDialog` 形态与其无 owner 降级 `Show()` 形态算一处（如 `App.CliInfra.cs` 的 172/174 为同一接缝）。守卫实际只扫 `ShowDialog`（其中 C 类 12 条），3 处降级 `Show()` 一并计入基线以免下次扫描时漏判。

| 文件（相对 `src/MantisZip.UI.Avalonia/`） | 条数 | 命中行 |
|---|---|---|
| `Views/MainWindow.axaml.cs` | 6 | 133、163、272、278、284、290 |
| `App.CliInfra.cs` | 6 | 172/174、185/187、218/220（3 处接缝 × 2 形态） |
| `Views/SettingsWindow.axaml.cs` | 2 | 31、132 |
| `Dialogs/PasswordManagerWindow.axaml.cs` | 1 | 428 |
| **合计** | **15** | 守卫扫描内 12 条 + 降级 `Show` 3 条 |

### 与初版清单的差异

1. **计数口径**：初版「68 处」含注释行与 `MessageBox` 噪音，且沿用了 WPF 时代命名。现仓库已无原生 `MessageBox`，消息框统一为 `AppMessageBox.Show(...)`（带参 → 不命中 `.Show()` 正则），故噪音源消失。2026-10-26 实测原始匹配 69 行、真实调用点 **60 处**（`ShowDialog` 41 + `Show()` 19）；另剔除 1 处非窗口的静态辅助调用 `Program.cs:24` `NativeSplash.Show(...)`（带参重载，不是 `Window.Show`）。
2. **初版 A 类中的「入口」不是命中点**：`ViewModels/MainWindowViewModel.Compress.cs:25-34`（`NewArchive`）、`:42-50`（`DragInCompressAsync`）、`ViewModels/MainWindowViewModel.Extract.cs:30`（`ExtractArchive`）、`:312`（`ExtractTo`）经 `ShowCompressSettingsDialog` / `ShowExtractSettingsDialog` 委托转发，真实 `ShowDialog` 在 `Views/MainWindow.axaml.cs:234/252`。它们仍在 T11 入口清单内，但**不构成 `ShowDialog` 命中**，已从本表移出；其「委托返回后补读 `vm` 属性」（`Compress.cs:31/47`、`Extract.cs:32/34-40/317/323`）属类型学②载荷旁路，由 T12 处理。
3. **`Controls/Menus/TestMenuView.axaml.cs` 结构变了**：初版记 `:81`/`:82`（压缩/解压设置窗两处 A）+ `:97`（C）；现为**单点分发**——`:111` 一次 `ShowDialog(owner)` 覆盖全部 28 个诊断窗口，`:107` 无 owner 时降级 `Show()`，两者统一归 **D**（Debug 专用）。
4. **行号漂移**（均已按当前工作区核对）：`Views/MainWindow.axaml.cs:414→419`（CommentDialog）、`:330→335`（ProgressWindow）、`:1573→1578`（拖放覆盖层）、`:234`/`:252` 未变；`Services/CompressFlow.cs:131-143→:141`；`App.CliCompress.cs:155`、`App.CliExtract.cs:353` 未变。
5. **判定调整（10 处 D→B + 1 处 D→F）**：初版把「有返回值且已被消费」一律记 D；按判据③复核后，其中 10 处同时存在**属性补读**（执行/持久化载荷从窗口公开属性取出），应归 B：`SettingsWindowViewModel.cs:462/2137/2158/2180/2201`、`FileFilterEditor.axaml.cs:682/710`、`FavoriteManagerWindow.axaml.cs:105/118`、`ExtractSettingsWindow.axaml.cs:93`。另有 `PasswordRetryLoop.cs:139` 先复核为 B、再经父 agent 裁定改判 **F**（与同方法 `:144` 同族，见末节裁定 1）。D 类从初版 17 处降到 **8 处**。
6. **`Services/ExtractFlow.cs:301` 定级**：初版记 D「待核」；实测为 `await dlg.ShowDialog(...)` 后五处属性回读（`IsPaused`/`CancelOperation`/`ResultAction`/`CustomName`/`ApplyToAll`）+ 模态冻结，与 `CompressFlow.cs:280` 完全同族 → 归 **E**，T15 一并改。
7. **初版未列、本次补登两处**：`ViewModels/SettingsWindowViewModel.cs:2052`（`OpenLogPrivacyHelp` 的非模态 `Show()`，按裁定 3 归「守卫范围外」）；`Dialogs/PasswordManagerWindow.axaml.cs:428`（**未 await** 的即发即忘帮助窗，C——它连 Task 都没人持有，守卫按「无返回值 `ShowDialog` 调用」识别）。
8. **F 类口径收紧（4 处）**：初版 F 列三处非模态 `Show()`，既与 A 类重复计数（`App.CliCompress.cs:155`、`App.CliExtract.cs:353` 同时出现在 A 与 F），又漏了 `PasswordRetryLoop.cs:139`。本次每处**只归一类**：A 只保留两处模态 `ShowDialog`（`Views/MainWindow.axaml.cs:234`、`:252`）；F = `App.CliCompress.cs:155`、`App.CliExtract.cs:353`、`Services/PasswordRetryLoop.cs:139`、`:144`（非模态设置/密码窗，载荷经窗口属性取出，随 T16 与新契约一并改）。

### 父 agent 裁定（2026-10-26）

三条争议由父 agent 拍板，附录已按裁定修正：

| # | 条目 | 裁定 | 理由与落地 |
|---|---|---|---|
| 1 | `Services/PasswordRetryLoop.cs:139` | B → **F** | `await ShowDialog<bool>` 后从 `dialog.Password` 属性补读载荷，正是子类②「载荷旁路」；且 `:144` 已在 §5 T16 列为 F，两者必须同族同判。已移入 F 块 → B 17→16、F 3→4 |
| 2 | `Controls/Menus/TestMenuView.axaml.cs:107`／`:111` | 维持 **D** | Debug 专用、Release 不可达；依据列已加「若将来把诊断窗纳入守卫，则升 A」。核实结论：`:111` 的单点分发**包含**「压缩设置 / 解压设置」两个诊断项（`TestMenuView.axaml:116/123` 同挂 `TestWindow_Click`、落入同一 tag switch）→ 二者属 **A 类入口**（宿主只是 Debug 菜单），接线时同样必须改成新契约 |
| 3 | 进度窗 / 拖放覆盖层等非模态 `Show()` | 采「守卫只看 `ShowDialog`」口径 | 12 条移出 C 类，单列「守卫范围外：非模态辅助窗」一节且不进基线；C 基线 27→**15**，附录并注明「守卫的 C 类检查只针对 `ShowDialog`，非模态辅助窗不在扫描范围内」 |

裁定后的最终分布：**A 2 / B 16 / C 15 / D 8 / E 3 / F 4 = 48 条（逐点判定表）+ 12 条（守卫范围外）= 60 处**。
