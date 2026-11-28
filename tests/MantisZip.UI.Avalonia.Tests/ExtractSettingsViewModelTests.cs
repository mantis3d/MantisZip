using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

public class ExtractSettingsViewModelTests
{
    /// <summary>
    /// v0.5.8 有意变更：默认落点从「父目录\包名」改成「父目录」。
    /// <para>
    /// 包名那一层现在由根策略补（默认智能加根），单根包因此比旧默认少一层
    /// ——正是要消掉的那层重复嵌套（`backup.zip` 里的 `backup/photos/`）。
    /// </para>
    /// </summary>
    [Fact]
    public void Constructor_WithSingleArchive_SetsDefaultDestination()
    {
        var paths = new[] { @"C:\archives\test.zip" };
        var vm = new ExtractSettingsViewModel(paths);
        Assert.Equal(@"C:\archives", vm.DestinationPath);
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
        vm.ConfirmCompleted += _ => closeCalled = true;
        await vm.ExtractCommand.ExecuteAsync(null);
        Assert.False(closeCalled);
    }

    [Fact]
    public async Task ExtractCommand_WithValidDestination_CallsClose()
    {
        var vm = new ExtractSettingsViewModel(new[] { "test.zip" });
        ConfirmOutcome<ExtractSettings>? outcome = null;
        vm.ConfirmCompleted += o => outcome = o;
        vm.DestinationPath = @"C:\out";
        await vm.ExtractCommand.ExecuteAsync(null);
        Assert.IsType<Confirmed<ExtractSettings>>(outcome);
    }

    [Fact]
    public async Task CancelCommand_CallsCloseWithFalse()
    {
        var vm = new ExtractSettingsViewModel(new[] { "test.zip" });
        ConfirmOutcome<ExtractSettings>? outcome = null;
        vm.ConfirmCompleted += o => outcome = o;
        await vm.CancelCommand.ExecuteAsync(null);
        Assert.IsType<Cancelled<ExtractSettings>>(outcome);
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

    // ── 「智能去根」档位的可用性：判定必须与执行侧同源，且只认「真的能剥」的包 ──

    /// <summary>单根包：档位出现，提示行不显示。</summary>
    [Fact]
    public void CanStripRoot_WithSingleRootArchive_OffersSmartStrip()
    {
        var vm = new ExtractSettingsViewModel(new[] { @"C:\archives\test.zip" });
        vm.SetFirstArchiveEntries(new[]
        {
            File("photos/a.jpg"),
            File("photos/sub/b.jpg"),
        });

        Assert.True(vm.CanStripRoot);
        Assert.Contains(vm.ExtractRootModeOptions, o => o.Value == "SmartStrip");
        Assert.False(vm.IsExtractRootModeHintVisible);
    }

    /// <summary>
    /// 只有目录条目的包：<c>HasSingleRootDirectory</c> 为 true，但取不到可剥的名字。
    /// 这一档必须**不出现在下拉里**，否则就是「窗里能选、跑起来什么都不做」的死选项。
    /// </summary>
    [Fact]
    public void CanStripRoot_WithOnlyDirectoryEntries_HidesSmartStrip()
    {
        var vm = new ExtractSettingsViewModel(new[] { @"C:\archives\test.zip" });
        vm.SetFirstArchiveEntries(new[] { Dir("photos") });

        Assert.False(vm.CanStripRoot);
        Assert.DoesNotContain(vm.ExtractRootModeOptions, o => o.Value == "SmartStrip");
        Assert.True(vm.IsExtractRootModeHintVisible);
    }

    /// <summary>散装包（文件直接躺在顶层）同样不可剥。</summary>
    [Fact]
    public void CanStripRoot_WithLooseEntries_HidesSmartStrip()
    {
        var vm = new ExtractSettingsViewModel(new[] { @"C:\archives\test.zip" });
        vm.SetFirstArchiveEntries(new[] { File("a.txt"), File("b.txt") });

        Assert.False(vm.CanStripRoot);
        Assert.DoesNotContain(vm.ExtractRootModeOptions, o => o.Value == "SmartStrip");
    }

    /// <summary>
    /// 选中「智能去根」后包被换成一个不可剥的：档位消失时必须静默退回原样，
    /// 不能让 RootMode 停在一个下拉里已经不存在的值上。
    /// </summary>
    [Fact]
    public void RootMode_FallsBackToOriginal_WhenArchiveStopsBeingStrippable()
    {
        var vm = new ExtractSettingsViewModel(new[] { @"C:\archives\test.zip" });
        vm.SetFirstArchiveEntries(new[] { File("photos/a.jpg") });
        vm.RootMode = RootDirectoryMode.SmartStrip;
        Assert.Equal(RootDirectoryMode.SmartStrip, vm.RootMode);

        vm.SetFirstArchiveEntries(new[] { Dir("photos") });

        Assert.Equal(RootDirectoryMode.Original, vm.RootMode);
        Assert.Equal("Original", vm.SelectedExtractRootModeOption?.Value);
    }

    private static ArchiveItem File(string fullPath)
        => new() { FullPath = fullPath, Name = fullPath, Size = 1 };

    private static ArchiveItem Dir(string fullPath)
        => new() { FullPath = fullPath + "/", Name = fullPath, IsDirectory = true, Size = 0 };
}
