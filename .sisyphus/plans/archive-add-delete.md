# Work Plan: Add/Delete Archive Entries

## Objective

Implement two user-facing features in MantisZip:

1. **添加文件到压缩包** — Add UI entry points (toolbar button + right-click menu) for the existing `AddToArchiveAsync` engine method
2. **从压缩包删除文件** — Full new feature: interface, engine implementations, UI, confirmation dialog, TDD

## Requirements Summary

| Aspect | Add | Delete |
|--------|-----|--------|
| **Engine** | Already exists (`AddToArchiveAsync`) | New: `DeleteEntriesAsync` |
| **UI entry** | Toolbar button + right-click menu + existing drag-drop | Right-click menu + Delete key |
| **Multi-select** | Not applicable (opens file dialog) | ✅ Ctrl/Shift multi-select, single file, whole dir |
| **Format support** | Zip ✅ 7z ✅ TarGz ❌ | Zip ✅ 7z ✅ TarGz ❌ Rar/ISO ❌ |
| **Password** | Reuse `_currentPassword` | Reuse `_currentPassword`, keep encryption on rewrite |
| **Confirmation** | Already in Window_Drop; new: none needed for button | Delete confirmation dialog |
| **Post-action** | Refresh file list | Refresh file list |
| **TDD** | N/A (no new engine code) | ✅ Write tests before implementation |

## Detailed Tasks

### Task 1: IArchiveEngine.DeleteEntriesAsync — Interface + Tests (TDD)

**1a. Define interface method**
```csharp
Task DeleteEntriesAsync(
    string archivePath,
    IReadOnlyList<string> entryNames,
    ArchiveOptions? options = null,
    IProgress<ArchiveProgress>? progress = null,
    CancellationToken cancellationToken = default);
```

- `entryNames`: list of full paths within the archive (e.g. `["dir/file1.txt", "dir/file2.txt"]`)
- For directories: caller pre-expands to individual files; the engine receives only file paths
- `ArchiveOptions`: carries password (for encrypted archives), other options

**1b. Write failing tests (TDD Red)**
- Test file: `tests/MantisZip.Tests/Engines/ZipEngineDeleteTests.cs`
- Test fixture: use temporary ZIP files created in `TempDir` (follow existing test pattern in `ZipEngineTests.cs`)
- Test cases:
  - Delete single file from ZIP, verify it's gone (list entries, assert not present)
  - Delete multiple files from ZIP
  - Delete from encrypted ZIP (with password)
  - Delete non-existent entry → throw `FileNotFoundException` (or skip silently — decide and document)
  - Delete on empty archive → no-op
  - Coverage: ZipEngine only (7z = integration, skip in unit test)

### Task 2: ZipEngine.DeleteEntriesAsync Implementation

- SharpZipLib `ZipFile` has `Delete(ZipEntry)` method (followed by `CommitUpdate()`)
- Implementation pattern (closely mirror existing `AddToArchiveAsync`):
  1. Open with `OpenZipFile()` to get `ZipFile` instance (auto-detect encoding)
  2. If entries are encrypted and password is null → throw `InvalidOperationException` (password required)
  3. `zipFile.BeginUpdate()`
  4. For each entryName → find `ZipEntry` by name → `zipFile.Delete(entry)`
  5. `zipFile.CommitUpdate()`
  6. If encrypted, the re-encryption is handled by SharpZipLib internally (it rewrites the whole ZIP)
- Progress: report `PercentComplete` based on processed count / total count

### Task 3: SevenZipEngine.DeleteEntriesAsync Implementation

- Shell out to `7z.exe d {archivePath} {entryName1} {entryName2} ...`
- Follow same pattern as CompressAsync/AddToArchiveAsync:
  - `ResolveSevenZipPath()` for auto-detection
  - `ArgumentList` (no command injection)
  - `cancellationToken.Register()` + `process.Kill()` for cancellation
  - Password file via temp response file (same as CompressAsync)
  - Progress: can't get per-file progress from 7z.exe, report at 0% then 100%
- TestAsync: handle exit code; 7z.exe exit code 0 = success, non-zero = failure

### Task 4: TarGzEngine / RarEngine / IsoEngine — NotSupportedException

- `TarGzEngine`: Add `DeleteEntriesAsync` → `throw new NotSupportedException("TAR/GZ format does not support in-place deletion")`
- `SevenZipEngine.CanHandle` returns true for RAR and ISO → they'll hit our SevenZipEngine impl, but 7z.exe won't handle RAR deletion well. Add a guard: if format is RAR or ISO, throw NotSupportedException.

### Task 5: UI — Add Files Button + Menu (Uses existing AddToArchiveAsync)

**5a. Format capability helper**
- Add a static helper `ArchiveFormatHelpers` (or similar) to determine if a format supports Add/Delete
- Used to enable/disable buttons without catching exceptions:
  ```csharp
  static bool CanAdd(ArchiveFormat fmt) => fmt is Zip or SevenZip;
  static bool CanDelete(ArchiveFormat fmt) => fmt is Zip or SevenZip;
  ```
- Place in `MainWindow.UI.cs` or a new utility class

**5b. Toolbar button**
- Add `<Button>` in toolbar between Test button and Password button
- Icon: "+" or similar visual
- Tooltip: "添加文件到压缩包…" (use localization key)
- `IsEnabled` bound to: `_currentArchivePath != null && CanAdd(_currentFormat)`
- Disabled state with tooltip for TarGz/RAR/ISO: "该格式不支持添加文件"
- Click handler: `AddFilesToArchive_Click`
  - Open `OpenFileDialog` (multi-select)
  - If user picks files → `AddFilesToArchiveAsync(files)`

**5c. Right-click context menu**
- Add `MenuItem` to `FileListGrid` context menu (in MainWindow.xaml)
- Header: use localization key (e.g. `Main_Ctx_AddFiles`)
- Separate from existing items (below separator, near bottom)
- Same click handler as toolbar
- `IsEnabled` bound to same condition as toolbar

**5d. AddFilesToArchiveAsync**
- Extract from `Window_Drop` / `AddFilesToCurrentArchiveAsync` → refactor into shared method used by both drag-drop and button/menu
- Same flow: confirm dialog (or skip if button-triggered — user already chose)
- `_currentPassword` passed to `AddToArchiveAsync` if archive is encrypted

### Task 6: UI — Delete Files (Right-click + Delete Key)

**6a. Right-click context menu**
- New `MenuItem` in XAML: Header via localization key + `InputGestureText="Del"`
- Click: `FileListCtx_Delete`

**6b. Delete key handler**
- Add `FileListGrid_PreviewKeyDown` event (or use existing if one exists)
- Filter: key == `Key.Delete`, not in edit mode (ensure DataGrid is not editing a cell)
- Same logic as right-click

**6c. Delete logic: `DeleteSelectedEntriesAsync`**
1. Get selected items from `FileListGrid.SelectedItems`
2. Expand directories to their contained files recursively (same pattern as `ExtractSelectedAsync`)
3. Build final deduplicated list of entry names to delete
4. Guard: if list is empty → return
5. Show confirmation dialog using localization key + entry count
6. If confirmed → call `engine.DeleteEntriesAsync(archivePath, entryNames, password, progress, ct)`
7. Refresh → `LoadArchiveAsync(_currentArchivePath)` to reload (resets selection, which is acceptable)
8. Handle `NotSupportedException` with appropriate message per format
9. Handle `OperationCanceledException` silently

### Task 7: Localization

- Add localization entries for all new UI strings in `src/MantisZip.UI/Localization/` (both `zh` and `en` if bilingual supported):
  - Toolbar button tooltip: "添加文件到压缩包…"
  - Right-click menu: "添加文件到压缩包…"
  - Right-click menu: "删除"
  - Delete confirmation message: "确定要删除选中的 {0} 个文件吗？此操作不可撤销。"
  - Disabled tooltip: "该格式不支持添加文件" / "该格式不支持删除"
  - NotSupported message per format

### Task 8: Verification

- Build: 0 errors, 0 warnings
- Tests: new TDD tests pass, existing tests not broken
- Manual verification checklist:
  - [ ] Drag-drop add → confirm dialog → refresh
  - [ ] Toolbar add button → file dialog → add → refresh
  - [ ] Right-click add → same flow as toolbar
  - [ ] Right-click delete → confirmation → delete → refresh
  - [ ] Delete key → same as right-click
  - [ ] Multi-select delete (Ctrl+click, Shift+click)
  - [ ] Directory delete (expands to all files)
  - [ ] TarGz/RAR/ISO → buttons disabled, NotSupportedException shown
  - [ ] Encrypted ZIP: add file keeps encryption, delete file works
  - [ ] 7z: add + delete via 7z.exe

## Key Design Decisions

1. **Delete confirmation**: Yes, always show before deletion. Dialog box with "确定/取消".
2. **Directory deletion**: Pre-expand directories to contained files in UI layer, engine receives flat file list. Matches existing `ExtractSelectedAsync` pattern.
3. **Password on delete**: `_currentPassword` is reused. For ZIP: SharpZipLib handles encryption on rewrite automatically if password is set. For 7z: pass `-p{password}` via temp response file.
4. **Compression level on add**: Reuses archive's existing level. For SharpZipLib: we can read `ZipFile`'s properties. For 7z: use `-mx` from the existing archive. (Note: reading existing level from SharpZipLib is not straightforward — may use AppSettings default as fallback.)
5. **Progress for delete**: Simple 0% → 100% (no per-file granularity, matching existing `SevenZipEngine.ExtractAsync` pattern for simplicity).

## Engine Support Matrix

| Format | Add | Delete | Notes |
|--------|:---:|:------:|-------|
| ZIP | ✅ | ✅ | SharpZipLib in-place update |
| 7z | ✅ | ✅ | 7z.exe `a` / `d` |
| TarGz | ❌ | ❌ | NotSupportedException |
| RAR | ❌ | ❌ | Read-only format |
| ISO | ❌ | ❌ | Read-only format |

## Open Questions (Auto-resolved)

- Q: Delete confirmation severity? → Simple confirmation, no "undo" mechanism (consistent with existing UX)
- Q: Add button icon? → Use text "+" or unicode ➕ since icon resources not available
- Q: 7z.exe d command supports multiple entries? → Yes, list all entry names as separate arguments

## Files to Modify

```
Add (new):
  tests/MantisZip.Tests/Engines/ZipEngineDeleteTests.cs
  (TDD tests)

Modify:
  src/MantisZip.Core/Abstractions/ArchiveEngine.cs
    — Add DeleteEntriesAsync to IArchiveEngine interface
    
  src/MantisZip.Core/Engines/ZipEngine.cs
    — Implement DeleteEntriesAsync
    
  src/MantisZip.Core/Engines/SevenZipEngine.cs
    — Implement DeleteEntriesAsync (7z.exe d)
    — Guard: RAR/ISO → NotSupportedException
    
  src/MantisZip.Core/Engines/TarGzEngine.cs
    — Add DeleteEntriesAsync → NotSupportedException

  src/MantisZip.UI/MainWindow.xaml
    — Toolbar: new Add button
    — Context menu: Add + Delete menu items
    
  src/MantisZip.UI/MainWindow.Menu.cs
    — Handler for Add toolbar/context
    — Refactor AddFilesToCurrentArchiveAsync from DragDrop.cs → shared
    — Handler for Delete context/key
    
  src/MantisZip.UI/MainWindow.DragDrop.cs
    — Extract shared AddFilesToCurrentArchiveAsync into reusable method
    
  src/MantisZip.UI/Localization/L.cs
    — Add new localization keys (source strings)
    
  src/MantisZip.UI/Localization/LanguageManager.cs  (or translation files)
    — Add translations for zh and en
