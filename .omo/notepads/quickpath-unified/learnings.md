# Learnings

## 2026-06-22 Wave 1 Complete

- T1: ExplorerWindowTracker.cs created — COM late binding via Shell.Application, GetExplorerWindowInfo record, GetOpenExplorerWindows/GetActiveExplorerPath API
  - Note: Security.SecurityException not available in .NET 9 (removed from BCL) — use generic Exception catch
  - CA1416 warnings expected (Windows-only COM code)
- T2: FavoritePathManager.cs created — favorites.json persistence, system paths (Desktop/Documents/Downloads), user CRUD, hidden state, thread-safe
- T3: PathHistoryManager.cs created — 50-item dedup history, path-history.json persistence

All compile clean. Moving to Wave 2 (UI components).

## [2026-06-22 15:00] Wave 4-6 Complete
Status: All 14 implementation tasks + 3 test tasks completed.
Tasks: T11-T13 (QuickPathDialog integration for PasswordManager export/App.xaml 7z.dll/MainWindow compress), T14 (ArchiveSaveAsDialog), T15-T16 (unit/integration tests), T17 (build verified)

### Key Learnings
- QuickPathDialog needed DefaultFileName and SelectedFileName properties exposed for file-save mode callers
- PasswordManagerWindow Export needed using MantisZip.Core.Utils for PathHistoryManager access
- App.xaml.cs ShowSevenZipDllDialog replaced OpenFileDialog with QuickPathDialog (IsFileOpenMode=true)
- MainWindow.Menu.cs Compress_Click SaveFileDialog replaced with QuickPathDialog + PathHistoryManager.Record
- ArchiveSaveAsDialog created as new file-save dialog with QuickPathControl + DynamicFormatOptionsPanel + encryption options
- All tests created: FavoritePathManager (11 tests), PathHistoryManager (11 tests) - compile clean, can't run due to ShellExt.dll lock
- ShellExt.dll locked by Explorer.exe (pid 756) - post-build copy always fails; compilation succeeds

### Remaining
- F1-F4: Final Verification Wave (need manual review since task() broken)
- Must update remaining final checklist boxes in plan
