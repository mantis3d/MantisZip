using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.UI.Avalonia.Models;

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
