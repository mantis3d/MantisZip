# APNG 动画预览支持

## TL;DR

为预览系统添加 APNG (Animated PNG) 动画支持。复用现有 `PreviewType.AnimatedImage` 管线（GIF + Animated WebP 已统一），零破坏性变更，扩展性优先设计。

> **状态**: ✅ 已完成 | **阶段**: [✅✅✅✅] (4/4)
> **依赖**: 无（可独立实施）

---

## 任务清单

- [x] **1. Core 枚举扩展** — `FileFormat.Apng`、`FileFormatHelper.GetDisplayName`、"APNG 动画" 显示名
- [x] **2. 魔数检测** — `FileFormatDetector.Detect` 扫描 PNG 后的 `acTL` chunk 识别 APNG
- [x] **3. 预览映射** — `MapFileFormatToPreviewType` + `ClassifyPreview` 映射到 `AnimatedImage`
- [x] **4. 验收测试** — 单元测试覆盖魔数检测、预览分类、播放功能
- [x] **5. APNG 解码器实现** — `ApngDecoder` (ImageSharp) 集成 `ShowGif`，修复 Rational 类型转换

---

## 动机

当前预览系统已统一支持 GIF 和 Animated WebP（共用 `AnimatedImage` + `ShowGif` 通用播放管线）。APNG 作为 PNG 的动画扩展，完全兼容现有管线：
- `SKCodec` 原生支持 APNG（同 PNG 魔数，`FrameCount > 1` 标识动画）
- 帧解码、延时播放、循环逻辑与 GIF 完全一致
- 工具栏能力（缩放、透明背景、动画控制）已就绪

仅需在**格式识别层**区分 PNG/APNG，即可零成本启用动画预览。

---

## 架构

### 数据流

```
用户选中 .apng 条目
    ↓
PreviewService.ClassifyPreviewByMagicAsync
    ↓ (ExtractHeadAsync 读取头部)
FileFormatDetector.Detect → FileFormat.Apng  (NEW: acTL chunk 检测)
    ↓
MapFileFormatToPreviewType → PreviewType.AnimatedImage  (复用现有)
    ↓
PreviewViewModel.ShowImage(filePath)
    ↓ (SKCodec.Create → FrameCount > 1)
ShowGif(filePath)  // 完全复用现有动画管线
    ↓
GifDecoder.DecodeFrames → List<GifFrameData> (通用帧容器)
    ↓
DispatcherTimer 播放动画
```

### 复用点一览

| 组件 | 现状 | APNG 复用 |
|------|------|-----------|
| `PreviewType.AnimatedImage` | 已存在 | ✅ 直接复用 |
| `PreviewCapabilities` | 已注册 AnimationControls | ✅ 自动获得播放/暂停/逐帧/循环 |
| `ShowImage` → `ShowGif` 分流 | `FrameCount > 1` 自动分流 | ✅ APNG 同理自动走动画管线 |
| `GifDecoder` / `ShowGif` | 通用帧解码 + 播放 | ✅ **完全复用**（SKCodec 统一解码） |
| `MetadataKeys.FrameCount` | 已注册 | ✅ 信息面板自动显示帧数 |

---

## 详细设计

### 1. FileFormat 枚举扩展

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

### 2. 魔数检测：APNG 识别算法

APNG 文件结构：
```
PNG 签名 (8 bytes): 89 50 4E 47 0D 0A 1A 0A
IHDR chunk (25 bytes)
[可选] acTL chunk (8+ bytes): 动画控制块，标识 APNG
```

**检测策略**：
1. 匹配 PNG 签名 (8 bytes)
2. 跳过 IHDR chunk（固定长度 25 bytes）
3. 扫描后续 chunk，发现 Type == "acTL" (0x6163544C) → 确认为 APNG
4. 扫描上限：前 64KB（覆盖 IHDR + acTL + 可能的 PLTE）

```csharp
// FileFormatDetector.Detect 新增逻辑
private static bool ScanForActlChunk(byte[] head, int length)
{
    // 从 offset=8 (PNG签名后) 开始遍历 chunks
    // Length(4) + Type(4) + Data + CRC(4) 大端序
    // 直到 IEND 或超出 length 或超出 64KB
}
```

### 3. 映射与分类表更新

```csharp
// PreviewService.MapFileFormatToPreviewType
FileFormat.Apng => PreviewType.AnimatedImage,  // NEW

// PreviewService.ClassifyPreview (扩展名兜底)
private static readonly string[] ApngExtensions = { ".apng" };
if (ApngExtensions.Contains(ext)) return PreviewType.AnimatedImage;  // NEW
```

### 4. 显示名

```csharp
// FileFormatHelper.GetDisplayName
FileFormat.Apng => "APNG 动画",
```

### 5. 预览能力注册

**无需变更** —— `AnimatedImage` 已注册：
```csharp
Register(PreviewType.AnimatedImage,
    PreviewCapability.Zoom | PreviewCapability.Transparency | PreviewCapability.AnimationControls);
```

---

## 扩展性设计（为 AVIF 等后续格式铺路）

### 标准接入清单（新增动画格式仅需 5 步）

后续新增动画格式（AVIF 动画、JXL 动画等）只需：

1. `FileFormat` 新增枚举值
2. `FileFormatDetector` 新增魔数检测
3. `MapFileFormatToPreviewType` 映射到 `AnimatedImage`
4. `ClassifyPreview` 扩展名映射
5. `FileFormatHelper.GetDisplayName` 显示名

**无需** 修改 `PreviewViewModel`、`PreviewCapabilities`、播放逻辑。

### 静态图回退机制

若某格式既有静态版也有动画版（如 AVIF），`SKCodec.FrameCount` 运行时判定：
- `FrameCount == 1` → `ShowImage` 静态管线（`PreviewType.Image`）
- `FrameCount > 1` → `ShowGif` 动画管线（`PreviewType.AnimatedImage`）

**魔数检测阶段无需区分动态/静态**，统一映射到基础格式，运行时由 `FrameCount` 分流。

---

## 实施步骤

| 步骤 | 文件 | 变更 | 状态 |
|------|------|------|------|
| 1 | `src/MantisZip.Core/Utils/FileFormatInfo.cs` | 新增 `Apng` 枚举 | ✅ 完成 |
| 2 | `src/MantisZip.Core/Utils/FileFormatDetector.cs` | 新增 `ScanForActlChunk` + PNG 分支调用 | ✅ 完成 |
| 3 | `src/MantisZip.Core/Utils/FileFormatHelper.cs` | 新增显示名 "APNG 动画" | ✅ 完成 |
| 4 | `src/MantisZip.UI.Avalonia/Services/PreviewService.cs` | `MapFileFormatToPreviewType` + `ClassifyPreview` 扩展名 | ✅ 完成 |
| 5 | `src/MantisZip.UI.Avalonia/Services/ApngDecoder.cs` | **新增** ImageSharp 解码器 | ✅ 完成 |
| 6 | `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs` | `ShowGif` 集成 APNG 解码器 | ✅ 完成 |
| 7 | `src/MantisZip.UI.Avalonia/Localization/strings.*.json` | 本地化键 `Preview_Header_Apng` | ✅ 完成 |

---

## 验收标准

1. **魔数识别**：含 `acTL` chunk 的 PNG 文件被识别为 `FileFormat.Apng`
2. **预览分类**：`PreviewType.AnimatedImage`
3. **播放功能**：帧动画正常播放、暂停、逐帧导航、循环
4. **工具栏**：显示缩放、透明背景、动画控制按钮
5. **信息面板**：显示 "APNG 动画"、尺寸、帧数
6. **扩展名兜底**：`.apng` 文件无魔数时也能正确分类
7. **不回归**：现有 GIF、Animated WebP、静态 PNG 预览完全正常

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

1. **Phase 1（本任务）**：实施 APNG，**静态 AVIF 同步接入**（`FileFormat.Avif` → `PreviewType.Image`）
2. **Phase 2**：升级 SkiaSharp 至 3.120+ 或引入 ImageSharp，测试 `SKCodec.FrameCount` 对动画 AVIF 表现
3. **Phase 3**：若原生支持 → 同 APNG 复用 `AnimatedImage`；不支持 → 引入 ImageSharp 实现 `AvifDecoder`

**预估工作量**：
- 静态 AVIF：~1 小时（仅枚举+映射+魔数）
- 动画 AVIF（原生支持）：~0 小时（自动复用）
- 动画 AVIF（ImageSharp 方案）：~4-6 小时（新解码器+集成测试）

---

## Definition of Done

- [x] `FileFormat.Apng` 枚举值新增且编译通过
- [x] `FileFormatDetector.Detect` 正确识别 APNG 魔数（含 `acTL` chunk）
- [x] `MapFileFormatToPreviewType` 映射 `Apng → AnimatedImage`
- [x] `ClassifyPreview` 扩展名 `.apng → AnimatedImage`
- [x] `FileFormatHelper.GetDisplayName` 返回 "APNG 动画"
- [x] 单元测试覆盖 APNG 魔数检测（正例/反例/含 PLTE）
- [x] 手动验证：APNG 文件预览播放正常、工具栏显示正确、信息面板显示正确
- [x] `dotnet build` 通过，无回归
- [x] 现有 GIF、Animated WebP、静态 PNG 预览无回归

### Final Checklist

- [x] APNG 魔数检测正确（含 `acTL` chunk 的 PNG → Apng，不含 → Png）
- [x] 预览类型正确映射到 `AnimatedImage`
- [x] 动画播放功能完整（播放/暂停/上帧/下帧/帧输入/循环）
- [x] 工具栏按钮：缩放、透明背景、动画控制均显示
- [x] 信息面板显示 "APNG 动画" + 尺寸 + 帧数
- [x] 现有 GIF、Animated WebP、静态 PNG 预览无回归
- [x] 核心/单测 509 通过