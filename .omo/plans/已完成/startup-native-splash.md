# 原生 Win32 启动 Splash（覆盖进程冷启动）

> **状态**: 📋 未开始（前置一期 A+B+C——CollectingWindow 收集弹窗 / MainWindow IsLoading 遮罩 / --extract 立即弹窗——已实施；本计划 NativeSplash 本体未实现）

## 背景

用户反馈：打开大压缩包 / 右键压缩大目录时，窗口迟迟不出现，用户不知道该等还是不等。

已实施的三项修复（`startup-feedback` 一期）：

| # | 修复 | 覆盖阶段 |
|---|------|---------|
| A | `--compress` IPC 收集期间立即显示纯文字弹窗 `CollectingWindow`「正在收集文件…」（无边框、无按钮） | Avalonia 初始化完成之后 |
| B | `MainWindow` 增加 `IsLoading` 加载遮罩「正在打开压缩包…」 | Avalonia 初始化完成之后 |
| C | `--extract`（解压到…）立即弹出 `ExtractSettingsWindow`，条目列表后台加载后 `SetEntries`（消除弹窗前最长 3s 的条目读取空白期） | Avalonia 初始化完成之后 |

**仍未被覆盖的阶段**：进程冷启动（.NET runtime + Avalonia 框架初始化 + JIT + 主题/设置加载，约 1–2s）。
此阶段发生在 `Program.Main` → `OnFrameworkInitializationCompleted` 之间，Avalonia 尚未就绪，
任何 Avalonia 窗口（含 A/B/C 的反馈）都无法显示。本计划用**原生 Win32 窗口**覆盖这段空窗期。

**覆盖范围扩大（2026-10-12 决策）**：不仅 CLI 慢路径，**无参普通启动也纳入 splash**。
实测依据（`startup-trace.log`，`startup-preview-defer` 完成后新基线）：无参启动到主窗口可见
**TOTAL≈2160ms**（`Avalonia.InitDone` ~950ms + `Init.*` ~140ms + MainWindow 构造+首帧 ~1070ms），
白屏期比 CLI compress 到 CollectingWindow 出现的 ~1s 更长，原「普通启动约 1s 不值得闪」的排除理由不成立。
splash 文案对无参启动用新增 key `Status_Starting`（「正在启动…」），CLI 场景按命令选择现有 key。

> **注意**：若 `progress-window-enhancement` 先行实施，ProgressWindow 布局变更（三种模式切换）可能影响窗口尺寸和打开时序，splash 关闭时机需回归验证。

## 平台范围（Windows-only）

本计划为 **Windows 专有**实现（纯 Win32 P/Invoke），依据：

- 项目当前仅支持 Windows；跨平台移植见 [cross-platform-port.md](cross-platform-port.md)
- 冷启动 1–2s 空窗在 macOS / Linux 同样存在，但解法各异：macOS 惯例为 Dock 图标弹跳（系统自动提供，用户容忍度高）；Linux 无统一惯例（X11/Wayland 分裂），投入产出比差
- 跨平台移植时本功能归入**「砍掉」类**（与 `OverlayController` / ShellExt COM 同列）——非 Windows 无 splash，除非届时有平台原生需求再另行立项

**跨平台安全护栏**：`NativeSplash.Show()` 第一行 `if (!OperatingSystem.IsWindows()) return;`——
一行代码使实现天然安全，移植时 `Program.Main` / `App.axaml.cs` 的挂接点无需改动
（`Hide()` 的 `Opened` 钩子与 `Status_Starting` 文案 key 均为纯 Avalonia/本地化代码，本身跨平台安全）。

## 目标

- **所有启动路径**（无参普通启动 + 全部 CLI 慢路径）进程启动后 **0.5s 内**在屏幕上出现启动提示窗口
  （文案按启动方式选择，见实现要点；仅非 UI 命令如 `--install-shell`/`--help` 排除）
- 不依赖 Avalonia / WPF / WinForms，仅用 P/Invoke 纯 Win32
- 与 A/B 无缝衔接：splash 在 Avalonia **终端窗口** `Opened` 时关闭（终端窗口按场景枚举，见实现要点）

## 方案

### 总体流程

```
Program.Main
 ├─ 解析 CLI 参数：是否属于非 UI 命令（--install-shell / --uninstall-shell / --help 等）
 ├─ 非 UI 命令 → 不显示 splash（进程秒退，无需反馈）
 ├─ 其余（含无参普通启动 + 全部 CLI 慢路径）→ 启动 splash 线程（原生 Win32 窗口 + 独立消息泵），立即可见
 └─ BuildAvaloniaApp().StartWithClassicDesktopLifetime(...)  // 冷启动照常进行
        └─ Avalonia 终端窗口 Opened 事件（见下方终端窗口表）
              └─ 向 splash 线程 PostMessage(WM_CLOSE) → splash 线程退出消息泵 → 线程结束
```

### 实现要点

1. **splash 线程**：`Thread` 启动（**`IsBackground = true`**——否则 desktop.Shutdown() 后
   前台 splash 线程的消息泵会阻止进程退出），线程内：
   - `RegisterClassExW` 注册无边框窗口类（`CS_HREDRAW|CS_VREDRAW`）
   - `CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE, ...)`——不抢焦点、不进任务栏
   - 窗口居中（`GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN)` 或 `MonitorFromPoint`）
   - 消息泵：`PeekMessageW` + `TranslateMessage` + `DispatchMessageW` 循环，收到 `WM_CLOSE` 退出
   - `WM_PAINT` 用 GDI 绘制：`FillRect`（背景色）+ `TextOutW`（文案）
   - 文案不写死：从 `AppSettings`（独立于 Avalonia 的纯 .NET 单例）读取当前语言，
     并按启动方式选择 key（见下方「splash 文案选择」）
2. **DPI**：进程级 `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)`（启动最早处调用），
   窗口尺寸/字体按当前 DPI 缩放
3. **关闭时机**：Avalonia 侧在 `App.OnFrameworkInitializationCompleted` 的 CLI 分支中，
   给**终端窗口**挂 `Opened += ...`，回调里 `FindWindow` 或直接持有 splash 窗口句柄 →
   `PostMessage(hwnd, WM_CLOSE, 0, 0)`。终端窗口按 CLI 场景枚举（见下表）——
   **不是**「第一个创建的窗口」，因为 `--compress` 的第一个窗口是 `CollectingWindow`，
   若在它 Opened 时关 splash，IPC 收集的 800ms 又会回到无反馈状态。
4. **协调 IPC 800ms**：splash 生命周期 = 冷启动 + IPC 收集 + 终端窗口出现。
   `CollectingWindow`（A）在 splash 存活期间位于其下方（splash `WS_EX_TOPMOST`），
   对用户不可见但无害；splash 在 `CompressSettingsWindow` `Opened` 时立即关闭，无重叠窗口切换感

#### 终端窗口枚举（splash 关闭时机）

| CLI 命令 | 第一个 Avalonia 窗口 | 终端窗口（挂 `Opened` 关 splash） | 说明 |
|---|---|---|---|
| （无参普通启动） | `MainWindow` | `MainWindow` | 主窗口首帧后关闭 |
| `--compress` | `CollectingWindow` | **`CompressSettingsWindow`** | `CollectingWindow` 是中间态，不能提前关 |
| `--compress-quick` | `ProgressWindow` | `ProgressWindow` | 无 IPC 收集，进度窗即终端 |
| `--compress-separate` | `ProgressWindow` | `ProgressWindow` | 收集期进度窗预填充，进度窗即终端 |
| `--compress-combined` | `ProgressWindow` | `ProgressWindow` | 收集期无独立窗口，进度窗即终端 |
| `--extract` | `ExtractSettingsWindow` | `ExtractSettingsWindow` | 条目后台加载，对话框即终端 |
| `--extract-here` / `--extract-to-name` / `--extract-smart`（单文件） | `ProgressWindow` | `ProgressWindow` | 直接解压，进度窗即终端 |
| `--extract-*`（多文件批处理） | `ProgressWindow` | `ProgressWindow` | 同上 |
| `--open` / `--open-dispatch` | `MainWindow` | `MainWindow` | 加载遮罩（B）接管后续反馈 |
| `--install-shell` 等无 UI 命令 | — | — | 不显示 splash |

#### splash 文案选择

文案在 `Program.Main` 启动 splash 线程前按启动方式确定（`AppSettings` 语言 + 命令）：

| 启动方式 | key | zh-CN 文案 | 备注 |
|---|---|---|---|
| （无参普通启动） | `Status_Starting` | 正在启动… | **新增 key**，三语成对添加（zh-CN / en / zh-TW） |
| `--compress` / `--compress-separate` / `--compress-combined` | `App_CompressCollecting` | 正在收集文件... | 现有 key |
| `--compress-quick` | `Cli_QuickCompress` 相关或 `Status_Compressing`（实施时按现有 key 就近选） | 正在压缩... | 现有 key |
| `--extract*` | `Progress_Title_Extract` 或 `Status_OpeningArchive`（实施时定） | 正在解压... | 现有 key |
| `--open` / `--open-dispatch` | `Status_OpeningArchive` | 正在打开压缩包… | 现有 key |

#### CollectingWindow 去留

**保留不动**。理由：

1. **降级兜底**：splash 初始化失败时（静默降级，见边界条件），`CollectingWindow` 是唯一的收集期反馈——删掉会在降级路径上留下空窗
2. **时序无冲突**：正常路径下 IPC 收集全程位于 splash 生命周期内（splash 在 `CompressSettingsWindow` `Opened` 时才关，而 `CollectingWindow` 在其之前已 `Close()`），splash `WS_EX_TOPMOST` 盖住中间态，二者不打架
3. **零维护成本**：已有本地化、主题适配，删除反而要动 `HandleCompress` 现有时序

### 代码落点

- 新增 `src/MantisZip.UI.Avalonia/Services/NativeSplash.cs`（静态类：`Show()` / `Hide()`，
  内部管理 splash 线程 + 窗口句柄）
- `Program.Main`：非 UI 命令判断为假时（含无参普通启动 + 全部 CLI 慢路径）调用 `NativeSplash.Show()`
  （需先读 `AppSettings.Load()` 判断语言与主题色，
  注意此时 Avalonia 未初始化，`AppSettings` 不依赖 Avalonia 可安全使用）
- `App.axaml.cs`：无参分支 `MainWindow` 创建处与 CLI 各**终端窗口**创建处挂 `Opened` → `NativeSplash.Hide()`（终端窗口枚举见上表）
- 复用现有 `NativeMethods`（`src/MantisZip.UI.Avalonia/NativeMethods.cs`）的 P/Invoke 基础设施

### 边界条件

- **非 UI 命令**（`--install-shell` / `--uninstall-shell` / `--install-assoc` / `--uninstall-assoc` /
  `--help` / `--test` 等秒退命令）：不显示 splash（无窗口创建，splash 会悬置无法关闭）
- **快速冷启动**（已预热）：splash 出现 <200ms 即关闭——用「最短显示时长」防闪烁
  （如强制显示 300ms 再关）
- **splash 线程崩溃**：静默降级（try-catch 包裹，失败即跳过），不影响主流程
- **splash 与真实窗口同时可见的瞬间**：splash 置 `WS_EX_TOPMOST`，真实窗口在其下方出现，
  关闭 splash 时无缝过渡；或将 splash 设为真实窗口的 Owner 无关——用 TOPMOST 即可

## 备选方案（不采用）

**Avalonia in-process splash**：在 `OnFrameworkInitializationCompleted` 最早处
`desktop.MainWindow = 轻量 splash 窗口`。实现极简，但**无法覆盖 Avalonia 初始化前的冷启动时间**
（冷启动正是本计划要覆盖的时段），故不采用；可作为本计划的兜底简化版。

## 验收标准

1. 无参普通启动冷启动：进程启动 → splash「正在启动…」→ MainWindow 首帧，全程无 >500ms 的完全无反馈期
2. `--compress <大目录>` 冷启动：进程启动 → splash「正在收集文件...」→ 收集窗口 → 压缩设置对话框，
   全程无 >500ms 的完全无反馈期
3. `--open <大压缩包>` 冷启动：splash → MainWindow + 加载遮罩，无缝衔接
4. `--install-shell` / `--help` 等非 UI 命令：无 splash，秒退行为与现状一致

## 实施结果（2026-10-20 回填）

**状态**： ✅ 核心已实施并冒烟验证（commit `c5fd2df`）

- **交付**：`Services/NativeSplash.cs`（独立后台线程 + 独立消息泵 + GDI 绘制 + `OperatingSystem.IsWindows()` 跨平台护栏 + 静默降级 try-catch）、`NativeMethods.cs` 新增 8 个 P/Invoke（PostMessage/GetMessage/PostQuitMessage/GetClientRect/FillRect/SetProcessDpiAwarenessContext/GetDpiForSystem/SetTextColor/CreateFont）+ 新常量、`Program.Main` 非 UI 命令判定（`--install-shell/--uninstall-shell/--install-assoc/--uninstall-assoc/--help/-h/--test` 秒退不显示）+ 扩展名判定 + 启动 splash（文字按 Language + 启动方式 key 选，主题色据 `AppSettings.Theme`）、`App.axaml.cs` 8 类终端窗口统一 `NativeSplash.Hook(window)`（ epidemiology per 表：无参/`--open`/`--open-dispatch` 3 分支/default/`--extract`/`--extract-here`/`--compress`/`--compress-*-`...）、新 `Status_Starting` key 三语成对添加
- **冒烟实测（Release 构建）**：`--test` 不显示 splash（skip 路径算定）；无参冷启动：splash `MantisZipNativeSplash` 顶层出现 **196ms**（<500ms 门控 ✓）→ 主窗出现 → **~1.2s 时熄灭**（Opened 钩子 + MinShowMs 300ms 抑制闪烁）→ 进程稳定存活 ✓
- **调试教训（重要！）**：① `NativeMethods.RegisterClassEx` 默认路径解析到 `RegisterClassExA`—— 用 `Sprite` 注册的类与 CreateWindowExW 查找不匹配，`lastErr=1407 ERROR_CANNOT_FIND_WND_CLASS`；② **带 `CharSet.Unicode` 的 string 字段 struct + `GetClassInfoEx`（读出 LPWSTR 的 ref-marshaling 时释放垃圾指针）→ `ntdll 0xC0000374 HEAP_CORRUPTION` 静默退进程（无 debug.log 无 lifecycle）**——共复查 5 次 WER 才定位；③ **最终可靠方案 = 全部窗口名以 `Marshal.StringToHGlobalUni` 生成 nint 指针传入**（WNDCLASSEX.lpszClassName / CreateWindowEx.lpClassName 类型改 `nint`，彻底绕开 C# string 壳的 marshaling 不确定性——同样正确适用于自定义 Win32 窗口类场景）
- **GUI 手动验收清单（待做）**：焦点不抢（焦点直达真实窗口）/任务栏不显示/`--compress <目录>` 全链路 /`--open <压缩包>` 全链路/最短显示时长/150-200% DPI 清晰/语言切换后 splash 文案
- **对已有沉浸式的全部承接**：无参数启动 → splash「正在启动…」；CLI 慢路径逐场景 key 对应；关闭时机在该 CLI 自有「终端窗口」出现时（CollectingWindow 保留为降级兜底）
5. splash 不抢焦点（焦点直达真实窗口）、不进任务栏、不闪烁（最短显示时长生效）
6. 高 DPI（150%/200%）下 splash 尺寸/文字清晰
7. `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 通过

## 工时预估

3–4h（含 Win32 绘制 + DPI + 生命周期协调 + 回归验证）
