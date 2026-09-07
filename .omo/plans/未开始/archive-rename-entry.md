# Work Plan: Rename Entries Inside Archive

> **Status**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜] (0/6)
> **预估工时**: 🟡中 (3-4h)

## 任务总览

- [ ] **1. UI: 重命名对话框** — RenameEntryDialog.xaml/.cs (TextInput + 验证)
- [ ] **2. UI: 右键菜单 + F2 快捷键** — MainWindow 上下文菜单、快捷键、多选保护
- [ ] **3. Core: ArchiveEntryRename** — 提取→删除→添加 核心逻辑 (Core/Utils)
- [ ] **4. UI: 移动到…** — 目录选择弹窗 + 移动处理器
- [ ] **5. 本地化** — 中英文字符串 (~18 条)
- [ ] **6. 验证** — 构建 + ZIP/7z 重命名 + TarGz 不支持 + 冲突提示

## Objective

Add the ability to rename files/folders inside an existing archive without needing to recreate it from scratch.

**Core principle**: No in-place rename. The operation is: **extract entry to temp → delete old entry → add back with new name**. If that means recompression, so be it — the user has explicitly OK'd this tradeoff.

## Approach

### Core insight: rename and move are the same operation

In archives, an entry's `FullPath` IS its name (e.g. `dir1/file.txt`). Renaming a filename and moving it to a different directory differ only in which part of the path changes:

| Operation | Old | New |
|-----------|-----|-----|
| Rename file | `dir1/old.txt` | `dir1/new.txt` |
| Move file | `dir1/file.txt` | `dir2/file.txt` |
| Rename + move | `dir1/old.txt` | `dir2/new.txt` |

All three are implemented identically: **change the entry path → extract → delete → add back**.

### Mechanism

Since `IArchiveEngine` already has `ExtractAsync` (via `ArchiveEntryExtractor`), `DeleteEntriesAsync`, and `AddToArchiveAsync`, rename/move can be implemented at a higher level without modifying the engine interface:

1. Extract the single entry to `%TEMP%\MantisZip\Rename\{guid}\`
2. Call `DeleteEntriesAsync` to remove the old entry
3. Call `AddToArchiveAsync` to add the temp file with the new path
4. Clean up temp
5. Reload the archive

## Requirements

| Aspect | Value |
|--------|-------|
| **UI entry (rename)** | Right-click menu "重命名" + F2 key (single file only, not multi-select) |
| **UI entry (move)** | Right-click menu "移动到…" → shows folder tree dialog (single file only) |
| **Input (rename)** | Simple rename dialog (textbox + OK/Cancel) |
| **Input (move)** | Folder selection dialog (reuse existing folder tree or `Ookii.Dialogs` VistaFolderBrowser) |
| **Format support** | Zip ✅ (delete+add both supported), 7z ✅ (same), TarGz ❌ (delete throws `NotSupportedException`) |
| **Password** | Reuse `_currentPassword` for extract and delete; drop encryption on re-add (simplest approach) |
| **Directory rename** | Rename the folder entry; all nested children already deleted and re-added by `DeleteEntriesAsync` + `AddToArchiveAsync` recursively — **Phase 2** |
| **Conflict** | Warn if new name already exists in archive |
| **Progress** | `ProgressWindow` with extract phase + delete phase + add phase |
| **Post-action** | Refresh file list, restore current folder |

## Tasks

### Task 1: IArchiveEngine.CanRename — Interface default method

Add a `CanRename(ArchiveFormat)` virtual method to `IArchiveEngine` (default `false`).

- `ZipEngine`: override → `true`
- `SevenZipEngine`: override → `true`
- `TarGzEngine`: override → `false`

Alternatively: skip the interface method and just check `CanDelete` + `CanAdd` at the UI level, since rename reuses both. Simpler and avoids interface churn.

**Decision**: Use `CanDelete` + `CanAdd` check at UI level. No interface change needed.

### Task 2: Rename dialog — `RenameEntryDialog.xaml/.cs`

Simple modal dialog with:
- Title: "重命名"
- Current name displayed (read-only)
- New name textbox (pre-filled with current name, extension highlighted)
- Input validation: no path separators, no empty
- OK/Cancel buttons

**Style**: Match existing `PasswordDialog` / `ConflictDialog` visual style (Dark.xaml theme resources).

### Task 3: MainWindow rename/move handler — UI wiring

**3a. Context menu items**: Add two items to the DataGrid context menu in `MainWindow.xaml`:

```xml
<MenuItem Header="重命名" Click="FileListCtx_Rename" InputGestureText="F2"/>
<MenuItem Header="移动到…" Click="FileListCtx_MoveTo"/>
<Separator/>
<!-- existing items below -->
```

Position after Delete, before the existing separators.

**3b. Rename handler**: `RenameSelectedEntryAsync()` in `MainWindow.Menu.cs`.

Flow:
```
User clicks "重命名" (or presses F2)
  ↓
RenameEntryDialog shows with current filename (extension highlighted)
  ↓
User enters new name, clicks OK
  ↓
Validate: no empty, no '/' in new name
  ↓
Build new FullPath: replace filename portion while keeping directory prefix
  ↓
Check: does new FullPath already exist in _allItems?
  ├─ Yes → warning dialog "目标已存在，是否覆盖？"
  └─ No → proceed
  ↓
ArchiveEntryRename.RenameEntryAsync(...) with ProgressWindow
  ↓
Cleanup temp → reload archive
```

**3c. Move handler**: `MoveSelectedEntryToFolderAsync()`.

Flow:
```
User clicks "移动到…"
  ↓
Show folder selection: build dialog showing archive's directory tree
  (reuse _treeNodes or simple ListBox of folder paths)
  ↓
User selects target folder (or types new folder name with auto-creation)
  ↓
Build new FullPath: target_dir + original_filename
  ↓
Same conflict check as rename
  ↓
ArchiveEntryRename.RenameEntryAsync(...) with ProgressWindow
  ↓
Reload archive
```

**3d. Disable for multi-select**: `ContextMenuOpening` event to disable both "重命名" and "移动到…" when != 1 item selected.

**3e. F2 keyboard shortcut**: `FileListGrid_PreviewKeyDown` handler (same place as Delete key).

**3f. Directory rename/move (Phase 2)**: For directory entries, recursively process all children. Requires listing all entries starting with `dirPath/`, then for each: extract → delete → add with updated path prefix.

### Task 4: Core rename logic — `ArchiveEntryRename` (Core/Utils)

Utility class (not on engine interface) to encapsulate the extract-delete-add sequence:

```csharp
public static class ArchiveEntryRename
{
    /// <summary>
    /// 在压缩包内重命名一个条目。实际操作为：提取到临时目录 → 删除旧条目 → 添加新条目。
    /// </summary>
    public static async Task RenameEntryAsync(
        IArchiveEngine engine,
        string archivePath,
        ArchiveFormat format,
        string oldEntryPath,
        string newEntryPath,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct)
    {
        // 1. Extract to temp dir
        // 2. Delete old entry
        // 3. Add temp file with new entry path
        // 4. Cleanup
    }

    /// <summary>
    /// 重命名目录（递归重命名其下所有条目）。
    /// </summary>
    public static async Task RenameDirectoryAsync(
        IArchiveEngine engine,
        string archivePath,
        ArchiveFormat format,
        string oldDirPath,
        string newDirPath,
        IReadOnlyList<ArchiveItem> allItems,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct)
    {
        // Phase 2
    }
}
```

### Task 5: Localization

Add to `Localization/L.cs`:

| Key | Default (zh-CN) | English |
|-----|-----------------|---------|
| `Main_Ctx_Rename` | "重命名" | "Rename" |
| `Main_RenameTitle` | "重命名" | "Rename" |
| `Main_RenamePrompt` | "新名称：" | "New name:" |
| `Main_Rename_StatusExtracting` | "正在提取…" | "Extracting…" |
| `Main_Rename_StatusDeleting` | "正在删除…" | "Deleting…" |
| `Main_Rename_StatusAdding` | "正在添加…" | "Adding…" |
| `Main_Rename_Done` | "重命名完成" | "Rename complete" |
| `Main_Rename_ConflictTitle` | "名称冲突" | "Name conflict" |
| `Main_Rename_ConflictMsg` | "压缩包中已存在「{0}」，是否覆盖？" | "An entry named '{0}' already exists. Overwrite?" |
| `Main_Rename_InvalidName` | "名称不能包含 / 或为空" | "Name cannot contain / or be empty" |
| `Main_Rename_MultiSelect` | "请只选择一个文件" | "Please select a single file" |
| `Main_Rename_NotSupported` | "此压缩格式不支持重命名" | "This archive format does not support renaming" |

### Task 6: Verification

1. Build passes (0 errors)
2. Manual test: open a ZIP, right-click file → rename → verify archive is updated
3. Manual test: rename a file in a 7z archive
4. Manual test: F2 shortcut on a selected file
5. Manual test: rename a file to a name that already exists → conflict warning
6. Manual test: rename on TarGz → "不支持" message
7. Manual test: multi-select → "重命名" disabled/greyed

## File Changes Summary

| File | Change |
|------|--------|
| `src/MantisZip.UI/MainWindow.xaml` | Add "重命名" and "移动到…" MenuItems to context menu |
| `src/MantisZip.UI/MainWindow.Menu.cs` | Add `RenameSelectedEntryAsync`, `MoveSelectedEntryToFolderAsync`, `FileListCtx_Rename`, `FileListCtx_MoveTo`, `FileListGrid_PreviewKeyDown` F2 handler |
| `src/MantisZip.UI/RenameEntryDialog.xaml` | New file — simple rename dialog |
| `src/MantisZip.UI/RenameEntryDialog.xaml.cs` | New file — code-behind |
| `src/MantisZip.UI/MoveToFolderDialog.xaml` | New file — folder tree selection dialog for move |
| `src/MantisZip.UI/MoveToFolderDialog.xaml.cs` | New file — code-behind |
| `src/MantisZip.Core/Utils/ArchiveEntryRename.cs` | New file — core rename/move logic |
| `src/MantisZip.UI/Localization/L.cs` | Add ~18 new string constants |

## Open Questions

1. **Password handling**: When re-adding the file, should we preserve the original encryption? Simplest: no encryption on re-add (use current `ArchiveOptions` defaults). User can re-encrypt via re-compress.
2. **Directory rename/move (Phase 2)**: For directory entries, recursively rename all children. Requires listing all entries starting with `dirPath/`, then for each: extract → delete → add with updated path.
3. **Drag-drop move between tree folders (Phase 3)**: Drag file list entries onto TreeView folders. Same extract-delete-add under the hood, but triggered via drag-drop instead of dialog.
4. **In-place ZIP rename for same-length names**: Nice-to-have optimization for Phase 2. Same-length in-place rename avoids recompression entirely. Implement as fast-path check in `ArchiveEntryRename.RenameEntryAsync`.
