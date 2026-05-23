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

            // 读取文件头判断 PDF 版本
            string? pdfVersion = null;
            string? title = null, author = null, subject = null;
            int? pageCount = null;
            bool isEncrypted = false;
            DateTime? creationDate = null, modifiedDate = null;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                // 读取前 256KB（元数据字典、Pages 树常在前部）
                int headLen = (int)Math.Min(256 * 1024, fs.Length);
                byte[] headBuf = new byte[headLen];
                fs.ReadExactly(headBuf, 0, headBuf.Length);
                string headText = System.Text.Encoding.ASCII.GetString(headBuf);

                // PDF 版本
                var versionMatch = Regex.Match(headText, @"%PDF-(\d+\.\d+)");
                if (!versionMatch.Success) return null; // 不是 PDF
                pdfVersion = versionMatch.Groups[1].Value;

                // 读取最后 64KB 扫描 trailer 中的 /Info
                long tailSize = Math.Min(65536, fs.Length);
                fs.Seek(-tailSize, SeekOrigin.End);
                byte[] tailBuf = new byte[tailSize];
                fs.ReadExactly(tailBuf, 0, tailBuf.Length);
                string tailText = System.Text.Encoding.ASCII.GetString(tailBuf);

                // 从尾部获取 /Info 字典引用
                string fullText = headText + "\n" + tailText;

                // 扫描 root 附近的 /Info 引用
                ExtractMetadata(fullText, out title, out author, out subject,
                    out creationDate, out modifiedDate, out isEncrypted);

                // 页数需要扫描更多文件内容（Pages 树可能不在头尾）
                pageCount = FindPageCount(fs);
            }

            return new FileFormatInfo
            {
                Format = FileFormat.Pdf,
                DisplayName = "PDF 文档",
                Extension = Path.GetExtension(filePath),
                FileSize = fi.Length,
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
        catch (Exception ex)
        {
            CoreLog.Info($"PdfParser.Parse failed: {ex.Message}");
            return null;
        }
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
    /// 从文件头部和尾部两端分别扫描（各 256KB），覆盖线性化和非线性化 PDF。
    /// 避免大文件全部加载。
    /// </summary>
    private static int? FindPageCount(FileStream fs)
    {
        const int scanSize = 256 * 1024;

        // 从文件头扫描 256KB
        int? count = ScanForPageCount(fs, 0, scanSize);
        if (count.HasValue) return count;

        // 从文件末尾往前扫描 256KB（线性化 PDF 的 Pages 树通常在末尾）
        long tailStart = Math.Max(0, fs.Length - scanSize);
        if (tailStart > 0)
        {
            count = ScanForPageCount(fs, tailStart, scanSize);
            if (count.HasValue) return count;
        }

        return null;
    }

    /// <summary>
    /// 在文件指定位置读取最多 length 字节，查找 /Type /Pages 字典中的 /Count。
    /// </summary>
    private static int? ScanForPageCount(FileStream fs, long position, int length)
    {
        int actualLen = (int)Math.Min(length, fs.Length - position);
        if (actualLen <= 0) return null;

        byte[] buffer = new byte[actualLen];
        fs.Seek(position, SeekOrigin.Begin);
        int bytesRead = fs.Read(buffer, 0, actualLen);
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
            catch { }
        }

        return null;
    }
}
