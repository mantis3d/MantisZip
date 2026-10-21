# 压缩预估 (Compression Estimator)

> 选好文件后、实际压缩前，估算各格式/级别的最终大小与耗时。
> **状态**: 🔶 部分实现（比率表 / 格式目录 / 自适应规则模型已完成；估算器本体未开始）
> **创建**: 2026-06-01 | **修订**: 2026-10-21（拆分 + 事实订正） | **优先级**: P2 | **预估工时**: 14-18h
> **前置**: 无硬依赖（`CompressionCoefficients` / `FormatCatalog` / `ArchiveFormat` 均已落地）
>
> **拆分说明（2026-10-21）**：原 `compression-estimator.md`（1299 行）混了三件事，现拆为：
> - **本文** = 压缩前预估（快速 / 标准 / 精确 + 设置窗口面板）
> - [runtime-eta.md](runtime-eta.md) = 压缩中 ETA（先验速度融合 + 批处理总剩余）
> - [adaptive-compression-level.md](../已完成/adaptive-compression-level.md) = 自适应压缩级别（已随 v0.5.2 发布，归档供回溯）

---

## 1. 目标与范围

**目标**：在 `CompressSettingsWindow` 中，选好文件后显示一张预估表，让用户在点「压缩」前就知道各格式/级别的预期大小与耗时。

```
┌─────────────────────────────────────────────────────────┐
│  压缩预估值                                              │
│  ┌─────────────────────────────────────────────────┐    │
│  │ 格式   级别   预计大小             预计耗时       │    │
│  │              标准      自适应      标准  自适应   │    │
│  │ ─────────────────────────────────────────────── │    │
│  │ ZIP     5    12.3 MB  10.1 MB*   ~2s   ~1s     │    │
│  │ ZIP     9    11.1 MB  10.0 MB*   ~4s   ~1s     │    │
│  │ 7z      5     8.7 MB   7.9 MB*   ~8s   ~3s     │    │
│  │ 7z      9     7.2 MB   6.8 MB*  ~15s   ~4s     │    │
│  │ Tar.gz  5    13.8 MB  11.2 MB*   ~3s   ~1s     │    │
│  └─────────────────────────────────────────────────┘    │
│  * 自适应：image_lossy / media / archive 自动降级 Store  │
│  [刷新预估]                                              │
└─────────────────────────────────────────────────────────┘
```

**范围内**：快速预估（查表）、标准预估（采样）、可选精确预估、UI 面板、自适应感知列。
**范围外**：压缩中 ETA（→ [runtime-eta.md](runtime-eta.md)）；自适应压缩本体（已完成）；分卷开销建模（§7 明确不做）；学习型历史数据库（§3.10 明确不做）。

---

## 2. 现状核实（2026-10-21 逐项核对）

| 项 | 事实 | 位置 |
|----|------|------|
| 比率表（**已有，勿重写**） | `CompressionCoefficients` 键为 **`(category, level)`**：ZIP 表 `_rates`、7z 表 `_sevenZipRates`；公开 `GetRate(category, level, format)` / `GetStoreRate(category)` / `ClassifyByExtension(fileName)` | `Core/Utils/CompressionCoefficients.cs:135`（`_rates`）/`:197`（`_sevenZipRates`）/`:246`/`:260`/`:280` |
| **Tar 缺陷（必须修）** | `GetRate` 只分 SevenZip 与其他（`:262`）→ `ArchiveFormat.Tar` 落进 ZIP 表，文本给出 0.15，而 Tar 实际不压缩 | 同上 `:262` |
| 格式目录（**已有**） | `FormatCatalog.GetAll(customFormats?)` / `GetById(id, customFormats?)` / `GetByExtension(ext, customFormats?)` | `Core/Services/FormatCatalog.cs:103/115/125` |
| 规则匹配（**已有但未接线**） | `AdaptiveRuleMatcher.ResolveLevel` / `ResolveAdaptiveLevel` / `ComputeMajorityLevel` 均存在，但**生产调用点为 0**（引擎只调 `ZipEntryClassifier`，扩展名 + `MultiThreadedStoreFormatIds`） | `Core/Utils/AdaptiveRuleMatcher.cs:22/82/105`；接线缺口见 [adaptive-compress-ui-convergence.md](../进行中/adaptive-compress-ui-convergence.md) Phase 2 |
| 归档格式 | `ArchiveFormat` = `Zip / SevenZip / Tar / GZip / Rar / Iso`；`.tar.gz`/`.tgz` 由 `TarGzEngine` 按扩展名分派，**不是独立枚举值** | `Core/Abstractions/ArchiveEngine.cs:287` |
| 包版本 | SharpCompress **0.50.4**、SharpSevenZip **2.0.45** | `MantisZip.Core.csproj` |
| 设置窗口插入点 | 根 `Grid RowDefinitions="*,Auto"`（左 TabControl / 中 Splitter / 右 `ResultTreeView`；底行按钮栏）→ 预估面板插在按钮栏前，改 `*,Auto,Auto` | `Dialogs/CompressSettingsWindow.axaml:22-28`（根）/`:568-589`（按钮栏） |
| 已有异步模式 | VM 用 `_previewCts`（CancellationTokenSource 取消旧构建）+ `_previewBuildVersion`（丢弃过期结果），**无 debounce 计时器** | `ViewModels/CompressSettingsViewModel.cs:665`（`BuildCompressPreview`） |
| 本地化 | 该窗口在**构造函数内**直接填 `LocalizedStrings[...] = LocalizationManager.T(...)` 字典（`:493-549`），XAML 用 `{Binding LocalizedStrings[Key]}`。**没有** `UpdateLocalizedStrings()` 方法，也**没有** `CultureChanged` 订阅（模态窗口生命周期短，切语言需重开） | `ViewModels/CompressSettingsViewModel.cs:493-549` |
| ETA（**已实现，本文不碰**） | `ProgressSpeedTracker`（EMA + 批次切换重置）、`ProgressViewModel` 已显示已用/剩余、暂停冻结时钟 | `Core/Utils/ProgressDisplayCalculator.cs:72-120`；`ProgressViewModel.cs:922-929`/`:1311-1337` |
| 存储目录（若将来持久化） | 必须 `AppSettings.DataDir`（便携模式跟随），禁止硬编码 `%LOCALAPPDATA%` | `Models/AppSettings.cs:232` |

**结论**：本计划真正的新增只有 —— ① 估算器（比率 × 大小 + 开销 + 耗时）；② 耗时模型；③ UI 面板与交互。比率表、格式目录、自适应规则模型都已在库里。

---

## 3. 设计

### 3.1 精度分级

| 精度 | 方法 | 触发 | 误差目标 |
|------|------|------|---------|
| 快速 | `CompressionCoefficients.GetRate` × 大小 + 条目开销 | 打开窗口 / 选文件变化（防抖 500ms） | ±35% |
| 标准 | 采样压缩外推（§3.5） | 用户点「刷新预估」 | ±15% |
| 精确 | 完整压缩到 `Stream.Null`（§3.6，可选） | 显式勾选且总量 ≤ 阈值 | ±1% |

> **熵过滤方案不采用**：字节级 Shannon 熵需要 ≥64KB 样本，且文件头偏差严重（ZIP/JPEG 头熵低但载荷近随机）、不反映 LZ 匹配能力。同成本下采样真压精度高一个数量级。此结论保留，避免重复提案。

### 3.2 快速预估（比率）

```
estSize = Σ_files  size_i × Rate(category_i, level, format)
        + Overhead(format, entryCount, sizes)      // §3.3
```

- `category = CompressionCoefficients.ClassifyByExtension(path)`（无扩展名 → `binary`，已知行为）。
- `Rate` 直接复用 `GetRate`；**必须先修 Tar 分支**（§3.3）。
- 自适应列：对每个文件按规则解析有效级别（复用 `AdaptiveRuleMatcher.ResolveLevel`），降级 Store 的文件用 `GetStoreRate` = 1.0。
  - ⚠️ 规则尚未接线到引擎，但**预估列可以先用**：预估只读配置，不影响实际压缩结果。UI 需在规则未生效期间不暗示「已启用」——见 §3.8 的说明文案。

### 3.3 条目开销与 Tar 精确建模

**问题**：纯比率模型忽略每条目固定开销。1 万个小文件时 ZIP 多出约 1MB、Tar 多出 5MB+，误差远超 ±35%。

| 格式 | 模型 |
|------|------|
| ZIP | `Σ roundup(compressed_i) + entryCount × 100B`（local header 30B + central dir 46B + data descriptor 16B + EOCD，取上界） |
| Tar | **不压缩，精确建模**：`Σ (512 + roundup(size_i, 512)) + 1024` |
| GZip（单文件 .gz） | `10B 头 + roundup(compressed) + 8B 尾` |
| Tar.gz | `Tar 大小 × GZip 比率 + 18B` |
| 7z | `Σ compressed_i + entryCount × 32B`（solid 块头，近似） |

**必须同时修的库缺陷**：`CompressionCoefficients.GetRate` 增加 `ArchiveFormat.Tar` 分支（返回 1.0），否则 Tar 预估必然错误。

### 3.4 耗时模型（新增 `CompressionSpeedModel`）

比率有级别维度，但**耗时完全没有模型**。新增 `Core/Utils/CompressionSpeedModel.cs`：

```csharp
public static class CompressionSpeedModel
{
    /// <summary>单线程基准吞吐（MB/s）。初值，由 T6 基准测试校准后固化。</summary>
    public static double GetSpeedMBps(ArchiveFormat format, int level, bool multiThreaded);

    /// <summary>每条目固定开销（毫秒）。小文件场景主导耗时。</summary>
    public static double GetPerEntryOverheadMs(ArchiveFormat format);
}
```

初值（**待 T6 用固定语料实测校准**，不可当成事实引用）：

| 格式 | L1 | L3 | L5 | L7 | L9 | Store |
|------|----|----|----|----|----|-------|
| ZIP (Deflate) | 60 | 45 | 30 | 18 | 12 | 600 |
| GZip | 60 | 45 | 30 | 18 | 12 | 600 |
| 7z (LZMA2) | 12 | 8 | 5 | 3 | 2 | 600 |
| Tar | — | — | — | — | — | 800 |

- 单位 MB/s（单线程）。多线程开启时乘 `MultiThreadedFactor`（初值 3.0；`compression-performance-optimization` 实测 7z `mt=on` 为 4.63x，取保守值）。
- `duration = Σ size_i / speed + entryCount × perEntryMs`。
- Store 走 I/O 限速，与格式无关。
- 7z 逐文件建归档固定开销高（每文件数十至数百 ms），快速模式用 `perEntryMs` 近似即可。

### 3.5 标准预估（采样）

**硬约束：必须有总预算**，否则「上限 10000 文件 × 1MB」= 最多 10GB 读取，与「~3s」矛盾。

```csharp
const long MaxSampleBytes = 64L * 1024 * 1024;  // 总采样预算
const int  MaxSampleFiles = 200;                 // 总文件数上限
const int  PerFileSample   = 1 * 1024 * 1024;    // 单文件上限
```

1. 按文件大小**降序**取样（大文件占比高，优先覆盖），直到预算或文件数用尽；小文件（< 64KB）直接整体计入，不做采样。
2. **ZIP / GZip**：逐文件压缩其样本（writer 开销低），汇总 `compressedSample / sampleBytes`。
3. **7z**：把多个文件的样本**拼接成一个流**压缩一次（避免每文件建归档的固定开销）；接受 solid-block 偏差，在 UI 标注「±15%」。
4. 外推：`estSize = (compressedSample / sampleBytes) × totalBytes + Overhead(...)`；样本未覆盖的部分按同比率处理。
5. 每轮循环 `ct.ThrowIfCancellationRequested()`；返回 `Confidence`（样本覆盖率 = `sampleBytes / totalBytes`，clamp 0.2–0.95）。

### 3.6 精确预估（可选，默认关闭）

完整压缩到 `Stream.Null` 等同真实压缩。**风险**：现有引擎写入需要可 Seek 输出（ZIP64 约束）且 `CompressAsync` 以输出路径为入口 —— 若无法无文件落地，本项直接降级为「不做」。实现前先验证 `ZipEngine`/`SevenZipEngine` 是否接受 `Stream`；不接受则跳过（不为此改引擎）。

### 3.7 接口（Core 不含任何显示字符串）

```csharp
public sealed class EstimateResult
{
    public ArchiveFormat Format { get; init; }
    public int Level { get; init; }
    public long EstimatedSize { get; init; }
    public TimeSpan EstimatedDuration { get; init; }
    public double Confidence { get; init; }   // 0..1
    public EstimationMethod Method { get; init; } // Quick / Standard / Exact
}

public enum EstimationMethod { Quick, Standard, Exact }

public static class CompressionEstimator
{
    /// <summary>快速预估单行。level 必须显式传入（原设计缺 level，无法生成按级别区分的表格）。</summary>
    public static EstimateResult QuickEstimate(
        IReadOnlyList<string> sourceFiles, long totalBytes,
        ArchiveFormat format, int level,
        bool adaptiveEnabled, IReadOnlyList<AdaptiveOverrideRule>? rules,
        IReadOnlyList<FormatDefinition>? customFormats);

    /// <summary>估算表格：格式 × 级别 组合（默认 5 与 9；UI 可配置）。</summary>
    public static Task<IReadOnlyList<EstimateResult>> EstimateAsync(
        IReadOnlyList<string> sourceFiles,
        IReadOnlyList<ArchiveFormat> formats,
        IReadOnlyList<int> levels,
        EstimationMethod method,
        bool adaptiveEnabled, IReadOnlyList<AdaptiveOverrideRule>? rules,
        IReadOnlyList<FormatDefinition>? customFormats,
        CancellationToken ct = default);
}
```

- **输入是文件列表**（不是 `string[] sourcePaths` 的目录）：目录枚举由调用方（VM）完成，Core 只做计算。原因：原签名隐含递归枚举与不可读文件处理，未定义。
- 显示字符串（`"12.3 MB"` / `"~2s"` / 列头）全部由 UI 用 `LocalizationManager.T` + `FormatUtil.FormatSize` 生成（规则 13）。

### 3.8 UI 面板

插入 `CompressSettingsWindow.axaml` 根 Grid（`:22-28`）第 1 行、按钮栏（`:568-589`）之前：

- 根 `Grid RowDefinitions="*,Auto,Auto"`；预估面板 Row 1，按钮栏 Row 2。
- 容器 `Border`：`Background="{DynamicResource ThemeSurfaceBgBrush}"`、`BorderBrush="{DynamicResource ThemeBorderBrush}"`、`CornerRadius="{DynamicResource BorderRadius}"`、`Padding="{DynamicResource SpacingMdThk}"`（规则 4/5）。
- 表格用 `ItemsControl` + 行模板；**行 `MinHeight="{DynamicResource ControlHeightSm}"`**（规则 7 豁免于 DataGrid 但 ItemsControl 项模板必须设）。
- 所有文案 key 在 `CompressSettingsViewModel` 构造函数内的 `LocalizedStrings[...] = LocalizationManager.T(...)` 区块（`:493-549`）追加，三语成对（规则 13）；该 VM 无 `UpdateLocalizedStrings()`、无 `CultureChanged` 订阅，不要凭空新增刷新机制；`CompressSettingsWindow.axaml` 新增区块加中文注释（规则 14）。
- 说明文案需区分两件事：
  - 自适应列 = 「按当前规则预估」，规则未接线到引擎时显示提示「规则将在压缩时生效（接线中）」，避免误导。
  - 预估值仅供参考，标准模式误差 ±15%。

### 3.9 交互

- 打开窗口：跑一次快速预估（后台线程，不阻塞 UI）。
- 源文件/过滤器变化：**500ms 防抖**（现有代码无 debounce，直接重建；新增 `DispatcherTimer` 或 `CancellationTokenSource.CancelAfter`）。
- 「刷新预估」：标准模式；同一时刻只允许一轮，重复点击取消旧轮（复用 `_previewCts` 模式）。
- 缓存：key = `(文件路径集合哈希, 各自 LastWriteTime+Length, format, level, method, adaptiveEnabled)`；命中直接复用，避免重开窗口重算。
- 预估失败（文件不可读/被占用）：跳过该文件并在面板显示「N 个文件无法读取」，不整体失败。

### 3.10 明确不做

| 项 | 理由 |
|----|------|
| 学习型历史数据库（原 §学习型预估） | 已知扩展名先查比率表命中即返回，学习数据永不生效；长尾格式本就该走采样（更准）。复杂度（分桶 × 级别 × 格式、冷启动 3 次、并发落盘、损坏清理）换不到精度。若将来确有需求，作为独立小计划。 |
| 熵过滤精度层 | 见 §3.1 注：成本与采样相当，精度低一个数量级。 |
| 分卷开销建模 | 分卷涉及每卷目录/尾部开销，模型复杂且收益小；本计划只给单卷大小。UI 可提示「分卷后每卷另有约 100KB 开销」。 |
| 加密对大小的影响 | AES 加密不改变压缩后大小（仅加少量头），忽略。 |

---

## 4. 任务分解（TDD；每任务一 commit）

- [ ] **T1 — Core 接口 + 快速预估**
  - `EstimateResult` / `EstimationMethod` / `CompressionEstimator.QuickEstimate`；纯函数，无 IO（大小由调用方传入）。
  - **测试**：文本 10MB @ ZIP5 ≈ 1.5MB；媒体 10MB @ ZIP9 ≈ 10MB；无扩展名 → `binary`；空列表 → 0 且不抛。
  - commit。

- [ ] **T2 — Core `CompressionSpeedModel` + 基准校准**
  - 新增 `Core/Utils/CompressionSpeedModel.cs`；`GetSpeedMBps` / `GetPerEntryOverheadMs`。
  - **测试**：级别越高速度越低（单调）；Store 最快；MT 因子 > 1；`duration > 0`。
  - **校准**：用固定语料（见 T7）实测 ZIP/7z 各级别吞吐，回填表值；把实测值写进代码注释。
  - commit。

- [ ] **T3 — 修 `CompressionCoefficients.GetRate` 的 Tar 分支 + 开销模型**
  - `ArchiveFormat.Tar` → 所有分类 1.0；新增 `OverheadCalculator`（ZIP/Tar/GZip/7z 按 §3.3）。
  - **测试**：Tar 文本比率 == 1.0（回归原缺陷）；Tar 1000 个 1KB 文件预估 == `1000×1024 + 1024`；ZIP 1000 文件开销 ≈ 100KB。
  - commit。

- [ ] **T4 — Core 标准预估（采样 + 预算 + 取消）**
  - 按 §3.5：降序取样、64MB/200 文件预算、ZIP 逐样本、7z 拼接、`ct` 检查。
  - **测试**：预算生效（构造 300 个 10MB 假文件，采样字节 ≤ 64MB）；`ct` 取消抛 `OperationCanceledException`；样本覆盖率写入 `Confidence`。
  - commit。

- [ ] **T5 — UI 面板（XAML + VM）**
  - 根 Grid 改三行；面板容器 + `ItemsControl`；VM 暴露 `EstimateRows`（含显示字符串）+ `IsEstimating` + `EstimateHint`。
  - 规则 4/5/7/13/14 全量落地（主题键、`Spacing*Thk`、行 `MinHeight`、三语 key 登记、中文注释）。
  - **测试**：VM 单测（无头）——快速预估产生非空行；`AdaptiveAvailable=false` 时自适应列隐藏。
  - commit。

- [ ] **T6 — 交互（防抖 / 取消 / 缓存 / 失败降级）**
  - 500ms 防抖；刷新按钮取消旧轮；缓存 key 见 §3.9；不可读文件计数提示。
  - **测试**：连续 5 次选文件变化只触发 1 次预估；重复点刷新取消前一轮；缓存命中不重复计算（计数 spy）。
  - commit。

- [ ] **T7 — 精度基准测试（固定语料）**
  - 语料：`tests/fixtures/estimator/` 下生成（测试内 `Random` 固定种子）：① 文本日志 10MB ② 随机二进制 10MB ③ 1000 个 4KB 小文件 ④ 混合目录。
  - **断言**：快速模式 ±35%、标准模式 ±15%、Tar 预估误差 ≤ 1%。
  - commit。

---

## 5. 涉及文件

**新增**
- `Core/Utils/CompressionEstimator.cs`、`Core/Utils/CompressionSpeedModel.cs`、`Core/Utils/ArchiveOverheadCalculator.cs`
- `Core/Models/EstimateResult.cs`（或并入 Estimator 文件）
- `tests/MantisZip.Tests/Utils/CompressionEstimatorTests.cs`、`CompressionSpeedModelTests.cs`、`ArchiveOverheadTests.cs`

**修改**
- `Core/Utils/CompressionCoefficients.cs`（Tar 分支）
- `Dialogs/CompressSettingsWindow.axaml` + `.axaml.cs`（三行布局 + 面板 + 交互）
- `ViewModels/CompressSettingsViewModel.cs`（`EstimateRows` / 防抖 / 缓存 / i18n 登记）
- `Resources/Strings/strings.{zh-CN,en,zh-TW}.json`（新增 key 三语）
- `docs/PLAN.md`、`docs/progress-avalonia-detail.md`

---

## 6. 验证

```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test  tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test  tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```

- `lsp_diagnostics` 无错误。
- **精度基准**（T7）全绿：快速 ±35% / 标准 ±15% / Tar ≤ 1%。
- 手动：打开窗口即出快速值；切格式/级别 500ms 后更新；点刷新出标准值且 UI 不卡；1000 文件目录采样总耗时 < 3s；三语文案齐全；Compact/Normal/Loose 三档布局不破；Light/Dark 配色正常。

---

## 7. 边界 / 非目标

- **不做** ETA（[runtime-eta.md](runtime-eta.md)）。
- **不做** 学习型历史库、熵过滤、分卷开销、加密大小差异（§3.10）。
- **不改** `ArchiveProgress`（估算在压缩前，与进度无关）。
- **不改** 引擎（精确预估若需引擎改造则放弃该项）。
- **不做** `Rar` / `Iso` 输出预估（本应用不支持这两种压缩输出）。

---

## 8. 风险

| 风险 | 缓解 |
|------|------|
| 快速模式误差大（经验系数） | UI 标注「估算值」；标准模式一键刷新；T7 固定语料守门 |
| 7z 采样 solid-block 偏差 | 拼接样本 + 标注 ±15%；必要时对 7z 提高采样预算 |
| 大目录采样超时 | 64MB / 200 文件硬预算 + 大文件优先 + `ct` 取消 |
| 预估阻塞 UI | 全程后台线程；防抖 500ms；`IsEstimating` 禁用刷新按钮 |
| 规则未接线导致自适应列与实际不符 | 面板文案明确「规则接线中」；或自适应列在 Phase 2 完成前不显示（T5 决策点） |
| 精确模式需引擎改 Stream 输出 | 先验证，不支持则直接砍（§3.6） |
| 与 `GetRate` 现有测试冲突（Tar 改 1.0） | T3 同步更新 `AdaptiveCompressionTests` 中受影响断言 |

---

## 9. 后续扩展

- **分卷推荐**：根据预估大小推荐分卷策略（含每卷开销）。
- **格式推荐**：按文件类型标注「7z 更小 / ZIP 更快」。
- **多目录汇总**：「总空间节省」摘要。
- **先验速度输出**：把快速预估的 `size/duration` 交给 [runtime-eta.md](runtime-eta.md) 做初始速度。
