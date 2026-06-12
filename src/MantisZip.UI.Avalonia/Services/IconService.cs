using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 使用 Windows Shell API (SHGetFileInfo) 获取系统文件图标。
/// 缓存 ConcurrentDictionary，返回 Avalonia Bitmap。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class IconService
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

    public static Bitmap? GetFileIcon(string extension)
    {
        var key = string.IsNullOrEmpty(extension) ? ".unknown" : extension.ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = LoadIcon(extension);
        _cache[key] = icon;
        return icon;
    }

    public static Bitmap? GetFolderIcon()
    {
        const string key = "__folder__";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = LoadFolderIcon();
        _cache[key] = icon;
        return icon;
    }

    private static Bitmap? LoadIcon(string extension)
        => GetIconFromShell("file" + extension, FILE_ATTRIBUTE_NORMAL);

    private static Bitmap? LoadFolderIcon()
        => GetIconFromShell("folder", FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_NORMAL);

    private static Bitmap? GetIconFromShell(string path, uint attributes)
    {
        var shfi = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | SHGFI_SMALLICON;
        var result = SHGetFileInfo(path, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            // Convert HICON to Avalonia Bitmap via System.Drawing.Icon
            using var icon = System.Drawing.Icon.FromHandle(shfi.hIcon);
            using var ms = new MemoryStream();
            icon.Save(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }

    public static void ClearCache() => _cache.Clear();
}
