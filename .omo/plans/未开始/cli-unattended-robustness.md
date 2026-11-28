# Avalonia CLI 无人值守健壮性：挂死修复 + 真实退出码

> **状态**: 📋 待实施（2026-11-27 按代码现状重写 + 判定方式定案；2026-11-28 探针实测修正判定代码） | **创建**: 2026-11-22 | **优先级**: P0 | **预估工时**: 12-16h
> **来源**: 2026-11-21/22 CLI 独立 exe 调研的实测结论（`qa_test_temp/cli-hang-probe.ps1`、`cli-redirect-probe.ps1`）
> **当前核实**: 2026-11-22 实测确认 `--extract-here` 在「损坏包 / 截断包 / 加密包无密码 / 同名冲突」四种情形下**永久挂起**（>12s 强杀），成功与失败路径的进程退出码**恒为 0**。2026-11-27 对 HEAD `9e863633` 复核：压钉链与退出码现状**未变**，但 `App.CliExtract.cs` 已增到 1046 行、新增了根策略（`stripRoot` / `addRoot`）与 `ExtractOutcomeTracker`，本文件已按新现状改写（见 §现状核实-4）。
> **2026-11-27 定案**：无人值守判定改为**「父控制台自动判定 + 显式参数覆盖」**，**不加设置项**、不改注册表（见 §0.5）。理由是右键菜单用户人在现场，不能因为走的是 CLI 路径就把他原本能用的冲突框 / 密码框 / UAC 全部关掉。
> **2026-11-28 探针实测修正**：临时 WinExe 探针证明 `GetConsoleProcessList` 在未 `AttachConsole` 时**恒返回 0**（有父控制台的 pwsh 宿主也是 0），所以「先 `GetConsoleWindow` 再数进程」的判定恒假。正确探针必须是 **先 `AttachConsole(ATTACH_PARENT_PROCESS)` 成功再数进程（count > 1）**，随后 `FreeConsole()`。§0 与 §0.5 的代码已按实测结果改写。

## 定位约定（本文件不写死行号）

2026-11-22 版计划里的行号在 5 天内全部漂移（`App.CliExtract.cs` 1023 → 1046 行）。本版一律用**函数名 + 唯一子串**定位；确需行号的地方注明「2026-11-27 核对」并只作为参考。

## 背景

项目对外宣称 CLI「主要用于 Shell 右键菜单集成和自动化脚本」（`docs/CLI.md` 开头），但实测这两件事目前只兑现了前者：

1. **会挂死**。CLI 的进度窗口在出现错误时被自动「压钉」（`KeepOpenOnComplete = true`），随后进入无限等待人工关闭。GUI 里这是对的——让用户看清错误；无人值守时就是进程永不退出，脚本卡死、CI 超时。
2. **没有退出码**。40 处 `AppShutdown.Request(desktop.Shutdown)` 全部走默认 `exitCode = 0`，成功与失败在脚本侧无法区分。

这两条与「是否拆独立 CLI exe」无关：拆不拆 exe 是分发形态，挂死和退出码是**行为正确性**。本计划只修行为，不动工程结构。

## 现状核实（2026-11-27）

### 1. 挂死的根因链

| 环节 | 定位 | 行为 |
|---|---|---|
| 压钉 | `ViewModels/ProgressViewModel.cs` 的 `partial void OnHasErrorsChanged(bool value)` | `if (value) { KeepOpenOnComplete = true; StatusKind = Failed; }`，**单向，没有任何地方解钉** |
| 无限等 | `Dialogs/ProgressWindow.axaml.cs` 的 `AutoCloseOrWaitAsync(Action closeAction)` 开头 `if (_vm.KeepOpenOnComplete)` | `await WaitForManualCloseAsync()` |
| 等待实现 | 同文件 `private async Task<ManualWaitExit> WaitForManualCloseAsync()` | 只在 `IsVisible == false` 或 `KeepOpenOnComplete == false` 时返回 ⇒ 窗口可见 + 已压钉 = 永不返回 |
| 三个挂死点 | `App.CliExtract.cs` 单文件失败出口、批处理失败出口、`Services/CliProgressPresenter.cs` 的 `catch (Exception ex)` 分支 | 分别对应单文件解压失败、批处理失败、压缩异常 |

`KeepOpenOnComplete` 全仓只有 5 处写入：字段定义、`OnKeepOpenOnCompleteChanged`、`OnHasErrorsChanged` 内、`OnIsTestFlowChanged` 内、`ProgressViewModel` 里一处 `KeepOpenOnComplete = true;`（约 :2196）。**没有解钉路径。**

### 2. 实测的四种挂死情形（2026-11-22，便携模式隔离）

| 场景 | 结果 | stderr |
|---|---|---|
| 损坏 zip（扩展名合法、内容坏） | HUNG >12s | `Extraction failed: Failed to locate the Zip Header` |
| 截断 zip（真包截到 60%） | HUNG >12s | 同上 |
| 7z 加密包 + 无保存密码 | HUNG >12s | 密码窗在前台弹出（无人点） |
| 同名冲突（`FileConflictAction=ask`） | HUNG >12s | 冲突窗在前台弹出（无人点） |

### 3. 退出码现状

- `Services/AppShutdown.cs`：`public static void Request(Action<int> shutdown, int exitCode = 0)` —— **漏斗本身已支持传码**（两分支都 `shutdown(exitCode)` 透传），只是没有调用方传。
- `AppShutdown.Request(` 全仓 40 处，**零处传非零值**。
- 唯一的例外是 shell 命令分支 `App.CliInfra.cs` 的 `HandleShellCommand`（未知命令 / 异常 `exitCode = 1`，末尾 `Environment.Exit(exitCode)`）。这条路径已经正确，**不动**。
- Avalonia 退出码通道（查 `~/.nuget/packages/avalonia/12.1.2/lib/net10.0/Avalonia.Controls.xml`）：`IControlledApplicationLifetime.Shutdown(System.Int32)` 文档原文「sets the exit code that is returned to the operating system when the application exits」；`ClassicDesktopStyleApplicationLifetime` **没有 ExitCode 属性**；`StartWithClassicDesktopLifetime` 返回 `void`。⇒ 唯一通道是 `desktop.Shutdown(exitCode)`，不需要 `Environment.Exit`，也不需要改 `Program.cs` 的返回值。（**注**：`Program.cs` 本计划仍要改——但只改「摘宿主开关」那一处，与退出码无关，见 §0.5。）
- **实测陷阱**：本 exe 是 `WinExe` 子系统，父 shell 不等待它。`pwsh` 里 `& .\MantisZip.UI.Avalonia.exe ...` 之后读 `$LASTEXITCODE` 拿到的是**上一条命令**的退出码；`Start-Process -Wait -PassThru` 的 `.ExitCode` 实测为空。验证必须用 .NET `ProcessStartInfo{RedirectStandardOutput=true}` 启动、再读 `Process.ExitCode`。

### 4. v0.5.8 之后的变化（本版计划必须遵守）

| # | 变化 | 对本计划的影响 |
|---|---|---|
| a | `App.CliExtract.cs` 1023 → 1046 行；`ResolveSmartDestCliAsync` / `ResolveSmartDestWithPasswordAsync` 返回**三元/四元组**（新增 `stripRoot` / `addRoot`） | 服务层调用不用改；但**批处理的冲突 options 已搬进 for 循环内逐包重建**（注释：根策略按包算，建在循环外会串包）。改冲突策略时不要把它搬回循环外 |
| b | 新增 `Services/ExtractOutcomeTracker.cs`（issue #91），`ThrowIfNothingWritten(int fallbackFailedEntries = 0)` 接在 GUI 三条叶子 | **CLI 两条叶子没接** ⇒ 「一条都没写出来」在 CLI 里仍算成功。本计划必须补（§5） |
| c | `SelectedItemsExtractService.CreateExtractOptions` 新增 `stripRoot` / `addRoot` 参数；**`livePolicy is not null` 分支完全忽略第一个参数 `conflictAction`** | **冲突注入点选错了**：真正决定行为的是 `SetConflictPolicyControl(flow, initialCode)` 的初始值，不是 `CreateExtractOptions` 的第一个参数（§1） |
| d | `App.CliExtract.cs` 单文件路径现在直连 `engine.ExtractAsync`（绕过 `ExtractFlow.ExtractAsync`），`extractResult.HasFailures` 时**只 `SetErrorSummary`，仍 `SetComplete` + 打印成功 + 写路径历史 + 删源包** | 只改退出码会出现「界面失败、exit 0」的新矛盾（§5） |
| e | 版本号已升到 **0.5.8**，本计划挂在 **v0.5.9** | 规则 1：开工前核对 `AppConstants.Version`，不一致**先问用户是否升版本**，不擅自改 |

### 5. 缺失的宿主标记

全仓 grep `IsCliMode|_isCli|IsCliHost|IsCommandLine` **零命中**。没有任何地方能回答「现在是不是无人值守执行」，这是三条修复都必须先补的地基。

## 已定决策（2026-11-22，用户通过选项确认）

| # | 问题 | 决定 |
|---|---|---|
| 1 | CLI 下 `FileConflictAction` 配的是 `ask`，没人点怎么办 | **静默改名（Rename）**。不覆盖（会丢数据）、不跳过（会静默少文件） |
| 2 | CLI 下遇到加密包且密码库没有可用密码 | **快速失败 + 非零退出码**。不弹窗、不新增 `--password` 参数、不读环境变量 |
| 3 | CLI 下进度窗因错误压钉 | **有限倒计时 10s 后关闭**。不立即关（让人有机会看到），stderr 仍写错误 |
| 4 | 冲突/密码之外的错误窗等待 | 同上，统一 10s |
| 5 | 「无人值守」怎么判定（2026-11-27 追加） | **父控制台自动判定 + 显式参数覆盖**。有父控制台（pwsh / cmd / CI）→ 无人值守；无（Explorer 右键、任务计划程序、双击）→ 交互。`--unattended` / `--interactive` 可强制覆盖。**不加设置项、不改注册表命令行** |
| 6 | 右键菜单体验 | **必须一字不变**——冲突框、密码框、UAC 提权框、错误压钉全部照旧（它们由决策 5 的「交互」判定自动保住，不需要动 `ShellIntegration.Menu.cs` 的 5 处 `command` 或 `ContextMenuHandler` 的拼串处） |

> 范围限定：决策 1–4 只作用于**无人值守路径**。判定见 §0.5，`--extract` 与 `--compress`（弹设置窗）无论判定结果如何都保持交互语义。

## 方案

### 0. 地基：两个新增的小类型

**`src/MantisZip.UI.Avalonia/Services/CliHostPolicy.cs`**（新增）

```csharp
internal static class CliHostPolicy
{
    /// <summary>当前是否处于「无人值守 CLI 执行」中。全部无人值守语义只看这一个标记。</summary>
    public static bool IsUnattended { get; private set; }

    private static bool _interactiveForced;

    /// <summary>无人值守下错误进度窗的兜底自动关闭秒数（用户已定 10s）。</summary>
    public const int UnattendedErrorCloseSeconds = 10;

    /// <summary>
    /// 由 Program.Main 与 App.OnFrameworkInitializationCompleted 各调一次：摘掉 --unattended / --interactive
    /// 并记下强制方向。返回的数组再交给 Avalonia（否则 switch (args[0]) 会落到 default 分支开 GUI）。
    /// **只增不减**（幂等）：第二次调用不会把已判定的 IsUnattended 清回 false。
    /// </summary>
    public static string[] ParseHostSwitches(string[] args)
    {
        if (args.Any(a => IsSwitch(a, "--interactive"))) _interactiveForced = true;
        if (!_interactiveForced && args.Any(a => IsSwitch(a, "--unattended"))) IsUnattended = true;
        return args.Where(a => !IsSwitch(a, "--unattended") && !IsSwitch(a, "--interactive")).ToArray();
    }

    /// <summary>进入无人值守执行前判定一次（见 §0.5 判定优先级的第 2 档）。</summary>
    public static void EnterUnattendedIfApplicable()
    {
        if (IsUnattended || _interactiveForced) return;
        IsUnattended = HasParentConsole();
    }

    /// <summary>探父控制台。真机实测见 §0.5：WinExe 下必须先 AttachConsole 再数进程。</summary>
    private static bool HasParentConsole()
    {
        if (HasParentConsoleForTesting is { } forced) return forced;   // 单测注入缝

        if (GetConsoleWindow() != IntPtr.Zero) return true;            // 未来若改 OutputType 的保险

        if (!AttachConsole(AttachParentProcess))
            return Marshal.GetLastWin32Error() == 5;                   // 已挂着控制台 → 5(ACCESS_DENIED) 也算有

        var list = new uint[8];
        var count = GetConsoleProcessList(list, (uint)list.Length);     // 含自身
        FreeConsole();                                                  // 立刻脱开，不留副作用
        return count > 1;
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    /// <summary>测试注入缝：非 null 时直接用它替代真机探测。生产恒 null。</summary>
    internal static Func<bool>? HasParentConsoleForTesting;

    private static bool IsSwitch(string arg, string expected)
        => string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>把设置里的冲突策略映射成无人值守下可用的策略：ask → rename，其余原样。</summary>
    public static string ResolveConflictAction(string configured)
        => IsUnattended && string.Equals(configured, "ask", StringComparison.OrdinalIgnoreCase)
            ? "rename"
            : configured;

    /// <summary>
    /// 密码输入入口：无人值守时返回「永远拿不到密码」的委托，让 PasswordRetryLoop 立即收尾；
    /// 交互模式返回 null（= 沿用弹 PasswordDialog 的默认行为）。
    /// </summary>
    public static Func<string, Task<string?>>? CreatePasswordPrompt()
        => IsUnattended ? _ => Task.FromResult<string?>(null) : null;

    internal static void ResetForTest()
    {
        IsUnattended = false;
        _interactiveForced = false;
        HasParentConsoleForTesting = null;
    }
}
```

> **实现注意**：`CliHostPolicy.cs` 需自带 `using System.Runtime.InteropServices;`（P/Invoke）与 `using System.Linq;`（`Any` / `Where`）——本仓没有 global using 文件，别指望隐式命名空间（`ImplicitUsings=enable` 只覆盖 `System` / `System.Linq` / `System.Threading.Tasks` 等基础集，`Runtime.InteropServices` 不在其中）。

**`src/MantisZip.UI.Avalonia/Models/CliExitCode.cs`**（新增）

```csharp
internal static class CliExitCode
{
    public const int Success = 0;
    public const int Failure = 1;          // 解压/压缩执行失败（含部分条目失败、参数无效、目标不可写）
    public const int PasswordRequired = 2; // 无人值守下遇到加密包但无可用密码
    public const int Cancelled = 3;        // 用户主动取消（交互模式下拒绝提权、关进度窗）
}
```

> **置位位置**（不是 `App.axaml.cs` 的 switch —— `--open-dispatch` 会绕过 switch 二次分发到 `RunExtractCliAsync`）：
> `RunExtractCliAsync`、`RunExtractSmartCliAsync`、`RunCliDirectExtractBatchAsync`、`CompressWithProgress` 四个方法**入口第一行**调 `CliHostPolicy.EnterUnattendedIfApplicable()`（见 §0.5，内部做判定，不自报真）。这四个正好是挂死点所在的四个方法，且 `--extract` / `--compress`（弹设置窗）自然排除在外。
> 不能在 `RunCliExtractWithProgressAsync` 里置位：`TryExtractSmartAsync` 在它之前就要用密码策略。

### 0.5 无人值守的判定方式（2026-11-27 定案：B + A 覆盖，不加设置项）

**判定优先级**（高 → 低）：

| 优先级 | 来源 | 判为无人值守 | 判为交互 |
|---|---|---|---|
| 1 | 命令行显式参数 | `--unattended` | `--interactive` |
| 2 | 父控制台探测 | 有父控制台 | 无父控制台 |

两个参数都是**全局开关**，可出现在参数表任意位置，路由前先摘掉（不能让 `App.axaml.cs` 的 `switch (args[0])` 把它们当作未知命令落到 default 分支显示 GUI）。

**摘参数的位置**：`src/MantisZip.UI.Avalonia/Program.cs` 的 `Main(string[] args)`。**必须在 `ShouldShowSplash(args)` 和 `SplashTextSpec(args)` 之前摘**，否则 `args[0]` 是 `--unattended` 时会落到 `ShouldShowSplash` 的 `_ => true` 分支（显示 splash，本该无窗口的脚本路径多闪一个 splash）。写法：

```csharp
public static void Main(string[] args)
{
    Services.StartupTimer.Begin();
    args = Services.CliHostPolicy.ParseHostSwitches(args);   // ← 新增：摘宿主开关 + 定强制方向
    if (ShouldShowSplash(args)) { ... }                       // 以下全部不变
    ...
    builder.StartWithClassicDesktopLifetime(args);            // 传给 Avalonia 的已是摘净的
}
```

> `Program.cs` 因此从「不动」移入「修改」清单。改动只有两行（一行赋值 + 一行注释），不动任何其它逻辑。

> **`App.axaml.cs` 要改第二处**：它自己调 `Environment.GetCommandLineArgs().Skip(1)` 重新读命令行（不走 `Program.Main` 的 `args` 形参），而 `ParseHostSwitches` 只摘掉传进来的数组、不改进程命令行字符串，所以 `--unattended` / `--interactive` 仍会出现在 `App` 读到的参数里。**必须在 `App` 里也过滤一次**，否则 `args[0]` 是 `--unattended` 时会落到 `switch (command)` 的 `default` 分支（`App.axaml.cs` 的 `// Unknown args: just show UI`）去开 GUI。两处都调 `CliHostPolicy.ParseHostSwitches` 是幂等的（第二次不会把已判定的结果清回 false），所以照抄一行即可：

```csharp
var args = Services.CliHostPolicy.ParseHostSwitches(
    Environment.GetCommandLineArgs().Skip(1).ToArray());   // ← 包一层
```

> 这条与 `Program.cs` 那处**必须同时做**：漏一处 = 要么 splash 多闪、要么直接弹 GUI，两种都是「脚本里多出一个窗口」。

**父控制台探测**：`App.axaml.cs` 的 `ShowCliHelpThenShutdown` 已经声明了 `AttachConsole(ATTACH_PARENT_PROCESS)` / `FreeConsole()`（`kernel32.dll`），判定要复用的正是这两个。**必须先 `AttachConsole` 再看进程数**——下面是真机实测（2026-11-28，临时 WinExe 探针）得出的结论，不是文档推断：

| 宿主 | `GetConsoleWindow()` | `GetConsoleProcessList()`（未 attach） | `AttachConsole(parent)` | attach 后 `GetConsoleProcessList()` |
|---|:--:|:--:|:--:|:--:|
| pwsh 里直接调用（有父控制台） | **0** | **0** | **1**（err=0） | **3**（自身 + pwsh 等） |
| Explorer 右键式拉起（无控制台） | **0** | **0** | **0**（**err=6** `ERROR_INVALID_HANDLE`） | — |

> **`GetConsoleProcessList` 在 WinExe 下不先 attach 永远返回 0**，所以「先看 `GetConsoleWindow` 再数进程」的写法会把**所有**宿主都判成交互（包括从 pwsh 调用），等于判定恒假、功能全不生效。`AttachConsole` 是这里唯一可靠的探针，代价是挂上后要 `FreeConsole()` 脱开（与 `--help` 那条路径同一套用法，互不干扰——两条路径不会同时跑）。完整实现见 §0 的 `CliHostPolicy.HasParentConsole()`。

> 判定只发生一次（`EnterUnattendedIfApplicable` 首行就 `if (IsUnattended || _interactiveForced) return;`），attach/free 各一次，微秒级，放在 `StartupTimer.Begin()` 之后无冷启影响。

**置位入口改为判定式**（与 §0 的类型定义一致）：

```csharp
public static void EnterUnattendedIfApplicable()
{
    if (IsUnattended || _interactiveForced) return;
    IsUnattended = HasParentConsole();
}
```

**为什么不动注册表**：`ShellIntegration.Menu.cs` 的 5 处 `command` 与 `ShellExt/ContextMenuHandler.cs` 的拼串处保持原样，老用户装完即得到「无控制台 → 交互」的新行为，不需要重注册，也不需要一个 `MenuSchema` 版本标记 + 启动自愈。这是本方案最大的成本优势。

**已知盲区（写进 `docs/CLI.md`，不登记 KI）**：

| 宿主 | 有父控制台 | 判定 | 是否合理 |
|---|---|---|---|
| pwsh / cmd 里直接调用 | 是 | 无人值守 | ✓ |
| CI（GitHub Actions `run:`） | 是 | 无人值守 | ✓ |
| Explorer 右键菜单 | 否 | 交互 | ✓ 用户在场 |
| 双击 .bat 包裹调用 | 视写法（`.bat` 自己会建控制台） | 通常无人值守 | ✓ 脚本意图 |
| 任务计划程序 | 否 | **交互 → 会挂死** | ✗ 需加 `--unattended` |
| 某些 CI agent（无 tty） | 否 | **交互 → 会挂死** | ✗ 需加 `--unattended` |

`MantisZip.exe --unattended --extract-here x.zip` 是无控制台自动化环境的唯一出路。

> **`--interactive` 的用途**：反向覆盖。典型场景是「在 pwsh 里手动敲 `--extract-here`，但就是想看冲突框自己选」。不加也能用（默认会静默改名，只是少了选择权），所以它是可选项，但成本只有一行参数比对，一起加上。

> **探测的可靠性（2026-11-28 已实测，不再是「待验证」）**：`GetConsoleProcessList` 必须**先 `AttachConsole` 成功**才有意义（未 attach 时 WinExe 恒返回 0）。attach 成功后 `count` 是**附着到该控制台的进程数（含自身）**：从 pwsh 拉起时实测 `count=3`（≥2 → 有父控制台）；Explorer 右键式无控制台宿主 `AttachConsole` 直接失败（err=6）→ 判交互。真机回归见 §验证 的 H1–H6。

> **`--unattended` / `--interactive` 同时出现时：`--interactive` 优先**（保守选择——宁可让用户看见弹窗，也不要把人本来能处理的状况静默处理掉）。

### 1. 冲突：`ask` → `rename`

**关键**：CLI 三条路径都传了 `livePolicy`，而 `SelectedItemsExtractService.CreateExtractOptions` 在 `livePolicy is not null` 分支里**固定 `ConflictAction = Ask` + `ConflictActionProvider = livePolicy`，完全忽略第一个参数 `conflictAction`**；`FileConflictHelper.ResolvePathAsync` 也是先读 `ConflictActionProvider`。所以**真正生效的是进度窗下拉框的初始值**，即 `SetConflictPolicyControl(flow, initialCode)`。

写法：在每处算出一次局部变量，两处消费（初始值 + options 兜底）。

```csharp
// 无人值守：ask → rename（决策 1）。设置本身不回写，只在执行期生效。
var conflictCode = CliHostPolicy.ResolveConflictAction(settings.FileConflictAction);
await Dispatcher.UIThread.InvokeAsync(() =>
    progressWindow.SetConflictPolicyControl(ConflictPolicyFlow.Extract, conflictCode));
var options = SelectedItemsExtractService.CreateExtractOptions(
    conflictCode,               // 兜底（本路径 livePolicy 非 null 时不起作用，保持与初始值一致）
    conflictDialog,
    livePolicy: () => progressWindow.ConflictPolicy,
    stripRoot: stripRoot,
    addRoot: addRoot);
```

| 位置（定位子串） | 改法 |
|---|---|
| `App.CliExtract.cs` 单文件路径：搜 `SetConflictPolicyControl(ConflictPolicyFlow.Extract`（第一处，紧邻 `RunCliExtractWithProgressAsync`） | 初始值 + `CreateExtractOptions` 第一参数都换成 `conflictCode` |
| `App.CliExtract.cs` 批处理路径：搜 `RunCliDirectExtractBatchAsync` 内的 `SetConflictPolicyControl` + for 循环内的 `CreateExtractOptions` | 同上；`conflictCode` 在循环外算一次，**`CreateExtractOptions` 仍留在循环内**（根策略按包算） |
| `App.CliCompress.cs` 的 `CompressWithProgress`：搜 `SetConflictPolicyControl(ConflictPolicyFlow.Compress` 与 `CompressFlow.CreateResolver(` | 同上，`conflictCode` 同时传给 `SetConflictPolicyControl` 和 `CreateResolver(..., defaultAction: conflictCode, ...)`。`CompressFlow.CreateResolver` 内部也是 `livePolicy?.Invoke() ?? defaultAction`，所以两处都要改 |

`request.ConflictAction` 来自 `settings.CompressFileConflictAction`，同一套字符串口径（`"overwrite" / "rename" / "skip" / "ask"`）。

> 为什么不改 `AppSettings`：设置是用户的，CLI 不该回写。映射只在执行期生效。

### 2. 密码：快速失败

**不要**用 `PasswordService.HasEncryptedEntries` 做预检。它在异常与未知引擎时**保守 `return true`**，拿它判「加密但无密码」会把**损坏包误判成缺密码**（与验收表「损坏包 → exit 1」冲突）。而且它是 `PasswordService` 的**实例方法**，`ArchiveService` 上并没有这个方法。

正确做法是**注入密码输入入口**，让引擎自己的报错决定语义：

- 加密码处：`PasswordRetryLoop.RunAsync(..., promptPassword: CliHostPolicy.CreatePasswordPrompt())`
- 判定处：`catch (PasswordRetryCancelledException) when (CliHostPolicy.IsUnattended)` → **`PasswordRequired`**
  （无人值守下进度窗的取消按钮抛的是 `OperationCanceledException`，不会污染这条路径；`Cancelled` 只可能来自被注入的 prompt 返回 null）

四个注入点（全部用 `promptPassword:` 具名参数，不动 `PasswordRetryLoop` 签名）：

| # | 位置 |
|---|---|
| 1 | `App.CliExtract.cs` 的 `RunCliExtractWithProgressAsync` 内 `PasswordRetryLoop.RunAsync(`（`owner: progressWindow` 那处） |
| 2 | `App.CliExtract.cs` 的 `TryExtractSmartAsync` 调 `ResolveSmartDestWithPasswordAsync(archivePath, engine, password)` |
| 3 | `App.CliExtract.cs` 的 `ResolveSmartDestWithPasswordAsync` 自身内部对 `PasswordRetryLoop.RunAsync` 的调用（已透传 `promptPassword`，无需改函数体，只改**调用方**传参） |
| 4 | `App.CliExtract.cs` 的 `RunCliDirectExtractBatchAsync`：for 循环内的 `ResolveSmartDestWithPasswordAsync(...)` 与 `PasswordRetryLoop.RunAsync(` 各一处 |

链路验证：

| 输入 | `IsPasswordRelatedError` | 结果 |
|---|---|---|
| 损坏 zip（`Failed to locate the Zip Header`） | 不含 password 关键词 + 命中 `HasCorruptionSignal` → false | 原样上抛 → 外层 `catch (Exception)` → **`Failure`** ✓ |
| 加密 7z / zip 无可用密码 | true | prompt 返回 null → `Cancelled` → 无人值守映射 → **`PasswordRequired`** ✓ |
| 加密包 + 密码库有匹配 | 不进入密码异常 | 正常解压 ✓ |

新增本地化 key（三语 `strings.*.json`）：`Cli_StatusPasswordRequired`（「缺少密码」/「Password required」/「缺少密碼」）。

批处理里单条密码失败**不升级**成整批 2：该行标 `Failed(reason)` + `continue`，整批结束后按「存在失败行」返回 `Failure`。

### 2.5 提权：无人值守下不弹窗（追加发现）

`App.CliInfra.cs` 的 `HandleElevationAsync` 在 CLI 下 `desktop.MainWindow == null`，会造一个 `IsVisible = false` 的临时 owner 再 `ShowDialog` —— **无人值守时同样是永久挂起**，与 §1/§2 同类。本计划一并处理：

```csharp
if (CliHostPolicy.IsUnattended)
{
    Console.Error.WriteLine($"{LocalizationManager.T("Cli_StatusNeedElevation")}: {string.Join("; ", unwritable)}");
    return false;   // 调用方按 Failure 收尾
}
```

新增 key：`Cli_StatusNeedElevation`（「目标目录不可写且无法在无人值守模式下提权」/ 英文 / 繁体）。
`AllowElevation=false` 的 `ElevationInfoDialog` 分支在无人值守下同样跳过。

### 3. 错误进度窗：10s 兜底

`Dialogs/ProgressWindow.axaml.cs` 加实例级设置（**不加构造函数参数**——`ProgressWindow()` / `ProgressWindow(string)` 两个构造函数在 `src/` 里有 7 处 `new ProgressWindow(` 调用，加参数会波及 GUI 路径，且诱导「每个调用点都要记得传」的散装写法）：

```csharp
/// <summary>无人值守宿主设置：错误压钉后最多等 N 秒再关（0 = 维持无限等）。</summary>
public void SetupUnattendedErrorTimeout(int seconds) => _unattendedErrorTimeoutMs = Math.Max(0, seconds) * 1000;
```

`AutoCloseOrWaitAsync` 的压钉分支改为：

```csharp
if (_vm.KeepOpenOnComplete)
{
    var timeout = _vm.HasErrors ? _unattendedErrorTimeoutMs : 0;
    if (await WaitForManualCloseAsync(timeout) == ManualWaitExit.Closed) { closeAction(); return; }
    continue;   // timeout == 0 → 行为完全不变（无限等）
}
```

`WaitForManualCloseAsync` 加可选 `int timeoutMs = 0`：`timeoutMs > 0` 时在心跳循环里判 `Stopwatch` 是否超时，超时返回 `ManualWaitExit.Unpinned`（复用现有「不再等」语义，**枚举不加值**）。

置位点（**只在无人值守时**，否则 `--extract` 的进度窗也会 10s 自关）：

| 位置 | 写法 |
|---|---|
| `Services/CliProgressPresenter.cs` 的 `RunAsync` 建窗/用窗之后 | `if (CliHostPolicy.IsUnattended) window.SetupUnattendedErrorTimeout(CliHostPolicy.UnattendedErrorCloseSeconds);` |
| `App.CliExtract.cs` 的 `RunCliExtractWithProgressAsync` 自建进度窗处 | 同上 |
| `App.CliExtract.cs` 的 `RunCliDirectExtractBatchAsync` 自建进度窗处 | 同上 |

### 4. 退出码贯通（范围收窄）

**不做「五条执行链全部改 int」**——实际调用多为 fire-and-forget（`_ = RunExtractCliAsync(...)` / `_ = CompressWithProgress(...)`），改签名是白工。只改真正需要传值的三个：

| 方法 | 现在 | 改为 | 说明 |
|---|---|---|---|
| `App.CliExtract.cs` `TryExtractArchiveAsync` | `Task<bool>`（true = 已提权重启） | `Task<int?>` | `null` = 已在内部请求退出（UAC 重启成功），调用方**不要**再 `Request` |
| `App.CliExtract.cs` `TryExtractSmartAsync` | `Task<bool>` | `Task<int?>` | 同上 |
| `App.CliExtract.cs` `RunCliExtractWithProgressAsync` | `Task<bool>`（恒 false） | `Task<int>` | 真正的结局码 |

其余保持 `Task`，在方法**内部自己的** `AppShutdown.Request(desktop.Shutdown)` 上带码：

```csharp
// App.CliExtract.cs 的 RunExtractCliAsync / RunExtractSmartCliAsync
var exitCode = await TryExtractArchiveAsync(...);
if (exitCode is int code)
    AppShutdown.Request(desktop.Shutdown, code);
// null → 提权路径已在 HandleElevationAsync 内 Request 过，重复调用会二次触发关窗
```

`RunCliDirectExtractBatchAsync` / `CompressWithProgress` 的既有三个/一个 `AppShutdown.Request` 出口各带上算好的码。

汇总规则：

| 情形 | 退出码 |
|---|---|
| 全部成功 | `Success` |
| 任一失败 / 异常 / 部分条目失败 / 参数无效 / 目标不可写（无人值守） | `Failure` |
| 无可用密码（无人值守） | `PasswordRequired` |
| 用户取消（`OperationCanceledException`、交互模式拒绝提权） | `Cancelled` |
| 提权重启（原进程退出，`HandleElevationAsync` 返回 true） | 不传码（`null` 路径），新进程重新执行 |

**参数无效的空集合分支也要带码**：`HandleDirectExtract`、`HandleExtract`、`HandleCompress*` 里 `if (myPaths.Count == 0) { AppShutdown.Request(desktop.Shutdown); return; }` 这类守卫，CLI 有参但全部无效 → 先 `Console.Error.WriteLine` 一行，再 `AppShutdown.Request(desktop.Shutdown, CliExitCode.Failure)`。

`CompressWithProgress`：`failedCount > 0` → `Failure`；异常 `catch` → `Failure`；`existingWindow` 不可见早退 → `Success`（沿用现状）。

### 5. CLI 解压「0 文件 / 部分失败」映射（v0.5.8 遗留）

CLI 两条叶子直连 `engine.ExtractAsync`，**没有接 `ExtractOutcomeTracker`**。只改退出码会出现「界面显示失败、exit 0」。两处都补：

**单文件（`RunCliExtractWithProgressAsync`）**

```csharp
var tracker = new ExtractOutcomeTracker(rawProgress);
var (singleOutcome, _) = await PasswordRetryLoop.RunAsync(
    archivePath, password, engine,
    owner: progressWindow,
    setStatus: msg => progressWindow.SetStatus(msg),
    attempt: async (pwd, token) =>
    {
        tracker.Reset();                       // 每轮重试清零，上一轮计数不能带进本轮
        extractResult = await engine.ExtractAsync(archivePath, targetDir, pwd, tracker, token, options);
    },
    ct: ct,
    promptPassword: CliHostPolicy.CreatePasswordPrompt());
...
tracker.ThrowIfNothingWritten(extractResult?.FailedEntries ?? 0);
```

然后 **`HasFailures` 不再走成功路径**：

```csharp
if (extractResult is { HasFailures: true })
{
    // 部分条目失败：显示错误汇总 + 走失败收尾，不打印成功、不写路径历史、不删源包
    errorSummary = T("Progress_WriteFailed_Fmt", extractResult.FailedEntries, targetDir);
    return CliExitCode.Failure;   // 由 AutoCloseOrWaitAsync 的 10s 兜底关窗
}
```

**批处理（`RunCliDirectExtractBatchAsync`）**：同样包 tracker，且逐包检查 `extractResult.HasFailures` —— 失败时 `UpdateBatchItemStatus(i, Failed, ...)`、**不 `Record(targetDir)`、不进 `succeededArchives`**，并累加 `failedCount`；循环结束后 `failedCount > 0` 时 `SetComplete` 改为 `CompleteWithErrors` 且退出码 `Failure`。

> 与 GUI 对齐：GUI 三条叶子只在「一条都没写出来」时抛错（`ThrowIfNothingWritten`），部分失败仍算完成。CLI 侧比 GUI 严一档（部分失败即非零退出码），因为脚本必须能区分「全部成功」。此差异写进 `docs/CLI.md`。

### 6. 文档

- `docs/CLI.md`：新增 `## 退出码` 章节（0/1/2/3 表）+ `## 无人值守行为` 一节（冲突自动改名、缺密码立即失败、错误窗 10s 自动关闭、目标不可写不提权）+ `## 宿主开关` 一节（`--unattended` / `--interactive` 的语义、优先级、「同时给时 `--interactive` 优先」、以及自动判定规则与已知盲区表）。
- `docs/ARCHITECTURE.md` 的 `### CLI entry points` 一节（2026-11-27 位于约 :723）：补一句宿主策略与退出码的归属（`CliHostPolicy` / `CliExitCode`）。

## 涉及文件

**新增**

- `src/MantisZip.UI.Avalonia/Services/CliHostPolicy.cs`
- `src/MantisZip.UI.Avalonia/Models/CliExitCode.cs`
- `tests/MantisZip.UI.Avalonia.Tests/CliHostPolicyTests.cs`（注入 seam：`internal static Func<bool>? HasParentConsoleForTesting`，生产恒 null。断言 `ParseHostSwitches` 摘参数 + 优先级 + **不摘非宿主参数** + `--interactive` 压过 `--unattended` + 大小写不敏感 + `ResetForTest` 后状态归零）
- `tests/MantisZip.UI.Avalonia.Tests/ProgressWindowUnattendedTimeoutTests.cs`

**修改**

- `src/MantisZip.UI.Avalonia/Program.cs`（摘宿主开关 + 传净参数给 Avalonia）
- `src/MantisZip.UI.Avalonia/App.axaml.cs`（`OnFrameworkInitializationCompleted` 里再摘一次——它走 `Environment.GetCommandLineArgs()` 而非 `Main` 的 `args`）
- `src/MantisZip.UI.Avalonia/App.CliExtract.cs`（置位 + 冲突初始值 + 密码注入 + tracker 接线 + `HasFailures` 映射 + 返回值签名 + 空集合守卫退出码）
- `src/MantisZip.UI.Avalonia/App.CliCompress.cs`（置位 + 冲突初始值 + `failedCount` 退出码）
- `src/MantisZip.UI.Avalonia/App.CliInfra.cs`（`HandleElevationAsync` 无人值守早退）
- `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs`（`SetupUnattendedErrorTimeout` + `WaitForManualCloseAsync` 超时）
- `src/MantisZip.UI.Avalonia/Services/CliProgressPresenter.cs`（超时注入，按 `IsUnattended` 门控）
- `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`（`Cli_StatusPasswordRequired`、`Cli_StatusNeedElevation`）
- `docs/CLI.md`、`docs/ARCHITECTURE.md`

**不动**

- `src/MantisZip.UI.Avalonia/Services/AppShutdown.cs`（漏斗已透传 exitCode）
- `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Menu.cs` 与 `src/MantisZip.ShellExt/ContextMenuHandler.cs`（**注册表命令行不动**，判定靠父控制台自动成立）
- `src/MantisZip.UI.Avalonia/App.CliInfra.cs` 的 `HandleShellCommand`（已有正确退出码语义）
- `src/MantisZip.UI.Avalonia/Services/PasswordRetryLoop.cs`（`promptPassword` 注入点已够用）
- `src/MantisZip.UI.Avalonia/Services/ExtractOutcomeTracker.cs`（复用，不改）
- `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`（不改默认值；用户设置不被 CLI 回写）
- `ViewModels/ProgressViewModel.cs` 的压钉逻辑（GUI 里仍需要它）

## 验证

### 构建与测试

```powershell
pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia
pwsh -File scripts\verify-repo.ps1 -Task test -Target ui-tests
pwsh -File scripts\verify-repo.ps1 -Task i18n
```

### 真机验证（必须，自动化跑不了）

前提：先重新构建（`bin\Release` 现有产物是 2026-11-22 的，早于 HEAD）；用便携模式隔离（`Portable.txt` + `Data\settings.json`），**每次全新随机目录 + 随机包名**；`.NET ProcessStartInfo` 读 `ExitCode`（不能用 pwsh 的 `$LASTEXITCODE`，见上文实测陷阱）。

| 场景 | 期望退出码 | 期望耗时 | 修复前 |
|---|---|---|---|
| 正常 zip `--extract-here` | 0 | < 10s | 7317ms（5s 自动关闭倒计时）→ 不变 |
| 损坏 zip `--extract-here` | 1 | < 15s（10s 兜底） | HUNG |
| 截断 zip `--extract-here` | 1 | < 15s | HUNG |
| 加密 7z 无密码 `--extract-here` | 2 | < 5s | HUNG |
| 同名冲突（`ask`）`--extract-here` | 0（改名成功） | < 10s | HUNG |
| 不存在的包 `--extract-here` | 1 | < 5s | 0（错） |
| 目标目录只读 `--extract-here` | 1 | < 5s | HUNG（提权弹窗） |
| 两包批处理（一好一坏）`--extract-here` | 1 | < 20s | HUNG |
| 损坏包 `--compress-quick` | 1 | < 15s | 待测 |
| 无参数（GUI） | 不受影响，正常显示主窗口 | — | — |

**宿主判定的验证（本轮新增，必须逐条跑）**：每条都用带外 `--extract-here` 一个正常小 zip，只看「有没有弹窗 / 退出码」，不需要复杂包。

| # | 宿主 | 期望判定 | 期望结果 |
|---|---|---|---|
| H1 | pwsh 里 `& $exe --extract-here x.zip` | 无人值守 | 无窗口，exit 0 |
| H2 | 无控制台启动（`Start-Process -Wait` 且不重定向） | 交互 | 有时会看到窗口（说明判定为交互），exit 0 |
| H3 | Explorer 右键「解压到此处」 | 交互 | **冲突框正常弹**（这是本方案的核心不退化断言） |
| H4 | `& $exe --interactive --extract-here x.zip`（pwsh 里强制交互） | 交互 | 有冲突时弹框 |
| H5 | `Start-Process -Wait $exe '--unattended','--extract-here',x.zip` | 无人值守 | 无窗口，冲突自动改名，exit 0 |
| H6 | `--unattended --interactive` 同时给 | 交互 | 等价 H4（`--interactive` 优先） |

> **只有 H3 能真正构造「无控制台」环境**：从 pwsh 用 `Start-Process` / `&` 拉起子进程时，子进程会继承 pwsh 的控制台（实测 attach 后 `count=3`），拿不到 H2 想要的「无控制台」宿主。**H2 不要用 `Start-Process` 假装**——要走 Explorer 右键、任务计划程序或双击 .bat 包裹；若真机无法构造，退一步在 `CliHostPolicy` 里加一行 `DebugLog` 打印 `AttachConsole` 返回值与 `count`，用日志断言，不要为了测试改产品行为。H5 显式带 `--unattended`，与宿主无关，任何环境都能跑。

**成功路径的 5s 倒计时不在本计划内**：那是 `AppSettings.AutoCloseDelaySeconds` 的 GUI 语义（用户确认窗口），改它会影响桌面体验。若将来要优化 CLI 启动开销，另立计划按宿主分流。

### 回归

- `AppShutdownWiringTests`（4 例）：`NoBareDesktopShutdownCalls_RemainInExitPaths` 会拦住新增的裸 `desktop.Shutdown(`，必须走 `AppShutdown.Request`
- `AppShutdownTests.Request_ForwardsExitCode`：漏斗透传码不能破
- `ProgressWindowPinTests`（2 例）：GUI 压钉行为不能变
- `ProgressViewModelTests` 里的压钉断言：`HasErrors` → 压钉的逻辑保留，只多了「无人值守下超时」
- `FactoryDefaultsTests`：`AutoCloseDelaySeconds` 默认值仍是 5
- `SmartExtractPasswordTests`（5 例）：`ResolveSmartDestWithPasswordAsync` 的四元组与密码注入默认行为不能变
- 新测试一律 `using var sandbox = new AppSettingsTestSandbox();`（规则 24；`ProgressWindow.OnOpened` 会 `AppSettings.Load()`）

## 边界

- **只修行为，不动工程结构**：不新增 `MantisZip.Cli.csproj`、不改 `OutputType=WinExe`、不动安装器与发布流水线。独立 CLI exe 是另一个话题（拆不拆与本次无关）。
- **不修输出重定向**：`MantisZip.exe --help > help.txt` 在 `cmd` / `pwsh` 下仍拿不到内容（`WinExe` + `AttachConsole` 的固有行为，实测 0 字节）。这是子系统问题，只有独立 exe 或改 `OutputType` 才能根治，明确不在本计划内。**退出码不受此影响**（`Process.ExitCode` 正常）。
- **不改 `AppSettings.AutoCloseDelaySeconds` 默认值**，不改 GUI 压钉语义。
- **不新增 CLI 参数**（无 `--password` / `--quiet` / `--yes`）。**例外**：`--unattended` / `--interactive` 是本计划新增的两个宿主开关（§0.5），已定案纳入对外契约，必须写进 `docs/CLI.md`。
- **不覆盖 `--extract` / `--compress` 的弹窗语义**：它们本来就要求用户在场。
- **不碰规则 20 的 `dialog-seam` 守卫**：本计划不新建 `CompressSettingsWindow` / `ExtractSettingsWindow`。
- **`docs/CLI.md` 的对外承诺暂不改口径**：修复后「自动化脚本」这句才成立；若实施中发现仍有场景无法脚本化，按规则 21 登记 KI，不要悄悄改文案。
- **不做设置项**（2026-11-27 用户定案）：不加 `AppSettings` 属性、不动设置页。判定只走「显式参数 → 父控制台」两档。将来若真出现「用户想全局关掉自动判定」的需求，再按规则 23 立计划（同一次提交接上运行时消费者 + `settingswiring` 守卫）。
- **不加 `MenuSchema` 版本标记、不做菜单启动自愈**：本方案不动注册表命令行，老用户无需重注册。
