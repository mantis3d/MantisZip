using System.Text;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 验证 DetectTextSubtype 的 JSON/INI 内容启发式识别（非扩展名路径）。
/// 判定标准：正例需能被识别，反例（散文/日志/普通文本）不得误报。
/// </summary>
public class FileFormatDetectorTextSubtypeTests
{
    private static FileFormat DetectText(string content) =>
        FileFormatDetector.Detect(Encoding.UTF8.GetBytes(content), Encoding.UTF8.GetByteCount(content));

    // ── JSON 正例 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("{\"name\": \"test\", \"version\": \"1.0\"}")]
    [InlineData("{\n  \"name\": \"test\",\n  \"count\": 42\n}")]
    [InlineData("[1, 2, 3]")]
    [InlineData("[\"a\", \"b\", \"c\"]")]
    [InlineData("  {\"key\": \"value\"}")]           // 前导空白
    [InlineData("{\"a\": {\"b\": [1, 2]}}")]          // 嵌套
    public void Detect_JsonContent_ReturnsJson(string json)
    {
        Assert.Equal(FileFormat.Json, DetectText(json));
    }

    // ── JSON 反例（不得误报为 JSON）─────────────────────────────────────

    [Theory]
    [InlineData("He went to the store, bought some milk, and came home.")]  // 英文散文
    [InlineData("[This is a note in brackets] with no structure")]          // 方括号散文
    [InlineData("{ not a json object at all")]                              // 花括号开头但无键值对
    [InlineData("Plain text without any special characters.")]
    [InlineData("[Section]\nkey=value")]                                    // INI 特征 → 应判 INI 而非 JSON
    public void Detect_NonJsonContent_DoesNotReturnJson(string text)
    {
        Assert.NotEqual(FileFormat.Json, DetectText(text));
    }

    // ── INI 正例 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("[General]\nname=MantisZip\nversion=1.0")]
    [InlineData("[Section1]\nkey1=value1\n[Section2]\nkey2=value2")]
    [InlineData("[Section]\nkey=value with spaces in value")]       // 值可含空格
    [InlineData("; comment\n[Section]\nkey=value")]                 // 注释行
    [InlineData("[Section]\n\nkey=value")]                          // 空行间隔
    [InlineData("[Section]\nsub.key=value")]                       // key 含点
    public void Detect_IniContent_ReturnsIni(string ini)
    {
        Assert.Equal(FileFormat.Ini, DetectText(ini));
    }

    // ── INI 反例（不得误报为 INI）───────────────────────────────────────

    [Theory]
    [InlineData("[Section] without any key value pairs")]           // 有段头但无 key=value
    [InlineData("key=value without a section header")]               // 有 key=value 但无段头
    [InlineData("[\"a\", \"b\", \"c\"]")]                            // JSON 数组：非段头（含引号）
    [InlineData("key = value with spaces around equals")]            // 段头缺失
    public void Detect_NonIniContent_DoesNotReturnIni(string text)
    {
        Assert.NotEqual(FileFormat.Ini, DetectText(text));
    }

    // ── 回归：既有文本类型不受影响 ──────────────────────────────────────

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><root><item>1</item></root>", FileFormat.Xml)]
    [InlineData("<html><body>Hello</body></html>", FileFormat.Html)]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle cx=\"1\" cy=\"2\" r=\"3\"/></svg>", FileFormat.Svg)]
    [InlineData("plain prose text", FileFormat.Text)]
    public void Detect_ExistingTextSubtypes_Regression(string content, FileFormat expected)
    {
        Assert.Equal(expected, DetectText(content));
    }
}