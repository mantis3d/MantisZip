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
