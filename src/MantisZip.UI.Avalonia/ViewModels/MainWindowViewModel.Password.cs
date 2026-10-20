using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Threading;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// MainWindowViewModel 的 密码会话/状态部分（自 MainWindowViewModel.cs 拆分，纯移动）。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// 会话密码缓存：压缩包路径|格式 → 密码（仅内存，不持久化）。
    /// Key 格式: "{path}|{format}"，防止同名不同格式压缩包（如 test.zip / test.7z）密码冲突。
    /// </summary>
    private readonly Dictionary<string, string> _sessionPasswords = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 生成会话密码缓存键（路径 + 格式）。
    /// </summary>
    private static string GetSessionPasswordKey(string path, ArchiveFormat format)
        => $"{path}|{format}";

    /// <summary>
    /// 从路径获取会话密码缓存键（自动推断格式）。
    /// </summary>
    private string GetSessionPasswordKey(string path)
        => GetSessionPasswordKey(path, ArchiveFormatHelper.GetFormat(path));

    private readonly PasswordService _passwordService = new();

    /// <summary>
    /// 密码服务（供预览面板密码持久化使用）。
    /// </summary>
    public PasswordService PasswordService => _passwordService;
    private readonly AppSettings _appSettings = AppSettings.Load();
    private string? _currentPassword;
    private bool _hasEncryptedArchive;
    /// <summary>当前已匹配密码的描述（供 MatchedPasswordDialog 展示，会话级）。</summary>
    private string? _currentPasswordDescription;
    /// <summary>当前已匹配密码的匹配规则（供 MatchedPasswordDialog 展示，会话级）。</summary>
    private List<string>? _currentPasswordPatterns;

    /// <summary>
    /// 更新密码状态 UI（锁图标 + 状态文字 + 工具栏密码按钮三态）。
    /// </summary>
    private void UpdatePasswordStatus(bool isMatched)
    {
        if (!_hasEncryptedArchive)
        {
            PasswordStatusMessage = null;
            PasswordStatusIcon = null;
            UpdateToolbarPasswordState();
            return;
        }

        PasswordStatusIcon = isMatched
            ? Application.Current?.FindResource("IconLockOpen")
            : Application.Current?.FindResource("IconLockClosed");
        PasswordStatusMessage = isMatched
            ? LocalizationManager.T("Status_PasswordMatched")
            : LocalizationManager.T("Status_Encrypted");
        UpdateToolbarPasswordState();
    }

    // ── 工具栏密码按钮（对齐 WPF EnterPasswordBtn 三态：无加密禁用 / 未匹配红锁 / 已匹配绿开锁） ──

    /// <summary>工具栏密码按钮图标 Geometry。</summary>
    [ObservableProperty]
    private object? _toolbarPasswordIcon;

    /// <summary>工具栏密码按钮前景色（默认主题色 / 错误红 / 成功绿）。</summary>
    [ObservableProperty]
    private global::Avalonia.Media.IBrush? _toolbarPasswordForeground;

    /// <summary>工具栏密码按钮 tooltip（三态文案）。</summary>
    [ObservableProperty]
    private string? _toolbarPasswordTooltip;

    /// <summary>当前压缩包密码已匹配（驱动按钮绿色高亮 Classes.passwordMatched）。</summary>
    [ObservableProperty]
    private bool _hasMatchedPassword;

    private bool CanEnterPassword() => !string.IsNullOrEmpty(CurrentArchivePath) && _hasEncryptedArchive;

    /// <summary>
    /// 更新工具栏密码按钮三态显示并刷新命令可用性。
    /// </summary>
    private void UpdateToolbarPasswordState()
    {
        var app = Application.Current;
        if (string.IsNullOrEmpty(CurrentArchivePath) || !_hasEncryptedArchive)
        {
            // 无加密 → 禁用，默认钥匙图标
            ToolbarPasswordIcon = app?.FindResource("IconKey");
            ToolbarPasswordForeground = app?.FindResource("ThemeTextPrimaryBrush") as global::Avalonia.Media.IBrush;
            HasMatchedPassword = false;
            ToolbarPasswordTooltip = LocalizationManager.T("Toolbar_PasswordTooltip");
        }
        else if (_currentPassword == null)
        {
            // 已加密未匹配 → 红色闭锁
            ToolbarPasswordIcon = app?.FindResource("IconLockClosed");
            ToolbarPasswordForeground = app?.FindResource("ThemeStatusErrorBrush") as global::Avalonia.Media.IBrush;
            HasMatchedPassword = false;
            ToolbarPasswordTooltip = LocalizationManager.T("Toolbar_PasswordMissingTooltip");
        }
        else
        {
            // 已匹配 → 绿色开锁 + 绿底高亮
            ToolbarPasswordIcon = app?.FindResource("IconLockOpen");
            ToolbarPasswordForeground = app?.FindResource("ThemeStatusSuccessBrush") as global::Avalonia.Media.IBrush;
            HasMatchedPassword = true;
            ToolbarPasswordTooltip = LocalizationManager.T("Toolbar_PasswordMatchedTooltip");
        }
        EnterPasswordCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 在密码库中查找与 (archivePath, password) 匹配的已保存条目（供展示描述/规则）。
    /// </summary>
    private static Core.PasswordEntry? FindSavedPasswordEntry(string archivePath, string password)
    {
        try
        {
            return PasswordManager.Instance.FindMatchingPasswords(archivePath)
                .FirstOrDefault(e => e.Password == password);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取当前已匹配密码的描述与规则：优先取会话记录（手动输入），否则回查密码库（自动匹配）。
    /// </summary>
    private (string? Description, List<string> Patterns) GetMatchedPasswordInfo()
    {
        if (_currentPasswordDescription != null || _currentPasswordPatterns != null)
            return (_currentPasswordDescription, _currentPasswordPatterns ?? new List<string>());

        var saved = FindSavedPasswordEntry(CurrentArchivePath ?? "", _currentPassword ?? "");
        return (saved?.Description, saved != null ? new List<string>(saved.Patterns) : new List<string>());
    }

    /// <summary>
    /// 工具栏「密码」按钮：针对当前压缩包输入/查看密码（对齐 WPF EnterPassword_Click）。
    /// 已匹配 → 打开查看/复制对话框；未匹配 → 输入密码并快速验证，可选保存到密码库。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEnterPassword))]
    private async Task EnterPasswordAsync()
    {
        if (string.IsNullOrEmpty(CurrentArchivePath)) return;

        // 已匹配 → 查看/复制对话框
        if (_currentPassword != null)
        {
            if (ShowMatchedPasswordDialog == null) return;
            var (desc, patterns) = GetMatchedPasswordInfo();
            await ShowMatchedPasswordDialog(new Core.PasswordEntry
            {
                Password = _currentPassword,
                Description = desc ?? "",
                Patterns = patterns
            }, Path.GetFileName(CurrentArchivePath));
            return;
        }

        // 未匹配 → 密码输入对话框
        if (ShowPasswordDialog == null) return;
        var response = await ShowPasswordDialog(CurrentArchivePath);
        if (response?.Password == null) return;

        var engine = ArchiveEngineFactory.GetEngineByExtension(CurrentArchivePath);
        if (engine != null)
        {
            var verifyInfo = _passwordService.QuickVerifyPasswordEx(CurrentArchivePath, response.Password, engine);
            if (verifyInfo.Result == PasswordVerificationResult.WrongPassword)
            {
                StatusMessage = LocalizationManager.T("Status_WrongPassword");
                await AppMessageBox.Show(
                    LocalizationManager.T("Status_WrongPassword"),
                    LocalizationManager.T("App_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (verifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
            {
                StatusMessage = LocalizationManager.T("Status_ArchiveCorrupted");
                await AppMessageBox.Show(
                    LocalizationManager.T("Status_ArchiveCorrupted"),
                    LocalizationManager.T("App_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        _sessionPasswords[GetSessionPasswordKey(CurrentArchivePath, _currentFormat)] = response.Password;
        _currentPassword = response.Password;
        _currentPasswordDescription = response.Description;
        _currentPasswordPatterns = response.Patterns is { Count: > 0 } ? response.Patterns.ToList() : null;

        if (response.SavePermanently)
        {
            var saved = _passwordService.TrySavePassword(response.Password, CurrentArchivePath, response.Patterns, response.Description);
            App.DebugLog($"TrySavePassword (toolbar): savePermanently=true, result={saved}, path={CurrentArchivePath}");
        }

        UpdatePasswordStatus(isMatched: true);
        StatusMessage = LocalizationManager.T("Status_PasswordMatched");
    }

    /// <summary>
    /// 确保当前压缩包已取得可用密码（对齐解压/打开流程）：
    /// 会话缓存 → 密码库自动匹配（含快速验证）→ 密码对话框循环（错密码重试直到正确或取消）。
    /// 返回 true=密码已就绪（含非加密包无需密码），false=应中止流程（用户取消/档案损坏/无对话框）。
    /// </summary>
    private async Task<bool> TryEnsureArchivePasswordAsync()
    {
        if (string.IsNullOrEmpty(CurrentArchivePath)) return false;

        // 会话缓存已有密码 → 直接使用（打开时已快速验证过，无需重复验证）
        _sessionPasswords.TryGetValue(GetSessionPasswordKey(CurrentArchivePath, _currentFormat), out var password);
        if (password != null) return true;

        // 非加密包 → 无需密码
        if (!_hasEncryptedArchive) return true;

        var engine = ArchiveEngineFactory.GetEngineByExtension(CurrentArchivePath);
        if (engine == null) return false;

        // Phase A：已保存密码静默自动匹配（内部含快速验证，对齐 LoadArchiveAsync）
        var match = _passwordService.TryMatchPasswordEx(CurrentArchivePath, engine);
        if (match != null)
        {
            var (matchedPwd, _, verifyInfo) = match.Value;

            // 档案损坏：直接报告错误，不再尝试其他密码
            if (verifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
            {
                App.DebugLog($"[Test] Archive corrupted during auto-match: {verifyInfo.DetailMessage}");
                StatusMessage = LocalizationManager.T("Status_ArchiveCorrupted");
                await AppMessageBox.Show(
                    LocalizationManager.T("Status_ArchiveCorrupted"),
                    LocalizationManager.T("App_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Success：写入会话缓存与已匹配状态
            _sessionPasswords[GetSessionPasswordKey(CurrentArchivePath, _currentFormat)] = matchedPwd;
            _currentPassword = matchedPwd;
            var savedEntry = FindSavedPasswordEntry(CurrentArchivePath, matchedPwd);
            _currentPasswordDescription = savedEntry?.Description;
            _currentPasswordPatterns = savedEntry != null ? new List<string>(savedEntry.Patterns) : null;
            UpdatePasswordStatus(isMatched: true);
            return true;
        }

        // Phase B：密码对话框循环（错密码提示后重试直到正确或取消，对齐 LoadArchiveAsync Phase B）
        while (_currentPassword == null)
        {
            if (ShowPasswordDialog == null)
            {
                StatusMessage = LocalizationManager.T("Status_PasswordRequired");
                return false;
            }

            var dialogResponse = await ShowPasswordDialog(CurrentArchivePath);
            if (dialogResponse?.Password == null)
            {
                StatusMessage = LocalizationManager.T("Status_PasswordCancelled");
                return false; // 用户取消
            }

            var verifyInfo = _passwordService.QuickVerifyPasswordEx(CurrentArchivePath, dialogResponse.Password, engine);
            if (verifyInfo.Result == PasswordVerificationResult.WrongPassword)
            {
                StatusMessage = LocalizationManager.T("Status_WrongPassword");
                continue;
            }
            if (verifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
            {
                StatusMessage = LocalizationManager.T("Status_ArchiveCorrupted");
                await AppMessageBox.Show(
                    LocalizationManager.T("Status_ArchiveCorrupted"),
                    LocalizationManager.T("App_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Success：写入会话缓存与已匹配状态
            _sessionPasswords[GetSessionPasswordKey(CurrentArchivePath, _currentFormat)] = dialogResponse.Password;
            _currentPassword = dialogResponse.Password;
            _currentPasswordDescription = dialogResponse.Description;
            _currentPasswordPatterns = dialogResponse.Patterns is { Count: > 0 } ? dialogResponse.Patterns.ToList() : null;

            if (dialogResponse.SavePermanently)
            {
                var saved = _passwordService.TrySavePassword(dialogResponse.Password, CurrentArchivePath,
                    dialogResponse.Patterns, dialogResponse.Description);
                App.DebugLog($"TrySavePassword (test flow): savePermanently=true, result={saved}, path={CurrentArchivePath}");
            }

            UpdatePasswordStatus(isMatched: true);
            return true;
        }

        return true;
    }

}
