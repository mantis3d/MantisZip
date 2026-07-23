using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Controls an existing overlay window (Avalonia Window) via Win32 API from a background thread.
/// The window itself is created by Avalonia on the UI thread; this class only does cross-thread
/// operations: UpdateLayeredWindow (position + size + content in one atomic call).
/// </summary>
internal class OverlayController : IDisposable
{
    private nint _hwnd;
    private Thread? _trackingThread;
    private CancellationTokenSource? _cts;
    private readonly ManualResetEvent _stopped = new(false);

    // Tracking state
    private uint _currentColor = 0x00808080; // BGR gray (default for no target)
    private DropTargetDetector.DropTargetStatus _currentStatus;
    private readonly object _stateLock = new();

    public OverlayController(nint hwnd)
    {
        _hwnd = hwnd;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();

        _trackingThread = new Thread(RunTrackingLoop)
        {
            Name = "OverlayTracking",
            IsBackground = true
        };
        _trackingThread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        if (!_stopped.WaitOne(TimeSpan.FromMilliseconds(200)))
        {
            Debug.WriteLine("[Overlay] Thread still running (non-blocking exit)");
        }
        _cts?.Dispose();
        _cts = null;
        _hwnd = nint.Zero;
    }

    public void Dispose() => Stop();

    private void RunTrackingLoop()
    {
        App.DebugLog("[Overlay] Tracking thread started");

        try
        {
            while (!_cts!.IsCancellationRequested)
            {
                try
                {
                    UpdatePosition();
                }
                catch (Exception ex)
                {
                    App.DebugLog($"[Overlay] UpdatePosition CRASHED: {ex.GetType().Name}: {ex.Message}");
                }
                Thread.Sleep(100);
            }
        }
        catch (ThreadAbortException) { }
        catch (Exception ex)
        {
            App.DebugLog($"[Overlay] RunTrackingLoop CRASHED: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _stopped.Set();
        }
    }

    private void UpdatePosition()
    {
        if (_hwnd == nint.Zero)
        {
            App.DebugLog("[Overlay] _hwnd is zero, skipping");
            return;
        }

        // Get cursor position
        if (!NativeMethods.GetCursorPos(out var pt))
        {
            App.DebugLog("[Overlay] GetCursorPos failed");
            return;
        }

        // Find window under cursor
        var target = NativeMethods.WindowFromPoint(pt);
        // If WindowFromPoint returned null or our overlay, skip this frame
        // (don't fall back to _lastTargetHwnd — that causes position oscillation)
        if (target == nint.Zero || target == _hwnd)
        {
            return;
        }

        // Get target bounds (from root window, not just the child under cursor)
        var rootTarget = NativeMethods.GetAncestor(target, 2); // GA_ROOT = 2
        if (rootTarget != nint.Zero)
        {
            if (!NativeMethods.GetWindowRect(rootTarget, out var rootRect))
                rootTarget = nint.Zero;
            else
                target = rootTarget;
        }

        if (!NativeMethods.GetWindowRect(target, out var rect))
        {
            return;
        }

        // Lightweight status check
        var (status, className, displayPath) = ClassifyWindow(target);

        // Skip overlay rendering if the window under cursor is our own Avalonia window
        if (className.StartsWith("Avalonia-", StringComparison.Ordinal) || className == "TMainBox")
        {
            return;
        }

        // Update color
            DropTargetDetector.DropTargetStatus newStatus = status;
            lock (_stateLock)
            {
                _currentStatus = status;
                _currentColor = status switch
                {
                    DropTargetDetector.DropTargetStatus.Success => 0x0050AF4C, // Green
                    DropTargetDetector.DropTargetStatus.Warning => 0x004336F4, // Red
                    _ => 0x00808080, // Gray (default for None/unrecognized)
                };
            }

        // Position + opacity
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        // Position + opacity — window was started fully transparent in MainWindow
        // via SetLayeredWindowAttributes, so SetWindowPos resize is invisible.
        const uint swpFlags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            rect.Left, rect.Top, w, h, swpFlags);

        // Compute breathing alpha: sine wave between 40 and 120 over ~4s (40 ticks)
        double breath = 80 + 40 * Math.Sin(_tick * Math.PI / 20);
        byte breathAlpha = (byte)Math.Clamp(breath, 40, 120);

        // Render overlay via UpdateLayeredWindow (from background thread, supports color + text)
        try
        {
            OverlayRender(_hwnd, _currentColor, displayPath, rect.Left, rect.Top, w, h, breathAlpha);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[Overlay] OverlayRender CRASHED: {ex.GetType().Name}: {ex.Message}");
        }

        _tick++;
        if (_tick % 10 == 0)
            App.DebugLog($"[Overlay] target=0x{target:X} pos=({rect.Left},{rect.Top}) size={w}x{h} status={newStatus} path={displayPath}");
    }
    private int _tick;

    private static (DropTargetDetector.DropTargetStatus Status, string ClassName, string DisplayPath) ClassifyWindow(nint hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        int maxWalk = 10;
        while (hWnd != nint.Zero && maxWalk-- > 0)
        {
            NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
            var cls = sb.ToString();
            if (cls == "CabinetWClass")
            {
                // Get actual folder path from Explorer window title
                var title = new System.Text.StringBuilder(512);
                NativeMethods.GetWindowText(hWnd, title, title.Capacity);
                var windowTitle = title.ToString();
                if (!string.IsNullOrEmpty(windowTitle))
                    return (DropTargetDetector.DropTargetStatus.Success, cls, windowTitle);
                return (DropTargetDetector.DropTargetStatus.Success, cls, "资源管理器");
            }
            if (cls == "Progman" || cls == "WorkerW")
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                return (DropTargetDetector.DropTargetStatus.Success, cls, desktopPath);
            }
            if (cls == "#32770")
            {
                // Try to get title from dialog
                var title = new System.Text.StringBuilder(512);
                NativeMethods.GetWindowText(hWnd, title, title.Capacity);
                var windowTitle = title.ToString();
                if (!string.IsNullOrEmpty(windowTitle))
                    return (DropTargetDetector.DropTargetStatus.Warning, cls, windowTitle);
                return (DropTargetDetector.DropTargetStatus.Warning, cls, "无法识别路径");
            }
            if (cls.StartsWith("Avalonia-", StringComparison.Ordinal) || cls == "TMainBox")
                return (DropTargetDetector.DropTargetStatus.None, cls, "");
            hWnd = NativeMethods.GetParent(hWnd);
        }
        return (DropTargetDetector.DropTargetStatus.None, "", "");
    }

    /// <summary>
    /// Render overlay content using UpdateLayeredWindow (color + border + text).
    /// Called from background thread; creates a 32bpp BGRA bitmap.
    /// Uses SourceConstantAlpha (window-wide) for breathing — border and text breathe
    /// with the background but use high-contrast bright colors and a drop shadow
    /// to remain clearly readable at minimum breathAlpha.
    /// </summary>
    private static void OverlayRender(nint hwnd, uint colorBgr, string text, int posX, int posY, int w, int h, byte breathAlpha)
    {
        if (hwnd == nint.Zero || w <= 0 || h <= 0) return;

        var hdcScreen = NativeMethods.GetDC(nint.Zero);
        if (hdcScreen == nint.Zero) return;

        var hdcMem = GdiCreateCompatibleDC(hdcScreen);
        if (hdcMem == nint.Zero) { NativeMethods.ReleaseDC(nint.Zero, hdcScreen); return; }

        try
        {
            // Create a 32bpp BGRA DIB section
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = 40,
                    biWidth = w,
                    biHeight = -h, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0 // BI_RGB
                }
            };
            var hBitmap = GdiCreateDIBSection(hdcMem, ref bmi, 0, out var bitsPtr, nint.Zero, 0);
            if (hBitmap == nint.Zero || bitsPtr == nint.Zero) return;

            var oldBitmap = GdiSelectObject(hdcMem, hBitmap);

            // colorBgr = 0x00BBGGRR (BGR format from GDI)
            byte bgB = (byte)(colorBgr >> 16);
            byte bgG = (byte)(colorBgr >> 8);
            byte bgR = (byte)colorBgr;

            int stride = w * 4;
            byte[] pixels = new byte[stride * h];

            // Border: white (high-contrast, visible even at low SourceConstantAlpha)
            const int borderThickness = 4;

            // Fill: background + bright border (all alpha=255; SourceConstantAlpha handles breathing)
            for (int y = 0; y < h; y++)
            {
                bool isBorderY = y < borderThickness || y >= h - borderThickness;
                for (int x = 0; x < w; x++)
                {
                    bool isBorder = isBorderY || x < borderThickness || x >= w - borderThickness;
                    int idx = y * stride + x * 4;
                    if (isBorder)
                    {
                        pixels[idx + 0] = 255; // B
                        pixels[idx + 1] = 255; // G
                        pixels[idx + 2] = 255; // R
                        pixels[idx + 3] = 255; // A
                    }
                    else
                    {
                        pixels[idx + 0] = bgB;
                        pixels[idx + 1] = bgG;
                        pixels[idx + 2] = bgR;
                        pixels[idx + 3] = 255; // A
                    }
                }
            }
            Marshal.Copy(pixels, 0, bitsPtr, pixels.Length);

            // Draw text via GDI
            if (!string.IsNullOrEmpty(text) && text.Length > 0)
            {
                var hFont = GdiCreateFont(-36, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
                if (hFont != nint.Zero)
                {
                    var oldFont = GdiSelectObject(hdcMem, hFont);
                    GdiSetBkMode(hdcMem, 1); // TRANSPARENT

                    // Shadow: dark gray, offset 3px down-right
                    GdiSetTextColor(hdcMem, 0x00303030); // Dark gray
                    var shadowRect = new NativeMethods.RECT { Left = 23, Top = 11, Right = w - 17, Bottom = h - 5 };
                    GdiDrawText(hdcMem, text, text.Length, ref shadowRect, 0x0125);

                    // Main text: white
                    GdiSetTextColor(hdcMem, 0x00FFFFFF); // White
                    var textRect = new NativeMethods.RECT { Left = 20, Top = 8, Right = w - 20, Bottom = h - 8 };
                    GdiDrawText(hdcMem, text, text.Length, ref textRect, 0x0125);

                    GdiSelectObject(hdcMem, oldFont);
                    GdiDeleteObject(hFont);
                }
            }

            // Update the layered window (hBitmap must still be selected in hdcMem!)
            // NOTE: pptDst in UpdateLayeredWindow ALSO sets window position — must use actual coords!
            var ptWnd = new NativeMethods.POINT { X = posX, Y = posY };
            var szWnd = new NativeMethods.SIZE { cx = w, cy = h };
            var ptSrc = new NativeMethods.POINT { X = 0, Y = 0 };
            // SourceConstantAlpha controls window-wide breathing (original working approach)
            var blend = new NativeMethods.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = breathAlpha, AlphaFormat = 0 };

            NativeMethods.UpdateLayeredWindow(hwnd, nint.Zero, ref ptWnd, ref szWnd,
                hdcMem, ref ptSrc, 0, ref blend, 2); // ULW_ALPHA = 2

            // Cleanup: restore old bitmap before deleting ours
            GdiSelectObject(hdcMem, oldBitmap);
            GdiDeleteObject(hBitmap);
        }
        catch (Exception ex)
        {
            // GDI operation failed — log but don't crash the overlay thread
            App.DebugLog($"[Overlay] OverlayRender error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            GdiDeleteDC(hdcMem);
            NativeMethods.ReleaseDC(nint.Zero, hdcScreen);
        }
    }

    // ── GDI P/Invokes for UpdateLayeredWindow ──
    // NOTE: All use EntryPoint to map Gdi-prefixed C# names to the correct Win32 export
    // (e.g. "GdiCreateCompatibleDC" → actually exported as "CreateCompatibleDC")

    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
    private static extern nint GdiCreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
    private static extern nint GdiCreateDIBSection(nint hdc, ref BITMAPINFO pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

    [DllImport("gdi32.dll", EntryPoint = "SelectObject")]
    private static extern nint GdiSelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    private static extern bool GdiDeleteObject(nint hObject);

    [DllImport("gdi32.dll", EntryPoint = "DeleteDC")]
    private static extern bool GdiDeleteDC(nint hdc);

    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
    private static extern nint GdiCreateFont(int nHeight, int nWidth, int nEscapement, int nOrientation, int fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut, uint fdwCharSet, uint fdwOutputPrecision, uint fdwClipPrecision, uint fdwQuality, uint fdwPitchAndFamily, string lpszFace);

    [DllImport("gdi32.dll", EntryPoint = "GetStockObject")]
    private static extern nint GdiGetStockObject(int fnObject);

    [DllImport("gdi32.dll", EntryPoint = "SetBkMode")]
    private static extern int GdiSetBkMode(nint hdc, int mode);

    [DllImport("gdi32.dll", EntryPoint = "SetTextColor")]
    private static extern uint GdiSetTextColor(nint hdc, uint crColor);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DrawTextW")]
    private static extern int GdiDrawText(nint hdc, string lpchText, int cchText, ref NativeMethods.RECT lprc, uint format);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER { public int biSize; public int biWidth; public int biHeight; public short biPlanes; public short biBitCount; public int biCompression; public int biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public int biClrUsed; public int biClrImportant; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }
}