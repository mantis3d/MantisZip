using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;
using static MantisZip.UI.Avalonia.Services.ShellIntegration;

namespace MantisZip.Tests;

/// <summary>
/// 动态右键菜单状态机测试（issue #80）。
///
/// 背景：旧实现用「Explorer 进程里有没有 comhost.dll」判断 COM 是否可用。该信号是假的 ——
/// Explorer 先 <c>LoadLibrary</c> 再问 <c>DllGetClassObject</c>，激活失败时 DLL 仍留在模块列表，
/// 于是状态被误判成 <c>active</c>、静态级联被删、COM 又画不出菜单，右键菜单彻底消失。
/// 现在唯一判据是 handler 自己上报的心跳（<c>LastRenderUtc</c>），且 <c>active</c> 可回退。
/// </summary>
public class ShellMenuStatusMachineTests
{
    // ──────────────────────────────────────────────
    // DecideComAction —— 纯决策表（不碰注册表）
    // ──────────────────────────────────────────────

    [Theory]
    // pending：心跳新鲜 → 升级为 active（撤级联）
    [InlineData(DynamicMenuStatus_Pending, (int)ComHeartbeatHealth.Fresh, false, (int)ComStatusAction.PromoteToActive)]
    // pending：试用期内还没心跳 → 保持 pending，只补级联（用户始终有菜单）
    [InlineData(DynamicMenuStatus_Pending, (int)ComHeartbeatHealth.Recent, false, (int)ComStatusAction.EnsureCascade)]
    [InlineData(DynamicMenuStatus_Pending, (int)ComHeartbeatHealth.Never, false, (int)ComStatusAction.EnsureCascade)]
    [InlineData(DynamicMenuStatus_Pending, (int)ComHeartbeatHealth.Stale, false, (int)ComStatusAction.EnsureCascade)]
    // pending：试用期结束仍无新鲜心跳 → 永久回退级联
    [InlineData(DynamicMenuStatus_Pending, (int)ComHeartbeatHealth.Recent, true, (int)ComStatusAction.DemoteToFallback)]
    [InlineData(DynamicMenuStatus_Pending, (int)ComHeartbeatHealth.Never, true, (int)ComStatusAction.DemoteToFallback)]
    // 只要心跳新鲜，即使试用期已过也照样升级（COM 确实能画菜单，就不该白回退）
    [InlineData(DynamicMenuStatus_Pending, (int)ComHeartbeatHealth.Fresh, true, (int)ComStatusAction.PromoteToActive)]
    // active：心跳过期 / 从未上报 → 回退级联（issue #80 根因 2：原先 active 后永久早退）
    [InlineData(DynamicMenuStatus_Active, (int)ComHeartbeatHealth.Stale, false, (int)ComStatusAction.DemoteToFallback)]
    [InlineData(DynamicMenuStatus_Active, (int)ComHeartbeatHealth.Never, false, (int)ComStatusAction.DemoteToFallback)]
    // active：心跳仍可信 → 不动（避免每次右键都重写注册表）
    [InlineData(DynamicMenuStatus_Active, (int)ComHeartbeatHealth.Fresh, false, (int)ComStatusAction.None)]
    [InlineData(DynamicMenuStatus_Active, (int)ComHeartbeatHealth.Recent, false, (int)ComStatusAction.None)]
    // fallback：确保级联在位（幂等）
    [InlineData(DynamicMenuStatus_Fallback, (int)ComHeartbeatHealth.Never, false, (int)ComStatusAction.EnsureCascade)]
    [InlineData(DynamicMenuStatus_Fallback, (int)ComHeartbeatHealth.Fresh, false, (int)ComStatusAction.EnsureCascade)]
    // 其余状态（disabled / not_installed / 未知）一律不动
    [InlineData(DynamicMenuStatus_Disabled, (int)ComHeartbeatHealth.Fresh, false, (int)ComStatusAction.None)]
    [InlineData("not_installed", (int)ComHeartbeatHealth.Fresh, false, (int)ComStatusAction.None)]
    [InlineData("unknown", (int)ComHeartbeatHealth.Never, true, (int)ComStatusAction.None)]
    public void DecideComAction_FollowsTransitionTable(
        string status, int health, bool probeExpired, int expected)
    {
        Assert.Equal(expected, (int)DecideComAction(status, (ComHeartbeatHealth)health, probeExpired));
    }

    /// <summary>
    /// 「active 只进不出」是 issue #80 的核心缺陷：老实现在 active 后直接 return，
    /// 一旦 COM 后来失效就永远修不回来。这条用例钉死「active 必须能降级」。
    /// </summary>
    [Fact]
    public void DecideComAction_ActiveIsNotTerminal()
    {
        Assert.Equal(ComStatusAction.DemoteToFallback,
            DecideComAction(DynamicMenuStatus_Active, ComHeartbeatHealth.Stale, false));
        Assert.Equal(ComStatusAction.DemoteToFallback,
            DecideComAction(DynamicMenuStatus_Active, ComHeartbeatHealth.Never, false));
    }

    /// <summary>时间窗口必须保持「新鲜 &lt; 试用期」且「新鲜 &lt; 可信」，否则状态机会自相矛盾。</summary>
    [Fact]
    public void TimeWindows_AreOrdered()
    {
        Assert.True(FreshHeartbeatWindow < RecentHeartbeatWindow);
        Assert.True(FreshHeartbeatWindow < ComProbeWindow);
    }

    // ──────────────────────────────────────────────
    // 出厂默认
    // ──────────────────────────────────────────────

    /// <summary>
    /// 出厂默认必须是静态级联（不依赖 Explorer 加载 .NET COM 组件）。
    /// 改回 true 就等于让所有用户回到 issue #80 的风险面上。
    /// </summary>
    [Fact]
    public void DefaultSettings_DynamicMenuIsOff()
    {
        Assert.False(new AppSettings().EnableDynamicMenu,
            "EnableDynamicMenu 出厂默认必须为 false（静态级联最稳，COM 仅作实验开关）");
    }
}

/// <summary>
/// 心跳读取测试。<see cref="ReadHeartbeatHealth"/> 只读注册表，用沙箱隔离。
/// </summary>
[Collection("RegistryTests")]
public class ShellMenuHeartbeatTests : IDisposable
{
    private const string Sandbox = @"MantisZip\MenuStatusSandbox";
    private const string SandboxFullPath = @"Software\" + Sandbox;

    public ShellMenuHeartbeatTests()
    {
        RegistryHost.Reset();
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(SandboxFullPath, throwOnMissingSubKey: false);
        RegistryHost.UseSandbox(Sandbox);
    }

    public void Dispose()
    {
        RegistryHost.Reset();
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(SandboxFullPath, throwOnMissingSubKey: false);
    }

    /// <summary>写入心跳原始值（ISO8601 UTC 字符串 + Explorer PID）。
    /// ⚠ 路径必须写**生产路径**：RegistryHost 在沙箱态下会自动重定向到本测试的沙箱键。</summary>
    private static void WriteHeartbeat(DateTime renderedUtc, int pid)
    {
        using var key = RegistryHost.Create(@"Software\MantisZip\ContextMenu");
        Assert.NotNull(key);
        key!.SetValue(LastRenderUtcValue, renderedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        key.SetValue(LastRenderPidValue, pid, Microsoft.Win32.RegistryValueKind.DWord);
    }

    [Fact]
    public void ReadHeartbeatHealth_NoKey_IsNever()
    {
        Assert.Equal(ComHeartbeatHealth.Never, ReadHeartbeatHealth());
    }

    [Fact]
    public void ReadHeartbeatHealth_MalformedTimestamp_IsNever()
    {
        using (var key = RegistryHost.Create(@"Software\MantisZip\ContextMenu"))
        {
            Assert.NotNull(key);
            key!.SetValue(LastRenderUtcValue, "not-a-date");
        }
        Assert.Equal(ComHeartbeatHealth.Never, ReadHeartbeatHealth());
    }

    /// <summary>
    /// 刚写过心跳，但 PID 指向的进程已不存在 / 不是 Explorer（例如上一代 Explorer 已重启）→
    /// 只能算 <see cref="ComHeartbeatHealth.Recent"/>，不足以升级成 active。
    /// </summary>
    [Fact]
    public void ReadHeartbeatHealth_FreshButDeadPid_IsRecent()
    {
        WriteHeartbeat(DateTime.UtcNow, pid: 999_999);
        Assert.Equal(ComHeartbeatHealth.Recent, ReadHeartbeatHealth());
    }

    [Fact]
    public void ReadHeartbeatHealth_OldTimestamp_IsStale()
    {
        WriteHeartbeat(DateTime.UtcNow - RecentHeartbeatWindow - TimeSpan.FromDays(1), pid: 999_999);
        Assert.Equal(ComHeartbeatHealth.Stale, ReadHeartbeatHealth());
    }

    /// <summary>
    /// 心跳新鲜且 PID 指向**当前存活的 Explorer** → Fresh（升级 active 的唯一充分条件）。
    /// CI/无桌面环境下可能没有 Explorer 进程，此时跳过断言（不算失败）。
    /// </summary>
    [Fact]
    public void ReadHeartbeatHealth_FreshWithLiveExplorerPid_IsFresh()
    {
        var explorer = System.Diagnostics.Process.GetProcessesByName("explorer").FirstOrDefault();
        if (explorer is null) return; // 无 Explorer 会话（服务账户 / 无头环境）

        WriteHeartbeat(DateTime.UtcNow, explorer.Id);
        Assert.Equal(ComHeartbeatHealth.Fresh, ReadHeartbeatHealth());
    }
}
