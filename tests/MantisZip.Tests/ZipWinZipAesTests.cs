using System.IO.Compression;
using System.Text;
using MantisZip.Core.Utils;
using SharpCompress.Common;
using SharpCompress.Readers;
using Xunit;
using SysZipArchive = System.IO.Compression.ZipArchive;
using SysZipArchiveMode = System.IO.Compression.ZipArchiveMode;
using SharpZipArchive = SharpCompress.Archives.Zip.ZipArchive;

namespace MantisZip.Tests;

/// <summary>
/// WinZip AES 读取层（issue #91）的回归测试。
/// <para>
/// 背景：SharpCompress 0.50.4 的 <c>WinzipAesEncryptionData</c> 把密码写死成 <c>Encoding.UTF8</c>，
/// 中文 GBK 密码的 AES 包因此永远验不过 verifier（用户看到「密码错误」）。
/// 修复引入自研解密层：按 UTF-8 → 系统 ANSI → GBK → GB18030 逐个候选比对 2 字节 verifier，
/// 命中后再解密并校验 HMAC-SHA1（AE-2）或 CRC32（AE-1）。
/// </para>
/// <para>
/// 语料 <c>tests/corpus/winzip-aes/</c> 由 issue #91 的 pyzipper 脚本生成：
/// 两个包内容完全一致（单条目 <c>样本.txt</c>，33 字节），密码都是 <c>测试A123</c>，
/// 唯一区别是写入时的密码字节编码（GBK vs UTF-8）。
/// </para>
/// </summary>
public class ZipWinZipAesTests
{
    private const string Password = "测试A123";
    private const string EntryName = "样本.txt";
    private const string ExpectedContent = "MantisZip password encoding test\n";

    static ZipWinZipAesTests()
    {
        // 与 ZipEngine 静态构造器同一做法：未注册时 GetEncoding("gbk") 会抛 NotSupportedException
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            $"无法定位仓库根（祖先目录链中未找到 'src'）。Base directory: {AppContext.BaseDirectory}");
    }

    private static string Fixture(string fileName)
        => Path.Combine(GetRepoRoot(), "tests", "corpus", "winzip-aes", fileName);

    /// <summary>用给定密码打开包并取出唯一条目（只借它拿 Key/IsEncrypted，AES 正文交给 ZipEntryAccess）。</summary>
    private static (Stream Package, SharpCompress.Archives.IArchive Archive, SharpCompress.Archives.IArchiveEntry Entry) OpenEntry(
        string archivePath, string? password)
    {
        var fs = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var archive = SharpZipArchive.OpenArchive(fs, new ReaderOptions { Password = password ?? string.Empty });
        var entry = archive.Entries.First(e => !e.IsDirectory);
        return (fs, archive, entry);
    }

    private static string ReadAll(Stream stream)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    // ── 端到端：两种密码字节编码都要能解开 ──────────────────────

    [Fact]
    public void OpenReadStream_Utf8EncodedPassword_DecryptsContent()
    {
        var path = Fixture("aes-utf8.zip");
        var (package, archive, entry) = OpenEntry(path, Password);
        using (package)
        using (archive)
        {
            using var stream = ZipEntryAccess.OpenReadStream(path, entry, Password);
            Assert.Equal(ExpectedContent, ReadAll(stream));
        }
    }

    /// <summary>
    /// issue #91 的主症状：GBK 密码的包在改动前被判「密码错误」。
    /// 该包只能靠 GBK 候选字节通过 verifier，证明候选编码探测生效。
    /// </summary>
    [Fact]
    public void OpenReadStream_GbkEncodedPassword_DecryptsContent()
    {
        var path = Fixture("aes-gbk.zip");
        var (package, archive, entry) = OpenEntry(path, Password);
        using (package)
        using (archive)
        {
            using var stream = ZipEntryAccess.OpenReadStream(path, entry, Password);
            Assert.Equal(ExpectedContent, ReadAll(stream));
        }
    }

    [Fact]
    public void WriteTo_GbkEncodedPassword_WritesExactContentAndReportsProgress()
    {
        var path = Fixture("aes-gbk.zip");
        var (package, archive, entry) = OpenEntry(path, Password);
        using (package)
        using (archive)
        {
            using var destination = new MemoryStream();
            long reported = -1;
            ZipEntryAccess.WriteTo(path, entry, destination, Password, written => reported = written);

            Assert.Equal(ExpectedContent, Encoding.UTF8.GetString(destination.ToArray()));
            // 进度回调收到的是累计已写字节数，最后一次必须等于文件长度
            Assert.Equal(destination.Length, reported);
        }
    }

    // ── 密码校验短路（密码框的第一个判定） ──────────────────────

    [Theory]
    [InlineData("aes-gbk.zip")]
    [InlineData("aes-utf8.zip")]
    public void VerifyAesPassword_CorrectPassword_ReturnsTrue(string fileName)
    {
        var path = Fixture(fileName);
        Assert.Equal(EntryName, ZipEntryAccess.FindAnyAesEntryName(path));
        Assert.True(ZipEntryAccess.VerifyAesPassword(path, EntryName, Password));
    }

    [Fact]
    public void VerifyAesPassword_WrongPassword_ReturnsFalse()
    {
        var path = Fixture("aes-gbk.zip");
        Assert.False(ZipEntryAccess.VerifyAesPassword(path, EntryName, "测试A124"));
        Assert.False(ZipEntryAccess.VerifyAesPassword(path, EntryName, "password"));
    }

    /// <summary>
    /// 错密码必须落成 SharpCompress 的「bad password」语义，
    /// 否则 UI 的密码启发式（<c>PasswordService.IsPasswordError</c> 找 "password"）认不出来，
    /// 会把「密码错」误报成「压缩包损坏」。
    /// </summary>
    [Fact]
    public void OpenReadStream_WrongPassword_ThrowsInvalidFormatWithPasswordSignal()
    {
        var path = Fixture("aes-gbk.zip");
        var (package, archive, entry) = OpenEntry(path, Password);
        using (package)
        using (archive)
        {
            var ex = Assert.Throws<InvalidFormatException>(
                () => ZipEntryAccess.OpenReadStream(path, entry, "测试A124"));
            Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OpenReadStream_EmptyPassword_FallsBackToOfficialPath()
    {
        var path = Fixture("aes-gbk.zip");
        var (package, archive, entry) = OpenEntry(path, null);
        using (package)
        using (archive)
        {
            // 无密码 → 不接管，交给 SharpCompress（它自己抛「密码错误」），
            // 保证既有「加密包未输密码」提示链路不变
            Assert.ThrowsAny<Exception>(() =>
            {
                using var stream = ZipEntryAccess.OpenReadStream(path, entry, null);
                stream.ReadByte();
            });
        }
    }

    [Fact]
    public void IsWinZipAesEntry_NonAesArchive_ReturnsFalse()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipAesTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var plain = Path.Combine(dir, "plain.zip");
            using (var fs = File.Create(plain))
            using (var zip = new SysZipArchive(fs, SysZipArchiveMode.Create))
            {
                var e = zip.CreateEntry("a.txt");
                using var s = e.Open();
                s.WriteByte((byte)'x');
            }

            Assert.Null(ZipEntryAccess.FindAnyAesEntryName(plain));
            Assert.False(ZipEntryAccess.IsWinZipAesEntry(plain, "a.txt"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 清理失败不影响断言 */ }
        }
    }

    // ── 纯函数：候选密码字节 / 扩展字段解析 / 长度推导 ──────────

    [Fact]
    public void CandidatePasswordBytes_AsciiPassword_ReturnsSingleCandidate()
    {
        // ASCII 在 UTF-8 / GBK / 系统 ANSI 下字节完全相同 → 去重后只剩一个候选
        Assert.Single(ZipWinZipAes.CandidatePasswordBytes("abc123"));
    }

    [Fact]
    public void CandidatePasswordBytes_CjkPassword_CoversUtf8AndGbk()
    {
        var candidates = ZipWinZipAes.CandidatePasswordBytes(Password);
        var expected = new Dictionary<string, byte[]>
        {
            ["gbk"] = Encoding.GetEncoding(936).GetBytes(Password),
            // GB18030 对这几个汉字与 GBK 字面一致，会被去重；只要求两者至少各出现一次
        };

        Assert.True(candidates.Count >= 2, $"候选编码应覆盖 UTF-8 与 GBK，实际 {candidates.Count} 个");
        Assert.Contains(candidates, c => c.Bytes.SequenceEqual(Encoding.UTF8.GetBytes(Password)));
        Assert.Contains(candidates, c => c.Bytes.SequenceEqual(expected["gbk"]));
    }

    [Fact]
    public void TryReadExtraInfo_ParsesAesExtraField()
    {
        // 与语料一致：version=2 (AE-2), vendor="AE", strength=3 (AES-256), actualMethod=8 (Deflate)
        byte[] extra =
        [
            0x01, 0x99, 0x07, 0x00,       // id=0x9901, size=7
            0x02, 0x00,                   // version 2
            (byte)'A', (byte)'E',         // vendor
            0x03,                         // strength 3 = AES-256
            0x08, 0x00,                   // actual method 8 (Deflate)
        ];

        Assert.True(ZipWinZipAes.TryReadExtraInfo(extra, out var info));
        Assert.Equal(2, info.Version);
        Assert.Equal(3, info.Strength);
        Assert.Equal((ushort)8, info.ActualMethod);
    }

    [Fact]
    public void TryReadExtraInfo_MissingOrShortExtraField_ReturnsFalse()
    {
        Assert.False(ZipWinZipAes.TryReadExtraInfo(null, out _));
        Assert.False(ZipWinZipAes.TryReadExtraInfo([], out _));
        // 只有 4 字节头但没有 payload
        Assert.False(ZipWinZipAes.TryReadExtraInfo([0x01, 0x99, 0x07, 0x00], out _));
    }

    [Theory]
    [InlineData(1, 8, 16)]
    [InlineData(2, 12, 24)]
    [InlineData(3, 16, 32)]
    public void SaltAndKeyLength_MatchWinZipAesSpec(int strength, int expectedSalt, int expectedKey)
    {
        Assert.Equal(expectedSalt, ZipWinZipAes.SaltLength(strength));
        Assert.Equal(expectedKey, ZipWinZipAes.KeyLength(strength));
    }

    [Fact]
    public void SaltAndKeyLength_UnknownStrength_ReturnsZero()
    {
        // 0 = 非法强度，调用方据此回落到 SharpCompress（不会更差）
        Assert.Equal(0, ZipWinZipAes.SaltLength(0));
        Assert.Equal(0, ZipWinZipAes.KeyLength(4));
    }

    [Fact]
    public void Crc32_MatchesKnownVectors()
    {
        // 标准向量：CRC32("123456789") = 0xCBF43926
        Assert.Equal(0xCBF43926u, Crc32Of("123456789"u8));
        // 空输入 = 0，避免累加流初值写错时静默通过
        Assert.Equal(0u, Crc32Of([]));
    }

    private static uint Crc32Of(ReadOnlySpan<byte> data) => ZipWinZipAes.Crc32(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;
}
