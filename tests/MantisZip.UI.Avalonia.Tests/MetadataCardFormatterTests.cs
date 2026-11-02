using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// <see cref="MetadataCardFormatter"/> 的用例：只有「格式分类」和「字段压成 2–3 行短文字」两件事，
/// 全部用假数据构造 <see cref="FileFormatInfo"/>，不碰真实压缩包。
///
/// <para>
/// 测试传固定文案表（<see cref="Labels"/>）而不是 <c>MetadataCardLabels.FromLocalization()</c>：
/// 断言不该依赖当前语言，否则换语言跑测试会红。
/// </para>
/// </summary>
public class MetadataCardFormatterTests
{
    private static readonly MetadataCardLabels Labels = new(
        Mono: "单声道",
        Stereo: "立体声",
        ChannelFormat: "{0} 声道",
        GlyphFormat: "{0} 字形",
        TableFormat: "{0} 表");

    [Theory]
    [InlineData("song.mp3", MetadataCardKind.Audio)]
    [InlineData("song.FLAC", MetadataCardKind.Audio)]
    [InlineData("clip.wav", MetadataCardKind.Audio)]
    [InlineData("movie.mp4", MetadataCardKind.Video)]
    [InlineData("movie.MKV", MetadataCardKind.Video)]
    [InlineData("font.ttf", MetadataCardKind.Font)]
    [InlineData("font.otf", MetadataCardKind.Font)]
    [InlineData("app.exe", MetadataCardKind.Executable)]
    [InlineData("lib.dll", MetadataCardKind.Executable)]
    [InlineData("readme.txt", MetadataCardKind.None)]
    [InlineData("photo.png", MetadataCardKind.None)]
    [InlineData(null, MetadataCardKind.None)]
    public void Classify_MapsExtensionToKind(string? name, MetadataCardKind expected)
    {
        Assert.Equal(expected, MetadataCardFormatter.Classify(name));
    }

    [Fact]
    public void BuildAudio_ComposesTechnicalAndPeopleLines()
    {
        var info = new FileFormatInfo
        {
            Duration = TimeSpan.FromSeconds(225),
            Bitrate = 192,
            SampleRate = 44100,
            Channels = 2,
            BitDepth = 16,
            Title = "夜曲",
            Artist = "周杰伦",
            Album = "十一月的萧邦",
        };

        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Audio, info, Labels);

        Assert.Equal(3, lines.Count);
        Assert.Equal("3:45 · 192 kbps", lines[0]);
        Assert.Equal("44.1 kHz · 立体声", lines[1]);
        Assert.Equal("夜曲 · 周杰伦 · 十一月的萧邦", lines[2]);
    }

    [Fact]
    public void BuildAudio_OverOneHour_UsesHourMinuteSecond()
    {
        var info = new FileFormatInfo { Duration = TimeSpan.FromSeconds(5400) };

        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Audio, info, Labels);

        Assert.Single(lines);
        Assert.Equal("1:30:00", lines[0]);
    }

    [Fact]
    public void BuildAudio_SampleRateNotMultipleOfThousand_KeepsHz()
    {
        var info = new FileFormatInfo { SampleRate = 11025 };

        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Audio, info, Labels);

        Assert.Single(lines);
        Assert.Equal("11025 Hz", lines[0]);
    }

    [Fact]
    public void BuildAudio_MonoAndMultichannel_UseLocalizedLabels()
    {
        var mono = MetadataCardFormatter.BuildLines(
            MetadataCardKind.Audio, new FileFormatInfo { Channels = 1 }, Labels);
        var surround = MetadataCardFormatter.BuildLines(
            MetadataCardKind.Audio, new FileFormatInfo { Channels = 6 }, Labels);

        Assert.Equal("单声道", mono[0]);
        Assert.Equal("6 声道", surround[0]);
    }

    [Fact]
    public void BuildAudio_EmptyInfo_YieldsNoLines()
    {
        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Audio, new FileFormatInfo(), Labels);

        Assert.Empty(lines);
    }

    [Fact]
    public void BuildVideo_ComposesResolutionAndContainerLines()
    {
        var info = new FileFormatInfo
        {
            VideoWidth = 1920,
            VideoHeight = 1080,
            Duration = TimeSpan.FromSeconds(95),
            Codec = "H.264",
            DisplayName = "MP4 视频",
        };

        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Video, info, Labels);

        Assert.Equal(2, lines.Count);
        Assert.Equal("1920×1080 · 1:35", lines[0]);
        Assert.Equal("H.264 · MP4 视频", lines[1]);
    }

    [Fact]
    public void BuildVideo_WithTitle_PutsTitleOnItsOwnLine()
    {
        var info = new FileFormatInfo
        {
            VideoWidth = 1280,
            VideoHeight = 720,
            Title = "旅行记录",
            DisplayName = "MKV 视频",
        };

        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Video, info, Labels);

        Assert.Equal(3, lines.Count);
        Assert.Equal("1280×720", lines[0]);
        Assert.Equal("MKV 视频", lines[1]);
        Assert.Equal("旅行记录", lines[2]);
    }

    [Fact]
    public void BuildVideo_WithoutResolution_StillShowsTitle()
    {
        var info = new FileFormatInfo { Title = "未知尺寸的片段" };

        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Video, info, Labels);

        Assert.Single(lines);
        Assert.Equal("未知尺寸的片段", lines[0]);
    }

    [Fact]
    public void BuildFont_ComposesNameAndCounts()
    {
        var info = new FileFormatInfo
        {
            FontName = "Source Han Sans SC",
            FontStyle = "Regular",
            GlyphCount = 1234,
            TableCount = 12,
        };

        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Font, info, Labels);

        var expectedCounts = string.Format(
            CultureInfo.CurrentCulture,
            Labels.GlyphFormat,
            1234.ToString("N0", CultureInfo.CurrentCulture));

        Assert.Equal(2, lines.Count);
        Assert.Equal("Source Han Sans SC · Regular", lines[0]);
        Assert.Equal($"{expectedCounts} · 12 表", lines[1]);
    }

    [Fact]
    public void BuildExecutable_ComposesProductAndPlatformLines()
    {
        var info = new FileFormatInfo
        {
            ProductName = "MantisZip",
            ProductVersion = "0.5.6",
            CompanyName = "mantis3d",
            Architecture = "x64",
            Subsystem = "GUI",
        };

        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Executable, info, Labels);

        Assert.Equal(2, lines.Count);
        Assert.Equal("MantisZip · v0.5.6", lines[0]);
        Assert.Equal("mantis3d · x64 · GUI", lines[1]);
    }

    [Fact]
    public void BuildExecutable_WithoutProductName_FallsBackToDisplayNameAndFileVersion()
    {
        var info = new FileFormatInfo
        {
            DisplayName = "应用",
            FileVersion = "1.0.0.0",
        };

        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Executable, info, Labels);

        Assert.Single(lines);
        Assert.Equal("应用 · 1.0.0.0", lines[0]);
    }

    [Fact]
    public void BuildLines_NullInfo_YieldsNoLines()
    {
        foreach (var kind in Enum.GetValues<MetadataCardKind>())
            Assert.Empty(MetadataCardFormatter.BuildLines(kind, null, Labels));
    }

    [Fact]
    public void BuildLines_NeverExceedsMaxLines()
    {
        var info = new FileFormatInfo
        {
            Duration = TimeSpan.FromSeconds(225),
            Bitrate = 320,
            SampleRate = 48000,
            Channels = 2,
            BitDepth = 24,
            Title = "标题",
            Artist = "艺术家",
            Album = "专辑",
        };

        var lines = MetadataCardFormatter.BuildLines(MetadataCardKind.Audio, info, Labels);

        Assert.True(lines.Count <= MetadataCardFormatter.MaxLines);
        Assert.DoesNotContain(lines, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void BuildLines_RejectsNullLabels()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MetadataCardFormatter.BuildLines(MetadataCardKind.Audio, new FileFormatInfo(), null!));
    }
}
