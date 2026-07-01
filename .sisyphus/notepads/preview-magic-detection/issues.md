# Issues — Preview Magic Detection

## Phase 2 resolved: implemented in WPF (no Avalonia dependency)

**Status**: ✅ Resolved
**Impact**: All 7/7 plan checkboxes complete

Phase 2 was originally blocked by Avalonia dependency, but the WPF codebase has a fully functional preview system. Phase 2 UI integration was implemented directly in WPF:

**Changes made**:
1. `AppSettings.EnableFormatDetection` (bool, default true) added
2. ~35 lines of magic detection code inserted in `ShowPreviewAsync` (right after `ShowPreviewLoading()`)
3. Sets `PreviewHeader.Text` to `"📄 {item.Name} → {realFormatName}"`
4. Format-specific methods overwrite header for supported formats; magic detection result persists for unsupported ones

**Important for future**: When Avalonia port happens, the same ~50 lines must be replicated in Avalonia's `MainWindow.Preview.cs`.

**LSP note**: WPF `x:Name` elements (PreviewHeader, PreviewWebView2, etc.) show as CS0103 in LSP but compile fine — false positives due to WPF XAML code-gen not being indexed.
