# 快速预览与渐进式加载 (Quick Preview & Progressive Loading)

> **状态**: 📋 待计划 | **创建日期**: 2026-06-29
> **关联计划**: `preview-extended-formats.md`（基础预览框架）、`preview-avalonia-opportunities.md`（Avalonia 下的预览能力变化）
> **前置**: 无（WPF 下可直接实施，Avalonia 迁移时 UI 控件名适配即可，Core 层逻辑全部复用）

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

| 格式 | 快速预览 | 渐进加载 | 完整加载 | 依赖 |
|:----:|---------|---------|---------|:----:|
| **图片** (JPG/PNG/BMP/WebP/ICO) | `DecodePixelWidth`=预览窗格尺寸（如 400px），秒出 | 后台解码全尺寸，完成后替换 Source | 全尺寸一次性解码 | 🟢 |
| **GIF** | 只解码第一帧，显示静止图 | 后台加载完整动画帧，替换为动画源 | 完整动画播放 | 🟢 |
| **文本** (TXT/Code/LOG) | 读前 N 字节（如 1024），末尾加 `…` 标记 | 后台 `FileStream` 继续读取剩余，追加到 `TextBox` | 全文加载 | 🟢 |
| **CSV** | 读前 N 行 × 检测到的列数 | 后台读更多行追加到 DataGrid | 全部行加载 | 🟢 |
| **字幕** (SRT/ASS/VTT) | 读前 N 条，取时间范围 | 后台读完剩余，追加列表 | 全部条目 | 🟢 |
| **PDF** | PdfPig 渲染第一页（小尺寸 400px） | 后台依次渲染第 2、3、4…页，翻页即时显示 | 全部页渲染完成再展示 | 🟢 |
| **字体** (TTF/OTF/WOFF) | 读 name table + glyph count，渲染小样本 "AaBb" | 后台加载完整字体，可支持更丰富的字形展示 | 完整字体渲染 | 🟢 |
| **DBF** | 读前 N 条记录的字段值 | 后台读更多记录 | 全部记录 | 🟢 |
| **SVG** | `Avalonia.Svg.Skia` 原生渲染（本身就快，不需要分段） | 同快速（无需渐进） | 同快速 | 🟡 |

### 1.2 元数据→内容（快速只展示元数据，渐进加载完整内容）

| 格式 | 快速预览 | 渐进加载 | 完整加载 | 依赖 |
|:----:|---------|---------|---------|:----:|
| **SQLite** | 表名+行数 | 后台加载表内容到 DataGrid | 所有表数据 | 🟢 |
| **Office (DOCX)** | XmlReader 流式读前 5 个 `<w:p>`，显示前 5 段 | 后台继续读剩下段落，追加 | 全文 | 🟢 |
| **Office (XLSX)** | 读前 20 行数据 | 后台读更多行，DataGrid 追加 | 所有行 | 🟢 |
| **Office (PPTX)** | 读 slide1.xml，显示第一页文本 | 后台读 slide2、3…，翻页即时 | 所有页 | 🟢 |
| **Markdown** | 纯文本取前 N 字符显示 | 后台 Markdig 解析 → Avalonia 原生控件渲染全文 | 完整渲染 | 🟡 |
| **EPUB** | 标题/作者 + 封面小图 | 后台加载全文内容 | 完整显示 | 🟢 |
| **HTML** | HtmlAgilityPack 取纯文本前 N 字符 | ❌ 无 WebView，无法渲染富 HTML | ❌ 仅纯文本预览 | 🟢 |

### 1.3 快速 = 完整（元数据格式，不需要渐进）

这些格式本身就是元数据，读到头就是全部：

| 格式 | 快速预览 | 渐进/完整 | 依赖 |
|:----:|---------|:---------:|:----:|
| **PE** (exe/dll/sys) | 公司/版本/架构/子系统 | 🟢 完全一致 | 🟢 |
| **Torrent** | InfoHash/文件列表/Magnet | 🟢 完全一致 | 🟢 |
| **ISO** | 卷标/格式/大小 | 🟢 完全一致 | 🟢 |
| **LNK** | 目标路径/参数 | 🟢 完全一致 | 🟢 |
| **STL** | 三角面数/格式(binary/ASCII) | 🟢 完全一致 | 🟢 |
| **GZ/BZ2/XZ/Zstd** | 原始文件名/压缩方法/原大小 | 🟢 完全一致 | 🟢 |
| **证书** (CER/PFX) | 颁发者/主题/有效期 | 🟢 完全一致 | 🟢 |
| **音频元数据** (WAV/FLAC/MP3) | 采样率/时长/编码/标签 | 🟢 完全一致（播放另需插件） | 🟢 |
| **视频元数据** (MP4/MKV/AVI) | 分辨率/时长/编码 | 🟢 完全一致（播放另需插件） | 🟢 |
| **VHD/VMDK** | 磁盘容量/格式版本 | 🟢 完全一致 | 🟢 |
| **DICOM** | 患者信息/影像参数 | 🟢 完全一致 | 🟢 |
| **MOBI/AZW3** | 书名/作者/出版方 | 🟢 完全一致 | 🟢 |
| **DXF/STEP/FBX** | 实体数/面数 | 🟢 完全一致 | 🟢 |

### 1.4 不支持或特殊处理

| 格式 | 原因 |
|:----:|------|
| **AI** (Illustrator) | 必须安装 Ghostscript 才能做任何渲染。无 GS 时降级为第三类（仅文件基本属性） |
| **音视频播放** | 播放的是插件功能（LibVLC），跟三模式正交。元数据展示走第三类（始终可用） |
| **HTML** (富文本) | 无 WebView 后无法渲染，仅支持纯文本预览 |

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

`AppSettings` 新增：

```csharp
public class AppSettings
{
    // 预览模式: 0=完整加载, 1=快速预览, 2=渐进式加载（默认）
    public int PreviewMode { get; set; } = 2;

    // 快速预览参数（各格式独立控制）
    public int QuickPreviewImageMaxDimension { get; set; } = 400;  // 图片最大解码尺寸
    public int QuickPreviewTextMaxBytes { get; set; } = 2048;      // 文本最大读取字节
    public int QuickPreviewCsvMaxRows { get; set; } = 50;          // CSV 最大预览行数
    public int QuickPreviewPdfPageCount { get; set; } = 1;         // PDF 预览页数
    public int QuickPreviewOfficeMaxParagraphs { get; set; } = 10; // Office 最大段落数
    public int QuickPreviewSrtMaxEntries { get; set; } = 20;       // 字幕最大条目数

    // 渐进加载缓冲
    public int ProgressiveLoadBatchSize { get; set; } = 4096;      // 文本渐进每批字节数
    public int ProgressiveLoadIntervalMs { get; set; } = 50;       // 批次间隔（避免 UI 卡顿）
}
```

### 2.4 模式持久化

- 当前模式存储在 `AppSettings.PreviewMode`，跨 session 保持
- 切换模式时更新设置并保存

---

## 3. 技术架构

### 3.1 预览流程改造

当前（完整模式）：

```
ShowPreviewAsync(item) → 提取完整文件 → ShowXxxPreview → 加载全部内容 → 显示
```

改造后：

```
ShowPreviewAsync(item, mode):
  │
  ├── 提取文件到 temp（此步骤不变，仍可配合 Phase 5 元数据优先提取）
  │
  ├── 根据 mode 选择加载策略：
  │     ├── 模式 1 完整: ShowXxxFull(item) → 加载全部 → 显示
  │     ├── 模式 2 快速: ShowXxxQuick(item) → 加载最少数据 → 显示 + 显示"查看完整"按钮
  │     └── 模式 3 渐进: 
  │           ├── ShowXxxQuick(item) → 显示（同模式 2）
  │           └── _progressiveCts.Token → 后台 ShowXxxFull → 完成后通知 UI 替换
  │
  └── 用户切换文件 / 切换模式 → 取消 _progressiveCts
```

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

各格式的 `ShowXxxPreviewAsync` 方法实现此接口，或直接在 `MainWindow.Preview.cs` 中用 switch 分支。

### 3.3 渐进加载管理器

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

### 3.4 各格式关键改造点

#### 图片

WPF 已有 `DecodePixelWidth`，改动最小：

```csharp
// 当前：写死 1920
bmp.DecodePixelWidth = 1920;

// 改造后：根据模式动态
if (mode == PreviewMode.Quick || mode == PreviewMode.Progressive)
    bmp.DecodePixelWidth = AppSettings.Instance.QuickPreviewImageMaxDimension;  // 400
else
    bmp.DecodePixelWidth = 0;  // 全尺寸
```

渐进模式下，后台用 `0`（全尺寸）重新解码，完成后替换 `PreviewImage.Source`。

#### 文本

```csharp
// 当前：读全文
string content = File.ReadAllText(filePath, encoding);

// 改造后：根据模式
if (mode == PreviewMode.Quick)
{
    // 只读前 N 字节
    var buffer = new byte[QuickPreviewTextMaxBytes];
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
PreviewImage.Source = bitmap.ToBitmap();

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
| **P0** | 模式切换 UI + `AppSettings` + `ProgressiveLoadManager` 框架 | ~3h | 先搭好架子，所有格式共用的基础设施 |
| **P1** | 图片快速预览 + 渐进加载 | ~2h | 改动最小（改 `DecodePixelWidth`），拿来验证框架 |
| **P1** | 文本快速预览 + 渐进加载 | ~3h | 核心体验提升，用户感知最强 |
| **P2** | CSV 快速预览 + 渐进加载 | ~1h | 同文本模式 |
| **P2** | PDF 第一页快速 + 逐页渐进 | ~4h | PdfPig 天然支持，改动独立 |
| **P2** | Office 流式加载 | ~4h | XmlReader 改造 |
| **P3** | GIF 快速 + 渐进 | ~2h | 第一帧 → 完整动画 |
| **P3** | 字幕、DBF、字体等快速预览 | ~3h | 逐个格式适配 |
| **P4** | SQLite、EPUB 等快速预览 | ~2h | 纯 C# 解析，改动小 |
| **P4** | Markdown 渐进加载（Avalonia 原生控件渲染） | ~3h | 依赖 Avalonia 迁移 |

**总计**：~27h

---

## 5. WPF 先行 / Avalonia 迁移影响

| 组件 | WPF 实现 | Avalonia 迁移 |
|------|---------|-------------|
| `ProgressiveLoadManager` | 纯 C#，直接用 | 无改动 |
| `AppSettings` 新属性 | 直接用 | 无改动 |
| 文本渐进 `TextBox` | WPF `TextBox.Text += chunk` | 改为 Avalonia `TextBox.Text += chunk` |
| 图片渐进 `DecodePixelWidth` | WPF `BitmapImage` 属性 | 改为 Avalonia `Bitmap` 手动创建 |
| PDF 渲染 | PdfPig + SkiaSharp → WPF BitmapSource | PdfPig + SkiaSharp → Avalonia Bitmap |
| 工具栏三段切换 | WPF 控件 | 改为 Avalonia 等价控件 |
| 渐进管理器取消 | `CancellationTokenSource` | 相同 |

**核心逻辑层（ProgressiveLoadManager、各格式数据读取策略）全部用纯 C# 实现，与 UI 框架无关。** WPF 下可以先行实施，Avalonia 迁移时只需重写 UI 渲染层（模式切换控件、TextBox/Image 等控件的赋值方式）。

---

## 6. Definition of Done

- [ ] `ProgressiveLoadManager` 实现（取消/替换/批处理）
- [ ] `AppSettings` 新增预览模式 + 各格式快速参数
- [ ] 工具栏三段式模式切换 UI
- [ ] 模式指示灯 + 提示条 UI
- [ ] 图片快速预览（动态 `DecodePixelWidth`） + 渐进加载
- [ ] 文本快速预览（前 N 字节）+ 渐进加载（分段追加）
- [ ] CSV 快速预览（前 N 行）+ 渐进加载
- [ ] PDF 快速预览（第一页）+ 渐进加载（逐页渲染）
- [ ] GIF 快速预览（第一帧）+ 渐进加载
- [ ] Office (DOCX/XLSX/PPTX) 流式预览
- [ ] 字幕/DBF/字体快速预览
- [ ] SQLite/EPUB 快速预览
- [ ] 格式不支持快速预览时，模式切换按钮禁用或灰显
- [ ] 切换文件/模式时后台加载正确取消
- [ ] `dotnet build` 通过

---

## 7. 后续扩展：文件列表缩略图模式

### 7.1 概述

快速预览模式的"降级解码"能力天然可扩展到文件列表：将每行条目左侧的文件类型图标替换为文件内容的小预览图。快速预览是"点一个看一个"，缩略图是"一屏同时看几十个"。

```
┌─────────────────────────────────────────┐
│ ☑ 显示缩略图  [尺寸: 32·48·64·96·128]   │ ← 工具栏切换
├─────────────────────────────────────────┤
│  🖼️  photo.jpg            1.2 MB   [###]│ ← 64px 缩略图
│  🖼️  screenshot.png       340 KB   [###]│
│  📄  readme.txt            12 KB   [   ]│ ← 文本没有，用图标
│  🖼️  panorama.hdr          8 MB    [###]│ ← HDR tone-mapped 小图
│  📄  main.cs               3 KB    [   ]│ ← 代码没有，用图标
│  🖼️  document.pdf         2.5 MB   [###]│ ← PDF 第一页小图
└─────────────────────────────────────────┘
```

### 7.2 与快速预览的关系

| 能力 | 快速预览（预览面板） | 缩略图（文件列表） |
|:----:|:------------------:|:-----------------:|
| 解码尺寸 | 400px | 32~128px（可配置） |
| 同时处理 | 1 个 | 视口内 N 个 |
| 优先级 | 用户点击 → 即时 | 可见项先加载，滚动按需 |
| 延迟要求 | < 200ms | < 50ms（滚动不卡顿） |
| 缓存 | 不需要（切文件就清） | **必须缓存** |
| 范围 | 全部格式 | 有视觉内容的格式（图片/PDF/SVG/PSD/HDR） |
| 兜底 | — | 无缩略图的项保持 SystemIconHelper 图标 |

### 7.3 基础设施复用

快速预览完成的以下组件可直接用于缩略图：

1. **降级解码策略** — `DecodePixelWidth` 动态控制，缩略图只需传更小的尺寸（64px）
2. **取消机制** — `ProgressiveLoadManager.Cancel()` 用于取消滚动后不可见项的加载
3. **Magick.NET 插件** — 通过已有 `IPreviewProvider` 解码 PSD/HDR/EXR 为小图，零额外集成

### 7.4 新增组件

#### 缩略图缓存

```csharp
public class ThumbnailCache : IDisposable
{
    private readonly LRUCache<string, Bitmap> _cache;

    // LRU 缓存，上限 200 项（约 3.2MB 以 64×64×4 计算）
    public ThumbnailCache(int maxEntries = 200, int defaultSize = 64);

    // 异步获取：缓存命中直接返回，未命中则后台解码
    public Task<Bitmap?> GetOrCreateAsync(
        string archivePath, string entryName, string extension,
        int size, CancellationToken ct);

    // 主动清除（关闭压缩包时）
    public void Clear();
}
```

#### 文件列表模式切换

`MainWindow.xaml` 中现有 `DataGrid` 的图标列加入模式切换：

```csharp
private bool _showThumbnails;
public bool ShowThumbnails
{
    get => _showThumbnails;
    set
    {
        _showThumbnails = value;
        if (value)
            SwitchToThumbnailMode();
        else
            SwitchToIconMode();
        // 保存设置
        AppSettings.Instance.ShowFileListThumbnails = value;
    }
}
```

图标列模板根据模式切换：

```xml
<!-- 缩略图模式 -->
<Image Width="{Binding ThumbnailSize}" Height="{Binding ThumbnailSize}"
       Source="{Binding Thumbnail, TargetNullValue={StaticResource DefaultIcon}}" />

<!-- 图标模式（当前）-->
<Image Width="16" Height="16" Source="{Binding Icon}" />
```

#### 虚拟化滚动加载

- 监听 `DataGrid` 滚动事件，计算视口内可见行
- 为可见行发起 `ThumbnailCache.GetOrCreateAsync`
- 滚动离开视口的行，`Cancel()` 其加载任务
- 利用 `INotifyPropertyChanged` 的 `Thumbnail` 属性绑定

### 7.5 受益格式

| 格式 | 缩略图内容 | 数据来源 |
|:----:|-----------|---------|
| **图片** (JPG/PNG/GIF/WebP/BMP/ICO) | 缩小后的图像 | `DecodePixelWidth` 降到缩略图尺寸 |
| **PSD/PSB** | 合成后的展平图 | Magick.NET 插件 |
| **HDR** | tone-mapped 小图 | Magick.NET 插件 |
| **EXR/TIFF/TGA** | 解码图 | Magick.NET 插件 |
| **PDF** | 第一页渲染 64px | PdfPig + SkiaSharp |
| **SVG** | 渲染 64px | `Avalonia.Svg.Skia` |
| **GIF** | 第一帧 | 同图片解码 |

**约 70% 的文件将拥有视觉缩略图**，其余保持系统图标。相比现有方案（全部文件只有系统图标），体验提升明显。

### 7.6 预估

| 工作项 | 预估 | 前置 |
|:------|:----:|:----:|
| `ThumbnailCache` 实现 | ~2h | 快速预览（降级解码） |
| 文件列表模式切换 UI | ~2h | — |
| DataGrid 列模板 + Virtualization | ~3h | — |
| 图片类缩略图生成 | ~1h | 快速预览完成 |
| PDF/SVG 缩略图 | ~2h | 快速预览完成 |
| Magick.NET 缩略图（PSD/HDR） | ~1h | Magick.NET 插件完成 |
| 滚动加载优先级 + 取消 | ~2h | — |
| **合计** | **~13h** | 需快速预览先行 |

### 7.7 与快速预览的集成点

```
快速预览完成                                 缩略图模式
──────────────                               ──────────
DecodePixelWidth=400          ──复用──▶    DecodePixelWidth=64
ProgressiveLoadManager.Cancel ──复用──▶    滚动取消不可见项
IPreviewProvider 接口         ──复用──▶    缩略图解码走同接口
Magick.NET 插件               ──复用──▶    PSD/HDR 缩略图
第一阶段 ~27h                               第二阶段 +~13h
```
