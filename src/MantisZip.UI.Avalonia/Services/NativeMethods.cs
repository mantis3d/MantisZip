using System.Runtime.InteropServices;
using System.Text;

namespace MantisZip.UI.Avalonia.Services;

internal static class NativeMethods
{
    // ── Helper methods ──

    /// <summary>
    /// Gets the window handle under the current cursor position.
    /// Wraps GetCursorPos + WindowFromPoint.
    /// </summary>
    /// <returns>The window handle, or nint.Zero if the cursor position cannot be determined.</returns>
    public static nint GetWindowUnderCursor()
    {
        if (!GetCursorPos(out var pt))
            return nint.Zero;
        return WindowFromPoint(pt);
    }

    /// <summary>
    /// Gets the window class name for the given window handle.
    /// Wraps GetClassName with a reusable StringBuilder.
    /// </summary>
    public static string GetWindowClassName(nint hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // ── user32.dll ──

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT Point);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetClassName(nint hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetParent(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hWnd, uint gaFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    // ── NativeSplash（startup-native-splash 计划）──

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    /// <summary>返回值是窗口所属线程 id；pid 经传出参数。</summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern int FillRect(nint hDC, ref RECT lprc, nint hbr);

    // DPI 上下文值：-4 = PER_MONITOR_AWARE_V2（重复调用失败无碍，仅首个生效方有效）
    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(nint value);

    [DllImport("user32.dll")]
    public static extern int GetDpiForSystem();

    // ── gdi32（splash 文本绘制）──

    [DllImport("gdi32.dll")]
    public static extern uint SetTextColor(nint hdc, uint color);

    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
    public static extern nint CreateFont(int nHeight, int nWidth, int nEscapement, int nOrientation,
        uint fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut,
        uint fdwCharSet, uint fdwOutputPrecision, uint fdwClipPrecision,
        uint fdwQuality, uint fdwPitchAndFamily, string lpszFace);

    // ── NativeSplash 图标 + Win11 圆角 ──

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconEx(string lpszFile, int nIconIndex, nint[] phiconLarge, nint[] phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    public static extern bool DrawIconEx(nint hdc, int xLeft, int yTop, nint hIcon, int cxWidth, int cyHeight, uint istepIfAniCur, nint hbrFlickerFreeDraw, uint diFlags);

    // DestroyIcon 已存在（Cursor API 段）——勿重复声明

    // DPI：DWMWA_WINDOW_CORNER_PREFERENCE = 33，DWMWCP_ROUND = 2（Win10 无此属性，调用失败静默忽略）
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);

    [DllImport("user32.dll")]
    public static extern bool IntersectRect(out RECT lprcDst, ref RECT lprcSrc1, ref RECT lprcSrc2);

    public static RECT GetVirtualScreenRect()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int cx = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int cy = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return new RECT { Left = x, Top = y, Right = x + cx, Bottom = y + cy };
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(nint hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    // 显式 W（A/W 不一致会导致 CreateWindowExW 找不到类，lastErr=1407）
    // lpszClassName 经全局指针传入（nint），绕开 C# string marshaling 不确定性
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "RegisterClassExW")]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW")]
    public static extern nint CreateWindowEx(
        uint dwExStyle, nint lpClassName, nint lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int DrawText(nint hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(nint hdc, int mode);

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(nint hWnd, nint hdcDst, ref POINT pptDst,
        ref SIZE psize, nint hdcSrc, ref POINT pptSrc, uint crKey,
        ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowText(nint hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(nint hWndParent, EnumChildProc lpEnumFunc, nint lParam);

    public delegate bool EnumChildProc(nint hWnd, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClipboardFormatW(string lpszFormat);

    // ── Cursor API（拖拽期间由自实现 IDropSource.GiveFeedback 直接 SetCursor）──

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint LoadCursorFromFile(string lpFileName);

    [DllImport("user32.dll")]
    public static extern nint LoadCursor(nint hInstance, nint lpCursorName);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    public static extern nint SetCursor(nint hCursor);

    // ── gdi32.dll ──

    [DllImport("gdi32.dll")]
    public static extern nint CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    public static extern bool Rectangle(nint hdc, int left, int top, int right, int bottom);

    // ── dwmapi.dll ──

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out RECT pvAttribute, uint cbAttribute);

    // ── kernel32.dll ──

    [DllImport("kernel32.dll")]
    public static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GlobalFree(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nuint GlobalSize(nint hMem);

    [DllImport("kernel32.dll")]
    public static extern void RtlMoveMemory(nint dest, nint src, nuint length);

    // ── ole32.dll ──

    [DllImport("ole32.dll")]
    public static extern int DoDragDrop(
        nint pDataObj,
        nint pDropSource,
        int dwOKEffects,
        out int pdwEffect);

    [DllImport("ole32.dll")]
    public static extern int OleInitialize(nint pvReserved);

    [DllImport("ole32.dll")]
    public static extern void OleUninitialize();

    [DllImport("ole32.dll")]
    public static extern void ReleaseStgMedium(ref STGMEDIUM pMedium);

    // ── Constants ──

    public const uint WS_EX_LAYERED = 0x80000;
    public const uint WS_EX_TRANSPARENT = 0x20;
    public const uint WS_EX_NOACTIVATE = 0x8000000;
    public const uint WS_EX_TOOLWINDOW = 0x80;
    public const uint WS_POPUP = 0x80000000;
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint LWA_ALPHA = 0x2;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static readonly nint HWND_TOPMOST = new(-1);
    public const int SW_SHOW = 5;
    public const int SW_HIDE = 0;
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint SRCCOPY = 0x00CC0020;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_NCCALCSIZE = 0x0083;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint DT_LEFT = 0x0000;
    public const uint DT_CENTER = 0x0001;
    public const uint DT_VCENTER = 0x0004;
    public const uint DT_SINGLELINE = 0x0020;
    public const uint DT_NOPREFIX = 0x0800;
    public const uint DT_CALCRECT = 0x0400;
    public const uint DT_END_ELLIPSIS = 0x8000;
    public const int TRANSPARENT = 1;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;
    public const int SW_SHOWNOACTIVATE = 4;
    public const uint PM_REMOVE = 1;
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    public const uint IDC_ARROW = 32512;
    public const uint DEFAULT_CHARSET = 1;
    public const uint CLEARTYPE_QUALITY = 5;
    public const uint FW_SEMIBOLD = 600;
    public const uint FW_NORMAL = 400;
    public const uint DEFAULT_PITCH = 0;
    public const uint DI_NORMAL = 0x0003;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    // Virtual screen bounds (works across all monitors)
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    public const uint TYMED_HGLOBAL = 1;
    public const uint TYMED_ISTREAM = 4;
    public const uint TYMED_GDI = 16;
    public const uint DV_E_FORMATETC = 0x80040064;
    public const uint DV_E_TYMED = 0x80040069;
    public const uint DV_E_DVASPECT = 0x8004006B;
    public const uint STG_E_MEDIUMFULL = 0x80030070;
    public const uint S_OK = 0;

    // System cursor ID（拖拽光标兜底：标准箭头）
    public const uint OCR_NORMAL = 32512;

    // ── Structs ──

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        public int reserved1;
        public int reserved2;
        public int reserved3;
        public int reserved4;
        public int reserved5;
        public int reserved6;
        public int reserved7;
        public int reserved8;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STGMEDIUM
    {
        public uint tymed;
        public nint unionmember;
        public nint pUnkForRelease;
    }
}
