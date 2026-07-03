namespace MantisZip.Core.Utils;

/// <summary>
/// 文件格式辅助方法。
/// </summary>
public static class FileFormatHelper
{
    /// <summary>
    /// 获取文件格式的中文显示名称。
    /// </summary>
    /// <param name="format">文件格式枚举值。</param>
    /// <returns>中文显示名称，未枚举的格式返回 format.ToString()。</returns>
    public static string GetDisplayName(FileFormat format)
    {
        return format switch
        {
            FileFormat.Unknown => "未知格式",
            FileFormat.Jpeg => "JPEG 图像",
            FileFormat.Png => "PNG 图像",
            FileFormat.Gif => "GIF 动画",
            FileFormat.Bmp => "BMP 位图",
            FileFormat.WebP => "WebP 图像",
            FileFormat.Ico => "ICO 图标",
            FileFormat.Tga => "TGA 图像",
            FileFormat.Hdr => "HDR 图像",
            FileFormat.Exr => "OpenEXR 图像",
            FileFormat.Svg => "SVG 矢量图",
            FileFormat.Wav => "WAV 音频",
            FileFormat.Flac => "FLAC 音频",
            FileFormat.Mp3 => "MP3 音频",
            FileFormat.Ogg => "Ogg Vorbis 音频",
            FileFormat.Mp4 => "MP4 视频",
            FileFormat.Mkv => "MKV 视频",
            FileFormat.WebM => "WebM 视频",
            FileFormat.Wmv => "WMV 视频",
            FileFormat.Mov => "MOV 视频",
            FileFormat.Avi => "AVI 视频",
            FileFormat.Flv => "FLV 视频",
            FileFormat.Pdf => "PDF 文档",
            FileFormat.Docx => "Word 文档",
            FileFormat.Xlsx => "Excel 表格",
            FileFormat.Pptx => "PowerPoint 演示文稿",
            FileFormat.Epub => "EPUB 电子书",
            FileFormat.Mobi => "Mobi 电子书",
            FileFormat.Azw3 => "AZW3 电子书",
            FileFormat.Text => "文本文件",
            FileFormat.Html => "HTML 网页",
            FileFormat.Markdown => "Markdown 文档",
            FileFormat.Csv => "CSV 表格",
            FileFormat.Json => "JSON 数据",
            FileFormat.Xml => "XML 文档",
            FileFormat.Ini => "INI 配置文件",
            FileFormat.Pe => "PE 可执行文件",
            FileFormat.Elf => "ELF 可执行文件",
            FileFormat.Zip => "ZIP 压缩包",
            FileFormat.SevenZip => "7z 压缩包",
            FileFormat.Rar => "RAR 压缩包",
            FileFormat.Tar => "TAR 归档",
            FileFormat.Gz => "GZip 压缩文件",
            FileFormat.Bz2 => "BZip2 压缩文件",
            FileFormat.Xz => "XZ 压缩文件",
            FileFormat.Zstd => "Zstd 压缩文件",
            FileFormat.Iso => "ISO 光盘映像",
            FileFormat.Iso9660 => "ISO 9660 光盘映像",
            FileFormat.Udf => "UDF 光盘映像",
            FileFormat.Sqlite => "SQLite 数据库",
            FileFormat.Dbf => "DBF 数据库",
            FileFormat.Stl => "STL 3D 模型",
            FileFormat.Dxf => "DXF 图纸",
            FileFormat.Step => "STEP 3D 模型",
            FileFormat.Fbx => "FBX 3D 模型",
            FileFormat.Ttf => "TrueType 字体",
            FileFormat.Otf => "OpenType 字体",
            FileFormat.Woff => "WOFF Web 字体",
            FileFormat.Woff2 => "WOFF2 Web 字体",
            FileFormat.Torrent => "BitTorrent 种子",
            FileFormat.Dicom => "DICOM 医学图像",
            FileFormat.Cer => "证书文件",
            FileFormat.Pfx => "PFX 证书",
            FileFormat.Lnk => "Windows 快捷方式",
            FileFormat.Vhd => "VHD 虚拟硬盘",
            FileFormat.Vhdx => "VHDX 虚拟硬盘",
            FileFormat.Vmdk => "VMDK 虚拟磁盘",
            FileFormat.Icl => "ICO 图标库",
            FileFormat.Subtitle => "字幕文件",
            FileFormat.OfficeOpenXml => "Office Open XML",
            FileFormat.OfficeLegacy => "Office 旧格式",
            FileFormat.Odt => "OpenDocument 文本",
            FileFormat.Ods => "OpenDocument 表格",
            FileFormat.Odp => "OpenDocument 演示",
            FileFormat.Rtf => "RTF 富文本",
            FileFormat.DjVu => "DjVu 文档",
            FileFormat.Xps => "XPS 文档",
            FileFormat.Fits => "FITS 天文数据",
            FileFormat.Parquet => "Parquet 列式数据",
            _ => format.ToString(),
        };
    }
}
