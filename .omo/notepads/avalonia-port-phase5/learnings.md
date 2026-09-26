## [2026-06-17] Session Start

### Codebase State (Phase 4 already complete)
- Most UI (MainWindow.axaml) already has menus/buttons for all Phase 5 features
- MainWindowViewModel.cs already has commands for AddFiles, DeleteFiles, TestArchive, SmartExtract, EditComment, etc.
- **4 methods are TODO stubs**: AddFiles (line 912), DeleteFiles (line 923), SmartExtract (uses plain ExtractAsync instead of smart logic), EditComment (shows dialog but doesn't save)
- Core `IArchiveEngine` already has `AddToArchiveAsync()`, `DeleteEntriesAsync()`, `TestArchiveAsync()`
- `ArchiveStructureAnalyzer` (Core) has `HasSingleRootDirectory()`
- `RecentFilesManager` fully implemented
- `CommentDialog` exists, needs backend integration
- i18n keys mostly done, missing a few status messages

### Key files to modify:
- `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`
- `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml`
- `src/MantisZip.UI.Avalonia/Localization/strings.en.json`
- `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json`

### Workflow:
- Task 1-4: ViewModel implementations (parallel)
- Task 5-7: UI + i18n (parallel, after 1-4)
- Task 8: Build & verify

## [2026-06-17] Implementation — 4 TODO/stub commands

### SmartExtract
- Uses `ArchiveStructureAnalyzer.HasSingleRootDirectory(_allRawItems)` to determine extraction target
- Single root → extract to parent dir (strips root folder)
- Dispersed → extract to named subfolder

### EditComment
- Uses `ZipCommentHelper.ReadComment()` / `WriteComment()` from Core
- **Why ZipCommentHelper instead of SharpCompress ZipArchive**: SharpCompress 0.48.1 compiled for net10.0 does NOT export `ZipArchive.Open(Stream)` or `ZipArchive.Comment` as public API. The `ZipCommentHelper` binary-patches the EOCD record directly — simpler, no recompression, and avoids all SharpCompress versioning issues.
- Guards: only ZIP format, reads existing comment for dialog pre-fill

### AddFiles
- Uses `GetOpenFilePaths()` dialog callback → `engine.AddToArchiveAsync()` with `RunWithProgress`
- Creates `ArchiveOptions` with session password
- Calls `RefreshArchive()` on success

### DeleteFiles
- Uses `SelectedEntry.FullPath ?? SelectedEntry.Name` → `engine.DeleteEntriesAsync()` with `RunWithProgress`
- Calls `RefreshArchive()` on success

### Key gotcha
- `ZipArchive.Open(Stream)` and `ZipArchive.Comment` are NOT available in SharpCompress 0.48.1 net10.0 build. Always prefer `ZipCommentHelper` from Core for ZIP comment operations.
