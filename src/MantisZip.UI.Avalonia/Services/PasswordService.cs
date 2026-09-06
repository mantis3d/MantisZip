using System.Security.Cryptography;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SharpSevenZip;
using SharpSevenZip.Exceptions;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 密码验证/匹配结果的精确分类
/// </summary>
public enum PasswordVerificationResult
{
    /// <summary>验证通过，密码正确</summary>
    Success,

    /// <summary>密码错误（或无密码但需要密码）</summary>
    WrongPassword,

    /// <summary>文件损坏/非加密格式/其他不可恢复错误——非密码问题</summary>
    CorruptedOrInvalid,

    /// <summary>无法确定（异常类型未知，建议重试或用户决定）</summary>
    Unknown,
}

/// <summary>
/// 密码验证详细结果
/// </summary>
public readonly record struct PasswordVerifyInfo(
    PasswordVerificationResult Result,
    string? DetailMessage = null,
    Exception? OriginalException = null)
{
    public static PasswordVerifyInfo Success() => new(PasswordVerificationResult.Success);
    public static PasswordVerifyInfo WrongPassword(string? detail = null) => new(PasswordVerificationResult.WrongPassword, detail);
    public static PasswordVerifyInfo CorruptedOrInvalid(string? detail = null) => new(PasswordVerificationResult.CorruptedOrInvalid, detail);
    public static PasswordVerifyInfo Unknown(string? detail = null) => new(PasswordVerificationResult.Unknown, detail);
}

/// <summary>
/// 密码验证与匹配服务 — 对标 WPF 的 App.Password.cs。
/// 处理已保存密码自动匹配、快速验证、密码保存。
/// 不直接处理 UI 对话框，由调用方通过回调呈现。
/// </summary>
public class PasswordService
{
    /// <summary>
    /// 快速验证密码是否正确——读第一个加密条目 1 字节（ZIP），
    /// 或提取最小加密条目的前 ~8KB（7z/RAR）。
    /// </summary>
    public bool QuickVerifyPassword(string archivePath, string password, IArchiveEngine engine)
    {
        try
        {
            if (engine is ZipEngine)
            {
                using var fs = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                using var archive = ArchiveFactory.OpenArchive(fs, new ReaderOptions { Password = password });
                var entry = archive.Entries.FirstOrDefault(e => e.IsEncrypted);
                if (entry == null)
                    return true;

                using var s = entry.OpenEntryStream();
                s.ReadByte();
                return true;
            }
            else if (engine is SevenZipEngine)
            {
                using var extractor = new SharpSevenZipExtractor(archivePath, password);
                var afd = extractor.ArchiveFileData;
                var encrypted = afd.Where(e => !e.IsDirectory && e.Encrypted).ToList();

                if (encrypted.Count == 0)
                    return true;

                var smallest = encrypted.OrderBy(e => e.Size).First();
                try
                {
                    using var verifyStream = new BoundedWriteStream(maxBytes: 8192);
                    extractor.ExtractFile(smallest.Index, verifyStream);
                    return true;
                }
                catch (Exception ex) when (IsPasswordOrCorruptedDataError(ex, true))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (IsPasswordError(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// 从已保存密码中匹配并快速验证。返回 (密码, 描述) 或 null。
    /// limitReached 表示匹配到的密码超过上限（防暴力破解），已截断。
    /// </summary>
    public (string Password, string Description)? TryMatchPassword(
        string archivePath,
        IArchiveEngine engine)
    {
        const int maxAttempts = 100;
        var allMatches = PasswordManager.Instance.FindMatchingPasswords(archivePath);
        var limitReached = allMatches.Count > maxAttempts;
        var candidatePasswords = limitReached ? allMatches.Take(maxAttempts).ToList() : allMatches;
        var tried = new HashSet<string>();

        foreach (var entry in candidatePasswords)
        {
            var pwd = entry.Password;
            if (!tried.Add(pwd)) continue;

            var desc = !string.IsNullOrEmpty(entry.Description) ? entry.Description : pwd;

            if (QuickVerifyPassword(archivePath, pwd, engine))
                return (pwd, desc);
        }

        return null;
    }

    /// <summary>
    /// 保存密码到密码库。
    /// </summary>
    public bool TrySavePassword(string password, string archivePath, List<string>? patterns, string? description)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        var savePatterns = patterns != null && patterns.Count > 0
            ? patterns
            : new List<string> { Path.GetFileName(archivePath) };
        var saveDesc = !string.IsNullOrEmpty(description) ? description : "";

        try
        {
            PasswordManager.Instance.AddPassword(password, saveDesc, savePatterns);
            System.Diagnostics.Debug.WriteLine(
                $"TrySavePassword: saved (desc='{saveDesc}', patterns=[{string.Join("; ", savePatterns)}])");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TrySavePassword: failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 判断异常是否表示需要密码。
    /// </summary>
    public static bool IsPasswordError(Exception ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("password") || msg.Contains("encrypted") ||
               msg.Contains("decrypt") || msg.Contains("encryption");
    }

    /// <summary>
    /// 判断异常是否属于密码相关的错误（含 SharpSevenZip "data error"）。
    /// </summary>
    public static bool IsPasswordOrCorruptedDataError(Exception ex, bool hasEncrypted)
    {
        if (IsPasswordError(ex)) return true;
        if (!hasEncrypted) return false;
        var msg = ex.Message;
        return msg.Contains("data error", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("corrupted", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 限制写入量的 Stream，用于密码验证时只提取一小部分数据即可确认密码正确。
    /// 写入超过 maxBytes 后静默丢弃，不抛异常（避免干扰 SharpSevenZip 内部状态机）。
    /// </summary>
    private sealed class BoundedWriteStream : Stream
    {
        private long _written;
        private readonly long _maxBytes;

        public BoundedWriteStream(long maxBytes) { _maxBytes = maxBytes; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_written >= _maxBytes) return;
            var toWrite = Math.Min(count, _maxBytes - _written);
            _written += toWrite;
        }
    }

    // ── 新增：精确分类方法 ──

    /// <summary>
    /// 快速验证密码，返回精确分类结果。
    /// </summary>
    public PasswordVerifyInfo QuickVerifyPasswordEx(string archivePath, string password, IArchiveEngine engine)
    {
        try
        {
            bool ok = QuickVerifyPassword(archivePath, password, engine);
            return ok ? PasswordVerifyInfo.Success() : PasswordVerifyInfo.WrongPassword();
        }
        catch (Exception ex)
        {
            return ClassifyException(ex, engine, hasEncrypted: true);
        }
    }

    /// <summary>
    /// 从已保存密码匹配并验证，返回精确结果。
    /// 关键优化：如果是文件损坏，直接返回，不再尝试其他密码。
    /// </summary>
    public (string Password, string Description, PasswordVerifyInfo VerifyInfo)? TryMatchPasswordEx(
        string archivePath,
        IArchiveEngine engine)
    {
        const int maxAttempts = 100;
        var allMatches = PasswordManager.Instance.FindMatchingPasswords(archivePath);
        var candidatePasswords = allMatches.Count > maxAttempts
            ? allMatches.Take(maxAttempts).ToList()
            : allMatches;
        var tried = new HashSet<string>();

        foreach (var entry in candidatePasswords)
        {
            var pwd = entry.Password;
            if (!tried.Add(pwd)) continue;

            var desc = !string.IsNullOrEmpty(entry.Description) ? entry.Description : pwd;
            var verifyInfo = QuickVerifyPasswordEx(archivePath, pwd, engine);

            if (verifyInfo.Result == PasswordVerificationResult.Success)
                return (pwd, desc, verifyInfo);

            // 关键优化：如果是文件损坏，直接返回，不再尝试其他密码
            if (verifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
                return (pwd, desc, verifyInfo);
        }
        return null;
    }

    /// <summary>
    /// 异常分类核心逻辑——根据引擎类型选择最精确的分类器。
    /// </summary>
    public static PasswordVerifyInfo ClassifyException(Exception ex, IArchiveEngine engine, bool hasEncrypted)
    {
        // 1. SharpCompress (ZIP/TAR/GZ)
        if (engine is ZipEngine)
            return ClassifySharpCompressException(ex);

        // 2. SharpSevenZip (7z/RAR/ISO)
        if (engine is SevenZipEngine)
            return ClassifySharpSevenZipException(ex);

        // 3. 通用兜底
        return ClassifyGenericException(ex, hasEncrypted);
    }

    /// <summary>
    /// SharpCompress 异常分类——CryptographicException = 密码错误，InvalidDataException = 文件损坏。
    /// </summary>
    public static PasswordVerifyInfo ClassifySharpCompressException(Exception ex)
    {
        return ex switch
        {
            CryptographicException => PasswordVerifyInfo.WrongPassword("密码错误"),
            InvalidDataException ide when ide.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                => PasswordVerifyInfo.WrongPassword("密码错误"),
            InvalidDataException => PasswordVerifyInfo.CorruptedOrInvalid("文件损坏或格式无效"),
            IOException => PasswordVerifyInfo.CorruptedOrInvalid("读取文件失败"),
            _ => ClassifyGenericException(ex, hasEncrypted: true)
        };
    }

    /// <summary>
    /// SharpSevenZip 异常分类——优先用 HRESULT 判断，回退到 Message 解析。
    /// </summary>
    public static PasswordVerifyInfo ClassifySharpSevenZipException(Exception ex)
    {
        // SharpSevenZipArchiveException 包含 HRESULT 和 Message
        if (ex is SharpSevenZipArchiveException szEx)
        {
            // HRESULT 分类（最准确）
            uint hresult = (uint)szEx.HResult;
            if (hresult == 0x80090005) // NTE_BAD_DATA - 密码错误
                return PasswordVerifyInfo.WrongPassword("密码错误");
            if (hresult == 0x80004005) // E_FAIL - 通用失败，结合 Message 判断
            {
                var msg = szEx.Message.ToLowerInvariant();
                if (msg.Contains("wrong password") || msg.Contains("password"))
                    return PasswordVerifyInfo.WrongPassword("密码错误");
                if (msg.Contains("data error") || msg.Contains("crc") || msg.Contains("corrupt"))
                    return PasswordVerifyInfo.CorruptedOrInvalid("文件损坏或数据错误");
            }
        }

        // 兜底：Message 解析
        var message = ex.Message.ToLowerInvariant();
        if (message.Contains("wrong password") || message.Contains("password"))
            return PasswordVerifyInfo.WrongPassword("密码错误");
        if (message.Contains("data error") || message.Contains("crc error") || message.Contains("corrupt"))
            return PasswordVerifyInfo.CorruptedOrInvalid("文件损坏");

        return PasswordVerifyInfo.Unknown(ex.Message);
    }

    /// <summary>
    /// 通用异常分类——基于 Message 关键词判断。
    /// </summary>
    public static PasswordVerifyInfo ClassifyGenericException(Exception ex, bool hasEncrypted)
    {
        var msg = ex.Message.ToLowerInvariant();
        if (msg.Contains("password") || msg.Contains("encrypted") || msg.Contains("decrypt"))
            return PasswordVerifyInfo.WrongPassword("密码错误");
        if (hasEncrypted && (msg.Contains("data error") || msg.Contains("corrupt")))
            return PasswordVerifyInfo.CorruptedOrInvalid("文件损坏");
        return PasswordVerifyInfo.Unknown(ex.Message);
    }
}

/// <summary>
/// 统一密码解析的结果（ViewModel 层用）。
/// </summary>
public class PasswordResult
{
    public string Password { get; set; } = "";
    public string? Description { get; set; }
    public List<string>? Patterns { get; set; }
    /// <summary>密码是否来自已保存的密码库（而非用户手动输入）。</summary>
    public bool IsFromSaved { get; set; }
}

/// <summary>
/// 密码对话框的返回结果（View 层 → ViewModel 层）。
/// </summary>
public class PasswordDialogResponse
{
    public string? Password { get; set; }
    public bool RememberInSession { get; set; } = true;
    public bool SavePermanently { get; set; }
    public string? Description { get; set; }
    public List<string>? Patterns { get; set; }
}
