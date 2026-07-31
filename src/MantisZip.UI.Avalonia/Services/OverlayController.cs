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
    private readonly nint _mainHwnd;
    private Thread? _trackingThread;
    private CancellationTokenSource? _cts;
    private readonly ManualResetEvent _stopped = new(false);

    // Tracking state
    private uint _currentColor = 0x00808080; // BGR gray (default for no target)
    private DropTargetDetector.DropTargetStatus _currentStatus;
    private bool _isOwnApp;
    private readonly object _stateLock = new();

    // Preview image (set from UI thread after async render completes)
    private volatile PreviewImageData? _preview;

    /// <summary>
    /// Holds a pre-rendered bitmap to be composited into the overlay.
    /// Called from any thread; background thread reads via volatile.
    /// </summary>
    public void SetPreview(byte[] bgraPixels, int imageWidth, int imageHeight)
    {
        _preview = new PreviewImageData
        {
            Pixels = bgraPixels,
            Width = imageWidth,
            Height = imageHeight
        };
    }

    public class PreviewImageData
    {
        public byte[] Pixels { get; init; } = Array.Empty<byte>();
        public int Width { get; init; }
        public int Height { get; init; }
    }

    public OverlayController(nint hwnd, nint mainHwnd)
    {
        _hwnd = hwnd;
        _mainHwnd = mainHwnd;
    }

    /// <summary>当前目标检测状态（跟踪线程写、拖拽线程读，加锁安全）。</summary>
    public DropTargetDetector.DropTargetStatus CurrentStatus
    {
        get { lock (_stateLock) return _currentStatus; }
    }

    /// <summary>当前是否悬停在自己应用窗口上（对应深灰状态）。</summary>
    public bool IsOverOwnWindow
    {
        get { lock (_stateLock) return _isOwnApp; }
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

        // Clip to virtual screen bounds so overlay isn't placed off-screen
        var screenRect = NativeMethods.GetVirtualScreenRect();
        if (NativeMethods.IntersectRect(out var clippedRect, ref rect, ref screenRect))
        {
            rect = clippedRect;
        }

        // Lightweight status check
        var (status, className, displayPath) = ClassifyWindow(target);

        // Detect own app via HWND comparison (reliable — class name heuristic misidentifies
        // other Avalonia apps as MantisZip since all Avalonia windows share "Avalonia-" prefix)
        bool isOwnApp = _mainHwnd != nint.Zero && target == _mainHwnd;

        // Update color
        DropTargetDetector.DropTargetStatus newStatus = status;
        lock (_stateLock)
        {
            _currentStatus = status;
            _isOwnApp = isOwnApp;
            if (isOwnApp)
            {
                _currentColor = 0x00333333; // Dark gray for own window
            }
            else
            {
                _currentColor = status switch
                {
                    DropTargetDetector.DropTargetStatus.Success => 0x006BD46B, // Brighter green
                    DropTargetDetector.DropTargetStatus.Warning => 0x004336F4, // Red
                    _ => 0x0000D7FF, // Warm gold
                };
            }
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

        // Compute breathing alpha: sine wave between 40 and 120 over ~2s (20 ticks)
        double breath = 80 + 40 * Math.Sin(_tick * Math.PI / 10);
        byte breathAlpha = (byte)Math.Clamp(breath, 40, 120);

        // Render overlay via UpdateLayeredWindow (from background thread, supports color + text)
        try
        {
            // Abbreviate long paths for small windows
            string displayText = status == DropTargetDetector.DropTargetStatus.Success && !string.IsNullOrEmpty(displayPath)
                ? AbbreviatePath(displayPath)
                : displayPath;

            // Build user-friendly display text based on status
        string overlayText = isOwnApp
            ? "拖拽到文件夹以释放文件，或者在此松开鼠标以取消"
            : status switch
            {
                DropTargetDetector.DropTargetStatus.Success => LocalizationManager.T("DragOverlay_TargetPath", displayText),
                DropTargetDetector.DropTargetStatus.Warning => displayText,
                _ => LocalizationManager.T("DragOverlay_DragToFolder")
            };

        OverlayRender(_hwnd, _currentColor, overlayText, rect.Left, rect.Top, w, h, breathAlpha, _preview, status);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[Overlay] OverlayRender CRASHED: {ex.GetType().Name}: {ex.Message}");
        }

        _tick++;
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
                // Get full folder path via ShellWindows COM (not just window title)
                var (fullPath, shellStatus) = DropTargetDetector.TryGetExplorerPathFromShell(hWnd);
                if (!string.IsNullOrEmpty(fullPath))
                    return (DropTargetDetector.DropTargetStatus.Success, cls, fullPath);
                // Virtual folders (This PC, Quick Access) → return Warning status
                return (shellStatus, cls, LocalizationManager.T("DragOverlay_Explorer"));
            }
            if (cls == "Progman" || cls == "WorkerW")
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                return (DropTargetDetector.DropTargetStatus.Success, cls, desktopPath);
            }
            if (cls == "#32770")
            {
                // Try to get full path via child window enumeration
                var (dlgPath, dlgStatus) = DropTargetDetector.TryGetDialogPath(hWnd);
                if (!string.IsNullOrEmpty(dlgPath))
                    return (dlgStatus, cls, dlgPath);
                // Fallback: show dialog title with notice
                var title = new System.Text.StringBuilder(512);
                NativeMethods.GetWindowText(hWnd, title, title.Capacity);
                var windowTitle = title.ToString();
                if (!string.IsNullOrEmpty(windowTitle))
                    return (DropTargetDetector.DropTargetStatus.Warning, cls, LocalizationManager.T("DragOverlay_DialogUnknownPath", windowTitle));
                return (DropTargetDetector.DropTargetStatus.Warning, cls, LocalizationManager.T("DragOverlay_NoPath"));
            }
            hWnd = NativeMethods.GetParent(hWnd);
        }
        return (DropTargetDetector.DropTargetStatus.None, "", "");
    }

    /// <summary>
    /// Abbreviates a long filesystem path by keeping drive + first directory,
    /// removing middle components, and showing the last 2 directories.
    /// Ex: "C:\Users\Admin\Documents\Projects\MantisZip\src\Core"
    ///   → "C:\Users\...\src\Core"
    /// Threshold: paths with ≤5 components are returned unchanged.
    /// </summary>
    private static string AbbreviatePath(string path, int threshold = 5)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        var parts = path.Split('\\');
        if (parts.Length <= threshold)
            return path;

        // Keep: [drive], [first dir], "...", [second-to-last], [last]
        return string.Join("\\",
            parts[0],
            parts[1],
            "...",
            parts[parts.Length - 2],
            parts[parts.Length - 1]);
    }

    /// <summary>
    /// Render overlay content using UpdateLayeredWindow (color + border + text + preview).
    /// Called from background thread; creates a 32bpp BGRA bitmap.
    /// Uses SourceConstantAlpha (window-wide) for breathing — border and text breathe
    /// with the background but use high-contrast bright colors and a drop shadow
    /// to remain clearly readable at minimum breathAlpha.
    /// </summary>
    private static void OverlayRender(nint hwnd, uint colorBgr, string text, int posX, int posY, int w, int h, byte breathAlpha, PreviewImageData? preview, DropTargetDetector.DropTargetStatus status)
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

            const int borderThickness = 8;
            int stride = w * 4;
            byte[] pixels = new byte[stride * h];

            // ── Calculate areas that should stay opaque (border, text, preview/icon) ──
            const int iconSize = 60;
            bool hasPreview = preview != null && preview.Pixels.Length > 0 && preview.Width > 0 && preview.Height > 0;
            int previewX = (w - iconSize) / 2;
            int previewY = h - borderThickness - 8 - iconSize + 10;
            int textTop = borderThickness + 8;
            int textBottom = hasPreview ? previewY - 8 : h - borderThickness - 8;

            // Fill: background breathes (alpha=breathAlpha, pre-multiplied).
            // Only border gets alpha=255. Text area and preview area are NOT made opaque here —
            // GDI will draw text/icon later, and we'll detect changed pixels to fix their alpha.
            for (int y = 0; y < h; y++)
            {
                bool isBorderY = y < borderThickness || y >= h - borderThickness;

                for (int x = 0; x < w; x++)
                {
                    bool isBorder = isBorderY || x < borderThickness || x >= w - borderThickness;

                    int idx = y * stride + x * 4;
                    if (isBorder)
                    {
                        // Border uses same color as background but fully opaque (no breathing)
                        pixels[idx + 0] = bgB; // B
                        pixels[idx + 1] = bgG; // G
                        pixels[idx + 2] = bgR; // R
                        pixels[idx + 3] = 255; // A (fully opaque)
                    }
                    else
                    {
                        // Pre-multiplied background with breathing alpha
                        pixels[idx + 0] = (byte)(bgB * breathAlpha / 255); // B
                        pixels[idx + 1] = (byte)(bgG * breathAlpha / 255); // G
                        pixels[idx + 2] = (byte)(bgR * breathAlpha / 255); // R
                        pixels[idx + 3] = breathAlpha; // A (breathes 40-120)
                    }
                }
            }

            // ── Preview image (composited below text when available) ──
            if (hasPreview)
            {
                CompositeImage(pixels, w, h, preview.Pixels, preview.Width, preview.Height,
                    previewX, previewY, iconSize, iconSize);
            }

            // Write pixel data to DIB before GDI text overlay
            Marshal.Copy(pixels, 0, bitsPtr, pixels.Length);

            // Main text — with single drop-shadow for readability
            if (!string.IsNullOrEmpty(text) && text.Length > 0)
            {
                GdiSetBkMode(hdcMem, 1); // TRANSPARENT

                const uint dtCenter = 0x0001;
                const uint dtWordBreak = 0x0010;
                const uint dtNoClip = 0x0100;
                const uint dtCalcRect = 0x0400;
                const uint dtEditControl = 0x2000;
                // DT_EDITCONTROL enables character-level line breaking (edit-control style),
                // so long words without spaces (file paths) wrap at character boundaries.
                const uint dtFormat = dtCenter | dtWordBreak | dtNoClip | dtEditControl;

                // Font size proportional to window dimensions (simple, predictable)
                int baseDim = Math.Min(w, h);
                int chosenSize = baseDim switch
                {
                    >= 500 => 36,
                    >= 350 => 28,
                    >= 250 => 24,
                    >= 180 => 20,
                    _ => 16
                };

                int availableHeight = textBottom - textTop;

                // Draw with chosen size
                var hFont = GdiCreateFont(-chosenSize, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
                if (hFont != nint.Zero)
                {
                    var oldFont = GdiSelectObject(hdcMem, hFont);

                    // Measure again with chosen size for centering
                    var measureRect = new NativeMethods.RECT { Left = 20, Top = 0, Right = w - 20, Bottom = 0 };
                    GdiDrawText(hdcMem, text, text.Length, ref measureRect, dtFormat | dtCalcRect);
                    int textHeight = measureRect.Bottom;

                    int vOffset = Math.Max(0, (availableHeight - textHeight) / 2);
                    int adjustedTop = textTop + vOffset;

                    var baseRect = new NativeMethods.RECT { Left = 20, Top = adjustedTop, Right = w - 20, Bottom = textBottom };

                    // Shadow: single offset at (+2,+2)
                    GdiSetTextColor(hdcMem, 0x00101010); // near-black
                    var shadowRect = new NativeMethods.RECT
                    {
                        Left = baseRect.Left + 2,
                        Top = baseRect.Top + 2,
                        Right = baseRect.Right + 2,
                        Bottom = baseRect.Bottom + 2
                    };
                    GdiDrawText(hdcMem, text, text.Length, ref shadowRect, dtFormat);

                    // Main text: white
                    GdiSetTextColor(hdcMem, 0x00FFFFFF);
                    GdiDrawText(hdcMem, text, text.Length, ref baseRect, dtFormat);

                    GdiSelectObject(hdcMem, oldFont);
                    GdiDeleteObject(hFont);
                }
            }

            // ── Status icon (only when no real preview image) ──
            if (!hasPreview)
            {
                string iconChar = status switch
                {
                    DropTargetDetector.DropTargetStatus.Success => "\u2713", // ✓ check mark
                    DropTargetDetector.DropTargetStatus.Warning => "\u26A0", // ⚠ warning sign
                    _ => ""
                };

                if (!string.IsNullOrEmpty(iconChar))
                {
                    uint iconColor = status switch
                    {
                        DropTargetDetector.DropTargetStatus.Success => 0x0060E090, // Bright green-blue BGR
                        DropTargetDetector.DropTargetStatus.Warning => 0x0020E0FF, // Bright amber/yellow BGR
                        _ => 0x00FFFFFF
                    };

                    var hIconFont = GdiCreateFont(-56, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI Symbol");
                    if (hIconFont != nint.Zero)
                    {
                        var oldIconFont = GdiSelectObject(hdcMem, hIconFont);
                        GdiSetBkMode(hdcMem, 1);

                        // Icon shadow: single offset at (+1,+1)
                        GdiSetTextColor(hdcMem, 0x00101010);
                        var iconBaseRect = new NativeMethods.RECT
                        {
                            Left = previewX + 4, Top = previewY + 4,
                            Right = previewX + iconSize + 4, Bottom = previewY + iconSize + 4
                        };
                        var iconShadowRect = new NativeMethods.RECT
                        {
                            Left = iconBaseRect.Left + 1,
                            Top = iconBaseRect.Top + 1,
                            Right = iconBaseRect.Right + 1,
                            Bottom = iconBaseRect.Bottom + 1
                        };
                        GdiDrawText(hdcMem, iconChar, iconChar.Length, ref iconShadowRect,
                            0x0125 | 0x0800);

                        // Icon
                        GdiSetTextColor(hdcMem, iconColor);
                        GdiDrawText(hdcMem, iconChar, iconChar.Length, ref iconBaseRect,
                            0x0125 | 0x0800);

                        GdiSelectObject(hdcMem, oldIconFont);
                        GdiDeleteObject(hIconFont);
                    }
                }
            }

            // ── Fix alpha channel in DIB bits after GDI operations ──
            // GDI DrawText corrupts alpha (sets to 0) for text glyph pixels in 32-bit DIB.
            // Strategy: compare DIB bits against the original pixel buffer to find pixels
            // GDI modified (text/shadow/icon strokes), and fix only those to alpha=255.
            // Background pixels (unmodified by GDI) keep their original alpha (breathe).
            byte[] rowBuf = new byte[stride];

            // Helper: for a pixel GDI modified, distinguish outline from text:
            // - Dark pixels (max channel ≤ 60) → outline stroke → alpha=255 (fully opaque)
            // - Bright pixels (max channel > 60) → text/anti-aliased → smooth alpha from brightness
            //   with pre-multiplied RGB, preserving anti-aliasing edges.
            void FixGdiPixel(byte[] row, int xOffset)
            {
                // row is BGRA: [B, G, R, A]
                byte b = row[xOffset];
                byte g = row[xOffset + 1];
                byte r = row[xOffset + 2];
                int maxChannel = Math.Max(r, Math.Max(g, b));
                if (maxChannel > 0)
                {
                    // Dark pixels → outline stroke (drawn at 8 offsets around text)
                    // Make fully opaque so dark outline is visible against background
                    if (maxChannel <= 60)
                    {
                        // Outline is already the desired dark color (e.g. 0x10,0x10,0x10 BGR).
                        // No pre-multiply needed since alpha=255.
                        row[xOffset + 3] = 255;
                    }
                    else
                    {
                        // Bright pixels → white text or anti-aliased edge.
                        // Graduated alpha preserves smooth text edges.
                        int alpha = maxChannel;
                        row[xOffset] = (byte)(b * alpha / 255);
                        row[xOffset + 1] = (byte)(g * alpha / 255);
                        row[xOffset + 2] = (byte)(r * alpha / 255);
                        row[xOffset + 3] = (byte)alpha;
                    }
                }
            }

            if (!string.IsNullOrEmpty(text) && text.Length > 0)
            {
                int textLeft = 20;
                int textRight = w - 20;
                int top = textTop;
                int bottom = Math.Min(textBottom, h);
                for (int y = top; y < bottom; y++)
                {
                    int rowOffset = y * stride;
                    // Read DIB row (post-GDI)
                    Marshal.Copy(bitsPtr + rowOffset, rowBuf, 0, stride);
                    for (int x = textLeft; x < textRight; x++)
                    {
                        int idx = x * 4;
                        int bufIdx = rowOffset + idx;
                        // Compare B, G, R with original pixel buffer
                        // If any byte differs, GDI modified this pixel → fix alpha
                        if (rowBuf[idx] != pixels[bufIdx] ||
                            rowBuf[idx + 1] != pixels[bufIdx + 1] ||
                            rowBuf[idx + 2] != pixels[bufIdx + 2])
                        {
                            FixGdiPixel(rowBuf, idx);
                        }
                    }
                    Marshal.Copy(rowBuf, 0, bitsPtr + rowOffset, stride);
                }
            }

            // Fix alpha in preview/icon area (GDI-drawn icon pixels)
            int iconY1 = Math.Clamp(previewY, 0, h - 1);
            int iconY2 = Math.Clamp(previewY + iconSize, 0, h);
            int iconX1 = Math.Clamp(previewX, 0, w - 1);
            int iconX2 = Math.Clamp(previewX + iconSize, 0, w);
            for (int y = iconY1; y < iconY2; y++)
            {
                int rowOffset = y * stride;
                Marshal.Copy(bitsPtr + rowOffset, rowBuf, 0, stride);
                for (int x = iconX1; x < iconX2; x++)
                {
                    int idx = x * 4;
                    int bufIdx = rowOffset + idx;
                    if (rowBuf[idx] != pixels[bufIdx] ||
                        rowBuf[idx + 1] != pixels[bufIdx + 1] ||
                        rowBuf[idx + 2] != pixels[bufIdx + 2])
                    {
                        FixGdiPixel(rowBuf, idx);
                    }
                }
                Marshal.Copy(rowBuf, 0, bitsPtr + rowOffset, stride);
            }

            // Update the layered window (hBitmap must still be selected in hdcMem!)
            var ptWnd = new NativeMethods.POINT { X = posX, Y = posY };
            var szWnd = new NativeMethods.SIZE { cx = w, cy = h };
            var ptSrc = new NativeMethods.POINT { X = 0, Y = 0 };
            var blend = new NativeMethods.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };

            NativeMethods.UpdateLayeredWindow(hwnd, nint.Zero, ref ptWnd, ref szWnd,
                hdcMem, ref ptSrc, 0, ref blend, 2); // ULW_ALPHA = 2

            // Cleanup
            GdiSelectObject(hdcMem, oldBitmap);
            GdiDeleteObject(hBitmap);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[Overlay] OverlayRender error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            GdiDeleteDC(hdcMem);
            NativeMethods.ReleaseDC(nint.Zero, hdcScreen);
        }
    }

    /// <summary>
    /// Composite a BGRA image into the pixel buffer, scaled to fit the target rect.
    /// Simple nearest-neighbor scaling.
    /// </summary>
    private static void CompositeImage(byte[] destPixels, int destStride, int destH,
        byte[] srcPixels, int srcW, int srcH,
        int dstX, int dstY, int dstW, int dstH)
    {
        for (int dy = 0; dy < dstH; dy++)
        {
            int srcY = dy * srcH / dstH;
            if (srcY >= srcH) srcY = srcH - 1;
            for (int dx = 0; dx < dstW; dx++)
            {
                int srcX = dx * srcW / dstW;
                if (srcX >= srcW) srcX = srcW - 1;

                int destIdx = (dstY + dy) * destStride + (dstX + dx) * 4;
                int srcIdx = srcY * srcW * 4 + srcX * 4;

                // Source-over blend (premultiplied alpha)
                byte sa = srcPixels[srcIdx + 3];
                if (sa == 0) continue;
                int da = 255;
                int outA = da;
                if (sa < 255)
                {
                    outA = 255; // opaque destination for overlay
                    destPixels[destIdx + 0] = (byte)((srcPixels[srcIdx + 0] * sa + destPixels[destIdx + 0] * (255 - sa)) / 255);
                    destPixels[destIdx + 1] = (byte)((srcPixels[srcIdx + 1] * sa + destPixels[destIdx + 1] * (255 - sa)) / 255);
                    destPixels[destIdx + 2] = (byte)((srcPixels[srcIdx + 2] * sa + destPixels[destIdx + 2] * (255 - sa)) / 255);
                }
                else
                {
                    destPixels[destIdx + 0] = srcPixels[srcIdx + 0]; // B
                    destPixels[destIdx + 1] = srcPixels[srcIdx + 1]; // G
                    destPixels[destIdx + 2] = srcPixels[srcIdx + 2]; // R
                }
                destPixels[destIdx + 3] = 255; // A
            }
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