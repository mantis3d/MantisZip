using System.Text.Json;
using System.Text.Json.Serialization;

namespace MantisZip.Core.Utils;

public record PathHistoryEntry(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("lastUsedAt")] DateTime LastUsedAt
);

internal class PathHistoryData
{
    [JsonPropertyName("entries")]
    public List<PathHistoryEntry>? Entries { get; set; }
}

public static class PathHistoryManager
{
    private const int MaxEntries = 50;

    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");

    private static readonly string FilePath = Path.Combine(AppDataPath, "path-history.json");

    private static readonly object _lock = new();
    private static List<PathHistoryEntry> _entries = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    static PathHistoryManager() { Load(); }

    public static List<PathHistoryEntry> GetRecent(int maxCount = MaxEntries)
    {
        lock (_lock) { return _entries.Take(Math.Max(1, maxCount)).ToList(); }
    }

    public static void Record(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (_lock)
        {
            var existingIndex = _entries.FindIndex(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0) _entries.RemoveAt(existingIndex);
            _entries.Insert(0, new PathHistoryEntry(path.Trim(), DateTime.Now));
            if (_entries.Count > MaxEntries) _entries = _entries.Take(MaxEntries).ToList();
            Save();
        }
    }

    public static void Clear()
    {
        lock (_lock) { _entries.Clear(); Save(); }
    }

    public static void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new PathHistoryData { Entries = _entries.ToList() }, JsonOptions));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"PathHistoryManager.Save failed: {ex.Message}"); }
        }
    }

    public static void Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FilePath)) { _entries = new List<PathHistoryEntry>(); return; }
                var data = JsonSerializer.Deserialize<PathHistoryData>(File.ReadAllText(FilePath));
                _entries = data?.Entries ?? new List<PathHistoryEntry>();
            }
            catch { _entries = new List<PathHistoryEntry>(); }
        }
    }
}