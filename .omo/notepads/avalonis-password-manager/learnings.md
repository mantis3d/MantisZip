# PasswordManagerWindow for Avalonia — Learnings

## Files created
- `src/MantisZip.UI.Avalonia/Dialogs/PasswordManagerWindow.axaml` — XAML layout
- `src/MantisZip.UI.Avalonia/Dialogs/PasswordManagerWindow.axaml.cs` — code-behind

## Files modified
- `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` — added 9 new keys
- `src/MantisZip.UI.Avalonia/Localization/strings.en.json` — added 9 new keys

## Key patterns followed
- Code-behind pattern with `DataContext = this` (like PasswordDialog.axaml.cs)
- All controls use `{DynamicResource Theme*}` bindings
- Button styles follow established pattern: `Background`, `Foreground`, `BorderBrush` + `:pressed` / `:pointerover` setters
- DataGrid has column header styles matching MainWindow
- Localization via `LocalizationManager.T()` with `PasswordManager_*` key prefix

## Key decisions
- Inline edit panel for Add/Edit/Delete confirmation (no separate dialog needed)
- Edit panel reuses for delete confirmation with disabled fields and Yes button
- Password masking via `PasswordChar` property on TextBox (same as PasswordDialog)
- `PlaceholderText` (not `Watermark` which is obsolete in this Avalonia version)
- `PasswordManager` is in `MantisZip.Core` namespace (not `MantisZip.Core.Utils`)
