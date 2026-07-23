using System;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Controls an existing overlay window (Avalonia Window) via Win32 API from a background thread.
/// The window itself is created by Avalonia on the UI thread; this class only does cross-thread
/// operations: SetWindowPos (position) and SetLayeredWindowAttributes (color + opacity).
/// </summary>
internal class OverlayController : IDisposable
{
    private nint _hwnd;
    private Thread? _trackingThread;
    private CancellationTokenSource? _cts;
    private readonly ManualResetEvent _stopped = new(false);

    // Tracking state
    private nint _lastTargetHwnd;
    private uint _currentColor = 0x0050AF4C; // BGR green
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
                UpdatePosition();
                Thread.Sleep(100);
            }
        }
        catch (ThreadAbortException) { }
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
        if (target == _hwnd || target == nint.Zero)
            target = _lastTargetHwnd;
        if (target == nint.Zero || target == _hwnd)
        {
            App.DebugLog($"[Overlay] No valid target (cursor=({pt.X},{pt.Y}), lastTarget=0x{_lastTargetHwnd:X})");
            return;
        }

        // Get target bounds
        if (!NativeMethods.GetWindowRect(target, out var rect))
        {
            App.DebugLog($"[Overlay] GetWindowRect failed for 0x{target:X}");
            return;
        }

        // Walk up to root window to cover the entire frame, not just a child control
        var rootTarget = NativeMethods.GetAncestor(target, 2); // GA_ROOT = 2
        if (rootTarget != nint.Zero && rootTarget != target)
        {
            if (!NativeMethods.GetWindowRect(rootTarget, out var rootRect))
                rootTarget = nint.Zero;
            else
                rect = rootRect;
        }

        _lastTargetHwnd = target;

        // Lightweight status check
        var (status, className, displayPath) = ClassifyWindow(target);

        // Update color
        DropTargetDetector.DropTargetStatus newStatus;
        lock (_stateLock)
        {
            newStatus = _currentStatus;
            if (status != _currentStatus)
            {
                _currentStatus = status;
                _currentColor = status switch
                {
                    DropTargetDetector.DropTargetStatus.Success => 0x0050AF4C, // Green
                    DropTargetDetector.DropTargetStatus.Warning => 0x004336F4, // Red
                    _ => 0x00808080, // Gray
                };
                newStatus = status;
            }
        }

        // Position + opacity
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0)
        {
            App.DebugLog($"[Overlay] Invalid target size: {w}x{h}");
            return;
        }

        const uint swpFlags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            rect.Left, rect.Top, w, h, swpFlags);

        // Render overlay via UpdateLayeredWindow (from background thread, supports color + text)
        OverlayRender(_hwnd, _currentColor, displayPath, w, h);

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
            if (cls == "CabinetWClass" || cls == "Progman" || cls == "WorkerW")
            {
                var path = cls == "Progman" || cls == "WorkerW"
                    ? "桌面"
                    : "资源管理器";
                return (DropTargetDetector.DropTargetStatus.Success, cls, path);
            }
            if (cls == "#32770")
                return (DropTargetDetector.DropTargetStatus.Warning, cls, "无法识别路径");
            if (cls.StartsWith("Avalonia-", StringComparison.Ordinal) || cls == "TMainBox")
                return (DropTargetDetector.DropTargetStatus.None, cls, "");
            hWnd = NativeMethods.GetParent(hWnd);
        }
        return (DropTargetDetector.DropTargetStatus.None, "", "");
    }

    // (path detection uses DropTargetDetector after drag ends, not during overlay)

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(nint hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(nint hdc, uint crColor);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(nint hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    private struct RECT { public int Left, Top, Right, Bottom; }

    private void DrawOverlayText(string text, int w, int h)
    {
        if (string.IsNullOrEmpty(text) || _hwnd == nint.Zero) return;

        var hdc = GetDC(_hwnd);
        if (hdc == nint.Zero) return;

        try
        {
            SetBkMode(hdc, 1); // TRANSPARENT
            SetTextColor(hdc, 0x00FFFFFF); // White text
            var textRect = new RECT { Left = 16, Top = 0, Right = w - 16, Bottom = h };
            DrawText(hdc, text, -1, ref textRect, 0x0124); // DT_CENTER | DT_VCENTER | DT_SINGLELINE
        }
        finally
        {
            ReleaseDC(_hwnd, hdc);
        }
}


    /// <summary>
    /// Render overlay content using UpdateLayeredWindow (color + text).
    /// Called from background thread; creates a 32bpp BGRA bitmap with pre-multiplied alpha.
    /// </summary>
    private static void OverlayRender(nint hwnd, uint colorBgr, string text, int w, int h)
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

            // Fill with overlay color (pre-multiplied alpha)
            // colorBgr = 0x00BBGGRR (BGR format from GDI)
            byte b = (byte)(colorBgr >> 16);
            byte g = (byte)(colorBgr >> 8);
            byte r = (byte)colorBgr;
            byte alpha = 80; // ~31% opacity

            // Fill pixels: BGRA with pre-multiplied alpha
            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * stride + x * 4;
                    pixels[idx + 0] = (byte)(b * alpha / 255); // B
                    pixels[idx + 1] = (byte)(g * alpha / 255); // G
                    pixels[idx + 2] = (byte)(r * alpha / 255); // R
                    pixels[idx + 3] = alpha;                   // A
                }
            }
            Marshal.Copy(pixels, 0, bitsPtr, pixels.Length);

            // Draw text using GDI on the DC (white, full opacity, in the center area)
            if (!string.IsNullOrEmpty(text))
            {
                var oldFont = GdiSelectObject(hdcMem, GdiGetStockObject(18)); // DEFAULT_GUI_FONT
                GdiSetBkMode(hdcMem, 1); // TRANSPARENT
                GdiSetTextColor(hdcMem, 0x00FFFFFF); // White
                var textRect = new NativeMethods.RECT { Left = 24, Top = 12, Right = w - 24, Bottom = h - 12 };
                GdiDrawText(hdcMem, text, -1, ref textRect, 0x0124); // DT_CENTER | DT_VCENTER | DT_SINGLELINE
                GdiSelectObject(hdcMem, oldFont);
            }

            // Update the layered window (hBitmap must still be selected in hdcMem!)
            var ptWnd = new NativeMethods.POINT { X = 0, Y = 0 };
            var szWnd = new NativeMethods.SIZE { cx = w, cy = h };
            var ptSrc = new NativeMethods.POINT { X = 0, Y = 0 };
            var blend = new NativeMethods.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 }; // AC_SRC_ALPHA

            NativeMethods.UpdateLayeredWindow(hwnd, nint.Zero, ref ptWnd, ref szWnd,
                hdcMem, ref ptSrc, 0, ref blend, 2); // ULW_ALPHA = 2

            // Cleanup: restore old bitmap before deleting ours
            GdiSelectObject(hdcMem, oldBitmap);
            GdiDeleteObject(hBitmap);
        }
        finally
        {
            GdiDeleteDC(hdcMem);
            NativeMethods.ReleaseDC(nint.Zero, hdcScreen);
        }
    }

    // ── GDI P/Invokes for UpdateLayeredWindow ──

    [DllImport("gdi32.dll")]
    private static extern nint GdiCreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint GdiCreateDIBSection(nint hdc, ref BITMAPINFO pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint GdiSelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    private static extern bool GdiDeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    private static extern bool GdiDeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint GdiGetStockObject(int fnObject);

    [DllImport("gdi32.dll")]
    private static extern int GdiSetBkMode(nint hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint GdiSetTextColor(nint hdc, uint crColor);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GdiDrawText(nint hdc, string lpchText, int cchText, ref NativeMethods.RECT lprc, uint format);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER { public int biSize; public int biWidth; public int biHeight; public short biPlanes; public short biBitCount; public int biCompression; public int biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public int biClrUsed; public int biClrImportant; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }
}