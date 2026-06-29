# Contributors Panel QA — Learnings

## Key verification results

**All 9 scenarios PASS.** The Contributors Panel implementation is correct and handles all edge cases properly.

### Architecture notes

- `Contributor` is a private nested class in `AboutWindow.xaml.cs` (line 118-122), not a separate file
- Simple model: `Name` (string, init), `Score` (int, init)
- Shared loader pattern: `LoadContributorList(string fileName, ItemsControl listControl, TextBlock emptyControl)` serves both sections
- Two independent CSV files: `contributors-technical.csv` and `contributors-financial.csv`, loaded from `AppDomain.CurrentDomain.BaseDirectory`

### Important design choices

1. **BOM handling**: `File.ReadAllLines` with `Encoding.UTF8` correctly handles both BOM and non-BOM UTF-8 files through StreamReader's built-in BOM detection
2. **Error isolation**: Each section's loading is fully independent — failure in one does not affect the other
3. **No score leakage**: ItemTemplate only binds `{Binding Name}` — no tooltip, Tag, or hidden element exposes Score
4. **Exception coverage**: Only IOException and UnauthorizedAccessException are caught — other exceptions (e.g., OutOfMemoryException) will propagate to the caller
5. **Sorting**: Score descending + Name ascending (Ordinal comparison) — consistent with typical leaderboard display

### F4 Scope Fidelity Post-Mortem

6. **Plan spec vs existing code conflict**: The plan spec said "use StaticResource for theme bindings" but the existing AboutWindow.xaml had a mix — the `Background` of the TabControl etc. used StaticResource, while the Close button used StaticResource, but the TextBlocks in the Acknowledgments tab already used DynamicResource (converted from StaticResource during implementation). **Lesson**: When writing plan specs, verify the actual code pattern first rather than assuming what pattern is used.

7. **BOM spec not strictly enforced**: The plan required "UTF-8 with BOM" but the implemented files are BOM-less UTF-8. The code handles both equally, so the spec requirement was unnecessarily strict. **Lesson**: For plan specs, distinguish between "functionally required" and "preferred but flexible" requirements.

8. **Scope creep detection**: The most significant deviation was converting existing StaticResource bindings to DynamicResource (on 3 pre-existing TextBlocks). This went beyond the scope of "add contributor sections" and modified existing unrelated markup. **Lesson**: Implementation agents should be explicitly instructed to minimize modifications to pre-existing code outside the new addition's boundary.
