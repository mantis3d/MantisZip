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
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    // SHGetImageList 的尺寸档位：JUMBO = 256px，EXTRALARGE = 48px，LARGE = 32px
    private const int SHIL_LARGE = 0x0;
    private const int SHIL_EXTRALARGE = 0x2;
    private const int SHIL_JUMBO = 0x4;
    private const uint ILD_TRANSPARENT = 0x00000001;

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
    /// 取系统图像列表（按尺寸档位）。Shell 的「大图标」都在这里，SHGetFileInfo 的
    /// SHGFI_ICON 最多只给 32px，要 256px 必须走这个入口。
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList? ppv);

    /// <summary>
    /// <c>IImageList</c> 的最小定义：只用到 <c>GetIcon</c>，但它前面 7 个方法的槽位必须按 vtable
    /// 顺序声明（COM 按序号派发，少一个就会调到隔壁方法上）。未使用的方法签名不必准确。
    /// </summary>
    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig]
        int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);

        [PreserveSig]
        int ReplaceIcon(int i, IntPtr hicon, out int pi);

        [PreserveSig]
        int SetOverlayImage(int iImage, int iOverlay);

        [PreserveSig]
        int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);

        [PreserveSig]
        int AddMasked(IntPtr hbmImage, int crMask, out int pi);

        [PreserveSig]
        int Draw(IntPtr pimldp);

        [PreserveSig]
        int Remove(int i);

        [PreserveSig]
        int GetIcon(int i, uint flags, out IntPtr picon);
    }

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
    /// 取系统文件图标的**大尺寸版本**（缩略图卡兜底用）：优先 256px（Jumbo），退 48px、32px。
    /// </summary>
    /// <param name="extension">文件扩展名（含点，如 <c>.mp4</c>）；空表示未知类型。</param>
    /// <returns>大图标位图；非 Windows 或 Shell 未提供时返回 <see langword="null"/>（调用方退回 16px 小图标）。</returns>
    /// <remarks>
    /// <see cref="GetFileIcon"/> 走 <c>SHGFI_SMALLICON</c>（16px），放进 128px 的缩略图卡会被拉伸得又小又糊，
    /// 所以卡片兜底改走 <c>SHGetImageList</c> 的系统图像列表：Jumbo 档是 256px，缩到 96px 显示依然清晰。
    /// </remarks>
    public static Bitmap? GetLargeFileIcon(string extension)
    {
        if (!_isWindows)
            return null;

        var key = "__large__" + (string.IsNullOrEmpty(extension) ? ".unknown" : extension.ToLowerInvariant());
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = GetLargeIconFromShell("file" + extension, FILE_ATTRIBUTE_NORMAL);
        if (icon != null)
            _cache[key] = icon;

        return icon;
    }

    /// <summary>取系统文件夹图标的**大尺寸版本**（缩略图卡兜底用），语义同 <see cref="GetLargeFileIcon"/>。</summary>
    public static Bitmap? GetLargeFolderIcon()
    {
        if (!_isWindows)
            return null;

        const string key = "__large__folder__";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = GetLargeIconFromShell("folder", FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_NORMAL);
        if (icon != null)
            _cache[key] = icon;

        return icon;
    }

    /// <summary>
    /// Get the real shell icon of a drive root (e.g. <c>C:\</c>).
    /// 硬盘 / 可移动磁盘 / 网络驱动器 / 光驱 由系统自动区分，与资源管理器一致。
    /// Results are cached per root path.
    /// </summary>
    /// <param name="rootPath">驱动器根路径（如 <c>C:\</c>）</param>
    public static Bitmap? GetDriveIcon(string rootPath)
    {
        if (!_isWindows || string.IsNullOrEmpty(rootPath))
            return null;

        var key = "__drive__" + rootPath;
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        // 必须去掉 SHGFI_USEFILEATTRIBUTES：带该 flag 时 Shell 只按「文件名/属性」蒙版返回
        // 通用图标，磁盘（无扩展名）会退化成文件夹图标，拿不到各类驱动器各自的真实图标。
        var icon = GetIconFromShell(rootPath, 0, useFileAttributes: false);
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

    private static Bitmap? GetIconFromShell(string path, uint attributes, bool useFileAttributes = true)
    {
        var shfi = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_SMALLICON;
        // 虚拟文件（不存在的文件名/扩展名）必须带 SHGFI_USEFILEATTRIBUTES，磁盘等真实路径则不能带
        if (useFileAttributes)
            flags |= SHGFI_USEFILEATTRIBUTES;

        var result = SHGetFileInfo(path, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            return ConvertIcon(shfi.hIcon);
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }

    /// <summary>
    /// 从系统图像列表取大尺寸图标：依次尝试 Jumbo(256) → ExtraLarge(48) → Large(32)，
    /// 任何一档取到就返回；全部失败返回 <see langword="null"/>。
    /// </summary>
    private static Bitmap? GetLargeIconFromShell(string path, uint attributes)
    {
        var shfi = new SHFILEINFO();
        // 只要索引：真正的像素由 SHGetImageList 按尺寸档位提供
        var flags = SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES;
        if (SHGetFileInfo(path, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags) == IntPtr.Zero)
            return null;

        foreach (var size in new[] { SHIL_JUMBO, SHIL_EXTRALARGE, SHIL_LARGE })
        {
            var iid = typeof(IImageList).GUID;
            IImageList? list = null;
            try
            {
                if (SHGetImageList(size, ref iid, out list) != 0 || list is null)
                    continue;

                if (list.GetIcon(shfi.iIcon, ILD_TRANSPARENT, out var hIcon) != 0 || hIcon == IntPtr.Zero)
                    continue;

                try
                {
                    return ConvertIcon(hIcon);
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
            catch
            {
                // 某一档不可用（老系统 / 类型无大图标）就继续试下一档
            }
            finally
            {
                if (list is not null && Marshal.IsComObject(list))
                    Marshal.ReleaseComObject(list);
            }
        }

        return null;
    }

    /// <summary>HICON → Avalonia <see cref="Bitmap"/>（经 System.Drawing 转 PNG，保留 alpha）。</summary>
    private static Bitmap? ConvertIcon(IntPtr hIcon)
    {
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(hIcon);
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
    }
}
