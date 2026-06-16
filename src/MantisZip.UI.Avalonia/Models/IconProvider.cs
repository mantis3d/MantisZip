using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// Cross-platform icon provider that generates file-type icons using SkiaSharp.
/// Replaces Win32 SHGetFileInfo-based IconService.
/// No external files required — icons are drawn programmatically.
/// </summary>
public static class IconProvider
{
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private const int IconSize = 16;

    /// <summary>
    /// Extension-to-category mapping.
    /// </summary>
    private static readonly Dictionary<string, string> ExtensionCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        // Archives
        [".zip"] = "archive",
        [".7z"] = "archive",
        [".rar"] = "archive",
        [".tar"] = "archive",
        [".gz"] = "archive",
        [".tar.gz"] = "archive",
        [".tgz"] = "archive",
        [".bz2"] = "archive",
        [".xz"] = "archive",
        // Text
        [".txt"] = "text",
        [".log"] = "text",
        [".md"] = "text",
        [".xml"] = "code",
        [".json"] = "code",
        [".yaml"] = "code",
        [".yml"] = "code",
        [".ini"] = "code",
        [".cfg"] = "code",
        // Code / PE
        [".exe"] = "code",
        [".dll"] = "code",
        [".sys"] = "code",
        [".ocx"] = "code",
        [".cs"] = "code",
        [".py"] = "code",
        [".js"] = "code",
        [".ts"] = "code",
        [".html"] = "code",
        [".htm"] = "code",
        [".css"] = "code",
        [".bat"] = "code",
        [".ps1"] = "code",
        [".sh"] = "code",
        // Images
        [".png"] = "image",
        [".jpg"] = "image",
        [".jpeg"] = "image",
        [".gif"] = "image",
        [".webp"] = "image",
        [".bmp"] = "image",
        [".ico"] = "image",
        [".svg"] = "image",
        [".tiff"] = "image",
        [".tif"] = "image",
        // Audio
        [".mp3"] = "audio",
        [".wav"] = "audio",
        [".flac"] = "audio",
        [".ogg"] = "audio",
        [".aac"] = "audio",
        [".wma"] = "audio",
        // Video
        [".mp4"] = "video",
        [".mkv"] = "video",
        [".avi"] = "video",
        [".mov"] = "video",
        [".wmv"] = "video",
        [".flv"] = "video",
        [".webm"] = "video",
        // Documents
        [".pdf"] = "document",
        [".doc"] = "document",
        [".docx"] = "document",
        [".xls"] = "spreadsheet",
        [".xlsx"] = "spreadsheet",
        [".ppt"] = "presentation",
        [".pptx"] = "presentation",
        [".rtf"] = "document",
        // Database
        [".sqlite"] = "database",
        [".db"] = "database",
        [".sqlite3"] = "database",
        // Disk images
        [".iso"] = "disc",
        [".img"] = "disc",
        // Torrent
        [".torrent"] = "torrent",
        // Fonts
        [".ttf"] = "font",
        [".otf"] = "font",
        [".woff"] = "font",
        [".woff2"] = "font",
    };

    /// <summary>
    /// Cache for category colors.
    /// </summary>
    private static readonly Dictionary<string, SKColor> CategoryColor = new()
    {
        ["archive"] = new SKColor(0xE8, 0xA0, 0x38),     // Amber
        ["text"] = new SKColor(0x60, 0xA0, 0xE0),         // Blue
        ["code"] = new SKColor(0x80, 0x60, 0xC0),         // Purple
        ["image"] = new SKColor(0x40, 0xC0, 0x80),        // Green
        ["audio"] = new SKColor(0xE0, 0x70, 0xA0),        // Pink
        ["video"] = new SKColor(0xD0, 0x50, 0x50),        // Red
        ["document"] = new SKColor(0x50, 0x90, 0xD0),     // Steel Blue
        ["spreadsheet"] = new SKColor(0x50, 0xB0, 0x60),  // Green
        ["presentation"] = new SKColor(0xE0, 0x70, 0x40), // Orange
        ["database"] = new SKColor(0x70, 0x90, 0xB0),     // Slate
        ["disc"] = new SKColor(0x90, 0x80, 0xC0),         // Lavender
        ["torrent"] = new SKColor(0x60, 0xB0, 0x90),      // Teal
        ["font"] = new SKColor(0xB0, 0x80, 0x60),         // Brown
        ["folder"] = new SKColor(0xE0, 0xB0, 0x40),       // Gold
    };

    public static Bitmap? GetFileIcon(string extension)
    {
        var key = string.IsNullOrEmpty(extension) ? ".unknown" : extension.ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var bitmap = GenerateIcon(extension);
        _cache[key] = bitmap;
        return bitmap;
    }

    public static Bitmap? GetFolderIcon()
    {
        const string key = "__folder__";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var bitmap = GenerateFolderIcon();
        _cache[key] = bitmap;
        return bitmap;
    }

    public static void ClearCache() => _cache.Clear();

    private static Bitmap? GenerateIcon(string extension)
    {
        var category = GetCategory(extension);
        var color = CategoryColor.GetValueOrDefault(category, new SKColor(0x90, 0x90, 0x90));

        using var surface = SKSurface.Create(new SKImageInfo(IconSize, IconSize));
        var canvas = surface.Canvas;

        // Draw file shape (rounded rectangle with folded corner)
        DrawFileShape(canvas, IconSize, color);

        // Draw category-specific symbol
        DrawCategorySymbol(canvas, IconSize, category);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }

    private static Bitmap? GenerateFolderIcon()
    {
        var color = CategoryColor.GetValueOrDefault("folder", new SKColor(0xE0, 0xB0, 0x40));

        using var surface = SKSurface.Create(new SKImageInfo(IconSize, IconSize));
        var canvas = surface.Canvas;

        // Draw folder shape
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Folder back
        var backPath = new SKPath();
        backPath.MoveTo(0, 4);
        backPath.LineTo(6, 4);
        backPath.LineTo(8, 6);
        backPath.LineTo(16, 6);
        backPath.LineTo(16, 14);
        backPath.LineTo(0, 14);
        backPath.Close();
        canvas.DrawPath(backPath, paint);

        // Folder tab
        using var tabPaint = new SKPaint
        {
            Color = color.WithAlpha(180),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var tabPath = new SKPath();
        tabPath.MoveTo(0, 2);
        tabPath.LineTo(6, 2);
        tabPath.LineTo(7, 4);
        tabPath.LineTo(0, 4);
        tabPath.Close();
        canvas.DrawPath(tabPath, tabPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }

    private static void DrawFileShape(SKCanvas canvas, int size, SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        var r = new SKRoundRect(new SKRect(0, 0, size, size), 2);
        canvas.DrawRoundRect(r, paint);
    }

    private static void DrawCategorySymbol(SKCanvas canvas, int size, string category)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(200),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };

        var cx = size / 2f;
        var cy = size / 2f;

        switch (category)
        {
            case "archive":
                // Box with zipper line
                canvas.DrawRect(new SKRect(cx - 4, cy - 3, cx + 4, cy + 3), paint);
                canvas.DrawLine(cx - 3, cy, cx + 3, cy, paint);
                break;

            case "text":
                // Document lines
                canvas.DrawLine(cx - 4, cy - 3, cx + 4, cy - 3, paint);
                canvas.DrawLine(cx - 4, cy, cx + 4, cy, paint);
                canvas.DrawLine(cx - 4, cy + 3, cx + 4, cy + 3, paint);
                break;

            case "code":
                // Angle brackets
                using (var codePaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(200),
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f
                })
                {
                    canvas.DrawLine(cx - 3, cy - 3, cx, cy, codePaint);
                    canvas.DrawLine(cx, cy, cx - 3, cy + 3, codePaint);
                    canvas.DrawLine(cx + 3, cy - 3, cx, cy, codePaint);
                    canvas.DrawLine(cx, cy, cx + 3, cy + 3, codePaint);
                }
                break;

            case "image":
                // Mountain + sun
                using (var imgPaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(200),
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f
                })
                {
                    canvas.DrawLine(cx - 5, cy + 3, cx - 1, cy - 2, imgPaint);
                    canvas.DrawLine(cx - 1, cy - 2, cx + 3, cy + 2, imgPaint);
                    canvas.DrawLine(cx + 3, cy + 2, cx + 5, cy - 1, imgPaint);
                    canvas.DrawCircle(cx + 3, cy - 3, 1.5f, imgPaint);
                }
                break;

            case "audio":
                // Music note
                using (var notePaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(200),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                })
                {
                    canvas.DrawCircle(cx - 1, cy + 2, 2, notePaint);
                    var notePath = new SKPath();
                    notePath.MoveTo(cx - 1, cy + 2);
                    notePath.LineTo(cx - 1, cy - 4);
                    notePath.LineTo(cx + 4, cy - 3);
                    notePath.LineTo(cx + 4, cy - 1);
                    notePath.LineTo(cx - 1, cy - 2);
                    notePath.Close();
                    canvas.DrawPath(notePath, notePaint);
                    canvas.DrawCircle(cx + 4, cy, 2, notePaint);
                }
                break;

            case "video":
                // Play triangle
                using (var playPaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(200),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                })
                {
                    var playPath = new SKPath();
                    playPath.MoveTo(cx - 2, cy - 3);
                    playPath.LineTo(cx + 3, cy);
                    playPath.LineTo(cx - 2, cy + 3);
                    playPath.Close();
                    canvas.DrawPath(playPath, playPaint);
                }
                break;

            case "document":
                // Page with text
                canvas.DrawLine(cx - 3, cy - 3, cx + 3, cy - 3, paint);
                canvas.DrawLine(cx - 3, cy, cx + 3, cy, paint);
                canvas.DrawLine(cx - 3, cy + 3, cx + 1, cy + 3, paint);
                break;

            case "spreadsheet":
                // Grid
                canvas.DrawLine(cx - 4, cy - 3, cx + 4, cy - 3, paint);
                canvas.DrawLine(cx - 4, cy, cx + 4, cy, paint);
                canvas.DrawLine(cx - 4, cy + 3, cx + 4, cy + 3, paint);
                canvas.DrawLine(cx - 2, cy - 4, cx - 2, cy + 4, paint);
                canvas.DrawLine(cx + 2, cy - 4, cx + 2, cy + 4, paint);
                break;

            case "presentation":
                // Bar chart
                using (var chartPaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(200),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                })
                {
                    canvas.DrawRect(new SKRect(cx - 4, cy + 1, cx - 2, cy + 4), chartPaint);
                    canvas.DrawRect(new SKRect(cx - 1, cy - 1, cx + 1, cy + 4), chartPaint);
                    canvas.DrawRect(new SKRect(cx + 2, cy - 3, cx + 4, cy + 4), chartPaint);
                }
                break;

            case "database":
                // Cylinder
                using (var dbPaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(200),
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f
                })
                {
                    canvas.DrawOval(new SKRect(cx - 4, cy - 3, cx + 4, cy - 1), dbPaint);
                    canvas.DrawLine(cx - 4, cy - 2, cx - 4, cy + 3, dbPaint);
                    canvas.DrawLine(cx + 4, cy - 2, cx + 4, cy + 3, dbPaint);
                    canvas.DrawArc(new SKRect(cx - 4, cy + 1, cx + 4, cy + 5), 0, 180, false, dbPaint);
                }
                break;

            case "disc":
                // Disc
                using (var discPaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(200),
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f
                })
                {
                    canvas.DrawCircle(cx, cy, 5, discPaint);
                    canvas.DrawCircle(cx, cy, 2, discPaint);
                }
                break;

            case "torrent":
                // Magnet
                using (var magnetPaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(200),
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f
                })
                {
                    // U-shape
                    canvas.DrawLine(cx - 4, cy - 3, cx - 4, cy + 2, magnetPaint);
                    canvas.DrawLine(cx + 4, cy - 3, cx + 4, cy + 2, magnetPaint);
                    var arcRect = new SKRect(cx - 4, cy - 1, cx + 4, cy + 5);
                    canvas.DrawArc(arcRect, 0, 180, false, magnetPaint);
                    // Dots at ends
                    using var dotPaint = new SKPaint
                    {
                        Color = SKColors.White.WithAlpha(200),
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill
                    };
                    canvas.DrawCircle(cx - 4, cy - 3, 1.5f, dotPaint);
                    canvas.DrawCircle(cx + 4, cy - 3, 1.5f, dotPaint);
                }
                break;

            case "font":
                // Letter F
                using (var fontPaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(200),
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f
                })
                {
                    canvas.DrawLine(cx - 3, cy - 3, cx - 3, cy + 4, fontPaint);
                    canvas.DrawLine(cx - 3, cy - 3, cx + 3, cy - 3, fontPaint);
                    canvas.DrawLine(cx - 3, cy + 1, cx + 2, cy + 1, fontPaint);
                }
                break;

            default:
                // Generic — small circle
                using (var genericPaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(180),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                })
                {
                    canvas.DrawCircle(cx, cy, 2, genericPaint);
                }
                break;
        }
    }

    private static string GetCategory(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return "unknown";
        return ExtensionCategory.GetValueOrDefault(extension.ToLowerInvariant(), "unknown");
    }
}
