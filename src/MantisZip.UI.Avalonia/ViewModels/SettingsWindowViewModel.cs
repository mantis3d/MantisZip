using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

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

    // ── Localized strings ──

    public string WindowTitle => LocalizationManager.T("Settings_Title");
    public string TabPreviewHeader => LocalizationManager.T("Settings_TabPreview");
    public string TabCompressHeader => LocalizationManager.T("Settings_TabCompress");
    public string TabDebugHeader => LocalizationManager.T("Settings_TabDebug");
    public string EnableImagePreviewText => LocalizationManager.T("Settings_EnableImagePreview");
    public string EnableTextPreviewText => LocalizationManager.T("Settings_EnableTextPreview");
    public string TextPreviewFontSizeText => LocalizationManager.T("Settings_TextPreviewFontSize");
    public string MaxPreviewBytesText => LocalizationManager.T("Settings_MaxPreviewBytes");
    public string DefaultFormatText => LocalizationManager.T("Settings_DefaultFormat");
    public string CompressionLevelText => LocalizationManager.T("Settings_CompressionLevel");
    public string EnableDebugLogText => LocalizationManager.T("Settings_EnableDebugLog");
    public string SaveText => LocalizationManager.T("Settings_Save");
    public string CancelText => LocalizationManager.T("Settings_Cancel");

    public SettingsWindowViewModel()
    {
        _settings = AppSettings.Load();
        _defaultFormat = _settings.DefaultFormat;
        _defaultLevel = _settings.DefaultLevel;
        _enableImagePreview = _settings.EnableImagePreview;
        _enableTextPreview = _settings.EnableTextPreview;
        _maxTextPreviewBytes = _settings.MaxTextPreviewBytes;
        _textPreviewFontSize = _settings.TextPreviewFontSize;
        _enableDebugLogging = _settings.EnableDebugLogging;

        LocalizationManager.CultureChanged += OnCultureChanged;
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(TabPreviewHeader));
        OnPropertyChanged(nameof(TabCompressHeader));
        OnPropertyChanged(nameof(TabDebugHeader));
        OnPropertyChanged(nameof(EnableImagePreviewText));
        OnPropertyChanged(nameof(EnableTextPreviewText));
        OnPropertyChanged(nameof(TextPreviewFontSizeText));
        OnPropertyChanged(nameof(MaxPreviewBytesText));
        OnPropertyChanged(nameof(DefaultFormatText));
        OnPropertyChanged(nameof(CompressionLevelText));
        OnPropertyChanged(nameof(EnableDebugLogText));
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(CancelText));
    }

    [RelayCommand]
    private void Save()
    {
        _settings.DefaultFormat = DefaultFormat;
        _settings.DefaultLevel = DefaultLevel;
        _settings.EnableImagePreview = EnableImagePreview;
        _settings.EnableTextPreview = EnableTextPreview;
        _settings.MaxTextPreviewBytes = MaxTextPreviewBytes;
        _settings.TextPreviewFontSize = TextPreviewFontSize;
        _settings.EnableDebugLogging = EnableDebugLogging;
        _settings.Save();
    }
}
