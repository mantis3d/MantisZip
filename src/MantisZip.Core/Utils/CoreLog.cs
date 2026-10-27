using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace MantisZip.Core.Utils;

/// <summary>
/// Logger for MantisZip.Core.
/// Info/Error/Entry/Exit are [Conditional("DEBUG")] — compiled away in RELEASE builds.
/// Trace is active in all builds.
/// Info/Error/Entry/Exit/Trace are gated by DiagnosticsEnabled (set from AppSettings.EnableDebugLogging at startup).
/// Output goes to %LOCALAPPDATA%\MantisZip\debug.log with timestamps.
/// <para>
/// 例外：<see cref="ConflictTrace"/> 不受 DiagnosticsEnabled 门控、RELEASE 也写入，
/// 单独落到 %LOCALAPPDATA%\MantisZip\conflict.log（冲突弹窗卡死是偶发问题，需要出厂即开的最小证据链）。
/// </para>
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

    /// <summary>
    /// 全局诊断开关。由 UI 层在启动时从 AppSettings.EnableDebugLogging 设置。
    /// true=允许写入日志，false=所有日志静默。重启后生效。
    /// 控制 CoreLog 的所有 Info/Error/Entry/Exit/Trace 方法。
    /// </summary>
    internal static bool DiagnosticsEnabled { get; set; } = true;

    /// <summary>
    /// 初始化 CoreLog 的诊断开关与脱敏委托。
    /// UI 层在启动时和设置变更时调用，统一注入点，避免分散设置导致遗漏。
    /// </summary>
    /// <param name="diagnosticsEnabled">是否启用调试日志（对应 AppSettings.EnableDebugLogging）。</param>
    /// <param name="privacyMode">脱敏模式字符串（"off"/"filename"/"extension"/"full"）。</param>
    /// <param name="redactFunc">脱敏函数（可选，为 null 时使用内置 LogRedactor）。</param>
    internal static void Initialize(bool diagnosticsEnabled, string privacyMode, Func<string, string>? redactFunc = null)
    {
        DiagnosticsEnabled = diagnosticsEnabled;
        RedactOverride = redactFunc ?? (msg => Utils.LogRedactor.RedactPaths(msg, Utils.LogRedactor.ParseMode(privacyMode)));
    }

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
    /// 追踪日志（DEBUG 和 RELEASE 都写入）。
    /// 写入 %LOCALAPPDATA%\MantisZip\debug.log（与 CoreLog.Write 同文件）。
    /// 也应用 RedactOverride 脱敏，受 DiagnosticsEnabled 开关控制。
    /// </summary>
    internal static void Trace(string msg)
    {
        if (!DiagnosticsEnabled) return;

        // 应用脱敏（由 UI 层注入，null=不脱敏）
        WriteTo(LogPath, MaxLogSize, RedactOverride?.Invoke(msg) ?? msg);
    }

    internal static void Trace(string fmt, params object[] args)
    {
        Trace(string.Format(fmt, args));
    }

    /// <summary>
    /// 冲突决策链路追踪：DEBUG 和 RELEASE 都写入，且**不受 <see cref="DiagnosticsEnabled"/> 门控**。
    /// <para>
    /// 单独写入 %LOCALAPPDATA%\MantisZip\conflict.log（2 MB 轮转），与 debug.log 分离：
    /// 「解压/压缩卡在冲突提示」是偶发问题，用户通常没开调试日志，需要一条出厂即开的证据链，
    /// 一次复现就能判断是卡在等对话框、卡在冲突闸门，还是根本没进入冲突链路。
    /// </para>
    /// 与 <see cref="Trace"/> 一样应用 <see cref="RedactOverride"/> 脱敏（不泄露完整路径）。
    /// </summary>
    internal static void ConflictTrace(string msg)
    {
        WriteTo(ConflictLogPath, MaxConflictLogSize, RedactOverride?.Invoke(msg) ?? msg);
    }

    /// <summary>格式化版 <see cref="ConflictTrace(string)"/>（参数允许为 null，脱敏后再写入）。</summary>
    internal static void ConflictTrace(string fmt, params object?[] args)
    {
        ConflictTrace(string.Format(fmt, args));
    }

    /// <summary>
    /// 冲突日志路径。默认 %LOCALAPPDATA%\MantisZip\conflict.log；
    /// 测试可覆盖为临时目录（进程级设置，测试类内串行使用）。
    /// </summary>
    internal static string ConflictLogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MantisZip", "conflict.log");

    /// <summary>
    /// 日志文件大小上限（10 MB）。超过时自动轮转。
    /// </summary>
    private const long MaxLogSize = 10L * 1024 * 1024;

    /// <summary>冲突日志文件大小上限（2 MB）。冲突证据链体积小，轮转更积极。</summary>
    private const long MaxConflictLogSize = 2L * 1024 * 1024;

    private static void RotateIfNeeded(string path, long maxSize)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Exists && fileInfo.Length > maxSize)
            {
                var backupPath = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                File.Move(path, backupPath);
            }
        }
        catch { /* 轮转失败不影响继续写入 */ }
    }

    /// <summary>
    /// 写入指定日志文件（自动建目录、超限轮转）。调用方负责脱敏与门控。
    /// </summary>
    private static void WriteTo(string path, long maxSize, string finalMsg)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            lock (_lock)
            {
                RotateIfNeeded(path, maxSize);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {finalMsg}");
                writer.Flush();
            }
        }
        catch (Exception logEx)
        {
            Debug.WriteLine($"CoreLog.WriteTo({Path.GetFileName(path)}) failed: {logEx.Message}");
        }
    }

    private static void Write(string msg)
    {
        if (!DiagnosticsEnabled)
            return;

        // 应用脱敏（由 UI 层注入，null=不脱敏）
        WriteTo(LogPath, MaxLogSize, RedactOverride?.Invoke(msg) ?? msg);
    }

    private static string TrimPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        var idx = path.LastIndexOf("\\MantisZip\\", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? path[(idx + 1)..] : Path.GetFileName(path);
    }
}
