using System.Text;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// WinZip AES 「占位密码 + 就地换密钥」回归（issue #92）。
///
/// <para>
/// 为什么需要换密钥：7z.dll 写 ZIP + AES 时要把密码转成系统 ANSI，密码含任意 &gt;127
/// 的字符就抛 <c>SharpSevenZipException: Invalid arguments provided. (0x80070057:
/// E_INVALIDARG)</c>，「ZIP + AES + 中文密码」根本压不出包。SharpSevenZip 的所有 Compress*
/// 都只收 string，没有密码字节入口。于是先用纯 ASCII 占位密码压出包，再在字节层把 AES
/// 密钥换成真实密码 —— WinZip AES 是纯 CTR keystream，换密钥不需要解压。
/// </para>
/// </summary>
public class ZipWinZipAesRekeyTests : IDisposable
{
    private readonly string _work;
    private readonly ZipEngine _engine = new();

    public ZipWinZipAesRekeyTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _work = Path.Combine(Path.GetTempPath(), "MantisZipRekeyTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* 清理失败不影响断言 */ }
        GC.SuppressFinalize(this);
    }

    private static bool Is7zDllAvailable
    {
        get
        {
            if (!File.Exists(SevenZipEngine.SevenZipDllPath))
                return false;
            try
            {
                SevenZipEngine.EnsureLibraryPath();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private async Task<string> CreateSourceFileAsync(string content)
    {
        var file = Path.Combine(_work, "source", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content, new UTF8Encoding(false));
        return file;
    }

    // ── NeedsRekey ──────────────────────────────────────────────

    [Theory]
    [InlineData("abc123", false)]
    [InlineData("", false)]
    [InlineData("P@ssw0rd!#$%^&*()", false)]  // 可打印 ASCII 全部 95 个字符，7z.dll 能自己处理
    [InlineData("测试A123", true)]
    [InlineData("café", true)]
    [InlineData("Пароль", true)]
    [InlineData("😀", true)]
    public void NeedsRekey_DetectsNonAsciiPassword(string password, bool expected)
        => Assert.Equal(expected, ZipWinZipAesRekey.NeedsRekey(password));

    // ── 换密钥本身的正确性 ──────────────────────────────────────

    [Fact]
    public void Rekey_ChinesePassword_KeepsArchiveLengthAndLetsOwnReaderDecode()
    {
        if (!Is7zDllAvailable)
            return;

        var content = "rekey roundtrip";
        var sourceFile = Path.Combine(_work, "rekey", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, content, new UTF8Encoding(false));

        var archive = Path.Combine(_work, "rekey.zip");
        CreatePlaceholderArchive(archive, sourceFile);
        long before = new FileInfo(archive).Length;

        ZipWinZipAesRekey.Rekey(archive, ZipWinZipAesRekey.PlaceholderPassword, "测试A123");

        // 长度必须不变（原地改写密文，不动目录与结构）
        Assert.Equal(before, new FileInfo(archive).Length);

        // 占位密码不再能通过 verifier
        Assert.False(ZipEntryAccess.VerifyAesPassword(archive, "a.txt", ZipWinZipAesRekey.PlaceholderPassword));
        // 真实密码能通过
        Assert.True(ZipEntryAccess.VerifyAesPassword(archive, "a.txt", "测试A123"));
    }

    [Fact]
    public void Rekey_WrongPlaceholderPassword_ThrowsInsteadOfCorruptingArchive()
    {
        if (!Is7zDllAvailable)
            return;

        var sourceFile = Path.Combine(_work, "bad", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, "x", new UTF8Encoding(false));

        var archive = Path.Combine(_work, "bad.zip");
        CreatePlaceholderArchive(archive, sourceFile);
        var original = File.ReadAllBytes(archive);

        Assert.Throws<SharpCompress.Common.InvalidFormatException>(() =>
            ZipWinZipAesRekey.Rekey(archive, "not-the-placeholder", "测试A123"));

        // 校验失败必须在写任何字节之前抛出：包内容保持不变
        Assert.Equal(original, File.ReadAllBytes(archive));
    }

    [Fact]
    public void Rekey_ZipCryptoEntry_ThrowsNotSupported()
    {
        if (!Is7zDllAvailable)
            return;

        var sourceFile = Path.Combine(_work, "zc", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, "x", new UTF8Encoding(false));

        var archive = Path.Combine(_work, "zipcrypto.zip");
        new SharpSevenZip.SharpSevenZipCompressor
        {
            ArchiveFormat = SharpSevenZip.OutArchiveFormat.Zip,
            ZipEncryptionMethod = SharpSevenZip.ZipEncryptionMethod.ZipCrypto,
            DirectoryStructure = true,
        }.CompressFilesEncrypted(archive, "abc123", [sourceFile]);

        Assert.Throws<NotSupportedException>(() =>
            ZipWinZipAesRekey.Rekey(archive, "abc123", "测试A123"));
    }

    // ── 引擎端到端 ──────────────────────────────────────────────

    [Theory]
    [InlineData("aes256")]
    [InlineData("aes192")]
    [InlineData("aes128")]
    public async Task CompressAsync_ChinesePassword_ProducesArchiveReadableByOwnEngine(string encryption)
    {
        if (!Is7zDllAvailable)
            return;

        const string password = "测试A123";
        var content = "hello " + new string('z', 3000);
        var sourceFile = await CreateSourceFileAsync(content);
        var archive = Path.Combine(_work, $"{encryption}.zip");

        await _engine.CompressAsync(
            [sourceFile], archive,
            new ArchiveOptions
            {
                Encrypt = true,
                Password = password,
                ZipEncryptionMethod = encryption,
                ZipCompressionMethod = "deflate",
            },
            null, default);

        var dest = Path.Combine(_work, "out-" + encryption);
        var result = await _engine.ExtractAsync(archive, dest, password, null, default,
            new ArchiveOptions { ParallelExtractDegree = 1 });

        var file = Assert.Single(Directory.GetFiles(dest, "*", SearchOption.AllDirectories));
        Assert.Equal(content, await File.ReadAllTextAsync(file, new UTF8Encoding(false)));
        Assert.Equal(0, result.FailedEntries);

        // 真实密码生效、占位密码失效 —— 否则等于把密码留在包里
        var entryName = Assert.Single(await _engine.ListEntriesAsync(archive, password)).Name;
        Assert.False(ZipEntryAccess.VerifyAesPassword(
            archive, entryName, ZipWinZipAesRekey.PlaceholderPassword));
    }

    [Theory]
    [InlineData("zipcrypto")]
    public async Task CompressAsync_ChinesePasswordWithZipCrypto_ThrowsActionableError(string encryption)
    {
        var sourceFile = await CreateSourceFileAsync("x");
        var archive = Path.Combine(_work, "zc-out.zip");

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _engine.CompressAsync(
                [sourceFile], archive,
                new ArchiveOptions
                {
                    Encrypt = true,
                    Password = "测试A123",
                    ZipEncryptionMethod = encryption,
                },
                null, default));

        // 消息必须说清「怎么办」，且是中文（界面直接透传，不再包一层「操作失败：」）
        Assert.Contains("AES", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ASCII", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompressAsync_AsciiPassword_DoesNotGoThroughRekey()
    {
        if (!Is7zDllAvailable)
            return;

        var sourceFile = await CreateSourceFileAsync("plain");
        var archive = Path.Combine(_work, "ascii.zip");

        await _engine.CompressAsync(
            [sourceFile], archive,
            new ArchiveOptions { Encrypt = true, Password = "abc123", ZipEncryptionMethod = "aes256" },
            null, default);

        // 直接交给 7z.dll 的密码就是真实密码，不该有「占位密码」痕迹
        var entryName = Assert.Single(await _engine.ListEntriesAsync(archive, "abc123")).Name;
        Assert.True(ZipEntryAccess.VerifyAesPassword(archive, entryName, "abc123"));
        Assert.False(ZipEntryAccess.VerifyAesPassword(
            archive, entryName, ZipWinZipAesRekey.PlaceholderPassword));
    }

    private static void CreatePlaceholderArchive(string archivePath, string sourceFile)
    {
        new SharpSevenZip.SharpSevenZipCompressor
        {
            ArchiveFormat = SharpSevenZip.OutArchiveFormat.Zip,
            ZipEncryptionMethod = SharpSevenZip.ZipEncryptionMethod.Aes256,
            DirectoryStructure = true,
        }.CompressFilesEncrypted(
            archivePath, ZipWinZipAesRekey.PlaceholderPassword, [sourceFile]);
    }
}
