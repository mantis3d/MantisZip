using System;
using System.Runtime.InteropServices;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Win32 popup window that displays a pre-rendered file tree bitmap
/// following the mouse cursor during a drag operation.
/// Runs on the overlay STA thread, not the Avalonia UI thread.
/// Pure Win32/GDI — no Avalonia controls, no WinForms, no WPF.
/// </summary>
internal class DragPreviewPopup : IDisposable
{
    private nint _hwnd;
    private nint _hBitmap;
    private readonly int _bitmapWidth;
    private readonly int _bitmapHeight;
    private readonly string _summary;
    private readonly int _totalWindowWidth;
    private readonly int _totalWindowHeight;

    /// <summary>Mouse offset from cursor to popup top-left corner.</summary>
    private const int OffsetX = 20;
    private const int OffsetY = 24;

    /// <summary>Height of the summary bar drawn above the bitmap.</summary>
    private const int SummaryBarHeight = 24;

    /// <summary>Stored as field to prevent GC from collecting the delegate.</summary>
    private readonly WndProcDelegate _wndProcDelegate;

    /// <summary>WS_BORDER style value.</summary>
    private const uint WS_BORDER = 0x00800000;

    /// <summary>PM_REMOVE for PeekMessage.</summary>
    private const uint PM_REMOVE = 1;

    /// <summary>
    /// Creates the preview popup window and displays it immediately.
    /// </summary>
    /// <param name="hInstance">HINSTANCE from GetModuleHandle(null).</param>
    /// <param name="data">Pre-rendered bitmap data from DragPreviewBitmapBuilder.</param>
    public DragPreviewPopup(nint hInstance, PreviewBitmapData data)
    {
        _bitmapWidth = data.Width;
        _bitmapHeight = data.Height;
        _summary = data.Summary;
        _totalWindowWidth = _bitmapWidth;
        _totalWindowHeight = _bitmapHeight + SummaryBarHeight;
        _wndProcDelegate = WndProc;

        // 1. Create GDI HBITMAP from pixel data
        _hBitmap = CreateBitmapFromPixels(data.Pixels, _bitmapWidth, _bitmapHeight);
        if (_hBitmap == nint.Zero)
            throw new InvalidOperationException("Failed to create DIB section for preview bitmap");

        // 2. Register window class
        var className = "MantisZipDragPreview";
        var wndClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = nint.Zero,
            hCursor = nint.Zero,
            hbrBackground = nint.Zero,
            lpszMenuName = null,
            lpszClassName = className,
            hIconSm = nint.Zero
        };

        var atom = NativeMethods.RegisterClassEx(ref wndClass);
        if (atom == 0)
            throw new InvalidOperationException("Failed to register DragPreview window class");

        // 3. Create the popup window (layered, no activation, tool window)
        _hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW,
            className,
            "DragPreview",
            NativeMethods.WS_POPUP | WS_BORDER,
            -1000, -1000, // Off-screen initially
            _totalWindowWidth, _totalWindowHeight,
            nint.Zero,
            nint.Zero,
            hInstance,
            nint.Zero);

        if (_hwnd == nint.Zero)
            throw new InvalidOperationException("Failed to create DragPreview window");

        // 4. Add WS_EX_TRANSPARENT for mouse passthrough
        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TRANSPARENT);

        // 5. Set initial opacity (slightly transparent: ~90% opaque)
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 230, NativeMethods.LWA_ALPHA);

        // 6. Show the window
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOW);
    }

    /// <summary>
    /// Reposition the popup to follow the mouse cursor.
    /// </summary>
    public void UpdatePosition(int cursorX, int cursorY)
    {
        if (_hwnd == nint.Zero)
            return;

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            cursorX + OffsetX,
            cursorY + OffsetY,
            _totalWindowWidth,
            _totalWindowHeight,
            NativeMethods.SWP_NOACTIVATE);
    }

    // ── Window procedure ──

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_PAINT:
                return HandlePaint(hWnd);

            case NativeMethods.WM_NCHITTEST:
                // HTTRANSPARENT — mouse clicks pass through to windows beneath
                return new nint(-1);

            case NativeMethods.WM_DESTROY:
                return nint.Zero;

            default:
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private nint HandlePaint(nint hWnd)
    {
        NativeMethods.BeginPaint(hWnd, out var ps);
        var hdc = ps.hdc;

        try
        {
            // 1. Draw the pre-rendered bitmap below the summary bar
            var memDc = NativeMethods.CreateCompatibleDC(hdc);
            if (memDc != nint.Zero)
            {
                var oldBitmap = NativeMethods.SelectObject(memDc, _hBitmap);
                NativeMethods.BitBlt(
                    hdc,
                    0, SummaryBarHeight,
                    _bitmapWidth, _bitmapHeight,
                    memDc,
                    0, 0,
                    NativeMethods.SRCCOPY);
                NativeMethods.SelectObject(memDc, oldBitmap);
                NativeMethods.DeleteDC(memDc);
            }

            // 2. Draw white background for the summary bar
            var whiteBrush = NativeMethods.CreateSolidBrush(0x00FFFFFF);
            if (whiteBrush != nint.Zero)
            {
                var oldBrush = NativeMethods.SelectObject(hdc, whiteBrush);
                NativeMethods.Rectangle(hdc, 0, 0, _totalWindowWidth, SummaryBarHeight);
                NativeMethods.SelectObject(hdc, oldBrush);
                NativeMethods.DeleteObject(whiteBrush);
            }

            // 3. Draw summary text on the bar (transparent background)
            NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
            var textRect = new NativeMethods.RECT
            {
                Left = 4,
                Top = 0,
                Right = _totalWindowWidth - 4,
                Bottom = SummaryBarHeight
            };
            NativeMethods.DrawText(hdc, _summary, _summary.Length, ref textRect, NativeMethods.DT_LEFT);
        }
        finally
        {
            NativeMethods.EndPaint(hWnd, ref ps);
        }

        return nint.Zero;
    }

    // ── Bitmap creation ──

    /// <summary>
    /// Creates a GDI HBITMAP from the BGRA 32bpp pixel data using CreateDIBSection.
    /// The bitmap is top-down (positive height = bottom-up, negative = top-down).
    /// </summary>
    private static nint CreateBitmapFromPixels(byte[] pixels, int width, int height)
    {
        var bmi = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // negative = top-down bitmap (no DIB flip)
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
                biSizeImage = 0,
                biXPelsPerMeter = 0,
                biYPelsPerMeter = 0,
                biClrUsed = 0,
                biClrImportant = 0
            }
        };

        var hdc = NativeMethods.GetDC(nint.Zero);
        nint hBitmap;
        nint bitsPtr;

        try
        {
            hBitmap = NativeMethods.CreateDIBSection(hdc, ref bmi, 0, out bitsPtr, nint.Zero, 0);
            if (hBitmap != nint.Zero && bitsPtr != nint.Zero)
            {
                Marshal.Copy(pixels, 0, bitsPtr, pixels.Length);
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(nint.Zero, hdc);
        }

        return hBitmap;
    }

    // ── Delegate type for the window procedure ──

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    // ── IDisposable ──

    public void Dispose()
    {
        if (_hwnd != nint.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }

        if (_hBitmap != nint.Zero)
        {
            NativeMethods.DeleteObject(_hBitmap);
            _hBitmap = nint.Zero;
        }
    }
}
