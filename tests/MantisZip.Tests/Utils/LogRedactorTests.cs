using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// LogRedactor —— 日志路径脱敏的单元测试。
/// 覆盖 Off/FilenameOnly/ExtensionOnly/Full 四种模式及 ParseMode 解析。
/// 每个测试前调用 Reset() 避免状态泄漏。
/// </summary>
public class LogRedactorTests : IDisposable
{
    public LogRedactorTests() => LogRedactor.Reset();
    public void Dispose() => LogRedactor.Reset();

    // ════════════════════════════════════════════════════════════════
    //  ParseMode — 模式字符串解析
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("filename", LogPrivacyMode.FilenameOnly)]
    [InlineData("extension", LogPrivacyMode.ExtensionOnly)]
    [InlineData("full", LogPrivacyMode.Full)]
    [InlineData("off", LogPrivacyMode.Off)]
    [InlineData("", LogPrivacyMode.Off)]
    [InlineData("unknown", LogPrivacyMode.Off)]
    [InlineData(null, LogPrivacyMode.Off)]
    public void ParseMode_ReturnsExpected(string? input, LogPrivacyMode expected)
    {
        Assert.Equal(expected, LogRedactor.ParseMode(input!));
    }

    // ════════════════════════════════════════════════════════════════
    //  RedactPaths — Off 模式
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RedactPaths_OffMode_Passthrough()
    {
        var msg = @"D:\Photos\private\wedding.jpg";
        Assert.Equal(msg, LogRedactor.RedactPaths(msg, LogPrivacyMode.Off));
    }

    [Fact]
    public void RedactPaths_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", LogRedactor.RedactPaths("", LogPrivacyMode.Full));
    }

    [Fact]
    public void RedactPaths_NullString_ReturnsNull()
    {
        Assert.Null(LogRedactor.RedactPaths(null!, LogPrivacyMode.Full));
    }

    // ════════════════════════════════════════════════════════════════
    //  RedactPaths — FilenameOnly 模式
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RedactPaths_FilenameOnly_DrivePath()
    {
        var msg = @"Opening D:\Photos\private\wedding.jpg";
        var result = LogRedactor.RedactPaths(msg, LogPrivacyMode.FilenameOnly);
        Assert.Contains("wedding.jpg", result);
        Assert.DoesNotContain("Photos", result);
        Assert.Contains("[PATH_", result);
    }

    [Fact]
    public void RedactPaths_FilenameOnly_UNCPath()
    {
        var msg = @"Opening \\server\share\file.txt";
        var result = LogRedactor.RedactPaths(msg, LogPrivacyMode.FilenameOnly);
        Assert.Contains("file.txt", result);
        Assert.DoesNotContain("server", result);
    }

    [Fact]
    public void RedactPaths_FilenameOnly_RelativePath()
    {
        var msg = @"Opening docs/sub/file.txt";
        var result = LogRedactor.RedactPaths(msg, LogPrivacyMode.FilenameOnly);
        Assert.Contains("file.txt", result);
    }

    // ════════════════════════════════════════════════════════════════
    //  RedactPaths — ExtensionOnly 模式
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RedactPaths_ExtensionOnly_PreservesExtension()
    {
        var msg = @"D:\Photos\private\wedding.jpg";
        var result = LogRedactor.RedactPaths(msg, LogPrivacyMode.ExtensionOnly);
        Assert.EndsWith(".jpg", result);
        Assert.DoesNotContain("wedding", result);
        Assert.DoesNotContain("Photos", result);
        Assert.Contains("[PATH_", result);
        Assert.Contains("[FILE_", result);
    }

    [Fact]
    public void RedactPaths_ExtensionOnly_NoExtension()
    {
        var msg = @"D:\Folder\Makefile";
        var result = LogRedactor.RedactPaths(msg, LogPrivacyMode.ExtensionOnly);
        Assert.Contains("[FILE_", result);
        // 无扩展名时只有 [FILE_N]，没有后缀
        Assert.DoesNotContain(".jpg", result);
    }

    // ════════════════════════════════════════════════════════════════
    //  RedactPaths — Full 模式
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RedactPaths_Full_ReplacesEntirePath()
    {
        var msg = @"Opening D:\Photos\private\wedding.jpg";
        var result = LogRedactor.RedactPaths(msg, LogPrivacyMode.Full);
        Assert.DoesNotContain("D:", result);
        Assert.DoesNotContain("Photos", result);
        Assert.DoesNotContain("wedding", result);
        Assert.Contains("[PATH_", result);
    }

    [Fact]
    public void RedactPaths_Full_SamePath_SameId()
    {
        var msg1 = LogRedactor.RedactPaths(@"D:\file.txt", LogPrivacyMode.Full);
        var msg2 = LogRedactor.RedactPaths(@"D:\file.txt", LogPrivacyMode.Full);
        // 同一路径 → 同一 ID
        Assert.Equal(msg1, msg2);
    }

    [Fact]
    public void RedactPaths_Full_DifferentPaths_DifferentIds()
    {
        var msg1 = LogRedactor.RedactPaths(@"D:\a.txt", LogPrivacyMode.Full);
        var msg2 = LogRedactor.RedactPaths(@"D:\b.txt", LogPrivacyMode.Full);
        Assert.NotEqual(msg1, msg2);
    }

    // ════════════════════════════════════════════════════════════════
    //  RedactPaths — 混合内容
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RedactPaths_MultiplePaths_AllRedacted()
    {
        var msg = @"Extracting from D:\a.zip to C:\dest and \\server\share";
        var result = LogRedactor.RedactPaths(msg, LogPrivacyMode.Full);
        Assert.DoesNotContain("D:", result);
        Assert.DoesNotContain("C:", result);
        Assert.DoesNotContain("server", result);
    }

    [Fact]
    public void RedactPaths_NoPaths_Unchanged()
    {
        var msg = "Simple log message without paths";
        Assert.Equal(msg, LogRedactor.RedactPaths(msg, LogPrivacyMode.Full));
    }

    // ════════════════════════════════════════════════════════════════
    //  Reset — 状态清理
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Reset_ClearsIdCounters()
    {
        LogRedactor.RedactPaths(@"D:\file.txt", LogPrivacyMode.Full);
        LogRedactor.Reset();
        // Reset 后 ID 从 1 重新开始
        var result = LogRedactor.RedactPaths(@"D:\file.txt", LogPrivacyMode.Full);
        Assert.Contains("[PATH_1]", result);
    }
}
