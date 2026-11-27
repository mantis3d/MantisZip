using System.Diagnostics;
using System.Text;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// WinZip AES 内层压缩方法的分派回归。
///
/// <para>
/// 背景：issue #91 的自研解密层最初只映射 Store(0) 与 Deflate(8)，其余一律抛
/// <c>NotSupportedException</c>。但 v0.5.7 之前的纯 SharpCompress 路径能读
/// Deflate64(9) / BZip2(12) / PPMd(98)，且在旧路径上 LZMA(14) 的「不支持」是
/// ZipCrypto 的限制、WinZip AES 下并不成立 —— 也就是说新路径会把这些包全部变成
/// 「解不开」，属于 #91 引入的回归。本类把六种方法逐个钉住。
/// </para>
///
/// <para>
/// 造包走 SharpSevenZip（7z.dll）；7z.dll 缺失时按仓库既有惯例跳过
/// （<see cref="SevenZipEngineTests"/> 的 <c>Is7zDllAvailable</c> 同款判据）。
/// </para>
/// </summary>
public class ZipEngineWinZipAesCompressionMethodTests : IDisposable
{
    private const string Password = "abc123";

    private readonly ZipEngine _engine = new();
    private readonly string _work;

    public ZipEngineWinZipAesCompressionMethodTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _work = Path.Combine(Path.GetTempPath(), "MantisZipAesMethodTest", Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("deflate")]
    [InlineData("deflate64")]
    [InlineData("bzip2")]
    [InlineData("lzma")]
    [InlineData("ppmd")]
    [InlineData("store")]
    public async Task ExtractAsync_AesEntryWithInnerCompressionMethod_DecodesContent(string method)
    {
        if (!Is7zDllAvailable)
            return;   // 7z.dll 缺失：本机无法造 AES 样本，跳过（仓库既有惯例）

        var content = "hello " + new string('z', 3000);
        var sourceFile = Path.Combine(_work, "source", $"{method}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, content, new UTF8Encoding(false));

        var archive = Path.Combine(_work, $"{method}.zip");
        await _engine.CompressAsync(
            [sourceFile],
            archive,
            new ArchiveOptions
            {
                Encrypt = true,
                Password = Password,
                ZipEncryptionMethod = "aes256",
                ZipCompressionMethod = method,
            },
            null,
            default);

        var dest = Path.Combine(_work, $"out-{method}");
        var result = await _engine.ExtractAsync(archive, dest, Password, null, default,
            new ArchiveOptions { ParallelExtractDegree = 1 });

        var files = Directory.GetFiles(dest, "*", SearchOption.AllDirectories);
        var file = Assert.Single(files);
        Assert.Equal(content, await File.ReadAllTextAsync(file, new UTF8Encoding(false)));
        Assert.Equal(1, result.SucceededEntries);
        Assert.Equal(0, result.FailedEntries);
    }

    /// <summary>
    /// 未知内层压缩方法必须给出<strong>可读</strong>的异常，而不是静默产出空文件。
    /// 用截断/篡改的 0x9901 actualMethod 复现：把 Deflate 包的
    /// 「实际压缩方法」字段改成 99（未实现），解密层应在开流时就拒绝。
    /// </summary>
    [Fact]
    public async Task OpenReadStream_UnknownInnerCompressionMethod_ThrowsNotSupportedWithMethodNumber()
    {
        if (!Is7zDllAvailable)
            return;

        var sourceFile = Path.Combine(_work, "unknown", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload", new UTF8Encoding(false));

        var archive = Path.Combine(_work, "unknown.zip");
        await _engine.CompressAsync(
            [sourceFile], archive,
            new ArchiveOptions { Encrypt = true, Password = Password, ZipEncryptionMethod = "aes256" },
            null, default);

        TamperActualCompressionMethod(archive, newMethod: 99);

        var dest = Path.Combine(_work, "out-unknown");
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _engine.ExtractAsync(archive, dest, Password, null, default,
                new ArchiveOptions { ParallelExtractDegree = 1 }));

        // 报错必须说清是哪个方法、怎么绕过，而不是让用户看到空文件
        Assert.Contains("99", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PPMd", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>把中央目录 0x9901 扩展字段里的 ActualMethod 改成指定值（就地改写）。</summary>
    private static void TamperActualCompressionMethod(string archivePath, ushort newMethod)
    {
        var bytes = File.ReadAllBytes(archivePath);

        int eocd = -1;
        for (int i = bytes.Length - 22; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(bytes, i) == 0x06054B50u) { eocd = i; break; }
        }
        Assert.True(eocd >= 0, "EOCD 未找到，样本包结构异常");

        int cd = (int)BitConverter.ToUInt32(bytes, eocd + 16);
        int nameLength = BitConverter.ToUInt16(bytes, cd + 28);
        int extraLength = BitConverter.ToUInt16(bytes, cd + 30);
        int extraStart = cd + 46 + nameLength;

        for (int pos = extraStart; pos + 4 <= extraStart + extraLength;)
        {
            ushort id = BitConverter.ToUInt16(bytes, pos);
            ushort size = BitConverter.ToUInt16(bytes, pos + 2);
            if (id == 0x9901 && size >= 7)
            {
                // payload: version(2) vendor(2) strength(1) actualMethod(2)
                bytes[pos + 4 + 5] = (byte)(newMethod & 0xFF);
                bytes[pos + 4 + 6] = (byte)(newMethod >> 8);
                File.WriteAllBytes(archivePath, bytes);
                return;
            }
            if (size == 0)
                break;
            pos += 4 + size;
        }

        Assert.Fail("未找到 0x9901 扩展字段");
    }
}
