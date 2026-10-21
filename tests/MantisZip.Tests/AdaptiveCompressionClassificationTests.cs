using System.Text;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// 自适应压缩 SSOT 判定测试（adaptive-compress-ui-convergence Phase 2 / Task 9-10）。
///
/// 覆盖：
/// ① 扩展名 SSOT 边界（svg/tar/wav 不算已压缩；jpg/mp4/zip 算）；
/// ② 魔数检测（真 JPEG 魔数 + 改名 .dat → Store；纯文本 → 不 Store；小于阈值不读头；读不到不抛异常）；
/// ③ 规则优先级（用户规则 > SSOT 扩展名 > 魔数）、禁用规则被忽略、自定义仅存储格式。
/// </summary>
public class AdaptiveCompressionClassificationTests : IDisposable
{
    private readonly string _dir;

    public AdaptiveCompressionClassificationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mz-adaptive-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录尽力删除 */ }
    }

    // ── ① 扩展名 SSOT ──

    [Theory]
    [InlineData("a.jpg", true)]
    [InlineData("a.png", true)]
    [InlineData("a.mp4", true)]
    [InlineData("a.zip", true)]
    [InlineData("a.ttf", true)]
    [InlineData("a.svg", false)]   // SVG 是 XML 文本，可压
    [InlineData("a.tar", false)]   // tar 是未压缩归档，可压
    [InlineData("a.wav", false)]   // PCM 未压缩，可压
    [InlineData("a.txt", false)]
    [InlineData("noext", false)]
    public void ExtensionClassification_FollowsSsot(string fileName, bool expectCompressed)
    {
        Assert.Equal(expectCompressed, ZipEntryClassifier.IsCompressed(Path.GetExtension(fileName)));
    }

    [Fact]
    public void IsCompressed_AcceptsExtensionWithOrWithoutDot()
    {
        Assert.True(ZipEntryClassifier.IsCompressed(".jpg"));
        Assert.True(ZipEntryClassifier.IsCompressed("JPG"));
        Assert.False(ZipEntryClassifier.IsCompressed(".svg"));
    }

    // ── ② 魔数检测 ──

    [Fact]
    public void MagicDetection_JpegContentWithFakeExtension_IsCompressed()
    {
        var path = Path.Combine(_dir, "photo.dat");
        var jpeg = new byte[100 * 1024];
        jpeg[0] = 0xFF; jpeg[1] = 0xD8; jpeg[2] = 0xFF; jpeg[3] = 0xE0;
        File.WriteAllBytes(path, jpeg);

        Assert.True(ZipEntryClassifier.IsCompressedByMagic(path));
    }

    [Fact]
    public void MagicDetection_PlainTextContent_IsNotCompressed()
    {
        var path = Path.Combine(_dir, "notes.dat");
        var text = Encoding.UTF8.GetBytes(string.Join("\n", Enumerable.Repeat("hello world", 20000)));
        File.WriteAllBytes(path, text);

        Assert.False(ZipEntryClassifier.IsCompressedByMagic(path));
    }

    [Fact]
    public void MagicDetection_SmallFile_IsSkippedByThreshold()
    {
        var path = Path.Combine(_dir, "small.dat");
        var jpeg = new byte[1024];
        jpeg[0] = 0xFF; jpeg[1] = 0xD8; jpeg[2] = 0xFF; jpeg[3] = 0xE0;
        File.WriteAllBytes(path, jpeg);

        Assert.True(jpeg.Length < ZipEntryClassifier.MagicDetectMinBytes);
        Assert.False(ZipEntryClassifier.IsCompressedByMagic(path));
    }

    [Fact]
    public void MagicDetection_MissingFile_ReturnsFalseWithoutThrowing()
    {
        Assert.False(ZipEntryClassifier.IsCompressedByMagic(Path.Combine(_dir, "ghost.dat")));
    }

    // ── ③ 决策入口（AdaptiveRuleMatcher.ResolveLevel）──

    [Fact]
    public void ResolveLevel_AdaptiveOff_ReturnsGlobalLevel()
    {
        var level = AdaptiveRuleMatcher.ResolveLevel("a.jpg", 6, adaptiveCompression: false);
        Assert.Equal(6, level);
    }

    [Fact]
    public void ResolveLevel_CompressedExtension_ReturnsStore()
    {
        Assert.Equal(0, AdaptiveRuleMatcher.ResolveLevel("a.jpg", 6, adaptiveCompression: true));
    }

    [Fact]
    public void ResolveLevel_PlainFile_ReturnsGlobalLevel()
    {
        Assert.Equal(6, AdaptiveRuleMatcher.ResolveLevel("a.txt", 6, adaptiveCompression: true));
    }

    [Fact]
    public void ResolveLevel_UserRule_TakesPriorityOverExtensionSsot()
    {
        // 规则：.jpg 强制按 9 级压缩（SSOT 会判 Store）→ 规则优先
        var rules = new List<AdaptiveOverrideRule>
        {
            new() { Name = "图片重压", FormatIds = new() { "Jpeg" }, Level = AdaptiveLevel.Max, Enabled = true },
        };

        var level = AdaptiveRuleMatcher.ResolveLevel("a.jpg", 6, adaptiveCompression: true, rules: rules);

        Assert.Equal(9, level);
    }

    [Fact]
    public void ResolveLevel_DisabledRule_IsIgnored()
    {
        var rules = new List<AdaptiveOverrideRule>
        {
            new() { Name = "文本", FormatIds = new() { "Txt" }, Level = AdaptiveLevel.Store, Enabled = false },
        };

        var level = AdaptiveRuleMatcher.ResolveLevel("a.txt", 6, adaptiveCompression: true, rules: rules);

        Assert.Equal(6, level);
    }

    [Fact]
    public void ResolveLevel_StoreRule_ReturnsStore()
    {
        // .exe 不在 SSOT 已压缩集合内，但规则指定「仅存储」→ Store
        var rules = new List<AdaptiveOverrideRule>
        {
            new() { Name = "可执行文件", FormatIds = new() { "Pe" }, Level = AdaptiveLevel.Store, Enabled = true },
        };

        var level = AdaptiveRuleMatcher.ResolveLevel("a.exe", 6, adaptiveCompression: true, rules: rules);

        Assert.Equal(0, level);
    }

    [Fact]
    public void ResolveLevel_SmartDetectOff_DoesNotUseMagic()
    {
        var path = Path.Combine(_dir, "photo.dat");
        var jpeg = new byte[100 * 1024];
        jpeg[0] = 0xFF; jpeg[1] = 0xD8; jpeg[2] = 0xFF; jpeg[3] = 0xE0;
        File.WriteAllBytes(path, jpeg);

        var withoutDetect = AdaptiveRuleMatcher.ResolveLevel(path, 6, adaptiveCompression: true);
        var withDetect = AdaptiveRuleMatcher.ResolveLevel(path, 6, adaptiveCompression: true, adaptiveSmartDetect: true);

        Assert.Equal(6, withoutDetect);
        Assert.Equal(0, withDetect);
    }
}
