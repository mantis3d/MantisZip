using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// AppSettings 进程级缓存（startup-appsettings-cache 三期）：
/// Load 同实例复用、ResetCacheForTests 复位后重新读盘。
/// Save→缓存同步语义不在此测（2026-11-19 起可用 <see cref="AppSettingsTestSandbox"/> 把它重定向到
/// 临时文件来测，但本类只钉缓存语义，落盘链路由 <see cref="AppSettingsTestSandboxTests"/> 覆盖）。
/// </summary>
public class AppSettingsCacheTests
{
    [Fact]
    public void Load_IsCached_ReturnsSameInstanceAcrossCalls()
    {
        using var sandbox = new AppSettingsTestSandbox();
        AppSettings.ResetCacheForTests();

        var a = AppSettings.Load();
        var b = AppSettings.Load();

        Assert.Same(a, b);
    }

    [Fact]
    public void ResetCacheForTests_NextLoadIsNewInstance_WithMirrorValues()
    {
        using var sandbox = new AppSettingsTestSandbox();
        AppSettings.ResetCacheForTests();

        var before = AppSettings.Load();

        AppSettings.ResetCacheForTests();
        var after = AppSettings.Load();

        Assert.NotSame(before, after);
        Assert.Equal(before.DefaultFormat, after.DefaultFormat);
        Assert.Equal(before.Theme, after.Theme);
    }
}
