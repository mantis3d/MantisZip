using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MantisZip.Core.Utils;

/// <summary>
/// PDF 文件元数据解析器。
/// 通过扫描文件头部和尾部文本提取标题、作者、页数等信息。
/// 不依赖任何第三方 PDF 库。
/// </summary>
public static class PdfParser
{
    /// <summary>
    /// 解析 PDF 文件元数据，返回格式信息；解析失败返回 null。
    /// </summary>
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Length < 8) return null;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            return ParseFromSeekable(fs, fi.Length, Path.GetExtension(filePath));
        }
        catch (Exception ex)
        {
            CoreLog.Info($"PdfParser.Parse failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 解析内存中的 PDF 字节，返回格式信息；解析失败返回 null。
    /// 与 <see cref="Parse(string)"/> 共用同一套扫描规则（<see cref="ParseFromSeekable"/>），
    /// 只是数据源从文件换成字节流——供「内存优先」预览路径使用，不产生磁盘写。
    /// </summary>
    /// <param name="bytes">PDF 完整字节。</param>
    /// <param name="extension">原始条目扩展名（仅用于回填 <see cref="FileFormatInfo.Extension"/>）。</param>
    public static FileFormatInfo? Parse(byte[] bytes, string extension)
    {
        try
        {
            if (bytes.Length < 8) return null;

            // writable: false 的只读视图，ParseFromSeekable 只读不写
            using var ms = new MemoryStream(bytes, writable: false);
            return ParseFromSeekable(ms, bytes.Length, extension);
        }
        catch (Exception ex)
        {
            CoreLog.Info($"PdfParser.Parse(bytes) failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 解析主体：对可 seek 的流做同一套头尾扫描（规则与历史实现逐行一致）。
    /// </summary>
    /// <param name="seekable">可 seek 的数据源（文件流或内存流）。</param>
    /// <param name="knownLength">数据源总长度（调用方已知，免去对 Stream.Length 的依赖）。</param>
    /// <param name="extension">回填用的扩展名。</param>
    private static FileFormatInfo? ParseFromSeekable(Stream seekable, long knownLength, string extension)
    {
        if (knownLength < 8) return null;

        // 读取文件头判断 PDF 版本
        string? pdfVersion = null;
        string? title = null, author = null, subject = null;
        int? pageCount = null;
        bool isEncrypted = false;
        DateTime? creationDate = null, modifiedDate = null;

        // 读取前 256KB（元数据字典、Pages 树常在前部）
        int headLen = (int)Math.Min(256 * 1024, knownLength);
        byte[] headBuf = new byte[headLen];
        seekable.Seek(0, SeekOrigin.Begin);
        seekable.ReadExactly(headBuf, 0, headBuf.Length);
        string headText = System.Text.Encoding.ASCII.GetString(headBuf);

        // PDF 版本
        var versionMatch = Regex.Match(headText, @"%PDF-(\d+\.\d+)");
        if (!versionMatch.Success) return null; // 不是 PDF
        pdfVersion = versionMatch.Groups[1].Value;

        // 读取最后 64KB 扫描 trailer 中的 /Info
        long tailSize = Math.Min(65536, knownLength);
        seekable.Seek(-tailSize, SeekOrigin.End);
        byte[] tailBuf = new byte[tailSize];
        seekable.ReadExactly(tailBuf, 0, tailBuf.Length);
        string tailText = System.Text.Encoding.ASCII.GetString(tailBuf);

        // 从尾部获取 /Info 字典引用
        string fullText = headText + "\n" + tailText;

        // 扫描 root 附近的 /Info 引用
        ExtractMetadata(fullText, out title, out author, out subject,
            out creationDate, out modifiedDate, out isEncrypted);

        // 页数需要扫描更多文件内容（Pages 树可能不在头尾）
        pageCount = FindPageCount(seekable, knownLength);

        return new FileFormatInfo
        {
            Format = FileFormat.Pdf,
            DisplayName = "PDF 文档",
            Extension = extension,
            FileSize = knownLength,
            Title = title,
            Author = author,
            Subject = subject,
            PageCount = pageCount,
            IsEncrypted = isEncrypted,
            CreationDate = creationDate,
            ModifiedDate = modifiedDate,
            AdditionalInfo = $"PDF {pdfVersion}",
        };
    }

    private static void ExtractMetadata(
        string text,
        out string? title, out string? author, out string? subject,
        out DateTime? creationDate, out DateTime? modifiedDate,
        out bool isEncrypted)
    {
        title = null; author = null; subject = null;
        creationDate = null; modifiedDate = null;
        isEncrypted = false;

        try
        {
            // 检测加密 — 扫描 /Encrypt
            isEncrypted = Regex.IsMatch(text, @"/Encrypt\s", RegexOptions.IgnoreCase);

            // 扫描 /Info 字典（通常出现在 trailer 附近）
            var infoMatch = Regex.Match(text,
                @"/Info\s*(<<.*?>>)",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (infoMatch.Success)
            {
                string infoDict = infoMatch.Groups[1].Value;

                title = ExtractPdfString(infoDict, "/Title");
                author = ExtractPdfString(infoDict, "/Author");
                subject = ExtractPdfString(infoDict, "/Subject");
                creationDate = ParsePdfDate(ExtractPdfString(infoDict, "/CreationDate"));
                modifiedDate = ParsePdfDate(ExtractPdfString(infoDict, "/ModDate"));
            }

            // 如果尾部的 /Info 没找到，尝试在整个文本中搜字典条目
            if (title == null)
                title = ExtractPdfString(text, "/Title");
            if (author == null)
                author = ExtractPdfString(text, "/Author");
        }
        catch (Exception ex)
        {
            CoreLog.Info($"PdfParser.ExtractMetadata failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 扫描 /Type /Pages 字典中的 /Count，获取页数。
    /// 从数据源头尾两端分别扫描（各 256KB），覆盖线性化和非线性化 PDF。
    /// 避免大文件全部加载。
    /// </summary>
    private static int? FindPageCount(Stream seekable, long knownLength)
    {
        const int scanSize = 256 * 1024;

        // 从数据源开头扫描 256KB
        int? count = ScanForPageCount(seekable, knownLength, 0, scanSize);
        if (count.HasValue) return count;

        // 从数据源末尾往前扫描 256KB（线性化 PDF 的 Pages 树通常在末尾）
        long tailStart = Math.Max(0, knownLength - scanSize);
        if (tailStart > 0)
        {
            count = ScanForPageCount(seekable, knownLength, tailStart, scanSize);
            if (count.HasValue) return count;
        }

        return null;
    }

    /// <summary>
    /// 在数据源指定位置读取最多 length 字节，查找 /Type /Pages 字典中的 /Count。
    /// </summary>
    private static int? ScanForPageCount(Stream seekable, long knownLength, long position, int length)
    {
        int actualLen = (int)Math.Min(length, knownLength - position);
        if (actualLen <= 0) return null;

        byte[] buffer = new byte[actualLen];
        seekable.Seek(position, SeekOrigin.Begin);
        int bytesRead = seekable.Read(buffer, 0, actualLen);
        if (bytesRead == 0) return null;

        string text = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead);

        // 两步：先定位 /Type /Pages 所在的 << >> 字典块，再提取 /Count
        // 应对 /Count 在 /Type /Pages 之前或之后的任意顺序
        var dictMatch = Regex.Match(text,
            @"<<(?:(?!>>).)*?/Type\s*/Pages(?:(?!>>).)*?>>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (dictMatch.Success)
        {
            var countMatch = Regex.Match(dictMatch.Value, @"/Count\s+(\d+)",
                RegexOptions.IgnoreCase);
            if (countMatch.Success && int.TryParse(countMatch.Groups[1].Value, out int count))
                return count;
        }

        return null;
    }

    /// <summary>
    /// 从 PDF 字典文本中提取指定键的字符串值。
    /// 处理圆括号 ( ) 内的文本。
    /// </summary>
    private static string? ExtractPdfString(string text, string key)
    {
        // 查找 /Key 后跟括号内容
        var match = Regex.Match(text,
            Regex.Escape(key) + @"\s*\(((?:[^()\\]|\\(?:\\|\)|\(|n|r|t|b|f))*?)\)",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string raw = match.Groups[1].Value;
            return DecodePdfString(raw);
        }

        // 备用：/Key 后跟十六进制 <HEX>
        var hexMatch = Regex.Match(text,
            Regex.Escape(key) + @"\s*\<([0-9A-Fa-f]+)\>",
            RegexOptions.IgnoreCase);
        if (hexMatch.Success)
        {
            string hex = hexMatch.Groups[1].Value;
            try
            {
                byte[] bytes = Convert.FromHexString(hex);
                return System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0');
            }
            catch (Exception hexEx) { CoreLog.Info($"PdfParser.DecodeHexString failed: {hexEx.Message}"); }
        }

        return null;
    }

    /// <summary>
    /// 解码 PDF 字符串中的转义符。
    /// </summary>
    private static string DecodePdfString(string raw)
    {
        return raw
            .Replace(@"\n", "\n")
            .Replace(@"\r", "\r")
            .Replace(@"\t", "\t")
            .Replace(@"\(", "(")
            .Replace(@"\)", ")")
            .Replace(@"\\", @"\");
    }

    /// <summary>
    /// 解析 PDF 日期格式（D:YYYYMMDDHHmmSS…）。
    /// </summary>
    private static DateTime? ParsePdfDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;

        // 去除 "D:" 前缀
        if (dateStr.StartsWith("D:"))
            dateStr = dateStr[2..];

        // 取前 14 位数字：YYYYMMDDHHmmSS
        var match = Regex.Match(dateStr, @"^(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})");
        if (!match.Success) return null;

        if (int.TryParse(match.Groups[1].Value, out int y) &&
            int.TryParse(match.Groups[2].Value, out int mo) &&
            int.TryParse(match.Groups[3].Value, out int d) &&
            int.TryParse(match.Groups[4].Value, out int h) &&
            int.TryParse(match.Groups[5].Value, out int mi) &&
            int.TryParse(match.Groups[6].Value, out int s))
        {
            try { return new DateTime(y, mo, d, h, mi, s); }
            catch (Exception ex) { CoreLog.Trace("PdfParser: failed to parse PDF metadata: {0}", ex.Message); }
        }

        return null;
    }
}
