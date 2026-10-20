using MantisZip.UI.Avalonia;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// Program.SplashTextSpec 分发逻辑测试（startup-native-splash 计划「splash 文案选择」）：
/// 纯函数返回 (key, 插值参数)，不依赖 LocalizedStrings 状态，语言无关。
/// </summary>
public class SplashTextSpecTests : IDisposable
{
    private readonly string _dir;

    public SplashTextSpecTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "splashtextspec-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录尽力删除 */ }
    }

    [Fact]
    public void NoArgs_ReturnsStarting()
    {
        var (key, args) = Program.SplashTextSpec([]);
        Assert.Equal("Status_Starting", key);
        Assert.Empty(args);
    }

    [Theory]
    [InlineData("--test")]
    [InlineData("--install-shell")]
    [InlineData("--uninstall-shell")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("--install-assoc")]
    [InlineData("--uninstall-assoc")]
    public void NonUiCommands_NeverReachSpec_ButSpecFallsBackToStarting(string cmd)
    {
        // 防御性：即使误调 SplashTextSpec（正常路径被 ShouldShowSplash 拦下），也退回静态文案
        var (key, args) = Program.SplashTextSpec([cmd, "nonexistent.zip"]);
        Assert.Equal("Status_Starting", key);
        Assert.Empty(args);
    }

    [Fact]
    public void OpenSingleExistingFile_UsesOpeningFmtWithFileName()
    {
        var zip = Path.Combine(_dir, "demo-open.zip");
        File.WriteAllText(zip, "stub");

        var (key, args) = Program.SplashTextSpec(["--open", zip]);

        Assert.Equal("Status_OpeningArchiveFmt", key);
        Assert.Single(args);
        Assert.Equal(Path.GetFileName(zip), args[0]);
    }

    [Fact]
    public void OpenDispatchSingleExistingFile_UsesOpeningFmt()
    {
        var zip = Path.Combine(_dir, "demo-dispatch.zip");
        File.WriteAllText(zip, "stub");

        var (key, args) = Program.SplashTextSpec(["--open-dispatch", zip]);

        Assert.Equal("Status_OpeningArchiveFmt", key);
        Assert.Equal(Path.GetFileName(zip), args[0]);
    }

    [Fact]
    public void OpenWithNonExistentPath_FallsBackToStatic()
    {
        var (key, args) = Program.SplashTextSpec(["--open", Path.Combine(_dir, "ghost.zip")]);

        Assert.Equal("Status_OpeningArchive", key);
        Assert.Empty(args);
    }

    [Fact]
    public void ExtractSingleExistingFile_UsesExtractingFmtWithFileName()
    {
        var zip = Path.Combine(_dir, "demo-single.zip");
        File.WriteAllText(zip, "stub");

        foreach (var cmd in new[] { "--extract", "--extract-here", "--extract-to-name", "--extract-smart" })
        {
            var (key, args) = Program.SplashTextSpec([cmd, zip]);
            Assert.Equal("Status_ExtractingFmt", key);
            Assert.Single(args);
            Assert.Equal(Path.GetFileName(zip), args[0]);
        }
    }

    [Fact]
    public void ExtractMultipleExistingFiles_UsesBatchFmtWithCount()
    {
        var a = Path.Combine(_dir, "a.zip");
        var b = Path.Combine(_dir, "b.zip");
        var c = Path.Combine(_dir, "c.txt"); // 非压缩包扩展名不影响 spec：以 File.Exists 为准
        File.WriteAllText(a, "stub");
        File.WriteAllText(b, "stub");
        File.WriteAllText(c, "stub");

        var (key, args) = Program.SplashTextSpec(["--extract-here", a, b, c, "ghost.zip"]);

        Assert.Equal("Status_ExtractingBatchFmt", key);
        Assert.Single(args);
        Assert.Equal(3, args[0]);
    }

    [Fact]
    public void ExtractWithNoValidPaths_UsesBatchFmtWithZero()
    {
        var (key, args) = Program.SplashTextSpec(["--extract", "ghost.zip"]);

        Assert.Equal("Status_ExtractingBatchFmt", key);
        Assert.Equal(0, args[0]);
    }

    [Fact]
    public void CompressQuickMultiple_UsesBatchFmtWithCount()
    {
        var files = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var f = Path.Combine(_dir, $"f{i}.dat");
            File.WriteAllText(f, "x");
            files.Add(f);
        }

        var (key, args) = Program.SplashTextSpec(["--compress-quick", .. files]);

        Assert.Equal("Status_CompressingBatchFmt", key);
        Assert.Equal(3, args[0]);
    }

    [Fact]
    public void CompressQuickSingle_DoesNotCarryFileName()
    {
        var f = Path.Combine(_dir, "only.dat");
        File.WriteAllText(f, "x");

        var (key, args) = Program.SplashTextSpec(["--compress-quick", f]);

        // 业务拍板：单文件压缩不带文件名（避免未压缩即暴露目标名），复用已有静态 key
        Assert.Equal("Progress_Title_Compress", key);
        Assert.Empty(args);
    }

    [Theory]
    [InlineData("--compress")]
    [InlineData("--compress-separate")]
    [InlineData("--compress-combined")]
    public void CompressIpcCommands_StaysStaticCollecting(string cmd)
    {
        var f = Path.Combine(_dir, "ip.dat");
        File.WriteAllText(f, "x");

        var (key, args) = Program.SplashTextSpec([cmd, f]);

        // IPC 收集期总数未知，保持静态——总数未知时数字反而误导
        Assert.Equal("App_CompressCollecting", key);
        Assert.Empty(args);
    }
}
