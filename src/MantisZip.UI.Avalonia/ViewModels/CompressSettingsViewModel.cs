using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
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

    // ── Output mode ──

    /// <summary>Manual 模式下的输出路径缓存，切换模式不丢失。</summary>
    private string? _cachedManualPath;

    [ObservableProperty]
    private CompressOutputMode _outputMode = CompressOutputMode.Manual;

    /// <summary>输出路径在非 Manual 模式下只读。</summary>
    public bool IsOutputPathReadOnly => OutputMode != CompressOutputMode.Manual;

    /// <summary>浏览按钮仅在 Manual 模式下显示。</summary>
    public bool IsBrowseButtonVisible => OutputMode == CompressOutputMode.Manual;

    /// <summary>文件名 / 扩展名区域仅在 Manual/Combined 模式下可见。</summary>
    public bool IsFileNameSectionVisible => OutputMode != CompressOutputMode.Separate;

    /// <summary>输出路径标签文字随模式变化。</summary>
    public string OutputPathLabel => OutputMode switch
    {
        CompressOutputMode.Separate => LocalizationManager.T("Compress_OutputMode_Separate"),
        CompressOutputMode.Combined => LocalizationManager.T("Compress_OutputMode_Combined"),
        _ => LocalizationManager.T("Compress_OutputPath"),
    };

    /// <summary>RadioButton 绑定：Manual 模式。</summary>
    public bool IsManualMode
    {
        get => OutputMode == CompressOutputMode.Manual;
        set { if (value) OutputMode = CompressOutputMode.Manual; }
    }

    /// <summary>RadioButton 绑定：Separate 模式。</summary>
    public bool IsSeparateMode
    {
        get => OutputMode == CompressOutputMode.Separate;
        set { if (value) OutputMode = CompressOutputMode.Separate; }
    }

    /// <summary>RadioButton 绑定：Combined 模式。</summary>
    public bool IsCombinedMode
    {
        get => OutputMode == CompressOutputMode.Combined;
        set { if (value) OutputMode = CompressOutputMode.Combined; }
    }

    [ObservableProperty]
    private string _defaultFormat = "zip";

    /// <summary>当前格式是否为 ZIP。</summary>
    public bool IsZipFormat => DefaultFormat == "zip";

    /// <summary>当前格式是否为 7z。</summary>
    public bool IsSevenZipFormat => DefaultFormat == "7z";

    /// <summary>当前格式是否支持加密（tar.gz 不支持）。</summary>
    public bool IsFormatEncryptionSupported => DefaultFormat != "tar.gz";

    /// <summary>压缩级别下拉列表（来自共享数据源 CompressionOptionData）。</summary>
    public List<CompressionOptionData.ComboOption> CompressionLevelOptions { get; }

    [ObservableProperty]
    private int _compressionLevel = 5;

    [ObservableProperty]
    private CompressionOptionData.ComboOption? _selectedLevelOption;

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

    /// <summary>ZIP 加密方法（共享数据源）。</summary>
    public List<CompressionOptionData.ComboOption> ZipEncryptionMethodOptions { get; }

    [ObservableProperty]
    private string _zipEncryptionMethod = "aes256";

    [ObservableProperty]
    private CompressionOptionData.ComboOption? _selectedZipEncryptionMethodOption;

    [ObservableProperty]
    private bool _sevenZipEncryptHeaders;

    // ── 分卷 ──

    /// <summary>分卷大小选项（共享数据源）。</summary>
    public List<CompressionOptionData.ComboOption> SplitSizeOptions { get; }

    [ObservableProperty]
    private CompressionOptionData.ComboOption? _selectedSplitSizeOption;

    /// <summary>自定义分卷大小文本（仅自定义模式可用）。</summary>
    [ObservableProperty]
    private string _customSplitSizeText = "";

    /// <summary>是否显示自定义分卷大小输入框。</summary>
    public bool IsCustomSplitSizeVisible => SelectedSplitSizeOption?.Tag == "-1";

    /// <summary>当前分卷大小（字节），0 表示不分卷。</summary>
    public long SplitSize
    {
        get
        {
            if (SelectedSplitSizeOption == null) return 0;
            var tag = SelectedSplitSizeOption.Tag;
            if (tag == "0") return 0;
            if (tag == "-1")
            {
                if (long.TryParse(CustomSplitSizeText, out var mb) && mb > 0)
                    return mb * 1024L * 1024L;
                return 0;
            }
            if (long.TryParse(tag, out var bytes))
                return bytes;
            return 0;
        }
    }

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

    // ── Preview tree ──

    /// <summary>预览树的根节点。</summary>
    [ObservableProperty]
    private PreviewTreeNode? _previewRoot;

    /// <summary>预览面板是否启用精简模式。</summary>
    [ObservableProperty]
    private bool _previewCompactMode = true;

    /// <summary>是否显示过滤项。</summary>
    [ObservableProperty]
    private bool _showFilteredGhosts;

    /// <summary>文件过滤条件（由 View 在对话框关闭时从 FileFilterEditor 获取并设置）。</summary>
    public FileFilterCriteria? FileFilter { get; set; }

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
            BuildCompressPreview();
            if (OutputMode != CompressOutputMode.Manual)
                RefreshOutputPathState();
            UpdateCanCompress();
        };

        // Populate localized strings
        LocalizedStrings["Compress_TabGeneral"] = LocalizationManager.T("Compress_TabGeneral");
        LocalizedStrings["Compress_TabAdvanced"] = LocalizationManager.T("Compress_TabAdvanced");
        LocalizedStrings["Compress_VolumeSize"] = LocalizationManager.T("Compress_VolumeSize");
        LocalizedStrings["Compress_TabPassword"] = LocalizationManager.T("Compress_TabPassword");
        LocalizedStrings["Compress_TabComment"] = LocalizationManager.T("Compress_TabComment");
        LocalizedStrings["Compress_TabFilter"] = LocalizationManager.T("Compress_TabFilter");
        LocalizedStrings["Compress_Format"] = LocalizationManager.T("Compress_Format");
        LocalizedStrings["Compress_Level"] = LocalizationManager.T("Compress_Level");
        LocalizedStrings["Compress_OutputMode"] = LocalizationManager.T("Compress_OutputMode");
        LocalizedStrings["Compress_OutputMode_Manual"] = LocalizationManager.T("Compress_OutputMode_Manual");
        LocalizedStrings["Compress_OutputMode_Separate"] = LocalizationManager.T("Compress_OutputMode_Separate");
        LocalizedStrings["Compress_OutputMode_Combined"] = LocalizationManager.T("Compress_OutputMode_Combined");
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
        LocalizedStrings["Compress_EncryptionMethod"] = LocalizationManager.T("Compress_EncryptionMethod");
        LocalizedStrings["Compress_ZipEncryption"] = LocalizationManager.T("Compress_ZipEncryption");
        LocalizedStrings["Compress_EncryptHeaders"] = LocalizationManager.T("Compress_EncryptHeaders");
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

        // 初始化压缩级别下拉选项（共享数据源，本地化 Display）
        CompressionLevelOptions = CompressionOptionData.LevelOptions
            .Select(o => new CompressionOptionData.ComboOption(o.Tag, LocalizationManager.T("Compress_Level_" + o.Tag switch
            {
                "0" => "Store",
                "3" => "Fast",
                "5" => "Normal",
                "9" => "Max",
                _ => "Normal",
            })))
            .ToList();
        SelectedLevelOption = CompressionLevelOptions.FirstOrDefault(
            o => o.Tag == CompressionLevel.ToString());

        // 初始化 ZIP 加密方式下拉选项（共享数据源）
        ZipEncryptionMethodOptions = CompressionOptionData.ZipEncryptionMethods
            .Select(o => new CompressionOptionData.ComboOption(o.Tag, o.Display))
            .ToList();
        SelectedZipEncryptionMethodOption = ZipEncryptionMethodOptions.FirstOrDefault(
            o => o.Tag == ZipEncryptionMethod);

        // 初始化分卷大小下拉选项
        SplitSizeOptions = CompressionOptionData.SplitSizeOptions
            .Select(o => new CompressionOptionData.ComboOption(
                o.Tag,
                o.Tag switch
                {
                    "0" => LocalizationManager.T("Compress_Volume_None"),
                    "-1" => LocalizationManager.T("Compress_Volume_Custom"),
                    _ => o.Display,
                }))
            .ToList();
        SelectedSplitSizeOption = SplitSizeOptions.FirstOrDefault(o => o.Tag == "0");

        // Load password library
        LoadPasswordLibrary();

        // Build initial compress preview from source paths
        BuildCompressPreview();
    }

    /// <summary>
    /// 构建压缩预览树。由构造函数自动调用，也可在源文件变更或过滤条件变化后重新调用。
    /// </summary>
    /// <param name="filter">文件过滤条件，不为空且 IsActive 时对文件节点标记 IsFilteredOut。</param>
    public void BuildCompressPreview(FileFilterCriteria? filter = null)
    {
        if (SelectedPaths.Count == 0)
        {
            PreviewRoot = null;
            return;
        }

        PreviewRoot = ResultPreviewService.BuildCompressPreview(
            SelectedPaths.ToList(),
            rootName: LocalizationManager.T("Compress_Title"),
            filter: filter);
    }

    partial void OnCompressionLevelChanged(int value)
    {
        // Sync the ComboBox selection when CompressionLevel is set programmatically
        if (CompressionLevelOptions is { } options)
            SelectedLevelOption = options.FirstOrDefault(o => o.Tag == value.ToString());
    }

    partial void OnSelectedLevelOptionChanged(CompressionOptionData.ComboOption? value)
    {
        if (value != null && int.TryParse(value.Tag, out var level))
            CompressionLevel = level;
    }

    partial void OnSelectedZipEncryptionMethodOptionChanged(CompressionOptionData.ComboOption? value)
    {
        if (value != null)
            ZipEncryptionMethod = value.Tag;
    }

    partial void OnZipEncryptionMethodChanged(string value)
    {
        if (ZipEncryptionMethodOptions is { } options)
            SelectedZipEncryptionMethodOption = options.FirstOrDefault(o => o.Tag == value);
    }

    partial void OnConfirmPasswordChanged(string? value)
    {
        OnPropertyChanged(nameof(PasswordsMatch));
    }

    partial void OnOutputModeChanged(CompressOutputMode value)
    {
        // Notify all dependent properties (including RadioButton bindings)
        OnPropertyChanged(nameof(IsOutputPathReadOnly));
        OnPropertyChanged(nameof(IsBrowseButtonVisible));
        OnPropertyChanged(nameof(IsFileNameSectionVisible));
        OnPropertyChanged(nameof(OutputPathLabel));
        OnPropertyChanged(nameof(IsManualMode));
        OnPropertyChanged(nameof(IsSeparateMode));
        OnPropertyChanged(nameof(IsCombinedMode));
        RefreshOutputPathState();
        UpdateCanCompress();
        if (AutoGenerateRules)
            RefreshAutoRules();
    }

    partial void OnDefaultFormatChanged(string value)
    {
        if (OutputMode == CompressOutputMode.Combined)
            RefreshCombinedPath();

        // tar.gz 不支持加密，切过去时自动取消加密
        if (value == "tar.gz")
            Encrypt = false;

        OnPropertyChanged(nameof(IsZipFormat));
        OnPropertyChanged(nameof(IsSevenZipFormat));
        if (AutoGenerateRules)
            RefreshAutoRules();
        OnPropertyChanged(nameof(IsFormatEncryptionSupported));
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
        OnPropertyChanged(nameof(SaveCheckLabel));
    }

    /// <summary>
    /// 密码库状态文本：未选中时显示"未选定密码"，选中后显示"已选定: {description}"。
    /// </summary>
    public string PasswordLibraryStatusText =>
        SelectedPasswordEntry != null
            ? string.Format(LocalizationManager.T("Compress_Pwd_Selected"), SelectedPasswordEntry.Description)
            : LocalizationManager.T("Compress_Pwd_NoEntry");

    /// <summary>
    /// 保存复选框标签：密码库模式显示"更新匹配规则"，新密码模式显示"保存到密码库"。
    /// 对标 WPF CompressSettingsWindow.Password.cs UpdatePasswordSourceUI。
    /// </summary>
    public string SaveCheckLabel =>
        IsPasswordLibraryMode
            ? LocalizationManager.T("Compress_Pwd_UpdateRules")
            : LocalizationManager.T("Compress_Pwd_SaveToLibrary");

    partial void OnSelectedPasswordEntryChanged(Core.PasswordEntry? value)
    {
        if (value != null)
        {
            // 库模式下密码来自 SelectedPasswordEntry.Password，不写入 Password 属性
            // （对标 WPF: PasswordBox.Password = "" 且 GetActivePassword 返回 _selectedLibraryEntry?.Password）
            PasswordDescription = value.Description;
            // RulesText 不由条目规则覆盖: WPF 在选中条目时不写 PwdRulesBox.Text，
            // 规则始终来自自动规则（AutoGenerateRules 为 true 时由 RefreshAutoRules 生成）
            // 或用户手动输入。选中条目后若 AutoGenerateRules 为 true 则重新生成。
            if (AutoGenerateRules)
                RefreshAutoRules();
        }
        OnPropertyChanged(nameof(PasswordLibraryStatusText));
    }

    partial void OnPasswordChanged(string? value)
    {
        OnPropertyChanged(nameof(PasswordStrength));
        OnPropertyChanged(nameof(PasswordStrengthValue));
        OnPropertyChanged(nameof(PasswordStrengthIndicator));
        OnPropertyChanged(nameof(PasswordsMatch));

        // 用户手动输入密码时，清除密码库选中并自动切换到新密码模式
        // 对标 WPF OnPasswordContentChanged
        if (!string.IsNullOrEmpty(value) && IsPasswordLibraryMode)
        {
            SelectedPasswordEntry = null;
            IsPasswordLibraryMode = false;
        }
    }

    partial void OnAutoGenerateRulesChanged(bool value)
    {
        if (value)
            RefreshAutoRules();
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

    partial void OnSelectedSplitSizeOptionChanged(CompressionOptionData.ComboOption? value)
    {
        OnPropertyChanged(nameof(IsCustomSplitSizeVisible));
        OnPropertyChanged(nameof(SplitSize));
    }

    partial void OnCustomSplitSizeTextChanged(string value)
    {
        OnPropertyChanged(nameof(SplitSize));
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
    /// Refresh auto-rules from output mode + source file paths.
    /// Matches WPF CompressSettingsWindow.Password.cs RefreshAutoRules logic.
    /// Generates rules that match expected output archive file names (e.g. "document*.zip"),
    /// not source file extensions.
    /// </summary>
    public void RefreshAutoRules()
    {
        if (!AutoGenerateRules) return;

        var ext = DefaultFormat == "tar.gz" ? ".tar.gz" : "." + DefaultFormat;

        switch (OutputMode)
        {
            case CompressOutputMode.Manual:
                if (!string.IsNullOrEmpty(OutputPath))
                {
                    var manualName = Path.GetFileNameWithoutExtension(OutputPath);
                    if (!string.IsNullOrEmpty(manualName))
                        RulesText = $"{manualName}*{ext}";
                }
                break;

            case CompressOutputMode.Separate:
                var rules = new List<string>();
                foreach (var src in SelectedPaths)
                {
                    string baseName;
                    if (File.Exists(src))
                        baseName = Path.GetFileNameWithoutExtension(src);
                    else if (Directory.Exists(src))
                        baseName = ArchivePath.GetFileName(src);
                    else
                        continue;
                    rules.Add($"{baseName}*{ext}");
                }
                RulesText = string.Join("\r\n", rules);
                break;

            case CompressOutputMode.Combined:
                var commonParent = App.FindCommonParent(SelectedPaths.ToList());
                if (commonParent != null && !App.IsDriveRoot(commonParent))
                {
                    var archiveName = ArchivePath.GetFileName(commonParent);
                    RulesText = $"{archiveName}*{ext}";
                }
                break;
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

    /// <summary>
    /// 获取当前激活的密码（无论库模式还是新密码模式）。
    /// 库模式且选中条目时返回条目密码，否则返回 Password 属性值。
    /// </summary>
    public string? GetActivePassword()
    {
        if (!Encrypt) return null;
        if (IsPasswordLibraryMode && SelectedPasswordEntry != null)
            return SelectedPasswordEntry.Password;
        return Password;
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

    private bool CanExecuteStartCompress() => SelectedPaths.Count > 0;

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
        // Delegate to RefreshAutoRules for output-mode-based rule generation.
        if (AutoGenerateRules)
            RefreshAutoRules();
    }

    // ── Output mode helpers ──

    /// <summary>
    /// 根据当前模式刷新输出路径的显示内容（路径 + 可编辑状态）。
    /// </summary>
    private void RefreshOutputPathState()
    {
        switch (OutputMode)
        {
            case CompressOutputMode.Manual:
                // 回到 Manual：恢复缓存路径；无缓存则清空（避免残留其他模式的说明文本）
                OutputPath = _cachedManualPath;
                _cachedManualPath = null;
                break;

            case CompressOutputMode.Separate:
                // 首次离开 Manual 时缓存路径；之后不再覆盖（避免转两次模式后丢失原始路径）
                _cachedManualPath ??= OutputPath;
                OutputPath = LocalizationManager.T("Compress_SeparateSummary", SelectedPaths.Count);
                break;

            case CompressOutputMode.Combined:
                // 首次离开 Manual 时缓存路径；之后不再覆盖
                _cachedManualPath ??= OutputPath;
                RefreshCombinedPath();
                break;
        }
    }

    /// <summary>
    /// 计算 Combined 模式的输出路径：公共父目录下的合并压缩包。
    /// </summary>
    private void RefreshCombinedPath()
    {
        if (SelectedPaths.Count == 0)
        {
            OutputPath = "";
            return;
        }

        var commonParent = App.FindCommonParent(SelectedPaths.ToList());
        if (commonParent != null && !App.IsDriveRoot(commonParent))
        {
            var archiveName = ArchivePath.GetFileName(commonParent);
            var ext = GetFormatExtension();
            OutputPath = System.IO.Path.Combine(commonParent, archiveName + ext);
        }
        else
        {
            // 跨驱动器或根目录 — 回退到手动模式
            OutputMode = CompressOutputMode.Manual;
            // 通知用户
            _ = AppMessageBox.Show(
                LocalizationManager.T("Compress_CombinedUnavailable"),
                LocalizationManager.T("Compress_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 从当前选择的格式推断文件扩展名。
    /// </summary>
    private string GetFormatExtension()
    {
        return DefaultFormat switch
        {
            "tar.gz" => ".tar.gz",
            _ => "." + DefaultFormat,
        };
    }

    /// <summary>
    /// 根据源路径推断默认文件名。
    /// </summary>
    private string GetDefaultFileName()
    {
        if (SelectedPaths.Count == 0) return "archive";
        if (SelectedPaths.Count == 1 && File.Exists(SelectedPaths[0]))
            return Path.GetFileNameWithoutExtension(SelectedPaths[0]);
        if (SelectedPaths.Count == 1 && Directory.Exists(SelectedPaths[0]))
            return ArchivePath.GetFileName(SelectedPaths[0]);
        return $"archive_{DateTime.Now:yyyyMMddHHmmss}";
    }

    /// <summary>
    /// 更新"开始压缩"按钮的启用状态。由模式切换和源文件变化时调用。
    /// </summary>
    private void UpdateCanCompress()
    {
        // 通知 CloseAction 调用方重新评估按钮状态
        // 实际按钮启用由 Command 的 CanExecute 决定，
        // 这里触发 CanExecuteChanged 刷新
        StartCompressCommand.NotifyCanExecuteChanged();
    }
}
