# 嵌入缩略图预览 (Embedded Thumbnail Preview)

> **状态**: 📋 待实施 | **阶段**: [⬜⬜⬜⬜] (0/4)

## TL;DR

在现有预览系统中增加从文件自身提取嵌入缩略图的功能。采用两层策略：**MetadataExtractor** 提取相机 RAW 格式的内嵌 JPEG 缩略图，**Windows Shell API**（`IShellItemImageFactory`）作为通用兜底，覆盖 CAD、PSD、TIFF 等 Windows 能显示缩略图的任何格式。提取的缩略图直接显示在现有的 `PreviewImage` 控件中。

> **依赖**: 无（独立于其他预览计划）
> **后续扩展**: 本计划完成后可考虑将缩略图显示扩展到文件列表视图（另见结尾章节）

---

## 任务总览

- [ ] **Task 1: 数据模型扩展** — `FileFormatInfo.cs` 新增 `ThumbnailData` 通用字段 + RAW 格式枚举
- [ ] **Task 2: 缩略图提取引擎** — `EmbeddedThumbnailExtractor` 实现 MetadataExtractor + Shell API 两层策略
- [ ] **Task 3: 预览管道集成** — 新增 `ThumbnailExtensions` + `MetadataOnlyExtensions` + dispatch 分支
- [ ] **Task 4: 预览展示方法** — `ShowEmbeddedThumbnailPreview` 提取 → 解码 → 显示到 `PreviewImage`

---

## Task 1: 数据模型扩展

**Files:**
- Modify: `src/MantisZip.Core/Utils/FileFormatInfo.cs`

### 1.1 新增 `ThumbnailData` 字段

在 `CoverArtData` 之后新增一个通用的缩略图数据字段：

```csharp
// ── 嵌入缩略图 ──
/// <summary>
/// 从文件中提取的嵌入缩略图原始字节（JPEG/PNG/BMP）。
/// 不同于 CoverArtData（仅限 MP3 APIC 帧），此字段来自任意格式的嵌入缩略图
/// （RAW/PSD/CAD/Office 等），由 EmbeddedThumbnailExtractor 填充。
/// </summary>
public byte[]? ThumbnailData { get; set; }
```

### 1.2 新增 RAW 格式枚举值

在 `FileFormat` 枚举中新增相机 RAW 格式，位于现有 `Jpeg` 条目之后：

```csharp
// 图像
Jpeg, Png, Gif, Bmp, WebP, Ico, Tga, Hdr, Exr, Svg,
// 相机 RAW
Cr2, Nef, Dng, Arw, Orf, Raf, Pef, Rw2,
```

> 注：RAW 格式缩略图走 MetadataExtractor，不在此枚举上做魔数检测，此枚举仅用于未来扩展时的格式标识。

### 自检清单

- [ ] `ThumbnailData` 字段类型为 `byte[]?`，位于 `CoverArtData` 之后
- [ ] RAW 格式枚举值不与现有值冲突
- [ ] 枚举值命名符合 PascalCase 风格

---

## Task 2: 缩略图提取引擎

**Files:**
- Create: `src/MantisZip.Core/Utils/EmbeddedThumbnailExtractor.cs`
- Modify: `src/MantisZip.Core/MantisZip.Core.csproj`（添加 MetadataExtractor）

### 2.1 添加 NuGet 依赖

```bash
dotnet add src\MantisZip.Core\MantisZip.Core.csproj package MetadataExtractor
```

`MetadataExtractor` v2.9.3+，Apache 2.0 许可，纯托管代码，无原生依赖。

### 2.2 实现 `EmbeddedThumbnailExtractor`

**全文件结构**：

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace MantisZip.Core.Utils;

/// <summary>
/// 从文件中提取嵌入缩略图的静态工具类。
/// 两层策略：MetadataExtractor（相机 RAW）→ Shell API（通用兜底）。
/// </summary>
public static class EmbeddedThumbnailExtractor
{
    /// <summary>
    /// 尝试从指定文件中提取嵌入缩略图。
    /// 优先使用 MetadataExtractor（适用于 RAW 格式的内嵌 JPEG），
    /// 失败后回退到 Windows Shell API。
    /// </summary>
    /// <param name="filePath">磁盘上的文件路径</param>
    /// <param name="thumbnailData">输出：缩略图的 JPEG/PNG/BMP 原始字节</param>
    /// <returns>是否成功提取</returns>
    public static bool TryExtractThumbnail(string filePath, out byte[]? thumbnailData)
    {
        // Tier 1: MetadataExtractor（RAW 格式内嵌 JPEG）
        if (TryExtractRawThumbnail(filePath, out thumbnailData))
            return true;

        // Tier 2: Windows Shell API（通用兜底）
        return TryExtractShellThumbnail(filePath, 256, out thumbnailData);
    }

    /// <summary>
    /// 使用 MetadataExtractor 从相机 RAW 文件中提取内嵌 JPEG 缩略图。
    /// 处理 CR2/NEF/DNG/ARW/ORF/RAF/PEF/RW2 等格式。
    /// </summary>
    private static bool TryExtractRawThumbnail(string filePath, out byte[]? thumbnailData)
    {
        thumbnailData = null;
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var thumbDir = directories
                .OfType<ExifThumbnailDirectory>()
                .FirstOrDefault();

            if (thumbDir != null && thumbDir.TryGetThumbnailBytes(out var bytes))
            {
                if (bytes != null && bytes.Length > 0)
                {
                    thumbnailData = bytes;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            CoreLog.Trace("EmbeddedThumbnailExtractor: MetadataExtractor failed for {0}: {1}",
                filePath, ex.Message);
        }
        return false;
    }

    /// <summary>
    /// 使用 Windows Shell API（IShellItemImageFactory）获取文件缩略图。
    /// 适用于任何 Windows 能显示缩略图的格式（PSD/CAD/TIFF/视频等）。
    /// 必须在后台线程调用（COM STA 限制）。
    /// </summary>
    /// <param name="filePath">磁盘上的文件路径</param>
    /// <param name="maxSize">缩略图最大边长（像素），默认 256</param>
    /// <param name="thumbnailData">输出：缩略图的 PNG 原始字节</param>
    internal static bool TryExtractShellThumbnail(string filePath, int maxSize, out byte[]? thumbnailData)
    {
        thumbnailData = null;
        try
        {
            var hBitmap = IntPtr.Zero;
            try
            {
                hBitmap = GetShellThumbnailBitmap(filePath, maxSize);
                if (hBitmap == IntPtr.Zero)
                    return false;

                // HBITMAP → BitmapSource → PNG byte[]
                using var bitmapStream = new MemoryStream();
                var source = System.Windows.Interop.Imaging
                    .CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        System.Windows.Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
                encoder.Save(bitmapStream);
                thumbnailData = bitmapStream.ToArray();
                return true;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);
            }
        }
        catch (Exception ex)
        {
            CoreLog.Trace("EmbeddedThumbnailExtractor: Shell API failed for {0}: {1}",
                filePath, ex.Message);
        }
        return false;
    }

    /// <summary>调用 IShellItemImageFactory 获取 HBITMAP。</summary>
    private static IntPtr GetShellThumbnailBitmap(string filePath, int maxSize)
    {
        var shellItem2 = (IShellItemImageFactory)SHCreateItemFromParsingName(
            filePath, IntPtr.Zero, typeof(IShellItemImageFactory).GUID);
        var size = new SIZE(maxSize, maxSize);
        shellItem2.GetImage(ref size, SIIGBF.SIIGBF_THUMBNAILONLY | SIIGBF.SIIGBF_MEMORYONLY, out var hBitmap);
        return hBitmap;
    }
}

#region Win32 P/Invoke

[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
private interface IShellItemImageFactory
{
    void GetImage([In] ref SIZE size, [In] SIIGBF flags, [Out] out IntPtr phbm);
}

[StructLayout(LayoutKind.Sequential)]
private struct SIZE
{
    public int cx;
    public int cy;
    public SIZE(int width, int height) { cx = width; cy = height; }
}

[Flags]
private enum SIIGBF : uint
{
    SIIGBF_RESIZETOFIT = 0x00,
    SIIGBF_BIGGERSIZEOK = 0x01,
    SIIGBF_MEMORYONLY = 0x02,
    SIIGBF_ICONONLY = 0x04,
    SIIGBF_THUMBNAILONLY = 0x08,
    SIIGBF_INCACHEONLY = 0x10,
}

[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
private static extern IShellItemImageFactory SHCreateItemFromParsingName(
    [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
    [In] IntPtr pbc,
    [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid);

[DllImport("gdi32.dll", SetLastError = true)]
private static extern bool DeleteObject(IntPtr hObject);

#endregion
```

> ⚠️ 注意：
> - `SHCreateItemFromParsingName` 使用 `PreserveSig = false`（抛出 COMException 而非返回 HRESULT）
> - 所有 Shell API 调用必须从后台线程进行（`Task.Run`），不允许在 WPF UI 线程调用
> - `DeleteObject` 必须在 `finally` 中保证释放，防止 GDI 句柄泄漏

### 自检清单

- [ ] `MetadataExtractor` NuGet 包已添加到 Core 项目
- [ ] P/Invoke 签名正确（`PreserveSig = false`）
- [ ] `DeleteObject` 在 `finally` 块中调用
- [ ] 所有 `try-catch` 中有 `CoreLog.Trace` 记录失败（不抛出到上层）
- [ ] 方法为 `internal` 而非 `public`（UI 项目通过 InternalsVisibleTo 或 Core 内部调用）

---

## Task 3: 预览管道集成

**Files:**
- Modify: `src/MantisZip.UI/MainWindow/Preview/MainWindow.Preview.cs`

### 3.1 新增 `ThumbnailExtensions` 集合

在 `SvgExtensions`（line 128-131）之后添加：

```csharp
/// <summary>支持嵌入缩略图提取的格式扩展名集合。</summary>
private static readonly HashSet<string> ThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    // 相机 RAW（走 MetadataExtractor）
    ".cr2", ".cr3",
    ".nef", ".nrw",
    ".dng",
    ".arw", ".srf", ".sr2",
    ".orf",
    ".raf",
    ".pef",
    ".rw2",
    // 以下格式走 Shell API（需系统有对应解码器）
    ".psd",
    ".dwg", ".dxf",
    ".ai",
    ".eps",
    ".heic", ".heif",
    ".webp",
    ".tiff", ".tif",
    ".ico",  // 已有 ICO 画廊预览，Shell API 可提供缩略图
};
```

### 3.2 添加至 `MetadataOnlyExtensions`

将 `ThumbnailExtensions` 中的格式加入 `MetadataOnlyExtensions`（line 144-153），因为缩略图提取只读取文件头，不消耗完整内容：

```csharp
private static readonly HashSet<string> MetadataOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    // ... 现有条目 ...
    // 嵌入缩略图格式（只读头）
    ".cr2", ".cr3", ".nef", ".nrw", ".dng",
    ".arw", ".srf", ".sr2", ".orf", ".raf", ".pef", ".rw2",
    ".psd",
    ".dwg", ".dxf",
    ".ai", ".eps",
    ".heic", ".heif",
    ".webp",
    ".tiff", ".tif",
};
```

### 3.3 添加 dispatch 分支

在 `ShowPreviewAsync` 中，在 `ImageExtensions` 分支之前（或现有 else 分支之前）插入缩略图分发。建议放在 `ImageExtensions` 之后、`ImageExtensions` 已有的 `.ico`/`.webp`/`.tiff` 会优先走图片预览，不走缩略图路径。

最合适的位置：在 `else if (CsvExtensions.Contains(ext))` 之前，即第 341 行附近。但要注意 `.ico`/`.webp`/`.tiff` 已经在 `ImageExtensions` 中优先匹配了，不会走到缩略图分支。

实际插入位置：在 `else if (SvgExtensions.Contains(ext))` 之后（约第 420 行），`else if (VideoExtensions.Contains(ext))` 之前：

```csharp
else if (ThumbnailExtensions.Contains(ext))
{
    var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
    await ShowEmbeddedThumbnailPreview(tempFile, item, ct);
}
```

但要注意被 `ImageExtensions` 包含的格式（`.ico`, `.webp`, `.tiff`）已经提前被 `ShowImagePreviewAsync` 处理了。

### 自检清单

- [ ] `ThumbnailExtensions` 集合与 `ImageExtensions` 不重复（`.ico`/`.webp`/`.tiff` 已在 ImageExtensions 中优先匹配）
- [ ] dispatch 分支的插入位置确保 `.ico` 等格式不会被缩略图路径劫持
- [ ] 新条目已加入 `MetadataOnlyExtensions`

---

## Task 4: 预览展示方法

**Files:**
- Modify: `src/MantisZip.UI/MainWindow/Preview/MainWindow.Preview.Metadata.cs`

### 4.1 新增 `ShowEmbeddedThumbnailPreview` 方法

在文件末尾（`ShowVideoPreview` 之后）添加：

```csharp
// ── 嵌入缩略图 ──

/// <summary>
/// 提取文件的嵌入缩略图并显示到 PreviewImage 控件。
/// 使用 EmbeddedThumbnailExtractor 两层策略：
///   Tier 1: MetadataExtractor（相机 RAW 内嵌 JPEG）
///   Tier 2: Windows Shell API（通用兜底）
/// 必须在后台线程调用 Shell API，因此用 Task.Run 包裹。
/// </summary>
private async Task ShowEmbeddedThumbnailPreview(
    string filePath, ArchiveItem item, CancellationToken ct)
{
    try
    {
        // 缩略图提取可能在后台线程进行（Shell API 要求）
        var thumbnailBytes = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            EmbeddedThumbnailExtractor.TryExtractThumbnail(filePath, out var bytes);
            return bytes;
        }, ct);

        ct.ThrowIfCancellationRequested();

        if (thumbnailBytes == null || thumbnailBytes.Length == 0)
        {
            ShowUnsupportedPreview(item, L.TF(L.Preview_Failed, 
                "嵌入式缩略图提取失败"));
            return;
        }

        HideAllPreviewControls();

        // byte[] → BitmapImage（使用 DecodePixelWidth 限制内存）
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(thumbnailBytes);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 256;  // 缩略图不需要大尺寸
        bitmap.EndInit();
        bitmap.Freeze();  // 跨线程安全

        PreviewImage.Source = bitmap;
        PreviewImageScroll.Visibility = Visibility.Visible;
        ApplyZoom(ZoomMode.FitWindow);

        // 基本信息
        SetPreviewInfo(item);

        var ext = Path.GetExtension(item.Name).ToUpperInvariant();
        PreviewHeader.Text = L.TF(L.Preview_ImageHeader, $"{ext} 缩略图 - {item.NameDisplay ?? item.Name}");

        // 工具栏：缩放控制
        SetToolbar(
            new[]
            {
                new ToolbarButton { Text = "※", Tooltip = L.T(L.Preview_ZoomFit), 
                    OnClick = () => ApplyZoom(ZoomMode.FitWindow) },
                new ToolbarButton { Text = "1:1", Tooltip = L.T(L.Preview_Zoom100), 
                    OnClick = () => ApplyZoom(ZoomMode.Zoom100) },
                new ToolbarButton { Text = "┋", Tooltip = L.T(L.Preview_ZoomFitWidth), 
                    OnClick = () => ApplyZoom(ZoomMode.FitWidth) },
            },
            Array.Empty<ToolbarButton>());

        ShowPreviewPanel();
    }
    catch (OperationCanceledException)
    {
        // 用户切换文件，静默取消
    }
    catch (Exception ex)
    {
        ShowUnsupportedPreview(item, L.TF(L.Preview_Failed, ex.Message));
    }
}
```

### 4.2 确认 L 本地化键

检查 `MantisZip.UI/Localization/` 中是否存在 `L.Preview_ImageHeader`。若存在，则直接使用；若不存在，使用字符串 `"{0} 预览"` 或创建新的本地化键。

实际上从 `MainWindow.Preview.Image.cs` line 74 可以看到：
```csharp
PreviewHeader.Text = L.TF(L.Preview_ImageHeader, Path.GetFileName(filePath));
```
所以 `L.Preview_ImageHeader` 已存在，可直接使用。

### 自检清单

- [ ] `Task.Run` 包裹缩略图提取（Shell API 需要非 UI 线程）
- [ ] `CancellationToken` 在后台线程中正确传递
- [ ] `bitmap.Freeze()` 被调用（WPF 跨线程要求）
- [ ] 提取失败时回退到 `ShowUnsupportedPreview`
- [ ] 工具栏至少包含三种缩放模式

---

## 完成后的扩展思考

本计划完成后，可以考虑将缩略图显示扩展到**文件列表视图**：

- 当前 `FileListGrid` 的 `IconSource` 列仅显示 16×16 系统图标（来自 `SystemIconHelper`）
- 缩略图列表模式需要：`ConcurrentDictionary` 缓存、后台队列加载、可能的视图切换（List/Details/Icons）
- 对于压缩包内的文件，每个缩略图都需要先提取到临时目录，I/O 开销显著大于系统图标
- 建议作为独立计划实现，并考虑使用 `VirtualizingStackPanel` 的 `IsVirtualizing=True` 带来的容器复用特性

> 缩略图列表模式预计工作量：3-5 天（含缓存、后台线程加载、UI 视图切换）

---

## 任务依赖图

```
Task 1 (FileFormatInfo) ─→ 无依赖
Task 2 (Extractor) ─────→ 无依赖（可并行）
Task 3 (管道集成) ──────→ 依赖 Task 2（需要扩展名集合）
Task 4 (展示方法) ──────→ 依赖 Task 2（需要提取引擎）+ Task 1（可选 ThumbnailData）
```

Task 1 和 Task 2 可并行实施。Task 3 和 Task 4 需在上述完成后实施。
