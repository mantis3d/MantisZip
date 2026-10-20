using MantisZip.UI.Avalonia.Services;
using Microsoft.Win32;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// RegistryHost 单元测试：验证测试沙箱重定向、fail-closed 路径映射与 Delete 注入缝。
/// 所有用例均在沙箱态下运行，绝不写入真实文件关联注册表；
/// 挂 [Collection("RegistryTests")] 与既有注册表测试串行执行（避免并发 Registry 读写冲突）。
/// </summary>
[Collection("RegistryTests")]
public class RegistryHostTests : IDisposable
{
    // 沙箱相对 Software 的路径段（UseSandbox 参数）
    private const string SandboxRelativeToSoftware = @"MantisZip\AssocTestSandbox";

    // 沙箱绝对路径（供直接 Registry 原语清理，规避 RegistryHost 状态依赖）
    private const string SandboxRootAbsolute = @"Software\MantisZip\AssocTestSandbox";

    // 探测路径：即使重定向失效也只落在 MantisZip 自有子树，绝不污染 Software\Classes 文件关联
    private const string ProbeRelative = @"Software\MantisZip\RegistryHostProbe";

    // 探测键被重定向后的真实落点
    private const string ProbeSandboxAbsolute = SandboxRootAbsolute + @"\MantisZip\RegistryHostProbe";

    public RegistryHostTests()
    {
        // 先进入沙箱态：构造后所有 RegistryHost 调用都被重定向，绝不写真实文件关联
        RegistryHost.UseSandbox(SandboxRelativeToSoftware);
        // 幂等复位注入缝（防上一用例异常退出遗留）
        RegistryHost.DeleteInterceptionForTesting = null;
        // 幂等清理上次崩溃残留的沙箱子树（直接走 Registry 原语，不经 RegistryHost）
        Registry.CurrentUser.DeleteSubKeyTree(SandboxRootAbsolute, throwOnMissingSubKey: false);
    }

    public void Dispose()
    {
        // 幂等复位注入缝 + 恢复生产行为，再清沙箱
        RegistryHost.DeleteInterceptionForTesting = null;
        RegistryHost.Reset();
        // Reset 后 RegistryHost.Delete 会落到真实路径，故用 Registry 原语清理沙箱
        Registry.CurrentUser.DeleteSubKeyTree(SandboxRootAbsolute, throwOnMissingSubKey: false);
    }

    // ──────────────────────────────────────────────
    // 沙箱重定向：写 / 读 / 删 均落在沙箱下
    // ──────────────────────────────────────────────

    [Fact]
    public void Create_WhenSandboxed_WritesUnderSandbox_NotRealPath()
    {
        using (var key = RegistryHost.Create(ProbeRelative))
        {
            Assert.NotNull(key);
            key!.SetValue("Value", "sandboxed");
        }

        // 真实落点在沙箱下
        using (var sandboxKey = Registry.CurrentUser.OpenSubKey(ProbeSandboxAbsolute))
        {
            Assert.NotNull(sandboxKey);
            Assert.Equal("sandboxed", sandboxKey!.GetValue("Value"));
        }

        // 生产态路径（未经重定向）不存在 —— 证明写入被重定向而非落真实注册表
        using var realKey = Registry.CurrentUser.OpenSubKey(ProbeRelative);
        Assert.Null(realKey);
    }

    [Fact]
    public void Open_WhenSandboxed_ReadsSandboxValue()
    {
        using (var key = RegistryHost.Create(ProbeRelative))
            key!.SetValue("Value", "read-back");

        using var read = RegistryHost.Open(ProbeRelative);
        Assert.NotNull(read);
        Assert.Equal("read-back", read!.GetValue("Value"));
    }

    [Fact]
    public void Delete_WhenSandboxed_RemovesSandboxSubtree()
    {
        using (var key = RegistryHost.Create(ProbeRelative))
            key!.SetValue("Value", "to-delete");

        RegistryHost.Delete(ProbeRelative);

        using var real = Registry.CurrentUser.OpenSubKey(ProbeSandboxAbsolute);
        Assert.Null(real);
    }

    // ──────────────────────────────────────────────
    // DeleteInterceptionForTesting 注入缝
    // ──────────────────────────────────────────────

    [Fact]
    public void Delete_InterceptionThrows_PropagatesAndSkipsRealDelete()
    {
        using (var key = RegistryHost.Create(ProbeRelative))
            key!.SetValue("Value", "protected");

        RegistryHost.DeleteInterceptionForTesting =
            _ => throw new InvalidOperationException("simulated UCPD denial");

        var ex = Assert.Throws<InvalidOperationException>(() => RegistryHost.Delete(ProbeRelative));
        Assert.Contains("simulated", ex.Message, StringComparison.Ordinal);

        // 注入缝抛出后真实删除未执行，键仍在
        using var real = Registry.CurrentUser.OpenSubKey(ProbeSandboxAbsolute);
        Assert.NotNull(real);
    }

    [Fact]
    public void Delete_WithNullInterception_RealDeleteProceeds()
    {
        using (var key = RegistryHost.Create(ProbeRelative))
            key!.SetValue("Value", "x");

        RegistryHost.DeleteInterceptionForTesting = null;   // 与生产一致
        RegistryHost.Delete(ProbeRelative);

        using var real = Registry.CurrentUser.OpenSubKey(ProbeSandboxAbsolute);
        Assert.Null(real);
    }

    // ──────────────────────────────────────────────
    // 沙箱态 fail-closed：非 "Software\" 前缀路径一律抛异常
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(@"SoftwareX\Foo")]        // 以 Software 开头但非 Software\ 前缀 —— 关键逃逸面
    [InlineData(@"SoftwareX")]
    [InlineData(@"Classes\.zip\OpenWithProgids")]
    [InlineData(@"MantisZip\ContextMenu")]
    [InlineData("")]
    public void Sandbox_NonSoftwarePrefix_Throws(string path)
    {
        Assert.Throws<InvalidOperationException>(() => RegistryHost.Open(path));
        Assert.Throws<InvalidOperationException>(() => RegistryHost.Create(path));
        Assert.Throws<InvalidOperationException>(() => RegistryHost.Delete(path));
    }

    [Fact]
    public void Sandbox_SoftwareExact_MapsToSandboxRoot()
    {
        using (var key = RegistryHost.Create("Software"))
        {
            Assert.NotNull(key);
            key!.SetValue("Probe", "root");
        }

        using var sandboxRoot = Registry.CurrentUser.OpenSubKey(SandboxRootAbsolute);
        Assert.NotNull(sandboxRoot);
        Assert.Equal("root", sandboxRoot!.GetValue("Probe"));
    }

    [Fact]
    public void Sandbox_SoftwarePrefix_CaseInsensitive_MapsIntoSandbox()
    {
        using (var key = RegistryHost.Create(@"software\MantisZip\CaseProbe"))
        {
            Assert.NotNull(key);
            key!.SetValue("Probe", "ci");
        }

        // 相对段保留原始大小写：software\... → Software\MantisZip\AssocTestSandbox\MantisZip\CaseProbe
        using var sandboxKey = Registry.CurrentUser.OpenSubKey(SandboxRootAbsolute + @"\MantisZip\CaseProbe");
        Assert.NotNull(sandboxKey);
        Assert.Equal("ci", sandboxKey!.GetValue("Probe"));
    }

    // ──────────────────────────────────────────────
    // Reset 恢复生产行为（仅只读探测，不写真实注册表）
    // ──────────────────────────────────────────────

    [Fact]
    public void Reset_RestoresProductionPassThrough_ReadOnlyProbe()
    {
        // 沙箱态：非 Software\ 路径 fail-closed 抛异常
        Assert.Throws<InvalidOperationException>(() => RegistryHost.Open(@"Classes\.mzprobe_nonexistent"));

        try
        {
            RegistryHost.Reset();   // 恢复生产行为

            // 生产态：路径原样透传，不再抛异常；只读 Open 探测均返回 null（不创建、不写入）
            using var softwareProbe = RegistryHost.Open(@"Software\Classes\.mzprobe_nonexistent_ext");
            Assert.Null(softwareProbe);

            using var nonSoftwareProbe = RegistryHost.Open(@"Classes\.mzprobe_nonexistent_ext");
            Assert.Null(nonSoftwareProbe);
        }
        finally
        {
            // 恢复本用例的沙箱态（Dispose 仍会 Reset 并清理，双保险）
            RegistryHost.UseSandbox(SandboxRelativeToSoftware);
        }
    }
}
