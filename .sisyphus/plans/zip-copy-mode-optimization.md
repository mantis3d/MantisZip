# ZIP Copy-Mode 优化：压缩流直拷替代解压-重压缩

> **状态**: 📋 计划中 | **优先级**: P1 | **依赖**: 无

---

## 动机

### 现状问题

当前 `ZipEngine.AddToArchiveAsync` 和 `DeleteEntriesAsync` 采用"全量解压 → 全量重压缩"模式：

```
AddToArchiveAsync:
  Phase 1: 用 SharpCompress 解压所有旧条目到临时目录  ← CPU 解压
  Phase 2: 复制新文件到临时目录
  Phase 3: 用 ZipWriter 从头压缩所有文件                ← CPU 压缩
  Phase 4: 原子替换

DeleteEntriesAsync:
  Pass 1-3: 解压保留条目到临时目录                       ← CPU 解压
  Phase 2: 用 ZipWriter 重压缩                           ← CPU 压缩
  Phase 3: 原子替换
```

对于大压缩包（100MB+），即使只添加/删除一个 1KB 的文件，也需要解压并重压缩 **全部数据**。CPU 和 I/O 完全浪费。

### 目标

对 ZIP 格式实现 **copy-mode（压缩流直拷）**：
- 保留条目的已压缩数据：**不解压、不重压缩**，直接从源文件字节拷贝
- 新增条目：正常压缩后追加
- 重写中央目录 + EOCD

预期效果：大包小改从 **数十秒 CPU 100%** → **秒级纯 I/O**。

---

## 技术原理

### ZIP 文件结构

```
[LFH1][压缩数据1][DD1?][LFH2][压缩数据2][DD2?]...[CDFH1][CDFH2]...[EOCD]
 ^^^^^ 文件体 / 条目数据区 ^^^^^^^^^^^^^^^^^^^^^^^^^
                                                    ^^^^ 中央目录 ^^^^^^^
```

| 缩写 | 全称 | 作用 |
|------|------|------|
| LFH | Local File Header | 30 字节固定头 + 变长文件名 + 变长 extra field |
| DD | Data Descriptor | 可选（bit 3），CRC + 压缩/解压后大小，在压缩数据后 |
| CDFH | Central Directory File Header | 中央目录条目，含完整元数据 + LFH 偏移 |
| EOCD | End of Central Directory | 中央目录起始偏移 + 条目数 |

### 直拷原理

从 CDFH 中获取每个条目的：
- **Relative offset of local file header**：LFH 在文件中的起始偏移
- **Compressed size**：压缩数据长度（即使 bit 3 也正确）
- **Compression method**、**CRC**、**flags** 等

直拷操作：
1. 读 LFH（`offset` 位置）：解析 `file name length` + `extra field length`
2. LFH 总大小 = `30 + filename_len + extra_len`
3. 压缩数据起始 = `offset + LFH_total`
4. 压缩数据长度 = `CDFH.CompressedSize`（始终正确）
5. 复制 `LFH + 压缩数据` 共 `LFH_total + CompressedSize` 字节到输出文件
6. 若 bit 3 置位：压缩数据后还有 Data Descriptor，但 DD 在文件内的边界可以通过 CDFH 信息 + 下一条目偏移推导

对于不支持的复杂情况：**fallback 到原 decompress-recompress 方法**。

---

## 方案设计

### 核心思路：双路径策略

| 条件 | 路径 | 性能 |
|------|------|------|
| **纯 copy-mode**：所有保留条目无 ZIP64、无 bit 3、无加密 | 直接字节拷贝，不解压不压缩 | ⚡ 秒级（纯 I/O） |
| **混合 path**：存在上述复杂条目的 ZIP | 复杂条目 fallback 到解压-重压缩；简单条目走直拷 | 🟡 中速 |
| **加密 ZIP**（AES-256） | 由于 SharpSevenZip 加密方案与 SharpCompress 不兼容，加密 ZIP 走 SharpSevenZip 全量重打包（**保持现状**） | 🐢 慢但正确 |

### 代码结构

**新增文件**：

| 文件 | 用途 |
|------|------|
| `Core/Utils/ZipBinaryRewriter.cs` | ZIP 二进制重写核心类，独立于 ZipEngine，可测试 |
| `tests/MantisZip.Tests/Utils/ZipBinaryRewriterTests.cs` | 单元测试 |

**修改文件**：

| 文件 | 改动 |
|------|------|
| `Core/Engines/ZipEngine.cs` | `AddToArchiveAsync` / `DeleteEntriesAsync` 改用 `ZipBinaryRewriter` |
| `Core/Engines/ZipEngine.cs` | 保留现有 SharpCompress 路径作为 fallback |
| `docs/PLAN.md` | 新增本计划条目 |

### ZipBinaryRewriter API

```csharp
/// <summary>
/// ZIP 压缩流重写器。将源 ZIP 中的保留条目以压缩流直拷方式写入目标 ZIP，
/// 可选添加新条目。不支持的复杂情况（加密/ZIP64）由调用方处理。
/// </summary>
internal static class ZipBinaryRewriter
{
    /// <summary>
    /// 重写 ZIP 文件（压缩流直拷模式）。
    /// </summary>
    /// <param name="sourcePath">源 ZIP 路径</param>
    /// <param name="destPath">目标 ZIP 路径（临时文件）</param>
    /// <param name="keepEntryNames">要保留的条目名列表（删除操作时使用）</param>
    /// <param name="addEntries">要添加的新条目（添加操作时使用），null 表示只拷贝</param>
    /// <param name="encoding">文件名编码（GBK / UTF-8），与源 ZIP 一致</param>
    /// <param name="comment">ZIP 注释（写入 EOCD）</param>
    /// <param name="progress">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重写摘要（拷贝/跳过/失败统计）</returns>
    /// <exception cref="ZipCopyModeException">当条目结构无法支持 copy-mode 时抛出，调用方应 fallback</exception>
    public static Task<RewriteResult> RewriteAsync(
        string sourcePath,
        string destPath,
        HashSet<string>? keepEntryNames,
        List<NewEntry>? addEntries,
        Encoding encoding,
        string? comment = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public readonly record struct RewriteResult(
    int EntriesCopied,
    long BytesCopied,
    int EntriesAdded,
    long BytesAdded);

public readonly record struct NewEntry(
    string EntryName,
    Stream Data,
    DateTime LastModified,
    long Size);
```

---

## 实现步骤

### Step 1: ZIP 二进制解析工具（在 ZipBinaryRewriter 中实现）~1h

在 `ZipBinaryRewriter` 中编写底层解析方法，用于从 ZIP 文件读取 EOCD 和 CDFH：

```csharp
/// <summary>解析 EOCD 记录，获取中央目录偏移和条目数</summary>
static (long cdOffset, int entryCount) ReadEocd(Stream stream);

/// <summary>读取中央目录中所有条目信息</summary>
static List<CdEntry> ReadCentralDirectory(Stream stream, long cdOffset, int entryCount);

readonly record struct CdEntry(
    string FileName,
    uint Crc32,
    long CompressedSize,
    long UncompressedSize,
    ushort CompressionMethod,
    ushort Flags,
    ushort LastModifiedDate,
    ushort LastModifiedTime,
    uint LocalHeaderOffset,
    byte[] RawExtraField,   // raw CDFH extra field bytes for preservation
    byte[] RawFileExtra,    // raw LFH extra field bytes (from LFH parse)
    int LfhFilenameLength,
    int LfhExtraLength
);
```

**关键实现细节：**
- 读取 EOCD signature `0x06054b50`（从文件末尾往前找，因为 comment 可变长）
- 从 EOCD 获取 `cdOffset` + `entryCount`
- 读取每个 CDFH（signature `0x02014b50`），解析所有字段
- 处理 ZIP64 扩展（若 `entryCount = 0xFFFF` 或 `cdOffset = 0xFFFFFFFF`，查找 ZIP64 EOCD locator）
- `LocalHeaderOffset` 用于定位 LFH

### Step 2: ZipBinaryRewriter 核心逻辑 ~3h

```
流程:
1. 打开源文件流 (read-only, share-read)
2. 解析 EOCD → 获取中央目录偏移 + 条目数
3. 读取中央目录 → 获取所有条目的 CdEntry 列表
4. 构建输出文件
5. 对每个保留条目:
   a. 验证是否满足 copy-mode 条件（无 bit3? 无 ZIP64 extended? 有 LocalHeaderOffset?）
   b. 若不满足: 抛出 ZipCopyModeException，调用方 fallback
   c. 若满足: 定位到 LFH 偏移
   d. 读取 LFH (signature 0x04034b50)，解析 filename length + extra length
   e. 计算 LFH 总大小 = 30 + filename_len + extra_len
   f. 从源文件读取 LFH_total + CompressedSize 字节，写入输出文件
   g. 更新进度
6. 对每个新增条目:
   a. 用 DeflateStream 压缩数据
   b. 构建 LFH (CRC + sizes + filename + extra)
   c. 写入输出文件
   d. 更新进度
7. 构建并写入中央目录（所有保留+新增条目）
8. 写入 EOCD（含可选的 ZIP comment）
9. 原子替换源文件 (Delete + Move)
```

### Step 3: 修改 ZipEngine.AddToArchiveAsync ~2h

```csharp
public async Task AddToArchiveAsync(...)
{
    // 加密路径：保持现状 (SharpSevenZip)
    if (isEncrypted)
        goto legacy_path;

    // 尝试 copy-mode 快路径
    try
    {
        var tempArchive = Path.GetTempFileName() + ".zip";
        var result = await ZipBinaryRewriter.RewriteAsync(
            sourcePath: archivePath,
            destPath: tempArchive,
            keepEntryNames: null,             // 保留全部已有条目
            addEntries: newEntries,
            encoding: detectedEncoding,
            comment: options.Comment,
            progress: progress,
            cancellationToken: cancellationToken);
        // 原子替换
        File.Replace(tempArchive, archivePath, backup: null);
        return;
    }
    catch (ZipCopyModeException)
    {
        // fallback 到现有 extract-recompress 路径
        CoreLog.Info("AddToArchiveAsync: copy-mode not available, falling back to legacy path");
    }

legacy_path:
    // 保留现有实现（SharpSevenZip 加密路径 + SharpCompress extract-recompress 路径）
}
```

### Step 4: 修改 ZipEngine.DeleteEntriesAsync ~1.5h

类似 Step 3，但 `keepEntryNames` 设为非删除条目 + `addEntries` 为 null。

### Step 5: 单元测试 ~2h

| 测试场景 | 覆盖 |
|---------|------|
| 添加文件到空 ZIP | copy-mode |
| 添加文件到有文件的 ZIP | copy-mode 保留旧条目 + 追加新条目 |
| 删除单个文件 | copy-mode 拷贝保留条目 |
| 删除多个文件 | copy-mode |
| 删除后添加同名文件 | copy-mode 拷保留 + 压缩新 |
| GBK 编码 ZIP（含中文文件名） | copy-mode 编码保持 |
| bit 3 data descriptor 条目 | 验证检测 + fallback |
| ZIP64 条目 | 验证 fallback |
| 加密 ZIP 添加文件 | 走 SharpSevenZip（不改变） |
| 加密 ZIP 删除文件 | 走 SharpSevenZip（不改变） |
| 大文件（100MB+ ZIP 删 1KB）| 验证耗时 ≈ I/O 时间 |
| 取消操作 | CancellationToken 正常传播 |
| 源文件损坏 | 抛出异常，不产生半成品输出 |

---

## 不处理/推迟的边界情况

以下情况 **不走 copy-mode**，fallback 到现有实现：

| 情况 | 原因 |
|------|------|
| **加密 ZIP**（AES-256） | SharpSevenZip 的加密方案与 SharpCompress 不兼容，加密数据格式不可直接拷贝 |
| **ZIP64 条目**（单文件 >4GB 或总条目 >65535）| 偏移字段为 8 字节，解析复杂，收益低 |
| **Data Descriptor（bit 3）** | LFH 中无 CompressedSize，CDFH 中有但无法精确定位 DD 边界（除非解析 DD）。走 fallback。 |
| **跨卷分卷 ZIP**（split）| MantisZip 的 SplitOutputStream 是写入专用，不支持读取已有分卷 |

以上限制在实践中影响很小：
- 加密 ZIP 走 SharpSevenZip，全量重打包，但这部分用户已有预期（加密就是慢）
- ZIP64 在普通场景很少见
- Data Descriptor 常见于流式写入，但 MantisZip 自身生成的 ZIP 不使用 bit 3

---

## 分阶段实施

### Phase 1: 纯 copy-mode（无加密、无 bit 3、无 ZIP64）

- 实现 `ZipBinaryRewriter` 基础版本
- 支持 `CompressionMethod = Deflate(8) | Store(0)` 的简单条目
- ZipEngine 优先尝试 copy-mode，失败自动 fallback
- 全量测试

### Phase 2: 处理复杂条目

- 若有必要，扩展 `ZipBinaryRewriter` 支持 bit 3 / ZIP64
- 或维持 fallback 策略，观察用户反馈

---

## 需确认的问题

1. SharpCompress 的 `IArchiveEntry` 是否确实不暴露压缩数据偏移？→ 已验证：不暴露
2. 数据描述符（bit 3）检测策略：读 LFH flags → 检测 bit 3
3. 是否需要支持 LZMA 压缩的 ZIP？SharpCompress 支持，但 MantisZip 生成只用 Deflate/Store

---

## 时间估算

| 步骤 | 内容 | 预计工时 |
|------|------|---------|
| Step 1 | ZipBinaryReader（EOCD + CDFH 解析） | 1h |
| Step 2 | ZipBinaryRewriter 核心（直拷 + 中央目录重建） | 3h |
| Step 3 | ZipEngine.AddToArchiveAsync 改造 | 2h |
| Step 4 | ZipEngine.DeleteEntriesAsync 改造 | 1.5h |
| Step 5 | 测试：单元测试 + 边界测试 | 2h |
| | **合计** | **~9.5h** |

### 各步骤并行度

| 步骤 | 可提前启动？| 依赖 |
|------|-----------|------|
| Step 1 | ✅ 立即可开始 | 无 |
| Step 2 | ❌ 需 Step 1 | Step 1 |
| Step 3 | ❌ 需 Step 2 | Step 2 |
| Step 4 | ❌ 需 Step 2 | Step 2 |
| Step 5 | ⚡ 部分并行 | Step 1（可先测解析）+ Step 2（完成后测全流程）|

### 实际交付时间

- **最速**（专注无打断）：**2 个工作日**
- **正常**（穿插其他工作）：**4-5 个工作日**

---

## 风险与缓解

| 风险 | 概率 | 影响 | 缓解 |
|------|------|------|------|
| ZIP 二进制结构解析遇到未预期的变体 | 中 | 高 | fallback 到原实现；逐步扩展覆盖 |
| 文件名编码（GBK/UTF-8）在直拷时失真 | 低 | 中 | CDFH 中 filename 作为 raw bytes 拷贝，不解码再编码，保证无损 |
| SharpCompress 与直拷生成的 ZIP 兼容性 | 低 | 中 | 始终有 fallback；测试验证 SharpCompress 可读直拷输出 |
| 大文件原子替换时 I/O 竞争 | 低 | 低 | 现有带重试的替换机制已可用 |

---

## 验收标准

- [ ] `dotnet test` 全部通过（新增 + 既有）
- [ ] 10MB ZIP 添加一个 1KB 文件，耗时 < 0.3s（旧方法 > 2s）
- [ ] 100MB ZIP 删除一个 1KB 文件，耗时 < 1s（旧方法 > 20s）
- [ ] GBK 编码 ZIP 添加/删除后文件名不乱码
- [ ] 加密 ZIP 添加/删除功能正常（走 SharpSevenZip 路径）
- [ ] fallback 机制正确：不支持的格式自动走旧路径
- [ ] `lsp_diagnostics` 零 warning
