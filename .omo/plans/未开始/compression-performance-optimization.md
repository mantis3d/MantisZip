# 压缩/解压性能优化 (Compression Performance Optimization)

> 通过并行化和缓冲区优化，将 ZIP 解压速度提升 5-10 倍
> **状态**: 📋 待实施 | **阶段**: [⬜⬜⬜⬜⬜] (0/5)
> **当前缓冲区**: 256KB（262144），所有引擎均未优化

---

## 动机

用户反馈 MantisZip 的压缩/解压速度比主流软件慢。基准测试显示：

- **CPU 和硬盘都未跑满**：解压时 CPU 使用率 ~20%，磁盘 I/O ~15%
- **根本原因**：完全串行处理，未利用多核 CPU 和现代 SSD 的并行能力
- **测试数据**：100 个 1MB 文件的 ZIP 解压，串行 0.87s，并行 0.14s（**6.32x 提升**）

**目标**：在不改变用户体验的前提下，将 ZIP 解压速度提升 5-10 倍。

---

## 基准测试结果

### 测试环境
- CPU: 8 核
- 测试文件: 100 × 1MB = 100MB ZIP
- 缓冲区: 256KB（当前默认）

### 测试 1: 串行 vs 并行

```
串行解压: 1.33s 0.75s 0.53s  平均: 0.87s
并行解压: 0.14s 0.14s 0.13s  平均: 0.14s

【结果】并行比串行快 6.32x (532% 提升)
```

### 测试 2: 缓冲区大小对比（串行）

```
缓冲区 256KB: 1.47s (基准)
缓冲区 1024KB: 0.82s (+79%)
缓冲区 4096KB: 0.54s (+170%)
```

### 测试 3: 缓冲区大小对比（并行）

```
缓冲区 256KB: 0.54s (基准)
缓冲区 4096KB: 0.23s (+133%)
```

### 关键发现

| 方案 | 收益 | 状态 |
|------|------|------|
| SharpCompress 单实例并行 | ❌ 失败（ZlibException） | 线程不安全 |
| SharpCompress 多实例并行 | ✅ 6.32x | 已验证可行 |
| 缓冲区 256KB → 4MB | ✅ 1.7x | 已验证可行 |
| **组合优化** | **~10x** | 最佳方案 |

---

## 架构设计

### 优化层次

```
┌─────────────────────────────────────────────────────────────┐
│                    优化层次                                   │
├─────────────────────────────────────────────────────────────┤
│  Level 1: 缓冲区优化（简单）                                  │
│    - CopyBufferSize: 256KB → 4MB                             │
│    - 预期收益: 1.7x                                         │
│    - 实现难度: 低                                            │
├─────────────────────────────────────────────────────────────┤
│  Level 2: 并行解压（中等）                                    │
│    - 多实例并行：每个线程独立打开 archive                      │
│    - 预期收益: 6.3x                                         │
│    - 实现难度: 中等                                          │
├─────────────────────────────────────────────────────────────┤
│  Level 3: 流水线（复杂，可选）                                 │
│    - 解压与写入重叠                                          │
│    - 预期收益: 1.5-2x（叠加在 Level 2 之上）                 │
│    - 实现难度: 高                                            │
└─────────────────────────────────────────────────────────────┘
```

### 并行解压方案

#### 为什么 SharpCompress 单实例不安全？

```csharp
// ❌ 失败：共享 archive 实例
using var archive = ZipArchive.OpenArchive(path);
await Parallel.ForEachAsync(archive.Entries, async (entry, ct) => {
    using var stream = entry.OpenEntryStream(); // 内部状态冲突
});
// 报错: ZlibException: Bad state (invalid block type)
```

原因：SharpCompress 的 `IArchive` 内部维护共享的解压状态（字典、滑动窗口等），并发访问会破坏状态。

#### 为什么多实例并行安全？

```csharp
// ✅ 成功：每个线程独立打开
await Parallel.ForEachAsync(entryKeys, async (key, ct) => {
    using var archive = ZipArchive.OpenArchive(path); // 独立实例
    var entry = archive.Entries.First(e => e.Key == key);
    using var stream = entry.OpenEntryStream(); // 独立状态
});
```

每个线程有自己的 archive 实例，无共享状态，线程安全。

#### 代价分析

| 项目 | 串行 | 并行（8线程） | 说明 |
|------|------|-------------|------|
| 文件句柄 | 1 个 | 8 个 | Windows 默认限制 ~512，足够 |
| 内存占用 | ~10MB | ~80MB | 8 × 10MB（可接受） |
| I/O 模式 | 顺序读取 | 8 路并发读取 | SSD 随机读性能好 |
| ZIP 头部解析 | 1 次 | 8 次 | 一次性开销，影响小 |

---

## 任务清单

### Phase 1: 缓冲区优化（低风险，快速见效）

- [ ] **1. 修改 `CopyBufferSize` 常量**
  - 文件: `Core/Engines/ZipEngine.cs`, `TarGzEngine.cs`, `Utils/ZipBinaryRewriter.cs`
  - 改动: `262144` → `4194304` (256KB → 4MB)
  - 验证: `dotnet build` + 单元测试

### Phase 2: 并行解压（核心优化）

- [ ] **2. 设计并行解压接口**
  - 文件: `Core/Abstractions/IArchiveEngine.cs`
  - 新增: `bool SupportsParallelExtract` 属性
  - 目的: 让调用方知道引擎是否支持并行

- [ ] **3. 实现 ZipEngine 并行解压**
  - 文件: `Core/Engines/ZipEngine.cs`
  - 新增: `ExtractAsyncParallel` 方法
  - 逻辑:
    1. 先获取 entryKeys 列表（单线程）
    2. 创建所有目标目录（单线程）
    3. `Parallel.ForEachAsync` 并行解压
    4. 每个线程独立打开 archive 实例

- [ ] **4. 实现 ExtractFlow 并行调度**
  - 文件: `UI/Services/ExtractFlow.cs`
  - 逻辑: 根据 `SupportsParallelExtract` 选择串行/并行路径
  - 保留: 进度报告、冲突处理、取消支持

- [ ] **5. 并行度配置**
  - 文件: `UI/Models/AppSettings.cs`
  - 新增: `int ParallelExtractDegree` (默认 = Environment.ProcessorCount)
  - 范围: 1-16，用户可在设置中调整

### Phase 3: 并行压缩（可选）

- [ ] **6. 研究 SharpSevenZip 多线程压缩**
  - 目标: 验证 7z 压缩是否原生支持多线程
  - 方法: 测试 `compr.CustomParameters["mt"] = "on"`
  - 如果可行: 直接启用，无需代码改动

- [ ] **7. 实现 ZipEngine 分组并行压缩**
  - 思路: 将文件分成 N 组，每组压缩到临时文件，最后合并
  - 适用场景: 大量小文件（每个文件压缩独立）
  - 注意: 合并阶段需要串行写入输出 ZIP

### Phase 4: 测试与验证

- [ ] **8. 单元测试**
  - 文件: `tests/MantisZip.Tests/Engines/ParallelExtractTests.cs`
  - 测试用例:
    - 并行解压正确性（对比文件内容）
    - 并行解压线程安全（无异常）
    - 并行度 = 1 时退化为串行
    - 取消操作正常工作

- [ ] **9. 性能基准测试**
  - 场景 1: 100 个小文件（1MB × 100）
  - 场景 2: 10 个中等文件（10MB × 10）
  - 场景 3: 1 个大文件（100MB × 1）
  - 对比: 串行 vs 并行，256KB vs 4MB 缓冲区

- [ ] **10. 压力测试**
  - 1000 个文件 × 100KB
  - 内存占用监控（确保不 OOM）
  - 文件句柄泄漏检测

### Phase 5: 文档与清理

- [ ] **11. 更新 AGENTS.md**
  - 新增: 并行解压架构说明
  - 更新: 性能优化相关注意事项

- [ ] **12. 清理测试项目**
  - 删除: `C:\Users\render\AppData\Local\Temp\MantisZipBenchmark\`

---

## 改动范围

### 核心文件

| 文件 | 改动类型 | 说明 |
|------|---------|------|
| `Core/Engines/ZipEngine.cs` | 修改 + 新增 | 缓冲区 + 并行解压方法 |
| `Core/Engines/TarGzEngine.cs` | 修改 | 缓冲区优化 |
| `Core/Utils/ZipBinaryRewriter.cs` | 修改 | 缓冲区优化 |
| `Core/Abstractions/IArchiveEngine.cs` | 新增 | `SupportsParallelExtract` 属性 |
| `UI/Services/ExtractFlow.cs` | 修改 | 并行调度逻辑 |
| `UI/Models/AppSettings.cs` | 新增 | `ParallelExtractDegree` 设置 |
| `tests/MantisZip.Tests/Engines/ParallelExtractTests.cs` | 新增 | 单元测试 |

### 不涉及的文件

- `SevenZipEngine.cs`: 7z 并行需要 SharpSevenZip 原生支持，暂不改动
- `TarGzEngine.cs` 并行: TAR 格式是顺序流，无法并行，仅优化缓冲区

---

## 实现细节

### 1. 缓冲区优化

```csharp
// ZipEngine.cs, TarGzEngine.cs, ZipBinaryRewriter.cs
// 修改前
private const int CopyBufferSize = 262144; // 256KB

// 修改后
private const int CopyBufferSize = 4194304; // 4MB
```

**理由**:
- NVMe SSD 顺序写入需要 1MB+ 缓冲区才能跑满带宽
- 4MB 是收益/风险比最高的值（更大收益递减明显）
- 内存占用增加可控（每流 4MB，最多 8 流 = 32MB）

### 2. 并行解压实现

```csharp
// ZipEngine.cs
public async Task<ExtractResult> ExtractAsync(
    string archivePath, string destinationPath,
    string? password, IProgress<ArchiveProgress> progress,
    CancellationToken ct, ArchiveOptions? options)
{
    // 获取条目列表（单线程）
    var entryKeys = await GetEntryKeysAsync(archivePath, password);
    
    // 如果文件数少或并行度=1，走串行
    if (entryKeys.Count <= 1 || MaxParallelism <= 1)
        return await ExtractSequential(...);
    
    // 并行解压
    return await ExtractParallel(entryKeys, archivePath, destinationPath, 
        password, progress, ct, options);
}
```

#### 调度策略：大文件优先启动

**问题**：朴素并行中，大文件可能被排到队列末尾，导致处理到最后只剩一个线程在工作（拖后腿）。

**解决方案**：按文件大小**降序排列**，让大文件立即开始处理：

```csharp
private async Task<ExtractResult> ExtractParallel(
    IReadOnlyList<string> entryKeys,
    string archivePath, string destinationPath,
    string? password, IProgress<ArchiveProgress> progress,
    CancellationToken ct, ArchiveOptions? options)
{
    // 1. 创建所有目录（单线程）
    var dirs = entryKeys.Select(k => Path.GetDirectoryName(
        FileConflictHelper.GetSafePath(destinationPath, k)))
        .Where(d => !string.IsNullOrEmpty(d))
        .Distinct();
    foreach (var dir in dirs)
        Directory.CreateDirectory(dir!);
    
    // 2. 获取文件大小并按降序排列（大文件优先）
    var entriesWithSize = new List<(string Key, long Size)>();
    using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
    {
        foreach (var key in entryKeys)
        {
            var entry = archive.Entries.FirstOrDefault(e => e.Key == key);
            if (entry != null && !entry.IsDirectory)
                entriesWithSize.Add((key, entry.Size));
        }
    }
    
    // 按大小降序：大文件排前面，优先被线程池调度
    var sortedKeys = entriesWithSize
        .OrderByDescending(x => x.Size)
        .Select(x => x.Key)
        .ToList();
    
    // 3. 并行解压
    int processedFiles = 0;
    int failedEntries = 0;
    var syncLock = new object();
    
    await Parallel.ForEachAsync(sortedKeys, new ParallelOptions
    {
        MaxDegreeOfParallelism = MaxParallelism
    }, async (key, ct) =>
    {
        // 每个线程独立打开 archive
        using var archive = OpenArchiveWithEncodingFallback(
            archivePath, password);
        var entry = archive.Entries.First(e => e.Key == key);
        
        // ... 后续解压逻辑不变
    });
}
```

**效果**：
- 大文件（如 1GB）立即被线程 1 取走开始处理
- 中等文件（如 100MB）被线程 2、3 取走
- 小文件（如 10KB）填充剩余线程
- 所有线程从第 1 秒就满载工作，无空闲等待

private async Task<ExtractResult> ExtractParallel(
    IReadOnlyList<string> entryKeys,
    string archivePath, string destinationPath,
    string? password, IProgress<ArchiveProgress> progress,
    CancellationToken ct, ArchiveOptions? options)
{
    // 1. 创建所有目录（单线程）
    var dirs = entryKeys.Select(k => Path.GetDirectoryName(
        FileConflictHelper.GetSafePath(destinationPath, k)))
        .Where(d => !string.IsNullOrEmpty(d))
        .Distinct();
    foreach (var dir in dirs)
        Directory.CreateDirectory(dir!);
    
    // 2. 并行解压
    int processedFiles = 0;
    int failedEntries = 0;
    var syncLock = new object();
    
    await Parallel.ForEachAsync(entryKeys, new ParallelOptions
    {
        MaxDegreeOfParallelism = MaxParallelism
    }, async (key, ct) =>
    {
        // 每个线程独立打开 archive
        using var archive = OpenArchiveWithEncodingFallback(
            archivePath, password);
        var entry = archive.Entries.First(e => e.Key == key);
        
        var outputPath = FileConflictHelper.GetSafePath(
            destinationPath, key);
        
        try
        {
            // 冲突处理（需要同步）
            lock (syncLock)
            {
                var resolved = FileConflictHelper.ResolvePath(
                    outputPath, options, entry.LastModifiedTime, entry.Size);
                if (resolved == null) return;
                outputPath = resolved;
            }
            
            // 解压（独立，无需同步）
            using var entryStream = entry.OpenEntryStream();
            using var outputStream = File.Create(outputPath);
            await entryStream.CopyToAsync(outputStream, 
                CopyBufferSize, ct);
            
            // 恢复时间戳
            File.SetLastWriteTime(outputPath, 
                entry.LastModifiedTime ?? DateTime.MinValue);
            
            lock (syncLock) Interlocked.Increment(ref processedFiles);
        }
        catch (Exception ex)
        {
            lock (syncLock) Interlocked.Increment(ref failedEntries);
            CoreLog.Error($"Parallel extract failed for {key}", ex);
        }
    });
    
    return new ExtractResult 
    { 
        SucceededEntries = processedFiles, 
        FailedEntries = failedEntries 
    };
}
```

### 3. ExtractFlow 调度

```csharp
// ExtractFlow.cs
public async Task ExtractAsync(...)
{
    var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
    
    // 根据引擎能力选择串行/并行
    if (engine.SupportsParallelExtract && AppSettings.ParallelExtractDegree > 1)
    {
        // 并行路径
        await engine.ExtractAsync(archivePath, destinationPath,
            password, progress, ct, options);
    }
    else
    {
        // 串行路径（回退）
        await engine.ExtractAsync(archivePath, destinationPath,
            password, progress, ct, options);
    }
}
```

### 4. 设置项

```csharp
// AppSettings.cs
/// <summary>
/// 并行解压度（1 = 串行，>1 = 并行线程数）。
/// 默认值 = Environment.ProcessorCount。
/// </summary>
[ObservableProperty]
private int _parallelExtractDegree = Environment.ProcessorCount;
```

UI: 设置窗口 → 解压标签页 → 新增滑块 "并行解压线程数 (1-16)"

---

## 风险与对策

| 风险 | 等级 | 对策 |
|------|------|------|
| 内存占用增加 | 🟡 中 | 限制最大并行度；监控内存使用 |
| 文件句柄耗尽 | 🟢 低 | Windows 默认 ~512，8 线程足够 |
| 冲突处理竞态 | 🟡 中 | 使用 lock 同步冲突检测 |
| 进度报告不准确 | 🟢 低 | 使用 Interlocked 原子计数 |
| 取消操作延迟 | 🟢 低 | 每次循环检查 ct.ThrowIfCancellationRequested |

---

## Definition of Done

### 功能完成
- [ ] ZIP 解压支持并行模式
- [ ] 缓冲区从 256KB 优化到 4MB
- [ ] 设置窗口可配置并行度
- [ ] 串行模式保留为回退选项

### 质量保证
- [ ] 单元测试覆盖并行解压逻辑
- [ ] 性能基准测试通过（并行比串行快 5x+）
- [ ] 压力测试通过（1000 文件无 OOM）
- [ ] `dotnet build` 无错误
- [ ] `dotnet test` 全部通过

### 文档
- [ ] AGENTS.md 更新并行解压架构说明
- [ ] 代码注释完整

---

## 后续扩展

- **并行压缩**: 将文件分组并行压缩，最后合并（Phase 3）
- **7z 多线程**: 启用 SharpSevenZip 原生多线程压缩
- **流水线优化**: 解压与写入重叠（Level 3 优化）
- **自适应并行度**: 根据文件大小和数量自动调整并行度
- **进度增强**: 并行解压时显示每个线程的进度

---

## 参考资料

- [SharpCompress GitHub](https://github.com/adamhathcock/sharpcompress)
- [.NET Parallel.ForEachAsync](https://learn.microsoft.com/dotnet/api/system.threading.tasks.parallel.foreachasync)
- [Stream.CopyToAsync](https://learn.microsoft.com/dotnet/api/system.io.stream.copytoasync)
