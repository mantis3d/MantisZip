namespace MantisZip.Core.Utils;

/// <summary>
/// 应用数据目录（<c>%LOCALAPPDATA%\MantisZip</c>）的唯一解析入口。
///
/// 为什么要有它：Core 里好几个状态文件（<c>favorites.json</c> / <c>path-history.json</c> /
/// <c>debug.log</c> / <c>conflict.log</c>）各自 <c>Path.Combine(LocalApplicationData, "MantisZip")</c>，
/// 于是测试想换落点就得逐个改，实际结果是**一个都没换**：跑一次 Core 测试就会重写用户真实的
/// <c>path-history.json</c>（2026-11-23 实测 mtime 被刷新）。集中到一处之后，测试只需重定向这里。
/// </summary>
internal static class AppDataPaths
{
    private static string? _override;

    /// <summary>
    /// 当前生效的数据目录。生产环境恒为 <c>%LOCALAPPDATA%\MantisZip</c>；
    /// 测试经 <see cref="UseIsolatedDirForTests"/> 重定向到一次性临时目录。
    /// </summary>
    internal static string Dir => _override ?? DefaultDir;

    private static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");

    /// <summary>
    /// 测试钩子：把数据目录重定向到临时目录。返回的句柄恢复原值。
    /// 用法见 <c>tests/MantisZip.Tests/TestAppDataRedirect.cs</c>（程序集级）与各测试类自己的沙箱。
    /// </summary>
    internal static IDisposable UseIsolatedDirForTests(string dir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dir);
        var previous = _override;
        _override = dir;
        return new DirScope(previous);
    }

    private sealed class DirScope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _override = previous;
        }
    }
}
