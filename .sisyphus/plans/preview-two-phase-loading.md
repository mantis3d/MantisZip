# Two-Phase Preview Loading Implementation Plan

> **状态**: ✅ 已完成（2026-07-16 实施落地，PROGRESS.md 有完整记录；本文档已按实际实现修正归档）
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate "stale content" confusion in Avalonia preview panel by implementing immediate loading state + info panel population before async content extraction, plus version-stamp guard against race conditions.

**Architecture:** Two-phase approach mirrors WPF's existing pattern: Phase 1 (synchronous, immediate) shows loading overlay + fills info panel from `ArchiveItemModel` properties already in memory; Phase 2 (async) extracts file, loads content, replaces overlay. A `_previewLoadVersion` counter in `MainWindowViewModel` prevents stale async completions from overwriting newer previews.

**Tech Stack:** Avalonia UI, CommunityToolkit.Mvvm source generators

---

### Task 1: Add loading state to PreviewViewModel

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs`

- [x] **Step 1: Add loading state properties** ✅ 已实施（`PreviewViewModel.cs`，属性位于类字段区）

Add these properties inside the `PreviewViewModel` class (near other `[ObservableProperty]` fields, after line 42):

```csharp
[ObservableProperty]
private bool _isLoadingPreview;

[ObservableProperty]
private string _loadingFileName = string.Empty;
```

- [x] **Step 2: Add `ShowLoading()` method** ✅ 已实施（`PreviewViewModel.cs:2656`）

> **实际实现与计划差异**：计划初稿要求复制 `Clear()` 的全部重置逻辑（~30 行字段清空）；最终实现更简洁——`ShowLoading()` 直接调用 `Clear()` 复用重置逻辑，再覆盖 loading 状态，避免两处重置逻辑漂移。另新增 `LoadingFileDisplay` 只读属性（本地化文案"正在加载: {0}"）与 `OnLoadingFileNameChanged` 同步通知。

实际代码（与 `Clear()` 保持同步，后续新增预览字段只需维护 `Clear()`）：

```csharp
/// <summary>
/// Switch to loading state: clear old content, show loading indicator with file name.
/// Phase 1 of two-phase preview — called immediately when user selects a new file.
/// </summary>
public void ShowLoading(string? fileName = null)
{
    // Reuse Clear() to reset all preview state, then override for loading phase.
    // This avoids duplicated reset logic — Clear() and ShowLoading() stay in sync.
    Clear();
    LoadingFileName = fileName ?? string.Empty;
    IsLoadingPreview = true;
    IsPreviewVisible = true;
}
```

- [x] **Step 3: Update `Clear()` to also reset loading state** ✅ 已实施（`PreviewViewModel.cs:2648-2649`）

In `Clear()`, these two lines were added at the end (after `IsInfoPanelVisible = false;`):

```csharp
IsLoadingPreview = false;
LoadingFileName = string.Empty;
```

---

### Task 2: Add loading overlay to PreviewPanel AXAML

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml`

- [x] **Step 1: Add loading state UI layer** ✅ 已实施（`PreviewPanel.axaml:573-627`）

> **实际实现与计划差异**：计划初稿用普通 `ProgressBar IsIndeterminate`；最终实现为**全页居中弹跳点动画**（3 个圆形 Border，Opacity 0.25↔1.0 循环，各自 `Delay` 0/0.2/0.4s 错开相位），`Spacing=20`，文件名用本地化后的 `LoadingFileDisplay` 属性绑定（而非 `StringFormat` 硬编码"正在加载: "前缀）。

```xml
<!-- Loading page (Phase 1 — replaces preview content area entirely) -->
<StackPanel IsVisible="{Binding IsLoadingPreview}"
            VerticalAlignment="Center"
            HorizontalAlignment="Center"
            Spacing="20">
  <!-- Bouncing dots spinner (3 Borders, staggered Delay 0/0.2/0.4s) -->
  <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Spacing="{DynamicResource SpacingXs}">
    <Border Width="10" Height="10" CornerRadius="5" Background="{DynamicResource ThemeTextSecondaryBrush}">
      <Border.Styles>
        <Style Selector="Border">
          <Style.Animations>
            <Animation Duration="0:0:1.0" IterationCount="Infinite">
              <KeyFrame Cue="0%"><Setter Property="Opacity" Value="0.25"/></KeyFrame>
              <KeyFrame Cue="40%"><Setter Property="Opacity" Value="1.0"/></KeyFrame>
              <KeyFrame Cue="60%"><Setter Property="Opacity" Value="0.25"/></KeyFrame>
              <KeyFrame Cue="100%"><Setter Property="Opacity" Value="0.25"/></KeyFrame>
            </Animation>
          </Style.Animations>
        </Style>
      </Border.Styles>
    </Border>
    <!-- ... 第 2/3 个 Border 同构，Animation Delay 分别 0:0:0.2 / 0:0:0.4 ... -->
  </StackPanel>
  <TextBlock Text="{Binding LoadingFileDisplay}"
             HorizontalAlignment="Center" FontSize="14"
             Foreground="{DynamicResource ThemeTextSecondaryBrush}"
             TextWrapping="Wrap" MaxWidth="400" TextAlignment="Center" />
</StackPanel>
```

**加载页关闭时机**：内容就绪后由 `OnPreviewTypeChanged` 观察 `PreviewType != None` 自动关闭（计划未覆盖的补充机制，避免手动逐处关闭）。

---

### Task 3: Implement two-phase flow + version guard in MainWindowViewModel

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`

- [x] **Step 1: Add version counter field** ✅ 已实施（`MainWindowViewModel.cs:25`）

Add near the top of `MainWindowViewModel` class (among other private fields, around line 50):

```csharp
private int _previewLoadVersion;
```

- [x] **Step 2: Restructure `ShowPreviewAsync` for two-phase + version guard** ✅ 已实施（`MainWindowViewModel.cs:960+`）

> **实际实现与计划差异**：计划的 `Preview.SetFileInfo(...)` 已随信息面板重构更名为 **`Preview.UpdateCommonMetadata(...)`**（`PreviewViewModel.cs:2545`）——签名扩展为 `(fileName, fileSize, compressedSize, compressionRatio, modifiedDate)` 五个参数，内部走 `MetadataHelper.RenderCommonToViewModel` 渲染通用 section，并设置 `IsFormatPending=true` 等 Phase 2 的 `ShowXxx` 填充格式 section（详见下方代码中 Phase 1 部分）。其余两阶段结构、版本守卫、异常路径与计划一致。

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
    Preview.UpdateCommonMetadata(
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
1. **Lines 2-3**: Phase 1 — `ShowLoading()` + `UpdateCommonMetadata()` called immediately, before any `await`
2. **Line 1**: `Interlocked.Increment` for version stamp
3. **Lines 4-5**: Reordered — `StopGifTimer()` moved before the async work, immediately at entry
4. **Lines after extract**: Version guard check `if (version != _previewLoadVersion)`
5. **Removed**: The old `SetFileInfo`/`UpdateCommonMetadata` call at the end (now done in Phase 1 instead)

---

### Task 4: Verify and test

- [x] **Step 1: LSP diagnostics** ✅ 已通过（2026-07-16 实施时 Build 0 errors 0 warnings）

Run diagnostics on all modified files:

```powershell
# Check for compilation errors
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj --no-restore 2>&1 | Select-String -Pattern "error"
```

Expected: No errors.

- [x] **Step 2: Run application and verify behavior** ✅ 已通过 + 自动化回归测试

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

**自动化回归测试**（`tests/MantisZip.UI.Avalonia.Tests/PreviewViewModelTests.cs`）：`ShowSvg_AfterShowLoading_SetsPreviewTypeAndDismissesLoading` — 验证 `ShowSvg` 成功后必须设置 `PreviewType = Svg` 并关闭加载页（缺失该赋值会导致预览永远停留在加载状态，曾为真实 bug）。

> **备注**：版本守卫 `_previewLoadVersion` 本身（手动验证清单第 5 条的竞态场景）目前无专门自动化单测，仅有上述 ShowLoading→ShowSvg 回归测试。

---

## 归档记录（2026-08-06）

**实施完成情况**：本计划于 **2026-07-16** 全部实施落地，PROGRESS.md 对应条目已记录（"预览两阶段加载：立即信息栏 + 弹跳点加载页 → 异步内容"）。PLAN.md 条目已移除，PROGRESS.md【历史设计方案索引】保留引用。

**实际实现与计划的差异汇总**：

> **行号说明**：计划正文中的行号引用（`line 42` / `line 1506` / `lines 539–730` / `around line 50` 等）为撰写时的定位指引，随代码演进已全部失效；归档后不再作为操作依据，实际位置以各 Step 标注的当前行号为准。

| 计划文本 | 实际实现 |
|---------|---------|
| `ShowLoading()` 复制 `Clear()` 全部重置逻辑 | 直接调用 `Clear()` 复用重置逻辑，仅覆盖 loading 状态（`PreviewViewModel.cs:2656`） |
| loading overlay 用 `ProgressBar IsIndeterminate` | 全页居中**弹跳点动画**（3 个 Border 循环 Opacity，Delay 错相），`Spacing=20`（`PreviewPanel.axaml:573`） |
| 文件名用 `LoadingFileName` + `StringFormat=正在加载: {0}` | 新增 `LoadingFileDisplay` 本地化属性（`Preview_LoadingFile` key），绑定即含文案 |
| Phase 1 调用 `SetFileInfo(...)`（4 参数） | 信息面板重构后改为 `UpdateCommonMetadata(...)`（5 参数，含 `CompressedSizeDisplay`），内部走 `MetadataHelper` + `IsFormatPending` 机制 |
| 加载页关闭时机未明确 | `OnPreviewTypeChanged` 观察 `PreviewType != None` 自动关闭，覆盖全部 ShowXxx 路径 |
| 无自动化测试 | 新增 `PreviewViewModelTests.ShowSvg_AfterShowLoading_SetsPreviewTypeAndDismissesLoading` 回归测试 |
