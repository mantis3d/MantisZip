using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia.Threading;
using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>当前打开的压缩包在磁盘上的可操作状态。</summary>
public enum ArchiveAvailability
{
    /// <summary>尚未核对（刚打开、还没比对过）。</summary>
    Unknown = 0,

    /// <summary>文件（分卷集则是全部卷）与打开时一致，可以继续操作。</summary>
    Available,

    /// <summary>文件已被删除或移走；分卷集则是缺少某一卷。</summary>
    Missing,

    /// <summary>同一路径上的文件已被替换（长度 / 最后写入时间 / 卷数变了）。</summary>
    Replaced,
}

/// <summary>
/// 监视「当前打开的压缩包」在磁盘上的变化（被外部删除 / 移动 / 覆盖），把状态变化报告给主窗口。
/// </summary>
/// <remarks>
/// <para>
/// 背景（2026-10-30）：解压时勾选「解压完成后删除压缩包」后，主窗口仍保持「压缩包已打开」状态，
/// 之后解压 / 编辑 / 刷新全部落到一个已不存在的路径上；外部软件删除、杀毒隔离、网盘同步搬走、
/// 另一个解压工具「解压后删源包」也会造成同样的状态残留。
/// </para>
/// <para>
/// 检测方式：<see cref="FileSystemWatcher"/> 监听压缩包所在目录（只关心本包文件名 / 分卷前缀），
/// 事件到达后去抖再核对一次；UNC 路径或 watcher 创建失败时降级为定时轮询（默认 2 秒）。
/// </para>
/// <para>
/// 判定不只看「文件还在不在」，还比对打开时的快照（长度 + 最后写入时间 + 卷数）：
/// 因此「同名文件被换成另一个包」会报告 <see cref="ArchiveAvailability.Replaced"/>，
/// 不会被当成同一个包继续编辑。
/// </para>
/// <para>
/// 本进程自己重写压缩包（添加 / 删除条目、改注释）时必须用 <see cref="BeginSelfWrite"/> 包住：
/// 期间不判定外部变更，作用域结束时以磁盘现状重设基线；否则自己的写入会被误报成「被替换」，
/// 并让随后的刷新被入口守卫拦下。
/// </para>
/// </remarks>
public sealed class ArchiveFileGuard : IDisposable
{
    /// <summary>文件系统事件去抖间隔：一次删除/移动往往连着多个事件。</summary>
    private const int DebounceMs = 250;

    /// <summary>watcher 不可用时的兜底轮询间隔。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly string _archivePath;
    private readonly List<VolumeSnapshot> _snapshot = [];
    private readonly string? _watchDirectory;
    private readonly string? _watchFilter;
    private readonly HashSet<string> _watchedFileNames = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounceTimer;
    private DispatcherTimer? _pollTimer;
    private bool _disposed;

    /// <summary>
    /// 自写嵌套深度：本进程正在重写压缩包期间 &gt; 0。
    /// 写包必然改动长度 / 修改时间（临时文件替换时文件还会短暂消失），此时不能判定成外部变更。
    /// </summary>
    private int _selfWriteDepth;

    private ArchiveFileGuard(string archivePath)
    {
        _archivePath = archivePath;
        CaptureSnapshot();

        if (_snapshot.Count == 0)
        {
            Current = ArchiveAvailability.Missing;
            return;
        }

        Current = ArchiveAvailability.Available;

        _watchDirectory = Path.GetDirectoryName(_snapshot[0].Path);
        // 分卷要监听整组（name.zip.001 … / name.part*.rar），任一卷缺失都算 Missing；
        // 具体前缀由 ArchiveVolumeSet 统一给出，避免各处各写一套分卷命名规则。
        _watchFilter = ArchiveVolumeSet.GetVolumeWatchFilter(_archivePath);

        foreach (var item in _snapshot)
            _watchedFileNames.Add(Path.GetFileName(item.Path));

        StartWatching();
    }

    /// <summary>状态变化通知（在 UI 线程触发）。</summary>
    public event EventHandler<ArchiveAvailability>? AvailabilityChanged;

    /// <summary>打开时记录的压缩包路径（分卷时为传入的那一卷）。</summary>
    public string ArchivePath => _archivePath;

    /// <summary>最近一次核对得到的可用性。</summary>
    public ArchiveAvailability Current { get; private set; } = ArchiveAvailability.Unknown;

    /// <summary>
    /// 为指定压缩包建立监视。文件不存在、或监视建立失败（权限 / 目录已消失）时返回 null，
    /// 调用方按「未监视」处理即可，绝不抛给 UI。
    /// </summary>
    public static ArchiveFileGuard? TryCreate(string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            return null;

        try
        {
            return new ArchiveFileGuard(archivePath);
        }
        catch (Exception ex)
        {
            LifetimeDiagnostics.Log($"archive-guard create failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>立即核对一次；状态发生变化时触发 <see cref="AvailabilityChanged"/>。</summary>
    public ArchiveAvailability Check()
    {
        if (_disposed)
            return Current;

        // 自写期间不判定：此刻磁盘状态必然与基线不同（写到一半 / 临时文件覆盖），
        // 基线由 BeginSelfWrite() 作用域结束时统一重设。
        if (_selfWriteDepth > 0)
            return Current;

        var next = Evaluate();
        if (next != Current)
        {
            Current = next;
            LifetimeDiagnostics.Log(
                $"archive-guard changed state={next} file='{Path.GetFileName(_archivePath)}'");
            AvailabilityChanged?.Invoke(this, next);
        }

        return Current;
    }

    /// <summary>
    /// 标记「本进程正在重写这个压缩包」：作用域内忽略文件系统事件，释放时重设基线。
    /// </summary>
    /// <remarks>
    /// 用法：<c>using (_archiveGuard?.BeginSelfWrite()) { ...写包... }</c>。
    /// 必须在 UI 线程进入/释放（与 <see cref="Check"/>、<see cref="AvailabilityChanged"/> 同一线程）。
    /// 允许嵌套：只有最外层释放时才重设基线，内层不会把写到一半的状态当成新基线。
    /// </remarks>
    public IDisposable BeginSelfWrite()
    {
        _selfWriteDepth++;
        return new SelfWriteScope(this);
    }


    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileSystemChanged;
                _watcher.Deleted -= OnFileSystemChanged;
                _watcher.Changed -= OnFileSystemChanged;
                _watcher.Renamed -= OnFileSystemRenamed;
                _watcher.Dispose();
                _watcher = null;
            }
        }
        catch
        {
            // 监视器释放失败不影响状态复位（目录可能已随网盘卸载消失）。
        }

        _debounceTimer?.Stop();
        _debounceTimer = null;
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    /// <summary>退出自写抑制态；最外层结束时以磁盘现状重设基线。</summary>
    private void EndSelfWrite()
    {
        if (_selfWriteDepth > 0)
            _selfWriteDepth--;

        if (_selfWriteDepth == 0)
            Rebase();
    }

    /// <summary>
    /// 以磁盘现状重设基线：自写结束后调用，把「自己刚写出来的版本」当成新的比对起点。
    /// 只有文件真被自己删掉（删除全部条目会移除整包）时才报告 <see cref="ArchiveAvailability.Missing"/>。
    /// </summary>
    private void Rebase()
    {
        if (_disposed)
            return;

        _snapshot.Clear();
        CaptureSnapshot();

        var next = Evaluate();
        if (next == Current)
            return;

        Current = next;
        LifetimeDiagnostics.Log(
            $"archive-guard rebased state={next} file='{Path.GetFileName(_archivePath)}'");
        AvailabilityChanged?.Invoke(this, next);
    }

    private void CaptureSnapshot()
    {
        // 分卷集要按「全部卷」快照：任一卷被删都要能报出来，卷数变化也算 Replaced。
        var volumes = ArchiveVolumeSet.EnumerateVolumes(_archivePath);
        if (volumes.Count == 0)
            volumes = [_archivePath];

        foreach (var path in volumes)
        {
            var info = new FileInfo(path);
            if (info.Exists)
                _snapshot.Add(new VolumeSnapshot(info.FullName, info.Length, info.LastWriteTimeUtc));
        }
    }

    private ArchiveAvailability Evaluate()
    {
        if (_snapshot.Count == 0)
            return ArchiveAvailability.Missing;

        var volumes = ArchiveVolumeSet.EnumerateVolumes(_archivePath);
        if (volumes.Count == 0)
            return ArchiveAvailability.Missing;
        if (volumes.Count < _snapshot.Count)
            return ArchiveAvailability.Missing;
        if (volumes.Count > _snapshot.Count)
            return ArchiveAvailability.Replaced;

        foreach (var snapshot in _snapshot)
        {
            var info = new FileInfo(snapshot.Path);
            if (!info.Exists)
                return ArchiveAvailability.Missing;
            if (info.Length != snapshot.Length || info.LastWriteTimeUtc != snapshot.LastWriteUtc)
                return ArchiveAvailability.Replaced;
        }

        return ArchiveAvailability.Available;
    }

    private void StartWatching()
    {
        if (string.IsNullOrEmpty(_watchDirectory) || !Directory.Exists(_watchDirectory))
        {
            StartPolling("directory-missing");
            return;
        }

        // UNC 路径上 FileSystemWatcher 不可靠（网盘 / 共享失联时静默丢事件），直接轮询。
        if (_watchDirectory.StartsWith(@"\\", StringComparison.Ordinal))
        {
            StartPolling("unc-path");
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(_watchDirectory, _watchFilter ?? "*")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;
        }
        catch (Exception ex)
        {
            StartPolling($"watcher-failed: {ex.GetType().Name}");
        }
    }

    private void StartPolling(string reason)
    {
        LifetimeDiagnostics.Log($"archive-guard polling mode ({reason}) file='{Path.GetFileName(_archivePath)}'");
        _pollTimer = new DispatcherTimer { Interval = PollInterval };
        _pollTimer.Tick += (_, _) => Check();
        _pollTimer.Start();
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // watcher 会为目录里每个匹配文件触发事件，先按文件名过滤掉无关项。
        if (!_watchedFileNames.Contains(e.Name ?? string.Empty))
            return;

        ScheduleRecheck();
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (!_watchedFileNames.Contains(e.OldName ?? string.Empty)
            && !_watchedFileNames.Contains(e.Name ?? string.Empty))
        {
            return;
        }

        ScheduleRecheck();
    }

    /// <summary>把事件合并到一次 UI 线程上的核对（同一操作常连着多个文件系统事件）。</summary>
    private void ScheduleRecheck()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
                return;

            if (_debounceTimer == null)
            {
                _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
                _debounceTimer.Tick += (_, _) =>
                {
                    _debounceTimer?.Stop();
                    Check();
                };
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    /// <summary>自写作用域：Dispose 时退出抑制态并重设基线（可重复 Dispose）。</summary>
    private sealed class SelfWriteScope : IDisposable
    {
        private ArchiveFileGuard? _owner;

        public SelfWriteScope(ArchiveFileGuard owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndSelfWrite();
        }
    }

    private readonly record struct VolumeSnapshot(string Path, long Length, DateTime LastWriteUtc);
}
