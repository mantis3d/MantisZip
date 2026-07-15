using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using System.Diagnostics;
using System.IO;

namespace MantisZip.UI.Avalonia.ViewModels;

public partial class SettingsWindowViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    [ObservableProperty]
    private string _defaultFormat;

    [ObservableProperty]
    private int _defaultLevel;

    [ObservableProperty]
    private bool _enableImagePreview;

    [ObservableProperty]
    private bool _enableTextPreview;

    [ObservableProperty]
    private long _maxTextPreviewBytes;

    [ObservableProperty]
    private int _textPreviewFontSize;

    [ObservableProperty]
    private string _textPreviewFontFamily = "";

    [ObservableProperty]
    private int _fontPreviewFontSize = 12;

    [ObservableProperty]
    private string _fontPreviewSampleText = "The quick brown fox jumps over the lazy dog.\n0123456789\n天地玄黄 宇宙洪荒 日月盈昃 辰宿列张";

    [ObservableProperty]
    private int _maxTablePreviewRows = 100;

    [ObservableProperty]
    private int _maxTablePreviewCols = 100;

    [ObservableProperty]
    private long _maxPreviewFileSize = 15 * 1024 * 1024;

    [ObservableProperty]
    private int _previewPosition = 4;

    [ObservableProperty]
    private string _infoPanelOrientation = "Vertical";

    [ObservableProperty]
    private bool _showPreviewPanel = true;

    [ObservableProperty]
    private bool _useColorEmoji = true;

    [ObservableProperty]
    private bool _enableFormatDetection = true;

    [ObservableProperty]
    private int _previewHeadSize = 4096;

    [ObservableProperty]
    private bool _enableDebugLogging;

    [ObservableProperty]
    private bool _closeAfterCompress;

    [ObservableProperty]
    private bool _keepOriginalExtension;

    // ── Compression advanced ──
    [ObservableProperty]
    private string _sevenZipCompressionMethod = "LZMA2";

    [ObservableProperty]
    private bool _sevenZipSolid = true;

    [ObservableProperty]
    private string _sevenZipSolidBlockSize = "";

    [ObservableProperty]
    private int _sevenZipDictionarySize = 0;

    [ObservableProperty]
    private int _sevenZipNumFastBytes = 0;

    [ObservableProperty]
    private string _sevenZipMatchFinder = "";

    [ObservableProperty]
    private string _zipCompressionMethod = "deflate";

    [ObservableProperty]
    private string _zipEncryptionMethod = "aes256";

    [ObservableProperty]
    private string _zipEncoding = "utf-8";

    [ObservableProperty]
    private bool _sevenZipEncryptHeaders = true;

    [ObservableProperty]
    private string _logPrivacyMode = "extension";

    // ── Extract ──
    [ObservableProperty]
    private string _extractDestination;

    [ObservableProperty]
    private string _fileConflictAction;

    [ObservableProperty]
    private bool _openFolderAfterExtract;

    [ObservableProperty]
    private bool _enableDragExtract;

    [ObservableProperty]
    private bool _extractPreserveFullPath;

    // ── ContextMenu ──
    [ObservableProperty]
    private bool _enableOpenMenu;

    [ObservableProperty]
    private bool _enableCompressMenu;

    [ObservableProperty]
    private bool _enableCompressSeparate;

    [ObservableProperty]
    private bool _enableCompressCombined;

    [ObservableProperty]
    private bool _enableExtractHereMenu;

    [ObservableProperty]
    private bool _enableSmartExtractMenu;

    [ObservableProperty]
    private bool _enableExtractToNamedMenu;

    [ObservableProperty]
    private bool _enableExtractToMenu;

    [ObservableProperty]
    private bool _showMenuIcons;

    [ObservableProperty]
    private bool _enableDynamicMenu;

    // ── Advanced ──
    [ObservableProperty]
    private string _sevenZipPath;

    [ObservableProperty]
    private bool _preserveDirectoryRoot;

    [ObservableProperty]
    private bool _cleanTempOnStartup;

    // ── Language ──
    [ObservableProperty]
    private string _selectedLanguage = "zh";

    // ── Appearance ──
    [ObservableProperty]
    private string _theme = "Light";

    [ObservableProperty]
    private int _maxRecentFiles = 10;

    [ObservableProperty]
    private string _appFontFamily = "";

    // ── Password ──
    [ObservableProperty]
    private bool _showPasswordMatchNotification;

    [ObservableProperty]
    private bool _passwordRevealByDefault;

    // ── File Assoc ──
    [ObservableProperty]
    private bool _assocZip = true;

    [ObservableProperty]
    private bool _assoc7z = true;

    [ObservableProperty]
    private bool _assocRar = true;

    [ObservableProperty]
    private bool _assocTar = true;

    [ObservableProperty]
    private bool _assocTarGz = true;

    [ObservableProperty]
    private bool _assocGz = true;

    [ObservableProperty]
    private bool _assocIso;

    // ── Combo ItemSource properties ──
    public System.Collections.ObjectModel.ObservableCollection<Option> DefaultFormatOptions { get; } = new();
    [ObservableProperty] private Option? _selectedDefaultFormatOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> PreviewPositionOptions { get; } = new();
    [ObservableProperty] private Option? _selectedPreviewPositionOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> InfoPanelOrientationOptions { get; } = new();
    [ObservableProperty] private Option? _selectedInfoPanelOrientationOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> ThemeOptions { get; } = new();
    [ObservableProperty] private Option? _selectedThemeOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> SelectedLanguageOptions { get; } = new();
    [ObservableProperty] private Option? _selectedSelectedLanguageOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> LogPrivacyModeOptions { get; } = new();
    [ObservableProperty] private Option? _selectedLogPrivacyModeOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> ExtractDestinationOptions { get; } = new();
    [ObservableProperty] private Option? _selectedExtractDestinationOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> FileConflictActionOptions { get; } = new();
    [ObservableProperty] private Option? _selectedFileConflictActionOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> TextFontFamilyOptions { get; } = new();
    [ObservableProperty] private Option? _selectedTextFontFamilyOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> AppFontFamilyOptions { get; } = new();
    [ObservableProperty] private Option? _selectedAppFontFamilyOption;

    // ── Compression advanced combos ──
    public System.Collections.ObjectModel.ObservableCollection<Option> SevenZipCompressionMethodOptions { get; } = new();
    [ObservableProperty] private Option? _selectedSevenZipCompressionMethodOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> SevenZipSolidBlockSizeOptions { get; } = new();
    [ObservableProperty] private Option? _selectedSevenZipSolidBlockSizeOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> SevenZipDictionarySizeOptions { get; } = new();
    [ObservableProperty] private Option? _selectedSevenZipDictionarySizeOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> SevenZipNumFastBytesOptions { get; } = new();
    [ObservableProperty] private Option? _selectedSevenZipNumFastBytesOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> SevenZipMatchFinderOptions { get; } = new();
    [ObservableProperty] private Option? _selectedSevenZipMatchFinderOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> ZipCompressionMethodOptions { get; } = new();
    [ObservableProperty] private Option? _selectedZipCompressionMethodOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> ZipEncryptionMethodOptions { get; } = new();
    [ObservableProperty] private Option? _selectedZipEncryptionMethodOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> ZipEncodingOptions { get; } = new();
    [ObservableProperty] private Option? _selectedZipEncodingOption;

    // ── Localized strings ──

    public string WindowTitle => LocalizationManager.T("Settings_Title");
    public string TabPreviewHeader => LocalizationManager.T("Settings_TabPreview");
    public string TabCompressHeader => LocalizationManager.T("Settings_TabCompress");
    public string TabExtractHeader => LocalizationManager.T("Settings_TabExtract");
    public string TabContextMenuHeader => LocalizationManager.T("Settings_TabContextMenu");
    public string TabAdvancedHeader => LocalizationManager.T("Settings_TabAdvanced");
    public string TabDebugHeader => LocalizationManager.T("Settings_TabDebug");

    // Preview strings
    public string EnableImagePreviewText => LocalizationManager.T("Settings_EnableImagePreview");
    public string EnableTextPreviewText => LocalizationManager.T("Settings_EnableTextPreview");
    public string TextPreviewFontSizeText => LocalizationManager.T("Settings_TextPreviewFontSize");
    public string MaxPreviewBytesText => LocalizationManager.T("Settings_MaxPreviewBytes");

    // Preview sub-tab headers
    public string PreviewTabTextHeader => LocalizationManager.T("Settings_Preview_Tab_Text");
    public string PreviewTabFontHeader => LocalizationManager.T("Settings_Preview_Tab_Font");
    public string PreviewTabTableHeader => LocalizationManager.T("Settings_Preview_Tab_Table");
    public string PreviewTabPositionHeader => LocalizationManager.T("Settings_Preview_Tab_Position");

    // Preview — Text sub-tab
    public string PreviewTextFontFamilyText => LocalizationManager.T("Settings_Preview_Text_FontFamily");
    public string PreviewTextColorEmojiText => LocalizationManager.T("Settings_Preview_Text_ColorEmoji");
    public string PreviewTextMaxSizeText => LocalizationManager.T("Settings_Preview_Text_MaxSize");

    // Preview — Font sub-tab
    public string PreviewFontSampleText => LocalizationManager.T("Settings_Preview_Font_Sample");
    public string PreviewFontPreviewSizeText => LocalizationManager.T("Settings_Preview_Font_PreviewSize");

    // Preview — Table sub-tab
    public string PreviewTableMaxRowsText => LocalizationManager.T("Settings_Preview_Table_MaxRows");
    public string PreviewTableMaxColsText => LocalizationManager.T("Settings_Preview_Table_MaxCols");

    // Preview — Layout sub-tab
    public string PreviewPositionText => LocalizationManager.T("Settings_Preview_Position");
    public string PreviewPositionBottomText => LocalizationManager.T("Settings_Preview_Position_Bottom");
    public string PreviewPositionBelowTreeText => LocalizationManager.T("Settings_Preview_Position_BelowTree");
    public string PreviewPositionBelowListText => LocalizationManager.T("Settings_Preview_Position_BelowList");
    public string PreviewPositionRightText => LocalizationManager.T("Settings_Preview_Position_Right");
    public string PreviewInfoOrientationText => LocalizationManager.T("Settings_Preview_InfoPanelOrientation");
    public string PreviewInfoOrientationHorizontalText => LocalizationManager.T("Settings_Preview_InfoPanel_Horizontal");
    public string PreviewInfoOrientationVerticalText => LocalizationManager.T("Settings_Preview_InfoPanel_Vertical");
    public string PreviewShowPanelText => LocalizationManager.T("Settings_Preview_ShowPanel");
    public string PreviewMaxFileSizeText => LocalizationManager.T("Settings_Preview_MaxFileSize");

    // Preview — Magic Detection
    public string EnableFormatDetectionText => "启用格式检测（魔数识别）";
    public string PreviewHeadSizeText => "检测头部字节数";

    // Preview — computed properties (slider-friendly MB)
    public double MaxTextPreviewMB
    {
        get => MaxTextPreviewBytes / (1024.0 * 1024.0);
        set => MaxTextPreviewBytes = (long)(value * 1024 * 1024);
    }

    public string MaxTextPreviewMBText => $"{(int)MaxTextPreviewMB} MB";

    public double MaxPreviewFileSizeMB
    {
        get => MaxPreviewFileSize / (1024.0 * 1024.0);
        set => MaxPreviewFileSize = (long)(value * 1024 * 1024);
    }

    public string MaxPreviewFileSizeMBText => $"{(int)MaxPreviewFileSizeMB} MB";

    // PreviewHeadSize slider — stored as bytes, slider-friendly in KB (1–64)
    public double PreviewHeadSizeKB
    {
        get => PreviewHeadSize / 1024.0;
        set => PreviewHeadSize = (int)(value * 1024);
    }

    public string PreviewHeadSizeKBText => $"{PreviewHeadSize / 1024} KB";

    // Compress strings
    public string DefaultFormatText => LocalizationManager.T("Settings_DefaultFormat");
    public string CompressionLevelText => LocalizationManager.T("Settings_CompressionLevel");
    public string CloseAfterCompressText => LocalizationManager.T("Settings_Compress_CloseAfterDone");
    public string KeepOriginalExtensionText => LocalizationManager.T("Settings_Compress_KeepExt");
    public string PreserveDirectoryRootText => LocalizationManager.T("Settings_Compress_PreserveRoot");

    // Extract strings
    public string ExtractDefaultDestText => LocalizationManager.T("Settings_Extract_DefaultDest");
    public string ExtractConflictActionText => LocalizationManager.T("Settings_Extract_ConflictAction");
    public string ExtractOpenFolderAfterText => LocalizationManager.T("Settings_Extract_OpenFolderAfter");
    public string ExtractEnableDragText => LocalizationManager.T("Settings_Extract_EnableDragExtract");
    public string ExtractPreserveFullPathText => LocalizationManager.T("Settings_Extract_PreserveFullPath");

    // Extract option display texts
    public string ExtractDestAskText => LocalizationManager.T("Settings_Extract_Dest_Ask");
    public string ExtractDestSameDirText => LocalizationManager.T("Settings_Extract_Dest_SameDir");
    public string ExtractDestDesktopText => LocalizationManager.T("Settings_Extract_Dest_Desktop");

    public string ConflictAskText => LocalizationManager.T("Settings_Extract_Conflict_Ask");
    public string ConflictOverwriteText => LocalizationManager.T("Settings_Extract_Conflict_Overwrite");
    public string ConflictOverwriteOlderText => LocalizationManager.T("Settings_Extract_Conflict_OverwriteOlder");
    public string ConflictOverwriteSmallerText => LocalizationManager.T("Settings_Extract_Conflict_OverwriteSmaller");
    public string ConflictRenameText => LocalizationManager.T("Settings_Extract_Conflict_Rename");
    public string ConflictSkipText => LocalizationManager.T("Settings_Extract_Conflict_Skip");

    // ContextMenu strings
    public string ContextMenuGroupHeader => LocalizationManager.T("Settings_ContextMenu_GroupHeader");
    public string ContextMenuGroupBrowse => LocalizationManager.T("Settings_ContextMenu_GroupBrowse");
    public string ContextMenuGroupCompress => LocalizationManager.T("Settings_ContextMenu_GroupCompress");
    public string ContextMenuGroupExtract => LocalizationManager.T("Settings_ContextMenu_GroupExtract");
    public string ContextMenuGroupDisplay => LocalizationManager.T("Settings_ContextMenu_GroupDisplay");
    public string ContextMenuEnableOpen => LocalizationManager.T("Settings_ContextMenu_EnableOpen");
    public string ContextMenuEnableCompress => LocalizationManager.T("Settings_ContextMenu_EnableCompress");
    public string ContextMenuEnableCompressSeparate => LocalizationManager.T("Settings_ContextMenu_EnableCompressSeparate");
    public string ContextMenuEnableCompressCombined => LocalizationManager.T("Settings_ContextMenu_EnableCompressCombined");
    public string ContextMenuEnableExtractHere => LocalizationManager.T("Settings_ContextMenu_EnableExtractHere");
    public string ContextMenuEnableSmartExtract => LocalizationManager.T("Settings_ContextMenu_EnableSmartExtract");
    public string ContextMenuEnableExtractToNamed => LocalizationManager.T("Settings_ContextMenu_EnableExtractToNamed");
    public string ContextMenuEnableExtractTo => LocalizationManager.T("Settings_ContextMenu_EnableExtractTo");
    public string ContextMenuShowMenuIcons => LocalizationManager.T("Settings_ContextMenu_ShowMenuIcons");
    public string ContextMenuEnableDynamicMenu => LocalizationManager.T("Settings_ContextMenu_EnableDynamicMenu");
    public string ContextMenuInstall => LocalizationManager.T("Settings_ContextMenu_Install");
    public string ContextMenuUninstall => LocalizationManager.T("Settings_ContextMenu_Uninstall");

    // Advanced strings
    public string AdvancedSevenZipPathText => LocalizationManager.T("Settings_Advanced_SevenZipPath");
    public string AdvancedBrowseText => LocalizationManager.T("Settings_Advanced_Browse");
    public string AdvancedPreserveRootText => LocalizationManager.T("Settings_Advanced_PreserveRoot");
    public string AdvancedTempGroupText => LocalizationManager.T("Settings_Advanced_TempGroup");
    public string AdvancedCleanPreviewTempText => LocalizationManager.T("Settings_Advanced_CleanPreviewTemp");
    public string AdvancedCleanAllTempText => LocalizationManager.T("Settings_Advanced_CleanAllTemp");
    public string AdvancedCleanOnStartupText => LocalizationManager.T("Settings_Advanced_CleanOnStartup");

    public string DebugText => LocalizationManager.T("Settings_EnableDebugLog");
    public string LogPrivacyModeText => LocalizationManager.T("Settings_Debug_LogPrivacyMode");
    public string LogPrivacyModeOffText => LocalizationManager.T("Settings_Debug_LogPrivacyMode_Off");
    public string LogPrivacyModeFilenameText => LocalizationManager.T("Settings_Debug_LogPrivacyMode_Filename");
    public string LogPrivacyModeExtensionText => LocalizationManager.T("Settings_Debug_LogPrivacyMode_Extension");
    public string LogPrivacyModeFullText => LocalizationManager.T("Settings_Debug_LogPrivacyMode_Full");
    public string LogPrivacyHelpText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp");
    public string LogPathText => LocalizationManager.T("Settings_Debug_LogPath");
    public string LogFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MantisZip", "debug.log");

    // ── New tab headers ──
    public string TabLanguageHeader => LocalizationManager.T("Settings_Tab_Language");
    public string TabAppearanceHeader => LocalizationManager.T("Settings_Tab_Appearance");
    public string TabPasswordHeader => LocalizationManager.T("Settings_Tab_Password");
    public string TabFileAssocHeader => LocalizationManager.T("Settings_Tab_FileAssoc");

    // ── Language strings ──
    public string LanguageText => LocalizationManager.T("Settings_Language");
    public string LanguageTranslatorText => LocalizationManager.T("Settings_Language_Translator");

    // ── Appearance strings ──
    public string AppearanceThemeText => LocalizationManager.T("Settings_Appearance_Theme");
    public string AppearanceThemeLightText => LocalizationManager.T("Settings_Appearance_Theme_Light");
    public string AppearanceThemeDarkText => LocalizationManager.T("Settings_Appearance_Theme_Dark");
    public string AppearanceMaxRecentFilesText => LocalizationManager.T("Settings_Appearance_MaxRecentFiles");
    public string AppearanceAppFontFamilyText => LocalizationManager.T("Settings_Appearance_AppFontFamily");

    // ── Password strings ──
    public string PwdShowNotificationText => LocalizationManager.T("Settings_Pwd_ShowNotification");
    public string PwdRevealDefaultText => LocalizationManager.T("Settings_Pwd_RevealDefault");

    // ── File Assoc strings ──
    public string FileAssocDescText => LocalizationManager.T("Settings_Assoc_Desc");
    public string FileAssocSelectAllText => LocalizationManager.T("Settings_Assoc_SelectAll");
    public string FileAssocDeselectAllText => LocalizationManager.T("Settings_Assoc_DeselectAll");

    public string SaveText => LocalizationManager.T("Settings_Save");
    public string CancelText => LocalizationManager.T("Settings_Cancel");

    public SettingsWindowViewModel()
    {
        _settings = AppSettings.Load();

        // Compress
        _defaultFormat = _settings.DefaultFormat;
        _defaultLevel = _settings.DefaultLevel;

        // Preview
        _enableImagePreview = _settings.EnableImagePreview;
        _enableTextPreview = _settings.EnableTextPreview;
        _maxTextPreviewBytes = _settings.MaxTextPreviewBytes;
        _textPreviewFontSize = _settings.TextPreviewFontSize;
        _textPreviewFontFamily = _settings.TextPreviewFontFamily;
        _fontPreviewFontSize = _settings.FontPreviewFontSize;
        _fontPreviewSampleText = _settings.FontPreviewSampleText;
        _maxTablePreviewRows = _settings.MaxTablePreviewRows;
        _maxTablePreviewCols = _settings.MaxTablePreviewCols;
        _maxPreviewFileSize = _settings.MaxPreviewFileSize;
        _previewPosition = _settings.PreviewPosition;
        _infoPanelOrientation = _settings.InfoPanelOrientation;
        _showPreviewPanel = _settings.ShowPreviewPanel;
        _useColorEmoji = _settings.UseColorEmoji;
        _enableFormatDetection = _settings.EnableFormatDetection;
        _previewHeadSize = _settings.PreviewHeadSize;

        // Debug
        _enableDebugLogging = _settings.EnableDebugLogging;
        _logPrivacyMode = _settings.LogPrivacyMode;

        // Compress (additional)
        _closeAfterCompress = _settings.CloseAfterCompress;
        _keepOriginalExtension = _settings.KeepOriginalExtension;

        // Compression advanced
        _sevenZipCompressionMethod = _settings.SevenZipCompressionMethod;
        _sevenZipSolid = _settings.SevenZipSolid;
        _sevenZipSolidBlockSize = _settings.SevenZipSolidBlockSize;
        _sevenZipDictionarySize = _settings.SevenZipDictionarySize;
        _sevenZipNumFastBytes = _settings.SevenZipNumFastBytes;
        _sevenZipMatchFinder = _settings.SevenZipMatchFinder;
        _zipCompressionMethod = _settings.ZipCompressionMethod;
        _zipEncryptionMethod = _settings.ZipEncryptionMethod;
        _zipEncoding = _settings.ZipEncoding;
        _sevenZipEncryptHeaders = _settings.SevenZipEncryptHeaders;

        // Extract
        _extractDestination = _settings.ExtractDestination;
        _fileConflictAction = _settings.FileConflictAction;
        _openFolderAfterExtract = _settings.OpenFolderAfterExtract;
        _enableDragExtract = _settings.EnableDragExtract;
        _extractPreserveFullPath = _settings.ExtractPreserveFullPath;

        // ContextMenu
        _enableOpenMenu = _settings.EnableOpenMenu;
        _enableCompressMenu = _settings.EnableCompressMenu;
        _enableCompressSeparate = _settings.EnableCompressSeparate;
        _enableCompressCombined = _settings.EnableCompressCombined;
        _enableExtractHereMenu = _settings.EnableExtractHereMenu;
        _enableSmartExtractMenu = _settings.EnableSmartExtractMenu;
        _enableExtractToNamedMenu = _settings.EnableExtractToNamedMenu;
        _enableExtractToMenu = _settings.EnableExtractToMenu;
        _showMenuIcons = _settings.ShowMenuIcons;
        _enableDynamicMenu = _settings.EnableDynamicMenu;

        // Advanced
        _sevenZipPath = _settings.SevenZipPath;
        _preserveDirectoryRoot = _settings.PreserveDirectoryRoot;
        _cleanTempOnStartup = _settings.CleanTempOnStartup;

        // Language
        _selectedLanguage = _settings.Language;

        // Appearance
        _theme = _settings.Theme;
        _maxRecentFiles = _settings.MaxRecentFiles;
        _appFontFamily = _settings.AppFontFamily;

        // Password
        _showPasswordMatchNotification = _settings.ShowPasswordMatchNotification;
        _passwordRevealByDefault = _settings.PasswordRevealByDefault;

        // File Assoc
        _assocZip = _settings.AssocZip;
        _assoc7z = _settings.Assoc7z;
        _assocRar = _settings.AssocRar;
        _assocTar = _settings.AssocTar;
        _assocTarGz = _settings.AssocTarGz;
        _assocGz = _settings.AssocGz;
        _assocIso = _settings.AssocIso;

        PopulateComboOptions();
        SetSelectedOptions();

        LocalizationManager.CultureChanged += OnCultureChanged;
    }

    private void PopulateComboOptions()
    {
        DefaultFormatOptions.Clear();
        DefaultFormatOptions.Add(new Option("zip", "zip"));
        DefaultFormatOptions.Add(new Option("7z", "7z"));
        DefaultFormatOptions.Add(new Option("tar.gz", "tar.gz"));

        PreviewPositionOptions.Clear();
        PreviewPositionOptions.Add(new Option(PreviewPositionBottomText, "1"));
        PreviewPositionOptions.Add(new Option(PreviewPositionBelowTreeText, "2"));
        PreviewPositionOptions.Add(new Option(PreviewPositionBelowListText, "3"));
        PreviewPositionOptions.Add(new Option(PreviewPositionRightText, "4"));

        InfoPanelOrientationOptions.Clear();
        InfoPanelOrientationOptions.Add(new Option(PreviewInfoOrientationHorizontalText, "Horizontal"));
        InfoPanelOrientationOptions.Add(new Option(PreviewInfoOrientationVerticalText, "Vertical"));

        ThemeOptions.Clear();
        ThemeOptions.Add(new Option(AppearanceThemeLightText, "Light"));
        ThemeOptions.Add(new Option(AppearanceThemeDarkText, "Dark"));

        SelectedLanguageOptions.Clear();
        SelectedLanguageOptions.Add(new Option("中文", "zh"));
        SelectedLanguageOptions.Add(new Option("English", "en"));

        LogPrivacyModeOptions.Clear();
        LogPrivacyModeOptions.Add(new Option(LogPrivacyModeOffText, "off"));
        LogPrivacyModeOptions.Add(new Option(LogPrivacyModeFilenameText, "filename"));
        LogPrivacyModeOptions.Add(new Option(LogPrivacyModeExtensionText, "extension"));
        LogPrivacyModeOptions.Add(new Option(LogPrivacyModeFullText, "full"));

        ExtractDestinationOptions.Clear();
        ExtractDestinationOptions.Add(new Option(ExtractDestAskText, "ask"));
        ExtractDestinationOptions.Add(new Option(ExtractDestSameDirText, "same-dir"));
        ExtractDestinationOptions.Add(new Option(ExtractDestDesktopText, "desktop"));

        FileConflictActionOptions.Clear();
        FileConflictActionOptions.Add(new Option(ConflictAskText, "ask"));
        FileConflictActionOptions.Add(new Option(ConflictOverwriteText, "overwrite"));
        FileConflictActionOptions.Add(new Option(ConflictOverwriteOlderText, "overwrite-if-older"));
        FileConflictActionOptions.Add(new Option(ConflictOverwriteSmallerText, "overwrite-if-smaller"));
        FileConflictActionOptions.Add(new Option(ConflictRenameText, "rename"));
        FileConflictActionOptions.Add(new Option(ConflictSkipText, "skip"));

        // 字体列表（文本预览 + 全局界面共用同一份系统字体枚举）
        PopulateFontOptions();

        // Compression advanced combos
        SevenZipCompressionMethodOptions.Clear();
        SevenZipCompressionMethodOptions.Add(new Option("LZMA", "LZMA"));
        SevenZipCompressionMethodOptions.Add(new Option("LZMA2", "LZMA2"));
        SevenZipCompressionMethodOptions.Add(new Option("PPMd", "PPMd"));
        SevenZipCompressionMethodOptions.Add(new Option("BZip2", "BZip2"));
        SevenZipCompressionMethodOptions.Add(new Option("Deflate", "Deflate"));

        SevenZipSolidBlockSizeOptions.Clear();
        SevenZipSolidBlockSizeOptions.Add(new Option("默认", ""));
        SevenZipSolidBlockSizeOptions.Add(new Option("64 MB", "64m"));
        SevenZipSolidBlockSizeOptions.Add(new Option("256 MB", "256m"));
        SevenZipSolidBlockSizeOptions.Add(new Option("512 MB", "512m"));
        SevenZipSolidBlockSizeOptions.Add(new Option("1 GB", "1g"));

        SevenZipDictionarySizeOptions.Clear();
        SevenZipDictionarySizeOptions.Add(new Option("默认", "0"));
        SevenZipDictionarySizeOptions.Add(new Option("16 MB", "16777216"));
        SevenZipDictionarySizeOptions.Add(new Option("32 MB", "33554432"));
        SevenZipDictionarySizeOptions.Add(new Option("128 MB", "134217728"));
        SevenZipDictionarySizeOptions.Add(new Option("256 MB", "268435456"));

        SevenZipNumFastBytesOptions.Clear();
        SevenZipNumFastBytesOptions.Add(new Option("默认", "0"));
        SevenZipNumFastBytesOptions.Add(new Option("32", "32"));
        SevenZipNumFastBytesOptions.Add(new Option("64", "64"));
        SevenZipNumFastBytesOptions.Add(new Option("128", "128"));
        SevenZipNumFastBytesOptions.Add(new Option("255", "255"));

        SevenZipMatchFinderOptions.Clear();
        SevenZipMatchFinderOptions.Add(new Option("默认", ""));
        SevenZipMatchFinderOptions.Add(new Option("BT2", "bt2"));
        SevenZipMatchFinderOptions.Add(new Option("BT3", "bt3"));
        SevenZipMatchFinderOptions.Add(new Option("BT4", "bt4"));

        ZipCompressionMethodOptions.Clear();
        ZipCompressionMethodOptions.Add(new Option("deflate", "deflate"));
        ZipCompressionMethodOptions.Add(new Option("deflate64", "deflate64"));
        ZipCompressionMethodOptions.Add(new Option("bzip2", "bzip2"));
        ZipCompressionMethodOptions.Add(new Option("lzma", "lzma"));
        ZipCompressionMethodOptions.Add(new Option("ppmd", "ppmd"));
        ZipCompressionMethodOptions.Add(new Option("store", "store"));

        ZipEncryptionMethodOptions.Clear();
        ZipEncryptionMethodOptions.Add(new Option("aes256", "aes256"));
        ZipEncryptionMethodOptions.Add(new Option("aes192", "aes192"));
        ZipEncryptionMethodOptions.Add(new Option("aes128", "aes128"));
        ZipEncryptionMethodOptions.Add(new Option("zipcrypto", "zipcrypto"));

        ZipEncodingOptions.Clear();
        ZipEncodingOptions.Add(new Option("UTF-8", "utf-8"));
        ZipEncodingOptions.Add(new Option("GBK", "gbk"));
        ZipEncodingOptions.Add(new Option("系统默认", "default"));
    }

    /// <summary>
    /// 用 SkiaSharp 枚举系统字体，填充文本预览和全局界面的字体系列 ComboBox。
    /// </summary>
    private void PopulateFontOptions()
    {
        var defaultName = LocalizationManager.T("Settings_Preview_FontDefault");

        TextFontFamilyOptions.Clear();
        TextFontFamilyOptions.Add(new Option(defaultName, ""));
        AppFontFamilyOptions.Clear();
        AppFontFamilyOptions.Add(new Option(defaultName, ""));

        try
        {
            var fontNames = SkiaSharp.SKFontManager.Default.FontFamilies
                .OrderBy(n => n)
                .ToList();
            foreach (var name in fontNames)
            {
                TextFontFamilyOptions.Add(new Option(name, name));
                AppFontFamilyOptions.Add(new Option(name, name));
            }
        }
        catch
        {
            // 获取字体列表失败时，至少保留"系统默认"项
        }
    }

    private void SetSelectedOptions()
    {
        SelectedDefaultFormatOption = DefaultFormatOptions.FirstOrDefault(o => o.Value == DefaultFormat);
        SelectedPreviewPositionOption = PreviewPositionOptions.FirstOrDefault(o => o.Value == PreviewPosition.ToString());
        SelectedInfoPanelOrientationOption = InfoPanelOrientationOptions.FirstOrDefault(o => o.Value == InfoPanelOrientation);
        SelectedThemeOption = ThemeOptions.FirstOrDefault(o => o.Value == Theme);
        SelectedSelectedLanguageOption = SelectedLanguageOptions.FirstOrDefault(o => o.Value == SelectedLanguage);
        SelectedLogPrivacyModeOption = LogPrivacyModeOptions.FirstOrDefault(o => o.Value == LogPrivacyMode);
        SelectedExtractDestinationOption = ExtractDestinationOptions.FirstOrDefault(o => o.Value == ExtractDestination);
        SelectedFileConflictActionOption = FileConflictActionOptions.FirstOrDefault(o => o.Value == FileConflictAction);

        SelectedTextFontFamilyOption = TextFontFamilyOptions.FirstOrDefault(o => o.Value == TextPreviewFontFamily)
                                       ?? TextFontFamilyOptions.FirstOrDefault();

        SelectedAppFontFamilyOption = AppFontFamilyOptions.FirstOrDefault(o => o.Value == AppFontFamily)
                                      ?? AppFontFamilyOptions.FirstOrDefault();

        // Compression advanced
        SelectedSevenZipCompressionMethodOption = SevenZipCompressionMethodOptions.FirstOrDefault(o => o.Value == SevenZipCompressionMethod);
        SelectedSevenZipSolidBlockSizeOption = SevenZipSolidBlockSizeOptions.FirstOrDefault(o => o.Value == SevenZipSolidBlockSize);
        SelectedSevenZipDictionarySizeOption = SevenZipDictionarySizeOptions.FirstOrDefault(o => o.Value == SevenZipDictionarySize.ToString());
        SelectedSevenZipNumFastBytesOption = SevenZipNumFastBytesOptions.FirstOrDefault(o => o.Value == SevenZipNumFastBytes.ToString());
        SelectedSevenZipMatchFinderOption = SevenZipMatchFinderOptions.FirstOrDefault(o => o.Value == SevenZipMatchFinder);
        SelectedZipCompressionMethodOption = ZipCompressionMethodOptions.FirstOrDefault(o => o.Value == ZipCompressionMethod);
        SelectedZipEncryptionMethodOption = ZipEncryptionMethodOptions.FirstOrDefault(o => o.Value == ZipEncryptionMethod);
        SelectedZipEncodingOption = ZipEncodingOptions.FirstOrDefault(o => o.Value == ZipEncoding);
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(TabPreviewHeader));
        OnPropertyChanged(nameof(TabCompressHeader));
        OnPropertyChanged(nameof(TabExtractHeader));
        OnPropertyChanged(nameof(TabContextMenuHeader));
        OnPropertyChanged(nameof(TabAdvancedHeader));
        OnPropertyChanged(nameof(TabDebugHeader));

        OnPropertyChanged(nameof(EnableImagePreviewText));
        OnPropertyChanged(nameof(EnableTextPreviewText));
        OnPropertyChanged(nameof(TextPreviewFontSizeText));
        OnPropertyChanged(nameof(MaxPreviewBytesText));

        OnPropertyChanged(nameof(PreviewTabTextHeader));
        OnPropertyChanged(nameof(PreviewTabFontHeader));
        OnPropertyChanged(nameof(PreviewTabTableHeader));
        OnPropertyChanged(nameof(PreviewTabPositionHeader));
        OnPropertyChanged(nameof(PreviewTextFontFamilyText));
        OnPropertyChanged(nameof(PreviewTextColorEmojiText));
        OnPropertyChanged(nameof(PreviewTextMaxSizeText));
        OnPropertyChanged(nameof(PreviewFontSampleText));
        OnPropertyChanged(nameof(PreviewFontPreviewSizeText));
        OnPropertyChanged(nameof(PreviewTableMaxRowsText));
        OnPropertyChanged(nameof(PreviewTableMaxColsText));
        OnPropertyChanged(nameof(PreviewPositionText));
        OnPropertyChanged(nameof(PreviewPositionBottomText));
        OnPropertyChanged(nameof(PreviewPositionBelowTreeText));
        OnPropertyChanged(nameof(PreviewPositionBelowListText));
        OnPropertyChanged(nameof(PreviewPositionRightText));
        OnPropertyChanged(nameof(PreviewInfoOrientationText));
        OnPropertyChanged(nameof(PreviewInfoOrientationHorizontalText));
        OnPropertyChanged(nameof(PreviewInfoOrientationVerticalText));
        OnPropertyChanged(nameof(PreviewShowPanelText));
        OnPropertyChanged(nameof(PreviewMaxFileSizeText));
        OnPropertyChanged(nameof(MaxTextPreviewMBText));
        OnPropertyChanged(nameof(MaxPreviewFileSizeMBText));

        OnPropertyChanged(nameof(EnableFormatDetectionText));
        OnPropertyChanged(nameof(PreviewHeadSizeText));
        OnPropertyChanged(nameof(PreviewHeadSizeKBText));

        OnPropertyChanged(nameof(DefaultFormatText));
        OnPropertyChanged(nameof(CompressionLevelText));
        OnPropertyChanged(nameof(CloseAfterCompressText));
        OnPropertyChanged(nameof(KeepOriginalExtensionText));
        OnPropertyChanged(nameof(PreserveDirectoryRootText));

        OnPropertyChanged(nameof(ExtractDefaultDestText));
        OnPropertyChanged(nameof(ExtractConflictActionText));
        OnPropertyChanged(nameof(ExtractOpenFolderAfterText));
        OnPropertyChanged(nameof(ExtractEnableDragText));
        OnPropertyChanged(nameof(ExtractPreserveFullPathText));
        OnPropertyChanged(nameof(ExtractDestAskText));
        OnPropertyChanged(nameof(ExtractDestSameDirText));
        OnPropertyChanged(nameof(ExtractDestDesktopText));
        OnPropertyChanged(nameof(ConflictAskText));
        OnPropertyChanged(nameof(ConflictOverwriteText));
        OnPropertyChanged(nameof(ConflictOverwriteOlderText));
        OnPropertyChanged(nameof(ConflictOverwriteSmallerText));
        OnPropertyChanged(nameof(ConflictRenameText));
        OnPropertyChanged(nameof(ConflictSkipText));

        OnPropertyChanged(nameof(ContextMenuGroupHeader));
        OnPropertyChanged(nameof(ContextMenuGroupBrowse));
        OnPropertyChanged(nameof(ContextMenuGroupCompress));
        OnPropertyChanged(nameof(ContextMenuGroupExtract));
        OnPropertyChanged(nameof(ContextMenuGroupDisplay));
        OnPropertyChanged(nameof(ContextMenuEnableOpen));
        OnPropertyChanged(nameof(ContextMenuEnableCompress));
        OnPropertyChanged(nameof(ContextMenuEnableCompressSeparate));
        OnPropertyChanged(nameof(ContextMenuEnableCompressCombined));
        OnPropertyChanged(nameof(ContextMenuEnableExtractHere));
        OnPropertyChanged(nameof(ContextMenuEnableSmartExtract));
        OnPropertyChanged(nameof(ContextMenuEnableExtractToNamed));
        OnPropertyChanged(nameof(ContextMenuEnableExtractTo));
        OnPropertyChanged(nameof(ContextMenuShowMenuIcons));
        OnPropertyChanged(nameof(ContextMenuEnableDynamicMenu));
        OnPropertyChanged(nameof(ContextMenuInstall));
        OnPropertyChanged(nameof(ContextMenuUninstall));

        OnPropertyChanged(nameof(AdvancedSevenZipPathText));
        OnPropertyChanged(nameof(AdvancedBrowseText));
        OnPropertyChanged(nameof(AdvancedPreserveRootText));
        OnPropertyChanged(nameof(AdvancedTempGroupText));
        OnPropertyChanged(nameof(AdvancedCleanPreviewTempText));
        OnPropertyChanged(nameof(AdvancedCleanAllTempText));
        OnPropertyChanged(nameof(AdvancedCleanOnStartupText));

        OnPropertyChanged(nameof(DebugText));
        OnPropertyChanged(nameof(LogPrivacyModeText));
        OnPropertyChanged(nameof(LogPrivacyModeOffText));
        OnPropertyChanged(nameof(LogPrivacyModeFilenameText));
        OnPropertyChanged(nameof(LogPrivacyModeExtensionText));
        OnPropertyChanged(nameof(LogPrivacyModeFullText));
        OnPropertyChanged(nameof(LogPrivacyHelpText));
        OnPropertyChanged(nameof(LogPathText));
        OnPropertyChanged(nameof(LogFilePath));

        OnPropertyChanged(nameof(TabLanguageHeader));
        OnPropertyChanged(nameof(TabAppearanceHeader));
        OnPropertyChanged(nameof(TabPasswordHeader));
        OnPropertyChanged(nameof(TabFileAssocHeader));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(LanguageTranslatorText));
        OnPropertyChanged(nameof(AppearanceThemeText));
        OnPropertyChanged(nameof(AppearanceThemeLightText));
        OnPropertyChanged(nameof(AppearanceThemeDarkText));
        OnPropertyChanged(nameof(AppearanceMaxRecentFilesText));
        OnPropertyChanged(nameof(PwdShowNotificationText));
        OnPropertyChanged(nameof(PwdRevealDefaultText));
        OnPropertyChanged(nameof(FileAssocDescText));
        OnPropertyChanged(nameof(FileAssocSelectAllText));
        OnPropertyChanged(nameof(FileAssocDeselectAllText));

        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(CancelText));

        // Re-populate localized combo options when culture changes
        PopulateComboOptions();
        SetSelectedOptions();
    }

    [RelayCommand]
    private void Save()
    {
        // Compress
        _settings.DefaultFormat = SelectedDefaultFormatOption?.Value ?? DefaultFormat;
        _settings.DefaultLevel = DefaultLevel;
        _settings.CloseAfterCompress = CloseAfterCompress;
        _settings.KeepOriginalExtension = KeepOriginalExtension;

        // Compression advanced
        _settings.SevenZipCompressionMethod = SelectedSevenZipCompressionMethodOption?.Value ?? SevenZipCompressionMethod;
        _settings.SevenZipSolid = SevenZipSolid;
        _settings.SevenZipSolidBlockSize = SelectedSevenZipSolidBlockSizeOption?.Value ?? SevenZipSolidBlockSize;
        _settings.SevenZipDictionarySize = int.Parse(SelectedSevenZipDictionarySizeOption?.Value ?? SevenZipDictionarySize.ToString());
        _settings.SevenZipNumFastBytes = int.Parse(SelectedSevenZipNumFastBytesOption?.Value ?? SevenZipNumFastBytes.ToString());
        _settings.SevenZipMatchFinder = SelectedSevenZipMatchFinderOption?.Value ?? SevenZipMatchFinder;
        _settings.ZipCompressionMethod = SelectedZipCompressionMethodOption?.Value ?? ZipCompressionMethod;
        _settings.ZipEncryptionMethod = SelectedZipEncryptionMethodOption?.Value ?? ZipEncryptionMethod;
        _settings.ZipEncoding = SelectedZipEncodingOption?.Value ?? ZipEncoding;
        _settings.SevenZipEncryptHeaders = SevenZipEncryptHeaders;

        // Preview
        _settings.EnableImagePreview = EnableImagePreview;
        _settings.EnableTextPreview = EnableTextPreview;
        _settings.MaxTextPreviewBytes = MaxTextPreviewBytes;
        _settings.TextPreviewFontSize = TextPreviewFontSize;
        _settings.TextPreviewFontFamily = SelectedTextFontFamilyOption?.Value ?? TextPreviewFontFamily;
        _settings.FontPreviewFontSize = FontPreviewFontSize;
        _settings.FontPreviewSampleText = FontPreviewSampleText;
        _settings.MaxTablePreviewRows = MaxTablePreviewRows;
        _settings.MaxTablePreviewCols = MaxTablePreviewCols;
        _settings.MaxPreviewFileSize = MaxPreviewFileSize;
        _settings.PreviewPosition = int.Parse(SelectedPreviewPositionOption?.Value ?? "4");
        _settings.InfoPanelOrientation = SelectedInfoPanelOrientationOption?.Value ?? InfoPanelOrientation;
        _settings.ShowPreviewPanel = ShowPreviewPanel;
        _settings.UseColorEmoji = UseColorEmoji;
        _settings.EnableFormatDetection = EnableFormatDetection;
        _settings.PreviewHeadSize = PreviewHeadSize;

        // Debug
        _settings.EnableDebugLogging = EnableDebugLogging;
        _settings.LogPrivacyMode = SelectedLogPrivacyModeOption?.Value ?? LogPrivacyMode;

        // Extract
        _settings.ExtractDestination = SelectedExtractDestinationOption?.Value ?? ExtractDestination;
        _settings.FileConflictAction = SelectedFileConflictActionOption?.Value ?? FileConflictAction;
        _settings.OpenFolderAfterExtract = OpenFolderAfterExtract;
        _settings.EnableDragExtract = EnableDragExtract;
        _settings.ExtractPreserveFullPath = ExtractPreserveFullPath;

        // ContextMenu
        _settings.EnableOpenMenu = EnableOpenMenu;
        _settings.EnableCompressMenu = EnableCompressMenu;
        _settings.EnableCompressSeparate = EnableCompressSeparate;
        _settings.EnableCompressCombined = EnableCompressCombined;
        _settings.EnableExtractHereMenu = EnableExtractHereMenu;
        _settings.EnableSmartExtractMenu = EnableSmartExtractMenu;
        _settings.EnableExtractToNamedMenu = EnableExtractToNamedMenu;
        _settings.EnableExtractToMenu = EnableExtractToMenu;
        _settings.ShowMenuIcons = ShowMenuIcons;
        _settings.EnableDynamicMenu = EnableDynamicMenu;

        // Advanced
        _settings.SevenZipPath = SevenZipPath;
        _settings.PreserveDirectoryRoot = PreserveDirectoryRoot;
        _settings.CleanTempOnStartup = CleanTempOnStartup;

        // Language
        _settings.Language = SelectedSelectedLanguageOption?.Value ?? SelectedLanguage;

        // Appearance
        _settings.Theme = SelectedThemeOption?.Value ?? Theme;
        _settings.MaxRecentFiles = MaxRecentFiles;
        _settings.AppFontFamily = SelectedAppFontFamilyOption?.Value ?? AppFontFamily;

        // Password
        _settings.ShowPasswordMatchNotification = ShowPasswordMatchNotification;
        _settings.PasswordRevealByDefault = PasswordRevealByDefault;

        // File Assoc
        _settings.AssocZip = AssocZip;
        _settings.Assoc7z = Assoc7z;
        _settings.AssocRar = AssocRar;
        _settings.AssocTar = AssocTar;
        _settings.AssocTarGz = AssocTarGz;
        _settings.AssocGz = AssocGz;
        _settings.AssocIso = AssocIso;

        _settings.Save();
    }

    [RelayCommand]
    private void InstallShell()
    {
        // Placeholder: In a full implementation, this would call ShellIntegration
        // For now, we log the action
        Debug.WriteLine("Install context menu requested");
    }

    [RelayCommand]
    private void UninstallShell()
    {
        // Placeholder: In a full implementation, this would call ShellIntegration
        Debug.WriteLine("Uninstall context menu requested");
    }

    [RelayCommand]
    private void BrowseSevenZip()
    {
        // Placeholder: Would show a file dialog to select 7z.dll
        Debug.WriteLine("Browse for 7z.dll requested");
    }

    [RelayCommand]
    private void OpenLogPrivacyHelp()
    {
        var dialog = new LogPrivacyHelpDialog();
        dialog.Show();
    }

    [RelayCommand]
    private void CleanPreviewTemp()
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "Preview");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [RelayCommand]
    private void CleanAllTemp()
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [RelayCommand]
    private void SelectAllAssoc()
    {
        AssocZip = true;
        Assoc7z = true;
        AssocRar = true;
        AssocTar = true;
        AssocTarGz = true;
        AssocGz = true;
        AssocIso = true;
    }

    [RelayCommand]
    private void DeselectAllAssoc()
    {
        AssocZip = false;
        Assoc7z = false;
        AssocRar = false;
        AssocTar = false;
        AssocTarGz = false;
        AssocGz = false;
        AssocIso = false;
    }
}

public record Option(string Display, string Value);
