using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

public class CompressSettingsViewModelTests
{
    [Fact]
    public void Constructor_WithEmptyPaths_ShowsNoFilesSelected()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        Assert.Equal("未选择文件", vm.SelectedPathsSummary);
    }

    [Fact]
    public void Constructor_WithPaths_ShowsCorrectCount()
    {
        var paths = new[] { "file1.txt", "file2.zip" };
        var vm = new CompressSettingsViewModel(paths);
        Assert.Contains("2 项", vm.SelectedPathsSummary);
        Assert.Equal(2, vm.SelectedPaths.Count);
    }

    [Fact]
    public void DefaultValues_AreSensible()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        Assert.Equal("zip", vm.DefaultFormat);
        Assert.Equal(5, vm.CompressionLevel);
        Assert.Null(vm.OutputPath);
        Assert.Null(vm.Password);
        Assert.False(vm.Encrypt);
    }

    [Fact]
    public void AdvancedFormatOptions_Constructor_MirrorsAppSettings()
    {
        // 高级格式选项在构造函数中从 AppSettings 读取默认值，
        // 保证对话框初始值与设置窗口一致（此后用户修改仅影响本次压缩）
        var s = AppSettings.Load();
        var vm = new CompressSettingsViewModel(Array.Empty<string>());

        Assert.Equal(s.ZipEncoding ?? "utf-8", vm.FileNameEncoding);
        Assert.Equal(s.ZipCompressionMethod ?? "deflate", vm.ZipCompressionMethod);
        Assert.Equal(s.SevenZipCompressionMethod ?? "LZMA2", vm.SevenZipCompressionMethod);
        Assert.Equal(s.SevenZipSolid, vm.SevenZipSolid);
        Assert.Equal(s.SevenZipSolidBlockSize ?? "", vm.SevenZipSolidBlockSize);
        Assert.Equal(s.SevenZipDictionarySize, vm.SevenZipDictionarySize);
        Assert.Equal(s.SevenZipNumFastBytes, vm.SevenZipNumFastBytes);
        Assert.Equal(s.SevenZipMatchFinder ?? "", vm.SevenZipMatchFinder);
        Assert.Equal(s.ZipEncryptionMethod ?? "aes256", vm.ZipEncryptionMethod);
        Assert.Equal(s.SevenZipEncryptHeaders, vm.SevenZipEncryptHeaders);
    }

    [Fact]
    public void AdvancedFormatOptions_AreSettable()
    {
        // 请求构建点（ExecuteCompressFromSettings / CLI）从这些属性读取，
        // 验证它们可被对话框快照写入并原样读出
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        vm.FileNameEncoding = "gbk";
        vm.ZipCompressionMethod = "deflate64";
        vm.SevenZipCompressionMethod = "PPMd";
        vm.SevenZipSolid = false;
        vm.SevenZipSolidBlockSize = "64m";
        vm.SevenZipDictionarySize = 64 * 1024 * 1024;
        vm.SevenZipNumFastBytes = 128;
        vm.SevenZipMatchFinder = "bt4";
        vm.ZipEncryptionMethod = "zipcrypto";
        vm.SevenZipEncryptHeaders = false;

        Assert.Equal("gbk", vm.FileNameEncoding);
        Assert.Equal("deflate64", vm.ZipCompressionMethod);
        Assert.Equal("PPMd", vm.SevenZipCompressionMethod);
        Assert.False(vm.SevenZipSolid);
        Assert.Equal("64m", vm.SevenZipSolidBlockSize);
        Assert.Equal(64 * 1024 * 1024, vm.SevenZipDictionarySize);
        Assert.Equal(128, vm.SevenZipNumFastBytes);
        Assert.Equal("bt4", vm.SevenZipMatchFinder);
        Assert.Equal("zipcrypto", vm.ZipEncryptionMethod);
        Assert.False(vm.SevenZipEncryptHeaders);
    }

    [Fact]
    public void PasswordStrength_Empty_ReturnsNone()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        vm.Password = "";
        Assert.Equal("无", vm.PasswordStrength);
    }

    [Theory]
    [InlineData("ab", "弱")]
    [InlineData("abcd1234", "中")]
    [InlineData("Str0ng!Pass#2024", "强")]
    public void PasswordStrength_VariousInputs_ReturnsExpected(string password, string expected)
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        vm.Password = password;
        Assert.Equal(expected, vm.PasswordStrength);
    }

    [Fact]
    public void PasswordsMatch_WhenEqual_ReturnsTrue()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        vm.Password = "secret123";
        vm.ConfirmPassword = "secret123";
        Assert.True(vm.PasswordsMatch);
    }

    [Fact]
    public void PasswordsMatch_WhenDifferent_ReturnsFalse()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        vm.Password = "secret123";
        vm.ConfirmPassword = "different";
        Assert.False(vm.PasswordsMatch);
    }

    [Fact]
    public void CommentDistribution_DefaultsToAllSame()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        Assert.Equal(CommentDistribution.AllSame, vm.CommentDistribution);
        Assert.True(vm.CommentAllSame);
        Assert.False(vm.CommentFirstOnly);
        Assert.False(vm.CommentPerLine);
    }

    [Fact]
    public void SettingCommentFirstOnly_UpdatesDistribution()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        vm.CommentFirstOnly = true;
        Assert.Equal(CommentDistribution.FirstOnly, vm.CommentDistribution);
        Assert.False(vm.CommentAllSame);
        Assert.False(vm.CommentPerLine);
    }

    [Fact]
    public void SettingCommentPerLine_UpdatesDistribution()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        vm.CommentPerLine = true;
        Assert.Equal(CommentDistribution.PerLine, vm.CommentDistribution);
        Assert.False(vm.CommentAllSame);
        Assert.False(vm.CommentFirstOnly);
    }

    [Fact]
    public async Task BrowseOutputCommand_CallsCallback()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        var called = false;
        vm.BrowseOutput = () =>
        {
            called = true;
            return Task.FromResult<string?>("C:\\test.zip");
        };
        await vm.BrowseOutputPathCommand.ExecuteAsync(null);
        Assert.True(called);
        Assert.Equal("C:\\test.zip", vm.OutputPath);
    }

    [Fact]
    public async Task StartCompressCommand_WithEncryptAndMismatch_DoesNotClose()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        vm.Encrypt = true;
        vm.Password = "abc";
        vm.ConfirmPassword = "xyz";
        var closeCalled = false;
        vm.CloseAction = async (result) => { closeCalled = true; await Task.CompletedTask; };
        await vm.StartCompressCommand.ExecuteAsync(null);
        Assert.False(closeCalled);
    }

    [Fact]
    public async Task StartCompressCommand_WithoutEncrypt_CallsCloseAction()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        var closeCalled = false;
        vm.CloseAction = async (result) => { closeCalled = true; await Task.CompletedTask; };
        await vm.StartCompressCommand.ExecuteAsync(null);
        Assert.True(closeCalled);
    }

    [Fact]
    public async Task CancelCommand_CallsCloseActionWithFalse()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        var closeResult = true;
        vm.CloseAction = async (result) => { closeResult = result; await Task.CompletedTask; };
        await vm.CancelCommand.ExecuteAsync(null);
        Assert.False(closeResult);
    }
}
