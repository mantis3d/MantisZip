using System.Buffers.Binary;
using System.IO;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 自解压文件里内嵌压缩包的类别。
/// </summary>
/// <remarks>
/// RAR 必须区分 v4 / v5：7z.dll 的格式标识是两套（<c>Rar4</c> / <c>Rar</c>），
/// 传错会直接报 <c>Invalid archive: open/read error!</c>（2026-10-31 实测，交叉使用一样失败）。
/// </remarks>
public enum SfxPayloadKind
{
    /// <summary>不是自解压文件，或没找到内嵌包。</summary>
    Unknown = 0,

    /// <summary>内嵌 zip。</summary>
    Zip,

    /// <summary>内嵌 7z。</summary>
    SevenZip,

    /// <summary>内嵌 RAR v1.5 ~ 4.x（签名第 7 字节为 0x00）。</summary>
    Rar4,

    /// <summary>内嵌 RAR 5.x（签名第 7 字节为 0x01）。</summary>
    Rar5,
}

/// <summary>
/// 自解压包（SFX）探测：PE 桩之后是否还挂着一个压缩包（issue #36）。
/// </summary>
/// <remarks>
/// 只在「文件头是 MZ（DOS/PE 桩）」时进入扫描，且扫描范围始终有界——不做全文件扫描
/// （几百 MB 的 exe 会让打开卡住）。三段有序扫描，命中即停：
/// <list type="number">
/// <item><b>PE 镜像末尾之后</b> —— 真实 SFX（WinRAR / 7-Zip GUI 生成）的归档紧贴镜像末尾，
/// 间隙通常 0 ~ 几百字节（`Test_7Z_SFX.exe` 187B、`Test_Rar_SFX.exe` 0B）。
/// 这是 v0.5.4 之后「大桩 SFX 打不开」的根因：旧实现只扫文件头 4KB + 文件尾 8KB，
/// 而这两个样本的归档起点分别在 102075 / 329216，完全落在窗外。</item>
/// <item><b>头窗</b> —— 小桩（几百字节）+ 包，整个文件都落在前 16KB 里。</item>
/// <item><b>尾窗</b> —— 归档起点离文件尾不足 8KB（包后追加了签名 / 注释等）。</item>
/// </list>
/// 本类的唯一职责是回答「这里值得交给 <c>SevenZipEngine</c> 试一把吗，该按哪种内嵌格式试」。
/// <para>
/// 注意：**不要**把命中的文件路由给 <c>ZipEngine</c>（SharpCompress）：实测它对 SFX zip 会静默返回
/// 0 个条目而不是报错，用户会看到一个空包，比报错更糟。
/// </para>
/// <para>
/// 探测成本：一次 <see cref="HeaderWindowBytes"/> 读 + 最多 <see cref="ForwardScanBytes"/> 的分块扫描；
/// 非 MZ 文件在读完头部就返回。调用点都是「打开一个归档」这种量级，不值得再加缓存。
/// </para>
/// </remarks>
public static class SfxProbe
{
    /// <summary>头窗大小：DOS 头 + PE 头 + 完整节表都落在这里（e_lfanew 偏大的桩也能覆盖）。</summary>
    public const int HeaderWindowBytes = 16 * 1024;

    /// <summary>尾部扫描窗口大小（归档起点离文件尾不足 8KB 的场景）。</summary>
    public const int TailWindowBytes = 8 * 1024;

    /// <summary>PE 镜像末尾之后最多再看多远——真实 SFX 的间隙是 0~几百字节，1MB 已极宽松。</summary>
    public const int ForwardScanBytes = 1024 * 1024;

    private const int ScanChunkBytes = 64 * 1024;

    /// <summary>最长签名长度（RAR 含版本字节共 8 字节），用于分块扫描时的边界重叠。</summary>
    private const int MaxSignatureLength = 8;

    /// <summary>zip 本地文件头 <c>PK\x03\x04</c>。</summary>
    private static ReadOnlySpan<byte> ZipSignature => [0x50, 0x4B, 0x03, 0x04];

    /// <summary>7z 签名 <c>7z\xBC\xAF\x27\x1C</c>。</summary>
    private static ReadOnlySpan<byte> SevenZipSignature => [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

    /// <summary>RAR 签名 <c>Rar!\x1A\x07</c>（第 7 字节为版本，另行判断）。</summary>
    private static ReadOnlySpan<byte> RarSignature => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];

    /// <summary>
    /// 判断 <paramref name="filePath"/> 是否是「MZ 桩 + 内嵌压缩包」的自解压文件。
    /// </summary>
    /// <param name="filePath">待判定文件。</param>
    /// <param name="head">已读到的文件头部字节（调用方已读，避免重复 IO；可为 null）。</param>
    /// <param name="headLength">头部有效长度。</param>
    /// <param name="container">命中的内嵌容器；未命中为 <see cref="ContainerFormat.Unknown"/>。</param>
    /// <remarks>
    /// 只要「是不是 SFX」的场景用这个；需要 RAR v4/v5 区分（传 <c>InArchiveFormat</c>）的场景
    /// 用 <see cref="TryDetectPayload(string, out SfxPayloadKind)"/>。
    /// </remarks>
    public static bool TryDetectEmbeddedContainer(
        string filePath, byte[] head, int headLength, out ContainerFormat container)
    {
        container = ContainerFormat.Unknown;
        if (!TryDetectPayload(filePath, head, headLength, out var kind))
            return false;
        return TryMapContainer(kind, out container);
    }

    /// <summary>
    /// 判断 <paramref name="filePath"/> 内嵌压缩包的类别。
    /// </summary>
    public static bool TryDetectPayload(string filePath, out SfxPayloadKind kind)
        => TryDetectPayload(filePath, null, 0, out kind);

    /// <summary>
    /// 判断 <paramref name="filePath"/> 内嵌压缩包的类别（可复用调用方已读的头部字节）。
    /// </summary>
    public static bool TryDetectPayload(
        string filePath, byte[]? head, int headLength, out SfxPayloadKind kind)
    {
        kind = SfxPayloadKind.Unknown;

        // 只对 MZ 桩尝试：普通二进制 / 无扩展名文件不进这条路。
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = fs.Length;
            if (length < 8)
                return false;

            int headerSize = (int)Math.Min(length, HeaderWindowBytes);
            var header = new byte[headerSize];
            int copied = head is null ? 0 : Math.Min(headLength, headerSize);
            if (copied > 0)
                Buffer.BlockCopy(head!, 0, header, 0, copied);
            if (copied < headerSize)
                ReadFully(fs, header, copied, headerSize - copied);

            if (headerSize < 2 || header[0] != 0x4D || header[1] != 0x5A)
                return false;

            // 1) PE 镜像末尾之后（真实 SFX 的主路径）
            if (TryGetImageEnd(header, length, out long imageEnd))
            {
                long scanEnd = Math.Min(length, imageEnd + ForwardScanBytes);
                if (ScanForPayload(fs, imageEnd, scanEnd, out kind))
                {
                    CoreLog.Trace($"SfxProbe: {filePath} 在 PE 镜像末尾后命中 {kind}（offset={imageEnd}）");
                    return true;
                }
            }

            // 2) 头窗（小桩 + 包：整个文件都在前 16KB）
            if (TryDetectKindInBuffer(header, out kind))
                return true;

            // 3) 尾窗（归档起点离文件尾不足 8KB）
            long tailStart = Math.Max(headerSize, length - TailWindowBytes);
            if (tailStart < length && ScanForPayload(fs, tailStart, length, out kind))
                return true;

            kind = SfxPayloadKind.Unknown;
            return false;
        }
        catch (Exception ex)
        {
            CoreLog.Trace($"SfxProbe: failed to inspect {filePath}: {ex.Message}");
            kind = SfxPayloadKind.Unknown;
            return false;
        }
    }

    /// <summary>
    /// SFX 内嵌载荷对应的 <see cref="ArchiveFormat"/>；不是 SFX 时返回 <c>null</c>。
    /// </summary>
    public static ArchiveFormat? TryGetPayloadFormat(string filePath)
        => TryDetectPayload(filePath, out var kind) ? MapArchiveFormat(kind) : null;

    /// <summary>
    /// 在给定缓冲区中找内嵌容器签名（按最先出现的位置返回）。纯字节操作，便于单测。
    /// </summary>
    /// <remarks>
    /// 只认 4 字节以上的签名：zip 本地文件头 <c>PK\x03\x04</c>、7z、RAR。
    /// 刻意**不认** GZip 的 <c>1F 8B</c>（2 字节，在任意 PE 二进制里几乎必然出现）。
    /// </remarks>
    public static bool TryDetectInBuffer(byte[] buffer, int length, out ContainerFormat container)
    {
        container = ContainerFormat.Unknown;
        if (buffer is null || length < 4)
            return false;

        int usable = Math.Min(length, buffer.Length);
        if (!TryDetectKindInBuffer(buffer.AsSpan(0, usable), out var kind))
            return false;

        return TryMapContainer(kind, out container);
    }

    /// <summary>
    /// 在给定缓冲区中找内嵌容器签名并区分 RAR 版本（按最先出现的位置返回）。
    /// </summary>
    public static bool TryDetectKindInBuffer(ReadOnlySpan<byte> buffer, out SfxPayloadKind kind)
    {
        kind = SfxPayloadKind.Unknown;
        if (buffer.Length < 4)
            return false;

        int bestPos = int.MaxValue;
        var best = SfxPayloadKind.Unknown;

        int pos = buffer.IndexOf(ZipSignature);
        if (pos >= 0 && pos < bestPos) { bestPos = pos; best = SfxPayloadKind.Zip; }

        pos = buffer.IndexOf(SevenZipSignature);
        if (pos >= 0 && pos < bestPos) { bestPos = pos; best = SfxPayloadKind.SevenZip; }

        pos = buffer.IndexOf(RarSignature);
        if (pos >= 0 && pos < bestPos)
        {
            bestPos = pos;
            // 第 7 字节是版本：0x00 = RAR v1.5 ~ 4.x，0x01 = RAR5（其余值按 v4 处理，保持旧行为）
            int versionIndex = pos + RarSignature.Length;
            best = versionIndex < buffer.Length && buffer[versionIndex] == 0x01
                ? SfxPayloadKind.Rar5
                : SfxPayloadKind.Rar4;
        }

        kind = best;
        return best != SfxPayloadKind.Unknown;
    }

    /// <summary>内嵌载荷类别 → 可执行格式（RAR v4/v5 合并成 <see cref="ArchiveFormat.Rar"/>）。</summary>
    private static ArchiveFormat? MapArchiveFormat(SfxPayloadKind kind) => kind switch
    {
        SfxPayloadKind.Zip => ArchiveFormat.Zip,
        SfxPayloadKind.SevenZip => ArchiveFormat.SevenZip,
        SfxPayloadKind.Rar4 or SfxPayloadKind.Rar5 => ArchiveFormat.Rar,
        _ => null,
    };

    /// <summary>内嵌载荷类别 → 容器格式（工厂路由用）。</summary>
    private static bool TryMapContainer(SfxPayloadKind kind, out ContainerFormat container)
    {
        container = kind switch
        {
            SfxPayloadKind.Zip => ContainerFormat.Zip,
            SfxPayloadKind.SevenZip => ContainerFormat.SevenZip,
            SfxPayloadKind.Rar4 or SfxPayloadKind.Rar5 => ContainerFormat.Rar,
            _ => ContainerFormat.Unknown,
        };
        return container != ContainerFormat.Unknown;
    }

    /// <summary>
    /// 算 PE 镜像末尾（= 覆盖层起点）：各节 <c>PointerToRawData + SizeOfRawData</c> 的最大值。
    /// </summary>
    /// <remarks>
    /// 解析失败（非 PE、节表落在头窗之外、数值越界）返回 false，由调用方退回头窗 / 尾窗扫描——
    /// 宁可少覆盖一种桩结构，也不能拿错误偏移去扫。
    /// </remarks>
    private static bool TryGetImageEnd(ReadOnlySpan<byte> header, long fileLength, out long imageEnd)
    {
        imageEnd = 0;
        if (header.Length < 0x40 || header[0] != 0x4D || header[1] != 0x5A)
            return false;

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(header[0x3C..]);
        if (peOffset < 0x40 || peOffset + 24 > header.Length)
            return false;

        // PE\0\0
        if (header[peOffset] != 0x50 || header[peOffset + 1] != 0x45
            || header[peOffset + 2] != 0x00 || header[peOffset + 3] != 0x00)
            return false;

        int coff = peOffset + 4;
        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(header[(coff + 2)..]);
        int optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(header[(coff + 16)..]);
        if (sectionCount <= 0 || sectionCount > 96)
            return false;

        int sectionTable = coff + 20 + optionalHeaderSize;
        if (sectionTable + sectionCount * 40 > header.Length)
            return false;

        long end = 0;
        for (int i = 0; i < sectionCount; i++)
        {
            int section = sectionTable + i * 40;
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(header[(section + 16)..]);
            uint rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(header[(section + 20)..]);
            if (rawPointer == 0 || rawSize == 0)
                continue;

            long sectionEnd = (long)rawPointer + rawSize;
            if (sectionEnd > end)
                end = sectionEnd;
        }

        if (end <= 0 || end > fileLength)
            return false;

        imageEnd = end;
        return true;
    }

    /// <summary>
    /// 在 [<paramref name="start"/>, <paramref name="end"/>) 区间内分块找内嵌包签名；
    /// 块间保留 <see cref="MaxSignatureLength"/> 字节重叠，避免签名被块边界切断而漏检。
    /// </summary>
    private static bool ScanForPayload(FileStream fs, long start, long end, out SfxPayloadKind kind)
    {
        kind = SfxPayloadKind.Unknown;
        if (end - start < 4)
            return false;

        fs.Seek(start, SeekOrigin.Begin);

        var buffer = new byte[ScanChunkBytes + MaxSignatureLength];
        int carry = 0;      // 上一块尾部保留的字节数
        long pos = start;

        while (pos < end)
        {
            int want = (int)Math.Min(ScanChunkBytes, end - pos);
            int read = ReadFully(fs, buffer, carry, want);
            if (read <= 0)
                break;

            int total = carry + read;
            if (TryDetectKindInBuffer(buffer.AsSpan(0, total), out kind))
                return true;

            carry = Math.Min(MaxSignatureLength, total);
            Buffer.BlockCopy(buffer, total - carry, buffer, 0, carry);
            pos += read;
        }

        kind = SfxPayloadKind.Unknown;
        return false;
    }

    /// <summary>尽量读满 <paramref name="count"/> 字节（FileStream 单次读可能短读），返回实际读到的字节数。</summary>
    private static int ReadFully(FileStream fs, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = fs.Read(buffer, offset + total, count - total);
            if (read <= 0)
                break;
            total += read;
        }
        return total;
    }
}
