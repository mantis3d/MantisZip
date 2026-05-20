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
- Extract: `ArchiveEntryExtractor.ExtractEntryAsync(...)` → temp file under `%TEMP%\MantisZip\{GUID}\`
- Display: 
  - `ShowImagePreviewAsync`: checks actual image dimensions first via `BitmapDecoder.Create(DelayCreation)` — only sets `DecodePixelWidth=1920` for images wider than 1920px (no upscaling); constrains `PreviewImage.MaxWidth`/`MaxHeight` to pixel dimensions to prevent `Stretch="Uniform"` from enlarging small images
  - `ShowTextPreview` (UTF-8/GBK fallback), `ShowUnsupportedPreview`, `ShowDirectoryPreview`
- Toolbar toggle button (`PreviewToggleBtn`) controls `_previewPanelEnabled` / `AppSettings.ShowPreviewPanel`; toggling calls `HidePreview()` (resets grid layout) or `ShowPreviewPanel()` (restores layout + re-previews selected item)
- `ClearPreviewContent()` clears image/text/icon/webview sources without resetting grid layout (used during file-to-file switching to avoid flicker); `HidePreview()` does full cleanup including grid rows/columns reset
- Image side panel: `PreviewInfoPanel` shows name, size, compression ratio, date — only for images
- Cleanup: `ClearPreviewTemp()` before each new preview; `App.OnExit` deletes `%TEMP%\MantisZip`
- Respects `AppSettings.EnableImagePreview`, `EnableTextPreview`, `MaxTextPreviewBytes`, `ShowPreviewPanel`

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

### Chinese filename encoding (ZIP)

```csharp
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
ZipStrings.CodePage = 936; // GBK — must be set before reading ZIP
```

These are set **once globally** in `App.InitializeApp()` (called at the top of `OnStartup`), so every entry point gets it automatically. As safety redundancy, `ZipStrings.CodePage = 936` is also set in each `ZipEngine` method (`ListEntriesAsync`, `ExtractAsync`, `TestArchiveAsync`) before creating `ZipFile`. This is a **process-wide side-effect** — it affects all ZIP operations in the entire process.

**Rule**: If adding new code paths that bypass `App.OnStartup`, call `App.InitializeApp()` first, or replicate both lines before any `ZipFile` usage.

### 7z compression requires external 7z.exe

`SevenZipEngine` hardcodes path: `C:\Program Files\7-Zip\7z.exe`. If absent, 7z compression fails silently.

### 7z encrypted preview not supported

`ArchiveEntryExtractor.ExtractSevenZipEntry` throws `NotSupportedException` when password is set. Encrypted 7z entries always show "无法预览此文件" (unsupported preview).

### `_currentFormat` classification (previously broken, now fixed)

`_currentFormat` is now derived from the file extension via `GetFormatByExtension()` in `LoadArchiveAsync`, instead of the old buggy `engine.CanHandle()` check that always classified non-ZIP formats as `SevenZip`.

### TarGzEngine metadata (previously broken, now fixed)

- ~~`ListEntriesAsync` sets `LastModified = DateTime.Now` (actual timestamp lost)~~ → now uses `entry.ModTime`
- ~~`CompressAsync` ignores `ArchiveOptions.CompressionLevel` (uses fixed gzip level 5)~~ → now uses `options.CompressionLevel`

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
