using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Common;

namespace MantisZip.Core.Utils;

/// <summary>
/// WinZip AES 的「就地换密钥」—— issue #92 的修复手段。
///
/// <para>
/// 为什么需要它：7z.dll 写 ZIP + AES 时要把密码从 UTF-16 转成 ANSI 交给加密回调，
/// 只要密码含任意 &gt;127 的字符，转换就失败并抛
/// <c>SharpSevenZipException: Invalid arguments provided. (0x80070057: E_INVALIDARG)</c>，
/// 于是「ZIP + AES + 中文密码」根本压不出包来。SharpSevenZip 的所有 Compress* 方法
/// 都只收 <c>string</c> 密码，没有密码字节入口，绕不开这一层。
/// </para>
///
/// <para>
/// 做法：先用一个<strong>纯 ASCII 占位密码</strong>让 7z.dll 正常压出包，
/// 随后在字节层面把每个 AES 条目的密钥换成真实密码 —— 不需要解压。
/// 依据是 WinZip AES 的加密是 <strong>纯 CTR keystream</strong>：
/// <c>密文 = 明文 XOR KS(key)</c>，所以换密钥只需
/// <c>密文' = 密文 XOR KS(old) XOR KS(new)</c>，两遍 AES-CTR、XOR 合成，
/// 与内层压缩方法（Store / Deflate / Deflate64 / BZip2 / LZMA / PPMd）、
/// 条目数、明文内容完全无关，且<strong>密文长度不变</strong>，可以原地改写。
/// </para>
///
/// <para>
/// 每个条目要改的只有 4 段（都在数据区，不动目录）：
/// <c>salt</c> → <c>password verifier</c> → <c>密文</c> → <c>10 字节 HMAC-SHA1</c>。
/// 条目名、CRC、压缩大小、本地头/中央目录偏移<strong>全部不变</strong>。
/// </para>
/// </summary>
internal static class ZipWinZipAesRekey
{
    /// <summary>
    /// 交给 7z.dll 的占位密码。必须是纯 ASCII（否则又回到 E_INVALIDARG），
    /// 长度无所谓（PBKDF2 不关心），但要有足够熵以免与用户密码字节撞车。
    /// 它只存在于内存与临时压缩过程中，换密钥后不会留在包里。
    /// </summary>
    internal const string PlaceholderPassword = "MantisZip-AES-Placeholder-DoNotUse";

    /// <summary>
    /// 真实密码是否需要「占位压缩 + 换密钥」这条迂回路径。
    /// 纯可打印 ASCII（0–127）7z.dll 能自己处理，直接传给它即可。
    /// </summary>
    internal static bool NeedsRekey(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        foreach (var c in password)
        {
            if (c > 127)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 换密钥时写入包的密码字节。
    ///
    /// <para>
    /// <strong>优先系统 ANSI 代码页</strong>（中文 Windows 上是 GBK）—— 这是实测出来的唯一
    /// 能让 7-Zip 同时打开的编码：7z.dll 自己写 ZIP+AES 时就是把密码转成 ANSI 交给加密回调
    /// （issue #91 已实测：GBK 密码的包 7-Zip 能开，UTF-8 密码的包 7-Zip 报 Wrong password）。
    /// 用同一口径写，用户拿到的包既能被本程序解开，也能被 7-Zip 解开。
    /// </para>
    /// <para>
    /// 该编码无法表示密码时（如系统 ANSI 是 GBK 却输入了 emoji）回退 UTF-8：
    /// 读取侧 <see cref="ZipWinZipAes.CandidatePasswordBytes"/> 的候选顺序是
    /// UTF-8 → 系统 ANSI → GBK → GB18030，两种写法都能被自己认回来。
    /// </para>
    /// </summary>
    internal static byte[] NewPasswordBytes(string password)
    {
        try
        {
            return NameEncodingCatalog.ResolveSystemEncoding().GetBytes(password);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetBytes(password);
        }
    }

    /// <summary>
    /// 把 <paramref name="archivePath"/> 里所有 WinZip AES 条目的密钥从
    /// <paramref name="placeholderPassword"/> 换成 <paramref name="newPassword"/>。
    /// 原地改写，包长度不变。
    /// </summary>
    /// <exception cref="InvalidFormatException">
    /// 占位密码与条目里的 verifier 不匹配、或条目结构非法（说明包不是刚由 7z.dll 生成的那个）。
    /// </exception>
    /// <exception cref="ZipCopyModeException">ZIP64 / EOCD 不可解析。</exception>
    internal static void Rekey(string archivePath, string placeholderPassword, string newPassword)
    {
        var oldPasswordBytes = Encoding.UTF8.GetBytes(placeholderPassword);
        var newPasswordBytes = NewPasswordBytes(newPassword);

        using var fs = new FileStream(
            archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var (cdOffset, entryCount, _) = ZipBinaryRewriter.ReadEocd(fs);
        var entries = ZipBinaryRewriter.ReadCentralDirectory(fs, cdOffset, entryCount);

        int rekeyed = 0;
        long totalBytes = 0;
        foreach (var entry in entries)
        {
            // 目录条目（无数据、未加密）直接跳过
            if ((entry.Flags & 0x0001) == 0)
                continue;

            // 没有 0x9901 = 传统 ZipCrypto，密钥派生不是 PBKDF2，换不了（调用方应已拦截）
            if (!ZipWinZipAes.TryReadExtraInfo(entry.RawExtraField, out var extra))
                throw new NotSupportedException(
                    "此处的加密方式不是 WinZip AES，无法在压缩后替换密码 "
                    + "(only WinZip AES entries can be re-keyed after compression)");

            int saltLength = ZipWinZipAes.SaltLength(extra.Strength);
            int keyLength = ZipWinZipAes.KeyLength(extra.Strength);
            if (saltLength == 0 || keyLength == 0)
                throw new InvalidFormatException(
                    $"WinZip AES: 未知的密钥强度 {extra.Strength}（条目 {entry.FileName}）");

            long cipherLength = entry.CompressedSize
                - saltLength - 2 - ZipWinZipAes.AuthenticationCodeLength;
            if (cipherLength < 0)
                throw new InvalidFormatException(
                    $"WinZip AES: 条目数据长度不足（条目 {entry.FileName}）");

            long dataStart = ReadDataStart(fs, entry.LocalHeaderOffset);

            // ── 读旧 salt + verifier，派生旧密钥，确认占位密码对得上 ──
            fs.Seek(dataStart, SeekOrigin.Begin);
            var oldSalt = new byte[saltLength];
            ReadExactly(fs, oldSalt);
            var storedVerifier = new byte[2];
            ReadExactly(fs, storedVerifier);

            var oldKeyMaterial = ZipWinZipAes.DeriveKey(oldPasswordBytes, oldSalt, keyLength);
            if (!CryptographicOperations.FixedTimeEquals(
                    oldKeyMaterial.AsSpan(2 * keyLength, 2), storedVerifier))
            {
                throw new InvalidFormatException(
                    $"WinZip AES: 占位密码校验失败（条目 {entry.FileName}），"
                    + "压缩产物可能被改动过，无法替换密码");
            }

            // ── 新的 salt + 新密钥 ──
            var newSalt = RandomNumberGenerator.GetBytes(saltLength);
            var newKeyMaterial = ZipWinZipAes.DeriveKey(newPasswordBytes, newSalt, keyLength);

            using var oldAes = CreateCtrCipher(oldKeyMaterial.AsSpan(0, keyLength));
            using var newAes = CreateCtrCipher(newKeyMaterial.AsSpan(0, keyLength));
            using var hmac = IncrementalHash.CreateHMAC(
                HashAlgorithmName.SHA1, newKeyMaterial.AsSpan(keyLength, keyLength).ToArray());

            // ── 密文原地换密钥（XOR 合成，两遍 keystream 一起异或）──
            long cipherStart = dataStart + saltLength + 2;
            var buffer = new byte[64 * 1024];
            long remaining = cipherLength;
            long position = cipherStart;
            long blockIndex = 1;

            while (remaining > 0)
            {
                int want = (int)Math.Min(remaining, buffer.Length);
                fs.Seek(position, SeekOrigin.Begin);
                ReadExactly(fs, buffer.AsSpan(0, want));

                TransformInPlace(buffer.AsSpan(0, want), oldAes, newAes, ref blockIndex);

                // 认证码作用于密文
                hmac.AppendData(buffer, 0, want);
                WriteAt(fs, position, buffer.AsSpan(0, want));

                position += want;
                remaining -= want;
            }

            // ── 写回新 salt / 新 verifier / 新认证码 ──
            WriteAt(fs, dataStart, newSalt);
            WriteAt(fs, dataStart + saltLength,
                newKeyMaterial.AsSpan(2 * keyLength, 2));
            WriteAt(fs, cipherStart + cipherLength,
                hmac.GetHashAndReset().AsSpan(0, ZipWinZipAes.AuthenticationCodeLength));

            rekeyed++;
            totalBytes += cipherLength;
        }

        fs.Flush(flushToDisk: true);
        CoreLog.Info(
            $"ZipWinZipAesRekey: re-keyed {rekeyed}/{entries.Count} entries, {totalBytes} cipher bytes");
    }

    // ── 内部实现 ───────────────────────────────────────────────

    private static Aes CreateCtrCipher(ReadOnlySpan<byte> key)
    {
        var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key.ToArray();
        return aes;
    }

    /// <summary>
    /// <c>密文' = 密文 XOR KS(old) XOR KS(new)</c>。
    /// 计数器规则与解密侧一致：128 位小端、块序号写 <c>counter[0..8]</c>、块号从 1 起、
    /// <c>counter[8..16]</c> 恒为 0（实测其他写法解出垃圾）。
    /// </summary>
    private static void TransformInPlace(
        Span<byte> data, Aes oldAes, Aes newAes, ref long blockIndex)
    {
        Span<byte> counter = stackalloc byte[16];
        Span<byte> oldKeyStream = stackalloc byte[16];
        Span<byte> newKeyStream = stackalloc byte[16];

        for (int offset = 0; offset < data.Length; offset += 16)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(counter[..8], (ulong)blockIndex);
            blockIndex++;

            oldAes.EncryptEcb(counter, oldKeyStream, PaddingMode.None);
            newAes.EncryptEcb(counter, newKeyStream, PaddingMode.None);

            int count = Math.Min(16, data.Length - offset);
            for (int i = 0; i < count; i++)
                data[offset + i] ^= (byte)(oldKeyStream[i] ^ newKeyStream[i]);
        }
    }

    /// <summary>读取本地文件头得到数据区起点：LFH(30) + nameLen + extraLen。</summary>
    private static long ReadDataStart(Stream stream, uint localHeaderOffset)
    {
        stream.Seek(localHeaderOffset, SeekOrigin.Begin);
        Span<byte> head = stackalloc byte[30];
        ReadExactly(stream, head);
        if (BinaryPrimitives.ReadUInt32LittleEndian(head) != 0x04034b50)
            throw new InvalidFormatException("WinZip AES: 找不到本地文件头 (local file header not found)");

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(head[26..]);
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(head[28..]);
        return localHeaderOffset + 30L + nameLength + extraLength;
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
        => stream.ReadExactly(destination);

    private static void WriteAt(Stream stream, long offset, ReadOnlySpan<byte> data)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        stream.Write(data);
    }
}
