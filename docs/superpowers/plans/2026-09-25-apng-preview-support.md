# APNG 动画预览支持 - 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为预览系统添加 APNG (Animated PNG) 动画支持，复用现有 `PreviewType.AnimatedImage` 管线（GIF + Animated WebP 已统一），仅扩展格式识别层区分 PNG/APNG。

**Architecture:** 核心策略是"最小变更、最大复用"。APNG 与 PNG 共享魔数签名，通过扫描 `acTL` chunk 区分。识别为 `FileFormat.Apng` 后映射到现有 `PreviewType.AnimatedImage`，自动复用 `ShowGif` 通用播放管线（SKCodec 统一解码、DispatcherTimer 播放、工具栏能力注册表）。无需修改 PreviewViewModel、PreviewCapabilities、播放逻辑。

**Tech Stack:** .NET 10, SkiaSharp 3.119.4, xUnit, Avalonia UI

---

### Task 1: FileFormatInfo.cs - 新增 Apng 枚举值

**Files:**
- Modify: `src/MantisZip.Core/Utils/FileFormatInfo.cs:134-172`

- [ ] **Step 1: 读取当前枚举定位插入位置**
```bash
# 确认 FileFormat 枚举当前结构
cat src/MantisZip.Core/Utils/FileFormatInfo.cs | sed -n '130,175p'
```

- [ ] **Step 2: 在 Png 后插入 Apng 枚举值**
```csharp
// src/MantisZip.Core/Utils/FileFormatInfo.cs
public enum FileFormat
{
    Unknown,
    // 图像
    Jpeg, Png, Gif, Bmp, WebP, Ico, Tga, Hdr, Exr, Svg,
    Apng,          // NEW: APNG 动画
    // 音频
    Wav, Flac, Mp3,
    // ... 其余保持不变
}
```

- [ ] **Step 3: 验证编译通过**
```bash
dotnet build src/MantisZip.Core/MantisZip.Core.csproj
```
Expected: 编译成功，无错误

- [ ] **Step 4: 提交**
```bash
git add src/MantisZip.Core/Utils/FileFormatInfo.cs
git commit -m "feat(core): 新增 FileFormat.Apng 枚举值"
```

---

### Task 2: FileFormatDetector.cs - 新增 APNG 魔数检测

**Files:**
- Modify: `src/MantisZip.Core/Utils/FileFormatDetector.cs:23-100`
- Test: `tests/MantisZip.Tests/Utils/FileFormatDetectorTests.cs`

- [ ] **Step 1: 读取 PNG 检测分支位置**
```bash
# 确认 PNG 检测代码位置（约第 25-32 行）
cat src/MantisZip.Core/Utils/FileFormatDetector.cs | sed -n '20,50p'
```

- [ ] **Step 2: 新增 ScanForActlChunk 私有方法（在类内部）**
```csharp
// src/MantisZip.Core/Utils/FileFormatDetector.cs - 在类内部适当位置（如 Detect 方法后）

/// <summary>
/// 扫描 PNG 数据中是否包含 acTL (Animation Control) chunk，判定为 APNG。
/// </summary>
/// <param name="head">文件头部字节数组</param>
/// <param name="length">有效长度</param>
/// <returns>true=APNG, false=静态 PNG</returns>
private static bool ScanForActlChunk(byte[] head, int length)
{
    if (length < 33) return false; // PNG签名8 + IHDR最小25 = 33

    // 跳过 PNG 签名 (8 bytes)
    int offset = 8;

    // 遍历 chunks，最多扫描 64KB 或 length
    int maxScan = Math.Min(length, 65536);

    while (offset + 8 <= maxScan) // 至少需要 Length(4) + Type(4)
    {
        // 读取 chunk 长度 (大端序)
        int chunkLength = (head[offset] << 24) | (head[offset + 1] << 16) | (head[offset + 2] << 8) | head[offset + 3];
        if (chunkLength < 0 || offset + 12 + chunkLength > maxScan)
            break; // 长度异常或超出扫描范围

        // 读取 chunk 类型 (4 bytes ASCII)
        int chunkType = (head[offset + 4] << 24) | (head[offset + 5] << 16) | (head[offset + 6] << 8) | head[offset + 7];

        // acTL = 0x6163544C ('a','c','T','L')
        if (chunkType == 0x6163544C)
            return true; // 发现 acTL chunk，确认为 APNG

        // IEND chunk 结束
        if (chunkType == 0x49454E44) // 'I','E','N','D'
            break;

        // 跳到下一个 chunk: Length(4) + Type(4) + Data(chunkLength) + CRC(4)
        offset += 12 + chunkLength;
    }

    return false;
}
```

- [ ] **Step 3: 在 PNG 检测分支中调用 ScanForActlChunk**
```csharp
// 修改现有 PNG 检测分支（约第 25-32 行）
// 1. PNG: 89 50 4E 47 0D 0A 1A 0A (8 bytes)
if (length >= 8 &&
    head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 &&
    head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
{
    CoreLog.Info("Detect: PNG magic matched");
    
    // NEW: 进一步检测是否为 APNG
    if (ScanForActlChunk(head, length))
    {
        CoreLog.Info("Detect: APNG magic matched (acTL chunk found)");
        return FileFormat.Apng;
    }
    
    return FileFormat.Png;
}
```

- [ ] **Step 4: 写入单元测试（测试文件可能已存在，追加测试用例）**
```csharp
// tests/MantisZip.Tests/Utils/FileFormatDetectorTests.cs - 追加在类末尾

[Fact]
public void Detect_Apng_ReturnsApng()
{
    // 构造包含 acTL chunk 的 PNG 头部
    // PNG签名 + IHDR(最小) + acTL + IEND
    var apngHead = new byte[]
    {
        // PNG 签名
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        // IHDR chunk: Length=13, Type="IHDR", Data(13), CRC(4)
        0x00, 0x00, 0x00, 0x0D,  // Length = 13
        0x49, 0x48, 0x44, 0x52,  // "IHDR"
        0x00, 0x00, 0x00, 0x01,  // Width = 1
        0x00, 0x00, 0x00, 0x01,  // Height = 1
        0x08, 0x02, 0x00, 0x00, 0x00,  // BitDepth=8, ColorType=2, Compression=0, Filter=0, Interlace=0
        0x90, 0x77, 0x53, 0xDE,  // CRC (placeholder)
        // acTL chunk: Length=8, Type="acTL", Data(8), CRC(4)
        0x00, 0x00, 0x00, 0x08,  // Length = 8
        0x61, 0x63, 0x54, 0x4C,  // "acTL"
        0x00, 0x00, 0x00, 0x0A,  // numFrames = 10
        0x00, 0x00, 0x00, 0x00,  // numPlays = 0 (infinite)
        0x00, 0x00, 0x00, 0x00,  // CRC (placeholder)
        // IEND chunk
        0x00, 0x00, 0x00, 0x00,  // Length = 0
        0x49, 0x45, 0x4E, 0x44,  // "IEND"
        0xAE, 0x42, 0x60, 0x82   // CRC
    };

    var result = FileFormatDetector.Detect(apngHead, apngHead.Length);
    Assert.Equal(FileFormat.Apng, result);
}

[Fact]
public void Detect_StaticPng_ReturnsPng()
{
    // 普通 PNG（无 acTL chunk）
    var pngHead = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00,
        0x90, 0x77, 0x53, 0xDE,
        0x00, 0x00, 0x00, 0x00,
        0x49, 0x45, 0x4E, 0x44,
        0xAE, 0x42, 0x60, 0x82
    };

    var result = FileFormatDetector.Detect(pngHead, pngHead.Length);
    Assert.Equal(FileFormat.Png, result);
}

[Fact]
public void Detect_Apng_WithPlte_BeforeActl_ReturnsApng()
{
    // APNG 可能在 IHDR 后有 PLTE，再是 acTL
    var apngHead = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        // IHDR
        0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x01,
        0x08, 0x03, 0x00, 0x00, 0x00, // ColorType=3 (indexed)
        0x00, 0x00, 0x00, 0x00,       // CRC placeholder
        // PLTE chunk (palette)
        0x00, 0x00, 0x00, 0x03,
        0x50, 0x4C, 0x54, 0x45,       // "PLTE"
        0xFF, 0x00, 0x00,             // 1 palette entry
        0x00, 0x00, 0x00,             // CRC placeholder
        // acTL chunk
        0x00, 0x00, 0x00, 0x08,
        0x61, 0x63, 0x54, 0x4C,       // "acTL"
        0x00, 0x00, 0x00, 0x05,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        // IEND
        0x00, 0x00, 0x00, 0x00,
        0x49, 0x45, 0x4E, 0x44,
        0xAE, 0x42, 0x60, 0x82
    };

    var result = FileFormatDetector.Detect(apngHead, apngHead.Length);
    Assert.Equal(FileFormat.Apng, result);
}
```

- [ ] **Step 5: 运行测试验证**
```bash
dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj --filter "FileFormatDetectorTests"
```
Expected: 3 个新测试全部通过

- [ ] **Step 6: 提交**
```bash
git add src/MantisZip.Core/Utils/FileFormatDetector.cs tests/MantisZip.Tests/Utils/FileFormatDetectorTests.cs
git commit -m "feat(core): FileFormatDetector 新增 APNG 魔数检测 (acTL chunk 扫描)"
```

---

### Task 3: FileFormatHelper.cs - 新增显示名

**Files:**
- Modify: `src/MantisZip.Core/Utils/FileFormatHelper.cs` (或同目录下类似文件)

- [ ] **Step 1: 定位 GetDisplayName 方法**
```bash
grep -n "GetDisplayName" src/MantisZip.Core/Utils/*.cs
```

- [ ] **Step 2: 在 switch 中新增 Apng case**
```csharp
// src/MantisZip.Core/Utils/FileFormatHelper.cs
public static string GetDisplayName(FileFormat format)
{
    return format switch
    {
        // ... 现有 case ...
        FileFormat.Png => "PNG 图像",
        FileFormat.Apng => "APNG 动画",  // NEW
        // ... 其余保持不变
        _ => "未知格式"
    };
}
```

- [ ] **Step 3: 验证编译通过**
```bash
dotnet build src/MantisZip.Core/MantisZip.Core.csproj
```

- [ ] **Step 4: 提交**
```bash
git add src/MantisZip.Core/Utils/FileFormatHelper.cs
git commit -m "feat(core): FileFormatHelper 新增 APNG 显示名"
```

---

### Task 4: PreviewService.cs - 映射与扩展名分类

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Services/PreviewService.cs:176-196` (ClassifyPreview) 和 `267-345` (MapFileFormatToPreviewType)

- [ ] **Step 1: 在 MapFileFormatToPreviewType 中新增映射**
```csharp
// src/MantisZip.UI.Avalonia/Services/PreviewService.cs - MapFileFormatToPreviewType 方法内
private static PreviewType MapFileFormatToPreviewType(FileFormat format)
{
    return format switch
    {
        // 图像 (GIF 单独处理，走动画预览路径)
        FileFormat.Gif => PreviewType.AnimatedImage,
        FileFormat.Apng => PreviewType.AnimatedImage,  // NEW

        FileFormat.Png or FileFormat.Jpeg or FileFormat.Bmp
            or FileFormat.WebP or FileFormat.Ico
            or FileFormat.Tga or FileFormat.Hdr or FileFormat.Exr
            => PreviewType.Image,
        // ... 其余保持不变
    };
}
```

- [ ] **Step 2: 在 ClassifyPreview 中新增扩展名分类**
```csharp
// src/MantisZip.UI.Avalonia/Services/PreviewService.cs - ClassifyPreview 方法内
// 在现有扩展名检查后、return Unsupported 前插入
private static readonly string[] ApngExtensions = { ".apng" };

public static PreviewType ClassifyPreview(string ext)
{
    // ... 现有检查 ...
    if (GifExtensions.Contains(ext)) return PreviewType.AnimatedImage;
    if (ApngExtensions.Contains(ext)) return PreviewType.AnimatedImage;  // NEW
    if (SvgExtensions.Contains(ext)) return PreviewType.Svg;
    // ... 其余保持不变
}
```

- [ ] **Step 3: 验证编译通过**
```bash
dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj
```

- [ ] **Step 4: 提交**
```bash
git add src/MantisZip.UI.Avalonia/Services/PreviewService.cs
git commit -m "feat(avalonia): PreviewService 映射 Apng->AnimatedImage + .apng 扩展名分类"
```

---

### Task 5: 集成验证与回归测试

**Files:**
- 无新文件，仅验证

- [ ] **Step 1: 完整构建验证**
```bash
dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj
```
Expected: 编译成功

- [ ] **Step 2: 运行所有相关测试**
```bash
dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj --filter "FileFormatDetectorTests"
dotnet test tests/MantisZip.UI.Avalonia.Tests/MantisZip.UI.Avalonia.Tests.csproj --filter "Preview"
```
Expected: 所有测试通过

- [ ] **Step 3: 手动验证清单（需真实 APNG 文件）**
```
[ ] 准备测试用 APNG 文件（含 acTL chunk）
[ ] 拖入 MantisZip 预览
[ ] 验证：PreviewType = AnimatedImage
[ ] 验证：动画播放正常（播放/暂停/上帧/下帧/循环）
[ ] 验证：工具栏显示缩放、透明背景、动画控制按钮
[ ] 验证：信息面板显示 "APNG 动画"、尺寸、帧数
[ ] 验证：.apng 扩展名兜底分类正常
[ ] 回归：GIF、Animated WebP、静态 PNG 预览完全正常
```

- [ ] **Step 4: 提交最终确认**
```bash
git status
# 确认无未跟踪/未提交的变更
```

---

## Definition of Done

- [ ] `FileFormat.Apng` 枚举值新增且编译通过
- [ ] `FileFormatDetector.Detect` 正确识别 APNG 魔数（含 `acTL` chunk）
- [ ] `MapFileFormatToPreviewType` 映射 `Apng → AnimatedImage`
- [ ] `ClassifyPreview` 扩展名 `.apng → AnimatedImage`
- [ ] `FileFormatHelper.GetDisplayName` 返回 "APNG 动画"
- [ ] 单元测试覆盖 APNG 魔数检测（正例/反例/含 PLTE）
- [ ] 手动验证：APNG 文件预览播放正常、工具栏显示正确、信息面板显示正确
- [ ] `dotnet build` 通过，无回归
- [ ] 现有 GIF、Animated WebP、静态 PNG 预览无回归

---

## Execution Notes

**并行性：** Task 1-4 完全独立，可并行派发 4 个 subagent 同时执行。Task 5 依赖前 4 个完成后执行。

**风险点：**
- `ScanForActlChunk` 扫描上限 64KB 足以覆盖标准 APNG 结构（IHDR 后通常紧跟 acTL）
- 若 APNG 文件极大且 acTL 靠后，可考虑增大 `AppSettings.PreviewHeadSize` 配合
- `GifDecoder` 类名虽为 "Gif" 但实为通用帧解码器，复用零风险