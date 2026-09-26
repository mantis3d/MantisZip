# Contributors Panel QA — Issues

> **Note**: The initial QA found no issues. Subsequent Scope Fidelity Check (F4) identified the following deviations.

## F4 Scope Fidelity Issues Found

### Issue 1: CSV files lack UTF-8 BOM (Minor)
- **Plan spec**: "UTF-8 with BOM" / "Don't use BOM-less UTF-8"
- **Actual**: Both `contributors-technical.csv` and `contributors-financial.csv` start with `0x23 0x20 0xE6` / `0x23 0x20 0xE8` (`# `) — NO BOM prefix (would be `0xEF 0xBB 0xBF`)
- **Impact**: None — `File.ReadAllLines(..., Encoding.UTF8)` handles both BOM and non-BOM correctly via StreamReader's auto-detection. But technically doesn't match the spec.
- **Severity**: Minor/Minimal (spec deviation, no functional impact)

### Issue 2: XAML theme bindings use DynamicResource instead of StaticResource (Scope Creep)
- **Plan spec**: "Theme bindings use StaticResource" / "Don't use DynamicResource (match StaticResource pattern)"
- **Actual implementation**: All new contributor section bindings use `{DynamicResource}` — AND the 3 existing TextBlocks (`About_Thanks_OSS`, `About_Thanks_7Zip`, `About_Thanks_AI`) were **converted** from `{StaticResource}` to `{DynamicResource}`.
- **Impact**: This is scope creep — the plan explicitly required matching the StaticResource pattern found in the rest of AboutWindow, but the implementation changed existing bindings. However, DynamicResource is actually more correct for theme-aware bindings that support live theme switching. All other parts of AboutWindow.xaml use a mixed pattern (some StaticResource, some DynamicResource).
- **Severity**: Moderate (plan deviation, but arguably the functionally better choice)

### Summary of scope fidelity

| Check | Status | Severity |
|-------|--------|----------|
| No UI editor or edit buttons | ✅ PASS | — |
| No score or ranking display | ✅ PASS | — |
| No click/interaction/hover effects | ✅ PASS | — |
| No avatars/icons/GitHub links | ✅ PASS | — |
| No search/filter functionality | ✅ PASS | — |
| No third-party CSV parsing library | ✅ PASS | — |
| No new XAML files or new windows | ✅ PASS | — |
| No TabControl structure modifications | ✅ PASS | — |
| No extra CSV files beyond the 2 spec'd | ✅ PASS | — |
| No extra localization keys beyond the 3 spec'd | ✅ PASS | — |
| No existing localization keys modified | ✅ PASS | — |
| UTF-8 BOM encoding on CSV files | ❌ MISS | Minor |
| Theme bindings use StaticResource | ❌ CREEP | Moderate |
