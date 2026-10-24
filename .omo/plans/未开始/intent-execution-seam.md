# 意图-执行接缝收口（消灭「点了没反应」类 bug）

> **状态**: 📋 待实施 | **创建**: 2026-10-24 | **优先级**: P1 | **预估工时**: 18-24h（三波）
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

- [ ] **T13** 逐点判定附录 A 的 68 处命中，产出**逐点判定表**回填本附录（A 改 / B 参照 / C 登记 / D 安全 / E 单独治 / F 随 A）。判定标准：①是否携带载荷 ②失败是否静默 ③是否存在属性补读。
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

## 附录 A：68 处 `ShowDialog` / `Show()` 命中分级（初版，T13 逐点回填）

> 计数含注释行，实际调用点约 60。判定标准：①是否携带载荷 ②失败是否静默 ③是否存在属性补读。

**A 类 —— 设置窗口族，本计划核心改造对象（9 入口 + 2 接线块）**

`ViewModels/MainWindowViewModel.Compress.cs:25-34`（NewArchive）、`:42-50`（DragInCompressAsync）、`App.CliCompress.cs:155`（`dlg.Show()`）、`Controls/Menus/TestMenuView.axaml.cs:81`；`ViewModels/MainWindowViewModel.Extract.cs:30`、`:312`、`Views/MainWindow.axaml.cs:234`、`App.CliExtract.cs:353`、`Controls/Menus/TestMenuView.axaml.cs:82`；接线块 `Views/MainWindow.axaml.cs:225-244`、`:249-267`

**B 类 —— 已封装为 DTO 工厂（安全，作为目标形态参照，不改）**

`Dialogs/CustomFilePickerDialog.axaml.cs:217`、`:231`、`:244`、`:1131`（5 个静态入口内部 discard bool，但工厂返回 `ExtractPickResult` / 路径列表）；`Views/MainWindow.axaml.cs:142`（PasswordDialog → 构造 `PasswordDialogResponse`）；`:414`（CommentDialog → `dialog.Comment`）

**C 类 —— 纯提示/帮助/管理器/进度窗口（无载荷；默认结果无人关心；按 T14 决定是否统一 API）**

`Views/MainWindow.axaml.cs:133`、`:163`、`:272`、`:278`、`:284`、`:290`、`:330`、`:1573`；`Views/SettingsWindow.axaml.cs:31`、`:132`；`Dialogs/PasswordManagerWindow.axaml.cs:428`；`ViewModels/SettingsWindowViewModel.cs:2052`；`Dialogs/AppMessageBox.axaml.cs:170`、`:197`；`Services/ExtractFlow.cs:94`；`App.CliCompress.cs:63`、`:236`、`:438`；`App.CliExtract.cs:405`、`:572`、`:761`；`App.CliInfra.cs:172`、`:174`、`:185`、`:187`、`:218`、`:220`；`Controls/Menus/TestMenuView.axaml.cs:97`

**D 类 —— 有返回值且已被消费（安全，仅登记）**

`Dialogs/AppMessageBox.axaml.cs:161`、`:167`、`:186`、`:193`；`App.CliInfra.cs:196`、`:203`；`Controls/FileFilterEditor.axaml.cs:682`、`:710`；`Dialogs/FavoriteManagerWindow.axaml.cs:105`、`:118`；`Dialogs/ExtractSettingsWindow.axaml.cs:93`；`ViewModels/SettingsWindowViewModel.cs:462`、`:2137`、`:2158`、`:2180`、`:2201`；`Services/PasswordRetryLoop.cs:139`；`Services/ExtractFlow.cs:301`（待核：密码弹窗结果取法）

**E 类 —— 阻断式接缝（T15 单独治）**

`Services/CompressFlow.cs:131-143`（双 sync-over-async + `dlg.ResultAction` 属性读）、`:280`（`CompressConflictDialog` 属性读，与 E 同族）

**F 类 —— 非模态 `Show()` + 属性读（随 A 统一）**

`App.CliCompress.cs:155`、`App.CliExtract.cs:353`、`Services/PasswordRetryLoop.cs:144`
