# Task 4 — SettingsWindow.xaml 新文件关联面板

## Completed
- Added `BooleanToVisibilityConverter x:Key="BoolToVis"` to Window.Resources
- Replaced old file association TabItem (2 buttons + status text) with full per-extension UI:
  - SelectAll/DeselectAll buttons
  - ItemsControl with Grid SharedSizeGroup layout: CheckBox | Icon | Extension | Description | CurrentHandler | DeleteBtn
  - FormatRow_MouseLeftButtonUp click handler on each row
  - Delete button (✕) visible only for custom extensions via BoolToVis converter
  - Add custom extension button
  - Status text (AssocStatusText preserved)
  - Bottom row: OpenDefaultApps / InstallAssoc / UninstallAssoc (x:Names preserved)
  - Inner ScrollViewer wrapped in Border for CornerRadius (ScrollViewer doesn't support CornerRadius in WPF)
- All existing x:Names preserved: `InstallAssocBtn`, `UninstallAssocBtn`, `AssocStatusText`

## Known Issues
- 5 CS1061 build errors remain for handlers not yet implemented in .cs:
  - `SelectAllBtn_Click`, `DeselectAllBtn_Click`, `FormatRow_MouseLeftButtonUp`
  - `AddCustomBtn_Click`, `OpenDefaultAppsBtn_Click`
  - These belong to Task 5 (SettingsWindow.xaml.cs)
  - `InstallAssoc_Click` and `UninstallAssoc_Click` already exist in .cs
  - ✅ All resolved in Task 5
- `FormatAssocItem` class already exists at bottom of file (lines 648-692) — Task 3 delivered it
- `ArchiveEngineFactory` is in `MantisZip.Core.Abstractions` namespace, not `MantisZip.Core.Engines`
  - Used `BuiltinAssocFormats.Length` instead of `ArchiveEngineFactory.SupportedExtensions.Length` to avoid adding another using
- `SHChangeNotify` is `private static extern` in `ShellIntegration.cs` — redeclared as private DllImport in SettingsWindow to call after selective uninstall
- Migration logic: `ShellIntegration.AreAssociationsInstalled` checks if ANY extension has registry association. Default `Assoc*` values are all `true` (except `AssocIso`), so migration only triggers when an unchecked format is detected
- `UpdateAssocStatus` and `UpdateAssocButtonState` manage `InstallAssocBtn.IsEnabled` separately — must not conflict. `UpdateAssocButtonState` handles install button; `UpdateAssocStatus` only handles uninstall button
- `InstallAssoc_Click` uses strategy: install all via `ShellIntegration.InstallAssociations()` then uninstall unchecked built-in extensions and install checked custom ones
- `SaveAssocSettings` saves all custom extensions (checked or not) — on reload they all appear checked (simplification per plan)

# Task 7 — xUnit 测试

## Completed
- Added `<InternalsVisibleTo Include="MantisZip.Tests" />` to `MantisZip.UI.csproj` so test project can access `internal` members like `ShellIntegration`
- Changed test project TFM from `net9.0` to `net9.0-windows10.0.17763.0` (required to reference MantisZip.UI which is WPF-targeted)
- Added `<ProjectReference Include="..\..\src\MantisZip.UI\MantisZip.UI.csproj" />` to test csproj
- Created `ShellIntegrationAssocTests.cs` with 22 test cases across:
  - Registry-based tests using `[Collection("RegistryTests")]` with `DisableParallelization = true` for serial execution
  - All registry tests use `try/finally` pattern for cleanup
  - Validation logic tests replicate the business rules from `AddCustomBtn_Click` as local static methods

## Key Insights
- ShellIntegration's per-extension methods (`InstallAssociationForExtension`, `UninstallAssociationForExtension`, `AreAssociationsInstalledForExtension`) do NOT depend on `App.LogDebug()` or `AppSettings`, making them safe to call from test context
- `.tar.gz` uses an early-return guard in both install and uninstall (not a skip-then-process pattern)
- `GetCurrentHandler` never returns null (returns "未设置" on error/empty)
- Validation logic (from `AddCustomBtn_Click`): Normalize (Trim + ToLowerInvariant + prepend "."), then validate (Length 2-10, starts with ".", no spaces, exactly one dot, only letter/digit after dot)
- Collection attribute is required to prevent parallel registry writes from different tests conflicting
