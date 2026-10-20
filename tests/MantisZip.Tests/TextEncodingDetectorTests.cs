using System.Text;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests;

public class TextEncodingDetectorTests
{
    static TextEncodingDetectorTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static byte[] GbkBytes(string s) => Encoding.GetEncoding("gbk").GetBytes(s);

    [Fact]
    public void DecodeText_ExplicitGbk_DecodesChinese()
    {
        var bytes = GbkBytes("中文内容");
        var result = TextEncodingDetector.DecodeText(bytes, "gbk");
        Assert.Equal("中文内容", result);
    }

    [Fact]
    public void DecodeText_InvalidName_FallsBackToUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("hello");
        var result = TextEncodingDetector.DecodeText(bytes, "not-a-real-encoding");
        Assert.Equal("hello", result); // 无效名不抛异常，走回退链
    }

    [Fact]
    public void DecodeText_NullOrAuto_BehavesLikeDefault()
    {
        var bytes = Encoding.UTF8.GetBytes("auto test");
        Assert.Equal("auto test", TextEncodingDetector.DecodeText(bytes, null));
        Assert.Equal("auto test", TextEncodingDetector.DecodeText(bytes, "auto"));
    }

    [Fact]
    public void DetectAndDecodeText_GbkBytes_ReturnsTextAndEncodingName()
    {
        // 需要足够长的非 ASCII 文本让 Ude 检测置信度达到阈值
        var bytes = GbkBytes("这是一个较长的中文测试字符串，用于验证自动编码检测功能的正确性");
        var (text, name) = TextEncodingDetector.DetectAndDecodeText(bytes);
        Assert.Equal("这是一个较长的中文测试字符串，用于验证自动编码检测功能的正确性", text);
        Assert.NotNull(name);
    }

    [Fact]
    public void DetectAndDecodeText_Empty_ReturnsEmptyWithNullName()
    {
        var (text, name) = TextEncodingDetector.DetectAndDecodeText(Array.Empty<byte>());
        Assert.Equal(string.Empty, text);
        Assert.Null(name);
    }

    // ══════════════════════════════════════════════════════════════════
    // DecodeComment —— ZIP 注释等「无编码标志自由文本」的解码
    // 核心契约：结果不得依赖宿主 locale（en-US 的系统 ANSI = 1252）
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void DecodeComment_GbkBytes_ReturnsChinese()
    {
        var bytes = GbkBytes("欢迎使用本压缩包");
        Assert.Equal("欢迎使用本压缩包", TextEncodingDetector.DecodeComment(bytes));
    }

    [Fact]
    public void DecodeComment_GbkBytes_UnderEnUsCulture_ReturnsChinese()
    {
        // CI runner 与英文版 Windows 的 ANSI 代码页是 1252；
        // 旧实现会回退到 ANSI，把 GBK 字节解成 cp1252 乱码。
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("en-US");

            var bytes = GbkBytes("欢迎使用本压缩包");
            Assert.Equal("欢迎使用本压缩包", TextEncodingDetector.DecodeComment(bytes));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void DecodeComment_Utf8WithBom_ReturnsTextWithoutBom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("UTF-8 注释")).ToArray();
        Assert.Equal("UTF-8 注释", TextEncodingDetector.DecodeComment(bytes));
    }

    [Fact]
    public void DecodeComment_Utf8WithoutBom_ReturnsSame()
    {
        var bytes = Encoding.UTF8.GetBytes("plain ascii comment");
        Assert.Equal("plain ascii comment", TextEncodingDetector.DecodeComment(bytes));
    }

    [Fact]
    public void DecodeComment_Gb18030FourByteSequence_Decodes()
    {
        // GB18030 是 GBK 超集，含 4 字节序列（GBK 无此能力）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = "中文€𝄞";
        var bytes = Encoding.GetEncoding(54936).GetBytes(text);
        Assert.Equal(text, TextEncodingDetector.DecodeComment(bytes));
    }

    [Fact]
    public void DecodeComment_Cp1252Bytes_UnderEnUsCulture_StillDecodesViaAnsi()
    {
        // 安全性护栏：GB18030 优先不能破坏西文注释。
        // "café" 的 cp1252 字节 63 61 66 E9 在 GB18030 下是截断的 2 字节序列 → 产生替换字符
        // → 应退回宿主 ANSI（en-US = 1252）得到正确结果。
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("en-US");

            var bytes = new byte[] { 0x63, 0x61, 0x66, 0xE9 };
            Assert.Equal("café", TextEncodingDetector.DecodeComment(bytes));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
