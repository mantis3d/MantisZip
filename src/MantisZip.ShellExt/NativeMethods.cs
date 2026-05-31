using System.Runtime.InteropServices;
using System.Text;

namespace MantisZip.ShellExt;

/// <summary>
/// Win32 native methods and structures for Shell extension interop.
/// </summary>
internal static class NativeMethods
{
    // ─── HRESULT ───
    public const int S_OK = 0;
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_INVALIDARG = unchecked((int)0x80070057);

    // ─── CF_HDROP / IDataObject ───
    public const ushort CF_HDROP = 15;
    public const uint DVASPECT_CONTENT = 1;
    public const uint TYMED_HGLOBAL = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct FormatEtc
    {
        public ushort cfFormat;
        public IntPtr ptd;
        public uint dwAspect;
        public int lindex;
        public uint tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STGMEDIUM
    {
        public uint tymed;
        public IntPtr hBitmap;
        public IntPtr unionmember;
        public IntPtr pUnkForRelease;
    }

    [DllImport("ole32.dll", PreserveSig = false)]
    public static extern int GetData(IntPtr pDataObj, ref FormatEtc pFormatEtc, out STGMEDIUM pMedium);

    [DllImport("ole32.dll")]
    public static extern void ReleaseStgMedium(ref STGMEDIUM pMedium);

    // ─── DragQueryFile ───
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern int DragQueryFile(IntPtr hDrop, int iFile, [Out] StringBuilder? lpszFile, int cch);

    // ─── PIDL ───
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, [Out] StringBuilder pszPath);

    public static string? GetPathFromPidl(IntPtr pidl)
    {
        var sb = new StringBuilder(260);
        if (SHGetPathFromIDList(pidl, sb) && sb.Length > 0)
            return sb.ToString();
        return null;
    }

    // ─── Menu ───
    public const uint MIIM_STRING = 0x0040;
    public const uint MIIM_ID = 0x0002;
    public const uint MIIM_FTYPE = 0x0100;
    public const uint MIIM_BITMAP = 0x0080;
    public const uint MIIM_SUBMENU = 0x0004;
    public const uint MFT_STRING = 0x0000;
    public const uint MFS_ENABLED = 0x0000;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint MF_BYPOSITION = 0x0400;
    public const uint MF_POPUP = 0x0010;
    public const uint MF_STRING = 0x0000;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InsertMenuItem(IntPtr hMenu, uint uItem, [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MenuItemInfo lpmii);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    // ─── IContextMenu ───
    public const uint GCS_HELPTEXT = 0x0001;
    public const uint GCS_VERB = 0x0003;

    // ─── Icon loading ───
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern uint ExtractIconEx(string szFileName, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ─── GDI for HICON → HBITMAP conversion ───
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    public const uint DI_NORMAL = 0x0003;
    public const int MenuIconSize = 16; // standard small icon for menus

    /// <summary>
    /// Load the application icon as an HBITMAP for menu display.
    /// Extracts the icon from MantisZip.UI.exe and converts HICON to HBITMAP.
    /// Caller must call DeleteObject() on the returned HBITMAP to avoid leaks.
    /// </summary>
    public static IntPtr LoadAppIconBitmap()
    {
        try
        {
            var exePath = Path.Combine(
                Path.GetDirectoryName(typeof(ContextMenuHandler).Assembly.Location) ?? ".",
                "MantisZip.UI.exe");

            if (!File.Exists(exePath))
                return IntPtr.Zero;

            // Extract the small icon (index 0 = app icon)
            var smallIcons = new IntPtr[1];
            uint count = ExtractIconEx(exePath, 0, null, smallIcons, 1);
            if (count == 0 || smallIcons[0] == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr hIcon = smallIcons[0];
            IntPtr hbmp = ConvertIconToBitmap(hIcon);
            DestroyIcon(hIcon);
            return hbmp;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Convert an HICON to an HBITMAP using GDI.
    /// Caller must DeleteObject() the returned HBITMAP.
    /// </summary>
    private static IntPtr ConvertIconToBitmap(IntPtr hIcon)
    {
        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
        IntPtr hbmp = CreateCompatibleBitmap(hdcScreen, MenuIconSize, MenuIconSize);
        IntPtr hOld = SelectObject(hdcMem, hbmp);
        DrawIconEx(hdcMem, 0, 0, hIcon, MenuIconSize, MenuIconSize, 0, IntPtr.Zero, DI_NORMAL);
        SelectObject(hdcMem, hOld);
        DeleteDC(hdcMem);
        ReleaseDC(IntPtr.Zero, hdcScreen);
        return hbmp;
    }
}

/// <summary>IntPtr extension for HIWORD/LOWORD extraction (match Win32 macros).</summary>
internal static class IntPtrExtensions
{
    public static uint LOWORD(this IntPtr ptr)
    {
        unchecked
        {
            if (IntPtr.Size == 8) // 64-bit
                return (uint)((ulong)ptr & 0xFFFF);
            return (uint)((uint)ptr & 0xFFFF);
        }
    }

    public static uint HIWORD(this IntPtr ptr)
    {
        unchecked
        {
            if (IntPtr.Size == 8) // 64-bit
                return (uint)(((ulong)ptr >> 16) & 0xFFFF);
            return (uint)(((uint)ptr >> 16) & 0xFFFF);
        }
    }
}
