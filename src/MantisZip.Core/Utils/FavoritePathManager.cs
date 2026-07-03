using System.Text.Json;
using System.Text.Json.Serialization;

namespace MantisZip.Core.Utils;

public record FavoritePathItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("addedAt")] DateTime AddedAt,
    [property: JsonPropertyName("isSystem")] bool IsSystem,
    [property: JsonPropertyName("systemKey")] string? SystemKey
);

internal class FavoriteData
{
    [JsonPropertyName("userFavorites")]
    public List<FavoritePathItem>? UserFavorites { get; set; }

    [JsonPropertyName("hiddenSystemPaths")]
    public List<string>? HiddenSystemPaths { get; set; }
}

public static class FavoritePathManager
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");

    private static readonly string FilePath = Path.Combine(AppDataPath, "favorites.json");

    private static readonly object _lock = new();

    private static readonly List<SystemPathDef> SystemPathDefs = new()
    {
        new SystemPathDef("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
        new SystemPathDef("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        new SystemPathDef("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
    };

    private static List<FavoritePathItem> _userFavorites = new();
    private static HashSet<string> _hiddenSystemKeys = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    static FavoritePathManager() { Load(); }

    public static List<FavoritePathItem> GetAll()
    {
        lock (_lock)
        {
            var result = new List<FavoritePathItem>();
            foreach (var def in SystemPathDefs)
            {
                if (!_hiddenSystemKeys.Contains(def.Key))
                {
                    result.Add(new FavoritePathItem(GetSystemDisplayName(def.Key), def.Path, DateTime.MinValue, true, def.Key));
                }
            }
            result.AddRange(_userFavorites);
            return result;
        }
    }

    public static List<FavoritePathItem> GetSystemPaths()
    {
        lock (_lock)
        {
            return SystemPathDefs.Select(def => new FavoritePathItem(GetSystemDisplayName(def.Key), def.Path, DateTime.MinValue, true, def.Key)).ToList();
        }
    }

    public static List<FavoritePathItem> GetUserFavorites()
    {
        lock (_lock) { return new List<FavoritePathItem>(_userFavorites); }
    }

    public static void Add(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return;
        var trimmedPath = NormalizePath(path.Trim());
        lock (_lock)
        {
            if (_userFavorites.Any(f => string.Equals(f.Path, trimmedPath, StringComparison.OrdinalIgnoreCase))) return;
            _userFavorites.Add(new FavoritePathItem(name.Trim(), trimmedPath, DateTime.Now, false, null));
            Save();
        }
    }

    public static void Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var trimmedPath = NormalizePath(path.Trim());
        lock (_lock)
        {
            // 用户收藏可能包含与系统路径相同的路径名（如手动添加的 Downloads 路径）
            // 直接匹配 _userFavorites 删除，不通过 IsSystemPathInternal 抛异常拦截
            var removed = _userFavorites.RemoveAll(f => string.Equals(f.Path, trimmedPath, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                Save();
        }
    }

    public static void Update(string oldPath, string newName, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath)) return;
        lock (_lock)
        {
            if (IsSystemPathInternal(oldPath, out _)) throw new InvalidOperationException("System paths cannot be edited.");
            var index = _userFavorites.FindIndex(f => string.Equals(f.Path, oldPath, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            _userFavorites[index] = new FavoritePathItem(newName.Trim(), newPath.Trim(), _userFavorites[index].AddedAt, false, null);
            Save();
        }
    }

    public static void Reorder(int oldIndex, int newIndex)
    {
        lock (_lock)
        {
            if (oldIndex < 0 || oldIndex >= _userFavorites.Count || newIndex < 0 || newIndex >= _userFavorites.Count || oldIndex == newIndex) return;
            var item = _userFavorites[oldIndex];
            _userFavorites.RemoveAt(oldIndex);
            _userFavorites.Insert(newIndex, item);
            Save();
        }
    }

    public static bool Exists(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        lock (_lock)
        {
            if (IsSystemPathInternal(path, out _)) return true;
            return _userFavorites.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static bool IsSystemPath(string path) => IsSystemPathInternal(path, out _);

    public static void SetSystemPathHidden(string key, bool hidden)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        lock (_lock)
        {
            if (hidden) _hiddenSystemKeys.Add(key); else _hiddenSystemKeys.Remove(key);
            Save();
        }
    }

    public static bool IsSystemPathHidden(string key) { lock (_lock) { return _hiddenSystemKeys.Contains(key); } }

    public static void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var data = new FavoriteData { UserFavorites = _userFavorites.ToList(), HiddenSystemPaths = _hiddenSystemKeys.ToList() };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data, JsonOptions));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FavoritePathManager.Save failed: {ex.Message}"); }
        }
    }

    public static void Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FilePath)) { _userFavorites = new List<FavoritePathItem>(); _hiddenSystemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase); return; }
                var data = JsonSerializer.Deserialize<FavoriteData>(File.ReadAllText(FilePath));
                _userFavorites = data?.UserFavorites ?? new List<FavoritePathItem>();
                _hiddenSystemKeys = new HashSet<string>(data?.HiddenSystemPaths ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            }
            catch { _userFavorites = new List<FavoritePathItem>(); _hiddenSystemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        }
    }

    private static bool IsSystemPathInternal(string path, out string? key)
    {
        var normalized = NormalizePath(path);
        foreach (var def in SystemPathDefs)
        {
            if (string.Equals(def.Path, normalized, StringComparison.OrdinalIgnoreCase))
            {
                key = def.Key;
                return true;
            }
        }
        key = null;
        return false;
    }

    /// <summary>
    /// 去除尾部目录分隔符（保留根路径如 "C:\" 的原状）。
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        // 对于根路径（如 "C:\"）保留尾部分隔符不截断
        while (path.Length > 3 &&
               (path[^1] == Path.DirectorySeparatorChar || path[^1] == Path.AltDirectorySeparatorChar))
            path = path[..^1];
        return path;
    }

    private static string GetSystemDisplayName(string key) => key switch { "Desktop" => "桌面", "Documents" => "文档", "Downloads" => "下载", _ => key };

    /// <summary>
    /// 获取系统路径对应的图标 emoji。
    /// </summary>
    public static string GetSystemIcon(string? key) => key switch
    {
        "Desktop"   => "🖥️",
        "Documents" => "📄",
        "Downloads" => "📥",
        _           => "📁",
    };

    private record SystemPathDef(string Key, string Path);
}