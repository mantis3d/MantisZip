# Avalonia Port Phase 3 — Learnings

## ExtractSettingsDialog Implementation

### Files created
- `src/MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs`
- `src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml`
- `src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml.cs`

### Key patterns followed

**ViewModel (MVVM with CommunityToolkit.Mvvm):**
- `ObservableObject` base class with `[ObservableProperty]` for bindable properties
- `[RelayCommand]` for commands (generates `BrowseDestinationCommand`, `ExtractCommand`, `CancelCommand`)
- Callbacks via `Func<Task<T?>>` pattern (e.g., `BrowseFolder`, `CloseAction`) — set by the View code-behind
- `IReadOnlyList<string> ArchivePaths` as constructor input; `SelectedPaths` as writable output list

**Window (code-behind):**
- **Must have a parameterless constructor** for Avalonia XAML code-gen (`AVLN3000` error if missing)
- Parameterized constructor calls `this()` (parameterless) first, then sets up ViewModel + callbacks
- Folder picker via `StorageProvider.OpenFolderPickerAsync` (Avalonia.Platform.Storage)
- Dialog result via `Close(bool)` pattern

**XAML:**
- Theme bindings: `ThemeWindowBgBrush`, `ThemeSurfaceBgBrush`, `ThemeTextPrimaryBrush`, `ThemeBorderBrush`, `ThemeComboBoxBgBrush`, `ThemeComboBoxBorderBrush`, `ThemeListSelectedBrush`, `ThemeButtonBgBrush`, `ThemeButtonHoverBrush`, `ThemeButtonPressedBrush`, `ThemeAccentBrush`
- `TextBox.Watermark` is obsolete in Avalonia 12 — use `PlaceholderText` instead
- ComboBox uses `SelectedItem` binding with `<ComboBoxItem Content="...">` children
- Buttons with Command bindings use `<Button.Styles>` for hover/pressed states

## CompressSettingsDialog Implementation

### Files created
- `src/MantisZip.UI.Avalonia/ViewModels/CompressSettingsViewModel.cs`
- `src/MantisZip.UI.Avalonia/Dialogs/CompressSettingsWindow.axaml`
- `src/MantisZip.UI.Avalonia/Dialogs/CompressSettingsWindow.axaml.cs`

### Key patterns followed

**ViewModel:**
- `[ObservableProperty]` for all bindable fields (format, level, output path, password, confirm, encrypt, comment, distribution)
- Comment radio button sync via 3 bool properties (`CommentAllSame`, `CommentFirstOnly`, `CommentPerLine`) with partial method sync — avoids enum converters
- Computed properties: `PasswordsMatch` (Password == ConfirmPassword), `PasswordStrength` (None/Weak/Medium/Strong based on length + char variety), `SelectedPathsSummary`
- Callbacks: `BrowseOutput` (`Func<Task<string?>>?` for save file picker), `CloseAction` (`Func<bool, Task>?` for window close with result)
- Commands: `BrowseOutputPathCommand`, `StartCompressCommand` (validates passwords match before close), `CancelCommand`

**Window (code-behind):**
- Parameterless constructor for Avalonia XAML code-gen, parameterized constructor takes `IReadOnlyList<string> sourcePaths`
- Save file picker via `StorageProvider.SaveFilePickerAsync` with `FilePickerSaveOptions` and `FilePickerFileType`
- TabControl with 3 tabs: General (format ComboBox, level Slider 1-9, output path), Password (password/confirm reveal toggle), Comment (text + distribution radio buttons)
- Password reveal toggle via code-behind `TogglePasswordReveal` event handler (toggles `PasswordChar` between '●' and default)
- `SizeToContent="Height"` (matches ExtractSettingsWindow pattern)

**XAML gotchas:**
- `StringFormat` in bindings can cause `AVLN2000` errors in Avalonia — use computed properties instead
- TabControl/TabItem styles need explicit `ThemeTabHeaderBgBrush` / `ThemeTabHeaderFgBrush` from theme resources
- RadioButton GroupName sync works with separate bool bindings + partial method sync
- Slider needs `IsSnapToGrid="True"` for integral values (not `TickFrequency` alone)
- Available formats: only "zip" and "tar.gz" (no 7z for cross-platform)

## MainWindowViewModel Phase 3 Additions

### New callback signatures used
- `Func<ExtractSettingsViewModel, Task<bool?>>? ShowExtractSettingsDialog` — MainWindowViewModel creates the VM, passes to callback. View shows dialog with this VM, returns true/false. MainWindowViewModel reads VM properties after.
- `Func<CompressSettingsViewModel, Task<bool?>>? ShowCompressSettingsDialog` — same pattern for compress.
- `Func<string, Func<IProgress<ArchiveProgress>, CancellationToken, Task>, Task<bool>>? RunWithProgress` — unified progress window callback. View creates ProgressWindow, shows non-modally, runs operation, closes window, returns success/failure.

### Filter system architecture
- `FilterFiles()` method (alias to `PopulateEntries()`) applies text/date/size filters on top of folder navigation
- `GetFilteredSource()` — applies all active filters to `_allRawItems`, returns filtered `IReadOnlyList<ArchiveItem>`
- `PopulateEntries()` — applies `GetFilteredSource()` + `ArchiveEntryLister.GetEntriesInFolder(..., ShowSubfolders)` → `CurrentEntries`
- `_isProgrammaticFilter` flag prevents re-entrant filtering during programmatic updates
- Each filter property (`FilterText`, `FilterDateFrom`, etc.) triggers `ApplyFilter()` via partial method

### Property change notifications
- `SelectionStats` — computed property notified via `OnPropertyChanged(nameof(SelectionStats))` in `OnSelectedEntryChanged`
- `ArchiveStats` — notified at end of `LoadArchiveAsync` after `_currentFormat` is set
- Both are manual properties (not `[ObservableProperty]`) with `FormatUtil.FormatSize()` for human-readable sizes

### Dialog callback pattern
Unlike WPF where MainWindow instantiates dialogs directly, the Avalonia ViewModel uses callbacks set by the View code-behind. Extract/Compress dialog VMs are created in MainWindowViewModel (so properties can be read after dialog closes) and passed to the callback for the View to display.
