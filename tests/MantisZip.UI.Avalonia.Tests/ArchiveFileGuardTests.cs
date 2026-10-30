using System;
using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「当前打开的压缩包被删 / 被替换」检测（issue：解压删源包后状态残留）。
/// 判定逻辑必须只看 <see cref="ArchiveFileGuard.Check"/> 的返回值 —— watcher 事件是异步的，
/// 测试里只用同步核对保证确定性。
/// </summary>
public sealed class ArchiveFileGuardTests : IDisposable
{
    private readonly string _tempDir;

    public ArchiveFileGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MantisZipGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // 文件仍被别的测试进程占用：临时目录由系统清理。
        }
    }

    [AvaloniaFact]
    public void TryCreate_MissingFile_ReturnsNull()
    {
        Assert.Null(ArchiveFileGuard.TryCreate(null));
        Assert.Null(ArchiveFileGuard.TryCreate(Path.Combine(_tempDir, "nope.zip")));
    }

    [AvaloniaFact]
    public void IntactFile_StaysAvailable()
    {
        var path = WriteArchive("a.zip", 128);

        using var guard = ArchiveFileGuard.TryCreate(path);

        Assert.NotNull(guard);
        Assert.Equal(ArchiveAvailability.Available, guard!.Current);
        Assert.Equal(ArchiveAvailability.Available, guard.Check());
    }

    [AvaloniaFact]
    public void DeletedFile_ReportsMissing_AndRaisesEventOnce()
    {
        var path = WriteArchive("b.zip", 128);
        using var guard = ArchiveFileGuard.TryCreate(path)!;

        var changes = 0;
        guard.AvailabilityChanged += (_, _) => changes++;

        File.Delete(path);

        Assert.Equal(ArchiveAvailability.Missing, guard.Check());
        Assert.Equal(1, changes);
        // 状态未再变化时不重复通知
        Assert.Equal(ArchiveAvailability.Missing, guard.Check());
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void RewrittenFile_ReportsReplaced()
    {
        var path = WriteArchive("c.zip", 128);
        using var guard = ArchiveFileGuard.TryCreate(path)!;

        // 同名文件被另一个包替换（长度变化）
        File.WriteAllBytes(path, new byte[256]);

        Assert.Equal(ArchiveAvailability.Replaced, guard.Check());
    }

    [AvaloniaFact]
    public void MissingSplitVolume_ReportsMissing()
    {
        var first = WriteArchive("d.zip.001", 64);
        var second = WriteArchive("d.zip.002", 64);
        using var guard = ArchiveFileGuard.TryCreate(first)!;

        Assert.Equal(ArchiveAvailability.Available, guard.Check());

        File.Delete(second);

        Assert.Equal(ArchiveAvailability.Missing, guard.Check());
    }

    [AvaloniaFact]
    public void DisposedGuard_StopsReporting()
    {
        var path = WriteArchive("e.zip", 128);
        var guard = ArchiveFileGuard.TryCreate(path)!;
        var changes = 0;
        guard.AvailabilityChanged += (_, _) => changes++;

        guard.Dispose();
        File.Delete(path);

        Assert.Equal(ArchiveAvailability.Available, guard.Check());
        Assert.Equal(0, changes);
    }

    [AvaloniaFact]
    public void ViewModel_AvailabilityMissing_ShowsBannerAndBlocksEntries()
    {
        var vm = new MainWindowViewModel
        {
            IsArchiveLoaded = true,
            CurrentArchivePath = Path.Combine(_tempDir, "gone.zip"),
        };

        vm.ArchiveAvailability = ArchiveAvailability.Missing;

        Assert.True(vm.IsArchiveUnavailable);
        Assert.False(vm.IsArchiveReplaced);
        Assert.False(vm.CanDeleteEntries);
        Assert.NotEmpty(vm.ArchiveAvailabilityTitleText);
        Assert.Contains("gone.zip", vm.ArchiveAvailabilityDetailText);

        // 入口守卫：文件不存在时直接拦下，并保持 Missing 状态
        Assert.False(vm.EnsureArchiveAvailable());
        Assert.Equal(ArchiveAvailability.Missing, vm.ArchiveAvailability);
    }

    [AvaloniaFact]
    public void ViewModel_AvailabilityReplaced_UsesReplacedWording()
    {
        var vm = new MainWindowViewModel
        {
            IsArchiveLoaded = true,
            CurrentArchivePath = Path.Combine(_tempDir, "swapped.zip"),
        };

        vm.ArchiveAvailability = ArchiveAvailability.Replaced;

        Assert.True(vm.IsArchiveUnavailable);
        Assert.True(vm.IsArchiveReplaced);
    }

    [AvaloniaFact]
    public void ViewModel_SyncAfterExtract_ClosesArchiveWhenSourceDeleted()
    {
        var path = WriteArchive("f.zip", 64);
        var vm = new MainWindowViewModel
        {
            IsArchiveLoaded = true,
            CurrentArchivePath = path,
        };

        // 源包还在：不应关包
        vm.SyncArchiveStateAfterExtract();
        Assert.True(vm.IsArchiveLoaded);

        File.Delete(path);
        vm.SyncArchiveStateAfterExtract();

        Assert.False(vm.IsArchiveLoaded);
        Assert.Null(vm.CurrentArchivePath);
        Assert.False(vm.IsArchiveUnavailable);
    }

    private string WriteArchive(string fileName, int length)
    {
        var path = Path.Combine(_tempDir, fileName);
        var bytes = new byte[length];
        Encoding.ASCII.GetBytes("PK").CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
