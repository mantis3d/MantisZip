using System.Text;
using MantisZip.Core.Engines;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// issue #91 的 UI 层回归：密码框的第一个判定（<see cref="PasswordService.QuickVerifyPassword"/>）
/// 必须能接受 GBK 编码的中文密码。
/// <para>
/// 修复前 ZIP 分支直接 <c>ZipArchive.OpenArchive(...).OpenEntryStream()</c>，
/// 密码字节永远是 UTF-8（SharpCompress 写死），GBK 包在这里被判「密码错误」，
/// 用户连解压都进不去。修复后 AES 条目走 2 字节 verifier 的候选编码探测。
/// </para>
/// </summary>
public class PasswordServiceWinZipAesTests
{
    private const string Password = "测试A123";

    static PasswordServiceWinZipAesTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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

    [Theory]
    [InlineData("aes-gbk.zip")]
    [InlineData("aes-utf8.zip")]
    public void QuickVerifyPassword_ChinesePassword_Succeeds(string archiveName)
    {
        var svc = new PasswordService();
        Assert.True(svc.QuickVerifyPassword(Fixture(archiveName), Password, new ZipEngine()));
    }

    [Fact]
    public void QuickVerifyPassword_GbkArchiveWithUtf8OnlyBytes_WouldFailWithoutProbe()
    {
        // GBK 包的密码字节只有 GBK 能表示：ASCII 或「UTF-8 目标串」都必须被判错，
        // 证明 QuickVerify 不是无条件 true（避免假阳性通过本组测试）。
        var svc = new PasswordService();
        Assert.False(svc.QuickVerifyPassword(Fixture("aes-gbk.zip"), "password", new ZipEngine()));
        Assert.False(svc.QuickVerifyPassword(Fixture("aes-gbk.zip"), "测试A124", new ZipEngine()));
    }

    [Theory]
    [InlineData("aes-gbk.zip")]
    [InlineData("aes-utf8.zip")]
    public void QuickVerifyPasswordEx_ChinesePassword_ReturnsSuccess(string archiveName)
    {
        var svc = new PasswordService();
        var info = svc.QuickVerifyPasswordEx(Fixture(archiveName), Password, new ZipEngine());
        Assert.Equal(PasswordVerificationResult.Success, info.Result);
    }

    [Fact]
    public void QuickVerifyPasswordEx_WrongPassword_ReturnsWrongPassword()
    {
        var svc = new PasswordService();
        var info = svc.QuickVerifyPasswordEx(Fixture("aes-gbk.zip"), "测试A124", new ZipEngine());

        // 必须是「密码错」而不是「损坏」：否则界面文案会误导用户以为是包坏了
        Assert.Equal(PasswordVerificationResult.WrongPassword, info.Result);
    }
}
