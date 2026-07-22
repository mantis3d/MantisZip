# MantisZip — Agent Guide

## Project overview

WPF→Avalonia 迁移中的压缩/解压桌面应用。当前存在两个 UI 项目并存：

| 项目 | 框架 | 状态 | 目标 |
|------|------|------|------|
| `MantisZip.UI` | WPF (`net9.0-windows`) | 🟡 维护模式，迁移完成后废弃 | 遗留版本 |
| `MantisZip.UI.Avalonia` | Avalonia (`net9.0`) | 🟢 主力开发 | 迁移目标，完成后废弃 WPF |

三个项目共享：`MantisZip.Core` (class library) + `MantisZip.ShellExt` (COM 组件 class library)。

**迁移完成后的计划**：
- 废弃 `MantisZip.UI`（WPF）项目

## Quick start

```powershell
# 构建 Avalonia 版（当前主力）
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj

# 运行 Avalonia 版（Windows）
dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj

# 构建 WPF 版（遗留）
dotnet build src\MantisZip.UI\MantisZip.UI.csproj

# 运行 WPF 版
dotnet run --project src\MantisZip.UI\MantisZip.UI.csproj

# Tests（Core 层测试，与 UI 框架无关）
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj

# Avalonia 测试
dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj
```

## Architecture

### 迁移阶段状态

Avalonia 移植 Phases 0–10 已完成，当前处于功能补齐后期：

| Phase | 内容 | 状态 |
|-------|------|------|
| 0–9 | 项目骨架·浏览·预览·设置·压缩解压·编辑·样式·交互 | ✅ 已完成 |
| 10 | WPF 功能补齐（进度条·信息面板·状态栏） | ✅ 已完成 |
| UI 功能补齐 | 对话框·控件·转换器补齐 | 27/29 🟡(补齐中) |
| Shell/COM 集成 | ShellIntegration·ShellExt·文件关联·CLI | 📋 待实施 |
| i18n + 清理 | 缺失 key 补齐·空目录清理·版本对齐 | ✅ 已完成 |
| HTML 预览升级 | 跨平台 WebView + ReverseMarkdown 降级 | 📋 待实施（恢复 WebView 双轨方案） |

### 依赖流向

```
                    ┌─── MantisZip.UI (WPF) ──reference──┐
                    │   (net9.0-windows, 待废弃)          │
                    │                                     │
MantisZip.Core ──────┤                                     ├── MantisZip.ShellExt (COM)
(net9.0)            │                                     │   (Explorer.exe 宿主)
                    │   MantisZip.UI.Avalonia ──reference─┘
                    │   (net9.0, 主力开发)
                    │
               ZipEngine    SevenZipEngine    TarGzEngine
              (SharpCompress) (SharpSevenZip) (SharpCompress)
```

### Engine pattern (strategy + factory)

所有引擎均在 `MantisZip.Core`，与 UI 框架无关：

- `IArchiveEngine` interface: `ListEntriesAsync`, `ExtractAsync`, `CompressAsync`, `TestArchiveAsync`
- `ArchiveEngineFactory` registers engines in static constructor, dispatches by file extension
- `SevenZipEngine.CompressAsync` uses `SharpSevenZipCompressor` (7z.dll COM binding)
- `ArchiveEntryExtractor` (Core/Utils) handles single-entry extraction for preview; only supports Zip and 7z

### Progress reporting

- `ArchiveProgress` (Core/Abstractions/ArchiveEngine.cs): `PercentComplete` (overall, 0–100), `FilePercentComplete` (nullable double, 0–100 for per-file granularity), `FileName` (current file name), `Message`
- `ZipEngine` reports per-file progress via buffered I/O copy loop with 100ms throttle; reports initial 0% and final 100% for each file
- `SevenZipEngine.ExtractAsync` and `TarGzEngine.ExtractAsync` report progress only at completion (100%)
- `SevenZipEngine.CompressAsync` reports progress via `SharpSevenZipCompressor.Compressing` event

### ArchiveItem duality

- **Core**: `MantisZip.Core.Abstractions.ArchiveItem` — engines produce these
- **WPF UI**: `MainWindow.xaml.cs` defines a subclass `ArchiveItem : Core.Abstractions.ArchiveItem` adding `DisplayName`, `SizeDisplay`, `NameDisplay`, `SortOrder`
- **Avalonia UI**: `ArchiveItemModel` (Models/) wraps `ArchiveItem` with `IconSource`, display properties, `SizeRatio` (progress bar width), `SortOrder`

### UI 模式：项目间差异

#### WPF（遗留版）：code-behind

Despite using `CommunityToolkit.Mvvm`, **all logic lives in `MainWindow.xaml.cs`**. No ViewModel classes exist. The `FolderNode` class at the bottom of that file implements `INotifyPropertyChanged` for TreeView binding only.

#### Avalonia（主力版）：MVVM

使用 `CommunityToolkit.Mvvm` 的 `ObservableObject` + source generators (`[ObservableProperty]`, `[RelayCommand]`)：

- **ViewModels**: `MainWindowViewModel`, `PreviewViewModel`, `ProgressViewModel`, `CompressSettingsViewModel`, `ExtractSettingsViewModel`, `SettingsWindowViewModel`, `IconTestViewModel`（图标测试窗口）
- **Services**: `ArchiveService`, `CompressService`, `ExtractService`, `PreviewService`, `IconService`, `LocalizationManager`, `CompressionOptionData`（选项数据源）、`GifDecoder`（自实现 GIF 解码）、`IcoParser`（ICO 多帧解析）、`MarkdownPreviewBuilder`（Markdig AST→控件树）、`ResultPreviewService`（结果预览树）
- **Views**: `MainWindow.axaml`, `PreviewPanel.axaml` (UserControl), `SettingsWindow.axaml`
- **Controls**: `ResultTreeView`（结果预览可复用控件，Compact/Full 模式、截断/过滤/冲突高亮）
- **Converters**: `BrushResourceConverter`（主题色键→画刷）、`GeometryResourceConverter`（资源键→Geometry）
- **紧凑度模式**: Compact/Normal/Loose 三档，`ApplyCompactness()` 运行时注入 12 个 `DynamicResource`（间距/控件高度/圆角），无需重启
- **上下文工具栏**: 目录树工具栏（展开/折叠全部+过滤器+分隔符切换）+ 文件列表工具栏（选择/反选/展平/排序/地址栏），`PathIcon` 矢量按钮
- 对话框通过 ViewModel 的回调委托（`ShowPasswordDialog`, `ShowExtractSettingsDialog` 等）与 View 解耦

### 预览子系统

#### WPF 版（遗留）

预览入口在 `MainWindow/Preview/` 的多个 partial 文件，code-behind 模式：

- `MainWindow.Preview.cs` — 入口 + 格式分发
- `MainWindow.Preview.Image.cs` — 图片/GIF（`WpfAnimatedGif`）
- `MainWindow.Preview.Metadata.cs` — PE/PDF/字体/音视频等元数据
- `MainWindow.Preview.Text.cs` — 文本/CSV
- `MainWindow.Preview.Web.cs` — HTML/Markdown/SVG（WebView2）
- WebView2 用于 HTML/Markdown/SVG/PDF 渲染（网络请求已拦截），Avalonia 版已移除 WPF 的 WebView2 依赖
- `PreviewWebView2` 控件名

#### Avalonia 版（主力）

预览系统在独立的 `PreviewPanel.axaml` (UserControl) + `PreviewViewModel` + `PreviewService`，MVVM 模式：

- **预览类型枚举**: `PreviewType` (Services/PreviewService.cs): `None`, `Text`, `Csv`, `Pe`, `Image`, `Gif`, `Svg`, `Font`, `Audio`, `Sqlite`, `Iso`, `Torrent`, `Office`, `Video`, `Html`, `Markdown`, `Pdf`, `IcoGallery`, `Unsupported`
- **格式分发**: `PreviewService.ClassifyPreviewByMagicAsync()`（魔数优先）→ `PreviewViewModel.ShowXxx(filePath)` 方法，扩展名回退
- **HTML/Markdown**: 当前 HTML 走 ReverseMarkdown → Markdig → 原生控件树，Markdown 直接 Markdig AST → 控件树。**计划恢复 WebView 双轨方案**：`Avalonia.Controls.WebView`（各平台原生引擎，Win→WebView2, Mac→WKWebView, Linux→WebKit GTK），不可用时降级到 ReverseMarkdown（详见 `.sisyphus/plans/html-preview-webview-fallback.md`）
- **PDF**: `UglyToad.PdfPig` + `SkiaSharp` 逐页位图渲染 + 翻页导航（PdfPig 0.1.15 + PdfPig.Rendering.Skia 0.1.15.4）
- **ICO 画廊**: 自实现 `IcoParser` 提取全部多帧，`WrapPanel` 画廊布局，FlattenAlpha 切换，透明背景棋盘格
- **SVG**: `Svg.Skia` 直接栅格化 → `WriteableBitmap`（无需 WebView2）
- **字体预览**: `HarfBuzzSharp` shaping + `SkiaSharp` 位图渲染 + 自动折行 + 连字检测（`CheckFontSupportsLigature` 自动检测，`IsLigatureEnabled` 可开关）
- **GIF**: 自实现 `GifDecoder` + `DispatcherTimer` 逐帧动画（无需 `WpfAnimatedGif`）
- **两阶段加载**: `ShowPreviewAsync` 拆分 Phase 1（同步显示加载状态+弹跳点动画+信息栏）→ Phase 2（异步提取后显示内容），`_previewLoadVersion` 版本号守卫防竞态
- **透明背景切换**: 图片/GIF/ICO 预览的 `DrawingBrush` 棋盘格（8×8），`IsTransparencyBgShown` 绑定 🏁 按钮
- **信息面板**: 托管在 PreviewPanel 右侧/下方，`ApplyInfoPanelOrientation()` 切换
- **DataGrid**: CSV/SQLite 预览用 `Avalonia.Controls.DataGrid`（手动列创建以绕过 `AutoGenerateColumns` 对 `DataView` 的 bug）
- **结果预览面板**: `ResultPreviewService` 构建文件树 → `ResultTreeView` 控件（Compact/Full 模式、冲突高亮、过滤灰显、深度/文件数截断、摘要栏）
- **魔数检测**: `PreviewService.ClassifyPreviewByMagicAsync` 通过文件魔数优先判定格式，与扩展名冲突时 FormatMetadata 显示警告提示

### 设置系统

`AppSettings` singleton 存在于两个 UI 项目中，格式兼容但各自独立序列化：

- **WPF**: `MantisZip.UI/AppSettings.cs` → `%LOCALAPPDATA%\MantisZip\settings.json`
- **Avalonia**: `MantisZip.UI.Avalonia/Models/AppSettings.cs` → 相同路径
- 两边的 `AppSettings` 字段定义保持同步

设置包含以下分类：
- **压缩**: DefaultFormat (zip/7z/tar.gz), DefaultLevel (1–9), CloseAfterCompress, KeepOriginalExtension, ZipEncoding, ZipCompressionMethod, ZipEncryptionMethod, SevenZipCompressionMethod, SevenZipSolid, SevenZipSolidBlockSize, SevenZipDictionarySize, SevenZipNumFastBytes, SevenZipMatchFinder, SevenZipEncryptHeaders
- **分卷**: SplitSizeTag (0=不分卷/1MB/10MB/…), CustomSplitSizeMB
- **解压**: ExtractDestination (ask/same-dir/desktop), FileConflictAction (ask/overwrite/rename/skip), OpenFolderAfterExtract
- **解压扩展**: EnableDragExtract, ExtractPreserveFullPath
- **上下文菜单**: EnableCompressMenu, EnableOpenMenu, EnableCascadingMenu, ShowMenuIcons, EnableSmartExtractMenu, EnableExtractHereMenu, EnableExtractToNamedMenu, EnableExtractToMenu, EnableCompressSeparate, EnableCompressCombined, EnableDynamicMenu
- **预览**: EnableImagePreview, EnableTextPreview, MaxTextPreviewBytes, ShowPreviewPanel, TextPreviewFontSize, TextPreviewFontFamily, TextEncodingPreference, MaxTablePreviewRows, MaxTablePreviewCols, MaxPreviewFileSize, FontPreviewFontSize, FontPreviewSampleText, FontPreviewEnableLigature, PreviewPosition, InfoPanelOrientation, UseColorEmoji, EnableFormatDetection, PreviewHeadSize
- **密码管理**: ShowPasswordMatchNotification, PasswordRevealByDefault
- **外观（Avalonia 新增）**: Theme (Light/Dark), MaxRecentFiles, AppFontFamily, CompactnessMode (Compact/Normal/Loose), Language, ShowProgressBars, SeparateDirBaseline
- **文件关联（Avalonia 新增）**: AssocZip/7z/Rar/Tar/TarGz/Gz/Iso, CustomAssocExtensions
- **收藏夹（Avalonia 新增）**: FavoritePaths (List<string>)
- **调试**: EnableDebugLogging, LogPrivacyMode (off/filename/full)
- **高级**: SevenZipPath, PreserveDirectoryRoot, CleanTempOnStartup

### Shell integration（WPF-only，Avalonia 待移植）

`ShellIntegration` (static class) 当前仅在 WPF 项目中。Avalonia 版计划在 `avalonia-shell-com-integration` 阶段移植。

安装 Windows Explorer 上下文菜单条目 via `HKCU\Software\Classes` — no admin required.

Since v0.3.7, `Install()` tries COM component registration first (`MantisZip.ShellExt.comhost.dll` via `InstallCom()`), falling back to static registry verbs if the COM host file is missing. `Uninstall()` clears both COM CLSID + shellex and all static verbs. `IsInstalled` checks the COM CLSID first.

#### COM context menu (ShellExt)

`ContextMenuHandler.cs` implements `IShellExtInit` + `IContextMenu` as a COM component hosted by `Explorer.exe`. Two groups:

- **Extract group** (inside "打开/解压" submenu, archive files only): 打开压缩包, 原地解压包, 智能原地解压, 解压到 {name}, 解压到……
- **Compress group** (inside "压缩" submenu): 压缩到 {name}.zip, 压缩到 {parentDir}.zip, 压缩……

Multi-file selection changes text dynamically: "打开压缩包 等 {N} 个文件", "原地解压{N}个压缩包", "智能原地解压{N}个压缩包".

#### Icon system

Three `.ico` files (`Open.ico`, `Extract.ico`, `Compress.ico`) from `src\MantisZip.UI\Resources\MenuIcons\` are embedded as managed resources in `MantisZip.ShellExt.dll`. Loaded at runtime via `GetIconForCommand()`:

1. Read .ico header → find 16×16 entry → extract raw image data
2. `CreateIconFromResourceEx` → get HICON
3. `ConvertIconToBitmap`: `CreateDIBSection` (32-bit DIB, top-down, alpha channel) → `DrawIconEx` → HBITMAP
4. HBITMAPs cached per command type (`_cachedIconOpen`, `_cachedIconExtract`, `_cachedIconCompress`)
5. `CleanupIconCache` called at the **start** of each `QueryContextMenu` (not the end, because Explorer draws asynchronously after `QueryContextMenu` returns)

Uses pure Win32 API — no `System.Drawing` dependency (COM host can't use it).

#### Menu text localization

ShellExt reads localized menu text from registry (`HKCU\Software\MantisZip\ContextMenu\Text*`), written by `ShellIntegration.WriteMenuTextToRegistry()` during `InstallCom()`. The UI project's `L.T()` translates 8 `ShellExt_*` keys (zh + en in `strings.*.json`). Fallback to hardcoded Chinese defaults if registry values are absent.

Two modes controlled by `AppSettings.EnableCascadingMenu`:

- **Cascade mode** (default: off): Single "MantisZip" submenu with separators between 浏览/压缩/解压 groups, numbered verbs via `ExtendedSubCommandsKey`
- **Verb mode**: Individual top-level verbs per target (`*`, `Directory`, `Directory\Background`), with top/bottom separators to isolate from other apps' menus

Menu items with individual toggles:

| # | Menu Item | Toggle | CLI Trigger |
|---|---|---|---|
| 1 | 打开压缩包 — Open archive | EnableOpenMenu | `--open` |
| 2 | 压缩菜单 — Compress dialog | EnableCompressMenu | `--compress` |
| 3 | 压缩到独立的（文件名）— Per-item archives | EnableCompressSeparate | `--compress-separate` |
| 4 | 压缩到（父目录名）— Combined archive | EnableCompressCombined | `--compress-combined` |
| 5 | 解压到此处 — Extract here | EnableExtractHereMenu | `--extract-here` |
| 6 | 智能解压到此处 — Smart extract | EnableSmartExtractMenu | `--extract-smart` |
| 7 | 解压到（压缩包名）— Extract to named folder | EnableExtractToNamedMenu | `--extract-to-name` |
| 8 | 解压到…… — Extract to… | EnableExtractToMenu | `--extract` |

Open and Extract verbs use `AppliesTo` filter (archive extensions only). Icons via `shell32.dll,3` when `ShowMenuIcons` is enabled (static cascade mode only; COM mode uses embedded .ico resources).

### CLI entry points

两套 UI 项目各自实现 CLI 入口，行为一致：

| Argument | Behavior |
|---|---|
| `--install-shell` | Install context menu, then exit |
| `--uninstall-shell` | Uninstall context menu, then exit |
| `--compress <paths...>` | Show compress dialog; multi-instance IPC merges paths from multiple Windows shell invocations |
| `--compress-quick <paths...>` | Direct compress with AppSettings defaults + ProgressWindow, then exit |
| `--compress-separate <paths...>` | Sequential per-item compress to each item's parent directory + ProgressWindow + IPC merge |
| `--compress-combined <paths...>` | Combined single archive from all items with common parent name + IPC merge; prompts if cross-drive |
| `--extract-here <path>` | Direct extract to source directory with AppSettings defaults + ProgressWindow, then exit |
| `--extract-smart <path>` | Smart extract (auto-detect top-level folder) + ProgressWindow, then exit |
| `--extract-to-name <path>` | Extract to named folder (archive name without extension) + ProgressWindow, then exit |
| `--extract <path>` | Direct extract with AppSettings defaults + ProgressWindow, then exit |
| `--open <path>` | Launch MainWindow and load archive for browsing |
| _(no args)_ | Normal MainWindow launch |

- **Avalonia**: `App.axaml.cs` `OnFrameworkInitializationCompleted` 中处理所有 CLI 路由
- **WPF**: `App.OnStartup` + `AppPartials/App.Cli.cs` 等 partial 文件

### System icon helper

`SystemIconHelper` (WPF) / `IconService` + `IconProvider` (Avalonia) uses `SHGetFileInfo` (Windows Shell API) to retrieve 16x16 file type icons by extension. Supports virtual/nonexistent files via `SHGFI_USEFILEATTRIBUTES`. Results cached in `ConcurrentDictionary`. Folder icon support included. Used in file list to show native Windows icons for archive entries.

## 迁移关键差异对照

| 维度 | WPF (MantisZip.UI) | Avalonia (MantisZip.UI.Avalonia) |
|------|-------------------|---------------------------------|
| UI 模式 | Code-behind | MVVM (ObservableObject) |
| 命名空间 | `System.Windows.*` | `Avalonia.*` |
| XAML 扩展名 | `.xaml` | `.axaml` |
| 数据绑定 | `{Binding}` | `{Binding}` + compiled bindings (`x:DataType`) |
| 图片 | `BitmapImage` | `Avalonia.Media.Imaging.Bitmap` |
| SVG | WebView2 | `Svg.Skia` → WriteableBitmap |
| GIF | `WpfAnimatedGif` | 自实现 `GifDecoder` + `DispatcherTimer` |
| 字体预览 | WPF GlyphTypeface + RenderTargetBitmap | HarfBuzzSharp + SkiaSharp 位图渲染 |
| HTML/Markdown | WebView2 (Microsoft.Web.WebView2) | 双轨：`Avalonia.Controls.WebView`（各平台原生，Win/Mac/Linux 各不同后端），不可用时降级到 ReverseMarkdown → Markdig → 控件树 |
| 对话框 | `Ookii.Dialogs.Wpf` | 原生 Avalonia + system dialogs |
| DataGrid | `System.Windows.Controls.DataGrid` | `Avalonia.Controls.DataGrid` |
| 主题资源 | `SolidColorBrush` 在 `Themes/Light.xaml` / `Dark.xaml` | 类似结构，但资源键名略有差异 |
| 目标框架 | `net9.0-windows` | `net9.0` (跨平台就绪) |

## 关键注意事项

### 中文 filename encoding (ZIP)

```csharp
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
```

This is set once in `App.InitializeApp()` (WPF) / `App.OnFrameworkInitializationCompleted` (Avalonia). ZIP encoding is handled per-instance via `StringCodec` (no global `ZipStrings.CodePage`). SharpCompress's `ZipArchive` uses `ReaderOptions.ArchiveEncoding` for per-instance encoding settings, respecting the system locale.

### 7z compression uses SharpSevenZip (7z.dll) — no external 7z.exe required

### 7z encrypted preview SUPPORTED

**实际验证通过：** SharpSevenZip 的 `ExtractFile(index, stream)` 在传入正确密码后，对所有 7z 配置均支持单项提取预览：
- 所有压缩方法（LZMA2 / LZMA / PPMd / BZip2 / Deflate）
- 固实压缩（Solid）与非固实
- AES-256 加密
- 加密文件名（需先传入密码才能列出条目）

`ArchiveEntryExtractor.ExtractSevenZipEntry` 接受 `password` 参数并传递给 `SharpSevenZipExtractor`，与 ZIP 的处理方式一致。代码中不存在 `NotSupportedException`。

⚠ **注意：** "加密文件名"（`EncryptHeaders = true`）的 7z 压缩包，在输入密码前无法读取文件列表（`ArchiveFileData` 会抛出 `SharpSevenZipArchiveException`），UI 需要先弹出密码输入框再尝试列出条目。

### `_currentFormat` classification (previously broken, now fixed)

`_currentFormat` is now derived from the file extension via `GetFormatByExtension()` in `LoadArchiveAsync`, instead of the old buggy `engine.CanHandle()` check that always classified non-ZIP formats as `SevenZip`.

### TarGzEngine metadata (previously broken, now fixed)

- ~~`ListEntriesAsync` sets `LastModified = DateTime.Now` (actual timestamp lost)~~ → now uses `entry.ModTime`
- ~~`CompressAsync` ignores `ArchiveOptions.CompressionLevel` (uses fixed gzip level 5)~~ → now uses `options.CompressionLevel`

### Password manager — MaxEntries (1000) and auto-try limit (100)

`PasswordManager` has a built-in `MaxEntries = 1000` cap to prevent brute-force abuse. `EntryCount` (public property) reflects current count. `AddPassword` throws `InvalidOperationException` when full. `FindMatchingPasswords(maxResults)` accepts an optional limit. `TryMatchPassword` in `App.Password.cs` uses `maxResults: 100` and exposes `out bool limitReached` — callers show a dialog when the cap is hit.

### Silent catch blocks — all converted to TraceLog

Several `catch { }` blocks existed across the codebase (logging, explorer launch, settings, TarGzEngine). All have been converted to `App.TraceLog()` or `CoreLog.Trace()` so the error path is never lost. Known remaining empty `catch { }` are defensive patterns (registry cleanup, progress window cleanup, log flush best-effort) where logging on failure is meaningless.

### `OpenZipFile` — exception-safe disposal

`ZipEngine.OpenZipFile` now wraps the encoding-detection enumeration (`ZipEntry.Any(...)`) in a try-catch. If the first `ZipFile`'s enumeration throws (corrupted archive), the file handle is disposed before rethrowing.

### `--compress` / `--compress-separate` / `--compress-combined` IPC multi-instance

All three `--compress-*` modes use a `Mutex` + `NamedPipeServerStream` pattern. Windows launches one process per selected file; the first process acts as collector, subsequent instances send their paths via named pipe then exit. 800ms collection window. Only the first instance shows the compress dialog or ProgressWindow.

- `--compress`: Mutex `MantisZipCompressMutex`, pipe `MantisZipCompressPipe`
- `--compress-separate`: Mutex `MantisZipCompressSeparateMutex`, pipe `MantisZipCompressSeparatePipe`
- `--compress-combined`: Mutex `MantisZipCompressCombinedMutex`, pipe `MantisZipCompressCombinedPipe`

### "Subfolder display" toggle semantics

The `ShowSubFoldersCheck` checkbox controls whether `FilterFiles` includes nested items. When **checked**, subdirectory contents are also shown in the current view (a flat combined list). Implementation: `FilterFiles` is called, which filters `_allItems` — the checkbox doesn't rebuild the list, it re-filters with the same logic.

### Filter / selection guard

`_isProgrammaticFilter` bool prevents `FilterFiles` (programmatic) from triggering `SelectionChanged` preview. The `SelectionChanged` handler infers the "last clicked item" from `e.AddedItems`/`e.RemovedItems` rather than `SelectedItem`, to support multi-select (Extended mode).

### No CI, no full test suite, no linters

No CI workflows, no pre-commit hooks, no linter/analyzer config. `test_encoding/` is a one-off CLI script for debugging ZIP encoding. `tests/` has a small test project (SmartExtractTests) but no comprehensive suite.

### Smart Extract

`ArchiveStructureAnalyzer` (Core/Utils) analyzes archive structure to determine whether smart extraction should extract directly to the current directory or to a subfolder:

- Scans the top-level entries: if they share a common root folder ≥60% of entries, extract with subfolder; otherwise extract directly
- Used by `ArchiveEngineFactory.SmartExtractEntriesAsync` which calls the analyzer, then delegates to `ExtractAsync` with the computed target path

Triggered via `--extract-smart` CLI or smart extract context menu item.

## Drag-drop (drag-out to Explorer)

Implements the **7-Zip eager-extraction model**: extract files to temp before `DoDragDrop`, show `ProgressWindow` during extraction + drag.

### WPF 版实现

1. `FileListGrid_PreviewMouseMove` detects drag start (threshold: `MinimumHorizontalDragDistance`)
2. Creates temp dir at `%TEMP%\MantisZip\DragDrop\{GUID}\`
3. Opens `ProgressWindow` and extracts files (all engines supported: ZIP/7z via `ArchiveEntryExtractor`, Tar/Gz via `TarInputStream`)
4. Creates standard `DataObject(FileDrop, paths)` — no custom `IDataObject`
5. Sets `_isOwnDrag = true`, starts `DoDragDrop`, keeps ProgressWindow with "正在拖拽 — 放到目标位置以复制文件"
6. After drop: closes ProgressWindow, cleans up temp dir, resets `_isOwnDrag = false`

### Own-window drop protection

`_isOwnDrag` flag prevents `Window_Drop` from reacting to files dragged out of and back into the app window (the temp paths are meaningless for add-to-archive).

### Subdirectory preservation

Uses `ArchiveItem.FullPath` for the output temp path so files from subdirectories retain their relative structure. `ExtractEntryForDragAsync` creates intermediate directories as needed.

### Cancellation

`ProgressWindow` provides cancel via `CancellationToken`. If cancelled before extraction finishes, `DoDragDrop` is skipped entirely.

### Avalonia 版

拖拽待移植，方案见 [drag-drop-direct-extract.md](.sisyphus/plans/drag-drop-direct-extract.md)（纯 Win32 独立覆层）。

### Custom `IDataObject` attempt (archived)

**Tried**: `System.Windows.IDataObject` (`DragDropDataObject` nested class) for delayed rendering — extraction in `GetData()` at drop time so ProgressWindow would show only after mouse release. **Result**: crashes Explorer.

**Root cause**: WPF OLE bridge (`IComDataObject`) has an internal bug when converting `string[]` → `CF_HDROP` for non-`DataStore` `_innerData` implementations. Confirmed by WPF source code (v8.0.1). Not fixable from app side.

**Status**: Abandoned. Code removed. Avalonia 迁移后不再依赖 WPF OLE 桥，此 bug 不复存在。

### Log privacy redaction

`LogRedactor` (Core/Utils) provides centralized path redaction for all log output. Uses `RegexOptions.Compiled` regex with three branches (drive-letter `C:\...`, UNC `\\server\share\...`, and relative `folder\sub\file.ext`), allowing spaces in paths (unlike earlier draft that excluded `\s`).

Four modes controlled by `AppSettings.LogPrivacyMode` (defaults to `"extension"`):
- **off**: No redaction
- **filename**: `D:\Photos\private\wedding.jpg` → `wedding.jpg`
- **extension**: `D:\Photos\private\wedding.jpg` → `[PATH_1]\[FILE_1].jpg` (preserves extension only)
- **full**: Same → `[PATH_1]` (sequential IDs, same path → same ID, capped at 10000 entries)

**Injection**: 
- `CoreLog.RedactOverride` (internal `Func<string, string>?`) set by UI's `App.OnStartup` so CoreLog can redact without referencing AppSettings
- `App.Log()`, `App.LogDebug()`, and `LogStartup()` call `LogRedactor.RedactPaths()` directly (they're in UI project and have AppSettings access)

**Help dialog**: `LogPrivacyHelpDialog` opened from Settings → Debug tab's `[?]` button, matching the PasswordManager help dialog style.

**Key files**: `Core/Utils/LogRedactor.cs` (framework-agnostic), `UI/LogPrivacyHelpDialog.xaml/.cs` (WPF), `UI.Avalonia/Dialogs/LogPrivacyHelpDialog.axaml/.cs` (Avalonia).

## Known issues (already fixed)

### Context menu cascade mode — CommandFlags=8 hides items

Setting `ECF_SEPARATORBEFORE` (`CommandFlags=8`) directly on verbs in an `ExtendedSubCommandsKey` cascade submenu causes those verbs to not appear on some Windows versions. Fixed by using explicit separator verbs instead.

### IPC pipe server only accepted one connection

`StartPipeServer` created one `NamedPipeServerStream` and called `WaitForConnectionAsync` once. With 3+ selected files, only 2 processes could communicate — the 3rd+ process's `Connect()` timed out. Fixed by wrapping in a `while (!ct.IsCancellationRequested)` loop creating a new pipe per client.

### `CompressConflictDialog` shown twice on auto-rename

When clicking "自动重命名" in the file conflict dialog, the `Rename` case re-created a new `CompressConflictDialog`. Fixed by capturing `CustomName` from the first dialog and using it directly, skipping the second popup.

## Version bump checklist

When releasing a new version, update the version string in ALL of these locations:

| # | File | Line | Content |
|---|------|------|---------|
| 1 | `src/MantisZip.UI/AppConstants.cs` | `public const string Version = "x.y.z"` | WPF 版 |
| 2 | `src/MantisZip.UI.Avalonia/AppConstants.cs` | `public const string Version = "x.y.z"` | Avalonia 版 |
| 3 | `src/MantisZip.UI/MantisZip.UI.csproj` | `<Version>x.y.z</Version>` | WPF 版 assembly version |
| 4 | `src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` | `<Version>x.y.z</Version>` | Avalonia 版 assembly version |
| 5 | `docs/PLAN.md` | `**当前版本**: x.y.z` | Plan document header |
| 6 | `docs/PROGRESS.md` | `**当前版本**: x.y.z` | 顶部版本号（三轨制：Avalonia 以日期为标识、WPF 以版本号为标识、共享层以版本号为标识） |

WPF 废弃后，#1 和 #3 将移除。

**Note:** `installer.iss` no longer requires manual version bumps. The release workflow (`release.yml`) passes the version from the git tag via `/dMyAppVersion=${{ env.VERSION }}` to ISCC at compile time. The `#define MyAppVersion` in `installer.iss` is wrapped in `#ifndef` and serves only as a fallback default for local builds — update it occasionally but it is no longer a release-blocking item.

## Build output

```powershell
# WPF（遗留）
src/MantisZip.UI/bin/Debug/net9.0-windows/MantisZip.UI.exe

# Avalonia（主力）
src/MantisZip.UI.Avalonia/bin/Debug/net9.0/MantisZip.UI.Avalonia.exe
```

Build artifacts (bin/, obj/) are gitignored.

## 每次 session 自动执行规则（必需）

以下规则对所有 session 生效，无需用户每次提醒：

### 规则 0：描述不清时必须追问

如果我的需求描述不清晰、有歧义、或者缺少关键信息，**必须**停下来向我提问澄清，直到完全确定后再执行。绝对不能猜测、假设或用「合理默认值」擅自推进。

如果用户是以疑问的方式描述某个问题，则先找到问题所在和解决方案后，必须先与用户沟通，待用户完全确定问题与解决方案后，再执行。

### 规则 1：Plan 变更同步

每当新增或修改 `.sisyphus/plans/` 内的计划文件时，**必须同步更新** `docs/PLAN.md`：
- 新增计划 → 在 PLAN.md 对应优先级区域（P2/P3/待实现）添加一行 `| 任务 | 说明 |` 引用新计划，保持与已存在行格式一致
- 修改计划 → 更新 PLAN.md 中对应任务的说明、优先级或状态
- 计划完成 → 将条目从 PLAN.md 移至 PROGRESS.md 的 【历史设计方案索引】 章节

### 规则 2：版本号变更
- 只有在用户明确说明变更版本号时才会变更，不要未经用户允许擅自变更版本号。如果你觉得应该变更版本号，需要向用户询问。
- 当变更版本号时，需遵循 Version bump checklist 的部分全部更新。

### 规则 3：提交前更新 PROGRESS.md（三轨制）

在每次执行 `git commit` 之前（也就是在 commit 相关的操作中），**必须先更新** `docs/PROGRESS.md`。

PROGRESS.md 分三个独立线索，根据变更影响范围选择对应线索追加条目：

- **Avalonia 版**（`### MantisZip.UI.Avalonia（主力版）`）— 以日期为标识，格式 `**2026-07-16** — 标题`
  - 多条同一日期时按时间从晚到早排列（同一日期下最新的在最上方）
- **WPF 版**（`### MantisZip.UI（WPF 遗留版）`）— 以版本号为标识，格式 `#### v0.x.x (2026-07-16)`
  - 如果当前版本号已有条目，追加到该条目下；否则创建新版本条目
- **共享层**（`### 共享层（Core / ShellExt / 构建）`）— 以版本号为标识，与 WPF 规则一致

通用规则：
- 条目排序均是 **从新到旧**
- 如果本次变更属于某个已有规划任务，在该任务后标注进度
- 如果变更涉及多个线索（例如 Core 引擎变更同时影响 WPF 和 Avalonia），在对应线索下各加一条

### 规则 4：新 UI 控件必须应用主题样式（跨框架适用）

新增任何 UI 控件（WPF 或 Avalonia），**必须显式设置主题样式键**，禁止使用系统默认颜色：

#### Avalonia（主力—优先遵循）
- `Background` 绑定 `"{DynamicResource ThemeSurfaceBgBrush}"` 或对应语义色
- `Foreground` 绑定 `"{DynamicResource ThemeTextPrimaryBrush}"` 或 `ThemeTextSecondaryBrush`
- `BorderBrush` 绑定 `"{DynamicResource ThemeBorderBrush}"` 或 `ThemeBorderLightBrush`
- 按钮用 `ThemeButtonBgBrush` / `ThemeButtonHoverBrush` / `ThemeButtonPressedBrush`
- **Avalonia 资源键名均以 `Brush` 结尾**（`ThemeWindowBgBrush` 而非 `Theme_WindowBg`）
- 如果新增控件类型在主题文件中尚无对应资源，需在 `ThemeLight.axaml` 和 `ThemeDark.axaml` 中成对添加

#### WPF（遗留维护）
- `Background` 绑定 `"{DynamicResource Theme_WindowBg}"`
- `Foreground` 绑定 `"{DynamicResource Theme_TextPrimary}"`
- `BorderBrush` 绑定 `"{DynamicResource Theme_Border}"`
- 按钮用 `Theme_ButtonBg` / `Theme_ButtonHover` / `Theme_ButtonPressed`
- 新增资源在 `Themes/Light.xaml` 和 `Dark.xaml` 中成对添加

#### 通用约束（两框架均适用）
- 不设置 `Height` 固定值除非有充分理由（已有统一高度可按需复用）
- **迁移期特别注意**：在 Avalonia 中新增控件时，优先使用 Avalonia 的资源键名（以 `Brush` 结尾）；不要混用 WPF 风格的下划线资源键名

示例 — Avalonia：
```xml
<!-- ✅ 正确：Avalonia 主题绑定 -->
<TextBox Name="MyTextBox" Width="200"
         Background="{DynamicResource ThemeSurfaceBgBrush}"
         Foreground="{DynamicResource ThemeTextPrimaryBrush}"
         BorderBrush="{DynamicResource ThemeBorderBrush}"/>
```

### 规则 5：紧凑度模式资源键名约定

紧凑度模式提供三档间距/控件高度，通过 `{DynamicResource}` 引用。**注意 double 与 Thickness 的类型差异**：

| 用途 | 资源键名 | C# 类型 | 适用属性 |
|------|---------|---------|---------|
| 间距（数字） | `SpacingXxs` / `Xs` / `Sm` / `Md` / `Lg` / `Xl` | `double` | `Spacing` |
| 间距（厚度） | `SpacingXxsThk` / `XsThk` / `SmThk` / `MdThk` / `LgThk` / `XlThk` | `Thickness` | `Margin` / `Padding` |
| 控件高度 | `ControlHeightSm` / `ControlHeight` / `ControlHeightMd` / `ControlHeightLg` / `ControlHeightXl` / `ControlHeightXxl` | `double` | `Height` / `MinHeight` / `MaxHeight` |
| 圆角 | `BorderRadius` | `CornerRadius`（由 `ApplyCompactness()` 运行时注入） | `CornerRadius` |
| 对话框内边距 | `DialogPadding` | `Thickness` | `Padding`（对话框级别） |

**核心规则**：
- `Margin`/`Padding` **必须使用** `SpacingXxxThk` 后缀变体（因为 `DynamicResource` 无法隐式将 `double` 转换为 `Thickness`）
- `Spacing` 属性必须使用无后缀的 `SpacingXxx`（`double` 类型）
- 新增控件时，不要硬编码间距/高度/圆角数值，优先使用这些 `{DynamicResource}` 引用
- 所有资源由 `App.axaml.cs` 的 `ApplyCompactness()` 在启动时注入三档数值，运行时切换无需重启

### 规则 6：开关控制面板区域时统一隐藏（方案 A）

当需要用一个 CheckBox/开关来控制一组控件（过滤条件、加密选项等）的可用性时，**统一使用隐藏（IsVisible）而非禁用（IsEnabled）**：

- **开关关闭 → 内容隐藏**：整个内容区域从视觉树中移除（`IsVisible="False"`），不占布局空间
- **开关打开 → 内容显示**：正常显示所有控件
- **例外**：如果保持内容可见有确凿的用户体验理由，需在 PR/代码评审时说明

**实现方式**：将受控内容包裹在一个命名容器（`StackPanel`/`Border`）中，在 switch 事件中切换容器的 `IsVisible`。

**参考实现**：
- `Controls/FileFilterEditor.axaml` — `FilterContentPanel` + `SyncControlStates()`
- `Dialogs/CompressSettingsWindow.axaml`（Password Tab）— `IsVisible="{Binding Encrypt}"`

**不推荐**的方案 B（逐个设置 `IsEnabled = false`）已废弃，新增面板无需再实现。

### 规则 7：列表/树形/表格控件必须使用紧凑度感知的行高

新增任何 `ListBox`、`DataGrid`、`TreeView`、`ItemsControl` 等列表类控件时，**必须设置行高/项最小高度为紧凑度资源键**，禁止使用固定数值：

| 控件类型 | 属性 | 推荐资源键 | 三档值 |
|---------|------|-----------|-------|
| `ListBox` | `ListBox.ItemContainerTheme` → `Setter Property="MinHeight"` | `ControlHeightMd` | 28/32/38 |
| `DataGrid` | `RowHeight` | `ControlHeightMd` | 28/32/38 |
| `TreeView` | `Style Selector="TreeViewItem"` → `Setter Property="MinHeight"` | `ControlHeightSm` | 22/26/30 |
| `ItemsControl` | 项模板最外层容器 `MinHeight` | `ControlHeightSm` | 22/26/30 |

**示例 — ListBox：**
```xml
<ListBox ItemsSource="{Binding ...}">
  <ListBox.ItemContainerTheme>
    <ControlTheme TargetType="ListBoxItem">
      <Setter Property="MinHeight" Value="{DynamicResource ControlHeightMd}" />
    </ControlTheme>
  </ListBox.ItemContainerTheme>
  ...
</ListBox>
```

**示例 — DataGrid：**
```xml
<DataGrid ItemsSource="{Binding ...}"
          RowHeight="{DynamicResource ControlHeightMd}" ... />
```

**示例 — TreeView：**
```xml
<TreeView ItemsSource="{Binding ...}">
  <TreeView.Styles>
    <Style Selector="TreeViewItem">
      <Setter Property="MinHeight" Value="{DynamicResource ControlHeightSm}" />
    </Style>
  </TreeView.Styles>
</TreeView>
```

**例外**：非数据行类列表（如 WrapPanel 标签云、图标画廊等不受紧凑度影响的控件）可豁免。

## 未来工作

### 迁移完成后的清理

1. **废弃 WPF 项目**: 删除 `src/MantisZip.UI/` 目录
2. **清理 WebView 依赖**: `Avalonia.Controls.WebView` 保留为跨平台 WebView 抽象（Win→WebView2，Mac→WKWebView，Linux→WPE WebKit），不需要清理。仅在 WPF 废弃时清理 WPF 项目的 WebView2 引用。
3. **Sln 文件更新**: 从解决方案中移除 WPF 项目
4. **构建脚本更新**: 移除 WPF 构建命令
5. **README 更新**: 更新为 WebView 跨平台说明（Win→WebView2，Mac→WKWebView，Linux→WPE WebKit，非 Windows 平台无需额外安装）

### 待实施计划

见 [docs/PLAN.md](docs/PLAN.md) 和 `.sisyphus/plans/` 下的设计文档。
