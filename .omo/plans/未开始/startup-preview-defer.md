# 启动优化：打点测量 + 预览 UI 延迟实例化 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 定位 Release 冷启动 3s 的耗时分布（阶段一打点），并按数据门控把预览面板控件树的创建挪出启动关键路径（阶段二延迟实例化）。

**Architecture:** 阶段一新增零分配内存打点器 `StartupTimer`，覆盖 `Program.Main`（含 pre-Main 进程启动基线）→ `OnFrameworkInitializationCompleted` 子段 → `MainWindow` ctor 子段 → 首帧，flush 到独立 `startup-trace.log`；专项隔离 `PreviewPanel`/`PreviewViewModel` 构造成本。阶段二（数据门控）把 `MainWindow.axaml` 内嵌的 `PreviewPanel` 换成占位 `ContentControl`，窗口首帧后 0.5s 定时 + 首次预览请求兜底地幂等创建，把 ~855 行控件树构造挪出启动与「打开压缩包」关键路径。

**Tech Stack:** .NET 10 / Avalonia 12 / xunit / System.Diagnostics.Stopwatch / PowerShell

**设计依据:** 与用户三节确认的讨论结论（2026-10-10）：
- .NET 对第三方程序集（PdfPig/SkiaSharp/Markdig 等）已是按需 JIT + 加载，「按格式加载模块」收益≈0，真正的急切成本是 `PreviewPanel` 控件树（`MainWindow.axaml:1271` 内嵌，855 行 XAML）与 `PreviewViewModel` 构造（`MainWindowViewModel.cs:327`）。
- 启动慢为 Release 构建体感 3s+（用户确认场景 A）；项目当前零打点，先测量再动刀。
- 与 `.omo/plans/未开始/startup-native-splash.md` 串行衔接：本计划打点先行，splash 计划随后；共享 `StartupTimer`。
- 约束：不做文件结构拆分（其他分支在开发）；不做插件化/ReadyToRun/NativeAOT（单独立项另议）；不改版本号。

**决策规则（阶段一门控，预先锁定）：**
- `Preview.Panel.Ctor + Preview.VM.Ctor ≥ 总时长 10%` 或 `≥ 300ms` → 执行阶段二；否则跳过，按 Top 热点另行立项。
- `Preview.VM.Ctor` 单独显著（≥100ms）→ 追加 VM ctor 瘦身（附加任务）。

---

## 文件结构总览

| 文件 | 动作 | 职责 |
|---|---|---|
| `src/MantisZip.UI.Avalonia/Services/StartupTimer.cs` | 新建 | 打点器（internal static，纯内存，幂等 flush） |
| `tests/MantisZip.UI.Avalonia.Tests/StartupTimerTests.cs` | 新建 | 打点器单测 |
| `src/MantisZip.UI.Avalonia/Program.cs` | 修改 | `Main` 首行 `Begin()` + `AppBuilder` 打点 |
| `src/MantisZip.UI.Avalonia/App.axaml.cs` | 修改 | `OnFrameworkInitializationCompleted` 子段打点 |
| `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs` | 修改 | ctor 子段打点 + `Opened`/首帧打点 + flush +（阶段二）`EnsurePreviewPanel` |
| `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml` | 修改 | （阶段二）`PreviewPanel` → 占位 `ContentControl` |
| `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml.cs` | 修改 | ctor 专项打点 |
| `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs` | 修改 | ctor 专项打点（+门控追加：ctor 瘦身） |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | 修改 | （阶段二）`PreviewPanelNeeded` 事件 + 预览链触发 |
| `tools/measure-startup.ps1` | 新建（可选） | Release 重复启动 + 中位数聚合 |

`StartupTimer` 用 `internal`——`MantisZip.UI.Avalonia.csproj:17-19` 已有 `InternalsVisibleTo` 给两个测试项目，无需新增。

---

## 阶段一：启动打点（必做）

### Task 1: StartupTimer 打点器（TDD）

**Files:**
- Create: `src/MantisZip.UI.Avalonia/Services/StartupTimer.cs`
- Test: `tests/MantisZip.UI.Avalonia.Tests/StartupTimerTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// tests/MantisZip.UI.Avalonia.Tests/StartupTimerTests.cs
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.Tests.UI;

/// <summary>
/// StartupTimer 打点器单测。注意：打点器是进程级单例，
/// 每个测试方法通过 ResetForTest() 隔离（internal，经 InternalsVisibleTo）。
/// </summary>
public class StartupTimerTests
{
    public StartupTimerTests() => StartupTimer.ResetForTest();

    [Fact]
    public void Begin_ThenMark_RecordsEntryAndMarks()
    {
        StartupTimer.Begin();
        StartupTimer.Mark("PhaseA");

        var marks = StartupTimer.SnapshotForTest();
        Assert.Equal(new[] { "Main.Entry", "PhaseA" }, marks.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Mark_DeltasAreNonNegative()
    {
        StartupTimer.Begin();
        Thread.Sleep(10); // 制造真实间隔
        StartupTimer.Mark("Slow");

        var marks = StartupTimer.SnapshotForTest();
        var slow = marks.Single(m => m.Name == "Slow");
        Assert.True(slow.DeltaMs >= 5, $"DeltaMs={slow.DeltaMs}");
        Assert.True(slow.CumulativeMs >= slow.DeltaMs);
    }

    [Fact]
    public void Mark_BeforeBegin_IsIgnored()
    {
        StartupTimer.Mark("TooEarly");
        Assert.Empty(StartupTimer.SnapshotForTest());
    }

    [Fact]
    public void Mark_AfterFlush_IsIgnored()
    {
        StartupTimer.Begin();
        StartupTimer.Mark("Before");
        StartupTimer.FlushForTest(); // 标记已 flush，不写文件
        StartupTimer.Mark("After");

        Assert.DoesNotContain(StartupTimer.SnapshotForTest(), m => m.Name == "After");
    }

    [Fact]
    public void Begin_Twice_IsIdempotent()
    {
        StartupTimer.Begin();
        var first = StartupTimer.SnapshotForTest().Count;
        StartupTimer.Begin();
        Assert.Equal(first, StartupTimer.SnapshotForTest().Count);
    }

    [Fact]
    public void Disabled_AfterError_NeverThrows()
    {
        StartupTimer.SimulateFailureForTest();
        StartupTimer.Begin();
        StartupTimer.Mark("X");
        StartupTimer.FlushForTest(); // 全部静默 no-op，不抛异常
        Assert.Empty(StartupTimer.SnapshotForTest());
    }

    [Fact]
    public void Snapshot_ContainsProcessStartBaseline()
    {
        StartupTimer.Begin();
        var marks = StartupTimer.SnapshotForTest();
        // Main.Entry 的 CumulativeMs 含 pre-Main 进程启动基线，应大于 0
        Assert.True(marks.Single(m => m.Name == "Main.Entry").CumulativeMs >= 0);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter StartupTimerTests`
Expected: 编译失败（`StartupTimer` 不存在）。

- [ ] **Step 3: 实现 StartupTimer**

```csharp
// src/MantisZip.UI.Avalonia/Services/StartupTimer.cs
using System.Diagnostics;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 启动耗时打点器：纯内存收集（零分配 mark 名字符串字面量、零 I/O），
/// 窗口首帧后一次性 flush 到 %LOCALAPPDATA%\MantisZip\startup-trace.log。
/// 设计约束：打点期间禁止触碰 App.DebugLog / AppSettings 等可能耗时或递归依赖的组件；
/// 任何异常静默置 _disabled，绝不影响启动主流程。
/// </summary>
internal static class StartupTimer
{
    /// <summary>单条打点记录（struct，零堆分配追加）。</summary>
    internal readonly record struct MarkInfo(string Name, double CumulativeMs, double DeltaMs);

    private static readonly List<MarkInfo> Marks = new(64);
    private static readonly object Gate = new();
    private static Stopwatch? _sw;
    private static bool _begun;
    private static bool _flushed;
    private static bool _disabled;
    private static long _lastTicks;
    private static double _preMainMs; // 进程启动 → Main.Entry 的墙钟近似（毫秒）

    /// <summary>Main 第一行调用：记录进程启动基线并启动 T0，随后打 "Main.Entry"。</summary>
    public static void Begin()
    {
        if (_disabled || _begun) return;
        try
        {
            // 进程启动时刻 → StopWatch 域的 pre-Main 基线（精度 ±10ms 量级，对 3s 量级足够）
            var procStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            _preMainMs = Math.Max(0, (DateTime.UtcNow - procStart).TotalMilliseconds);
            _sw = Stopwatch.StartNew();
            _lastTicks = 0;
            _begun = true;
            AddMark("Main.Entry");
        }
        catch { _disabled = true; }
    }

    /// <summary>记录一个阶段点（相对上一 mark 的增量 + 相对 T0 的累计）。</summary>
    public static void Mark(string name)
    {
        if (_disabled || !_begun) return;
        try { AddMark(name); } catch { _disabled = true; }
    }

    private static void AddMark(string name)
    {
        var sw = _sw!;
        var ticks = sw.ElapsedTicks;
        var freq = (double)Stopwatch.Frequency;
        var cumulativeMs = _preMainMs + ticks / freq * 1000.0;
        var deltaMs = (ticks - _lastTicks) / freq * 1000.0;
        _lastTicks = ticks;
        lock (Gate)
        {
            if (_flushed) return;
            Marks.Add(new MarkInfo(name, cumulativeMs, deltaMs));
        }
    }

    /// <summary>
    /// 格式化并追加写入 startup-trace.log（幂等，仅首次生效）。
    /// 窗口首帧后调用一次；App 退出前可再调（no-op）兜底 CLI 慢路径。
    /// </summary>
    public static void Flush(string reason)
    {
        if (_disabled || !_begun) return;
        string text;
        lock (Gate)
        {
            if (_flushed) return;
            _flushed = true;
            text = FormatTrace(reason);
        }
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "startup-trace.log");
            File.AppendAllText(path, text + Environment.NewLine);
            TrimLogFile(path, maxEntries: 20);
        }
        catch { /* flush 失败不影响主流程 */ }
    }

    private static string FormatTrace(string reason)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"==== MantisZip Startup Trace | {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | reason={reason} ====");
        sb.AppendLine($"build: {(Debugger.IsAttached ? "Debugger" : "Release/Debug?")} | os: {Environment.OSVersion.Version} | logicalCpu: {Environment.ProcessorCount}");
        try { sb.AppendLine($"cmdline: {Core.Utils.LogRedactor.RedactPaths(Environment.CommandLine)}"); } catch { }
        sb.AppendLine($"  {"phase",-28} {"delta",10} {"cumulative",12}");
        foreach (var m in SnapshotForTest())
            sb.AppendLine($"  {m.Name,-28} {m.DeltaMs,9:F1}ms {m.CumulativeMs,10:F1}ms");
        var total = SnapshotForTest().Count > 0 ? SnapshotForTest()[^1].CumulativeMs : 0;
        sb.AppendLine($"TOTAL to last mark = {total:F1}ms");
        // 预览专项合计与占比（直接对应阶段二门控）
        double previewMs = 0;
        foreach (var m in SnapshotForTest())
            if (m.Name.StartsWith("Preview.", StringComparison.Ordinal)) previewMs += m.DeltaMs;
        if (previewMs > 0)
            sb.AppendLine($"preview-eager total = {previewMs:F1}ms ({previewMs / Math.Max(total, 1) * 100:F1}%)");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>只保留最近 maxEntries 条样本（每条以 "==== MantisZip Startup Trace" 起始）。</summary>
    private static void TrimLogFile(string path, int maxEntries)
    {
        var all = File.ReadAllLines(path);
        var starts = new List<int>();
        for (int i = 0; i < all.Length; i++)
            if (all[i].StartsWith("==== MantisZip Startup Trace", StringComparison.Ordinal))
                starts.Add(i);
        if (starts.Count <= maxEntries) return;
        var cut = starts[starts.Count - maxEntries];
        File.WriteAllLines(path, all.Skip(cut));
    }

    // ── 测试钩子（internal，经 InternalsVisibleTo）──

    internal static IReadOnlyList<MarkInfo> SnapshotForTest()
    {
        lock (Gate) return Marks.ToArray();
    }

    internal static void ResetForTest()
    {
        lock (Gate)
        {
            Marks.Clear(); _sw = null; _begun = false; _flushed = false;
            _disabled = false; _lastTicks = 0; _preMainMs = 0;
        }
    }

    /// <summary>标记 flush 完成但不写文件（测试用）。</summary>
    internal static void FlushForTest()
    {
        lock (Gate) _flushed = true;
    }

    /// <summary>模拟异常导致的永久禁用（测试用）。</summary>
    internal static void SimulateFailureForTest() => _disabled = true;
}
```

注意补 `using System.Text;`（`StringBuilder`）。文件顶部加中文注释头（规则 14 精神）。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter StartupTimerTests`
Expected: 7 个测试全部 PASS。

- [ ] **Step 5: Commit**

```powershell
git add src/MantisZip.UI.Avalonia/Services/StartupTimer.cs tests/MantisZip.UI.Avalonia.Tests/StartupTimerTests.cs
git commit -m "feat(avalonia): 新增 StartupTimer 启动耗时打点器（纯内存+单次flush）"
```

---

### Task 2: 接入 Program.cs 与 App.axaml.cs 打点

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Program.cs:7-14`
- Modify: `src/MantisZip.UI.Avalonia/App.axaml.cs`（`OnFrameworkInitializationCompleted`，约 :54-152）

- [ ] **Step 1: Program.cs 入口打点**

将 `Main` 与 `BuildAvaloniaApp` 改为（`using MantisZip.UI.Avalonia.Services;` 加到文件头）：

```csharp
[STAThread]
public static void Main(string[] args)
{
    Services.StartupTimer.Begin(); // 必须是 Main 第一行：捕获 pre-Main 基线后的第一个点
    BuildAvaloniaApp();
    Services.StartupTimer.Mark("AppBuilder.Ready"); // UsePlatformDetect 等原生探测完成
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    // 注意：StartWithClassicDesktopLifetime 阻塞至退出，后续点位在 App/MainWindow 内打
    Services.StartupTimer.Flush("app-exit"); // CLI 慢路径兜底（主路径已在首帧 flush，此处幂等 no-op）
}
```

简化为单一 builder 引用（避免 BuildAvaloniaApp 调两次）：

```csharp
[STAThread]
public static void Main(string[] args)
{
    Services.StartupTimer.Begin();
    var builder = BuildAvaloniaApp();
    Services.StartupTimer.Mark("AppBuilder.Ready");
    builder.StartWithClassicDesktopLifetime(args);
    Services.StartupTimer.Flush("app-exit");
}
```

- [ ] **Step 2: App.OnFrameworkInitializationCompleted 子段打点**

在方法内以下语句之后各插一行 `Services.StartupTimer.Mark(...)`（顺序与现有代码一致）：

| 插点（现有代码位置） | Mark 名 |
|---|---|
| `:59-63` OLE 初始化后 | `Init.EncodingOle` |
| `:66` `ApplyTheme();` 后 | `Init.Theme` |
| `:79` `ApplyAppFontFamily();` 后 | `Init.Font` |
| `:89` `ApplyCompactness(compactMode);` 后 | `Init.Settings` |
| `:102` PreviewService 配置块后（语言恢复 `:105-106` 之后） | `Init.PreviewCfgLocale` |
| `:124` 7z 路径块后、`:124` resolve 回调注册后 | `Init.SevenZip` |
| 首次运行 Shell/注册表块之后（约 `:152` 之后） | `Init.ShellFirstRun` |
| 方法末尾（CLI switch 之前） | `Init.Done` |

示例（以 `Init.Theme` 为例，其余同形）：

```csharp
// ── Apply theme (System/Light/Dark) ──
ApplyTheme();
Services.StartupTimer.Mark("Init.Theme");
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 error。

- [ ] **Step 4: Commit**

```powershell
git add src/MantisZip.UI.Avalonia/Program.cs src/MantisZip.UI.Avalonia/App.axaml.cs
git commit -m "feat(avalonia): 启动打点接入 Program.Main 与 App 初始化子段"
```

---

### Task 3: 接入 MainWindow / PreviewPanel / PreviewViewModel 打点

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs:63-141`（ctor）
- Modify: `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs`（新增 `Window_Opened` 处理器）
- Modify: `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml.cs:26-33`
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs:159-167`

- [ ] **Step 1: MainWindow ctor 子段打点**

```csharp
public MainWindow()
{
    Services.StartupTimer.Mark("Win.Ctor.Enter");
    InitializeComponent();
    Services.StartupTimer.Mark("Win.Xaml"); // 1359 行 XAML 解析 + 子控件构造（含内嵌 PreviewPanel）

    // ... 现有拖拽计时器 / TestMenu / 图标 ...

    var columnStates = WindowStateManager.Load(this, out var savedSortColumnPath, out var savedSortDirection);
    ApplyColumnStates(columnStates);
    _lastSortMemberPath = string.IsNullOrEmpty(savedSortColumnPath) ? null : savedSortColumnPath;
    _lastSortDescending = savedSortDirection == 2;
    UpdateSortArrows();
    Services.StartupTimer.Mark("Win.State"); // WindowStateManager.Load 读盘 + 列状态恢复

    ApplySavedLayout();
    ApplyPreviewLayout();
    Services.StartupTimer.Mark("Win.Layout"); // 布局快照恢复 + 预览位置应用

    var vm = new MainWindowViewModel();
    Services.StartupTimer.Mark("Win.VM"); // 主 VM 构造（内含 PreviewViewModel 构造）

    // ... 现有回调接线（GetOpenFilePath / ShowSettingsWindow / ... / DataContext = vm 等保持不动）...

    // 末尾（ctor 最后一行之后）：
    Services.StartupTimer.Mark("Win.Ctor.Exit");
    Opened += OnStartupOpened; // 首帧打点 + flush，一次性
}
```

- [ ] **Step 2: 首帧打点与 flush**

```csharp
/// <summary>窗口首次 Opened：打首帧近似点并 flush 启动 trace（一次性，与启动测量无关的后续 Opened 不受影响）。</summary>
private void OnStartupOpened(object? sender, EventArgs e)
{
    Opened -= OnStartupOpened;
    Services.StartupTimer.Mark("Win.Visible");
    // Avalonia 无 WPF ContentRendered：用 Render 优先级回调近似首帧上屏
    Dispatcher.UIThread.Post(() =>
    {
        Services.StartupTimer.Mark("Win.FirstFrame");
        Services.StartupTimer.Flush("first-frame");
    }, Avalonia.Threading.DispatcherPriority.Render);
}
```

- [ ] **Step 3: PreviewPanel ctor 专项打点**

`PreviewPanel.axaml.cs:26`：

```csharp
public PreviewPanel()
{
    Services.StartupTimer.Mark("Preview.Panel.Ctor.Enter");
    InitializeComponent();
    Services.StartupTimer.Mark("Preview.Panel.Ctor.Exit");
    // ... 现有 FontPreviewScrollViewer 订阅等保持不动 ...
}
```

- [ ] **Step 4: PreviewViewModel ctor 专项打点**

`PreviewViewModel.cs:159`：

```csharp
public PreviewViewModel()
{
    Services.StartupTimer.Mark("Preview.VM.Ctor.Enter");
    MetadataSettingsManager.SettingsChanged += OnMetadataSettingsChanged;
    LocalizationManager.CultureChanged += OnCultureChanged;
    UpdateLocalizedStrings();
    var savedEncodingKey = AppSettings.Load().TextEncodingPreference;
    SelectedEncoding = EncodingOptions.FirstOrDefault(o => o.Key == savedEncodingKey)
                       ?? EncodingOptions.FirstOrDefault(o => o.Key == "auto");
    Services.StartupTimer.Mark("Preview.VM.Ctor.Exit");
}
```

（`Preview.VM.Ctor.*` 与 `Preview.Panel.Ctor.*` 成对出现，报告侧取 Exit−Enter 作专项 delta；`FormatTrace` 的 `preview-eager total` 汇总按 `Preview.` 前缀累加 DeltaMs 即覆盖两段 Exit 的增量，可接受——Exit mark 的 Delta 即构造段耗时。）

- [ ] **Step 5: 构建 + 全量测试**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 error。
Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`
Expected: 全部 PASS（打点为纯观测，不应破坏任何测试）。

- [ ] **Step 6: 手动冒烟（Debug）**

Run: `dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 窗口正常打开；`%LOCALAPPDATA%\MantisZip\startup-trace.log` 新增一条含 `Main.Entry` → `Win.FirstFrame` 全链路的样本。

- [ ] **Step 7: Commit**

```powershell
git add src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml.cs src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs
git commit -m "feat(avalonia): 启动打点接入 MainWindow/PreviewPanel/PreviewViewModel 构造与首帧"
```

---

### Task 4: Release 基线测量与报告（阶段一交付物）

**Files:**
- Create: `tools/measure-startup.ps1`（可选聚合脚本）
- 产出：测量报告（写入本计划文件末尾「阶段一测量结果」章节，或单独 `docs/` 片段——按用户偏好）

- [ ] **Step 1: 写聚合脚本**

```powershell
# tools/measure-startup.ps1
# 重复启动 Release 版 N 次，聚合 startup-trace.log 中位数（粗粒度：取 TOTAL 行）。
# 用法: .\tools\measure-startup.ps1 -ExePath ..\src\MantisZip.UI.Avalonia\bin\Release\net10.0\MantisZip.UI.Avalonia.exe -Runs 5
param(
    [Parameter(Mandatory)][string]$ExePath,
    [int]$Runs = 5,
    [int]$WarmupRuns = 1
)
$trace = Join-Path $env:LOCALAPPDATA "MantisZip\startup-trace.log"
if (-not (Test-Path $ExePath)) { throw "exe not found: $ExePath" }

1..$WarmupRuns | ForEach-Object { Start-Process $ExePath -PassThru | Wait-Process; Start-Sleep 1 }
$totals = @()
for ($i = 1; $i -le $Runs; $i++) {
    $before = if (Test-Path $trace) { (Get-Content $trace -Raw) } else { "" }
    Start-Process $ExePath -PassThru | Wait-Process
    Start-Sleep 1
    $after = Get-Content $trace -Raw
    $new = $after.Substring($before.Length)
    if ($new -match "TOTAL to last mark = ([\d\.]+)ms") {
        $totals += [double]$Matches[1]
        "run $i : $($Matches[1]) ms"
    }
}
if ($totals.Count -eq 0) { throw "no samples parsed" }
$sorted = $totals | Sort-Object
$median = $sorted[[int][math]::Floor($sorted.Count / 2)]
"`nmedian TOTAL = $median ms  (samples: $($totals -join ', '))"
```

- [ ] **Step 2: Release 构建 + 冷/热采样**

```powershell
dotnet build -c Release src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
# 热样本 ×5
.\tools\measure-startup.ps1 -ExePath src\MantisZip.UI.Avalonia\bin\Release\net10.0\MantisZip.UI.Avalonia.exe -Runs 5 -WarmupRuns 1
```

- [ ] **Step 3: 按 trace 逐段分析并对照决策规则**

打开 `%LOCALAPPDATA%\MantisZip\startup-trace.log` 最新样本，产出报告：
1. 冷样本中位数的完整分布表（pre-Main / Avalonia.InitDone / Init.* / Win.* 各段）
2. Top 3 增量阶段
3. `preview-eager total` 与占比 → **对照 1.7 门控给出「阶段二做/不做」结论**
4. 若 Stopwatch 加总 < 实际总时长 70%（存在未解释黑盒）→ 可选 `dotnet-trace collect` 加餐定位 JIT/程序集加载

- [ ] **Step 4: 把结论写进计划文件末尾「阶段一测量结果」章节，Commit**

```powershell
git add tools/measure-startup.ps1 .omo/plans/未开始/startup-preview-defer.md
git commit -m "feat(avalonia): 启动测量聚合脚本 + 阶段一基线数据"
```

**→ 检查点：门控决策。** `preview-eager` 达标 → 继续阶段二；不达标 → 停止本计划，报告 Top 热点并按 3.2 另行立项。向用户汇报数据并确认后再继续。

---

## 阶段二：预览 UI 延迟实例化（数据门控，仅当 Task 4 门控通过）

### Task 5: 占位 host + EnsurePreviewPanel（变体 A）

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml:1270-1274`
- Modify: `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs`（新增方法 + ctor 接线）

- [ ] **Step 1: XAML 换占位 host**

```xml
<!-- 预览占位 host：真正的 PreviewPanel 由 EnsurePreviewPanel 在首帧后延迟创建并填充（启动提速）。
     IsVisible 绑定 MainWindowViewModel.IsPreviewVisible（原面板级绑定经 DataContext=Preview 解析到
     PreviewViewModel.IsPreviewVisible，语义不同——原面板内部 Show* 置 true 逻辑保持在面板内部不变；
     此处 host 显隐跟菜单勾选态）。 -->
<ContentControl x:Name="PreviewPanelHost"
                Grid.Column="4"
                IsVisible="{Binding IsPreviewVisible}">
  <!-- PreviewPanel 由 code-behind 填充 -->
</ContentControl>
```

- [ ] **Step 2: code-behind 新增 EnsurePreviewPanel（异常安全 + 幂等）**

```csharp
private bool _previewPanelCreated;

/// <summary>
/// 延迟创建预览面板控件树（启动时 XAML 仅保留占位 host）。
/// 幂等；构造异常时清理半成品 Content 且 flag 不置位，允许兜底路径重试。
/// </summary>
private void EnsurePreviewPanel()
{
    if (_previewPanelCreated) return;
    _previewPanelCreated = true; // 先置位防重入（构造中再触发直接返回）
    try
    {
        if (DataContext is not MainWindowViewModel vm) { _previewPanelCreated = false; return; }
        var panel = new PreviewPanel { DataContext = vm.Preview };
        PreviewPanelHost.Content = panel;
        App.DebugLog("EnsurePreviewPanel: preview panel created (lazy)");
    }
    catch (Exception ex)
    {
        _previewPanelCreated = false;
        PreviewPanelHost.Content = null; // 清理半成品
        App.DebugLog($"EnsurePreviewPanel failed: {ex.Message}");
    }
}
```

- [ ] **Step 3: ctor 接线定时器与兜底**

ctor 末尾（`Opened += OnStartupOpened` 旁）：

```csharp
// 预览面板延迟创建：首帧后 0.5s 定时预取（把控件树构造挪出启动与打开压缩包关键路径）；
// 0.5s 内即发生预览请求时由 vm.PreviewPanelNeeded 兜底同步创建。
Opened += (_, _) => DispatcherTimer.RunOnce(
    () => Dispatcher.UIThread.Post(EnsurePreviewPanel, Avalonia.Threading.DispatcherPriority.Background),
    TimeSpan.FromMilliseconds(500));
vm.PreviewPanelNeeded += EnsurePreviewPanel;
```

补 `using Avalonia.Threading;`（如未有）。

- [ ] **Step 4: 构建 + 冒烟**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 error。
Run: `dotnet run --project ...` → 启动 0.5s 后日志出现 `EnsurePreviewPanel: preview panel created (lazy)`；预览面板正常可用。

- [ ] **Step 5: Commit**

```powershell
git add src/MantisZip.UI.Avalonia/Views/MainWindow.axaml src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat(avalonia): 预览面板控件树延迟实例化（占位host+0.5s定时+幂等）"
```

---

### Task 6: PreviewPanelNeeded 触发链（兜底）

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`（事件声明 + 预览链入口触发）

- [ ] **Step 1: 声明事件**

`MainWindowViewModel` 类内（`Preview` 属性附近，:327 旁）：

```csharp
/// <summary>预览面板控件树需要就绪（首次预览请求兜底触发；View 侧订阅 EnsurePreviewPanel）。</summary>
public event Action? PreviewPanelNeeded;
```

- [ ] **Step 2: 预览链入口触发**

在预览触发链起点（:1419 `Preview.StopGifTimer();` / `Preview.ShowLoading(...)` 之前）：

```csharp
PreviewPanelNeeded?.Invoke(); // 兜底：面板未建好时同步创建（幂等）
Preview.StopGifTimer();
Preview.ShowLoading(entry.NameDisplay ?? entry.Name);
```

- [ ] **Step 3: 构建 + 全量测试**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 error。
Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全部 PASS。

- [ ] **Step 4: Commit**

```powershell
git add src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs
git commit -m "feat(avalonia): PreviewPanelNeeded 事件作为预览面板延迟创建兜底"
```

---

### Task 7: 功能回归 + A/B trace 对比（阶段二验收）

- [ ] **Step 1: 回归清单（手动，按设计 2.7）**

1. 冷启动不打开压缩包 → trace 无 `Preview.Panel.Ctor.*` 段
2. 打开含各类文件的压缩包（文本/图/PDF/Office/字体/GIF/加密包密码链）→ 预览全部正常，首次选中无可感知延迟
3. 预览面板 4 位置切换、分隔条拖拽、布局保存/恢复、菜单显隐切换
4. 设置窗口改预览位置/显隐/紧凑度/主题
5. 拖拽解压、拖入添加等既有交互不受影响

- [ ] **Step 2: A/B 对比**

同一机器分别跑阶段一基线与阶段二后的 `measure-startup.ps1`（×5 中位数），对比 `Win.Xaml` 段与 TOTAL：预期 `Win.Xaml` 下降 ≈ 基线 `Preview.Panel.Ctor` 测得值。把对比表写进计划文件「阶段二验证结果」章节。

- [ ] **Step 3: Commit（含计划文件结果更新）**

```powershell
git add .omo/plans/未开始/startup-preview-defer.md
git commit -m "docs: 启动优化计划回填阶段一/二测量与验证结果"
```

---

## 附加任务（门控：基线 `Preview.VM.Ctor` ≥ 100ms 时才做）

### Task A1: PreviewViewModel ctor 瘦身

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs:159-167`

- [ ] **Step 1: 拆 EnsureInitialized**

```csharp
private bool _initialized;

public PreviewViewModel()
{
    Services.StartupTimer.Mark("Preview.VM.Ctor.Enter");
    // 瘦身：ctor 仅做零成本字段初始化；重活移入 EnsureInitialized（各 Show*/Clear 入口调用）
    Services.StartupTimer.Mark("Preview.VM.Ctor.Exit");
}

/// <summary>首次真正使用预览时执行订阅/设置读取/本地化填充（幂等）。</summary>
private void EnsureInitialized()
{
    if (_initialized) return;
    _initialized = true;
    MetadataSettingsManager.SettingsChanged += OnMetadataSettingsChanged;
    LocalizationManager.CultureChanged += OnCultureChanged;
    UpdateLocalizedStrings();
    var savedEncodingKey = AppSettings.Load().TextEncodingPreference;
    SelectedEncoding = EncodingOptions.FirstOrDefault(o => o.Key == savedEncodingKey)
                       ?? EncodingOptions.FirstOrDefault(o => o.Key == "auto");
}
```

- [ ] **Step 2: 各入口调用**

所有 `Show*` 方法与 `Clear()` 首行加 `EnsureInitialized();`。注意 `PreviewViewModelTests` 直接测 VM——确认测试中触碰的属性/方法路径已含 EnsureInitialized 或测试补调用。

- [ ] **Step 3: 测试 + 冒烟 + Commit**

Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全 PASS。

```powershell
git add src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs
git commit -m "perf(avalonia): PreviewViewModel 构造瘦身，重活延迟到 EnsureInitialized"
```

---

## 明确不做（防范围蔓延，见设计 3.2）

1. 不做格式模块插件化 / AssemblyLoadContext 分包（CLR 已按需加载，收益≈0）
2. 不做 `App.axaml.cs` / `MainWindow.axaml.cs` partial 文件拆分（其他分支开发中，保护合并面）
3. 不做 ReadyToRun / NativeAOT / 发布裁剪（若 dotnet-trace 指向 JIT 大头 → 单独立项）
4. 不做 splash 本体（`startup-native-splash.md` 负责，串行在本计划后）
5. 不做无关重构；不改版本号

## 与 splash 计划的衔接契约

- `StartupTimer` 保留为共享设施：splash 可用 `MarkEvent` 扩展（如 splash 显示/关闭时点）或直接消费 trace
- 阶段一报告的 `pre-Main + Avalonia.InitDone` 段 → splash 计划复核「普通启动约 1s，非慢路径不显示」假设
- 阶段二完成后启动总时长变化 → splash 最短显示时长参数以新数据为准
- 代码重叠仅 `Program.cs`/`App.axaml.cs` 少量行，串行实施消化

## 工时预估

| 任务 | 工时 |
|---|---|
| Task 1-3 打点 | 2-3h |
| Task 4 测量报告 | 1h |
| Task 5-6 延迟实例化（若门控通过） | 2-3h |
| Task 7 回归验证 | 1h |
| Task A1（按需） | 1h |
| **合计** | **7-9h** |
