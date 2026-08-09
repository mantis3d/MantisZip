using System.Text;
using Ude;

namespace MantisZip.Core.Utils;

/// <summary>
/// 文本编码检测工具。使用 Ude 自动检测文件编码并读取文本内容。
/// 支持 GBK、Shift-JIS、Big5、EUC-KR、UTF-8 等数十种编码。
/// 置信度不足时回退为 UTF-8 → 系统默认 ANSI 编码。
/// </summary>
public static class TextEncodingDetector
{
    /// <summary>
    /// 检测文件编码并读取全文。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="systemFallbackCodePage">系统默认 ANSI 代码页（如 936=GBK, 932=Shift-JIS）。传 0 则使用当前系统的 ANSI 代码页。</param>
    /// <returns>读取的文本内容。</returns>
    public static string DetectAndReadText(string filePath, int systemFallbackCodePage = 0)
    {
        // 读取文件头供编码检测（不需要全文）
        byte[] header;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            int len = (int)Math.Min(fs.Length, 4096);
            header = new byte[len];
            fs.ReadExactly(header, 0, len);
        }

        var detector = new CharsetDetector();
        detector.Feed(header, 0, header.Length);
        detector.DataEnd();

        string detected = detector.Charset;
        double confidence = detector.Confidence;

        CoreLog.Trace("DetectAndReadText: detected={0}, confidence={1:P1}", detected, confidence);

        // 置信度 >= 50% 且编码名有效 → 用检测到的编码读取
        if (confidence >= 0.5 && !string.IsNullOrEmpty(detected))
        {
            try
            {
                var enc = Encoding.GetEncoding(detected);
                return File.ReadAllText(filePath, enc);
            }
            catch (Exception ex)
            {
                CoreLog.Trace("DetectAndReadText: detected encoding {0} failed: {1}", detected, ex.Message);
            }
        }

        // 回退：UTF-8 → 系统默认 ANSI 编码
        try
        {
            var utf8 = File.ReadAllText(filePath, Encoding.UTF8);
            if (!utf8.Contains('\uFFFD'))
                return utf8;
            CoreLog.Trace("DetectAndReadText: UTF8 fallback produced replacement chars, trying system default encoding");
        }
        catch (Exception utfEx)
        {
            CoreLog.Trace("DetectAndReadText: UTF8 fallback failed: {0}", utfEx.Message);
        }

        // 使用系统默认 ANSI 编码
        int cp = systemFallbackCodePage > 0 ? systemFallbackCodePage :
            System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        var systemEncoding = Encoding.GetEncoding(cp);
        return File.ReadAllText(filePath, systemEncoding);
    }

    /// <summary>
    /// 从字节数组解码文本（用于内存中的短文本，如 ZIP EOCD 注释）。
    /// 编码探测顺序：UTF-8 BOM → 严格 UTF-8（失败回退）→ 系统默认 ANSI 代码页。
    /// </summary>
    /// <param name="data">原始字节（不包含 BOM 时也正确处理）。</param>
    /// <param name="systemFallbackCodePage">系统默认 ANSI 代码页（如 936=GBK, 932=Shift-JIS）。传 0 使用当前系统的 ANSI 代码页。</param>
    /// <returns>解码后的文本。</returns>
    public static string DecodeText(byte[] data, int systemFallbackCodePage = 0)
    {
        if (data.Length == 0) return string.Empty;

        // 1. UTF-8 BOM → 按 UTF-8 解码（去 BOM）
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return new UTF8Encoding(false).GetString(data, 3, data.Length - 3);

        // 2. 严格 UTF-8：大多数现代工具/标准行为；无效字节序列即非 UTF-8
        try
        {
            return new UTF8Encoding(false, true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            CoreLog.Trace("TextEncodingDetector.DecodeText: not valid UTF-8, falling back to system ANSI codepage");
        }

        // 3. 系统默认 ANSI 编码（中文 Windows = GBK，兼容旧工具写入的注释）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        int cp = systemFallbackCodePage > 0 ? systemFallbackCodePage :
            System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        return Encoding.GetEncoding(cp).GetString(data);
    }
}
