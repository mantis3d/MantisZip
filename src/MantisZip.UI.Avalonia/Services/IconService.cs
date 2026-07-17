using Avalonia.Media.Imaging;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Icon service with Win32 native icon priority and SkiaSharp fallback.
/// On Windows: uses SHGetFileInfo (Win32IconProvider) for native system icons.
/// On other platforms: falls back to IconProvider (SkiaSharp-drawn).
/// </summary>
internal static class IconService
{
    /// <summary>
    /// Get a file type icon for the given file extension.
    /// On Windows, returns the native system icon; on other platforms,
    /// falls back to SkiaSharp-generated icons.
    /// </summary>
    /// <param name="extension">File extension including the dot (e.g. ".txt", ".zip").</param>
    /// <returns>16x16 bitmap icon, or null if generation fails.</returns>
    public static Bitmap? GetFileIcon(string extension)
    {
        // Win32 priority on Windows
        if (Win32IconProvider.IsSupported)
        {
            var icon = Win32IconProvider.GetFileIcon(extension);
            if (icon != null)
                return icon;
        }
        // Fallback to SkiaSharp-drawn icons
        return IconProvider.GetFileIcon(extension);
    }

    /// <summary>
    /// Get a folder icon.
    /// On Windows, returns the native system folder icon; on other platforms,
    /// falls back to SkiaSharp-generated folder icon.
    /// </summary>
    public static Bitmap? GetFolderIcon()
    {
        if (Win32IconProvider.IsSupported)
        {
            var icon = Win32IconProvider.GetFolderIcon();
            if (icon != null)
                return icon;
        }
        return IconProvider.GetFolderIcon();
    }

    /// <summary>
    /// Clear all icon caches.
    /// </summary>
    public static void ClearCache()
    {
        Win32IconProvider.ClearCache();
        IconProvider.ClearCache();
    }
}
