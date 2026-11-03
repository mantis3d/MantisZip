using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 卡片上能展示元数据的格式族。
/// </summary>
public enum MetadataCardKind
{
    /// <summary>不展示（该格式没有解析器，或解析后没有可用字段）。</summary>
    None = 0,

    /// <summary>音频：flac / wav / mp3。</summary>
    Audio = 1,

    /// <summary>视频：mp4 / mkv / mov 等。</summary>
    Video = 2,

    /// <summary>字体：ttf / otf / ttc。</summary>
    Font = 3,

    /// <summary>可执行：exe / dll。</summary>
    Executable = 4,
}

/// <summary>
/// 元数据卡用到的本地化文案。
/// <para>
/// 抽成一张表而不是在 <see cref="MetadataCardFormatter"/> 里直接调 <c>LocalizationManager.T</c>：
/// 一是纯函数才好写单元测试（测试传固定文案，断言不看当前语言）；二是规则 13 要求所有用户可见
/// 文案都走本地化——表由 <see cref="FromLocalization"/> 从三语 json 取。
/// </para>
/// </summary>
public sealed record MetadataCardLabels(
    string Mono,
    string Stereo,
    string ChannelFormat,
    string GlyphFormat,
    string TableFormat)
{
    /// <summary>从 <c>LocalizationManager</c> 取当前语言文案（UI 线程调用）。</summary>
    public static MetadataCardLabels FromLocalization() => new(
        LocalizationManager.T("MetadataCard_Mono"),
        LocalizationManager.T("MetadataCard_Stereo"),
        LocalizationManager.T("MetadataCard_ChannelFmt"),
        LocalizationManager.T("MetadataCard_GlyphFmt"),
        LocalizationManager.T("MetadataCard_TableFmt"));
}

/// <summary>
/// 把 <see cref="FileFormatInfo"/> 压成卡片上的 2–3 行短文字（Tile / Content 布局的元数据卡）。
/// <para>
/// <b>为什么独立成纯函数</b>：卡片一行只有 ~110 px 宽（128 尺寸卡片减去内边距），字段取舍和拼接
/// 属于纯排版逻辑，与「怎么把条目取出来」无关；抽出来就能用假数据写单元测试，不用真解压压缩包。
/// </para>
/// <para>
/// 输出只有值、不带标签（<c>03:45 · 192 kbps</c> 而不是「时长：03:45」）——卡片宽度有限，
/// 标签会吃掉一半可用字符数；单位（kbps / kHz / 字形）本身已带语义。
/// </para>
/// </summary>
public static class MetadataCardFormatter
{
    /// <summary>卡片最多显示几行。超过这个行数在最小尺寸（96 px）卡片里会被裁掉。</summary>
    public const int MaxLines = 3;

    /// <summary>
    /// 按扩展名判断该条目能不能出元数据卡。只有返回非 <see cref="MetadataCardKind.None"/> 的格式，
    /// 才会触发「选中时读一次条目」的取数动作。
    /// </summary>
    public static MetadataCardKind Classify(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".flac" or ".wav" or ".mp3" => MetadataCardKind.Audio,
            ".mp4" or ".m4v" or ".mov" or ".mkv" or ".avi" or ".webm" => MetadataCardKind.Video,
            ".ttf" or ".otf" or ".ttc" => MetadataCardKind.Font,
            ".exe" or ".dll" => MetadataCardKind.Executable,
            _ => MetadataCardKind.None,
        };
    }

    /// <summary>
    /// 生成卡片文本行。返回空集合表示「这个条目没有值得展示的元数据」，调用方应回退到大图标。
    /// </summary>
    public static IReadOnlyList<string> BuildLines(
        MetadataCardKind kind,
        FileFormatInfo? info,
        MetadataCardLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        if (info == null)
            return [];

        var lines = kind switch
        {
            MetadataCardKind.Audio => BuildAudioLines(info, labels),
            MetadataCardKind.Video => BuildVideoLines(info),
            MetadataCardKind.Font => BuildFontLines(info, labels),
            MetadataCardKind.Executable => BuildExecutableLines(info),
            _ => new List<string>(),
        };

        // 去掉空行（解析器常常只填了一部分字段），再截断到 MaxLines：
        // 宁可少一行，也不要让最有价值的信息被卡片下沿裁掉。
        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(MaxLines)
            .ToList();
    }

    private static List<string> BuildAudioLines(FileFormatInfo info, MetadataCardLabels labels)
    {
        var lines = new List<string>(MaxLines);

        // 技术参数按「时长/码率」「采样率/声道」拆两行：卡片一行只能放 ~110 px（≈20 个半角字符），
        // 5 个片段挤一行会折在「16 / bit」中间；位深不进卡片（预览的元数据面板里有）。
        lines.Add(Join(
            FormatDuration(info.Duration),
            info.Bitrate.HasValue ? $"{info.Bitrate.Value} kbps" : null));

        lines.Add(Join(
            info.SampleRate.HasValue ? FormatSampleRate(info.SampleRate.Value) : null,
            info.Channels.HasValue ? FormatChannels(info.Channels.Value, labels) : null));

        // 标题 / 艺术家 / 专辑合成一行：这三项同属「作品信息」一组。
        lines.Add(Join(info.Title, info.Artist, info.Album));

        return lines;
    }

    private static List<string> BuildVideoLines(FileFormatInfo info)
    {
        var resolution = info.VideoWidth is > 0 && info.VideoHeight is > 0
            ? $"{info.VideoWidth.Value}×{info.VideoHeight.Value}"
            : null;

        return
        [
            // 分辨率与时长一行（都短），编码与容器名一行：把编码留在上一行会折成
            // 「1080×1080 · 0:07 ·」+「H.264」，行尾挂一个多余的分隔符。
            Join(resolution, FormatDuration(info.Duration)),
            Join(info.Codec, string.IsNullOrWhiteSpace(info.DisplayName) ? null : info.DisplayName),
            Join(info.Title),
        ];
    }

    private static List<string> BuildFontLines(FileFormatInfo info, MetadataCardLabels labels)
    {
        return
        [
            Join(info.FontName, info.FontStyle),
            Join(
                info.GlyphCount.HasValue
                    ? string.Format(CultureInfo.CurrentCulture, labels.GlyphFormat, info.GlyphCount.Value.ToString("N0", CultureInfo.CurrentCulture))
                    : null,
                info.TableCount.HasValue
                    ? string.Format(CultureInfo.CurrentCulture, labels.TableFormat, info.TableCount.Value)
                    : null),
        ];
    }

    private static List<string> BuildExecutableLines(FileFormatInfo info)
    {
        return
        [
            Join(
                string.IsNullOrWhiteSpace(info.ProductName) ? info.DisplayName : info.ProductName,
                info.ProductVersion is { Length: > 0 } ? $"v{info.ProductVersion}" : info.FileVersion),

            Join(
                string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName,
                info.Architecture,
                info.Subsystem),
        ];
    }

    /// <summary>
    /// 时长：不足一小时写 <c>mm:ss</c>，超过一小时写 <c>h:mm:ss</c>（避免 90 分钟显示成 90:00 难读）。
    /// </summary>
    private static string? FormatDuration(TimeSpan? duration)
    {
        if (duration is not { } value || value <= TimeSpan.Zero)
            return null;

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}"
            : $"{value.Minutes}:{value.Seconds:D2}";
    }

    /// <summary>
    /// 采样率：能被 100 整除时写 kHz（44100 → <c>44.1 kHz</c>、48000 → <c>48 kHz</c>），
    /// 否则保留 Hz（11025 → <c>11025 Hz</c>）——用 kHz 会出现 <c>11.025 kHz</c> 这种超长小数。
    /// </summary>
    private static string? FormatSampleRate(int sampleRate)
    {
        if (sampleRate <= 0)
            return null;

        return sampleRate % 100 == 0
            ? $"{sampleRate / 1000.0:0.#} kHz"
            : $"{sampleRate} Hz";
    }

    /// <summary>声道数：1 / 2 用「单声道 / 立体声」，其余只写数字（解析器给的是计数，不猜 5.1 布局）。</summary>
    private static string? FormatChannels(int channels, MetadataCardLabels labels) => channels switch
    {
        <= 0 => null,
        1 => labels.Mono,
        2 => labels.Stereo,
        _ => string.Format(CultureInfo.CurrentCulture, labels.ChannelFormat, channels),
    };

    /// <summary>用 <c> · </c> 拼接非空片段；全空时返回空串（由调用方按空行丢弃）。</summary>
    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
