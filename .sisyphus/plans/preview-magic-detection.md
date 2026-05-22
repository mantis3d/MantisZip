# 魔数检测文件真实格式 (Magic Byte Content Detection)

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

## 工作项

### Step 1: FileFormat 枚举 + FileFormatDetector 核心
- **文件**: `Core/Utils/FileFormat.cs`, `Core/Utils/FileFormatDetector.cs`
- 实现 `FileFormat` 枚举（仅 Plan A 已支持的格式）
- 实现 `Detect(byte[], int)` — 魔数匹配引擎，覆盖上表前 20+ 种格式
- 实现 `DetectByExtension(string)` — 扩展名回退
- PE 特殊检测：MZ + PE signature 双重确认

### Step 2: ExtractHeadAsync + ExtractHeadTailAsync
- **文件**: `Core/Utils/ArchiveEntryExtractor.cs`
- `ExtractHeadAsync` — ZIP (Deflate/Store), 7z, RAR 支持
- `ExtractHeadTailAsync` — 可选尾读取
- 7z 固态压缩降级策略
- headSize 通过 `AppSettings.PreviewHeadSize` 可配置

### Step 3: ShowPreviewAsync 改造
- **文件**: `MainWindow.Preview.cs`
- 在现有 `ext` 判定之前插入：`FileFormatDetector.Detect(head)` 或 `DetectByExtension(ext)`
- 修改 PreviewHeader 展示真实格式
- `FileFormat` → 映射到 Plan A 各解码器

### Step 4: ZIP 子类型检测
- `DetectZipSubtype(byte[])` — EPUB/DOCX/XLSX/PPTX/ODF 区分
- 部分 Deflate 解压 + 文件内容匹配

### Step 5: MP4 tail 检测
- `ExtractHeadTailAsync` 的 MP4 调用策略
- moov box 解析：时长 (mvhd) + 分辨率 (tkhd)

### Step 6: 设置开关
- `AppSettings` 新增 `EnableFormatDetection` (默认 true)
- 关闭时回退到当前纯扩展名流程

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
