# 快速预览与渐进式加载 (Quick Preview & Progressive Loading)

> **状态**: 📋 待计划 | **创建日期**: 2026-06-29 | **最近修正**: 2026-10-20
> **关联计划**: `preview-extended-formats.md`（基础预览框架，进行中）、`preview-avalonia-opportunities.md`（Avalonia 下的预览能力变化，已归档）、`preview-two-phase-loading.md`（✅ 已完成的两阶段加载，本计划叠加其上）、`html-preview-webview-fallback.md`（✅ HTML WebView 双轨 + ReverseMarkdown 降级，已完成）
> **前置**: 无（Avalonia 直接实施；规则 11：新功能只进 Avalonia。WPF 版已整体删除，无同步负担）

## TL;DR

新增三种预览模式，用户根据场景在速度和完整度之间选择：

| 模式 | 行为 | 适合场景 |
|:----:|------|---------|
| **⚡ 快速预览** | 每个格式只读取最少数据就显示，提供"查看完整"按钮 | 快速浏览大批文件，只对感兴趣的几个看完整内容 |
| **▶ 渐进式加载** | 先快速预览，后台自动加载完整内容，加载完无缝切换 | 默认模式——秒开预览，越看越清晰 |
| **📄 完整加载** | 当前行为：一次性加载全部内容再显示 | 需要完整信息（如搜索文本全文）或对加载速度不敏感 |

---

## 1. 格式支持总表

按格式类型分析三种模式下的行为。**依赖标记**: 🟢 纯 C#（框架无关）、🟡 需 Avalonia 原生控件、🔴 需外部依赖

### 1.1 天然支持快速+渐进（数据可分段消费）

这些格式可以低成本展示一部分，后台继续加载剩余部分：

> **⚠️ 现状校准（2026-10-20）**：下表除 DBF、字幕外均为 Avalonia 已实现的 `PreviewType`（Text/Csv/Pe/Image/AnimatedImage/Svg/Font/Pdf/Docx/Xlsx/Pptx/Markdown）。**DBF 与字幕（SRT/ASS/VTT）现状为 `Unsupported`**——Core `FileFormatDetector` 虽识别 `FileFormat.Subtitle`，但 `PreviewService.ClassifyPreview`/魔数映射均无 Subtitle 分支（`TextExtensions` 亦不含 `.srt` 等），需先建基础预览（同 §1.3 校准，计入前置工作项，不计本计划工时）。另：ICO 多帧为独立 `PreviewType.IcoGallery`（现状已实现多帧画廊）。

| 格式 | 快速预览 | 渐进加载 | 完整加载 | 依赖 |
|:----:|---------|---------|---------|:----:|
| **图片** (JPG/PNG/BMP/WebP/AVIF) | `SKCodec` 降采样解码至预览窗格尺寸（如 400px）——复用现有 `ShowImage` 已有的 >1920px 降采样管线（`_skOriginalPreview` 缓存），秒出 | 后台解码全尺寸，完成后替换 Source | 全尺寸一次性解码（现状） | 🟢 |
| **动画图像** (GIF/Animated WebP/APNG，`AnimatedImage`，自建解码器) | 只解码第一帧，显示静止图 | 后台加载完整动画帧，替换为动画源（现有 GifDecoder SKCodec 通用帧 API 零改动） | 完整动画播放 | 🟢 |
| **文本** (TXT/Code/LOG) | 读前 N 字节（如 1024），末尾加 `…` 标记 | 后台 `FileStream` 继续读取剩余，追加到 `TextBox` | 全文加载 | 🟢 |
| **CSV** | 读前 N 行 × 检测到的列数 | 后台读更多行追加到 DataGrid | 全部行加载 | 🟢 |
| **字幕** (SRT/ASS/VTT) | 读前 N 条，取时间范围 | 后台读完剩余，追加列表 | 全部条目 | 🔴* |
| **PDF** | PdfPig 渲染第一页（小尺寸 400px） | 后台依次渲染第 2、3、4…页，翻页即时显示 | 全部页渲染完成再展示 | 🟢 |
| **字体** (TTF/OTF/WOFF) | 读 name table + glyph count，渲染小样本 "AaBb" | 后台加载完整字体，可支持更丰富的字形展示 | 完整字体渲染 | 🟢 |
| **DBF** | 读前 N 条记录的字段值 | 后台读更多记录 | 全部记录 | 🟢 🔴* |
| **SVG** | `Avalonia.Svg.Skia` 原生渲染（本身就快，不需要分段） | 同快速（无需渐进） | 同快速 | 🟡 |

> *DBF：快速预览能力依赖基础 DBF 元数据预览先行（当前 Unsupported）。
> *字幕：Core `FileFormatDetector` 识别 `FileFormat.Subtitle` 与条目数，但 UI 侧 `PreviewService.ClassifyPreview`/魔数映射均无 Subtitle 分支（`TextExtensions` 亦不含 `.srt` 等）→ 零预览路径，需基础预览先行（同 DBF）。

### 1.2 元数据→内容（快速只展示元数据，渐进加载完整内容）

| 格式 | 快速预览 | 渐进加载 | 完整加载 | 依赖 |
|:----:|---------|---------|---------|:----:|
| **SQLite** | 表名+行数 | 后台加载表内容到 DataGrid | 所有表数据 | 🟢 |
| **Office (DOCX)** | XmlReader 流式读前 5 个 `<w:p>`，显示前 5 段 | 后台继续读剩下段落，追加 | 全文 | 🟢 |
| **Office (XLSX)** | 读前 20 行数据 | 后台读更多行，DataGrid 追加 | 所有行 | 🟢 |
| **Office (PPTX)** | 读 slide1.xml，显示第一页文本 | 后台读 slide2、3…，翻页即时 | 所有页 | 🟢 |
| **Markdown** | 纯文本取前 N 字符显示 | 后台 Markdig 解析 → Avalonia 原生控件渲染全文 | 完整渲染 | 🟡 |
| **EPUB** | 标题/作者 + 封面小图 | 后台加载全文内容 | 完整显示 | 🟢 |
| **HTML** | 无 WebView 降级路径取前 N 字符纯文本起步 | 后台 ReverseMarkdown → Markdig 控件树渲染全文；WebView 主轨可用时直接原生渲染（✅ 双轨已落地：NativeWebView 主轨 + ReverseMarkdown 降级，见 `html-preview-webview-fallback.md`） | 完整渲染（主轨/降级双轨均需可用） | 🟡 |

### 1.3 快速 = 完整（元数据格式，不需要渐进）

这些格式本身就是元数据，读到头就是全部：

> **⚠️ 现状校准（2026-08-06）**：以下表格中 **✅ 已实现** 的格式在 Avalonia `PreviewService.ClassifyPreview` 中已有对应 `PreviewType`；**🔴 未实现** 的格式当前归类为 `PreviewType.Unsupported`（无任何元数据预览），需先实现基础元数据预览才能受益于本计划的三模式，故从"快速=完整"表**降级**——标注后保留在表内以便规划，但 DoD 中不得计入本计划工时。

| 格式 | 快速预览 | 渐进/完整 | 现状 |
|:----:|---------|:---------:|:----:|
| **PE** (exe/dll/sys) | 公司/版本/架构/子系统 | 🟢 完全一致 | ✅ 已实现（PreviewType.Pe） |
| **Torrent** | InfoHash/文件列表/Magnet | 🟢 完全一致 | ✅ 已实现（PreviewType.Torrent） |
| **ISO** | 卷标/格式/大小 | 🟢 完全一致 | ✅ 已实现（PreviewType.Iso） |
| **音频元数据** (WAV/FLAC/MP3) | 采样率/时长/编码/标签 | 🟢 完全一致（播放另需插件） | ✅ 已实现（PreviewType.Audio） |
| **视频元数据** (MP4/MKV/AVI) | 分辨率/时长/编码 | 🟢 完全一致（播放另需插件） | ✅ 已实现（PreviewType.Video） |
| **LNK** | 目标路径/参数 | 🟢 完全一致 | 🔴 未实现（Unsupported，需先建基础预览） |
| **STL** | 三角面数/格式(binary/ASCII) | 🟢 完全一致 | 🔴 未实现（Unsupported，需先建基础预览） |
| **GZ/BZ2/XZ/Zstd** | 原始文件名/压缩方法/原大小 | 🟢 完全一致 | 🔴 未实现（Unsupported，需先建基础预览） |
| **证书** (CER/PFX) | 颁发者/主题/有效期 | 🟢 完全一致 | 🔴 未实现（Unsupported，需先建基础预览） |
| **VHD/VMDK** | 磁盘容量/格式版本 | 🟢 完全一致 | 🔴 未实现（Unsupported，需先建基础预览） |
| **DICOM** | 患者信息/影像参数 | 🟢 完全一致 | 🔴 未实现（Unsupported，需先建基础预览） |
| **MOBI/AZW3** | 书名/作者/出版方 | 🟢 完全一致 | 🔴 未实现（Unsupported，需先建基础预览） |
| **DXF/STEP/FBX** | 实体数/面数 | 🟢 完全一致 | 🔴 未实现（Unsupported，需先建基础预览） |

> **修正说明**：原表将 LNK/STL/GZ/证书/VHD/DICOM/MOBI/DXF/DBF 等视为"已有元数据预览"是 2026-06 规划时的假设，现状（Avalonia `PreviewType` 枚举）仅 PE/Torrent/ISO/Audio/Video 已实现。这些格式的快速预览能力**依赖各自的基础元数据预览先行**，本计划只覆盖 P0–P2 已实现格式的三模式改造；🔴 格式降级为独立的前置工作项（见 §4 优先级调整）。

### 1.4 不支持或特殊处理

| 格式 | 原因 |
|:----:|------|
| **AI** (Illustrator) | 必须安装 Ghostscript 才能做任何渲染。无 GS 时降级为第三类（仅文件基本属性） |
| **音视频播放** | 播放未来可能以插件形式提供（代码库当前无播放器依赖），跟三模式正交。元数据展示走第三类（始终可用） |

> **2026-10-20 修正**：原第三行「HTML 无 WebView 后无法渲染，仅支持纯文本预览」已删除——HTML WebView 双轨（NativeWebView 主轨 + ReverseMarkdown 降级）已落地（`html-preview-webview-fallback.md` ✅），HTML 按 §1.2 行执行。

---

## 2. UX 设计

### 2.1 模式切换

在预览工具栏右侧添加一个**三段式切换按钮**：

```
[预览：⚡ 快速  |  ▶ 渐进  |  📄 完整]
```

- 点击切换模式，立即生效
- 当前模式用高亮/填充色表示
- 切换模式时取消当前的后台加载任务，按新模式重新开始

### 2.2 模式指示灯

在预览内容区右上角显示当前模式及状态：

```
┌──────────────────────────────────────┐
│ 文件名.ext              [⚡ 快速预览] │
│                                      │
│  (预览内容)                           │
│                                      │
│  ⚡ 仅显示部分内容 [查看完整 →]       │
└──────────────────────────────────────┘
```

快速模式：显示提示条 + "查看完整"按钮
渐进模式：显示加载动画（三点跳动或薄进度条）+ 无按钮（自动升级）
完整模式：无提示

### 2.3 设置项

`AppSettings`（仅 Avalonia `Models/AppSettings.cs`；WPF 版已删除，无字段同步负担）新增：

```csharp
public class AppSettings
{
    // 预览模式: 0=完整加载, 1=快速预览, 2=渐进式加载（默认）
    public int PreviewMode { get; set; } = 2;

    // 快速预览参数（各格式独立控制）
    public int QuickPreviewImageMaxDimension { get; set; } = 400;  // 图片最大解码尺寸
    public int QuickPreviewPdfPageCount { get; set; } = 1;         // PDF 预览页数
    public int QuickPreviewOfficeMaxParagraphs { get; set; } = 10; // Office 最大段落数
    public int QuickPreviewSrtMaxEntries { get; set; } = 20;       // 字幕最大条目数

    // 渐进加载缓冲
    public int ProgressiveLoadBatchSize { get; set; } = 4096;      // 文本渐进每批字节数
    public int ProgressiveLoadIntervalMs { get; set; } = 50;       // 批次间隔（避免 UI 卡顿）
}
```

> **⚠️ 与现有字段的整合（2026-08-06 修正）**：原计划新增 `QuickPreviewTextMaxBytes`/`QuickPreviewCsvMaxRows` 与现有预览设置**语义重复**，已删除——快速/完整的上限差异通过**复用现有字段 + 区分档位**实现，避免两套平行上限漂移：
>
> | 用途 | 现有字段（AppSettings.cs） | 快速模式取值 | 完整模式取值 |
> |------|---------------------------|-------------|-------------|
> | 文本最大读取字节 | `MaxTextPreviewBytes` = 1MB（现有，默认完整模式上限） | 快速档 = min(现有, 快速上限 2048B) | 现有默认 |
> | 表格最大行数 | `MaxTablePreviewRows` = 100（现有，完整上限） | 快速档 = min(现有, 50) | 现有默认 |
> | 表格最大列数 | `MaxTablePreviewCols` = 100 | 不变 | 不变 |
> | 文件大小上限 | `MaxPreviewFileSize` = 15MB | 不变 | 不变 |
>
> 实现方式：`PreviewService` 读取时若 `PreviewMode != 完整`，将上限临时取 `Math.Min(现有上限, 快速档常量)`；**不新增** `QuickPreviewTextMaxBytes`/`QuickPreviewCsvMaxRows` 两个持久化字段。快速档常量（2048/50）作为 `const` 或代码内常量，不进设置界面。

### 2.4 模式持久化

- 当前模式存储在 `AppSettings.PreviewMode`，跨 session 保持
- 切换模式时更新设置并保存

---

## 3. 技术架构

### 3.1 预览流程改造

> **⚠️ 现状校准（2026-08-06）**：`preview-two-phase-loading.md` **已实施完成**（2026-07-16）。`ShowPreviewAsync` 现有结构为 Phase 1（`ShowLoading` + `UpdateCommonMetadata` 立即信息栏 + `_previewLoadVersion` 版本守卫）→ Phase 2（异步提取 → 魔数检测 → `ShowXxx` 完整渲染）。本计划**叠加在 Phase 2 内**，不重建流程：

```
ShowPreviewAsync(item):                              // 现有两阶段结构不变
  │
  ├── Phase 1（已实现，不动）: ShowLoading + UpdateCommonMetadata + 版本号++
  │
  └── Phase 2（改造点）: 提取到 temp（不变）
        │
        ├── mode == 完整: 现有 ShowXxx → 全部加载 → 显示          （现状行为）
        ├── mode == 快速: ShowXxxQuick → 最少数据 → 显示 + "查看完整"按钮
        └── mode == 渐进:
              ├── ShowXxxQuick → 显示（同快速）
              └── _progressiveCts.Token → 后台 ShowXxxFull → 完成后无缝替换
  │
  └── 用户切换文件 / 切换模式 → _progressiveCts.Cancel() + 版本号++（见 3.3）
```

**取消机制协同（关键）**：
- `_previewLoadVersion`（现有，`Interlocked` 版本号）——防"过期异步结果覆盖新选择"：渐进后台完成后仍须检查 `version == _previewLoadVersion` 才允许替换 UI
- `_progressiveCts`（本计划新增，`CancellationTokenSource`）——主动取消浪费的后台加载：切换文件/模式时 `Cancel()` 立即中断正在进行的渐进批次
- **两者都要做**：版本号是最终防线（即使 CTS 未及时取消，版本检查也会丢弃过期结果）；CTS 是效率优化（避免无谓的解码/读取占用）。

### 3.2 核心接口

```csharp
public interface IQuickPreviewProvider
{
    /// <summary>格式是否支持快速预览</summary>
    bool SupportsQuickPreview { get; }

    /// <summary>快速预览（读取最少数据）</summary>
    Task ShowQuickAsync(string filePath, ArchiveItem item, CancellationToken ct);

    /// <summary>完整加载（用于渐进模式的后台任务）</summary>
    Task LoadFullAsync(string filePath, ArchiveItem item, IProgress<double>? progress, CancellationToken ct);

    /// <summary>快速预览到完整的切换（模式 2 用户点击按钮时）</summary>
    Task UpgradeToFullAsync(string filePath, ArchiveItem item, CancellationToken ct);
}
```

各格式的 `ShowXxxAsync`（`PreviewViewModel` 现有方法）实现此接口，或在 `PreviewService.ClassifyPreview` 分发后按 `PreviewType` switch 分支。

### 3.3 提取策略：内存 vs 硬盘

#### 决策原则

快速预览追求**速度**，应优先使用 `MemoryStream`（无磁盘 I/O）；完整预览追求**稳定性**，大文件应提取到硬盘（内存效率）。

| 因素 | 内存优先 | 硬盘优先 |
|------|---------|---------|
| 文件大小 | < 50MB | > 50MB |
| 预览模式 | 快速预览 | 完整预览 |
| 并发预览数 | 少（1-2 个） | 多（同时预览多个） |
| 格式特性 | 流式解码 | 需要随机访问 / 文件路径 |

#### 各格式策略

| 格式 | 快速预览 | 完整预览 | 说明 |
|------|---------|---------|------|
| 文本 | ✅ 内存 | ⚠️ 看大小 | `StreamReader` 支持流 |
| 图片 | ✅ 内存 | ⚠️ 看大小 | `SKBitmap.Decode(stream)` |
| PDF | ✅ 内存 | ⚠️ 看大小 | `PdfDocument.Open(stream)` |
| Office | ✅ 内存 | ⚠️ 看大小 | `XmlReader` 支持流 |
| CSV | ✅ 内存 | ⚠️ 看大小 | `StreamReader` 支持流 |
| SQLite | ⚠️ 仅元数据 | ❌ 必须硬盘 | SQLite 需要文件路径随机访问 |
| 视频 | ⚠️ 仅元数据 | ❌ 必须硬盘 | 播放器需要文件路径 |
| 音频 | ⚠️ 仅元数据 | ❌ 必须硬盘 | 播放器需要文件路径 |

#### 阈值与决策逻辑

```csharp
// 阈值（与 MantisZip 现有 MaxPreviewFileSize = 15MB 对齐）
const long MemoryThreshold = 50 * 1024 * 1024;  // 50MB

public async Task<Stream> ExtractForPreviewAsync(
    IArchiveEntry entry, PreviewMode mode, bool needsFilePath)
{
    // 格式要求文件路径（SQLite/视频/音频）→ 必须硬盘
    if (needsFilePath)
        return await ExtractToFileAsync(entry);
    
    // 快速预览 + 小文件 → 内存（秒开）
    if (mode == PreviewMode.Quick && entry.Size <= MemoryThreshold)
        return await ExtractToStreamAsync(entry);
    
    // 完整预览 或 大文件 → 硬盘（内存友好）
    return await ExtractToFileAsync(entry);
}
```

#### 快速预览的内存优化

```csharp
// ArchiveEntryExtractor 新增方法
public static async Task<MemoryStream> ExtractToStreamAsync(
    IArchive archive, string entryKey, string? password = null)
{
    var entry = archive.Entries.First(e => e.Key == entryKey);
    var stream = new MemoryStream();
    
    using var entryStream = entry.OpenEntryStream();
    await entryStream.CopyToAsync(stream);
    
    stream.Position = 0;  // 重置位置供读取
    return stream;
}
```

#### 预览流程改造

```
ShowPreviewAsync(item):
  │
  ├── Phase 1（已实现，不动）: ShowLoading + UpdateCommonMetadata + 版本号++
  │
  └── Phase 2（改造点）: 提取策略选择
        │
        ├── mode == 快速 + 小文件:
        │     └── 提取到 MemoryStream → ShowXxxQuick → 显示（秒开）
        │
        ├── mode == 快速 + 大文件:
        │     └── 只读元数据（内存）→ 显示"完整预览需加载"
        │
        ├── mode == 渐进:
        │     ├── 提取到 MemoryStream → ShowXxxQuick → 显示（同快速）
        │     └── _progressiveCts.Token → 后台 ShowXxxFull → 完成后无缝替换
        │
        └── mode == 完整:
              └── 提取到 temp 文件（硬盘）→ ShowXxx → 全部加载 → 显示
```

### 3.4 渐进加载管理器

```csharp
public class ProgressiveLoadManager : IDisposable
{
    private CancellationTokenSource? _cts;

    /// <summary>启动后台渐进加载</summary>
    public async Task StartProgressiveAsync(
        string filePath, ArchiveItem item,
        Func<string, ArchiveItem, IProgress<double>, CancellationToken, Task> loadFullAsync,
        Action onCompleted,  // 加载完成后的 UI 回调
        CancellationToken ct)
    {
        // 取消前一轮
        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await loadFullAsync(filePath, item, _progress, _cts.Token);
            // UI 线程回调
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(onCompleted);
        }
        catch (OperationCanceledException) { /* 切换文件/模式时正常取消 */ }
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    public void Dispose() => _cts?.Dispose();
}
```

### 3.5 各格式关键改造点

#### 图片

Avalonia 无 WPF `BitmapImage.DecodePixelWidth`，用 `SkiaSharp` 降尺寸解码（现有 `ShowImage` 已是 `SKBitmap`/`WriteableBitmap` 管线）：

```csharp
// 当前（现状，全尺寸解码）：SKBitmap.FromEncodedData(filePath) → WriteableBitmap
using var src = SKBitmap.Decode(filePath);
var bitmap = new WriteableBitmap(PixelSize.FromSize(src.Info.Size, 1), ...);
PreviewImage.Source = bitmap;

// 改造后：根据模式降采样
if (mode == PreviewMode.Quick || mode == PreviewMode.Progressive)
{
    int maxDim = AppSettings.Load().QuickPreviewImageMaxDimension;  // 400（现状 API 为 AppSettings.Load() 静态缓存）
    // 等比缩放到 maxDim 内再解码（SKBitmap.Resize 或 CreateScaledBitmap）
    var scaled = src.Resize(new SKImageInfo(maxDim, maxDim), SKFilterQuality.Medium);
    PreviewImage.Source = scaled.ToWriteableBitmap();
}
else
{
    // 完整模式：全尺寸解码（现状行为）
}
```

渐进模式下，后台用全尺寸重新解码，完成后替换 `PreviewImage.Source`。

#### 文本

```csharp
// 当前：读全文
string content = File.ReadAllText(filePath, encoding);

// 改造后：根据模式
if (mode == PreviewMode.Quick)
{
    // 只读前 N 字节（快速档上限 2048B，见 §2.3 与 MaxTextPreviewBytes 的档位整合）
    var buffer = new byte[QuickPreviewTextBatchSize];  // const 2048
    int read = fs.Read(buffer);
    content = encoding.GetString(buffer, 0, read) + "\n…";
}
else
{
    content = File.ReadAllText(filePath, encoding);  // 全文
}
```

渐进模式下，后台用 `FileStream` 分段 `Read`，每读完一批通过 `Dispatcher` 追加到 `TextBox`：

```csharp
// 渐进追加
var buffer = new byte[batchSize];
while ((read = fs.Read(buffer)) > 0)
{
    chunk = encoding.GetString(buffer, 0, read);
    // UI 线程追加
    await Dispatcher.UIThread.InvokeAsync(() => textBox.Text += chunk);
    await Task.Delay(intervalMs, ct);  // 控制节奏，不影响 UI 响应
}
```

#### PDF (第一页 + 逐页渐进)

```csharp
// 快速：只渲染第一页小图
using var doc = PdfDocument.Open(filePath);
var page = doc.GetPage(1);
var bitmap = RenderPageToBitmap(page, width: QuickPreviewImageMaxDimension);  // 400px
PreviewImage.Source = bitmap.ToAvaloniaBitmap();  // SKBitmap → WriteableBitmap（现有 ShowPdf 同款）

// 渐进：后台渲染高清版 + 后续页面
Task.Run(async () =>
{
    for (int i = 1; i <= doc.PageCount; i++)
    {
        var page = doc.GetPage(i);
        var bitmap = RenderPageToBitmap(page, width: 1200);
        // 缓存到页面数组
        _pdfPages[i] = bitmap.ToBitmap();
    }
});
```

#### Office (DOCX 流式读段落)

```csharp
// 快速：XmlReader 读前 N 段
using var reader = XmlReader.Create(archive.GetEntry("word/document.xml").Open());
int paraCount = 0;
while (reader.Read() && paraCount < QuickPreviewOfficeMaxParagraphs)
{
    if (reader is w:p element)
    {
        text += ExtractText(element) + "\n";
        paraCount++;
    }
}

// 渐进：后台继续读剩余段落
// 快速预览已完成，后台从上次位置继续读
```

---

## 4. 实现优先级

| 优先级 | 功能 | 预估 | 说明 |
|:------:|------|:----:|------|
| **P0** | 模式切换 UI + `AppSettings` + `ProgressiveLoadManager` 框架 | ~3h | 先搭好架子，所有格式共用的基础设施；叠加在现有两阶段加载之上 |
| **P1** | 图片快速预览 + 渐进加载 | ~2h | 改动最小（Avalonia `Bitmap` 手动创建降尺寸解码），拿来验证框架 |
| **P1** | 文本快速预览 + 渐进加载 | ~3h | 核心体验提升，用户感知最强 |
| **P2** | CSV 快速预览 + 渐进加载 | ~1h | 同文本模式 |
| **P2** | PDF 第一页快速 + 逐页渐进 | ~4h | PdfPig 天然支持，改动独立 |
| **P2** | Office 流式加载 | ~4h | 现有 DOCX/XLSX/PPTX 预览改造 |
| **P3** | 动画图像 (GIF/Animated WebP/APNG) 快速 + 渐进 | ~2h | 第一帧 → 完整动画（复用现有 AnimatedImage 解码器） |
| **P3** | 字体、ICO 多帧画廊等快速预览 | ~2h | 逐个格式适配（移除 DBF 与字幕——基础预览未实现，见下） |
| **P4** | SQLite、EPUB 等快速预览 | ~2h | 纯 C# 解析，改动小 |
| **P4** | Markdown 渐进加载 | ~2h | 现有 ReverseMarkdown→Markdig 控件树管线（已实现）加"前 N 字符快速档" |

> **⚠️ 2026-08-06 修正**：
> - ~~Markdown 渐进"依赖 Avalonia 迁移"~~ → **Markdown 控件树渲染已实现**（`MarkdownPreviewBuilder`），降为 ~2h
> - ~~字幕、DBF、字体~~ → **DBF 移除**：现状 `Unsupported`，需先建基础预览（不计本计划工时，见 §1.1/§1.3 校准）
> - **🔴 格式前置工作项**（独立计划/需求，不在本计划工时内）：LNK/STL/GZ-BZ2-XZ-Zstd/证书/VHD/VMDK/DICOM/MOBI/DXF/DBF/**字幕** 的基础元数据预览 → 完成后再按 §1.3 接入三模式
>
> **⚠️ 2026-10-20 修正**：
> - `PreviewType.Gif` 已废弃（不再产出）→ **`AnimatedImage`**（GIF / Animated WebP / APNG 共用，自建解码器），P3「GIF」行同步改名
> - **字幕移除**：SRT/ASS/VTT 现状 `Unsupported`（UI 侧无 Subtitle 映射），与 DBF 同为前置
> - ICO 多帧为独立 `PreviewType.IcoGallery`（现状已实现多帧画廊），快速档=首帧小图
> - §7 文件列表缩略图已独立为 `file-list-thumbnails.md` / `embedded-thumbnail-preview.md`（均不依赖本计划），本章细节已删除

**总计（本计划）**：~25h（不含 🔴 格式基础预览前置）

---

## 5. 平台策略：Avalonia-only（2026-08-06 修正 / 2026-10-20 更新 WPF 现状）

> **修正说明**：原计划按"WPF 先行，Avalonia 迁移时适配"撰写（2026-06 规划时 WPF 仍是主力）。现状 **WPF 版已整体删除**（`.sln` 仅存 Core/ShellExt/Avalonia 三项目），**本计划全部只进 Avalonia，无任何 WPF 同步负担**；Core 层逻辑（若有）天然共享。

| 组件 | Avalonia 实现 | 纯 C# 复用 |
|------|--------------|:---------:|
| `ProgressiveLoadManager` | 直接实现 | ✅ |
| `AppSettings` 新属性 | 仅 Avalonia `Models/AppSettings.cs`（WPF 已删除，无同步例外） | ✅ |
| 文本渐进追加 | Avalonia `TextBox.Text += chunk`（或 `TextBlock` 局部替换） | — |
| 图片渐进解码 | Avalonia `Bitmap` 手动创建（`SKBitmap`/`WriteableBitmap`），替代 WPF `BitmapImage.DecodePixelWidth` | — |
| PDF 渲染 | PdfPig + SkiaSharp → Avalonia `Bitmap`（现有 `ShowPdf` 同款管线） | ✅ |
| 工具栏三段切换 | Avalonia 控件（现有预览工具栏 `ToolbarButton`/`ToggleButton` 类样式） | — |
| 渐进取消 | `CancellationTokenSource` | ✅ |

**核心逻辑层（`ProgressiveLoadManager`、各格式数据读取策略）全部纯 C#，与 UI 框架无关**；Avalonia 侧只需实现 UI 渲染层（模式切换控件、`TextBox`/`Image` 赋值方式）。

---

## 6. Definition of Done

> **⚠️ 2026-10-20 修正**：DoD 基准为 Avalonia 已实现的 `PreviewType`（Text/Csv/Pe/Image/AnimatedImage/Svg/Font/Audio/Sqlite/Iso/Torrent/Docx/Xlsx/Pptx/Video/Html/Markdown/Pdf/IcoGallery）。DBF、字幕及 §1.3 🔴 格式为独立前置，不计入本 DoD。

- [ ] `ProgressiveLoadManager` 实现（取消/替换/批处理，与 `_previewLoadVersion` 版本守卫协同）
- [ ] `AppSettings` 新增预览模式 + 快速参数（复用现有 `MaxTextPreviewBytes`/`MaxTablePreviewRows` 档位）
- [ ] 工具栏三段式模式切换 UI（Avalonia）
- [ ] 模式指示灯 + 提示条 UI
- [ ] 图片快速预览（Avalonia `Bitmap` 降尺寸解码）+ 渐进加载
- [ ] 文本快速预览（前 N 字节）+ 渐进加载（分段追加）
- [ ] CSV 快速预览（前 N 行）+ 渐进加载
- [ ] PDF 快速预览（第一页）+ 渐进加载（逐页渲染）
- [ ] 动画图像 (GIF/Animated WebP/APNG) 快速预览（第一帧）+ 渐进加载
- [ ] Office (DOCX/XLSX/PPTX) 流式预览
- [ ] 字体快速预览；ICO 多帧画廊首帧快速档（字幕为前置，不计）
- [ ] SQLite/EPUB 快速预览
- [ ] Markdown 快速档（前 N 字符 → 后台 Markdig 控件树全文）
- [ ] HTML 快速档（纯文本起步 → 后台升级；WebView 双轨已落地，主轨与降级路径均需回归）
- [ ] 格式不支持快速预览时，模式切换按钮禁用或灰显
- [ ] 切换文件/模式时后台加载正确取消（CTS）+ 过期结果不覆盖（版本号）
- [ ] `dotnet build` 通过（Core + Avalonia）

---

## 7. 文件列表缩略图 → 已独立成后续计划（2026-10-20 删除细节）

本章（2026-06 撰写）的技术假设与后续探索结论漂移，缩略图需求已拆分为独立计划，且**均不依赖本计划先行**：

- **`file-list-thumbnails.md`**（未开始）：详情/平铺/内容三态布局 + 图像缩略图/文本文字卡片。内存提取走 `ArchiveEntryExtractor.ExtractHeadAsync` + `SKCodec`（8MB 条目上限），复用 `PreviewService.ClassifyPreview` 分类；**固定 128px**（显式不做本计划原 §7.1 的 32~128 尺寸选择器）；**排除** Magick.NET 格式（PSD/HDR/EXR/TIFF/TGA，插件尚不存在）。
- **`embedded-thumbnail-preview.md`**（未开始）：嵌入式（元数据内嵌）缩略图提取，完成后可作为含缩略图资源的格式。
- **`archive-manager.md`**（未开始）中另有「缩略图预览」条目，独立维护。

> 原因：原章假设的 LRU `ThumbnailCache`、Magick.NET 插件复用、`IPreviewProvider` 解码等已与实际架构不符——删除细节避免两份计划漂移。实现文件列表缩略图时以 `file-list-thumbnails.md` 为准，本计划只需提供「降采样解码 + 取消机制」两类可复用能力。