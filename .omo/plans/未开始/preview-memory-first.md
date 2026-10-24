# 预览取数层内存优先（不落盘预览）

## TL;DR

> **Quick Summary**: 现在**每一次**预览都先把整个条目解压到 `%TEMP%\MantisZip\Preview\`，再从磁盘读回来——文本类写 15MB 盘只为读 1MB。本计划把「取数层」改成**能流式的走内存、必须文件路径的才落盘**，分四片推进；**第一片（文本/CSV/Markdown/代码/日志）进 S 档**，约 5–6h，收益最高、风险最低。
>
> **Deliverables**:
> - `src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs` — head 读取真正流式（Zip 不再整条目解压到内存）+ 7z 内存上限保护
> - `src/MantisZip.UI.Avalonia/Services/PreviewService.cs` — 新增内容取数 API（字节 or 临时路径二选一），替代无条件的 `ExtractToTempAsync`
> - `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs` — `ShowText` / `ShowCsv` / `ShowMarkdownPreview` 增加 `byte[]` 重载
> - `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Preview.cs` — 按预览类型分流取数 + head 缓存 + 临时文件命名/清理策略
> - `tests/MantisZip.UI.Avalonia.Tests/PreviewServiceContentTests.cs` — 分流与缓存单元测试
>
> **Estimated Effort**: **批次 A = 切片 1（文本 5–6h）+ 切片 2（图片/PDF 3–5h）+ 切片 4（>15MB 部分预览 4–6h）= 12–17h，S 档**；**批次 B = 切片 3（Office 5–7h），A 档**
> **Parallel Execution**: NO（Core → UI 单向依赖）
> **Critical Path**: T1（Core 流式 head）→ T2（取数分流）→ T3（Show\* 字节重载）→ T4（临时文件策略）→ T5（head 缓存）→ T6（验证）→ 切片 2 → 切片 4

---

## Context

### Original Request

> 用户（2026-10-24）：「`preview-quick-modes.md` 这个计划里面有一条是预览的时候把文件直接加载到内存而非解压到磁盘。你看把他单独提出来是不是值得放在 s 轮？」

结论：**值得单独成计划**（它改的是取数层，和 `preview-quick-modes` 的渐进渲染正交）。

> **2026-10-24 排期调整（用户拍板）**：用户问「先做文本+图片部分提升体验是不是更好」，讨论后确认两件事：①内存化在 SSD 上**不省多少时间**（写 15MB 是几十毫秒量级，而解码一张大 JPEG 是几百毫秒量级），它省的是「磁盘痕迹 + 便携模式 U 盘写 + 固定文件名竞态」，这属于「干净」而非「更快」；②用户最终选择的执行顺序是 **图标 → 接缝 → 预览 → 自动更新**，预览这一项按 **切片 1+2+4 合并成一批（12–17h）**，切片 3（Office）留在 A 档。
>
> 调整后的两个变化：**切片 4 提前进 S 档**（它才是用户能直接感觉到的那一项：现在超 15MB 一律 `Preview_TooLarge` 拒绝）；**切片 3 单独后置**（Office 本来就要解析完整文档，省下只是落盘，感知最弱）。
>
> ⚠️ 一个削弱切片 4 收益的事实：`MaxPreviewFileSize`（默认 15MB）**是用户可在设置里调大的**（[AppSettings.cs:150](src/MantisZip.UI.Avalonia/Models/AppSettings.cs#L150) → `App.axaml.cs:124`），所以「大文件看不了」并非死路。切片 4 的真实价值是「不想为看一眼 PDF 首页就解压 200MB」，因此范围收窄为**文本/PDF/Office 的 head-only 部分预览**（图片无法部分解码，仍受完整字节约束）。
>
> ⚠️ 本文件 `## TODOs` 目前只细化到切片 1（T1–T6）；切片 2/4 的任务分解在开工时补（约定见「后续切片」表），不要以为现有 TODOs 就是全批。

### 现状（实测，2026-10-24）

预览的完整链路，三个坑都在里面：

1. **顺序取数**：[MainWindowViewModel.Preview.cs:246-248](src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Preview.cs#L246-L248) 无条件调用 `PreviewService.ExtractToTempAsync`。
2. **全量落盘 + 每次清目录**：[PreviewService.cs:356-384](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L356-L384) —— 先 `foreach (var f in Directory.GetFiles(tempDir)) File.Delete(f)`，再把**整个条目**写到 `%TEMP%\MantisZip\Preview\preview<ext>`。
3. **从磁盘读回**：`ShowText(filePath)` 内部 `File.ReadAllBytes`（[PreviewViewModel.cs:1081](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L1081-L1083)）；`ShowCsv` / `ShowImage` / `ShowFont` / `ShowOffice` / `ShowPdfAsync` / `ShowMarkdownPreview` 同样是路径入参。

具体的浪费：

| 现象 | 证据 | 影响 |
|---|---|---|
| 文本预览写 15MB 盘只为读 1MB | `MaxPreviewFileSize = 15MB`（[PreviewService.cs:152](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L152)）vs `MaxTextPreviewBytes = 1MB`（[PreviewService.cs:157](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L157)） | 最高频操作里最没意义的 IO |
| 切来切去反复全量解压 | 每次预览前清空目录（[MainWindowViewModel.Preview.cs:130-131](src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Preview.cs#L130-L131)） | A→B→A 触发两轮删除+重写 |
| 便携模式往 U 盘写 | `AppSettings.GetTempDir()` 便携分支指向 `Data\Temp`（[AppSettings.cs:265-266](src/MantisZip.UI.Avalonia/Models/AppSettings.cs#L265-L266)） | 预览 10MB PDF = 往 U 盘写 10MB |
| 敏感文件明文落 TEMP | 同上 | 密码库/合同预览后可能残留 |
| 魔数检测已经读过 head，却被丢掉 | `ClassifyPreviewByMagicAsync` 调 `ExtractHeadAsync`（[PreviewService.cs:218](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L218)）只返回 `(type, format, displayName)` | 同一份数据读两遍 |

### 两个必须知道的前提（决定成本与边界）

1. **`ExtractHeadAsync` 对 Zip 其实是「整条目解压到 MemoryStream 再截断」**：`entry.WriteTo(ms)` 之后再 `new byte[min(maxBytes, ms.Length)]`（[ArchiveEntryExtractor.cs:303-308](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L303-L308)）。所以直接复用它虽然省了磁盘，但内存占用仍是 O(条目大小) —— 切片 1 必须顺手改成真流式（`entry.Open()` 读到 maxBytes 就停）。
2. **`7z`/`TarGz` 有硬约束**：7z 走 `extractor.ExtractFile(index, ms)`（[ArchiveEntryExtractor.cs:325](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L325)）必须解完整条目；固实 7z 与 TarGz 甚至要走磁盘临时文件（[ArchiveEntryExtractor.cs:371-393](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L371-L393)）。7z 侧用「截断写入流」（只接收前 N 字节、其余丢弃）把内存固定住；TarGz 保留落盘路径不变。

### Key Decisions

1. **只动取数层，不动分类与渲染**。`ClassifyPreview` / `FileFormatDetector` / 各 `Show*` 的渲染逻辑一行不改。
2. **显式例外，不追求「零落盘」**：HTML→WebView 必须落盘（`NativeWebView` 不支持 `data:` URI，见 [PreviewViewModel.cs:3128](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L3128) 注释）、`OpenWith` 必须落盘（外部程序要真实路径）、字体预览保留落盘（`FontParser` 要文件名，走 `%TEMP%\MantisZip\Fonts`）。**这三处写进 Must NOT，防止后续"顺手统一"。**
3. **按「批次」排序而不是按切片号**（2026-10-24 调整）：**批次 A = 切片 1+2+4（12–17h，S 档）**，**批次 B = 切片 3（5–7h，A 档）**。理由：切片 1 是地基（T1–T6 把它做完，2/4 才便宜），切片 2/4 是用户能直接感觉到的部分，切片 3 感知最弱且逐库确认 Stream 支持最费时——把它单拎出去，批次 A 的节奏才不被不确定性拖住。
4. **head 缓存做，但只缓存小对象**：LRU 6 条 × ≤1MB，key = 压缩包路径 + 条目 FullPath + 条目大小 + 压缩包 mtime。命中省一次解压（固实 7z 上尤其明显），失效靠 mtime 而非监听。
5. **不缓存解压后的完整文件**。大文件缓存会吃掉内存且引入失效面，切片 2/3 之后也只在「同一文件回看」时不重复解压（靠 OS 页缓存即可）。

### Research Findings

- `PreviewHeadSize = 4096`（[PreviewService.cs:136](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L136)）—— 魔数用，远小于文本所需的 1MB。
- `PreviewService` 全部为 `static`，靠 `Enable*` / `Max*` 可写属性做开关，测试可直接改（现有 `PreviewViewModelTests.cs` 即此风格）。
- 现有预览测试：`PreviewViewModelTests.cs`、`PreviewWebViewLazyInitTests.cs`、`MarkdownPreviewBuilderTests.cs`、`PreviewAudioPreviewTests.cs`。**没有取数层测试** —— 本计划补上。
- `ArchiveEntryExtractor` 公开面只有 `ExtractEntryAsync`（→ 文件）/ `ExtractHeadAsync` / `ExtractHeadTailAsync`，**没有 stream 版**；切片 2/3 才需要 `ExtractEntryToStreamAsync`。

---

## Work Objectives

### Core Objective

让预览的**默认路径不再产生磁盘写**（文本类彻底不写；图片/PDF/Office 在 A 档跟进），同时把内存占用和临时文件残留都变成有界、可预期的行为。

### Concrete Deliverables（切片 1）

- `ArchiveEntryExtractor`：Zip head 改真流式；7z 加 `TruncatingWriteStream` 上限；保留 TarGz 落盘兜底
- `PreviewService`：`LoadPreviewContentAsync` 返回 `PreviewContent(byte[]? Bytes, string? TempPath)`，按 `PreviewType` 分流
- `PreviewViewModel`：`ShowText(byte[])` / `ShowCsv(byte[])` / `ShowMarkdownPreview(byte[])` 重载
- 临时文件：命名带条目哈希，不再每次清空整目录
- head 缓存：LRU 6 条，命中率可观测（`App.DebugLog`）
- 测试：`PreviewServiceContentTests.cs`

### Definition of Done

- [ ] 预览 `.txt` / `.log` / `.json` / `.md` / `.csv` / 代码文件时，`%TEMP%\MantisZip\Preview\` 下**不出现新文件**（用目录快照对比验证）
- [ ] 便携模式下同样不写 `Data\Temp\Preview`
- [ ] `pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia` 通过
- [ ] `pwsh -File scripts\verify-repo.ps1 -Task test -Target ui-tests` 通过（含新增用例）
- [ ] 图片/PDF/Office/字体/HTML/打开方式 六类预览行为与改造前**完全一致**（回归矩阵见 T6）

### Must Have

- 文本类预览零磁盘写（Zip / 7z / TarGz 三种包都要覆盖）
- head 读取内存占用与 `maxBytes` 同阶，不再随条目大小线性增长（Zip）
- 加密包 + 会话密码路径必须与现状一致
- 临时文件按条目命名，`A→B→A` 不重复解压（缓存命中）
- 所有路径都有降级：取不到内存内容时回落到原磁盘路径

### Must NOT Have (Guardrails)

- **不改** HTML→WebView 的落盘（`NativeWebView` 不接受 `data:` URI）
- **不改** `OpenWith` 临时解压（外部程序要真实路径）
- **不改** 字体预览的落盘（`FontParser` 依赖文件名）
- **不改** 预览分类 / 魔数检测 / `FileFormatDetector` 的判定逻辑
- **不动** `preview-quick-modes` 的三模式渐进渲染（继续冻结，见 `docs/PLAN.md`）
- **不引入**新 NuGet 依赖

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** —— 除主观观感外全部由 agent 执行。

### Test Decision

- **Infrastructure exists**: YES（`tests/MantisZip.UI.Avalonia.Tests`）
- **Automated tests**: 新增 `PreviewServiceContentTests.cs`
  1. 文本类 `PreviewContent.Bytes != null` 且 `TempPath == null`
  2. 图片/PDF 类仍返回 `TempPath`（切片 1 不改变其行为）
  3. 同一 (包, 条目, size, mtime) 连续取两次 → `ExtractHeadAsync` 只被调用一次（用计数假实现）
  4. 条目大小变化（模拟 mtime/size 变）→ 缓存 miss
  5. Zip head 流式：构造一个「大条目小 maxBytes」用例，断言返回长度 = maxBytes 且不再整条目解压（用可观测的 `WriteTo` 计数或条目大小阈值断言耗时/内存不现实，改为断言 `TruncatingWriteStream` 行为）
- **Framework**: xUnit

### QA Policy

- **磁盘证据**：预览前后对 `%TEMP%\MantisZip\Preview\` 与 `Data\Temp\Preview\` 各拍一次文件清单快照，对比必须为空（这是本计划最硬的判据）
- **回归矩阵**：见 T6，8 类格式 × 3 种包（Zip / 7z / TarGz）
- 证据现场生成、不落库（同 `icon-normalization` 的约定）

---

## Execution Strategy

### Waves

```
Wave 1（Core，串行）:
└── T1 Zip head 真流式 + 7z 截断流 + TarGz 兜底保留

Wave 2（取数分流，串行）:
├── T2 PreviewService.LoadPreviewContentAsync（按 PreviewType 分流）
└── T3 ShowText / ShowCsv / ShowMarkdownPreview 字节重载

Wave 3（策略，可与 W2 并行）:
├── T4 临时文件命名 + 清理策略
└── T5 head LRU 缓存

Wave 4（验证）:
└── T6 单测 + 回归矩阵 + 磁盘快照
```

### Dependency Matrix

- **T1**: 无 → 阻塞 T2
- **T2**: 依赖 T1 → 阻塞 T3、T5
- **T3**: 依赖 T2 → 阻塞 T6
- **T4**: 依赖 T2 → 阻塞 T6
- **T5**: 依赖 T1/T2 → 阻塞 T6
- **T6**: 依赖 T3/T4/T5

---

## TODOs（批次 A · 目前只细化到切片 1 的 T1–T6）

> ⚠️ 切片 2（图片/PDF Stream）与切片 4（>15MB 部分预览）的任务分解**尚未写进本节**——它们依赖 T1–T6 落地后的实际接口形态，开工时补写（不能拿现有 T1–T6 当全批的任务清单）。

- [ ] **T1. Core：head 读取真流式**

  **What to do**:
  - Zip 分支：`entry.WriteTo(ms)` 改为 `using var s = entry.Open(); 读满 maxBytes 即返回`（不满则返回实际长度）
  - 7z 分支：新增内部类 `TruncatingWriteStream`（Capacity = maxBytes，写满后丢弃剩余但继续接收，避免底层解压中断），`ExtractFile(index, truncating)` 后取缓冲区
  - TarGz / 固实 7z：**保持**现有落盘兜底（`ExtractHeadViaFullExtractAsync`），但加 `CoreLog.Info` 标注走了慢路径
  - `ExtractHeadTailAsync` 的 head 部分同步改成流式（它复用同一实现）

  **Must NOT do**:
  - 不改 `ExtractEntryAsync`（写完整文件）的语义
  - 不改 `MaxTailExtractSize = 10MB` 等既有上限

  **Acceptance Criteria**:
  - 对 100MB 的 zip 条目取 1MB head：进程峰值内存不再出现 ~100MB 尖峰（用 `GC.GetTotalAllocatedBytes` 对比断言，允许 5% 波动）
  - 三种包 + 加密包 head 内容与改造前逐字节一致（用现有测试做回归）

- [ ] **T2. PreviewService：按类型分流取数**

  **What to do**:
  - 新增 `public sealed record PreviewContent(byte[]? Bytes, string? TempPath)`
  - 新增 `public static async Task<PreviewContent?> LoadPreviewContentAsync(archivePath, entry, format, previewType, password, ct)`
    - `PreviewType.Text / Csv / Markdown` → 取 head（上限 `MaxTextPreviewBytes`）→ `Bytes`
    - 其余 → 复用现有 `ExtractToTempAsync` → `TempPath`
    - 取内存失败（超限/异常）→ 回落 `ExtractToTempAsync`
  - `ClassifyPreviewByMagicAsync` 增加 `out byte[]? head` 重载或返回元组扩展，让魔数阶段的 head 能复用给文本预览（`PreviewHeadSize = 4096` 不够 1MB 时仍需二次取，但可省掉一次 4KB 读）

  **Acceptance Criteria**:
  - 文本类返回 `Bytes` 且 `TempPath == null`
  - 图片/PDF/Office 行为不变（仍 `TempPath`）

- [ ] **T3. PreviewViewModel：字节重载**

  **What to do**:
  - `ShowText(byte[] bytes)` 重载：抽出 `ShowText` 的主体，`_textPreviewBytes = bytes`
  - `ShowCsv(byte[])`：CSV 解析从 `File.ReadAllText` 改为读字节
  - `ShowMarkdownPreview(byte[])`：Markdown 解码同上
  - `MainWindowViewModel.Preview.cs` 的 switch 分支：`PreviewContent.Bytes != null` 走字节重载，否则走原路径方法

  **Must NOT do**:
  - 不删原有 `string filePath` 重载（图片/PDF/Office/字体/OpenWith 仍在用，删了会扩大改动面）

- [ ] **T4. 临时文件命名与清理策略**

  **What to do**:
  - 文件名改为 `preview_{条目 FullPath 的稳定哈希}{ext}`，不再固定 `preview{ext}`
  - 取消「每次预览前清空整个 Preview 目录」；改为：启动时清一次 + 单次预览后只删**上一条**本次会话产生的文件（保留当前这条以便回看）
  - 保留 `CleanupPreviewTemp`（递归删）作为退出兜底

  **Acceptance Criteria**:
  - `A→B→A` 三次预览后目录内文件数 ≤ 2
  - 应用退出后目录为空

- [ ] **T5. head LRU 缓存**

  **What to do**:
  - `PreviewService` 内静态 `LinkedList` + `Dictionary` 实现 6 条 LRU，单条上限 `MaxTextPreviewBytes`
  - key = `archivePath|entryFullPath|entrySize|archiveMtimeTicks`
  - 命中时 `App.DebugLog("[PRV] head cache hit")`
  - 提供 `internal static void ResetHeadCacheForTests()`

  **Must NOT do**:
  - 不缓存图片/PDF/Office 内容
  - 不做后台预热（预览是用户触发行为，预热纯浪费）

- [ ] **T6. 验证**

  **What to do**:
  - 新增 `PreviewServiceContentTests.cs`（5 条用例见 Verification）
  - 回归矩阵手测：文本 / CSV / Markdown / 图片 / PDF / Office / 字体 / HTML × Zip/7z/TarGz，含一个加密包
  - 磁盘快照对比：预览文本前后 `Preview` 目录清单必须一致（空）
  - `verify-repo.ps1 -Task build -Target avalonia` + `-Task test -Target ui-tests` + `-Task check`

---

## 切片与批次（2026-10-24 调整后）

| 切片 | 内容 | 工时 | 批次 / 档位 | 关键风险 |
|---|---|:--:|:--:|---|
| 1 | 文本/CSV/Markdown/代码/日志走内存 + Core 流式 head + 临时文件按条目命名 + head LRU | 5–6h | **A / S 档** | Zip head 改流式要保证「读满即停」，别把截断逻辑写丢 |
| 2 | 图片 → `SKBitmap.Decode(Stream)`（含 `SKCodec` 尺寸探测）；PDF → `PdfDocument.Open(Stream)` | 3–5h | **A / S 档** | SkiaSharp 需 `Stream` 可 seek；PDF 需验证内存流生命周期（`PdfDocument` 持有流） |
| 4 | >15MB 的 head-only 部分预览，替代现在直接 `Preview_TooLarge` 拒绝 | 4–6h | **A / S 档** | 要新设计「部分内容」UI 提示；**范围收窄到文本/PDF/Office**（图片无法部分解码）；且 15MB 上限本身可调，收益是「不为看一眼解压 200MB」 |
| 3 | Office → `SpreadsheetDocument.Open(Stream)` / `WordprocessingDocument.Open(Stream)` / `PresentationDocument.Open(Stream)`；ClosedXML 走 `new XLWorkbook(stream)` | 5–7h | **B / A 档** | OpenXml 各 API 对 seek 要求不同；ClosedXML 大文件内存占用需实测；感知最弱（本来就要解析全文） |

> **为什么切片 3 不进批次 A**：它的工时几乎全在「逐库确认 Stream 支持 + 生命周期管理 + 大文件内存实测」，是探索性工作；切片 1/2/4 是确定性工作。混在一起会让本轮节奏被不确定性拖住。
>
> **为什么切片 4 反而进了批次 A**：它是本计划里**唯一有直接体感**的一项（现在超限就是一句 `Preview_TooLarge` 拒绝），而 1/2 主要是「更干净」。用户 2026-10-24 定的顺序是「图标 → 接缝 → 预览 → 自动更新」，预览这一项既然排在用户可见的第三位，就该把有体感的切片带上。

---

## 与既有计划的关系

- **`preview-quick-modes`（冻结）**：本计划切片 1 覆盖其约六成的体感收益（数据从哪来），而它是「先给部分再补全」（渲染进度）。和解冻条件一致：如果将来做它，取数层已经是内存优先，不会返工。
- **`file-list-thumbnails`（B 档）**：它复用 `ArchiveEntryExtractor.ExtractHeadAsync` 提缩略图；本计划 T5 的 head 缓存可直接复用，先做本计划能让它便宜一点。
- **`core-temp-root-injectable`（A 档小活）**：本计划 T4 改的是「Preview 子目录的文件命名与清理」，那个计划改的是「临时根在哪」。两者不冲突，顺序无所谓。
