using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// UTF-16 文本的保守识别（格式检测与文本解码共用，避免两处各写一套阈值）。
/// </summary>
/// <remarks>
/// <para>
/// 先认 BOM（FF FE / FE FF，权威、零误判）；无 BOM 时只接受「隔字节 NUL」这一种典型形态：
/// 一侧字节几乎全是 0x00、另一侧几乎全是可打印 ASCII / 空白。
/// </para>
/// <para>
/// 不认无 BOM 的 UTF-16 中日韩文本：这类内容两侧都非 0，任何阈值都可能把二进制误判成文本。
/// </para>
/// <para>
/// 存在的原因：文本启发式此前按「NUL 字节 ≤ 1%」判文本，而 UTF-16 的 ASCII 内容
/// （regedit 导出的 <c>.reg</c>、记事本「Unicode」保存的 txt）每个字符后跟一个 0x00，
/// NUL 比例约 50% —— 会被判成二进制，预览落到「暂不支持预览此文件格式」。
/// </para>
/// </remarks>
internal static class Utf16TextHeuristic
{
    /// <summary>零字节位的占比下限（95%）：UTF-16 的 ASCII 内容里这一侧就是全 0。</summary>
    private const int ZeroSlotPercent = 95;

    /// <summary>字符位的可打印占比下限（85%）：留出少量非 ASCII 字符的余量。</summary>
    private const int PrintablePercent = 85;

    /// <summary>
    /// 判断字节是否为 UTF-16 文本。命中时给出编码与正文起始偏移（BOM 已跳过）。
    /// </summary>
    /// <param name="data">字节数组（不要求含 BOM）。</param>
    /// <param name="length">参与判断的有效长度。</param>
    /// <param name="encoding">命中的编码（小端 <see cref="Encoding.Unicode"/> / 大端 <see cref="Encoding.BigEndianUnicode"/>）。</param>
    /// <param name="offset">正文起始偏移：有 BOM 时为 2，无 BOM 时为 0。</param>
    public static bool TryGetEncoding(byte[] data, int length, out Encoding encoding, out int offset)
    {
        encoding = Encoding.Unicode;
        offset = 0;

        if (length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            offset = 2;
            return true;
        }

        if (length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            offset = 2;
            return true;
        }

        // 无 BOM 的判定需要足够样本，避免两三个字节的巧合
        if (length < 8)
            return false;

        if (HasAsciiUtf16Layout(data, length, zeroOnOdd: true))
            return true;

        if (HasAsciiUtf16Layout(data, length, zeroOnOdd: false))
        {
            encoding = Encoding.BigEndianUnicode;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 无 BOM 的「隔字节 NUL」布局：一侧是零字节、另一侧是可打印字符。
    /// </summary>
    /// <param name="zeroOnOdd">true = 零字节位于奇数下标（UTF-16 LE 的 ASCII 形态）。</param>
    private static bool HasAsciiUtf16Layout(byte[] data, int length, bool zeroOnOdd)
    {
        int zeroSlots = 0;
        int zeroBytes = 0;
        int charSlots = 0;
        int printableChars = 0;

        for (int i = 0; i < length; i++)
        {
            bool isZeroSlot = ((i & 1) == 1) == zeroOnOdd;
            if (isZeroSlot)
            {
                zeroSlots++;
                if (data[i] == 0)
                    zeroBytes++;
                continue;
            }

            charSlots++;
            byte b = data[i];
            if ((b >= 0x20 && b <= 0x7E) || b == 9 || b == 10 || b == 12 || b == 13)
                printableChars++;
        }

        return zeroSlots > 0 && charSlots > 0
            && zeroBytes * 100 >= zeroSlots * ZeroSlotPercent
            && printableChars * 100 >= charSlots * PrintablePercent;
    }
}
