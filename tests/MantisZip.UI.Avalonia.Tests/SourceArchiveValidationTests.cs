using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 解压设置窗口逐包校验的异常分类与行模型状态联动测试。
/// 加密文件名的 7z 包无密码时抛密码类异常，必须归类为「需密码」而非「损坏」。
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
        Assert.Equal("·", item.StatusIcon);

        item.Status = SourceArchiveStatus.Validating;
        Assert.Equal("⏳", item.StatusIcon);

        item.Status = SourceArchiveStatus.Ok;
        Assert.Equal("✅", item.StatusIcon);

        item.Status = SourceArchiveStatus.NeedsPassword;
        Assert.Equal("🔒", item.StatusIcon);

        item.Status = SourceArchiveStatus.Failed;
        Assert.Equal("⚠️", item.StatusIcon);
    }

    [Fact]
    public void SourceArchiveItem_DisplayName_IsFileNameOnly()
    {
        var item = new SourceArchiveItem(@"D:\archives\my pack\b.7z");
        Assert.Equal(@"D:\archives\my pack\b.7z", item.Path);
        Assert.Equal("b.7z", item.DisplayName);
    }
}
