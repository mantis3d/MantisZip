using System.Collections.Generic;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 单一数据源：所有压缩选项的可选值列表。
/// SettingsWindow 和 CompressSettingsWindow 共用此数据，避免重复定义和不一致。
/// 选项列表以 WPF 版为准（10 项固实块大小等）。
/// </summary>
public static class CompressionOptionData
{
    /// <summary>支持的压缩格式值。</summary>
    public static readonly string[] ArchiveFormatValues = ["zip", "7z", "tar.gz"];

    public sealed record ComboOption(string Tag, string Display);

    // ─── ZIP ──────────────────────────────────────────────────────────────

    /// <summary>ZIP 文件名编码。</summary>
    public static readonly ComboOption[] ZipEncodings =
    [
        new("utf-8", "UTF-8"),
        new("gbk", "GBK"),
        new("default", ""), // display: localize via CompressOpt_KeepOriginal
    ];

    /// <summary>ZIP 压缩方法。</summary>
    public static readonly ComboOption[] ZipCompressionMethods =
    [
        new("deflate", "Deflate"),
        new("deflate64", "Deflate64"),
        new("bzip2", "BZip2"),
        new("lzma", "LZMA"),
        new("ppmd", "PPMd"),
        new("store", "Store"),
    ];

    /// <summary>ZIP 加密方法。</summary>
    public static readonly ComboOption[] ZipEncryptionMethods =
    [
        new("aes256", "AES-256"),
        new("aes192", "AES-192"),
        new("aes128", "AES-128"),
        new("zipcrypto", "ZipCrypto"),
    ];

    // ─── 7z ───────────────────────────────────────────────────────────────

    /// <summary>7z 压缩方法。</summary>
    public static readonly ComboOption[] SevenZipMethods =
    [
        new("LZMA", "LZMA"),
        new("LZMA2", "LZMA2"),
        new("PPMd", "PPMd"),
        new("BZip2", "BZip2"),
        new("Deflate", "Deflate"),
    ];

    /// <summary>7z 固实块大小（10 项，与 WPF 一致）。</summary>
    public static readonly ComboOption[] SevenZipSolidBlockSizes =
    [
        new("", ""),    // display: localize via CompressOpt_SolidBlockSize_Default
        new("16m", "16MB"),
        new("32m", "32MB"),
        new("64m", "64MB"),
        new("128m", "128MB"),
        new("256m", "256MB"),
        new("512m", "512MB"),
        new("1g", "1GB"),
        new("2g", "2GB"),
        new("4g", "4GB"),
    ];

    /// <summary>7z 字典大小。</summary>
    public static readonly ComboOption[] SevenZipDictionarySizes =
    [
        new("0", ""),               // display: localize via CompressOpt_DictSize_Default
        new("16777216", "16MB"),
        new("33554432", "32MB"),
        new("67108864", "64MB"),
        new("134217728", "128MB"),
        new("268435456", "256MB"),
    ];

    /// <summary>7z 单词大小（FastBytes）。</summary>
    public static readonly ComboOption[] SevenZipNumFastBytes =
    [
        new("0", ""),   // display: localize via CompressOpt_WordSize_Default
        new("32", "32"),
        new("64", "64"),
        new("128", "128"),
        new("255", "255"),
    ];

    /// <summary>7z 匹配器。</summary>
    public static readonly ComboOption[] SevenZipMatchFinders =
    [
        new("", ""),    // display: localize via CompressOpt_MatchFinder_Default
        new("bt2", "BT2"),
        new("bt3", "BT3"),
        new("bt4", "BT4"),
    ];
}
