using System;
using System.IO;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// CoreLog.ConflictTrace —— 冲突证据链日志的契约测试。
/// 关键约定：**不受 DiagnosticsEnabled 门控**（用户没开调试日志、RELEASE 构建也要写）、
/// 复用 RedactOverride 脱敏（不泄露完整路径）、写到 ConflictLogPath 指定的文件。
/// 本类会改进程级静态状态，因此每个用例都指向临时目录并在 Dispose 中恢复。
/// </summary>
public sealed class CoreLogConflictTraceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _logPath;
    private readonly string _originalConflictLogPath;
    private readonly Func<string, string>? _originalRedactOverride;
    private readonly bool _originalDiagnosticsEnabled;

    public CoreLogConflictTraceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MantisZipTests", "conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logPath = Path.Combine(_tempDir, "conflict.log");

        _originalConflictLogPath = CoreLog.ConflictLogPath;
        _originalRedactOverride = CoreLog.RedactOverride;
        _originalDiagnosticsEnabled = CoreLog.DiagnosticsEnabled;

        CoreLog.ConflictLogPath = _logPath;
    }

    public void Dispose()
    {
        CoreLog.ConflictLogPath = _originalConflictLogPath;
        CoreLog.RedactOverride = _originalRedactOverride;
        CoreLog.DiagnosticsEnabled = _originalDiagnosticsEnabled;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 清理失败不影响断言结果 */ }
    }

    // ════════════════════════════════════════════════════════════════
    //  门控 — 关掉调试日志也必须写
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ConflictTrace_WritesWhenDiagnosticsDisabled()
    {
        CoreLog.DiagnosticsEnabled = false;

        CoreLog.ConflictTrace("gate-wait thread=7 batch=1 file='a.zip'");

        Assert.True(File.Exists(_logPath));
        var content = File.ReadAllText(_logPath);
        Assert.Contains("gate-wait thread=7 batch=1", content);
    }

    [Fact]
    public void ConflictTrace_FormattedOverload_SubstitutesArguments()
    {
        CoreLog.DiagnosticsEnabled = false;

        CoreLog.ConflictTrace("decision thread={0} result={1} elapsedMs={2}", 5, "renamed", 42);

        var content = File.ReadAllText(_logPath);
        Assert.Contains("decision thread=5 result=renamed elapsedMs=42", content);
    }

    // ════════════════════════════════════════════════════════════════
    //  脱敏 — 复用 UI 层注入的 RedactOverride
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ConflictTrace_AppliesRedactOverride()
    {
        CoreLog.DiagnosticsEnabled = false;
        CoreLog.RedactOverride = msg => msg.Replace(
            "C:\\Users\\Admin\\secret", "[REDACTED]", StringComparison.OrdinalIgnoreCase);

        CoreLog.ConflictTrace("dialog-open file='C:\\Users\\Admin\\secret\\a.zip'");

        var content = File.ReadAllText(_logPath);
        Assert.Contains("[REDACTED]", content);
        Assert.DoesNotContain("secret", content);
    }
}
