using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace MantisZip.Core.Utils;

/// <summary>
/// DEBUG-only logger for MantisZip.Core. All methods are [Conditional("DEBUG")]
/// so they compile to nothing in RELEASE builds.
/// Writes to %TEMP%\MantisZip\core.log with timestamps.
/// </summary>
internal static class CoreLog
{
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(), "MantisZip", "core.log");

    private static readonly object _lock = new();

    /// <summary>Log a message (DEBUG only).</summary>
    [Conditional("DEBUG")]
    public static void Info(string msg,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
    {
        Write($"[INF] {TrimPath(file)}:{line} {member} | {msg}");
    }

    /// <summary>Log a formatted message (DEBUG only).</summary>
    [Conditional("DEBUG")]
    public static void Info(string fmt, params object[] args)
    {
        Write($"[INF] {string.Format(fmt, args)}");
    }

    /// <summary>Log an error with exception details (DEBUG only).</summary>
    [Conditional("DEBUG")]
    public static void Error(string msg, Exception? ex = null,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
    {
        var text = $"[ERR] {TrimPath(file)}:{line} {member} | {msg}";
        if (ex != null)
            text += $" | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        Write(text);
    }

    /// <summary>Log entry to a method (DEBUG only).</summary>
    [Conditional("DEBUG")]
    public static void Entry(
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
    {
        Write($"[ENT] {TrimPath(file)}:{line} {member}");
    }

    /// <summary>Log exit from a method (DEBUG only).</summary>
    [Conditional("DEBUG")]
    public static void Exit(
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
    {
        Write($"[EXT] {TrimPath(file)}:{line} {member}");
    }

    private static void Write(string msg)
    {
        var dir = Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        lock (_lock)
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n");
            }
            catch { /* best effort */ }
        }

        Debug.WriteLine($"[MantisZip.Core] {msg}");
    }

    private static string TrimPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        var idx = path.LastIndexOf("\\MantisZip\\", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? path[(idx + 1)..] : Path.GetFileName(path);
    }
}
