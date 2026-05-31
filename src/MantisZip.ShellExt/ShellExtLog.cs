using System.Diagnostics;

namespace MantisZip.ShellExt;

/// <summary>
/// Logger for the ShellExt COM component.
/// Outputs to OutputDebugString (captured via DebugView) and optionally to a file
/// at %LOCALAPPDATA%\MantisZip\ShellExt\log.txt for persistent offline review.
///
/// Thread-safe. Auto-creates log directory. Rotates at ~1 MB.
/// Prefixes every line with [ShellExt] PID TID timestamp for traceability.
/// </summary>
internal static class ShellExtLog
{
    private static readonly object _lock = new();
    private static string? _logPath;
    private static bool _initDone;

    /// <summary>Log an informational message.</summary>
    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    /// <summary>Log a warning message.</summary>
    public static void Warn(string message)
    {
        Write("WARN", message, null);
    }

    /// <summary>Log an error message with optional exception detail.</summary>
    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", message, ex);
    }

    private static void Write(string level, string message, Exception? ex)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        int pid = Environment.ProcessId;
        int tid = Environment.CurrentManagedThreadId;
        string exSuffix = ex != null ? $" | {ex.GetType().Name}: {ex.Message}" : "";

        // Build line — keep it parseable: [ShellExt] LEVEL PID TID HH:mm:ss.fff | message
        string line = $"[ShellExt] {level} {pid} {tid} {timestamp} | {message}{exSuffix}";

        // Always output to debug (capturable via DebugView, WinDbg, etc.)
        NativeMethods.OutputDebugString(line);

        // Also write to file for persistent review
        WriteToFile(line);
    }

    private static void WriteToFile(string line)
    {
        // Lazy init to avoid doing work in the static constructor (which runs inside Explorer)
        if (!_initDone)
        {
            lock (_lock)
            {
                if (!_initDone)
                {
                    try
                    {
                        string tempDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "MantisZip", "ShellExt");
                        Directory.CreateDirectory(tempDir);
                        _logPath = Path.Combine(tempDir, "log.txt");

                        // Rotate if too large
                        var fi = new FileInfo(_logPath);
                        if (fi.Exists && fi.Length > 1_048_576) // 1 MB
                        {
                            string archived = Path.Combine(tempDir, $"log.{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                            try { File.Move(_logPath, archived); } catch { }
                        }
                    }
                    catch
                    {
                        // Can't log the logging failure — just give up on file I/O
                    }
                    _initDone = true;
                }
            }
        }

        if (_logPath == null) return;

        // Append with lock to prevent interleaved writes from concurrent Explorer threads
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch
            {
                // Best-effort file logging — don't crash Explorer over a log write
            }
        }
    }
}
