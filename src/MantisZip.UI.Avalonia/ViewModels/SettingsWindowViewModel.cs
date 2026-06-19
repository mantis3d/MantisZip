using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private bool _enableDebugLogging;

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

    // Compress strings
    public string DefaultFormatText => LocalizationManager.T("Settings_DefaultFormat");
    public string CompressionLevelText => LocalizationManager.T("Settings_CompressionLevel");

    // Extract strings
    public string ExtractDefaultDestText => LocalizationManager.T("Settings_Extract_DefaultDest");
    public string ExtractConflictActionText => LocalizationManager.T("Settings_Extract_ConflictAction");
    public string ExtractOpenFolderAfterText => LocalizationManager.T("Settings_Extract_OpenFolderAfter");
    public string ExtractEnableDragText => LocalizationManager.T("Settings_Extract_EnableDragExtract");
    public string ExtractPreserveFullPathText => LocalizationManager.T("Settings_Extract_PreserveFullPath");

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

        // Debug
        _enableDebugLogging = _settings.EnableDebugLogging;

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

        LocalizationManager.CultureChanged += OnCultureChanged;
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
        OnPropertyChanged(nameof(DefaultFormatText));
        OnPropertyChanged(nameof(CompressionLevelText));

        OnPropertyChanged(nameof(ExtractDefaultDestText));
        OnPropertyChanged(nameof(ExtractConflictActionText));
        OnPropertyChanged(nameof(ExtractOpenFolderAfterText));
        OnPropertyChanged(nameof(ExtractEnableDragText));
        OnPropertyChanged(nameof(ExtractPreserveFullPathText));

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
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(CancelText));
    }

    [RelayCommand]
    private void Save()
    {
        // Compress
        _settings.DefaultFormat = DefaultFormat;
        _settings.DefaultLevel = DefaultLevel;

        // Preview
        _settings.EnableImagePreview = EnableImagePreview;
        _settings.EnableTextPreview = EnableTextPreview;
        _settings.MaxTextPreviewBytes = MaxTextPreviewBytes;
        _settings.TextPreviewFontSize = TextPreviewFontSize;

        // Debug
        _settings.EnableDebugLogging = EnableDebugLogging;

        // Extract
        _settings.ExtractDestination = ExtractDestination;
        _settings.FileConflictAction = FileConflictAction;
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
}
