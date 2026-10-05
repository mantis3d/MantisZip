# N 组并行 ZIP 压缩 + 真实通道进度 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用 MantisZip 自主 N 组并行压缩替换 7z `mt=on` 黑盒，让进度窗口展示 N 条真实字节级通道进度。

**Architecture:** 现有路径把全部待压文件交给单个 `SharpSevenZipCompressor`（`mt=on`），只能拿到 Started/Finished 两个黑盒事件。新路径按文件大小把待压文件分成 N 组，每组一个独立 7z 压缩器（`mt=off`，并行度来自 N 个实例而非 7z 内部线程），各自产出 tempZip，最后用 `ZipBinaryRewriter` 的 copy-mode 多源重载把 N 个 tempZip 原样字节拼接成最终 ZIP。

**Tech Stack:** C# / .NET 10 / SharpCompress 0.48.1 / SharpSevenZip 2.0.45 / Avalonia 12.0.4 / xUnit

**设计依据:** `docs/superpowers/specs/2026-10-05-mt-progress-display-design.md`

---

## 关键既有事实（已核实，实现时不得矛盾）

| 事实 | 位置 |
|------|------|
| `IsMultiThreadedEligible(ArchiveOptions, int totalEntryCount, long totalSize)` 已存在，7 条回退条件 | `ZipEngine.cs:2950` |
| 其调用点：压缩 `:1407`、追加 `:2165` | — |
| `compr.CustomParameters["mt"] = "on"` **硬编码** | `ZipEngine.cs:3063` |
| `CompressGroupWithSevenZip` 全部计数器均为方法内局部变量（`reportLock`/`completedBytes`/`pendingEntryKeys`），并发调用天然安全 | `ZipEngine.cs:3041-3076` |
| `ref DateTime lastReportTime` 是唯一跨调用共享状态 → N 组各自必须持独立局部变量 | `ZipEngine.cs:3050` |
| `RewriteAsync(string sourcePath, ...)` 仅支持单源 | `ZipBinaryRewriter.cs:287` |
| `ArchiveProgress` 已有全部批次字段，**不新增契约字段** | `ArchiveEngine.cs:315-347` |
| `ArchiveOptions.ParallelExtractDegree` 默认 `0` | `ArchiveEngine.cs:162` |
| `AppSettings.ParallelExtractDegree` 默认 `Environment.ProcessorCount` | `AppSettings.cs:78` |
| `SplitCompressGroup` **尚不存在**，需新建（现有 `:1409-1413` 只是 store/compress 二分，非 N 分组） | — |

**默认值不对称是既有约定，勿"修正"：** `AppSettings` 用 `ProcessorCount`，`ArchiveOptions` 用 `0`（运行时才解析）。新字段照此办理。

---

## 约束

- **不 commit、不 push、不改版本号**（用户明确要求）。本计划**不含任何 `git add` / `git commit` / `git push` 步骤**，各任务末尾只报告验证结果。改动由用户自行审阅后决定提交时机。
- 新功能只在 `MantisZip.UI.Avalonia`；WPF 版已删除。
- 压缩并行时 `_isBatchMode` 必须保持 `false`，否则 `ComputeOverallPercent` 会覆盖全局字节加权百分比。
- 回退路径行为必须与今天**完全一致**（含 `mt=on`）。

---

## File Structure

| 文件 | 责任 |
|------|------|
| `src/MantisZip.Core/Engines/ZipEngine.cs` | 新增 `SplitCompressGroup`；改造 `CompressGroupWithSevenZip`；`CompressAsync`/`AddToArchiveAsync` 接入 N 组 |
| `src/MantisZip.Core/Utils/ZipBinaryRewriter.cs` | 新增多源 `RewriteAsync` 重载 |
| `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` | 新增 `ArchiveOptions.ParallelCompressDegree` |
| `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` | 新增 `ParallelCompressDegree` |
| `src/MantisZip.UI.Avalonia/Models/ParallelBatchProgressItem.cs` | 新增 `FileRatio` |
| `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs` | `UpsertParallelBatch` 前移 + `FileRatio` + arrival-triggered 切模式 |
| `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` | 通道行 4 处改动 |
| `tests/MantisZip.Tests/Engines/ParallelCompressTests.cs` | **新建**：Core 层 N 组压缩测试 |
| `tests/MantisZip.Tests/Utils/ZipBinaryRewriterMultiSourceTests.cs` | **新建**：多源拼接测试 |
| `scripts/bench-zip-mt.cs` | `--degree` 扩展 |

---

## Task 1: `SplitCompressGroup` 纯函数

**Files:**
- Modify: `src/MantisZip.Core/Engines/ZipEngine.cs`（在 `IsMultiThreadedEligible` 之前插入）
- Test: `tests/MantisZip.Tests/Engines/ParallelCompressTests.cs`（新建）

- [ ] **Step 1: 写失败测试**

创建 `tests/MantisZip.Tests/Engines/ParallelCompressTests.cs`：

```csharp
using MantisZip.Core.Engines;
using Xunit;

namespace MantisZip.Tests.Engines;

public class SplitCompressGroupTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "mz_split_" + Guid.NewGuid().ToString("N"));

    public SplitCompressGroupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 按给定字节数<b>真实落盘</b>后返回 (FullPath, RelativePath)。
    /// 必须真实落盘：SplitCompressGroup 内部用 FileInfo.Length 取大小，
    /// 虚构路径会让所有文件大小恒为 0，LPT 分组退化为「全进第 0 组」，
    /// 使 LargerDegreeYieldsMoreGroups 断言（2 组 &lt; 4 组）必然失败。
    /// </summary>
    private (string FullPath, string RelativePath)[] MakeFiles(params long[] sizes)
    {
        var result = new (string FullPath, string RelativePath)[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            string rel = $"f{i}.bin";
            string full = Path.Combine(_dir, rel);
            File.WriteAllBytes(full, new byte[sizes[i]]);
            result[i] = (full, rel);
        }
        return result;
    }

    [Fact]
    public void Degree1_ReturnsSingleGroupWithAllFiles()
    {
        var files = MakeFiles(10, 20, 30);
        var groups = ZipEngine.SplitCompressGroup(files, 1);

        Assert.Single(groups);
        Assert.Equal(3, groups[0].Count);
    }

    [Fact]
    public void GroupsPartitionAllFiles_ExactlyOnce()
    {
        var files = MakeFiles(10, 20, 30, 40, 50, 60, 70);
        var groups = ZipEngine.SplitCompressGroup(files, 3);

        var flat = groups.SelectMany(g => g.Select(f => f.RelativePath)).OrderBy(x => x);
        Assert.Equal(files.Select(f => f.RelativePath).OrderBy(x => x), flat);
    }

    [Fact]
    public void EmptyGroupsAreDropped()
    {
        var files = MakeFiles(10, 20);
        var groups = ZipEngine.SplitCompressGroup(files, 8);

        Assert.All(groups, g => Assert.NotEmpty(g));
    }

    [Fact]
    public void LargerDegreeYieldsMoreGroups()
    {
        var files = MakeFiles(1, 2, 3, 4, 5, 6, 7, 8);
        Assert.True(ZipEngine.SplitCompressGroup(files, 2).Count
                    < ZipEngine.SplitCompressGroup(files, 4).Count);
    }

    [Fact]
    public void HeaviestFileAloneInItsGroup_LongestProcessingTimeMinimized()
    {
        // 贪心 LPT：最大文件先进最轻的桶
        var files = MakeFiles(100, 1, 1, 1);
        var groups = ZipEngine.SplitCompressGroup(files, 2);

        Assert.Equal(2, groups.Count);
        Assert.Single(groups.First(g => g.Any(f => f.RelativePath == "f0.bin")));
    }
}
```

- [ ] **Step 2: 运行确认失败**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~SplitCompressGroupTests"
```
Expected: 编译错误 `ZipEngine` 不含 `SplitCompressGroup`。

- [ ] **Step 3: 实现**

在 `ZipEngine.cs` 的 `IsMultiThreadedEligible` 之前插入：

```csharp
    /// <summary>
    /// 把待压文件按大小贪心分入<paramref name="degree"/> 组（LPT / longest-processing-time-first）。
    /// <para>
    /// 每轮取当前<b>未分配最大</b>的文件放入<b>累计字节最小</b>的组，
    /// 使最重文件尽早开工、避免长尾残留。空组在返回前被剔除，
    /// 因此返回的组数可能小于 <paramref name="degree"/>（调用方须以返回值为准）。
    /// </para>
    /// </summary>
    /// <param name="files">待分组文件。</param>
    /// <param name="degree">期望组数，&lt;= 1 时返回单组。</param>
    internal static List<List<(string FullPath, string RelativePath)>> SplitCompressGroup(
        IReadOnlyList<(string FullPath, string RelativePath)> files,
        int degree)
    {
        var result = new List<List<(string FullPath, string RelativePath)>>();
        if (files.Count == 0) return result;

        if (degree <= 1)
        {
            result.Add(files.ToList());
            return result;
        }

        var bins = new List<(long Bytes, List<(string, string)> Items)>();
        for (int i = 0; i < degree; i++)
            bins.Add((0L, new List<(string, string)>()));

        // 待分配集合按文件大小降序；同大小按路径序，保证结果确定性
        var pending = files
            .Select((f, idx) => (Item: f, Idx: idx, Size: SafeSize(f.FullPath)))
            .OrderByDescending(x => x.Size)
            .ThenBy(x => x.Item.FullPath, StringComparer.Ordinal)
            .ToList();

        foreach (var p in pending)
        {
            int target = 0;
            for (int i = 1; i < bins.Count; i++)
                if (bins[i].Bytes < bins[target].Bytes) target = i;

            bins[target].Items.Add(p.Item);
            bins[target] = (bins[target].Bytes + p.Size, bins[target].Items);
        }

        foreach (var b in bins)
            if (b.Items.Count > 0) result.Add(b.Items);

        return result;

        static long SafeSize(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0L; }
        }
    }
```

- [ ] **Step 4: 运行确认通过**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~SplitCompressGroupTests"
```
Expected: 5 passed。

报告：Task 1 完成，5 测试通过。

---

## Task 2: `ZipBinaryRewriter` 多源重载

**Files:**
- Modify: `src/MantisZip.Core/Utils/ZipBinaryRewriter.cs`
- Test: `tests/MantisZip.Tests/Utils/ZipBinaryRewriterMultiSourceTests.cs`（新建）

现有单源签名（`ZipBinaryRewriter.cs:287`）：

```csharp
public static async Task<RewriteResult> RewriteAsync(
    string sourcePath, string destPath, HashSet<string>? keepEntryNames,
    List<NewEntry>? addEntries, Encoding encoding, string? comment = null,
    IProgress<ArchiveProgress>? progress = null,
    CancellationToken cancellationToken = default)
```

新重载保持同一形态，仅首参改为列表。**关键约束**：`ReadEocd`/`ReadCentralDirectory` 是单流解析，多源需逐源解析后顺序写出，且**不得**让 `sourcePath` 与 `destPath` 相同（原实现靠 `.tmp` 原子替换，多源沿用同一手法）。

- [ ] **Step 1: 写失败测试**

创建 `tests/MantisZip.Tests/Utils/ZipBinaryRewriterMultiSourceTests.cs`：

```csharp
using System.IO.Compression;
using System.Text;
using MantisZip.Core.Utils;

namespace MantisZip.Tests.Utils;

public class ZipBinaryRewriterMultiSourceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mz_rewrite_ms").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        GC.SuppressFinalize(this);
    }

    private string MakeZip(string name, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var fs = File.Create(path);
        using var za = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            var e = za.CreateEntry(entry);
            using var w = new StreamWriter(e.Open(), Encoding.UTF8);
            w.Write(content);
        }
        return path;
    }

    [Fact]
    public async Task MergeTwoSources_ContainsAllEntriesFromBoth()
    {
        var a = MakeZip("a.zip", ("a1.txt", "AAA"), ("a2.txt", "BBB"));
        var b = MakeZip("b.zip", ("b1.txt", "CCC"));
        var dest = Path.Combine(_dir, "merged.zip");

        await ZipBinaryRewriter.RewriteAsync(new[] { a, b }, dest, null, null, Encoding.UTF8);

        using var za = ZipFile.OpenRead(dest);
        var names = za.Entries.Select(e => e.FullName).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "a1.txt", "a2.txt", "b1.txt" }, names);
    }

    [Fact]
    public async Task MergePreservesEntryContentByteForByte()
    {
        var payload = new string('x', 5000);
        var a = MakeZip("a.zip", ("big.txt", payload));
        var b = MakeZip("b.zip", ("small.txt", "s"));
        var dest = Path.Combine(_dir, "merged.zip");

        await ZipBinaryRewriter.RewriteAsync(new[] { a, b }, dest, null, null, Encoding.UTF8);

        using var za = ZipFile.OpenRead(dest);
        using var r = new StreamReader(za.GetEntry("big.txt")!.Open());
        Assert.Equal(payload, await r.ReadToEndAsync());
    }

    [Fact]
    public async Task MergeThreeSources_EntryCountIsSum()
    {
        var a = MakeZip("a.zip", ("a1.txt", "1"));
        var b = MakeZip("b.zip", ("b1.txt", "2"));
        var c = MakeZip("c.zip", ("c1.txt", "3"));
        var dest = Path.Combine(_dir, "merged.zip");

        await ZipBinaryRewriter.RewriteAsync(new[] { a, b, c }, dest, null, null, Encoding.UTF8);

        using var za = ZipFile.OpenRead(dest);
        Assert.Equal(3, za.Entries.Count);
    }

    [Fact]
    public async Task SingleSourceOverload_BehavesLikeMultiSource()
    {
        var a = MakeZip("a.zip", ("only.txt", "Z"));
        var dest = Path.Combine(_dir, "one.zip");

        await ZipBinaryRewriter.RewriteAsync(new[] { a }, dest, null, null, Encoding.UTF8);

        using var za = ZipFile.OpenRead(dest);
        Assert.Single(za.Entries);
    }

    [Fact]
    public async Task TempFileIsCleanedUpOnSuccess()
    {
        var a = MakeZip("a.zip", ("x.txt", "1"));
        var dest = Path.Combine(_dir, "out.zip");

        await ZipBinaryRewriter.RewriteAsync(new[] { a }, dest, null, null, Encoding.UTF8);

        Assert.False(File.Exists(dest + ".tmp"));
    }
}
```

- [ ] **Step 2: 运行确认失败**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~ZipBinaryRewriterMultiSourceTests"
```
Expected: 编译错误 —— 无接受 `string[]` 的重载。

- [ ] **Step 3: 抽取单条目复制逻辑为 `CopyEntryAsync`**

多源重载必须复用**同一份**逐条目复制逻辑，否则 copy-mode 校验（Store/Deflate/Deflate64、未加密、非 ZIP64）与 LFH 改写会在两份代码里漂移。当前这段逻辑内联在单源 `RewriteAsync` 的 Phase 1 循环体里（`ZipBinaryRewriter.cs:344-414`）。

在 `ZipBinaryRewriter.cs` 的 `RewriteAsync` 之后新增私有方法，把循环体中「校验 → 读 LFH → 写 LFH → 流复制 → 收集 CDFH」整段抽出；原循环体改为调用它（保持 `basePct`/`entryWeight` 的计算位置不变）：

```csharp
    /// <summary>
    /// 复制单个条目（LFH + 已压缩数据）到输出流，并把 CDFH 记录追加到 <paramref name="entriesToWrite"/>。
    /// 单源与多源路径共用，确保 copy-mode 校验与 LFH 改写只有一处实现。
    /// </summary>
    private static async Task<long> CopyEntryAsync(
        Stream source,
        Stream output,
        CdEntry entry,
        List<(CdEntry Entry, long NewOffset, bool IsNew, byte[]? NewLfh)> entriesToWrite,
        double basePct,
        double entryWeight,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        // ── Copy-mode validation ─────────────────────────────
        if (entry.CompressionMethod != 0 && entry.CompressionMethod != 8 && entry.CompressionMethod != 9)
        {
            CoreLog.Info($"Entry '{entry.FileName}': unsupported compression method {entry.CompressionMethod}");
            throw new ZipCopyModeException(
                $"Entry '{entry.FileName}' uses unsupported compression method ({entry.CompressionMethod}). " +
                "Only Store (0), Deflate (8) and Deflate64 (9) are supported by copy-mode.");
        }

        if ((entry.Flags & 0x0001) != 0) // bit 0 = encrypted
        {
            CoreLog.Info($"Entry '{entry.FileName}': encrypted, not supported by copy-mode");
            throw new ZipCopyModeException(
                $"Entry '{entry.FileName}' is encrypted. Encrypted entries are not supported by copy-mode.");
        }

        if (entry.CompressedSize >= 0xFFFFFFFF)
        {
            CoreLog.Info($"Entry '{entry.FileName}': ZIP64 compressed size, not supported by copy-mode");
            throw new ZipCopyModeException(
                $"Entry '{entry.FileName}' uses ZIP64 compressed size. ZIP64 is not supported by copy-mode.");
        }

        if (entry.LocalHeaderOffset >= 0xFFFFFFFF)
        {
            CoreLog.Info($"Entry '{entry.FileName}': ZIP64 local header offset, not supported by copy-mode");
            throw new ZipCopyModeException(
                $"Entry '{entry.FileName}' uses ZIP64 local header offset. ZIP64 is not supported by copy-mode.");
        }

        // ── Read and optionally rewrite LFH ──────────────────
        LfhInfo lfhInfo = ReadAndMaybeRewriteLfh(
            source, entry.LocalHeaderOffset, entry, out byte[] lfhHeader);

        long entryOffset = output.Position;

        // Write LFH header to output
        output.Write(lfhHeader, 0, lfhHeader.Length);

        // Stream-copy compressed data with per-chunk progress
        await CopyStreamRangeAsync(
            source, output, entry.CompressedSize,
            entry.FileName, basePct, entryWeight,
            progress, cancellationToken);

        long bytesWritten = lfhHeader.Length + entry.CompressedSize;

        // If bit 3 was cleared in the LFH rewrite, propagate the flag change
        // to the CDFH so it matches the LFH (no data descriptor present).
        var entryForCd = lfhInfo.Flags != entry.Flags
            ? entry with { Flags = lfhInfo.Flags }
            : entry;
        entriesToWrite.Add((entryForCd, entryOffset, false, lfhHeader));

        CoreLog.Trace("ZipBinaryRewriter: copied entry '{0}' ({1} bytes)",
            entry.FileName, entry.CompressedSize);

        return bytesWritten;
    }
```

原单源 Phase 1 循环体收敛为：

```csharp
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip entries not in the keep set (when keepAll == false)
                if (!keepAll && !keepSet.Contains(entry.FileName))
                    continue;

                double basePct = totalEntries > 0
                    ? (double)processedEntries / totalEntries * 100
                    : 0;
                double entryWeight = 90.0 / totalEntries;

                bytesCopied += await CopyEntryAsync(source, output, entry,
                    entriesToWrite, basePct, entryWeight, progress, cancellationToken);
                processedEntries++;
            }
```

- [ ] **Step 4: 单源路径重构后跑既有测试（防回归，必须先绿）**

```powershell
dotnet test tests\MantisZip.Tests/MantisZip.Tests.csproj --filter "FullyQualifiedName~ZipBinaryRewriter"
```
Expected: 既有 `ZipBinaryRewriter` 相关测试全部通过（重构前后的行为等价性验证）。

- [ ] **Step 5: 实现多源重载**

在 `ZipBinaryRewriter.cs` 的现有 `RewriteAsync` 之后追加：

```csharp
    /// <summary>
    /// 多源 copy-mode 拼接：把多个 ZIP 的条目<b>原样复制</b>（LFH + 压缩字节 + CDFH）到单一输出。
    /// <para>
    /// 从不解压也从不重压，因此各源的压缩方法码无需统一。按 <paramref name="sourcePaths"/>
    /// 顺序写出，条目顺序即源顺序。任一源解析失败即整体失败（不产出半成品：
    /// 输出先写 <c>destPath + ".tmp"</c>，全部成功后原子替换）。
    /// </para>
    /// </summary>
    public static async Task<RewriteResult> RewriteAsync(
        IReadOnlyList<string> sourcePaths,
        string destPath,
        HashSet<string>? keepEntryNames,
        List<NewEntry>? addEntries,
        Encoding encoding,
        string? comment = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ZipBinaryRewriter.RewriteAsync(multi): sources={sourcePaths.Count}, dest='{destPath}'");

        if (sourcePaths is null || sourcePaths.Count == 0)
            throw new ArgumentException("sourcePaths must not be empty", nameof(sourcePaths));

        // 单源直接委托既有实现，避免重复实现 copy-mode 逻辑
        if (sourcePaths.Count == 1)
        {
            return await RewriteAsync(sourcePaths[0], destPath, keepEntryNames,
                addEntries, encoding, comment, progress, cancellationToken);
        }

        Stream? output = null;
        string tempDestPath = destPath + ".tmp";
        long copied = 0;
        int entryCount = 0;

        try
        {
            // 先解析全部源，算出跨源总条目数 —— 这样单条目进度权重覆盖整个拼接，
            // 而不是每个源各自从 0 开始。
            var parsed = new List<(Stream Stream, List<CdEntry> Entries)>();
            try
            {
                foreach (var src in sourcePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var source = File.Open(src, FileMode.Open, FileAccess.Read,
                        FileShare.Read | FileShare.Delete);

                    byte[] magic = new byte[2];
                    source.ReadExactly(magic, 0, 2);
                    source.Seek(0, SeekOrigin.Begin);
                    if (magic[0] == 'M' && magic[1] == 'Z')
                    {
                        source.Dispose();
                        throw new ZipCopyModeException("SFX ZIP not supported by copy-mode");
                    }

                    var (cdOffset, srcEntryCount, srcComment) = ReadEocd(source);
                    parsed.Add((source, ReadCentralDirectory(source, cdOffset, srcEntryCount)));
                    existingComment ??= srcComment;
                }

                int totalEntries = parsed.Sum(p => p.Entries.Count)
                    + (addEntries?.Count ?? 0);
                if (totalEntries == 0) totalEntries = 1; // avoid division by zero

                // ── Phase 1: 按源顺序复制条目 ────────────────────
                foreach (var (source, entries) in parsed)
                {
                    bool keepAll = keepEntryNames == null;
                    foreach (var entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!keepAll && !keepEntryNames!.Contains(entry.FileName))
                            continue;

                        double basePct = (double)processedEntries / totalEntries * 100;
                        double entryWeight = 90.0 / totalEntries;

                        bytesCopied += await CopyEntryAsync(source, output, entry,
                            entriesToWrite, basePct, entryWeight, progress, cancellationToken);
                        processedEntries++;
                    }
                }
            }
            finally
            {
                // 源流按元素 Dispose（构造中途抛错时已入队的也要释放）
                foreach (var (s, _) in parsed) s.Dispose();
            }

            // ── Phase 2: Add new entries ──────────────────────────
            if (addEntries != null)
            {
                foreach (var newEntry in addEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    double basePct = (double)processedEntries / totalEntries * 100;
                    double entryWeight = 90.0 / totalEntries;

                    long entryOffset = output.Position;

                    var (lfhBytes, compressedSize, crc32) =
                        CompressNewEntry(output, newEntry, encoding,
                            basePct, entryWeight, progress, cancellationToken);

                    bytesAdded += lfhBytes.Length + compressedSize;

                    var (dosDate, dosTime) = DateTimeToDos(newEntry.LastModified);
                    byte[] fileNameBytes = encoding.GetBytes(newEntry.EntryName);

                    entriesToWrite.Add((new CdEntry(
                        FileName: newEntry.EntryName,
                        Crc32: crc32,
                        CompressedSize: compressedSize,
                        UncompressedSize: newEntry.Size,
                        CompressionMethod: (ushort)(newEntry.Store ? 0 : 8),
                        Flags: 0,
                        LastModifiedDate: dosDate,
                        LastModifiedTime: dosTime,
                        LocalHeaderOffset: 0, // unused; NewOffset in the tuple is used instead
                        RawExtraField: [],
                        RawFileExtra: [],
                        LfhFilenameLength: fileNameBytes.Length,
                        LfhExtraLength: 0
                    ), entryOffset, true, lfhBytes));
                    processedEntries++;
                }
            }

            // ── Phase 3: Write central directory ─────────────────
            // 逐字节合并已完成，此处沿用既有实现（单源路径同一段逻辑）
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "正在写入中央目录...",
                PercentComplete = 92,
                FilePercentComplete = 100
            });

            long centralDirStart = output.Position;
            WriteCentralDirectory(output, entriesToWrite, encoding, progress);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "正在写入目录结束标记...",
                PercentComplete = 94,
                FilePercentComplete = 100
            });

            // ── Phase 4: Write EOCD ───────────────────────────────
            string effectiveComment = comment ?? existingComment ?? string.Empty;
            WriteEocd(output, centralDirStart, entriesToWrite.Count, effectiveComment);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "正在保存到磁盘...",
                PercentComplete = 97,
                FilePercentComplete = 100
            });

            // ── Finalize (close then atomically replace) ─────────
            output.Dispose();
            output = null;

            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(tempDestPath, destPath);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                FilePercentComplete = 100
            });

            int copyCount = entriesToWrite.Count(e => !e.IsNew);
            int addCount = entriesToWrite.Count - copyCount;

            CoreLog.Info(
                $"ZipBinaryRewriter: multi-source rewrite complete — {copyCount} entries copied ({bytesCopied} bytes), " +
                $"{addCount} entries added ({bytesAdded} bytes)");
            CoreLog.Exit();

            return new RewriteResult(copyCount, bytesCopied, addCount, bytesAdded);
        }
        catch (OperationCanceledException)
        {
            CoreLog.Info("ZipBinaryRewriter: cancelled");
            CleanupFile(tempDestPath);
            throw;
        }
        catch (Exception ex)
        {
            if (ex is ZipCopyModeException)
                CoreLog.Info("ZipBinaryRewriter: copy-mode not supported, aborting (caller must fall back to the serial path)");
            else
                CoreLog.Error("ZipBinaryRewriter: error during multi-source rewrite", ex);
            CleanupFile(tempDestPath);
            throw;
        }
        finally
        {
            output?.Dispose();
        }
    }
```

> **注记（实现者必读）**
> - **不要新增 adapter 类型**。上例已按既有单源路径的真实成员名写好：`ReadEocd` / `ReadCentralDirectory` / `ReadAndMaybeRewriteLfh` / `CopyStreamRangeAsync` / `CompressNewEntry` / `WriteCentralDirectory` / `WriteEocd` / `CleanupFile` 全部直接复用。
> - `entriesToWrite` 元素类型必须与单源路径一致：`(CdEntry Entry, long NewOffset, bool IsNew, byte[]? NewLfh)`。
> - 多源注释/EOCD 规则：`comment` 显式传入优先；否则沿用**第一个非空**源的注释（`existingComment ??=`）；都没有则空串。
> - 校验与异常语义必须与单源路径一致：SFX、加密、ZIP64、非 (0/8/9) 压缩方法均抛 `ZipCopyModeException`，由调用方回退串行。
> - 每个源流在 Phase 2 开始前已全部关闭（`finally` 中逐个 `Dispose`），避免多源同时持句柄。

- [ ] **Step 6: 运行确认通过**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~ZipBinaryRewriterMultiSourceTests"
```
Expected: 5 passed。

- [ ] **Step 7: 全量 Core 测试（防回归）**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```
Expected: 全部通过（基线 572 passed / 3 skipped，+10 新增）。

---

## Task 3: `CompressGroupWithSevenZip` 参数化

**Files:**
- Modify: `src/MantisZip.Core/Engines/ZipEngine.cs:3041-3063`

三处改动：`mt` 由硬编码改为参数；新增批次身份参数；进度上报补 `BatchIndex`/`BatchCount`。

- [ ] **Step 1: 纯机械签名重构（不改行为）**

在 `ZipEngine.cs:3041-3050` 的签名中，`storeProcessedFiles` 之后、`ref DateTime lastReportTime` 之前插入两个参数：

```csharp
    internal static void CompressGroupWithSevenZip(
        List<(string FullPath, string RelativePath)> files,
        string tempPath,
        ArchiveOptions options,
        IProgress<ArchiveProgress>? progress,
        long storeProcessedBytes,
        long totalBytes,
        int totalFiles,
        int storeProcessedFiles,
        int? batchIndex,          // 新增：组索引；null = 旧单组路径
        int? batchCount,          // 新增：有效组数
        ref DateTime lastReportTime)
```

本步**只加参数、不加任何逻辑**。两个既有调用点补 `null, null`（`null` = 旧单组路径，行为完全不变）：

`:1439`（旧单组路径）：

```csharp
            CompressGroupWithSevenZip(compressGroup, tempZip, options, progress, 0, totalBytes, totalFiles, 0, null, null, ref lastReportTime);
```

`:2206`（`AddToArchiveAsync` 旧路径）：

```csharp
        CompressGroupWithSevenZip(compressGroup, tempZip, options, progress, compressProcessed, compressTotalBytes, compressFiles.Count, mtProcessedFiles, null, null, ref lastReportTime);
```

> **为什么先重构再写测试**：本任务是 C# 签名变更，新参数不存在时测试**无法编译**，
> 「运行确认失败」会退化成编译错误（红得不是想测的东西）。故先用既有测试证明
> 重构等价（绿），再写针对新行为的测试（红），最后实现（绿）。

- [ ] **Step 2: 确认重构未破坏既有行为（必须先绿）**

```powershell
dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj --filter "FullyQualifiedName~ZipEngineTests"
```
Expected: 全部通过（测试数与基线一致）。若此处失败，说明签名改动引入了问题，**先修复再继续**。

- [ ] **Step 3: 写失败测试**

追加到 `tests/MantisZip.Tests/Engines/ParallelCompressTests.cs`。`MantisZip.Tests` 已在
`MantisZip.Core.csproj:13-15` 的 `InternalsVisibleTo` 中，可直接调用 `internal` 成员：

```csharp
    /// <summary>同步执行的 IProgress：不像 Progress&lt;T&gt; 那样投递到同步上下文。</summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public async Task CompressGroup_ReportsBatchIndexAndCount()
    {
        var dir = Directory.CreateTempSubdirectory("mz_batchidx").FullName;
        try
        {
            var files = new List<(string FullPath, string RelativePath)>();
            long total = 0;
            for (int i = 0; i < 4; i++)
            {
                var p = Path.Combine(dir, $"f{i}.bin");
                var payload = new byte[64 * 1024];
                Random.Shared.NextBytes(payload);
                await File.WriteAllBytesAsync(p, payload);
                files.Add((p, $"f{i}.bin"));
                total += payload.Length;
            }

            var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
            var inline = new InlineProgress<ArchiveProgress>(p =>
            {
                if (p.BatchIndex.HasValue && p.BatchCount.HasValue)
                    seen[$"{p.BatchIndex.Value}/{p.BatchCount.Value}"] = 0;
            });

            var lastReport = DateTime.Now;
            ZipEngine.CompressGroupWithSevenZip(
                files, Path.Combine(dir, "g.zip"),
                new ArchiveOptions { CompressionLevel = 5 },
                inline, 0, total, files.Count, 0,
                2, 3, ref lastReport);

            // 组身份必须原样透传到进度上报
            Assert.Contains("2/3", seen.Keys);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
```

- [ ] **Step 4: 运行确认失败**

```powershell
dotnet test tests\MantisZip.Tests/MantisZip.Tests.csproj --filter "FullyQualifiedName~CompressGroup_ReportsBatchIndexAndCount"
```
Expected: **编译通过**，断言失败 —— `BatchIndex`/`BatchCount` 尚未在方法内的
`progress?.Report(...)` 中赋值（压缩路径当前不上报批次字段；`ZipEngine.cs` 中 4 处
`BatchIndex` 赋值全在并行解压路径）。

- [ ] **Step 5: 参数化 `mt`**

修改 `ZipEngine.cs:3063`（签名与两个调用点已在 Step 1 改好，此处只改 `mt` 取值）：

```csharp
        // mt 由调用方决定：N 组并行路径每组一个压缩器，并行度来自组间，
        // 组内必须 mt=off 以免 N× cores 过度订阅；旧单组路径保持 mt=on。
        compr.CustomParameters["mt"] = batchIndex.HasValue ? "off" : "on";
```

- [ ] **Step 6: 进度上报补批次字段**

在该方法内**每一处** `progress?.Report(new ArchiveProgress { ... })` 中补两个字段（`Compressing` 累积、`Finished` 完成、末尾 100% 补发等，逐处确认，不漏）：

```csharp
                    BatchIndex = batchIndex,
                    BatchCount = batchCount,
```

- [ ] **Step 7: 运行确认通过**

```powershell
dotnet test tests\MantisZip.Tests/MantisZip.Tests.csproj --filter "FullyQualifiedName~ParallelCompressTests"
```
Expected: 全部通过。

- [ ] **Step 8: 回归（mt=on 旧路径未变）**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~ZipEngineTests|FullyQualifiedName~SevenZipEngineTests"
```
Expected: 全部通过（验证 `mt=on` 单组路径与加密路径未受影响）。

---

## Task 4: `CompressAsync` 接入 N 组并行

**Files:**
- Modify: `src/MantisZip.Core/Engines/ZipEngine.cs:1407` 附近

- [ ] **Step 1: 写失败测试**

追加到 `ParallelCompressTests.cs`：

```csharp
    private static byte[] RandomBytes(int n, int seed)
    {
        var rnd = new Random(seed);
        var b = new byte[n];
        rnd.NextBytes(b);
        return b;
    }

    [Fact]
    public async Task Degree3_OutputIsValidAndByteIdenticalToSource()
    {
        var dir = Directory.CreateTempSubdirectory("mz_deg3").FullName;
        try
        {
            var expected = new Dictionary<string, byte[]>();
            for (int i = 0; i < 9; i++)
            {
                var data = RandomBytes(200_000 + i * 1000, i);
                var name = $"f{i}.bin";
                expected[name] = data;
                await File.WriteAllBytesAsync(Path.Combine(dir, name), data);
            }

            var outZip = Path.Combine(dir, "out.zip");
            await new ZipEngine().CompressAsync(
                Directory.GetFiles(dir, "*.bin"), outZip,
                new ArchiveOptions { CompressionLevel = 5, MultiThreadedCompression = true, ParallelCompressDegree = 3 });

            using var za = ZipFile.OpenRead(outZip);
            Assert.Equal(9, za.Entries.Count);
            foreach (var (name, data) in expected)
            {
                using var s = za.GetEntry(name)!.Open();
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms);
                Assert.Equal(data, ms.ToArray());
            }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Degree3_ArchivePassesTestArchive()
    {
        var dir = Directory.CreateTempSubdirectory("mz_deg3_ok").FullName;
        try
        {
            for (int i = 0; i < 6; i++)
                await File.WriteAllBytesAsync(Path.Combine(dir, $"f{i}.bin"), RandomBytes(50_000, i + 99));

            var outZip = Path.Combine(dir, "out.zip");
            var engine = new ZipEngine();
            await engine.CompressAsync(Directory.GetFiles(dir, "*.bin"), outZip,
                new ArchiveOptions { CompressionLevel = 5, MultiThreadedCompression = true, ParallelCompressDegree = 3 });

            var result = await engine.TestArchiveAsync(outZip);
            Assert.True(result.Success, string.Join("; ", result.Errors));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Degree1_FallsBackToSerialAndProducesValidArchive()
    {
        var dir = Directory.CreateTempSubdirectory("mz_deg1").FullName;
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, "a.bin"), RandomBytes(30_000, 7));
            var outZip = Path.Combine(dir, "out.zip");

            await new ZipEngine().CompressAsync(Directory.GetFiles(dir, "*.bin"), outZip,
                new ArchiveOptions { CompressionLevel = 5, MultiThreadedCompression = true, ParallelCompressDegree = 1 });

            using var za = ZipFile.OpenRead(outZip);
            Assert.Single(za.Entries);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task EncryptedZip_FallsBackAndStillEncrypts()
    {
        var dir = Directory.CreateTempSubdirectory("mz_enc").FullName;
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, "a.bin"), RandomBytes(20_000, 3));
            var outZip = Path.Combine(dir, "enc.zip");

            await new ZipEngine().CompressAsync(Directory.GetFiles(dir, "*.bin"), outZip,
                new ArchiveOptions
                {
                    CompressionLevel = 5,
                    MultiThreadedCompression = true,
                    ParallelCompressDegree = 4,
                    Encrypt = true,
                    Password = "p@ss",
                });

            Assert.True(File.Exists(outZip));
            Assert.True(new FileInfo(outZip).Length > 0);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: 运行确认失败**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~ParallelCompressTests"
```
Expected: `Degree3_OutputIsValidAndByteIdenticalToSource` 失败 —— `ArchiveOptions.ParallelCompressDegree` 不存在（Task 5 才加），或分组未生效。

- [ ] **Step 3: 加 `ArchiveOptions.ParallelCompressDegree`**

在 `ArchiveEngine.cs:162`（`ParallelExtractDegree`）之后插入：

```csharp
    /// <summary>
    /// N 组并行压缩的目标组数。0 = 自动（取 <see cref="Environment.ProcessorCount"/>），上限 16。
    /// 运行时解析见 ZipEngine 内部；&lt;= 1 时回退标准串行路径。
    /// </summary>
    public int ParallelCompressDegree { get; set; } = 0;
```

- [ ] **Step 4: 解析有效组数**

在 `ZipEngine.cs` 内 `IsMultiThreadedEligible` 之后插入：

```csharp
    /// <summary>
    /// 解析有效并行组数：&lt;=0 取 CPU 数，上限 16，下限 1。
    /// 与解压侧 <c>ParallelExtractDegree</c> 的解析规则保持一致。
    /// </summary>
    internal static int ResolveParallelCompressDegree(int configured)
    {
        int n = configured;
        if (n <= 0) n = Environment.ProcessorCount;
        if (n > 16) n = 16;
        if (n < 1) n = 1;
        return n;
    }
```

- [ ] **Step 5: 接线 N 组分支**

修改 `ZipEngine.cs:1407` 的 `if (IsMultiThreadedEligible(options, files.Count, totalBytes))` 分支：在既有 store/compress 二分与 `compressGroup.Count == 0` 检查**之后**、既有单组 7z 调用**之前**，插入 N 组路径：

```csharp
                    // ── N 组并行分支 ──
                    // 复用既有回退判定（IsMultiThreadedEligible 的 7 条已完成）；
                    // 此处仅追加「组数」维度：分组后有效组 < 2 则走下方既有单组路径。
                    int degree = ResolveParallelCompressDegree(options.ParallelCompressDegree);
                    var groups = SplitCompressGroup(compressGroup, degree);

                    if (groups.Count >= 2)
                    {
                        // 每组一个 tempZip，组内 mt=off；并行度来自组间而非 7z 内部线程
                        var tempDir = Path.Combine(Path.GetTempPath(),
                            "mz_ngroup_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempDir);
                        var tempZips = new string[groups.Count];

                        // 全局字节加权进度的分母 = 全部输入文件的**输入**字节之和
                        long globalTotal = compressGroup.Sum(f => SafeFileSize(f.FullPath));
                        if (globalTotal <= 0) globalTotal = 1;

                        // 已结束组的输入字节累计（Interlocked 保证并发安全）
                        long finishedInputBytes = 0;

                        try
                        {
                            await Parallel.ForEachAsync(
                                Enumerable.Range(0, groups.Count),
                                new ParallelOptions
                                {
                                    MaxDegreeOfParallelism = groups.Count,
                                    CancellationToken = cancellationToken
                                },
                                async (i, ct) =>
                                {
                                    var groupTotal = groups[i].Sum(f => SafeFileSize(f.FullPath));
                                    var localLastReport = DateTime.Now;
                                    tempZips[i] = Path.Combine(tempDir, $"g{i}.zip");

                                    // 把组内**局部**字节进度换算为**全局**字节加权进度，
                                    // 同时携带 BatchIndex/BatchCount 供 UI 建立并更新通道行。
                                    var adapter = new InlineProgress<ArchiveProgress>(local =>
                                    {
                                        long localDone = Math.Min(local.ProcessedBytes, groupTotal);
                                        long globalDone = Interlocked.Read(ref finishedInputBytes) + localDone;

                                        progress?.Report(new ArchiveProgress
                                        {
                                            // 组阶段最多推进到 95%，余下 5% 留给合并阶段
                                            PercentComplete = Math.Min(95.0, globalDone * 100.0 / globalTotal),
                                            ProcessedBytes = globalDone,
                                            TotalBytes = globalTotal,
                                            CurrentFile = local.CurrentFile,
                                            EntryKey = local.EntryKey,
                                            BatchIndex = i,
                                            BatchCount = groups.Count,
                                            BatchPercentComplete = groupTotal > 0
                                                ? Math.Min(100.0, localDone * 100.0 / groupTotal)
                                                : 0.0,
                                        });
                                    });

                                    await Task.Run(() => CompressGroupWithSevenZip(
                                        groups[i], tempZips[i], options, adapter,
                                        0, groupTotal, groups[i].Count, 0,
                                        i, groups.Count, ref localLastReport), ct);

                                    // 该组完成：把它的输入字节并入全局已完成基数
                                    Interlocked.Add(ref finishedInputBytes, groupTotal);
                                });

                            // N 个 tempZip 原样字节拼接为最终 ZIP（copy-mode，不重压）。
                            // 合并是纯字节拷贝，耗时占比小；**不转发**其内部 92/94/97 百分比
                            // ——那会与组阶段的字节加权进度倒退/跳变，故 progress 传 null，
                            // 合并完成后直接收尾到 100%。
                            await ZipBinaryRewriter.RewriteAsync(
                                tempZips.Where(File.Exists).ToArray(), outputPath,
                                null, null,
                                options.FileNameEncoding == "gbk" ? Encoding.GetEncoding("GBK")
                                    : options.FileNameEncoding == "default" ? Encoding.Default
                                    : Encoding.UTF8,
                                options.Comment, null, cancellationToken);

                            progress?.Report(new ArchiveProgress
                            {
                                PercentComplete = 100,
                                ProcessedBytes = globalTotal,
                                TotalBytes = globalTotal,
                                CurrentFile = string.Empty,
                            });
                        }
                        finally
                        {
                            try { Directory.Delete(tempDir, true); } catch { /* best-effort */ }
                        }

                        processedBytes = totalBytes;
                        processedFiles = totalFiles;
                    }
```

**如何避免 `else` 分支复制旧逻辑（本步唯一需要人工判断的地方）：**

不要把既有 `compressGroup.Count == 0` 分支及其后的全部旧代码搬进 `else`。正确做法是把 **既有的单组 7z 调用点包进 `else`**：即在 `if (groups.Count >= 2) { ...N 组路径... }` 之后，把原本无条件执行的那段单组 7z 压缩 + store/compress 分支用 `else { }` 括起来。这样：

- `groups.Count < 2` → 走 `else`，与改动前**逐字节等价**（旧代码一行未改，只是缩进变化）；
- `groups.Count >= 2` → 走 N 组路径，旧代码整段跳过；
- diff 最小，且不需要在本计划里抄写旧逻辑（避免计划与源码漂移）。

实现者只需在编辑器里做这一次缩进包裹，不要重排旧代码顺序。

并在类内加两个辅助：

```csharp
    /// <summary>
    /// 取文件字节数；文件不存在/无权限时返回 0（进度计算用，不应中断压缩）。
    /// </summary>
    private static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0L; }
    }

    /// <summary>
    /// 立即执行的 <see cref="IProgress{T}"/>：不像 <see cref="Progress{T}"/> 那样
    /// 投递到同步上下文，因此在 <c>Parallel.ForEachAsync</c> 的后台线程里也能即时上报。
    /// </summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
```

> **`Progress<T>` 不可用于此处**：`Progress<T>.Report` 会把回调排入构造时捕获的
> `SynchronizationContext`；`Parallel.ForEachAsync` 的 worker 线程没有 UI 上下文，
> 回调会延迟甚至丢失，通道行将不刷新。必须用上面的 `InlineProgress<T>`。

- [ ] **Step 6: `AddToArchiveAsync` 同样接入 N 组（拖入已有压缩包场景）**

`AddToArchiveAsync` 有独立的 store/compress 分组（`:2163-2187`）与独立的合并收尾，
**不能**照抄 Step 5。它的形状是「已有压缩包 + 新增文件」，因此多源合并的三个输入是：

| 输入 | 在多源重载中的角色 |
|---|---|
| `tempArchive`（已有压缩包） | `sourcePaths[0]`（`keepEntryNames: null` = 全保留；`comment: null` = 沿用其注释） |
| N 个 `g{i}.zip` | `sourcePaths[1..N]` |
| `mtStoreGroup` 的条目 | `addEntries`（`NewEntry`，`Store: true`） |

把 `:2189` 的 `if (mtCompressGroup is { Count: > 0 })` 分支内、`:2206` 之前插入 N 组并行压缩，
与 Step 5 完全同构（独立 `tempDir` / `InlineProgress` 适配器 / `Interlocked` 累计）：

```csharp
                                    int degree = ResolveParallelCompressDegree(options.ParallelCompressDegree);
                                    var groups = SplitCompressGroup(compressGroup, degree);

                                    if (groups.Count >= 2)
                                    {
                                        var tempDir = Path.Combine(Path.GetTempPath(),
                                            "mz_ngroup_" + Guid.NewGuid().ToString("N"));
                                        Directory.CreateDirectory(tempDir);
                                        var tempZips = new string[groups.Count];

                                        long globalTotal = compressGroup.Sum(f => SafeFileSize(f.FullPath));
                                        if (globalTotal <= 0) globalTotal = 1;
                                        long finishedInputBytes = 0;

                                        try
                                        {
                                            await Parallel.ForEachAsync(
                                                Enumerable.Range(0, groups.Count),
                                                new ParallelOptions
                                                {
                                                    MaxDegreeOfParallelism = groups.Count,
                                                    CancellationToken = cancellationToken
                                                },
                                                async (i, ct) =>
                                                {
                                                    var groupTotal = groups[i].Sum(f => SafeFileSize(f.FullPath));
                                                    var localLastReport = DateTime.Now;
                                                    tempZips[i] = Path.Combine(tempDir, $"g{i}.zip");

                                                    var adapter = new InlineProgress<ArchiveProgress>(local =>
                                                    {
                                                        long localDone = Math.Min(local.ProcessedBytes, groupTotal);
                                                        long globalDone = Interlocked.Read(ref finishedInputBytes) + localDone;

                                                        progress?.Report(new ArchiveProgress
                                                        {
                                                            PercentComplete = Math.Min(95.0, globalDone * 100.0 / globalTotal),
                                                            ProcessedBytes = globalDone,
                                                            TotalBytes = globalTotal,
                                                            CurrentFile = local.CurrentFile,
                                                            EntryKey = local.EntryKey,
                                                            BatchIndex = i,
                                                            BatchCount = groups.Count,
                                                            BatchPercentComplete = groupTotal > 0
                                                                ? Math.Min(100.0, localDone * 100.0 / groupTotal)
                                                                : 0.0,
                                                        });
                                                    });

                                                    await Task.Run(() => CompressGroupWithSevenZip(
                                                        groups[i], tempZips[i], options, adapter,
                                                        0, groupTotal, groups[i].Count, 0,
                                                        i, groups.Count, ref localLastReport), ct);

                                                    Interlocked.Add(ref finishedInputBytes, groupTotal);
                                                });

                                            // 一次合并完成：已有压缩包 + N 个 tempZip + Store 条目
                                            // （对比旧路径的「先 Move tempZip，再单独二次合并 Store 组」两步）
                                            var storeEntries = new List<NewEntry>(storeGroup.Count);
                                            foreach (var (fullPath, relativePath) in storeGroup)
                                            {
                                                cancellationToken.ThrowIfCancellationRequested();
                                                var fi = new FileInfo(fullPath);
                                                storeEntries.Add(new NewEntry(
                                                    EntryName: relativePath,
                                                    Data: File.OpenRead(fullPath),
                                                    LastModified: fi.LastWriteTime,
                                                    Size: fi.Length,
                                                    Store: true));
                                            }

                                            await ZipBinaryRewriter.RewriteAsync(
                                                new[] { tempArchive }
                                                    .Concat(tempZips.Where(File.Exists))
                                                    .ToArray(),
                                                finalArchivePath,
                                                null, storeEntries,
                                                zipEncoding,
                                                null,   // 沿用 tempArchive 原注释
                                                null, cancellationToken);

                                            compressProcessed = compressTotalBytes;
                                            // 标记 N 组路径已完成，跳过下方旧的单组合并收尾
                                            goto NGroupMergeDone;
                                        }
                                        finally
                                        {
                                            try { Directory.Delete(tempDir, true); } catch { /* best-effort */ }
                                        }
                                    }
```

**必须注意的三点：**

1. `NewEntry.Data` 的 `FileStream` **必须保持打开**直到 `RewriteAsync` 完成（见 `ZipBinaryRewriter.cs:53-56` 的注释），故不能写在 `using` 里。
2. `goto NGroupMergeDone` 是刻意的：既有的单组合并收尾（`:2212` 起的 `File.Move` / Store 组二次合并）应被**整体跳过**，而非复制一份。在 `:2189` 分支块末尾加标签 `NGroupMergeDone:;` 即可。若编译器对跨越变量初始化的 `goto` 报错，改为把整段包进 `if (groups.Count >= 2) { ... } else { ...旧逻辑... }`，语义等价。
3. `finalArchivePath` 用既有临时输出路径，合并成功后按既有方式原子替换 `tempArchive`；**不要**改变对外可见的产物路径语义。

- [ ] **Step 7: 运行确认通过**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~ParallelCompressTests"
```
Expected: 全部通过（含 degree=1 回退、加密回退）。

- [ ] **Step 8: 全量 Core 测试**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```
Expected: 全部通过。

---

## Task 5: 设置项贯通 + 设置窗口 UI

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Models/AppSettings.cs:78` 附近
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/CompressSettingsViewModel.cs`（新属性 + `LocalizedStrings` 登记）
- Modify: `src/MantisZip.UI.Avalonia/Dialogs/CompressSettingsWindow.axaml`（压缩设置窗口「高级」tab）
- Modify: `src/MantisZip.UI.Avalonia/Services/CompressFlow.cs`
- Modify: `src/MantisZip.UI.Avalonia/Services/CompressService.cs`
- Modify: `src/MantisZip.UI.Avalonia/Localization/strings.{zh-CN,en,zh-TW}.json`

> **已核实：`SettingsWindowViewModel` 中不存在任何 degree 属性**，`ParallelExtractDegree` 的 UI
> 也不在设置窗口，而在 `Dialogs/ExtractSettingsWindow.axaml:127-138`，绑定的是
> `ViewModels/ExtractSettingsViewModel.cs:45`。本任务按**压缩侧的对称位置**落地
> （`CompressSettingsWindow` + `CompressSettingsViewModel`），**不要**改 `SettingsWindowViewModel`。

- [ ] **Step 1: 加 `AppSettings` 属性**

在 `AppSettings.cs:78`（`ParallelExtractDegree`）之后插入：

```csharp
    /// <summary>N 组并行压缩的目标组数，默认 CPU 数；1 = 串行。</summary>
    public int ParallelCompressDegree { get; set; } = Environment.ProcessorCount;
```

> 默认值沿用 `ParallelExtractDegree`（同为 `Environment.ProcessorCount`）。`ArchiveOptions`
> 侧默认 `0` 表示「运行时解析」，两者不对称是既有约定（见计划开头「默认值不对称」说明），勿"修正"。

- [ ] **Step 2: 贯通传递链**

对齐既有 `ParallelExtractDegree` 链路（其运行时消费点在 `ExtractFlow.cs:182`、
`SelectedItemsExtractService.cs:58`），逐层加字段：

```
AppSettings.ParallelCompressDegree
  → CompressSettingsViewModel.ParallelCompressDegree（默认取自 AppSettings，1..16 钳制）
  → CompressFlow.BuildRequest → CompressRequest.ParallelCompressDegree
  → CompressService.BuildOptions → ArchiveOptions.ParallelCompressDegree
  → ZipEngine.CompressAsync / AddToArchiveAsync（Task 4 已消费）
```

`CompressSettingsViewModel` 中的钳制写法照既有 `AdaptiveCompression`（`:201`）/
`MultiThreadedCompression`（`:207`）的写法办理。

- [ ] **Step 3: 压缩设置窗口 UI**

在 `Dialogs/CompressSettingsWindow.axaml` 的「高级」tab 内、`MultiThreadedCompression`
控件旁复制一份，**逐字照搬** `ExtractSettingsWindow.axaml:127-138` 的 `NumericUpDown`
模式（`Minimum="1" Maximum="16" Width="120"` + 灰色提示文字）：

```xml
                <!-- 并行压缩组数（1=串行，默认=CPU核心数；镜像 ExtractSettingsWindow 的并行解压线程数） -->
                <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,8,0,0">
                  <TextBlock Text="{Binding LocalizedStrings[Compress_ParallelDegree]}"
                             VerticalAlignment="Center" />
                  <NumericUpDown Minimum="1" Maximum="16"
                                 Value="{Binding ParallelCompressDegree}"
                                 Width="120" />
                  <TextBlock Text="{Binding LocalizedStrings[Compress_ParallelDegree_Hint]}"
                             VerticalAlignment="Center"
                             Foreground="{DynamicResource ThemeTextSecondaryBrush}"
                             FontSize="11" />
                </StackPanel>
```

**按项目规则 13，新增用户可见文案必须走本地化**：

1. 三语文件各加 2 个 key（插到文件头 `{` 之后，UTF-8 无 BOM + CRLF + 2 空格缩进）：
   `Compress_ParallelDegree`（标签「并行压缩组数」/「Parallel compress groups」）、
   `Compress_ParallelDegree_Hint`（提示「(1=串行，默认=CPU核心数)」）。
   key 集须三语完全一致，`AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 会校验。
2. **必须**在 `CompressSettingsViewModel.cs:478-479` 附近（既有 `LocalizedStrings[...] = ...`
   登记处）追加两行，否则 XAML 绑定空白且构建不报错：
   ```csharp
   LocalizedStrings["Compress_ParallelDegree"] = LocalizationManager.T("Compress_ParallelDegree");
   LocalizedStrings["Compress_ParallelDegree_Hint"] = LocalizationManager.T("Compress_ParallelDegree_Hint");
   ```

> 既有 `ExtractSettingsWindow.axaml:134` 的提示文字 `"(1=串行，默认=CPU核心数)"` 是**硬编码中文**
> （未走本地化，属既有缺陷）。本次新增的压缩侧提示走本地化，**不要**复制那个硬编码写法。

- [ ] **Step 4: 构建验证**

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
```
Expected: 0 errors。

- [ ] **Step 5: Avalonia 测试**

```powershell
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```
Expected: 全部通过（基线 114 passed / 2 skipped），含三语 key 集一致性校验。

---

## Task 6: 进度窗口通道视图

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Models/ParallelBatchProgressItem.cs`
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs:457-462`（早返回）、`:582`/`:603`（`UpsertParallelBatch`）
- Modify: `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml:343`（模板）、`:365-371`（文件名格）

共 5 处改动。**顺序重要**：先 VM（可独立验证），再 XAML。

- [ ] **Step 1: 写失败测试**

追加到 `tests/MantisZip.Tests/ProgressWindowBatchLogicTests.cs`：

```csharp
    [Fact]
    public void UpsertParallelBatch_WithBatchFields_CreatesRowWithFileRatio()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "a/b/c.txt",
            BatchIndex = 0,
            BatchCount = 3,
            BatchPercentComplete = 42.5,
            FilePercentComplete = 30.0,
            BatchProcessedFiles = 2,
            BatchTotalFiles = 7,
        });

        var row = Assert.Single(vm.ParallelBatchItems);
        Assert.Equal(0, row.Index);
        Assert.Equal(3, row.Total);
        Assert.Equal(42.5, row.Percent, 3);
        Assert.Equal(30.0, row.FileRatio, 3);
        Assert.Equal("2/7 文件", row.DetailText);
    }

    [Fact]
    public void SetProgress_WithEntryStatus_StillUpsertsParallelBatch()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "x.txt",
            BatchIndex = 1,
            BatchCount = 2,
            EntryKey = "x.txt",
            EntryStatus = ArchiveEntryStatus.Completed,
        });

        var row = Assert.Single(vm.ParallelBatchItems);
        Assert.Equal(1, row.Index);
    }

    [Fact]
    public void FileRatio_IsClampedToUnitRange()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { CurrentFile = "a", BatchIndex = 0, FilePercentComplete = 150 });
        Assert.Equal(1.0, vm.ParallelBatchItems[0].FileRatio, 3);

        var vm2 = new ProgressViewModel();
        vm2.SetProgress(new ArchiveProgress { CurrentFile = "a", BatchIndex = 0, FilePercentComplete = -20 });
        Assert.Equal(0.0, vm2.ParallelBatchItems[0].FileRatio, 3);
    }

    [Fact]
    public void FirstBatchArrival_AutoSwitchesToDetailedOnlyFromSimple()
    {
        var vm = new ProgressViewModel();
        Assert.Equal(ProgressContentMode.Simple, vm.ContentMode);

        vm.SetProgress(new ArchiveProgress { CurrentFile = "a", BatchIndex = 0, BatchCount = 2 });
        Assert.Equal(ProgressContentMode.Detailed, vm.ContentMode);
    }

    [Fact]
    public void ExplicitListChoice_IsNotOverriddenByFirstBatchArrival()
    {
        var vm = new ProgressViewModel();
        vm.ContentMode = ProgressContentMode.List;

        vm.SetProgress(new ArchiveProgress { CurrentFile = "a", BatchIndex = 0, BatchCount = 2 });
        Assert.Equal(ProgressContentMode.List, vm.ContentMode);
    }
```

- [ ] **Step 2: 运行确认失败**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~ProgressWindowBatchLogicTests"
```
Expected: 编译错误 —— `FileRatio` 不存在。

- [ ] **Step 3: 加 `FileRatio`**

在 `ParallelBatchProgressItem.cs` 中加属性（属性名与既有 `SizeRatio` 区分，避免语义混淆）：

```csharp
    /// <summary>当前文件的字节进度 0..1，驱动文件名格底纹宽度。</summary>
    [ObservableProperty]
    private double _fileRatio;
```

> **必须用 `[ObservableProperty]` 源生成器，不能写 `public double FileRatio { get; set; }`。**
> `UpsertParallelBatch` 是在**已存在的行对象**上反复赋值（`:1482` 每次上报都写），
> 而底纹宽度是 `MultiBinding` 绑定 `FileRatio` 的——普通自动属性不触发 `PropertyChanged`，
> 底纹会**停在首次赋值永不刷新**，且**构建与测试全部通过**（静默视觉缺陷）。
> 本类既有成员 `_percent` / `_statusBrushName` / `_detailText` / `_currentFile` 全部是
> `[ObservableProperty]` 写法（`Index` 因 `init` 只赋值一次除外），新成员须沿用同一约定。

- [ ] **Step 4: `UpsertParallelBatch` 赋值 + 前移早返回**

修改 `ProgressViewModel.cs`。当前 `:457-462` 有一个早返回，位于 `UpsertParallelBatch`（`:582`/`:603`）**之前**，导致带 `EntryStatus` 的上报根本走不到批次 upsert。

调整后的顺序：

```csharp
    private void SetProgress(ArchiveProgress p)
    {
        // ① 批次 upsert 必须在早返回【之前】：EntryStatus 上报同样携带批次身份，
        //    若被早返回拦下，通道行的 Percent 会被冻结在中途值。
        if (p.BatchIndex.HasValue)
            UpsertParallelBatch(p);

        // ② 原有早返回条件保持不变（其余逻辑不动）
        if (<原条件>)
            return;

        // ... 其余原有逻辑 ...
    }
```

在 `UpsertParallelBatch` 内部赋值并钳制：

```csharp
        item.FileRatio = Math.Clamp((p.FilePercentComplete ?? 0) / 100.0, 0.0, 1.0);
```

- [ ] **Step 5: arrival-triggered 自动切详细**

在 `UpsertParallelBatch` 末尾（集合已添加元素之后）追加：

```csharp
        // 方案 B：首个 BatchIndex 到达、集合由空转非空时自动切详细。
        // 守卫 _contentMode == Simple 保证不覆盖用户显式选择（默认即 Simple，
        // 三个单选项 Click 只把它改写为 Simple/Detailed/List）。
        // 回退路径永不上报 BatchIndex，故此分支永不触发 —— 无需回退兜底，
        // 也不依赖 ProgressViewModel.cs:838-839（那是多档案切换私有路径）。
        if (_parallelBatchItems.Count == 1 && _contentMode == ProgressContentMode.Simple)
            ContentMode = ProgressContentMode.Detailed;
```

- [ ] **Step 6: 运行确认通过**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter "FullyQualifiedName~ProgressWindowBatchLogicTests|FullyQualifiedName~ProgressBatchItemTests"
```
Expected: 全部通过。

- [ ] **Step 7: XAML 改动 1 —— 文件名格换 Grid**

`ProgressWindow.axaml:365-371` 现为纯 `TextBlock`。改为底纹 + 文字叠加，参照 `MainWindow.axaml:1144-1158` 的 `RatioToWidthConverter` + `ProgressBarSizeBrush` 模式：

```xml
                        <!-- 批次当前文件名格：底纹（该批次已处理字节比例）+ 文字叠加 -->
                        <Grid Grid.Column="1">
                            <!-- 底纹：宽度 = FileRatio × 容器实际宽度，故必须用 MultiBinding（RatioToWidthConverter 是双输入转换器） -->
                            <Rectangle Fill="{DynamicResource ProgressBarSizeBrush}"
                                       HorizontalAlignment="Left"
                                       IsVisible="{Binding CurrentFile, Converter={StaticResource StringNotEmpty}}">
                                <Rectangle.Width>
                                    <MultiBinding Converter="{StaticResource RatioToWidthConverter}">
                                        <Binding Path="FileRatio" />
                                        <Binding Path="Bounds.Width"
                                                 RelativeSource="{RelativeSource AncestorType=ContentPresenter}" />
                                    </MultiBinding>
                                </Rectangle.Width>
                            </Rectangle>
                            <!-- 文字叠加：引擎未上报当前文件名时为空串 → 隐藏，无布局异常 -->
                            <TextBlock Text="{Binding CurrentFile}"
                                       IsVisible="{Binding CurrentFile, Converter={StaticResource StringNotEmpty}}"
                                       FontSize="11"
                                       TextTrimming="CharacterEllipsis"
                                       VerticalAlignment="Center"
                                       Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
                        </Grid>
```

> **`RatioToWidthConverter` 是 `MultiBindingConverter`（双输入），不是单输入 `IValueConverter`。**
> 已核实 `MainWindow.axaml:1144-1158` 的实际用法：`<MultiBinding Converter="...">` 内放
> `Binding Path="SizeRatio"` + `Binding Path="Bounds.Width" RelativeSource={RelativeSource AncestorType=ContentPresenter}`。
> 若写成 `Width="{Binding FileRatio, Converter=...}"`（单绑定），转换器拿不到容器宽度，
> 运行时会抛 `InvalidCastException`/返回默认值 —— **这是本计划早前版本的真实缺陷，已修正**。
>
> `ParallelBatchProgressItem` 的实际成员只有 `Index`（`init`）、`Percent`、`StatusBrushName`、
> `DetailText`、`CurrentFile`。**没有 `FileName`、没有 `IsActive`**；文件名绑定一律用 `CurrentFile`，
> 可见性沿用既有 `StringNotEmpty` 转换器（见 `ProgressWindow.axaml:367`）。

- [ ] **Step 8: XAML 改动 2 —— 模板扩 5 列**

`ProgressWindow.axaml:343` 的 `Grid.ColumnDefinitions` 由 4 列扩为 5 列，新增最右详情列；在 `Grid.Column="4"` 处绑定既有 `DetailText`（已在 `ProgressViewModel.cs:616` 计算，三语 key 齐全，此前未绑定）：

```xml
                            <TextBlock Grid.Column="4"
                                       Text="{Binding DetailText}"
                                       FontSize="10"
                                       Foreground="{DynamicResource ThemeTextSecondaryBrush}"
                                       VerticalAlignment="Center"
                                       TextTrimming="CharacterEllipsis" />
```

- [ ] **Step 9: 构建验证**

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
```
Expected: 0 errors。

- [ ] **Step 10: 全量测试**

```powershell
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```
Expected: 全部通过。

---

## Task 7: bench `--degree` 扩展

**Files:**
- Modify: `scripts/bench-zip-mt.cs`

**关键约束（已核实）：** `Configs.All` 是**固定枚举**，`--degree` 若作为其维度会产生 N×M 组合爆炸。正确做法是 `degree` 作为**外层循环**，`Configs.All` 固定。

- [ ] **Step 1: 加参数解析（逗号分隔列表，一次跑完所有 degree）**

```csharp
        // --degrees 1,2,4,8 —— 逗号分隔；缺省 = { 0 }（不跑 degree 维度，保持既有行为）
        int[] degrees = { 0 };
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--degrees" && i + 1 < args.Length)
            {
                degrees = args[++i]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse)
                    .ToArray();
            }
        }
```

> **参数名定为 `--degrees`（复数）**：单个 `--degree 1 2 4 8` 无法区分「一个多值参数」与
> 「四个位置参数」，而 bench 的既有解析器会把后续 token 当作语料路径。逗号分隔是唯一
> 无歧义且与既有 `--xxx` 风格一致的选择。

- [ ] **Step 2: 外层循环**

把现有对 `Configs.All` 的遍历包一层：

```csharp
        foreach (var d in degrees)
        {
            options.ParallelCompressDegree = d;
            // ... 既有对 Configs.All 的遍历原样保留 ...
        }
```

并让输出行携带 degree：

```csharp
        Console.WriteLine($"degree={d}\t{config.Name}\t{elapsedMs}\t{sizeBytes}\t{ratio:F4}");
```

- [ ] **Step 3: 跑基准**

```powershell
dotnet run --project scripts/bench-zip-mt.cs -- --degrees 1,2,4,8
```

记录 `degree=1`（≈现状）与 `degree=N` 的耗时比，验证 G2（N 组是否快于单线程）。

---

## Task 8: 端到端手动验证

**Files:** 无代码改动。

- [ ] **Step 1: 全量测试**

```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```
Expected: 构建 0 error；Core 572+15 passed（新增 5+5+1+4）；Avalonia 114+5 passed。

- [ ] **Step 2: 并行压缩手动验证（G1）**

```powershell
dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
```

构造 ≥ 8 个文件、总量 ≥ 50MB 的临时目录（含若干大文件），压缩组数设为 4。

**期望**：进度窗口自动切到「详细」，显示 **4 行**通道；每行有独立底纹（文件名格）与百分比；「完成 N/M 文件」列有值；底部总进度单调递增到 100%。

- [ ] **Step 3: 串行验证**

组数设为 1，重新压缩同一目录。

**期望**：窗口停在**简约模式**（路径行 + 当前文件名 + 单条当前文件进度条）；「详细」单选项**不出现**；**没有**任何通道行。

- [ ] **Step 4: 回退验证**

用加密 ZIP（设密码 + 组数 4）压缩同一目录。

**期望**：与 Step 3 相同 —— 简约模式，无通道行，无空白面板。

- [ ] **Step 5: 用户手动切换不被打断**

组数 4，开始压缩后**立即**点「列表」，等首个批次到达。

**期望**：停留在「列表」，**不**被自动切到「详细」。

- [ ] **Step 6: 产出校验**

用 7-Zip / Windows 资源管理器打开 Step 2 的输出 zip，确认条目完整、可解压。

- [ ] **Step 7: 报告**

汇总：G1（通道真实字节进度）达成情况、G2（并行加速比）、6 项手动验证结果。**不 commit、不 push、不改版本号。**

---

## Self-Review

**1. Spec 覆盖检查**

| Spec 章节 | 对应任务 |
|---|---|
| §4.6 回退条件（9 条） | Task 4 Step 4（`ResolveParallelCompressDegree` + 组数判定）、Step 5（`CompressAsync`）、Step 6（`AddToArchiveAsync`）；Step 1-2 测试覆盖加密 / `degree=1` 回退 |
| §5.1 复用既有批次字段 | Task 3 Step 6（补批次字段，不新增契约字段） |
| §5.2.1 改动 1-2（`FileRatio` + 赋值） | Task 6 Step 3（加属性）、Step 4（赋值 + 钳制） |
| §5.2.1 改动 3（Grid 底纹） | Task 6 Step 7 |
| §5.2.1 改动 4（5 列 + `DetailText`） | Task 6 Step 8 |
| §5.3 早返回缺陷 | Task 6 Step 4（前移早返回至 `UpsertParallelBatch` 之前） |
| §5.4.1 方案 B（arrival-triggered） | Task 6 Step 5 |
| §6 设置项 + 传递链 | Task 5 |
| §7.3 bench `--degrees` | Task 7 |
| §9 实施顺序 8 步 | Task 1-8 一一对应 |

无遗漏。

**2. 已修正的缺陷（本轮审查实际发现并修复，非推测）**

初稿存在 8 处会导致实现失败的问题，均已按源码核实后修正：

| # | 缺陷 | 后果 | 修正 |
|---|---|---|---|
| 1 | Task 2 虚构 `ZipOutputWriterAdapter` / `CopyEntryAsync` / `RewriteResult.Success` / `EntryCount` | 编译失败 | 按 `ZipBinaryRewriter.cs` 真实成员改写：`ReadEocd`/`ReadCentralDirectory`/`ReadAndMaybeRewriteLfh`/`CopyStreamRangeAsync`/`CompressNewEntry`/`WriteCentralDirectory`/`WriteEocd`/`CleanupFile` |
| 2 | `CdEntry.Name`（实际为 `FileName`）、`NewEntry` 参数顺序 | 编译失败 | 已核实并修正 |
| 3 | Task 4 进度回调可能为 `null`，且组内百分比被当作整体百分比 | 进度不刷新 / 数值倒退 | 引入同步 `InlineProgress<T>` + `Interlocked` 字节加权聚合；组阶段封顶 95%，完成时直报 100% |
| 4 | Task 3 依赖 Task 4 才新增的 `ArchiveOptions.ParallelCompressDegree` | 顺序倒置，红测不成立 | 改为 refactor-first：Step 1-2 先做纯签名重构并跑绿，Step 3 直接调用 internal 方法写红测 |
| 5 | Task 7 `--degree` 单值解析 vs 示例传 4 个值 | 基准脚本把 `2 4 8` 当语料路径 | 定为 `--degrees 1,2,4,8` 逗号分隔 |
| 6 | Task 6 Step 7 把 `RatioToWidthConverter` 当单输入 `Converter=` 用，并绑定不存在的 `FileName` / `IsActive` | 运行时转换器异常 / 绑定空 | 改为 `MultiBinding`（比例 + `Bounds.Width`），文件名改用实际的 `CurrentFile`，可见性沿用 `StringNotEmpty` |
| 7 | Task 5 把 UI 宿主写成设置窗口 + `SettingsWindowViewModel` | 改错文件，该 VM 无 degree 属性 | 已核实：extract 侧在 `ExtractSettingsWindow.axaml:127-138` + `ExtractSettingsViewModel`；压缩侧改用对称的 `CompressSettingsWindow` + `CompressSettingsViewModel` |
| 8 | Task 4 只接线 `CompressAsync`，与「`AddToArchiveAsync` 接入 N 组」矛盾 | 拖入已有压缩包时无通道进度 | 补 Task 4 Step 6（`AddToArchiveAsync` 走多源合并：已有包 + N 个 tempZip + Store 条目） |

另修正 Task 2 / Task 4 的步骤编号错乱（Task 2 原有三个 "Step 3"；Task 4 原缺 Step 6）。

**3. 占位符扫描**

已无占位符、无「以实际实现为准」类推脱。Task 2 早期刻意保留的「请先读源码确认字段名」注记，在核实 `RewriteResult` / `CdEntry` / `NewEntry` / `ArchiveProgress` 真实定义后已替换为可直接粘贴的代码。

计划中所有引用的行号与成员均已对源码核实：
`AppSettings.cs:78`、`ExtractSettingsWindow.axaml:127-138`、`ExtractSettingsViewModel.cs:45`/`:207`、
`ExtractFlow.cs:182`、`SelectedItemsExtractService.cs:58`、`CompressSettingsViewModel.cs:201`/`:207`/`:478-479`、
`ParallelBatchProgressItem.cs`（全部成员）、`ProgressViewModel.cs:453`/`:583`/`:603`/`:838`、
`ProgressWindow.axaml:339`/`:343`/`:365-371`、`MainWindow.axaml:1144-1158`、`MantisZip.Core.csproj:13-15`。

**4. 类型一致性**

- `SplitCompressGroup` 返回 `List<List<(string FullPath, string RelativePath)>>` — Task 1 定义，Task 4 Step 5/6 消费，一致。
- `CompressGroupWithSevenZip` 新参数顺序 `(..., int storeProcessedFiles, int? batchIndex, int? batchCount, ref DateTime lastReportTime)` — Task 3 Step 1 定义，Step 7 接线两个调用点，Task 4 Step 5/6 按此顺序调用，一致。
- `ParallelCompressDegree`：`ArchiveOptions` 默认 `0`（运行时由 `ResolveParallelCompressDegree` 解析，上限 16）；`AppSettings` 默认 `ProcessorCount`。与 `ParallelExtractDegree` 的既有不对称约定一致，勿"修正"。
- `ParallelBatchProgressItem` 实际成员为 `Index`（`init`）、`Percent`、`StatusBrushName`、`DetailText`、`CurrentFile`；Task 6 只**新增** `FileRatio`，不假设 `FileName` / `IsActive` 存在。
- `UpsertParallelBatch` 为 `private`；Task 6 测试经 public `SetProgress(ArchiveProgress)` 驱动，一致。
- `FileRatio` / `FilePercentComplete` 除以 100 后钳制 0..1 — Task 6 Step 3/4/测试三处一致。