# 卡片元数据文字（选中项）

> **档位**: A 档 · v0.5.6 追加项（缩略图列表体验的收尾）
> **状态**: ✅ 已完成（2026-11-02 归档，随 v0.5.6；实施记录见文末）
> **预估工时**: 2-3 小时（实际约 2.5 小时，含真机三轮回炉）

## 背景

卡片布局（平铺 / 内容）里，没有缩略图、也没有文本摘录的条目只能显示系统大图标——音频显示一个播放图标、
字体显示一个字体图标，用户看不到任何「这个文件到底是什么」的信息。

但仓库里**已经有**这些格式的解析器（`src/MantisZip.Core/Utils/` 下的 `FlacParser` / `RiffParser` /
`Id3v2Parser` / `VideoParser` / `FontParser` / `PeParser` 等，全部 `static FileFormatInfo? Parse(string filePath)`），
预览面板也正是靠它们显示「时长 / 分辨率 / 字形数 / 产品版本」。

用户（2026-11-02）提出：能不能像文本缩略图那样，把这些元数据压成几行文字显示在卡片上？

## 决策

| 决策点 | 结论 | 理由 |
|---|---|---|
| 覆盖范围 | **只做「当前选中」的那一张卡** | 每张卡都读一遍条目 = 再叠一遍缩略图的 I/O；选中项只处理一个文件，代价可忽略 |
| 首批格式 | 音频（flac/wav/mp3）、视频（mp4/mkv/mov/avi/webm/m4v）、字体（ttf/otf/ttc）、可执行（exe/dll） | 这四族解析器现成、字段密度高；Office / PDF 需要整包读（OOXML 要中央目录、PDF 的 xref 在文件尾），留后续 |
| 取数方式 | 整条目落临时文件 → 现有解析器 → 纯函数排版 | 解析器只吃 `filePath`，加流/byte[] 重载是 11 个文件的大改动；选中项一次一个文件，落盘可接受 |
| 卡片优先级 | 缩略图 > 文本摘录 > 元数据 > 大图标 | 三者互斥，避免同一张卡叠两层文字 |
| 字段取舍 | 每格式 2–3 行，**只有值不带标签** | 128 px 卡片一行只剩 ~110 px（≈20 个半角字符），标签会吃掉一半 |
| 设置项 | 不加开关 | 只在选中卡上出现，不打扰；要关掉的话用详情布局即可 |

## 实现

| # | 文件 | 内容 |
|---|------|------|
| T1 | `src/MantisZip.UI.Avalonia/Services/MetadataCardFormatter.cs` | `enum MetadataCardKind{None,Audio,Video,Font,Executable}`、`Classify(fileName)`（按扩展名）、`BuildLines(kind, info, labels)` 纯函数（≤3 行，空行丢弃）、`MetadataCardLabels` 文案表（`FromLocalization()` 从三语 json 取，测试传固定表） |
| T2 | `src/MantisZip.UI.Avalonia/Services/MetadataCardService.cs` | `LoadLinesAsync(archivePath, entry, format, password, ct)`：`Classify` → 超 64 MB 直接返回空 → `ArchiveEntryExtractor.ExtractEntryAsync` 落 `%TEMP%\MantisZip\MetaCard\meta_{fnv16}{ext}`（独立目录，不跟预览抢同一个临时文件）→ 按格式族调解析器 → `BuildLines`；异常一律吞掉记一行日志 |
| T3 | `src/MantisZip.UI.Avalonia/Models/ArchiveItemModel.cs` | 新 `MetadataCardLines`（`IReadOnlyList<string>?`）+ `ShowMetadataCard`；`ShowLargeIcon`/`ShowSmallIcon` 加 `!ShowMetadataCard` 条件 |
| T4 | `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.MetadataCard.cs`（新 partial） | `RefreshMetadataCard(entry)`：取消上一轮 → 清旧卡文字 → 详情布局短路 → 发起新一轮；`LoadMetadataCardAsync` 回写（`ConfigureAwait(true)` + 归属条目二次确认）；`OnSelectedEntryChanged` 与 `OnLayoutModeChanged` 各接一处 |
| T5 | `src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml` | `ThumbnailContentCard` 里加 ②b 元数据卡（`Border` + `ItemsControl`，11 px、次要前景色、行距 2），插在文本卡与兜底图标之间 |
| T6 | 三份 `Localization/strings.*.json` | 5 个 key：`MetadataCard_Mono` / `_Stereo` / `_ChannelFmt` / `_GlyphFmt` / `_TableFmt` |
| T7 | `tests/MantisZip.UI.Avalonia.Tests/MetadataCardFormatterTests.cs`（新）+ `ArchiveItemModelCardIconTests.cs` | 17 + 3 例：扩展名分类 12 个 Theory、四种格式的排版、>1 小时时长、非整百采样率、空信息、`MaxLines` 上限、`null` 标签抛异常；模型层三条互斥优先级 |

## 验收口径

- [x] 选中音频卡显示「时长 · 码率」+「采样率 · 声道」；有 ID3 标题时第三行显示「标题 · 艺术家 · 专辑」。
- [x] 选中视频卡显示「分辨率 · 时长」+「编码 · 容器名」。
- [x] 选中字体卡显示「字体名 · 字重」+「字形数」。
- [x] 选中可执行文件显示「产品名 · 版本」+「公司 · 架构 · 子系统」。
- [x] 改选下一个条目时，**上一张卡的文字立刻消失**（不等异步返回）。
- [x] 详情布局不触发取数；从详情切回卡片布局时，当前选中项立刻补出元数据卡。
- [x] 卡片里不会出现「大图标 + 文字」叠在一起。
- [x] 解析失败 / 非法数据 / 超 64 MB 时静默回退大图标，不弹错、不写坏列表。

## 实施记录（2026-11-02）

### 与计划的偏差

1. **临时文件放独立目录**（`%TEMP%\MantisZip\MetaCard\`）而不是复用 `PreviewService.ExtractToTempAsync`：
   后者维护「上一次的临时文件」单槽，预览和卡片同时作用于同一个条目时会互相删文件。代价是这条路径自己
   维护一份单槽清理（`MetadataCardService._lastTempFile`）。
2. **位深（`16 bit`）不进卡片**：第一版把「时长 · 码率 · 采样率 · 声道 · 位深」拼成一行，实测在 128 卡片里
   折成「… · 16」+「bit」两行，非常难看。改成两条技术行并去掉位深（预览的元数据面板里仍有）。
3. **视频行序调整**：第一版「分辨率 · 时长 · 编码」会把编码挤到第二行行首、行尾留下一个多余的分隔符
   「1080×1080 · 0:07 ·」。改成「分辨率 · 时长」/「编码 · 容器名」/「标题（如有）」。
4. 计划外的 **`MetadataCardLabels` 文案表**：`BuildLines` 里原本直接写「单声道 / 立体声 / {0} 声道」，
   违反规则 13（用户可见文案必须走本地化）；同时纯函数测试不该依赖当前语言。抽出不可变表，生产端
   `FromLocalization()`、测试端传固定值。

### 实测（真机）

测试包 `%TEMP%\mz-meta.zip`（`alarm.wav` 480 KB / `arial.ttf` 1.0 MB / `clip.mp4` 2.4 MB / `MantisZip.exe` 224 KB /
`MantisZip.UI.Avalonia.dll`）：

| 条目 | 卡片显示 |
|---|---|
| `alarm.wav` | `0:05 · 705 kbps` / `22050 Hz · 立体声` |
| `arial.ttf` | `Arial · Normal` / `4,503 字形` |
| `clip.mp4` | `1080×1080 · 0:07` / `H.264 · MP4 视频` |
| `MantisZip.exe` | `PE 可执行文件` / `x64 · Other`（该 exe 是 .NET apphost，没有版本资源 ⇒ 回退 `DisplayName`） |

### 验证

- Release `--no-incremental` 构建 ✅ 11.5s / 0 警告（`-Task build -Target avalonia`）。
- `-Task test -Target ui-tests` ✅ `ok=True`（750 例，其中新增 20 例）。
- `-Task i18n` ✅ 1428 key（1423 + 5），BOM/CRLF 复核通过。

### 已知留白

- **改选回上一个条目会重读一次**：没有做卡片级缓存（选中项一次一个文件，收益不明显）；
  如果以后改成「所有卡都显示」，必须补缓存并与 `ThumbnailService` 共用并发上限。
- **视频 moov 在尾部也能解析**：走的是整条目落盘，不是读头，所以非 faststart 的 mp4 也能取到时长；
  代价是这类文件会整条写到临时文件（≤64 MB）。
- 已知限制：`PeParser` 对没有版本资源的可执行文件只能给 `DisplayName` + 架构 + 子系统，这是数据上限，
  不是排版问题。
