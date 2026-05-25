# 扩展预览格式支持 (Extended Preview Format Support)

## TL;DR

在现有预览系统的基础上，新增预览工具栏 + 更多格式的支持。各格式通过扩展名识别，暂不涉及魔数检测。

> **依赖**: 无（独立于魔数检测计划）
> **并行执行**: 各 Phase 内部可并行（工具按钮 + 各格式解码器独立文件）

---

## Phase 0 — 预览工具栏

### 目标

在预览内容区域顶部添加可扩展的工具栏，根据当前预览的格式动态显示相关按钮。

### 界面布局

```
┌─────────────────────────────────────────────┐
│ PreviewHeader (filename.ext → 格式名)         │
├────────────────┬────────────────────────────┤
│ PreviewInfoPanel│ [▶❚❚] [🔍⋮] [Aa⋮] [⇅]  │ ← 工具栏
│ (格式信息)      ├────────────────────────────┤
│                │ 预览内容区域                 │
│                │ (image/text/html/etc)       │
└────────────────┴────────────────────────────┘
```

工具栏按钮始终可见的通用按钮 + 按格式动态显示的按钮。

### 通用按钮（所有格式共用）

| 按钮 | 功能 | 实现方式 |
|---|---|---|
| 缩放菜单 | 适应宽度 / 100% / 适应窗口 | 改 `PreviewImage.Stretch` / `MaxWidth` / `MaxHeight` |
| 字体大小 +/- | 文本/代码预览字号临时调整 | 改 `TextBlock.FontSize`，仅本次预览生效 |
| 切换视图 | HTML/Markdown 切换源码/渲染 | 两个容器 (`WebBrowser` vs `TextBlock`) 切换可见性 |
| 重置 | 恢复所有工具栏选项到默认值 | 重置上述状态 |

### 格式特定按钮

| 格式 | 按钮 | 实现 | 难度 |
|---|---|---|---|
| GIF | ▶ 播放 / ❚❚ 暂停 | `WpfAnimatedGif.AnimationBehavior.SetIsAnimating()` | 1 行代码 |
| PNG (透明图) | ☐ 透明背景棋盘格 | 图片容器 `Background` 切换 `null` ↔ `CheckerBrush`（`DrawingBrush` 棋盘格图案） | ~10 行 |
| 所有图片 | 缩放同上已涵盖 | — | — |

### 技术方案

```csharp
// 可扩展的工具栏项目描述
public class PreviewToolbarAction
{
    public string? IconGlyph { get; set; }    // Segoe MDL2 字符或 PathGeometry
    public string Tooltip { get; set; }
    public bool IsToggle { get; set; }        // true=开关按钮, false=普通按钮
    public bool? IsChecked { get; set; }      // 当前开关状态
    public Action<MainWindow> Execute { get; set; }
    public Func<MainWindow, bool>? CanExecute { get; set; }
}

// 各格式返回自己的工具栏配置
// 在 ShowPreviewAsync 的各分支中调用 SetToolbar(items)
```

### 工作项

#### 0.1 工具栏容器
- **文件**: `MainWindow.xaml` + `MainWindow.Preview.cs`
- 在预览内容区域顶部添加 `ToolBar` 或自定义工具栏（水平 `StackPanel`）
- 工具栏通过 `<ContentControl Content="{Binding ...}" />` 或代码动态填充
- **约束**: 不破坏现有 Grid 行列布局；与 PreviewInfoPanel 保持同级

#### 0.2 通用按钮实现
- 缩放菜单（`ComboBox` 或下拉按钮）: 适应宽度 / 100% / 适应窗口
- 字号 +/- 按钮（仅文本/代码预览时显示）
- 源码/渲染切换（仅 HTML/Markdown 时显示）
- 重置按钮

#### 0.3 GIF 播放/暂停
- 检测到 `.gif` 时显示 ▶/❚❚ 按钮
- 读取 `WpfAnimatedGif.AnimationBehavior.GetIsAnimating()` 切换状态
- **状态保持问题**: 切换文件后默认恢复为播放。如需跨文件保持暂停状态，可在 `PreviewToolbarAction` 中存 `isChecked`

#### 0.4 PNG 透明背景棋盘格
- 创建棋盘格 `DrawingBrush` 资源（浅灰/白交替 8×8 方格）
- 切换按钮: 点击后 `PreviewImage.Parent` 的 `Background` 在 `null` 与棋盘格之间切换
- **注意**: 切文件后重置为透明，不保持状态

---

## Phase 1 — 预览信息面板增强

### 目标

当前 `PreviewInfoPanel` 只对图片展示信息。改为所有格式都展示信息，信息内容根据格式动态渲染。

### 工作项

#### 1.1 创建格式信息展示容器
- **文件**: `MainWindow.xaml` — `PreviewInfoPanel` 区域
- 将当前硬编码的图片信息布局改为动态 `ItemsControl` 或 `StackPanel` + 代码填充
- 每项 = 标签 + 值，自动隐藏值为 null/空的条目

#### 1.2 基本信息展示
- 所有格式通用: 文件大小、压缩前/后、压缩比、修改日期
- 这些数据 `SetPreviewInfo` 已有，改为通过新容器展示

#### 1.3 预留格式特定信息插槽
- 属性面板预留位置，后续 Phase 各格式填入自己的解析结果
- 数据结构: `Dictionary<string, string>` 或 `List<(string label, string value)>`

---

## 共享数据模型 — FileFormatInfo

所有格式解码器的输出统一使用 `FileFormatInfo` 类，信息面板根据其填充字段动态渲染。

### 定义位置

`Core/Utils/FileFormatInfo.cs`（新建）

### 类定义

```csharp
public class FileFormatInfo
{
    public FileFormat Format { get; set; }
    public string DisplayName { get; set; }    // e.g. "JPEG 图像", "BitTorrent 种子"
    public string Extension { get; set; }      // 原始扩展名

    // ── 通用 ──
    public long? FileSize { get; set; }

    // ── 图像 ──
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public int? BitDepth { get; set; }
    public string? PixelFormat { get; set; }   // "Bgra32", "Rgba32"
    public string? Compression { get; set; }   // TGA "无压缩/RLE" / EXR "PIZ/B44"

    // ── 音频/视频 ──
    public TimeSpan? Duration { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public int? Bitrate { get; set; }
    public string? Codec { get; set; }
    public int? VideoWidth { get; set; }
    public int? VideoHeight { get; set; }

    // ── 文档/电子书 ──
    public int? PageCount { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public DateTime? CreationDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    // ── 可执行文件 ──
    public string? CompanyName { get; set; }
    public string? ProductName { get; set; }
    public string? FileVersion { get; set; }
    public string? ProductVersion { get; set; }
    public string? Architecture { get; set; }    // "x86", "x64", "ARM64"
    public string? Subsystem { get; set; }       // "GUI", "CUI", "DLL"

    // ── ICL 图标库 ──
    public int? IconCount { get; set; }
    public string? IconSizes { get; set; }       // "16×16 ~ 256×256"

    // ── 压缩包 ──
    public int? EntryCount { get; set; }
    public double? CompressionRatio { get; set; }
    public long? UncompressedSize { get; set; }
    public bool? IsEncrypted { get; set; }
    public string? CompressionMethod { get; set; }

    // ── 3D ──
    public int? VertexCount { get; set; }
    public int? FaceCount { get; set; }

    // ── 数据库 ──
    public int? TableCount { get; set; }
    public string? TextEncoding { get; set; }

    // ── 字体 ──
    public string? FontName { get; set; }
    public string? FontStyle { get; set; }
    public int? GlyphCount { get; set; }

    // ── 证书 ──
    public string? Issuer { get; set; }
    public string? SubjectName { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }

    // ── BT 种子 ──
    public string? TorrentFileName { get; set; }
    public long? TorrentTotalSize { get; set; }
    public long? PieceSize { get; set; }
    public int? PieceCount { get; set; }
    public string? InfoHashV1 { get; set; }
    public string? MagnetLink { get; set; }
    public string? TrackerUrl { get; set; }
    public int? TrackerCount { get; set; }
    public bool? IsPrivate { get; set; }
    public string? CreatedBy { get; set; }
    public int? FileCount { get; set; }

    // ── 磁盘映像 ──
    public string? VolumeLabel { get; set; }
    public long? DiskSize { get; set; }

    // ── Windows 快捷方式 ──
    public string? LinkTarget { get; set; }

    // ── 字幕 ──
    public int? SubtitleEntryCount { get; set; }

    // ── 通用兜底 ──
    public string? AdditionalInfo { get; set; }
}
```

> **注意**: `FileFormatInfo` 作为共享数据模型被魔数检测计划 (Plan B) 直接使用。Plan B 不重新定义，只引用此文件。

---

## 内容区方案分类

每个格式实现时，需要同时决定**信息面板**和**内容区**的展示方案。根据格式特性分为三类：

### 第一类：内容 ≠ 信息，双区独立

格式本身有"可渲染的内容"（图片、文本、渲染结果），信息面板展示元数据，内容区展示内容本身。

| 格式 | 信息面板 | 内容区 |
|---|---|---|
| PNG/JPEG/GIF/BMP/WEBP/ICO | 分辨率、位深、压缩比 | ✅ 图片（已有） |
| TGA/HDR/EXR | 分辨率、位深、像素格式 | ✅ 解码后的图片 |
| SVG | viewBox、元素数 | ✅ 渲染为图片显示 |
| TTF/OTF/WOFF | 字体名、样式、字形数 | ✅ 字体样本 "AaBbCc..." |
| Text/Code | 编码、行数 | ✅ 文本内容（已有） |
| HTML/Markdown | — | ✅ 渲染页面或源码切换（已有） |

### 第二类：内容本身就是信息（全信息格式）

格式所有数据本质上是结构化信息，没有"画面/文本"可渲染。内容区不应空置显示"无法预览"，而应展示详细结构化视图。

| 格式 | 信息面板（摘要） | 内容区（详细视图） |
|---|---|---|
| **Torrent** | 总大小、分片、Hash | 📋 **文件列表** + 🔗 **Magnet 链接（可复制）** |
| **PE (EXE/DLL)** | 版本号、公司、架构 | 🖼 **图标(如有)** + 产品名大号展示 |
| **SQLite** | 版本、页大小、编码 | 📋 **表列表（表名 \| 行数 \| 列数）** |
| **DBF** | 记录数、字段数 | 📋 **字段定义列表（字段名 \| 类型 \| 长度）** |
| **ICO** | 图标数、尺寸列表 | 🖼 **图标列表（各尺寸缩略图排列）** |
| **ICL** | 图标数、尺寸范围、色深 | 🖼 **图标网格（来源于 PE 资源提取）** |
| **LNK** | 目标路径 | ℹ️ 链接详情（参数、快捷键、图标位置） |

### 第三类：纯信息展示（内容区空置或简单展示）

格式只有元数据，不足以填充内容区。内容区暂时空置或显示简单统计。

| 格式 | 信息面板 | 内容区 |
|:---|---|---|
| WAV/FLAC | 采样率、位深、声道、时长 | ⬜ 空置（或展示大号时长） |
| MP3 | 标题、歌手、专辑、封面 | ⬜ 空置（封面照片可展示在内容区） |
| STL | 三角面数 | ⬜ 空置 |
| ISO | 卷标、格式 | ⬜ 空置 |
| GZ/BZ2/XZ/Zstd | 原始文件名、压缩方法 | ⬜ 空置 |
| CER/PFX | 主题、颁发者、有效期 | ⬜ 空置 |
| VHD/VMDK | 磁盘容量、格式版本 | ⬜ 空置 |
| MP4/MKV | 分辨率、时长、编解码器 | ⬜ 空置 |
| 字幕 (SRT/ASS/VTT) | 条目数、时间范围 | ✅ 文本内容（复用 TextPreview） |
| DOCX/XLSX/PPTX | 标题、作者、页数 | ⬜ 空置 |
| EPUB | 标题、作者、语种 | ⬜ 空置 |
| ODT/ODS/ODP | 标题、作者、页数 | ⬜ 空置 |

---

## 信息面板 vs 内容区分配表（完整参考）

> 所有格式的完整分配方案。实现时按此表执行。如需调整直接改此表即可。

### 已有格式（无需改动）

| 格式 | 信息面板 | 内容区 |
|---|---|---|
| JPEG/PNG/GIF/BMP/WEBP | 分辨率、位深、压缩比、大小、日期 | 图片（已有） |
| ICO | 分辨率、位深 | 图片（已有） |
| Text/Code | 编码、行数、大小 | 文本内容（已有） |
| HTML/Markdown | （无需额外信息） | 渲染页面/源码切换（已有） |

### 新增格式

| 格式 | 信息面板 | 内容区 |
|---|---|---|
| **Torrent** | 总大小、分片数、Tracker 数、InfoHash、创建时间、创建者、是否私有 | 文件列表 + Magnet 链接（可复制） |
| **PE (EXE/DLL)** | 公司、产品名、版本号、架构(x86/x64)、子系统(GUI/CUI)、大小 | 产品名大号展示 + 图标(Phase 4) |
| **PDF** | PDF 版本、标题、作者、页数、是否加密、大小 | 🖼 **第一页渲染图（PdfPig + SkiaSharp）** + [📄 显示完整内容] 按钮切 WebView2 |
| **WAV** | 采样率、位深、声道数、时长、大小 | ▶ 音频播放（Phase 3 加） |
| **FLAC** | 采样率、位深、声道数、时长、大小 | ▶ 音频播放（Phase 3 加） |
| **MP3** | 标题、歌手、专辑、时长、比特率、采样率、大小 | 封面 + 标题 + ▶ 音频播放（Phase 3 加） |
| **SQLite** | 版本、页大小、编码、表数、大小 | 表列表（表名 \| 行数） |
| **DBF** | 记录数、字段数、大小 | 字段定义列表（名 \| 类型 \| 长度） |
| **ISO** | 卷标、格式类型、大小 | ⬜ 空置 |
| **STL** | 三角面数、格式(binary/ASCII)、大小 | ⬜ 空置 |
| **GZ/BZ2/XZ/Zstd** | 原始文件名、压缩方法、原大小、大小 | ⬜ 空置 |
| **LNK** | 目标路径、工作目录、大小 | 链接详情（快捷键、图标位置、参数） |
| **TGA** | 分辨率、位深、压缩类型、大小 | ✅ 解码后的图片 |
| **HDR** | 分辨率、曝光值、软件、大小 | ✅ tone-mapped 图片 |
| **SVG** | viewBox 宽高、大小 | ✅ 渲染为图片 |
| **TTF/OTF** | 字体名、样式、字形数、大小 | ✅ 字体样本 |
| **ICO (增强)** | 图标数、各尺寸/色深列表 | 🖼 图标缩略图排列 |
| **ICL** | 图标数、尺寸范围、色深、版本信息 | 🖼 图标网格 |
| **SRT/ASS/VTT** | 条目数、起始/结束时间、大小 | ✅ 文本内容 |
| **DOCX/XLSX/PPTX** | 标题、作者、页数/工作表数、大小 | ⬜ 空置 |
| **EPUB** | 标题、作者、语种、大小 | 🖼 **封面图**（从 OPF 提取）|
| **CER/PFX** | 主题、颁发者、有效期(start/end)、签名算法、大小 | ⬜ 空置 |
| **VHD/VMDK** | 磁盘容量、格式版本、大小 | ⬜ 空置 |
| **MP4/MOV** | 分辨率、时长、编解码器、大小 | ▶ 视频播放（Phase 3 加） |
| **WMV** | 分辨率、时长、编解码器、大小 | ▶ 视频播放（Phase 3 加） |
| **MKV/WebM** | 分辨率、时长、编解码器、大小 | ⬜ 空置（不支持播放） |
| **DICOM** | 患者姓名、出生日期、检查日期、模态、图像尺寸、大小 | ⬜ 空置 |
| **MOBI/AZW3** | 书名、作者、出版方、大小 | ⬜ 空置 |
| **DXF/STEP/FBX** | 实体数、面数、大小 | ⬜ 空置 |
| **EXR** | 分辨率、通道列表、压缩方式、像素类型、大小 | ✅ 解码后的图片 |

> **灵活调整**：表中信息面板和内容区的内容可以自由挪动。比如想把 MP3 封面图移到内容区居中展示，或者把 SQLite 表列表移到信息面板——改对应格式的 `ShowXxxPreviewAsync` 方法即可，不需要改框架代码。

---

## Phase 2 — 快速出货格式

按优先级从高到低实现格式解码器。每个格式新增到 `ShowPreviewAsync` 的扩展名分支中。

### 通用模式

```csharp
// 统一入口模式
if (SomeExtensions.Contains(ext))
{
    // 1. 提取到临时文件（已有 ExtractEntryAsync）
    // 2. 解析头部元数据（只读 tempFile 前 N 字节）
    // 3. 填充 PreviewInfoPanel
    // 4. 如果支持全量预览 → 展示内容
    // 5. 配置工具栏按钮
}
```

### Phase 2A — 信息类格式（无全量预览，只展示元数据信息）

#### 2A.1 Torrent 元数据
- **文件**: `Core/Utils/TorrentParser.cs` (新增)
- **扩展名**: `.torrent`
- **类别**: 第二类（全信息格式）
- **信息面板**: 内含文件名、总大小、分片数、Tracker URL、InfoHash、创建者、创建时间、是否私有
- **内容区**: 文件列表（名称 + 大小，可滚动）+ Magnet 链接（可复制按钮）
- **实现**: Bencode 解析器 ~100 行纯 C#；读完整文件（通常 < 60KB）
- **预估**: ~4h

#### 2A.2 PE 版本信息 (EXE/DLL)
- **文件**: `Core/Utils/PeParser.cs` (新增)
- **扩展名**: `.exe`, `.dll`, `.sys`, `.ocx`
- **类别**: 第二类（全信息格式）
- **信息面板**: 公司名、产品名、文件版本、产品版本、架构 (x86/x64/ARM64)、子系统 (GUI/CUI/DLL)
- **内容区**: 产品名大号突出展示 + 描述(如有)。图标提取放在 Phase 4，届时补上图标
- **实现**: BinaryReader 解析 DOS header → PE header → Optional Header → VS_VERSIONINFO
- **预估**: ~4h

#### 2A.3 PDF 元数据 + 第一页预览 + 完整内容切换
- **文件**: `Core/Utils/PdfParser.cs` + `MainWindow.Preview.cs` 修改
- **扩展名**: `.pdf`
- **类别**: 第一类（内容 ≠ 信息，有图片渲染 + WebView2 切换）
- **信息面板**: PDF 版本 (1.x)、标题、作者、页数、是否加密
- **内容区（轻量模式）**: 🖼 PDF 第一页渲染图（PdfPig + SkiaSharp）
- **内容区（完整模式）**: WebView2 加载完整 PDF
- **工具栏**: `[🔍 缩放] [📄 显示完整内容]` → 点击后切换 WebView2 → `[✕ 关闭完整内容]`

### 实现方案

#### 依赖
- **PdfPig** (`UglyToad.PdfPig`) — 纯 .NET PDF 解析库，读取交叉引用表按需加载页面
- **SkiaSharp** (`SkiaSharp`) — 纯 .NET 跨平台渲染库，把 PDF 页面画到 bitmap
- 两者均为纯托管依赖，无 native 层

#### 工作流

```
用户选择 .pdf 文件
  │
  ├── 文件 ≤ MaxPreviewFileSize
  │     ├── 提取完整文件到 temp（供 WebView2 使用）
  │     │     └── 工具栏显示 [📄 显示完整内容]
  │     ├── PdfPig 读取文件头 + 第一页（按 seek，不读全文）
  │     └── SkiaSharp 渲染第一页为 BitmapImage → PreviewImage
  │
  └── 文件 > MaxPreviewFileSize
        ├── 只提取第一页所需的数据（PdfPig 只 seek 不读全文）
        ├── SkiaSharp 渲染第一页
        └── 不显示 [📄 显示完整内容]（因未提取完整文件）
```

#### 关键技术点

1. **PdfPig 延迟加载** — `PdfDocument.Open(stream)` 默认只读交叉引用表，不加载全部对象。`document.GetPage(1)` 时只读第一页引用的对象（内容流、字体、图片）
2. **渲染** — SkiaSharp 把 `PdfPig` 的页面操作符 (`ShowText`, `PaintPath` 等) 转换成 `SKCanvas` 绘制命令。或者直接用 `PdfPig` 的 `page.GetLetters()` + `SvgLetter` 组合成图像
3. **完整文件提取** — 当 `item.Size ≤ MaxPreviewFileSize` 时，`ExtractPreviewFileAsync` 提取完整 PDF 到 temp。轻量模式不需要等它完成（用另一个 task 预提取）
4. **模式切换** — [📄 显示完整内容] 按钮调用 `EnsureWebView2InitializedAsync()` → `Navigate(filePath)`。切换回轻量模式时 `HideAllPreviewControls` + 显示 PreviewImage

```csharp
private async Task ShowPdfPreview(string filePath, ArchiveItem item)
{
    // 1. 元数据解析（已有 PdfParser.Parse）
    var info = PdfParser.Parse(filePath);
    
    // 2. 轻量模式：渲染第一页
    using var pdfDoc = UglyToad.PdfPig.PdfDocument.Open(filePath);
    var page = pdfDoc.GetPage(1);
    using var bitmap = RenderPageToBitmap(page, width: 1200);
    PreviewImage.Source = bitmap.ToBitmapImage();
    
    // 3. 如果 ≤ MaxPreviewFileSize，预提取完整文件供 WebView2 使用
    if (item.Size <= MaxPreviewFileSize)
    {
        _pdfFullPath = await ExtractPreviewFileAsync(item, "preview.pdf", ct);
        // 工具栏显示 [显示完整内容]
    }
    
    // 4. 显示信息面板 + 工具栏
    SetPreviewInfo(item, ...);
    SetToolbar(...); // 缩放 + [显示完整内容]
}

// 模式切换
private async Task ShowPdfFullContent()
{
    if (_pdfFullPath == null) return;
    await EnsureWebView2InitializedAsync();
    HideAllPreviewControls();
    PreviewWebView2.CoreWebView2.Navigate(new Uri(_pdfFullPath).AbsoluteUri);
    PreviewWebView2.Visibility = Visibility.Visible;
    SetToolbar(...); // [关闭完整内容]
}
```

#### 关于 WebView2 崩溃

当前已知：
- 点击 PDF 工具栏的 **MoreSettings**（齿轮按钮）→ 浏览器进程崩溃 → 已隐藏
- 大 PDF 或复杂 PDF **本身可能触发**浏览器进程崩溃 → 已添加 `ProcessFailed` 事件处理 + 自动恢复

轻量模式+PdfPig 方案彻底绕过了这个大 PDF 的问题：用户看到的永远是第一页预览图，只有主动点击"显示完整内容"才启动 WebView2。

#### 预估

| 工作项 | 预估 |
|--------|------|
| PdfPig + SkiaSharp 集成（NuGet + 渲染 helper） | ~2h |
| 第一页渲染实现（RenderPageToBitmap） | ~3h |
| 轻量/完整模式切换逻辑（ShowPdfPreview 改造） | ~2h |
| 工具栏按钮 + 状态管理 | ~1h |
| 大文件处理（不提取完整文件时隐藏按钮） | ~0.5h |
| **合计** | **~8.5h** |

#### 可选的简化路径

如果 PdfPig + SkiaSharp 组合太重或效果不佳，备选方案：
- **`Windows.Data.Pdf`**（WinRT API，系统内置）— 无需额外依赖，但只支持 Win10 1809+
- **PdfiumViewer**（封装 Google PDFium）— 有 native DLL，集成复杂
- **继续只用 WebView2** — 大 PDF 保持当前行为（仅元数据），小 PDF 用 WebView2 渲染

#### 2A.4 WAV 元数据
- **文件**: `Core/Utils/RiffParser.cs` (新增)
- **扩展名**: `.wav`
- **类别**: 第三类（纯信息展示）
- **信息面板**: 采样率、位深、声道数、时长、数据大小
- **内容区**: ⬜ 空置（或展示大号时长 + 波形概念图）
- **实现**: RIFF chunk 解析，读 `fmt ` chunk 获取采样率/声道/位深，读 `data` chunk size 计算时长
- **预估**: ~2h

#### 2A.5 FLAC 元数据
- **文件**: `Core/Utils/FlacParser.cs` (新增)
- **扩展名**: `.flac`
- **类别**: 第三类（纯信息展示）
- **信息面板**: 采样率、位深、声道数、时长
- **内容区**: ⬜ 空置
- **实现**: 读 STREAMINFO 块获取采样率/声道/位深/总样本数 → 计算时长
- **预估**: ~2h

#### 2A.6 SQLite 头信息
- **文件**: `Core/Utils/SQLiteParser.cs` (新增)
- **扩展名**: `.sqlite`, `.sqlite3`, `.db`, `.db3`
- **类别**: 第二类（全信息格式）
- **信息面板**: 版本、页大小、编码 (UTF-8/UTF-16)、表数
- **内容区**: 表列表（表名 | 行数），可滚动
- **实现**: 读 header string + 页大小 (offset 16) + 编码 (offset 18) + 读 `sqlite_master` 表 → 表数
- **预估**: ~1h

#### 2A.7 ISO 卷标
- **文件**: `Core/Utils/IsoParser.cs` (新增)
- **扩展名**: `.iso`
- **类别**: 第三类（纯信息展示）
- **信息面板**: 卷标、格式 (ISO 9660/Joliet/UDF)、总大小
- **内容区**: ⬜ 空置
- **实现**: 读 offset 0x8000 主卷描述符获取卷标、格式类型
- **预估**: ~1h

#### 2A.8 STL 三角面数
- **文件**: `Core/Utils/StlParser.cs` (新增)
- **扩展名**: `.stl`
- **类别**: 第三类（纯信息展示）
- **信息面板**: 三角面数、格式 (binary/ASCII)
- **内容区**: ⬜ 空置
- **实现**: binary STL: offset 80 读 uint32 → triangle count; ASCII STL: 数 "facet" 关键字
- **预估**: ~1h

#### 2A.9 GZ/BZ2/XZ/Zstd 头部
- **文件**: `Core/Utils/ArchiveHeaderParser.cs` (新增)
- **扩展名**: `.gz`, `.bz2`, `.xz`, `.zst`
- **类别**: 第三类（纯信息展示）
- **信息面板**: 原始文件名、压缩方法、原大小
- **内容区**: ⬜ 空置
- **实现**: 各自头部解析获取原始文件名(如有)、原大小
- **预估**: ~2h

### Phase 2B — 音频元数据

#### 2B.1 MP3 (ID3v2)
- **文件**: `Core/Utils/Id3v2Parser.cs` (新增)
- **扩展名**: `.mp3`
- **类别**: 第三类（纯信息展示，但封面可作为内容）
- **信息面板**: 标题、歌手、专辑、时长、比特率、采样率
- **内容区**: 封面图片(如有 APIC frame) 居中展示 + 标题/歌手大号显示。无封面时空置
- **实现**: 解析 ID3v2 header → 帧列表; 提取 TIT2/TPE1/TALB/TLEN; 可选 APIC 封面
- **预估**: ~5h

### Phase 2C — 图像格式解码（直接显示图片）

#### 2C.1 TGA 图像
- **文件**: `Core/Utils/TgaDecoder.cs` (新增) + `MainWindow.Preview.cs` 修改
- **扩展名**: `.tga`, `.targa`
- **类别**: 第一类（内容 ≠ 信息）
- **信息面板**: 分辨率、位深、压缩类型 (Type 2/10/3)
- **内容区**: ✅ 解码后的图片
- **实现**: DIY 纯 C# ~200 行; 解析 18 字节头部 → 读取像素数据 → `BitmapSource.Create()`; 支持 Type 2(无压缩), Type 10(RLE), Type 3(灰度)
- **集成**: 在 `ShowImagePreviewAsync` 的 `catch (Exception)` 中加 `.tga` 后备分支
- **预估**: ~6h

---

## Phase 3 — 中等价值格式

| # | 格式 | 类别 | 信息面板 | 内容区 | 文件 | 预估 |
|---|------|------|---------|--------|------|------|
| 3.1 | SVG | 第一类 | viewBox 宽高 | ✅ 渲染为图片 | `SvgParser.cs` | ~3h |
| 3.2 | TTF/OTF 字体 | 第一类 | 字体名、样式、字形数 | ✅ 字体样本 "AaBbCc..." | `FontParser.cs` | ~4h |
| 3.3 | LNK 快捷方式 | 第二类 | 目标路径 | ℹ️ 链接详情（参数/快捷键） | `LnkParser.cs` | ~3h |
| 3.4 | DBF 数据库 | 第二类 | 记录数、字段数 | 📋 字段定义列表（名\|类型\|长度） | `DbfParser.cs` | ~3h |
| 3.5 | ICO 图标信息 | 第二类 | 图标数、各尺寸/色深 | 🖼 图标缩略图排列 | `IcoParser.cs` | ~2h |
| 3.6 | SRT/ASS/VTT 字幕 | 第一类 | 条目数、时间范围 | ✅ 文本内容（复用 TextPreview） | 复用 TextPreview | ~2h |
| 3.7 | DOCX/XLSX/PPTX | 第三类 | 页数 | 标题、作者 | `OfficeParser.cs` | ~6h |
| 3.8 | EPUB | 第三类 | 标题、作者、语种 | 🖼 **封面图**（从 OPF 提取 zip 内的图片） | `EpubParser.cs` | ~5h |
| 3.9 | HDR 图像 | 第一类 | 分辨率、曝光值 | ✅ 解码(tone-mapped)后图片 | `HdrDecoder.cs` | ~8h |
| 3.10 | **🎵 音频播放** | 播放器 | WAV/FLAC 同 Phase 2A；MP3 同 Phase 2B | ▶ `MediaElement` + 播放/暂停/进度条/音量 | `MainWindow.Preview.cs` | ~6h |
| 3.11 | **🎬 视频播放** | 播放器 | 分辨率、时长、编解码器 | ▶ `MediaElement` + 播放/暂停/进度条 | `MainWindow.Preview.cs` | ~4h |

### 3.10 音频播放 详细设计

**影响格式**: MP3, WAV, FLAC（Phase 2A/2B 已有元数据解析）

**内容区变更**:

```
初始状态:                        点击播放后:
┌─────────────────┐              ┌─────────────────┐
│  封面(若有)      │              │  ▶❚❚ 暂停      │
│  标题 歌手       │   ▶ 点击   → │  01:23 / 03:42  │
│  [▶ 播放]       │              │  ██████░░░░░░   │
│                 │              │  🔊 ───○──      │
└─────────────────┘              └─────────────────┘
```

**技术实现**:
- `MediaElement` 丢到内容区，`Source = new Uri(tempFile)`
- **切换文件时**: 切到新的音频/视频前，先 Stop() + 清 Source + 卸载 MediaElement
- **压缩包内大文件**: 首次点击播放时才开始提取到 temp。可显示 "正在提取…" 提示
- **进度条**: `MediaElement` 本身不提供 UI，需自己实现 Slider + Timer（`Position` 轮询）
- **音量**: `MediaElement.Volume` 0-1

**工具栏联动**:
- 播放中显示 ▶/❚❚ 按钮（已有工具栏，格式特定按钮）
- 不需要额外依赖

### 3.11 视频播放 详细设计

**影响格式**: MP4 (.mp4, .m4v), WMV (.wmv)

> MKV/WebM/FLV 等 `MediaElement` 不支持的格式 → 信息面板正常显示元数据，内容区显示 "此格式暂不支持播放"

**内容区变更**:

```
初始状态:                        点击播放后:
┌─────────────────┐              ┌─────────────────┐
│                 │              │                 │
│  ▶ 点击播放     │              │  ▶❚❚          │
│                 │   ▶ 点击   → │  视频画面       │
│  MP4 视频       │              │                 │
│  12.3 MB        │              │  01:23 / 10:30  │
│                 │              │  ██████░░░░░░   │
└─────────────────┘              └─────────────────┘
```

**大文件处理**:
- 视频文件可能很大（几百 MB 到 GB）。点击播放后先显示 "正在提取…（xx MB / yy MB）"，用 `ExtractEntryAsync` 的 Progress 回调更新
- 提取完成后 `MediaElement.Source = new Uri(tempFile)`
- **注意**: 大文件提取可能耗时较长，确保用户能看到进度反馈

**支持的视频格式**:
| 格式 | 扩展名 | 原生播放 | 备注 |
|---|---|---|---|
| MP4 (H.264) | `.mp4`, `.m4v` | ✅ Win10 内置 | HEVC 扩展可能需额外安装，H.264 通吃 |
| WMV | `.wmv`, `.asf` | ✅ WMP 原生 | 零问题 |
| AVI | `.avi` | ⚠️ 跟编码器 | 不保证，随缘 |
| MKV | `.mkv` | ❌ | 不支持的格式提示 |
| WebM | `.webm` | ❌ | 不支持的格式提示 |

**清理**: 切换文件时 `MediaElement.Close()` + 删除 temp 文件（同现有清理逻辑）

---

## Phase 4 — 高难度格式

| # | 格式 | 说明 | 预估 |
|---|------|------|------|
| 4.1 | PE 图标提取 | 遍历 PE 资源目录提取 RT_ICON，解码 ICONIMAGE 结构 | ~10h |
| 4.2 | ICL 图标库 | 复用 PE 检测 + 图标提取，展示图标网格 | ~8h |
| 4.3 | MKV/WebM | EBML 解析获取时长/分辨率（不能播放） | ~5h |
| 4.4 | DICOM 元数据 | 读 tag 获取患者/影像信息 | ~6h |
| 4.5 | CER/PFX 证书 | ASN.1 解析主题/有效期 | ~4h |
| 4.6 | ODT/ODS/ODP | ZIP 内读 meta.xml | ~4h |
| 4.7 | VHD/VMDK | 头部解析磁盘容量 | ~4h |
| 4.8 | MOBI/AZW3 | Palm DB 格式解析 | ~8h |
| 4.9 | DXF/STEP/FBX | 实体数/面数 | ~15h |
| 4.10 | EXR 图像 | 需引入 Magick.NET 依赖 | 待定 |

---

## Phase 5 — 元数据优先提取与两步式预览优化

> **定位**: 在所有 Phase 2/3/4 格式解码器完成后实施的横切面优化。
> **不依赖魔数检测**，但可与之协同（魔数检测做格式识别，此 Phase 做元数据提前提取）。

### 动机

当前所有格式的执行路径：
```
ExtractPreviewFileAsync (完整文件) → ShowXxxPreview (元数据 + 内容一起展示)
```

**问题**：
- 对于大文件（如 500MB 的视频），完整的元数据仅需前 100KB，却要解压全部文件
- 解压和元数据解析串行，用户必须等到完整提取完成才能看到任何信息
- 纯元数据格式（PE/audio/SQLite/ISO/torrent）实际上不需要提取完整文件

### 目标

将 ShowPreviewAsync 拆分为两阶段：
```
① 提取头部 N 字节 → 解析元数据 → 显示信息面板
②（可选）提取完整文件 → 加载内容面板
```

### 可行性分析：100KB 头部能拿到什么

| 格式 | 元数据位置 | 100KB 够？ | 备注 |
|------|-----------|:----------:|------|
| PE (.exe/.dll) | DOS Header → PE Header | ✅ 绰绰有余 | ~64 字节即可 |
| PDF | Info dict 在尾部 xref + trailer | ❌ 不够 | 需要头部+尾部各~100KB（双端提取）|
| Office (docx/xlsx/pptx) | 内部 ZIP 的 `docProps/core.xml` | ⚠️ 看情况 | 取决于 docProps/ 在内部 ZIP 中的偏移 |
| WAV | RIFF header + fmt chunk | ✅ 绰绰有余 | 前 44 字节 |
| FLAC | STREAMINFO metadata block | ✅ 绰绰有余 | 前几 KB |
| SQLite | Header (offset 0–100) | ✅ 绰绰有余 | 前 100 字节 |
| ISO | Volume Descriptor (sector 16) | ✅ 足够 | ~32KB 偏移，在 100KB 内 |
| Torrent | Bencoded dict 从文件头开始 | ✅ 足够 | 解析到 info dict 即可停 |
| MP4 | `moov` box 可能在文件尾部 | ❌ 经常不够 | 非 fast-start 优化的文件 moov 在尾部 |
| MKV | Segment Info 通常在文件前部 | ✅ 通常够 | EBML 头 + SeekHead + SegmentInfo |
| AVI | RIFF header + main header | ✅ 足够 | 前几百字节 |
| JPEG/PNG/GIF/BMP/WebP | 分辨率在文件头 | ✅ 绰绰有余 | 几 KB 内 |
| TTF/OTF/WOFF | Table Directory 在文件头 | ✅ 足够 | ~12KB |
| TGA | 18 字节头部 | ✅ 绰绰有余 | |
| HDR | 分辨率在第一行 | ✅ 足够 | |
| 文本 (txt/code) | 编码检测仅需前 4KB | ✅ 足够 | 但显示全文仍需要完整提取 |
| HTML/Markdown/SVG | 元数据没有，需要全文渲染 | ❌ 不适用 | 不是元数据格式 |

**覆盖 14/18 种格式**。PDF 和 MP4 是明显例外，需要特殊处理。

### 引擎限制

| 压缩格式 | 部分提取可行性 | 实现方式 |
|----------|:------------:|---------|
| ZIP (Deflate) | ✅ 可以 | 解压流前 N 字节后关闭 |
| ZIP (Store) | ✅ 可以 | 直接读偏移 |
| 7z (非固实) | ✅ 可以 | `entry.Extract(stream)` 后截断 |
| **7z (固实)** | ❌ **无法部分提取** | 固实 block 包含多个文件，不解压完整个 block 拿不到中间条目 |
| RAR | ✅ 可以 | 同 7z 非固实 |
| Tar/Gz | ✅ 可以 | TarInputStream 流式读取，读够 N 字节关闭 |

对于 7z 固实，需要 fallback 到：
1. 基于扩展名判断格式（无魔数检测）
2. 提取完整文件后再解析元数据

### `ArchiveEntryExtractor` 新增方法

```csharp
/// <summary>
/// 提取条目前 maxBytes 字节到临时文件。
/// 用于元数据优先提取——避免为读取头部而解压整个文件。
/// </summary>
public static async Task<string?> ExtractEntryHeadToFileAsync(
    string archivePath, string entryName, string outputPath,
    int maxBytes, ArchiveFormat format, string? password = null,
    CancellationToken ct = default);
// 返回 null 表示不支持部分提取（如 7z 固实），调用者应 fallback
```

实现策略：

- **ZIP (Deflate)**: `ZipInputStream.GetNextEntry()` → 开 `DeflateStream` → `CopyToAsync` 累计读取 maxBytes → 关闭
- **ZIP (Store)**: 直接读取 `LocalFileHeader` 后的偏移，无需解压
- **7z (非固实)**: `SevenZipExtractor.ExtractFile` → 写 maxBytes 到 `MemoryStream` → 写到磁盘
- **7z (固实)**: 跳过，返回 `null`，调用者 fallback 到全文件提取
- **Tar/Gz**: `TarInputStream` 读第一个条目，`CopyToAsync` maxBytes → 关闭

```csharp
/// <summary>
/// 提取头部 + 尾部（用于 PDF/MP4 等元数据在尾部的格式）。
/// </summary>
public static async Task<(string? headFile, string? tailFile)> ExtractEntryHeadTailToFileAsync(
    string archivePath, string entryName, string outputDir,
    int headBytes, int tailBytes, ArchiveFormat format,
    string? password = null, CancellationToken ct = default);
```

**注意**: 提取尾部对于 Deflate ZIP 的代价接近全量提取（必须解压完整流才能知道结尾 N 字节）。对于 Store ZIP 可以直接 seek。使用前需要根据实际压缩方法判断是否值得。

### `ShowPreviewAsync` 改造流程

改造后：

```
ShowPreviewAsync(item)
  │
  ├── ① 清理：ClearPreviewContent + ClearPreviewTemp + 清空 InfoPanel
  │
  ├── ② 大小检查（MetadataOnlyExtensions 免检）
  │
  ├── ③ 显示基础信息面板 + Loading
  │
  ├── ④ 提取头部 N 字节（ExtractEntryHeadToFileAsync）
  │     ├── ZIP → 通常 100KB
  │     ├── 7z 固实 → 跳过（fallback 到扩展名判断）
  │     └── 失败 → fallback 到全文件提取
  │
  ├── ⑤ 解析元数据 → 填充 PreviewExtraInfoPanel
  │     └── 调用格式对应的 GetXxxMetadata(headFile) 方法
  │
  ├── ⑥ 分类处理：
  │     ├── 纯元数据格式 (PE/Audio/SQLite/ISO/Torrent/Video)
  │     │     └── ✅ 完成（不加载内容面板）
  │     │         注：Video 格式需要时长为内容显示依据，
  │     │         100KB 头部不够时仍需全文件提取
  │     │
  │     ├── 需要内容渲染的格式 (Image/Text/GIF/Font/HTML/MD/SVG)
  │     │     ├── 取消 Loading
  │     │     ├── ExtractPreviewFileAsync (全文件，后台进行)
  │     │     └── 加载内容控件
  │     │
  │     └── 需要双端提取的特例 (PDF/MP4)
  │           ├── ExtractEntryHeadTailToFileAsync
  │           ├── 解析元数据（补充尾部信息）
  │           └── → 纯元数据或全量渲染
  │
  └── ⑦ finally: HidePreviewLoading
```

### 工作项

#### 5.1 ArchiveEntryExtractor 新增部分提取方法
- **文件**: `Core/Utils/ArchiveEntryExtractor.cs`
- 新增 `ExtractEntryHeadToFileAsync` — ZIP/非固实 7z/Tar/Gz 支持
- 新增 `ExtractEntryHeadTailToFileAsync` — 用于 PDF/MP4 双端提取
- 检测 7z 固实并返回 null，由调用者 fallback
- 为 Deflate ZIP 的 tail 提取添加性能警告注释
- **预估**: ~3h

#### 5.2 每个格式分解为 GetMetadata + ShowContent
- 对 Phase 2/3/4 所有格式解码器，要求：
  - 元数据解析独立为 `static GetXxxMetadata(Stream/byte[]) → Dictionary<string,string>`
  - 内容加载保持为 `ShowXxxPreview(string filePath, item)`
- 已有格式（Image/Text/PE/PDF/Audio/SQLite/ISO/Torrent/Video/Office）逐一拆分
- **预估**: ~1h/格式 × 15+ 格式 = ~15h+

#### 5.3 ShowPreviewAsync 两阶段编排
- **文件**: `MainWindow.Preview.cs`
- 修改入口流程为两阶段
- 头部提取失败 → 静默降级到原全文件流程
- Loading 状态管理（第一阶段：元数据解析中；第二阶段：内容加载中）
- **预估**: ~4h

#### 5.4 特殊格式处理：PDF / MP4
- **文件**: `MainWindow.Preview.cs` + 对应 Parser
- PDF: 头部 100KB 不够 → 双端提取 → 若 tail 搜不到 xref 则全量提取
- MP4: head 搜 `ftyp`, tail 搜 `moov` box → 从中解析 `mvhd` 时长
- **预估**: ~3h

#### 5.5 7z 固实检测与降级
- **文件**: `Core/Utils/ArchiveEntryExtractor.cs`
- `SevenZipExtractor` 提供 `IsSolidArchive(stream)` 判断
- 固实时跳过部分提取，使用扩展名分支逻辑
- **预估**: ~2h

### 总计预估

| 工作项 | 预估 |
|--------|------|
| 5.1 ArchiveEntryExtractor 扩展 | ~3h |
| 5.2 格式分解 × 15+ | ~15h |
| 5.3 两阶段编排 | ~4h |
| 5.4 PDF/MP4 特殊处理 | ~3h |
| 5.5 7z 固实检测 | ~2h |
| **合计** | **~27h** |

### 风险与备选

1. **收益取决于用例** — 大部分压缩包内文件 < 10MB，完整提取和头部提取差异不大。大视频/ISO 文件才有显著收益
2. **7z 固实无法优化** — 固实 7z 永远需要完整提取，这是压缩算法限制，无法绕过
3. **简化路径** — 如果 Phase 2/3/4 工作量已经很大，Phase 5 可推迟到下一迭代。纯元数据格式当前已经只提取一次，用户体验上差别不大
4. **PDF 双端提取的代价** — Deflate ZIP 中的 PDF 提取尾部需要解压完整流，实际效果等同于全量提取。Store ZIP 可以优化

---

## 实施顺序建议

```
Phase 0  [工具栏]           → 基础框架，为后续所有格式提供交互
  ↓
Phase 1  [信息面板]          → 信息展示容器，所有格式共用
  ↓
Phase 2A [信息类格式]       → Torrent, PE, PDF, WAV, FLAC, SQLite, ISO, STL, GZ/BZ2/XZ
  ↓
Phase 2B [音频元数据]       → MP3 ID3v2
  ↓
Phase 2C [图像解码]         → TGA
  ↓
Phase 3  [中等价值]         → SVG, TTF, LNK, DBF, ICO, 字幕, Office, EPUB(封面), HDR, 🎵音频播放, 🎬视频播放
  ↓
Phase 4  [高难度]           → PE图标, ICL, MKV, DICOM, 证书, VHD, ...
  ↓
Phase 5  [元数据优先提取]    → 两步式预览优化（格式基础全部完成后）
```

每 Phase 可并行开发内部格式（各格式解码器互不依赖）。
