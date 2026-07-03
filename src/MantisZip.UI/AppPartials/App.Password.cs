using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using MantisZip.UI.Localization;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SharpSevenZip;

namespace MantisZip.UI;

/// <summary>
/// 密码管理相关方法 — 保存密码匹配、快速验证、密码输入对话框
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 从已保存密码中匹配并快速验证。返回 (密码, 描述) 或 null。
    /// limitReached 表示匹配到的密码超过上限（防暴力破解），已截断。
    /// </summary>
    internal static (string Password, string Description)? TryMatchPassword(
        string archivePath, IArchiveEngine engine, ProgressWindow? progressWindow,
        bool showPwdSection, out bool limitReached)
    {
        const int maxAttempts = 100;
        var allMatches = PasswordManager.Instance.FindMatchingPasswords(archivePath);
        limitReached = allMatches.Count > maxAttempts;
        var candidatePasswords = limitReached ? allMatches.Take(maxAttempts).ToList() : allMatches;
        var tried = new HashSet<string>();

        LogDebug("TryMatchPassword: archive='{0}', {1} candidates found (limitReached={2})",
            archivePath, candidatePasswords.Count, limitReached);

        foreach (var entry in candidatePasswords)
        {
            var pwd = entry.Password;
            if (!tried.Add(pwd)) continue;

            var desc = !string.IsNullOrEmpty(entry.Description) ? entry.Description : pwd;
            if (showPwdSection) progressWindow?.ShowPasswordAttempt(desc);

            if (QuickVerifyPassword(archivePath, pwd, engine))
            {
                LogDebug("TryMatchPassword: password matched: desc='{0}'", desc);
                if (showPwdSection) progressWindow?.ShowPasswordMatched(pwd, desc);
                return (pwd, desc);
            }
            LogDebug("TryMatchPassword: password '{0}' failed quick verify", desc);
        }
        LogDebug("TryMatchPassword: no saved password matched for '{0}'", archivePath);
        return null;
    }

    /// <summary>
    /// 弹出密码输入框，返回 (密码, 是否记住, 描述, 规则列表) 或 null（用户取消）。
    /// 会隐藏并恢复 progressWindow 避免被挡住。
    /// </summary>
    internal static (string? Password, bool Remember, string? Description, List<string>? Patterns)? PromptForPassword(
        string archivePath, ProgressWindow progressWindow, Window? owner)
    {
        return progressWindow.Dispatcher.Invoke(() =>
        {
            progressWindow.Hide();
            var dialog = new PasswordDialog(Path.GetFileName(archivePath));
            dialog.Owner = owner;
            if (owner == null)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Topmost = true;
            }
            PasswordDialogResult? result = null;
            if (dialog.ShowDialog() == true)
            {
                result = new PasswordDialogResult
                {
                    Password = dialog.ResultPassword,
                    Remember = dialog.RememberPassword,
                    Description = dialog.Description,
                    Patterns = dialog.Patterns
                };
            }
            progressWindow.Show();
            return result != null
                ? (result.Password, result.Remember, result.Description, result.Patterns)
                : default((string? Password, bool Remember, string? Description, List<string>? Patterns)?);
        });
    }

    internal class PasswordDialogResult
    {
        public string? Password { get; set; }
        public bool Remember { get; set; }
        public string? Description { get; set; }
        public List<string> Patterns { get; set; } = new();
    }

    /// <summary>
    /// 保存密码到密码库。失败时弹出提示告知用户。
    /// </summary>
    /// <param name="password">要保存的密码</param>
    /// <param name="archivePath">压缩包路径（用于生成默认匹配规则）</param>
    /// <param name="patterns">用户指定的匹配规则，为空时使用文件名</param>
    /// <param name="description">描述</param>
    /// <returns>是否保存成功</returns>
    internal static bool TrySavePassword(string password, string archivePath, List<string>? patterns, string? description)
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
            LogDebug("TrySavePassword: saved password (desc='{0}', patterns=[{1}])", saveDesc, string.Join("; ", savePatterns));
            return true;
        }
        catch (Exception pwdEx)
        {
            LogDebug("TrySavePassword: failed to save password: {0}", pwdEx.Message);
            try
            {
                AppMessageBox.Show(
                    L.TF(L.PwdMgr_SaveFailed, pwdEx.Message),
                    L.T(L.App_ErrorTitle),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception uiEx)
            {
                CoreLog.Trace("HandlePassword: UI not available: {0}", uiEx.Message);
            }
            return false;
        }
    }

    /// <summary>
    /// 统一密码解析的结果。
    /// </summary>
    internal sealed class PasswordResult
    {
        public string Password { get; set; } = "";
        public string? Description { get; set; }
        public List<string>? Patterns { get; set; }
        /// <summary>密码是否来自已保存的密码库（而非用户手动输入）。</summary>
        public bool IsFromSaved { get; set; }
    }

    /// <summary>
    /// 统一密码解析入口。处理所有格式的密码获取逻辑：
    /// 1. 检查是否需要密码
    /// 2. 尝试已保存密码（TryMatchPassword）
    /// 3. 弹出密码输入框让用户输入（循环验证直到通过或取消）
    /// 4. 返回 PasswordResult 或 null（用户取消/无需密码）
    ///
    /// progressWindow 用于执行 UI 线程调度（对话框）；
    /// 如果 progressWindow 为 null，调用方必须在 UI 线程上，且需提供 owner。
    /// </summary>
    internal static async Task<PasswordResult?> ResolvePasswordAsync(
        string archivePath,
        IArchiveEngine engine,
        IReadOnlyList<MantisZip.Core.Abstractions.ArchiveItem>? existingItems,
        ProgressWindow? progressWindow,
        Window? owner,
        CancellationToken ct)
    {
        // Step 1: 检查是否需要密码
        bool hasEncrypted;
        if (existingItems != null)
        {
            hasEncrypted = existingItems.Any(i => i.IsEncrypted);
        }
        else
        {
            // existingItems==null → 无密码无法列出（EncryptHeaders=true 7z）
            hasEncrypted = true;
        }

        if (!hasEncrypted)
        {
            LogDebug("ResolvePasswordAsync: no encrypted entries, skipping");
            return null;
        }

        LogDebug("ResolvePasswordAsync: archive='{0}', engine={1}", archivePath, engine.GetType().Name);

        bool showPwd = AppSettings.Instance.ShowPasswordMatchNotification;

        // Step 2: 尝试已保存密码
        var match = TryMatchPassword(archivePath, engine, progressWindow, showPwd, out var limitReached);
        if (match != null)
        {
            LogDebug("ResolvePasswordAsync: saved password matched: desc='{0}'", match.Value.Description);
            var matchedEntry = PasswordManager.Instance.FindMatchingPasswords(archivePath)
                .FirstOrDefault(e => e.Password == match.Value.Password && e.Description == match.Value.Description);
            return new PasswordResult
            {
                Password = match.Value.Password,
                Description = match.Value.Description,
                Patterns = matchedEntry?.Patterns?.ToList(),
                IsFromSaved = true
            };
        }

        if (limitReached && progressWindow != null)
        {
            await progressWindow.Dispatcher.InvokeAsync(() =>
                AppMessageBox.Show(L.TF(L.PwdMgr_AutoTry_LimitReached, 100),
                    L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Warning));
        }

        // Step 3: 密码对话框循环
        LogDebug("ResolvePasswordAsync: no saved password matched, prompting user");
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            PasswordResult? dialogResult;
            if (progressWindow != null)
            {
                // 通过 progressWindow.Dispatcher 调度到 UI 线程
                var prompt = PromptForPassword(archivePath, progressWindow, owner);
                if (prompt == null) return null;
                var (userPwd, remember, pwdDesc, pwdPatterns) = prompt.Value;
                if (string.IsNullOrEmpty(userPwd)) return null;

                if (QuickVerifyPassword(archivePath, userPwd, engine))
                {
                    if (remember) TrySavePassword(userPwd, archivePath, pwdPatterns, pwdDesc);
                    return new PasswordResult
                    {
                        Password = userPwd,
                        Description = pwdDesc,
                        Patterns = pwdPatterns?.ToList(),
                        IsFromSaved = false
                    };
                }

                // 密码错误
                await progressWindow.Dispatcher.InvokeAsync(() =>
                    AppMessageBox.Show(L.T(L.Main_PasswordWrong), L.T(L.App_ErrorTitle),
                        MessageBoxButton.OK, MessageBoxImage.Error));
            }
            else
            {
                // 无 progressWindow：调用方必须在 UI 线程上
                if (owner == null)
                {
                    LogDebug("ResolvePasswordAsync: no owner and no progressWindow, cannot show dialog");
                    return null;
                }

                bool userCancelled = false;
                dialogResult = await owner.Dispatcher.InvokeAsync(() =>
                {
                    var dlg = new PasswordDialog(Path.GetFileName(archivePath));
                    dlg.Owner = owner;
                    if (dlg.ShowDialog() != true) { userCancelled = true; return null; }

                    var userPwd = dlg.ResultPassword;
                    if (string.IsNullOrEmpty(userPwd)) { userCancelled = true; return null; }

                    if (QuickVerifyPassword(archivePath, userPwd, engine))
                    {
                        if (dlg.RememberPassword)
                            TrySavePassword(userPwd, archivePath, dlg.Patterns, dlg.Description);
                        return new PasswordResult
                        {
                            Password = userPwd,
                            Description = dlg.Description,
                            Patterns = dlg.Patterns?.ToList(),
                            IsFromSaved = false
                        };
                    }

                    // 密码错误 → 由外层 while 循环重试
                    AppMessageBox.Show(L.T(L.Main_PasswordWrong), L.T(L.App_ErrorTitle),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                });

                if (userCancelled) return null;           // 用户取消 → 退出整个流程
                if (dialogResult != null) return dialogResult; // 密码正确 → 返回
                // null + !userCancelled → 密码错误 → 继续循环
            }
        }
    }

    /// <summary>
    /// 快速检查压缩包是否有加密条目（不验证密码，只检查有无加密标志）。
    /// </summary>
    internal static bool HasEncryptedEntries(string archivePath, IArchiveEngine engine)
    {
        try
        {
            if (engine is ZipEngine)
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
                return archive.Entries.Any(e => e.IsEncrypted);
            }
            if (engine is SevenZipEngine)
            {
                using var extractor = new SharpSevenZipExtractor(archivePath);
                return extractor.ArchiveFileData.Any(e => !e.IsDirectory && e.Encrypted);
            }
            return false;
        }
        catch (Exception ex)
        {
            // 无法检查时保守返回 true（宁可多弹密码输入框，不可静默跳过密码导致解压失败）
            CoreLog.Trace("HasEncryptedEntries: check failed for '{0}': {1}", archivePath, ex.Message);
            LogDebug("HasEncryptedEntries: 无法检查压缩包 '{0}'，保守假定有加密: {1}", archivePath, ex.Message);
            return true;
        }
    }

    /// <summary>
    /// 快速验证密码是否正确——读第一个加密条目 1 字节（ZIP），
    /// 或提取最小加密条目的前 ~8KB（7z/RAR）。
    /// 对 EncryptHeaders=false 的 7z/RAR，ArchiveFileData 无密码即可读取，
    /// 因此需要真正提取一个加密条目来验证密码。
    /// 只捕获密码相关异常，系统级错误向上传播。
    /// </summary>
    internal static bool QuickVerifyPassword(string archivePath, string password, IArchiveEngine engine)
    {
        try
        {
            if (engine is ZipEngine)
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions { Password = password });
                var entry = archive.Entries.FirstOrDefault(e => e.IsEncrypted);
                TraceLog("QuickVerifyPassword(Zip): archive='{0}', foundEncrypted={1}, entryKey='{2}'",
                    archivePath, entry != null, entry?.Key ?? "(none)");

                if (entry == null)
                {
                    TraceLog("QuickVerifyPassword(Zip): no encrypted entries, password treated as valid");
                    return true;
                }

                TraceLog("QuickVerifyPassword(Zip): opening stream for entry '{0}'", entry.Key ?? "(nullKey)");
                using var s = entry.OpenEntryStream();
                s.ReadByte();
                TraceLog("QuickVerifyPassword(Zip): password OK for archive='{0}'", archivePath);
                return true;
            }
            else if (engine is SevenZipEngine)
            {
                using var extractor = new SharpSevenZipExtractor(archivePath, password);
                var afd = extractor.ArchiveFileData;
                var total = afd.Count;
                var encrypted = afd.Where(e => !e.IsDirectory && e.Encrypted).ToList();
                TraceLog("QuickVerifyPassword(7z): archive='{0}', totalEntries={1}, encrypted={2}",
                    archivePath, total, encrypted.Count);

                if (encrypted.Count == 0)
                {
                    // 无加密条目 → 无需密码验证
                    TraceLog("QuickVerifyPassword(7z): no encrypted entries, password treated as valid");
                    return true;
                }

                // EncryptHeaders=true 时，ArchiveFileData 会在密码错误时抛出异常被外层捕获；
                // 能到达此处说明密码对 EncryptHeaders=true 已验证通过。
                // EncryptHeaders=false 时 ArchiveFileData 无密码也能读取，需要实际提取来验证。
                // 选最小加密条目以减少提取开销。
                var smallest = encrypted.OrderBy(e => e.Size).First();
                try
                {
                    using var verifyStream = new BoundedWriteStream(maxBytes: 8192);
                    extractor.ExtractFile(smallest.Index, verifyStream);
                    TraceLog("QuickVerifyPassword(7z): password OK for archive='{0}'", archivePath);
                    return true;
                }
                catch (Exception ex) when (IsPasswordOrCorruptedDataError(ex, true))
                {
                    TraceLog("QuickVerifyPassword(7z): FAILED - password wrong or data corrupted: [{0}] {1}",
                        ex.GetType().Name, ex.Message);
                    return false;
                }
            }

            // TarGzEngine 不支持加密
            TraceLog("QuickVerifyPassword: engine '{0}' has no encryption, skipping verify",
                engine.GetType().Name);
            return true;
        }
        catch (Exception ex) when (IsPasswordError(ex))
        {
            TraceLog("QuickVerifyPassword: FAILED for archive='{0}', password len={1}: [{2}] {3}",
                archivePath, password?.Length ?? -1, ex.GetType().Name, ex.Message);
            return false;
        }
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

    /// <summary>
    /// 判断异常是否表示需要密码。
    /// </summary>
    internal static bool IsPasswordError(Exception ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("password") || msg.Contains("encrypted") ||
               msg.Contains("decrypt") || msg.Contains("encryption");
    }

    /// <summary>
    /// 判断异常是否属于密码相关的错误（含 SharpSevenZip "data error"）。
    /// SharpSevenZip 对 EncryptHeaders=false 的加密压缩包，传入错误密码时抛出
    /// "File is corrupted. Data error has occured." 而非显式密码错误。
    /// 当已知压缩包有加密条目时，需要将此异常视为密码错误。
    /// </summary>
    internal static bool IsPasswordOrCorruptedDataError(Exception ex, bool hasEncrypted)
    {
        if (IsPasswordError(ex)) return true;
        if (!hasEncrypted) return false;
        var msg = ex.Message;
        return msg.Contains("data error", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("corrupted", StringComparison.OrdinalIgnoreCase);
    }
}
