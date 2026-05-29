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
- [ ] **2. Core: `CompressionHistoryStore` 类** — 学习型预估数据库（两级 key 策略 + JSON 持久化）
- [ ] **3. Core: `CompressionCoefficients` 经验系数表** — 扩展名 → 类型 → 压缩率
- [ ] **4. UI: `CompressSettingsWindow` 预估面板** — XAML 布局 + 数据绑定
- [ ] **5. UI: 预估交互逻辑** — 自动检测 + 刷新按钮 + 防抖 + 学习型记录 hook
- [ ] **6. Test: 单元测试** — `CompressionEstimatorTests` + `CompressionHistoryStoreTests`

## 改动范围

涉及 **6 个文件**：

| 文件 | 改动 | 预估工时 |
|------|------|---------|
| `Core/Utils/CompressionEstimator.cs` | 🆕 新增 — 三级预估算法 | 3h |
| `Core/Utils/CompressionHistoryStore.cs` | 🆕 新增 — 学习型预估数据库（两级 key 策略） | 1.5h |
| `UI/CompressSettingsWindow.xaml` | 添加预估面板 UI | 30min |
| `UI/CompressSettingsWindow.xaml.cs` | 集成预估逻辑 + 刷新按钮 | 1h |
| `UI/MainWindow.xaml.cs` | 压缩前显示预估（可选步骤） | 15min |
| 测试项目 | `CompressionEstimatorTests` + `CompressionHistoryStoreTests` | 1h |

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
FileFormat 枚举值                             CompressionCoefficients 分类
────────────────────────                    ────────────────────────
Jpeg, WebP                                  → image_lossy (压不动, 压缩率 ~0.99)
Png, Gif, Bmp, Ico, Tga, Hdr, Exr, Svg     → image_lossless (可压, 压缩率 ~0.85)
Mp4, Mkv, WebM, Wmv, Mov, Avi, Flv         → media (压不动, 压缩率 ~1.00)
Wav, Flac, Mp3, Ogg                         → media (已压缩, 压缩率 ~0.99)
Zip, SevenZip, Rar, Tar, Gz, Bz2, Xz, Zstd → archive (已压缩, 压缩率 ~1.00)
Pdf, Docx, Xlsx, Pptx, Epub                 → binary (可压, 压缩率 ~0.60)
Pe, Elf, Cer, Pfx                           → binary (可压, 压缩率 ~0.60)
Ttf, Otf, Woff, Woff2                       → binary (可压, 压缩率 ~0.60)
Sqlite, Dbf, Iso                            → binary (可压, 压缩率 ~0.60)
Torrent, Stl, Lnk, Vhd, Vmdk                → binary (可压, 压缩率 ~0.60)
Text, Html, Markdown                        → text (高压缩比, 压缩率 ~0.15)
Subtitle                                    → text (高压缩比, 压缩率 ~0.15)
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

- [ ] `CompressionEstimator` 三级预估算法完成
- [ ] `CompressionCoefficients` 经验系数表覆盖所有常见文件类型
- [ ] `CompressSettingsWindow` 预估面板 UI 完成
- [ ] 自动预估 + 手动刷新交互正常
- [ ] 预估值不阻塞 UI（async 后台）
- [ ] `dotnet build` 通过，`dotnet test` 通过

### Final Checklist

- [ ] 快速预估（经验系数）在窗口打开时自动显示
- [ ] 标准预估（采样压缩）用户手动触发正常
- [ ] 格式/级别变更时自动重新预估（500ms 防抖）
- [ ] 预估面板不阻塞 UI 操作
- [ ] 自适应压缩级别（后续扩展）接口已预留
