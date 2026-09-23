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
}
