# Avalonia CLI 无人值守健壮性：挂死修复 + 真实退出码

> **状态**: 📋 待实施 | **创建**: 2026-11-22 | **优先级**: P0 | **预估工时**: 8-12h
> **来源**: 2026-11-21/22 CLI 独立 exe 调研的实测结论（`qa_test_temp/cli-hang-probe.ps1`、`cli-redirect-probe.ps1`）
> **当前核实**: 2026-11-22 实测确认：`--extract-here` 在「损坏包 / 截断包 / 加密包无密码 / 同名冲突」四种情形下**永久挂起**（>12s 强制终止）；成功与失败路径的进程退出码**恒为 0**。

## 背景

项目对外宣称 CLI「主要用于 Shell 右键菜单集成和自动化脚本」（`docs/CLI.md:3`），但实测这两件事目前都只兑现了前者：

1. **会挂死**。CLI 的进度窗口在出现错误时被自动「压钉」（`KeepOpenOnComplete = true`），随后进入无限等待人工关闭。GUI 里这是对的——让用户看清错误；无人值守时就是进程永不退出，脚本卡死、CI 超时。
2. **没有退出码**。40 处 `AppShutdown.Request(desktop.Shutdown)` 全部走默认 `exitCode = 0`，成功与失败在脚本侧无法区分。

这两条与「是否拆独立 CLI exe」无关：拆不拆 exe 是分发形态，挂死和退出码是**行为正确性**。本计划只修行为，不动工程结构。

## 现状核实（2026-11-22）

### 挂死的根因链

| 环节 | 位置 | 行为 |
|---|---|---|
| 压钉 | `ViewModels/ProgressViewModel.cs:488` `OnHasErrorsChanged` | `if (value) { KeepOpenOnComplete = true; StatusKind = Failed; }`，**单向，没有任何地方解钉** |
| 无限等 | `Dialogs/ProgressWindow.axaml.cs:334` | `if (_vm.KeepOpenOnComplete) await WaitForManualCloseAsync()` |
| 等待实现 | `Dialogs/ProgressWindow.axaml.cs:468` | 只在 `IsVisible == false` 或 `KeepOpenOnComplete == false` 时返回 ⇒ 窗口可见 + 已压钉 = 永不返回 |
| 三个挂死点 | `App.CliExtract.cs:789`、`App.CliExtract.cs:1009`、`Services/CliProgressPresenter.cs:124` | 分别对应单文件解压失败、批处理失败、压缩异常 |

`KeepOpenOnComplete` 全仓只有 5 处写入：`:437` 定义、`:443` 变更回调、`:492`（`HasErrors` 压钉）、`:500`（`IsTestFlow` 压钉）、`:2196`。**没有解钉路径**。

### 实测的四种挂死情形

| 场景 | 结果 | stderr |
|---|---|---|
| 损坏 zip（扩展名合法、内容坏） | HUNG >12s | `Extraction failed: Failed to locate the Zip Header` |
| 截断 zip（真包截到 60%） | HUNG >12s | 同上 |
| 7z 加密包 + 无保存密码 | HUNG >12s | 密码窗在前台弹出（无人点） |
| 同名冲突（`FileConflictAction=ask`） | HUNG >12s | 冲突窗在前台弹出（无人点） |

### 退出码现状

- `Services/AppShutdown.cs:50 public static void Request(Action<int> shutdown, int exitCode = 0)` —— **漏斗本身已支持传码**（`:55` / `:59` 两分支都透传），只是没有调用方传。
- `AppShutdown.Request(` 全仓 40 处，**零处传非零值**。
- 唯一的例外是 shell 命令分支 `App.CliInfra.cs:47 HandleShellCommand`：`:88` 未知命令 `exitCode = 1`、`:96` 异常 `exitCode = 1`、`:99 Environment.Exit(exitCode)`。这条路径已经正确，**不动**。
- Avalonia 侧退出码通道已核实（`C:\Users\Admin\.nuget\packages\avalonia\12.1.2\lib\net10.0\Avalonia.Controls.xml`）：`IControlledApplicationLifetime.Shutdown(System.Int32)` 文档原文「sets the exit code that is returned to the operating system when the application exits」；`ClassicDesktopStyleApplicationLifetime` **没有 ExitCode 属性**；`StartWithClassicDesktopLifetime` 返回 `void`。⇒ 唯一通道是 `desktop.Shutdown(exitCode)`，不需要 `Environment.Exit`，也不需要改 `Program.cs`。
- **实测陷阱**：本 exe 是 `WinExe` 子系统，父 shell 不等待它。`pwsh` 里 `& .\MantisZip.UI.Avalonia.exe ...` 之后读 `$LASTEXITCODE` 拿到的是**上一条命令**的退出码；`Start-Process -Wait -PassThru` 的 `.ExitCode` 实测为空。验证必须用 .NET `ProcessStartInfo{RedirectStandardOutput=true}` + `Process.ExitCode`，或用 `cmd /c` 包一层。

### 缺失的宿主标记

全仓 grep `IsCliMode|_isCli|IsCliHost|IsCommandLine` **零命中**。没有任何地方能回答「现在是不是无人值守执行」，这就是三条修复都必须先补的地基。

## 已定决策（2026-11-22，用户通过选项确认）

| # | 问题 | 决定 |
|---|---|---|
| 1 | CLI 下 `FileConflictAction` 配的是 `ask`，没人点怎么办 | **静默改名（Rename）**。不覆盖（会丢数据）、不跳过（会静默少文件） |
| 2 | CLI 下遇到加密包且密码库没有可用密码 | **快速失败 + 非零退出码**。不弹窗、不新增 `--password` 参数、不读环境变量 |
| 3 | CLI 下进度窗因错误压钉 | **有限倒计时 10s 后关闭**。不立即关（让人有机会看到），stderr 仍写错误 |
| 4 | 冲突/密码之外的错误窗等待 | 同上，统一 10s |

## 方案

### 0. 地基：两个新增的小类型

**`src/MantisZip.UI.Avalonia/Services/CliHostPolicy.cs`**（新增）

```csharp
internal static class CliHostPolicy
{
    /// <summary>当前是否处于「无人值守 CLI 执行」中。由各 CLI 执行方法在进入时置位。</summary>
    public static bool IsUnattended { get; set; }

    /// <summary>无人值守下错误进度窗的兜底自动关闭秒数（用户已定 10s）。</summary>
    public const int UnattendedErrorCloseSeconds = 10;

    /// <summary>把设置里的冲突策略映射成无人值守下可用的策略：ask → rename，其余原样。</summary>
    public static string ResolveConflictAction(string configured)
        => IsUnattended && string.Equals(configured, "ask", StringComparison.OrdinalIgnoreCase)
            ? "rename"
            : configured;

    internal static void ResetForTest() => IsUnattended = false;
}
```

**`src/MantisZip.UI.Avalonia/Models/CliExitCode.cs`**（新增）

```csharp
internal static class CliExitCode
{
    public const int Success = 0;
    public const int Failure = 1;          // 解压/压缩执行失败（含部分批处理失败）
    public const int PasswordRequired = 2; // 无人值守下遇到加密包但无可用密码
    public const int Cancelled = 3;        // 用户主动取消
}
```

> **置位位置**（不是 `App.axaml.cs` 的 switch —— `--open-dispatch` 会绕过 switch 在 `:278` 二次分发）：
> `RunExtractCliAsync`、`RunExtractSmartCliAsync`、`RunCliDirectExtractBatchAsync`、`CompressWithProgress` 四个方法入口置 `CliHostPolicy.IsUnattended = true`。这四处正好是挂死点所在的四个方法，覆盖面与需求一致，且 `--extract` / `--compress`（弹设置窗、用户在场）自然排除在外。

### 1. 冲突：`ask` → `rename`

两个注入点，都在 `CliHostPolicy.ResolveConflictAction` 之后传：

| 位置 | 现状 | 改为 |
|---|---|---|
| `App.CliExtract.cs:675`（单文件） | `SelectedItemsExtractService.CreateExtractOptions(settings.FileConflictAction, ...)` | `CliHostPolicy.ResolveConflictAction(settings.FileConflictAction)` |
| `App.CliExtract.cs:841`（批处理） | 同上 | 同上 |
| `App.CliCompress.cs:459` | `CompressFlow.CreateResolver(..., request.ConflictAction, ...)` | `CliHostPolicy.ResolveConflictAction(request.ConflictAction)` |

`request.ConflictAction` 来自 `settings.CompressFileConflictAction`（`App.CliCompress.cs:186`），同一套字符串口径（`"overwrite" / "rename" / "skip" / "ask"`，见 `SelectedItemsExtractService.MapConflictActionString:114`）。

> 为什么不改 `AppSettings`：设置是用户的，CLI 不该回写。映射只在执行期生效。

### 2. 密码：快速失败

`App.CliExtract.cs:43 ResolveCliPassword` 已经做了 `service.HasEncryptedEntries(archivePath, engine)` 判断，返回 null 表示「加密但密码库没匹配上」。**不要**去改 `PasswordRetryLoop`（`Services/PasswordRetryLoop.cs:72` 的 `promptPassword` 注入点确实能用，但拿回来的是 `PasswordRetryOutcome.Cancelled`，语义是「用户主动取消」，跟「无人值守下没有密码」不是一回事，混用会让退出码失真）。

在三个 CLI 路径上，`ResolveCliPassword` 返回 null **且**引擎确认加密**且** `CliHostPolicy.IsUnattended` 时，直接判失败：

| 位置 | 插入点 |
|---|---|
| `App.CliExtract.cs:114 TryExtractArchiveAsync` | `:142` 取到 password 之后 |
| `App.CliExtract.cs:162 TryExtractSmartAsync` | `:181` 取到 password 之后 |
| `App.CliExtract.cs:879`（批处理循环内） | `:879` 取到 password 之后，标该行 `Failed` 并 `continue`（不中断整批） |

统一走一个 helper：

```csharp
// App.CliExtract.cs 内
private static bool RejectUnattendedEncryptedArchive(string archivePath, IArchiveEngine engine, string? password)
{
    if (password is not null || !CliHostPolicy.IsUnattended) return false;
    if (!ArchiveService.HasEncryptedEntries(archivePath, engine)) return false;
    Console.Error.WriteLine($"Password required: {archivePath}");
    return true;
}
```

- 单文件路径：返回 `CliExitCode.PasswordRequired`
- 批处理路径：该行标 `Failed` + 原因 `T("Cli_StatusPasswordRequired")`，整批结束后若存在此类失败则退出码仍为 `Failure`（批处理里单条密码失败不升级成 2，避免一个坏包污染整批语义）

新增一个本地化 key（三语 `strings.*.json`）：`Cli_StatusPasswordRequired`（如「缺少密码」/「Password required」/「缺少密碼」）。

### 3. 错误进度窗：10s 兜底

`Dialogs/ProgressWindow.axaml.cs` 加一个实例级设置（**不加构造函数参数**——`ProgressWindow()` / `ProgressWindow(string)` 两个构造函数在 `src/` 里有 7 处 `new ProgressWindow(` 调用，加参数会波及 GUI 路径，且会诱导「每个调用点都要记得传」的散装写法）：

```csharp
/// <summary>无人值守宿主设置：错误压钉后最多等 N 秒再关（0 = 维持无限等）。</summary>
public void SetupUnattendedErrorTimeout(int seconds) => _unattendedErrorTimeoutMs = Math.Max(0, seconds) * 1000;
```

`AutoCloseOrWaitAsync` 的压钉分支（`:334`）改为：

```csharp
if (_vm.KeepOpenOnComplete)
{
    var timeout = _vm.HasErrors ? _unattendedErrorTimeoutMs : 0;
    if (await WaitForManualCloseAsync(timeout) == ManualWaitExit.Closed) { closeAction(); return; }
    continue;   // timeout==0 → 行为完全不变（无限等）
}
```

`WaitForManualCloseAsync` 加可选 `int timeoutMs = 0`：`timeoutMs > 0` 时在心跳循环里判断 `Stopwatch` 是否超时，超时返回 `ManualWaitExit.Unpinned`（复用现有语义「不再等」）。这样 `ManualWaitExit` 枚举不用加值。

置位点：`CliProgressPresenter.RunAsync` 建窗/用窗之后调用
`window.SetupUnattendedErrorTimeout(CliHostPolicy.UnattendedErrorCloseSeconds)`；`App.CliExtract.cs:635` / `:821` 自建进度窗的两处同样调用。

### 4. 退出码贯通

把四个 CLI 执行链的返回值从 `bool`（语义混乱：`TryExtractArchiveAsync` 的 bool 是「是否已重启进程」）改成 `int`（退出码）：

| 方法 | 现在 | 改为 |
|---|---|---|
| `App.CliExtract.cs:114 TryExtractArchiveAsync` | `Task<bool>`（true=已重启提权） | `Task<int>` |
| `App.CliExtract.cs:162 TryExtractSmartAsync` | `Task<bool>` | `Task<int>` |
| `App.CliExtract.cs:623 RunCliExtractWithProgressAsync` | `Task<bool>`（恒 false） | `Task<int>` |
| `App.CliExtract.cs:814 RunCliDirectExtractBatchAsync` | `Task` | `Task<int>` |
| `App.CliCompress.cs:413 CompressWithProgress` | `Task` | `Task<int>` |

调用侧：

```csharp
// App.CliExtract.cs:377（单文件）
var exitCode = await TryExtractArchiveAsync(...);
AppShutdown.Request(desktop.Shutdown, exitCode);
```

> 提权分支（`:129 HandleElevationAsync`）返回 `true` → 现在是 `AppShutdown.Request(desktop.Shutdown)` 不带码（新进程会重跑，退出码无意义）→ 保持 `CliExitCode.Success`。

汇总规则：

| 情形 | 退出码 |
|---|---|
| 全部成功 | `Success` |
| 任一失败 / 异常 / 部分批处理失败 | `Failure` |
| 无可用密码（仅单文件路径） | `PasswordRequired` |
| 用户取消（`OperationCanceledException`） | `Cancelled` |
| 提权重启（原进程退出） | `Success`（新进程重新执行） |

`CompressWithProgress`：`failedCount > 0` → `Failure`；`:497 catch` → `Failure`；其余 `Success`。

### 5. 文档

- `docs/CLI.md`：新增 `## 退出码` 章节（0/1/2/3 表）+ 一段「无人值守行为」说明（冲突自动改名、缺密码立即失败、错误窗 10s 自动关闭）。
- `docs/ARCHITECTURE.md:715 ### CLI entry points`：补一句宿主策略与退出码的归属（`CliHostPolicy` / `CliExitCode`）。

## 涉及文件

**新增**

- `src/MantisZip.UI.Avalonia/Services/CliHostPolicy.cs`
- `src/MantisZip.UI.Avalonia/Models/CliExitCode.cs`
- `tests/MantisZip.UI.Avalonia.Tests/CliHostPolicyTests.cs`
- `tests/MantisZip.UI.Avalonia.Tests/ProgressWindowUnattendedTimeoutTests.cs`

**修改**

- `src/MantisZip.UI.Avalonia/App.CliExtract.cs`（返回值签名 + 密码预检 + 冲突映射 + 进度窗超时）
- `src/MantisZip.UI.Avalonia/App.CliCompress.cs`（同上，压缩侧）
- `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs`（`SetupUnattendedErrorTimeout` + `WaitForManualCloseAsync` 超时）
- `src/MantisZip.UI.Avalonia/Services/CliProgressPresenter.cs`（置位 + 超时注入）
- `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`（`Cli_StatusPasswordRequired` 三语）
- `docs/CLI.md`、`docs/ARCHITECTURE.md`

**不动**

- `src/MantisZip.UI.Avalonia/Services/AppShutdown.cs`（漏斗已透传 exitCode）
- `src/MantisZip.UI.Avalonia/Program.cs`（`StartWithClassicDesktopLifetime` 返回 void）
- `src/MantisZip.UI.Avalonia/App.CliInfra.cs`（`HandleShellCommand` 已有正确退出码语义）
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

用便携模式隔离（`Portable.txt` + `Data\settings.json`），**每次全新随机目录 + 随机包名**，`.NET ProcessStartInfo` 读 `ExitCode`（不能用 pwsh 的 `$LASTEXITCODE`，见上文实测陷阱）：

| 场景 | 期望退出码 | 期望耗时 | 现状 |
|---|---|---|---|
| 正常 zip `--extract-here` | 0 | < 5s | 7317ms（5s 倒计时）→ 修复后仍 ~7.3s（成功路径不属本次范围） |
| 损坏 zip `--extract-here` | 1 | < 15s（10s 兜底） | HUNG → 修复后约 10s 退出 |
| 截断 zip `--extract-here` | 1 | < 15s | HUNG |
| 加密 7z 无密码 `--extract-here` | 2 | < 5s | HUNG |
| 同名冲突（`ask`）`--extract-here` | 0（改名成功） | < 5s | HUNG |
| 不存在的包 `--extract-here` | 1 | < 5s | 0（错） |
| 损坏包 `--compress-quick` | 1 | < 15s | 待测 |
| 无参数（GUI） | 不受影响，正常显示主窗口 | — | — |

**成功路径的 5s 倒计时不在本计划内**：那是 `AppSettings.AutoCloseDelaySeconds` 的 GUI 语义（用户确认窗口），改它会影响桌面体验。若将来要优化 CLI 启动开销，另立计划按宿主分流。

### 回归

- `AppShutdownWiringTests`（4 例）：`NoBareDesktopShutdownCalls_RemainInExitPaths` 会拦住新增的裸 `desktop.Shutdown(`，必须走 `AppShutdown.Request`
- `ProgressWindowPinTests`（2 例）：GUI 压钉行为不能变
- `ProgressViewModelTests` 里 8 处压钉断言：`HasErrors` → 压钉的逻辑保留，只多了「无人值守下超时」
- `FactoryDefaultsTests:22`：`AutoCloseDelaySeconds` 默认值仍是 5

## 边界

- **只修行为，不动工程结构**：不新增 `MantisZip.Cli.csproj`、不改 `OutputType=WinExe`、不动安装器与发布流水线。独立 CLI exe 是另一个话题（拆不拆与本次无关）。
- **不修输出重定向**：`MantisZip.exe --help > help.txt` 在 `cmd` / `pwsh` 下仍拿不到内容（`WinExe` + `AttachConsole` 的固有行为，实测 0 字节）。这是子系统问题，只有独立 exe 或改 `OutputType` 才能根治，明确不在本计划内。**但退出码不受此影响**（`Process.ExitCode` 正常）。
- **不改 `AppSettings.AutoCloseDelaySeconds` 默认值**，不改 GUI 压钉语义。
- **不新增 CLI 参数**（无 `--password` / `--quiet` / `--yes`）。
- **不碰规则 20 的 `dialog-seam` 守卫**：本计划不新建 `CompressSettingsWindow` / `ExtractSettingsWindow`。
- **`docs/CLI.md:3` 的对外承诺暂不改口径**：修复后「自动化脚本」这句才成立；若实施中发现仍有场景无法脚本化，按规则 21 登记 KI，不要悄悄改文案。
