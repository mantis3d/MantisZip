# CLI 退出路径崩溃修复（主循环启动前 `desktop.Shutdown()` 收口）

> **状态**: ✅ 已实施（2026-10-18，1 处设计偏差 + 端到端取证见文末「执行结果」） | **创建**: 2026-10-18 | **优先级**: P1（崩溃 + 未处理异常） | **预估工时**: 2-3h（实际 ~1h）
> **来源**: 用户实测 `--help` 抛未处理异常（2026-10-18），要求单独立项修复
> **当前核实**: 根因已定位并**实测复现**（4 条命令逐条拿到同一堆栈与退出码），影响面 12 处提前退出分支

## 背景

控制台执行 `MantisZip.UI.Avalonia.exe --help`：帮助文本打印完成后，进程抛出**未处理异常**并以非零码退出（stderr 有完整堆栈）。因是 headless CLI 路径，GUI 使用完全无感，长期未被发现。

`%LOCALAPPDATA%\MantisZip\lifecycle.log` 显示这不是偶发，且**不止 `--help`**：

```
[2026-10-04 15:05:32.374] ── app start pid=16292 ... args=[...MantisZip.UI.Avalonia.dll --help]
[2026-10-04 15:05:32.392] DOMAIN EXCEPTION terminating=True InvalidOperationException: Cannot perform requested operation because the Dispatcher shut down
[2026-10-18 11:04:32.410] ── app start pid=11192 ... args=[...MantisZip.UI.Avalonia.dll --help]
[2026-10-18 11:04:32.420] DOMAIN EXCEPTION terminating=True InvalidOperationException: ...
[2026-10-18 11:05:43.614] ── app start pid=16300 ... args=[...MantisZip.UI.Avalonia.dll --help]
[2026-10-18 11:05:43.623] DOMAIN EXCEPTION terminating=True InvalidOperationException: ...
[2026-10-18 11:16:32.265] ── app start pid=4884 ... args=[...MantisZip.UI.Avalonia.dll --help]
[2026-10-18 11:16:32.277] DOMAIN EXCEPTION terminating=True InvalidOperationException: ...
```

## 根因（已取证）

### 实测堆栈（2026-10-18，`bin\Debug\net10.0\MantisZip.UI.Avalonia.exe --help`）

```
Unhandled exception. System.InvalidOperationException: Cannot perform requested operation because the Dispatcher shut down
   at Avalonia.Threading.Dispatcher.PushFrame(DispatcherFrame frame)
   at Avalonia.Threading.Dispatcher.MainLoop(CancellationToken cancellationToken)
   at Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime.StartCore(String[] args)
   at Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime.Start(String[] args)
   at Avalonia.ClassicDesktopStyleApplicationLifetimeExtensions.StartWithClassicDesktopLifetime(AppBuilder builder, String[] args, Action`1 lifetimeBuilder)
   at MantisZip.UI.Avalonia.Program.Main(String[] args) in ...\Program.cs:line 15
```

实测退出码 **-532462766**（= `0xE0434352`，CLR 未处理异常），stderr 有上面完整堆栈。

### 机制

1. `Program.Main` → `builder.StartWithClassicDesktopLifetime(args)`（`Program.cs:15`）
2. Avalonia lifetime `Setup()` 触发 `App.OnFrameworkInitializationCompleted()`；**此刻 Dispatcher 主循环尚未启动**，回调返回后 `StartCore()` 才执行 `Dispatcher.MainLoop()`
3. CLI 分发整段（`App.axaml.cs:204-361`）就在该回调内**同步**跑完；命中「提前退出」分支时同步调用 `desktop.Shutdown()`
4. `Shutdown()` 关掉 UI Dispatcher 并置位退出请求
5. 回调返回 → `StartCore()` → `Dispatcher.MainLoop()` → `PushFrame()` → **抛 `InvalidOperationException: ... the Dispatcher shut down`**
6. 主线程未处理异常 → `AppDomain.CurrentDomain.UnhandledException`（`terminating=True`）→ 进程以 `0xE0434352` 终止、堆栈打到 stderr

一句话：**在主循环启动之前调用 `desktop.Shutdown()` 必然崩**——`Shutdown` 拆掉 Dispatcher，紧接着 `MainLoop` 又要求 Dispatcher 活着。

### 旁证：同文件既有先例与同因注解

`HandleShellCommand`（`App.axaml.cs:1694-1698`）的文档注释已写明同一机制，并因此改用 `Environment.Exit(exitCode)`：

```
/// Uses Environment.Exit(0) instead of desktop.Shutdown() because these commands
/// run during OnFrameworkInitializationCompleted, before the Dispatcher main loop
/// starts — calling Shutdown() there causes InvalidOperationException.
```

即：`--install-shell` 等命令躲过了这个坑（且是逐个命令各写各的），其余 CLI 退出分支全部中招。本计划把「主循环前退出」做成**统一通道**，而不是继续按命令散落处理。

## 影响面（实测 4 条 + 同构推导 8 条）

### 已实测（本机 `bin\Debug\net10.0\MantisZip.UI.Avalonia.exe`，均 `ExitCode=-532462766` + 同一 stderr 堆栈 + lifecycle.log `DOMAIN EXCEPTION`）

| 命令行 | 代码位置 | 触发条件 |
|---|---|---|
| `--help` | `App.axaml.cs:390`（`AttachConsole` 成功的控制台分支） | 从 cmd/pwsh 调用 |
| `--extract-here C:\__nope__.zip` | `App.axaml.cs:276` | 无有效文件 |
| `--compress C:\__nope__.zip` | `App.axaml.cs:1946` | 无有效路径 |
| `--compress-quick C:\__nope__.zip` | `App.axaml.cs:2068` | 无有效路径 |

### 同构推导（同一同步段、代码形态与上表逐字相同，未逐条实测）

| 命令行 | 代码位置 | 触发条件 |
|---|---|---|
| `--extract-to-name` / `--extract-smart` 无有效文件 | `App.axaml.cs:289` / `:308` | ShellExt 传入的路径在进程启动前被删除/改名 |
| `--extract` 无有效文件 | `App.axaml.cs:1092`（`RunExtractDialogCliAsync` 首个 await 之前） | 同上 |
| `--compress` / `--compress-separate` / `--compress-combined` 的**非首实例**分支 | `App.axaml.cs:1991` / `:2155` / `:2237` | 资源管理器多选 N 个文件右键压缩 → 第 2..N 个进程走完 IPC 投递后退出 |
| `--compress-separate` / `--compress-combined` 无有效路径 | `App.axaml.cs:2110` / `:2204` | 路径全部失效 |

> ⚠️ **非首实例分支是日常路径**：多选 N 个文件压缩 = N-1 次未处理异常（首实例正常）。其严重度高于 `--help`。
> 本次未实测该分支：跑它会触发首实例副作用（弹出 `CollectingWindow` + 压缩设置窗口），故改列入「GUI 人工验收」清单。

> 除 `--help` 外的 11 条命令都不弹窗、不写注册表，因此自动化矩阵可放心逐条跑。

## 方案

### 方案 A（推荐）：统一漏斗 `AppShutdown.Request()` + 主循环前延后

新增 `Services/AppShutdown.cs`（~40 行），**全仓唯一的退出通道**：

```csharp
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 应用退出统一漏斗。
///
/// 背景（已取证）：Avalonia 的 OnFrameworkInitializationCompleted 在主循环启动**之前**执行，
/// CLI 分发正是在该回调内同步跑完的。此时同步调用 desktop.Shutdown() 会关掉 UI Dispatcher，
/// 随后 StartCore→MainLoop→PushFrame 抛
/// InvalidOperationException: Cannot perform requested operation because the Dispatcher shut down
/// → 主线程未处理异常 → 进程以 0xE0434352 终止、堆栈打到 stderr（12 条 CLI 退出路径中招）。
///
/// 故退出只能走这里：MarkFrameworkInitCompleted() 之前的调用一律 Dispatcher.UIThread.Post
/// 延后到主循环首轮泵消息执行；之后行为与直接 Shutdown() 完全一致（零语义变更）。
/// </summary>
internal static class AppShutdown
{
    private static bool _frameworkInitCompleted;   // 判据 = OnFrameworkInitializationCompleted 是否已返回

    /// <summary>由 App.OnFrameworkInitializationCompleted 在 base. 调用之后立即调用一次。</summary>
    public static void MarkFrameworkInitCompleted() => _frameworkInitCompleted = true;

    public static void Request(IControlledApplicationLifetime lifetime, int exitCode = 0)
    {
        if (!_frameworkInitCompleted)
        {
            Dispatcher.UIThread.Post(() => lifetime.Shutdown(exitCode));
            return;
        }
        lifetime.Shutdown(exitCode);
    }

    /// <summary>测试隔离（静态状态位）。</summary>
    internal static void ResetForTest() => _frameworkInitCompleted = false;
}
```

> ⚠️ **实施偏差（已落地）**：形参最终为 `Action<int>` 而**不是** `IControlledApplicationLifetime` —— Avalonia 12.1.2 的 `IApplicationLifetime` 在源码中被显式标注「This interface or abstract class is **-not- implementable by user code**」，fake lifetime 无法构造（编译实测 CS0535/CS0738），漏斗两条分支将失去单测。改用退出委托后调用点写 `AppShutdown.Request(desktop.Shutdown)`（方法组），测试传记录用 lambda。详见文末「执行结果」。

要点：

- **形参用 `IControlledApplicationLifetime`**（`IClassicDesktopStyleApplicationLifetime` 的基接口，`Shutdown(int)` 就声明在这个接口上，已核对 Avalonia 12.1.2 XML 文档）→ 调用点直接传 `desktop`；测试可用极小的 fake 实现，不必造完整 desktop lifetime。
- **判据是「回调是否已返回」而非「Dispatcher 是否活着」**：Avalonia 未公开「主循环是否已启动」的查询 API（`Dispatcher` 只有 `ShutdownStarted`/`ShutdownFinished` 事件）。而崩溃窗口与「`OnFrameworkInitializationCompleted` 未返回」逐字对齐——该回调内同步执行的一切（含 `_ = SomeAsyncCli(...)` 的同步段）都落在窗口内，之后的延续（dispatcher / 对话框回调）都落在窗口外。注释里写明依据。
- **状态位翻转点**：`App.OnFrameworkInitializationCompleted` 最后的 `base.OnFrameworkInitializationCompleted();` **之后**一行。置于之前会让整个 CLI 段退回旧行为（守卫测试锁定这个顺序）。

### 方案 B（备选，本计划不采纳）：只把 12 处提前退出分支改 `Environment.Exit(0)`

- 优点：改动最小；有 `HandleShellCommand` 先例。
- 不采纳原因：① 必须**穷举**同步段内全部退出点，漏一个（`--extract` 零路径、非首实例分支、`RunExtractCliAsync` 同步完成等）就仍然崩；② 跳过 Avalonia 正常退出链（`Program.Main` 的 `StartupTimer.Flush("app-exit")` 不再执行、启动打点丢失）；③ 语义混用——后续新增 CLI 分支极易重犯。
- 若用户偏好「最小改动」，本题作为可切换方案保留：改动点 = 上表 12 处 `desktop.Shutdown()` → `Environment.Exit(0)`。

### 方案 C（已否决）：把整个 CLI 分发 Post 到主循环之后执行

影响全部 CLI 路径的时序（含正常启动建窗），风险远大于收益；本次只修「退出」这一个已知崩点。

### 方案 D（已否决）：`Program.Main` 加 catch-all 兜底

把未处理异常吞成「日志 + 退出码」会掩盖其它真实崩溃，且不修根因。

## 任务拆分

### Task 1 — 新增 `AppShutdown` 漏斗

`src/MantisZip.UI.Avalonia/Services/AppShutdown.cs`（新建），内容见方案 A。

> ⚠️ 实施偏差：`Request` 形参为 `Action<int> shutdown`（非 `IControlledApplicationLifetime`），原因见上文偏差说明。

### Task 2 — `App.axaml.cs` 39 处退出点收口

- `desktop.Shutdown()` → `AppShutdown.Request(desktop)`（逐条机械替换；行号清单见「涉及文件」）。
- 文件末尾 `base.OnFrameworkInitializationCompleted();` **之后**加 `AppShutdown.MarkFrameworkInitCompleted();`。
- `HandleShellCommand` 的 `Environment.Exit(exitCode)` **保持不变**（headless、显式退出码、不依赖 Avalonia 生命周期）。
- ⚠️ 第 1696 行是 `HandleShellCommand` 文档注释里的 `desktop.Shutdown()` 文字，**不要**改。

### Task 3 — `MainWindowViewModel.Exit` 收口（1 处）

`ViewModels/MainWindowViewModel.cs:3083` 的 `desktop.Shutdown()` → `AppShutdown.Request(desktop)`。
行为不变（该命令在主循环内调用），收益是让「唯一退出通道」成为全仓不变式——守卫测试才有意义。

### Task 4 — 架构守卫测试（源码文本级）

`tests/MantisZip.UI.Avalonia.Tests/AppShutdownWiringTests.cs`（新建）。

- 复用先例设施：`ExtractPreserveFullPathWiringTests.GetRepoRoot()`（从 `AppContext.BaseDirectory` 向上找 `src`）+ 空白归一 + `TrySkipNonCode`（跳过注释/字符串，避免把 `HandleShellCommand` 注释里的 `desktop.Shutdown()` 与文档误判为调用）。
- 断言：
  1. `App.axaml.cs` 与 `MainWindowViewModel.cs` 中不存在**裸** `desktop.Shutdown(` 调用。
  2. `App.axaml.cs` 中 `AppShutdown.MarkFrameworkInitCompleted()` 的**文本位置在** `base.OnFrameworkInitializationCompleted()` **之后**（防有人把状态位翻转提前 → 整个 CLI 段退回崩溃行为）。
  3. `App.axaml.cs` 中 `AppShutdown.Request(` 出现次数 ≥ 30（下限断言，防有人批量改回 `desktop.Shutdown()`；不绑死具体行号，避免每次移动行就假失败）。

### Task 5 — 漏斗行为单测

`tests/MantisZip.UI.Avalonia.Tests/AppShutdownTests.cs`（新建）。

- 每个用例开头 `AppShutdown.ResetForTest()`（静态状态位隔离）。
- ~~fake `IControlledApplicationLifetime`~~ → **实施偏差**：fake lifetime 不可行（`IApplicationLifetime` 被标注不可实现），改为一个 `ShutdownProbe` 类（`void Shutdown(int)` 记录次数与退出码）直接作为 `Action<int>` 传入。
- 用例：
  1. **主循环前**（未 `MarkFrameworkInitCompleted()`）调 `Request(fake)` → 立即 `ShutdownCalls == 0`；`Dispatcher.UIThread.RunJobs()` 泵一轮后 → `ShutdownCalls == 1` 且退出码 0。
  2. **主循环后**（先 `MarkFrameworkInitCompleted()`）调 `Request(fake)` → **立即** `ShutdownCalls == 1`（不需要泵消息），与现状一致。
  3. 退出码透传：`Request(fake, 3)` → 退出码 3。
- 若 fake 因接口成员过多导致文件臃肿，可只保留用例 1/2 并在文件头注释说明降级原因（诚实优先）。

### Task 6 — 端到端退出码矩阵（自动化取证）

```powershell
$exe = "src\MantisZip.UI.Avalonia\bin\Debug\net10.0\MantisZip.UI.Avalonia.exe"
$cases = @(
  @('--help'),
  @('--extract-here','C:\__nope__.zip'),
  @('--extract-to-name','C:\__nope__.zip'),
  @('--extract-smart','C:\__nope__.zip'),
  @('--extract','C:\__nope__.zip'),
  @('--compress','C:\__nope__.zip'),
  @('--compress-quick','C:\__nope__.zip'),
  @('--compress-separate','C:\__nope__.zip'),
  @('--compress-combined','C:\__nope__.zip')
)
foreach ($c in $cases) {
  $so = "$env:TEMP\mz-exit-out.txt"; $se = "$env:TEMP\mz-exit-err.txt"
  Remove-Item $so,$se -ErrorAction SilentlyContinue
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = Start-Process -FilePath $exe -ArgumentList $c -NoNewWindow -RedirectStandardOutput $so -RedirectStandardError $se -PassThru
  $ok = $p.WaitForExit(30000); $sw.Stop()
  "{0,-45} ExitCode={1} elapsed={2}ms stderr={3}B" -f ($c -join ' '), $(if($ok){$p.ExitCode}else{'TIMEOUT'}), $sw.ElapsedMilliseconds, (Get-Item $se).Length
}
```

期望：9 条全部 `ExitCode=0`、`stderr=0B`；且 `lifecycle.log` 无新增 `DOMAIN EXCEPTION`、`startup-trace.log` 每条新增一行 `reason=app-exit`（证明走的是「MainLoop 正常返回 → Main 收尾」，不是硬杀）。

成功路径抽样（1 条，GUI 会闪现进度窗并自动关闭）：

```powershell
# 临时目录造小 zip → --extract-here 解压 → 断言退出码 0 + 文件落地 + stderr 为空
```

### Task 7 — 文档（规则 3）

- `docs/progress-avalonia-detail.md` 的 `**2026-10-18**` 段：新增条目（根因 + 漏斗 + 测试）。
- `docs/PROGRESS.md` 的 `#### 2026-10`（Avalonia 版）：里程碑一行（未处理异常崩溃属重大 bug 修复）。

## 涉及文件

| 文件 | 改动 |
|---|---|
| `src/MantisZip.UI.Avalonia/Services/AppShutdown.cs` | 新建（~40 行） |
| `src/MantisZip.UI.Avalonia/App.axaml.cs` | 39 处 `desktop.Shutdown()` → `AppShutdown.Request(desktop)`（行号：276, 289, 308, 390, 396, 410, 1062, 1075, 1092, 1114, 1125, 1136, 1459, 1474, 1483, 1662, 1677, 1686, 1863, 1946, 1984, 1991, 2000, 2016, 2031, 2068, 2110, 2148, 2155, 2163, 2204, 2230, 2237, 2245, 2307, 2396, 2412, 2420, 2427）+ 末尾 1 行 `MarkFrameworkInitCompleted()`；第 1696 行为注释，不改 |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | 1 处（:3083） |
| `tests/MantisZip.UI.Avalonia.Tests/AppShutdownWiringTests.cs` | 新建（3 用例） |
| `tests/MantisZip.UI.Avalonia.Tests/AppShutdownTests.cs` | 新建（2-3 用例） |
| `docs/progress-avalonia-detail.md`、`docs/PROGRESS.md` | 进度条目 |

## 验证

### 基线（本次已实测，2026-10-18，修复前）

| 命令行 | ExitCode | stderr | lifecycle.log |
|---|---|---|---|
| `--help` | -532462766 | 完整堆栈 | `DOMAIN EXCEPTION` |
| `--extract-here C:\__nope__.zip` | -532462766 | 完整堆栈 | `DOMAIN EXCEPTION` |
| `--compress C:\__nope__.zip` | -532462766 | 完整堆栈 | `DOMAIN EXCEPTION` |
| `--compress-quick C:\__nope__.zip` | -532462766 | 完整堆栈 | `DOMAIN EXCEPTION` |

（对照参考：spike 单点验证「主循环前改 Post」后 → `ExitCode=0`、stderr 空、`startup-trace.log` 出现 `reason=app-exit`、耗时 650ms。）

### 修复后门禁（全部必须通过）

1. `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 警告 0 错误。
2. `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 新增用例全过、无新增失败（注意规则 17 的增量构建 XAML 毒化，必要时 `--no-incremental` 全量重建）。
3. Task 6 矩阵 9 条全部 `ExitCode=0`、stderr 为空。
4. 矩阵跑完后 `lifecycle.log` 无新增 `DOMAIN EXCEPTION`；`startup-trace.log` 每条都有对应 `reason=app-exit`。
5. 成功路径抽样通过（临时 zip 解压落地、退出码 0、无 stderr）。
6. 矩阵**不得**包含 `--install-shell` / `--uninstall-shell` / `--install-assoc` / `--uninstall-assoc`（它们会真改 HKCU；且走 `Environment.Exit`，不在本次改动范围）。
7. 收口后立即 `git status` 确认无意外文件（本计划只应新增 2 个测试文件 + 1 个服务文件）。

### GUI 人工验收（代码侧无法代跑）

1. 资源管理器多选 2-3 个文件 → 右键「压缩到独立的…」→ 只出现 1 个压缩设置窗口，其余进程静默退出；`lifecycle.log` 无 `DOMAIN EXCEPTION`。
2. 同法验证「压缩到（父目录名）」「压缩……」（对应 `--compress-combined` / `--compress` 的非首实例分支）。
3. 右键压缩包「原地解压」前先删除/改名该文件（让路径失效）→ 进程静默退出，无崩溃、无残留窗口。
4. 控制台 `MantisZip.UI.Avalonia.exe --help` → 中文帮助文本完整、无堆栈、`$LASTEXITCODE -eq 0`。
5. 主窗口菜单「退出」与标题栏关闭按钮行为不变（后者仍走 `OnLastWindowClose` 隐式退出，不经本漏斗）。

## 边界

- **不改退出码语义**：所有 CLI 路径统一 0（与现状一致；`--compress-*` IPC 投递方与 ShellExt 都不读退出码）。为「无有效路径」引入非零退出码属独立议题，需单独决策。
- **不改** `HandleShellCommand` 的 `Environment.Exit(exitCode)`（headless 命令、显式退出码）。
- 无新增用户可见文案 → 不涉及本地化（规则 13 不触发）；无 UI 控件新增（规则 4/7/14 不触发）。
- 不依赖 Avalonia 内部 API：只用公开的 `Dispatcher.UIThread.Post` / `Shutdown(int)`。
- 状态位只描述「`OnFrameworkInitializationCompleted` 已返回」，对正常 UI 启动路径零影响（其退出调用都发生在主循环内）。

## 风险与局限

| 风险 | 说明 | 处置 |
|---|---|---|
| 静态状态位串味 | `_frameworkInitCompleted` 是静态字段 | 每个测试用例先 `ResetForTest()` |
| 39 处机械替换引入笔误 | 一次性改 39 行 | 构建 + 守卫测试（裸调用清零）+ 退出码矩阵三重兜底 |
| 延后 = 多活一轮消息泵 | 实测 `--help` 端到端 650ms（含 Avalonia 启动），无感知 | 矩阵中记录每条耗时 |
| 判据是「等价推断」而非 API 查询 | Avalonia 未公开「主循环是否启动」的查询 API | 判据与崩溃窗口逐字对齐，并在源码注释写明依据与失效条件 |
| 非首实例分支未实测 | 触发会弹首实例窗口（副作用） | 列入 GUI 人工验收（本计划 §验证） |
| 未来若有人把 `MarkFrameworkInitCompleted()` 提前 | 整个 CLI 段退回崩溃行为 | 守卫测试断言 2 锁定文本顺序 |

## 执行流程

1. 计划移入 `.omo/plans/进行中/` 后开工。
2. Task 1 → 构建；Task 2/3 → 构建；Task 4/5 → 跑测试；Task 6 → 取证。
3. 更新进度文档（规则 3）→ commit（`fix(avalonia): ...`，conventional commits，规则 10）。
4. 计划移入 `.omo/plans/已完成/`；`docs/PLAN.md` 本行删除、条目移入 `docs/PROGRESS.md`【历史设计方案索引】（规则 1）。

## 环境事实（2026-10-18）

- Avalonia **12.1.2**、`net10.0`、Windows + PowerShell 7。
- 复现二进制：`src\MantisZip.UI.Avalonia\bin\Debug\net10.0\MantisZip.UI.Avalonia.exe`（本次勘察中的临时改动已 `git checkout` 还原并重建，工作树干净）。
- 基线日志：`%LOCALAPPDATA%\MantisZip\lifecycle.log`（异常序列）、`startup-trace.log`（`reason=first-frame|app-exit`，可判定是否走正常退出链）。
- `--help` 帮助文本本身正常（GBK 中文，捕获文件 0 处 `0x3F` 替换符）——编码不是本次问题。

## 执行结果（2026-10-18 实施，同日收口）

### 交付物

| 文件 | 状态 |
|---|---|
| `src/MantisZip.UI.Avalonia/Services/AppShutdown.cs` | 新建（唯一退出漏斗；`Request(Action<int> shutdown, int exitCode = 0)` + `MarkFrameworkInitCompleted()` + `ResetForTest()`） |
| `src/MantisZip.UI.Avalonia/App.axaml.cs` | 39 处 `desktop.Shutdown()` → `AppShutdown.Request(desktop.Shutdown)`；末尾 `base.OnFrameworkInitializationCompleted();` 之后加 `AppShutdown.MarkFrameworkInitCompleted();`；`HandleShellCommand` 注释同步更新（仍用 `Environment.Exit(exitCode)`） |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | 1 处（`Exit` 命令） |
| `tests/MantisZip.UI.Avalonia.Tests/AppShutdownWiringTests.cs` | 新建，4 条源码文本级守卫 |
| `tests/MantisZip.UI.Avalonia.Tests/AppShutdownTests.cs` | 新建，3 条行为用例 |

### 唯一设计偏差

原计划漏斗形参为 `IControlledApplicationLifetime`（为的是能用 fake lifetime 写单测）。**编译实测**：Avalonia 12.1.2 的 `IApplicationLifetime` 被源码显式标注「This interface or abstract class is **-not- implementable by user code**」→ `CS0535` + `CS0738`（`Shutdown(int)` 在 Avalonia 12 返回 `void`，且接口本身禁止外部实现）。fake 不可行 ⇒ 改为接收退出委托 `Action<int>`，调用点 `AppShutdown.Request(desktop.Shutdown)`（方法组天然匹配）。**结论**：漏斗语义不变，且两条分支反而变得可精确单测（原计划在此点不可实现）。

### 验证证据

| 门禁 | 结果 |
|---|---|
| 主项目构建 | **0 警告 0 错误**（`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`） |
| 新增用例 | `--filter FullyQualifiedName~AppShutdown` → **7 通过 / 0 失败** |
| 测试项目构建 | 0 错误（24 个告警全部为既有文件中的 xUnit 分析器提示，新增 2 个文件零告警） |
| 全量测试 | **306 总 / 300 过 / 3 跳过 / 3 失败**；3 条失败全在 `WholeArchiveErrorLocalizerTests`，均为「找不到文件：…\tests\corpus\corrupt-archives\…」——该仓库**从未跟踪** `corrupt-01-truncated-tail.zip` / `enc-01-zip-aes.zip` / `edge-02-zip-slip.zip`（`git ls-files` 实测），属**预存环境缺口** |
| 退出码矩阵（9 条） | **全部 `ExitCode=0`、stderr 0 字节**（569–1557ms）：`--help` / `--extract-here` / `--extract-to-name` / `--extract-smart` / `--extract` / `--compress` / `--compress-quick` / `--compress-separate` / `--compress-combined`（后 8 条传失效路径） |
| 未处理异常 | `lifecycle.log` 的 `DOMAIN EXCEPTION` 计数 **14 → 14**（无新增） |
| 正常退出链 | `startup-trace.log` 该时段恰好 **9 条 `reason=app-exit`**（= `Program.Main` 第 17 行 `StartupTimer.Flush("app-exit")`，证明 `MainLoop` 正常返回而非硬杀） |
| 成功路径抽样 | 临时目录 `Compress-Archive` 造包 → `--extract-here` → `ExitCode=0`、文件解压落地、stdout `Extracted: …`；stderr 仅既有 libpng `iCCP` 告警（Skia 资源既有噪声） |

### 未自动验证（GUI 人工验收）

三个 `--compress-*` 的**非首实例分支**（触发会弹首实例的 `CollectingWindow` + 压缩设置窗口，自动化会污染桌面），以及三条右键压缩命令的实机行为 —— 清单同本计划「验证 → GUI 人工验收」。

### 收口时点说明

按本仓惯例（上一计划 `file-assoc-persistence-fix` 亦如此），计划在**代码侧完成 + 自动化取证齐全**后即收口归档，GUI 人工验收项不阻塞归档。

