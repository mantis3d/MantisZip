# Shell Integration Port — Issues & Learnings

## Key Architecture Decisions
- ShellIntegration uses internal static partial class across 3 files in namespace MantisZip.UI.Avalonia.Services
- Uses Microsoft.Win32.Registry for HKCU\Software\Classes operations (Windows-only)
- No WPF dependencies — registry + SHChangeNotify + Process are all framework-agnostic

## Adaptations from WPF
- WPF App.LogDebug() → Avalonia App.DebugLog()
- WPF L.T() → Avalonia LocalizationManager.T()
- WPF AppSettings.Instance → Avalonia AppSettings.Load() / new AppSettings()
- WPF GetExePath() → use Environment.ProcessPath or Assembly.GetEntryAssembly().Location

## Critical CLSID
- Must match ShellExt's ContextMenuHandler.cs: {C90B2A1E-5E4F-4A7A-9B0F-8C1D3E5F7A9B}

## AppSettings.EnableDynamicMenu
- Avalonia AppSettings already has EnableDynamicMenu, ShowMenuIcons, and all individual toggle properties

## Localization
- Added Shell_* and ShellExt_* keys to both zh-CN.json and en.json
- ShellIntegration uses LocalizationManager.T() not L.T()

## ShellExt.csproj
- Already has <EmbeddedResource> links for MenuIcons from ..\MantisZip.UI\Resources\MenuIcons\
- These links will continue to work regardless of which UI project builds ShellExt

## Cross-TFM Conflict (resolved)
- MantisZip.UI.Avalonia targets net9.0 (cross-platform)
- MantisZip.ShellExt targets net9.0-windows10.0.17763.0 (Windows-only COM)
- Direct ProjectReference causes NU1202/NF1001 incompatibility
- Solution: Remove ProjectReference, use hardcoded path in CopyShellExtComhost MSBuild target

## Build Verification
- dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj — 0 errors
- ShellExt.comhost.dll + ShellExt.dll + runtimeconfig.json auto-copied to Avalonia output dir
- WPF build may fail if ShellExt.dll locked by Explorer/Everything (pre-existing issue)

## Testing Results
- Core tests: 236/236 passed
- Avalonia tests: 35 passed, 2 skipped (IconProvider needs Windows desktop)
- Tests pass consistently — no regressions from this change

## Remaining Manual Verification
- Run MantisZip.UI.Avalonia.exe --install-shell on real Windows — check HKCU\Software\Classes
- Run --install-assoc — check per-extension ProgId creation
- Verify context menu icons appear in Explorer
