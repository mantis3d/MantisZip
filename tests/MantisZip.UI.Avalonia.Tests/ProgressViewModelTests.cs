using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

public class ProgressViewModelTests
{
    [Fact]
    public void Constructor_Defaults()
    {
        var vm = new ProgressViewModel();
        Assert.Equal(0, vm.PercentComplete);
        Assert.Equal(0, vm.FilePercentComplete);
        Assert.Null(vm.FileName);
        Assert.Null(vm.StatusMessage);
        Assert.False(vm.IsIndeterminate);
    }

    [Fact]
    public void SetProgress_UpdatesProperties()
    {
        var vm = new ProgressViewModel();
        var progress = new ArchiveProgress
        {
            PercentComplete = 50,
            FilePercentComplete = 75.0,
            CurrentFile = "file.txt",
        };

        vm.SetProgress(progress);

        Assert.Equal(50, vm.PercentComplete);
        Assert.Equal(75, vm.FilePercentComplete);
        Assert.Equal("file.txt", vm.FileName);
        Assert.False(vm.IsIndeterminate);
    }

    [Fact]
    public void SetProgress_NullFilePercent_SetsToZero()
    {
        var vm = new ProgressViewModel();
        var progress = new ArchiveProgress
        {
            PercentComplete = 30,
            FilePercentComplete = null,
            CurrentFile = "file.txt",
        };

        vm.SetProgress(progress);

        Assert.Equal(30, vm.PercentComplete);
        Assert.Equal(0, vm.FilePercentComplete);
        Assert.Equal("file.txt", vm.FileName);
        Assert.False(vm.IsIndeterminate);
    }

    [Fact]
    public void SetProgress_SetsFileName()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { CurrentFile = "test.bin" });
        Assert.Equal("test.bin", vm.FileName);
    }

    [Fact]
    public void StatusMessage_DirectSet()
    {
        var vm = new ProgressViewModel();
        vm.StatusMessage = "Working...";
        Assert.Equal("Working...", vm.StatusMessage);
    }

    [Fact]
    public void CancelCommand_TriggersCancellation()
    {
        var vm = new ProgressViewModel();
        vm.InitCancellation();
        Assert.False(vm.CancellationToken.IsCancellationRequested);

        vm.CancelCommand.Execute(null);

        Assert.True(vm.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void CancelCommand_WithoutInit_DoesNotThrow()
    {
        var vm = new ProgressViewModel();
        var exception = Record.Exception(() => vm.CancelCommand.Execute(null));
        Assert.Null(exception);
    }

    [Fact]
    public void PercentComplete_ClampsTo100()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { PercentComplete = 150 });
        Assert.Equal(100, vm.PercentComplete);
    }

    [Fact]
    public void PercentComplete_Negative_ClampsTo0()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { PercentComplete = -10 });
        Assert.Equal(0, vm.PercentComplete);
    }
}
