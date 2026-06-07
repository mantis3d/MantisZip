# ZipEngine SharpZipLib → SharpCompress 迁移

## TL;DR

> **Quick Summary**: 将 `ZipEngine.cs` 中剩余的 3 个方法从 SharpZipLib API 迁移到 SharpCompress，从而完全移除 SharpZipLib 依赖。
>
> 好消息：`ExtractAsync` / `ExtractEntriesAsync` / `ListEntriesAsync` / `TestArchiveAsync` **已经使用 SharpCompress**，无需修改。
>
> **Deliverables**:
> - `CompressAsync` 从 `ZipOutputStream` 迁移到 `ZipWriter`
> - `AddToArchiveAsync` 从 `OpenZipFile`+`GetInputStream`+`ZipOutputStream` 全部迁移到 SharpCompress
> - `DeleteEntriesAsync` 从 `OpenZipFile`+`GetInputStream`+`ZipOutputStream` 全部迁移到 SharpCompress
> - 删除 `OpenZipFile` 静态方法（不再需要）
> - 删除 `using ICSharpCode.SharpZipLib.Zip`
> - 从 `.csproj` 移除 SharpZipLib 包引用
>
> **Estimated Effort**: Medium (3-4h)
> **Parallel Execution**: NO — 3 个方法有重叠逻辑，需顺序迁移
> **Critical Path**: CompressAsync → AddToArchiveAsync → DeleteEntriesAsync → 清理

---

## Context

### 当前依赖分析

`ZipEngine.cs`（1128 行）中 SharpZipLib 的实际使用范围：

| 方法 | 行数 | SharpZipLib API | SharpCompress 等价 API |
|------|:----:|-----------------|----------------------|
| `CompressAsync` | ~100 | `ZipOutputStream` + `SetComment` + `SetLevel` + `Password` + `PutNextEntry` + `Write` | `ZipWriter(Stream, ZipWriterOptions)` + `ZipWriter.Write()` |
| `AddToArchiveAsync` | ~250 | `ZipFile`(枚举) + `GetInputStream`(提取) + `ZipOutputStream`(重压缩) + `ZipEntry` | `ZipArchive.Open()` + `entry.OpenEntryStream()` + `ZipWriter` |
| `DeleteEntriesAsync` | ~250 | `ZipFile`(验证) + `GetInputStream`(提取) + `ZipOutputStream`(重压缩) | `ZipArchive.Open()` + `entry.OpenEntryStream()` + `ZipWriter` |
| `ReadFileWithRetry` | ~90 | `ZipEntry` + `PutNextEntry` + `Write`（被 CompressAsync 调用） | 随 CompressAsync 一起替换 |
| `OpenZipFile` | ~35 | 静态方法，返回 `ZipFile` 实例 | 删除，全部用 `OpenArchiveWithEncodingFallback` |

**已迁移**（无需修改）：
- `ExtractAsync` ✅ — 已用 `OpenArchiveWithEncodingFallback` + `entry.OpenEntryStream()`
- `ExtractEntriesAsync` ✅ — 同上
- `ListEntriesAsync` ✅ — 已用 `OpenArchiveWithEncodingFallback`
- `TestArchiveAsync` ✅ — 已用 `OpenArchiveWithEncodingFallback`

### SharpCompress `ZipWriter` API 对照

```csharp
// SharpZipLib 原写法
using var zipStream = new ZipOutputStream(fsOut);
zipStream.SetLevel(options.CompressionLevel);
zipStream.SetComment(options.Comment);
zipStream.Password = options.Password;
var entry = new ZipEntry(ZipEntry.CleanName(relPath)) { DateTime = dt };
zipStream.PutNextEntry(entry);
// ... copy data ...
zipStream.Write(buffer, 0, read);

// SharpCompress 等价写法
using var zipWriter = WriterFactory.Open(fsOut, ArchiveType.Zip, new WriterOptions(CompressionType.Deflate)
{
    CompressionLevel = options.CompressionLevel,
    ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8 }
});
zipWriter.Write(relPath, fsInput, new EntryInfo { ModifiedTime = dt });
// Write() 自动处理 PutNextEntry + 数据复制

// 注释需要压缩后单独写入
zipWriter.Dispose(); // 确保所有数据写入完毕
ZipCommentHelper.WriteComment(outputPath, options.Comment);
```

### Metis Review

**关键风险**：
1. **进度报告**：`ReadFileWithRetry` 有字节级加权进度 + 100ms 节流 + 重试逻辑。SharpCompress 的 `ZipWriter.Write(entryName, stream)` 是一步操作，不支持中间进度回调。需要改为手动循环读写（与当前类似）或使用 `Write(entryName, stream, size, progress)` 重载（需查 SharpCompress API）。
2. **加密**：SharpCompress 的 ZIP 加密通过 `ZipWriterOptions` 配置。SharpZipLib 用 `zipStream.Password = pwd` + `AESKeySize = 256`。SharpCompress 使用 `ZipEncryption` 枚举 + password 参数在 `ZipWriterOptions` 中。
3. **分卷（Split）**：`CompressAsync` 支持分卷压缩（`SplitOutputStream`）。SharpCompress 的 `ZipWriter` 不直接支持分卷，需要保持 `SplitOutputStream` 或改用其他方式。但 `SplitOutputStream` 是自定义包装流，与 writer 解耦，可以继续使用。
4. **注释**：`ZipOutputStream.SetComment()` 在压缩过程中写入 EOCD。SharpCompress 的 `ZipWriter` 不支持在压缩时设置注释，需要在压缩完成后调用 `ZipCommentHelper.WriteComment()`。
5. **ZipEntry.DateTime**：SharpCompress 的 `EntryInfo.ModifiedTime` 替代。测试验证时间戳一致性。

**工作量分布**：
- 核心 API 替换：简单（API 模式类似）
- 进度报告移植：中等（需要仔细保持字节加权进度逻辑）
- 加密路径验证：中等（需要确认 SharpCompress ZIP 加密行为与当前一致）
- 测试验证：**关键**（必须保证所有 171 测试通过）

---

## Work Objectives

### Core Objective
完全移除 SharpZipLib 依赖，所有 ZIP 操作统一使用 SharpCompress + ZipCommentHelper。

### Definition of Done
- [ ] `CompressAsync` 迁移完成，压缩功能正常，进度报告正常
- [ ] `AddToArchiveAsync` 迁移完成，添加条目到已有压缩包功能正常
- [ ] `DeleteEntriesAsync` 迁移完成，删除条目功能正常
- [ ] 分卷压缩正常
- [ ] 加密压缩正常（ZIP 2.0 / AES-256）
- [ ] 注释写入正常（通过 ZipCommentHelper 后写）
- [ ] 编码回退逻辑正常（UTF-8 → GBK）
- [ ] `OpenZipFile` 删除，无残留 SharpZipLib 引用
- [ ] `.csproj` 移除 SharpZipLib，构建通过
- [ ] 全部 171 测试通过

### Must Have
- 保持 `IArchiveEngine` 接口不变
- 保持进度报告行为一致（字节加权、100ms 节流、per-file）
- 保持重试逻辑（3 次 IOException 重试）
- 保持编码回退逻辑（UTF-8 → 系统 ANSI）
- 注释通过 `ZipCommentHelper.WriteComment` 后写

### Must NOT Have
- 不要修改 `IArchiveEngine` 接口签名
- 不要修改进度报告事件频率或语义
- 不要改动 `ExtractAsync`/`ExtractEntriesAsync`/`ListEntriesAsync`/`TestArchiveAsync`（已迁移）
- 不要引入新的 NuGet 包
- 不要修改分卷压缩的 `SplitOutputStream` 逻辑

---

## Implementation Plan

### Phase 1 — CompressAsync 迁移

**目标**：将 `ZipOutputStream` → `ZipWriter`，保持所有功能点。

**涉及范围**：Line 342-440 (CompressAsync 主体) + Line 1041-1128 (ReadFileWithRetry)

**替换模式**：

```csharp
// Before (SharpZipLib)
using var zipStream = new ZipOutputStream(fsOut);
zipStream.SetLevel(options.CompressionLevel);
if (!string.IsNullOrEmpty(options.Comment))
    zipStream.SetComment(options.Comment);
if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
    zipStream.Password = options.Password;
// ...
var entry = new ZipEntry(ZipEntry.CleanName(relativePath))
{
    DateTime = fi.LastWriteTime,
    AESKeySize = options.Encrypt ? 256 : 0
};
zipStream.PutNextEntry(entry);
// ... copy data loop with progress ...

// After (SharpCompress)
using var zipWriter = WriterFactory.Open(fsOut, ArchiveType.Zip,
    new WriterOptions(CompressionType.Deflate)
    {
        CompressionLevel = options.CompressionLevel,
        ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8 }
    });
// 加密通过 ZipWriterOptions 参数
// 注释通过 ZipCommentHelper 压缩后写入
// Write() 需要手动循环以支持进度报告
```

**关键改动**：
- `ReadFileWithRetry` 方法签名从 `ZipOutputStream` → `ZipWriter`
- 字节复制循环保留（用于进度报告），使用 `zipWriter.Write(relPath, fsInput)` 的高级 API 或保留手动 `PutNextEntry`+`Write` 方式
- 注释：`zipStream.SetComment()` → 压缩完成后 `ZipCommentHelper.WriteComment(outputPath, options.Comment)`
- 分卷：`SplitOutputStream` 继续使用，`ZipWriter` 写入到该流

**验证**：`ZipEngineTests` 中的压缩测试全部通过。

---

### Phase 2 — AddToArchiveAsync 迁移

**目标**：将 SharpZipLib 的读取/提取/重压缩全部替换为 SharpCompress。

**涉及范围**：Line 530-782

**替换点**：

| 行号 | SharpZipLib | SharpCompress |
|:----:|-------------|---------------|
| 571, 607 | `OpenZipFile()` → `ZipFile` | `OpenArchiveWithEncodingFallback()` → `IArchive` |
| 573, 609 | `foreach (ZipEntry entry in zipFile)` | `foreach (var entry in archive.Entries)` |
| 628, 906 | `zipFile.GetInputStream(entry)` | `entry.OpenEntryStream()` |
| 690, 953 | `ZipOutputStream` 重压缩 | `ZipWriter` 重压缩 |
| 702, 962 | `new ZipEntry(ZipEntry.CleanName(...))` | 直接传路径字符串 |
| 573 | `entry.Name`, `.Size`, `.DateTime` | `entry.Key`, `.Size`, `.LastModifiedTime` |
| 936 | `entry.DateTime` set on output | `entry.LastModifiedTime` |

**注意**：`AddToArchiveAsync` 的 Phase 1 提取旧条目到临时目录，Phase 2 添加新文件，Phase 3 重压缩。提取逻辑已有 SharpCompress 版本（在 ExtractAsync 中），可以直接复用模式。

---

### Phase 3 — DeleteEntriesAsync 迁移

**目标**：将 SharpZipLib 的读取替换为 SharpCompress。

**涉及范围**：Line 783-1030

**替换点**（与 AddToArchiveAsync 几乎相同）：

| 行号 | SharpZipLib | SharpCompress |
|:----:|-------------|---------------|
| 816 | `OpenZipFile()` | `OpenArchiveWithEncodingFallback()` |
| 819 | `foreach (ZipEntry entry in zipFile)` | `foreach (var entry in archive.Entries)` |
| 884 | 同上（Pass 2） | 同上 |
| 906 | `zipFile.GetInputStream(entry)` | `entry.OpenEntryStream()` |
| 953 | `ZipOutputStream` 重压缩 | `ZipWriter` 重压缩 |
| 962 | `new ZipEntry(...)` | 直接传路径字符串 |

**注意**：`DeleteEntriesAsync` 有 2-pass 结构：Pass 1 验证条目存在 + 确定保留列表；Pass 2 提取保留条目到临时目录 + 重压缩。Pass 1 的验证逻辑需要适配 `IArchiveEntry` API。

---

### Phase 4 — 清理

1. 删除 `OpenZipFile` 静态方法（不再被任何代码调用）
2. 删除 `using ICSharpCode.SharpZipLib.Zip`（ZipEngine.cs 顶部）
3. 从 `MantisZip.Core.csproj` 移除 `SharpZipLib` 包引用
4. 更新类注释 `/// ZIP 压缩引擎（基于 SharpZipLib）` → `/// ZIP 压缩引擎（基于 SharpCompress）`
5. 构建验证 + 测试验证

---

## Testing Strategy

### 自动化测试（已有 171 测试）

| 测试文件 | 覆盖范围 | 需验证 |
|----------|---------|--------|
| `ZipEngineTests.cs` | ListEntries, Compress, Extract | ✅ 全部通过 |
| `ZipEngineDeleteTests.cs` | DeleteEntries | ✅ 全部通过 |
| `CompressServiceTests.cs` | 压缩服务集成 | ✅ 全部通过 |

### 手动验证清单

- [ ] 压缩一个含中文文件名的文件夹 → 文件名正确
- [ ] 压缩加密 ZIP（AES-256）→ 解压需要密码
- [ ] 压缩 ZIP 分卷 → 分卷文件正确
- [ ] 压缩时设置注释 → 注释正确写入
- [ ] 修改已有压缩包的注释 → 注释更新，不解压
- [ ] 向已有压缩包添加文件 → 条目正确，旧条目保留
- [ ] 从压缩包删除条目 → 条目删除，其他保留
- [ ] 打开 GBK 编码的旧 ZIP → 文件名正确显示
- [ ] 解压 GBK 编码的 ZIP → 文件名正确

---

## Rollback Plan

如果迁移后出现问题：

```bash
git revert <commit-hash>
```

SharpZipLib 恢复后所有功能回到迁移前状态。

---

## Estimated Effort

| Phase | 内容 | 时间 |
|-------|------|:----:|
| Phase 1 | CompressAsync 迁移 | 1-1.5h |
| Phase 2 | AddToArchiveAsync 迁移 | 1-1.5h |
| Phase 3 | DeleteEntriesAsync 迁移 | 0.5-1h |
| Phase 4 | 清理 + 验证 | 0.5h |
| **Total** | | **3.5-4.5h** |
