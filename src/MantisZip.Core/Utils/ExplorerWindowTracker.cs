using System.Diagnostics;
using System.Runtime.InteropServices;

#pragma warning disable CA1416 // This class is Windows-only (P/Invoke + COM)

namespace MantisZip.Core.Utils;

public record ExplorerWindowInfo(string Path, string DisplayName, IntPtr HWND, bool IsActive);

public static class ExplorerWindowTracker
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const string CabinetWClass = "CabinetWClass";
    private const string ExploreWClass = "ExploreWClass";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    /// <summary>
    /// Get the path from a Shell Windows LocationURL string.
    /// </summary>
    private static string? GetPathFromLocationUrl(string? locationUrl)
    {
        if (string.IsNullOrEmpty(locationUrl))
            return null;
        if (!locationUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            !locationUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var uri = new Uri(locationUrl);
            return Uri.UnescapeDataString(uri.LocalPath);
        }
        catch { return null; }
    }

    /// <summary>
    /// Enumerate open Explorer windows.
    /// Uses IShellWindows COM. Falls back to Win32 EnumWindows on failure.
    /// </summary>
    public static List<ExplorerWindowInfo> GetOpenExplorerWindows()
    {
        var result = new List<ExplorerWindowInfo>();
        var foregroundHwnd = GetForegroundWindow();

        // Method 1: COM IShellWindows (primary)
        try { if (GetViaCom(result, foregroundHwnd) > 0) return result; } catch { }

        // Method 2: Win32 window enumeration fallback
        try { GetViaWindowEnum(result, foregroundHwnd); } catch { }

        return result;
    }

    /// <summary>
    /// Method 1: COM IShellWindows — enumerate all registered shell windows.
    /// </summary>
    private static int GetViaCom(List<ExplorerWindowInfo> result, IntPtr foregroundHwnd)
    {
        var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
        if (shellWindowsType == null) return 0;

        var shellWindows = Activator.CreateInstance(shellWindowsType);
        if (shellWindows == null) return 0;

        try
        {
            var count = shellWindowsType.InvokeMember("Count",
                System.Reflection.BindingFlags.GetProperty, null, shellWindows, null) is int c ? c : 0;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var win = shellWindowsType.InvokeMember("Item",
                        System.Reflection.BindingFlags.InvokeMethod, null, shellWindows,
                        new object[] { i });
                    if (win == null) continue;

                    var locationUrl = (string?)win.GetType().InvokeMember("LocationURL",
                        System.Reflection.BindingFlags.GetProperty, null, win, null);
                    var path = GetPathFromLocationUrl(locationUrl);
                    if (path == null) continue;

                    var hwndRaw = win.GetType().InvokeMember("HWND",
                        System.Reflection.BindingFlags.GetProperty, null, win, null);
                    var hwnd = IntPtr.Zero;
                    if (hwndRaw is int hwndInt) hwnd = new IntPtr(hwndInt);
                    else if (hwndRaw is uint hwndUint) hwnd = new IntPtr(hwndUint);

                    result.Add(new ExplorerWindowInfo(
                        path,
                        System.IO.Path.GetFileName(path),
                        hwnd,
                        hwnd == foregroundHwnd));
                }
                catch { }
            }
        }
        finally
        {
            try { Marshal.FinalReleaseComObject(shellWindows); } catch { }
        }

        return result.Count;
    }

    /// <summary>
    /// Method 2: Win32 window enumeration fallback.
    /// Finds CabinetWClass windows (Explorer) and extracts paths.
    /// </summary>
    private static void GetViaWindowEnum(List<ExplorerWindowInfo> result, IntPtr foregroundHwnd)
    {
        var seenPids = new HashSet<uint>();
        var syncLock = new object();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var className = new System.Text.StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            var cls = className.ToString();

            if (cls != CabinetWClass && cls != ExploreWClass) return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            lock (syncLock) { if (!seenPids.Add(pid)) return true; }

            try
            {
                var proc = Process.GetProcessById((int)pid);
                if (!proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                    return true;

                // Get window title — may contain folder path info
                var title = new System.Text.StringBuilder(512);
                GetWindowText(hWnd, title, title.Capacity);
                var windowTitle = title.ToString();

                // Try to find the folder path from the title by looking for common patterns
                var path = TryExtractPathFromExplorerTitle(windowTitle);
                if (path != null && System.IO.Path.IsPathRooted(path))
                {
                    var isActive = hWnd == foregroundHwnd;
                    lock (syncLock)
                    {
                        result.Add(new ExplorerWindowInfo(
                            path,
                            System.IO.Path.GetFileName(path),
                            hWnd,
                            isActive));
                    }
                }
            }
            catch { }

            return true;
        }, IntPtr.Zero);
    }

    /// <summary>
    /// Try to extract a folder path from the Explorer window title.
    /// Window title formats (locale-dependent):
    ///   EN: "Documents - File Explorer"
    ///   ZH: "文档 - 文件资源管理器"
    ///   Generic: "FolderName"
    /// Only returns rooted paths; partial extractions are returned as-is.
    /// </summary>
    private static string? TryExtractPathFromExplorerTitle(string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return null;

        var title = windowTitle.Trim();

        // Strip common suffixes
        var suffixes = new[] { " - File Explorer", " - 文件资源管理器", " - Explorer", " - 资源管理器" };
        foreach (var suffix in suffixes)
        {
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                title = title.Substring(0, title.Length - suffix.Length).Trim();
                break;
            }
        }

        if (string.IsNullOrEmpty(title)) return null;

        // If the title already looks like a path (rare), return it
        if (System.IO.Path.IsPathRooted(title))
            return title;

        // If the title is just a folder name, we can't determine the full path
        // Return the folder name as-is — the calling code can use it as display name
        return title;
    }

    /// <summary>
    /// Get the active (foreground) Explorer window's folder path.
    /// </summary>
    public static string? GetActiveExplorerPath()
    {
        try
        {
            var windows = GetOpenExplorerWindows();
            // 优先前台活动窗口；否则返回枚举到的第一个资源管理器窗口。
            // 注意：MantisZip 打开对话框时前台窗口是 MantisZip 自己，资源管理器已不可能是 IsActive，
            // 因此此处不能只认 IsActive —— 否则该来源永远返回 null。
            var active = windows.FirstOrDefault(w => w.IsActive);
            if (active != null) return active.Path;
            return windows.FirstOrDefault()?.Path;
        }
        catch { return null; }
    }
}