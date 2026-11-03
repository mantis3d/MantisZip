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
/// 同时支持 RAR5 与 RAR4：RAR5 的块长度是变长整数、CRC32 覆盖「长度字段 + 块头」；
/// RAR4 是固定 7 字节头（CRC16 + 类型 + 标志 + 长度）、CRC 只取 CRC32 低 16 位、
/// 文件块的数据区不计入 <c>HEAD_SIZE</c>（要按 <c>PACK_SIZE</c> 跳过）。
/// 见 <see cref="ValidateRar4Volume"/> 的说明（issue #68 报告者提供的 RAR4 样本与改法）。
/// 头部加密（RAR5 块类型 4）返回 Unknown，避免把加密包误判成损坏。
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

    /// <summary>RAR4 标记块（7 字节，最后一字节 0x00 表示 v4.x）。</summary>
    private static ReadOnlySpan<byte> Rar4Signature => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00];

    /// <summary>RAR4 固定头长度：HEAD_CRC(2) + HEAD_TYPE(1) + HEAD_FLAGS(2) + HEAD_SIZE(2)。</summary>
    private const int Rar4FixedHeaderLength = 7;

    /// <summary>RAR4 块类型：主块头（每卷开头）。</summary>
    private const byte Rar4MainHeader = 0x73;

    /// <summary>RAR4 块类型：文件块（数据区跟在头后面，长度由 PACK_SIZE 给出）。</summary>
    private const byte Rar4FileHeader = 0x74;

    /// <summary>RAR4 块类型：end-of-archive（每卷收尾块）。</summary>
    private const byte Rar4EndOfArchive = 0x7B;

    /// <summary>RAR4 MAIN_HEAD 标志：本卷是分卷集的首卷。</summary>
    private const ushort Rar4FlagFirstVolume = 0x0100;

    /// <summary>RAR4 ENDARC 标志：后面还有下一卷。</summary>
    private const ushort Rar4FlagNextVolume = 0x0001;

    /// <summary>RAR4 块标志：块后带数据区（LONG_BLOCK，0x8000）——文件块的数据长度首 32 位在紧跟块的 4 字节里。</summary>
    private const ushort Rar4FlagLongBlock = 0x8000;

    /// <summary>RAR4 文件块标志：带 4GB+ 的高 32 位长度字段（LHD_LARGE，0x0100，不是 0x8000）。</summary>
    private const ushort Rar4FlagLargeFile = 0x0100;

    /// <summary>RAR4 文件块中数据长度首 32 位相对 HEAD_TYPE 的偏移（即 ADD_SIZE / PACK_SIZE）。</summary>
    private const int Rar4PackSizeOffset = 5;

    /// <summary>RAR4 文件块中高 32 位长度相对 HEAD_TYPE 的偏移（仅 <see cref="Rar4FlagLargeFile"/>）。</summary>
    private const int Rar4HighPackSizeOffset = 30;

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

            // RAR5 与 RAR4 的块结构与 CRC 覆盖面完全不同，各走一条链
            if (signature[..Rar4Signature.Length].SequenceEqual(Rar4Signature))
                return ValidateRar4Volume(stream, length, volumeCount, index, out failureReason);

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

    /// <summary>
    /// 校验单卷 RAR4 块链：逐块校验头 CRC（CRC32 取低 16 位）、按 PACK_SIZE 跳过文件数据区、
    /// 以 end-of-archive 收尾，并核对「首卷标志」「还有下一卷标志」。
    /// </summary>
    /// <remarks>
    /// 报告者给的复现改法：把 RAR4 <c>ENDARC_HEAD</c> 的 <c>HEAD_SIZE</c> 由 7 改成 6，
    /// WinRAR 报「Headers Error」而 7z.dll 仍说完好。头 CRC 覆盖 <c>HEAD_TYPE..HEAD_SIZE</c>，
    /// 改长度必然让 CRC 不符，因此这条校验正好补上该缺口。
    /// </remarks>
    private static RarStructureStatus ValidateRar4Volume(
        FileStream stream,
        long length,
        int volumeCount,
        int index,
        out string? failureReason)
    {
        failureReason = null;

        long position = Rar4Signature.Length;
        var lastType = (byte)0;
        var lastEndFlags = (ushort)0;
        var declaresFirstVolume = false;
        var sawMainHeader = false;

        while (position < length)
        {
            if (IsZeroPadding(stream, position, length))
                break;

            if (position + Rar4FixedHeaderLength > length)
            {
                failureReason = Describe(index, volumeCount, $"块头截断（偏移 {position}）");
                return RarStructureStatus.Broken;
            }

            var fixedHeader = new byte[Rar4FixedHeaderLength];
            stream.Seek(position, SeekOrigin.Begin);
            if (!ReadExactly(stream, fixedHeader))
            {
                failureReason = Describe(index, volumeCount, $"块头读取失败（偏移 {position}）");
                return RarStructureStatus.Broken;
            }

            var storedCrc = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader);
            var blockType = fixedHeader[2];
            var blockFlags = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(3));
            var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(5));

            if (headerSize < Rar4FixedHeaderLength)
            {
                failureReason = Describe(index, volumeCount, $"块头长度非法（偏移 {position}，长度 {headerSize}）");
                return RarStructureStatus.Broken;
            }

            if (position + headerSize > length)
            {
                failureReason = Describe(index, volumeCount, $"块越界（偏移 {position}，类型 0x{blockType:X2}）");
                return RarStructureStatus.Broken;
            }

            // 头 CRC：CRC32 覆盖 HEAD_TYPE 起的 headerSize - 2 字节，取低 16 位
            var crcCover = new byte[headerSize - 2];
            stream.Seek(position + 2, SeekOrigin.Begin);
            if (!ReadExactly(stream, crcCover))
            {
                failureReason = Describe(index, volumeCount, $"块头 CRC 数据读取失败（偏移 {position}）");
                return RarStructureStatus.Broken;
            }

            if ((ushort)(Crc32(crcCover, 0, crcCover.Length) & 0xFFFF) != storedCrc)
            {
                failureReason = Describe(
                    index,
                    volumeCount,
                    $"块头 CRC 不符（偏移 {position}，类型 0x{blockType:X2}，前一块类型 0x{lastType:X2}）");
                return RarStructureStatus.Broken;
            }

            // 文件块的数据区不计入 HEAD_SIZE：LONG_BLOCK 时紧跟块的 4 字节是数据长度（PACK_SIZE），
            // LHD_LARGE 时再补高 32 位（位于 ATTR 之后、文件名之前）
            long dataSize = 0;
            if (blockType == Rar4FileHeader
                && (blockFlags & Rar4FlagLongBlock) != 0
                && crcCover.Length >= Rar4PackSizeOffset + 4)
            {
                dataSize = BinaryPrimitives.ReadUInt32LittleEndian(crcCover.AsSpan(Rar4PackSizeOffset));
                if ((blockFlags & Rar4FlagLargeFile) != 0
                    && crcCover.Length >= Rar4HighPackSizeOffset + 4)
                {
                    dataSize |= (long)BinaryPrimitives.ReadUInt32LittleEndian(
                        crcCover.AsSpan(Rar4HighPackSizeOffset)) << 32;
                }
            }

            lastType = blockType;
            if (blockType == Rar4MainHeader)
            {
                sawMainHeader = true;
                if ((blockFlags & Rar4FlagFirstVolume) != 0)
                    declaresFirstVolume = true;
            }
            else if (blockType == Rar4EndOfArchive)
            {
                lastEndFlags = blockFlags;
            }

            var next = position + headerSize + dataSize;
            if (next > length)
            {
                failureReason = Describe(
                    index,
                    volumeCount,
                    $"数据区越界（偏移 {position}，类型 0x{blockType:X2}，声明长度 {dataSize}）");
                return RarStructureStatus.Broken;
            }

            position = next;
        }

        if (!sawMainHeader)
        {
            failureReason = Describe(index, volumeCount, "缺少主块头");
            return RarStructureStatus.Broken;
        }

        if (lastType != Rar4EndOfArchive)
        {
            failureReason = Describe(index, volumeCount, $"缺少结束块（最后块类型 0x{lastType:X2}）");
            return RarStructureStatus.Broken;
        }

        // 非末卷必须声明「还有下一卷」，末卷必须声明「到此为止」
        var expectsNext = index < volumeCount - 1;
        var declaresNext = (lastEndFlags & Rar4FlagNextVolume) != 0;
        if (expectsNext != declaresNext)
        {
            failureReason = Describe(
                index,
                volumeCount,
                declaresNext ? "末卷仍声明有下一卷（可能缺卷）" : "非末卷却声明已结束");
            return RarStructureStatus.Broken;
        }

        // 分卷集的首卷必须带「首卷」位，其余卷必须不带（单卷包没有该位是正常的）
        if (volumeCount > 1 && (index == 0) != declaresFirstVolume)
        {
            failureReason = Describe(
                index,
                volumeCount,
                declaresFirstVolume ? "非首卷却声明自己是首卷" : "首卷缺少首卷标志");
            return RarStructureStatus.Broken;
        }

        return RarStructureStatus.Intact;
    }

    private static string Describe(int index, int volumeCount, string detail) =>
        $"第 {index + 1}/{volumeCount} 卷 {detail}";
}
