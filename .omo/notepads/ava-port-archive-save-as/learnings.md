# ArchiveSaveAsDialog Avalonia Port — Learnings

## Key differences from WPF

### Theme resource keys
- `Theme_WindowBg` → `ThemeWindowBgBrush`
- `Theme_TextPrimary` → `ThemeTextPrimaryBrush`
- `Theme_TextSecondary` → `ThemeTextSecondaryBrush`
- `Theme_Accent` → `ThemeAccentBrush`
- `Theme_ButtonBg` → `ThemeButtonBgBrush`
- `Theme_Border` → `ThemeBorderBrush`
- `Theme_SurfaceBg` → `ThemeSurfaceBgBrush`

### Password input
- This Avalonia project does NOT use native `PasswordBox` control (it doesn't resolve in Avalonia XAML code-gen). Instead, use `<TextBox PasswordChar="●" .../>` with `.Text` property instead of `.Password`.

### CheckBox events
- WPF: `Checked` / `Unchecked` events → Avalonia: `IsCheckedChanged` event (single event for both).
- Handler signature: `(object? sender, RoutedEventArgs e)`

### Dialog result
- WPF: `DialogResult = true/false` → Avalonia: `Close(true/false)`
- `Close(value)` sets the result returned by `ShowDialog<T>(owner)`.

### AppMessageBox API
- WPF: `AppMessageBox.Show(...)` is synchronous blocking
- Avalonia: `await AppMessageBox.Show(...)` returns `Task<MessageBoxResult>` — must be awaited in `async void` handlers
- Pass `this` as owner parameter to keep dialog modal

### Missing APIs
- No `PathHistoryManager` in Avalonia project — use `PathControl.AddToHistory(dir)` instead
- No `App.ApplyTextRenderingMode(this)` needed

### ComboBox
- `SelectionChanged` event exists in Avalonia with same name
- `ComboBoxItem.Tag` works in Avalonia (inherited from `StyledElement`)
