using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 解压设置窗口逐包校验的异常分类与行模型状态联动测试。
/// 加密文件名的 7z 包无密码时抛密码类异常，必须归类为「需密码」而非「损坏」。
/// 图标断言使用 AppIcons.axaml 矢量资源键（与树节点图标体系一致）。
/// </summary>
public class SourceArchiveValidationTests
{
    [Theory]
    [InlineData("Wrong password for 'a.zip'")]
    [InlineData("Archive is encrypted and cannot be opened")]
    [InlineData("此压缩包已加密，请输入密码")]
    [InlineData("Can not open the archive as encrypted header requires password")]
    public void IsPasswordRelatedError_PasswordKeywords_ReturnsTrue(string message)
        => Assert.True(ArchiveService.IsPasswordRelatedError(new InvalidOperationException(message)));

    [Theory]
    [InlineData("Could not find the central directory")]
    [InlineData("Stream is too short")]
    [InlineData("文件头损坏")]
    public void IsPasswordRelatedError_CorruptionMessages_ReturnsFalse(string message)
        => Assert.False(ArchiveService.IsPasswordRelatedError(new InvalidOperationException(message)));

    // 损坏指标优先于密码指标：7z.dll 错误消息有限且复用，损坏场景可能同时含 password/encrypted 字样
    // （如 "CRC Failed in Encrypted file. Wrong password?"）——打开阶段尚未尝试密码，不应弹密码框
    [Theory]
    [InlineData("CRC Failed in Encrypted file. Wrong password?")]
    [InlineData("Data Error in file. Wrong password?")]
    [InlineData("The archive is corrupt: data error at offset 1234")]
    [InlineData("Cannot open the file as archive: encrypted header looks damaged")]
    public void IsPasswordRelatedError_CorruptionWithPasswordKeywords_ReturnsFalse(string message)
        => Assert.False(ArchiveService.IsPasswordRelatedError(new InvalidOperationException(message)));

    /// <summary>
    /// SharpSevenZip 对「7z/RAR 加密头无密码」与「7z/RAR 损坏」抛同一条歧义消息，
    /// 传入路径时靠头部结构探测区分（方案 B）。语料见 tests/corpus/corrupt-archives/。
    /// </summary>
    private const string AmbiguousSevenZipMessage =
        "Invalid archive: open/read error! Is it encrypted and a wrong password was provided? " +
        "If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format.";

    [Fact]
    public void IsPasswordRelatedError_AmbiguousMessage_Corrupt7z_ReturnsFalse()
        => Assert.False(ArchiveService.IsPasswordRelatedError(
            new InvalidOperationException(AmbiguousSevenZipMessage),
            CorpusPath("corrupt-14-7z-truncated-tail.7z")));

    [Fact]
    public void IsPasswordRelatedError_AmbiguousMessage_EncryptedHeader7z_ReturnsTrue()
        => Assert.True(ArchiveService.IsPasswordRelatedError(
            new InvalidOperationException(AmbiguousSevenZipMessage),
            CorpusPath("enc-04-7z-encrypt-headers.7z")));

    [Fact]
    public void IsPasswordRelatedError_AmbiguousMessage_CorruptRar_ReturnsFalse()
        => Assert.False(ArchiveService.IsPasswordRelatedError(
            new InvalidOperationException(AmbiguousSevenZipMessage),
            CorpusPath("corrupt-24-rar-bad-signature.rar")));

    [Fact]
    public void IsPasswordRelatedError_AmbiguousMessage_EncryptedHeaderRar_ReturnsTrue()
        => Assert.True(ArchiveService.IsPasswordRelatedError(
            new InvalidOperationException(AmbiguousSevenZipMessage),
            CorpusPath("enc-06-rar-headers-encrypted.rar")));

    [Fact]
    public void IsPasswordRelatedError_PasswordMessage_NoPath_ReturnsTrue()
    {
        // 旧签名（无路径）不做结构探测，保持纯消息启发式
        Assert.True(ArchiveService.IsPasswordRelatedError(
            new InvalidOperationException("Wrong password for 'a.zip'")));
    }

    private static string CorpusPath(string fileName) =>
        Path.Combine(GetRepoRoot(), "tests", "corpus", "corrupt-archives", fileName);

    /// <summary>从测试程序集输出目录向上遍历，找到第一个含 src 子目录的目录作为仓库根。</summary>
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            $"无法定位仓库根（未找到 'src' 目录）。Base directory: {AppContext.BaseDirectory}");
    }

    [Fact]
    public void SourceArchiveItem_StatusChange_UpdatesIcon()
    {
        var item = new SourceArchiveItem(@"D:\test\a.zip");
        Assert.Equal(SourceArchiveStatus.Pending, item.Status);
        Assert.Equal("IconTimer", item.StatusIconKey);

        item.Status = SourceArchiveStatus.Validating;
        Assert.Equal("IconArchiveClock", item.StatusIconKey);

        item.Status = SourceArchiveStatus.Ok;
        Assert.Equal("IconCheckmark", item.StatusIconKey);

        item.Status = SourceArchiveStatus.NeedsPassword;
        Assert.Equal("IconLockClosed", item.StatusIconKey);

        item.Status = SourceArchiveStatus.Failed
            ;
        Assert.Equal("IconWarning", item.StatusIconKey);
        Assert.Equal("ConflictRed", item.StatusForegroundKey);
    }

    [Fact]
    public void SourceArchiveItem_DisplayName_IsFileNameOnly()
    {
        var item = new SourceArchiveItem(@"D:\archives\my pack\b.7z");
        Assert.Equal(@"D:\archives\my pack\b.7z", item.Path);
        Assert.Equal("b.7z", item.DisplayName);
    }

    [Fact]
    public void SourceArchiveItem_EncryptedStates_DeriveKeyAndUnlockIcons()
    {
        // B 类：列出成功但加密未匹配 → 钥匙（蓝色）
        var item = new SourceArchiveItem(@"D:\t\e.zip")
        {
            Status = SourceArchiveStatus.Ok,
            IsEncrypted = true,
        };
        Assert.Equal("IconKey", item.StatusIconKey);
        Assert.Equal("Blue", item.StatusForegroundKey);

        // 匹配/手动输对 → 开锁（黄色）
        item.SetMatched("pwd", "desc");
        Assert.Equal("IconLockOpen", item.StatusIconKey);
        Assert.Equal("Yellow", item.StatusForegroundKey);

        // 普通无加密包不受影响
        var plain = new SourceArchiveItem(@"D:\t\f.zip") { Status = SourceArchiveStatus.Ok };
        Assert.Equal("IconCheckmark", plain.StatusIconKey);
        Assert.Equal("Green", plain.StatusForegroundKey);
    }

    [Fact]
    public void IsUnlockable_LockedOrEncryptedUnmatched_ReturnsTrue()
    {
        var locked = new SourceArchiveItem(@"D:\t\a.7z") { Status = SourceArchiveStatus.NeedsPassword };
        Assert.True(ExtractSettingsViewModel.IsUnlockable(locked));

        var encryptedUnmatched = new SourceArchiveItem(@"D:\t\b.zip")
        {
            Status = SourceArchiveStatus.Ok,
            IsEncrypted = true,
        };
        Assert.True(ExtractSettingsViewModel.IsUnlockable(encryptedUnmatched));

        var unlocked = new SourceArchiveItem(@"D:\t\c.zip")
        {
            Status = SourceArchiveStatus.Ok,
            IsEncrypted = true,
            MatchedPassword = "pwd",
        };
        Assert.False(ExtractSettingsViewModel.IsUnlockable(unlocked));

        var plain = new SourceArchiveItem(@"D:\t\d.zip") { Status = SourceArchiveStatus.Ok };
        Assert.False(ExtractSettingsViewModel.IsUnlockable(plain));

        Assert.False(ExtractSettingsViewModel.IsUnlockable(null));
    }
}
