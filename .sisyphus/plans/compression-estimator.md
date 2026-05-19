# 压缩预估 (Compression Estimator)

> 选好文件后、实际压缩前，快速估算各格式/级别的最终大小
> 状态: 📋 待定（独立功能，不依赖其他系统）

---

## 动机

用户压缩文件时面临一个盲选问题：

- 选 ZIP 还是 7z？
- 用级别 5 还是级别 9？
- 不分卷够不够？要不要分 100MB 一卷？

现在唯一的办法就是直接压一遍看结果——不行再换。大文件一次压缩几分钟，试错成本很高。

**目标**：在 `CompressSettingsWindow` 中，选好文件和格式后，显示一个预估大小表格，让用户在点击「压缩」前就知道预期结果。

---

## 架构设计

### 用户界面

在 `CompressSettingsWindow` 底部添加预估面板：

```
┌─────────────────────────────────────────┐
│  压缩预估值                              │
│  ┌─────────────────────────────────┐    │
│  │ 格式      级别   预计大小   耗时   │    │
│  │ ─────────────────────────────── │    │
│  │ ZIP       5      12.3 MB   ~2s  │    │
│  │ ZIP       9      11.1 MB   ~4s  │    │
│  │ 7z        5       8.7 MB   ~8s  │    │
│  │ 7z        9       7.2 MB  ~15s  │    │
│  │ Tar.gz    5      13.8 MB   ~3s  │    │
│  └─────────────────────────────────┘    │
│  [刷新预估]                              │
└─────────────────────────────────────────┘
```

### 预估策略

分三级精度，用户可在设置中选择：

| 精度 | 方法 | 速度 | 误差 | 适用场景 |
|------|------|------|------|---------|
| 快速 | 按文件类型查经验表 | 瞬间 | ±30% | 初次打开窗口时自动显示 |
| 标准 | 每个格式实际压缩采样（前 1MB） | ~3s | ±10% | 用户点击「刷新预估」 |
| 精确 | 完整压缩但不写出（Stream.Null） | 等同真实压缩 | ±1% | 不确定且文件不大时 |

### 经验系数表 (快速模式)

```csharp
public static class CompressionCoefficients
{
    // 格式 × 类型 → 预估压缩率
    // 值 = 压缩后大小 / 原始大小
    private static readonly Dictionary<(ArchiveFormat, string), double> _rates = new()
    {
        // 文本类
        { (ArchiveFormat.Zip, "text"),      0.15 },
        { (ArchiveFormat.SevenZip, "text"), 0.08 },
        { (ArchiveFormat.Tar, "text"),      1.00 }, // Tar 不压缩

        // 代码/脚本
        { (ArchiveFormat.Zip, "code"),      0.25 },
        { (ArchiveFormat.SevenZip, "code"), 0.12 },

        // 图片（无损压缩格式 = PNG/BMP）
        { (ArchiveFormat.Zip, "image_lossless"),  0.85 },
        { (ArchiveFormat.SevenZip, "image_lossless"), 0.80 },

        // 图片（有损 = JPG/WebP — 基本压不动）
        { (ArchiveFormat.Zip, "image_lossy"),     0.99 },
        { (ArchiveFormat.SevenZip, "image_lossy"), 0.98 },

        // 已压缩多媒体（MP4/MP3 — 压不动）
        { (ArchiveFormat.Zip, "media"),     1.00 },
        { (ArchiveFormat.SevenZip, "media"), 0.99 },

        // 二进制/可执行
        { (ArchiveFormat.Zip, "binary"),    0.60 },
        { (ArchiveFormat.SevenZip, "binary"), 0.45 },

        // 压缩包（已压缩数据 — 压不动）
        { (ArchiveFormat.Zip, "archive"),   1.00 },
        { (ArchiveFormat.SevenZip, "archive"), 0.99 },
    };

    /// <summary>根据文件扩展名判定类型。</summary>
    public static string ClassifyByExtension(string fileName);
}
```

---

## 改动范围

涉及 **5 个文件**：

| 文件 | 改动 | 预估工时 |
|------|------|---------|
| `Core/Utils/CompressionEstimator.cs` | 🆕 新增 — 三级预估算法 | 3h |
| `UI/CompressSettingsWindow.xaml` | 添加预估面板 UI | 30min |
| `UI/CompressSettingsWindow.xaml.cs` | 集成预估逻辑 + 刷新按钮 | 1h |
| `UI/MainWindow.xaml.cs` | 压缩前显示预估（可选步骤） | 15min |
| 测试项目 | `CompressionEstimatorTests` | 30min |

**运行时依赖变更：** 无

---

## 实现细节

### 核心接口

```csharp
public static class CompressionEstimator
{
    /// <summary>快速预估（经验公式，不碰文件）。</summary>
    public static EstimateResult QuickEstimate(
        string[] sourcePaths, ArchiveFormat format);

    /// <summary>标准预估（采样压缩，更准确）。</summary>
    public static Task<EstimateResult> StandardEstimateAsync(
        string[] sourcePaths, ArchiveFormat format, int level,
        CancellationToken ct = default);

    /// <summary>获取所有常见格式的全部预估值，用于表格展示。</summary>
    public static Task<List<EstimateRow>> EstimateAllAsync(
        string[] sourcePaths,
        CancellationToken ct = default);
}

public class EstimateResult
{
    public ArchiveFormat Format { get; set; }
    public int Level { get; set; }
    public long EstimatedSize { get; set; }
    public string EstimatedSizeDisplay => FormatFileSize(EstimatedSize);
    public double Confidence { get; set; }  // 0.0 ~ 1.0
    public string Method { get; set; }      // "quick" / "standard"
    public TimeSpan EstimatedDuration { get; set; }
}

public class EstimateRow
{
    public string FormatLabel { get; set; }  // "ZIP"
    public int Level { get; set; }           // 5
    public string SizeDisplay { get; set; }  // "12.3 MB"
    public string DurationDisplay { get; set; } // "~2s"
    public bool IsRecommended { get; set; }  // 标记推荐组合
}
```

### 采样压缩实现 (StandardEstimateAsync)

```csharp
// 对每个源文件，取前 sampleSize 字节压缩，外推整体
const int sampleSize = 1 * 1024 * 1024; // 1MB

long totalSize = sourcePaths.Sum(GetFileSize);
long sampleTotal = 0;
long compressedSampleTotal = 0;

foreach (var path in sourcePaths)
{
    using var fs = File.OpenRead(path);
    int toRead = (int)Math.Min(sampleSize, fs.Length);
    byte[] sample = new byte[toRead];
    await fs.ReadAsync(sample, 0, toRead, ct);

    int compressedLen = CompressSample(sample, format, level);
    sampleTotal += toRead;
    compressedSampleTotal += compressedLen;
}

double ratio = (double)compressedSampleTotal / sampleTotal;
long estimatedSize = (long)(totalSize * ratio);
```

`CompressSample` 使用 `MemoryStream` + 对应的引擎流（`ZipOutputStream` / 模拟），只压缩到内存，不写磁盘。

### 显示时机

- `CompressSettingsWindow` 加载时自动运行 `QuickEstimate`
- 用户选择文件/格式变更时自动重新估算（延迟 500ms 防抖）
- 用户可手动点击「刷新预估」触发 `StandardEstimateAsync`
- 不阻塞 UI（`async` + 后台线程）

---

## 风险

| 风险 | 等级 | 对策 |
|------|------|------|
| 采样低估误差（前 1MB 可压但后面不可压） | 🟡 | 备注说明「预估值仅供参考」；加粗误差范围 |
| 7z 采样需调 7z.exe，无法纯内存 | 🟡 | 7z 的采样回退到经验公式；或只对 ZIP/Tar 做实际采样 |
| 超大文件源（TB 级），扫描耗时 | 🟢 | 限制扫描文件数（默认 10000）；超大文件跳过采样 |
| 预估耗时本身太长 | 🟢 | 默认仅快速模式；标准模式需用户手动触发 |

---

## 后续扩展

- **分卷推荐**：根据预估大小自行推荐分卷策略
- **格式推荐**：根据文件类型自动推荐最佳格式（如纯文本 → 7z，图片 → ZIP store）
- **批量估算**：选中多个目录，批量显示「总空间节省」摘要
- **自适应压缩级别 (Adaptive Compression Level)** — 见下方

---

## 自适应压缩级别 (Adaptive Compression Level)

> 状态: 📋 待定  
> 依赖: `CompressionCoefficients.ClassifyByExtension()`（本计划已设计）

### 动机

JPG/PNG/MP4/ZIP/7z 等已压缩格式，使用高压缩级别几乎不减小体积，却消耗大量 CPU。
自动检测此类文件并降为 Store(0) 或 Fast(1)，可显著提速而不影响最终包大小。

**量化**：
- 混合目录（代码 + 图片）：压缩速度提升 **40-60%**，包大小几乎不变
- 纯图片目录：速度提升 **5-10 倍**（全部走 Store）
- 纯文本目录：无影响

### 分类策略（分两级，按文件大小切换）

| 文件大小 | 策略 | 开销 | 准确度 |
|---------|------|------|--------|
| ≤ 64KB（小文件） | 仅扩展名查表 | 零开销 | 90% |
| > 64KB（大文件） | 魔数识别 + 可选采样试压 | ~1ms/文件 | 99% |

**为什么小文件只用扩展名**：
- 1000 个 2KB 小文件，扩展名查表总耗时 < 1ms
- 即使偶有误判（如 `.docx` 改名 `.txt`），2KB 用错了级别也无所谓——多花的 CPU 不到 1ms
- 精度的投入应集中在大文件上（一个 500MB `.iso` 用错级别=浪费几十秒）

### 配置项（AppSettings → 压缩 标签页）

```
自适应压缩级别:
  ○ 禁用（始终使用选定级别）
  ○ 仅对已知格式自动降级（扩展名查表）← 默认推荐
  ○ 智能检测（大文件魔数 + 采样试压，适合追求极致准确）
```

`禁用` 时行为与现在一致；`仅已知格式` 时零额外开销。

### 引擎改动

- `ArchiveOptions` 增加 `AdaptiveCompressionLevel`（三态枚举）
- `ZipEngine`：`PutNextEntry` 前按文件切换 `SetLevel`
- `TarGzEngine`：同上，`SetLevel` 前切换
- `SevenZipEngine`：按级别分组文件，多次 `7z u` 增量更新

### 与预估器的关系

`CompressionCoefficients.ClassifyByExtension()` 可作为分类基础（扩展名 → 文本/代码/图片有损/图片无损/媒体/压缩包/二进制），
当分类结果为 `image_lossy` / `media` / `archive` 时自动降级。
