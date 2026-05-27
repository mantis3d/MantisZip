using System;
using System.IO;
using System.Windows;
using MantisZip.Core;
using MantisZip.Core.Utils;

namespace MantisZip.UI;

/// <summary>
/// 日志子系统 — LogStartup、Log、LogDebug、TraceLog
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 日志文件大小上限（10 MB）。超过时自动轮转。
    /// </summary>
    private const long MaxLogFileSize = 10L * 1024 * 1024;

    /// <summary>
    /// 检查日志文件大小，超过上限时自动轮转（添加时间戳后缀）。
    /// 线程安全：仅在 try-catch 内单次调用，不强制跨方法同步（日志写入不要求强一致性）。
    /// </summary>
    private static void RotateDebugLogIfNeeded(string logPath)
    {
        try
        {
            var fileInfo = new FileInfo(logPath);
            if (fileInfo.Exists && fileInfo.Length > MaxLogFileSize)
            {
                var backupPath = logPath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                File.Move(logPath, backupPath);
            }
        }
        catch { /* 轮转失败不影响继续写入 */ }
    }

    /// <summary>
    /// 启动/操作日志（写入统一日志文件 %LOCALAPPDATA%\MantisZip\debug.log，不被自动删除）。
    /// 已应用隐私脱敏。
    /// </summary>
    private static void LogStartup(string msg)
    {
        try
        {
            // LogStartup 在 AppSettings 加载之后调用，可以安全读取配置
            var mode = AppSettings.Instance != null
                ? LogRedactor.ParseMode(AppSettings.Instance.LogPrivacyMode)
                : LogPrivacyMode.Off;
            var redacted = LogRedactor.RedactPaths(msg, mode);

            var dir = Path.GetDirectoryName(StartupLog);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            RotateDebugLogIfNeeded(StartupLog);
            using var logStream = new FileStream(StartupLog, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var logWriter = new StreamWriter(logStream);
            logWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}");
            logWriter.Flush();
        }
        catch { }
    }

    public static void Log(string msg)
    {
        try
        {
            if (!AppSettings.Instance.EnableDebugLogging) return;
            var redacted = LogRedactor.RedactPaths(msg,
                LogRedactor.ParseMode(AppSettings.Instance.LogPrivacyMode));
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            RotateDebugLogIfNeeded(LogFile);
            using var logStream = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var logWriter = new StreamWriter(logStream);
            logWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}");
            logWriter.Flush();
        }
        catch { }
    }

    public static void Log(string fmt, params object[] args)
    {
        Log(string.Format(fmt, args));
    }

    /// <summary>
    /// 调试日志：仅当 AppSettings.EnableDebugLogging 开启时写入。
    /// 用于用户开启后帮助排查问题。
    /// </summary>
    public static void LogDebug(string msg)
    {
        try
        {
            if (!AppSettings.Instance.EnableDebugLogging) return;
            var redacted = LogRedactor.RedactPaths(msg,
                LogRedactor.ParseMode(AppSettings.Instance.LogPrivacyMode));
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            RotateDebugLogIfNeeded(LogFile);
            using var logStream = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var logWriter = new StreamWriter(logStream);
            logWriter.WriteLine($"[DBG] [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}");
            logWriter.Flush();
        }
        catch { }
    }

    public static void LogDebug(string fmt, params object[] args)
    {
        LogDebug(string.Format(fmt, args));
    }

    /// <summary>
    /// 无条件写入统一日志文件的追踪日志（不依赖 EnableDebugLogging 设置）。
    /// 用于调试进度条等难以复现的问题。已应用隐私脱敏。
    /// </summary>
    public static void TraceLog(string msg)
    {
        try
        {
            var redacted = LogRedactor.RedactPaths(msg,
                LogRedactor.ParseMode(AppSettings.Instance.LogPrivacyMode));
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            RotateDebugLogIfNeeded(LogFile);
            using var logStream = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var logWriter = new StreamWriter(logStream);
            logWriter.WriteLine($"[TRACE] [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}");
            logWriter.Flush();
        }
        catch { }
    }

    public static void TraceLog(string fmt, params object[] args)
    {
        TraceLog(string.Format(fmt, args));
    }
}
