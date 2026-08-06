using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using System.Diagnostics;
using System.IO;
using System.Collections.ObjectModel;

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
    private bool _showPreviewInfoPanel = true;

    [ObservableProperty]
    private bool _useColorEmoji = true;

    [ObservableProperty]
    private bool _enableFormatDetection = true;

    [ObservableProperty]
    private int _previewHeadSize = 4096;

    // ── Metadata Panel Settings ──
    public MetadataPanelSettingsViewModel MetadataPanelSettings { get; }

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

    [ObservableProperty]
    private bool _deleteArchiveAfterExtract;

    [ObservableProperty]
    private string _doubleClickAction = "open";

    [ObservableProperty]
    private long _doubleClickOpenThreshold = 10 * 1024 * 1024;

    /// <summary>DoubleClickOpenThreshold 的 MB 版本（UI 显示用）</summary>
    public long DoubleClickOpenThresholdMB
    {
        get => DoubleClickOpenThreshold / (1024 * 1024);
        set
        {
            if (value < 0) value = 0;
            DoubleClickOpenThreshold = value * (1024 * 1024);
            OnPropertyChanged();
        }
    }

    partial void OnDoubleClickOpenThresholdChanged(long value)
    {
        OnPropertyChanged(nameof(DoubleClickOpenThresholdMB));
    }

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

    [ObservableProperty]
    private string _shellStatusText = "";

    [ObservableProperty]
    private bool _isShellInstalled;

    public bool IsShellNotInstalled => !IsShellInstalled;

    partial void OnIsShellInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsShellNotInstalled));
    }

    // ── Advanced ──
    [ObservableProperty]
    private string _sevenZipPath;

    [ObservableProperty]
    private bool _preserveDirectoryRoot;

    [ObservableProperty]
    private bool _cleanTempOnStartup;

    [ObservableProperty]
    private bool _allowElevation;

    // ── Default path priority ──
    public ObservableCollection<PathPriorityItemModel> PathPriorityItems { get; } = new();

    [ObservableProperty]
    private string _customPath = "";

    // ── Language ──
    [ObservableProperty]
    private string _selectedLanguage = "zh";

    // ── Appearance ──
    [ObservableProperty]
    private string _theme = "System";

    [ObservableProperty]
    private int _maxRecentFiles = 10;

    [ObservableProperty]
    private string _appFontFamily = "";

    [ObservableProperty]
    private string _compactnessMode = "Normal";

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

    // Per-extension assoc items (UI list)
    public System.Collections.ObjectModel.ObservableCollection<FormatAssocItemModel> AssocItems { get; } = new();

    [RelayCommand]
    private async Task AddCustomAssoc()
    {
        try
        {
            var dlg = new MantisZip.UI.Avalonia.Dialogs.AddAssocDialog();

            // Prefer owning dialog to MainWindow to avoid null-owner dialog issues
            var ownerWindow = (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var result = await dlg.ShowDialog<bool?>(ownerWindow);
            if (result != true) return;
            var ext = dlg.Extension;
            // validate duplicates
            if (string.IsNullOrEmpty(ext)) return;
            if (AssocItems.Any(i => i.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                await AppMessageBox.Show(LocalizationManager.T("Settings_Assoc_CustomAlreadyExists"), "", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if ((_settings.CustomAssocExtensions?.Count ?? 0) >= 20)
            {
                await AppMessageBox.Show(LocalizationManager.T("Settings_Assoc_CustomMaxReached"), "", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.CustomAssocExtensions.Add(ext);
            var item = CreateAssocItem(ext, isCustom: true);
            item.DeleteCommand = new RelayCommand(() => DeleteCustomExtension(item));
            AssocItems.Add(item);
        }
        catch (Exception ex)
        {
            App.DebugLog($"AddCustomAssoc failed: {ex.Message}");
            try { await AppMessageBox.Show(string.Format(LocalizationManager.T("Settings_Assoc_AddFailed"), ex.Message), LocalizationManager.T("Settings_Title"), MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
        }
    }

    private void DeleteCustomExtension(FormatAssocItemModel item)
    {
        if (item == null) return;
        if (!item.IsCustom) return;
        _settings.CustomAssocExtensions.Remove(item.Extension);
        AssocItems.Remove(item);
    }

    [RelayCommand]
    private void InstallSelectedAssoc()
    {
        // Install only checked items
        var selected = AssocItems.Where(i => i.IsEnabled).Select(i => i.Extension).ToList();
        if (selected.Count == 0) return;
        try
        {
            ShellIntegration.PrepareAssocRegistration();
            ShellIntegration.InstallAssociations(selected);
            RefreshAssocStatus();
            AppMessageBox.Show(LocalizationManager.T("Settings_Assoc_InstallDone"), LocalizationManager.T("Settings_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.DebugLog($"InstallSelectedAssoc failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void UninstallAllAssoc()
    {
        try
        {
            ShellIntegration.UninstallAssociations();
            RefreshAssocStatus();
            AppMessageBox.Show(LocalizationManager.T("Settings_Assoc_UninstallDone"), LocalizationManager.T("Settings_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.DebugLog($"UninstallAllAssoc failed: {ex.Message}");
        }
    }

    private FormatAssocItemModel CreateAssocItem(string ext, bool isCustom = false)
    {
        var desc = ext switch
        {
            ".zip" => LocalizationManager.T("Settings_Assoc_FormatDesc_Zip"),
            ".7z" => LocalizationManager.T("Settings_Assoc_FormatDesc_7z"),
            ".rar" => LocalizationManager.T("Settings_Assoc_FormatDesc_Rar"),
            ".tar" => LocalizationManager.T("Settings_Assoc_FormatDesc_Tar"),
            ".tgz" or ".tar.gz" => LocalizationManager.T("Settings_Assoc_FormatDesc_TarGz"),
            ".gz" => LocalizationManager.T("Settings_Assoc_FormatDesc_Gz"),
            ".iso" => LocalizationManager.T("Settings_Assoc_FormatDesc_Iso"),
            _ => LocalizationManager.T("Settings_Assoc_UserCustom")
        };

        var item = new FormatAssocItemModel
        {
            Extension = ext,
            Description = desc,
            Icon = IconService.GetFileIcon(ext),
            IsCustom = isCustom,
            IsEnabled = IsEnabledFromSettings(ext),
            CurrentHandler = ShellIntegration.GetCurrentHandler(ext)
        };

        return item;
    }

    private bool IsEnabledFromSettings(string ext)
    {
        return ext switch
        {
            ".zip" => AssocZip,
            ".7z" => Assoc7z,
            ".rar" => AssocRar,
            ".tar" => AssocTar,
            ".tgz" or ".tar.gz" => AssocTarGz,
            ".gz" => AssocGz,
            ".iso" => AssocIso,
            _ => _settings.CustomAssocExtensions?.Contains(ext) ?? false
        };
    }

    private void RefreshAssocStatus()
    {
        foreach (var item in AssocItems)
        {
            item.CurrentHandler = ShellIntegration.GetCurrentHandler(item.Extension);
            item.IsEnabled = IsEnabledFromSettings(item.Extension);
        }
    }

    private void PopulateAssocItems()
    {
        AssocItems.Clear();
        var builtins = new[] { ".zip", ".7z", ".rar", ".tar", ".tar.gz", ".gz", ".iso" };
        foreach (var ext in builtins)
        {
            // normalize .tar.gz -> .tgz for display consistency
            var displayExt = ext == ".tar.gz" ? ".tgz" : ext;
            var item = CreateAssocItem(displayExt, isCustom: false);
            item.DeleteCommand = null;
            AssocItems.Add(item);
        }

        if (_settings.CustomAssocExtensions != null)
        {
            foreach (var c in _settings.CustomAssocExtensions)
            {
                var item = CreateAssocItem(c, isCustom: true);
                item.DeleteCommand = new RelayCommand(() => DeleteCustomExtension(item));
                AssocItems.Add(item);
            }
        }
    }

    // ── Combo ItemSource properties ──
    public System.Collections.ObjectModel.ObservableCollection<Option> DefaultFormatOptions { get; } = new();
    [ObservableProperty] private Option? _selectedDefaultFormatOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> DefaultLevelOptions { get; } = new();
    [ObservableProperty] private Option? _selectedDefaultLevelOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> PreviewPositionOptions { get; } = new();
    [ObservableProperty] private Option? _selectedPreviewPositionOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> InfoPanelOrientationOptions { get; } = new();
    [ObservableProperty] private Option? _selectedInfoPanelOrientationOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> ThemeOptions { get; } = new();
    [ObservableProperty] private Option? _selectedThemeOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> CompactnessModeOptions { get; } = new();
    [ObservableProperty] private Option? _selectedCompactnessModeOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> SelectedLanguageOptions { get; } = new();
    [ObservableProperty] private Option? _selectedSelectedLanguageOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> LogPrivacyModeOptions { get; } = new();
    [ObservableProperty] private Option? _selectedLogPrivacyModeOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> ExtractDestinationOptions { get; } = new();
    [ObservableProperty] private Option? _selectedExtractDestinationOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> FileConflictActionOptions { get; } = new();
    [ObservableProperty] private Option? _selectedFileConflictActionOption;

    public System.Collections.ObjectModel.ObservableCollection<Option> DoubleClickActionOptions { get; } = new();
    [ObservableProperty] private Option? _selectedDoubleClickActionOption;

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

    // Compress sub-tab headers
    public string CompressTabGeneralHeader => LocalizationManager.T("Settings_Compress_Tab_General");
    public string CompressTabFormatHeader => LocalizationManager.T("Settings_Compress_Tab_Format");

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
    public string PreviewShowInfoPanelText => LocalizationManager.T("Settings_Preview_ShowInfoPanel");
    public string PreviewMaxFileSizeText => LocalizationManager.T("Settings_Preview_MaxFileSize");

    // Preview — Magic Detection
    public string EnableFormatDetectionText => LocalizationManager.T("Settings_EnableFormatDetection");
    public string PreviewHeadSizeText => LocalizationManager.T("Settings_PreviewHeadSize");

    // Preview sub-tab headers (General / Image / Torrent / Executable / Metadata Panel)
    public string PreviewTabGeneralHeader => LocalizationManager.T("Settings_Preview_Tab_General");
    public string PreviewTabImageHeader => LocalizationManager.T("Settings_Preview_Tab_Image");
    public string PreviewTabTorrentHeader => LocalizationManager.T("Settings_Preview_Tab_Torrent");
    public string PreviewTabExecutableHeader => LocalizationManager.T("Settings_Preview_Tab_Executable");
    public string PreviewTabMetadataPanelHeader => LocalizationManager.T("Settings_Preview_Tab_MetadataPanel");

    // Section titles
    public string FormatDetectionSectionText => LocalizationManager.T("Settings_FormatDetection");
    public string TorrentComingSoonText => LocalizationManager.T("Settings_Preview_TorrentComingSoon");
    public string PeComingSoonText => LocalizationManager.T("Settings_Preview_PeComingSoon");
    public string PasswordOptionsSectionText => LocalizationManager.T("Settings_Pwd_Options");
    public string AssocSectionText => LocalizationManager.T("Settings_Assoc_Title");
    public string DebugSectionText => LocalizationManager.T("Settings_Debug_Title");

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
    public string CompressOptionsText => LocalizationManager.T("Settings_Compress_Options");
    public string ZipDefaultOptionsText => LocalizationManager.T("Settings_Zip_DefaultOptions");
    public string ZipEncodingText => LocalizationManager.T("Settings_Zip_Encoding");
    public string ZipCompressionMethodText => LocalizationManager.T("Settings_Zip_CompressionMethod");
    public string ZipEncryptionMethodText => LocalizationManager.T("Settings_Zip_EncryptionMethod");
    public string SevenZipDefaultOptionsText => LocalizationManager.T("Settings_SevenZip_DefaultOptions");
    public string SevenZipCompressionMethodText => LocalizationManager.T("Settings_SevenZip_CompressionMethod");
    public string SevenZipSolidText => LocalizationManager.T("Settings_SevenZip_Solid");
    public string SevenZipEncryptHeadersText => LocalizationManager.T("Settings_SevenZip_EncryptHeaders");
    public string SevenZipSolidBlockSizeText => LocalizationManager.T("Settings_SevenZip_SolidBlockSize");
    public string SevenZipDictionarySizeText => LocalizationManager.T("Settings_SevenZip_DictionarySize");
    public string SevenZipNumFastBytesText => LocalizationManager.T("Settings_SevenZip_NumFastBytes");
    public string SevenZipMatchFinderText => LocalizationManager.T("Settings_SevenZip_MatchFinder");

    // Extract strings
    public string ExtractDefaultDestText => LocalizationManager.T("Settings_Extract_DefaultDest");
    public string ExtractConflictActionText => LocalizationManager.T("Settings_Extract_ConflictAction");
    public string ExtractOpenFolderAfterText => LocalizationManager.T("Settings_Extract_OpenFolderAfter");
    public string ExtractEnableDragText => LocalizationManager.T("Settings_Extract_EnableDragExtract");
    public string ExtractPreserveFullPathText => LocalizationManager.T("Settings_Extract_PreserveFullPath");
    public string ExtractDeleteAfterExtractText => LocalizationManager.T("Settings_Extract_DeleteArchiveAfterExtract");
    public string ExtractDoubleClickActionText => LocalizationManager.T("Settings_Extract_DoubleClickAction");
    public string ExtractDoubleClickThresholdText => LocalizationManager.T("Settings_Extract_DoubleClickThreshold");

    // DoubleClickAction option display texts
    public string DoubleClickActionOpenText => LocalizationManager.T("Settings_DoubleClick_Open");
    public string DoubleClickActionExtractHereText => LocalizationManager.T("Settings_DoubleClick_ExtractHere");
    public string DoubleClickActionSmartExtractText => LocalizationManager.T("Settings_DoubleClick_SmartExtract");
    public string DoubleClickActionExtractToText => LocalizationManager.T("Settings_DoubleClick_ExtractTo");

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
    public string ContextMenuStatusGroup => LocalizationManager.T("Settings_ContextMenu_StatusGroup");
    public string ContextMenuBtnApply => LocalizationManager.T("Settings_ContextMenu_BtnApply");

    // Advanced strings
    public string AdvancedSevenZipPathText => LocalizationManager.T("Settings_Advanced_SevenZipPath");
    public string AdvancedBrowseText => LocalizationManager.T("Settings_Advanced_Browse");
    public string AdvancedPreserveRootText => LocalizationManager.T("Settings_Advanced_PreserveRoot");
    public string AdvancedTempGroupText => LocalizationManager.T("Settings_Advanced_TempGroup");
    public string AdvancedCleanPreviewTempText => LocalizationManager.T("Settings_Advanced_CleanPreviewTemp");
    public string AdvancedCleanAllTempText => LocalizationManager.T("Settings_Advanced_CleanAllTemp");
    public string AdvancedCleanOnStartupText => LocalizationManager.T("Settings_Advanced_CleanOnStartup");
    public string AdvancedAllowElevationText => LocalizationManager.T("Settings_Advanced_AllowElevation");

    public string DefaultPathGroupHeader => LocalizationManager.T("Settings_DefaultPath_GroupHeader");
    public string DefaultPathDesktopRow => LocalizationManager.T("Settings_DefaultPath_DesktopRow");
    public string DefaultPathHint => LocalizationManager.T("Settings_DefaultPath_Hint");

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
    public string AppearanceThemeSystemText => LocalizationManager.T("Settings_Appearance_Theme_System");
    public string AppearanceThemeLightText => LocalizationManager.T("Settings_Appearance_Theme_Light");
    public string AppearanceThemeDarkText => LocalizationManager.T("Settings_Appearance_Theme_Dark");
    public string AppearanceMaxRecentFilesText => LocalizationManager.T("Settings_Appearance_MaxRecentFiles");
    public string AppearanceAppFontFamilyText => LocalizationManager.T("Settings_Appearance_AppFontFamily");
    public string AppearanceCompactnessText => LocalizationManager.T("Settings_Appearance_Compactness");
    public string AppearanceCompactnessCompactText => LocalizationManager.T("Settings_Appearance_Compactness_Compact");
    public string AppearanceCompactnessNormalText => LocalizationManager.T("Settings_Appearance_Compactness_Normal");
    public string AppearanceCompactnessLooseText => LocalizationManager.T("Settings_Appearance_Compactness_Loose");

    // ── Password strings ──
    public string PwdShowNotificationText => LocalizationManager.T("Settings_Pwd_ShowNotification");
    public string PwdRevealDefaultText => LocalizationManager.T("Settings_Pwd_RevealDefault");

    // ── File Assoc strings ──
    public string FileAssocDescText => LocalizationManager.T("Settings_Assoc_Desc");
    public string FileAssocSelectAllText => LocalizationManager.T("Settings_Assoc_SelectAll");
    public string FileAssocDeselectAllText => LocalizationManager.T("Settings_Assoc_DeselectAll");
    public string FileAssocAddText => LocalizationManager.T("Settings_Assoc_Add");
    public string FileAssocInstallText => LocalizationManager.T("Settings_Assoc_Install");
        public string FileAssocUninstallText => LocalizationManager.T("Settings_Assoc_Uninstall");

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
        _showPreviewInfoPanel = _settings.ShowPreviewInfoPanel;
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
        _deleteArchiveAfterExtract = _settings.DeleteArchiveAfterExtract;
        _doubleClickAction = _settings.DoubleClickAction;
        _doubleClickOpenThreshold = _settings.DoubleClickOpenThreshold;

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
        _shellStatusText = LocalizationManager.T("Settings_ContextMenu_StatusChecking");
        RefreshShellStatus();

        // Advanced
        _sevenZipPath = _settings.SevenZipPath;
        _preserveDirectoryRoot = _settings.PreserveDirectoryRoot;
        _cleanTempOnStartup = _settings.CleanTempOnStartup;
        _allowElevation = _settings.AllowElevation;

        // Default path priority
        _customPath = _settings.CustomDefaultPath ?? "";
        ReloadPathPriorityItems();

    // Language
    _selectedLanguage = _settings.Language;

    // Metadata panel
    MetadataPanelSettings = new MetadataPanelSettingsViewModel();
    MetadataPanelSettings.Load();

        // Appearance
        _theme = _settings.Theme;
        _maxRecentFiles = _settings.MaxRecentFiles;
        _appFontFamily = _settings.AppFontFamily;
        _compactnessMode = _settings.CompactnessMode;

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

        // Populate per-extension assoc items
        PopulateAssocItems();
        RefreshAssocStatus();

        LocalizationManager.CultureChanged += OnCultureChanged;
    }

    private void PopulateComboOptions()
    {
        DefaultFormatOptions.Clear();
        foreach (var val in CompressionOptionData.ArchiveFormatValues)
            DefaultFormatOptions.Add(new Option(val, val));

        DefaultLevelOptions.Clear();
        foreach (var opt in CompressionOptionData.LevelOptions)
        {
            var display = LocalizationManager.T("Compress_Level_" + opt.Tag switch
            {
                "0" => "Store",
                "3" => "Fast",
                "5" => "Normal",
                "9" => "Max",
                _ => "Normal",
            });
            DefaultLevelOptions.Add(new Option(display, opt.Tag));
        }

        PreviewPositionOptions.Clear();
        PreviewPositionOptions.Add(new Option(PreviewPositionBottomText, "1"));
        PreviewPositionOptions.Add(new Option(PreviewPositionBelowTreeText, "2"));
        PreviewPositionOptions.Add(new Option(PreviewPositionBelowListText, "3"));
        PreviewPositionOptions.Add(new Option(PreviewPositionRightText, "4"));

        InfoPanelOrientationOptions.Clear();
        InfoPanelOrientationOptions.Add(new Option(PreviewInfoOrientationHorizontalText, "Horizontal"));
        InfoPanelOrientationOptions.Add(new Option(PreviewInfoOrientationVerticalText, "Vertical"));

        ThemeOptions.Clear();
        ThemeOptions.Add(new Option(AppearanceThemeSystemText, "System"));
        ThemeOptions.Add(new Option(AppearanceThemeLightText, "Light"));
        ThemeOptions.Add(new Option(AppearanceThemeDarkText, "Dark"));

        CompactnessModeOptions.Clear();
        CompactnessModeOptions.Add(new Option(AppearanceCompactnessCompactText, "Compact"));
        CompactnessModeOptions.Add(new Option(AppearanceCompactnessNormalText, "Normal"));
        CompactnessModeOptions.Add(new Option(AppearanceCompactnessLooseText, "Loose"));

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

        DoubleClickActionOptions.Clear();
        DoubleClickActionOptions.Add(new Option(DoubleClickActionOpenText, "open"));
        DoubleClickActionOptions.Add(new Option(DoubleClickActionExtractHereText, "extract-here"));
        DoubleClickActionOptions.Add(new Option(DoubleClickActionSmartExtractText, "smart-extract"));
        DoubleClickActionOptions.Add(new Option(DoubleClickActionExtractToText, "extract-dialog"));

        // 字体列表（文本预览 + 全局界面共用同一份系统字体枚举）
        PopulateFontOptions();

        // Compression advanced combos — populated from CompressionOptionData (single source of truth)
        SevenZipCompressionMethodOptions.Clear();
        foreach (var opt in CompressionOptionData.SevenZipMethods)
            SevenZipCompressionMethodOptions.Add(new Option(opt.Display, opt.Tag));

        SevenZipSolidBlockSizeOptions.Clear();
        foreach (var opt in CompressionOptionData.SevenZipSolidBlockSizes)
        {
            var display = opt.Tag == "" ? LocalizationManager.T("FormatOptions_7z_SolidBlockSize_Default") : opt.Display;
            SevenZipSolidBlockSizeOptions.Add(new Option(display, opt.Tag));
        }

        SevenZipDictionarySizeOptions.Clear();
        foreach (var opt in CompressionOptionData.SevenZipDictionarySizes)
        {
            var display = opt.Tag == "0" ? LocalizationManager.T("FormatOptions_7z_DictSize_Default") : opt.Display;
            SevenZipDictionarySizeOptions.Add(new Option(display, opt.Tag));
        }

        SevenZipNumFastBytesOptions.Clear();
        foreach (var opt in CompressionOptionData.SevenZipNumFastBytes)
        {
            var display = opt.Tag == "0" ? LocalizationManager.T("FormatOptions_7z_WordSize_Default") : opt.Display;
            SevenZipNumFastBytesOptions.Add(new Option(display, opt.Tag));
        }

        SevenZipMatchFinderOptions.Clear();
        foreach (var opt in CompressionOptionData.SevenZipMatchFinders)
        {
            var display = opt.Tag == "" ? LocalizationManager.T("FormatOptions_7z_MatchFinder_Default") : opt.Display;
            SevenZipMatchFinderOptions.Add(new Option(display, opt.Tag));
        }

        ZipCompressionMethodOptions.Clear();
        foreach (var opt in CompressionOptionData.ZipCompressionMethods)
            ZipCompressionMethodOptions.Add(new Option(opt.Display, opt.Tag));

        ZipEncryptionMethodOptions.Clear();
        foreach (var opt in CompressionOptionData.ZipEncryptionMethods)
            ZipEncryptionMethodOptions.Add(new Option(opt.Display, opt.Tag));

        ZipEncodingOptions.Clear();
        foreach (var opt in CompressionOptionData.ZipEncodings)
        {
            var display = opt.Tag == "default" ? LocalizationManager.T("FormatOptions_Zip_EncodingDefault") : opt.Display;
            ZipEncodingOptions.Add(new Option(display, opt.Tag));
        }
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
        SelectedDefaultLevelOption = DefaultLevelOptions.FirstOrDefault(o => o.Value == DefaultLevel.ToString());
        SelectedPreviewPositionOption = PreviewPositionOptions.FirstOrDefault(o => o.Value == PreviewPosition.ToString());
        SelectedInfoPanelOrientationOption = InfoPanelOrientationOptions.FirstOrDefault(o => o.Value == InfoPanelOrientation);
        SelectedThemeOption = ThemeOptions.FirstOrDefault(o => o.Value == Theme);
        SelectedCompactnessModeOption = CompactnessModeOptions.FirstOrDefault(o => o.Value == CompactnessMode);
        SelectedSelectedLanguageOption = SelectedLanguageOptions.FirstOrDefault(o => o.Value == SelectedLanguage);
        SelectedLogPrivacyModeOption = LogPrivacyModeOptions.FirstOrDefault(o => o.Value == LogPrivacyMode);
        SelectedExtractDestinationOption = ExtractDestinationOptions.FirstOrDefault(o => o.Value == ExtractDestination);
        SelectedFileConflictActionOption = FileConflictActionOptions.FirstOrDefault(o => o.Value == FileConflictAction);

        SelectedDoubleClickActionOption = DoubleClickActionOptions.FirstOrDefault(o => o.Value == DoubleClickAction);

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
        OnPropertyChanged(nameof(CompressTabGeneralHeader));
        OnPropertyChanged(nameof(CompressTabFormatHeader));

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
    OnPropertyChanged(nameof(PreviewShowInfoPanelText));
        OnPropertyChanged(nameof(PreviewMaxFileSizeText));
        OnPropertyChanged(nameof(MaxTextPreviewMBText));
        OnPropertyChanged(nameof(MaxPreviewFileSizeMBText));

        OnPropertyChanged(nameof(EnableFormatDetectionText));
        OnPropertyChanged(nameof(PreviewHeadSizeText));
        OnPropertyChanged(nameof(PreviewHeadSizeKBText));

        OnPropertyChanged(nameof(PreviewTabGeneralHeader));
        OnPropertyChanged(nameof(PreviewTabImageHeader));
        OnPropertyChanged(nameof(PreviewTabTorrentHeader));
        OnPropertyChanged(nameof(PreviewTabExecutableHeader));
        OnPropertyChanged(nameof(PreviewTabMetadataPanelHeader));
        OnPropertyChanged(nameof(FormatDetectionSectionText));
        OnPropertyChanged(nameof(TorrentComingSoonText));
        OnPropertyChanged(nameof(PeComingSoonText));
        OnPropertyChanged(nameof(PasswordOptionsSectionText));
        OnPropertyChanged(nameof(AssocSectionText));
        OnPropertyChanged(nameof(DebugSectionText));

        OnPropertyChanged(nameof(DefaultFormatText));
        OnPropertyChanged(nameof(CompressionLevelText));
        OnPropertyChanged(nameof(CloseAfterCompressText));
        OnPropertyChanged(nameof(KeepOriginalExtensionText));
        OnPropertyChanged(nameof(PreserveDirectoryRootText));
        OnPropertyChanged(nameof(CompressOptionsText));
        OnPropertyChanged(nameof(ZipDefaultOptionsText));
        OnPropertyChanged(nameof(ZipEncodingText));
        OnPropertyChanged(nameof(ZipCompressionMethodText));
        OnPropertyChanged(nameof(ZipEncryptionMethodText));
        OnPropertyChanged(nameof(SevenZipDefaultOptionsText));
        OnPropertyChanged(nameof(SevenZipCompressionMethodText));
        OnPropertyChanged(nameof(SevenZipSolidText));
        OnPropertyChanged(nameof(SevenZipEncryptHeadersText));
        OnPropertyChanged(nameof(SevenZipSolidBlockSizeText));
        OnPropertyChanged(nameof(SevenZipDictionarySizeText));
        OnPropertyChanged(nameof(SevenZipNumFastBytesText));
        OnPropertyChanged(nameof(SevenZipMatchFinderText));

        OnPropertyChanged(nameof(ExtractDefaultDestText));
        OnPropertyChanged(nameof(ExtractConflictActionText));
        OnPropertyChanged(nameof(ExtractOpenFolderAfterText));
        OnPropertyChanged(nameof(ExtractEnableDragText));
        OnPropertyChanged(nameof(ExtractPreserveFullPathText));
        OnPropertyChanged(nameof(ExtractDeleteAfterExtractText));
        OnPropertyChanged(nameof(ExtractDoubleClickActionText));
        OnPropertyChanged(nameof(ExtractDoubleClickThresholdText));
        OnPropertyChanged(nameof(DoubleClickActionOpenText));
        OnPropertyChanged(nameof(DoubleClickActionExtractHereText));
        OnPropertyChanged(nameof(DoubleClickActionSmartExtractText));
        OnPropertyChanged(nameof(DoubleClickActionExtractToText));
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
        OnPropertyChanged(nameof(ContextMenuStatusGroup));
        OnPropertyChanged(nameof(ContextMenuBtnApply));

        // Refresh shell status display (localized text)
        RefreshShellStatus();

        OnPropertyChanged(nameof(AdvancedSevenZipPathText));
        OnPropertyChanged(nameof(AdvancedBrowseText));
        OnPropertyChanged(nameof(AdvancedPreserveRootText));
        OnPropertyChanged(nameof(AdvancedTempGroupText));
        OnPropertyChanged(nameof(AdvancedCleanPreviewTempText));
        OnPropertyChanged(nameof(AdvancedCleanAllTempText));
        OnPropertyChanged(nameof(AdvancedCleanOnStartupText));
        OnPropertyChanged(nameof(AdvancedAllowElevationText));
        OnPropertyChanged(nameof(DefaultPathGroupHeader));
        OnPropertyChanged(nameof(DefaultPathDesktopRow));
        OnPropertyChanged(nameof(DefaultPathHint));
        // 刷新排序项的名称（本地化文案随语言切换）
        RefreshPathPriorityDisplayNames();

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
        OnPropertyChanged(nameof(AppearanceThemeSystemText));
        OnPropertyChanged(nameof(AppearanceThemeLightText));
        OnPropertyChanged(nameof(AppearanceThemeDarkText));
        OnPropertyChanged(nameof(AppearanceMaxRecentFilesText));
        OnPropertyChanged(nameof(AppearanceCompactnessText));
        OnPropertyChanged(nameof(AppearanceCompactnessCompactText));
        OnPropertyChanged(nameof(AppearanceCompactnessNormalText));
        OnPropertyChanged(nameof(AppearanceCompactnessLooseText));
        OnPropertyChanged(nameof(PwdShowNotificationText));
        OnPropertyChanged(nameof(PwdRevealDefaultText));
        OnPropertyChanged(nameof(FileAssocDescText));
        OnPropertyChanged(nameof(FileAssocSelectAllText));
        OnPropertyChanged(nameof(FileAssocDeselectAllText));
        OnPropertyChanged(nameof(FileAssocAddText));
        OnPropertyChanged(nameof(FileAssocInstallText));
        OnPropertyChanged(nameof(FileAssocUninstallText));

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
        _settings.DefaultLevel = int.TryParse(SelectedDefaultLevelOption?.Value, out var l) ? l : 5;
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
        _settings.ShowPreviewInfoPanel = ShowPreviewInfoPanel;
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
        _settings.DeleteArchiveAfterExtract = DeleteArchiveAfterExtract;
        _settings.DoubleClickAction = SelectedDoubleClickActionOption?.Value ?? DoubleClickAction;
        _settings.DoubleClickOpenThreshold = DoubleClickOpenThreshold;

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
        _settings.AllowElevation = AllowElevation;

        // Language
        var languageCode = SelectedSelectedLanguageOption?.Value ?? SelectedLanguage;
        _settings.Language = languageCode;
        // Apply immediately so the change takes effect without restart
        // (matches WPF LanguageManager.SwitchTo behavior)
        LocalizationManager.CurrentLanguage = languageCode == "en" ? AppLanguage.English : AppLanguage.Chinese;

        // Appearance
        _settings.Theme = SelectedThemeOption?.Value ?? Theme;
        _settings.MaxRecentFiles = MaxRecentFiles;
        _settings.AppFontFamily = SelectedAppFontFamilyOption?.Value ?? AppFontFamily;
        _settings.CompactnessMode = SelectedCompactnessModeOption?.Value ?? CompactnessMode;

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

        // Persist custom extensions from the AssocItems list
        var customList = AssocItems.Where(i => i.IsCustom).Select(i => i.Extension).ToList();
        _settings.CustomAssocExtensions = customList;

        // Metadata panel
        MetadataPanelSettings.ApplyAllTypeConfigs();
        MetadataPanelSettings.Save();

        // Default path priority
        _settings.DefaultPathOrder = PathPriorityItems.Select(p => p.Kind).ToList();
        _settings.CustomDefaultPath = CustomPath ?? "";

        _settings.Save();
    }

    // ── Default path priority helpers ──────────────────────────────────────

    /// <summary>从 AppSettings.DefaultPathOrder 重建排序列表项（Kind→DisplayName 映射 + 首末可移动状态）。</summary>
    private void ReloadPathPriorityItems()
    {
        var order = _settings.DefaultPathOrder ?? new List<string>();
        // 过滤已知值、去重，保持用户顺序
        var distinct = new List<string>();
        foreach (var kind in order)
        {
            if (kind is "context" or "explorer" or "recent" or "custom" && !distinct.Contains(kind))
                distinct.Add(kind);
        }
        // 补上缺失的已知项（防止持久化数据不全导致链不完整）
        foreach (var kind in new[] { "context", "explorer", "recent", "custom" })
        {
            if (!distinct.Contains(kind)) distinct.Add(kind);
        }

        PathPriorityItems.Clear();
        foreach (var kind in distinct)
        {
            PathPriorityItems.Add(new PathPriorityItemModel
            {
                Kind = kind,
                DisplayName = kind switch
                {
                    "context" => LocalizationManager.T("Settings_DefaultPath_Context"),
                    "explorer" => LocalizationManager.T("Settings_DefaultPath_Explorer"),
                    "recent" => LocalizationManager.T("Settings_DefaultPath_Recent"),
                    "custom" => LocalizationManager.T("Settings_DefaultPath_Custom"),
                    _ => kind
                }
            });
        }
        RefreshPathPriorityMoveState();
    }

    /// <summary>语言切换时刷新排序项的名称（保持 Kind 顺序不变）。</summary>
    private void RefreshPathPriorityDisplayNames()
    {
        foreach (var item in PathPriorityItems)
        {
            item.DisplayName = item.Kind switch
            {
                "context" => LocalizationManager.T("Settings_DefaultPath_Context"),
                "explorer" => LocalizationManager.T("Settings_DefaultPath_Explorer"),
                "recent" => LocalizationManager.T("Settings_DefaultPath_Recent"),
                "custom" => LocalizationManager.T("Settings_DefaultPath_Custom"),
                _ => item.Kind
            };
        }
    }

    private void RefreshPathPriorityMoveState()
    {
        for (var i = 0; i < PathPriorityItems.Count; i++)
        {
            PathPriorityItems[i].CanMoveUp = i > 0;
            PathPriorityItems[i].CanMoveDown = i < PathPriorityItems.Count - 1;
        }
    }

    [RelayCommand]
    private void MovePathUp(PathPriorityItemModel? item)
    {
        if (item == null) return;
        var idx = PathPriorityItems.IndexOf(item);
        if (idx <= 0) return;
        PathPriorityItems.Move(idx, idx - 1);
        RefreshPathPriorityMoveState();
    }

    [RelayCommand]
    private void MovePathDown(PathPriorityItemModel? item)
    {
        if (item == null) return;
        var idx = PathPriorityItems.IndexOf(item);
        if (idx < 0 || idx >= PathPriorityItems.Count - 1) return;
        PathPriorityItems.Move(idx, idx + 1);
        RefreshPathPriorityMoveState();
    }

    private void RefreshShellStatus()
    {
        try
        {
            var installed = ShellIntegration.IsInstalled;
            if (installed)
            {
                var dynStatus = ShellIntegration.GetDynamicMenuStatus();
                ShellStatusText = dynStatus switch
                {
                    "active" => LocalizationManager.T("Settings_ContextMenu_StatusDynamicActive"),
                    "fallback" => LocalizationManager.T("Settings_ContextMenu_StatusDynamicFallback"),
                    _ => LocalizationManager.T("Settings_ContextMenu_StatusInstalled")
                };
            }
            else
            {
                ShellStatusText = LocalizationManager.T("Settings_ContextMenu_StatusNotInstalled");
            }
            IsShellInstalled = installed;
        }
        catch (Exception ex)
        {
            App.DebugLog($"RefreshShellStatus failed: {ex.Message}");
            ShellStatusText = ex.Message;
            IsShellInstalled = false;
        }
    }

    [RelayCommand]
    private async Task InstallShell()
    {
        try
        {
            ShellStatusText = LocalizationManager.T("Settings_ContextMenu_StatusChecking");
            Save();
            ShellIntegration.Uninstall();
            ShellIntegration.Install();
            ShellIntegration.CheckComStatus();
            RefreshShellStatus();
            await AppMessageBox.Show(
                LocalizationManager.T("Settings_ContextMenu_InstalledMsg"),
                LocalizationManager.T("Settings_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.DebugLog($"InstallShell failed: {ex.Message}");
            ShellStatusText = string.Format(
                LocalizationManager.T("Settings_ContextMenu_InstallFailed"), ex.Message);
            await AppMessageBox.Show(
                string.Format(LocalizationManager.T("Settings_ContextMenu_InstallFailed"), ex.Message),
                LocalizationManager.T("Settings_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task UninstallShell()
    {
        try
        {
            ShellStatusText = LocalizationManager.T("Settings_ContextMenu_StatusChecking");
            ShellIntegration.Uninstall();
            RefreshShellStatus();
            await AppMessageBox.Show(
                LocalizationManager.T("Settings_ContextMenu_UpdatedMsg"),
                LocalizationManager.T("Settings_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.DebugLog($"UninstallShell failed: {ex.Message}");
            ShellStatusText = string.Format(
                LocalizationManager.T("Settings_ContextMenu_UninstallFailed"), ex.Message);
            await AppMessageBox.Show(
                string.Format(LocalizationManager.T("Settings_ContextMenu_UninstallFailed"), ex.Message),
                LocalizationManager.T("Settings_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ApplyShellChanges()
    {
        try
        {
            ShellStatusText = LocalizationManager.T("Settings_ContextMenu_StatusChecking");
            Save();
            ShellIntegration.Uninstall();
            ShellIntegration.Install();
            ShellIntegration.CheckComStatus();
            RefreshShellStatus();
        }
        catch (Exception ex)
        {
            App.DebugLog($"ApplyShellChanges failed: {ex.Message}");
            ShellStatusText = string.Format(
                LocalizationManager.T("Settings_ContextMenu_InstallFailed"), ex.Message);
        }
    }

    [RelayCommand]
    private async Task BrowseSevenZip()
    {
        try
        {
            var ownerWindow = (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var path = await CustomFilePickerDialog.ShowOpenFileAsync(
                ownerWindow!,
                initialPath: string.IsNullOrEmpty(SevenZipPath) ? null : SevenZipPath,
                fileExtensions: ["*.dll"]);
            if (!string.IsNullOrEmpty(path))
            {
                SevenZipPath = path;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] BrowseSevenZip failed: {ex.Message}");
        }
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
