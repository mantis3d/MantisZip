using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MantisZip.UI;

/// <summary>
/// 最近打开的文件列表管理器。
/// 存储路径 + 最后打开时间，持久化到 %LOCALAPPDATA%\MantisZip\recent.json。
/// </summary>
internal class RecentFileManager
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MantisZip", "recent.json");

    private List<RecentFileEntry> _entries = [];

    /// <summary>已加载的条目（按最后打开时间倒序）。</summary>
    public IReadOnlyList<RecentFileEntry> Entries => _entries.AsReadOnly();

    /// <summary>加载持久化的最近文件列表。</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            var entries = JsonSerializer.Deserialize<List<RecentFileEntry>>(json);
            if (entries != null)
            {
                // 过滤不存在的文件，按时间倒序，限制数量
                var max = AppSettings.Instance.MaxRecentFiles;
                _entries = entries
                    .Where(e => File.Exists(e.Path))
                    .OrderByDescending(e => e.LastOpened)
                    .Take(max > 0 ? max : 10)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("RecentFileManager.Load: failed: {0}", ex.Message);
            _entries = [];
        }
    }

    /// <summary>保存列表到文件。</summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            App.LogDebug("RecentFileManager.Save: failed: {0}", ex.Message);
        }
    }

    /// <summary>添加或更新路径到最后打开时间。</summary>
    public void Add(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        // 移除旧记录（如果存在）
        _entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

        // 添加到头部
        _entries.Insert(0, new RecentFileEntry { Path = path, LastOpened = DateTime.Now });

        // 限制数量
        var max = AppSettings.Instance.MaxRecentFiles;
        if (_entries.Count > max && max > 0)
            _entries = _entries.Take(max).ToList();

        Save();
    }

    /// <summary>从列表中移除指定路径。</summary>
    public void Remove(string path)
    {
        var removed = _entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) Save();
    }

    /// <summary>清空所有记录。</summary>
    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    /// <summary>获取仍存在的文件列表（按时间倒序）。</summary>
    public List<RecentFileEntry> GetExisting()
    {
        return _entries
            .Where(e => File.Exists(e.Path))
            .OrderByDescending(e => e.LastOpened)
            .ToList();
    }
}

internal class RecentFileEntry
{
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; }
}
