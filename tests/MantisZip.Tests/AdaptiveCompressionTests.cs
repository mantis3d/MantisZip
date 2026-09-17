using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// 自适应压缩服务单元测试 — 覆盖 CompressionCoefficients、FormatCatalog、AdaptiveRuleMatcher。
/// </summary>
public class AdaptiveCompressionTests
{
    #region CompressionCoefficients — ClassifyByExtension

    [Fact]
    public void ClassifyByExtension_ReturnsCorrectCategory()
    {
        Assert.Equal("image_lossy", CompressionCoefficients.ClassifyByExtension("photo.jpg"));
        Assert.Equal("code", CompressionCoefficients.ClassifyByExtension("Program.cs"));
        Assert.Equal("archive", CompressionCoefficients.ClassifyByExtension("backup.zip"));
        Assert.Equal("binary", CompressionCoefficients.ClassifyByExtension("document.pdf"));
        Assert.Equal("media", CompressionCoefficients.ClassifyByExtension("song.mp3"));
        Assert.Equal("text", CompressionCoefficients.ClassifyByExtension("readme.txt"));
    }

    [Fact]
    public void ClassifyByExtension_NoExtension_ReturnsBinary()
    {
        Assert.Equal("binary", CompressionCoefficients.ClassifyByExtension("Makefile"));
    }

    [Fact]
    public void ClassifyByExtension_UnknownExtension_ReturnsBinary()
    {
        Assert.Equal("binary", CompressionCoefficients.ClassifyByExtension("file.xyz123"));
    }

    #endregion

    #region CompressionCoefficients — GetRate

    [Fact]
    public void GetRate_TextCategory_ReturnsDecreasingRates()
    {
        var rate1 = CompressionCoefficients.GetRate("text", 1, ArchiveFormat.Zip);
        var rate5 = CompressionCoefficients.GetRate("text", 5, ArchiveFormat.Zip);
        var rate9 = CompressionCoefficients.GetRate("text", 9, ArchiveFormat.Zip);

        // 级别越高，压缩率越小（压缩效果越好）
        Assert.True(rate1 > rate5, $"rate1={rate1} should be > rate5={rate5}");
        Assert.True(rate5 > rate9, $"rate5={rate5} should be > rate9={rate9}");
    }

    [Fact]
    public void GetRate_StoreLevel_Returns100Percent()
    {
        // Store（级别 0）在所有分类下都是 1.0
        var rate = CompressionCoefficients.GetRate("text", 0, ArchiveFormat.Zip);
        Assert.Equal(1.0, rate);
    }

    [Fact]
    public void GetRate_SevenZip_BetterThanZip()
    {
        // 7z 压缩效果应优于 ZIP（rate 更小）
        var zipRate = CompressionCoefficients.GetRate("text", 5, ArchiveFormat.Zip);
        var sevenZipRate = CompressionCoefficients.GetRate("text", 5, ArchiveFormat.SevenZip);
        Assert.True(sevenZipRate <= zipRate,
            $"7z rate {sevenZipRate} should be <= ZIP rate {zipRate}");

        // 二进制类同理
        var zipBinary = CompressionCoefficients.GetRate("binary", 5, ArchiveFormat.Zip);
        var sevenZipBinary = CompressionCoefficients.GetRate("binary", 5, ArchiveFormat.SevenZip);
        Assert.True(sevenZipBinary <= zipBinary,
            $"7z binary rate {sevenZipBinary} should be <= ZIP binary rate {zipBinary}");
    }

    [Fact]
    public void GetRate_UnknownCategory_ReturnsFallback()
    {
        // 未定义分类应回退到 0.60
        var rate = CompressionCoefficients.GetRate("nonexistent_category", 5, ArchiveFormat.Zip);
        Assert.Equal(0.60, rate);
    }

    #endregion

    #region FormatCatalog — GetAll

    [Fact]
    public void GetAll_BuiltInFormats_ContainsJpeg()
    {
        var formats = FormatCatalog.GetAll();
        var jpeg = formats.FirstOrDefault(f => f.Id == "Jpeg");
        Assert.NotNull(jpeg);
        Assert.Contains(".jpg", jpeg.Extensions);
        Assert.Contains(".jpeg", jpeg.Extensions);
        Assert.True(jpeg.IsBuiltIn);
    }

    [Fact]
    public void GetAll_WithCustomFormats_IncludesCustom()
    {
        var custom = new List<FormatDefinition>
        {
            new() { Id = "CustomFmt", DisplayName = "自定义格式", Extensions = new() { ".cst" }, IsBuiltIn = false }
        };
        var formats = FormatCatalog.GetAll(custom);
        var found = formats.FirstOrDefault(f => f.Id == "CustomFmt");
        Assert.NotNull(found);
        Assert.Equal("自定义格式", found.DisplayName);
    }

    #endregion

    #region FormatCatalog — GetById

    [Fact]
    public void GetById_ExistingFormat_ReturnsMatch()
    {
        var format = FormatCatalog.GetById("Jpeg");
        Assert.NotNull(format);
        Assert.Equal("Jpeg", format.Id);
    }

    [Fact]
    public void GetById_NonExistent_ReturnsNull()
    {
        var format = FormatCatalog.GetById("NonExistentFormat");
        Assert.Null(format);
    }

    #endregion

    #region FormatCatalog — GetByExtension

    [Fact]
    public void GetByExtension_CaseInsensitive()
    {
        var format = FormatCatalog.GetByExtension(".JPG");
        Assert.NotNull(format);
        Assert.Equal("Jpeg", format.Id);
    }

    [Fact]
    public void GetByExtension_NonExistent_ReturnsNull()
    {
        var format = FormatCatalog.GetByExtension(".unknownext");
        Assert.Null(format);
    }

    #endregion

    #region AdaptiveRuleMatcher — ResolveLevel

    [Fact]
    public void ResolveLevel_Disabled_ReturnsGlobalLevel()
    {
        var level = AdaptiveRuleMatcher.ResolveLevel(
            "photo.jpg", 5, AdaptiveCompressionMode.Disabled);
        Assert.Equal(5, level);
    }

    [Fact]
    public void ResolveLevel_StoreForCompressed_ImageLossy_ReturnsStore()
    {
        var level = AdaptiveRuleMatcher.ResolveLevel(
            "photo.jpg", 5, AdaptiveCompressionMode.StoreForCompressed);
        Assert.Equal(0, level);
    }

    [Fact]
    public void ResolveLevel_StoreForCompressed_Text_ReturnsGlobalLevel()
    {
        var level = AdaptiveRuleMatcher.ResolveLevel(
            "readme.txt", 5, AdaptiveCompressionMode.StoreForCompressed);
        Assert.Equal(5, level);
    }

    [Fact]
    public void ResolveLevel_WithRule_MatchesCorrectly()
    {
        var rules = new List<AdaptiveOverrideRule>
        {
            new()
            {
                Name = "图片规则",
                FormatIds = new() { "Jpeg" },
                Level = AdaptiveLevel.Store,
                Enabled = true
            }
        };
        var level = AdaptiveRuleMatcher.ResolveLevel(
            "photo.jpg", 5, AdaptiveCompressionMode.SmartDetect, rules);
        Assert.Equal(0, level);
    }

    [Fact]
    public void ResolveLevel_RuleDisabled_Skipped()
    {
        var rules = new List<AdaptiveOverrideRule>
        {
            new()
            {
                Name = "禁用规则",
                FormatIds = new() { "Jpeg" },
                Level = AdaptiveLevel.Store,
                Enabled = false // 禁用
            }
        };
        // 规则被禁用，应走内置分类（image_lossy → Store）
        var level = AdaptiveRuleMatcher.ResolveLevel(
            "photo.jpg", 5, AdaptiveCompressionMode.SmartDetect, rules);
        Assert.Equal(0, level); // 内置分类 image_lossy 也是 Store
    }

    [Fact]
    public void ResolveLevel_RuleBeforeBuiltin_Wins()
    {
        // 用户规则将 .jpg 设置为非 Store 级别，覆盖内置 image_lossy → Store 行为
        var rules = new List<AdaptiveOverrideRule>
        {
            new()
            {
                Name = "强制压缩图片",
                FormatIds = new() { "Jpeg" },
                Level = AdaptiveLevel.Normal, // 强制级别 5
                Enabled = true
            }
        };
        var level = AdaptiveRuleMatcher.ResolveLevel(
            "photo.jpg", 7, AdaptiveCompressionMode.SmartDetect, rules);
        Assert.Equal(5, level); // 规则优先于内置分类
    }

    #endregion

    #region AdaptiveRuleMatcher — ResolveAdaptiveLevel

    [Fact]
    public void ResolveAdaptiveLevel_Store_Returns0()
    {
        Assert.Equal(0, AdaptiveRuleMatcher.ResolveAdaptiveLevel(
            AdaptiveLevel.Store, null, 5));
    }

    [Fact]
    public void ResolveAdaptiveLevel_GlobalPlusOne_Clamps9()
    {
        // globalLevel=9 → 9+1=10，clamp 到 9
        Assert.Equal(9, AdaptiveRuleMatcher.ResolveAdaptiveLevel(
            AdaptiveLevel.GlobalPlusOne, null, 9));
    }

    [Fact]
    public void ResolveAdaptiveLevel_GlobalMinusOne_Clamps0()
    {
        // globalLevel=0 → 0-1=-1，clamp 到 0
        Assert.Equal(0, AdaptiveRuleMatcher.ResolveAdaptiveLevel(
            AdaptiveLevel.GlobalMinusOne, null, 0));
    }

    [Fact]
    public void ResolveAdaptiveLevel_Custom_ReturnsCustomLevel()
    {
        Assert.Equal(7, AdaptiveRuleMatcher.ResolveAdaptiveLevel(
            AdaptiveLevel.Custom, 7, 5));
    }

    #endregion

    #region AdaptiveRuleMatcher — ComputeMajorityLevel

    [Fact]
    public void ComputeMajorityLevel_MostlyCompressed_ReturnsStore()
    {
        // 4 个已压缩文件 vs 1 个可压缩文件 → 多数已压缩 → Store
        var files = new List<string> { "a.jpg", "b.mp4", "c.zip", "d.mp3", "e.txt" };
        var level = AdaptiveRuleMatcher.ComputeMajorityLevel(files, 5);
        Assert.Equal(0, level);
    }

    [Fact]
    public void ComputeMajorityLevel_MostlyText_ReturnsGlobalLevel()
    {
        // 3 个可压缩文件 vs 2 个已压缩文件 → 多数可压缩 → globalLevel
        var files = new List<string> { "a.txt", "b.cs", "c.py", "d.jpg", "e.mp4" };
        var level = AdaptiveRuleMatcher.ComputeMajorityLevel(files, 5);
        Assert.Equal(5, level);
    }

    [Fact]
    public void ComputeMajorityLevel_Empty_ReturnsGlobalLevel()
    {
        var level = AdaptiveRuleMatcher.ComputeMajorityLevel(new List<string>(), 5);
        Assert.Equal(5, level);
    }

    #endregion
}
