# 预览格式识别与元数据展示 — ❌ 已废弃

> **此计划已被拆分为两份独立计划**：
>
> 1. **[preview-extended-formats.md](preview-extended-formats.md)** — 扩展预览格式支持
>    - 预览工具栏 → GIF 暂停、PNG 透明、缩放等
>    - 各格式解码器，通过扩展名识别
>    - 从 Phase 0 到 Phase 4 分批实施
>
> 2. **[preview-magic-detection.md](preview-magic-detection.md)** — 魔数检测文件真实格式
>    - FileFormatDetector + ExtractHeadAsync
>    - 替换扩展名识别为魔数检测
>    - 依赖 Plan A 的解码器成果
>
> 拆分原因：先通过扩展名支持更多格式预览（Plan A），再通过魔数替代扩展名（Plan B），避免阻塞依赖，实现增量交付。
>
> ---

## 概述

点击文件列表条目时，**从压缩包内解压前 100KB 到内存**，分析魔数判断文件的真实格式，获取该格式的元数据并展示在预览窗格中。如果格式支持全量预览（如图片、文本），再完全解压并显示。

### 当前问题

- 预览完全依赖文件扩展名（`.jpg` → 图片，`.txt` → 文本，其余 → "无法预览"）
- 扩展名可能不存在或错误（如 `.dat`, 无名文件, 改名文件）
- 即使扩展名能匹配，也"无信息可看"的格式直接拒之门外（如 `.exe`、`.pdf`、`.epub`）

### 目标

- 任何格式至少显示：**真实格式名 + 基本信息（尺寸、时长、条目数等）**
- 格式识别基于内容（魔数/magic bytes），而非扩展名
- 支持格式的元数据差异化展示

---

## 架构设计

### 新增组件

```
┌─────────────────────────────────────────────┐
│             预览流程（修改后）                   │
├─────────────────────────────────────────────┤
│  SelectionChanged                            │
│    ↓                                         │
│  ArchiveEntryExtractor.ExtractHeadAsync()     │
│    ↓ (byte[] head, 100KB)                    │
│  FileFormatDetector.Detect(head)              │
│    ↓ (FileFormatInfo)                        │
│  PreviewPane.ShowFormatInfo(info)             │
│    ↓ (若格式支持全量预览)                       │
│  ArchiveEntryExtractor.ExtractEntryAsync()    │
│    → ShowImagePreview / ShowTextPreview 等     │
└─────────────────────────────────────────────┘
```

### 1. `FileFormatDetector` (`Core/Utils/FileFormatDetector.cs`)

静态类，核心职责：输入 byte[] → 输出检测结果。

```csharp
public static class FileFormatDetector
{
    public static FileFormat Detect(byte[] header, int length);
    // 根据魔数匹配 → 返回 FileFormat 枚举
    // 无匹配 → FileFormat.Unknown

    public static FileFormat DetectByExtension(string extension);
    // 回退方案：已知扩展名 → FileFormat
    // 用于无法从魔数判断但扩展名可靠的格式（TAR 等）
}

public enum FileFormat
{
    Unknown,

    // 可执行文件 / 图标库
    PeExe, PeDll, PeSys,
    IconLibrary,  // ICL — PE 格式的图标库
    MsiInstaller,

    // 档案/压缩
    Zip, SevenZip, Rar, Tar, GZip, BZip2, Xz, Zstd,
    Cab, Wim, Iso9660,

    // 电子书
    Epub, Mobi, Azw3, FictionBook,
    Cbr, Cbz,

    // 办公文档
    Pdf, Docx, Xlsx, Pptx, Odt, Ods, Odp, Rtf,
    DjVu, Xps,

    // 音频
    Mp3, Flac, Wav, OggVorbis, M4a, Wma, Ape, Opus,

    // 视频
    Mp4, Avi, Mkv, WebM, Flv, Wmv, Mov,

    // 字体
    TrueType, OpenType, Woff, Woff2,

    // 3D / CAD
    Stl, Obj, Gltf, Glb, Fbx, Dxf, Step, Blender,

    // 数据库
    Sqlite, AccessDb, Dbase, Parquet,

    // 图像（冷门，WIC 不支持需 DIY/库解码）
    Tga, Hdr, Exr,

    // 矢量
    Svg, Emf, Wmf,

    // 证书/安全
    CertDer, CertPem, Pkcs12, PgpPublicKey,

    // 磁盘映像
    Vhd, Vmdk, Vhdx, Dmg,

    // 字幕
    Srt, Ass, WebVtt,

    // BT 种子
    Torrent,

    // 其他
    WindowsLnk, Dicom, Fits, Wad, McWorld,
}
```

### 2. `FileFormatInfo` (`Core/Utils/FileFormatInfo.cs`)

所有格式统一的信息结构。预览窗格根据不同字段存在与否动态渲染。

```csharp
public class FileFormatInfo
{
    public FileFormat Format { get; set; }
    public string DisplayName { get; set; }    // e.g. "JPEG 图像", "EPUB 电子书"
    public string Extension { get; set; }      // 原始扩展名（可能有）

    // 通用元数据
    public long? FileSize { get; set; }

    // 图像
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public int? BitDepth { get; set; }
    // 图像还可直接从现有 ShowImagePreviewAsync 拿

    // 音频/视频
    public TimeSpan? Duration { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public int? Bitrate { get; set; }
    public string? Codec { get; set; }
    public int? VideoWidth { get; set; }
    public int? VideoHeight { get; set; }

    // 文档/电子书
    public int? PageCount { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public string? Publisher { get; set; }
    public string? Isbn { get; set; }
    public DateTime? CreationDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    // 可执行文件 / DLL / 图标库
    public string? CompanyName { get; set; }
    public string? ProductName { get; set; }
    public string? FileVersion { get; set; }
    public string? ProductVersion { get; set; }
    public string? Architecture { get; set; }    // "x86", "x64", "ARM64"
    public string? Subsystem { get; set; }       // "GUI", "CUI", "EFI"
    // 图标：提取为 byte[]，后续可展示在 PreviewInfoPanel

    // ICL 图标库专用
    public int? IconCount { get; set; }         // 图标组数
    public string? IconSizes { get; set; }      // "16×16 ~ 256×256"
    public int? IconMaxBitDepth { get; set; }   // 最大色深 32/24/8

    // 图像冷门格式专用
    public string? Compression { get; set; }    // TGA "无压缩" / EXR "PIZ"
    public string? PixelFormat { get; set; }    // EXR "half float"

    // 压缩包
    public int? EntryCount { get; set; }
    public double? CompressionRatio { get; set; }  // 压缩比
    public long? UncompressedSize { get; set; }
    public bool? IsEncrypted { get; set; }
    public string? CompressionMethod { get; set; } // "Deflate", "LZMA", "Store"

    // 3D
    public int? VertexCount { get; set; }
    public int? FaceCount { get; set; }

    // 数据库
    public int? TableCount { get; set; }
    public string? TextEncoding { get; set; }

    // 字体
    public string? FontName { get; set; }
    public string? FontStyle { get; set; }
    public int? GlyphCount { get; set; }

    // 证书
    public string? Issuer { get; set; }
    public string? SubjectName { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }

    // 磁盘映像
    public string? VolumeLabel { get; set; }
    public long? DiskSize { get; set; }

    // Windows 快捷方式
    public string? LinkTarget { get; set; }

    // 字幕
    public int? SubtitleEntryCount { get; set; }

    // 通用
    public string? AdditionalInfo { get; set; }  // 兜底 — 格式专有信息

    // Torrent 种子文件
    public long? TorrentTotalSize { get; set; }   // 总数据量
    public long? PieceSize { get; set; }          // 分片大小
    public int? PieceCount { get; set; }          // 分片数
    public string? InfoHashV1 { get; set; }       // SHA1 (40 hex chars)
    public string? InfoHashV2 { get; set; }       // SHA2-256 (64 hex chars)
    public string? MagnetLink { get; set; }        // 可直接复制打开的 magnet 链接
    public string? TrackerUrl { get; set; }        // 主 tracker
    public int? TrackerCount { get; set; }         // 含备用 tracker 总数
    public bool? IsPrivate { get; set; }           // 私有种子
    public string? CreatedBy { get; set; }         // 客户端名
    public DateTime? TorrentCreationDate { get; set; }
    public string? Comment { get; set; }
    public int? FileCount { get; set; }            // info.files 内的文件数
    public bool? IsV2 { get; set; }                // v2 / hybrid 种子
}
```

### 3. 预览窗格的改动 (`MainWindow.xaml` / `MainWindow.xaml.cs`)

**PreviewInfoPanel 增强**：当前只对图片显示信息（名称、大小、压缩比、日期）。改为所有格式都显示信息，内容来自 `FileFormatInfo`。

在 `ShowPreviewAsync` 中增加流程：

```
1. ExtractHeadAsync(100KB) → byte[] head
2. FileFormatDetector.Detect(head) → FileFormat
3. 设 PreviewHeader = "📄 filename.dat → JPEG 图像, 1920×1080"
4. PreviewInfoPanel 展示 FileFormatInfo 的现有字段
5. 如果是全量预览支持格式（Image/Text/Html/Markdown）
   → 继续走原有完全解压 → 显示
6. 如果不是 → 只显示信息，不显示全量预览
```

### 4. `ArchiveEntryExtractor.ExtractHeadAsync` (`Core/Utils/ArchiveEntryExtractor.cs`)

新增方法：

```csharp
/// <summary>
/// 提取压缩包内条目的前 maxBytes 字节到内存（用于格式检测）。
/// </summary>
public static Task<byte[]> ExtractHeadAsync(
    string archivePath,
    string entryName,
    int maxBytes,
    ArchiveFormat format,
    string? password = null,
    CancellationToken ct = default);
```

实现：在现有 `ExtractZipEntry` 基础上，读 `min(maxBytes, entry.Size)` 字节到 `MemoryStream`，返回 `byte[]`。

7z 格式 (`SevenZipExtractor`) 的 `entry.Extract(stream)` 不支持部分提取，需完全提取后再截取前 N 字节——对于 7z 文件，需要权衡是否值得为此额外开销。

**注意**：当 7z 为固态压缩（solid）时，提取单个条目需要解压前面的所有数据。此时 100KB head 提取的代价接近完全提取。建议：固态 7z 跳过 head 检测，直接基于扩展名判断。

---

## 各格式检测详解

### 通用魔数匹配表（`FileFormatDetector.Detect`）

按魔数字节顺序排列：

| 魔数（十六进制） | FileFormat |
|---|---|---|
| `4D 5A` 后跟 `50 45 00 00` (PE\|0\0 at offset 0x3C 指向) | **EXE / DLL / ICL** (PE 格式) |
| `89 50 4E 47 0D 0A 1A 0A` | PNG（已有 ImagePreview） |
| `FF D8 FF` | JPEG（已有 ImagePreview） |
| `47 49 46 38 37 61` / `47 49 46 38 39 61` | GIF（已有 ImagePreview） |
| `42 4D` | BMP（已有） |
| `00 00 01 00` | ICO（已有） |
| `52 49 46 46 xx xx xx xx 57 45 42 50` | WEBP（已有） |
| `76 2F 31 01` | **EXR (OpenEXR)** — 需库解码 |
| `23 3F 52 41 44 49 41 4E 43 45` | **HDR (Radiance RGBE)** |
| — | **TGA** — 无固定多字节魔数；扩展名 `.tga` + 18 字节头部有效性检测 |
| | — TGA 头部 ID length(1) + ColorMap(1) + ImageType(1) + 宽高(4) + bpp(1) |
| `25 50 44 46 2D` | **PDF** |
| `50 4B 03 04` | **ZIP**（也可能 DOCX/XLSX/PPTX/EPUB/ODT/ODS/JAR）→ 需进一步检测 |
| `50 4B 05 06` | ZIP 空档（无中央目录指针） |
| `50 4B 07 08` | ZIP 分卷 |
| `37 7A BC AF 27 1C` | **7z** |
| `52 61 72 21 1A 07 00` | **RAR** |
| `52 61 72 21 1A 07 01 00` | RAR5 |
| `1F 8B` | **GZip** |
| `42 5A 68` | **BZip2** |
| `FD 37 7A 58 5A 00` | **XZ** |
| `28 B5 2F FD` | **Zstd** |
| `4D 53 43 46` | **CAB** |
| `4D 53 57 49 4D 00 00 00` | **WIM** |
| `43 44 30 30 31` (offset 0x8001) | **ISO 9660** |
| `49 44 33` | **MP3 (ID3v2)** |
| `66 4C 61 43` | **FLAC** |
| `52 49 46 46 xx xx xx xx 57 41 56 45` | **WAV** |
| `4F 67 67 53` | **OGG** |
| `66 74 79 70 4D 34 41` / `66 74 79 70 69 73 6F 6D` | **M4A / MP4 / MOV** |
| `30 26 B2 75 8E 66 CF 11` | **WMA / WMV** (ASF) |
| `4D 41 43 20` | **APE** |
| `4F 70 75 73 48 65 61 64` | **Opus** (在 Ogg 内) |
| `46 4F 52 4D xx xx xx xx 41 49 46 46` | **AIFF** |
| `1A 45 DF A3` | **MKV / WebM** |
| `46 4C 56 01` | **FLV** |
| `00 01 00 00 00` | **TrueType** (TTF) |
| `4F 54 54 4F` | **OpenType** (OTF) |
| `77 4F 46 46` | **WOFF** |
| `77 4F 46 32` | **WOFF2** |
| `73 6F 6C 69 64` | **STL (ASCII)** |
| `4F 62 6A 65 63 74` 等 (OBJ 是文本) | **OBJ** |
| `67 6C 54 46` (offset 4) | **GLB** |
| `4B 61 79 64 61 72 61` 等 (offset 0x15) | **FBX** |
| `42 4C 45 4E 44 45 52` | **Blender** |
| `53 51 4C 69 74 65 20 66 6F 72 6D 61 74 20 33 00` | **SQLite** |
| `D0 CF 11 E0 A1 B1 1A E1` | OLE2 (MDB/ACCDB/老 XLS/MSI) |
| | — offset 0xE0 的 CLSID 区分 |
| `04 22 4D 18` | **LZ4** |
| `23 21 41 4D 52 0A` | **AMR** |
| `49 57 41 44` / `50 57 41 44` | **WAD** |
| `63 6F 6E 65 63 74 69 78` | **VHD** |
| `76 68 64 78 66 69 6C 65` | **VHDX** |
| `23 20 44 69 73 6B 20 44 65 73 63` | **VMDK** |
| `4C 00 00 00 01 14 02 00` | **LNK** |
| `30 82` (ASN.1 SEQUENCE 标记 DER 证书) | **CER/DER/PFX** |
| `2D 2D 2D 2D 2D 42 45 47 49 4E` | **PEM 证书/密钥** |
| `53 49 4D 50 4C 45 20 20 20 20 20 20 20 20 3D 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 20 54` (FITS 格式) | **FITS** |
| `89 48 44 46 0D 0A 1A 0A` | **HDF5** |
| `64` (首字节 `d` = Bencode 字典起始) 且可解析为包含 `info` + `announce` 的字典 | **Torrent (BT 种子)** — Bencode 编码，非固定魔数 |

### ZIP 内部分类检测

ZIP 的魔数 `PK\x03\x04` 太通用，需要打开 ZIP 内部的特定文件进一步区分：

```csharp
static FileFormat DetectZipSubtype(byte[] head)
{
    // 从 ZIP 字节流中定位关键文件的原始存储偏移
    // 用 DeflateStream / 直接读取存储的文件内容判断

    // .../mimetype 含 "application/epub+zip" → Epub
    // [Content_Types].xml 含 "word/document.xml" → Docx
    // [Content_Types].xml 含 "xl/workbook.xml" → Xlsx
    // [Content_Types].xml 含 "ppt/presentation.xml" → Pptx
    // meta.xml 含 "office:document-meta" → Odf/Ods/Odp
    // 以上都不是 → Zip（普通压缩包）
}
```

> **关于 ZIP 中 100KB 读取的说明**：压缩包中的文件通过存储（Store/不压缩）时可以直接读取指定偏移；通过 Deflate 压缩时需要解压。前 100KB 的 ZIP 数据通常包含 local file header + 部分压缩数据，对于 Store 的文件可以直接提取，对于 Deflate 的文件需要解压部分数据。由于 Deflate 是流式可以"解压一点就中止"，100KB 的 Deflate 数据解压非常快。

> **简化方案**：如果 100KB 内没有所有需要判断的文件（例如 EPUB 的 OPF 可能在更深处完全解压后才暴露），可以考虑将 head 扩大到 512KB 或直接完全提取到 temp 的小型内存探索（对于 < 1MB 的 ZIP 完全提取也不贵）。

### PE 格式检测与元数据提取

PE 格式头部布局（前 100KB 内可解析）：

```
offset 0:     "MZ" (DOS header)
offset 0x3C:  指向 PE signature 的偏移 (DWORD)
PE offset:    "PE\0\0"
PE offset+4:  Machine (x86=0x14C, x64=0x8664, ARM64=0xAA64)
PE offset+20: Optional Header Magic (PE32=0x10B, PE32+=0x20B)
PE offset+68: Subsystem (GUI=2, CUI=3)
PE offset+92+? VS_VERSIONINFO 资源在 RT_RESOURCE 段中
```

VS_FIXEDFILEINFO 从 RT_VERSION 资源中读取，包含 `dwFileVersionMS/dwFileVersionLS`。

**图标**：通过 PE 资源目录提取 `RT_GROUP_ICON`（分组）和 `RT_ICON`（实际图像数据）。

---

### ICL 图标库 (Icon Library)

**ICL = PE DLL 的换皮**，只是资源段只包含图标资源且扩展名改为 `.icl`。

#### 检测方式

```csharp
// MZ 检测 → PE 检测通过
// 扩展名 ".icl" 或通过资源目录发现仅含图标资源
if (detectedFormat is PeExe or PeDll && extension == ".icl")
    → IconLibrary
```

老式 ICL（Windows 3.x Icon Manager）使用 NE (New Executable) 格式 (`MZ` + offset 0x80 的 `NE` 签名)，但数量极少，初期只需检测 PE 格式的 ICL。

#### 可提取的元数据

| 数据 | 来源 | 获取方式 |
|------|------|---------|
| 图标总数 | PE 资源段 `RT_GROUP_ICON` 的条目数 | 遍历资源目录树 |
| 可用分辨率 | 每个 `GRPICONDIRENTRY` 的 `bWidth` × `bHeight` | 读 `GRPICONDIR` 结构 |
| 色深 | `wBitCount` (4/8/24/32) | 同上 |
| 实际图标像素 | `RT_ICON` 资源 → `ICONIMAGE` 结构解码 | 提取 + 解码 AND/XOR mask |
| PE 版本信息 | `VS_VERSIONINFO` | 同 EXE 检测 |

#### 图标数据解码

ICO 图标像素布局（不同于 PNG/JPEG）：
```
ICONIMAGE:
  BITMAPINFOHEADER (40 bytes)
  AND mask (每像素 1 bit, 按行补齐到 DWORD)
  XOR mask (每像素 bpp bits, 按行补齐到 DWORD)
```

解码到 `BitmapSource` 约 80 行代码（含 mask 合并）。

#### ICL 预览展示方案

预览窗格分为两部分：
- **上半部分**：PE 版本信息（同 EXE）
- **下半部分**：图标缩略图网格（`WrapPanel` 或 `ItemsControl`），每行 N 个

```
┌─ PreviewHeader ──────────────────────────────┐
│ 📦 icons.icl → ICL 图标库, 42 个图标          │
├─ PreviewInfoPanel ────────────────────────────┤
│ 格式:  PE32 图标库              图标数: 42    │
│ 架构:  x64                      尺寸:  16~256 │
│ 公司:  IconDesigner Studio      色深:  32bpp  │
├─ 图标网格 ────────────────────────────────────┤
│ ┌────┐ ┌────┐ ┌────┐ ┌────┐ ┌────┐ ┌────┐  │
│ │ 🖼 │ │ 🖼 │ │ 🖼 │ │ 🖼 │ │ 🖼 │ │ 🖼 │  │
│ │ 48 │ │ 48 │ │ 48 │ │ 48 │ │ 48 │ │ 48 │  │
│ └────┘ └────┘ └────┘ └────┘ └────┘ └────┘  │
│ ┌────┐ ┌────┐                                │
│ │ 🖼 │ │ 🖼 │                                │
│ │ 32 │ │ 32 │                                │
│ └────┘ └────┘                                │
└──────────────────────────────────────────────┘
```

#### 实现方案

| 方案 | 代码量 | 依赖 | 推荐度 |
|------|-------|------|-------|
| **`Ico.Reader`** | ~50 行（集成 + 界面） | `Ico.Reader` NuGet | ⭐⭐⭐ 推荐，轻量 |
| DIY PE 资源遍历 | ~300 行（资源目录 + mask 解码） | 无 | ⭐⭐ 与 PE 检测共享 |
| `ExtractIconEx` (P/Invoke) | ~30 行 | `shell32.dll` | ⭐ 但 HICON 内存管理繁琐 |

**建议**：Phase 2 与 PE 版本信息一同实现，推荐 `Ico.Reader` 方案。

---

### 冷门图像格式: TGA, HDR, EXR

当前 `ImageExtensions` 列表：
```csharp
{ ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".webp" }
```

这些格式均通过 WPF 的 `BitmapDecoder` 解码，背后依赖 Windows Imaging Component (WIC)。WIC 不内置支持 TGA/HDR/EXR。

#### TGA (Truevision Targa)

| 项目 | 内容 |
|------|------|
| 魔数 | `\x00\x00\x02\x00\x00\x00\x00\x00\x00\x00\x00\x00`（前 12 字节，ID+ColorMap+ImageType 的组合，不是单一魔数） |
| 检测方式 | 更好的检测：基于扩展名 `.tga` + 跳过 18 字节后剩余的 XOR mask 合理性校验 |
| `ImageExtensions` | 添加 `.tga` |
| WIC 支持 | ❌ 不支持，`BitmapDecoder.Create` 会抛异常 |
| 解码方案 | **DIY 纯 C#** ~200 行 |

**TGA 头部结构 (18 bytes)：**
```
Offset  Size  Field
0       1     ID length
1       1     Color map type (0=无, 1=有)
2       1     Image type (2=无压缩RGB, 3=无压缩灰度, 10=RLE压缩RGB)
3       5     Color map spec (通常全零)
8       2     X origin
10      2     Y origin
12      2     Width
14      2     Height
16      1     Pixel depth (24/32)
17      1     Image descriptor (bits 3-0=alpha depth)
```

支持的图像类型优先级：Type 2（无压缩 RGB）→ Type 10（RLE RGB）→ Type 3（灰度）。

**解码 → WPF 显示：**
```csharp
public static BitmapSource DecodeTga(byte[] data)
{
    // 解析 18 字节头
    int width = data[12] | (data[13] << 8);
    int height = data[14] | (data[15] << 8);
    int bpp = data[16]; // 24 或 32

    // 像素数据从 offset 18 + ID length 开始
    int pixelOffset = 18 + data[0];
    // TGA 像素是 BGR(A) 顺序，WPF 的 Bgr32/Bgra32 格式直接匹配
    // 对 RLE 需要先解压

    return BitmapSource.Create(width, height, 96, 96,
        bpp == 32 ? PixelFormats.Bgra32 : PixelFormats.Bgr24,
        null, pixels, stride);
}
```

**在 `ShowImagePreviewAsync` 中的集成方式：**
```csharp
// 现有代码：BitmapDecoder.Create(filePath) 对标准格式生效
// 对 TGA：catch 中加后备
catch (Exception imgEx) when (ext == ".tga")
{
    var tgaBmp = TgaDecoder.DecodeFromFile(tempFile);
    if (tgaBmp != null) { /* 正常展示 */ }
}
```

这样改动最小，不影响现有 7 种标准格式。

#### HDR (Radiance RGBE)

| 项目 | 内容 |
|------|------|
| 魔数 | `#?RADIANCE` (offset 0) 或 `#?RGBE` |
| `ImageExtensions` | 添加 `.hdr`, `.rgbe`, `.pic` |
| WIC 支持 | ❌ 不支持 |
| 解码方案 | DIY ~350 行 或 ImageSharp |

**HDR 文件结构：**
```
#?RADIANCE        ← 签名（前 10 字节）
# 注释行...
FORMAT=32-bit_rle_rgbe  ← 格式标识
GAMMA=2.2
EXPOSURE=1.0

-Y 1080 +X 1920   ← 分辨率标记，后跟空行
[RGBE 像素数据]
```

**解码核心流程：**
1. 解析文本头 → 提取 `-Y N +X M` 得到分辨率
2. 跳过空行后的 RGBE 像素数据
3. 解码 RGBE 为 `float[]`（4 字节 RGBE → 3 浮点）
4. **Tone-map** 从 HDR 空间映射到 SDR 8-bit sRGB（关键步骤）

**Tone-mapping 方案（Reinhard 全局算子，约 30 行）：**
```csharp
// Reinhard 全局 tone-map
for (int i = 0; i < pixels.Length; i++)
{
    float luminance = 0.2126f * r[i] + 0.7152f * g[i] + 0.0722f * b[i];
    float lScale = luminance / (1.0f + luminance);  // Reinhard
    r[i] *= lScale / luminance;
    g[i] *= lScale / luminance;
    b[i] *= lScale / luminance;
    // clamp to [0, 1]
}
// 转 8-bit sRGB (gamma 2.2)
```

**展示方案：**
- 信息面板：分辨率、曝光值、软件名称
- 全量预览：tone-map 后的 SDR 图像（如用户需要 HDR 原始数据不在本工具范围内）
- 预览头标注 `🌞 HDR (tonemapped)` 提醒用户看到的是 tone-map 版本

#### EXR (OpenEXR)

| 项目 | 内容 |
|------|------|
| 魔数 | `\x76\x2F\x31\x01` (magic + version) |
| `ImageExtensions` | 添加 `.exr` |
| WIC 支持 | ❌ 不支持 |
| 解码方案 | **必须用库**（ImageSharp / Magick.NET），不可 DIY |

**EXR 头部 (前 40 字节)：**
```
Offset  Size  Field
0       2     Magic (0x0131 = 76 2F)
2       1     Version (0x01 = version 1)
3       1     Flags (tiled/multipart/deep)
4       24    Zero padding (reserved)
28      4     显示窗口 minX
32      4     显示窗口 minY
36      4     显示窗口 maxX
40      4     显示窗口 maxY
...
```

然后跟一系列的 **attributes**（名称+类型+值），包括：
- `compression` — ZIP/PIZ/RLE/B44/DWAA/DWAB
- `channels` — 通道列表（R/G/B/A 等）
- `displayWindow` — 图像尺寸
- `pixelType` — half/float
- `worldToCamera` / `worldToNDC` — 合成用的变换矩阵（VFX 用）

**为什么不建议 DIY：**
1. **多种压缩方式** — PIZ（wavelet）、B44（HDR 4x4 block）、DWAA/DWAB（差值）实现极其复杂
2. **深数据 (deep data)** — 每像素可以存储可变长度数据（用于程序化体积渲染）
3. **Tile vs Scanline** — 两种不同的数据组织方式
4. **多部分 (multipart)** — 一个文件可包含多个独立图像（如 AOVs）

**推荐方案：** 评估用户需求后再决定是否添加。如果使用频率高，引入 `Magick.NET`（Apache 2.0，开源友好）一次性解决 EXR 及 250+ 其他格式。

---

### BT 种子文件 (Torrent / .torrent)

.torrent 文件使用 **Bencode** 编码，本质是嵌套的字典。99% 的种子文件小于 100KB（典型值 20-60KB），所以 `ExtractHeadAsync` 的 100KB 限制会直接读取完整文件。

#### Bencode 编解码格式

```
Bencode 只有 4 种类型：
  字符串     <长度十进制>:<内容>           → "4:spam"
  整数       i<数字十进制>e                 → "i42e"
  列表       l<元素序列>e                   → "l4:spam4:eggse"
  字典       d<交替的键-值对>e              → "d3:cow3:moo4:spam4:eggse"
  键按字典序排列（字节比较）
```

纯 C# 解析器约 **80-120 行**（递归/迭代 JSON 式下降解析）。

#### .torrent 顶级字典字段

| 字段 | 类型 | 可选 | 说明 |
|------|------|------|------|
| `announce` | 字符串 | 必 | 主 Tracker URL |
| `info` | 字典 | 必 | 文件/分片信息（被哈希的部分） |
| `announce-list` | 列表 | 可 | 备用 Tracker 列表 |
| `creation date` | 整数 | 可 | Unix 时间戳 |
| `comment` | 字符串 | 可 | 种子描述 |
| `created by` | 字符串 | 可 | 创建客户端（如 "qBittorrent 4.3.9"） |
| `encoding` | 字符串 | 可 | 编码（通常 "UTF-8"） |
| `info` 字典 | — | — | 见下 |

#### `info` 字典关键字段

单文件模式：
| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | 字符串 | 建议的文件名 |
| `length` | 整数 | 文件大小（字节） |
| `piece length` | 整数 | 每片字节数（如 262144 = 256KB） |
| `pieces` | 字符串 | 二进制 SHA1 哈希串，长度 = n × 20 |
| `private` | 整数 | 1 = 私有种子（仅用 Tracker） |

多文件模式：
| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | 字符串 | 建议的根目录名 |
| `files` | 列表 | 文件信息数组：`{ length, path: ["dir", "file.txt"] }` |
| `piece length` | 整数 | 同上 |
| `pieces` | 字符串 | 同上 |

v2 (BEP-52) 新增：
| 字段 | 说明 |
|------|------|
| `meta version` | = 2 |
| `file tree` | 替代 `files` / `length`，递归字典树 |
| `piece layers` | Merkle 树哈希层 |
| Info hash 使用 **SHA2-256** 替代 SHA1 |

#### 可提取的元数据

| 数据 | 来源 | 示例 |
|------|------|------|
| **文件名 / 目录名** | `info.name` + `info.files[].path` | "ubuntu-22.04.iso" |
| **文件列表** | `info.files[].path` | 15 个文件 |
| **文件总数** | `info.files` 数组长度，或单文件算 1 | 15 |
| **总大小** | 各 `length` 求和 | 4.5 GB |
| **分片大小** | `info["piece length"]` | 256 KiB |
| **分片数** | `pieces` 长度 ÷ 20 | 17203 |
| **Info hash (v1)** | SHA1(`info` 字典的原始字节) | `A1B2C3D4...` |
| **Info hash (v2)** | SHA2-256(`info` 字典原始字节) | (32 字节) |
| **Magnet 链接** | 从 info hash + trackers 构造 | `magnet:?xt=urn:btih:A1B2...` |
| **Tracker URL** | `announce` + `announce-list` | `http://tracker.org:6969/announce` |
| **创建时间** | `creation date` | 2024-01-15 |
| **客户端** | `created by` | "qBittorrent 4.6.0" |
| **备注** | `comment` | "分享快乐" |
| **是否私有** | `info.private == 1` | 是/否 |
| **混合种子** | 同时存在 v1 和 v2 字段 | v1+v2 hybrid |

#### Info hash 计算（关键功能）

Info hash 是种子的全局唯一标识。计算公式：

```csharp
// 1. 读取 .torrent 文件全部字节
byte[] rawFile = File.ReadAllBytes(torrentPath);

// 2. Bencode 解析顶层字典 → 找到 "info" 键对应的值的起止偏移
//    注意：必须用原始字节偏移，不能 decode → re-encode（会改变内容）
int infoStart = FindInfoValueOffset(rawFile);  // "info" 值第一个字节的位置
int infoEnd = FindInfoValueEnd(rawFile);        // 对应 'e' 的位置 + 1

// 3. 截取 info 字典原始字节
byte[] infoBytes = rawFile[infoStart..infoEnd];

// 4. 计算哈希
byte[] infoHashV1 = System.Security.Cryptography.SHA1.HashData(infoBytes);
// v2: byte[] infoHashV2 = System.Security.Cryptography.SHA256.HashData(infoBytes);
```

#### Magnet 链接生成

```csharp
string ToHex(byte[] hash) => Convert.ToHexString(hash).ToLower();

// v1 magnet
$"magnet:?xt=urn:btih:{ToHex(infoHashV1)}&dn={UrlEncode(name)}&tr={UrlEncode(announce)}"

// v2 magnet
$"magnet:?xt=urn:btmh:1220{ToHex(infoHashV2)}&dn={UrlEncode(name)}&tr={UrlEncode(announce)}"
```

检测 `tr=` 参数建议包含前 3 个 tracker（防止单 tracker 失效）。

#### 预览展示方案

```
┌─ PreviewHeader ──────────────────────────────────────┐
│ 🌐 ubuntu-22.04.torrent → BitTorrent 种子 (v1)       │
├─ PreviewInfoPanel ────────────────────────────────────┤
│ 文件名:  ubuntu-22.04-desktop-amd64.iso               │
│ 大小:    4.5 GB (单文件)                              │
│ 分片:    256 KiB × 17203 片                          │
│ Tracker: tracker.ubuntu.com (主) + 2 备用              │
│ 创建:    2024-01-15  by qBittorrent 4.6.0             │
│ InfoHash: A1B2C3D4E5F6... (SHA1)                      │
│ 私有:    否                                            │
│ ┌──────────────────────────────────────────────────┐  │
│ │ 🔗 magnet:?xt=urn:btih:A1B2...&dn=ubuntu-22...   │  │
│ │    (点击复制 Magnet 链接)                         │  │
│ └──────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────┘
```

多文件种子展示文件列表（同目录树）：每个文件显示名称 + 大小。

#### 实现说明

| 项目 | 内容 |
|------|------|
| 检测方式 | 头部 `d` (0x64) 且尝试 Bencode 解析后包含 `info` + `announce` 键 |
| 解析依赖 | **纯 C#，零依赖** |
| 解析代码量 | ~100 行 Bencode 解析器 + ~80 行元数据提取 |
| 读取方式 | 完整文件读取（大多数 < 60KB，100KB 足够） |
| Info hash | 必须用原始字节偏移（不可 decode→re-encode） |
| 核心价值 | 不打开 BT 客户端就能知道种子内容、大小、tracker 和唯一标识 |
| 推荐阶段 | **Phase 1**（Bencode 解析简单 + 价值高） |

---

## 格式支持路线图 (Roadmap)

### Phase 1 — 核心框架 + 低 hanging fruit

目标文件：
- `Core/Utils/FileFormatDetector.cs` (魔数匹配引擎)
- `Core/Utils/FileFormatInfo.cs` (信息模型)
- `Core/Utils/ArchiveEntryExtractor.ExtractHeadAsync()` (部分提取)
- `UI/MainWindow.xaml.cs` — `ShowPreviewAsync` 修改

同时支持：
- 所有扩展名+魔数的基本匹配（至少显示格式名称）
- DOCX/XLSX/PPTX 元数据（ZIP 内读 XML）
- EPUB 元数据（ZIP 内读 OPF）
- ODT/ODS/ODP 元数据
- PDF 版本 + 页数检查
- 所有压缩格式的压缩比展示（已有数据）
- SQLite 头信息（版本、页大小、编码）
- GZ/BZ2/XZ/Zstd 基本信息（原始文件名、压缩方法）
- STL 三角面数
- ISO 卷标
- WAV/FLAC 采样率/时长/声道
- **TGA 图像** — DIY 解码器，零依赖，约 200 行
- **Torrent 种子文件** — Bencode 解析约 100 行，完整读取（通常 < 100KB）

### Phase 2 — 媒体格式 + 可执行文件

- MP3 ID3v2 → 标题/歌手/专辑/封面
- MP4/MOV/MKV → 分辨率 + 时长
- **PE (EXE/DLL/ICL)** → 版本信息 + 图标
- SVG → viewBox + 尺寸
- TTF/OTF → 字体名 + 字形数
- DBF → 记录数 + 字段数
- SRT/ASS/VTT → 条目数 + 时长
- **HDR 图像** — RGBE 解码 + Reinhard tone-map 转 SDR，约 350 行
- **ICL 图标库** — 复用 PE 检测 + Ico.Reader 或 DIY 资源遍历，预览图标网格

### Phase 3 — 高级格式

- MOBI/AZW3 元数据
- DICOM 患者信息 + 图像信息
- CHM 主题列表
- DXF/STEP/FBX → 实体数
- CER/PFX → 主题/有效期
- VHD/VMDK → 磁盘容量
- FITS → 图像维度
- **EXR 图像** — 需要 ImageSharp 或 Magick.NET；复杂度高不建议 DIY

### 关于第三方图像库的决策

TGA 可以用纯 C# DIY 解决。HDR 可以 DIY 但效果取决于 tone-map 质量。**EXR 必须引入库**。

| 库 | TGA | HDR | EXR | 体积 | 许可限制 |
|---|:---:|:---:|:---:|:----:|:--------:|
| DIY 纯 C# | ✅ 推荐 | ✅ 可行 | ❌ 太复杂 | 0 | 无 |
| ImageSharp 2.x | ✅ | ✅ | ✅ | ~1.5 MB | 商用需付费 |
| Magick.NET | ✅ | ✅ | ✅ | ~15 MB (含 native) | Apache 2.0 开源 |
| SkiaSharp | ❌ | ❌ | ❌ | ~3 MB | MIT |

**推荐策略**：TGA DIY → HDR DIY（后续可升级到 ImageSharp）→ EXR 视需求决定是否引入 Magick.NET。

---

## 界面设计

### PreviewHeader 格式

```
📄 filename.ext → JPEG 图像, 1920×1080
📄 setup.exe → PE32+ 可执行文件, v1.2.3.4, Contoso Inc.
📄 book.epub → EPUB 电子书, "三体", 刘慈欣, 中文
📄 archive.zip → ZIP 压缩包, 15 项, 压缩比 68%
📄 unknown.dat → 无法识别格式 (hex: 00 01 02 ...)
```

### PreviewInfoPanel 自适应渲染

信息面板自动根据 `FileFormatInfo` 的填充字段渲染字段-值列表。例如：

**PDF 文件：**
```
📄 格式:     PDF 1.7
📄 页数:     42
📄 标题:     年度报告 (如果 /Info 中有)
📄 作者:     张三
📄 大小:     2.3 MB
📄 加密:     否
```

**MP3 文件：**
```
🎵 格式:     MP3 (ID3v2.4)
🎵 标题:     夜曲
🎵 歌手:     周杰伦
🎵 专辑:     十一月的萧邦
🎵 时长:     3:42
🎵 比特率:   320 kbps
🎵 采样率:   44100 Hz
🎵 大小:     8.9 MB
```

**EXE 文件：**
```
⚙ 格式:     PE32+ 可执行文件
⚙ 架构:     x64
⚙ 子系:     GUI
⚙ 公司:     Microsoft Corporation
⚙ 产品:     Windows Explorer
⚙ 版本:     10.0.19041.1
⚙ 大小:     4.2 MB
```

**SQLite 文件：**
```
🗄 格式:     SQLite 3.x
🗄 页大小:   4096
🗄 编码:     UTF-8
🗄 表计数:   14
🗄 大小:     1.1 MB
```

**TGA 图像：**
```
🖼 格式:     TGA (Truevision Targa)
🖼 尺寸:     1920 × 1080
🖼 位深:     32 bpp (RGBA)
🖼 压缩:     无压缩 (Type 2)
🖼 大小:     5.2 MB
```

**HDR 图像：**
```
🌞 格式:     HDR (Radiance RGBE)
🌞 尺寸:     4096 × 2048
🌞 曝光:     1.0
🌞 软件:     Photoshop HDR Merge
🌞 大小:     16.8 MB
```

**EXR 图像：**
```
🎞 格式:     OpenEXR
🎞 尺寸:     2048 × 1024
🎞 通道:     R, G, B, A
🎞 压缩:     PIZ (wavelet)
🎞 像素:     half float
🎞 大小:     8.3 MB
```

**ICL 图标库：**
```
📦 格式:     PE32 图标库 (Icon Library)
📦 图标数:   42 个
📦 尺寸范围: 16×16 ~ 256×256
📦 色深:     32 bpp (带 Alpha)
📦 公司:     IconDesigner Studio  (从 VS_VERSIONINFO)
📦 大小:     3.1 MB
```
预览区域显示所有图标的缩略图网格（如 8 列 × N 行），每个图标展示其最大可用尺寸。

**BT 种子文件：**
```
🌐 ubuntu-22.04.torrent → BitTorrent 种子 (v1)
│ 文件名:  ubuntu-22.04-desktop-amd64.iso
│ 大小:    4.5 GB (单文件)
│ 分片:    256 KiB × 17203 片
│ 文件数:  1
│ Tracker: tracker.ubuntu.com (主) + 2 备用
│ 创建:    2024-01-15  by qBittorrent 4.6.0
│ InfoHash: A1B2C3D4E5F67890ABCDE... (SHA1)
│ 私有:    否
│ 类型:    v1
┌──────────────────────────────────────────────┐
│ 🔗 magnet:?xt=urn:btih:A1B2...&dn=ubuntu...  │
│    (点击复制 Magnet 链接)                     │
└──────────────────────────────────────────────┘
```

---

## 技术细节

### headSize 的选择

- 默认：**100 KB** (102400 bytes)
- 理由：覆盖大多数格式 header（PE header < 1KB, ID3v2 < 256MB 但前 100KB 能覆盖大部分 metadata frame, PDF cross-reference 通常在文件尾部但 Page count 信息在 body 中也可以在 100KB 内找到）
- 对于 ZIP 类格式：100KB 足够包含前几个 local file header + 部分压缩数据
- 可通过 `AppSettings` 配置（`PreviewHeadSize`）

### .NET 标准库能力（无需第三方库）

| 需要的操作 | .NET 类 |
|-----------|---------|
| 字节读取比较 | `BinaryReader`, `ReadOnlySpan<byte>` (推荐) |
| 魔数顺序匹配 | `Span.SequenceEqual()` |
| PE 结构解析 | `BinaryReader` + 结构体布局 |
| ASN.1 基础解析 | 自写（长度标记值） |
| ZIP 内特定文件 | `System.IO.Compression.ZipArchive` |
| XML 解析 | `XDocument` / `XmlReader` |
| UTF-16 读取 | `Encoding.Unicode` |
| SFV/CRC 校验 | 不涉及 |
| WAV/RIFF 解析 | `BinaryReader` 读 chunk |
| ID3v2 大小计算 | synchsafe integer: `(b0<<21)｜(b1<<14)｜(b2<<7)｜b3` |
| FLAC 元数据块 | 简单字节读取 |
| OGG 页面解析 | 页头偏移 + segment_table |
| MP4 box 解析 | 4字节 size + 4字节 type → 可跳过大 box |

### 性能考虑

- `ExtractHeadAsync` 优先从内存流读取，而非写入临时文件
- 格式检测在后台线程执行（当前预览流程已满足）
- 已有的 `_previewCts` (CancellationTokenSource) 保障快速切换文件时能取消进行中的检测
- 对于 7z 固态压缩：直接基于扩展名判断，不做 head 提取（或做了也等于全量）

### 取消与降级

- 当 head 提取完成后检测到文件被切换：OperationCanceledException → 静默忽略（已有模式）
- 当 head 提取遇到不可恢复错误：显示 "无法预览" + 错误信息（已有模式）
- 当 `ExtractHeadAsync` 在某格式上不支持（如 7z 需要全量解压才能获取 head）：降级为基于扩展名检测
- 当文件大小 < headSize：直接读取完整文件
- 当 `AppSettings.EnableFormatDetection = false`：回退到当前基于扩展名的预览流程

---

## 增量实施顺序（具体 Todo）

### Step 1: 核心魔数匹配框架
- 创建 `Core/Utils/FileFormatDetector.cs`
- 实现前十几种最常见格式的魔数匹配
- 创建 `FileFormatInfo` 模型
- 写单元测试用例

### Step 2: ExtractHeadAsync
- 在 `ArchiveEntryExtractor` 中添加 `ExtractHeadAsync` 方法
- 支持 ZIP（部分读取 SharpZipLib InputStream）
- 支持 7z（全量提取到 MemoryStream 后截取 headSize 字节）
- 返回 `byte[]` + 实际读取长度

### Step 3: GZ/BZ2/XZ/Zstd 检测
- 最简单的格式，只需几个字节判断
- 展示原始文件名、压缩方法等

### Step 4: 办公文档 & EPUB
- ZIP 内部扫描 `mimetype` 或 `[Content_Types].xml`
- 读取 OPF/core.xml/meta.xml
- 提取标题/作者/页数

### Step 5: PDF 基本检测
- 读前 100KB 定位 `/Type /Pages` 找 `/Count`
- `/Info` 中找到作者/标题元数据
- 检测 `/Encrypt` 标记

### Step 6: PE (EXE/DLL) 版本信息
- 解析 DOS header → PE offset → COFF header → Optional header
- 资源目录遍历找 `RT_VERSION`
- 解析 `VS_FIXEDFILEINFO`
- 显示产品名/公司/版本/架构

### Step 7: WAV/FLAC 检测
- 解析 RIFF/FMT chunk 获取采样率、位深、声道
- FLAC STREAMINFO 块解析

### Step 8: MP3 ID3v2 检测
- 解析 ID3v2 header → 帧列表
- 提取 TIT2（标题）/TPE1（歌手）/TALB（专辑）/TLEN（时长）
- 检测 APIC（封面图片）— 可选

### Step 9: 数据库 & 3D
- SQLite header 解析
- STL 三角面数
- DBF 记录数

### Step 10: 界面集成
- 修改 `ShowPreviewAsync` 集成头提取 + 格式检测
- PreviewInfoPanel 自适应渲染 FileFormatInfo
- PreviewHeader 展示格式检测结果
- 设置开关 (`EnableFormatDetection`)
