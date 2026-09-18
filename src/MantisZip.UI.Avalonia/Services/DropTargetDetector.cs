using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Detects the target directory path when the user drops files onto
/// an Explorer window, the desktop, or a #32770 dialog.
/// </summary>
internal static class DropTargetDetector
{
    public enum DropTargetStatus
    {
        None,     // Can't identify
        Success,  // Identified and got valid path
        Warning   // Identified window type but couldn't extract path
    }

    /// <summary>
    /// Detects the target directory path based on the current cursor position.
    /// </summary>
    /// <returns>
    /// A tuple with the detected path (or null) and the detection status.
    /// </returns>
    public static (string? Path, DropTargetStatus Status) DetectTargetDirectory()
    {
        // 1. Find window at cursor position
        var hWnd = NativeMethods.GetWindowUnderCursor();
        App.DebugLog($"[DropTargetDetector] Window under cursor: hWnd=0x{hWnd:X}");
        if (hWnd == nint.Zero)
        {
            App.DebugLog("[DropTargetDetector] No window under cursor");
            return (null, DropTargetStatus.None);
        }

        // 2. Find recognized ancestor and extract path
        return TryGetExplorerPath(hWnd);
    }

    /// <summary>
    /// Attempts to extract the directory path from the given window,
    /// walking up the parent chain if needed to find a recognized window type.
    /// </summary>
    private static (string? Path, DropTargetStatus Status) TryGetExplorerPath(nint hWnd)
    {
        App.DebugLog($"[DropTargetDetector] TryGetExplorerPath: hWnd=0x{hWnd:X}");

        var ancestor = FindRecognizedAncestor(hWnd);
        if (ancestor is null)
        {
            App.DebugLog("[DropTargetDetector] No recognized ancestor found");
            return (null, DropTargetStatus.None);
        }

        var (ancestorHwnd, className) = ancestor.Value;
        App.DebugLog($"[DropTargetDetector] Ancestor: 0x{ancestorHwnd:X} -> {className}");

        return className switch
        {
            "CabinetWClass" => TryGetExplorerPathFromShell(ancestorHwnd),
            "#32770" => TryGetDialogPath(ancestorHwnd),
            "Progman" or "WorkerW" => (Environment.GetFolderPath(Environment.SpecialFolder.Desktop), DropTargetStatus.Success),
            _ => (null, DropTargetStatus.None)
        };
    }

    /// <summary>
    /// Walks up the parent chain from the given window to find a recognized
    /// Explorer window type (CabinetWClass, #32770, Progman/WorkerW).
    /// This is the shared logic used by both OverlayController and DragDropService.
    /// </summary>
    /// <param name="hWnd">Starting window handle.</param>
    /// <returns>The recognized window handle and class name, or null if not found.</returns>
    internal static (nint HWnd, string ClassName)? FindRecognizedAncestor(nint hWnd)
    {
        int maxWalk = 10;
        while (hWnd != nint.Zero && maxWalk-- > 0)
        {
            var cls = NativeMethods.GetWindowClassName(hWnd);
            if (cls is "CabinetWClass" or "#32770" or "Progman" or "WorkerW")
                return (hWnd, cls);
            hWnd = NativeMethods.GetParent(hWnd);
        }
        return null;
    }

    /// <summary>
    /// Uses late-binding COM (ShellWindows CLSID) to find the Explorer
    /// window matching the given HWND and extract its folder path.
    /// </summary>
    internal static (string? Path, DropTargetStatus Status) TryGetExplorerPathFromShell(nint hWnd)
    {
        try
        {
            // ShellWindows CLSID: {9BA05972-F6A8-11CF-A442-00A0C90A8F39}
            var shellWindowsType = Type.GetTypeFromCLSID(
                new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (shellWindowsType == null)
                return (null, DropTargetStatus.Warning);

            dynamic? shellWindows = Activator.CreateInstance(shellWindowsType);
            if (shellWindows == null)
                return (null, DropTargetStatus.Warning);

            // ShellWindows implements IEnumerable — use foreach via dynamic dispatch
            foreach (dynamic ie in shellWindows)
            {
                try
                {
                    nint ieHwnd = (nint)ie.HWND;
                    if (ieHwnd != hWnd)
                        continue;

                    dynamic? doc = ie.Document;
                    if (doc == null)
                        continue;

                    dynamic? folder = doc.Folder;
                    if (folder == null)
                        continue;

                    dynamic? self = folder.Self;
                    if (self == null)
                        continue;

                    string? path = (string?)self.Path;
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        return (path, DropTargetStatus.Success);
                    }
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException)
                {
                    // Some ShellWindows entries may not be accessible;
                    // do not release the COM object — the foreach owns it.
                    Debug.WriteLine($"[DropTargetDetector] COM error enumerating shell window: {ex.Message}");
                }
            }

            return (null, DropTargetStatus.Warning);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[DropTargetDetector] ShellWindows enumeration failed: {ex.Message}");
            return (null, DropTargetStatus.Warning);
        }
    }

    /// <summary>
    /// Attempts to extract the current directory path from a #32770 dialog
    /// (common file dialog, folder picker, etc.) by enumerating child windows.
    /// </summary>
    internal static (string? Path, DropTargetStatus Status) TryGetDialogPath(nint hWnd)
    {
        var path = TryGetDialogPathViaWin32(hWnd);
        if (path is not null)
        {
            return (path, DropTargetStatus.Success);
        }

        return (null, DropTargetStatus.None);
    }

    /// <summary>
    /// Uses EnumChildWindows + GetWindowText to extract the path from
    /// common dialog controls (e.g. ToolbarWindow32, ComboBox32, Edit).
    /// </summary>
    private static string? TryGetDialogPathViaWin32(nint hWnd)
    {
        var resultPath = new StringBuilder(260);
        var found = false;

        NativeMethods.EnumChildProc callback = (nint childHwnd, nint lParam) =>
        {
            var childClass = NativeMethods.GetWindowClassName(childHwnd);

            if (childClass is "ToolbarWindow32" or "ComboBox32")
            {
                // Enumerate grandchildren for Edit controls
                NativeMethods.EnumChildWindows(childHwnd, (nint grandChild, nint _) =>
                {
                    if (NativeMethods.GetWindowClassName(grandChild) == "Edit")
                    {
                        var text = new StringBuilder(260);
                        NativeMethods.GetWindowText(grandChild, text, text.Capacity);
                        var dir = text.ToString();
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            resultPath.Append(dir);
                            found = true;
                            return false; // Stop enumeration
                        }
                    }
                    return true; // Continue enumeration
                }, nint.Zero);

                if (found)
                    return false; // Stop parent enumeration
            }

            if (childClass == "Edit")
            {
                var text = new StringBuilder(260);
                NativeMethods.GetWindowText(childHwnd, text, text.Capacity);
                var dir = text.ToString();
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    resultPath.Append(dir);
                    found = true;
                    return false; // Stop enumeration
                }
            }

            return !found; // Continue if not found
        };

        NativeMethods.EnumChildWindows(hWnd, callback, nint.Zero);

        return found ? resultPath.ToString() : null;
    }
}
