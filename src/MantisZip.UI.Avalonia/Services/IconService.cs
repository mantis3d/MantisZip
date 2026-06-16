using Avalonia.Media.Imaging;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Cross-platform icon service using SkiaSharp-generated file type icons.
/// Replaces the old Win32 SHGetFileInfo-based implementation.
/// </summary>
internal static class IconService
{
    /// <summary>
    /// Get a file type icon for the given file extension.
    /// </summary>
    /// <param name="extension">File extension including the dot (e.g. ".txt", ".zip").</param>
    /// <returns>16x16 bitmap icon, or null if generation fails.</returns>
    public static Bitmap? GetFileIcon(string extension)
        => IconProvider.GetFileIcon(extension);

    /// <summary>
    /// Get a folder icon.
    /// </summary>
    public static Bitmap? GetFolderIcon()
        => IconProvider.GetFolderIcon();

    /// <summary>
    /// Clear the icon cache.
    /// </summary>
    public static void ClearCache() => IconProvider.ClearCache();
}
