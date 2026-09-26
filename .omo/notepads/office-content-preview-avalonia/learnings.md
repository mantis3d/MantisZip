## 2026-07-21 Task 1: NuGet Integration

### Actions
- Added DocumentFormat.OpenXml v3.5.1 to Avalonia project
- Added ClosedXML v0.105.0 to Avalonia project
- Modified csproj to add Condition="'$(SkipShellExtCopy)' != 'true'" on CopyShellExtComhost target for dev builds (ShellExt.dll locked by Explorer)

### Key Files
- `src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` - new NuGet refs + SkipShellExtCopy condition

### Verification
- `dotnet build` passes with 0 errors (9 pre-existing warnings)

## 2026-07-21 Task 2-5: DOCX/XLSX/PPTX Content Preview Implementation

### Actions
- **PreviewService.cs**: Added `Docx`, `Xlsx`, `Pptx` to `PreviewType` enum; updated `ClassifyPreview()` to check individual extensions (`.docx`/`.xlsx`/`.pptx`) instead of `OfficeExtensions.Contains(ext)`; updated `MapFileFormatToPreviewType()` to return individual types
- **PreviewViewModel.cs**: Added `DocxOutlineItem` model with `Text`, `Level`, `CharOffset`, `Indent` properties; added DOCX properties (`DocxOutline`, `DocxFullText`, `DocxNoOutlineText`) + `ShowDocx()` method (extracts headings + full text via DocumentFormat.OpenXml); added XLSX properties (`_xlsxDataTable`/`XlsxDataTable`, `XlsxData`) + `ShowXlsx()` (ClosedXML → DataTable, 100 rows × 100 cols); added `ShowPptx()` (manual Zip → XDocument → a:t extraction); updated `OnPreviewTypeChanged()` and `Clear()` for all new properties
- **MainWindowViewModel.cs**: Replaced `case PreviewType.Office` with separate `Docx`/`Xlsx`/`Pptx` cases
- **PreviewPanel.axaml**: Replaced Office metadata panel with DOCX left-right split (GridSplitter + outline + full text), XLSX DataGrid, PPTX TextBox panels; kept Office panel as legacy fallback
- **PreviewPanel.axaml.cs**: Added `OnOutlineItemClicked()` handler (character-offset ratio → scroll position), XLSX DataGrid column setup in `OnVmPropertyChanged`

### Gotchas
- `CoreLog` is `internal` to `MantisZip.Core` — cannot use from Avalonia UI project. Use `App.DebugLog()` instead for logging.
- `Thickness` must use `global::Avalonia.Thickness` when referenced inside `MantisZip.UI.Avalonia.*` namespace to avoid resolution as `MantisZip.UI.Avalonia.Thickness`.
- `Avalonia.Controls.TextBlock` reference with full namespace prefix `Avalonia.Controls.TextBlock` is resolved relative to current namespace (`MantisZip.UI.Avalonia.Controls.TextBlock`) — use short name `TextBlock` with existing `using Avalonia.Controls` instead.
- `ScrollViewer.ScrollBarMaximum` returns non-nullable `Vector` — use `.Offset = new Vector(x, y)` for programmatic scrolling; `ScrollToVerticalOffset()` method doesn't exist in Avalonia.
- ClosedXML `RangeUsed()` may return null for empty worksheets — handle gracefully.
- `XLWorkbook` constructor may throw for password-protected files — catch and show relevant message.
- DocumentFormat.OpenXml's `WordprocessingDocument.Open(path, false)` is read-only mode.

### Verification
- `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj -p:SkipShellExtCopy=true` passes with 0 errors (same 9 pre-existing warnings)

## 2026-07-21 Task 6: Localization Strings

### Actions
- Added 13 new localization keys to `strings.zh-CN.json` and `strings.en.json` for Office content preview status messages
- Keys inserted alphabetically between `Preview_Csv` and `Preview_Font`

### Verification
- `dotnet build` passes with 0 errors, 0 warnings
- All keys present in both zh-CN and en
