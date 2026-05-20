# 智能解压到此处 (Smart Extract Here)

## TL;DR

> **Quick Summary**: Add a "Smart Extract Here" context menu option + toolbar button that intelligently decides whether to extract directly or create an archive-named folder, based on whether the archive has a single root directory.
>
> **Deliverables**:
> - New context menu verb: "智能解压到此处" (between current #2 and #3)
> - New CLI entry: `--extract-smart <path>`
> - New toolbar button in MainWindow
> - AppSettings toggle (default: enabled)
> - Integration tests with real archives
>
> **Estimated Effort**: Medium
> **Parallel Execution**: YES - 2 waves
> **Critical Path**: Localization → Core logic → Shell/CLI/UI (parallel)

---

## Context

### Original Request
Add a "Smart Extract" context menu option that automatically determines whether to extract files directly into the current folder (if the archive has a single root directory) or create a new folder named after the archive (if files are scattered at the top level).

### Interview Summary
**Key Decisions**:
- **Position**: New independent menu item between "Extract Here" (#2) and "Extract to (archive name)" (#3)
- **Display name**: "智能解压到此处"
- **Settings**: Add `EnableSmartExtractMenu` toggle, default enabled
- **Toolbar**: Add button in MainWindow toolbar (visible when archive loaded)
- **CLI**: New entry `--extract-smart <path>`
- **Tests**: Integration tests with real archive fixtures (xUnit)

### Metis Review
(No gaps identified — Metis auto-approved)

---

## Work Objectives

### Core Objective
Add a Smart Extract feature with context menu, CLI, toolbar, and settings support.

### Concrete Deliverables
- `--extract-smart` CLI handler in `App.xaml.cs`
- Smart extract logic (analyze archive structure, decide destination)
- Context menu verb registration in `ShellIntegration.cs`
- Toolbar button in `MainWindow.xaml` + handler
- Settings checkbox in `SettingsWindow`
- Localized strings (zh/en)
- Integration tests

### Must Have
- Smart logic: single root → extract here; multiple roots → archive-named folder
- Works with all supported formats (zip, 7z, tar.gz, rar)
- Encrypted archives prompt for password before analysis
- Progress window shown during extraction (reuse existing)

### Must NOT Have
- No change to existing context menu behavior (Extract Here and Extract to Named remain unchanged)
- No MVVM refactoring
- No changes to core engine interfaces

---

## Verification Strategy

> **首要原则：自动化验证优先** — 尽可能 agent-executed；个别 WPF UI 场景需人工辅助（标注为 manual smoke test）。

### Test Decision
- **Infrastructure exists**: YES (xUnit + test project)
- **Automated tests**: Integration tests with real archives
- **Framework**: xUnit
- **CI**: None (pre-existing)

### QA Policy
Each task includes agent-executed QA scenarios:
- CLI: Run `--extract-smart`, verify output directory structure
- UI: Verify toolbar button visibility, click triggers correct action
- Shell: Verify registry entries created correctly

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Foundation — can start in parallel):
├── Task 1: Add Shell_SmartExtract localization strings (zh/en + L.cs)
├── Task 2: Add EnableSmartExtractMenu to AppSettings
└── Task 3: Add SmartExtract logic helper to Core

Wave 2 (Integration — depends on Wave 1, can run in parallel):
├── Task 4: Add --extract-smart CLI handler to App.xaml.cs
├── Task 5: Add smart extract verb to ShellIntegration
├── Task 6: Add toolbar button to MainWindow
├── Task 7: Add settings checkbox to SettingsWindow
└── Task 8: Integration tests

Critical Path: Task 1 → Task 4 → Task 5 (Shell needs localization)
                → Task 6 (needs logic from Task 3)
                → Task 7 (needs Task 2)
```

---

## TODOs

- [ ] 1. Add localization strings

  **What to do**:
   - Add key `Shell_SmartExtract` with value `"智能解压到此处"` to `Resources/strings.zh.json`
   - Add key `Shell_SmartExtract` with value `"Smart Extract Here"` to `Resources/strings.en.json`
   - Add key `Main_Tooltip_SmartExtract` with value `"智能解压到此处"` to `Resources/strings.zh.json` (for toolbar button tooltip)
   - Add key `Main_Tooltip_SmartExtract` with value `"Smart Extract Here"` to `Resources/strings.en.json`
   - Add constants to `Localization/L.cs` (in alpha order):
     - `public const string Shell_SmartExtract = "Shell_SmartExtract";` after `Shell_QuickCompress`
     - `public const string Main_Tooltip_SmartExtract = "Main_Tooltip_SmartExtract";` after `Main_Tooltip_Preview`

  **Must NOT do**:
   - Don't regenerate L.cs from scratch — just add the two constants manually

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1
  - **Blocks**: Tasks 4, 5, 6, 7
  - **Blocked By**: None

  **References**:
   - `src/MantisZip.UI/Resources/strings.zh.json:418` — Pattern for Shell_QuickCompress entry
   - `src/MantisZip.UI/Resources/strings.en.json:418` — Same in English
   - `src/MantisZip.UI/Localization/L.cs:385-390` — Pattern for Main_Tooltip_* constants and their alpha position

   **QA Scenarios**:
   ```
   Scenario: All localization keys resolve correctly
     Tool: Bash (grep)
     Preconditions: Files exist with correct encoding
     Steps:
       1. grep 'Shell_SmartExtract' strings.zh.json → verify has Chinese text
       2. grep 'Shell_SmartExtract' strings.en.json → verify has English text
       3. grep 'Shell_SmartExtract' L.cs → verify constant exists
       4. grep 'Main_Tooltip_SmartExtract' strings.zh.json → verify has Chinese text
       5. grep 'Main_Tooltip_SmartExtract' strings.en.json → verify has English text
       6. grep 'Main_Tooltip_SmartExtract' L.cs → verify constant exists
     Expected Result: All 6 items found
     Evidence: .sisyphus/evidence/task-1-localization.txt
   ```

   **Commit**: YES
   - Message: `i18n: add Shell_SmartExtract + Main_Tooltip_SmartExtract keys`
   - Files: `src/MantisZip.UI/Resources/strings.zh.json`, `src/MantisZip.UI/Resources/strings.en.json`, `src/MantisZip.UI/Localization/L.cs`

---

- [ ] 2. Add EnableSmartExtractMenu to AppSettings

  **What to do**:
  - Add property `public bool EnableSmartExtractMenu { get; set; } = true;` to `AppSettings` class, after the existing `EnableCascadingMenu` / `ShowMenuIcons` properties

  **Must NOT do**:
  - Don't change any existing settings

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1
  - **Blocks**: Task 7
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.UI/AppSettings.cs:27` — Pattern: `EnableCascadingMenu` + `ShowMenuIcons`

  **QA Scenarios**:
  ```
  Scenario: Property exists and defaults to true
    Tool: Bash (grep + dotnet build)
    Preconditions: None
    Steps:
      1. grep 'EnableSmartExtractMenu' AppSettings.cs → verify property exists
      2. dotnet build → verify 0 errors
    Expected Result: Property found, build clean
    Evidence: .sisyphus/evidence/task-2-appsettings.txt
  ```

  **Commit**: YES (groups with Task 7)
  - Message: `feat(settings): add EnableSmartExtractMenu toggle`
  - Files: `src/MantisZip.UI/AppSettings.cs`

---

- [ ] 3. Add SmartExtract logic helper

  **What to do**:
  - Add a new internal static method `ArchiveStructureAnalyzer` class to `MantisZip.Core` (new file `Core/Utils/ArchiveStructureAnalyzer.cs`)
  - Method: `public static bool HasSingleRootDirectory(IReadOnlyList<ArchiveItem> items)`
    - Extract top-level paths: for each item's `FullPath`, get first segment before `/`
    - If all items share one distinct root → return `true`
    - If items have 0 distinct roots (empty) → return `true` (empty archive → extract here)
    - If items have 2+ distinct roots → return `false`
    - Ignore directory entries for the root check (files imply the structure)
    - Edge case: archive with only directories → return `true` (directories at root level are a single common ancestor)

  **Must NOT do**:
  - Don't modify any existing engine or interface
  - Don't add dependencies

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1
  - **Blocks**: Tasks 4, 6, 8
  - **Blocked By**: None

  **References**:
  - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs:6-18` — ArchiveItem structure (FullPath format)
  - `tests/MantisZip.Tests/Engines/ZipEngineTests.cs` — Test patterns

  **Acceptance Criteria**:
  - [ ] New file: `Core/Utils/ArchiveStructureAnalyzer.cs`

  **QA Scenarios**:
  ```
  Scenario: Single root directory returns true
    Tool: Bash (dotnet test)
    Preconditions: ArchiveStructureAnalyzer implemented
    Steps:
      1. Create test with items: ["dir/file1.txt", "dir/sub/file2.txt"] → HasSingleRootDirectory returns true
      2. dotnet test
    Expected Result: Test passes
    Evidence: .sisyphus/evidence/task-3-logic.txt

  Scenario: Multiple root entries returns false
    Tool: Bash (dotnet test)
    Preconditions: Same
    Steps:
      1. Create test with items: ["file1.txt", "dir/file2.txt"] → HasSingleRootDirectory returns false
      2. dotnet test
    Expected Result: Test passes
    Evidence: .sisyphus/evidence/task-3-logic-2.txt

  Scenario: Directories-only archive returns true
    Tool: Bash (dotnet test)
    Preconditions: Same
    Steps:
      1. Create test with items: ["dir1/sub/", "dir2/"] → HasSingleRootDirectory returns true
      2. dotnet test
    Expected Result: Test passes
    Evidence: .sisyphus/evidence/task-3-logic-3.txt
  ```

  **Commit**: YES
  - Message: `feat(core): add ArchiveStructureAnalyzer for smart extract logic`
  - Files: `src/MantisZip.Core/Utils/ArchiveStructureAnalyzer.cs`

---

- [ ] 4. Add --extract-smart CLI handler to App.xaml.cs

  **What to do**:
  - In `App.OnStartup`, add `case "--extract-smart": HandleExtractSmart(e.Args[1]); return;` after the existing `--extract-to-name` case
  - Add new method `HandleExtractSmart(string? archivePath)`:
    - Validate archive exists
    - Get engine via `ArchiveEngineFactory.GetEngineByExtension`
    - **List entries with password-aware fallback**:
      - Try `engine.ListEntriesAsync(archivePath, password: null, ...)` first
      - If it throws a crypto/access exception → the archive likely requires a password before entry listing (known issue: SharpZipLib on certain encrypted ZIPs). Capture the exception, then:
        1. Try saved passwords via `QuickVerifyPassword` to find the correct one
        2. Re-try `ListEntriesAsync` with the found password
        3. If no saved password works → prompt user for password before proceeding
        4. If user cancels → exit gracefully
    - If items list is empty after listing → show "压缩包为空" and exit (no extract needed)
    - Call `ArchiveStructureAnalyzer.HasSingleRootDirectory(items)`
    - If true → dest = parent dir (like `HandleExtractHere`)
    - If false → dest = `parentDir/archiveName/` (like `HandleExtractToNamed`)
    - Show ProgressWindow, call `engine.ExtractAsync`, exit when done
    - Handle password (post-listing): if archive has encrypted entries, try saved passwords first, prompt on failure
    - Reuse existing `RunExtractStatic` pattern but with smart destination logic

  **Must NOT do**:
  - Don't duplicate extract progress logic — reuse existing patterns
  - Don't add new dependencies

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Task 1, 3)
  - **Blocks**: None
  - **Blocked By**: Task 1, Task 3

  **References**:
  - `src/MantisZip.UI/App.xaml.cs:324-361` — HandleExtractHere and HandleExtractToNamed for destination logic
  - `src/MantisZip.UI/App.xaml.cs:498-521` — RunExtractStatic for extract flow
  - `src/MantisZip.Core/Utils/ArchiveStructureAnalyzer.cs` — Smart extract decision logic

  **QA Scenarios**:
  ```
  Scenario: --extract-smart with single-root archive extracts to parent dir
    Tool: Bash (tmux)
    Preconditions: Test archive with single root dir exists
    Steps:
      1. Create temp dir with test archive
      2. Run: MantisZip.UI.exe --extract-smart <test_archive.zip>
      3. Check parent dir contents
    Expected Result: Files extracted directly to parent dir
    Evidence: .sisyphus/evidence/task-4-cli-smart.txt

  Scenario: --extract-smart with multi-root archive creates folder
    Tool: Bash (tmux)
    Preconditions: Test archive with scattered files exists
    Steps:
      1. Create temp dir with test archive
      2. Run: MantisZip.UI.exe --extract-smart <test_archive.zip>
      3. Check parent dir for archive-named folder
    Expected Result: Archive-named folder created, files inside
    Evidence: .sisyphus/evidence/task-4-cli-smart-2.txt
  ```

  **Commit**: YES (groups with Task 6)
  - Message: `feat(cli): add --extract-smart command handler`
  - Files: `src/MantisZip.UI/App.xaml.cs`

---

- [ ] 5. Add smart extract verb to ShellIntegration

  **What to do**:
  - Add new verb constants:
    - `ExtractSmartVerb = "02_MantisZipSmartExtract"` (sorts between `02_MantisZipExtractHere` and `03_MantisZipExtractToNamed`)
    - `ExtractSmartDisplay = L.T(L.Shell_SmartExtract)`
  - In `InstallCascadeFor`:
    - Add smart extract subcommand between extracthere and extracttonamed, guarded by `s.EnableSmartExtractMenu` and `s.EnableExtractMenu`
    - Apply `AppliesTo` filter (archive extensions only)
    - Command: `--extract-smart "%1"` or `--extract-smart "%V"`
    - ⚠ **Cascade subcommand renumbering**: Inserting a new subcommand shifts `03_extracttonamed`→`04_`, `04_extract`→`05_`, `05_quick`→`06_`, `06_compress`→`07_`. This changes registry subcommand keys on existing installs. On upgrade, old keys remain as garbage (not harmful — `Uninstall` deletes the cascade root entry before reinstall). Acceptable risk.
  - In `InstallVerbs`:
    - Add smart extract verb registration for `*` only (must receive an archive file path, not a folder — same as other extract verbs)
  - In `Uninstall`:
    - Add `DeleteRegistryKey` for `ExtractSmartVerb` + old version cleanup
  - `IsInstalled`: Add check for `02_MantisZipSmartExtract` verb key (same pattern as existing `CompressVerb` fallback check). Smart extract is an additional verb alongside existing ones — `IsInstalled` should return `true` if any MantisZip verb key exists, not only when smart extract is present.

  **Must NOT do**:
  - Don't renumber existing verb constants (the `02_` prefix sorts correctly between existing `02_MantisZipExtractHere` and `03_MantisZipExtractToNamed`)
  - Don't modify verb constants for existing entries

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Task 1)
  - **Blocks**: None
  - **Blocked By**: Task 1

  **References**:
  - `src/MantisZip.UI/ShellIntegration.cs:155-170` — Pattern: ExtractHere verb registration
  - `src/MantisZip.UI/ShellIntegration.cs:38-43` — Display name constants
  - `src/MantisZip.UI/ShellIntegration.cs:81-117` — Uninstall cleanup pattern

  **QA Scenarios**:
  ```
  Scenario: Registry verb created correctly
    Tool: Bash (reg query)
    Preconditions: Shell installed
    Steps:
      1. Build and run --install-shell
      2. reg query HKCU\Software\Classes\*\shell\02_MantisZipSmartExtract
    Expected Result: Key exists with correct command
    Evidence: .sisyphus/evidence/task-5-shell-reg.txt

  Scenario: Uninstall removes smart extract verb
    Tool: Bash (reg query)
    Preconditions: After uninstall
    Steps:
      1. Run --uninstall-shell
      2. reg query HKCU\Software\Classes\*\shell\02_MantisZipSmartExtract (should fail)
    Expected Result: Key not found
    Evidence: .sisyphus/evidence/task-5-shell-uninstall.txt
  ```

  **Commit**: YES
  - Message: `feat(shell): add smart extract context menu verb`
  - Files: `src/MantisZip.UI/ShellIntegration.cs`

---

- [ ] 6. Add smart extract toolbar button to MainWindow

  **What to do**:
  - In `MainWindow.xaml`, add a new button to the toolbar (use `x:Name="SmartExtractBtn"`):
    - Content: bound to `Shell_SmartExtract` localization
    - ToolTip: `Main_Tooltip_SmartExtract` localization (use separate tooltip key per existing convention)
    - Archive-dependency: use `IsEnabled="False"` + code-behind enable when `_currentArchivePath != null` (reuse `AddFilesBtn`/`DeleteFilesBtn` pattern — NOT `Visibility`, to avoid layout shifts)
    - Position: insert after the Compress button (line 89), before the first Separator (line 90) — groups with New/Open/Extract/Compress operations
  - In `MainWindow.xaml.cs`, add event handler:
    - Determine destination using same logic as `HandleExtractSmart`
    - Show progress, call engine extract
    - Reuse existing `ExtractAsync` method or flow
    - Handle password if needed

  **Must NOT do**:
  - Don't change existing toolbar layout
  - Don't add MVVM patterns

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Task 1, 3)
  - **Blocks**: None
  - **Blocked By**: Task 1, Task 3

  **References**:
  - `src/MantisZip.UI/MainWindow.xaml` — Existing toolbar buttons for layout pattern
  - `src/MantisZip.UI/MainWindow.xaml.cs:476-525` — Existing ExtractAsync for progress flow
  - `src/MantisZip.Core/Utils/ArchiveStructureAnalyzer.cs` — Smart logic

  **QA Scenarios**:
  ```
  Scenario: XAML compiles with correct IsEnabled pattern
    Tool: Bash (dotnet build + grep)
    Preconditions: XAML changes applied
    Steps:
      1. dotnet build src/MantisZip.UI/MantisZip.UI.csproj → verify 0 errors
      2. grep 'SmartExtractBtn' MainWindow.xaml → confirm IsEnabled="False" attribute
         (matching AddFilesBtn/DeleteFilesBtn pattern, not Visibility)
    Expected Result: Build clean, button uses IsEnabled pattern for archive-dependency
    Evidence: .sisyphus/evidence/task-6-build.txt

  Scenario: Handler references correct engine method
    Tool: Bash (grep)
    Preconditions: Code-behind changes applied
    Steps:
      1. grep 'HasSingleRootDirectory' MainWindow.xaml.cs → verify handler calls it
      2. grep 'ExtractAsync' MainWindow.xaml.cs → verify extract flow exists
    Expected Result: Handler logic references ArchiveStructureAnalyzer and engine extract
    Evidence: .sisyphus/evidence/task-6-handler.txt

  Scenario: App runs with button visible (manual smoke test)
    Tool: Bash (interactive)
    Preconditions: App built
    Steps:
      1. Launch MantisZip.UI.exe
      2. Open a .zip archive via File → Open
      3. Visually confirm "智能解压到此处" button appears in toolbar
      4. Click it and verify correct behavior (single-root vs multi-root)
    Expected Result: Button appears and functions correctly
    Evidence: .sisyphus/evidence/task-6-smoke.txt
  ```

  **Commit**: YES (groups with Task 4)
  - Message: `feat(ui): add smart extract toolbar button`
  - Files: `src/MantisZip.UI/MainWindow.xaml`, `src/MantisZip.UI/MainWindow.xaml.cs`

---

- [ ] 7. Add settings checkbox to SettingsWindow

  **What to do**:
   - In `SettingsWindow.xaml`, add a CheckBox `x:Name="EnableSmartCheck"` for "智能解压到此处" in the context menu section, after `EnableExtractCheck` (the last checkbox in the first StackPanel of the context menu tab)
  - In `SettingsWindow.xaml.cs`:
    - Load: `EnableSmartCheck.IsChecked = s.EnableSmartExtractMenu;`
    - Save: `s.EnableSmartExtractMenu = EnableSmartCheck.IsChecked == true;`

  **Must NOT do**:
  - Don't redesign the settings layout

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 4, 5, 6)
  - **Blocks**: None
  - **Blocked By**: Task 2

  **References**:
  - `src/MantisZip.UI/SettingsWindow.xaml` — Context menu section layout
  - `src/MantisZip.UI/SettingsWindow.xaml.cs:73-78` — Existing checkbox loading
  - `src/MantisZip.UI/SettingsWindow.xaml.cs:147-152` — Existing checkbox saving

  **QA Scenarios**:
  ```
  Scenario: Build compiles and property binding is correct
    Tool: Bash (dotnet build + grep)
    Preconditions: XAML + code-behind changes applied
    Steps:
      1. dotnet build src/MantisZip.UI/MantisZip.UI.csproj → verify 0 errors
      2. grep 'EnableSmartExtractMenu' SettingsWindow.xaml.cs → verify
         load: EnableSmartCheck.IsChecked = s.EnableSmartExtractMenu
         save: s.EnableSmartExtractMenu = EnableSmartCheck.IsChecked == true;
    Expected Result: Build clean, load/save logic matches AppSettings property
    Evidence: .sisyphus/evidence/task-7-settings.txt

  Scenario: AppSettings default is true
    Tool: Bash (grep)
    Preconditions: AppSettings.cs modified in Task 2
    Steps:
      1. grep 'EnableSmartExtractMenu' AppSettings.cs → confirm default = true
    Expected Result: Property exists with default true
    Evidence: .sisyphus/evidence/task-7-default.txt
  ```

  **Commit**: YES (groups with Task 2)
  - Message: `feat(settings): add EnableSmartExtractMenu checkbox to SettingsWindow`
  - Files: `src/MantisZip.UI/SettingsWindow.xaml`, `src/MantisZip.UI/SettingsWindow.xaml.cs`

---

- [ ] 8. Integration tests

  **What to do**:
  - New file: `tests/MantisZip.Tests/Engines/SmartExtractTests.cs`
  - Test scenarios:
    1. Single root folder → `HasSingleRootDirectory` returns `true`
    2. Multiple root entries → returns `false`
    3. Empty archive → returns `true`
    4. Single file at root → returns `false`
    5. Mixed (file + folder at root) → returns `false`
    6. Only directories (no files) → returns `true` (directories at root share a single common ancestor)
    7. Create real zip archives with single/multi root structure, test end-to-end smart destination resolution
    8. Encrypted archive: create a password-protected zip with single-root structure → verify smart extract CLI handles password prompt and extracts correctly

  **Must NOT do**:
  - Don't test ShellIntegration (registry) in unit tests
  - Don't test MainWindow UI

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Task 3)
  - **Blocks**: None
  - **Blocked By**: Task 3

  **References**:
  - `tests/MantisZip.Tests/Engines/ZipEngineTests.cs` — Test patterns and setup/teardown
  - `tests/MantisZip.Tests/Fixtures/ArchiveFixtures.cs` — Existing fixture helpers
  - `src/MantisZip.Core/Utils/ArchiveStructureAnalyzer.cs` — Unit under test

  **Acceptance Criteria**:
  - [ ] `dotnet test tests/MantisZip.Tests/` passes with new tests

  **QA Scenarios**:
  ```
  Scenario: All tests pass
    Tool: Bash
    Preconditions: All code changes from previous tasks complete
    Steps:
      1. dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
    Expected Result: Tests passed, including new SmartExtractTests
    Evidence: .sisyphus/evidence/task-8-tests.txt
  ```

  **Commit**: YES
  - Message: `test(smart-extract): add integration tests`
  - Files: `tests/MantisZip.Tests/Engines/SmartExtractTests.cs`

---

## Success Criteria

### Verification Commands
```bash
dotnet build src/MantisZip.UI/MantisZip.UI.csproj
dotnet test tests/MantisZip.Tests/MantisZip.Tests.csproj
```

### Final Checklist
- [ ] All localization keys present (zh + en + L.cs)
- [ ] AppSettings.EnableSmartExtractMenu exists, defaults to true
- [ ] ArchiveStructureAnalyzer.HasSingleRootDirectory correctly analyzes archive structure
- [ ] `--extract-smart` CLI works for both single-root and multi-root archives
- [ ] Context menu verb registered correctly (verified via reg query)
- [ ] Settings checkbox loads/saves correctly
- [ ] Toolbar button visible when archive loaded, hidden otherwise
- [ ] All integration tests pass
- [ ] No regression: existing extract here and extract to named still work
