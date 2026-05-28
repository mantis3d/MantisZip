# 魔数检测文件真实格式 (Magic Byte Content Detection)

> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜] (0/6)

## TL;DR

在 Plan A（扩展名识别→预览）完成后，新增基于魔数/文件内容的格式检测引擎，替代当前的扩展名判断，使无名文件、扩展名错误或被改名的文件也能被正确识别和预览。

> **依赖**: Plan A 已完成（各格式解码器已就绪）
> **核心价值**: 扩展名不可靠时仍能识别格式，并在 PreviewHeader 中显示真实格式名

---

## 动机

### 当前问题

- 预览完全依赖 `Path.GetExtension(item.Name)` → 匹配 `ImageExtensions` / `TextExtensions` 等硬编码集合
- 无扩展名的文件、扩展名错误或被改名的文件 → 全部进入 "无法预览此文件"
- 用户看不到文件的真实格式，也无法获得任何元数据信息

### 目标

- 任何文件至少显示真实格式名称（如 "JPEG 图像" 而非 "无法预览"）
- 能展示元数据的格式展示元数据（Plan A 各解码器的输出）
- 和 Plan A 各格式解码器无缝衔接——检测出 `FileFormat` 后，调用 Plan A 对应解码器
- `FileFormatInfo` 数据模型由 Plan A 定义，Plan B 直接引用，不重复定义

---

## 架构

```
SelectionChanged
  ↓
ArchiveEntryExtractor.ExtractHeadAsync(100KB)   → byte[] head
  ↓  或 (针对 MP4 等格式)
ArchiveEntryExtractor.ExtractHeadTailAsync(100KB, 100KB)  → (byte[] head, byte[]? tail)
  ↓
FileFormatDetector.Detect(head, tail)            → FileFormat enum
  ↓  fallback
FileFormatDetector.DetectByExtension(ext)        → FileFormat enum
  ↓
FileFormatHelper.GetDisplayName(format)          → "JPEG 图像", "PE32+ 可执行文件"
  ↓
ShowPreviewHeader(format, displayName)           → 展示真实格式名
  ↓  若 Plan A 对应解码器存在
Plan A 解码器 (PeParser, TorrentParser, ...)     → FileFormatInfo ⚡
  ↓
PreviewInfoPanel 展示

> ⚡ `FileFormatInfo` 由 Plan A 在 `Core/Utils/FileFormatInfo.cs` 中定义。
> Plan B 不重新定义，直接引用。详见 [`preview-extended-formats.md` → 共享数据模型](preview-extended-formats.md)。
  ↓  若支持全量预览
全量预览 (ShowImagePreviewAsync / TextPreview / ...)
```

---

## 新增组件

### 1. `FileFormatDetector` (`Core/Utils/FileFormatDetector.cs`)

静态类，核心职责：输入 byte[] → 输出 `FileFormat`。

```csharp
public static class FileFormatDetector
{
    public static FileFormat Detect(byte[] head, int length, byte[]? tail = null);
    // 根据魔数匹配 → 返回 FileFormat
    // 无匹配 → FileFormat.Unknown

    public static FileFormat DetectByExtension(string extension);
    // 回退方案：已知扩展名 → FileFormat
}
```

### 2. `FileFormat` 枚举 (`Core/Utils/FileFormat.cs`)

```csharp
public enum FileFormat
{
    Unknown,

    // 图像（已有 Plan A WIC 支持）
    Png, Jpeg, Gif, Bmp, Ico, Webp, Tga, Hdr, Exr,

    // 可执行文件
    PeExe, PeDll, PeSys, MsiInstaller,

    // 文档
    Pdf, Docx, Xlsx, Pptx, Odt, Ods, Odp, Epub, Rtf, DjVu, Xps,

    // 音频
    Mp3, Flac, Wav, OggVorbis, M4a, Wma, Ape, Opus,

    // 视频
    Mp4, Avi, Mkv, WebM, Flv, Wmv, Mov,

    // 压缩包
    Zip, SevenZip, Rar, Tar, GZip, BZip2, Xz, Zstd,

    // 字体
    TrueType, OpenType, Woff, Woff2,

    // 数据库
    Sqlite, AccessDb, Dbase, Parquet,

    // 证书/安全
    CertDer, CertPem, Pkcs12,

    // 其他
    Torrent, WindowsLnk, Iso9660,
    Stl, Svg, Dicom, Fits, Vhd, Vmdk, Vhdx,
    Icl, // Icon Library (PE 变体)
    Sqlite, // 同上
}
```

**只包含 Plan A 已支持的格式**，避免超前定义。当 Plan A 添加新格式时，同步在此添加对应枚举值。

### 3. `ExtractHeadAsync` / `ExtractHeadTailAsync` (`Core/Utils/ArchiveEntryExtractor.cs`)

新增方法：

```csharp
/// <summary>
/// 提取压缩包内条目的前 maxBytes 字节到内存（用于格式检测）。
/// </summary>
public static Task<byte[]> ExtractHeadAsync(
    string archivePath, string entryName, int maxBytes,
    ArchiveFormat format, string? password = null,
    CancellationToken ct = default);

/// <summary>
/// 提取头部 + 尾部各指定字节数（用于 MP4 等元数据在尾部的格式）。
/// tailBytes = null 时等同于 ExtractHeadAsync。
/// </summary>
public static Task<(byte[] head, byte[]? tail)> ExtractHeadTailAsync(
    string archivePath, string entryName, int headBytes, int? tailBytes,
    ArchiveFormat format, string? password = null,
    CancellationToken ct = default);
```

#### 各压缩格式的实现策略

| 压缩格式 | Head 实现 | Tail 实现 |
|---|---|---|
| ZIP (Deflate) | 解压前 min(headBytes, 实际大小) 字节到 MemoryStream | 需要解压完整流，截取最后 N 字节。**代价接近全量提取**，大文件谨慎使用 |
| ZIP (Store) | 直接读取对应偏移 | 直接读取尾部偏移 |
| 7z (非固态) | `entry.Extract(stream)` 然后截取前 N 字节 | 同上 |
| 7z (固态) | **跳过 head 检测**，直接基于扩展名判断 | 同上，固态 7z 不做尾读取 |
| RAR | 同 7z 非固态 | 同上 |
| Tar/Gz | TarInputStream 读取第一个条目前 N 字节 | 不支持（流式压缩） |

#### MP4/视频格式的 head + tail 策略

专门针对 MP4/MOV/M4A 的检测流程：

```
1. ExtractHeadTailAsync(archive, entry, 100KB, 100KB)
2. head[4..8] = "ftyp" → 判定为 MP4 系列
3. 在 tail 中搜索 "moov" box:
   a. 找到 → 解析 mvhd (时长) + tkhd (分辨率)
   b. 未找到 → 降级显示 "MP4 (无法获取元数据)"
```

### 4. `ShowPreviewAsync` 改造 (`MainWindow.Preview.cs`)

当前流程：
```
ext → ImageExtensions.Contains → ShowImagePreviewAsync
ext → TextExtensions.Contains → ShowTextPreview
ext → 其他 → ShowUnsupportedPreview (无法预览)
```

改造后流程：

```
ext → 先通过 DetectByExtension 得到 FileFormat
     ↓
  尝试 Detect(head) → 如果得到不同格式，以 Detect 为准（覆盖扩展名）
     ↓
  展示 PreviewHeader: "📄 filename.dat → JPEG 图像, 1920×1080"
     ↓
  FileFormat → 查找 Plan A 中是否注册了解码器
     ↓ 有解码器
  调用对应解码器 → 填充 PreviewInfoPanel
  如果需要全量预览 → 继续走 ShowImagePreviewAsync / ShowTextPreview 等
     ↓ 无解码器
  仅展示 "JPEG 图像" / "PE32+ 可执行文件" 等信息，无全量预览
```

### 5. Post-Plan-A 新增格式注册

在 Plan A 中，每个格式解码器需要在统一的注册点声明自己支持的 `FileFormat` 和对应的解码逻辑。

```csharp
// 方案一：ShowPreviewAsync switch 分支（简单直接，Plan A 已有模式）
if (format == FileFormat.PeExe || format == FileFormat.PeDll)
    ShowPePreviewAsync(tempFile, item);

// 方案二（可选）：注册表模式
_previewHandlers[FileFormat.PeExe] = ShowPePreviewAsync;
_previewHandlers[FileFormat.Torrent] = ShowTorrentPreviewAsync;
```

**建议使用方案一**，与 Plan A 现有模式一致，改动最小。

---

## 方案选择：库 vs 手动魔数

`FileFormatDetector.Detect()` 的核心是魔数匹配，有两条实现路径，按「库优先 → 手动回退」的优先级策略协同工作。

### 方案 A：Mime-Detective 库（推荐主路径）

> **适用场景**：通用格式检测的第一选择。覆盖计划中 80%+ 的格式，代码量减少 ~80%。

[Mime-Detective](https://www.nuget.org/packages/Mime-Detective)（v25.8.1，1300万+ 下载量）是 .NET 生态最成熟的魔数检测库，支持 `byte[]` / `ReadOnlySpan<byte>` / `Stream` 输入。

```xml
<!-- 安装主包（含 Default 定义，MIT 许可证，约 50+ 常见签名） -->
<PackageReference Include="Mime-Detective" Version="25.8.1" />

<!-- 可选：Condensed 定义包（100+ 常见签名，含更多视频/音频格式） -->
<!-- 注：Condensed/Exhaustive 源自 TrID 签名数据库，有许可证注意事项 -->
<!-- <PackageReference Include="Mime-Detective.Definitions.Condensed" Version="25.8.1" /> -->
```

```csharp
// 使用示例
var inspector = new ContentInspectorBuilder
{
    Definitions = MimeDetective.Definitions.DefaultDefinitions.All()
}.Build();

var results = inspector.Inspect(headBytes);
// results 按匹配度排序，含 MIME type、扩展名、格式名称
// 取第一个（最高匹配度）映射到 FileFormat 枚举
```

**覆盖情况对比**：

| 格式分类 | Mime-Detective Default | Condensed | 计划中需要 |
|---------|:---:|:---:|:---:|
| 常见图像 (PNG/JPEG/GIF/BMP/ICO/WEBP) | ✅ | ✅ | ✅ |
| 文档 (PDF/DOCX/XLSX/PPTX/RTF) | ✅ | ✅ | ✅ |
| 压缩包 (ZIP/7z/RAR/GZip/BZip2/XZ) | ✅ | ✅ | ✅ |
| 音频 (MP3/FLAC/WAV/OGG) | ✅ | ✅ | ✅ |
| 视频 (MP4/FLV) | ✅ | ✅ | ✅ |
| 视频 (AVI/MKV/MOV) | ❌ | ✅ | ✅ |
| 可执行文件 (EXE/DLL) | ✅ | ✅ | ✅ |
| TTF / OTF | ❌ | ❌ | ✅ |
| WOFF / WOFF2 | ❌ | ❌ | ✅ |
| SQLite | ❌ | ❌ | ✅ |
| EXR / HDR / STL / LNK | ❌ | ❌ | ✅ |
| Torrent | ❌ | ❌ | ✅ |

**仍需手动实现的部分**（Mime-Detective 返回 `Unknown` 时回退到方案 B）：

1. **ZIP 子类型（EPUB/DOCX/XLSX/PPTX/ODF）** — 需部分解压 + 内容扫描
2. **PE 子类型（EXE/DLL/SYS）** — 需解析 PE header 的 `Characteristics`
3. **Torrent** — Bencode 字典，无统一魔数
4. **WOFF/WOFF2/TTF/OTF** — 若 Default 包未覆盖
5. **EXR/HDR/STL/LNK/SQLite** — 小众格式
6. **Zstd** — Default 包可能不含

**集成方式**：`FileFormatDetector.Detect()` 内部按优先级调用：
```
优先: Mime-Detective 检测 → 成功 → 返回 FileFormat
回退: 方案 B 手动魔数匹配表 → 成功 → 返回 FileFormat
兜底: DetectByExtension(ext)
```

### 方案 B：手动魔数匹配（备选/补充）

> **适用场景**：Mime-Detective 未覆盖的小众格式、ZIP/PE 子类型检测、许可证敏感环境。

即下文 `## 魔数匹配表` 详述的 30+ 条手动魔数 + PE/ZIP 子类型检测逻辑。零第三方依赖。

**优先级规则**：方案 A 优先（库检测更全、更准），方案 A 返回 `Unknown` 时回退到方案 B 的手动表。

### 相关计划引用

> `CompressionEstimator`（压缩预估）的自适应压缩级别功能也依赖魔数检测判别文件类型。
> 大文件（>64KB）的场景同样优先使用本方案的 Mime-Detective 库，
> 检测结果映射到 `CompressionCoefficients` 分类（`image_lossy` / `media` / `archive`），
> 从而自动降级压缩级别。
> 详见 [`compression-estimator.md` → 自适应压缩级别](compression-estimator.md)。

---

## 魔数匹配表

### 匹配优先级

魔数匹配按**特异性从高到低**顺序检测，防止误匹配（如 `MZ` → PE 的检测需额外判断 PE signature）：

| 优先级 | 魔数 | 长度 | FileFormat |
|---|---|---|---|
| 1 | `89 50 4E 47 0D 0A 1A 0A` | 8 | PNG |
| 2 | `FF D8 FF` | 3 | JPEG |
| 3 | `47 49 46 38 37 61` / `47 49 46 38 39 61` | 6 | GIF |
| 4 | `42 4D` | 2 | BMP |
| 5 | `00 00 01 00` | 4 | ICO |
| 6 | `52 49 46 46 xx xx xx xx 57 45 42 50` | 12 | WEBP |
| 7 | `76 2F 31 01` | 4 | EXR |
| 8 | `23 3F 52 41 44 49 41 4E 43 45` | 10 | HDR |
| 9 | `25 50 44 46 2D` | 5 | PDF |
| 10 | `50 4B 03 04` → ZIP 内部分类 | 4 | ZIP 子类 |
| 11 | `37 7A BC AF 27 1C` | 6 | 7z |
| 12 | `52 61 72 21 1A 07 00` / `52 61 72 21 1A 07 01 00` | 7/8 | RAR / RAR5 |
| 13 | `1F 8B` | 2 | GZip |
| 14 | `42 5A 68` | 3 | BZip2 |
| 15 | `FD 37 7A 58 5A 00` | 6 | XZ |
| 16 | `28 B5 2F FD` | 4 | Zstd |
| 17 | `49 44 33` | 3 | MP3 (ID3v2) |
| 18 | `66 4C 61 43` | 4 | FLAC |
| 19 | `52 49 46 46 xx xx xx xx 57 41 56 45` | 12 | WAV |
| 20 | `4F 67 67 53` | 4 | OGG |
| 21 | `66 74 79 70` (ftyp) | 4 | MP4/M4A |
| 22 | `1A 45 DF A3` | 4 | MKV/WebM |
| 23 | `46 4C 56 01` | 4 | FLV |
| 24 | `30 26 B2 75 8E 66 CF 11` | 8 | WMA/WMV |
| 25 | `00 01 00 00 00` | 4 | TTF |
| 26 | `4F 54 54 4F` | 4 | OTF |
| 27 | `77 4F 46 46` | 4 | WOFF |
| 28 | `77 4F 46 32` | 4 | WOFF2 |
| 29 | `4D 5A` + PE signature 验证 | 2+ | PE (EXE/DLL) |
| 30 | `53 51 4C 69 74 65` (SQLite) | 6 | SQLite |
| 31 | `4C 00 00 00 01 14 02 00` | 8 | LNK |
| 32 | `D0 CF 11 E0 A1 B1 1A E1` (OLE2) | 8 | MSI/MDB(X) |
| 33 | `73 6F 6C 69 64` (solid) | 5 | STL (ASCII) |
| 34 | `64` (Bencode 字典) | 1 | Torrent (需验证) |

### ZIP 内部分类检测

ZIP (`PK\x03\x04`) 太通用→需进一步判断子类型：

```csharp
static FileFormat DetectZipSubtype(byte[] head)
{
    // 在 head 中定位关键文件:
    //   "mimetype" 含 "application/epub+zip" → Epub
    //   "[Content_Types].xml" 含 "word/document.xml" → Docx
    //   "[Content_Types].xml" 含 "xl/workbook.xml" → Xlsx
    //   "[Content_Types].xml" 含 "ppt/presentation.xml" → Pptx
    //   "meta.xml" 含 "office:document-meta" → ODF
    // 以上都不是 → Zip (普通压缩包)
}
```

**注意**: 需要部分解压 Deflate 数据。100KB 内通常包含前几个 local file header + 部分压缩数据，对于 Store 的文件可直接读，Deflate 的需部分解压后检查。

### PE 格式检测

```csharp
static bool IsPe(byte[] head)
{
    // offset 0: "MZ"
    if (head[0] != 'M' || head[1] != 'Z') return false;
    // offset 0x3C: PE signature 偏移
    int peOffset = head[0x3C] | (head[0x3D] << 8);
    if (peOffset + 4 >= head.Length) return false;
    // PE signature: "PE\0\0"
    return head[peOffset] == 'P' && head[peOffset+1] == 'E'
        && head[peOffset+2] == 0 && head[peOffset+3] == 0;
}
```

---

## 磁数检测 + 尾读取（针对 MP4）

### 问题

MP4/MOV/M4A 的 moov box（含时长、分辨率、旋转信息）通常在文件尾部。前 100KB 只能拿到 ftyp box（文件类型）。

### 方案

`ExtractHeadTailAsync` 同时读取头部 100KB + 尾部 100KB：

```csharp
// MP4 检测流程
if (FileFormatDetector.Detect(head) == FileFormat.Mp4 && tail != null)
{
    // 在 tail 中寻找 moov box
    int moovStart = FindBox(tail, "moov");
    if (moovStart >= 0)
    {
        ParseMoovBox(tail, moovStart, out double duration, out int width, out int height);
    }
}
```

### 性能权衡

| 场景 | 全量提取 | 仅 head | head+tail |
|---|---|---|---|
| ZIP Store | 快 | 极快 | 略慢 (需两次 seek) |
| ZIP Deflate | 慢 | 快 (解压前 100KB) | **接近全量** (需解压到尾巴) |
| 7z 固态 | 很慢 | 同全量 | 同全量 |
| 7z 非固态 | 中等 | 快 | 略慢 |

**建议**: 仅在以下情况启用 tail 读取：
1. 格式为 **ZIP Store**（无需解压）
2. 格式为 **非固态 7z**（可快速定位）
3. 文件在压缩包中**未压缩** (Store / Stored)

其他情况跳过 tail 读取，降级为仅 head 检测。

---

## 任务总览

- [ ] **Step 1: FileFormat 枚举 + FileFormatDetector 核心**
- [ ] **Step 2: ExtractHeadAsync + ExtractHeadTailAsync**
- [ ] **Step 3: ShowPreviewAsync 改造**
- [ ] **Step 4: ZIP 子类型检测**
- [ ] **Step 5: MP4 tail 检测**
- [ ] **Step 6: 设置开关**

## 工作项

### Step 1: FileFormat 枚举 + FileFormatDetector 核心 [⬜⬜⬜] (0/3)

- [ ] `FileFormat` 枚举定义 + `FileFormatDetector.Detect()` 魔数匹配引擎
- [ ] `DetectByExtension()` 扩展名回退
- [ ] PE 特殊检测：MZ + PE signature 双重确认
- **文件**: `Core/Utils/FileFormat.cs`, `Core/Utils/FileFormatDetector.cs`
- 实现 `FileFormat` 枚举（仅 Plan A 已支持的格式）
- 实现 `Detect(byte[], int)` — 魔数匹配引擎，覆盖上表前 20+ 种格式
- 实现 `DetectByExtension(string)` — 扩展名回退
- PE 特殊检测：MZ + PE signature 双重确认

### Step 2: ExtractHeadAsync + ExtractHeadTailAsync [⬜⬜⬜⬜] (0/4)

- [ ] `ExtractHeadAsync` — ZIP (Deflate/Store), 7z, RAR 支持
- [ ] `ExtractHeadTailAsync` — 可选尾读取
- [ ] 7z 固态压缩降级策略
- [ ] headSize 通过 `AppSettings.PreviewHeadSize` 可配置

**文件**: `Core/Utils/ArchiveEntryExtractor.cs`

### Step 3: ShowPreviewAsync 改造 [⬜⬜⬜] (0/3)

- [ ] 在现有 `ext` 判定之前插入魔数检测
- [ ] 修改 PreviewHeader 展示真实格式
- [ ] `FileFormat` → 映射到 Plan A 各解码器

**文件**: `MainWindow.Preview.cs`

### Step 4: ZIP 子类型检测 [⬜⬜] (0/2)

- [ ] `DetectZipSubtype(byte[])` — EPUB/DOCX/XLSX/PPTX/ODF 区分
- [ ] 部分 Deflate 解压 + 文件内容匹配

### Step 5: MP4 tail 检测 [⬜⬜] (0/2)

- [ ] `ExtractHeadTailAsync` 的 MP4 调用策略
- [ ] moov box 解析：时长 (mvhd) + 分辨率 (tkhd)

### Step 6: 设置开关 [⬜⬜] (0/2)

- [ ] `AppSettings` 新增 `EnableFormatDetection` (默认 true)
- [ ] 关闭时回退到当前纯扩展名流程

---

## 取消与降级

| 场景 | 行为 |
|---|---|
| head 提取时文件被切换 | `OperationCanceledException` → 静默忽略（已有模式） |
| head 提取失败（格式不支持） | 降级为 `DetectByExtension` |
| 7z 固态压缩 | 跳过 head 提取，直接走扩展名 |
| MP4 tail 找不到 moov box | 降级显示 "MP4 文件" 无元数据 |
| `EnableFormatDetection = false` | 回退到当前纯扩展名流程（Plan A 逻辑） |
| 魔数匹配不到 | 回退扩展名；扩展名也未知 → `FileFormat.Unknown` → "无法识别格式" |

---

## 与两步式预览优化的关系

> 参见 [`preview-extended-formats.md` → Phase 5 — 元数据优先提取与两步式预览优化](preview-extended-formats.md)

### 依赖链

```
魔数检测 (Plan B)  →  ExtractHeadAsync / ExtractHeadTailAsync 基础设施
     ↓
格式解码器 (Phase 2/3/4)  →  每个格式的 GetXxxMetadata(byte[]) 方法
     ↓
两步式预览优化 (Phase 5)  →  用以上两者组合实现元数据优先显示
```

### 共享组件

| 组件 | 魔数检测用途 | 两步式预览用途 |
|------|-----------|--------------|
| `ExtractHeadAsync` | 取前 N 字节检测文件真实格式 | 取前 N 字节提取元数据填 InfoPanel |
| `ExtractHeadTailAsync` | 检测 MP4 的 ftyp + moov | 解析 MP4 时长/分辨率、PDF 尾部 xref |
| `FileFormatDetector.Detect()` | 判定 FileFormat 枚举 | 辅助判定调用哪个格式解码器 |
| 7z 固实降级策略 | 固实时跳过 head 检测 | 固实时跳过部分提取，fallback 全量 |

### 关键区别

魔数检测读 head 是**判定格式身份**（PE vs PDF vs ZIP），需要的数据量少（前几十字节通常就够了）。

两步式预览读 head 是**提取元数据**（分辨率、编码、页数等），需要的数据量更大（通常 4KB~100KB），且每个格式有专用的元数据解析方法。

两者共享 `ExtractHeadAsync` 的基础设施，但调用目的和 `maxBytes` 参数不同：

```csharp
// 魔数检测：仅需少量字节做格式识别
byte[] head = await ExtractHeadAsync(archivePath, entry, maxBytes: 4096);
var format = FileFormatDetector.Detect(head);
if (format == FileFormat.Unknown) format = DetectByExtension(ext);

// 两步式预览：需要更多字节做元数据解析
string tempHeadFile = await ExtractEntryHeadToFileAsync(
    archivePath, entry, outputPath, maxBytes: 100 * 1024);
var metadata = GetPdfMetadata(tempHeadFile);  // 或其它格式的元数据方法
```

### 实施建议

1. **魔数检测先落地** — 确保 `ExtractHeadAsync` 在所有压缩格式上工作正常
2. **格式解码器中分离元数据方法** — 在实现各格式时（Phase 2/3/4），有意将元数据解析写成独立的 `GetXxxMetadata(byte[])`，不要和内容加载混在一起
3. **Phase 5 统一编排** — 最后再用两阶段编排把魔数检测 + 元数据提取 + 内容加载串起来

---

## Definition of Done

- [ ] `FileFormatDetector` 魔数匹配引擎完成（覆盖 20+ 格式）
- [ ] `ExtractHeadAsync` / `ExtractHeadTailAsync` 实现
- [ ] `ShowPreviewAsync` 集成魔数检测
- [ ] ZIP 子类型检测（EPUB/DOCX/XLSX/PPTX/ODF）
- [ ] MP4 tail 检测（moov box 解析）
- [ ] `AppSettings.EnableFormatDetection` 开关
- [ ] 7z 固态压缩降级处理
- [ ] `dotnet build` 通过

### Final Checklist

- [ ] 无扩展名的文件可识别真实格式
- [ ] 扩展名错误的文件按真实格式展示
- [ ] PreviewHeader 显示真实格式名
- [ ] 回退到 Plan A 解码器正常
- [ ] 7z 固态压缩不执行 head 提取
- [ ] MP4 正确解析时长/分辨率
- [ ] `EnableFormatDetection = false` 时完全回退到扩展名流程
- [ ] 取消/切换文件时无异常
