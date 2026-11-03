# 卡片元数据：头部窗口 + 小文件整条兜底

> **档位**: A 档 · v0.5.7（2026-11-03 立项；原 v0.5.6「卡片元数据文字（选中项）」已撤回）
> **工作量**: 12-16h
> **状态**: 📋 未开始

## 1. 背景

平铺 / 内容布局里，没有缩略图也没有文本摘录的条目只能显示一枚系统大图标 —— 音频是播放图标、字体是字体图标、exe 是通用应用图标，用户看不出「这是什么、多长、什么编码」。

Core 里早就有这些格式的解析器（预览面板正是靠它们显示时长、分辨率、字形数、产品版本），只是它们统一吃「磁盘文件路径」，而包内条目要先落盘才能用。

**v0.5.6 做过一版「选中项才显示」，已撤回。** 理由：只在选中的那张卡上取数，每换一次选中就读一遍条目；而从产品上看真正有价值的是「所有候选卡都能显示」（浏览时一眼扫过去），两者取数模型不同（前者按选中触发、后者要批量预取 + 缓存 + 限流），塞在同一版里会互相将就。撤回记录见 `docs/progress-avalonia-detail.md` 2026-11-03 条目。

## 2. 目标与非目标

**做**
- 平铺 / 内容布局里，**所有**候选卡（不是选中项）显示 2–3 行格式元数据。
- 覆盖：音频 / 视频 / 字体 / 可执行 / PDF / Office（docx/xlsx/pptx）/ BT 种子 / SQLite / ISO。
- 取数**不再落临时文件**：读条目头部窗口（默认 256 KB），解析器改吃 `Stream` / `byte[]`。
- 6 组按类型开关 + 「外观 → 缩略图」第 4 张卡片；关掉的类型不读任何字节。
- 批量预取与缩略图同构：并发上限、可取消、结果缓存。

**不做**
- 不复活「选中项升级显示」那一层（用户明确不要）。
- 不做行级「在卡片里显示哪些字段」的自定义（先用固定 3 行模板；`MetadataPanelSettings` 那套字段级配置仍只服务预览面板）。
- 不动详情（表格）布局的图标列。
- 不给 >10 MB 的非 faststart 视频做尾段解析（成本不成比例，见 §4 C 档）。

## 3. 五档分级（按「读多少」划分）

| 档 | 读多少 | 覆盖格式 | 说明 |
|---|---|---|---|
| **A** | 头部 256 KB | WAV / AVI（RIFF `fmt`/`avih`）、FLAC（STREAMINFO）、MP3（ID3v2 + Xing）、MKV / WebM（EBML Info/Tracks）、**BT 种子**（bencode 全在开头）、**SQLite**（前 100 字节 header）、**ISO**（PVD 在偏移 32768）、PE 头、字体头 | 这些格式的关键字段天然在文件开头，头部窗口就够 |
| **B** | 头 + 整条（≤2 MB 兜底） | 字体（名 / 字重 / 字形数 / 表数）、PE 版本资源（产品名 / 公司 / 版本）、PDF（`PageCount`/`Title`/`Author`）、OOXML docx/xlsx/pptx、FLAC / OGG 标签 | 头部拿不到、但「整条读进内存」对小文件很便宜 |
| **C** | 头 + 尾（entry ≤10 MB） | MP4 / MOV / M4A（`moov` 在尾） | Core 已有 `ArchiveEntryExtractor.ExtractTailSync`，但它是「整条解出再截尾」⇒ zip 只对 ≤10 MB 生效；固实 7z 返回 null |
| **D** | 不读 | >10 MB 非 faststart 视频、.doc/.xls/.ppt 旧格式、zip 类清单、无解析器的格式、图像类（缩略图优先） | 明确放弃，卡片显示兜底大图标 |

**两条硬防线**
1. **固实 7z 整批跳过**：`ArchiveEntryExtractor.ExtractHeadAsync` 在固实 7z 上会退化成整条解压（`ExtractHeadViaFullExtractAsync`），一张卡一次几秒 ⇒ 直接不显示元数据。
2. **视频尾段只对 ≤10 MB**：`ExtractTailSync` 是整条解出再截尾，>10 MB 等于每次读整片。

## 4. 取数层改造（本计划的主要成本）

### 4.1 解析器改吃 Stream
现在 6 个解析器都是 `public static FileFormatInfo? Parse(string filePath)`，内部 `new FileStream(...)` + `Seek`，且有 `try/finally` 关闭。改成：

```csharp
public static FileFormatInfo? Parse(Stream stream);              // 主入口（不负责 Dispose）
public static FileFormatInfo? Parse(string filePath);            // 转调：打开文件 → Parse(stream)
public static FileFormatInfo? Parse(byte[] bytes, string ext);   // 转调：new MemoryStream(bytes, writable:false)
```

要点：
- `Parse(Stream)` **不得** Dispose 调用方的流；需要「定位」的地方显式 `stream.Position = x`（调用方若给的是不可 Seek 的流，先整体读进内存再包 `MemoryStream`）。
- 涉及：`FlacParser` / `RiffParser` / `Id3v2Parser` / `VideoParser` / `FontParser` / `PeParser`（`src/MantisZip.Core/Utils/`）。
- `PdfParser` / `OfficeParser` 已有 `Parse(byte[], string)` 重载（PdfPig / OpenXml 都能吃 `MemoryStream`），本计划只是接上；`TorrentParser` / `SQLiteParser` / `IsoParser` 需要补「从头部字节解析」的入口。

### 4.2 卡片取数服务
新建 `src/MantisZip.UI.Avalonia/Services/CardMetadataService.cs`（`internal static`）：

```csharp
internal static Task<IReadOnlyList<string>> LoadAsync(
    string archivePath, ArchiveItemModel entry, ArchiveFormat format,
    string? password, string? fileNameEncoding, CancellationToken ct);
```

流程：
1. `CardMetadataKind kind = CardMetadataFormatter.Classify(entry.Name)`；`kind == None` 或该组开关关闭 ⇒ 返回空（**0 I/O**）。
2. A/B/C 档判定（按 `kind` + `entry.Size`）。
3. 取字节：
   - A：`ArchiveEntryExtractor.ExtractHeadAsync(archivePath, entry.FullPath, HeadWindowBytes, format, password, ct, fileNameEncoding)`（zip 为真流式，`src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs:246`）。
   - B（≤2 MB）：`ExtractEntryToMemoryAsync(..., maxBytes: 2 MB, ...)`（`:333`）。
   - C（≤10 MB）：`ExtractTailSync` + 头部窗口拼接（或整体读入，按实现成本取一）。
4. 调对应解析器 → `CardMetadataFormatter.BuildLines(kind, info, labels)`。
5. 任何异常只记一行 `App.DebugLog` 并返回空（绝不抛）。

**不再落 `%TEMP%\MantisZip\MetaCard\`**（v0.5.6 那版的临时目录整体删除）。

### 4.3 批量预取
新建 `src/MantisZip.UI.Avalonia/Services/CardMetadataBatch.cs`，仿 `Services/ThumbnailService.cs`：
- 并发上限 2（元数据解析比缩略图重，且要读条目）。
- `Start(entries)` / `Cancel()`；进入新目录、切换布局、改设置即取消上一批。
- 结果写 `ArchiveItemModel.MetadataCardLines`（UI 线程回写，逐条触发，卡片即时亮起来）。
- 结果缓存：`ConcurrentDictionary`，key `archivePath|entryPath|size`（大小变了必然要重读）。

## 5. 模型与卡片

- `src/MantisZip.UI.Avalonia/Models/ArchiveItemModel.cs`：`[ObservableProperty] private IReadOnlyList<string>? _metadataCardLines;` + `ShowMetadataCard => !HasThumbnailContent && MetadataCardLines is { Count: > 0 }`；`ShowLargeIcon` / `ShowSmallIcon` 各加 `&& !ShowMetadataCard` ⇒ 四级互斥「缩略图 > 文本摘录 > 元数据 > 兜底大图标」。
- `src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml`：`ThumbnailContentCard` 里在文本卡与兜底图标之间插 ②b（`Border` + `ItemsControl ItemsSource="{Binding MetadataCardLines}"`，`FontSize 11` / `TextWrapping="Wrap"` / `TextTrimming="CharacterEllipsis"` / `ThemeTextSecondaryBrush`）。
- 行模板（与 v0.5.6 那版相同的排版结论，保留）：
  - 音频：「时长 · 码率」/「采样率 · 声道」/「标题 · 艺术家 · 专辑」
  - 视频：「分辨率 · 时长」/「编码 · 容器名」/「标题」
  - 字体：「字体名 · 字重」/「N 字形 · N 表」
  - 可执行：「产品名 · v版本」/「公司 · 架构 · 子系统」
  - PDF / Office：「N 页」/「标题 · 作者」
  - BT 种子：「总大小 · N 个文件」/「创建时间」/「Tracker」
  - SQLite：「N 页 · 页大小」/「编码」
  - ISO：「卷标」/「N 个扇区」
  - 最多 3 行、空行丢弃、只有值不带标签（128 卡片一行约 110 px ≈ 20 个半角字符）。

## 6. 设置项

`AppSettings` 加 6 个 `bool`（默认全 `true`）：`CardMetadataAudio` / `_Video` / `_Font` / `_Executable` / `_Document`（pdf + office）/ `_Other`（torrent + sqlite + iso）。

UI：`Views/SettingsWindow.axaml` 的「外观 → **缩略图**」子页签第 4 张卡片「卡片元数据（平铺 / 内容布局）」，6 个 `CheckBox` + 一行说明「关掉的类型不读取任何内容，卡片显示系统大图标」。保存后触发 `App.CardMetadataStyleChanged` → 主窗清缓存并重跑当前批次（仿 `ThumbnailStyleChanged`）。

## 7. 任务拆分

| # | 任务 | 文件 | 估时 |
|---|---|---|---|
| T1 | 解析器改吃 `Stream` / `byte[]`（6 个 + 3 个补入口） | `src/MantisZip.Core/Utils/*Parser.cs` | 3-4h |
| T2 | `CardMetadataFormatter`（纯函数：分类 + 排版 + 本地化标签） | `src/MantisZip.UI.Avalonia/Services/CardMetadataFormatter.cs` | 1-2h |
| T3 | `CardMetadataService`（头部窗口 / 整条兜底 / 尾段 / 异常兜底） | `src/MantisZip.UI.Avalonia/Services/CardMetadataService.cs` | 3-4h |
| T4 | `CardMetadataBatch`（并发 2 / 取消 / 缓存 / 版本守卫） | `src/MantisZip.UI.Avalonia/Services/CardMetadataBatch.cs` | 2h |
| T5 | 模型 + 卡片 ②b 块 | `Models/ArchiveItemModel.cs`、`Controls/ArchiveContentView.axaml` | 1h |
| T6 | 设置项 + 设置窗第 4 张卡片 + 即时生效 | `Models/AppSettings.cs`、`ViewModels/SettingsWindowViewModel.cs`、`Views/SettingsWindow.axaml(.cs)`、`App.Theming.cs` | 2h |
| T7 | i18n（6 个开关文案 + 说明 + 9 组元数据标签，三语） | 三份 `strings.*.json` | 1h |
| T8 | 测试：分类 / 排版 / 各档取数（含固实 7z 跳过、>2 MB 字体、>10 MB 视频） | `tests/MantisZip.UI.Avalonia.Tests/`、`tests/MantisZip.Tests/` | 2-3h |

## 8. 验收口径

1. `%TEMP%` 下不再出现 `MantisZip\MetaCard\` 临时文件。
2. 平铺布局打开一个含 wav / flac / mp3 / mp4 / mkv / ttf / exe / pdf / docx / torrent / sqlite / iso 的包：**所有**这些卡都在 1 秒内（不阻塞 UI）显示 2–3 行元数据，滚动时无卡顿。
3. 固实 7z 包：卡片不显示元数据，且不出现长时间卡顿。
4. 关掉「视频」开关后，视频卡不再读字节（可用 `App.DebugLog` 取数日志或临时计数断言），显示兜底大图标。
5. 只有 16/32 帧的老 `.ico`、>10 MB 非 faststart `.mp4`、.doc 旧格式：都退化为兜底大图标，不报错、不卡顿。
6. `-Task build -Target core` / `-Target avalonia`（Release `--no-incremental`）0 警告；`-Task test -Target tests` / `-Target ui-tests` 全绿；`-Task i18n` / `-Task check` 全绿。

## 9. 风险

| 风险 | 应对 |
|---|---|
| 解析器改 `Stream` 改动面大（6 个文件 + 现有调用方） | 保留 `Parse(string)` 签名不动，只把主干挪进 `Parse(Stream)`；现有调用方零改动，编译期即可验证 |
| 头部窗口不够（某些 MP3 / MKV 的标签在尾） | 落到 B 档（≤2 MB 整条兜底）；>2 MB 显示「音频 · 24 MB」这类降级行，不再追求完整 |
| 批量解析拖慢打开大目录 | 并发 2 + 只对视口内首次出现的条目触发 + 结果缓存；仍不够就加「滚动停稳后再取」的 200 ms 防抖 |
| 固实 7z 判断不准 | 复用 `MainWindowViewModel.DetectSevenZipSolid` 的保守策略（判不准按固实处理） |
