# 新增压缩格式支持计划

> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜⬜] (0/9)
> **前置依赖**: 无（SharpCompress 0.48.1 内置 BZip2/XZ/Zstd；Brotli 用 .NET 内置 `BrotliStream`；只读格式复用现有 7z.dll 26.00 自动检测）
> **适用范围**: Core + Avalonia（规则 11：新功能只进 Avalonia；WPF 仅通过 Core 变更被动受益）

---

## 动机

### 现状

| 格式 | 解压浏览 | 压缩输出 | 引擎 |
|------|:--------:|:--------:|------|
| ZIP | ✅ | ✅ | ZipEngine (SharpCompress) |
| 7z | ✅ | ✅ | SevenZipEngine (SharpSevenZip) |
| RAR | ✅ | ❌ | SevenZipEngine (只读) |
| ISO | ✅ | ❌ | SevenZipEngine (只读) |
| TAR 裸格式 (.tar) | ✅ | ✅（Core 已实现，UI 未放开） | TarGzEngine |
| GZip 单文件 (.gz) | ✅ | ✅（Core 已实现，UI 未放开） | TarGzEngine |
| tar.gz | ✅ | ✅ | TarGzEngine |

**压缩输出仅 3 种格式**，而 SharpCompress 0.48.1 底层原生支持更多（BZip2/XZ/Zstd 读写），只需包装新 Engine 类即可解锁。本项目已有 `IArchiveEngine` 策略模式，新增格式代价低。

### 目标格式（2026-08-06 依赖核验后修正）

**方案 A：压缩/解压全能（本计划主线）**

| 格式 | 解压 | 压缩 | 依赖 | 优先级 |
|------|:----:|:----:|------|:------:|
| **TAR 裸格式 (.tar)** | ✅ 已有 | ✅ Core 已有，仅 UI 放开 | SharpCompress | P0 |
| **GZip 单文件 (.gz)** | ✅ 已有 | ✅ Core 已有，仅 UI 放开 | SharpCompress | P0 |
| **BZip2 (.bz2)** | ➕ 新增 | ➕ 新增 | SharpCompress 原生 | P1 |
| **Tar.bz2** | ➕ 新增 | ➕ 新增 | SharpCompress 原生 | P1 |
| **XZ (.xz)** | ➕ 新增 | ➕ 新增 | SharpCompress 原生 | P1 |
| **Tar.xz** | ➕ 新增 | ➕ 新增 | SharpCompress 原生 | P1 |
| **Zstandard (.zst)** | ➕ 新增 | ➕ 新增 | **SharpCompress 0.48.1 内置** | P1（原 P3 提升） |
| **Tar.zst** | ➕ 新增 | ➕ 新增 | SharpCompress 内置 | P1 |
| **Brotli (.br)** | ➕ 新增 | ➕ 新增 | **.NET 内置 `BrotliStream`**（方案 B） | P1 |
| **Tar.br** | ➕ 新增 | ➕ 新增 | .NET 内置（手动包装） | P1 |

**方案 B：7z.dll 只读格式解锁（读 ~15 种，零新增依赖）**

7z.dll 26.00（SharpSevenZip 已捆绑分发）原生支持读取 49 种格式，其中对普通用户有实际意义的档案格式可全部解锁（只读）：

| 格式 | 扩展名 | 说明 | 优先级 |
|------|--------|------|:------:|
| **CAB** | `.cab` | Windows 安装包（原计划已有） | P2 |
| **ARJ** | `.arj` | 老牌归档格式，存量文件仍常见 | P2 |
| **LZH / LHA** | `.lzh` `.lha` | 日系归档格式 | P2 |
| **CHM** | `.chm` | Windows 帮助文档（复合文档） | P3 |
| **CPIO** | `.cpio` | Unix 打包格式 | P3 |
| **DEB** | `.deb` | Debian 包（ar + tar.xz） | P3 |
| **RPM** | `.rpm` | Red Hat 系包 | P3 |
| **WIM** | `.wim` | Windows 映像 | P3 |
| **XAR** | `.xar` | macOS 打包格式 | P3 |
| **LZMA** | `.lzma` | 裸 LZMA 流 | P3 |
| **MSI** | `.msi` | Windows 安装包 | P3 |
| **NSIS** | `.exe`(NSIS 安装器) | 安装器读取（扩展名冲突，仅魔数路径） | 可选 |
| **DMG** | `.dmg` | macOS 映像（Windows 上意义有限） | 可选 |

**变更说明（对比旧计划）**：
- ~~Zstd 需引入 `ZstdNet`/`ZstdSharp` 外部依赖~~ → **SharpCompress 0.48.1 已内置完整 Zstandard 实现**（`CompressionType.ZStandard` + `SharpCompress.Compressors.ZStandard` 公开类），零外部依赖，优先级从 P3 提升到 P1
- ~~CAB 用 SharpCompress `CabArchive`~~ → **SharpCompress 0.48.1 无 CAB 支持**（`ArchiveType` 枚举无 Cab 成员），改走现有 `SevenZipEngine` 只读分支（7z.dll 支持 CAB 读取，与 RAR/ISO 同路）
- ~~Brotli 排除~~ → **方案 B 采用 .NET 内置 `System.IO.Compression.BrotliStream`**（net9.0 自带，零依赖），SharpCompress 无需支持
- ~~ARJ / LZH / CPIO 排除~~ → **方案 B 复用 7z.dll 自动检测解锁只读**（已确认 `SharpSevenZipExtractor(archivePath)` 单参构造自动检测格式，无需显式 `InArchiveFormat`）

### 依赖核验记录（2026-08-06，反编译 SharpCompress 0.48.1 + 外部库生态调研）

| 能力 | 结论 |
|------|------|
| `CompressionType` 枚举 | 含 `None, GZip, BZip2, PPMd, Deflate, Rar, LZMA, LZMA2, LZip, Xz, ..., ZStandard, ...` ✅ |
| `TarWriterOptions(CompressionType, bool)` | 直接接受任意压缩类型 → tar 组合写入可复用 TarGzEngine 的 `TarWriter.OpenWriter` 模式 ✅ |
| `SharpCompress.Compressors.ZStandard` | 公开 `CompressionStream(stream, level, bufferSize, leaveOpen)` / `DecompressionStream(stream, bufferSize, checkEndOfStream, leaveOpen)` → 单文件 .zst 读/写 ✅ |
| `ArchiveType` 枚举 | `Rar, Zip, Tar, SevenZip, GZip, Arc, Arj, Ace, Lzw` — **无 Cab** ❌（CAB 走 7z.dll） |
| `BZip2Stream` / `XZStream` | 存在（SharpCompress.Compressors.BZip2 / LZMA）✅ |
| 魔数检测 `FileFormatDetector` | 已支持 XZ (`FD 37 7A 58 5A 00`) 与 Zstd (`28 B5 2F FD`) ✅；BZip2 (`BZh`) 与 CAB (`MSCF`) 未检测（可后续补充） |
| **Brotli（方案 B）** | `System.IO.Compression.BrotliStream` — .NET 9 内置，`CompressionMode.Compress/Decompress`，quality 0–11。SharpCompress **无** `CompressionType.Brotli`，tar.br 需手动包装外层流 ✅ |
| **7z.dll 只读解锁（方案 B）** | `SevenZipEngine` 所有 extractor 构造均为 `new SharpSevenZipExtractor(archivePath[, password])` — **自动格式检测**，无需显式 `InArchiveFormat`。已确认 7z.dll 26.00 的 `InArchiveFormat` 枚举含 Arj/BZip2/Cab/Chm/Cpio/Deb/GZip/Iso/Lzh/Lzma/Nsis/Rpm/Tar/Wim/Xar/Udf/Msi/SquashFS/Dmg 等 49 项 ✅ |
| **外部库调研** | libarchive 绑定 (LibArchive.Net) 有增量（cpio/ar/xar 写入）但与 7z.dll 重叠高、多一份 native dll → **不引入**；wimlib/SharpZipLib/ZstdSharp 均冗余 ❌ |

---

## 改动范围总览

涉及 **~16 个文件**（Core 为主，Avalonia UI 为 UI 侧；按 Phase 渐进）：

### 核心库（Core）— 两 UI 共享

| 文件 | 改动 |
|------|------|
| `Core/Abstractions/ArchiveEngine.cs` | `ArchiveFormat` 枚举新增 `BZip2`, `XZ`, `Zstd`, `Brotli`, `Cab`, `Arj`, `Lzh`, `Chm`, `Cpio`, `Deb`, `Rpm`, `Wim`, `Xar`, `Lzma`, `Msi`；`SupportedExtensions` 扩展；`GetFormatByExtension` / `GetEngineByExtension` 新 case；`MapFileFormatToArchiveFormat` 补 `FileFormat.Xz` / `FileFormat.Zstd` 映射；工厂注册新引擎 |
| `Core/Engines/BZip2Engine.cs` | **新建** — BZip2 压缩/解压引擎（含 .tar.bz2 组合） |
| `Core/Engines/XzEngine.cs` | **新建** — XZ 压缩/解压引擎（含 .tar.xz 组合） |
| `Core/Engines/ZstdEngine.cs` | **新建** — Zstandard 压缩/解压引擎（含 .tar.zst 组合） |
| `Core/Engines/BrotliEngine.cs` | **新建** — Brotli 压缩/解压引擎（含 .tar.br 组合，.NET 内置 BrotliStream） |
| `Core/Engines/SevenZipEngine.cs` | `CanHandle` 增加 `Cab, Arj, Lzh, Chm, Cpio, Deb, Rpm, Wim, Xar, Lzma, Msi`（只读复用 7z.dll 自动检测） |
| `Core/Utils/FileFormatDetector.cs` | （可选）补 BZip2 (`BZh`) / CAB (`MSCF`) / Brotli (`CE B2 CF 81`) 魔数 |

### Avalonia UI（主力）

| 文件 | 改动 |
|------|------|
| `UI.Avalonia/Services/CompressionOptionData.cs` | `ArchiveFormatValues` 增加 `"tar"`, `"gz"`, `"bz2"`, `"xz"`, `"zst"`, `"br"`；新增格式显示名数据 |
| `UI.Avalonia/Models/AppSettings.cs` | `DefaultFormat` 取值域注释；新增 `AssocBZip2` / `AssocXz` / `AssocZstd` / `AssocBrotli` / `AssocCab` 关联开关 |
| `UI.Avalonia/Services/ShellIntegration.cs` | `GetProgId` switch 增加新格式 case（`SupportedExtensions` 已直接引用 Core 单一数据源，自动生效） |
| `UI.Avalonia/Localization/strings.zh-CN.json` / `strings.en.json` | 新格式显示名 key（成对添加） |
| `UI.Avalonia/ViewModels/MainWindowViewModel.cs` | 若存在格式→显示名映射则补充（核查 `FormatDisplay` 相关逻辑） |

### WPF（遗留，不主动改）

- 仅通过 Core 变更被动受益（打开/浏览/提取新格式文件可用）
- 不新增 UI 项、不扩 ShellIntegration、不加关联开关（规则 11）

---

## 架构决策

### 决策 1：三个新引擎各自独立，纯格式 + tar 组合内置

与 TarGzEngine 一致（GZip 组合内置）：每个新格式一个 Engine 类，内部按扩展名区分单文件 vs tar 组合：

| 引擎 | 单文件格式 | tar 组合 |
|------|-----------|---------|
| `BZip2Engine` | `.bz2` | `.tar.bz2` / `.tbz` / `.tbz2` |
| `XzEngine` | `.xz` | `.tar.xz` / `.txz` |
| `ZstdEngine` | `.zst` | `.tar.zst` |
| `BrotliEngine` | `.br` | `.tar.br` |

`GetFormatByExtension` 将 `.tar.bz2` → `BZip2`、`.tar.xz` → `XZ`、`.tar.zst` → `Zstd`、`.tar.br` → `Brotli`（与 `.tar.gz` → `Tar` 的既有处理一致）。

### 决策 2：tar 组合写入复用 TarWriter 模式

与 TarGzEngine.CompressAsync 完全同构，仅替换压缩类型：

```csharp
// 模板 — 以 BZip2Engine 为例
using var fileStream = File.Create(outputPath);
var compressionType = isTarCombo ? CompressionType.BZip2 : CompressionType.None;
using IWriter writer = TarWriter.OpenWriter(fileStream, new TarWriterOptions(compressionType, true)
{
    CompressionLevel = options.CompressionLevel
});
```

Zstd 组合写入同样用 `CompressionType.ZStandard`（TarWriterOptions 接受）。

### 决策 3：单文件压缩/解压用原生 Stream 包装

| 格式 | 压缩 | 解压 |
|------|------|------|
| BZip2 | `BZip2Stream(fileStream, CompressionMode.Compress)` 或 `BZip2Writer` | `BZip2Stream(fileStream, CompressionMode.Decompress)` |
| XZ | `XZStream` | `XZStream` |
| Zstd | `SharpCompress.Compressors.ZStandard.CompressionStream(stream, level, ...)` | `SharpCompress.Compressors.ZStandard.DecompressionStream(stream, ...)` |
| Brotli | `new BrotliStream(fileStream, CompressionMode.Compress)`（quality 映射见下） | `new BrotliStream(fileStream, CompressionMode.Decompress)` |

**Brotli quality 映射**：`ArchiveOptions.CompressionLevel` (1–9) → Brotli quality（0–11，默认 4）。映射策略：`level * 11 / 9` 四舍五入，或直接传 `level`（0–9 均在合法区间）。Phase 实施时用真实文件验证压缩率后定稿。

### 决策 4：tar 组合解压 — 手动解压外层压缩流（关键验证点）

TarGzEngine 的解压依赖 `TarReader.OpenReader(inputStream, new ReaderOptions { LookForHeader = true })` 让 TarReader 自动检测 gzip 头。**BZip2/XZ/Zstd/Brotli 组合需确认 `LookForHeader` 是否同样自动识别**——若不支持，需手动包装：

```csharp
// 模板 — tar.bz2 解压（若 LookForHeader 不识别 BZip2）
using var fileStream = File.OpenRead(archivePath);
using var bz2Stream = new BZip2Stream(fileStream, CompressionMode.Decompress, false);
using var reader = TarReader.OpenReader(bz2Stream, new ReaderOptions { LookForHeader = true });
```

> **待验证**：SharpCompress `TarReader.LookForHeader` 对 BZip2/XZ/Zstd/Brotli 的自动识别能力（Phase 2 实现时用真实文件验证）。

**Brotli 特例**：SharpCompress **无** `CompressionType.Brotli`，故 tar.br 写入必须手动包装外层流（`BrotliStream` 包 FileStream，TarWriter 用 `CompressionType.None` 写原始 tar 到 BrotliStream 上）：

```csharp
// tar.br 写入 — Brotli 手动包装（决策 4 推论）
using var fileStream = File.Create(outputPath);
using var brStream = new BrotliStream(fileStream, CompressionMode.Compress);
using IWriter writer = TarWriter.OpenWriter(brStream, new TarWriterOptions(CompressionType.None, true)
{
    CompressionLevel = options.CompressionLevel
});
```

### 决策 5：UI 格式列表 — 扩展 CompressionOptionData（不引入第二数据源）

**否决旧计划的 `ArchiveEngineFactory.GetCompressibleFormats()` 方案**（会造成 Core 工厂 + `CompressionOptionData.ArchiveFormatValues` 两个事实来源，且 Core 无法持有本地化显示名）。

做法：扩展 `CompressionOptionData.ArchiveFormatValues`（压缩窗口 + 设置窗口 `DefaultFormat` 共用）与对应显示名映射。新格式的显示名 key 走本地化（`Format_BZip2` 等），`CompressionOptionData.ComboOption` 模式不变。

### 决策 6：文件关联 — 独立开关（与现有 Assoc* 一致）

`AppSettings` 新增 `AssocBZip2` / `AssocXz` / `AssocZstd` / `AssocBrotli` / `AssocCab`（默认值对齐现有：压缩格式 true、CAB 只读 false）。`ShellIntegration.GetProgId` 增加对应 case。

**只读解锁格式（ARJ/LZH/CHM 等）不单独加开关**：它们不进关联设置页，但 `SupportedExtensions`（Core 单一数据源）扩展后，ShellExt 右键菜单"打开压缩包"会自动覆盖（`AppliesTo` 过滤器来自该数组）。用户如想双击打开可走既有 `CustomAssocExtensions` 扩展点。

### 决策 7：顺手修复 GZip 扩展名映射不一致

现状：`GetFormatByExtension(".gz")` 返回 `Tar`（`.tar or .tgz or .gz => ArchiveFormat.Tar`），而魔数路径 `MapFileFormatToArchiveFormat(FileFormat.Gz)` 返回 `GZip`。功能上因 TarGzEngine 同时 `CanHandle(Tar)` 和 `CanHandle(GZip)` 未出问题，但语义不一致。趁本次改动统一为 `.gz` → `GZip`。

### 决策 8：7z.dll 只读解锁 — 纯映射，引擎零改动

**已验证**：`SevenZipEngine` 的 6 处 `SharpSevenZipExtractor` 构造全部为 `new SharpSevenZipExtractor(archivePath[, password])` — 7z.dll **自动检测格式**，无需（也从未）显式传 `InArchiveFormat`。因此解锁 ARJ/LZH/CHM/CPIO/DEB/RPM/WIM/XAR/LZMA/MSI/CAB 只读 = **仅**：
1. `ArchiveFormat` 枚举新增成员
2. `GetFormatByExtension` / `GetEngineByExtension` 加扩展名 case
3. `SevenZipEngine.CanHandle` 加 `or ArchiveFormat.Xxx`
4. `SupportedExtensions` 追加（右键菜单自动生效）

`ListEntriesAsync` / `ExtractAsync` / `TestArchiveAsync` / 密码流程零改动。风险点：7z.dll 自动检测对某格式可能误判 → 扩展名 case 直接定格式后，engine 侧对**魔数路径**仍走检测，若魔数与扩展名冲突，按既有 `_currentFormat` 逻辑（扩展名优先）处理。

---

## 分阶段实现

### Phase 0：基础设施（30min）

**目标：** 扩展枚举 + 工厂 + 格式映射，不改引擎逻辑。

文件：
- `Core/Abstractions/ArchiveEngine.cs`

改动：
1. `ArchiveFormat` 枚举新增 `BZip2`, `XZ`, `Zstd`, `Brotli`, `Cab`, `Arj`, `Lzh`, `Chm`, `Cpio`, `Deb`, `Rpm`, `Wim`, `Xar`, `Lzma`, `Msi`
2. `ArchiveEngineFactory.SupportedExtensions` 追加 `.bz2`, `.tbz`, `.tbz2`, `.tar.bz2`, `.xz`, `.txz`, `.tar.xz`, `.zst`, `.tar.zst`, `.br`, `.tar.br`, `.cab`, `.arj`, `.lzh`, `.lha`, `.chm`, `.cpio`, `.deb`, `.rpm`, `.wim`, `.xar`, `.lzma`, `.msi`
3. `GetFormatByExtension` 新增 case（注意 `.tar.bz2` 等组合需在 `Path.GetExtension` 之前用 `EndsWith` 判断）：
   - `.tar.bz2` / `.tbz` / `.tbz2` / `.bz2` → `BZip2`
   - `.tar.xz` / `.txz` / `.xz` → `XZ`
   - `.tar.zst` / `.zst` → `Zstd`
   - `.tar.br` / `.br` → `Brotli`
   - `.cab` → `Cab`；`.arj` → `Arj`；`.lzh` / `.lha` → `Lzh`；`.chm` → `Chm`；`.cpio` → `Cpio`；`.deb` → `Deb`；`.rpm` → `Rpm`；`.wim` → `Wim`；`.xar` → `Xar`；`.lzma` → `Lzma`；`.msi` → `Msi`
   - **顺手修复**：`.gz` → `GZip`（原返回 `Tar`）
4. `GetEngineByExtension` 同样扩展（`.tar.bz2` 的 `Path.GetExtension` 结果是 `.bz2`，case `.bz2` 命中即可，引擎内部再判断完整路径；只读格式全部映射到 `GetEngine(ArchiveFormat.Xxx)` → 工厂按 `CanHandle` 落到 SevenZipEngine）
5. `MapFileFormatToArchiveFormat` 补 `FileFormat.Xz => ArchiveFormat.XZ`、`FileFormat.Zstd => ArchiveFormat.Zstd`
6. 工厂静态构造器先不注册新引擎（引擎未实现时返回 null 不抛）

```
- [ ] ArchiveFormat 枚举扩展（15 个新成员）
- [ ] SupportedExtensions 扩展
- [ ] GetFormatByExtension / GetEngineByExtension 新 case + .gz→GZip 修复
- [ ] MapFileFormatToArchiveFormat 补 Xz/Zstd 映射
```

### Phase 1：UI 放开 TAR 裸格式 + GZip 单文件（30min）

**目标：** 压缩设置窗口可选纯 `.tar` 和单文件 `.gz`。

Core 已具备全部能力（`TarGzEngine.CompressAsync` 按扩展名分流：`.tar` → `CompressionType.None`，`.gz` → 单文件 GZipWriter），本阶段纯 UI。

文件：
- `UI.Avalonia/Services/CompressionOptionData.cs` — `ArchiveFormatValues` 增加 `"tar"`, `"gz"`
- `UI.Avalonia/ViewModels/CompressSettingsViewModel.cs` — 核查格式→输出扩展名映射，确认 `.tar`/`.gz` 自然生效
- 本地化 key（如 `Format_Tar`, `Format_GZip`）成对添加

```
- [ ] ArchiveFormatValues 增加 tar/gz
- [ ] 验证压缩输出 .tar（无压缩层）与 .gz（单文件）正确
- [ ] 7z/WinRAR 验证生成文件可打开
```

### Phase 2：BZip2 支持（3-4h）

**目标：** 完整 BZip2 读/写，含 `.bz2` 和 `.tar.bz2`。

文件：
- `Core/Engines/BZip2Engine.cs`（新建，~250 行）
- `Core/Abstractions/ArchiveEngine.cs` — 注册引擎
- `UI.Avalonia/Services/CompressionOptionData.cs` + 本地化

BZip2Engine 实现要点：

| 方法 | 技术路径 | 备注 |
|------|---------|------|
| `CanHandle` | `format == ArchiveFormat.BZip2` | |
| `ListEntriesAsync` | 单文件：`BZip2Stream` 解压；tar.bz2：`TarReader.OpenReader(bz2Stream, LookForHeader)` | 参照 TarGzEngine |
| `ExtractAsync` | 同上，逐文件 `WriteToFile` + 冲突处理 | 复用 `FileConflictHelper` |
| `CompressAsync` | 多文件：`TarWriter.OpenWriter(fileStream, TarWriterOptions(BZip2, true))`；单文件：`BZip2Stream` CopyFrom | 参照 TarGzEngine |
| `TestArchiveAsync` | 遍历全部入口解压到空流 | |
| `ExtractEntriesAsync` | 按 key 过滤（tar.bz2 内条目） | 过滤解压需实现 |
| `CanAdd / CanDelete` | `false` | BZip2 不支持原地修改 |

**压缩级别映射**：`ArchiveOptions.CompressionLevel` (1–9) → BZip2 level（1–9 原生对应）。

```
- [ ] 验证 TarReader LookForHeader 是否自动识别 BZip2（决策 4 关键验证点）
- [ ] 新建 BZip2Engine.cs
- [ ] ListEntriesAsync / ExtractAsync / CompressAsync / TestArchiveAsync 实现
- [ ] ArchiveEngineFactory 注册
- [ ] UI ArchiveFormatValues + 本地化
- [ ] 验证：压缩 .bz2 / .tar.bz2 → 解压 → 内容一致；7z 可打开
```

### Phase 3：XZ 支持（2-3h）

**目标：** 完整 XZ 读/写，含 `.xz` 和 `.tar.xz`。

文件：
- `Core/Engines/XzEngine.cs`（新建，~250 行）
- `Core/Abstractions/ArchiveEngine.cs` — 注册引擎
- `UI.Avalonia` 同步

XZ 实现与 BZip2 几乎完全对称，不同点：

| 方面 | BZip2 | XZ |
|------|-------|-----|
| TarWriter 压缩类型 | `CompressionType.BZip2` | `CompressionType.Xz` |
| 单文件 Stream | `BZip2Stream` | `XZStream`（SharpCompress.Compressors.LZMA） |
| 压缩级别映射 | 1-9 → 1-9 | 1-9 → XZ preset（0-9，`CompressionLevel-1` 或直接传） |

```
- [ ] 新建 XzEngine.cs
- [ ] ListEntriesAsync / ExtractAsync / CompressAsync / TestArchiveAsync 实现
- [ ] ArchiveEngineFactory 注册
- [ ] UI ArchiveFormatValues + 本地化
- [ ] 验证：压缩 .xz / .tar.xz → 解压 → 内容一致；xz -d 可解压
```

### Phase 4：Zstandard 支持（1-2h，原 3-5h 缩减）

**目标：** 完整 Zstd 读/写，含 `.zst` 和 `.tar.zst`。**无外部依赖**（SharpCompress 0.48.1 内置）。

文件：
- `Core/Engines/ZstdEngine.cs`（新建，~250 行）
- `Core/Abstractions/ArchiveEngine.cs` — 注册引擎
- `UI.Avalonia` 同步

关键 API（已确认公开）：
- 单文件压缩：`new SharpCompress.Compressors.ZStandard.CompressionStream(stream, level, bufferSize, leaveOpen)`
- 单文件解压：`new SharpCompress.Compressors.ZStandard.DecompressionStream(stream, bufferSize, checkEndOfStream, leaveOpen)`
- tar.zst 写入：`TarWriter.OpenWriter(fileStream, new TarWriterOptions(CompressionType.ZStandard, true))`
- tar.zst 解压：`DecompressionStream` 包装 → `TarReader.OpenReader(..., LookForHeader)`（若 LookForHeader 不识别则手动包装，同决策 4）

**压缩级别映射**：Zstd level 范围 1–22（默认 3），`ArchiveOptions.CompressionLevel` (1–9) 直接映射或乘 2（2–18），Phase 实施时定。

```
- [ ] 新建 ZstdEngine.cs
- [ ] 单文件 .zst 读/写 + tar.zst 组合读/写
- [ ] ArchiveEngineFactory 注册
- [ ] UI ArchiveFormatValues + 本地化
- [ ] 验证：压缩 .zst / .tar.zst → 解压 → 内容一致；zstd -d 可解压；压缩率对比
```

### Phase 5：Brotli 支持（2-3h，方案 B）

**目标：** 完整 Brotli 读/写，含 `.br` 和 `.tar.br`。**零外部依赖**（.NET 内置 `BrotliStream`）。

文件：
- `Core/Engines/BrotliEngine.cs`（新建，~250 行）
- `Core/Abstractions/ArchiveEngine.cs` — 注册引擎
- `UI.Avalonia/Services/CompressionOptionData.cs` + 本地化

关键 API（.NET 内置，无 SharpCompress 依赖）：
- 单文件压缩：`new BrotliStream(fileStream, CompressionMode.Compress)`（可传 quality 0–11，`BrotliEncoder` 底层）
- 单文件解压：`new BrotliStream(fileStream, CompressionMode.Decompress)`
- **tar.br 写入：BrotliStream 包 FileStream + `TarWriter.OpenWriter(brStream, TarWriterOptions(CompressionType.None, true))`**（SharpCompress 无 `CompressionType.Brotli`，手动外层包装，见决策 4 推论）
- tar.br 解压：`BrotliStream(File.OpenRead, Decompress)` 包装 → `TarReader.OpenReader(brStream, LookForHeader)`（同决策 4 验证点）

**压缩级别映射**：Brotli quality 0–11，`ArchiveOptions.CompressionLevel` (1–9) 直接映射（1–9 均在合法区间）或 `*11/9`，Phase 实施时用真实文件对比压缩率定稿。

```
- [ ] 新建 BrotliEngine.cs
- [ ] 单文件 .br 读/写 + tar.br 组合读/写（手动外层包装）
- [ ] ArchiveEngineFactory 注册
- [ ] UI ArchiveFormatValues + 本地化
- [ ] 验证：压缩 .br / .tar.br → 解压 → 内容一致；brotli -d 可解压；压缩率对比
```

### Phase 6：7z.dll 只读格式解锁（1-2h，方案 B 扩展）

**目标：** 打开/浏览/提取 11 种 7z.dll 原生只读格式（CAB/ARJ/LZH/CHM/CPIO/DEB/RPM/WIM/XAR/LZMA/MSI），不输出。

**路线：纯映射，引擎零改动（决策 8）。**

文件：
- `Core/Engines/SevenZipEngine.cs` — `CanHandle` 增加 `or ArchiveFormat.Cab or Arj or Lzh or Chm or Cpio or Deb or Rpm or Wim or Xar or Lzma or Msi`
- `Core/Abstractions/ArchiveEngine.cs` — Phase 0 已完成映射与 `SupportedExtensions`
- `Core/Utils/FileFormatDetector.cs` —（可选）补 `MSCF`（CAB）、`BZh`（BZip2）、`CE B2 CF 81`（Brotli）魔数

实现要点：
- 7z.dll 自动检测（`new SharpSevenZipExtractor(archivePath)`）原生支持全部 11 格式，`ListEntriesAsync` / `ExtractAsync` / `TestArchiveAsync` 走既有只读逻辑（与 RAR/ISO 同路），**无任何引擎改动**
- `CanAdd` / `CanDelete` 保持 `false`（只读）
- **UI 无需改动**（只读格式不进 FormatComboBox；文件关联走 `CustomAssocExtensions` 扩展点，决策 6）
- NSIS/DMG 等可选格式：扩展名冲突（NSIS 是 .exe）或定位模糊，仅走魔数路径，Phase 7 验证后决定是否纳入

```
- [ ] SevenZipEngine.CanHandle 增加 11 个只读格式
- [ ] 验证：逐一打开 .cab/.arj/.lzh/.chm/.cpio/.deb/.rpm/.wim/.xar/.lzma/.msi → 正常浏览/提取
- [ ] （可选）FileFormatDetector 补 MSCF/BZh/Brotli 魔数
```

### Phase 7：文件关联 + 收尾（1h）

**目标：** 新格式系统级文件关联。

文件：
- `UI.Avalonia/Models/AppSettings.cs` — 新增 `AssocBZip2` / `AssocXz` / `AssocZstd` / `AssocBrotli` / `AssocCab`
- `UI.Avalonia/Services/ShellIntegration.cs` — `GetProgId` switch 增加 `.bz2` / `.xz` / `.zst` / `.br` / `.cab` case（含组合扩展名 `.tar.bz2` 等，参照 `.tgz`/`.tar.gz` 处理）
- 设置窗口关联 UI（SettingsWindow，若按现有 Assoc* 模式逐项列出）

注意：WPF 版 AppSettings.cs 的 `DefaultFormat` 注释与字段保持同步（共享契约，规则 11 例外——AppSettings 字段两边需同步）。

```
- [ ] AppSettings 新增 AssocBZip2/AssocXz/AssocZstd/AssocBrotli/AssocCab
- [ ] ShellIntegration.GetProgId 新 case
- [ ] 设置窗口关联项 UI + 本地化
- [ ] 验证：安装关联后双击 .bz2/.xz/.zst/.br/.cab 用 MantisZip 打开
```

### Phase 8：全面验证（1h）

```
- [ ] dotnet build（Core + Avalonia）通过
- [ ] lsp_diagnostics 无新增 error/warning
- [ ] 每种新格式：压缩 → 解压 → 内容一致
- [ ] 打开外部工具生成的新格式文件（7z/WinRAR/zstd/xz 命令行）→ 正常浏览/提取
- [ ] 进度报告正常（ProgressWindow）
- [ ] 加密 ZIP 无关路径回归（ZipEngine 不受影响）
- [ ] 本地化：中英文切换显示正常
```

---

## 验证清单

### 每个 Phase 通用验证

```
- [ ] dotnet build 通过（Core + Avalonia）
- [ ] 打开对应格式压缩包 → 正常浏览文件列表
- [ ] 提取到本地 → 文件完整、目录结构正确
- [ ] 压缩（如支持）→ 生成有效文件
- [ ] 7z/WinRAR 验证生成的文件可正常打开
- [ ] 进度报告正常（ProgressWindow）
- [ ] lsp_diagnostics 无新 warning/error
```

### 各格式特殊验证

| 格式 | 特殊验证项 |
|------|-----------|
| `.tar` | tar tvf 验证无压缩层 |
| `.gz` | gzip -d 验证可正常解压 |
| `.bz2` | 多文件 tar.bz2 解压后目录结构；bzip2 -d 验证 |
| `.xz` | xz -d 验证；高压缩级别性能 |
| `.zst` | zstd -d 验证；压缩率对比（对已知数据集 vs gzip/xz） |
| `.br` | brotli -d 验证；quality 映射压缩率对比 |
| `.cab` | Windows expand 命令验证 |
| `.arj` / `.lzh` / `.chm` / `.cpio` / `.deb` / `.rpm` / `.wim` / `.xar` / `.lzma` / `.msi` | 7-Zip（宿主 7z.dll）能打开的样例文件逐一浏览/提取；加密/固实包密码流程回归 |

---

## 未纳入范围

- **RAR 压缩** — 已有独立计划 `.sisyphus/plans/rar-compression.md`
- **libarchive 系格式写入（cpio/ar/xar/warc）** — 外部调研确认 LibArchive.Net 有增量但需引入第二份 native dll，与 7z.dll 重叠度高；若未来需要写入 cpio/xar 再评估
- **NSIS (.exe) / DMG (.dmg)** — 扩展名冲突/Windows 定位不符，仅保留魔数路径（可选），不进扩展名映射
- **WIM 写入 / 分卷 WIM** — 7z.dll 可读可写但 SharpSevenZip 压缩路径未验证，WIM 定位与压缩工具不符，只读即可
- **新格式拖拽出 Explorer** — `ArchiveEntryExtractor`（Core/Utils）目前仅支持 Zip/7z，新格式拖拽需另行扩展，不在本计划范围
