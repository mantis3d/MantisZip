# NuGet 依赖升级计划

> **状态**: ✅ 已完成 | **阶段**: [✅✅✅✅] (4/4)
> **前置依赖**: 无
> **适用范围**: Core + Avalonia（依赖升级影响全局，需逐库验证）

---

## 动机

### 现状

| 库 | 当前版本 | 最新版本 | 落后程度 | 升级风险 |
|----|---------|---------|---------|---------|
| Markdig | 0.40.0 | 1.3.2 | 🔴 大版本差距 | 🟢 低 |
| SharpCompress | 0.48.1 | 0.50.4 | 🟡 次版本差距 | 🟡 中 |
| SkiaSharp | 3.119.4 | 4.152.0 | 🔴 大版本差距 | 🔴 高 |
| Svg.Skia | 2.0.0.5 | 5.2.1 | 🔴 大版本差距 | 🔴 高（依赖 SkiaSharp 4.x） |

### 升级收益

- **Markdig**: 进入稳定版 1.x，性能优化，API 更成熟
- **SharpCompress**: Bugfix + .NET 10 适配 + 并行解压验证
- **SkiaSharp**: 可变字体支持、彩色字体调色板、动画 WebP 编码、P/Invoke 改为托管代码
- **Svg.Skia**: 跟随 SkiaSharp 4.x 生态

### 升级约束

- **SkiaSharp 4.x ↔ Svg.Skia 5.x 强绑定**：Svg.Skia 5.x 依赖 SkiaSharp ≥ 4.x，必须同步升级
- **SkiaSharp 3.x ↔ Svg.Skia 2.x 强绑定**：如果保持 SkiaSharp 3.x，Svg.Skia 只能用 2.x
- **PdfPig.Rendering.Skia 兼容性**：需确认是否已适配 SkiaSharp 4.x

---

## 受影响的计划

| 计划 | 受影响库 | 影响程度 | 说明 |
|------|---------|---------|------|
| **new-format-support.md** (进行中) | SharpCompress | 🔴 直接阻塞 | 计划明确声明"SharpCompress 0.48.1 内置 BZip2/XZ/Zstd"，升级后 API 可能变化 |
| **compression-performance-optimization.md** (未开始) | SharpCompress | 🟡 需验证 | 并行解压依赖 `ZipArchive.OpenArchive()` 多实例模式 |
| **preview-extended-formats.md** (进行中) | SkiaSharp, Svg.Skia | 🔴 直接阻塞 | Phase 2A.3（PDF）和 Phase 3.1（SVG）明确依赖 SkiaSharp 渲染 |
| **office-content-preview-avalonia.md** (进行中) | Markdig | 🟡 需验证 | Markdown 预览的 WebView 路线用 `markdig.ToHtml()`，纯文本路线用 AST → 控件树 |
| **cross-platform-port.md** (未开始) | SkiaSharp, Svg.Skia, Markdig | 🟡 间接影响 | 跨平台依赖 SkiaSharp/Svg.Skia 渲染，Markdig 用于 Markdown 预览 |

---

## 验证清单

### 1. Markdig 0.40.0 → 1.3.2

**验证项**：

- [x] `MarkdownPreviewBuilder.TryBuildBlock` 的 AST 节点类型是否变化
- [x] `UsePipeTables()` 扩展是否正常工作
- [x] `markdig.ToHtml()` 输出是否一致
- [x] Markdown → Avalonia 控件树渲染管线是否正常

**涉及文件**：
- `src/MantisZip.UI.Avalonia/Services/MarkdownPreviewBuilder.cs`
- `src/MantisZip.UI.Avalonia/Services/PreviewService.cs`（`ShowMarkdown` 方法）

**验证方法**：
```powershell
# 1. 更新 csproj
dotnet add src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj package Markdig --version 1.3.2

# 2. 构建验证
dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj

# 3. 运行测试
dotnet test tests/MantisZip.UI.Avalonia.Tests/MantisZip.UI.Avalonia.Tests.csproj
```

### 2. SharpCompress 0.48.1 → 0.50.4

**验证项**：

- [x] `ZipArchive.OpenArchive()` API 是否变化
- [x] `TarReader.OpenReader()` API 是否变化
- [x] `TarWriter.OpenWriter()` API 是否变化
- [x] `CompressionType.ZStandard` 是否仍可用
- [x] `BZip2Stream`、`XZStream` 是否仍可用
- [x] `IEntry.Size`、`LastModifiedTime` 等属性是否变化
- [x] RAR5 异步解压修复（0.50.3）是否影响现有行为

**涉及文件**：
- `src/MantisZip.Core/Engines/ZipEngine.cs`
- `src/MantisZip.Core/Engines/TarGzEngine.cs`
- `src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs`

**验证方法**：
```powershell
# 1. 更新 csproj
dotnet add src/MantisZip.Core/MantisZip.Core.csproj package SharpCompress --version 0.50.4

# 2. 构建验证
dotnet build src/MantisZip.Core/MantisZip.Core.csproj

# 3. 运行测试
dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
```

### 3. SkiaSharp 3.119.4 → 4.152.0 + Svg.Skia 2.0.0.5 → 5.2.1

**前置条件**：

- [x] 确认 `PdfPig.Rendering.Skia` 是否已适配 SkiaSharp 4.x
- [x] 确认 `Svg.Skia` 5.x 是否已适配 SkiaSharp 4.x
- [x] 如果上游库未适配，暂时保持 3.x / 2.x
- [x] SkiaSharp 3.119.4 → 4.152.0 升级完成

**验证项**：

- [x] PDF 渲染是否正常（`PdfPig.Rendering.Skia`）
- [x] SVG 渲染是否正常（`Svg.Skia`）
- [x] Svg.Skia 2.0.0.5 → 5.2.1 升级完成
- [x] 字体预览是否正常（`HarfBuzzSharp` + `SKCanvas`）
- [x] HarfBuzzSharp 升级完成
- [x] GIF/Animated WebP 解码是否正常（`SKCodec`）
- [x] 图片解码是否正常（`SKBitmap`、`SKImage`）

**涉及文件**：
- `src/MantisZip.UI.Avalonia/Services/PreviewService.cs`（PDF/SVG/GIF/字体预览）
- `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs`

**验证方法**：
```powershell
# 1. 更新 csproj（同步升级 SkiaSharp + Svg.Skia + HarfBuzzSharp）
dotnet add src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj package SkiaSharp --version 4.152.0
dotnet add src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj package Svg.Skia --version 5.2.1
dotnet add src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj package HarfBuzzSharp --version 8.0.0

# 2. 构建验证
dotnet build src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj

# 3. 运行测试
dotnet test tests/MantisZip.UI.Avalonia.Tests/MantisZip.UI.Avalonia.Tests.csproj
```

---

## 执行顺序

```
优先级 P1 — 立即可做（低风险）：
  └─ Markdig 0.40.0 → 1.3.2
     └─ 验证 MarkdownPreviewBuilder + ToHtml() 兼容性
     └─ 预计耗时：1 小时

优先级 P2 — 需先验证（中风险）：
  └─ SharpCompress 0.48.1 → 0.50.4
     └─ 先在测试项目验证关键 API
     └─ 确认 new-format-support 计划的 API 仍有效
     └─ 确认后升级，同步更新 new-format-support 计划
     └─ 预计耗时：2 小时

优先级 P3 — 等上游适配（高风险）：
  └─ SkiaSharp 3.x → 4.x + Svg.Skia 2.x → 5.x
     └─ 等 PdfPig.Rendering.Skia 适配 SkiaSharp 4.x
     └─ 等 Svg.Skia 稳定版适配 SkiaSharp 4.x
     └─ 两者就绪后一起升级
     └─ 预计耗时：待上游就绪后评估
```

---

## 回滚方案

如果升级后出现问题：

1. **立即回滚**：还原 csproj 中的 PackageReference 版本，执行 `dotnet restore`
2. **部分回滚**：如果仅某个库有问题，单独回滚该库
3. **验证回滚**：回滚后执行 `dotnet build` + `dotnet test` 确认恢复

```powershell
# 回滚示例（以 SharpCompress 为例）
dotnet add src/MantisZip.Core/MantisZip.Core.csproj package SharpCompress --version 0.48.1
dotnet build src/MantisZip.Core/MantisZip.Core.csproj
```

---

## 参考资料

- [SharpCompress 0.50.0 Release Notes](https://github.com/adamhathcock/sharpcompress/releases/tag/0.50.0)
- [SkiaSharp 4.x Breaking Changes](https://github.com/mono/SkiaSharp/wiki/Breaking-Changes)
- [Svg.Skia Releases](https://github.com/nickspag/Svg.Skia/releases)
- [Markdig Releases](https://github.com/xoofx/markdig/releases)
