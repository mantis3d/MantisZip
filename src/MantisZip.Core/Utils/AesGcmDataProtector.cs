using System.Security.Cryptography;
using System.Text;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 基于 AES-256-GCM 的数据保护实现。
/// 密钥存储在 <c>%APPDATA%/MantisZip/.masterkey</c>，首次使用时自动生成。
/// 输出格式（由 PasswordManager 添加前缀后写入文件）：
///   raw = versionByte(1) + nonce(12) + tag(16) + ciphertext(N)
/// </summary>
/// <remarks>
/// 安全性权衡：密钥以文件形式存储在用户目录下，受文件系统 ACL 保护。
/// 相比 DPAPI（密钥由 Windows 管理），此实现允许跨平台迁移密钥文件。
/// </remarks>
public class AesGcmDataProtector : IDataProtector
{
    internal const int KeyLength = 32;     // AES-256
    internal const int NonceLength = 12;   // 96-bit nonce (GCM 标准推荐)
    internal const int TagLength = 16;     // 128-bit authentication tag
    internal const byte CurrentVersion = 1;

    private static readonly string KeyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MantisZip",
        ".masterkey");

    private readonly Lazy<byte[]> _key;

    public AesGcmDataProtector()
    {
        _key = new Lazy<byte[]>(LoadOrCreateKey, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// 加载或创建 AES 密钥文件。
    /// </summary>
    private static byte[] LoadOrCreateKey()
    {
        if (File.Exists(KeyFilePath))
        {
            var key = File.ReadAllBytes(KeyFilePath);
            if (key.Length == KeyLength)
            {
                CoreLog.Trace("AesGcmDataProtector: loaded existing key");
                return key;
            }

            CoreLog.Trace("AesGcmDataProtector: key file corrupted (expected {0} bytes, got {1}), regenerating",
                KeyLength, key.Length);
        }
        else
        {
            CoreLog.Trace("AesGcmDataProtector: key file not found at {0}, generating new key", KeyFilePath);
        }

        var newKey = RandomNumberGenerator.GetBytes(KeyLength);
        var dir = Path.GetDirectoryName(KeyFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(KeyFilePath, newKey);
        CoreLog.Info("AesGcmDataProtector: new 256-bit key generated at {0}", KeyFilePath);
        return newKey;
    }

    /// <summary>
    /// 重新生成 AES 密钥（用于测试或密钥轮换）。
    /// </summary>
    internal static void RegenerateKey()
    {
        if (File.Exists(KeyFilePath))
            File.Delete(KeyFilePath);
        // 下次访问时自动生成新密钥
    }

    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (plaintext.Length == 0)
            return [];

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        using var aes = new AesGcm(_key.Value, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // 格式: version(1) + nonce(12) + tag(16) + ciphertext(N)
        var combined = new byte[1 + NonceLength + TagLength + plaintext.Length];
        combined[0] = CurrentVersion;
        Buffer.BlockCopy(nonce, 0, combined, 1, NonceLength);
        Buffer.BlockCopy(tag, 0, combined, 1 + NonceLength, TagLength);
        Buffer.BlockCopy(ciphertext, 0, combined, 1 + NonceLength + TagLength, ciphertext.Length);

        return combined;
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (ciphertext.Length == 0)
            return [];

        if (ciphertext.Length < 1 + NonceLength + TagLength)
            throw new InvalidOperationException(
                $"Invalid ciphertext: expected at least {1 + NonceLength + TagLength} bytes, got {ciphertext.Length}");

        var version = ciphertext[0];
        if (version != CurrentVersion)
            throw new InvalidOperationException($"Unsupported format version: {version} (expected {CurrentVersion})");

        var nonce = new byte[NonceLength];
        var tag = new byte[TagLength];
        var payloadLength = ciphertext.Length - 1 - NonceLength - TagLength;
        var payload = new byte[payloadLength];

        Buffer.BlockCopy(ciphertext, 1, nonce, 0, NonceLength);
        Buffer.BlockCopy(ciphertext, 1 + NonceLength, tag, 0, TagLength);
        Buffer.BlockCopy(ciphertext, 1 + NonceLength + TagLength, payload, 0, payloadLength);

        var plaintext = new byte[payloadLength];
        using var aes = new AesGcm(_key.Value, TagLength);
        aes.Decrypt(nonce, payload, tag, plaintext);

        return plaintext;
    }
}
