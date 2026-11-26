using System.Runtime.CompilerServices;
using MantisZip.Core.Utils;

namespace MantisZip.Tests;

/// <summary>
/// Core 测试程序集级安全网：把 <see cref="AppDataPaths.Dir"/> 在整个测试程序集生命周期内
/// 重定向到一次性临时目录。
///
/// 为什么需要（2026-11-23 实测事故）：<c>PathHistoryManagerTests</c> 构造函数第一句就是
/// <c>PathHistoryManager.Clear()</c>，而 <c>PathHistoryManager</c> 当时把落点固化成
/// <c>%LOCALAPPDATA%\MantisZip\path-history.json</c>。跑一次这个测试类，用户真实的
/// <c>path-history.json</c> 就被清空重写（实测 mtime 11:41:36 → 12:53:43、MD5 变了）。
/// 「最近打开的目录」是用户攒了很久的数据，测试没有任何理由碰它。
///
/// 与 UI 测试程序集的 <c>AppSettingsTestRedirect</c> 分工相同：单靠「谁需要谁自己建沙箱」会漏，
/// 漏掉时必须失败在明处而不是悄悄改写用户磁盘。需要精确控制目录内容的用例仍应自己
/// <c>using var scope = AppDataPaths.UseIsolatedDirForTests(...)</c>（它会在这一层之上再重定向）。
/// </summary>
internal static class TestAppDataRedirect
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mz-core-tests-appdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        SandboxDir = dir;

        // 句柄刻意不释放：随进程结束一起消失，避免「某个测试先跑完就提前解除重定向」
        AppDataPaths.UseIsolatedDirForTests(dir);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 进程退出阶段清理失败无影响 */ }
        };
    }

    /// <summary>本程序集的临时数据目录（调试用：跑测试时想确认写了哪里，看这个路径）。</summary>
    internal static string SandboxDir { get; private set; } = "";
}
