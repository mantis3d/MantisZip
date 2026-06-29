# Avalonia 迁移下的预览系统机会分析

> **状态**: 🔍 调研 | **创建日期**: 2026-06-29
> **前置依赖**: `preview-extended-formats.md`（所有 Phase 0–5 已完成或已规划）
> **关联计划**: `cross-platform-port.md`（WPF→Avalonia 整体迁移策略）

## TL;DR

WPF→Avalonia 迁移不仅仅是"换个 UI 框架"。Avalonia 的 **Skia 原生渲染管线** 和 **跨平台架构** 为 MantisZip 的预览系统带来了 WPF 无法实现的新能力，同时也暴露了 WPF 独占依赖的短板。

**核心发现**：
- **SVG 预览**从 WebView2 兜圈子变为原生控件一行搞定
- **自定义渲染**（字体样本、图标网格、HDR 全景）从 WPF 繁琐的 interop 变为 Skia 原生直出
- **拖拽导出**摆脱 WPF OLE bridge 的 bug 限制
- **音视频播放**失去 WPF MediaElement，需寻找替代方案

---

## 1. 预览系统冲击简表

以 `preview-extended-formats.md` 的 Phase 划分维度，分析迁移到 Avalonia 后每个功能的难度变化：

| 变化 | 功能 | 原因 |
|:----:|------|------|
| ✅ **显著变易** | SVG 预览（Phase 3.1） | WebView2 → `Avalonia.Svg.Skia` 原生控件 |
| ✅ **变易** | PDF 第一页渲染（Phase 2A.3）、Magick.NET 解码（Phase 2D） | Skia 是 Avalonia 渲染后端，Bitmap 直出，无需 WPF BitmapSource 互转 |
| ✅ **变易** | 自定义绘制（字体、图标网格、文件树等） | Avalonia `SKCanvasView` 原生控件，零 interop 开销 |
| 🟢 **持平** | 纯 C# 元数据解析（PE/Torrent/SQLite/ISO/Office 等） | 框架无关，UI 层只是 TextBlock/DataGrid 换控件名 |
| 🟡 **略难** | GIF 动画（Phase 0.3） | WpfAnimatedGif → Avalonia.Labs.Gif（较新但功能够用） |
| 🔴 **显著变难** | **音视频播放**（Phase 3.10/3.11） | WPF MediaElement 一行搞定 → **Avalonia 无内置**，需 LibVLCSharp（+~30MB）|

---

## 2. 新格式预览方案分析

以下分析针对 `preview-extended-formats.md` 尚未覆盖的新格式，以及需要重估的格式。

### 2.1 PSD (Photoshop) — ✅ Magick.NET 原生

**方案 A：Magick.NET（推荐，纳入 Phase 2D）**

ImageMagick 原生支持 PSD 格式（解析图层合成、通道数据），零额外依赖。

```csharp
using var image = new MagickImage("file.psd");
PreviewImage.Source = image.ToBitmap();  // 自动合成所有可见层
```

**方案 B：PsdSharp（专用库，有 Avalonia 原生控件）**

`PsdSharp.Avalonia` NuGet 包提供专门的 `PsdView` 控件，直接渲染 PSD 文件：

```xml
<psdsharp:PsdView x:Name="PsdView" />
```

```csharp
PsdView.PsdFile = File.OpenRead("image.psd");
```

**对比**：

| 维度 | Magick.NET | PsdSharp |
|------|-----------|----------|
| 集成方式 | `image.ToBitmap()` 一行 | 独立控件 PsdView |
| 依赖体积 | +~20MB（但已有 Phase 2D） | +~500KB 纯托管 |
| 精度 | 合成后的展平图像 | 逐图层信息可访问 |
| 复杂度 | 直接纳入现有 MagickExtensions 分支 | 新增独立预览分支 |
| 覆盖格式 | PSD + 200+ 其他格式 | 仅 PSD/PSB |

**建议**：用 Magick.NET 做 PSD 预览（零额外集成成本，Phase 2D 已有），PsdSharp 作为未来"显示图层列表"增强时的备选。

### 2.2 AI (Illustrator) — ⚠️ 需 Ghostscript

- `.ai`（CS2+）= 内部是 PDF 包装
- `.ai`（CS1-）= 基于 EPS（PostScript）

Magick.NET 支持 AI 输入，**但必须安装 Ghostscript** 来渲染 PostScript/PDF 内容。

```csharp
using var image = new MagickImage("file.ai");
// 如果系统没装 Ghostscript → MagickException
PreviewImage.Source = image.ToBitmap();
```

**建议**：
1. 纳入 MagickExtensions 但标注 `RequiresGhostscript`
2. 设置页增加 Ghostscript 路径配置（参考现有 `SevenZipPath` 设计）
3. 无 GS 时友好提示"需要安装 Ghostscript 以预览 AI 文件"
4. 不阻塞其他格式，纯属锦上添花

### 2.3 HDR（Radiance RGBE） — ✅ Magick.NET 原生 + 全景机会

Magick.NET 原生支持 `.hdr` / `.pic` / `.rgbe`，零额外依赖。

```csharp
using var image = new MagickImage("file.hdr");
// Magick.NET 自动做 Reinhard tone-mapping
PreviewImage.Source = image.ToBitmap();
```

**⚠️ 重要**：推荐使用 `Q16-HDRI` 版本的 Magick.NET（计划已选），因为 HDR 使用 32-bit 浮点通道，Q8/Q16 的整数精度在 tone-map 前会损失动态范围。

---

## 3. HDR 全景 360° 查看器（新功能提案）

这是 Avalonia 迁移后**新增的能力**，WPF 下虽然也可行但 Avalonia 能做得更好。

### 3.1 什么是 HDR 全景查看？

`.hdr` 文件中很多是 equirectangular 投影的 360° 环境贴图（常用于 3D 渲染、游戏引擎、建筑可视化）。普通图片预览只能看到"拉扁的球形"，用户无法直观感受全景内容。

### 3.2 方案 A：WebView2 + Three.js（最快路径）

复用现有 WebView2 预览架构，嵌入一个 HTML 页面：

```html
<script type="importmap">
{ "imports": { "three": "..." } }
</script>
<script type="module">
import * as THREE from 'three';
import { RGBELoader } from '...';
import { OrbitControls } from '...';

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(75, viewportRatio, 0.1, 1000);
const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(0, 0, 0);

const loader = new RGBELoader();
loader.load('panorama.hdr', (texture) => {
    texture.mapping = THREE.EquirectangularReflectionMapping;
    scene.background = texture;
});
</script>
```

Three.js 的 `RGBELoader` **原生支持 Radiance HDR 格式**，`OrbitControls` 提供鼠标拖拽旋转/缩放。

**集成步骤**：
1. 在 `mainWindow.Preview.cs` 新增 `.hdr` 扩展名分支
2. 提取完整 HDR 文件到 temp（通常 < 20MB）
3. 构建内嵌 HTML（含 Three.js CDN 或本地 bundled 版本）
4. WebView2.NavigateToString(html) 或 Navigate(localHtmlFile)
5. 检测 WebView2 就绪后，通过 `ExecuteScriptAsync` 传入文件路径

**预估**：~4h

| 工作项 | 预估 |
|--------|------|
| HTML + Three.js 全景查看器 | ~2h |
| C# 侧集成（HDR 识别 + 提取 + WebView2 ) | ~1.5h |
| 工具栏按钮（重置视角/曝光调节） | ~0.5h |

### 3.3 方案 B：SkiaSharp 自渲染 360° 查看器（Avalonia 原生优势）

原理：将 equirectangular HDR 贴图映射到虚拟球体上，用鼠标拖拽改变视角。

```
对于输出画面的每个像素 (x, y):
  1. 根据相机旋转 (yaw, pitch) 计算射线方向向量
  2. 将射线方向转为球坐标 (theta, phi)
  3. 在 HDR 全景图上采样对应像素（双线性插值）
  4. 应用 tone-mapping（Reinhard / ACES）
  5. 写入输出 bitmap
```

Avalonia 上实现为自定义 `SKCanvasView` 控件：

```csharp
public class HdrPanoramaView : SKCanvasView
{
    private SKBitmap? _panorama;
    private float _yaw, _pitch, _fov = 75;
    private SKPoint _lastMousePos;

    // 逐帧渲染
    protected override void OnPaint(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        // 方式 A：CPU 逐像素（慢，但简单）
        RenderPanoramaCPU(canvas, info);

        // 方式 B：GPU shader（推荐，~60fps）
        RenderPanoramaSKSL(canvas, info);
    }
}
```

**方式 B（推荐）用 SKSL（Skia Shader Language）在 GPU 上跑**：

```glsl
// panorma.sksl
uniform shader uPanorama;
uniform float2 uRotation;  // x=yaw, y=pitch
uniform float uFov;

half4 main(float2 fragCoord) {
    // 计算射线方向
    float2 uv = fragCoord / uScreenSize * 2 - 1;
    float3 ray = normalize( /* projection matrix */ );

    // 应用旋转
    ray = rotateYawPitch(ray, uRotation);

    // equirectangular 采样
    float theta = atan2(-ray.z, ray.x);
    float phi = acos(ray.y);
    float2 panoramaUV = float2(theta / 6.2832, phi / 3.1416);

    return uPanorama.eval(panoramaUV);
}
```

**为什么 Avalonia 比 WPF 更适合这个方案？**

| 维度 | WPF | Avalonia |
|------|-----|----------|
| Skia 集成 | `SkiaSharp.Views.WPF.SKElement` — 非原生，通过 HwndHost interop | `SKCanvasView` — 原生控件，渲染管线直通 GPU |
| 逐像素更新 | WriteableBitmap → CopyPixels (CPU→GPU 拷贝) | SKCanvas → GPU shader，零拷贝 |
| 渲染性能 | 30fps 左右（WPF 渲染线程瓶颈） | 60fps 轻松（Skia 直接调用 GPU） |
| 色彩管理 | WPF WIC 管线老旧，HDR 色彩空间支持差 | Skia 的 F16/N16 色彩模式原生支持 HDR |

**预估**：~12h

| 工作项 | 预估 |
|--------|------|
| 核心像素着色器（SKSL raytrace + equirectangular 采样） | ~4h |
| 交互控制（拖拽旋转、滚轮缩放、惯性） | ~2h |
| Tone-mapping 调节（曝光、Reinhard/ACES 切换） | ~2h |
| Avalonia 控件封装 + 预提取逻辑 | ~2h |
| 工具栏（视角重置、FOV 滑块、曝光滑块） | ~1h |
| 信息面板（分辨率、曝光值） | ~1h |

### 3.4 方案对比

| 维度 | WebView2 + Three.js（方案 A） | SkiaSharp 自渲染（方案 B） |
|------|-----------------------------|--------------------------|
| 实现时间 | ~4h | ~12h |
| 渲染质量 | 好（Three.js 成熟） | 更好（GPU shader 直出） |
| HDR 动态范围 | 内置 tone-map | 可自由控制（多算法切换） |
| 帧率 | 60fps | 60fps |
| 交互 | OrbitControls 全部自带 | 需手写拖拽/缩放/惯性 |
| 额外依赖 | WebView2（已有） | 无 |
| 可定制性 | 低（JS 侧改） | 高（全部 C# 控制） |
| WPF 也能做？ | ✅ 可以 | ⚠️ 有 interop 性能损失 |

**建议路线**：
1. **Phase 1**（快速出货）：方案 A WebView2 + Three.js，~4h，先让用户能用上
2. **Phase 2**（进阶）：方案 B SkiaSharp 自渲染，~12h，替代方案 A 获得更好的性能和 HDR 色彩控制

---

## 4. 音视频播放替代方案

这是迁移到 Avalonia 损失最大的功能。**需要外部依赖**。

### 4.1 方案对比

| 方案 | 格式覆盖 | 额外依赖 | 集成难度 | 跨平台 |
|------|---------|---------|---------|--------|
| **LibVLCSharp** | 几乎所有格式（VLC 引擎） | +~30MB（libvlc native） | 🟡 中（控件 + 事件） | ✅ Linux/macOS/Win |
| **FFMpegCore** | 几乎所有格式 | FFmpeg 二进制 | 🟡 中（进程外） | ✅（需自带 ffmpeg） |
| **WinRT MediaPlayer**（仅 Windows） | 系统解码器 | 无 | 🟢 低 | ❌ 仅 Win10+ |
| **Avalonia.MediaPlayer**（不存在） | — | — | — | — |

### 4.2 建议：LibVLCSharp

LibVLCSharp + Avalonia 集成的示例：

```csharp
// LibVLCSharp 有 Avalonia 支持
using LibVLCSharp.Shared;

var libVlc = new LibVLC();
var mediaPlayer = new MediaPlayer(libVlc);
mediaPlayer.Play(new Media(libVlc, tempFilePath));

// Avalonia 集成：VideoView 控件
// <vlc:VideoView Name="MyVideoView" />
```

已有 NuGet 包 `LibVLCSharp.Avalonia`，提供 `VideoView` 控件。

**初步预估**：

| 工作项 | 预估 |
|--------|------|
| LibVLCSharp 集成 + NuGet 引用 | ~1h |
| 音频预览模式（UI 简化版：播放/暂停/进度条/音量） | ~4h |
| 视频预览模式（VideoView + 控制条） | ~6h |
| 压缩包内大文件渐进式提取 + 播放（流式处理） | ~4h |
| 工具栏按钮 + 状态管理 | ~1h |
| **合计** | **~16h** |

---

## 5. GIF 动画替代方案

WPF 用的 `WpfAnimatedGif` 不适用于 Avalonia。

- **方案 A**（推荐）：`Avalonia.Labs.Gif` NuGet 包，功能基本够用
- **方案 B**：用 SkiaSharp 自己解码 GIF 帧，用 `WriteableBitmap` 逐帧显示（更可控但代码多）
- **方案 C**：Magick.NET 读取 GIF → 提取各帧 → 定时器轮换（简单但内存消耗大）

**建议**：先用方案 A，如果遇到兼容性问题回退方案 C（Magick.NET 已纳入 Phase 2D，零额外依赖）。

---

## 6. 拖拽导出能力提升

这是 README 已知问题中提到的迁移动机。WPF OLE bridge 的 bug（`IDataObject` 延迟渲染会崩 Explorer）是 WPF 自身的 bug，**Avalonia 不存在此问题**。

迁移到 Avalonia 后拖拽导出的架构可以简化：

```
当前（WPF）:
  MouseMove → 提取全部文件到 temp → DoDragDrop(已提取的文件路径)

Avalonia 可行:
  MouseMove → DoDragDrop(自定义 DataObject，延迟提取) 
    → 用户放到 Explorer → 调用 GetData() 时才解压
```

这意味着：
- **小文件**：拖拽后立即看到文件出现在 Explorer
- **大文件**：Explorer 逐块请求，逐步解压，用户体验大幅提升
- **不需要进度窗口**：用户看到的是"正在复制"的标准系统对话框

此项改造的收益取决于用户场景中大文件的频率。

> **注意**：此部分已由 `drag-drop-direct-extract.md` 覆盖，此处仅说明 Avalonia 移除了底层限制。

---

## 7. 实施建议

### 7.1 与 `preview-extended-formats.md` 的衔接

`preview-extended-formats.md` 的 Phase 0–3 已在 WPF 下完成，Phase 4 和 Phase 5 尚未开始。

**迁移期间的建议顺序**：

```
WPF Phase 4（高难度格式）       → 跳过，等 Avalonia 迁移后直接做
WPF Phase 5（元数据优先提取）   → Core 层可先行（纯 C#，框架无关）
Avalonia 迁移                  → 主力
   ├── Phase A: 基础框架迁移    → SVG 原生支持 + Magick.NET 集成（Phase 2D Avalonia 版）
   ├── Phase B: 高难度格式      → PE 图标、ICL、证书等（原 Phase 4）
   ├── Phase C: 音视频播放      → LibVLCSharp 集成
   ├── Phase D: HDR 全景查看器  → Three.js 快速版（4h）→ Skia 进阶版（12h）
   └── Phase E: Phase 5        → 元数据优先提取（Core 层已完成，UI 适配）
```

### 7.2 新功能的优先级建议

| 优先级 | 功能 | 难度 | 预估 | 说明 |
|--------|------|:----:|:----:|------|
| **P1** | SVG 原生预览 (`Avalonia.Svg.Skia`) | 🟢 低 | ~1h | 替换 WebView2，降低开销 |
| **P1** | Magick.NET 统一解码（Avalonia 适配） | 🟢 低 | ~2h | 包含 PSD + HDR 等 |
| **P2** | GIF 动画 (`Avalonia.Labs.Gif`) | 🟢 低 | ~1h | 替换 WpfAnimatedGif |
| **P2** | HDR 全景查看器 WebView2 版 | 🟡 中 | ~4h | 快速出货，Three.js |
| **P3** | 音视频播放 (LibVLCSharp) | 🔴 高 | ~16h | 损失最大的功能，最晚落地 |
| **P4** | HDR 全景查看器 Skia 自渲染版 | 🔴 高 | ~12h | 替代 WebView2 版，更好的体验 |
| **P4** | PSD 图层预览 (PsdSharp) | 🟡 中 | ~6h | Magick.NET 之外的可选增强 |

### 7.3 不建议在 WPF 下实施的功能

以下功能虽然 WPF 也能做，但迁移到 Avalonia 后会有质的提升，**建议等 Avalonia 迁移后再做**：

1. **HDR 全景 Skia 自渲染** — WPF 的 WIC 管线老旧，Skia 的 GPU shader 方案在 WPF 上 interop 性能损失大
2. **大幅优化自定义 DrawingVisual 渲染**（波形、频谱等） — Avalonia 的 `SKCanvasView` 原生快速
3. **拖拽延迟渲染** — WPF OLE bridge bug 导致不可行

---

## 8. 重大依赖的体积分析与分离方案

### 8.1 依赖规模总览

迁移到 Avalonia 后，预览系统涉及的第三方依赖按体积分级：

| 依赖 | NuGet 包 | 体积 | 涉及格式 | 类型 |
|------|---------|:----:|---------|------|
| **Magick.NET** | `Magick.NET-Q16-HDRI-AnyCPU` + `Magick.NET.AvaloniaMediaImaging` | ~28MB | TGA/HDR/EXR/TIFF/PSD + 200+ 其他格式 | 原生 DLL |
| **LibVLC** | `LibVLCSharp` + `libvlc` native 包 | ~30MB | 音视频播放（MP4/MP3/WAV/FLAC 等） | 原生 DLL |
| **SkiaSharp** | `SkiaSharp` | ~5MB | 已在 Avalonia 内置 | 原生 DLL |
| **Ghostscript** | 外部安装，非 NuGet | ~30MB | AI/EPS/PDF 渲染 | 外部 EXE |
| **Avalonia.WebView** | `Avalonia.WebView` | ~1MB | HTML/Markdown/SVG/PDF | 托管 DLL |
| 预览元数据解析器 | （纯 C# 自研） | 零额外 | PE/Torrent/SQLite/ISO/Office 等 | 托管代码 |

**主要矛盾**：Magick.NET + LibVLC 合计 **~60MB** 原生 DLL。如果直接捆绑，安装包体积从当前的 ~10MB 膨胀到 ~70MB+，远超用户可接受范围。

### 8.2 `preview-modular-providers.md` 方案的局限

已有计划 [preview-modular-providers.md](.sisyphus/plans/preview-modular-providers.md) 提出了将预览格式的数据读取器抽取为独立类库的方案，通过 `ITableDataProvider` 接口反射加载。

但这个方案只解决了 **managed 依赖** 的分离（如 SQLitePCLRaw ~2MB、DocumentFormat.OpenXml ~3MB），对 **原生 DLL**（Magick.NET.Native 的 20+ MB、libvlc 的 30+ MB）不够：

- 原生 DLL 必须存在于进程加载路径或 `DllImport` 搜索路径中
- 仅仅把 C# wrapper 放到独立项目里，原生 DLL 不装仍然跑不起来
- 不能像 managed DLL 那样随手反射加载，调用 `new MagickImage()` 时如果原生 DLL 缺失就直接 crash

### 8.3 方案设计：三级依赖隔离体系

```
依赖分级策略：

┌─────────────────────────────────────────────────────────────┐
│  一级：核心内置（安装包必带）                                 │
│  SkiaSharp（Avalonia 已有）                                  │
│  预览元数据解析器（纯 C#，零额外体积）                        │
├─────────────────────────────────────────────────────────────┤
│  二级：可选插件——安装时选择                                   │
│  Magick.NET（~28MB）— 提供小众图片格式 + PSD/HDR 预览      │
│  Inno Setup 复选框：□ 高级图片预览支持（推荐）              │
├─────────────────────────────────────────────────────────────┤
│  三级：按需下载——首次使用时提示                               │
│  LibVLC（~30MB）— 提供音视频播放                            │
│  Ghostscript（~30MB）— 提供 AI/EPS 预览                    │
│  点击"播放"时 → 弹窗提示下载 → 后台下载 native DLLs        │
└─────────────────────────────────────────────────────────────┘
```

#### 一级：核心内置（零额外体积）

- 所有纯 C# 元数据解析器（PE/Torrent/SQLite/ISO/Office/音频元数据/视频元数据）直接编译在主程序内
- 这些格式始终可用，不影响安装包体积
- 信息面板展示 + 纯文本/CSV 预览不需要任何额外依赖

#### 二级：安装时可选的插件包

基于 `preview-modular-providers.md` 的反射加载模式扩展，每个插件包含 **C# wrapper DLL + 原生 DLLs**：

```
MantisZip.Preview.Magick/
├── MantisZip.Preview.Magick.dll          ← C# wrapper（实现 IPreviewProvider）
├── Magick.NET-Q16-HDRI-AnyCPU.dll         ← C# bridge
└── native/                                ← 原生 DLL
    └── x64/
        └── Magick.Native-*.dll (~28MB)
```

加载方式——使用 `AssemblyLoadContext` 隔离加载：

```csharp
// 每个插件用自己的 AssemblyLoadContext，独立探测原生 DLL 路径
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDir;

    protected override Assembly Load(AssemblyName name)
    {
        // 先在自己目录找
        var path = Path.Combine(_pluginDir, name.Name + ".dll");
        if (File.Exists(path)) return LoadFromFile(path);
        // 回退到默认
        return Default.LoadFromAssemblyName(name);
    }

    // 原生 DLL 解析回调
    protected override IntPtr LoadUnmanagedDll(string unmanagedName)
    {
        var path = Path.Combine(_pluginDir, "native", "x64", unmanagedName + ".dll");
        if (File.Exists(path)) return LoadUnmanagedDllFromPath(path);
        return IntPtr.Zero;
    }
}
```

这样即使 Magick.NET 没安装，主程序也不会 crash——只是 `IPreviewProvider` 列表里没有 Magick 项。

**接口定义**（扩展自 `ITableDataProvider` 的概念）：

```csharp
// Core/Abstractions/IPreviewProvider.cs
public interface IPreviewProvider
{
    string PluginName { get; }              // "Magick.NET Advanced Image Support"
    string? Description { get; }            // "Adds support for PSD, HDR, EXR, TIFF..."
    long? EstimatedSizeBytes { get; }       // 用户决策参考

    IEnumerable<string> SupportedExtensions { get; }
    bool IsAvailable { get; }               // 原生 DLL 是否存在

    Task<PreviewResult?> TryPreviewAsync(Stream data, string extension, CancellationToken ct);
}

public class PreviewResult
{
    public Bitmap? Image { get; init; }     // 解码后的图片
    public Dictionary<string, string>? Metadata { get; init; }  // 信息面板数据
}
```

**插件发现**：启动时扫描 `plugins/` 子目录下的 `MantisZip.Preview.*.dll`，每个用自己的 `AssemblyLoadContext` 加载。加载失败（如原生 DLL 缺失）静默跳过，`IsAvailable` 返回 false。

**用户缺失插件时的体验**：当用户预览 `.psd` 文件且 Magick 插件未安装时，信息面板正常显示基本文件信息（大小、日期），内容区显示：

> 🔔 预览此格式需要安装「高级图片支持」插件
> [📥 下载并安装] → 打开 GitHub Releases 页面或启动内置下载

#### 三级：运行时按需下载

对于 **LibVLC**（~30MB）这种体积大、使用频率低的依赖，不适合塞进安装包也不适合预装在 `plugins/` 目录里。采用"首次使用提示下载"模式：

1. 用户点击音频/视频文件 → 检测到 LibVLC 原生 DLL 缺失
2. 弹窗："播放音视频需要下载 LibVLC 解码引擎（约 30MB）"
3. 用户确认 → 后台从 CDN/GitHub Releases 下载 → 解压到 `plugins/MantisZip.Preview.MediaPlayer/native/x64/`
4. 刷新插件列表 → 自动继续播放

**技术要点**：

- 下载用 `HttpClient` + 进度回调，在 StatusBar 或 ProgressWindow 显示
- 下载完成后触发 `AssemblyLoadContext` 重新加载对应插件
- 下载包用 7z 压缩，传输体积约 ~10MB
- Ghostscript 同理，但 GS 是系统级安装而非本地 DLL，提示用户自行安装

### 8.4 与 `preview-modular-providers.md` 的整合

| 概念 | `preview-modular-providers.md` 原方案 | 本方案扩展 |
|------|--------------------------------------|-----------|
| 分离对象 | managed DLL（SQLitePCLRaw 等） | 原生 DLL（Magick.NET.Native、libvlc） |
| 加载机制 | `Assembly.LoadFrom` + 目录扫描 | `AssemblyLoadContext` + 自定义原生 DLL 探测 |
| 接口 | `ITableDataProvider`（仅数据表） | `IPreviewProvider`（通用预览接口） |
| 分发 | Inno Setup 组件选择 | 安装组件选择 + 运行时按需下载 |
| 缺失提示 | "此格式预览模块未安装" | 安装 / 下载按钮 + 可选自动下载 |

建议合并两个计划的接口抽象，统一为 `IPreviewProvider`，覆盖从纯数据表到图片解码的所有预览类型。

### 8.5 实现工作量预估

| 工作项 | 预估 |
|--------|:----:|
| `IPreviewProvider` 接口设计 + `PreviewResult` 模型 | ~1h |
| `PluginLoadContext` 实现（支持原生 DLL 隔离加载） | ~3h |
| 插件发现机制（启动时扫描 plugins/ 目录） | ~2h |
| Magick.NET 插件化改造（将现有 Phase 2D 代码抽出到独立项目）| ~4h |
| 按需下载组件（HttpClient + 进度 + 解压） | ~4h |
| LibVLC 插件化 + 下载集成 | ~4h |
| 缺失插件时的友好提示 UI | ~2h |
| **合计** | **~20h** |

### 8.6 安装包体积最终预期

| 阶段 | 安装包体积 | 内容 |
|:----:|:--------:|------|
| 当前 WPF | ~10MB | 全部内置 |
| 捆绑 Magick.NET + LibVLC | ~70MB | ❌ 不可接受 |
| **三级隔离后（默认安装）** | **~12MB** | 核心 + 元数据预览 + GIF/文本/WebView2 |
| + 安装时选装 Magick | +~28MB | 高级图片格式（PSD/HDR/EXR/TIFF） |
| + 首次使用时下载 LibVLC | +~30MB（按需） | 音视频播放 |

### 8.7 其他注意事项

1. **Avalonia.WebView 差异**：Avalonia 的 WebView 控件实现与 WPF 版有 API 差异，HTML/Markdown/PDF 预览的 WebView2 交互代码可能需要重写。
2. **PicView 参考价值**：PicView（Avalonia 开源图片查看器）已实现 PSD/SVG/HEIC/RAW 等多种格式预览，可参考其 Magick.NET 集成方式。
3. **Ghostscript 降级策略**：AI/EPS 预览可检测 GS 是否存在，缺失时只显示文件名/大小等基本信息，不阻断其他功能。
