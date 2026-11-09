using System.Text;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// UTF-16 文本识别回归测试。
/// <para>
/// 事故：regedit 导出的 <c>.reg</c> 是 UTF-16 LE + BOM，每个 ASCII 字符后跟一个 0x00，
/// NUL 比例约 50% —— 旧的「NUL ≤ 1%」启发式把它判成二进制，预览提示「暂不支持预览此文件格式」。
/// </para>
/// <para>
/// 判定标准：BOM 与「隔字节 NUL」形态必须识别为文本；全零内容不得误判为文本。
/// </para>
/// </summary>
public class FileFormatDetectorUtf16Tests
{
    /// <summary>典型 regedit 导出内容（两个段头，结构上像 INI，但必须报 Text）。</summary>
    private const string RegContent =
        "Windows Registry Editor Version 5.00\r\n" +
        "\r\n" +
        "[HKEY_CURRENT_USER\\Software\\MantisZipTest]\r\n" +
        "\"Value1\"=\"Data\"\r\n" +
        "\r\n" +
        "[HKEY_CURRENT_USER\\Software\\MantisZipTest\\Sub]\r\n" +
        "\"Value2\"=\"More\"\r\n";

    private static FileFormat Detect(byte[] bytes) => FileFormatDetector.Detect(bytes, bytes.Length);

    private static byte[] Utf16LeWithBom(string s) =>
        new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(s)).ToArray();

    private static byte[] Utf16BeWithBom(string s) =>
        new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes(s)).ToArray();

    // ── 正例：UTF-16 文本 ──────────────────────────────────────────────

    [Fact]
    public void Detect_Utf16LeRegWithBom_ReturnsText()
    {
        Assert.Equal(FileFormat.Text, Detect(Utf16LeWithBom(RegContent)));
    }

    [Fact]
    public void Detect_Utf16BeRegWithBom_ReturnsText()
    {
        Assert.Equal(FileFormat.Text, Detect(Utf16BeWithBom(RegContent)));
    }

    [Fact]
    public void Detect_Utf16LeWithoutBom_ReturnsText()
    {
        // 无 BOM 的 UTF-16 LE（部分工具会这么写）：隔字节 NUL 启发式
        var bytes = Encoding.Unicode.GetBytes("plain ascii text saved as utf-16 without bom");
        Assert.Equal(FileFormat.Text, Detect(bytes));
    }

    [Fact]
    public void Detect_Utf16LeWithoutBom_JsonContent_ReturnsJson()
    {
        // 子类型识别也必须先按 UTF-16 解码，否则读成 "{\0\"\0…" 匹配不上 JSON 特征
        var bytes = Encoding.Unicode.GetBytes("{\"name\": \"mantiszip\", \"count\": 42}");
        Assert.Equal(FileFormat.Json, Detect(bytes));
    }

    [Fact]
    public void Detect_AnsiRegedit4_ReturnsText()
    {
        // ANSI 版导出：首行 REGEDIT4，同样有 [Section]，不得因像 INI 而报 Ini
        var bytes = Encoding.ASCII.GetBytes(
            "REGEDIT4\r\n\r\n[HKEY_CURRENT_USER\\Software\\A]\r\n\"V\"=\"1\"\r\n\r\n" +
            "[HKEY_CURRENT_USER\\Software\\B]\r\n\"V\"=\"2\"\r\n");
        Assert.Equal(FileFormat.Text, Detect(bytes));
    }

    // ── 反例：不得把二进制当文本 ────────────────────────────────────────

    [Fact]
    public void Detect_AllZeroBytes_ReturnsUnknown()
    {
        // 全零：零字节位 100% 但字符位没有可打印字符 → 不认 UTF-16，NUL 比例也超限
        Assert.Equal(FileFormat.Unknown, Detect(new byte[64]));
    }

    [Fact]
    public void Detect_NulBlockFollowedByAscii_ReturnsUnknown()
    {
        // 30 个 NUL + 34 个 'A'：NUL 占 46%，但不是「隔字节」形态 → 不得误判为 UTF-16
        var bytes = Enumerable.Repeat((byte)0x00, 30)
            .Concat(Enumerable.Repeat((byte)0x41, 34)).ToArray();
        Assert.Equal(FileFormat.Unknown, Detect(bytes));
    }

    // ── 扩展名兜底 ────────────────────────────────────────────────────

    [Theory]
    [InlineData(".reg")]
    [InlineData(".REG")]
    public void DetectByExtension_Reg_ReturnsText(string extension)
    {
        Assert.Equal(FileFormat.Text, FileFormatDetector.DetectByExtension(extension));
    }
}
