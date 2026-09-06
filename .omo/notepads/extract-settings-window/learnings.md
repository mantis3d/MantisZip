
## [2026-05-29] Plan Complete: ExtractSettingsWindow

**Status**: ALL TASKS COMPLETE ? (4/4 + F1-F4)

**Files Created**:
- src/MantisZip.Core/Models/ExtractOutputMode.cs �� Enum with Here, Smart, ToName, Manual
- src/MantisZip.UI/ExtractSettingsWindow.xaml �� Window layout (480px, 8 rows, theme resources)
- src/MantisZip.UI/ExtractSettingsWindow.xaml.cs �� Code-behind with radio logic, Browse dialog, DialogResult

**Files Modified**:
- src/MantisZip.UI/Localization/L.cs �� Added 11 ExtractSettings_ keys
- src/MantisZip.UI/Resources/strings.zh.json �� Chinese translations
- src/MantisZip.UI/Resources/strings.en.json �� English translations

**Verification**:
- dotnet build: 0 errors, 0 warnings ?
- All 11 localization keys present in all 3 files ?
- Scope fidelity: no files outside plan scope modified ?
- Git status matches expected files only ?

**Design Notes**:
- Subagents were unavailable (model config issue), so Atlas executed manually
- XAML pattern matches CompressSettingsWindow (theme resources, l:L binding)
- Code pattern: ObservableCollection for file list, 4 RadioButton group, VistaFolderBrowserDialog for Manual mode
- Default mode: ToName (safest, per-archive isolation)
- Ready for integration with HandleExtractBatch (Task 7) and HandleExtract* (Task 9) in batch-progress-list.md

## [2026-05-30] Redesign: Visual alignment with CompressSettingsWindow

**Changes**:
- **Layout**: Simple Auto-stack → TabControl (TabItem template matching Compress) + GroupBox × 3 + 2-column Grid (80px label column)
- **Tab 1 "基本"**: GroupBox "源文件" (fixed height 140, like Compress) + GroupBox "解压选项" (output mode radios + output path)
- **Tab 2 "高级"**: GroupBox "行为设置" (file conflict radios + open folder checkbox)
- **配色**: Removed ALL explicit Foreground/Background/BorderBrush from ListBox, TextBox, Button → theme inheritance matching Compress
- **输出路径**: No more Visibility toggle (caused layout shift). Always visible, switches IsEnabled. Disabled mode shows computed path preview
- **First archive path**: `_firstArchiveDir` / `_firstArchiveNameOnly` computed in constructor for path preview
- **Conflict + open folder**: Written to AppSettings in ExtractButton_Click (HandleExtractBatchCore reads them)
- **Localization**: 8 new keys added (Tab/GroupBox/label headers)
- **Window**: Width 530px (matches CompressSettingsWindow)
- **Build**: 0 errors, 0 warnings
