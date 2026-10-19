using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// AppSettings 进程级缓存（startup-appsettings-cache 三期）：
/// Load 同实例复用、ResetCacheForTests 复位后重新读盘。
/// Save→缓存同步语义不在此测（Save 写真实用户文件，SettingsFile static readonly 不可注入），
/// 由 Task 3 GUI 验收覆盖。
/// </summary>
public class AppSettingsCacheTests
{
    [Fact]
    public void Load_IsCached_ReturnsSameInstanceAcrossCalls()
    {
        AppSettings.ResetCacheForTests();

        var a = AppSettings.Load();
        var b = AppSettings.Load();

        Assert.Same(a, b);
    }

    [Fact]
    public void ResetCacheForTests_NextLoadIsNewInstance_WithMirrorValues()
    {
        var before = AppSettings.Load();

        AppSettings.ResetCacheForTests();
        var after = AppSettings.Load();

        Assert.NotSame(before, after);
        Assert.Equal(before.DefaultFormat, after.DefaultFormat);
        Assert.Equal(before.Theme, after.Theme);
    }
}
