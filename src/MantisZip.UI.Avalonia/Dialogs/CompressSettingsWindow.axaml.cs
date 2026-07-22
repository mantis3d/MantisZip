using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 压缩设置窗口。通过 ShowDialog 返回 bool 结果，
/// 调用方在结果为 true 后读取 ViewModel 的属性构造 CompressRequest。
/// </summary>
public partial class CompressSettingsWindow : Window
{
    private bool _loaded;
    /// <summary>
    /// ViewModel，公开属性供调用方关闭后读取。
    /// </summary>
    public CompressSettingsViewModel ViewModel { get; }

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="CompressSettingsWindow(IReadOnlyList{string})"/>。
    /// </summary>
    public CompressSettingsWindow()
    {
        InitializeComponent();
        ViewModel = new CompressSettingsViewModel(Array.Empty<string>());
        DataContext = ViewModel;
        SubscribeViewModel();
    }

    public CompressSettingsWindow(IReadOnlyList<string> sourcePaths)
    {
        InitializeComponent();

        ViewModel = new CompressSettingsViewModel(sourcePaths);

        // 设置文件保存浏览回调
        ViewModel.BrowseOutput = async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save archive",
                DefaultExtension = ".zip",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("ZIP archives") { Patterns = new[] { "*.zip" } },
                    new FilePickerFileType("7z archives") { Patterns = new[] { "*.7z" } },
                    new FilePickerFileType("TAR.GZ archives") { Patterns = new[] { "*.tar.gz" } },
                }
            });
            return file?.Path?.LocalPath;
        };

        // 设置文件/文件夹选择回调
        ViewModel.PickFiles = async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select files to compress",
                AllowMultiple = true
            });
            return files?.Select(f => f.Path?.LocalPath).Where(p => p != null).ToList()!;
        };

        ViewModel.PickFolder = async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select folder to compress",
                AllowMultiple = false
            });
            return folders.Count >= 1 ? folders[0].Path?.LocalPath : null;
        };

        // 设置关闭回调
        ViewModel.CloseAction = async (result) =>
        {
            if (result)
            {
                // 关闭前将 DynamicFormatOptionsPanel 当前值写回 AppSettings
                SaveFormatOptionsToSettings();
            }
            Close(result);
            await Task.CompletedTask;
        };

        DataContext = ViewModel;
        SubscribeViewModel();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// 获取当前文件过滤条件。返回 null 表示不过滤。
    /// </summary>
    public FileFilterCriteria? GetFilter()
    {
        if (FileFilterControl == null) return null;
        if (!FileFilterControl.IsFilterEnabled) return null;
        var filter = FileFilterControl.GetFilter();
        return filter.IsActive ? filter : null;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        FormatOptionsPanel.LoadDefaults();
        LoadSplitSizeFromSettings();
        InitFileFilter();
    }

    /// <summary>初始化文件过滤控件（预设 + 事件）。</summary>
    private void InitFileFilter()
    {
        var settings = AppSettings.Load();
        FileFilterControl.LoadPresets(settings.FilterPresets);

        FileFilterControl.SavePresetRequested += name =>
        {
            var filter = FileFilterControl.GetFilter();
            if (!filter.IsActive) return;
            settings.AddPreset(new FileFilterPreset(name, filter));
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets);
        };

        FileFilterControl.DeletePresetRequested += preset =>
        {
            settings.FilterPresets.Remove(preset);
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets);
        };
    }

    /// <summary>
    /// 从 AppSettings 加载分卷大小设置。
    /// </summary>
    private void LoadSplitSizeFromSettings()
    {
        var s = AppSettings.Load();
        if (!string.IsNullOrEmpty(s.SplitSizeTag))
        {
            var option = ViewModel.SplitSizeOptions.FirstOrDefault(o => o.Tag == s.SplitSizeTag);
            if (option != null)
                ViewModel.SelectedSplitSizeOption = option;
        }
        if (!string.IsNullOrEmpty(s.CustomSplitSizeMB))
            ViewModel.CustomSplitSizeText = s.CustomSplitSizeMB;
    }

    /// <summary>
    /// 将 DynamicFormatOptionsPanel 的当前值保存到 AppSettings。
    /// 在关闭前调用，确保后续压缩流程能读取到最新的高级选项设置。
    /// </summary>
    private void SaveFormatOptionsToSettings()
    {
        var s = AppSettings.Load();

        s.DefaultFormat = ViewModel.DefaultFormat;
        s.ZipEncoding = FormatOptionsPanel.FileNameEncoding ?? "utf-8";
        s.ZipCompressionMethod = FormatOptionsPanel.ZipCompressionMethod ?? "deflate";
        s.SevenZipCompressionMethod = FormatOptionsPanel.SevenZipCompressionMethod ?? "LZMA2";
        s.SevenZipSolid = FormatOptionsPanel.SevenZipSolid;
        s.SevenZipSolidBlockSize = FormatOptionsPanel.SevenZipSolidBlockSize ?? "";
        s.SevenZipDictionarySize = FormatOptionsPanel.SevenZipDictionarySize;
        s.SevenZipNumFastBytes = FormatOptionsPanel.SevenZipNumFastBytes;
        s.SevenZipMatchFinder = FormatOptionsPanel.SevenZipMatchFinder ?? "";
        s.ZipEncryptionMethod = ViewModel.ZipEncryptionMethod;
        s.SevenZipEncryptHeaders = ViewModel.SevenZipEncryptHeaders;
        s.SplitSizeTag = ViewModel.SelectedSplitSizeOption?.Tag ?? "0";
        s.CustomSplitSizeMB = ViewModel.CustomSplitSizeText;

        s.Save();
    }

    /// <summary>
    /// Subscribe to ViewModel PropertyChanged events to update UI elements
    /// that can't be easily bound in XAML.
    /// </summary>
    private void SubscribeViewModel()
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.Password):
            case nameof(ViewModel.ConfirmPassword):
                UpdatePasswordMatchIndicator();
                break;
            case nameof(ViewModel.IsPasswordLibraryMode):
                UpdateSaveCheckLabel();
                break;
        }
    }

    /// <summary>
    /// Update the password match indicator text and visibility.
    /// </summary>
    private void UpdatePasswordMatchIndicator()
    {
        if (PasswordMatchIndicator == null) return;

        if (!string.IsNullOrEmpty(ViewModel.ConfirmPassword))
        {
            if (ViewModel.PasswordsMatch)
            {
                PasswordMatchIndicator.Text = LocalizationManager.T("Compress_Pwd_Match");
                PasswordMatchIndicator.Foreground = new SolidColorBrush(Color.Parse("#4CAF50")); // Green
                PasswordMatchIndicator.IsVisible = true;
            }
            else
            {
                PasswordMatchIndicator.Text = LocalizationManager.T("Compress_Pwd_NoMatch");
                PasswordMatchIndicator.Foreground = new SolidColorBrush(Color.Parse("#F44336")); // Red
                PasswordMatchIndicator.IsVisible = true;
            }
        }
        else
        {
            PasswordMatchIndicator.IsVisible = false;
        }
    }

    /// <summary>
    /// Update the save-to-library checkbox label based on current mode.
    /// </summary>
    private void UpdateSaveCheckLabel()
    {
        // The SaveToLibrary checkbox text is bound to localized strings in XAML,
        // but the label changes based on mode (library vs new password).
        // We handle the dynamic label update here since Avalonia's binding
        // can't easily change between two different string keys.
    }

    /// <summary>
    /// 切换密码显示/隐藏。
    /// </summary>
    private void TogglePasswordReveal(object? sender, RoutedEventArgs e)
    {
        if (PasswordTextBox.PasswordChar == '●')
        {
            PasswordTextBox.PasswordChar = default;
            RevealButton.Content = LocalizationManager.T("Compress_ShowPassword") == "Show" ? "Hide" : "隐藏";
        }
        else
        {
            PasswordTextBox.PasswordChar = '●';
            RevealButton.Content = LocalizationManager.T("Compress_ShowPassword");
        }
    }
}
