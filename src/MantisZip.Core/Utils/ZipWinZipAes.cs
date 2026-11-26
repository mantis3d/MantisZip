using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Common;

namespace MantisZip.Core.Utils;

/// <summary>
/// WinZip AES（AE-1 / AE-2）解密基础件 —— issue #91 的自研解密层。
///
/// <para>
/// 为什么自研：SharpCompress 0.50.4 的 <c>WinzipAesEncryptionData</c> 固定把密码按
/// <c>Encoding.UTF8.GetBytes(password)</c> 派生密钥，且<strong>没有任何公开入口</strong>
/// 可以注入密码字节（<c>ArchiveEncoding.Password</c> 对 AES 分支无效，
/// <c>ReaderOptions</c> / <c>WriterOptions</c> 都只有 string 形态的 Password）。
/// 于是 pyzipper 用 GBK 编码密码写出的 AES 包在 MantisZip 里永远「密码错误」——
/// 报告者机器系统代码页是 UTF-8，这条路径与 Windows 区域设置无关，只能自己解密。
/// </para>
///
/// <para>
/// 文件布局（实测确认，注意 salt <strong>不在</strong> 0x9901 扩展字段里）：
/// <code>
/// LFH(30) + nameLen + extraLen  ⇒ dataStart
/// data[dataStart .. +saltLen]   ⇒ salt（长度由 strength 推导：1→8 / 2→12 / 3→16）
/// data[+saltLen .. +2]          ⇒ 2 字节 password verifier（小端）
/// 其后到末尾 -10 字节            ⇒ AES-CTR 密文
/// 最后 10 字节                   ⇒ HMAC-SHA1 前 10 字节（作用于<strong>密文</strong>）
/// </code>
/// </para>
///
/// <para>
/// 派生：<c>PBKDF2-HMAC-SHA1(pwdBytes, salt, 1000, 2*keyLen + 2)</c> →
/// <c>encKey = [0, keyLen)</c>、<c>macKey = [keyLen, 2*keyLen)</c>、
/// verifier = <c>[2*keyLen, 2*keyLen+2)</c>。
/// AES-CTR 计数器实测只有一种写法可行：128 位<strong>小端</strong>、块序号写在
/// <c>counter[0..8]</c>、块号从 1 起、<c>counter[8..16]</c> 恒为 0
/// （序号写在高 8 字节、或 NIST 大端都会解出垃圾）。
/// </para>
/// </summary>
internal static class ZipWinZipAes
{
    /// <summary>WinZip AES 扩展字段 ID（中央目录与本地头都带）。</summary>
    internal const ushort ExtraFieldId = 0x9901;

    /// <summary>条目数据末尾的 HMAC-SHA1 认证码长度。</summary>
    internal const int AuthenticationCodeLength = 10;

    /// <summary>PBKDF2 迭代次数（WinZip AES 规范固定 1000）。</summary>
    internal const int KdfIterations = 1000;

    /// <summary>扩展字段内的密钥强度字段（1 = 128 位、2 = 192 位、3 = 256 位）。</summary>
    internal readonly record struct ExtraInfo(int Version, int Strength, ushort ActualMethod);

    /// <summary>候选密码字节的一种编码。</summary>
    internal readonly record struct PasswordBytes(string EncodingKey, byte[] Bytes);

    /// <summary>
    /// 解析条目扩展字段中的 0x9901 块。返回 false 表示该条目不是 WinZip AES 加密。
    /// 布局：id(2) size(2) version(2) vendor(2 = "AE") strength(1) actualMethod(2)。
    /// </summary>
    internal static bool TryReadExtraInfo(byte[]? extraField, out ExtraInfo info)
    {
        info = default;
        if (extraField is null || extraField.Length < 11)
            return false;

        var span = extraField.AsSpan();
        int pos = 0;
        while (pos + 4 <= span.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(span[pos..]);
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos + 2));
            if (size == 0 || pos + 4 + size > span.Length)
                break;

            if (id == ExtraFieldId && size >= 7)
            {
                var payload = span.Slice(pos + 4, size);
                int version = BinaryPrimitives.ReadUInt16LittleEndian(payload[..2]);
                int strength = payload[4];
                ushort actualMethod = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(5, 2));
                info = new ExtraInfo(version, strength, actualMethod);
                return true;
            }

            pos += 4 + size;
        }

        return false;
    }

    /// <summary>密钥强度 → salt 长度（未知强度返回 0，调用方据此判定格式非法）。</summary>
    internal static int SaltLength(int strength) => strength switch
    {
        1 => 8,
        2 => 12,
        3 => 16,
        _ => 0,
    };

    /// <summary>密钥强度 → 密钥长度（未知强度返回 0）。</summary>
    internal static int KeyLength(int strength) => strength switch
    {
        1 => 16,
        2 => 24,
        3 => 32,
        _ => 0,
    };

    /// <summary>
    /// 密码的候选字节编码（顺序即探测顺序）：
    /// <strong>UTF-8 优先</strong>（APPNOTE 与 pyzipper 默认，也是旧行为），
    /// 然后系统 ANSI 代码页（7z.dll 在中文 Windows 上就是这么干的）、
    /// 再 GBK / GB18030（python 的 <c>encode("gbk")</c> 最常见）。
    /// 字节完全相同的候选会被去掉（纯 ASCII 密码四种编码字节一致，只探一次）。
    /// </summary>
    internal static IReadOnlyList<PasswordBytes> CandidatePasswordBytes(string password)
    {
        var results = new List<PasswordBytes>(4);

        Add("utf-8", TryGetBytes(() => Encoding.UTF8.GetBytes(password)));
        Add(NameEncodingCatalog.System, TryGetBytes(() => NameEncodingCatalog.ResolveSystemEncoding().GetBytes(password)));
        Add("gbk", TryGetBytes(() => Encoding.GetEncoding(936).GetBytes(password)));
        Add("gb18030", TryGetBytes(() => Encoding.GetEncoding(54936).GetBytes(password)));
        return results;

        void Add(string key, byte[]? bytes)
        {
            if (bytes is null) return;
            foreach (var existing in results)
            {
                if (existing.Bytes.AsSpan().SequenceEqual(bytes)) return;
            }
            results.Add(new PasswordBytes(key, bytes));
        }

        static byte[]? TryGetBytes(Func<byte[]> factory)
        {
            try
            {
                return factory();
            }
            catch (ArgumentException)
            {
                // 该编码无法表示此密码（如 GBK 遇到 emoji）→ 跳过该候选
                return null;
            }
        }
    }

    /// <summary>PBKDF2 派生：返回 <c>2*keyLength+2</c> 字节（加密密钥 + 认证密钥 + 2 字节校验值）。</summary>
    internal static byte[] DeriveKey(byte[] passwordBytes, byte[] salt, int keyLength)
        => Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes,
            salt,
            KdfIterations,
            HashAlgorithmName.SHA1,
            2 * keyLength + 2);

    /// <summary>CRC32（反射多项式 0xEDB88320），供 AES 条目在 AE-1 场景校验明文完整性。</summary>
    internal static uint Crc32(uint state, ReadOnlySpan<byte> data)
    {
        uint crc = state;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc;
    }

    /// <summary>密码错误（verifier 不匹配）——消息含 "password"，沿用既有密码异常分类启发式。</summary>
    internal static InvalidFormatException WrongPassword() => new("bad password (WinZip AES)");

    /// <summary>认证码不匹配（密码对但数据被改）——同样走 <c>InvalidFormatException</c> 的损坏判定路径。</summary>
    internal static InvalidFormatException AuthenticationFailed(string entryName)
        => new($"WinZip AES authentication failed: {entryName}");
}

/// <summary>
/// WinZip AES 的 AES-CTR 解密流：只读、前向、不缓冲整个条目。
///
/// <para>
/// 每读到一段密文就 <c>HMAC-SHA1</c> 累积一次（认证码作用于<strong>密文</strong>），
/// 读到密文末尾时与条目数据尾部 10 字节比对；比对失败抛
/// <see cref="InvalidFormatException"/>（与 SharpCompress 的损坏异常同族，既有的
/// 逐条目失败/整包损坏收尾逻辑无需改动）。
/// 调用方未读到末尾就 dispose（例如预览只取头部）时不校验认证码 —— 此时失败点
/// 已在 PBKDF2 的 2 字节 verifier 上暴露，不会把错密码放过去。
/// </para>
/// </summary>
internal sealed class WinZipAesDecryptStream : Stream
{
    private const int ChunkSize = 4096;   // 16 的整数倍，凑满 AES 块

    private readonly Stream _cipher;
    private readonly Aes _aes;
    private readonly byte[] _authenticationKey;
    private readonly byte[] _expectedAuthenticationCode;
    private readonly IncrementalHash _hmac;
    private readonly byte[] _counter = new byte[16];
    private readonly byte[] _keyStream = new byte[16];
    private readonly byte[] _cipherChunk = new byte[ChunkSize];
    private readonly byte[] _plainChunk = new byte[ChunkSize];

    private long _blockIndex = 1;
    private long _cipherRemaining;
    private int _plainAvailable;
    private int _plainPosition;
    private bool _finished;
    private bool _verified;
    private bool _disposed;

    internal WinZipAesDecryptStream(
        Stream cipherStream,
        long cipherLength,
        byte[] encryptionKey,
        byte[] authenticationKey,
        byte[] expectedAuthenticationCode)
    {
        _cipher = cipherStream;
        _cipherRemaining = cipherLength;
        _authenticationKey = authenticationKey;
        _expectedAuthenticationCode = expectedAuthenticationCode;
        _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, authenticationKey);

        _aes = Aes.Create();
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _aes.Key = encryptionKey;
        // counter[8..16] 恒为 0（规范要求，实测写这里会解出垃圾）
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (count <= 0)
            return 0;

        if (_plainPosition >= _plainAvailable && !FillNextChunk())
            return 0;

        int n = Math.Min(count, _plainAvailable - _plainPosition);
        Array.Copy(_plainChunk, _plainPosition, buffer, offset, n);
        _plainPosition += n;
        return n;
    }

    private bool FillNextChunk()
    {
        if (_finished)
            return false;

        int length = 0;
        while (length < _cipherChunk.Length && _cipherRemaining > 0)
        {
            int want = (int)Math.Min(_cipherRemaining, _cipherChunk.Length - length);
            int read = _cipher.Read(_cipherChunk, length, want);
            if (read <= 0)
                break;
            length += read;
            _cipherRemaining -= read;
        }

        if (length == 0)
        {
            _finished = true;
            VerifyAuthenticationCode();
            return false;
        }

        _hmac.AppendData(_cipherChunk, 0, length);

        // 逐块生成 keystream 并异或（最后一块可能不足 16 字节）
        int blocks = (length + 15) / 16;
        for (int b = 0; b < blocks; b++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(_counter.AsSpan(0, 8), (ulong)_blockIndex);
            _blockIndex++;
            _aes.EncryptEcb(_counter, _keyStream, PaddingMode.None);

            int baseOffset = b * 16;
            int blockBytes = Math.Min(16, length - baseOffset);
            for (int i = 0; i < blockBytes; i++)
                _plainChunk[baseOffset + i] = (byte)(_cipherChunk[baseOffset + i] ^ _keyStream[i]);
        }

        _plainAvailable = length;
        _plainPosition = 0;
        return true;
    }

    private void VerifyAuthenticationCode()
    {
        if (_verified)
            return;
        _verified = true;

        var actual = _hmac.GetHashAndReset();
        var expected = new byte[ZipWinZipAes.AuthenticationCodeLength];
        Array.Copy(_expectedAuthenticationCode, expected, expected.Length);

        var actualPrefix = actual.AsSpan(0, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(actualPrefix, expected))
            throw ZipWinZipAes.AuthenticationFailed("entry");
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
            {
                _hmac.Dispose();
                _aes.Dispose();
                _cipher.Dispose();
                _authenticationKey.AsSpan().Clear();
            }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 校验明文的 CRC32（AE-1 条目头部带真实 CRC；AE-2 的 CRC 字段为 0，由 HMAC 承担完整性）。
/// 读到末尾才比对，失败抛 <see cref="InvalidFormatException"/>。
/// </summary>
internal sealed class Crc32VerifyingStream : Stream
{
    private readonly Stream _inner;
    private readonly uint _expectedCrc;
    private readonly string _entryName;
    private uint _crc = 0xFFFFFFFFu;
    private bool _verified;
    private bool _disposed;

    internal Crc32VerifyingStream(Stream inner, uint expectedCrc, string entryName)
    {
        _inner = inner;
        _expectedCrc = expectedCrc;
        _entryName = entryName;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int read = _inner.Read(buffer, offset, count);
        if (read > 0)
        {
            _crc = ZipWinZipAes.Crc32(_crc, buffer.AsSpan(offset, read));
        }
        else if (!_verified)
        {
            _verified = true;
            uint actual = _crc ^ 0xFFFFFFFFu;
            if (actual != _expectedCrc)
            {
                throw new InvalidFormatException(
                    $"CRC mismatch: entry '{_entryName}' expected 0x{_expectedCrc:X8}, actual 0x{actual:X8}");
            }
        }
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
                _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
