using System.Text.Json;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// Manages recently opened archive file paths.
/// Persisted to a JSON file in %APPDATA%/MantisZip/recent.json.
/// </summary>
public static class RecentFilesManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
    private static readonly string RecentFile = Path.Combine(SettingsDir, "recent.json");
    private const int MaxEntries = 10;

    private static List<string>? _cache;

    /// <summary>
    /// Get the list of recent file paths (most recent first).
    /// </summary>
    public static List<string> GetPaths()
    {
        if (_cache != null) return _cache;
        return Load();
    }

    /// <summary>
    /// Add a file path to the recent list.
    /// </summary>
    public static void AddPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        var paths = GetPaths();
        paths.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        paths.Insert(0, path);

        if (paths.Count > MaxEntries)
            paths.RemoveRange(MaxEntries, paths.Count - MaxEntries);

        Save(paths);
    }

    /// <summary>
    /// Clear all recent file entries.
    /// </summary>
    public static void Clear()
    {
        _cache = new List<string>();
        Save(_cache);
    }

    private static List<string> Load()
    {
        try
        {
            if (!File.Exists(RecentFile))
            {
                _cache = new List<string>();
                return _cache;
            }
            var json = File.ReadAllText(RecentFile);
            var paths = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            // Remove non-existent paths
            paths.RemoveAll(p => !File.Exists(p));
            _cache = paths;
            return paths;
        }
        catch
        {
            _cache = new List<string>();
            return _cache;
        }
    }

    private static void Save(List<string> paths)
    {
        _cache = paths;
        try
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RecentFile, json);
        }
        catch
        {
            // Best-effort
        }
    }
}
