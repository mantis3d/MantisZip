# Avalonia Phase 10: WPF Feature Parity - Learnings

## Project Structure
- Avalonia UI project: `src/MantisZip.UI.Avalonia/`
- Themes: `ThemeLight.axaml` and `ThemeDark.axaml` (91 lines each, color+brush pairs)
- ViewModels: `MainWindowViewModel.cs` (~800+ lines), `PreviewViewModel.cs` (~300+ lines)
- Views: `MainWindow.axaml` (823 lines), `PreviewPanel.axaml` (~400+ lines)
- Models: `ArchiveItemModel.cs`, `AppSettings.cs`
- Localization: `strings.zh-CN.json`, `strings.en.json`

## Current Status Bar
- Grid.Row="4" with 3 columns: SelectionStats | ArchiveStats (center) | StatusMessage
- Colors via DynamicResource ThemeTextPrimaryBrush, ThemeHeaderBgBrush

## Current DataGrid Columns
- Icon (22px template), Name (*), Size (80), CompressedSize (70), Modified (100)
- All DataGridTextColumn, no progress bars

## Current Preview Panel
- Single scrollable content area, NO info side panel
- FormatMetadata ItemsControl embedded per-format type
- No unified file info (name/size/ratio/date) section

## Current ArchiveItemModel
- Properties: Name, DisplayName, FullPath, Size, SizeDisplay, CompressedSize, 
  CompressedSizeDisplay, LastModified, LastModifiedDisplay, IsDirectory, 
  CompressionRatio, IconSource, SortOrder
- No ratio/bar properties yet

## Current AppSettings
- Appearance section: Theme, MaxRecentFiles, Language only
- No ShowProgressBars or SeparateDirBaseline yet

## Key Rules
- All new UI controls must use DynamicResource theme bindings
- Localization keys follow Menu_*, Status_*, DataGrid_* convention
- ViewModel properties use [ObservableProperty] source generator
- PreviewViewModel uses PreviewType enum for visibility toggling
## Preview Info Panel (2026-07-01)

- Added right-side info panel to PreviewPanel.axaml: Grid changed from single-column to RowDefinitions="Auto,*" ColumnDefinitions="*,Auto"
- Info panel placed in Col 1 spanning both rows (Grid.RowSpan="2"), 220px wide, with left border separator
- Used ThemeHeaderBgBrush for panel background, ThemeBorderBrush for border, ThemeTextPrimaryBrush/ThemeTextSecondaryBrush for text
- New PreviewViewModel properties: FileName, FileSize, CompressedSize, CompressionRatio, ModifiedDate, IsInfoPanelVisible
- New SetFileInfo() method populates all info panel fields and sets IsInfoPanelVisible = true
- Clear() resets all info panel properties
- MainWindowViewModel.ShowPreviewAsync calls Preview.SetFileInfo() after the switch block (only for supported preview types)
- Uses existing ArchiveItemModel properties: NameDisplay, SizeDisplay, CompressedSizeDisplay, CompressionRatio, LastModifiedDisplay

