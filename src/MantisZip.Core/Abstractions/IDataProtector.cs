namespace MantisZip.Core.Abstractions;

/// <summary>
/// 平台无关的数据保护接口。
/// 用于加密/解密敏感数据（如密码库），各平台可提供不同实现：
/// - Windows: AesGcmDataProtector（文件级密钥）
/// - macOS: Keychain 实现
/// - Linux: libsecret 或文件级加密
/// </summary>
public interface IDataProtector
{
    /// <summary>
    /// 加密明文数据。
    /// </summary>
    /// <param name="plaintext">明文字节数组</param>
    /// <returns>密文字节数组（不含编码、不包含格式标记）</returns>
    byte[] Protect(byte[] plaintext);

    /// <summary>
    /// 解密密文数据。
    /// </summary>
    /// <param name="ciphertext">密文字节数组（由 <see cref="Protect"/> 产生）</param>
    /// <returns>明文字节数组</returns>
    byte[] Unprotect(byte[] ciphertext);
}
