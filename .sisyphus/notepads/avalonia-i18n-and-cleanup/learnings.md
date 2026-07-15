# Learnings

## 2026-07-15 - Session Start

- Avalonia strings.en.json: 608 keys
- WPF strings.en.json: 805 keys
- Gap: ~197 keys (not 290 as originally estimated, some keys were added after plan creation)
- Avalonia.Diagnostics: Latest NuGet version is 11.3.18 (no 12.x version exists!)
- Full WPF key alignment deferred due to different naming conventions

## 2026-07-15 - Post-Version-Sync

- Avalonia LocalizationManager.cs hardcodes AvailableLanguages list — does NOT read languages.json
- languages.json was copied to Resources/ but is unused by current Avalonia code
- To use languages.json, need LoadLanguageMetadata() similar to WPF's LanguageManager
- Implemented LoadLanguageMetadata() in LocalizationManager — reads Resources/languages.json, maps "zh" → "zh-CN", falls back to hardcoded values on failure
- csproj updated: None Update now includes Resources\languages.json for CopyToOutputDirectory

