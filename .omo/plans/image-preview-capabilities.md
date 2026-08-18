# 图片预览能力系统（透明 + 动画）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将图片预览的「透明」（棋盘格 🏁）与「动画」（播放/暂停/帧导航）从格式硬编码升级为按 PreviewType 声明的能力注册表，实现 GIF 透明支持 + WebP 动画预览，并让未来新格式（APNG 等）只需注册能力即可获得对应工具栏控件。

**Architecture:** 新增 `PreviewCapabilities` 静态注册表（对齐 `MetadataRegistry` 模式）按 `PreviewType` 声明能力（Zoom/Transparency/FlattenAlpha/AnimationControls），ViewModel 的 4 个 `HasXxxControls` 属性改为查表。新增 `PreviewType.AnimatedImage` 取代 `PreviewType.Gif`（GIF 与 WebP 动画共用），`ShowImage` 内通过 SKCodec 检测 `FrameCount > 1` 分流到动画路径。解码器 `GifDecoder` 已是 SKCodec 通用帧 API 实现（对 GIF/WebP 动画原样可用，已验证），无需改动解码逻辑。

**Tech Stack:** .NET 9 / Avalonia 11 / CommunityToolkit.Mvvm / SkiaSharp 3.119.4（SKCodec 帧 API 对 Animated WebP 逐帧解码已由源码级调研确认支持，含 libwebp demux、disposal/blend 处理）

---

## 背景事实（已调研确认，实现时勿重复调研）

1. **`GifDecoder.DecodeFrames`（Services/GifDecoder.cs）用 SKCodec 通用帧 API**：`SKCodec.Create` → `codec.FrameCount` → 循环 `codec.GetPixels(info, pixels, new SKCodecOptions(i))`。对 Animated WebP 同一路径原样可用（Skia `SkWebpCodec` 覆写全套帧 API，libwebp demux 实现，含 required frame 递归解码）。**解码层零改动**。
2. **`SKCodec.FrameCount` 语义**：静态 WebP = 1，动画 WebP = N，静态 PNG/JPEG = 0。`FrameCount > 1` 判定"是动画"对 GIF/WebP 均成立。
3. **GIF 透明基础设施已就位**：`PreviewPanel.axaml` 的 `GifPreviewScrollViewer` 内已有 `TransparencyCheckerboardBrush` 矩形（绑定 `IsTransparencyBgShown`），`ToggleTransparencyBg` 命令通用。只差 `HasTransparencyControls` 未覆盖 GIF → 工具栏 🏁 按钮不显示。
4. **`PreviewType.Gif` 全部引用点（迁移清单）**：
   - `Services/PreviewService.cs:179`（扩展名回退分类）、`:264`（魔数映射）
   - `ViewModels/MainWindowViewModel.cs:1167`（switch case）
   - `ViewModels/PreviewViewModel.cs:212`（HasZoomControls）、`:222`（IsGifVisible）、`:252`/`:270`（OnPreviewTypeChanged 通知）、`:530`（HasGifControls）、`:1065`（ShowGif 赋值）
   - `Views/PreviewPanel.axaml:77`（`HasGifControls`）、`:270`（`IsGifVisible`）
5. **`OnPreviewTypeChanged`（PreviewViewModel.cs:242-284）已通知全部 `HasXxxControls`/`IsXxxVisible`**，本计划只需把 `nameof(HasGifControls)` 改 `nameof(HasAnimationControls)`。
6. **`ext` 变量在 MainWindowViewModel 的 switch 外已定义**（case 内直接可用）。
7. **`OnPreviewTypeChanged` 中 IsGifVisible 通知改名**后，`PreviewPanel.axaml` 绑定同步改。

---

## 文件结构

| 文件 | 操作 | 职责 |
|------|------|------|
| `src/MantisZip.UI.Avalonia/Services/PreviewCapabilities.cs` | 新增 | 能力枚举 + 静态注册表 |
| `src/MantisZip.UI.Avalonia/Services/PreviewService.cs` | 修改 | `PreviewType` 枚举 + GIF 映射 → `AnimatedImage` |
| `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs` | 修改 | `HasXxxControls` 查表、`ShowImage` 动画分流、`ShowGif` 类型/标题 |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | 修改 | switch case + 状态栏文案分流 |
| `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml` | 修改 | 绑定改名 |
| `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` | 修改 | 新增 key |
| `src/MantisZip.UI.Avalonia/Localization/strings.en.json` | 修改 | 新增 key |
| `tests/MantisZip.UI.Avalonia.Tests/PreviewViewModelTests.cs` | 修改 | 能力表/透明/动画分流测试 |
| `docs/PLAN.md` | 修改 | 规则 1：计划引用行 |
| `docs/PROGRESS.md` | 修改 | 规则 3：提交前更新 |

---

## Task 1: `PreviewType.AnimatedImage` 枚举迁移

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Services/PreviewService.cs:10-34`（枚举）、`:179`、`:264`
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs:1167`
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs:212`、`:222`、`:252`、`:270`、`:530`、`:1065`
- Modify: `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml:77`、`:270`

- [ ] **Step 1: 枚举新增 `AnimatedImage`（保留 `Gif` 但注释废弃）**

`Services/PreviewService.cs` 的 `PreviewType` 枚举（约 10-34 行）：

```csharp
public enum PreviewType
{
    None,
    Text,
    Csv,
    Pe,
    Image,
    Gif,            // ⚠️ 已废弃：由 AnimatedImage 取代（保留枚举值避免破坏性变更，不再被任何映射产出）
    AnimatedImage,  // 动画图像预览（GIF / Animated WebP 共用）
    Svg,
    Font,
    Audio,
    Sqlite,
    Iso,
    Torrent,
    Office,
    Docx,
    Xlsx,
    Pptx,
    Video,
    Html,
    Markdown,
    Pdf,
    IcoGallery,
    Unsupported
}
```

- [ ] **Step 2: 两处 GIF → AnimatedImage 映射**

`Services/PreviewService.cs:179`（扩展名回退分类）：

```csharp
if (GifExtensions.Contains(ext)) return PreviewType.AnimatedImage;
```

`Services/PreviewService.cs:264`（魔数映射）：

```csharp
FileFormat.Gif => PreviewType.AnimatedImage,
```

- [ ] **Step 3: ViewModel 属性迁移**

`ViewModels/PreviewViewModel.cs`：

```csharp
// :212
public bool HasZoomControls => PreviewType is PreviewType.Image or PreviewType.AnimatedImage;

// :222
public bool IsAnimatedImageVisible => PreviewType == PreviewType.AnimatedImage;

// :252
OnPropertyChanged(nameof(IsAnimatedImageVisible));

// :270
OnPropertyChanged(nameof(HasAnimationControls));

// :530
public bool HasAnimationControls => PreviewType == PreviewType.AnimatedImage;

// :1065（ShowGif 内）
PreviewType = PreviewType.AnimatedImage;
```

- [ ] **Step 4: MainWindowViewModel switch case**

`ViewModels/MainWindowViewModel.cs:1167`：

```csharp
case PreviewType.AnimatedImage:
    if (!PreviewService.EnableImagePreview)
    {
        Preview.ShowUnsupported(LocalizationManager.T("Preview_ImageDisabled"));
        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
        break;
    }
    Preview.ShowGif(tempFile);
    StatusMessage = LocalizationManager.T("Preview_Gif", entry.DisplayName);
    break;
```

（状态栏文案按扩展名分流在 Task 4 完成，此处先保持 `Preview_Gif` 保证编译与行为不回归。）

- [ ] **Step 5: PreviewPanel.axaml 绑定改名**

`Views/PreviewPanel.axaml:77`：`IsVisible="{Binding HasGifControls}"` → `IsVisible="{Binding HasAnimationControls}"`

`Views/PreviewPanel.axaml:270`：`IsVisible="{Binding IsGifVisible}"` → `IsVisible="{Binding IsAnimatedImageVisible}"`

- [ ] **Step 6: 验证构建**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 已成功生成，0 警告，0 错误（如报 `PreviewType.Gif` 残留引用，按背景事实 4 的清单排查）

- [ ] **Step 7: 提交**

```bash
git add src/MantisZip.UI.Avalonia/Services/PreviewService.cs src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml
git commit -m "refactor(avalonia): PreviewType.Gif 迁移为 AnimatedImage——GIF/WebP 动画共用预览类型"
```

---

## Task 2: `PreviewCapabilities` 能力注册表 + GIF 透明

**Files:**
- Create: `src/MantisZip.UI.Avalonia/Services/PreviewCapabilities.cs`
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs:212`、`:530`、`:535-536`
- Test: `tests/MantisZip.UI.Avalonia.Tests/PreviewViewModelTests.cs`

- [ ] **Step 1: 写失败测试（能力注册表 + GIF 透明按钮）**

`tests/MantisZip.UI.Avalonia.Tests/PreviewViewModelTests.cs` 追加：

```csharp
/// <summary>
/// 能力注册表：Image=缩放+透明+压平；AnimatedImage=缩放+透明+动画控制（无压平，方案 A 决策）；
/// Svg=透明+压平；IcoGallery=仅透明；未注册类型=None。
/// </summary>
[Fact]
public void PreviewCapabilities_Registry_DeclaresExpectedFlags()
{
    Assert.True(PreviewCapabilities.For(PreviewType.Image).HasFlag(PreviewCapability.Zoom));
    Assert.True(PreviewCapabilities.For(PreviewType.Image).HasFlag(PreviewCapability.Transparency));
    Assert.True(PreviewCapabilities.For(PreviewType.Image).HasFlag(PreviewCapability.FlattenAlpha));
    Assert.False(PreviewCapabilities.For(PreviewType.Image).HasFlag(PreviewCapability.AnimationControls));

    Assert.True(PreviewCapabilities.For(PreviewType.AnimatedImage).HasFlag(PreviewCapability.Zoom));
    Assert.True(PreviewCapabilities.For(PreviewType.AnimatedImage).HasFlag(PreviewCapability.Transparency));
    Assert.True(PreviewCapabilities.For(PreviewType.AnimatedImage).HasFlag(PreviewCapability.AnimationControls));
    Assert.False(PreviewCapabilities.For(PreviewType.AnimatedImage).HasFlag(PreviewCapability.FlattenAlpha));

    Assert.True(PreviewCapabilities.For(PreviewType.Svg).HasFlag(PreviewCapability.Transparency));
    Assert.True(PreviewCapabilities.For(PreviewType.Svg).HasFlag(PreviewCapability.FlattenAlpha));

    Assert.True(PreviewCapabilities.For(PreviewType.IcoGallery).HasFlag(PreviewCapability.Transparency));
    Assert.False(PreviewCapabilities.For(PreviewType.IcoGallery).HasFlag(PreviewCapability.FlattenAlpha));

    Assert.Equal(PreviewCapability.None, PreviewCapabilities.For(PreviewType.Text));
}

/// <summary>
/// GIF 预览必须暴露透明控制（🏁 棋盘格）且不暴露压平（🎨 为静态图专用）。
/// 用 1×1 透明 GIF 样本（base64 内嵌，SKCodec 可解 1 帧）。
/// </summary>
[AvaloniaFact]
public void ShowGif_ExposesTransparencyControls()
{
    var gifPath = CreateTestGif();
    try
    {
        var vm = new PreviewViewModel();
        vm.ShowGif(gifPath);

        Assert.Equal(PreviewType.AnimatedImage, vm.PreviewType);
        Assert.True(vm.HasTransparencyControls);
        Assert.False(vm.HasFlattenAlphaControls);
        Assert.True(vm.HasAnimationControls);
    }
    finally
    {
        File.Delete(gifPath);
    }
}

/// <summary>写一个 1×1 透明 GIF 到临时目录（经典 43 字节样本，base64）。</summary>
private static string CreateTestGif()
{
    var bytes = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
    var path = Path.Combine(Path.GetTempPath(), $"mantiszip_gif_test_{Guid.NewGuid():N}.gif");
    File.WriteAllBytes(path, bytes);
    return path;
}
```

- [ ] **Step 2: 运行测试验证失败**

Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~PreviewCapabilities|FullyQualifiedName~ShowGif_ExposesTransparencyControls"`
Expected: FAIL——`PreviewCapabilities` 类型不存在（编译错误）；若先建类则断言失败（AnimatedImage 未注册 Transparency）

- [ ] **Step 3: 新建能力注册表**

`src/MantisZip.UI.Avalonia/Services/PreviewCapabilities.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 预览能力标记（[Flags]）。新增预览类型时在 <see cref="PreviewCapabilities"/> 注册能力即可，
/// 工具栏按钮/控件区域据此显隐，无需再改 ViewModel 属性。
/// </summary>
[Flags]
public enum PreviewCapability
{
    None = 0,
    /// <summary>缩放控制（放大/缩小/适应视口）。</summary>
    Zoom = 1 << 0,
    /// <summary>透明背景棋盘格切换（🏁）。</summary>
    Transparency = 1 << 1,
    /// <summary>压平 Alpha（🎨，仅静态图语义；动画帧不做全帧压平）。</summary>
    FlattenAlpha = 1 << 2,
    /// <summary>动画播放控制（播放/暂停/上帧/下帧/帧输入）。</summary>
    AnimationControls = 1 << 3,
}

/// <summary>
/// 预览类型 → 能力注册表（对齐 MetadataRegistry 的静态注册模式）。
/// 能力影响工具栏按钮显隐（HasZoomControls/HasTransparencyControls/HasFlattenAlphaControls/HasAnimationControls）。
/// </summary>
public static class PreviewCapabilities
{
    private static readonly Dictionary<PreviewType, PreviewCapability> _capabilities = new();

    static PreviewCapabilities()
    {
        Register(PreviewType.Image,
            PreviewCapability.Zoom | PreviewCapability.Transparency | PreviewCapability.FlattenAlpha);
        Register(PreviewType.AnimatedImage,
            PreviewCapability.Zoom | PreviewCapability.Transparency | PreviewCapability.AnimationControls);
        Register(PreviewType.Svg,
            PreviewCapability.Transparency | PreviewCapability.FlattenAlpha);
        Register(PreviewType.IcoGallery,
            PreviewCapability.Transparency);
    }

    private static void Register(PreviewType type, PreviewCapability capabilities)
    {
        _capabilities[type] = capabilities;
    }

    /// <summary>查询预览类型的能力集合；未注册类型返回 <see cref="PreviewCapability.None"/>。</summary>
    public static PreviewCapability For(PreviewType type)
    {
        return _capabilities.TryGetValue(type, out var caps) ? caps : PreviewCapability.None;
    }
}
```

- [ ] **Step 4: ViewModel 属性查表**

`ViewModels/PreviewViewModel.cs`：

```csharp
// :212
public bool HasZoomControls => PreviewCapabilities.For(PreviewType).HasFlag(PreviewCapability.Zoom);

// :530
public bool HasAnimationControls => PreviewCapabilities.For(PreviewType).HasFlag(PreviewCapability.AnimationControls);

// :535-536
public bool HasTransparencyControls => PreviewCapabilities.For(PreviewType).HasFlag(PreviewCapability.Transparency);
public bool HasFlattenAlphaControls => PreviewCapabilities.For(PreviewType).HasFlag(PreviewCapability.FlattenAlpha);
```

- [ ] **Step 5: 运行测试验证通过**

Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~PreviewCapabilities|FullyQualifiedName~ShowGif_ExposesTransparencyControls"`
Expected: PASS（能力表 4 断言组 + ShowGif 透明 3 断言）

- [ ] **Step 6: 验证构建 + 全量测试**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj && dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`
Expected: 0 错误 0 警告；测试全部通过（基线 56 通过 2 跳过 + 新增）

- [ ] **Step 7: 提交**

```bash
git add src/MantisZip.UI.Avalonia/Services/PreviewCapabilities.cs src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs tests/MantisZip.UI.Avalonia.Tests/PreviewViewModelTests.cs
git commit -m "feat(avalonia): 预览能力注册表——GIF 透明棋盘格支持（AnimatedImage 注册 Transparency）"
```

---

## Task 3: WebP 动画分流（ShowImage 检测 FrameCount）

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs:836-893`（ShowImage）
- Test: `tests/MantisZip.UI.Avalonia.Tests/PreviewViewModelTests.cs`

- [ ] **Step 1: 写失败测试（animated WebP 分流到动画路径 + 静态 WebP 保持 Image）**

`tests/MantisZip.UI.Avalonia.Tests/PreviewViewModelTests.cs` 追加：

```csharp
/// <summary>
/// Animated WebP（多帧）经 ShowImage 必须分流到动画预览（AnimatedImage 类型 + 透明控制）。
/// 样本：32×32 两帧红→蓝 animated webp（base64 内嵌，用 Python Pillow 生成后转码，见下文样本说明）。
/// </summary>
[AvaloniaFact]
public void ShowImage_AnimatedWebP_RoutesToAnimationPreview()
{
    var webpPath = CreateTestAnimatedWebP();
    try
    {
        var vm = new PreviewViewModel();
        vm.ShowImage(webpPath);

        Assert.Equal(PreviewType.AnimatedImage, vm.PreviewType);
        Assert.True(vm.HasTransparencyControls);
        Assert.True(vm.HasAnimationControls);
    }
    finally
    {
        File.Delete(webpPath);
    }
}

/// <summary>
/// 静态 WebP（单帧）经 ShowImage 保持 Image 预览（透明+压平能力）。
/// </summary>
[AvaloniaFact]
public void ShowImage_StaticWebP_StaysImagePreview()
{
    var webpPath = CreateTestWebp(64, 64);
    try
    {
        var vm = new PreviewViewModel();
        vm.ShowImage(webpPath);

        Assert.Equal(PreviewType.Image, vm.PreviewType);
        Assert.True(vm.HasTransparencyControls);
        Assert.True(vm.HasFlattenAlphaControls);
    }
    finally
    {
        File.Delete(webpPath);
    }
}

/// <summary>用 SkiaSharp 生成纯色静态 WebP 测试图。</summary>
private static string CreateTestWebp(int width, int height)
{
    using var bitmap = new SKBitmap(width, height);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Blue);
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Webp, 90);
    var path = Path.Combine(Path.GetTempPath(), $"mantiszip_webp_test_{Guid.NewGuid():N}.webp");
    File.WriteAllBytes(path, data.ToArray());
    return path;
}

/// <summary>写 animated WebP（32×32 两帧）到临时目录。样本为 base64 内嵌常量（生成方法见下）。</summary>
private static string CreateTestAnimatedWebP()
{
    // 样本：32×32 两帧红→蓝 animated webp（236 字节，base64 内嵌；生成方法见下注释）
    var bytes = Convert.FromBase64String("UklGRuQAAABXRUJQVlA4WAoAAAACAAAAHwAAHwAAQU5JTQYAAAAAAAAAAABBTk1GXAAAAAAAAAAAAB8AAB8AAGQAAAJWUDggRAAAALADAJ0BKiAAIAA+bTSWR6QjIiEoCACADYllAMkCgH4AAtaQY7UAAP7wm0P/yC5YXXI1//ID/kB/yA//kB/+m9mp84AAQU5NRlQAAAAAAAAAAAAfAAAfAABkAAAAVlA4IDwAAADUAgCdASogACAAPm00lkeCgIAAANiWUAdgAExQ4dowAP75Hdv//kB//kB//kB//ID//iF3shz/9P4AAAA=");
    var path = Path.Combine(Path.GetTempPath(), $"mantiszip_anim_webp_test_{Guid.NewGuid():N}.webp");
    File.WriteAllBytes(path, bytes);
    return path;
}
```

> **样本来源（供复核）**：上述 base64 为 32×32 两帧（红→蓝）animated WebP，236 字节，由 Python Pillow 12.3 生成（`Image.save('anim.webp', save_all=True, append_images=[frame2], duration=100, loop=0)`）。实现时若测试失败，先验证 `SKCodec.Create` 对该样本的 `FrameCount` 是否 ≥ 2（Pillow 输出为标准 RIFF/VP8X/ANIM/ANMF，SKCodec 应返回 2）；如需重新生成，方法见上注释或 ffmpeg：`ffmpeg -f lavfi -i "color=c=red:s=32x32:d=0.1" -f lavfi -i "color=c=blue:s=32x32:d=0.1" -filter_complex "[0][1]concat=n=2:v=1" -loop 0 anim.webp`。

- [ ] **Step 2: 运行测试验证失败**

Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~AnimatedWebP|FullyQualifiedName~StaticWebP"`
Expected: `ShowImage_AnimatedWebP_RoutesToAnimationPreview` FAIL（当前 ShowImage 把动画 WebP 当静态图，PreviewType=Image）；`ShowImage_StaticWebP_StaysImagePreview` 通过或 FAIL（视占位 base64 是否已替换——若未替换则 Convert.FromBase64String 抛异常，先把占位换成真实样本再跑）

- [ ] **Step 3: ShowImage 加 FrameCount 分流**

`ViewModels/PreviewViewModel.cs` 的 `ShowImage`（836-893 行），在 SKCodec 检测块内插入动画分支：

```csharp
global::Avalonia.Media.Imaging.Bitmap bitmap;
using (var fs = File.OpenRead(filePath))
{
    using var skStream = new SKManagedStream(fs, disposeManagedStream: false);
    using var codec = SKCodec.Create(skStream);
    if (codec == null || codec.Info.Width <= 0)
    {
        // codec 无法解析时退回原生解码路径（解码失败由上层异常处理）
        fs.Position = 0;
        bitmap = new global::Avalonia.Media.Imaging.Bitmap(fs);
    }
    else if (codec.FrameCount > 1)
    {
        // 动画（当前为 Animated WebP；GIF 由分类直接走 ShowGif 不经此路径）：
        // 复用通用动画预览——播放/暂停/帧导航 + 透明棋盘格（Task 2 已注册能力）
        ShowGif(filePath);
        return;
    }
    else if (codec.Info.Width > 1920)
    {
        fs.Position = 0;
        bitmap = global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(fs, 1920);
    }
    else
    {
        fs.Position = 0;
        bitmap = new global::Avalonia.Media.Imaging.Bitmap(fs);
    }
}
```

- [ ] **Step 4: 运行测试验证通过**

Run: `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~AnimatedWebP|FullyQualifiedName~StaticWebP|FullyQualifiedName~SmallImage|FullyQualifiedName~LargeImage"`
Expected: 全部 PASS（含上一轮的小图/大图回归测试，确认分流未破坏 DecodeToWidth 门槛）

- [ ] **Step 5: 验证构建 + 全量测试**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj && dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`
Expected: 0 错误 0 警告；全部通过

- [ ] **Step 6: 提交**

```bash
git add src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs tests/MantisZip.UI.Avalonia.Tests/PreviewViewModelTests.cs
git commit -m "feat(avalonia): Animated WebP 预览——ShowImage 检测 FrameCount>1 分流到动画路径"
```

---

## Task 4: 本地化 + 状态栏/标题按扩展名分流

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json`
- Modify: `src/MantisZip.UI.Avalonia/Localization/strings.en.json`
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs`（ShowGif 标题）
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`（case 状态栏）

- [ ] **Step 1: 新增本地化 key（成对，插入文件头 `{` 之后，UTF-8 无 BOM + CRLF + 2 空格缩进）**

`strings.zh-CN.json`：

```json
  "Preview_AnimatedImage": "动画预览 {0}",
  "Preview_Header_AnimatedImage": "动画图片",
```

`strings.en.json`：

```json
  "Preview_AnimatedImage": "Animated Image Preview {0}",
  "Preview_Header_AnimatedImage": "Animated Image",
```

（注意：`Preview_Gif`/`Preview_Header_Gif` 保留不动，GIF 文件仍显示 GIF 文案。）

- [ ] **Step 2: ShowGif 标题按扩展名分流**

`ViewModels/PreviewViewModel.cs` 的 `ShowGif` 内（约 1068 行）：

```csharp
// 替换：
PreviewHeaderText = LocalizationManager.T("Preview_Header_Gif");
// 为：
var isGif = Path.GetExtension(filePath).Equals(".gif", StringComparison.OrdinalIgnoreCase);
PreviewHeaderText = LocalizationManager.T(isGif ? "Preview_Header_Gif" : "Preview_Header_AnimatedImage");
```

- [ ] **Step 3: MainWindowViewModel 状态栏按扩展名分流**

`ViewModels/MainWindowViewModel.cs` 的 `case PreviewType.AnimatedImage` 内（Task 1 已改的 case）：

```csharp
Preview.ShowGif(tempFile);
var isGifFile = ext.Equals(".gif", StringComparison.OrdinalIgnoreCase);
StatusMessage = LocalizationManager.T(
    isGifFile ? "Preview_Gif" : "Preview_AnimatedImage", entry.DisplayName);
break;
```

- [ ] **Step 4: 验证**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 错误 0 警告
Run: 检查 JSON 双文件 key 集对称（zh 与 en 的新增 key 各 2 个、一一对应）

- [ ] **Step 5: 提交**

```bash
git add src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json src/MantisZip.UI.Avalonia/Localization/strings.en.json src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs
git commit -m "feat(avalonia): 动画预览本地化——WebP 动画与 GIF 分流文案（Preview_AnimatedImage/Preview_Header_AnimatedImage）"
```

---

## Task 5: 文档同步 + 最终验证

**Files:**
- Modify: `docs/PLAN.md`
- Modify: `docs/PROGRESS.md`

- [ ] **Step 1: PLAN.md 引用行（规则 1）**

在 `docs/PLAN.md` 对应优先级区域（参照既有条目格式）添加：

```markdown
| 图片预览能力系统（透明+动画注册表） | PreviewType 能力注册表（Zoom/Transparency/FlattenAlpha/AnimationControls）+ GIF 透明棋盘格 + Animated WebP 动画预览，新格式注册能力即可复用工具栏 | 方案见 [image-preview-capabilities.md](.omo/plans/image-preview-capabilities.md) |
```

并更新 PLAN.md 头部「最后更新日期」。

- [ ] **Step 2: 最终验证**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj && dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`
Expected: 0 错误 0 警告；全部测试通过（基线 56 通过 2 跳过 + 新增 5 个：能力表 1 + GIF 透明 1 + 动画 WebP 1 + 静态 WebP 1 + 共 4 个新测试）

- [ ] **Step 3: PROGRESS.md 条目（规则 3，提交前）**

在 `docs/PROGRESS.md` 的 `### MantisZip.UI.Avalonia（主力版）` 区域顶部（`**2026-08-18**` 条目之上）新增：

```markdown
**2026-08-18** — 图片预览能力系统：透明/动画能力注册表 + GIF 透明 + Animated WebP 预览
  - **预览能力注册表**：新增 `PreviewCapabilities`（[Flags] `PreviewCapability`：Zoom/Transparency/FlattenAlpha/AnimationControls），按 PreviewType 声明能力，`HasZoomControls`/`HasTransparencyControls`/`HasFlattenAlphaControls`/`HasAnimationControls` 全部查表（对齐 MetadataRegistry 模式）；新增格式只注册能力即可获得工具栏控件
  - **GIF 透明支持**：`AnimatedImage` 注册 Transparency——工具栏 🏁 棋盘格出现（棋盘格矩形早已接好，此前仅按钮未暴露）；🎨 压平保持静态图专用（方案 A 决策：动画帧不做全帧压平）
  - **Animated WebP 预览**：`PreviewType.Gif` → `PreviewType.AnimatedImage`（GIF/WebP 动画共用）；`ShowImage` 内 SKCodec 检测 `FrameCount > 1` 分流到动画路径（解码复用 `GifDecoder` 的 SKCodec 通用帧 API，零解码改造，已源码级验证 Skia WebP 帧支持）；静态 WebP 保持 Image 预览
  - **本地化**：新增 `Preview_AnimatedImage`/`Preview_Header_AnimatedImage`，状态栏与标题按扩展名分流（.gif → GIF 文案，其余动画 → 动画文案）
  - 涉及文件：`Services/PreviewCapabilities.cs`（新增）、`Services/PreviewService.cs`、`ViewModels/PreviewViewModel.cs`、`ViewModels/MainWindowViewModel.cs`、`Views/PreviewPanel.axaml`、`Localization/strings.*.json`、`tests/MantisZip.UI.Avalonia.Tests/PreviewViewModelTests.cs`
  - 验证：`dotnet build` 0 错误 0 警告、Avalonia 测试全部通过（56 基线 + 4 新增）、lsp 无诊断
```

- [ ] **Step 4: 最终提交**

```bash
git add docs/PLAN.md docs/PROGRESS.md
git commit -m "docs: 图片预览能力系统计划与进度记录（能力注册表 + GIF 透明 + WebP 动画）"
```

---

## 自检清单（对照需求）

| 用户需求 | 对应 Task | 完成标准 |
|---------|----------|---------|
| GIF 透明（🏁 棋盘格） | Task 2 | `ShowGif_ExposesTransparencyControls` 测试通过；GIF 预览工具栏出现 🏁 |
| WebP 动画（播放/暂停/帧导航） | Task 3 | `ShowImage_AnimatedWebP_RoutesToAnimationPreview` 测试通过 |
| 方便扩展（能力注册表） | Task 2 | `PreviewCapabilities.Registry` 测试通过；新增格式只改注册表 |
| 静态 WebP 不回归 | Task 3 | `ShowImage_StaticWebP_StaysImagePreview` 测试通过 |
| 小图/大图解码门槛不回归 | Task 3 Step 4 | `ShowImage_SmallImage_KeepsNativeResolution` / `ShowImage_LargeImage_DownscalesTo1920` 仍通过 |
| 本地化（规则 13） | Task 4 | 双文件 key 对称；无硬编码新文案 |
| 文档同步（规则 1/3） | Task 5 | PLAN.md 引用行 + PROGRESS.md 条目 |

**范围外（明确不做）**：
- 🎨 压平对动画帧（方案 A 决策，`AnimatedImage` 不注册 `FlattenAlpha`）
- `GifDecoder`/`GifFrameData`/播放命令名（`PlayPauseGif` 等）改名——SKCodec 通用解码器，内部命名属后续清理项
- WebP 动画循环次数（`RepetitionCount`）精确遵循——现状 GIF 亦为无限循环，保持一致
- `PreviewType.Gif` 枚举值删除——保留避免破坏性变更