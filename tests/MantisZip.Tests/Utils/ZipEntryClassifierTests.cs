using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// ZipEntryClassifier 单元测试 — 覆盖内置已压缩扩展名分类、GetAdaptiveLevel 三开关分流决策。
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

    #region GetAdaptiveLevel(3 参) — 自适应开关分流

    [Fact]
    public void GetAdaptiveLevel_AdaptiveOff_ReturnsUserLevel()
    {
        // 未启用自适应 → 任何文件都用用户级别
        Assert.Equal(5, ZipEntryClassifier.GetAdaptiveLevel("photo.jpg", 5, adaptive: false));
        Assert.Equal(9, ZipEntryClassifier.GetAdaptiveLevel("readme.txt", 9, adaptive: false));
    }

    [Fact]
    public void GetAdaptiveLevel_AdaptiveOn_CompressedFile_ReturnsStore()
    {
        Assert.Equal(0, ZipEntryClassifier.GetAdaptiveLevel("photo.jpg", 5, adaptive: true));
        Assert.Equal(0, ZipEntryClassifier.GetAdaptiveLevel("video.mp4", 5, adaptive: true));
    }

    [Fact]
    public void GetAdaptiveLevel_AdaptiveOn_TextFile_ReturnsUserLevel()
    {
        Assert.Equal(5, ZipEntryClassifier.GetAdaptiveLevel("readme.txt", 5, adaptive: true));
        Assert.Equal(7, ZipEntryClassifier.GetAdaptiveLevel("Program.cs", 7, adaptive: true));
    }

    #endregion

    #region GetAdaptiveLevel(4 参) — MultiThreadedStoreFormatIds 自定义覆盖

    [Fact]
    public void GetAdaptiveLevel_AdaptiveOff_IgnoresCustomStoreFormats()
    {
        // 未启用自适应时，即使指定自定义 Store 格式也忽略
        var custom = new HashSet<string> { "Jpeg" };
        Assert.Equal(5, ZipEntryClassifier.GetAdaptiveLevel("photo.jpg", 5, adaptive: false, custom));
    }

    [Fact]
    public void GetAdaptiveLevel_CustomStoreFormatId_ReturnsStore()
    {
        // 格式目录中非内置已压缩的格式 ID（如 Png 扩展名 .png 虽在内置列表，
        // 用不在 CompressedExtensions 里的格式验证自定义路径生效）
        // 注意：.wav 不在内置已压缩列表，但属于 FormatCatalog 的 Wav 格式
        var custom = new HashSet<string> { "Wav" };
        Assert.Equal(0, ZipEntryClassifier.GetAdaptiveLevel("sound.wav", 5, adaptive: true, custom));
    }

    [Fact]
    public void GetAdaptiveLevel_NonCustomStoreFormatId_ReturnsUserLevel()
    {
        // 格式 ID 不在自定义列表 → 不 Store
        var custom = new HashSet<string> { "Wav" };
        Assert.Equal(5, ZipEntryClassifier.GetAdaptiveLevel("readme.txt", 5, adaptive: true, custom));
    }

    [Fact]
    public void GetAdaptiveLevel_UnknownFormatId_ReturnsUserLevel()
    {
        // 自定义列表含不存在的格式 ID → 忽略，不 Store
        var custom = new HashSet<string> { "NonExistentFormat" };
        Assert.Equal(5, ZipEntryClassifier.GetAdaptiveLevel("readme.txt", 5, adaptive: true, custom));
    }

    [Fact]
    public void GetAdaptiveLevel_NullCustomStoreFormats_FallsBackToBuiltin()
    {
        // customStoreFormatIds 为 null → 仅内置已压缩判定生效
        Assert.Equal(0, ZipEntryClassifier.GetAdaptiveLevel("photo.jpg", 5, adaptive: true, null));
        Assert.Equal(5, ZipEntryClassifier.GetAdaptiveLevel("readme.txt", 5, adaptive: true, null));
    }

    [Fact]
    public void GetAdaptiveLevel_BuiltInTakesPrecedenceOverCustomList()
    {
        // .jpg 内置已压缩 → Store，即使自定义列表不含它
        var custom = new HashSet<string> { "Wav" };
        Assert.Equal(0, ZipEntryClassifier.GetAdaptiveLevel("photo.jpg", 5, adaptive: true, custom));
    }

    #endregion
}