using System.Collections.Generic;

namespace MantisZip.Core.Utils;

public static class MetadataKeys
{
    // ── 通用文件信息 ──
    public const string FileName = "FileName";
    public const string FileSize = "FileSize";
    public const string CompressedSize = "CompressedSize";
    public const string CompressionRatio = "CompressionRatio";
    public const string FileModifiedDate = "FileModifiedDate";

    // ── 文档类 ──
    public const string Title = "Title";
    public const string Author = "Author";
    public const string Subject = "Subject";
    public const string PageCount = "PageCount";
    public const string SheetCount = "SheetCount";
    public const string SlideCount = "SlideCount";
    public const string CreatedDate = "CreatedDate";
    public const string DocModifiedDate = "DocModifiedDate";

    // ── 多媒体 ──
    public const string Duration = "Duration";
    public const string SampleRate = "SampleRate";
    public const string Channels = "Channels";
    public const string Bitrate = "Bitrate";
    public const string BitDepth = "BitDepth";
    public const string Artist = "Artist";
    public const string Album = "Album";
    public const string Resolution = "Resolution";
    public const string Codec = "Codec";

    // ── 图片 ──
    public const string Dimensions = "Dimensions";
    public const string ImageDpi = "ImageDpi";
    public const string FrameCount = "FrameCount";

    // ── 种子 ──
    public const string InfoHash = "InfoHash";
    public const string MagnetLink = "MagnetLink";
    public const string TrackerUrl = "TrackerUrl";
    public const string FileCount = "FileCount";
    public const string TotalSize = "TotalSize";
    public const string IsPrivate = "IsPrivate";
    public const string CreatedBy = "CreatedBy";
    public const string TorrentFileName = "TorrentFileName";
    public const string TrackerCount = "TrackerCount";
    public const string AdditionalInfo = "AdditionalInfo";

    // ── 数据库 ──
    public const string TableCount = "TableCount";

    // ── ISO ──
    public const string VolumeLabel = "VolumeLabel";
    public const string IsoFormat = "IsoFormat";

    // ── PE ──
    public const string ProductName = "ProductName";
    public const string CompanyName = "CompanyName";
    public const string FileVersion = "FileVersion";
    public const string ProductVersion = "ProductVersion";
    public const string Architecture = "Architecture";
    public const string Subsystem = "Subsystem";
    public const string Description = "Description";

    // ── 字体 ──
    public const string FontName = "FontName";
    public const string FontStyle = "FontStyle";
    public const string GlyphCount = "GlyphCount";

    // ── ICO ──
    public const string IconCount = "IconCount";

    // ── PDF ──
    public const string Encrypted = "Encrypted";
}

public static class MetadataRegistry
{
    private static readonly Dictionary<string, MetadataFieldDef[]> _fields = new();

    public record MetadataFieldDef(
        string Key,
        string DisplayName,
        string Category // 用于设置 UI 分组
    );

    static MetadataRegistry()
    {
        Register("common", [
            new(MetadataKeys.FileName, "文件名", "文件信息"),
            new(MetadataKeys.FileSize, "文件大小", "文件信息"),
            new(MetadataKeys.CompressedSize, "压缩后大小", "文件信息"),
            new(MetadataKeys.CompressionRatio, "压缩率", "文件信息"),
            new(MetadataKeys.FileModifiedDate, "文件修改日期", "文件信息"),
        ]);

        Register("docx", [
            new(MetadataKeys.Title, "标题", "文档信息"),
            new(MetadataKeys.Author, "作者", "文档信息"),
            new(MetadataKeys.Subject, "主题", "文档信息"),
            new(MetadataKeys.PageCount, "页数", "文档信息"),
            new(MetadataKeys.CreatedDate, "创建日期", "文档信息"),
            new(MetadataKeys.DocModifiedDate, "修改日期", "文档信息"),
        ]);

        Register("xlsx", [
            new(MetadataKeys.Title, "标题", "文档信息"),
            new(MetadataKeys.Author, "作者", "文档信息"),
            new(MetadataKeys.Subject, "主题", "文档信息"),
            new(MetadataKeys.SheetCount, "工作表数", "文档信息"),
            new(MetadataKeys.CreatedDate, "创建日期", "文档信息"),
            new(MetadataKeys.DocModifiedDate, "修改日期", "文档信息"),
        ]);

        Register("pptx", [
            new(MetadataKeys.Title, "标题", "文档信息"),
            new(MetadataKeys.Author, "作者", "文档信息"),
            new(MetadataKeys.Subject, "主题", "文档信息"),
            new(MetadataKeys.SlideCount, "幻灯片数", "文档信息"),
            new(MetadataKeys.CreatedDate, "创建日期", "文档信息"),
            new(MetadataKeys.DocModifiedDate, "修改日期", "文档信息"),
        ]);

        Register("pe", [
            new(MetadataKeys.ProductName, "产品名称", "PE 信息"),
            new(MetadataKeys.CompanyName, "公司", "PE 信息"),
            new(MetadataKeys.FileVersion, "文件版本", "PE 信息"),
            new(MetadataKeys.ProductVersion, "产品版本", "PE 信息"),
            new(MetadataKeys.Architecture, "架构", "PE 信息"),
            new(MetadataKeys.Subsystem, "子系统", "PE 信息"),
            new(MetadataKeys.Description, "说明", "PE 信息"),
        ]);

        Register("image", [
            new(MetadataKeys.Dimensions, "尺寸", "图片信息"),
            new(MetadataKeys.ImageDpi, "DPI", "图片信息"),
            new(MetadataKeys.FrameCount, "帧数", "图片信息"),
        ]);

        Register("audio", [
            new(MetadataKeys.Duration, "时长", "音频信息"),
            new(MetadataKeys.SampleRate, "采样率", "音频信息"),
            new(MetadataKeys.BitDepth, "位深", "音频信息"),
            new(MetadataKeys.Channels, "声道", "音频信息"),
            new(MetadataKeys.Bitrate, "码率", "音频信息"),
            new(MetadataKeys.Artist, "艺术家", "音频信息"),
            new(MetadataKeys.Album, "专辑", "音频信息"),
        ]);

        Register("video", [
            new(MetadataKeys.Duration, "时长", "视频信息"),
            new(MetadataKeys.Resolution, "分辨率", "视频信息"),
            new(MetadataKeys.Codec, "编码", "视频信息"),
            new(MetadataKeys.Bitrate, "码率", "视频信息"),
        ]);

        Register("font", [
            new(MetadataKeys.FontName, "字体名", "字体信息"),
            new(MetadataKeys.FontStyle, "样式", "字体信息"),
            new(MetadataKeys.GlyphCount, "字形数", "字体信息"),
        ]);

        Register("torrent", [
            new(MetadataKeys.InfoHash, "InfoHash", "种子信息"),
            new(MetadataKeys.FileCount, "文件数", "种子信息"),
            new(MetadataKeys.TotalSize, "总大小", "种子信息"),
            new(MetadataKeys.IsPrivate, "私有", "种子信息"),
            new(MetadataKeys.CreatedBy, "创建者", "种子信息"),
            new(MetadataKeys.TorrentFileName, "种子名称", "种子信息"),
            new(MetadataKeys.MagnetLink, "Magnet 链接", "种子信息"),
            new(MetadataKeys.TrackerUrl, "Tracker", "种子信息"),
            new(MetadataKeys.TrackerCount, "Tracker 数量", "种子信息"),
            new(MetadataKeys.CreatedDate, "创建日期", "种子信息"),
            new(MetadataKeys.AdditionalInfo, "备注", "种子信息"),
        ]);

        Register("iso", [
            new(MetadataKeys.VolumeLabel, "卷标", "ISO 信息"),
            new(MetadataKeys.IsoFormat, "格式", "ISO 信息"),
            new(MetadataKeys.TotalSize, "大小", "ISO 信息"),
        ]);

        Register("sqlite", [
            new(MetadataKeys.TableCount, "表数量", "数据库信息"),
        ]);

        Register("ico", [
            new(MetadataKeys.IconCount, "图标数量", "图标信息"),
        ]);

        Register("pdf", [
            new(MetadataKeys.Title, "标题", "文档信息"),
            new(MetadataKeys.Author, "作者", "文档信息"),
            new(MetadataKeys.Subject, "主题", "文档信息"),
            new(MetadataKeys.PageCount, "页数", "文档信息"),
            new(MetadataKeys.Encrypted, "加密", "文档信息"),
            new(MetadataKeys.CreatedDate, "创建日期", "文档信息"),
            new(MetadataKeys.DocModifiedDate, "修改日期", "文档信息"),
        ]);
    }

    private static void Register(string typeKey, MetadataFieldDef[] fields)
    {
        _fields[typeKey] = fields;
    }

    public static MetadataFieldDef[] GetFields(string typeKey)
    {
        return _fields.TryGetValue(typeKey, out var fields) ? fields : [];
    }

    public static IEnumerable<string> GetAllTypeKeys() => _fields.Keys;
}
