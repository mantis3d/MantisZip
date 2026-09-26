using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 进程退出诊断器（纯观察，零行为变更）。
///
/// 背景：「点击关闭后界面消失但后台仍残留进程」间歇性复现（右键菜单操作后高发），
/// 黑盒自动化测试未能触发。本类在运行期持续记录窗口生命周期与僵尸状态，
/// 下次复发时 lifecycle.log 会直接点名泄漏窗口。
///
/// 记录内容：
///  1. 启动条目（命令行/PID/初始 ShutdownMode）
///  2. 窗口打开/关闭差分（含不可见窗口 —— 拖拽覆层、提权 tempOwner 等）
///  3. ShutdownRequested 事件（所有 desktop.Shutdown() 调用都会经过这里）
///  4. 僵尸状态：无任何可见窗口但进程存活且已无自动退出可能，持续 ≥6s 时记录完整窗口转储
///  5. UI 线程 / AppDomain / Task 未观察异常（不改变 Handled/Observed 状态）
///
/// 日志：%LOCALAPPDATA%\MantisZip\lifecycle.log（无条件写入，不受 EnableDebugLogging 门控；
/// 路径经 LogRedactor 脱敏；超过 5MB 自动轮转为 .bak）。定位根因后整体删除本文件即可。
/// </summary>
internal static class LifetimeDiagnostics
{
    private const int TickIntervalMs = 2000;
    private const int ZombieGraceTicks = 3;          // 6s：过滤对话框切换的正常间隙
    private const int ZombieRepeatTicks = 8;         // 首次记录后每 ~16s 重申一次
    private const long MaxLogSize = 5L * 1024 * 1024;

    private static readonly object Lock = new();
    private static IClassicDesktopStyleApplicationLifetime? _desktop;
    private static DispatcherTimer? _timer;
    private static Dictionary<int, string> _lastSnapshot = new();
    private static int _zombieTicks;
    private static int _sinceLastZombieLog;

    /// <summary>在 OnFrameworkInitializationCompleted 的 desktop 分支内调用一次。</summary>
    public static void Install(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;

        var args = Environment.GetCommandLineArgs();
        WriteLog($"── app start pid={Environment.ProcessId} mode={desktop.ShutdownMode} args=[{string.Join(" ", args)}]");

        // 所有 desktop.Shutdown() 与系统关机会话都会先经过此事件
        desktop.ShutdownRequested += (_, e) =>
            WriteLog($"ShutdownRequested cancel={e.Cancel} mode={desktop.ShutdownMode} windows={DumpWindows()}");

        // UI 线程未处理异常（async void 处理器抛出会到这里；只观察，不改 Handled）
        Dispatcher.UIThread.UnhandledException += (_, e) =>
            WriteLog($"UIThread EXCEPTION {e.Exception.GetType().Name}: {e.Exception.Message}\n{e.Exception.StackTrace}");

        AppDomain.CurrentDomain.UnhandledException += (_, args2) =>
            WriteLog($"DOMAIN EXCEPTION terminating={args2.IsTerminating} " +
                     $"{(args2.ExceptionObject as Exception)?.GetType().Name}: {(args2.ExceptionObject as Exception)?.Message}");

        // 只观察不 SetObserved（保持默认策略不变）
        TaskScheduler.UnobservedTaskException += (_, args2) =>
            WriteLog($"UNOBSERVED TASK {(args2.Exception?.GetBaseException())?.GetType().Name}: {args2.Exception?.GetBaseException()?.Message}");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickIntervalMs) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    /// <summary>供对话框等关键路径记录决策点（无条件写入 lifecycle.log）。</summary>
    public static void Log(string msg) => WriteLog(msg);

    private static void OnTick()
    {
        var desktop = _desktop;
        if (desktop == null) return;

        try
        {
            var windows = desktop.Windows.ToList();

            // ── 差分：新出现的窗口 / 已消失的窗口 ──
            var current = new Dictionary<int, string>();
            foreach (var w in windows)
                current[w.GetHashCode()] = Describe(w);

            foreach (var kv in current)
                if (!_lastSnapshot.ContainsKey(kv.Key))
                    WriteLog($"WINDOW OPEN  {kv.Value} total={windows.Count} mode={desktop.ShutdownMode}");
            foreach (var kv in _lastSnapshot)
                if (!current.ContainsKey(kv.Key))
                    WriteLog($"WINDOW CLOSE {kv.Value} total={windows.Count} mode={desktop.ShutdownMode}");
            _lastSnapshot = current;

            // ── 僵尸检测：无可见窗口 + 进程已无自动退出路径 ──
            bool anyVisible = windows.Any(w => w.IsVisible);
            bool mainWindowGone = desktop.MainWindow == null || !desktop.MainWindow.IsVisible;
            bool zombieState = !anyVisible &&
                               (desktop.ShutdownMode == ShutdownMode.OnExplicitShutdown || mainWindowGone);

            if (!zombieState)
            {
                _zombieTicks = 0;
                _sinceLastZombieLog = 0;
                return;
            }

            _zombieTicks++;
            _sinceLastZombieLog++;

            if (_zombieTicks == ZombieGraceTicks)
            {
                WriteLog($"ZOMBIE STATE ENTERED after {_zombieTicks * TickIntervalMs / 1000.0:0.#}s " +
                         $"mode={desktop.ShutdownMode} mainWindowGone={mainWindowGone} windows={DumpWindows()}");
            }
            else if (_zombieTicks > ZombieGraceTicks && _sinceLastZombieLog >= ZombieRepeatTicks)
            {
                _sinceLastZombieLog = 0;
                WriteLog($"ZOMBIE STILL ALIVE ticks={_zombieTicks} ({_zombieTicks * TickIntervalMs / 1000.0:0}s) " +
                         $"windows={DumpWindows()}");
            }
        }
        catch (Exception ex)
        {
            WriteLog($"OnTick error {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Describe(Window w)
    {
        try
        {
            var title = w.Title ?? "";
            return $"{w.GetType().Name}#{w.GetHashCode():X} vis={w.IsVisible} taskbar={w.ShowInTaskbar} " +
                   $"active={w.IsActive} state={w.WindowState} title='{title}'";
        }
        catch (Exception ex)
        {
            return $"{w.GetType().Name}#{w.GetHashCode():X} <describe failed: {ex.Message}>";
        }
    }

    private static string DumpWindows()
    {
        var desktop = _desktop;
        if (desktop == null) return "[]";
        try
        {
            var list = desktop.Windows.Select(Describe).ToList();
            return list.Count == 0 ? "[none]" : "[" + string.Join("; ", list) + "]";
        }
        catch (Exception ex)
        {
            return $"<dump failed: {ex.Message}>";
        }
    }

    /// <summary>无条件写入独立诊断日志（路径脱敏 + 5MB 轮转），失败静默。</summary>
    private static void WriteLog(string msg)
    {
        try
        {
            var logPath = Path.Combine(AppSettings.DataDir, "lifecycle.log");
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            lock (Lock)
            {
                try
                {
                    var fi = new FileInfo(logPath);
                    if (fi.Exists && fi.Length > MaxLogSize)
                        File.Move(logPath, logPath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak");
                }
                catch { /* 轮转失败不影响写入 */ }

                var redacted = LogRedactor.RedactPaths(msg, LogPrivacyMode.FilenameOnly);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [LIFE] {redacted}\n");
            }
        }
        catch { /* 诊断日志绝不影响主流程 */ }
    }
}
