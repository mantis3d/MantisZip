using System.Text.Json;

namespace MantisZip.Core.Services;

/// <summary>
/// 最近打开的文件列表管理器。
/// 存储路径 + 最后打开时间，持久化到 JSON 文件。
/// 线程安全（非并发），通常在 UI 线程使用。
/// </summary>
public class RecentFileManager
{
    private readonly string _filePath;
    private readonly int _maxEntries;
    private List<RecentFileEntry> _entries = [];

    /// <summary>已加载的条目（按最后打开时间倒序）。</summary>
    public IReadOnlyList<RecentFileEntry> Entries => _entries.AsReadOnly();

    /// <param name="filePath">持久化 JSON 文件路径。</param>
    /// <param name="maxEntries">最大条目数（默认 10）。</param>
    public RecentFileManager(string filePath, int maxEntries = 10)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _maxEntries = maxEntries > 0 ? maxEntries : 10;
    }

    /// <summary>加载持久化的最近文件列表。</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            var entries = JsonSerializer.Deserialize<List<RecentFileEntry>>(json);
            if (entries != null)
            {
                _entries = entries
                    .Where(e => File.Exists(e.Path))
                    .OrderByDescending(e => e.LastOpened)
                    .Take(_maxEntries)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Utils.CoreLog.Trace("RecentFileManager.Load: failed: {0}", ex.Message);
            _entries = [];
        }
    }

    /// <summary>保存列表到文件。</summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Utils.CoreLog.Trace("RecentFileManager.Save: failed: {0}", ex.Message);
        }
    }

    /// <summary>添加或更新路径到最后打开时间。</summary>
    public void Add(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        _entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        _entries.Insert(0, new RecentFileEntry { Path = path, LastOpened = DateTime.Now });

        if (_entries.Count > _maxEntries)
            _entries = _entries.Take(_maxEntries).ToList();

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

/// <summary>最近文件条目。</summary>
public class RecentFileEntry
{
    /// <summary>文件完整路径。</summary>
    public string Path { get; set; } = "";
    /// <summary>最后打开时间。</summary>
    public DateTime LastOpened { get; set; }
}
