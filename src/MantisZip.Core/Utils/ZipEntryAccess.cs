using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace MantisZip.Core.Utils;

/// <summary>
/// 中央目录里与解密相关的一条记录（仅供 WinZip AES 自研路径使用）。
/// </summary>
internal readonly record struct ZipAesEntryInfo(
    string EntryName,
    ushort Flags,
    ushort CompressionMethod,
    uint Crc32,
    long CompressedSize,
    long UncompressedSize,
    uint LocalHeaderOffset,
    byte[] RawExtraField);

/// <summary>
/// ZIP 条目读取的统一入口 —— issue #91 的接入点。
///
/// <para>
/// 非 AES 条目完全沿用 SharpCompress 的 <c>OpenEntryStream()</c>（行为零变化）。
/// AES 条目改走自研 <see cref="ZipWinZipAes"/> 解密层：因为 SharpCompress 把密码
/// 写死成 UTF-8 派生，没有任何公开入口能换密码字节，GBK 密码写出的包永远失败。
/// </para>
///
/// <para>
/// 密码字节编码采用<strong>自动探测</strong>：依次试 UTF-8 → 系统 ANSI 代码页 → GBK → GB18030，
/// 由 PBKDF2 派生出的 2 字节 verifier 判定命中（256 分之一的误判率由随后的 HMAC-SHA1
/// 认证码兜底，实际等于零）。命中后解码、解压、校验全部沿用该编码，所以密码校验 /
/// 测试 / 预览 / 解压四个入口天然同一套策略。用户无需改 Windows 区域设置。
/// </para>
/// </summary>
internal static class ZipEntryAccess
{
    /// <summary>同时缓存的最多压缩包数量（AES 包极少，缓存只为避免并行解压时重复解析中央目录）。</summary>
    private const int MaxCachedArchives = 8;

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, (long Length, DateTime WriteTime, List<ZipAesEntryInfo> Entries)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 打开条目的<strong>明文</strong>读取流（已解密且已解压）。AES 条目在读到末尾时会校验
    /// HMAC 认证码（及 AE-1 的 CRC32），失败抛 <see cref="InvalidFormatException"/>。
    /// </summary>
    /// <param name="archivePath">压缩包路径（AES 路径需要它自行定位本地头）。</param>
    /// <param name="entry">SharpCompress 枚举出的条目（用其 Key 匹配中央目录记录）。</param>
    /// <param name="password">用户输入的密码（string，本层负责编码探测）。</param>
    internal static Stream OpenReadStream(string archivePath, IArchiveEntry entry, string? password)
    {
        if (!entry.IsEncrypted || string.IsNullOrEmpty(password))
            return entry.OpenEntryStream();

        // 加密但非 WinZip AES（传统 ZipCrypto）→ SharpCompress 能处理，保持原样
        var info = FindAesEntry(archivePath, entry.Key);
        if (info is null)
            return entry.OpenEntryStream();

        return OpenAesReadStream(archivePath, info.Value, password);
    }

    /// <summary>
    /// 把条目的明文内容完整读出并写入 <paramref name="destination"/>（带进度回调）。
    /// 等价于 SharpCompress 的 <c>WriteTo</c>，但 AES 条目走自研层。
    /// </summary>
    /// <param name="onProgress">每写完一块回调一次（已写字节数）。</param>
    internal static void WriteTo(
        string archivePath,
        IArchiveEntry entry,
        Stream destination,
        string? password,
        Action<long>? onProgress = null,
        int bufferSize = 65536)
    {
        if (!entry.IsEncrypted || string.IsNullOrEmpty(password))
        {
            // 非 AES：沿用官方 CRC 校验路径，行为与改动前完全一致
            entry.WriteTo(destination, CreateCrcOptions(), MakeProgress(onProgress));
            return;
        }

        var info = FindAesEntry(archivePath, entry.Key);
        if (info is null)
        {
            entry.WriteTo(destination, CreateCrcOptions(), MakeProgress(onProgress));
            return;
        }

        using var plain = OpenAesReadStream(archivePath, info.Value, password);
        var buffer = new byte[bufferSize];
        long written = 0;
        while (true)
        {
            int read = plain.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            destination.Write(buffer, 0, read);
            written += read;
            onProgress?.Invoke(written);
        }
    }

    /// <summary>是否为 WinZip AES 条目（供密码校验层短路用）。</summary>
    internal static bool IsWinZipAesEntry(string archivePath, string entryName)
        => FindAesEntry(archivePath, entryName) is not null;

    /// <summary>
    /// 归档内任意一个 WinZip AES 条目的名字（没有 AES 条目时返回 null）。
    /// 密码校验层用它挑「待验证条目」：AES 包不依赖 SharpCompress 的 IsEncrypted 语义。
    /// </summary>
    internal static string? FindAnyAesEntryName(string archivePath)
    {
        var entries = GetAesEntries(archivePath);
        return entries.Count == 0 ? null : entries[0].EntryName;
    }

    /// <summary>
    /// 直接用密码校验 AES 条目的 2 字节 verifier —— 不读正文、不解压，最快路径。
    /// 返回 true = 该密码（任一候选编码）匹配；false = 全部候选都不匹配。
    /// </summary>
    internal static bool VerifyAesPassword(string archivePath, string entryName, string password)
    {
        var info = FindAesEntry(archivePath, entryName);
        if (info is null)
            return false;

        try
        {
            using var fs = OpenArchiveFile(archivePath, out long dataStart);
            var layout = ReadAesDataLayout(fs, dataStart, info.Value);
            if (layout is null)
                return false;

            var matches = DiscoverPasswordBytes(layout.Value.Salt, layout.Value.StoredVerifier, layout.Value.KeyLength, password);
            return matches.Count > 0;
        }
        catch (InvalidFormatException)
        {
            return false;
        }
    }

    // ── 内部实现 ───────────────────────────────────────────────

    private static ExtractionOptions CreateCrcOptions() => new() { CheckCrc = true, BufferSize = 65536 };

    private static IProgress<ProgressReport>? MakeProgress(Action<long>? onProgress)
        => onProgress is null ? null : new InlineProgress<ProgressReport>(rep => onProgress(rep.BytesTransferred));

    /// <summary>
    /// 在中央目录里查找该条目对应的 AES 记录（按条目名精确匹配）。
    /// 返回 null 表示：不是 AES 条目、或中央目录解析不了（ZIP64 / 非标准布局）→ 调用方回落到 SharpCompress。
    /// </summary>
    private static ZipAesEntryInfo? FindAesEntry(string archivePath, string? entryName)
    {
        if (string.IsNullOrEmpty(entryName))
            return null;

        var entries = GetAesEntries(archivePath);
        if (entries.Count == 0)
            return null;

        var normalized = ArchivePath.Normalize(entryName);
        foreach (var e in entries)
        {
            if (string.Equals(e.EntryName, entryName, StringComparison.Ordinal))
                return e;
        }
        foreach (var e in entries)
        {
            if (string.Equals(ArchivePath.Normalize(e.EntryName), normalized, StringComparison.Ordinal))
                return e;
        }
        return null;
    }

    private static List<ZipAesEntryInfo> GetAesEntries(string archivePath)
    {
        var fileInfo = new FileInfo(archivePath);
        if (!fileInfo.Exists)
            return [];

        lock (CacheLock)
        {
            if (Cache.TryGetValue(archivePath, out var cached)
                && cached.Length == fileInfo.Length
                && cached.WriteTime == fileInfo.LastWriteTimeUtc)
            {
                return cached.Entries;
            }
        }

        List<ZipAesEntryInfo> list;
        try
        {
            using var fs = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var (cdOffset, entryCount, _) = ZipBinaryRewriter.ReadEocd(fs);
            var cds = ZipBinaryRewriter.ReadCentralDirectory(fs, cdOffset, entryCount);

            list = [];
            foreach (var cd in cds)
            {
                if ((cd.Flags & 0x0001) == 0)
                    continue;   // 未加密
                if (!ZipWinZipAes.TryReadExtraInfo(cd.RawExtraField, out _))
                    continue;   // 传统 ZipCrypto（没有 0x9901）
                list.Add(new ZipAesEntryInfo(
                    cd.FileName,
                    cd.Flags,
                    cd.CompressionMethod,
                    cd.Crc32,
                    cd.CompressedSize,
                    cd.UncompressedSize,
                    cd.LocalHeaderOffset,
                    cd.RawExtraField));
            }
        }
        catch (Exception ex) when (ex is ZipCopyModeException or InvalidDataException or IOException or ArgumentException)
        {
            // 中央目录不可解析（ZIP64 / 加密的中央目录 / 畸形包）→ 本次按「无 AES 条目」处理，
            // 交给 SharpCompress 走既有 UTF-8 路径（行为与改动前一致，不会更差）。
            CoreLog.Info($"ZipEntryAccess: 中央目录不可解析，跳过 AES 自研路径: {ex.Message}");
            list = [];
        }

        lock (CacheLock)
        {
            if (Cache.Count >= MaxCachedArchives)
                Cache.Clear();
            Cache[archivePath] = (fileInfo.Length, fileInfo.LastWriteTimeUtc, list);
        }
        return list;
    }

    /// <summary>打开只读文件流并把 fileStream 定位到条目数据区。</summary>
    private static FileStream OpenArchiveFile(string archivePath, out long dataStart)
    {
        var fs = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        try
        {
            dataStart = ReadDataStart(fs, 0);
            return fs;
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 读取本地文件头得到数据区起点：LFH(30) + nameLen + extraLen。
    /// <paramref name="localHeaderOffset"/> 为该条目在包内的本地头偏移。
    /// </summary>
    private static long ReadDataStart(Stream stream, uint localHeaderOffset)
    {
        stream.Seek(localHeaderOffset, SeekOrigin.Begin);
        Span<byte> head = stackalloc byte[30];
        int read = stream.Read(head);
        if (read < 30 || BinaryPrimitives.ReadUInt32LittleEndian(head) != 0x04034b50)
            throw new InvalidFormatException("WinZip AES: local file header not found");

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(head[26..]);
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(head[28..]);
        return localHeaderOffset + 30L + nameLength + extraLength;
    }

    private readonly record struct AesDataLayout(byte[] Salt, ushort StoredVerifier, int KeyLength, long CipherLength);

    /// <summary>
    /// 从数据区起点读出 salt / verifier，并算出密文长度
    /// （compSize = saltLen + 2 + 密文 + 10 字节认证码）。
    /// </summary>
    private static AesDataLayout? ReadAesDataLayout(Stream stream, long dataStart, ZipAesEntryInfo info)
    {
        if (!ZipWinZipAes.TryReadExtraInfo(info.RawExtraField, out var extra))
            return null;

        int saltLength = ZipWinZipAes.SaltLength(extra.Strength);
        int keyLength = ZipWinZipAes.KeyLength(extra.Strength);
        if (saltLength == 0 || keyLength == 0)
            return null;

        long cipherLength = info.CompressedSize - saltLength - 2 - ZipWinZipAes.AuthenticationCodeLength;
        if (cipherLength < 0)
            return null;

        stream.Seek(dataStart, SeekOrigin.Begin);
        var salt = new byte[saltLength];
        int read = 0;
        while (read < saltLength)
        {
            int n = stream.Read(salt, read, saltLength - read);
            if (n <= 0)
                return null;
            read += n;
        }

        var verifierBytes = new byte[2];
        read = 0;
        while (read < 2)
        {
            int n = stream.Read(verifierBytes, read, 2 - read);
            if (n <= 0)
                return null;
            read += n;
        }

        ushort storedVerifier = BinaryPrimitives.ReadUInt16LittleEndian(verifierBytes);
        return new AesDataLayout(salt, storedVerifier, keyLength, cipherLength);
    }

    /// <summary>
    /// 自动探测密码字节编码：逐个候选派生密钥、比对 2 字节 verifier，返回所有命中的候选。
    /// 空列表 = 密码错误（该条目的密码不由任何已知编码表示）。
    /// </summary>
    private static List<ZipWinZipAes.PasswordBytes> DiscoverPasswordBytes(
        byte[] salt, ushort storedVerifier, int keyLength, string password)
    {
        var matches = new List<ZipWinZipAes.PasswordBytes>(1);
        foreach (var candidate in ZipWinZipAes.CandidatePasswordBytes(password))
        {
            var derived = ZipWinZipAes.DeriveKey(candidate.Bytes, salt, keyLength);
            ushort actual = BinaryPrimitives.ReadUInt16LittleEndian(derived.AsSpan(2 * keyLength, 2));
            if (actual == storedVerifier)
                matches.Add(candidate);
        }
        return matches;
    }

    /// <summary>
    /// 打开 AES 条目：定位 → 探测密码编码 → 解密 → 解压 → （可选）CRC 校验。
    /// verifier 不匹配时抛 <see cref="InvalidFormatException"/>（消息含 "bad password"，
    /// 既有 <c>PasswordService</c> / <c>ArchiveService</c> 的密码启发式直接命中）。
    /// </summary>
    private static Stream OpenAesReadStream(string archivePath, ZipAesEntryInfo info, string password)
    {
        var fs = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        Stream? current = null;
        try
        {
            long dataStart = ReadDataStart(fs, info.LocalHeaderOffset);
            var layout = ReadAesDataLayout(fs, dataStart, info)
                ?? throw new InvalidFormatException($"WinZip AES: 条目的 0x9901 扩展字段非法: {info.EntryName}");

            // 中央目录里的 method 字段对 AES 条目恒为 99（AES 标记），真正的压缩方法在 0x9901 的
            // ActualMethod 里 —— 拿 method 99 去分派必然落到「不支持」，issue #91 语料即 Deflate(8)。
            if (!ZipWinZipAes.TryReadExtraInfo(info.RawExtraField, out var extraInfo))
                throw new InvalidFormatException($"WinZip AES: 条目的 0x9901 扩展字段非法: {info.EntryName}");

            var candidates = DiscoverPasswordBytes(layout.Salt, layout.StoredVerifier, layout.KeyLength, password);
            if (candidates.Count == 0)
                throw ZipWinZipAes.WrongPassword();

            // 用第一个命中的编码（顺序 = UTF-8 → 系统 ANSI → GBK → GB18030，与旧行为的 UTF-8 优先一致）
            var derived = Derive(salt: layout.Salt, passwordBytes: candidates[0].Bytes, keyLength: layout.KeyLength);

            long cipherStart = dataStart + layout.Salt.Length + 2;

            // 认证码位于条目数据末尾 10 字节
            var expectedMac = new byte[ZipWinZipAes.AuthenticationCodeLength];
            fs.Seek(cipherStart + layout.CipherLength, SeekOrigin.Begin);
            int macRead = 0;
            while (macRead < expectedMac.Length)
            {
                int n = fs.Read(expectedMac, macRead, expectedMac.Length - macRead);
                if (n <= 0)
                    throw new InvalidFormatException($"WinZip AES: 认证码缺失: {info.EntryName}");
                macRead += n;
            }

            var cipherRegion = new SubStream(fs, cipherStart, layout.CipherLength, leaveOpen: true);
            var decrypt = new WinZipAesDecryptStream(
                cipherRegion,
                layout.CipherLength,
                derived.EncryptionKey,
                derived.MacKey,
                expectedMac);

            // AES 包裹的只是压缩后字节，内层压缩方法与普通条目一致（0x9901 的 ActualMethod）。
            // 注意：SharpCompress 自己的 AES 分支只覆盖 None/Deflate 的少数方法，且把 LZMA 一律判成
            // 「LZMA with pkware encryption」—— 那是 ZipCrypto 的限制，WinZip AES 下并不成立
            // （实测 AES + LZMA 包可正常解出）。这里按实际方法分派，保住 #91 之前
            // （v0.5.7 纯 SharpCompress 路径）能读的全部方法。
            Stream decompressed = extraInfo.ActualMethod switch
            {
                0 => decrypt, // Store
                8 => new System.IO.Compression.DeflateStream(decrypt, CompressionMode.Decompress, leaveOpen: false),
                9 => new SharpCompress.Compressors.Deflate64.Deflate64Stream(
                    decrypt, SharpCompress.Compressors.CompressionMode.Decompress),
                12 => SharpCompress.Compressors.BZip2.BZip2Stream.Create(
                    decrypt,
                    SharpCompress.Compressors.CompressionMode.Decompress,
                    decompressConcatenated: false,
                    leaveOpen: false,
                    tolerateTruncatedStream: false),
                14 => OpenLzmaStream(decrypt),
                98 => OpenPpmdStream(decrypt),
                _ => throw new NotSupportedException(
                    $"WinZip AES: 该条目的压缩方法不受支持（方法 {extraInfo.ActualMethod}，条目 {info.EntryName}）。"
                    + " WinZip AES: unsupported inner compression method for this entry. "
                    + "本程序支持 Store / Deflate / Deflate64 / BZip2 / LZMA / PPMd，其余方法请用支持该格式的工具解压。"),
            };

            // AE-1（version 1）在头里带真实 CRC32；AE-2（version 2）CRC 字段恒为 0，完整性由 HMAC 负责
            if (extraInfo.Version == 1 && info.Crc32 != 0)
                decompressed = new Crc32VerifyingStream(decompressed, info.Crc32, info.EntryName);

            current = decompressed;
            return current;
        }
        catch
        {
            current?.Dispose();
            fs.Dispose();
            throw;
        }
    }

    /// <summary>
    /// ZIP 里的 PPMd 条目在压缩数据最前面带 2 字节属性（模型阶数 + 分配器大小），
    /// 需要先读出来构造 <c>PpmdProperties</c>，剩余字节才是 PPMd 码流。
    /// 与 SharpCompress 自己的 ZIP PPMd 分支（<c>ZipFilePart.CreateDecompressionStream</c>）一致。
    /// </summary>
    private static Stream OpenPpmdStream(Stream source)
    {
        Span<byte> properties = stackalloc byte[2];
        source.ReadExactly(properties);
        var ppmProperties = new SharpCompress.Compressors.PPMd.PpmdProperties(properties);
        return SharpCompress.Compressors.PPMd.PpmdStream.Create(ppmProperties, source, compress: false);
    }

    /// <summary>
    /// ZIP 里的 LZMA 条目数据以「版本号(2) + 属性长度(2) + 属性 + 码流」开头，
    /// 属性长度实测恒为 5。码流自带结束标记（EOS），所以用两参 <c>Create(properties, stream)</c>
    /// 即可解到底 —— 传 inSize/outSize 反而在部分样本上抛 <c>DataErrorException: Data Error</c>。
    /// </summary>
    private static Stream OpenLzmaStream(Stream source)
    {
        Span<byte> header = stackalloc byte[4];
        source.ReadExactly(header);
        int propertiesLength = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
        if (propertiesLength is <= 0 or > 8)
            throw new InvalidFormatException($"WinZip AES: LZMA 属性长度非法（{propertiesLength}）");

        var properties = new byte[propertiesLength];
        source.ReadExactly(properties);
        return SharpCompress.Compressors.LZMA.LzmaStream.Create(properties, source, leaveOpen: false);
    }

    /// <summary>由密码字节派生三元组（加密密钥 / 认证密钥）。</summary>
    private static DerivedKey Derive(byte[] salt, byte[] passwordBytes, int keyLength)
    {
        var keyMaterial = ZipWinZipAes.DeriveKey(passwordBytes, salt, keyLength);
        var encryptionKey = keyMaterial.AsSpan(0, keyLength).ToArray();
        var macKey = keyMaterial.AsSpan(keyLength, keyLength).ToArray();
        return new DerivedKey(encryptionKey, macKey);
    }

    private readonly record struct DerivedKey(byte[] EncryptionKey, byte[] MacKey);

    /// <summary>只读、限制在某段字节区间上的文件视图（不复制数据、不缓冲）。</summary>
    private sealed class SubStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _start;
        private readonly long _totalLength;
        private readonly bool _leaveOpen;
        private long _remaining;

        internal SubStream(Stream inner, long start, long totalLength, bool leaveOpen)
        {
            _inner = inner;
            _start = start;
            _totalLength = totalLength;
            _leaveOpen = leaveOpen;
            _remaining = totalLength;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _totalLength;
        public override long Position
        {
            get => _totalLength - _remaining;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            if (count > _remaining) count = (int)_remaining;

            _inner.Seek(_start + (_totalLength - _remaining), SeekOrigin.Begin);
            int read = _inner.Read(buffer, offset, count);
            if (read > 0) _remaining -= read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
