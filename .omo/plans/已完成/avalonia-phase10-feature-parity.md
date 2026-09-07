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

### 4. Missing Format Metadata Fields (WPF → Avalonia 补齐)

**Status**: 📋 Planned | **Priority**: P2 (post-Phase 10 cleanup)

Each format method in `PreviewViewModel.cs` populates `FormatMetadata` with format-specific key-value pairs.
The following fields exist in WPF but are missing/不同的 in the current Avalonia implementation.

| # | Format | WPF Field | WPF Key | Avalonia Status | Parser Field |
|---|--------|-----------|---------|-----------------|-------------|
| 1 | **Image** (non-GIF) | `像素数` | `Preview_ImagePixels` | ❌ 缺；Avalonia 目前用"文件大小" | `bitmap.PixelWidth * bitmap.PixelHeight` |
| 2 | **Image** (non-GIF) | `色深` | `Preview_ImageBitDepth` | ❌ 缺 | `bitmap.Format.BitsPerPixel` |
| 3 | **Image** (non-GIF) | `DPI` | `Preview_ImageDpi` | ❌ 缺 | `bitmap.DpiX / bitmap.DpiY` |
| 4 | **Image** (non-GIF) | WPF 用 BitmapDecoder 获取的信息 | — | ⚠️ Avalonia 用 `Bitmap.DecodeToWidth`，PixelSize 属性不同 | — |
| 5 | **Audio** | `位深` (BitDepth) | `Preview_AudioBitDepth` | ❌ 缺 | `FileFormatInfo.BitDepth` |
| 6 | **Audio** (MP3) | `标题` / `歌手` / `专辑` | `Preview_Mp3Title/Artist/Album` | ✅ 已通过 `info.Artist`/`info.Album` 覆盖 | `Id3v2Parser` → FileFormatInfo |
| 7 | **SQLite** | `编码` | `Preview_SqliteEncoding` | ❌ 缺；Avalonia 只查了表数量 | `meta.TextEncoding` |
| 8 | **SQLite** | `页大小` | `Preview_SqlitePageSize` | ❌ 缺 | `meta.AdditionalInfo` (含 page size) |
| 9 | **SQLite** | 表数量 | — | ✅ Avalonia 有「表数量」 | `meta.TableCount` |
| 10 | **Office** | `修改日期` | `Preview_DocModified` | ❌ 缺；Avalonia 只有创建日期 | `info.ModifiedDate` |
| 11 | **Torrent** | `创建日期` | `Preview_TorrentCreationDate` | ❌ 缺；Avalonia 无此字段 | `info.CreationDate` |
| 12 | **Torrent** | `Tracker 数量` | `Preview_TorrentTrackerCount` | ❌ 缺 | `info.TrackerCount` |
| 13 | **Torrent** | `是否私有` | `Preview_TorrentPrivate` | ❌ 缺 | `info.IsPrivate` |
| 14 | **Torrent** | `备注` (Comment) | `Preview_TorrentComment` | ❌ 缺 | `info.AdditionalInfo` |

### 5. Avalonia 优势字段（WPF 较难获取，Avalonia 更简单）

这些字段在 WPF 中需要走 `BitmapDecoder.Create` 才能获取（额外创建解码器），而在 Avalonia 中 `Bitmap` 直接暴露，一行属性访问即可。

| # | 字段 | Avalonia 获取方式 | WPF 获取方式 | 说明 |
|---|------|-----------------|-------------|------|
| 1 | Image **DPI** | `bitmap.Dpi.X / bitmap.Dpi.Y` | `BitmapDecoder.Create(...).Frames[0].DpiX/DpiY` | ✅ 已存在于 DebugLog，只需加到 FormatMetadata |
| 2 | Image **像素格式** | `bitmap.Format` → `"32-bit BGRA"` 等 | `Frames[0].Format.BitsPerPixel`（PixelFormat 复杂类） | ✅ `PixelFormat` 枚举直接可映射 |
| 3 | Image **物理尺寸 (DIP)** | `bitmap.Size`（Width × Height in DIPs） | 需手动 `PixelWidth / DpiX * 96` | 显示 DPI 感知尺寸与像素尺寸的差异 |

### 6. Parser 已暴露但两个版本都未展示的字段

| # | 字段 | Parser 来源 | 说明 |
|---|------|-----------|------|
| 1 | PE **架构/子系统** | `PeInfo.Architecture` / `.Subsystem` | WPF 只在 header 显示，不在 FormatMetadata |
| 2 | Torrent **分片大小** | `TorrentInfo.PieceSize` | 可直接 `FormatSize(info.PieceSize)` |
| 3 | Torrent **分片数** | `TorrentInfo.PieceCount` | 整数值，直接显示 |

### 综合优先级建议

| 优先级 | 项数 | 标签 | 难度 |
|--------|------|------|------|
| P0 | 2 | Image DPI + 像素格式 | ⭐ — 一行代码，Avalonia 直出 |
| P0 | 5 | Audio BitDepth / Office ModifiedDate / Torrent 创建日期+Tracker数+私有+备注 | ⭐ — 一行 Add |
| P1 | 2 | SQLite 编码+页大小 | ⭐⭐ — 需加 PRAGMA 查询 |
| P1 | 3 | PE 架构/子系统 / Torrent 分片大小+分片数 | ⭐ — 一行 Add |
| P2 | 3 | Image 色深/DPI(WPF方式)/物理尺寸 | ⭐⭐ — Avalonia Bitmap 的 Format 枚举需映射 |

#### 备注

- **Image** (WPF 方式): WPF 用 `System.Windows.Media.Imaging.BitmapDecoder.Create` 获取 BitsPerPixel/DpiX；Avalonia 用 `Bitmap.DecodeToWidth` 解码，DPI/Format 是直接属性，反而更简单 (#5.1-5.3)。
- **Image Avalonia 优势**: Avalonia 的 `Bitmap` 直接暴露 `Dpi` (Vector) 和 `Format` (PixelFormat 枚举)，无需像 WPF 那样走 BitmapDecoder 间接获取。
- **Audio**: `FileFormatInfo.BitDepth` 在 Avalonia 的 FlacParser/RiffParser 中应该已经解析了，只需在 `ShowAudio` 中加 `FormatMetadata.Add` 即可。
- **SQLite**: WPF 从 `SqliteMeta` 获取 TextEncoding/AdditionalInfo；Avalonia 的 `ShowSqlitePreview` 直接连 SQLite 但没读取编码/页大小等元信息，需添加 `PRAGMA page_size` 和 `PRAGMA encoding` 查询。
- **Torrent**: `info.CreationDate`/`info.TrackerCount`/`info.IsPrivate`/`info.AdditionalInfo` 在 Avalonia 的 `TorrentParser.Parse` 输出中应存在，只需在 `ShowTorrent` 中添加。
- **Office**: `info.ModifiedDate` 在 `OfficeParser.Parse` 输出中应存在，只需在 `ShowOffice` 中添加。

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
