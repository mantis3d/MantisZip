# 压缩预估 (Compression Estimator)

> 选好文件后、实际压缩前，快速估算各格式/级别的最终大小
> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜] (0/5)

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
┌─────────────────────────────────────────────────────────┐
│  压缩预估值                                              │
│  ┌─────────────────────────────────────────────────┐    │
│  │ 格式   级别   预计大小             预计耗时       │    │
│  │              标准      自适应      标准  自适应   │    │
│  │ ─────────────────────────────────────────────── │    │
│  │ ZIP     5    12.3 MB  10.1 MB*   ~2s   ~1s     │    │
│  │ ZIP     9    11.1 MB  10.0 MB*   ~4s   ~1s     │    │
│  │ 7z      5     8.7 MB   7.9 MB*   ~8s   ~3s     │    │
│  │ 7z      9     7.2 MB   6.8 MB*  ~15s   ~4s     │    │
│  │ Tar.gz  5    13.8 MB  11.2 MB*   ~3s   ~1s     │    │
│  └─────────────────────────────────────────────────┘    │
│  * 自适应压缩: image_lossy/media/archive 自动降级 Store  │
│  [刷新预估]                                              │
└─────────────────────────────────────────────────────────┘
```

### 预估策略

分四级精度，用户可在设置中选择：

| 精度 | 方法 | 速度 | 误差 | 适用场景 |
|------|------|------|------|---------|
| 快速 | 按文件类型查经验表 + 扩展名 | 瞬间 | ±30% | 初次打开窗口时自动显示 |
| ~~中速（推荐保留）~~ | ~~字节熵过滤（见下文）~~ | ~~<10ms~~ | ~~±20%（仅作参考）~~ | ~~扩展名与采样之间的过渡层~~ |
| 标准 | 每个格式实际压缩采样（前 1MB） | ~3s | ±10% | 用户点击「刷新预估」 |
| 精确 | 完整压缩但不写出（Stream.Null） | 等同真实压缩 | ±1% | 不确定且文件不大时 |

> ⚠️ **中速（熵过滤）已从正式精度等级中移除，保留作为参考方案**。原因见下文分析。

---

### 熵过滤方案（参考，不推荐实际落地）

#### 基本原理

信息熵反映数据的随机程度：高熵 → 接近随机 → 难以压缩；低熵 → 强规律 → 压缩率高。

按字节级 Shannon 熵估算压缩率的理论思路：

```
H = - Σ p(i) · log₂(p(i))    （i = 0..255, p(i) = 字节值 i 的频率）
粗糙估计： 压缩率 ≈ 1 - (H / 8)，H 越高越压不动
```

#### 为何已从正式等级中移除

1. **样本量不足**：有效的字节级熵估计至少需要 ~64KB 样本（256 个可能值 × 250+ 次采样），这已经比魔数检测多两个数量级。读取开销与采样压缩已可相比。

2. **文件头系统偏差**——头部的熵不代表整体：
   - 压缩包头（ZIP local file header）：高度结构化 → 熵极低 → 误判为「可压」
   - JPEG 文件头（`FF D8 FF` + EXIF）：结构固定 → 熵低 → 误判为「可压」
   - 实际上 ZIP/JPEG 的有效载荷接近随机，几乎压不动

3. **熵只反映信息论下界，不反映实际算法**：
   - 熵为 7.0 的 PNG 用 ZIP 压 → 压缩率 1.01（反而变大）
   - 同样熵 7.0 的纯文本用 7z 压 → 压缩率 0.08（可压）
   - 差异来自 LZ77 匹配算法和字典大小，这些信息熵完全不反映

4. **实用精度不如扩展名查表**：实测中，按扩展名分类（text / image_lossy / media）加上经验系数，精度已比熵过滤更稳定。

#### 唯一可能的使用场景

如果未来做纯内存文件场景（无磁盘路径、无扩展名、仅 byte[]），可考虑用熵做粗略参考分类：

```
H > 7.5 → 很可能已压缩，给默认系数 0.99
H < 4.0 → 很可能文本/代码，给默认系数 0.20
两者之间 → 给中间值 0.60，备注「不可靠」
```

但即使在此场景，也**远不如直接采样 64KB 做真实压缩准确**——采样 64KB 真压耗时 ~10ms，比熵计算多不了多少，精度却高一个数量级。

---

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

## 任务清单

- [ ] **1. Core: `CompressionEstimator` 类** — 三级预估算法（快速/标准/精确）
- [ ] **2. Core: `CompressionHistoryStore` 类** — 学习型预估数据库（三级 key 策略：格式+文件大小分桶 + JSON 持久化）
- [ ] **3. Core: `CompressionCoefficients` 经验系数表** — 扩展名 → 类型 → 压缩率
- [ ] **4. UI: `CompressSettingsWindow` 预估面板** — XAML 布局 + 数据绑定
- [ ] **5. UI: 预估交互逻辑** — 自动检测 + 刷新按钮 + 防抖 + 学习型记录 hook
- [ ] **6. Test: 单元测试** — `CompressionEstimatorTests` + `CompressionHistoryStoreTests`
- [ ] **7. Core: `RuntimeEstimator` 类** — 加权融合实时 ETA 算法，支持先验速度初始化
- [ ] **8. Core: `BatchRuntimeEstimator` 类** — 批处理模式下两个层级的 ETA 计算（当前包实时 + 待处理包预估值）
- [ ] **9. UI: `ProgressWindow` 时间信息行** — XAML 布局 + 时间格式化 + 防抖
- [ ] **10. UI: ETA 集成逻辑** — `App.Compress.cs` / `CompressService` 传递预估数据到 ProgressWindow，批处理模式支持
- [ ] **11. Test: `RuntimeEstimatorTests`** — 速度推算 + 加权融合 + 批处理汇总
- [ ] **12. Core: 格式目录 FormatCatalog** — `FormatDefinition` 模型 + 内置格式注册 + 自定义格式管理 + 查询匹配
- [ ] **13. Core+UI: 自适应规则系统** — `AdaptiveOverrideRule` + `AdaptiveLevel` 枚举 + SettingsWindow 规则编辑器 + 预设规则
- [ ] **14. Core: 规则匹配集成** — 引擎压缩 + 预估器自适应列均按「用户规则 → 格式目录 → 内置分类」优先级查表

## 改动范围

涉及 **13 个文件**：

| 文件 | 改动 | 预估工时 |
|------|------|---------|
| `Core/Utils/CompressionEstimator.cs` | 🆕 新增 — 三级预估算法 | 3h |
| `Core/Utils/CompressionHistoryStore.cs` | 🆕 新增 — 学习型预估数据库 | 2h |
| `Core/Utils/RuntimeEstimator.cs` | 🆕 新增 — 加权融合实时 ETA + 批处理汇总 | 2h |
| `Core/Models/FormatDefinition.cs` | 🆕 新增 — `FormatDefinition` 模型 | 15min |
| `Core/Models/AdaptiveOverrideRule.cs` | 🆕 新增 — `AdaptiveOverrideRule` + `AdaptiveLevel` 枚举 | 30min |
| `Core/Services/FormatCatalog.cs` | 🆕 新增 — 格式目录注册/查询/匹配 | 1h |
| `UI/AppSettings.cs` | 新增 `CustomFormats` + `AdaptiveOverrides` 属性 + 预设默认规则初始化 | 30min |
| `UI/SettingsWindow.xaml` + `.cs` | 格式目录列表 + 自定义格式编辑器 + 规则列表 + 规则编辑器 | 3h |
| `UI/CompressSettingsWindow.xaml` | 添加预估面板 UI | 30min |
| `UI/CompressSettingsWindow.xaml.cs` | 集成预估逻辑 + 刷新按钮 | 1h |
| `UI/ProgressWindow.xaml` | 添加时间信息行 UI | 30min |
| `UI/ProgressWindow.xaml.cs` | 集成 RuntimeEstimator、时间格式化、防抖 | 1.5h |
| `UI/AppPartials/App.Compress.cs` 或 `Core/Services/CompressService.cs` | 传递预估值到 ProgressWindow | 30min |
| 测试项目 | 全部 3 个测试文件 | 2h |

**运行时依赖变更：** 无（JSON 文件写入，不需要外部数据库）

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

    // 🆕 自适应压缩预估列
    public string AdaptiveSizeDisplay { get; set; }     // "10.1 MB" — 启用自适应后的预估大小
    public string AdaptiveDurationDisplay { get; set; } // "~1s" — 启用自适应后的预估耗时
    public bool AdaptiveAvailable { get; set; }          // 当前设置是否已启用自适应模式
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

`CompressSample` 使用 `MemoryStream` + 对应引擎的写入器，只压缩到内存，不写磁盘。各格式实现：

- **ZIP**：`SharpCompress.Writers.Zip.ZipWriter` + `MemoryStream`
- **7z**：`SharpSevenZipCompressor` + `MemoryStream`（SharpSevenZip 通过 COM 绑定 7z.dll，支持纯内存压缩）
- **Tar/Gz**：`SharpCompress.Writers.Tar.TarWriter` + 可选 `GZipStream` 包装 `MemoryStream`

### 自适应感知预估 (Adaptive-Aware Estimation)

当用户在设置中启用了自适应压缩时（设置 → 压缩 → 自适应压缩级别），预估值需要反映自适应压缩的实际效果——部分文件会被自动降级为 Store（级别 0）。

**核心逻辑**：

```
EstimateAllAsync → 对每行（格式×级别）计算两组值:
  标准值:  所有文件按选定级别压缩 → size_standard, duration_standard
  自适应值: 按分类逐文件决定级别 → size_adaptive, duration_adaptive

自适应值计算:
  foreach (var path in sourcePaths)
  {
      var category = ClassifyByExtension(path);  // text/code/image_lossy/...
      
      // 自适应降级判定
      int effectiveLevel = (category is "image_lossy" or "media" or "archive")
          ? 0   // Store — 不压缩
          : selectedLevel;
      
      size_adaptive += GetCoefficient(format, category, effectiveLevel) × fileSize;
      duration_adaptive += GetDuration(format, category, effectiveLevel, fileSize);
      
      size_standard += GetCoefficient(format, category, selectedLevel) × fileSize;
      duration_standard += GetDuration(format, category, selectedLevel, fileSize);
  }
```

**系数扩展**：`CompressionCoefficients._rates` 需要支持按级别查表。对自适应降级的 Store(0) 级别，有以下系数：

| 分类 | ZIP 系数 | 7z 系数 | Tar 系数 |
|------|---------|---------|---------|
| `image_lossy` Store(0) | 1.00 | 1.00 | 1.00 |
| `media` Store(0) | 1.00 | 1.00 | 1.00 |
| `archive` Store(0) | 1.00 | 1.00 | 1.00 |

这些类型本身已经是压缩格式，Store 不压缩 ≈ 保持原大小。对其他分类（`text`/`code`/`image_lossless`/`binary`），自适应不会降级，所以系数与选定级别一致。

**耗时预估**：自适应降级为 Store 后，耗时大幅降低（只做流拷贝，不做压缩计算）。经验耗时系数：

| 操作 | 速度基准 |
|------|---------|
| 标准压缩（级别 5） | ~50 MB/s (ZIP), ~10 MB/s (7z) |
| Store（级别 0） | ~500 MB/s (磁盘 I/O 限速) |

当 `image_lossy`/`media`/`archive` 文件占比较高时，自适应耗时显著低于标准耗时。

**UI 联动**：
- `EstimateRow.AdaptiveAvailable` 根据 `AppSettings` 当前的自适应压缩级别设置决定
- 如果自适应设置为「禁用」，则 `AdaptiveAvailable = false`，表格中自适应列隐藏或置灰
- 如果自适应设置为「仅已知格式」或「智能检测」，自适应列正常显示

### 显示时机

- `CompressSettingsWindow` 加载时自动运行 `QuickEstimate`
- 用户选择文件/格式变更时自动重新估算（延迟 500ms 防抖）
- 用户可手动点击「刷新预估」触发 `StandardEstimateAsync`
- 不阻塞 UI（`async` + 后台线程）

### 学习型预估（经验数据库）

在快速预估（硬编码系数表）之后、标准采样之前，增加一个**经验数据库**层，
通过记录历史真实压缩率来优化长尾格式的预估。

**数据模型**：

```csharp
public class CompressionHistoryStore
{
    // key = 两级 key:
    //   有魔数: $"{FileFormat}_{ArchiveFormat}_{Level}"  如 "Jpeg_Zip_5"
    //   无魔数: $"{Extension}_{ArchiveFormat}_{Level}"   如 ".pxm_Zip_5"
    // value = 历史压缩率列表（压缩后大小/原始大小）
    private Dictionary<string, List<double>> _records;

    // 持久化路径: %LOCALAPPDATA%\MantisZip\compression_stats.json
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MantisZip", "compression_stats.json");
}
```

**两级 key 策略**：

```
QuickEstimate 流程：

1. 硬编码系数表查找 (扩展名 → 类型 → 压缩率)
   → 找到 → 返回（热门格式无需学习）

2. 魔数检测 + 经验数据库查找
   ├─ FileFormat != Unknown → key = "Jpeg_Zip_5"
   ├─ 魔数 Unknown 但有扩展名 → key = ".pxm_Zip_5"
   └─ 魔数 Unknown 且无扩展名 → 跳过，走第 3 步
   → 命中 N ≥ 3 条 → 返回平均值
   → 命中 1-2 条 → 可用但标记低置信度

3. 回退到标准采样预估（StandardEstimateAsync）
```

**何时记录**：用户在 `CompressSettingsWindow` 点击「压缩」→ 压缩完成 → 回调记录：

```csharp
// 在每项压缩完成后调用（CompressAsync 完成时的 hook）
CompressionHistoryStore.Record(
    fileFormat: FileFormatDetector.Detect(headBytes),  // 有魔数时
    extension: Path.GetExtension(filePath),             // 无魔数时 fallback
    archiveFormat: ArchiveFormat.Zip,
    level: 5,
    originalSize: fileSize,
    compressedSize: compressedSize);
```

**魔数 Unknown 的可行性分析**：

| 场景 | key 来源 | 压缩率一致性 | 预估可用性 |
|---|---|---|---|
| 有魔数（`Jpeg`/`Pdf`/`Mp4`） | `FileFormat` 枚举 | ✅ 同魔数 ≈ 同压缩特性 | ✅ 高 |
| 魔数 Unknown + 有扩展名（`.pxm`/`.vff`/`.bin`） | 扩展名 | ✅ 同扩展名 ≈ 同格式 | ✅ 中 |
| 魔数 Unknown + 无扩展名 | 不记录，直接默认值 | ❌ 无法归类 | ❌ |

**注意事项**：
- **冷启动**：一个 key 需要至少 3 次压缩记录才启用预估，之前走标准采样
- **数据清理**：保留每 key 最近 20 条记录，超出的丢弃最旧条目，防止数据膨胀
- **并发安全**：使用 `file lock` 或 `ConcurrentDictionary` + 定时落盘
- **文件损坏**：JSON 解析失败时清空数据库，不影响主流程

| 风险 | 等级 | 对策 |
|------|------|------|
| 采样低估误差（前 1MB 可压但后面不可压） | 🟡 | 备注说明「预估值仅供参考」；加粗误差范围 |
| ~~7z 采样需调 7z.exe，无法纯内存~~ | ✅ 已解决 | SharpSevenZip（COM 绑定 7z.dll）支持 `MemoryStream` 输出，7z 纯内存采样可行 |
| 超大文件源（TB 级），扫描耗时 | 🟢 | 限制扫描文件数（默认 10000）；超大文件跳过采样 |
| 预估耗时本身太长 | 🟢 | 默认仅快速模式；标准模式需用户手动触发 |

### 精度增强：文件大小分桶

**问题**：同魔数格式内部，压缩比可能因文件特征差异而不同。例如小尺寸 JPEG（缩略图，64KB）与高质量大尺寸 JPEG（数 MB）的压缩特性不同，混合在一个桶里取平均可能两头不靠。

**方案**：在现有 key 后追加一维大小分段，形成三位 key：

```
Jpeg_Zip_5_S    < 100KB      → 缩略图/图标类
Jpeg_Zip_5_M    100KB–1MB    → 普通网络图片
Jpeg_Zip_5_L    1MB–10MB     → 高分辨率照片
Jpeg_Zip_5_XL   > 10MB       → 超高质/RAW 导出
```

**分段策略**（对数分段，覆盖从 KB 到 GB 的广泛范围）：

| 分段 | 阈值 | 适用场景 | 典型文件 |
|------|------|---------|---------|
| S (Small) | < 100KB | 缩略图、图标、小文本 | `.ico`、小 `.jpg`、短 `.txt` |
| M (Medium) | 100KB – 1MB | 普通文档、中等图片 | `.pdf`、`.docx`、网页截图 |
| L (Large) | 1MB – 10MB | 高分辨率图片、短音频 | `.mp3`、大 `.jpg`、`.png` |
| XL (Extra Large) | 10MB – 100MB | 长音频、短视频、安装包 | `.exe`、`.wav`、`.mp4` 片段 |
| XXL (Double Extra) | 100MB – 1GB | 高清视频、数据库 | `.mkv`、`.iso`、`.7z` |
| XXXL (Triple Extra) | > 1GB | 大型数据集 | 虚拟机镜像、超大压缩包 |

**key 格式**：

```
有魔数: {FileFormat}_{ArchiveFormat}_{Level}_{Bucket}   如 "Wav_Zip_5_XL"
无魔数: {Extension}_{ArchiveFormat}_{Level}_{Bucket}    如 ".wav_Zip_5_XL"
```

**桶内逻辑与魔数 Unknown 相同**：

```
有魔数 → 桶内取平均值
魔数 Unknown + 有扩展名 → 桶内取平均值
魔数 Unknown + 无扩展名 → 不记录
```

**Record 签名变化**：

```csharp
CompressionHistoryStore.Record(
    fileFormat: ...,
    extension: ...,
    archiveFormat: ...,
    level: ...,
    originalSize: fileSize,           ← 已有，用此值计算桶
    compressedSize: compressedSize);
```

**冷启动影响**：每个桶独立积累 3 次才启用。相比无分桶方案，一个格式需要 3×N 桶次才能覆盖所有大小范围，冷启动更慢。对策：

- **全局平均作为回退**：大小桶积累不足时（< 3 次），回退到同格式无分桶的全局平均值（已有设计），两者都低于 3 次再走采样
- **合并相邻桶**：S 桶 + M 桶累计 ≥ 3 但各自 < 3 时，合并 SM 区间临时取平均

**数据清理**：同现有设计，每 key 保留最近 20 条。

---

## 运行时预估 (Runtime ETA)

> 压缩过程中，在进度条区域实时显示预计剩余时间和总时间。
> **依赖**: `CompressionEstimator` 的预估值做初始速度加权

### 用户界面

在 `ProgressWindow` 的进度条下方、按钮上方，新增一条时间信息行：

```
┌─────────────────────────────────────┐
│  正在压缩: 照片.zip                   │
│  ████████████████░░░░░  72%          │  ← 文件进度条
│  ████████████████████░  85%          │  ← 总体进度条
│  ⏱ 已用 1:23 · 剩余 0:32 · 共 1:55  │  ← 🆕 时间信息行
│  📦 2 / 3 个压缩包                    │  ← 文件计数
└─────────────────────────────────────┘
```

批处理模式下显示两行：

```
│  📦 当前包: 已用 1:23 · 剩余 0:32 · 共 1:55          │
│  📋 全部:   已用 1:23 · 预估剩余 4:15 · 共 5:38      │
```

### 核心算法：加权融合

```csharp
public class RuntimeEstimator
{
    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly long _totalBytes;
    private readonly double _initialSpeed;  // 来自 CompressionEstimator 的先验速度

    private long _lastProcessedBytes;
    private DateTime _lastUpdateTime;
    private double _blendedSpeed;

    // 加权融合:
    //   前 5 秒: blendedSpeed = initialSpeed × (1-w) + realSpeed × w
    //   5 秒后:  blendedSpeed ≈ realSpeed (w → 1)
    public TimeSpan? GetEstimatedRemaining(long processedBytes)
    {
        var elapsed = DateTime.UtcNow - _startTime;
        double realSpeed = elapsed.TotalSeconds > 0
            ? processedBytes / elapsed.TotalSeconds
            : 0;

        // weight 从 0 线性增长到 1，5 秒 stabilization
        double weight = Math.Min(1.0, elapsed.TotalSeconds / 5.0);
        _blendedSpeed = realSpeed * weight + _initialSpeed * (1 - weight);

        if (_blendedSpeed <= 0) return null;

        long remaining = _totalBytes - processedBytes;
        return TimeSpan.FromSeconds(remaining / _blendedSpeed);
    }

    public TimeSpan Elapsed => DateTime.UtcNow - _startTime;
    public TimeSpan EstimatedTotal => Elapsed + (GetEstimatedRemaining(...) ?? TimeSpan.Zero);
}
```

**`_initialSpeed` 来源**：
- `CompressionEstimator.StandardEstimateAsync` 返回的 `EstimatedDuration` 推算出预期速度
- 若未运行预估值（用户未点刷新），使用 `QuickEstimate` 的经验系数 × 文件大小估算

### 先验速度的获取与传输

```csharp
// CompressService 启动时传递预估数据
var estimate = await CompressionEstimator.EstimateAllAsync(sourcePaths);
var initialSpeed = estimate.AverageTotalBytes / estimate.AverageDuration.TotalSeconds;

// 通过新类传递到 ProgressWindow
var runtimeEstimator = new RuntimeEstimator(totalBytes: estimate.TotalSize, initialSpeed);
```

`RuntimeEstimator` 实例由 `CompressService`（或 `App.Compress.cs`/`App.Extract.cs`）创建，传递给 `ProgressWindow`。

### ArchiveProgress 扩展

`ArchiveProgress` 增加一个可选字段，用于引擎报告更精确的进度预估值（如 7z 引擎知道整个压缩包的总输入大小）：

```csharp
public class ArchiveProgress
{
    // ... 现有字段 ...

    /// <summary>压缩引擎预估的剩余时间（可选），由 RuntimeEstimator 在 UI 层计算后填入显示。</summary>
    // 注：此字段不由引擎设置，由 ProgressWindow 的 RuntimeEstimator 在 UI 层计算
}
```

实际上 `RuntimeEstimator` 在 UI 层运行即可，无需引擎参与。`ArchiveProgress` 已有 `TotalBytes`/`ProcessedBytes`，`RuntimeEstimator` 只需这两个值 + `_startTime` + `_initialSpeed`。

### 批处理模式时间预估

批处理模式下，需要维护两个层级的 `RuntimeEstimator`：

```
BatchRuntimeEstimator
├── _currentArchiveEstimator: RuntimeEstimator  // 当前包的实时 ETA
├── _pendingArchives: List<ArchivePreEstimate>  // 待处理包的预估值
└── GetRemaining():
      currentArchiveRemaining = _currentArchiveEstimator.GetEstimatedRemaining(...)
      pendingArchivesTotal = _pendingArchives.Sum(p => p.estimatedDuration)
      totalRemaining = currentArchiveRemaining + pendingArchivesTotal
```

**`ArchivePreEstimate` 数据模型**：

```csharp
public class ArchivePreEstimate
{
    public string ArchiveName { get; set; }
    public long TotalBytes { get; set; }
    public TimeSpan EstimatedDuration { get; set; }  // 来自 CompressionEstimator
}
```

**显示**：
- 当前包剩余时间：用 `_currentArchiveEstimator` 的实时速度推算
- 全部剩余时间 = 当前包剩余 + 待处理包的预估时间之和

### 显示防抖

- ETA 文本更新频率：每 **1 秒**刷新一次（不需要和进度条一样快）
- 前 3 秒显示 `"估算中..."` 避免初始闪烁
- 剩余时间 < 5 秒时显示 `"即将完成"` 而非 `"剩余 0:03"`
- 暂停时显示 `"已暂停"`，恢复后继续

### 时间格式化

```csharp
public static string FormatTimeSpan(TimeSpan ts)
{
    if (ts.TotalHours >= 1)
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";  // 1:23:45
    return $"{ts.Minutes}:{ts.Seconds:D2}";  // 23:45
}
```

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

**魔数检测实现**：>64KB 大文件的魔数识别复用 `preview-magic-detection.md` 中的 `FileFormatDetector`。

> ⚠️ 注意：这里无需使用 `ArchiveEntryExtractor.ExtractHeadAsync`——压缩预估操作的是**磁盘上的源文件**（不是压缩包内的条目），直接 `File.Read` 前 4KB 即可。采样试压阶段才需要读更多字节（~1MB）。

**检测流程**：

```
>64KB 源文件路径 → File.Read 前 4KB → FileFormatDetector.Detect(head)
                                                         ↓
                                              FileFormat 枚举值
                                                         ↓
                                          FileFormatToCategory() 映射表
                                                         ↓
                                          CompressionCoefficients 分类
                                          (text / code / image_lossless /
                                           image_lossy / media / binary / archive)
```

| 场景 | 检测方案 | 详情 |
|------|---------|------|
| 主路径 | **手动魔数匹配**（`FileFormatDetector.Detect()`） | 读前 4KB → 返回 `FileFormat` → 查映射表 → 压缩分类 |
| 可选增强 | **Mime-Detective 库**（如已安装） | 手动魔数返回 `Unknown` 时尝试库检测 |
| 回退 | `ClassifyByExtension()` | 两种魔数都失败或文件 ≤ 64KB 时降级 |

**`FileFormat` → `CompressionCoefficients` 映射表**（主路径使用）：

```
FileFormat 枚举值                                               CompressionCoefficients 分类
────────────────────────                                      ────────────────────────
Jpeg, WebP, DjVu                                              → image_lossy (压不动, 压缩率 ~0.99)
Png, Gif, Bmp, Ico, Tga, Hdr, Exr, Svg                       → image_lossless (可压, 压缩率 ~0.85)
Mp4, Mkv, WebM, Wmv, Mov, Avi, Flv                           → media (压不动, 压缩率 ~1.00)
Wav, Flac, Mp3, Ogg                                           → media (已压缩, 压缩率 ~0.99)
Zip, SevenZip, Rar, Tar, Gz, Bz2, Xz, Zstd                   → archive (已压缩, 压缩率 ~1.00)
Pdf, Docx, Xlsx, Pptx, Epub, Mobi, Azw3                       → binary (可压, 压缩率 ~0.60)
Odt, Ods, Odp                                                 → binary (ZIP-based OOXML 等价, 压缩率 ~0.60)
Xps                                                           → binary (ZIP-based, 压缩率 ~0.60)
OfficeOpenXml, OfficeLegacy                                   → binary (压缩率 ~0.60)
Pe, Elf, Cer, Pfx                                             → binary (可压, 压缩率 ~0.60)
Ttf, Otf, Woff, Woff2                                         → binary (可压, 压缩率 ~0.60)
Sqlite, Dbf, Iso, Vhd, Vhdx, Vmdk, VhdLegacy, Iso9660, Udf   → binary (可压, 压缩率 ~0.60)
Torrent, Stl, Lnk, Icl, Dicom                                 → binary (可压, 压缩率 ~0.60)
Dxf, Step, Fbx                                                → binary (3D 格式, 可压, 压缩率 ~0.60)
Fits, Parquet                                                 → binary (科学数据, 可压, 压缩率 ~0.60)
Text, Html, Markdown, Rtf                                     → text (高压缩比, 压缩率 ~0.15)
Subtitle                                                      → text (高压缩比, 压缩率 ~0.15)
```

如果安装了 Mime-Detective 作为可选增强，其 MIME type 映射保持不变（同 `preview-magic-detection.md`）：

```
image/jpeg, image/webp  → image_lossy
image/png, image/bmp    → image_lossless
video/*, audio/*        → media
application/zip,...     → archive
...                      （同上表）
```

> **引用**：魔数检测的完整方案见
> [`preview-magic-detection.md` → 方案选择：手动魔数为主，Mime-Detective 可选](preview-magic-detection.md)。

### 配置项（AppSettings → 压缩 标签页）

```
自适应压缩级别:
  ○ 禁用（始终使用选定级别）
  ○ 仅对已知格式自动降级（扩展名查表）← 默认推荐
  ○ 智能检测（大文件魔数 + 采样试压，适合追求极致准确）
```

`禁用` 时行为与现在一致；`仅已知格式` 时零额外开销。

### 用户自定义格式级别覆盖规则

在自适应压缩基础上，用户可以创建**多条件规则**，用一条规则覆盖一组格式。

核心设计分为**两层**：**格式目录**（纯数据定义） + **规则**（引用格式，设定行为）。

#### 第一层：格式目录（FormatCatalog）

格式目录是系统中所有已知/自定义格式的定义中心。**没有启用开关**——格式定义只是事实描述（X = 这些扩展名 + 这些魔数），不存在启用/禁用的概念。

**内置格式**预加载自 `FileFormat` 枚举，不可修改删除：

```
格式 ID  显示名           扩展名                             魔数(内置)
──────────────────────────────────────────────────────────────────
Jpeg     JPEG 图片        .jpg .jpeg                         由 FileFormatDetector 处理
Png      PNG 图片         .png                                由 FileFormatDetector 处理
WebP     WebP 图片        .webp                               由 FileFormatDetector 处理
Mp4      MP4 视频         .mp4                                由 FileFormatDetector 处理
Mp3      MP3 音频         .mp3                                由 FileFormatDetector 处理
Zip      ZIP 压缩包       .zip                                由 FileFormatDetector 处理
...      ...
```

**自定义格式**用户新增，用于工作中的专有/小众格式，可设置扩展名 + 魔数 hex：

```
格式 ID  显示名           扩展名       魔数(hex)
──────────────────────────────────────────────
MaxFile  Max3D 场景文件   .max         1A2B3C4D...
```

```csharp
public class FormatDefinition
{
    public string Id { get; set; } = "";                 // "Jpeg"
    public string DisplayName { get; set; } = "";         // "JPEG 图片"
    public List<string> Extensions { get; set; } = new();   // [".jpg", ".jpeg"]
    public string? MagicHex { get; set; }                 // "FFD8FFE0" — 仅自定义格式需要
    public bool IsBuiltIn { get; set; } = true;            // 内置不可删改
}
```

**魔数获取方式**：UI 提供「从文件读取」按钮，用户选择一个同格式文件，程序读前 16 字节自动填入 hex。

#### 第二层：规则（AdaptiveOverrideRule）

规则引用格式目录中的 `Id`，不直接写扩展名。

```csharp
public class AdaptiveOverrideRule
{
    public string Name { get; set; } = "";                    // "图片类"
    public List<string> FormatIds { get; set; } = new();      // ["Jpeg", "Png", "WebP"]
    public AdaptiveLevel Level { get; set; } = AdaptiveLevel.Store;
    public bool Enabled { get; set; } = true;
}

public enum AdaptiveLevel
{
    Store,          // 0
    Fast,           // 3
    Normal,         // 5
    Max,            // 9
    Global,         // 跟随全局级别
    GlobalPlusOne,  // 全局+1（不超过9）
    GlobalMinusOne, // 全局-1（不低于0）
    Custom,         // 用户自定义 1-9
}

public class AppSettings
{
    // ... 现有字段 ...
    public List<FormatDefinition> CustomFormats { get; set; } = new();  // 用户自建格式
    public List<AdaptiveOverrideRule> AdaptiveOverrides { get; set; } = new();
}
```

#### 级别解析

```csharp
int ResolveAdaptiveLevel(AdaptiveOverrideRule rule, int globalLevel) =>
    rule.Level switch
    {
        AdaptiveLevel.Store          => 0,
        AdaptiveLevel.Fast           => 3,
        AdaptiveLevel.Normal         => 5,
        AdaptiveLevel.Max            => 9,
        AdaptiveLevel.Global         => globalLevel,
        AdaptiveLevel.GlobalPlusOne  => Math.Min(9, globalLevel + 1),
        AdaptiveLevel.GlobalMinusOne => Math.Max(0, globalLevel - 1),
        AdaptiveLevel.Custom         => rule.CustomLevel ?? globalLevel,
        _                            => globalLevel,
    };
```

#### 匹配优先级

```
对每个文件 path:
  ext = Path.GetExtension(path).ToLowerInvariant()
  if 文件 > 64KB: 读前 16 字节 → head[]

  1. 用户自定义规则（按列表顺序，第一条命中即生效）
     foreach rule in AdaptiveOverrides:
       if !rule.Enabled: continue

       foreach fid in rule.FormatIds:
         def = FormatCatalog.Get(fid)    // 合并内置 + 自定义
         if def == null: continue

         // 扩展名匹配（所有格式通用）
         if ext in def.Extensions:
           effectiveLevel = ResolveAdaptiveLevel(rule, globalLevel)
           goto apply

         // 自定义魔数匹配（仅自定义格式、>64KB 且 MagicHex 非空）
         if def.MagicHex != null && head != null &&
            head.Length >= def.MagicHex.Length/2 &&
            head.Take(def.MagicHex.Length/2).SequenceEqual(HexToBytes(def.MagicHex)):
           effectiveLevel = ResolveAdaptiveLevel(rule, globalLevel)
           goto apply

       // 内置格式命中一条即可（不需要逐个格式试魔数）
       // 扩展名已匹配到内置格式的 def，直接生效

  2. 未命中 → 走内置自适应分类
     category = ClassifyByExtension(path)
     if category in ("image_lossy", "media", "archive"):
       effectiveLevel = 0   // Store
     else:
       effectiveLevel = globalLevel

  apply: 用 effectiveLevel 处理此文件
```

**关键设计**：
- 用户规则 **先于** 内置规则检查，优先级更高
- 规则列表按**顺序**匹配（可拖拽排序），首条命中即生效
- 内置格式匹配走扩展名（魔数由 `FileFormatDetector` 在内置分类阶段处理）
- 自定义格式匹配走扩展名 + hex 魔数双重验证
- 空规则列表时行为与无此功能完全一致，零兼容成本

#### UI 设计

SettingsWindow → 压缩标签页，自适应压缩级别选择器下方，分两个区域：

**格式目录面板**：

```
┌─ 格式目录 ─────────────────────────────────────┐
│  内置格式（不可删除）                              │
│  JPEG (.jpg .jpeg)                               │
│  PNG (.png)                                      │
│  WebP (.webp)                                    │
│  ...                                             │
│                                                   │
│  自定义格式                                         │
│  Max3D 场景文件  .max  [1A2B3C4D...]     [✕]    │
│  [+ 添加自定义格式]                                 │
└──────────────────────────────────────────────────┘
```

点击 [+ 添加自定义格式] 弹出编辑器：

```
┌─ 自定义格式 ──────────────────────────────┐
│  名称:      Max3D 场景文件                  │
│  扩展名:    .max                            │
│  魔数(hex): [1A2B3C4D ...         ]         │
│             [📂 从文件读取]                  │
│                                            │
│  [✔ 保存]    [取消]                        │
└────────────────────────────────────────────┘
```

**规则面板**：

```
┌─ 自适应规则 ───────────────────────────────┐
│  ☑ 图片类     [JPEG PNG WebP Gif] → 存储   │
│  ☑ 视频类     [MP4 MKV Avi]      → 存储   │
│  ☐ 文档类     [PDF Docx Epub]    → 全局-1  │
│  ☐ .max文件   [Max3D]            → 存储   │
│  ──────────────────────────────────────── │
│  [+ 添加规则]    [恢复默认]                │
└────────────────────────────────────────────┘
```

规则编辑器选择格式时，直接弹出一个可选列表（内置+自定义合并展示），勾选即可。

#### 预设默认规则（首次安装时填充，用户可删改）

```csharp
new AdaptiveOverrideRule
{
    Name = "图片类",
    FormatIds = new() { "Jpeg", "Png", "WebP", "Bmp", "Gif", "Ico", "Tga", "Hdr", "Exr", "Svg" },
    Level = AdaptiveLevel.Store,
    Enabled = true,
},
new AdaptiveOverrideRule
{
    Name = "视频类",
    FormatIds = new() { "Mp4", "Mkv", "WebM", "Wmv", "Mov", "Avi", "Flv" },
    Level = AdaptiveLevel.Store,
    Enabled = true,
},
new AdaptiveOverrideRule
{
    Name = "音频类",
    FormatIds = new() { "Mp3", "Flac", "Wav", "Ogg" },
    Level = AdaptiveLevel.Store,
    Enabled = true,
},
new AdaptiveOverrideRule
{
    Name = "压缩包类",
    FormatIds = new() { "Zip", "SevenZip", "Rar", "Tar", "Gz", "Bz2", "Xz", "Zstd", "Iso" },
    Level = AdaptiveLevel.Store,
    Enabled = true,
},
```

#### 对预估器和引擎的影响

| 组件 | 改动 |
|------|------|
| `FormatCatalog`（新增） | 管理内置+自定义格式定义，提供按 ID/扩展名/魔数查询 |
| `Core/Models/FormatDefinition.cs` | 🆕 格式定义数据模型 |
| `Core/Models/AdaptiveOverrideRule.cs` | 🆕 规则模型（`FormatIds` 引用格式 ID） |
| `CompressionEstimator.EstimateAllAsync` | 计算自适应列时，先从规则表查级别 |
| `ZipEngine` / `TarGzEngine` | 压缩时按文件查规则表决定最终级别 |
| `SevenZipEngine` | 同上；按级别分组打包 |
| `AppSettings` | 新增 `CustomFormats` + `AdaptiveOverrides` 属性 |
| `SettingsWindow.xaml` + `.cs` | 格式目录列表 + 自定义格式编辑器 + 规则列表 + 规则编辑器 |

### 引擎改动

- `ArchiveOptions` 增加 `AdaptiveCompressionLevel`（三态枚举）
- `ZipEngine`：`PutNextEntry` 前按文件切换 `SetLevel`
- `TarGzEngine`：同上，`SetLevel` 前切换
- `SevenZipEngine`：按级别分组文件，多次 `7z u` 增量更新

### 与预估器的关系

`CompressionCoefficients.ClassifyByExtension()` 可作为分类基础（扩展名 → 文本/代码/图片有损/图片无损/媒体/压缩包/二进制），
当分类结果为 `image_lossy` / `media` / `archive` 时自动降级。

---

## Definition of Done

### 压缩前预估
- [ ] `CompressionEstimator` 三级预估算法完成
- [ ] `CompressionCoefficients` 经验系数表覆盖所有常见文件类型
- [ ] `CompressSettingsWindow` 预估面板 UI 完成
- [ ] 自动预估 + 手动刷新交互正常
- [ ] 预估值不阻塞 UI（async 后台）
- [ ] `dotnet build` 通过，`dotnet test` 通过

### 运行时 ETA
- [ ] `RuntimeEstimator` 加权融合算法完成（先验速度 + 实时速度）
- [ ] `BatchRuntimeEstimator` 批处理双层 ETA 完成
- [ ] `ProgressWindow` 时间信息行 UI 完成（单包 / 批处理）
- [ ] ETA 防抖：前 3 秒「估算中」、暂停时「已暂停」、即将完成提示
- [ ] 预估值从 CompressionEstimator → RuntimeEstimator 的传递链路打通

### Final Checklist

#### 压缩前预估
- [ ] 快速预估（经验系数）在窗口打开时自动显示
- [ ] 标准预估（采样压缩）用户手动触发正常
- [ ] 格式/级别变更时自动重新预估（500ms 防抖）
- [ ] 预估面板不阻塞 UI 操作
- [ ] 自适应感知：表格自适应列根据 AppSettings 联动显示/隐藏
- [ ] 自适应感知：启用自适应后，image_lossy/media/archive 文件的系数正确使用 Store 级别
- [ ] 自适应感知：耗时列也反映自适应降级后的速度提升
- [ ] 自适应压缩级别（后续扩展）接口已预留

#### 运行时 ETA
- [ ] 压缩/解压进度窗口显示 ETA（已用时间、剩余时间、总时间）
- [ ] 批处理模式下同时显示当前包和全部的总时间
- [ ] 暂停时 ETA 正确暂停，恢复后续算
