# Conflict Dialog Icons Design

**Date**: 2026-06-12
**Status**: ✅ Implemented

## Overview

Add colorful Emoji icons (via `Emoji.Wpf` library) to buttons in two file conflict dialogs:
- `ConflictDialog` — extract conflict (disk vs archive file comparison, 5 buttons)
- `CompressConflictDialog` — compress target conflict (4 buttons)

Uses the same `Emoji.Wpf` library already used by the main toolbar (🆕, 📂, 📤, 📥, 🤖 etc.).

## Layout

**Icon position**: Above text (vertical `StackPanel` inside each button)

**Button height**: Auto (no longer fixed `Height="28"`), estimated ~42–46px

**Button width**: 
- Standard buttons: `MinWidth="70"` or `MinWidth="80"`
- "添加到压缩包" button: `MinWidth="100"` (wide button for 6-char text)

## Icon Library

**Library**: `Emoji.Wpf` (NuGet package, already referenced in project)

**Namespace**: `xmlns:emoji="clr-namespace:Emoji.Wpf;assembly=Emoji.Wpf"`

**Control**: `<emoji:TextBlock Text="🔄" FontSize="16" HorizontalAlignment="Center"/>`

## Button Details

### ConflictDialog (5 buttons)

```
  ┌───────────┐  ┌───────────┐  ┌───────────┐  ┌───────────┐  ┌───────────┐
  │    🔄     │  │    🕐     │  │    ⬇️     │  │    ✏️     │  │    ⏭️    │
  │    覆盖    │  │  覆盖较旧  │  │  覆盖较小  │  │   重命名   │  │    跳过   │
  └───────────┘  └───────────┘  └───────────┘  └───────────┘  └───────────┘
```

| Button | Localization Key | Emoji | Semantic |
|--------|-----------------|-------|----------|
| 覆盖 | `CompressConflict_Overwrite` | 🔄 | Cycle/replace |
| 覆盖较旧 | `Conflict_Btn_OverwriteOlder` | 🕐 | Time-based decision |
| 覆盖较小 | `Conflict_Btn_OverwriteSmaller` | ⬇️ | Smaller = overwrite |
| 重命名 | `Conflict_Btn_Rename` | ✏️ | Edit/rename |
| 跳过 | `Error_Skip` | ⏭️ | Skip forward |

### CompressConflictDialog (4 buttons)

```
  ┌───────────┐  ┌───────────────┐  ┌───────────┐  ┌───────────┐
  │    🔄     │  │      ➕       │  │    ✏️     │  │    ❌     │
  │    覆盖    │  │  添加到压缩包   │  │   重命名   │  │    跳过   │
  └───────────┘  └───────────────┘  └───────────┘  └───────────┘
```

| Button | Localization Key | Emoji | Semantic |
|--------|-----------------|-------|----------|
| 覆盖 | `CompressConflict_Overwrite` | 🔄 | Cycle/replace |
| 添加到压缩包 | `CompressConflict_Add` | ➕ | Add to archive |
| 重命名 | `CompressConflict_Rename` | ✏️ | Edit/rename |
| 跳过/取消 | `Error_Skip` | ❌ | Cancel |

## XAML Implementation Pattern

```xml
<Button Height="Auto" Padding="6,4" MinWidth="70" Click="Overwrite_Click"
        Background="{StaticResource Theme_ButtonBg}"
        BorderBrush="{StaticResource Theme_Border}" BorderThickness="1">
    <StackPanel Orientation="Vertical" HorizontalAlignment="Center">
        <emoji:TextBlock Text="🔄" FontSize="16" HorizontalAlignment="Center"/>
        <TextBlock Text="{l:L CompressConflict_Overwrite}" FontSize="11"
                   Foreground="{StaticResource Theme_TextPrimary}"
                   HorizontalAlignment="Center" Margin="0,2,0,0"/>
    </StackPanel>
</Button>
```

## Files Modified

1. `src/MantisZip.UI/Dialogs/ConflictDialog.xaml` — added emoji namespace + changed 5 buttons
2. `src/MantisZip.UI/Dialogs/CompressConflictDialog.xaml` — added emoji namespace + changed 4 buttons

No changes to `.cs` files, localization strings, or resource files.

## Theme Compliance

All new TextBlocks within buttons use `Theme_TextPrimary` for text foreground — existing theme resource key. No new theme resources needed.

## Verification

- ✅ `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` — 0 errors, 0 new warnings
- No new dependencies (Emoji.Wpf already in project)
