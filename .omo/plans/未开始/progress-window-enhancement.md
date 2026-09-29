# 进度窗口增强改造（progress-window-enhancement）— 修订版 v2

> **Agentic Execution Note** — 本计划为 **WPF→Avalonia 全量修订版**（v2）。v1（2026-09-15，基于已删除的 WPF 版本）经代码级审查后发现全部文件路径、API、线程模型均为 WPF 语境，不可直接执行。v2 已并入全部审查必改项 + 密码徽标 + 密码弹窗兜底两项新功能决策，所有代码引用均经当前仓库（Avalonia 版）grep/读取核实。
>
> 执行方式：按 Wave 顺序执行；波次内标注 `∥` 的任务可并行。每任务完成后必须运行验证命令（Rule 12），全部通过才能标记完成。

---

## TL;DR

### Quick Summary
重构 `ProgressWindow`：路径/文件名分离多行显示 + TopDisplayMode/DensityMode 双模式切换 + 实时统计栏（已处理/跳过/出错/已覆盖/速度）+ 已用/剩余时间（ETA 批次切换守卫）+ ZIP 并行批次详细行 + 批处理密码徽标（逐包点亮、行内 Flyout 查看/复制）+ 解压密码弹窗兜底（输错循环重弹、取消标记行继续批处理）。统计埋点下沉到 Core 引擎解压冲突解析点（10 处 `ResolvePathAsync`），数据通道唯一为 `IProgress<ArchiveProgress>`。

### Deliverables
- `Core/Utils/ProgressDisplayCalculator.cs`（新建，纯函数 + `ProgressSpeedTracker`，含单测）
- `ArchiveProgress`/`ExtractResult` 可选统计字段（BatchIndex/BatchCount/SkippedFiles/FailedFiles/OverwrittenFiles 等）
- `ProgressBatchItem` 统计字段 + `BatchPasswordState` 密码态字段
- 三引擎 10 处冲突埋点 + ZIP 并行批次索引上报 + 硬编码「正在压缩: 」前缀清除
- `ProgressViewModel` 模式/密度/统计/时间/ETA 属性 + 集中通知 + LocalizedStrings 新 key
- `ProgressWindow.axaml` 布局重构（三模式上方、统计栏、时间行、中文注释）
- 批处理行密码徽标（🔄匹配中/🔑●●●● + Flyout）+ 死横幅/死方法清理
- `PasswordRetryLoop` 共享密码重试（4 个叶子入口接线，取消→行标记）

### Effort
**12–15h**（v1 估 3-4h 已作废——WPF 路径全废 + 功能范围扩大 2 项）

### Parallel Execution
```
Wave 1 (Core) ── T1 ──────── T4        (T1→T4 严格依赖)
                 T2 ∥ T3              (与 T1/T4 完全独立)
Wave 2 (UI)  ── T5 ── T6 ── T7        (模型→VM→视图，顺序)
Wave 3 (功能) ─ T8 ── T9               (同文件相邻区域，串行；见各任务说明)
Final         ─ F1 → F2 → F3 → F4
```

### Critical Path
**T1 → T4 → T6 → T7 → T8 → T9**（约 11h）；T2/T3/T5 可提前并入。

---

## Context

### Original Request
在 v6 交互原型（`docs/prototypes/progress-window-enhancement.html`，已 Playwright 验证 13 断言 ALL PASS）基础上，把进度窗口从「单文件名+单进度条」升级为多行信息、双模式切换、实时统计、时间/ETA、批处理密码可视化与错误兜底的完整进度中心。

### Review Findings（v1 审查必改项，v2 已全部并入）
1. **WPF→Avalonia 路径全废**：`MantisZip.UI/**` → `src/MantisZip.UI.Avalonia/`；`ProgressWindow.xaml` → `Dialogs/ProgressWindow.axaml(.cs)`；`AppPartals/App.Extract.cs` → `App.axaml.cs` + `Services/ExtractFlow.cs`；`Visibility.Visible` → `IsVisible`；`Theme_TextSecondary` → `Theme*Brush` 结尾。
2. **MVVM**：`_isBatchMode/_currentBatchIndex/_lastProgressUpdate/ProgressThrottle` 已在 `ProgressViewModel.cs:24-27`；`ProgressWindow.SetProgress`(:126) 仅转发 `_vm.SetProgress`；状态写入任务下沉 VM。
3. **多线程**：`ZipEngine.ExtractAsyncParallel`(:391) = `Parallel.ForEachAsync` 分批 + 每批独占 archive 实例（AGENTS.md 并行解压架构）；v1 的 `Parallel.ForEach` + `ManagedThreadId` + `progressReporter` 方案整体作废。
4. **数据通道唯一**：`IProgress<ArchiveProgress>`；`ArchiveProgress` 是 class（`ArchiveEngine.cs:294-305`），可加可选字段；**禁止改 `IArchiveEngine` 签名**。
5. **统计埋点**：v1 的 `ConflictActionCallback` 不存在于代码；正确位置是 `FileConflictHelper.ResolvePathAsync`（`FileConflictHelper.cs:67`）的引擎调用点——grep 实测 **10 处全为解压路径**：ZipEngine :307/:502/:712/:912、TarGzEngine :89/:154/:617/:707、SevenZipEngine :363/:749。杀掉「进程 8 线程」假统计。
6. **矛盾修复**：Task 7b `Brush?` vs Task 8 `StatusBrushName` → 单一机制（资源键字符串 + 既有 `BrushResourceConverter`）；行号全部刷新为当前 grep 锚点；空 Acceptance Criteria 补实。
7. **硬编码前缀**：引擎 Compress 路径 `CurrentFile = "正在压缩: " + ...` 硬中文前缀破坏 `SplitFilePath` → 前缀移出 Core，文案走 XAML/`LocalizationManager.T`。
8. **ETA 跨批守卫**：byte 基线在批次切换时重置，overall = `(completedArchives + pct/100) / N`。

### Interview Decisions（用户已拍板，8 项）

| # | 决策 |
|---|------|
| D1 | **密码徽标 · 点亮时机**：按数据到达逐项点亮。路径 A（预匹配 `_matchedPasswords`：`ExtractSettingsViewModel:149/152`，设置点 :428/:508，经 App.axaml.cs L1184 参数 + L1224-1227 `TryGetValue` 开始前全亮）；路径 B（循环内 `ResolveCliPassword` L774 轮到才亮） |
| D2 | **密码徽标 · 展示**：行内 `🔑+●●●●` 按钮 + Flyout（掩码尊重 `PasswordRevealByDefault`、复制恒明文、含规则/描述；Flyout 内容 code-behind 按 x:Name 填充避免 DataContext 继承问题） |
| D3 | **删死横幅**：`ProgressWindow.axaml` Row1 `PasswordSection`(:97) + PwdMatchText(:122)/PwdRevealBtn(:130)/PwdCopyBtn(:146) + 死方法 `ShowPasswordAttempt`(VM:394)/`ShowPasswordMatched`(:409)/`HidePasswordSection`(:448) + code-behind 包装(:286/:304/:322) + 关联 password props(VM:98-119)。**删前逐符号 grep 验证零调用** |
| D4 | 密码徽标只显示 🔄匹配中 状态，**不显示「尝试规则 N/M」** |
| D5 | **弹窗兜底 · 错密码循环**：对齐 Phase B（`MainWindowViewModel:966-1027`）——先 `QuickVerifyPasswordEx`(`PasswordService.cs:217`)，Wrong → `Status_WrongPassword` + 重弹循环，直到正确或取消 |
| D6 | **覆盖全部解压入口**：共享层实现（`PasswordRetryLoop`），4 个叶子接线点全覆盖 |
| D7 | **取消语义**：当前行标 ✗ `Status_PasswordCancelled`（「已取消 - 需要密码」，zh-CN:943，三语已有），**批处理继续** |
| D8 | 不做「密码同时用于后续压缩包」横幅功能 |

### Research Findings（代码事实）
- **引擎密码抛点全在并行派发之前**（弹窗时 0 工作线程）：ZipEngine :270-273（sequential）/:411-413（parallel 派发前）/:819（ExtractEntriesAsync 入口）、SevenZipEngine :333；TarGz 无加密。`Parallel.ForEachAsync` 等 in-flight 收尾才传播异常 → 引擎完全 unwind 后才可能弹窗，无并行中弹窗风险。
- **批处理顺序 for+await**（App.axaml.cs L1205-1253）不跨包并行。
- `PasswordDialog`（`Views/PasswordDialog.axaml.cs`）OK/Cancel 即 `Close(true/false)`（:159/:163/:169），**不支持弹窗内保持** → 采用 Phase B 外部重弹循环。
- `BatchStatusConverters.cs` 已有 Text/Icon/Background 三转换器（Background 为硬编码色，v2 新增主题键承接其颜色值）。
- `BrushResourceConverter`（主题键→Brush）已存在于 `Converters/`，直接复用。
- `ProgressViewModel.LocalizedStrings` 在 ctor :36-50 构建（**ProgressWindow 绑定 key 的唯一登记点**，非 `MainWindowViewModel.UpdateLocalizedStrings:232`——那只管主窗口）。
- `ArchiveProgress` 无任何 Batch/统计字段；`ExtractResult`(:310) 仅有 `SucceededEntries/FailedEntries/HasFailures`。
- 现有本地化 key：`Status_WrongPassword`(zh:962)、`Status_PasswordCancelled`(zh:943)、`Progress_FileCompressing/Extracting`(:593-596)、`Progress_FileCount` —— 三语齐全，可直接复用。
- 依赖链（docs/PLAN.md:68）：`nuget → compression-perf/estimator → progress-window-enhancement → progress-bar-segments`。

---

## Goal

修复并完成进度窗口增强：多行路径/文件名显示、实时解压统计（真实埋点，非假数据）、双模式切换、密码可视化与错误兜底，全部基于 Avalonia MVVM。

## Architecture

- **Core 层**（框架无关）：字段扩展（T1）→ 纯函数计算（T2）→ 行模型扩展（T3）→ 引擎埋点（T4）。统计唯一数据通道 `ArchiveProgress`（class，可选字段向后兼容）。
- **UI 层**：行模型 + key（T5）→ ViewModel 模式/统计/ETA（T6）→ 视图布局（T7）→ 密码徽标（T8）→ 密码弹窗兜底（T9）。
- **弹窗兜底放 UI 层**：`PasswordRetryLoop`（Services）在引擎完全 unwind 后、UI 线程弹窗；**禁止进 Core**。
- **文案分层**：Core 只产数值（`TimeSpan`/数字），中文格式化全部在 VM 经 `LocalizationManager.T`。

## Tech Stack

- .NET 10 / Avalonia 11 + CommunityToolkit.Mvvm 手动属性模式（`ProgressViewModel` 现有风格）
- SharpCompress ZipEngine / SharpSevenZip SevenZipEngine / SharpCompress TarGzEngine
- xUnit（`tests/MantisZip.Tests`）

---

## File Structure

```
src/
├── MantisZip.Core/
│   ├── Abstractions/ArchiveEngine.cs            # [M] T1: ArchiveProgress + ExtractResult 字段
│   ├── Utils/
│   │   ├── ProgressDisplayCalculator.cs         # [NEW] T2: 纯函数 + ProgressSpeedTracker
│   │   └── FileConflictHelper.cs                # (ref only, :67 ResolvePathAsync)
│   ├── Models/ProgressBatchItem.cs              # [M] T3: 统计字段 + 密码态字段 + StatusBrushName
│   └── Engines/
│       ├── ZipEngine.cs                         # [M] T4: 4 处埋点(:307/:502/:712/:912) + 批次索引上报 + 去前缀
│       ├── TarGzEngine.cs                       # [M] T4: 4 处埋点(:89/:154/:617/:707) + 去前缀
│       └── SevenZipEngine.cs                    # [M] T4: 2 处埋点(:363/:749) + 去前缀
├── MantisZip.UI.Avalonia/
│   ├── Models/
│   │   ├── ParallelBatchProgressItem.cs         # [NEW] T5: 并行批次详细行模型
│   │   └── ProgressDisplayMode.cs               # [NEW] T5: TopDisplayMode + DensityMode 枚举
│   ├── Converters/BrushResourceConverter.cs     # (reuse, T5)
│   ├── Converters/BatchStatusConverters.cs      # (ref: 状态→文案/图标)
│   ├── ViewModels/ProgressViewModel.cs          # [M] T6+T8: 模式/统计/ETA + LocalizedStrings + 删死方法(T8)
│   ├── Dialogs/
│   │   ├── ProgressWindow.axaml                 # [M] T7+T8: 布局重构 + 徽标/删横幅
│   │   └── ProgressWindow.axaml.cs              # [M] T7+T8: 计时器/Flyout 填充 + 删死包装(T8)
│   ├── Services/
│   │   ├── PasswordRetryLoop.cs                 # [NEW] T9: 共享密码重试循环
│   │   ├── ExtractService.cs                    # [M] T9: 叶子接线 1 (:28)
│   │   ├── ExtractFlow.cs                       # [M] T9: 叶子接线 2 (过滤分支 :153)
│   │   └── SelectedItemsExtractService.cs       # [M] T9: 叶子接线 3
│   ├── App.axaml.cs                             # [M] T8: 徽标两路径接线; T9: 叶子接线 4 (L1379 直连 engine)
│   ├── ViewModels/ExtractSettingsViewModel.cs   # (ref: _matchedPasswords :149/:152)
│   └── Localization/
│       ├── strings.zh-CN.json                   # [M] T5/T6/T8: 新 key (插入 { 后, UTF-8 无 BOM, CRLF, 2空格)
│       ├── strings.en.json                      # [M] 同上
│       └── strings.zh-TW.json                   # [M] 同上
tests/
└── MantisZip.Tests/
    └── ProgressDisplayCalculatorTests.cs        # [NEW] T2: 单测
docs/
└── PLAN.md                                      # [M] F4: 同步 :32 登记行 (Rule 1)
```

> DragDropService（`Services/DragDropService` :92 → `ExtractFlow.RunSelectedItemsExtractionAsync`）经叶子接线 3 自动覆盖，不单独改。

---

## 与 v6 原型的对应关系

| 原型（progress-window-enhancement.html v6） | 实现任务 |
|---|---|
| 上方三模式切换（全路径/仅目录/仅文件名） | T5 枚举 → T6 `TopDisplayMode` 属性 → T7 切换控件 + 绑定 |
| 下方紧凑度三档（Compact/Normal/Loose） | T5 枚举 → T6 `DensityMode` 属性 → T7 控件密度绑定（复用 Rule 5 资源键） |
| 统计栏：已处理/跳过/出错/已覆盖/速度 | T4 埋点计数 → T6 统计属性 + 可见性 → T7 统计栏（Rule 6） |
| 时间行：已用 / 剩余（ETA） | T2 `ProgressSpeedTracker`（批次切换重置基线）→ T6 时间属性 → T7 时间行 |
| 路径行 + 文件名行（SplitFilePath 分离） | T2 `SplitFilePath`（含前缀防御性剥离）→ T6 `DirName/FileName` → T7 两行显示 |
| 批处理行密码徽标（🔄 / 🔑●●●● + Flyout） | T3 密码态字段 → T8 徽标 + Flyout + 两路径接线 + 删死横幅 |
| 批处理行完成摘要（N 成功 / M 失败 / 跳过 / 覆盖） | T4 最终 `ArchiveProgress` 携带统计 → T6 拷贝入 BatchItem → T7 行摘要文本 |
| ZIP 并行批次详细行 | T4 `BatchIndex/BatchCount` 上报 → T5 行模型 → T6 集合 → T7 ItemsControl（无数据时 Rule 6 隐藏） |
| 密码错误弹窗重试 | T9 `PasswordRetryLoop`（Phase B 循环对齐） |

---

## Execution Strategy

### Dependency Matrix

| Task | 依赖 | 可与谁并行 | 预估 |
|------|------|-----------|------|
| T1 ArchiveProgress/ExtractResult 字段 | — | T2、T3 | 0.5h |
| T2 ProgressDisplayCalculator + 单测 | — | T1、T3 | 1.5h |
| T3 BatchItem 扩展 | — | T1、T2 | 1h |
| T4 引擎埋点 + 批次上报 + 去前缀 | T1 | T2/T3 收尾后 | 2h |
| T5 行模型 + 三语 key | T3 | — | 1h |
| T6 ProgressViewModel 重构 | T1、T5 | — | 2h |
| T7 ProgressWindow.axaml 布局 | T6 | — | 2h |
| T8 密码徽标 + 删死横幅 | T3、T7 | 不与 T9 并行（同文件相邻区域） | 1.5h |
| T9 PasswordRetryLoop + 4 接线 | T7 | T8 完成后 | 2h |
| F1–F4 验证波 | 全部 | — | 1h |

### 决策锁定（执行时不得偏离）

1. 统计埋点**唯一位置** = `ResolvePathAsync` 调用点（10 处），`resolvedPath == null` → skip、`existedBefore && resolvedPath == outputPath` → overwritten（`File.Exists` 预检须在 `ResolvePathAsync` **之前**）。
2. UI 统计/批摘要数据**唯一来源** = `ArchiveProgress`（含最终 100% 报告携带的终值）；`ExtractResult` 新字段仅作引擎单测断言用，**不改** `ExtractService`/`ExtractFlow` 返回类型。
3. 弹窗只在 UI 层 4 个叶子点，Core 不知道弹窗存在。
4. 死横幅/死方法删除**只在 T8**；T7 保留 `PasswordSection` 块仅调整行号并加注释标记。
5. `StatusBrushName` 单一机制（资源键字符串 + `BrushResourceConverter`），不引入 `Brush?` 直存。

---

## TODOs

- [ ] 1. ArchiveProgress / ExtractResult 统计字段扩展（Core）

**Files:**
- Modify: `src/MantisZip.Core/Abstractions/ArchiveEngine.cs`

**What to do:**

1. 在 `ArchiveProgress` class（:294，现有字段 CurrentFile/TotalBytes/ProcessedBytes/TotalFiles/ProcessedFiles/PercentComplete/FilePercentComplete）追加可选字段：

```csharp
/// <summary>ZIP 并行批次索引（0-based）。非并行/未分批时为 null。</summary>
public int? BatchIndex { get; set; }

/// <summary>ZIP 并行总批次数。非并行为 null。</summary>
public int? BatchCount { get; set; }

/// <summary>解压冲突统计（null = 本报告未携带该计数，UI 隐藏对应项）。压缩路径恒为 null。</summary>
public long? SkippedFiles { get; set; }
public long? FailedFiles { get; set; }
public long? OverwrittenFiles { get; set; }
```

2. 在 `ExtractResult`（:310，现有 `SucceededEntries/FailedEntries/HasFailures`）追加：

```csharp
/// <summary>因冲突策略跳过的条目数。</summary>
public int SkippedEntries { get; init; }

/// <summary>被覆盖写入的已有文件数。</summary>
public int OverwrittenEntries { get; init; }
```

**Must NOT do:**
- ❌ 禁止改 `IArchiveEngine` 接口签名
- ❌ 禁止给新字段加必填构造参数（保持 nullable/init，所有现有 `new ArchiveProgress{...}` 编译不受影响）
- ❌ 禁止引入 UI 类型或中文字符串

**References:**
- `ArchiveEngine.cs:294-305`（ArchiveProgress 定义）、`:310-318`（ExtractResult 定义）、`:162`（ArchiveOptions.ParallelExtractDegree）

**Verification:**
```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
```
- 构建通过，0 新增错误

**Acceptance Criteria:**
- 6 个新字段存在且默认值不破坏任何现有调用点
- `ArchiveProgress` 仍为 class、`ExtractResult` init-only 风格保持

**Recommended Agent Profile**:
- **Category**: `quick`（单文件追加可选字段）
- **Skills**: `[]`

**QA Scenarios**:
- Core 构建 + 全量既有测试无回归（新字段默认 null，既有 `new ArchiveProgress{...}` 与 `ExtractResult` 使用点全部编译通过）

**Parallelization:** 与 T2/T3 并行；T4 阻塞等待本任务。

---

- [ ] 2. ProgressDisplayCalculator（Core 计算层 + 单测）

**Files:**
- Create: `src/MantisZip.Core/Utils/ProgressDisplayCalculator.cs`
- Create: `tests/MantisZip.Tests/ProgressDisplayCalculatorTests.cs`

**What to do:**

1. 新建静态纯函数类 + 状态追踪器（**零中文文案**，格式化输出为数值/TimeSpan/文化中性字符串）：

```csharp
namespace MantisZip.Core.Utils;

/// <summary>进度窗口显示计算：路径分离、总进度、时长格式化。
/// 纯函数，不依赖任何 UI 框架；文案格式化由 UI 层经 LocalizationManager 完成。</summary>
public static class ProgressDisplayCalculator
{
    private static readonly string[] EnginePrefixes =
        ["正在压缩: ", "正在解压: ", "Compressing: ", "Extracting: "];

    /// <summary>剥离引擎 CurrentFile 可能携带的硬编码状态前缀（防御性；T4 已在源头清除）。</summary>
    public static string StripEnginePrefix(string? currentFile)
    {
        if (string.IsNullOrEmpty(currentFile)) return currentFile ?? string.Empty;
        foreach (var p in EnginePrefixes)
            if (currentFile.StartsWith(p, StringComparison.Ordinal))
                return currentFile[p.Length..];
        return currentFile;
    }

    /// <summary>路径/文件名分离（兼容 ZIP 条目 '/' 分隔符；已先剥前缀）。</summary>
    public static (string Dir, string FileName) SplitFilePath(string? currentFile)
    {
        var raw = StripEnginePrefix(currentFile);
        if (string.IsNullOrEmpty(raw)) return (string.Empty, string.Empty);
        var trimmed = raw.Replace('\\', '/').TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        return idx < 0 ? (string.Empty, trimmed)
                       : (trimmed[..idx], trimmed[(idx + 1)..]);
    }

    /// <summary>总进度 = (已完成档案数 + 当前档案百分比/100) / 总档案数。</summary>
    public static double ComputeOverallPercent(int completedArchives, double currentPercent, int totalArchives)
    {
        if (totalArchives <= 0) return Math.Clamp(currentPercent, 0, 100);
        var frac = Math.Clamp(currentPercent, 0, 100) / 100.0;
        return Math.Clamp((completedArchives + frac) / totalArchives * 100.0, 0, 100);
    }

    /// <summary>时长格式化（文化中性：1:02:03；>24h 启用天段）。负值归零。</summary>
    public static string FormatDuration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 24
            ? t.ToString(@"d\.hh\:mm\:ss")
            : t.ToString(@"h\:mm\:ss");
    }
}

/// <summary>速度/ETA 追踪器：EMA 平滑；批次切换必须重置字节基线（ETA 跨批守卫）。</summary>
public sealed class ProgressSpeedTracker
{
    private long _archiveStartBytes;   // 当前档案开始时的累计字节（跨批基线）
    private long _lastSampleBytes;
    private DateTime _lastSampleTime = DateTime.MinValue;
    private double _emaBytesPerSecond;

    /// <summary>批次/档案切换时调用：重置字节基线，防止上一档案速度污染 ETA。</summary>
    public void OnArchiveSwitch(long totalProcessedBytesBeforeArchive, DateTime now)
    {
        _archiveStartBytes = totalProcessedBytesBeforeArchive;
        _lastSampleBytes = totalProcessedBytesBeforeArchive;
        _lastSampleTime = now;
        _emaBytesPerSecond = 0;
    }

    /// <summary>采样当前累计字节，返回平滑速度（bytes/s；0 = 无效样本）。</summary>
    public double RecordSample(long totalProcessedBytes, DateTime now)
    {
        if (_lastSampleTime == DateTime.MinValue)
        {
            _lastSampleTime = now;
            _lastSampleBytes = totalProcessedBytes;
            return _emaBytesPerSecond;
        }
        var dt = (now - _lastSampleTime).TotalSeconds;
        if (dt < 0.1) return _emaBytesPerSecond;   // 100ms 节流
        var delta = totalProcessedBytes - _lastSampleBytes;
        if (delta < 0) { OnArchiveSwitch(totalProcessedBytes, now); return 0; }  // 计数回退(换档案)→重置
        var inst = delta / dt;
        _emaBytesPerSecond = _emaBytesPerSecond <= 0 ? inst : _emaBytesPerSecond * 0.7 + inst * 0.3;
        _lastSampleBytes = totalProcessedBytes;
        _lastSampleTime = now;
        return _emaBytesPerSecond;
    }

    /// <summary>ETA 秒数（null = 速度无效；0 = 已完成）。</summary>
    public double? ComputeEtaSeconds(long processedInArchive, long totalInArchive)
    {
        if (_emaBytesPerSecond <= 0 || totalInArchive <= 0) return null;
        var remain = totalInArchive - processedInArchive;
        return remain <= 0 ? 0 : remain / _emaBytesPerSecond;
    }

    /// <summary>当前档案内已处理字节（相对本档案开始，clamp ≥ 0）。</summary>
    public long ProcessedInArchive(long totalProcessedBytes) =>
        Math.Max(0, totalProcessedBytes - _archiveStartBytes);
}
```

2. 单测（xUnit，`tests/MantisZip.Tests`）至少覆盖：
   - `StripEnginePrefix`：真实前缀样本（`"正在压缩: a/b.zip"` → `"a/b.zip"`；无前缀原样；null → `""`）
   - `SplitFilePath`：`"dir/sub/file.txt"` → `("dir/sub","file.txt")`；`"file.txt"` → `("","file.txt")`；`"a\b\c.txt"` 反斜杠兼容；空/null 不抛
   - `ComputeOverallPercent`：`(0,50,4)=12.5`、`(3,100,4)=100`、`total=0` 回退、越界 clamp
   - `FormatDuration`：`5s → "0:00:05"`、`1h2m → "1:02:00"`、`25h → "1:01:00:00"`、负值 → `"0:00:00"`
   - `ProgressSpeedTracker`：采样平滑、`OnArchiveSwitch` 后速度归零重起、字节回退自动重置、ETA 边界（速度 0 → null，剩 0 → 0）

**Must NOT do:**
- ❌ Core 内禁止中文文案/`"已用 {0}"` 类格式串（返回 `TimeSpan`/数字，VM 用 `LocalizationManager.T`）
- ❌ 禁止依赖 Avalonia/WPF 类型
- ❌ 禁止 `DateTime.Now` 直调（用注入 `now` 参数保证可测）

**References:**
- v1 计划 `FormatTimeDisplay` 曾把中文放 Core——本任务显式修正该错误

**Verification:**
```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter ProgressDisplayCalculator
```
- 构建通过 + 新单测全绿

**Acceptance Criteria:**
- 上述单测全部通过
- 除注释外全文无中文字符

**Recommended Agent Profile**:
- **Category**: `quick`（新建单文件 + 单测文件，纯逻辑）
- **Skills**: `[]`

**QA Scenarios**:
- 新单测覆盖前缀剥离/路径分离/总进度/时长/ETA 守卫全部边界；Core 构建 0 错误

**Parallelization:** 与 T1/T3 完全并行。

---

- [ ] 3. ProgressBatchItem 扩展（统计字段 + 密码态 + StatusBrushName）

**Files:**
- Modify: `src/MantisZip.Core/Models/ProgressBatchItem.cs`

**What to do:**

1. 文件内新增密码态枚举：

```csharp
/// <summary>批处理行密码徽标状态。</summary>
public enum BatchPasswordState
{
    None,      // 无密码/未开始
    Matching,  // 🔄 匹配中
    Matched,   // 🔑 已匹配（显示 ●●●●，可 Flyout 查看）
}
```

2. 给 `BatchItem`（现有 `Name/FullPath/Status/ErrorMessage/Progress`，Status 与 Progress 已带 PropertyChanged 通知）追加字段 + **集中通知**（AGENTS.md 派生属性通知模式，禁止逐字段 `[NotifyPropertyChangedFor]`）：

```csharp
// ── 解压统计（来自 ArchiveProgress 最终报告；UI 按 HasXxx 可见性控制） ──
private long _totalFiles;
private long _processedFiles;
private long _skippedFiles;
private long _failedFiles;
private long _overwrittenFiles;
public long TotalFiles { get => _totalFiles; set => Set(ref _totalFiles, value); }
public long ProcessedFiles { get => _processedFiles; set { if (Set(ref _processedFiles, value)) NotifyBatchProperties(); } }
public long SkippedFiles { get => _skippedFiles; set { if (Set(ref _skippedFiles, value)) NotifyBatchProperties(); } }
public long FailedFiles { get => _failedFiles; set { if (Set(ref _failedFiles, value)) NotifyBatchProperties(); } }
public long OverwrittenFiles { get => _overwrittenFiles; set { if (Set(ref _overwrittenFiles, value)) NotifyBatchProperties(); } }

// ── 密码徽标 ──
private BatchPasswordState _passwordState;
private string? _matchedPassword;
private string? _passwordRule;
private string? _passwordDescription;
public BatchPasswordState PasswordState { get => _passwordState; set { if (Set(ref _passwordState, value)) NotifyBatchProperties(); } }
public string? MatchedPassword { get => _matchedPassword; set => Set(ref _matchedPassword, value); }
public string? PasswordRule { get => _passwordRule; set => Set(ref _passwordRule, value); }
public string? PasswordDescription { get => _passwordDescription; set => Set(ref _passwordDescription, value); }

// ── 派生显示属性（集中通知） ──
public bool HasSkipped => SkippedFiles > 0;
public bool HasFailures => FailedFiles > 0;
public bool HasOverwritten => OverwrittenFiles > 0;
public bool HasPasswordBadge => PasswordState != BatchPasswordState.None;
/// <summary>状态行摘要（完成时填充，如 "12 成功 · 1 跳过"——文案 key 由 UI 层拼装，此处存格式化结果）。</summary>
public string? SummaryText { get => _summaryText; set { if (Set(ref _summaryText, value)) NotifyBatchProperties(); } }
private string? _summaryText;

// ── 状态画刷资源键（单一机制：UI 用 BrushResourceConverter 物化；Task 5 注册主题键） ──
public string StatusBrushName => Status switch
{
    BatchStatus.Success => "ThemeStatusSuccessBrush",
    BatchStatus.Failed   => "ThemeStatusFailedBrush",
    BatchStatus.Cancelled => "ThemeStatusCancelledBrush",
    BatchStatus.Skipped  => "ThemeStatusSkippedBrush",
    _ => "ThemeBorderBrush",   // Pending/Running
};

private void NotifyBatchProperties()
{
    OnPropertyChanged(nameof(HasSkipped));
    OnPropertyChanged(nameof(HasFailures));
    OnPropertyChanged(nameof(HasOverwritten));
    OnPropertyChanged(nameof(HasPasswordBadge));
    OnPropertyChanged(nameof(StatusBrushName));
}
```

> `Set(...)` = 文件既有属性通知辅助方法（若无则沿用 Status/Progress 的现有写法逐一套用）；`BatchStatus` 枚举值以文件现状为准（grep `enum BatchStatus` 确认，缺失的成员用现有枚举替代并在实现时记录）。

**Must NOT do:**
- ❌ Core 不得引用任何 UI 类型（Brush/Color/Avalonia）
- ❌ 禁止逐字段 `[NotifyPropertyChangedFor]`——只允许 `NotifyBatchProperties()` 集中通知
- ❌ 不得改变现有 `Status`/`Progress`/`ErrorMessage` 的语义

**References:**
- `ProgressBatchItem.cs` 全文（现有 5 属性）；`Converters/BatchStatusConverters.cs`（状态→文案/图标映射，图标转换器继续服务行内状态图标）

**Verification:**
```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
```
- 构建通过

**Acceptance Criteria:**
- 新字段全部带通知，派生属性集中在 `NotifyBatchProperties()`
- `StatusBrushName` 仅返回资源键字符串（无 Brush 类型）

**Recommended Agent Profile**:
- **Category**: `quick`（单文件追加字段 + 集中通知）
- **Skills**: `[]`

**QA Scenarios**:
- Core 构建通过；`StatusBrushName` 仅返回资源键字符串（grep 无 `Brush` 类型引用）；派生属性只经 `NotifyBatchProperties()` 通知

**Parallelization:** 与 T1/T2 并行；T5 依赖本任务。

---

- [ ] 4. 三引擎解压冲突埋点 + ZIP 并行批次上报 + 去硬编码前缀（Core）

  **Files:**
  - Create: `src/MantisZip.Core/Utils/ConflictStatsCounter.cs`
  - Modify: `src/MantisZip.Core/Engines/ZipEngine.cs`（4 处 :307/:502/:712/:912 + 批次上报 + 去前缀）
  - Modify: `src/MantisZip.Core/Engines/TarGzEngine.cs`（4 处 :89/:154/:617/:707 + 去前缀）
  - Modify: `src/MantisZip.Core/Engines/SevenZipEngine.cs`（2 处 :363/:749 + 密码抛点 :333 附近不动 + 去前缀）

  **What to do:**

  1. 新建线程安全计数器：

  ```csharp
  namespace MantisZip.Core.Utils;

  /// <summary>解压冲突统计（Interlocked 线程安全；ZIP 并行批次内多线程共享同一实例）。</summary>
  public sealed class ConflictStatsCounter
  {
      private int _skipped, _overwritten, _failed;
      public void RecordSkipped() => Interlocked.Increment(ref _skipped);
      public void RecordOverwritten() => Interlocked.Increment(ref _overwritten);
      public void RecordFailed() => Interlocked.Increment(ref _failed);
      public (int Skipped, int Overwritten, int Failed) Snapshot =>
          (_skipped, _overwritten, _failed);
      public void Reset() { _skipped = 0; _overwritten = 0; _failed = 0; }
  }
  ```

  2. **10 处 `ResolvePathAsync` 调用点逐点埋点**（grep `ResolvePathAsync(` 复核行号——行号会漂移，以符号定位为准）。每处模式：

  ```csharp
  var existedBefore = File.Exists(outputPath);          // 预检必须在 ResolvePathAsync 之前
  var resolvedPath = await FileConflictHelper.ResolvePathAsync(outputPath, options, ct);
  if (resolvedPath == null)
  {
      conflictStats.RecordSkipped();                    // 策略=跳过
      // ... 既有 skip 分支行为保持不变 ...
      continue;
  }
  if (existedBefore && string.Equals(resolvedPath, outputPath, StringComparison.OrdinalIgnoreCase))
      conflictStats.RecordOverwritten();                // 覆盖已有文件
  // ... 既有逻辑不动 ...
  ```

  - 每个解压方法（`ExtractAsync`/`ExtractEntriesAsync`）入口创建一个 `ConflictStatsCounter` 实例，方法内所有调用点共享
  - 条目级 `catch` 中调用 `conflictStats.RecordFailed()`（仅在既有 per-entry catch 存在处补一行，不新增 try 结构）
  - **先确认每处调用点所在方法确为解压路径**（grep 实测 10 处全在 ExtractAsync/ExtractEntriesAsync 内；若行号漂移后发现压缩路径调用点则跳过并记录）
  - TarGz :154 这类无 entryModified 重载同样计数

  3. **上报**：方法内每次构造/更新 `ArchiveProgress` 时追加：

  ```csharp
  var (sk, ov, fl) = conflictStats.Snapshot;            // 锁外/无锁快照（计数器本身线程安全）
  progress?.Report(new ArchiveProgress
  {
      // ... 既有字段 ...
      SkippedFiles = sk,
      OverwrittenFiles = ov,
      FailedFiles = fl,
  });
  ```

  - **进度报告已在锁内的**（ZipEngine parallel `conflictGate`/`_progressGate` 区域）：锁内只拷贝计数快照与进度字段到局部变量，**`Report` 必须在释放锁之后调用**（AGENTS.md 性能陷阱：锁内 Report 8 线程争用曾 25x 劣化）
  - 节流维持既有 ~100ms 逻辑；**最终 100% 报告必须携带终值统计**
  - 压缩路径（CompressAsync）**不创建计数器、不设统计字段**（保持 null）

  4. **ZIP 并行批次索引**：`ExtractAsyncParallel`（:391）的 `Parallel.ForEachAsync`（:476）批次委托内，构造 `ArchiveProgress` 时设 `BatchIndex = 批次序号(0-based)`、`BatchCount = 批次数`；sequential/非并行路径不设（保持 null）。**按批次索引标识，禁止用 `ManagedThreadId`**。

  5. **最终 `ExtractResult`**：各 `ExtractAsync`/`ExtractEntriesAsync` 返回处，从计数器快照填充 `SkippedEntries`/`OverwrittenEntries`（强转 int），`FailedEntries` 保持既有逻辑（如已有失败计数则不重复统计）。

  6. **去硬编码前缀**：`grep -n "正在压缩" src/MantisZip.Core` 与 `"正在解压"`，将 `CurrentFile = "正在压缩: " + path` 类语句改为 `CurrentFile = path`（raw 路径）。UI 语境由既有 `Progress_FileCompressing/Extracting` key 在需要处表达（本计划不新增展示位）。`StripEnginePrefix`（T2）作防御性兜底。

  **Must NOT do:**
  - ❌ 禁止改 `IArchiveEngine` 签名 / `ArchiveOptions`
  - ❌ 禁止在锁内调用 `progress.Report`
  - ❌ 禁止改动冲突解决行为本身（只观察计数，不改 ResolvePathAsync 语义）
  - ❌ 禁止给压缩路径伪造统计（保持 null）
  - ❌ 禁止用 `ManagedThreadId`/线程 ID 做批次归属
  - ❌ Core 内禁止新增中文用户可见文案（去前缀是删除，不是新增）

  **Recommended Agent Profile:**
  - **Category**: `deep`（跨 3 引擎 10 处精细埋点 + 锁纪律 + 线程安全，易错）
  - **Skills**: `[]`

  **QA Scenarios:**
  - 加密 ZIP 并行解压 + 覆盖/跳过策略混合：统计栏数字与实际文件操作一致；无锁竞争性能回归（对照 AGENTS.md 0.3s 基线量级）
  - grep 确认 10 处调用点全部埋点、0 处 `正在压缩`/`正在解压` 残留在 Core

  **Verification:**
  ```powershell
  dotnet build src\MantisZip.Core\MantisZip.Core.csproj
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
  ```
  - [ ] 构建通过 + 既有测试全绿

  **Acceptance Criteria:**
  - [ ] 10 处调用点全部有 skip/overwritten 计数（grep `RecordSkipped` ≥10 处调用）
  - [ ] ZIP 并行报告携带 `BatchIndex/BatchCount`，其他路径为 null
  - [ ] 三引擎最终报告与 `ExtractResult` 统计一致
  - [ ] Core 无 `正在压缩`/`正在解压` 前缀残留

  **Parallelization:**
  - **Can Run In Parallel**: NO（与 T5-T7 波次串行更稳；文件独立可与 T5 并行但建议顺序）
  - **Blocked By**: Task 1
  - **Blocks**: Task 6（VM 消费统计字段）

---

- [ ] 5. 行模型 + 模式枚举 + 主题键 + 三语本地化 key（UI）

  **Files:**
  - Create: `src/MantisZip.UI.Avalonia/Models/ProgressDisplayMode.cs`
  - Create: `src/MantisZip.UI.Avalonia/Models/ParallelBatchProgressItem.cs`
  - Modify: `src/MantisZip.UI.Avalonia/Resources/ThemeLight.axaml` + `ThemeDark.axaml`（若缺状态主题键）
  - Modify: `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`

  **What to do:**

  1. 模式枚举：

  ```csharp
  namespace MantisZip.UI.Avalonia.Models;

  /// <summary>进度窗口上方信息显示模式（v6 原型三模式）。</summary>
  public enum TopDisplayMode { FullName, DirOnly, NameOnly }

  /// <summary>进度窗口信息密度（三档，与紧凑度模式独立）。</summary>
  public enum DensityMode { Compact, Normal, Loose }
  ```

  2. 并行批次详细行模型（仅 ZIP 并行时由 VM 填充；非并行集合为空 → XAML 按 Rule 6 隐藏）：

  ```csharp
  /// <summary>ZIP 并行解压批次详细行（ArchiveProgress.BatchIndex 驱动）。</summary>
  public sealed class ParallelBatchProgressItem : ObservableObject
  {
      // BatchIndex+1（1-based 显示序号）
      public int Index { get; init; }
      [ObservableProperty] private double _percent;        // 0-100
      [ObservableProperty] private string _statusBrushName = "ThemeBorderBrush"; // 资源键，BrushResourceConverter 物化
      [ObservableProperty] private string _detailText = ""; // 如 "12/40 文件"（VM 用 T() 拼好传入）
  }
  ```
  > 若文件用手工属性通知（与 ProgressViewModel 风格一致），改用手工 `Set` 模式，保持仓库风格统一——二选一，以既有 UI/Models 文件风格为准（grep `ObservableObject` in Models/ 确认）。

  3. **状态主题键**：grep `ThemeStatusSuccessBrush` 于 `ThemeLight.axaml`/`ThemeDark.axaml`；缺失则成对新增 4 个：
     - `ThemeStatusSuccessBrush`、`ThemeStatusFailedBrush`、`ThemeStatusCancelledBrush`、`ThemeStatusSkippedBrush`
     - 颜色值**承接 `BatchStatusConverters.cs` 既有硬编码色**（grep `FromRgb`/`#` 取值：Failed `#F43643` 系、Success `#6BD46B`/`#4CAF50` 系、Skipped `#00BCD4` 系——以转换器实际值为准；暗色档用同色即可）
     - Rule 4：两主题文件必须成对，键名以 `Brush` 结尾

  4. **三语 key**（Rule 13：先 grep 既有 key 复用，缺失才新增；插入文件头 `{` 之后，不排序，UTF-8 无 BOM + CRLF + 2 空格缩进）：

  | key | zh-CN | en | zh-TW |
  |---|---|---|---|
  | `Progress_Stats_Processed` | `已处理 {0}` | `Processed {0}` | `已處理 {0}` |
  | `Progress_Stats_Skipped` | `跳过 {0}` | `Skipped {0}` | `跳過 {0}` |
  | `Progress_Stats_Failed` | `出错 {0}` | `Failed {0}` | `出錯 {0}` |
  | `Progress_Stats_Overwritten` | `已覆盖 {0}` | `Overwritten {0}` | `已覆蓋 {0}` |
  | `Progress_Stats_Speed` | `{0}/s` | `{0}/s` | `{0}/s` |
  | `Progress_Time_Elapsed` | `已用 {0}` | `Elapsed {0}` | `已用 {0}` |
  | `Progress_Time_Remaining` | `剩余 {0}` | `Remaining {0}` | `剩餘 {0}` |
  | `Progress_CurrentFileLabel` | `当前文件` | `Current file` | `目前檔案` |
  | `Progress_Batch_ArchiveOf` | `{0} / {1}` | `{0} / {1}` | `{0} / {1}` |
  | `Progress_Batch_Pwd_Matching` | `匹配中` | `Matching` | `匹配中` |
  | `Progress_Batch_Pwd_MatchedTip` | `已匹配密码，点击查看` | `Password matched, click to view` | `已匹配密碼，點擊查看` |
  | `Progress_Batch_Pwd_Rule` | `规则 {0}` | `Rule {0}` | `規則 {0}` |
  | `Progress_Batch_Pwd_Desc` | `描述 {0}` | `Description {0}` | `描述 {0}` |

  - **新增前必 grep**：`Status_WrongPassword`/`Status_PasswordCancelled`/`Status_ArchiveCorrupted`/`Progress_FileCount`/`Progress_CopyTooltip`/`Progress_RevealTooltip` 已存在，**复用勿重复新增**
  - 行摘要不新增 key：VM 用 `Progress_Stats_*` 组 parts 拼 `SummaryText`
  - 完成后跑 `AboutWindowTests.AllThreeLanguages_HaveSameKeySet`（三语 key 集校验）

  **Must NOT do:**
  - ❌ 不在 `.cs`/`.axaml` 硬编码任何用户可见文案（Rule 13）
  - ❌ 不改既有 key 的值
  - ❌ 键文件保持 UTF-8 无 BOM + CRLF + 2 空格缩进

  **Recommended Agent Profile:**
  - **Category**: `quick`（枚举 + 模型小文件 + JSON key 插入）
  - **Skills**: `[]`

  **QA Scenarios:**
  - `dotnet test` 三语 key 集一致；grep 确认新 key 三文件同步；主题键两文件成对

  **Verification:**
  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter AllThreeLanguages
  ```
  - [ ] 构建通过 + key 集测试绿

  **Acceptance Criteria:**
  - [ ] 新 key 三语同步、插入位置正确、无 BOM
  - [ ] `StatusBrushName` 引用的主题键在亮/暗两主题均存在

  **Parallelization:**
  - **Blocked By**: Task 3
  - **Blocks**: Task 6（VM 引用枚举/行模型）、Task 7（XAML 绑定 key）

---

- [ ] 6. ProgressViewModel 重构：模式/密度/统计/时间/ETA/批次集合（UI）

  **Files:**
  - Modify: `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs`

  **What to do:**

  1. **新增字段**（与 :24-27 既有私有字段同区）：`_speedTracker`（`ProgressSpeedTracker`）、`_opStartUtc`（DateTime，ctor/InitBatchMode 重置）、`_topDisplayMode`、`_densityMode`、`_statsProcessed/_statsSkipped/_statsFailed/_statsOverwritten`（long）、`_hasConflictStats`（bool）、`_dirName`、`_fileName`、`_elapsedText`、`_remainingText`、`_speedText`、`_currentFileLabel`、`_batchArchiveIndexText`、`ObservableCollection<ParallelBatchProgressItem> _parallelBatchItems`。

  2. **模式/密度属性**（手工通知，与文件既有风格一致）：

  ```csharp
  public TopDisplayMode TopDisplayMode
  {
      get => _topDisplayMode;
      set { if (Set(ref _topDisplayMode, value)) NotifyDisplayProperties(); }
  }
  public DensityMode DensityMode
  {
      get => _densityMode;
      set { if (Set(ref _densityMode, value)) NotifyDisplayProperties(); }
  }
  // 派生可见性（集中通知，禁 [NotifyPropertyChangedFor] 逐个标注）
  public bool DirVisible => TopDisplayMode != TopDisplayMode.NameOnly && DirName.Length > 0;
  public bool NameVisible => TopDisplayMode != TopDisplayMode.DirOnly;
  public bool IsCompactDensity => DensityMode == DensityMode.Compact;
  // ...
  private void NotifyDisplayProperties()
  {
      OnPropertyChanged(nameof(DirVisible));
      OnPropertyChanged(nameof(NameVisible));
      OnPropertyChanged(nameof(IsCompactDensity));
      // 其余派生一并在此
  }
  ```

  3. **`SetProgress` 接入计算器**（现有 :211-256 逻辑内改造，保留 100ms 节流 `_lastProgressUpdate/ProgressThrottle`）：
  - `ProgressDisplayCalculator.SplitFilePath(p.CurrentFile)` → `DirName`/`FileName` 属性（set 时触发 `DirVisible/NameVisible` 重算经集中通知）
  - 批次模式总进度：`ComputeOverallPercent(_currentBatchIndex, p.PercentComplete, BatchItems.Count)`；非批次 = `p.PercentComplete`
  - 速度/ETA：`_speedTracker.RecordSample(p.ProcessedBytes, DateTime.UtcNow)` → `SpeedText = T("Progress_Stats_Speed", FormatBytes(speed))`；`ComputeEtaSeconds(ProcessedInArchive, TotalBytes)` → `RemainingText = T("Progress_Time_Remaining", FormatDuration(...))`（`FormatBytes` 用既有 KB/MB 换算辅助，grep 既有实现复用）
  - `_hasConflictStats`：任一报告 `p.SkippedFiles != null` 时置 true（压缩路径恒 null → 统计栏隐藏，Rule 6）
  - 统计值拷贝：`SkippedFiles/FailedFiles/OverwrittenFiles` 非 null 时更新 `_statsXxx` + 拼 `T("Progress_Stats_Xxx", value)`
  - 并行批次集合：`p.BatchIndex != null` → upsert `_parallelBatchItems[p.BatchIndex]`（Index/Percent/StatusBrushName/DetailText）；`p.BatchIndex == null` 时**不动集合**（避免非并行清空闪烁）；`SetCurrentBatchItem`（:311）切换档案时清空集合

  4. **`SetCurrentBatchItem` 增加 ETA 守卫**：调用 `_speedTracker.OnArchiveSwitch(当前累计字节, DateTime.UtcNow)` —— 档案切换重置字节基线与 EMA（防上一档案速度污染剩余时间）。同时把上一档案的最终统计写入 `BatchItem`：`TotalFiles/ProcessedFiles/SkippedFiles/FailedFiles/OverwrittenFiles` + `SummaryText`（parts 经 `T("Progress_Stats_*")` 拼接）。

  5. **时间刷新**：新增 `RefreshTimeDisplay()`（public，供 code-behind DispatcherTimer 每 1s 调用）：`ElapsedText = T("Progress_Time_Elapsed", FormatDuration(DateTime.UtcNow - _opStartUtc))`；`_opStartUtc` 在 InitBatchMode/SetProgress 首次调用时初始化。

  6. **`UpdateBatchItemStats(ArchiveProgress p)`**（新方法，SetProgress 批次分支内调用）：把统计字段写入 `BatchItems[_currentBatchIndex]`（行级实时统计）。

  7. **LocalizedStrings 登记**（ctor :36-50 数组追加 T5 全部新 key + `Progress_CurrentFileLabel`）——**漏登记 = XAML 绑定空白**（Rule 13 特别注意：此字典由显式数组构建，非全量加载 JSON）。

  8. **不动**：password props(:98-119) 与死方法(:394/:409/:448) —— T8 负责删除（决策锁定4）。

  **Must NOT do:**
  - ❌ 禁止逐字段 `[NotifyPropertyChangedFor]`（AGENTS.md 集中通知模式）
  - ❌ 禁止硬编码中文（一律 `LocalizationManager.T`）
  - ❌ 禁止在本任务删除任何既有成员（死代码清理归 T8）
  - ❌ 禁止把 ETA 守卫做成「档案切换不清基线」的近似实现

  **Recommended Agent Profile:**
  - **Category**: `unspecified-high`（单 VM 大改造，属性联动多）
  - **Skills**: `[]`

  **QA Scenarios:**
  - 非批次解压：统计栏出现（已处理/速度/时间），压缩时统计栏隐藏
  - 批次解压档案切换：ETA/速度归零重起，上一行写入 SummaryText
  - ZIP 并行解压：并行批次集合出现行；非 ZIP：集合恒空（XAML 隐藏）

  **Verification:**
  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  ```
  - [ ] 构建通过、`lsp_diagnostics` 无错误

  **Acceptance Criteria:**
  - [ ] 模式/密度/统计/时间/ETA 属性齐备且集中通知
  - [ ] LocalizedStrings 含 T5 全部新 key
  - [ ] ETA 守卫在 `SetCurrentBatchItem` 重置基线

  **Parallelization:**
  - **Blocked By**: Task 1、Task 4、Task 5
  - **Blocks**: Task 7（XAML 绑定本 VM 属性）

---

- [ ] 7. ProgressWindow.axaml 布局重构 + code-behind 计时器/模式接线（UI）

  **Files:**
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs`

  **What to do:**

  1. **目标行结构**（Grid 重排，`x:CompileBindings="False"` 保留；现有控件按新行号迁移，不删功能）：

  | 行 | 内容 | 绑定/说明 |
  |---|---|---|
  | 0 | **TopSection 模式切换**：TopDisplayMode 三选（全路径/仅目录/仅文件名）+ DensityMode 三选（紧凑/标准/宽松），两组 RadioButton | code-behind Click → `_vm.TopDisplayMode/_vm.DensityMode`（枚举绑定需转换器，薄接线放 code-behind 更简） |
  | 1 | **统计栏**：`已处理 / 跳过 / 出错 / 已覆盖 / 速度` 五个 TextBlock | 跳过/出错/已覆盖 `IsVisible="{Binding HasConflictStats}"`（Rule 6，压缩隐藏） |
  | 2 | 路径行 DirName | `IsVisible="{Binding DirVisible}"`，`ThemeTextSecondaryBrush` |
  | 3 | 文件名行 FileName | `IsVisible="{Binding NameVisible}"`，加粗 |
  | 4 | 当前文件标签 + 文件进度条 + 百分比（迁移既有 `FileCountText`(:222)/进度条控件） | 标签绑定 `LocalizedStrings[Progress_CurrentFileLabel]` |
  | 5 | 时间行：`已用 / 剩余` | `ElapsedText/RemainingText` |
  | 6 | 状态行（既有 :229 迁移） | 既有绑定不动 |
  | 7 | 错误摘要 ErrorSummaryBox（既有 :238 迁移） | 既有绑定不动 |
  | 8 | 批处理列表 BatchFileList（既有 :38 迁移）+ 并行批次行 ItemsControl | 批列表行高已有 `MinHeight=ControlHeightMd`（Rule 7 满足）；并行批次容器 `IsVisible` 绑 `HasParallelBatches`（VM 集合变更通知；若 T6 未加该派生属性，在此补充经 `NotifyDisplayProperties` 式集中通知）——**无数据即隐藏（Rule 6）** |
  | 9 | PasswordSection（**原样保留**，仅迁到本行；上方加注释 `<!-- 死横幅：任务 8 删除 -->`，决策锁定4） | 既有绑定不动 |
  | 10 | 按钮行（既有 :253 KeepOpen/Pause/Cancel 迁移） | 既有绑定不动 |

  2. **样式规则（逐条硬性）**：
  - Rule 4：所有颜色 `DynamicResource Theme*Brush` 结尾键；新增处若缺主题键 → `ThemeLight.axaml`/`ThemeDark.axaml` 成对补
  - Rule 5：间距 `SpacingXxxThk`（Margin/Padding）/`SpacingXxx`（Spacing）/`ControlHeightXxx`/`BorderRadius`——禁止硬编码数值
  - Rule 6：统计项/路径行/并行容器/密码徽标（T8）一律 `IsVisible` 切换，不 `IsEnabled`
  - Rule 14：所有新增分区/控件上方中文 `<!-- -->` 注释（模式切换区、统计栏、路径/文件名区、时间行、并行批次区、按钮区）

  3. **code-behind**：
  - `DispatcherTimer`（1000ms）：`OnOpened` 启动 → `_vm.RefreshTimeDisplay()`；`OnClosed` 停止（防泄漏）
  - 模式 RadioButton Click 处理器（6 个，各 1 行赋值 VM 属性）
  - 保留既有 `DispatchIfNeeded` 包装与转发结构；**不在此任务删除任何成员**

  **Must NOT do:**
  - ❌ 禁止 `Visibility.Visible`（WPF 残留）/`Visibility` 任何用法 → `IsVisible`
  - ❌ 禁止 `Theme_Text*` 下划线键名
  - ❌ 禁止固定 Height/硬编码间距（除有充分理由）
  - ❌ 禁止删除 PasswordSection 与死方法（T8 职责）
  - ❌ 禁止新增用户可见硬编码文案（Rule 13）

  **Recommended Agent Profile:**
  - **Category**: `visual-engineering`（布局/样式域；load_skills=["frontend"] 评估，Avalonia 专用规则以上文为准）
  - **Skills**: `[]`

  **QA Scenarios:**
  - 三模式切换：全路径→两行显示；仅目录→隐藏文件名行；仅文件名→隐藏路径行
  - 压缩任务：统计栏跳过/出错/已覆盖三项隐藏；解压任务显示
  - 紧凑/标准/宽松切换行高与间距变化（Rule 5 资源键生效）

  **Verification:**
  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  ```
  - [ ] 构建通过、`lsp_diagnostics` 无错误

  **Acceptance Criteria:**
  - [ ] 11 行结构就位，既有控件全部迁移无丢失
  - [ ] 规则 4/5/6/14 逐条自检通过
  - [ ] DispatcherTimer 启停正确

  **Parallelization:**
  - **Blocked By**: Task 6
  - **Blocks**: Task 8（行模板/Flyout 落点）、Task 9（状态行展示）

---

- [ ] 8. 批处理密码徽标 + Flyout + 两路径接线 + 删死横幅/死方法（UI + 接线）

  **Files:**
  - Modify: `src/MantisZip.Core/Models/ProgressBatchItem.cs`（补两个派生 bool）
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml`（行模板徽标 + Flyout；删 PasswordSection）
  - Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs`（Flyout 填充 + `SetBatchPasswordState` 包装；删死包装）
  - Modify: `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs`（`SetBatchPasswordState` 方法；删死方法 :394/:409/:448 与 password props :98-119）
  - Modify: `src/MantisZip.UI.Avalonia/App.axaml.cs`（Path A L1224-1227 + Path B L774 附近）

  **What to do:**

  1. **删除前 grep 守卫（第一步，必须先做）**：
     ```
     grep -n "ShowPasswordAttempt\|ShowPasswordMatched\|HidePasswordSection\|IsPasswordSectionVisible\|PasswordMatchText\|PasswordRuleText\|PasswordStatusText" src/
     ```
     - 预期：仅命中 VM 定义(:98-119/:394/:409/:448)、code-behind 包装(:286/:304/:322)、XAML 绑定——**零外部业务调用**
     - 若出现外部调用者 → **停止本任务，报告用户**，不得盲删

  2. **删除**（grep 确认后）：
     - XAML：`PasswordSection` Border 整块（Row 1 区，:97 起，含 PwdMatchText:122/PwdRevealBtn:130/PwdCopyBtn:146；任务 7 已把它迁到 Row 9，此时直接移除该行）
     - VM：password props 区（:98-119 七个字段+属性）与三个死方法（:394/:409/:448）
     - code-behind：三个包装方法（:286/:304/:322）

  3. **BatchItem 补派生 bool**（Core，纳入既有 `NotifyBatchProperties()`）：
     ```csharp
     public bool IsPasswordMatching => PasswordState == BatchPasswordState.Matching;
     public bool IsPasswordMatched => PasswordState == BatchPasswordState.Matched;
     // NotifyBatchProperties() 内追加 OnPropertyChanged(nameof(IsPasswordMatching/IsPasswordMatched))
     ```

  4. **行模板徽标**（BatchFileList 的 DataTemplate 内，密度走既有 ControlHeight 资源键）：
     - `匹配中` 元素：`🔄` + `TextBlock` 绑 `LocalizedStrings[Progress_Batch_Pwd_Matching]`，`IsVisible="{Binding IsPasswordMatching}"`
     - `已匹配` 元素：`🔑 ●●●●` Button，`IsVisible="{Binding IsPasswordMatched}"`，ToolTip 绑 `LocalizedStrings[Progress_Batch_Pwd_MatchedTip]`，挂 Flyout
     - **不显示尝试规则 N/M（D4）**

  5. **Flyout**（XAML：每行模板内实例化一个共享结构；内容控件 x:Name）：
     - `Opening` 事件 code-behind：`(sender as Button)?.DataContext as BatchItem` → 填充：
       - 掩码文本：默认 `●●●●●●`；**显示态尊重 `AppSettings.PasswordRevealByDefault`**（true 则初始明文）；👁 切换按钮只切显示态
       - **复制按钮恒复制明文** `MatchedPassword`
       - 规则行：`IsVisible` = PasswordRule 非空，文本 `T("Progress_Batch_Pwd_Rule", rule)`
       - 描述行：同理 `Progress_Batch_Pwd_Desc`
     - 用 code-behind 按 x:Name 填充（Flyout 内容会继承触发元素 DataContext，直接绑定会串行数据——决策 D2 明确此点）

  6. **VM/包装方法**：
     ```csharp
     // ProgressViewModel：安全调用（集合变更需 UI 线程；与 SetCurrentBatchItem 既有约定一致）
     public void SetBatchPasswordState(int index, BatchPasswordState state,
         string? password, string? rule, string? description)
     // ProgressWindow 包装：DispatchIfNeeded(() => _vm.SetBatchPasswordState(...))
     ```

  7. **Path A 接线（App.axaml.cs，批循环开始前 L1224-1227 `TryGetValue` 区域后）**：
     ```csharp
     for (int i = 0; i < archivePaths.Count; i++)
         if (matchedPasswords.TryGetValue(archivePaths[i], out var pwd))
         {
             var entry = passwordService.FindSavedPasswordEntry(archivePaths[i], pwd); // 同 MainWindowViewModel:957 模式
             progressWindow.SetBatchPasswordState(i, BatchPasswordState.Matched,
                 pwd, entry?.Patterns is { Count: > 0 } ? string.Join("; ", entry.Patterns) : null,
                 entry?.Description);
         }
     ```
     —— 预匹配密码**开始解压前整行全亮**（D1）

  8. **Path B 接线（循环内 `ResolveCliPassword` L774 调用区）**：
     - 解析前：`SetBatchPasswordState(currentIndex, Matching, null, null, null)`
     - 解析返回非 null → `SetBatchPasswordState(currentIndex, Matched, pwd, rule, desc)`
     - 解析返回 null（无密码包）→ `SetBatchPasswordState(currentIndex, None, null, null, null)`（熄灭 Matching）
     - `rule/desc` 同 Path A 查找模式；查不到传 null（Flyout 行隐藏）

  **Must NOT do:**
  - ❌ grep 守卫未过禁止删除
  - ❌ Flyout 禁止显示「尝试规则 N/M」（D4）
  - ❌ 复制禁止只复制掩码
  - ❌ 禁止恢复/新增横幅类全局密码提示（D3/D8：只删不加）
  - ❌ 禁止在 Core 的 BatchItem 引入 UI 类型

  **Recommended Agent Profile:**
  - **Category**: `unspecified-high`（跨 Core/UI/App 三处 + 删码守卫 + 两路径接线）
  - **Skills**: `[]`

  **QA Scenarios:**
  - 预匹配密码批处理：开始前徽标全亮（Path A）；无预匹配包：轮到该包时先 🔄 后 🔑（Path B），无密码包不出现徽标
  - Flyout：默认掩码/明文随 `PasswordRevealByDefault`；👁 切换；复制后剪贴板为明文；规则/描述缺失时对应行隐藏
  - 删除后全仓 grep 死符号 = 0 命中，构建通过

  **Verification:**
  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  dotnet build src\MantisZip.Core\MantisZip.Core.csproj
  ```
  - [ ] 构建通过、`lsp_diagnostics` 无错误

  **Acceptance Criteria:**
  - [ ] 死横幅/7 个死属性/3 个死方法/3 个包装全部移除且零调用者
  - [ ] 两路径点亮行为与 D1 一致
  - [ ] 无规则 N/M 展示

  **Parallelization:**
  - **Blocked By**: Task 3、Task 7
  - **Blocks**: Task 9（同文件，串行）

---

- [ ] 9. PasswordRetryLoop 密码弹窗兜底 + 4 叶子入口接线（UI 服务）

  **Files:**
  - Create: `src/MantisZip.UI.Avalonia/Services/PasswordRetryLoop.cs`
  - Modify: `src/MantisZip.UI.Avalonia/Services/ExtractService.cs`（叶子 1，:28）
  - Modify: `src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs`（叶子 2，:153 过滤分支）
  - Modify: `src/MantisZip.UI.Avalonia/Services/SelectedItemsExtractService.cs`（叶子 3）
  - Modify: `src/MantisZip.UI.Avalonia/App.axaml.cs`（叶子 4：L1379 直连 engine + 批循环 L1248-1251 catch 补标记分支）

  **What to do:**

  1. **`PasswordRetryLoop`**（UI 层；引擎完全 unwind 后才可能弹窗——密码抛点全在并行派发前，见 Research Findings）：

  ```csharp
  namespace MantisZip.UI.Avalonia.Services;

  /// <summary>用户在密码弹窗点取消（区别于引擎密码错误——调用方据此标记「已取消 - 需要密码」）。</summary>
  public sealed class PasswordRetryCancelledException(string archivePath)
      : Exception(archivePath);

  public enum PasswordRetryOutcome { Success, Cancelled, CorruptedOrInvalid }

  public static class PasswordRetryLoop
  {
      /// <summary>attempt 失败且为密码类错误 → 弹 PasswordDialog → QuickVerifyPasswordEx 验证 →
      /// 错密码回 Status_WrongPassword 并重弹（Phase B 模式, MainWindowViewModel:966-1027）；
      /// 取消返回 Cancelled；Corrupted 返回 CorruptedOrInvalid。</summary>
      public static async Task<(PasswordRetryOutcome Outcome, string? Password)> RunAsync(
          string archivePath,
          string? initialPassword,
          IArchiveEngine engine,                 // 叶子调用点已持有 engine
          Window? owner,                         // ProgressWindow.CurrentVisible ?? MainWindow（镜像 ExtractFlow:199 模式）
          Action<string> setStatus,              // 错密码/损坏提示回调（批处理→行 ErrorMessage；单文件→StatusMessage）
          Func<string?, CancellationToken, Task> attempt,
          CancellationToken ct)
      { ... }
  }
  ```

  循环体要点：
  - `attempt(initialPassword)` 成功 → `(Success, password)`
  - catch 到 `ArchiveService.IsPasswordRelatedError(ex)`（:16）→ `Dispatcher.UIThread.InvokeAsync` 弹 `PasswordDialog(Path.GetFileName(archivePath))`（ctor :42），`ShowDialog<PasswordDialogResponse>` owner 同上
  - 返回 null（取消）→ `(Cancelled, null)`
  - `QuickVerifyPasswordEx(archivePath, resp.Password, engine)`（PasswordService:217）：
     - `WrongPassword` → `setStatus(T("Status_WrongPassword"))` → **continue 重弹**（Phase B L990-994 同构）
     - `CorruptedOrInvalid` → `setStatus(T("Status_ArchiveCorrupted"))` → `(CorruptedOrInvalid, null)`
     - 否则 → 以新 password 重跑 `attempt`
  - `OperationCanceledException`/`ct` 取消 → 原样抛出（既有取消链路处理，不吞）
  - 重试用的 `attempt` 每次以传入 password 构造 options（若 `ArchiveOptions` 非 record 则复制对象改 Password，禁止共享可变实例）

  2. **4 叶子接线（只在叶子包，禁止编排层重复包装）**：
     1. **ExtractService.ExtractAsync**（:28→:48 `engine.ExtractAsync`）：包一层；`(Cancelled,_)` → `throw new PasswordRetryCancelledException(path)`
     2. **ExtractFlow.ExtractAsync 过滤分支**（:153 内 `engine.ExtractEntriesAsync` 调用处）：同上（非过滤分支经 `ExtractService` 已覆盖，**不再包**）
     3. **SelectedItemsExtractService.ExtractEntriesAsync**：同上
     4. **App `RunCliDirectExtractBatchAsync`**（L1379 `engine.ExtractAsync`，既有 catch L1393 前）

  3. **`PasswordRetryCancelledException` 捕获点（grep 每个叶子的全部调用方补 catch，置于通用 catch 之前）**：
  - 批循环 L1248-1251：`catch (PasswordRetryCancelledException)` → `UpdateBatchItemStatus(index, Failed, T("Status_PasswordCancelled"))` → **continue 下一包（D7）**
  - 直接批 L1393：同上（行 Failed + `Status_PasswordCancelled`，批继续）
  - `ExtractFlow.RunSelectedItemsExtractionAsync` :110 既有 catch：加标记分支 → ErrorMessage=`T("Status_PasswordCancelled")`
  - `MainWindowViewModel` 解压方法（ExtractArchiveHere:2257/ToName:2284/ExtractTo:2635/SmartExtract:2663 及 Selected 系列）：既有 catch 内加分支 → `StatusMessage = T("Status_PasswordCancelled")`
  - 若某调用点**无任何 catch** → 新增 catch（仅标记，不吞其他异常）

  4. **键**：`Status_WrongPassword`/`Status_PasswordCancelled`/`Status_ArchiveCorrupted` 三语已存在——**只 grep 复核，不新增**（缺则按 Rule 13 成对补三文件）。

  **Must NOT do:**
  - ❌ Core/引擎内禁止弹窗、禁止引用 PasswordDialog（弹窗只在 UI 层叶子点）
  - ❌ 禁止在编排层（ExtractFlow.ExtractAsync 整体、批循环整体）二次包装（双弹窗风险）
  - ❌ 禁止改 `IArchiveEngine`/`ExtractService`/`ExtractFlow` 返回类型（决策锁定2：返回类型不动，取消语义走标记异常）
  - ❌ 禁止「密码同时用于后续压缩包」横幅（D8）
  - ❌ 禁止吞掉 `OperationCanceledException`

  **Recommended Agent Profile:**
  - **Category**: `deep`（跨 4 入口异常语义 + UI 线程弹窗 + 批处理取消分支，易漏调用方）
  - **Skills**: `["systematic-debugging"]`（备选：若接线后出现双弹窗/异常被吞，按该技能排查）

  **QA Scenarios:**
  - 无密码 CLI `--extract` 加密包 → 弹窗；输错 → 状态栏 `Status_WrongPassword` + 重弹；输对 → 解压成功
  - 多包批处理中 1 包取消密码 → 该行 ✗「已取消 - 需要密码」，**后续包继续**；其余包成功行状态正确
  - 拖拽解压加密包（叶子 3 路径）同样弹窗
  - 断言：解压进行中（并行批次跑动时）不出现弹窗（密码抛点在派发前）

  **Verification:**
  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
  ```
  - [ ] 构建通过 + 既有测试全绿

  **Acceptance Criteria:**
  - [ ] 4 叶子全部接线、调用方 catch 完备（grep PasswordRetryCancelledException 覆盖检查）
  - [ ] 错密码循环对齐 Phase B；取消批继续（D5/D6/D7）
  - [ ] Core 零改动（本任务）

  **Parallelization:**
  - **Blocked By**: Task 7（状态行展示）；Task 8 完成后执行（同文件相邻区域）
  - **Blocks**: F1-F4

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`

  对照本计划逐条审计实现：
  - grep 守卫（全部必须 0 命中，除历史注记节）：`MantisZip.UI\\`（非 Avalonia 路径）、`Visibility.Visible`、`Theme_Text`、`ManagedThreadId`、`ConflictActionCallback`
  - Rule 13：新增 key 三语同步（`AboutWindowTests.AllThreeLanguages_HaveSameKeySet`）；XAML 绑定 key 全部登记在 `ProgressViewModel` ctor LocalizedStrings（grep 漏登记 = 空白文案）
  - Rule 4/5/6/7/14：T7 新增 XAML 抽查（DynamicResource 均 `*Brush` 结尾、间距均 `*Thk`、开关均 IsVisible、中文注释齐）
  - Core 无中文用户可见文案（`ProgressDisplayCalculator`/引擎新增代码）
  - 与 8 项决策（D1-D8）逐条比对

- [ ] F2. **Code Quality Review** — `unspecified-high`

  - `dotnet build` Core + UI 两项目、`dotnet test` 两测试项目全绿
  - `lsp_diagnostics` 变更文件无错误
  - 重点：T4 锁纪律（Report 在锁外）、T3/T6 集中通知无漏属性、T9 无双弹窗/异常吞没

- [ ] F3. **Real Manual QA** — `unspecified-high`

  实际运行 App 验证（对照 v6 原型 13 断言）：
  - 单文件解压：统计栏 + 时间行 + 路径/文件名两行 + 三模式切换 + 三密度
  - 压缩任务：跳过/出错/已覆盖三项隐藏
  - ZIP 并行解压：批次详细行出现；7z/TAR：不出现（Rule 6）
  - 批处理密码：Path A 全亮 / Path B 逐亮、Flyout 掩码/明文/复制明文/规则描述行
  - 输错密码循环、取消 → 行 ✗ 且批继续
  - 批次切换后 ETA/速度归零重起；上一行出现完成摘要
  - 统计数字与实际文件操作抽样比对（skip/overwrite 各 ≥1 例）

- [ ] F4. **Scope Fidelity Check** — `deep`

  - 9 任务 + 4 验证项 Acceptance Criteria 全勾
  - 「与 v6 原型对应关系」表逐行有落点
  - `docs/PLAN.md:32` 登记行已同步（Rule 1：任务说明含密码徽标+弹窗兜底、估时 12-15h）
  - 无超范围改动（未动 `IArchiveEngine` 签名、未动版本号 Rule 2、未 commit Rule 3）

---

## Commit Strategy

**不主动 commit**（需用户明确要求）。用户要求时按波次提交，提交前必须先更新进度文档（Rule 3：`docs/PROGRESS.md` 里程碑 + `docs/progress-avalonia-detail.md` 细节）：

1. `feat(core): 进度统计字段、解压冲突埋点与进度计算器`（T1-T4）
2. `feat(avalonia): 进度窗口多行显示、统计时间行与双模式切换`（T5-T7）
3. `feat(avalonia): 批处理密码徽标与解压密码弹窗兜底`（T8-T9）

遵循 conventional commits 中文风格（Rule 10，scope: `core`/`avalonia`）。

---

## Success Criteria

- [ ] `dotnet build src\MantisZip.Core\MantisZip.Core.csproj` 通过
- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 通过
- [ ] `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` 全绿（含新增 `ProgressDisplayCalculatorTests`）
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` 全绿（含三语 key 校验）
- [ ] grep 守卫 0 命中：`Visibility.Visible`、`Theme_Text`、`ManagedThreadId`、`ConflictActionCallback`、非 Avalonia `MantisZip.UI\`、Core 内 `正在压缩:`/`正在解压:` 前缀
- [ ] 10 处 `ResolvePathAsync` 调用点全部埋点，统计与实际一致
- [ ] 密码徽标两路径（A 全亮/B 逐亮）+ Flyout + 死横幅/死方法清除
- [ ] 密码弹窗 4 叶子接线，错密码循环、取消批继续
- [ ] 三模式/三密度/统计栏/时间行/ETA 批次守卫全部可用
- [ ] `docs/PLAN.md:32` 已同步（Rule 1）
- [ ] 未擅自变更版本号（Rule 2）、未擅自 commit（Rule 3/10）

