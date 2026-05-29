# 智能解压到此处 (Smart Extract Here)

> **状态**: ✅ 已完成（v0.2.10）| **阶段**: [✅✅✅✅✅✅✅✅] (8/8)

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

- [x] 1. Add localization strings
- [x] 2. Add EnableSmartExtractMenu to AppSettings
- [x] 3. Add SmartExtract logic helper
- [x] New file: `Core/Utils/ArchiveStructureAnalyzer.cs`
- [x] 4. Add --extract-smart CLI handler to App.xaml.cs
- [x] 5. Add smart extract verb to ShellIntegration
- [x] 6. Add smart extract toolbar button to MainWindow
- [x] 7. Add settings checkbox to SettingsWindow
- [x] 8. Integration tests

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
- [x] `dotnet test tests/MantisZip.Tests/` passes with new tests
- [x] All localization keys present (zh + en + L.cs)
- [x] AppSettings.EnableSmartExtractMenu exists, defaults to true
- [x] ArchiveStructureAnalyzer.HasSingleRootDirectory correctly analyzes archive structure
- [x] `--extract-smart` CLI works for both single-root and multi-root archives
- [x] Context menu verb registered correctly (verified via reg query)
- [x] Settings checkbox loads/saves correctly
- [x] Toolbar button visible when archive loaded, hidden otherwise
- [x] All integration tests pass
- [x] No regression: existing extract here and extract to named still work
