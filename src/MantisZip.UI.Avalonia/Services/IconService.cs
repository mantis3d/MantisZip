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
    /// Get the native icon of a drive root (e.g. <c>C:\</c>) so hard disks, removable
    /// disks, network drives and optical drives look like they do in Explorer.
    /// Windows only — returns null on other platforms, where the caller falls back to
    /// the vector <c>IconDrive</c> resource.
    /// </summary>
    /// <param name="rootPath">驱动器根路径（如 <c>C:\</c>）</param>
    public static Bitmap? GetDriveIcon(string rootPath)
    {
        if (!Win32IconProvider.IsSupported)
            return null;

        return Win32IconProvider.GetDriveIcon(rootPath);
    }

    /// <summary>
    /// 取文件类型图标的**大尺寸版本**，专供缩略图卡 / 内容行兜底使用（96px 显示）。
    /// </summary>
    /// <param name="extension">File extension including the dot (e.g. ".mp4").</param>
    /// <returns>
    /// 256px（Jumbo）优先，退 48px / 32px；**非 Windows 返回 <see langword="null"/>**——
    /// SkiaSharp 兜底图标是按 16px 绘制的，拉伸到 96px 只会更糊，不如让调用方继续显示 16px 原尺寸。
    /// </returns>
    public static Bitmap? GetLargeFileIcon(string extension)
    {
        if (!Win32IconProvider.IsSupported)
            return null;

        return Win32IconProvider.GetLargeFileIcon(extension);
    }

    /// <summary>取文件夹图标的**大尺寸版本**，语义同 <see cref="GetLargeFileIcon"/>。</summary>
    public static Bitmap? GetLargeFolderIcon()
    {
        if (!Win32IconProvider.IsSupported)
            return null;

        return Win32IconProvider.GetLargeFolderIcon();
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
