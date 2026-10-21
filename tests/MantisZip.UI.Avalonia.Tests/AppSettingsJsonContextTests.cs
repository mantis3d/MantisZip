using System.Text.Json;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// AppSettings 的 STJ 源生成上下文（stj-source-gen-appsettings）回归测试：
/// 锁定往返一致性、旧 int 字典大小转换器兼容、与反射路径输出逐字一致、未知字段忽略。
/// </summary>
public class AppSettingsJsonContextTests
{
    [Fact]
    public void RoundTrip_PreservesNestedCollectionsAndEnums()
    {
        var source = new AppSettings
        {
            DefaultLevel = 7,
            SevenZipDictionarySize = "256m",
            FavoritePaths = { @"C:\one", @"D:\two" },
            CustomAssocExtensions = { ".abc", ".def" },
            AdaptiveOverrides =
            {
                new AdaptiveOverrideRule
                {
                    Name = "图片",
                    FormatIds = { "Jpeg", "Png" },
                    Level = AdaptiveLevel.Custom,
                    CustomLevel = 3,
                    Enabled = false,
                },
            },
            CustomFormats =
            {
                new FormatDefinition
                {
                    Id = "MyFmt",
                    DisplayName = "My Format",
                    Extensions = { ".my" },
                    MagicHex = "AABBCC",
                    IsBuiltIn = false,
                },
            },
            FilterPresets =
            {
                new FileFilterPreset("p", new FileFilterCriteria
                {
                    IncludeExtensions = { ".txt", ".md" },
                }),
            },
        };

        var json = JsonSerializer.Serialize(source, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);

        Assert.NotNull(back);
        Assert.Equal(7, back!.DefaultLevel);
        Assert.Equal("256m", back.SevenZipDictionarySize);
        Assert.Equal(new[] { @"C:\one", @"D:\two" }, back.FavoritePaths);
        Assert.Equal(new[] { ".abc", ".def" }, back.CustomAssocExtensions);

        var rule = Assert.Single(back.AdaptiveOverrides);
        Assert.Equal("图片", rule.Name);
        Assert.Equal(new[] { "Jpeg", "Png" }, rule.FormatIds);
        Assert.Equal(AdaptiveLevel.Custom, rule.Level);
        Assert.Equal(3, rule.CustomLevel);
        Assert.False(rule.Enabled);

        var fmt = Assert.Single(back.CustomFormats);
        Assert.Equal("MyFmt", fmt.Id);
        Assert.Equal("AABBCC", fmt.MagicHex);
        Assert.False(fmt.IsBuiltIn);

        var preset = Assert.Single(back.FilterPresets);
        Assert.Equal("p", preset.Name);
        Assert.Equal(new[] { ".txt", ".md" }, preset.Criteria.IncludeExtensions);
    }

    [Fact]
    public void LegacyIntegerDictionarySize_IsConvertedToSizeString()
    {
        const string json = """{"SevenZipDictionarySize":268435456}""";

        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);

        Assert.NotNull(back);
        Assert.Equal("256m", back!.SevenZipDictionarySize);
    }

    [Fact]
    public void SourceGeneratedOutput_MatchesReflectionOutput()
    {
        var settings = new AppSettings();

        var viaSourceGen = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var viaReflection = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

        Assert.Equal(viaReflection, viaSourceGen);
    }

    [Fact]
    public void UnknownFields_AreIgnored()
    {
        const string json = """{"FutureField":123,"DefaultLevel":3}""";

        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);

        Assert.NotNull(back);
        Assert.Equal(3, back!.DefaultLevel);
    }
}
