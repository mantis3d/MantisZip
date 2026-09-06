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

## 2026-07-15 - Key Alignment Deep Analysis

Key comparison results (strings.en.json):
- WPF: 804 keys | Avalonia: 608 keys
- WPF-only keys: 598 (named differently)
- Avalonia-only keys: 402 (different prefix patterns)
- Shared by exact name: ~206 keys only
- Naming divergence is FUNDAMENTAL — completely different prefix systems
  - WPF uses: Main_*, Settings_*, Preview_*, App_*, PwdMgr_*, ExtractSettings_*, ShellExt_*
  - Avalonia uses: Settings_*, Compress_*, Status_*, Menu_*, Test_*, PasswordManager_*, Filter_*, QuickPath_*, QuickPathPre_*, FormatOptions_*, Toolbar_*, Ctx_*, FavMgr_*
- Full alignment would require: canonical scheme decision + rename in one/both projects + update all code references
- Plan was correct to defer this — it's a major refactoring, not a cleanup task

