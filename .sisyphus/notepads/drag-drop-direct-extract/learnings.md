
## 2026-07-23 — Implementation Complete

### Architecture Decisions Validated
1. **Win32 overlay on STA thread** — necessary because Avalonia's DoDragDropAsync blocks UI thread; confirmed correct approach
2. **Late-binding COM** (Type.GetTypeFromCLSID + dynamic) — replaces SHDocVw compile-time reference, fixes MSB4803 "dotnet build doesn't support ResolveComReference"
3. **DataTransferItem.SetText()** — Avalonia 12.0.4 doesn't have SetData(); drag uses text format as carrier (extraction happens after DoDragDropAsync returns)
4. **DwmGetWindowAttribute** — used before GetWindowRect fallback for Win11 rounded corner detection

### Files Created (7)
- NativeMethods.cs, DropTargetDetector.cs, DragDropItemExpander.cs, DragDropService.cs, DragOverlayWindow.cs, DragPreviewPopup.cs, DragPreviewBitmapBuilder.cs

### Files Modified (5)
- MainWindow.axaml.cs (drag handlers replaced), MainWindowViewModel.cs (GetAllRawItems + GetSessionPassword), ArchiveItemModel.cs (ToCoreItem), ThemeLight.axaml + ThemeDark.axaml (6 brush resources)

### Verification
- dotnet build: all projects compile (Core, ShellExt, UI.Avalonia)
- dotnet test: 236/236 passed

### Remaining (manual QA only)
- T8.1: Manual test scenarios (needs running app with actual drag interaction)
- Definition of Done: acceptance criteria (all require runtime verification)
