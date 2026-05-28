using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MantisZip.UI;

/// <summary>
/// 使用 Windows Shell API (SHGetFileInfo) 获取系统文件图标。
/// 支持不存在的虚拟文件（通过 SHGFI_USEFILEATTRIBUTES 标志），适合压缩包内文件。
/// </summary>
internal static class SystemIconHelper
{
    private static readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// 获取文件扩展名对应的系统图标（16x16）。
    /// 结果被缓存，相同扩展名只调用一次 SHGetFileInfo。
    /// </summary>
    public static ImageSource GetFileIcon(string extension)
    {
        var key = string.IsNullOrEmpty(extension) ? ".unknown" : extension.ToLowerInvariant();

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        App.LogDebug("SystemIconHelper: cache miss for '{0}', loading from shell", key);
        var icon = LoadIcon(extension);
        if (icon != null)
        {
            _cache[key] = icon;
            App.LogDebug("SystemIconHelper: loaded icon for '{0}'", key);
        }
        else
        {
            App.LogDebug("SystemIconHelper: SHGetFileInfo returned null for '{0}', using fallback", key);
        }
        return icon ?? GetFallbackIcon();
    }

    /// <summary>
    /// 获取文件夹图标（16x16）。
    /// </summary>
    public static ImageSource GetFolderIcon()
    {
        const string key = "__folder__";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = LoadFolderIcon();
        if (icon != null)
        {
            _cache[key] = icon;
        }
        return icon ?? GetFallbackIcon();
    }

    private static ImageSource? LoadIcon(string extension)
    {
        var sampleName = "file" + extension;
        return GetIconFromShell(sampleName, FILE_ATTRIBUTE_NORMAL);
    }

    private static ImageSource? LoadFolderIcon()
    {
        return GetIconFromShell("folder", FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_NORMAL);
    }

    private static ImageSource? GetIconFromShell(string path, uint attributes)
    {
        var shfi = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | SHGFI_SMALLICON;

        var result = SHGetFileInfo(path, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var bs = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }

    private static ImageSource? _fallback;
    private static ImageSource GetFallbackIcon()
    {
        if (_fallback != null)
            return _fallback;

        // 用 .txt 作为保底图标
        _fallback = LoadIcon(".txt") ?? LoadIcon(".bin") ?? LoadIcon("");
        return _fallback ?? throw new InvalidOperationException("无法加载任何系统图标");
    }

    /// <summary>
    /// 清空图标缓存（系统主题变更时调用）
    /// </summary>
    public static void ClearCache() => _cache.Clear();
}
