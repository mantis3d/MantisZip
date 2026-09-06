## [2026-06-10] Task E3 - Exclude/MatchMode localization
- Added 4 keys to both JSON files after Main_Filter_PickSizeMax
- Keys: Main_Filter_ExcludeLabel, Main_Filter_ExcludePlaceholder, Main_Filter_MatchModeSubstring, Main_Filter_MatchModeWildcard
- Inserted after line 309 in zh.json, after line 297 in en.json
- Both files validated as valid JSON (ConvertFrom-Json succeeds)
- Existing keys: zh.json grew from 784 to 788 lines, en.json from 757 to 761 lines
- Note: build fails intermittently due to ShellExt.dll lock by Everything.exe (environment, not code)

## [2026-06-10] Task E1a/E1b - Area 1 XAML + event handlers

### ArchiveFilter.cs
- Added `FilterMatchMode` enum (`Substring`, `Wildcard`)
- Added `ExcludeText` (string?) and `MatchMode` (FilterMatchMode) to `SearchFilters` record

### MainWindow.xaml.cs
- Added fields `_excludeText` (string?) and `_matchMode` (FilterMatchMode)

### MainWindow.xaml (area 1)
- Outer StackPanel changed from `Orientation="Horizontal"` to `Orientation="Vertical"`
- Row 1: 🔍 + FileSearchBox + MatchModeCombo (ComboBox, 100px, 2 items)
- Row 2: ⊘ + ExcludeBox (TextBox, 160px)
- All new controls have DynamicResource theme bindings (Theme_WindowBg, Theme_TextPrimary, Theme_Border)

### MainWindow.UI.cs
- `ClearFiltersBtn_Click`: Added ExcludeBox.Text = "", MatchModeCombo.SelectedIndex = 0, _excludeText = null
- `ToggleFilterBarBtn_Click`: Added _excludeText = null, _matchMode = FilterMatchMode.Substring
- Added 3 handler stubs: MatchModeCombo_SelectionChanged, ExcludeBox_TextChanged, ExcludeBox_PreviewKeyDown (Escape)

### Notes
- Build 0 errors 0 warnings after fix (Everything.exe lock resolved)

## [2026-06-10] Task E2 — Exclude + wildcard logic

### ArchiveFilter.cs
- Added `using System.Collections.Concurrent` + `using System.Text.RegularExpressions`
- Added `ConcurrentDictionary<string, Regex> _regexCache` for wildcard pattern caching
- Added `MatchItem(ArchiveItem, string pattern, FilterMatchMode mode)` static method:
  - `Substring`: uses `string.Contains` with `OrdinalIgnoreCase`
  - `Wildcard`: converts `*` → `.*` and `?` → `.` after `Regex.Escape`, then caches Regex
- Updated `ApplyFilters` empty-filters early return to check `ExcludeText`
- Replaced inline substring logic with `MatchItem()` call
- Added exclude filter block: if `ExcludeText` set, items matching it are excluded

### MainWindow.UI.cs
- `MatchModeCombo_SelectionChanged`: reads SelectedIndex → sets _matchMode → RefreshFilter()
- `ExcludeBox_TextChanged`: reads text → sets _excludeText (null if empty/whitespace) → RefreshFilter()
- `ExcludeBox_PreviewKeyDown`: Escape clears text, _excludeText = null, RefreshFilter()
- `RefreshFilter()`: now passes ExcludeText + MatchMode to SearchFilters constructor
- `HasActiveFilters()`: now checks `!string.IsNullOrEmpty(_excludeText)`

### Verification
- Build: 0 errors, 4 pre-existing warnings
- Tests: 171/171 passed

## [2026-06-10] Task E4 — Unit tests for wildcard + exclude

### Added 12 new test methods to FileListFilterTests.cs:

**Substring Regression (1):**
- `Filter_Exclude_Substring_Regress` — guards that MatchMode.Substring preserves existing behavior

**Wildcard (5):**
- `Filter_Wildcard_Star` — `*.cs` returns 4 .cs files
- `Filter_Wildcard_Question` — `?at` matches 3-char names (cat/bat/hat), not 4-char (chat)
- `Filter_Wildcard_Prefix` — `src/*` matches all src/ files, excludes root files
- `Filter_Wildcard_CaseInsensitive` — `*.CS` same as `*.cs` (RegexOptions.IgnoreCase)
- `Filter_Wildcard_NoMatch` — `*.xyz` returns empty

**Exclude (3):**
- `Filter_Exclude_Text` — same include/exclude text → empty
- `Filter_Exclude_NonExclude` — `.cs` files excluding `util.cs` → 3 items
- `Filter_Exclude_Empty` — empty exclude string → no effect

**Combined (2):**
- `Filter_Wildcard_WithExclude` — `*.cs` + exclude `test*` → .cs files except test-prefixed
- `Filter_Substring_WithExclude` — `src` + exclude `utils` → src/ files not under utils/

**Cache (1):**
- `Filter_Wildcard_CacheReuse` — same pattern called twice returns same results

### Gotchas discovered during implementation:
1. **`?at` doesn't match "cat.txt"**: The wildcard `?` becomes `.` in the regex anchored with `$`, so `?at` → `^.at$` only matches exactly 3 characters. The test data must use extensionless names ("cat" not "cat.txt") for `?at` to work correctly.
2. **`ExcludeText="util"` over-matches**: Substring "util" in exclude would also exclude `src/utils/helper.cs` (path contains "util"). Used `ExcludeText="util.cs"` instead to specifically target only `util.cs`.
3. **ConcurrentDictionary cache is static**: The `_regexCache` is shared across all calls, which the `Filter_Wildcard_CacheReuse` test validates — same pattern returns same cached regex each time.

### Verification
- Build: 0 errors, 0 warnings
- Tests: 183/183 passed (171 original + 12 new)
