# 原生 Win32 启动 Splash（覆盖进程冷启动）

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

## 目标

- 进程启动后 **0.5s 内**在屏幕上出现「正在打开…」提示窗口
- 不依赖 Avalonia / WPF / WinForms，仅用 P/Invoke 纯 Win32
- 与 A/B 无缝衔接：splash 在 Avalonia 第一个窗口 `Opened` 时关闭

## 方案

### 总体流程

```
Program.Main
 ├─ 解析 CLI 参数：是否属于慢路径（--open / --compress / --compress-* / --extract*）
 ├─ 慢路径 → 启动 splash 线程（原生 Win32 窗口 + 独立消息泵），立即可见
 └─ BuildAvaloniaApp().StartWithClassicDesktopLifetime(...)  // 冷启动照常进行
        └─ Avalonia 第一个窗口 Opened 事件
              └─ 向 splash 线程 PostMessage(WM_CLOSE) → splash 线程退出消息泵 → 线程结束
```

### 实现要点

1. **splash 线程**：`Thread` 启动，线程内：
   - `RegisterClassExW` 注册无边框窗口类（`CS_HREDRAW|CS_VREDRAW`）
   - `CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE, ...)`——不抢焦点、不进任务栏
   - 窗口居中（`GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN)` 或 `MonitorFromPoint`）
   - 消息泵：`PeekMessageW` + `TranslateMessage` + `DispatchMessageW` 循环，收到 `WM_CLOSE` 退出
   - `WM_PAINT` 用 GDI 绘制：`FillRect`（背景色）+ `TextOutW`（「正在打开…」文案）
   - 文案不写死：从 `AppSettings`（独立于 Avalonia 的纯 .NET 单例）读取当前语言，对应现有
     `Status_OpeningArchive` key 的中/英文
2. **DPI**：进程级 `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)`（启动最早处调用），
   窗口尺寸/字体按当前 DPI 缩放
3. **关闭时机**：Avalonia 侧在 `App.OnFrameworkInitializationCompleted` 的 CLI 分支中，
   给第一个创建的窗口（MainWindow / CompressSettingsWindow / ProgressWindow）挂 `Opened += ...`，
   回调里 `FindWindow` 或直接持有 splash 窗口句柄 → `PostMessage(hwnd, WM_CLOSE, 0, 0)`
4. **协调 IPC 800ms**：splash 生命周期 = 冷启动 + IPC 收集 + 第一个真实窗口出现。
   A 的 `ProgressWindow` / `CompressSettingsWindow` 出现时 splash 立即关闭，无重叠窗口切换感

### 代码落点

- 新增 `src/MantisZip.UI.Avalonia/Services/NativeSplash.cs`（静态类：`Show()` / `Hide()`，
  内部管理 splash 线程 + 窗口句柄）
- `Program.Main`：慢路径分支调用 `NativeSplash.Show()`（需先读 `AppSettings.Load()` 判断语言与主题色，
  注意此时 Avalonia 未初始化，`AppSettings` 不依赖 Avalonia 可安全使用）
- `App.axaml.cs` CLI 分支：各真实窗口创建处挂 `Opened` → `NativeSplash.Hide()`
- 复用现有 `NativeMethods`（`src/MantisZip.UI.Avalonia/NativeMethods.cs`）的 P/Invoke 基础设施

### 边界条件

- **非慢路径（无参普通启动）**：不显示 splash（普通启动约 1s，不值得闪一下）
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

1. `--compress <大目录>` 冷启动：进程启动 → splash「正在打开…」→ 收集窗口 → 压缩设置对话框，
   全程无 >500ms 的完全无反馈期
2. `--open <大压缩包>` 冷启动：splash → MainWindow + 加载遮罩，无缝衔接
3. splash 不抢焦点（焦点直达真实窗口）、不进任务栏、不闪烁（最短显示时长生效）
4. 高 DPI（150%/200%）下 splash 尺寸/文字清晰
5. `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 通过

## 工时预估

3–4h（含 Win32 绘制 + DPI + 生命周期协调 + 回归验证）
