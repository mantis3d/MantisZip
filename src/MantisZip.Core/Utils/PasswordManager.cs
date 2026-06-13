using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MantisZip.Core.Abstractions;
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

    [JsonIgnore]
    public string PatternsDisplay => string.Join(", ", Patterns);
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
/// 密码管理器（AES-GCM 加密存储，跨平台）
/// </summary>
public class PasswordManager
{
    /// <summary>密码库最大条目数（防暴力破解滥用）</summary>
    public const int MaxEntries = 1000;

    /// <summary>AES-GCM 格式文件前缀</summary>
    internal const string FormatPrefix = "MZPAES|";

    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MantisZip");
    private static readonly string PasswordFilePath = Path.Combine(AppDataPath, "passwords.json");

    private PasswordData _data = new();
    private static readonly Lazy<PasswordManager> _instance = new(() => new PasswordManager(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static PasswordManager Instance => _instance.Value;

    /// <summary>
    /// 数据保护器。默认使用 AES-GCM，可在应用程序启动时更换为平台特定实现。
    /// 必须在首次访问 <see cref="Instance"/> 之前设置。
    /// </summary>
    public static IDataProtector Protector { get; set; } = new AesGcmDataProtector();

    private PasswordManager()
    {
        Load();
    }

    /// <summary>
    /// 加载密码数据。
    /// 支持三种格式自动检测：
    ///   1. 明文 JSON（v0.x 早期版本）
    ///   2. DPAPI 加密（v0.3.x 版本）— 自动迁移到 AES-GCM
    ///   3. AES-GCM 加密（当前版本）
    /// </summary>
    public void Load()
    {
        CoreLog.Info($"PasswordManager.Load: path={PasswordFilePath}");
        try
        {
            if (File.Exists(PasswordFilePath))
            {
                var raw = File.ReadAllText(PasswordFilePath);
                var trimmed = raw.TrimStart();

                // Format 1: 明文 JSON（v0.x 早期版本）
                if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                {
                    _data = JsonSerializer.Deserialize<PasswordData>(raw) ?? new PasswordData();
                    CoreLog.Info($"PasswordManager.Load: loaded {_data.Passwords.Count} entries (plaintext, will migrate on save)");
                }
                // Format 2: AES-GCM 加密（当前版本）
                else if (trimmed.StartsWith(FormatPrefix))
                {
                    var base64 = trimmed.Substring(FormatPrefix.Length);
                    var ciphertext = Convert.FromBase64String(base64);
                    var decrypted = Protector.Unprotect(ciphertext);
                    var json = Encoding.UTF8.GetString(decrypted);
                    _data = JsonSerializer.Deserialize<PasswordData>(json) ?? new PasswordData();
                    CoreLog.Info($"PasswordManager.Load: loaded {_data.Passwords.Count} entries (AES-GCM encrypted)");
                }
                // Format 3: 旧 DPAPI 加密格式（v0.3.x）— 自动迁移到 AES-GCM
                else
                {
                    MigrateFromDpapi(raw);
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
    /// 从旧 DPAPI 加密格式迁移到 AES-GCM。
    /// 解密成功后立即用 <see cref="Protector"/> 重写文件。
    /// </summary>
#pragma warning disable CA1416 // ProtectedData 仅 Windows 支持，此处由 try-catch PlatformNotSupportedException 兜底
    private void MigrateFromDpapi(string raw)
    {
        try
        {
            var ciphertext = Convert.FromBase64String(raw);
            var decrypted = ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            _data = JsonSerializer.Deserialize<PasswordData>(json) ?? new PasswordData();
            CoreLog.Info($"PasswordManager.Load: loaded {_data.Passwords.Count} entries (DPAPI encrypted), migrating to AES-GCM");

            // 备份旧 DPAPI 文件
            var backupPath = PasswordFilePath + ".dpapi-backup";
            if (!File.Exists(backupPath))
            {
                File.Copy(PasswordFilePath, backupPath);
                CoreLog.Info("PasswordManager.Load: backed up DPAPI file to {0}", backupPath);
            }

            // 立即用 AES-GCM 格式保存
            Save();

            CoreLog.Info("PasswordManager.Load: migration complete (DPAPI → AES-GCM)");
        }
        catch (PlatformNotSupportedException)
        {
            // 非 Windows 平台无法解密 DPAPI 数据，抛出让外层 catch 处理
            CoreLog.Trace("PasswordManager.MigrateFromDpapi: DPAPI not available on this platform, cannot migrate");
            throw;
        }
        catch (Exception ex)
        {
            // 解密失败（不是 DPAPI 格式或密钥不匹配）
            CoreLog.Trace("PasswordManager.MigrateFromDpapi: DPAPI decryption failed: {0}", ex.Message);
            throw;
        }
    }
#pragma warning restore CA1416

    /// <summary>
    /// 保存密码数据（AES-GCM 加密）。
    /// 保存失败时抛出异常，由调用方（UI 层）处理并通知用户。
    /// </summary>
    public void Save()
    {
        if (!Directory.Exists(AppDataPath))
            Directory.CreateDirectory(AppDataPath);

        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        var plaintext = Encoding.UTF8.GetBytes(json);
        var encrypted = Protector.Protect(plaintext);
        var encoded = FormatPrefix + Convert.ToBase64String(encrypted);
        File.WriteAllText(PasswordFilePath, encoded);
        CoreLog.Info($"PasswordManager.Save: saved {_data.Passwords.Count} entries (AES-GCM encrypted)");
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
