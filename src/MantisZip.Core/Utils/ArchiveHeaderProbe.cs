namespace MantisZip.Core.Utils;

/// <summary>
/// 压缩包头部结构探测结论。
/// </summary>
public enum HeaderStructure
{
    /// <summary>无法判定（非 7z/RAR 格式，或读取失败）——调用方应保持既有行为。</summary>
    Unknown,

    /// <summary>7z/RAR 头部结构通过校验（签名 + CRC + next-header 边界）。</summary>
    Intact,

    /// <summary>7z/RAR 头部结构明确损坏（签名错、StartHeaderCRC 不匹配、next-header 越界）。</summary>
    Broken,
}

/// <summary>
/// 压缩包头部结构探测（方案 B）。
/// <para>
/// <b>动机</b>：SharpSevenZip 对「7z/RAR 加密头无密码」与「7z/RAR 头部损坏」抛出**完全相同**的
/// <c>SharpSevenZipArchiveException</c>（同 HRESULT 0x80131500、同消息
/// "Invalid archive: open/read error! Is it encrypted and a wrong password was provided?..."），
/// 无法靠异常消息/HRESULT 区分。本探测在**异常之外**独立读取文件字节，校验格式头部结构：
/// 结构明确损坏 → 判为损坏（不弹密码框）；结构完好 → 视为可能加密（走密码流程）。
/// </para>
/// <para>
/// <b>范围</b>：仅 7z（StartHeader：签名 + 版本 + StartHeaderCRC + next-header 越界）与
/// RAR（签名 RAR4/RAR5）。ZIP/TAR 由 SharpCompress 抛出语义清晰的异常，无需探测。
/// </para>
/// <para>
/// 纯字节解析，无 7z.dll 依赖，可脱离引擎独立测试。
/// </para>
/// </summary>
public static class ArchiveHeaderProbe
{
    private static readonly byte[] SevenZipMagic = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] Rar4Magic = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00];
    private static readonly byte[] Rar5Magic = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];

    /// <summary>next-header 参与 CRC 校验的字节上限；超过则跳过该校验（超大头部不值得为探测读一遍）。</summary>
    private const int MaxProbeHeaderSize = 4 * 1024 * 1024;

    /// <summary>
    /// 探测指定文件的头部结构。
    /// </summary>
    /// <param name="filePath">压缩包路径。</param>
    public static HeaderStructure Probe(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return HeaderStructure.Unknown;

        try
        {
            using var fs = File.OpenRead(filePath);
            Span<byte> head = stackalloc byte[8];
            var n = fs.Read(head);

            // 魔数优先分派（分卷 .001 亦靠魔数识别真实格式，不依赖扩展名）
            if (n >= 6 && head[..6].SequenceEqual(SevenZipMagic))
                return Inspect7z(filePath);

            if (n >= 8 && head[..8].SequenceEqual(Rar5Magic))
                return HeaderStructure.Intact;
            if (n >= 7 && head[..7].SequenceEqual(Rar4Magic))
                return HeaderStructure.Intact;

            // 魔数不匹配：若扩展名声称是 7z/RAR，则签名已损坏（如 RAR 签名首字节被翻转）
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".7z" or ".rar")
                return HeaderStructure.Broken;

            return HeaderStructure.Unknown;
        }
        catch (IOException)
        {
            return HeaderStructure.Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return HeaderStructure.Unknown;
        }
    }

    /// <summary>
    /// 7z StartHeader（首 32 字节，未加密）校验：
    /// 签名已在上层确认；此处校验版本主号、StartHeaderCRC、next-header 是否越界。
    /// </summary>
    private static HeaderStructure Inspect7z(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        var h = new byte[32];
        if (fs.Read(h, 0, 32) < 32)
            return HeaderStructure.Broken; // 连 32 字节签名头都不足 → 截断

        // 版本主号：7-Zip 一律写 0x00（伪造/损坏文件此处常为随机字节）
        if (h[6] != 0x00)
            return HeaderStructure.Broken;

        // StartHeaderCRC = CRC32(bytes[12..32))，与 offset 8 的存储值比对
        var calc = Crc32(h, 12, 20);
        var stored = BitConverter.ToUInt32(h, 8);
        if (calc != stored)
            return HeaderStructure.Broken;

        // next-header 必须落在有效长度内（分卷集按全部兄弟卷求和）
        ulong offset = BitConverter.ToUInt64(h, 12);
        ulong size = BitConverter.ToUInt64(h, 20);
        ulong end = 32UL + offset + size;
        if (end > EffectiveVolumeLength(filePath))
            return HeaderStructure.Broken;

        // next-header 自身的 CRC（stored = h[28..32)）：上面只校验了 StartHeader（前 32 字节）与越界，
        // 头所在卷的内容被改动而长度不变时仍会判 Intact —— 7z.dll 对这种包抛的正是
        // 「Is it encrypted and a wrong password was provided?」，于是损坏包被当成加密包弹密码框（issue #69）。
        // CRC 覆盖的是**存储态**字节（加密头的密文同样参与），故加密包仍会通过。
        if (size > 0 && size <= MaxProbeHeaderSize)
        {
            if (!TryReadVirtualRange(filePath, 32UL + offset, (int)size, out var headerBytes))
                return HeaderStructure.Broken; // 头区读不全 = 卷缺失或截断

            if (Crc32(headerBytes, 0, headerBytes.Length) != BitConverter.ToUInt32(h, 28))
                return HeaderStructure.Broken;
        }

        return HeaderStructure.Intact;
    }

    /// <summary>
    /// 按**虚拟偏移**跨卷读取：分卷集被当作一条连续字节流，<paramref name="start"/> 可大于单卷长度。
    /// </summary>
    private static bool TryReadVirtualRange(string filePath, ulong start, int count, out byte[] buffer)
    {
        buffer = new byte[count];
        var volumes = ArchiveVolumeSet.EnumerateVolumes(filePath);
        if (volumes.Count == 0)
            return false;

        var written = 0;
        var virtualStart = start;
        foreach (var volume in volumes)
        {
            var length = (ulong)new FileInfo(volume).Length;
            if (virtualStart >= length)
            {
                virtualStart -= length;
                continue;
            }

            using var fs = File.OpenRead(volume);
            fs.Seek((long)virtualStart, SeekOrigin.Begin);
            var want = (int)Math.Min((ulong)(count - written), length - virtualStart);
            var read = 0;
            while (read < want)
            {
                var n = fs.Read(buffer, written + read, want - read);
                if (n <= 0)
                    break;
                read += n;
            }

            written += read;
            virtualStart = 0;
            if (written == count)
                return true;
        }

        return written == count;
    }

    /// <summary>
    /// 分卷集有效长度：单卷 = 文件自身；<c>base.001</c> 起的分卷 = 同基名全部兄弟卷之和。
    /// 缺卷时（如仅存首卷）返回已存在卷之和，使越界判定成立。
    /// </summary>
    private static ulong EffectiveVolumeLength(string filePath)
    {
        var selfLength = (ulong)new FileInfo(filePath).Length;
        if (!ArchiveVolumeSet.HasVolumeSuffix(filePath))
            return selfLength;

        ulong total = 0;
        foreach (var volume in ArchiveVolumeSet.EnumerateVolumes(filePath))
            total += (ulong)new FileInfo(volume).Length;

        return total > 0 ? total : selfLength;
    }

    /// <summary>标准 CRC32（反射多项式 0xEDB88320，无查表）。</summary>
    private static uint Crc32(byte[] data, int offset, int count)
    {
        uint crc = 0xFFFFFFFF;
        for (var i = offset; i < offset + count; i++)
        {
            crc ^= data[i];
            for (var b = 0; b < 8; b++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
