using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Text.RegularExpressions;

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
/// 密码管理器
/// </summary>
public class PasswordManager
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MantisZip");
    private static readonly string PasswordFilePath = Path.Combine(AppDataPath, "passwords.json");

    private PasswordData _data = new();
    private static PasswordManager? _instance;

    public static PasswordManager Instance => _instance ??= new PasswordManager();

    private PasswordManager()
    {
        Load();
    }

    /// <summary>
    /// 加载密码数据
    /// </summary>
    public void Load()
    {
        try
        {
            if (File.Exists(PasswordFilePath))
            {
                var json = File.ReadAllText(PasswordFilePath);
                _data = JsonSerializer.Deserialize<PasswordData>(json) ?? new PasswordData();
            }
        }
        catch
        {
            _data = new PasswordData();
        }
    }

    /// <summary>
    /// 保存密码数据
    /// </summary>
    public void Save()
    {
        try
        {
            if (!Directory.Exists(AppDataPath))
            {
                Directory.CreateDirectory(AppDataPath);
            }

            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PasswordFilePath, json);
        }
        catch
        {
            // 忽略保存错误
        }
    }

    /// <summary>
    /// 获取所有密码列表
    /// </summary>
    public IReadOnlyList<PasswordEntry> GetAllPasswords() => _data.Passwords.AsReadOnly();

    /// <summary>
    /// 根据文件名查找匹配的密码
    /// </summary>
    public List<PasswordEntry> FindMatchingPasswords(string fileName)
    {
        var results = new List<PasswordEntry>();
        var name = Path.GetFileName(fileName);

        foreach (var entry in _data.Passwords)
        {
            if (MatchPattern(entry.Patterns, name))
            {
                results.Add(entry);
            }
        }

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
                {
                    return true;
                }
            }
            catch
            {
                // 正则表达式无效，尝试 Glob 匹配
            }

            // Glob 匹配（支持 * 和 ?）
            if (GlobMatch(fileName, pattern))
            {
                return true;
            }
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
                    {
                        regexPattern += "\\" + c;
                    }
                    else
                    {
                        regexPattern += c;
                    }
                    break;
            }
        }
        regexPattern += "$";

        try
        {
            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 添加密码
    /// </summary>
    public void AddPassword(string password, string description, List<string> patterns)
    {
        var entry = new PasswordEntry
        {
            Password = password,
            Description = description,
            Patterns = patterns
        };

        _data.Passwords.Add(entry);
        Save();
    }

    /// <summary>
    /// 更新密码
    /// </summary>
    public void UpdatePassword(string id, string password, string description, List<string> patterns)
    {
        var entry = _data.Passwords.FirstOrDefault(p => p.Id == id);
        if (entry != null)
        {
            entry.Password = password;
            entry.Description = description;
            entry.Patterns = patterns;
            Save();
        }
    }

    /// <summary>
    /// 删除密码
    /// </summary>
    public void DeletePassword(string id)
    {
        var entry = _data.Passwords.FirstOrDefault(p => p.Id == id);
        if (entry != null)
        {
            _data.Passwords.Remove(entry);
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
    /// 获取密码文件路径
    /// </summary>
    public static string GetPasswordFilePath() => PasswordFilePath;
}