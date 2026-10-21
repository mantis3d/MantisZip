using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 自适应压缩判定单元测试 — 覆盖内置已压缩扩展名分类（SSOT）与 ResolveLevel 决策入口。
/// MultiThreadedStoreFormatIds 用户自定义仅存储格式覆盖逻辑也在此验证。
/// </summary>
public class ZipEntryClassifierTests
{
    #region IsCompressed — 内置扩展名分类

    [Theory]
    [InlineData("jpg")]
    [InlineData("JPG")]
    [InlineData(".Jpeg")]
    [InlineData("png")]
    [InlineData("gif")]
    [InlineData("webp")]
    [InlineData("mp3")]
    [InlineData("flac")]
    [InlineData("mp4")]
    [InlineData("mkv")]
    [InlineData("zip")]
    [InlineData("7z")]
    [InlineData("rar")]
    [InlineData("gz")]
    [InlineData("woff2")]
    [InlineData("ttf")]
    [InlineData("pdf")]
    [InlineData("docx")]
    [InlineData("xlsx")]
    [InlineData("jar")]
    [InlineData("apk")]
    public void IsCompressed_BuiltInFormats_ReturnsTrue(string extension)
    {
        Assert.True(ZipEntryClassifier.IsCompressed(extension),
            $"IsCompressed(\"{extension}\") should be true");
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("cs")]
    [InlineData("md")]
    [InlineData("json")]
    [InlineData("exe")]
    [InlineData("dll")]
    [InlineData("dat")]
    [InlineData("wav")]
    [InlineData("sqlite")]
    [InlineData("unknownxyz")]
    public void IsCompressed_TextOrMiscFormats_ReturnsFalse(string extension)
    {
        Assert.False(ZipEntryClassifier.IsCompressed(extension),
            $"IsCompressed(\"{extension}\") should be false");
    }

    [Fact]
    public void IsCompressed_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(ZipEntryClassifier.IsCompressed(null));
        Assert.False(ZipEntryClassifier.IsCompressed(""));
        Assert.False(ZipEntryClassifier.IsCompressed("."));
    }

    [Fact]
    public void IsCompressed_CaseInsensitive_WithOrWithoutDot()
    {
        // 全部等价：jpg / .jpg / JPG / .JPG
        Assert.True(ZipEntryClassifier.IsCompressed("jpg"));
        Assert.True(ZipEntryClassifier.IsCompressed("JPG"));
        Assert.True(ZipEntryClassifier.IsCompressed(".jpg"));
        Assert.True(ZipEntryClassifier.IsCompressed(".JPG"));
    }

    #endregion

    #region ResolveLevel(3 参) — 自适应开关分流

    [Fact]
    public void ResolveLevel_AdaptiveOff_ReturnsUserLevel()
    {
        // 未启用自适应 → 任何文件都用用户级别
        Assert.Equal(5, AdaptiveRuleMatcher.ResolveLevel("photo.jpg", 5, adaptiveCompression: false));
        Assert.Equal(9, AdaptiveRuleMatcher.ResolveLevel("readme.txt", 9, adaptiveCompression: false));
    }

    [Fact]
    public void ResolveLevel_AdaptiveOn_CompressedFile_ReturnsStore()
    {
        Assert.Equal(0, AdaptiveRuleMatcher.ResolveLevel("photo.jpg", 5, adaptiveCompression: true));
        Assert.Equal(0, AdaptiveRuleMatcher.ResolveLevel("video.mp4", 5, adaptiveCompression: true));
    }

    [Fact]
    public void ResolveLevel_AdaptiveOn_TextFile_ReturnsUserLevel()
    {
        Assert.Equal(5, AdaptiveRuleMatcher.ResolveLevel("readme.txt", 5, adaptiveCompression: true));
        Assert.Equal(7, AdaptiveRuleMatcher.ResolveLevel("Program.cs", 7, adaptiveCompression: true));
    }

    #endregion

    #region ResolveLevel — 用户自定义规则（Phase 2 合并：原「仅存储格式」列表已并入规则）

    [Fact]
    public void ResolveLevel_AdaptiveOff_IgnoresRules()
    {
        // 未启用自适应时，即使命中规则也忽略
        var rules = new List<AdaptiveOverrideRule>
        {
            new() { Name = "图片", FormatIds = new() { "Jpeg" }, Level = AdaptiveLevel.Store, Enabled = true },
        };
        Assert.Equal(5, AdaptiveRuleMatcher.ResolveLevel("photo.jpg", 5, adaptiveCompression: false, rules: rules));
    }

    [Fact]
    public void ResolveLevel_StoreRule_ReturnsStore()
    {
        // .wav 不在 SSOT 已压缩集合内，但规则指定「仅存储」→ Store
        var rules = new List<AdaptiveOverrideRule>
        {
            new() { Name = "音频", FormatIds = new() { "Wav" }, Level = AdaptiveLevel.Store, Enabled = true },
        };
        Assert.Equal(0, AdaptiveRuleMatcher.ResolveLevel("sound.wav", 5, adaptiveCompression: true, rules: rules));
    }

    [Fact]
    public void ResolveLevel_NonMatchingRule_ReturnsUserLevel()
    {
        // 规则命中的格式与文件不符 → 不 Store
        var rules = new List<AdaptiveOverrideRule>
        {
            new() { Name = "音频", FormatIds = new() { "Wav" }, Level = AdaptiveLevel.Store, Enabled = true },
        };
        Assert.Equal(5, AdaptiveRuleMatcher.ResolveLevel("readme.txt", 5, adaptiveCompression: true, rules: rules));
    }

    [Fact]
    public void ResolveLevel_UnknownFormatIdRule_ReturnsUserLevel()
    {
        // 规则含不存在的格式 ID → 忽略，不 Store
        var rules = new List<AdaptiveOverrideRule>
        {
            new() { Name = "未知", FormatIds = new() { "NonExistentFormat" }, Level = AdaptiveLevel.Store, Enabled = true },
        };
        Assert.Equal(5, AdaptiveRuleMatcher.ResolveLevel("readme.txt", 5, adaptiveCompression: true, rules: rules));
    }

    [Fact]
    public void ResolveLevel_NullRules_FallsBackToBuiltin()
    {
        // rules 为 null → 仅 SSOT 已压缩判定生效
        Assert.Equal(0, AdaptiveRuleMatcher.ResolveLevel("photo.jpg", 5, adaptiveCompression: true, rules: null));
        Assert.Equal(5, AdaptiveRuleMatcher.ResolveLevel("readme.txt", 5, adaptiveCompression: true, rules: null));
    }

    [Fact]
    public void ResolveLevel_BuiltInTakesEffectWithoutRules()
    {
        // .jpg 属 SSOT 已压缩 → Store（无规则时）
        Assert.Equal(0, AdaptiveRuleMatcher.ResolveLevel("photo.jpg", 5, adaptiveCompression: true));
    }

    #endregion
}
