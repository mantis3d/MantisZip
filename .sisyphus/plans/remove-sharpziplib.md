# 移除 SharpZipLib 依赖 — 自定义 ZIP 注释读写

## TL;DR

> **Quick Summary**: 为 ZIP 注释读写创建 `ZipCommentHelper`（直接操作 EOCD 字节），替换 `ArchiveCommentDialog` 中的 SharpZipLib 直接调用，并添加"正在保存注释"文字提示 UX。
>
> ⚠️ **范围修正**：SharpZipLib 仍在 `ZipEngine.cs` 中大量使用（压缩核心引擎），无法完全移除。本次只替换注释编辑功能的调用层。
>
> **Deliverables**:
> - `ZipCommentHelper` 工具类（Core/Utils，~80 行），直接读写 EOCD 注释字段
> - 修改 `MainWindow.xaml.cs:ReadArchiveComment()` — 改用 `ZipCommentHelper`
> - 修改 `ArchiveCommentDialog.xaml` — 添加"正在保存注释"TextBlock
> - 修改 `ArchiveCommentDialog.xaml.cs` — 改用 async + 保存中文字提示 + ZipCommentHelper
> - 新增本地化键 `Main_ArchiveComment_Saving`
> - 清理 3 处无用 import（App.xaml.cs / App.Cli.cs / MainWindow.xaml.cs）
>
> **Estimated Effort**: Low (~30 min)
> **Parallel Execution**: YES (2 条独立修改线)
> **Critical Path**: ZipCommentHelper → 调用方替换

---

## Context

### Original Request
替换 SharpZipLib，但需要保留压缩包注释原地编辑功能（不重新压缩整个压缩包）。

### Technical Analysis

当前 SharpZipLib 在整个代码库中的实际使用情况：

| 文件 | 行号 | 用途 | 能否替换 |
|--------|------|---------|----------|
| `ArchiveCommentDialog.xaml.cs` | 44-47 | `ZipFile.BeginUpdate()` + `SetComment()` + `CommitUpdate()` — 写入 ZIP 注释 | ✅ 直接字节操作 |
| `MainWindow.xaml.cs` | 497-498 | `new ZipFile()` + `.ZipFileComment` — 读取 ZIP 注释 | ✅ 直接字节操作 |
| `App.xaml.cs` | 11 | `using ICSharpCode.SharpZipLib.Zip` — 残留 import，`ZipStrings.CodePage` 已移除 | ✅ 直接删除 |
| `App.Cli.cs` | 8 | `using ICSharpCode.SharpZipLib.Zip` — 从未实际使用 | ✅ 直接删除 |
| `App.Password.cs` | 205 | 注释中提及 SharpZipLib，实际代码已用 SharpCompress | ✅ 删除注释 |

ZIP 注释存储在文件末尾的 **EOCD（End of Central Directory）** 记录中，这是一个固定格式的二进制结构：

```
偏移  大小  字段
0     4     EOCD 签名 0x06054b50
4     2     磁盘号
6     2     中央目录所在磁盘
8     2     本磁盘的中央目录记录数
10    2     中央目录记录总数
12    4     中央目录大小
16    4     中央目录起始偏移
20    2     注释长度 (n)
22    n     注释内容 (UTF-8)
```

操作 EOCD 注释 **不需要接触任何压缩数据或中央目录条目**，只需文件尾部几个字节的读写。SharpZipLib 的 `SetComment()` 内部做的也是同样的事情。

### Metis Review (Self-Check)

**潜在风险**：
- EOCD 签名可能在文件数据中巧合出现 → ZIP 规范要求从文件末尾开始搜索，取最后一个匹配（标准做法）
- 注释编码：SharpCompress 默认 UTF-8，新 helper 也使用 UTF-8，兼容
- 大文件性能：只需读/写文件末尾几百字节，O(1) 操作
- 并发写入：调用方已有 UI 线程序列化（用户手动点击保存），无需额外锁

---

## Work Objectives

### Core Objective
移除 SharpZipLib 依赖，同时保持 ZIP 注释原地编辑功能不变。

### Concrete Deliverables
1. ✅ `MantisZip.Core/Utils/ZipCommentHelper.cs` — 静态工具类，提供 `ReadCommentAsync` / `WriteCommentAsync`
2. ✅ `ArchiveCommentDialog.xaml` — 添加 "正在保存注释..." TextBlock（默认 Collapsed）
3. ✅ `ArchiveCommentDialog.xaml.cs` — 改为 async，保存时显示保存文字 + 禁用按钮，完成后关闭；替换 `ZipFile.SetComment` 为 `ZipCommentHelper.WriteCommentAsync`
4. ✅ `MainWindow.xaml.cs` `ReadArchiveComment()` — 替换 `ZipFile.ZipFileComment` 为 `ZipCommentHelper.ReadComment`
5. ✅ 删除 `App.xaml.cs` / `App.Cli.cs` 中的 `using ICSharpCode.SharpZipLib.Zip`
6. ❌ **阻塞** 从 `MantisZip.Core.csproj` 移除 `<PackageReference Include="SharpZipLib" />` — `ZipEngine.cs` 仍需 SharpZipLib 用于加密 ZIP 写入回退
7. ✅ 新增本地化键 `Main_ArchiveComment_Saving`
8. ✅ 构建验证 + `lsp_diagnostics` 清洁

### Definition of Done
- [x] `ZipCommentHelper` 实现完成（`src/MantisZip.Core/Utils/ZipCommentHelper.cs`），含 XML 文档注释
- [x] 注释读取功能正常（打开已有注释的 ZIP 能显示）
- [x] 注释写入功能正常（修改注释后保存，重新打开能看到新注释）
- [x] 无注释的 ZIP 文件也能正常处理（返回 null/空）
- [x] 大文件 ZIP 时操作正常
- [x] 所有 import 清理干净（`App.xaml.cs` 已无 SharpZipLib 引用；`App.Cli.cs` 已合并/删除）
- [ ] `SharpZipLib` 从 `.csproj` 移除后构建通过 — ⏸️ **阻塞：`ZipEngine.cs` 仍依赖 SharpZipLib (加密 ZIP 回退)**，详见 scope-correction 更新
- [x] 全部 171 个测试通过（经 `zipengine-sharpcompress-migration` 后为 183/183）

### Must Have
- 原地修改，不重新压缩
- 保持异步 API（`ReadCommentAsync` / `WriteCommentAsync`）
- UTF-8 编码（与 SharpCompress 一致）
- 文件流正确释放
- 异常处理：损坏的 ZIP / 无 EOCD 签名 / IO 错误 → 抛异常

### Must NOT Have (Guardrails)
- 不要修改任何 ZIP 数据的其他部分（中央目录、本地文件头、压缩数据）
- 不要引入新的 NuGet 依赖
- 不要改变 `ArchiveCommentDialog` 的 UI 逻辑或对话框交互流程
- 不要修改 `MainWindow.ReadArchiveComment` 的签名或调用方
- 不要修改现有的压缩/解压引擎代码

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification automated.

### Acceptance Tests (Manual)
1. 打开一个带注释的 ZIP → 注释显示在对话框中
2. 修改注释并保存 → 重新打开确认已更新
3. 打开一个无注释的 ZIP → 对话框显示空
4. 添加新注释并保存 → 重新打开看到新注释
5. 打开非 ZIP 格式 → 对话框提示不支持（现有行为不变）
6. 运行 `dotnet build` → 0 错误
7. 运行 `dotnet test` → 171/171 通过

### Rollback Plan
如果出现问题，在 git 中 `revert` 该 commit 即可恢复 SharpZipLib 引用。

---

## Implementation Plan

### Wave 1 — ZipCommentHelper（可并行）

**文件**: `src/MantisZip.Core/Utils/ZipCommentHelper.cs` (新建)

```csharp
namespace MantisZip.Core.Utils;

/// <summary>ZIP EOCD 注释原地读写工具（无需重新压缩）</summary>
public static class ZipCommentHelper
{
    /// <summary>EOCD 签名</summary>
    private const uint EocdSignature = 0x06054b50;

    /// <summary>EOCD 固定字段大小（签名4 + 磁盘号2 + ... + 注释长度2）</summary>
    private const int EocdFixedSize = 22;

    /// <summary>读取 ZIP 文件注释，无注释返回 null</summary>
    public static string? ReadComment(string archivePath)
    {
        // 1. 打开文件流
        // 2. 从末尾向前扫描 EOCD 签名（取最后一个匹配）
        // 3. 解析注释长度字段（EOCD偏移20处，2字节小端）
        // 4. 读取注释字节，UTF-8 解码
    }

    /// <summary>写入/更新 ZIP 文件注释。comment 为 null 或空字符串时清空注释。</summary>
    public static void WriteComment(string archivePath, string? comment)
    {
        // 1. 打开文件流写入模式
        // 2. 定位 EOCD 签名
        // 3. 将新注释编码为 UTF-8 字节
        // 4. 覆写注释长度 + 注释内容
        // 5. 截断文件（若新注释比旧注释短）
    }
}
```

**关键实现细节**：
- EOCD 搜索：从文件末尾向前扫描签名，取最后一个匹配（ZIP 规范要求）
- 写入逻辑：如果是覆盖（长度 ≤ 原长度），直接覆写；如果是扩展，需要重新布局 EOCD（但实际场景几乎不会超过 65535 bytes 的注释长度限制）
- 使用 `FileStream` + `BinaryReader`/`BinaryWriter`

### Wave 2 — 替换调用方（与 Wave 1 互不依赖，可并行）

**文件**: `src/MantisZip.UI/MainWindow.xaml.cs` — 修改 `ReadArchiveComment()`（约 Line 491-506）

将：
```csharp
using var zf = new ZipFile(archivePath, ICSharpCode.SharpZipLib.Zip.StringCodec.Default);
var comment = zf.ZipFileComment;
```
替换为：
```csharp
var comment = ZipCommentHelper.ReadComment(archivePath);
```

**文件**: `src/MantisZip.UI/ArchiveCommentDialog.xaml.cs` — 修改 `Save_Click()`（约 Line 44-47）

将：
```csharp
using var zf = new ZipFile(_archivePath, StringCodec.Default);
zf.BeginUpdate();
zf.SetComment(newComment);
zf.CommitUpdate();
```
替换为：
```csharp
ZipCommentHelper.WriteComment(_archivePath, newComment);
```

### Wave 3 — 清理 + 构建验证

- 删除 `App.xaml.cs:11` 的 `using ICSharpCode.SharpZipLib.Zip`（该文件已无实际使用）
- 删除 `App.Cli.cs:8` 的 `using ICSharpCode.SharpZipLib.Zip`（该文件已无实际使用）
- 更新 `App.Password.cs:205` 的注释（SharpZipLib → SharpCompress）
- ⚠️ **不移除 SharpZipLib 包引用** — `ZipEngine.cs` 仍依赖 SharpZipLib
- `dotnet build` → 0 errors
- `dotnet test` → 171/171 pass

---

## Scope Correction (post-implementation) — 已更新

**初始发现**：`ZipEngine.cs` 大量使用 SharpZipLib（`ZipFile`, `ZipOutputStream`, `ZipEntry` 等），导致无法完全移除依赖。

**后续进展**：`zipengine-sharpcompress-migration.md` 计划已完成 ZipEngine 三方法的 SharpCompress 迁移：
- ✅ `CompressAsync` → `ZipWriter`（未加密）；`ZipOutputStream` 保留为加密回退
- ✅ `AddToArchiveAsync` → `IArchive`+`IArchiveEntry`+`ZipWriter`（未加密）；`ZipOutputStream` 保留为加密回退
- ✅ `DeleteEntriesAsync` → `IArchive`+`IArchiveEntry`+`ZipWriter`（该方法无加密）
- ✅ `OpenZipFile` 静态方法已删除（68 行死代码）
- ❌ `ZipOutputStream` 约 20 行生产代码留在 CompressAsync/AddToArchiveAsync 的加密分支中
- ❌ `ReadFileWithRetryZipOutputStream` helper 保留（约 90 行），供加密分支使用

因此：
- ✅ `ZipCommentHelper` + ArchiveCommentDialog 改进：**已完成**
- ✅ ZipEngine 核心方法（Compress/Add/Delete）SharpZipLib → SharpCompress 迁移：**已完成**
- ❌ 完全移除 SharpZipLib 依赖：**阻塞**（SharpCompress ZipWriter 不支持加密 API → 见 `zipengine-sharpcompress-migration.md` post-migration analysis）

### 当前 SharpZipLib 残留范围（精确到行）

**生产代码**（`src/MantisZip.Core/Engines/ZipEngine.cs`）：
- `using ICSharpCode.SharpZipLib.Zip` (行 5)
- `ZipOutputStream` 在 CompressAsync + AddToArchiveAsync 加密分支中（约 20 行）
- `ReadFileWithRetryZipOutputStream` helper 方法（约 90 行）

**测试固件**（`tests/MantisZip.Tests/`）：
- `Fixtures/ArchiveFixtures.cs` — `ZipOutputStream` 创建加密 ZIP
- `Engines/SmartExtractTests.cs` — `ZipOutputStream`+`ZipEntry` 创建测试 ZIP
- `Engines/ZipEngineTests.cs` — `using ICSharpCode.SharpZipLib.Zip`
- `Engines/CompressServiceTests.cs` — `ZipOutputStream` 创建测试 ZIP
- `Engines/ZipEngineDeleteTests.cs` — `ZipOutputStream` 创建测试 ZIP

---

## Estimated Effort

| Item | Time | Status |
|------|------|--------|
| ZipCommentHelper 实现 | 15 min | ✅ |
| ArchiveCommentDialog UX 改进 | 10 min | ✅ |
| 替换 MainWindow.ReadArchiveComment | 2 min | ✅ |
| Import 清理 | 3 min | ✅ |
| 构建 + 测试验证 | 5 min | ✅ |
| **Total** | **~35 min** | **全部完成** |

## Rollback

```bash
git revert <commit-hash>
```
