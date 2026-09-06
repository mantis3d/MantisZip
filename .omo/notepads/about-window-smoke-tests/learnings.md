# AboutWindow Smoke Tests — Learnings

## Path resolution for test project

The test project (`tests/MantisZip.Tests`) only references `MantisZip.Core`, not `MantisZip.UI`. This means:
- Cannot reference `AppConstants.Version` or `L.cs` constants directly
- Must test JSON localization files via file I/O instead

## Repo root detection from test runner

The test assembly runs from `tests/MantisZip.Tests/bin/Debug/net9.0/`. Can't hardcode the up-level count because it varies.

**Solution**: Walk up from `AppContext.BaseDirectory` looking for directory with a `src` subdirectory as repo root marker:

```csharp
var dir = AppContext.BaseDirectory;
while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
    dir = Path.GetDirectoryName(dir);
```

## What's tested

13 test methods covering:
- Both JSON files exist
- All 21 `About_*` keys present in both zh and en
- All `About_*` values non-empty
- Key set parity between zh and en (same keys)
- Minimum count check (≥21)
- Backward compat: `Main_About_Text`, `Main_About_Title` exist
- Cross-lang consistency: if zh has value, en should too
