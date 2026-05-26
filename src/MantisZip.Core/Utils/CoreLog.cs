using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace MantisZip.Core.Utils;

/// <summary>
/// DEBUG-only logger for MantisZip.Core. All methods are [Conditional("DEBUG")]
/// so they compile to nothing in RELEASE builds.
/// All output goes to %LOCALAPPDATA%\MantisZip\debug.log with timestamps.
/// CoreLog.Trace is the only method active in RELEASE builds (for hard-to-repro bugs).
/// </summary>
internal static class CoreLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MantisZip", "debug.log");

    private static readonly object _lock = new();

    /// <summary>
    /// 日志脱敏覆盖委托。由 UI 层在初始化时注入（因为 CoreLog 不能直接引用 AppSettings）。
    /// 参数为原始消息字符串，返回脱敏后的消息。为 null 表示不做脱敏。
    /// </summary>
    internal static Func<string, string>? RedactOverride { get; set; }

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

    /// <summary>
    /// 无条件追踪日志（DEBUG 和 RELEASE 都写入）。
    /// 写入 %LOCALAPPDATA%\MantisZip\debug.log（与 CoreLog.Write 同文件）。
    /// 也应用 RedactOverride 脱敏。用于调试进度条等难以复现的问题。
    /// </summary>
    internal static void Trace(string msg)
    {
        // 应用脱敏（由 UI 层注入，null=不脱敏）
        var finalMsg = RedactOverride?.Invoke(msg) ?? msg;

        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            lock (_lock)
            {
                RotateIfNeeded();
                using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {finalMsg}");
                writer.Flush();
            }
        }
        catch (Exception logEx)
        {
            Debug.WriteLine($"CoreLog.Trace write failed: {logEx.Message}");
        }
    }

    internal static void Trace(string fmt, params object[] args)
    {
        Trace(string.Format(fmt, args));
    }

    /// <summary>
    /// 日志文件大小上限（10 MB）。超过时自动轮转。
    /// </summary>
    private const long MaxLogSize = 10L * 1024 * 1024;

    private static void RotateIfNeeded()
    {
        try
        {
            var fileInfo = new FileInfo(LogPath);
            if (fileInfo.Exists && fileInfo.Length > MaxLogSize)
            {
                var backupPath = LogPath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                File.Move(LogPath, backupPath);
            }
        }
        catch { /* 轮转失败不影响继续写入 */ }
    }

    private static void Write(string msg)
    {
        // 应用脱敏（由 UI 层注入，null=不脱敏）
        var finalMsg = RedactOverride?.Invoke(msg) ?? msg;

        var dir = Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        lock (_lock)
        {
            try
            {
                RotateIfNeeded();
                using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {finalMsg}");
                writer.Flush();
            }
            catch (Exception logEx)
            {
                Debug.WriteLine($"CoreLog.Write failed: {logEx.Message}");
            }
        }

    }

    private static string TrimPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        var idx = path.LastIndexOf("\\MantisZip\\", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? path[(idx + 1)..] : Path.GetFileName(path);
    }
}
