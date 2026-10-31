using System.Buffers.Binary;

namespace MantisZip.Core.Utils;

/// <summary>RAR 头部结构探针的判定结果。</summary>
public enum RarStructureStatus
{
    /// <summary>无法判定（非 RAR5、头部加密、读取失败）——调用方保持原有行为。</summary>
    Unknown = 0,

    /// <summary>块链完整：头 CRC 全部相符、卷间链接与前缀一致、以 end-of-archive 收尾。</summary>
    Intact,

    /// <summary>结构损坏：头 CRC 不符、块越界、缺结束块或卷数不连续。</summary>
    Broken,
}

/// <summary>
/// RAR5 块链结构校验（issue #69 在 RAR 上的残留）——7z.dll 的完整性检查补不上的那一块。
/// </summary>
/// <remarks>
/// <para>
/// 现象（真分卷实测）：WinRAR 制作的 <c>.partN.rar</c> 分卷集，**末卷尾部**（end-of-archive
/// 块的头 CRC）被改坏时，<c>SharpSevenZipExtractor.Check()</c> 仍返回 <c>true</c>，
/// 而 WinRAR 自己的 <c>UnRAR.exe t</c> 与 7-Zip 命令行都报「Headers Error」。
/// 原因是 7z.dll 只校验数据区与块头本身，不校验卷尾收尾块。
/// </para>
/// <para>
/// 这里按 RAR5 规范独立走一遍块链：每个块的 <c>CRC32</c> 覆盖「块长度字段 + 块头」，
/// 数据区按 <c>DataSize</c> 跳过；每个卷必须以 <c>End of archive</c> 块收尾，
/// 其 <c>EndFlags</c> 的 bit0 表示「还有下一卷」——非末卷必须为 1、末卷必须为 0，
/// 于是「缺末卷」「中间卷截断」也会被判坏。
/// </para>
/// <para>
/// 只支持 RAR5（签名 <c>52 61 72 21 1A 07 01 00</c>）。RAR4 的块结构不同且仓库语料全是 RAR4，
/// 本轮不实现，遇到即返回 <see cref="RarStructureStatus.Unknown"/>（已登记 KNOWN_ISSUES）。
/// 头部加密（块类型 4）同样返回 Unknown，避免把加密包误判成损坏。
/// </para>
/// <para>
/// 只挂在「测试压缩包」（<see cref="Engines.SevenZipEngine.TestArchiveAsync"/>）上，
/// **不挂**打开路径（<see cref="ArchiveHeaderProbe"/>）：结构判定有误时报「文件损坏」比
/// 少报一次更糟，打开路径保持既有语义。
/// </para>
/// </remarks>
public static class RarHeaderValidator
{
    /// <summary>RAR5 签名（8 字节，最后一字节 0x00 表示 v5）。</summary>
    private static ReadOnlySpan<byte> Rar5Signature => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];

    /// <summary>块类型：头部加密（后续块头全部加密，无法继续解析）。</summary>
    private const ulong HeaderTypeEncryption = 4;

    /// <summary>块类型：end-of-archive（每卷收尾块）。</summary>
    private const ulong HeaderTypeEndOfArchive = 5;

    /// <summary>块头大小上限：超过即视为损坏（防止损坏数据导致巨量分配）。</summary>
    private const long MaxHeaderSize = 16 * 1024 * 1024;

    /// <summary>非末卷尾部允许的零填充长度（WinRAR 会把非末卷补齐到固定卷大小）。</summary>
    private const int ZeroPaddingLimit = 1024;

    /// <summary>块头前缀长度：足够容纳长度字段 + 公共头（类型/标志/大小字段）。</summary>
    private const int HeaderPrefixLength = 64;

    /// <summary>校验整组卷的 RAR5 块链结构。</summary>
    /// <param name="archivePath">任意一卷（内部会从第一卷开始枚举）；非 RAR5 返回 <see cref="RarStructureStatus.Unknown"/>。</param>
    public static RarStructureStatus Validate(string archivePath) => Validate(archivePath, out _);

    /// <summary>校验整组卷的 RAR5 块链结构，并给出可读原因（<see cref="RarStructureStatus.Broken"/> 时非空）。</summary>
    public static RarStructureStatus Validate(string archivePath, out string? failureReason)
    {
        failureReason = null;
        if (string.IsNullOrEmpty(archivePath))
            return RarStructureStatus.Unknown;

        IReadOnlyList<string> volumes;
        try
        {
            volumes = ArchiveVolumeSet.EnumerateVolumes(archivePath);
        }
        catch (Exception ex)
        {
            CoreLog.Trace("RarHeaderValidator: 枚举分卷失败 {0}: {1}", archivePath, ex.Message);
            return RarStructureStatus.Unknown;
        }

        if (volumes.Count == 0)
            return RarStructureStatus.Unknown;

        for (var index = 0; index < volumes.Count; index++)
        {
            var status = ValidateVolume(volumes[index], volumes.Count, index, out failureReason);
            switch (status)
            {
                case RarStructureStatus.Broken:
                    CoreLog.Error($"RarHeaderValidator: {failureReason} ({volumes[index]})");
                    return RarStructureStatus.Broken;

                case RarStructureStatus.Unknown:
                    // 非 RAR5 / 头部加密：不做判定，保持既有行为
                    failureReason = null;
                    return RarStructureStatus.Unknown;
            }
        }

        return RarStructureStatus.Intact;
    }

    /// <summary>校验单卷块链（内部按卷位置判断「是否应该还有下一卷」）。</summary>
    private static RarStructureStatus ValidateVolume(
        string volumePath,
        int volumeCount,
        int index,
        out string? failureReason)
    {
        failureReason = null;

        try
        {
            using var stream = new FileStream(
                volumePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            var length = stream.Length;
            if (length < Rar5Signature.Length)
                return RarStructureStatus.Unknown;

            Span<byte> signature = stackalloc byte[Rar5Signature.Length];
            if (!ReadExactly(stream, signature))
                return RarStructureStatus.Unknown;

            // 只处理 RAR5；RAR4 的块结构与 CRC 覆盖面完全不同（见类注释）
            if (!signature.SequenceEqual(Rar5Signature))
                return RarStructureStatus.Unknown;

            long position = Rar5Signature.Length;
            ulong lastType = 0;
            ulong lastEndFlags = 0;

            while (position < length)
            {
                if (IsZeroPadding(stream, position, length))
                    break;
                if (position + 5 > length)
                {
                    failureReason = Describe(index, volumeCount, $"块头截断（偏移 {position}）");
                    return RarStructureStatus.Broken;
                }

                var crcBytes = new byte[4];
                stream.Seek(position, SeekOrigin.Begin);
                if (!ReadExactly(stream, crcBytes))
                {
                    failureReason = Describe(index, volumeCount, $"块 CRC 读取失败（偏移 {position}）");
                    return RarStructureStatus.Broken;
                }

                var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(crcBytes);

                var blockStatus = ReadBlockHeader(stream, position + 4, length, out var block, out var headerSize);
                if (blockStatus != RarStructureStatus.Intact)
                {
                    failureReason = Describe(index, volumeCount, $"块头不完整（偏移 {position}）");
                    return RarStructureStatus.Broken;
                }

                if (Crc32(block, 0, block.Length) != storedCrc)
                {
                    failureReason = Describe(
                        index,
                        volumeCount,
                        $"块头 CRC 不符（偏移 {position}，前一块类型 {lastType}）");
                    return RarStructureStatus.Broken;
                }

                // 公共头：HeaderType(vint) + HeaderFlags(vint)，flags 决定后面是否还有 ExtraAreaSize / DataSize
                var cursor = headerSize.LengthLength;
                if (!TryReadVint(block, ref cursor, out var headerType)
                    || !TryReadVint(block, ref cursor, out var headerFlags))
                {
                    failureReason = Describe(index, volumeCount, $"块类型字段不完整（偏移 {position}）");
                    return RarStructureStatus.Broken;
                }

                ulong dataSize = 0;
                if ((headerFlags & 0x0001) != 0)
                {
                    // ExtraAreaSize：跳过即可（恢复记录等附加区）
                    if (!TryReadVint(block, ref cursor, out _))
                    {
                        failureReason = Describe(index, volumeCount, $"附加区长度字段不完整（偏移 {position}）");
                        return RarStructureStatus.Broken;
                    }
                }

                if ((headerFlags & 0x0002) != 0
                    && !TryReadVint(block, ref cursor, out dataSize))
                {
                    failureReason = Describe(index, volumeCount, $"数据区长度字段不完整（偏移 {position}）");
                    return RarStructureStatus.Broken;
                }

                if (headerType == HeaderTypeEncryption)
                {
                    // 头部已加密：后续块头无法解出，交回调用方按「未知」处理（密码流程负责）
                    return RarStructureStatus.Unknown;
                }

                lastType = headerType;
                lastEndFlags = 0;
                if (headerType == HeaderTypeEndOfArchive
                    && cursor < block.Length
                    && TryReadVint(block, ref cursor, out var endFlags))
                {
                    lastEndFlags = endFlags;
                }

                var next = position + 4 + block.Length + (long)dataSize;
                if (next > length)
                {
                    failureReason = Describe(index, volumeCount, $"块越界（偏移 {position}，类型 {headerType}）");
                    return RarStructureStatus.Broken;
                }

                position = next;
            }

            if (lastType != HeaderTypeEndOfArchive)
            {
                failureReason = Describe(index, volumeCount, $"缺少结束块（最后块类型 {lastType}）");
                return RarStructureStatus.Broken;
            }

            // 非末卷必须声明「还有下一卷」（bit0 = 1），末卷必须声明「到此为止」
            var expectsNext = index < volumeCount - 1;
            var declaresNext = (lastEndFlags & 0x1) != 0;
            if (expectsNext != declaresNext)
            {
                failureReason = Describe(
                    index,
                    volumeCount,
                    declaresNext ? "末卷仍声明有下一卷（可能缺卷）" : "非末卷却声明已结束");
                return RarStructureStatus.Broken;
            }

            return RarStructureStatus.Intact;
        }
        catch (Exception ex)
        {
            // 读不动（共享冲突 / 权限）不判损坏，交给上层既有错误路径
            CoreLog.Trace("RarHeaderValidator: 读取失败 {0}: {1}", volumePath, ex.Message);
            return RarStructureStatus.Unknown;
        }
    }

    /// <summary>读一个块头：长度字段 + 块头本体；返回 <see cref="RarStructureStatus.Broken"/> 表示不完整。</summary>
    private static RarStructureStatus ReadBlockHeader(
        FileStream stream,
        long offset,
        long fileLength,
        out byte[] block,
        out (int LengthLength, int HeaderSize) headerSize)
    {
        block = [];
        headerSize = (0, 0);

        var prefix = new byte[HeaderPrefixLength];
        stream.Seek(offset, SeekOrigin.Begin);
        var read = ReadUpTo(stream, prefix, 0, prefix.Length);

        long parsed = 0;
        var shift = 0;
        var lengthLength = 0;
        while (lengthLength < read)
        {
            var b = prefix[lengthLength++];
            parsed |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                break;
            shift += 7;
            if (shift >= 63)
                return RarStructureStatus.Broken;
        }

        if (lengthLength == 0 || (prefix[lengthLength - 1] & 0x80) != 0 || parsed <= 0 || parsed > MaxHeaderSize)
            return RarStructureStatus.Broken;

        var total = lengthLength + (int)parsed;
        if (offset + total > fileLength)
            return RarStructureStatus.Broken;

        block = new byte[total];
        var copied = Math.Min(read, total);
        Array.Copy(prefix, block, copied);
        if (copied < total)
        {
            stream.Seek(offset + copied, SeekOrigin.Begin);
            if (!ReadExactly(stream, block.AsSpan(copied, total - copied)))
                return RarStructureStatus.Broken;
        }

        headerSize = (lengthLength, (int)parsed);
        return RarStructureStatus.Intact;
    }

    /// <summary>从当前位置起是否只剩零填充（非末卷会被 WinRAR 补齐到固定卷大小）。</summary>
    private static bool IsZeroPadding(FileStream stream, long position, long length)
    {
        var remaining = length - position;
        if (remaining > ZeroPaddingLimit)
            return false;

        var buffer = new byte[remaining];
        stream.Seek(position, SeekOrigin.Begin);
        var read = ReadUpTo(stream, buffer, 0, buffer.Length);
        if (read != buffer.Length)
            return false;

        foreach (var b in buffer)
        {
            if (b != 0)
                return false;
        }

        return true;
    }

    private static bool TryReadVint(byte[] buffer, ref int cursor, out ulong value)
    {
        value = 0;
        var shift = 0;
        while (cursor < buffer.Length)
        {
            var b = buffer[cursor++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return true;
            shift += 7;
            if (shift >= 63)
                return false;
        }

        return false;
    }

    private static int ReadUpTo(Stream stream, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, offset + total, count - total);
            if (read <= 0)
                break;
            total += read;
        }

        return total;
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read <= 0)
                return false;
            total += read;
        }

        return true;
    }

    private static uint Crc32(byte[] data, int offset, int count)
    {
        uint crc = 0xFFFFFFFFu;
        for (var i = offset; i < offset + count; i++)
        {
            crc ^= data[i];
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }

    private static string Describe(int index, int volumeCount, string detail) =>
        $"第 {index + 1}/{volumeCount} 卷 {detail}";
}
