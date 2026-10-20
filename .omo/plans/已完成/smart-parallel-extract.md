# 智能并行解压（全局开关 + SSD/HDD 智能度 + 大包预测试）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 根据目标磁盘类型自动选择并行解压线程数（SSD/HDD 双可调默认值），大压缩包（≥1GB）预测试选优，用户可自定义或关闭；决策逻辑集中在 Core `ParallelDegreeResolver`，UI 层在调用引擎前解析出具体线程数经 `ArchiveOptions.ParallelExtractDegree` 传入。

**Architecture:** Core 新增三个无 UI 依赖的组件——`DiskTypeDetector`（Windows DeviceIoControl 检测 SSD/HDD + 盘符缓存）、`ParallelDegreeOptimizer`（大包预测试：样本解压计时选优）、`ParallelDegreeResolver`（5 步决策链纯函数 + 异步封装）。UI 层 `ExtractFlow` / `SelectedItemsExtractService` 在构造 `ArchiveOptions` 时调用 resolver 得到具体线程数；`ZipEngine` 无需改动（已正确消费 `options.ParallelExtractDegree > 0`）。设置窗口新增「并行解压」卡片（全局开关 + 智能开关 + SSD/HDD 双输入 + 预测试开关 + 帮助弹窗），解压对话框线程数默认改 0=自动。

**Tech Stack:** .NET 10 / C#、Avalonia 12、CommunityToolkit.Mvvm、SharpCompress（引擎复用）、xUnit、Windows DeviceIoControl P/Invoke。

**Spec:** `docs/superpowers/specs/2026-10-04-smart-parallel-extract-design.md`
**原型:** `docs/prototypes/smart-parallel-extract-settings.html`

---

## 文件结构总览

| 操作 | 文件 | 职责 |
|------|------|------|
| 新增 | `src/MantisZip.Core/Utils/DiskTypeDetector.cs` | 磁盘类型检测（SSD/HDD/Unknown）+ 盘符缓存 |
| 新增 | `src/MantisZip.Core/Services/ParallelDegreeOptimizer.cs` | 大包预测试（样本解压计时选优） |
| 新增 | `src/MantisZip.Core/Services/ParallelDegreeResolver.cs` | 5 步线程数决策链 + `ParallelExtractSettings` 记录 |
| 新增 | `src/MantisZip.UI.Avalonia/Dialogs/ParallelExtractHelpDialog.axaml` | 帮助弹窗 UI |
| 新增 | `src/MantisZip.UI.Avalonia/Dialogs/ParallelExtractHelpDialog.axaml.cs` | 帮助弹窗 code-behind |
| 修改 | `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` | +5 属性 |
| 修改 | `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs` | +属性/命令/本地化 |
| 修改 | `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml` | 解压 Tab 新增卡片 |
| 修改 | `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml.cs` | [?] 按钮 Click 处理 |
| 修改 | `src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs` | 接线 resolver |
| 修改 | `src/MantisZip.UI.Avalonia/Services/SelectedItemsExtractService.cs` | 接线 resolver |
| 修改 | `src/MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs` | 线程数默认 0=自动 |
| 修改 | `src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml` | NumericUpDown Minimum=0 + 提示文案 |
| 修改 | `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` | +15 key |
| 修改 | `src/MantisZip.UI.Avalonia/Localization/strings.en.json` | +15 key |
| 修改 | `src/MantisZip.UI.Avalonia/Localization/strings.zh-TW.json` | +15 key |
| 新增 | `tests/MantisZip.Tests/Utils/DiskTypeDetectorTests.cs` | 磁盘检测测试 |
| 新增 | `tests/MantisZip.Tests/Services/ParallelDegreeOptimizerTests.cs` | 优化器测试 |
| 新增 | `tests/MantisZip.Tests/Services/ParallelDegreeResolverTests.cs` | 决策链测试 |
| 修改 | `tests/MantisZip.UI.Avalonia.Tests/ExtractParallelDegreeWiringTests.cs` | 更新源码守卫 |

**不改动**：`ZipEngine.cs`（已正确消费 `options.ParallelExtractDegree > 0`，resolver 在 UI 层解析后传入具体值）。

---

## Task 1: Core — DiskTypeDetector

**Files:**
- Create: `src/MantisZip.Core/Utils/DiskTypeDetector.cs`
- Create: `tests/MantisZip.Tests/Utils/DiskTypeDetectorTests.cs`

### Step 1: 写失败测试

创建 `tests/MantisZip.Tests/Utils/DiskTypeDetectorTests.cs`：

```csharp
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

public class DiskTypeDetectorTests
{
    [Fact]
    public void GetDiskType_SystemTempPath_ReturnsValidEnum()
    {
        var path = Path.GetTempPath();
        var result = DiskTypeDetector.GetDiskType(path);
        Assert.True(Enum.IsDefined(result));
    }

    [Fact]
    public void GetDiskType_SamePathTwice_ReturnsCachedValue()
    {
        var path = Path.GetTempPath();
        var first = DiskTypeDetector.GetDiskType(path);
        var second = DiskTypeDetector.GetDiskType(path);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GetDiskType_EmptyPath_ReturnsUnknown()
        => Assert.Equal(DiskType.Unknown, DiskTypeDetector.GetDiskType(""));

    [Fact]
    public void GetDiskType_NullPath_ReturnsUnknown()
        => Assert.Equal(DiskType.Unknown, DiskTypeDetector.GetDiskType(null!));

    [Fact]
    public void GetDiskType_NonWindowsStylePath_ReturnsUnknown()
        => Assert.Equal(DiskType.Unknown, DiskTypeDetector.GetDiskType(@"relative\path"));

    [Fact]
    public void ExtractDriveRoot_VariousFormats_ExtractsRoot()
    {
        Assert.Equal("C:", DiskTypeDetector.ExtractDriveRoot(@"C:\Users\test"));
        Assert.Equal("C:", DiskTypeDetector.ExtractDriveRoot(@"C:/Users/test"));
        Assert.Equal("C:", DiskTypeDetector.ExtractDriveRoot(@"C:"));
        Assert.Null(DiskTypeDetector.ExtractDriveRoot(@"relative\path"));
        Assert.Null(DiskTypeDetector.ExtractDriveRoot(@"\\server\share\x"));
    }
}
```

### Step 2: 运行测试确认失败

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter DiskTypeDetectorTests`
Expected: 编译失败 `DiskTypeDetector` 未定义。

### Step 3: 实现 DiskTypeDetector

创建 `src/MantisZip.Core/Utils/DiskTypeDetector.cs`：

```csharp
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MantisZip.Core.Utils;

/// <summary>磁盘类型。</summary>
public enum DiskType
{
    /// <summary>固态硬盘（无寻道惩罚）。</summary>
    Ssd,
    /// <summary>机械硬盘（有寻道惩罚）。</summary>
    Hdd,
    /// <summary>未知（检测失败 / 网络盘 / 非 Windows）。</summary>
    Unknown
}

/// <summary>
/// 磁盘类型检测（Windows DeviceIoControl IOCTL_STORAGE_QUERY_PROPERTY +
/// StorageDeviceSeekPenaltyProperty）。结果按盘符缓存（磁盘类型不会变）。
/// 非 Windows / 检测失败 / UNC / 相对路径 → Unknown。
/// </summary>
public static class DiskTypeDetector
{
    private static readonly ConcurrentDictionary<string, DiskType> Cache = new();

    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const int StorageDeviceSeekPenaltyProperty = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
    {
        public int Version;
        public int Size;
        public byte IncursSeekPenalty;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        ref STORAGE_PROPERTY_QUERY lpInBuffer, uint nInBufferSize,
        ref DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READWRITE = 3;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    /// <summary>根据路径提取盘符根（如 "C:\Users\x" → "C:"）。无盘符（相对/UNC）返回 null。</summary>
    public static string? ExtractDriveRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return null;
            // "C:\" → "C:"；UNC → null
            if (root.Length >= 2 && root[1] == ':') return root[..2];
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>检测路径所在磁盘类型。非 Windows 返回 Unknown。</summary>
    public static DiskType GetDiskType(string? path)
    {
        if (!OperatingSystem.IsWindows()) return DiskType.Unknown;

        var root = ExtractDriveRoot(path);
        if (root == null) return DiskType.Unknown;

        return Cache.GetOrAdd(root, QuerySeekPenalty);
    }

    private static DiskType QuerySeekPenalty(string driveRoot)
    {
        try
        {
            using var handle = CreateFile(
                driveRoot + @"\",
                GENERIC_READ,
                FILE_SHARE_READWRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle.IsInvalid) return DiskType.Unknown;

            var query = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageDeviceSeekPenaltyProperty,
                QueryType = 0 // PropertyStandardQuery
            };
            var descriptor = new DEVICE_SEEK_PENALTY_DESCRIPTOR();

            if (!DeviceIoControl(
                    handle, IOCTL_STORAGE_QUERY_PROPERTY,
                    ref query, (uint)Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(),
                    ref descriptor, (uint)Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(),
                    out _, IntPtr.Zero))
            {
                return DiskType.Unknown;
            }

            return descriptor.IncursSeekPenalty != 0 ? DiskType.Hdd : DiskType.Ssd;
        }
        catch
        {
            return DiskType.Unknown;
        }
    }
}
```

### Step 4: 运行测试验证通过

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter DiskTypeDetectorTests`
Expected: 全部 PASS。

### Step 5: 提交

```powershell
git add src/MantisZip.Core/Utils/DiskTypeDetector.cs tests/MantisZip.Tests/Utils/DiskTypeDetectorTests.cs
git commit -m "feat(core): 新增 DiskTypeDetector 磁盘类型检测（DeviceIoControl SeekPenalty + 盘符缓存）"
```

---

## Task 2: Core — ParallelDegreeOptimizer（大包预测试）

**Files:**
- Create: `src/MantisZip.Core/Services/ParallelDegreeOptimizer.cs`
- Create: `tests/MantisZip.Tests/Services/ParallelDegreeOptimizerTests.cs`

### Step 1: 写失败测试

创建 `tests/MantisZip.Tests/Services/ParallelDegreeOptimizerTests.cs`：

```csharp
using MantisZip.Core.Services;
using MantisZip.Tests.Fixtures;
using Xunit;

namespace MantisZip.Tests.Services;

public class ParallelDegreeOptimizerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var d in _tempDirs.Where(Directory.Exists))
            try { Directory.Delete(d, true); } catch { }
    }

    private string TrackDir(string path) { _tempDirs.Add(path); return path; }

    private static string NewTempDir()
        => Directory.CreateTempSubdirectory("MantisZipOptTest_").FullName;

    [Fact]
    public void GetTestConfigs_HighCoreCount_Returns4Candidates()
    {
        var configs = ParallelDegreeOptimizer.GetTestConfigs(16);
        Assert.Equal(new[] { 4, 8, 12, 16 }, configs);
    }

    [Fact]
    public void GetTestConfigs_MidCoreCount_ReturnsDeduplicated()
    {
        var configs = ParallelDegreeOptimizer.GetTestConfigs(10);
        // [2, 4, 8, 10] — 10 不与前面重复
        Assert.Equal(new[] { 2, 4, 8, 10 }, configs);
    }

    [Fact]
    public void GetTestConfigs_MidCoreCountDedup_CapsAt4()
    {
        // ProcessorCount=8 → [2, 4, 8, 8] 去重后 [2, 4, 8] 只有 3 个
        var configs = ParallelDegreeOptimizer.GetTestConfigs(8);
        Assert.Equal(new[] { 2, 4, 8 }, configs);
    }

    [Fact]
    public void GetTestConfigs_LowCoreCount_Returns1Based()
    {
        var configs = ParallelDegreeOptimizer.GetTestConfigs(4);
        Assert.Equal(new[] { 1, 2, 4 }, configs);
    }

    [Fact]
    public void ShouldPreTest_LargeArchiveWithPreTestOn_ReturnsTrue()
        => Assert.True(ParallelDegreeOptimizer.ShouldPreTest(
            2L * 1024 * 1024 * 1024, preTestEnabled: true, supportsParallel: true));

    [Fact]
    public void ShouldPreTest_SmallArchive_ReturnsFalse()
        => Assert.False(ParallelDegreeOptimizer.ShouldPreTest(
            100L * 1024 * 1024, preTestEnabled: true, supportsParallel: true));

    [Fact]
    public void ShouldPreTest_PreTestDisabled_ReturnsFalse()
        => Assert.False(ParallelDegreeOptimizer.ShouldPreTest(
            2L * 1024 * 1024 * 1024, preTestEnabled: false, supportsParallel: true));

    [Fact]
    public void ShouldPreTest_EngineNotSupportsParallel_ReturnsFalse()
        => Assert.False(ParallelDegreeOptimizer.ShouldPreTest(
            2L * 1024 * 1024 * 1024, preTestEnabled: true, supportsParallel: false));

    [Fact]
    public void ShouldPreTest_ThresholdBoundary_1GBExactly_True()
        => Assert.True(ParallelDegreeOptimizer.ShouldPreTest(
            1L * 1024 * 1024 * 1024, preTestEnabled: true, supportsParallel: true));

    [Fact]
    public void ShouldPreTest_JustBelow1GB_False()
        => Assert.False(ParallelDegreeOptimizer.ShouldPreTest(
            1L * 1024 * 1024 * 1024 - 1, preTestEnabled: true, supportsParallel: true));

    [Fact]
    public async Task FindOptimalDegreeAsync_SmallZip_ReturnsValidDegree()
    {
        // 用小 ZIP 跑一次真实预测试：不检查返回值是否"最优"，
        // 只验证不抛异常、返回值在测试配置集合内、临时目录已清理
        var archive = ArchiveFixtures.CreateMultiFileZipArchive(30, 10); // 30 文件 x 10KB
        var dest = TrackDir(NewTempDir());

        var result = await ParallelDegreeOptimizer.FindOptimalDegreeAsync(
            archive, dest, password: null, progress: null, CancellationToken.None);

        Assert.InRange(result, 1, 16);
        // 预测试临时目录应已全部删除
        var leftovers = Directory.GetDirectories(dest, "__parallel_test_*");
        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task FindOptimalDegreeAsync_CorruptArchive_ReturnsNegative()
    {
        var corrupt = Path.Combine(NewTempDir(), "corrupt.zip");
        File.WriteAllBytes(corrupt, new byte[] { 1, 2, 3, 4, 5 });
        var dest = TrackDir(NewTempDir());

        var result = await ParallelDegreeOptimizer.FindOptimalDegreeAsync(
            corrupt, dest, password: null, progress: null, CancellationToken.None);

        Assert.True(result < 0, $"corrupt archive should return negative, got {result}");
    }

    [Fact]
    public async Task FindOptimalDegreeAsync_Cancelled_ThrowsAndCleansUp()
    {
        var archive = ArchiveFixtures.CreateMultiFileZipArchive(30, 10);
        var dest = TrackDir(NewTempDir());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ParallelDegreeOptimizer.FindOptimalDegreeAsync(
                archive, dest, password: null, progress: null, cts.Token));

        var leftovers = Directory.GetDirectories(dest, "__parallel_test_*");
        Assert.Empty(leftovers);
    }
}
```

### Step 2: 运行测试确认失败

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter ParallelDegreeOptimizerTests`
Expected: 编译失败 `ParallelDegreeOptimizer` 未定义。

### Step 3: 实现 ParallelDegreeOptimizer

创建 `src/MantisZip.Core/Services/ParallelDegreeOptimizer.cs`：

```csharp
using System.Diagnostics;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;

namespace MantisZip.Core.Services;

/// <summary>
/// 大包预测试优化器：解压 ≥1GB 压缩包前，用小样本对比不同线程数耗时，选出最优线程数。
/// 测试写入目标目录下的临时子目录（用户已确认），测完立即删除。
/// 任何异常/取消 → 返回负数（调用方回退磁盘类型默认值）。
/// </summary>
public static class ParallelDegreeOptimizer
{
    /// <summary>触发预测试的压缩包大小阈值（1GB）。</summary>
    public const long LargeArchiveThreshold = 1L * 1024 * 1024 * 1024;

    /// <summary>样本目标大小（约 50MB）。</summary>
    private const long SampleTargetBytes = 50L * 1024 * 1024;

    /// <summary>样本最少文件数（避免单文件大包主导结果）。</summary>
    private const int MinSampleFiles = 5;

    /// <summary>样本最多文件数（避免文件数过多影响测试）。</summary>
    private const int MaxSampleFiles = 50;

    /// <summary>
    /// 判断是否应执行预测试：压缩包 ≥1GB 且预测试开启且引擎支持并行。
    /// </summary>
    public static bool ShouldPreTest(long archiveSize, bool preTestEnabled, bool supportsParallel)
        => preTestEnabled && supportsParallel && archiveSize >= LargeArchiveThreshold;

    /// <summary>
    /// 根据 CPU 核数生成测试配置（去重、排序、≤4 个）：
    /// ≥16 核: [4,8,12,16]；8-15 核: [2,4,8,N]；&lt;8 核: [1,2,4,N]。
    /// </summary>
    public static int[] GetTestConfigs(int processorCount)
    {
        int[] raw = processorCount >= 16
            ? [4, 8, 12, 16]
            : processorCount >= 8
                ? [2, 4, 8, processorCount]
                : [1, 2, 4, processorCount];
        return raw.Distinct().OrderBy(x => x).Take(4).ToArray();
    }

    /// <summary>
    /// 快速测试找出最优并行度。
    /// </summary>
    /// <returns>最优线程数；失败/取消返回负数（-1），调用方回退磁盘类型默认值。</returns>
    public static async Task<int> FindOptimalDegreeAsync(
        string archivePath,
        string destinationPath,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct)
    {
        CoreLog.Entry();
        var configs = GetTestConfigs(Environment.ProcessorCount);
        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null || !engine.SupportsParallelExtract)
        {
            CoreLog.Info("FindOptimalDegreeAsync: engine does not support parallel, skip");
            return -1;
        }

        // 1. 选取测试样本（按压缩包内条目顺序累加到 ~50MB）
        IReadOnlyList<ArchiveItem> allEntries;
        try
        {
            allEntries = await engine.ListEntriesAsync(archivePath, password, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CoreLog.Info($"FindOptimalDegreeAsync: ListEntriesAsync failed: {ex.Message}");
            return -1;
        }

        var sampleKeys = SelectSampleEntries(allEntries);
        if (sampleKeys.Count == 0)
        {
            CoreLog.Info("FindOptimalDegreeAsync: no sample entries");
            return -1;
        }

        CoreLog.Info($"FindOptimalDegreeAsync: {sampleKeys.Count} sample files, configs=[{string.Join(",", configs)}]");

        // 2. 逐配置测试
        int bestDegree = -1;
        long bestElapsed = long.MaxValue;

        foreach (var degree in configs)
        {
            ct.ThrowIfCancellationRequested();

            var tempDir = Path.Combine(destinationPath, $"__parallel_test_{degree}");
            try
            {
                Directory.CreateDirectory(tempDir);

                var sw = Stopwatch.StartNew();
                await engine.ExtractEntriesAsync(
                    archivePath, sampleKeys, tempDir,
                    password, progress: null, ct,
                    options: new ArchiveOptions { ParallelExtractDegree = degree });
                sw.Stop();

                CoreLog.Info($"FindOptimalDegreeAsync: degree={degree}, elapsed={sw.ElapsedMilliseconds}ms");
                if (sw.ElapsedMilliseconds < bestElapsed)
                {
                    bestElapsed = sw.ElapsedMilliseconds;
                    bestDegree = degree;
                }
            }
            catch (OperationCanceledException)
            {
                CleanupTempDir(tempDir);
                throw;
            }
            catch (Exception ex)
            {
                CoreLog.Info($"FindOptimalDegreeAsync: degree={degree} failed: {ex.Message}");
                // 本配置失败跳过，继续下一配置
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        CoreLog.Exit();
        return bestDegree; // -1 = 全部失败
    }

    /// <summary>
    /// 按压缩包内条目顺序累加文件大小到 ~50MB 选样本。
    /// 至少 MinSampleFiles 个（不足则全部取），最多 MaxSampleFiles 个。
    /// </summary>
    private static List<string> SelectSampleEntries(IReadOnlyList<ArchiveItem> entries)
    {
        var files = entries.Where(e => !e.IsDirectory).ToList();
        var result = new List<string>();
        long accumulated = 0;

        foreach (var entry in files)
        {
            if (result.Count >= MaxSampleFiles) break;
            result.Add(entry.FullPath ?? entry.Name);
            accumulated += entry.Size;
            if (accumulated >= SampleTargetBytes && result.Count >= MinSampleFiles) break;
        }

        // 文件太少时全取
        if (result.Count < MinSampleFiles)
            result = files.Take(MaxSampleFiles).Select(e => e.FullPath ?? e.Name).ToList();

        return result;
    }

    private static void CleanupTempDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            CoreLog.Info($"CleanupTempDir: failed to delete {dir}: {ex.Message}");
        }
    }
}
```

### Step 4: 运行测试验证通过

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter ParallelDegreeOptimizerTests`
Expected: 全部 PASS。

### Step 5: 提交

```powershell
git add src/MantisZip.Core/Services/ParallelDegreeOptimizer.cs tests/MantisZip.Tests/Services/ParallelDegreeOptimizerTests.cs
git commit -m "feat(core): 新增大包预测试 ParallelDegreeOptimizer（样本解压计时选优，失败回退）"
```

---

## Task 3: Core — ParallelDegreeResolver（5 步决策链）

**Files:**
- Create: `src/MantisZip.Core/Services/ParallelDegreeResolver.cs`
- Create: `tests/MantisZip.Tests/Services/ParallelDegreeResolverTests.cs`

### Step 1: 写失败测试

创建 `tests/MantisZip.Tests/Services/ParallelDegreeResolverTests.cs`：

```csharp
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Services;

public class ParallelDegreeResolverTests
{
    private static ParallelExtractSettings DefaultSettings => new()
    {
        EnableParallel = true,
        SmartParallel = true,
        SmartSsdDegree = 12,
        SmartHddDegree = 8,
        LargeArchivePreTest = true,
        ManualDegree = Environment.ProcessorCount
    };

    // ── 步骤 1：调用方指定 ──

    [Fact]
    public void Resolve_CallerOverride_WinsOverEverything()
    {
        var s = DefaultSettings with { EnableParallel = false }; // 全局关也应被覆盖
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: 6, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(6, result);
    }

    [Fact]
    public void Resolve_CallerOverride_Zero_NotAnOverride()
    {
        var s = DefaultSettings;
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: 0, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(12, result); // 落到智能 SSD
    }

    // ── 步骤 2：全局关 → 串行 ──

    [Fact]
    public void Resolve_GlobalDisabled_Returns1()
    {
        var s = DefaultSettings with { EnableParallel = false };
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(1, result);
    }

    // ── 步骤 3：智能关 → 手动值 / ProcessorCount ──

    [Fact]
    public void Resolve_SmartOff_ManualDegree6_Returns6()
    {
        var s = DefaultSettings with { SmartParallel = false, ManualDegree = 6 };
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(6, result);
    }

    [Fact]
    public void Resolve_SmartOff_ManualDegree0_ReturnsProcessorCount()
    {
        var s = DefaultSettings with { SmartParallel = false, ManualDegree = 0 };
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(Environment.ProcessorCount, result);
    }

    // ── 步骤 5（同步路径，无预测试触发时）：磁盘类型默认值 ──

    [Fact]
    public void Resolve_SmartOn_Ssd_ReturnsSmartSsdDegree()
    {
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, DefaultSettings, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(12, result);
    }

    [Fact]
    public void Resolve_SmartOn_Hdd_ReturnsSmartHddDegree()
    {
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, DefaultSettings, DiskType.Hdd, archiveSize: 0);
        Assert.Equal(8, result);
    }

    [Fact]
    public void Resolve_SmartOn_Unknown_ReturnsMinProcessorCount12()
    {
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, DefaultSettings, DiskType.Unknown, archiveSize: 0);
        Assert.Equal(Math.Min(Environment.ProcessorCount, 12), result);
    }

    // ── 越界 clamp ──

    [Fact]
    public void Resolve_SsdDegreeOutOfRange_ClampsTo16()
    {
        var s = DefaultSettings with { SmartSsdDegree = 99 };
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: null, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(16, result);
    }

    [Fact]
    public void Resolve_CallerOverrideOutOfRange_ClampsTo16()
    {
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: 99, DefaultSettings, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(16, result);
    }

    [Fact]
    public void Resolve_CallerOverrideNegative_ClampsTo1()
    {
        // 负数不视为 override（<=0 落入决策链），但最终值仍 clamp
        var s = DefaultSettings with { EnableParallel = false };
        var result = ParallelDegreeResolver.Resolve(
            callerDegree: -5, s, DiskType.Ssd, archiveSize: 0);
        Assert.Equal(1, result); // 全局关
    }
}
```

### Step 2: 运行测试确认失败

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter ParallelDegreeResolverTests`
Expected: 编译失败 `ParallelDegreeResolver` / `ParallelExtractSettings` 未定义。

### Step 3: 实现 ParallelDegreeResolver

创建 `src/MantisZip.Core/Services/ParallelDegreeResolver.cs`：

```csharp
using MantisZip.Core.Utils;

namespace MantisZip.Core.Services;

/// <summary>
/// 并行解压设置快照（Core 层不依赖 UI 的 AppSettings，由调用方传入）。
/// </summary>
public record ParallelExtractSettings
{
    /// <summary>全局并行开关：false = 始终串行（线程数=1）。</summary>
    public bool EnableParallel { get; init; } = true;

    /// <summary>智能并行度开关：true = 按磁盘类型/预测试选线程数。</summary>
    public bool SmartParallel { get; init; } = true;

    /// <summary>智能模式 SSD 线程数（1-16）。</summary>
    public int SmartSsdDegree { get; init; } = 12;

    /// <summary>智能模式 HDD 线程数（1-16）。</summary>
    public int SmartHddDegree { get; init; } = 8;

    /// <summary>大包预测试开关（仅智能模式参与决策链第 4 步）。</summary>
    public bool LargeArchivePreTest { get; init; } = true;

    /// <summary>手动线程数（智能关闭时使用；0 = ProcessorCount）。</summary>
    public int ManualDegree { get; init; }
}

/// <summary>
/// 并行解压线程数决策链（5 步）：
/// 1. callerDegree &gt; 0 → 直接使用（调用方覆盖）
/// 2. EnableParallel = false → 1（串行）
/// 3. SmartParallel = false → ManualDegree 或 ProcessorCount
/// 4. 大包 + 预测试开 → FindOptimalDegreeAsync（异步路径）
/// 5. 磁盘类型默认值（Ssd→SmartSsdDegree / Hdd→SmartHddDegree / Unknown→min(CPU,12)）
/// </summary>
public static class ParallelDegreeResolver
{
    /// <summary>未知磁盘回退上限。</summary>
    private const int UnknownDiskCap = 12;

    /// <summary>线程数上限。</summary>
    private const int MaxDegree = 16;

    /// <summary>
    /// 同步决策（步骤 1-3 + 5；步骤 4 由异步封装在满足条件时先执行预测试再落到本方法）。
    /// 供测试与非大包路径使用。
    /// </summary>
    /// <param name="callerDegree">调用方覆盖（null 或 ≤0 = 无覆盖）。</param>
    /// <param name="settings">并行解压设置快照。</param>
    /// <param name="diskType">目标磁盘类型（步骤 5 输入）。</param>
    /// <param name="archiveSize">压缩包大小（当前同步路径不触发预测试，仅记录；预测试由 ResolveAsync 处理）。</param>
    public static int Resolve(
        int? callerDegree,
        ParallelExtractSettings settings,
        DiskType diskType,
        long archiveSize = 0)
    {
        // 步骤 1：调用方明确指定（最高优先级）
        if (callerDegree is > 0)
            return Math.Clamp(callerDegree.Value, 1, MaxDegree);

        // 步骤 2：全局关 → 串行
        if (!settings.EnableParallel)
            return 1;

        // 步骤 3：智能关 → 手动值或 ProcessorCount
        if (!settings.SmartParallel)
        {
            var manual = settings.ManualDegree > 0 ? settings.ManualDegree : Environment.ProcessorCount;
            return Math.Clamp(manual, 1, MaxDegree);
        }

        // 步骤 5：磁盘类型默认值（步骤 4 预测试不适用/失败时也落到这里）
        return diskType switch
        {
            DiskType.Ssd => Math.Clamp(settings.SmartSsdDegree, 1, MaxDegree),
            DiskType.Hdd => Math.Clamp(settings.SmartHddDegree, 1, MaxDegree),
            _ => Math.Clamp(Math.Min(Environment.ProcessorCount, UnknownDiskCap), 1, MaxDegree)
        };
    }

    /// <summary>
    /// 异步决策：步骤 1-3 同步短路；满足预测试条件（步骤 4）时先执行预测试，
    /// 预测试失败/不适用回退磁盘类型默认值（步骤 5）。
    /// </summary>
    /// <param name="callerDegree">调用方覆盖（解压对话框单次指定）。</param>
    /// <param name="settings">设置快照。</param>
    /// <param name="archivePath">压缩包路径（预测试列目录/取样本）。</param>
    /// <param name="destinationPath">目标目录（磁盘检测优先用它——写盘是瓶颈）。</param>
    /// <param name="password">密码（预测试列加密包需要）。</param>
    /// <param name="engineSupportsParallel">引擎是否支持并行（仅 ZipEngine 为 true）。</param>
    /// <param name="progress">进度回调（预测试期间上报状态文案）。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<int> ResolveAsync(
        int? callerDegree,
        ParallelExtractSettings settings,
        string archivePath,
        string destinationPath,
        string? password,
        bool engineSupportsParallel,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct)
    {
        // 步骤 1-3 同步短路（不进入 I/O 路径）
        if (callerDegree is > 0)
            return Math.Clamp(callerDegree.Value, 1, MaxDegree);
        if (!settings.EnableParallel)
            return 1;
        if (!settings.SmartParallel)
        {
            var manual = settings.ManualDegree > 0 ? settings.ManualDegree : Environment.ProcessorCount;
            return Math.Clamp(manual, 1, MaxDegree);
        }

        // 磁盘检测（优先目标目录——写盘是瓶颈；不可用回退压缩包路径）
        var diskType = DiskTypeDetector.GetDiskType(destinationPath);
        if (diskType == DiskType.Unknown)
            diskType = DiskTypeDetector.GetDiskType(archivePath);

        // 步骤 4：大包预测试
        long archiveSize = 0;
        try { archiveSize = new FileInfo(archivePath).Length; } catch { /* 文件不可读时按 0 处理 */ }

        if (ParallelDegreeOptimizer.ShouldPreTest(archiveSize, settings.LargeArchivePreTest, engineSupportsParallel))
        {
            try
            {
                // 上报预测试状态（不显示百分比，CurrentFile 携带状态文案）
                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = "STATUS_TESTING_PARALLEL_DEGREE", // 调用方可替换为本地化文案
                    PercentComplete = 0
                });

                var optimal = await ParallelDegreeOptimizer.FindOptimalDegreeAsync(
                    archivePath, destinationPath, password, progress, ct);
                if (optimal > 0)
                {
                    CoreLog.Info($"ParallelDegreeResolver: pre-test optimal degree = {optimal}");
                    return Math.Clamp(optimal, 1, MaxDegree);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // 用户取消直接向上传播
            }
            catch (Exception ex)
            {
                CoreLog.Info($"ParallelDegreeResolver: pre-test failed, fallback to disk type: {ex.Message}");
            }
        }

        // 步骤 5：磁盘类型默认值
        return Resolve(callerDegree: null, settings, diskType, archiveSize);
    }
}
```

### Step 4: 运行测试验证通过

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter ParallelDegreeResolverTests`
Expected: 全部 PASS。

### Step 5: 提交

```powershell
git add src/MantisZip.Core/Services/ParallelDegreeResolver.cs tests/MantisZip.Tests/Services/ParallelDegreeResolverTests.cs
git commit -m "feat(core): 新增 ParallelDegreeResolver 5 步线程数决策链（调用方覆盖/全局关/智能/预测试/磁盘默认）"
```

---

## Task 4: UI — AppSettings 新增 5 属性

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Models/AppSettings.cs:78`（`ParallelExtractDegree` 之后）

### Step 1: 添加属性

在 `ParallelExtractDegree`（约 line 78）与 `ParallelCompressDegree`（约 line 81）之间插入：

```csharp
    /// <summary>并行解压全局开关：false = 始终串行（线程数=1）。</summary>
    public bool EnableParallelExtract { get; set; } = true;

    /// <summary>智能并行度：true = 按磁盘类型/预测试自动选择线程数。</summary>
    public bool SmartParallelDegree { get; set; } = true;

    /// <summary>智能模式下 SSD 的线程数（1-16）。</summary>
    public int SmartSsdDegree { get; set; } = 12;

    /// <summary>智能模式下 HDD 的线程数（1-16）。</summary>
    public int SmartHddDegree { get; set; } = 8;

    /// <summary>大包预测试：≥1GB 压缩包先测最优线程数（仅智能模式生效）。</summary>
    public bool LargeArchivePreTest { get; set; } = true;
```

### Step 2: 加载时 clamp（边界情况 #8）

在 `AppSettings.Load()` 方法内（`return settings;` 之前）添加：

```csharp
            // 并行解压智能线程数越界 clamp 到 [1,16]（边界情况 #8）
            settings.SmartSsdDegree = Math.Clamp(settings.SmartSsdDegree, 1, 16);
            settings.SmartHddDegree = Math.Clamp(settings.SmartHddDegree, 1, 16);
```

### Step 3: 构建验证

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 错误（可有 NU1903/CA1416 已知警告）。

### Step 4: 提交

```powershell
git add src/MantisZip.UI.Avalonia/Models/AppSettings.cs
git commit -m "feat(avalonia): AppSettings 新增并行解压 5 设置（全局开关/智能/SSD/HDD线程数/预测试）"
```

---

## Task 5: UI — 本地化 key（15 key × 3 语）

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json`
- Modify: `src/MantisZip.UI.Avalonia/Localization/strings.en.json`
- Modify: `src/MantisZip.UI.Avalonia/Localization/strings.zh-TW.json`

### Step 1: 三语文件各插入 15 个 key

每个文件在开头 `{` 之后插入（维持 UTF-8 无 BOM + CRLF + 2 空格缩进；key 不排序）：

**zh-CN.json：**
```json
  "Settings_EnableParallelExtract": "并行解压（关闭则始终串行解压）",
  "Settings_SmartParallelDegree": "智能并行度（根据磁盘类型选择线程数）",
  "Settings_SmartParallelDegreeOff": "智能并行度（关闭 → 使用下方固定线程数）",
  "Settings_SmartSsdLabel": "SSD 线程数:",
  "Settings_SmartHddLabel": "HDD 线程数:",
  "Settings_ManualDegreeLabel": "线程数:",
  "Settings_ManualDegreeHint": "1=串行，1-16",
  "Settings_LargeArchivePreTest": "大包预测试（≥1GB 时自动测试最优线程数）",
  "Settings_ParallelHelpButton": "并行解压说明",
  "ParallelHelp_Title": "并行解压说明",
  "ParallelHelp_EnableSection": "并行解压允许同时用多个线程解压不同文件。关闭后始终串行解压（最兼容，速度最慢）。",
  "ParallelHelp_SmartSection": "智能并行度根据目标磁盘类型选择线程数：SSD 用上方 SSD 线程数（默认 12），HDD 用 HDD 线程数（默认 8），未知磁盘取 CPU 核数与 12 的较小值。关闭智能后可设固定线程数。",
  "ParallelHelp_PreTestSection": "大包预测试仅在智能并行度开启时可用。解压 ≥1GB 压缩包时，先用 1-2 秒在目标目录测试不同线程数的速度（临时解压约 50MB 数据后删除），选择最优配置再正式解压。关闭可节省这 1-2 秒。",
  "Status_TestingParallelDegree": "正在测试最优线程数...",
  "Extract_ParallelDegreeAutoHint": "0=自动（智能/设置），1=串行，2-16=线程数"
```

**en.json：**
```json
  "Settings_EnableParallelExtract": "Parallel extract (off = always serial)",
  "Settings_SmartParallelDegree": "Smart parallel degree (pick threads by disk type)",
  "Settings_SmartParallelDegreeOff": "Smart parallel degree (off = use fixed thread count below)",
  "Settings_SmartSsdLabel": "SSD threads:",
  "Settings_SmartHddLabel": "HDD threads:",
  "Settings_ManualDegreeLabel": "Threads:",
  "Settings_ManualDegreeHint": "1=serial, 1-16",
  "Settings_LargeArchivePreTest": "Large archive pre-test (auto-test optimal threads for ≥1GB)",
  "Settings_ParallelHelpButton": "Parallel extract help",
  "ParallelHelp_Title": "Parallel Extract Help",
  "ParallelHelp_EnableSection": "Parallel extract uses multiple threads to extract different files at once. Disable for always-serial extraction (most compatible, slowest).",
  "ParallelHelp_SmartSection": "Smart mode picks thread count by disk type: SSD uses the SSD thread count (default 12), HDD uses the HDD thread count (default 8), unknown disks use min(CPU cores, 12). Disable smart to set a fixed count.",
  "ParallelHelp_PreTestSection": "Large archive pre-test is only available when smart parallel degree is on. For ≥1GB archives, it briefly tests different thread counts (~50MB temp extraction, then deleted) to find the optimal configuration before full extraction. Disable to save 1-2 seconds.",
  "Status_TestingParallelDegree": "Testing optimal thread count...",
  "Extract_ParallelDegreeAutoHint": "0=auto (smart/settings), 1=serial, 2-16=threads"
```

**zh-TW.json：**
```json
  "Settings_EnableParallelExtract": "並行解壓（關閉則始終串行解壓）",
  "Settings_SmartParallelDegree": "智能並行度（根據磁碟類型選擇執行緒數）",
  "Settings_SmartParallelDegreeOff": "智能並行度（關閉 → 使用下方固定執行緒數）",
  "Settings_SmartSsdLabel": "SSD 執行緒數:",
  "Settings_SmartHddLabel": "HDD 執行緒數:",
  "Settings_ManualDegreeLabel": "執行緒數:",
  "Settings_ManualDegreeHint": "1=串行，1-16",
  "Settings_LargeArchivePreTest": "大包預測試（≥1GB 時自動測試最優執行緒數）",
  "Settings_ParallelHelpButton": "並行解壓說明",
  "ParallelHelp_Title": "並行解壓說明",
  "ParallelHelp_EnableSection": "並行解壓允許同時用多個執行緒解壓不同檔案。關閉後始終串行解壓（最相容，速度最慢）。",
  "ParallelHelp_SmartSection": "智能並行度根據目標磁碟類型選擇執行緒數：SSD 用上方 SSD 執行緒數（預設 12），HDD 用 HDD 執行緒數（預設 8），未知磁碟取 CPU 核心數與 12 的較小值。關閉智能後可設固定執行緒數。",
  "ParallelHelp_PreTestSection": "大包預測試僅在智能並行度開啟時可用。解壓 ≥1GB 壓縮包時，先用 1-2 秒在目標目錄測試不同執行緒數的速度（臨時解壓約 50MB 資料後刪除），選擇最優配置再正式解壓。關閉可節省這 1-2 秒。",
  "Status_TestingParallelDegree": "正在測試最優執行緒數...",
  "Extract_ParallelDegreeAutoHint": "0=自動（智能/設定），1=串行，2-16=執行緒數"
```

注意：三语均为 15 个 key，key 集必须完全一致（`AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 会校验）。

### Step 2: 运行三语 key 集一致性测试

Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter AllThreeLanguages_HaveSameKeySet`
Expected: PASS。

### Step 3: 构建验证

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 错误。

### Step 4: 提交

```powershell
git add src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json src/MantisZip.UI.Avalonia/Localization/strings.en.json src/MantisZip.UI.Avalonia/Localization/strings.zh-TW.json
git commit -m "feat(avalonia): 并行解压本地化 key 三语成对（15 key）"
```

---

## Task 6: UI — ParallelExtractHelpDialog 帮助弹窗

**Files:**
- Create: `src/MantisZip.UI.Avalonia/Dialogs/ParallelExtractHelpDialog.axaml`
- Create: `src/MantisZip.UI.Avalonia/Dialogs/ParallelExtractHelpDialog.axaml.cs`

### Step 1: 创建 AXAML（规则 14：全中文注释）

创建 `src/MantisZip.UI.Avalonia/Dialogs/ParallelExtractHelpDialog.axaml`：

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="MantisZip.UI.Avalonia.Dialogs.ParallelExtractHelpDialog"
        x:CompileBindings="False"
        Title="{Binding WinTitle}"
        Width="520"
        Height="460"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource ThemeWindowBgBrush}"
        Padding="{DynamicResource SpacingMdThk}">
  <DockPanel>
    <!-- 关闭按钮：固定在底部，不随 ScrollViewer 滚动 -->
    <Button Content="{Binding CloseText}"
            DockPanel.Dock="Bottom"
            HorizontalAlignment="Right"
            Width="100"
            Height="{DynamicResource ControlHeight}"
            IsDefault="True"
            Click="OnCloseClick"
            Background="{DynamicResource ThemeAccentBrush}"
            Foreground="White"
            Margin="0,8,0,0" />

    <!-- 内容区：3 章节可滚动 -->
    <ScrollViewer VerticalScrollBarVisibility="Auto"
                  TextElement.Foreground="{DynamicResource ThemeTextPrimaryBrush}">
      <StackPanel Margin="20,20,20,0" Spacing="{DynamicResource SpacingMd}">

        <!-- 标题 -->
        <TextBlock Text="{Binding WinTitle}"
                   FontSize="18"
                   FontWeight="Bold" />

        <!-- 章节 1：并行解压（全局开关） -->
        <Border Background="{DynamicResource ThemeSurfaceBgBrush}"
                CornerRadius="{DynamicResource BorderRadius}"
                Padding="14"
                BorderBrush="{DynamicResource ThemeBorderBrush}"
                BorderThickness="1">
          <StackPanel Spacing="8">
            <TextBlock Text="并行解压"
                       FontWeight="SemiBold"
                       FontSize="14" />
            <TextBlock Text="{Binding EnableSectionText}"
                       TextWrapping="Wrap"
                       LineHeight="20" />
          </StackPanel>
        </Border>

        <!-- 章节 2：智能并行度 -->
        <Border Background="{DynamicResource ThemeSurfaceBgBrush}"
                CornerRadius="{DynamicResource BorderRadius}"
                Padding="14"
                BorderBrush="{DynamicResource ThemeBorderBrush}"
                BorderThickness="1">
          <StackPanel Spacing="8">
            <TextBlock Text="智能并行度"
                       FontWeight="SemiBold"
                       FontSize="14" />
            <TextBlock Text="{Binding SmartSectionText}"
                       TextWrapping="Wrap"
                       LineHeight="20" />
          </StackPanel>
        </Border>

        <!-- 章节 3：大包预测试（仅智能模式可用） -->
        <Border Background="{DynamicResource ThemeSurfaceBgBrush}"
                CornerRadius="{DynamicResource BorderRadius}"
                Padding="14"
                BorderBrush="{DynamicResource ThemeBorderBrush}"
                BorderThickness="1">
          <StackPanel Spacing="8">
            <TextBlock Text="大包预测试"
                       FontWeight="SemiBold"
                       FontSize="14" />
            <TextBlock Text="{Binding PreTestSectionText}"
                       TextWrapping="Wrap"
                       LineHeight="20" />
          </StackPanel>
        </Border>

      </StackPanel>
    </ScrollViewer>
  </DockPanel>
</Window>
```

### Step 2: 创建 code-behind

创建 `src/MantisZip.UI.Avalonia/Dialogs/ParallelExtractHelpDialog.axaml.cs`：

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Localization;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 并行解压说明帮助弹窗：3 章节（并行解压/智能并行度/大包预测试）。
/// 从设置窗口解压 Tab 的 [?] 按钮打开。
/// </summary>
public partial class ParallelExtractHelpDialog : Window
{
    public ParallelExtractHelpDialog()
    {
        InitializeComponent();
        WinTitle = LocalizationManager.T("ParallelHelp_Title");
        EnableSectionText = LocalizationManager.T("ParallelHelp_EnableSection");
        SmartSectionText = LocalizationManager.T("ParallelHelp_SmartSection");
        PreTestSectionText = LocalizationManager.T("ParallelHelp_PreTestSection");
        CloseText = LocalizationManager.T("Common_Close");
        DataContext = this;
    }

    public string WinTitle { get; }
    public string EnableSectionText { get; }
    public string SmartSectionText { get; }
    public string PreTestSectionText { get; }
    public string CloseText { get; }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
```

注意：`Common_Close` key 需要确认已存在于三语文件。如不存在，改用已有关闭 key（grep 确认）或使用 `Settings_Cancel`。

### Step 3: 构建验证

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 错误。

### Step 4: 提交

```powershell
git add src/MantisZip.UI.Avalonia/Dialogs/ParallelExtractHelpDialog.axaml src/MantisZip.UI.Avalonia/Dialogs/ParallelExtractHelpDialog.axaml.cs
git commit -m "feat(avalonia): 新增 ParallelExtractHelpDialog 并行解压帮助弹窗（3 章节）"
```

---

## Task 7: UI — SettingsWindowViewModel 新增属性/命令/本地化

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs`

### Step 1: 新增 [ObservableProperty] 字段

在 `DoubleClickOpenThreshold` 属性（约 line 213）之后插入：

```csharp
    // ── Parallel Extract ──
    [ObservableProperty]
    private bool _enableParallelExtract;

    [ObservableProperty]
    private bool _smartParallelDegree;

    [ObservableProperty]
    private int _smartSsdDegree = 12;

    [ObservableProperty]
    private int _smartHddDegree = 8;

    [ObservableProperty]
    private bool _largeArchivePreTest;

    [RelayCommand]
    private void ShowParallelHelp()
    {
        ShowParallelHelpRequested?.Invoke();
    }

    /// <summary>帮助弹窗打开请求（由 SettingsWindow code-behind 订阅并弹窗）。</summary>
    public event Action? ShowParallelHelpRequested;
```

### Step 2: 构造函数加载

在构造函数中 `_doubleClickOpenThreshold = _settings.DoubleClickOpenThreshold;`（约 line 975）之后插入：

```csharp
        // Parallel Extract
        _enableParallelExtract = _settings.EnableParallelExtract;
        _smartParallelDegree = _settings.SmartParallelDegree;
        _smartSsdDegree = _settings.SmartSsdDegree;
        _smartHddDegree = _settings.SmartHddDegree;
        _largeArchivePreTest = _settings.LargeArchivePreTest;
```

### Step 3: SaveSettings 写回

在 `SaveSettings()` 中 `_settings.DoubleClickOpenThreshold = DoubleClickOpenThreshold;`（约 line 1506）之后插入：

```csharp
        // Parallel Extract
        _settings.EnableParallelExtract = EnableParallelExtract;
        _settings.SmartParallelDegree = SmartParallelDegree;
        _settings.SmartSsdDegree = Math.Clamp(SmartSsdDegree, 1, 16);
        _settings.SmartHddDegree = Math.Clamp(SmartHddDegree, 1, 16);
        _settings.LargeArchivePreTest = LargeArchivePreTest;
```

### Step 4: 本地化文本属性

在 `ExtractOpenFolderAfterText` 附近（约 line 796）插入：

```csharp
    public string EnableParallelExtractText => LocalizationManager.T("Settings_EnableParallelExtract");
    public string SmartParallelDegreeText => SmartParallelDegree
        ? LocalizationManager.T("Settings_SmartParallelDegree")
        : LocalizationManager.T("Settings_SmartParallelDegreeOff");
    public string SmartSsdLabelText => LocalizationManager.T("Settings_SmartSsdLabel");
    public string SmartHddLabelText => LocalizationManager.T("Settings_SmartHddLabel");
    public string LargeArchivePreTestText => LocalizationManager.T("Settings_LargeArchivePreTest");
    public string ParallelHelpButtonText => LocalizationManager.T("Settings_ParallelHelpButton");
```

### Step 5: SmartParallelDegree 变化时通知 SmartParallelDegreeText

利用 AGENTS.md 的集中通知模式，在 `OnSmartParallelDegreeChanged` 中通知文本属性：

```csharp
    partial void OnSmartParallelDegreeChanged(bool value)
    {
        OnPropertyChanged(nameof(SmartParallelDegreeText));
    }
```

### Step 6: OnCultureChanged 追加通知

在 `OnCultureChanged()` 的 `OnPropertyChanged(...)` 列表（约 line 1330 起）追加：

```csharp
        OnPropertyChanged(nameof(EnableParallelExtractText));
        OnPropertyChanged(nameof(SmartParallelDegreeText));
        OnPropertyChanged(nameof(SmartSsdLabelText));
        OnPropertyChanged(nameof(SmartHddLabelText));
        OnPropertyChanged(nameof(LargeArchivePreTestText));
        OnPropertyChanged(nameof(ParallelHelpButtonText));
```

### Step 7: 构建验证

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 错误。

### Step 8: 提交

```powershell
git add src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs
git commit -m "feat(avalonia): SettingsWindowViewModel 新增并行解压 5 属性 + 帮助命令 + 本地化"
```

---

## Task 8: UI — SettingsWindow.axaml 解压 Tab 新增卡片

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml`（解压 Tab，约 line 486-487 `</Border></StackPanel>` 前）
- Modify: `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml.cs`（[?] 按钮 Click 处理 + 事件订阅）

### Step 1: 插入并行解压卡片

在解压 Tab 内最后一张卡片之后（`</Border>` 和 `</StackPanel>` 闭合之前，约 line 486-487）插入：

```xml
            <!-- ═══ 并行解压卡片（全局开关 + 智能并行度 + SSD/HDD 线程数 + 预测试 + 帮助） ═══ -->
            <Border Background="{DynamicResource ThemeSurfaceBgBrush}"
                    CornerRadius="{DynamicResource BorderRadius}"
                    Padding="{DynamicResource DialogPadding}"
                    BorderBrush="{DynamicResource ThemeBorderBrush}"
                    BorderThickness="1">
              <StackPanel Spacing="{DynamicResource SpacingSm}">

                <!-- 卡片标题行：标题 + [?] 帮助按钮 -->
                <Grid ColumnDefinitions="*,Auto">
                  <TextBlock Text="{Binding ExtractParallelExtractTitle}"
                             FontWeight="Bold"
                             FontSize="15"
                             Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
                  <Button Grid.Column="1"
                          Classes="ToolbarIcon"
                          Content="?"
                          Command="{Binding ShowParallelHelpCommand}"
                          ToolTip.Tip="{Binding ParallelHelpButtonText}"
                          Background="{DynamicResource ThemeHeaderBgBrush}"
                          Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
                </Grid>

                <!-- 全局并行开关：关闭则始终串行 -->
                <CheckBox Content="{Binding EnableParallelExtractText}"
                          IsChecked="{Binding EnableParallelExtract}"
                          Foreground="{DynamicResource ThemeTextPrimaryBrush}" />

                <!-- 智能并行度区域（全局开关关闭时隐藏，规则 6） -->
                <StackPanel Spacing="{DynamicResource SpacingSm}"
                            IsVisible="{Binding EnableParallelExtract}">
                  <CheckBox Content="{Binding SmartParallelDegreeText}"
                            IsChecked="{Binding SmartParallelDegree}"
                            Foreground="{DynamicResource ThemeTextPrimaryBrush}" />

                  <!-- 智能开启：SSD/HDD 双线程数输入 -->
                  <Grid ColumnDefinitions="Auto,*,Auto,*,"
                        IsVisible="{Binding SmartParallelDegree}"
                        Margin="24,0,0,0">
                    <TextBlock Text="{Binding SmartSsdLabelText}"
                               VerticalAlignment="Center"
                               Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
                    <NumericUpDown Grid.Column="1"
                                   Minimum="1"
                                   Maximum="16"
                                   Increment="1"
                                   Value="{Binding SmartSsdDegree}"
                                   Width="80"
                                   HorizontalAlignment="Left" />
                    <TextBlock Grid.Column="2"
                               Text="{Binding SmartHddLabelText}"
                               VerticalAlignment="Center"
                               Margin="12,0,0,0"
                               Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
                    <NumericUpDown Grid.Column="3"
                                   Minimum="1"
                                   Maximum="16"
                                   Increment="1"
                                   Value="{Binding SmartHddDegree}"
                                   Width="80"
                                   HorizontalAlignment="Left" />
                  </Grid>

                  <!-- 智能关闭：固定线程数（0=ProcessorCount，解压对话框可覆盖） -->
                  <!-- 注：此卡片内不重复固定线程数输入——解压对话框 NumericUpDown 已提供单次覆盖；
                       智能关闭时 resolver 用 AppSettings.ParallelExtractDegree（现有设置项） -->
                </StackPanel>

                <!-- 大包预测试开关（仅智能模式显示，规则 6） -->
                <CheckBox Content="{Binding LargeArchivePreTestText}"
                          IsChecked="{Binding LargeArchivePreTest}"
                          IsVisible="{Binding EnableParallelExtract}"
                          Foreground="{DynamicResource ThemeTextPrimaryBrush}"
                          Margin="24,4,0,0" />

              </StackPanel>
            </Border>
```

注意：需要在 ViewModel 中补一个 `ExtractParallelExtractTitle` 属性（或复用已有设置分组标题 key）。检查解压 Tab 其它卡片的标题模式（如 `ExtractSettingsTitle`），保持一致——如已有通用「解压」标题则用它，新卡片不需要额外标题行时可省略标题 TextBlock。

### Step 2: code-behind 订阅帮助弹窗

在 `SettingsWindow.axaml.cs` 构造函数或 `OnDataContextChanged` 中：

```csharp
        // 并行解压帮助弹窗：VM 发起请求 → code-behind 弹窗（规则：对话框经回调委托与 View 解耦）
        if (DataContext is SettingsWindowViewModel vm)
        {
            vm.ShowParallelHelpRequested += () =>
            {
                var dlg = new ParallelExtractHelpDialog();
                dlg.ShowDialog(this);
            };
        }
```

### Step 3: 构建验证

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 错误。

### Step 4: 提交

```powershell
git add src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml.cs
git commit -m "feat(avalonia): 设置窗口解压 Tab 新增并行解压卡片（开关/智能/SSD/HDD/预测试/帮助）"
```

---

## Task 9: UI — 接线 resolver 进解压流程

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs`（`ExtractAsync` 中 `ArchiveOptions` 赋值处，约 line 184）
- Modify: `src/MantisZip.UI.Avalonia/Services/SelectedItemsExtractService.cs`（约 line 58）

### Step 1: ExtractFlow.ExtractAsync 接线

在构造 `ArchiveOptions` 时（当前直接读 `AppSettings.ParallelExtractDegree` 的位置），替换为 resolver 调用：

```csharp
        // 智能并行解压：5 步决策链（调用方覆盖 → 全局关 → 智能关 → 预测试 → 磁盘默认）
        var parallelSettings = new ParallelExtractSettings
        {
            EnableParallel = AppSettings.Load()?.EnableParallelExtract ?? true,
            SmartParallel = AppSettings.Load()?.SmartParallelDegree ?? true,
            SmartSsdDegree = AppSettings.Load()?.SmartSsdDegree ?? 12,
            SmartHddDegree = AppSettings.Load()?.SmartHddDegree ?? 8,
            LargeArchivePreTest = AppSettings.Load()?.LargeArchivePreTest ?? true,
            ManualDegree = AppSettings.Load()?.ParallelExtractDegree ?? 0
        };

        var resolvedDegree = await ParallelDegreeResolver.ResolveAsync(
            callerDegree: parallelDegree,          // 解压对话框单次指定（0/null = 自动）
            settings: parallelSettings,
            archivePath: archivePath,
            destinationPath: destinationPath,
            password: password,
            engineSupportsParallel: engine.SupportsParallelExtract,
            progress: progress,
            ct: ct);

        var options = new ArchiveOptions
        {
            // ... 其它现有字段 ...
            ParallelExtractDegree = resolvedDegree
        };
```

注意：`ExtractFlow.ExtractAsync` 的现有签名中如果已有 `parallelDegree` 参数（来自解压对话框），传给 `callerDegree`；若无此参数则传 `null`。`ResolveDisplayParallelDegree`（约 line 307-313）需同步更新——显示文案应反映 resolver 结果而非原始设置。

### Step 2: SelectedItemsExtractService 接线

在 `ExtractEntriesAsync` 中同样替换 `ParallelExtractDegree` 赋值：

```csharp
        var settings = AppSettings.Load();
        var resolvedDegree = await ParallelDegreeResolver.ResolveAsync(
            callerDegree: null,  // 选中条目解压无对话框覆盖
            settings: new ParallelExtractSettings
            {
                EnableParallel = settings?.EnableParallelExtract ?? true,
                SmartParallel = settings?.SmartParallelDegree ?? true,
                SmartSsdDegree = settings?.SmartSsdDegree ?? 12,
                SmartHddDegree = settings?.SmartHddDegree ?? 8,
                LargeArchivePreTest = settings?.LargeArchivePreTest ?? true,
                ManualDegree = settings?.ParallelExtractDegree ?? 0
            },
            archivePath: archivePath,
            destinationPath: destinationPath,
            password: password,
            engineSupportsParallel: engine.SupportsParallelExtract,
            progress: progress,
            ct: ct);
        options.ParallelExtractDegree = resolvedDegree;
```

### Step 3: 预测试状态文案本地化

`ParallelDegreeResolver.ResolveAsync` 内的 `CurrentFile = "STATUS_TESTING_PARALLEL_DEGREE"` 是占位 key。在 UI 层调用前或通过 progress 包装，将它替换为 `LocalizationManager.T("Status_TestingParallelDegree")`。最简做法：在 ExtractFlow 里传一个包装的 `IProgress<ArchiveProgress>`，拦截 key 替换为本地化文案后再转发。

### Step 4: 构建验证

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 错误。

### Step 5: 提交

```powershell
git add src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs src/MantisZip.UI.Avalonia/Services/SelectedItemsExtractService.cs
git commit -m "feat(avalonia): 解压流程接线 ParallelDegreeResolver（ExtractFlow + SelectedItemsExtractService）"
```

---

## Task 10: UI — 解压对话框线程数默认 0=自动

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs`（约 line 207）
- Modify: `src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml`（约 line 127-138）

### Step 1: ViewModel 默认值改 0

将 `_parallelExtractDegree` 的初始化从 `Environment.ProcessorCount` 改为 `0`（0 = 自动，走 resolver 决策链）：

```csharp
    [ObservableProperty]
    private int _parallelExtractDegree = 0; // 0 = 自动（resolver 决策）
```

同时更新提示文本属性（`ParallelExtractDegreeHint` 之类）的 key 指向 `Extract_ParallelDegreeAutoHint`。

### Step 2: AXAML NumericUpDown Minimum=0 + 新提示

```xml
<!-- 并行线程数：0=自动（智能/设置），1=串行，2-16=线程数 -->
<NumericUpDown Minimum="0"
               Maximum="16"
               Increment="1"
               Value="{Binding ParallelExtractDegree}" />
```

旧提示文案「(1=串行，默认=CPU核心数)」替换为 `{DynamicResource Extract_ParallelDegreeAutoHint}` 绑定（或 code-behind 属性 `=> LocalizationManager.T("Extract_ParallelDegreeAutoHint")`，视该窗口绑定模式而定）。

### Step 3: 提交

```powershell
git add src/MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml
git commit -m "feat(avalonia): 解压对话框线程数默认 0=自动，提示文案对齐 resolver 决策链"
```

---

## Task 11: 测试更新 + 全量验证

**Files:**
- Modify: `tests/MantisZip.UI.Avalonia.Tests/ExtractParallelDegreeWiringTests.cs`
- Run: 全量构建 + 测试

### Step 1: 更新源码守卫测试

现有守卫断言 `parallelExtractDegree ?? AppSettings.Load()?.ParallelExtractDegree ?? 0` 的源码模式已失效（接线改为 resolver）。更新为断言新模式存在：

```csharp
// 期望 ExtractFlow.cs / SelectedItemsExtractService.cs 中出现 resolver 调用
[Fact]
public void ExtractFlow_UsesParallelDegreeResolver()
{
    var src = File.ReadAllText(extractFlowPath);
    Assert.Contains("ParallelDegreeResolver.ResolveAsync", src);
}

[Fact]
public void SelectedItemsExtractService_UsesParallelDegreeResolver()
{
    var src = File.ReadAllText(servicePath);
    Assert.Contains("ParallelDegreeResolver.ResolveAsync", src);
}
```

同时保留对 `ParallelExtractDegree = resolvedDegree` 或等价赋值的断言。

### Step 2: Core 全量测试

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj`
Expected: 全部 PASS（新增 3 个测试类全绿）。

### Step 3: Avalonia 全量测试

Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`
Expected: 全部 PASS（含更新后的接线守卫 + 三语 key 一致性）。

### Step 4: Avalonia 构建验证

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 错误。

### Step 5: 提交

```powershell
git add tests/MantisZip.UI.Avalonia.Tests/ExtractParallelDegreeWiringTests.cs
git commit -m "test(avalonia): 更新并行解压接线守卫测试（断言 resolver 调用）"
```

---

## 完成后检查清单

- [ ] Core 三个组件测试全绿（DiskTypeDetector / Optimizer / Resolver）
- [ ] 三语本地化 key 集一致测试 PASS
- [ ] `dotnet build src\MantisZip.UI.Avalonia` 0 错误
- [ ] `dotnet build src\MantisZip.Core` 0 错误
- [ ] `dotnet test tests\MantisZip.Tests` 全绿
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests` 全绿
- [ ] 设置窗口 UI 手测：卡片显示/开关切换/SSD-HDD 输入/帮助弹窗/预测试随智能隐藏
- [ ] 解压对话框手测：线程数默认 0、提示文案正确
- [ ] 大包手测：≥1GB ZIP 解压触发预测试、进度显示「正在测试最优线程数...」、临时目录清理
- [ ] 规则 1：`docs/PLAN.md` 新增 P2 条目引用本计划
- [ ] 规则 3：提交前更新 `docs/PROGRESS.md` + `docs/progress-avalonia-detail.md`

## 预期警告（非阻塞）

- NU1903（SQLitePCLRaw 已知漏洞警告，历史存在）
- CA1416（平台特定 API 警告，DiskTypeDetector 的 P/Invoke 有 OperatingSystem.IsWindows 守卫）

## 关键参考（实现时必读）

- `AppSettings.cs:78` — 现有 `ParallelExtractDegree`（默认 `Environment.ProcessorCount`）
- `ExtractFlow.cs:184` — ArchiveOptions 构造处；`:307` — `ResolveDisplayParallelDegree`
- `SettingsWindowViewModel.cs:190-213` — 属性模式；`:794` — 文本属性；`:1330` — OnCultureChanged；`:1500` — Save
- `SettingsWindow.axaml:486-487` — 卡片插入点
- `ExtractSettingsWindow.axaml:127-138` — NumericUpDown
- `LogPrivacyHelpDialog.axaml` — 帮助弹窗模板（x:CompileBindings=False）
- `ArchiveFixtures.CreateMultiFileZipArchive(fileCount, fileSizeKB)` — 优化器测试夹具
