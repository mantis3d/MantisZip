using System.IO;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// 自解压包（SFX）探测：PE 桩之后是否还挂着一个压缩包（issue #36）。
/// </summary>
/// <remarks>
/// 只在「扩展名未命中 + 文件头是 MZ（DOS/PE 桩）」时调用，且只读文件尾部一个窗口：
/// <list type="bullet">
/// <item>**不**全文件扫描（几百 MB 的 exe 会让打开卡住）；</item>
/// <item>**不**解析 PE 节表 / 自己算偏移——真正的偏移交给 7z.dll，它本来就按签名搜索（实测可开）。</item>
/// </list>
/// 本类的唯一职责是回答「这里值得交给 <c>SevenZipEngine</c> 试一把吗」。
/// <para>
/// 注意：**不要**把命中的文件路由给 <c>ZipEngine</c>（SharpCompress）：实测它对 SFX zip 会静默返回
/// 0 个条目而不是报错，用户会看到一个空包，比报错更糟。
/// </para>
/// </remarks>
public static class SfxProbe
{
    /// <summary>尾部扫描窗口大小（足够覆盖 WinRAR / 7-Zip SFX 的桩 + 包起始）。</summary>
    public const int TailWindowBytes = 8 * 1024;

    /// <summary>
    /// 判断 <paramref name="filePath"/> 是否是「MZ 桩 + 内嵌压缩包」的自解压文件。
    /// </summary>
    /// <param name="filePath">待判定文件。</param>
    /// <param name="head">已读到的文件头部字节（调用方已读，避免重复 IO）。</param>
    /// <param name="headLength">头部有效长度。</param>
    /// <param name="container">命中的内嵌容器；未命中为 <see cref="ContainerFormat.Unknown"/>。</param>
    public static bool TryDetectEmbeddedContainer(
        string filePath, byte[] head, int headLength, out ContainerFormat container)
    {
        container = ContainerFormat.Unknown;

        // 只对 MZ 桩尝试：普通二进制 / 无扩展名文件不进这条路（避免为无关文件付 IO）。
        if (string.IsNullOrEmpty(filePath) || head == null || headLength < 2)
            return false;
        if (head[0] != 0x4D || head[1] != 0x5A)
            return false;

        try
        {
            // 1) 先扫已读到的头部：小体积 SFX（几百字节的桩 + 包）整个都落在前 4KB 窗口里，
            //    此时不存在「尾部窗口」，只能在这一块里找。
            if (TryDetectInBuffer(head, headLength, out container))
                return true;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = fs.Length;

            // 2) 大文件再扫尾部窗口：起点不早于已读头部（既省 IO，也避免头部字节被重复扫描）。
            long start = Math.Max(headLength, length - TailWindowBytes);
            if (start >= length)
                return false;

            int windowSize = (int)Math.Min(TailWindowBytes, length - start);
            if (windowSize < 4)
                return false;

            var tail = new byte[windowSize];
            fs.Seek(start, SeekOrigin.Begin);
            int read = fs.Read(tail, 0, windowSize);
            if (read < 4)
                return false;

            return TryDetectInBuffer(tail, read, out container);
        }
        catch (Exception ex)
        {
            CoreLog.Trace($"SfxProbe: failed to inspect {filePath}: {ex.Message}");
            container = ContainerFormat.Unknown;
            return false;
        }
    }

    /// <summary>
    /// 在给定缓冲区中找内嵌容器签名（按最先出现的位置返回）。纯字节操作，便于单测。
    /// </summary>
    /// <remarks>
    /// 只认 4 字节以上的签名：zip 本地文件头 <c>PK\x03\x04</c>、7z、RAR。
    /// 刻意**不认** GZip 的 <c>1F 8B</c>（2 字节，在任意 PE 二进制里几乎必然出现）。
    /// </remarks>
    public static bool TryDetectInBuffer(byte[] tail, int length, out ContainerFormat container)
    {
        container = ContainerFormat.Unknown;
        if (tail == null || length < 4)
            return false;

        int bestPos = int.MaxValue;
        var best = ContainerFormat.Unknown;

        int pos = IndexOf(tail, length, 0x50, 0x4B, 0x03, 0x04);          // zip
        if (pos >= 0 && pos < bestPos) { bestPos = pos; best = ContainerFormat.Zip; }

        pos = IndexOf(tail, length, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C);  // 7z
        if (pos >= 0 && pos < bestPos) { bestPos = pos; best = ContainerFormat.SevenZip; }

        pos = IndexOf(tail, length, 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07);  // Rar!
        if (pos >= 0 && pos < bestPos) { bestPos = pos; best = ContainerFormat.Rar; }

        container = best;
        return best != ContainerFormat.Unknown;
    }

    private static int IndexOf(byte[] buffer, int length, params byte[] pattern)
    {
        int last = length - pattern.Length;
        for (int i = 0; i <= last; i++)
        {
            int j = 0;
            while (j < pattern.Length && buffer[i + j] == pattern[j])
                j++;
            if (j == pattern.Length)
                return i;
        }
        return -1;
    }
}
