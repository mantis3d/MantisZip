# About Window Redesign

## TL;DR

> **Quick Summary**: Replace the current primitive MessageBox-based "About" dialog with a proper WPF AboutWindow featuring 4 tabs (关于/作者/依赖库/致谢), following qBittorrent EE's dialog design.
>
> **Deliverables**:
> - New `AboutWindow.xaml` + `AboutWindow.xaml.cs` (TabControl with 4 tabs)
> - ~15 new localization keys (zh + en)
> - Modified `MainWindow.Menu.cs` (About_Click → opens AboutWindow)
> - Smoke test in existing test project
>
> **Estimated Effort**: Small
> **Parallel Execution**: YES — 2 waves
> **Critical Path**: Localization → AboutWindow → Menu integration → Test

---

## Context

### Original Request
User wants to enrich the current "About" dialog (currently just a primitive `AppMessageBox.Show()` with plain text) to resemble qBittorrent Enhanced Edition's About window — a structured TabControl dialog.

### Interview Summary
**Key Discussions**:
- Logo: Reuses `App.ico` (same as main window) — `docs/images/Logo.png` exists but user confirmed it's same icon
- Tab order: ①关于(About) → ②作者(Author) → ③依赖库(Dependencies) → ④致谢(Acknowledgments)
- Window: Resizable, ~480×480, CenterScreen, ShowInTaskbar=false
- Donation: NOT included (has separate DonationDialog)
- Developer info: MantisZen, micheal.liu@163.com, github.com/mantis3d, gitee.com/mantis3d

**Research Findings**:
- Existing dialog pattern: `DonationDialog` (non-resizable, SizeToContent, l:L XAML markup)
- Theme: `Theme_WindowBg`, `Theme_TextPrimary`, `Theme_TextSecondary`, `Theme_Border`, `Theme_Accent*`, `Theme_ButtonBg`
- Localization: `L.cs` auto-generated constants + `strings.*.json` files + `l:L` XAML markup extension
- Test project: `tests/MantisZip.Tests/` (xUnit, references Core only, NOT UI)

### Metis Review
**Identified Gaps** (addressed):
- **Test limitations**: Existing test project references only `MantisZip.Core`, not UI. Can't test AboutWindow directly. → Smoke test will test *constants/strings*, not UI instantiation.
- **Old key cleanup**: `Main_About_Text` and `Main_About_Title` become unused after change. → Keep old keys (no removal) to avoid breaking potential external references; mark as deprecated in summary only.
- **SettingsWindow about section**: Out of scope — left completely untouched.
- **Resizable vs non-resizable**: User chose resizable. → MinWidth=400, MinHeight=350 to prevent unusable tiny state.
- **Dependency data**: Hardcoded strings matching README (not runtime reflection).

**Auto-Resolved** (minor gaps fixed):
- Hyperlink pattern: Use `<Hyperlink>` in XAML with `RequestNavigate` event + `Process.Start(UseShellExecute=true)` (DonationDialog pattern)
- Dependency sort: Alphabetical by package name
- Email link: `mailto:` hyperlink

---

## Work Objectives

### Core Objective
Replace the MessageBox About dialog with a proper 4-tab AboutWindow.

### Concrete Deliverables
- `src/MantisZip.UI/AboutWindow.xaml` — XAML layout
- `src/MantisZip.UI/AboutWindow.xaml.cs` — code-behind
- Modified `src/MantisZip.UI/MainWindow.Menu.cs` — About_Click
- New keys in `strings.zh.json`, `strings.en.json`, `L.cs`
- New test in `tests/MantisZip.Tests/`

### Definition of Done
- [ ] `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` succeeds (no errors, no warnings)
- [ ] Menu 帮助 → 关于 opens AboutWindow instead of MessageBox
- [ ] All 4 tabs render correctly with content
- [ ] Hyperlinks (GitHub, Gitee, email) open in browser/mail client
- [ ] `dotnet test tests/MantisZip.Tests/` passes

### Must Have
- AboutWindow with 4 functional tabs
- Localized strings (zh + en)
- Clickable hyperlinks for GitHub/Gitee/email
- Correct version display from `AppConstants.Version`
- Tab navigation works (click, Ctrl+Tab, keyboard)

### Must NOT Have (Guardrails)
- NO donation/sponsor content (separate dialog exists)
- NO SettingsWindow modifications (out of scope)
- NO UI project reference added to test project
- NO custom TabControl templates/styles
- NO animations, transitions, or custom window chrome
- NO `Main_About_Text` key removal (kept, no longer referenced)
- NO new external dependencies/NuGet packages

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (xUnit in `tests/MantisZip.Tests/`)
- **Automated tests**: Tests-after (smoke test for constants only)
- **Framework**: xUnit
- **Note**: AboutWindow is a WPF window — cannot be instantiated in headless xUnit. Smoke test covers constants/strings/version.

### QA Policy
Every task MUST include agent-executed QA scenarios.

- **XAML/CS files**: Use `dotnet build` + grep for structural verification
- **Localization**: Use grep/read to verify keys in JSON + L.cs
- **No UI automation**: Can't open WPF window headlessly — verification via code review + build

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Start Immediately — foundation):
├── Task 1: Localization keys (zh + en + L.cs) [quick]
└── Task 2: AboutWindow.xaml + .xaml.cs [quick]

Wave 2 (After Wave 1 — integration + test):
├── Task 3: MainWindow.Menu.cs — About_Click [quick]
├── Task 4: Smoke test for constants [quick]
└── Task 5: Build verification + cleanup check [quick]
```

### Dependency Matrix
- **1**: — 2, 3, 4
- **2**: — 3
- **3**: 1, 2 — 5
- **4**: 1 — 5
- **5**: 3, 4 —

---

## TODOs

- [x] 1. Add About_* localization keys

  **What to do**:
  - Add ~15 new keys to `strings.zh.json` (Chinese) and `strings.en.json` (English)
  - Add corresponding `public const string` entries in `L.cs` (alphabetical position)
  - Keys needed:

    | Key | Purpose |
    |-----|---------|
    | `About_Title` | Window title bar |
    | `About_Tab_About` | Tab 1 header |
    | `About_Tab_Author` | Tab 2 header |
    | `About_Tab_Dependencies` | Tab 3 header |
    | `About_Tab_Acknowledgments` | Tab 4 header |
    | `About_Version` | "Version: {0}" label |
    | `About_Description` | ".NET 9 + WPF · SharpCompress + SharpSevenZip" |
    | `About_Formats` | "Supported formats: ZIP, 7z, RAR, TAR, GZ, ISO" |
    | `About_License` | "MIT License" |
    | `About_GitHub` | "GitHub" link text |
    | `About_Author_Name` | "MantisZen" |
    | `About_Author_Email` | Email label |
    | `About_Author_GitHub` | "GitHub: github.com/mantis3d" |
    | `About_Author_Gitee` | "Gitee: gitee.com/mantis3d" |
    | `About_Library_Name` | "Library" column header |
    | `About_Library_Version` | "Version" |
    | `About_Library_License` | "License" |
    | `About_Library_Purpose` | "Purpose" |
    | `About_Thanks_OSS` | "Thanks to all open source projects..." |
    | `About_Thanks_AI` | "Developed with OpenCode + Sisyphus Agent" |
    | `About_Thanks_7Zip` | "7-Zip (GNU LGPL)" |

  **Must NOT do**:
  - Do NOT remove existing `Main_About_Text`/`Main_About_Title` keys (keep them)
  - Do NOT touch `Settings_Advanced_About*` keys (used in SettingsWindow)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Mechanical translation task — add strings to JSON files + one constant per string in L.cs
  - **Skills**: `[]`
  - **Skills Evaluated but Omitted**:
    - No skills needed for simple text/key additions

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Task 2)
  - **Blocks**: Tasks 3, 4
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI/Resources/strings.zh.json` — Follow existing key naming convention (`About_` prefix)
  - `src/MantisZip.UI/Resources/strings.en.json` — Same keys, English values
  - `src/MantisZip.UI/Localization/L.cs` — Add `public const string About_*` entries following the existing pattern
  - Existing pattern: `Settings_Advanced_About*` keys (lines 478-481 in strings.zh.json, 466-469 in strings.en.json)
  - `src/MantisZip.UI/Localization/L.cs:169-170` — Existing `Main_About_Text`/`Main_About_Title` constants for reference

  **Acceptance Criteria**:
  - [ ] All new keys exist in `strings.zh.json` (grep `About_` returns exactly N keys)
  - [ ] All keys in zh JSON also exist in en JSON
  - [ ] All new keys have corresponding `public const string` in `L.cs`
  - [ ] `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` succeeds

  **QA Scenarios**:

  ```
  Scenario: Verify all About_* keys in zh.json
    Tool: Bash
    Preconditions: strings.zh.json exists at expected path
    Steps:
      1. grep -c '"About_' src/MantisZip.UI/Resources/strings.zh.json
    Expected Result: Returns count matching the new keys (e.g., 21)
    Evidence: .sisyphus/evidence/task-1-zh-keys.txt

  Scenario: Verify all keys in zh.json also in en.json
    Tool: Bash
    Preconditions: Both JSON files exist
    Steps:
      1. grep -oP '"About_[^"]+' src/MantisZip.UI/Resources/strings.zh.json | sort > zh.txt
      2. grep -oP '"About_[^"]+' src/MantisZip.UI/Resources/strings.en.json | sort > en.txt
      3. diff zh.txt en.txt
    Expected Result: No differences (identical key sets)
    Evidence: .sisyphus/evidence/task-1-keys-match.txt

  Scenario: Verify L.cs constants exist
    Tool: Bash
    Preconditions: L.cs exists
    Steps:
      1. For each About_ key, grep for it in L.cs: grep "About_" src/MantisZip.UI/Localization/L.cs
    Expected Result: All keys found as public const string
    Evidence: .sisyphus/evidence/task-1-lcs-constants.txt
  ```

  **Evidence to Capture**:
  - [ ] .sisyphus/evidence/task-1-zh-keys.txt
  - [ ] .sisyphus/evidence/task-1-keys-match.txt
  - [ ] .sisyphus/evidence/task-1-lcs-constants.txt

  **Commit**: YES
  - Message: `i18n(about): add About_* localization keys for new AboutWindow`
  - Files: `strings.zh.json`, `strings.en.json`, `L.cs`

---

- [x] 2. Create AboutWindow.xaml and AboutWindow.xaml.cs

  **What to do**:
  - Create `src/MantisZip.UI/AboutWindow.xaml`:
    - Window: Title=`{l:L About_Title}`, Icon=`/Resources/App.ico`, MinWidth=400, MinHeight=350, Width=480, Height=460
    - WindowStartupLocation=`CenterOwner`, ShowInTaskbar=`False`, ResizeMode=`CanResize`
    - Background=`{StaticResource Theme_WindowBg}`
    - **Header area** (above TabControl, not scrollable):
      - Grid/StackPanel with App.ico (32×32 or 48×48) + app name "MantisZip" + version string
      - Separator line
    - **TabControl** with TabStripPlacement="Top", 4 TabItems:
      - Tab 1 (关于): Large App.ico + app name + version + description + formats + license + GitHub hyperlink, wrapped in ScrollViewer
      - Tab 2 (作者): Developer info — Name, Email (mailto:), GitHub link, Gitee link, via Hyperlink elements
      - Tab 3 (依赖库): Scrollable list/table of dependencies (TextBlock/ItemsControl with Name, Version, License, Purpose columns)
      - Tab 4 (致谢): TextBlock with thanks text, including 7-Zip and OpenCode/Sisyphus Agent mention
    - **Bottom area** (below TabControl): [确定] button, right-aligned
    - Use `l:L` XAML markup extension for ALL user-visible strings

  - Create `src/MantisZip.UI/AboutWindow.xaml.cs`:
    - Constructor: `InitializeComponent()`
    - `VersionText.Text = "v" + AppConstants.Version;` (in About tab)
    - Hyperlink `RequestNavigate` event handlers → `Process.Start(UseShellExecute=true)`
    - Close button click → `DialogResult = true`

  **Dependencies tab layout**:
  - Use a simple `<ItemsControl>` or `<StackPanel>` with hardcoded dependency rows
  - Each row: Library name (bold) | Version | License | Purpose
  - Data from README.md "第三方依赖" section:
    - SharpCompress 0.48.1 MIT — ZIP/TAR/GZ engine
    - SharpSevenZip 2.0.45 LGPL-2.1 — 7z/RAR engine
    - SharpZipLib 1.4.2 MIT — Legacy (minor compat)
    - CommunityToolkit.Mvvm 8.4.2 MIT — MVVM helpers
    - Markdig 1.2.0 BSD-2-Clause — Markdown rendering
    - Ookii.Dialogs.Wpf 5.0.1 BSD-3-Clause — Folder picker
    - Ude.NetStandard 1.2.0 MIT — Encoding detection
    - WpfAnimatedGif 2.0.2 MIT — GIF animation
    - Microsoft.Web.WebView2 1.0.3967.48 BSD-3-Clause — HTML/PDF preview
    - 7z.dll — LGPL — 7z native engine

  **Must NOT do**:
  - Do NOT use custom TabControl templates (default WPF style)
  - Do NOT add any animations or transitions
  - Do NOT reference `docs/images/Logo.png` (use `App.ico` instead)

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
    - Reason: WPF/XAML UI construction with TabControl, Hyperlinks, ScrollViewer, theme resource binding
  - **Skills**: `[]`
  - **Skills Evaluated but Omitted**:
    - No specialized skills needed for standard WPF layout

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Task 1)
  - **Blocks**: Task 3
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI/DonationDialog.xaml` — Existing dialog pattern (Window structure, theme resources, l:L)
  - `src/MantisZip.UI/DonationDialog.xaml.cs` — Code-behind pattern (Process.Start for URL, Close_Click)
  - `src/MantisZip.UI/SettingsWindow.xaml:17-49` — TabControl with TabItem styling pattern (use as guide, but with TabStripPlacement="Top")
  - `src/MantisZip.UI/MainWindow.xaml:1-18` — Window structure pattern (Icon, Background, theme)
  - `src/MantisZip.UI/AppConstants.cs:11` — `AppConstants.Version` for version display
  - `README.md` — Dependencies list for Tab 3 content
  - `AGENTS.md` — Credits info for Tab 4

  **Acceptance Criteria**:
  - [ ] `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` succeeds
  - [ ] File `src/MantisZip.UI/AboutWindow.xaml` exists
  - [ ] File `src/MantisZip.UI/AboutWindow.xaml.cs` exists
  - [ ] Window uses `l:L` markup for all user-visible strings
  - [ ] Window uses `Theme_*` static resources for background/text/foreground
  - [ ] Hyperlink RequestNavigate handlers use Process.Start(UseShellExecute=true)

  **QA Scenarios**:

  ```
  Scenario: Verify AboutWindow builds without errors
    Tool: Bash
    Preconditions: AboutWindow.xaml and .xaml.cs exist
    Steps:
      1. dotnet build src\MantisZip.UI\MantisZip.UI.csproj 2>&1
    Expected Result: Build succeeds (exit code 0, no errors)
    Evidence: .sisyphus/evidence/task-2-build.txt

  Scenario: Verify all l:L keys in XAML correspond to L.cs constants
    Tool: Bash
    Preconditions: AboutWindow.xaml and L.cs exist
    Steps:
      1. grep -oP '\{l:L About_\w+' src/MantisZip.UI/AboutWindow.xaml | sed 's/{l:L //' | sort > xaml_keys.txt
      2. grep -oP 'About_\w+' xaml_keys.txt | while read k; do grep -q "About_$k" L.cs || echo "MISSING: $k"; done
    Expected Result: No "MISSING" lines (all XAML-referenced keys exist in L.cs)
    Evidence: .sisyphus/evidence/task-2-xaml-keys.txt

  Scenario: Verify Hyperlink RequestNavigate wired up
    Tool: Bash
    Preconditions: AboutWindow.xaml.cs exists
    Steps:
      1. grep "RequestNavigate" src/MantisZip.UI/AboutWindow.xaml
      2. grep "Process.Start" src/MantisZip.UI/AboutWindow.xaml.cs
    Expected Result: Both found — XAML has RequestNavigate events, CS has Process.Start handlers
    Evidence: .sisyphus/evidence/task-2-hyperlinks.txt
  ```

  **Evidence to Capture**:
  - [ ] .sisyphus/evidence/task-2-build.txt
  - [ ] .sisyphus/evidence/task-2-xaml-keys.txt
  - [ ] .sisyphus/evidence/task-2-hyperlinks.txt

  **Commit**: YES (groups with Task 1)
  - Message: `feat(ui): add AboutWindow with 4 tabs (About/Author/Dependencies/Acknowledgments)`
  - Files: `AboutWindow.xaml`, `AboutWindow.xaml.cs`

---

- [x] 3. Update MainWindow.Menu.cs — About_Click handler

  **What to do**:
  - Modify `About_Click` method in `src/MantisZip.UI/MainWindow.Menu.cs`:
    - Replace current `AppMessageBox.Show(...)` with:
      ```csharp
      new AboutWindow { Owner = this }.ShowDialog();
      ```
  - The old `Main_About_Text` key becomes unreferenced — this is expected (keys kept in JSON for compatibility)

  **Must NOT do**:
  - Do NOT change any other method or menu item
  - Do NOT add any other handlers
  - Do NOT remove old `Main_About_Text` or `Main_About_Title` keys

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Single-line change, trivial
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (with Tasks 4, 5)
  - **Blocks**: Task 5
  - **Blocked By**: Tasks 1, 2

  **References**:
  - `src/MantisZip.UI/MainWindow.Menu.cs:178-182` — Current `About_Click` method body to replace
  - `src/MantisZip.UI/MainWindow.Menu.cs:171-176` — `Donate_Click` pattern (shows dialog with Owner pattern): `new DonationDialog { Owner = this }.ShowDialog();`

  **Acceptance Criteria**:
  - [ ] grep for `new AboutWindow` in `MainWindow.Menu.cs` returns 1 match
  - [ ] grep for `AppMessageBox.Show` with `Main_About_Text` in `MainWindow.Menu.cs` returns 0 matches
  - [ ] `dotnet build` succeeds

  **QA Scenarios**:

  ```
  Scenario: Verify About_Click no longer uses AppMessageBox
    Tool: Bash
    Preconditions: MainWindow.Menu.cs exists
    Steps:
      1. grep -n "About_Click" src/MantisZip.UI/MainWindow.Menu.cs
      2. grep -A2 "About_Click" src/MantisZip.UI/MainWindow.Menu.cs | grep "AppMessageBox"
    Expected Result: Step 2 returns no matches (AppMessageBox removed from About_Click)
    Evidence: .sisyphus/evidence/task-3-about-click.txt

  Scenario: Verify AboutWindow is instantiated
    Tool: Bash
    Preconditions: MainWindow.Menu.cs exists
    Steps:
      1. grep "new AboutWindow" src/MantisZip.UI/MainWindow.Menu.cs
    Expected Result: Match found with `{ Owner = this }.ShowDialog()`
    Evidence: .sisyphus/evidence/task-3-new-aboutwindow.txt
  ```

  **Evidence to Capture**:
  - [ ] .sisyphus/evidence/task-3-about-click.txt
  - [ ] .sisyphus/evidence/task-3-new-aboutwindow.txt

  **Commit**: YES (groups with Tasks 1, 2)
  - Message: `feat(ui): wire AboutWindow into menu About_Click`
  - Files: `MainWindow.Menu.cs`

---

- [x] 4. Add smoke test for About window constants

  **What to do**:
  - Add a new test file `tests/MantisZip.Tests/AboutWindowTests.cs`
  - Test content:
    - `AppConstants.Version` is not null/empty (verifies version display source)
    - All `About_*` keys exist in `L.cs` (using reflection or direct constant access)
    - Verify `Main_About_Text` still exists (keeping backward compat)
  - Note: Cannot test AboutWindow UI instantiation — test project references Core only, not UI

  **Must NOT do**:
  - Do NOT add project reference from test project to MantisZip.UI
  - Do NOT try to instantiate AboutWindow in test (requires WPF STA)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Simple xUnit test with string/constant assertions
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES (partially)
  - **Parallel Group**: Wave 2 (with Task 3)
  - **Blocks**: Task 5
  - **Blocked By**: Task 1

  **References**:
  - `tests/MantisZip.Tests/` — Existing test project structure
  - `tests/MantisZip.Tests/ProgressWindowBatchLogicTests.cs` — Example test file for pattern reference
  - `src/MantisZip.UI/AppConstants.cs` — Version constant to test
  - `src/MantisZip.UI/Localization/L.cs:169-170` — Existing constants to verify

  **Acceptance Criteria**:
  - [ ] `dotnet test tests/MantisZip.Tests/` passes (all existing + new tests)
  - [ ] Test file `tests/MantisZip.Tests/AboutWindowTests.cs` exists
  - [ ] Tests verify `AppConstants.Version` is non-empty
  - [ ] Tests verify L.cs About_ constants exist

  **QA Scenarios**:

  ```
  Scenario: Verify smoke test file exists
    Tool: Bash
    Preconditions: None
    Steps:
      1. Test-Path tests/MantisZip.Tests/AboutWindowTests.cs
    Expected Result: True (file exists)
    Evidence: .sisyphus/evidence/task-4-test-exists.txt

  Scenario: Run tests
    Tool: Bash
    Preconditions: Test file exists
    Steps:
      1. dotnet test tests/MantisZip.Tests/ 2>&1
    Expected Result: All tests pass (exit code 0)
    Evidence: .sisyphus/evidence/task-4-test-pass.txt
  ```

  **Evidence to Capture**:
  - [ ] .sisyphus/evidence/task-4-test-exists.txt
  - [ ] .sisyphus/evidence/task-4-test-pass.txt

  **Commit**: YES
  - Message: `test(about): add smoke tests for About window constants`
  - Files: `AboutWindowTests.cs`

---

- [x] 5. Full build verification + dead key audit

  **What to do**:
  - Run full build: `dotnet build src\MantisZip.UI\MantisZip.UI.csproj`
  - Run all tests: `dotnet test tests/MantisZip.Tests/`
  - Audit dead code: Verify `Main_About_Text` is no longer used in any `.cs` file
  - Verify no other dead or orphaned references

  **Must NOT do**:
  - Do NOT delete `Main_About_Text`/`Main_About_Title` keys from JSON files

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Build verification + grep audit
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (sequential, after 3, 4)
  - **Blocks**: None
  - **Blocked By**: Tasks 3, 4

  **References**:
  - Previous task outputs for build and test results

  **Acceptance Criteria**:
  - [ ] `dotnet build` succeeds with exit code 0
  - [ ] `dotnet test` succeeds with exit code 0
  - [ ] `Main_About_Text` is referenced in 0 `.cs` files (only in JSON + L.cs)
  - [ ] No compilation warnings added

  **QA Scenarios**:

  ```
  Scenario: Full build
    Tool: Bash
    Preconditions: All tasks done
    Steps:
      1. dotnet build src\MantisZip.UI\MantisZip.UI.csproj 2>&1 | tail -20
    Expected Result: Build succeeded (exit code 0, no errors)
    Evidence: .sisyphus/evidence/task-5-build.txt

  Scenario: Full tests
    Tool: Bash
    Preconditions: All tasks done
    Steps:
      1. dotnet test tests/MantisZip.Tests/ 2>&1 | tail -20
    Expected Result: All tests passed (exit code 0)
    Evidence: .sisyphus/evidence/task-5-tests.txt

  Scenario: Dead key audit
    Tool: Bash
    Preconditions: All tasks done
    Steps:
      1. grep -r "Main_About_Text" src/MantisZip.UI/ --include="*.cs"
    Expected Result: No matches (key no longer referenced in code)
    Evidence: .sisyphus/evidence/task-5-dead-key-audit.txt
  ```

  **Evidence to Capture**:
  - [ ] .sisyphus/evidence/task-5-build.txt
  - [ ] .sisyphus/evidence/task-5-tests.txt
  - [ ] .sisyphus/evidence/task-5-dead-key-audit.txt

  **Commit**: NO (verification only)

---

## Final Verification Wave

- [x] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. Verify: all 5 tasks completed, all Must Have present, all Must NOT Have absent. Check evidence files exist in `.sisyphus/evidence/`. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [x] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build` + `dotnet test`. Review all changed files for: empty catches, console.log in prod, commented-out code, unused imports. Check AI slop: excessive comments, over-abstraction, generic names.
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | Files [N clean/N issues] | VERDICT`

- [x] F3. **Real Manual QA** — `unspecified-high`
  From clean state, verify build succeeds. Verify `About_*` keys in both JSON files match. Verify About_Click creates AboutWindow (grep code). Verify AppConstants.Version is non-empty.
  Output: `Scenarios [N/N pass] | VERDICT`

- [x] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff (git log/diff). Verify 1:1 — everything built, nothing extra. Check "Must NOT do" compliance. Detect cross-task contamination.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | VERDICT`

---

## Commit Strategy

- **Task 1**: `i18n(about): add About_* localization keys for new AboutWindow`
- **Tasks 2+3**: `feat(ui): add AboutWindow with 4 tabs and wire into menu`
- **Task 4**: `test(about): add smoke tests for About window constants`
- **Task 5**: (verification only, no commit)

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.UI\MantisZip.UI.csproj   # Expected: Build succeeded
dotnet test tests/MantisZip.Tests/                    # Expected: All tests passed
grep -r "Main_About_Text" src/ --include="*.cs"      # Expected: no matches (dead key)
```

### Final Checklist
- [x] All "Must Have" present (4-tab AboutWindow, localization, hyperlinks, version)
- [x] All "Must NOT Have" absent (no donation, no SettingsWindow changes, no UI ref in tests)
- [x] All tests pass
