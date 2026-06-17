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
