using System.Diagnostics;
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
        public IntPtr pUnkForRelease;
    }

    // IDataObject is a COM interface, not a DLL export
    // — use Marshal.GetObjectForIUnknown + COM interop instead

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

    // ─── IContextMenu GetCommandString flags ───
    public const uint GCS_VERBA = 0x0000;
    public const uint GCS_VERBW = 0x0004;
    public const uint GCS_HELPTEXTA = 0x0001;
    public const uint GCS_HELPTEXTW = 0x0005;

    // ─── Debug logging (OutputDebugString) ───
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern void OutputDebugString(string lpOutputString);

    // ─── GDI cleanup (for menu icon HBITMAPs) ───
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

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

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    public const uint DI_NORMAL = 0x0003;

    [DllImport("user32.dll")]
    public static extern IntPtr CreateIconFromResourceEx(byte[] pbIconBits, uint cbIconBits, [MarshalAs(UnmanagedType.Bool)] bool fIcon, uint dwVer, int cxDesired, int cyDesired, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ─── DIB section (for alpha-transparent bitmaps) ───
    public const uint DIB_RGB_COLORS = 0;

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public uint[] bmiColors;
    }
}

/// <summary>IDataObject COM interface — for calling GetData from IShellExtInit::Initialize.</summary>
[ComImport, Guid("0000010E-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDataObjectCom
{
    [PreserveSig]
    int GetData(ref NativeMethods.FormatEtc pFormatEtc, out NativeMethods.STGMEDIUM pMedium);
    [PreserveSig]
    int GetDataHere(ref NativeMethods.FormatEtc pFormatEtc, ref NativeMethods.STGMEDIUM pMedium);
    [PreserveSig]
    int QueryGetData(ref NativeMethods.FormatEtc pFormatEtc);
    [PreserveSig]
    int GetCanonicalFormatEtc(ref NativeMethods.FormatEtc pFormatEtcIn, out NativeMethods.FormatEtc pFormatEtcOut);
    [PreserveSig]
    int SetData(ref NativeMethods.FormatEtc pFormatEtc, ref NativeMethods.STGMEDIUM pMedium, [MarshalAs(UnmanagedType.Bool)] bool fRelease);
    [PreserveSig]
    int EnumFormatEtc(uint dwDirection, out IntPtr ppenumFormatEtc);
    [PreserveSig]
    int DAdvise(ref NativeMethods.FormatEtc pFormatEtc, uint advf, IntPtr pAdvSink, out uint pdwConnection);
    [PreserveSig]
    int DUnadvise(uint dwConnection);
    [PreserveSig]
    int EnumDAdvise(out IntPtr ppenumAdvise);
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
