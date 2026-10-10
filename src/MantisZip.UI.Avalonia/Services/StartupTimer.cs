using System.Diagnostics;
using System.Text;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 启动耗时打点器：纯内存收集（mark 名为字符串字面量、零堆分配追加、收集期零 I/O），
/// 窗口首帧后一次性 flush 到 %LOCALAPPDATA%\MantisZip\startup-trace.log。
/// 设计约束：打点期间禁止触碰 App.DebugLog / AppSettings 等可能耗时或递归依赖的组件；
/// 任何异常静默置 _disabled，绝不影响启动主流程。
/// 消费方：startup-preview-defer 计划阶段一（基线测量）+ startup-native-splash 计划（共享设施）。
/// </summary>
internal static class StartupTimer
{
    /// <summary>单条打点记录（距 T0 累计 + 相对上一 mark 的增量）。</summary>
    internal readonly record struct MarkInfo(string Name, double CumulativeMs, double DeltaMs);

    private static readonly List<MarkInfo> Marks = new(64);
    private static readonly object Gate = new();
    private static Stopwatch? _sw;
    private static bool _begun;
    private static bool _flushed;
    private static bool _disabled;
    private static long _lastTicks;
    private static double _preMainMs; // 进程启动 → Main.Entry 的墙钟近似（毫秒）

    /// <summary>Main 第一行调用：记录进程启动基线并启动 T0，随后打 "Main.Entry"。</summary>
    public static void Begin()
    {
        if (_disabled || _begun) return;
        try
        {
            // 进程启动时刻 → Stopwatch 域的 pre-Main 基线（精度 ±10ms 量级，对 3s 量级足够）
            var procStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            _preMainMs = Math.Max(0, (DateTime.UtcNow - procStart).TotalMilliseconds);
            _sw = Stopwatch.StartNew();
            _lastTicks = 0;
            _begun = true;
            AddMark("Main.Entry");
        }
        catch { _disabled = true; }
    }

    /// <summary>记录一个阶段点（相对上一 mark 的增量 + 相对 T0 的累计）。</summary>
    public static void Mark(string name)
    {
        if (_disabled || !_begun) return;
        try { AddMark(name); } catch { _disabled = true; }
    }

    private static void AddMark(string name)
    {
        var sw = _sw!;
        var ticks = sw.ElapsedTicks;
        var freq = (double)Stopwatch.Frequency;
        var cumulativeMs = _preMainMs + ticks / freq * 1000.0;
        var deltaMs = (ticks - _lastTicks) / freq * 1000.0;
        _lastTicks = ticks;
        lock (Gate)
        {
            if (_flushed) return;
            Marks.Add(new MarkInfo(name, cumulativeMs, deltaMs));
        }
    }

    /// <summary>
    /// 格式化并追加写入 startup-trace.log（幂等，仅首次生效）。
    /// 窗口首帧后调用一次；App 退出前可再调（no-op）兜底 CLI 慢路径。
    /// </summary>
    public static void Flush(string reason)
    {
        if (_disabled || !_begun) return;
        string text;
        lock (Gate)
        {
            if (_flushed) return;
            _flushed = true;
            text = FormatTrace(reason);
        }
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "startup-trace.log");
            File.AppendAllText(path, text + Environment.NewLine);
            TrimLogFile(path, maxEntries: 20);
        }
        catch { /* flush 失败不影响主流程 */ }
    }

    private static string FormatTrace(string reason)
    {
        var snapshot = SnapshotForTest();
        var sb = new StringBuilder();
        sb.AppendLine($"==== MantisZip Startup Trace | {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | reason={reason} ====");
        sb.AppendLine($"os: {Environment.OSVersion.Version} | logicalCpu: {Environment.ProcessorCount}");
        try
        {
            // flush 发生在窗口首帧之后（测量窗口之外），读设置是安全的
            var mode = Core.Utils.LogRedactor.ParseMode(Models.AppSettings.Load().LogPrivacyMode);
            sb.AppendLine($"cmdline: {Core.Utils.LogRedactor.RedactPaths(Environment.CommandLine, mode)}");
        }
        catch { }
        sb.AppendLine($"  {"phase",-28} {"delta",10} {"cumulative",12}");
        foreach (var m in snapshot)
            sb.AppendLine($"  {m.Name,-28} {m.DeltaMs,9:F1}ms {m.CumulativeMs,10:F1}ms");
        var total = snapshot.Count > 0 ? snapshot[^1].CumulativeMs : 0;
        sb.AppendLine($"TOTAL to last mark = {total:F1}ms");
        // 预览专项合计与占比（直接对应阶段二门控决策规则）
        double previewMs = 0;
        foreach (var m in snapshot)
            if (m.Name.StartsWith("Preview.", StringComparison.Ordinal)) previewMs += m.DeltaMs;
        if (previewMs > 0)
            sb.AppendLine($"preview-eager total = {previewMs:F1}ms ({previewMs / Math.Max(total, 1) * 100:F1}%)");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>只保留最近 maxEntries 条样本（每条以 "==== MantisZip Startup Trace" 起始）。</summary>
    private static void TrimLogFile(string path, int maxEntries)
    {
        var all = File.ReadAllLines(path);
        var starts = new List<int>();
        for (int i = 0; i < all.Length; i++)
            if (all[i].StartsWith("==== MantisZip Startup Trace", StringComparison.Ordinal))
                starts.Add(i);
        if (starts.Count <= maxEntries) return;
        var cut = starts[starts.Count - maxEntries];
        File.WriteAllLines(path, all.Skip(cut));
    }

    // ── 测试钩子（internal，经 InternalsVisibleTo）──

    /// <summary>当前全部 mark 的快照（测试与 FormatTrace 共用）。</summary>
    internal static IReadOnlyList<MarkInfo> SnapshotForTest()
    {
        lock (Gate) return Marks.ToArray();
    }

    /// <summary>重置全部状态（测试隔离用）。</summary>
    internal static void ResetForTest()
    {
        lock (Gate)
        {
            Marks.Clear();
            _sw = null;
            _begun = false;
            _flushed = false;
            _disabled = false;
            _lastTicks = 0;
            _preMainMs = 0;
        }
    }

    /// <summary>标记 flush 完成但不写文件（测试用）。</summary>
    internal static void FlushForTest()
    {
        lock (Gate) _flushed = true;
    }

    /// <summary>模拟异常导致的永久禁用（测试用）。</summary>
    internal static void SimulateFailureForTest() => _disabled = true;
}
