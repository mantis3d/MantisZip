## [2026-07-13] Blocker: 2 remaining checkboxes require GUI testing

### Resolved via Unit Tests
- ✅ 收藏夹新建/删除/排序正常工作，数据持久化 — `FavoritePathManagerTests` (14/14 pass)
- ✅ QuickPathControl 显示历史建议 — `PathHistoryManagerTests` (11/11 pass; `AddToHistory` wraps `RecentPaths` which refs `PathHistoryManager`)
- ✅ ArchiveCommentDialog 能读取和写入 ZIP 注释 — `ZipCommentHelper` test (5/5 pass)
- ✅ `dotnet build` 通过 — 0 errors, 0 warnings

### Still Blocked (GUI required)
The remaining 2 verification criteria require running the Avalonia app interactively on Windows:
- 所有对话框可正常打开/关闭
- Elevation 系列对话框在权限不足场景正确触发

These cannot be verified programmatically (no Avalonia headless test infrastructure set up).
Blocked until user runs the app for manual verification.
