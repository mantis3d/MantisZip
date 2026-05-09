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

- Trigger: `FileListGrid_SelectionChanged` → `ShowPreviewAsync(item)`
- Extract: `ArchiveEntryExtractor.ExtractEntryAsync(...)` → temp file under `%TEMP%\MantisZip\{GUID}\`
- Display: `ShowImagePreviewAsync` (BitmapImage with `DecodePixelWidth=1920` on background thread), `ShowTextPreview` (UTF-8/GBK fallback), `ShowUnsupportedPreview`
- Image side panel: `PreviewInfoPanel` shows name, size, compression ratio, date — only for images
- Cleanup: `ClearPreviewTemp()` before each new preview; `App.OnExit` deletes `%TEMP%\MantisZip`
- Respects `AppSettings.EnableImagePreview`, `EnableTextPreview`, `MaxTextPreviewBytes`

### Window persistence

Window size, tree column width, and preview row height saved to `%LOCALAPPDATA%\MantisZip\window.json` as JSON. Restored on startup. Preview row height supports both `Pixel` and `Star` `GridLength` types.

### Settings system

`AppSettings` singleton stores all user preferences in `%LOCALAPPDATA%\MantisZip\settings.json` as JSON. Sections:

- **压缩**: DefaultFormat (zip/7z/tar.gz), DefaultLevel (1–9), CloseAfterCompress, KeepOriginalExtension
- **解压**: ExtractDestination (ask/same-dir/desktop), FileConflictAction (ask/overwrite/rename/skip), OpenFolderAfterExtract
- **上下文菜单**: EnableCompressMenu, EnableExtractMenu, EnableOpenMenu, EnableQuickCompress, EnableCascadingMenu, ShowMenuIcons
- **预览**: EnableImagePreview, EnableTextPreview, MaxTextPreviewBytes
- **高级**: SevenZipPath

`SettingsWindow` (tabbed UI) provides GUI editing; `CompressSettingsWindow` loads defaults from `AppSettings`.

### Shell integration

`ShellIntegration` (static class) installs Windows Explorer context menu entries via `HKCU\Software\Classes` — no admin required.

Two modes controlled by `AppSettings.EnableCascadingMenu`:

- **Cascade mode** (default: off): Single "MantisZip" submenu, numbered verbs (`01_compress`, `02_quick`, `03_open`, `04_extract`) via `ExtendedSubCommandsKey`
- **Verb mode**: Individual top-level verbs per target (`*`, `Directory`, `Directory\Background`)

Per-verb toggles: EnableCompressMenu, EnableQuickCompress, EnableOpenMenu, EnableExtractMenu. Open and Extract verbs use `AppliesTo` filter (archive extensions only). Icons via `shell32.dll,3`.

CLI triggers: `--install-shell`, `--uninstall-shell`.

### CLI entry points

All handled in `App.OnStartup` before normal UI startup:

| Argument | Behavior |
|---|---|
| `--install-shell` | Install context menu, then exit |
| `--uninstall-shell` | Uninstall context menu, then exit |
| `--compress <paths...>` | Show compress dialog; multi-instance IPC merges paths from multiple Windows shell invocations |
| `--compress-quick <paths...>` | Direct compress with AppSettings defaults + ProgressWindow, then exit |
| `--extract <path>` | Direct extract with AppSettings defaults + ProgressWindow, then exit |
| `--open <path>` | Launch MainWindow and load archive for browsing |
| _(no args)_ | Normal MainWindow launch |

`--extract` bypasses MainWindow entirely (avoids `Loaded` event timing issues). `--open` uses MainWindow with archive loaded.

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

### `_currentFormat` bug (critical)

In `MainWindow.LoadArchiveAsync`:
```csharp
_currentFormat = engine.CanHandle(ArchiveFormat.Zip) ? ArchiveFormat.Zip : ArchiveFormat.SevenZip;
```
This always classifies non-ZIP formats as `SevenZip`. Tar/GZip/Rar archives get wrong format, causing preview extraction (`ArchiveEntryExtractor`) to fail or use wrong code paths. **Fix**: derive `_currentFormat` from the file extension instead.

### TarGzEngine metadata issues

- `ListEntriesAsync` sets `LastModified = DateTime.Now` (actual timestamp lost)
- `CompressAsync` ignores `ArchiveOptions.CompressionLevel` (uses fixed gzip level 5)

### "Subfolder display" toggle semantics

The `ShowSubFoldersCheck` checkbox controls whether `FilterFiles` includes nested items. When **checked**, subdirectory contents are also shown in the current view (a flat combined list). Implementation: `FilterFiles` is called, which filters `_allItems` — the checkbox doesn't rebuild the list, it re-filters with the same logic.

### Filter / selection guard

`_isProgrammaticFilter` bool prevents `FilterFiles` (programmatic) from triggering `SelectionChanged` preview. The `SelectionChanged` handler infers the "last clicked item" from `e.AddedItems`/`e.RemovedItems` rather than `SelectedItem`, to support multi-select (Extended mode).

### No CI, no tests, no linters

No CI workflows, no pre-commit hooks, no linter/analyzer config. `test_encoding/` is a one-off CLI script for debugging ZIP encoding, not a test framework.

### Password manager stores plaintext

`PasswordManager` (singleton) saves passwords as **plaintext JSON** at `%APPDATA%\MantisZip\passwords.json`. No encryption, no DPAPI.

### No RAR engine registered

`ArchiveEngineFactory.GetEngineByExtension` maps `.rar` to `ArchiveFormat.Rar`, but no engine registers itself for `Rar`. Opening a `.rar` archive returns `null` from the factory — the UI shows "不支持的压缩格式".

### --compress IPC multi-instance

The `--compress` handler uses a `Mutex` + `NamedPipeServerStream` pattern. Windows launches one process per selected file; the first process acts as collector, subsequent instances send their paths via named pipe then exit. 800ms collection window. Only the first instance shows the compress dialog.

## Build output

```
src/MantisZip.UI/bin/Debug/net9.0-windows/MantisZip.UI.exe
```

Build artifacts (bin/, obj/) are gitignored.
