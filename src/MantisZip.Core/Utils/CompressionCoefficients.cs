using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 经验系数表 — 扩展名 → 压缩分类 → 各格式/级别的预估压缩率。
/// 值 = 压缩后大小 / 原始大小（越小越好压）。
/// </summary>
public static class CompressionCoefficients
{
    // 扩展名 → 分类
    private static readonly Dictionary<string, string> _extensionToCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        // 图片有损（压不动）
        [".jpg"] = "image_lossy",
        [".jpeg"] = "image_lossy",
        [".webp"] = "image_lossy",
        [".jpe"] = "image_lossy",
        [".jfif"] = "image_lossy",

        // 图片无损（可压）
        [".png"] = "image_lossless",
        [".bmp"] = "image_lossless",
        [".gif"] = "image_lossless",
        [".ico"] = "image_lossless",
        [".tga"] = "image_lossless",
        [".hdr"] = "image_lossless",
        [".exr"] = "image_lossless",
        [".svg"] = "image_lossless",

        // 视频（压不动）
        [".mp4"] = "media",
        [".mkv"] = "media",
        [".webm"] = "media",
        [".wmv"] = "media",
        [".mov"] = "media",
        [".avi"] = "media",
        [".flv"] = "media",
        [".m4v"] = "media",
        [".mpg"] = "media",
        [".mpeg"] = "media",
        [".3gp"] = "media",

        // 音频（已压缩）
        [".mp3"] = "media",
        [".wav"] = "media",
        [".flac"] = "media",
        [".ogg"] = "media",
        [".aac"] = "media",
        [".wma"] = "media",
        [".m4a"] = "media",
        [".opus"] = "media",

        // 压缩包（压不动）
        [".zip"] = "archive",
        [".7z"] = "archive",
        [".rar"] = "archive",
        [".tar"] = "archive",
        [".gz"] = "archive",
        [".bz2"] = "archive",
        [".xz"] = "archive",
        [".zst"] = "archive",
        [".iso"] = "archive",
        [".cab"] = "archive",
        [".lz"] = "archive",
        [".lzma"] = "archive",

        // 文本（高压缩比）
        [".txt"] = "text",
        [".log"] = "text",
        [".csv"] = "text",
        [".tsv"] = "text",
        [".md"] = "text",
        [".rst"] = "text",
        [".rtf"] = "text",

        // 代码（高压缩比）
        [".cs"] = "code",
        [".js"] = "code",
        [".ts"] = "code",
        [".py"] = "code",
        [".java"] = "code",
        [".cpp"] = "code",
        [".c"] = "code",
        [".h"] = "code",
        [".html"] = "code",
        [".css"] = "code",
        [".xml"] = "code",
        [".json"] = "code",
        [".yaml"] = "code",
        [".yml"] = "code",
        [".toml"] = "code",
        [".ini"] = "code",
        [".cfg"] = "code",
        [".conf"] = "code",
        [".sh"] = "code",
        [".bat"] = "code",
        [".ps1"] = "code",
        [".rb"] = "code",
        [".go"] = "code",
        [".rs"] = "code",
        [".swift"] = "code",
        [".kt"] = "code",
        [".scala"] = "code",
        [".php"] = "code",
        [".pl"] = "code",
        [".lua"] = "code",
        [".r"] = "code",
        [".sql"] = "code",

        // 二进制（可压）
        [".pdf"] = "binary",
        [".docx"] = "binary",
        [".xlsx"] = "binary",
        [".pptx"] = "binary",
        [".epub"] = "binary",
        [".exe"] = "binary",
        [".dll"] = "binary",
        [".so"] = "binary",
        [".ttf"] = "binary",
        [".otf"] = "binary",
        [".woff"] = "binary",
        [".woff2"] = "binary",
        [".sqlite"] = "binary",
        [".db"] = "binary",
        [".dat"] = "binary",
        [".bin"] = "binary",
        [".msi"] = "binary",
        [".appx"] = "binary",
        [".xap"] = "binary",
    };

    // (分类, 级别) → 压缩率
    // 级别 0 = Store, 1-9 = 对应级别
    private static readonly Dictionary<(string, int), double> _rates = new()
    {
        // Store (level 0) — 所有分类都是 1.0（不压缩）
        [("text", 0)] = 1.0,
        [("code", 0)] = 1.0,
        [("image_lossless", 0)] = 1.0,
        [("image_lossy", 0)] = 1.0,
        [("media", 0)] = 1.0,
        [("binary", 0)] = 1.0,
        [("archive", 0)] = 1.0,

        // Text — 高压缩比
        [("text", 1)] = 0.25,
        [("text", 3)] = 0.18,
        [("text", 5)] = 0.15,
        [("text", 7)] = 0.12,
        [("text", 9)] = 0.10,

        // Code — 高压缩比
        [("code", 1)] = 0.30,
        [("code", 3)] = 0.22,
        [("code", 5)] = 0.18,
        [("code", 7)] = 0.14,
        [("code", 9)] = 0.12,

        // Image lossless — 可压
        [("image_lossless", 1)] = 0.90,
        [("image_lossless", 3)] = 0.87,
        [("image_lossless", 5)] = 0.85,
        [("image_lossless", 7)] = 0.83,
        [("image_lossless", 9)] = 0.82,

        // Image lossy — 压不动
        [("image_lossy", 1)] = 0.99,
        [("image_lossy", 3)] = 0.99,
        [("image_lossy", 5)] = 0.99,
        [("image_lossy", 7)] = 0.99,
        [("image_lossy", 9)] = 0.99,

        // Media — 压不动
        [("media", 1)] = 1.00,
        [("media", 3)] = 1.00,
        [("media", 5)] = 1.00,
        [("media", 7)] = 1.00,
        [("media", 9)] = 1.00,

        // Binary — 中等压缩比
        [("binary", 1)] = 0.70,
        [("binary", 3)] = 0.63,
        [("binary", 5)] = 0.60,
        [("binary", 7)] = 0.57,
        [("binary", 9)] = 0.55,

        // Archive — 压不动
        [("archive", 1)] = 1.00,
        [("archive", 3)] = 1.00,
        [("archive", 5)] = 1.00,
        [("archive", 7)] = 1.00,
        [("archive", 9)] = 1.00,
    };

    // 7z 的系数比 ZIP 更好（更强的压缩算法）
    private static readonly Dictionary<(string, int), double> _sevenZipRates = new()
    {
        [("text", 0)] = 1.0,
        [("text", 1)] = 0.20,
        [("text", 3)] = 0.12,
        [("text", 5)] = 0.08,
        [("text", 7)] = 0.06,
        [("text", 9)] = 0.05,
        [("code", 0)] = 1.0,
        [("code", 1)] = 0.25,
        [("code", 3)] = 0.16,
        [("code", 5)] = 0.12,
        [("code", 7)] = 0.09,
        [("code", 9)] = 0.07,
        [("image_lossless", 0)] = 1.0,
        [("image_lossless", 1)] = 0.88,
        [("image_lossless", 3)] = 0.83,
        [("image_lossless", 5)] = 0.80,
        [("image_lossless", 7)] = 0.78,
        [("image_lossless", 9)] = 0.77,
        [("image_lossy", 0)] = 1.0,
        [("image_lossy", 1)] = 0.99,
        [("image_lossy", 3)] = 0.99,
        [("image_lossy", 5)] = 0.99,
        [("image_lossy", 7)] = 0.99,
        [("image_lossy", 9)] = 0.99,
        [("media", 0)] = 1.0,
        [("media", 1)] = 1.00,
        [("media", 3)] = 1.00,
        [("media", 5)] = 1.00,
        [("media", 7)] = 1.00,
        [("media", 9)] = 1.00,
        [("binary", 0)] = 1.0,
        [("binary", 1)] = 0.55,
        [("binary", 3)] = 0.48,
        [("binary", 5)] = 0.45,
        [("binary", 7)] = 0.42,
        [("binary", 9)] = 0.40,
        [("archive", 0)] = 1.0,
        [("archive", 1)] = 1.00,
        [("archive", 3)] = 1.00,
        [("archive", 5)] = 1.00,
        [("archive", 7)] = 1.00,
        [("archive", 9)] = 1.00,
    };

    /// <summary>根据文件扩展名判定压缩分类。</summary>
    /// <param name="fileName">文件名（含扩展名），如 "photo.jpg"。</param>
    /// <returns>分类字符串：text / code / image_lossless / image_lossy / media / binary / archive。</returns>
    public static string ClassifyByExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return "binary"; // 无扩展名默认为二进制
        return _extensionToCategory.TryGetValue(ext, out var cat) ? cat : "binary";
    }

    /// <summary>
    /// 根据分类、级别和归档格式获取预估压缩率。
    /// </summary>
    /// <param name="category">压缩分类（text / code / image_lossy 等）。</param>
    /// <param name="level">压缩级别（0-9）。</param>
    /// <param name="format">归档格式（Zip / SevenZip 等）。</param>
    /// <returns>预估压缩率（压缩后 / 原始），值越小表示压缩效果越好。</returns>
    public static double GetRate(string category, int level, ArchiveFormat format)
    {
        var rates = format == ArchiveFormat.SevenZip ? _sevenZipRates : _rates;
        // 未定义的级别回退到最近的已知级别
        var effectiveLevel = level switch
        {
            0 => 0,
            <= 2 => 1,
            <= 4 => 3,
            <= 6 => 5,
            <= 8 => 7,
            _ => 9,
        };
        return rates.TryGetValue((category, effectiveLevel), out var rate) ? rate : 0.60;
    }

    /// <summary>
    /// 获取 Store 级别的压缩率（用于自适应降级）。
    /// Store 不压缩，始终返回 1.0。
    /// </summary>
    public static double GetStoreRate(string category) => 1.0; // Store 不压缩
}
