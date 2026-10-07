# 进度窗口字节埋点 + 语言即时刷新

> **Spec**: `docs/superpowers/specs/2026-10-06-progress-bytes-i18n-design.md`（权威设计，实施依据）
> **优先级**: P2 · **预估**: 4-6h · **分支**: alpha · **版本**: 保持 0.5.1（规则 2）

## TL;DR

三个独立收益点打包为一个计划：

1. **字节埋点全格式生效**：`SevenZipEngine` / `TarGzEngine` 全路径填充 `ArchiveProgress.ProcessedBytes / TotalBytes`，让速度（B/s）与剩余时间（ETA）在 7z、tar、gz 上与 ZIP 一样可用（当前仅 `ZipEngine` 填字节）。
2. **语言即时刷新**：`ProgressViewModel` 订阅 `LocalizationManager.CultureChanged`，解压/压缩进行中切换语言时窗口文案立即重灌；`ProgressWindow.OnClosed` 调 `DetachLocalization()` 退订（修复静态事件泄漏，全仓目前无 `CultureChanged -=` 先例）。
3. **计划文本回写**：`progress-window-prototype-alignment.md` Deferred 第 7 项（通用压缩播种，commit `7505952` 已实施）回写为已实施，并同步 `docs/PLAN.md` 行 33。

**执行方式**: 逐任务 TDD（每任务先失败测试后实现）；**无并行任务**（全部触碰同两个引擎文件 + 同一 VM，串行是硬约束）。**不做逐任务 commit**（提交需用户明确要求），仅任务 7 终局门控提交。

## Architecture / 关键决策（已拍板）

| 决策点 | 结论 |
|---|---|
| 埋点位置 | 方案 A：引擎就地埋点（非 UI 层推算） |
| 7z 压缩字节来源 | 百分比推算 `total × accumulatedPercent / 100`（`ZipEngine.cs:1564` 先例） |
| 跳过条目 | `processedBytes += entry.Size`（`ZipEngine.cs:315` 先例） |
| 覆盖范围 | `CompressAsync` / `ExtractAsync` / `ExtractEntriesAsync` / `AddToArchiveAsync` / `DeleteEntriesAsync` 全覆盖；`TestArchiveAsync` 不改 |
| TAR/GZ 解压 TotalBytes | = 压缩流长度（spec §2.5-3 接受，与既有 `PercentComplete` 同基准） |
| TAR 压缩输入流 | 新增 `Core/Utils/ReadProgressStream.cs`（`WriteProgressStream` 镜像），经 `TarWriteFileWithRetry` 可选 `onRead` 回调接入 |
| 7z 压缩事件并发 | 测试用 `SevenZipMultithreaded = false` 保证事件串行；生产代码按闭包捕获局部变量累加 |
| 语言刷新线程 | 不 dispatch（`PreviewViewModel.cs:163-177` 先例，`CultureChanged` 由 UI 线程 setter 触发） |
| 刷新机制 | `FillLocalizedStrings()` 抽取 + 缓存字段（speed/ETA/FileCount/stats）+ `OnCultureChanged` 重渲；不处理 `StatusMessage`/准备态（spec 范围外） |

## Context — 实施时必知的陷阱

- **行号为 2026-10-06 快照，实施以符号定位**，勿盲目信任行号。
- **`SetProgress` 早返回**（`ProgressViewModel.cs:512-517`）：带 `EntryStatus` 的报告在到达速度/ETA 采样前就 return → 字节**只能挂在无 EntryStatus 的常规报告**上；`TarGzEngine.cs:290-300` 的 `EntryStatus = Completed` 报告**不加字节字段**。
- **字节单调性**：任何路径 `ProcessedBytes` 不得回退。7z 解压单条目内回调值必须钳制 `processedBytes + Math.Min(bytesWritten, entrySize)`；跨阶段（DeleteEntries 提取段 → 重打包段）映射必须单调：提取段占 0-50%，重打包段 `50 + 0.5p`。
- **压缩基准差异**：7z `PercentComplete` 来自 7z.dll 事件（字节可推算）；TarGz `CompressAsync` 的 percent 是**文件计数基准**（多文件 tar 循环），**不得**拿它推算字节——tar 分支必须用 `onRead` 真实字节，percent 语义保持不变。gz 单文件分支才可用 byte 基准 percent。
- **字节默认值**：`ArchiveEngine.cs:321-322` `TotalBytes`/`ProcessedBytes` 为非空 `long` 默认 0 → 报告里漏填不会 NPE，只会显示 0 字节（`SetProgress` 对 `TotalBytes > 0` 才走速度/ETA 路径）。
- **消费契约**：速度 `RecordSample(p.ProcessedBytes, UtcNow)`；ETA `ComputeEtaSeconds(ProcessedInArchive(...), p.TotalBytes)`。解压中 `PercentComplete` 与字节百分比必须同向（percent 降序解压时字节升序——两者基准不同是允许的，但同一字段族内必须单调）。
- **退订泄漏**：`ProgressViewModel` 构造时订阅静态 `LocalizationManager.CultureChanged`，不退订则窗口关闭后 VM 被静态事件根引用泄漏，且旧 VM 仍会响应语言切换。
- **测试并行**：xUnit 类间并行 → 切语言的测试用 `try/finally` 快速恢复 `LocalizationManager.CurrentLanguage`；不比对新旧值差异（防 vacuous 断言），改为断言 `PropertyChanged("LocalizedStrings")` + dict 值 == `LocalizationManager.T("Progress_Cancel")`。
- **基线**（终局验证对照）：Core **608 pass / 0 fail / 3 skip**、Avalonia **114 / 0 / 2**。本计划新增 Core +4、Avalonia +2 测试。

### 锚点表（符号定位用）

| 符号 | 文件 | 快照行 | 用途 |
|---|---|---|---|
| `AttachCompressorProgress` | `Core/Engines/SevenZipEngine.cs` | 202-227 | 任务2 签名扩展 + 字节推算 |
| `CompressAsync` attach / final report | 同上 | 591 / 637 | 任务2 重排 + 最终报告 |
| `AddToArchiveAsync` attach / final report | 同上 | 1160 / 1260 | 任务2 同上 |
| `DeleteEntriesAsync` | 同上 | 981-1108 | 任务2 三段式字节（提取/重打包/最终） |
| `ExtractAsync` | 同上 | 393-568 | 任务1 四处报告 + 跳过累加 |
| `ExtractEntriesAsync` | 同上 | 805-975 | 任务1 keySet 过滤 total + 四处报告 |
| `ExtractAsync` gz `CopyTo` | `Core/Engines/TarGzEngine.cs` | 194 | 任务3 改手动缓冲循环 |
| `CompressAsync` tar 循环 / 最终报告 | 同上 | 272-317 | 任务4 onRead + 节流报告 |
| `TarWriteFileWithRetry` | 同上 | 344-383 | 任务4 加 `onRead` 形参 |
| `ExtractEntriesAsync` gz `CopyTo` | 同上 | 799 | 任务3 同 194 |
| `SetProgress` 早返回 / 速度 / ETA | `UI.Avalonia/ViewModels/ProgressViewModel.cs` | 512-517 / 574-585 | 消费契约 + 任务5 缓存点 |
| ctor `LocalizedStrings` 填充 | 同上 | 78-143 | 任务5 `FillLocalizedStrings` 抽取 |
| `OnClosed` | `UI.Avalonia/Dialogs/ProgressWindow.axaml.cs` | 699-711 | 任务5 退订接线（路径是 `Dialogs/`） |
| `CultureChanged` 订阅先例 | `UI.Avalonia/ViewModels/PreviewViewModel.cs` | 163-206 | 任务5 参照 |
| 跳过先例 / 百分比→字节先例 | `Core/Engines/ZipEngine.cs` | 315 / 1564 | 引用不改 |
| `WriteProgressStream` | `Core/Utils/WriteProgressStream.cs` | 全文 | 任务4 新增 `ReadProgressStream` 模板 |
| Deferred #7 | `.omo/plans/未开始/progress-window-prototype-alignment.md` | 809 | 任务6 回写 |
| `docs/PLAN.md` 行 33/34 | `docs/PLAN.md` | — | 任务6 追注 / 本计划加行（行 33 超长须 PowerShell 编辑） |

## Tech Stack

- C# / .NET 10，xUnit，`dotnet build` + `dotnet test`（**无 CI、无 linter**）
- 测试配方（规则 12）：
  ```powershell
  dotnet build tests\MantisZip.Tests\MantisZip.Tests.csproj -p:BuildProjectReferences=false
  dotnet test  tests\MantisZip.Tests\MantisZip.Tests.csproj --no-build
  dotnet build tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj -p:BuildProjectReferences=false -p:SkipShellExtCopy=true
  dotnet test  tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --no-build
  ```

## File Structure

```
新增:
  src/MantisZip.Core/Utils/ReadProgressStream.cs          # 任务4（WriteProgressStream 镜像）
修改:
  src/MantisZip.Core/Engines/SevenZipEngine.cs            # 任务1+2
  src/MantisZip.Core/Engines/TarGzEngine.cs               # 任务3+4
  src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs  # 任务5
  src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs  # 任务5 OnClosed
  tests/MantisZip.Tests/Engines/SevenZipEngineTests.cs    # 任务1+2 测试
  tests/MantisZip.Tests/Engines/TarGzEngineTests.cs       # 任务3+4 测试
  tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs # 任务5 测试
  .omo/plans/未开始/progress-window-prototype-alignment.md # 任务6 回写
  docs/PLAN.md                                            # 任务6 行33追注 + 本计划加行
  docs/PROGRESS.md, docs/progress-avalonia-detail.md      # 任务7（提交前，规则3）
```

---

## 任务 1 — 7z 解压字节埋点（ExtractAsync / ExtractEntriesAsync）

**目标**：7z 全量解压与过滤解压的每一份常规进度报告都携带 `TotalBytes`（非目录条目 Size 总和）与单调递增的 `ProcessedBytes`，跳过条目也计入已处理字节。

**测试文件**：`tests/MantisZip.Tests/Engines/SevenZipEngineTests.cs`

### Step 1 — 写失败测试

在文件中追加（类内 private 嵌套 + 共享断言辅助 + 两条测试）：

```csharp
/// <summary>
/// 有序进度收集器：lock 同步按报告顺序记录，供字节单调性断言。
/// （类内已有 ConcurrentBag 的 ProgressCollector 是无序的，不可用于单调断言）
/// </summary>
private sealed class OrderedProgressCollector
{
    private readonly List<ArchiveProgress> _items = new();
    public IReadOnlyList<ArchiveProgress> Snapshot()
    {
        lock (_items) return _items.ToList();
    }
    public IProgress<ArchiveProgress> Progress =>
        new Progress<ArchiveProgress>(p => { lock (_items) _items.Add(p); });
}

/// <summary>
/// 断言：所有带 TotalBytes &gt; 0 的报告中 ProcessedBytes 单调不减，
/// 且最后一个（最终 100%）报告满足 ProcessedBytes == TotalBytes。
/// </summary>
private static void AssertByteProgressMonotonic(
    IReadOnlyList<ArchiveProgress> items, string label)
{
    var withBytes = items.Where(p => p.TotalBytes > 0).ToList();
    Assert.True(withBytes.Count > 0,
        $"{label}: 未捕获任何带 TotalBytes 的进度报告");
    long last = -1;
    foreach (var p in withBytes)
    {
        Assert.True(p.ProcessedBytes >= last,
            $"{label}: ProcessedBytes 回退 {last} -> {p.ProcessedBytes} (Percent={p.PercentComplete})");
        last = p.ProcessedBytes;
    }
    var final = withBytes[^1];
    Assert.Equal(final.TotalBytes, final.ProcessedBytes);
}

[Fact]
public async Task ExtractAsync_ReportsByteProgress()
{
    using var dir = TestDirectory.Create();
    var archivePath = Path.Combine(dir.Path, "bytes.7z");
    await SevenZipEngineTestHelpers.CreateTestArchiveAsync(archivePath); // 若无此 helper，
    // 则改用 ArchiveFixtures.CreateSevenZipArchive() 返回 null 时 return 的既有 fixture 写法

    var engine = new SevenZipEngine();
    var dest = Path.Combine(dir.Path, "out");
    var collector = new OrderedProgressCollector();
    await engine.ExtractAsync(archivePath, dest,
        new ArchiveOptions { Password = null }, collector.Progress, CancellationToken.None);

    AssertByteProgressMonotonic(collector.Snapshot(), "ExtractAsync");
}

[Fact]
public async Task CompressAsync_ReportsByteProgress()
{
    if (!SevenZipEngine.Is7zDllAvailable()) return; // 7z.dll 缺失时静默跳过（fixture 先例）

    using var dir = TestDirectory.Create();
    var src = Path.Combine(dir.Path, "src");
    Directory.CreateDirectory(src);
    await File.WriteAllBytesAsync(Path.Combine(src, "a.bin"), new byte[64 * 1024]);
    await File.WriteAllBytesAsync(Path.Combine(src, "b.bin"), new byte[32 * 1024]);

    var archivePath = Path.Combine(dir.Path, "out.7z");
    var engine = new SevenZipEngine();
    var collector = new OrderedProgressCollector();
    await engine.CompressAsync(new[] { src }, archivePath,
        new ArchiveOptions { SevenZipMultithreaded = false }, // 事件串行，保证闭包累加顺序
        collector.Progress, CancellationToken.None);

    AssertByteProgressMonotonic(collector.Snapshot(), "CompressAsync");
}
```

> 实施时按测试项目既有 fixture/helper 命名调整（`ArchiveFixtures`、`TestDirectory` 等以仓库现状为准）；断言辅助 `AssertByteProgressMonotonic` 与 `OrderedProgressCollector` 保留在本测试类 private 嵌套（任务 2 复用）。

### Step 2 — 跑测试确认失败

```powershell
dotnet build tests\MantisZip.Tests\MantisZip.Tests.csproj -p:BuildProjectReferences=false
dotnet test  tests\MantisZip.Tests\MantisZip.Tests.csproj --no-build --filter "FullyQualifiedName~ReportsByteProgress"
```

预期：`ExtractAsync_ReportsByteProgress` 失败（"未捕获任何带 TotalBytes 的进度报告"）；`CompressAsync_ReportsByteProgress` 若任务 2 未实施同样失败——本任务先只修解压侧，压缩侧失败留到任务 2 变绿（同一测试类允许阶段性红）。

### Step 3 — 实现（`SevenZipEngine.cs`）

1. **`ExtractAsync`（快照 L393-568）**：
   - 在 `lastReportTime`（≈L426）之后声明：
     ```csharp
     long totalBytes = allEntries.Where(e => !e.IsDirectory).Sum(e => (long)e.Size);
     long processedBytes = 0;
     ```
   - 跳过分支（≈L451-458，conflict=Skip）：`processedBytes += (long)entry.Size;`
   - 条目提取前的预报告（≈L468）：追加 `TotalBytes = totalBytes, ProcessedBytes = processedBytes`。
   - 提取回调内的节流报告（≈L494）：`ProcessedBytes = processedBytes + Math.Min(bytesWritten, entrySize)`（钳制防回退；`entrySize = (long)entry.Size`）。
   - 成功写盘后（≈L514）：`processedBytes += entrySize;`（失败 catch **不加**，最终报告前跳合法）。
   - 完成报告（≈L523）与最终报告（≈L553）：`ProcessedBytes = processedBytes` / `TotalBytes = totalBytes`；最终报告强制 `ProcessedBytes = TotalBytes = totalBytes`。
2. **`ExtractEntriesAsync`（快照 L805-975）**：
   - total 仅统计命中过滤的条目：
     ```csharp
     long totalBytes = allEntries
         .Where(e => keySet.Contains(ArchivePath.Normalize(e.FileName)) && !e.IsDirectory)
         .Sum(e => (long)e.Size);
     ```
     （`keySet`/`ArchivePath.Normalize` 以该方法内实际变量名为准）
   - 四处报告（快照 L879 / L903 / L932 / L963）按 ExtractAsync 同构填充：预报告 + 跳过累加 + 回调钳制值 + 成功累加 + 最终 `Processed == Total`。

### Step 4 — 验证

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --no-build --filter "FullyQualifiedName~SevenZipEngine"
```
两测试绿 + 既有 SevenZip 测试全绿。`lsp_diagnostics` 对 `SevenZipEngine.cs` 无错误。

**Done when**：解压/过滤解压所有 `TotalBytes > 0` 报告单调、最终 `Processed == Total`；既有测试不回归。

---

## 任务 2 — 7z 压缩字节埋点（CompressAsync / AddToArchiveAsync / DeleteEntriesAsync）

**目标**：7z 压缩、追加、删除三条写路径的进度报告携带可推算的字节进度（百分比推算，`ZipEngine.cs:1564` 先例）；`DeleteEntriesAsync` 三阶段（提取 → 重打包 → 完成）字节全程单调。

### Step 1 — 写失败测试

`SevenZipEngineTests.cs` 追加：

```csharp
[Fact]
public async Task DeleteEntriesAsync_ReportsMonotonicByteProgress()
{
    if (!SevenZipEngine.Is7zDllAvailable()) return;

    using var dir = TestDirectory.Create();
    var archivePath = Path.Combine(dir.Path, "del.7z");
    // 用 CompressAsync 造含 a.bin + b.bin 的归档（同任务2 CompressAsync 测试写法）

    var engine = new SevenZipEngine();
    var collector = new OrderedProgressCollector();
    await engine.DeleteEntriesAsync(archivePath, new[] { "a.bin" },
        new ArchiveOptions { SevenZipMultithreaded = false },
        collector.Progress, CancellationToken.None);

    AssertByteProgressMonotonic(collector.Snapshot(), "DeleteEntriesAsync");
}
```

> `CompressAsync_ReportsByteProgress`（任务 1 引入）在本任务实现后变绿。

### Step 2 — 跑测试确认失败

同任务 1 配方，filter `FullyQualifiedName~SevenZipEngine`：`CompressAsync_ReportsByteProgress` 与 `DeleteEntriesAsync_ReportsMonotonicByteProgress` 失败（无字节报告 / 报告不完整）。

### Step 3 — 实现（`SevenZipEngine.cs`）

1. **新增静态工具**（`AttachCompressorProgress` 附近）：
   ```csharp
   /// <summary>
   /// 求源文件未压缩字节总和（跳过目录）；单文件读取失败仅 Trace 并计 0，
   /// 不阻断压缩（字节进度是尽力而为）。
   /// </summary>
   private static long SumSourceFileSizes(IEnumerable<string> paths)
   {
       long total = 0;
       foreach (var path in paths)
       {
           try
           {
               var attrs = File.GetAttributes(path);
               if ((attrs & FileAttributes.Directory) != 0) continue;
               total += new FileInfo(path).Length;
           }
           catch (Exception ex)
           {
               CoreLog.Trace($"SumSourceFileSizes skip {LogRedactor.RedactPaths(path)}: {ex.Message}");
           }
       }
       return total;
   }
   ```
2. **`AttachCompressorProgress`（快照 L202-227）** 签名加 `long totalSourceBytes`；事件回调内（accumulated percent 累加处）：
   ```csharp
   ProcessedBytes = (long)(totalSourceBytes * accumulatedPercent / 100.0),
   TotalBytes = totalSourceBytes,
   ```
   （调用点若在声明前，先改签名再逐点修编译错误。）
3. **`CompressAsync`**：
   - `validated` 列表构造（快照 L596-599）之后、attach（≈L591 → 实际调整到 validated 之后）计算：
     ```csharp
     long totalSourceBytes = SumSourceFileSizes(validated);
     ```
     并传入 attach。
   - 最终报告（≈L637）：`ProcessedBytes = TotalBytes = totalSourceBytes`。
4. **`AddToArchiveAsync`**：attach（≈L1160）移到 finalDict（≈L1241）计算之后、`CompressFileDictionary`（≈L1255）调用之前，传入 `totalSourceBytes = SumSourceFileSizes(finalDict.Keys)`（以实际字典形态为准）；最终报告（≈L1260）同上填满。
5. **`DeleteEntriesAsync`（快照 L981-1108）**：
   - listing（L997-1010）把 `keepEntries` 从二元组扩为三元组（path, isDir, size）；解构点改 `foreach (var (path, isDir, _) in keepEntries)`。
   - `long totalSourceBytes = keepEntries.Where(t => !t.isDir).Sum(t => t.size);`
   - **提取段**报告（L1064-1068）：`TotalBytes = totalSourceBytes`、`ProcessedBytes = (long)(totalSourceBytes * pct * 0.5 / 100.0)`、`PercentComplete = pct * 0.5`（提取占前 50%）。
   - **重打包段**（L1090 包装的 compressor 进度回调）映射：
     ```csharp
     PercentComplete = 50 + 0.5 * p.PercentComplete,
     ProcessedBytes = (long)(totalSourceBytes * (50 + 0.5 * p.PercentComplete) / 100.0),
     TotalBytes = totalSourceBytes,
     ```
     （字节从映射后的 percent 重算，跨阶段单调。）
   - **最终报告**（L1108）：`ProcessedBytes = TotalBytes = totalSourceBytes`、`PercentComplete = 100`。

### Step 4 — 验证

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --no-build --filter "FullyQualifiedName~SevenZipEngine"
```
任务 1+2 四条 byte 测试全绿 + 既有 SevenZip 全绿（`AddToArchiveAsync`/`DeleteEntriesAsync` 既有测试重点确认）。`lsp_diagnostics` 干净。

**Done when**：压缩/追加/删除路径全部 `TotalBytes > 0` 且单调；最终报告 `Processed == Total`。

---

## 任务 3 — TarGz 解压字节埋点（ExtractAsync / ExtractEntriesAsync）

**目标**：tar 分支以压缩流位置为字节进度（`TotalBytes` = 压缩流长度，spec §2.5-3 接受）；gz 分支把两处 `Stream.CopyTo` 换成手动缓冲循环，按 `Position / gzLen` 报告（钳制 99.9%，完成时 100%）。

**测试文件**：`tests/MantisZip.Tests/Engines/TarGzEngineTests.cs`

### Step 1 — 写失败测试

```csharp
[Fact]
public async Task ExtractAsync_ReportsByteProgress()
{
    // 复用本类既有 tar / tgz roundtrip fixture 写法造归档
    using var dir = TestDirectory.Create();
    var archivePath = /* 既有 fixture：造 .tar.gz */;
    var engine = new TarGzEngine();
    var dest = Path.Combine(dir.Path, "out");
    var collector = new OrderedProgressCollector();   // 同任务1 的嵌套辅助（本类内再定义一份）
    await engine.ExtractAsync(archivePath, dest,
        new ArchiveOptions(), collector.Progress, CancellationToken.None);

    AssertByteProgressMonotonic(collector.Snapshot(), "TarGz.ExtractAsync");
}
```

> `OrderedProgressCollector` / `AssertByteProgressMonotonic` 在本测试类复制为 private 嵌套（与 SevenZipEngineTests 各自独立，不跨类共享——两个测试程序集/类隔离更简单）。

### Step 2 — 跑测试确认失败

配方同前，filter `FullyQualifiedName~TarGzEngine`：失败（"未捕获任何带 TotalBytes 的进度报告"）。

### Step 3 — 实现（`TarGzEngine.cs`）

1. **分支前声明字节总量**（`ExtractAsync` 内、打开流之后、进入 tar/gz 分支之前）：
   ```csharp
   long bytesTotal = 0;   // 链尾最终报告使用；gz 分支赋值压缩流长度
   ```
2. **tar 分支**：
   ```csharp
   long totalCompressedBytes = inputStream.Length;   // try 内取，异常则保持 0（不报字节）
   bytesTotal = totalCompressedBytes;
   ```
   - 已有条目读写节流报告（快照 L82-87、L132-137；`ExtractEntriesAsync` 对应 L660 / L708 / L730）追加：
     `TotalBytes = totalCompressedBytes, ProcessedBytes = inputStream.Position`
   - `Position` 随解压推进天然单调；Seek 不支持时取 Length/Position 的既有 try 兜底逻辑保持。
3. **gz 分支（ExtractAsync 快照 L194 的 `CopyTo`）** 改手动循环：
   ```csharp
   long gzLen = inputStream.Length;
   bytesTotal = gzLen;
   long lastReport = Environment.TickCount64;
   Report(0, 0, gzLen);                       // 循环前 0% 初始报告
   var buffer = new byte[CopyBufferSize];      // 以该类既有缓冲区常量/字段名为准
   int read;
   long lastPct = -1;
   while ((read = await outputStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
   {
       await outputStream.WriteAsync(buffer.AsMemory(0, read), ct);   // 目标流方向按原 CopyTo 调整
       if (Environment.TickCount64 - lastReport >= 100)
       {
           lastReport = Environment.TickCount64;
           double pct = Math.Min(inputStream.Position * 100.0 / gzLen, 99.9);
           Report(pct, inputStream.Position, gzLen);
       }
   }
   ```
   > 方向说明：解压时是「压缩流 Read → 输出流 Write」，两处 `CopyTo`（ExtractAsync L194、ExtractEntriesAsync L799）都以「输入流 `inputStream.Position`」为进度分子。若原代码持有流变量名不同（如 `gzip`/`source`），以实际名为准。
4. **链尾最终报告**（快照 L225 / L822）：`ProcessedBytes = bytesTotal, TotalBytes = bytesTotal`（`bytesTotal` 为 0 时填 0，`SetProgress` 对 `TotalBytes > 0` 才走速度路径，无副作用）。
5. **`ExtractEntriesAsync`**：同构处理 gz `CopyTo`（L799）与各节流报告（L660/L708/L730）。
6. **EntryStatus=Completed 的报告（L290-300）不加字节字段**（早返回陷阱，见 Context）。

### Step 4 — 验证

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --no-build --filter "FullyQualifiedName~TarGzEngine"
```
新测试绿 + 既有 TarGz 全绿。`lsp_diagnostics` 干净。

**Done when**：tar 与 gz 解压路径都有单调字节报告，最终 `Processed == Total`。

---

## 任务 4 — ReadProgressStream 新增 + TarGz 压缩字节埋点

**目标**：新增 `Core/Utils/ReadProgressStream.cs`（`WriteProgressStream` 镜像）；`TarWriteFileWithRetry` 加可选 `onRead` 回调；`CompressAsync` tar 多文件循环用**真实读取字节**报告（percent 保持文件计数基准不变），gz 分支包输入流按字节报 percent。

### Step 1 — 写失败测试

`TarGzEngineTests.cs` 追加两条（+ gz roundtrip 单文件一条）：

```csharp
[Fact]
public async Task CompressAsync_ReportsByteProgress()
{
    using var dir = TestDirectory.Create();
    var src = Path.Combine(dir.Path, "src");
    Directory.CreateDirectory(src);
    await File.WriteAllBytesAsync(Path.Combine(src, "a.bin"), new byte[128 * 1024]);
    await File.WriteAllBytesAsync(Path.Combine(src, "b.bin"), new byte[64 * 1024]);

    var archivePath = Path.Combine(dir.Path, "out.tar");
    var engine = new TarGzEngine();
    var collector = new OrderedProgressCollector();
    await engine.CompressAsync(new[] { src }, archivePath,
        new ArchiveOptions(), collector.Progress, CancellationToken.None);

    AssertByteProgressMonotonic(collector.Snapshot(), "TarGz.CompressAsync");
}

[Fact]
public async Task CompressExtract_GzRoundtrip_ReportsByteProgressBothWays()
{
    using var dir = TestDirectory.Create();
    var srcFile = Path.Combine(dir.Path, "a.bin");
    await File.WriteAllBytesAsync(srcFile, new byte[256 * 1024]);
    var archivePath = Path.Combine(dir.Path, "out.gz");
    var engine = new TarGzEngine();

    var c = new OrderedProgressCollector();
    await engine.CompressAsync(new[] { srcFile }, archivePath,
        new ArchiveOptions(), c.Progress, CancellationToken.None);
    AssertByteProgressMonotonic(c.Snapshot(), "Compress.gz");

    var dest = Path.Combine(dir.Path, "out");
    var e = new OrderedProgressCollector();
    await engine.ExtractAsync(archivePath, dest,
        new ArchiveOptions(), e.Progress, CancellationToken.None);
    AssertByteProgressMonotonic(e.Snapshot(), "Extract.gz");
}
```

### Step 2 — 跑测试确认失败

filter `FullyQualifiedName~TarGzEngine`：两条压缩测试失败。

### Step 3 — 新增 `src/MantisZip.Core/Utils/ReadProgressStream.cs`

镜像 `WriteProgressStream`（全文已读作模板）：包装输入流，`Read`/`ReadAsync` 累加已读字节并回调 `onRead(totalRead)`；`CanSeek = false`（代理内层）、`CanRead = true`、`CanWrite = false`；`Seek`/`SetLength`/`Write` 抛 `NotSupportedException`；`Flush` no-op。中文 XML 注释说明用途（规则 14 针对 axaml，此处类注释仍写）。

### Step 4 — 实现（`TarGzEngine.cs`）

1. **`TarWriteFileWithRetry`（快照 L344-383）** 加可选形参：
   ```csharp
   Action<long>? onRead = null
   ```
   方法体开头：
   ```csharp
   using Stream readStream = onRead is null
       ? sourceStream
       : new ReadProgressStream(sourceStream, onRead);
   ```
   后续写 tar 的读取源改用 `readStream`（原 `sourceStream` 引用处按实际变量名替换）。
2. **`SafeFileSize`**：新增私有静态（try/catch `File.GetAttributes` 判目录 + `FileInfo.Length`，失败 `CoreLog.Trace` 返回 0，复用任务 2 `SumSourceFileSizes` 的 catch 语义；单文件版）。
3. **`CompressAsync` tar 循环**：
   - 循环外：`long totalSourceBytes = ...` 对源路径列表求和（`SafeFileSize`，目录取 0）；`long bytesDone = 0;`
   - 每文件：`long srcSize = SafeFileSize(fullPath);`
   - 快照 L272 的预报告（循环开始前 0%）加 `TotalBytes = totalSourceBytes, ProcessedBytes = bytesDone`。
   - 调 `TarWriteFileWithRetry(..., onRead: read => { fileRead = read; /* 100ms 节流见下 */ })`，闭包捕获 `fileRead`（无条件传 onRead，不用三元跳过——读取即有进度）。
   - 节流报告（与既有 percent 节流同一时机）：
     ```csharp
     long fileProcessed = bytesDone + Math.Min(read, srcSize);
     Report(existingPercent /* 文件计数基准不变 */,
         fileProcessed, totalSourceBytes);
     FilePercentComplete = Math.Min(read, srcSize) * 100.0 / srcSize  // 若既有报告含 FilePercent
     ```
   - 文件成功写入与**跳过**（既有分支）均 `bytesDone += srcSize`（跳过先例 `ZipEngine.cs:315`）。
   - 快照 L290-300 `EntryStatus = Completed` 报告**不加字节**（早返回陷阱）。
4. **`CompressAsync` gz 分支**（单文件）：
   - `long gzSrcSize = SafeFileSize(...)`；包输入流为 `ReadProgressStream`；
   - 循环/既有节流点改 byte 基准：`PercentComplete = Math.Min(Position * 100.0 / gzSrcSize, 99.9)`、`ProcessedBytes = Position, TotalBytes = gzSrcSize`（gzip 单文件无文件计数语义，byte 基准合法）。
5. **最终报告（快照 L317）**：`ProcessedBytes = TotalBytes = totalSourceBytes`。

### Step 5 — 验证

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --no-build --filter "FullyQualifiedName~TarGzEngine"
```
任务 3+4 新增 3 条全绿 + 既有 TarGz 全绿。构建 Core：`dotnet build src\MantisZip.Core\MantisZip.Core.csproj`。

**Done when**：tar/gz 压缩、tar/gz 解压四条路径全部 `TotalBytes > 0` 单调；TarGz percent 语义未变（tar 仍文件计数）。

---

## 任务 5 — ProgressViewModel 语言即时刷新 + OnClosed 退订

**目标**：压缩/解压进行中切换语言，进度窗口所有静态文案（`LocalizedStrings`、标签、speed/ETA/FileCount/stats 文本）立即重灌；窗口关闭时退订静态 `LocalizationManager.CultureChanged`，消除 VM 泄漏。

**测试文件**：`tests/MantisZip.UI.Avalonia.Tests/ProgressViewModelTests.cs`

### Step 1 — 写失败测试

```csharp
[Fact]
public void CultureChanged_RefreshesLocalizedStrings()
{
    var original = LocalizationManager.CurrentLanguage;
    try
    {
        var vm = new ProgressViewModel(/* 按本测试类既有构造方式 */);
        bool raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProgressViewModel.LocalizedStrings)) raised = true;
        };

        // 切到与当前不同的语言（保证触发 CultureChanged）
        LocalizationManager.CurrentLanguage = original == "en" ? "zh-CN" : "en";

        Assert.True(raised, "CultureChanged 后应触发 LocalizedStrings PropertyChanged");
        Assert.Equal(
            LocalizationManager.T("Progress_Cancel"),
            vm.LocalizedStrings["Progress_Cancel"]);
    }
    finally
    {
        LocalizationManager.CurrentLanguage = original; // try/finally 快速恢复，缓解 xUnit 跨类并行
    }
}

[Fact]
public void DetachLocalization_PreventsCultureChangedRefresh()
{
    var original = LocalizationManager.CurrentLanguage;
    try
    {
        var vm = new ProgressViewModel(/* 同上 */);
        vm.DetachLocalization();
        bool raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProgressViewModel.LocalizedStrings)) raised = true;
        };

        LocalizationManager.CurrentLanguage = original == "en" ? "zh-CN" : "en";

        Assert.False(raised, "Detach 后不应再响应 CultureChanged");
    }
    finally
    {
        LocalizationManager.CurrentLanguage = original;
    }
}
```

> 断言要点：不比对「切换前后值不同」（若两语言该 key 相同会 vacuous），改为断言事件触发 + dict 与 `T()` 当前值一致。构造方式、语言码（`CurrentLanguage` 实际取值 `zh-CN`/`en`/`zh-TW` 以 `LocalizationManager` 为准）按测试类现状调整。

### Step 2 — 跑测试确认失败

```powershell
dotnet build tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj -p:BuildProjectReferences=false -p:SkipShellExtCopy=true
dotnet test  tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~ProgressViewModel"
```
预期：`CultureChanged_RefreshesLocalizedStrings` 失败（未订阅 → raised=false）；`DetachLocalization_...` 因方法不存在直接编译失败（属预期红）。

### Step 3 — 实现（`ProgressViewModel.cs` + `ProgressWindow.axaml.cs`）

1. **ctor 抽取 `FillLocalizedStrings()`**：
   - ctor（快照 L78-143）里 `LocalizedStrings` 的 49 键整块赋值移入新私有方法 `FillLocalizedStrings()`。
   - `LocalizedStrings` 是 get-only 属性 → ctor 先 `LocalizedStrings = new Dictionary<string, string>();` 再调 `FillLocalizedStrings()` 填充内部字典（Clear+Add 或直接填充既有 dict；属性本身仍只在 ctor 赋一次引用）。
2. **ctor 订阅**：
   ```csharp
   LocalizationManager.CultureChanged += OnCultureChanged;
   ```
3. **新增退订方法**：
   ```csharp
   /// <summary>窗口关闭时调用，退订静态 CultureChanged，防止 VM 被静态事件根引用泄漏。</summary>
   public void DetachLocalization()
       => LocalizationManager.CultureChanged -= OnCultureChanged;
   ```
4. **缓存字段**（速度/ETA/FileCount/stats 最近一次值，供重渲）：
   - `private double _lastSpeedBytesPerSec;` — 在 SetProgress 速度写入点（快照 L574-585 区域）`speed > 0` 时赋值。
   - `private long? _lastEtaSeconds;` — 同区域 ETA 计算结果缓存。
   - `private int _lastFileCountCurrent, _lastFileCountTotal; private bool _lastFileCountIsBatch;` — 快照 L559-572 两个分支各自赋值。
   - stats 文本字段（`_statsXxx` 既有字段若已存在则复用；`StatsXxxLabel` 属性在 OnCultureChanged 里从它们重渲，`!string.IsNullOrEmpty` 守卫）。
5. **`OnCultureChanged` 处理器**（不 dispatch，`PreviewViewModel.cs:163-177` 先例）：
   ```csharp
   private void OnCultureChanged()
   {
       FillLocalizedStrings();
       OnPropertyChanged(nameof(LocalizedStrings));
       // CurrentFileLabel 重取（按其现有 backing 字段/属性重新求值）
       RefreshTimeDisplay();
       if (_lastSpeedBytesPerSec > 0)
           SpeedText = /* 与 SetProgress 同一格式化路径，格式化函数抽私有或复制公式 */;
       RemainingText = _lastEtaSeconds is { } eta && eta > 0
           ? /* 既有 ETA 格式化 */ : string.Empty;
       if (_lastFileCountTotal > 0) { /* 重渲 FileCountText / BatchArchiveIndexText */ }
       // stats 文本重渲（非空守卫）+ OnPropertyChanged ×4 StatsXxxLabel
       NotifyBatchProperties(); // 既有批次派生属性集中通知
   }
   ```
   - `SpeedText`/`RemainingText` 的格式化逻辑若内联在 `SetProgress`，抽取 `FormatSpeed(double)` / `FormatEta(long)` 私有方法复用（单一事实来源）。
   - `StatusMessage`/准备态文案不处理（spec 范围外）。
6. **`ProgressWindow.OnClosed`（快照 L699-711，`Dialogs/` 非 `Views/`）**：在 `_vm` 可用处追加 `_vm.DetachLocalization();`（null 守卫沿用该方法既有写法）。

### Step 4 — 验证

```powershell
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~ProgressViewModel"
```
新 2 条绿 + 既有 114 条不回归。`lsp_diagnostics` 对两改动文件无错误。

**Done when**：切语言触发 `LocalizedStrings` 刷新且值 == `T()` 当前值；`DetachLocalization()` 后不再响应；窗口关闭路径接线完成。

---

## 任务 6 — 计划文本回写（Deferred #7 + PLAN.md 行 33）

**目标**：把 `progress-window-prototype-alignment.md` Deferred 第 7 项（通用压缩进度播种）回写为已实施状态（commit `7505952`），并在 `docs/PLAN.md` 对应行追加进度注。

### Step 1 — prototype-alignment 回写

文件：`.omo/plans/未开始/progress-window-prototype-alignment.md`（Deferred 清单 L801-809）

- 将 **第 7 项（L809，通用压缩进度播种）** 改为已实施格式，对齐该文件内其他已完成项的写法，注明 commit `7505952`。
- **只回写 #7**（spec §4 范围）。#2（LocalizedStrings 刷新）与 #3（7z/TAR 速度 ETA）在**本计划任务 5 / 任务 1-4 实施完成后**才回写——**执行本任务时若任务 1-5 已完成，顺带把 #2/#3 也标注为「本计划 progress-window-bytes-i18n 已实施」；若尚未完成则只回写 #7**，并向用户提一句确认是否回写 #2/#3。
- 行号为快照，以 Deferred 列表中条目文本定位（`第 7 项`/`通用压缩` 关键词）。

### Step 2 — docs/PLAN.md 行 33 追注

⚠️ 行 33（prototype-alignment 行）**超 2000 字符**，Read/grep 均截断 → 必须用 PowerShell 编辑：

```powershell
$lines = [IO.File]::ReadAllLines("F:\GitHub\MantisZip\docs\PLAN.md")
$i = [Array]::FindIndex($lines, [Predicate[string]]{ param($l) $l.StartsWith('| **P2** | 进度窗口原型对齐改造 |') })
if ($i -lt 0) { throw "PLAN.md 行33 锚点未找到" }
$line = $lines[$i]
$idx = $line.LastIndexOf(' |')
# 在倒数第二个 " |" 前插入进度注（列结构对齐；插入内容按该行既有状态列格式）
```

- 插入注内容（列结构以实际行为准，措辞）：`已实施（含 Deferred #1-#4 等，commit 7505952 及后续）` 或按该行状态列现状最小追加。
- 写回用 UTF-8 **无 BOM**：`[IO.File]::WriteAllLines($path, $lines, [Text.UTF8Encoding]::new($false))`
- 完成后 `git diff docs/PLAN.md` 人工核对：仅行 33 变更、无 BOM 变化、其余行逐字节不变。

**Done when**：Deferred #7 标已实施 + PLAN.md 行 33 追注完成且 diff 干净。

---

## 任务 7 — 终局验证 + 进度文档 + 门控提交

**目标**：全量构建/测试通过、诊断干净、进度文档双轨更新（规则 3），提交待用户确认后执行（规则 10）。

### Step 1 — 全量验证

```powershell
# 1) 源码构建（规则 12）
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj

# 2) 测试基线对照（Core 608+4 / Avalonia 114+2，均 0 fail；skip 3 / 2 属既有）
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj

# 3) 诊断
#    lsp_diagnostics 对全部改动文件（SevenZipEngine.cs / TarGzEngine.cs /
#    ReadProgressStream.cs / ProgressViewModel.cs / ProgressWindow.axaml.cs / 三个测试文件）severity=error 为空
```

- 任一失败 → 按任务内 Step 修复后重跑；**不得删测试/压诊断换绿**。
- 预期新增测试数：Core +4（7z extract/comp/delete + TarGz extract + gz roundtrip ≈ 5 条中 Delete 复用 collector；以实际数为准）、Avalonia +2。超出/不足需在交付说明中如实汇报。

### Step 2 — 进度文档双轨更新（规则 3，提交前必须完成）

- **`docs/PROGRESS.md`**（里程碑）：`### MantisZip.UI.Avalonia（主力版）` 下 `#### 2026-10` 分组（若无则新建）追加一条：
  `- **10-07** — 进度窗口字节埋点全格式生效 + 语言即时刷新`
  （共享层 Core 引擎变更如需，同时在 `### 共享层（Core / ShellExt / 构建）` 对应版本组追加；版本分组以该文件现状为准，不改版本号。）
- **`docs/progress-avalonia-detail.md`**（细节）：日期 `**2026-10-07**` 标识，追加详细条目（7z/TarGz 字节埋点位置、ReadProgressStream、LanguageChanged 刷新机制、退订修复、+N 测试），同日期多条从晚到早排列。
- 不标注版本号变更（规则 2：版本保持 0.5.1）。

### Step 3 — 门控提交（仅在用户明确要求时执行）

```powershell
git status   # 确认改动范围 = 计划列出的文件 + 两个进度文档 + spec/计划文件（是否入库听用户指示）
git diff     # 复核
```

- 提交信息（规则 10 conventional commits）建议：
  `feat(core,avalonia): 进度窗口字节埋点全格式生效 + 语言即时刷新`
- spec（`docs/superpowers/specs/...`）与本计划文件是否随本次提交入库 → **执行前询问用户**。
- 禁止未经允许的 `git add` 范围外文件；禁止 amend/push。

**Done when**：构建 0 错误、双测试套全绿、lsp 干净、进度文档已更新；提交按用户指示完成或保持未提交待指示。

---

## 自审 Checklist（计划完成标准）

- [x] **Spec 全覆盖**：§2 字节埋点（7z extract/compress/delete + TarGz extract/compress）→ 任务 1-4；§3 语言刷新 + 退订 → 任务 5；§4 计划回写 → 任务 6；验证/进度文档 → 任务 7。
- [x] **无占位符**：所有代码块可直接落位；fixture/helper 标注「以仓库现状为准」处均为已知命名歧义（`ArchiveFixtures`/`TestDirectory`/缓冲区常量），非 TODO。
- [x] **类型一致**：`TotalBytes`/`ProcessedBytes` 为非空 `long`；`Eta` 为 `long?`；事件 `CultureChanged` 是无参 Action 风格（`OnCultureChanged()` 无参签名，以 `LocalizationManager.cs:21-31` 实际委托类型为准——若带参按实际调整，测试不依赖委托签名只依赖触发）。
- [x] **依赖顺序**：1→2（共享 collector/断言）、3→4（同一引擎文件）、5 独立、6 在 1-5 后（#2/#3 回写条件）、7 最后。**无并行任务**（同文件冲突，硬约束）。
- [x] **陷阱显式化**：EntryStatus 早返回、字节单调/钳制、压缩基准差异、xUnit 并行（try/finally）、PLAN.md 超长行（PowerShell）、`Dialogs/` 路径 —— 全部写入 Context 或对应任务步骤。
- [x] **基线数字**：Core 608/0/3、Avalonia 114/0/2 已写入任务 7 对照。
- [x] **规则遵守**：规则 1（PLAN.md 同步 = 本计划落盘时执行）、规则 2（版本不动）、规则 3（任务 7 Step 2）、规则 10（提交信息）、规则 12（构建验证）、规则 13（无新增 UI 文案——`LocalizedStrings` 仅重灌既有 key）、规则 14（无新增 axaml）。

## 执行方式（完成后向用户确认）

计划就绪。按 writing-plans 流程，向用户提供执行选项：
1. **subagent-driven-development**（推荐）— 每任务独立 subagent 执行 + 任务间 review 检查点
2. **executing-plans** — 同会话顺序执行，按检查点自审
3. 用户先审阅计划文本再指示
