# 压缩中 ETA 增强 (Runtime ETA)

> 压缩/解压进行中的剩余时间：先验速度融合 + 批处理总剩余 + 状态文案。
> **状态**: 🔶 基础 ETA 已实现（EMA 实时速度 + 单档案 ETA + 暂停冻结）；本计划只做增强项
> **创建**: 2026-10-21（自 `compression-estimator.md` 拆出） | **优先级**: P2 | **预估工时**: 5-7h
> **前置**: [compression-estimator.md](compression-estimator.md) 的快速预估（提供先验速度；可降级为「无先验」运行）

---

## 1. 目标

现状：进度窗口已显示「已用 / 剩余」，但只有**实时 EMA 速度**，存在三个可感知问题：

| 问题 | 现象 | 根因 |
|------|------|------|
| 冷启动无 ETA | 前 1–3 秒剩余时间为空 | `_emaBytesPerSecond` 从 0 起，需积累样本 |
| 批处理只算当前包 | 处理第 1 个包时显示「剩余 2 秒」，实际总共 30 秒 | `ComputeEtaSeconds` 只看当前档案 |
| 末尾抖动 | 最后几 KB 剩余时间在 1s / 2s / 空之间跳 | 速度样本噪声 + 无「即将完成」态 |

**目标**：① 冷启动即有值（先验融合）；② 批处理显示总剩余；③ 末尾稳定为「即将完成」。

**范围外**：压缩前预估（[compression-estimator.md](compression-estimator.md)）；自适应压缩（已完成）。

---

## 2. 现状核实（2026-10-21）

| 项 | 事实 | 位置 |
|----|------|------|
| 速度追踪器 | `ProgressSpeedTracker`：EMA `0.7/0.3`、100ms 节流、计数回退即重置、`OnArchiveSwitch` 重置基线 | `Core/Utils/ProgressDisplayCalculator.cs:72-120` |
| 公开 API | `OnArchiveSwitch(long bytesBefore, DateTime now)` / `RecordSample(long totalProcessed, DateTime now)` / `ComputeEtaSeconds(long processedInArchive, long totalInArchive)` / `ProcessedInArchive(long total)` | 同上 `:81/90/110/118` |
| 单档案 ETA 接线 | 进度回调里 `_speedTracker.ComputeEtaSeconds(...)` → `_lastEtaSeconds` → `RemainingText` | `ViewModels/ProgressViewModel.cs:922-929` |
| 文案重灌 | 语言切换时用 `_lastEtaSeconds` 重算 `RemainingText`（**不要破坏这条路径**） | 同上 `:227-229` |
| 冻结时钟 | 暂停/模态对话框期间冻结已用时间（`ElapsedText`） | 同上 `:1311-1337` |
| 已有 key | `Progress_Time_Elapsed` / `Progress_Time_Remaining`（带 `{0}` 占位） | 同上 `:163-164` |
| 视图 | 时间行 `ElapsedText` + `RemainingText`（`StringNotEmpty` 控制显隐） | `Dialogs/ProgressWindow.axaml:869-880` |
| 进度数据 | `ArchiveProgress.ProcessedBytes` / `TotalBytes`（三引擎全路径埋点） | `Core/Abstractions/ArchiveEngine.cs:337-400` |

**关键约束**：`ProgressSpeedTracker` 是 **Core 纯计算类**，所有时间参数显式传入（可注入假时钟测试）。增强必须保持这一点 —— 不引入 `DispatcherTimer`、不读 `DateTime.Now`。

---

## 3. 设计

### 3.1 先验速度融合（解决冷启动）

```csharp
// Core/Utils/ProgressDisplayCalculator.cs（扩展，不新建平行类）
public sealed class ProgressSpeedTracker
{
    /// <summary>注入先验速度（bytes/s）。0 = 无先验。</summary>
    public void SeedPrior(double bytesPerSecond);

    /// <summary>融合速度：前 N 秒线性提高实测权重。</summary>
    public double EffectiveBytesPerSecond { get; }
}
```

融合公式：

```
w = clamp(elapsedSinceSeed / 5.0, 0, 1)      // 5 秒内从「全信先验」过渡到「全信实测」
speed = w × ema + (1 - w) × prior            // ema 为 0 时直接取 prior
```

- 先验来源：`compression-estimator` 的快速预估 `totalBytes / estimatedDuration`。若预估不可用（用户直接 CLI 压缩、或预估失败）→ `SeedPrior(0)`，行为与今天完全一致。
- `OnArchiveSwitch` **不清除先验**（同一批任务的先验相同），但重置 `w` 的计时起点。
- 先验偏差过大时的自愈：若实测 `ema` 与 `prior` 相差 > 5 倍且 `elapsed > 3s`，直接丢弃 prior（`prior = 0`），避免「先验拖慢收敛」。

### 3.2 批处理总剩余（解决只算当前包）

```csharp
public double? ComputeTotalRemainingSeconds(
    long processedInArchive, long totalInArchive,
    long pendingBytesAfterThisArchive);
```

```
currentRemaining = totalInArchive - processedInArchive
totalRemaining   = currentRemaining + pendingBytesAfterThisArchive
etaSeconds       = totalRemaining / EffectiveBytesPerSecond
```

- `pendingBytesAfterThisArchive` 由 UI 层在批次开始时累加得到（`MainWindowViewModel` 已有批次文件列表与各包大小，**无需新增 Core API**）。
- 单包场景 `pending = 0`，退化为现有行为。
- 文案区分：单包 → 「剩余 0:02:15」；批处理 → 「剩余 0:30:10（共 12 个包）」。新增 key `Progress_Time_RemainingBatch`（三语）。

### 3.3 状态文案（解决末尾抖动）

| 条件 | 显示 |
|------|------|
| 无速度且已过 1s | 「估算中…」（新 key `Progress_Time_Estimating`） |
| `0 < eta < 5s` | 「即将完成」（新 key `Progress_Time_AlmostDone`） |
| `eta == 0` 且进度 100% | 「已完成」（复用现有完成文案，若无则新 key） |
| 冻结（暂停/模态） | 保持最后值，不刷新（沿用 `:1311-1337` 冻结语义） |
| `eta` 为 null 且已过 1s | 「剩余 --:--」（新 key `Progress_Time_Unknown`） |

**抖动抑制**：`eta` 显示值按 1s 量化后，若与上次显示相差 < 1s 则不更新 `RemainingText`（避免每秒两次刷新导致的视觉跳动）。

### 3.4 分层与所有权

```
Core: ProgressSpeedTracker（纯计算，显式时间/字节入参）
UI:   ProgressViewModel 持有实例 → 每次进度回调 RecordSample → 计算 ETA → 格式化文案
```

**禁止**：在 Core 里创建 UI 对象、读本地化、读 `AppSettings`（规则 15 同理：Core 不反向依赖）。

---

## 4. 任务分解（TDD）

- [ ] **T1 — `SeedPrior` + 融合速度**
  - 扩展 `ProgressSpeedTracker`：`SeedPrior` / `EffectiveBytesPerSecond` / 5s 线性权重 / 5 倍偏差自愈。
  - **测试**（假时钟）：① 无样本时 ETA 用先验 → 非 null；② 5s 后速度收敛到实测；③ prior 偏差 10 倍且 3s 后 → prior 被丢弃；④ `SeedPrior(0)` 行为与旧实现逐值一致（回归）。
  - commit。

- [ ] **T2 — 批处理总剩余**
  - `ComputeTotalRemainingSeconds`；`OnArchiveSwitch` 保留 prior。
  - **测试**：pending=0 与旧公式等价；pending>0 时 ETA 按总字节线性增长；跨档案切换不串速度。
  - commit。

- [ ] **T3 — 文案状态机 + 抖动抑制**
  - `ProgressViewModel`：估算中 / 即将完成 / 未知 / 批处理；量化抑制；语言切换重灌路径同步（`:227-229`）。
  - **测试**：状态迁移表（null+1s→估算中；3s→即将完成；100%→已完成）；`CultureChanged` 后 `RemainingText` 使用当前语言。
  - commit。

- [ ] **T4 — 三语 key + 接线**
  - `strings.{zh-CN,en,zh-TW}.json` 新增 4 个 key（`Progress_Time_Estimating` / `AlmostDone` / `Unknown` / `RemainingBatch`），插入文件头 `{` 之后；`ProgressViewModel` 的 keys 数组（`:163` 附近）登记。
  - **测试**：`AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 通过；`mantiszip_i18n` 校验。
  - commit。

- [ ] **T5 — 先验注入接线**
  - 压缩入口把 `compression-estimator` 的快速预估结果换算为 bytes/s 传给 `SeedPrior`；不可用时不注入。
  - **测试**：注入路径存在（源码守卫）；未启用预估时 `SeedPrior` 未被调用。
  - commit。

---

## 5. 涉及文件

- `Core/Utils/ProgressDisplayCalculator.cs`（`ProgressSpeedTracker` 扩展）
- `ViewModels/ProgressViewModel.cs`（文案状态机 / 批处理 / 语言切换）
- `Dialogs/ProgressWindow.axaml`（仅在需要新行时改；默认复用 `:869-880`）
- `Resources/Strings/strings.{zh-CN,en,zh-TW}.json`
- 压缩入口（`MainWindowViewModel` / `CompressService` 调用方，注入先验与 pending 字节）
- `tests/MantisZip.Tests/Utils/ProgressDisplayCalculatorTests.cs`、`tests/MantisZip.UI.Avalonia.Tests/...`

---

## 6. 验证

```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test  tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test  tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```

- 手动：① 压缩 500MB 单包，首秒即显示剩余时间；② 批量压缩 10 个包，剩余时间覆盖全部；③ 最后 5 秒显示「即将完成」；④ 暂停 10 秒后恢复，剩余时间不跳变；⑤ 中/英/繁切换后文案即时更新。
- **回归重点**：无先验（CLI 直接压缩）时数值与改造前一致。

---

## 7. 风险

| 风险 | 缓解 |
|------|------|
| 先验严重偏差 → 首秒显示荒谬值 | 5s 线性收敛 + 5 倍偏差 3s 后丢弃；文案显示「估算中」弱化早期数值 |
| 批处理 pending 字节统计不准（过滤/失败跳过） | 只用于总剩余展示；失败包在切换时扣减；不参与进度条百分比 |
| 语言切换路径遗漏 → 文案卡在旧语言 | T3 测试覆盖 `CultureChanged`；沿用现有 `_lastEtaSeconds` 重灌机制 |
| EMA 抖动 | 1s 量化抑制；不改变 EMA 系数（避免影响已验收行为） |
