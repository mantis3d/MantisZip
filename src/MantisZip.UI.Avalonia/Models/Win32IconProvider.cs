using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// Uses Windows Shell API (SHGetFileInfo) to retrieve native system file icons.
/// Falls back to IconProvider (SkiaSharp-drawn) on non-Windows platforms.
/// Supports virtual/nonexistent files via SHGFI_USEFILEATTRIBUTES flag.
/// </summary>
internal static class Win32IconProvider
{
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly bool _isWindows =
        OperatingSystem.IsWindows();

    /// <summary>
    /// Whether Win32 Shell API is supported (Windows only).
    /// </summary>
    public static bool IsSupported => _isWindows;

    /// <summary>
    /// Get the system file icon for the given file extension.
    /// Results are cached.
    /// </summary>
    public static Bitmap? GetFileIcon(string extension)
    {
        if (!_isWindows)
            return null;

        var key = string.IsNullOrEmpty(extension) ? ".unknown" : extension.ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = LoadIcon(extension);
        if (icon != null)
            _cache[key] = icon;

        return icon;
    }

    /// <summary>
    /// Get the system folder icon.
    /// </summary>
    public static Bitmap? GetFolderIcon()
    {
        if (!_isWindows)
            return null;

        const string key = "__folder__";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = LoadFolderIcon();
        if (icon != null)
            _cache[key] = icon;

        return icon;
    }

    /// <summary>
    /// Clear the icon cache.
    /// </summary>
    public static void ClearCache() => _cache.Clear();

    private static Bitmap? LoadIcon(string extension)
    {
        var sampleName = "file" + extension;
        return GetIconFromShell(sampleName, FILE_ATTRIBUTE_NORMAL);
    }

    private static Bitmap? LoadFolderIcon()
    {
        return GetIconFromShell("folder", FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_NORMAL);
    }

    private static Bitmap? GetIconFromShell(string path, uint attributes)
    {
        var shfi = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | SHGFI_SMALLICON;

        var result = SHGetFileInfo(path, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            // Convert HICON to Avalonia Bitmap via System.Drawing
            using var icon = System.Drawing.Icon.FromHandle(shfi.hIcon);
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }
}
