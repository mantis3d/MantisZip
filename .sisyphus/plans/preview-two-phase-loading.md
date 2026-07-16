# Two-Phase Preview Loading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate "stale content" confusion in Avalonia preview panel by implementing immediate loading state + info panel population before async content extraction, plus version-stamp guard against race conditions.

**Architecture:** Two-phase approach mirrors WPF's existing pattern: Phase 1 (synchronous, immediate) shows loading overlay + fills info panel from `ArchiveItemModel` properties already in memory; Phase 2 (async) extracts file, loads content, replaces overlay. A `_previewLoadVersion` counter in `MainWindowViewModel` prevents stale async completions from overwriting newer previews.

**Tech Stack:** Avalonia UI, CommunityToolkit.Mvvm source generators

---

### Task 1: Add loading state to PreviewViewModel

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs`

- [ ] **Step 1: Add loading state properties**

Add these properties inside the `PreviewViewModel` class (near other `[ObservableProperty]` fields, after line 42):

```csharp
[ObservableProperty]
private bool _isLoadingPreview;

[ObservableProperty]
private string _loadingFileName = string.Empty;
```

- [ ] **Step 2: Add `ShowLoading()` method**

Add this method near `Clear()` (around line 1506):

```csharp
/// <summary>
/// Switch to loading state: clear old content, show loading indicator with file name.
/// Phase 1 of two-phase preview — called immediately when user selects a new file.
/// </summary>
public void ShowLoading(string? fileName = null)
{
    // Full clear like Clear() but sets loading state instead of None
    PreviewType = PreviewType.None;
    TextContent = string.Empty;
    HeaderText = string.Empty;
    PeTitle = string.Empty;
    PeSubtitle = string.Empty;
    PeMetadata.Clear();
    CsvData = null;
    FormatMetadata.Clear();
    PreviewHeaderText = string.Empty;
    PreviewImage = null;
    ImageWidth = 0;
    ImageHeight = 0;
    HtmlContent = string.Empty;
    TorrentTreeRoots.Clear();
    SqliteTableData = null;
    SqliteTableNames.Clear();
    SelectedTableIndex = 0;
    _lastPreviewFilePath = null;
    StopGifTimer();
    _gifFrames = null;
    FontFamily = global::Avalonia.Media.FontFamily.Default;
    IsToolbarVisible = false;
    ZoomLevel = 1.0;
    FontSize = 13;

    // Don't reset info panel — SetFileInfo will be called immediately after ShowLoading

    // Show loading overlay
    LoadingFileName = fileName ?? string.Empty;
    IsLoadingPreview = true;
    IsPreviewVisible = true;
}
```

- [ ] **Step 3: Update `Clear()` to also reset loading state**

In `Clear()` (line 1506), add these two lines after `IsInfoPanelVisible = false;`:

```csharp
IsLoadingPreview = false;
LoadingFileName = string.Empty;
```

---

### Task 2: Add loading overlay to PreviewPanel AXAML

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml`

- [ ] **Step 1: Add loading state UI layer**

Inside the preview content area `<Grid>` (after the `<ScrollViewer x:Name="PreviewContentScroller">` opens, before the existing `<Grid>` with all preview type sections at line 78), add as the **first child** of that inner `<Grid>`:

```xml
<!-- Loading state overlay (Phase 1 — shown immediately on file selection) -->
<StackPanel IsVisible="{Binding IsLoadingPreview}"
            VerticalAlignment="Center"
            HorizontalAlignment="Center"
            Spacing="12">
  <ProgressBar IsIndeterminate="True"
               Width="200"
               Height="6" />
  <TextBlock Text="{Binding LoadingFileName, StringFormat=正在加载: {0}}"
             HorizontalAlignment="Center"
             FontSize="14"
             Foreground="{DynamicResource ThemeTextSecondaryBrush}"
             TextWrapping="Wrap"
             MaxWidth="400"
             TextAlignment="Center" />
</StackPanel>
```

---

### Task 3: Implement two-phase flow + version guard in MainWindowViewModel

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add version counter field**

Add near the top of `MainWindowViewModel` class (among other private fields, around line 50):

```csharp
private int _previewLoadVersion;
```

- [ ] **Step 2: Restructure `ShowPreviewAsync` for two-phase + version guard**

Replace the current `ShowPreviewAsync` method body (lines 539–730) with the following two-phase implementation:

```csharp
private async Task ShowPreviewAsync(ArchiveItemModel entry)
{
    App.DebugLog($"[PRV] ShowPreviewAsync start: {entry.Name}, fmt={_currentFormat}");

    // Phase 1: Immediate — show loading state + populate info panel from in-memory data
    // This runs synchronously before any async extraction, so user never sees stale content.
    var version = Interlocked.Increment(ref _previewLoadVersion);
    Preview.StopGifTimer();
    Preview.ShowLoading(entry.NameDisplay ?? entry.Name);
    Preview.SetFileInfo(
        entry.NameDisplay,
        entry.SizeDisplay,
        entry.CompressedSizeDisplay,
        entry.Size > 0 ? $"{entry.CompressionRatio:F1}%" : "N/A",
        entry.LastModifiedDisplay);
    StatusMessage = LocalizationManager.T("Status_Extracting");

    try
    {
        var ext = Path.GetExtension(entry.Name);

        // ── Magic detection ──
        var previewType = PreviewType.Unsupported;
        FileFormat magicFormat = FileFormat.Unknown;
        string? detectedFormatName = null;
        if (PreviewService.EnableFormatDetection && CurrentArchivePath != null)
        {
            try
            {
                _sessionPasswords.TryGetValue(CurrentArchivePath, out var pwd);
                var (magicType, format, displayName) = await PreviewService.ClassifyPreviewByMagicAsync(
                    CurrentArchivePath, entry, _currentFormat,
                    PreviewService.PreviewHeadSize, pwd);
                if (magicType != PreviewType.Unsupported && format != FileFormat.Unknown)
                {
                    previewType = magicType;
                    magicFormat = format;
                    detectedFormatName = displayName;
                    App.DebugLog($"[PRV] Magic detected: {format} ({displayName}) -> {previewType}");
                }
            }
            catch (Exception ex)
            {
                App.DebugLog($"[PRV] Magic detection failed: {ex.Message}");
            }
        }

        if (previewType == PreviewType.Unsupported)
        {
            previewType = PreviewService.ClassifyPreview(ext);
            App.DebugLog($"[PRV] Fallback to extension classification: {previewType}");
        }

        if (detectedFormatName == null && PreviewService.EnableFormatDetection)
        {
            var extFormat = FileFormatDetector.DetectByExtension(ext);
            if (extFormat != FileFormat.Unknown)
                detectedFormatName = FileFormatHelper.GetDisplayName(extFormat);
        }

        if (previewType == PreviewType.Unsupported)
        {
            Preview.ShowUnsupported();
            StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
            return;
        }

        if (CurrentArchivePath == null)
        {
            App.DebugLog("[PRV] CurrentArchivePath is null, aborting");
            return;
        }

        // ── Extract to temp (async, slow) ──
        var tempFile = await PreviewService.ExtractToTempAsync(
            CurrentArchivePath, entry, _currentFormat);
        App.DebugLog($"[PRV] Extracted to: {tempFile}");

        if (tempFile == null)
        {
            Preview.ShowUnsupported(LocalizationManager.T("Status_ExtractFailed"));
            return;
        }

        // Version guard: if user selected another file while we were extracting, discard this result
        if (version != _previewLoadVersion)
        {
            App.DebugLog($"[PRV] Stale preview result discarded (version {version} != {_previewLoadVersion})");
            // Clean up temp file
            try { File.Delete(tempFile); } catch { /* best effort */ }
            return;
        }

        // Phase 2: Content loaded — show the actual preview
        switch (previewType)
        {
            case PreviewType.Text:
                Preview.ShowText(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Text", entry.DisplayName);
                break;
            case PreviewType.Csv:
                Preview.ShowCsv(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Csv", entry.DisplayName);
                break;
            case PreviewType.Pe:
                Preview.ShowPe(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Pe", entry.DisplayName);
                break;
            case PreviewType.Image:
                var icoExt = Path.GetExtension(tempFile).ToLowerInvariant();
                if (icoExt == ".ico")
                {
                    Preview.ShowIcoGallery(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Ico", entry.DisplayName);
                }
                else
                {
                    Preview.ShowImage(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Image", entry.DisplayName);
                }
                break;
            case PreviewType.Gif:
                Preview.ShowGif(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Gif", entry.DisplayName);
                break;
            case PreviewType.Svg:
                Preview.ShowSvg(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Svg", entry.DisplayName);
                break;
            case PreviewType.Font:
                Preview.ShowFont(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Font", entry.DisplayName);
                break;
            case PreviewType.Audio:
                Preview.ShowAudio(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Audio", entry.DisplayName);
                break;
            case PreviewType.Sqlite:
                Preview.ShowSqlitePreview(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Sqlite", entry.DisplayName);
                break;
            case PreviewType.Iso:
                Preview.ShowIso(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Iso", entry.DisplayName);
                break;
            case PreviewType.Torrent:
                Preview.ShowTorrent(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Torrent", entry.DisplayName);
                break;
            case PreviewType.Office:
                Preview.ShowOffice(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Office", entry.DisplayName);
                break;
            case PreviewType.Video:
                Preview.ShowVideo(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Video", entry.DisplayName);
                break;
            case PreviewType.Html:
                Preview.ShowHtmlPreview(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Html", entry.DisplayName);
                break;
            case PreviewType.Markdown:
                Preview.ShowMarkdownPreview(tempFile);
                StatusMessage = LocalizationManager.T("Preview_Markdown", entry.DisplayName);
                break;
        }

        // Populate format metadata from magic detection (if any)
        if (detectedFormatName != null)
        {
            var extFormat = FileFormatDetector.DetectByExtension(ext);
            bool hasConflict = extFormat != FileFormat.Unknown
                && magicFormat != FileFormat.Unknown
                && extFormat != magicFormat;
            string formatValue = hasConflict
                ? $"⚠️ {detectedFormatName}（扩展名: {ext}）"
                : detectedFormatName;
            for (int i = Preview.FormatMetadata.Count - 1; i >= 0; i--)
            {
                if (Preview.FormatMetadata[i].Key == "格式")
                    Preview.FormatMetadata.RemoveAt(i);
            }
            Preview.FormatMetadata.Insert(0, new FormatMetadataItem("格式", formatValue));
        }
    }
    catch (Exception ex)
    {
        App.DebugLog($"[PRV] ShowPreviewAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        Preview.ShowUnsupported(LocalizationManager.T("Status_PreviewFailed", ex.Message));
        StatusMessage = LocalizationManager.T("Status_PreviewFailed", ex.Message);
    }
    App.DebugLog("[PRV] ShowPreviewAsync end");
}
```

Key changes from current code:
1. **Lines 2-3**: Phase 1 — `ShowLoading()` + `SetFileInfo()` called immediately, before any `await`
2. **Line 1**: `Interlocked.Increment` for version stamp
3. **Lines 4-5**: Reordered — `StopGifTimer()` moved before the async work, immediately at entry
4. **Lines after extract**: Version guard check `if (version != _previewLoadVersion)`
5. **Removed**: The old `SetFileInfo` call at the end (now done in Phase 1 instead)

---

### Task 4: Verify and test

- [ ] **Step 1: LSP diagnostics**

Run diagnostics on all modified files:

```powershell
# Check for compilation errors
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj --no-restore 2>&1 | Select-String -Pattern "error"
```

Expected: No errors.

- [ ] **Step 2: Run application and verify behavior**

```powershell
dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
```

Manual verification checklist:
1. Open an archive with multiple files
2. Click a file → immediately see loading overlay + info panel populated
3. After extraction → content replaces loading overlay
4. Click another file rapidly → loading overlay replaces previous content immediately
5. Click a small file then a large file → fast file's result is not overwritten by slow file
6. Verify GIF timer is stopped on file switch (stop + restart)
