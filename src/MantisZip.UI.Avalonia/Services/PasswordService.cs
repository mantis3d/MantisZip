using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SharpSevenZip;

namespace MantisZip.UI.Avalonia.Services;

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
            return true;
        }
        catch
        {
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
