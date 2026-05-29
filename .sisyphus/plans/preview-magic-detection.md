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

### 2. `FileFormat` 枚举（追加到现有 `Core/Utils/FileFormatInfo.cs`）

`FileFormat` 枚举**已存在**于 `Core/Utils/FileFormatInfo.cs` 末尾，使用**短名风格**。Plan B 不新建文件，只需追加当前缺失的值。

现有枚举值（代码中已有的，Plan B 直接引用，不重复定义）：

```
Unknown,
Jpeg, Png, Gif, Bmp, WebP, Ico, Tga, Hdr, Exr, Svg,      // 图像
Wav, Flac, Mp3,                                              // 音频
Mp4, Mkv, WebM, Wmv, Mov, Avi, Flv,                         // 视频
Pdf, Docx, Xlsx, Pptx, Epub, Mobi, Azw3,                     // 文档
Text, Html, Markdown,                                         // 文本/标记
Pe, Elf,                                                      // 可执行
Zip, SevenZip, Rar, Tar, Gz, Bz2, Xz, Zstd, Iso,             // 压缩包
Sqlite, Dbf,                                                  // 数据库
Stl, Dxf, Step, Fbx,                                          // 3D
Ttf, Otf, Woff,                                               // 字体
Torrent, Dicom, Cer, Pfx, Lnk, Vhd, Vmdk, Icl,               // 其他
Subtitle, OfficeOpenXml, OfficeLegacy,                        // 其他
Iso9660, Udf,                                                 // 映像
```

Plan B 需要追加的值（与现有命名风格一致，短名）：

```csharp
// 追加到现有 FileFormat 枚举末尾（文件 Core/Utils/FileFormatInfo.cs）

// 音频（补充）
Ogg,

// 文档（补充）
Odt, Ods, Odp, Rtf, DjVu, Xps,

// 字体（补充）
Woff2,

// 其他
Fits, Vhdx, Parquet,
```

**追加原则**：
- 使用短名（`Gz` 而非 `GZip`，`Bz2` 而非 `BZip2`，`Ttf` 而非 `TrueType`），与现有代码一致
- 不拆分子类型：PE 不分 `PeExe/PeDll/PeSys`（统一用 `Pe`，通过 `FileFormatInfo.Subsystem` 区分）
- 证书不拆分 `CertDer/CertPem/Pkcs12`（统一用 `Cer`/`Pfx`）
- 不超前定义 Plan A 未实现的格式（如 `MsiInstaller`、`AccessDb`、`Dbase`、`Parquet` 等待实际实现时再加）
- 当 Plan A 添加新格式时，同步追加对应枚举值

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

#### 实现方案：利用现有 ExtractEntryAsync 部分解压

`ExtractHeadAsync` 基于现有的 `ExtractEntryAsync` 实现，**不改动现有完整提取逻辑**：

```csharp
public static async Task<byte[]> ExtractHeadAsync(
    string archivePath, string entryName, int maxBytes,
    ArchiveFormat format, string? password = null,
    CancellationToken ct = default)
{
    // 方案 A（优先）：写 MemoryStream 提前截断
    // 利用 SharpCompress 的 entry.WriteTo(stream)，maxBytes 后断开
    // 适用于小型条目和 Store/非固态压缩
    //
    // 方案 B（兜底）：ExtractEntryAsync 到临时文件 → 读前 N 字节 → 删文件
    // 适用于 Deflate 大文件和固态 7z
}
```

| 压缩格式 | Head 实现方案 | Tail 实现 |
|---|---|---|
| ZIP (Deflate) | **方案 A**：`MemoryStream` + 写满 maxBytes 后 Dispose | 需要解压完整流，截取最后 N 字节。**代价接近全量提取**，大文件谨慎使用 |
| ZIP (Store) | **方案 A**：直接读对应偏移 | 直接读取尾部偏移 |
| 7z (非固态) | **方案 A**：`SharpSevenZipExtractor.ExtractFile(index, stream)` → 截取 | 同上 |
| 7z (固态) | **方案 B**：固态 7z 无法随机读 → `ExtractEntryAsync` 到 temp → 读前 N 字 → 删 temp | 固态 7z 不做尾读取 |
| RAR | **方案 A**：同 7z 非固态 | 同上 |
| Tar/Gz | **方案 B**：`TarInputStream` 读取第一个条目前 N 字节 | 不支持（流式压缩） |

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

当前流程（Plan A 已实现的 17 个 else-if 分支，全用扩展名判断）：
```
ext → ImageExtensions.Contains → ShowImagePreviewAsync
ext → PeExtensions.Contains → ShowPePreview
ext → TorrentExtensions.Contains → ShowTorrentPreview
... 共 17 个分支 ...
ext → 其他 → ShowUnsupportedPreview (无法预览)
```

改造后流程：

```
ext → 先通过 DetectByExtension 得到 FileFormat
     ↓
  ExtractHeadAsync(headBytes) → Detect(head) → 如果 Detect 返回的格式与扩展名不同，
  以 Detect 为准（覆盖扩展名）。⚠ 例外：Detect 返回 Zip 时表示子类型判定失败，
  仍沿用 DetectByExtension 的结果（.docx 保持 Docx）
     ↓
  展示 PreviewHeader: "📄 filename.dat → JPEG 图像, 1920×1080"
     ↓
  FileFormat → 映射到 Plan A 现有的 ShowXxxPreview 方法
     ↓ 格式有对应解码器
  调用对应解码器 → 填充 PreviewInfoPanel
  如果需要全量预览 → 继续走 ShowImagePreviewAsync / ShowTextPreview 等
     ↓ 格式无对应解码器
  仅展示格式名称（如 "Ogg Vorbis 音频"），无全量预览
```

**映射规则**：魔数检测出的 `FileFormat` → Plan A 现有的扩展名分支：

| 魔数检测结果 | 映射到 Plan A 分支 |
|---|---|
| `Jpeg`, `Png`, `Gif`, `Bmp`, `WebP`, `Ico` | `ImageExtensions.Contains(ext)` → `ShowImagePreviewAsync` |
| `Pe` | `PeExtensions.Contains(ext)` → `ShowPePreview` |
| `Pdf` | `PdfExtensions.Contains(ext)` → `ShowPdfPreview` |
| `Torrent` | `TorrentExtensions.Contains(ext)` → `ShowTorrentPreview` |
| `Docx`, `Xlsx`, `Pptx` | `OfficeExtensions.Contains(ext)` → `ShowOfficePreview` |
| 其余无对应解码器的格式 | 展示格式名 + 基本信息，全量预览区域留空 |

### 5. Plan A 现有分支的衔接

Plan A 已使用 17 个 `else if (XxxExtensions.Contains(ext))` 分支实现预览。
魔数检测改造**不改动现有分支结构**，只在分支之前插入魔数检测 + 修改 `PreviewHeader`：

```csharp
private async Task ShowPreviewAsync(ArchiveItem item)
{
    // ...
    var ext = Path.GetExtension(item.Name);

    // [新增] 魔数检测：覆盖扩展名判断
    string? realFormatName = null;
    byte[]? headBytes = null;
    if (AppSettings.Instance.EnableFormatDetection)
    {
        headBytes = await ArchiveEntryExtractor.ExtractHeadAsync(
            _currentArchivePath!, item.Name, 4096, _currentFormat, _currentPassword, ct);
        var fileFormat = FileFormatDetector.Detect(headBytes);
        if (fileFormat != FileFormat.Unknown)
        {
            realFormatName = FileFormatHelper.GetDisplayName(fileFormat);
            // 不阻断现有分支——现有 ShowPePreview 等仍然通过 ext 进入
            // 魔数检测只负责修改 PreviewHeader 显示
        }
    }

    // [修改] PreviewHeader：显示真实格式名（如果有）
    if (realFormatName != null)
        PreviewHeader.Text = $"📄 {item.Name} → {realFormatName}";
    else
        PreviewHeader.Text = $"📄 {item.Name}";

    // 以下是 Plan A 现有的 17 个 else-if 分支，不做改动
    if (ImageExtensions.Contains(ext)) { ... }
    else if (PeExtensions.Contains(ext)) { ... }
    // ...
}
```

这种改造方式的好处：**不改变现有预览逻辑**。魔数检测只用来改 `PreviewHeader`，
Plan A 的每个格式解码器仍然通过扩展名触发，互不干扰。

---

## 方案选择：手动魔数为主，Mime-Detective 可选

`FileFormatDetector.Detect()` 的核心是魔数匹配，以「手动优先 → 库可选增强」的优先级策略工作。

### 主路径：手动魔数匹配

即下文 `## 魔数匹配表` 详述的 30+ 条手动魔数 + PE/ZIP 子类型检测逻辑。`Detect()` 方法约 ~300 行代码，零第三方依赖，零许可证风险。

**优势**：完全可控，覆盖计划中所需格式的 90%+。ZIP 子类型检测（DOCX/EPUB 等）和 PE 检测必须手动实现。

### 可选增强：Mime-Detective 库

> **作为补充**：当手动魔数返回 `Unknown` 时，如果安装了 Mime-Detective 库，用它做二次尝试。

[Mime-Detective](https://www.nuget.org/packages/Mime-Detective)（v25.8.1）是 .NET 生态成熟的魔数检测库。**默认项目中不引用**，如需更广覆盖可手动添加：

```xml
<PackageReference Include="Mime-Detective" Version="25.8.1" />
<!-- Condensed/Exhaustive 源自 TrID 签名数据库，需注意许可证 -->
```

**Default 包的覆盖缺口**：
Manual 表已覆盖的头像 (PNG/JPEG/GIF) 、文档 (PDF) 、压缩包 (ZIP/7z/RAR) 、音频 (MP3/FLAC/WAV) 已被手动表覆盖。Default 包缺少的 MKV/MOV/TTF/OTF/WOFF/SQLite/EXR/HDR 等，手动表也覆盖了。因此 Mime-Detective 只能作为极少数边缘情况的兜底。

**集成方式**（条件编译或 try 反射）：
```csharp
// Detect() 内部优先级：
优先: 手动魔数匹配表 → 成功 → 返回 FileFormat
回退: Mime-Detective（如果 NuGet 包已安装）→ 成功 → 返回 FileFormat
兜底: DetectByExtension(ext)
```

### 相关计划引用

> `CompressionEstimator`（压缩预估）的自适应压缩级别功能也依赖魔数检测判别文件类型。
> 大文件（>64KB）的场景复用本方案的 `FileFormatDetector` 结果，
> 映射到 `CompressionCoefficients` 分类（`image_lossy` / `media` / `archive`），
> 从而自动降级压缩级别。
> 详见 [`compression-estimator.md` → 自适应压缩级别](compression-estimator.md)。

---

## 魔数匹配表

### 匹配优先级

魔数匹配按**特异性从高到低**顺序检测，防止误匹配（如 `MZ` → PE 的检测需额外判断 PE signature）：

| 优先级 | 魔数 | 长度 | FileFormat（枚举值） |
|---|---|---|---|
| 1 | `89 50 4E 47 0D 0A 1A 0A` | 8 | `Png` |
| 2 | `FF D8 FF` | 3 | `Jpeg` |
| 3 | `47 49 46 38 37 61` / `47 49 46 38 39 61` | 6 | `Gif` |
| 4 | `42 4D` | 2 | `Bmp` |
| 5 | `00 00 01 00` | 4 | `Ico` |
| 6 | `52 49 46 46 xx xx xx xx 57 45 42 50` | 12 | `WebP` |
| 7 | `76 2F 31 01` | 4 | `Exr` |
| 8 | `23 3F 52 41 44 49 41 4E 43 45` | 10 | `Hdr` |
| 9 | `25 50 44 46 2D` | 5 | `Pdf` |
| 10 | `50 4B 03 04` → 内联 DetectZipSubtype | 4 | `Zip` → 子类型 |
| 11 | `37 7A BC AF 27 1C` | 6 | `SevenZip` |
| 12 | `52 61 72 21 1A 07 00` / `52 61 72 21 1A 07 01 00` | 7/8 | `Rar` |
| 13 | `1F 8B` | 2 | `Gz` |
| 14 | `42 5A 68` | 3 | `Bz2` |
| 15 | `FD 37 7A 58 5A 00` | 6 | `Xz` |
| 16 | `28 B5 2F FD` | 4 | `Zstd` |
| 17 | `49 44 33` | 3 | `Mp3` (ID3v2) |
| 18 | `66 4C 61 43` | 4 | `Flac` |
| 19 | `52 49 46 46 xx xx xx xx 57 41 56 45` | 12 | `Wav` |
| 20 | `4F 67 67 53` | 4 | `Ogg` |
| 21 | `66 74 79 70` (ftyp) | 4 | `Mp4` |
| 22 | `1A 45 DF A3` | 4 | `Mkv` / `WebM` |
| 23 | `46 4C 56 01` | 4 | `Flv` |
| 24 | `30 26 B2 75 8E 66 CF 11` | 8 | `Wmv` |
| 25 | `00 01 00 00 00` | 4 | `Ttf` |
| 26 | `4F 54 54 4F` | 4 | `Otf` |
| 27 | `77 4F 46 46` | 4 | `Woff` |
| 28 | `77 4F 46 32` | 4 | `Woff2` |
| 29 | `4D 5A` + PE signature 验证 | 2+ | `Pe` |
| 30 | `53 51 4C 69 74 65` (SQLite) | 6 | Sqlite |
| 31 | `4C 00 00 00 01 14 02 00` | 8 | Lnk |
| 32 | `D0 CF 11 E0 A1 B1 1A E1` (OLE2) | 8 | Cer (OLE2 格式的证书/PFX) |
| 33 | `73 6F 6C 69 64` (solid) | 5 | Stl (ASCII) |
| 34 | `64` (Bencode 字典) | 1 | Torrent (需验证) |

### ZIP 内部分类检测（**内联到 Detect() 内，不对外暴露 Zip 中间态**）

**关键约束**：`.docx` / `.xlsx` / `.pptx` / `.epub` 全部以 `PK\x03\x04`（ZIP 魔数）开头。
如果 `Detect()` 返回 `Zip` 再由外界判断，"以 Detect 为准"的逻辑会把 Office/EPUB 降级成普通 ZIP，
Plan A 已实现的文档解码器就废了。

**因此 `Detect()` 内部必须完成 ZIP 子类型判定，只对外返回最终类型（`Docx` / `Epub` / `Zip`）：**

```csharp
static FileFormat Detect(byte[] head, int length, byte[]? tail = null)
{
    // 1. 先匹配非 ZIP 的魔数（PNG/JPEG/PDF/PE 等 30+ 条）
    //    ...
    
    // 2. 如果匹配到 PK\x03\x04（ZIP）→ 不立即返回 Zip，
    //    而是调用 DetectZipSubtype() 做子类型判定
    if (StartsWith(head, [0x50, 0x4B, 0x03, 0x04]))
        return DetectZipSubtype(head, length);
    
    // 3. 其他魔数直接返回
    //    ...
}

static FileFormat DetectZipSubtype(byte[] head, int length)
{
    // 扫描 head 中的 local file header:
    //   "mimetype" 内容 = "application/epub+zip" → Epub
    //   "[Content_Types].xml" → Docx / Xlsx / Pptx（根据 .xml 内容判断）
    //   "META-INF/manifest.xml" → Odf / Ods / Odp
    // 以上都不是 → Zip (普通压缩包)
}
```

**实现细节**：
- 100KB head 内通常包含前几个 local file header。对于 Store 的文件，文件名直接可读
- 对于 Deflate 的文件，需要部分解压后检查。利用 SharpCompress 的 `ZipArchive` / `ZipFile` 只读 entry 列表，不提取完整文件

### PE 格式检测（返回 `Pe`，不拆分子类型）

```csharp
// 魔数检测只返回 FileFormat.Pe，不区分 EXE/DLL/SYS
// 子类型由 Plan A 的 PeParser 通过 Subsystem 字段区分
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

- [ ] `FileFormat` 枚举追加（在已有 `FileFormatInfo.cs` 末尾追加缺失值）
- [ ] `FileFormatDetector.Detect()` 魔数匹配引擎 + `DetectByExtension()` 回退
- [ ] PE 特殊检测：MZ + PE signature 双重确认
- **文件**: `Core/Utils/FileFormatInfo.cs`（追加）, `Core/Utils/FileFormatDetector.cs`（新建）
- 在 `FileFormatInfo.cs` 末尾追加：`Ogg`, `Odt`, `Ods`, `Odp`, `Rtf`, `DjVu`, `Xps`, `Woff2`, `Fits`, `Vhdx`, `Parquet`
- 实现 `Detect(byte[], int)` — 魔数匹配引擎，覆盖 30+ 种格式
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
