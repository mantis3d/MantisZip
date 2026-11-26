using System.IO;
using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// <see cref="AppSettingsTestSandbox"/> / <c>AppSettings.UseIsolatedSettingsFileForTests</c> 的自测：
/// 沙箱内的读写必须完全落在临时目录，退出后真实设置文件一个字节都不该被动过。
///
/// 这两条是「测试不碰用户设置」这条不变量的回归网 —— 少了它们，沙箱本身坏掉（比如忘了清缓存）
/// 时所有依赖它的用例都会静默回到「写真实文件」的老路上，而用例本身照样绿。
/// </summary>
public class AppSettingsTestSandboxTests
{
    [Fact]
    public void Sandbox_RedirectsLoadAndSave_IntoTempDirectory()
    {
        using var sandbox = new AppSettingsTestSandbox();

        Assert.False(File.Exists(sandbox.SettingsFile), "沙箱初始不应有设置文件");

        var settings = AppSettings.Load();
        settings.FileNameTemplateDialog = "{name}_{seq}";
        Assert.True(settings.Save());

        Assert.True(File.Exists(sandbox.SettingsFile), "Save 必须落到沙箱文件");

        // 清缓存后重新读盘，值来自沙箱文件
        AppSettings.ResetCacheForTests();
        Assert.Equal("{name}_{seq}", AppSettings.Load().FileNameTemplateDialog);
    }

    [Fact]
    public void Sandbox_RedirectsDataDir_SoWindowAndRecentStateAlsoLandInTemp()
    {
        using var sandbox = new AppSettingsTestSandbox();

        // recent.json / window.json / layout.json / metadata-panel.json / lifecycle.log
        // 全都从 AppSettings.DataDir 拼路径。只重定向 settings.json 的话，
        // 跑一次测试照样会把这些文件写成测试数据（2026-11-23 实测三份文件 mtime 被刷成同一秒）。
        Assert.Equal(sandbox.Root, AppSettings.DataDir);

        var stateFile = Path.Combine(AppSettings.DataDir, "window.json");
        File.WriteAllText(stateFile, "{}");
        Assert.StartsWith(sandbox.Root, stateFile, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sandbox_Dispose_RestoresRealSettingsFile_AndDiskIsUntouched()
    {
        var realSettingsFile = Path.Combine(AppSettings.DataDir, "settings.json");
        var stampBefore = File.Exists(realSettingsFile)
            ? File.GetLastWriteTimeUtc(realSettingsFile)
            : (System.DateTime?)null;

        string sandboxFile;
        string sandboxRoot;
        using (var sandbox = new AppSettingsTestSandbox())
        {
            sandboxFile = sandbox.SettingsFile;
            sandboxRoot = sandbox.Root;
            AppSettings.Load().FileNameTemplateDialog = "{name}_sandbox";
            AppSettings.Load().Save();
            Assert.NotEqual(realSettingsFile, sandboxFile);
        }

        // 沙箱目录连同文件一起清掉
        Assert.False(File.Exists(sandboxFile), "沙箱文件应随 Dispose 删除");
        Assert.False(Directory.Exists(sandboxRoot), "沙箱目录应随 Dispose 删除");

        // 数据目录恢复成沙箱外的落点（本程序集的安全网目录，而不是刚删掉的沙箱）
        Assert.NotEqual(sandboxRoot, AppSettings.DataDir);
        Assert.Equal(Path.GetDirectoryName(realSettingsFile), AppSettings.DataDir);

        // 沙箱外的那份设置：存在性不变、修改时间不变（沙箱期间一次都没被写）
        var stampAfter = File.Exists(realSettingsFile)
            ? File.GetLastWriteTimeUtc(realSettingsFile)
            : (System.DateTime?)null;
        Assert.Equal(stampBefore, stampAfter);
    }
}
