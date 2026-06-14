using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MantisZip.Core.Services;

namespace MantisZip.UI;

/// <summary>
/// 最近打开的文件列表管理器（WPF 适配层）。
/// 存储路径 + 最后打开时间，持久化到 %LOCALAPPDATA%\MantisZip\recent.json。
/// </summary>
internal class RecentFileManager
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MantisZip", "recent.json");

    private readonly Core.Services.RecentFileManager _core;

    /// <summary>已加载的条目（按最后打开时间倒序）。</summary>
    public IReadOnlyList<Core.Services.RecentFileEntry> Entries => _core.Entries;

    public RecentFileManager()
    {
        var max = AppSettings.Instance.MaxRecentFiles;
        _core = new Core.Services.RecentFileManager(FilePath, max > 0 ? max : 10);
    }

    public void Load() => _core.Load();
    public void Save() => _core.Save();
    public void Add(string path) => _core.Add(path);
    public void Remove(string path) => _core.Remove(path);
    public void Clear() => _core.Clear();

    public List<RecentFileEntry> GetExisting()
        => _core.GetExisting().Select(e => new RecentFileEntry { Path = e.Path, LastOpened = e.LastOpened }).ToList();
}

internal class RecentFileEntry
{
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; }
}
