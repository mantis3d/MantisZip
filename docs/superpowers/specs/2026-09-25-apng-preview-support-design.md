# APNG 预览支持设计文档

## 1. 概述

为 MantisZip 预览系统添加 APNG (Animated PNG) 动画预览支持。

**设计目标：**
- 复用现有 `PreviewType.AnimatedImage` 管线（GIF + Animated WebP 已统一），零破坏性变更
- 扩展性优先：后续新增动画格式（如 AVIF 动画、WebP 2 动画等）只需极少改动
- 魔数检测准确识别 APNG，扩展名兜底

## 2. 现有架构复用分析

### 2.1 已就绪的复用点

| 组件 | 现状 | APNG 复用方式 |
|------|------|---------------|
| `PreviewType.AnimatedImage` | 已存在，处理 GIF + Animated WebP | 直接复用，无需新增枚举 |
| `PreviewCapabilities` | `AnimatedImage` 注册了 `Zoom \| Transparency \| AnimationControls` | 自动获得缩放、透明背景、播放控制 |
| `ShowImage` → `ShowGif` 分流 | `SKCodec.FrameCount > 1` 自动分流到 `ShowGif` | APNG 同理 `FrameCount > 1` 自动走动画管线 |
| `GifDecoder` / `ShowGif` | 通用帧解码 + `DispatcherTimer` 播放 | **完全复用**：APNG 也是逐帧解码 + 延时播放 |
| `MetadataKeys.FrameCount` | 已注册 | 信息面板自动显示帧数 |

### 2.2 需要扩展的最小集

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `FileFormat` (Core) | 新增 `Apng` 枚举值 | 格式追踪、统计、调试 |
| `FileFormatDetector.Detect` | 扩展魔数检测 | 读取 PNG 头部后继续扫描 `acTL` chunk |
| `MapFileFormatToPreviewType` | 新增 `FileFormat.Apng => AnimatedImage` | 魔数→预览类型映射 |
| `ClassifyPreview` (扩展名) | 新增 `.apng` → `AnimatedImage` | 扩展名兜底分类 |
| `PreviewCapabilities` | 无需变更 | `AnimatedImage` 已注册动画能力 |

## 3. 详细设计

### 3.1 FileFormat 枚举扩展

```csharp
// src/MantisZip.Core/Utils/FileFormatInfo.cs
public enum FileFormat
{
    // ... 现有值 ...
    Png,           // 现有
    Apng,          // NEW: APNG 动画
    // ... 其余 ...
}
```

**理由**：区分静态 PNG 与 APNG，便于：
- 信息面板显示正确格式名（"APNG 动画" vs "PNG 图像"）
- 统计/遥测区分格式
- 未来若需差异化处理有枚举依托

### 3.2 魔数检测：APNG 识别算法

APNG 文件结构：
```
PNG 签名 (8 bytes): 89 50 4E 47 0D 0A 1A 0A
IHDR chunk (25 bytes): 长度(4) + "IHDR"(4) + 数据(13) + CRC(4)
[可选] acTL chunk (8+ bytes): 动画控制块，标识 APNG
```

**检测策略**：
1. 先匹配 PNG 签名
2. 读取 IHDR chunk（固定位置）
3. 继续扫描后续 chunk，若发现 `acTL` (0x6163544C) → 确认为 APNG
4. 扫描上限：前 64KB（足以覆盖 IHDR + acTL + 可能的 PLTE）

```csharp
// FileFormatDetector.Detect 新增逻辑（伪码）
if (IsPngSignature(head, length)) {
    if (ScanForActlChunk(head, length)) 
        return FileFormat.Apng;
    return FileFormat.Png;
}
```

**扫描实现细节**：
- PNG chunk 结构：`Length(4) + Type(4) + Data(Length) + CRC(4)`
- 大端序读取 Length
- 从 offset=8 开始遍历，直到 `IEND` 或超出 `length` 或超出 64KB
- 发现 Type == "acTL" 即返回 true

### 3.3 映射与分类表更新

```csharp
// PreviewService.MapFileFormatToPreviewType
FileFormat.Apng => PreviewType.AnimatedImage,  // NEW

// PreviewService.ClassifyPreview (扩展名)
if (ApngExtensions.Contains(ext)) return PreviewType.AnimatedImage;  // NEW
// ApngExtensions = new[] { ".apng" }
```

### 3.4 信息面板显示名

```csharp
// FileFormatHelper.GetDisplayName
FileFormat.Apng => "APNG 动画",
```

### 3.5 预览能力注册

**无需变更** —— `AnimatedImage` 已注册：
```csharp
Register(PreviewType.AnimatedImage,
    PreviewCapability.Zoom | PreviewCapability.Transparency | PreviewCapability.AnimationControls);
```

工具栏自动显示：播放/暂停、上帧/下帧、帧输入框、缩放、透明背景切换。

## 4. 数据流走向

```
用户选中 .apng 条目
    ↓
PreviewService.ClassifyPreviewByMagicAsync
    ↓ (ExtractHeadAsync 读取头部 4KB+)
FileFormatDetector.Detect → FileFormat.Apng
    ↓
MapFileFormatToPreviewType → PreviewType.AnimatedImage
    ↓
PreviewViewModel.ShowImage(filePath)
    ↓ (SKCodec.Create → FrameCount > 1)
ShowGif(filePath)  // 复用现有动画管线
    ↓
GifDecoder.DecodeFrames → List<GifFrameData> (复用类，实为通用帧数据)
    ↓
DispatcherTimer 播放动画
```

**关键点**：`GifDecoder` 类名虽为 "Gif"，实则使用 `SKCodec` 通用解码，**完全兼容 APNG**（同为基于 PNG 的帧序列格式）。无需新建解码器。

## 5. 扩展性设计（为 AVIF 等后续格式铺路）

### 5.1 通用动画帧数据类重命名（可选，建议）

当前 `GifFrameData` 实为通用帧容器。建议重命名为 `AnimationFrameData`，或保留原名但加 XML 注释说明通用性。**不阻塞本任务**，作为后续重构项。

### 5.2 新增动画格式的标准接入清单

后续新增动画格式（AVIF 动画、JXL 动画等）只需：

1. `FileFormat` 新增枚举值
2. `FileFormatDetector` 新增魔数检测
3. `MapFileFormatToPreviewType` 映射到 `AnimatedImage`
4. `ClassifyPreview` 扩展名映射
5. `FileFormatHelper.GetDisplayName` 显示名
6. **无需** 修改 `PreviewViewModel`、`PreviewCapabilities`、播放逻辑

### 5.3 静态图回退机制

若某格式既有静态版也有动画版（如 AVIF），`SKCodec.FrameCount` 运行时判定：
- `FrameCount == 1` → `ShowImage` 静态管线（`PreviewType.Image`）
- `FrameCount > 1` → `ShowGif` 动画管线（`PreviewType.AnimatedImage`）

**魔数检测阶段无需区分动态/静态**，统一映射到基础格式（如 `FileFormat.Avif`），运行时由 `FrameCount` 分流。

## 6. 实施步骤

| 步骤 | 文件 | 变更 |
|------|------|------|
| 1 | `FileFormatInfo.cs` | 新增 `Apng` 枚举 |
| 2 | `FileFormatDetector.cs` | 新增 `ScanForActlChunk` + PNG 分支调用 |
| 3 | `FileFormatHelper.cs` | 新增显示名 "APNG 动画" |
| 4 | `PreviewService.cs` | `MapFileFormatToPreviewType` + `ClassifyPreview` 扩展名 |
| 5 | `strings.zh-CN.json` / `strings.en.json` | 新增本地化键（如 `Preview_Header_Apng` 可选，复用 `Preview_Header_AnimatedImage` 即可） |
| 6 | 单元测试 | `FileFormatDetectorTests` 新增 APNG 测试用例 |

## 7. 验收标准

1. **魔数识别**：含 `acTL` chunk 的 PNG 文件被识别为 `FileFormat.Apng`
2. **预览分类**：`PreviewType.AnimatedImage`
3. **播放功能**：帧动画正常播放、暂停、逐帧导航、循环
4. **工具栏**：显示缩放、透明背景、动画控制按钮
5. **信息面板**：显示 "APNG 动画"、尺寸、帧数
6. **扩展名兜底**：`.apng` 文件无魔数时也能正确分类
7. **不回归**：现有 GIF、Animated WebP、静态 PNG 预览完全正常

## 8. 风险与对策

| 风险 | 可能性 | 对策 |
|------|--------|------|
| `ExtractHeadAsync` 读取头部不足导致漏检 `acTL` | 低 | 默认 `headSize=4096`，APNG 的 `acTL` 通常在前 1KB；可配置 `AppSettings.PreviewHeadSize` 增大 |
| `GifDecoder` 类名误导维护者 | 中 | 加 XML 注释说明通用性；后续重构重命名 |
| 极大尺寸 APNG 解码内存压力 | 低 | 复用现有 1920px 宽度限制逻辑（`ShowImage` 中已有） |

---

## 附录：AVIF 动画支持难度调查（初步）

### 现状
- **静态 AVIF**：SkiaSharp 3.x 完全支持（`SKCodec` 识别 `ftyp` box brand `avif`）
- **动画 AVIF**：
  - SkiaSharp 3.119.4 **不确定** 支持程度
  - `SKCodec.FrameCount` 对 AVIF 动画可能返回 1（仅首帧）
  - 底层 Skia 对 AVIF 动画（`av01`/`avis` brand + `mif1` track）支持较新版本才完善

### 可行路径对比

| 方案 | 难度 | 优点 | 缺点 |
|------|------|------|------|
| **方案 A：等待 SkiaSharp 升级** | 低（等待） | 统一管线，零新依赖 | 不确定时间线 |
| **方案 B：引入 `SixLabors.ImageSharp` + `ImageSharp.Formats.Avif`** | 中 | 成熟支持动画 AVIF | 新增 NuGet 依赖（~500KB）、需编写独立解码器 |
| **方案 C：FFmpeg 进程外解码** | 高 | 格式支持最全 | 重依赖、部署复杂、启动慢 |
| **方案 D：静态回退** | 低 | 立即可用、零风险 | 动画 AVIF 仅显示首帧 |

### 建议策略

1. **Phase 1（现在）**：实施 APNG，**静态 AVIF 归入 `PreviewType.Image`**（复用 `FileFormat.Avif` → `Image` 映射）
2. **Phase 2**：升级 SkiaSharp 至 3.120+ 或引入 ImageSharp，测试 `SKCodec.FrameCount` 对动画 AVIF 表现
3. **Phase 3**：若原生支持 → 同 APNG 复用 `AnimatedImage`；不支持 → 引入 ImageSharp 实现 `AvifDecoder`（参考 `GifDecoder` 模式）

**预估工作量**：
- 静态 AVIF：~1 小时（仅枚举+映射+魔数）
- 动画 AVIF（原生支持）：~0 小时（自动复用）
- 动画 AVIF（ImageSharp 方案）：~4-6 小时（新解码器+集成测试）

---

**设计文档版本**：v1.0  
**创建日期**：2026-09-25  
**状态**：待评审