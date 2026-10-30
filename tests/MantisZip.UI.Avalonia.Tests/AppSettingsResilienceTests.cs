using System.Text.Json;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 设置文件的损坏兜底与落盘加固（issue #64 复盘）：
/// 读不出来时必须留证 + 尝试 settings.json.bak + 回落默认值，不允许再静默吞成默认值；
/// 保存时必须「先写临时文件再替换」并保留上一版 <c>.bak</c>。
/// 用 <see cref="AppSettings.TryReadSettings"/> / <see cref="AppSettings.LoadCore"/> /
/// <see cref="AppSettings.SaveTo"/> 这些内层入口，避免动到真实用户设置目录。
/// </summary>
public sealed class AppSettingsResilienceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "MantisZipSettingsTests", Guid.NewGuid().ToString("N"));

    public AppSettingsResilienceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录删不掉不影响断言结果
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private string BackupPath => SettingsPath + ".bak";

    [Fact]
    public void TryReadSettings_ValidJson_AppliesLegacyModeMigration()
    {
        // 3 = AdaptiveCompressionMode.MultiThreaded（旧版枚举写法）
        File.WriteAllText(SettingsPath, """{"AdaptiveCompressionMode":3,"DefaultLevel":7}""");

        Assert.True(AppSettings.TryReadSettings(SettingsPath, out var settings, out var error), error);
        Assert.Null(error);
        Assert.Equal(7, settings.DefaultLevel);
        Assert.True(settings.MultiThreadedCompression);
        // 迁移完成后旧枚举必须归零，否则下次保存又会被判成「待迁移」
#pragma warning disable CS0618 // 旧字段仍需被读/断言，故此处显式使用废弃枚举
        Assert.Equal(AdaptiveCompressionMode.Disabled, settings.AdaptiveCompressionMode);
#pragma warning restore CS0618
        Assert.NotEmpty(settings.AdaptiveOverrides);
    }

    [Fact]
    public void TryReadSettings_BrokenJson_ReturnsFalseWithError()
    {
        File.WriteAllText(SettingsPath, "{ \"DefaultLevel\": 7,  << 半截文件");

        Assert.False(AppSettings.TryReadSettings(SettingsPath, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void QuarantineBrokenFile_RenamesWithBadSuffixAndKeepsContent()
    {
        const string broken = "{ broken";
        File.WriteAllText(SettingsPath, broken);

        var quarantined = AppSettings.QuarantineBrokenFile(SettingsPath);

        Assert.NotNull(quarantined);
        Assert.Contains(".bad-", quarantined);
        Assert.False(File.Exists(SettingsPath));
        Assert.Equal(broken, File.ReadAllText(Path.Combine(_dir, quarantined!)));
    }

    [Fact]
    public void LoadCore_BrokenMainWithGoodBackup_RecoversFromBackup()
    {
        File.WriteAllText(SettingsPath, "{ broken");
        File.WriteAllText(BackupPath, """{"DefaultLevel":3}""");

        var (settings, cacheable) = AppSettings.LoadCore(SettingsPath);

        Assert.Equal(3, settings.DefaultLevel);
        // 从备份恢复属降级路径：不缓存，下次 Load 重新读盘
        Assert.False(cacheable);
        // 坏文件被改名留证，备份文件保持原样可继续用
        Assert.True(File.Exists(BackupPath));
        Assert.NotEmpty(Directory.GetFiles(_dir, "settings.json.bad-*"));
    }

    [Fact]
    public void LoadCore_BrokenMainWithoutBackup_FallsBackToDefaults()
    {
        File.WriteAllText(SettingsPath, "{ broken");

        var (settings, cacheable) = AppSettings.LoadCore(SettingsPath);

        Assert.False(cacheable);
        Assert.Equal(new AppSettings().DefaultLevel, settings.DefaultLevel);
        Assert.NotEmpty(Directory.GetFiles(_dir, "settings.json.bad-*"));
    }

    [Fact]
    public void LoadCore_MissingFile_ReturnsCacheableDefaults()
    {
        var (settings, cacheable) = AppSettings.LoadCore(SettingsPath);

        Assert.True(cacheable);
        Assert.Equal(new AppSettings().DefaultLevel, settings.DefaultLevel);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void SaveTo_IsAtomicAndKeepsPreviousVersionAsBackup()
    {
        var first = new AppSettings { DefaultLevel = 4 };
        Assert.True(first.SaveTo(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));

        var second = new AppSettings { DefaultLevel = 9 };
        Assert.True(second.SaveTo(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));

        Assert.Equal(9, ReadDefaultLevel(SettingsPath));
        Assert.Equal(4, ReadDefaultLevel(BackupPath));
    }

    [Fact]
    public void SaveTo_AfterBrokenLoad_DoesNotOverwriteBackupWithBrokenFile()
    {
        // 主文件坏掉 → LoadCore 把它改名留证；此时保存不应把「坏文件」当成上一版备份
        File.WriteAllText(SettingsPath, "{ broken");
        _ = AppSettings.LoadCore(SettingsPath);

        Assert.True(new AppSettings { DefaultLevel = 6 }.SaveTo(SettingsPath));

        Assert.Equal(6, ReadDefaultLevel(SettingsPath));
        Assert.False(File.Exists(BackupPath));
    }

    private static int ReadDefaultLevel(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty(nameof(AppSettings.DefaultLevel)).GetInt32();
    }
}
