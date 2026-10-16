# ZIP 损坏检测（CRC 校验）修复

> **状态**: 🔄 实施中（代码完成，验收收尾） | **创建**: 2026-10-15 | **优先级**: P1 | **预估工时**: 3-5h
> **来源**: 用户生成损坏压缩包做测试时发现「CRC 不匹配的 ZIP 测试/解压均不报错」
>
> **⚠️ 2026-10-16 修正**：SharpCompress 已由 `0.48.1` 升级至 **`0.50.4`**（`src/MantisZip.Core/MantisZip.Core.csproj`）。下文「0.48.1 尚无 `ExtractionOptions.CheckCrc` API」为立项时（0.48.1）的事实陈述——项目实际已在升级后的 0.50.4 上实施本修复（这也是升级动机之一）。

## 背景

为测试损坏压缩包处理，构造了一批损坏样本。其中 `corrupt-03-bad-crc.zip`（stored 条目的数据字节被翻转，中央目录完好）在 MantisZip 里**「测试压缩包」不报错、解压也不报错**，静默输出错误内容。

## 根因

**引擎用错了 SharpCompress 的 API：`IArchiveEntry.OpenEntryStream()` 返回裸流，不做 CRC 校验。**

`ZipEngine` 的测试与解压路径（4 处）都直接 `OpenEntryStream()` + 手写复制循环；代码注释曾错误地假设「SharpCompress 在读取完整流时内部会检测 CRC」。实测（SharpCompress **0.50.4**，与应用同一份 dll）推翻该假设：

| 场景 | 结果 |
|------|------|
| deflate 流损坏 | 抛 `ZlibException`（靠 zlib 自检）✅ 偶然检出 |
| 本地头魔数损坏 | 抛 `ArchiveOperationException`（解析期）✅ 偶然检出 |
| **stored 条目数据被改（CRC 不匹配）** | **读取 OK，不报错** ❌ |
| **CRC 字段被改（数据完好）** | **读取 OK，不报错** ❌ |
| `archive.Entries + OpenEntryStream()` / `ReaderFactory.OpenReader()` | 两条读路径均不校验 CRC |
| 0.48.1 vs 0.50.4 | 行为**逐字节一致** → 非版本回归，换版本无解 |

**SharpCompress 的 CRC 校验是存在的**，但只挂在官方高层提取 API 上：

- `ZipEntry.Checksum` → 返回 `ChecksumDescriptor(ChecksumKind.Crc32, ..., IsAvailable: true)`（源码 `Common/Zip/ZipEntry.cs`）；
- 校验器 `ChecksumValidationStream` 仅在 `IArchiveEntryExtensions.WriteTo/WriteToAsync` 中按 `ExtractionOptions.CheckCrc`（默认 `true`）包裹；
- 官方测试 `tests/SharpCompress.Test/Zip/ZipCrcExtractionTests.cs`：`WriteTo(..., CheckCrc=true)` 抛 `InvalidFormatException`；
- 0.48.1 **尚无** `ExtractionOptions.CheckCrc` API（编译报错）→ 该机制为 0.48.1 之后新增。

**边界安全**：官方路径内部已正确处理目录条目、`IsCrcAvailable=false`、以及 WinZip AES **AE-2**（其 CRC 字段按设计为 0，`IsReliableCrcMetadata` 返回 false 跳过校验）——故对加密包不会误报。

## 方案（用户拍板：用官方 CheckCrc 路径）

不自己实现 CRC32（避免重踩加密/数据描述符等边界），改用官方带校验的提取路径。实测 `entry.WriteTo(dest, new ExtractionOptions { CheckCrc = true }, IProgress<ProgressReport>)`：

- 不释放调用方传入的目标流；
- 进度经 `ProgressReport { EntryPath, BytesTransferred, TotalBytes, PercentComplete }` 上报；
- CRC 不匹配抛 `SharpCompress.Common.InvalidFormatException`（消息 `CRC mismatch for entry '...'. Expected 0x.., actual 0x..`）。

### 已实施改动

| 文件 | 改动 |
|------|------|
| `Core/Engines/ZipEngine.cs` | 新增 `CreateCrcExtractionOptions()`（`CheckCrc=true`，`BufferSize=CopyBufferSize` 保吞吐）+ `TryDeleteCorruptOutput()`（删损坏输出）；`TestArchiveAsync` + 4 处解压循环（串行/并行 × 全量/过滤）改走 `WriteTo`/`WriteToAsync`；4 处新增 `catch (InvalidFormatException)` → 逐条目 `Failed`（不中止整批） |
| `Core/Abstractions/ArchiveEngine.cs` | `EntryFailureReason` 新增 `CrcMismatch` |
| `Core/Utils/FailureReasonMapper.cs` | `InvalidFormatException`（消息含 CRC）→ `CrcMismatch` |
| `UI.Avalonia/ViewModels/ProgressViewModel.cs` | `ReasonText` 增加 `CrcMismatch` 分支 |
| `UI.Avalonia/Localization/strings.{zh-CN,en,zh-TW}.json` | 新增 `Error_Reason_CrcMismatch`（三语成对，BOM+CRLF+2 空格缩进） |
| `tests/MantisZip.Tests/Engines/ZipEngineCrcValidationTests.cs` | **新增** 4 条回归测试（损坏检测 × 2 + 正常对照 × 2） |

**决策点**：
- 解压失败时**删除**已写出的损坏文件（CRC 在读完时才校验，此时文件已含内容错误的完整数据，保留会误导用户）；
- 只把消息含 "CRC" 的 `InvalidFormatException` 归为 `CrcMismatch`，其它格式异常仍归 `Other`（`FailureReasonMapper` 启发式，符合规则 13 豁免）。

## 验证

- [x] `dotnet build` Core：0 错误 0 警告
- [x] 新增测试 4/4 通过（`ZipEngineCrcValidationTests`）
- [x] 全量 `MantisZip.Tests`：684 通过 / 3 跳过 / 0 失败
- [ ] 全量 `MantisZip.UI.Avalonia.Tests`：首轮 30 失败均为 `No precompiled XAML found for ResultTreeView`（增量构建毒化，规则 17；与本次改动无关），**待 `-t:Rebuild` 全量重建后复验**
- [ ] 用真实损坏样本 `corrupt-03-bad-crc.zip` / `corrupt-13-crc-field-flipped.zip` 手测「测试压缩包」+ 解压（UI 层）

## 边界

- 不动 `OpenEntryStream()` 的其它用途（压缩端把源条目解到临时目录：`ZipEngine.cs` 2736/3522，非解压流程，本次不纳入）；
- 不改 7z / tar.gz 的校验（7z 由 7z.dll 校验、gzip CRC 由解压器自检，已正常检出）；
- 不新增/不改变公开 API 签名；
- 未提交（提交前需按规则 3 同步 `docs/PROGRESS.md` 与 `docs/progress-avalonia-detail.md`）。
