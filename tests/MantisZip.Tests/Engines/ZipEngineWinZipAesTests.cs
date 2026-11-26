using System.Text;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// WinZip AES 中文密码的端到端回归（issue #91）。
/// <para>
/// 语料 <c>tests/corpus/winzip-aes/</c>：两个包内容相同、密码都是 <c>测试A123</c>，
/// 只有写入时的密码字节编码不同（<c>aes-gbk.zip</c> = GBK，<c>aes-utf8.zip</c> = UTF-8）。
/// 修复前 GBK 包在这三条链路上分别表现为：密码框判「密码错误」、解压「成功」但 0 文件、测试返回 false。
/// </para>
/// </summary>
public class ZipEngineWinZipAesTests : IDisposable
{
    private const string Password = "测试A123";
    private const string ExpectedContent = "MantisZip password encoding test\n";

    private readonly ZipEngine _engine = new();
    private readonly string _work;

    public ZipEngineWinZipAesTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _work = Path.Combine(Path.GetTempPath(), "MantisZipAesEngineTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* 清理失败不影响断言 */ }
        GC.SuppressFinalize(this);
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

    /// <summary>解压到本次测试专属子目录，返回该目录下所有文件路径。</summary>
    private async Task<string[]> ExtractAsync(string archiveName)
    {
        var dest = Path.Combine(_work, Path.GetFileNameWithoutExtension(archiveName));
        Directory.CreateDirectory(dest);
        // 并行度 1 → 走串行路径（单条目包本来也走串行）。
        await _engine.ExtractAsync(Fixture(archiveName), dest, Password, null, default,
            new ArchiveOptions { ParallelExtractDegree = 1 });
        return Directory.GetFiles(dest, "*", SearchOption.AllDirectories);
    }

    [Theory]
    [InlineData("aes-gbk.zip")]
    [InlineData("aes-utf8.zip")]
    public async Task ExtractAsync_ChinesePassword_WritesEntry(string archiveName)
    {
        var files = await ExtractAsync(archiveName);

        var file = Assert.Single(files);
        Assert.Equal("样本.txt", Path.GetFileName(file));
        Assert.Equal(ExpectedContent, await File.ReadAllTextAsync(file, new UTF8Encoding(false)));
    }

    [Theory]
    [InlineData("aes-gbk.zip")]
    [InlineData("aes-utf8.zip")]
    public async Task ListEntriesAsync_ChinesePassword_SeesEncryptedEntry(string archiveName)
    {
        var entries = await _engine.ListEntriesAsync(Fixture(archiveName), Password);

        var entry = Assert.Single(entries);
        Assert.Equal("样本.txt", entry.Name);
        Assert.True(entry.IsEncrypted);
    }

    [Theory]
    [InlineData("aes-gbk.zip")]
    [InlineData("aes-utf8.zip")]
    public async Task TestArchiveAsync_ChinesePassword_ReturnsTrue(string archiveName)
    {
        // 修复前 GBK 包恒为 false（密码字节不匹配 → 条目解密失败被计为失败条目）
        Assert.True(await _engine.TestArchiveAsync(Fixture(archiveName), Password));
    }

    [Fact]
    public async Task TestArchiveAsync_WrongPassword_ReturnsFalse()
    {
        Assert.False(await _engine.TestArchiveAsync(Fixture("aes-gbk.zip"), "测试A124"));
    }

    /// <summary>
    /// 「解压看起来成功、实际 0 文件」的静默失败回归：
    /// 输错密码时引擎**不抛异常**（逐条目失败计入返回值），必须由返回值的失败计数体现，
    /// 界面层据此报错（<c>ExtractOutcomeTracker.ThrowIfNothingWritten</c>）。
    /// </summary>
    [Fact]
    public async Task ExtractAsync_WrongPassword_WritesNothingAndReportsFailure()
    {
        var dest = Path.Combine(_work, "wrongpwd");
        Directory.CreateDirectory(dest);

        var result = await _engine.ExtractAsync(Fixture("aes-gbk.zip"), dest, "测试A124", null, default,
            new ArchiveOptions { ParallelExtractDegree = 1 });

        Assert.Empty(Directory.GetFiles(dest, "*", SearchOption.AllDirectories));
        Assert.Equal(0, result.SucceededEntries);
        Assert.True(result.FailedEntries > 0, "输错密码必须计入失败条目，否则界面会把 0 文件当成功");
        Assert.True(result.HasFailures);
    }
}
