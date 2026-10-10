# 进度窗口逐文件压缩后大小 + 压缩率 + 整体压缩汇总（列表列 + 统计卡段2）

> **状态**: 📋 设计已确认（2026-10-10 讨论定稿），待实施
>
> **For agentic workers:** 本文档是设计规格 + 任务分解（澄清确认已定稿，见「已确认设计决策」；文末「任务分解」含 checkbox 步骤）。执行时使用 superpowers:subagent-driven-development 或 superpowers:executing-plans。

## TL;DR

> **Quick Summary**: ZIP 压缩完成后**后置回填**每条目压缩后大小，做两处展示——① 「列表」模式每行新增一列「压缩后大小 + 压缩率」（如 `600 KB (50%)`）；② 「完整」档统计卡区**段 2 后置单行**（并行度元素旁）新增**整体压缩汇总**（如 `🗜 300 MB (60%)`）。仅 ZIP 压缩；解压/7z 固实/TAR/GZ 不显示（Rule 6 隐藏）。
>
> **Deliverables**:
> - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` —— `ArchiveProgress` 新增 `EntryCompressedBytes` / `TotalCompressedBytes`
> - `src/MantisZip.Core/Engines/ZipEngine.cs` —— `CompressAsync` 末尾后置回填（读成品 ZIP 中央目录 → 逐条目 `Completed` + 压缩字节 → 整体汇总）
> - `src/MantisZip.Core/Models/EntryProgressItem.cs` —— 新增 `CompressedSize` + 派生 `CompressedText`
> - `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs` —— `UpdateEntryStatus` 加 `long? compressedBytes` 参数；整体汇总派生属性
> - `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` —— 列表行新列；统计卡段 2 汇总元素
> - 三语 i18n 新增 key
> - `docs/PLAN.md` 登记行（规则 1）
>
> **Estimated Effort**: Low–Medium（1–1.5 天）
> **Parallel Execution**: YES — 任务 1（契约+模型）先行；任务 2（引擎）依赖 1；任务 3/4（UI，同 XAML 文件需顺序）依赖 1
> **Critical Path**: 任务 1（契约+模型）→ 任务 2（引擎后置回填）/ 任务 3（列表列）/ 任务 4（段2 汇总）→ GUI 验收

---

## Context

### Original Request

用户原话（2026-10-10）：

1. 「列表」的那个列表面板，压缩时能显示**已完成文件的压缩后大小和压缩率**吗？
2. 汇总（整体压缩率）「应该是在下面的总体信息那里……放在『并行』旁边刚刚好」——即统计卡区段 2 后置单行。
3. 确认：接受**回填**（非实时）；**仅 ZIP**；列表**加一列**；**独立新计划**；汇总格式选 **A**（`🗜 输出大小 (率%)`）。

### Research Findings

- **列表行模型**（`Core/Models/EntryProgressItem.cs`）：`EntryKey`/`Name`/`Size`/`Percent`/`State`/`StatusText`；派生 `SizeText`/`PercentText`/`StatusIcon`/`StatusBrushName`/`IsPending`/`IsActive`/`IsTerminal`。**无压缩后大小字段**。
- **列表播种/更新**：`ProgressViewModel.SeedEntryItems`（`:751`）播种；`UpdateEntryStatus(entryKey, state, percent, long? fileSize = null)`（`:788`）upsert（`_entryIndex` 字典 O(1)）。压缩侧播种入口 `CompressFlow.TrySeedEntryItemsInBackground`（`:332`，阈值 `MaxSeedEntryCount = 5000`）。
- **`ArchiveProgress`**（`Core/Abstractions/ArchiveEngine.cs:318-354`）：CurrentFile/TotalBytes/ProcessedBytes/…/EntryKey/EntryStatus —— 本计划另加 2 字段。
- **ZIP 压缩路径**：`ZipEngine.CompressAsync`（`:1262-1788`）三分支——加密走 7z.dll（`:1297+`）、多线程/N 组走 `CompressGroupWithSevenZip`（`:1543`/`:1646`/`:2485`/`:2588`）、串行走 `SharpCompress ZipWriter`（`:1729`）。**串行路径逐条目上报 Completed/Skipped**（`:1746`/`:1741`）；**MT 路径经 `FileCompressionFinished` FIFO 出队上报 Completed**（`:3572-3610`）。
- **`ListEntriesAsync` 已读 `entry.CompressedSize`**（`:1808`）→ 成品 ZIP 中央目录可读逐条目压缩大小（**加密包中央目录未加密，无需密码**）。
- **分卷**：`SplitOutputStream`（`options.SplitSize > 0`）产出 `.zip.001…`，不读作单一 ZIP → 后置回填须跳过。
- **统计卡段 2 后置单行**（`ProgressWindow.axaml` 约 `:726-744`，stats-cards 计划已实施）：当前仅「⚙ 并行 N」，`IsVisible="{Binding HasParallelDegree}"`。
- **耦合**：本计划与 `progress-window-channel-info` 计划**同改** `ArchiveProgress` / `ProgressViewModel` / `ProgressWindow.axaml`，**不同改** `EntryProgressItem`（channel-info 改 `ParallelBatchProgressItem`）。两计划不共用 `CompressionRatio`（见 D6）。建议 channel-info 先落地或两者协同校准行号。

---

## 已确认设计决策（2026-10-10 讨论定稿）

### D1 范围：仅 ZIP 压缩

- 仅对 **ZIP 格式压缩**（`ZipEngine.CompressAsync`）生效。解压不涉及压缩；7z 固实（逐文件压缩大小无定义）、7z 非固实、TAR/GZ（流式）**不显示**（对应列/段 Rule 6 隐藏）。
- 空串 = 隐藏：列表列空 → 该列不占视觉；汇总段空 → 整段（含前置分隔符）隐藏。

### D2 后置回填机制（非实时）

- 压缩**完成后**（`CompressAsync` 末尾），`OpenArchiveWithEncodingFallback(outputPath, password)` 读成品 ZIP 中央目录，遍历非目录条目：
  - 逐条目 `progress.Report(new ArchiveProgress { EntryKey = normalizedKey, EntryStatus = Completed, EntryCompressedBytes = entry.CompressedSize })`。
  - 累加 `TotalCompressedBytes`，最后上报一次 `{ TotalBytes, TotalCompressedBytes }`。
- **幂等**：逐条目 `Completed` 与压缩过程中的上报重复——`UpdateEntryStatus` 的终态累加去重守卫（检查旧状态）保证统计卡累加器不重复计数；`EntryCompressedBytes` 仅补填 `CompressedSize`。
- **分卷跳过**：`options.SplitSize > 0` 时整段回填跳过（不读 `.zip.001`）。
- 任何异常（读中央目录失败等）`CoreLog.Trace` 吞掉，仅影响展示，不影响压缩结果。

### D3 逐文件列（列表模式，加一列）

- 列表行新增一列 `EntryProgressItem.CompressedText`，内容 `压缩后大小 (率%)`（如 `600 KB (50%)`）；`Size` 或 `CompressedSize` 未知（0）→ 空串（Rule 6 隐藏）。
- 位于原大小列之后、状态列之前。
- 无列标题（与列表现有风格一致）。

### D4 整体汇总（统计卡段 2，并行旁，格式 A）

- 统计卡区段 2 后置单行新增汇总元素：`🗜 {OverallCompressionText}`，格式 A = **输出大小 (率%)**（如 `🗜 300 MB (60%)`）——**不重复** 📏 总大小卡（后者显示输入总量）。
- 位置：紧跟「⚙ 并行 N」之后，同一横排。
- `OverallCompressionText = FormatSize(TotalCompressedBytes) + " (" + ratio% + ")"`；ratio = `TotalCompressedBytes / TotalBytes × 100`（VM 计算，`Math.Clamp(...,0,100)`）。

### D5 显隐规则

- 逐文件列：`CompressedText` 空 → 该格隐藏（`StringNotEmpty`）。
- 整体汇总段：`HasOverallCompression`（= `TotalCompressedBytes > 0`）才显示。
- 分隔符 `·`：在「并行」与「压缩汇总」**都显示**时才出现——用一个组合可见性（如新增派生 `ShowChannelMetaSeparator => HasParallelDegree && HasOverallCompression`）或把分隔符并入汇总段前置、随其显隐。
- 「并行」与「压缩汇总」各自独立显隐：只有其一 → 单独显示、无分隔符；都无 → 整段 2 隐藏。

### D6 字段契约（新增，不改既有语义）

- `ArchiveProgress` 新增：
  ```csharp
  /// <summary>逐条目压缩后字节（后置回填；0 = 未知/非 ZIP 压缩）。</summary>
  public long EntryCompressedBytes { get; set; }

  /// <summary>整体压缩后字节（后置回填；0 = 未知）。</summary>
  public long TotalCompressedBytes { get; set; }
  ```
- `EntryProgressItem` 新增 `long CompressedSize`（0 = 未知）+ 派生 `CompressedText`。
- `ProgressViewModel.UpdateEntryStatus(entryKey, state, percent, long? fileSize = null, long? compressedBytes = null)`——追加末参（前向兼容）。
- **不**复用 `progress-window-channel-info` 的 `CompressionRatio`（批次级语义不同，避免混淆）；本计划整体率由 VM 从 `TotalCompressedBytes/TotalBytes` 计算。

---

## Work Objectives

### Core Objective

ZIP 压缩完成后，用户能在**列表模式**逐文件看到「压缩后大小 + 压缩率」，并在**统计卡区**看到整体压缩汇总；解压与非 ZIP 格式自然不显示。

### Concrete Deliverables

- `ArchiveEngine.cs`：`ArchiveProgress` 新增 `EntryCompressedBytes` / `TotalCompressedBytes`。
- `ZipEngine.cs`：`CompressAsync` 末尾后置回填（读成品 ZIP → 逐条目 `EntryCompressedBytes` + 整体 `TotalCompressedBytes`）；分卷跳过；异常吞掉。
- `EntryProgressItem.cs`：`CompressedSize` + `CompressedText`。
- `ProgressViewModel.cs`：`UpdateEntryStatus` 追加 `compressedBytes` 参数并写入行；`HasOverallCompression` + `OverallCompressionText` 派生属性；`ShowChannelMetaSeparator`（D5）。
- `ProgressWindow.axaml`：列表行新列；统计卡段 2 汇总元素 + 分隔符。
- 三语 i18n：段 2 汇总标签 key（`Progress_Stats_Compressed`，如「压缩」）。
- 测试：`ProgressViewModelTests`（UpdateEntryStatus 写入压缩字节、整体汇总派生）；`EntryProgressItem` 派生 `CompressedText` 用例；`ZipEngineTests`（后置回填上报压缩字节的集成断言）；`ProgressWindowXamlTests`（列/段绑定）。

### Definition of Done

- [ ] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误 0 警告
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj` → 全绿（既有预存失败除外）
- [ ] `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全绿
- [ ] GUI 手动验收：ZIP 压缩完成后，列表行显示「压缩后大小 + 压缩率」、统计卡段 2 显示 `🗜 300 MB (60%)`；解压、7z/TAR/GZ 压缩、分卷压缩下均不显示（隐藏）

### Must Have

- 列表模式逐文件列显示压缩后大小 + 压缩率（仅 ZIP 压缩完成后）。
- 统计卡段 2 在「并行」旁显示整体压缩汇总（格式 A）。
- 逐文件列/汇总段缺失数据时按 Rule 6 隐藏；分隔符随两侧显隐。
- 后置回填**不改**既有逐条目终态语义（重复 `Completed` 幂等）。
- 新增 axaml 控件中文注释（规则 14）；间距用 `{DynamicResource Spacing*}`（规则 5）。

### Must NOT Have (Guardrails)

- **不得**做实时（压缩过程中不显示）——本项目明确采用回填（D2）。
- **不得**改 `ArchiveProgress` 既有字段语义（只加字段）；`ConflictStats.ApplyTo` 不丢新字段（原地改）。
- **不得**为解压/7z/TAR/GZ 显示压缩后大小（无意义）——对应列/段隐藏。
- **不得**读分卷 `.zip.001` 作 ZIP（D2 跳过）。
- **不得**复用 channel-info 的批次级 `CompressionRatio` 作整体汇总（D6）。
- **不得**新增列表列标题（与既有列表风格一致）。
- 版本号不变（规则 2）。

---

## Verification Strategy (MANDATORY)

### Test Decision

- **Infrastructure exists**: YES（xunit.v3 + Avalonia.Headless.XUnit；`ProgressViewModelTests`/`ProgressWindowXamlTests`/`ZipEngineTests` 既有）。
- **Automated tests**: Unit（`UpdateEntryStatus` 写压缩字节、`CompressedText` 派生、整体汇总派生、后置回填集成）。
- **Framework**: xunit.v3，沿用既有文件与 `[Fact]`/`[AvaloniaFact]` 模式。

### AI-Driven QA

- 视觉验收（列宽、段 2 排布、分隔符显隐）走**人工 GUI 验收**，不做浏览器 QA。

### 自动化验证命令

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```

---

## 依赖与后续

- **依赖**：与 `progress-window-channel-info` 计划**同改 3 文件**（`ArchiveEngine.cs`/`ProgressViewModel.cs`/`ProgressWindow.axaml`）→ **建议 channel-info 先落地**再执行本计划，或将两者行号协同校准；`progress-stats-cards-three-row` 已实施（commit `6fc2b01`，段 2 并行后置元素来源）。
- **后续受益**：后置回填机制可扩展至 `AddToArchiveAsync`（追加到已有包）——本计划**不含**，如需另立。
- **文档同步**：本文件新增 → `docs/PLAN.md` P2 区登记（规则 1）。

---

## 任务分解

> **执行方式**: superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，checkbox 逐步勾选。
> **提交策略**: 默认**不 commit**（未经用户明确要求）；仅用户要求时执行末尾提交任务，并按规则 3 先更新进度文档。

### 执行波次

```
Wave 1（契约 + 模型）
└── 任务 1: ArchiveProgress 新字段 + EntryProgressItem.CompressedSize + VM 参数/派生（TDD）

Wave 2（引擎）
└── 任务 2: ZipEngine.CompressAsync 末尾后置回填（Blocked By: 1）

Wave 3（UI，同 XAML 文件需顺序）
├── 任务 3: 列表行新增压缩后列（Blocked By: 1）
└── 任务 4: 统计卡段 2 整体汇总 + 分隔符 + i18n（Blocked By: 1）

Wave FINAL
├── 任务 5: 构建 + 双测试套 + GUI 验收（Blocked By: 1–4）
└── 任务 6: 进度文档同步与提交（仅用户要求时；Blocked By: 5）
```

---

- [ ] 1. 契约 + 模型 + VM（`ArchiveEngine.cs` + `EntryProgressItem.cs` + `ProgressViewModel.cs`，TDD）

  **What to do**:

  (1a) 写失败测试 —— `tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs` 追加：

  ```csharp
  [AvaloniaFact]
  public void UpdateEntryStatus_WithCompressedBytes_SetsCompressedSize()
  {
      var vm = new ProgressViewModel();
      vm.SeedEntryItems(new[] { ("docs/a.txt", "a.txt", 1000L) });
      vm.UpdateEntryStatus("docs/a.txt", EntryRowState.Completed, null, fileSize: 1000, compressedBytes: 500);
      var row = vm.EntryItems[0];
      Assert.Equal(500, row.CompressedSize);
      Assert.Contains("500 B", row.CompressedText);
      Assert.Contains("50%", row.CompressedText);
  }

  [AvaloniaFact]
  public void UpdateEntryStatus_WithoutCompressedBytes_LeavesCompressedTextEmpty()
  {
      var vm = new ProgressViewModel();
      vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 1000L) });
      vm.UpdateEntryStatus("a.txt", EntryRowState.Completed, null);
      Assert.Equal(string.Empty, vm.EntryItems[0].CompressedText);
  }

  [AvaloniaFact]
  public void SetProgress_TotalCompressedBytes_PopulatesOverallSummary()
  {
      var vm = new ProgressViewModel();
      vm.SetProgress(new ArchiveProgress { TotalBytes = 1000, PercentComplete = 100, TotalCompressedBytes = 600 });
      Assert.True(vm.HasOverallCompression);
      Assert.Contains("600 B", vm.OverallCompressionText);
      Assert.Contains("60%", vm.OverallCompressionText);
  }
  ```

  运行确认编译级红（`CompressedSize`/`CompressedText`/`HasOverallCompression`/`OverallCompressionText` 尚不存在）：
  ```powershell
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~ProgressViewModelTests"
  ```

  (1b) `ArchiveEngine.cs:354`（`EntryStatus` 属性后）追加：
  ```csharp
  /// <summary>逐条目压缩后字节（后置回填；0 = 未知/非 ZIP 压缩）。</summary>
  public long EntryCompressedBytes { get; set; }

  /// <summary>整体压缩后字节（后置回填；0 = 未知）。</summary>
  public long TotalCompressedBytes { get; set; }
  ```

  (1c) `EntryProgressItem.cs` 新增（镜像 `Size`/`SizeText` 模式，`Set` 变更时通知派生）：
  ```csharp
  private long _compressedSize;

  /// <summary>压缩后字节（后置回填；0 = 未知）。</summary>
  public long CompressedSize
  {
      get => _compressedSize;
      set { if (Set(ref _compressedSize, value)) OnPropertyChanged(nameof(CompressedText)); }
  }

  /// <summary>压缩后大小 + 压缩率（如 "600 KB (50%)"）；Size 或 CompressedSize 未知（0）时返回空串（Rule 6 隐藏）。</summary>
  public string CompressedText => _compressedSize > 0 && _size > 0
      ? $"{FormatUtil.FormatSize(_compressedSize)} ({_compressedSize * 100.0 / _size:0}%)"
      : string.Empty;
  ```
  注意：`Size` 的 setter 亦须追加 `OnPropertyChanged(nameof(CompressedText));`（率依赖 Size，播种后 Size 先于 CompressedSize 到达，仍显式通知以防顺序变化）。

  (1d) `ProgressViewModel.cs`：
  - `UpdateEntryStatus` 签名追加末参 `long? compressedBytes = null`；在方法体（`row.State = state;` 附近）写入：
    ```csharp
    if (compressedBytes is > 0)
        row.CompressedSize = compressedBytes.Value;
    ```
  - 消费端（`SetProgress` 内处理 `EntryStatus != null` 处，调用 `UpdateEntryStatus` 的地方）把末参从 `null` 改为 `p.EntryCompressedBytes`（或新增第 5 实参）。实施时 grep `UpdateEntryStatus(` 调用点。
  - 新增整体汇总派生 + 通知：
    ```csharp
    /// <summary>整体压缩汇总可用（后置回填 TotalCompressedBytes > 0）。</summary>
    public bool HasOverallCompression => _overallCompressedBytes > 0;

    /// <summary>整体压缩汇总文案："300 MB (60%)"（格式 A，D4/D6）。</summary>
    public string OverallCompressionText => _overallCompressedBytes > 0
        ? $"{FormatUtil.FormatSize(_overallCompressedBytes)} ({_overallCompressedBytes * 100.0 / Math.Max(1, _overallTotalBytes):0}%)"
        : string.Empty;
    ```
    私有字段 `_overallCompressedBytes`/`_overallTotalBytes`；在 `SetProgress` 收到 `TotalCompressedBytes > 0` 时写入并集中通知（`OnPropertyChanged(nameof(HasOverallCompression)); OnPropertyChanged(nameof(OverallCompressionText));`）。
  - `ShowChannelMetaSeparator`（D5）：`public bool ShowChannelMetaSeparator => HasParallelDegree && HasOverallCompression;`，随二者变化通知（并入现有集中通知）。
  - 清空：`ClearEntryItems`/批次重置处重置 `_overallCompressedBytes`/`_overallTotalBytes` 并通知。

  (1e) 验证：`dotnet build` → 0 错误；`dotnet test ... --filter ProgressViewModelTests` → 新测试绿。

- [ ] 2. `ZipEngine.CompressAsync` 末尾后置回填（`ZipEngine.cs`）

  **What to do**:

  (2a) 在 `CompressAsync`（`:1262`）末尾、`CoreLog.Info("CompressAsync: done…")`（`:1784`）之前插入后置回填（**仅非分卷**）：
  ```csharp
  // 后置回填（本计划 D2）：成品 ZIP 写出后读中央目录，逐条目回填压缩后大小 + 整体汇总。
  // 仅 ZIP（本引擎）；分卷跳过；加密包中央目录未加密，无需密码。
  if (options.SplitSize <= 0)
  {
      try
      {
          long totalCompressed = 0;
          using (var finalArchive = OpenArchiveWithEncodingFallback(outputPath, options.Password))
          {
              foreach (var entry in finalArchive.Entries)
              {
                  if (entry.IsDirectory) continue;
                  var key = ArchivePath.Normalize(entry.Key);
                  totalCompressed += entry.CompressedSize;
                  progress?.Report(new ArchiveProgress
                  {
                      EntryKey = key,
                      EntryStatus = ArchiveEntryStatus.Completed,
                      EntryCompressedBytes = entry.CompressedSize,
                  });
              }
          }
          if (totalCompressed > 0)
              progress?.Report(new ArchiveProgress
              {
                  TotalBytes = totalBytes,
                  TotalCompressedBytes = totalCompressed,
              });
      }
      catch (Exception ex)
      {
          CoreLog.Trace("CompressAsync: post-pass compressed-size fill skipped: {0}", ex.Message);
      }
  }
  ```
  （`totalBytes` 来自 `:1272` `FileScanner.CollectFiles`；`ArchivePath.Normalize` 与 `ListEntriesAsync:1802` 同源。）

  (2b) 集成断言 —— `tests/MantisZip.Tests/Engines/ZipEngineTests.cs` 追加：压缩若干文件后，捕获的进度序列应至少出现一次 `EntryCompressedBytes > 0` 的条目上报与一次 `TotalCompressedBytes > 0` 的整体上报；解压路径不产生 `EntryCompressedBytes`。（无现成进度捕获用例则复用既有压缩测试的捕获模式。）

  (2c) 验证：`dotnet test tests\MantisZip.Tests` → 全绿（既有引擎测试监测压缩行为未破坏）。

- [ ] 3. 列表行新增压缩后列（`ProgressWindow.axaml`）

  **What to do**:

  (3a) 定位列表模式 `ItemsControl`（数据源 `EntryItems`）的行模板（含 `SizeText`/`StatusText` 列），在**原大小列之后、状态列之前**插入：
  ```xml
  <!-- 压缩后大小 + 压缩率（仅 ZIP 压缩完成回填；空=隐藏，规则 6） -->
  <TextBlock Text="{Binding CompressedText}"
             IsVisible="{Binding CompressedText, Converter={StaticResource StringNotEmpty}}"
             FontSize="11" VerticalAlignment="Center"
             Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
  ```
  尺寸/间距沿用同模板既有列（`{DynamicResource Spacing*}`，规则 5）；加中文注释（规则 14）。

  (3b) 验证：build 0 错误；`ProgressWindowXamlTests` 若有行模板结构断言需同步；GUI 并入任务 5。

- [ ] 4. 统计卡段 2 整体汇总 + 分隔符 + i18n（`ProgressWindow.axaml`）

  **What to do**:

  (4a) i18n（规则 13，三语成对）：`strings.zh-CN.json`/`strings.en.json`/`strings.zh-TW.json` 新增 `Progress_Stats_Compressed`（如「压缩」/`Compressed`/「壓縮」）。插入文件头 `{` 之后（key 不排序），UTF-8 无 BOM + CRLF。

  (4b) 统计卡区段 2（`:726-744` 现有「⚙ 并行 N」StackPanel）改为容纳两个独立显隐元素 + 条件分隔符：
  ```xml
  <!-- 段 2：全局元信息后置单行（并行度 + 整体压缩汇总；两者各自显隐） -->
  <StackPanel Orientation="Horizontal" Spacing="{DynamicResource SpacingXs}"
              HorizontalAlignment="Center" Margin="0,8,0,0">
    <!-- 并行度（仅并行操作显示） -->
    <StackPanel Orientation="Horizontal" Spacing="{DynamicResource SpacingXxs}"
                IsVisible="{Binding HasParallelDegree}">
      <TextBlock Text="⚙" FontSize="14" VerticalAlignment="Center"
                 Foreground="{DynamicResource ThemeProgressFillBrush}" />
      <TextBlock Text="{Binding LocalizedStrings[Progress_Stats_Parallel]}" FontSize="11"
                 VerticalAlignment="Center" Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
      <TextBlock Text="{Binding ParallelDegree}" FontSize="13" FontWeight="SemiBold"
                 VerticalAlignment="Center" Foreground="{DynamicResource ThemeProgressFillBrush}" />
    </StackPanel>
    <!-- 分隔符（仅并行与压缩汇总都在时显示） -->
    <TextBlock Text="·" FontSize="11" VerticalAlignment="Center"
               IsVisible="{Binding ShowChannelMetaSeparator}"
               Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
    <!-- 整体压缩汇总（仅 ZIP 压缩完成后显示；格式 A：输出 (率%)） -->
    <StackPanel Orientation="Horizontal" Spacing="{DynamicResource SpacingXxs}"
                IsVisible="{Binding HasOverallCompression}">
      <TextBlock Text="🗜" FontSize="14" VerticalAlignment="Center"
                 Foreground="{DynamicResource ThemeProgressFillBrush}" />
      <TextBlock Text="{Binding LocalizedStrings[Progress_Stats_Compressed]}" FontSize="11"
                 VerticalAlignment="Center" Foreground="{DynamicResource ThemeTextSecondaryBrush}" />
      <TextBlock Text="{Binding OverallCompressionText}" FontSize="13" FontWeight="SemiBold"
                 VerticalAlignment="Center" Foreground="{DynamicResource ThemeProgressFillBrush}" />
    </StackPanel>
  </StackPanel>
  ```
  **注意**：外层 StackPanel 不再绑 `IsVisible`（原「⚙ 并行 N」段落曾整段绑 `HasParallelDegree`）——改由内部两个子元素各自控制；整段 2 无内容时因两子元素皆隐藏而视觉消失（空 StackPanel 不占高度，若出现占位则给外层加组合可见性 `HasParallelDegree || HasOverallCompression`）。`LocalizedStrings[Progress_Stats_Compressed]` 须登记进 `MainWindowViewModel.UpdateLocalizedStrings()` 的 keys 数组（规则 13）。

  (4c) 验证：build 0 错误；三语 key 校验通过（`AboutWindowTests.AllThreeLanguages_HaveSameKeySet`）。

- [ ] 5. 构建 + 双测试套 + GUI 验收

  ```powershell
  dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
  dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
  dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
  ```

  GUI 验收：① ZIP 压缩（多线程）+「列表」模式 → 每完成文件行显示「压缩后大小 (率%)」，完成后统计卡段 2 出现 `🗜 X MB (Y%)`（并行时与「⚙ 并行 N」间有 `·`）；② ZIP 串行压缩 → 同上（无「并行」，汇总单独显示无分隔符）；③ 7z / TAR / GZ 压缩 → 列表无压缩后列、段 2 无汇总；④ 解压 → 无压缩后列、无汇总；⑤ 分卷 ZIP 压缩 → 无汇总（跳过回填）。

- [ ] 6. 进度文档同步与提交（仅用户明确要求提交时）

  按规则 3 更新 `docs/PROGRESS.md`（里程碑，若属新功能上线）+ `docs/progress-avalonia-detail.md`（细节），再按规则 10 conventional commits 提交。

---

## 附：关键行号索引（2026-10-10 落笔时校准，实施前须重新 grep 定位）

| 符号 | 文件 | 行 |
|------|------|-----|
| `ArchiveProgress` 字段区 | Core/Abstractions/ArchiveEngine.cs | :318-354（+2 新字段插在 EntryStatus 后） |
| `EntryProgressItem` 字段/派生 | Core/Models/EntryProgressItem.cs | 全文件（约 167 行） |
| `UpdateEntryStatus` | UI.Avalonia/ViewModels/ProgressViewModel.cs | :788 |
| `SeedEntryItems` | 同上 | :751 |
| `SetProgress` 主体 | 同上 | :540 |
| `CompressAsync`（ZIP 压缩总入口） | Core/Engines/ZipEngine.cs | :1262-1788 |
| 串行逐条目 Completed 上报 | 同上 | :1746 |
| MT `FileCompressionFinished` FIFO 上报 | 同上 | :3572-3610 |
| `ListEntriesAsync` 读 CompressedSize（模式参考） | 同上 | :1808 |
| 统计卡段 2 并行后置元素 | UI.Avalonia/Dialogs/ProgressWindow.axaml | :726-744 |
| 列表模式行模板（`EntryItems`，加列目标） | 同上 | 待 grep（列表 ItemsControl） |
| `UpdateLocalizedStrings` keys 数组 | UI.Avalonia/ViewModels/MainWindowViewModel.cs | 约 :203 |
| 三语 i18n 文件 | UI.Avalonia/Localization/strings.*.json | — |
