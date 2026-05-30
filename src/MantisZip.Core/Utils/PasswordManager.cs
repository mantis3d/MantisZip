using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MantisZip.Core.Utils;

namespace MantisZip.Core;

/// <summary>
/// 密码条目
/// </summary>
public class PasswordEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("patterns")]
    public List<string> Patterns { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("lastUsed")]
    public DateTime? LastUsed { get; set; }
}

/// <summary>
/// 密码数据
/// </summary>
public class PasswordData
{
    [JsonPropertyName("passwords")]
    public List<PasswordEntry> Passwords { get; set; } = new();
}

/// <summary>
/// 密码管理器（DPAPI 加密存储，仅 Windows）
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class PasswordManager
{
    /// <summary>密码库最大条目数（防暴力破解滥用）</summary>
    public const int MaxEntries = 1000;

    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MantisZip");
    private static readonly string PasswordFilePath = Path.Combine(AppDataPath, "passwords.json");

    private PasswordData _data = new();
    private static readonly Lazy<PasswordManager> _instance = new(() => new PasswordManager(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static PasswordManager Instance => _instance.Value;

    private PasswordManager()
    {
        Load();
    }

    /// <summary>
    /// 加载密码数据
    /// </summary>
    public void Load()
    {
        CoreLog.Info($"PasswordManager.Load: path={PasswordFilePath}");
        try
        {
            if (File.Exists(PasswordFilePath))
            {
                var raw = File.ReadAllText(PasswordFilePath);

                // 旧格式 (未加密的 JSON) — 以 '{' 开头
                if (raw.TrimStart().StartsWith("{") || raw.TrimStart().StartsWith("["))
                {
                    _data = JsonSerializer.Deserialize<PasswordData>(raw) ?? new PasswordData();
                    CoreLog.Info($"PasswordManager.Load: loaded {_data.Passwords.Count} entries (plaintext, will migrate on save)");
                }
                else
                {
                    // 新格式：Base64 → DPAPI 解密 → JSON
                    var ciphertext = Convert.FromBase64String(raw);
                    var decrypted = ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
                    var json = Encoding.UTF8.GetString(decrypted);
                    _data = JsonSerializer.Deserialize<PasswordData>(json) ?? new PasswordData();
                    CoreLog.Info($"PasswordManager.Load: loaded {_data.Passwords.Count} entries (encrypted)");
                }
            }
            else
            {
                CoreLog.Info("PasswordManager.Load: file not found, using empty data");
                _data = new PasswordData();
            }
        }
        catch (Exception ex)
        {
            // 使用 Trace（无 [Conditional("DEBUG")]）确保 RELEASE 下也能记录
            CoreLog.Trace("PasswordManager.Load: failed, resetting to empty: {0}", ex.Message);
            // 备份损坏的文件，避免用户永久丢失密码数据
            try
            {
                if (File.Exists(PasswordFilePath))
                {
                    var backupPath = PasswordFilePath + ".corrupted." + DateTime.Now.ToString("yyyyMMddHHmmss");
                    File.Move(PasswordFilePath, backupPath);
                    CoreLog.Trace("PasswordManager.Load: backed up corrupted file to {0}", backupPath);
                }
            }
            catch { /* 备份失败不影响继续运行 */ }
            _data = new PasswordData();
        }
    }

    /// <summary>
    /// 保存密码数据（DPAPI 加密）。
    /// 保存失败时抛出异常，由调用方（UI 层）处理并通知用户。
    /// </summary>
    public void Save()
    {
        if (!Directory.Exists(AppDataPath))
            Directory.CreateDirectory(AppDataPath);

        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        var plaintext = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        var base64 = Convert.ToBase64String(encrypted);
        File.WriteAllText(PasswordFilePath, base64);
        CoreLog.Info($"PasswordManager.Save: saved {_data.Passwords.Count} entries (encrypted)");
    }

    /// <summary>
    /// 当前密码条目数
    /// </summary>
    public int EntryCount => _data.Passwords.Count;

    /// <summary>
    /// 获取所有密码列表
    /// </summary>
    public IReadOnlyList<PasswordEntry> GetAllPasswords() => _data.Passwords.AsReadOnly();

    /// <summary>
    /// 根据文件名查找匹配的密码
    /// </summary>
    public List<PasswordEntry> FindMatchingPasswords(string fileName, int maxResults = int.MaxValue)
    {
        var name = Path.GetFileName(fileName);
        var results = new List<PasswordEntry>();

        foreach (var entry in _data.Passwords)
        {
            if (results.Count >= maxResults) break;

            if (MatchPattern(entry.Patterns, name))
                results.Add(entry);
        }

        var clipped = results.Count >= maxResults ? " (clipped)" : "";
        CoreLog.Info($"PasswordManager.FindMatchingPasswords: file={fileName} -> {results.Count} matches{clipped}");

        return results;
    }

    /// <summary>
    /// 单个密码是否匹配
    /// </summary>
    public bool MatchPattern(List<string> patterns, string fileName)
    {
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;

            // 尝试作为正则表达式匹配
            try
            {
                if (Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            catch (Exception ex)
            {
                CoreLog.Info($"PasswordManager: invalid regex pattern='{pattern}', falling back to glob: {ex.Message}");
            }

            // Glob 匹配（支持 * 和 ?）
            if (GlobMatch(fileName, pattern))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Glob 模式匹配
    /// </summary>
    private bool GlobMatch(string input, string pattern)
    {
        // 转换 Glob 为正则表达式
        var regexPattern = "^";
        foreach (var c in pattern)
        {
            switch (c)
            {
                case '*':
                    regexPattern += ".*";
                    break;
                case '?':
                    regexPattern += ".";
                    break;
                case '.':
                    regexPattern += "\\.";
                    break;
                default:
                    if ("\\+^$|{}()[]".Contains(c))
                        regexPattern += "\\" + c;
                    else
                        regexPattern += c;
                    break;
            }
        }
        regexPattern += "$";

        try
        {
            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
        }
        catch (Exception ex)
        {
            CoreLog.Info($"PasswordManager: GlobMatch regex failed pattern='{regexPattern}', input='{input}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 添加密码
    /// </summary>
    public void AddPassword(string password, string description, List<string> patterns)
    {
        if (_data.Passwords.Count >= MaxEntries)
            throw new InvalidOperationException($"Password library is full (max {MaxEntries})");

        CoreLog.Info($"PasswordManager.AddPassword: patterns=[{string.Join("; ", patterns)}]");
        var entry = new PasswordEntry
        {
            Password = password,
            Description = description,
            Patterns = patterns
        };

        _data.Passwords.Add(entry);
        CoreLog.Info($"PasswordManager.AddPassword: id={entry.Id}");
        Save();
    }

    /// <summary>
    /// 更新密码
    /// </summary>
    public void UpdatePassword(string id, string password, string description, List<string> patterns)
    {
        CoreLog.Info($"PasswordManager.UpdatePassword: id={id}");
        var entry = _data.Passwords.FirstOrDefault(p => p.Id == id);
        if (entry != null)
        {
            entry.Password = password;
            entry.Description = description;
            entry.Patterns = patterns;
            CoreLog.Info($"PasswordManager.UpdatePassword: updated id={entry.Id}");
            Save();
        }
    }

    /// <summary>
    /// 删除密码
    /// </summary>
    public void DeletePassword(string id)
    {
        CoreLog.Info($"PasswordManager.DeletePassword: id={id}");
        var entry = _data.Passwords.FirstOrDefault(p => p.Id == id);
        if (entry != null)
        {
            _data.Passwords.Remove(entry);
            CoreLog.Info($"PasswordManager.DeletePassword: removed id={entry.Id}");
            Save();
        }
    }

    /// <summary>
    /// 标记使用时间
    /// </summary>
    public void MarkUsed(string id)
    {
        var entry = _data.Passwords.FirstOrDefault(p => p.Id == id);
        if (entry != null)
        {
            entry.LastUsed = DateTime.Now;
            Save();
        }
    }

    /// <summary>
    /// 导出为明文 JSON（用于导入导出功能）
    /// </summary>
    public string ExportToJson()
    {
        return JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 从明文 JSON 导入（追加模式）
    /// </summary>
    public void ImportFromJson(string json)
    {
        var imported = JsonSerializer.Deserialize<PasswordData>(json) ?? new PasswordData();
        if (_data.Passwords.Count + imported.Passwords.Count > MaxEntries)
            throw new InvalidOperationException($"Import would exceed password library limit ({MaxEntries})");
        _data.Passwords.AddRange(imported.Passwords);
        Save();
    }

    /// <summary>
    /// 获取密码文件路径
    /// </summary>
    public static string GetPasswordFilePath() => PasswordFilePath;
}
