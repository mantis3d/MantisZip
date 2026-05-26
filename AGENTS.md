# MantisZip — Agent Guide

## Project overview

WPF desktop compression/decompression app (.NET 9, Windows only). Two projects: `MantisZip.Core` (class library) + `MantisZip.UI` (WinExe).

## Quick start

```powershell
# Build everything
dotnet build src\MantisZip.UI\MantisZip.UI.csproj

# Run (requires Windows)
dotnet run --project src\MantisZip.UI\MantisZip.UI.csproj

# No proper test project exists — run no tests.
# test_encoding/ is a throwaway CLI tool for debugging ZIP encoding, not a test suite.
```

## Architecture

### Dependency flow

```
MantisZip.UI (WPF) ──reference──▶ MantisZip.Core (net9.0)
                                        │
                        ┌───────────────┼───────────────┐
                   ZipEngine    SevenZipEngine    TarGzEngine
                   (SharpZipLib) (SevenZipExtractor) (SharpZipLib)
```

### Engine pattern (strategy + factory)

- `IArchiveEngine` interface: `ListEntriesAsync`, `ExtractAsync`, `CompressAsync`, `TestArchiveAsync`
- `ArchiveEngineFactory` registers engines in static constructor, dispatches by file extension
- `SevenZipEngine.CompressAsync` shells out to `C:\Program Files\7-Zip\7z.exe` — **not** a managed library
- `ArchiveEntryExtractor` (Core/Utils) handles single-entry extraction for preview; only supports Zip and 7z

### Progress reporting

- `ArchiveProgress` (Core/Abstractions/ArchiveEngine.cs): `PercentComplete` (overall, 0–100), `FilePercentComplete` (nullable double, 0–100 for per-file granularity), `FileName` (current file name), `Message`
- `ZipEngine` reports per-file progress via buffered I/O copy loop with 100ms throttle; reports initial 0% and final 100% for each file
- `SevenZipEngine.ExtractAsync` and `TarGzEngine.ExtractAsync` report progress only at completion (100%)
- `SevenZipEngine.CompressAsync` polls `7z.exe` with `Thread.Sleep(100)`
- `ProgressWindow` shows two progress bars: file-level (top) and overall (bottom); `SetProgress(ArchiveProgress)` overload drives both

### ArchiveItem duality

- **Core**: `MantisZip.Core.Abstractions.ArchiveItem` — engines produce these
- **UI**: `MainWindow.xaml.cs` defines a subclass `ArchiveItem : Core.Abstractions.ArchiveItem` adding `DisplayName`, `SizeDisplay`, `NameDisplay`, `SortOrder` — all engine output is mapped into UI instances in `LoadArchiveAsync`

### UI pattern: code-behind, not MVVM

Despite using `CommunityToolkit.Mvvm`, **all logic lives in `MainWindow.xaml.cs`**. No ViewModel classes exist. The `FolderNode` class at the bottom of that file implements `INotifyPropertyChanged` for TreeView binding only.

### Preview subsystem

- Trigger: `FileListGrid_SelectionChanged` → files via `ShowPreviewAsync(item)`, directories via `ShowDirectoryPreview(item)` (system folder icon + directory info panel)
- **`ExtractPreviewFileAsync(item, fallbackName, ct)`** — shared helper (lines ~139) for temp extraction; creates temp dir, extracts, returns file path. Replaces 14 identical 5-line extraction blocks. Callers just `var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);`
- **`HideAllPreviewControls()`** — collapses all 5 content controls (`PreviewImage`, `PreviewTextBox`, `PreviewFileIcon`, `PreviewUnsupported`, `PreviewWebView2`). Called at the start of every `Show*Preview` method before showing the relevant control. Ensures no orphaned visibility states from previous formats.
- **`SetToolbar(ToolbarButton[] leftButtons, ToolbarButton[] rightButtons)`** — left side is common controls (zoom, font size), right side is format-specific (transparency toggle, ligature toggle, GIF frame nav). Separator auto-inserted between left and right arrays. Callers specify both arrays explicitly.
- **Info panel clearing** is centralized in `ShowPreviewAsync` (line ~165-167) — `PreviewExtraInfoPanel.Children.Clear()` + `.Visibility = Collapsed` before any format's display method runs. Individual format methods no longer need to clean up.
- **`SetFormatSpecificInfo(params (string, string)[] pairs)`** — adds key-value rows to `PreviewExtraInfoPanel`; used by all metadata formats (PE/PDF/Font/Audio/SQLite/ISO/Office/Video) and Image/GIF.
- Display (per format):
  - **Image**: `ShowImagePreviewAsync` — BitmapImage with DecodePixelWidth=1920 downsampling; zoom toolbar + transparency toggle for PNG/ICO/WebP
  - **GIF**: Same `ShowImagePreviewAsync` path with `WpfAnimatedGif` — play/pause/frame-nav toolbar + editable frame input (TextBox + total label)
  - **Text**: `ShowTextPreview` — UTF-8/GBK detection via Ude.NetStandard; font size toolbar
  - **HTML**: `ShowHtmlPreview` — WebView2 rendering (network requests blocked)
  - **Markdown**: `ShowMarkdownPreview` — Markdig → HTML → WebView2 (dark theme support via `prefers-color-scheme`)
  - **PE**: `ShowPePreview` — product name, company, version, architecture, subsystem
  - **PDF**: `ShowPdfPreview` — metadata + WebView2 PDF content rendering (size-gated)
  - **Font**: `ShowFontPreview` — font name/style/glyph count; sample text rendering; ligature toggle (re-sets TextBox.Text to force WPF redraw)
  - **Audio**: `ShowAudioPreview` — WAV/FLAC duration, sample rate, channels, bitrate
   - **SQLite**: `ShowSqlitePreview` — encoding, page size, table count + DataGrid table(s) via `SqliteDataReader` (Microsoft.Data.Sqlite); multi-table via `ShowMultiTablePreview` (TabControl)
  - **ISO**: `ShowIsoPreview` — volume label, format, size
  - **Torrent**: `ShowTorrentPreview` — file tree, InfoHash, Magnet, tracker, creator
  - **Office**: `ShowOfficePreview` — docx/xlsx/pptx title, author, page/slide/sheet count
  - **SVG**: `ShowSvgPreview` — WebView2 rendering
  - **Video**: `ShowVideoPreview` — MP4/MKV/AVI resolution, duration, codec
- **`ShowTablePreview(DataTable, ArchiveItem, title)`** — shared method for tabular data (CSV, SQLite, future formats). Uses `PreviewCsvGrid` (DataGrid) with 100-row × 100-col limit. Params: `DataTable`, `ArchiveItem` for info panel, string title for header.
- **`ITableDataProvider`** — optional interface in `Core/Abstractions/` for pluggable table data readers (SQLite, Office, etc.). See [modular preview providers plan](.sisyphus/plans/preview-modular-providers.md) for future extraction into separate class libraries.
- Metadata-only formats (PE, Office, audio, SQLite, ISO, torrent, video) skip the `MaxPreviewFileSize` check since they only read file headers. PDF is also metadata-only (exempt from the outer size check) but conditionally renders content if `item.Size <= MaxPreviewFileSize`
- Toolbar: `SetToolbar(left, right)` — common controls left, format-specific right, separator between. Image zoom / Text font-size / GIF play-pause-frame / Font ligature toggle / Transparency toggle.
- `ClearPreviewContent()` clears all sources without resetting grid layout; `HidePreview()` does full cleanup
- Image side panel: `PreviewInfoPanel` shows name, size, compression ratio, date — now with format-specific key-value pairs below general info
- Cleanup: `ClearPreviewTemp()` before each new preview; `App.OnExit` deletes `%TEMP%\MantisZip`
- `MetadataOnlyExtensions` set defines formats exempt from size limits

### Window persistence

Window size, tree column width, and preview row height saved to `%LOCALAPPDATA%\MantisZip\window.json` as JSON. Restored on startup. Preview row height supports both `Pixel` and `Star` `GridLength` types.

### Settings system

`AppSettings` singleton stores all user preferences in `%LOCALAPPDATA%\MantisZip\settings.json` as JSON. Sections:

- **压缩**: DefaultFormat (zip/7z/tar.gz), DefaultLevel (1–9), CloseAfterCompress, KeepOriginalExtension
- **解压**: ExtractDestination (ask/same-dir/desktop), FileConflictAction (ask/overwrite/rename/skip), OpenFolderAfterExtract
- **上下文菜单**: EnableCompressMenu, EnableOpenMenu, EnableCascadingMenu, ShowMenuIcons, EnableSmartExtractMenu, EnableExtractHereMenu, EnableExtractToNamedMenu, EnableExtractToMenu, EnableCompressSeparate, EnableCompressCombined
- **预览**: EnableImagePreview, EnableTextPreview, MaxTextPreviewBytes, ShowPreviewPanel, TextPreviewFontSize
- **调试**: EnableDebugLogging, LogPrivacyMode (off/filename/full)
- **密码管理**: ShowPasswordMatchNotification, PasswordRevealByDefault
- **高级**: SevenZipPath

`SettingsWindow` (tabbed UI) provides GUI editing; `CompressSettingsWindow` loads defaults from `AppSettings`.

### Shell integration

`ShellIntegration` (static class) installs Windows Explorer context menu entries via `HKCU\Software\Classes` — no admin required.

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

Open and Extract verbs use `AppliesTo` filter (archive extensions only). Icons via `shell32.dll,3` when `ShowMenuIcons` is enabled.

### CLI entry points

All handled in `App.OnStartup` before normal UI startup:

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

`--extract`, `--extract-here`, `--extract-smart`, and `--extract-to-name` bypass MainWindow entirely (avoid `Loaded` event timing issues). `--open` uses MainWindow with archive loaded.

### System icon helper

`SystemIconHelper` uses `SHGetFileInfo` (Windows Shell API) to retrieve 16x16 file type icons by extension. Supports virtual/nonexistent files via `SHGFI_USEFILEATTRIBUTES`. Results cached in `ConcurrentDictionary`. Folder icon support included. Used in file list to show native Windows icons for archive entries.

## Key gotchas

## Version 0.3.0 — Preview Format Expansion (✅ Completed)

Added metadata-based preview for 12 new format types across Core parsers and UI preview methods. Also includes info panel restructuring, torrent file tree, WOFF font support, PDF metadata, and toolbar restoration.

## Upcoming work

Already implemented in v0.2.13:
- Archive comment editing (MainWindow edit menu)
- CompressSettingsWindow TabControl (General + Comment tabs)
- Comment distribution (AllSame / FirstOnly / PerLine)

Plans tracked under `.sisyphus/plans/`:

| Plan | Status | Dependency |
|------|--------|------------|
| [engine-unification-sharpcompress.md](.sisyphus/plans/engine-unification-sharpcompress.md) | 📋 Planned | None |
| [file-filter-feature.md](.sisyphus/plans/file-filter-feature.md) | 📋 Planned | SharpCompress migration |
| [preview-modular-providers.md](.sisyphus/plans/preview-modular-providers.md) | 📋 Planned | Preview system |

### Planned: Engine unification (SharpZipLib → SharpCompress)

Replace SharpZipLib with SharpCompress to get:
- Unified `IArchive` / `IReader` API across all formats
- Per-instance encoding (no more `ZipStrings.CodePage` global state)
- Native async/await
- Selective extraction via `IArchiveEntry.WriteToFile()` — enables per-entry filtering
- New `IArchiveEngine.ExtractEntriesAsync()` method for filtered extraction

See [plan](.sisyphus/plans/engine-unification-sharpcompress.md) for phased implementation (TarGzEngine → ArchiveEntryExtractor → ZipEngine → SevenZipEngine).

### Planned: File filter feature

Add filtering to compress/extract operations — filter by file type extension, filename pattern, size range, or date range. Supports named presets persisted in settings.

See [plan](.sisyphus/plans/file-filter-feature.md).

## v0.2.13 — Archive comment features

### Archive comment editing (MainWindow)

- **Trigger**: 编辑 → 压缩包注释 (EditMenuArchiveComment)
- **Dialog**: `ArchiveCommentDialog` (new XAML + .cs) opens Modal, fetches existing comment via `ZipFile.ZipFileComment` (read-only property for display)
- **Save**: Uses `ZipFile.BeginUpdate()` + `zipFile.SetComment(comment)` + `zipFile.CommitUpdate()` — this modifies the ZIP EOCD comment **in-place without recompression**
- **Guard**: Only enabled when archive is ZIP format (checked via `_currentFormat == ArchiveFormat.Zip` in `UpdateAddDeleteBtnState`)
- **Flow**: `EditArchiveComment_Click` → `OpenArchiveStream` → `ZipFile` instance → show dialog → if OK: `BeginUpdate/SetComment/CommitUpdate`

### CompressSettingsWindow TabControl

- **Layout**: `TabControl` with 2 tabs — "通用" (General) and "注释" (Comment)
- **General tab**: Existing compress options (format/level/encryption/split volumes), wrapped in `ScrollViewer`
- **Comment tab**: Multi-line TextBox + radio group for distribution strategy
- **Theme**: TabControl/TabItem styled with `Theme_HeaderBg`, `Theme_WindowBg`, `Theme_Accent`, `Theme_ButtonHover` — matches dark/light mode system
- **WPF content virtualization**: TabItem content is lazily created (not loaded until first selection). **All event handlers and state-update helpers must guard against null controls** in Comment tab until it's been displayed once.
- **Output mode linkage**: Distribution radio group is always visible but only enabled when `OutputMode.Separate` is selected. Other modes ignore the distribution setting and pass the raw comment text as-is.

### Comment distribution (separate output only)

- **`ArchiveOptions.Comment`** (string?) — optional comment text to embed in archive
- **`CommentDistribution`** enum in `ArchiveEngine.cs`:
  - `AllSame`: Every archive gets the full comment text
  - `FirstOnly`: Only the first archive gets the comment; others get empty string
  - `PerLine`: Comment is split by newline; line N → archive N; empty lines skipped; excess archives get no comment
- **Implementation in UI layer**: `RunSeparateCompressAsync` (CompressSettingsWindow.xaml.cs) pre-computes `perLineComments` and sets `options.Comment` per iteration based on selected distribution mode

### Key gotchas

- **`ZipFile.ZipFileComment` is read-only**: SharpZipLib exposes the ZIP comment as a read-only property. To write, you must call `SetComment(comment)` **after** `BeginUpdate()`.
- **TabItem controls null until first selection**: WPF virtualizes TabItem content. Don't access `x:Name` controls inside an unselected TabItem without null guards.
- **Comment distribution ignored outside Separate mode**: When `OutputMode` is `SingleArchive` or `QuickCompress`, the distribution radio selection has no effect — the raw comment is passed as-is.
- **Only ZIP supports comments**: Other archive formats (7z, tar.gz) do not have a comment field. The Comment tab shows a "仅 ZIP 格式支持注释" hint.

## Known issues (already fixed)

### Context menu cascade mode — CommandFlags=8 hides items

Setting `ECF_SEPARATORBEFORE` (`CommandFlags=8`) directly on verbs in an `ExtendedSubCommandsKey` cascade submenu causes those verbs to not appear on some Windows versions. Fixed by using explicit separator verbs instead.

### IPC pipe server only accepted one connection

`StartPipeServer` created one `NamedPipeServerStream` and called `WaitForConnectionAsync` once. With 3+ selected files, only 2 processes could communicate — the 3rd+ process's `Connect()` timed out. Fixed by wrapping in a `while (!ct.IsCancellationRequested)` loop creating a new pipe per client.

### `CompressConflictDialog` shown twice on auto-rename

When clicking "自动重命名" in the file conflict dialog, the `Rename` case re-created a new `CompressConflictDialog`. Fixed by capturing `CustomName` from the first dialog and using it directly, skipping the second popup.

## Key gotchas

### Chinese filename encoding (ZIP) — to be removed with SharpCompress

*(This section is historic — SharpCompress migration will eliminate the global encoding hack.)*

```csharp
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
ZipStrings.CodePage = 936; // GBK — must be set before reading ZIP
```

These are set **once globally** in `App.InitializeApp()` (called at the top of `OnStartup`), so every entry point gets it automatically. As safety redundancy, `ZipStrings.CodePage = 936` is also set in each `ZipEngine` method (`ListEntriesAsync`, `ExtractAsync`, `TestArchiveAsync`) before creating `ZipFile`. This is a **process-wide side-effect** — it affects all ZIP operations in the entire process.

**Rule**: If adding new code paths that bypass `App.OnStartup`, call `App.InitializeApp()` first, or replicate both lines before any `ZipFile` usage.

### 7z compression requires external 7z.exe — to be removed with SharpCompress migration Phase 4

### 7z encrypted preview not supported

`ArchiveEntryExtractor.ExtractSevenZipEntry` throws `NotSupportedException` when password is set. Encrypted 7z entries always show "无法预览此文件" (unsupported preview).

### `_currentFormat` classification (previously broken, now fixed)

`_currentFormat` is now derived from the file extension via `GetFormatByExtension()` in `LoadArchiveAsync`, instead of the old buggy `engine.CanHandle()` check that always classified non-ZIP formats as `SevenZip`.

### TarGzEngine metadata (previously broken, now fixed)

- ~~`ListEntriesAsync` sets `LastModified = DateTime.Now` (actual timestamp lost)~~ → now uses `entry.ModTime`
- ~~`CompressAsync` ignores `ArchiveOptions.CompressionLevel` (uses fixed gzip level 5)~~ → now uses `options.CompressionLevel`

### Password manager — MaxEntries (1000) and auto-try limit (100)

`PasswordManager` has a built-in `MaxEntries = 1000` cap to prevent brute-force abuse. `EntryCount` (public property) reflects current count. `AddPassword` throws `InvalidOperationException` when full. `FindMatchingPasswords(maxResults)` accepts an optional limit. `TryMatchPassword` in `App.xaml.cs` uses `maxResults: 100` and exposes `out bool limitReached` — callers show a dialog when the cap is hit.

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

### Architecture

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

### Custom `IDataObject` attempt (archived)

**Tried**: `System.Windows.IDataObject` (`DragDropDataObject` nested class) for delayed rendering — extraction in `GetData()` at drop time so ProgressWindow would show only after mouse release. **Result**: crashes Explorer.

**Root cause**: WPF OLE bridge (`IComDataObject`) has an internal bug when converting `string[]` → `CF_HDROP` for non-`DataStore` `_innerData` implementations. Confirmed by WPF source code (v8.0.1). Not fixable from app side.

**Status**: Abandoned. Code removed. If a delayed-rendering solution is needed in the future, use `VirtualFileDataObject` (Microsoft.VisualStudio.OLE.Interop / Shell32 community wrappers) instead of `System.Windows.IDataObject`.

### Log privacy redaction

`LogRedactor` (Core/Utils) provides centralized path redaction for all log output. Uses `RegexOptions.Compiled` regex with two branches (drive-letter `C:\...` and UNC `\\server\share\...`), allowing spaces in paths (unlike earlier draft that excluded `\s`).

Three modes controlled by `AppSettings.LogPrivacyMode` (defaults to `"full"`):
- **off**: No redaction
- **filename**: `D:\Photos\private\wedding.jpg` → `wedding.jpg`
- **full**: Same → `[PATH_1]` (sequential IDs, same path → same ID, capped at 10000 entries)

**Injection**: 
- `CoreLog.RedactOverride` (internal `Func<string, string>?`) set by UI's `App.OnStartup` so CoreLog can redact without referencing AppSettings
- `App.Log()`, `App.LogDebug()`, and `LogStartup()` call `LogRedactor.RedactPaths()` directly (they're in UI project and have AppSettings access)

**Help dialog**: `LogPrivacyHelpDialog` opened from Settings → Debug tab's `[?]` button, matching the PasswordManager help dialog style.

**Key files**: `Core/Utils/LogRedactor.cs` (new), `UI/LogPrivacyHelpDialog.xaml/.cs` (new).

### Future: COM context menu + VirtualFileDataObject

Both improvements require COM component integration:

**Dynamic shell context menu** — Replace static registry verbs with a COM `IContextMenu` handler:
- Right-click file name is available at menu-build time → dynamic display text (e.g. "添加到 (文件名).zip")
- Full control over menu ordering, icons, submenus
- Registration via `*\shellex\ContextMenuHandlers\{GUID}`
- Effort: medium (COM registration, `IContextMenu` / `IShellExtInit` implementation)

**VirtualFileDataObject** (drag-out delayed rendering):
- Delayed rendering without Explorer crash
- Files appear in Explorer before fully extracted
- Explorer requests data chunk by chunk via `GetData`
- Works around WPF OLE bridge bug by implementing `System.Runtime.InteropServices.ComTypes.IDataObject` directly in COM
- Effort: medium (P/Invoke for `COMStreamWrapper`, `FORMATETC`, `STGMEDIUM`)

Both can be packaged as a single helper library if desired.

## Build output

```
src/MantisZip.UI/bin/Debug/net9.0-windows/MantisZip.UI.exe
```

Build artifacts (bin/, obj/) are gitignored.
