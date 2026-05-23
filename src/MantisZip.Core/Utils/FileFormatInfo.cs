using System;

namespace MantisZip.Core.Utils;

/// <summary>
/// 扩展预览格式的共享数据模型。
/// 所有格式解码器的输出统一使用此模型，信息面板根据其字段动态渲染。
/// 魔数检测计划 (Plan B) 直接引用此模型，不重新定义。
/// </summary>
public class FileFormatInfo
{
    /// <summary>格式枚举</summary>
    public FileFormat Format { get; set; } = FileFormat.Unknown;

    /// <summary>人可读的格式名称，如 "JPEG 图像"、"PE 可执行文件"</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>原始扩展名（含点），如 ".exe"</summary>
    public string Extension { get; set; } = string.Empty;

    // ── 通用 ──
    /// <summary>文件大小（字节）</summary>
    public long? FileSize { get; set; }

    // ── 图像 ──
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public int? BitDepth { get; set; }
    /// <summary>像素格式，如 "Bgra32"、"Rgba32"</summary>
    public string? PixelFormat { get; set; }
    /// <summary>压缩方式，TGA "无压缩/RLE" / EXR "PIZ/B44"</summary>
    public string? Compression { get; set; }

    // ── 音频/视频 ──
    public TimeSpan? Duration { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public int? Bitrate { get; set; }
    public string? Codec { get; set; }
    public int? VideoWidth { get; set; }
    public int? VideoHeight { get; set; }

    // ── 文档/电子书 ──
    public int? PageCount { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public DateTime? CreationDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    // ── 可执行文件 ──
    public string? CompanyName { get; set; }
    public string? ProductName { get; set; }
    public string? FileVersion { get; set; }
    public string? ProductVersion { get; set; }
    /// <summary>架构，如 "x86"、"x64"、"ARM64"</summary>
    public string? Architecture { get; set; }
    /// <summary>子系统，如 "GUI"、"CUI"、"DLL"</summary>
    public string? Subsystem { get; set; }

    // ── ICL 图标库 ──
    public int? IconCount { get; set; }
    /// <summary>图标尺寸范围，如 "16×16 ~ 256×256"</summary>
    public string? IconSizes { get; set; }

    // ── 压缩包 ──
    public int? EntryCount { get; set; }
    public double? CompressionRatio { get; set; }
    public long? UncompressedSize { get; set; }
    public bool? IsEncrypted { get; set; }
    public string? CompressionMethod { get; set; }

    // ── 3D ──
    public int? VertexCount { get; set; }
    public int? FaceCount { get; set; }

    // ── 数据库 ──
    public int? TableCount { get; set; }
    public List<string>? TableNames { get; set; }
    public string? TextEncoding { get; set; }

    // ── 字体 ──
    public string? FontName { get; set; }
    public string? FontStyle { get; set; }
    public int? GlyphCount { get; set; }
    /// <summary>WOFF/WOFF2 解压后的临时 TTF/OTF 路径，供 FontFamily 渲染使用</summary>
    public string? FontDecompressedPath { get; set; }

    // ── 证书 ──
    public string? Issuer { get; set; }
    public string? SubjectName { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }

    // ── BT 种子 ──
    public string? TorrentFileName { get; set; }
    public long? TorrentTotalSize { get; set; }
    /// <summary>种子内文件列表（路径 / 大小），用于目录树展示</summary>
    public List<(string Path, long Size)>? TorrentFileEntries { get; set; }
    public long? PieceSize { get; set; }
    public int? PieceCount { get; set; }
    public string? InfoHashV1 { get; set; }
    public string? MagnetLink { get; set; }
    public string? TrackerUrl { get; set; }
    public int? TrackerCount { get; set; }
    public bool? IsPrivate { get; set; }
    public string? CreatedBy { get; set; }
    public int? FileCount { get; set; }

    // ── 磁盘映像 ──
    public string? VolumeLabel { get; set; }
    public long? DiskSize { get; set; }

    // ── Windows 快捷方式 ──
    public string? LinkTarget { get; set; }

    // ── 字幕 ──
    public int? SubtitleEntryCount { get; set; }

    // ── 通用兜底 ──
    public string? AdditionalInfo { get; set; }
}

/// <summary>
/// 已知文件格式枚举。用于 <see cref="FileFormatInfo.Format"/>。
/// 魔数检测计划 (Plan B) 的 <see cref="FileFormatDetector"/> 使用相同枚举。
/// </summary>
public enum FileFormat
{
    Unknown,
    // 图像
    Jpeg, Png, Gif, Bmp, WebP, Ico, Tga, Hdr, Exr, Svg,
    // 音频
    Wav, Flac, Mp3,
    // 视频
    Mp4, Mkv, WebM, Wmv, Mov, Avi,
    // 文档
    Pdf, Docx, Xlsx, Pptx, Epub, Mobi, Azw3,
    // 文本/标记
    Text, Html, Markdown,
    // 可执行
    Pe, Elf,
    // 压缩包
    Zip, SevenZip, Rar, Tar, Gz, Bz2, Xz, Zstd, Iso,
    // 数据库
    Sqlite, Dbf,
    // 3D
    Stl, Dxf, Step, Fbx,
    // 字体
    Ttf, Otf, Woff,
    // 其他
    Torrent, Dicom, Cer, Pfx, Lnk, Vhd, Vmdk, Icl,
    Subtitle, OfficeOpenXml, OfficeLegacy,
    // 映像
    VhdLegacy, Iso9660, Udf,
}
