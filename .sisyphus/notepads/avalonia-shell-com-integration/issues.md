# Shell Integration Port — Learnings

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
"@ | Out-File -FilePath "E:\github\MantisZip\.sisyphus\notepads\avalonia-shell-com-integration\learnings.md" -Encoding utf8

@"
# Known Issues

## AppSettings.EnableDynamicMenu
- Avalonia AppSettings already has EnableDynamicMenu, ShowMenuIcons, and all individual toggle properties

## Localization
- Added Shell_* and ShellExt_* keys to both zh-CN.json and en.json
- ShellIntegration uses LocalizationManager.T() not L.T()

## ShellExt.csproj
- Already has <EmbeddedResource> links for MenuIcons from ..\MantisZip.UI\Resources\MenuIcons\
- These links will continue to work regardless of which UI project builds ShellExt
