using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 压缩设置对话框的 ViewModel。
/// 用户选择格式、压缩级别、输出路径、密码和注释，通过 CloseAction 回调返回结果。
/// </summary>
public partial class CompressSettingsViewModel : ObservableObject
{
    /// <summary>源文件/目录路径列表（可修改，显示用）。</summary>
    public ObservableCollection<string> SelectedPaths { get; } = new();

    /// <summary>本地化字符串字典，XAML 通过 {Binding LocalizedStrings[Key]} 访问。</summary>
    public Dictionary<string, string> LocalizedStrings { get; } = new();

    /// <summary>支持的格式选项列表，绑定到格式 ComboBox。来自 <see cref="CompressionOptionData"/> 共享数据源。</summary>
    public List<string> FormatOptions { get; } = new(CompressionOptionData.ArchiveFormatValues);

    /// <summary>源文件摘要文字，用于界面显示。</summary>
    public string SelectedPathsSummary => SelectedPaths.Count > 0
        ? LocalizationManager.T("Compress_NItemsSelected", SelectedPaths.Count)
        : LocalizationManager.T("Compress_NoFilesSelected");

    /// <summary>由 View 设置的文件保存选择回调。返回选择的路径，取消返回 null。</summary>
    public Func<Task<string?>>? BrowseOutput { get; set; }

    /// <summary>由 View 设置的文件选择回调。返回选择的文件路径列表，取消返回 null。</summary>
    public Func<Task<IReadOnlyList<string>?>>? PickFiles { get; set; }

    /// <summary>由 View 设置的文件夹选择回调。返回选择的路径，取消返回 null。</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>由 View 设置的关闭回调。参数 true=确认压缩，false=取消。</summary>
    public Func<bool, Task>? CloseAction { get; set; }

    [ObservableProperty]
    private string _defaultFormat = "zip";

    [ObservableProperty]
    private int _compressionLevel = 5;

    [ObservableProperty]
    private string? _outputPath;

    [ObservableProperty]
    private string? _password;

    [ObservableProperty]
    private string? _confirmPassword;

    [ObservableProperty]
    private bool _encrypt;

    [ObservableProperty]
    private string? _comment;

    [ObservableProperty]
    private CommentDistribution _commentDistribution = CommentDistribution.AllSame;

    // -- Password mode (library vs new password)

    [ObservableProperty]
    private bool _isPasswordLibraryMode = true;

    [ObservableProperty]
    private string _passwordSearchText = "";

    [ObservableProperty]
    private Core.PasswordEntry? _selectedPasswordEntry;

    [ObservableProperty]
    private bool _saveToLibrary = true;

    [ObservableProperty]
    private string _passwordDescription = "";

    [ObservableProperty]
    private bool _autoGenerateRules = true;

    [ObservableProperty]
    private string _rulesText = "";

    [ObservableProperty]
    private bool _isPasswordRevealed;

    /// <summary>Filtered list of password library entries.</summary>
    public ObservableCollection<Core.PasswordEntry> FilteredPasswordEntries { get; } = new();

    /// <summary>Password strength as numeric value 0-4 for visual indicator.</summary>
    public int PasswordStrengthValue
    {
        get
        {
            if (string.IsNullOrEmpty(Password))
                return -1;
            return GetPasswordStrength(Password);
        }
    }

    /// <summary>Visual password strength indicator (●●●●).</summary>
    public string PasswordStrengthIndicator
    {
        get
        {
            if (string.IsNullOrEmpty(Password))
                return "○○○○";
            int strength = GetPasswordStrength(Password);
            return new string('●', Math.Max(1, strength)).PadRight(4, '○');
        }
    }

    [ObservableProperty]
    private string _windowTitle = LocalizationManager.T("Compress_Title");

    // -- Comment radio button backing (sync via partial methods)

    [ObservableProperty]
    private bool _commentAllSame = true;

    [ObservableProperty]
    private bool _commentFirstOnly;

    [ObservableProperty]
    private bool _commentPerLine;

    /// <summary>密码与确认密码是否匹配。</summary>
    public bool PasswordsMatch => Password == ConfirmPassword;

    /// <summary>密码强度描述（None / Weak / Medium / Strong）。</summary>
    public string PasswordStrength
    {
        get
        {
            if (string.IsNullOrEmpty(Password))
                return LocalizationManager.T("Compress_Strength_None");

            if (Password.Length < 4)
                return LocalizationManager.T("Compress_Strength_Weak");

            bool hasUpper = Password.Any(char.IsUpper);
            bool hasLower = Password.Any(char.IsLower);
            bool hasDigit = Password.Any(char.IsDigit);
            bool hasSpecial = Password.Any(c => !char.IsLetterOrDigit(c));

            int types = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);

            if (Password.Length >= 12 && types >= 3)
                return LocalizationManager.T("Compress_Strength_Strong");

            if (Password.Length >= 8 && types >= 2)
                return LocalizationManager.T("Compress_Strength_Medium");

            return LocalizationManager.T("Compress_Strength_Weak");
        }
    }

    public CompressSettingsViewModel(IReadOnlyList<string> sourcePaths)
    {
        foreach (var p in sourcePaths)
            SelectedPaths.Add(p);
        SelectedPaths.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedPathsSummary));
            UpdateAutoRules();
        };

        // Populate localized strings
        LocalizedStrings["Compress_TabGeneral"] = LocalizationManager.T("Compress_TabGeneral");
        LocalizedStrings["Compress_TabPassword"] = LocalizationManager.T("Compress_TabPassword");
        LocalizedStrings["Compress_TabComment"] = LocalizationManager.T("Compress_TabComment");
        LocalizedStrings["Compress_Format"] = LocalizationManager.T("Compress_Format");
        LocalizedStrings["Compress_Level"] = LocalizationManager.T("Compress_Level");
        LocalizedStrings["Compress_OutputPath"] = LocalizationManager.T("Compress_OutputPath");
        LocalizedStrings["Compress_OutputPlaceholder"] = LocalizationManager.T("Compress_OutputPlaceholder");
        LocalizedStrings["Compress_SourceFiles"] = LocalizationManager.T("Compress_SourceFiles");
        LocalizedStrings["Compress_Archive_Group"] = LocalizationManager.T("Compress_Archive_Group");
        LocalizedStrings["Compress_AddFile"] = LocalizationManager.T("Compress_AddFile");
        LocalizedStrings["Compress_AddFolder"] = LocalizationManager.T("Compress_AddFolder");
        LocalizedStrings["Compress_Remove"] = LocalizationManager.T("Compress_Remove");
        LocalizedStrings["Compress_Browse"] = LocalizationManager.T("Compress_Browse");
        LocalizedStrings["Compress_Password"] = LocalizationManager.T("Compress_Password");
        LocalizedStrings["Compress_PasswordPlaceholder"] = LocalizationManager.T("Compress_PasswordPlaceholder");
        LocalizedStrings["Compress_ConfirmPassword"] = LocalizationManager.T("Compress_ConfirmPassword");
        LocalizedStrings["Compress_ConfirmPlaceholder"] = LocalizationManager.T("Compress_ConfirmPlaceholder");
        LocalizedStrings["Compress_ShowPassword"] = LocalizationManager.T("Compress_ShowPassword");
        LocalizedStrings["Compress_EncryptArchive"] = LocalizationManager.T("Compress_EncryptArchive");
        LocalizedStrings["Compress_Strength"] = LocalizationManager.T("Compress_Strength");
        LocalizedStrings["Compress_Comment"] = LocalizationManager.T("Compress_Comment");
        LocalizedStrings["Compress_CommentPlaceholder"] = LocalizationManager.T("Compress_CommentPlaceholder");
        LocalizedStrings["Compress_CommentHint"] = LocalizationManager.T("Compress_CommentHint");
        LocalizedStrings["Compress_Distribution"] = LocalizationManager.T("Compress_Distribution");
        LocalizedStrings["Compress_Distribute_AllSame"] = LocalizationManager.T("Compress_Distribute_AllSame");
        LocalizedStrings["Compress_Distribute_FirstOnly"] = LocalizationManager.T("Compress_Distribute_FirstOnly");
        LocalizedStrings["Compress_Distribute_PerLine"] = LocalizationManager.T("Compress_Distribute_PerLine");
        LocalizedStrings["Compress_Pwd_Library"] = LocalizationManager.T("Compress_Pwd_Library");
        LocalizedStrings["Compress_Pwd_NewPassword"] = LocalizationManager.T("Compress_Pwd_NewPassword");
        LocalizedStrings["Compress_Pwd_Search"] = LocalizationManager.T("Compress_Pwd_Search");
        LocalizedStrings["Compress_Pwd_NoEntry"] = LocalizationManager.T("Compress_Pwd_NoEntry");
        LocalizedStrings["Compress_Pwd_Selected"] = LocalizationManager.T("Compress_Pwd_Selected");
        LocalizedStrings["Compress_Pwd_EnterPwd"] = LocalizationManager.T("Compress_Pwd_EnterPwd");
        LocalizedStrings["Compress_Pwd_ConfirmPwd"] = LocalizationManager.T("Compress_Pwd_ConfirmPwd");
        LocalizedStrings["Compress_Pwd_Match"] = LocalizationManager.T("Compress_Pwd_Match");
        LocalizedStrings["Compress_Pwd_NoMatch"] = LocalizationManager.T("Compress_Pwd_NoMatch");
        LocalizedStrings["Compress_Pwd_SaveToLibrary"] = LocalizationManager.T("Compress_Pwd_SaveToLibrary");
        LocalizedStrings["Compress_Pwd_UpdateRules"] = LocalizationManager.T("Compress_Pwd_UpdateRules");
        LocalizedStrings["Compress_Pwd_Description"] = LocalizationManager.T("Compress_Pwd_Description");
        LocalizedStrings["Compress_Pwd_DescWatermark"] = LocalizationManager.T("Compress_Pwd_DescWatermark");
        LocalizedStrings["Compress_Pwd_AutoRules"] = LocalizationManager.T("Compress_Pwd_AutoRules");
        LocalizedStrings["Compress_Pwd_Rules"] = LocalizationManager.T("Compress_Pwd_Rules");
        LocalizedStrings["Compress_Pwd_RulesWatermark"] = LocalizationManager.T("Compress_Pwd_RulesWatermark");
        LocalizedStrings["Compress_Start"] = LocalizationManager.T("Compress_Start");
        LocalizedStrings["Compress_Cancel"] = LocalizationManager.T("Compress_Cancel");

        // Load password library
        LoadPasswordLibrary();
    }

    partial void OnPasswordChanged(string? value)
    {
        OnPropertyChanged(nameof(PasswordStrength));
        OnPropertyChanged(nameof(PasswordStrengthValue));
        OnPropertyChanged(nameof(PasswordStrengthIndicator));
        OnPropertyChanged(nameof(PasswordsMatch));
    }

    partial void OnConfirmPasswordChanged(string? value)
    {
        OnPropertyChanged(nameof(PasswordsMatch));
    }

    partial void OnIsPasswordLibraryModeChanged(bool value)
    {
        if (!value)
        {
            // Switching to new password mode — clear selected entry
            SelectedPasswordEntry = null;
        }
        else
        {
            // Switching to library mode — refresh list
            ApplyPasswordFilter();
        }
        OnPropertyChanged(nameof(IsPasswordLibraryMode));
    }

    partial void OnSelectedPasswordEntryChanged(Core.PasswordEntry? value)
    {
        if (value != null)
        {
            Password = value.Password;
            PasswordDescription = value.Description;
            RulesText = value.PatternsDisplay;
            ConfirmPassword = value.Password; // auto-match in library mode
        }
    }

    partial void OnPasswordSearchTextChanged(string value)
    {
        ApplyPasswordFilter();
    }

    partial void OnCommentAllSameChanged(bool value)
    {
        if (value)
        {
            CommentFirstOnly = false;
            CommentPerLine = false;
            CommentDistribution = CommentDistribution.AllSame;
        }
    }

    partial void OnCommentFirstOnlyChanged(bool value)
    {
        if (value)
        {
            CommentAllSame = false;
            CommentPerLine = false;
            CommentDistribution = CommentDistribution.FirstOnly;
        }
    }

    partial void OnCommentPerLineChanged(bool value)
    {
        if (value)
        {
            CommentAllSame = false;
            CommentFirstOnly = false;
            CommentDistribution = CommentDistribution.PerLine;
        }
    }

    /// <summary>
    /// Load all password entries from PasswordManager into the filtered list.
    /// </summary>
    public void LoadPasswordLibrary()
    {
        FilteredPasswordEntries.Clear();
        var entries = PasswordManager.Instance.GetAllPasswords()
            .OrderByDescending(e => e.LastUsed ?? DateTime.MinValue)
            .ToList();
        foreach (var entry in entries)
        {
            FilteredPasswordEntries.Add(entry);
        }
    }

    /// <summary>
    /// Apply search text filter to the password library.
    /// </summary>
    public void ApplyPasswordFilter()
    {
        var allEntries = PasswordManager.Instance.GetAllPasswords()
            .OrderByDescending(e => e.LastUsed ?? DateTime.MinValue);

        FilteredPasswordEntries.Clear();
        foreach (var entry in allEntries)
        {
            if (string.IsNullOrEmpty(PasswordSearchText) ||
                entry.Description.Contains(PasswordSearchText, StringComparison.OrdinalIgnoreCase) ||
                entry.PatternsDisplay.Contains(PasswordSearchText, StringComparison.OrdinalIgnoreCase))
            {
                FilteredPasswordEntries.Add(entry);
            }
        }
    }

    /// <summary>
    /// Refresh auto-rules from source file paths.
    /// </summary>
    public void RefreshAutoRules()
    {
        // Generate rules from file extensions of selected paths
        var extensions = SelectedPaths
            .Select(p => Path.GetExtension(p))
            .Where(e => !string.IsNullOrEmpty(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (extensions.Count > 0)
        {
            RulesText = string.Join(", ", extensions.Select(e => $"*{e}"));
        }
        else
        {
            RulesText = string.Join(", ", SelectedPaths.Select(p => Path.GetFileName(p)));
        }
    }

    /// <summary>
    /// Get password strength as numeric value 0-4.
    /// </summary>
    public static int GetPasswordStrength(string? pwd)
    {
        if (string.IsNullOrEmpty(pwd)) return -1;
        int score = 0;
        if (pwd.Length >= 8) score++;
        if (pwd.Any(char.IsUpper) && pwd.Any(char.IsLower)) score++;
        if (pwd.Any(char.IsDigit)) score++;
        if (pwd.Any(c => !char.IsLetterOrDigit(c))) score++;
        return Math.Min(score, 4);
    }

    [RelayCommand]
    private void TogglePasswordMode()
    {
        IsPasswordLibraryMode = !IsPasswordLibraryMode;
    }

    [RelayCommand]
    private void TogglePasswordReveal()
    {
        IsPasswordRevealed = !IsPasswordRevealed;
    }

    [RelayCommand]
    private void ClearPasswordSearch()
    {
        PasswordSearchText = "";
    }

    [RelayCommand]
    private async Task BrowseOutputPath()
    {
        if (BrowseOutput == null) return;
        var path = await BrowseOutput();
        if (!string.IsNullOrEmpty(path))
        {
            OutputPath = path;
        }
    }

    [RelayCommand]
    private async Task StartCompress()
    {
        // Validate passwords match if encrypting
        if (Encrypt && !PasswordsMatch)
        {
            return;
        }

        if (CloseAction != null)
            await CloseAction(true);
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (CloseAction != null)
            await CloseAction(false);
    }

    // ── Source file management ──

    [RelayCommand]
    private async Task AddFiles()
    {
        if (PickFiles == null) return;
        var files = await PickFiles();
        if (files != null)
        {
            foreach (var f in files)
            {
                if (!SelectedPaths.Contains(f))
                    SelectedPaths.Add(f);
            }
        }
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        if (PickFolder == null) return;
        var folder = await PickFolder();
        if (!string.IsNullOrEmpty(folder) && !SelectedPaths.Contains(folder))
        {
            SelectedPaths.Add(folder);
        }
    }

    [RelayCommand]
    private void RemoveSelected(object? selectedItem)
    {
        if (selectedItem is string path)
        {
            SelectedPaths.Remove(path);
        }
    }

    private void UpdateAutoRules()
    {
        // Re-generate auto-rules from current file list
        var extensions = SelectedPaths
            .Select(p => Path.GetExtension(p))
            .Where(e => !string.IsNullOrEmpty(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(e => $"*{e}")
            .ToList();
        if (extensions.Count > 0 && AutoGenerateRules)
        {
            RulesText = string.Join(", ", extensions);
        }
    }
}
