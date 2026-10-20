using Microsoft.Win32;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// HKCU 注册表访问的唯一入口。
/// 生产：路径原样落盘。
/// 测试：<see cref="UseSandbox"/> 后，所有以 "Software\" 开头的路径被重定向到
/// "Software\{sandbox}\..."，从而不污染真实的文件关联/右键菜单注册；
/// 沙箱态下传入**非** "Software\" 的路径会直接抛异常（fail-closed，见计划 Interfaces 约束）。
/// </summary>
internal static class RegistryHost
{
    private const string Root = @"Software";
    private static string? _sandbox;   // 相对 Software 的一段，如 "MantisZip\AssocTestSandbox"

    /// <summary>测试注入缝：非 null 时 Delete 在真正删除之前先调用该委托。
    /// 委托抛出的异常直接向上传播（用于模拟 UCPD / 权限拒绝）；
    /// 委托正常返回则继续执行真实删除。仅测试赋值；生产路径恒为 null。</summary>
    internal static Action<string>? DeleteInterceptionForTesting;

    internal static RegistryKey? Open(string fullPath, bool writable = false)
        => Registry.CurrentUser.OpenSubKey(Map(fullPath), writable);

    internal static RegistryKey Create(string fullPath)
        => Registry.CurrentUser.CreateSubKey(Map(fullPath));

    internal static void Delete(string fullPath)
    {
        DeleteInterceptionForTesting?.Invoke(fullPath);   // 测试注入：可在此抛出以模拟 UCPD / 权限拒绝
        Registry.CurrentUser.DeleteSubKeyTree(Map(fullPath), throwOnMissingSubKey: false);
    }

    /// <summary>测试专用：重定向到沙箱（参数为相对 Software 的路径段）。</summary>
    internal static void UseSandbox(string sandboxRelativeToSoftware) => _sandbox = sandboxRelativeToSoftware;

    /// <summary>测试专用：恢复生产行为。</summary>
    internal static void Reset() => _sandbox = null;

    private static string Map(string fullPath)
    {
        if (_sandbox == null) return fullPath;   // 生产：路径原样落盘，行为完全不变
        // 沙箱态 fail-closed：只映射 "Software\" 下的路径；其余路径一律抛异常，
        // 绝不放行到真实 HKCU（机械封死"测试在沙箱态下误写真实注册表"的逃逸面）。
        // 前缀判断须精确到**分隔符级**：SoftwareX\Foo 这类"以 Software 开头但并非 Software\ 前缀"
        // 的路径也必须抛异常，不得被静默改写进沙箱。
        if (!(fullPath.StartsWith(@"Software\", StringComparison.OrdinalIgnoreCase)
              || fullPath.Equals("Software", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $@"RegistryHost sandbox only maps paths under 'Software\'; got: {fullPath}");
        var rest = fullPath.Substring(Root.Length).TrimStart('\\');
        return string.IsNullOrEmpty(rest)
            ? $@"{Root}\{_sandbox}"
            : $@"{Root}\{_sandbox}\{rest}";
    }
}
