# Avalonia Phase 10: WPF Feature Parity (Progress Bars, Info Panel, Status Bar)

> **Status**: ✅ Completed | **Target**: v0.4.0

## Overview

Port three WPF features that were not implemented during the initial Avalonia port (Phase 0-9): file list progress bars, separated preview info panel, and enriched status bar.

## TODOs

- [x] Status Bar Enrichment
- [x] Preview Info Panel
- [x] File List Progress Bars

## 1. File List Progress Bars

### Goal

Port the WPF `file-size-progress-bar` feature: background progress bars in the DataGrid columns for Size, CompressedSize, Compression Ratio, and Date.

### Changes

| File | Change |
|------|--------|
| `src/MantisZip.UI.Avalonia/Models/ArchiveItemModel.cs` | Add 6 properties: `SizeRatio`, `CompressedSizeRatio`, `DateRatio`, `RatioBarValue`, `ProgressBarEnabled`, `SeparateDirBaseline`, `UseDirProgressColor` |
| `src/MantisZip.UI.Avalonia/Converters/RatioToWidthConverter.cs` | **New file** — `IMultiValueConverter` that takes (ratio, actualWidth) → width |
| `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` | Add `ShowProgressBars` (bool, default true) and `SeparateDirBaseline` (bool, default false) in `// ===== 外观 =====` section |
| `src/MantisZip.UI.Avalonia/Themes/ThemeLight.axaml` | Add 8 color/brush resources: `ProgressBarSizeColor`, `ProgressBarSizeDirColor`, `ProgressBarCompressedSizeColor`, `ProgressBarCompressedSizeDirColor`, `ProgressBarRatioColor`, `ProgressBarDateColor`, plus corresponding `*Bg` brushes |
| `src/MantisZip.UI.Avalonia/Themes/ThemeDark.axaml` | Same 8 color/brush resources in dark variant |
| `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml` | Register `RatioToWidthConverter`; replace Size/CompressedSize/Modified columns with `DataGridTemplateColumn` containing background `Rectangle` + text overlay; add ratio column |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | Add `ShowProgressBars`, `SeparateDirBaseline` observable properties; wire to settings; add toggle commands; compute ratios in `LoadArchiveAsync` after filtering |
| `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml` (menu) | Add View menu items for "进度条" toggle and "目录独立基准" toggle |
| `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` | Add `Menu_ProgressBars`, `Menu_SepDirBaseline` |
| `src/MantisZip.UI.Avalonia/Localization/strings.en.json` | Add `Menu_ProgressBars`, `Menu_SepDirBaseline` |

### Ratio Calculation Logic

After `FilterFiles` builds `sortedItems`, compute:

- **SizeRatio**: `item.Size / maxSizeInView` (files only for max, or all items depending on SeparateDirBaseline)
- **CompressedSizeRatio**: `item.CompressedSize / maxCompressedInView`
- **DateRatio**: `(item.LastModified - minDate) / (maxDate - minDate)` (files only, skip items with default/min date)
- **RatioBarValue**: `Math.Min(item.CompressedSize / (double)item.Size, 1.0)` (ratio column, unrestricted)

### Visual Design

| Column | Base Color (Light) | Base Color (Dark) | Dir Color (Light) | Dir Color (Dark) |
|--------|-------------------|-------------------|-------------------|-------------------|
| Size | `#E0E7FF` | `#1E3A5F` | `#A3B8FF` | `#0F2A45` |
| CompressedSize | `#E8E0FF` | `#2D1B69` | `#C4B0FF` | `#1A0F3D` |
| Ratio | `#FDE68A` | `#5C4A1E` | n/a | n/a |
| Date | `#D1FAE5` | `#14532D` | n/a | n/a |

Progress bar `Rectangle` fills the cell width with `HorizontalAlignment="Left"`, width bound to `(ratio * cellWidth)`. White text overlay on top.

## 2. Preview Info Panel

### Goal

Add a dedicated side panel next to preview content showing general file info (name, size, compressed size, ratio, date) + format-specific metadata, matching the WPF `PreviewInfoPanel`.

### Changes

| File | Change |
|------|--------|
| `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml` | Add a right-side `Border` panel (`PreviewInfoPanel`) containing: file name, size/compressed size/ratio/date grid, separator, `FormatMetadata` ItemsControl. Positioned to the right of the content area via Grid columns. |
| `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs` | Add observable properties: `FileName`, `FileSize`, `CompressedSize`, `CompressionRatio`, `ModifiedDate`. Populate when `ShowPreviewAsync` is called. |
| `src/MantisZip.UI.Avalonia/Services/PreviewService.cs` | (If applicable) adjust `ShowImagePreviewAsync` etc. to also populate the new info properties |

### Layout

```
┌──────────────────────────────────┬──────────────────┐
│                                  │  PreviewInfoPanel │
│     Preview Content              │  ────────────────  │
│     (Image/Text/HTML/etc)        │  文件名: abc.txt   │
│                                  │  大小: 1.2 MB      │
│                                  │  压缩后: 800 KB    │
│                                  │  压缩率: 65%       │
│                                  │  修改日期: ...      │
│                                  │  ────────────────  │
│                                  │  FormatMetadata    │
│                                  │  (key-value pairs) │
└──────────────────────────────────┴──────────────────┘
```

The panel uses `ThemeHeaderBgBrush` background, `CornerRadius="4"`, `Padding="10"`.

## 3. Status Bar Enrichment

### Goal

Extend the existing 3-column status bar to match WPF's richer layout, adding directory stats, date display, filter state, and encoding info.

### Changes

| File | Change |
|------|--------|
| `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml` | Extend status bar `Grid` from 3 to 6+ columns: add `DirStatsText`, `DateText`, `FilterStatsText`, `EncodingText` |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | Add observable properties: `DirStats`, `FilterStats`, `EncodingInfo`; populate from archive loading and filter changes |

### Status Bar Layout

```
│ Selected: 3 items (1.2 MB) │ 3 dirs, 15 files │ Total: 18 entries │ 2024-01-15 │ Filter: *.txt │ UTF-8 │
```

## Implementation Order

1. **Status Bar** (smallest change, independent)
2. **File List Progress Bars** (medium, touches multiple files)
3. **Preview Info Panel** (largest, affects layout and data flow)

## Dependencies

None between the three features — they can be implemented in any order.
