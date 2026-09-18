# 多线程压缩方案 A：分组并行 + 多级别自适应

> 状态: 📋 待定 | 阶段: [⬜⬜⬜⬜⬜] (0/5)
> 前置: 自适应压缩已实现（`alpha` 分支 `e97010a`）
> 升级路径: 方案 D 完成后实施

---

## 动机

方案 D 中，需压缩类文件统一用一个级别——放弃了这部分的自适应能力。
本方案通过**按级别分组 → 各组并行压缩 → 合并 ZIP**，实现**多级别 + 多线程**同时启用。

**量化预期**：
- 混合目录（代码 + 图片 + 视频）：压缩速度提升 **3-5x**
- 每个文件都用最合适的级别，包大小与串行自适应一致

---

## 设计

### 核心思路

```
文件列表
  ↓
AdaptiveRuleMatcher 分级
  ├── Level=0 (Store):   [a.jpg, b.png, c.gif]    → Group-Store
  ├── Level=2 (Fast):    [data.bin]                 → Group-Fast
  ├── Level=6 (Normal):  [main.cs, utils.cs]        → Group-Normal
  └── Level=9 (Max):     [readme.txt, doc.md]       → Group-Max

并行压缩（每个 Group 独立 ZipWriter → MemoryStream）：
  Task-Store:  ZipWriter(store)  → MemoryStream-1  (几乎不耗时)
  Task-Fast:   ZipWriter(fast)   → MemoryStream-2
  Task-Normal: ZipWriter(normal) → MemoryStream-3
  Task-Max:    ZipWriter(max)    → MemoryStream-4

合并到输出 ZIP：
  主线程从 MemoryStream-1,2,3,4 逐个读取 entries 写入最终 ZipWriter
```

### 为什么用 MemoryStream 而不是临时文件？

- MemoryStream 无磁盘 I/O，合并阶段更快
- 临时文件方案：写入慢 + 需要清理 + 磁盘空间占用
- 内存占用可控：每组缓存的是压缩后的数据（通常比原文件小）

### 内存控制策略

**问题**：大文件缓存在 MemoryStream 会导致 OOM。

**策略**：按文件大小分批处理

```
阈值: 单文件 > 10MB → 不参与分组，直接写入输出 ZIP（串行）
```

| 文件大小 | 处理方式 | 原因 |
|---------|---------|------|
| ≤ 10MB | 缓存在 MemoryStream 参与并行 | 压缩后更小，内存可控 |
| > 10MB | 串行写入输出 ZIP | 避免 OOM，但占比通常不高 |

### 流程

```
Phase 1: 分级分组
  files → AdaptiveRuleMatcher → Dictionary<int/*level*/, List<FileEntry>> groups
  同时分离出大文件列表 (> 10MB)

Phase 2: 并行压缩小文件（各组独立 ZipWriter → MemoryStream）
  tasks = groups.Select(group => Task.Run(() => {
    using var ms = new MemoryStream();
    using var writer = new ZipWriter(ms, new ZipWriterOptions { CompressionLevel = group.Key });
    foreach file in group:
      writer.WriteToStream(file.RelativePath, file.FullPath);
    return (level: group.Key, stream: ms);
  }));
  results = await Task.WhenAll(tasks);

Phase 3: 串行写入大文件（直接写入输出 ZIP）
  foreach file in bigFiles:
    zipWriter.WriteToStream(file.RelativePath, file.FullPath);

Phase 4: 合并（从 MemoryStream 读取 entries 写入输出 ZIP）
  foreach result in results:
    result.stream.Position = 0;
    using var tempArchive = ZipArchive.OpenArchive(result.stream);
    foreach entry in tempArchive.Entries:
      zipWriter.WriteToStream(entry.Key, entry.OpenEntryStream());
```

### 进度报告

并行阶段的进度报告需要汇总：

```csharp
// 每个 Task 独立上报自己 Group 的进度
// 主线程用 Interlocked 汇总
int totalProcessedBytes = 0;
int totalProcessedFiles = 0;

// 在 Task.WhenAll 的回调中汇总
var overallPct = (double)totalProcessedBytes / totalBytes * 100;
progress?.Report(new ArchiveProgress {
    CurrentFile = "正在压缩...",
    PercentComplete = overallPct,
    ...
});
```

### 与方案 D 的关系

方案 A 是方案 D 的超集：

| 特性 | 方案 D | 方案 A |
|------|--------|--------|
| Store 类 Store | ✅ | ✅ |
| 需压缩类多线程 | ✅（SharpSevenZip mt） | ✅（ZipWriter 并行） |
| 需压缩类多级别 | ❌ 统一一个级别 | ✅ 每组独立级别 |
| 实现复杂度 | 低 | 高 |
| 内存占用 | 低（临时文件） | 中（MemoryStream 缓存） |

---

## 任务清单

### Phase 1: 分级分组逻辑

- [ ] **1. 实现 GroupByCompressionLevel()**
  - 文件: `Core/Engines/ZipEngine.cs`（新增私有方法）
  - 参数: `List<FileEntry> files, ArchiveOptions options`
  - 返回: `Dictionary<int, List<FileEntry>>` + `List<FileEntry> bigFiles`
  - 逻辑:
    1. 遍历 files，调用 `ZipEntryClassifier.GetAdaptiveLevel()` 获取级别
    2. `fileSize > 10MB` → 加入 bigFiles（串行处理）
    3. 其余按 level 分组到 Dictionary
  - 验证: 单元测试覆盖分组逻辑

### Phase 2: 并行压缩 + MemoryStream

- [ ] **2. 实现 CompressGroupParallel()**
  - 文件: `Core/Engines/ZipEngine.cs`（新增私有方法）
  - 参数: `Dictionary<int, List<FileEntry>> groups, ArchiveOptions options`
  - 返回: `List<(int Level, MemoryStream Stream)>`
  - 逻辑:
    1. `groups.Select(group => Task.Run(() => CompressOneGroup(group, options)))`
    2. `await Task.WhenAll(tasks)`
    3. 每个 Task 内部: 创建 ZipWriter → MemoryStream → 写入该组所有文件
  - 线程安全: 每个 Task 独立 ZipWriter + MemoryStream，无共享状态

- [ ] **3. 实现 CompressOneGroup()**
  - 文件: `Core/Engines/ZipEngine.cs`（新增私有方法）
  - 参数: `(int level, List<FileEntry> files, ArchiveOptions options)`
  - 返回: `MemoryStream`
  - 逻辑:
    ```csharp
    var ms = new MemoryStream();
    var writerOptions = new ZipWriterOptions(CompressionType.Deflate)
    {
        CompressionLevel = level,
        ArchiveEncoding = ...,
    };
    using var writer = new ZipWriter(ms, writerOptions);
    foreach (var file in files)
    {
        using var entryStream = writer.WriteToStream(file.RelativePath, ...);
        using var fs = File.OpenRead(file.FullPath);
        fs.CopyTo(entryStream);
    }
    return ms;
    ```

### Phase 3: 合并 + 串行大文件

- [ ] **4. 实现 MergeParallelResults()**
  - 文件: `Core/Engines/ZipEngine.cs`（新增私有方法）
  - 参数: `ZipWriter zipWriter, List<(int Level, MemoryStream Stream)> results, List<FileEntry> bigFiles, ...`
  - 逻辑:
    1. 先写入大文件（串行，直接 `zipWriter.WriteToStream`）
    2. 再合并各 MemoryStream:
       ```csharp
       foreach (var (level, stream) in results)
       {
           stream.Position = 0;
           using var tempArchive = ZipArchive.OpenArchive(stream);
           foreach (var entry in tempArchive.Entries.Where(e => !e.IsDirectory))
           {
               using var entryStream = entry.OpenEntryStream();
               var entryOptions = new ZipWriterEntryOptions
               {
                   ModificationDateTime = entry.LastModifiedTime ?? DateTime.Now,
               };
               using var outStream = zipWriter.WriteToStream(entry.Key, entryOptions);
               entryStream.CopyTo(outStream);
           }
       }
       ```
    3. 释放所有 MemoryStream

- [ ] **5. CompressAsync 接入**
  - 文件: `Core/Engines/ZipEngine.cs`
  - 改动: 在非加密路径中，当 `AdaptiveCompressionMode != Disabled` 时：
    - 文件数 ≤ 阈值（如 10）或总大小 ≤ 阈值（如 100MB）→ 走现有串行路径
    - 否则 → 走 `GroupByCompressionLevel()` → `CompressGroupParallel()` → `MergeParallelResults()`
  - 保留: 进度报告、取消支持、错误处理

### Phase 4: 设置与 UI

- [ ] **6. AppSettings 并行度配置**
  - 文件: `UI/Models/AppSettings.cs`
  - 新增: `int ParallelCompressDegree`（默认 `Environment.ProcessorCount`，范围 1-16）
  - UI: 压缩设置窗口新增滑块 "并行压缩线程数"

- [ ] **7. i18n 新增 key**
  - `Compress_ParallelDegree`: "并行线程数" / "Parallel threads"
  - `Compress_ParallelDegree_Tooltip`: "压缩时使用的并行线程数（1=串行）" / "Number of parallel threads for compression (1=serial)"

### Phase 5: 测试与验证

- [ ] **8. 单元测试**
  - 文件: `tests/MantisZip.Tests/Engines/GroupedParallelCompressTests.cs`（新增）
  - 测试用例:
    - 分组正确性：同一级别文件分到同一组
    - 大文件分离：> 10MB 文件不参与并行
    - 并行压缩正确性：各组压缩结果可解压，内容一致
    - 多级别验证：不同组的压缩级别正确应用
    - 合并正确性：最终 ZIP 包含所有文件
    - 进度报告：并行阶段进度正确汇总
    - 并行度=1 时退化为串行
    - 取消操作正常工作

- [ ] **9. 性能基准测试**
  - 场景 1: 100 个小文件（1MB × 100，3 个级别）
  - 场景 2: 10 个中等文件（10MB × 10，2 个级别）
  - 场景 3: 混合大小文件（1KB-50MB，4 个级别）
  - 对比: 串行自适应 vs 并行分组自适应
  - 监控: CPU 利用率、内存峰值、压缩时间

- [ ] **10. 压力测试**
  - 1000 个文件 × 100KB
  - 内存占用监控（确保不 OOM）
  - 取消操作后资源清理

---

## 改动范围

| 文件 | 改动类型 | 说明 |
|------|---------|------|
| `Core/Engines/ZipEngine.cs` | 修改 + 新增 | 分组 + 并行压缩 + 合并 + CompressAsync 接入 |
| `UI/Models/AppSettings.cs` | 新增 | ParallelCompressDegree 设置 |
| `UI/Dialogs/CompressSettingsWindow.axaml` | 新增 | 并行度滑块 |
| `UI/Localization/strings.zh-CN.json` | 新增 | 2 个 key |
| `UI/Localization/strings.en.json` | 新增 | 2 个 key |
| `UI/ViewModels/MainWindowViewModel.cs` | 修改 | UpdateLocalizedStrings 注册新 key |
| `tests/MantisZip.Tests/.../GroupedParallelCompressTests.cs` | 新增 | 单元测试 |

---

## 风险与对策

| 风险 | 等级 | 对策 |
|------|------|------|
| MemoryStream OOM | 🟡 中 | 大文件阈值 10MB + 并行度上限 16 |
| ZipWriter 非线程安全 | 🟢 低 | 每个 Task 独立 ZipWriter，无共享状态 |
| 合并阶段串行瓶颈 | 🟡 中 | MemoryStream → ZipArchive.OpenArchive 流式读取，避免全量缓存 |
| 进度报告不准确 | 🟢 低 | Interlocked 原子计数 + Task 完成回调汇总 |
| 并行度=1 退化 | 🟢 低 | 检测并走现有串行路径 |

---

## Definition of Done

- [ ] ZIP 非加密压缩支持分组并行 + 多级别自适应
- [ ] 大文件（> 10MB）串行处理，不 OOM
- [ ] 并行度可配置（1-16）
- [ ] 加密 ZIP 行为不变
- [ ] 单元测试全部通过
- [ ] 性能基准测试：并行比串行快 2x+
- [ ] `dotnet build` 0 errors
- [ ] `dotnet test` 全部通过

---

## 与方案 D 的升级路径

1. 方案 D 先上线：SharpSevenZip mt=on 处理需压缩类文件
2. 方案 A 后续升级：替换 CompressGroupWithSevenZip 为分组并行 ZipWriter
3. 两方案共享: Store 类路径、分级逻辑、UI 设置入口
