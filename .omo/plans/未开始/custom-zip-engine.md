# 自研 ZIP 引擎（Per-entry 压缩 + 多线程）

> 支持 per-entry 压缩级别/方法控制 + 多线程并行压缩的纯 .NET ZIP 引擎
> **状态**: 📋 待实施 | **阶段**: [⬜⬜⬜⬜⬜⬜] (0/6，Phase 6 可选)
> **前置依赖**: 无
> **适用范围**: Core 层（MantisZip.Core）

---

## 动机

### 现状

| 库 | Per-entry 控制 | 多线程 | 零依赖 | 跨平台 |
|---|---|---|---|---|
| SharpCompress | ❌ | ❌ | ❌ | ✅ |
| SharpZipLib | ❌ | ❌ | ❌ | ✅ |
| System.IO.Compression | ❌ | ❌ | ✅ | ✅ |
| SharpSevenZip (7z.dll) | ❌ | ✅ (mt=on) | ❌ | ❌ |

**结论**：现有库均不支持 per-entry 压缩级别控制。

### 需求

1. **Per-entry 压缩级别**：每个文件独立设置 0-9 压缩级别（产品卖点）
2. **Per-entry 压缩方法**：Store（不压缩）/ Deflate（标准压缩）可选
3. **多线程压缩**：利用多核 CPU 并行压缩多个文件
4. **零外部依赖**：纯 .NET 实现，便于跨平台和开源

### 收益

- **产品差异化**：per-entry 控制是独特卖点
- **性能提升**：多线程压缩提速 2-4x
- **开源潜力**：.NET 生态缺少这样的库
- **跨平台**：纯 .NET 实现，支持 Avalonia 未来跨平台

---

## 架构设计

### 核心原理

```
ZIP 文件结构：
┌─────────────────────────────────────┐
│ 本地文件头 + 压缩数据（每个条目独立） │ ← Per-entry 控制点
│ 本地文件头 + 压缩数据               │
│ ...                                │
│ 中央目录（一次性组装）               │
│ EOCD 签名                          │
└─────────────────────────────────────┘

多线程策略：
1. 每个文件独立压缩（天然并行）
2. 收集偏移量/CRC/大小
3. 一次性写入中央目录
```

### Per-entry 控制

```csharp
// 每个文件独立配置
var entry = new ZipEntry
{
    FileName = "document.txt",
    CompressionLevel = 9,        // 高压缩
    CompressionMethod = Deflate, // 标准方法
    // ...
};

var entry2 = new ZipEntry
{
    FileName = "photo.jpg",
    CompressionLevel = 0,        // 不压缩（已是压缩格式）
    CompressionMethod = Store,   // 直接存储
    // ...
};
```

### 多线程实现

```csharp
// 并行压缩
await Parallel.ForEachAsync(entries, new ParallelOptions
{
    MaxDegreeOfParallelism = Environment.ProcessorCount
}, async (entry, ct) =>
{
    // 每个文件独立压缩
    var compressed = await CompressEntryAsync(entry, ct);
    results[entry.Index] = compressed;
});

// 一次性写入中央目录
WriteCentralDirectory(results);
```

---

## 分阶段实施

### Phase 1: 核心压缩逻辑

**目标**：实现单文件 Deflate 压缩

**任务**：
1. 创建 `CustomZipEngine` 类（`Core/Engines/CustomZipEngine.cs`）
2. 实现 `CompressEntryAsync` 方法（使用 `DeflateStream`）
3. 实现 CRC32 计算
4. 实现本地文件头写入
5. 单元测试

**验收标准**：
- 能压缩单个文件为有效 ZIP
- 支持 Store/Deflate 两种方法
- CRC32 校验正确

**预计耗时**：1-2 天

---

### Phase 2: ZIP 格式组装

**目标**：实现完整的 ZIP 文件结构

**任务**：
1. 实现中央目录组装
2. 实现 EOCD 签名写入
3. 支持 ZIP64（大文件支持）
4. 支持文件时间戳保留
5. 集成测试

**验收标准**：
- 生成的 ZIP 可被 Windows Explorer / 7-Zip / SharpCompress 正确读取
- 支持多文件压缩
- 支持目录结构

**预计耗时**：1-2 天

---

### Phase 3: Per-entry 控制

**目标**：实现每个条目独立的压缩配置

**任务**：
1. 设计 `ZipEntry` 配置模型
2. 实现 per-entry 压缩级别（0-9）
3. 实现 per-entry 压缩方法（Store/Deflate）
4. 实现智能默认：已压缩文件（jpg/png/mp4）自动 Store
5. 单元测试

**验收标准**：
- 不同文件可用不同压缩级别
- 已压缩文件自动跳过压缩
- 压缩率对比测试

**预计耗时**：1 天

---

### Phase 4: 多线程并行

**目标**：利用多核 CPU 并行压缩

**任务**：
1. 实现 `Parallel.ForEachAsync` 并行压缩
2. 线程安全的偏移量收集
3. 并行度配置（`ParallelMaxThreads`）
4. 进度报告（字节加权）
5. 性能基准测试

**验收标准**：
- 多线程压缩正确性（解压后逐字节比对）
- 8 核环境加速比 ≥ 2x
- 进度报告准确

**预计耗时**：1 天

---

### Phase 5: 测试 + 集成

**目标**：完整测试 + MantisZip 集成

**任务**：
1. 与 `ArchiveEngineFactory` 集成（路由逻辑）
2. 在 `AppSettings` 中添加配置选项
3. 端到端测试
4. 性能对比（vs SharpCompress）
5. 代码审查

**验收标准**：
- 所有现有测试通过
- 新引擎可通过配置启用/禁用
- 性能不低于 SharpCompress

**预计耗时**：1 天

---

### Phase 6（可选）: 加密支持

**目标**：AES-256 加密支持（后续扩展）

**前置条件**：Phase 1-5 完成且稳定运行

**任务**：
1. 评估加密实现方案（集成 SharpZipLib / 继续使用 SharpSevenZip / 自实现）
2. 实现 per-entry 加密控制（可选加密/不加密）
3. 密码验证
4. 端到端测试（与 7-Zip / Windows Explorer 兼容性）

**验收标准**：
- 加密 ZIP 可被主流工具正确解密
- per-entry 加密控制正常工作
- 性能影响可接受

**预计耗时**：2-3 天（取决于方案选择）

**备注**：
- 延迟原因：SharpZipLib 维护活跃度不高，自实现复杂度大
- 推荐方案：继续使用 SharpSevenZip（当前已集成），或评估更活跃的替代库
- 优先级：相对较低，per-entry 压缩控制是更核心的需求

---

## 技术细节

### ZIP 文件格式

```
本地文件头（每个条目）：
├── 签名：0x04034b50
├── 版本需要：20（2.0）
├── 通用标志：0（或 1 加密）
├── 压缩方法：8（Deflate）/ 0（Store）
├── 最后修改时间
├── 最后修改日期
├── CRC32
├── 压缩大小
├── 未压缩大小
├── 文件名长度
├── 扩展字段长度
└── 文件名 + 扩展字段 + 压缩数据

中央目录（所有条目）：
├── 签名：0x02014b50
├── 版本创建/需要
├── 通用标志
├── 压缩方法
├── 时间/日期
├── CRC32
├── 压缩/未压缩大小
├── 文件名/扩展字段/注释长度
├── 磁盘号
├── 内部/外部属性
├── 局部头部偏移量 ← 关键：并行压缩后收集
└── 文件名 + 扩展字段 + 注释

EOCD：
├── 签名：0x06054b50
├── 磁盘号
├── 中央目录磁盘号
├── 本磁盘条目数
├── 总条目数
├── 中央目录大小
├── 中央目录偏移量
└── 注释长度
```

### 多线程策略

```csharp
// 1. 预分配偏移量槽位
var offsetSlots = new long[entries.Count];
var crcSlots = new uint[entries.Count];
var compressedSizeSlots = new long[entries.Count];

// 2. 并行压缩（每个线程写入独立临时缓冲区）
await Parallel.ForEachAsync(entries.Select((e, i) => (Entry: e, Index: i)), 
    new ParallelOptions { MaxDegreeOfParallelism = maxThreads },
    async (item, ct) =>
{
    using var ms = new MemoryStream();
    await CompressEntryToStreamAsync(item.Entry, ms, ct);
    
    // 收集结果（线程安全）
    offsetSlots[item.Index] = Interlocked.Add(ref currentOffset, ms.Length);
    crcSlots[item.Index] = item.Entry.Crc32;
    compressedSizeSlots[item.Index] = ms.Length;
    
    // 写入临时文件（避免内存压力）
    await WriteToTempFileAsync(item.Index, ms.ToArray(), ct);
});

// 3. 顺序组装最终 ZIP
using var output = File.Create(outputPath);
for (int i = 0; i < entries.Count; i++)
{
    // 写入本地文件头 + 压缩数据（从临时文件读取）
    await WriteLocalHeaderAndDataAsync(output, entries[i], offsetSlots[i], ...);
}

// 4. 写入中央目录 + EOCD
WriteCentralDirectory(output, entries, offsetSlots, crcSlots, compressedSizeSlots);
WriteEOCD(output, entries.Count, centralDirOffset, centralDirSize);
```

### 智能压缩默认

```csharp
// 根据文件扩展名自动选择压缩方法
private static CompressionMethod GetSmartMethod(string fileName)
{
    var ext = Path.GetExtension(fileName).ToLowerInvariant();
    return ext switch
    {
        // 已压缩格式 → Store（不压缩）
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" 
        or ".mp4" or ".mkv" or ".avi" or ".mov" 
        or ".mp3" or ".flac" or ".aac" or ".ogg"
        or ".zip" or ".7z" or ".rar" or ".gz" or ".bz2" or ".xz"
        or ".pdf" or ".docx" or ".xlsx" or ".pptx"
            => CompressionMethod.Store,
        
        // 文本/代码文件 → Deflate
        _ => CompressionMethod.Deflate
    };
}
```

---

## 受影响的文件

| 文件 | 操作 | 说明 |
|------|------|------|
| `Core/Engines/CustomZipEngine.cs` | 新增 | 自研 ZIP 引擎核心 |
| `Core/Models/ZipEntry.cs` | 新增 | 条目配置模型 |
| `Core/Utils/Crc32Calculator.cs` | 新增 | CRC32 计算 |
| `Core/Utils/CentralDirectoryBuilder.cs` | 新增 | 中央目录组装 |
| `Core/Engines/ArchiveEngineFactory.cs` | 修改 | 添加路由逻辑（per-entry 压缩时返回 CustomZipEngine） |
| `Models/AppSettings.cs` | 修改 | 添加配置选项 |
| `tests/MantisZip.Tests/Engines/CustomZipEngineTests.cs` | 新增 | 单元测试 |

> **注意**：不修改 `IArchiveEngine` 接口，通过引擎类型判断是否支持 per-entry 压缩。`CompressService` 无需直接修改，通过 `ArchiveEngineFactory` 路由。

---

## 与现有计划的关系

```
compression-performance-optimization.md
├── ✅ 并行解压（已完成）
├── ✅ 7z mt=on（已完成）
├── ✅ 缓冲区优化（已完成）
└── ⏸ ZIP 并行压缩 → 移到本计划

custom-zip-engine.md（本计划）
├── Per-entry 压缩级别（新需求）
├── 多线程压缩（从旧计划迁移）
├── Per-entry 压缩方法（新需求）
└── 开源为独立库（后续目标）
```

---

## 后续：开源为独立库

### 阶段 1：MantisZip 内部模块
- 在 Core 层开发，先满足自身需求
- 完成所有功能和测试

### 阶段 2：抽取为独立库
- 创建独立仓库：`mantis3d/ZipPerEntry`
- NuGet 包：`ZipPerEntry`
- MIT 许可证
- 完整文档和示例

### 阶段 3：MantisZip 切换引用
- `dotnet add package ZipPerEntry`
- 删除自研代码
- 保持功能一致

### 开源收益
- **社区影响力**：.NET 生态缺这样的库
- **Bug 发现**：更多用户 = 更多边界情况
- **MantisZip 声誉**："我们开源了一个被广泛使用的库"
- **维护分担**：社区贡献 PR

---

## 配置选项

在 `AppSettings` 中添加：

```csharp
/// <summary>
/// 使用自定义 ZIP 引擎（支持 per-entry 压缩控制）。默认 false。
/// 启用后，ZIP 压缩将使用 CustomZipEngine 替代 ZipEngine。
/// </summary>
public bool UseCustomZipEngine { get; set; } = false;

/// <summary>
/// 自定义 ZIP 引擎并行压缩线程数。默认 0（使用 Environment.ProcessorCount）。
/// </summary>
public int CustomZipEngineThreads { get; set; } = 0;
```

### 集成路径

```csharp
// ArchiveEngineFactory.GetEngineByExtension 中添加路由逻辑
if (ext == ".zip" && AppSettings.Instance.UseCustomZipEngine)
    return new CustomZipEngine();

// 原有逻辑保持不变
return ext switch
{
    ".zip" => GetEngine(ArchiveFormat.Zip),
    // ...
};
```

---

## 成功标准

### 功能验收
- [ ] Per-entry 压缩级别（0-9 独立控制）
- [ ] Per-entry 压缩方法（Store/Deflate）
- [ ] 多线程压缩（加速比 ≥ 2x @ 8核）
- [ ] 生成的 ZIP 被主流工具正确读取
- [ ] 所有现有测试通过
- [ ] 可通过配置启用/禁用新引擎

### 性能验收
- [ ] 单线程性能不低于 SharpCompress
- [ ] 多线程加速比 ≥ 2x（8核环境）
- [ ] 内存使用合理（大文件支持）

### 质量验收
- [ ] 单元测试覆盖率 ≥ 80%
- [ ] 集成测试通过
- [ ] 代码审查完成
- [ ] 文档完整

---

## 验证命令

```powershell
# 构建
dotnet build src/MantisZip.Core/MantisZip.Core.csproj

# 测试
dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj

# 性能测试
dotnet run --project tests/MantisZip.Tests/Engines/CustomZipEngineBenchmarks.csproj
```

---

## 参考资料

- [ZIP 文件格式规范](https://pkware.cachefly.net/docs/case-studies/appnote.txt)
- [System.IO.Compression.DeflateStream](https://learn.microsoft.com/dotnet/api/system.io.compression.deflatestream)
- [Parallel.ForEachAsync](https://learn.microsoft.com/dotnet/api/system.threading.tasks.parallel.foreachasync)
- [SharpCompress ZIP 实现](https://github.com/adamhathcock/sharpcompress)（参考格式处理）
