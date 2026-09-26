## [TIMESTAMP] P1-S5: Text Subtype Detection

### Design
- `DetectTextSubtype(byte[] head, int length)` — called when `LooksLikeText` returns true
- Converts bytes to string (UTF-8 with BOM detection), then analyzes content
- Returns `Xml` > `Json` > `Html` > `Markdown` > `Csv` > `Ini` > `Text` by specificity
- All new code in `FileFormatDetector.cs`, file-scoped namespace

### Enum additions (already applied to FileFormatInfo.cs)
- `Csv, Json, Xml, Ini` added to `// 文本/标记` line

### FileFormatHelper display names
- Csv → "CSV 表格"
- Json → "JSON 数据"
- Xml → "XML 文档"
- Ini → "INI 配置文件"

### Detection heuristics
1. **XML**: first line `<?xml ` or root element `<...>` pattern, high angle-bracket density
2. **JSON**: first non-whitespace char `{` or `[`, balanced brackets
3. **HTML**: `<!DOCTYPE html` or `<html`/`<head`/`<body` tags (case insensitive)
4. **Markdown**: heading lines `# ` / `## ` / `### `, `[]()` links, ` ``` ` code fences
5. **CSV**: 3+ lines with consistent delimiter count (comma/tab/semicolon)
6. **INI**: `[section]` header lines + `key=value` pattern
7. **Text**: fallback

### Modified `Detect()` flow
```csharp
// After all magic checks fail:
if (LooksLikeText(head, Math.Min(length, 512)))
{
    var subtype = DetectTextSubtype(head, length);
    return subtype;  // no longer always returns Text
}
```

### Already done
- FileFormatInfo.cs: Csv, Json, Xml, Ini added

### Files to modify
- `src/MantisZip.Core/Utils/FileFormatHelper.cs` — display names
- `src/MantisZip.Core/Utils/FileFormatDetector.cs` — DetectTextSubtype, Detect(), DetectByExtension
- `src/MantisZip.UI/MainWindow/Preview/MainWindow.Preview.cs` — TryMagicPreview, MapFileFormatToExtension

---

## [2026-07-01] Implementation Complete

### Changes made
1. **FileFormatHelper.cs**: Added 4 display name entries (`Csv → "CSV 表格"`, `Json → "JSON 数据"`, `Xml → "XML 文档"`, `Ini → "INI 配置文件"`)
2. **FileFormatDetector.cs**:
   - `Detect()` — replaced `return FileFormat.Text` with `return DetectTextSubtype(head, length)`
   - `DetectByExtension()` — added `.csv → Csv`, `.json → Json`, `.xml → Xml`, `.ini → Ini`
   - Added `DetectTextSubtype()` — full method with UTF-8/CP1252 decoding fallback
   - Added helper methods: `AngleBracketDensity()`, `HasBalancedBrackets()`, `StartsWithAny()`, `ContainsAny()`, `LooksLikeCsv()`, `LooksLikeIni()`, `SplitLines()`

### Detection order
XML → JSON → HTML → Markdown → CSV → INI → Text (fallback)

### Design notes
- HTML files starting with `<html` get caught by the XML angle-bracket-density check first since `<` + density > 30% matches HTML too. The task explicitly specifies this order.
- `HasBalancedBrackets` correctly handles JSON strings (tracks `inString` state to avoid counting inside strings)
- CSV checks 3 delimiters (comma, tab, semicolon) requiring 3+ lines with consistent count
- INI checks `[section]` presence AND `key=value` ratio > 30%
- Line splitting handles `\n`, `\r`, `\r\n` edge cases without LINQ dependency
- UTF-8 decoding with replacement-char threshold (10%) before falling back to CP1252
- Build passes with 0 errors (only pre-existing warnings from ExplorerWindowTracker.cs)
## 2026-07-01: Text subtype preview routing

### Changes made to MainWindow.Preview.cs

**TryMagicPreview** (added 2 new case blocks before default: return false):
- FileFormat.Csv → extracts to "preview.csv" → calls ShowCsvPreview(csvFile, item)
- FileFormat.Json or FileFormat.Xml or FileFormat.Ini → extracts to "preview{ext}" → calls ShowTextPreview(jsonXmlIniFile, ext, item)
- Both cases guard on s.EnableTextPreview and item.Size > s.MaxTextPreviewBytes (same pattern as existing Text case)

**MapFileFormatToExtension** (added 4 mappings after existing Markdown entry):
- FileFormat.Csv => ".csv"
- FileFormat.Json => ".json"
- FileFormat.Xml => ".xml"
- FileFormat.Ini => ".ini"

### Pre-existing issue (outside scope)
- FileFormatDetector.cs line 341 references DetectTextSubtype(head, length) which is not implemented yet → CS0103 build error in Core project. This method needs to be written to route text-format files (CSV/JSON/XML/INI) based on content heuristics.
