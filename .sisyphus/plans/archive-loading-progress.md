# Archive Loading Progress Indicator

> **状态**: ✅ 已完成 | **阶段**: [✅] (1/1)

## TL;DR
> **Quick Summary**: Add a centered overlay (indeterminate ProgressBar + "加载中…" text, then entry count "正在处理 N 个文件…") inside the file list area that shows while a large archive is being opened, before the file list populates.
>
> **Deliverables**:
> - XAML: `ArchiveLoadingOverlay` Border inside `InnerContentGrid` (over TreeView + DataGrid), with progress text + indeterminate bar + count label
> - Code-behind: Show overlay at start of `LoadArchiveAsync`, update to entry count after `ListEntriesAsync` returns, hide after filter completes
>
> **Estimated Effort**: Quick
> **Parallel Execution**: NO — single task
> **Critical Path**: Task 1

---

## Context

### Original Request
> 打开一个较大的压缩包时，在文件列表加载之前，能有一个加载进度条之类的东西吗？

### Interview Summary
**Key Discussions**:
- Current `LoadArchiveAsync` only sets status bar text to "加载中…" — not visible enough for large archives
- The existing `PreviewLoadingPanel` pattern (indeterminate ProgressBar + centered text + percent text) serves as a good template
- The loading overlay should sit inside `InnerContentGrid` (Grid.Row="0", Grid.ColumnSpan="5") so it overlays both the folder tree and the file list
- True progress percentage requires modifying `IArchiveEngine.ListEntriesAsync` interface (Core change) — deferred to future
- **Phase 1 approach**: Show indeterminate bar during `ListEntriesAsync`, then show "正在处理 {items.Count} 个文件…" count label after entries are returned

**Research Findings**:
- `LoadArchiveAsync` (MainWindow.xaml.cs:263) is the single entry point for all archive opening (normal open, `--open` CLI, post-compression)
- The slow operation is `engine.ListEntriesAsync(archivePath, ...)` at ~line 286
- After `ListEntriesAsync` returns, there's a fast `.Select()` conversion + `FilterFiles("")` + `BuildFolderTree()` — typically fast
- `FileListPanel.Visibility` is set to `Visible` at line 372 (after everything is loaded)
- `DropHint.Visibility` is set to `Collapsed` at line 373
- Error handler at lines 392–405 does not reset overlay visibility

### Metis Review
> *(Metis consultation results incorporated after initial draft)*

---

## Work Objectives

### Core Objective
Show a centered indeterminate progress bar overlay in the file list area while an archive is loading.

### Concrete Deliverables
- `MainWindow.xaml`: New `ArchiveLoadingOverlay` Border element
- `MainWindow.xaml.cs`: Show/hide logic in `LoadArchiveAsync`

### Definition of Done
- [x] `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` → 0 errors, 0 warnings
- [x] Open a large archive, see loading overlay appear before file list shows
- [x] Overlay disappears when file list is ready
- [x] On error, overlay is hidden and DropHint is shown

### Must Have
- Overlay appears before `engine.ListEntriesAsync()` is called
- Overlay disappears after `FilterFiles("")` completes and file list shows
- Overlay is hidden on error (catch block)
- Uses existing `Theme_*` resource keys for styling (no new colors)

### Must NOT Have (Guardrails)
- No changes to engine or Core project
- No changes to any other file list flow (extract, compress progress already handled by ProgressWindow)

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed. No exceptions.

### Test Decision
- **Infrastructure exists**: NO (no UI test project)
- **Automated tests**: None
- **Agent-Executed QA**: Build verification + visual inspection via screenshot

### QA Policy
Every task MUST include agent-executed QA scenarios.

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Single task):
├── Task 1: Add ArchiveLoadingOverlay XAML + show/hide logic [quick]
```

---

## TODOs

- [x] 1. Add archive loading overlay (XAML + code-behind)

  **What to do**:
  1. In `MainWindow.xaml`, inside `InnerContentGrid` (before its closing `</Grid>` tag):
     - Add a `Border x:Name="ArchiveLoadingOverlay"` with `Grid.Row="0"` and `Grid.ColumnSpan="5"`, `Background="{StaticResource Theme_WindowBg}"`, `Visibility="Collapsed"`
     - Inside: a centered `StackPanel` with:
       - `TextBlock x:Name="ArchiveLoadingText"` bound to `{l:L Main_Status_Loading}`, FontSize="16", Foreground Theme_TextDisabled, centered
       - `ProgressBar x:Name="ArchiveLoadingBar" Width="240" Height="6" IsIndeterminate="True"` with `Foreground="{StaticResource Theme_Accent}"`, Margin="0,16,0,0"
       - `TextBlock x:Name="ArchiveLoadingPercent"` Text="" FontSize="11" Foreground Theme_TextDisabled, centered, Margin="0,4,0,0"
     - Match the styling pattern of the existing `PreviewLoadingPanel`

  2. In `MainWindow.xaml.cs`, inside `LoadArchiveAsync`:
     - **Show overlay at start** (before slow operations, after `App.TraceLog(...)` at line 265):
       ```csharp
       FileListPanel.Visibility = Visibility.Visible;
       ArchiveLoadingOverlay.Visibility = Visibility.Visible;
       ArchiveLoadingBar.IsIndeterminate = true;
       ArchiveLoadingText.Text = L.T(L.Main_Status_Loading); // "正在加载压缩包…"
       ArchiveLoadingPercent.Text = "";
       DropHint.Visibility = Visibility.Collapsed;
       ```
     - **Update to entry count** (after `engine.ListEntriesAsync()` returns, before the `.Select()` conversion):
       ```csharp
       ArchiveLoadingText.Text = L.TF(L.Main_Status_ProcessingEntries, items.Count);
       // e.g. "正在处理 156 个文件…"
       ```
       (Note: `L.Main_Status_ProcessingEntries` is a new localization key — see below)
     - **Hide on success** (after `FilterFiles("")` at line 370):
       ```csharp
       ArchiveLoadingOverlay.Visibility = Visibility.Collapsed;
       ```
     - **On error** (in the `catch` block at line 392):
       ```csharp
       ArchiveLoadingOverlay.Visibility = Visibility.Collapsed;
       FileListPanel.Visibility = Visibility.Collapsed;
       DropHint.Visibility = Visibility.Visible;
       ```
     - Remove the old `FileListPanel.Visibility = Visibility.Visible;` and `DropHint.Visibility = Visibility.Collapsed;` from lines 372–373 (now done at the start).

  3. **New localization key** (in `Localization.resx` / localized strings):
     - `Main_Status_ProcessingEntries`: `"正在处理 {0} 个文件…"` / `"Processing {0} entries…"`
     - Search for `Main_Status_Loaded` in the locale files to find where to add the new key

  **Must NOT do**:
  - Don't add new localization strings — reuse `L.T(L.Main_Status_Loading)`
  - Don't modify any Core file or XAML outside MainWindow.xaml
  - Don't change any engine or archive reading logic

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Small, well-defined UI change. One XAML element + ~10 lines of code-behind.
  - **Skills**: `[]`
    - No specialized skills needed

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: None
  - **Blocked By**: None

  **References** (CRITICAL — Be Exhaustive):

  **Pattern References**:
  - `src/MantisZip.UI/MainWindow.xaml:385-403` — `PreviewLoadingPanel` (existing pattern to follow for styling)
  - `src/MantisZip.UI/MainWindow.xaml:144-152` — `DropHint` Border pattern (existing overlay pattern)

  **Code References**:
  - `src/MantisZip.UI/MainWindow.xaml:155-281` — `FileListPanel` / `InnerContentGrid` (insertion location for the overlay)
  - `src/MantisZip.UI/MainWindow.xaml.cs:263-406` — `LoadArchiveAsync` (show/hide logic insertion points:
    - Line 265: after `TraceLog`, show overlay
    - Line 370: after `FilterFiles("")`, hide overlay
    - Lines 392-405: catch block, hide overlay + reset visibility)

  **External References**:
  - None needed

  **WHY Each Reference Matters**:
  - PreviewLoadingPanel provides the exact styling pattern to follow (Theme_WindowBg background, centered layout, indeterminate ProgressBar with Theme_Accent)
  - DropHint shows the existing overlay pattern (Border with Visibility toggle)
  - LoadArchiveAsync is the only method that needs modification

  **Acceptance Criteria**:

  **QA Scenarios (MANDATORY — task is INCOMPLETE without these):**

  ```
  Scenario: Loading overlay appears during archive open
    Tool: Bash (dotnet build)
    Preconditions: Project builds cleanly
    Steps:
      1. dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected Result: Build succeeds with 0 errors, 0 warnings
    Evidence: .sisyphus/evidence/task-1-build.txt

  Scenario: UI smoke test — open archive shows FileListPanel
    Tool: Bash (dotnet build)
    Preconditions: Build passes
    Steps:
      1. Verify ArchiveLoadingOverlay is declared in MainWindow.xaml (grep for x:Name="ArchiveLoadingOverlay")
      2. Verify ArchiveLoadingOverlay.Visibility toggles exist in LoadArchiveAsync (3 locations: show at start, hide after filter, hide on error)
      3. Verify FileListPanel.Visibility and DropHint.Visibility are set correctly in both success and error paths
    Expected Result: All 3 visibility manipulation points exist in code-behind
    Evidence: .sisyphus/evidence/task-1-code-review.txt
  ```

  **Evidence to Capture:**
  - [ ] Build output
  - [ ] Code review confirming all 3 show/hide points

  **Commit**: YES
  - Message: `feat(ui): add loading overlay while opening large archives`
  - Files: `src/MantisZip.UI/MainWindow.xaml`, `src/MantisZip.UI/MainWindow.xaml.cs`
  - Pre-commit: `dotnet build src\MantisZip.UI\MantisZip.UI.csproj`

---

## Final Verification Wave

- [x] F1. **Plan Compliance Audit** — `oracle`
- [x] F2. **Code Quality Review** — `unspecified-high`
- [x] F3. **Real Manual QA** — `unspecified-high`
- [x] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff. Verify 1:1 — overlay added, show/hide logic matches plan. No changes to engine or Core. VERDICT

---

## Commit Strategy

- **1**: `feat(ui): add loading overlay while opening large archives` — MainWindow.xaml, MainWindow.xaml.cs, dotnet build

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
# Expected: Build succeeded. 0 warnings, 0 errors
```

### Final Checklist
- [x] ArchiveLoadingOverlay appears in InnerContentGrid
- [x] Loading overlay shown before ListEntriesAsync call
- [x] Loading overlay hidden after FilterFiles completes
- [x] Loading overlay hidden on error, DropHint restored
- [x] Build passes with 0 errors, 0 warnings
