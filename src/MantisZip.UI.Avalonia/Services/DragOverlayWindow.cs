using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Pure Win32 overlay window that shows cosmetic drag feedback.
/// Runs on a dedicated STA thread since Avalonia's DoDragDropAsync blocks the UI thread.
/// 
/// The overlay is a layered, transparent window that displays a breath-animated
/// green border (#4CAF50) during drag. No target detection is needed since
/// OLE DoDragDrop handles file transfer natively.
/// 
/// Also manages a DragPreviewPopup that follows the cursor.
/// </summary>
public class DragOverlayWindow : IDisposable
{
    // ── Window handle and thread ──

    private nint _hwnd;
    private Thread? _uiThread;
    private CancellationTokenSource? _cts;

    // ── Synchronisation ──

    private readonly ManualResetEvent _windowCreated = new(false);
    private readonly ManualResetEvent _windowClosed = new(false);

    // ── Preview popup ──

    private PreviewBitmapData? _previewData;
    private DragPreviewPopup? _previewPopup;

    // ── Animation ──

    private System.Timers.Timer? _animationTimer;
    private const int AnimationIntervalMs = 50; // 20 fps
    private uint _currentColor = 0x0050AF4C; // BGR green #4CAF50

    // ── Window procedure ──

    private readonly WndProcDelegate _wndProcDelegate;

    // ── Missing NativeMethods helpers ──

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);

    private const uint PM_REMOVE = 1;

    // ── Constructor ──

    public DragOverlayWindow()
    {
        _wndProcDelegate = WndProc;
    }

    // ── Public API ──

    /// <summary>
    /// Start the overlay window on a background STA thread.
    /// Blocks until the window is created (up to 5 seconds).
    /// </summary>
    public void Show()
    {
        _cts = new CancellationTokenSource();

        _uiThread = new Thread(RunWindowLoop)
        {
            Name = "DragOverlayWindow",
            IsBackground = true
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        if (!_windowCreated.WaitOne(TimeSpan.FromSeconds(5)))
        {
            Debug.WriteLine("[DragOverlayWindow] Timed out waiting for window creation (5s)");
        }
    }

    /// <summary>
    /// Close the overlay window and clean up.
    /// Thread-safe; can be called from the Avalonia UI thread.
    /// </summary>
    public void Close()
    {
        // Stop the animation timer first
        if (_animationTimer != null)
        {
            _animationTimer.Stop();
            _animationTimer.Dispose();
            _animationTimer = null;
        }

        // Signal the message loop to exit
        _cts?.Cancel();

        // Dispose preview popup
        _previewPopup?.Dispose();
        _previewPopup = null;

        // Destroy the overlay window (cross-thread safe on Win10+)
        if (_hwnd != nint.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }

        // Wait for the thread to signal exit (with timeout)
        if (!_windowClosed.WaitOne(TimeSpan.FromSeconds(2)))
        {
            Debug.WriteLine("[DragOverlayWindow] Timed out waiting for thread exit (2s)");
        }

        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Provide the pre-rendered preview bitmap before Show() is called.
    /// The popup will be created on the STA thread after window creation.
    /// </summary>
    public void SetPreviewBitmap(PreviewBitmapData data)
    {
        _previewData = data;
    }

    // ── STA thread entry point ──

    private void RunWindowLoop()
    {
        var hInstance = NativeMethods.GetModuleHandle(null);

        // ── 1. Register window class ──
        var className = "MantisZipDragOverlay";
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
        {
            Debug.WriteLine("[DragOverlayWindow] RegisterClassEx failed");
            _windowCreated.Set();
            return;
        }

        // ── 2. Create overlay window ──
        _hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_LAYERED |
            NativeMethods.WS_EX_TRANSPARENT |
            NativeMethods.WS_EX_NOACTIVATE |
            NativeMethods.WS_EX_TOOLWINDOW,
            className,
            "DragOverlay",
            NativeMethods.WS_POPUP,
            0, 0, 0, 0,          // Size/position — updated by target window detection
            nint.Zero,
            nint.Zero,
            hInstance,
            nint.Zero);

        if (_hwnd == nint.Zero)
        {
            Debug.WriteLine("[DragOverlayWindow] CreateWindowEx failed");
            _windowCreated.Set();
            return;
        }

        // ── 3. Initial window setup ──
        // Start fully transparent — animation will fade it in
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0, NativeMethods.LWA_ALPHA);
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOW);

        // ── 4. Create preview popup if data was provided ──
        if (_previewData != null && _previewData != PreviewBitmapData.Empty)
        {
            try
            {
                _previewPopup = new DragPreviewPopup(hInstance, _previewData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DragOverlayWindow] Failed to create preview popup: {ex.Message}");
            }
        }

        // ── 5. Signal that the window is ready ──
        _windowCreated.Set();

        // ── 6. Start animation timer ──
        _animationTimer = new System.Timers.Timer(AnimationIntervalMs)
        {
            AutoReset = true
        };
        _animationTimer.Elapsed += OnAnimationTick;
        _animationTimer.Start();

        // ── 7. Message loop (PeekMessage — non-blocking) ──
        try
        {
            while (!_cts!.IsCancellationRequested)
            {
                // Process all pending messages
                while (NativeMethods.PeekMessage(out var msg, nint.Zero, 0, 0, PM_REMOVE))
                {
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessage(ref msg);
                }

                // Non-message work: popup positioning
                UpdatePreviewPopupPosition();

                // Prevent CPU spin — 10ms sleep between idle checks
                Thread.Sleep(10);
            }
        }
        catch (ThreadAbortException)
        {
            // STA thread aborted during shutdown — expected
        }
        finally
        {
            _animationTimer?.Stop();
            _animationTimer?.Dispose();
            _animationTimer = null;

            _windowClosed.Set();
        }
    }

    // ── Preview popup positioning ──

    private void UpdatePreviewPopupPosition()
    {
        if (_previewPopup == null)
            return;

        NativeMethods.GetCursorPos(out var pt);
        _previewPopup.UpdatePosition(pt.X, pt.Y);
    }

    // ── Animation ──

    /// <summary>
    /// Timer callback (runs on thread pool) — updates overlay opacity.
    /// Uses a sine wave breath animation in the 0.15–0.45 opacity range.
    /// </summary>
    private void OnAnimationTick(object? sender, ElapsedEventArgs e)
    {
        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        if (_hwnd == nint.Zero)
            return;

        // Calculate breath opacity: 0.15–0.45 range using sine over a 2-second period
        var phase = (DateTime.UtcNow.Ticks % (TimeSpan.TicksPerSecond * 2))
                    / (double)(TimeSpan.TicksPerSecond * 2);
        var breath = 0.15 + Math.Sin(phase * Math.PI * 2) * 0.15;
        var alpha = (byte)(breath * 255);

        // Always use green #4CAF50 (BGR format for GDI) — no target detection needed
        _currentColor = 0x0050AF4C;

        // Apply the layered alpha for the breath effect
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
    }

    // ── Window procedure ──

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_PAINT:
                return HandlePaint(hWnd);

            case NativeMethods.WM_DESTROY:
                return nint.Zero;

            case NativeMethods.WM_NCCALCSIZE:
                // Return 0 to suppress the non-client area (no border)
                return nint.Zero;

            case NativeMethods.WM_NCHITTEST:
                // HTTRANSPARENT — mouse events pass through to windows beneath
                return new nint(-1);

            default:
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private nint HandlePaint(nint hWnd)
    {
        NativeMethods.BeginPaint(hWnd, out var ps);

        try
        {
            // Fill the entire client area with the current overlay color
            var brush = NativeMethods.CreateSolidBrush(_currentColor);
            if (brush != nint.Zero)
            {
                var oldBrush = NativeMethods.SelectObject(ps.hdc, brush);
                NativeMethods.Rectangle(
                    ps.hdc,
                    ps.rcPaint.Left,
                    ps.rcPaint.Top,
                    ps.rcPaint.Right,
                    ps.rcPaint.Bottom);
                NativeMethods.SelectObject(ps.hdc, oldBrush);
                NativeMethods.DeleteObject(brush);
            }
        }
        finally
        {
            NativeMethods.EndPaint(hWnd, ref ps);
        }

        return nint.Zero;
    }

    // ── Delegate type (stored as field to prevent GC) ──

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    // ── IDisposable ──

    public void Dispose()
    {
        Close();
    }
}
