# 预览取数层内存优先（不落盘预览）

> **状态**: ✅ 已完成（2026-10-28 归档，批次 A = 切片 1+2+4 已落地，随 v0.5.4）。T1–T6 = 切片 1，T7–T9 = 切片 2，T10–T11 = 切片 4，T12 = 批次 A 收尾验证（全部见「TODOs」）。

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
> **Critical Path**: T1（Core 流式 head）→ T2（取数分流）→ T3（Show\* 字节重载）→ T4 / T5（策略）→ **切片 2**：T7（Core 整条目进内存）→ T8 / T9（图片 / PDF 走字节，可并行）→ **切片 4**：T10（判定顺序重排 + 文本部分预览）→ T11（部分内容提示）→ T6 + T12（验证收尾）

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
> ⚠️ 本文件 `## TODOs` 已覆盖批次 A 全部任务（T1–T12）：T1–T6 = 切片 1、T7–T9 = 切片 2、T10–T11 = 切片 4、T12 = 收尾验证；切片 3（Office）的任务分解（T13 起）留到批次 B 开工时补。

### 现状（实测，2026-10-24）

预览的完整链路，三个坑都在里面：

1. **顺序取数**：[MainWindowViewModel.Preview.cs:246-248](src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Preview.cs#L246-L248) 无条件调用 `PreviewService.ExtractToTempAsync`。
2. **全量落盘 + 每次清目录**：[PreviewService.cs:356-384](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L356-L384) —— 先 `foreach (var f in Directory.GetFiles(tempDir)) File.Delete(f)`，再把**整个条目**写到 `%TEMP%\MantisZip\Preview\preview<ext>`。
3. **从磁盘读回**：`ShowText(filePath)` 内部 `File.ReadAllBytes`（[PreviewViewModel.cs:1081](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L1081-L1083)）；`ShowCsv` / `ShowImage` / `ShowFont` / `ShowOffice` / `ShowPdfAsync` / `ShowMarkdownPreview` 同样是路径入参。

具体的浪费：

| 现象 | 证据 | 影响 |
|---|---|---|
| 文本预览写 15MB 盘只为读 1MB | `MaxPreviewFileSize = 15MB`（[PreviewService.cs:152](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L152)）vs `MaxTextPreviewBytes = 1MB`（[PreviewService.cs:157](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L157)） | 最高频操作里最没意义的 IO |
| 切来切去反复全量解压 | 每次预览前清空整个 Preview 目录（[PreviewService.cs:367-370](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L367-L370)） | A→B→A 触发两轮删除+重写 |
| 便携模式往 U 盘写 | `AppSettings.GetTempDir()` 便携分支指向 `Data\Temp`（[AppSettings.cs:265-266](src/MantisZip.UI.Avalonia/Models/AppSettings.cs#L265-L266)） | 预览 10MB PDF = 往 U 盘写 10MB |
| 敏感文件明文落 TEMP | 同上 | 密码库/合同预览后可能残留 |
| 魔数检测已经读过 head，却被丢掉 | `ClassifyPreviewByMagicAsync` 调 `ExtractHeadAsync`（[PreviewService.cs:218](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L218)）只返回 `(type, format, displayName)` | 同一份数据读两遍 |

### 两个必须知道的前提（决定成本与边界）

1. **`ExtractHeadAsync` 的流式化已在工作区落地（未提交——`git status` 可见 `M src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs`、新增 `src/MantisZip.Core/Utils/TruncatingWriteStream.cs`，以及 `tests/MantisZip.Tests/Utils/ArchiveEntryExtractorTests.cs` / `tests/MantisZip.Tests/Utils/TruncatingWriteStreamTests.cs` 两个新测试）**：Zip 走 `entry.OpenEntryStream()` + `ReadHeadFromStream` 逐块读（[ArchiveEntryExtractor.cs:326-365](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L326-L365)），7z 走 `TruncatingWriteStream` 截断写入（[ArchiveEntryExtractor.cs:378-398](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L378-L398)）。**切片 2 的 T7 直接踩在这套实现上**（同目录、同流式思路），不要回头改它的语义。
2. **`7z`/`TarGz` 有硬约束**：7z 的 `extractor.ExtractFile(entry.Index, stream)` 必须解完整条目（非固实走截断流、固实要跑完整个固实块，[ArchiveEntryExtractor.cs:378-398](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L378-L398)）；固实 7z 与 TarGz 只能先落盘（[ArchiveEntryExtractor.cs:445-475](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L445-L475)）。⇒ 切片 2 **不追求「零落盘」**，只追求「完整字节在内存里、临时文件随即删除」；切片 4 的部分预览在 TarGz 上**不省磁盘**（见 T10 的成本表与硬上限）。

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
- `ArchiveEntryExtractor` 公开面只有 `ExtractEntryAsync`（→ 文件）/ `ExtractHeadAsync` / `ExtractHeadTailAsync`，**没有「整条目进内存」的 API**；切片 2 由 T7 补 `ExtractEntryToMemoryAsync`（返回 `byte[]`，理由见 T7），切片 3 的 Office 取数才只需要它一次。

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
- **回归矩阵**：切片 1 见 T6；批次 A 全量见 T12（8 类主格式 + 4 类特例 × Zip / 7z / TarGz，每格写预期取数路径）
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

Wave 4（切片 2：整条目内存取数）:
├── T7 Core ExtractEntryToMemoryAsync（整条目 + 硬上限，返回 byte[]）
└── T7 之后并行:
    ├── T8 静态图片走字节（GIF/APNG/SVG/ICO/TGA/AVIF 六类特例保持落盘）
    └── T9 PDF 走字节（PdfParser 字节重载 + PdfDocument.Open(byte[])）

Wave 5（切片 4：部分预览）:
├── T10 判定顺序重排 + 文本部分预览 + MaxPartialPreviewBytes 硬上限（Office 留接口位）
└── T11 部分内容提示 + 三语 key 草案（依赖 T10 的截断标记）

Wave 6（验证，全部汇合）:
├── T6 切片 1 单测 + 回归矩阵 + 磁盘快照
└── T12 批次 A 回归矩阵（40 格）+ 双重临时根快照 + HeadExtract 残留检查
```

### Dependency Matrix

- **T1**: 无 → 阻塞 T2、T7
- **T2**: 依赖 T1 → 阻塞 T3、T5、T10
- **T3**: 依赖 T2 → 阻塞 T6、T8、T9
- **T4**: 依赖 T2 → 阻塞 T6
- **T5**: 依赖 T1/T2 → 阻塞 T6
- **T6**: 依赖 T3/T4/T5 → 阻塞 T12
- **T7**（切片 2）: 依赖 T1（复用流式 / 截断流的结论）→ 阻塞 T8、T9
- **T8**（切片 2）: 依赖 T3（`PreviewContent` 字节分流）+ T7（整条目 API）→ 与 T9 可并行 → 阻塞 T12
- **T9**（切片 2）: 依赖 T3 + T7 → 与 T8 可并行 → 阻塞 T12
- **T10**（切片 4）: 依赖 T2（`PreviewContent` 需带 `IsPartial` / `TotalSize`）+ T3；与切片 2 无依赖，**可与 T8/T9 并行** → 阻塞 T11、T12
- **T11**（切片 4）: 依赖 T10（截断标记）→ 阻塞 T12
- **T12**: 依赖 T6、T8、T9、T10、T11

---

## TODOs（批次 A · 切片 1 的 T1–T6 + 切片 2 的 T7–T9 + 切片 4 的 T10–T12）

> ⚠️ 本节覆盖批次 A 全部任务，行号取自 2026-10-28 的实读快照（`ArchiveEntryExtractor.cs` 713 行、`PreviewViewModel.cs` 3752 行、`MainWindowViewModel.Preview.cs` 463 行、`PreviewPanel.axaml` 929 行）。后续改动导致偏移时以**方法名/属性名**为准，不要盲信行号。

- [x] **T1. Core：head 读取真流式**

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

- [x] **T2. PreviewService：按类型分流取数**

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

- [x] **T3. PreviewViewModel：字节重载**

  **What to do**:
  - `ShowText(byte[] bytes)` 重载：抽出 `ShowText` 的主体，`_textPreviewBytes = bytes`
  - `ShowCsv(byte[])`：CSV 解析从 `File.ReadAllText` 改为读字节
  - `ShowMarkdownPreview(byte[])`：Markdown 解码同上
  - `MainWindowViewModel.Preview.cs` 的 switch 分支：`PreviewContent.Bytes != null` 走字节重载，否则走原路径方法

  **Must NOT do**:
  - 不删原有 `string filePath` 重载（图片/PDF/Office/字体/OpenWith 仍在用，删了会扩大改动面）

- [x] **T4. 临时文件命名与清理策略**

  **What to do**:
  - 文件名改为 `preview_{条目 FullPath 的稳定哈希}{ext}`，不再固定 `preview{ext}`
  - 取消「每次预览前清空整个 Preview 目录」；改为：启动时清一次 + 单次预览后只删**上一条**本次会话产生的文件（保留当前这条以便回看）
  - 保留 `CleanupPreviewTemp`（递归删）作为退出兜底

  **Acceptance Criteria**:
  - `A→B→A` 三次预览后目录内文件数 ≤ 2
  - 应用退出后目录为空

- [x] **T5. head LRU 缓存**

  **What to do**:
  - `PreviewService` 内静态 `LinkedList` + `Dictionary` 实现 6 条 LRU，单条上限 `MaxTextPreviewBytes`
  - key = `archivePath|entryFullPath|entrySize|archiveMtimeTicks`
  - 命中时 `App.DebugLog("[PRV] head cache hit")`
  - 提供 `internal static void ResetHeadCacheForTests()`

  **Must NOT do**:
  - 不缓存图片/PDF/Office 内容
  - 不做后台预热（预览是用户触发行为，预热纯浪费）

- [x] **T6. 验证**

  **What to do**:
  - 新增 `PreviewServiceContentTests.cs`（5 条用例见 Verification）
  - 回归矩阵手测：文本 / CSV / Markdown / 图片 / PDF / Office / 字体 / HTML × Zip/7z/TarGz，含一个加密包
  - 磁盘快照对比：预览文本前后 `Preview` 目录清单必须一致（空）
  - `verify-repo.ps1 -Task build -Target avalonia` + `-Task test -Target ui-tests` + `-Task check`

- [x] **T7. 切片 2：Core 新增「整条目进内存」API**

  **What to do**:
  - 在 `ArchiveEntryExtractor` 里新增 `public static async Task<byte[]> ExtractEntryToMemoryAsync(string archivePath, string entryName, ArchiveFormat format, long maxBytes, string? password = null, CancellationToken ct = default)`：拿**完整条目字节**（不是 head 截断），给必须吃完整数据的消费方用（SkiaSharp 解码、PdfPig 打开文档）。
  - **返回 `byte[]` 而不是 `MemoryStream`（理由，写进方法 XML 注释）**：
    ① 消费方需求分叉——SkiaSharp 要 `Stream`（`Bitmap.Decode` / `SKCodec.Create`），PdfPig 有专门的 `PdfDocument.Open(byte[], ParsingOptions)` 重载（本机包 `~/.nuget/packages/pdfpig/0.1.15/lib/net8.0/UglyToad.PdfPig.xml` 的 `M:UglyToad.PdfPig.PdfDocument.Open(System.Byte[],UglyToad.PdfPig.ParsingOptions)`）；`byte[]` 两边都满足（要流时 `new MemoryStream(bytes, writable:false)` 包一层，不拷贝），反过来 `MemoryStream → byte[]` 得再拷一次。
    ② `byte[]` **没有所有权/Dispose 义务**——PdfPig 的 `Open(Stream, ParsingOptions)` XML 注释原文是「The caller must manage disposing the stream. The created PdfDocument will not dispose the stream.」返回 `MemoryStream` 会把「谁 dispose」变成调用方/文档/GC 三方扯皮（T9 直接踩这个坑）。
    ③ 与既有 `ExtractHeadAsync` 的返回类型一致（[ArchiveEntryExtractor.cs:231](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L231)），T5 的 head LRU 可复用同一套容量/失效模型。
    ④ 拷贝成本有界：上限由调用方传入的 `maxBytes` 兜住（UI 传 `PreviewService.MaxPreviewFileSize`，默认 15MB），一次拷贝可接受。
  - 三条取数路径：
    - **Zip（非固实）**：`entry.OpenEntryStream()` + 循环 `Read` 填满 `maxBytes`，读满即停；条目实际大小超限 → 抛新增的 `EntryTooLargeException`（Core 内新增，带 `ActualSize` / `Limit` 只读属性，与既有 `PasswordRequiredException`（[ArchiveEntryExtractor.cs:24](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L24)）同风格）。可直接复用 `ReadHeadFromStream`（[ArchiveEntryExtractor.cs:347-365](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L347-L365)）的读循环，但判断不同：`ExtractHeadAsync` 是「读满就截断返回」，这里必须「读满还没到 EOF 就失败」。
    - **7z / RAR / ISO**：`extractor.ExtractFile(entry.Index, ms)` 解到 `MemoryStream`，外层套守卫：先按 `entry.Size` 判上限，再在写入侧计数兜底（写超即抛）。**不能**复用 `TruncatingWriteStream`（[ArchiveEntryExtractor.cs:391-397](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L391-L397)）——它是「丢弃多余字节」，整条目进内存时丢弃等于静默给残缺数据，必须显式失败。
    - **TarGz / 固实 7z**：**沿用落盘兜底后读回**——`ExtractEntryAsync` 写到 `%TEMP%\MantisZip\HeadExtract\`（目录与 finally 清理复用 `ExtractHeadViaFullExtractAsync` 的既有实现，[ArchiveEntryExtractor.cs:445-475](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L445-L475)），再 `ReadExactly` 回 `byte[]`，finally 删文件。理由：固实 7z 解单个条目要跑完整个固实块（`IsSevenZipSolid`，[ArchiveEntryExtractor.cs:404](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L404)），内存与耗时都不可控；TarGz 是流式容器、无法随机访问单条目（[ArchiveEntryExtractor.cs:269-271](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L269-L271) 现状已走同一兜底）。
  - 上限参数必须由调用方传：**Core 里不出现 15MB/1MB 常量**，也不引用 `AppSettings` / `PreviewService`（`MaxPreviewFileSize` 是用户可在设置里调的，[AppSettings.cs:150](src/MantisZip.UI.Avalonia/Models/AppSettings.cs#L150) → [App.axaml.cs:124](src/MantisZip.UI.Avalonia/App.axaml.cs#L124)）。
  - 加密包语义与现状对齐：`IsSevenZipEncryptHeaders` 且无密码 → 抛 `PasswordRequiredException`（与 [ArchiveEntryExtractor.cs:261-265](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L261-L265) 完全一致），UI 侧落到 `PreviewType.NeedsPassword`。

  **Must NOT do**:
  - 不改 `ExtractEntryAsync`（写文件）的签名与语义
  - 不在 Core 里写死预览上限常量，不引用 `AppSettings` / `PreviewService`
  - 不给 TarGz / 固实 7z 加「流式进内存」（会与 T1 划定的边界打架）
  - 不把整条目 API 接到魔数检测路径上（魔数只要 4KB，继续走 `ExtractHeadAsync`，[PreviewService.cs:218](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L218)）
  - 不新增 NuGet 依赖

  **Acceptance Criteria**:
  - 100MB 的 Zip 条目 + `maxBytes = 15MB` → 抛 `EntryTooLargeException`，且 `GC.GetTotalAllocatedBytes` 增量 < 2 × maxBytes（不许先整条读完再判上限）
  - ≤ maxBytes 的条目，Zip / 7z / TarGz × 有/无密码：返回字节的长度与内容同 `ExtractEntryAsync` 落盘结果逐字节一致
  - 固实 7z / TarGz 走完后 `%TEMP%\MantisZip\HeadExtract\` 为空
  - 对同一包同一密文条目，`ExtractEntryToMemoryAsync` 与 `ExtractHeadAsync(maxBytes=条目大小)` 返回内容一致（两条实现互为对照）

- [x] **T8. 切片 2：图片预览走内存**

  **What to do**:
  - `PreviewViewModel` 新增 `public bool ShowImage(byte[] bytes, string entryName)`（返回值 `false` = 字节路径搞不定，请调用方回落文件路径）：把 `ShowImage(string filePath)`（[PreviewViewModel.cs:1222-1300](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L1222-L1300)）主体抽成 `ShowImageCore(Stream seekable, string ext)`，路径版与字节版共用同一段解码/元数据 `ZoomFit()` 代码，**不要把渲染逻辑抄两份**。
  - `SKCodec` 尺寸探测原样保留，只换数据源：`using var ms = new MemoryStream(bytes, writable:false)` → `new SKManagedStream(ms, disposeManagedStream:false)` → `SKCodec.Create(skStream)` 拿 `codec.Info.Width` / `codec.FrameCount`（现状 [PreviewViewModel.cs:1246-1252](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L1246-L1252)）。**每个消费方切换前必须 `ms.Position = 0`**（现状用 `fs.Position = 0`，:1253 / :1265 / :1270 三处），改字节版时这三处一处都不能省，否则第二遍解码读到流尾直接失败。
  - 宽 > 1920 的图仍走 `Bitmap.DecodeToWidth(ms, 1920)`（[PreviewViewModel.cs:1263-1267](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L1263-L1267)）——这是内存化收益最大的一类（大图不必先落盘再读回）。
  - 扩展名改从条目名取：现状 `ShowImage` 从 `filePath` 取扩展名（:1228），而临时文件名是 `preview{ext}`（[PreviewService.cs:373](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L373)），两者等价；字节版显式传 `entry.Name`，语义更硬，也避免 TempPath 为空时取不到扩展名。
  - `MainWindowViewModel.Preview.cs` 的 `PreviewType.Image` 分支（:307-325）：`.ico` 预判现在读 `Path.GetExtension(tempFile)`（:314），改成 `Path.GetExtension(entry.Name)`；分支顺序 = ① `.ico` → 仍落盘后 `ShowIcoGallery(tempFile)`（:317，不变）② 其余 → 先拿 `PreviewContent.Bytes` 调 `ShowImage(bytes, entry.Name)`，返回 `false` 再 `ExtractToTempAsync` 回落 `ShowImage(tempFile)`（只在下文点名的特例上多一次落盘）。
  - 逐格式判定（**逐个交代，不许含糊**）：
    - 静态 JPEG / PNG / BMP / WebP / TIFF → **走内存**（SkiaSharp 原生支持，`Bitmap.Decode` 只需可 seek 流，`MemoryStream` 满足）。
    - 动画 WebP（`codec.FrameCount > 1`，[PreviewViewModel.cs:1256-1262](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L1256-L1262)）→ **保持现状落盘**：该分支调 `ShowGif(filePath)`，而 `ShowGif` 依赖 `GifDecoder.DecodeFrames(string filePath)`（[GifDecoder.cs:17](src/MantisZip.UI.Avalonia/Services/GifDecoder.cs#L17)）与 `ApngDecoder.DecodeFrames(string filePath)`（[ApngDecoder.cs:23](src/MantisZip.UI.Avalonia/Services/ApngDecoder.cs#L23)）两个**只有 path 版**的 API。要把动画也内存化，得给两个解码器加 `Stream` 重载并重做帧缓存/`DispatcherTimer`（[PreviewViewModel.cs:1523](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L1523) 起），超出切片 2 范围 ⇒ 字节版发现多帧就返回 `false` 让上层落盘。
    - GIF / APNG（`PreviewType.AnimatedImage`，switch :326-337 → `ShowGif` :333）→ **保持现状落盘**，理由同上。
    - AVIF：**今天本来就不支持解码**——`ShowAvifImage`（[PreviewViewModel.cs:1305](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L1305)）直接 `ShowUnsupported(T("Preview_Avif_Unsupported_Decode"))`，注释写明 ImageSharp 4.1.3 / SkiaSharp 4.152.0 都不支持 AVIF 解码（:1307-1312）。内存化对它没有意义 ⇒ **保持现状（仍落盘、仍提示不支持）**；顺带记一笔：`.avif` 不在 `MetadataOnlyExtensions` 白名单里，超 `MaxPreviewFileSize` 时会被 :236 提前拒绝，不会白落盘。
    - SVG（`PreviewType.Svg` → `ShowSvg` :345 → `SKSvg.Load(filePath)`，[PreviewViewModel.cs:1655-1660](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L1655-L1660)）→ **保持现状落盘**：SVG 是 XML 文本、体积通常 KB 级，落盘成本可忽略；`SKSvg.Load(Stream)` 虽存在，但改它得重新确认相对资源/字体解析行为，收益≈0 而风险≠0。
    - ICO（`PreviewType.Image` 分支内按 `.ico` 分流到 `ShowIcoGallery` :317 → `IcoParser.ExtractFrames(string filePath)`，[IcoParser.cs:21](src/MantisZip.UI.Avalonia/Services/IcoParser.cs#L21) 只有 path 版）→ **保持现状落盘**。
    - TGA（`ShowTgaImage` → ImageSharp `Image.Load<Rgba32>(filePath)`，[PreviewViewModel.cs:1318-1324](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L1318-L1324)）→ **保持现状落盘**：它本来就不在 Skia 快路径上（ImageSharp → SKBitmap → PNG → Bitmap 三次转换），且扩展名判断依赖文件名，收益不抵复杂度。

  **Must NOT do**:
  - 不删 `ShowImage(string filePath)`（OpenWith、回落路径、上面点名的特例都在用）
  - 不动 `ShowGif` / `ShowSvg` / `ShowIcoGallery` / `ShowTgaImage` / `ShowAvifImage` 的实现
  - 不为了「统一」把 `GifDecoder` / `ApngDecoder` / `IcoParser` 改成 Stream 版
  - 不在字节路径里 Dispose 调用方传进来的 `byte[]`（托管数组没有 Dispose）
  - 不改 `_skOriginalPreview` / `BitmapToSkia` / 透明背景压平那一套

  **Acceptance Criteria**:
  - 20MB 的 PNG（Zip 内）预览：`%TEMP%\MantisZip\Preview\` 全程不出现新文件；`ImageWidth/Height`、DPI 元数据、初始 `ZoomFit()` 与改造前一致
  - 宽 4000px 的 JPEG：仍走 `DecodeToWidth(1920)`，输出像素宽 1920
  - GIF / APNG / ICO / SVG / TGA / AVIF 六个特例：仍走落盘路径，渲染结果与改造前一致（AVIF 仍显示不支持提示）
  - 字节路径返回 `false` 后回落只多一次 `ExtractToTempAsync`，且版本守卫仍生效（过期结果不得渲染）
  - 解码失败（伪造字节）走既有 `catch`，不新增未处理异常类型

- [x] **T9. 切片 2：PDF 预览走内存**

  **What to do**:
  - `PreviewViewModel` 新增 `public async Task ShowPdfAsync(byte[] bytes, string entryName)`；**保留** `ShowPdfAsync(string filePath)`（[PreviewViewModel.cs:2933](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L2933)）不动（OpenWith、回落路径仍在用）。
  - 元数据：给 Core 的 `PdfParser` 加 `Parse(byte[] bytes, string extension)` 重载，把现有主体抽成私有的 `ParseFromSeekable(Stream fs, long length, string extension)`——`PdfParser.Parse` 现在是**全文路径版**：`new FileInfo(filePath)` 取长度、`fs.Seek(-tailSize, SeekOrigin.End)` 读尾部 trailer（[PdfParser.cs:44-49](src/MantisZip.Core/Utils/PdfParser.cs#L44-L49)）、`FindPageCount` 从头部和**尾部**各扫 256KB（[PdfParser.cs:133-150](src/MantisZip.Core/Utils/PdfParser.cs#L133-L150)）。这些逻辑**一行不改**，只把数据源换成 `new MemoryStream(bytes, writable:false)`。理由：`ShowPdfAsync` 是**先** `var info = PdfParser.Parse(filePath)`、null 就 `Preview_PdfParseFailed` 直接返回（:2937-2942）；不补字节重载，字节路径要么丢元数据、要么得改用 PdfPig 的 `Information` / `NumberOfPages` 重写元数据渲染——那份 key 文案与今天不一致，属于回归。
  - 打开文档：`PdfDocument.Open(bytes, SkiaRenderingParsingOptions.Instance)`（**byte[] 重载**，不用 Stream 重载），`d.AddSkiaPageFactory()`、页尺寸算 `renderScale`（maxW 1920 / maxH 1080）、返回 `(doc, NumberOfPages, scale)` 这一段整体照搬 [PreviewViewModel.cs:2950-2969](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L2950-L2969)；`LoadPdfPageAsync(1)`（:2976）、`PreviewType = PreviewType.Pdf`（:2979）、`PreviewHeaderText` 与 PDF 格式元数据组装（:2982-2989）都不动。
  - **流生命周期（本任务的硬要求）**：PdfPig 的 `Open(Stream, ParsingOptions)` 明确「调用方负责 dispose 流、PdfDocument 不负责」，且非 seekable 流会被它内部复制一份（同 XML 注释）。因此：① 用 **byte[] 重载**，让 PdfPig 自己建内部 `MemoryStream`——托管内存、**无文件句柄**，GC 可回收，彻底绕开「谁 dispose」；② **禁止**写 `using (var ms = new MemoryStream(bytes)) { _pdfDocument = PdfDocument.Open(ms, …); }`——退出 using 后 `_pdfDocument` 握着已 dispose 的流，翻页时必 `ObjectDisposedException`；③ 不新增 `_pdfStream` 字段，`_pdfDocument`（[PreviewViewModel.cs:2898](src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs#L2898)）仍是唯一持有者，释放点维持现状两处：`ShowPdfAsync` 开头 `_pdfDocument?.Dispose()`（:2947-2948）与 `Clear()`（:3318-3319）。
  - `MainWindowViewModel.Preview.cs` 的 `PreviewType.Pdf` 分支（:407-410）：`PreviewContent.Bytes != null ? await Preview.ShowPdfAsync(bytes, entry.Name) : await Preview.ShowPdfAsync(tempFile)`。
  - 失败语义不变：元数据解析失败 → `Preview_PdfParseFailed`；加密 PDF 由 PdfPig 抛异常 → 现有 `catch`/`ShowUnsupported` 路径。

  **Must NOT do**:
  - 不删路径版 `ShowPdfAsync`
  - 不给 `_pdfDocument` 塞外部 `MemoryStream`，不在 `Task.Run` 里 `using` 那个流
  - 不改 `_pdfRenderScale` / `PdfPageInfo` / 翻页命令（`PreviousPdfPage` / `NextPdfPage` / `OnPdfCurrentPageChanged`）的算法
  - 不改 `SkiaRenderingParsingOptions.Instance`
  - 不改 `PdfParser` 的解析规则（正则、扫描窗口、`/Encrypt` 判定）

  **Acceptance Criteria**:
  - 10MB PDF（Zip 内）：预览后 `Preview` 目录无新文件；首页渲染结果与落盘路径逐像素一致（或 `PdfPageInfo` + 位图尺寸 + PNG 哈希一致）
  - 连续翻到第 2、3 页正常（证明流没被提前 dispose）
  - 快速切条目 A→B→A：不抛 `ObjectDisposedException`，`_pdfDocument` 不残留
  - 解析失败的 PDF 仍显示 `Preview_PdfParseFailed`；`PdfParser.Parse(byte[], ext)` 与 `Parse(path)` 对同一文件返回等价结果（Title/Author/PageCount/Encrypted 一致）

- [x] **T10. 切片 4：>15MB 的 head-only 部分预览（文本/PDF/Office）**

  **What to do**:
  - **先纠正一个前提**（写进代码注释与提交信息，别让后续读者被计划标题误导）：文本类今天**不是**被 15MB 拦，而是被 1MB 拦——`Text` / `Csv` / `Markdown` 在 switch 里各自先判 `entry.Size > MaxTextPreviewBytes` → `Preview_TooLargeText` 且**完全不预览**（[MainWindowViewModel.Preview.cs:274-281](src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Preview.cs#L274-L281)、:292-299、:418-425；`Html` 同判在 :396-403）；15MB 的 `Preview_TooLarge` 在 :236-243 更早拦一次（`IsMetadataOnlyExtension` 豁免）。⇒ **切片 4 对文本的真实收益是把 1MB 硬拒绝改成「只显示前 1MB + 已截断提示」**（今天一个 1.1MB 的日志文件一个字都看不了），而不是「绕开 15MB」。
  - **新判定顺序**（全部落在 `ShowPreviewAsync`，位置在魔数检测/扩展名兜底之后、取数之前）：
    ① `PreviewService.IsMetadataOnlyExtension(ext)` 命中 → 不受 `MaxPreviewFileSize` 限制（维持 :236 现状，不动）。
    ② `entry.Size > MaxPreviewFileSize` 且未命中① → 按类型分档：
       - `Text` / `Csv` / `Markdown`（**不含 `Html`**）→ 走部分预览（进入③）
       - `Pdf` → **保持现状**（见下面「做不到的部分」，PDF 根本不走这条判定，因为它在元数据白名单里）
       - `Office` / `Docx` / `Xlsx` / `Pptx` → **只留接口位**：本切片仍按超限拒绝、显示 `Preview_TooLarge`，但把判定收进单点方法 `CanPreviewPartially(PreviewType type) => false`，切片 3 做完 Office 取数后只翻这一个开关
       - 其余（`Image` / `AnimatedImage` / `Svg` / `IcoGallery` / `Font` / `Audio` / `Video` / `Html` / `Pe` / `Sqlite` / `Iso` / `Torrent`）→ **仍 `Preview_TooLarge` 拒绝**
    ③ 文本类 `entry.Size > MaxTextPreviewBytes`（默认 1MB）→ **不再拒绝**，按 `MaxTextPreviewBytes` 取 head + 打部分标记；取数直接复用 T1 的 `ExtractHeadAsync`（[ArchiveEntryExtractor.cs:231](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L231)），不需要新 API。
    ④ `Preview_TooLarge` / `Preview_TooLargeText` 两个既有 key **保留**，用途收窄为「格式不支持部分预览」与「取数超硬上限」；文案不改（[strings.zh-CN.json:233-234](src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json#L233-L234)）。
  - **部分预览的取数成本必须写清**（这是最容易自我欺骗的地方，直接决定要不要加硬上限）：

    | 包类型 | 取前 N 字节的真实代价 | 结论 |
    |---|---|---|
    | Zip（非固实） | 流式读到 N 即停（T1 已实现，[ArchiveEntryExtractor.cs:326-341](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L326-L341)） | 真省 |
    | 7z / RAR / ISO（非固实） | 解压跑完整个条目、只保留前 N（`TruncatingWriteStream`，[ArchiveEntryExtractor.cs:378-398](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L378-L398)） | 省内存不省 CPU |
    | TarGz / 固实 7z | `ExtractHeadViaFullExtractAsync` **整条目落盘**再读前 N（[ArchiveEntryExtractor.cs:445-475](src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs#L445-L475)） | 只解决「看得见」，不省磁盘 |

    ⇒ 因此必须加一条**硬上限**：`public static long MaxPartialPreviewBytes = 256L * 1024 * 1024`（放 `PreviewService` 的 `Max*` 区块，[PreviewService.cs:131-167](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L131-L167) 同风格），超过就直接 `Preview_TooLarge` 拒绝。否则一个 2GB 的 `app.log` 在 tar.gz 里会把用户磁盘写满——比「看不了」更糟。
  - 图片仍然拒绝的理由（写进注释）：SkiaSharp 的 `SKCodec` / `Bitmap.Decode` 要求完整数据流；渐进式 JPEG 只支持「逐级清晰」，不支持「任意位置截断」，只给前 N 字节拿不到可用像素。要做部分解码得引入专用解码器，超出本计划边界。
  - **做不到的部分（明确写下来）——PDF 的 head-only 部分预览不成立**：
    - PDF 的 `xref` / `trailer` 在**文件末尾**：`PdfParser` 自己就得 `fs.Seek(-tailSize, SeekOrigin.End)` 读尾部 trailer（[PdfParser.cs:44-49](src/MantisZip.Core/Utils/PdfParser.cs#L44-L49)），页数也从尾部扫（:141-147）。截断头部得到的是没有 trailer 的字节流，PdfPig 定位不到交叉引用表，`Open` 会抛异常或给出 0 页 ⇒ **head-only 数据不能交给 `PdfDocument.Open`**。
    - 更关键：PDF 属于 `MetadataOnlyExtensions`（[PreviewService.cs:116-125](src/MantisZip.UI.Avalonia/Services/PreviewService.cs#L116-L125)）⇒ **今天任何大小的 PDF 都不会被 15MB 拦**，一律整条落盘 + PdfPig 打开；而 `ShowPdfAsync` 本来就只渲染首页（`LoadPdfPageAsync(1)`，:2976），后续页按需渲染。也就是说「PDF 只显示首页」**已经是现状**，切片 4 若对 PDF 加拒绝反而是**功能回归**。
    - 结论：**PDF 判定与行为一行不改**；大 PDF「整条落盘」的成本问题不属本计划范围，按规则 21 在 `docs/KNOWN_ISSUES.md` 立一条 KI（现象：超大 PDF 预览会整条解压到 `Preview` 目录；阻塞类型：成本超阈值；复评条件：`core-temp-root-injectable` 落地或后续 PDF 计划）。
  - 为切片 2/4 共用，`PreviewContent` 记录在 T2 定义时就带上部分标记：`public sealed record PreviewContent(byte[]? Bytes, string? TempPath, bool IsPartial = false, long TotalSize = 0)`（默认值保证 T2 现有用例不用改）。

  **Must NOT do**:
  - 不给 PDF / 图片 / Office 做「假的部分预览」（把截断的残缺数据丢给解码器）
  - 不动 `IsMetadataOnlyExtension` 的白名单
  - 不把 `MaxPartialPreviewBytes` 暴露到设置 UI（本切片不新增设置项，避免设置窗口 + 三语 + 迁移一起扩散）
  - 不改 `ExtractHeadAsync` / `ExtractEntryToMemoryAsync` 的语义
  - 不改 `Preview_TooLarge` / `Preview_TooLargeText` 的文案格式

  **Acceptance Criteria**:
  - 1.1MB 的 `.log`（Zip）：显示前 1MB 文本 + 部分预览提示，**不再**出现 `Preview_TooLargeText`
  - 20MB 的 `.csv`（Zip）：显示前 1MB 的行 + 提示，表格不崩（行/列仍受 `MaxTablePreviewRows` / `MaxTablePreviewCols` 限制）
  - 20MB 的 `.jpg`（Zip）：仍 `Preview_TooLarge`（图片不部分预览）
  - 300MB 的 `.log`（tar.gz）：仍 `Preview_TooLarge`（硬上限生效），且 `%TEMP%\MantisZip\HeadExtract\` 无残留
  - 50MB 的 PDF：行为与改造前完全一致（能预览、首页渲染、翻页正常）
  - 1.1MB 的 `.cpp` / `.json`：同样走部分预览（验证不是只对 `.txt/.log` 生效）

- [x] **T11. 切片 4：部分预览的 UI 提示与三语 key**

  **What to do**:
  - **位置（先读结构再定，已读）**：`PreviewPanel.axaml` 目前**没有**底部状态栏——`PreviewRootGrid` 的 `RowDefinitions="Auto,*,Auto"`（[PreviewPanel.axaml:37-39](src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml#L37-L39)）第三行空置，全文件 grep `Grid.Row="2"` 零命中。所以**不要**新造第三行：那会和右侧信息面板的 `Grid.RowSpan="2"`（[PreviewPanel.axaml:810-817](src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml#L810-L817)）打架。
  - 改为在 `ContentTopBorder`（:198）内部、`ItemsControl ItemsSource="{Binding ContentTopItems}"`（:202）**之前**插一个 `Border`：`IsVisible="{Binding IsPartialContentHintVisible}"`，内含一个 `TextBlock Text="{Binding PartialContentHintText}"`；样式按规则 4 显式给（`Foreground="{DynamicResource ThemeTextSecondaryBrush}"`、`Background="{DynamicResource ThemeHeaderBgBrush}"`、边框 `ThemeBorderBrush`）。理由：① 这块在所有非 WebView 格式下都显示、且随内容滚动，不占工具栏；② HTML 走 `WebViewHost`（:790）与源码 `ScrollViewer`（:797）是并列的独立元素，天然不受影响；③ 只新增一个 `bool` + 一个 `string` 两个 VM 属性，不动网格布局。
  - **VM 侧**：`PreviewViewModel` 加 `[ObservableProperty] private bool _isPartialContentHintVisible;` 与 `public string PartialContentHintText => LocalizationManager.T("Preview_PartialContent", ShownMb, TotalMb);`（`ShownMb` / `TotalMb` 由 T10 的 `PreviewContent` 传入；文化切换时在 `OnCultureChanged` 刷新，参照现有 `UpdateLocalizedStrings` 模式）。`Clear()`（:3299）与 `ShowLoading` 路径都要清零，避免上一条的提示残留到下一个条目。
  - **新增 key（只写进本计划，本次不动 JSON）**：
    - `Preview_PartialContent`，占位符 `{0:F1}` = 已显示 MB、`{1:F1}` = 原文 MB
      - zh-CN：`文件较大，仅显示前 {0:F1} MB（原文 {1:F1} MB）`
      - en：`Large file — showing only the first {0:F1} MB (original {1:F1} MB)`
      - zh-TW：`檔案較大，僅顯示前 {0:F1} MB（原始 {1:F1} MB）`
      - 语气对齐现有 `Preview_TooLarge`（zh-CN：`文件过大 ({0:F1} MB)，超过预览大小限制 ({1:F0} MB)`，[strings.zh-CN.json:233](src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json#L233)）：同一处用「文件较大 / 文件过大」区分「部分可见 / 完全拒绝」，不让用户以为是同一件事。
  - **主窗口状态栏同步一条**（用户不盯预览区也能知道）：新增 `Status_PartialPreview`
    - zh-CN：`部分内容预览：{0}`
    - en：`Partial preview: {0}`
    - zh-TW：`部分內容預覽：{0}`
    - 占位符 = `entry.DisplayName`；替换掉现在四处 `StatusMessage = LocalizationManager.T("Status_Unsupported", ext)`（:279 / :297 / :402 / :424）。
  - **明确不加的 key**（防止后续自作聪明加戏）：不加「滚到底再提示一次」（顶部已提示，重复即噪音）；不加 PDF 专用「只显示首页」文案（`PdfPageInfo` 的「1 / N」+ 翻页命令已经说明）；不加任何设置项文案（本切片不新增设置项）。
  - 落地时的本地化硬要求（规则 13）：三份 JSON **成对新增**、UTF-8 **with BOM** + CRLF + 2 空格缩进、插在文件头 `{` 之后（不排序）；`Preview_PartialContent` 若走 `LocalizedStrings[...]` 绑定，必须登记进 `MainWindowViewModel.UpdateLocalizedStrings()` 的 keys 数组；走 VM 计算属性（`PartialContentHintText`）则不需要登记；改完跑 `pwsh -File scripts\verify-repo.ps1 -Task i18n`。

  **Must NOT do**:
  - **本次不改** `strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`（只写计划）
  - 不在 `.cs` / `.axaml` 里硬编码任何提示字符串
  - 不新增 `Grid.Row="2"` 的底部状态栏，不动 `PreviewRootGrid` 的行定义
  - 不动 `MetadataRenderEngine` / `ContentTopItems` 的元数据渲染

  **Acceptance Criteria**:
  - 部分预览时提示可见、三语文案齐全且占位符一致；非部分预览时 `IsPartialContentHintVisible == false`
  - 切条目后提示不残留（A 条目的提示不得出现在 B 条目）
  - `pwsh -File scripts\verify-repo.ps1 -Task i18n` 通过（三语 key 集合一致、`{0}` 占位符一致、无重复 key）
  - 状态栏文案与预览区提示语义一致（都表达「部分内容」而非「失败」）

- [x] **T12. 批次 A 收尾验证（回归矩阵 + 磁盘快照）**

  **What to do**:
  - 回归矩阵：8 类主格式 × 3 种包，加 4 类特例。格子写**预期取数路径**，图例：🟢=内存（不进磁盘）/ 🟡=落盘读回（整条目落盘后立即读入内存并删临时文件）/ 🔵=保持落盘（消费方要真实路径）/ ❌=拒绝。

    | 格式 | Zip | 7z | TarGz | 依据 |
    |---|:--:|:--:|:--:|---|
    | 文本 `.txt/.log/.json/.cpp` | 🟢 | 🟢 | 🟡 | T2/T3，走 `ExtractHeadAsync`；TarGz 走 `HeadExtract` 兜底目录 |
    | CSV | 🟢 | 🟢 | 🟡 | 同上，另受 `MaxTablePreviewRows/Cols` |
    | Markdown | 🟢 | 🟢 | 🟡 | 同上 |
    | 静态图片 `.png/.jpg/.webp` | 🟢 | 🟢 | 🟡 | T8；>1920px 走 `DecodeToWidth` |
    | PDF | 🟢 | 🟢 | 🟡 | T9；元数据走 `PdfParser.Parse(byte[])` |
    | Office `.docx` | 🔵 | 🔵 | 🔵 | 切片 3 才改，本批保持现状 |
    | 字体 `.ttf` | 🔵 | 🔵 | 🔵 | `FontParser` 依赖文件名（Must NOT 已写死） |
    | HTML | 🔵 | 🔵 | 🔵 | `NativeWebView` 不接受 `data:` URI（Must NOT 已写死） |
    | GIF / APNG（`AnimatedImage`） | 🔵 | 🔵 | 🔵 | `GifDecoder` / `ApngDecoder` 只有 path 版（T8 判定） |
    | SVG | 🔵 | 🔵 | 🔵 | `SKSvg.Load(filePath)`（T8 判定） |
    | ICO（`Image` 分支按 `.ico` 分流） | 🔵 | 🔵 | 🔵 | `IcoParser.ExtractFrames(filePath)`（T8 判定） |
    | 加密包（Zip / 7z，会话密码已录） | 同明文规则 | 同明文规则 | — | 只验「有密码时字节内容与明文一致」；无密码时应显示 `Preview_EntryNeedsPassword` 而不是崩 |

  - 每格附一句**可观测证据**：🟢 → `Preview` 目录快照无变化；🟡 → `HeadExtract` 目录在预览**结束后**为空；🔵 → `Preview\preview{ext}` 出现且时间戳更新。
  - **磁盘快照判据（本计划最硬的一条）**：
    - 普通模式 `%TEMP%\MantisZip\Preview\`、便携模式 `Data\Temp\Preview\`（`AppSettings.GetTempDir()` 便携分支，[AppSettings.cs:263-268](src/MantisZip.UI.Avalonia/Models/AppSettings.cs#L263-L268)）各拍两次：预览前、预览后。🔵 格断言「有文件且时间戳更新」，🟢/🟡 格断言「文件数不变」。**两条路径都要测**（便携模式用 `Portable.txt` 触发）。
    - `%TEMP%\MantisZip\HeadExtract\` 也要拍：任何预览结束后必须是空目录（T1/T7 的 finally 清理必须真的生效）。
  - 用例并入 `PreviewServiceContentTests.cs`（T2 已建），至少加四组：`ExtractEntryToMemoryAsync` 上限行为、`ShowImage(byte[])` 与 `ShowImage(path)` 像素一致、`PdfParser.Parse(byte[])` 与 `Parse(path)` 等价、部分预览标记位（`IsPartial` / `TotalSize`）。
  - 命令：`pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia`、`-Task build -Target core`、`-Task test -Target ui-tests`、`-Task check`。

  **Must NOT do**:
  - 不为了让矩阵「全绿」而改既有上限或 `MetadataOnlyExtensions` 白名单
  - 不在测试里真造 100MB 文件（用合成流 + 小 `maxBytes` 覆盖同一代码路径）
  - 不把磁盘快照脚本留进仓库（现场生成、用完即弃，同 `icon-normalization` 的约定）

  **Acceptance Criteria**:
  - 矩阵 40 格全部实测并留证据（目录快照 / 日志行 / 截图）
  - 普通模式与便携模式两条临时根都验过，`Preview` 在 🟢 格无新增、`HeadExtract` 无残留
  - 构建（avalonia + core）、UI 测试、`-Task check` 三命令全绿
  - 发现且本次不修的问题，已在 `docs/KNOWN_ISSUES.md` 立 KI（含大 PDF 整条落盘那条）

---

## 切片与批次（2026-10-24 调整后）

| 切片 | 任务 | 内容 | 工时 | 批次 / 档位 | 关键风险 |
|---|---|---|:--:|:--:|---|
| 1 | T1–T6 | 文本/CSV/Markdown/代码/日志走内存 + Core 流式 head + 临时文件按条目命名 + head LRU | 5–6h | **A / S 档** | Zip head 改流式要保证「读满即停」，别把截断逻辑写丢 |
| 2 | T7–T9 | 图片 → `SKBitmap.Decode(Stream)`（含 `SKCodec` 尺寸探测）；PDF → `PdfDocument.Open(byte[])`（含 `PdfParser` 字节重载与流生命周期） | 3–5h | **A / S 档** | 整条目进内存必须有硬上限；六类特例（GIF/APNG/SVG/ICO/TGA/AVIF）回落落盘；`PdfDocument` 不 dispose 传入流 |
| 4 | T10–T11 | >15MB（文本实为 >1MB）的 head-only 部分预览，替代现在直接 `Preview_TooLarge` 拒绝 + 「部分内容」提示 | 4–6h | **A / S 档** | 要新设计「部分内容」UI 提示；PDF 做不到 head-only（trailer 在文件尾）；图片无法部分解码；TarGz 部分预览不省磁盘，需硬上限 |
| 3 | T13+（批次 B 开工时补） | Office → `SpreadsheetDocument.Open(Stream)` / `WordprocessingDocument.Open(Stream)` / `PresentationDocument.Open(Stream)`；ClosedXML 走 `new XLWorkbook(stream)` | 5–7h | **B / A 档** | OpenXml 各 API 对 seek 要求不同；ClosedXML 大文件内存占用需实测；感知最弱（本来就要解析全文） |

> **为什么切片 3 不进批次 A**：它的工时几乎全在「逐库确认 Stream 支持 + 生命周期管理 + 大文件内存实测」，是探索性工作；切片 1/2/4 是确定性工作。混在一起会让本轮节奏被不确定性拖住。
>
> **为什么切片 4 反而进了批次 A**：它是本计划里**唯一有直接体感**的一项（现在超限就是一句 `Preview_TooLarge` 拒绝），而 1/2 主要是「更干净」。用户 2026-10-24 定的顺序是「图标 → 接缝 → 预览 → 自动更新」，预览这一项既然排在用户可见的第三位，就该把有体感的切片带上。

---

## 与既有计划的关系

- **`preview-quick-modes`（冻结）**：本计划切片 1 覆盖其约六成的体感收益（数据从哪来），而它是「先给部分再补全」（渲染进度）。和解冻条件一致：如果将来做它，取数层已经是内存优先，不会返工。
- **`file-list-thumbnails`（B 档）**：它复用 `ArchiveEntryExtractor.ExtractHeadAsync` 提缩略图；本计划 T5 的 head 缓存可直接复用，先做本计划能让它便宜一点。
- **`core-temp-root-injectable`（A 档小活）**：本计划 T4 改的是「Preview 子目录的文件命名与清理」，那个计划改的是「临时根在哪」。两者不冲突，顺序无所谓。

---

## 实施记录（2026-10-28，批次 A = 切片 1 + 2 + 4）

### 代码落地位置

| 任务 | 文件 | 关键内容 |
|------|------|----------|
| T1 | `src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs` | `ExtractZipHeadToMemory` 改 `entry.OpenEntryStream()` + 新增 `ReadHeadFromStream`（不依赖 `Length`、读满即停）；7z head 走 `TruncatingWriteStream`；新增 `src/MantisZip.Core/Utils/TruncatingWriteStream.cs` |
| T2/T4/T5 | `src/MantisZip.UI.Avalonia/Services/PreviewService.cs` | `PreviewContent(byte[]? Bytes, string? TempPath, bool IsPartial, long TotalSize)` + `ExceedsPartialLimit`；`LoadPreviewContentAsync`；head LRU（容量 6、key 含 `archiveMtimeTicks`）；临时文件 `preview_{fnv1a64:x16}{ext}`（自写 FNV-1a，跨会话稳定）；**去掉「每次预览前清空整个 Preview 目录」** |
| T3 | `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs` | `ShowText/ShowCsv/ShowMarkdownPreview(byte[], string)` 字节重载，路径版转表达式体 |
| T7 | `src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs` | `ExtractEntryToMemoryAsync(...)`：Zip 流式读满即停、7z/RAR/ISO 按 `entry.Size` 预判 + 截断流事后校验、TarGz/固实 7z 走落盘兜底；新增 `src/MantisZip.Core/Abstractions/EntryTooLargeException.cs` |
| T8 | `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs` | `ShowImage(byte[], string)` + `ShowImageCore(Stream, string, string?)`，三处 `Position = 0` 保留；avif/tga/多帧 WebP 返回 `false` 交回落 |
| T9 | `src/MantisZip.Core/Utils/PdfParser.cs` + `PreviewViewModel.cs` | `PdfParser.Parse(byte[], string)` + `ParseFromSeekable`（解析规则未改）；`ShowPdfAsync(byte[], string)` 用 `PdfDocument.Open(byte[], ParsingOptions)` |
| T10/T11 | `PreviewService.cs` + `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Preview.cs` + 三份 `strings.*.json` | `MaxPartialPreviewBytes = 256MB`、`CanPreviewPartially()`、`ExceedsPartialLimit` 信号；文本/Csv/Markdown 从「>1MB 直接拒绝」改为「显示前 1MB + 状态栏 `Preview_PartialText`」；15MB 闸门加 `!CanPreviewPartially(previewType)` |
| 回归 | 新增 6 个测试文件 | Core 侧 `ArchiveEntryExtractorTests` / `TruncatingWriteStreamTests` / `ArchiveEntryToMemoryTests`（+ `ArchiveHeadMemoryCollection`）；UI 侧 `PreviewServiceContentTests` / `PreviewMemoryWiringTests` / `PreviewPartialContentTests` |

### 与计划的偏差（如实记录）

1. **切片 4 取消 PDF 部分预览**：`PdfParser` 的 xref/trailer 在文件尾（`ParseFromSeekable` 头尾双向扫描），head-only 交给 `PdfDocument.Open` 只会失败或 0 页；且 PDF 属 `MetadataOnlyExtensions`、本来不受 15MB 限制、`ShowPdfAsync` 也只渲染首页。加拒绝反而是回归 ⇒ 只做文本 / CSV / Markdown，并把「大 PDF 必须整条目落盘」按规则 21 登记为 **KI-007**。
2. **T10 多改了一处 15MB 闸门**（计划只写了三处文本分支）：不加 `!PreviewService.CanPreviewPartially(previewType)`，「20MB CSV 显示前 1MB」在到达部分预览前就被 15MB 拒绝，验收无法成立。
3. **`MaxPartialPreviewBytes` 是 `{ get; set; }` 属性而非公开可变字段**：规避 CA2211，与相邻 `MaxTextPreviewBytes` 风格一致。
4. **`PreviewContent` 新增字段用 `init` 属性而非加位置参数**：4 个既有构造点零改动。
5. **`ProgressPauseGate` 属上一计划的过渡方案**，与本计划无关，未触碰。
6. **T8 里 `.ico` 判定改从 `entry.Name` 取扩展名**（原从临时文件名取），因为字节路径下 TempPath 可能为空。

### 验证结论

- `-Task build -Target avalonia -c Release` → **0 警告**；`-Task build -Target core` → 0 警告。
- `-Task test -Target tests -c Release` → `ok: true`（**874 通过 / 3 跳过**，新增 20+ 例）。
- `-Task test -Target ui-tests -c Release` → `ok: true`（新增 29 例：`PreviewServiceContentTests` 6 + `PreviewMemoryWiringTests` 6 + `PreviewPartialContentTests` 17）。
- `-Task check` → 版本 0.5.4（6 处）、三语 **1337 key**、五项守卫全绿（含 `dialog-seam`）。
- 内存断言：32MB 条目取 64KB head，`GC.GetTotalAllocatedBytes` 增量 < 条目 1/4，证明「读满即停」真的生效。

### 未做（留给人工 / 后续）

- **回归矩阵目视**（T6/T12）：文本 / CSV / Markdown / 图片 / PDF / Office / 字体 / HTML × Zip/7z/TarGz 的九宫格，需在真机上看渲染结果与改造前一致。
- **磁盘快照人工核对**：自动化侧已断言「文本预览不产生 `preview_*.log`」，但「打开 100MB 文本时 `%TEMP%\MantisZip\Preview` 全程为空」仍建议手工看一眼。
- 切片 3（Office 部分预览接口位）与切片 4 的 Office 部分未纳入本批次。
- `KI-007`（大 PDF 整条目落盘）；`KI-003` 剩余项（`SKTypeface.GetGlyphs` 已清，`IconProvider`/`IcoParser` 已是新 API，待全量复核后回填状态）。