using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

public class ExtractSettingsViewModelTests
{
    [Fact]
    public void Constructor_WithSingleArchive_SetsDefaultDestination()
    {
        var paths = new[] { @"C:\archives\test.zip" };
        var vm = new ExtractSettingsViewModel(paths);
        Assert.Equal(@"C:\archives\test", vm.DestinationPath);
    }

    [Fact]
    public void Constructor_WithEmptyPaths_EmptyDestination()
    {
        var vm = new ExtractSettingsViewModel(Array.Empty<string>());
        Assert.Equal("", vm.DestinationPath);
    }

    [Fact]
    public void DefaultConflictAction_IsAsk()
    {
        var vm = new ExtractSettingsViewModel(new[] { "test.zip" });
        Assert.Equal("ask", vm.ConflictAction);
    }

    [Fact]
    public void Constructor_MirrorsAppSettings_OpenFolderAfterExtract()
    {
        // 构造函数从 AppSettings 读取 OpenFolderAfterExtract（对齐 WPF LoadDefaultsFromSettings）。
        // 镜像断言：不改写真实 settings.json，与 CompressSettingsViewModelTests 同模式。
        var vm = new ExtractSettingsViewModel(new[] { "test.zip" });
        Assert.Equal(AppSettings.Load().OpenFolderAfterExtract, vm.OpenFolderAfterExtract);
    }

    [Fact]
    public async Task BrowseDestinationCommand_CallsCallback()
    {
        var vm = new ExtractSettingsViewModel(new[] { "test.zip" });
        var called = false;
        vm.BrowseFolder = () =>
        {
            called = true;
            return Task.FromResult<string?>(@"C:\output");
        };
        await vm.BrowseDestinationCommand.ExecuteAsync(null);
        Assert.True(called);
        Assert.Equal(@"C:\output", vm.DestinationPath);
    }

    [Fact]
    public async Task ExtractCommand_WithEmptyDestination_DoesNotCallClose()
    {
        var vm = new ExtractSettingsViewModel(Array.Empty<string>());
        var closeCalled = false;
        vm.CloseAction = async (result) => { closeCalled = true; await Task.CompletedTask; };
        await vm.ExtractCommand.ExecuteAsync(null);
        Assert.False(closeCalled);
    }

    [Fact]
    public async Task ExtractCommand_WithValidDestination_CallsClose()
    {
        var vm = new ExtractSettingsViewModel(new[] { "test.zip" });
        var closeResult = false;
        vm.CloseAction = async (result) => { closeResult = result; await Task.CompletedTask; };
        vm.DestinationPath = @"C:\out";
        await vm.ExtractCommand.ExecuteAsync(null);
        Assert.True(closeResult);
    }

    [Fact]
    public async Task CancelCommand_CallsCloseWithFalse()
    {
        var vm = new ExtractSettingsViewModel(new[] { "test.zip" });
        var closeResult = true;
        vm.CloseAction = async (result) => { closeResult = result; await Task.CompletedTask; };
        await vm.CancelCommand.ExecuteAsync(null);
        Assert.False(closeResult);
    }

    [Fact]
    public void ConflictAction_CanBeSet()
    {
        var vm = new ExtractSettingsViewModel(new[] { "test.zip" });
        vm.ConflictAction = "overwrite";
        Assert.Equal("overwrite", vm.ConflictAction);
        vm.ConflictAction = "rename";
        Assert.Equal("rename", vm.ConflictAction);
        vm.ConflictAction = "skip";
        Assert.Equal("skip", vm.ConflictAction);
    }
}
