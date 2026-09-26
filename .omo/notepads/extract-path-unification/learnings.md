# Learnings - Extract Path Unification

## Initial Codebase State
- `IArchiveEngine.ExtractEntriesAsync` has 7 params (no `outputPathOverrides`)
- `ZipEngine.ExtractEntriesAsync` uses `FileConflictHelper.GetSafePath(destinationPath, entryKey)` at line 378
- `SevenZipEngine.ExtractEntriesAsync` uses `FileConflictHelper.GetSafePath(destinationPath, fileName)` at line 680
  - Note: SevenZipEngine normalizes entryKey via `ArchivePath.Normalize(k)` before HashSet lookup
- `TarGzEngine.ExtractEntriesAsync` throws `NotSupportedException` (streaming format)
- `ExtractSelectedAsync` in `MainWindow.Menu.cs` (line 618) has a manual for-loop calling `ArchiveEntryExtractor.ExtractEntryAsync`
- `ProgressWindow.CreateBackgroundProgress(pw)` creates `IProgress<ArchiveProgress>` for engine progress integration

## 2026-07-10: Added `outputPathOverrides` parameter

- Added `IReadOnlyDictionary<string, string>? outputPathOverrides = null` as 8th parameter to `IArchiveEngine.ExtractEntriesAsync`
- Applied in `ZipEngine`: `outputPathOverrides?.GetValueOrDefault(entryKey) ?? FileConflictHelper.GetSafePath(destinationPath, entryKey)`
- Applied in `SevenZipEngine`: keys matched against already-normalized `fileName` variable (normalized at `ArchivePath.Normalize(entry.FileName)`)
- `TarGzEngine` also needed the parameter added for interface compliance (C# requires matching signatures for implicit interface implementation even with default parameter values), but its behavior (throws `NotSupportedException`) is unchanged
- Build passes with 0 errors

## 2026-07-10: Rewrote `ExtractSelectedAsync` to use `engine.ExtractEntriesAsync()`

- Removed the manual for-loop in `ExtractSelectedAsync` (MainWindow.Menu.cs lines 641-669) that called `ArchiveEntryExtractor.ExtractEntryAsync` per-item
- Replaced with logic that builds `entryKeys` list + `pathOverrides` dictionary from `filesToExtract`, then calls `engine.ExtractEntriesAsync()`
- Tar/Gz format falls back to `engine.ExtractAsync()` (full extract) since streaming formats don't support per-entry extraction
- Path clipping logic (ExtractPreserveFullPath + _currentFolder stripping) preserved in the override-building loop
- Manual `pw.CancellationToken.ThrowIfCancellationRequested()` and `pw.SetProgress()` calls in the loop body removed — progress handling now delegated to the engine
- Added `using MantisZip.Core.Engines;` to MainWindow.Menu.cs
- Catch blocks (OperationCanceledException and Exception) kept exactly as-is
