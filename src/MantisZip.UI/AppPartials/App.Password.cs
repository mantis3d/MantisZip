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
            catch { /* UI 可能不可用（CLI 模式）*/ }
            return false;
        }
    }

    /// <summary>
    /// QuickVerify + 全量解压 + 密码区 UI 更新。
    /// 解压成功返回 true，密码错误弹窗后返回 false。
    /// </summary>
    internal static async Task<bool> ExtractWithPasswordAsync(
        string archivePath, string destinationPath, IArchiveEngine engine,
        string password, string description, ProgressWindow progressWindow,
        IProgress<ArchiveProgress> progress, CancellationToken ct,
        bool showPwdSection, bool? rememberPwd = null,
        string? pwdDesc = null, List<string>? pwdPatterns = null)
    {
        LogDebug("ExtractWithPasswordAsync: archive='{0}', dest='{1}', desc='{2}', remember={3}",
            archivePath, destinationPath, description, rememberPwd);
        if (showPwdSection) progressWindow.ShowPasswordAttempt(description);
        if (!QuickVerifyPassword(archivePath, password, engine))
        {
            LogDebug("ExtractWithPasswordAsync: quick verify failed for '{0}'", description);
            return false;
        }

        LogDebug("ExtractWithPasswordAsync: quick verify passed for '{0}'", description);
        if (showPwdSection) progressWindow.ShowPasswordMatched(password, description);

        var opts = CreateExtractOptions();
        await engine.ExtractAsync(archivePath, destinationPath, password, progress, ct, opts);
        LogDebug("ExtractWithPasswordAsync: extraction done");

        if (rememberPwd == true && !string.IsNullOrEmpty(password))
        {
            TrySavePassword(password, archivePath, pwdPatterns, pwdDesc);
        }
        return true;
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
            LogDebug("HasEncryptedEntries: 无法检查压缩包 '{0}'，保守假定有加密: {1}", archivePath, ex.Message);
            return true;
        }
    }

    /// <summary>
    /// 快速验证密码是否正确——读第一个加密条目 1 字节，
    /// 密码不对时 SharpCompress / SharpSevenZipExtractor 会在读字节前抛异常。
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
                var encrypted = afd.Count(e => !e.IsDirectory && e.Encrypted);
                TraceLog("QuickVerifyPassword(7z): archive='{0}', totalEntries={1}, encrypted={2}",
                    archivePath, total, encrypted);
                return true;
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
    /// 判断异常是否表示需要密码。
    /// </summary>
    internal static bool IsPasswordError(Exception ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("password") || msg.Contains("encrypted") ||
               msg.Contains("decrypt") || msg.Contains("encryption");
    }
}
