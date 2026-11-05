using System;
using System.Collections.Generic;
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
    public void SelfWriteScope_IgnoresOwnRewrite_AndRebasesBaseline()
    {
        var path = WriteArchive("g.zip", 128);
        using var guard = ArchiveFileGuard.TryCreate(path)!;
        var changes = 0;
        guard.AvailabilityChanged += (_, _) => changes++;

        using (guard.BeginSelfWrite())
        {
            // 本进程重写：长度 / 修改时间都变了，但自写期间不得判定成外部替换
            File.WriteAllBytes(path, new byte[256]);
            Assert.Equal(ArchiveAvailability.Available, guard.Check());
        }

        // 作用域结束：基线已换成自己写出的版本，后续刷新不会被旧快照拦住
        Assert.Equal(ArchiveAvailability.Available, guard.Current);
        Assert.Equal(ArchiveAvailability.Available, guard.Check());
        Assert.Equal(0, changes);

        // 之后外部再改，仍能正常报 Replaced（自写抑制没有把监视器弄哑）
        File.WriteAllBytes(path, new byte[512]);
        Assert.Equal(ArchiveAvailability.Replaced, guard.Check());
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void SelfWriteScope_DeletingArchive_ReportsMissingAfterRebase()
    {
        var path = WriteArchive("h.zip", 128);
        using var guard = ArchiveFileGuard.TryCreate(path)!;
        var reported = new List<ArchiveAvailability>();
        guard.AvailabilityChanged += (_, state) => reported.Add(state);

        using (guard.BeginSelfWrite())
        {
            // 删除全部条目时引擎会把整包删掉；自写期间同样不判定
            File.Delete(path);
            Assert.Equal(ArchiveAvailability.Available, guard.Check());
        }

        Assert.Equal(ArchiveAvailability.Missing, guard.Current);
        Assert.Equal(new[] { ArchiveAvailability.Missing }, reported);
    }

    [AvaloniaFact]
    public void NestedSelfWriteScopes_RebaseOnlyOnOutermostDispose()
    {
        var path = WriteArchive("i.zip", 128);
        using var guard = ArchiveFileGuard.TryCreate(path)!;

        var outer = guard.BeginSelfWrite();
        var inner = guard.BeginSelfWrite();
        File.WriteAllBytes(path, new byte[256]);

        // 内层释放后仍处于自写态：不判定、不把写到一半的状态当基线
        inner.Dispose();
        Assert.Equal(ArchiveAvailability.Available, guard.Check());

        outer.Dispose();
        Assert.Equal(ArchiveAvailability.Available, guard.Current);
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
    public void ViewModel_RefreshAllowsReplacedButStillBlocksMissing()
    {
        var path = WriteArchive("j.zip", 64);
        var vm = new MainWindowViewModel
        {
            IsArchiveLoaded = true,
            CurrentArchivePath = path,
        };

        vm.ArchiveAvailability = ArchiveAvailability.Replaced;

        // 写操作（添加 / 删除 / 解压）仍拦：不能拿旧列表往新文件里写
        Assert.False(vm.EnsureArchiveAvailable());
        // 刷新放行：刷新就是从磁盘重读，是替换态最安全的恢复动作
        Assert.True(vm.EnsureArchiveRefreshable());

        File.Delete(path);
        vm.ArchiveAvailability = ArchiveAvailability.Missing;
        Assert.False(vm.EnsureArchiveRefreshable());
    }

    [AvaloniaFact]
    public void ViewModel_GuardStatusClearedWhenAvailabilityRecovers()
    {
        var vm = new MainWindowViewModel();

        vm.ApplyArchiveAvailability(ArchiveAvailability.Replaced);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));

        // 包回到可用（外部还原 / 自写重设基线）：守卫写下的提示不能残留
        vm.ApplyArchiveAvailability(ArchiveAvailability.Available);
        Assert.Null(vm.StatusMessage);
    }

    [AvaloniaFact]
    public void ViewModel_GuardStatusRecoveryKeepsUnrelatedStatus()
    {
        var vm = new MainWindowViewModel();

        vm.ApplyArchiveAvailability(ArchiveAvailability.Missing);
        vm.StatusMessage = "other";

        vm.ApplyArchiveAvailability(ArchiveAvailability.Available);
        Assert.Equal("other", vm.StatusMessage);
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
