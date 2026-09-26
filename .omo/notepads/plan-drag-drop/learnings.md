
## 2026-07-23 — DragDrop visual feedback files created

### Files created
- Services/DragPreviewBitmapBuilder.cs — UI-thread helper that pre-renders ResultTreeView to byte[] BGRA pixels via RenderTargetBitmap. Uses GCHandle pinning for CopyPixels (byte[] overload not available — must use (PixelRect, nint, int, int) signature).
- Services/DragPreviewPopup.cs — Pure Win32/GDI popup window on STA thread. Creates HBITMAP via CreateDIBSection, shows file tree bitmap + summary bar. No Avalonia/WPF/WinForms.
- Services/DragOverlayWindow.cs — Main overlay on dedicated STA thread. Runs PeekMessage loop, hover detection (150ms throttle), breath animation via System.Timers.Timer (50ms, sine 0.15-0.45 opacity), color-coded by DropTargetStatus. InvalidateRect for repaint — not in NativeMethods, added as private DllImport in class.

### Key decisions
- BuildDragPreview didn't exist in ResultPreviewService — used BuildExtractPreview with modified root node instead.
- RenderTargetBitmap.CopyPixels requires pinned IntPtr + buffer size + stride (no byte[] overload in this Avalonia version).
- DragOverlayWindow uses InvalidateRect (user32) which is not in NativeMethods — added as private DllImport in DragOverlayWindow.
- Cross-thread DestroyWindow is safe on Win10+ for layered popup windows.
- PreviewBitmapData class defined alongside DragPreviewBitmapBuilder to keep the type co-located with its production path.
