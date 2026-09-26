# installer-dotnet-autodownload — Learnings

## Summary
Added .NET 9 Desktop Runtime automatic detection, download, and silent installation to `installer.iss`, following the exact WebView2 pattern.

## Key observations

### Registry key structure
- .NET 9 Desktop Runtime registers under `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App`
- Subkeys are version numbers like `9.0.0`, `9.0.1` etc.
- Detection must use `RegGetSubkeyNames` (subkey enumeration) rather than `RegQueryStringValue` (value check) — different from WebView2's `pv` value approach
- Both HKLM and HKLM32 (WOW6432Node) must be checked for 32-bit runtime installations

### Inno Setup Pascal quirks
- `TArrayOfString` is the type for dynamic string arrays returned by `RegGetSubkeyNames`
- `GetArrayLength()` is the correct way to get array length (not `Length()`)
- `Copy(str, 1, 2)` extracts first 2 characters — Inno Pascal uses 1-based indexing
- `Exit;` can be used to early-return from a function

### Pre-existing compilation issue
- `iscc installer.iss` fails with "publish_output\*.csv does not exist" — this is unrelated to our changes. The Pascal [Code] section compiles without errors.

### Structure of changes
- Constants added in the `const` section alongside WebView2 constants
- Function placed after `IsWebView2Installed` and before `URLDownloadToFile` external declaration
- .NET block placed before WebView2 block in `CurStepChanged(ssPostInstall)` — .NET is more critical (app won't start without it)
- `BootstrapperPath` and `ResultCode` variables were already declared at the top of `CurStepChanged` — reused by both .NET and WebView2 blocks
